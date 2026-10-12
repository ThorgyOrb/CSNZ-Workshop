using System.Text.Json;
using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;
using CSNZWorkshop.Media;
using CSNZ.UnityBridge;
using System.Security.Cryptography;
using System.Text;

static class TextureChecks
{
    const string ExternalName = "#004002test.bmp";
    static void Assert(bool value) { if (!value) throw new Exception("Texture assertion failed"); }
    static byte[] Placeholder(int flags = 0)
    {
        var bytes = Synthetic.Model();
        bytes.AsSpan(Synthetic.TextureAt, 64).Clear(); Encoding.ASCII.GetBytes(ExternalName).CopyTo(bytes, Synthetic.TextureAt);
        Synthetic.Put(bytes, Synthetic.TextureAt + 72, 1); Synthetic.Put(bytes, Synthetic.TextureAt + 64, flags);
        return bytes;
    }
    static byte[] Bmp(bool topDown = false)
    {
        byte[] bytes = new byte[54 + 1024 + 8];
        Synthetic.PutShort(bytes, 0, 0x4d42); Synthetic.Put(bytes, 2, bytes.Length); Synthetic.Put(bytes, 10, 1078);
        Synthetic.Put(bytes, 14, 40); Synthetic.Put(bytes, 18, 4); Synthetic.Put(bytes, 22, topDown ? -2 : 2);
        Synthetic.PutShort(bytes, 26, 1); Synthetic.PutShort(bytes, 28, 8); Synthetic.Put(bytes, 34, 8);
        for (int i = 0; i < 256; i++) { bytes[54 + i * 4] = (byte)i; bytes[55 + i * 4] = (byte)(255 - i); bytes[56 + i * 4] = (byte)((i + 30) % 256); }
        byte[] top = [0, 1, 2, 255], bottom = [4, 5, 6, 7];
        (topDown ? top : bottom).CopyTo(bytes, 1078); (topDown ? bottom : top).CopyTo(bytes, 1082);
        return bytes;
    }
    static int[] Pixels(System.Drawing.Bitmap bitmap)
    {
        var result = new int[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) result[y * bitmap.Width + x] = bitmap.GetPixel(x, y).ToArgb();
        return result;
    }
    public static void Run(string root, Action<string, Action> check)
    {
        check("Texturas externas: BMP con paleta, origen de filas y máscara por índice", () =>
        {
            byte[] bmp = Bmp(); var decoded = TextureCodec.DecodeModelBitmap(bmp, ExternalName, 0);
            using var input = new MemoryStream(bmp); using var independent = new System.Drawing.Bitmap(input);
            Assert(decoded.Pixels.SequenceEqual(Pixels(independent)));
            Assert(decoded.Pixels.SequenceEqual(TextureCodec.DecodeModelBitmap(Bmp(true), ExternalName, 0).Pixels));
            // Duplicate palette colors must not make a non-255 index transparent.
            bmp.AsSpan(54 + 255 * 4, 4).CopyTo(bmp.AsSpan(54 + 4, 4));
            var masked = TextureCodec.DecodeModelBitmap(bmp, ExternalName, 64);
            Assert(masked.Pixels[3] == 0 && (uint)masked.Pixels[1] >> 24 == 255);
            var malformed = bmp.ToArray(); Synthetic.Put(malformed, 10, 20);
            try { TextureCodec.DecodeModelBitmap(malformed, ExternalName, 0); throw new Exception("Overlapping BMP accepted"); } catch (InvalidDataException) { }
        });
        check("Texturas externas: sustituye relleno 4x1, conserva UV y exporta PNG reales", () =>
        {
            var bundle = ModelBundle.Load(Placeholder(), "test.mdl", Path.Combine(root, "test.mdl"), _ => null,
                name => new(name, Bmp(), 0, "external.bmp"));
            Assert(bundle.Missing.Count == 0 && bundle.ExternalTextures.Count == 1 && bundle.Model.Textures[0].Height == 2);
            Assert(!bundle.Model.Textures[0].ExternalPlaceholder && bundle.Model.Textures[0].Source == "external.bmp");
            var data = UnityModelExporter.Build(bundle.Model); Assert(data.meshes[0].uv[3] == -.5f);
            string folder = UnityModelExporter.Export(bundle, root);
            using var png = new System.Drawing.Bitmap(Path.Combine(folder, "textures", "texture_0000.png"));
            Assert(Pixels(png).SequenceEqual(bundle.Model.Textures[0].Pixels));
            var saved = JsonSerializer.Deserialize<ModelData>(File.ReadAllText(Path.Combine(folder, "model.json")), new JsonSerializerOptions { IncludeFields = true })!;
            Assert(saved.textures[0].source == "external.bmp" && saved.textures[0].height == 2);
            Assert(saved.textures[0].sha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, saved.textures[0].file)))));
            string prepared = bundle.Prepare(root); Assert(File.Exists(Path.Combine(prepared, "texture", ExternalName)));
            Assert(ModelBundle.FromFile(Path.Combine(prepared, "test.mdl")).Model.Textures[0].Pixels.SequenceEqual(bundle.Model.Textures[0].Pixels));
        });
        check("Texturas externas: resuelve otro paquete, manos compartidas y carpetas shop", () =>
        {
            string mdl = ExtractionChecks.WriteNar(root, "external-model.nar", "/dstrike/models/shop/s_weapon/test.mdl", Placeholder(), Synthetic.Size, 0);
            var bmpBytes = Bmp(); string bmp = ExtractionChecks.WriteNar(root, "external-texture.nar", "/dstrike/models/texture/" + ExternalName, bmpBytes, bmpBytes.Length, 0);
            var entry = ArchiveIndex.Read(mdl).Single(); var texture = ArchiveIndex.Read(bmp).Single();
            var bundle = ModelBundle.FromArchive(entry, [entry, texture]); Assert(bundle.Missing.Count == 0 && bundle.Model.Textures[0].Height == 2);
            Assert(RelatedResources.Find(entry, bundle, [entry, texture]).Items.Any(i => i.Reference && i.Entry == texture && i.Kind == "Textura externa BMP"));
            var shared = texture with { Ruta = "/cstrike/models/texture/" + ExternalName };
            Assert(ExternalModelTextures.Candidates(entry, ExternalName, [entry, shared]).Single() == shared);
            var fake = shared with { Ruta = "/other/models/texture/" + ExternalName, Paquete = "other.nar" };
            Assert(ExternalModelTextures.Candidates(entry, ExternalName, [shared, fake]).Length == 2);
            var ambiguous = ModelBundle.FromArchive(entry, [entry, shared, fake]); Assert(ambiguous.Missing.Contains("texture/" + ExternalName));
            try { UnityModelExporter.Export(ambiguous, root); throw new Exception("Unresolved texture exported"); } catch (InvalidDataException) { }
        });
        check("Texturas externas: respeta ámbito y no exporta rellenos sin resolver", () =>
        {
            var source = new ResourceEntry("/dstrike/models/v_test.mdl", "source.nar", "NAR", 0, 0, 0, 0);
            var scoped = source with { Ruta = "/dstrike/models/texture/" + ExternalName, Paquete = "other.nar" };
            var samePackageWrongRoot = source with { Ruta = "/estrike/models/texture/" + ExternalName };
            Assert(ExternalModelTextures.Candidates(source, ExternalName, [scoped, samePackageWrongRoot]).Single() == scoped);
            var missing = ModelBundle.Load(Placeholder(), "test.mdl", "test.mdl", _ => null);
            Assert(missing.Missing.Contains("texture/" + ExternalName));
            try { UnityModelExporter.Build(missing.Model); throw new Exception("Placeholder exported"); } catch (InvalidDataException) { }
            try { ExternalModelTextures.Filename("../wrong.bmp"); throw new Exception("Unsafe texture name accepted"); } catch (InvalidDataException) { }
        });
    }
    public static void VerifyAudit(string reportFile)
    {
        using var report = JsonDocument.Parse(File.ReadAllText(reportFile));
        string root = Path.GetDirectoryName(Path.GetFullPath(reportFile))!; int models = 0, textures = 0;
        foreach (var item in report.RootElement.EnumerateArray())
        {
            int id = item.GetProperty("id").GetInt32(); string path = item.GetProperty("Ruta").GetString()!;
            string folder = Path.Combine(root, "model_" + id.ToString("D3"));
            var bundle = ModelBundle.FromFile(Path.Combine(folder, Path.GetFileName(path.Replace('\\', '/'))));
            Assert(bundle.Missing.Count == 0);
            string unity = item.GetProperty("unityFolder").GetString()!;
            var data = JsonSerializer.Deserialize<ModelData>(File.ReadAllText(Path.Combine(unity, "model.json")), new JsonSerializerOptions { IncludeFields = true })!;
            foreach (var texture in data.textures)
            {
                var source = bundle.Model.Textures[texture.id];
                using var png = new System.Drawing.Bitmap(Path.Combine(unity, texture.file));
                Assert(texture.width == source.Width && texture.height == source.Height && Pixels(png).SequenceEqual(source.Pixels));
                Assert(texture.sha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(unity, texture.file)))));
                if (source.Source is not null)
                {
                    using var bmp = new System.Drawing.Bitmap(source.Source);
                    var independent = Pixels(bmp); Assert(bmp.Width == source.Width && bmp.Height == source.Height);
                    for (int pixel = 0; pixel < independent.Length; pixel++)
                        Assert(source.Pixels[pixel] == independent[pixel] || ((source.Flags & 64) != 0 && source.Pixels[pixel] == 0));
                }
                textures++;
            }
            foreach (var mesh in data.meshes)
                foreach (var submesh in mesh.submeshes)
                    Assert(submesh.texture == bundle.Model.Skins[mesh.skin][submesh.slot]);
            models++;
        }
        Console.WriteLine($"Verificados: {models} modelos, {textures} texturas; BMP originales, PNG, SHA256 y asignación de materiales coinciden.");
    }
    public static void Audit(string indexFile, string pattern, string destination)
    {
        var saved = JsonSerializer.Deserialize<ResourceIndex>(File.ReadAllText(indexFile))!;
        var index = ArchiveIndex.Build(saved.Recursos.Select(e => e.Paquete).Distinct(StringComparer.OrdinalIgnoreCase));
        var models = index.Recursos.Where(e => e.Extension == ".mdl" && e.Ruta.Contains(pattern, StringComparison.OrdinalIgnoreCase)).ToArray();
        Directory.CreateDirectory(destination); var results = new List<object>();
        for (int id = 0; id < models.Length; id++)
        {
            var entry = models[id]; string folder = Path.Combine(destination, "model_" + id.ToString("D3")); Directory.CreateDirectory(folder);
            try
            {
                var bundle = ModelBundle.FromArchive(entry, index.Recursos);
                File.WriteAllBytes(Path.Combine(folder, bundle.Main.Name), bundle.Main.Data);
                foreach (var companion in bundle.Companions) File.WriteAllBytes(Path.Combine(folder, companion.Name), companion.Data);
                var textures = new List<object>();
                for (int texture = 0; texture < bundle.Model.Textures.Length; texture++)
                {
                    var t = bundle.Model.Textures[texture];
                    if (t.Source is not null)
                    {
                        var externalAsset = bundle.ExternalTextures.Single(a => a.Name.Equals("texture/" + t.Name, StringComparison.OrdinalIgnoreCase));
                        using var bmpStream = new MemoryStream(externalAsset.Data);
                        using var independent = new System.Drawing.Bitmap(bmpStream);
                        Assert(independent.Width == t.Width && independent.Height == t.Height);
                        var originalPixels = Pixels(independent);
                        for (int pixel = 0; pixel < t.Pixels.Length; pixel++)
                            Assert(t.Pixels[pixel] == originalPixels[pixel] || ((t.Flags & 64) != 0 && t.Pixels[pixel] == 0));
                    }
                    using var bitmap = ModelRenderer.TextureBitmap(t); bitmap.Save(Path.Combine(folder, $"embedded_{texture:D3}.png"));
                    var candidates = index.Recursos.Where(e => e.Extension == ".bmp" &&
                        (Path.GetFileName(e.Ruta).Equals(Path.GetFileName(t.Name), StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileNameWithoutExtension(e.Ruta).Equals(Path.GetFileNameWithoutExtension(t.Name), StringComparison.OrdinalIgnoreCase))).ToArray();
                    foreach (var candidate in candidates)
                    {
                        string dir = Path.Combine(folder, "texture"); Directory.CreateDirectory(dir);
                        File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(candidate.Ruta)), Extraction.ReadResource(candidate));
                    }
                    textures.Add(new { t.Name, t.Width, t.Height, t.Flags, colors = t.Pixels.Distinct().Count(),
                        topColors = t.Pixels.GroupBy(p => p).OrderByDescending(g => g.Count()).Take(4).Select(g => new { color = $"{g.Key:X8}", count = g.Count() }),
                        external = candidates.Select(c => c.Ruta + " :: " + c.Paquete) });
                }
                using var preview = ModelRenderer.Render(bundle.Model, 600, 420, new()); preview.Save(Path.Combine(folder, "preview.png"));
                string? unityFolder = null; string? unityError = null;
                try
                {
                    unityFolder = UnityModelExporter.Export(bundle, folder);
                    var exported = JsonSerializer.Deserialize<ModelData>(File.ReadAllText(Path.Combine(unityFolder, "model.json")), new JsonSerializerOptions { IncludeFields = true })!;
                    foreach (var tex in exported.textures)
                    {
                        using var png = new System.Drawing.Bitmap(Path.Combine(unityFolder, tex.file));
                        Assert(png.Width == tex.width && png.Height == tex.height && Pixels(png).SequenceEqual(bundle.Model.Textures[tex.id].Pixels));
                        Assert(tex.sha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(unityFolder, tex.file)))));
                    }
                }
                catch (Exception error) { unityError = error.Message; }
                results.Add(new { id, entry.Ruta, entry.Paquete, version = bundle.Main.OriginalVersion, textures, missing = bundle.Missing, unityFolder, unityError });
                Console.WriteLine($"{id}: {entry.Ruta}: " + string.Join("; ", bundle.Model.Textures.Select(t => $"{t.Name} {t.Width}x{t.Height} flags=0x{t.Flags:X} colors={t.Pixels.Distinct().Count()}")));
            }
            catch (Exception error) { results.Add(new { id, entry.Ruta, error = error.Message }); Console.WriteLine(entry.Ruta + ": " + error.Message); }
        }
        File.WriteAllText(Path.Combine(destination, "audit.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
