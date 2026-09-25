namespace CSNZWorkshop.Models;

// Optional viewer simulation. Bodygroup switching rules are not encoded by MDL sequences.
public static class EffectPreview
{
    public static float Fade(double milliseconds, int interval)
    {
        double phase = Math.Max(0, milliseconds) / Math.Max(1, interval) % 1;
        // Zero value and zero slope at each variant boundary: no hard flash on switching.
        double wave = Math.Sin(Math.PI * phase);
        return (float)(wave * wave);
    }
    public static int[] VisibleVariants(StudioBody body) => body.Variants.Select((v, i) => (v, i))
        .Where(p => p.v.Triangles.Length > 0).Select(p => p.i).ToArray();

    public static bool Suggested(StudioModel model, StudioBody body)
    {
        if (VisibleVariants(body).Length == 0) return false;
        if (body.Name.Contains("effect", StringComparison.OrdinalIgnoreCase) || body.Name.Equals("fx", StringComparison.OrdinalIgnoreCase)) return true;
        var triangles = body.Variants.SelectMany(v => v.Triangles).ToArray();
        return triangles.Length > 0 && triangles.All(t => model.Skins.Length > 0 && t.Skin < model.Skins[0].Length
            && model.Skins[0][t.Skin] is var id && id >= 0 && id < model.Textures.Length
            && ((model.Textures[id].Flags & 0x20) != 0 || ModelRenderer.IsNexonEffect(model.Textures[id])));
    }

    public static int[] Resolve(StudioModel model, int[] manual, IEnumerable<int> groups, double milliseconds, int interval)
    {
        var result = (int[])manual.Clone();
        foreach (int group in groups)
        {
            if (group < 0 || group >= model.Bodies.Length || group >= result.Length) continue;
            var choices = VisibleVariants(model.Bodies[group]);
            if (choices.Length > 0) result[group] = choices[(int)(Math.Max(0, milliseconds) / Math.Max(1, interval) % choices.Length)];
        }
        return result;
    }
}
