#if UNITY_EDITOR && PRYINGEYES_FISHNET
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend.EditorTools
{
    internal static class BuildMultiplayerPlayer
    {
        public static void Windows()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            SampleWorkspaceGuard.AssertCanCreate(project, Application.dataPath);
            string root = Path.GetFullPath(Path.Combine(project, "../.."));
            string output = Path.Combine(root, "tools/unity-validation/player/PryingEyesMultiplayer.exe");
            string scene = AssetDatabase.FindAssets("MultiplayerProbe t:Scene", new[] { "Assets/_PryingEyes/Multiplayer" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path).FirstOrDefault();
            if (scene == null) throw new InvalidOperationException("Create the isolated probe first.");
            if (!Application.isBatchMode) throw new InvalidOperationException("This build entry point is batch-only.");
            var opened = EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var launcher = opened.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<FishNetSessionLauncher>(true)).Single();
            string worldPath = AssetDatabase.GetAssetPath(launcher.worldPrefab);
            if (!worldPath.StartsWith("Assets/_PryingEyes/Multiplayer/", StringComparison.Ordinal))
                throw new InvalidOperationException("Only multiplayer-owned prefabs may be upgraded.");
            var worldContents = PrefabUtility.LoadPrefabContents(worldPath);
            try
            {
                if (worldContents.GetComponent<FishNetTowerObjectives>() == null)
                {
                    worldContents.AddComponent<FishNetTowerObjectives>();
                    PrefabUtility.SaveAsPrefabAsset(worldContents, worldPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(worldContents); }
            var uses = new[] { KeyCode.Tab, KeyCode.RightShift, KeyCode.RightAlt, KeyCode.LeftAlt };
            foreach (var source in launcher.inputs) source.use = uses[source.seat];
            if (!opened.GetRootGameObjects().Any(root => root.GetComponentInChildren<ProcKit>(true) != null))
            {
                CreateMultiplayerProbe.CopyProceduralKit();
            }
            if (!EditorSceneManager.SaveScene(opened)) throw new InvalidOperationException("Could not save the upgraded probe scene.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            // Explicit scene list leaves the user's Build Settings untouched.
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Multiplayer build failed: " + report.summary.result);
            Debug.Log("Multiplayer player built: " + output);
        }
    }
}
#endif
