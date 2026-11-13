using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Regista della partita a piani: genera la pianta, la costruisce, mette il giocatore nell'ascensore di arrivo,
    /// piazza le armi sui muri e i candelabri da accendere, manda le orde (a ogni candela e a intervalli) e fa scendere al
    /// piano dopo quando le candele sono tutte accese. Sostituisce le ondate della hall.</summary>
    public partial class FloorDirector : MonoBehaviour
    {
        public static FloorDirector I;
        public int floor;
        public int runSeed;
        public FloorInstance cur;
        public bool transitioning;
        int units = 1;                       // ali-coppia del piano in corso: orde e popolazione crescono con il piano

        int lit, total, deaths;
        bool exitOpen, firstHorde, lastHorde;
        public bool ExitOpen => exitOpen;
        float trickleT, floorTime, hudT, probeT;
        bool perfStarted;                    // -perftest: la sonda dei fotogrammi gira una volta sola per partita
        Room playerRoom;
        readonly HashSet<int> explored = new HashSet<int>();
        readonly HashSet<WallBuy> foundWeapons = new HashSet<WallBuy>();
        readonly HashSet<Candelabro> foundCandles = new HashSet<Candelabro>();
        readonly HashSet<int> foundShrines = new HashSet<int>();      // 0 Biblioteca, 1 Scrigno, 2 altare: segnati sulla mappa quando li hai visti da vicino
        bool foundExit, mapOpen, miniMap = true;
        float mapScan;
        GameManager gm;
        const string BotLog = "C:/Users/danie/Desktop/prying-eyes/hotel_build/proctest.txt";

        static readonly string[] FloorLines =
        {
            "Il campanello della reception suona. Al banco non c'è nessuno.",
            "L'ascensore scende. Nessuno l'ha chiamato.",
            "Le candele si spengono da sole, una a una.",
            "Il grammofono suona in una camera chiusa a chiave.",
            "L'organo non ha un organista.",
            "Qualcuno ha rifatto i letti. Dal lato sbagliato.",
            "L'albergo e la chiesa non hanno mai avuto muri tra loro.",
            "Stanotte tutte le camere sono occupate.",
        };

        void Awake()
        {
            I = this;
            if (GetComponent<RoundDirector>() == null) gameObject.AddComponent<RoundDirector>();     // round nelle zone sigillabili
        }

        static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs()) if (s == a) return true;
            return false;
        }

        static int ArgInt(string name, int def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name && int.TryParse(a[i + 1], out int v)) return v;
            return def;
        }

        static string ArgStr(string name, string def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        static bool? noEnemies;
        /// <summary>Solo per le prove (-autotest -noenemies): il piano resta vuoto, per misurare la sola navigazione.</summary>
        static bool NoEnemies => noEnemies ??= GameManager.autoTest && HasArg("-noenemies");

        /// <summary>Avvia la partita: primo piano. Lo chiama il GameManager su "Gioca". In cooperativa un ospitato la avvia con il seme e il
        /// piano dell'ospite.</summary>
        public void StartRun(int seed = 0, int startFloor = 0)
        {
            gm = GameManager.I;
            StealthRig.Install();      // torcia, ping, freccia, cuore (Scripts/Stealth)
            if (HasArg("-oldplan")) PlanGen.UseGrid = false;      // le ali d'albergo e di chiesa di prima, al posto della pianta a griglia
            runSeed = seed != 0 ? seed : ArgInt("-seed", Random.Range(1, 1 << 28));
            floor = 0;
            deaths = 0;
            RoundDirector.ResetRun();
            Drops.ResetRun();
            Perks.ResetRun();
            StartCoroutine(GoTo(startFloor > 0 ? startFloor : ArgInt("-floor", 1), true));
        }

        /// <summary>Cooperativa, sull'ospite: un compagno e' entrato nell'ascensore aperto, si scende.</summary>
        public void CoopDescend()
        {
            if (!transitioning && exitOpen && cur != null) StartCoroutine(Descend());
        }

        /// <summary>Cooperativa: l'ospite ha cambiato piano. Chi e' ancora nel piano di prima comincia la discesa (stessa dissolvenza di Descend).</summary>
        public void CoopFollow(int seed, int toFloor)
        {
            if (transitioning || floor == toFloor || floor == 0) return;
            if (seed != 0) runSeed = seed;
            StartCoroutine(toFloor == floor + 1 ? Descend() : GoTo(toFloor, false));
        }

        // ------------------------------------------------------------------ cambio piano
        IEnumerator GoTo(int n, bool first)
        {
            transitioning = true;
            gm = GameManager.I;
            var pl = gm.player;
            pl.invulnerable = true;
            var hud = gm.hud;
            Coop.OnFloorStart(n);                // cooperativa: l'ospite annuncia il piano (epoca), gli altri aspettano il suo
            if (cur != null)
            {
                RoundDirector.I?.Disarm();       // le serrande sono figlie del piano: vanno via con lui
                Destroy(cur.root);
                cur = null;
                Enemy.ClearAll();
                Drops.NewFloor();
                WallBuy.ClearAll();
                Interact.Clear();
                LightPool.Clear();
                yield return null;
            }
            floor = n;
            Perks.NewFloor();                    // la Rinascita e' di nuovo pronta, il Velo e l'Occhio si spengono
            Biblioteca.Clear();
            explored.Clear(); foundWeapons.Clear(); foundCandles.Clear(); foundShrines.Clear(); foundExit = false; mapOpen = false;
            // piu' si scende, piu' buio: la scala di tutte le luci del piano, letta da RoomKits.AddLight mentre costruisce
            LightPool.Dim = LightDim(n);
            var plan = PlanGen.Generate(runSeed + n * 1013, n);
            if (plan == null) { Debug.LogError("[PryingEyes] piano non generabile: " + PlanGen.LastFailure); yield break; }
            units = plan.units;
            float t0 = Time.realtimeSinceStartup;
            cur = FloorBuilder.Build(plan, null);
            float tBuild = Time.realtimeSinceStartup - t0;
            Physics.SyncTransforms();
            yield return null;
            var regions = RoundDirector.PrepareNav(plan);     // zone sigillabili: le loro soglie diventano aree a parte della navmesh
            PlaceShrines(plan, regions);                      // Biblioteca, Scrigno, altare della Consacrazione: prima della navmesh, sono ostacoli
            HotelNav.Rebuild(cur.bounds, cur.propRoots);
            int stripped = Connectivity.Repair(cur, out int problems);
            if (problems > 0) Debug.Log($"[PryingEyes] connettivita': {problems} problemi, {Connectivity.LastCleared} arredi tolti dai varchi, {stripped} stanze svuotate");
            ValidateShrines(plan, regions);                   // leggii, scrigno e altare devono avere un posto davanti che si raggiunge a piedi
            ApplyLook(plan, floor);
            ValidateReach(plan);

            // obiettivo: candele da accendere; uscita sigillata finche' non lo sono tutte
            lit = 0; total = cur.candles.Count; exitOpen = false; firstHorde = false; lastHorde = false;
            Guide.I?.ClearMarks();
            Stealth.Reset();
            ghostMark = Stealth.Spotted;
            PlaceBatteries(plan);
            BuildPatrolPoints(plan);
            if (Coop.Sim) PopulateFloor(plan);   // in cooperativa i nemici li simula solo l'ospite
            foreach (var c in cur.candles) c.onLit += OnCandle;
            RoundDirector.I?.Arm(cur, regions);
            if (cur.exit != null)
            {
                cur.exit.unlocked = false;
                cur.exit.lockedText = () => $"L'ASCENSORE È SIGILLATO — CANDELE {lit}/{total}";
                cur.exit.onEnter = () =>
                {
                    if (transitioning) return;
                    if (Coop.IsClient) Coop.Game?.RequestDescend();      // cooperativa: decide l'ospite
                    else StartCoroutine(Descend());
                };
            }
            PlaceWallBuys(plan);
            StartCoroutine(Gramophone(plan));
            floorTime = 0f; trickleT = 40f; hudT = 0f;

            // giocatore nella cabina di arrivo
            var lift = cur.arrival;
            if (lift != null)
            {
                Vector3 spot = lift.spawnPoint;
                if (Coop.Active)
                {
                    // in cooperativa ognuno nasce a fianco degli altri nella cabina, non sulla stessa mattonella
                    Vector3 side = Vector3.Cross(Vector3.up, new Vector3(lift.lookOut.x, 0f, lift.lookOut.z)).normalized;
                    spot += side * ((Coop.Slot - 1.5f) * 0.36f);
                }
                pl.SetSpawn(spot);
                pl.yaw = Mathf.Atan2(lift.lookOut.x, lift.lookOut.z);
                pl.pitch = 0f;
                lift.SetOpenInstant(false);
            }
            playerRoom = cur.RoomAt(pl.transform.position);
            hud?.SetWaveLabel("piano");
            hud?.SetWave(n);
            RefreshObjective();
            hud?.Banner("Piano " + n, plan.epithet);
            hud?.Toast(FloorLines[(n - 1) % FloorLines.Length]);
            if (Coop.Active) Debug.Log($"[PryingEyes] coop: firma del piano {n}: {FloorSignature():X8}");
            Coop.OnFloorBuilt();                 // cooperativa: piano costruito (la torcia dei compagni torna nel pool, l'ospite saluta chi aspettava)
            Debug.Log($"[PryingEyes] piano {n} seme {runSeed + n * 1013}: {plan.rooms.Count} stanze in {plan.units} ali, {cur.candles.Count} candele, {LightPool.Count} luci, " +
                      $"costruito in {tBuild * 1000f:F0} ms, partenza {(plan.startInChurch ? "chiesa" : "albergo")}, pianta {(plan.grid ? "a griglia" : "ad ali")}");
            if (!first) { for (float k = 1f; k > 0f; k -= Time.deltaTime / 0.9f) { hud?.SetFade(k); yield return null; } }
            hud?.SetFade(0f);
            if (lift != null) lift.Open();
            yield return new WaitForSeconds(1.2f);
            pl.invulnerable = false;
            transitioning = false;
        }

        /// <summary>Impronta del piano costruito (candele, uscita, arrivo, numero di luci): in cooperativa le macchine devono ottenere lo stesso
        /// numero. Si legge nel log, ed e' il modo di accorgersi di un piano che si costruisce diverso da una macchina all'altra.</summary>
        public int FloorSignature()
        {
            if (cur == null) return 0;
            unchecked
            {
                int h = 17;
                void Mix(Vector3 p) { h = h * 31 + Mathf.RoundToInt(p.x * 100f); h = h * 31 + Mathf.RoundToInt(p.z * 100f); }
                foreach (var c in cur.candles) if (c != null) Mix(c.transform.position);
                if (cur.exit != null) Mix(cur.exit.transform.position);
                if (cur.arrival != null) Mix(cur.arrival.spawnPoint);
                h = h * 31 + cur.candles.Count; h = h * 31 + LightPool.Count; h = h * 31 + cur.plan.rooms.Count;
                return h;
            }
        }

        /// <summary>Ogni candela e l'uscita devono essere raggiungibili a piedi dall'arrivo. Una candela finita dove non si arriva
        /// (dentro un arredo, su un'isola della navmesh) viene spostata in un punto libero e raggiungibile della sua stanza.</summary>
        void ValidateReach(Plan plan)
        {
            var lift = cur.arrival;
            if (lift == null || !NavMesh.SamplePosition(lift.frontPoint, out NavMeshHit fh, 2.5f, NavMesh.AllAreas)) return;
            var path = new NavMeshPath();
            bool Reach(Vector3 target)
            {
                if (!NavMesh.SamplePosition(target, out NavMeshHit th, 0.9f, NavMesh.AllAreas)) return false;
                return NavMesh.CalculatePath(fh.position, th.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            }
            var rng = new Rng(plan.seed + 99);
            foreach (var c in cur.candles)
            {
                if (c == null || Reach(c.transform.position)) continue;
                var room = cur.RoomAt(c.transform.position);
                bool moved = false;
                if (room != null)
                    for (int i = 0; i < 80 && !moved; i++)
                    {
                        float x = rng.Range(room.x0 * 0.1f + 1.2f, room.x1 * 0.1f - 1.2f), z = rng.Range(room.z0 * 0.1f + 1.2f, room.z1 * 0.1f - 1.2f);
                        var q = new Vector3(x, 0f, z);
                        if (Physics.CheckBox(q + Vector3.up * 1.0f, new Vector3(0.45f, 0.95f, 0.45f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (!Reach(q)) continue;
                        c.transform.position = q;
                        moved = true;
                    }
                Debug.Log($"[PryingEyes] candela non raggiungibile in {(room != null ? room.kind.ToString() : "?")}: {(moved ? "spostata" : "NON SPOSTATA")}");
            }
            if (cur.exit != null && !Reach(cur.exit.frontPoint)) Debug.LogWarning("[PryingEyes] uscita non raggiungibile dall'arrivo!");
        }

        /// <summary>Il valzer del grammofono (lo stesso della hall) esce da una camera a caso: lo senti prima di sapere dov'e'.</summary>
        IEnumerator Gramophone(Plan plan)
        {
            var rooms = plan.rooms.FindAll(q => (q.kind == RoomKind.Camera || q.kind == RoomKind.Suite) && q.id != plan.start);
            if (rooms.Count == 0) yield break;
            var rng = new Rng(plan.seed + 1234);
            var r = rooms[rng.Range(0, rooms.Count)];
            var root = cur != null ? cur.root : null;
            for (float t = 0f; t < 40f && !SoundBank.WaltzReady; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (cur == null || cur.root != root || SoundBank.Waltz == null) yield break;
            var g = new GameObject("Grammofono");
            g.transform.SetParent(root.transform, false);
            g.transform.position = new Vector3(r.CenterX, 0.9f, r.CenterZ);
            Sfx.Loop(g, SoundBank.Waltz, 0.45f, 3f, 40f, 1f, 1f, 0f, true);
        }

        public static float LightDim(int floor) => Mathf.Lerp(0.50f, 0.32f, Mathf.Clamp01((floor - 1) / 7f));

        /// <summary>Atmosfera condivisa tra il piano di gioco e le foto di taratura.</summary>
        public static void ApplyLook(Plan plan, int floor)
        {
            var rnd = new Rng(plan.seed + 77);
            bool churchy = plan.startInChurch;
            float deep = Mathf.Clamp01((floor - 1) / 7f);
            // La foschia fonde lo sfondo lasciando leggibili gli oggetti vicini.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = rnd.Range(0.027f, 0.033f) * Mathf.Lerp(1f, 1.22f, deep);
            // teatro: polvere rossastra di velluto e legno; sanatorio: aria verdastra di vernice e disinfettante
            Color fog = churchy ? new Color(0.17f, 0.19f, 0.23f) : new Color(0.19f, 0.16f, 0.17f);
            Color sky = new Color(0.13f, 0.15f, 0.21f), equator = new Color(0.095f, 0.095f, 0.11f), ground = new Color(0.05f, 0.046f, 0.05f);
            if (plan.theme == Theme.Teatro)
            {
                fog = new Color(0.21f, 0.11f, 0.11f);
                sky = new Color(0.16f, 0.11f, 0.13f); equator = new Color(0.115f, 0.082f, 0.088f); ground = new Color(0.06f, 0.04f, 0.045f);
            }
            else if (plan.theme == Theme.Sanatorio)
            {
                fog = new Color(0.12f, 0.175f, 0.155f);
                sky = new Color(0.105f, 0.17f, 0.17f); equator = new Color(0.082f, 0.11f, 0.105f); ground = new Color(0.045f, 0.052f, 0.05f);
            }
            RenderSettings.fogColor = fog;
            float amb = Mathf.Lerp(0.48f, 0.31f, deep);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * amb;
            RenderSettings.ambientEquatorColor = equator * amb;
            RenderSettings.ambientGroundColor = ground * amb;
            RenderSettings.reflectionIntensity = 0.30f;     // il cubemap della hall illuminata, al buio, farebbe brillare i metalli
            // grading piu' duro (profilo copiato in istanza: l'asset non cambia)
            var vol = FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            if (vol != null && vol.profile != null)
            {
                var prof = vol.profile;
                if (prof.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments ca)) { ca.saturation.Override(-24f); ca.contrast.Override(5f); }
                if (prof.TryGet(out UnityEngine.Rendering.Universal.Vignette vg)) { vg.intensity.Override(0.24f); vg.smoothness.Override(0.55f); }
                if (prof.TryGet(out UnityEngine.Rendering.Universal.FilmGrain fg)) fg.intensity.Override(0.30f);
            }
        }

        // ------------------------------------------------------------------ candele e uscita
        /// <summary>Quanti avvistamenti c'erano (Stealth.Spotted) all'ultima candela, o all'inizio del piano: se non e' cambiato niente
        /// e nessun prete ti sta cacciando, la candela vale il bonus Fantasma.</summary>
        int ghostMark;
        public int GhostMark => ghostMark;

        /// <summary>Un premio a chi ha acceso la candela ('slot': 0 da solo o l'ospite, 1..3 un compagno, a cui lo manda l'ospite).</summary>
        void Reward(int slot, int pts, float ammo)
        {
            if (slot == Coop.Slot)
            {
                gm.AddPoints(pts);
                if (ammo > 0f) gm.player.weapon.AddAmmoFraction(ammo);
            }
            else Coop.Game?.AwardPoints(slot, pts, ammo);
        }

        /// <summary>Cooperativa, sugli ospitati: l'ospite ha detto che una candela si e' accesa.</summary>
        Candelabro lastLit;
        public void CoopCandleLit(Candelabro c, int by, bool quiet = false)
        {
            if (c == null || c.lit) return;
            c.Setup(true);
            c.litBy = by;
            if (!quiet) Sfx.At(SoundBank.Ignite, c.transform.position + Vector3.up, 0.9f, Random.Range(0.95f, 1.05f), 2f, 28f);
            lit++;
            lastLit = c;
            if (lit < total && !quiet) gm.hud?.Toast($"Candela accesa ({lit}/{total}). Qualcosa si muove nell'albergo.");
            Guide.I?.CandleLit(c, exitOpen);
            RefreshObjective();
        }

        /// <summary>Cooperativa: quante candele sono accese (per l'ospite che saluta chi entra dopo).</summary>
        public int LitCount => lit;
        public int CandleCount => total;

        void OnCandle(Candelabro c)
        {
            lit++;
            var hud = gm.hud;
            int by = c.litBy;                                  // chi l'ha accesa: da solo, sempre il giocatore
            bool mine = by == Coop.Slot;
            Coop.Game?.CandleLit(c, by);                       // cooperativa: l'ospite lo racconta agli altri
            Reward(by, 60, 0.18f);
            bool ghost = Stealth.Spotted == ghostMark && Stealth.Hunters == 0;
            ghostMark = Stealth.Spotted;
            if (ghost) Reward(by, Punti.Fantasma, 0f);
            // una candela in una zona sigillabile fa partire un round (RoundDirector): le serrande calano e l'orda nasce dentro. Se
            // parte, il resto (uscita, richiamo) aspetta la fine del round (OnRoundEnd). Il tono, calmo o in allarme, lo misura
            // prima del rumore della candela, che altrimenti sveglierebbe tutti.
            bool round = RoundDirector.I != null && RoundDirector.I.TryStart(c);
            // accendere una candela si sente e si vede: i preti vicini vengono a controllare
            Noise.Emit(c.transform.position, 26f, NoiseKind.Candle);
            // Fantasma: grande avviso (nel round resta quello del round, e si usa la riga piccola)
            if (ghost && mine)
            {
                Sfx.UI(SoundBank.PickCoins, 0.5f, 0.85f);
                if (round) hud?.Toast($"Fantasma: nessun prete ti ha visto.  +{Punti.Fantasma} punti");
                else hud?.Banner("Fantasma", $"Nessun prete ti ha visto.  +{Punti.Fantasma} punti");
            }
            else if (ghost)
                Coop.Game?.Announce(by, (round ? 0 : 1) | 2, round ? $"Fantasma: +{Punti.Fantasma} punti" : "Fantasma", $"Nessun prete ti ha visto.  +{Punti.Fantasma} punti");
            if (round) { RefreshObjective(); return; }
            if (lit >= total)
            {
                OpenExit();
            }
            else
            {
                hud?.Toast($"Candela accesa ({lit}/{total}). Qualcosa si muove nell'albergo.");
                Summon(2 + floor / 3 + lit / 2, c.transform.position);
            }
            Guide.I?.CandleLit(c, exitOpen);
            RefreshObjective();
        }

        /// <summary>Il round e' finito: ora vale la candela (uscita se erano tutte accese).</summary>
        public void OnRoundEnd(Candelabro c)
        {
            if (lit >= total) OpenExit();
            else gm.hud?.Toast($"Candela accesa ({lit}/{total}). Le serrande si aprono.");
            Guide.I?.CandleLit(c, exitOpen);
            RefreshObjective();
        }

        /// <summary>Il round e' saltato (giocatore morto): la candela e' di nuovo spenta e non conta.</summary>
        public void OnRoundAborted(Candelabro c)
        {
            lit = Mathf.Max(0, lit - 1);
            RefreshObjective();
        }

        /// <summary>Un tipo di nemico per i round, gia' nel costume del tema della stanza.</summary>
        public Enemy.EnemyType PickRoundType(Room room) => Retheme(PickType(), room);

        /// <summary>Solo per le prove (-cooptest): apre l'uscita come se tutte le candele fossero accese.</summary>
        public void DebugOpenExit() { if (cur != null && !exitOpen) OpenExit(); }

        /// <summary>Cooperativa, sugli ospitati: l'ospite ha aperto l'uscita.</summary>
        public void CoopOpenExit()
        {
            if (exitOpen || cur == null) return;
            if (lastLit != null) Guide.I?.CandleLit(lastLit, true);
            OpenExit(false);
        }

        /// <summary>Apre l'uscita. 'announce' e' falso sugli ospitati: l'orda dell'ultima candela la richiama solo l'ospite.</summary>
        void OpenExit(bool announce = true)
        {
            exitOpen = true;
            if (announce) Coop.Game?.ExitOpened();
            if (cur.exit != null) { cur.exit.unlocked = true; cur.exit.Open(); }
            gm.hud?.Toast("Tutte le candele sono accese. Le porte dell'ascensore si aprono.");
            Sfx.UI(SoundBank.Toll, 0.7f);
            if (announce && !lastHorde && cur.exit != null) { lastHorde = true; Summon(3 + floor / 2, cur.exit.frontPoint); }
            // faro verde sulla porta dell'ascensore: nel buio si vede da lontano
            if (cur.exit != null)
            {
                var bg = new GameObject("FaroUscita");
                bg.transform.SetParent(cur.root.transform, false);
                bg.transform.position = cur.exit.frontPoint + Vector3.up * 2.2f;
                var bl = bg.AddComponent<Light>();
                bl.type = LightType.Point;
                bl.color = new Color(0.45f, 1f, 0.6f);
                bl.intensity = 2.6f;
                bl.range = 11f;
                bl.shadows = LightShadows.None;
                LightPool.Register(bl, false, true);
                var bp = bg.AddComponent<LightPulse>();
                bp.l = bl; bp.baseI = 2.6f; bp.amp = 0.35f; bp.speed = 2.6f;
            }
        }

        public void RefreshObjectiveNow() => RefreshObjective();

        void RefreshObjective()
        {
            var hud = gm.hud;
            if (hud == null) return;
            if (RoundDirector.I != null && RoundDirector.I.Fighting) { hud.SetObjective(RoundDirector.I.Status()); return; }
            if (exitOpen) { hud.SetObjective("Raggiungi l'ascensore e scendi"); return; }
            hud.SetObjective($"Accendi le candele {lit}/{total}  ·  V per il ping");
        }

        IEnumerator Descend()
        {
            transitioning = true;
            Coop.OnDescendBegin(floor + 1);      // cooperativa: l'ospite dice a tutti che si scende, e ognuno comincia la sua dissolvenza
            var pl = gm.player;
            pl.invulnerable = true;
            gm.AddPoints(100 + 50 * floor);
            pl.weapon.AddAmmoFraction(0.4f);
            gm.hud?.Toast($"Piano {floor} superato. +{100 + 50 * floor} punti, munizioni recuperate");
            Sfx.UI(SoundBank.LiftRide, 0.55f);
            if (cur.exit != null) cur.exit.Close();
            for (float k = 0f; k < 1f; k += Time.deltaTime / 1.2f) { gm.hud?.SetFade(k); yield return null; }
            gm.hud?.SetFade(1f);
            yield return new WaitForSeconds(0.9f);
            yield return GoTo(floor + 1, false);
        }

        /// <summary>Chiamato dal giocatore quando rinasce: l'ascensore di arrivo lo aspetta aperto.</summary>
        public void OnRespawn()
        {
            deaths++;
            ghostMark = Stealth.Spotted;         // dopo la morte si ricomincia a contare
            Drops.OnPlayerDied();
            RoundDirector.I?.OnPlayerDied();     // se era un round, salta (di norma l'ha gia' visto morire)
            if (cur != null && cur.arrival != null) cur.arrival.SetOpenInstant(true);
            Enemy.ForgetOf(Targets.Local);     // chi ti cercava ha perso la pista (gli altri, che cercano un compagno, no)
            StartCoroutine(Grace());
        }

        /// <summary>Due secondi e mezzo di tregua alla rinascita, per non essere ucciso di nuovo sulla porta.</summary>
        IEnumerator Grace()
        {
            var pl = gm.player;
            pl.invulnerable = true;
            yield return new WaitForSeconds(2.5f);
            if (!transitioning) pl.invulnerable = false;
        }

        // ------------------------------------------------------------------ nemici
        Enemy.EnemyType PickType()
        {
            float fly = Mathf.Min(0.38f, 0.14f + floor * 0.025f);
            float abate = floor >= 2 ? Mathf.Min(0.22f, 0.04f + floor * 0.025f) : 0f;
            float r = Random.value;
            if (r < fly)
            {
                if (r < fly * 0.38f) return Enemy.EnemyType.Falena;
                if (floor >= 2 && r < fly * 0.70f) return Enemy.EnemyType.Medusa;
                return Enemy.EnemyType.Occhio;
            }
            if (r < fly + abate) return r < fly + abate * 0.55f ? Enemy.EnemyType.Carapace : Enemy.EnemyType.Abate;
            return Enemy.EnemyType.Monaco;
        }

        /// <summary>Nei piani a tema chi cammina cambia costume: nel teatro il monaco diventa Maschera e l'abate Direttore, nel
        /// sanatorio Infermiere e Primario; la cappella del sanatorio resta dei monaci. Chi vola e gli alieni non cambiano.</summary>
        Enemy.EnemyType Retheme(Enemy.EnemyType t, Room room)
        {
            if (cur == null || (t != Enemy.EnemyType.Monaco && t != Enemy.EnemyType.Abate)) return t;
            var theme = cur.plan.theme;
            if (theme == Theme.Albergo || (room != null && room.church)) return t;
            bool heavy = t == Enemy.EnemyType.Abate;
            if (theme == Theme.Teatro) return heavy ? Enemy.EnemyType.Direttore : Enemy.EnemyType.Maschera;
            return heavy ? Enemy.EnemyType.Primario : Enemy.EnemyType.Infermiere;
        }

        /// <summary>Richiamo (il primario): arrivano altri nemici, fuori vista, verso quel punto.</summary>
        public void CallHelp(Vector3 where, int count)
        {
            if (cur != null && Coop.Sim) StartCoroutine(SummonCo(count, where));
        }

        /// <summary>Un punto di nascita fuori dalla vista del giocatore e a distanza giusta; sulla navmesh per chi cammina.</summary>
        bool PickSpawn(bool flying, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (cur == null || gm.player == null) return false;
            var list = flying ? cur.airSpawns : cur.groundSpawns;
            if (list.Count == 0) return false;
            int off = Random.Range(0, list.Count);
            for (int pass = 0; pass < 3; pass++)
            {
                float dmin = pass == 0 ? 15f : pass == 1 ? 9f : 4f, dmax = pass == 0 ? 52f : 90f;
                for (int j = 0; j < list.Count; j++)
                {
                    Vector3 p = list[(off + j) % list.Count];
                    float d = Targets.NearestDistance(p);              // lontano da tutti i giocatori (da solo: dal giocatore)
                    if (d < dmin || d > dmax) continue;
                    Vector3 q = p + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
                    if (!flying)
                    {
                        if (!NavMesh.SamplePosition(q, out NavMeshHit nh, 2.5f, NavMesh.AllAreas)) continue;
                        q = nh.position;
                    }
                    if (RoundDirector.I != null && RoundDirector.I.InsideSealed(q)) continue;     // dentro la zona sigillata nascono solo i nemici del round
                    if (pass < 2 && Targets.AnySees(q + Vector3.up * 1.2f)) continue;              // si vede: no
                    pos = q;
                    return true;
                }
            }
            return false;
        }

        void SpawnOne()
        {
            if (!Coop.Sim || NoEnemies) return;
            var t = PickType();
            if (PickSpawn(Enemy.IsFlying(t), out Vector3 p)) Enemy.Spawn(Retheme(t, cur.RoomAt(p)), p);
        }

        void Horde(int count, string why)
        {
            if (Coop.Sim) StartCoroutine(HordeCo(count, why));
        }

        IEnumerator HordeCo(int count, string why)
        {
            int cap = 16 + floor;
            for (int i = 0; i < count; i++)
            {
                if (cur == null) yield break;
                if (Enemy.All.Count < cap) SpawnOne();
                yield return new WaitForSeconds(Random.Range(0.5f, 1.3f));
            }
        }

        // ------------------------------------------------------------------ pile, pattuglie, richiami
        /// <summary>Pile per la torcia sparse per il piano: una non lontano dall'arrivo (un premio presto), le altre in stanze a caso,
        /// sempre in punti liberi, raggiungibili a piedi e distanziati tra loro. Il numero cala di una ogni tre piani.</summary>
        void PlaceBatteries(Plan plan)
        {
            var lift = cur.arrival;
            if (lift == null || !NavMesh.SamplePosition(lift.frontPoint, out NavMeshHit fh, 2.5f, NavMesh.AllAreas)) return;
            var dist = plan.WalkDist(plan.start);
            var path = new NavMeshPath();
            bool Reach(Vector3 target)
            {
                if (!NavMesh.SamplePosition(target, out NavMeshHit th, 0.7f, NavMesh.AllAreas)) return false;
                return NavMesh.CalculatePath(fh.position, th.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            }
            var rng = new Rng(plan.seed + 4242);
            int n = Mathf.Max(3, total + 2 - floor / 3);
            var rooms = new List<Room>();
            foreach (var r in plan.rooms)
                if (!r.IsLift && dist[r.id] < float.MaxValue && r.W * r.D >= 500) rooms.Add(r);
            for (int i = rooms.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); var t = rooms[i]; rooms[i] = rooms[j]; rooms[j] = t; }
            int first = rooms.FindIndex(r => dist[r.id] >= 8f && dist[r.id] <= 30f);
            if (first > 0) { var t = rooms[first]; rooms.RemoveAt(first); rooms.Insert(0, t); }
            var placed = new List<Vector3>();
            int made = 0;
            for (int pass = 0; pass < 2 && made < n; pass++)
                foreach (var room in rooms)
                {
                    if (made >= n) break;
                    for (int tries = 0; tries < 40; tries++)
                    {
                        float x = rng.Range(room.x0 * 0.1f + 0.7f, room.x1 * 0.1f - 0.7f), z = rng.Range(room.z0 * 0.1f + 0.7f, room.z1 * 0.1f - 0.7f);
                        var q = new Vector3(x, 0.02f, z);
                        bool tooNear = false;
                        foreach (var p in placed) if ((p - q).sqrMagnitude < 49f) { tooNear = true; break; }
                        if (tooNear) continue;
                        if (Physics.CheckBox(q + Vector3.up * 0.3f, new Vector3(0.22f, 0.2f, 0.22f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (!Reach(q)) continue;
                        Pila.Make(q, rng.Range(0f, 360f), cur.root.transform);
                        placed.Add(q);
                        made++;
                        break;
                    }
                }
            Debug.Log($"[PryingEyes] piano {floor}: {made}/{n} pile");
        }

        /// <summary>Dove pattugliano i preti: il centro di ogni stanza e qualche punto in piu' in quelle grandi.</summary>
        void BuildPatrolPoints(Plan plan)
        {
            var pts = Enemy.PatrolPoints;
            pts.Clear();
            var rng = new Rng(plan.seed + 321);
            foreach (var r in plan.rooms)
            {
                if (r.IsLift || r.type == RoomType.Segreta) continue;      // nella stanza segreta i nemici non entrano (si passa a carponi)
                pts.Add(new Vector3(r.CenterX, 0f, r.CenterZ));
                int extra = r.W * r.D > 3000 ? 2 : r.W * r.D > 1400 ? 1 : 0;
                for (int i = 0; i < extra; i++)
                    pts.Add(new Vector3(rng.Range(r.x0 * 0.1f + 1.5f, r.x1 * 0.1f - 1.5f), 0f, rng.Range(r.z0 * 0.1f + 1.5f, r.z1 * 0.1f - 1.5f)));
            }
        }

        /// <summary>Preti che pattugliano il piano fin dall'inizio, in stanze lontane dall'arrivo (almeno 20 m a piedi, se il piano lo permette).</summary>
        void PopulateFloor(Plan plan)
        {
            if (NoEnemies) return;
            if (plan.grid) { PopulateGrid(plan); return; }
            var dist = plan.WalkDist(plan.start);
            var rng = new Rng(plan.seed + 555);
            var rooms = new List<Room>();
            foreach (var r in plan.rooms)
                if (!r.IsLift && dist[r.id] < float.MaxValue && dist[r.id] >= 20f) rooms.Add(r);
            if (rooms.Count == 0)
                foreach (var r in plan.rooms)
                    if (!r.IsLift && dist[r.id] < float.MaxValue && dist[r.id] >= 10f) rooms.Add(r);
            if (rooms.Count == 0) return;
            int n = Mathf.Min(12, 2 + floor / 2 + (floor >= 3 ? 1 : 0) + plan.units - 1);
            for (int i = 0; i < n; i++)
            {
                var room = rooms[rng.Range(0, rooms.Count)];
                var t = Retheme(PickType(), room);
                Vector3 p = new Vector3(rng.Range(room.x0 * 0.1f + 1.2f, room.x1 * 0.1f - 1.2f), 0f, rng.Range(room.z0 * 0.1f + 1.2f, room.z1 * 0.1f - 1.2f));
                if (Enemy.IsFlying(t)) p.y = Mathf.Min(2.2f, room.wallH - 0.7f);
                else if (NavMesh.SamplePosition(p, out NavMeshHit nh, 2.5f, NavMesh.AllAreas)) p = nh.position;
                else continue;
                Enemy.Spawn(t, p);
            }
        }

        /// <summary>Quanti punti vita in piu' ha il guardiano dell'uscita rispetto a un abate qualunque.</summary>
        public static float BossHpMul(int floor) => floor <= 2 ? 2.0f : 2.5f;

        /// <summary>Pianta a griglia: ogni stanza di guardia ha le sue guardie (una, due se e' grande, qualcuna in piu' piu' si scende) che
        /// non escono dalla loro stanza finche' non ti notano; il guardiano pesante dell'uscita sta nella stanza del boss. Le altre stanze
        /// (partenza, negozio, biblioteca, arene, segreta) restano vuote: in un'arena arriva l'orda solo quando accendi la candela. Al primo
        /// piano le stanze attaccate alla partenza sono senza guardie, per un inizio tranquillo.</summary>
        void PopulateGrid(Plan plan)
        {
            var rng = new Rng(plan.seed + 555);
            var hops = plan.Hops(plan.start);
            var rooms = plan.rooms.FindAll(r => r.type == RoomType.Guardia && hops[r.id] >= 0);
            if (floor == 1)
            {
                var far = rooms.FindAll(r => hops[r.id] >= 3);
                if (far.Count > 0) rooms = far;
            }
            int budget = Mathf.Min(12, 3 + floor);
            int made = 0;
            foreach (var room in rooms)
            {
                int n = 1 + ((long)room.W * room.D >= 14000 ? 1 : 0) + (floor >= 5 && rng.Chance(0.4) ? 1 : 0);
                for (int i = 0; i < n && made < budget; i++)
                {
                    var t = Retheme(PickType(), room);
                    var e = SpawnInRoom(room, t, rng);
                    if (e == null) continue;
                    e.SetHome(new Vector2(room.x0 * 0.1f + 1.5f, room.z0 * 0.1f + 1.5f), new Vector2(room.x1 * 0.1f - 1.5f, room.z1 * 0.1f - 1.5f));
                    made++;
                }
            }
            // il guardiano dell'uscita: la versione pesante del tema, piu' robusta
            var bossRoom = plan.OfType(RoomType.Boss);
            if (bossRoom.Count > 0)
            {
                var room = bossRoom[0];
                var e = SpawnInRoom(room, Retheme(Enemy.EnemyType.Abate, room), rng);
                if (e != null)
                {
                    e.hp = e.maxHp = e.maxHp * BossHpMul(floor);
                    e.SetHome(new Vector2(room.x0 * 0.1f + 1.5f, room.z0 * 0.1f + 1.5f), new Vector2(room.x1 * 0.1f - 1.5f, room.z1 * 0.1f - 1.5f));
                    made++;
                    Debug.Log($"[PryingEyes] guardiano {e.type} nella stanza {room.kind}: {e.hp:F0} punti vita");
                }
            }
            Debug.Log($"[PryingEyes] piano {floor}: {made} guardie in {rooms.Count} stanze di guardia");
        }

        /// <summary>Un nemico in un punto libero della stanza (sulla navmesh se cammina, a un paio di metri da terra se vola).</summary>
        Enemy SpawnInRoom(Room room, Enemy.EnemyType t, Rng rng)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                Vector3 p = new Vector3(rng.Range(room.x0 * 0.1f + 1.5f, room.x1 * 0.1f - 1.5f), 0f, rng.Range(room.z0 * 0.1f + 1.5f, room.z1 * 0.1f - 1.5f));
                if (Enemy.IsFlying(t)) p.y = Mathf.Min(2.2f, room.wallH - 0.7f);
                else if (NavMesh.SamplePosition(p, out NavMeshHit nh, 1.5f, NavMesh.AllAreas)) p = nh.position;
                else continue;
                return Enemy.Spawn(t, p);
            }
            return null;
        }

        /// <summary>Richiama dei preti verso un punto (una candela appena accesa, l'ascensore): nascono fuori vista e vanno a controllare.</summary>
        void Summon(int count, Vector3 target)
        {
            if (Coop.Sim) StartCoroutine(SummonCo(count, target));
        }

        IEnumerator SummonCo(int count, Vector3 target)
        {
            if (NoEnemies) yield break;
            int cap = 8 + floor + units;
            yield return new WaitForSeconds(Random.Range(2f, 5f));
            for (int i = 0; i < count; i++)
            {
                if (cur == null) yield break;
                if (Enemy.All.Count < cap)
                {
                    var t = PickType();
                    if (PickSpawn(Enemy.IsFlying(t), out Vector3 p)) Enemy.Spawn(Retheme(t, cur.RoomAt(p)), p, target);
                }
                yield return new WaitForSeconds(Random.Range(1.5f, 4f));
            }
        }

        // ------------------------------------------------------------------ santuari (fase 3)
        /// <summary>Le stanze di Biblioteca, Scrigno e altare di questo piano (nulla se non c'era posto).</summary>
        public Room libraryRoom, chestRoom, altarRoom;

        /// <summary>Tre stanze grandi e diverse, lontane dai candelabri e dalle zone sigillabili: la Biblioteca a un po' piu' di meta' strada
        /// (il libro "da trovare"), lo Scrigno a meta', l'altare lontano. Si costruiscono prima della navmesh: i leggii e i blocchi sono
        /// ostacoli come gli altri arredi.</summary>
        void PlaceShrines(Plan plan, List<RoundRegion> regions)
        {
            libraryRoom = chestRoom = altarRoom = null;
            if (cur == null || plan.start < 0) return;
            var rng = new Rng(plan.seed + 211);
            var used = new HashSet<int>();
            libraryRoom = PlaceShrine(0, plan, regions, used, rng);
            chestRoom = PlaceShrine(1, plan, regions, used, rng);
            altarRoom = PlaceShrine(2, plan, regions, used, rng);
            Physics.SyncTransforms();
        }

        /// <summary>Un santuario: k = 0 Biblioteca, 1 Scrigno, 2 altare. Nella pianta a griglia ognuno ha la sua stanza (la Biblioteca, il
        /// Negozio, la stanza Segreta); se li' non c'e' posto ripiega su una stanza di guardia, come nelle piante di prima.</summary>
        Room PlaceShrine(int k, Plan plan, List<RoundRegion> regions, HashSet<int> used, Rng rng)
        {
            bool Put(Room r) => k == 0 ? Biblioteca.Place(cur, plan, r, rng) != null : k == 1 ? Scrigno.Place(cur, plan, r, rng) != null : Altare.Place(cur, plan, r, rng) != null;
            if (plan.grid)
                foreach (var r in plan.OfType(k == 0 ? RoomType.Biblioteca : k == 1 ? RoomType.Negozio : RoomType.Segreta))
                    if (Put(r)) { used.Add(r.id); return r; }
            return PlaceOne(plan, regions, used, k == 0 ? 0.62f : k == 1 ? 0.45f : 0.78f, k == 0 ? 3200f : k == 1 ? 2000f : 2600f, rng, Put);
        }

        static Room PlaceOne(Plan plan, List<RoundRegion> regions, HashSet<int> used, float target, float minArea, Rng rng, System.Func<Room, bool> place)
        {
            foreach (var r in ShrineKit.Candidates(plan, regions, used, target, minArea, rng))
                if (place(r)) { used.Add(r.id); return r; }
            return null;
        }

        /// <summary>Un santuario ('at' e' dove poggia, 'it' il suo punto d'uso) e' usabile se c'e' un posto sulla navmesh a 1,5-2 m, nel raggio
        /// d'uso, raggiungibile a piedi dall'ascensore di arrivo e senza muri tra gli occhi e lui. Senza ascensore o navmesh non si puo' sapere: si'.</summary>
        public bool ShrineUsable(Vector3 at, IInteract it)
        {
            var lift = cur != null ? cur.arrival : null;
            if (lift == null || !NavMesh.SamplePosition(lift.frontPoint, out NavMeshHit fh, 2.5f, NavMesh.AllAreas)) return true;
            Vector3 from = fh.position;
            // la stanza Segreta si raggiunge a carponi, dove la navmesh non passa: il percorso si controlla da dentro, davanti al buco
            var inRoom = cur.RoomAt(at);
            if (inRoom != null && inRoom.type == RoomType.Segreta && !SecretEntry(inRoom, out from)) return false;
            var path = new NavMeshPath();
            for (int a = 0; a < 12; a++)
                for (int k = 0; k < 2; k++)
                {
                    float d = k == 0 ? 1.5f : 2.0f;
                    Vector3 dir = Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward;
                    if (!NavMesh.SamplePosition(new Vector3(at.x + dir.x * d, at.y, at.z + dir.z * d), out NavMeshHit h, 0.3f, NavMesh.AllAreas)) continue;
                    Vector3 flat = new Vector3(at.x - h.position.x, 0f, at.z - h.position.z);
                    float dist = flat.magnitude;
                    if (dist > it.Radius * 0.9f) continue;
                    Vector3 eye = h.position + Vector3.up * 1.5f;
                    if (Vector3.Distance(eye, it.Point) > it.Radius * 0.95f || !Interact.Visible(eye, it.Point, (it as Component)?.transform)) continue;
                    if (NavMesh.CalculatePath(from, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) return true;
                }
            return false;
        }

        /// <summary>Il punto sulla navmesh appena dentro la stanza Segreta, davanti al suo passaggio basso (da dove si comincia a camminare
        /// dopo essersi infilati carponi).</summary>
        public bool SecretEntry(Room r, out Vector3 p)
        {
            p = default;
            if (cur == null) return false;
            foreach (var o in cur.plan.openings)
            {
                if (o.style != OpenStyle.Passaggio || (o.a != r.id && o.b != r.id)) continue;
                float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                Vector3 inward = new Vector3(r.CenterX - ox, 0f, r.CenterZ - oz);
                inward = o.alongX ? new Vector3(0f, 0f, Mathf.Sign(inward.z)) : new Vector3(Mathf.Sign(inward.x), 0f, 0f);
                if (NavMesh.SamplePosition(new Vector3(ox, 0f, oz) + inward * 1.3f, out NavMeshHit h, 2.5f, NavMesh.AllAreas)) { p = h.position; return true; }
            }
            return false;
        }

        /// <summary>Dopo la navmesh: quello che finisce in una tana tra gli arredi, o su un'isola della navmesh, dove non si arriva, si toglie e si
        /// rimette in un'altra stanza (la navmesh si rifa' in una quarantina di millisecondi). Tre giri al massimo; se ancora non va, quel santuario manca.</summary>
        void ValidateShrines(Plan plan, List<RoundRegion> regions)
        {
            if (cur == null || cur.arrival == null) return;
            var rng = new Rng(plan.seed + 212);
            var bad = new HashSet<int>();                       // stanze dove un santuario non si raggiungeva: non si riprovano
            for (int round = 0; ; round++)
            {
                bool libBad = libraryRoom != null && Biblioteca.Current != null && !Biblioteca.Current.stands.TrueForAll(s => ShrineUsable(s.transform.position, s));
                bool chestBad = chestRoom != null && Scrigno.Current != null && !ShrineUsable(Scrigno.Current.transform.position, Scrigno.Current);
                bool altarBad = altarRoom != null && Altare.Current != null && !ShrineUsable(Altare.Current.transform.position, Altare.Current);
                if (!libBad && !chestBad && !altarBad) return;
                Debug.Log($"[PryingEyes] santuari non raggiungibili (giro {round}): biblioteca={libBad} scrigno={chestBad} altare={altarBad}");
                var used = new HashSet<int>(bad);
                if (libraryRoom != null) used.Add(libraryRoom.id);
                if (chestRoom != null) used.Add(chestRoom.id);
                if (altarRoom != null) used.Add(altarRoom.id);
                if (libBad) { bad.Add(libraryRoom.id); RemoveShrine(Biblioteca.Current.root); Biblioteca.Clear(); libraryRoom = null; }
                if (chestBad) { bad.Add(chestRoom.id); RemoveShrine(Scrigno.Current.gameObject); Scrigno.Current = null; chestRoom = null; }
                if (altarBad) { bad.Add(altarRoom.id); RemoveShrine(Altare.Current.gameObject); Altare.Current = null; altarRoom = null; }
                if (round < 2)
                {
                    foreach (int id in bad) used.Add(id);
                    if (libBad) libraryRoom = PlaceShrine(0, plan, regions, used, rng);
                    if (chestBad) chestRoom = PlaceShrine(1, plan, regions, used, rng);
                    if (altarBad) altarRoom = PlaceShrine(2, plan, regions, used, rng);
                }
                Physics.SyncTransforms();
                HotelNav.Rebuild(cur.bounds, cur.propRoots);
                if (round >= 2) return;
            }
        }

        void RemoveShrine(GameObject go)
        {
            if (go == null) return;
            cur.propRoots.Remove(go.transform);
            go.SetActive(false);                                // fuori dalla navmesh e dalla fisica subito, la distruzione arriva a fine fotogramma
            Destroy(go);
        }

        /// <summary>La morte costa: meta' dei punti (a multipli di dieci) e l'ultimo perk comprato. Ritorna il testo per il banner.</summary>
        public string DeathPenalty()
        {
            var g = GameManager.I != null ? GameManager.I : gm;
            if (g == null) return null;
            int lost = g.score / 20 * 10;
            if (lost > 0) g.AddPoints(-lost);
            var parts = new List<string>();
            if (lost > 0) parts.Add(lost + " punti");
            if (Perks.LoseLast(out var p)) parts.Add("il perk " + Perks.Of(p).name);
            return parts.Count == 0 ? null : "Perdi " + string.Join(" e ", parts) + ". L'hotel ti riporta all'ingresso.";
        }

        // ------------------------------------------------------------------ armi sui muri
        /// <summary>Pianta a griglia: l'arma modesta sul muro della stanza di partenza, le altre due (quelle buone) nel Negozio, a muri e posti
        /// diversi. Se nel Negozio non c'e' un muro libero, ripiega su una stanza di guardia.</summary>
        void PlaceGridWallBuys(Plan plan)
        {
            int[] picks = PickWeapons(plan.seed);
            var rng = new Rng(plan.seed + 5);
            var taken = new List<Vector3>();
            var fallback = plan.rooms.FindAll(r => r.type == RoomType.Guardia);
            bool Put(int k, Room room)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (!TryWallSpot(plan, room, rng, out Vector3 pos, out Quaternion rot)) return false;
                    bool near = false;
                    foreach (var s in taken) if ((s - pos).sqrMagnitude < 16f) { near = true; break; }
                    if (near) continue;
                    taken.Add(pos);
                    WallBuy.Place(picks[k], WeaponDefs.All[picks[k]].price, pos, rot, WeaponDefs.All[picks[k]].GradeColor);
                    return true;
                }
                return false;
            }
            var start = plan.OfType(RoomType.Partenza);
            var shop = plan.OfType(RoomType.Negozio);
            if (start.Count == 0 || !Put(0, start[0])) foreach (var r in fallback) if (Put(0, r)) break;
            for (int k = 1; k < 3; k++)
                if (shop.Count == 0 || !Put(k, shop[0])) foreach (var r in fallback) if (Put(k, r)) break;
        }

        void PlaceWallBuys(Plan plan)
        {
            if (plan.grid) { PlaceGridWallBuys(plan); return; }
            // tre espositori: uno vicino all'arrivo, uno a meta' strada, uno lontano. Stanze scelte in base alla distanza a piedi.
            var dist = plan.WalkDist(plan.start);
            var cand = new List<Room>();
            foreach (var r in plan.rooms)
            {
                if (r.IsLift || r.W * r.D < 700) continue;
                if (dist[r.id] == float.MaxValue) continue;
                cand.Add(r);
            }
            cand.Sort((a, b) => dist[a.id].CompareTo(dist[b.id]));
            if (cand.Count == 0) return;
            int[] pick = { Mathf.Min(1, cand.Count - 1), cand.Count / 2, Mathf.Max(0, cand.Count - 1 - cand.Count / 6) };
            int[] picks = PickWeapons(plan.seed);
            var used = new HashSet<Room>();
            var rng = new Rng(plan.seed + 5);
            for (int k = 0; k < 3; k++)
            {
                Room room = cand[Mathf.Clamp(pick[k], 0, cand.Count - 1)];
                int guard = 0;
                while (used.Contains(room) && guard++ < cand.Count) room = cand[(cand.IndexOf(room) + 1) % cand.Count];
                used.Add(room);
                if (TryWallSpot(plan, room, rng, out Vector3 pos, out Quaternion rot))
                    WallBuy.Place(picks[k], WeaponDefs.All[picks[k]].price, pos, rot, WeaponDefs.All[picks[k]].GradeColor);
            }
        }

        /// <summary>Tre armi per piano, per grado: piu' si scende, piu' potenti. Vicino all'arrivo la piu' modesta, lontano la migliore.
        /// Mai la pistola di partenza, mai due armi della stessa classe se si puo' evitare.</summary>
        int[] PickWeapons(int seed)
        {
            var r = new System.Random(seed * 31 + floor * 7 + 5);
            int gMax = Mathf.Min(5, 2 + floor / 2);                    // grado "normale" del piano: 2, 3, 3, 4, 4, 5...
            int[][] bands =
            {
                new[] { 1, Mathf.Min(2, gMax) },                          // vicino: modesta
                new[] { Mathf.Max(1, gMax - 1), gMax },                   // a meta': del grado del piano
                new[] { gMax, Mathf.Min(5, gMax + 1) },                   // lontano: un grado sopra, da meritarsi
            };
            int start = WeaponDefs.IndexOf("p9_pieta");
            var picks = new int[3];
            var classes = new HashSet<string>();
            for (int k = 0; k < 3; k++)
            {
                var pool = new List<int>();
                for (int i = 0; i < WeaponDefs.All.Length; i++)
                {
                    var d = WeaponDefs.All[i];
                    if (i == start || d.grade < bands[k][0] || d.grade > bands[k][1]) continue;
                    bool dup = false;
                    for (int j = 0; j < k; j++) if (picks[j] == i) dup = true;
                    if (dup || (classes.Contains(d.cls) && r.Next(3) != 0)) continue;
                    pool.Add(i);
                }
                if (pool.Count == 0) for (int i = 0; i < WeaponDefs.All.Length; i++) { bool dup2 = false; for (int j = 0; j < k; j++) if (picks[j] == i) dup2 = true; if (i != start && !dup2 && WeaponDefs.All[i].grade <= gMax + 1) pool.Add(i); }
                picks[k] = pool[r.Next(pool.Count)];
                classes.Add(WeaponDefs.All[picks[k]].cls);
            }
            return picks;
        }

        bool TryWallSpot(Plan plan, Room r, Rng rng, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            float T = FloorBuilder.TH;
            var sides = new List<Side> { Side.S, Side.N, Side.W, Side.E };
            for (int i = sides.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); var t = sides[i]; sides[i] = sides[j]; sides[j] = t; }
            foreach (var s in sides)
            {
                bool ax = Plan.AlongX(s);
                float lo = (ax ? r.x0 : r.z0) * 0.1f + T + 1.2f, hi = (ax ? r.x1 : r.z1) * 0.1f - T - 1.2f;
                if (hi - lo < 0.5f) continue;
                Vector3 inward = s == Side.S ? Vector3.forward : s == Side.N ? Vector3.back : s == Side.W ? Vector3.right : Vector3.left;
                float face = s == Side.S ? r.z0 * 0.1f + T : s == Side.N ? r.z1 * 0.1f - T : s == Side.W ? r.x0 * 0.1f + T : r.x1 * 0.1f - T;
                var ops = plan.OpeningsOn(r, s);
                for (int tries = 0; tries < 30; tries++)
                {
                    float c = rng.Range(lo, hi);
                    bool blocked = false;
                    foreach (var o in ops)
                    {
                        if (o.sill > 0.5f && o.top < 6.5f && !o.oneSided && o.b >= 0) { }
                        if (Mathf.Abs(c - o.center * 0.1f) < o.width * 0.05f + 1.05f) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    Vector3 wp = ax ? new Vector3(c, 1.55f, face) : new Vector3(face, 1.55f, c);
                    Vector3 mid = wp + inward * 0.55f;
                    if (Physics.CheckBox(mid, new Vector3(0.7f, 0.55f, 0.5f), Quaternion.LookRotation(inward), ~0, QueryTriggerInteraction.Ignore)) continue;
                    pos = wp + inward * 0.03f;
                    rot = Quaternion.LookRotation(inward);
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ rete di sicurezza contro le cadute
        Vector3 lastSafe;
        float safeT;
        bool haveSafe;
        /// <summary>Quante volte la rete di sicurezza e' dovuta intervenire (le prove controllano che resti ferma).</summary>
        public static int statFalls;

        /// <summary>Se per qualunque motivo il giocatore esce dal pavimento (una volta, in una prova col bot, e' caduto nel vuoto in un
        /// campanile senza che il pavimento avesse buchi: causa non trovata) lo riporta all'ultimo punto d'appoggio invece di lasciarlo
        /// cadere per sempre. Lascia una riga CADUTA nel log del gioco.</summary>
        void FallGuard(float dt)
        {
            var pl = gm.player;
            Vector3 p = pl.transform.position;
            safeT -= dt;
            if (safeT <= 0f && p.y > -0.3f && p.y < 12f && pl.GetComponent<CharacterController>().isGrounded && cur.RoomAt(p) != null)
            {
                lastSafe = p; haveSafe = true; safeT = 0.5f;
            }
            if (p.y > -20f) return;
            Vector3 back = haveSafe ? lastSafe + Vector3.up * 0.1f : cur.arrival != null ? cur.arrival.spawnPoint : Vector3.up;
            statFalls++;
            Debug.LogWarning($"[PryingEyes] CADUTA: giocatore a y={p.y:F0} in ({p.x:F1},{p.z:F1}), lo riporto a ({back.x:F1},{back.z:F1})");
            pl.Teleport(back);
        }

        // ------------------------------------------------------------------ ogni fotogramma
        void Update()
        {
            if (cur == null || gm == null || gm.player == null) return;
            float dt = Time.deltaTime;
            if (!GameMenu.Blocked && !transitioning && gm.player.alive)
            {
                if (Input.GetKeyDown(KeyCode.M) || TouchControls.Down(TouchControls.Action.Map)) mapOpen = !mapOpen;
                if (Input.GetKeyDown(KeyCode.N)) miniMap = !miniMap;
                var room = cur.RoomAt(gm.player.transform.position);
                if (room != null) explored.Add(room.id);
                mapScan -= dt;
                if (mapScan <= 0f)
                {
                    mapScan = 0.5f;
                    Vector3 here = gm.player.transform.position;
                    foreach (var w in FindObjectsOfType<WallBuy>())
                        if ((w.transform.position - here).sqrMagnitude < 64f && cur.RoomAt(w.transform.position)?.id == room?.id) foundWeapons.Add(w);
                    foreach (var c in cur.candles)
                        if (c != null && (c.transform.position - here).sqrMagnitude < 64f && cur.RoomAt(c.transform.position)?.id == room?.id) foundCandles.Add(c);
                    if (cur.exit != null && (cur.exit.transform.position - here).sqrMagnitude < 64f) foundExit = true;
                    for (int k = 0; k < 3; k++)
                    {
                        var sp = ShrinePos(k);
                        if (sp.HasValue && (sp.Value - here).sqrMagnitude < 100f && cur.RoomAt(sp.Value)?.id == room?.id) foundShrines.Add(k);
                    }
                }
            }
            if (!transitioning && gm.player.alive)
            {
                floorTime += dt;
                FallGuard(dt);
                // il piano si ripopola piano piano: ogni tanto un prete nuovo, che pattuglia
                trickleT -= dt;
                if (trickleT <= 0f)
                {
                    trickleT = Mathf.Lerp(40f, 22f, Mathf.Clamp01(floor / 10f)) * Random.Range(0.8f, 1.25f);
                    bool sealedNow = RoundDirector.I != null && RoundDirector.I.Sealed;     // durante un round il piano non si ripopola
                    if (floorTime > 25f && !perfStarted && !sealedNow && Enemy.All.Count < 3 + floor + units) SpawnOne();
                }
            }
            hudT -= dt;
            if (hudT <= 0f)
            {
                hudT = 0.5f;
                gm.hud?.SetEnemiesLeft(Enemy.All.Count);
                if (!exitOpen) RefreshObjective();
                playerRoom = cur.RoomAt(gm.player.transform.position);
            }
            if (GameManager.autoTest && HasArg("-procbot")) { if (HasArg("-botstrict")) BotStrictTick(dt); else BotTick(dt); }
            if (GameManager.autoTest && HasArg("-procshots")) ShotTick();
            if (GameManager.autoTest && !perfStarted && !transitioning && floorTime > 2f && HasArg("-perftest"))
            {
                perfStarted = true;
                StartCoroutine(PerfProbe.Run(this));
            }
        }

        /// <summary>Solo per le prove: apre la mappa grande o la minimappa (e chiude l'altra).</summary>
        public void DebugShowMap(bool full, bool mini)
        {
            mapOpen = full; miniMap = mini;
        }

        /// <summary>Solo per le prove: segna una stanza come visitata (la mappa mostra solo quello che hai visto).</summary>
        public void DebugExplore(int roomId) => explored.Add(roomId);

        /// <summary>Solo per le prove: le stanze visitate diventano esattamente queste.</summary>
        public void DebugSetExplored(IEnumerable<int> ids)
        {
            explored.Clear();
            foreach (int id in ids) explored.Add(id);
        }

        void OnGUI()
        {
            if (cur == null || gm == null || gm.player == null || GameMenu.Blocked || transitioning) return;
            if (Event.current.type != EventType.Repaint) return;      // la mappa si disegna e basta: niente liste e stringhe a ogni evento di layout/mouse
            if (miniMap)
            {
                float scale = gm.hud != null ? gm.hud.GetComponent<Canvas>().scaleFactor : 1f;
                DrawMap(new Rect(Screen.width - 220 * scale, 18 * scale, 202 * scale, 202 * scale), false);
            }
            if (mapOpen) DrawMap(new Rect(Screen.width * 0.12f, Screen.height * 0.1f, Screen.width * 0.76f, Screen.height * 0.8f), true);
        }

        void DrawMap(Rect area, bool full)
        {
            var plan = cur.plan;
            Vector3 player = gm.player.transform.position;
            float span = full ? Mathf.Max((plan.maxX - plan.minX) * 0.1f, (plan.maxZ - plan.minZ) * 0.1f) + 8f : 34f;
            float scale = Mathf.Min(area.width, area.height - (full ? 45f : 0f)) / Mathf.Max(span, 1f);
            float cx = full ? (plan.minX + plan.maxX) * 0.05f : player.x;
            float cz = full ? (plan.minZ + plan.maxZ) * 0.05f : player.z;
            GUI.BeginGroup(area);
            GUI.color = new Color(0.025f, 0.035f, 0.045f, 0.92f); GUI.DrawTexture(new Rect(0, 0, area.width, area.height), Texture2D.whiteTexture);
            float px(float x) => area.width * 0.5f + (x - cx) * scale;
            float py(float z) => (area.height - (full ? 35f : 0f)) * 0.5f - (z - cz) * scale;
            if (plan.grid) DrawGridRooms(plan, full, px, py, scale);
            else
            foreach (var r in plan.rooms)
            {
                if (!explored.Contains(r.id)) continue;
                GUI.color = r.church ? new Color(0.36f, 0.40f, 0.43f) : new Color(0.40f, 0.36f, 0.32f);
                GUI.DrawTexture(new Rect(px(r.x0 * 0.1f), py(r.z1 * 0.1f), (r.x1-r.x0)*0.1f*scale, (r.z1-r.z0)*0.1f*scale), Texture2D.whiteTexture);
            }
            void Mark(Vector3 p, Color color, string label)
            {
                float x = px(p.x), y = py(p.z);
                GUI.color = color; GUI.DrawTexture(new Rect(x-3, y-3, 6, 6), Texture2D.whiteTexture);
                if (full) { GUI.color = Color.white; GUI.Label(new Rect(x+5, y-10, 100, 20), label); }
            }
            foreach (var w in foundWeapons) if (w != null) Mark(w.transform.position, Color.cyan, full ? w.displayName : "");
            foreach (var c in foundCandles) if (c != null) Mark(c.transform.position, Color.yellow, c.lit ? "Candela accesa" : "Candela");
            if (foundExit && cur.exit != null) Mark(cur.exit.transform.position, Color.green, "Uscita");
            foreach (int k in foundShrines)
            {
                var sp = ShrinePos(k);
                if (sp.HasValue) Mark(sp.Value, k == 0 ? new Color(0.72f, 0.48f, 1f) : k == 1 ? new Color(1f, 0.58f, 0.16f) : new Color(1f, 0.86f, 0.35f), k == 0 ? "Biblioteca" : k == 1 ? "Scrigno" : "Altare");
            }
            Mark(player, Color.red, "Tu");
            GUI.color = Color.white;
            if (full) GUI.Label(new Rect(12, area.height - 42, area.width - 24, 40), "M: chiudi   N: minimappa   • rosso: tu   • azzurro: armi   • giallo: candele   • verde: uscita\n• viola: biblioteca dei perk   • arancio: scrigno delle armi   • oro: altare della consacrazione" +
                (plan.grid ? "\nStanze: fuoco arena (candela) · oro negozio · viola biblioteca · cremisi guardiano · turchese segreta · acciaio guardie" : ""));
            GUI.EndGroup();
        }

        static GUIStyle mapLabel;

        /// <summary>Un elemento della mappa a griglia: un riquadro pieno, un bordo (di spessore t) o un'etichetta, gia' in coordinate schermo.</summary>
        public struct MapItem
        {
            public enum Kind { Fill, Box, Label }
            public Kind kind;
            public Rect r;
            public Color c;
            public float t;
            public string text;
        }

        /// <summary>Pianta a griglia: le stanze sono i nodi della mappa, tinte col loro ruolo (FloorBuilder.FrameColor, lo stesso colore degli
        /// stipiti delle porte); le porte sono tacche sul muro in comune. Si vede quello che hai visitato e, come in Isaac, le stanze oltre le
        /// porte aperte delle stanze visitate, come "?"; la stanza segreta solo dopo che ci sei stato. Qui si decide cosa disegnare e dove (e'
        /// la parte che le prove guardano); DrawGridRooms lo disegna con IMGUI.</summary>
        public List<MapItem> GridMapItems(Plan plan, bool full, System.Func<float, float> px, System.Func<float, float> py, float scale)
        {
            var items = new List<MapItem>();
            var known = new HashSet<int>();
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                if (explored.Contains(o.a)) known.Add(o.b);
                if (explored.Contains(o.b)) known.Add(o.a);
            }
            Color Tint(Room r) => r.type == RoomType.Normale ? new Color(0.62f, 0.62f, 0.62f) : FloorBuilder.FrameColor(r.type);
            foreach (var r in plan.rooms)
            {
                if (r.IsLift) continue;
                bool seen = explored.Contains(r.id);
                if (!seen && (!known.Contains(r.id) || r.type == RoomType.Segreta)) continue;
                var q = new Rect(px(r.x0 * 0.1f) + 1.5f, py(r.z1 * 0.1f) + 1.5f, (r.x1 - r.x0) * 0.1f * scale - 3f, (r.z1 - r.z0) * 0.1f * scale - 3f);
                if (seen)
                {
                    Color t = Tint(r);
                    items.Add(new MapItem { kind = MapItem.Kind.Fill, r = q, c = new Color(t.r * 0.26f + 0.07f, t.g * 0.26f + 0.07f, t.b * 0.26f + 0.07f, 1f) });
                    items.Add(new MapItem { kind = MapItem.Kind.Box, r = q, c = new Color(t.r, t.g, t.b, 0.9f), t = 2f });
                    if (full)
                        items.Add(new MapItem { kind = MapItem.Kind.Label, r = q, c = new Color(1f, 1f, 1f, 0.75f),
                            text = r.type == RoomType.Normale ? r.kind.ToString() : r.type == RoomType.Candela ? "Arena" : r.type == RoomType.Boss ? "Guardiano" : r.type.ToString() });
                }
                else
                {
                    items.Add(new MapItem { kind = MapItem.Kind.Fill, r = q, c = new Color(0.11f, 0.12f, 0.14f, 1f) });
                    items.Add(new MapItem { kind = MapItem.Kind.Box, r = q, c = new Color(0.30f, 0.33f, 0.38f, 1f), t = 1.5f });
                    items.Add(new MapItem { kind = MapItem.Kind.Label, r = q, c = new Color(1f, 1f, 1f, 0.55f), text = "?" });
                }
            }
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore) continue;
                var ra = plan.rooms[o.a]; var rb = plan.rooms[o.b];
                bool secret = o.style == OpenStyle.Passaggio;
                if (secret ? !(explored.Contains(o.a) && explored.Contains(o.b)) : !(explored.Contains(o.a) || explored.Contains(o.b))) continue;
                Color ca = Tint(ra), cb = Tint(rb);
                bool aSpecial = ra.type != RoomType.Guardia && ra.type != RoomType.Normale, bSpecial = rb.type != RoomType.Guardia && rb.type != RoomType.Normale;
                Color c = bSpecial && !aSpecial ? cb : ca;
                float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                float w = Mathf.Max(5f, o.width * 0.1f * scale * (secret ? 0.7f : 0.85f));
                var col = secret ? new Color(0.30f, 0.95f, 0.85f) : new Color(c.r, c.g, c.b, 1f);
                var rect = o.alongX ? new Rect(px(ox) - w * 0.5f, py(oz) - 2.5f, w, 5f) : new Rect(px(ox) - 2.5f, py(oz) - w * 0.5f, 5f, w);
                items.Add(new MapItem { kind = MapItem.Kind.Fill, r = rect, c = col });
            }
            return items;
        }

        void DrawGridRooms(Plan plan, bool full, System.Func<float, float> px, System.Func<float, float> py, float scale)
        {
            if (mapLabel == null) mapLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            foreach (var it in GridMapItems(plan, full, px, py, scale))
            {
                GUI.color = it.c;
                switch (it.kind)
                {
                    case MapItem.Kind.Fill:
                        GUI.DrawTexture(it.r, Texture2D.whiteTexture);
                        break;
                    case MapItem.Kind.Box:
                        GUI.DrawTexture(new Rect(it.r.x, it.r.y, it.r.width, it.t), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(it.r.x, it.r.yMax - it.t, it.r.width, it.t), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(it.r.x, it.r.y, it.t, it.r.height), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(it.r.xMax - it.t, it.r.y, it.t, it.r.height), Texture2D.whiteTexture);
                        break;
                    default:
                        GUI.Label(it.r, it.text, mapLabel);
                        break;
                }
            }
        }

        /// <summary>Dove sta il santuario k (0 Biblioteca, 1 Scrigno, 2 altare), se c'e' in questo piano.</summary>
        static Vector3? ShrinePos(int k)
        {
            if (k == 0) return Biblioteca.Current != null && Biblioteca.Current.stands.Count > 0 && Biblioteca.Current.root != null ? Biblioteca.Current.root.transform.position : (Vector3?)null;
            if (k == 1) return Scrigno.Current != null ? Scrigno.Current.transform.position : (Vector3?)null;
            return Altare.Current != null ? Altare.Current.transform.position : (Vector3?)null;
        }

        // ---- foto dal punto di vista del giocatore (con HUD), una per tipo di stanza in cui entra: -autotest -procshots (senza -nographics)
        readonly HashSet<string> shotKinds = new HashSet<string>();
        int shotCount;
        float shotCool;

        void ShotTick()
        {
            shotCool -= Time.deltaTime;
            if (transitioning || shotCool > 0f || shotCount >= 16 || playerRoom == null || floorTime < 2.0f) return;
            string key = floor + "_" + playerRoom.kind;
            if (shotKinds.Contains(key)) return;
            shotKinds.Add(key);
            shotCount++;
            shotCool = 1.2f;
            GameManager.CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/gioco_p" + floor + "_" + playerRoom.kind + ".png", null, true);
        }

        public Room PlayerRoom => playerRoom;

        // ------------------------------------------------------------------ bot di prova (-autotest -procbot)
        NavMeshPath botPath;
        float botRepath, botStuckT, botLogT, botT;
        Vector3 botLastPos, botLogPos;
        int botFrames;

        void BotTick(float dt)
        {
            botT += dt;
            botFrames++;
            var pl = gm.player;
            Vector3 prevDir = PlayerController.botDir;
            PlayerController.botDir = Vector3.zero;
            int limit = ArgInt("-procsecs", 300);
            botLogT -= dt;
            if (botLogT <= 0f)
            {
                botLogT = 5f;
                var p = pl.transform.position;
                float vmed = (p - botLogPos).magnitude / 5f; botLogPos = p;
                System.IO.File.AppendAllText(BotLog, $"fps={botFrames / 5f:F0} v={vmed:F2} dir=({prevDir.x:F1},{prevDir.z:F1}) cc={pl.GetComponent<CharacterController>().velocity.magnitude:F2} ");
                botFrames = 0;
                System.IO.File.AppendAllText(BotLog, $"t={botT:F0} piano={floor} candele={lit}/{total} uscita={(exitOpen ? "aperta" : "chiusa")} hp={pl.hp:F0} nemici={Enemy.All.Count} pos=({p.x:F0},{p.z:F0}) y={p.y:F1} " +
                    $"stanza={(playerRoom != null ? playerRoom.kind.ToString() : "-")} morti={deaths} kill={gm.kills} punti={gm.score}\n");
            }
            if (botT > limit) { System.IO.File.AppendAllText(BotLog, $"fine: piano {floor}, morti {deaths}, kill {gm.kills}\n"); Application.Quit(); return; }
            if (transitioning || !pl.alive) return;

            Vector3 pp = pl.transform.position;
            Vector3 target;
            Candelabro tc = null;
            if (!exitOpen)
            {
                float bd = float.MaxValue;
                foreach (var c in cur.candles) if (c != null && !c.lit) { float d = (c.transform.position - pp).sqrMagnitude; if (d < bd) { bd = d; tc = c; } }
                if (tc == null) return;
                target = tc.transform.position;
            }
            else target = cur.exit.frontPoint;

            if (new Vector2(target.x - pp.x, target.z - pp.z).magnitude < 2.0f)
            {
                if (tc != null) tc.Use(); else if (!transitioning) StartCoroutine(Descend());
                return;
            }
            botRepath -= dt;
            if (botPath == null) botPath = new NavMeshPath();
            if (botRepath <= 0f)
            {
                botRepath = 0.4f;
                if (NavMesh.SamplePosition(target, out NavMeshHit th, 3f, NavMesh.AllAreas) && NavMesh.SamplePosition(pp, out NavMeshHit fh, 2f, NavMesh.AllAreas))
                    NavMesh.CalculatePath(fh.position, th.position, NavMesh.AllAreas, botPath);
            }
            Vector3 next = target;
            if (botPath.corners != null && botPath.corners.Length > 1)
            {
                next = botPath.corners[1];
                if (new Vector2(next.x - pp.x, next.z - pp.z).magnitude < 0.5f && botPath.corners.Length > 2) next = botPath.corners[2];
            }
            Vector3 dir = new Vector3(next.x - pp.x, 0f, next.z - pp.z);
            if (dir.sqrMagnitude > 0.01f) PlayerController.botDir = dir.normalized;

            // incastrato? dopo 5 s fermo, rimette il giocatore sulla navmesh piu' vicina al bersaglio
            botStuckT += dt;
            if (botStuckT > 5f)
            {
                botStuckT = 0f;
                if ((pp - botLastPos).magnitude < 0.5f)
                {
                    System.IO.File.AppendAllText(BotLog, $"INCASTRATO a ({pp.x:F1},{pp.z:F1}) verso ({target.x:F1},{target.z:F1}); teletrasporto\n");
                    if (NavMesh.SamplePosition(Vector3.Lerp(pp, target, 0.15f), out NavMeshHit nh, 4f, NavMesh.AllAreas)) pl.Teleport(nh.position);
                }
                botLastPos = pp;
            }
        }
    }
}
