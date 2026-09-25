using System.Numerics;

namespace CSNZWorkshop.Models;

public static class ModelComparison
{
    public static (Vector3 Center, float Radius) Frame(StudioModel left, StudioModel right, int leftSequence, int rightSequence)
    {
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        foreach (var (model, sequence) in new[] { (left, leftSequence), (right, rightSequence) })
        {
            int count = sequence >= 0 ? model.Sequences[sequence].Frames : 1;
            for (int f = 0; f < count; f += Math.Max(1, count / 8))
            {
                var pose = model.Pose(sequence, f);
                foreach (var part in model.Bodies.Select(b => b.Variants.FirstOrDefault()).Where(p => p is not null))
                    for (int i = 0; i < part!.Vertices.Length; i++)
                    {
                        var v = Vector3.Transform(part.Vertices[i], pose[part.BoneIndices[i]]);
                        min = Vector3.Min(min, v); max = Vector3.Max(max, v);
                    }
            }
        }
        return float.IsFinite(min.X) ? ((min + max) * .5f, Math.Max((max - min).Length() * .5f, .01f)) : (Vector3.Zero, 1);
    }

    public static IReadOnlyList<string[]> Rows(ModelBundle left, ModelBundle right)
    {
        var rows = new List<string[]>();
        void Add(string label, object a, object b) => rows.Add([label, a.ToString() ?? "", b.ToString() ?? ""]);
        var a = left.Model; var b = right.Model;
        Add("Huesos", a.Bones.Length, b.Bones.Length); Add("Texturas", a.Textures.Length, b.Textures.Length);
        Add("Secuencias", a.Sequences.Length, b.Sequences.Length);
        Add("Hitboxes", new ModelBytes(a.Data).Int(156), new ModelBytes(b.Data).Int(156));
        Add("Attachments", new ModelBytes(a.Data).Int(212), new ModelBytes(b.Data).Int(212));
        for (int i = 0; i < Math.Max(a.Sequences.Length, b.Sequences.Length); i++)
        {
            string Seq(StudioModel m) => i >= m.Sequences.Length ? "Ausente" : $"{m.Sequences[i].Name} · {m.Sequences[i].Frames} frames · {m.Sequences[i].Fps} FPS · {m.Sequences[i].Blends} blends";
            Add("Secuencia #" + i, Seq(a), Seq(b));
        }
        for (int i = 0; i < Math.Max(a.Bones.Length, b.Bones.Length); i++)
        {
            string Bone(StudioModel m) => i >= m.Bones.Length ? "Ausente" : $"{m.Bones[i].Name} · padre {m.Bones[i].Parent} · posición {m.Bones[i].Position}";
            Add("Hueso #" + i, Bone(a), Bone(b));
        }
        foreach (var (label, countAt, offsetAt, stride) in new[] { ("Hitbox", 156, 160, 32), ("Attachment", 212, 216, 88) })
        {
            var ra = new ModelBytes(a.Data); var rb = new ModelBytes(b.Data);
            for (int i = 0; i < Math.Max(ra.Int(countAt), rb.Int(countAt)); i++)
            {
                string Describe(ModelBytes r)
                {
                    if (i >= r.Int(countAt)) return "Ausente";
                    int at = r.Int(offsetAt) + i * stride;
                    return label == "Hitbox" ? $"hueso {r.Int(at)} · grupo {r.Int(at + 4)} · {r.Vector(at + 8)} → {r.Vector(at + 20)}"
                        : $"{r.Text(at, 32)} · hueso {r.Int(at + 36)} · posición {r.Vector(at + 40)}";
                }
                Add(label + " #" + i, Describe(ra), Describe(rb));
            }
        }
        return rows;
    }
}
