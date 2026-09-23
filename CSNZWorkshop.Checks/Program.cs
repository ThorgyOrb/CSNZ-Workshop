using System.Text;
using CSNZWorkshop.Archives;
using ICSharpCode.SharpZipLib.BZip2;

var root = Path.Combine(Path.GetTempPath(), "CSNZWorkshop-checks-" + Guid.NewGuid());
Directory.CreateDirectory(root);
int passed = 0;
void Check(string name, Action action)
{
    action(); passed++; Console.WriteLine("OK " + name);
}
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ICSharpCode.SharpZipLib.SharpZipBaseException) { return; }
    throw new Exception("Malformed input accepted");
}
byte[] NarIndex(string name = "/cstrike/models/sample.mdl", uint offset = 8, int stored = 4)
{
    using var data = new MemoryStream();
    using var writer = new BinaryWriter(data, Encoding.UTF8, true);
    writer.Write(1); writer.Write(0); writer.Write(0); writer.Write(1); writer.Write(1);
    byte[] path = Encoding.ASCII.GetBytes(name);
    writer.Write((ushort)path.Length); writer.Write(path); writer.Write(0u);
    writer.Write(offset); writer.Write(stored); writer.Write(4); writer.Write(0); writer.Write(0u);
    return data.ToArray();
}
string WriteNar(string filename, byte[] index)
{
    using var compressed = new MemoryStream();
    using (var input = new MemoryStream(index)) BZip2.Compress(input, compressed, false, 9);
    byte[] bytes = compressed.ToArray();
    byte[] key = [0x19,0x5B,0x7B,0x2C,0x65,0x5E,0x79,0x25,0x6E,0x4B,0x07,0x21,0x62,0x7F,0x00,0x29];
    for (int i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % 16];
    var path = Path.Combine(root, filename);
    using var output = new BinaryWriter(File.Create(path));
    output.Write(0x0052414Eu); output.Write(0x01000000u); output.Write("IDST"u8.ToArray());
    output.Write(bytes); output.Write(bytes.Length ^ 0x4076551F); output.Write(0x0052414Eu);
    return path;
}
try
{
    string valid = WriteNar("valid.nar", NarIndex());
    Check("NAR válido: ruta, extensión, tamaño y offset", () =>
    {
        var resources = ArchiveIndex.Read(valid);
        Assert(resources.Count == 1 && resources[0].Ruta == "/cstrike/models/sample.mdl"
            && resources[0].Extension == ".mdl" && resources[0].Bytes == 4 && resources[0].Offset == 8);
    });
    Check("NAR con rango fuera del archivo", () => Reject(() => ArchiveIndex.Read(WriteNar("range.nar", NarIndex(offset: uint.MaxValue)))));
    Check("NAR con tamaño negativo", () => Reject(() => ArchiveIndex.Read(WriteNar("negative.nar", NarIndex(stored: -1)))));
    Check("NAR con ruta nula", () => Reject(() => ArchiveIndex.Read(WriteNar("null.nar", NarIndex(name: "bad\0path")))));
    Check("NAR con índice truncado", () => Reject(() => ArchiveIndex.Read(WriteNar("index.nar", [1, 0, 0, 0]))));
    Check("NAR truncado en cada byte", () =>
    {
        byte[] bytes = File.ReadAllBytes(valid);
        string path = Path.Combine(root, "truncated.nar");
        for (int length = 0; length < bytes.Length; length++)
        { File.WriteAllBytes(path, bytes[..length]); Reject(() => ArchiveIndex.Read(path)); }
    });
    string badPak = Path.Combine(root, "common_00000.pak");
    Check("PAK con cabecera inválida", () => { File.WriteAllBytes(badPak, new byte[2048]); Reject(() => ArchiveIndex.Read(badPak)); });
    Check("PAK truncado", () => { File.WriteAllBytes(badPak, [0, 1, 2]); Reject(() => ArchiveIndex.Read(badPak)); });
    Check("El lote conserva recursos y registra paquetes fallidos", () =>
    {
        var index = ArchiveIndex.Build([badPak, valid]);
        Assert(index.PaquetesLeidos == 1 && index.Recursos.Count == 1 && index.Errores.Count == 1);
    });
    Check("Las rutas duplicadas conservan su origen", () =>
    {
        string second = WriteNar("second.nar", NarIndex());
        var index = ArchiveIndex.Build([valid, second]);
        Assert(index.Recursos.Count == 2 && index.Recursos[0].Paquete != index.Recursos[1].Paquete);
    });
    Check("Cancelación del lote", () =>
    {
        using var token = new CancellationTokenSource(); token.Cancel();
        try { ArchiveIndex.Build([valid], token.Token); }
        catch (OperationCanceledException) { return; }
        throw new Exception("Cancellation ignored");
    });
    if (args.Length == 1)
    {
        Check("Integración: índices reales NAR y PAK", () =>
        {
            var nar = ArchiveIndex.Read(Path.Combine(args[0], "cstrike.nar"));
            var pak = ArchiveIndex.Read(Path.Combine(args[0], "Packer", "common_00000.pak"));
            Assert(nar.Any(r => r.Extension == ".mdl") && pak.Any(r => r.Extension == ".wav"));
            Console.WriteLine($"  cstrike.nar: {nar.Count}; common_00000.pak: {pak.Count}");
        });
    }
    ExtractionChecks.Run(root, args.Length == 1 ? args[0] : null, Check);
    PakChecks.Run(Check);
    ModelChecks.Run(root, args.Length == 1 ? args[0] : null, Check);
    Console.WriteLine($"{passed} comprobaciones correctas.");
}
finally
{
    // Only remove the unique temporary directory created by this test process.
    Directory.Delete(root, true);
}
