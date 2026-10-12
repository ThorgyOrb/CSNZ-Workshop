using System.Text.RegularExpressions;

namespace CSNZWorkshop.Models;

public readonly record struct GeometrySectionKey(int Body, int Variant, int Slot);
public sealed record GeometrySection(GeometrySectionKey Key, string Label, int Triangles, bool SuggestedExclude, string Reason);

public static class WeaponGeometry
{
    static readonly Regex Arms = new(@"(^|[^a-z])(hands?|arms?|forearms?|gloves?|sleeves?)([^a-z]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static GeometrySection[] Sections(StudioModel model)
    {
        var result = new List<GeometrySection>();
        for (int body = 0; body < model.Bodies.Length; body++)
        for (int variant = 0; variant < model.Bodies[body].Variants.Length; variant++)
        {
            var group = model.Bodies[body]; var part = group.Variants[variant];
            foreach (var mesh in part.Triangles.GroupBy(t => t.Skin))
            {
                var materials = model.Skins.Select(s => mesh.Key < s.Length ? s[mesh.Key] : -1).Distinct().ToArray();
                var names = materials.Select(id => id >= 0 && id < model.Textures.Length ? model.Textures[id].Name : "sin textura").ToArray();
                bool byPart = Arms.IsMatch(group.Name) || Arms.IsMatch(part.Name);
                // A slot may change texture with the skin. Suggest it only if all textures identify hands/arms.
                bool byMaterial = names.Length > 0 && names.All(n => Arms.IsMatch(n));
                string reason = byPart ? "Nombre del grupo/submodelo identifica brazos o manos" : byMaterial ? "Todas las skins del material identifican brazos o manos" : "Selección manual; no identificado como brazos/manos";
                result.Add(new(new(body, variant, mesh.Key), $"[{body}:{variant}:{mesh.Key}] {group.Name} / {part.Name} / {string.Join(", ", names)}", mesh.Count(), byPart || byMaterial, reason));
            }
        }
        return result.ToArray();
    }

    public static StudioModel Apply(StudioModel original, IEnumerable<GeometrySectionKey> excluded)
    {
        var remove = excluded.ToHashSet(); var known = Sections(original).Select(s => s.Key).ToHashSet();
        if (!remove.IsSubsetOf(known)) throw new ArgumentException("La selección contiene una parte que no pertenece al modelo.", nameof(excluded));
        // Geometry is filtered; bone IDs, parents, animation bytes, blends and attachments are unchanged.
        var result = new StudioModel
        {
            Data = original.Data, Name = original.Name, Bones = original.Bones, Textures = original.Textures,
            Sequences = original.Sequences, Skins = original.Skins, GroupNames = original.GroupNames,
            Bodies = original.Bodies.Select((b, body) => new StudioBody(b.Name, b.Variants.Select((p, variant) => p with
            {
                Triangles = p.Triangles.Where(t => !remove.Contains(new(body, variant, t.Skin))).ToArray()
            }).ToArray())).ToArray()
        };
        result.Events.AddRange(original.Events); result.Attachments.AddRange(original.Attachments); result.Warnings.AddRange(original.Warnings);
        foreach (var group in original.ExternalGroups) result.ExternalGroups.Add(group.Key, group.Value);
        return result;
    }
}
