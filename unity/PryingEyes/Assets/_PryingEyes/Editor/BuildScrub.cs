using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    /// <summary>
    /// Keeps the paths of the build machine out of a distribution build. The C# compiler writes the path of the .pdb it produced into the
    /// debug directory of every assembly (<c>C:\...\Library\Bee\artifacts\...\X.pdb</c>); the game ships without .pdb files, so that entry is
    /// overwritten in place with the bare file name (the field keeps its size, nothing else moves). Then every managed assembly of the
    /// build is searched for the root of this checkout, and any hit is reported: a literal path in game code shows up here.
    /// </summary>
    public static class BuildScrub
    {
        /// <summary>Scrubs and checks the managed assemblies of the build whose executable is <paramref name="exePath"/>.</summary>
        public static void Run(string exePath)
        {
            string managed = Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath) + "_Data", "Managed");
            if (!Directory.Exists(managed)) { Debug.LogWarning("[PryingEyes] distribution: no Managed folder at " + managed); return; }
            string project = Norm(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))) + "/";
            string checkout = Norm(Path.GetFullPath(Path.Combine(Application.dataPath, "../..")));
            int scrubbed = 0, leaks = 0;
            foreach (string dll in Directory.GetFiles(managed, "*.dll"))
            {
                if (ScrubDebugPath(dll, project)) scrubbed++;
                if (Contains(File.ReadAllBytes(dll), checkout))
                {
                    leaks++;
                    Debug.LogWarning("[PryingEyes] distribution: the path of this checkout is still inside " + Path.GetFileName(dll));
                }
            }
            Debug.Log("[PryingEyes] distribution: debug path removed from " + scrubbed + " assembly(ies), " + leaks + " still contain the checkout path");
        }

        static string Norm(string path) => path.Replace('\\', '/').TrimEnd('/');

        /// <summary>Overwrites the CodeView (.pdb) path of a PE file when it points inside the project. Returns true if the file changed.</summary>
        static bool ScrubDebugPath(string file, string projectRoot)
        {
            byte[] b = File.ReadAllBytes(file);
            if (b.Length < 0x100 || b[0] != 'M' || b[1] != 'Z') return false;
            int pe = BitConverter.ToInt32(b, 0x3C);
            if (pe <= 0 || pe + 24 > b.Length || b[pe] != 'P' || b[pe + 1] != 'E') return false;
            int sections = BitConverter.ToUInt16(b, pe + 6);
            int optionalSize = BitConverter.ToUInt16(b, pe + 20);
            int optional = pe + 24;
            int directories = optional + (BitConverter.ToUInt16(b, optional) == 0x20B ? 112 : 96);   // PE32+ : PE32
            uint debugRva = BitConverter.ToUInt32(b, directories + 6 * 8);                           // data directory 6 = debug
            uint debugLength = BitConverter.ToUInt32(b, directories + 6 * 8 + 4);
            if (debugRva == 0 || debugLength < 28) return false;
            int table = optional + optionalSize, debug = -1;
            for (int i = 0; i < sections; i++)
            {
                int s = table + i * 40;
                uint virtualSize = BitConverter.ToUInt32(b, s + 8), virtualAddress = BitConverter.ToUInt32(b, s + 12), rawPointer = BitConverter.ToUInt32(b, s + 20);
                if (debugRva >= virtualAddress && debugRva < virtualAddress + virtualSize) { debug = (int)(rawPointer + (debugRva - virtualAddress)); break; }
            }
            if (debug < 0) return false;
            bool changed = false;
            for (int e = debug; e + 28 <= debug + debugLength && e + 28 <= b.Length; e += 28)
            {
                int type = BitConverter.ToInt32(b, e + 12), size = BitConverter.ToInt32(b, e + 16), at = BitConverter.ToInt32(b, e + 24);
                if (type != 2 || size <= 24 || at <= 0 || at + size > b.Length) continue;             // 2 = CodeView
                if (b[at] != 'R' || b[at + 1] != 'S' || b[at + 2] != 'D' || b[at + 3] != 'S') continue;
                int start = at + 24, length = 0;
                while (start + length < at + size && b[start + length] != 0) length++;
                string pdb = Encoding.UTF8.GetString(b, start, length);
                if (!pdb.Replace('\\', '/').StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) continue;
                byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(pdb));
                if (name.Length >= size - 24) continue;                                                // keep the terminating zero
                Array.Clear(b, start, size - 24);
                Array.Copy(name, 0, b, start, name.Length);
                changed = true;
            }
            if (changed) File.WriteAllBytes(file, b);
            return changed;
        }

        /// <summary>True if the bytes hold the text as ASCII or as UTF-16, with either slash, ignoring case.</summary>
        static bool Contains(byte[] data, string text)
        {
            foreach (string variant in new[] { text, text.Replace('/', '\\') })
                if (Has(data, variant, false) || Has(data, variant, true)) return true;
            return false;
        }

        static bool Has(byte[] data, string text, bool wide)
        {
            int step = wide ? 2 : 1, n = text.Length;
            for (int i = 0; i + n * step <= data.Length; i++)
            {
                int k = 0;
                for (; k < n; k++)
                {
                    int at = i + k * step;
                    if (wide && data[at + 1] != 0) break;
                    int c = data[at], t = text[k];
                    if (c >= 'A' && c <= 'Z') c += 32;
                    if (t >= 'A' && t <= 'Z') t += 32;
                    if (c != t) break;
                }
                if (k == n) return true;
            }
            return false;
        }
    }
}
