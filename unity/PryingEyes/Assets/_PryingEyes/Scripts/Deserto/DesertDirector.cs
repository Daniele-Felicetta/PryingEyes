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
        public const float LoadBuilding = 280f, LoadTower = 340f, Unhook = 90f;
        public const float DressInBuilding = 85f, DressOutBuilding = 135f, DressInStack = 75f, DressOutStack = 120f;
        /// <summary>La zona attorno alla cattedrale dove non nasce nessun nemico all'aperto (l'arrivo e' sicuro).</summary>
        const float SafeRadius = 42f;
        /// <summary>Un passo di lavoro di un luogo (un guscio, una stanza arredata, una guardia) che dura di piu' si scrive nel registro con il suo nome: e' un fotogramma lungo che chi cammina sente.</summary>
        const long SlowStepMs = 30;

        Transform player;
        DesertTerrain terrain;
        FloorInstance cathedral;
        readonly Dictionary<long, SiteRt> rts = new Dictionary<long, SiteRt>();
        // la pelle di chi sta lontano (SiteFacade): ogni luogo entro 760 m ne ha una, finche' il suo guscio vero non e' costruito
        readonly Dictionary<long, SiteFacade> farOf = new Dictionary<long, SiteFacade>();
        readonly List<Site> farNear = new List<Site>();
        readonly List<long> farGone = new List<long>();
        readonly Stopwatch farWatch = new Stopwatch();
        Transform farRoot;
        public int FarSkins => farOf.Count;
        public int FarSkinsDone { get { int n = 0; foreach (var f in farOf.Values) if (f.Done) n++; return n; } }
        public int FarSkinsShown { get { int n = 0; foreach (var f in farOf.Values) if (f.Shown) n++; return n; } }
        /// <summary>Le pelle accese (il guscio vero non c'e') non ancora finite: quelle spente non si costruiscono.</summary>
        public int FarSkinsPending { get { int n = 0; foreach (var f in farOf.Values) if (f.Shown && !f.Done) n++; return n; } }
        public int FarVertices { get { int n = 0; foreach (var f in farOf.Values) n += f.Vertices; return n; } }
        public SiteFacade FarSkinOf(Site s) => farOf.TryGetValue(Key(s), out var f) ? f : null;
        readonly List<SiteRt> order = new List<SiteRt>();
        readonly List<Site> near = new List<Site>();
        readonly List<Site> tmp = new List<Site>();
        readonly HashSet<long> seenNear = new HashSet<long>();
        int spawnRound;
        readonly List<long> gone = new List<long>();
        readonly List<Enemy> wanderers = new List<Enemy>();
        readonly Stopwatch watch = new Stopwatch();
        float planT, spawnT, lootT;
        // la scritta dell'obiettivo (WhereText) si calcola ogni tanto, per ogni posto
        readonly float[] nearAt = new float[Seats.MaxSeats];
        readonly string[] nearTexts = new string[Seats.MaxSeats];
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
        /// <summary>Per le prove: i luoghi caricati e scaricati in tutto, i passi di lavoro che hanno superato SlowStepMs e il peggiore (quanto e cosa era).</summary>
        public int SitesLoadedTotal { get; private set; }
        public int SitesUnloadedTotal { get; private set; }
        public int SlowSteps { get; private set; }
        public long WorstStepMs { get; private set; }
        public string WorstStepWhat { get; private set; } = "";
        /// <summary>Per le prove: l'ultimo scarico di un luogo (fotogramma, quanti oggetti aveva, che luogo era): un luogo grande che si distrugge costa a fine fotogramma e si vede solo li'.</summary>
        public int LastUnloadFrame { get; private set; } = -100;
        public int LastUnloadObjects { get; private set; }
        public string LastUnloadWhat { get; private set; } = "";
        /// <summary>Per le prove: azzera il conto dei passi lenti (per misurare una camminata sola).</summary>
        public void ResetStepStats() { SlowSteps = 0; WorstStepMs = 0; WorstStepWhat = ""; }
        public int Wanderers => wanderers.Count;
        public int OutdoorCaches => caches.Count;
        public IEnumerable<SiteRt> Loaded => rts.Values;

        public static DesertDirector Create(Transform followed, DesertTerrain ground)
        {
            var go = new GameObject("DesertDirector");
            var d = go.AddComponent<DesertDirector>();
            d.player = followed;
            d.terrain = ground;
            DesertScatter.Create(go, followed);           // sassi, cactus, alberi morti, ossa, colonne, tende, pozzi, mulini...
            DesertFires.Create(go, followed);             // la luce e il fumo dei fuochi dei campi, di notte
            DesertLife.Create(go, followed);              // sterpaglie, lucertole, scorpioni, avvoltoi e pipistrelli
            DesertDust.Create(go, followed);              // la sabbia che vola col vento e nelle tempeste
            DesertSpindrift.Create(go, followed);         // i veli di sabbia che il vento stacca dalle creste delle dune
            DesertDevils.Create(go, followed);            // i diavoli di sabbia, di giorno col tempo buono
            DesertPrints.Create(go);                      // le orme sulla sabbia, cancellate dal vento in un minuto
            DesertTracks.Create(go);                      // le tracce delle gomme della moto, anche loro cancellate dal vento in un minuto
            DesertSound.Create(go, followed);             // sibilo della sabbia, grilli, gracchi, ululati e il crepitio dei fuochi
            DesertBikes.Create(go.transform);             // le moto, ai piedi della scalinata: una per posto (si montano con Usa)
            // schermo condiviso (0.9.1): gli altri posti hanno la loro sabbia che vola, le loro sterpaglie e i loro animali, i loro veli e diavoli di sabbia (i fuochi li serve la stessa
            // istanza, che accende quelli vicini a ogni posto, e il suono e' uno solo: c'e' un solo ascoltatore)
            for (int i = 1; i < Seats.All.Count; i++)
            {
                var seat = Seats.All[i];
                if (seat.player == null) continue;
                var host = new GameObject("Posto " + (i + 1));
                host.transform.SetParent(go.transform, false);
                var t = seat.player.transform;
                var life = DesertLife.Create(host, t); life.view = seat.camera; life.Reseed(i);
                DesertDust.Create(host, t).view = seat.camera;
                DesertSpindrift.Create(host, t).view = seat.camera;
                var devils = DesertDevils.Create(host, t); devils.view = seat.camera; devils.Reseed(i);
            }
            return d;
        }

        void Awake()
        {
            I = this;
            DesertNav.Blockers = BlockersIn;
            farRoot = new GameObject("DesertFar").transform;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            DesertNav.Blockers = null;
            foreach (var rt in rts.Values) rt.Unload();
            rts.Clear();
            foreach (var f in farOf.Values) f.Destroy();
            farOf.Clear();
            if (farRoot != null) Destroy(farRoot.gameObject);
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
            // cooperativa: l'ospite serve anche i compagni (DesertAnchors); da soli e sugli ospitati 'others' e' vuoto e tutto va come prima
            var others = DesertAnchors.OtherPositions;
            DesertNav.Tick(p, others);
            planT -= Time.unscaledDeltaTime;
            if (planT <= 0f) { planT = 0.25f; Plan(p, others); }
            Work(p);
            EdgeTicks();
            // chi simula (da soli o l'ospite) fa girare i nemici e guarda i boss; l'ospitato li vede arrivare dalla rete. In cooperativa il gioco non si ferma col menu di uno
            if (Coop.Sim && (!GameMenu.Blocked || Coop.Active) && Time.timeScale > 0f)
            {
                TickWanderers(p, Time.deltaTime);
                WakeTick(p, Time.deltaTime);
                BossWatch();
            }
            lootT -= Time.unscaledDeltaTime;
            if (lootT <= 0f) { lootT = 0.8f; OutdoorLoot(p); }
            LibraryTick(p);
            CleanTick();
            if (DesertMode.Clock.day != day) { day = DesertMode.Clock.day; if (day > 0) Seats.ForHuds(h => h.Banner($"GIORNO {day + 1}", "Il sole sorge ancora sulla sabbia. I luoghi sono come li hai lasciati.")); }
        }

        // ------------------------------------------------------------------ il bordo del mondo
        readonly float[] edgeT = new float[Seats.MaxSeats];

        /// <summary>Il bordo del mondo per ogni giocatore: nello schermo condiviso ogni posto per conto suo, sul proprio HUD.</summary>
        void EdgeTicks()
        {
            if (!Seats.Split) { EdgeTick(GameManager.I.player, GameManager.I.hud, 0); return; }
            for (int i = 0; i < Seats.All.Count && i < edgeT.Length; i++) EdgeTick(Seats.All[i].player, Seats.All[i].hud, i);
        }

        /// <summary>Il mondo finisce a <see cref="DesertWorld.WorldRadius"/> dalla cattedrale (oltre, i float perdono precisione e nessuno ha disegnato niente): mezzo
        /// chilometro prima un avviso, oltre il bordo il giocatore e' riportato cento metri dentro, verso casa.</summary>
        void EdgeTick(PlayerController pl, HUD hud, int seat)
        {
            if (pl == null) return;
            Vector3 p = pl.transform.position;
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z), limit = DesertWorld.WorldRadius;
            if (r < limit - 500f) return;
            if (r < limit)
            {
                if (Time.unscaledTime > edgeT[seat]) { edgeT[seat] = Time.unscaledTime + 30f; if (hud != null) hud.Toast("Più avanti il deserto finisce."); }
                return;
            }
            float k = (limit - 100f) / r, x = p.x * k, z = p.z * k;
            pl.Teleport(new Vector3(x, DesertWorld.Height(x, z) + 1.5f, z));
            if (hud != null) hud.Banner("IL DESERTO FINISCE QUI", "L'orizzonte si chiude: torna verso la cattedrale.");
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
                else if (Time.time - libTakenAt >= LibraryRenewDelay && lib.root != null && DesertAnchors.NearestViewFlat(lib.root.transform.position, p) > 5f) renew = true;
            }
            if (!renew) return;
            DesertCathedral.RefreshLibrary(cathedral);
            libWatched = Biblioteca.Current; libTakenAt = -1f; libPerks = Perks.Count;
            if (libWatched != null)
            {
                const string msg = "La Biblioteca ha tre libri nuovi sui leggii.";
                if (!Seats.Split) { if (DesertWorld.DistToCathedral(p.x, p.z) < 80f) GameManager.I?.hud?.Toast(msg); }
                else
                    foreach (var s in Seats.All)
                    {
                        if (s.player == null || s.hud == null) continue;
                        var q = s.player.transform.position;
                        if (DesertWorld.DistToCathedral(q.x, q.z) < 80f) s.hud.Toast(msg);
                    }
            }
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
        void Plan(Vector3 p, List<Vector3> others)
        {
            // i luoghi attorno a chi gioca e, sull'ospite, attorno a ogni compagno (senza doppioni)
            near.Clear();
            seenNear.Clear();
            for (int a = 0; a <= others.Count; a++)
            {
                Vector3 q = a == 0 ? p : others[a - 1];
                DesertWorld.SitesNearKnown(q.x, q.z, LoadTower, tmp);
                foreach (var s in tmp) if (seenNear.Add(Key(s))) near.Add(s);
            }
            int made = 0;
            foreach (var s in near)
            {
                long k = Key(s);
                if (rts.ContainsKey(k) || made >= 2) continue;
                if (DesertAnchors.NearestFootprint(s, p, out _) > LoadRadius(s)) continue;
                rts[k] = new SiteRt(s);
                SitesLoadedTotal++;
                made++;
            }
            gone.Clear();
            foreach (var kv in rts)
            {
                var rt = kv.Value;
                // la distanza e il punto che contano sono quelli del giocatore piu' vicino al luogo (da soli: il suo)
                rt.hdist = DesertAnchors.NearestFootprint(rt.site, p, out rt.anchor);
                rt.dist = rt.hdist;
                if (rt.hdist > LoadRadius(rt.site) + Unhook) { gone.Add(kv.Key); continue; }
                bool stack = rt.site.IsStack;
                rt.UpdateWant(p, others, stack ? DressInStack : DressInBuilding, stack ? DressOutStack : DressOutBuilding);
            }
            foreach (long k in gone)
            {
                // Object.Destroy lavora a fine fotogramma: il costo di un luogo grande non si vede attorno alla chiamata ma nel fotogramma (lo dice la camminata di prova, con LastUnloadFrame)
                LastUnloadFrame = Time.frameCount;
                LastUnloadWhat = $"{rts[k].site.kind} a {rts[k].site.cx},{rts[k].site.cz}";
                if (Application.isBatchMode)            // contare gli oggetti costa: lo si fa solo nelle prove
                {
                    LastUnloadObjects = rts[k].root != null ? rts[k].root.GetComponentsInChildren<Transform>(true).Length : 0;
                    Debug.Log($"[PryingEyes] deserto: scarico di {LastUnloadWhat} ({LastUnloadObjects} oggetti)");
                }
                rts[k].Unload(); rts.Remove(k);
            }
            SitesUnloadedTotal += gone.Count;
            if (gone.Count > 0 && cleanAt < 0f) cleanAt = Mathf.Max(Time.unscaledTime + CleanDelay, cleanLast + CleanInterval);
            order.Clear();
            order.AddRange(rts.Values);
            order.Sort((a, b) => a.dist.CompareTo(b.dist));
            PlanFar(p);
        }

        /// <summary>Le pelle di chi sta lontano: nascono per i luoghi entro il raggio (poche per volta), si spengono quando il guscio vero e' completo, si buttano oltre il raggio di tenuta.</summary>
        void PlanFar(Vector3 p)
        {
            // le pelle servono a chi guarda: il giocatore e, nello schermo condiviso, ogni altro posto (non i compagni di rete, che il loro deserto se lo guardano da se')
            var views = DesertAnchors.ViewerPositions;
            int made = 0;
            for (int v = 0; v <= views.Count; v++)
            {
                Vector3 q = v == 0 ? p : views[v - 1];
                DesertWorld.SitesNearKnown(q.x, q.z, SiteFacade.BuildRadius, farNear);
                foreach (var s in farNear)
                {
                    long k = Key(s);
                    if (farOf.ContainsKey(k) || made >= 4) continue;
                    farOf[k] = new SiteFacade(s, farRoot);
                    made++;
                }
            }
            farGone.Clear();
            foreach (var kv in farOf)
            {
                if (DesertAnchors.NearestViewFootprint(kv.Value.site, p) > SiteFacade.KeepRadius) { farGone.Add(kv.Key); continue; }
                kv.Value.SetVisible(!rts.TryGetValue(kv.Key, out var rt) || !rt.ShellComplete);
            }
            foreach (long k in farGone) { farOf[k].Destroy(); farOf.Remove(k); }
        }

        void Work(Vector3 p)
        {
            watch.Restart();
            foreach (var rt in order)
            {
                while (watch.ElapsedMilliseconds < 7)
                {
                    long t0 = watch.ElapsedMilliseconds;
                    if (!rt.Step(rt.anchor)) break;
                    long dt = watch.ElapsedMilliseconds - t0;
                    if (dt >= SlowStepMs)
                    {
                        string what = $"{rt.lastKind}, livello {rt.lastUnit}, {rt.site.kind} a {rt.site.cx},{rt.site.cz}";
                        SlowSteps++;
                        if (dt > WorstStepMs) { WorstStepMs = dt; WorstStepWhat = what; }
                        Debug.Log($"[PryingEyes] deserto: un passo di lavoro ({what}) ha impiegato {dt} ms");
                    }
                }
                if (watch.ElapsedMilliseconds >= 7) break;
            }
            // poi, con poco tempo, le pelle lontane: la piu' vicina non finita per prima
            farWatch.Restart();
            while (farWatch.ElapsedMilliseconds < 3)
            {
                SiteFacade pick = null; float best = float.MaxValue;
                foreach (var f in farOf.Values)
                {
                    if (f.Done || !f.Shown) continue;              // chi ha il guscio vero non ha bisogno della pelle (si fa quando serve)
                    float d = DesertAnchors.NearestViewFootprint(f.site, p);
                    if (d < best) { best = d; pick = f; }
                }
                if (pick == null || !pick.Step()) break;
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
            int si = Mathf.Clamp(Seats.SeatIndex, 0, nearAt.Length - 1);       // nello schermo condiviso ogni posto ha la sua scritta (chi gira adesso e' Seats.Current)
            if (Time.unscaledTime < nearAt[si]) return nearTexts[si] ?? "";      // nearAt e' l'ora del prossimo calcolo (la scritta si rinfresca ogni mezzo secondo: un conto alla rovescia a fotogrammi non scenderebbe mai)
            nearAt[si] = Time.unscaledTime + 1.5f;
            Vector3 p = gm.player.transform.position;
            var cam = Seats.Split && Seats.Current != null && Seats.Current.camera != null ? Seats.Current.camera : Camera.main;
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
            nearTexts[si] = text;
            return text;
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

        // ------------------------------------------------------------------ chi riposa e chi si sveglia (niente furtivita': Enemy.Desert.cs)
        /// <summary>A che distanza (m) si svegliano i nemici di giorno e di notte (misurata con il dislivello pesato: un nemico due piani sopra non sente chi e' fuori).
        /// Oltre <see cref="LeashDist"/> per <see cref="LeashSeconds"/> secondi chi e' sveglio rinuncia e torna a riposare dov'era.</summary>
        public const float WakeDay = 30f, WakeNight = 48f, LeashDist = 130f, LeashBoss = 220f, LeashSeconds = 7f, VerticalWeight = 2.2f;
        float wakeT;
        readonly List<Enemy> wakeList = new List<Enemy>(), dozeList = new List<Enemy>();

        static float Reach(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float v = d.y * VerticalWeight;
            return Mathf.Sqrt(d.x * d.x + d.z * d.z + v * v);
        }

        /// <summary>Quattro volte al secondo: sveglia chi riposa vicino al giocatore e rimanda a riposo chi lo ha perso. Le liste sono copie: svegliarne uno puo' far nascere altri.</summary>
        void WakeTick(Vector3 p, float dt)
        {
            wakeT -= dt;
            if (wakeT > 0f) return;
            float step = 0.25f;
            wakeT = step;
            float baseWake = DesertMode.Clock.IsNight ? WakeNight : WakeDay;
            wakeList.Clear(); dozeList.Clear();
            var others = DesertAnchors.OtherPositions;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.IsDead || e.relentless) continue;
                // chi e' piu' vicino: il giocatore di questa macchina o, sull'ospite, un compagno (un nemico si sveglia per il piu' vicino e rinuncia solo se li ha persi tutti)
                float d = Reach(p, e.transform.position);
                for (int i = 0; i < others.Count; i++) d = Mathf.Min(d, Reach(others[i], e.transform.position));
                if (e.dormant)
                {
                    float wake = baseWake * (Enemy.IsFlying(e.type) ? 1.25f : 1f) * (Enemy.IsBoss(e.type) ? 0.85f : 1f);
                    if (d < wake) wakeList.Add(e);
                }
                else
                {
                    float leash = Enemy.IsBoss(e.type) ? LeashBoss : LeashDist;
                    if (d > leash) { e.farT += step; if (e.farT >= LeashSeconds) dozeList.Add(e); }
                    else e.farT = 0f;
                }
            }
            foreach (var e in wakeList) if (e != null) e.WakeUp();
            foreach (var e in dozeList) if (e != null) e.Doze();
        }

        // ------------------------------------------------------------------ i nemici all'aperto
        void TickWanderers(Vector3 p, float dt)
        {
            for (int i = wanderers.Count - 1; i >= 0; i--)
            {
                var e = wanderers[i];
                if (e == null || e.IsDead) { wanderers.RemoveAt(i); continue; }
                float far = e.dormant ? 150f : 115f;
                // sparisce solo se e' lontano da tutti (da soli: dal giocatore)
                if (DesertAnchors.NearestFlat(e.transform.position) > far || InSanctuary(e.transform.position)) { Enemy.Remove(e); wanderers.RemoveAt(i); }
            }
            spawnT -= dt;
            if (spawnT > 0f || !HotelNav.Ready) return;
            // chi simula serve ogni giocatore: i nemici in giro nascono attorno a uno per volta, a turno, e il loro numero cresce con i compagni
            var others = DesertAnchors.Others;
            int players = 1 + others.Count;
            int turn = spawnRound++ % players;
            Vector3 centre = turn == 0 ? p : others[turn - 1].pos;
            float danger = DesertMode.Danger(centre.x, centre.z);
            bool night = DesertMode.Clock.IsNight;
            int target = Mathf.Min(night ? 14 : 8, night ? 5 + Mathf.RoundToInt(2f * danger) : 2 + Mathf.RoundToInt(1.0f * danger));
            target = Mathf.RoundToInt(target * (1f + 0.7f * (players - 1)));
            spawnT = (night ? 3.0f : 5f) / players;
            if (wanderers.Count >= target || Enemy.All.Count >= 28 + 12 * (players - 1)) return;
            Vector3 back;
            if (turn == 0)
            {
                var cam = Camera.main;
                back = cam != null ? -cam.transform.forward : Vector3.back;
            }
            else back = new Vector3(-Mathf.Sin(others[turn - 1].yaw), 0f, -Mathf.Cos(others[turn - 1].yaw));       // dietro le spalle del compagno (yaw: 0 verso +z)
            back.y = 0f;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // di giorno gruppi fermi in giro (li si incontra davanti come dietro); di notte chi caccia arriva da dietro e di lato
                bool awake = night && Random.value < 0.6f;
                Vector3 dir = awake ? Quaternion.Euler(0f, Random.Range(-115f, 115f), 0f) * back.normalized : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                Vector3 c = centre + dir * (awake ? Random.Range(46f, 64f) : Random.Range(40f, 72f));
                if (players > 1 && DesertAnchors.NearestFlat(c) < 36f) continue;             // non sotto il naso di un altro giocatore
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
                if (!awake)
                {
                    e.Doze();
                    if (!fly) e.SetHome(new Vector2(hit.position.x - 11f, hit.position.z - 11f), new Vector2(hit.position.x + 11f, hit.position.z + 11f));
                }
                wanderers.Add(e);
                return;
            }
        }

        bool InSiteFootprint(float x, float z, float margin)
        {
            DesertWorld.SitesNearKnown(x, z, margin, tmp);
            return tmp.Count > 0;
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

        /// <summary>Il boss di un luogo e' caduto (lo vede l'ospite, o chi gioca da solo): il trofeo va nel magazzino, e in cooperativa l'ospite lo dice a tutti (CoopKind.DesertBossDown).</summary>
        public void OnBossDown(Site s)
        {
            ApplyBossDown(s);
            if (Coop.IsHost) Coop.Game?.DesertBossDown(s.cx, s.cz);
        }

        /// <summary>Il boss di un luogo e' caduto: il trofeo del suo ramo va subito nel magazzino della cattedrale (di ognuno: ogni giocatore ha il suo), e il luogo non lo rimette.</summary>
        public void ApplyBossDown(Site s)
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
            // attorno a ogni giocatore (nello schermo condiviso: ogni posto); si buttano solo quelli lontani da tutti
            var views = DesertAnchors.ViewerPositions;
            for (int v = 0; v <= views.Count; v++) OutdoorLootAround(v == 0 ? p : views[v - 1]);
            CullLoot(p, views);
        }

        void OutdoorLootAround(Vector3 p)
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
        }

        /// <summary>Si buttano quelli rimasti indietro (e si riproveranno se il giocatore torna: quelli presi non rinascono), lontani da tutti i giocatori.</summary>
        void CullLoot(Vector3 p, List<Vector3> views)
        {
            gone.Clear();
            foreach (var kv in caches)
            {
                int cx = (int)(kv.Key >> 32) - 100000, cz = (int)(kv.Key & 0xffffffffL) - 100000;
                float x = (cx + 0.5f) * CellSize, z = (cz + 0.5f) * CellSize;
                float dx = x - p.x, dz = z - p.z, d2 = dx * dx + dz * dz;
                for (int i = 0; i < views.Count; i++) { dx = x - views[i].x; dz = z - views[i].z; d2 = Mathf.Min(d2, dx * dx + dz * dz); }
                bool taken = kv.Value.pickup == null && kv.Value.rack == null;
                if (taken || d2 > 260f * 260f) gone.Add(kv.Key);
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
            var wb = WallBuy.Place(weaponIndex, 0, root.transform.TransformPoint(new Vector3(0f, 1.55f, 0.0f)), Quaternion.LookRotation(face), def.GradeColor.ToColor(), true);
            wb.transform.SetParent(root.transform, true);
            return root;
        }
    }
}
