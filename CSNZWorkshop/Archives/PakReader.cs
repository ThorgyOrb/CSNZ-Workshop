// Adapted from CSO-Paker by Luís Leite / overl4y, GPL-3.0; see THIRD_PARTY.md.
using System.Buffers.Binary;
using System.Text;

namespace CSNZWorkshop.Archives;

public sealed class PakReader : IArchiveReader
{
    const string KeySuffix = "CqeLFV@*0IfewH(";

    public IReadOnlyList<ResourceEntry> Read(string path, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(path);
        string filename = Path.GetFileName(path);
        int sum = filename.Sum(c => (int)c);
        long headerStart = sum % 312 + 30;
        if (stream.Length < headerStart + 12) throw new InvalidDataException("Cabecera PAK truncada.");
        stream.Position = headerStart;
        byte[] header = new byte[12];
        stream.ReadExactly(header);
        var cipher = new SnowCipher();
        cipher.SetKey(Key(filename, false)); cipher.Decrypt(header);
        uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(header);
        byte version = header[4];
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(5));
        if (version != 2 || checksum != (long)version + count)
            throw new InvalidDataException("No se pudo validar PAK v2. Conserva el nombre original del paquete.");
        long entriesStart = headerStart + 42 + (3L * sum % 212);
        if (entriesStart > stream.Length || count > 1_000_000 || count > (stream.Length - entriesStart) / 40)
            throw new InvalidDataException("Cantidad de recursos PAK inválida.");
        stream.Position = entriesStart;
        var input = new CipherReader(stream, Key(filename, true));
        var entries = new List<ResourceEntry>((int)count);
        var unicode = new UnicodeEncoding(false, false, true);
        for (uint i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint length = input.UInt32();
            if (length == 0 || length > 16384) throw new InvalidDataException($"Longitud de ruta PAK inválida en recurso {i}.");
            string name = unicode.GetString(input.Bytes((int)length * 2));
            if (name.Contains('\0')) throw new InvalidDataException("Ruta PAK contiene caracteres nulos.");
            _ = input.UInt32(); // unknown field
            uint flags = input.UInt32();
            long block = input.UInt32();
            uint size = input.UInt32(), stored = input.UInt32();
            byte[] key = input.Bytes(16);
            entries.Add(new(name, Path.GetFullPath(path), "PAK", size, stored, block * 1024, flags) { DataKey = key });
        }
        long dataStart = (stream.Position + 1023) / 1024 * 1024;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i] with { Offset = dataStart + entries[i].Offset };
            if (entry.Offset > stream.Length || entry.BytesGuardados > stream.Length - entry.Offset)
                throw new InvalidDataException($"Rango PAK fuera del paquete: {entry.Ruta}");
            entries[i] = entry;
        }
        return entries;
    }

    static byte[] Key(string filename, bool entries)
    {
        string text = filename + KeySuffix;
        var key = new byte[128];
        for (int i = 0; i < key.Length; i++)
            key[i] = unchecked((byte)(entries ? i + (i % 3 + 2) * text[text.Length - i % text.Length - 1]
                : i + text[i % text.Length]));
        return key;
    }

    // Decrypt complete 32-bit words while preserving unconsumed bytes between fields.
    sealed class CipherReader
    {
        readonly Stream stream;
        readonly SnowCipher cipher = new();
        readonly byte[] word = new byte[4];
        int position = 4;
        long consumed;
        public CipherReader(Stream stream, byte[] key) { this.stream = stream; cipher.SetKey(key); }
        public byte[] Bytes(int count)
        {
            if (consumed + count > 64 * 1024 * 1024) throw new InvalidDataException("Índice PAK demasiado grande.");
            var result = new byte[count];
            for (int i = 0; i < count; i++)
            {
                if (position == 4) { stream.ReadExactly(word); cipher.Decrypt(word); position = 0; }
                result[i] = word[position++];
            }
            consumed += count;
            return result;
        }
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
    }
}
