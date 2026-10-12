using CSNZWorkshop.Models;

static class WeaponChecks
{
    static void Assert(bool value) { if (!value) throw new Exception("Weapon geometry assertion failed"); }
    static StudioModel Fixture()
    {
        var source = StudioModel.Parse(Synthetic.Model()); var part = source.Bodies[0].Variants[0]; var texture = source.Textures[0];
        return new()
        {
            Data = source.Data, Name = source.Name,
            Bones = [source.Bones[0] with { Name = "hand_root_also_moves_weapon" }, source.Bones[1]],
            Textures = [texture with { Name = "weapon.bmp" }, texture with { Name = "CSO_Hand_Male.bmp" }, texture with { Name = "CSO_Hand_Female.bmp" }],
            Bodies = [new("weapon", [part]), new("hands", [part with { Name = "male", Triangles = part.Triangles.Select(t => t with { Skin = 1 }).ToArray() },
                part with { Name = "female", Triangles = part.Triangles.Select(t => t with { Skin = 2 }).ToArray() }])],
            Sequences = source.Sequences, Skins = [[0, 1, 2]], GroupNames = source.GroupNames
        };
    }
    public static void Run(string root, Action<string, Action> check)
    {
        check("Solo arma: quita ambas variantes de manos y conserva huesos y animaciones", () =>
        {
            var model = Fixture(); var keys = WeaponGeometry.Sections(model).Where(s => s.SuggestedExclude).Select(s => s.Key).ToArray(); Assert(keys.Length == 2);
            var filtered = WeaponGeometry.Apply(model, keys);
            Assert(filtered.Bodies[0].Variants[0].Triangles.Length == 2 && filtered.Bodies[1].Variants.All(p => p.Triangles.Length == 0));
            Assert(model.Bodies[1].Variants.All(p => p.Triangles.Length == 2));
            Assert(filtered.Bones.Length == model.Bones.Length && filtered.Bones[0].Name.StartsWith("hand_root"));
            for (int frame = 0; frame < model.Sequences[0].Frames; frame++) Assert(model.Pose(0, frame).SequenceEqual(filtered.Pose(0, frame)));
            var full = UnityModelExporter.Build(model); var weapon = UnityModelExporter.Build(model, excludedGeometry: keys);
            Assert(weapon.meshes.Where(m => m.body == 1).All(m => m.positions.Length == 0 && m.submeshes.Length == 0));
            Assert(full.bones.Length == weapon.bones.Length && weapon.animations.Length == full.animations.Length);
            Assert(full.animations[0].positions.SequenceEqual(weapon.animations[0].positions) && full.animations[0].rotations.SequenceEqual(weapon.animations[0].rotations));
            Assert(weapon.geometrySections.Count(s => s.excluded) == 2 && weapon.meshes[0].positions.SequenceEqual(full.meshes[0].positions));
        });
        check("Solo arma: materiales dentro de una parte mixta y skins ambiguas", () =>
        {
            var model = Fixture(); var original = model.Bodies[0].Variants[0];
            model.Bodies[0].Variants[0] = original with { Triangles = [original.Triangles[0], original.Triangles[1] with { Skin = 1 }] };
            var suggested = WeaponGeometry.Sections(model).Where(s => s.Key.Body == 0 && s.SuggestedExclude).Select(s => s.Key).ToArray(); Assert(suggested.Length == 1);
            var filtered = WeaponGeometry.Apply(model, suggested); Assert(filtered.Bodies[0].Variants[0].Triangles.Length == 1 && filtered.Bodies[0].Variants[0].Triangles[0].Skin == 0);
            // An arm texture in one skin and a weapon texture in another isn't a reliable automatic cut.
            var multiSkin = new StudioModel { Data = model.Data, Name = model.Name, Bones = model.Bones, Textures = model.Textures,
                Bodies = model.Bodies, Sequences = model.Sequences, Skins = [[0, 1, 2], [0, 0, 2]], GroupNames = model.GroupNames };
            Assert(!WeaponGeometry.Sections(multiSkin).Single(s => s.Key == suggested[0]).SuggestedExclude);
            model.Textures[0] = model.Textures[0] with { Name = "armageddon_weapon.bmp" };
            Assert(!WeaponGeometry.Sections(model).Single(s => s.Key.Body == 0 && s.Key.Slot == 0).SuggestedExclude);
        });
        check("Solo arma: rechaza selección inválida o arma vacía", () =>
        {
            var model = Fixture();
            try { WeaponGeometry.Apply(model, [new(999, 0, 0)]); throw new Exception("Invalid section accepted"); } catch (ArgumentException) { }
            var all = WeaponGeometry.Sections(model).Select(s => s.Key).ToArray();
            try { UnityModelExporter.Build(model, excludedGeometry: all); throw new Exception("Empty weapon exported"); } catch (InvalidDataException) { }
            Assert(WeaponGeometry.Apply(model, []).Bodies.Sum(b => b.Variants.Sum(v => v.Triangles.Length)) == 6);
        });
        check("Interfaz Solo arma: selección sugerida, restaurar y vista previa de animación", () =>
        {
            var model = Fixture(); var bundle = new ModelBundle { Model = model, Main = new("test.mdl", model.Data, 10, Path.Combine(root, "test.mdl")) };
            UiChecks.VerifyWeaponExport(bundle);
        });
    }
    public static void Guillotine(string file, string destination)
    {
        var bundle = ModelBundle.FromFile(file); var model = bundle.Model;
        Assert(Path.GetFileName(file).Equals("v_guillotine.mdl", StringComparison.OrdinalIgnoreCase));
        var sections = WeaponGeometry.Sections(model); var excluded = sections.Where(s => s.SuggestedExclude).Select(s => s.Key).ToArray();
        Assert(excluded.Length > 0 && sections.Where(s => s.SuggestedExclude).All(s => model.Bodies[s.Key.Body].Name.Equals("hands", StringComparison.OrdinalIgnoreCase)));
        var full = UnityModelExporter.Build(model); var weapon = UnityModelExporter.Build(model, excludedGeometry: excluded);
        Assert(full.bones.Length == weapon.bones.Length && full.animations.Length == weapon.animations.Length);
        for (int i = 0; i < full.animations.Length; i++)
            Assert(full.animations[i].positions.SequenceEqual(weapon.animations[i].positions) && full.animations[i].rotations.SequenceEqual(weapon.animations[i].rotations));
        Assert(weapon.meshes.Where(m => model.Bodies[m.body].Name.Equals("hands", StringComparison.OrdinalIgnoreCase)).All(m => m.boneIndices.Length == 0));
        string complete = UnityModelExporter.Export(bundle, Path.Combine(destination, "complete"));
        string removed = UnityModelExporter.Export(bundle, Path.Combine(destination, "weapon-only"), excludedGeometry: excluded);
        UiChecks.VerifyWeaponExport(bundle, Path.Combine(Path.GetFullPath(destination), "selection-preview.png"));
        var filtered = WeaponGeometry.Apply(model, excluded);
        foreach (int sequence in Enumerable.Range(0, model.Sequences.Length))
        {
            int frame = Math.Min(model.Sequences[sequence].Frames - 1, model.Sequences[sequence].Frames / 2);
            using var image = ModelRenderer.Render(filtered, 800, 520, new(Sequence: sequence, Frame: frame, FocusGeometry: true));
            image.Save(Path.Combine(destination, $"sequence_{sequence:D2}.png"));
        }
        Console.WriteLine($"Guillotine: {excluded.Length} secciones de manos retiradas; {model.Bones.Length} huesos y {weapon.animations.Length} clips conservados exactamente.");
        Console.WriteLine("Modelo completo: " + complete); Console.WriteLine("Solo arma: " + removed);
    }
}
