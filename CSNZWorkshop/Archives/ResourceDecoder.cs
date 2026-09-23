// NAR decoding adapted from NARTools (Da_FileServer), GPL-3.0-or-later.
// PAK data keys and cipher passes adapted from CSO-Paker (Luís Leite / overl4y), GPL-3.0.
// Streaming, bounds checks and cancellation added for CSNZ Workshop. See THIRD_PARTY.md.
using System.Text;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace CSNZWorkshop.Archives;

public static class ResourceDecoder
{
    public const long MaxResourceBytes = 512L * 1024 * 1024;

    public static void Decode(Stream source, ResourceEntry entry, Stream destination, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (entry.Bytes < 0 || entry.BytesGuardados < 0 || entry.Bytes > MaxResourceBytes || entry.BytesGuardados > MaxResourceBytes)
            throw new InvalidDataException("El recurso supera el límite de 512 MiB o declara un tamaño inválido.");
        if (entry.Offset < 0 || entry.Offset > source.Length || entry.BytesGuardados > source.Length - entry.Offset)
            throw new InvalidDataException("El recurso está fuera del paquete o el paquete fue truncado.");
        source.Position = entry.Offset;
        if (entry.Formato == "NAR") DecodeNar(source, entry, destination, token);
        else if (entry.Formato == "PAK") DecodePak(source, entry, destination, token);
        else throw new NotSupportedException("Formato no compatible para extracción.");
    }

    static void DecodeNar(Stream source, ResourceEntry entry, Stream output, CancellationToken token)
    {
        if (entry.Flags > 2) throw new NotSupportedException($"Tipo de almacenamiento NAR no compatible: {entry.Flags}.");
        if (entry.StoredCrc is null) throw new InvalidDataException("Falta el CRC NAR; vuelve a leer el índice.");
        var key = new byte[16];
        if (entry.Flags != 0)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            byte[] path = Encoding.GetEncoding(51949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(entry.Ruta);
            uint seed = 0;
            foreach (byte b in path) seed = unchecked(seed * 1000003) ^ b;
            seed ^= (uint)path.Length;
            for (int i = 0; i < key.Length; i++) { seed = unchecked(seed * 1103515245 + 12345); key[i] = (byte)seed; }
        }
        var input = new NarInput(source, entry.BytesGuardados, key);
        long written = 0;
        if (entry.Flags != 2)
        {
            if (entry.Bytes != entry.BytesGuardados) throw new InvalidDataException("Tamaños incompatibles para un recurso NAR sin compresión.");
            byte[] buffer = new byte[65536];
            while (written < entry.Bytes)
            {
                token.ThrowIfCancellationRequested();
                int count = (int)Math.Min(buffer.Length, entry.Bytes - written);
                for (int i = 0; i < count; i++) buffer[i] = input.RequiredByte();
                output.Write(buffer, 0, count); written += count;
            }
        }
        else
        {
            // LZ dictionary: 8 KiB history; overlapping copies are intentional.
            byte[] window = new byte[8192];
            byte[] buffer = new byte[65536];
            int buffered = 0;
            void Emit(byte value)
            {
                window[written & 8191] = value;
                buffer[buffered++] = value; written++;
                if (buffered == buffer.Length) { output.Write(buffer); buffered = 0; }
            }
            while (written < entry.Bytes)
            {
                token.ThrowIfCancellationRequested();
                int control = input.RequiredByte();
                int operation = control >> 5, length = control & 31;
                if (operation == 0)
                {
                    int count = length + 1;
                    if (count > entry.Bytes - written) throw new InvalidDataException("NAR descomprime más bytes de los declarados.");
                    for (int i = 0; i < count; i++) Emit(input.RequiredByte());
                }
                else
                {
                    if (operation == 7) operation += input.RequiredByte();
                    int count = operation + 2;
                    int distance = ((length << 8) | input.RequiredByte()) + 1;
                    if (distance > Math.Min(written, 8192) || count > entry.Bytes - written)
                        throw new InvalidDataException("Referencia LZ NAR fuera de rango.");
                    for (int i = 0; i < count; i++) Emit(window[(written - distance) & 8191]);
                }
            }
            if (buffered > 0) output.Write(buffer, 0, buffered);
        }
        if (input.NextByte() != -1) throw new InvalidDataException("Sobran datos en el recurso NAR.");
        if ((uint)input.Crc.Value != entry.StoredCrc) throw new InvalidDataException("CRC NAR incorrecto: contenido alterado o dañado.");
    }

    static void DecodePak(Stream source, ResourceEntry entry, Stream output, CancellationToken token)
    {
        if ((entry.Flags & ~7u) != 0) throw new NotSupportedException($"Flags PAK desconocidos: 0x{entry.Flags:X}.");
        bool compressed = (entry.Flags & 1) != 0;
        // Compressed resources store the packed stream; the rest store the content padded to a whole word.
        long stored = compressed ? entry.BytesGuardados : entry.Bytes;
        long padded = (stored + 3) / 4 * 4;
        long required = (entry.Flags & 6) != 0 ? padded : stored;
        if (entry.BytesGuardados < required) throw new InvalidDataException("El bloque PAK es menor que el contenido declarado.");
        if (compressed && (entry.BytesGuardados == 0 || entry.Bytes == 0))
            throw new InvalidDataException("Recurso PAK comprimido con tamaños inválidos.");
        if (entry.DataKey is not { Length: 16 } || entry.Ruta.Length == 0) throw new InvalidDataException("Falta la clave del recurso PAK.");
        byte[] key = new byte[128];
        for (int i = 0; i < key.Length; i++)
            key[i] = unchecked((byte)(i + entry.Ruta[i % entry.Ruta.Length] * (i % 5 + entry.DataKey[i % 16] + 2)));
        SnowCipher? outer = null, inner = null;
        if ((entry.Flags & 4) != 0) { outer = new SnowCipher(); outer.SetKey(key); }
        if ((entry.Flags & 2) != 0) { inner = new SnowCipher(); inner.SetKey(key); }
        // Short resources include the final padded word in the inner layer too.
        var plain = new PakInput(source, required, outer, inner, Math.Min(1024, padded), token);
        if (compressed) { Inflate(plain, entry.Bytes, output, token); return; }
        byte[] buffer = new byte[65536];
        long written = 0;
        while (written < entry.Bytes)
        {
            int count = plain.Read(buffer, 0, (int)Math.Min(buffer.Length, entry.Bytes - written));
            if (count == 0) throw new InvalidDataException("Recurso PAK incompleto.");
            output.Write(buffer, 0, count); written += count;
        }
    }

    // The game file system module (FileSystem_Nar.dll) links zlib 1.2.11, so the packed block is read as a zlib
    // stream; a raw deflate stream is accepted too. The output must match the declared size exactly.
    static void Inflate(Stream plain, long expected, Stream output, CancellationToken token)
    {
        // Report a damaged stream the same way as the rest of the readers, instead of a library exception.
        try { InflateStream(plain, expected, output, token); }
        catch (ICSharpCode.SharpZipLib.SharpZipBaseException ex)
        { throw new InvalidDataException("El recurso PAK comprimido está dañado: " + ex.Message); }
    }

    static void InflateStream(Stream plain, long expected, Stream output, CancellationToken token)
    {
        byte[] head = new byte[2];
        int available = 0;
        while (available < head.Length)
        {
            int read = plain.Read(head, available, head.Length - available);
            if (read == 0) break;
            available += read;
        }
        if (available < 2) throw new InvalidDataException("Recurso PAK comprimido truncado.");
        if (head[0] == 0x1F && head[1] == 0x8B) throw new NotSupportedException("Recurso PAK comprimido con gzip: formato no implementado.");
        if (head[0] == 0x42 && head[1] == 0x5A) throw new NotSupportedException("Recurso PAK comprimido con bzip2: formato no implementado.");
        // zlib header: deflate method with a valid check value; anything else is treated as raw deflate.
        bool zlib = (head[0] & 0x0F) == 8 && ((head[0] << 8) | head[1]) % 31 == 0;
        var inflater = new Inflater(!zlib);
        inflater.SetInput(head, 0, available);
        byte[] input = new byte[65536], chunk = new byte[65536];
        long written = 0;
        while (written < expected)
        {
            token.ThrowIfCancellationRequested();
            if (inflater.IsNeedingDictionary) throw new NotSupportedException("El recurso PAK comprimido necesita un diccionario.");
            if (inflater.IsFinished) throw new InvalidDataException("El recurso PAK comprimido termina antes de su tamaño declarado.");
            if (inflater.IsNeedingInput)
            {
                int read = plain.Read(input, 0, input.Length);
                if (read == 0) throw new InvalidDataException("Recurso PAK comprimido truncado.");
                inflater.SetInput(input, 0, read);
            }
            int produced = inflater.Inflate(chunk, 0, (int)Math.Min(chunk.Length, expected - written));
            if (produced > 0) { output.Write(chunk, 0, produced); written += produced; }
        }
        while (!inflater.IsFinished)
        {
            token.ThrowIfCancellationRequested();
            if (inflater.Inflate(chunk, 0, chunk.Length) > 0)
                throw new InvalidDataException("El recurso PAK comprimido genera más bytes de los declarados.");
            if (inflater.IsFinished || !inflater.IsNeedingInput) break;
            int read = plain.Read(input, 0, input.Length);
            if (read == 0) break;
            inflater.SetInput(input, 0, read);
        }
        // Finishing cleanly also validates the Adler-32 checksum of a zlib stream.
        if (!inflater.IsFinished) throw new InvalidDataException("El flujo comprimido del recurso PAK no termina correctamente.");
    }

    // Serves the resource block already decrypted: the outer layer covers everything, the inner one a prefix.
    sealed class PakInput(Stream source, long length, SnowCipher? outer, SnowCipher? inner, long innerPrefix, CancellationToken token) : Stream
    {
        readonly byte[] buffer = new byte[65536];
        long consumed;
        int position, count;
        public override int Read(byte[] destination, int offset, int size)
        {
            token.ThrowIfCancellationRequested();
            if (position == count)
            {
                if (consumed == length) return 0;
                count = (int)Math.Min(buffer.Length, length - consumed);
                source.ReadExactly(buffer.AsSpan(0, count));
                outer?.Decrypt(buffer.AsSpan(0, count));
                if (inner is not null && consumed < innerPrefix)
                    inner.Decrypt(buffer.AsSpan(0, (int)Math.Min(count, innerPrefix - consumed)));
                consumed += count; position = 0;
            }
            int served = Math.Min(size, count - position);
            buffer.AsSpan(position, served).CopyTo(destination.AsSpan(offset, served));
            position += served;
            return served;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => consumed - count + position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    sealed class NarInput(Stream source, long length, byte[] key)
    {
        readonly byte[] buffer = new byte[65536];
        long loaded;
        int position, count;
        public Crc32 Crc { get; } = new();
        public int NextByte()
        {
            if (position == count)
            {
                if (loaded == length) return -1;
                count = (int)Math.Min(buffer.Length, length - loaded);
                source.ReadExactly(buffer.AsSpan(0, count));
                Crc.Update(new ArraySegment<byte>(buffer, 0, count));
                for (int i = 0; i < count; i++) buffer[i] ^= key[(loaded + i) & 15];
                loaded += count; position = 0;
            }
            return buffer[position++];
        }
        public byte RequiredByte() => NextByte() is int value && value >= 0 ? (byte)value
            : throw new EndOfStreamException("Contenido NAR truncado.");
    }
}
