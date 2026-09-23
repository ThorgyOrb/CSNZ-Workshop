using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CSNZWorkshop.Archives;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Checksum;

static class ExtractionChecks
{
    public static void Run(string root, string? game, Action<string, Action> check)
    {
        string output = Path.Combine(root, "extracted");
        byte[] raw = "Hello CSNZ!"u8.ToArray();
        string plain = WriteNar(root, "raw.nar", "/cstrike/test.txt", raw, raw.Length, 0);
        ResourceEntry Entry(string path) => ArchiveIndex.Read(path).Single();
        check("Extracción NAR: contenido exacto, CRC y SHA-256", () =>
        {
            var report = Extraction.Run([Entry(plain)], output);
            Assert(report.Extraidos == 1 && report.Errores == 0);
            Assert(File.ReadAllBytes(report.Archivos[0].Destino!).SequenceEqual(raw));
            Assert(report.Archivos[0].Sha256 == Convert.ToHexString(SHA256.HashData(raw)));
        });
        check("NAR cifrado: recupera los bytes exactos", () =>
        {
            string package = WriteNar(root, "encoded.nar", "/cstrike/test.txt", raw, raw.Length, 1);
            var report = Extraction.Run([Entry(package)], output);
            Assert(report.Extraidos == 1 && File.ReadAllBytes(report.Archivos[0].Destino!).SequenceEqual(raw));
        });
        check("NAR LZ: literales y copia solapada", () =>
        {
            // Literal 'abc', then six bytes at distance three => abcabcabc.
            byte[] compressed = [2, 97, 98, 99, 0x80, 2];
            string package = WriteNar(root, "lz.nar", "/cstrike/lz.txt", compressed, 9, 2);
            var report = Extraction.Run([Entry(package)], output);
            Assert(report.Extraidos == 1 && File.ReadAllText(report.Archivos[0].Destino!) == "abcabcabc");
        });
        check("NAR LZ: longitud extendida y vuelta de la ventana", () =>
        {
            using var data = new MemoryStream();
            data.Write([0, 65]); // literal A
            for (int i = 0; i < 50; i++) data.Write([0xe0, 255, 0]); // 264 x distance 1
            string package = WriteNar(root, "lz-long.nar", "/cstrike/long.txt", data.ToArray(), 13201, 2);
            var report = Extraction.Run([Entry(package)], output);
            Assert(report.Extraidos == 1 && File.ReadAllBytes(report.Archivos[0].Destino!).All(b => b == 65));
        });
        check("CRC incorrecto: no publica un archivo ni deja temporales", () =>
        {
            string package = WriteNar(root, "bad-crc.nar", "/cstrike/bad.txt", raw, raw.Length, 0, badCrc: true);
            var entry = Entry(package);
            var report = Extraction.Run([entry], output);
            Assert(report.Errores == 1 && !File.Exists(SafeOutput.ResourcePath(output, entry)));
            Assert(!Directory.GetFiles(output, "*.partial", SearchOption.AllDirectories).Any());
        });
        check("LZ corrupto, truncado o excesivo: rechaza la salida", () =>
        {
            foreach (byte[] compressed in new byte[][] { [0x20, 0], [2, 65], [0, 65, 0xe0], [3, 65, 66, 67, 68] })
            {
                string package = WriteNar(root, Guid.NewGuid() + ".nar", "/cstrike/bad.txt", compressed, 3, 2);
                Assert(Extraction.Run([Entry(package)], output).Errores == 1);
            }
            Assert(!Directory.GetFiles(output, "*.partial", SearchOption.AllDirectories).Any());
        });
        check("Recurso vacío: crea un archivo de cero bytes", () =>
        {
            string package = WriteNar(root, "empty.nar", "/cstrike/empty.txt", [], 0, 0);
            var report = Extraction.Run([Entry(package)], output);
            Assert(report.Extraidos == 1 && new FileInfo(report.Archivos[0].Destino!).Length == 0);
        });
        check("Los archivos existentes no se sobrescriben", () =>
        {
            var entry = Entry(plain);
            string target = SafeOutput.ResourcePath(output, entry);
            File.WriteAllText(target, "User edit");
            Assert(Extraction.Run([entry], output).Omitidos == 1 && File.ReadAllText(target) == "User edit");
        });
        check("Rutas peligrosas, UNC, ADS y nombres reservados", () =>
        {
            foreach (string path in new[] { "../escape", "/cstrike/../../escape", "C:/escape", "//server/share/x", "\\\\?\\C:\\escape",
                "/cstrike/a:stream", "/cstrike/NUL.txt", "/cstrike/COM1", "/cstrike/COM¹.txt", "/cstrike/x.", "/cstrike/x ", "/cstrike//x" })
                Reject(() => SafeOutput.ResourcePath(output, Entry(plain) with { Ruta = path }));
        });
        check("Un recurso malicioso no impide extraer el siguiente", () =>
        {
            string bad = WriteNar(root, "evil.nar", "/../escape.txt", raw, raw.Length, 0);
            string good = WriteNar(root, "good.nar", "/cstrike/good.txt", raw, raw.Length, 0);
            var report = Extraction.Run([Entry(bad), Entry(good)], output);
            Assert(report.Errores == 1 && report.Extraidos == 1 && !File.Exists(Path.Combine(root, "escape.txt")));
        });
        check("Paquetes de igual nombre en carpetas distintas no colisionan", () =>
        {
            string sub = Path.Combine(root, "second"); Directory.CreateDirectory(sub);
            string other = WriteNar(sub, "raw.nar", "/cstrike/test.txt", raw, raw.Length, 0);
            Assert(SafeOutput.ResourcePath(output, Entry(other)) != SafeOutput.ResourcePath(output, Entry(plain)));
        });
        check("Índice cambiado tras listar: no extrae metadatos obsoletos", () =>
        {
            var obsolete = Entry(plain) with { Bytes = 123 };
            Assert(Extraction.Run([obsolete], output).Errores == 1);
        });
        check("Cancelación antes de comenzar conserva todas las entradas pendientes", () =>
        {
            using var token = new CancellationTokenSource(); token.Cancel();
            var report = Extraction.Run([Entry(plain)], output, token.Token);
            Assert(report.Cancelada && report.Pendientes == 1 && report.Extraidos == 0);
        });
        check("Cancelación del decodificador durante escritura", () =>
        {
            // Cancellation is tested directly in the decoder at a chunk boundary.
            using var token = new CancellationTokenSource();
            byte[] data = new byte[140000];
            string package = WriteNar(root, "cancel.nar", "/cstrike/large.bin", data, data.Length, 0);
            using var input = File.OpenRead(package);
            using var sink = new CancelOnWrite(token);
            try { ResourceDecoder.Decode(input, Entry(package), sink, token.Token); }
            catch (OperationCanceledException) { Assert(sink.Length == 65536); return; }
            throw new Exception("Cancellation ignored");
        });
        check("Cancelación al extraer no publica un archivo parcial", () =>
        {
            using var token = new CancellationTokenSource();
            string package = WriteNar(root, "cancel-batch.nar", "/cstrike/cancel.bin", raw, raw.Length, 0);
            var entry = Entry(package);
            var report = Extraction.Run([entry], output, token.Token, new CancelOnProgress(token));
            Assert(report.Cancelada && report.Pendientes == 1 && !File.Exists(SafeOutput.ResourcePath(output, entry)));
            Assert(!Directory.GetFiles(output, "*.partial", SearchOption.AllDirectories).Any());
        });
        check("PAK con flags desconocidos: error explícito", () =>
        {
            using var input = new MemoryStream(new byte[4]);
            using var sink = new MemoryStream();
            foreach (uint flag in new uint[] { 8, 16, 0x80000000 })
                Reject(() => ResourceDecoder.Decode(input, new("test", "test.pak", "PAK", 4, 4, 0, flag), sink));
            Assert(sink.Length == 0);
        });
        check("Límite de tamaño se comprueba antes de escribir", () =>
        {
            using var input = new MemoryStream(new byte[4]);
            using var sink = new MemoryStream();
            Reject(() => ResourceDecoder.Decode(input, Entry(plain) with { Bytes = ResourceDecoder.MaxResourceBytes + 1 }, sink));
            Assert(sink.Length == 0);
        });
        check("Protege la carpeta de instalación", () =>
            Reject(() => SafeOutput.CheckDestination(Path.Combine(root, "Game", "Data", "out"), [Path.Combine(root, "Game", "Data", "sample.nar")])));
        check("Informe JSON: resultados y pendientes", () =>
        {
            var report = Extraction.Run([Entry(plain)], output);
            string path = Extraction.SaveReport(output, report);
            Assert(File.ReadAllText(path).Contains("Omitidos") && report.Pendientes == 0);
        });
        check("Interfaz: carga recursos y habilita extracción múltiple", () => UiChecks.Verify(plain));
        if (game is not null) Integration(game, root, check);
    }

    static void Integration(string game, string root, Action<string, Action> check)
    {
        check("Integración: extracción NAR 0/1/2 y PAK 6", () =>
        {
            var nar = ArchiveIndex.Read(Path.Combine(game, "cstrike.nar"));
            var pak = ArchiveIndex.Read(Path.Combine(game, "Packer", "common_00000.pak"));
            var model = ArchiveIndex.Read(Path.Combine(game, "Packer", "common_00004.pak"));
            ResourceEntry[] selected = [
                nar.First(r => r.Ruta == "/cstrike/resource/warning.wav"),
                nar.First(r => r.Flags == 1 && r.Bytes > 0),
                nar.First(r => r.Extension == ".mdl"), nar.First(r => r.Extension == ".spr"),
                pak.First(r => r.Extension == ".wav"), model.First(r => r.Extension == ".mdl")];
            var report = Extraction.Run(selected, Path.Combine(root, "real-samples"));
            foreach (var item in report.Archivos) Console.WriteLine($"  {item.Estado}: {item.Recurso}: {item.Detalle}");
            Assert(report.Extraidos == selected.Length && report.Errores == 0);
            foreach (var item in report.Archivos.Where(i => i.Recurso.EndsWith(".mdl")))
            {
                using var file = File.OpenRead(item.Destino!); byte[] signature = new byte[4]; file.ReadExactly(signature);
                Assert(signature.SequenceEqual("IDST"u8.ToArray()));
            }
            foreach (var item in report.Archivos.Where(i => i.Recurso.EndsWith(".wav")))
            {
                using var file = File.OpenRead(item.Destino!); byte[] header = new byte[12]; file.ReadExactly(header);
                Assert(header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WAVE"u8));
                Assert(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) + 8L == file.Length);
            }
        });
        check("Integración: PAK pequeño descifra la última palabra con padding", () =>
        {
            var entry = ArchiveIndex.Read(Path.Combine(game, "Packer", "common_00036.pak"))
                .Single(r => r.Ruta == "lstrike/common/resource/Hud/hud_cd_num_big_blue_colon.tga");
            var report = Extraction.Run([entry], Path.Combine(root, "small-pak"));
            Assert(report.Extraidos == 1);
            byte[] bytes = File.ReadAllBytes(report.Archivos[0].Destino!);
            // Current installed fixture: uncompressed 6x22 BGRA texture, transparent white at the edge.
            Assert(bytes.Length == 546 && bytes[2] == 2 && bytes[16] == 32);
            Assert(bytes.AsSpan(bytes.Length - 8).SequenceEqual(new byte[] { 255, 255, 255, 0, 255, 255, 255, 0 }));
        });
    }

    public static string WriteNar(string root, string file, string name, byte[] data, int decodedLength, uint flags, bool badCrc = false)
    {
        byte[] stored = data.ToArray();
        if (flags != 0)
        {
            byte[] pathBytes = Encoding.ASCII.GetBytes(name);
            uint seed = 0;
            foreach (byte b in pathBytes) seed = unchecked(seed * 1000003) ^ b;
            seed ^= (uint)pathBytes.Length;
            byte[] key = new byte[16];
            for (int i = 0; i < 16; i++) { seed = unchecked(seed * 1103515245 + 12345); key[i] = (byte)seed; }
            for (int i = 0; i < stored.Length; i++) stored[i] ^= key[i & 15];
        }
        var crc = new Crc32(); crc.Update(stored);
        using var decodedIndex = new MemoryStream();
        using (var writer = new BinaryWriter(decodedIndex, Encoding.UTF8, true))
        {
            writer.Write(1); writer.Write(0); writer.Write(0); writer.Write(1); writer.Write(1);
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            writer.Write((ushort)nameBytes.Length); writer.Write(nameBytes); writer.Write(flags);
            writer.Write(8u); writer.Write(stored.Length); writer.Write(decodedLength); writer.Write(0);
            writer.Write((uint)crc.Value ^ (badCrc ? 1u : 0u));
        }
        decodedIndex.Position = 0;
        using var compressed = new MemoryStream(); BZip2.Compress(decodedIndex, compressed, false, 9);
        byte[] header = compressed.ToArray();
        byte[] xor = [0x19,0x5B,0x7B,0x2C,0x65,0x5E,0x79,0x25,0x6E,0x4B,0x07,0x21,0x62,0x7F,0x00,0x29];
        for (int i = 0; i < header.Length; i++) header[i] ^= xor[i & 15];
        string target = Path.Combine(root, file);
        using var output = new BinaryWriter(File.Create(target));
        output.Write(0x0052414Eu); output.Write(0x01000000u); output.Write(stored); output.Write(header);
        output.Write(header.Length ^ 0x4076551F); output.Write(0x0052414Eu);
        return target;
    }

    static void Assert(bool condition) { if (!condition) throw new Exception("Extraction assertion failed"); }
    static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException) { return; }
        throw new Exception("Unsafe or unsupported input accepted");
    }
    sealed class CancelOnWrite(CancellationTokenSource token) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { base.Write(buffer, offset, count); token.Cancel(); }
    }
    sealed class CancelOnProgress(CancellationTokenSource token) : IProgress<ExtractionProgress>
    {
        public void Report(ExtractionProgress value) { if (value.Recurso.Length > 0) token.Cancel(); }
    }
}
