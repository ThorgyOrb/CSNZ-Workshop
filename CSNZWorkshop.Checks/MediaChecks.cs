using CSNZWorkshop;
using CSNZWorkshop.Archives;
using CSNZWorkshop.Media;

static class MediaChecks
{
    public static byte[] Sprite(bool group = false, int format = 3)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("IDSP"u8); writer.Write(2); writer.Write(2); writer.Write(format); writer.Write(2f);
        writer.Write(2); writer.Write(2); writer.Write(group ? 1 : 2); writer.Write(0f); writer.Write(0);
        writer.Write((ushort)256);
        for (int i = 0; i < 256; i++) { writer.Write((byte)i); writer.Write((byte)(255 - i)); writer.Write((byte)0); }
        if (group) { writer.Write(1); writer.Write(2); writer.Write(.1f); writer.Write(.3f); }
        for (int i = 0; i < 2; i++)
        {
            if (!group) writer.Write(0);
            writer.Write(i); writer.Write(2); writer.Write(2); writer.Write(2);
            writer.Write(new byte[] { 1, 255, 2, (byte)(3 + i) });
        }
        return stream.ToArray();
    }

    public static byte[] Wave()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(8036); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(8000); writer.Write(8000);
        writer.Write((ushort)1); writer.Write((ushort)8); writer.Write("data"u8); writer.Write(8000);
        writer.Write(Enumerable.Repeat((byte)128, 8000).ToArray()); return stream.ToArray();
    }

    public static byte[] Mp3()
    {
        // Silent MPEG-1 Layer III frames: 128 kbps, 44.1 kHz, no reservoir or coded samples.
        byte[] bytes = new byte[417 * 40];
        for (int i = 0; i < 40; i++) { bytes[i * 417] = 0xff; bytes[i * 417 + 1] = 0xfb; bytes[i * 417 + 2] = 0x90; }
        return bytes;
    }

    public static void Run(string root, Action<string, Action> check)
    {
        check("SPR aditivo: negro y tonos oscuros suman luz sin fondo opaco", () =>
        {
            var clip = SpriteClip.Parse(Sprite(format: 1));
            clip.Palette[1] = unchecked((int)0xff000000); clip.Palette[255] = unchecked((int)0xff030405);
            using var image = clip.Render(0, 3, 2);
            Assert(image.GetPixel(0, 0).ToArgb() == unchecked((int)0xff202833));
            Assert(image.GetPixel(1, 0).ToArgb() == unchecked((int)0xff232c38));
            var normal = SpriteClip.Parse(Sprite(format: 0)); normal.Palette[1] = unchecked((int)0xff000000);
            using var opaque = normal.Render(0, 3, 2); Assert(opaque.GetPixel(0, 0).ToArgb() == unchecked((int)0xff000000));
        });
        check("MDL aditivo: negro invisible y mezcla sobre el fondo", () =>
        {
            var model = CSNZWorkshop.Models.StudioModel.Parse(Synthetic.Model());
            var texture = model.Textures[0];
            model.Textures[0] = texture with { Flags = 0x24, Pixels = Enumerable.Repeat(unchecked((int)0xff000000), texture.Pixels.Length).ToArray() };
            using var black = CSNZWorkshop.Models.ModelRenderer.Render(model, 160, 160, new(0, 0));
            for (int y = 0; y < black.Height; y++) for (int x = 0; x < black.Width; x++)
                Assert(black.GetPixel(x, y).ToArgb() == unchecked((int)0xff202833));
            Array.Fill(model.Textures[0].Pixels, unchecked((int)0xff140a05));
            using var glow = CSNZWorkshop.Models.ModelRenderer.Render(model, 160, 160, new(0, 0));
            Assert(glow.GetPixel(80, 80).ToArgb() == unchecked((int)0xff343238));
            model.Textures[0] = model.Textures[0] with { Name = "$0b_effect.bmp", Flags = 0 };
            using var nexon = CSNZWorkshop.Models.ModelRenderer.Render(model, 160, 160, new(0, 0));
            Assert(nexon.GetPixel(80, 80).ToArgb() == glow.GetPixel(80, 80).ToArgb());
            using var disabled = CSNZWorkshop.Models.ModelRenderer.Render(model, 160, 160, new(0, 0, NexonEffects: false));
            Assert(disabled.GetPixel(80, 80).R < 32 && model.Textures[0].Flags == 0);
        });
        check("SPR: fotogramas, origen estable y transparencia", () =>
        {
            var clip = SpriteClip.Parse(Sprite());
            Assert(clip.Frames.Length == 2 && clip.Bounds.Width == 3 && clip.Bounds.Height == 2);
            using var image = clip.Render(0, 3, 2);
            Assert(image.GetPixel(0, 0).G == 254 && image.GetPixel(1, 0).A == 0);
            using var shifted = clip.Render(1, 3, 2);
            Assert(shifted.GetPixel(0, 0).A == 0 && shifted.GetPixel(1, 0).G == 254);
        });
        check("SPR: grupos conservan intervalos y alfa indexado", () =>
        {
            var clip = SpriteClip.Parse(Sprite(true, 2));
            Assert(clip.Frames[0].Milliseconds == 100 && clip.Frames[1].Milliseconds == 200);
            using var image = clip.Render(0, 3, 2);
            Assert(image.GetPixel(0, 0).A == 1 && image.GetPixel(0, 0).R == 255);
        });
        check("SPR: truncamientos, límites y cabeceras inválidas", () =>
        {
            byte[] bytes = Sprite();
            for (int i = 0; i < bytes.Length; i++) Reject(() => SpriteClip.Parse(bytes[..i]));
            foreach (var (offset, value) in new[] { (0, 0), (4, 99), (12, 9), (20, int.MaxValue), (28, 0), (810, 9), (822, 0) })
            {
                byte[] corrupt = Sprite(); Synthetic.Put(corrupt, offset, value); Reject(() => SpriteClip.Parse(corrupt));
            }
            byte[] group = Sprite(true); Synthetic.PutFloat(group, 822, float.NaN); Reject(() => SpriteClip.Parse(group));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { SpriteClip.Parse(bytes, canceled.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        });
        check("Colecciones: persistencia y separación de paquetes con rutas iguales", () =>
        {
            var collection = new ResourceCollection();
            var a = new ResourceEntry("/cstrike/a.wav", Path.Combine(root, "a.nar"), "NAR", 4, 4, 0, 0);
            var b = a with { Paquete = Path.Combine(root, "b.nar") };
            Assert(collection.Add([a, a, b]) == 2);
            collection.Rename("Mi mod zombie");
            string filename = Path.Combine(root, "mod.csnzcollection"); collection.Save(filename);
            Assert(!collection.Dirty);
            var restored = new ResourceCollection(); restored.Load(filename);
            Assert(restored.Name == collection.Name && restored.Resources.Count == 2);
            Assert(restored.Resolve([a, b]).All(r => r.Entry is not null));
            Assert(restored.Resolve([a]).Count(r => r.Entry is null) == 1);
            Assert(restored.Resolve([a, a, b])[0].Entry is null);
            restored.Remove([restored.Resources[0]]); Assert(restored.Dirty && restored.Resources.Count == 1);
        });
        check("Colecciones: JSON inválido conserva selección y no carga offsets", () =>
        {
            var collection = new ResourceCollection(); collection.Rename("Conservar");
            string filename = Path.Combine(root, "bad.csnzcollection");
            foreach (string json in new[] { "null", "{}", "{\"Version\":99}", "{\"Version\":1,\"Name\":\"x\",\"Resources\":[null]}" })
            {
                File.WriteAllText(filename, json); Reject(() => collection.Load(filename)); Assert(collection.Name == "Conservar");
            }
        });
        check("Colecciones: exporta recursos de varios paquetes sin colisiones", () =>
        {
            byte[] bytes = Wave();
            var a = ArchiveIndex.Read(ExtractionChecks.WriteNar(root, "collection-a.nar", "/cstrike/sound/a.wav", bytes, bytes.Length, 0)).Single();
            var b = ArchiveIndex.Read(ExtractionChecks.WriteNar(root, "collection-b.nar", "/cstrike/sound/a.wav", bytes, bytes.Length, 0)).Single();
            var collection = new ResourceCollection(); collection.Add([a, b]);
            var resolved = collection.Resolve([a, b]);
            var result = Extraction.Run(resolved.Select(r => r.Entry!).ToArray(), Path.Combine(root, "collection-output"));
            Assert(result.Extraidos == 2 && result.Archivos.Select(a => a.Destino).Distinct().Count() == 2);
        });
        check("Audio WAV: duración, reproducción silenciosa, parada y limpieza temporal", () =>
        {
            string? temporary;
            using (var audio = new AudioPreview(Wave(), ".wav"))
            {
                temporary = audio.TemporaryPath;
                Assert(audio.DurationMilliseconds == 1000 && File.Exists(temporary));
                audio.Play(); audio.Stop(); Assert(!audio.IsPlaying);
            }
            Assert(!File.Exists(temporary));
        });
        check("Audio MP3: decodificación integrada y reproducción de una muestra silenciosa", () =>
        {
            using var audio = new AudioPreview(Mp3(), ".mp3");
            Assert(audio.DurationMilliseconds is > 500 and < 2000);
            audio.Play(); audio.Stop(); Assert(!audio.IsPlaying);
        });
        check("Audio MP3: cancelación antes de decodificar", () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { AudioPreview.DecodeMp3(Mp3(), cancellation.Token); }
            catch (OperationCanceledException) { return; }
            throw new Exception("MP3 cancellation ignored");
        });
        byte[] sprite = Sprite(), wave = Wave();
        string spritePackage = ExtractionChecks.WriteNar(root, "preview-sprite.nar", "/cstrike/sprites/test.spr", sprite, sprite.Length, 0);
        string wavePackage = ExtractionChecks.WriteNar(root, "preview-wave.nar", "/cstrike/sound/test.wav", wave, wave.Length, 0);
        check("Interfaz: sprites, audio y cambio de selección durante reproducción", () => UiChecks.VerifyMedia(spritePackage, wavePackage));
    }

    static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { return; }
        throw new Exception("Invalid input accepted");
    }
}
