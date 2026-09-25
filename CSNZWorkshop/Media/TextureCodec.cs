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
