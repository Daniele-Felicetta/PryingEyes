using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Una scelta a valori in fila (‹ valore ›) del menu delle impostazioni: un solo oggetto selezionabile per riga, come un cursore.
    /// Sinistra e destra (tastiera, croce, stick) cambiano il valore senza lasciare la riga, su e giu' passano alla riga vicina, Invio, A o un
    /// clic sulla parte destra vanno avanti, un clic sulla parte sinistra torna indietro. Cio' che significano i valori lo decide chi la costruisce
    /// (GameMenu.Settings.cs): qui c'e' solo l'input e il testo.</summary>
    public sealed class MenuChoice : Selectable, IPointerClickHandler, ISubmitHandler
    {
        /// <summary>Chiamato con -1 (indietro) o +1 (avanti).</summary>
        public Action<int> onStep;
        public Text valueLabel, rowLabel;
        /// <summary>I colori dei testi della riga quando e' accesa e quando e' spenta (la riga spenta sembra spenta, non scelta).</summary>
        public Color textOn = Color.white, textOff = new Color(0.60f, 0.70f, 0.67f, 0.45f);
        string text = "";

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            Color color = state == SelectionState.Disabled ? textOff : textOn;
            if (valueLabel != null) valueLabel.color = color;
            if (rowLabel != null) rowLabel.color = color;
        }

        public string Value
        {
            get => text;
            set { text = value ?? ""; if (valueLabel != null) valueLabel.text = text; }
        }

        /// <summary>Cambia di un passo (anche dalle prove). Una riga spenta non cambia.</summary>
        public void Step(int direction)
        {
            if (!IsActive() || !IsInteractable() || direction == 0) return;
            onStep?.Invoke(direction > 0 ? 1 : -1);
            MenuSound.Step();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsActive() || !IsInteractable()) return;
            var rt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out Vector2 local);
            Step(local.x < rt.rect.xMin + rt.rect.width * 0.28f ? -1 : 1);
        }

        public void OnSubmit(BaseEventData eventData) => Step(1);

        public override void OnMove(AxisEventData eventData)
        {
            if (IsActive() && IsInteractable())
            {
                if (eventData.moveDir == MoveDirection.Left) { Step(-1); eventData.Use(); return; }
                if (eventData.moveDir == MoveDirection.Right) { Step(1); eventData.Use(); return; }
            }
            base.OnMove(eventData);
        }
    }

    /// <summary>Su un comando del menu: il suggerimento che spiega cosa fa (si legge in basso nella schermata) e il tic quando ci si arriva. Si accende
    /// sia col puntatore sia con la tastiera e il controller.</summary>
    public sealed class MenuFeedback : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        /// <summary>Il testo fisso, o quello calcolato al momento (per il display, che dice cose diverse a seconda dello stato).</summary>
        public string hint;
        public Func<string> hintSource;
        /// <summary>Senza il tic (i cursori ne hanno uno loro).</summary>
        public bool quiet;
        /// <summary>Chiamato quando la voce viene scelta (con la tastiera, il controller o un clic): le schede lo usano per aprirsi.</summary>
        public Action selected;

        public string Hint => hintSource != null ? hintSource() : hint;

        public void OnSelect(BaseEventData eventData)
        {
            selected?.Invoke();
            GameMenu.I?.Feedback(this, true);
        }

        public void OnPointerEnter(PointerEventData eventData) => GameMenu.I?.Feedback(this, false);
    }

    /// <summary>Una schermata del menu che entra con una dissolvenza breve (0,2 s) e una piccola salita, sul tempo reale (il mondo e' fermo).
    /// Si spegne con l'oscillazione visuale a zero e nell'editor (le immagini di MenuReview escono finite).</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class MenuPanelFade : MonoBehaviour
    {
        public const float Duration = 0.2f, Rise = 14f;
        CanvasGroup group;
        RectTransform rect;
        float t = 1f;

        void Awake() { group = GetComponent<CanvasGroup>(); rect = (RectTransform)transform; }

        void OnEnable()
        {
            if (group == null) Awake();
            t = !Application.isPlaying || GameSettings.Motion < 0.01f ? 1f : 0f;
            Apply();
        }

        void OnDisable() { t = 1f; Apply(); }

        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.MoveTowards(t, 1f, Time.unscaledDeltaTime / Duration);
            Apply();
        }

        void Apply()
        {
            if (group == null || rect == null) return;
            float e = 1f - (1f - t) * (1f - t);
            group.alpha = e;
            rect.anchoredPosition = new Vector2(0f, (e - 1f) * Rise);
        }
    }

    /// <summary>Il pulsante del menu principale che "si fa avanti": quando e' acceso o scelto il testo scorre di qualche unita' verso destra e la
    /// freccia si illumina. Sul tempo reale (il menu e' a mondo fermo) e solo se l'oscillazione visuale non e' spenta.</summary>
    public sealed class MenuButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Text label, arrow;
        public float slide = 9f;
        Vector2 labelHome, arrowHome;
        bool pointer, selected, homed;
        float amount;
        static readonly Color ArrowIdle = new Color(0.69f, 0.53f, 0.30f), ArrowLit = new Color(1f, 0.84f, 0.50f);

        public void OnPointerEnter(PointerEventData eventData) { pointer = true; }
        public void OnPointerExit(PointerEventData eventData) { pointer = false; }
        public void OnSelect(BaseEventData eventData) { selected = true; }
        public void OnDeselect(BaseEventData eventData) { selected = false; }

        void OnDisable() { pointer = selected = false; amount = 0f; Apply(); }

        void Update()
        {
            if (label == null || !Application.isPlaying) return;
            if (!homed) { homed = true; labelHome = label.rectTransform.anchoredPosition; if (arrow != null) arrowHome = arrow.rectTransform.anchoredPosition; }
            bool lit = pointer || selected;
            float target = lit ? 1f : 0f;
            if (GameSettings.Motion < 0.01f) amount = target;
            else amount = Mathf.MoveTowards(amount, target, Time.unscaledDeltaTime * 7f);
            Apply();
        }

        void Apply()
        {
            if (label == null || !homed) return;
            float e = amount * amount * (3f - 2f * amount);
            label.rectTransform.anchoredPosition = labelHome + new Vector2(slide * e, 0f);
            if (arrow != null)
            {
                arrow.rectTransform.anchoredPosition = arrowHome + new Vector2(slide * 0.6f * e, 0f);
                arrow.color = Color.Lerp(ArrowIdle, ArrowLit, e);
            }
        }
    }
}
