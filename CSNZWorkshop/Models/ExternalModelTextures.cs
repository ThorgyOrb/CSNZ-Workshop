using CSNZWorkshop.Archives;
using CSNZWorkshop.Media;

namespace CSNZWorkshop.Models;

public static class ExternalModelTextures
{
    static string Normalize(string value) => value.Replace('\\', '/').TrimStart('/');
    public static string Filename(string name)
    {
        // A texture reference is a filename, never a user-controlled path out of the package.
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Nombre de textura externa no válido: " + name);
        return name;
    }

    public static ResourceEntry[] Candidates(ResourceEntry source, string filename, IReadOnlyList<ResourceEntry> library)
    {
        filename = Filename(filename);
        string path = Normalize(source.Ruta), directory = path[..(path.LastIndexOf('/') + 1)];
        int models = path.IndexOf("/models/", StringComparison.OrdinalIgnoreCase);
        string root = models >= 0 ? path[..(models + 8)] : path.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? "models/" : directory;
        var matches = library.Where(e => e.Extension == ".bmp" && Path.GetFileName(Normalize(e.Ruta)).Equals(filename, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (string location in new[] { directory + "texture/" + filename, root + "texture/" + filename })
        {
            var scoped = matches.Where(e => Normalize(e.Ruta).Equals(location, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (scoped.Length > 0) return PreferPackage(scoped, source.Paquete);
        }
        // Shared textures (notably cstrike hands) may live in another root/package.
        var shared = matches.Where(e => Normalize(e.Ruta).Contains("/models/texture/", StringComparison.OrdinalIgnoreCase)
            || Normalize(e.Ruta).StartsWith("models/texture/", StringComparison.OrdinalIgnoreCase)).ToArray();
        return PreferPackage(shared, source.Paquete);
    }
    static ResourceEntry[] PreferPackage(ResourceEntry[] entries, string package)
    {
        var local = entries.Where(e => e.Paquete.Equals(package, StringComparison.OrdinalIgnoreCase)).ToArray();
        return local.Length > 0 ? local : entries;
    }
    public static void Resolve(ModelBundle bundle, Func<string, ModelAsset?> resolve, CancellationToken token = default)
    {
        var cache = new Dictionary<(string Name, int Flags), StudioTexture>();
        long pixels = bundle.Model.Textures.Sum(t => (long)t.Pixels.Length);
        for (int id = 0; id < bundle.Model.Textures.Length; id++)
        {
            token.ThrowIfCancellationRequested(); var original = bundle.Model.Textures[id];
            if (!original.ExternalPlaceholder) continue;
            string name = Filename(original.Name);
            var key = (name.ToUpperInvariant(), original.Flags);
            if (!cache.TryGetValue(key, out var loaded))
            {
                ModelAsset? asset = bundle.ExternalTextures.FirstOrDefault(a => a.Name.Equals("texture/" + name, StringComparison.OrdinalIgnoreCase)) ?? resolve(name);
                if (asset is null)
                {
                    bundle.Missing.Add("texture/" + name);
                    bundle.Model.Warnings.Add("No se pudo resolver la textura externa (ausente o ambigua): " + name);
                    continue;
                }
                loaded = TextureCodec.DecodeModelBitmap(asset.Data, name, original.Flags) with { Source = asset.Origin };
                cache.Add(key, loaded);
                if (!bundle.ExternalTextures.Any(a => a.Name.Equals("texture/" + name, StringComparison.OrdinalIgnoreCase)))
                    bundle.ExternalTextures.Add(asset with { Name = "texture/" + name });
            }
            pixels += loaded.Pixels.Length - original.Pixels.Length;
            if (pixels > 32 * 1024 * 1024) throw new InvalidDataException("Las texturas externas superan el presupuesto de 128 MiB RGBA.");
            bundle.Model.Textures[id] = loaded with { Name = original.Name, Flags = original.Flags };
        }
    }
}
