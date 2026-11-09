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

        int lit, total, deaths;
        bool exitOpen, firstHorde, lastHorde;
        float trickleT, floorTime, hudT, probeT;
        Room playerRoom;
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

        void Awake() { I = this; }

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
            runSeed = seed != 0 ? seed : ArgInt("-seed", Random.Range(1, 1 << 28));
            floor = 0;
            deaths = 0;
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
                Destroy(cur.root);
                cur = null;
                Enemy.ClearAll();
                WallBuy.ClearAll();
                Interact.Clear();
                LightPool.Clear();
                yield return null;
            }
            floor = n;
            var plan = PlanGen.Generate(runSeed + n * 1013, n);
            if (plan == null) { Debug.LogError("[PryingEyes] piano non generabile: " + PlanGen.LastFailure); yield break; }
            float t0 = Time.realtimeSinceStartup;
            cur = FloorBuilder.Build(plan, null);
            float tBuild = Time.realtimeSinceStartup - t0;
            Physics.SyncTransforms();
            yield return null;
            HotelNav.Rebuild(cur.bounds, cur.propRoots);
            int stripped = Connectivity.Repair(cur, out int problems);
            if (problems > 0) Debug.Log($"[PryingEyes] connettivita': {problems} problemi, {stripped} stanze svuotate");
            ApplyLook(plan);
            ValidateReach(plan);

            // obiettivo: candele da accendere; uscita sigillata finche' non lo sono tutte
            lit = 0; total = cur.candles.Count; exitOpen = false; firstHorde = false; lastHorde = false;
            foreach (var c in cur.candles) c.onLit += OnCandle;
            if (cur.exit != null)
            {
                cur.exit.unlocked = false;
                cur.exit.lockedText = () => $"L'ASCENSORE È SIGILLATO — CANDELE {lit}/{total}";
                cur.exit.onEnter = () => { if (!transitioning) StartCoroutine(Descend()); };
            }
            PlaceWallBuys(plan);
            StartCoroutine(Gramophone(plan));
            floorTime = 0f; trickleT = 22f; hudT = 0f;

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
            Debug.Log($"[PryingEyes] piano {n} seme {runSeed + n * 1013}: {plan.rooms.Count} stanze, {cur.candles.Count} candele, {LightPool.Count} luci, " +
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

        /// <summary>Atmosfera del piano: nebbia e tinta dell'ambiente cambiano un po' tra piani d'albergo e di chiesa.</summary>
        void ApplyLook(Plan plan)
        {
            var rnd = new Rng(plan.seed + 77);
            bool churchy = plan.startInChurch;
            RenderSettings.fogDensity = rnd.Range(0.016f, 0.026f);
            RenderSettings.fogColor = churchy ? new Color(0.04f, 0.06f, 0.10f) : new Color(0.07f, 0.055f, 0.06f);
        }

        // ------------------------------------------------------------------ candele e uscita
        void OnCandle(Candelabro c)
        {
            lit++;
            var hud = gm.hud;
            gm.AddPoints(60);
            if (lit >= total)
            {
                OpenExit();
            }
            else
            {
                hud?.Toast($"Candela accesa ({lit}/{total}). Qualcosa si muove nell'albergo.");
                Horde(3 + floor + lit, "candela");
            }
            RefreshObjective();
        }

        void OpenExit()
        {
            exitOpen = true;
            if (cur.exit != null) { cur.exit.unlocked = true; cur.exit.Open(); }
            gm.hud?.Toast("Tutte le candele sono accese. Le porte dell'ascensore si aprono.");
            Sfx.UI(SoundBank.Toll, 0.7f);
            if (!lastHorde) { lastHorde = true; Horde(4 + floor * 2, "ultima"); }
        }

        void RefreshObjective()
        {
            var hud = gm.hud;
            if (hud == null) return;
            if (exitOpen) { hud.SetObjective("Raggiungi l'ascensore e scendi"); return; }
            float best = float.MaxValue;
            var p = gm.player.transform.position;
            foreach (var c in cur.candles) if (c != null && !c.lit) best = Mathf.Min(best, Vector3.Distance(p, c.transform.position));
            hud.SetObjective(best < float.MaxValue ? $"Accendi le candele {lit}/{total}  ·  la più vicina a {best:F0} m" : $"Candele {lit}/{total}");
        }

        IEnumerator Descend()
        {
            transitioning = true;
            var pl = gm.player;
            pl.invulnerable = true;
            gm.AddPoints(100 + 50 * floor);
            gm.hud?.Toast($"Piano {floor} superato. +{100 + 50 * floor} punti");
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
            if (cur != null && cur.arrival != null) cur.arrival.SetOpenInstant(true);
        }

        // ------------------------------------------------------------------ nemici
        Enemy.EnemyType PickType()
        {
            float fly = Mathf.Min(0.38f, 0.14f + floor * 0.025f);
            float abate = floor >= 2 ? Mathf.Min(0.22f, 0.04f + floor * 0.025f) : 0f;
            float r = Random.value;
            if (r < fly) return Enemy.EnemyType.Occhio;
            if (r < fly + abate) return Enemy.EnemyType.Abate;
            return Enemy.EnemyType.Monaco;
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
            if (PickSpawn(t == Enemy.EnemyType.Occhio, out Vector3 p)) Enemy.Spawn(t, p);
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
            var tints = new[] { new Color(0.30f, 0.95f, 1f), new Color(1f, 0.62f, 0.22f), new Color(0.70f, 0.55f, 1f) };
            int[] cost = { 500, 1200, 1800 };
            var used = new HashSet<Room>();
            var rng = new Rng(plan.seed + 5);
            for (int k = 0; k < 3; k++)
            {
                Room room = cand[Mathf.Clamp(pick[k], 0, cand.Count - 1)];
                int guard = 0;
                while (used.Contains(room) && guard++ < cand.Count) room = cand[(cand.IndexOf(room) + 1) % cand.Count];
                used.Add(room);
                if (TryWallSpot(plan, room, rng, out Vector3 pos, out Quaternion rot))
                    WallBuy.Place(k + 1, cost[k], pos, rot, tints[k]);
            }
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

        // ------------------------------------------------------------------ ogni fotogramma
        void Update()
        {
            if (cur == null || gm == null || gm.player == null) return;
            float dt = Time.deltaTime;
            if (!transitioning && gm.player.alive)
            {
                floorTime += dt;
                if (!firstHorde && floorTime > 16f) { firstHorde = true; Horde(2 + floor / 2, "primo"); }
                trickleT -= dt;
                if (trickleT <= 0f)
                {
                    trickleT = Mathf.Lerp(24f, 10f, Mathf.Clamp01(floor / 10f)) * Random.Range(0.8f, 1.25f);
                    if (floorTime > 8f && Enemy.All.Count < 4 + floor) SpawnOne();
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
                System.IO.File.AppendAllText(BotLog, $"t={botT:F0} piano={floor} candele={lit}/{total} uscita={(exitOpen ? "aperta" : "chiusa")} hp={pl.hp:F0} nemici={Enemy.All.Count} pos=({p.x:F0},{p.z:F0}) " +
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
