using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CSNZWorkshop.Models;

public sealed record ViewSettings(float Yaw = 0.55f, float Pitch = -0.2f, float Zoom = 1,
    int Sequence = -1, int Frame = 0, int Skin = 0, int[]? Variants = null, bool Wireframe = false);

public static class ModelRenderer
{
    public static Bitmap TextureBitmap(StudioTexture texture) => BitmapFrom(texture.Width, texture.Height, texture.Pixels);

    public static Bitmap Render(StudioModel model, int width, int height, ViewSettings settings)
    {
        width = Math.Clamp(width, 64, 900); height = Math.Clamp(height, 64, 700);
        int[] pixels = new int[width * height];
        Array.Fill(pixels, unchecked((int)0xff202833));
        float[] depth = new float[pixels.Length]; Array.Fill(depth, float.PositiveInfinity);
        var pose = model.Pose(settings.Sequence, settings.Frame);
        var selected = model.Bodies.Select((b, i) => b.Variants.Length == 0 ? null : b.Variants[
            Math.Clamp(settings.Variants is not null && i < settings.Variants.Length ? settings.Variants[i] : 0, 0, b.Variants.Length - 1)])
            .Where(p => p is not null).Select(p => p!).ToArray();
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
            foreach (var part in selected)
                for (int i = 0; i < part.Vertices.Length; i++)
                { var v = Vector3.Transform(part.Vertices[i], transforms[part.BoneIndices[i]]); min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        if (!float.IsFinite(min.X)) return BitmapFrom(width, height, pixels);
        Vector3 center = (min + max) * 0.5f;
        float radius = Math.Max((max - min).Length() * .5f, .01f);
        float scale = Math.Min(width, height) * .42f / radius * Math.Clamp(settings.Zoom, .05f, 20);
        var rotation = Matrix4x4.CreateRotationZ(settings.Yaw) * Matrix4x4.CreateRotationX(settings.Pitch);
        var lines = new List<(PointF, PointF, PointF)>();
        for (int p = 0; p < selected.Length; p++)
        {
            var points = vertices[p].Select(v =>
            {
                var camera = Vector3.Transform(v - center, rotation);
                return new Vector3(width * .5f + camera.X * scale, height * .5f - camera.Z * scale, camera.Y);
            }).ToArray();
            foreach (var triangle in selected[p].Triangles)
            {
                Vector3 a = points[triangle.A.Vertex], b = points[triangle.B.Vertex], c = points[triangle.C.Vertex];
                if (!float.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z + c.X + c.Y + c.Z)) continue;
                if (settings.Wireframe)
                {
                    lines.Add((new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y))); continue;
                }
                int id = model.Skins.Length > 0 && triangle.Skin < model.Skins[0].Length
                    ? model.Skins[Math.Clamp(settings.Skin, 0, model.Skins.Length - 1)][triangle.Skin] : -1;
                StudioTexture? texture = id >= 0 && id < model.Textures.Length ? model.Textures[id] : null;
                float area = Edge(a, b, c.X, c.Y);
                if (Math.Abs(area) < 0.001f) continue;
                int left = (int)Math.Clamp(MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, width - 1);
                int right = (int)Math.Clamp(MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, width - 1);
                int top = (int)Math.Clamp(MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, height - 1);
                int bottom = (int)Math.Clamp(MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, height - 1);
                Vector3 normal = Vector3.Cross(vertices[p][triangle.B.Vertex] - vertices[p][triangle.A.Vertex], vertices[p][triangle.C.Vertex] - vertices[p][triangle.A.Vertex]);
                float light = normal.LengthSquared() < 1e-10 ? 1 : .35f + .65f * Math.Abs(Vector3.Dot(Vector3.Normalize(normal), Vector3.Normalize(new Vector3(.2f, -.8f, .6f))));
                if (texture is not null && (texture.Flags & 4) != 0) light = 1;
                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++)
                    {
                        float wa = Edge(b, c, x + .5f, y + .5f) / area;
                        float wb = Edge(c, a, x + .5f, y + .5f) / area;
                        float wc = 1 - wa - wb;
                        if (wa < 0 || wb < 0 || wc < 0) continue;
                        float z = wa * a.Z + wb * b.Z + wc * c.Z;
                        int pixel = y * width + x;
                        if (z >= depth[pixel]) continue;
                        int color = unchecked((int)0xffb7c9db);
                        if (texture is not null)
                        {
                            var uv = wa * triangle.A.UV + wb * triangle.B.UV + wc * triangle.C.UV;
                            int u = ((int)MathF.Floor(uv.X) % texture.Width + texture.Width) % texture.Width;
                            int v = ((int)MathF.Floor(uv.Y) % texture.Height + texture.Height) % texture.Height;
                            color = texture.Pixels[v * texture.Width + u];
                            if ((uint)color >> 24 == 0) continue;
                        }
                        int red = (int)(((color >> 16) & 255) * light), green = (int)(((color >> 8) & 255) * light), blue = (int)((color & 255) * light);
                        pixels[pixel] = unchecked((int)0xff000000) | red << 16 | green << 8 | blue;
                        depth[pixel] = z;
                    }
            }
        }
        var bitmap = BitmapFrom(width, height, pixels);
        if (settings.Wireframe)
        {
            using var graphics = Graphics.FromImage(bitmap);
            using var pen = new Pen(Color.LightSteelBlue, .6f);
            foreach (var (a, b, c) in lines) { graphics.DrawLine(pen, a, b); graphics.DrawLine(pen, b, c); graphics.DrawLine(pen, c, a); }
        }
        return bitmap;
    }

    static float Edge(Vector3 a, Vector3 b, float x, float y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);

    static Bitmap BitmapFrom(int width, int height, int[] pixels)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length); }
        finally { bitmap.UnlockBits(bits); }
        return bitmap;
    }
}
