using System;
using System.IO;
using System.Linq;
using CSNZ.UnityBridge;
using UnityEditor;
using UnityEngine;

public static class CSNZUnityBatchChecks
{
    public static void Run()
    {
        try
        {
            VerifyMixedMeshRemoval();
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--csnz-package");
            Require(index >= 0 && index + 1 < args.Length, "Missing --csnz-package model.json");
            string file = args[index + 1];
            bool weaponOnly = Array.IndexOf(args, "--csnz-weapon-only") >= 0;
            var data = CSNZModelImporter.ReadData(file, weaponOnly);
            var prefab = CSNZModelImporter.Import(file, weaponOnly);
            Require(AssetDatabase.Contains(prefab), "Prefab is not persistent");
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
            try
            {
                var info = instance.GetComponent<CSNZModelInstance>();
                Require(info != null && info.metadata != null && info.exportReport != null, "Metadata missing");
                Require(info.clips.Length == data.animations.Length && info.meshes.Length == data.meshes.Length, "Clips/meshes missing");
                if (weaponOnly)
                {
                    var original = JsonUtility.FromJson<ModelData>(File.ReadAllText(file));
                    Require(original.bones.Length == data.bones.Length && original.animations.Length == data.animations.Length, "Removing arms changed the skeleton or clips");
                    for (int c = 0; c < data.animations.Length; c++)
                        Require(original.animations[c].positions.SequenceEqual(data.animations[c].positions) && original.animations[c].rotations.SequenceEqual(data.animations[c].rotations), "Removing arms changed animation samples");
                    foreach (var mesh in data.meshes)
                        Require(!mesh.submeshes.Any(s => data.geometrySections.Any(g => g.suggestedExclude && g.body == mesh.body && g.variant == mesh.variant && g.slot == s.slot)), "Hands/arms geometry is still present");
                }
                var animator = instance.GetComponent<Animator>();
                Require(animator.avatar != null && animator.avatar.isValid && !animator.avatar.isHuman, "Generic avatar invalid");
                Require(AssetDatabase.Contains(animator.avatar) && AssetDatabase.Contains(animator.runtimeAnimatorController), "Animator assets not persistent");
                foreach (var item in info.meshes)
                {
                    var renderer = item.mesh.GetComponent<SkinnedMeshRenderer>();
                    var meshInput = data.meshes[Array.IndexOf(info.meshes, item)];
                    if (meshInput.boneIndices.Length == 0) { Require(renderer == null, "Empty variant has a renderer"); continue; }
                    Require(renderer != null && AssetDatabase.Contains(renderer.sharedMesh), "Mesh not persistent");
                    Require(renderer.bones.Length == data.bones.Length && renderer.sharedMesh.bindposes.Length == data.bones.Length, "Skeleton missing");
                    Require(renderer.sharedMaterials.All(m => AssetDatabase.Contains(m) && AssetDatabase.Contains(m.mainTexture)), "Material/texture not persistent");
                    Require(renderer.sharedMaterials.All(m => !ShaderUtil.ShaderHasError(m.shader)), "Shader compile error");
                    for (int sub = 0; sub < meshInput.submeshes.Length; sub++)
                    {
                        int textureId = meshInput.submeshes[sub].texture; var texture = data.textures[textureId];
                        var materialTexture = renderer.sharedMaterials[sub].mainTexture;
                        Require(materialTexture.name == "texture_" + textureId.ToString("D4"), "Wrong texture assigned to submesh");
                        Require(materialTexture.width == texture.width && materialTexture.height == texture.height, "Texture dimensions changed");
                        var textureImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(materialTexture));
                        Require(textureImporter.textureCompression == TextureImporterCompression.Uncompressed && textureImporter.npotScale == TextureImporterNPOTScale.None, "Texture settings lose original pixels or dimensions");
                    }
                    // Skinning at rest must preserve the exported common-space positions.
                    var baked = new Mesh(); renderer.BakeMesh(baked);
                    var input = data.meshes[Array.IndexOf(info.meshes, item)];
                    for (int v = 0; v < baked.vertexCount; v++)
                        Require(Vector3.Distance(baked.vertices[v], V(input.positions, v * 3)) < .0001f, "Bind pose changes geometry");
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                for (int skin = 0; skin < data.skins.Length; skin++)
                {
                    info.SelectSkin(skin);
                    for (int variant = 0; variant < data.bodies[0].variants.Length; variant++)
                    {
                        info.SelectVariant(0, variant);
                        Require(info.meshes.Count(m => m.mesh.activeSelf) == data.bodies.Length, "Selection overlaps or missing");
                    }
                }
                var bones = Enumerable.Range(0, data.bones.Length).Select(b => instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "bone_" + b.ToString("D4"))).ToArray();
                for (int clip = 0; clip < info.clips.Length; clip++)
                {
                    Require(!info.clips[clip].legacy && AssetDatabase.Contains(info.clips[clip]), "Clip must be persistent Generic");
                    var input = data.animations[clip];
                    foreach (int frame in new[] { 0, input.frames - 1 })
                    {
                        info.clips[clip].SampleAnimation(instance, frame / input.fps);
                        for (int bone = 0; bone < bones.Length; bone++)
                        {
                            int sample = frame * bones.Length + bone;
                            Require(Vector3.Distance(bones[bone].localPosition, V(input.positions, sample * 3)) < .0001f, "Animation position mismatch");
                            int at = sample * 4;
                            var expected = new Quaternion(input.rotations[at], input.rotations[at + 1], input.rotations[at + 2], input.rotations[at + 3]);
                            Require(Quaternion.Angle(bones[bone].localRotation, expected) < .05f, "Animation rotation mismatch");
                        }
                    }
                }
                Require(instance.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("attachment_")) == data.attachments.Length, "Attachments missing");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
            Debug.Log("CSNZ UNITY CHECKS PASSED: persistent prefab, meshes, textures, materials, Generic rig, animation samples, skins and variants.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
    }
    static Vector3 V(float[] a, int i) { return new Vector3(a[i], a[i + 1], a[i + 2]); }
    static void VerifyMixedMeshRemoval()
    {
        // Two triangles share a mesh: only the second belongs to the weapon.
        var input = new ModelData
        {
            version = 1, units = "meters", metersPerUnit = .0254f,
            bones = new[] { new BoneData { id = 0, parent = -1, name = "arm_bone_also_moves_weapon", position = new float[3], rotation = new[] { 0f, 0f, 0f, 1f } } },
            textures = new[] { new TextureData { id = 0, width = 4, height = 4, name = "hand.bmp" }, new TextureData { id = 1, width = 4, height = 4, name = "weapon.bmp" } },
            bodies = new[] { new BodyData { id = 0, name = "mixed", variants = new[] { "mixed" } } }, skins = new[] { new SkinData { textures = new[] { 0, 1 } } },
            animations = new ClipData[0], attachments = new AttachmentData[0], events = new EventData[0],
            geometrySections = new[] { new GeometrySectionData { body = 0, variant = 0, slot = 0, triangles = 1, suggestedExclude = true }, new GeometrySectionData { body = 0, variant = 0, slot = 1, triangles = 1 } },
            meshes = new[] { new MeshData { body = 0, variant = 0, skin = 0,
                positions = new[] { 0f,0,0, 1,0,0, 0,1,0, 2,0,0, 3,0,0, 2,1,0 }, normals = Enumerable.Repeat(1f, 18).ToArray(),
                uv = new[] { 0f,0, 1,0, 0,1, 0,0, 1,0, 0,1 }, boneIndices = new int[6], normalBoneIndices = new int[6], weights = Enumerable.Repeat(1f, 6).ToArray(),
                submeshes = new[] { new SubmeshData { slot = 0, texture = 0, indices = new[] { 0,1,2 } }, new SubmeshData { slot = 1, texture = 1, indices = new[] { 3,4,5 } } } } }
        };
        string file = Path.Combine(Application.dataPath, "../Library/CSNZ-mixed-mesh-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, JsonUtility.ToJson(input));
            var filtered = CSNZModelImporter.ReadData(file, true); var mesh = filtered.meshes[0];
            Require(mesh.boneIndices.Length == 3 && mesh.submeshes.Length == 1 && mesh.submeshes[0].texture == 1, "Mixed mesh retained arms or lost weapon material");
            Require(mesh.positions.SequenceEqual(new[] { 2f,0,0, 3,0,0, 2,1,0 }) && mesh.submeshes[0].indices.SequenceEqual(new[] { 0,1,2 }), "Mixed mesh indices no longer address the correct vertices");
            Require(filtered.bones.Length == 1 && filtered.bones[0].name == input.bones[0].name, "Shared arm/weapon bone removed");
        }
        finally { File.Delete(file); }
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
