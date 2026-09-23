using CSNZWorkshop.Archives;
using System.Security.Cryptography;
using System.Text.Json;

namespace CSNZWorkshop.Models;

public record ModelAsset(string Name, byte[] Data, int OriginalVersion, string Origin);

public sealed class ModelBundle
{
    public required StudioModel Model { get; init; }
    public required ModelAsset Main { get; init; }
    public List<ModelAsset> Companions { get; } = [];
    public List<string> Missing { get; } = [];

    public static ModelBundle FromArchive(ResourceEntry entry, IReadOnlyList<ResourceEntry> library, CancellationToken token = default)
    {
        byte[] raw = Extraction.ReadResource(entry, token);
        string path = entry.Ruta.Replace('\\', '/');
        byte[]? Resolve(string filename)
        {
            token.ThrowIfCancellationRequested();
            string sibling = path[..(path.LastIndexOf('/') + 1)] + filename;
            var candidates = library.Where(r => r.Ruta.Replace('\\', '/').TrimStart('/').Equals(sibling.TrimStart('/'), StringComparison.OrdinalIgnoreCase)).ToArray();
            var preferred = candidates.Where(r => r.Paquete.Equals(entry.Paquete, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (preferred.Length == 1) return Extraction.ReadResource(preferred[0], token);
            return candidates.Length == 1 ? Extraction.ReadResource(candidates[0], token) : null;
        }
        return Load(raw, Path.GetFileName(path), entry.Paquete + " :: " + entry.Ruta, Resolve);
    }

    public static ModelBundle FromFile(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        static byte[] Read(string path)
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 128 * 1024 * 1024) throw new InvalidDataException("Modelo demasiado grande (máximo 128 MiB).");
            byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
        }
        return Load(Read(path), Path.GetFileName(path), Path.GetFullPath(path), filename =>
        {
            string sibling = Path.Combine(directory, filename);
            return File.Exists(sibling) ? Read(sibling) : null;
        });
    }

    public static ModelBundle Load(byte[] raw, string filename, string origin, Func<string, byte[]?> resolve)
    {
        var header = new ModelBytes(raw); header.Range(0, 244);
        int version = header.Int(4);
        byte[] data = ModelCrypt.Decode(raw);
        StudioModel? texture = null;
        ModelAsset? textureAsset = null;
        string textureName = Path.GetFileNameWithoutExtension(filename) + "T.mdl";
        if (header.Int(180) == 0 && header.Int(204) > 0)
        {
            var bytes = resolve(textureName);
            if (bytes is not null)
            {
                byte[] decoded = ModelCrypt.Decode(bytes); texture = StudioModel.Parse(decoded);
                textureAsset = new(textureName, decoded, new ModelBytes(bytes).Int(4), "Compañero de " + origin);
            }
        }
        var model = StudioModel.Parse(data, texture);
        var result = new ModelBundle { Model = model, Main = new(filename, data, version, origin) };
        if (textureAsset is not null) result.Companions.Add(textureAsset);
        else if (model.Textures.Length == 0 && model.Bodies.Length > 0) result.Missing.Add(textureName);
        foreach (int id in model.Sequences.Select(s => s.Group).Where(g => g > 0).Distinct())
        {
            // GoldSrc convention: model01.mdl, model02.mdl ... in the model's directory.
            string name = Path.GetFileNameWithoutExtension(filename) + id.ToString("00") + ".mdl";
            byte[]? group = resolve(name);
            if (group is null) { result.Missing.Add(name); continue; }
            var r = new ModelBytes(group); r.Range(0, 76);
            if (r.Int(0) != 0x51534449 || r.Int(4) != 10 || r.Int(72) != group.Length)
                throw new InvalidDataException("Grupo de animación externo inválido: " + name);
            model.ExternalGroups[id] = group;
            result.Companions.Add(new(name, group, 10, "Compañero de " + origin));
        }
        return result;
    }

    public string Describe()
    {
        var r = new ModelBytes(Main.Data);
        string[] lines = [
            $"{Main.Name} · MDL original v{Main.OriginalVersion}" + (Main.OriginalVersion is 20 or 21 ? " → descifrado en memoria a v10" : ""),
            $"{Model.Bones.Length} huesos · {Model.Textures.Length} texturas · {Model.Bodies.Length} grupos de cuerpo · {Model.Sequences.Length} animaciones",
            $"{r.Int(156)} hitboxes · {r.Int(212)} attachments · {Companions.Count} archivos compañeros encontrados",
            "El visor aproxima materiales e iluminación. La preparación no garantiza funcionamiento en CS 1.6.",
            .. Model.Warnings,
            .. Missing.Select(m => "Falta dependencia: " + m),
            "Comprueba animaciones de disparo/recarga, sonidos y límites del cliente antes de instalar."];
        return string.Join(Environment.NewLine, lines);
    }

    public string Prepare(string destination, CancellationToken token = default)
    {
        if (Missing.Count > 0) throw new InvalidDataException("Faltan archivos compañeros: " + string.Join(", ", Missing));
        // Validate every first-blend animation frame before calling this a prepared candidate.
        long work = Model.Sequences.Sum(s => (long)s.Frames * Model.Bones.Length);
        if (work > 5000000) throw new InvalidDataException("Demasiados fotogramas para la validación automática; requiere revisión manual.");
        for (int s = 0; s < Model.Sequences.Length; s++)
            for (int frame = 0; frame < Model.Sequences[s].Frames; frame++) { token.ThrowIfCancellationRequested(); _ = Model.Pose(s, frame); }
        SafeOutput.CheckDestination(destination, [Main.Origin.Split(" :: ")[0]]);
        SafeOutput.CreateDirectory(destination);
        string folder = Path.Combine(Path.GetFullPath(destination), "modelo-" + Guid.NewGuid().ToString("N"));
        SafeOutput.CreateDirectory(folder);
        var written = new List<string>();
        try
        {
            foreach (var asset in new[] { Main }.Concat(Companions))
            {
                token.ThrowIfCancellationRequested();
                // Reuse path validation; then put files together to preserve companion lookup.
                _ = SafeOutput.ResourcePath(folder, new(asset.Name, "prepared.mdl", "PAK", 0, 0, 0, 0));
                string target = Path.Combine(folder, asset.Name);
                using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(asset.Data);
                written.Add(target);
            }
            string report = Path.Combine(folder, "preparacion.json");
            using (var file = new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(file, new
                {
                    Estado = "Candidato para prueba; no instalado ni probado en CS 1.6",
                    Origen = Main.Origin, VersionOriginal = Main.OriginalVersion, VersionSalida = 10,
                    Diagnostico = Describe(),
                    Animaciones = Model.Sequences.Select(s => new { s.Name, s.Frames, s.Fps, s.Blends }),
                    Archivos = new[] { Main }.Concat(Companions).Select(a => new { a.Name, Bytes = a.Data.Length, SHA256 = Convert.ToHexString(SHA256.HashData(a.Data)) }),
                    Pendiente = "No se reordenan secuencias, no se reducen polígonos ni se incluyen sonidos externos automáticamente. Los blends adicionales no se validan."
                }, new JsonSerializerOptions { WriteIndented = true });
            written.Add(report);
            return folder;
        }
        catch
        {
            foreach (string file in written) { SafeOutput.RejectLinks(file); File.Delete(file); }
            throw;
        }
    }
}
