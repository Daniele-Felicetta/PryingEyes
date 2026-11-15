using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Split screen sullo stesso PC, da due a quattro giocatori.
    ///
    /// Perche' cosi': il gioco tiene lo stato di UN giocatore per processo (punti, armi, perk, HUD, torcia, musica: statici, 247 usi di
    /// GameManager.I), e la cooperativa e' gia' costruita e provata su "un'istanza per giocatore". Lo split screen non cambia niente di
    /// tutto questo: il giocatore 1 e' l'ospite di una cooperativa solo locale (UDP su loopback, porta libera scelta a caso) e gli altri
    /// sono istanze dello stesso gioco che l'ospite lancia da se', con una finestra senza bordi ciascuna, disposte sullo schermo come le
    /// parti di uno schermo diviso (TileOf: 2 giocatori affiancati, 3 e 4 in due righe). Da fuori e' uno schermo diviso; sotto sono N giochi
    /// collegati in rete locale, quindi nemici, piani, punti e premi funzionano come in cooperativa.
    ///
    /// Comandi: la tastiera e il mouse arrivano solo alla finestra attiva (di default quella del giocatore 1; un clic o Alt+Tab li passa a
    /// un'altra), i controller si leggono da XInput (PadControls) e valgono anche per una finestra non attiva: il giocatore 2 usa il
    /// controller 1, il 3 il controller 2 e cosi' via (o tutti col controller, il giocatore 1 compreso).
    /// Le finestre dei giocatori 2-4 escono da sole quando esce il giocatore 1. Documentazione: docs/multiplayer/split-screen.md.</summary>
    public static partial class SplitScreen
    {
        public const int MinPlayers = 2, MaxPlayers = 4;
        /// <summary>Oltre questo campo visivo orizzontale (gradi) la visuale si deforma troppo: nelle finestre larghe si riduce quello verticale.</summary>
        public const float MaxHorizontalFov = 125f;

        // ---- argomenti (letti una volta, al primo uso). NON un inizializzatore di campo: i campi statici di una classe parziale si
        // inizializzano file per file in un ordine non definito, e un altro file (SplitScreen.Win.cs) li usa nei suoi inizializzatori.
        static string[] argv;
        static string[] args => argv ?? (argv = Environment.GetCommandLineArgs());
        static bool Has(string a) => Array.IndexOf(args, a) >= 0;
        static string Arg(string name)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
        static int IntArg(string name, int def) => int.TryParse(Arg(name), out int v) ? v : def;

        /// <summary>Sono una finestra figlia (giocatori 2-4): il giocatore 1 mi lancia con -splitchild N (N = 1..3).</summary>
        public static readonly int ChildTile = IntArg("-splitchild", -1);
        public static bool IsChild => ChildTile > 0;
        /// <summary>-splitscreen N: l'ospite parte da solo in split screen con N giocatori (come se avesse premuto il bottone del menu).</summary>
        public static readonly int AutoStartCount = IntArg("-splitscreen", 0);
        /// <summary>-splitpad1: con -splitscreen anche il giocatore 1 usa un controller.</summary>
        public static readonly bool AutoP1Pad = Has("-splitpad1");

        /// <summary>Questa istanza e' una finestra di una partita in split screen (l'ospite dopo Begin, o una figlia).</summary>
        public static bool Active { get; private set; }
        /// <summary>Quale parte dello schermo: 0 il giocatore 1 (ospite), 1..3 le figlie.</summary>
        public static int Tile { get; private set; }
        public static int Count { get; private set; }
        /// <summary>Lo schermo diviso (area utile del monitor) e la parte di questa finestra, in pixel del desktop.</summary>
        public static RectInt Area { get; private set; }
        public static RectInt TileRect { get; private set; }
        /// <summary>Questa finestra non riceve tastiera e mouse "per natura" (e' guidata da un controller): il cursore non va mai bloccato.</summary>
        public static bool PadOnly { get; private set; }

        /// <summary>Nel Windows vero (non nell'editor) si puo' fare; altrove il bottone del menu resta spento.</summary>
        public static bool Supported
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Si puo' avviare adesso: sistema giusto, cooperativa disponibile e nessuna partita in rete in corso.</summary>
        public static bool Available => Supported && Coop.Available && !Coop.Active && !Active;

        // ------------------------------------------------------------------ disposizione (pura: la prova la controlla senza finestre)

        /// <summary>La parte di schermo del giocatore 'tile' (0..count-1) dentro l'area. Origine in alto a sinistra, y verso il basso (come le
        /// finestre di Windows). Due giocatori: uno a sinistra e uno a destra (con l'angolo verticale che usa il gioco ogni finestra e' piu'
        /// stretta ma non schiacciata); tre: due sopra e il terzo sotto a tutta larghezza; quattro: quattro quadranti. Le parti coprono
        /// l'area esattamente, senza buchi e senza sovrapposizioni.</summary>
        public static RectInt TileOf(int tile, int count, RectInt a)
        {
            if (count <= 1) return a;
            int hw = a.width / 2, hh = a.height / 2;
            if (count == 2)
                return tile <= 0 ? new RectInt(a.x, a.y, hw, a.height) : new RectInt(a.x + hw, a.y, a.width - hw, a.height);
            if (tile <= 0) return new RectInt(a.x, a.y, hw, hh);
            if (tile == 1) return new RectInt(a.x + hw, a.y, a.width - hw, hh);
            int y2 = a.y + hh, h2 = a.height - hh;
            if (count == 3) return new RectInt(a.x, y2, a.width, h2);
            return tile == 2 ? new RectInt(a.x, y2, hw, h2) : new RectInt(a.x + hw, y2, a.width - hw, h2);
        }

        /// <summary>Quale controller (slot XInput) dare a ciascun giocatore, -1 = nessuno (tastiera e mouse). 'mask' dice quali slot hanno un
        /// controller collegato. I giocatori che ne hanno bisogno prendono i collegati in ordine; se non bastano, i primi slot ancora liberi
        /// (cosi' un controller collegato dopo funziona). Il giocatore 1 ne prende uno solo se p1Pad.</summary>
        public static int[] AssignPads(int mask, int count, bool p1Pad)
        {
            var result = new int[Mathf.Max(1, count)];
            var used = new bool[4];
            var order = new List<int>();
            for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) order.Add(i);
            for (int i = 0; i < 4; i++) if ((mask & (1 << i)) == 0) order.Add(i);       // poi i liberi: un controller collegato dopo
            int next = 0;
            for (int p = 0; p < result.Length; p++)
            {
                if (p == 0 && !p1Pad) { result[p] = -1; continue; }
                while (next < order.Count && used[order[next]]) next++;
                result[p] = next < order.Count ? order[next] : -1;
                if (result[p] >= 0) used[result[p]] = true;
            }
            return result;
        }

        /// <summary>L'angolo di campo verticale da usare in una finestra larga 'aspect' volte la sua altezza: quello scelto dal giocatore, tranne
        /// quando darebbe piu' di MaxHorizontalFov gradi in orizzontale (le finestre molto larghe dello split screen), e allora si accorcia.</summary>
        public static float FovFor(float verticalFov, float aspect, float maxHorizontal = MaxHorizontalFov)
        {
            if (aspect <= 0.01f) return verticalFov;
            float h = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            if (h <= maxHorizontal) return verticalFov;
            return 2f * Mathf.Atan(Mathf.Tan(maxHorizontal * 0.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
        }

        /// <summary>Gli argomenti con cui l'ospite lancia la finestra di un giocatore (puro, per poterli controllare).</summary>
        public static string ChildArgs(int tile, int count, RectInt tileRect, ushort port, int parentPid, int pad, string logFile, string extra)
        {
            var sb = new StringBuilder();
            sb.Append("-popupwindow -screen-fullscreen 0 -screen-width ").Append(tileRect.width).Append(" -screen-height ").Append(tileRect.height);
            sb.Append(" -splitchild ").Append(tile).Append(" -splitcount ").Append(count);
            sb.Append(" -splittile ").Append(tileRect.x).Append(',').Append(tileRect.y).Append(',').Append(tileRect.width).Append(',').Append(tileRect.height);
            sb.Append(" -splitparent ").Append(parentPid);
            sb.Append(" -padindex ").Append(pad);
            sb.Append(" -coopjoin 127.0.0.1 -coopport ").Append(port);
            sb.Append(" -coopname \"Giocatore ").Append(tile + 1).Append('"');
            if (!string.IsNullOrEmpty(logFile)) sb.Append(" -logFile \"").Append(logFile).Append('"');
            if (!string.IsNullOrWhiteSpace(extra)) sb.Append(' ').Append(extra.Trim());
            return sb.ToString();
        }

        static bool TryRect(string s, out RectInt r)
        {
            r = default;
            if (string.IsNullOrEmpty(s)) return false;
            var p = s.Split(',');
            if (p.Length != 4 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) || !int.TryParse(p[2], out int w) || !int.TryParse(p[3], out int h)) return false;
            r = new RectInt(x, y, w, h);
            return w > 0 && h > 0;
        }

        // ------------------------------------------------------------------ avvio

        /// <summary>Una finestra figlia: legge dove sta, quale controller usa e chi e' il giocatore 1, poi lascia fare alla cooperativa
        /// (-coopjoin, gia' negli argomenti) e al gestore delle finestre. Lo chiama GameManager.Awake prima di Coop.BootFromArgs.</summary>
        public static void BootFromArgs()
        {
            if (!IsChild || Active) return;
            Active = true; Tile = ChildTile; Count = Mathf.Clamp(IntArg("-splitcount", 2), MinPlayers, MaxPlayers);
            TryRect(Arg("-splittile"), out RectInt r); TileRect = r;
            parentPid = IntArg("-splitparent", 0);
            int pad = IntArg("-padindex", -1);
            PadOnly = pad >= 0;
            if (pad >= 0) PadControls.Enable(pad);
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 1; Application.targetFrameRate = -1;
            Debug.Log($"[PryingEyes] split: finestra {Tile + 1} di {Count}, controller {(pad >= 0 ? (pad + 1).ToString() : "nessuno")}, parte {TileRect}, giocatore 1 pid {parentPid}");
            EnsureManager();
        }

        /// <summary>Il giocatore 1 avvia lo split screen: ospita una cooperativa locale, lancia gli altri giocatori e si mette nella sua parte
        /// dello schermo. In caso di errore dice perche'.</summary>
        public static bool Begin(int count, bool p1Pad, out string error)
        {
            error = null;
            if (Active) { error = "Lo split screen e' gia' avviato"; return false; }
            if (!Supported) { error = "Lo split screen c'e' solo nel gioco per Windows"; return false; }
            if (count < MinPlayers || count > MaxPlayers) { error = "Da due a quattro giocatori"; return false; }
            if (!Coop.Available) { error = "La cooperativa non e' disponibile in questa versione"; return false; }
            if (Coop.Active) { error = "C'e' gia' una partita in rete"; return false; }

            ushort port = FreePort();
            if (!Coop.StartHost(port, true)) { error = Coop.Status; return false; }

            Active = true; Tile = 0; Count = count;
            Area = WorkArea();
            TileRect = TileOf(0, count, Area);
            var pads = AssignPads(PadControls.ConnectedMask(), count, p1Pad);
            PadOnly = pads[0] >= 0;
            if (pads[0] >= 0) PadControls.Enable(pads[0]); else PadControls.Disable();      // i controller sono degli altri giocatori: la ricerca automatica non deve prenderne uno
            Debug.Log($"[PryingEyes] split: {count} giocatori, porta {port}, schermo {Area}, il mio posto {TileRect}, controller {string.Join(",", pads)}");

            Screen.SetResolution(TileRect.width, TileRect.height, FullScreenMode.Windowed);
            LaunchChildren(count, Area, port, pads);
            EnsureManager();
            return true;
        }

        static int parentPid;

        /// <summary>Chiude lo split screen: il giocatore 1 chiude il gioco e le altre finestre lo seguono, una figlia lascia solo la partita.</summary>
        public static void Quit(string why)
        {
            Debug.Log("[PryingEyes] split: esco (" + why + ")");
            Application.Quit();
        }

        /// <summary>Una riga per il menu di pausa: come si passano tastiera e mouse da una finestra all'altra.</summary>
        public static string Hint => PadControls.Enabled
            ? "Questa finestra usa il controller " + (PadControls.Index + 1) + "."
            : "Questa finestra usa tastiera e mouse quando e' attiva: un clic o Alt+Tab li passa a un'altra finestra.";
    }
}
