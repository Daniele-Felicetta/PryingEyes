using PryingEyes.Deserto;
using PryingEyes.Pista;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La scelta della modalita' quando si comincia una partita nuova: la standard di sempre (la cattedrale e le catacombe sotto), il
    /// deserto (Scripts/Deserto) o la moto su pista (Scripts/Pista, preparazione in GameMenu.Pista.cs). Compare dopo "Gioca" (e dopo "Ricomincia dalla cattedrale") e, dalla 0.9.1, dopo "Ospita una partita": in cooperativa
    /// il deserto si gioca fino a quattro e la modalita' la sceglie chi ospita (gli altri partono dove gioca lui); e, sempre dalla 0.9.1, nello schermo condiviso: dopo
    /// "Chi gioca?" (i personaggi dei posti) si sceglie la partita per tutti. La scelta si ricorda per la volta dopo (DesertMode.Requested).</summary>
    public partial class GameMenu
    {
        RectTransform modePanel;
        Text modeTitle, modeSubtitle;
        Button modePistaButton;
        /// <summary>La scelta e' per la partita cooperativa che sto per ospitare (CoopHost): scelta la modalita' si apre la porta (HostNow).</summary>
        bool modeForCoop { get => PistaMode.ForCoop; set => PistaMode.ForCoop = value; }          // the career reads it too (PistaMode.CareerApplies): a co-op host picks any bike
        /// <summary>La scelta e' per lo schermo condiviso (0.9.1): scelta la modalita' si avvia (SplitBegin) con i posti e i personaggi gia' decisi in "Chi gioca?".</summary>
        bool modeForSplit;
        int modeSplitCount;
        System.Collections.Generic.IList<Characters.CharacterRecord> modeSplitCast;

        void BuildMode()
        {
            modePanel = Stretch(backdrop, "ModoDiGioco");
            modeTitle = Label(modePanel, "Titolo", new Vector2(0, 196), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Come vuoi giocare?", Ink, FontStyle.Normal);
            modeSubtitle = Label(modePanel, "Sottotitolo", new Vector2(0, 146), new Vector2(1000, 30), 20, TextAnchor.MiddleCenter, "La cattedrale e il magazzino sono gli stessi in tutte e due.", Dim, FontStyle.Italic);
            // tre colonne: la standard, il deserto e (dalla 0.10) la moto su pista (GameMenu.Pista.cs)
            MakeButton(modePanel, "Standard", new Vector2(-380, 70), () => ChooseMode(false));
            MakeButton(modePanel, "Il deserto", new Vector2(0, 70), () => ChooseMode(true));
            modePistaButton = MakeButton(modePanel, "Su pista", new Vector2(380, 70), ChoosePista);
            Label(modePanel, "DescrizioneStandard", new Vector2(-380, -64), new Vector2(340, 190), 18, TextAnchor.UpperCenter,
                "I piani sotto la cattedrale, divisi in rami: ondate di nemici, porte da comprare, un boss in fondo a ogni ramo. Si scende e si risale con il bottino. Anche in cooperativa e a schermo condiviso.", Dim, FontStyle.Normal);
            Label(modePanel, "DescrizioneDeserto", new Vector2(0, -64), new Vector2(340, 190), 18, TextAnchor.UpperCenter,
                "Un deserto enorme, con il giorno e la notte. Scendi la scalinata della cattedrale: edifici interi e in rovina, torri altissime e catacombe con i boss. In giro armi comuni, punti, munizioni, medikit ed essenza. Anche in cooperativa e a schermo condiviso, fino a quattro.", Dim, FontStyle.Normal);
            Label(modePanel, "DescrizionePista", new Vector2(380, -64), new Vector2(340, 190), 18, TextAnchor.UpperCenter,
                "Gare in moto su un circuito disegnato a ogni partita: griglia, semaforo, giri e piazzamenti contro i piloti del computer. Fisica vera: inclinazione, gomme, frenata, impennate. In cooperativa fino a quattro, con la minimappa. Non a schermo condiviso.", Dim, FontStyle.Normal);
            MakeButton(modePanel, "Indietro", new Vector2(0, -250), ModeCancel);
            modePanel.gameObject.AddComponent<MenuPanelFade>();
        }

        /// <summary>La partita nuova sta per cominciare: si chiede la modalita'. Le prove automatiche (-autoplay) non passano di qui. Con 'forCoop' e' la partita
        /// che si sta per ospitare: il titolo lo dice e la scelta apre la porta invece di cominciare da soli.</summary>
        void ShowModes(bool forCoop = false, bool forSplit = false)
        {
            modeForCoop = forCoop;
            modeForSplit = forSplit;
            if (modeTitle != null) modeTitle.text = forCoop ? "Che partita ospiti?" : forSplit ? "Che partita giocate?" : "Come vuoi giocare?";
            if (modeSubtitle != null) modeSubtitle.text = forCoop ? "Gli altri entrano nella tua, fino a quattro: ognuno ha le sue armi e i suoi punti."
                : forSplit ? "Giocate tutti la stessa, sullo stesso schermo: ognuno ha le sue armi e i suoi punti." : "La cattedrale e il magazzino sono gli stessi in tutte e due.";
            if (modePistaButton != null) modePistaButton.interactable = !forSplit;         // la moto su pista non c'e' a schermo condiviso
            Show(modePanel);
            // la scelta dell'ultima volta e' gia' selezionata: Invio (o A) la ripete, la croce porta alle altre
            bool pista = PistaMode.Requested && !forSplit;
            var want = modePanel.Find(pista ? "Pulsante Su pista" : DesertMode.Requested ? "Pulsante Il deserto" : "Pulsante Standard");
            var system = UnityEngine.EventSystems.EventSystem.current;
            if (want != null && system != null) system.SetSelectedGameObject(want.gameObject);
        }

        void ChooseMode(bool desert)
        {
            DesertMode.Requested = desert;
            PistaMode.Requested = false;
            if (modeForCoop) { modeForCoop = false; HostNow(); return; }
            if (modeForSplit)
            {
                modeForSplit = false;
                if (!SplitBegin(modeSplitCount, modeSplitCast)) Show(castPanel);        // se non parte, si torna a "Chi gioca?", dove l'errore e' scritto
                return;
            }
            Play();
        }

        void ModeShow(RectTransform p)
        {
            if (modePanel != null) modePanel.gameObject.SetActive(p == modePanel);
        }

        /// <summary>Indietro dalla scelta: alla schermata da cui si era venuti (il menu principale, la cooperativa se si stava per ospitare, "Chi gioca?" nello schermo condiviso).</summary>
        void ModeCancel()
        {
            bool coop = modeForCoop, split = modeForSplit;
            modeForCoop = modeForSplit = false;
            Show(coop ? coopPanel : split ? castPanel : mainPanel);
        }

        /// <summary>Indietro (Esc, B) dalla scelta della modalita'. Vero se ne ha gestito uno.</summary>
        bool ModeBack()
        {
            if (modePanel == null || !modePanel.gameObject.activeSelf) return false;
            MenuSound.Back();
            ModeCancel();
            return true;
        }
    }
}
