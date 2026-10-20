using System.Collections.Generic;
using Kinforge.Sim;
using UnityEngine;

namespace Kinforge.Game
{
    /// <summary>
    /// Entry point. Owns the simulation, steps it at a fixed 20 Hz, and drives the camera, input,
    /// world view and HUD. Everything is built from code, so pressing Play in any scene starts the game.
    /// </summary>
    public sealed class KinforgeGame : MonoBehaviour
    {
        public const int DefaultSeed = 1337;

        public static KinforgeGame Instance { get; private set; }

        public World World { get; private set; }
        public int Seed { get; private set; } = DefaultSeed;
        public CameraRig Rig { get; private set; }
        public WorldView View { get; private set; }
        public Hud Hud { get; private set; }
        public CommandInput Commands { get; private set; }
        public Selection Selection { get; } = new Selection();

        /// <summary>Debug-only speed multiplier (F9). The design never pauses, so there is no 0.</summary>
        public float SimSpeed { get; private set; } = 1f;

        /// <summary>0..1 between the previous and current sim tick, for smooth rendering.</summary>
        public float InterpAlpha { get; private set; }

        public readonly List<SimEvent> FrameEvents = new List<SimEvent>(256);

        private float _accumulator;
        private Transform _worldRoot;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Application.targetFrameRate = 120;
            QualitySettings.shadowDistance = 180f;
            Mats.Init();
            SetupLighting();
            Rig = new CameraRig(FindOrCreateCamera());
            Hud = new Hud(this);
            Commands = new CommandInput(this);
        }

        private void Start()
        {
            StartWorld(DefaultSeed);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void StartWorld(int seed)
        {
            if (_worldRoot != null) Destroy(_worldRoot.gameObject);
            Seed = seed;
            World = Kinforge.Sim.World.Create(seed);
            _worldRoot = new GameObject("World").transform;
            View = new WorldView(this, World, _worldRoot);
            Selection.Clear();
            Hud.OnNewWorld();
            _accumulator = 0f;
            FrameEvents.Clear();
            var first = World.Squads.Count > 0 ? World.Squads[0] : null;
            if (first != null) Selection.SelectOnly(first.Id);
            Rig.FocusOn(ToUnity.Ground(World, World.BasePos + new Vec2(0f, 14f)), true);
        }

        public void CycleSpeed()
        {
            SimSpeed = SimSpeed >= 4f ? 1f : SimSpeed * 2f;
        }

        private void Update()
        {
            if (World == null) return;
            float dt = Time.deltaTime;

            Commands.Update(dt);

            FrameEvents.Clear();
            _accumulator += Mathf.Min(dt, 0.25f) * SimSpeed;
            int steps = 0;
            while (_accumulator >= Balance.Dt && steps < 10)
            {
                World.InspectedUnitId = Selection.InspectUnitId;
                World.Step();
                FrameEvents.AddRange(World.Events);
                _accumulator -= Balance.Dt;
                steps++;
            }
            if (steps >= 10) _accumulator = 0f;
            InterpAlpha = Mathf.Clamp01(_accumulator / Balance.Dt);

            View.Sync(dt);
            Hud.Tick(dt);
        }

        private void LateUpdate()
        {
            if (World == null) return;
            Rig.Update(Time.unscaledDeltaTime, World);
        }

        private void OnGUI()
        {
            if (World == null) return;
            Hud.Draw();
        }

        // ------------------------------------------------------------------------------------

        private static Camera FindOrCreateCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Sky;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;
            cam.fieldOfView = 50f;
            return cam;
        }

        private static void SetupLighting()
        {
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional)
                {
                    sun = l;
                    break;
                }
            }
            if (sun == null)
            {
                var go = new GameObject("Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.color = Palette.Sun;
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Palette.Ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Palette.Sky;
            RenderSettings.fogStartDistance = 160f;
            RenderSettings.fogEndDistance = 420f;
        }
    }

    /// <summary>Starts the game automatically when Play is pressed in any scene.</summary>
    public static class KinforgeAutoStart
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindObjectsByType<KinforgeGame>(FindObjectsSortMode.None).Length > 0) return;
            var go = new GameObject("Kinforge");
            go.AddComponent<KinforgeGame>();
        }
    }
}
