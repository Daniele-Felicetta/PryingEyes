using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PryingEyes
{
    /// <summary>Le finestre del gioco su Windows, per lo schermo condiviso su piu' monitor. Quando Unity accende un monitor (Display.Activate) apre
    /// una seconda finestra senza bordi: qui si trova la finestra del gioco (per rimetterla davanti se Windows ha dato il fuoco alla nuova, altrimenti
    /// tastiera e mouse non arriverebbero al giocatore 1 e il gioco andrebbe in pausa) e si nascondono le finestre dei monitor accesi quando lo
    /// schermo condiviso finisce (Unity non le chiude). Fuori dal gioco per Windows (editor, altri sistemi) non fa niente.</summary>
    static class WinWindows
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out Rc rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Pt point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)] struct Rc { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] struct Pt { public int x, y; }

        const int SwHide = 0, SwShowNoActivate = 4;
        const uint GaRoot = 2, SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, WsPopup = 0x80000000;
        const uint WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000, SsNotify = 0x00000100;
        static readonly IntPtr HwndTop = IntPtr.Zero;

        struct Win { public IntPtr handle; public int x, y, w, h; public bool visible; public string cls, title; }

        static IntPtr main;
        static readonly List<IntPtr> hidden = new List<IntPtr>();
        static uint ownPid;

        static uint Pid => ownPid != 0 ? ownPid : (ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id);
        static bool IsOurs(IntPtr handle) { GetWindowThreadProcessId(handle, out uint p); return p == Pid; }

        static List<Win> Own(bool onlyVisible)
        {
            var list = new List<Win>();
            EnumWindows((handle, lp) =>
            {
                if (!IsOurs(handle)) return true;
                bool vis = IsWindowVisible(handle);
                if (onlyVisible && !vis) return true;
                GetWindowRect(handle, out Rc r);
                var cls = new StringBuilder(128); GetClassName(handle, cls, 128);
                var title = new StringBuilder(128); GetWindowText(handle, title, 128);
                list.Add(new Win { handle = handle, x = r.left, y = r.top, w = r.right - r.left, h = r.bottom - r.top, visible = vis, cls = cls.ToString(), title = title.ToString() });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static string ClassOf(IntPtr handle) { var s = new StringBuilder(128); GetClassName(handle, s, 128); return s.ToString(); }

        /// <summary>Si ricorda la finestra del gioco: da chiamare prima di accendere i monitor (poi le finestre visibili sono piu' d'una).</summary>
        public static void RememberMain()
        {
            if (main != IntPtr.Zero && IsOurs(main)) return;
            var fg = GetForegroundWindow();
            main = fg != IntPtr.Zero && IsOurs(fg) ? fg : System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        }

        /// <summary>Rimostra le finestre dei monitor nascoste alla fine di una partita precedente (senza prendere il fuoco).</summary>
        public static void ShowSecondary()
        {
            foreach (var handle in hidden) ShowWindow(handle, SwShowNoActivate);
            hidden.Clear();
        }

        /// <summary>Nasconde le finestre dei monitor accesi (quelle della stessa classe della finestra del gioco, diverse da lei e grandi).</summary>
        public static void HideSecondary()
        {
            if (main == IntPtr.Zero) return;
            string cls = ClassOf(main);
            foreach (var w in Own(true))
            {
                if (w.handle == main || w.cls != cls || w.w < 200 || w.h < 200) continue;
                ShowWindow(w.handle, SwHide);
                hidden.Add(w.handle);
            }
        }

        static bool IsBig(Win w) => w.w >= 200 && w.h >= 200;

        /// <summary>Vero se la finestra e' quella che si vede al suo centro: nessun altro programma la copre (un programma a tutto schermo sul
        /// secondo monitor, per esempio).</summary>
        static bool OnTop(Win w)
        {
            var h = WindowFromPoint(new Pt { x = w.x + w.w / 2, y = w.y + w.h / 2 });
            return h != IntPtr.Zero && GetAncestor(h, GaRoot) == w.handle;
        }

        /// <summary>Porta in primo piano, senza prendere il fuoco, le finestre dei monitor accesi che un altro programma copre: Windows puo' aprire
        /// una finestra nuova dietro a quella a cui l'utente stava lavorando. Restituisce quante ne ha mosse.</summary>
        public static int RaiseSecondary()
        {
            if (main == IntPtr.Zero) return 0;
            string cls = ClassOf(main);
            int n = 0;
            foreach (var w in Own(true))
            {
                if (w.handle == main || w.cls != cls || !IsBig(w) || OnTop(w)) continue;
                if (SetWindowPos(w.handle, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate)) n++;
            }
            return n;
        }

        /// <summary>Solo per le prove: una finestra senza bordi, di questo processo, messa sopra la prima finestra di un monitor acceso, come un programma
        /// a tutto schermo che la copre. Zero se non c'e' nessuna finestra da coprire. Non prende il fuoco (WS_EX_NOACTIVATE, mostrata senza attivarla:
        /// altrimenti il gioco la prenderebbe per un cambio di programma e andrebbe in pausa) e risponde al puntatore (SS_NOTIFY: una finestra "Static"
        /// senza e' trasparente per WindowFromPoint e non coprirebbe niente).</summary>
        public static IntPtr TestCover()
        {
            if (main == IntPtr.Zero) return IntPtr.Zero;
            string cls = ClassOf(main);
            foreach (var w in Own(true))
            {
                if (w.handle == main || w.cls != cls || !IsBig(w)) continue;
                var cover = CreateWindowEx(WsExToolWindow | WsExNoActivate, "Static", "", WsPopup | SsNotify, w.x, w.y, w.w, w.h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (cover != IntPtr.Zero)
                {
                    ShowWindow(cover, SwShowNoActivate);
                    SetWindowPos(cover, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                }
                return cover;
            }
            return IntPtr.Zero;
        }

        /// <summary>Solo per le prove: toglie la finestra messa da TestCover.</summary>
        public static void TestUncover(IntPtr cover)
        {
            if (cover != IntPtr.Zero) DestroyWindow(cover);
        }

        /// <summary>Se il fuoco e' a un'altra finestra del gioco (quella del monitor appena acceso), lo rimette sulla principale.</summary>
        public static bool FocusMainIfStolen()
        {
            if (main == IntPtr.Zero) return false;
            var fg = GetForegroundWindow();
            if (fg == main || fg == IntPtr.Zero || !IsOurs(fg)) return false;
            return SetForegroundWindow(main);
        }

        public static string Report()
        {
            var sb = new StringBuilder();
            foreach (var w in Own(false))
            {
                string state = !w.visible ? "nascosta  " : !IsBig(w) ? "visibile  " : OnTop(w) ? "in primo piano  " : "COPERTA  ";
                sb.Append(w.handle == main ? "[principale] " : "             ").Append(state)
                  .Append(w.cls).Append(" '").Append(w.title).Append("' ").Append(w.x).Append(',').Append(w.y).Append(' ').Append(w.w).Append('x').Append(w.h).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Una finestra grande del gioco vero e proprio: della classe della finestra principale (non conta quella messa da TestCover).</summary>
        static bool IsGameWindow(Win w) => IsBig(w) && (main == IntPtr.Zero || w.cls == ClassOf(main));

        public static int VisibleCount()
        {
            int n = 0;
            foreach (var w in Own(true)) if (IsGameWindow(w)) n++;
            return n;
        }

        /// <summary>Quante finestre grandi del gioco si vedono per intero al loro centro (nessun altro programma le copre).</summary>
        public static int OnTopCount()
        {
            int n = 0;
            foreach (var w in Own(true)) if (IsGameWindow(w) && OnTop(w)) n++;
            return n;
        }
#else
        public static void RememberMain() { }
        public static void ShowSecondary() { }
        public static void HideSecondary() { }
        public static int RaiseSecondary() => 0;
        public static IntPtr TestCover() => IntPtr.Zero;
        public static void TestUncover(IntPtr cover) { }
        public static bool FocusMainIfStolen() => false;
        public static string Report() => "";
        public static int VisibleCount() => 0;
        public static int OnTopCount() => 0;
#endif
    }
}
