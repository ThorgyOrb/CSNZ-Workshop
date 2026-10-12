#nullable disable
using System;

namespace CSNZ.UnityBridge
{
    // Plain fields intentionally shared with System.Text.Json and Unity JsonUtility.
    [Serializable] public class ModelData
    {
        public int version;
        public string name, axes, units;
        public float metersPerUnit;
        public BoneData[] bones;
        public TextureData[] textures;
        public BodyData[] bodies;
        public SkinData[] skins;
        public MeshData[] meshes;
        public ClipData[] animations;
        public AttachmentData[] attachments;
        public EventData[] events;
    }
    [Serializable] public class BoneData { public int id, parent; public string name; public float[] position, rotation; }
    [Serializable] public class TextureData { public int id, width, height, flags; public string name, file; }
    [Serializable] public class BodyData { public int id; public string name; public string[] variants; }
    [Serializable] public class SkinData { public int[] textures; }
    [Serializable] public class MeshData
    {
        public int body, variant, skin;
        public string name;
        public float[] positions, normals, uv;
        public int[] boneIndices, normalBoneIndices;
        public float[] weights;
        public SubmeshData[] submeshes;
    }
    [Serializable] public class SubmeshData { public int slot, texture; public int[] indices; }
    [Serializable] public class ClipData
    {
        public int sequence, blend, frames, flags;
        public string name;
        public float fps;
        public bool loop;
        // Frame-major, then bone-major: xyz and xyzw.
        public float[] positions, rotations;
    }
    [Serializable] public class AttachmentData { public int id, bone; public string name; public float[] position, orientation; }
    [Serializable] public class EventData { public int sequence, frame, code; public string options; }
}
