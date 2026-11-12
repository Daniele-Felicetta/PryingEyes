using System;
using System.IO;

namespace PryingEyes.Multiplayer.FishNetBackend.EditorTools
{
    // Pure file checks, also exercised by the standalone .NET checks. Never calls Git or writes Git metadata.
    internal static class SampleWorkspaceGuard
    {
        public static void AssertCanCreate(string projectRoot, string assetsRoot)
        {
            projectRoot = Path.GetFullPath(projectRoot);
            assetsRoot = Path.GetFullPath(assetsRoot);
            if (!SamePath(assetsRoot, Path.Combine(projectRoot, "Assets")) || !Directory.Exists(assetsRoot))
                throw new InvalidOperationException("Expected this project's existing Assets directory.");
            var directory = new DirectoryInfo(assetsRoot);
            while (directory != null)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Shared/junction asset paths are not supported by this sample generator.");
                string marker = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(marker))
                    throw new InvalidOperationException("Refusing to generate in the main Git checkout. Use feature/multiplayer worktree.");
                if (File.Exists(marker))
                {
                    string pointer = File.ReadAllText(marker).Trim();
                    if (!pointer.StartsWith("gitdir:", StringComparison.Ordinal))
                        throw new InvalidOperationException("Invalid linked worktree marker.");
                    string gitDirectory = Path.GetFullPath(Path.Combine(directory.FullName, pointer.Substring(7).Trim()));
                    string head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
                    string backlink = File.ReadAllText(Path.Combine(gitDirectory, "gitdir")).Trim();
                    if (head != "ref: refs/heads/feature/multiplayer" ||
                        !File.Exists(Path.Combine(gitDirectory, "commondir")) || !SamePath(backlink, marker))
                        throw new InvalidOperationException("Generation requires the linked feature/multiplayer worktree.");
                    return;
                }
                directory = directory.Parent;
            }
            throw new InvalidOperationException("No linked feature/multiplayer worktree found.");
        }

        static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
