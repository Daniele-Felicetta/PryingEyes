using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il menu usato col controller (e con la tastiera): una cornice sulla voce scelta, i suggerimenti dei tasti che seguono il
    /// dispositivo in uso (tastiera o controller), la scelta del pulsante giusto quando non c'e' niente di selezionato, l'uscita da un campo
    /// di testo senza tastiera, il ritorno al pulsante da cui si era partiti. L'input vero e' in PadMenuInput.</summary>
    public partial class GameMenu
    {
        public const string FooterKeys = "WASD / FRECCE   NAVIGA       ·       INVIO   SCEGLI       ·       ESC   INDIETRO";
        public const string FooterPad = "CROCE / STICK   NAVIGA       ·       A   SCEGLI       ·       B   INDIETRO       ·       START   PAUSA";

        RectTransform currentPanel, focusRing;
        Image[] ringBars;
        readonly Stack<(RectTransform panel, GameObject selected)> panelStack = new Stack<(RectTransform panel, GameObject selected)>();
        Text footer, helpMove, helpRound, helpPad;
        string shownHints;
        int prevNavX, prevNavY;
        readonly Vector3[] ringCorners = new Vector3[4];

        static string HelpMoveText(bool pad) => "TIENITI IN MOVIMENTO\nI nemici ti inseguono subito. Usa scale, balconate e ripari per creare una via di fuga. " +
            (pad ? "R3 per il corpo a corpo." : "G per il corpo a corpo.");
        static string HelpRoundText(bool pad) => "APRI NUOVE AREE\nLe ondate partono da sole. Spendi i punti alle porte con " + (pad ? "RB" : "F") +
            ": le aree acquistate restano aperte per tutti. " + (pad ? "LB + giù indica le porte." : "V indica le porte.");

        /// <summary>La cornice che segue la voce scelta: quattro liste sottili in un rettangolo che si adatta a quello che e' selezionato.</summary>
        void BuildPadUi()
        {
            focusRing = Rect(backdrop, "Selezione", new Vector2(100, 40), Vector2.zero);
            ringBars = new[]
            {
                RingBar(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, 2)),       // sopra
                RingBar(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, 2)),       // sotto
                RingBar(new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f), new Vector2(2, 0)),       // sinistra
                RingBar(new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f), new Vector2(2, 0)),       // destra
            };
            focusRing.gameObject.SetActive(false);
        }

        Image RingBar(Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
        {
            var rt = Rect(focusRing, "Lato", size, Vector2.zero);
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot; rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
            var image = rt.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Il primo pulsante utilizzabile di un pannello (se sono tutti spenti, il primo).</summary>
        static GameObject FirstSelectable(RectTransform panel)
        {
            if (panel == null) return null;
            Button fallback = null;
            foreach (var b in panel.GetComponentsInChildren<Button>(false))
            {
                if (b.IsInteractable()) return b.gameObject;
                if (fallback == null) fallback = b;
            }
            return fallback != null ? fallback.gameObject : null;
        }

        /// <summary>Non c'e' niente di selezionato (un clic nel vuoto): sceglie il primo pulsante della schermata. Lo chiama PadMenuInput.</summary>
        internal void SelectDefault()
        {
            var system = EventSystem.current;
            if (system == null) return;
            var pick = FirstSelectable(displayPending ? displayConfirm : currentPanel);
            if (pick != null) system.SetSelectedGameObject(pick);
        }

        /// <summary>Ogni fotogramma con un menu aperto. Ritorna true se B va letto come "indietro".</summary>
        bool PadUpdate()
        {
            RefreshHints();
            UpdateRing();
            var system = EventSystem.current;
            var selected = system != null ? system.currentSelectedGameObject : null;
            var field = selected != null ? selected.GetComponent<InputField>() : null;
            int nx = PadControls.NavX, ny = PadControls.NavY;
            bool edge = (nx != 0 && nx != prevNavX) || (ny != 0 && ny != prevNavY);
            prevNavX = nx; prevNavY = ny;
            if (field != null && field.isFocused && (PadControls.Cancel || PadControls.Confirm || edge))
            {
                // il controller non scrive: A, B o la croce chiudono la modifica del campo (e la croce passa anche alla voce vicina)
                field.DeactivateInputField();
                PadMenuInput.Swallow();
                if (edge)
                {
                    var next = ny > 0 ? field.FindSelectableOnUp() : ny < 0 ? field.FindSelectableOnDown() : nx < 0 ? field.FindSelectableOnLeft() : field.FindSelectableOnRight();
                    if (next != null) system.SetSelectedGameObject(next.gameObject);
                }
                return false;
            }
            return PadControls.Cancel;
        }

        /// <summary>La cornice sulla voce scelta, solo quando si naviga con tastiera o controller (col mouse il puntatore basta).</summary>
        void UpdateRing()
        {
            var system = EventSystem.current;
            var selected = system != null ? system.currentSelectedGameObject : null;
            var selectable = selected != null && selected.activeInHierarchy ? selected.GetComponent<Selectable>() : null;
            var target = selectable != null && selectable.IsInteractable() && PadMenuInput.NavMode ? selectable.transform as RectTransform : null;
            if (target == null) { if (focusRing.gameObject.activeSelf) focusRing.gameObject.SetActive(false); return; }
            if (!focusRing.gameObject.activeSelf) focusRing.gameObject.SetActive(true);
            if (focusRing.GetSiblingIndex() != backdrop.childCount - 1) focusRing.SetAsLastSibling();      // sopra tutto, anche sopra la conferma dello schermo
            target.GetWorldCorners(ringCorners);
            Vector3 low = backdrop.InverseTransformPoint(ringCorners[0]), high = backdrop.InverseTransformPoint(ringCorners[2]);
            focusRing.anchoredPosition = (low + high) * 0.5f;
            focusRing.sizeDelta = new Vector2(high.x - low.x + 8f, high.y - low.y + 8f);
            var color = new Color(1f, 0.82f, 0.46f, 0.80f + 0.20f * Mathf.Sin(Time.unscaledTime * 6f));
            foreach (var bar in ringBars) bar.color = color;
        }

        /// <summary>Le scritte che nominano i tasti seguono il dispositivo in uso: tastiera oppure controller.</summary>
        void RefreshHints()
        {
            bool pad = PadControls.Preferred, split = SplitScreen.Active;
            string key = (pad ? "pad" : "tastiera") + (split ? "+split" : "");
            if (shownHints == key) return;
            shownHints = key;
            footer.text = pad ? FooterPad : FooterKeys;
            if (pauseHint != null)      // nello schermo condiviso riprende anche Start di un altro controller (la pausa e' di tutti)
                pauseHint.text = (Coop.Active ? "Gli altri continuano a giocare. " : "La partita è sospesa. ") + (pad ? "Start o B per riprendere." : split ? "Esc o Start per riprendere." : "Esc per riprendere.");
            if (padHelp != null) padHelp.gameObject.SetActive(pad);
            helpPad.gameObject.SetActive(pad);
            helpMove.text = HelpMoveText(pad);
            helpRound.text = HelpRoundText(pad);
        }
    }
}
