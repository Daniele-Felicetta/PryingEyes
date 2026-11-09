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
        public static int shotsFired = 0, shotsHit = 0, damageTaken = 0;
        float autoT = 0f, reportT = 0f, walkerMaxY = 0f;
        // -botmezz: il bot gioca dal soppalco (y 4.2), dove i nemici colpivano attraverso il pavimento
        static readonly Vector3 MezzSpot = new Vector3(-20f, 4.3f, -17f);
        bool shot1done = false, shot2done = false;
        // -camshots: foto dalla camera del giocatore su RenderTexture, funziona anche con -batchmode senza finestra
        // (ScreenCapture li' non ha uno schermo da copiare). Niente HUD: serve a controllare luce e materiali.
        bool camShots, cam1done, cam2done, wallShotDone;
        bool wallTestDone, magicTestDone, enemyShotDone;
        const string REPORT = "C:/Users/danie/Desktop/prying-eyes/hotel_build/autotest.txt";

        void Awake()
        {
            I = this;
            // i valori serializzati nella scena costruita dall'editor hanno la precedenza sui default:
            // qui riallineo la valuta di partenza al ritmo COD Zombies
            score = 500;
            autoTest = HasArg("-autotest");
            if (autoTest && HasArg("-procbot")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }
            camShots = HasArg("-camshots");
            if (autoTest)
                System.IO.File.WriteAllText(REPORT, "t wave kills score hp shield mag reserve enemies shots hits colpiSubiti yMaxCamminatori\n");
            proc = FindFirstObjectByType<Proc.FloorDirector>() != null;
            if (!proc)
            {
                HotelNav.Build();   // prima di qualunque nemico: i collider della scena ci sono gia'
                WallBuy.BuildAll(); // armi sui muri (VESPER-9, BREACHER-12, LANCE-7)
                Ambience.Create();  // eco, vento, scricchiolii, grammofono; genera anche tutti i suoni
            }
            else Proc.ProcAmbience.Create();
            if (!autoTest && !camShots) GameMenu.Create();   // menu principale: la prima ondata parte da "Gioca"
        }

        /// <summary>Avvia la partita: prima ondata. Lo chiama il menu su "Gioca".</summary>
        public void BeginRun()
        {
            if (proc) { if (Proc.FloorDirector.I != null && Proc.FloorDirector.I.floor == 0) Proc.FloorDirector.I.StartRun(); return; }
            if (wave == 0 && !waveActive) StartWave(1);
        }

        /// <summary>Punti: +10 per colpo a segno, +50 per uccisione, +100 se letale alla testa.
        /// Spenderli al muro sottrae (n negativo).</summary>
        public void AddPoints(int n)
        {
            score = Mathf.Max(0, score + n);
            hud?.SetScore(score);
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
                var d = player.weapon.Def;
                hud?.SetAmmo(player.weapon.mag, player.weapon.reserve);
                hud?.SetWeaponName(d.name + " · " + d.modeLabel);
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
                    if (e != null && e.type != Enemy.EnemyType.Occhio) walkerMaxY = Mathf.Max(walkerMaxY, e.transform.position.y);
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
                if (autoT >= (proc ? (HasArg("-procbot") ? 1e9f : 70f) : 100f))
                {
                    System.IO.File.AppendAllText(REPORT, $"flicker spegnimenti={Flicker.dropouts} occhi chiusi={WatchingEyes.closings}\n");
                    Application.Quit();
                }
            }
        }

        /// <summary>Foto su RenderTexture. Con l'HUD: il canvas passa per un attimo in ScreenSpaceCamera, cosi' entra
        /// nella foto (in Overlay non si vede dentro una RenderTexture).</summary>
        public static void CamShot(string path, Camera cam = null, bool withHud = true)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[PryingEyes] camshot: nessuna camera"); return; }
            const int W = 1280, H = 720;
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
            int count = Mathf.Min(28, 4 + n * 2);
            // chi deve ancora arrivare conta gia': l'ondata non finisce prima che siano entrati tutti
            alive = count;
            hud?.Banner("Ondata " + HUD.Roman(n), WaveLines[(n - 1) % WaveLines.Length]);
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
        static readonly Vector3 BellPos = new Vector3(24f, 1.3f, 2f);

        /// <summary>Messa in scena dell'ondata: il campanello della reception suona da solo, l'ascensore arriva ed esce
        /// il primo monaco, gli altri entrano scaglionati dagli altri ingressi.</summary>
        System.Collections.IEnumerator SpawnWave(int n, int count)
        {
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

        Vector3 PickSpawn(Enemy.EnemyType type)
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

        public void OnEnemyKilled(Enemy e, bool headshot)
        {
            if (proc) { kills++; AddPoints(50 + (headshot ? 50 : 0)); return; }
            alive--;
            kills++;
            // COD Zombies: 50 punti per uccisione, 100 se letale alla testa (i +10 dei colpi arrivano da Damage)
            AddPoints(50 + (headshot ? 50 : 0));
            if (alive <= 0 && waveActive)
            {
                waveActive = false;
                waveBreak = 8f;
                if (player != null)
                {
                    player.shield = 50f;
                    player.weapon.reserve = Mathf.Min(player.weapon.reserveMax,
                        player.weapon.reserve + Mathf.CeilToInt(player.weapon.reserveMax * 0.25f));
                    hud?.SetAmmo(player.weapon.mag, player.weapon.reserve);
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
