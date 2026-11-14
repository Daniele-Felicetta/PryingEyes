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

        /// <summary>Scena Torre (piani procedurali) + build Windows: niente bake, e' veloce.
        /// Con -scriptsOnly (e una build completa gia' presente nella stessa cartella) ricompila solo il codice: una build completa
        /// riscrive ~700 MB di dati (resources.assets.resS, sharedassets0.assets.resS) e TorreBuilder rigenera scena e materiali, che
        /// per provare la modifica a uno script e' un'enorme scrittura su disco inutile. Se le risorse sono cambiate (modelli, texture,
        /// prefab, materiali, componenti nella scena) serve la build completa, cioe' senza il flag.</summary>
        public static void Torre()
        {
            if (HasArg("-scriptsOnly") && System.IO.File.Exists(OutPath()))
            {
                Debug.Log("[PryingEyes] build solo-script (nessun dato riscritto)");
                Build(true);
                return;
            }
            TorreBuilder.Build();
            Windows();
        }

        static bool HasArg(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;

        /// <summary>Dove scrive la build: Builds/PryingEyes.exe, o la cartella data da -buildOut (per provare mentre una copia
        /// del gioco e' aperta e blocca i file di Builds/).</summary>
        static string OutPath()
        {
            var outPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "../Builds/PryingEyes.exe"));
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-buildOut") outPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(args[i + 1], "PryingEyes.exe"));
            return outPath;
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

        public static void Windows() => Build(false);

        /// <summary>Build the experimental city as a separate executable, leaving Torre's build alone.</summary>
        public static void City()
        {
            bool scriptsOnly = HasArg("-scriptsOnly");
            if (!scriptsOnly) CityBuilder.Build();
            var output = HasArg("-buildOut") ? OutPath() : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../Builds/CityPreview/PryingEyes.exe"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { CityBuilder.ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = scriptsOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None
            });
            Debug.Log("[PryingEyes] city build: " + report.summary.result + " errors=" + report.summary.totalErrors);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(2);
        }

        static void Build(bool scriptsOnly)
        {
            // la Torre (piani procedurali) e' la scena principale; la hall classica resta nel progetto ma fuori dalla build
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/_PryingEyes/Scenes/Torre.unity", true)
            };
            var outPath = OutPath();
            Debug.Log("[PryingEyes] Building to: " + outPath);
            BodyLodBuilder.WarnIfStale();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_PryingEyes/Scenes/Torre.unity" },
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                options = scriptsOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None
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
