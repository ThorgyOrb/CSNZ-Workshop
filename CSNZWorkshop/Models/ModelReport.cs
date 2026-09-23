using CSNZWorkshop.Archives;
using System.Text.Json;

namespace CSNZWorkshop.Models;

public record ModelIssue(string Paquete, string Ruta, string Detalle);
public record ModelSummary(int Revisados, int Leidos, int PaquetesLeidos, Dictionary<string, int> Versiones,
    List<ModelIssue> Problemas, List<ModelIssue> Dependencias, List<ModelIssue> Paquetes);

// Batch diagnosis: reads every .mdl of each package with the same code path as the viewer, opening each
// package once. It reports what the viewer can read; it does not write or convert anything.
public static class ModelReport
{
    public static ModelSummary Build(IEnumerable<string> packages, CancellationToken token = default,
        IProgress<string>? progress = null)
    {
        int reviewed = 0, read = 0, packagesRead = 0;
        var versions = new Dictionary<string, int>();
        List<ModelIssue> problems = [], dependencies = [], packageErrors = [];
        foreach (string package in packages)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(Path.GetFileName(package));
            IReadOnlyList<ResourceEntry> entries;
            try { entries = ArchiveIndex.Read(package, token); packagesRead++; }
            catch (Exception ex) when (Expected(ex)) { packageErrors.Add(new(package, "", ex.Message)); continue; }
            var models = entries.Where(entry => entry.Extension == ".mdl").ToArray();
            if (models.Length == 0) continue;
            var byPath = entries.GroupBy(Normalize, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            using var source = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] Read(ResourceEntry entry)
            {
                if (entry.Bytes < 0 || entry.Bytes > 128 * 1024 * 1024)
                    throw new InvalidDataException("El informe admite modelos de hasta 128 MiB.");
                byte[] bytes = new byte[(int)entry.Bytes];
                using var output = new MemoryStream(bytes, true);
                ResourceDecoder.Decode(source, entry, output, token);
                if (output.Position != bytes.Length) throw new InvalidDataException("Recurso incompleto.");
                return bytes;
            }
            foreach (var entry in models)
            {
                token.ThrowIfCancellationRequested();
                reviewed++;
                string path = Normalize(entry);
                string folder = path[..(path.LastIndexOf('/') + 1)];
                try
                {
                    var bundle = ModelBundle.Load(Read(entry), Path.GetFileName(path), package + " :: " + entry.Ruta,
                        name => byPath.TryGetValue(folder + name, out var sibling) ? Read(sibling) : null);
                    read++;
                    string version = "v" + bundle.Main.OriginalVersion;
                    versions[version] = versions.GetValueOrDefault(version) + 1;
                    foreach (string missing in bundle.Missing) dependencies.Add(new(package, entry.Ruta, "Falta " + missing));
                }
                catch (Exception ex) when (Expected(ex)) { problems.Add(new(package, entry.Ruta, ex.Message)); }
            }
        }
        return new(reviewed, read, packagesRead, versions, problems, dependencies, packageErrors);
    }

    public static void Export(string path, ModelSummary summary) => File.WriteAllText(path,
        JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

    static string Normalize(ResourceEntry entry) => entry.Ruta.Replace('\\', '/').TrimStart('/');

    static bool Expected(Exception ex) => ex is IOException or InvalidDataException or NotSupportedException
        or UnauthorizedAccessException or OverflowException or System.Text.DecoderFallbackException
        or ICSharpCode.SharpZipLib.SharpZipBaseException;
}
