using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PryingEyes.Deserto
{
    /// <summary>La sabbia: blocchi quadrati da 64 m intorno al giocatore, tre livelli di dettaglio (passo 2, 4 e 8 m), calcolati su un thread e
    /// trasformati in mesh sul thread principale, due al massimo per fotogramma. Solo i blocchi vicini hanno il collider (e finiscono nella
    /// navmesh). I blocchi lontani spariscono nella nebbia; quelli che il giocatore si lascia alle spalle si buttano.
    /// Tutta la forma del terreno viene da DesertWorld.Height: lo stesso punto ha sempre la stessa quota, a qualunque livello di dettaglio.</summary>
    public sealed class DesertTerrain : MonoBehaviour
    {
        public static DesertTerrain I { get; private set; }

        /// <summary>Distanze (m) entro cui un blocco ha il passo di 2, 4 e 8 m; oltre l'ultima non c'e' sabbia (la nebbia la nasconde).</summary>
        public const float Lod0 = 150f, Lod1 = 380f, Lod2 = 740f;
        const float Hyst = 70f;
        const int MaxJobs = 6, MaxApply = 2;
        static readonly float[] Steps = { 2f, 4f, 8f };

        sealed class Chunk { public int cx, cz, lod = -1; public GameObject go; public Mesh mesh; public MeshCollider col; public MeshRenderer mr; }
        sealed class Job { public int cx, cz, lod; public Task<ChunkData> task; }

        readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
        readonly Dictionary<long, Job> jobs = new Dictionary<long, Job>();
        readonly List<Job> finished = new List<Job>();
        readonly List<Site> nearSites = new List<Site>();
        readonly List<(float d, int cx, int cz, int lod)> wanted = new List<(float, int, int, int)>();
        Material sand;
        Transform player;
        Vector3 lastPlanPos = new Vector3(1e9f, 0f, 0f);
        float planTimer;
        readonly Stopwatch watch = new Stopwatch();

        public int ChunkCount => chunks.Count;
        public int PendingJobs => jobs.Count;
        public int Applied { get; private set; }

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        /// <summary>Il collider e' un blocco di sabbia (e non il pavimento di un luogo o della cattedrale): i passi sulla sabbia suonano diversi.</summary>
        public static bool IsSand(Collider c) => I != null && c != null && c.transform.parent == I.transform;

        public static DesertTerrain Create(Transform followed)
        {
            var go = new GameObject("DesertTerrain");
            var t = go.AddComponent<DesertTerrain>();
            t.player = followed;
            return t;
        }

        void Awake()
        {
            I = this;
            var shader = Shader.Find("PryingEyes/DesertSand");
            if (shader != null) sand = new Material(shader) { name = "DesertSand" };
            else
            {
                Debug.LogWarning("[PryingEyes] deserto: lo shader della sabbia non c'e', uso il Lit");
                sand = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DesertSandFallback", color = new Color(0.72f, 0.56f, 0.36f) };
            }
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var c in chunks.Values) Free(c);
            chunks.Clear();
            if (sand != null) Destroy(sand);
        }

        static void Free(Chunk c)
        {
            if (c.go != null) Destroy(c.go);
            if (c.mesh != null) Destroy(c.mesh);
        }

        static int LodFor(float d) => d < Lod0 ? 0 : d < Lod1 ? 1 : d < Lod2 ? 2 : -1;

        /// <summary>La distanza dal punto al rettangolo del blocco (0 dentro).</summary>
        static float DistTo(Vector3 p, int cx, int cz)
        {
            float x0 = cx * DesertTerrainGen.ChunkSize, z0 = cz * DesertTerrainGen.ChunkSize, s = DesertTerrainGen.ChunkSize;
            float dx = Mathf.Max(Mathf.Max(x0 - p.x, p.x - (x0 + s)), 0f), dz = Mathf.Max(Mathf.Max(z0 - p.z, p.z - (z0 + s)), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Tutti i blocchi entro 'radius' dal punto sono pronti, con il loro collider (per non far cadere nessuno nel vuoto all'avvio).</summary>
        public bool Ready(Vector3 p, float radius)
        {
            int r = Mathf.CeilToInt(radius / DesertTerrainGen.ChunkSize) + 1;
            int pcx = Mathf.FloorToInt(p.x / DesertTerrainGen.ChunkSize), pcz = Mathf.FloorToInt(p.z / DesertTerrainGen.ChunkSize);
            for (int i = -r; i <= r; i++)
                for (int j = -r; j <= r; j++)
                {
                    if (DistTo(p, pcx + i, pcz + j) > radius) continue;
                    if (!chunks.TryGetValue(Key(pcx + i, pcz + j), out var c) || c.lod != 0 || c.col == null || c.col.sharedMesh == null) return false;
                }
            return true;
        }

        /// <summary>Il suolo sotto un'area (xz) e' tutto li', con il collider: la navmesh di un tassello non si costruisce prima, altrimenti nascerebbe
        /// vuota (senza sabbia tra le sorgenti) e nessuno la rifarebbe.</summary>
        public bool GroundReady(Bounds b)
        {
            float s = DesertTerrainGen.ChunkSize;
            int x0 = Mathf.FloorToInt(b.min.x / s), x1 = Mathf.FloorToInt((b.max.x - 0.01f) / s);
            int z0 = Mathf.FloorToInt(b.min.z / s), z1 = Mathf.FloorToInt((b.max.z - 0.01f) / s);
            for (int i = x0; i <= x1; i++)
                for (int j = z0; j <= z1; j++)
                    if (!chunks.TryGetValue(Key(i, j), out var c) || c.lod != 0 || c.col == null || c.col.sharedMesh == null) return false;
            return true;
        }

        void Update()
        {
            if (player == null) return;
            Vector3 p = player.position;
            planTimer -= Time.unscaledDeltaTime;
            if (planTimer <= 0f || (p - lastPlanPos).sqrMagnitude > 12f * 12f)
            {
                planTimer = 0.4f;
                lastPlanPos = p;
                Plan(p);
            }
            Collect();
        }

        void Plan(Vector3 p)
        {
            wanted.Clear();
            int pcx = Mathf.FloorToInt(p.x / DesertTerrainGen.ChunkSize), pcz = Mathf.FloorToInt(p.z / DesertTerrainGen.ChunkSize);
            int r = Mathf.CeilToInt((Lod2 + Hyst) / DesertTerrainGen.ChunkSize) + 1;
            for (int i = -r; i <= r; i++)
                for (int j = -r; j <= r; j++)
                {
                    int cx = pcx + i, cz = pcz + j;
                    float d = DistTo(p, cx, cz);
                    long k = Key(cx, cz);
                    chunks.TryGetValue(k, out var have);
                    int want = LodFor(d);
                    if (have != null)
                    {
                        // si tiene il livello che ha finche' non si e' allontanato di piu' dell'isteresi (niente andirivieni al confine)
                        if (want == have.lod || LodFor(d - Hyst) == have.lod || (want == -1 && d < Lod2 + Hyst)) continue;
                        if (want == -1) { Free(have); chunks.Remove(k); continue; }
                    }
                    else if (want == -1) continue;
                    if (jobs.ContainsKey(k)) continue;
                    wanted.Add((d, cx, cz, want));
                }
            wanted.Sort((a, b) => a.d.CompareTo(b.d));
            watch.Restart();
            foreach (var w in wanted)
            {
                if (jobs.Count >= MaxJobs) break;
                if (watch.ElapsedMilliseconds > 6) break;               // i luoghi nuovi si generano sul thread principale: pochi per fotogramma
                if (!EnsureSites(w.cx, w.cz)) break;
                Queue(w.cx, w.cz, w.lod);
            }
        }

        /// <summary>I luoghi delle celle attorno a un blocco devono esistere prima che un thread calcoli la sua quota (la quota dipende dai luoghi vicini).</summary>
        bool EnsureSites(int cx, int cz)
        {
            float x0 = cx * DesertTerrainGen.ChunkSize, z0 = cz * DesertTerrainGen.ChunkSize, s = DesertTerrainGen.ChunkSize;
            int a = Mathf.FloorToInt(x0 / DesertWorld.Cell) - 1, b = Mathf.FloorToInt((x0 + s) / DesertWorld.Cell) + 1;
            int c = Mathf.FloorToInt(z0 / DesertWorld.Cell) - 1, d = Mathf.FloorToInt((z0 + s) / DesertWorld.Cell) + 1;
            for (int i = a; i <= b; i++)
                for (int j = c; j <= d; j++)
                {
                    if (DesertWorld.Known(i, j)) continue;
                    DesertWorld.SiteOf(i, j);
                    if (watch.ElapsedMilliseconds > 6) return false;       // il resto al prossimo giro: il blocco non parte ancora
                }
            return true;
        }

        void Queue(int cx, int cz, int lod)
        {
            float x0 = cx * DesertTerrainGen.ChunkSize, z0 = cz * DesertTerrainGen.ChunkSize, s = DesertTerrainGen.ChunkSize;
            DesertWorld.SitesNear(x0 + s * 0.5f, z0 + s * 0.5f, s * 0.75f, nearSites);
            List<HoleRect> holes = null;
            foreach (var site in nearSites)
                if (site.WellRect(out var rect)) (holes ??= new List<HoleRect>()).Add(rect);
            float step = Steps[lod];
            var job = new Job { cx = cx, cz = cz, lod = lod };
            job.task = Task.Run(() => DesertTerrainGen.Build(cx, cz, step, holes));
            jobs[Key(cx, cz)] = job;
        }

        void Collect()
        {
            finished.Clear();
            foreach (var j in jobs.Values) if (j.task.IsCompleted) finished.Add(j);
            if (finished.Count == 0) return;
            finished.Sort((a, b) => a.lod.CompareTo(b.lod));            // prima i vicini
            int made = 0;
            foreach (var j in finished)
            {
                if (made >= MaxApply && j.lod != 0) break;
                if (made >= MaxApply + 2) break;
                jobs.Remove(Key(j.cx, j.cz));
                if (j.task.IsFaulted) { Debug.LogError("[PryingEyes] deserto: blocco " + j.cx + "," + j.cz + ": " + j.task.Exception?.GetBaseException().Message); continue; }
                Apply(j);
                made++;
            }
        }

        void Apply(Job j)
        {
            var data = j.task.Result;
            long k = Key(j.cx, j.cz);
            if (!chunks.TryGetValue(k, out var c))
            {
                c = new Chunk { cx = j.cx, cz = j.cz };
                c.go = new GameObject($"Sabbia_{j.cx}_{j.cz}");
                c.go.transform.SetParent(transform, false);
                c.go.transform.position = new Vector3(j.cx * DesertTerrainGen.ChunkSize, 0f, j.cz * DesertTerrainGen.ChunkSize);
                c.go.AddComponent<MeshFilter>();
                c.mr = c.go.AddComponent<MeshRenderer>();
                c.mr.sharedMaterial = sand;
                chunks[k] = c;
            }
            var mesh = new Mesh { name = "Sabbia" };
            int n = data.vertexCount;
            var v = new Vector3[n]; var nm = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(data.pos[i * 3], data.pos[i * 3 + 1], data.pos[i * 3 + 2]);
                nm[i] = new Vector3(data.nrm[i * 3], data.nrm[i * 3 + 1], data.nrm[i * 3 + 2]);
            }
            mesh.vertices = v; mesh.normals = nm; mesh.triangles = data.tris;
            mesh.bounds = new Bounds(new Vector3(DesertTerrainGen.ChunkSize * 0.5f, (data.minY + data.maxY) * 0.5f, DesertTerrainGen.ChunkSize * 0.5f),
                                     new Vector3(DesertTerrainGen.ChunkSize, data.maxY - data.minY + 1f, DesertTerrainGen.ChunkSize));
            var old = c.mesh;
            c.mesh = mesh;
            c.go.GetComponent<MeshFilter>().sharedMesh = mesh;
            c.lod = j.lod;
            c.mr.shadowCastingMode = j.lod <= 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            c.mr.receiveShadows = j.lod <= 1;
            if (j.lod == 0)
            {
                if (c.col == null)
                {
                    c.col = c.go.AddComponent<MeshCollider>();
                    c.col.cookingOptions = MeshColliderCookingOptions.None;
                }
                c.col.sharedMesh = mesh;
            }
            else if (c.col != null) { Destroy(c.col); c.col = null; }
            if (old != null) Destroy(old);
            Applied++;
        }

        /// <summary>Rifa tutto da capo (le prove cambiano seme).</summary>
        public void Clear()
        {
            foreach (var c in chunks.Values) Free(c);
            chunks.Clear();
            jobs.Clear();
            lastPlanPos = new Vector3(1e9f, 0f, 0f);
        }
    }
}
