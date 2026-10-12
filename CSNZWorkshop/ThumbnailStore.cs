using System.Security.Cryptography;
using System.Text;
using CSNZWorkshop.Archives;

namespace CSNZWorkshop;

public sealed class ThumbnailStore
{
    readonly string root, generation;
    public ThumbnailStore(string root, IEnumerable<ResourceEntry> library)
    {
        this.root = Path.GetFullPath(root);
        generation = Hash(string.Join("\n", library.Select(e => e.Paquete).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p =>
            {
                var info = new FileInfo(p);
                return info.Exists ? $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{info.CreationTimeUtc.Ticks}" : p + "|missing";
            })));
    }
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    string PathFor(ResourceEntry entry) => Path.Combine(root, Hash($"v3|{generation}|{entry.Paquete}|{entry.Ruta}|{entry.Offset}|{entry.Bytes}") + ".png");
    public Bitmap? Read(ResourceEntry entry)
    {
        try
        {
            string path = PathFor(entry); SafeOutput.RejectLinks(path);
            var info = new FileInfo(path); if (!info.Exists || info.Length > 256 * 1024) return null;
            using (var stream = File.OpenRead(path))
            {
                Span<byte> header = stackalloc byte[24]; if (stream.Read(header) != header.Length) return null;
                if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !header.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
                int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..]);
                int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..]);
                if (width is < 1 or > 152 || height is < 1 or > 98) return null;
            }
            using var image = Image.FromFile(path);
            if (image.Width > 152 || image.Height > 98) return null;
            return new Bitmap(image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { return null; }
    }
    public void Write(ResourceEntry entry, Bitmap bitmap)
    {
        string path = PathFor(entry), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            SafeOutput.RejectLinks(root); Directory.CreateDirectory(root); SafeOutput.RejectLinks(path);
            bitmap.Save(temporary, System.Drawing.Imaging.ImageFormat.Png); File.Move(temporary, path, true);
            // Bound the disk cache as well as the gallery's in-memory cache.
            foreach (var old in new DirectoryInfo(root).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Skip(2048))
            { SafeOutput.RejectLinks(old.FullName); old.Delete(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
