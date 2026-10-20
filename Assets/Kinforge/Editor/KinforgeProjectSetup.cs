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
    /// adds it to Build Settings and opens it. The game builds itself from code at Play, so the scene
    /// only needs Unity's default camera and light.
    /// </summary>
    [InitializeOnLoad]
    public static class KinforgeProjectSetup
    {
        public const string SceneFolder = "Assets/Kinforge/Scenes";
        public const string ScenePath = SceneFolder + "/Main.unity";

        static KinforgeProjectSetup()
        {
            EditorApplication.delayCall += AutoSetup;
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
