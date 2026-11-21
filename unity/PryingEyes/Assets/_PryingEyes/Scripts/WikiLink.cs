using System;
using System.IO;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>The wiki of the game, opened from the main menu in the browser. A built player finds the copy that
    /// EditorTools.WikiBundle makes after each build; in the editor the wiki of the repository is used.</summary>
    public static class WikiLink
    {
        /// <summary>The front page of the wiki, or null where there is none (a player built without the wiki).</summary>
        public static string IndexPath
        {
            get
            {
                string built = Path.Combine(Application.streamingAssetsPath, "wiki", "site", "index.html");
                if (File.Exists(built)) return built;
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "wiki", "site", "index.html"));
#else
                return null;
#endif
            }
        }

        public static bool Available => IndexPath is string path && File.Exists(path);

        /// <summary>Opens the front page in the default browser; does nothing when the wiki is not there.</summary>
        public static void Open()
        {
            string path = IndexPath;
            if (path == null || !File.Exists(path)) return;
            Application.OpenURL(new Uri(path).AbsoluteUri);
        }
    }
}
