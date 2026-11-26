using System.IO;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>
    /// Where the test hooks of the player (photos, bot and performance logs, what a flag asks for) write: the <c>hotel_build/</c> folder
    /// of the checkout the game runs in. No path of any machine is compiled into the game. The folder is found at run time by walking up
    /// from the data folder (editor: <c>unity/PryingEyes/Assets</c>; build: <c>unity/PryingEyes/Builds/Audit/PryingEyes_Data</c>) until a
    /// directory with that name exists. A copy of the game outside any checkout (a released build) falls back to
    /// <c>persistentDataPath/hotel_build</c>, which is created the first time something is written, never at start-up.
    /// What the automatic run writes by itself goes next to it, in <c>unity_logs/player/</c> (<see cref="Scratch"/>), which git ignores:
    /// <c>hotel_build/</c> holds tracked evidence, and a run must not rewrite it unasked.
    /// Main thread only, like the Unity calls it makes.
    /// </summary>
    public static class TestPaths
    {
        const string FolderName = "hotel_build";
        static string root;
        static bool ensured;

        /// <summary>The folder, with forward slashes (the test scripts and the literals this replaces use them).</summary>
        public static string Root => root ??= Resolve();

        /// <summary>Path of a file or sub-folder inside <see cref="Root"/>; makes sure the root folder exists.</summary>
        public static string Out(string name)
        {
            if (!ensured) { Directory.CreateDirectory(Root); ensured = true; }
            return Root + "/" + name;
        }

        /// <summary>
        /// Path of a file that the player writes by itself during an automated run: the report of <c>-autotest</c> when <c>-report</c> is
        /// absent, its two timed screenshots, the default log of the bot. It is in <c>unity_logs/player/</c> of the checkout, found as
        /// <see cref="Root"/> is (next to <c>hotel_build/</c>) and created when missing. What a flag asks for (<c>-camshots</c>, the
        /// photographs of a suite) is evidence, not scratch, and still goes where its test says.
        /// </summary>
        public static string Scratch(string name)
        {
            return LogFolder("player") + "/" + name;
        }

        /// <summary>
        /// <c>unity_logs/&lt;folder&gt;</c> of the checkout the game runs in (the folder that holds <c>hotel_build/</c>), with forward slashes,
        /// created when it is missing. The one place that knows where <c>unity_logs/</c> is: <see cref="Scratch"/> and
        /// <c>TestRun.OutDir</c> both ask it, so a run started from any folder writes where the pages say.
        /// </summary>
        public static string LogFolder(string folder)
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Root) ?? ".", "unity_logs", folder).Replace('\\', '/');
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string Resolve()
        {
            try
            {
                var dir = new DirectoryInfo(Application.dataPath);
                for (int up = 0; dir != null && up < 12; up++, dir = dir.Parent)
                {
                    string candidate = System.IO.Path.Combine(dir.FullName, FolderName);
                    if (Directory.Exists(candidate)) return candidate.Replace('\\', '/');
                }
            }
            catch (System.Exception) { }    // off the main thread Unity refuses to give the path: use the fallback
            string fallback;
            try { fallback = System.IO.Path.Combine(Application.persistentDataPath, FolderName); }
            catch (System.Exception) { fallback = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PryingEyes_" + FolderName); }
            return fallback.Replace('\\', '/');
        }
    }
}
