using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Deserto
{
    /// <summary>La navmesh del deserto: un solo NavMeshData che si riempie a tasselli quadrati da 64 m (640 voxel da 10 cm, lo stesso passo dei blocchi di
    /// sabbia) attorno al giocatore. Ogni tassello si costruisce da solo, in modo asincrono, e si rifa' quando un edificio nasce o sparisce nella sua
    /// area (Dirty); quelli lontani si svuotano. Cosi' i nemici camminano sulla sabbia e dentro le stanze con la stessa navmesh, e il costo di un
    /// fotogramma e' al piu' la raccolta dei collider di un tassello. Per i collider usa la stessa maschera dei piani (HotelNav): niente giocatore,
    /// nemici e porte; gli arredi (le radici "Arredi" delle stanze) sono ostacoli, come nei piani.</summary>
    public static class DesertNav
    {
        public const float Tile = 64f, YMin = -60f, YMax = 150f;
        /// <summary>Intorno al giocatore (m) si tengono costruiti i tasselli; oltre KeepRadius si svuotano.</summary>
        public const float WantRadius = 110f, KeepRadius = 380f;
        const float Margin = 24f;

        static NavMeshData data;
        static NavMeshDataInstance inst;
        static AsyncOperation op;
        static long opKey;
        static bool opClears;
        static int version = 1;
        static readonly Dictionary<long, int> built = new Dictionary<long, int>();      // tassello -> versione con cui e' stato costruito
        static readonly HashSet<long> dirty = new HashSet<long>();
        static readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        static readonly List<NavMeshBuildSource> none = new List<NavMeshBuildSource>();
        static readonly List<NavMeshBuildMarkup> markups = new List<NavMeshBuildMarkup>();
        static readonly List<long> far = new List<long>();

        /// <summary>Chi sa quali radici di arredi stanno in un'area (DesertDirector): sono ostacoli per la navmesh.</summary>
        public static System.Func<Bounds, List<Transform>> Blockers;

        public static int BuiltTiles => built.Count;
        public static bool Busy => op != null;
        /// <summary>-desertnavlog: ogni tassello dice quante sorgenti ha raccolto e quanti triangoli ha la navmesh quando finisce (per le prove).</summary>
        static readonly bool Verbose = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-desertnavlog") >= 0;
        static int opSources;

        static long Key(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;
        static int TileOf(float v) => Mathf.FloorToInt(v / Tile);

        public static Bounds TileBounds(int tx, int tz) =>
            new Bounds(new Vector3((tx + 0.5f) * Tile, (YMin + YMax) * 0.5f, (tz + 0.5f) * Tile), new Vector3(Tile, YMax - YMin, Tile));

        /// <summary>Il tassello sotto il punto e' costruito (e non da rifare).</summary>
        public static bool Covers(Vector3 p)
        {
            long k = Key(TileOf(p.x), TileOf(p.z));
            return built.ContainsKey(k) && !dirty.Contains(k);
        }

        /// <summary>Tutti i tasselli entro 'radius' dal punto sono costruiti.</summary>
        public static bool ReadyAround(Vector3 p, float radius)
        {
            int r = Mathf.CeilToInt(radius / Tile);
            int pt = TileOf(p.x), pz = TileOf(p.z);
            for (int i = -r; i <= r; i++)
                for (int j = -r; j <= r; j++)
                {
                    var b = TileBounds(pt + i, pz + j);
                    float dx = Mathf.Max(Mathf.Max(b.min.x - p.x, p.x - b.max.x), 0f), dz = Mathf.Max(Mathf.Max(b.min.z - p.z, p.z - b.max.z), 0f);
                    if (dx * dx + dz * dz > radius * radius) continue;
                    long k = Key(pt + i, pz + j);
                    if (!built.ContainsKey(k) || dirty.Contains(k)) return false;
                }
            return true;
        }

        /// <summary>Qualcosa e' nato o sparito in quest'area (un edificio, un livello, un arredo): i suoi tasselli si rifanno.</summary>
        public static void Dirty(Bounds area)
        {
            int x0 = TileOf(area.min.x - 2f), x1 = TileOf(area.max.x + 2f), z0 = TileOf(area.min.z - 2f), z1 = TileOf(area.max.z + 2f);
            for (int i = x0; i <= x1; i++)
                for (int j = z0; j <= z1; j++)
                {
                    long k = Key(i, j);
                    if (built.ContainsKey(k) || (op != null && k == opKey)) dirty.Add(k);      // un tassello non ancora costruito si fara' con quello che c'e' allora
                }
        }

        /// <summary>Crea il NavMeshData vuoto e lo registra (la prima volta).</summary>
        public static void Begin()
        {
            if (inst.valid) return;
            data = new NavMeshData(0);
            inst = NavMesh.AddNavMeshData(data);
        }

        public static void Clear()
        {
            if (inst.valid) NavMesh.RemoveNavMeshData(inst);
            inst = default;
            if (data != null && (op == null || op.isDone)) Object.Destroy(data);
            data = null;
            op = null;
            built.Clear(); dirty.Clear(); sources.Clear(); markups.Clear();
            Blockers = null;
            HotelNav.SetExternalReady(false);
        }

        static NavMeshBuildSettings Settings()
        {
            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = HotelNav.AgentRadius;
            s.agentHeight = 1.9f;
            s.agentClimb = HotelNav.AgentClimb;
            s.agentSlope = 45f;
            s.overrideVoxelSize = true;
            s.voxelSize = HotelNav.VoxelSize;
            s.overrideTileSize = true;
            s.tileSize = Mathf.RoundToInt(Tile / HotelNav.VoxelSize);
            // Senza questo Unity toglie dalla navmesh i tasselli fuori dai limiti dell'aggiornamento: ogni tassello nuovo cancellava tutti gli altri.
            s.preserveTilesOutsideBounds = true;
            return s;
        }

        /// <summary>Una volta a fotogramma: chiude il tassello finito e ne avvia un altro (il piu' vicino al giocatore tra quelli da fare). In cooperativa l'ospite passa anche i compagni
        /// ('others'): i nemici che li inseguono camminano sulla stessa navmesh, quindi anche attorno a loro i tasselli si costruiscono e solo quelli lontani da tutti si svuotano.</summary>
        public static void Tick(Vector3 p, List<Vector3> others = null)
        {
            if (op != null)
            {
                if (!op.isDone) return;
                op = null;
                if (!opClears) { built[opKey] = version; HotelNav.SetExternalReady(true); }
                else built.Remove(opKey);
                if (Verbose)
                {
                    var tri = NavMesh.CalculateTriangulation();
                    Debug.Log($"[PryingEyes] deserto: tassello {(int)(opKey >> 32)},{(int)(opKey & 0xffffffffL)} {(opClears ? "svuotato" : "finito")} ({opSources} sorgenti): la navmesh ha {tri.indices.Length / 3} triangoli");
                }
            }
            Begin();
            int r = Mathf.CeilToInt(WantRadius / Tile);
            long best = 0; int bx = 0, bz = 0; float bd = float.MaxValue; bool found = false;
            int anchors = 1 + (others != null ? others.Count : 0);
            for (int a = 0; a < anchors; a++)
            {
                // il giocatore di questa macchina per primo, poi i compagni: vince il tassello da fare piu' vicino a uno qualunque
                Vector3 q = a == 0 ? p : others[a - 1];
                int pt = TileOf(q.x), pz = TileOf(q.z);
                for (int i = -r; i <= r; i++)
                    for (int j = -r; j <= r; j++)
                    {
                        int tx = pt + i, tz = pz + j;
                        var b = TileBounds(tx, tz);
                        float dx = Mathf.Max(Mathf.Max(b.min.x - q.x, q.x - b.max.x), 0f), dz = Mathf.Max(Mathf.Max(b.min.z - q.z, q.z - b.max.z), 0f);
                        float d2 = dx * dx + dz * dz;
                        if (d2 > WantRadius * WantRadius) continue;
                        long k = Key(tx, tz);
                        if (built.ContainsKey(k) && !dirty.Contains(k)) continue;
                        if (d2 >= bd) continue;
                        // senza la sabbia sotto (con il collider) il tassello nascerebbe vuoto: si aspetta (la sabbia vicina arriva in pochi fotogrammi)
                        if (DesertTerrain.I != null && !DesertTerrain.I.GroundReady(new Bounds(b.center, new Vector3(Tile + 6f, 1f, Tile + 6f)))) continue;
                        bd = d2; best = k; bx = tx; bz = tz; found = true;
                    }
            }
            if (found) { StartTile(best, bx, bz, false); return; }
            // niente da fare vicino: si svuota un tassello lontano da tutti (la memoria non cresce con la strada fatta)
            far.Clear();
            foreach (var k in built.Keys)
            {
                int tx = (int)(k >> 32), tz = (int)(k & 0xffffffffL);
                var b = TileBounds(tx, tz);
                bool kept = false;
                for (int a = 0; a < anchors && !kept; a++)
                {
                    Vector3 q = a == 0 ? p : others[a - 1];
                    float dx = b.center.x - q.x, dz = b.center.z - q.z;
                    if (dx * dx + dz * dz <= KeepRadius * KeepRadius) kept = true;
                }
                if (!kept) far.Add(k);
            }
            if (far.Count > 0)
            {
                long k = far[0];
                StartTile(k, (int)(k >> 32), (int)(k & 0xffffffffL), true);
            }
        }

        static void StartTile(long key, int tx, int tz, bool clear)
        {
            var b = TileBounds(tx, tz);
            var s = Settings();
            // i limiti sono quelli ESATTI del tassello: Unity costruisce solo i tasselli contenuti per intero nei limiti (con 4 cm di meno non
            // nasceva nessun tassello) e, grazie a preserveTilesOutsideBounds, lascia stare tutti gli altri
            var update = b;
            if (clear) op = NavMeshBuilder.UpdateNavMeshDataAsync(data, s, none, update);
            else
            {
                Physics.SyncTransforms();
                var collect = new Bounds(b.center, b.size + new Vector3(Margin * 2f, 0f, Margin * 2f));
                sources.Clear(); markups.Clear();
                var blockers = Blockers != null ? Blockers(collect) : null;
                if (blockers != null)
                    foreach (var t in blockers)
                        if (t != null) markups.Add(new NavMeshBuildMarkup { root = t, area = 1, overrideArea = true, applyToChildren = true });
                int mask = ~((1 << 2) | (1 << HotelNav.EnemyLayer) | (1 << HotelNav.DoorLayer));
                NavMeshBuilder.CollectSources(collect, mask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
                opSources = sources.Count;
                if (Verbose)
                {
                    var report = s.ValidationReport(update);
                    Debug.Log($"[PryingEyes] deserto: tassello {tx},{tz} parte: {sources.Count} sorgenti, {markups.Count} ostacoli, navmesh {NavMesh.CalculateTriangulation().indices.Length / 3} triangoli, avvisi [{string.Join("; ", report)}]");
                }
                op = NavMeshBuilder.UpdateNavMeshDataAsync(data, s, sources, update);
            }
            opKey = key;
            opClears = clear;
            dirty.Remove(key);
        }
    }
}
