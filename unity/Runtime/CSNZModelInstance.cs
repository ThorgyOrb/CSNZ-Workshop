using System;
using UnityEngine;

namespace CSNZ.UnityBridge
{
    public sealed class CSNZModelInstance : MonoBehaviour
    {
        [Serializable] public class VariantMesh
        {
            public int body, variant, skin;
            public GameObject mesh;
        }
        public TextAsset metadata;
        public TextAsset exportReport;
        public int skin;
        public int[] variants;
        public VariantMesh[] meshes;
        public AnimationClip[] clips;

        public void SelectSkin(int value)
        {
            if (value < 0 || Array.FindIndex(meshes, m => m.skin == value) < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            skin = value; RefreshSelection();
        }
        public void SelectVariant(int body, int value)
        {
            if (body < 0 || body >= variants.Length || value < 0 || Array.FindIndex(meshes, m => m.body == body && m.variant == value) < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            variants[body] = value; RefreshSelection();
        }
        public void RefreshSelection()
        {
            if (meshes == null || variants == null) return;
            foreach (var item in meshes)
                if (item.mesh != null) item.mesh.SetActive(item.skin == skin && item.variant == variants[item.body]);
        }
        void OnValidate() { RefreshSelection(); }
    }
}
