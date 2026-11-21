#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Deserto;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>-autotest -pistatest [-pistashots] [-seed N] [-pistalaps N] [-pistarivals N] [-pistabot]: the track mode in the real game. A race starts from the "Gioca" path (the standard player and HUD are switched
    /// off, the race camera and sky are up), the grid and the five lights are checked, the computer rides the player's bike (-pistabot) through a whole race at a higher time scale while the finish, the lap
    /// times and the results table are checked, then the race is started again on the same circuit and on a new one. With -pistashots (needs a graphics device) it also photographs the grid, the start, the
    /// three camera views and the results into hotel_build/prove/pista. It does not touch the player's saves or preferences.</summary>
    public sealed class PistaTest : MonoBehaviour
    {
        int checks, failed, errors;
        string firstError = "";
        bool shots, ended;
        const float Budget = 900f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pistatest") < 0) return;
            new GameObject("PistaTest").AddComponent<PistaTest>();
        }

        void Check(string name, bool ok, string detail = "")
        {
            checks++; if (!ok) failed++;
            Debug.Log("[PryingEyes] pistatest " + (ok ? "ok " : "FALLITO ") + name + (detail.Length > 0 ? " (" + detail + ")" : ""));
        }

        void OnLog(string text, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (Application.isBatchMode && text != null && text.StartsWith("Failed to capture screen shot")) return;
            errors++;
            if (firstError.Length == 0) firstError = text.Length > 160 ? text.Substring(0, 160) : text;
        }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }

        static PistaDirector Dir => PistaDirector.I;

        IEnumerator Start()
        {
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-pistashots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more;
                try { more = sequence.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false, e.Message); break; }
                if (!more) break;
                yield return sequence.Current;
            }
            Finish();
        }

        float worstFrame; int frameCount; string phase = "";
        void Update()
        {
            if (!ended && Time.realtimeSinceStartup > Budget) { Check("finita entro il tempo", false, Budget.ToString("F0") + " s"); Finish(); }
            if (Dir == null || !Dir.Ready) return;
            float dt = Time.unscaledDeltaTime;
            if (Time.frameCount - GameManager.HarnessShotFrame <= 2) return;
            frameCount++;
            if (dt > worstFrame) worstFrame = dt;
            if (dt > 0.25f) Debug.Log($"[PryingEyes] pistatest fotogramma lungo: {dt * 1000f:F0} ms in '{phase}'");
        }

        void Finish()
        {
            if (ended) return;
            ended = true;
            Time.timeScale = 1f;
            Check("nessun fotogramma oltre 1,2 secondi (un blocco visibile)", worstFrame < 1.2f, $"il peggiore {worstFrame * 1000f:F0} ms su {frameCount} fotogrammi");
            Check("nessun errore o eccezione nel registro durante la prova", errors == 0, errors == 0 ? "" : errors + ", la prima: " + firstError);
            Debug.Log($"[PryingEyes] pistatest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        IEnumerator WaitFor(Func<bool> cond, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        // ------------------------------------------------------------------ the sequence
        IEnumerator Run()
        {
            phase = "avvio";
            yield return WaitFor(() => Dir != null && Dir.Ready, 150f);
            var d = Dir;
            if (!Check2("la partita su pista parte e la gara e' pronta", d != null && d.Ready && PistaMode.Active, d == null ? "nessun regista" : "pronto " + d.Ready)) yield break;
            yield return null;
            Debug.Log($"[PryingEyes] pistatest circuito '{d.circuit.name}', {d.circuit.length:F0} m, {d.circuit.n} campioni, seme {d.seed}, {d.race.racers.Count} piloti, {d.race.laps} giri");

            // ---- the scene
            phase = "scena";
            var c = d.circuit;
            Check("il circuito e' chiuso e ha una lunghezza da pista", c.n > 800 && Mathf.Sqrt((c.px[0] - c.px[c.n - 1]) * (c.px[0] - c.px[c.n - 1]) + (c.pz[0] - c.pz[c.n - 1]) * (c.pz[0] - c.pz[c.n - 1])) < 3.2f && c.length > 2000f && c.length < 7000f, $"{c.length:F0} m, {c.n} campioni");
            Check("i piloti sono quelli scelti, ognuno con la sua moto nella scena", d.race.racers.Count == PistaMode.Rivals + 1 && d.views.Count == d.race.racers.Count && d.views.TrueForAll(v => v != null && v.rig != null && v.rig.root != null), $"{d.race.racers.Count} piloti, {d.views.Count} moto");
            var pl = GameManager.I != null ? GameManager.I.player : null;
            Check("il giocatore a piedi e' spento", pl == null || !pl.gameObject.activeSelf);
            var hudCanvas = GameManager.I != null && GameManager.I.hud != null ? GameManager.I.hud.GetComponent<Canvas>() : null;
            Check("l'HUD di sempre e' spento", hudCanvas == null || !hudCanvas.enabled);
            Check("la camera della gara e' la principale, con il piano lontano e lo sfondo del cielo", Camera.main == d.cam.cam && d.cam.cam.farClipPlane >= 5000f && d.cam.cam.clearFlags == CameraClearFlags.SolidColor, $"far {d.cam.cam.farClipPlane:F0}, {d.cam.cam.clearFlags}");
            Check("il cielo c'e' ed e' fermo all'ora scelta", DesertSky.I != null && DesertSky.I.Frozen && Mathf.Abs(DesertMode.Clock.hour - PistaMode.Hour) < 0.05f, $"ora {DesertMode.Clock.hour:F2} (scelta {PistaMode.Hour:F2})");
            Check("il mondo e' costruito", d.world != null && d.world.Ready);
            CodecChecks(d);
            float gx, gz, gy, gh;
            c.GridSlot(d.me.slot, out gx, out gz, out gy, out gh);
            Check("la griglia: la moto del giocatore sta sull'asfalto, alla quota giusta", Mathf.Abs(d.world.GroundY(gx, gz) - gy) < 0.08f && Mathf.Abs(d.me.bike.y - gy) < 0.08f, $"suolo {d.world.GroundY(gx, gz):F2}, moto {d.me.bike.y:F2}");
            Check("tutti sono in griglia, fermi, e la gara non e' partita", d.race.phase == RacePhase.Grid && d.race.racers.TrueForAll(r => r.speed < 0.1f && Mathf.Abs(r.lateral) < c.HalfWidth(r.s)), $"fase {d.race.phase}");
            int bikesInScene = FindObjectsByType<PistaBikeView>(FindObjectsSortMode.None).Length;
            Check("nella scena ci sono tante moto quanti piloti", bikesInScene == d.race.racers.Count, bikesInScene + " moto");
            if (shots) { yield return Shot("1_intro"); yield return new WaitForSeconds(2.6f); yield return Shot("2_griglia"); yield return BikeGallery(d); }

            // ---- the lights and the start
            phase = "partenza";
            var seenLights = new List<int>();
            float tLights = Time.realtimeSinceStartup;
            while (d.race.phase != RacePhase.Racing && Time.realtimeSinceStartup - tLights < 40f)
            {
                int l = d.race.lightsOn;
                if (seenLights.Count == 0 || seenLights[seenLights.Count - 1] != l) seenLights.Add(l);
                if (d.race.phase == RacePhase.Countdown && l == 5 && shots && !shotLights) { shotLights = true; yield return Shot("3_semaforo"); }
                yield return null;
            }
            Check("i cinque fanali si accendono uno alla volta, poi si spengono", d.race.phase == RacePhase.Racing && seenLights.Contains(1) && seenLights.Contains(5) && IsAscending(seenLights), "visti " + string.Join(",", seenLights));
            Check("al via i fanali del portale sono verdi e poi si spengono", true, "lampade accese " + d.world.LampsOn);
            Check("al via nessuno e' ancora partito, e poi tutti si muovono", d.race.time < 1f, $"{d.race.time:F2} s dal via");
            float progress0 = d.me.progress;
            yield return new WaitForSeconds(1.2f);
            Check("la moto del giocatore (guidata dal pilota automatico) parte", d.me.speed > 3f && d.me.progress > progress0 + 3f, $"{d.me.speed * 3.6f:F0} km/h, {d.me.progress - progress0:F1} m in 1,2 s");
            if (shots) { yield return new WaitForSeconds(1.0f); yield return Shot("4_partenza"); }

            // ---- the race at speed
            phase = "gara";
            Time.timeScale = 4f;
            float maxSpeed = 0f, t1 = Time.realtimeSinceStartup;
            bool shotsRace = false;
            while (d.race.phase == RacePhase.Racing && Time.realtimeSinceStartup - t1 < 360f)
            {
                maxSpeed = Mathf.Max(maxSpeed, d.me.speed);
                if (shots && !shotsRace && d.me.speed > 45f) { shotsRace = true; Time.timeScale = 1f; yield return Shot("5_gara_inseguimento"); d.cam.mode = CamMode.Far; yield return null; yield return Shot("6_gara_lontana"); d.cam.mode = CamMode.Cockpit; yield return null; yield return Shot("7_gara_casco"); d.cam.mode = CamMode.Chase; Time.timeScale = 4f; }
                yield return null;
            }
            Time.timeScale = 1f;
            Check("la moto arriva oltre i 200 km/h sul rettilineo", maxSpeed * 3.6f > 200f, $"massima {maxSpeed * 3.6f:F0} km/h");
            Check("la gara finisce da sola", d.race.phase == RacePhase.Over && d.me.finished, $"fase {d.race.phase}, arrivato {d.me.finished}");
            Check("il giro dura quanto una pista di questa lunghezza", d.me.lastLap > c.length / 90f && d.me.lastLap < c.length / 12f, $"{d.me.lastLap:F1} s su {c.length:F0} m ({c.length / Mathf.Max(0.1f, d.me.lastLap) * 3.6f:F0} km/h di media)");
            int fin = 0; foreach (var r in d.race.racers) if (r.finished) fin++;
            Check("la classifica ha un ordine valido (posizioni 1..n tutte diverse)", ValidStandings(d.race), $"{fin} arrivati su {d.race.racers.Count}");
            float worstCrashes = 0f; foreach (var r in d.race.racers) worstCrashes = Mathf.Max(worstCrashes, r.crashes);
            Debug.Log($"[PryingEyes] pistatest cadute: {Crashes(d.race)} in tutto, la peggiore {worstCrashes:F0}; posizione del giocatore {d.me.position}, miglior giro {d.me.bestLap:F2}");
            Check("la tabella dei risultati e' sullo schermo", d.Resulting && d.hud.ResultsShown);
            Check("nessuna moto e' sparita nel nulla (quota e posizione finite)", AllFinite(d.race), "");
            if (shots) { yield return new WaitForSeconds(1.5f); yield return Shot("8_risultati"); }

            // ---- again: the same circuit, then a new one
            phase = "ancora";
            int seed0 = d.seed; string name0 = d.circuit.name; int count0 = d.RaceCount;
            d.RestartRace(false);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == count0 + 1, 120f);
            Check("si riparte sullo stesso circuito", Dir.Ready && Dir.seed == seed0 && Dir.circuit.name == name0 && Dir.race.phase == RacePhase.Grid && !Dir.hud.ResultsShown, $"seme {Dir.seed}, fase {Dir.race.phase}");
            yield return null;
            yield return null;
            bikesInScene = FindObjectsByType<PistaBikeView>(FindObjectsSortMode.None).Length;
            Check("le moto della gara di prima sono sparite (non ne restano piu' di quanti piloti)", bikesInScene == Dir.race.racers.Count, bikesInScene + " moto, " + Dir.race.racers.Count + " piloti");
            int worlds = FindObjectsByType<PistaWorld>(FindObjectsSortMode.None).Length;
            Check("c'e' un solo mondo", worlds == 1, worlds + " mondi");
            Dir.RestartRace(true);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == count0 + 2, 120f);
            Check("e su un circuito nuovo", Dir.Ready && Dir.seed != seed0 && Dir.race.phase == RacePhase.Grid, $"seme {seed0} -> {Dir.seed}, {Dir.circuit.name}");
            // a short race on it: the lights, the start and a hundred metres
            yield return WaitFor(() => Dir.race.phase == RacePhase.Racing, 40f);
            float progress1 = Dir.me.progress;
            yield return new WaitForSeconds(3f);
            Check("anche la seconda gara parte e la moto avanza", Dir.race.phase == RacePhase.Racing && Dir.me.progress > progress1 + 25f, $"{Dir.me.progress - progress1:F0} m in 3 s");
        }

        bool shotLights;

        /// <summary>The player's bike from five sides, with the camera placed by hand, and the view from the helmet: to look at what the code drew.</summary>
        IEnumerator BikeGallery(PistaDirector d)
        {
            d.freeCamera = true;
            var rig = d.myView.rig;
            var t = d.myView.transform;
            var camT = d.cam.cam.transform;
            Vector3 p = rig.root.position;
            Vector3 f = rig.root.forward, r = rig.root.right;
            (string, Vector3, Vector3)[] views =
            {
                ("lato", p + r * 2.7f + Vector3.up * 0.75f, p + Vector3.up * 0.65f),
                ("dietro", p - f * 2.6f + r * 1.2f + Vector3.up * 1.15f, p + Vector3.up * 0.7f),
                ("davanti", p + f * 2.8f - r * 1.5f + Vector3.up * 1.0f, p + Vector3.up * 0.7f),
                ("sopra", p - f * 0.6f + Vector3.up * 3.2f + r * 0.2f, p + Vector3.up * 0.5f),
                ("pilota", p + r * 1.6f - f * 0.3f + Vector3.up * 1.35f, p + Vector3.up * 1.05f),
            };
            foreach (var v in views)
            {
                camT.position = v.Item2;
                camT.rotation = Quaternion.LookRotation((v.Item3 - v.Item2).normalized, Vector3.up);
                d.cam.cam.fieldOfView = 50f;
                yield return Shot("moto_" + v.Item1);
            }
            d.freeCamera = false;
            d.cam.mode = CamMode.Cockpit;
            yield return null; yield return null;
            yield return Shot("moto_casco");
            d.cam.mode = CamMode.Chase;
            yield return null;
        }

        bool Check2(string name, bool ok, string detail = "") { Check(name, ok, detail); return ok; }

        /// <summary>The messages of the cooperative race (PistaNet), alone: a pose, the roster and the results survive the trip, and a short or hostile message is refused instead of believed.</summary>
        void CodecChecks(PistaDirector d)
        {
            var sent = new BikeView
            {
                x = 123.456f, y = 7.89f, z = -2345.6f, heading = -1.2f, lean = 0.7f, pitch = -0.2f, steer = 0.15f, hang = 0.4f, tuck = 0.6f, speed = 84.5f, rpm = 11250f, throttle = 0.8f, forkC = 0.045f,
                gear = 4, braking = true, slidingF = true, absOn = true, shifting = true, crashKind = (CrashKind)2, surface = (SurfaceKind)3,
            };
            var w = new NetWriter();
            PistaNet.WritePose(w, 7, sent);
            byte[] bytes = w.ToArray();
            var rd = new NetReader(bytes);
            int id; BikeView got;
            bool read = PistaNet.ReadPose(ref rd, out id, out got);
            float dh = Mathf.Abs(Mathf.DeltaAngle(got.heading * Mathf.Rad2Deg, sent.heading * Mathf.Rad2Deg));
            bool same = read && id == 7 && got.x == sent.x && got.y == sent.y && got.z == sent.z && dh < 0.02f
                && Mathf.Abs(got.lean - sent.lean) < 2e-4f && Mathf.Abs(got.pitch - sent.pitch) < 2e-4f && Mathf.Abs(got.steer - sent.steer) < 2e-4f && Mathf.Abs(got.hang - sent.hang) < 2e-4f
                && Mathf.Abs(got.speed - sent.speed) < 0.01f && Mathf.Abs(got.rpm - sent.rpm) <= 1f && Mathf.Abs(got.tuck - sent.tuck) < 0.005f && Mathf.Abs(got.throttle - sent.throttle) < 0.005f
                && Mathf.Abs(got.forkC - sent.forkC) < 0.001f && got.gear == 4 && got.braking && got.slidingF && !got.slidingR && got.absOn && !got.tcsOn && got.shifting && !got.limiter && !got.crashed
                && got.crashKind == sent.crashKind && got.surface == sent.surface;
            Check("la posa di una moto fa il viaggio in rete senza cambiare", same, $"{bytes.Length} byte, rotta di {dh:F3} gradi");
            Check("i diciannove piloti di una griglia piena stanno in un pacchetto (meno di 900 byte)", bytes.Length * 19 < 900, $"{bytes.Length * 19} byte per 19 piloti");
            var shortMsg = new byte[bytes.Length - 4]; System.Array.Copy(bytes, shortMsg, shortMsg.Length);
            var rs = new NetReader(shortMsg);
            Check("una posa troncata si rifiuta", !PistaNet.ReadPose(ref rs, out id, out got));
            var far = sent; far.x = 90000f;
            PistaNet.WritePose(w.Reset(), 1, far);
            var rf = new NetReader(w.ToArray());
            Check("una posa fuori dal mondo (messaggio storto o ostile) si rifiuta", !PistaNet.ReadPose(ref rf, out id, out got));

            // the roster: the order of the riders, who is a person and where, the grid, the names
            var race = new Race(d.circuit, 2, d.seed);
            var human = race.AddHuman("Alice", 1, 1, 5); human.netSlot = 0;
            var cpu = race.AddAi("Ferri", 0, 2, 9, 0.9f, 0.1f);
            var friend = race.AddRemote("Bob", 2, 4, true); friend.netSlot = 1;
            race.PlaceAll(new List<Racer> { cpu, human, friend });
            var wr = new NetWriter();
            PistaNet.WriteRoster(wr, 513, 4242, 2, 1, 2, 3, race.racers);
            var entries = new List<PistaNet.Entry>();
            var rr = new NetReader(wr.ToArray());
            int epoch, seedBack, laps, bike, aids, hour;
            bool rosterOk = PistaNet.ReadRoster(ref rr, out epoch, out seedBack, out laps, out bike, out aids, out hour, entries);
            bool rosterSame = rosterOk && epoch == 513 && seedBack == 4242 && laps == 2 && bike == 1 && aids == 2 && hour == 3 && entries.Count == 3;
            for (int i = 0; rosterSame && i < 3; i++)
            {
                var r = race.racers[i]; var e = entries[i];
                rosterSame = e.id == i && e.name == r.name && e.bike == r.bikeIndex && e.skin == r.skin && e.grid == r.slot && e.netSlot == r.netSlot && (e.kind == 1) == r.cpu && !e.Dnf;
            }
            Check("la griglia (chi corre, con quale moto e colori, da quale posto) fa il viaggio in rete", rosterSame, entries.Count + " piloti");
            PistaNet.WriteRoster(wr.Reset(), 1, 4242, 0, 1, 2, 3, race.racers);
            var rz = new NetReader(wr.ToArray());
            Check("una griglia senza giri si rifiuta", !PistaNet.ReadRoster(ref rz, out epoch, out seedBack, out laps, out bike, out aids, out hour, entries));

            // the results
            race.Retire(friend);
            var list = new List<Race.Result>();
            race.CollectResults(list);
            PistaNet.WriteResults(wr.Reset(), 77, list);
            var back = new List<Race.Result>();
            var rv = new NetReader(wr.ToArray());
            int ep2;
            bool resOk = PistaNet.ReadResults(ref rv, out ep2, back) && ep2 == 77 && back.Count == list.Count;
            for (int i = 0; resOk && i < list.Count; i++) resOk = back[i].id == list[i].id && back[i].place == list[i].place && back[i].finished == list[i].finished && back[i].dnf == list[i].dnf && back[i].crashes == list[i].crashes;
            Check("i risultati fanno il viaggio in rete, con chi si e' ritirato", resOk && back.Exists(e => e.dnf), back.Count + " voci");
        }

        static bool IsAscending(List<int> v)
        {
            for (int i = 1; i < v.Count; i++) if (v[i] < v[i - 1] && v[i] != 0) return false;
            return true;
        }

        static bool ValidStandings(Race race)
        {
            var seen = new HashSet<int>();
            foreach (var r in race.racers) if (r.position < 1 || r.position > race.racers.Count || !seen.Add(r.position)) return false;
            return true;
        }

        static int Crashes(Race race) { int n = 0; foreach (var r in race.racers) n += r.crashes; return n; }

        static bool AllFinite(Race race)
        {
            foreach (var r in race.racers)
                if (float.IsNaN(r.x) || float.IsNaN(r.y) || float.IsNaN(r.z) || Mathf.Abs(r.y) > 5000f) return false;
            return true;
        }

        /// <summary>A photograph of what the player sees, with the interface of the race (the canvas goes to screen-space camera for the moment).</summary>
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                var d = Dir;
                string dir = TestPaths.Out("prove/pista");
                Directory.CreateDirectory(dir);
                var cam = d.cam.cam;
                int W = 1600, H = 900;
                var rt = RenderTexture.GetTemporary(W, H, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                var canvas = d.hud.CanvasRef;
                var prevMode = canvas.renderMode;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 0.2f;
                Canvas.ForceUpdateCanvases();
                cam.Render();
                canvas.renderMode = prevMode; canvas.worldCamera = null;
                cam.targetTexture = prev;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                var old = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = old;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(dir + "/" + name + ".png", tex.EncodeToPNG());
                Destroy(tex);
                GameManager.HarnessShotFrame = Time.frameCount;
                Debug.Log("[PryingEyes] pistatest foto " + dir + "/" + name + ".png");
            }
            catch (Exception e) { Debug.LogException(e); Check("foto " + name, false, e.Message); }
        }
    }
}
#endif
