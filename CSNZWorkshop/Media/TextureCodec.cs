using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CSNZWorkshop.Models;

namespace CSNZWorkshop.Media;

public static class TextureCodec
{
    sealed class Allocator : Pfim.IImageAllocator
    {
        long outstanding;
        public byte[] Rent(int size)
        {
            if (size < 0 || outstanding + size > 128 * 1024 * 1024) throw new InvalidDataException("La textura supera el presupuesto de decodificación.");
            outstanding += size; return new byte[size];
        }
        public void Return(byte[] data) { outstanding -= data.Length; }
    }
    public static void Dimensions(int width, int height)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096) throw new InvalidDataException("Vista previa de textura limitada a 4096 × 4096.");
    }
    public static Bitmap Decode(byte[] bytes, string extension)
    {
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Textura demasiado grande (64 MB).");
        var r = new ModelBytes(bytes);
        if (extension == ".dds") { r.Range(0, 128); Dimensions(r.Int(16), r.Int(12)); }
        else if (extension == ".tga") { r.Range(0, 18); Dimensions(r.UShort(12), r.UShort(14)); }
        else throw new NotSupportedException("Formato de textura no admitido.");
        using var stream = new MemoryStream(bytes, false);
        using var decoded = Pfim.Pfimage.FromStream(stream, new Pfim.PfimConfig(allocator: new Allocator()));
        Dimensions(decoded.Width, decoded.Height);
        int bytesPerPixel = decoded.BitsPerPixel / 8;
        int ColorAt(int x, int y)
        {
            int offset = y * decoded.Stride + x * bytesPerPixel;
            var data = decoded.Data;
            if (decoded.Format == Pfim.ImageFormat.Rgba32) return BitConverter.ToInt32(data, offset);
            if (decoded.Format == Pfim.ImageFormat.Rgb24) return unchecked((int)0xff000000) | data[offset + 2] << 16 | data[offset + 1] << 8 | data[offset];
            if (decoded.Format == Pfim.ImageFormat.Rgb8) return unchecked((int)0xff000000) | data[offset] * 0x010101;
            int value = data[offset] | data[offset + 1] << 8;
            int red, green, blue, alpha = 255;
            if (decoded.Format == Pfim.ImageFormat.Rgba16) { blue = (value & 15) * 17; green = (value >> 4 & 15) * 17; red = (value >> 8 & 15) * 17; alpha = (value >> 12 & 15) * 17; }
            else if (decoded.Format == Pfim.ImageFormat.R5g6b5) { blue = (value & 31) * 255 / 31; green = (value >> 5 & 63) * 255 / 63; red = (value >> 11 & 31) * 255 / 31; }
            else if (decoded.Format is Pfim.ImageFormat.R5g5b5 or Pfim.ImageFormat.R5g5b5a1)
            { blue = (value & 31) * 255 / 31; green = (value >> 5 & 31) * 255 / 31; red = (value >> 10 & 31) * 255 / 31; if (decoded.Format == Pfim.ImageFormat.R5g5b5a1) alpha = (value & 32768) == 0 ? 0 : 255; }
            else throw new NotSupportedException("Textura HDR no admitida en esta vista previa.");
            return alpha << 24 | red << 16 | green << 8 | blue;
        }
        return Bitmap(decoded.Width, decoded.Height, ColorAt);
    }
    public static StudioTexture DecodeModelBitmap(byte[] bytes, string name, int flags)
    {
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("BMP externo demasiado grande (64 MB).");
        var r = new ModelBytes(bytes); r.Range(0, 54);
        if (r.UShort(0) != 0x4d42) throw new InvalidDataException("La textura externa no es un BMP.");
        int dib = r.Int(14), width = r.Int(18), signedHeight = r.Int(22), bits = r.UShort(28), compression = r.Int(30), start = r.Int(10);
        if (dib < 40 || dib > 124 || r.UShort(26) != 1 || signedHeight == int.MinValue) throw new InvalidDataException("Cabecera BMP externa no válida.");
        int height = Math.Abs(signedHeight); Dimensions(width, height); r.Range(14, dib);
        if (bits is not (8 or 24 or 32) || compression != 0) throw new NotSupportedException("La textura externa requiere BMP sin compresión de 8, 24 o 32 bits.");
        int stride = checked(((width * bits + 31) / 32) * 4), paletteAt = 14 + dib;
        int colors = bits == 8 ? r.Int(46) : 0;
        if (bits == 8 && colors == 0) colors = 256;
        if (colors < 0 || colors > 256) throw new InvalidDataException("Paleta BMP no válida.");
        r.Range(paletteAt, colors * 4L);
        if (start < paletteAt + colors * 4) throw new InvalidDataException("Píxeles BMP solapados con la cabecera o paleta.");
        r.Range(start, (long)stride * height);
        var pixels = new int[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int at = start + (signedHeight > 0 ? height - 1 - y : y) * stride + x * (bits / 8);
            if (bits == 8)
            {
                int index = bytes[at];
                if (index >= colors) throw new InvalidDataException("Índice de paleta BMP fuera de rango.");
                if ((flags & 64) != 0 && index == 255) { pixels[y * width + x] = 0; continue; }
                at = paletteAt + index * 4;
            }
            // BI_RGB's fourth byte is reserved, not an alpha channel.
            pixels[y * width + x] = unchecked((int)0xff000000) | bytes[at + 2] << 16 | bytes[at + 1] << 8 | bytes[at];
        }
        return new(name, width, height, flags, pixels);
    }
    internal static Bitmap Bitmap(int width, int height, Func<int, int, int> pixel)
    {
        Dimensions(width, height); var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var bits = bitmap.LockBits(new(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[width];
                for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) row[x] = pixel(x, y); Marshal.Copy(row, 0, bits.Scan0 + y * bits.Stride, width); }
            }
            finally { bitmap.UnlockBits(bits); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
