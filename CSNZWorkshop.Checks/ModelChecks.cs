using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;

static class ModelChecks
{
    // Same keys as ModelCrypt: the fixtures encrypt with them to prove the decoder recovers the original bytes.
    const string Key20 = "32A621E0AB6BF42C93C6F196FB387568BA701386E0B371F4E39B07220CFE883A";
    const string Key21 = "227A196F7B867DE08CC6F196FB387568887A788678866770D991073A1474FE22";

    public static void Run(string root, string? game, Action<string, Action> check)
    {
        byte[] valid = Synthetic.Model();
        string origin = Path.Combine(root, "sintetico.nar");
        check("MDL sintético: huesos, textura, malla y animación", () =>
        {
            var model = StudioModel.Parse(valid);
            Assert(model.Bones.Length == 2 && model.Bones[1].Parent == 0 && model.Textures.Length == 1);
            Assert(model.Textures[0].Width == 4 && model.Textures[0].Pixels.Length == 16);
            Assert(model.Skins.Length == 1 && model.Skins[0].Length == 1 && model.GroupNames.Length == 2);
            Assert(model.Sequences.Length == 1 && model.Sequences[0].Frames == 8 && model.Sequences[0].Fps == 30);
            var part = model.Bodies.Single().Variants.Single();
            Assert(part.Vertices.Length == 4 && part.Triangles.Length == 2 && part.BoneIndices.All(b => b == 0));
        });
        check("Pose: los bloques RLE cambian con el fotograma", () =>
        {
            var model = StudioModel.Parse(valid);
            Assert(Math.Abs(model.Pose()[0].Translation.X) < 0.001f);
            Assert(Math.Abs(model.Pose(0, 3)[0].Translation.X) < 0.001f);
            Assert(Math.Abs(model.Pose(0, 5)[0].Translation.X - 5) < 0.001f);
            // The child bone inherits its parent's animated position.
            Assert(Math.Abs(model.Pose(0, 5)[1].Translation.X - 5) < 0.001f && Math.Abs(model.Pose(0, 5)[1].Translation.Z - 20) < 0.001f);
        });
        check("Canal de animación RLE inválido", () =>
        {
            foreach (var (valids, total) in new[] { (0, 4), (1, 0), (5, 1) })
            {
                byte[] copy = Synthetic.Model(); copy[Synthetic.RleAt] = (byte)valids; copy[Synthetic.RleAt + 1] = (byte)total;
                Reject(() => StudioModel.Parse(copy).Pose(0, 2));
            }
        });
        check("Render: dibuja el modelo, la malla y la animación", () =>
        {
            var model = StudioModel.Parse(valid);
            using var bitmap = ModelRenderer.Render(model, 200, 150, new());
            Assert(bitmap.Width == 200 && bitmap.Height == 150 && Drawn(bitmap) > 200);
            using var wire = ModelRenderer.Render(model, 200, 150, new(Wireframe: true));
            Assert(Drawn(wire) > 0 && Fingerprint(wire) != Fingerprint(bitmap));
            using var rest = ModelRenderer.Render(model, 200, 150, new(Sequence: 0, Frame: 0));
            using var moved = ModelRenderer.Render(model, 200, 150, new(Sequence: 0, Frame: 5));
            Assert(Drawn(moved) > 200 && Fingerprint(rest) != Fingerprint(moved));
            using var texture = ModelRenderer.TextureBitmap(model.Textures[0]);
            Assert(texture.Width == 4 && texture.Height == 4 && texture.GetPixel(1, 0).ToArgb() == unchecked((int)0xff01fe80));
        });
        check("Render: una animación que desplaza el hueso raíz sigue en cuadro", () =>
        {
            // The model is 20 units wide and the animation moves it 60: without sequence framing it leaves the view.
            byte[] lejos = Synthetic.Model(); Synthetic.PutShort(lejos, Synthetic.RleAt + 6, 60);
            var model = StudioModel.Parse(lejos);
            using var first = ModelRenderer.Render(model, 200, 150, new(Sequence: 0, Frame: 0));
            using var last = ModelRenderer.Render(model, 200, 150, new(Sequence: 0, Frame: 7));
            Assert(Drawn(first) > 100 && Drawn(last) > 100);
            // The framing covers the whole sequence, so playing it does not change the zoom.
            Assert(Math.Abs(Drawn(first) - Drawn(last)) < Drawn(first) / 4);
            Assert(Fingerprint(first) != Fingerprint(last));
        });
        check("Un grupo de animación abierto como modelo se explica, no se descifra", () =>
        {
            try { ModelCrypt.Decode(Synthetic.AnimationGroup(9)); }
            catch (NotSupportedException ex) { Assert(ex.Message.Contains("IDSQ")); return; }
            throw new Exception("Animation group accepted as a model");
        });
        check("Render: tamaños extremos se recortan sin fallar", () =>
        {
            var model = StudioModel.Parse(valid);
            foreach (var (width, height) in new[] { (1, 1), (4000, 4000) })
            { using var bitmap = ModelRenderer.Render(model, width, height, new(Zoom: 40)); Assert(bitmap.Width is >= 1 and <= 1200 && bitmap.Height is >= 1 and <= 900 && bitmap.Width == bitmap.Height); }
        });
        check("MDL rechazado: firma, versión, longitud y tablas fuera de rango", () =>
        {
            (string Reason, Action<byte[]> Damage)[] damages = [
                ("firma", b => Synthetic.Put(b, 0, 0x12345678)),
                ("versión", b => Synthetic.Put(b, 4, 11)),
                ("longitud", b => Synthetic.Put(b, 72, Synthetic.Size + 1)),
                ("padre de hueso", b => Synthetic.Put(b, Synthetic.BoneAt + 32, 1)),
                ("hueso de hitbox", b => Synthetic.Put(b, Synthetic.HitboxAt, 7)),
                ("coordenada de hueso", b => Synthetic.PutFloat(b, Synthetic.BoneAt + 64, float.NaN)),
                ("tabla de huesos", b => Synthetic.Put(b, 144, Synthetic.Size - 8)),
                ("textura sin dimensiones", b => Synthetic.Put(b, Synthetic.TextureAt + 68, 0)),
                ("textura fuera de límites", b => Synthetic.Put(b, Synthetic.TextureAt + 72, 5000)),
                ("píxeles fuera del archivo", b => Synthetic.Put(b, Synthetic.TextureAt + 76, Synthetic.Size - 16)),
                ("skin fuera de la tabla", b => Synthetic.PutShort(b, Synthetic.SkinAt, 4)),
                ("material de malla", b => Synthetic.Put(b, Synthetic.MeshAt + 8, 3)),
                ("triángulos declarados", b => Synthetic.Put(b, Synthetic.MeshAt, 5)),
                ("tira demasiado corta", b => Synthetic.PutShort(b, Synthetic.TriAt, 2)),
                ("vértice de triángulo", b => Synthetic.PutShort(b, Synthetic.TriAt + 2, 9)),
                ("normal de triángulo", b => Synthetic.PutShort(b, Synthetic.TriAt + 4, 9)),
                ("hueso de vértice", b => b[Synthetic.VertInfoAt] = 9),
                ("fotogramas de animación", b => Synthetic.Put(b, Synthetic.SeqAt + 56, 0)),
                ("fps de animación", b => Synthetic.PutFloat(b, Synthetic.SeqAt + 32, 5000)),
                ("grupo de animación inexistente", b => Synthetic.Put(b, Synthetic.SeqAt + 156, 5)),
                ("tabla de animación", b => Synthetic.Put(b, Synthetic.SeqAt + 124, Synthetic.Size - 4))];
            foreach (var damage in damages)
            {
                byte[] copy = Synthetic.Model(); damage.Damage(copy);
                try { StudioModel.Parse(copy); }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or OverflowException) { continue; }
                throw new Exception("Damaged model accepted: " + damage.Reason);
            }
        });
        check("MDL truncado en cada byte", () =>
        {
            for (int length = 0; length < valid.Length; length++) Reject(() => StudioModel.Parse(valid[..length]));
        });
        check("Compañero T.mdl: aporta texturas y tabla de materiales", () =>
        {
            var bundle = ModelBundle.Load(Synthetic.Model(true), "sintetico.mdl", origin,
                name => name == "sinteticoT.mdl" ? Synthetic.TextureCompanion() : null);
            Assert(bundle.Model.Textures.Length == 1 && bundle.Model.Skins.Length == 1);
            Assert(bundle.Companions.Count == 1 && bundle.Missing.Count == 0);
            using var bitmap = ModelRenderer.Render(bundle.Model, 200, 150, new());
            Assert(Drawn(bitmap) > 200);
        });
        check("Sin compañero: anota la dependencia y avisa en el diagnóstico", () =>
        {
            var bundle = ModelBundle.Load(Synthetic.Model(true), "sintetico.mdl", origin, _ => null);
            Assert(bundle.Missing.Contains("sinteticoT.mdl") && bundle.Model.Textures.Length == 0);
            Assert(bundle.Describe().Contains("Falta dependencia: sinteticoT.mdl"));
            Assert(bundle.Model.Warnings.Any(w => w.Contains("sin textura")));
            // Untextured meshes still render, so the viewer shows geometry while the companion is missing.
            using var bitmap = ModelRenderer.Render(bundle.Model, 200, 150, new());
            Assert(Drawn(bitmap) > 200);
        });
        check("Grupo de animación externo: valida IDSQ y aporta sus fotogramas", () =>
        {
            var bundle = ModelBundle.Load(External(), "sintetico.mdl", origin,
                name => name == "sintetico01.mdl" ? Synthetic.AnimationGroup(9) : null);
            Assert(bundle.Companions.Count == 1 && bundle.Missing.Count == 0);
            Assert(Math.Abs(bundle.Model.Pose(0, 1)[0].Translation.X) < 0.001f);
            Assert(Math.Abs(bundle.Model.Pose(0, 5)[0].Translation.X - 9) < 0.001f);
        });
        check("Grupo externo ausente, con otra firma o con longitud falsa", () =>
        {
            var incomplete = ModelBundle.Load(External(), "sintetico.mdl", origin, _ => null);
            Assert(incomplete.Missing.Contains("sintetico01.mdl"));
            Reject(() => incomplete.Model.Pose(0, 0));
            Reject(() => ModelBundle.Load(External(), "sintetico.mdl", origin, _ => Synthetic.Model()));
            Reject(() => ModelBundle.Load(External(), "sintetico.mdl", origin, _ =>
            { byte[] group = Synthetic.AnimationGroup(9); Synthetic.Put(group, 72, 12); return group; }));
        });
        check("Preparar copia: escribe el modelo, su compañero y el informe", () =>
        {
            var bundle = ModelBundle.Load(External(), "sintetico.mdl", origin,
                name => name == "sintetico01.mdl" ? Synthetic.AnimationGroup(9) : null);
            string folder = bundle.Prepare(Path.Combine(root, "preparado"));
            Assert(File.ReadAllBytes(Path.Combine(folder, "sintetico.mdl")).Length == Synthetic.Size);
            Assert(File.Exists(Path.Combine(folder, "sintetico01.mdl")));
            string report = File.ReadAllText(Path.Combine(folder, "preparacion.json"));
            Assert(report.Contains("SHA256") && report.Contains("Pendiente") && report.Contains("no instalado"));
            Assert(Directory.GetFiles(folder).Length == 3);
        });
        check("Preparar: rechaza dependencias que faltan y respeta la cancelación", () =>
        {
            string destination = Path.Combine(root, "preparado-rechazado");
            Reject(() => ModelBundle.Load(External(), "sintetico.mdl", origin, _ => null).Prepare(destination));
            var bundle = ModelBundle.Load(External(), "sintetico.mdl", origin,
                name => name == "sintetico01.mdl" ? Synthetic.AnimationGroup(9) : null);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { bundle.Prepare(destination, cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Assert(!Directory.Exists(destination) || Directory.GetDirectories(destination).Length == 0);
        });
        check("MDL v20: ICE descifra texturas y vértices y restaura la versión 10", () =>
        {
            byte[] encrypted = Encrypted(20);
            Assert(!encrypted.SequenceEqual(valid));
            byte[] decoded = ModelCrypt.Decode(encrypted);
            Assert(decoded.SequenceEqual(valid));
            Assert(StudioModel.Parse(decoded).Textures[0].Pixels.Length == 16);
        });
        check("MDL v21 usa su propia clave", () =>
        {
            Assert(ModelCrypt.Decode(Encrypted(21)).SequenceEqual(valid));
            // Decoding v21 data with the v20 key must not reproduce the original bytes by accident.
            byte[] wrong = Encrypted(20); Synthetic.Put(wrong, 4, 21);
            Assert(!ModelCrypt.Decode(wrong).SequenceEqual(valid));
        });
        check("MDL cifrado con bloques solapados, texturas fuera de límites o truncado", () =>
        {
            byte[] overlap = Encrypted(20); Synthetic.Put(overlap, Synthetic.SubAt + 88, Synthetic.PixelsAt + 8);
            Reject(() => ModelCrypt.Decode(overlap));
            byte[] huge = Encrypted(20); Synthetic.Put(huge, Synthetic.TextureAt + 72, 9000);
            Reject(() => ModelCrypt.Decode(huge));
            Reject(() => ModelCrypt.Decode(Encrypted(20)[..1000]));
        });
        check("Versiones desconocidas no se renumeran a 10", () =>
        {
            foreach (int version in new[] { 11, 22, 44 })
            { byte[] copy = Synthetic.Model(); Synthetic.Put(copy, 4, version); Reject(() => ModelCrypt.Decode(copy)); }
            Assert(ReferenceEquals(ModelCrypt.Decode(valid), valid));
        });
        check("Interfaz: el visor dibuja el modelo y lista sus animaciones", () =>
            UiChecks.VerifyModel(ModelBundle.Load(valid, "sintetico.mdl", origin, _ => null)));
        check("Interfaz: la biblioteca abre el visor solo para un .mdl", () =>
        {
            string package = ExtractionChecks.WriteNar(root, "modelo.nar", "/cstrike/models/sintetico.mdl", valid, valid.Length, 0);
            var entry = ArchiveIndex.Read(package).Single();
            var bundle = ModelBundle.FromArchive(entry, [entry]);
            Assert(bundle.Model.Bodies.Single().Variants.Single().Triangles.Length == 2);
            UiChecks.VerifyLibraryModel(package);
        });
        check("Informe por lotes: versiones, fallos, dependencias y paquetes ilegibles", () =>
        {
            byte[] roto = Synthetic.Model(); Synthetic.Put(roto, 0, 0x12345678);
            string[] packages = [
                ExtractionChecks.WriteNar(root, "informe-ok.nar", "/cstrike/models/sintetico.mdl", valid, valid.Length, 0),
                ExtractionChecks.WriteNar(root, "informe-roto.nar", "/cstrike/models/roto.mdl", roto, roto.Length, 0),
                ExtractionChecks.WriteNar(root, "informe-cifrado.nar", "/cstrike/models/cifrado.mdl", Encrypted(20), Synthetic.Size, 0),
                ExtractionChecks.WriteNar(root, "informe-externo.nar", "/cstrike/models/externo.mdl", External(), Synthetic.Size, 0),
                Path.Combine(root, "no-existe.nar")];
            var summary = ModelReport.Build(packages);
            Assert(summary.Revisados == 4 && summary.Leidos == 3 && summary.PaquetesLeidos == 4);
            Assert(summary.Versiones["v10"] == 2 && summary.Versiones["v20"] == 1);
            Assert(summary.Problemas.Count == 1 && summary.Problemas[0].Ruta == "/cstrike/models/roto.mdl");
            Assert(summary.Dependencias.Count == 1 && summary.Dependencias[0].Detalle.Contains("externo01.mdl"));
            Assert(summary.Paquetes.Count == 1 && summary.Paquetes[0].Paquete.EndsWith("no-existe.nar"));
            string path = Path.Combine(root, "informe-modelos.json");
            ModelReport.Export(path, summary);
            Assert(File.ReadAllText(path).Contains("Revisados") && File.ReadAllText(path).Contains("v20"));
        });
        check("Informe por lotes: la cancelación no deja el paquete abierto", () =>
        {
            string package = ExtractionChecks.WriteNar(root, "informe-cancelado.nar", "/cstrike/models/sintetico.mdl", valid, valid.Length, 0);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { ModelReport.Build([package], cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            using var reopened = new FileStream(package, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert(reopened.Length > 0);
        });
        if (game is not null) Integration(game, check);
    }

    static void Integration(string game, Action<string, Action> check)
    {
        check("Integración: modelos reales se leen, resuelven compañeros y se dibujan", () =>
        {
            var nar = ArchiveIndex.Read(Path.Combine(game, "cstrike.nar"));
            string[] paths = ["/cstrike/models/player/707/707.mdl", "/cstrike/models/v_ak47red.mdl",
                "/cstrike/models/ammo_box_set/w_556nato.mdl"];
            foreach (string path in paths)
            {
                var watch = Stopwatch.StartNew();
                var bundle = ModelBundle.FromArchive(nar.First(r => r.Ruta == path), nar);
                Assert(bundle.Model.Bodies.Length > 0 && bundle.Model.Sequences.Length > 0 && bundle.Missing.Count == 0);
                Assert(bundle.Model.Textures.Length > 0 && bundle.Model.Bodies.Any(b => b.Variants.Any(v => v.Triangles.Length > 0)));
                using var bitmap = ModelRenderer.Render(bundle.Model, 320, 240, new(Sequence: 0, Frame: 1));
                Assert(Drawn(bitmap) > 500);
                Console.WriteLine($"  {Path.GetFileName(path)}: v{bundle.Main.OriginalVersion}, {bundle.Model.Bones.Length} huesos, "
                    + $"{bundle.Model.Textures.Length} texturas, {bundle.Model.Sequences.Length} animaciones, "
                    + $"{bundle.Companions.Count} compañeros, {watch.ElapsedMilliseconds} ms");
            }
        });
        check("Integración: modelos cifrados reales v20 y v21 se descifran y dibujan", () =>
        {
            var nar = ArchiveIndex.Read(Path.Combine(game, "cstrike.nar"));
            foreach (var (path, version) in new[] { ("/cstrike/models/monster/dedi-bot_heavy.mdl", 20),
                ("/cstrike/models/monster/dedi-bonus_mon.mdl", 21) })
            {
                var bundle = ModelBundle.FromArchive(nar.First(r => r.Ruta == path), nar);
                Assert(bundle.Main.OriginalVersion == version && bundle.Main.Data.Length == bundle.Model.Data.Length);
                Assert(bundle.Model.Textures.Length > 0 && bundle.Model.Bodies.Sum(b => b.Variants.Sum(v => v.Triangles.Length)) > 50);
                using var bitmap = ModelRenderer.Render(bundle.Model, 320, 240, new(Sequence: 0, Frame: 1));
                Assert(Drawn(bitmap) > 500);
                Console.WriteLine($"  {Path.GetFileName(path)}: v{version} descifrado, {bundle.Model.Bones.Length} huesos, "
                    + $"{bundle.Model.Bodies.Sum(b => b.Variants.Sum(v => v.Vertices.Length))} vértices");
            }
        });
        check("Integración: modelo de un PAK con su textura incrustada", () =>
        {
            var pak = ArchiveIndex.Read(Path.Combine(game, "Packer", "common_00004.pak"));
            var bundle = ModelBundle.FromArchive(pak.First(r => r.Ruta == "lstrike/common/models/costume/buffclass22s2ct_face.mdl"), pak);
            Assert(bundle.Model.Textures.Length > 0 && bundle.Model.Bodies.Length > 0);
            using var bitmap = ModelRenderer.Render(bundle.Model, 320, 240, new());
            Assert(Drawn(bitmap) > 500);
        });
    }

    static byte[] External()
    {
        byte[] model = Synthetic.Model();
        Synthetic.Put(model, Synthetic.SeqAt + 156, 1);
        return model;
    }

    static byte[] Encrypted(int version)
    {
        byte[] data = Synthetic.Model();
        Synthetic.Put(data, 4, version);
        using var ice = new Ice(4);
        using var transform = ice.CreateEncryptor(Convert.FromHexString(version == 20 ? Key20 : Key21), []);
        foreach (var (offset, length) in new[] { (Synthetic.PixelsAt, 4 * 4 + 768), (Synthetic.VertAt, 4 * 12) })
            for (int done = 0; done < length; done += 1024)
            {
                int block = Math.Min(1024, length - done);
                if (block % 8 != 0) break;
                transform.TransformBlock(data, offset + done, block, data, offset + done);
            }
        return data;
    }

    static int[] Pixels(Bitmap bitmap)
    {
        var bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int[] pixels = new int[bitmap.Width * bitmap.Height];
        try { Marshal.Copy(bits.Scan0, pixels, 0, pixels.Length); }
        finally { bitmap.UnlockBits(bits); }
        return pixels;
    }

    static int Drawn(Bitmap bitmap) => Pixels(bitmap).Count(p => p != unchecked((int)0xff202833));
    static string Fingerprint(Bitmap bitmap) => string.Join(",", Pixels(bitmap).Select(p => p.ToString("X8")));

    static void Assert(bool condition) { if (!condition) throw new Exception("Model assertion failed"); }
    static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or OverflowException) { return; }
        throw new Exception("Invalid or unsupported model accepted");
    }
}
