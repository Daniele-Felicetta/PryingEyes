using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Regista della partita a piani: genera la pianta, la costruisce, mette il giocatore nell'ascensore di arrivo,
    /// piazza le armi sui muri e i candelabri da accendere, manda le orde (a ogni candela e a intervalli) e fa scendere al
    /// piano dopo quando le candele sono tutte accese. Sostituisce le ondate della hall.</summary>
    public class FloorDirector : MonoBehaviour
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

        /// <summary>Avvia la partita: primo piano. Lo chiama il GameManager su "Gioca".</summary>
        public void StartRun(int seed = 0)
        {
            gm = GameManager.I;
            StealthRig.Install();      // torcia, ping, freccia, cuore (Scripts/Stealth)
            runSeed = seed != 0 ? seed : ArgInt("-seed", Random.Range(1, 1 << 28));
            floor = 0;
            deaths = 0;
            RoundDirector.ResetRun();
            Drops.ResetRun();
            StartCoroutine(GoTo(ArgInt("-floor", 1), true));
        }

        // ------------------------------------------------------------------ cambio piano
        IEnumerator GoTo(int n, bool first)
        {
            transitioning = true;
            gm = GameManager.I;
            var pl = gm.player;
            pl.invulnerable = true;
            var hud = gm.hud;
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
            explored.Clear(); foundWeapons.Clear(); foundCandles.Clear(); foundExit = false; mapOpen = false;
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
            HotelNav.Rebuild(cur.bounds, cur.propRoots);
            int stripped = Connectivity.Repair(cur, out int problems);
            if (problems > 0) Debug.Log($"[PryingEyes] connettivita': {problems} problemi, {stripped} stanze svuotate");
            ApplyLook(plan, floor);
            ValidateReach(plan);

            // obiettivo: candele da accendere; uscita sigillata finche' non lo sono tutte
            lit = 0; total = cur.candles.Count; exitOpen = false; firstHorde = false; lastHorde = false;
            Guide.I?.ClearMarks();
            Stealth.Reset();
            ghostMark = Stealth.Spotted;
            PlaceBatteries(plan);
            BuildPatrolPoints(plan);
            PopulateFloor(plan);
            foreach (var c in cur.candles) c.onLit += OnCandle;
            RoundDirector.I?.Arm(cur, regions);
            if (cur.exit != null)
            {
                cur.exit.unlocked = false;
                cur.exit.lockedText = () => $"L'ASCENSORE È SIGILLATO — CANDELE {lit}/{total}";
                cur.exit.onEnter = () => { if (!transitioning) StartCoroutine(Descend()); };
            }
            PlaceWallBuys(plan);
            StartCoroutine(Gramophone(plan));
            floorTime = 0f; trickleT = 40f; hudT = 0f;

            // giocatore nella cabina di arrivo
            var lift = cur.arrival;
            if (lift != null)
            {
                pl.SetSpawn(lift.spawnPoint);
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
            Debug.Log($"[PryingEyes] piano {n} seme {runSeed + n * 1013}: {plan.rooms.Count} stanze in {plan.units} ali, {cur.candles.Count} candele, {LightPool.Count} luci, " +
                      $"costruito in {tBuild * 1000f:F0} ms, partenza {(plan.startInChurch ? "chiesa" : "albergo")}");
            if (!first) { for (float k = 1f; k > 0f; k -= Time.deltaTime / 0.9f) { hud?.SetFade(k); yield return null; } }
            hud?.SetFade(0f);
            if (lift != null) lift.Open();
            yield return new WaitForSeconds(1.2f);
            pl.invulnerable = false;
            transitioning = false;
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

        void OnCandle(Candelabro c)
        {
            lit++;
            var hud = gm.hud;
            gm.AddPoints(60);
            gm.player.weapon.AddAmmoFraction(0.18f);
            bool ghost = Stealth.Spotted == ghostMark && Stealth.Hunters == 0;
            ghostMark = Stealth.Spotted;
            if (ghost) gm.AddPoints(Punti.Fantasma);
            // una candela in una zona sigillabile fa partire un round (RoundDirector): le serrande calano e l'orda nasce dentro. Se
            // parte, il resto (uscita, richiamo) aspetta la fine del round (OnRoundEnd). Il tono, calmo o in allarme, lo misura
            // prima del rumore della candela, che altrimenti sveglierebbe tutti.
            bool round = RoundDirector.I != null && RoundDirector.I.TryStart(c);
            // accendere una candela si sente e si vede: i preti vicini vengono a controllare
            Noise.Emit(c.transform.position, 26f, NoiseKind.Candle);
            // Fantasma: grande avviso (nel round resta quello del round, e si usa la riga piccola)
            if (ghost)
            {
                Sfx.UI(SoundBank.PickCoins, 0.5f, 0.85f);
                if (round) hud?.Toast($"Fantasma: nessun prete ti ha visto.  +{Punti.Fantasma} punti");
                else hud?.Banner("Fantasma", $"Nessun prete ti ha visto.  +{Punti.Fantasma} punti");
            }
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

        void OpenExit()
        {
            exitOpen = true;
            if (cur.exit != null) { cur.exit.unlocked = true; cur.exit.Open(); }
            gm.hud?.Toast("Tutte le candele sono accese. Le porte dell'ascensore si aprono.");
            Sfx.UI(SoundBank.Toll, 0.7f);
            if (!lastHorde && cur.exit != null) { lastHorde = true; Summon(3 + floor / 2, cur.exit.frontPoint); }
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
            Enemy.ForgetAll();                 // chi ti cercava ha perso la pista
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
            if (cur != null) StartCoroutine(SummonCo(count, where));
        }

        /// <summary>Un punto di nascita fuori dalla vista del giocatore e a distanza giusta; sulla navmesh per chi cammina.</summary>
        bool PickSpawn(bool flying, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (cur == null || gm.player == null) return false;
            var list = flying ? cur.airSpawns : cur.groundSpawns;
            if (list.Count == 0) return false;
            Vector3 pp = gm.player.transform.position, eye = pp + Vector3.up * 1.6f;
            int off = Random.Range(0, list.Count);
            for (int pass = 0; pass < 3; pass++)
            {
                float dmin = pass == 0 ? 15f : pass == 1 ? 9f : 4f, dmax = pass == 0 ? 52f : 90f;
                for (int j = 0; j < list.Count; j++)
                {
                    Vector3 p = list[(off + j) % list.Count];
                    float d = new Vector2(p.x - pp.x, p.z - pp.z).magnitude;
                    if (d < dmin || d > dmax) continue;
                    Vector3 q = p + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
                    if (!flying)
                    {
                        if (!NavMesh.SamplePosition(q, out NavMeshHit nh, 2.5f, NavMesh.AllAreas)) continue;
                        q = nh.position;
                    }
                    if (RoundDirector.I != null && RoundDirector.I.InsideSealed(q)) continue;     // dentro la zona sigillata nascono solo i nemici del round
                    if (pass < 2 && HotelNav.ClearLine(eye, q + Vector3.up * 1.2f)) continue;     // si vede: no
                    pos = q;
                    return true;
                }
            }
            return false;
        }

        void SpawnOne()
        {
            var t = PickType();
            if (PickSpawn(Enemy.IsFlying(t), out Vector3 p)) Enemy.Spawn(Retheme(t, cur.RoomAt(p)), p);
        }

        void Horde(int count, string why)
        {
            StartCoroutine(HordeCo(count, why));
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
                if (r.IsLift) continue;
                pts.Add(new Vector3(r.CenterX, 0f, r.CenterZ));
                int extra = r.W * r.D > 3000 ? 2 : r.W * r.D > 1400 ? 1 : 0;
                for (int i = 0; i < extra; i++)
                    pts.Add(new Vector3(rng.Range(r.x0 * 0.1f + 1.5f, r.x1 * 0.1f - 1.5f), 0f, rng.Range(r.z0 * 0.1f + 1.5f, r.z1 * 0.1f - 1.5f)));
            }
        }

        /// <summary>Preti che pattugliano il piano fin dall'inizio, in stanze lontane dall'arrivo (almeno 20 m a piedi, se il piano lo permette).</summary>
        void PopulateFloor(Plan plan)
        {
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

        /// <summary>Richiama dei preti verso un punto (una candela appena accesa, l'ascensore): nascono fuori vista e vanno a controllare.</summary>
        void Summon(int count, Vector3 target)
        {
            StartCoroutine(SummonCo(count, target));
        }

        IEnumerator SummonCo(int count, Vector3 target)
        {
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

        // ------------------------------------------------------------------ armi sui muri
        void PlaceWallBuys(Plan plan)
        {
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
                if (Input.GetKeyDown(KeyCode.M)) mapOpen = !mapOpen;
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
            if (GameManager.autoTest && HasArg("-procbot")) BotTick(dt);
            if (GameManager.autoTest && HasArg("-procshots")) ShotTick();
            if (GameManager.autoTest && !perfStarted && !transitioning && floorTime > 2f && HasArg("-perftest"))
            {
                perfStarted = true;
                StartCoroutine(PerfProbe.Run(this));
            }
        }

        void OnGUI()
        {
            if (cur == null || gm == null || gm.player == null || GameMenu.Blocked || transitioning) return;
            if (miniMap) DrawMap(new Rect(Screen.width - 220, 18, 202, 202), false);
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
            Mark(player, Color.red, "Tu");
            GUI.color = Color.white;
            if (full) GUI.Label(new Rect(12, area.height - 30, area.width - 24, 25), "M: chiudi   N: minimappa   • rosso: tu   • azzurro: armi   • giallo: candele   • verde: uscita");
            GUI.EndGroup();
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
