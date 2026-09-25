namespace CSNZWorkshop.Archives;

public static class ModExport
{
    public static string Relative(ResourceEntry entry)
    {
        string[] parts = entry.Ruta.Replace('\\', '/').TrimStart('/').Split('/');
        if (parts.Any(p => p is "." or ".." || string.IsNullOrWhiteSpace(p))) throw new InvalidDataException("Ruta interna no válida.");
        int start = Array.FindIndex(parts, p => new[] { "models", "sound", "sprites", "maps", "gfx" }.Contains(p.ToLowerInvariant()));
        if (start >= 0 && start < parts.Length - 1) return string.Join('/', parts[start..]);
        if (entry.Extension == ".wad") return parts[^1];
        throw new NotSupportedException("Sin carpeta de mod reconocida; utiliza la extracción original.");
    }
    public static string Target(string root, ResourceEntry entry)
    {
        string relative = Relative(entry);
        _ = SafeOutput.ResourcePath(root, entry with { Ruta = relative, Formato = "PAK" });
        string result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!SafeOutput.IsInside(result, root)) throw new InvalidDataException("Destino fuera de la carpeta del mod.");
        return result;
    }
}
