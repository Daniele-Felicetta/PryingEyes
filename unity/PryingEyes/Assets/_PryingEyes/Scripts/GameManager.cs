using UnityEngine;
using UnityEngine.Rendering.Universal;
using PryingEyes.Testing;

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
        /// <summary>-nofire, o un test che lo chiede (<c>TestRun.HoldFire</c>): il bot di prova non spara da solo; senza, ogni sparo sveglia tutto il piano.</summary>
        public static bool noFire => noFireFlags || TestRun.HoldFire;
        static bool noFireFlags;
        public static int shotsFired = 0, shotsHit = 0, damageTaken = 0;
        /// <summary>Per il tabellone (HUD.Score.cs): uccisioni alla testa di questo giocatore e volte che e' morto.</summary>
        public static int headshots = 0, deaths = 0;
        float autoT = 0f, reportT = 0f, walkerMaxY = 0f;
        // -botmezz: il bot gioca dal soppalco (y 4.2), dove i nemici colpivano attraverso il pavimento
        static readonly Vector3 MezzSpot = new Vector3(-20f, 4.3f, -17f);
        bool shot1done = false, shot2done = false;
        /// <summary>Il fotogramma in cui la prova automatica ha scattato una foto da se' (auto_shot*, auto_cam*, WallShot): una foto costa 150-650 ms da sola, e le prove che misurano i fotogrammi (DesertTest) non devono contarla come un blocco del gioco.</summary>
        public static int HarnessShotFrame = -100;
        // -camshots: foto dalla camera del giocatore su RenderTexture, funziona anche con -batchmode senza finestra
        // (ScreenCapture li' non ha uno schermo da copiare). Niente HUD: serve a controllare luce e materiali.
        bool camShots, cam1done, cam2done, wallShotDone;
        bool enemyShotDone;
        /// <summary>Rapporto delle prove (-autotest). Con -report FILE ogni processo scrive il suo: piu' prove in parallelo non si pestano i piedi.</summary>
        static string REPORT => ReportPath();

        /// <summary>Il rapporto di -autotest. Scriverlo non deve mai fermare il gioco: nella prova in cooperativa piu' processi scrivono lo
        /// stesso file e Windows a volte rifiuta l'accesso (IOException 1224); prima l'Awake si interrompeva li', l'ospite non apriva la
        /// partita e l'ospitato restava senza collegamento (0.7.1).</summary>
        static void Report(string text, bool append = true)
        {
            try
            {
                if (append) System.IO.File.AppendAllText(REPORT, text);
                else System.IO.File.WriteAllText(REPORT, text);
            }
            catch (System.Exception ex)
            {
                if (!reportWarned) { reportWarned = true; Debug.LogWarning($"[PryingEyes] rapporto {REPORT} non scritto: {ex.Message}"); }
            }
        }
        static bool reportWarned;
        static string ReportPath()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-report") return a[i + 1];
            return TestPaths.Scratch("autotest.txt");
        }
        bool returningToMenu;

        /// <summary>Chiude la sessione locale e ricrea la scena iniziale: nessuno stato di una discesa entra nella successiva.</summary>
        public void ReturnToMenu()
        {
            if (returningToMenu) return;
            returningToMenu = true;
            Seats.EachOrOnce(_ => Cattedrale.Fabbrica.Chiudi(score));      // una spedizione abbandonata conta come persa (lo zaino); il gioco diretto ha gia' tutto in magazzino
            Characters.Progress.CommitTime();
            Characters.Roster.Flush();      // il menu riparte con tutto scritto su disco
            Coop.Stop("");
            Seats.End();                  // schermo condiviso: via i giocatori in piu' e le porzioni, resta il posto 0 (quello della scena)
            StopAllCoroutines();
            Proc.FloorDirector.I?.StopAllCoroutines();
            Proc.RoundDirector.ResetRun();
            Enemy.ClearAll();
            WallBuy.ClearAll();
            Proc.Interact.Clear();
            Proc.LightPool.Clear();
            Drops.ResetRun();
            Perks.ResetRun();
            Medals.Reset();
            Targets.Clear();
            Fx.Clear();
            Sfx.StopAll();
            TouchControls.ResetInput();
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        void Awake()
        {
            StartupTrace.Mark("scena caricata (GameManager.Awake)");
            SoundBank.PrefetchAll();      // i suoni si calcolano in thread a priorita' bassa: il menu compare senza aspettarli (SoundPrefetch.cs)
            I = this;
            shotsFired = shotsHit = damageTaken = 0;
            // i valori serializzati nella scena costruita dall'editor hanno la precedenza sui default:
            // qui riallineo la valuta di partenza al ritmo COD Zombies
            score = 500;
            autoTest = HasArg("-autotest") || HasArg("-guntest");
            noFireFlags = HasArg("-nofire") || HasArg("-survivaltest") || HasArg("-cattedraletest") || HasArg("-desertotest");
            camShots = HasArg("-camshots");
            if (autoTest)
                Report("t wave kills score hp shield mag reserve enemies shots hits colpiSubiti yMaxCamminatori\n", false);
            proc = FindFirstObjectByType<Proc.FloorDirector>() != null;
            GameSettings.BindBrightness(FindAnyObjectByType<UnityEngine.Rendering.Volume>());
            if (!proc)
            {
                FindFirstObjectByType<CityDirector>()?.PrepareAccess();
                HotelNav.Build();   // prima di qualunque nemico: i collider della scena ci sono gia'
                if (FindFirstObjectByType<CityDirector>() == null)
                    WallBuy.BuildAll(); // armi sui muri della hall classica
                Ambience.Create();  // eco, vento, scricchiolii, grammofono; genera anche tutti i suoni
            }
            else { Proc.ProcAmbience.Create(); MusicDirector.Create(); }     // ambiente dei piani e musica adattiva delle ondate
            if (proc) Coop.BootFromArgs();                   // prove con due processi: -coophost / -coopjoin <indirizzo>
            if (!autoTest && !camShots) GameMenu.Create();   // menu principale: la prima ondata parte da "Gioca"
            // controller: lo cerca il gioco e adotta il primo su cui si preme un tasto (nello schermo condiviso gli slot li assegna Seats.Begin; nelle prove no, salvo -padauto)
            if (!VideoSettings.Automated || HasArg("-padauto")) PadControls.EnableAuto();
            StartupTrace.Mark("GameManager.Awake finito");
        }

        /// <summary>Avvia la partita: prima ondata. Lo chiama il menu su "Gioca".</summary>
        public void BeginRun()
        {
            if (Coop.WaitingForHost) return;     // ospitato: la partita parte quando l'ospite lo saluta (BeginCoopRun)
            if (proc) { if (Proc.FloorDirector.I != null && Proc.FloorDirector.I.floor == 0) Proc.FloorDirector.I.StartRun(); return; }
            if (wave == 0 && !waveActive) StartWave(1);
        }

        /// <summary>Cooperativa, ospitato: l'ospite ha detto seme e piano, si parte da li' (e il menu si toglie di mezzo).</summary>
        public void BeginCoopRun(int seed, int floor, bool desert = false, bool pista = false)
        {
            GameMenu.I?.CoopEnter();
            if (proc && Proc.FloorDirector.I != null && Proc.FloorDirector.I.floor == 0) Proc.FloorDirector.I.StartRun(seed, floor, desert, pista);      // desert: l'ospite gioca nel deserto (0.9.1); pista: in una gara su pista
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

        /// <summary>The dedicated tests that are not on the harness yet: while one of these flags is on the command line the automatic run
        /// does not close the game by itself. A test on the harness (<c>PlayerTest</c>) says so through <c>TestRun.Dedicated</c> and is not named
        /// here; porting the rest (T08, docs/engineering/modular-tasks.md) empties this list. One flag per line, so that two changes to it merge.</summary>
        static readonly string[] LegacyDedicatedFlags =
        {
            "-magiatest",
            "-desertotest",
            "-pistatest",
        };

        static bool HasAnyArg(string[] flags)
        {
            foreach (var flag in flags)
                if (HasArg(flag)) return true;
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
            if (GameMenu.I == null)
            {
                // prove (-autotest, senza menu): -splitscreen N avvia lo schermo condiviso prima della prima ondata, come il bottone del menu
                int split = proc ? SplitScreen.AutoStartCount : 0;
                if (split >= SplitScreen.MinPlayers && !SplitScreen.Begin(Mathf.Min(split, SplitScreen.MaxPlayers), SplitScreen.AutoP1Pad, out string splitError))
                    Debug.LogWarning("[PryingEyes] split: non parte: " + splitError);
                BeginRun();
            }
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
                bool trailer = HasArg("-trailer");         // le riprese del trailer (FloorDirector.Trailer.cs) non scrivono i rapporti dei test ne' escono da sole
                foreach (var e in Enemy.All)
                    if (e != null && !Enemy.IsFlying(e.type)) walkerMaxY = Mathf.Max(walkerMaxY, e.transform.position.y);
                if (reportT >= 5f && !trailer)
                {
                    reportT = 0f;
                    var list = new System.Collections.Generic.List<Enemy>(FindObjectsByType<Enemy>(FindObjectsSortMode.None));
                    string epos = "";
                    for (int i = 0; i < Mathf.Min(3, list.Count); i++)
                        epos += $" e{i}({list[i].transform.position.x:F0},{list[i].transform.position.y:F0},{list[i].transform.position.z:F0})";
                    Report(
                        $"{autoT:F0} {wave} {kills} {score} {player.hp:F0} {player.shield:F0} {player.weapon.mag} {player.weapon.reserve} {list.Count} {shotsFired} {shotsHit} {damageTaken} {walkerMaxY:F1}" +
                        $" p({player.transform.position.x:F0},{player.transform.position.y:F0},{player.transform.position.z:F0}){epos}\n");
                }
                if (autoT >= 15f && !shot1done && !trailer) { shot1done = true; HarnessShotFrame = Time.frameCount; ScreenCapture.CaptureScreenshot(TestPaths.Scratch("auto_shot1.png")); }
                if (autoT >= 75f && !shot2done && !trailer) { shot2done = true; HarnessShotFrame = Time.frameCount; ScreenCapture.CaptureScreenshot(TestPaths.Scratch("auto_shot2.png")); }
                if (camShots && autoT >= 8f && !wallShotDone) { wallShotDone = true; HarnessShotFrame = Time.frameCount; WallShot(); }
                if (camShots && autoT >= 20f && !cam1done) { cam1done = true; HarnessShotFrame = Time.frameCount; CamShot(TestPaths.Out("auto_cam1.png")); }
                if (camShots && autoT >= 60f && !cam2done) { cam2done = true; HarnessShotFrame = Time.frameCount; CamShot(TestPaths.Out("auto_cam2.png")); }
                bool dedicated = TestRun.Dedicated || trailer || HasAnyArg(LegacyDedicatedFlags);
                if (autoT >= (proc ? (dedicated ? 1e9f : 70f) : (TestRun.Dedicated ? 1e9f : 100f)))
                {
                    Report($"flicker spegnimenti={Flicker.dropouts} occhi chiusi={WatchingEyes.closings}\n");
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
            CamShot(TestPaths.Out("wallbuy.png"), cam, true);
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
            CamShot(TestPaths.Out("enemies.png"), cam, false);
            Destroy(go);
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
            CamShot(TestPaths.Out("auto_cam_ascensore.png"), cam, false);
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
            int pts = Punti.PerUccisione(src, headshot);
            if (pts > 0) Characters.Progress.Kill(headshot, src == DamageSource.Knife || src == DamageSource.Takedown);       // l'esperienza del personaggio di chi gira
            if (Cattedrale.Catacombe.Attive) pts = Mathf.RoundToInt(pts * Cattedrale.Fabbrica.PuntiUccisioneMul);      // la Decima dell'altare
            AddPoints(pts);
            if (headshot) headshots++;
            player?.Arcana.OnKill();                        // essenza della mano sinistra e Clessidra di Piombo
            Prove.OnKill(src, headshot);
            Medals.OnKill(src, headshot);                   // imprese istantanee (Medals.cs)
            float heal = Perks.BloodHeal(src);
            if (heal > 0f && player != null && player.alive && !player.Downed)      // a terra la Sete di sangue non ti rialza
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
                if (e != null) Proc.FloorMap.NoteKill(e.transform.position);      // il sangue sulla mappa (Proc/Map/FloorMap.cs)
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
            if (proc) { Seats.EnemiesLeft(Enemy.All.Count); return; }
            hud?.SetEnemiesLeft(alive);
        }
    }
}
