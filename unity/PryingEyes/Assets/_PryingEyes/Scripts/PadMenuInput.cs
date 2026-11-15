using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>L'input dei menu (uGUI): al posto di quello dell'Input Manager di Unity, che per il controller legge solo lo stick sinistro e A
    /// (niente croce, niente B per tornare indietro, e due volte lo stesso tasto se lo si legge anche da XInput). Qui tastiera e controller
    /// arrivano da una parte sola: frecce/WASD/croce/stick sinistro muovono la selezione, Invio/Spazio/A confermano, Esc/B annullano.
    /// Il controller si legge da PadControls (XInput) e non da Input.GetAxis, quindi funziona anche in una finestra dello split screen che non
    /// ha il fuoco (vedi PadEventSystem).
    ///
    /// Gestisce anche due cose che uGUI non fa: (1) se non c'e' niente di selezionato (un clic nel vuoto, il menu che si apre) il primo
    /// comando di tastiera o controller si limita a selezionare il pulsante giusto, senza muoversi; (2) "modo navigazione": dopo un comando
    /// da tastiera o controller il puntatore del mouse viene tenuto fuori dallo schermo e il cursore si nasconde, cosi' un pulsante sotto un
    /// mouse fermo non resta acceso insieme a quello scelto col controller. Basta muovere il mouse per riavere il puntatore.</summary>
    public sealed class PadMenuInput : BaseInput
    {
        /// <summary>Gli ultimi comandi sono venuti da tastiera o controller, non dal mouse.</summary>
        public static bool NavMode { get; private set; }

        static int swallowFrame = -1;
        static bool holdSwallow;
        int latchedFrame = -1;
        Vector2 lastMouse;
        bool haveMouse, wasOpen;
        float openedAt;

        /// <summary>Per questo fotogramma i comandi di navigazione e conferma non arrivano al modulo (li ha gia' usati qualcun altro).</summary>
        public static void Swallow() => swallowFrame = Time.frameCount;
        static bool Swallowed => swallowFrame == Time.frameCount || holdSwallow;
        static bool Open => GameMenu.Blocked;

        /// <summary>Mette questo input nell'EventSystem (lo crea se manca). Lo chiama GameMenu.</summary>
        public static void Install()
        {
            var system = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            var go = system != null ? system.gameObject : new GameObject("EventSystem", typeof(PadEventSystem));
            var module = go.GetComponent<StandaloneInputModule>();
            if (module == null) module = go.AddComponent<StandaloneInputModule>();
            var input = go.GetComponent<PadMenuInput>();
            if (input == null) input = go.AddComponent<PadMenuInput>();
            module.inputOverride = input;
        }

        static float Key(KeyCode a, KeyCode b) => Input.GetKey(a) || Input.GetKey(b) ? 1f : 0f;

        /// <summary>Un campo di testo ha il fuoco: le lettere, WASD compresi, sono testo e non comandi.</summary>
        static bool Typing
        {
            get
            {
                var system = EventSystem.current;
                var selected = system != null ? system.currentSelectedGameObject : null;
                var field = selected != null ? selected.GetComponent<InputField>() : null;
                return field != null && field.isFocused;
            }
        }

        /// <summary>Una volta per fotogramma: chi comanda adesso (mouse o tastiera/controller) e, se non c'e' niente di selezionato, seleziona.</summary>
        void Latch()
        {
            int frame = Time.frameCount;
            if (latchedFrame == frame) return;
            latchedFrame = frame;

            bool open = Open;
            if (open && !wasOpen) { NavMode = PadControls.Preferred; openedAt = Time.unscaledTime; }      // il menu si apre: se si gioca col controller si parte col puntatore nascosto
            wasOpen = open;

            // il puntatore salta quando il cursore si sblocca all'apertura del menu: per un attimo non conta come un movimento del mouse
            Vector2 mouse = Input.mousePosition;
            bool moved = haveMouse && Time.unscaledTime - openedAt > 0.3f && (mouse - lastMouse).sqrMagnitude > 1f;
            bool mouseUsed = moved || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2) || Input.mouseScrollDelta.sqrMagnitude > 0f;
            lastMouse = mouse; haveMouse = true;
            if (!open) { holdSwallow = false; return; }
            // dopo aver scelto un pulsante al posto del comando, quello stesso movimento tenuto premuto non deve poi muoversi
            if (holdSwallow && PadControls.NavX == 0 && PadControls.NavY == 0 && Key(KeyCode.RightArrow, KeyCode.D) + Key(KeyCode.LeftArrow, KeyCode.A)
                + Key(KeyCode.UpArrow, KeyCode.W) + Key(KeyCode.DownArrow, KeyCode.S) == 0f) holdSwallow = false;

            bool padMove = PadControls.NavX != 0 || PadControls.NavY != 0 || PadControls.Confirm;
            bool typing = Typing;
            bool keyMove = !typing && (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)
                || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D));
            bool confirm = !typing && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) || PadControls.Confirm;
            if (mouseUsed) NavMode = false;
            else if (padMove || keyMove || confirm || PadControls.Cancel) NavMode = true;     // Esc no: apre la pausa, non e' navigare

            // niente di selezionato (un clic nel vuoto, un pannello appena cambiato): il primo comando sceglie dove stare e basta
            if (!mouseUsed && (padMove || keyMove || confirm))
            {
                var system = EventSystem.current;
                var selected = system != null ? system.currentSelectedGameObject : null;
                if (system != null && (selected == null || !selected.activeInHierarchy) && GameMenu.I != null)
                {
                    GameMenu.I.SelectDefault();
                    holdSwallow = true;
                }
            }
        }

        // ---- mouse: uguale a quello di Unity, tranne il puntatore in modo navigazione

        public override Vector2 mousePosition { get { Latch(); return NavMode ? new Vector2(-10000f, -10000f) : (Vector2)Input.mousePosition; } }
        public override bool GetMouseButtonDown(int button) { Latch(); return !NavMode && Input.GetMouseButtonDown(button); }
        public override bool GetMouseButtonUp(int button) { Latch(); return Input.GetMouseButtonUp(button); }
        public override bool GetMouseButton(int button) { Latch(); return Input.GetMouseButton(button); }

        // ---- tastiera e controller

        public override float GetAxisRaw(string axisName)
        {
            Latch();
            if (!Open || Swallowed) return 0f;
            if (axisName == "Horizontal") return Mathf.Clamp(Key(KeyCode.RightArrow, KeyCode.D) - Key(KeyCode.LeftArrow, KeyCode.A) + PadControls.NavX, -1f, 1f);
            if (axisName == "Vertical") return Mathf.Clamp(Key(KeyCode.UpArrow, KeyCode.W) - Key(KeyCode.DownArrow, KeyCode.S) + PadControls.NavY, -1f, 1f);
            return 0f;
        }

        public override bool GetButtonDown(string buttonName)
        {
            Latch();
            if (!Open || Swallowed) return false;
            switch (buttonName)
            {
                case "Submit": return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || PadControls.Confirm;
                case "Cancel": return Input.GetKeyDown(KeyCode.Escape) || PadControls.Cancel;
                default: return false;       // "Horizontal" e "Vertical": uGUI li usa solo per scattare a +-1, e i nostri assi lo sono gia'
            }
        }
    }

    /// <summary>L'EventSystem di uGUI, ma nello split screen resta "in primo piano" anche quando la finestra non lo e' (le finestre degli
    /// altri giocatori sono guidate da un controller e non hanno mai il fuoco): senza questo il suo menu di pausa non risponderebbe.</summary>
    public sealed class PadEventSystem : EventSystem
    {
        /// <summary>Per le prove con una finestra vera che non ha il fuoco: la UI ascolta lo stesso.</summary>
        public static bool AlwaysFocused;

        protected override void OnApplicationFocus(bool hasFocus) => base.OnApplicationFocus(hasFocus || SplitScreen.Active || AlwaysFocused);
    }
}
