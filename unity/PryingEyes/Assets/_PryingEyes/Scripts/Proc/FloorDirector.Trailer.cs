#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.Proc
{
    /// <summary>Le riprese del trailer: <c>PryingEyes.exe -batchmode -autotest -nofire -nowaves -trailer copione.json</c> (senza -nographics).
    /// Il copione (TrailerJob, scritto da tools/trailer) dice che cosa fare: "themes" elenca tema e stanze di una serie di semi senza
    /// costruire niente; "scout" costruisce i piani e fotografa ogni stanza da piu' punti (e scrive le piante, per piazzare le camere);
    /// "preview" fa tre foto per ripresa; "film" gira i filmati. Il gioco e' quello vero (luci, nebbia, nemici, arma in mano): la camera
    /// la guida il copione, il tempo e' a fotogrammi fissi (Time.captureFramerate) e ogni fotogramma passa da TrailerCapture a ffmpeg.
    /// Gli arredi e i nemici non sanno di essere ripresi: i nemici inseguono il giocatore, che il regista tiene sotto la camera.</summary>
    public partial class FloorDirector
    {
        bool trailerStarted;
        TrailerJob tj;
        TrailerCapture tcap;
        float tEye = 1.62f;

        void TrailerHook()
        {
            if (trailerStarted || !GameManager.autoTest || transitioning || floorTime <= 2f || !HasArg("-trailer")) return;
            trailerStarted = true;
            StartCoroutine(TrailerMain());
        }

        // ------------------------------------------------------------------ utilita'
        static Vector3 TV(float[] a, Vector3 def) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : def;
        static float TF(float v, float def) => v > 0f ? v : def;
        static float[] TA(Vector3 v) => new[] { Mathf.Round(v.x * 100f) / 100f, Mathf.Round(v.y * 100f) / 100f, Mathf.Round(v.z * 100f) / 100f };

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>Posa della camera al tempo t: spline di Catmull-Rom per posizione e punto guardato, tempo rimappato dall'andamento (ease).</summary>
        static void TrailerEval(TrailerShot s, float t, out Vector3 pos, out Vector3 look, out float fov, out float roll)
        {
            float u = Mathf.Clamp01(t / s.dur);
            switch (s.ease)
            {
                case 1: u = u * u * (3f - 2f * u); break;
                case 2: u = 1f - (1f - u) * (1f - u); break;
                case 3: u *= u; break;
            }
            float tt = u * s.dur;
            var k = s.keys;
            int n = k.Length;
            if (n == 1)
            {
                pos = TV(k[0].p, Vector3.zero);
                look = TV(k[0].look, pos + Vector3.forward);
                fov = TF(k[0].fov, 62f); roll = k[0].roll;
                return;
            }
            int i = 0;
            while (i < n - 2 && tt > k[i + 1].t) i++;
            float t0 = k[i].t, t1 = k[i + 1].t;
            float f = t1 > t0 ? Mathf.Clamp01((tt - t0) / (t1 - t0)) : 1f;
            Vector3 P(int j) => TV(k[Mathf.Clamp(j, 0, n - 1)].p, Vector3.zero);
            Vector3 L(int j) => TV(k[Mathf.Clamp(j, 0, n - 1)].look, Vector3.zero);
            pos = CatmullRom(P(i - 1), P(i), P(i + 1), P(i + 2), f);
            look = CatmullRom(L(i - 1), L(i), L(i + 1), L(i + 2), f);
            fov = Mathf.Lerp(TF(k[i].fov, 62f), TF(k[i + 1].fov, 62f), f);
            roll = Mathf.Lerp(k[i].roll, k[i + 1].roll, f);
        }

        /// <summary>Il giocatore sotto la camera: i nemici lo inseguono, le luci vicine si accendono e i budget dei corpi si calcolano da li'.</summary>
        void TrailerPlacePlayer(Vector3 camPos)
        {
            var pl = GameManager.I.player;
            // sulla navmesh se c'e' (anche con la camera in basso o in alto: il giocatore sta a terra), altrimenti a un'altezza d'occhio sotto la camera
            Vector3 feet = NavMesh.SamplePosition(camPos, out NavMeshHit h, 3.2f, NavMesh.AllAreas) ? h.position : new Vector3(camPos.x, camPos.y - tEye, camPos.z);
            pl.Teleport(feet + Vector3.up * 0.02f);
        }

        static void TrailerSetLook(PlayerController pl, Vector3 eye, Vector3 look)
        {
            Vector3 d = look - eye;
            pl.yaw = Mathf.Atan2(d.x, d.z);
            pl.pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude);       // positivo = guarda in basso
        }

        // ------------------------------------------------------------------ regia
        IEnumerator TrailerMain()
        {
            string file = ArgStr("-trailer", "");
            if (!File.Exists(file)) { Debug.LogError("[PryingEyes] trailer: copione non trovato: " + file); Application.Quit(2); yield break; }
            try { tj = JsonUtility.FromJson<TrailerJob>(File.ReadAllText(file)); }
            catch (Exception ex) { Debug.LogError("[PryingEyes] trailer: copione non valido: " + ex.Message); tj = null; }
            if (tj == null || string.IsNullOrEmpty(tj.outDir)) { Debug.LogError("[PryingEyes] trailer: copione vuoto o senza outDir"); Application.Quit(2); yield break; }
            tj.fps = (int)TF(tj.fps, 30); tj.width = (int)TF(tj.width, 1920); tj.height = (int)TF(tj.height, 1080);
            tj.crf = (int)TF(tj.crf, 14); tj.msaa = (int)TF(tj.msaa, 4);
            Directory.CreateDirectory(tj.outDir);
            string mode = string.IsNullOrEmpty(tj.mode) ? "film" : tj.mode;
            var pl = GameManager.I.player;
            if (mode != "themes" && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            { Debug.LogError("[PryingEyes] trailer: serve la grafica vera (senza -nographics)"); Application.Quit(2); yield break; }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Time.captureFramerate = tj.fps;
            PlayerController.holdLook = true;
            pl.invulnerable = true;
            Enemy.ShadowBudgetOn = false; Enemy.DetailBudgetOn = false;     // corpi interi e con ombra: il costo non conta, il filmato e' fuori tempo reale
            LightPool.MaxShadows = 6; LightPool.ShadowDist = 16f;
            var main = pl.cam.GetComponent<Camera>();
            if (main == null) main = Camera.main;
            tcap = TrailerCapture.Create(main, tj.width, tj.height, tj.msaa);
            float t0 = Time.realtimeSinceStartup;
            Debug.Log($"[PryingEyes] trailer: modo {mode}, {tj.width}x{tj.height} a {tj.fps} fps, uscita {tj.outDir}");

            if (mode == "themes") yield return TrailerThemes();
            else if (mode == "scout") yield return TrailerScout();
            else yield return TrailerFilm(mode == "preview");

            Debug.Log($"[PryingEyes] trailer: fine in {Time.realtimeSinceStartup - t0:F0} s");
            File.WriteAllText(Path.Combine(tj.outDir, "done.txt"), mode + " " + DateTime.Now.ToString("s") + "\n");
            tcap.Dispose();
            Time.captureFramerate = 0;
            Application.Quit();
        }

        IEnumerator TrailerBuild(int planSeed, int n)
        {
            runSeed = planSeed - n * 1013;
            yield return GoTo(n, true);
            var pl = GameManager.I.player;
            pl.invulnerable = true;
            Enemy.ClearAll();
            var lift = cur.arrival;
            while (lift != null && lift.Busy) yield return null;
            if (RoomAccessDoors.I != null)           // tutte le aree aperte: le serrande delle aree a pagamento sono su e le camere passano
            {
                var ids = new List<int>();
                foreach (var r in cur.plan.rooms) ids.Add(r.id);
                RoomAccessDoors.I.ApplySnapshot(ids, true);
            }
            // le cornici colorate delle porte (tipo di stanza, come in Isaac) sono guida di gioco: nel filmato sono contorni bianchi che distraggono
            foreach (var tr in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (tr.name == "Cornici di tipo") tr.gameObject.SetActive(false);
            for (int i = 0; i < 8; i++) yield return null;
            Debug.Log($"[PryingEyes] trailer: piano {n} seme {planSeed} pronto, tema {cur.plan.theme}");
        }

        // ------------------------------------------------------------------ themes: elenco dei temi per semi
        IEnumerator TrailerThemes()
        {
            var sb = new StringBuilder();
            PlanGen.SurvivalLayout = SurvivalMode.Enabled;
            var tries = tj.floors != null && tj.floors.Length > 0 ? tj.floors : new[] { new TrailerFloor { floor = 3 } };
            int count = 0;
            for (int seed = tj.seedFrom; seed <= tj.seedTo; seed++)
                foreach (var f in tries)
                {
                    var plan = PlanGen.Generate(seed, f.floor);
                    if (plan == null) { sb.AppendLine($"seed={seed} floor={f.floor} piano non generabile: {PlanGen.LastFailure}"); continue; }
                    var kinds = new List<string>();
                    foreach (var r in plan.rooms) if (!r.IsLift) kinds.Add($"{r.kind}{(r.type != RoomType.Normale ? "/" + r.type : "")}:{r.W / 10f:F0}x{r.D / 10f:F0}");
                    sb.AppendLine($"seed={seed} floor={f.floor} theme={plan.theme} rooms={plan.rooms.Count} :: {string.Join(", ", kinds)}");
                    if (++count % 25 == 0) yield return null;
                }
            File.WriteAllText(Path.Combine(tj.outDir, "themes.txt"), sb.ToString());
            Debug.Log($"[PryingEyes] trailer: themes {count} piani in {Path.Combine(tj.outDir, "themes.txt")}");
        }

        // ------------------------------------------------------------------ scout: foto di ogni stanza e dati della pianta
        struct ScoutView { public string tag; public Vector3 eye, look; public float fov; }

        /// <summary>Quattro punti di vista per stanza: dalla porta piu' larga, dalle due estremita' dell'asse lungo e da un angolo in alto.</summary>
        static List<ScoutView> TrailerViews(Plan plan, Room r)
        {
            var res = new List<ScoutView>();
            Vector3 ctr = new Vector3(r.CenterX, 0f, r.CenterZ);
            bool ax = r.W >= r.D;
            Vector3 axis = ax ? Vector3.right : Vector3.forward;
            float len = Mathf.Max(r.W, r.D) * 0.1f;
            Vector3 Ground(Vector3 p, out bool ok)
            {
                ok = NavMesh.SamplePosition(p, out NavMeshHit h, 2.5f, NavMesh.AllAreas);
                return ok ? h.position : p;
            }
            // dalla porta piu' larga verso l'interno
            Opening best = null; int bw = -1;
            foreach (var o in plan.openings)
            {
                if ((o.a != r.id && o.b != r.id) || o.b < 0 || o.oneSided) continue;
                if (plan.rooms[o.a == r.id ? o.b : o.a].IsLift) continue;
                if (o.width > bw) { bw = o.width; best = o; }
            }
            if (best != null)
            {
                Vector3 wall = best.alongX ? new Vector3(best.center * 0.1f, 0f, best.line * 0.1f) : new Vector3(best.line * 0.1f, 0f, best.center * 0.1f);
                Vector3 dir = best.alongX ? new Vector3(0f, 0f, Mathf.Sign(ctr.z - wall.z)) : new Vector3(Mathf.Sign(ctr.x - wall.x), 0f, 0f);
                Vector3 g = Ground(wall + dir * 0.9f, out bool ok);
                if (ok) res.Add(new ScoutView { tag = "door", eye = g + Vector3.up * 1.6f, look = g + Vector3.up * 1.45f + dir * 8f, fov = 66f });
            }
            // lungo l'asse, da una estremita' e dall'altra
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 g = Ground(ctr - axis * s * (len * 0.5f - 1.4f), out bool ok);
                if (!ok) continue;
                res.Add(new ScoutView { tag = s < 0 ? "axisB" : "axisA", eye = g + Vector3.up * 1.6f, look = ctr + axis * s * (len * 0.5f) + Vector3.up * 1.5f, fov = 66f });
            }
            // dall'angolo, in alto
            {
                Vector3 c = new Vector3(r.x0 * 0.1f + 1.2f, 0f, r.z0 * 0.1f + 1.2f);
                Vector3 g = Ground(c, out bool ok);
                if (ok) res.Add(new ScoutView { tag = "corner", eye = g + Vector3.up * Mathf.Min(2.6f, r.wallH * 0.65f), look = ctr + Vector3.up * 1.3f, fov = 72f });
            }
            return res;
        }

        IEnumerator TrailerStill(Vector3 eye, Vector3 look, float fov, string path)
        {
            TrailerPlacePlayer(eye);
            for (int i = 0; i < 8; i++) { if (i % 3 == 0) LightPool.Tick(true); yield return null; }
            tcap.hideView = true;
            tcap.Request(true, eye, Quaternion.LookRotation((look - eye).normalized, Vector3.up), fov, path);
            yield return null;
        }

        IEnumerator TrailerScout()
        {
            if (tj.floors == null || tj.floors.Length == 0) { Debug.LogWarning("[PryingEyes] trailer: scout senza piani"); yield break; }
            foreach (var f in tj.floors)
            {
                yield return TrailerBuild(f.seed, f.floor);
                var plan = cur.plan;
                var sf = new ScoutFloor { seed = f.seed, floor = f.floor, theme = plan.theme.ToString(), grid = plan.grid };
                var rooms = new List<ScoutRoom>();
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var r in plan.rooms)
                {
                    rooms.Add(new ScoutRoom
                    {
                        id = r.id, wing = r.wing, kind = r.kind.ToString(), type = r.type.ToString(), ceil = r.ceil.ToString(), church = r.church, lift = r.IsLift,
                        gallery = r.upperGallery, x0 = r.x0 * 0.1f, z0 = r.z0 * 0.1f, x1 = r.x1 * 0.1f, z1 = r.z1 * 0.1f, wallH = r.wallH, ridgeH = r.ridgeH,
                    });
                    minX = Mathf.Min(minX, r.x0 * 0.1f); minZ = Mathf.Min(minZ, r.z0 * 0.1f); maxX = Mathf.Max(maxX, r.x1 * 0.1f); maxZ = Mathf.Max(maxZ, r.z1 * 0.1f);
                }
                sf.rooms = rooms.ToArray();
                var ops = new List<ScoutOpening>();
                foreach (var o in plan.openings)
                    ops.Add(new ScoutOpening { a = o.a, b = o.b, alongX = o.alongX, line = o.line * 0.1f, center = o.center * 0.1f, width = o.width * 0.1f, sill = o.sill, top = o.top, style = o.style.ToString() });
                sf.openings = ops.ToArray();
                if (cur.arrival != null)
                {
                    sf.liftSpawn = TA(cur.arrival.spawnPoint); sf.liftLook = TA(cur.arrival.lookOut);
                    sf.liftFront = TA(cur.arrival.frontPoint); sf.liftDoor = TA(cur.arrival.doorPoint);
                }
                if (cur.exit != null) sf.exitSpawn = TA(cur.exit.spawnPoint);
                // punti camminabili della navmesh (due quote: terra e balconate)
                var walk = new List<float>();
                for (float x = minX; x <= maxX; x += 0.8f)
                    for (float z = minZ; z <= maxZ; z += 0.8f)
                        foreach (float y in new[] { 0.5f, 3.6f })
                            if (NavMesh.SamplePosition(new Vector3(x, y, z), out NavMeshHit h, 0.6f, NavMesh.AllAreas) && Mathf.Abs(h.position.x - x) < 0.3f && Mathf.Abs(h.position.z - z) < 0.3f
                                && (y < 1f || h.position.y > 1.5f))
                            { walk.Add(Mathf.Round(h.position.x * 100f) / 100f); walk.Add(Mathf.Round(h.position.y * 100f) / 100f); walk.Add(Mathf.Round(h.position.z * 100f) / 100f); }
                sf.walk = walk.ToArray();
                // foto
                var stills = new List<string>();
                foreach (var r in plan.rooms)
                {
                    if (r.IsLift) continue;
                    foreach (var v in TrailerViews(plan, r))
                    {
                        string name = $"s{f.seed}_f{f.floor}_r{r.id}_{r.kind}_{v.tag}.jpg";
                        yield return TrailerStill(v.eye, v.look, v.fov, Path.Combine(tj.outDir, name));
                        stills.Add(name);
                    }
                }
                if (cur.arrival != null)
                {
                    var lf = cur.arrival;
                    Vector3 eye = lf.spawnPoint + Vector3.up * 1.55f - new Vector3(lf.lookOut.x, 0f, lf.lookOut.z).normalized * 0.45f;
                    lf.SetOpenInstant(true);
                    string name = $"s{f.seed}_f{f.floor}_lift_view.jpg";
                    yield return TrailerStill(eye, eye + new Vector3(lf.lookOut.x, -0.05f, lf.lookOut.z) * 6f, 62f, Path.Combine(tj.outDir, name));
                    stills.Add(name);
                }
                sf.stills = stills.ToArray();
                File.WriteAllText(Path.Combine(tj.outDir, $"scout_{f.seed}_{f.floor}.json"), JsonUtility.ToJson(sf, false));
                Debug.Log($"[PryingEyes] trailer: scout {f.seed}/{f.floor} tema {plan.theme}: {plan.rooms.Count} stanze, {stills.Count} foto, {sf.walk.Length / 3} punti navmesh");
            }
        }

        // ------------------------------------------------------------------ film e anteprima
        static bool TrailerMatch(string name, string[] words)
        {
            foreach (var w in words) if (w.Length > 0 && name.IndexOf(w.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        IEnumerator TrailerFilm(bool preview)
        {
            if (tj.shots == null || tj.shots.Length == 0) { Debug.LogWarning("[PryingEyes] trailer: nessuna ripresa nel copione"); yield break; }
            string[] only = string.IsNullOrEmpty(tj.only) ? null : tj.only.Split(',');
            int curSeed = int.MinValue, curFloor = -1;
            foreach (var s in tj.shots)
            {
                if (s == null || s.skip || string.IsNullOrEmpty(s.name)) continue;
                if (only != null && !TrailerMatch(s.name, only)) continue;
                if (s.seed != curSeed || s.floor != curFloor || s.rebuild)
                {
                    yield return TrailerBuild(s.seed, s.floor);
                    curSeed = s.seed; curFloor = s.floor;
                }
                yield return TrailerShotPlay(s, preview);
            }
        }

        Enemy TrailerSpawn(TrailerEnemy te)
        {
            if (!Enum.TryParse(te.type, out Enemy.EnemyType et)) { Debug.LogWarning("[PryingEyes] trailer: nemico sconosciuto " + te.type); return null; }
            var e = Enemy.Spawn(et, TV(te.pos, Vector3.zero));
            if (e == null) return null;
            e.hp = e.maxHp = te.hp > 0f ? te.hp : 100000f;
            var ag = e.GetComponent<NavMeshAgent>();
            if (te.speed > 0f) { e.speed = te.speed; if (ag != null) ag.speed = te.speed; }
            e.transform.rotation = Quaternion.Euler(0f, te.yaw, 0f);
            if (te.still) { if (ag != null) ag.enabled = false; e.enabled = false; }
            return e;
        }

        void TrailerEventDo(TrailerEvent e, Enemy[] spawned)
        {
            var w = GameManager.I.player.weapon;
            switch (e.kind)
            {
                case "fire": w.DebugFire(); break;
                case "reload": w.Reload(); break;
                case "ads": Weapon.debugAds = e.v > 0.5f; break;
                case "equip": w.Equip(e.i); break;
                case "torch": if (Torch.I != null) { Torch.I.on = e.v > 0.5f; Torch.I.charge = 1f; } break;
                case "slow": Time.timeScale = Mathf.Clamp(e.v, 0.02f, 1f); break;
                case "kill":
                    if (e.i >= 0 && e.i < spawned.Length && spawned[e.i] != null) spawned[e.i].Damage(99999f, spawned[e.i].AimPoint, true);
                    break;
            }
        }

        IEnumerator TrailerShotPlay(TrailerShot s, bool preview)
        {
            var pl = GameManager.I.player;
            int fps = tj.fps;
            s.dur = TF(s.dur, 4f);
            int nFrames = Mathf.Max(2, Mathf.RoundToInt(s.dur * fps));
            int nWarm = Mathf.Max(3, Mathf.RoundToInt(TF(s.warm, 0.6f) * fps));
            if (s.keys == null || s.keys.Length == 0) { Debug.LogWarning("[PryingEyes] trailer: ripresa senza camera: " + s.name); yield break; }
            float wall0 = Time.realtimeSinceStartup;

            // ambiente del piano come all'inizio, poi i ritocchi della ripresa
            Enemy.ClearAll();
            Weapon.debugAds = false;
            ApplyLook(cur.plan, floor);
            GameSettings.SetThemeExposure(LookOf(cur.plan).exposure + LookTune.Exposure + s.ev);
            if (s.fog > 0f) RenderSettings.fogDensity *= s.fog;
            var vol = FindFirstObjectByType<Volume>();
            if (s.bloom > 0f && vol != null && vol.profile.TryGet(out Bloom bl)) bl.intensity.Override(bl.intensity.value * s.bloom);
            Time.timeScale = 1f;
            DepthOfField dof = null;
            if (s.dof > 0f && vol != null)
            {
                if (!vol.profile.TryGet(out dof)) dof = vol.profile.Add<DepthOfField>(true);
                dof.active = true;
                dof.mode.Override(DepthOfFieldMode.Bokeh);
                dof.aperture.Override(Mathf.Clamp(s.dof, 1f, 32f));
                dof.focalLength.Override(TF(s.focal, 50f));
                dof.focusDistance.Override(TF(s.focus, 8f));
            }
            if (s.equip > 0) pl.weapon.Equip(s.equip - 1);
            if (Torch.I != null) { Torch.I.on = s.torch; Torch.I.charge = 1f; }
            tcap.hideView = !s.fps;

            // posa iniziale, ascensore, nemici
            TrailerEval(s, 0f, out Vector3 p0, out Vector3 l0, out _, out _);
            TrailerPlacePlayer(p0);
            TrailerSetLook(pl, p0, l0);
            var lift = cur.arrival;
            if (s.lift && lift != null) { while (lift.Busy) yield return null; lift.SetOpenInstant(false); }
            var enemyDefs = s.enemies ?? new TrailerEnemy[0];
            var spawned = new Enemy[enemyDefs.Length];
            var spawnedDone = new bool[enemyDefs.Length];
            for (int j = 0; j < enemyDefs.Length; j++)
                if (enemyDefs[j].delay <= 0f) { spawned[j] = TrailerSpawn(enemyDefs[j]); spawnedDone[j] = true; }

            for (int i = 0; i < nWarm; i++)
            {
                TrailerPlacePlayer(p0);
                TrailerSetLook(pl, p0, l0);
                if (i % 3 == 0) LightPool.Tick(true);
                yield return null;
            }
            if (pl.cam != null) tEye = Mathf.Clamp(pl.cam.position.y - pl.transform.position.y, 1.2f, 2.0f);

            string path = Path.Combine(tj.outDir, s.name + ".mp4");
            if (!preview && !tcap.BeginVideo(path, fps, tj.crf)) { Debug.LogError("[PryingEyes] trailer: ffmpeg non parte, ripresa saltata: " + s.name); yield break; }
            var evs = new List<TrailerEvent>(s.events ?? new TrailerEvent[0]);
            evs.Sort((a, b) => a.t.CompareTo(b.t));
            int evNext = 0;
            int[] stillAt = preview ? new[] { 1, nFrames / 2, nFrames - 2 } : null;
            string[] stillTag = { "a", "b", "c" };
            var burstShots = new Dictionary<TrailerEvent, int>();
            float seedN = Mathf.Abs(s.name.GetHashCode() % 1000) * 0.37f;
            Vector3 lookSmooth = Vector3.zero; bool haveLook = false;

            for (int i = 0; i < nFrames; i++)
            {
                float t = i / (float)fps;
                for (int j = 0; j < enemyDefs.Length; j++)
                    if (!spawnedDone[j] && t >= enemyDefs[j].delay) { spawned[j] = TrailerSpawn(enemyDefs[j]); spawnedDone[j] = true; }
                while (evNext < evs.Count && evs[evNext].t <= t + 1e-4f)
                {
                    var ev = evs[evNext++];
                    if (ev.kind != "burst") TrailerEventDo(ev, spawned);
                }
                foreach (var ev in evs)           // raffiche: v secondi a i colpi al secondo
                {
                    if (ev.kind != "burst" || t < ev.t || t >= ev.t + ev.v) continue;
                    int due = Mathf.FloorToInt((t - ev.t) * Mathf.Max(1, ev.i)) + 1;
                    burstShots.TryGetValue(ev, out int done);
                    if (due > done) { pl.weapon.DebugFire(); burstShots[ev] = done + 1; }
                }
                if (i == 0 && s.lift && lift != null) lift.Open();

                TrailerEval(s, t, out Vector3 pos, out Vector3 look, out float fov, out float roll);
                if (s.lookEnemy > 0 && s.lookEnemy <= spawned.Length && spawned[s.lookEnemy - 1] != null && t >= s.lookFrom)
                {
                    Vector3 target = spawned[s.lookEnemy - 1].AimPoint + TV(s.lookOff, Vector3.zero);
                    float w = Mathf.SmoothStep(0f, 1f, (t - s.lookFrom) / 0.6f);
                    look = Vector3.Lerp(look, target, w);
                }
                if (dof != null && s.focus <= 0f && s.lookEnemy > 0 && s.lookEnemy <= spawned.Length && spawned[s.lookEnemy - 1] != null)
                    dof.focusDistance.Override(Mathf.Max(0.5f, Vector3.Distance(pos, spawned[s.lookEnemy - 1].AimPoint)));
                if (!haveLook) { lookSmooth = look; haveLook = true; }
                lookSmooth = Vector3.Lerp(lookSmooth, look, 1f - Mathf.Exp(-14f / fps * 3f));      // l'inseguimento non scatta
                look = lookSmooth;

                TrailerPlacePlayer(pos);
                if (i % 2 == 0) LightPool.Tick(true);
                if (s.fps)
                {
                    var tgt = s.aim == "nearest" ? Enemy.Nearest(pos) : null;
                    if (tgt != null)
                    {
                        Vector3 d = tgt.AimPoint - pos;
                        float yawT = Mathf.Atan2(d.x, d.z), pitchT = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude);
                        float k = 1f - Mathf.Exp(-7f / fps);
                        pl.yaw = Mathf.LerpAngle(pl.yaw * Mathf.Rad2Deg, yawT * Mathf.Rad2Deg, k) * Mathf.Deg2Rad;
                        pl.pitch = Mathf.Lerp(pl.pitch, pitchT, k);
                    }
                    else TrailerSetLook(pl, pos, look);
                    if (!preview || Array.IndexOf(stillAt, i) >= 0)
                        tcap.Request(false, pos, Quaternion.identity, fov, preview ? Path.Combine(tj.outDir, $"{s.name}_{stillTag[Array.IndexOf(stillAt, i)]}.jpg") : null);
                }
                else
                {
                    TrailerSetLook(pl, pos, look);
                    if (!preview || Array.IndexOf(stillAt, i) >= 0)
                    {
                        float sw = s.sway;
                        Vector3 off = Vector3.zero; Quaternion rot = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
                        float tn = t + seedN;
                        if (sw > 0f)
                        {
                            off = new Vector3(Mathf.PerlinNoise(tn * 0.55f, 1.3f) - 0.5f, Mathf.PerlinNoise(tn * 0.7f, 7.1f) - 0.5f, Mathf.PerlinNoise(tn * 0.5f, 3.7f) - 0.5f) * 0.05f * sw;
                            float dp = (Mathf.PerlinNoise(tn * 0.8f, 11.3f) - 0.5f) * 0.9f * sw, dy = (Mathf.PerlinNoise(tn * 0.65f, 5.9f) - 0.5f) * 0.9f * sw;
                            float dr = (Mathf.PerlinNoise(tn * 0.45f, 9.2f) - 0.5f) * 0.7f * sw;
                            rot *= Quaternion.Euler(dp, dy, dr);
                        }
                        rot *= Quaternion.Euler(0f, 0f, roll);
                        tcap.Request(true, pos + off, rot, fov, preview ? Path.Combine(tj.outDir, $"{s.name}_{stillTag[Array.IndexOf(stillAt, i)]}.jpg") : null);
                    }
                }
                yield return null;
            }
            if (!preview) tcap.EndVideo();
            if (dof != null) dof.active = false;
            Time.timeScale = 1f;
            Weapon.debugAds = false;
            Enemy.ClearAll();
            Debug.Log($"[PryingEyes] trailer: ripresa {s.name}: {(preview ? 3 : tcap.Frames)} {(preview ? "foto" : "fotogrammi")} in {Time.realtimeSinceStartup - wall0:F1} s");
        }
    }
}
#endif
