using System.Numerics;
using System.Text.Json;
using CSNZ.UnityBridge;
using CSNZWorkshop.Archives;

namespace CSNZWorkshop.Models;

public static class UnityModelExporter
{
    // Row-vector convention: GoldSrc forward +X, left +Y, up +Z -> Unity right +X, up +Y, forward +Z.
    static readonly Matrix4x4 Basis = new(0, 0, 1, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1);
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    public static Vector3 ConvertDirection(Vector3 value) => Vector3.TransformNormal(value, Basis);
    public static Quaternion ConvertRotation(Quaternion value)
    {
        var matrix = Matrix4x4.Transpose(Basis) * Matrix4x4.CreateFromQuaternion(value) * Basis;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
    }
    static float[] V(Vector3 v) => [v.X, v.Y, v.Z];
    static float[] Q(Quaternion q) => [q.X, q.Y, q.Z, q.W];

    public static ModelData Build(StudioModel model, float metersPerUnit = .0254f, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!float.IsFinite(metersPerUnit) || metersPerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(metersPerUnit));
        long samples = model.Sequences.Sum(s => (long)s.Frames * s.Blends * model.Bones.Length);
        if (samples > 2000000) throw new InvalidDataException("La exportación supera dos millones de poses de huesos. Reduce el modelo o las secuencias.");
        var reference = model.GetLocalPose();
        var globals = model.Pose();
        var meshes = new List<MeshData>();
        long corners = model.Bodies.Sum(b => b.Variants.Sum(p => (long)p.Triangles.Length * 3)) * Math.Max(1, model.Skins.Length);
        if (corners > 3000000) throw new InvalidDataException("Demasiada geometría multiplicada por skins para exportar.");
        for (int body = 0; body < model.Bodies.Length; body++)
        for (int variant = 0; variant < model.Bodies[body].Variants.Length; variant++)
        for (int skin = 0; skin < Math.Max(1, model.Skins.Length); skin++)
        {
            token.ThrowIfCancellationRequested();
            var part = model.Bodies[body].Variants[variant];
            var positions = new List<float>(); var normals = new List<float>(); var uv = new List<float>();
            var bones = new List<int>(); var normalBones = new List<int>(); var submeshes = new List<SubmeshData>();
            var vertices = new Dictionary<(StudioCorner Corner, int Texture), int>();
            foreach (var group in part.Triangles.GroupBy(t => t.Skin))
            {
                int texture = model.Skins.Length > 0 && group.Key < model.Skins[skin].Length ? model.Skins[skin][group.Key] : -1;
                if (texture < 0 || texture >= model.Textures.Length) throw new InvalidDataException("Faltan texturas o relaciones de skins para exportar a Unity.");
                var tex = model.Textures[texture]; var indices = new List<int>();
                int Corner(StudioCorner corner)
                {
                    if (vertices.TryGetValue((corner, texture), out int existing)) return existing;
                    int index = bones.Count; vertices.Add((corner, texture), index);
                    int bone = part.BoneIndices[corner.Vertex], normalBone = part.NormalBoneIndices[corner.Normal];
                    var position = Vector3.Transform(part.Vertices[corner.Vertex], globals[bone]);
                    var normal = Vector3.TransformNormal(part.Normals[corner.Normal], globals[normalBone]);
                    if (normal.LengthSquared() < 1e-12f) throw new InvalidDataException("Normal nula: no se puede exportar una dirección válida.");
                    positions.AddRange(V(ConvertDirection(position) * metersPerUnit));
                    normals.AddRange(V(Vector3.Normalize(ConvertDirection(normal))));
                    uv.Add(corner.UV.X / tex.Width); uv.Add(1 - corner.UV.Y / tex.Height);
                    bones.Add(bone); normalBones.Add(normalBone); return index;
                }
                foreach (var triangle in group)
                {
                    token.ThrowIfCancellationRequested();
                    // Basis has determinant -1, so reverse each triangle.
                    indices.Add(Corner(triangle.A)); indices.Add(Corner(triangle.C)); indices.Add(Corner(triangle.B));
                }
                submeshes.Add(new() { slot = group.Key, texture = texture, indices = indices.ToArray() });
            }
            meshes.Add(new() { body = body, variant = variant, skin = skin, name = part.Name,
                positions = positions.ToArray(), normals = normals.ToArray(), uv = uv.ToArray(), boneIndices = bones.ToArray(), normalBoneIndices = normalBones.ToArray(),
                weights = Enumerable.Repeat(1f, bones.Count).ToArray(), submeshes = submeshes.ToArray() });
        }
        var clips = new List<ClipData>();
        for (int sequence = 0; sequence < model.Sequences.Length; sequence++)
        {
            var seq = model.Sequences[sequence];
            // Preserve the descriptor in the report; zero FPS has no defined playback time.
            if (seq.Fps == 0) continue;
            for (int blend = 0; blend < seq.Blends; blend++)
            {
                var positions = new float[checked(seq.Frames * model.Bones.Length * 3)];
                var rotations = new float[checked(seq.Frames * model.Bones.Length * 4)];
                var previous = new Quaternion[model.Bones.Length];
                for (int frame = 0; frame < seq.Frames; frame++)
                {
                    token.ThrowIfCancellationRequested(); var pose = model.GetLocalPose(sequence, frame, blend);
                    for (int bone = 0; bone < pose.Length; bone++)
                    {
                        int sample = frame * pose.Length + bone;
                        V(ConvertDirection(pose[bone].Position) * metersPerUnit).CopyTo(positions, sample * 3);
                        var rotation = ConvertRotation(pose[bone].Rotation);
                        if (frame > 0 && Quaternion.Dot(previous[bone], rotation) < 0) rotation = -rotation;
                        previous[bone] = rotation; Q(rotation).CopyTo(rotations, sample * 4);
                    }
                }
                clips.Add(new() { sequence = sequence, blend = blend, name = seq.Name, fps = seq.Fps, frames = seq.Frames,
                    flags = seq.Flags, loop = (seq.Flags & 1) != 0, positions = positions, rotations = rotations });
            }
        }
        return new()
        {
            version = 1, name = model.Name, axes = "Unity: +X right, +Y up, +Z forward; source (-Y,Z,X); winding reversed", units = "meters", metersPerUnit = metersPerUnit,
            bones = model.Bones.Select((b, i) => new BoneData { id = i, name = b.Name, parent = b.Parent,
                position = V(ConvertDirection(reference[i].Position) * metersPerUnit), rotation = Q(ConvertRotation(reference[i].Rotation)) }).ToArray(),
            textures = model.Textures.Select((t, i) => new TextureData { id = i, name = t.Name, width = t.Width, height = t.Height, flags = t.Flags, file = $"textures/texture_{i:D4}.png" }).ToArray(),
            bodies = model.Bodies.Select((b, i) => new BodyData { id = i, name = b.Name, variants = b.Variants.Select(p => p.Name).ToArray() }).ToArray(),
            skins = model.Skins.Select(s => new SkinData { textures = s.Select(t => (int)t).ToArray() }).ToArray(),
            meshes = meshes.ToArray(), animations = clips.ToArray(),
            attachments = model.Attachments.Select((a, i) => new AttachmentData { id = i, name = a.Name, bone = a.Bone,
                position = V(ConvertDirection(a.Position) * metersPerUnit), orientation = a.Orientation.SelectMany(v => V(ConvertDirection(v))).ToArray() }).ToArray(),
            events = model.Events.Select(e => new EventData { sequence = e.Sequence, frame = e.Frame, code = e.Code, options = e.Options }).ToArray()
        };
    }

    public static string Export(ModelBundle bundle, string destination, float metersPerUnit = .0254f, CancellationToken token = default)
    {
        if (bundle.Missing.Count > 0) throw new InvalidDataException("Faltan compañeros: " + string.Join(", ", bundle.Missing));
        var data = Build(bundle.Model, metersPerUnit, token);
        SafeOutput.CheckDestination(destination, [bundle.Main.Origin.Split(" :: ")[0]]);
        string folder = Path.Combine(Path.GetFullPath(destination), "unity-model-" + Guid.NewGuid().ToString("N"));
        SafeOutput.CreateDirectory(Path.Combine(folder, "textures"));
        var written = new List<string>();
        void Write(string relative, Action<Stream> action)
        {
            token.ThrowIfCancellationRequested(); string path = Path.Combine(folder, relative);
            SafeOutput.RejectLinks(path);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            written.Add(path); action(stream);
        }
        try
        {
            foreach (var texture in data.textures)
                Write(texture.file, stream => { using var bitmap = ModelRenderer.TextureBitmap(bundle.Model.Textures[texture.id]); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); });
            Write("model.json", stream => JsonSerializer.Serialize(stream, data, Json));
            Write("export-report.json", stream => JsonSerializer.Serialize(stream, new
            {
                version = 1, origin = bundle.Main.Origin, sourceVersion = bundle.Main.OriginalVersion,
                sequences = bundle.Model.Sequences.Select((s, i) => new { id = i, s.Name, s.Fps, s.Frames, s.Blends, s.Flags }),
                cornersWithDifferentNormalBone = data.meshes.Sum(m => m.boneIndices.Where((b, i) => b != m.normalBoneIndices[i]).Count()),
                warnings = bundle.Model.Warnings.Concat(bundle.Model.Sequences.Where(s => s.Fps == 0).Select(s => $"Secuencia omitida por FPS cero: {s.Name}")),
                limitations = new[] { "Eventos conservados como metadatos; requieren adaptación manual.",
                    "Materiales GoldSrc/Nexon aproximados; no se exporta lógica de partículas ni controladores de huesos.",
                    "Los blends se exportan como clips separados, sin reconstruir controladores de mezcla.",
                    "Unity anima las normales con el hueso del vértice. Las normales con otro hueso solo coinciden exactamente en la pose de referencia.",
                    "Attachments conservan sus tres vectores; las bases degeneradas requieren orientación manual.",
                    "Escala predeterminada: una unidad GoldSrc equivale a 0.0254 metros; confirmar para el modelo." }
            }, Json));
            return folder;
        }
        catch
        {
            foreach (var file in written) { SafeOutput.RejectLinks(file); File.Delete(file); }
            SafeOutput.RejectLinks(folder); Directory.Delete(Path.Combine(folder, "textures")); Directory.Delete(folder);
            throw;
        }
    }
}
