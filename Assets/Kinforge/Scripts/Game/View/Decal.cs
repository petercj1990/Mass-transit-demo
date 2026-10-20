using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>A textured, alpha-blended patch that hugs the terrain (telegraphs, fields, markers).</summary>
    public sealed class Decal
    {
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Material _mat;

        public Decal(Transform parent, Texture2D tex, Color c, string name = "Decal")
        {
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            _mat = Mats.NewTransparent(c, tex);
            _go = new GameObject(name);
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void Place(World w, Vec2 center, Vec2 forward, float halfX, float halfZ, float lift = 0.12f)
        {
            float size = Mathf.Max(halfX, halfZ);
            int res = Mathf.Clamp(Mathf.CeilToInt(size * 1.2f), 4, 32);
            MeshKit.Draped(_mesh, w, center, forward, Mathf.Max(0.01f, halfX), Mathf.Max(0.01f, halfZ), res, lift);
        }

        public void SetColor(Color c) => Mats.SetColor(_mat, c);

        public void SetActive(bool on)
        {
            if (_go.activeSelf != on) _go.SetActive(on);
        }

        public void Destroy()
        {
            Object.Destroy(_go);
            Object.Destroy(_mesh);
            Object.Destroy(_mat);
        }
    }
}
