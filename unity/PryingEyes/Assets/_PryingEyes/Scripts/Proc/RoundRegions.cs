using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    /// <summary>Una zona che si puo' sigillare intorno a un candelabro: da una a tre stanze collegate, chiuse da serrande su tutte le
    /// aperture che portano fuori. Accendere la candela la chiude e parte il round. Geometria intera, senza UnityEngine: si calcola e si
    /// prova anche fuori dal gioco (tools/planlab, comando "regions").</summary>
    public sealed class RoundRegion
    {
        public int candleRoom;
        public readonly List<int> rooms = new List<int>();
        /// <summary>Le aperture che escono dalla zona: ognuna avra' una serranda.</summary>
        public readonly List<Opening> cuts = new List<Opening>();
        public float area;          // m2
        public float cutWidth;      // m, somma delle luci da chiudere
        public float score;         // piu' basso e' meglio

        public bool Has(int roomId) => rooms.Contains(roomId);

        public override string ToString() =>
            $"stanze [{string.Join(",", rooms)}] candela in {candleRoom}, {area:F0} m2, {cuts.Count} serrande ({cutWidth:F1} m), punteggio {score:F2}";
    }

    public static class RoundRegions
    {
        public const float MinArea = 28f, MaxArea = 330f;   // m2: sotto non ci si muove, sopra non e' piu' una stanza
        public const float IdealArea = 90f;
        public const int MaxCuts = 4, MaxRooms = 3;
        public const float MaxCutWidth = 13f;               // m di serranda in tutto

        /// <summary>Un passaggio vero tra due stanze: non una finestra, non il lato di una finestra alta.</summary>
        public static bool IsLink(Opening o) => o.b >= 0 && !o.oneSided && o.sill < 0.5f;

        /// <summary>Stanze che non si sigillano mai: gli ascensori e la stanza a cui sono attaccati (l'arrivo e l'uscita restano liberi).</summary>
        public static HashSet<int> Forbidden(Plan p)
        {
            var f = new HashSet<int>();
            foreach (var r in p.rooms) if (r.IsLift) f.Add(r.id);
            foreach (var o in p.openings)
                if (o.style == OpenStyle.Ascensore) { f.Add(o.a); if (o.b >= 0) f.Add(o.b); }
            return f;
        }

        /// <summary>Per ogni candela del piano la zona migliore da sigillare (nessuna se non ce n'e' una buona).</summary>
        public static List<RoundRegion> Find(Plan p) => Find(p, null);

        /// <summary>'why' (se non nullo) conta i motivi per cui le zone candidate sono state scartate.</summary>
        public static List<RoundRegion> Find(Plan p, Dictionary<string, int> why)
        {
            var res = new List<RoundRegion>();
            var forbidden = Forbidden(p);
            var adj = new List<int>[p.rooms.Count];
            for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
            foreach (var o in p.openings)
            {
                if (!IsLink(o)) continue;
                if (!adj[o.a].Contains(o.b)) adj[o.a].Add(o.b);
                if (!adj[o.b].Contains(o.a)) adj[o.b].Add(o.a);
            }
            foreach (int cr in p.objectives)
            {
                if (forbidden.Contains(cr)) { Count(why, "candela vicino all'ascensore"); continue; }
                RoundRegion best = null;
                foreach (var set in Sets(cr, adj, forbidden, p.objectives))
                {
                    var reg = Evaluate(p, set, cr, why);
                    if (reg != null && (best == null || reg.score < best.score)) best = reg;
                }
                if (best != null) res.Add(best); else Count(why, "nessuna zona valida");
            }
            return res;
        }

        static void Count(Dictionary<string, int> why, string key)
        {
            if (why == null) return;
            why[key] = why.TryGetValue(key, out int c) ? c + 1 : 1;
        }

        /// <summary>Insiemi connessi di al massimo MaxRooms stanze che contengono la candela, senza stanze vietate ne' altre candele.</summary>
        static IEnumerable<List<int>> Sets(int c, List<int>[] adj, HashSet<int> forbidden, List<int> objectives)
        {
            var seen = new HashSet<string>();
            var cur = new List<List<int>> { new List<int> { c } };
            yield return cur[0];
            for (int size = 2; size <= MaxRooms; size++)
            {
                var next = new List<List<int>>();
                foreach (var s in cur)
                    foreach (int m in s)
                        foreach (int n in adj[m])
                        {
                            if (s.Contains(n) || forbidden.Contains(n) || (n != c && objectives.Contains(n))) continue;
                            var t = new List<int>(s) { n };
                            t.Sort();
                            if (!seen.Add(string.Join(",", t))) continue;
                            next.Add(t);
                        }
                foreach (var t in next) yield return t;
                cur = next;
            }
        }

        static RoundRegion Evaluate(Plan p, List<int> set, int candleRoom, Dictionary<string, int> why)
        {
            float area = 0f;
            foreach (int id in set) area += p.rooms[id].W * p.rooms[id].D / 100f;
            if (area < MinArea) { Count(why, "troppo piccola"); return null; }
            if (area > MaxArea) { Count(why, "troppo grande"); return null; }
            var reg = new RoundRegion { candleRoom = candleRoom, area = area };
            reg.rooms.AddRange(set);
            foreach (var o in p.openings)
            {
                if (!IsLink(o)) continue;
                if (set.Contains(o.a) == set.Contains(o.b)) continue;
                reg.cuts.Add(o);
                reg.cutWidth += o.width * 0.1f;
            }
            if (reg.cuts.Count == 0) { Count(why, "nessuna uscita (zona isolata)"); return null; }
            if (reg.cuts.Count > MaxCuts) { Count(why, "troppe aperture"); return null; }
            if (reg.cutWidth > MaxCutWidth) { Count(why, "aperture troppo larghe"); return null; }
            reg.score = (float)Math.Abs(Math.Log(area / IdealArea)) + 0.35f * reg.cuts.Count + 0.3f * (set.Count - 1) + 0.05f * reg.cutWidth;
            return reg;
        }
    }
}
