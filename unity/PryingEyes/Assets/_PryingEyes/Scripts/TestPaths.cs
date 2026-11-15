using System.IO;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>
    /// Where the test hooks of the player (autotest report, photos, bot and performance logs) write: the <c>hotel_build/</c> folder of
    /// the checkout the game runs in. No path of any machine is compiled into the game. The folder is found at run time by walking up
    /// from the data folder (editor: <c>unity/PryingEyes/Assets</c>; build: <c>unity/PryingEyes/Builds/Audit/PryingEyes_Data</c>) until a
    /// directory with that name exists. A copy of the game outside any checkout (a released build) falls back to
    /// <c>persistentDataPath/hotel_build</c>, which is created the first time something is written, never at start-up.
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
