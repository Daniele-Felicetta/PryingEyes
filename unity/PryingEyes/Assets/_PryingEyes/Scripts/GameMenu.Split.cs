using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Menu dello split screen (stesso PC): pannello per scegliere da due a quattro giocatori, attesa delle finestre figlie, testi
    /// del menu di pausa e della chiusura quando la finestra fa parte di una partita in split screen, promemoria dei comandi del controller.</summary>
    public partial class GameMenu
    {
        RectTransform splitPanel, splitWait;
        Text splitStatus, splitPads, splitWaitStatus, padHelp;
        Button splitP1Button;
        Button[] splitCountButtons;
        bool splitP1Pad;
        float splitRefreshAt;

        static readonly string PadSheet =
            "CONTROLLER\n" +
            "Stick sinistro: muovi (premuto: corsa)\n" +
            "Stick destro: guarda (premuto: coltello)\n" +
            "RT spara · LT mira\n" +
            "A salta · B abbassati · X ricarica · Y cambia arma\n" +
            "RB usa e compra · croce: sporgersi, torcia (su), acquasanta (giù)\n" +
            "LB + A velo · LB + B modo di fuoco · LB + X carillon · LB + Y mina\n" +
            "LB + su mappa · LB + giù indica le porte · LB + sinistra pila\n" +
            "Back tabellone · Start pausa";

        void BuildSplit()
        {
            splitPanel = Stretch(backdrop, "SplitScreen");
            Label(splitPanel, "Titolo", new Vector2(0, 200), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Schermo condiviso", Ink, FontStyle.Normal);
            Label(splitPanel, "Sottotitolo", new Vector2(0, 148), new Vector2(1100, 30), 20, TextAnchor.MiddleCenter,
                "Da due a quattro giocatori sullo stesso PC: ognuno ha la sua finestra, tutte insieme riempiono lo schermo.", Dim, FontStyle.Italic);
            splitPads = Label(splitPanel, "Controller", new Vector2(0, 104), new Vector2(1000, 30), 20, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            splitCountButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int n = i + 2;
                splitCountButtons[i] = MakeButton(splitPanel, n + " giocatori", new Vector2(0, 40 - i * 66), () => SplitStart(n));
            }
            splitP1Button = MakeButton(splitPanel, "", new Vector2(0, -158), () => { splitP1Pad = !splitP1Pad; RefreshSplitPanel(true); });
            splitStatus = Label(splitPanel, "Stato", new Vector2(0, -208), new Vector2(1100, 30), 20, TextAnchor.MiddleCenter, "", Ink, FontStyle.Italic);
            Label(splitPanel, "Nota", new Vector2(0, -250), new Vector2(1100, 44), 16, TextAnchor.MiddleCenter,
                "Il giocatore 1 usa tastiera e mouse, gli altri un controller Xbox (o compatibile) a testa. Un clic su una finestra, o Alt+Tab, passa tastiera e mouse a quella finestra.",
                Dim, FontStyle.Italic);
            MakeButton(splitPanel, "Indietro", new Vector2(0, -306), () => Show(mainPanel));

            splitWait = Stretch(backdrop, "AttesaSplitScreen");
            Label(splitWait, "Titolo", new Vector2(0, 70), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Schermo condiviso", Ink, FontStyle.Normal);
            splitWaitStatus = Label(splitWait, "Stato", new Vector2(0, 0), new Vector2(1100, 34), 22, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);

            padHelp = Label(pausePanel, "Comandi controller", new Vector2(420, 10), new Vector2(430, 300), 15, TextAnchor.UpperLeft, PadSheet, Dim, FontStyle.Normal);
            padHelp.gameObject.SetActive(false);
        }

        void OpenSplit()
        {
            splitStatus.text = "";
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
            splitPads.text = "Controller collegati: " + pads;
            var label = splitP1Button.GetComponentInChildren<Text>();
            if (label != null) label.text = "Giocatore 1: " + (splitP1Pad ? "controller" : "tastiera e mouse");
            bool ok = SplitScreen.Available;
            foreach (var b in splitCountButtons) b.interactable = ok;
            splitP1Button.interactable = ok;
            if (!ok)
            {
                splitStatus.text = !SplitScreen.Supported ? "Lo split screen c'è solo nel gioco per Windows."
                    : Coop.Active || SplitScreen.Active ? "C'è già una partita: torna al menu principale per cominciare."
                    : "La cooperativa non è disponibile in questa versione.";
                return;
            }
            int need = splitP1Pad ? 2 : 1;
            splitStatus.text = pads >= need ? "" : "Per due giocatori servono " + need + " controller: ne vedo " + pads + ". Gli altri giocatori aspettano il controller (si può comandare una finestra alla volta con Alt+Tab).";
        }

        /// <summary>Avvia lo split screen con n giocatori: ospita la partita locale, lancia le altre finestre e comincia la partita del giocatore 1.</summary>
        void SplitStart(int n)
        {
            if (!SplitScreen.Begin(n, splitP1Pad, out string error))
            {
                if (splitStatus != null) splitStatus.text = error;
                Debug.Log("[PryingEyes] split: non parte: " + error);
                return;
            }
            Play();                                   // la partita del giocatore 1 parte subito: gli altri entrano quando le loro finestre sono pronte
            GameManager.I?.hud?.Toast("Split screen: gli altri giocatori entrano appena le loro finestre sono pronte");
        }

        /// <summary>All'avvio: la finestra figlia mostra l'attesa; con -splitscreen N l'ospite parte da solo come dal bottone.</summary>
        void SplitAutoStart()
        {
            if (SplitScreen.IsChild) { Show(splitWait); return; }
            if (SplitScreen.AutoStartCount >= SplitScreen.MinPlayers) { splitP1Pad = SplitScreen.AutoP1Pad; SplitStart(SplitScreen.AutoStartCount); }
        }

        void SplitUpdate()
        {
            if (splitPanel == null) return;
            if (splitPanel.gameObject.activeSelf) RefreshSplitPanel();
            if (splitWait.gameObject.activeSelf && splitWaitStatus != null)
                splitWaitStatus.text = Coop.Active ? "Entro nella partita del giocatore 1... " + Coop.Status : "Cerco la partita del giocatore 1...";
        }

        /// <summary>"Torna al menu" in una finestra dello split screen non ha senso (le altre restano aperte): chiude lo split screen o lascia la partita.</summary>
        void SplitLeaveText()
        {
            var title = leavePanel.Find("Titolo");
            var confirm = leavePanel.Find("Pulsante Lascia la partita");
            bool split = SplitScreen.Active, child = SplitScreen.IsChild;
            if (title != null && title.GetComponent<Text>() != null)
                title.GetComponent<Text>().text = split ? (child ? "Lasciare la partita?" : "Chiudere lo split screen?") : "Tornare al menu?";
            if (split) leaveNote.text = child ? "La tua finestra si chiude, gli altri giocatori continuano."
                : "Il gioco si chiude e con lui le finestre degli altri giocatori. Progressi, armi e punti di questa discesa andranno persi.";
            var button = confirm != null ? confirm.GetComponent<Button>() : null;
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = split ? (child ? "Lascia la partita" : "Chiudi lo split screen") : "Lascia la partita";
            button.onClick.RemoveAllListeners();
            if (split) button.onClick.AddListener(() => SplitScreen.Quit("scelto dal menu"));
            else button.onClick.AddListener(() => GameManager.I?.ReturnToMenu());
        }

        void SplitPauseText()
        {
            var leave = pausePanel.Find("Pulsante Torna al menu");
            var text = leave != null ? leave.GetComponentInChildren<Text>() : null;
            if (text != null) text.text = SplitScreen.Active ? (SplitScreen.IsChild ? "Lascia la partita" : "Chiudi lo split screen") : "Torna al menu";
            if (padHelp != null) padHelp.gameObject.SetActive(PadControls.Enabled);
        }
    }
}
