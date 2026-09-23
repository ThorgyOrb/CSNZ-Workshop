using System.Text.Json;

namespace CSNZWorkshop;

public record Package(string Nombre, string Formato, long Bytes, string Cabecera, string Ruta, string Estado);
public record ScanResult(List<Package> Packages, List<string> Errors);

public static class Inventory
{
    public const string DefaultPath = @"C:\Program Files (x86)\Steam\steamapps\common\CSNZ\Data";

    public static ScanResult Scan(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("La carpeta no existe: " + root);
        var packages = new List<Package>();
        var errors = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var directory))
        {
            try
            {
                foreach (var child in Directory.GetDirectories(directory))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                foreach (var path in Directory.GetFiles(directory))
                {
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is not (".nar" or ".pak")) continue;
                    try
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var header = new byte[32];
                        var read = stream.Read(header);
                        packages.Add(new(Path.GetFileName(path), extension[1..].ToUpperInvariant(), stream.Length,
                            Convert.ToHexString(header.AsSpan(0, read)), path,
                            "Detectado por extensión; usa Ver recursos para validar el índice"));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { errors.Add(path + ": " + ex.Message); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { errors.Add(directory + ": " + ex.Message); }
        }
        return new(packages.OrderBy(p => p.Ruta, StringComparer.OrdinalIgnoreCase).ToList(), errors);
    }

    public static void Export(string path, ScanResult result) => File.WriteAllText(path,
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}
