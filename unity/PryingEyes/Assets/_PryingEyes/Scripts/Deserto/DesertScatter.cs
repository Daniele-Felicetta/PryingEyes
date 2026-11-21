using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PryingEyes.Deserto
{
    /// <summary>Le cose sulla sabbia (sassi, cactus, cespugli, alberi morti, ossa, colonne...) attorno al giocatore. Ogni quadrato da 64 m ha il suo insieme (DesertScatterGen), calcolato su un thread;
    /// qui si tengono le matrici per genere e si disegnano a migliaia con l'istanziamento (nessun oggetto di scena per copia). Le cose piccole si vedono da vicino, le grandi da lontano
    /// (DesertScatterGen.TierRadius). Quelle su cui si inciampa (massi, lastre, cactus, alberi, colonne, stele) hanno un collider di capsula solo entro 140 m dal giocatore: la navmesh
    /// li vede quando costruisce i tasselli (entro 110 m) e chi cammina non ci passa attraverso.</summary>
    public sealed class DesertScatter : MonoBehaviour
    {
        public static DesertScatter I { get; private set; }

        const float ColliderIn = 140f, ColliderOut = 190f;
        const int MaxJobs = 6;
        /// <summary>Quanti sono i generi: i sassi e i massi lontani (PropMeshes.HasLow) stanno in una seconda serie di liste, dopo i generi, con la mesh ridotta.</summary>
        const int Kinds = (int)Prop.Count;
        /// <summary>Entro questa distanza (m) un sasso o un masso ha tutti i suoi triangoli; piu' in la' ha la versione ridotta, con la stessa sagoma (da 70 m un sasso di un metro e' una quindicina di punti sullo schermo a 1080p).</summary>
        const float LodNear = 70f;
        /// <summary>Sotto la sabbia piu' di tanto (m) non si disegna nessuna delle cose che ci stanno sopra: da una catacomba non si vedono e le loro ombre cadono sul tetto.</summary>
        const float UndergroundDepth = 2.5f;

        sealed class Cell
        {
            public int cx, cz; public long key;
            public Task<List<PropItem>> task;
            public List<PropItem> items;
            public Matrix4x4[][] mats;          // per genere
        }

        readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
        readonly List<Cell> pending = new List<Cell>();
        readonly Dictionary<(long, int), CapsuleCollider> live = new Dictionary<(long, int), CapsuleCollider>();
        readonly List<CapsuleCollider> free = new List<CapsuleCollider>();
        readonly List<(long, int)> drop = new List<(long, int)>();
        readonly List<Matrix4x4>[] gather = new List<Matrix4x4>[2 * Kinds];
        readonly Matrix4x4[][] agg = new Matrix4x4[2 * Kinds][];
        readonly int[] aggCount = new int[2 * Kinds];
        readonly bool[] casts = new bool[Kinds];
        Transform player, colRoot;
        Material mat;
        Vector3 lastPlan = new Vector3(1e9f, 0f, 0f), lastAgg = new Vector3(1e9f, 0f, 0f);
        float planT, colT;
        bool dirty;
        // i fuochi (le luci e il fumo li fa DesertFires) e i mulini (il rotore gira qui)
        readonly List<Vector3> fires = new List<Vector3>();
        readonly List<Matrix4x4> windmills = new List<Matrix4x4>();
        readonly Matrix4x4[] rotorMats = new Matrix4x4[16];
        float rotorAngle;
        public const float FireRange = 130f;

        /// <summary>I fuochi dei campi entro 130 m dal giocatore (alla base delle fiamme).</summary>
        public IReadOnlyList<Vector3> Fires => fires;
        public int Windmills => windmills.Count;

        public int CellsReady { get { int n = 0; foreach (var c in cells.Values) if (c.items != null) n++; return n; } }
        public int CellsPending => pending.Count;
        /// <summary>Per le prove: la distanza (m) del quadrato di cose piu' vicino che manca ancora (in attesa, partito o no, e senza le sue cose), 9999 se non ne manca nessuno.</summary>
        public float NearestMissingCell(Vector3 p)
        {
            float best = 9999f;
            foreach (var c in pending) if (c.items == null) best = Mathf.Min(best, DistTo(p, c.cx, c.cz));
            return best;
        }
        /// <summary>Per le prove: i primi quadrati in attesa che non sono ancora partiti, con la distanza dal punto e se i luoghi attorno ci sono (se no aspettano che il terreno li faccia: -desertonly motopiano).</summary>
        public string PendingReport(Vector3 p)
        {
            var sb = new System.Text.StringBuilder(); int n = 0;
            float s = DesertScatterGen.Chunk;
            foreach (var c in pending)
            {
                if (c.task != null) continue;
                if (n++ >= 4) { sb.Append(" ..."); break; }
                sb.Append($" [{c.cx},{c.cz} a {DistTo(p, c.cx, c.cz):F0} m, luoghi attorno {(DesertWorld.KnownAround(c.cx * s, c.cz * s, (c.cx + 1) * s, (c.cz + 1) * s) ? "ci sono" : "NON ci sono")}]");
            }
            return sb.ToString();
        }
        public int Instances { get { int n = 0; foreach (var c in aggCount) n += c; return n; } }
        public int Colliders => live.Count;
        public int CountOf(Prop p) => aggCount[(int)p] + aggCount[Kinds + (int)p];
        /// <summary>Delle cose pesanti (sassi, massi, cactus e cespugli, PropMeshes.HasLow): quante hanno tutti i triangoli (entro 70 m) e quante la versione ridotta (oltre).</summary>
        public int FullDetail { get { int n = 0; for (int k = 0; k < Kinds; k++) if (PropMeshes.HasLow((Prop)k)) n += aggCount[k]; return n; } }
        public int ReducedDetail { get { int n = 0; for (int k = 0; k < Kinds; k++) if (PropMeshes.HasLow((Prop)k)) n += aggCount[Kinds + k]; return n; } }
        /// <summary>Vero se l'ultimo fotogramma non ha mandato alla scheda niente perche' il giocatore e' sotto la sabbia (una catacomba): da li' le cose sopra non si vedono.</summary>
        public bool Underground { get; private set; }

        public static DesertScatter Create(GameObject host, Transform followed)
        {
            var s = host.AddComponent<DesertScatter>();
            s.player = followed;
            return s;
        }

        void Awake()
        {
            I = this;
            mat = PropMaterial(false);
            for (int i = 0; i < gather.Length; i++) gather[i] = new List<Matrix4x4>();
            for (int i = 0; i < casts.Length; i++)
            {
                var p = (Prop)i;
                casts[i] = !(p == Prop.Tuft || p == Prop.Pebbles || p == Prop.Bones || p == Prop.Cairn || p == Prop.Barrel || p == Prop.FirePit || p == Prop.Crate);
            }
            colRoot = new GameObject("DesertPropColliders").transform;
        }

        /// <summary>La materia delle cose sulla sabbia: una copia di Resources/Deserto/DesertProp (o DesertPropLife per le cose che si muovono). I due materiali stanno nelle Resources perche'
        /// la build tenga la variante istanziata dello shader (con 'Strip Unused' e un materiale fatto solo a runtime il disegno istanziato non produce niente).</summary>
        public static Material PropMaterial(bool life)
        {
            var asset = Resources.Load<Material>(life ? "Deserto/DesertPropLife" : "Deserto/DesertProp");
            if (asset != null) return new Material(asset) { name = asset.name, enableInstancing = true };
            var shader = Shader.Find("PryingEyes/DesertProp");
            Material m;
            if (shader != null) m = new Material(shader) { name = "DesertProp", enableInstancing = true };
            else { Debug.LogWarning("[PryingEyes] deserto: lo shader delle cose sulla sabbia non c'e', uso il Lit"); m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DesertPropFallback", enableInstancing = true }; }
            if (life) m.SetFloat("_Fixed", 1f);
            return m;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (colRoot != null) Destroy(colRoot.gameObject);
            live.Clear(); free.Clear();
            if (mat != null) Destroy(mat);
            PropMeshes.Clear();
        }

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        void Update()
        {
            if (player == null) return;
            Vector3 p = player.position;
            var others = DesertAnchors.OtherPositions;
            var views = DesertAnchors.ViewerPositions;          // schermo condiviso: gli altri posti, che si guardano da questa macchina
            // chi guarda si e' mosso abbastanza da rifare le liste? (il primo punto e quelli degli altri posti)
            float moved = (p - lastPlan).sqrMagnitude;
            for (int i = 0; i < views.Count; i++)
                moved = Mathf.Max(moved, i < lastViews.Count ? (views[i] - lastViews[i]).sqrMagnitude : 1e9f);
            planT -= Time.unscaledDeltaTime;
            if (planT <= 0f || moved > 16f * 16f) { planT = 0.4f; lastPlan = p; lastViews.Clear(); lastViews.AddRange(views); Plan(p, others, views); }
            Collect();
            StartJobs();
            float movedAgg = (p - lastAgg).sqrMagnitude;
            for (int i = 0; i < views.Count; i++)
                movedAgg = Mathf.Max(movedAgg, i < lastAggViews.Count ? (views[i] - lastAggViews[i]).sqrMagnitude : 1e9f);
            if (dirty || movedAgg > 20f * 20f) { Aggregate(p, views); dirty = false; lastAgg = p; lastAggViews.Clear(); lastAggViews.AddRange(views); }
            colT -= Time.unscaledDeltaTime;
            if (colT <= 0f) { colT = 0.35f; Colliders_(p, others); }
            Draw(p, views);
        }

        readonly List<Vector3> lastViews = new List<Vector3>(), lastAggViews = new List<Vector3>();

        /// <summary>La distanza dalla cella al piu' vicino tra il giocatore e gli altri posti che guardano (nello schermo condiviso).</summary>
        static float DistToViews(Vector3 p, List<Vector3> views, int cx, int cz)
        {
            float best = DistTo(p, cx, cz);
            for (int i = 0; i < views.Count; i++) best = Mathf.Min(best, DistTo(views[i], cx, cz));
            return best;
        }

        /// <summary>La distanza al quadrato dal punto al piu' vicino tra il giocatore e gli altri posti.</summary>
        static float NearestViewSqr(Vector3 p, List<Vector3> views, float x, float z)
        {
            float dx = x - p.x, dz = z - p.z, best = dx * dx + dz * dz;
            for (int i = 0; i < views.Count; i++)
            {
                dx = x - views[i].x; dz = z - views[i].z;
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            return best;
        }

        static float DistTo(Vector3 p, int cx, int cz)
        {
            float s = DesertScatterGen.Chunk, x0 = cx * s, z0 = cz * s;
            float dx = Mathf.Max(Mathf.Max(x0 - p.x, p.x - (x0 + s)), 0f), dz = Mathf.Max(Mathf.Max(z0 - p.z, p.z - (z0 + s)), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>La distanza dal compagno piu' vicino al quadrato (molto grande se non ce ne sono).</summary>
        static float DistToOthers(List<Vector3> others, int cx, int cz)
        {
            float best = 1e9f;
            for (int i = 0; i < others.Count; i++) best = Mathf.Min(best, DistTo(others[i], cx, cz));
            return best;
        }

        // ------------------------------------------------------------------ quali quadrati
        /// <summary>I quadrati attorno a chi gioca fino al raggio piu' largo; in cooperativa l'ospite ne tiene anche attorno ai compagni quanto basta ai collider (i sassi si incontrano
        /// anche li': chi insegue un compagno non deve attraversarli, e la navmesh li vede). Non si disegnano: il disegno e' solo attorno a chi guarda.</summary>
        void Plan(Vector3 p, List<Vector3> others, List<Vector3> views)
        {
            float far = DesertScatterGen.TierRadius[2];
            int r = Mathf.CeilToInt(far / DesertScatterGen.Chunk) + 1;
            pending.Clear();
            for (int v = 0; v <= views.Count; v++)              // il giocatore e, nello schermo condiviso, ognuno degli altri posti
            {
                Vector3 q = v == 0 ? p : views[v - 1];
                int pcx = Mathf.FloorToInt(q.x / DesertScatterGen.Chunk), pcz = Mathf.FloorToInt(q.z / DesertScatterGen.Chunk);
                for (int i = -r; i <= r; i++)
                    for (int j = -r; j <= r; j++)
                    {
                        int cx = pcx + i, cz = pcz + j;
                        if (DistTo(q, cx, cz) > far) continue;
                        long k = Key(cx, cz);
                        if (cells.ContainsKey(k) || (v > 0 && pending.Exists(c => c.key == k))) continue;
                        pending.Add(new Cell { cx = cx, cz = cz, key = k });
                    }
            }
            pending.Sort((a, b) => DistToViews(p, views, a.cx, a.cz).CompareTo(DistToViews(p, views, b.cx, b.cz)));
            const float OtherReach = ColliderIn + 30f;
            for (int o = 0; o < others.Count; o++)
            {
                Vector3 q = others[o];
                int qcx = Mathf.FloorToInt(q.x / DesertScatterGen.Chunk), qcz = Mathf.FloorToInt(q.z / DesertScatterGen.Chunk);
                int ro = Mathf.CeilToInt(OtherReach / DesertScatterGen.Chunk) + 1;
                for (int i = -ro; i <= ro; i++)
                    for (int j = -ro; j <= ro; j++)
                    {
                        int cx = qcx + i, cz = qcz + j;
                        if (DistTo(q, cx, cz) > OtherReach) continue;
                        long k = Key(cx, cz);
                        if (cells.ContainsKey(k) || pending.Exists(c => c.key == k)) continue;
                        pending.Add(new Cell { cx = cx, cz = cz, key = k });
                    }
            }
            // quelli che sono rimasti troppo indietro si buttano
            var gone = new List<long>();
            foreach (var kv in cells)
                if (DistToViews(p, views, kv.Value.cx, kv.Value.cz) > far + 90f && (others.Count == 0 || DistToOthers(others, kv.Value.cx, kv.Value.cz) > OtherReach + 60f)) gone.Add(kv.Key);
            foreach (long k in gone) { cells.Remove(k); dirty = true; }
            StartJobs();
        }

        /// <summary>Avvia i lavori dei quadrati in attesa, dal piu' vicino, finche' ce ne sono MaxJobs in corso. Si chiama a ogni fotogramma (come DesertTerrain.Pump), non solo quando Plan rifa' l'elenco:
        /// a tre quadrati per giro ogni 0,4 s le cose sulla sabbia nascevano a sette al secondo, e a 83 km/h ne servono otto.</summary>
        void StartJobs()
        {
            if (pending.Count == 0) return;
            int jobs = 0;
            foreach (var c in cells.Values) if (c.task != null && c.items == null) jobs++;
            float s = DesertScatterGen.Chunk;
            foreach (var c in pending)
            {
                if (jobs >= MaxJobs) break;
                if (c.task != null) continue;                           // e' gia' partito (l'elenco resta com'e' finche' Plan non lo rifa')
                // i luoghi attorno devono esistere prima che un thread calcoli le quote (come per la sabbia)
                if (!DesertWorld.KnownAround(c.cx * s, c.cz * s, (c.cx + 1) * s, (c.cz + 1) * s)) continue;
                int cx = c.cx, cz = c.cz;
                c.task = Task.Run(() => { var l = new List<PropItem>(); DesertScatterGen.Build(cx, cz, l); return l; });
                cells[c.key] = c;
                jobs++;
            }
        }

        void Collect()
        {
            int made = 0;
            foreach (var c in cells.Values)
            {
                if (c.task == null || c.items != null || !c.task.IsCompleted) continue;
                if (c.task.IsFaulted) { Debug.LogError("[PryingEyes] deserto: cose sulla sabbia " + c.cx + "," + c.cz + ": " + c.task.Exception?.GetBaseException().Message); c.items = new List<PropItem>(); c.mats = new Matrix4x4[(int)Prop.Count][]; continue; }
                c.items = c.task.Result;
                var per = new List<Matrix4x4>[(int)Prop.Count];
                foreach (var it in c.items)
                {
                    var l = per[it.kind] ??= new List<Matrix4x4>();
                    l.Add(Matrix4x4.TRS(new Vector3(it.x, it.y, it.z), Quaternion.Euler(0f, it.yaw, 0f), Vector3.one * it.scale));
                }
                c.mats = new Matrix4x4[(int)Prop.Count][];
                for (int k = 0; k < per.Length; k++) if (per[k] != null) c.mats[k] = per[k].ToArray();
                dirty = true;
                if (++made >= 4) break;
            }
        }

        /// <summary>Le matrici di ogni genere dei quadrati che stanno entro il raggio del genere.</summary>
        void Aggregate(Vector3 p, List<Vector3> views)
        {
            foreach (var l in gather) l.Clear();
            foreach (var c in cells.Values)
            {
                if (c.mats == null) continue;
                float d = DistToViews(p, views, c.cx, c.cz);
                for (int k = 0; k < c.mats.Length; k++)
                {
                    if (c.mats[k] == null || d > DesertScatterGen.TierRadius[DesertScatterGen.Tier((Prop)k)]) continue;
                    if (!PropMeshes.HasLow((Prop)k)) { gather[k].AddRange(c.mats[k]); continue; }
                    if (d > LodNear) { gather[Kinds + k].AddRange(c.mats[k]); continue; }
                    // un quadrato che arriva dentro l'anello vicino: ogni copia sceglie dalla sua distanza
                    foreach (var m in c.mats[k])
                    {
                        gather[NearestViewSqr(p, views, m.m03, m.m23) > LodNear * LodNear ? Kinds + k : k].Add(m);
                    }
                }
            }
            for (int k = 0; k < gather.Length; k++)
            {
                aggCount[k] = gather[k].Count;
                if (agg[k] == null || agg[k].Length < aggCount[k]) agg[k] = new Matrix4x4[Mathf.Max(64, aggCount[k] * 3 / 2)];
                gather[k].CopyTo(agg[k]);
            }
            // i fuochi vicini e i mulini in vista
            fires.Clear(); windmills.Clear();
            float far = DesertScatterGen.TierRadius[2];
            foreach (var c in cells.Values)
            {
                if (c.items == null) continue;
                float d = DistToViews(p, views, c.cx, c.cz);
                if (d > far) continue;
                foreach (var it in c.items)
                {
                    if (it.kind == (byte)Prop.FirePit)
                    {
                        if (NearestViewSqr(p, views, it.x, it.z) < FireRange * FireRange) fires.Add(new Vector3(it.x, it.y, it.z));
                    }
                    else if (it.kind == (byte)Prop.Windmill && windmills.Count < rotorMats.Length)
                        windmills.Add(Matrix4x4.TRS(new Vector3(it.x, it.y, it.z), Quaternion.Euler(0f, it.yaw, 0f), Vector3.one * it.scale));
                }
            }
        }

        /// <summary>Senza scheda grafica (-nographics: la prova di tutto il gioco in cento secondi) o su una scheda che non sa l'istanziamento, RenderMeshInstanced lancia un'eccezione a ogni
        /// chiamata: le cose si contano e si piazzano lo stesso, ma non si disegnano.</summary>
        public static bool CanDraw => SystemInfo.supportsInstancing && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        void Draw(Vector3 p, List<Vector3> views)
        {
            // sotto la sabbia (una catacomba) non si manda niente alla scheda: da li' le cose sopra non si vedono, e i loro sassi pesavano 2,5 ms nella sala del boss
            // (nello schermo condiviso solo se tutti sono sotto: basta uno all'aperto perche' le cose si disegnino per la sua camera)
            Underground = p.y < DesertWorld.Height(p.x, p.z) - UndergroundDepth;
            var bounds = new Bounds(p, new Vector3(1300f, 400f, 1300f));
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (v.y >= DesertWorld.Height(v.x, v.z) - UndergroundDepth) Underground = false;
                bounds.Encapsulate(new Bounds(v, new Vector3(1300f, 400f, 1300f)));
            }
            if (!CanDraw || Underground) return;
            for (int k = 0; k < agg.Length; k++)
            {
                int n = aggCount[k];
                if (n == 0) continue;
                bool far = k >= Kinds;
                int kind = far ? k - Kinds : k;
                var mesh = far ? PropMeshes.GetLow((Prop)kind) : PropMeshes.Get((Prop)kind);
                var rp = new RenderParams(mat) { shadowCastingMode = casts[kind] ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, worldBounds = bounds, layer = 0 };
                for (int s = 0; s < n; s += 1000) Graphics.RenderMeshInstanced(rp, mesh, 0, agg[k], Mathf.Min(1000, n - s), s);
            }
            // i rotori dei mulini: girano piu' in fretta col vento
            if (windmills.Count > 0)
            {
                float wind = DesertSky.I != null ? DesertSky.I.Weather.wind : 0.3f;
                rotorAngle += Time.deltaTime * (14f + 80f * wind);
                var hub = Matrix4x4.Translate(PropMeshes.WindmillHub);
                for (int i = 0; i < windmills.Count; i++) rotorMats[i] = windmills[i] * hub * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, rotorAngle + i * 37f));
                var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = bounds, layer = 0 };
                Graphics.RenderMeshInstanced(rp, PropMeshes.Rotor(), 0, rotorMats, windmills.Count);
            }
        }

        // ------------------------------------------------------------------ i collider
        /// <summary>Il punto 'at' (xz) e' entro 'radius' dal giocatore o da un compagno?</summary>
        static bool NearAny(Vector3 p, List<Vector3> others, float x, float z, float radius)
        {
            float r2 = radius * radius, dx = x - p.x, dz = z - p.z;
            if (dx * dx + dz * dz <= r2) return true;
            for (int i = 0; i < others.Count; i++)
            {
                dx = x - others[i].x; dz = z - others[i].z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        void Colliders_(Vector3 p, List<Vector3> others)
        {
            drop.Clear();
            foreach (var kv in live)
            {
                var col = kv.Value;
                if (col == null) { drop.Add(kv.Key); continue; }
                var cp = col.transform.position;
                if (!NearAny(p, others, cp.x, cp.z, ColliderOut)) drop.Add(kv.Key);
            }
            foreach (var k in drop)
            {
                var col = live[k]; live.Remove(k);
                if (col != null) { col.gameObject.SetActive(false); free.Add(col); }
            }
            int made = 0;
            foreach (var c in cells.Values)
            {
                if (c.items == null || (DistTo(p, c.cx, c.cz) > ColliderIn && DistToOthers(others, c.cx, c.cz) > ColliderIn)) continue;
                for (int i = 0; i < c.items.Count; i++)
                {
                    var it = c.items[i];
                    DesertScatterGen.Solid((Prop)it.kind, out float r, out float h);
                    if (r <= 0f) continue;
                    if (!NearAny(p, others, it.x, it.z, ColliderIn) || live.ContainsKey((c.key, i))) continue;
                    CapsuleCollider col;
                    if (free.Count > 0) { col = free[free.Count - 1]; free.RemoveAt(free.Count - 1); }
                    else { var go = new GameObject("PropCol"); go.transform.SetParent(colRoot, false); col = go.AddComponent<CapsuleCollider>(); }
                    col.gameObject.SetActive(true);
                    // il nome decide i colpi e i passi (Surfaces.OfName): pietra, legno, tessuto
                    var kind = (Prop)it.kind;
                    col.gameObject.name = (kind == Prop.DeadTree || kind == Prop.Crate || kind == Prop.Wagon) ? "legno_prop" : (kind == Prop.Cactus || kind == Prop.Tent) ? "tessuto_prop" : "pietra_prop";
                    col.transform.position = new Vector3(it.x, it.y, it.z);
                    col.radius = r * it.scale; col.height = Mathf.Max(h * it.scale, col.radius * 2f); col.center = new Vector3(0f, col.height * 0.5f + 0.1f, 0f); col.direction = 1;
                    live[(c.key, i)] = col;
                    if (++made >= 60) return;
                }
            }
        }
    }
}
