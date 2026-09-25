using CSNZWorkshop;
using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;

static class AnalysisChecks
{
    static byte[] WithSound(int code = 5004, int frame = 2)
    {
        byte[] original = Synthetic.Model(); byte[] data = new byte[original.Length + 76]; original.CopyTo(data, 0);
        Synthetic.Put(data, 72, data.Length);
        Synthetic.Put(data, Synthetic.SeqAt + 48, 1); Synthetic.Put(data, Synthetic.SeqAt + 52, original.Length);
        Synthetic.Put(data, original.Length, frame); Synthetic.Put(data, original.Length + 4, code);
        System.Text.Encoding.ASCII.GetBytes("weapons/ak47_fire.wav").CopyTo(data, original.Length + 12); return data;
    }

    public static void Run(string root, Action<string, Action> check)
    {
        ResourceEntry Entry(string path, string package = "analysis.nar") => new(path, Path.Combine(root, package), "NAR", 1, 1, 8, 0);
        var source = Entry("/cstrike/models/v_ak47.mdl");
        var sound = Entry("/cstrike/sound/weapons/ak47_fire.wav");
        var variant = Entry("/cstrike/models/p_ak47.mdl");
        var sprite = Entry("/cstrike/sprites/ak47_flash.spr");
        var unrelated = Entry("/cstrike/sound/weapons/ak470_fire.wav");
        ModelBundle Bundle(byte[]? data = null) => ModelBundle.Load(data ?? WithSound(), "v_ak47.mdl", source.Paquete + " :: " + source.Ruta, _ => null);
        check("Relacionados: evento de sonido confirmado y sugerencias separadas", () =>
        {
            var bundle = Bundle(); Assert(bundle.Model.Events.Single().Code == 5004);
            var result = RelatedResources.Find(source, bundle, [source, sound, variant, sprite, unrelated]);
            Assert(result.Items.Single(i => i.Entry == sound).Reference);
            Assert(result.Items.Where(i => i.Entry == variant || i.Entry == sprite).All(i => !i.Reference));
            Assert(result.Items.All(i => i.Entry != unrelated && i.Entry != source));
            var unknown = RelatedResources.Find(source, Bundle(WithSound(9999)), [sound]);
            Assert(unknown.Items.All(i => !i.Reference));
        });
        check("Relacionados: faltantes, duplicados y mayúsculas no eligen versiones silenciosamente", () =>
        {
            var missing = RelatedResources.Find(source, Bundle(), [source]);
            Assert(missing.Items.Any(i => i.Reference && i.Entry is null));
            var second = sound with { Paquete = Path.Combine(root, "otro.nar"), Ruta = "\\cstrike\\SOUND\\weapons\\AK47_FIRE.WAV" };
            var multiple = RelatedResources.Find(source, Bundle(), [sound, second]);
            Assert(multiple.Items.Count(i => i.Reference && i.Entry is not null) == 2);
            Assert(CompatibilityReport.Analyze(Bundle(), multiple).Findings.Any(f => f.Area == "Origen ambiguo"));
        });
        check("Relacionados: compañeros mantienen preferencia del mismo paquete", () =>
        {
            var bundle = Bundle(Synthetic.Model(true));
            var companion = Entry("/cstrike/models/v_ak47T.mdl");
            var other = companion with { Paquete = Path.Combine(root, "otra-version.nar") };
            var result = RelatedResources.Find(source, bundle, [companion, other]);
            Assert(result.Items.Single(i => i.Reference).Entry == companion);
        });
        check("Relacionados: sugerencias acotadas y cancelación", () =>
        {
            var library = Enumerable.Range(0, 250).Select(i => Entry($"/cstrike/sprites/ak47_{i}.spr")).ToArray();
            var result = RelatedResources.Find(source, Bundle(), library);
            Assert(result.SuggestionsTruncated && result.Items.Count(i => !i.Reference) == 200);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { RelatedResources.Find(source, Bundle(), library, canceled.Token); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Cancellation ignored");
        });
        check("Diagnóstico: eventos fuera de rango, materiales y poses corruptas", () =>
        {
            byte[] data = WithSound(frame: 99); Synthetic.Put(data, Synthetic.TextureAt + 64, 256);
            data[Synthetic.RleAt] = 0;
            var bundle = Bundle(data); var report = CompatibilityReport.Analyze(bundle, validateAnimation: true);
            Assert(report.Findings.Any(f => f.Area == "Evento fuera de animación"));
            Assert(report.Findings.Any(f => f.Area == "Materiales"));
            Assert(report.Findings.Any(f => f.Area == "Datos de animación"));
        });
        check("Diagnóstico: límites por submodelo y validación sin certificar compatibilidad", () =>
        {
            var bundle = Bundle(Synthetic.Model());
            var part = bundle.Model.Bodies[0].Variants[0];
            bundle.Model.Bodies[0].Variants[0] = part with { Vertices = new System.Numerics.Vector3[2049] };
            Assert(CompatibilityReport.Analyze(bundle).Findings.Any(f => f.Area.StartsWith("Vértices")));
            bundle.Model.Bodies[0].Variants[0] = part;
            var report = CompatibilityReport.Analyze(bundle, new([], false), true);
            Assert(report.Findings.Any(f => f.Level == "Comprobado"));
            Assert(report.Findings.Any(f => f.Level == "Pendiente" && f.Area == "Uso en CS 1.6"));
            Assert(report.Status.Contains("pendiente de prueba"));
        });
        check("Interfaz: análisis y selección explícita de relacionados para colección", () =>
            UiChecks.VerifyAnalysis(Bundle(), source, [source, sound, variant, sprite]));
        check("Interfaz: relacionados previsualiza modelos, WAV, MP3 y sprites", () =>
        {
            ResourceEntry Pack(string file, string path, byte[] data) => ArchiveIndex.Read(
                ExtractionChecks.WriteNar(root, file, path, data, data.Length, 0)).Single();
            var model = Pack("related-model.nar", source.Ruta, WithSound());
            var wav = Pack("related-wav.nar", sound.Ruta, MediaChecks.Wave());
            var mp3 = Pack("related-mp3.nar", "/cstrike/sound/weapons/ak47_music.mp3", MediaChecks.Mp3());
            var spr = Pack("related-sprite.nar", sprite.Ruta, MediaChecks.Sprite());
            UiChecks.VerifyRelatedPreview(ModelBundle.FromArchive(model, [model]), model, [model, wav, mp3, spr]);
        });
    }

    static void Assert(bool condition) { if (!condition) throw new Exception("Analysis assertion failed"); }
}
