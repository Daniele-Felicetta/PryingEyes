using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Split screen sullo stesso PC, da due a quattro giocatori, in UN solo gioco e UNA sola finestra.
    ///
    /// Come funziona: il mondo e' uno (piani, nemici, luci, punti, premi); ogni giocatore e' un "posto" (Seats.cs) con il suo corpo, la sua
    /// camera, il suo HUD e il suo stato, e vede il mondo in una porzione dello schermo (Camera.rect). Il codice scritto per un giocatore solo
    /// continua a funzionare perche' quando gira il codice di un posto, Seats.Enter mette al posto dei dati del giocatore quelli di quel posto.
    /// Non c'e' rete: i nemici, i piani e i premi sono quelli del gioco da solo, con piu' bersagli (Targets, come in cooperativa).
    ///
    /// Comandi: ogni giocatore puo' usare un controller (PadControls: XInput, uno slot per posto). Il giocatore 1 ha anche tastiera e mouse.
    /// Questa classe tiene solo le regole pure: la disposizione delle porzioni, l'assegnazione dei controller e l'angolo di campo.
    /// Documentazione: docs/multiplayer/split-screen.md.</summary>
    public static class SplitScreen
    {
        public const int MinPlayers = 2, MaxPlayers = 4;
        /// <summary>Oltre questo campo visivo orizzontale (gradi) la visuale si deforma troppo: nelle porzioni larghe si riduce quello verticale.</summary>
        public const float MaxHorizontalFov = 125f;

        // ---- argomenti (letti una volta, al primo uso)
        static string[] argv;
        static string[] args => argv ?? (argv = Environment.GetCommandLineArgs());
        static bool Has(string a) => Array.IndexOf(args, a) >= 0;
        static string Arg(string name)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
        static int IntArg(string name, int def) => int.TryParse(Arg(name), out int v) ? v : def;

        /// <summary>-splitscreen N: il gioco parte da solo in split screen con N giocatori (come se si fosse premuto il bottone del menu).</summary>
        public static int AutoStartCount => IntArg("-splitscreen", 0);
        /// <summary>-splitpad1: con -splitscreen anche il giocatore 1 usa subito un controller.</summary>
        public static bool AutoP1Pad => Has("-splitpad1");

        /// <summary>Lo split screen e' in corso.</summary>
        public static bool Active => Seats.Split;
        public static int Count => Seats.Count;

        /// <summary>Si puo' fare solo dove c'e' XInput per i controller (Windows).</summary>
        public static bool Supported
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Si puo' avviare adesso: sistema giusto, ai piani dell'hotel, nessuna partita in rete e non gia' avviato.</summary>
        public static bool Available => Supported && GameManager.proc && !Coop.Active && !Coop.WaitingForHost && !Active;

        // ------------------------------------------------------------------ disposizione (pura: la prova la controlla senza finestre)

        /// <summary>La porzione di schermo del giocatore 'tile' (0..count-1), in frazioni dello schermo con l'origine in basso a sinistra (come
        /// Camera.rect). Due giocatori: uno a sinistra e uno a destra; tre: due sopra e il terzo sotto a tutta larghezza; quattro: quattro
        /// quadranti. Le parti coprono lo schermo esattamente, senza buchi e senza sovrapposizioni.</summary>
        public static Rect ViewOf(int tile, int count)
        {
            if (count <= 1) return new Rect(0f, 0f, 1f, 1f);
            if (count == 2) return tile <= 0 ? new Rect(0f, 0f, 0.5f, 1f) : new Rect(0.5f, 0f, 0.5f, 1f);
            if (tile <= 0) return new Rect(0f, 0.5f, 0.5f, 0.5f);
            if (tile == 1) return new Rect(0.5f, 0.5f, 0.5f, 0.5f);
            if (count == 3) return new Rect(0f, 0f, 1f, 0.5f);
            return tile == 2 ? new Rect(0f, 0f, 0.5f, 0.5f) : new Rect(0.5f, 0f, 0.5f, 0.5f);
        }

        /// <summary>Quale controller (slot XInput) dare a ciascun giocatore, -1 = nessuno (tastiera e mouse). 'mask' dice quali slot hanno un
        /// controller collegato. I giocatori che ne hanno bisogno prendono i collegati in ordine; se non bastano, i primi slot ancora liberi
        /// (cosi' un controller collegato dopo funziona). Il giocatore 1 ne prende uno solo se p1Pad; altrimenti resta su tastiera e mouse e
        /// adotta da solo il primo controller libero su cui si preme un tasto.</summary>
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

        /// <summary>L'angolo di campo verticale da usare in una porzione larga 'aspect' volte la sua altezza: quello scelto dal giocatore, tranne
        /// quando darebbe piu' di MaxHorizontalFov gradi in orizzontale (porzioni molto larghe), e allora si accorcia.</summary>
        public static float FovFor(float verticalFov, float aspect, float maxHorizontal = MaxHorizontalFov)
        {
            if (aspect <= 0.01f) return verticalFov;
            float h = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            if (h <= maxHorizontal) return verticalFov;
            return 2f * Mathf.Atan(Mathf.Tan(maxHorizontal * 0.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------ avvio e fine

        /// <summary>Avvia lo split screen con 'count' giocatori: nascono i corpi, le camere e gli HUD degli altri e lo schermo si divide. Lo chiama
        /// il menu prima di "Gioca". In caso di errore dice perche'.</summary>
        public static bool Begin(int count, bool p1Pad, out string error)
        {
            error = null;
            if (Active) { error = "Lo split screen e' gia' avviato"; return false; }
            if (!Supported) { error = "Lo split screen c'e' solo nel gioco per Windows"; return false; }
            if (!GameManager.proc) { error = "Lo split screen c'e' solo ai piani dell'hotel"; return false; }
            if (Coop.Active || Coop.WaitingForHost) { error = "C'e' gia' una partita in rete"; return false; }
            return Seats.Begin(count, p1Pad, out error);
        }

        /// <summary>Una riga per il menu di pausa: chi usa cosa.</summary>
        public static string Hint
        {
            get
            {
                if (!Active) return "";
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < Seats.All.Count; i++)
                {
                    var s = Seats.All[i];
                    if (i > 0) sb.Append("   ");
                    sb.Append("G").Append(i + 1).Append(": ");
                    bool pad = s.pad != null && s.pad.index >= 0;
                    if (s.keyboard && pad) sb.Append("tastiera + controller ").Append(s.pad.index + 1);
                    else if (s.keyboard) sb.Append("tastiera e mouse");
                    else if (pad) sb.Append("controller ").Append(s.pad.index + 1);
                    else sb.Append("premi un tasto su un controller");
                }
                return sb.ToString();
            }
        }
    }
}
