using System.Buffers.Binary;
using System.Text;

// Minimal but valid GoldSrc MDL fixtures: the checks patch copies of these buffers to build rejections.
static class Synthetic
{
    public const int BoneAt = 244, HitboxAt = BoneAt + 224, GroupAt = HitboxAt + 32, SeqAt = GroupAt + 208,
        AnimAt = SeqAt + 176, RleAt = AnimAt + 24, TextureAt = AnimAt + 32, PixelsAt = TextureAt + 80,
        SkinAt = PixelsAt + 784, BodyAt = SkinAt + 2, SubAt = BodyAt + 76, VertInfoAt = SubAt + 112,
        VertAt = VertInfoAt + 4, NormInfoAt = VertAt + 48, NormAt = NormInfoAt + 2, MeshAt = NormAt + 12,
        TriAt = MeshAt + 20, Size = TriAt + 36;
    public const int CompanionTextureAt = 244, CompanionPixelsAt = CompanionTextureAt + 80,
        CompanionSkinAt = CompanionPixelsAt + 784, CompanionSize = CompanionSkinAt + 2;

    public static void Put(byte[] data, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at), value);
    public static void PutShort(byte[] data, int at, int value) => BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at), (short)value);
    public static void PutFloat(byte[] data, int at, float value) => Put(data, at, BitConverter.SingleToInt32Bits(value));
    static void PutText(byte[] data, int at, string text) => Encoding.ASCII.GetBytes(text).CopyTo(data, at);

    public static byte[] Model(bool externalTextures = false)
    {
        byte[] b = new byte[Size];
        void Int(int at, int value) => Put(b, at, value);
        void Real(int at, float value) => PutFloat(b, at, value);
        void Text(int at, string text) => PutText(b, at, text);
        Int(0, 0x54534449); Int(4, 10); Text(8, "sintetico.mdl"); Int(72, Size);
        Int(140, 2); Int(144, BoneAt);
        Int(156, 1); Int(160, HitboxAt);
        Int(164, 1); Int(168, SeqAt);
        Int(172, 2); Int(176, GroupAt);
        Int(180, externalTextures ? 0 : 1); Int(184, externalTextures ? 0 : TextureAt); Int(188, PixelsAt);
        Int(192, externalTextures ? 0 : 1); Int(196, externalTextures ? 0 : 1); Int(200, externalTextures ? 0 : SkinAt);
        Int(204, 1); Int(208, BodyAt);
        Text(BoneAt, "raiz"); Int(BoneAt + 32, -1);
        Text(BoneAt + 112, "punta"); Int(BoneAt + 112 + 32, 0); Real(BoneAt + 112 + 64 + 8, 20);
        for (int bone = 0; bone < 2; bone++)
            for (int axis = 0; axis < 6; axis++) Real(BoneAt + bone * 112 + 88 + axis * 4, 1);
        Int(HitboxAt, 0);
        for (int axis = 0; axis < 3; axis++) { Real(HitboxAt + 8 + axis * 4, -10); Real(HitboxAt + 20 + axis * 4, 10); }
        Text(GroupAt, "default"); Text(GroupAt + 32, "models/sintetico.mdl");
        Text(GroupAt + 104, "externo"); Text(GroupAt + 104 + 32, "models/sintetico01.mdl");
        Text(SeqAt, "idle"); Real(SeqAt + 32, 30); Int(SeqAt + 56, 8); Int(SeqAt + 120, 1); Int(SeqAt + 124, AnimAt); Int(SeqAt + 156, 0);
        // Bone 0 moves along X with two RLE blocks: frames 0-3 stay, frames 4-7 shift.
        PutShort(b, AnimAt, RleAt - AnimAt);
        b[RleAt] = 1; b[RleAt + 1] = 4; PutShort(b, RleAt + 2, 0);
        b[RleAt + 4] = 1; b[RleAt + 5] = 4; PutShort(b, RleAt + 6, 5);
        Text(TextureAt, "prueba.bmp"); Int(TextureAt + 68, 4); Int(TextureAt + 72, 4); Int(TextureAt + 76, PixelsAt);
        Palette(b, PixelsAt);
        PutShort(b, SkinAt, 0);
        Text(BodyAt, "cuerpo"); Int(BodyAt + 64, 1); Int(BodyAt + 68, 1); Int(BodyAt + 72, SubAt);
        Text(SubAt, "malla"); Int(SubAt + 72, 1); Int(SubAt + 76, MeshAt); Int(SubAt + 80, 4); Int(SubAt + 84, VertInfoAt);
        Int(SubAt + 88, VertAt); Int(SubAt + 92, 1); Int(SubAt + 96, NormInfoAt); Int(SubAt + 100, NormAt);
        float[,] quad = { { -10, 0, -10 }, { 10, 0, -10 }, { -10, 0, 10 }, { 10, 0, 10 } };
        for (int v = 0; v < 4; v++) for (int c = 0; c < 3; c++) Real(VertAt + v * 12 + c * 4, quad[v, c]);
        Real(NormAt + 8, 1);
        Int(MeshAt, 2); Int(MeshAt + 4, TriAt); Int(MeshAt + 8, 0); Int(MeshAt + 12, 1);
        PutShort(b, TriAt, 4);
        short[,] corners = { { 0, 0, 0, 0 }, { 1, 0, 3, 0 }, { 2, 0, 0, 3 }, { 3, 0, 3, 3 } };
        for (int c = 0; c < 4; c++) for (int f = 0; f < 4; f++) PutShort(b, TriAt + 2 + c * 8 + f * 2, corners[c, f]);
        return b;
    }

    public static byte[] TextureCompanion()
    {
        byte[] b = new byte[CompanionSize];
        Put(b, 0, 0x54534449); Put(b, 4, 10); PutText(b, 8, "sinteticoT.mdl"); Put(b, 72, CompanionSize);
        Put(b, 180, 1); Put(b, 184, CompanionTextureAt); Put(b, 188, CompanionPixelsAt);
        Put(b, 192, 1); Put(b, 196, 1); Put(b, 200, CompanionSkinAt);
        PutText(b, CompanionTextureAt, "compania.bmp");
        Put(b, CompanionTextureAt + 68, 4); Put(b, CompanionTextureAt + 72, 4); Put(b, CompanionTextureAt + 76, CompanionPixelsAt);
        Palette(b, CompanionPixelsAt);
        PutShort(b, CompanionSkinAt, 0);
        return b;
    }

    // Animation groups reuse the model layout with the IDSQ signature, so external frames are read from them.
    public static byte[] AnimationGroup(short delta)
    {
        byte[] group = Model();
        Put(group, 0, 0x51534449);
        PutShort(group, RleAt + 6, delta);
        return group;
    }

    static void Palette(byte[] data, int at)
    {
        for (int p = 0; p < 16; p++) data[at + p] = (byte)p;
        for (int c = 0; c < 256; c++)
        { data[at + 16 + c * 3] = (byte)c; data[at + 16 + c * 3 + 1] = (byte)(255 - c); data[at + 16 + c * 3 + 2] = 128; }
    }
}
