using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace PryingEyes.EditorTools
{
    public static class BuildPlayer
    {
        public static void All()
        {
            HotelBuilder.Build();
            Windows();
        }

        public static void Verify()
        {
            HotelBuilder.Build();
            ShotScene.Shots();
        }

        public static void Windows()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/_PryingEyes/Scenes/Hotel.unity", true)
            };
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_PryingEyes/Scenes/Hotel.unity" },
                locationPathName = "Builds/PryingEyes.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[PryingEyes] Build result: " + report.summary.result +
                      " errors=" + report.summary.totalErrors +
                      " size=" + report.summary.totalSize);
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(2);
        }
    }
}
