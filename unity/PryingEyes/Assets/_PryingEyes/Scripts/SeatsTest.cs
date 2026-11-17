#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-seatstest -splitscreen N [-seed S -floor F] [-seatsshots]: lo schermo condiviso in UN processo e UNA finestra, provato da dentro.
    /// Due modi di far partire la partita: dal menu (senza -autotest, con -menureview -nowaves: GameMenu.SplitAutoStart preme il bottone "N giocatori",
    /// quindi si prova anche il percorso vero, Gioca e la pausa) oppure con -autotest -nofire -noenemies (senza menu: parte GameManager.Start e la
    /// pausa non si prova). Si controlla: struttura (N giocatori, ognuno con corpo, camera, HUD, controller e oggetti suoi; camere nelle loro
    /// porzioni, un solo AudioListener, strati dell'arma separati), cambio di posto (Seats.Enter mette al posto dei globali quelli del posto), punti
    /// e contatori per giocatore, bersagli dei nemici (nascono sul piu' vicino, seguono chi li provoca o chi vedono), danno, morte e rinascita di
    /// un solo giocatore, controller indipendenti (stato XInput finto per canale: cammina, spara e salta solo il posto giusto, anche il primo),
    /// pausa con Start di qualunque controller (ferma tutti, riprende per tutti), fine dello schermo condiviso (si torna a un giocatore solo, e
    /// funziona). Con -seatsshots, a finestra aperta (senza -nographics), salva una foto di tutto lo schermo (porzioni, HUD e righe) in unity_logs/seats.
    /// Lo lancia tools/coop/run_split.ps1; documentazione in docs/multiplayer/split-screen.md.</summary>
    public sealed class SeatsTest : MonoBehaviour
    {
        int checks, failed, exceptions, errors;
        readonly List<string> samples = new List<string>();
        bool shots;
        bool systemPhotos = true;      // con piu' monitor veri: finche' il copione run_split.ps1 risponde alle richieste di foto
        static readonly FieldInfo TgtField = typeof(Enemy).GetField("tgt", BindingFlags.Instance | BindingFlags.NonPublic);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-seatstest") < 0) return;
            Application.runInBackground = true;         // a finestra aperta ma senza il fuoco il gioco (runInBackground e' spento nel progetto) si fermerebbe: la prova no
            new GameObject("SeatsTest").AddComponent<SeatsTest>();
        }

        bool Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] seatstest " + (ok ? "ok " : "FALLITO ") + name); return ok; }

        void Log(string message, string stack, LogType type)
        {
            // senza finestra (batchmode con la GPU, per le foto dei corpi) la foto automatica del gioco non puo' riuscire: non e' un errore del gioco
            if (type == LogType.Error && Application.isBatchMode && message != null && message.StartsWith("Failed to capture screen shot")) return;
            if (type == LogType.Exception) exceptions++;
            else if (type == LogType.Error || type == LogType.Assert) errors++;
            else return;
            if (samples.Count < 8) samples.Add(type + ": " + message);
        }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-seatsshots") >= 0 && !Application.isBatchMode;
            Application.targetFrameRate = 60;           // a 10000 fotogrammi al secondo il CharacterController non cammina come dovrebbe
            QualitySettings.vSyncCount = 0;
            yield return null;                          // GameManager.Start avvia lo schermo condiviso nello stesso fotogramma
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            foreach (var s in Seats.All) PadControls.Inject(s.pad, null);
            PadControls.Inject(null); PadControls.Disable();
            Check("nessuna eccezione runtime", exceptions == 0);
            Check("nessun errore nel registro", errors == 0);
            foreach (var s in samples) Debug.Log("[PryingEyes] seatstest registro: " + s);
            Debug.Log($"[PryingEyes] seatstest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ utilita'
        static PadControls.Raw Pad(ushort buttons = 0, int lt = 0, int rt = 0, int lx = 0, int ly = 0, int rx = 0, int ry = 0) =>
            new PadControls.Raw { connected = true, buttons = buttons, leftTrigger = (byte)lt, rightTrigger = (byte)rt, lx = (short)lx, ly = (short)ly, rx = (short)rx, ry = (short)ry };

        // i contatori del posto che gira sono nei globali, quelli degli altri nel posto
        static int ScoreOf(Seat s) => s == Seats.Current ? GameManager.I.score : s.score;
        static int KillsOf(Seat s) => s == Seats.Current ? GameManager.I.kills : s.kills;
        static int ShotsOf(Seat s) => s == Seats.Current ? GameManager.shotsFired : s.shotsFired;
        static int HitsOf(Seat s) => s == Seats.Current ? GameManager.shotsHit : s.shotsHit;
        static int DeathsOf(Seat s) => s == Seats.Current ? GameManager.deaths : s.deaths;

        static bool Same(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) < 1e-4f && Mathf.Abs(a.y - b.y) < 1e-4f && Mathf.Abs(a.width - b.width) < 1e-4f && Mathf.Abs(a.height - b.height) < 1e-4f;

        static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
        static ITarget TgtOf(Enemy e) => TgtField != null ? (ITarget)TgtField.GetValue(e) : null;
        static void Place(Seat s, Vector3 at, float yaw) { s.player.Teleport(at + Vector3.up * 0.05f); s.player.yaw = yaw; s.player.pitch = 0f; }

        /// <summary>Punti di terra lontani da 'from' e fra loro, dove parcheggiare i giocatori che non servono alla prova.</summary>
        static List<Vector3> FarSpots(Vector3 from, int count)
        {
            var pts = new List<Vector3>();
            foreach (var p in FloorDirector.I.cur.groundSpawns) if (p.y < 0.5f) pts.Add(p);
            pts.Sort((a, b) => (b - from).sqrMagnitude.CompareTo((a - from).sqrMagnitude));
            var res = new List<Vector3>();
            foreach (var p in pts)
            {
                bool ok = Flat(p, from) > 25f;
                foreach (var q in res) if (Flat(p, q) < 12f) ok = false;
                if (ok) res.Add(p);
                if (res.Count == count) break;
            }
            return res;
        }

        /// <summary>0.7.1: il giocatore 2 a tre metri davanti al giocatore 1. La camera del 1 lo disegna davvero (stessa immagine con e senza
        /// lo strato del corpo del 2: devono cambiare abbastanza pixel) e la foto resta in hotel_build/seats/ per guardarla. Senza grafica
        /// (-nographics) solo una riga nel rapporto.</summary>
        IEnumerator SeeEachOther(Seat[] seats)
        {
            if (seats.Length < 2 || !MeleeTest.FindArena(out Vector3 arena, out Vector3 forward)) { Debug.Log("[PryingEyes] seatstest: nessuno spazio per la foto dei corpi"); yield break; }
            Place(seats[0], arena - forward * 1.5f, Mathf.Atan2(forward.x, forward.z));
            Place(seats[1], arena + forward * 1.5f, Mathf.Atan2(-forward.x, -forward.z));
            for (int i = 2; i < seats.Length; i++) Place(seats[i], arena - forward * 4f + Vector3.Cross(Vector3.up, forward) * (i * 1.2f), Mathf.Atan2(forward.x, forward.z));
            yield return new WaitForSeconds(0.6f);
            yield return new WaitForEndOfFrame();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Debug.Log("[PryingEyes] seatstest: senza grafica niente foto dei corpi"); yield break; }
            var cam = seats[0].camera;
            int other = 1 << (Seats.FirstAvatarLayer + 1);
            var with = RenderOnce(cam, cam.cullingMask, 640, 360);
            var without = RenderOnce(cam, cam.cullingMask & ~other, 640, 360);
            int changed = 0;
            var a = with.GetPixels32(); var b = without.GetPixels32();
            for (int k = 0; k < a.Length; k++)
                if (Mathf.Abs(a[k].r - b[k].r) + Mathf.Abs(a[k].g - b[k].g) + Mathf.Abs(a[k].b - b[k].b) > 24) changed++;
            float frac = changed / (float)a.Length;
            try
            {
                string dir = TestPaths.Out("seats");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"vedersi_{seats.Length}.png"), with.EncodeToPNG());
            }
            catch (Exception ex) { Debug.Log("[PryingEyes] seatstest: foto dei corpi non scritta: " + ex.Message); }
            Destroy(with); Destroy(without);
            Check($"il giocatore 1 vede il giocatore 2 davanti a se' (il suo corpo cambia il {frac * 100f:F1}% dei pixel)", frac > 0.01f);
        }

        static Texture2D RenderOnce(Camera cam, int mask, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24);
            Rect oldRect = cam.rect; int oldMask = cam.cullingMask; var oldTarget = cam.targetTexture;
            cam.cullingMask = mask; cam.rect = new Rect(0f, 0f, 1f, 1f); cam.targetTexture = rt;
            cam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = oldTarget; cam.cullingMask = oldMask; cam.rect = oldRect;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>Con piu' monitor veri e la finestra (-seatsshots): ogni monitor acceso ha la sua finestra e nessun altro programma la copre
        /// (altrimenti il giocatore che sta li' non vedrebbe il gioco). Si scrive nel registro l'elenco delle finestre del gioco.</summary>
        IEnumerator MonitorWindows()
        {
            if (!shots || Seats.Monitors < 2 || SplitScreen.RealMonitors < Seats.Monitors) yield break;
            float until = Time.realtimeSinceStartup + 4f;
            while ((SplitScreen.VisibleWindows() < Seats.Monitors || SplitScreen.WindowsOnTop() < Seats.Monitors) && Time.realtimeSinceStartup < until)
                yield return new WaitForSecondsRealtime(0.25f);          // le finestre dei monitor accesi si aprono e vengono in primo piano
            Debug.Log("[PryingEyes] seatstest: finestre del gioco: " + SplitScreen.WindowsReport().Replace("\n", " | "));
            Check($"ogni monitor acceso ha la sua finestra ({SplitScreen.VisibleWindows()} visibili, {Seats.Monitors} monitor)", SplitScreen.VisibleWindows() >= Seats.Monitors);
            Check($"nessun altro programma copre le finestre del gioco ({SplitScreen.WindowsOnTop()} in primo piano su {Seats.Monitors})", SplitScreen.WindowsOnTop() >= Seats.Monitors);
            // quanto pesa avere piu' monitor: fotogrammi al secondo in due secondi (il mondo e' fermo, i nemici tolti); solo un'informazione
            int f0 = Time.frameCount; float t0 = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(2f);
            float fps = (Time.frameCount - f0) / Mathf.Max(0.01f, Time.realtimeSinceStartup - t0);
            Debug.Log($"[PryingEyes] seatstest: fotogrammi al secondo con {Seats.Monitors} monitor ({SplitScreen.DescribeDisplays(Seats.Monitors)}): {fps:F0} (vsync {QualitySettings.vSyncCount}, limite {Application.targetFrameRate})");
        }

        /// <summary>Un altro programma copre la finestra di un monitor acceso: il gioco la riconosce coperta e la riporta in primo piano, senza prendere
        /// il fuoco (se lo prendesse il gioco andrebbe in pausa). La finestra che copre e' di questo stesso processo e non prende il fuoco. Si fa verso
        /// la fine della prova, cosi' se qualcosa va storto non rovina il resto.</summary>
        IEnumerator CoveredWindow()
        {
            if (!shots || Seats.Monitors < 2 || SplitScreen.RealMonitors < Seats.Monitors) yield break;
            var cover = SplitScreen.TestCover();
            if (cover == IntPtr.Zero) { Debug.Log("[PryingEyes] seatstest: finestra coperta: non c'e' una finestra da coprire, niente prova"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            int covered = SplitScreen.WindowsOnTop();
            bool paused = GameMenu.Paused;
            Debug.Log($"[PryingEyes] seatstest: finestra coperta: in primo piano {covered}, in pausa {paused}, con il fuoco {Application.isFocused}");
            Check($"una finestra di un altro programma sopra quella del monitor acceso la copre ({covered} in primo piano su {Seats.Monitors})", covered < Seats.Monitors);
            Check("la finestra messa sopra non prende il fuoco: il gioco non va in pausa", !paused);
            int raised = SplitScreen.RaiseWindows();
            yield return new WaitForSecondsRealtime(0.5f);
            Check($"il gioco riporta in primo piano la finestra coperta ({raised} mossa, {SplitScreen.WindowsOnTop()} in primo piano su {Seats.Monitors})", SplitScreen.WindowsOnTop() >= Seats.Monitors);
            SplitScreen.TestUncover(cover);
            yield return new WaitForSecondsRealtime(0.3f);
            Check("tolta la finestra messa sopra il gioco gira ancora (nessuna pausa)", !GameMenu.Paused && Time.timeScale == 1f);
        }

        /// <summary>Chi sta su un monitor oltre il primo ha la sua mappa in uGUI (l'IMGUI si vede solo sul primo monitor): la grande e la minimappa si
        /// aprono e si chiudono con i segnalini e le scritte giuste; chi sta sul primo monitor non ne ha una seconda.</summary>
        IEnumerator SeatMaps(Seat[] seats)
        {
            var fd = FloorDirector.I;
            float until = Time.realtimeSinceStartup + 5f;
            while (fd != null && !fd.MapsShown && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.1f);
            if (!Check($"il piano e' in vista per la prova delle mappe (bloccato {GameMenu.Blocked}, in pausa {GameMenu.Paused}, scala del tempo {Time.timeScale})", fd != null && fd.MapsShown)) yield break;
            // all'arrivo i giocatori sono ancora nell'ascensore, che la mappa non disegna: se qualcuno sta su un altro monitor si segnano visitate le
            // sei stanze piu' vicine, perche' ci sia qualcosa da vedere (e da fotografare) oltre ai segnalini
            int visited = 0;
            bool otherMonitor = false;
            foreach (var s in seats) otherMonitor |= s.display != 0;
            if (otherMonitor)
            {
                var origin = seats[0].player.transform.position;
                var near = new List<KeyValuePair<float, int>>();
                foreach (var room in fd.cur.plan.rooms)
                {
                    if (room.IsLift) continue;
                    float dx = (room.x0 + room.x1) * 0.05f - origin.x, dz = (room.z0 + room.z1) * 0.05f - origin.z;
                    near.Add(new KeyValuePair<float, int>(dx * dx + dz * dz, room.id));
                }
                near.Sort((a, b) => a.Key.CompareTo(b.Key));
                var ids = new List<int>();
                for (int i = 0; i < near.Count && i < 6; i++) ids.Add(near[i].Value);
                fd.DebugSetExplored(ids);
                visited = ids.Count;
            }
            foreach (var s in seats)
            {
                var ui = s.hud != null ? s.hud.GetComponent<SeatMapUi>() : null;
                if (s.display == 0)
                {
                    Check($"il giocatore {s.index + 1} (monitor 1) ha la mappa dell'IMGUI e non una seconda in uGUI", ui == null);
                    continue;
                }
                fd.DebugShowMapFor(s.index, true, true);
                yield return null; yield return null; yield return null;
                Check($"il giocatore {s.index + 1} (monitor {s.display + 1}) vede la mappa grande ({(ui != null ? ui.BigItems : 0)} elementi, con 'Tu', la legenda e le {visited} stanze visitate) e la minimappa ({(ui != null ? ui.MiniItems : 0)})",
                    ui != null && ui.BigShown && ui.MiniShown && ui.BigItems >= 5 + visited && ui.MiniItems >= 2 && ui.BigHasText("Tu"));
                yield return Photo($"1b-mappa-del-giocatore-{s.index + 1}");
                fd.DebugShowMapFor(s.index, false, true);
                yield return null; yield return null; yield return null;
                Check($"il giocatore {s.index + 1}: chiusa la mappa grande resta la minimappa", ui != null && !ui.BigShown && ui.MiniShown);
                fd.DebugShowMapFor(s.index, false, false);
                yield return null; yield return null; yield return null;
                Check($"il giocatore {s.index + 1}: spenta anche la minimappa non si vede niente", ui != null && !ui.BigShown && !ui.MiniShown);
                fd.DebugShowMapFor(s.index, false, true);          // com'era
            }
        }

        /// <summary>Una foto di questo momento. Con una finestra sola e' ScreenCapture. Con piu' monitor veri ScreenCapture vedrebbe solo l'ultimo
        /// monitor disegnato (e alla misura della finestra principale): si chiede a run_split.ps1 -Graphics -Monitors N di fotografare ogni monitor
        /// dal sistema (tools/coop/capture_monitors.ps1: &lt;nome&gt;-monitor1.png, &lt;nome&gt;-monitor2.png ...) e si aspetta il suo "fatto" (&lt;nome&gt;.done,
        /// al massimo 20 s); se nessuno risponde, le foto di questa prova si saltano.</summary>
        IEnumerator Photo(string name)
        {
            if (!shots) yield break;
            string folder = Path.GetFullPath("unity_logs/seats");
            Directory.CreateDirectory(folder);
            if (Seats.Monitors > 1 && SplitScreen.RealMonitors >= Seats.Monitors)
            {
                if (!systemPhotos) yield break;
                string done = Path.Combine(folder, name + ".done");
                try { File.Delete(done); } catch (Exception) { }
                yield return null; yield return null;                    // il fotogramma e' gia' sullo schermo
                Debug.Log("[PryingEyes] seatstest: foto-richiesta: " + name);
                float until = Time.realtimeSinceStartup + 20f;
                while (!File.Exists(done) && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.1f);
                if (!File.Exists(done)) { systemPhotos = false; Debug.Log("[PryingEyes] seatstest: foto-monitor: nessuno le fa (serve run_split.ps1 -Graphics -Monitors N): niente foto"); }
                yield break;
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
            yield return null; yield return null;
        }

        // ------------------------------------------------------------------ la prova
        IEnumerator Run()
        {
            int n = SplitScreen.AutoStartCount;
            Check($"-splitscreen {n}: un solo processo con {n} giocatori", n >= SplitScreen.MinPlayers && Seats.Split && Seats.Count == n);
            if (!Seats.Split) yield break;
            var seats = Seats.All.ToArray();
            var gm = GameManager.I;

            float deadline = Time.realtimeSinceStartup + 120f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Check("il piano e' pronto", FloorDirector.I != null && FloorDirector.I.cur != null && !FloorDirector.I.transitioning)) yield break;
            yield return null; yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            foreach (var s in seats) s.player.invulnerable = true;

            Structure(seats, n);
            ContextSwap(seats);
            yield return MonitorWindows();
            yield return Photo("1-arrivo");
            yield return SeatMaps(seats);
            yield return SeeEachOther(seats);

            // ---- spazio per la prova
            if (!Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward))) yield break;
            float yaw = Mathf.Atan2(forward.x, forward.z);
            Vector3 start = arena - forward * 2.8f;
            var far = FarSpots(arena, n);
            if (!Check($"ci sono {n} punti lontani dove parcheggiare i giocatori ({far.Count})", far.Count >= n)) yield break;
            void ParkAllBut(int keep) { for (int i = 0; i < n; i++) if (i != keep) Place(seats[i], far[i], 0f); }
            for (int i = 0; i < n; i++) Place(seats[i], far[i], 0f);
            yield return new WaitForSeconds(0.3f);
            yield return Photo("2-ognuno-in-una-stanza-diversa");

            // ---- cambio di posizione: ognuno al suo posto, gli occhi sono quelli giusti
            {
                bool eyes = true, nearest = true;
                for (int i = 0; i < n; i++)
                {
                    eyes &= Seats.NearestEye(seats[i].player.transform.position) == seats[i].Eye;
                    nearest &= Seats.NearestPlayer(seats[i].player.transform.position) == seats[i].player;
                    nearest &= Targets.Nearest(seats[i].player.transform.position) == seats[i].target;
                }
                Check($"{n} giocatori lontani fra loro: ognuno ha vicino il suo occhio, il suo corpo e il suo bersaglio", eyes && nearest);
                Check("a schermo condiviso gli occhi sono uno per giocatore", Seats.Eyes().Count == n);
                Check("la distanza dall'occhio piu' vicino e' quella del proprio occhio", Mathf.Abs(Seats.EyeDistanceSqr(seats[1].player.transform.position) - (seats[1].Eye.position - seats[1].player.transform.position).sqrMagnitude) < 1e-3f);
            }

            // ---- controller: ogni giocatore cammina col suo, gli altri restano fermi
            for (int who = 0; who < n; who++)
            {
                ParkAllBut(who);
                Place(seats[who], start, yaw);
                yield return new WaitForSeconds(0.4f);
                var before = new Vector3[n];
                for (int i = 0; i < n; i++) before[i] = seats[i].player.transform.position;
                PadControls.Inject(seats[who].pad, Pad(ly: 32767));
                yield return new WaitForSeconds(1.0f);
                PadControls.Inject(seats[who].pad, null);
                Vector3 d = seats[who].player.transform.position - before[who];
                float along = Vector3.Dot(d, forward);
                bool others = true;
                for (int i = 0; i < n; i++) if (i != who) others &= Flat(seats[i].player.transform.position, before[i]) < 0.1f;
                Check($"controller del giocatore {who + 1}: avanza ({along:F1} m in 1 s) e gli altri {n - 1} restano fermi", along > 2f && others);
                yield return new WaitForSeconds(0.4f);
            }

            // ---- il grilletto di un giocatore spara solo la sua arma
            {
                ParkAllBut(1);
                Place(seats[1], start, yaw);
                yield return new WaitForSeconds(0.4f);
                var shotsBefore = new int[n];
                for (int i = 0; i < n; i++) shotsBefore[i] = ShotsOf(seats[i]);
                Check("l'arma del giocatore 2 ha colpi", seats[1].player.weapon.mag > 0);
                PadControls.Inject(seats[1].pad, Pad(rt: 255));
                yield return new WaitForSeconds(0.6f);
                PadControls.Inject(seats[1].pad, null);
                bool onlyHim = ShotsOf(seats[1]) > shotsBefore[1];
                for (int i = 0; i < n; i++) if (i != 1) onlyHim &= ShotsOf(seats[i]) == shotsBefore[i];
                Check($"grilletto del giocatore 2: spara lui ({ShotsOf(seats[1]) - shotsBefore[1]} colpi) e nessun altro", onlyHim);
                yield return new WaitForSeconds(0.3f);
            }

            // ---- due controller insieme: uno cammina, l'altro salta, senza scambiarsi i comandi
            {
                ParkAllBut(-1);
                Place(seats[0], start, yaw);
                yield return new WaitForSeconds(0.6f);
                Vector3 p0 = seats[0].player.transform.position, p1 = seats[1].player.transform.position;
                float y1 = p1.y, y1Max = p1.y, y0Max = p0.y;
                var rest = new Vector3[n];
                for (int i = 0; i < n; i++) rest[i] = seats[i].player.transform.position;
                PadControls.Inject(seats[0].pad, Pad(ly: 32767));
                PadControls.Inject(seats[1].pad, Pad(PadControls.A));
                yield return null; yield return null;
                PadControls.Inject(seats[1].pad, Pad());
                for (float t = 0f; t < 0.9f; t += Time.deltaTime)
                {
                    y1Max = Mathf.Max(y1Max, seats[1].player.transform.position.y);
                    y0Max = Mathf.Max(y0Max, seats[0].player.transform.position.y);
                    yield return null;
                }
                PadControls.Inject(seats[0].pad, null);
                PadControls.Inject(seats[1].pad, null);
                float along = Vector3.Dot(seats[0].player.transform.position - p0, forward);
                bool others = true;
                for (int i = 2; i < n; i++) others &= Flat(seats[i].player.transform.position, rest[i]) < 0.1f;
                Check($"giocatore 1 cammina ({along:F1} m) mentre il 2 salta (+{y1Max - y1:F2} m): il 1 non salta (+{y0Max - p0.y:F2} m), il 2 non si sposta ({Flat(seats[1].player.transform.position, p1):F2} m)",
                    along > 1.5f && y1Max - y1 > 0.25f && y0Max - p0.y < 0.12f && Flat(seats[1].player.transform.position, p1) < 0.5f && others);
                yield return new WaitForSeconds(0.5f);
            }

            // ---- punti e contatori per giocatore
            {
                int[] score = new int[n];
                for (int i = 0; i < n; i++) score[i] = ScoreOf(seats[i]);
                using (Seats.Enter(seats[1])) gm.AddPoints(100);
                bool ok = ScoreOf(seats[1]) == score[1] + 100;
                for (int i = 0; i < n; i++) if (i != 1) ok &= ScoreOf(seats[i]) == score[i];
                Check($"+100 punti dal giocatore 2: li ha solo lui ({score[1]} -> {ScoreOf(seats[1])})", ok);
                for (int i = 0; i < n; i++) score[i] = ScoreOf(seats[i]);
                var last = seats[n - 1];
                using (Seats.Enter(last)) gm.AddPoints(-50);
                ok = ScoreOf(last) == score[n - 1] - 50;
                for (int i = 0; i < n - 1; i++) ok &= ScoreOf(seats[i]) == score[i];
                Check($"-50 punti (un acquisto) dal giocatore {n}: li perde solo lui", ok);
            }

            // ---- nemici: bersaglio, provocazione, colpi e uccisioni
            {
                foreach (var s in seats) s.player.invulnerable = true;
                ParkAllBut(1);
                Place(seats[1], start, yaw);
                yield return new WaitForSeconds(0.3f);
                Vector3 spot = start + forward * 1.8f;
                var e = Enemy.Spawn(Enemy.EnemyType.Monaco, spot);
                Check("il nemico nasce", e != null && TgtField != null);
                if (e == null) yield break;
                e.transform.rotation = Quaternion.LookRotation(-forward);
                Check("nasce puntando il giocatore piu' vicino (il 2), non il primo", TgtOf(e) == Targets.At(1));

                // chi caccia per round (onnisciente: sempre, nel ciclo a ondate) segue il piu' vicino
                e.relentless = true;
                TgtField.SetValue(e, Targets.At(0));
                yield return new WaitForSeconds(2.0f);
                Check("nemico onnisciente: lontano dal primo e a due passi dal secondo, passa al secondo", TgtOf(e) == Targets.At(1));
                e.relentless = false;
                yield return Photo("3-nemico-davanti-al-giocatore-2");

                if (Stealth.Enabled)
                {
                    // vecchio ciclo con la furtivita': Provoke del posto che gira, e chi vede un giocatore passa a lui
                    TgtField.SetValue(e, Targets.At(0));
                    using (Seats.Enter(seats[1])) e.Provoke();
                    Check("Provoke di chi sta girando (il 2): il nemico passa a lui", TgtOf(e) == Targets.At(1));
                    TgtField.SetValue(e, Targets.At(0));
                    yield return new WaitForSeconds(2.0f);
                    Check("furtivita': il nemico vede il giocatore 2 a due passi e passa a lui", TgtOf(e) == Targets.At(1));
                }
                else Debug.Log("[PryingEyes] seatstest: ciclo a ondate, nemici onniscienti: niente prova di Provoke e della vista");

                int[] hits = new int[n], kills = new int[n], score = new int[n];
                for (int i = 0; i < n; i++) { hits[i] = HitsOf(seats[i]); kills[i] = KillsOf(seats[i]); score[i] = ScoreOf(seats[i]); }
                using (Seats.Enter(seats[1])) e.Damage(15f, e.AimPoint, false);
                bool ok = HitsOf(seats[1]) == hits[1] + 1 && ScoreOf(seats[1]) > score[1];
                for (int i = 0; i < n; i++) if (i != 1) ok &= HitsOf(seats[i]) == hits[i] && ScoreOf(seats[i]) == score[i];
                Check($"colpo del giocatore 2 al nemico: colpi a segno e punti solo suoi ({ScoreOf(seats[1]) - score[1]} punti)", ok);

                score[1] = ScoreOf(seats[1]);
                using (Seats.Enter(seats[1])) e.Damage(5000f, e.AimPoint, true);
                ok = e.IsDead && KillsOf(seats[1]) == kills[1] + 1 && ScoreOf(seats[1]) > score[1];
                for (int i = 0; i < n; i++) if (i != 1) ok &= KillsOf(seats[i]) == kills[i] && ScoreOf(seats[i]) == score[i];
                Check($"uccisione del giocatore 2: l'uccisione e i punti sono solo suoi ({ScoreOf(seats[1]) - score[1]} punti)", ok);

                // un nemico da round (onnisciente) segue il piu' vicino
                yield return new WaitForSeconds(0.3f);
                Place(seats[0], start, yaw);
                Place(seats[1], far[1], 0f);
                yield return new WaitForSeconds(0.3f);
                var r = Enemy.Spawn(Enemy.EnemyType.Monaco, start + forward * 2.4f);
                if (r != null)
                {
                    r.relentless = true;
                    TgtField.SetValue(r, Targets.At(1));
                    yield return new WaitForSeconds(2.0f);
                    Check("nemico da round: lontano dal giocatore 2 e a due passi dal primo, segue il primo", TgtOf(r) == Targets.At(0));
                    using (Seats.Enter(seats[0])) r.Damage(5000f, r.AimPoint, false);
                }
                Enemy.ClearAll();
            }

            // ---- ferite, a terra, rianimazione e morte di un solo giocatore (0.7.2: a zero si va a terra, non si muore subito)
            {
                ParkAllBut(-1);
                for (int i = 0; i < n; i++) Place(seats[i], far[i], 0f);
                yield return new WaitForSeconds(0.3f);
                foreach (var s in seats) s.player.invulnerable = false;
                // le ferite del giocatore 2 le vede lui solo
                float[] hp = new float[n];
                for (int i = 0; i < n; i++) hp[i] = seats[i].player.hp;
                Targets.At(1).Hurt(10f);
                bool ok = seats[1].player.hp < hp[1];
                for (int i = 0; i < n; i++) if (i != 1) ok &= Mathf.Approximately(seats[i].player.hp, hp[i]);
                Check($"un colpo al bersaglio del giocatore 2: perde vita solo lui ({hp[1]:F0} -> {seats[1].player.hp:F0})", ok);

                var p1 = seats[1].player;
                int s1 = ScoreOf(seats[1]), s0 = ScoreOf(seats[0]), d0 = DeathsOf(seats[0]), d1 = DeathsOf(seats[1]);
                // a zero il giocatore 2 va a terra: vivo, ignorato dai nemici, nessuna morte e nessun punto perso
                Targets.At(1).Hurt(5000f);
                yield return null;
                ok = p1.alive && p1.Downed && !Targets.At(1).Alive && DeathsOf(seats[1]) == d1 && ScoreOf(seats[1]) == s1;
                for (int i = 0; i < n; i++) if (i != 1) ok &= seats[i].player.alive && !seats[i].player.Downed && Targets.At(i).Alive;
                Check($"a zero il giocatore 2 va a terra ({p1.BleedLeft:F0} s per rialzarlo): i nemici lo ignorano, nessuna morte, gli altri in piedi", ok);
                Check("con un giocatore a terra c'e' ancora chi e' vivo per i nemici", Targets.AnyAlive);

                // il giocatore 1 gli si mette accanto e tiene Usa: in tre secondi lo rialza (nello schermo condiviso senza rete)
                Place(seats[1], start, yaw);
                Place(seats[0], start + forward * 1.1f, yaw + Mathf.PI);
                yield return new WaitForSeconds(0.2f);
                if (shots) yield return Photo("4-giocatore-2-a-terra");
                float mid = -1f;
                seats[0].player.testHoldUse = true;                 // come se tenesse premuto Usa: lo legge il suo Update
                for (float t = 0f; t < PlayerController.ReviveTime + 0.8f && p1.Downed; t += Time.deltaTime)
                {
                    if (mid < 0f && t > PlayerController.ReviveTime * 0.5f) mid = p1.RisingProgress;
                    yield return null;
                }
                seats[0].player.testHoldUse = false;
                ok = p1.alive && !p1.Downed && Targets.At(1).Alive && Mathf.Abs(p1.hp - p1.MaxHp * 0.5f) < 1f && mid > 0.3f && DeathsOf(seats[1]) == d1;
                Check($"il giocatore 1 tiene Usa {PlayerController.ReviveTime:F0} s accanto al giocatore 2 e lo rialza (a meta' tempo {mid:P0}, vita {p1.hp:F0})", ok);

                // da solo, una volta per piano: tenendo Usa per cinque secondi
                yield return new WaitForSeconds(2.7f);              // la tregua dopo essersi rialzati
                ParkAllBut(1);
                yield return new WaitForSeconds(0.2f);
                Targets.At(1).Hurt(5000f);
                yield return null;
                bool down2 = p1.Downed && p1.SelfReviveAvailable;
                p1.testHoldUse = true;
                for (float t = 0f; t < PlayerController.SelfReviveTime + 1f && p1.Downed; t += Time.deltaTime) yield return null;
                p1.testHoldUse = false;
                Check("di nuovo a terra, il giocatore 2 si rialza da solo tenendo Usa (una volta per piano)", down2 && !p1.Downed && p1.alive && !p1.SelfReviveAvailable);

                // la terza volta nessuno lo rialza: si dissangua e muore come prima (punti dimezzati, una morte), gli altri restano in piedi
                yield return new WaitForSeconds(2.7f);
                PlayerController.bleedOutOverride = 1.0f;
                Targets.At(1).Hurt(5000f);
                yield return null;
                bool down3 = p1.Downed;
                yield return new WaitForSeconds(1.6f);
                PlayerController.bleedOutOverride = -1f;
                ok = down3 && !p1.alive && !p1.Downed && !Targets.At(1).Alive && DeathsOf(seats[1]) == d1 + 1 && DeathsOf(seats[0]) == d0 && ScoreOf(seats[0]) == s0 && ScoreOf(seats[1]) < s1;
                for (int i = 0; i < n; i++) if (i != 1) ok &= seats[i].player.alive && Targets.At(i).Alive;
                Check($"senza aiuto il giocatore 2 si dissangua: muore solo lui, una morte sua ({DeathsOf(seats[1])}), punti {s1} -> {ScoreOf(seats[1])}, gli altri vivi e coi loro punti", ok);
                Check("con un giocatore morto c'e' ancora chi e' vivo per i nemici", Targets.AnyAlive);
                if (shots) { yield return new WaitForSeconds(0.7f); yield return Photo("4b-giocatore-2-morto"); }
                yield return new WaitForSeconds(3.0f);
                ok = seats[1].player.alive && Targets.At(1).Alive && seats[1].player.hp > 50f;
                for (int i = 0; i < n; i++) if (i != 1) ok &= seats[i].player.alive;
                Check("il giocatore 2 rinasce da solo (vita piena) e gli altri non se ne accorgono", ok);
                foreach (var s in seats) s.player.invulnerable = true;
            }

            yield return Photo("5-dopo-la-rinascita");

            // ---- pausa: Start di un controller qualunque ferma il mondo per tutti e un altro Start lo rimette in moto (serve il menu: con -autotest non c'e')
            if (GameMenu.I != null)
            {
                for (int i = 0; i < n; i++) Place(seats[i], far[i], 0f);
                yield return new WaitForSeconds(0.4f);
                var still = new Vector3[n];
                for (int i = 0; i < n; i++) still[i] = seats[i].player.transform.position;
                int shotsPaused = ShotsOf(seats[1]);
                Check("prima della pausa il mondo gira", !GameMenu.Paused && !GameMenu.Blocked && Time.timeScale == 1f);

                var asker = seats[n - 1];
                PadControls.Inject(asker.pad, Pad(PadControls.StartBtn));
                yield return null; yield return null; yield return null;
                PadControls.Inject(asker.pad, null);
                yield return null; yield return null;
                bool huds = true;
                foreach (var s in seats) { var c = s.hud.GetComponent<Canvas>(); huds &= c != null && !c.enabled; }
                Check($"Start sul controller del giocatore {n}: la pausa e' di tutti (mondo fermo, comandi bloccati)", GameMenu.Paused && GameMenu.Blocked && Time.timeScale == 0f);
                Check("in pausa gli HUD di tutti i giocatori sono nascosti", huds);
                if (Seats.Monitors > 1)
                {
                    bool covers = true;
                    for (int d = 1; d < Seats.Monitors; d++) covers &= Seats.PauseCoverShown(d);
                    Check($"in pausa i monitor oltre il primo mostrano il velo (IN PAUSA): {Seats.Monitors - 1}", covers);
                }
                yield return Photo("6-pausa");

                // in pausa i controller non danno comandi di gioco: nessuno cammina, nessuno spara
                PadControls.Inject(seats[0].pad, Pad(ly: 32767));
                PadControls.Inject(seats[1].pad, Pad(rt: 255));
                yield return new WaitForSecondsRealtime(0.5f);
                PadControls.Inject(seats[0].pad, null);
                PadControls.Inject(seats[1].pad, null);
                bool frozen = ShotsOf(seats[1]) == shotsPaused;
                for (int i = 0; i < n; i++) frozen &= Flat(seats[i].player.transform.position, still[i]) < 0.05f;
                Check("in pausa nessuno cammina e nessuno spara, anche coi controller premuti", frozen);

                PadControls.Inject(seats[1].pad, Pad(PadControls.StartBtn));
                yield return null; yield return null; yield return null;
                PadControls.Inject(seats[1].pad, null);
                yield return null; yield return null;
                huds = true;
                foreach (var s in seats) { var c = s.hud.GetComponent<Canvas>(); huds &= c != null && c.enabled; }
                Check("Start di un altro giocatore riprende la partita per tutti (mondo in moto, HUD di nuovo visibili)", !GameMenu.Paused && !GameMenu.Blocked && Time.timeScale == 1f && huds);
                if (Seats.Monitors > 1)
                {
                    bool gone = true;
                    for (int d = 1; d < Seats.Monitors; d++) gone &= !Seats.PauseCoverShown(d);
                    Check("ripreso il gioco il velo dei monitor oltre il primo e' tolto", gone);
                }
            }
            else Debug.Log("[PryingEyes] seatstest: partita avviata senza menu (-autotest): niente prova della pausa");

            yield return CoveredWindow();

            // ---- fine dello schermo condiviso: si torna a un giocatore solo e funziona
            {
                var player0 = seats[0].player;
                var cam0 = seats[0].camera;
                var eq0 = Equipment.I; var torch0 = Torch.I; var knife0 = Knife.I; var rig0 = StealthRig.I;
                int monitorsBefore = Seats.Monitors;
                Seats.End();
                yield return null; yield return null;
                if (shots && monitorsBefore > 1 && SplitScreen.RealMonitors >= monitorsBefore)
                    Check($"fine: le finestre dei monitor accesi sono nascoste ({SplitScreen.VisibleWindows()} finestra visibile)", SplitScreen.VisibleWindows() == 1);
                int bodies = FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length;
                int listeners = 0;
                foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (l.enabled) listeners++;
                Check("fine: non c'e' piu' lo schermo condiviso e c'e' un solo bersaglio", !Seats.Split && Seats.Count == 0 && Targets.Count == 1);
                Check($"fine: resta un solo giocatore ({bodies} corpi) e un solo AudioListener ({listeners})", bodies == 1 && listeners == 1 && gm.player == player0);
                Check("fine: la camera del giocatore 1 e' di nuovo a tutto schermo, sul monitor della finestra del gioco", Same(cam0.rect, new Rect(0f, 0f, 1f, 1f)) && cam0.targetDisplay == 0 && Seats.Monitors == 1);
                Check("fine: gli oggetti del giocatore 1 sono ancora i suoi (equipaggiamento, torcia, coltello, rete di furtivita')",
                    Equipment.I == eq0 && Torch.I == torch0 && Knife.I == knife0 && StealthRig.I == rig0 && eq0 != null && rig0 != null);

                // e il giocatore singolo cammina col suo controller come prima
                PadControls.Enable(0);
                player0.invulnerable = true;
                player0.Teleport(start + Vector3.up * 0.05f); player0.yaw = yaw; player0.pitch = 0f;
                yield return new WaitForSeconds(0.4f);
                Vector3 p0 = player0.transform.position;
                PadControls.Inject(Pad(ly: 32767));
                yield return new WaitForSeconds(1.0f);
                PadControls.Inject(null);
                float along = Vector3.Dot(player0.transform.position - p0, forward);
                Check($"dopo la fine il giocatore singolo cammina col controller ({along:F1} m in 1 s)", along > 2f);
            }
        }

        // ------------------------------------------------------------------ struttura
        void Structure(Seat[] seats, int n)
        {
            var bodies = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            Check($"{n} corpi di giocatore, uno per posto", bodies.Length == n);

            bool parts = true, distinct = true;
            for (int i = 0; i < n; i++)
            {
                var s = seats[i];
                parts &= s.player != null && s.hud != null && s.camera != null && s.target != null && s.pad != null && s.index == i;
                parts &= i == 0 ? s.root == null && s.hud.seat == null : s.root != null && s.hud.seat == s;       // il posto 1 e' la scena di sempre
                for (int j = 0; j < i; j++)
                    distinct &= s.player != seats[j].player && s.hud != seats[j].hud && s.camera != seats[j].camera && s.target != seats[j].target
                        && s.pad != seats[j].pad && s.stealth != seats[j].stealth && s.medals != seats[j].medals && s.interact != seats[j].interact && s.wallBuy != seats[j].wallBuy;
            }
            Check("ogni posto ha corpo, camera, HUD, bersaglio e controller, e il suo stato (furtivita', medaglie, interazioni, acquisti)", parts && distinct);

            // 0.7.1: ognuno vede il corpo degli altri (SeatAvatar) e non il proprio
            bool avatars = true, seeOthers = true, nearBody = true;
            for (int i = 0; i < n; i++)
            {
                var s = seats[i];
                int layer = Seats.FirstAvatarLayer + i;
                avatars &= s.avatar != null && s.avatar.Seat == s;
                if (s.avatar != null) foreach (var r in s.avatar.GetComponentsInChildren<Renderer>(true)) avatars &= r.gameObject.layer == layer;
                if (s.avatar != null && s.player != null) nearBody &= Vector3.Distance(s.avatar.transform.position, s.player.transform.position) < 0.5f;
                if (s.camera == null) { seeOthers = false; continue; }
                for (int j = 0; j < n; j++)
                {
                    bool sees = (s.camera.cullingMask & (1 << (Seats.FirstAvatarLayer + j))) != 0;
                    seeOthers &= j == i ? !sees : sees;
                }
            }
            Check("ognuno ha un corpo che gli altri vedono, su uno strato suo (manichino del posto)", avatars);
            Check("la camera di ognuno disegna i corpi degli altri e non il proprio", seeOthers);
            Check("il corpo visto dagli altri sta dove sta il giocatore", nearBody);
            Check("solo il posto 1 riceve tastiera e mouse", seats[0].keyboard && Count(seats, s => s.keyboard) == 1);

            // camere: ognuna nella sua porzione del suo monitor, senza buchi e senza sovrapposizioni (su ogni monitor)
            int monitors = Seats.Monitors;
            Check($"{monitors} monitor in uso (voluti {SplitScreen.WantedMonitors}, fra quelli collegati {SplitScreen.ConnectedMonitors} e al piu' uno per giocatore)", monitors == SplitScreen.MonitorsFor(n, SplitScreen.WantedMonitors));
            bool rects = true, disjoint = true, onDisplays = true;
            var area = new float[monitors];
            for (int i = 0; i < n; i++)
            {
                var cam = seats[i].camera;
                int d = SplitScreen.DisplayOf(i, n, monitors);
                rects &= cam != null && Same(cam.rect, SplitScreen.ViewOn(i, n, monitors)) && Same(seats[i].view, SplitScreen.ViewOn(i, n, monitors));
                onDisplays &= cam != null && cam.targetDisplay == d && seats[i].display == d;
                if (cam != null) area[d] += cam.rect.width * cam.rect.height;
                for (int j = 0; j < i; j++) if (seats[j].camera != null && cam != null && seats[j].camera.targetDisplay == cam.targetDisplay) disjoint &= Overlap(cam.rect, seats[j].camera.rect) < 1e-5f;
            }
            bool covered = true; string areas = "";
            for (int d = 0; d < monitors; d++) { covered &= Mathf.Abs(area[d] - 1f) < 1e-3f; areas += (d > 0 ? " + " : "") + area[d].ToString("F3"); }
            Check($"{n} camere ognuna nella sua porzione (SplitScreen.ViewOn)", rects);
            Check($"ogni camera disegna sul monitor del suo giocatore (Camera.targetDisplay da {SplitScreen.DisplayOf(0, n, monitors)} a {SplitScreen.DisplayOf(n - 1, n, monitors)})", onDisplays);
            Check($"le porzioni non si sovrappongono e coprono ogni monitor (area {areas})", disjoint && covered);

            // audio: un solo ascoltatore, sul giocatore 1
            Camera main = Camera.main;
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            int on = 0; AudioListener active = null;
            foreach (var l in listeners) if (l.enabled && l.gameObject.activeInHierarchy) { on++; active = l; }
            Check($"un solo AudioListener attivo ({on}), sulla camera del giocatore 1", on == 1 && active.gameObject == seats[0].camera.gameObject);
            Check("la camera principale e' quella del giocatore 1", main == seats[0].camera);

            // strati: ognuno vede il proprio arma e le proprie mani, non quelle degli altri; i corpi degli altri si', il proprio no (0.7.1)
            bool masks = true;
            int special = Seats.AllViewLayersMask | Seats.AllAvatarLayersMask;
            int baseMask = seats[0].camera.cullingMask & ~special;
            for (int i = 0; i < n; i++)
            {
                int mask = seats[i].camera.cullingMask, own = 1 << seats[i].viewLayer, ownBody = 1 << (Seats.FirstAvatarLayer + i);
                masks &= (mask & own) != 0 && (mask & Seats.AllViewLayersMask & ~own) == 0 && (mask & ~special) == baseMask
                         && (mask & ownBody) == 0 && (mask & Seats.AllAvatarLayersMask) == (Seats.AllAvatarLayersMask & ~ownBody);
            }
            Check("maschera di ogni camera: il resto del mondo, il proprio strato di arma e mani e i corpi degli altri", masks);
            bool layers = true, any = true;
            for (int i = 0; i < n; i++)
            {
                bool own = false;
                foreach (var r in seats[i].camera.GetComponentsInChildren<Renderer>(true))
                {
                    int l = r.gameObject.layer;
                    if (l == seats[i].viewLayer) own = true;
                    else if (l >= Seats.FirstViewLayer && l < Seats.FirstViewLayer + Seats.MaxSeats) layers = false;      // un arma sullo strato di un altro
                }
                any &= own;
            }
            Check("arma e mani di ogni giocatore stanno sul suo strato (e su nessun altro)", layers && any);

            // un canvas per monitor, e l'HUD di ogni giocatore sotto quello del suo monitor
            bool canvases = true;
            for (int i = 0; i < n; i++)
            {
                var own = seats[i].hud.GetComponentInParent<Canvas>();
                var root = own != null ? own.rootCanvas : null;
                canvases &= root != null && root.renderMode == RenderMode.ScreenSpaceOverlay && root.targetDisplay == seats[i].display;
            }
            Check($"l'HUD di ogni giocatore sta sotto il canvas del suo monitor (Canvas.targetDisplay = monitor del giocatore, {monitors} canvas)", canvases);

            // HUD: ognuno nel suo rettangolo di schermo (in pixel del suo monitor)
            bool sized = true;
            for (int d = 0; d < monitors; d++) { var sz = SplitScreen.DisplaySize(d); sized &= sz.x > 0 && sz.y > 0; }
            if (sized)
            {
                bool huds = true; string detail = ""; int pretend = 0;
                var corners = new Vector3[4];
                for (int i = 0; i < n; i++)
                {
                    // un monitor finto (-splitmonitors N su un PC con meno monitor) ha il canvas della misura che Unity gli da' (4K), non quella di DisplaySize:
                    // li' si guardano solo struttura e assegnazioni, la misura si controlla sui monitor veri
                    if (seats[i].display >= SplitScreen.RealMonitors) { pretend++; continue; }
                    var rt = seats[i].hud.GetComponent<RectTransform>();
                    rt.GetWorldCorners(corners);
                    var v = seats[i].view; var size = SplitScreen.DisplaySize(seats[i].display);
                    float x0 = v.x * size.x, y0 = v.y * size.y, x1 = v.xMax * size.x, y1 = v.yMax * size.y;
                    bool ok = Mathf.Abs(corners[0].x - x0) < 2f && Mathf.Abs(corners[0].y - y0) < 2f && Mathf.Abs(corners[2].x - x1) < 2f && Mathf.Abs(corners[2].y - y1) < 2f;
                    if (!ok) detail += $" [{i + 1}: ({corners[0].x:F0},{corners[0].y:F0})-({corners[2].x:F0},{corners[2].y:F0}) attesi ({x0:F0},{y0:F0})-({x1:F0},{y1:F0})]";
                    huds &= ok;
                }
                Check($"l'HUD di ogni giocatore sta nel rettangolo della sua porzione (monitor: {SplitScreen.DescribeDisplays(monitors)}{(pretend > 0 ? $"; {pretend} su monitor finti, non misurati" : "")}){detail}", huds);
            }
            else Debug.Log("[PryingEyes] seatstest: schermo senza misura (prova senza finestra), niente controllo dei rettangoli degli HUD");

            // arrivo: uno accanto all'altro, non sulla stessa mattonella
            bool side = true;
            float maxD = 0f, minD = 99f;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < i; j++)
                {
                    float d = Flat(seats[i].player.transform.position, seats[j].player.transform.position);
                    maxD = Mathf.Max(maxD, d); minD = Mathf.Min(minD, d);
                }
            side = minD > 0.2f && maxD < 3f;
            Check($"all'arrivo i giocatori stanno uno accanto all'altro (distanze {minD:F2}-{maxD:F2} m)", side);

            // i bersagli dei nemici
            bool targets = Targets.Count == n;
            for (int i = 0; i < n && targets; i++) targets &= Targets.At(i).Slot == i && Seats.SeatOf(Targets.At(i)) == seats[i] && seats[i].target == Targets.At(i);
            targets &= Targets.At(0) == Targets.Local && !(seats[1].target is LocalTarget);
            Check($"{n} bersagli per i nemici, uno per posto (slot 0..{n - 1}), il primo e' quello locale", targets);
            Check("Seats.Slot e' lo slot del posto che gira (a riposo il primo)", Seats.Slot == 0 && Seats.OfSlot(1) == seats[1] && Seats.OfSlot(n) == null);
            Check("lo schermo condiviso non e' la cooperativa in rete", !Coop.Active);
        }

        void ContextSwap(Seat[] seats)
        {
            int visits = 0; bool swap = true, slot = true, owned = true;
            string problem = "";
            Seats.ForEach(s =>
            {
                visits++;
                var gm = GameManager.I;
                swap &= Seats.Current == s && Seats.SeatIndex == s.index && gm.player == s.player && gm.hud == s.hud && Seats.Keyboard == s.keyboard && Seats.ViewLayer == s.viewLayer;
                swap &= Stealth.Current == s.stealth && Medals.Current == s.medals && Proc.Interact.Mine == s.interact && WallBuy.Mine == s.wallBuy && PadControls.Mine == s.pad;
                slot &= Seats.Slot == s.index && Seats.MyTarget == s.target;
                // gli oggetti installati sono quelli del corpo di questo posto
                var parts = new Component[] { Equipment.I, Knife.I, Torch.I, Guide.I, StealthRig.I };
                foreach (var c in parts)
                {
                    if (c == null) { owned = false; problem += $" [posto {s.index + 1}: manca un oggetto]"; continue; }
                    var owner = Seats.OwnerOf(c.transform);
                    if (s.index == 0 ? owner != null : owner != s) { owned = false; problem += $" [posto {s.index + 1}: {c.GetType().Name} di un altro]"; }
                }
            });
            Check($"Seats.ForEach passa da tutti i {seats.Length} posti ({visits}) e a ognuno mette i suoi globali (giocatore, HUD, furtivita', medaglie, interazioni, controller)", visits == seats.Length && swap);
            Check("dentro un posto lo slot e il bersaglio sono i suoi", slot);
            Check("dentro un posto equipaggiamento, coltello, torcia, guida e rete di furtivita' sono quelli del suo corpo" + problem, owned);
            bool distinct = true;
            for (int i = 0; i < seats.Length; i++)
                for (int j = 0; j < i; j++)
                    distinct &= seats[i].equipment != seats[j].equipment && seats[i].knife != seats[j].knife && seats[i].torch != seats[j].torch
                        && seats[i].guide != seats[j].guide && seats[i].rig != seats[j].rig;
            Check("equipaggiamento, coltello, torcia, guida e rete di furtivita' sono distinti fra i posti (dopo il giro)", distinct);
            Check("finito il giro e' di nuovo installato il posto 1", Seats.Current == seats[0] && GameManager.I.player == seats[0].player && GameManager.I.hud == seats[0].hud);
        }

        static int Count(Seat[] seats, Func<Seat, bool> f) { int c = 0; foreach (var s in seats) if (f(s)) c++; return c; }
    }
}
#endif
