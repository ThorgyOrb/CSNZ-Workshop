using System.Numerics;
using System.Text.Json;
using CSNZ.UnityBridge;
using CSNZWorkshop.Models;

static class UnityChecks
{
    static void Assert(bool value) { if (!value) throw new Exception("Unity export assertion failed"); }
    static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .00001f;
    static Vector3 V(float[] a, int i) => new(a[i], a[i + 1], a[i + 2]);
    public static string ExportFixture(string destination)
    {
        var source = StudioModel.Parse(Synthetic.Model());
        var texture = source.Textures[0];
        var model = new StudioModel
        {
            Data = source.Data, Name = source.Name, Bones = source.Bones,
            Textures = [texture, texture with { Width = 8, Height = 8, Pixels = Enumerable.Repeat(unchecked((int)0xffff8000), 64).ToArray() }],
            Bodies = [new("body", [source.Bodies[0].Variants[0], source.Bodies[0].Variants[0] with { Name = "second" }])],
            Sequences = source.Sequences, Skins = [[0], [1]], GroupNames = source.GroupNames
        };
        model.Attachments.Add(new("muzzle", 1, new(3, 0, 0), [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ]));
        model.Events.Add(new(0, 3, 5001, "metadata only"));
        var bundle = new ModelBundle { Model = model, Main = new("fixture.mdl", source.Data, 10, Path.GetFullPath("fixture.mdl")) };
        return UnityModelExporter.Export(bundle, destination);
    }
    public static void Run(string root, Action<string, Action> check)
    {
        check("Unity: normales independientes, pose local, UV y ejes", () =>
        {
            byte[] bytes = Synthetic.Model();
            Synthetic.PutFloat(bytes, Synthetic.BoneAt + 76, .4f);
            Synthetic.PutFloat(bytes, Synthetic.BoneAt + 80, -.6f);
            Synthetic.PutFloat(bytes, Synthetic.BoneAt + 84, .8f);
            bytes[Synthetic.VertInfoAt + 2] = 1; bytes[Synthetic.NormInfoAt] = 1;
            var model = StudioModel.Parse(bytes); var data = UnityModelExporter.Build(model);
            var mesh = data.meshes.Single(); var part = model.Bodies[0].Variants[0];
            Assert(part.NormalBoneIndices[0] == 1 && part.Triangles[0].A.Normal == 0);
            var globals = model.Pose(); var local = model.GetLocalPose();
            Assert(Near(local[1].Position, new(0, 0, 20)));
            foreach (int index in Enumerable.Range(0, mesh.boneIndices.Length))
            {
                // Find original vertex by UV (fixture uses a unique UV per source vertex).
                int source = (mesh.uv[index * 2] > 0 ? 1 : 0) + (mesh.uv[index * 2 + 1] < 1 ? 2 : 0);
                Assert(Near(V(mesh.positions, index * 3), UnityModelExporter.ConvertDirection(Vector3.Transform(part.Vertices[source], globals[part.BoneIndices[source]])) * .0254f));
                Assert(Near(V(mesh.normals, index * 3), Vector3.Normalize(UnityModelExporter.ConvertDirection(Vector3.TransformNormal(part.Normals[0], globals[1])))));
                Assert(mesh.weights[index] == 1 && mesh.boneIndices[index] == part.BoneIndices[source]);
            }
            var point = new Vector3(2, 3, 5);
            var converted = Vector3.Transform(UnityModelExporter.ConvertDirection(point), UnityModelExporter.ConvertRotation(local[0].Rotation));
            Assert(Near(converted, UnityModelExporter.ConvertDirection(Vector3.Transform(point, local[0].Rotation))));
            Matrix4x4[] ExportedPose(int frame)
            {
                var result = new Matrix4x4[data.bones.Length]; var clip = data.animations[0];
                for (int bone = 0; bone < result.Length; bone++)
                {
                    int sample = frame * result.Length + bone, q = sample * 4;
                    var rotation = new Quaternion(clip.rotations[q], clip.rotations[q + 1], clip.rotations[q + 2], clip.rotations[q + 3]);
                    var transform = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(V(clip.positions, sample * 3));
                    result[bone] = data.bones[bone].parent < 0 ? transform : transform * result[data.bones[bone].parent];
                }
                return result;
            }
            var bind = ExportedPose(0); var animated = ExportedPose(7); var sourceAnimated = model.Pose(0, 7);
            for (int vertex = 0; vertex < mesh.boneIndices.Length; vertex++)
            {
                int source = (mesh.uv[vertex * 2] > 0 ? 1 : 0) + (mesh.uv[vertex * 2 + 1] < 1 ? 2 : 0), bone = mesh.boneIndices[vertex];
                Assert(Matrix4x4.Invert(bind[bone], out var inverse));
                var skinned = Vector3.Transform(V(mesh.positions, vertex * 3), inverse * animated[bone]);
                Assert(Near(skinned, UnityModelExporter.ConvertDirection(Vector3.Transform(part.Vertices[source], sourceAnimated[bone])) * .0254f));
            }
            var tri = mesh.submeshes[0].indices;
            // Triangle 0 is A,C,B after handedness conversion.
            Assert(mesh.uv[tri[0] * 2] == 0 && mesh.uv[tri[1] * 2 + 1] == .25f && mesh.uv[tri[2] * 2] == .75f);
        });
        check("Unity: seams de UV/normales y dimensiones por skin", () =>
        {
            var model = StudioModel.Parse(Synthetic.Model()); var part = model.Bodies[0].Variants[0];
            part.Triangles[1] = part.Triangles[1] with { A = part.Triangles[1].A with { UV = new(2, 2) } };
            var data = UnityModelExporter.Build(model); Assert(data.meshes[0].boneIndices.Length == 5);
            // A changed normal index also creates a distinct Unity vertex.
            var expanded = part with { Normals = [Vector3.UnitZ, Vector3.UnitX], NormalBoneIndices = [0, 0] };
            expanded.Triangles[1] = expanded.Triangles[1] with { A = expanded.Triangles[1].A with { UV = part.Triangles[0].B.UV, Normal = 1 } };
            model.Bodies[0].Variants[0] = expanded;
            Assert(UnityModelExporter.Build(model).meshes[0].boneIndices.Length == 5);
            string fixture = ExportFixture(root);
            var skins = JsonSerializer.Deserialize<ModelData>(File.ReadAllText(Path.Combine(fixture, "model.json")), new JsonSerializerOptions { IncludeFields = true })!;
            Assert(skins.meshes.Length == 4 && skins.skins.Length == 2);
            Assert(skins.meshes[0].uv[4] == skins.meshes[1].uv[4] * 2);
            Assert(skins.meshes[0].submeshes[0].texture == 0 && skins.meshes[1].submeshes[0].texture == 1);
        });
        check("Unity: blends, loop, FPS cero y continuidad de cuaterniones", () =>
        {
            byte[] bytes = Synthetic.Model();
            // Allocate a separate two-bone animation table at the end of the file.
            Array.Resize(ref bytes, bytes.Length + 48); int table = bytes.Length - 48;
            Synthetic.Put(bytes, 72, bytes.Length); Synthetic.Put(bytes, Synthetic.SeqAt + 120, 2);
            Synthetic.Put(bytes, Synthetic.SeqAt + 124, table); Synthetic.Put(bytes, Synthetic.SeqAt + 36, 1);
            Array.Resize(ref bytes, bytes.Length + 4); Synthetic.Put(bytes, 72, bytes.Length);
            Synthetic.PutShort(bytes, table + 24, 24); bytes[table + 48] = 1; bytes[table + 49] = 8; Synthetic.PutShort(bytes, table + 50, 9);
            var model = StudioModel.Parse(bytes); Assert(model.GetLocalPose(0, 0, 1)[0].Position.X == 9);
            var data = UnityModelExporter.Build(model); Assert(data.animations.Length == 2 && data.animations.All(c => c.loop));
            Assert(Math.Abs(data.animations[1].positions[2] - 9 * .0254f) < .00001f);
            foreach (var clip in data.animations)
                for (int frame = 1; frame < clip.frames; frame++)
                    for (int bone = 0; bone < data.bones.Length; bone++)
                    {
                        int i = (frame * data.bones.Length + bone) * 4, j = i - data.bones.Length * 4;
                        Assert(Enumerable.Range(0, 4).Sum(k => clip.rotations[i + k] * clip.rotations[j + k]) >= 0);
                    }
            model.Sequences[0] = model.Sequences[0] with { Group = 1 };
            var external = bytes.ToArray(); Synthetic.PutShort(external, table + 50, 12); model.ExternalGroups[1] = external;
            Assert(model.GetLocalPose(0, 7, 1)[0].Position.X == 12);
            Assert(Math.Abs(UnityModelExporter.Build(model).animations[1].positions[2] - 12 * .0254f) < .00001f);
            Synthetic.PutFloat(bytes, Synthetic.SeqAt + 32, 0);
            Assert(UnityModelExporter.Build(StudioModel.Parse(bytes)).animations.Length == 0);

            byte[] rotating = Synthetic.Model(); int channel = rotating.Length; Array.Resize(ref rotating, channel + 18);
            Synthetic.Put(rotating, 72, rotating.Length); Synthetic.PutFloat(rotating, Synthetic.BoneAt + 88 + 5 * 4, .01f);
            Synthetic.PutShort(rotating, Synthetic.AnimAt + 10, channel - Synthetic.AnimAt);
            rotating[channel] = 8; rotating[channel + 1] = 8;
            for (int frame = 0; frame < 8; frame++) Synthetic.PutShort(rotating, channel + 2 + frame * 2, frame * 100);
            var rotations = UnityModelExporter.Build(StudioModel.Parse(rotating)).animations[0].rotations;
            for (int frame = 1; frame < 8; frame++)
            {
                int i = frame * 8, j = i - 8;
                Assert(Enumerable.Range(0, 4).Sum(k => rotations[i + k] * rotations[j + k]) >= 0);
            }
        });
        check("Unity: attachments y eventos conservados", () =>
        {
            byte[] bytes = Synthetic.Model(); int at = bytes.Length; Array.Resize(ref bytes, at + 88);
            Synthetic.Put(bytes, 72, bytes.Length); Synthetic.Put(bytes, 212, 1); Synthetic.Put(bytes, 216, at);
            Synthetic.Put(bytes, at + 36, 1); Synthetic.PutFloat(bytes, at + 40, 3);
            Synthetic.PutFloat(bytes, at + 52, 1); Synthetic.PutFloat(bytes, at + 68, 1); Synthetic.PutFloat(bytes, at + 84, 1);
            var model = StudioModel.Parse(bytes); model.Events.Add(new(0, 3, 5001, "manual"));
            var data = UnityModelExporter.Build(model);
            Assert(data.attachments[0].bone == 1 && Near(V(data.attachments[0].position, 0), new(0, 0, 3 * .0254f)));
            Assert(Near(V(data.attachments[0].orientation, 0), Vector3.UnitZ)); Assert(data.events[0].code == 5001);
            bytes[Synthetic.NormInfoAt] = 255;
            try { StudioModel.Parse(bytes); throw new Exception("Invalid normal bone accepted"); } catch (InvalidDataException) { }
        });
        check("Unity: paquete JSON/PNG, compañeros faltantes y cancelación", () =>
        {
            var bundle = ModelBundle.Load(Synthetic.Model(), "test.mdl", Path.Combine(root, "test.mdl"), _ => null);
            string folder = UnityModelExporter.Export(bundle, root);
            var data = JsonSerializer.Deserialize<ModelData>(File.ReadAllText(Path.Combine(folder, "model.json")), new JsonSerializerOptions { IncludeFields = true })!;
            Assert(data.version == 1 && data.bones.Length == 2 && data.animations[0].frames == 8);
            using var png = new System.Drawing.Bitmap(Path.Combine(folder, data.textures[0].file));
            Assert(png.Width == 4 && png.Height == 4 && png.GetPixel(0, 0).ToArgb() == bundle.Model.Textures[0].Pixels[0]);
            Assert(File.Exists(Path.Combine(folder, "export-report.json")));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            int before = Directory.GetDirectories(root, "unity-model-*").Length;
            try { UnityModelExporter.Export(bundle, root, token: cancellation.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
            Assert(Directory.GetDirectories(root, "unity-model-*").Length == before);
            bundle.Missing.Add("test01.mdl");
            try { UnityModelExporter.Export(bundle, root); throw new Exception("Missing dependency accepted"); } catch (InvalidDataException) { }
        });
    }
}
