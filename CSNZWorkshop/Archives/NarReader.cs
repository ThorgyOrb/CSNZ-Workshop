// NAR layout and XOR constants based on NARTools, Copyright 2010 Da_FileServer.
// Modified implementation with bounded parsing. GPL-3.0-or-later; see THIRD_PARTY.md.
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;

namespace CSNZWorkshop.Archives;

public sealed class NarReader : IArchiveReader
{
    const int MaxIndexBytes = 64 * 1024 * 1024;
    static readonly byte[] HeaderKey = [0x19, 0x5B, 0x7B, 0x2C, 0x65, 0x5E, 0x79, 0x25,
        0x6E, 0x4B, 0x07, 0x21, 0x62, 0x7F, 0x00, 0x29];

    public IReadOnlyList<ResourceEntry> Read(string path, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 16 || reader.ReadUInt32() != 0x0052414E)
            throw new InvalidDataException("Firma NAR inválida o archivo truncado.");
        if (reader.ReadUInt32() != 0x01000000)
            throw new NotSupportedException("Versión de NAR no compatible.");
        stream.Position = stream.Length - 8;
        var length = reader.ReadInt32() ^ 0x4076551F;
        if (reader.ReadUInt32() != 0x0052414E || length < 4 || length > MaxIndexBytes || length > stream.Length - 16)
            throw new InvalidDataException("Índice NAR inválido, truncado o demasiado grande.");
        long indexStart = stream.Length - 8 - length;
        stream.Position = indexStart;
        var compressed = reader.ReadBytes(length);
        for (int i = 0; i < compressed.Length; i++) compressed[i] ^= HeaderKey[i % 16];
        using var input = new MemoryStream(compressed, false);
        using var bzip = new BZip2InputStream(input);
        using var decoded = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = bzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decoded.Length + read > MaxIndexBytes) throw new InvalidDataException("Índice NAR descomprimido demasiado grande.");
            decoded.Write(buffer, 0, read);
        }
        decoded.Position = 0;
        using var index = new BinaryReader(decoded);
        if (decoded.Length < 20) throw new InvalidDataException("Índice NAR incompleto.");
        var version = index.ReadInt32();
        if (version is not (1 or 2)) throw new NotSupportedException($"Índice NAR versión {version} no compatible.");
        decoded.Position = 16;
        int count = index.ReadInt32();
        if (count < 0 || count > (decoded.Length - 20) / 26)
            throw new InvalidDataException("Cantidad de recursos NAR inválida.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(51949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var entries = new List<ResourceEntry>(count);
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int pathLength = index.ReadUInt16();
            if (pathLength == 0 || pathLength > 16384 || pathLength + 24 > decoded.Length - decoded.Position)
                throw new InvalidDataException($"Ruta NAR inválida en recurso {i}.");
            var name = encoding.GetString(index.ReadBytes(pathLength));
            if (name.Contains('\0')) throw new InvalidDataException("Ruta NAR contiene caracteres nulos.");
            uint flags = index.ReadUInt32();
            long offset = index.ReadUInt32();
            int stored = index.ReadInt32(), size = index.ReadInt32();
            _ = index.ReadInt32(); // timestamp
            uint crc = index.ReadUInt32(); // CRC of stored bytes, checked during extraction
            if (stored < 0 || size < 0 || offset > indexStart || stored > indexStart - offset)
                throw new InvalidDataException($"Rango NAR fuera del paquete: {name}");
            entries.Add(new(name, Path.GetFullPath(path), "NAR", size, stored, offset, flags) { StoredCrc = crc });
        }
        return entries;
    }
}
