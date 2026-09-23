using System.Security.Cryptography;
using System.Text;

namespace CSNZWorkshop.Archives;

public static class SafeOutput
{
    public static string ResourcePath(string root, ResourceEntry entry)
    {
        root = Path.GetFullPath(root);
        string name = entry.Ruta.Replace('\\', '/');
        // NAR uses a single leading slash as a virtual archive root, not a disk root.
        if (entry.Formato == "NAR" && name.StartsWith('/') && !name.StartsWith("//")) name = name[1..];
        var parts = name.Split('/');
        foreach (string part in parts) ValidateComponent(part);
        string package = Path.GetFileName(entry.Paquete);
        ValidateComponent(package);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(entry.Paquete).ToUpperInvariant())))[..8];
        string result = Path.GetFullPath(Path.Combine([root, package + "_" + hash, .. parts]));
        if (!IsInside(result, root)) throw new InvalidDataException("La ruta del recurso sale de la carpeta de destino.");
        return result;
    }

    static void ValidateComponent(string part)
    {
        if (string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
            || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Any(char.IsControl))
            throw new InvalidDataException("Ruta de recurso no segura o no válida en Windows.");
        string stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3])))
            throw new InvalidDataException("La ruta contiene un nombre reservado de Windows.");
    }

    public static bool IsInside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    public static void CheckDestination(string root, IEnumerable<string> sources)
    {
        root = Path.GetFullPath(root);
        RejectLinks(root);
        foreach (string source in sources)
        {
            // Protect the installation when sources are in CSNZ/Data or Data/Packer.
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(source))!); directory is not null; directory = directory.Parent)
            {
                if (!directory.Name.Equals("Data", StringComparison.OrdinalIgnoreCase) || directory.Parent is null) continue;
                string installation = directory.Parent.FullName;
                if (root.Equals(installation, StringComparison.OrdinalIgnoreCase) || IsInside(root, installation))
                    throw new InvalidDataException("Elige una carpeta fuera de la instalación del juego.");
                break;
            }
        }
    }

    public static void RejectLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("El destino contiene un enlace o una unión de carpetas; elige una carpeta normal.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    public static void CreateDirectory(string path)
    {
        RejectLinks(path);
        Directory.CreateDirectory(path);
        RejectLinks(path);
    }
}
