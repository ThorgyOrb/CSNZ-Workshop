using System.Security.Cryptography;
using System.Text.Json;

namespace CSNZWorkshop.Archives;

public record ExtractionItem(string Recurso, string Paquete, string? Destino, string Estado, string Detalle,
    long Bytes = 0, string? Sha256 = null);
public sealed record ExtractionReport(int Solicitados, List<ExtractionItem> Archivos)
{
    public bool Cancelada { get; set; }
    public int Extraidos => Archivos.Count(a => a.Estado == "Extraído");
    public int Omitidos => Archivos.Count(a => a.Estado == "Omitido");
    public int Errores => Archivos.Count(a => a.Estado == "Error");
    public int Pendientes => Solicitados - Archivos.Count;
}
public record ExtractionProgress(int Procesados, int Total, string Recurso);

public static class Extraction
{
    public static byte[] ReadResource(ResourceEntry requested, CancellationToken token = default)
    {
        if (requested.Bytes < 0 || requested.Bytes > 128 * 1024 * 1024)
            throw new InvalidDataException("La vista previa admite recursos de hasta 128 MiB.");
        using var source = new FileStream(requested.Paquete, FileMode.Open, FileAccess.Read, FileShare.Read);
        var entry = ArchiveIndex.Read(requested.Paquete, token).FirstOrDefault(r => r.Ruta == requested.Ruta && r.Offset == requested.Offset && SameEntry(r, requested))
            ?? throw new InvalidDataException("El índice cambió; vuelve a abrir la biblioteca.");
        byte[] bytes = new byte[(int)entry.Bytes];
        using var output = new MemoryStream(bytes, true);
        ResourceDecoder.Decode(source, entry, output, token);
        if (output.Position != bytes.Length) throw new InvalidDataException("Recurso incompleto.");
        return bytes;
    }
    public static ExtractionReport Run(IReadOnlyList<ResourceEntry> selected, string destination,
        CancellationToken token = default, IProgress<ExtractionProgress>? progress = null, bool organizeForMod = false)
    {
        string root = Path.GetFullPath(destination);
        SafeOutput.CheckDestination(root, selected.Select(r => r.Paquete).Distinct(StringComparer.OrdinalIgnoreCase));
        if (organizeForMod && selected.Select(e => ModExport.Target(root, e)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Count)
            throw new InvalidDataException("Hay destinos repetidos. Elige una sola versión de cada recurso.");
        var report = new ExtractionReport(selected.Count, []);
        try
        {
            foreach (var group in selected.GroupBy(r => r.Paquete, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // Keep the source locked against writers while re-reading its index and extracting.
                    using var source = new FileStream(group.Key, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var current = ArchiveIndex.Read(group.Key, token).ToLookup(r => (r.Ruta, r.Offset));
                    foreach (var requested in group)
                    {
                        token.ThrowIfCancellationRequested();
                        progress?.Report(new(report.Archivos.Count, report.Solicitados, requested.Ruta));
                        try
                        {
                            var entry = current[(requested.Ruta, requested.Offset)].FirstOrDefault(r => SameEntry(r, requested))
                                ?? throw new InvalidDataException("El índice cambió desde el listado. Vuelve a abrir la biblioteca.");
                            report.Archivos.Add(ExtractOne(source, entry, root, token, organizeForMod));
                        }
                        catch (Exception ex) when (Expected(ex))
                        { report.Archivos.Add(new(requested.Ruta, requested.Paquete, null, "Error", ex.Message)); }
                    }
                }
                catch (Exception ex) when (Expected(ex))
                {
                    // Opening or indexing failed before this group's extraction loop began.
                    foreach (var entry in group) report.Archivos.Add(new(entry.Ruta, entry.Paquete, null, "Error", ex.Message));
                }
            }
        }
        catch (OperationCanceledException) { report.Cancelada = true; }
        progress?.Report(new(report.Archivos.Count, report.Solicitados, ""));
        return report;
    }

    static bool SameEntry(ResourceEntry a, ResourceEntry b) => a.Formato == b.Formato && a.Bytes == b.Bytes
        && a.BytesGuardados == b.BytesGuardados && a.Flags == b.Flags && a.StoredCrc == b.StoredCrc
        && (a.DataKey ?? []).AsSpan().SequenceEqual(b.DataKey ?? []);

    static bool Expected(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException
        or NotSupportedException or ArgumentException or ICSharpCode.SharpZipLib.SharpZipBaseException;

    static ExtractionItem ExtractOne(Stream source, ResourceEntry entry, string root, CancellationToken token, bool organizeForMod)
    {
        string target = organizeForMod ? ModExport.Target(root, entry) : SafeOutput.ResourcePath(root, entry);
        SafeOutput.RejectLinks(target);
        if (File.Exists(target) || Directory.Exists(target)) return new(entry.Ruta, entry.Paquete, target, "Omitido", "El destino ya existe; no se sobrescribió.");
        SafeOutput.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".csnz-" + Guid.NewGuid().ToString("N") + ".partial");
        bool created = false;
        try
        {
            string hash, diagnostic;
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536))
            {
                created = true;
                ResourceDecoder.Decode(source, entry, output, token);
                if (output.Length != entry.Bytes) throw new InvalidDataException("El tamaño extraído no coincide con el índice.");
                diagnostic = ResourceInspection.Inspect(output, entry.Extension);
                output.Position = 0;
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[65536];
                int count;
                while ((count = output.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); sha.AppendData(buffer, 0, count); }
                hash = Convert.ToHexString(sha.GetHashAndReset());
            }
            token.ThrowIfCancellationRequested();
            SafeOutput.RejectLinks(target);
            File.Move(temporary, target, false);
            created = false;
            string verification = entry.Formato == "NAR" ? "Tamaño y CRC almacenado verificados. " : "Tamaño verificado; PAK sin CRC de contenido implementado. ";
            return new(entry.Ruta, entry.Paquete, target, "Extraído", verification + diagnostic, entry.Bytes, hash);
        }
        finally
        {
            if (created)
            {
                SafeOutput.RejectLinks(temporary);
                File.Delete(temporary);
            }
        }
    }

    public static string SaveReport(string destination, ExtractionReport report)
    {
        SafeOutput.CreateDirectory(Path.GetFullPath(destination));
        string path = Path.Combine(destination, $"informe-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
        return path;
    }
}
