using CSNZWorkshop.Archives;

namespace CSNZWorkshop;

public static class ResourceSearch
{
    public static int Rank(ResourceEntry entry, string query)
    {
        if (query.Length == 0) return 0;
        string name = Path.GetFileName(entry.Ruta);
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(name).Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        if (entry.Ruta.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
        return entry.Paquete.Contains(query, StringComparison.OrdinalIgnoreCase) ? 4 : int.MaxValue;
    }
}
