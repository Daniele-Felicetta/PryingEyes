using PryingEyes.Deserto;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La scelta della modalita' quando si comincia una partita nuova da soli: la standard di sempre (la cattedrale e le catacombe sotto) o il
    /// deserto (Scripts/Deserto). Compare dopo "Gioca" (e dopo "Ricomincia dalla cattedrale"); la scelta si ricorda per la volta dopo
    /// (DesertMode.Requested). La cooperativa e lo schermo condiviso restano nella modalita' standard.</summary>
    public partial class GameMenu
    {
        RectTransform modePanel;

        void BuildMode()
        {
            modePanel = Stretch(backdrop, "ModoDiGioco");
            Label(modePanel, "Titolo", new Vector2(0, 196), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Come vuoi giocare?", Ink, FontStyle.Normal);
            Label(modePanel, "Sottotitolo", new Vector2(0, 146), new Vector2(1000, 30), 20, TextAnchor.MiddleCenter, "La cattedrale e il magazzino sono gli stessi in tutte e due.", Dim, FontStyle.Italic);
            MakeButton(modePanel, "Standard", new Vector2(-200, 70), () => ChooseMode(false));
            MakeButton(modePanel, "Il deserto", new Vector2(200, 70), () => ChooseMode(true));
            Label(modePanel, "DescrizioneStandard", new Vector2(-200, -64), new Vector2(360, 190), 19, TextAnchor.UpperCenter,
                "I piani sotto la cattedrale, divisi in rami: ondate di nemici, porte da comprare, un boss in fondo a ogni ramo. Si scende e si risale con il bottino.", Dim, FontStyle.Normal);
            Label(modePanel, "DescrizioneDeserto", new Vector2(200, -64), new Vector2(360, 190), 19, TextAnchor.UpperCenter,
                "Un deserto enorme e quasi infinito, con il giorno e la notte. Scendi la scalinata della cattedrale: edifici interi e in rovina, torri altissime e catacombe, con i boss all'ultimo piano. In giro solo armi comuni, punti, munizioni, medikit ed essenza.", Dim, FontStyle.Normal);
            MakeButton(modePanel, "Indietro", new Vector2(0, -250), () => Show(mainPanel));
            modePanel.gameObject.AddComponent<MenuPanelFade>();
        }

        /// <summary>La partita nuova sta per cominciare: si chiede la modalita'. Le prove automatiche (-autoplay) non passano di qui.</summary>
        void ShowModes()
        {
            Show(modePanel);
            // la scelta dell'ultima volta e' gia' selezionata: Invio (o A) la ripete, la croce porta all'altra
            var want = modePanel.Find(DesertMode.Requested ? "Pulsante Il deserto" : "Pulsante Standard");
            var system = UnityEngine.EventSystems.EventSystem.current;
            if (want != null && system != null) system.SetSelectedGameObject(want.gameObject);
        }

        void ChooseMode(bool desert)
        {
            DesertMode.Requested = desert;
            Play();
        }

        void ModeShow(RectTransform p)
        {
            if (modePanel != null) modePanel.gameObject.SetActive(p == modePanel);
        }

        /// <summary>Indietro (Esc, B) dalla scelta della modalita'. Vero se ne ha gestito uno.</summary>
        bool ModeBack()
        {
            if (modePanel == null || !modePanel.gameObject.activeSelf) return false;
            MenuSound.Back();
            Show(mainPanel);
            return true;
        }
    }
}
