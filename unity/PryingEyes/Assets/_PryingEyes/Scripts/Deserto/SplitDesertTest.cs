#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Cattedrale;
using PryingEyes.Proc;
using PryingEyes.Testing;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>-splitdesertotest -splitscreen N -deserto -catacombe -autotest [-seed S] [-splitmonitors M] [-seatsshots]: il deserto nello schermo condiviso (0.9.1), provato da dentro. Parte la
    /// partita nel deserto con N posti (come dal menu: "Chi gioca?", poi la scelta della partita) e si controlla che ogni posto abbia il suo mondo attorno: la sua camera ha il cielo (piano
    /// lontano e sfondo), gli effetti (sabbia che vola, animali, veli di sabbia, diavoli) nascono per ognuno, il posto 2 portato a due chilometri dal primo trova la sabbia con il collider,
    /// la navmesh, le cose sulla sabbia, il luogo costruito e la sua pelle da lontano (e il primo non perde niente), il bottino che il posto raccoglie e' suo (punti suoi, non del primo),
    /// l'obiettivo di ognuno dice dove sta lui, la minimappa di ciascuno mostra gli altri solo se sono nella finestra, il bordo del mondo riporta dentro solo chi lo oltrepassa; ognuno ha la sua
    /// moto e il posto 2 sale in sella mentre gli altri restano a piedi. Con la grafica (anche senza finestra: -batchmode senza -nographics) fotografa il posto 2 in sella visto dal posto 1
    /// (hotel_build/deserto/split_moto_posto2_in_sella.png) e il cielo di ogni camera (hotel_build/deserto/split_postoN.png: non deve essere lo sfondo vuoto) e, con -seatsshots su piu' monitor veri, chiede allo script
    /// (tools/coop/run_split.ps1 -Desert -Graphics -Monitors N) le foto di sistema di ogni monitor. Lo lancia run_split.ps1 -Desert; documentazione in docs/multiplayer/split-screen.md.</summary>
    public sealed class SplitDesertTest : PlayerTest
    {
        bool shots, systemPhotos = true;

        protected override string Suite { get { return "splitdesertotest"; } }
        protected override float TimeLimit { get { return 420f; } }
        protected override string ExceptionCheck { get { return "nessun errore o eccezione nel registro durante la prova"; } }
        protected override bool IsError(string message, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return false;
            return !(Application.isBatchMode && message != null && message.StartsWith("Failed to capture screen shot"));      // messaggio noto di Unity senza finestra, innocuo (come in SeatsTest)
        }
        protected override void Finish() { Time.timeScale = 1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!TestRun.Has("-splitdesertotest")) return;
            Application.runInBackground = true;         // a finestra aperta ma senza il fuoco il gioco (runInBackground e' spento nel progetto) si fermerebbe: la prova no
            Fabbrica.Azzera();                          // profilo di prova: una cattedrale in rovina e un magazzino vuoto (come DesertTest)
            InstallWhen<SplitDesertTest>("-splitdesertotest");
        }

        static FloorDirector Fd => FloorDirector.I;
        static DesertDirector Dd => DesertDirector.I;

        void Update()
        {
            // il codice del gioco toglie l'invulnerabilita' in piu' punti (a fine dell'arrivo): chi prova a camminare lontano non deve finire a terra
            foreach (var s in Seats.All) if (s.player != null && s.player.alive && !s.player.invulnerable) s.player.invulnerable = true;
        }

        // ------------------------------------------------------------------ strumenti
        IEnumerator WaitFor(Func<bool> cond, float timeout)
        {
            float t0 = Time.time;
            while (Time.time - t0 < timeout)
            {
                bool ok;
                try { ok = cond(); } catch { ok = false; }
                if (ok) yield break;
                yield return null;
            }
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        static float FootDist(Site s, Vector3 p)
        {
            float dx = Mathf.Max(Mathf.Max(s.fx0 - p.x, p.x - s.fx1), 0f), dz = Mathf.Max(Mathf.Max(s.fz0 - p.z, p.z - s.fz1), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static int ScoreOf(Seat s) => s == Seats.Current ? GameManager.I.score : s.score;

        static Vector3 Pos(Seat s) => s.player.transform.position;

        static bool Grounded(Seat s) => s.player.GetComponent<CharacterController>().isGrounded;

        /// <summary>Porta un posto sulla sabbia in un punto (un po' in alto: la sabbia sotto nasce mentre cade) e aspetta che tocchi terra.</summary>
        IEnumerator Land(Seat s, float x, float z, float yaw, float timeout = 45f)
        {
            s.player.Teleport(new Vector3(x, DesertWorld.Height(x, z) + 8f, z));
            s.player.yaw = yaw; s.player.pitch = 0f;
            yield return null;
            yield return WaitFor(() => DesertTerrain.I != null && DesertTerrain.I.Ready(Pos(s), 14f), timeout);
            yield return WaitFor(() => Grounded(s), 15f);
        }

        /// <summary>Un luogo (non una torre) lontano dalla cattedrale: dove portare un posto.</summary>
        static Site FindFarSite(float minRadius)
        {
            var list = new List<Site>();
            for (int k = 0; k < 140; k++)
            {
                float a = (k * 47f + 20f) * Mathf.Deg2Rad, r = minRadius + 50f * (k % 9) + 30f * (k / 9);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                DesertWorld.SitesNear(x, z, 150f, list);          // (anche quelli non ancora fatti: a due chilometri dalla cattedrale nessuno li ha mai guardati)
                foreach (var s in list) if (!s.IsTower && DesertWorld.DistToCathedral(s.x, s.z) > minRadius - 100f) return s;
            }
            return null;
        }

        static Texture2D RenderOnce(Camera cam, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24);
            Rect oldRect = cam.rect; var oldTarget = cam.targetTexture;
            cam.rect = new Rect(0f, 0f, 1f, 1f); cam.targetTexture = rt;
            cam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = oldTarget; cam.rect = oldRect;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>Il colore medio della striscia in alto della foto (il cielo, a camera dritta).</summary>
        static Color TopStrip(Texture2D tex)
        {
            int w = tex.width, h = tex.height, rows = Mathf.Max(2, h / 12);
            var px = tex.GetPixels(0, h - rows, w, rows);
            float r = 0f, g = 0f, b = 0f;
            foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
            float n = px.Length;
            return new Color(r / n, g / n, b / n);
        }

        /// <summary>Con piu' monitor veri si chiede allo script di fotografare ogni monitor dal sistema (NOME-monitorN.png in unity_logs/seats) e si aspetta il suo "fatto"; senza, e' ScreenCapture.</summary>
        IEnumerator MonitorPhoto(string name)
        {
            if (!shots) yield break;
            string folder = Path.GetFullPath("unity_logs/seats");
            Directory.CreateDirectory(folder);
            if (Seats.Monitors > 1 && SplitScreen.RealMonitors >= Seats.Monitors)
            {
                if (!systemPhotos) yield break;
                string done = Path.Combine(folder, name + ".done");
                try { File.Delete(done); } catch (Exception) { }
                yield return null; yield return null;
                Debug.Log("[PryingEyes] splitdesertotest: foto-richiesta: " + name);
                float until = Time.realtimeSinceStartup + 20f;
                while (!File.Exists(done) && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.1f);
                if (!File.Exists(done)) { systemPhotos = false; Debug.Log("[PryingEyes] splitdesertotest: foto-monitor: nessuno le fa (serve run_split.ps1 -Desert -Graphics -Monitors N): niente foto"); }
                yield break;
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
            yield return null; yield return null;
        }

        // ------------------------------------------------------------------ la prova
        protected override IEnumerator Run()
        {
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-seatsshots") >= 0 && !Application.isBatchMode;
            Application.targetFrameRate = 60;           // a 10000 fotogrammi al secondo il CharacterController non cammina come dovrebbe
            QualitySettings.vSyncCount = 0;
            yield return null;                          // GameManager.Start avvia lo schermo condiviso nello stesso fotogramma
            int n = SplitScreen.AutoStartCount;
            Check($"-splitscreen {n}: un solo processo con {n} posti", n >= SplitScreen.MinPlayers && Seats.Split && Seats.Count == n);
            if (!Seats.Split) yield break;
            yield return WaitFor(() => Fd != null && Fd.InHub && !Fd.transitioning && Fd.cur != null && Fd.Desert != null && DesertMode.Active, 150f);
            if (!Check("la partita e' nel deserto, con la cattedrale costruita", DesertMode.Active && Fd != null && Fd.InHub && Fd.Desert != null && DesertTerrain.I != null && DesertSky.I != null,
                    Fd == null ? "nessun regista" : $"Active {DesertMode.Active}, InHub {Fd.InHub}")) yield break;
            yield return new WaitForSeconds(2f);
            var seats = Seats.All.ToArray();

            yield return Arrival(seats);
            yield return Bikes(seats);
            yield return FarSeat(seats);
            yield return Loot(seats);
            yield return Texts(seats);
            yield return Skies(seats);
            yield return TowerLevels(seats);
            yield return Edge(seats);
            yield return Together(seats);
        }

        IEnumerator Arrival(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- arrivo");
            int n = seats.Length;
            bool cams = true; string camText = "";
            foreach (var s in seats)
            {
                bool ok = s.camera != null && s.camera.farClipPlane >= 1499f && s.camera.clearFlags == CameraClearFlags.SolidColor;
                cams &= ok;
                camText += $" posto {s.index + 1}: piano lontano {(s.camera != null ? s.camera.farClipPlane : 0f):F0} sfondo {(s.camera != null ? s.camera.clearFlags.ToString() : "-")};";
            }
            Check("ogni camera ha il piano lontano a 1500 m e lo sfondo a colore pieno (il suo cielo)", cams, camText);
            var dust = FindObjectsByType<DesertDust>(FindObjectsSortMode.None);
            int life = FindObjectsByType<DesertLife>(FindObjectsSortMode.None).Length, veils = FindObjectsByType<DesertSpindrift>(FindObjectsSortMode.None).Length;
            int devils = FindObjectsByType<DesertDevils>(FindObjectsSortMode.None).Length, fires = FindObjectsByType<DesertFires>(FindObjectsSortMode.None).Length;
            Check($"gli effetti hanno un'istanza per posto (sabbia {dust.Length}, animali {life}, veli {veils}, diavoli {devils}) e i fuochi una sola ({fires})",
                dust.Length == n && life == n && veils == n && devils == n && fires == 1);
            int nulls = 0; var views = new HashSet<Camera>();
            foreach (var d in dust) { if (d.view == null) nulls++; else views.Add(d.view); }
            bool mine = nulls == 1 && views.Count == n - 1;
            for (int i = 1; i < n; i++) mine &= views.Contains(seats[i].camera);
            Check("la sabbia che vola di ogni posto dopo il primo guarda la camera del suo posto", mine, $"{nulls} senza camera, {views.Count} camere");
            bool home = true; float minGap = 1e9f;
            for (int i = 0; i < n; i++)
            {
                home &= DesertWorld.DistToCathedral(Pos(seats[i]).x, Pos(seats[i]).z) < 45f;
                for (int j = i + 1; j < n; j++) minGap = Mathf.Min(minGap, Flat(Pos(seats[i]), Pos(seats[j])));
            }
            Check("tutti i posti nascono in cima alla scalinata, uno accanto all'altro", home && minGap > 0.2f, $"il minimo tra due posti {minGap:F2} m");
            bool alive = true; foreach (var s in seats) alive &= s.player.alive;
            Check("tutti i giocatori sono vivi e a terra", alive && Grounded(seats[0]));
            for (int i = 0; i < n; i++) Check($"il posto {i + 1} ha il suo HUD con l'obiettivo del deserto", seats[i].hud != null && seats[i].hud.DesertObjectiveShown.Contains("Torri e catacombe"), seats[i].hud != null ? seats[i].hud.DesertObjectiveShown : "senza HUD");
            yield return null;
        }

        /// <summary>L'avatar (il corpo che vedono gli altri posti) di un posto.</summary>
        static SeatAvatar AvatarOf(Seat s)
        {
            foreach (var a in FindObjectsByType<SeatAvatar>(FindObjectsSortMode.None)) if (a.Seat == s) return a;
            return null;
        }

        /// <summary>Il posto 'from' guarda il posto 'to' e lo fotografa dal suo punto di vista (hotel_build/deserto/&lt;file&gt;, 1920x1080).</summary>
        IEnumerator SnapRider(Seat from, Seat to, string file)
        {
            for (int k = 0; k < 2; k++)
            {
                Vector3 look = Pos(to) + Vector3.up * 0.9f - from.player.cam.position;
                from.player.yaw = Mathf.Atan2(look.x, look.z);
                from.player.pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude);
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            var shot = RenderOnce(from.camera, 1920, 1080);
            string dir = TestPaths.Out("deserto");
            Directory.CreateDirectory(dir);
            try { File.WriteAllBytes(Path.Combine(dir, file), shot.EncodeToPNG()); } catch (Exception ex) { Debug.Log("[PryingEyes] splitdesertotest: foto non scritta: " + ex.Message); }
            Destroy(shot);
        }

        /// <summary>La moto nello schermo condiviso: una per posto, ognuna al suo posto di nascita; il posto 2 sale e guida (comandi forzati dalla prova) mentre gli altri restano a piedi, il suo corpo
        /// visto dagli altri e' in sella e senza le braccia della moto sul manubrio, e quando scende la moto resta li' parcheggiata.</summary>
        IEnumerator Bikes(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- moto");
            int n = seats.Length;
            var bikes = new List<DesertBike>();
            foreach (var b in DesertBike.All) if (b != null && !b.puppet) bikes.Add(b);
            bool oneEach = bikes.Count == n;
            DesertBike mine = null;
            for (int i = 0; i < n; i++)
            {
                bool has = false;
                foreach (var b in bikes) if (b.slot == i && Flat(b.transform.position, DesertBikes.HomeFor(i)) < 0.5f) { has = true; if (i == 1) mine = b; }
                oneEach &= has;
            }
            Check($"c'e' una moto per ognuno dei {n} posti, ognuna al suo posto di nascita", oneEach, $"{bikes.Count} moto");
            if (mine == null) yield break;
            var s0 = seats[0]; var s1 = seats[1]; var p1 = s1.player;
            Vector3 origin0 = Pos(s0), home0 = origin0;
            bool snap = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;          // con la scheda grafica (anche senza finestra) si fotografa il pilota visto dal posto 1
            if (snap)
            {
                // con la grafica il posto 1 si mette sulla sabbia a sette metri dalla strada della moto, girato verso di lei: la foto di com'e' il posto 2 in sella visto da un altro posto
                PlayerController.holdLook = true;
                s0.player.Teleport(new Vector3(mine.home.x - 7f, DesertWorld.Height(mine.home.x - 7f, mine.home.z - 6f) + 0.2f, mine.home.z - 6f));
                yield return null;
                yield return WaitFor(() => Grounded(s0), 5f);
                home0 = Pos(s0);
            }
            p1.Mount(mine);
            yield return null;
            Check("il posto 2 sale in sella e gli altri restano a piedi", p1.Riding && mine.rider == p1 && !s0.player.Riding && !(n > 2 && seats[2].player.Riding));
            Check("in uno schermo condiviso la moto non mostra le braccia del pilota (le vedrebbero gli altri posti)", !mine.ArmsShown);
            DesertBike.botThrottle = 1f;
            if (snap)
            {
                // con la grafica i fotogrammi sono lunghi e la guida va a passi di 0,05 s al massimo: si aspetta la strada fatta, non i secondi
                yield return WaitFor(() => Flat(Pos(s1), mine.home) > 3.5f, 8f);
                yield return SnapRider(s0, s1, "split_moto_posto2_in_sella.png");
                var avFire = AvatarOf(s1);
                // e in curva (a sinistra, lontano dal posto 1): il corpo si inclina con la moto
                DesertBike.botThrottle = 0.5f; DesertBike.botSteer = -0.8f;
                yield return WaitFor(() => Mathf.Abs(mine.RollVisual) > 10f, 6f);
                yield return SnapRider(s0, s1, "split_moto_posto2_in_curva.png");
                // e fermo, col fucile e col posto 1 davanti a destra: la destra impugna l'arma, che gli altri posti gli vedono in mano (dal lato sinistro il corpo la nasconderebbe)
                DesertBike.botSteer = 0f; DesertBike.botThrottle = 0f;
                yield return WaitFor(() => Mathf.Abs(p1.RideSpeed) < 0.6f, 10f);
                float hd = p1.RideHeading;
                Vector3 spot = Pos(s1) + new Vector3(Mathf.Sin(hd), 0f, Mathf.Cos(hd)) * 2.0f + new Vector3(Mathf.Cos(hd), 0f, -Mathf.Sin(hd)) * 2.6f;
                s0.player.Teleport(new Vector3(spot.x, DesertWorld.Height(spot.x, spot.z) + 0.2f, spot.z));
                yield return null;
                yield return WaitFor(() => Grounded(s0), 5f);
                p1.weapon.Equip(WeaponDefs.IndexOf("ar9_malleus"));
                p1.weapon.mag = p1.weapon.magSize; p1.weapon.reserve = 120;
                Weapon.rideBotFire = true;
                yield return WaitFor(() => avFire != null && avFire.Body != null && avFire.Body.HasWeapon && p1.weapon.RideGunUp, 3f);
                yield return new WaitForSeconds(0.25f);
                yield return SnapRider(s0, s1, "split_moto_posto2_spara.png");
                Weapon.rideBotFire = false;
                s0.player.Teleport(home0);
                yield return null;
                yield return WaitFor(() => Grounded(s0), 5f);
                DesertBike.botThrottle = 1f;
            }
            yield return WaitFor(() => Flat(Pos(s1), mine.home) > 20f && p1.RideSpeed > 5f, 12f);
            DesertBike.botThrottle = 0f;
            float went = Flat(Pos(s1), mine.home);
            Check("il posto 2 ha corso con la sua moto", went > 15f && p1.RideSpeed > 5f, $"{went:F0} m, {p1.RideSpeed * 3.6f:F0} km/h");
            Check("il posto 1 non si e' mosso", Flat(Pos(s0), home0) < 3f && !s0.player.Riding);
            Check("in sella la minimappa del posto 2 non segna la sua moto (il pilota e' la freccia rossa)", s1.hud.DesertMapShown && !s1.hud.DesertMapBikeShown, $"mappa accesa {s1.hud.DesertMapShown}, tondo {s1.hud.DesertMapBikeShown}");
            Check("in sella il posto 2 ha il suo tachimetro sulla sua porzione di schermo, e gli altri posti no", s1.hud.SpeedoShown && s1.hud.SpeedoKmh >= 12 && !s0.hud.SpeedoShown && !(n > 2 && seats[2].hud.SpeedoShown),
                $"posto 2: acceso {s1.hud.SpeedoShown}, {s1.hud.SpeedoKmh} km/h; posto 1 acceso {s0.hud.SpeedoShown}");
            SeatAvatar av = AvatarOf(s1);
            // il corpo vero (con i modelli) sta seduto: bacino sulla sella, piedi sulle pedane, mani sulle manopole (SurvivorBody, posa 'seated'); senza i modelli resta il manichino accovacciato
            bool seatedOk = av != null && av.Body != null && av.Body.input.seated && av.Body.input.velocity.sqrMagnitude < 0.01f;
            if (av != null && av.Body != null) yield return WaitFor(() => av.Body.SeatAmount > 0.99f, 3f);
            Check("il corpo del posto 2 visto dagli altri e' seduto in sella, con le gambe ferme", seatedOk && av.Body.SeatAmount > 0.99f, av != null ? (av.Body != null ? $"seduto {av.Body.input.seated}, posa {av.Body.SeatAmount:F2}" : "senza corpo") : "nessun avatar");
            Check("e le sue mani arrivano alle manopole (le due mani a meno di 12 cm dalle manopole, in tutto)", seatedOk && av.Body.RideHandsOff < 0.12f, seatedOk ? $"{av.Body.RideHandsOff * 100f:F1} cm in tutto" : "non seduto");
            // spara dalla sella: la destra impugna l'arma, la sinistra resta sul manubrio; lasciato fuoco l'arma sparisce
            if (seatedOk)
            {
                Weapon.rideBotFire = true;
                yield return WaitFor(() => av.Body.HasWeapon && p1.weapon.RideGunUp, 3f);
                Check("il posto 2 spara dalla sella e gli altri posti gli vedono l'arma in mano (e la destra non e' piu' sul manubrio)", av.Body.HasWeapon && p1.weapon.RideGunUp && p1.weapon.mag >= 0, $"arma in mano {av.Body.HasWeapon}, alzata {p1.weapon.RideGunUp}");
                Weapon.rideBotFire = false;
                yield return WaitFor(() => !av.Body.HasWeapon, 5f);
                Check("lasciato fuoco l'arma sparisce dalla sua mano e le sue mani tornano sulle manopole", !av.Body.HasWeapon, $"arma in mano {av.Body.HasWeapon}");
                yield return WaitFor(() => av.Body.RideHandsOff < 0.12f, 2f);
                Check("e le due mani sono di nuovo a meno di 12 cm dalle manopole", av.Body.RideHandsOff < 0.12f, $"{av.Body.RideHandsOff * 100f:F1} cm in tutto");
            }
            p1.RideRequestDismount();
            yield return WaitFor(() => !p1.Riding, 20f);
            Check("il posto 2 scende e la sua moto resta parcheggiata dov'e'", !p1.Riding && mine.rider == null && mine.ColliderOn);
            yield return null; yield return null;
            Check("scesi, il tachimetro del posto 2 si spegne", !s1.hud.SpeedoShown);
            // la minimappa di ogni posto segna la moto di quel posto, non quella di un altro (nate a 1,8 m una dall'altra, sulla carta sono 0,7 px: la tolleranza e' 0,25)
            yield return WaitFor(() => s1.hud.DesertMapBikeShown, 3f);
            yield return null; yield return null;
            bool dotsOk = true; string dotsWhy = "";
            for (int i = 0; i < n; i++)
            {
                var ob = DesertBikes.Own(seats[i].player);
                if (ob == null || ob.rider != null) { dotsOk = false; dotsWhy += $" posto {i + 1}: nessuna moto parcheggiata;"; continue; }
                Vector3 sp = Pos(seats[i]);
                var want = new Vector2((ob.transform.position.x - sp.x) * HUD.DesertMapPixelsPerMetre, (ob.transform.position.z - sp.z) * HUD.DesertMapPixelsPerMetre);
                var got = seats[i].hud.DesertMapBikeAt;
                if (!seats[i].hud.DesertMapBikeShown || (got - want).magnitude > 0.25f) { dotsOk = false; dotsWhy += $" posto {i + 1}: {got} invece di {want};"; }
            }
            Check("sulla carta di ogni posto c'e' il tondo della sua moto parcheggiata, e non quello di un altro posto", dotsOk, dotsWhy);
            DesertBike.BotReset();
            if (snap)
            {
                PlayerController.holdLook = false;
                s0.player.Teleport(origin0);
                yield return null;
                yield return WaitFor(() => Grounded(s0), 5f);
            }
            yield return null;
        }

        // il luogo lontano dove sta il posto 2 e quanto c'era prima
        Site farSite; Vector3 farSpot; int cellsBefore, cachesBefore;

        IEnumerator FarSeat(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- un posto lontano");
            var s0 = seats[0]; var s1 = seats[1];
            var home = Pos(s0);
            cellsBefore = DesertScatter.I != null ? DesertScatter.I.CellsReady : 0;
            cachesBefore = Dd != null ? Dd.OutdoorCaches : 0;
            farSite = FindFarSite(2200f);
            farSpot = farSite != null ? new Vector3(farSite.fx1 + 22f, 0f, farSite.z) : new Vector3(2500f, 0f, 700f);
            Debug.Log($"[PryingEyes] splitdesertotest: il posto 2 va a ({farSpot.x:F0}, {farSpot.z:F0}), " + (farSite != null ? $"vicino a {DesertDirector.KindName(farSite)} a {farSite.cx},{farSite.cz}" : "sabbia aperta"));
            yield return Land(s1, farSpot.x, farSpot.z, 0f);
            var p1 = Pos(s1);
            Check($"il posto 2 e' lontano dal primo ({Flat(p1, home) / 1000f:F1} km) e sta sulla sabbia", Flat(p1, home) > 1800f && Grounded(s1) && p1.y > DesertWorld.Height(p1.x, p1.z) - 1.5f, $"y {p1.y:F1}, sabbia {DesertWorld.Height(p1.x, p1.z):F1}");
            Check("la sabbia attorno al posto 2 ha il collider (a dieci metri)", DesertTerrain.I.Ready(p1, 14f));
            Check("la sabbia attorno al posto 1 (alla cattedrale) c'e' ancora", DesertTerrain.I.Ready(home, 14f) && Flat(Pos(s0), home) < 3f);
            yield return WaitFor(() => DesertNav.ReadyAround(Pos(s1), 25f), 40f);
            Check("la navmesh attorno al posto 2 c'e'", DesertNav.ReadyAround(Pos(s1), 25f));
            Check("la navmesh attorno al posto 1 c'e' ancora", DesertNav.ReadyAround(home, 20f));
            yield return WaitFor(() => DesertScatter.I != null && DesertScatter.I.CellsReady > cellsBefore, 30f);
            Check($"le cose sulla sabbia nascono attorno al posto 2 (celle {cellsBefore} -> {(DesertScatter.I != null ? DesertScatter.I.CellsReady : 0)})", DesertScatter.I != null && DesertScatter.I.CellsReady > cellsBefore);
            yield return new WaitForSeconds(3f);          // il bottino all'aperto e le pelle dei luoghi
            Check($"il bottino all'aperto nasce anche attorno al posto 2 (cumuli {cachesBefore} -> {Dd.OutdoorCaches})", Dd.OutdoorCaches > cachesBefore);
            if (farSite != null)
            {
                yield return WaitFor(() => { foreach (var rt in Dd.Loaded) if (rt.site == farSite) return true; return false; }, 45f);
                bool built = false; foreach (var rt in Dd.Loaded) if (rt.site == farSite) built = true;
                Check("il luogo vicino al posto 2 si costruisce (e' nel regista)", built, $"{DesertDirector.KindName(farSite)} a {FootDist(farSite, Pos(s1)):F0} m dal posto 2");
                Check("il luogo ha la sua pelle da lontano (le pelle seguono ogni posto)", Dd.FarSkinOf(farSite) != null || built);
            }
            // il cielo del posto 2 e' all'aperto, e i compagni non stanno sulla carta del primo (troppo lontani)
            yield return new WaitForSeconds(1f);
            Check("il posto 2 e' all'aperto (il suo cielo)", DesertSky.I.IndoorFor(s1.camera) < 0.2f, $"{DesertSky.I.IndoorFor(s1.camera):F2}");
            // (gli altri posti oltre il secondo sono rimasti accanto al primo: lui li vede, il secondo no)
            Check("sulla carta del posto 1 il posto 2 non c'e' (e' fuori dalla finestra) e quelli rimasti a casa si", s0.hud.DesertMapMateDots == seats.Length - 2, $"{s0.hud.DesertMapMateDots} punti su {seats.Length - 2} attesi");
        }

        IEnumerator Loot(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- il bottino e' di chi lo prende");
            var s0 = seats[0]; var s1 = seats[1];
            int a0 = ScoreOf(s0), a1 = ScoreOf(s1);
            var at = Pos(s1) + s1.player.transform.forward * 0.4f;
            var pick = DesertPickup.Spawn(null, at, PickKind.Points, 70, 0);
            Check("un oggetto a terra accanto al posto 2 nasce", pick != null);
            yield return new WaitForSeconds(0.8f);
            Check($"i 70 punti sono del posto 2 ({a1} -> {ScoreOf(s1)}) e non del posto 1 ({a0} -> {ScoreOf(s0)})", ScoreOf(s1) == a1 + 70 && ScoreOf(s0) == a0);
        }

        IEnumerator Texts(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- l'obiettivo di ognuno");
            Fd.RefreshObjectiveNow();
            yield return new WaitForSeconds(1.7f);          // la scritta si ricalcola ogni secondo e mezzo
            Fd.RefreshObjectiveNow();
            yield return null;
            string t0 = seats[0].hud.DesertObjectiveShown, t1 = seats[1].hud.DesertObjectiveShown;
            Debug.Log("[PryingEyes] splitdesertotest: obiettivo posto 1: " + t0.Replace("\n", " / "));
            Debug.Log("[PryingEyes] splitdesertotest: obiettivo posto 2: " + t1.Replace("\n", " / "));
            Check("l'obiettivo del posto 2 dice la strada di casa (e' lontano)", t1.Contains("cattedrale a"));
            Check("l'obiettivo del posto 1 non la dice (e' alla cattedrale)", !t0.Contains("cattedrale a"));
            Check("i due obiettivi sono diversi", t0 != t1);
            Check("e' una riga d'orario seguita dal luogo vicino al posto 2", farSite == null || t1.Contains(" m"));
        }

        IEnumerator Skies(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- il cielo di ogni camera");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Debug.Log("[PryingEyes] splitdesertotest: senza grafica niente foto del cielo"); yield break; }
            var sky = DesertSky.I;
            sky.SetHour(12f, true);
            foreach (var s in seats) { s.player.pitch = 0.05f; }
            yield return new WaitForSeconds(1.2f);
            yield return new WaitForEndOfFrame();
            string dir = TestPaths.Out("deserto");
            Directory.CreateDirectory(dir);
            for (int i = 0; i < seats.Length; i++)
            {
                var tex = RenderOnce(seats[i].camera, 960, 540);
                try { File.WriteAllBytes(Path.Combine(dir, $"split_posto{i + 1}.png"), tex.EncodeToPNG()); } catch (Exception ex) { Debug.Log("[PryingEyes] splitdesertotest: foto non scritta: " + ex.Message); }
                var top = TopStrip(tex);
                // a mezzogiorno il cielo in alto e' azzurro (il blu supera il rosso); lo sfondo vuoto a colore pieno e' quello della nebbia, sabbioso (il rosso supera il blu)
                Check($"il posto {i + 1} vede il cielo (in alto {top.r:F2} {top.g:F2} {top.b:F2}: azzurro, non la nebbia)", top.b >= top.r * 0.97f && top.b > 0.12f);
                Destroy(tex);
            }
            yield return MonitorPhoto("deserto-lontano");
            sky.Frozen = false;
        }

        static Vector3 Centre(Room r, float y) => new Vector3((r.x0 + r.x1) * 0.05f, y, (r.z0 + r.z1) * 0.05f);

        static SiteRt RtOf(Site s)
        {
            if (Dd == null || s == null) return null;
            foreach (var rt in Dd.Loaded) if (rt.site == s) return rt;
            return null;
        }

        /// <summary>"In coop a volte al giocatore 2 scompaiono alcune stanze" (segnalazione del 20 novembre): due posti nella stessa torre, uno al piano terra e l'altro piu' su. L'arredo dei livelli
        /// di una torre (SiteRt.UpdateWant) seguiva il solo giocatore piu' vicino: a pari distanza vinceva il primo posto, e i livelli dell'altro si svuotavano (arredi, bottino e guardie).</summary>
        IEnumerator TowerLevels(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- una torre, due posti su piani diversi");
            var s0 = seats[0]; var s1 = seats[1];
            var home0 = Pos(s0);
            bool inv0 = s0.player.invulnerable, inv1 = s1.player.invulnerable;
            var tower = DesertWorld.Landmark();
            if (!Check("c'e' la torre di riferimento, con molti piani", tower != null && tower.IsTower && tower.LevelCount >= 7, tower?.ToString() ?? "manca")) yield break;
            s0.player.invulnerable = s1.player.invulnerable = true;
            // i due posti sulla sabbia davanti alla torre: il luogo si costruisce quando sono vicini (il guscio di tutti i piani)
            float gz = tower.fz0 - 12f;
            yield return Land(s0, tower.CentreX - 2f, gz, 0f);
            yield return Land(s1, tower.CentreX + 2f, gz, 0f);
            SiteRt rt = null;
            yield return WaitFor(() => { rt = RtOf(tower); return rt != null && rt.ShellComplete; }, 90f);
            if (!Check("la torre ha il guscio di tutti i piani", rt != null && rt.ShellComplete, rt == null ? "non caricata" : "")) { s0.player.invulnerable = inv0; s1.player.invulnerable = inv1; yield break; }
            // il posto 1 nella sala dell'ingresso, il posto 2 cinque piani piu' su (non l'ultimo: li' sta il signore della torre)
            int lo = tower.entrance, hi = Mathf.Min(lo + 5, tower.LevelCount - 2);
            s0.player.Teleport(Centre(PlanGen.HallRoom(tower.levels[lo]), tower.LevelY(lo) + 0.1f));
            s1.player.Teleport(Centre(PlanGen.HallRoom(tower.levels[hi]), tower.LevelY(hi) + 0.1f));
            float t0 = Time.time;
            yield return WaitFor(() => rt.UnitDressed(lo) && rt.UnitDressed(hi), 40f);
            yield return new WaitForSeconds(2f);
            bool dressLo = rt.UnitDressed(lo), dressHi = rt.UnitDressed(hi);
            int dressedUnits = 0; for (int u = 0; u < rt.Units; u++) if (rt.UnitDressed(u)) dressedUnits++;
            Debug.Log($"[PryingEyes] splitdesertotest: torre di {tower.LevelCount} piani, posto 1 al piano {lo} (y {Pos(s0).y:F1}), posto 2 al piano {hi} (y {Pos(s1).y:F1}); dopo {Time.time - t0:F0} s arredati {dressedUnits} piani, {rt.DressedRooms} stanze di {rt.DressableRooms}; il piano del posto 1 {(dressLo ? "si" : "NO")}, quello del posto 2 {(dressHi ? "si" : "NO")}");
            Check($"il piano del posto 1 (il {lo}) e' arredato", dressLo);
            Check($"il piano del posto 2 (il {hi}), lontano dal piano del primo, e' arredato anche lui", dressHi, $"arredati {dressedUnits} piani su {rt.Units}");
            Check("e i due posti sono ancora ognuno al suo piano (nessuno e' stato ributtato fuori)", Pos(s0).y < tower.LevelY(lo) + 2f && Pos(s1).y > tower.LevelY(hi) - 1f && Pos(s1).y < tower.LevelY(hi) + 3f, $"{Pos(s0).y:F1} e {Pos(s1).y:F1}");
            // tutti e due fuori: il posto 1 torna a casa in cima alla scalinata (gli altri passi lo contano li'), il posto 2 resta dov'e'
            s0.player.invulnerable = inv0; s1.player.invulnerable = inv1;
            s0.player.Teleport(home0);
            yield return WaitFor(() => Grounded(s0), 15f);
        }

        IEnumerator Edge(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- il bordo del mondo");
            var s0 = seats[0]; var s1 = seats[1];
            var home = Pos(s0);
            float R = DesertWorld.WorldRadius;
            s1.player.Teleport(new Vector3(R + 60f, DesertWorld.Height(R + 60f, 0f) + 8f, 0f));
            yield return WaitFor(() => { var p = Pos(s1); return Mathf.Sqrt(p.x * p.x + p.z * p.z) < R; }, 5f);
            var q = Pos(s1);
            Check($"chi oltrepassa il bordo del mondo e' riportato dentro ({Mathf.Sqrt(q.x * q.x + q.z * q.z):F0} m dalla cattedrale, il limite e' {R:F0})", Mathf.Sqrt(q.x * q.x + q.z * q.z) < R);
            Check("e il posto 1 non si e' mosso", Flat(Pos(s0), home) < 3f);
        }

        IEnumerator Together(Seat[] seats)
        {
            Debug.Log("[PryingEyes] splitdesertotest --- di nuovo insieme");
            var s0 = seats[0]; var s1 = seats[1];
            var home = Pos(s0);
            yield return Land(s1, home.x + 38f, home.z + 6f, Mathf.PI * 0.5f * -1f);
            // tutti gli altri posti accanto, sulla carta di tutti
            for (int i = 2; i < seats.Length; i++) yield return Land(seats[i], home.x + 38f + 4f * (i - 1), home.z + 6f, Mathf.PI * 0.5f * -1f);
            yield return new WaitForSeconds(1.2f);
            int others = seats.Length - 1;
            for (int i = 0; i < seats.Length; i++)
                Check($"sulla carta del posto {i + 1} ci sono gli altri {others} posti (nella finestra)", seats[i].hud.DesertMapMateDots == others, $"{seats[i].hud.DesertMapMateDots} punti");
            var sky = DesertSky.I;
            sky.SetHour(9f, true);
            yield return new WaitForSeconds(1.2f);
            yield return MonitorPhoto("deserto-insieme");
            sky.Frozen = false;
        }
    }
}
#endif
