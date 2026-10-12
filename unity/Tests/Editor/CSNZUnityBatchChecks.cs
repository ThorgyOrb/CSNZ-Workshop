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
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--csnz-package");
            Require(index >= 0 && index + 1 < args.Length, "Missing --csnz-package model.json");
            string file = args[index + 1];
            var data = JsonUtility.FromJson<ModelData>(File.ReadAllText(file));
            var prefab = CSNZModelImporter.Import(file);
            Require(AssetDatabase.Contains(prefab), "Prefab is not persistent");
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
            try
            {
                var info = instance.GetComponent<CSNZModelInstance>();
                Require(info != null && info.metadata != null && info.exportReport != null, "Metadata missing");
                Require(info.clips.Length == data.animations.Length && info.meshes.Length == data.meshes.Length, "Clips/meshes missing");
                var animator = instance.GetComponent<Animator>();
                Require(animator.avatar != null && animator.avatar.isValid && !animator.avatar.isHuman, "Generic avatar invalid");
                Require(AssetDatabase.Contains(animator.avatar) && AssetDatabase.Contains(animator.runtimeAnimatorController), "Animator assets not persistent");
                foreach (var item in info.meshes)
                {
                    var renderer = item.mesh.GetComponent<SkinnedMeshRenderer>();
                    Require(renderer != null && AssetDatabase.Contains(renderer.sharedMesh), "Mesh not persistent");
                    Require(renderer.bones.Length == data.bones.Length && renderer.sharedMesh.bindposes.Length == data.bones.Length, "Skeleton missing");
                    Require(renderer.sharedMaterials.All(m => AssetDatabase.Contains(m) && AssetDatabase.Contains(m.mainTexture)), "Material/texture not persistent");
                    Require(renderer.sharedMaterials.All(m => !m.shader.isSupported || !ShaderUtil.ShaderHasError(m.shader)), "Shader compile error");
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
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
