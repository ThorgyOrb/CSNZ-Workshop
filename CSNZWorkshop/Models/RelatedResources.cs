using CSNZWorkshop.Archives;

namespace CSNZWorkshop.Models;

public sealed record RelatedResource(string Kind, string Path, ResourceEntry? Entry, string Reason, bool Reference);
public sealed record RelatedResult(IReadOnlyList<RelatedResource> Items, bool SuggestionsTruncated);

public static class RelatedResources
{
    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
    static string Stem(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(Normalize(path));
        return stem.StartsWith("v_") || stem.StartsWith("p_") || stem.StartsWith("w_") ? stem[2..] : stem;
    }

    public static RelatedResult Find(ResourceEntry source, ModelBundle bundle, IReadOnlyList<ResourceEntry> library, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var items = new List<RelatedResource>();
        var byPath = library.ToLookup(e => Normalize(e.Ruta));
        var bySound = library.Where(e => e.Extension is ".wav" or ".mp3").ToLookup(e => SoundPath(e.Ruta));
        var referenced = new HashSet<ResourceEntry>();
        string main = Normalize(source.Ruta), folder = main[..(main.LastIndexOf('/') + 1)];
        void Reference(string kind, string path, IEnumerable<ResourceEntry> matches, string reason, bool preferPackage)
        {
            token.ThrowIfCancellationRequested();
            var candidates = matches.ToArray();
            var local = candidates.Where(c => c.Paquete.Equals(source.Paquete, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (preferPackage && local.Length > 0) candidates = local;
            if (candidates.Length == 0) items.Add(new(kind, path, null, reason + ". No encontrado en la biblioteca abierta.", true));
            foreach (var entry in candidates)
            {
                referenced.Add(entry);
                items.Add(new(kind, path, entry, reason + (candidates.Length > 1 ? ". Varias coincidencias: elige la versión." : ". Coincidencia de ruta."), true));
            }
        }
        foreach (string name in bundle.Companions.Select(c => c.Name).Concat(bundle.Missing).Distinct(StringComparer.OrdinalIgnoreCase))
            Reference("Compañero MDL", folder + name, byPath[Normalize(folder + name)], "Dependencia del modelo según convención de nombres GoldSrc", true);
        foreach (string sound in bundle.Model.Events.Where(e => e.Code == 5004).Select(e => e.Options.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = SoundPath(sound);
            Reference("Sonido en evento 5004", sound, bySound[path], "Ruta declarada en un evento de sonido del MDL", false);
        }
        string stem = Stem(source.Ruta);
        int suggestions = 0; bool truncated = false;
        foreach (var entry in library)
        {
            token.ThrowIfCancellationRequested();
            if (entry == source || referenced.Contains(entry) || entry.Extension is not (".mdl" or ".wav" or ".mp3" or ".spr") || stem.Length < 3) continue;
            string candidate = Stem(entry.Ruta);
            if (candidate != stem && !candidate.StartsWith(stem + "_") && !candidate.StartsWith(stem + "-")
                && !Normalize(entry.Ruta).Split('/').Contains(stem)) continue;
            if (suggestions++ >= 200) { truncated = true; continue; }
            items.Add(new("Sugerencia por nombre", entry.Ruta, entry, "Nombre o carpeta similar; relación no confirmada", false));
        }
        return new(items, truncated);
    }

    static string SoundPath(string path)
    {
        string normalized = Normalize(path);
        int at = normalized.IndexOf("/sound/", StringComparison.Ordinal);
        if (at >= 0) return normalized[(at + 7)..];
        return normalized.StartsWith("sound/") ? normalized[6..] : normalized;
    }
}
