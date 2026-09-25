using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CSNZWorkshop.Models;

public sealed record ViewSettings(float Yaw = 0.55f, float Pitch = -0.2f, float Zoom = 1,
    int Sequence = -1, int Frame = 0, int Skin = 0, int[]? Variants = null, bool Wireframe = false,
    Vector3? SharedCenter = null, float? SharedRadius = null, int[]? AdditiveTextures = null, bool NexonEffects = true,
    float PanX = 0, float PanY = 0, bool ShowBones = false, int Background = unchecked((int)0xff202833),
    bool FocusGeometry = false, bool HideEffects = false, bool FirstPerson = false, float FieldOfView = 90, int[]? FramingGroups = null,
    int[]? FadingGroups = null, float EffectIntensity = 1);

public static class ModelRenderer
{
    // Observed on v_gungnirex: effect textures use this prefix with standard flags = 0.
    // This is a preview heuristic, not a complete decoder of Nexon's material system.
    public static bool IsNexonEffect(StudioTexture texture) => texture.Name.StartsWith("$0b", StringComparison.OrdinalIgnoreCase);
    public static Bitmap TextureBitmap(StudioTexture texture) => BitmapFrom(texture.Width, texture.Height, texture.Pixels);

    public static Bitmap Render(StudioModel model, int width, int height, ViewSettings settings)
    {
        float resolutionScale = Math.Min(1f, Math.Min(1200f / Math.Max(1, width), 900f / Math.Max(1, height)));
        width = Math.Max(1, (int)(Math.Max(1, width) * resolutionScale)); height = Math.Max(1, (int)(Math.Max(1, height) * resolutionScale));
        int[] pixels = new int[width * height];
        Array.Fill(pixels, settings.Background | unchecked((int)0xff000000));
        float[] depth = new float[pixels.Length]; Array.Fill(depth, float.PositiveInfinity);
        var pose = model.Pose(settings.Sequence, settings.Frame);
        var selected = model.Bodies.Select((b, i) => b.Variants.Length == 0 ? null : b.Variants[
            Math.Clamp(settings.Variants is not null && i < settings.Variants.Length ? settings.Variants[i] : 0, 0, b.Variants.Length - 1)])
            .Where(p => p is not null).Select(p => p!).ToArray();
        var selectedGroups = model.Bodies.Select((b, i) => (b, i)).Where(pair => pair.b.Variants.Length > 0).Select(pair => pair.i).ToArray();
        int TextureId(StudioTriangle triangle) => model.Skins.Length > 0 && triangle.Skin < model.Skins[0].Length
            ? model.Skins[Math.Clamp(settings.Skin, 0, model.Skins.Length - 1)][triangle.Skin] : -1;
        bool Effect(StudioTriangle triangle)
        {
            int id = TextureId(triangle);
            return id >= 0 && id < model.Textures.Length && ((model.Textures[id].Flags & 0x20) != 0
                || IsNexonEffect(model.Textures[id]) || settings.AdditiveTextures?.Contains(id) == true);
        }
        var framingParts = model.Bodies.SelectMany((body, i) => settings.FramingGroups?.Contains(i) == true ? body.Variants
            : body.Variants.Length == 0 ? [] : new[] { body.Variants[Math.Clamp(settings.Variants is not null && i < settings.Variants.Length ? settings.Variants[i] : 0, 0, body.Variants.Length - 1)] }).ToArray();
        var framingVertices = framingParts.Select(part => part.Triangles.Where(t => !settings.FocusGeometry || !Effect(t))
            .SelectMany(t => new[] { t.A.Vertex, t.B.Vertex, t.C.Vertex }).Distinct().ToArray()).ToArray();
        if (framingVertices.All(v => v.Length == 0))
            framingVertices = framingParts.Select(part => Enumerable.Range(0, part.Vertices.Length).ToArray()).ToArray();
        var vertices = selected.Select(part => part.Vertices.Select((v, i) => Vector3.Transform(v, pose[part.BoneIndices[i]])).ToArray()).ToArray();
        // Stable framing samples the whole selected animation: the zoom stays fixed while it plays and
        // sequences that move the root bone keep the model inside the view.
        var framing = new List<Matrix4x4[]>();
        if (settings.Sequence >= 0 && settings.Sequence < model.Sequences.Length)
        {
            int frames = model.Sequences[settings.Sequence].Frames;
            for (int frame = 0; frame < frames; frame += Math.Max(1, frames / 8)) framing.Add(model.Pose(settings.Sequence, frame));
        }
        else framing.Add(model.Pose());
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        foreach (var transforms in framing)
            for (int p = 0; p < framingParts.Length; p++)
                foreach (int i in framingVertices[p])
                { var part = framingParts[p]; var v = Vector3.Transform(part.Vertices[i], transforms[part.BoneIndices[i]]); min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        if (!float.IsFinite(min.X)) return BitmapFrom(width, height, pixels);
        Vector3 center = settings.SharedCenter ?? (min + max) * 0.5f;
        float radius = settings.SharedRadius ?? Math.Max((max - min).Length() * .5f, .01f);
        float scale = Math.Min(width, height) * .42f / radius * Math.Clamp(settings.Zoom, .05f, 20);
        var rotation = Matrix4x4.CreateRotationZ(settings.Yaw) * Matrix4x4.CreateRotationX(settings.Pitch);
        Vector3 Project(Vector3 vertex)
        {
            if (settings.FirstPerson)
            {
                var oriented = Vector3.Transform(vertex, rotation);
                float distance = oriented.X;
                if (distance < .1f) return new(float.NaN);
                float focal = width * .5f / MathF.Tan(Math.Clamp(settings.FieldOfView, 40, 120) * MathF.PI / 360) * settings.Zoom;
                return new(width * (.5f + settings.PanX) - oriented.Y * focal / distance,
                    height * (.5f + settings.PanY) - oriented.Z * focal / distance, distance);
            }
            var camera = Vector3.Transform(vertex - center, rotation);
            return new Vector3(width * (.5f + settings.PanX) + camera.X * scale,
                height * (.5f + settings.PanY) - camera.Z * scale, camera.Y);
        }
        var lines = new List<(PointF, PointF, PointF)>();
        IEnumerable<(Vector3 Point, Vector2 UV)[]> ProjectTriangle(StudioTriangle triangle, Vector3[] world, Vector3[] points)
        {
            var corners = new[] { triangle.A, triangle.B, triangle.C };
            if (!settings.FirstPerson) { yield return corners.Select(c => (points[c.Vertex], c.UV)).ToArray(); yield break; }
            // Clip to the near plane before projecting, keeping interpolated texture coordinates.
            var polygon = corners.Select(c => (Point: world[c.Vertex], c.UV)).ToArray();
            var clipped = new List<(Vector3 Point, Vector2 UV)>();
            const float near = .101f;
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                float da = Vector3.Transform(a.Point, rotation).X, db = Vector3.Transform(b.Point, rotation).X;
                if (da >= near) clipped.Add(a);
                if ((da >= near) != (db >= near))
                {
                    float t = (near - da) / (db - da);
                    clipped.Add((Vector3.Lerp(a.Point, b.Point, t), Vector2.Lerp(a.UV, b.UV, t)));
                }
            }
            for (int i = 1; i + 1 < clipped.Count; i++)
                yield return new[] { clipped[0], clipped[i], clipped[i + 1] }.Select(v => (Project(v.Point), v.UV)).ToArray();
        }
        // Opaque geometry establishes depth first. Additive effects test that depth
        // but do not write it, so their black texels cannot hide other surfaces.
        for (int pass = 0; pass < (settings.Wireframe ? 1 : 2); pass++)
        for (int p = 0; p < selected.Length; p++)
        {
            var points = vertices[p].Select(v =>
            {
                return Project(v);
            }).ToArray();
            foreach (var triangle in selected[p].Triangles)
            {
                if (settings.HideEffects && Effect(triangle)) continue;
                foreach (var projected in ProjectTriangle(triangle, vertices[p], points))
                {
                Vector3 a = projected[0].Point, b = projected[1].Point, c = projected[2].Point;
                if (!float.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z + c.X + c.Y + c.Z)) continue;
                if (settings.Wireframe)
                {
                    lines.Add((new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y))); continue;
                }
                int id = model.Skins.Length > 0 && triangle.Skin < model.Skins[0].Length
                    ? model.Skins[Math.Clamp(settings.Skin, 0, model.Skins.Length - 1)][triangle.Skin] : -1;
                StudioTexture? texture = id >= 0 && id < model.Textures.Length ? model.Textures[id] : null;
                bool nexonEffect = texture is not null && settings.NexonEffects && IsNexonEffect(texture);
                bool additive = texture is not null && ((texture.Flags & 0x20) != 0 || nexonEffect || settings.AdditiveTextures?.Contains(id) == true);
                if (additive != (pass == 1)) continue;
                float area = Edge(a, b, c.X, c.Y);
                if (Math.Abs(area) < 0.001f) continue;
                int left = (int)Math.Clamp(MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, width - 1);
                int right = (int)Math.Clamp(MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, width - 1);
                int top = (int)Math.Clamp(MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, height - 1);
                int bottom = (int)Math.Clamp(MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, height - 1);
                Vector3 normal = Vector3.Cross(vertices[p][triangle.B.Vertex] - vertices[p][triangle.A.Vertex], vertices[p][triangle.C.Vertex] - vertices[p][triangle.A.Vertex]);
                float light = normal.LengthSquared() < 1e-10 ? 1 : .35f + .65f * Math.Abs(Vector3.Dot(Vector3.Normalize(normal), Vector3.Normalize(new Vector3(.2f, -.8f, .6f))));
                if (texture is not null && ((texture.Flags & 4) != 0 || nexonEffect)) light = 1;
                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++)
                    {
                        float wa = Edge(b, c, x + .5f, y + .5f) / area;
                        float wb = Edge(c, a, x + .5f, y + .5f) / area;
                        float wc = Edge(a, b, x + .5f, y + .5f) / area;
                        if (wa < 0 || wb < 0 || wc < 0) continue;
                        // A shared edge belongs to only one triangle (avoids a bright seam
                        // when two additive triangles form an effect quad).
                        if (wa == 0 && !OwnsEdge(b, c, area) || wb == 0 && !OwnsEdge(c, a, area)
                            || wc == 0 && !OwnsEdge(a, b, area)) continue;
                        float z = wa * a.Z + wb * b.Z + wc * c.Z;
                        if (settings.FirstPerson)
                        {
                            float inverseDepth = wa / a.Z + wb / b.Z + wc / c.Z;
                            z = 1 / inverseDepth;
                            wa = wa / a.Z * z; wb = wb / b.Z * z; wc = wc / c.Z * z;
                        }
                        int pixel = y * width + x;
                        if (z >= depth[pixel]) continue;
                        int color = unchecked((int)0xffb7c9db);
                        if (texture is not null)
                        {
                            var uv = wa * projected[0].UV + wb * projected[1].UV + wc * projected[2].UV;
                            int u = ((int)MathF.Floor(uv.X) % texture.Width + texture.Width) % texture.Width;
                            int v = ((int)MathF.Floor(uv.Y) % texture.Height + texture.Height) % texture.Height;
                            color = texture.Pixels[v * texture.Width + u];
                            if ((uint)color >> 24 == 0) continue;
                        }
                        int red = (int)(((color >> 16) & 255) * light), green = (int)(((color >> 8) & 255) * light), blue = (int)((color & 255) * light);
                        if (additive)
                        {
                            float intensity = settings.FadingGroups?.Contains(selectedGroups[p]) == true ? Math.Clamp(settings.EffectIntensity, 0, 1) : 1;
                            red = (int)(red * intensity); green = (int)(green * intensity); blue = (int)(blue * intensity);
                            int behind = pixels[pixel];
                            red = Math.Min(255, red + ((behind >> 16) & 255));
                            green = Math.Min(255, green + ((behind >> 8) & 255));
                            blue = Math.Min(255, blue + (behind & 255));
                        }
                        pixels[pixel] = unchecked((int)0xff000000) | red << 16 | green << 8 | blue;
                        if (!additive) depth[pixel] = z;
                    }
                }
            }
        }
        var bitmap = BitmapFrom(width, height, pixels);
        if (settings.ShowBones)
        {
            using var graphics = Graphics.FromImage(bitmap);
            using var pen = new Pen(Color.FromArgb(255, 200, 85), 1.5f);
            using var brush = new SolidBrush(Color.FromArgb(80, 230, 220));
            for (int i = 0; i < model.Bones.Length; i++)
            {
                var point = Project(pose[i].Translation);
                if (!float.IsFinite(point.X + point.Y)) continue;
                int parent = model.Bones[i].Parent;
                if (parent >= 0)
                {
                    var other = Project(pose[parent].Translation);
                    if (float.IsFinite(other.X + other.Y)) graphics.DrawLine(pen, point.X, point.Y, other.X, other.Y);
                }
                graphics.FillEllipse(brush, point.X - 2, point.Y - 2, 4, 4);
            }
        }
        if (settings.Wireframe)
        {
            using var graphics = Graphics.FromImage(bitmap);
            using var pen = new Pen(Color.LightSteelBlue, .6f);
            foreach (var (a, b, c) in lines) { graphics.DrawLine(pen, a, b); graphics.DrawLine(pen, b, c); graphics.DrawLine(pen, c, a); }
        }
        return bitmap;
    }

    static float Edge(Vector3 a, Vector3 b, float x, float y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
    static bool OwnsEdge(Vector3 a, Vector3 b, float area)
    {
        if (area < 0) (a, b) = (b, a);
        return a.Y < b.Y || a.Y == b.Y && a.X > b.X;
    }

    static Bitmap BitmapFrom(int width, int height, int[] pixels)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length); }
        finally { bitmap.UnlockBits(bits); }
        return bitmap;
    }
}
