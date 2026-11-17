using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Lo schermo condiviso su piu' monitor: quanti ne vede il gioco, quanti se ne usano, la loro misura e l'accensione dei monitor oltre
    /// il primo. Le regole su chi sta dove (PlayersOn, DisplayOf, ViewOn) sono pure e stanno in SplitScreen.cs.
    ///
    /// Come funziona: il monitor 0 e' la finestra del gioco; gli altri sono "display" di Unity che si accendono con Display.Activate (Unity apre
    /// una finestra senza bordi sul monitor, alla sua misura). Ogni camera dei giocatori ha Camera.targetDisplay del suo monitor e ogni monitor ha il suo
    /// canvas con gli HUD dei giocatori che ci stanno sopra (Seats.Rig.cs). Cio' che Unity disegna solo sul monitor 0 (IMGUI: la mappa, il registro
    /// della Fabbrica) resta li'.</summary>
    public static partial class SplitScreen
    {
        const string MonitorsKey = "split_monitors";

        /// <summary>Solo per le prove (e per la revisione del menu in editor, che non ha monitor): fa credere collegati almeno N monitor.</summary>
        public static int TestConnected;
        /// <summary>Solo per la revisione del menu: la scelta dei monitor (0: nessuna, vale la regola di sempre).</summary>
        public static int TestWanted;

        /// <summary>-splitmonitors N: usa N monitor (anche se il PC ne ha meno: in quel caso sono finti, per le prove senza finestre; non cambia
        /// la scelta salvata). Senza il flag, le prove automatiche usano un monitor solo e il gioco la scelta fatta nel menu.</summary>
        public static int MonitorsArg => IntArg("-splitmonitors", 0);

        /// <summary>Quanti monitor vede Unity (Display.displays), senza i finti.</summary>
        public static int RealMonitors => Display.displays != null ? Mathf.Max(1, Display.displays.Length) : 1;

        /// <summary>Quanti monitor si possono usare (al massimo MaxMonitors).</summary>
        public static int ConnectedMonitors => Mathf.Clamp(Mathf.Max(RealMonitors, Mathf.Max(MonitorsArg, TestConnected)), 1, MaxMonitors);

        /// <summary>Quanti monitor vuole il giocatore per la prossima partita: la scelta del menu (la prima volta, tutti quelli collegati), mai piu'
        /// di quelli collegati. Le prove automatiche ne usano uno solo, salvo -splitmonitors: non dipendono da cio' che e' salvato su questo PC.</summary>
        public static int WantedMonitors
        {
            get
            {
                int connected = ConnectedMonitors, forced = MonitorsArg;
                if (TestWanted > 0) return Mathf.Clamp(TestWanted, 1, connected);
                if (forced > 0) return Mathf.Clamp(forced, 1, connected);
                if (VideoSettings.Automated) return 1;
                int saved = PlayerPrefs.GetInt(MonitorsKey, 0);
                return saved > 0 ? Mathf.Clamp(saved, 1, connected) : connected;
            }
        }

        /// <summary>La scelta del menu: da quanti monitor deve essere formato lo schermo condiviso (si ricorda fra una partita e l'altra).</summary>
        public static void ChooseMonitors(int n)
        {
            PlayerPrefs.SetInt(MonitorsKey, Mathf.Clamp(n, 1, MaxMonitors));
            PlayerPrefs.Save();
        }

        /// <summary>La misura in pixel del monitor 'display' (quella a cui Unity disegna): il principale e' lo schermo del gioco. Un monitor finto (prove)
        /// ha la misura del principale.</summary>
        public static Vector2Int DisplaySize(int display)
        {
            var all = Display.displays;
            if (display > 0 && all != null && display < all.Length)
            {
                int w = all[display].renderingWidth, h = all[display].renderingHeight;
                if (w <= 0 || h <= 0) { w = all[display].systemWidth; h = all[display].systemHeight; }
                return new Vector2Int(w, h);
            }
            return new Vector2Int(Screen.width, Screen.height);
        }

        /// <summary>Una riga per il registro: la misura di ogni monitor usato.</summary>
        public static string DescribeDisplays(int monitors)
        {
            var sb = new System.Text.StringBuilder();
            for (int d = 0; d < monitors; d++)
            {
                var s = DisplaySize(d);
                if (d > 0) sb.Append(" + ");
                sb.Append(s.x).Append('x').Append(s.y);
                if (d > 0 && d >= RealMonitors) sb.Append(" (finto)");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ accensione dei monitor

        static readonly HashSet<int> activated = new HashSet<int>();
        static float graceUntil, focusCheckUntil, nextFocusCheck;
        static Vector2Int mainBefore;
        static bool mainChangeLogged;

        /// <summary>Per un paio di secondi dall'accensione di un monitor la finestra del gioco puo' perdere il fuoco (Windows da' il fuoco alla
        /// finestra nuova): non e' una pausa voluta dal giocatore.</summary>
        public static bool JustActivated => Time.realtimeSinceStartup < graceUntil;

        /// <summary>Accende i monitor 1..monitors-1 (il monitor 0 e' la finestra del gioco, sempre acceso) e ci rimette la finestra davanti se
        /// serve. Unity non sa spegnerli: alla fine della partita le finestre si nascondono (Release).</summary>
        public static void ActivateDisplays(int monitors)
        {
            if (monitors <= 1) return;
            var all = Display.displays;
            WinWindows.RememberMain();
            mainBefore = new Vector2Int(Screen.width, Screen.height);
            mainChangeLogged = false;
            bool any = false;
            for (int d = 1; d < monitors; d++)
            {
                if (all == null || d >= all.Length) continue;          // monitor finto (prove) o scollegato
                any = true;
                if (!activated.Add(d)) continue;
                try { all[d].Activate(); Debug.Log($"[PryingEyes] split: monitor {d + 1} acceso ({all[d].systemWidth}x{all[d].systemHeight})"); }
                catch (Exception e) { activated.Remove(d); Debug.LogWarning("[PryingEyes] split: il monitor " + (d + 1) + " non si accende: " + e.Message); }
            }
            if (!any) return;
            WinWindows.ShowSecondary();
            graceUntil = Time.realtimeSinceStartup + 3f;
            focusCheckUntil = Time.realtimeSinceStartup + 3f;
            nextFocusCheck = 0f;
        }

        /// <summary>Ogni fotogramma dello schermo condiviso: nei primi secondi dopo l'accensione rimette davanti la finestra del gioco se Windows
        /// ha dato il fuoco a quella nuova (altrimenti tastiera e mouse non arriverebbero al giocatore 1) e porta in primo piano le finestre dei
        /// monitor accesi se un altro programma le copre (senza prendere il fuoco).</summary>
        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now > focusCheckUntil || now < nextFocusCheck) return;
            nextFocusCheck = now + 0.25f;
            if (WinWindows.FocusMainIfStolen()) Debug.Log("[PryingEyes] split: il fuoco e' tornato alla finestra del gioco");
            int raised = WinWindows.RaiseSecondary();
            if (raised > 0) Debug.Log("[PryingEyes] split: " + raised + " finestra dei monitor portata in primo piano (un altro programma la copriva)");
            if (!mainChangeLogged && (Screen.width != mainBefore.x || Screen.height != mainBefore.y))
            {
                mainChangeLogged = true;       // Unity porta la finestra del gioco alla misura del suo monitor quando ne accende un altro: lo si scrive nel registro
                Debug.Log("[PryingEyes] split: la finestra del gioco e' passata da " + mainBefore.x + "x" + mainBefore.y + " a " + Screen.width + "x" + Screen.height + " (" + Screen.fullScreenMode + ")");
            }
        }

        /// <summary>Alla fine dello schermo condiviso: le finestre dei monitor accesi si nascondono (Unity non le chiude: resterebbero nere sul
        /// secondo monitor fino alla chiusura del gioco).</summary>
        public static void Release()
        {
            WinWindows.HideSecondary();
        }

        /// <summary>Per le prove: le finestre del gioco, una per riga (monitor accesi compresi).</summary>
        public static string WindowsReport() => WinWindows.Report();

        /// <summary>Per le prove: quante finestre visibili ha il gioco (senza finestra o fuori da Windows: 0).</summary>
        public static int VisibleWindows() => WinWindows.VisibleCount();

        /// <summary>Per le prove: quante di quelle finestre si vedono per intero al centro, cioe' non sono coperte da un altro programma.</summary>
        public static int WindowsOnTop() => WinWindows.OnTopCount();
    }
}
