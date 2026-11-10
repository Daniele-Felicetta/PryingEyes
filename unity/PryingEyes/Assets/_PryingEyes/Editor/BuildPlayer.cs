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

        /// <summary>Scena Torre (piani procedurali) + build Windows: niente bake, e' veloce.</summary>
        public static void Torre()
        {
            TorreBuilder.Build();
            Windows();
        }

        public static void Verify()
        {
            HotelBuilder.Build();
            ShotScene.Shots();
        }

        /// <summary>Tutto in una sessione: scena + bake, screenshot, suoni in WAV, build Windows.</summary>
        public static void Everything()
        {
            HotelBuilder.Build();
            ShotScene.Shots();
            AudioExport.Run();
            Windows();
        }

        public static void Web()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_PryingEyes/Scenes/Torre.unity" },
                locationPathName = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Builds/Web")),
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            Debug.Log("[PryingEyes] Web build: " + report.summary.result + " errors=" + report.summary.totalErrors);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(2);
        }

        public static void Windows()
        {
            // la Torre (piani procedurali) e' la scena principale; la hall classica resta nel progetto ma fuori dalla build
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/_PryingEyes/Scenes/Torre.unity", true)
            };
            var outPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "../Builds/PryingEyes.exe"));
            Debug.Log("[PryingEyes] Building to: " + outPath);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_PryingEyes/Scenes/Torre.unity" },
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[PryingEyes] report outputPath=" + report.summary.outputPath);
            Debug.Log("[PryingEyes] Build result: " + report.summary.result +
                      " errors=" + report.summary.totalErrors +
                      " size=" + report.summary.totalSize);
            Debug.Log("[PryingEyes] exe exists=" + System.IO.File.Exists(outPath) +
                      (System.IO.File.Exists(outPath) ? " mtime=" + System.IO.File.GetLastWriteTime(outPath).ToString("HH:mm:ss") : ""));
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(2);
        }
    }
}
