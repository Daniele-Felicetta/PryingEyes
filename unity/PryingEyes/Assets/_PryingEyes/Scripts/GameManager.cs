using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PryingEyes
{
    /// <summary>Wave manager, score, spawn points, player respawn. Port of web/UE design 1:1.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I;
        /// <summary>True nella scena Torre (piani procedurali): niente ondate della hall.</summary>
        public static bool proc;
        public bool LevelReady => player != null && player.weapon != null && (!proc ||
            (Proc.FloorDirector.I != null && Proc.FloorDirector.I.cur != null && !Proc.FloorDirector.I.transitioning));

        [Header("Waves")]
        public int wave = 0;
        // I punti sono la valuta: si parte da 500 come in COD Zombies.
        public int score = 500;
        public int kills = 0;
        public bool waveActive = false;
        float waveBreak = 0f;

        [Header("Refs (auto-wired by HotelBuilder)")]
        public PlayerController player;
        public HUD hud;
        public Transform[] spawnPoints;

        int alive = 0;

        // --- autotest bot (built with -autotest flag; off for players) ---
        public static bool autoTest = false;
        /// <summary>-guntest: niente bot e niente nemici, il test passa in rassegna tutte le armi (foto e numeri).</summary>
        public static bool gunTest = false;
        /// <summary>-nofire (o -stealthtest): il bot di prova non spara da solo; senza, ogni sparo sveglia tutto il piano.</summary>
        public static bool noFire = false;
        public static int shotsFired = 0, shotsHit = 0, damageTaken = 0;
        float autoT = 0f, reportT = 0f, walkerMaxY = 0f;
        // -botmezz: il bot gioca dal soppalco (y 4.2), dove i nemici colpivano attraverso il pavimento
        static readonly Vector3 MezzSpot = new Vector3(-20f, 4.3f, -17f);
        bool shot1done = false, shot2done = false;
        // -camshots: foto dalla camera del giocatore su RenderTexture, funziona anche con -batchmode senza finestra
        // (ScreenCapture li' non ha uno schermo da copiare). Niente HUD: serve a controllare luce e materiali.
        bool camShots, cam1done, cam2done, wallShotDone;
        bool wallTestDone, magicTestDone, enemyShotDone, gunTestStarted, leanTestStarted;
        /// <summary>Rapporto delle prove (-autotest). Con -report FILE ogni processo scrive il suo: piu' prove in parallelo non si pestano i piedi.</summary>
        static readonly string REPORT = ReportPath();
        static string ReportPath()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-report") return a[i + 1];
            return "C:/Users/danie/Desktop/prying-eyes/hotel_build/autotest.txt";
        }
        bool returningToMenu;

        /// <summary>Chiude la sessione locale e ricrea la scena iniziale: nessuno stato di una discesa entra nella successiva.</summary>
        public void ReturnToMenu()
        {
            if (returningToMenu) return;
            returningToMenu = true;
            Coop.Stop("");
            StopAllCoroutines();
            Proc.FloorDirector.I?.StopAllCoroutines();
            Proc.RoundDirector.ResetRun();
            Enemy.ClearAll();
            WallBuy.ClearAll();
            Proc.Interact.Clear();
            Proc.LightPool.Clear();
            Drops.ResetRun();
            Perks.ResetRun();
            Targets.Clear();
            Fx.Clear();
            Sfx.StopAll();
            TouchControls.ResetInput();
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        void Awake()
        {
            I = this;
            shotsFired = shotsHit = damageTaken = 0;
            // i valori serializzati nella scena costruita dall'editor hanno la precedenza sui default:
            // qui riallineo la valuta di partenza al ritmo COD Zombies
            score = 500;
            autoTest = HasArg("-autotest") || HasArg("-guntest");
            gunTest = HasArg("-guntest");
            noFire = HasArg("-nofire") || HasArg("-stealthtest") || HasArg("-cooptest") || HasArg("-survivaltest") || HasArg("-citytest");
            if (autoTest && (HasArg("-procbot") || HasArg("-stealthtest"))) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }
            camShots = HasArg("-camshots");
            if (autoTest)
                System.IO.File.WriteAllText(REPORT, "t wave kills score hp shield mag reserve enemies shots hits colpiSubiti yMaxCamminatori\n");
            proc = FindFirstObjectByType<Proc.FloorDirector>() != null;
            GameSettings.BindBrightness(FindAnyObjectByType<UnityEngine.Rendering.Volume>());
            if (!proc)
            {
                HotelNav.Build();   // prima di qualunque nemico: i collider della scena ci sono gia'
                if (FindFirstObjectByType<CityDirector>() == null)
                    WallBuy.BuildAll(); // armi sui muri della hall classica
                Ambience.Create();  // eco, vento, scricchiolii, grammofono; genera anche tutti i suoni
            }
            else Proc.ProcAmbience.Create();
            if (proc) Coop.BootFromArgs();   // prove con due processi: -coophost / -coopjoin <indirizzo>
            if (!autoTest && !camShots) GameMenu.Create();   // menu principale: la prima ondata parte da "Gioca"
        }

        /// <summary>Avvia la partita: prima ondata. Lo chiama il menu su "Gioca".</summary>
        public void BeginRun()
        {
            if (Coop.WaitingForHost) return;     // ospitato: la partita parte quando l'ospite lo saluta (BeginCoopRun)
            if (proc) { if (Proc.FloorDirector.I != null && Proc.FloorDirector.I.floor == 0) Proc.FloorDirector.I.StartRun(); return; }
            if (wave == 0 && !waveActive) StartWave(1);
        }

        /// <summary>Cooperativa, ospitato: l'ospite ha detto seme e piano, si parte da li' (e il menu si toglie di mezzo).</summary>
        public void BeginCoopRun(int seed, int floor)
        {
            GameMenu.I?.CoopEnter();
            if (proc && Proc.FloorDirector.I != null && Proc.FloorDirector.I.floor == 0) Proc.FloorDirector.I.StartRun(seed, floor);
        }

        /// <summary>Punti (tabella in Punti.cs): +10 per colpo che non uccide, 60 per uccisione, 100 alla testa, 130 col coltello.
        /// Spenderli al muro sottrae (n negativo). Con l'Offerta in corso i guadagni valgono il doppio.</summary>
        public void AddPoints(int n)
        {
            if (n > 0 && Drops.DoublePoints) n *= 2;
            score = Mathf.Max(0, score + n);
            hud?.SetScore(score);
            if (n > 0) hud?.PointFeed(n, Drops.DoublePoints);
        }

        static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs())
                if (s == a) return true;
            return false;
        }

        void Start()
        {
            if (autoTest && HasArg("-botmezz") && player != null) player.SetSpawn(MezzSpot);
            hud?.SetScore(score);
            if (player != null && player.weapon != null)
            {
                player.weapon.Refresh();
            }
            if (GameMenu.I == null) BeginRun();
        }

        void Update()
        {
            if (!proc && !waveActive && wave > 0)
            {
                waveBreak -= Time.deltaTime;
                if (waveBreak <= 0f) StartWave(wave + 1);
            }
            if (autoTest)
            {
                if (HasArg("-leantest") && !leanTestStarted && autoT > 2f) { leanTestStarted = true; StartCoroutine(LeanTest()); }
                if (gunTest && !gunTestStarted && autoT > 2f) { gunTestStarted = true; StartCoroutine(GunTest()); }
                if (!wallTestDone && HasArg("-walltest") && autoT > 3f)
                {
                    wallTestDone = true;
                    StartCoroutine(WallTest());
                }
                if (!magicTestDone && HasArg("-magictest") && autoT > 1f)
                {
                    magicTestDone = true;
                    var monaco = Enemy.Spawn(Enemy.EnemyType.Monaco, new Vector3(0f, 0.05f, 6f));
                    if (monaco != null) monaco.hp = monaco.maxHp = 3000f;   // non muore prima di lanciare
                    Debug.Log("[PryingEyes] magictest: monaco piazzato a 13 m");
                }
                if (!enemyShotDone && HasArg("-enemyshot") && autoT > 1f)
                {
                    enemyShotDone = true;
                    var abate = Enemy.Spawn(Enemy.EnemyType.Abate, new Vector3(-1.6f, 0.05f, 8f));
                    if (abate != null) abate.hp = abate.maxHp = 5000f;
                    var occhio = Enemy.Spawn(Enemy.EnemyType.Occhio, new Vector3(1.6f, 2.4f, 8f));
                    if (occhio != null) occhio.hp = occhio.maxHp = 5000f;
                    StartCoroutine(EnemyShot());
                }
                autoT += Time.deltaTime;
                reportT += Time.deltaTime;
                foreach (var e in Enemy.All)
                    if (e != null && !Enemy.IsFlying(e.type)) walkerMaxY = Mathf.Max(walkerMaxY, e.transform.position.y);
                if (reportT >= 5f)
                {
                    reportT = 0f;
                    var list = new System.Collections.Generic.List<Enemy>(FindObjectsByType<Enemy>(FindObjectsSortMode.None));
                    string epos = "";
                    for (int i = 0; i < Mathf.Min(3, list.Count); i++)
                        epos += $" e{i}({list[i].transform.position.x:F0},{list[i].transform.position.y:F0},{list[i].transform.position.z:F0})";
                    System.IO.File.AppendAllText(REPORT,
                        $"{autoT:F0} {wave} {kills} {score} {player.hp:F0} {player.shield:F0} {player.weapon.mag} {player.weapon.reserve} {list.Count} {shotsFired} {shotsHit} {damageTaken} {walkerMaxY:F1}" +
                        $" p({player.transform.position.x:F0},{player.transform.position.y:F0},{player.transform.position.z:F0}){epos}\n");
                }
                if (autoT >= 15f && !shot1done) { shot1done = true; ScreenCapture.CaptureScreenshot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_shot1.png"); }
                if (autoT >= 75f && !shot2done) { shot2done = true; ScreenCapture.CaptureScreenshot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_shot2.png"); }
                if (camShots && autoT >= 8f && !wallShotDone) { wallShotDone = true; WallShot(); }
                if (camShots && autoT >= 20f && !cam1done) { cam1done = true; CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_cam1.png"); }
                if (camShots && autoT >= 60f && !cam2done) { cam2done = true; CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_cam2.png"); }
                bool dedicated = HasArg("-architecturetest") || HasArg("-flyingmeleetest") || HasArg("-meleetest") ||
                    HasArg("-relictest") || HasArg("-combatfeedbacktest") || HasArg("-torchreview") || HasArg("-survivaltest") || HasArg("-citytest");
                if (autoT >= (proc ? (dedicated || HasArg("-procbot") || HasArg("-stealthtest") || HasArg("-perftest") || gunTest || HasArg("-leantest") || HasArg("-cooptest") ? 1e9f : 70f) : (HasArg("-citytest") ? 1e9f : 100f)))
                {
                    System.IO.File.AppendAllText(REPORT, $"flicker spegnimenti={Flicker.dropouts} occhi chiusi={WatchingEyes.closings}\n");
                    Application.Quit();
                }
            }
        }

        /// <summary>Foto su RenderTexture. Con l'HUD: il canvas passa per un attimo in ScreenSpaceCamera, cosi' entra
        /// nella foto (in Overlay non si vede dentro una RenderTexture).</summary>
        public static void CamShot(string path, Camera cam = null, bool withHud = true, int width = 1280, int height = 720)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[PryingEyes] camshot: nessuna camera"); return; }
            int W = width, H = height;
            var rt = RenderTexture.GetTemporary(W, H, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas hudCanvas = withHud && I != null && I.hud != null ? I.hud.GetComponent<Canvas>() : null;
            if (hudCanvas != null)
            {
                hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                hudCanvas.worldCamera = cam;
                hudCanvas.planeDistance = 0.2f;
                Canvas.ForceUpdateCanvases();
            }
            cam.Render();
            if (hudCanvas != null)
            {
                hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                hudCanvas.worldCamera = null;
            }
            cam.targetTexture = prev;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[PryingEyes] camshot " + path);
        }

        /// <summary>Foto di verifica dell'espositore VESPER-9 sul muro sud (usata con -camshots).</summary>
        void WallShot()
        {
            var main = Camera.main;
            if (main == null) return;
            var go = new GameObject("FotoArmiMuro");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            var src = main.GetComponent<UniversalAdditionalCameraData>();
            var dst = cam.GetUniversalAdditionalCameraData();
            if (src != null) { dst.renderPostProcessing = src.renderPostProcessing; dst.antialiasing = src.antialiasing; }
            cam.enabled = false;
            cam.transform.position = new Vector3(-10f, 1.55f, 20.15f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(-10f, 1.45f, 22.42f) - cam.transform.position);
            CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/wallbuy.png", cam, true);
            Destroy(go);
        }

        /// <summary>Foto di controllo dei nemici meccanici (usata con -enemyshot).</summary>
        System.Collections.IEnumerator EnemyShot()
        {
            yield return new WaitForSeconds(0.5f);
            var main = Camera.main;
            if (main == null) yield break;
            var go = new GameObject("FotoNemici");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            var src = main.GetComponent<UniversalAdditionalCameraData>();
            var dst = cam.GetUniversalAdditionalCameraData();
            if (src != null) { dst.renderPostProcessing = src.renderPostProcessing; dst.antialiasing = src.antialiasing; }
            cam.enabled = false;
            cam.transform.position = new Vector3(0f, 1.65f, 13f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.25f, 8f) - cam.transform.position);
            CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/enemies.png", cam, false);
            Destroy(go);
        }

        float lookOpenDist;

        /// <summary>Per le prove: ferma la rotazione automatica dell'autotest e guarda dove c'e' piu' spazio (foto con profondita',
        /// niente muro a 30 cm, e il muro di prova del lean resta dove l'ho messo).</summary>
        void LookOpenest()
        {
            PlayerController.holdLook = true;
            var c = player.cam;
            float bestD = 0f, bestA = 0f;
            for (int a = 0; a < 24; a++)
            {
                float ang = a * Mathf.PI * 2f / 24f;
                var dv = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                float dd = 8f;
                if (Physics.Raycast(c.position, dv, out RaycastHit rh, 8f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) dd = rh.distance;
                if (dd > bestD) { bestD = dd; bestA = ang; }
            }
            player.yaw = bestA; player.pitch = 0f;
            lookOpenDist = bestD;
        }

        /// <summary>Prova del lean (-leantest): nel piccolo ascensore di arrivo, lean a destra e a sinistra con foto e numeri:
        /// spostamento della testa, inclinazione, punto del corpo visto dai nemici e spazio concesso dai muri.</summary>
        System.Collections.IEnumerator LeanTest()
        {
            string dir = "C:/Users/danie/Desktop/prying-eyes/hotel_build/armi_gioco";
            System.IO.Directory.CreateDirectory(dir);
            Enemy.ClearAll();
            player.invulnerable = true;
            player.weapon.Equip(WeaponDefs.IndexOf("ar9_malleus"));
            var cam = player.cam;
            LookOpenest();
            yield return new WaitForSeconds(1.2f);
            foreach (float l in new[] { 0f, 1f, -1f, 1f })
            {
                PlayerController.debugLean = l;
                yield return new WaitForSeconds(1.0f);
                Vector3 off = cam.position - (player.transform.position + Vector3.up * 1.62f);
                float lateral = Vector3.Dot(off, new Vector3(Mathf.Cos(player.yaw), 0f, -Mathf.Sin(player.yaw)));
                float roll = cam.eulerAngles.z; if (roll > 180f) roll -= 360f;
                Vector3 head = player.LeanedPoint(1.58f), hip = player.LeanedPoint(0.9f);
                string msg = $"lean={l:F0} reale={player.lean:F2} testa laterale={lateral:F2} m quota={off.y:F3} m roll={roll:F1} gradi spazio_dx={player.LeanRoomPublic(1f):F2} spazio_sx={player.LeanRoomPublic(-1f):F2} " +
                             $"testa_per_i_nemici={(head - player.transform.position - Vector3.up * 1.58f).magnitude:F2} m bacino_per_i_nemici={(hip - player.transform.position - Vector3.up * 0.9f).magnitude:F2} m";
                Debug.Log("[PryingEyes] leantest " + msg);
                System.IO.File.AppendAllText(dir + "/leantest.txt", msg + "\n");
                CamShot(dir + "/lean_" + (l == 0f ? "centro" : l > 0f ? "destra" : "sinistra") + ".png", null, true);
            }
            // con un muro a 33 cm sulla destra la testa non puo' sporgere piu' di tanto
            var right = new Vector3(Mathf.Cos(player.yaw), 0f, -Mathf.Sin(player.yaw));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "MuroProvaLean";
            wall.transform.position = player.transform.position + right * 0.38f + Vector3.up * 1.5f;
            wall.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Sin(player.yaw), 0f, Mathf.Cos(player.yaw)));
            wall.transform.localScale = new Vector3(0.10f, 3f, 3f);
            wall.transform.rotation = Quaternion.LookRotation(right) * Quaternion.Euler(0f, 90f, 0f);
            PlayerController.debugLean = 1f;
            yield return new WaitForSeconds(1.2f);
            string m2 = $"muro a destra: lean reale={player.lean:F2} spazio_dx={player.LeanRoomPublic(1f):F2}";
            Debug.Log("[PryingEyes] leantest " + m2);
            System.IO.File.AppendAllText(dir + "/leantest.txt", m2 + "\n");
            CamShot(dir + "/lean_muro.png", null, true);
            Destroy(wall);
            PlayerController.debugLean = float.NaN;
            Application.Quit();
        }

        /// <summary>Prova delle armi (-guntest [-armi id1,id2]): per ognuna foto in mano, in mira, allo sparo e a meta' ricarica,
        /// piu' i numeri misurati (dispersione, rinculo, cadenza). Foto in hotel_build/armi_gioco/, numeri in guntest.txt.</summary>
        System.Collections.IEnumerator GunTest()
        {
            string dir = "C:/Users/danie/Desktop/prying-eyes/hotel_build/armi_gioco";
            System.IO.Directory.CreateDirectory(dir);
            string log = dir + "/guntest.txt";
            System.IO.File.WriteAllText(log, "id grado classe colpi_in_4s rpm_misurata kick_max_gradi dispersione_mano dispersione_mira\n");
            string only = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-armi") only = args[i + 1];
            Enemy.ClearAll();
            var w = player.weapon;
            var cam = player.cam;
            Cursor.lockState = CursorLockMode.None;
            LookOpenest();   // tutte le foto guardano lungo la linea piu' libera: tracce, fiamme e raggi hanno spazio per vedersi
            for (int idx = 0; idx < WeaponDefs.All.Length; idx++)
            {
                var d = WeaponDefs.All[idx];
                if (only != null && System.Array.IndexOf(only.Split(','), d.id) < 0) continue;
                Weapon.debugAds = false;
                w.Equip(idx);
                player.invulnerable = true;
                // il goccia a goccia dei nemici riempie la scena mentre si prova: fuori tutti prima di ogni arma (le foto devono vedere l'arma)
                Enemy.ClearAll();
                yield return new WaitForSeconds(0.9f);
                CamShot(dir + "/" + d.id + "_mano.png", null, true);
                // in mira
                Weapon.debugAds = true;
                yield return new WaitForSeconds(Mathf.Max(0.8f, d.adsTime * 4f));
                w.RenderScopeNow();
                CamShot(dir + "/" + d.id + "_mira.png", null, true);
                // allo sparo: fiammata, bossolo, traccia
                Weapon.debugAds = false;
                Enemy.ClearAll();
                yield return new WaitForSeconds(0.5f);
                w.mag = d.mag;
                w.DebugFire();
                yield return null;   // un fotogramma: tracce e raggi nascono orientati solo al primo aggiornamento di Fx
                if (d.fx == "flame")
                {
                    // il getto di fiamme si allunga solo dopo qualche colpo: grilletto tenuto per un terzo di secondo prima della foto
                    float tf = Time.time;
                    while (Time.time - tf < 0.35f) { if (w.CanFireNow) w.DebugFire(); yield return null; }
                }
                if (d.fx == "flame" || d.beam) Debug.Log("[PryingEyes] fxdump " + d.id + " " + Fx.Dump());
                CamShot(dir + "/" + d.id + "_sparo.png", null, true);
                yield return new WaitForSeconds(0.4f);
                // ricarica a meta'
                w.mag = Mathf.Max(0, d.mag / 3);
                w.Reload();
                yield return new WaitForSeconds(Mathf.Max(0.3f, d.style == ReloadStyle.Tube ? 0.7f : d.reload * 0.4f));
                CamShot(dir + "/" + d.id + "_ricarica.png", null, true);
                yield return new WaitForSeconds(d.reloadEmpty + d.shellTime * 8f + 1f);
                // misura: grilletto tenuto per 4 s con caricatore senza fondo; cadenza = colpi tra il primo e l'ultimo
                w.mag = 999; w.reserve = 999;
                int shots0 = shotsFired;
                float t0 = Time.time, tFirst = -1f, tLast = -1f;
                int lastCount = shots0;
                float maxKick = 0f;
                while (Time.time - t0 < 4f)
                {
                    if (w.CanFireNow) w.DebugFire();
                    if (shotsFired != lastCount) { lastCount = shotsFired; if (tFirst < 0f) tFirst = Time.time; tLast = Time.time; }
                    maxKick = Mathf.Max(maxKick, player.ViewKickPitch);
                    yield return null;
                }
                int fired = shotsFired - shots0;
                float rpmMeasured = fired > 1 && tLast > tFirst ? (fired - 1) / (tLast - tFirst) * 60f : 0f;
                System.IO.File.AppendAllText(log, $"{d.id} {d.grade} {d.cls.Replace(' ', '_')} {fired} {rpmMeasured:F0} {maxKick:F1} {d.hip:F2} {d.ads:F2}\n");
                Debug.Log($"[PryingEyes] guntest {d.id}: {fired} colpi in 4 s ({rpmMeasured:F0} al minuto, scheda {d.rpm:F0}), kick massimo {maxKick:F1} gradi");
                yield return new WaitForSeconds(0.6f);
            }
            Weapon.debugAds = false;
            if (HasArg("-fxtest"))
            {
                // prova degli effetti a quad (fiamma, raggio, traccia) senza arma: foto subito dopo la nascita
                Enemy.ClearAll();
                Vector3 o = cam.position + cam.forward * 0.9f + Vector3.down * 0.25f + cam.right * 0.15f;
                for (int pass = 0; pass < 3; pass++)
                {
                    Fx.Clear();
                    if (pass == 0) for (int i = 0; i < 8; i++) Fx.Flame(o + cam.forward * (i * 0.15f), cam.forward);
                    if (pass == 1) Fx.Beam(o, o + cam.forward * 8f, 0.04f, new Color(1f, 0.6f, 0.1f));
                    if (pass == 2) Fx.Tracer(o + cam.right * 0.3f, o + cam.right * 0.3f + cam.forward * 20f, 30f, 0.03f, new Color(1f, 0.9f, 0.4f));
                    yield return null; yield return null;
                    CamShot(dir + "/fxtest" + pass + ".png", null, true);
                    Debug.Log("[PryingEyes] fxtest" + pass + " " + Fx.Dump());
                }
            }
            // espositori: tre armi sul muro davanti al giocatore (come i muri dei piani); con -armi solo se c'e' anche -espositori
            if (only == null || HasArg("-espositori"))
            {
                // i pannelli non devono finire dentro un muro: si guarda dove c'e' piu' spazio
                Enemy.ClearAll();
                LookOpenest();
                yield return null; yield return null;
                var fwd = new Vector3(cam.forward.x, 0f, cam.forward.z).normalized;
                float place = Mathf.Clamp(lookOpenDist - 0.5f, 1.3f, 2.4f);
                float spread = place < 2f ? 1.25f : 1.55f;
                string[] ids3 = { "p9_pieta", "ar9_malleus", "pu_pupilla" };
                for (int k = 0; k < 3; k++)
                {
                    int wi = WeaponDefs.IndexOf(ids3[k]);
                    Vector3 pos = cam.position + fwd * place + cam.right * ((k - 1) * spread);
                    pos.y = cam.position.y - 0.05f;
                    WallBuy.Place(wi, WeaponDefs.All[wi].price, pos, Quaternion.LookRotation(-fwd), WeaponDefs.All[wi].GradeColor);
                }
                yield return new WaitForSeconds(0.6f);
                CamShot(dir + "/espositori.png", null, true);
            }
            Debug.Log("[PryingEyes] guntest finito");
            Application.Quit();
        }

        /// <summary>Test automatico (-walltest): si mette davanti alla VESPER-9 e verifica prompt,
        /// acquisto con F, spesa dei punti e ricarica della riserva.</summary>
        System.Collections.IEnumerator WallTest()
        {
            if (player == null || player.weapon == null) yield break;
            // in autotest la camera del giocatore punta ai nemici: uso una sonda fissa davanti alla VESPER
            var probe = new GameObject("WallTestProbe");
            probe.transform.position = new Vector3(-10f, 1.55f, 20.2f);
            probe.transform.rotation = Quaternion.LookRotation(new Vector3(-10f, 1.55f, 22.42f) - probe.transform.position);
            yield return null;
            WallBuy.Tick(probe.transform);
            Debug.Log("[PryingEyes] walltest prompt=\"" + (hud != null ? hud.CurrentPrompt : "") + "\" score=" + score);
            WallBuy.TryBuy();
            Debug.Log("[PryingEyes] walltest dopo acquisto: score=" + score + " arma=" + player.weapon.Def.name
                + " mag=" + player.weapon.mag + " riserva=" + player.weapon.reserve
                + " hud=" + (hud != null ? hud.CurrentAmmo + " " + hud.CurrentReserve : ""));
            yield return null;
            AddPoints(300);
            player.weapon.reserve = 5;
            WallBuy.Tick(probe.transform);
            Debug.Log("[PryingEyes] walltest prompt munizioni=\"" + (hud != null ? hud.CurrentPrompt : "") + "\"");
            WallBuy.TryBuy();
            Debug.Log("[PryingEyes] walltest dopo munizioni: score=" + score
                + " riserva=" + player.weapon.reserve + "/" + player.weapon.reserveMax
                + " hud=" + (hud != null ? hud.CurrentAmmo + " " + hud.CurrentReserve : ""));

            // animazione di ricarica: foto a metà e controllo che il contatore si aggiorni
            yield return new WaitForSeconds(0.25f);
            player.weapon.mag = 3;
            player.weapon.Reload();
            yield return new WaitForSeconds(0.70f);
            CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/reload.png", Camera.main, true);
            Debug.Log("[PryingEyes] walltest hud durante ricarica=" + hud.CurrentAmmo + " " + hud.CurrentReserve);
            yield return new WaitForSeconds(1.20f);
            Debug.Log("[PryingEyes] walltest hud dopo ricarica=" + hud.CurrentAmmo + " " + hud.CurrentReserve);
            Destroy(probe);
            Application.Quit();
        }

        bool liftShotDone;

        /// <summary>Chiamato dall'ascensore quando le porte sono aperte: con -camshots, la prima volta, una foto da
        /// un punto fisso che lo inquadra (il monaco sta uscendo).</summary>
        public void OnLiftOpen()
        {
            if (!camShots || liftShotDone) return;
            liftShotDone = true;
            StartCoroutine(LiftShot());
        }

        System.Collections.IEnumerator LiftShot()
        {
            yield return new WaitForSeconds(1.4f);
            var go = new GameObject("FotoAscensore");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) cam.CopyFrom(main);
            var src = main != null ? main.GetComponent<UniversalAdditionalCameraData>() : null;
            var dst = cam.GetUniversalAdditionalCameraData();
            if (src != null) { dst.renderPostProcessing = src.renderPostProcessing; dst.antialiasing = src.antialiasing; }
            cam.enabled = false;
            go.transform.position = new Vector3(-27.5f, 1.7f, 3.5f);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(-36.5f, 1.2f, 0f) - go.transform.position);
            CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_cam_ascensore.png", cam, false);
            Destroy(go);
        }

        public void StartWave(int n)
        {
            if (waveActive) return;
            wave = n;
            waveActive = true;
            var city = FindFirstObjectByType<CityDirector>();
            int count = city != null ? city.survival.Count(n) : Mathf.Min(28, 4 + n * 2);
            // chi deve ancora arrivare conta gia': l'ondata non finisce prima che siano entrati tutti
            alive = count;
            hud?.Banner("Ondata " + HUD.Roman(n), FindFirstObjectByType<CityDirector>() != null
                ? CityWaveLines[(n - 1) % CityWaveLines.Length] : WaveLines[(n - 1) % WaveLines.Length]);
            hud?.SetWave(n);
            hud?.SetEnemiesLeft(alive);
            StartCoroutine(SpawnWave(n, count));
        }

        static readonly string[] WaveLines =
        {
            "Il campanello della reception suona. Al banco non c'è nessuno.",
            "L'ascensore scende. Nessuno l'ha chiamato.",
            "Dal soppalco qualcuno ti guarda.",
            "Il grammofono non si è mai fermato.",
            "Le luci tremano. Stanno arrivando.",
            "Il Grand Hotel non chiude mai.",
            "Stanotte ogni stanza è occupata.",
            "Gli ospiti ti stavano aspettando.",
        };
        static readonly string[] CityWaveLines =
        {
            "La strada e' deserta. Senti passi dietro l'angolo.",
            "Qualcuno bussa alle vetrine chiuse.",
            "Dalle traverse arrivano altri infetti.",
            "Il quartiere non e' mai stato evacuato.",
        };
        static readonly Vector3 BellPos = new Vector3(24f, 1.3f, 2f);

        /// <summary>Messa in scena dell'ondata: il campanello della reception suona da solo, l'ascensore arriva ed esce
        /// il primo monaco, gli altri entrano scaglionati dagli altri ingressi.</summary>
        System.Collections.IEnumerator SpawnWave(int n, int count)
        {
            var city = FindFirstObjectByType<CityDirector>();
            if (city != null)
            {
                yield return city.RunWave(this, n);
                yield break;
            }
            Sfx.At(SoundBank.Bell, BellPos, 0.8f, 1f, 3f, 50f);
            var bell = GameObject.Find("Bell");
            if (bell != null) StartCoroutine(Jiggle(bell.transform));
            yield return new WaitForSeconds(0.8f);
            var lift = Elevator.I;
            bool byLift = lift != null && lift.Arrive(() => Enemy.Spawn(Enemy.EnemyType.Monaco, lift.spawn.position));
            if (!byLift) Enemy.Spawn(Enemy.EnemyType.Monaco, PickSpawn(Enemy.EnemyType.Monaco));
            for (int k = 1; k < count; k++)
            {
                // ritmo lento da orda: i nemici entrano scaglionati, i camminatori sono la maggioranza
                yield return new WaitForSeconds(Random.Range(0.6f, 1.4f));
                bool flying = Random.value < Mathf.Min(0.5f, 0.22f + n * 0.03f);
                var type = flying ? Enemy.EnemyType.Occhio
                    : Random.value < 0.68f ? Enemy.EnemyType.Monaco : Enemy.EnemyType.Abate;   // l'abate e' raro: un bersaglio da 280 colpi di vita
                Enemy.Spawn(type, PickSpawn(type));
            }
        }

        static System.Collections.IEnumerator Jiggle(Transform t)
        {
            Vector3 s0 = t.localScale, p0 = t.position;
            for (float k = 0f; k < 0.6f; k += Time.deltaTime)
            {
                float w = Mathf.Sin(k * 60f) * Mathf.Exp(-k * 7f);
                t.localScale = s0 * (1f + 0.12f * w);
                t.position = p0 + Vector3.up * 0.015f * Mathf.Abs(w);
                yield return null;
            }
            t.localScale = s0;
            t.position = p0;
        }

        public Vector3 PickSpawn(Enemy.EnemyType type)
        {
            for (int j = 0; j < spawnPoints.Length; j++)
            {
                Vector3 p = spawnPoints[(Random.Range(0, 1000) + j) % spawnPoints.Length].position;
                if (type != Enemy.EnemyType.Occhio && p.y > 1f) continue;   // chi cammina nasce a terra
                if (player != null && Vector2.Distance(new Vector2(p.x, p.z),
                    new Vector2(player.transform.position.x, player.transform.position.z)) < 10f) continue;
                p.x += Random.Range(-1.5f, 1.5f);
                p.z += Random.Range(-1.5f, 1.5f);
                if (type == Enemy.EnemyType.Occhio) p.y = Mathf.Max(3f, p.y + 1f);
                return p;
            }
            Vector3 fallback = spawnPoints[0].position;
            if (type == Enemy.EnemyType.Occhio) fallback.y = 3f;
            return fallback;
        }

        /// <summary>Un'uccisione a mio nome: il conteggio, i punti (COD Zombies: 60, 100 se letale alla testa, 130 col coltello) e la cura
        /// di Sete di sangue (il coltello cura: poco in faccia, molto alle spalle). In cooperativa arriva anche dall'ospite per le uccisioni
        /// dei compagni (CoopKind.KillCredit).</summary>
        public void CreditKill(DamageSource src, bool headshot)
        {
            kills++;
            AddPoints(Punti.PerUccisione(src, headshot));
            float heal = Perks.BloodHeal(src);
            if (heal > 0f && player != null && player.alive)
            {
                player.Heal(heal);
                hud?.Flash(Perks.Of(PerkId.SeteDiSangue).color, 0.35f, 0.18f);
            }
        }

        /// <summary>'claimer' e' lo slot di chi ha dato il colpo che ha ucciso (solo in cooperativa, sull'ospite): 0 sono io.</summary>
        public void OnEnemyKilled(Enemy e, bool headshot, DamageSource src = DamageSource.Gun, int claimer = 0)
        {
            // COD Zombies: 60 punti per uccisione, 100 se letale alla testa, 130 col coltello (i +10 dei colpi arrivano da Damage)
            int pts = Punti.PerUccisione(src, headshot);
            if (proc)
            {
                if (claimer == 0) CreditKill(src, headshot);
                else Coop.Game?.KillCredit(claimer, src, headshot);
                Drops.OnKill(e, src);
                return;
            }
            alive--;
            kills++;
            AddPoints(pts);
            if (alive <= 0 && waveActive && FindFirstObjectByType<CityDirector>() == null) CompleteWave(8f);
        }

        public void SetWaveRemaining(int remaining)
        {
            alive = remaining;
            hud?.SetEnemiesLeft(alive);
        }

        public void CompleteWave(float intermission)
        {
            if (waveActive)
            {
                waveActive = false;
                waveBreak = intermission;
                if (player != null)
                {
                    player.shield = 50f;
                    player.weapon.AddAmmoFraction(0.25f);
                }
                hud?.Toast("Silenzio, per ora. Scudo e riserva parzialmente ricaricati.");
            }
        }

        /// <summary>Presenze ancora da affrontare nell'ondata (comprese quelle non ancora entrate).</summary>
        public void SetAlive(int existing)
        {
            if (proc) { hud?.SetEnemiesLeft(Enemy.All.Count); return; }
            hud?.SetEnemiesLeft(alive);
        }
    }
}
