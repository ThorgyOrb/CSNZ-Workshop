using CSNZWorkshop;
using CSNZWorkshop.Models;
using CSNZWorkshop.Archives;
using System.Numerics;

static class EnhancementChecks
{
    static void Assert(bool condition) { if (!condition) throw new Exception("Enhancement assertion failed"); }
    static int Changed(Bitmap image) => Enumerable.Range(0, image.Height).Sum(y => Enumerable.Range(0, image.Width).Count(x => image.GetPixel(x, y).ToArgb() != unchecked((int)0xff202833)));
    public static void Run(string root, Action<string, Action> check)
    {
        check("Desvanecido: transición continua y brillo limitado al grupo elegido", () =>
        {
            Assert(EffectPreview.Fade(0, 900) == 0 && Math.Abs(EffectPreview.Fade(450, 900) - 1) < .0001f);
            Assert(EffectPreview.Fade(899, 900) < .0001f && EffectPreview.Fade(901, 900) < .0001f);
            var model = StudioModel.Parse(Synthetic.Model());
            var original = model.Textures[0];
            model.Textures[0] = original with { Flags = 0x24, Pixels = Enumerable.Repeat(unchecked((int)0xff402010), original.Pixels.Length).ToArray() };
            using var invisible = ModelRenderer.Render(model, 160, 160, new(0, 0, FadingGroups: [0], EffectIntensity: 0));
            using var half = ModelRenderer.Render(model, 160, 160, new(0, 0, FadingGroups: [0], EffectIntensity: .5f));
            using var full = ModelRenderer.Render(model, 160, 160, new(0, 0));
            using var otherGroup = ModelRenderer.Render(model, 160, 160, new(0, 0, FadingGroups: [1], EffectIntensity: 0));
            Assert(invisible.GetPixel(80, 80).ToArgb() == unchecked((int)0xff202833));
            Assert(half.GetPixel(80, 80).R == 64 && full.GetPixel(80, 80).R == 96);
            Assert(otherGroup.GetPixel(80, 80) == full.GetPixel(80, 80));
        });
        check("Efectos: alterna geometría visible sin cambiar variantes manuales", () =>
        {
            var bundle = ModelBundle.Load(Synthetic.Model(), "test.mdl", "test", _ => null);
            var part = bundle.Model.Bodies[0].Variants[0];
            bundle.Model.Bodies[0] = new("effect", [part, part with { Name = "lightning2" }, part with { Triangles = [] }]);
            int[] manual = [2];
            Assert(EffectPreview.Suggested(bundle.Model, bundle.Model.Bodies[0]));
            Assert(EffectPreview.Resolve(bundle.Model, manual, [0], 0, 150)[0] == 0);
            Assert(EffectPreview.Resolve(bundle.Model, manual, [0], 150, 150)[0] == 1);
            Assert(EffectPreview.Resolve(bundle.Model, manual, [0], 300, 150)[0] == 0 && manual[0] == 2);
            UiChecks.VerifyEffectCycle(bundle);
        });
        check("Encuadre principal: ignora efectos alejados y no cambia el MDL", () =>
        {
            byte[] original = Synthetic.Model(); var baseModel = StudioModel.Parse(original);
            var part = baseModel.Bodies[0].Variants[0];
            var effect = part with { Vertices = part.Vertices.Select(v => v + new Vector3(300, 0, 0)).ToArray(), Triangles = part.Triangles.Select(t => t with { Skin = 1 }).ToArray() };
            var model = new StudioModel { Data = baseModel.Data, Name = "effects", Bones = baseModel.Bones,
                Textures = [baseModel.Textures[0], baseModel.Textures[0] with { Name = "$0b_effect.bmp" }],
                Bodies = [baseModel.Bodies[0], new("effect", [effect])], Sequences = baseModel.Sequences, Skins = [[0, 1]], GroupNames = baseModel.GroupNames };
            using var all = ModelRenderer.Render(model, 200, 200, new(0, 0, HideEffects: true));
            using var focused = ModelRenderer.Render(model, 200, 200, new(0, 0, FocusGeometry: true, HideEffects: true));
            Assert(Changed(focused) > Changed(all) * 5);
            Assert(original.SequenceEqual(Synthetic.Model()) && model.Textures[1].Flags == baseModel.Textures[0].Flags);
            using var mesh = ModelRenderer.Render(model, 200, 200, new(0, 0, Wireframe: true, HideEffects: true, FocusGeometry: true));
            Assert(Changed(mesh) > 0);
        });
        check("Primera persona: FOV, perspectiva y recorte cercano", () =>
        {
            var model = StudioModel.Parse(Synthetic.Model()); var part = model.Bodies[0].Variants[0];
            model.Bodies[0].Variants[0] = part with { Vertices = part.Vertices.Select(v => new Vector3(30, v.X, v.Z)).ToArray() };
            using var narrow = ModelRenderer.Render(model, 200, 200, new(0, 0, FirstPerson: true, FieldOfView: 60));
            using var wide = ModelRenderer.Render(model, 200, 200, new(0, 0, FirstPerson: true, FieldOfView: 110));
            Assert(Changed(narrow) > Changed(wide) && Changed(wide) > 0);
            model.Bodies[0].Variants[0].Vertices[0] = new(-2, -10, -10);
            using var clipped = ModelRenderer.Render(model, 200, 200, new(0, 0, FirstPerson: true)); Assert(Changed(clipped) > 0);
        });
        byte[] raw = Synthetic.Model(); string package = ExtractionChecks.WriteNar(root, "enhancements.nar", "/cstrike/models/v_test.mdl", raw, raw.Length, 0);
        var entry = ArchiveIndex.Read(package).Single();
        check("Interfaz: diagnóstico de exportación se invalida al cambiar selección", () => UiChecks.VerifyExportDiagnostic(entry));
        check("Búsqueda: nombre exacto, prefijo y ruta conservan prioridades", () =>
        {
            Assert(ResourceSearch.Rank(entry, "v_test") == 0);
            Assert(ResourceSearch.Rank(entry, "v_") == 1);
            Assert(ResourceSearch.Rank(entry, "test") == 2);
            Assert(ResourceSearch.Rank(entry, "models") == 3);
        });
        check("Miniaturas: persisten y se invalidan al cambiar el paquete", () =>
        {
            string cache = Path.Combine(root, "thumbs"); var store = new ThumbnailStore(cache, [entry]);
            using var bitmap = new Bitmap(10, 10); bitmap.SetPixel(0, 0, Color.Red); store.Write(entry, bitmap);
            using var restored = new ThumbnailStore(cache, [entry]).Read(entry); Assert(restored?.GetPixel(0, 0).R == 255);
            File.SetLastWriteTimeUtc(package, DateTime.UtcNow.AddMinutes(1));
            Assert(new ThumbnailStore(cache, [entry]).Read(entry) is null);
            File.WriteAllText(Directory.GetFiles(cache).Single(), "broken"); Assert(store.Read(entry) is null);
        });
        check("Exportación: diagnóstico distingue adaptación y errores de lectura", () =>
        {
            var report = ExportDiagnostic.Analyze(entry, [entry], [entry], default);
            Assert(report.Status.Contains("prueba") || report.Status.Contains("revisión"));
            var missing = entry with { Paquete = Path.Combine(root, "absent.nar") };
            Assert(ExportDiagnostic.Analyze(missing, [missing], [missing], default).Status == "No se pudo verificar");
        });
        check("Exportación: detecta sonidos referenciados fuera de la selección", () =>
        {
            byte[] data = new byte[raw.Length + 76]; raw.CopyTo(data, 0);
            Synthetic.Put(data, 72, data.Length); Synthetic.Put(data, Synthetic.SeqAt + 48, 1); Synthetic.Put(data, Synthetic.SeqAt + 52, raw.Length);
            Synthetic.Put(data, raw.Length + 4, 5004); System.Text.Encoding.ASCII.GetBytes("weapons/test.wav").CopyTo(data, raw.Length + 12);
            string modelPackage = ExtractionChecks.WriteNar(root, "dependency-model.nar", "/cstrike/models/v_test.mdl", data, data.Length, 0);
            var model = ArchiveIndex.Read(modelPackage).Single();
            var sound = entry with { Ruta = "/cstrike/sound/weapons/test.wav" };
            Assert(ExportDiagnostic.Analyze(model, [model], [model, sound], default).Status == "Faltan dependencias en la selección");
            Assert(ExportDiagnostic.Analyze(model, [model, sound], [model, sound], default).Status != "Faltan dependencias en la selección");
        });
    }
}
