#if UNITY_EDITOR && PRYINGEYES_FISHNET
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend.EditorTools
{
    internal static class RunMultiplayerSmoke
    {
        public static void Local()
        {
            SampleWorkspaceGuard.AssertCanCreate(Path.GetDirectoryName(Application.dataPath), Application.dataPath);
            string scene = AssetDatabase.FindAssets("MultiplayerProbe t:Scene", new[] { "Assets/_PryingEyes/Multiplayer" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path).FirstOrDefault();
            if (scene == null) throw new InvalidOperationException("Generate the multiplayer probe first.");
            if (!Environment.GetCommandLineArgs().Contains("-mpSmokeLocal")) throw new InvalidOperationException("Explicit -mpSmokeLocal flag required.");
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
