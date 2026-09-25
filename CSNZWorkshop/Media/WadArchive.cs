using CSNZWorkshop.Models;

namespace CSNZWorkshop.Media;

public sealed record WadTexture(string Name, int Offset, int Size, byte Type, bool Compressed)
{
    public override string ToString() => Name + (Compressed || Type is not (0x43 or 0x42) ? " (sin vista previa)" : "");
}

public sealed class WadArchive
{
    readonly byte[] bytes;
    public IReadOnlyList<WadTexture> Textures { get; }
    public WadArchive(byte[] bytes)
    {
        this.bytes = bytes;
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("WAD demasiado grande para vista previa (64 MB).");
        var r = new ModelBytes(bytes); r.Range(0, 12);
        if (r.Int(0) != 0x33444157) throw new NotSupportedException("La vista previa requiere WAD3 con paletas; WAD2 no se decodifica sin su paleta externa.");
        int count = r.Int(4), table = r.Int(8);
        if (count is < 0 or > 10000 || table < 12) throw new InvalidDataException("Índice WAD inválido.");
        r.Range(table, count * 32L);
        var textures = new List<WadTexture>();
        for (int i = 0; i < count; i++)
        {
            int at = table + i * 32, offset = r.Int(at), stored = r.Int(at + 4);
            if (offset < 12) throw new InvalidDataException("Entrada WAD solapada con la cabecera.");
            r.Range(offset, stored);
            textures.Add(new(r.Text(at + 16, 16), offset, stored, bytes[at + 12], bytes[at + 13] != 0));
        }
        Textures = textures;
    }
    public Bitmap Render(int index)
    {
        var texture = Textures[index];
        if (texture.Compressed || texture.Type is not (0x43 or 0x42)) throw new NotSupportedException("Esta entrada WAD no es una textura sin compresión admitida.");
        var r = new ModelBytes(bytes.AsSpan(texture.Offset, texture.Size).ToArray());
        int width, height, pixels, palette;
        if (texture.Type == 0x43)
        {
            r.Range(0, 40); width = r.Int(16); height = r.Int(20); TextureCodec.Dimensions(width, height);
            if (width < 8 || height < 8) throw new InvalidDataException("Mipmaps WAD demasiado pequeños.");
            for (int mip = 0; mip < 4; mip++)
            {
                int offset = r.Int(24 + mip * 4);
                if (offset < 40) throw new InvalidDataException("Mipmaps WAD ausentes o solapados.");
                r.Range(offset, (long)(width >> mip) * (height >> mip));
            }
            pixels = r.Int(24); palette = checked(r.Int(36) + (width >> 3) * (height >> 3));
        }
        else { r.Range(0, 8); width = r.Int(0); height = r.Int(4); TextureCodec.Dimensions(width, height); pixels = 8; palette = checked(8 + width * height); }
        r.Range(pixels, (long)width * height); int colors = r.UShort(palette); palette += 2;
        if (colors is < 1 or > 256) throw new InvalidDataException("Paleta WAD inválida.");
        r.Range(palette, colors * 3L);
        return TextureCodec.Bitmap(width, height, (x, y) =>
        {
            int color = r.Data[pixels + y * width + x];
            if (color >= colors) throw new InvalidDataException("Índice WAD fuera de paleta.");
            if (texture.Name.StartsWith('{') && color == 255) return 0;
            int at = palette + color * 3;
            return unchecked((int)0xff000000) | r.Data[at] << 16 | r.Data[at + 1] << 8 | r.Data[at + 2];
        });
    }
}
