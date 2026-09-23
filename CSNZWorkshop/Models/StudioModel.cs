using System.Numerics;

namespace CSNZWorkshop.Models;

public record StudioBone(string Name, int Parent, Vector3 Position, Vector3 Rotation, float[] Scale);
public record StudioTexture(string Name, int Width, int Height, int Flags, int[] Pixels);
public record StudioCorner(int Vertex, Vector2 UV);
public record StudioTriangle(StudioCorner A, StudioCorner B, StudioCorner C, int Skin);
public record StudioPart(string Name, Vector3[] Vertices, byte[] BoneIndices, StudioTriangle[] Triangles);
public record StudioBody(string Name, StudioPart[] Variants);
public record StudioSequence(string Name, float Fps, int Frames, int Blends, int AnimationOffset, int Group);

public sealed class StudioModel
{
    public required byte[] Data { get; init; }
    public required string Name { get; init; }
    public required StudioBone[] Bones { get; init; }
    public required StudioTexture[] Textures { get; init; }
    public required StudioBody[] Bodies { get; init; }
    public required StudioSequence[] Sequences { get; init; }
    public required short[][] Skins { get; init; }
    public required string[] GroupNames { get; init; }
    public List<string> Warnings { get; } = [];
    public Dictionary<int, byte[]> ExternalGroups { get; } = [];

    public static StudioModel Parse(byte[] data, StudioModel? textureCompanion = null)
    {
        var r = new ModelBytes(data); r.Range(0, 244);
        if (r.Int(0) != 0x54534449 || r.Int(4) != 10) throw new NotSupportedException("El visor requiere un MDL IDST versión 10 o una variante 20/21 descifrada.");
        if (r.Int(72) != data.Length) throw new InvalidDataException("Longitud MDL declarada distinta del archivo.");
        int boneCount = r.Table(r.Int(140), r.Int(144), 112, 512);
        var bones = new StudioBone[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            int at = r.Int(144) + i * 112, parent = r.Int(at + 32);
            if (parent < -1 || parent >= i) throw new InvalidDataException("Jerarquía de huesos cíclica o fuera de orden.");
            bones[i] = new(r.Text(at, 32), parent, r.Vector(at + 64), r.Vector(at + 76),
                Enumerable.Range(0, 6).Select(c => r.Float(at + 88 + c * 4)).ToArray());
        }
        r.Table(r.Int(148), r.Int(152), 24, 512);
        int boxes = r.Table(r.Int(156), r.Int(160), 32, 4096);
        for (int i = 0; i < boxes; i++)
        {
            int at = r.Int(160) + i * 32;
            if (r.Int(at) < 0 || r.Int(at) >= boneCount) throw new InvalidDataException("Hueso de hitbox inválido.");
            _ = r.Vector(at + 8); _ = r.Vector(at + 20);
        }
        int attachments = r.Table(r.Int(212), r.Int(216), 88, 1024);
        for (int i = 0; i < attachments; i++)
            if (r.Int(r.Int(216) + i * 88 + 36) < 0 || r.Int(r.Int(216) + i * 88 + 36) >= boneCount)
                throw new InvalidDataException("Hueso de attachment inválido.");
        int textureCount = r.Table(r.Int(180), r.Int(184), 80, 1024);
        var textures = new StudioTexture[textureCount];
        long totalPixels = 0;
        for (int i = 0; i < textureCount; i++)
        {
            int at = r.Int(184) + i * 80, width = r.Int(at + 68), height = r.Int(at + 72), start = r.Int(at + 76);
            if (width < 1 || height < 1 || width > 4096 || height > 4096) throw new InvalidDataException("Dimensiones de textura MDL inválidas.");
            int count = checked(width * height); totalPixels += count;
            if (totalPixels > 32 * 1024 * 1024) throw new InvalidDataException("Demasiadas texturas para la vista previa (128 MiB RGBA).");
            r.Range(start, (long)count + 768);
            int flags = r.Int(at + 64);
            int[] pixels = new int[count];
            for (int p = 0; p < count; p++)
            {
                int palette = data[start + p], color = start + count + palette * 3;
                pixels[p] = (flags & 64) != 0 && palette == 255 ? 0 : unchecked((int)0xff000000) | data[color] << 16 | data[color + 1] << 8 | data[color + 2];
            }
            textures[i] = new(r.Text(at, 64), width, height, flags, pixels);
        }
        int skinRefs = r.Int(192), skinFamilies = r.Int(196);
        r.Table(skinRefs, r.Int(200), 2, 4096);
        if (skinFamilies < 0 || skinFamilies > 4096 || (long)skinRefs * skinFamilies > 1000000) throw new InvalidDataException("Tabla de skins demasiado grande.");
        r.Range(r.Int(200), (long)skinRefs * skinFamilies * 2);
        var skins = new short[skinFamilies][];
        for (int f = 0; f < skinFamilies; f++)
        {
            skins[f] = new short[skinRefs];
            for (int s = 0; s < skinRefs; s++)
            {
                short id = r.Short(r.Int(200) + (f * skinRefs + s) * 2);
                if (id < 0 || (textureCount > 0 && id >= textureCount)) throw new InvalidDataException("Índice de textura en skin inválido.");
                skins[f][s] = id;
            }
        }
        if (textureCount == 0 && textureCompanion is not null) { textures = textureCompanion.Textures; skins = textureCompanion.Skins; }
        // With an external T.mdl the main file carries no skin table: its meshes index the companion's.
        int meshSkins = skins.Length > 0 ? skins[0].Length : 0;
        int groupCount = r.Table(r.Int(172), r.Int(176), 104, 256);
        string[] groups = Enumerable.Range(0, groupCount).Select(i => r.Text(r.Int(176) + i * 104 + 32, 64)).ToArray();
        int seqCount = r.Table(r.Int(164), r.Int(168), 176, 4096);
        var sequences = new StudioSequence[seqCount];
        for (int i = 0; i < seqCount; i++)
        {
            int at = r.Int(168) + i * 176;
            int frames = r.Int(at + 56), blends = r.Int(at + 120), group = r.Int(at + 156);
            float fps = r.Float(at + 32);
            if (frames < 1 || frames > 100000 || blends < 1 || blends > 64 || group < 0 || group >= groupCount || fps < 0 || fps > 1000)
                throw new InvalidDataException("Descriptor de animación inválido.");
            int events = r.Table(r.Int(at + 48), r.Int(at + 52), 76, 4096);
            for (int e = 0; e < events; e++) _ = r.Text(r.Int(at + 52) + e * 76 + 12, 64);
            if (group == 0) r.Table(boneCount * blends, r.Int(at + 124), 12, 32768);
            sequences[i] = new(r.Text(at, 32), fps, frames, blends, r.Int(at + 124), group);
        }
        int bodyCount = r.Table(r.Int(204), r.Int(208), 76, 256);
        var bodies = new StudioBody[bodyCount];
        int totalTriangles = 0, totalVertices = 0;
        for (int b = 0; b < bodyCount; b++)
        {
            int body = r.Int(208) + b * 76;
            int modelCount = r.Table(r.Int(body + 64), r.Int(body + 72), 112, 256);
            var variants = new StudioPart[modelCount];
            for (int m = 0; m < modelCount; m++)
            {
                int model = r.Int(body + 72) + m * 112;
                int vertexCount = r.Table(r.Int(model + 80), r.Int(model + 88), 12, 100000);
                totalVertices += vertexCount;
                if (totalVertices > 1000000) throw new InvalidDataException("Modelo demasiado grande para el visor.");
                r.Range(r.Int(model + 84), vertexCount);
                var vertices = new Vector3[vertexCount]; var boneIds = new byte[vertexCount];
                for (int v = 0; v < vertexCount; v++)
                {
                    vertices[v] = r.Vector(r.Int(model + 88) + v * 12);
                    boneIds[v] = data[r.Int(model + 84) + v];
                    if (boneIds[v] >= bones.Length) throw new InvalidDataException("Hueso de vértice inválido.");
                }
                int normals = r.Table(r.Int(model + 92), r.Int(model + 100), 12, 100000);
                r.Range(r.Int(model + 96), normals);
                int meshes = r.Table(r.Int(model + 72), r.Int(model + 76), 20, 4096);
                var triangles = new List<StudioTriangle>();
                for (int mesh = 0; mesh < meshes; mesh++)
                {
                    int at = r.Int(model + 76) + mesh * 20, cursor = r.Int(at + 4), skin = r.Int(at + 8);
                    if (skin < 0 || (meshSkins > 0 ? skin >= meshSkins : skin > 4096)) throw new InvalidDataException("Material de malla inválido.");
                    int declared = r.Int(at), generated = 0;
                    if (declared < 0 || declared > 300000) throw new InvalidDataException("Cantidad de triángulos inválida.");
                    while (true)
                    {
                        int signed = r.Short(cursor); cursor += 2;
                        if (signed == 0) break;
                        int count = Math.Abs(signed);
                        if (count < 3) throw new InvalidDataException("Tira de triángulos inválida.");
                        r.Range(cursor, count * 8L);
                        var corners = new StudioCorner[count];
                        for (int c = 0; c < count; c++, cursor += 8)
                        {
                            int vertex = r.Short(cursor), normal = r.Short(cursor + 2);
                            if (vertex < 0 || vertex >= vertexCount || normal < 0 || normal >= normals) throw new InvalidDataException("Índice de triángulo inválido.");
                            corners[c] = new(vertex, new(r.Short(cursor + 4), r.Short(cursor + 6)));
                        }
                        for (int c = 2; c < count; c++)
                        {
                            var a = signed < 0 ? corners[0] : corners[c - 2];
                            var second = corners[c - 1];
                            if (signed > 0 && (c & 1) != 0) (a, second) = (second, a);
                            triangles.Add(new(a, second, corners[c], skin));
                            generated++; totalTriangles++;
                            if (generated > declared || totalTriangles > 300000) throw new InvalidDataException("Exceso de triángulos en el modelo.");
                        }
                    }
                    if (generated != declared) throw new InvalidDataException("La malla no coincide con sus triángulos declarados.");
                }
                variants[m] = new(r.Text(model, 64), vertices, boneIds, triangles.ToArray());
            }
            bodies[b] = new(r.Text(body, 64), variants);
        }
        var result = new StudioModel { Data = data, Name = r.Text(8, 64), Bones = bones, Textures = textures, Bodies = bodies, Sequences = sequences, Skins = skins, GroupNames = groups };
        if (bones.Length > 128) result.Warnings.Add("Supera 128 huesos: requiere recompilación/reducción para el formato clásico.");
        if (bodies.Any(b => b.Variants.Any(v => v.Vertices.Length > 2048))) result.Warnings.Add("Hay submodelos con más de 2048 vértices; revisar límites del cliente destino.");
        if (textures.Length > 100) result.Warnings.Add("Más de 100 texturas: revisar límites del cliente destino.");
        if (textures.Any(t => (t.Flags & ~127) != 0)) result.Warnings.Add("Hay flags de material adicionales de Nexon; el visor muestra una aproximación.");
        if (r.Int(148) > 0) result.Warnings.Add("Vista previa sin ajuste interactivo de controladores de huesos.");
        if (sequences.Any(s => s.Blends > 1)) result.Warnings.Add("Las animaciones muestran el primer blend.");
        if (textures.Length == 0 && bodies.Length > 0) result.Warnings.Add("Faltan texturas: busca el archivo compañero T.mdl.");
        if (meshSkins == 0 && bodies.Any(b => b.Variants.Any(v => v.Triangles.Length > 0)))
            result.Warnings.Add("Sin tabla de materiales: las mallas se muestran sin textura.");
        return result;
    }

    public Matrix4x4[] Pose(int sequence = -1, int frame = 0)
    {
        StudioSequence? seq = sequence >= 0 && sequence < Sequences.Length ? Sequences[sequence] : null;
        ModelBytes r = new(Data);
        if (seq is not null && seq.Group != 0)
            r = new(ExternalGroups.TryGetValue(seq.Group, out var group) ? group : throw new InvalidDataException("Falta el archivo del grupo de animación externo."));
        var transforms = new Matrix4x4[Bones.Length];
        for (int b = 0; b < Bones.Length; b++)
        {
            var bone = Bones[b]; Vector3 position = bone.Position, rotation = bone.Rotation;
            if (seq is not null)
            {
                int at = checked(seq.AnimationOffset + b * 12);
                for (int axis = 0; axis < 6; axis++)
                {
                    int offset = r.UShort(at + axis * 2);
                    if (offset == 0) continue;
                    int cursor = checked(at + offset), remaining = Math.Clamp(frame, 0, seq.Frames - 1);
                    while (true)
                    {
                        r.Range(cursor, 2); int valid = r.Data[cursor], total = r.Data[cursor + 1];
                        if (total == 0 || valid == 0 || valid > total) throw new InvalidDataException("Canal de animación RLE inválido.");
                        r.Range(cursor + 2, valid * 2L);
                        if (remaining < total)
                        {
                            float delta = r.Short(cursor + 2 + Math.Min(remaining, valid - 1) * 2) * bone.Scale[axis];
                            if (axis < 3) position[axis] += delta; else rotation[axis - 3] += delta;
                            break;
                        }
                        remaining -= total; cursor += (valid + 1) * 2;
                    }
                }
            }
            var local = Matrix4x4.CreateRotationX(rotation.X) * Matrix4x4.CreateRotationY(rotation.Y)
                * Matrix4x4.CreateRotationZ(rotation.Z) * Matrix4x4.CreateTranslation(position);
            transforms[b] = bone.Parent < 0 ? local : local * transforms[bone.Parent];
        }
        return transforms;
    }
}
