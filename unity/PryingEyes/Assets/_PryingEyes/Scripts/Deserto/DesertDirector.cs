using System.Collections.Generic;
using System.Diagnostics;
using Production.Economy;
using PryingEyes.Cattedrale;
using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace PryingEyes.Deserto
{
    /// <summary>Il regista del deserto: ogni fotogramma tiene la navmesh a tasselli, fa nascere e sparire i luoghi attorno al giocatore (SiteRt, a pezzi
    /// di pochi millisecondi), manda i nemici in giro sulla sabbia (pochi di giorno, tanti di notte, piu' forti e vari con la distanza dalla cattedrale),
    /// sparge il bottino all'aperto (punti, munizioni, medikit, essenza, qualche pannello d'arma comune) e guarda i boss: quando uno cade il suo
    /// trofeo va nel magazzino della cattedrale. Il mondo e' una funzione del seme (DesertWorld): qui non si salva niente.</summary>
    public sealed class DesertDirector : MonoBehaviour
    {
        public static DesertDirector I { get; private set; }

        /// <summary>Quanto lontano (m) un luogo si costruisce e quanto vicino si arreda. Le torri sono alte: si vedono e si caricano da piu' lontano.</summary>
        public const float LoadBuilding = 280f, LoadTower = 520f, Unhook = 90f;
        public const float DressInBuilding = 85f, DressOutBuilding = 135f, DressInStack = 75f, DressOutStack = 120f;
        /// <summary>La zona attorno alla cattedrale dove non nasce nessun nemico all'aperto (l'arrivo e' sicuro).</summary>
        const float SafeRadius = 42f;

        Transform player;
        DesertTerrain terrain;
        FloorInstance cathedral;
        readonly Dictionary<long, SiteRt> rts = new Dictionary<long, SiteRt>();
        readonly List<SiteRt> order = new List<SiteRt>();
        readonly List<Site> near = new List<Site>();
        readonly List<Site> tmp = new List<Site>();
        readonly List<long> gone = new List<long>();
        readonly List<Enemy> wanderers = new List<Enemy>();
        readonly Stopwatch watch = new Stopwatch();
        float planT, spawnT, patrolT, lootT, nearT;
        string nearText = "";
        int day = -1;

        /// <summary>Secondi tra il libro preso e i tre libri nuovi sui leggii: nel deserto non ci sono altri piani con un'altra Biblioteca, quindi la stessa si rinnova
        /// (solo se il giocatore non e' tra i leggii). Le prove lo accorciano.</summary>
        public static float LibraryRenewDelay = 45f;
        float libT, libTakenAt = -1f;
        Biblioteca libWatched;
        int libPerks = -1;

        // il bottino all'aperto: una "cella" da 70 m ha al piu' un oggetto (e, di rado, un pannello d'arma su un sostegno)
        sealed class Cache { public DesertPickup pickup; public GameObject rack; }
        readonly Dictionary<long, Cache> caches = new Dictionary<long, Cache>();
        readonly HashSet<long> emptyCells = new HashSet<long>();
        const float CellSize = 70f;

        public int LoadedSites => rts.Count;
        public int Wanderers => wanderers.Count;
        public int OutdoorCaches => caches.Count;
        public IEnumerable<SiteRt> Loaded => rts.Values;

        public static DesertDirector Create(Transform followed, DesertTerrain ground)
        {
            var go = new GameObject("DesertDirector");
            var d = go.AddComponent<DesertDirector>();
            d.player = followed;
            d.terrain = ground;
            return d;
        }

        void Awake()
        {
            I = this;
            DesertNav.Blockers = BlockersIn;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            DesertNav.Blockers = null;
            foreach (var rt in rts.Values) rt.Unload();
            rts.Clear();
            foreach (var e in wanderers) if (e != null) Enemy.Remove(e);
            wanderers.Clear();
            foreach (var c in caches.Values) FreeCache(c);
            caches.Clear();
        }

        public void SetCathedral(FloorInstance fi) { cathedral = fi; }

        static long Key(Site s) => ((long)(s.cx + 4096) << 20) ^ (long)(s.cz + 4096);

        static float FootDist(Site s, Vector3 p)
        {
            float dx = Mathf.Max(Mathf.Max(s.fx0 - p.x, p.x - s.fx1), 0f), dz = Mathf.Max(Mathf.Max(s.fz0 - p.z, p.z - s.fz1), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float LoadRadius(Site s) => s.IsTower ? LoadTower : LoadBuilding;

        // ------------------------------------------------------------------ ogni fotogramma
        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null || player == null) return;
            Vector3 p = player.position;
            DesertNav.Tick(p);
            planT -= Time.unscaledDeltaTime;
            if (planT <= 0f) { planT = 0.25f; Plan(p); }
            Work(p);
            EdgeTick(p);
            if (!GameMenu.Blocked && Time.timeScale > 0f)
            {
                TickWanderers(p, Time.deltaTime);
                BossWatch();
            }
            lootT -= Time.unscaledDeltaTime;
            if (lootT <= 0f) { lootT = 0.8f; OutdoorLoot(p); }
            LibraryTick(p);
            CleanTick();
            if (DesertMode.Clock.day != day) { day = DesertMode.Clock.day; if (day > 0) Seats.ForHuds(h => h.Banner($"GIORNO {day + 1}", "Il sole sorge ancora sulla sabbia. I luoghi sono come li hai lasciati.")); }
        }

        // ------------------------------------------------------------------ il bordo del mondo
        float edgeT;

        /// <summary>Il mondo finisce a <see cref="DesertWorld.WorldRadius"/> dalla cattedrale (oltre, i float perdono precisione e nessuno ha disegnato niente): mezzo
        /// chilometro prima un avviso, oltre il bordo il giocatore e' riportato cento metri dentro, verso casa.</summary>
        void EdgeTick(Vector3 p)
        {
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z), limit = DesertWorld.WorldRadius;
            if (r < limit - 500f) return;
            if (r < limit)
            {
                if (Time.unscaledTime > edgeT) { edgeT = Time.unscaledTime + 30f; Seats.ForHuds(h => h.Toast("Più avanti il deserto finisce.")); }
                return;
            }
            float k = (limit - 100f) / r, x = p.x * k, z = p.z * k;
            GameManager.I.player.Teleport(new Vector3(x, DesertWorld.Height(x, z) + 1.5f, z));
            Seats.ForHuds(h => h.Banner("IL DESERTO FINISCE QUI", "L'orizzonte si chiude: torna verso la cattedrale."));
            Debug.Log($"[PryingEyes] deserto: oltre il bordo del mondo ({r:F0} m), riportato a ({x:F0}, {z:F0})");
        }

        // ------------------------------------------------------------------ la Biblioteca dei Voti
        /// <summary>Una volta al secondo: la Biblioteca (se l'opera e' costruita) offre tre libri; preso uno gli altri si richiudono, e dopo <see cref="LibraryRenewDelay"/>
        /// secondi (con il giocatore lontano dai leggii) i leggii si rinnovano con tre perk nuovi. Se non c'era (i quattro posti pieni, niente spazio) ci si riprova
        /// solo quando il numero dei perk cambia (la morte ne toglie uno).</summary>
        void LibraryTick(Vector3 p)
        {
            libT -= Time.unscaledDeltaTime;
            if (libT > 0f) return;
            libT = 1f;
            var fd = FloorDirector.I;
            if (cathedral == null || fd == null || !fd.InHub || fd.transitioning || fd.cur != cathedral || !Fabbrica.Costruita("biblioteca_voti")) return;
            var lib = Biblioteca.Current;
            if (lib != libWatched) { libWatched = lib; libTakenAt = -1f; if (libPerks < 0) libPerks = Perks.Count; }
            bool renew = false;
            if (lib == null)
                renew = !Perks.Full && Perks.Count != libPerks;
            else if (lib.chosen)
            {
                if (libTakenAt < 0f) libTakenAt = Time.time;
                else if (Time.time - libTakenAt >= LibraryRenewDelay && lib.root != null && Flat(lib.root.transform.position, p) > 5f) renew = true;
            }
            if (!renew) return;
            DesertCathedral.RefreshLibrary(cathedral);
            libWatched = Biblioteca.Current; libTakenAt = -1f; libPerks = Perks.Count;
            if (libWatched != null && DesertWorld.DistToCathedral(p.x, p.z) < 80f)
                GameManager.I?.hud?.Toast("La Biblioteca ha tre libri nuovi sui leggii.");
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // ------------------------------------------------------------------ la pulizia della memoria
        /// <summary>I luoghi si costruiscono mentre si gioca: le mesh e i materiali dei loro muri non sono di nessuno (non sono asset del progetto) e quando un luogo si scarica
        /// restano in memoria finche' qualcuno non chiama <c>Resources.UnloadUnusedAssets</c> (Unity lo fa da solo solo cambiando scena, e qui la scena non cambia mai). Lo ha
        /// mostrato la prova di memoria (-desertonly soak): ogni giro di quattro luoghi lasciava 1.800 mesh e 120 MB. A piedi per un'ora si caricano e scaricano centinaia di luoghi:
        /// dopo che se ne scarica uno, passati <see cref="CleanDelay"/> secondi (il tempo che gli oggetti distrutti se ne vadano davvero) si fa la pulizia, al piu' una volta ogni
        /// <see cref="CleanInterval"/> secondi. Nella prova costa pochi millisecondi (il registro dice quanto a ogni pulizia: durata e fotogramma piu' lungo), ma costa di piu' con piu'
        /// cose in memoria: per questo non parte con un nemico a meno di <see cref="CleanCalm"/> metri (si riprova ogni due secondi, al piu' sei volte).</summary>
        public static float CleanDelay = 6f, CleanInterval = 60f, CleanCalm = 30f;
        float cleanAt = -1f, cleanLast = -1000f, sweepStart, sweepWorst;
        int cleanDefer;
        AsyncOperation sweep;
        public int Sweeps { get; private set; }
        /// <summary>Quanto e' durata l'ultima pulizia (ms) e il fotogramma piu' lungo mentre durava; il peggiore di tutti i fotogrammi di pulizia.</summary>
        public float LastSweepMs { get; private set; }
        public float LastSweepWorstMs { get; private set; }
        public float WorstSweepMs { get; private set; }
        /// <summary>Una pulizia e' attesa o in corso.</summary>
        public bool Cleaning => cleanAt >= 0f || (sweep != null && !sweep.isDone);

        static bool EnemyNear(Vector3 p, float radius)
        {
            float r2 = radius * radius;
            foreach (var e in Enemy.All)
                if (e != null && !e.IsDead && (e.transform.position - p).sqrMagnitude < r2) return true;
            return false;
        }

        void CleanTick()
        {
            if (sweep != null)
            {
                sweepWorst = Mathf.Max(sweepWorst, Time.unscaledDeltaTime);
                if (!sweep.isDone) return;
                sweep = null;
                LastSweepMs = (Time.realtimeSinceStartup - sweepStart) * 1000f;
                LastSweepWorstMs = sweepWorst * 1000f;
                WorstSweepMs = Mathf.Max(WorstSweepMs, LastSweepWorstMs);
                Debug.Log($"[PryingEyes] deserto: pulizia della memoria in {LastSweepMs:F0} ms, il fotogramma piu' lungo {LastSweepWorstMs:F0} ms");
            }
            if (cleanAt < 0f || Time.unscaledTime < cleanAt) return;
            if (cleanDefer < 6 && player != null && EnemyNear(player.position, CleanCalm)) { cleanDefer++; cleanAt = Time.unscaledTime + 2f; return; }
            cleanDefer = 0;
            cleanAt = -1f;
            cleanLast = Time.unscaledTime;
            sweepStart = Time.realtimeSinceStartup; sweepWorst = 0f;
            sweep = Resources.UnloadUnusedAssets();
            Sweeps++;
        }

        // ------------------------------------------------------------------ i luoghi
        void Plan(Vector3 p)
        {
            DesertWorld.SitesNearKnown(p.x, p.z, LoadTower, near);
            int made = 0;
            foreach (var s in near)
            {
                long k = Key(s);
                if (rts.ContainsKey(k) || made >= 2) continue;
                if (FootDist(s, p) > LoadRadius(s)) continue;
                rts[k] = new SiteRt(s);
                made++;
            }
            gone.Clear();
            foreach (var kv in rts)
            {
                var rt = kv.Value;
                rt.hdist = FootDist(rt.site, p);
                rt.dist = rt.hdist;
                if (rt.hdist > LoadRadius(rt.site) + Unhook) { gone.Add(kv.Key); continue; }
                bool stack = rt.site.IsStack;
                rt.UpdateWant(p, stack ? DressInStack : DressInBuilding, stack ? DressOutStack : DressOutBuilding);
            }
            foreach (long k in gone) { rts[k].Unload(); rts.Remove(k); }
            if (gone.Count > 0 && cleanAt < 0f) cleanAt = Mathf.Max(Time.unscaledTime + CleanDelay, cleanLast + CleanInterval);
            order.Clear();
            order.AddRange(rts.Values);
            order.Sort((a, b) => a.dist.CompareTo(b.dist));
        }

        void Work(Vector3 p)
        {
            watch.Restart();
            foreach (var rt in order)
            {
                while (watch.ElapsedMilliseconds < 7 && rt.Step(p)) { }
                if (watch.ElapsedMilliseconds >= 7) break;
            }
        }

        /// <summary>Le radici degli arredi (ostacoli per la navmesh) nei dintorni di un'area: dei luoghi arredati e della cattedrale.</summary>
        List<Transform> BlockersIn(Bounds area)
        {
            var list = new List<Transform>();
            foreach (var rt in rts.Values)
            {
                var a = rt.Area;
                if (a.min.x > area.max.x || a.max.x < area.min.x || a.min.z > area.max.z || a.max.z < area.min.z) continue;
                rt.CollectBlockers(list);
            }
            if (cathedral != null)
            {
                var b = cathedral.bounds;
                // la cattedrale e' un santuario: tutta la sua radice (pavimenti, scalinata, arredi) e' non calpestabile per i nemici, che restano ai piedi
                // della scalinata. Il giocatore cammina con il CharacterController e non ne risente.
                if (!(b.min.x > area.max.x || b.max.x < area.min.x || b.min.z > area.max.z || b.max.z < area.min.z) && cathedral.root != null)
                    list.Add(cathedral.root.transform);
            }
            return list;
        }

        /// <summary>Chi vola sopra la cattedrale (i nemici a piedi non hanno navmesh li') sparisce: la zona sicura resta sicura.</summary>
        public static bool InSanctuary(Vector3 v) => DesertWorld.DistToCathedral(v.x, v.z) < 1.5f && v.y > DesertWorld.GroundY + 1f;

        public static readonly string[] Where8 = { "davanti", "davanti a destra", "a destra", "dietro a destra", "dietro", "dietro a sinistra", "a sinistra", "davanti a sinistra" };

        /// <summary>", a destra": dove sta un punto rispetto a dove guarda il giocatore (vuoto se e' li').</summary>
        static string DirText(Vector3 from, Vector3 look, float tx, float tz)
        {
            int s = DesertWorld.Sector(tx - from.x, tz - from.z, look.x, look.z);
            return s < 0 ? "" : ", " + Where8[s];
        }

        static int Rounded(float m) => m < 300f ? Mathf.RoundToInt(m / 10f) * 10 : Mathf.RoundToInt(m / 50f) * 50;

        /// <summary>Per l'obiettivo: il luogo piu' vicino e, lontano da casa, la cattedrale, con da che parte stanno rispetto a dove si guarda.</summary>
        public string WhereText()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return "";
            if (Time.unscaledTime < nearT) return nearText;      // nearT e' l'ora del prossimo calcolo (la scritta si rinfresca ogni mezzo secondo: un conto alla rovescia a fotogrammi non scenderebbe mai)
            nearT = Time.unscaledTime + 1.5f;
            Vector3 p = gm.player.transform.position;
            var cam = Camera.main;
            Vector3 look = cam != null ? cam.transform.forward : Vector3.forward;
            DesertWorld.SitesNearKnown(p.x, p.z, 500f, tmp);
            Site best = null; float bd = float.MaxValue;
            foreach (var s in tmp) { float d = FootDist(s, p); if (d < bd) { bd = d; best = s; } }
            string text = best == null ? "" : $"  ·  {KindName(best)} a {Rounded(bd)} m{DirText(p, look, best.CentreX, best.CentreZ)}";
            float dc = DesertWorld.DistToCathedral(p.x, p.z);
            if (dc > 60f)
            {
                DesertWorld.CathedralCentre(out float cx, out float cz);
                text += $"  ·  cattedrale a {Rounded(dc)} m{DirText(p, look, cx, cz)}";
            }
            nearText = text;
            return nearText;
        }

        public static string KindName(Site s)
        {
            switch (s.kind)
            {
                case SiteKind.Casa: return "una casa";
                case SiteKind.Cappella: return "una cappella";
                case SiteKind.Locanda: return "una locanda";
                case SiteKind.Chiesa: return "una chiesa";
                case SiteKind.Albergo: return "un albergo";
                case SiteKind.Teatro: return "un teatro";
                case SiteKind.Sanatorio: return "un sanatorio";
                case SiteKind.Complesso: return "un complesso";
                case SiteKind.Torre: return "una torre";
                default: return "un mausoleo";
            }
        }

        // ------------------------------------------------------------------ i nemici all'aperto
        void TickWanderers(Vector3 p, float dt)
        {
            for (int i = wanderers.Count - 1; i >= 0; i--)
            {
                var e = wanderers[i];
                if (e == null || e.IsDead) { wanderers.RemoveAt(i); continue; }
                if ((e.transform.position - p).sqrMagnitude > 115f * 115f || InSanctuary(e.transform.position)) { Enemy.Remove(e); wanderers.RemoveAt(i); }
            }
            patrolT -= dt;
            if (patrolT <= 0f) { patrolT = 7f; RefreshPatrol(p); }
            spawnT -= dt;
            if (spawnT > 0f || !HotelNav.Ready) return;
            float danger = DesertMode.Danger(p.x, p.z);
            bool night = DesertMode.Clock.IsNight;
            int target = Mathf.Min(12, night ? 4 + Mathf.RoundToInt(2f * danger) : 1 + Mathf.RoundToInt(0.8f * danger));
            spawnT = night ? 3.2f : 7f;
            if (wanderers.Count >= target || Enemy.All.Count >= 24) return;
            var cam = Camera.main;
            Vector3 back = cam != null ? -cam.transform.forward : Vector3.back;
            back.y = 0f;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 dir = Quaternion.Euler(0f, Random.Range(-115f, 115f), 0f) * back.normalized;
                Vector3 c = p + dir * Random.Range(36f, 54f);
                if (DesertWorld.DistToCathedral(c.x, c.z) < SafeRadius) continue;
                if (InSiteFootprint(c.x, c.z, 7f)) continue;
                c.y = DesertWorld.Height(c.x, c.z);
                if (!DesertNav.Covers(c)) continue;
                if (!NavMesh.SamplePosition(c + Vector3.up * 1.5f, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                var rng = new Rng(Random.Range(1, 1 << 28));
                var type = SiteRt.PickType(rng, Theme.Albergo, danger);
                if (Enemy.IsFlying(type) && !night) type = Enemy.EnemyType.Monaco;
                bool fly = Enemy.IsFlying(type);
                var e = Enemy.Spawn(type, fly ? hit.position + Vector3.up * 1.8f : hit.position);
                if (e == null) return;
                e.maxHp *= 0.85f + 0.30f * danger; e.hp = e.maxHp; e.speed *= 1f + 0.06f * (danger - 1f);
                if (!fly) e.RiseFromGround(1.4f);
                if (night) e.Investigate(p);
                wanderers.Add(e);
                return;
            }
        }

        bool InSiteFootprint(float x, float z, float margin)
        {
            DesertWorld.SitesNearKnown(x, z, margin, tmp);
            return tmp.Count > 0;
        }

        /// <summary>Punti di pattuglia attorno al giocatore: chi non ti ha ancora visto gira dove sei tu.</summary>
        void RefreshPatrol(Vector3 p)
        {
            Enemy.PatrolPoints.Clear();
            for (int i = 0; i < 14 && Enemy.PatrolPoints.Count < 9; i++)
            {
                Vector2 o = Random.insideUnitCircle.normalized * Random.Range(16f, 46f);
                var c = new Vector3(p.x + o.x, DesertWorld.Height(p.x + o.x, p.z + o.y), p.z + o.y);
                if (!DesertNav.Covers(c)) continue;
                if (NavMesh.SamplePosition(c + Vector3.up * 1.5f, out NavMeshHit hit, 3f, NavMesh.AllAreas)) Enemy.PatrolPoints.Add(hit.position);
            }
        }

        // ------------------------------------------------------------------ i boss
        void BossWatch()
        {
            foreach (var rt in rts.Values)
            {
                var b = rt.boss;
                if (b == null || !b.IsDead) continue;
                rt.boss = null;
                OnBossDown(rt.site);
            }
        }

        /// <summary>Il boss di un luogo e' caduto: il trofeo del suo ramo va subito nel magazzino della cattedrale, e il luogo non lo rimette.</summary>
        public void OnBossDown(Site s)
        {
            int ramo = DesertMode.BranchOf(s);
            DesertMode.BossesDown.Add(DesertMode.Pack(s, s.boss, 0));
            Bundle trophy = null;
            Seats.EachOrOnce(_ => { var t = Fabbrica.Trofeo(Rami.Piano(ramo, 3)); if (trophy == null) trophy = t; });
            var def = Rami.Tutti[ramo];
            Seats.ForHuds(h => h.Banner($"{def.bossTitolo} è caduto", "Al sicuro nella cattedrale: " + Risorse.Testo(trophy)));
            Sfx.UI(SoundBank.BossRoar, 0.6f, 0.7f);
            Debug.Log($"[PryingEyes] deserto: {def.bossTitolo} caduto a {s}, trofeo [{trophy}]");
        }

        // ------------------------------------------------------------------ il bottino all'aperto
        static long CellKey(int cx, int cz) => ((long)(cx + 100000) << 32) ^ (uint)(cz + 100000);

        void OutdoorLoot(Vector3 p)
        {
            int pcx = Mathf.FloorToInt(p.x / CellSize), pcz = Mathf.FloorToInt(p.z / CellSize);
            const int R = 2;
            for (int i = -R; i <= R; i++)
                for (int j = -R; j <= R; j++)
                {
                    int cx = pcx + i, cz = pcz + j;
                    long k = CellKey(cx, cz);
                    if (caches.ContainsKey(k) || emptyCells.Contains(k)) continue;
                    var rng = new Rng((int)(DesertNoise.Hash(cx, cz, DesertMode.Seed + 733) & 0x7fffffff));
                    if (!rng.Chance(0.40)) { emptyCells.Add(k); continue; }
                    float x = (cx + rng.Range(0.1f, 0.9f)) * CellSize, z = (cz + rng.Range(0.1f, 0.9f)) * CellSize;
                    if (DesertWorld.DistToCathedral(x, z) < 20f) { emptyCells.Add(k); continue; }
                    if (InSiteFootprint(x, z, 9f)) { emptyCells.Add(k); continue; }
                    if (terrain != null && !terrain.Ready(new Vector3(x, 0f, z), 4f)) continue;         // la sabbia deve esserci, altrimenti si riprova
                    float y = GroundAt(x, z);
                    var c = new Cache();
                    if (rng.Chance(0.14))
                    {
                        var pool = new List<int>();
                        int start = WeaponDefs.IndexOf("p9_pieta");
                        for (int w = 0; w < WeaponDefs.All.Length; w++) if (w != start && !WeaponDefs.All[w].cityOnly && WeaponDefs.All[w].grade <= 1) pool.Add(w);
                        if (pool.Count > 0) c.rack = PlaceRack(new Vector3(x, y, z), rng.Range(0f, 360f), pool[rng.Range(0, pool.Count)]);
                    }
                    else
                    {
                        var kind = (PickKind)rng.Weighted(new double[] { 34, 28, 16, 22 });
                        float danger = DesertMode.Danger(x, z);
                        int amount = kind == PickKind.Points ? Mathf.RoundToInt(rng.Range(40f, 110f) * danger / 10f) * 10 : kind == PickKind.Medikit ? 50 : kind == PickKind.Essence ? 30 : 0;
                        long memory = (1L << 62) ^ k;
                        c.pickup = DesertPickup.Spawn(null, new Vector3(x, y + 0.05f, z), kind, amount, memory);
                        if (c.pickup == null) { emptyCells.Add(k); continue; }       // preso in questa partita
                    }
                    caches[k] = c;
                }
            // si buttano quelli rimasti indietro (e si riproveranno se il giocatore torna: quelli presi non rinascono)
            gone.Clear();
            foreach (var kv in caches)
            {
                int cx = (int)(kv.Key >> 32) - 100000, cz = (int)(kv.Key & 0xffffffffL) - 100000;
                float dx = (cx + 0.5f) * CellSize - p.x, dz = (cz + 0.5f) * CellSize - p.z;
                bool taken = kv.Value.pickup == null && kv.Value.rack == null;
                if (taken || dx * dx + dz * dz > 260f * 260f) gone.Add(kv.Key);
            }
            foreach (long k in gone) { FreeCache(caches[k]); caches.Remove(k); }
        }

        static float GroundAt(float x, float z)
        {
            if (Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out RaycastHit hit, 800f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return DesertWorld.Height(x, z);
        }

        static void FreeCache(Cache c)
        {
            if (c.pickup != null) Destroy(c.pickup.gameObject);
            if (c.rack != null) Destroy(c.rack);
        }

        /// <summary>Un pannello d'arma libero su un sostegno di pietra e legno, all'aperto: tre assi e due pali, il pannello davanti.</summary>
        static GameObject PlaceRack(Vector3 at, float yaw, int weaponIndex)
        {
            ShrineKit.Mats();
            var root = new GameObject("Rastrelliera");
            root.transform.position = at;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            void Beam(string n, Vector3 pos, Vector3 size, Material m)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = n; g.transform.SetParent(root.transform, false);
                g.transform.localPosition = pos; g.transform.localScale = size;
                g.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            Beam("Palo", new Vector3(-0.78f, 0.95f, -0.12f), new Vector3(0.14f, 1.9f, 0.14f), ShrineKit.Wood);
            Beam("Palo", new Vector3(0.78f, 0.95f, -0.12f), new Vector3(0.14f, 1.9f, 0.14f), ShrineKit.Wood);
            Beam("Fondo", new Vector3(0f, 1.55f, -0.08f), new Vector3(1.6f, 1.1f, 0.10f), ShrineKit.Wood);
            Beam("Zoccolo", new Vector3(0f, 0.12f, 0.1f), new Vector3(1.9f, 0.24f, 0.7f), ShrineKit.StoneDark);
            var def = WeaponDefs.All[weaponIndex];
            Vector3 face = root.transform.TransformDirection(Vector3.forward);
            var wb = WallBuy.Place(weaponIndex, 0, root.transform.TransformPoint(new Vector3(0f, 1.55f, 0.0f)), Quaternion.LookRotation(face), def.GradeColor, true);
            wb.transform.SetParent(root.transform, true);
            return root;
        }
    }
}
