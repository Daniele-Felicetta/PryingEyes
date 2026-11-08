using UnityEngine;

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
        public static int shotsFired = 0, shotsHit = 0;
        float autoT = 0f, reportT = 0f;
        bool shot1done = false, shot2done = false;
        const string REPORT = "C:/Users/danie/Desktop/prying-eyes/hotel_build/autotest.txt";

        void Awake()
        {
            I = this;
            autoTest = HasArg("-autotest");
            if (autoTest)
                System.IO.File.WriteAllText(REPORT, "t wave kills score hp shield mag reserve enemies shots hits\n");
        }

        static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs())
                if (s == a) return true;
            return false;
        }

        void Start()
        {
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
                if (reportT >= 5f)
                {
                    reportT = 0f;
                    var list = new System.Collections.Generic.List<Enemy>(FindObjectsByType<Enemy>(FindObjectsSortMode.None));
                    string epos = "";
                    for (int i = 0; i < Mathf.Min(3, list.Count); i++)
                        epos += $" e{i}({list[i].transform.position.x:F0},{list[i].transform.position.y:F0},{list[i].transform.position.z:F0})";
                    System.IO.File.AppendAllText(REPORT,
                        $"{autoT:F0} {wave} {kills} {score} {player.hp:F0} {player.shield:F0} {player.weapon.mag} {player.weapon.reserve} {list.Count} {shotsFired} {shotsHit}" +
                        $" p({player.transform.position.x:F0},{player.transform.position.z:F0}){epos}\n");
                }
                if (autoT >= 15f && !shot1done) { shot1done = true; ScreenCapture.CaptureScreenshot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_shot1.png"); }
                if (autoT >= 75f && !shot2done) { shot2done = true; ScreenCapture.CaptureScreenshot("C:/Users/danie/Desktop/prying-eyes/hotel_build/auto_shot2.png"); }
                if (autoT >= 100f) Application.Quit();
            }
        }

        public void StartWave(int n)
        {
            if (waveActive) return;
            wave = n;
            waveActive = true;
            int count = 3 + n * 2;
            for (int k = 0; k < count; k++)
            {
                Enemy.EnemyType type;
                if (k == 0) type = Enemy.EnemyType.Monaco;            // almeno un monaco per ondata
                else
                {
                    bool drone = Random.value < Mathf.Min(0.65f, 0.35f + n * 0.06f);
                    type = drone ? Enemy.EnemyType.Drone
                        : Random.value < 0.35f ? Enemy.EnemyType.Monaco : Enemy.EnemyType.Brute;
                }
                Vector3 sp = PickSpawn(type);
                Enemy.Spawn(type, sp);
                alive++;
            }
            hud?.Banner("ONDATA " + n, count + " ostili in arrivo");
            hud?.SetWave(n);
            Synth.Wave();
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
            hud?.SetKills(kills);
            hud?.KillConfirm(headshot ? "HEADSHOT +" + pts : "+" + pts);
            if (alive <= 0 && waveActive)
            {
                waveActive = false;
                waveBreak = 5f;
                if (player != null)
                {
                    player.shield = 50f;
                    player.weapon.reserve = Mathf.Min(240, player.weapon.reserve + 60);
                }
                hud?.Toast("Ondata " + wave + " completata! +60 munizioni, scudo ripristinato");
            }
        }

        public void SetAlive(int n)
        {
            hud?.SetEnemiesLeft(n);
        }
    }
}
