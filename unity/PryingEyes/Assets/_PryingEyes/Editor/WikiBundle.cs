using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    /// <summary>After a successful Windows build, copies the wiki (wiki/site and wiki/img, made by tools/wiki_site.py and
    /// tools/wiki_images.py) into the player's data folder, StreamingAssets/wiki, where WikiLink finds it.</summary>
    public sealed class WikiBundle : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 || report.summary.result != BuildResult.Succeeded) return;
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string site = Path.Combine(repo, "wiki", "site");
            string images = Path.Combine(repo, "wiki", "img");
            if (!Directory.Exists(site) || !Directory.Exists(images))
            {
                Debug.LogWarning("WikiBundle: wiki/site or wiki/img is missing, so the Wiki entry of the main menu stays off. Run python tools/wiki_site.py.");
                return;
            }
            string exe = report.summary.outputPath;
            string data = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data");
            string target = Path.Combine(data, "StreamingAssets", "wiki");
            if (Directory.Exists(target)) Directory.Delete(target, true);
            int files = CopyTree(site, Path.Combine(target, "site")) + CopyTree(images, Path.Combine(target, "img"));
            Debug.Log($"WikiBundle: {files} files copied to {target}");
        }

        static int CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, dir.Substring(from.Length).TrimStart('\\', '/')));
            int count = 0;
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(to, file.Substring(from.Length).TrimStart('\\', '/')), true);
                count++;
            }
            return count;
        }
    }
}
