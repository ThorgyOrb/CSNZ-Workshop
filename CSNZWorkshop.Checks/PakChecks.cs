using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using CSNZWorkshop.Archives;

// PAK content checks. No package in the examined installation uses the compression flag, so these fixtures
// build the resource block by hand with the same key derivation and layer order the decoder expects.
static class PakChecks
{
    const string Path = "lstrike/common/models/prueba.mdl";
    static readonly byte[] DataKey = Convert.FromHexString("0102030405060708090A0B0C0D0E0F10");

    public static void Run(Action<string, Action> check)
    {
        byte[] content = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("CSNZ Workshop compresión PAK. ", 800)));
        check("PAK comprimido: zlib con cada combinación de capas", () =>
        {
            foreach (uint flags in new uint[] { 1, 3, 5, 7 })
            {
                byte[] block = Block(content, flags, out var entry);
                Assert(block.Length < content.Length / 2);
                Assert(Decode(block, entry).SequenceEqual(content));
            }
        });
        check("PAK comprimido: deflate sin cabecera zlib", () =>
        {
            byte[] block = Block(content, 7, out var entry, raw: true);
            Assert(Decode(block, entry).SequenceEqual(content));
        });
        check("PAK comprimido: recurso corto y relleno de la última palabra", () =>
        {
            foreach (int length in new[] { 1, 3, 4, 7, 1021, 1024, 1027 })
            {
                byte[] small = Enumerable.Range(0, length).Select(i => (byte)(i * 7)).ToArray();
                byte[] block = Block(small, 7, out var entry);
                Assert(block.Length % 4 == 0 && Decode(block, entry).SequenceEqual(small));
            }
        });
        check("PAK comprimido: contenido incompresible y de 1 MiB", () =>
        {
            var random = new Random(7);
            byte[] noise = new byte[1024 * 1024];
            random.NextBytes(noise);
            byte[] block = Block(noise, 7, out var entry);
            Assert(Decode(block, entry).SequenceEqual(noise));
        });
        check("PAK comprimido: tamaño declarado distinto del real", () =>
        {
            byte[] block = Block(content, 7, out var entry);
            Reject(() => Decode(block, entry with { Bytes = entry.Bytes + 1 }));
            Reject(() => Decode(block, entry with { Bytes = entry.Bytes - 1 }));
            Reject(() => Decode(block, entry with { Bytes = 0 }));
            Reject(() => Decode(block, entry with { BytesGuardados = 0 }));
        });
        check("PAK comprimido: bloque truncado o alterado", () =>
        {
            byte[] block = Block(content, 7, out var entry);
            Reject(() => Decode(block[..(block.Length / 2)], entry with { BytesGuardados = block.Length / 2 }));
            for (int position = 0; position < 3; position++)
            {
                byte[] damaged = block.ToArray();
                damaged[position * 37 % damaged.Length] ^= 0xFF;
                Reject(() => Decode(damaged, entry));
            }
        });
        check("PAK comprimido: gzip y bzip2 se informan como no implementados", () =>
        {
            foreach (byte[] magic in new byte[][] { [0x1F, 0x8B, 0x08, 0x00], [0x42, 0x5A, 0x68, 0x39] })
            {
                var entry = Entry(64, magic.Length, 1);
                try { Decode(magic, entry); }
                catch (NotSupportedException ex) { Assert(ex.Message.Contains("gzip") || ex.Message.Contains("bzip2")); continue; }
                throw new Exception("Unknown compressed container accepted");
            }
        });
        check("PAK comprimido: cancelación antes de descomprimir", () =>
        {
            byte[] block = Block(content, 7, out var entry);
            using var token = new CancellationTokenSource(); token.Cancel();
            using var source = new MemoryStream(block);
            using var sink = new MemoryStream();
            try { ResourceDecoder.Decode(source, entry, sink, token.Token); }
            catch (OperationCanceledException) { Assert(sink.Length == 0); return; }
            throw new Exception("Cancellation ignored");
        });
        check("PAK sin comprimir: sigue igual con y sin cifrado", () =>
        {
            foreach (uint flags in new uint[] { 0, 2, 4, 6 })
            {
                byte[] block = Block(content, flags, out var entry);
                Assert(block.Length >= content.Length && Decode(block, entry).SequenceEqual(content));
            }
        });
    }

    static ResourceEntry Entry(long bytes, long stored, uint flags) =>
        new(Path, "prueba.pak", "PAK", bytes, stored, 0, flags) { DataKey = DataKey };

    // Builds the stored block: compress when the flag asks for it, pad to a whole word, then apply the inner
    // layer over the first kilobyte and the outer layer over everything, which is the order the reader undoes.
    static byte[] Block(byte[] content, uint flags, out ResourceEntry entry, bool raw = false)
    {
        byte[] payload = (flags & 1) != 0 ? Compress(content, raw) : content;
        int padded = (payload.Length + 3) / 4 * 4;
        byte[] block = new byte[(flags & 6) != 0 ? padded : payload.Length];
        payload.CopyTo(block, 0);
        byte[] key = new byte[128];
        for (int i = 0; i < key.Length; i++)
            key[i] = unchecked((byte)(i + Path[i % Path.Length] * (i % 5 + DataKey[i % 16] + 2)));
        if ((flags & 2) != 0) Encrypt(block.AsSpan(0, Math.Min(1024, padded)), key);
        if ((flags & 4) != 0) Encrypt(block, key);
        entry = Entry(content.Length, block.Length, flags);
        return block;
    }

    static byte[] Compress(byte[] content, bool raw)
    {
        using var output = new MemoryStream();
        if (raw) using (var stream = new DeflateStream(output, CompressionLevel.SmallestSize, true)) stream.Write(content);
        else using (var stream = new ZLibStream(output, CompressionLevel.SmallestSize, true)) stream.Write(content);
        return output.ToArray();
    }

    // SnowCipher only decrypts; decrypting zeros yields the negated keystream, which encrypts the fixture.
    static void Encrypt(Span<byte> data, byte[] key)
    {
        byte[] keystream = new byte[data.Length];
        var cipher = new SnowCipher(); cipher.SetKey(key);
        cipher.Decrypt(keystream);
        for (int i = 0; i < data.Length; i += 4)
        {
            uint negated = BinaryPrimitives.ReadUInt32LittleEndian(keystream.AsSpan(i));
            uint plain = BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
            BinaryPrimitives.WriteUInt32LittleEndian(data[i..], unchecked(plain - negated));
        }
    }

    static byte[] Decode(byte[] block, ResourceEntry entry)
    {
        using var source = new MemoryStream(block);
        using var sink = new MemoryStream();
        ResourceDecoder.Decode(source, entry, sink);
        return sink.ToArray();
    }

    static void Assert(bool condition) { if (!condition) throw new Exception("PAK assertion failed"); }
    static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException) { return; }
        throw new Exception("Invalid PAK content accepted");
    }
}
