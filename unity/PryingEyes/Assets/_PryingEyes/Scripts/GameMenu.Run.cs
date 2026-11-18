using PryingEyes.Characters;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La spedizione sospesa nel menu: dalla pausa "Salva e torna al menu" (solo da soli, tra un'ondata e l'altra: FloorDirector.SuspendProblem
    /// dice perche' no), e "Gioca" con una spedizione salvata chiede se riprenderla o ricominciare dalla cattedrale. La logica sta in
    /// FloorDirector.Run.cs e Characters.Roster; qui ci sono i pannelli.</summary>
    public partial class GameMenu
    {
        RectTransform runPanel;
        Text runNote, runPack, runHint;
        Button leaveSaveButton, leaveCancelButton, leaveGoButton;
        RunRecord runShown;

        /// <summary>Solo per la revisione del menu (MenuReview): finge che si possa salvare, per vedere il pannello con il terzo bottone.</summary>
        public static bool? TestCanSave;

        void BuildRun()
        {
            runPanel = Stretch(backdrop, "SpedizioneSospesa");
            Label(runPanel, "Titolo", new Vector2(0, 170), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Una spedizione ti aspetta", Ink, FontStyle.Normal);
            runNote = Label(runPanel, "Dettagli", new Vector2(0, 98), new Vector2(980, 64), 22, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            runPack = Label(runPanel, "Zaino", new Vector2(0, 46), new Vector2(980, 52), 18, TextAnchor.MiddleCenter, "", Dim, FontStyle.Normal);
            runHint = Label(runPanel, "Suggerimento", new Vector2(0, -4), new Vector2(980, 44), 17, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            MakeButton(runPanel, "Riprendi la spedizione", new Vector2(0, -60), RunResume);
            MakeButton(runPanel, "Ricomincia dalla cattedrale", new Vector2(0, -130), RunDiscard);
            MakeButton(runPanel, "Indietro", new Vector2(0, -200), () => Show(mainPanel));
            runPanel.gameObject.AddComponent<MenuPanelFade>();
        }

        /// <summary>"Gioca": se il personaggio ha una spedizione sospesa si chiede cosa farne (vero = il pannello e' aperto, la partita non parte).</summary>
        bool RunWaiting()
        {
            var run = Roster.SuspendedRun(Roster.Local);
            if (run == null) return false;
            ShowRun(run);
            return true;
        }

        void ShowRun(RunRecord run)
        {
            runShown = run;
            var who = Roster.Local;
            string when = Date(run.savedAt);
            runNote.text = (who != null ? who.name + " — " : "") + Cattedrale.Rami.NomeLivello(run.floor) + "\n" + run.score.ToString("N0", Italian) + " punti";
            runPack.text = "Nello zaino: " + Cattedrale.Risorse.Testo(RunSave.ToBundle(run.carried), "niente");
            runHint.text = (when.Length > 0 ? "Salvata il " + when + ". " : "") + "Riprende dall'inizio del livello: le ondate ricominciano.";
            Show(runPanel);
        }

        /// <summary>"Riprendi la spedizione": il gioco parte dal livello salvato (FloorDirector.StartRun prende la spedizione scelta qui).</summary>
        void RunResume()
        {
            if (runShown == null) { Show(mainPanel); return; }
            Cattedrale.Fabbrica.Seleziona(Roster.Local);
            Proc.FloorDirector.ResumeNext(runShown);
            runShown = null;
            Play();
        }

        /// <summary>"Ricomincia dalla cattedrale": la spedizione sospesa si perde come una abbandonata (lo zaino conta come una morte: in parte
        /// va nelle spoglie), e si parte dalla cattedrale. Il file non si distrugge: ne resta una copia in runs/consumed.</summary>
        void RunDiscard()
        {
            if (Roster.Local != null && runShown != null) RunSave.Discard(Roster.Local, runShown);
            runShown = null;
            Play();
        }

        /// <summary>Dalla pausa: salva la spedizione e torna al menu. Se non riesce la partita resta com'e' e il pannello dice perche'.</summary>
        void SaveAndLeave()
        {
            var fd = Proc.FloorDirector.I;
            string problem = "non c'è una spedizione da salvare";
            if (TestCanSave == null && fd != null && fd.SuspendExpedition(out problem))
            {
                GameManager.I?.ReturnToMenu();
                return;
            }
            leaveNote.text = "Non sono riuscito a salvare: " + problem + "\nLa partita resta com'è: puoi riprovare o restare qui.";
        }

        /// <summary>I bottoni della conferma "Tornare al menu?": con il salvataggio sono tre, senza due (senza buchi).</summary>
        void LeaveLayout(bool canSave)
        {
            leaveSaveButton.gameObject.SetActive(canSave);
            ((RectTransform)leaveSaveButton.transform).anchoredPosition = new Vector2(0, -60);
            ((RectTransform)leaveCancelButton.transform).anchoredPosition = new Vector2(0, canSave ? -130 : -60);
            ((RectTransform)leaveGoButton.transform).anchoredPosition = new Vector2(0, canSave ? -200 : -130);
        }

        /// <summary>Show() accende solo il pannello scelto: questo accende o spegne quello della spedizione sospesa.</summary>
        void RunShow(RectTransform p)
        {
            if (runPanel != null) runPanel.gameObject.SetActive(p == runPanel);
        }

        /// <summary>Indietro (Esc, B) dal pannello della spedizione sospesa. Vero se ne ha gestito uno.</summary>
        bool RunBack()
        {
            if (runPanel == null || !runPanel.gameObject.activeSelf) return false;
            MenuSound.Back();
            Show(mainPanel);
            return true;
        }
    }
}
