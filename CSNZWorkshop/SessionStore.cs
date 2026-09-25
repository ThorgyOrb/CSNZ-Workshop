using System.Text.Json;
using CSNZWorkshop.Archives;

namespace CSNZWorkshop;

public sealed class WorkshopSession
{
    public string GameFolder { get; set; } = Inventory.DefaultPath;
    public string Destination { get; set; } = "";
    public string Search { get; set; } = "";
    public int Category { get; set; }
    public bool Gallery { get; set; } = true;
    public bool Dark { get; set; } = true;
    public List<string> RecentSearches { get; set; } = [];
    public List<string> Favorites { get; set; } = [];
}

public sealed class SessionStore
{
    public string Root { get; }
    public WorkshopSession State { get; private set; } = new();
    public string? Warning { get; private set; }
    public SessionStore(string root)
    {
        Root = Path.GetFullPath(root);
        try
        {
            string path = Path.Combine(Root, "session.json");
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Sesión demasiado grande.");
                State = JsonSerializer.Deserialize<WorkshopSession>(File.ReadAllText(path)) ?? new();
                State.RecentSearches ??= []; State.Favorites ??= [];
                State.GameFolder ??= Inventory.DefaultPath; State.Search ??= ""; State.Destination ??= "";
                State.Category = Math.Clamp(State.Category, 0, 5);
                State.RecentSearches = State.RecentSearches.Where(s => !string.IsNullOrWhiteSpace(s)).Take(12).ToList();
                State.Favorites = State.Favorites.Where(s => s is not null).Distinct(StringComparer.OrdinalIgnoreCase).Take(10000).ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { Warning = "No se pudo recuperar la sesión: " + ex.Message; State = new(); }
    }
    public void RememberSearch(string value)
    {
        value = value.Trim(); if (value.Length == 0) return;
        State.RecentSearches.RemoveAll(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
        State.RecentSearches.Insert(0, value); State.RecentSearches = State.RecentSearches.Take(12).ToList();
    }
    public static string ResourceKey(ResourceEntry entry) => Path.GetFullPath(entry.Paquete).ToUpperInvariant() + "|" + entry.Ruta.Replace('\\', '/').TrimStart('/').ToUpperInvariant();
    public bool Save()
    {
        try { WriteJson(Path.Combine(Root, "session.json"), State); Warning = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Warning = "No se pudo guardar la sesión: " + ex.Message; return false; }
    }
    internal static void WriteJson<T>(string path, T value)
    {
        SafeOutput.CreateDirectory(Path.GetDirectoryName(path)!); SafeOutput.RejectLinks(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(file, value);
            SafeOutput.RejectLinks(path); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
