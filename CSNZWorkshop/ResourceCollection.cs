using System.Text.Json;
using CSNZWorkshop.Archives;

namespace CSNZWorkshop;

public sealed record CollectionReference(string Package, string Resource);
public sealed record CollectionDocument(int Version, string Name, List<CollectionReference> Resources);
public sealed record CollectionItem(CollectionReference Reference, ResourceEntry? Entry, string Status);

public sealed class ResourceCollection
{
    const int MaxItems = 10000;
    readonly List<CollectionReference> resources = [];
    public string Name { get; private set; } = "Mi colección";
    public bool Dirty { get; private set; }
    public IReadOnlyList<CollectionReference> Resources => resources;
    static string Key(string package, string resource) => Path.GetFullPath(package).ToUpperInvariant() + "\0" + resource.Replace('\\', '/').TrimStart('/').ToUpperInvariant();

    public int Add(IEnumerable<ResourceEntry> entries)
    {
        var existing = resources.Select(r => Key(r.Package, r.Resource)).ToHashSet();
        var additions = new List<CollectionReference>();
        foreach (var entry in entries)
            if (existing.Add(Key(entry.Paquete, entry.Ruta))) additions.Add(new(Path.GetFullPath(entry.Paquete), entry.Ruta));
        if (resources.Count + additions.Count > MaxItems) throw new InvalidDataException("Una colección admite hasta 10,000 recursos.");
        resources.AddRange(additions); Dirty |= additions.Count > 0; return additions.Count;
    }

    public void Remove(IEnumerable<CollectionReference> items)
    {
        var removed = items.ToHashSet();
        Dirty |= resources.RemoveAll(removed.Contains) > 0;
    }

    public void Rename(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 120) throw new InvalidDataException("El nombre debe tener entre 1 y 120 caracteres.");
        Dirty |= Name != name; Name = name;
    }

    public void Clear() { resources.Clear(); Name = "Mi colección"; Dirty = false; }
    public void Move(CollectionReference item, int direction)
    {
        int at = resources.IndexOf(item), target = at + Math.Sign(direction);
        if (at < 0 || target < 0 || target >= resources.Count) return;
        (resources[at], resources[target]) = (resources[target], resources[at]); Dirty = true;
    }
    public void RelocatePackage(string oldPath, string newPath, IReadOnlyList<ResourceEntry> current)
    {
        string replacement = Path.GetFullPath(newPath);
        var affected = resources.Where(r => r.Package.Equals(oldPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var item in affected)
            if (current.Count(e => e.Paquete.Equals(replacement, StringComparison.OrdinalIgnoreCase) && e.Ruta.Replace('\\', '/').Equals(item.Resource.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) != 1)
                throw new InvalidDataException("El paquete elegido no contiene todas las referencias de forma única: " + item.Resource);
        var changed = resources.Select(r => r.Package.Equals(oldPath, StringComparison.OrdinalIgnoreCase) ? r with { Package = replacement } : r).ToList();
        if (changed.Select(r => Key(r.Package, r.Resource)).Distinct().Count() != changed.Count) throw new InvalidDataException("La reubicación produciría referencias duplicadas.");
        resources.Clear(); resources.AddRange(changed); Dirty |= affected.Length > 0;
    }

    public IReadOnlyList<CollectionItem> Resolve(IReadOnlyList<ResourceEntry> library)
    {
        var lookup = library.ToLookup(r => Key(r.Paquete, r.Ruta));
        return resources.Select(reference =>
        {
            var candidates = lookup[Key(reference.Package, reference.Resource)].Take(2).ToArray();
            return new CollectionItem(reference, candidates.Length == 1 ? candidates[0] : null,
                candidates.Length == 1 ? "Disponible" : candidates.Length == 0 ? "No está en esta biblioteca" : "Ruta ambigua en el paquete");
        }).ToArray();
    }

    public void Save(string filename)
    {
        string full = Path.GetFullPath(filename);
        if (!full.EndsWith(".csnzcollection", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Usa la extensión .csnzcollection.");
        SafeOutput.CheckDestination(full, resources.Select(r => r.Package));
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new CollectionDocument(1, Name, resources), new JsonSerializerOptions { WriteIndented = true });
                if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("Colección demasiado grande (8 MB).");
                stream.Flush(true);
            }
            SafeOutput.RejectLinks(full); File.Move(temporary, full, true); Dirty = false;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Load(string filename)
    {
        using var stream = File.OpenRead(filename);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("Colección demasiado grande (8 MB).");
        var document = JsonSerializer.Deserialize<CollectionDocument>(stream);
        if (document is null || document.Version != 1 || string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 120
            || document.Resources is null || document.Resources.Count > MaxItems) throw new InvalidDataException("Colección inválida o versión no admitida.");
        var keys = new HashSet<string>();
        foreach (var resource in document.Resources)
        {
            if (resource is null || string.IsNullOrWhiteSpace(resource.Package) || !Path.IsPathFullyQualified(resource.Package)
                || string.IsNullOrWhiteSpace(resource.Resource) || resource.Resource.Length > 16384
                || !keys.Add(Key(resource.Package, resource.Resource))) throw new InvalidDataException("Referencia de colección inválida o duplicada.");
        }
        // Commit only after the entire document has been validated. Offsets are never loaded from JSON.
        resources.Clear(); resources.AddRange(document.Resources); Name = document.Name; Dirty = false;
    }
}
