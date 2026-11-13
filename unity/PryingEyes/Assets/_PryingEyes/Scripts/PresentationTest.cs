using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-uitest: usa i menu e il controller veri, senza input di sistema. -uishots salva anche le schermate.</summary>
    public sealed class PresentationTest : MonoBehaviour
    {
        readonly Dictionary<string, float?> prefs = new Dictionary<string, float?>();
        float sensitivity, fov, motion;
        int checks, failures, exceptions;
        bool restored;
        string output;
        bool Shots => Array.IndexOf(Environment.GetCommandLineArgs(), "-uishots") >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-uitest") >= 0)
                new GameObject("PresentationTest").AddComponent<PresentationTest>();
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject); // la prova verifica anche il ritorno al menu e la scena ricreata
            sensitivity = GameSettings.Sensitivity; fov = GameSettings.Fov; motion = GameSettings.Motion;
            foreach (string key in new[] { "input_sensitivity", "video_fov", "video_motion" })
                prefs[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetFloat(key) : (float?)null;
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Validation/Presentation"));
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-uiout") output = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
        }

        void OnLog(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }
        void OnDestroy() { Application.logMessageReceived -= OnLog; Restore(); }
        void OnApplicationQuit() => Restore();
        void Restore()
        {
            if (restored) return;
            restored = true;
            GameSettings.SetSensitivity(sensitivity); GameSettings.SetFov(fov); GameSettings.SetMotion(motion);
            foreach (var kv in prefs)
                if (kv.Value.HasValue) PlayerPrefs.SetFloat(kv.Key, kv.Value.Value); else PlayerPrefs.DeleteKey(kv.Key);
            PlayerPrefs.Save();
        }

        void Check(string name, bool ok)
        {
            checks++; if (!ok) failures++;
            Debug.Log("[PryingEyes] uitest " + (ok ? "ok " : "FALLITO ") + name);
        }

        IEnumerator Start()
        {
            // Le eccezioni dentro una coroutine altrimenti la fermerebbero senza chiudere il processo.
            var run = Run();
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("copione completo", false); break; }
                if (!more) break;
                yield return run.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Restore();
            string result = $"RISULTATO {(failures == 0 ? "SUPERATA" : "FALLITA")}: {checks - failures}/{checks}";
            File.WriteAllText(Path.Combine(output, "result.txt"), result);
            Debug.Log("[PryingEyes] uitest " + result);
            Application.Quit(failures == 0 ? 0 : 1);
        }

        static void Click(string label)
        {
            var go = GameObject.Find("Pulsante " + label);
            if (go == null) throw new InvalidOperationException("Pulsante non disponibile: " + label);
            go.GetComponent<Button>().onClick.Invoke();
        }

        void Layout(string name)
        {
            Canvas.ForceUpdateCanvases();
            bool fits = true;
            bool inScreen = true;
            var corners = new Vector3[4];
            foreach (var text in GameMenu.I.GetComponentsInChildren<Text>())
            {
                if (text.preferredHeight > text.rectTransform.rect.height + 2f)
                { fits = false; Debug.Log("[PryingEyes] uitest testo tagliato: " + text.name + " " + text.preferredHeight); }
                text.rectTransform.GetWorldCorners(corners);
                foreach (Vector3 point in corners)
                    if (point.x < -1f || point.y < -1f || point.x > Screen.width + 1f || point.y > Screen.height + 1f)
                    { inScreen = false; Debug.Log("[PryingEyes] uitest fuori schermo: " + text.name + " " + point); break; }
            }
            Check("testi nel pannello " + name, fits);
            Check("pannello entro lo schermo " + name, inScreen);
            if (!Shots) return;
            var canvas = GameMenu.I.GetComponent<Canvas>();
            var cam = Camera.main;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam; canvas.planeDistance = 0.2f;
            Canvas.ForceUpdateCanvases();
            try { GameManager.CamShot(Path.Combine(output, name + ".png"), cam, false, Screen.width, Screen.height); }
            finally { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        }

        static Touch Finger(int id, TouchPhase phase, float x, float y)
        {
            var safe = Screen.safeArea;
            return new Touch { fingerId = id, phase = phase,
                position = new Vector2(safe.x + x / 1280f * safe.width, safe.yMax - y / 720f * safe.height) };
        }

        IEnumerator Run()
        {
            yield return null;
            var gm = GameManager.I;
            Check("avvio sul menu con mondo fermo", GameMenu.InMenu && GameMenu.Blocked && Time.timeScale == 0f);
            Check("HUD nascosto nel menu", !gm.hud.GetComponent<Canvas>().enabled);
            Layout("principale");
            Click("Come si gioca"); Layout("guida"); Click("Indietro");
            Click("Impostazioni"); Layout("impostazioni");
            var sliders = GameMenu.I.GetComponentsInChildren<Slider>();
            Check("sette impostazioni disponibili", sliders.Length == 7);
            Check("maniglie contenute nei cursori", Array.TrueForAll(sliders,
                s => s.handleRect.rect.height <= s.GetComponent<RectTransform>().rect.height));
            sliders[1].value = 90f; sliders[2].value = 1.5f; sliders[3].value = 0f;
            Check("cursori applicano FOV, sensibilità e comfort", GameSettings.Fov == 90f && GameSettings.Sensitivity == 1.5f && GameSettings.Motion == 0f);
            GameSettings.SetFov(999f); GameSettings.SetSensitivity(-1f); GameSettings.SetMotion(float.NaN);
            Check("valori non validi contenuti", GameSettings.Fov == 105f && GameSettings.Sensitivity == 0.25f && GameSettings.Motion == 0.5f);
            GameSettings.SetFov(90f); GameSettings.SetSensitivity(1f); GameSettings.SetMotion(0f);
            Click("Indietro"); Click("Cooperativa"); Layout("cooperativa"); Click("Indietro");
            TouchControls.ProcessTouches(new[] { Finger(1, TouchPhase.Began, 1200, 520) });
            Check("fuoco touch bloccato dal menu", !TouchControls.Held(TouchControls.Action.Fire));
            Click("Gioca");
            float deadline = Time.realtimeSinceStartup + 60f;
            while ((FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            Check("prima discesa completa dal pulsante Gioca", FloorDirector.I.cur != null && !FloorDirector.I.transitioning && !GameMenu.Blocked);
            Enemy.debugFreeze = true; Enemy.debugNoSense = true; gm.player.invulnerable = true;
            yield return new WaitForSecondsRealtime(0.5f);
            Check("FOV applicato alla camera reale", Mathf.Abs(Camera.main.fieldOfView - 90f) < 0.5f);
            CheckInteractions(gm);
            TouchControls.ProcessTouches(new[] { Finger(1, TouchPhase.Began, 1200, 520), Finger(2, TouchPhase.Began, 1200, 520) });
            Check("due dita tengono il fuoco", TouchControls.Held(TouchControls.Action.Fire) && TouchControls.Down(TouchControls.Action.Fire));
            TouchControls.ProcessTouches(new[] { Finger(1, TouchPhase.Ended, 1200, 520), Finger(2, TouchPhase.Stationary, 1200, 520) });
            Check("un rilascio mantiene l'altro dito", TouchControls.Held(TouchControls.Action.Fire) && !TouchControls.Down(TouchControls.Action.Fire));
            TouchControls.ProcessTouches(new[] { Finger(2, TouchPhase.Canceled, 1200, 520) });
            Check("annullamento toglie il fuoco", !TouchControls.Held(TouchControls.Action.Fire));
            TouchControls.ProcessTouches(new[] { Finger(3, TouchPhase.Began, 1100, 40) });
            Check("mappa disponibile sul touch", TouchControls.Down(TouchControls.Action.Map));
            TouchControls.ProcessTouches(new[] { Finger(3, TouchPhase.Ended, 1100, 40) });
            GameMenu.I.SendMessage("OnApplicationFocus", false);
            Check("perdita del fuoco sospende il gioco singolo", GameMenu.Paused && GameMenu.Blocked && Time.timeScale == 0f);
            Layout("pausa");
            Click("Come si gioca"); Click("Indietro");
            Check("guida torna alla pausa", GameObject.Find("Pulsante Riprendi") != null && GameMenu.Paused);
            Click("Riprendi");
            Check("ripresa riattiva mondo e HUD", !GameMenu.Blocked && Time.timeScale == 1f && gm.hud.GetComponent<Canvas>().enabled);
            if (Coop.Available)
            {
                Check("ospite locale per HUD cooperativa", Coop.StartHost(localOnly: true));
                Coop.OnFloorStart(FloorDirector.I.floor); Coop.OnFloorBuilt();
                var peer = RemotePlayer.Create(1, "Compagno di prova");
                Coop.Game.Players[1] = peer;
                peer.Apply(new PlayerNet { epoch = Coop.Game.EpochByte, pos = gm.player.transform.position + Vector3.forward * 4f, alive = true, hp = 0.75f });
                yield return new WaitForSecondsRealtime(0.4f);
                Check("compagno e vita nell'HUD", gm.hud.transform.Find("Compagno0").gameObject.activeSelf &&
                    gm.hud.transform.Find("Compagno0/CompagnoStato0").GetComponent<Text>().text.Contains("75%"));
                if (Shots) GameManager.CamShot(Path.Combine(output, "compagni.png"), null, true, Screen.width, Screen.height);
                GameMenu.I.SendMessage("OnApplicationFocus", false);
                Check("pausa in cooperativa lascia il mondo attivo", GameMenu.Blocked && Time.timeScale == 1f);
                Click("Riprendi");
                Coop.Stop();
                yield return new WaitForSecondsRealtime(0.4f);
                Check("HUD pulito alla disconnessione", !gm.hud.transform.Find("Compagno0").gameObject.activeSelf);
            }
            // Due nuove partite nello stesso processo: da solo, poi dopo aver chiuso un server locale.
            int signature = FloorDirector.I.FloorSignature();
            for (int cycle = 0; cycle < 2; cycle++)
            {
                if (cycle == 1 && Coop.Available) Check("sessione da chiudere al ritorno", Coop.StartHost(localOnly: true));
                gm.player.weapon.Equip(WeaponDefs.IndexOf("dmr_lance7"));
                gm.AddPoints(750); gm.kills = 7;
                Drops.Apply(DropKind.Double);
                Drops.Spawn(DropKind.Ammo, gm.player.transform.position);
                GameMenu.I.SendMessage("Pause");
                Click("Torna al menu"); Layout("conferma_uscita"); Click("Annulla");
                Check("annullare conserva partita e pausa", !GameMenu.InMenu && GameMenu.Paused && gm.kills == 7);
                Click("Torna al menu");
                var old = gm;
                Click("Lascia la partita");
                float until = Time.realtimeSinceStartup + 30f;
                while ((GameManager.I == old || GameMenu.I == null || !GameMenu.InMenu) && Time.realtimeSinceStartup < until) yield return null;
                gm = GameManager.I;
                Check("menu ricreato con sessione chiusa", gm != old && GameMenu.InMenu && GameMenu.Blocked && !Coop.Active && Time.timeScale == 0f);
                Check("nessun nemico, luce o bonus della partita vecchia", Enemy.All.Count == 0 && LightPool.Count == 0 && Drops.Live.Count == 0 && !Drops.DoublePoints);
                Check("ottica vecchia rilasciata", !Array.Exists(Resources.FindObjectsOfTypeAll<RenderTexture>(), rt => rt.name == "ScopeRT" && rt.IsCreated()));
                Check("personaggio e punteggio nuovi", gm.score == 500 && gm.kills == 0 && gm.player.alive && gm.player.weapon.Def.id == "p9_pieta");
                Check("un solo menu e listener audio", FindObjectsByType<GameMenu>(FindObjectsSortMode.None).Length == 1 && FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 1);
                yield return new WaitForSecondsRealtime(0.5f);
                Check("trasporto distrutto anche col menu in pausa", FindObjectsByType<CoopNet>(FindObjectsSortMode.None).Length == 0);
                Check("nessun elenco prefab di rete abbandonato", !Array.Exists(Resources.FindObjectsOfTypeAll<ScriptableObject>(), so => so.name == "CoopSpawnables"));
                Check("nessuna navmesh della partita lasciata nel menu", Resources.FindObjectsOfTypeAll<UnityEngine.AI.NavMeshData>().Length == 0 && !HotelNav.Ready);
                Click("Gioca");
                until = Time.realtimeSinceStartup + 60f;
                while ((FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < until) yield return null;
                Check("nuova partita con piano identico e round azzerato", FloorDirector.I.FloorSignature() == signature && RoundDirector.Round == 0);
                Check("una sola navmesh dopo la nuova partita", Resources.FindObjectsOfTypeAll<UnityEngine.AI.NavMeshData>().Length == 1 && HotelNav.Ready);
                gm.player.invulnerable = true;
            }
        }

        void CheckInteractions(GameManager gm)
        {
            var probe = new GameObject("Sonda interazioni").transform;
            probe.position = new Vector3(1000f, 1f, 1000f);
            var candleGo = new GameObject("Candela prova");
            candleGo.transform.position = new Vector3(1000f, 0f, 1002f);
            var candle = candleGo.AddComponent<Candelabro>();
            candle.objective = true;
            Interact.Add(candle);
            var own = GameObject.CreatePrimitive(PrimitiveType.Cube);
            own.transform.SetParent(candleGo.transform, false);
            own.transform.localPosition = Vector3.up;
            own.transform.localScale = Vector3.one * 0.2f;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(1000f, 1f, 1001f);
            wall.transform.localScale = new Vector3(2f, 2f, 0.1f);
            wall.SetActive(false);
            Physics.SyncTransforms();
            Check("candela selezionabile con il proprio collider", Interact.Tick(probe));
            wall.SetActive(true); Physics.SyncTransforms();
            Interact.TryUse();
            Check("ostacolo sopraggiunto impedisce l'uso del prompt precedente", !candle.lit);
            Check("parete impedisce il prompt della candela", !Interact.Tick(probe));
            wall.SetActive(false); Physics.SyncTransforms();
            Interact.Tick(probe); Interact.TryUse();
            Check("candela utilizzabile dopo la rimozione dell'ostacolo", candle.lit);
            candleGo.SetActive(false); Interact.Remove(candle);
            var rack = WallBuy.Place(0, 1, new Vector3(1000f, 1f, 1002f), Quaternion.Euler(0f, 180f, 0f), Color.white);
            Physics.SyncTransforms();
            Check("espositore libero selezionabile", WallBuy.Tick(probe) && WallBuy.Find(probe) == rack);
            int points = gm.score;
            wall.SetActive(true); Physics.SyncTransforms();
            WallBuy.TryBuy();
            Check("parete impedisce acquisto e spesa da prompt precedente", gm.score == points && WallBuy.Find(probe) == null);
            wall.SetActive(false); Physics.SyncTransforms();
            Check("espositore torna accessibile", WallBuy.Find(probe) == rack);
            Destroy(rack.gameObject); Destroy(wall); Destroy(candleGo); Destroy(probe.gameObject);
            // Pannello montato sulla faccia di un muro: retro e occhio dentro il collider (sporgenza/camera a filo muro).
            var eye = new GameObject("Occhio acquisti a parete").transform;
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0f, side * 90f, 0f);
                Vector3 forward = rot * Vector3.forward, center = new Vector3(2000f + side * 10f, 1.5f, 2000f);
                var mounted = WallBuy.Place(WeaponDefs.IndexOf("smg_vesper9"), 500, center + forward * 0.2f, rot, Color.white);
                var partition = GameObject.CreatePrimitive(PrimitiveType.Cube);
                partition.transform.SetPositionAndRotation(center, rot);
                partition.transform.localScale = new Vector3(3f, 3f, 0.4f);
                partition.SetActive(false);
                eye.position = center - forward; eye.LookAt(mounted.transform);
                Physics.SyncTransforms();
                Check("retro del pannello non acquistabile lato " + side, WallBuy.Find(eye) == null);
                partition.SetActive(true);
                eye.position = center + forward * 0.02f; eye.LookAt(mounted.transform);
                Physics.SyncTransforms();
                int before = gm.score;
                WallBuy.Tick(eye); WallBuy.TryBuy();
                Check("occhio nel muro non compra lato " + side, WallBuy.Find(eye) == null && gm.score == before);
                eye.position = center + forward * 2f; eye.LookAt(mounted.transform);
                Check("fronte montato sul muro acquistabile lato " + side, WallBuy.Find(eye) == mounted);
                mounted.gameObject.SetActive(false); partition.SetActive(false);
                Destroy(mounted.gameObject); Destroy(partition);
            }
            Destroy(eye.gameObject);
        }
    }
}
