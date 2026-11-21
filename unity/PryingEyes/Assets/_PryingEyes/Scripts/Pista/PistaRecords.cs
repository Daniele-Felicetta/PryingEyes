using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The best laps of the track mode, kept as ghosts (Ghost, in PoseTape.cs): the best lap of each bike on each circuit, apart for the lap with the standing start and for a flying lap. In the
    /// memory of the game for every circuit; on disk, in a file of its own under the game's data folder, only for the named circuits (Catalogue), which are the same every time: a circuit that is one of a
    /// kind has nothing to remember. The automated runs never read or write the files of the player. A damaged or foreign file is ignored, never fatal.</summary>
    public static class PistaRecords
    {
        static readonly Dictionary<string, Ghost> best = new Dictionary<string, Ghost>();
        static readonly HashSet<string> looked = new HashSet<string>();

        static string Key(int seed, string bikeId, int kind) { return seed + "|" + bikeId + "|" + kind; }

        /// <summary>Is a lap on this circuit worth keeping beyond this game: a named circuit, and a real player (not an automated run).</summary>
        public static bool Persistent(int seed) { return Catalogue.Named(seed) && !PistaMode.Volatile; }

        static string Folder { get { return Path.Combine(Application.persistentDataPath, "pista"); } }

        static string FileFor(int seed, string bikeId, int kind) { return Path.Combine(Folder, "fantasma-" + seed + "-" + bikeId + "-" + kind + ".bin"); }

        /// <summary>The best lap known of this kind (0 standing start, 1 flying) for this bike on this circuit, or null.</summary>
        public static Ghost Best(Circuit c, string bikeId, int kind)
        {
            string key = Key(c.seed, bikeId, kind);
            Ghost g;
            if (best.TryGetValue(key, out g)) return g;
            if (Persistent(c.seed) && looked.Add(key))
            {
                try
                {
                    string path = FileFor(c.seed, bikeId, kind);
                    if (File.Exists(path) && Ghost.TryFromBytes(File.ReadAllBytes(path), out g) && g.kind == kind && g.Fits(c.seed, bikeId, c.length))
                    {
                        best[key] = g;
                        return g;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[PryingEyes] pista: non riesco a leggere un fantasma: " + e.Message); }
            }
            return null;
        }

        /// <summary>A lap is over: keep it when it is the best of its kind. True when it is a new record (the first lap of a kind is one too).</summary>
        public static bool Submit(Circuit c, string bikeId, int kind, float lapTime, LapTrace trace, PoseTape tape)
        {
            if (trace == null || tape == null || tape.Count < 10 || !trace.Complete) return false;
            var cur = Best(c, bikeId, kind);
            if (cur != null && cur.lapTime <= lapTime) return false;
            var g = new Ghost { seed = c.seed, bikeId = bikeId, kind = kind, lapTime = lapTime, circuitLength = c.length, trace = trace, tape = tape };
            best[Key(c.seed, bikeId, kind)] = g;
            if (Persistent(c.seed))
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    string path = FileFor(c.seed, bikeId, kind), tmp = path + ".tmp";
                    File.WriteAllBytes(tmp, g.ToBytes());
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch (Exception e) { Debug.LogWarning("[PryingEyes] pista: non riesco a scrivere il fantasma: " + e.Message); }
            }
            return true;
        }

        /// <summary>For the tests: forget what this game has kept.</summary>
        public static void ForgetSession() { best.Clear(); looked.Clear(); }
    }
}
