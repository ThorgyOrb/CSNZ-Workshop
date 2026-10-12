using System.Text;
using CSNZWorkshop.Archives;
using ICSharpCode.SharpZipLib.BZip2;

if (args.Length == 3 && args[0] == "--guillotine-weapon-check")
{
    WeaponChecks.Guillotine(args[1], args[2]);
    return;
}
if (args.Length == 4 && args[0] == "--texture-audit")
{
    TextureChecks.Audit(args[1], args[2], args[3]);
    return;
}
if (args.Length == 2 && args[0] == "--verify-texture-audit")
{
    TextureChecks.VerifyAudit(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "--unity-fixture")
{
    Console.WriteLine(UnityChecks.ExportFixture(args[1]));
    return;
}
if (args.Length == 3 && args[0] == "--export-unity")
{
    Console.WriteLine(CSNZWorkshop.Models.UnityModelExporter.Export(CSNZWorkshop.Models.ModelBundle.FromFile(args[1]), args[2]));
    return;
}

if (args.Length == 3 && args[0] is "--additive-model" or "--inspect-effects")
{
    var inventory = CSNZWorkshop.Inventory.Scan(args[1]);
    var index = ArchiveIndex.Build(inventory.Packages.Select(p => p.Ruta));
    var entry = index.Recursos.First(r => Path.GetFileName(r.Ruta).Equals(args[2], StringComparison.OrdinalIgnoreCase));
    var bundle = CSNZWorkshop.Models.ModelBundle.FromArchive(entry, index.Recursos);
    Console.WriteLine(entry.Ruta);
    foreach (var group in bundle.Model.Textures.GroupBy(t => t.Flags)) Console.WriteLine($"Flags 0x{group.Key:X}: {group.Count()} texturas");
    for (int i = 0; i < bundle.Model.Textures.Length; i++) Console.WriteLine($"{i}: {bundle.Model.Textures[i].Name}");
    if (args[0] == "--inspect-effects")
    {
        var model = bundle.Model;
        var effectBones = new HashSet<int>();
        foreach (var part in model.Bodies.SelectMany(b => b.Variants))
            foreach (var triangle in part.Triangles)
                if (model.Skins.Any(s => triangle.Skin < s.Length && s[triangle.Skin] >= 0 && s[triangle.Skin] < model.Textures.Length
                    && ((model.Textures[s[triangle.Skin]].Flags & 0x20) != 0 || CSNZWorkshop.Models.ModelRenderer.IsNexonEffect(model.Textures[s[triangle.Skin]]))))
                    foreach (var corner in new[] { triangle.A, triangle.B, triangle.C }) effectBones.Add(part.BoneIndices[corner.Vertex]);
        Console.WriteLine("Huesos de efectos: " + string.Join(", ", effectBones.Select(i => $"{i}:{model.Bones[i].Name}")));
        Console.WriteLine("Compañeros faltantes: " + string.Join(", ", bundle.Missing));
        for (int sequence = 0; sequence < model.Sequences.Length; sequence++)
        {
            var seq = model.Sequences[sequence];
            try
            {
                var start = model.Pose(sequence, 0); var moving = new HashSet<int>();
                for (int frame = 1; frame < seq.Frames; frame++)
                {
                    var pose = model.Pose(sequence, frame);
                    foreach (int bone in effectBones) if (pose[bone] != start[bone]) moving.Add(bone);
                }
                Console.WriteLine($"Secuencia {sequence}: {seq.Name}, {seq.Frames} frames, {seq.Fps} FPS, {seq.Blends} blends; huesos de efectos con cambio: {string.Join(",", moving)}");
            }
            catch (Exception ex) { Console.WriteLine($"Secuencia {sequence}: {seq.Name}: {ex.Message}"); }
        }
        foreach (var ev in model.Events) Console.WriteLine($"Evento: secuencia={ev.Sequence}, frame={ev.Frame}, código={ev.Code}, opciones={ev.Options}");
        Console.WriteLine($"Total eventos: {model.Events.Count}; inspección de poses: primer blend; no evalúa materiales ni código del cliente.");
        return;
    }
    Directory.CreateDirectory("artifacts");
    using var corrected = CSNZWorkshop.Models.ModelRenderer.Render(bundle.Model, 900, 700, new(Sequence: 0));
    corrected.Save("artifacts/additive-model-after.png");
    using var focused = CSNZWorkshop.Models.ModelRenderer.Render(bundle.Model, 900, 700, new(Sequence: 0, FocusGeometry: true, HideEffects: true));
    focused.Save("artifacts/model-focused.png");
    using var firstPerson = CSNZWorkshop.Models.ModelRenderer.Render(bundle.Model, 900, 700, new(0, 0, Sequence: 0, FirstPerson: true, HideEffects: true));
    firstPerson.Save("artifacts/model-first-person.png");
    for (int i = 0; i < bundle.Model.Textures.Length; i++) bundle.Model.Textures[i] = bundle.Model.Textures[i] with { Flags = bundle.Model.Textures[i].Flags & ~0x20 };
    using var before = CSNZWorkshop.Models.ModelRenderer.Render(bundle.Model, 900, 700, new(Sequence: 0, NexonEffects: false));
    before.Save("artifacts/additive-model-before.png");
    return;
}

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
    UnityChecks.Run(root, Check);
    TextureChecks.Run(root, Check);
    WeaponChecks.Run(root, Check);
    MediaChecks.Run(root, Check);
    AnalysisChecks.Run(root, Check);
    WorkflowChecks.Run(root, Check);
    EnhancementChecks.Run(root, Check);
    Console.WriteLine($"{passed} comprobaciones correctas.");
}
finally
{
    // Only remove the unique temporary directory created by this test process.
    Directory.Delete(root, true);
}
