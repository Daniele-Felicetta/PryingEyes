using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PryingEyes
{
    /// <summary>Wave manager, score, spawn points, player respawn. Port of web/UE design 1:1.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I;

        [Header("Waves")]
        public int wave = 0;
        public int score = 0;
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
        bool camShots, cam1done, cam2done;
        const string REPORT = "C:/Users/danie/Desktop/prying-eyes/hotel_build/autotest.txt";

        void Awake()
        {
            I = this;
            autoTest = HasArg("-autotest");
            camShots = HasArg("-camshots");
            if (autoTest)
                System.IO.File.WriteAllText(REPORT, "t wave kills score hp shield mag reserve enemies shots hits colpiSubiti yMaxCamminatori\n");
            HotelNav.Build();   // prima di qualunque nemico: i collider della scena ci sono gia'
            Ambience.Create();  // eco, vento, scricchiolii, grammofono; genera anche tutti i suoni
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
            StartWave(1);
        }

        void Update()
        {
            if (!waveActive)
            {
                waveBreak -= Time.deltaTime;
                if (waveBreak <= 0f) StartWave(wave + 1);
            }
            if (autoTest)
            {
                autoT += Time.deltaTime;
                reportT += Time.deltaTime;
                foreach (var e in Enemy.All)
                    if (e != null && e.type != Enemy.EnemyType.Drone) walkerMaxY = Mathf.Max(walkerMaxY, e.transform.position.y);
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
                if (camShots && autoT >= 20f && !cam1done) { cam1done = true; CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_cam1.png"); }
                if (camShots && autoT >= 60f && !cam2done) { cam2done = true; CamShot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_cam2.png"); }
                if (autoT >= 100f)
                {
                    System.IO.File.AppendAllText(REPORT, $"flicker spegnimenti={Flicker.dropouts} occhi chiusi={WatchingEyes.closings}\n");
                    Application.Quit();
                }
            }
        }

        /// <summary>Foto su RenderTexture. Con l'HUD: il canvas passa per un attimo in ScreenSpaceCamera, cosi' entra
        /// nella foto (in Overlay non si vede dentro una RenderTexture).</summary>
        static void CamShot(string path, Camera cam = null, bool withHud = true)
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
            int count = 3 + n * 2;
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
                yield return new WaitForSeconds(Random.Range(0.35f, 0.9f));
                bool drone = Random.value < Mathf.Min(0.65f, 0.35f + n * 0.06f);
                var type = drone ? Enemy.EnemyType.Drone
                    : Random.value < 0.35f ? Enemy.EnemyType.Monaco : Enemy.EnemyType.Brute;
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
                if (type != Enemy.EnemyType.Drone && p.y > 1f) continue;   // chi cammina nasce a terra
                if (player != null && Vector2.Distance(new Vector2(p.x, p.z),
                    new Vector2(player.transform.position.x, player.transform.position.z)) < 10f) continue;
                p.x += Random.Range(-1.5f, 1.5f);
                p.z += Random.Range(-1.5f, 1.5f);
                if (type == Enemy.EnemyType.Drone) p.y = Mathf.Max(3f, p.y + 1f);
                return p;
            }
            Vector3 fallback = spawnPoints[0].position;
            if (type == Enemy.EnemyType.Drone) fallback.y = 3f;
            return fallback;
        }

        public void OnEnemyKilled(Enemy e, bool headshot)
        {
            alive--;
            kills++;
            int pts = (e.type == Enemy.EnemyType.Drone ? 100 : e.type == Enemy.EnemyType.Brute ? 150 : 200)
                      + (headshot ? 50 : 0) + wave * 10;
            score += pts;
            hud?.SetScore(score);
            if (alive <= 0 && waveActive)
            {
                waveActive = false;
                waveBreak = 6f;
                if (player != null)
                {
                    player.shield = 50f;
                    player.weapon.reserve = Mathf.Min(240, player.weapon.reserve + 60);
                    hud?.SetAmmo(player.weapon.mag, player.weapon.reserve);
                }
                hud?.Toast("Silenzio, per ora. Munizioni e scudo ricaricati.");
            }
        }

        /// <summary>Presenze ancora da affrontare nell'ondata (comprese quelle non ancora entrate).</summary>
        public void SetAlive(int existing)
        {
            hud?.SetEnemiesLeft(alive);
        }
    }
}
