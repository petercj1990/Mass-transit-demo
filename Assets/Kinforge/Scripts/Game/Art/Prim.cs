using UnityEngine;
using UnityEngine.Rendering;

namespace Kinforge.Game
{
    /// <summary>Helpers for assembling low-poly models out of Unity primitives.</summary>
    public static class Prim
    {
        public static GameObject Make(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            return go;
        }

        public static GameObject Box(Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool shadows = true) =>
            Make(PrimitiveType.Cube, parent, pos, scale, mat, euler, shadows);

        public static GameObject Ball(Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool shadows = true) =>
            Make(PrimitiveType.Sphere, parent, pos, scale, mat, euler, shadows);

        public static GameObject Pill(Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool shadows = true) =>
            Make(PrimitiveType.Capsule, parent, pos, scale, mat, euler, shadows);

        public static GameObject Tube(Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool shadows = true) =>
            Make(PrimitiveType.Cylinder, parent, pos, scale, mat, euler, shadows);

        public static GameObject Mesh(Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool shadows = true)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            return go;
        }

        public static Transform Node(string name, Transform parent, Vector3 pos = default, Vector3 euler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }
    }
}
