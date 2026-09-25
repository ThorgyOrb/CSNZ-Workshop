using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSNZWorkshop.Archives;

public sealed record CachedEntry(ResourceEntry Entry, uint? Crc, byte[]? Key);
public sealed record CachedPackage(int Version, string Path, long Length, long Modified, long Created, List<CachedEntry> Entries);
public sealed record CachedIndexResult(ResourceIndex Index, int Reused, int Updated, string? Warning);

public static class CachedIndex
{
    public static CachedIndexResult Build(IEnumerable<string> paths, string directory, bool refresh = false,
        CancellationToken token = default, IProgress<string>? progress = null)
    {
        var entries = new List<ResourceEntry>(); var errors = new List<IndexError>();
        int reused = 0, updated = 0; string? warning = null;
        foreach (string source in paths)
        {
            token.ThrowIfCancellationRequested(); string path = Path.GetFullPath(source);
            progress?.Report(Path.GetFileName(path));
            try
            {
                var info = new FileInfo(path);
                long length = info.Length, modified = info.LastWriteTimeUtc.Ticks, created = info.CreationTimeUtc.Ticks;
                string cache = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))) + ".json");
                if (!refresh && TryRead(cache, path, length, modified, created) is { } saved)
                { entries.AddRange(saved); reused++; continue; }
                var current = ArchiveIndex.Read(path, token); token.ThrowIfCancellationRequested();
                entries.AddRange(current); updated++;
                info.Refresh();
                if (info.Length == length && info.LastWriteTimeUtc.Ticks == modified && info.CreationTimeUtc.Ticks == created)
                {
                    try { SessionStore.WriteJson(cache, new CachedPackage(1, path, length, modified, created, current.Select(e => new CachedEntry(e, e.StoredCrc, e.DataKey)).ToList())); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { warning = "No se pudo guardar parte de la caché: " + ex.Message; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ICSharpCode.SharpZipLib.SharpZipBaseException)
            { errors.Add(new(path, ex.Message)); }
        }
        return new(new(entries, errors, reused + updated), reused, updated, warning);
    }

    static List<ResourceEntry>? TryRead(string cache, string path, long length, long modified, long created)
    {
        try
        {
            if (!File.Exists(cache) || new FileInfo(cache).Length > 64 * 1024 * 1024) return null;
            using var file = File.OpenRead(cache); var saved = JsonSerializer.Deserialize<CachedPackage>(file);
            if (saved is null || saved.Version != 1 || saved.Path != path || saved.Length != length || saved.Modified != modified || saved.Created != created || saved.Entries is null) return null;
            if (saved.Entries.Any(c => c?.Entry is not { } e || e.Paquete != path || string.IsNullOrEmpty(e.Ruta)
                || e.Offset < 0 || e.Bytes < 0 || e.BytesGuardados < 0 || e.Offset > length || e.BytesGuardados > length - e.Offset)) return null;
            return saved.Entries.Select(c => c.Entry with { StoredCrc = c.Crc, DataKey = c.Key }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return null; }
    }
}
