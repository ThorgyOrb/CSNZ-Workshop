using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSNZ.UnityBridge;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

public static class CSNZModelImporter
{
    [MenuItem("Tools/CSNZ Workshop/Import model.json")]
    public static void ImportDialog()
    {
        string file = EditorUtility.OpenFilePanel("CSNZ Workshop model.json", "", "json");
        if (string.IsNullOrEmpty(file)) return;
        try { Selection.activeObject = Import(file); }
        catch (Exception error) { Debug.LogException(error); EditorUtility.DisplayDialog("CSNZ Workshop", error.Message, "OK"); }
    }

    public static GameObject Import(string filename)
    {
        var data = JsonUtility.FromJson<ModelData>(File.ReadAllText(filename));
        Validate(data);
        string source = Path.GetDirectoryName(Path.GetFullPath(filename));
        // Never use source names as asset paths, animation paths or object identifiers.
        var textureFiles = data.textures.Select(t => TexturePath(source, t.file)).ToArray();
        foreach (string file in textureFiles) if (!File.Exists(file)) throw new InvalidDataException("Missing texture: " + file);
        if (!AssetDatabase.IsValidFolder("Assets/CSNZImported")) AssetDatabase.CreateFolder("Assets", "CSNZImported");
        string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/CSNZImported/Model");
        AssetDatabase.CreateFolder("Assets/CSNZImported", Path.GetFileName(folder));
        GameObject root = null;
        try
        {
            root = new GameObject("CSNZ_Model");
            var metadata = root.AddComponent<CSNZModelInstance>();
            File.Copy(filename, folder + "/model.json"); AssetDatabase.ImportAsset(folder + "/model.json");
            metadata.metadata = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/model.json");
            string report = Path.Combine(source, "export-report.json");
            if (File.Exists(report))
            {
                File.Copy(report, folder + "/export-report.json"); AssetDatabase.ImportAsset(folder + "/export-report.json");
                metadata.exportReport = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/export-report.json");
            }
            Transform skeleton = new GameObject("Skeleton").transform; skeleton.SetParent(root.transform, false);
            var bones = new Transform[data.bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = data.bones[i]; bones[i] = new GameObject("bone_" + i.ToString("D4")).transform;
                bones[i].SetParent(b.parent < 0 ? skeleton : bones[b.parent], false);
                bones[i].localPosition = V(b.position, 0); bones[i].localRotation = Q(b.rotation, 0);
            }
            var materials = new Material[data.textures.Length];
            Shader shader = Shader.Find("CSNZ/GoldSrc Unlit");
            if (shader == null) throw new InvalidOperationException("Install the CSNZ GoldSrcUnlit.shader alongside the importer.");
            for (int i = 0; i < materials.Length; i++)
            {
                string path = folder + "/texture_" + i.ToString("D4") + ".png";
                File.Copy(textureFiles[i], path); AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default; importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = (data.textures[i].flags & 64) != 0;
                importer.wrapMode = TextureWrapMode.Repeat; importer.maxTextureSize = 4096; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                var material = new Material(shader) { name = "texture_" + i.ToString("D4"), mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path) };
                bool additive = (data.textures[i].flags & 32) != 0 || data.textures[i].name.StartsWith("$0b", StringComparison.OrdinalIgnoreCase);
                material.SetFloat("_Cutoff", (data.textures[i].flags & 64) != 0 ? .5f : 0);
                material.SetInt("_SrcBlend", (int)BlendMode.One); material.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.Zero));
                material.SetInt("_ZWrite", additive ? 0 : 1); material.renderQueue = additive ? 3000 : ((data.textures[i].flags & 64) != 0 ? 2450 : 2000);
                AssetDatabase.CreateAsset(material, folder + "/material_" + i.ToString("D4") + ".mat"); materials[i] = material;
            }
            var entries = new List<CSNZModelInstance.VariantMesh>();
            for (int i = 0; i < data.meshes.Length; i++)
            {
                var input = data.meshes[i]; int count = input.boneIndices.Length;
                var mesh = new Mesh { name = "mesh_" + i.ToString("D4"), indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.vertices = Enumerable.Range(0, count).Select(v => V(input.positions, v * 3)).ToArray();
                mesh.normals = Enumerable.Range(0, count).Select(v => V(input.normals, v * 3)).ToArray();
                mesh.uv = Enumerable.Range(0, count).Select(v => new Vector2(input.uv[v * 2], input.uv[v * 2 + 1])).ToArray();
                mesh.boneWeights = Enumerable.Range(0, count).Select(v => new BoneWeight { boneIndex0 = input.boneIndices[v], weight0 = input.weights[v] }).ToArray();
                mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
                mesh.subMeshCount = input.submeshes.Length;
                for (int sub = 0; sub < input.submeshes.Length; sub++) mesh.SetTriangles(input.submeshes[sub].indices, sub);
                mesh.RecalculateBounds(); if (count > 0) mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, folder + "/mesh_" + i.ToString("D4") + ".asset");
                var go = new GameObject(mesh.name); go.transform.SetParent(root.transform, false);
                var renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.bones = bones; renderer.rootBone = skeleton;
                renderer.sharedMaterials = input.submeshes.Select(s => materials[s.texture]).ToArray();
                // Animated bounds may extend far beyond the reference pose; don't cull on reference bounds.
                renderer.updateWhenOffscreen = true;
                entries.Add(new CSNZModelInstance.VariantMesh { body = input.body, variant = input.variant, skin = input.skin, mesh = go });
            }
            foreach (var a in data.attachments)
            {
                var attachment = new GameObject("attachment_" + a.id.ToString("D4")).transform; attachment.SetParent(bones[a.bone], false);
                attachment.localPosition = V(a.position, 0);
                // Source basis X=forward, Z=up. Preserve raw converted vectors in metadata.
                Vector3 forward = V(a.orientation, 0), up = V(a.orientation, 6);
                if (forward.sqrMagnitude > 1e-10f && Vector3.Cross(forward, up).sqrMagnitude > 1e-10f)
                    attachment.localRotation = Quaternion.LookRotation(forward, up);
                else Debug.LogWarning("Attachment " + a.id + " has no usable orientation basis; using identity.");
            }
            var clips = new List<AnimationClip>();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/animations.controller");
            foreach (var input in data.animations)
            {
                string id = "seq_" + input.sequence.ToString("D4") + "_blend_" + input.blend.ToString("D2");
                var clip = new AnimationClip { name = id, frameRate = input.fps, legacy = false };
                for (int bone = 0; bone < bones.Length; bone++)
                {
                    string path = AnimationUtility.CalculateTransformPath(bones[bone], root.transform);
                    for (int axis = 0; axis < 7; axis++)
                    {
                        int width = axis < 3 ? 3 : 4, component = axis < 3 ? axis : axis - 3;
                        float[] samples = axis < 3 ? input.positions : input.rotations;
                        var keys = new Keyframe[input.frames];
                        for (int frame = 0; frame < input.frames; frame++) keys[frame] = new Keyframe(frame / input.fps, samples[(frame * bones.Length + bone) * width + component]);
                        var curve = new AnimationCurve(keys);
                        for (int k = 0; k < keys.Length; k++) { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear); }
                        string property = (axis < 3 ? "m_LocalPosition." : "m_LocalRotation.") + "xyzw"[component];
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
                    }
                }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = input.loop; AnimationUtility.SetAnimationClipSettings(clip, settings);
                AssetDatabase.CreateAsset(clip, folder + "/" + id + ".anim");
                var state = controller.layers[0].stateMachine.AddState(id); state.motion = clip; clips.Add(clip);
            }
            var animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            if (bones.Length > 0)
            {
                var avatar = AvatarBuilder.BuildGenericAvatar(root, "Skeleton");
                if (!avatar.isValid) throw new InvalidDataException("Unity could not build a Generic avatar.");
                AssetDatabase.CreateAsset(avatar, folder + "/avatar.asset"); animator.avatar = avatar;
            }
            metadata.skin = 0; metadata.variants = new int[data.bodies.Length]; metadata.meshes = entries.ToArray(); metadata.clips = clips.ToArray(); metadata.RefreshSelection();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/model.prefab");
            if (prefab == null) throw new IOException("Could not save prefab.");
            AssetDatabase.SaveAssets(); return prefab;
        }
        catch { AssetDatabase.DeleteAsset(folder); throw; }
        finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
    }

    static Vector3 V(float[] a, int i) { return new Vector3(a[i], a[i + 1], a[i + 2]); }
    static Quaternion Q(float[] a, int i) { return new Quaternion(a[i], a[i + 1], a[i + 2], a[i + 3]); }
    static string TexturePath(string source, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid texture path.");
        string path = Path.GetFullPath(Path.Combine(source, relative));
        if (!path.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Texture path escapes package.");
        for (var info = new FileInfo(path) as FileSystemInfo; info != null; info = info is FileInfo f ? f.Directory : ((DirectoryInfo)info).Parent)
        {
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Texture path contains a link.");
            if (info.FullName.Equals(source, StringComparison.OrdinalIgnoreCase)) break;
        }
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Texture must be PNG.");
        return path;
    }
    static void Require(bool condition) { if (!condition) throw new InvalidDataException("Invalid CSNZ model package."); }
    static void Floats(float[] values, long size)
    {
        Require(values != null && values.LongLength == size && values.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));
    }
    static void Validate(ModelData d)
    {
        Require(d != null && d.version == 1 && d.units == "meters");
        Require(d.metersPerUnit > 0 && !float.IsInfinity(d.metersPerUnit));
        Require(d.bones != null && d.textures != null && d.meshes != null && d.animations != null && d.attachments != null && d.bodies != null && d.skins != null && d.events != null);
        Require(d.bones.Length <= 512 && d.meshes.Length <= 100000);
        for (int i = 0; i < d.bones.Length; i++)
        {
            var b = d.bones[i]; Require(b != null && b.id == i && b.parent >= -1 && b.parent < i); Floats(b.position, 3); Floats(b.rotation, 4);
            Require(Mathf.Abs(Quaternion.Dot(Q(b.rotation, 0), Q(b.rotation, 0)) - 1) < .001f);
        }
        for (int i = 0; i < d.textures.Length; i++) Require(d.textures[i] != null && d.textures[i].id == i && d.textures[i].name != null);
        for (int i = 0; i < d.bodies.Length; i++) Require(d.bodies[i] != null && d.bodies[i].id == i && d.bodies[i].variants != null);
        foreach (var m in d.meshes)
        {
            Require(m != null && m.boneIndices != null && m.normalBoneIndices != null && m.submeshes != null); int n = m.boneIndices.Length; Require(n <= 3000000 && m.normalBoneIndices.Length == n);
            Floats(m.positions, n * 3L); Floats(m.normals, n * 3L); Floats(m.uv, n * 2L); Floats(m.weights, n);
            Require(m.boneIndices.All(b => b >= 0 && b < d.bones.Length) && m.weights.All(w => w == 1));
            Require(m.normalBoneIndices.All(b => b >= 0 && b < d.bones.Length));
            Require(m.body >= 0 && m.body < d.bodies.Length && m.variant >= 0 && m.variant < d.bodies[m.body].variants.Length && m.skin >= 0 && m.skin < Math.Max(1, d.skins.Length));
            foreach (var s in m.submeshes) Require(s != null && s.texture >= 0 && s.texture < d.textures.Length && s.indices != null && s.indices.Length % 3 == 0 && s.indices.All(v => v >= 0 && v < n));
        }
        long total = 0;
        foreach (var c in d.animations)
        {
            Require(c != null && c.frames > 0 && c.frames <= 100000 && c.fps > 0 && c.fps <= 1000 && !float.IsNaN(c.fps));
            long samples = (long)c.frames * d.bones.Length; total += samples; Require(total <= 2000000);
            Floats(c.positions, samples * 3); Floats(c.rotations, samples * 4);
        }
        foreach (var a in d.attachments) { Require(a != null && a.bone >= 0 && a.bone < d.bones.Length); Floats(a.position, 3); Floats(a.orientation, 9); }
    }
}
