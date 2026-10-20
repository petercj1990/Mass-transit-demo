using System.Collections.Generic;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>Short-lived pooled effects: bursts, expanding rings, beams, lightning and debris.</summary>
    public sealed class Vfx
    {
        private enum Kind
        {
            Burst,
            Ring,
            Line,
            Debris,
        }

        private sealed class Fx
        {
            public Kind Kind;
            public GameObject Go;
            public Material Mat;
            public LineRenderer Line;
            public float T;
            public float Life;
            public float Size;
            public Color Color;
            public Vector3 Velocity;
        }

        private readonly Transform _root;
        private readonly List<Fx> _active = new List<Fx>(128);
        private readonly Dictionary<Kind, Stack<Fx>> _pool = new Dictionary<Kind, Stack<Fx>>();
        private readonly Material _lineMat;
        private const int MaxActive = 260;

        public Vfx(Transform parent)
        {
            _root = Prim.Node("Vfx", parent);
            _lineMat = Mats.NewTransparent(Color.white);
            foreach (Kind k in System.Enum.GetValues(typeof(Kind))) _pool[k] = new Stack<Fx>();
        }

        private Fx Get(Kind kind)
        {
            if (_active.Count >= MaxActive) return null;
            var stack = _pool[kind];
            Fx fx;
            if (stack.Count > 0)
            {
                fx = stack.Pop();
                fx.Go.SetActive(true);
            }
            else
            {
                fx = new Fx { Kind = kind };
                switch (kind)
                {
                    case Kind.Burst:
                        fx.Mat = Mats.NewTransparent(Color.white);
                        fx.Go = Prim.Ball(_root, Vector3.zero, Vector3.one, fx.Mat, default, false);
                        break;
                    case Kind.Ring:
                        fx.Mat = Mats.NewTransparent(Color.white, Tex.Ring);
                        fx.Go = Prim.Mesh(MeshKit.Quad, _root, Vector3.zero, Vector3.one, fx.Mat, default, false);
                        break;
                    case Kind.Debris:
                        fx.Mat = Mats.NewLit(Color.gray);
                        fx.Go = Prim.Box(_root, Vector3.zero, Vector3.one * 0.18f, fx.Mat, default, false);
                        break;
                    case Kind.Line:
                        fx.Go = new GameObject("Line");
                        fx.Go.transform.SetParent(_root, false);
                        fx.Line = fx.Go.AddComponent<LineRenderer>();
                        fx.Line.sharedMaterial = _lineMat;
                        fx.Line.useWorldSpace = true;
                        fx.Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        fx.Line.receiveShadows = false;
                        break;
                }
            }
            fx.T = 0f;
            _active.Add(fx);
            return fx;
        }

        public void Burst(Vector3 pos, Color c, float size, float life = 0.3f)
        {
            var fx = Get(Kind.Burst);
            if (fx == null) return;
            fx.Go.transform.position = pos;
            fx.Size = size;
            fx.Life = life;
            fx.Color = c;
            fx.Go.transform.localScale = Vector3.one * size * 0.3f;
            Mats.SetColor(fx.Mat, c);
        }

        public void Ring(Vector3 pos, Color c, float radius, float life = 0.5f)
        {
            var fx = Get(Kind.Ring);
            if (fx == null) return;
            fx.Go.transform.position = pos + Vector3.up * 0.15f;
            fx.Size = radius * 2f;
            fx.Life = life;
            fx.Color = c;
            fx.Go.transform.localScale = Vector3.one * radius * 0.4f;
            Mats.SetColor(fx.Mat, c);
        }

        public void Line(Vector3 a, Vector3 b, Color c, float width, float life, bool jagged = false)
        {
            var fx = Get(Kind.Line);
            if (fx == null) return;
            fx.Life = life;
            fx.Color = c;
            fx.Size = width;
            var lr = fx.Line;
            int n = jagged ? 7 : 2;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0f : (float)i / (n - 1);
                var p = Vector3.Lerp(a, b, t);
                if (jagged && i > 0 && i < n - 1) p += Random.insideUnitSphere * 0.45f;
                lr.SetPosition(i, p);
            }
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = c;
            lr.endColor = c;
        }

        public void Debris(Vector3 pos, Color c, int count, float speed = 5f)
        {
            for (int i = 0; i < count; i++)
            {
                var fx = Get(Kind.Debris);
                if (fx == null) return;
                fx.Go.transform.position = pos + Random.insideUnitSphere * 0.4f;
                fx.Go.transform.rotation = Random.rotation;
                fx.Velocity = new Vector3(Random.Range(-1f, 1f), Random.Range(0.8f, 1.6f), Random.Range(-1f, 1f)) * speed;
                fx.Life = Random.Range(0.6f, 1.1f);
                fx.Size = Random.Range(0.12f, 0.3f);
                fx.Go.transform.localScale = Vector3.one * fx.Size;
                Mats.SetColor(fx.Mat, c);
            }
        }

        public void Update(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var fx = _active[i];
                fx.T += dt;
                float k = fx.Life > 0f ? fx.T / fx.Life : 1f;
                if (k >= 1f)
                {
                    fx.Go.SetActive(false);
                    _pool[fx.Kind].Push(fx);
                    _active.RemoveAt(i);
                    continue;
                }
                switch (fx.Kind)
                {
                    case Kind.Burst:
                        fx.Go.transform.localScale = Vector3.one * Mathf.Lerp(fx.Size * 0.3f, fx.Size, Mathf.Sqrt(k));
                        Mats.SetColor(fx.Mat, new Color(fx.Color.r, fx.Color.g, fx.Color.b, fx.Color.a * (1f - k)));
                        break;
                    case Kind.Ring:
                        fx.Go.transform.localScale = Vector3.one * Mathf.Lerp(fx.Size * 0.2f, fx.Size, Mathf.Sqrt(k));
                        Mats.SetColor(fx.Mat, new Color(fx.Color.r, fx.Color.g, fx.Color.b, fx.Color.a * (1f - k)));
                        break;
                    case Kind.Line:
                    {
                        var c = new Color(fx.Color.r, fx.Color.g, fx.Color.b, fx.Color.a * (1f - k));
                        fx.Line.startColor = c;
                        fx.Line.endColor = c;
                        break;
                    }
                    case Kind.Debris:
                        fx.Velocity += Vector3.down * 18f * dt;
                        fx.Go.transform.position += fx.Velocity * dt;
                        fx.Go.transform.localScale = Vector3.one * fx.Size * (1f - k * k);
                        break;
                }
            }
        }
    }
}
