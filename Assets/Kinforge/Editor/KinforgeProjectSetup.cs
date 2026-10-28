using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kinforge.EditorTools
{
    /// <summary>
    /// First-open setup: creates Assets/Kinforge/Scenes/Main.unity (Unity writes the file itself),
    /// adds it to Build Settings and opens it, and adds the gamepad stick and trigger axes the game
    /// reads to this project's Input Manager settings. The game builds itself from code at Play, so
    /// the scene only needs Unity's default camera and light.
    /// </summary>
    [InitializeOnLoad]
    public static class KinforgeProjectSetup
    {
        public const string SceneFolder = "Assets/Kinforge/Scenes";
        public const string ScenePath = SceneFolder + "/Main.unity";

        static KinforgeProjectSetup()
        {
            EditorApplication.delayCall += AutoSetup;
            EditorApplication.delayCall += EnsureGamepadAxes;
        }

        // Gamepad axes (Xbox layout as Windows reports it; 0-based axis index): sticks, triggers, d-pad.
        private static readonly (string name, int axis, bool invert)[] PadAxes =
        {
            ("KF Move X", 0, false),
            ("KF Move Y", 1, true),
            ("KF Look X", 3, false),
            ("KF Look Y", 4, true),
            ("KF DPad X", 5, false),
            ("KF DPad Y", 6, false),
            ("KF Trigger L", 8, false),
            ("KF Trigger R", 9, false),
        };

        /// <summary>
        /// Adds the joystick axes the game reads (KF Move/Look/DPad/Trigger) to ProjectSettings/InputManager.asset
        /// if they are missing. Only touches entries with these names.
        /// </summary>
        [MenuItem("Kinforge/Set Up Gamepad Axes")]
        public static void EnsureGamepadAxes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var axes = so.FindProperty("m_Axes");
            if (axes == null || !axes.isArray) return;
            var have = new HashSet<string>();
            for (int i = 0; i < axes.arraySize; i++) have.Add(axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue);
            int added = 0;
            foreach (var (name, axis, invert) in PadAxes)
            {
                if (have.Contains(name)) continue;
                axes.arraySize++;
                var e = axes.GetArrayElementAtIndex(axes.arraySize - 1);
                e.FindPropertyRelative("m_Name").stringValue = name;
                e.FindPropertyRelative("descriptiveName").stringValue = "";
                e.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
                e.FindPropertyRelative("negativeButton").stringValue = "";
                e.FindPropertyRelative("positiveButton").stringValue = "";
                e.FindPropertyRelative("altNegativeButton").stringValue = "";
                e.FindPropertyRelative("altPositiveButton").stringValue = "";
                e.FindPropertyRelative("gravity").floatValue = 0f;
                e.FindPropertyRelative("dead").floatValue = 0.19f;
                e.FindPropertyRelative("sensitivity").floatValue = 1f;
                e.FindPropertyRelative("snap").boolValue = false;
                e.FindPropertyRelative("invert").boolValue = invert;
                e.FindPropertyRelative("type").intValue = 2; // joystick axis
                e.FindPropertyRelative("axis").intValue = axis;
                e.FindPropertyRelative("joyNum").intValue = 0; // any joystick
                added++;
            }
            if (added == 0) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Kinforge] Added {added} gamepad axes to the Input Manager (sticks, triggers, d-pad).");
        }

        private static void AutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = SceneManager.GetActiveScene();
            bool untitledAndClean = string.IsNullOrEmpty(active.path) && !active.isDirty && SceneManager.sceneCount == 1;
            if (!File.Exists(ScenePath))
            {
                if (!untitledAndClean)
                {
                    Debug.Log("[Kinforge] Use the menu Kinforge > Create Main Scene to set up the playable scene (or just press Play in any scene).");
                    return;
                }
                CreateScene();
            }
            EnsureBuildSettings();
            active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty && SceneManager.sceneCount == 1) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Kinforge/Create Main Scene")]
        public static void CreateSceneMenu()
        {
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateScene();
            EnsureBuildSettings();
        }

        [MenuItem("Kinforge/Open Main Scene and Play")]
        public static void Play()
        {
            if (!File.Exists(ScenePath)) CreateSceneMenu();
            else if (SceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath);
            }
            EditorApplication.isPlaying = true;
        }

        private static void CreateScene()
        {
            if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets/Kinforge", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[Kinforge] Created " + ScenePath + ". Press Play to start the game.");
        }

        private static void EnsureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
            {
                if (s.path == ScenePath) return;
            }
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
