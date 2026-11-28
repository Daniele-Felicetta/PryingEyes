using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Menu dello schermo condiviso (split screen: un solo gioco, una sola finestra): pannello per scegliere da due a quattro giocatori,
    /// promemoria dei comandi del controller nel menu di pausa e il testo di chi chiude la partita.</summary>
    public partial class GameMenu
    {
        RectTransform splitPanel;
        Text splitStatus, splitPads, splitSubtitle, padHelp;
        Button splitP1Button, splitMonitorButton;
        Button[] splitCountButtons;
        bool splitP1Pad;
        float splitRefreshAt;

        const string SplitSubtitle = "Da due a quattro giocatori sullo stesso PC, nello stesso gioco: lo schermo si divide in parti uguali.";

        static readonly string PadSheet =
            "CONTROLLER  (tra parentesi: tenendo premuto)\n" +
            "Stick sinistro: muovi (premuto: corsa)\n" +
            "Stick destro: guarda (tocco: coltello, tenuto: rampino)\n" +
            "RT spara · LT mira (L3 / tocco R3 per sporgerti)\n" +
            "A salta · B abbassati · Y cambia arma (modo di fuoco)\n" +
            "X ricarica (usa e compra) · RB acquasanta\n" +
            "LB magia della mano sinistra (sigillo sull'arma)\n" +
            "Croce su: torcia (cambia pila)\n" +
            "Croce giù: carillon (mina)\n" +
            "Croce sinistra: sigillo seguente (velo)\n" +
            "Croce destra: magia seguente (indica le porte)\n" +
            "Back mappa (tabellone) · Start pausa";

        void BuildSplit()
        {
            splitPanel = Stretch(backdrop, "SplitScreen");
            // sei righe di bottoni da 56 e tre di testo: "Indietro" deve finire sopra la riga dei comandi in fondo (a -298), come negli altri pannelli
            Label(splitPanel, "Titolo", new Vector2(0, 214), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Schermo condiviso", Ink, FontStyle.Normal);
            splitSubtitle = Label(splitPanel, "Sottotitolo", new Vector2(0, 164), new Vector2(1100, 30), 20, TextAnchor.MiddleCenter, SplitSubtitle, Dim, FontStyle.Italic);
            splitPads = Label(splitPanel, "Controller", new Vector2(0, 126), new Vector2(1000, 30), 20, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            splitCountButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int n = i + 2;
                splitCountButtons[i] = MakeButton(splitPanel, n + " giocatori", new Vector2(0, 76 - i * 62), () => SplitStart(n));
            }
            splitP1Button = MakeButton(splitPanel, "", new Vector2(0, -110), () => { splitP1Pad = !splitP1Pad; RefreshSplitPanel(true); });
            // la riga ha sempre due bottoni, uno accanto all'altro (RefreshSplitPanel); con un monitor solo il secondo non sceglie: spiega che cosa fare
            splitMonitorButton = MakeButton(splitPanel, "", new Vector2(190, -110), SplitCycleMonitors);
            splitStatus = Label(splitPanel, "Stato", new Vector2(0, -158), new Vector2(1100, 30), 20, TextAnchor.MiddleCenter, "", Ink, FontStyle.Italic);
            Label(splitPanel, "Nota", new Vector2(0, -194), new Vector2(1100, 44), 16, TextAnchor.MiddleCenter,
                "Ogni giocatore usa un controller Xbox (o compatibile). Il giocatore 1 ha anche tastiera e mouse; un controller libero lo adotta premendo un tasto.",
                Dim, FontStyle.Italic);
            MakeButton(splitPanel, "Indietro", new Vector2(0, -250), () => Show(mainPanel));

            padHelp = Label(pausePanel, "Comandi controller", new Vector2(420, -120), new Vector2(430, 300), 15, TextAnchor.UpperLeft, PadSheet, Dim, FontStyle.Normal);      // sotto la frase e la riga di rete: stanno al centro fino a +-260
            padHelp.gameObject.SetActive(false);
        }

        void OpenSplit()
        {
            splitStatus.text = "";
            monitorHintUntil = 0f;
            Debug.Log("[PryingEyes] split: pannello aperto, monitor: " + SplitScreen.MonitorReport());
            RefreshSplitPanel(true);
            Show(splitPanel);
        }

        /// <summary>Controller collegati, stato dei bottoni e promemoria (si rinfresca ogni mezzo secondo: XInput si interroga di rado).</summary>
        void RefreshSplitPanel(bool force = false)
        {
            if (!force && Time.unscaledTime < splitRefreshAt) return;
            splitRefreshAt = Time.unscaledTime + 0.5f;
            int mask = PadControls.ConnectedMask(), pads = 0;
            for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) pads++;
            int connected = SplitScreen.ConnectedMonitors, wanted = SplitScreen.WantedMonitors;
            bool multi = connected > 1, late = SplitScreen.MonitorsAddedAfterStart;
            // il numero di monitor si dice sempre (anche uno solo): chi ne ha due e non vede la scelta capisce che il gioco ne vede uno
            splitPads.text = "Controller collegati: " + pads + "   ·   Monitor collegati: " + connected + (late ? " (Windows ne vede " + SplitScreen.SystemMonitors + ")" : "");
            splitSubtitle.text = multi && wanted > 1 ? SplitScreen.Arrangement(wanted) : SplitSubtitle;      // su piu' monitor la riga dice come si dividono i giocatori
            var label = splitP1Button.GetComponentInChildren<Text>();
            if (label != null) label.text = "Giocatore 1: " + (splitP1Pad ? "controller (e tastiera)" : "tastiera e mouse");
            // il bottone c'e' sempre: con piu' monitor li sceglie, con uno solo (o se Windows ne ha di piu') spiega che cosa fare
            splitMonitorButton.gameObject.SetActive(true);
            ((RectTransform)splitP1Button.transform).anchoredPosition = new Vector2(-190f, -110f);
            var monitorLabel = splitMonitorButton.GetComponentInChildren<Text>();
            if (monitorLabel != null) monitorLabel.text = !multi && late ? "Monitor: riavvia il gioco" : "Monitor: " + MonitorWord(wanted);
            bool ok = SplitScreen.Available;
            foreach (var b in splitCountButtons) b.interactable = ok;
            splitP1Button.interactable = ok;
            splitMonitorButton.interactable = ok;
            if (!ok)
            {
                splitStatus.text = !SplitScreen.Supported ? "Lo schermo condiviso c'è solo nel gioco per Windows."
                    : Coop.Active || SplitScreen.Active ? "C'è già una partita: torna al menu principale per cominciare."
                    : "Non è disponibile in questa partita.";
                return;
            }
            int need = splitP1Pad ? 2 : 1;      // il minimo per due giocatori
            splitStatus.text = Time.unscaledTime < monitorHintUntil ? monitorHint
                : late ? MonitorsLateText()
                : pads >= need ? "" : "Per due giocatori servono " + need + " controller: ne vedo " + pads + ". Chi non ne ha uno aspetta, e entra col primo che si collega.";
        }

        static string MonitorWord(int n) => n <= 1 ? "uno solo" : n == 2 ? "due" : n == 3 ? "tre" : "quattro";

        string monitorHint = "";
        float monitorHintUntil;

        /// <summary>Unity conta i monitor all'avvio del gioco: se un monitor si accende o si estende dopo, Windows ne vede di piu' del gioco.</summary>
        static string MonitorsLateText() =>
            "Windows vede " + SplitScreen.SystemMonitors + " monitor, il gioco ne ha visti " + SplitScreen.RealMonitors + " all'avvio: chiudilo e riaprilo per usarli.";

        const string OneMonitorText = "Il gioco vede un solo monitor. Per giocare su due: collegalo in modalità Estendi (Win+P), poi riavvia il gioco.";

        /// <summary>Scelta dei monitor: uno solo, poi due, e cosi' fino a quanti ne sono collegati, poi di nuovo uno. Si ricorda fra una partita e l'altra.
        /// Con un monitor solo non c'e' niente da scegliere: il bottone dice che cosa fare (accendere il secondo in modalita' Estendi, o riavviare il
        /// gioco se Windows ne vede piu' del gioco), invece di non esserci. Con piu' monitor sceglie sempre; l'avviso dei monitor arrivati dopo
        /// l'avvio sta nella riga di stato.</summary>
        void SplitCycleMonitors()
        {
            if (SplitScreen.ConnectedMonitors < 2)
            {
                monitorHint = SplitScreen.MonitorsAddedAfterStart ? MonitorsLateText() : OneMonitorText;
                monitorHintUntil = Time.unscaledTime + 10f;
                Debug.Log("[PryingEyes] split: bottone Monitor: " + SplitScreen.MonitorReport());
                RefreshSplitPanel(true);
                return;
            }
            int next = SplitScreen.WantedMonitors + 1;
            if (next > SplitScreen.ConnectedMonitors) next = 1;
            SplitScreen.ChooseMonitors(next);
            RefreshSplitPanel(true);
        }

        /// <summary>Avvia lo schermo condiviso con n giocatori e comincia la partita: sono tutti nello stesso gioco, quindi parte tutto insieme.</summary>
        void SplitStart(int n)
        {
            if (NeedCharacter(splitPanel)) return;
            OpenCast(n);                    // prima ognuno sceglie il suo personaggio (Chi gioca?), poi "Comincia" avvia (SplitBegin)
        }

        /// <summary>Avvia davvero lo schermo condiviso, con i personaggi dei posti ('cast' puo' mancare: -splitscreen N sceglie da solo). Falso se non parte (l'errore e' scritto).</summary>
        bool SplitBegin(int n, System.Collections.Generic.IList<Characters.CharacterRecord> cast)
        {
            if (!SplitScreen.Begin(n, splitP1Pad, out string error, cast))
            {
                if (splitStatus != null) splitStatus.text = error;
                if (castStatus != null) castStatus.text = error;
                Debug.Log("[PryingEyes] split: non parte: " + error);
                return false;
            }
            Play();
            GameManager.I?.hud?.Toast("Schermo condiviso a " + n + " giocatori");
            return true;
        }

        /// <summary>All'avvio: con -splitscreen N il gioco parte da solo con N giocatori, come dal bottone del menu.</summary>
        void SplitAutoStart()
        {
            int n = SplitScreen.AutoStartCount;
            if (n < SplitScreen.MinPlayers) return;
            splitP1Pad = SplitScreen.AutoP1Pad;
            SplitBegin(Mathf.Min(n, SplitScreen.MaxPlayers), null);
        }

        void SplitUpdate()
        {
            if (splitPanel != null && splitPanel.gameObject.activeSelf) RefreshSplitPanel();
        }

        /// <summary>Chiudere la partita nello schermo condiviso la chiude per tutti: il testo lo dice.</summary>
        void SplitLeaveText()
        {
            if (SplitScreen.Active) leaveNote.text += "\nLo schermo condiviso si chiude per tutti i giocatori.";
        }

        void SplitPauseText()
        {
            if (padHelp != null) padHelp.gameObject.SetActive(PadControls.Preferred || (SplitScreen.Active && PadControls.ConnectedMask() != 0));
        }
    }
}
