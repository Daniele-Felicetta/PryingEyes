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

        void Awake()
        {
            I = this;
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
        }

        public void StartWave(int n)
        {
            if (waveActive) return;
            wave = n;
            waveActive = true;
            int count = 3 + n * 2;
            for (int k = 0; k < count; k++)
            {
                bool drone = Random.value < Mathf.Min(0.65f, 0.35f + n * 0.06f);
                Enemy.EnemyType type = drone ? Enemy.EnemyType.Drone : Enemy.EnemyType.Brute;
                Vector3 sp = PickSpawn(type);
                Enemy.Spawn(type, sp);
                alive++;
            }
            hud?.Banner("ONDATA " + n, count + " ostili in arrivo");
            hud?.SetWave(n);
        }

        Vector3 PickSpawn(Enemy.EnemyType type)
        {
            for (int j = 0; j < spawnPoints.Length; j++)
            {
                Vector3 p = spawnPoints[(Random.Range(0, 1000) + j) % spawnPoints.Length].position;
                if (type == Enemy.EnemyType.Brute && p.y > 1f) continue;
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
            int pts = (e.type == Enemy.EnemyType.Drone ? 100 : 150) + (headshot ? 50 : 0) + wave * 10;
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
