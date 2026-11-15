using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Debug = UnityEngine.Debug;
#endif

namespace PryingEyes
{
    /// <summary>La parte di SplitScreen che parla con Windows: lancio dei giocatori 2-4, finestre senza bordi nelle loro parti dello schermo,
    /// fuoco, controllo dei processi. Fuori da Windows (editor compreso) restano i soli ganci vuoti.</summary>
    public static partial class SplitScreen
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // ------------------------------------------------------------------ user32 / kernel32
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowTextW(IntPtr h, string text);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000,
            WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
        const uint WS_EX_DLGMODALFRAME = 0x1, WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000;
        const uint SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
        const uint MONITOR_DEFAULTTONEAREST = 2;
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        static readonly List<Process> children = new List<Process>();
        static readonly HashSet<int> seenChild = new HashSet<int>();
        static IntPtr ownWnd;
        static float tBorn = -1f, tWindow, tWatch, offSince = -1f;
        static int retries;
        static bool everStarted;
        static GameObject manager;
        static int OwnPid => Process.GetCurrentProcess().Id;

        // ------------------------------------------------------------------ finestre

        /// <summary>La finestra di gioco (classe UnityWndClass) di un processo, o IntPtr.Zero se non ne ha (senza grafica non c'e').</summary>
        static IntPtr WindowOf(int pid)
        {
            IntPtr found = IntPtr.Zero;
            var name = new StringBuilder(64);
            EnumProc cb = (h, l) =>
            {
                GetWindowThreadProcessId(h, out uint p);
                if (p != (uint)pid || !IsWindowVisible(h)) return true;
                name.Length = 0; GetClassNameW(h, name, 64);
                if (name.ToString() != "UnityWndClass") return true;
                found = h;
                return false;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return found;
        }

        static RectInt ToRectInt(RECT r) => new RectInt(r.left, r.top, r.right - r.left, r.bottom - r.top);

        /// <summary>L'area utile (senza la barra delle applicazioni) del monitor su cui sta la finestra del gioco.</summary>
        static RectInt WorkArea()
        {
            IntPtr h = WindowOf(OwnPid);
            if (h != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (GetMonitorInfoW(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST), ref info)) return ToRectInt(info.rcWork);
            }
            return new RectInt(0, 0, Mathf.Max(1280, Screen.currentResolution.width), Mathf.Max(720, Screen.currentResolution.height));
        }

        /// <summary>La finestra e' gia' nella sua parte, senza titolo ne' bordo.</summary>
        static bool WindowIsPlaced(IntPtr h, RectInt r)
        {
            if (!GetWindowRect(h, out RECT cur)) return false;
            var c = ToRectInt(cur);
            uint style = unchecked((uint)GetWindowLongPtr(h, GWL_STYLE).ToInt64());
            return Mathf.Abs(c.x - r.x) <= 1 && Mathf.Abs(c.y - r.y) <= 1 && Mathf.Abs(c.width - r.width) <= 1 && Mathf.Abs(c.height - r.height) <= 1
                && (style & (WS_CAPTION | WS_THICKFRAME)) == 0;
        }

        /// <summary>Toglie titolo e bordo, mette il nome del giocatore e porta la finestra nella sua parte. Senza bordi la finestra e la sua
        /// area di disegno coincidono, quindi lo schermo diviso non ha righe in mezzo.</summary>
        static void PlaceBorderless(IntPtr h, RectInt r, string title)
        {
            uint style = unchecked((uint)GetWindowLongPtr(h, GWL_STYLE).ToInt64());
            style = (style & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)) | WS_POPUP | WS_VISIBLE;
            SetWindowLongPtr(h, GWL_STYLE, new IntPtr(unchecked((int)style)));
            uint ex = unchecked((uint)GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64());
            ex &= ~(WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
            SetWindowPos(h, HWND_NOTOPMOST, r.x, r.y, r.width, r.height, SWP_FRAMECHANGED | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            if (!string.IsNullOrEmpty(title)) SetWindowTextW(h, title);
        }

        static int ForegroundPid()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(fg, out uint pid);
            return (int)pid;
        }

        /// <summary>Porta una finestra in primo piano anche se il fuoco ce l'ha un altro processo (si collega un momento al suo ciclo di
        /// messaggi: e' il modo ammesso da Windows per restituire il fuoco).</summary>
        static void Activate(IntPtr h)
        {
            IntPtr fg = GetForegroundWindow();
            uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
            uint me = GetCurrentThreadId();
            bool attach = fgThread != 0 && fgThread != me;
            if (attach) AttachThreadInput(me, fgThread, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (attach) AttachThreadInput(me, fgThread, false);
        }

        static bool ProcessAlive(int pid)
        {
            try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ lancio

        /// <summary>Una porta UDP libera su loopback (scelta a caso: due partite split screen sullo stesso PC non si pestano).</summary>
        static ushort FreePort()
        {
            int forced = IntArg("-splitport", 0);
            if (forced > 0 && forced < 65536) return (ushort)forced;
            var rnd = new System.Random();
            for (int i = 0; i < 40; i++)
            {
                int p = 20000 + rnd.Next(20000);
                try { using (new UdpClient(new IPEndPoint(IPAddress.Loopback, p))) { } return (ushort)p; }
                catch (SocketException) { }
            }
            return (ushort)(CoopProtocol.DefaultPort + 1);
        }

        /// <summary>Lancia i giocatori 2..count: lo stesso gioco, una finestra senza bordi nella sua parte, che entra da sola nella partita
        /// dell'ospite. -splitextra "..." (solo per le prove) aggiunge argomenti a tutti, -splitlogdir cambia dove scrivono il log.</summary>
        static void LaunchChildren(int count, RectInt area, ushort port, int[] pads)
        {
            string exe;
            try { exe = Process.GetCurrentProcess().MainModule.FileName; }
            catch (Exception e) { Debug.LogWarning("[PryingEyes] split: non trovo il file del gioco: " + e.Message); return; }
            string dir = Path.GetDirectoryName(exe);
            string logDir = Arg("-splitlogdir") ?? Path.Combine(Application.persistentDataPath, "split");
            try { Directory.CreateDirectory(logDir); } catch (Exception) { logDir = null; }
            string extra = Arg("-splitextra");
            int me = OwnPid;
            for (int k = 1; k < count; k++)
            {
                var t = TileOf(k, count, area);
                string a = ChildArgs(k, count, t, port, me, pads[k], logDir != null ? Path.Combine(logDir, "giocatore" + (k + 1) + ".log") : null, extra);
                try
                {
                    var p = Process.Start(new ProcessStartInfo { FileName = exe, Arguments = a, WorkingDirectory = dir, UseShellExecute = false });
                    children.Add(p);
                    Debug.Log($"[PryingEyes] split: giocatore {k + 1} avviato (pid {p.Id}): {a}");
                }
                catch (Exception e) { Debug.LogWarning($"[PryingEyes] split: il giocatore {k + 1} non parte: {e.Message}"); }
            }
        }

        // ------------------------------------------------------------------ gestore

        static void EnsureManager()
        {
            if (manager != null) return;
            manager = new GameObject("SplitScreen");
            UnityEngine.Object.DontDestroyOnLoad(manager);
            manager.AddComponent<SplitManager>();
        }

        /// <summary>Ogni fotogramma: tiene la finestra nella sua parte (Unity puo' rimetterla a posto da solo nei primi secondi) e controlla chi
        /// deve controllare: l'ospite le finestre dei figli, le figlie il giocatore 1 e il collegamento.</summary>
        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (tBorn < 0f) tBorn = now;
            if (now >= tWindow)
            {
                tWindow = now + (now - tBorn < 60f ? 0.25f : 1f);
                EnforceWindow();
            }
            if (now < tWatch) return;
            tWatch = now + 1f;
            LogSelfNow();
            if (IsChild) WatchParent(now); else WatchChildren();
        }

        /// <summary>Dopo gli altri script: una finestra guidata da un controller non blocca mai il cursore (il mouse serve al giocatore 1).</summary>
        internal static void LateTick()
        {
            if (!PadOnly) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = GameMenu.Blocked;
        }

        static void EnforceWindow()
        {
            if (TileRect.width <= 0 || TileRect.height <= 0) return;
            if (ownWnd == IntPtr.Zero || !IsWindow(ownWnd)) ownWnd = WindowOf(OwnPid);
            if (ownWnd == IntPtr.Zero) return;                  // senza grafica non c'e' finestra
            if (!WindowIsPlaced(ownWnd, TileRect)) PlaceBorderless(ownWnd, TileRect, "Prying Eyes - Giocatore " + (Tile + 1));
        }

        static float tLog;
        static bool? logCompanions;
        static bool LogCompanions => logCompanions ?? (logCompanions = Has("-splitlog")).Value;

        /// <summary>Solo per la prova a piu' processi (-splitlog): ogni 4 secondi dove l'ospite vede i compagni.</summary>
        static void LogCompanionsNow()
        {
            if (!LogCompanions || Coop.Game == null || Time.unscaledTime < tLog) return;
            tLog = Time.unscaledTime + 4f;
            foreach (var kv in Coop.Game.Players)
            {
                var p = kv.Value;
                if (p == null) continue;
                Debug.Log(FormattableString.Invariant($"[PryingEyes] split: compagno {p.playerName}[{kv.Key}] pos {p.last.pos.x:F1} {p.last.pos.y:F1} {p.last.pos.z:F1} yaw {p.last.yaw * Mathf.Rad2Deg:F0}"));
            }
        }

        static float tSelf;

        /// <summary>Solo per la prova a piu' processi (-splitlog): ogni 4 secondi cosa fa questa finestra (posizione, direzione, controller, menu).</summary>
        static void LogSelfNow()
        {
            if (!LogCompanions || Time.unscaledTime < tSelf) return;
            tSelf = Time.unscaledTime + 4f;
            var pl = GameManager.I != null ? GameManager.I.player : null;
            Vector3 p = pl != null ? pl.transform.position : Vector3.zero;
            Debug.Log(FormattableString.Invariant(
                $"[PryingEyes] split: io pos {p.x:F1} {p.y:F1} {p.z:F1} yaw {(pl != null ? Mathf.Repeat(pl.yaw * Mathf.Rad2Deg, 360f) : 0f):F0} controller {(PadControls.Connected ? 1 : 0)} stick {PadControls.Move.x:F2} {PadControls.Move.y:F2} sguardo {PadControls.LookRate.x:F2} menu {(GameMenu.Blocked ? "bloccato" : "libero")} coop {(Coop.Active ? 1 : 0)}"));
        }

        static void WatchChildren()
        {
            LogCompanionsNow();
            bool fresh = false;
            for (int i = 0; i < children.Count; i++)
            {
                var p = children[i];
                if (p == null || p.HasExited || seenChild.Contains(p.Id) || WindowOf(p.Id) == IntPtr.Zero) continue;
                seenChild.Add(p.Id); fresh = true;
                Debug.Log($"[PryingEyes] split: la finestra del giocatore {i + 2} e' pronta");
            }
            if (!fresh || ownWnd == IntPtr.Zero) return;
            // una finestra appena nata si prende il fuoco: tastiera e mouse tornano al giocatore 1
            int fp = ForegroundPid();
            foreach (var p in children) if (p != null && p.Id == fp) { Activate(ownWnd); break; }
        }

        static void WatchParent(float now)
        {
            if (parentPid > 0 && !ProcessAlive(parentPid)) { Quit("il giocatore 1 non c'e' piu'"); return; }
            var game = Coop.Game;
            if (game != null && game.Started) everStarted = true;
            if (Coop.Active) { offSince = -1f; return; }
            if (offSince < 0f) offSince = now;
            if (now - tBorn >= 120f && !everStarted) { Quit("il giocatore 1 non risponde"); return; }
            if (now - offSince <= 3f) return;
            if (everStarted) { Quit("il collegamento con il giocatore 1 si e' chiuso"); return; }
            if (retries >= 25) return;
            retries++; offSince = now;
            Debug.Log("[PryingEyes] split: riprovo a entrare nella partita (" + retries + ")");
            Coop.StartJoin("127.0.0.1", (ushort)IntArg("-coopport", CoopProtocol.DefaultPort));
        }

        /// <summary>Alla chiusura del giocatore 1: chiede alle altre finestre di chiudersi (se non lo fanno, lo faranno da sole accorgendosi che
        /// il giocatore 1 non c'e' piu').</summary>
        internal static void Shutdown()
        {
            foreach (var p in children)
            {
                try { if (p != null && !p.HasExited) p.CloseMainWindow(); }
                catch (Exception) { }
            }
        }

        /// <summary>Quante finestre figlie sono ancora aperte (per le prove).</summary>
        public static int ChildrenAlive
        {
            get
            {
                int n = 0;
                foreach (var p in children) { try { if (p != null && !p.HasExited) n++; } catch (Exception) { } }
                return n;
            }
        }
#else
        static void EnsureManager() { }
        static RectInt WorkArea() => new RectInt(0, 0, 1920, 1080);
        static ushort FreePort() => (ushort)(CoopProtocol.DefaultPort + 1);
        static void LaunchChildren(int count, RectInt area, ushort port, int[] pads) { }
        internal static void Tick() { }
        internal static void LateTick() { }
        internal static void Shutdown() { }
        public static int ChildrenAlive => 0;
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    /// <summary>L'oggetto che fa battere SplitScreen (creato una volta, non muore col cambio di scena).</summary>
    sealed class SplitManager : MonoBehaviour
    {
        void Update() => SplitScreen.Tick();
        void LateUpdate() => SplitScreen.LateTick();
        void OnApplicationQuit() => SplitScreen.Shutdown();
    }
#endif
}
