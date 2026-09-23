using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSNZWorkshop.Archives;

public record ResourceEntry(string Ruta, string Paquete, string Formato, long Bytes, long BytesGuardados,
    long Offset, uint Flags)
{
    public string Extension => Path.GetExtension(Ruta).ToLowerInvariant();
    [JsonIgnore] public uint? StoredCrc { get; init; }
    [JsonIgnore] public byte[]? DataKey { get; init; }
}
public record IndexError(string Paquete, string Error);
public record ResourceIndex(List<ResourceEntry> Recursos, List<IndexError> Errores, int PaquetesLeidos);

public interface IArchiveReader
{
    IReadOnlyList<ResourceEntry> Read(string path, CancellationToken cancellationToken = default);
}

public static class ArchiveIndex
{
    public static IReadOnlyList<ResourceEntry> Read(string path, CancellationToken token = default) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".nar" => new NarReader().Read(path, token),
            ".pak" => new PakReader().Read(path, token),
            _ => throw new NotSupportedException("El archivo no es NAR ni PAK.")
        };

    public static ResourceIndex Build(IEnumerable<string> paths, CancellationToken token = default,
        IProgress<string>? progress = null)
    {
        var entries = new List<ResourceEntry>();
        var errors = new List<IndexError>();
        int count = 0;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(Path.GetFileName(path));
            try
            {
                entries.AddRange(Read(path, token));
                count++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException
                or System.Text.DecoderFallbackException or ICSharpCode.SharpZipLib.SharpZipBaseException)
            { errors.Add(new(path, ex.Message)); }
        }
        return new(entries, errors, count);
    }

    public static void Export(string path, ResourceIndex index) => File.WriteAllText(path,
        JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
}
