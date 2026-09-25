using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CSNZWorkshop.Media;

public sealed record SpriteFrame(int X, int Y, int Width, int Height, byte[] Pixels, int Milliseconds);

// GoldSrc IDSP v2 layout: ValveSoftware/halflife, utils/sprgen/spritegn.h.
public sealed class SpriteClip
{
    public required int Format { get; init; }
    public required int[] Palette { get; init; }
    public required SpriteFrame[] Frames { get; init; }
    public required Rectangle Bounds { get; init; }

    public static SpriteClip Parse(byte[] bytes, CancellationToken token = default)
    {
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Sprite demasiado grande para la vista previa (64 MB).");
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream);
        try
        {
            if (reader.ReadInt32() != 0x50534449) throw new InvalidDataException("Firma SPR no reconocida.");
            if (reader.ReadInt32() != 2) throw new NotSupportedException("La vista previa admite sprites IDSP versión 2.");
            int type = reader.ReadInt32(), format = reader.ReadInt32();
            float radius = reader.ReadSingle();
            int width = reader.ReadInt32(), height = reader.ReadInt32(), count = reader.ReadInt32();
            float beam = reader.ReadSingle(); int sync = reader.ReadInt32();
            if (type is < 0 or > 4 || format is < 0 or > 3 || sync is < 0 or > 1
                || !float.IsFinite(radius) || !float.IsFinite(beam) || radius < 0)
                throw new InvalidDataException("Cabecera SPR inválida.");
            Dimensions(width, height);
            if (count is < 1 or > 1024) throw new InvalidDataException("Cantidad de fotogramas fuera del límite de vista previa (1024).");
            int colors = reader.ReadUInt16();
            if (colors is < 1 or > 256 || (format == 2 && colors != 256)) throw new InvalidDataException("Paleta SPR inválida.");
            var palette = new int[colors];
            for (int i = 0; i < colors; i++)
                palette[i] = unchecked((int)0xff000000) | reader.ReadByte() << 16 | reader.ReadByte() << 8 | reader.ReadByte();
            var frames = new List<SpriteFrame>();
            long pixels = 0;
            void ReadFrame(int duration)
            {
                token.ThrowIfCancellationRequested();
                int x = reader.ReadInt32(), y = reader.ReadInt32(), w = reader.ReadInt32(), h = reader.ReadInt32();
                Dimensions(w, h);
                if (Math.Abs((long)x) > 8192 || Math.Abs((long)y) > 8192) throw new InvalidDataException("Origen SPR fuera de rango.");
                pixels += (long)w * h;
                if (pixels > 16 * 1024 * 1024 || frames.Count >= 1024) throw new InvalidDataException("Sprite excede el presupuesto de vista previa.");
                byte[] data = reader.ReadBytes(w * h);
                if (data.Length != w * h || data.Any(p => p >= colors)) throw new InvalidDataException("Píxeles SPR truncados o fuera de la paleta.");
                frames.Add(new(x, y, w, h, data, duration));
            }
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                int group = reader.ReadInt32();
                if (group == 0) ReadFrame(100);
                else if (group == 1)
                {
                    int size = reader.ReadInt32();
                    if (size < 1 || size > 1024 - frames.Count) throw new InvalidDataException("Grupo SPR demasiado grande.");
                    var durations = new int[size]; float previous = 0;
                    for (int f = 0; f < size; f++)
                    {
                        float end = reader.ReadSingle(), duration = end - previous;
                        if (!float.IsFinite(end) || duration <= 0 || duration > 3600) throw new InvalidDataException("Intervalos SPR inválidos.");
                        durations[f] = Math.Max(1, (int)Math.Round(duration * 1000)); previous = end;
                    }
                    foreach (int duration in durations) ReadFrame(duration);
                }
                else throw new InvalidDataException("Tipo de fotograma SPR desconocido.");
            }
            int left = frames.Min(f => f.X), top = frames.Min(f => -f.Y);
            int right = frames.Max(f => f.X + f.Width), bottom = frames.Max(f => -f.Y + f.Height);
            if (right - left > 8192 || bottom - top > 8192) throw new InvalidDataException("Encuadre SPR demasiado grande.");
            return new() { Format = format, Palette = palette, Frames = frames.ToArray(), Bounds = Rectangle.FromLTRB(left, top, right, bottom) };
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Sprite truncado.", ex); }
    }

    static void Dimensions(int width, int height)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096) throw new InvalidDataException("Dimensiones SPR fuera del límite de vista previa (4096).");
    }

    public Bitmap Render(int frame, int width = 480, int height = 360)
    {
        var source = Frames[frame];
        width = Math.Clamp(width, 1, 900); height = Math.Clamp(height, 1, 700);
        double scale = Math.Min((double)width / Bounds.Width, (double)height / Bounds.Height);
        width = Math.Max(1, (int)(Bounds.Width * scale)); height = Math.Max(1, (int)(Bounds.Height * scale));
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var pixels = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int sx = (int)(x / scale) + Bounds.Left - source.X;
                int sy = (int)(y / scale) + Bounds.Top + source.Y;
                if (sx < 0 || sy < 0 || sx >= source.Width || sy >= source.Height) continue;
                int index = source.Pixels[sy * source.Width + sx], color = Palette[index];
                if (Format == 3 && index == 255) color = 0;
                else if (Format == 2) color = (Palette[255] & 0xffffff) | index << 24;
                // Composite additive sprites onto the same backdrop used by the model viewer.
                // Near-black pixels must add light too, rather than becoming opaque dark boxes.
                else if (Format == 1)
                    color = unchecked((int)0xff000000)
                        | Math.Min(255, ((color >> 16) & 255) + 32) << 16
                        | Math.Min(255, ((color >> 8) & 255) + 40) << 8
                        | Math.Min(255, (color & 255) + 51);
                pixels[y * width + x] = color;
            }
        var bits = output.LockBits(new(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length); }
        finally { output.UnlockBits(bits); }
        return output;
    }
}
