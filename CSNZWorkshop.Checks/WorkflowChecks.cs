using CSNZWorkshop;
using CSNZWorkshop.Archives;
using CSNZWorkshop.Media;
using CSNZWorkshop.Models;
using System.Numerics;

static class WorkflowChecks
{
    public static byte[] Tga()
    {
        byte[] data = new byte[30]; data[2] = 2; data[12] = 2; data[14] = 2; data[16] = 24; data[17] = 32;
        for (int i = 18; i < data.Length; i += 3) data[i + 2] = 255; return data;
    }
    public static byte[] Dds()
    {
        byte[] data = new byte[136];
        Synthetic.Put(data, 0, 0x20534444); Synthetic.Put(data, 4, 124); Synthetic.Put(data, 8, 0x81007);
        Synthetic.Put(data, 12, 4); Synthetic.Put(data, 16, 4); Synthetic.Put(data, 20, 8); Synthetic.Put(data, 28, 1);
        Synthetic.Put(data, 76, 32); Synthetic.Put(data, 80, 4); Synthetic.Put(data, 84, 0x31545844); Synthetic.Put(data, 108, 0x1000);
        data[128] = 0; data[129] = 248; data[130] = 224; data[131] = 7; return data;
    }
    public static byte[] Wad()
    {
        const int size = 8 + 4 + 2 + 768, table = 12 + size;
        byte[] data = new byte[table + 32];
        Synthetic.Put(data, 0, 0x33444157); Synthetic.Put(data, 4, 1); Synthetic.Put(data, 8, table);
        Synthetic.Put(data, 12, 2); Synthetic.Put(data, 16, 2);
        data[20] = 1; data[21] = 255; data[22] = 1; data[23] = 1; Synthetic.PutShort(data, 24, 256);
        data[26 + 3] = 255;
        Synthetic.Put(data, table, 12); Synthetic.Put(data, table + 4, size); Synthetic.Put(data, table + 8, size); data[table + 12] = 0x42;
        System.Text.Encoding.ASCII.GetBytes("{test").CopyTo(data, table + 16); return data;
    }
    public static void Run(string root, Action<string, Action> check)
    {
        check("Sesión: preferencias, favoritos y búsquedas persisten; JSON dañado se recupera", () =>
        {
            string location = Path.Combine(root, "session"); var session = new SessionStore(location);
            session.State.GameFolder = root; session.State.Dark = true; session.State.Category = 3; session.State.Destination = root;
            session.State.Favorites.Add("sample"); session.RememberSearch("AK47"); session.RememberSearch("ak47"); Assert(session.Save());
            var reopened = new SessionStore(location); Assert(reopened.State.Dark && reopened.State.Category == 3 && reopened.State.Favorites.Count == 1 && reopened.State.RecentSearches.Count == 1);
            File.WriteAllText(Path.Combine(location, "session.json"), "{"); Assert(new SessionStore(location).Warning is not null);
        });
        byte[] raw = MediaChecks.Wave();
        string package = ExtractionChecks.WriteNar(root, "cached.nar", "/cstrike/sound/a.wav", raw, raw.Length, 0);
        check("Caché: reutiliza índices sin perder CRC y revalida al extraer", () =>
        {
            string cache = Path.Combine(root, "cache");
            var first = CachedIndex.Build([package], cache); var second = CachedIndex.Build([package], cache);
            Assert(first.Updated == 1 && second.Reused == 1 && second.Index.Recursos[0].StoredCrc is not null);
            Assert(Extraction.ReadResource(second.Index.Recursos[0]).SequenceEqual(raw));
            File.SetLastWriteTimeUtc(package, DateTime.UtcNow.AddSeconds(3));
            Assert(CachedIndex.Build([package], cache).Updated == 1);
            Assert(CachedIndex.Build([package], cache, true).Updated == 1);
            File.WriteAllText(Directory.GetFiles(cache).Single(), "{}"); Assert(CachedIndex.Build([package], cache).Updated == 1);
            Assert(CachedIndex.Build([Path.Combine(root, "missing.nar")], cache).Index.Errores.Count == 1);
        });
        check("Exportación para mod: organiza rutas, omite existentes y rechaza colisiones", () =>
        {
            var entry = ArchiveIndex.Read(package).Single(); string output = Path.Combine(root, "mod-output");
            var report = Extraction.Run([entry], output, organizeForMod: true);
            Assert(report.Extraidos == 1 && File.ReadAllBytes(Path.Combine(output, "sound", "a.wav")).SequenceEqual(raw));
            Assert(Extraction.Run([entry], output, organizeForMod: true).Omitidos == 1);
            Reject(() => Extraction.Run([entry, entry with { Paquete = Path.Combine(root, "other.nar") }], output, organizeForMod: true));
            Reject(() => ModExport.Target(output, entry with { Ruta = "/cstrike/models/../evil.mdl" }));
            Reject(() => ModExport.Target(output, entry with { Ruta = "/cstrike/models/CON.mdl" }));
        });
        check("Colecciones: reordenar y reubicar verifica referencias sin perder el orden", () =>
        {
            var entry = ArchiveIndex.Read(package).Single(); var other = entry with { Ruta = "/cstrike/sound/b.wav" };
            var collection = new ResourceCollection(); collection.Add([entry, other]); collection.Move(collection.Resources[1], -1);
            Assert(collection.Resources[0].Resource == other.Ruta);
            string replacement = Path.Combine(root, "new.nar");
            Reject(() => collection.RelocatePackage(package, replacement, [entry with { Paquete = replacement }]));
            Assert(collection.Resources.All(r => r.Package == package));
            collection.RelocatePackage(package, replacement, [entry with { Paquete = replacement }, other with { Paquete = replacement }]);
            Assert(collection.Resources.All(r => r.Package == replacement) && collection.Resources[0].Resource == other.Ruta);
        });
        var left = ModelBundle.Load(Synthetic.Model(), "left.mdl", package, _ => null);
        var right = ModelBundle.Load(Synthetic.Model(), "right.mdl", package, _ => null);
        check("Visor: proporciones, desplazamiento, fondo y huesos", () =>
        {
            using var wide = ModelRenderer.Render(left.Model, 2000, 500, new());
            Assert(wide.Width == 1200 && wide.Height == 300);
            using var centered = ModelRenderer.Render(left.Model, 200, 200, new(0, 0, Background: unchecked((int)0xffeeeeee)));
            using var shifted = ModelRenderer.Render(left.Model, 200, 200, new(0, 0, PanX: .2f, Background: unchecked((int)0xffeeeeee)));
            Assert(centered.GetPixel(0, 0).ToArgb() == unchecked((int)0xffeeeeee));
            for (int y = 0; y < 200; y++) for (int x = 0; x < 160; x++) Assert(centered.GetPixel(x, y) == shifted.GetPixel(x + 40, y));
            using var bones = ModelRenderer.Render(left.Model, 200, 200, new(0, 0, ShowBones: true, Background: unchecked((int)0xffeeeeee)));
            Assert(Enumerable.Range(0, 200).Any(y => Enumerable.Range(0, 200).Any(x => bones.GetPixel(x, y) != centered.GetPixel(x, y))));
        });
        check("Interfaz: cámara, huesos y reproducción desde el fotograma elegido", () => UiChecks.VerifyViewerControls(left));
        check("Comparación: escala común, índices, huesos y attachments", () =>
        {
            var small = ModelComparison.Frame(left.Model, right.Model, -1, -1);
            var part = right.Model.Bodies[0].Variants[0]; right.Model.Bodies[0].Variants[0] = part with { Vertices = part.Vertices.Select(v => v * 3).ToArray() };
            var large = ModelComparison.Frame(left.Model, right.Model, -1, -1); Assert(large.Radius > small.Radius * 2);
            var rows = ModelComparison.Rows(left, right); Assert(rows.Any(r => r[0] == "Secuencia #0") && rows.Any(r => r[0] == "Hueso #0") && rows.Any(r => r[0] == "Hitbox #0"));
        });
        check("Texturas: TGA y DDS DXT1 conservan dimensiones y colores", () =>
        {
            using var tga = TextureCodec.Decode(Tga(), ".tga"); using var dds = TextureCodec.Decode(Dds(), ".dds");
            Assert(tga.Width == 2 && tga.Height == 2 && tga.GetPixel(0, 0).R == 255);
            Assert(dds.Width == 4 && dds.Height == 4 && dds.GetPixel(0, 0).R == 255);
            byte[] broken = Tga(); Synthetic.PutShort(broken, 12, 5000); Reject(() => TextureCodec.Decode(broken, ".tga"));
        });
        check("WAD3: paleta, transparencia y rangos truncados", () =>
        {
            var wad = new WadArchive(Wad()); using var texture = wad.Render(0);
            Assert(texture.Width == 2 && texture.GetPixel(0, 0).R == 255 && texture.GetPixel(1, 0).A == 0);
            byte[] data = Wad(); for (int n = 0; n < data.Length; n++) Reject(() => new WadArchive(data[..n]));
        });
        check("Interfaz: comparador dibuja ambos modelos", () => UiChecks.VerifyComparison(left, right));
        var texturePackages = new[] { ("test.tga", Tga()), ("test.dds", Dds()), ("test.wad", Wad()) }
            .Select((pair, i) => ExtractionChecks.WriteNar(root, "textures" + i + ".nar", "/cstrike/gfx/" + pair.Item1, pair.Item2, pair.Item2.Length, 0)).ToArray();
        check("Interfaz: vistas de texturas y selector WAD", () => UiChecks.VerifyTextures(texturePackages));
        check("Interfaz: búsqueda global encuentra recursos fuera del paquete abierto", () => UiChecks.VerifyGlobalSearch(texturePackages));
    }
    static void Assert(bool condition) { if (!condition) throw new Exception("Workflow assertion failed"); }
    static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { return; }
        throw new Exception("Unsafe input accepted");
    }
}
