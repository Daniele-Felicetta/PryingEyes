using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Menu principale, pausa (Esc) e impostazioni audio, costruiti a runtime come l'HUD.
    /// All'avvio il gioco resta fermo sul menu; "Gioca" fa partire la prima ondata. In pausa il tempo si ferma,
    /// l'HUD si nasconde e il mouse torna libero. Blocked dice agli altri script di non prendere input.</summary>
    public partial class GameMenu : MonoBehaviour
    {
        public static GameMenu I;
        public static bool Blocked { get; private set; }
        public static bool InMenu { get; private set; }
        public static bool Paused { get; private set; }
        static bool overlay;
        /// <summary>Un'interfaccia sopra il gioco (il grimorio della stanza segreta): blocca i comandi come la pausa e libera il mouse, ma
        /// non ferma il tempo, non nasconde l'HUD e Esc la chiude lei (GrimoireUI). E' di chi gira: nello schermo condiviso ogni posto ha il
        /// suo (Seats lo salva e lo rimette a ogni cambio di posto, Seats.SetOverlay lo da' a un posto qualsiasi), quindi chi legge si
        /// ferma e gli altri giocano.</summary>
        public static bool Overlay
        {
            get => overlay;
            set { overlay = value; Blocked = InMenu || Paused || overlay; }
        }

        static readonly Color Ink = new Color(0.93f, 0.89f, 0.80f);
        static readonly Color Dim = new Color(0.60f, 0.70f, 0.67f);
        static readonly Color BtnColor = new Color(0.055f, 0.12f, 0.12f, 0.98f);

        RectTransform backdrop, mainPanel, pausePanel, settingsPanel, settingsFrom, coopPanel, helpPanel, helpFrom, leavePanel;
        Slider sMaster, sEffects, sMusic, sBrightness, sSensitivity, sFov, sMotion, sCoopHud;
        Text vMaster, vEffects, vMusic, vBrightness, vSensitivity, vFov, vMotion, vCoopHud, coopStatus, pauseNet, pauseHint, pauseStats;
        InputField coopName, coopAddr;
        Button hostButton, joinButton, bellButton;
        Text leaveNote;

        public static void Create()
        {
            if (I != null) return;
            I = new GameObject("GameMenu").AddComponent<GameMenu>();
        }

        void Awake()
        {
            I = this;
            Paused = false;
            Build();
            InMenu = true;
            SyncSliders();
            MenuMusic.Create();             // il tema del menu: si calcola in un thread e parte appena e' pronto
            Show(mainPanel);
            Refresh();
        }

        void Start()
        {
            Refresh();
            // prova senza mouse: -autoplay preme "Gioca" da solo (per far girare tutto il percorso del giocatore vero, non solo quello del bot)
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-autoplay") { Play(); break; }
            SplitAutoStart();       // -splitscreen N: lo schermo condiviso parte da solo, come dal bottone
        }

        void Update()
        {
            bool confirming = displayPending;
            UpdateDisplayPreview();
            if (confirming) return;
            if (overlay && !InMenu && !Paused)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = !PadMenuInput.NavMode;
                return;                                       // l'interfaccia sopra il gioco gestisce da se' Esc e il controller
            }
            bool padBack = false;
            if (Blocked)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = !PadMenuInput.NavMode;      // col controller o la tastiera il cursore sta nascosto
                padBack = PadUpdate();
            }
            if (coopPanel.gameObject.activeSelf && coopStatus != null)
            {
                coopStatus.text = Coop.Available ? Coop.Status : "La cooperativa non è disponibile in questa versione.";
                hostButton.interactable = joinButton.interactable = Coop.Available && !Coop.Active;
            }
            SplitUpdate();
            if (Blocked) UpdateTips();
            if (settingsPanel.gameObject.activeSelf)        // PagSu e PagGiu: da una scheda all'altra, da qualunque riga
            {
                int step = Input.GetKeyDown(KeyCode.PageDown) ? 1 : Input.GetKeyDown(KeyCode.PageUp) ? -1 : 0;
                if (step != 0) CyclePage(step);
            }
            // Esc e Start (di qualunque controller) aprono e chiudono la pausa; l'Esc che ha appena chiuso il grimorio no
            bool toggle = (Input.GetKeyDown(KeyCode.Escape) && !GrimoireUI.ClosedThisFrame) || TouchControls.Down(TouchControls.Action.Pause) || PadControls.PausePressed;
            if (!toggle && !padBack) return;                                                                         // B torna solo indietro: in gioco e' "abbassati"
            if (settingsPanel.gameObject.activeSelf) { MenuSound.Back(); Back(); return; }
            if (leavePanel.gameObject.activeSelf) { MenuSound.Back(); Show(pausePanel); return; }
            if (helpPanel.gameObject.activeSelf) { MenuSound.Back(); Show(helpFrom); return; }
            if (splitPanel.gameObject.activeSelf) { MenuSound.Back(); Show(mainPanel); return; }
            if (coopPanel.gameObject.activeSelf) { MenuSound.Back(); CoopBack(); return; }
            if (InMenu) return;
            if (Paused) Resume(); else if (toggle) Pause();
        }

        // ---------- stato ----------

        // 0.8.0: "Pausa a finestra inattiva" (Impostazioni > Gioco) la si spegne per giocare con un'altra finestra accanto
        void OnApplicationFocus(bool focused)
        {
            if (focused) SplitScreen.OnFocusGained();          // le finestre dei monitor accesi che un altro programma copriva tornano davanti
            if (!focused && !GameManager.autoTest && GameSettings.PauseOnFocusLoss && !SplitScreen.JustActivated) Pause();      // JustActivated: Windows da' il fuoco alla finestra del monitor appena acceso
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && !GameManager.autoTest && GameSettings.PauseOnFocusLoss) Pause();
        }

        void OnDestroy()
        {
            CancelDisplayPreview();
            if (I != this) return;
            I = null;
            Blocked = InMenu = Paused = false;
            Time.timeScale = 1f;
        }

        void AskLeave()
        {
            var fd = Proc.FloorDirector.I;
            bool expedition = Cattedrale.Catacombe.Hub && fd != null && !fd.InHub && Cattedrale.Fabbrica.Spedizione.Active;
            leaveNote.text = (expedition ? "Abbandoni la spedizione: perdi lo zaino come se fossi morto (le spoglie restano nel ramo). La cattedrale e il magazzino restano."
                : fd != null && fd.InHub ? "La cattedrale, il magazzino e le opere restano salvati." : "La partita attuale verrà chiusa. Progressi, armi e punti di questa discesa andranno persi.") +
                (Coop.IsHost ? "\nI compagni verranno scollegati e potranno proseguire da soli." : "");
            SplitLeaveText();
            Show(leavePanel);
        }

        void Play()
        {
            InMenu = false;
            Show(null);
            Refresh();
            if (GameManager.I != null) GameManager.I.BeginRun();
        }

        /// <summary>Cooperativa: l'ospite ha salutato (o ha avviato lui la partita): il menu si toglie e il gioco parte.</summary>
        public void CoopEnter()
        {
            if (!InMenu) return;
            InMenu = false;
            Show(null);
            Refresh();
        }

        void OpenCoop()
        {
            coopName.text = Coop.PlayerName;
            coopAddr.text = Coop.LastAddress;
            coopStatus.text = Coop.Available ? "" : "La cooperativa non c'è in questa versione.";
            Show(coopPanel);
        }

        void CoopHost()
        {
            Coop.PlayerName = coopName.text;
            if (Coop.StartHost())
            {
                Play();                              // la partita dell'ospite parte subito: gli altri entrano quando vogliono
                GameManager.I?.hud?.Toast(NetInfo(2) + " - lo ritrovi in pausa");
            }
            else coopStatus.text = Coop.Status;
        }

        /// <summary>Che cosa dire agli amici (ospite) o a chi gioca (ospitato), per il menu di pausa.</summary>
        static string NetInfo(int maxAddr = 4) => SplitScreen.Active ? "Split screen a " + SplitScreen.Count + " giocatori" + (Seats.Monitors > 1 ? " su " + Seats.Monitors + " monitor" : "") + ". " + SplitScreen.Hint : NetInfoOnline(maxAddr);
        static string NetInfoOnline(int maxAddr)
        {
            if (Coop.IsHost)
            {
                string a = Coop.LocalAddresses(maxAddr);
                return "Partita aperta: gli altri scrivono " + (a.Length > 0 ? a : "il tuo indirizzo") + " (UDP " + CoopProtocol.DefaultPort + ")";
            }
            return Coop.IsClient ? "In cooperativa: sei collegato all'ospite." : "";
        }

        void CoopJoin()
        {
            Coop.PlayerName = coopName.text;
            Coop.StartJoin(coopAddr.text);
            coopStatus.text = Coop.Status;
        }

        void CoopBack()
        {
            if (Coop.WaitingForHost) Coop.Stop("");     // stava ancora provando a entrare: lascia stare
            PlayerPrefs.Save();
            Show(mainPanel);
        }

        /// <summary>La riga sotto "In pausa": ondata, piano, punti, uccisioni e tempo di gioco (le stesse cifre del tabellone, Tab).</summary>
        static string PauseStatsText()
        {
            var gm = GameManager.I;
            if (gm == null) return "";
            int round = Proc.RoundDirector.Round, floor = Proc.FloorDirector.I != null ? Proc.FloorDirector.I.floor : 0;
            int secs = Mathf.FloorToInt(Time.timeSinceLevelLoad);
            var it = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
            return (round > 0 ? "ONDATA " + HUD.Roman(round) + "  ·  PIANO " + floor : "PIANO " + floor) + "   ·   " + gm.score.ToString("N0", it) + " PUNTI   ·   "
                + gm.kills + " UCCISIONI   ·   " + secs / 60 + ":" + (secs % 60).ToString("00");
        }

        void Pause()
        {
            if (InMenu || Paused) return;
            Paused = true;
            if (pauseStats != null) pauseStats.text = PauseStatsText();
            if (pauseNet != null) pauseNet.text = NetInfo(); SplitPauseText();
            var fd = Proc.FloorDirector.I;
            if (bellButton != null) bellButton.gameObject.SetActive(Cattedrale.Fabbrica.CampanellaPronta && fd != null && !fd.InHub && fd.floor >= 1);
            Show(pausePanel);
            shownHints = null; RefreshHints();       // la frase "per riprendere" nomina i tasti del dispositivo in uso
            Refresh();
        }

        void Resume()
        {
            Paused = false;
            Show(null);
            Refresh();
        }

        /// <summary>La campanella del Campanile: si chiude la pausa e si torna alla cattedrale con tutto lo zaino.</summary>
        void RingBell()
        {
            Resume();
            Proc.FloorDirector.I?.UseReturnBell();
        }

        void OpenSettings(RectTransform from)
        {
            settingsFrom = from;
            SyncSettings();
            var open = page ?? pages[0];            // si riapre sull'ultima scheda usata
            Show(settingsPanel);
            SelectPage(open);
            EventSystem.current?.SetSelectedGameObject(open.tab.gameObject);
        }

        void Back()
        {
            if (displayPending) { CancelDisplayPreview(); return; }
            PlayerPrefs.Save();
            Show(settingsFrom);
        }

        static void Quit() => Application.Quit();

        void Show(RectTransform p)
        {
            var system = EventSystem.current;
            // tornando indietro alla schermata da cui si era partiti si ritrova il pulsante che l'aveva aperta
            GameObject restore = null;
            if (p == null) panelStack.Clear();
            else if (panelStack.Count > 0 && panelStack.Peek().panel == p) restore = panelStack.Pop().selected;
            else if (currentPanel != null && currentPanel != p) panelStack.Push((currentPanel, system != null ? system.currentSelectedGameObject : null));
            currentPanel = p;
            showFrame = Time.frameCount;            // la scelta automatica del primo pulsante non fa il tic
            mainPanel.gameObject.SetActive(p == mainPanel);
            pausePanel.gameObject.SetActive(p == pausePanel);
            settingsPanel.gameObject.SetActive(p == settingsPanel);
            coopPanel.gameObject.SetActive(p == coopPanel);
            helpPanel.gameObject.SetActive(p == helpPanel);
            leavePanel.gameObject.SetActive(p == leavePanel); splitPanel.gameObject.SetActive(p == splitPanel);
            if (system != null)
            {
                system.SetSelectedGameObject(null);
                var pick = restore != null && restore.activeInHierarchy ? restore : FirstSelectable(p);
                if (pick != null) system.SetSelectedGameObject(pick);
            }
        }

        void Refresh()
        {
            Blocked = InMenu || Paused || overlay;
            bool menuUp = InMenu || Paused;
            // in cooperativa gli altri non si fermano: la pausa blocca solo i comandi. Il grimorio aperto non ferma il tempo (blocca solo
            // chi legge)
            Time.timeScale = menuUp && !Coop.Active ? 0f : 1f;
            backdrop.gameObject.SetActive(menuUp);
            if (Seats.Split) Seats.SetHudsVisible(!menuUp);         // schermo condiviso: gli HUD di tutti
            else
            {
                var hud = GameManager.I != null && GameManager.I.hud != null ? GameManager.I.hud.GetComponent<Canvas>() : null;
                if (hud != null) hud.enabled = !menuUp;
            }
            bool alive = GameManager.I == null || GameManager.I.player == null || GameManager.I.player.alive;
            if (!Blocked && alive) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        // ---------- suggerimenti e suoni ----------

        int showFrame = -1;
        MenuFeedback lastTicked;
        float lastTickedAt = -9f;

        /// <summary>Una voce del menu e' stata scelta (con la tastiera, il controller o un clic) o il puntatore ci e' arrivato sopra: sulle
        /// impostazioni il suggerimento sotto l'elenco dice cosa fa, e la voce fa il suo tic (una volta sola per voce se i due arrivano insieme).</summary>
        internal void Feedback(MenuFeedback feedback, bool selected)
        {
            if (feedback == null) return;
            if (settingsHint != null && settingsPanel != null && feedback.transform.IsChildOf(settingsPanel)) settingsHint.text = feedback.Hint ?? "";
            if (feedback.quiet || Time.frameCount == showFrame) return;
            if (feedback == lastTicked && Time.unscaledTime - lastTickedAt < 0.6f) return;
            lastTicked = feedback; lastTickedAt = Time.unscaledTime;
            MenuSound.Tick();
        }

        // ---------- costruzione ----------

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            PadMenuInput.Install();         // EventSystem con l'input dei menu: tastiera, mouse e controller (croce, A, B)

            backdrop = Stretch(transform as RectTransform, "Velo");
            var veil = backdrop.gameObject.AddComponent<Image>();
            veil.color = new Color(0.018f, 0.035f, 0.039f, 0.98f);

            BuildAtmosphere();
            BuildMainMenu();
            Debug.Log("[PryingEyes] " + VersionText);       // nel log del giocatore: dice quale versione e' stata costruita

            pausePanel = Stretch(backdrop, "Pausa");
            Label(pausePanel, "Titolo", new Vector2(0, 198), new Vector2(1000, 80), 60, TextAnchor.MiddleCenter, "In pausa", Ink, FontStyle.Normal);
            pauseStats = Label(pausePanel, "Riepilogo", new Vector2(0, 144), new Vector2(1000, 26), 17, TextAnchor.MiddleCenter, "", Brass, FontStyle.Normal);
            pauseHint = Label(pausePanel, "Sottotitolo", new Vector2(0, 104), new Vector2(1000, 34), 22, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            MakeButton(pausePanel, "Riprendi", new Vector2(0, 10), Resume);
            MakeButton(pausePanel, "Impostazioni", new Vector2(0, -60), () => OpenSettings(pausePanel));
            MakeButton(pausePanel, "Come si gioca", new Vector2(0, -130), () => OpenHelp(pausePanel));
            MakeButton(pausePanel, "Torna al menu", new Vector2(0, -200), AskLeave);
            MakeButton(pausePanel, "Esci dal gioco", new Vector2(0, -270), Quit);
            // la campanella del ritorno (il Campanile della cattedrale): c'e' solo nelle catacombe, una volta per spedizione
            bellButton = MakeButton(pausePanel, "Suona la campanella del ritorno", new Vector2(0, -332), RingBell);
            bellButton.gameObject.SetActive(false);
            pauseNet = Label(pausePanel, "Rete", new Vector2(0, 66), new Vector2(1100, 36), 16, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);

            leavePanel = Stretch(backdrop, "LasciaPartita");
            Label(leavePanel, "Titolo", new Vector2(0, 150), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Tornare al menu?", Ink, FontStyle.Normal);
            leaveNote = Label(leavePanel, "Conseguenze", new Vector2(0, 45), new Vector2(900, 110), 22, TextAnchor.MiddleCenter, "", Dim, FontStyle.Normal);
            MakeButton(leavePanel, "Annulla", new Vector2(0, -60), () => Show(pausePanel));
            MakeButton(leavePanel, "Lascia la partita", new Vector2(0, -130), () => GameManager.I?.ReturnToMenu());

            coopPanel = Stretch(backdrop, "Cooperativa");
            Label(coopPanel, "Titolo", new Vector2(0, 200), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Cooperativa", Ink, FontStyle.Normal);
            Label(coopPanel, "Sottotitolo", new Vector2(0, 148), new Vector2(1000, 30), 20, TextAnchor.MiddleCenter, "Fino a quattro giocatori nello stesso albergo. L'ospite fa girare il mondo.", Dim, FontStyle.Italic);
            Label(coopPanel, "EtichettaNome", new Vector2(-250, 98), new Vector2(200, 28), 20, TextAnchor.MiddleLeft, "Il tuo nome", Dim, FontStyle.Normal);
            coopName = MakeInput(coopPanel, new Vector2(60, 98), new Vector2(340, 40), "Giocatore");
            Label(coopPanel, "EtichettaIndirizzo", new Vector2(-250, 44), new Vector2(200, 28), 20, TextAnchor.MiddleLeft, "Indirizzo", Dim, FontStyle.Normal);
            coopAddr = MakeInput(coopPanel, new Vector2(60, 44), new Vector2(340, 40), "192.168.1.10");
            hostButton = MakeButton(coopPanel, "Ospita una partita", new Vector2(0, -34), CoopHost);
            joinButton = MakeButton(coopPanel, "Unisciti all'indirizzo", new Vector2(0, -100), CoopJoin);
            coopStatus = Label(coopPanel, "Stato", new Vector2(0, -156), new Vector2(1100, 30), 20, TextAnchor.MiddleCenter, "", Ink, FontStyle.Italic);
            Label(coopPanel, "Nota", new Vector2(0, -190), new Vector2(1100, 26), 16, TextAnchor.MiddleCenter,
                "Chi ospita deve aprire la porta UDP 7807 sul router. Nella stessa rete basta l'indirizzo locale.", Dim, FontStyle.Italic);
            MakeButton(coopPanel, "Indietro", new Vector2(0, -250), CoopBack);

            BuildSplit();
            BuildSettings();
            BuildHelp();
            BuildPadUi();
            // ogni schermata entra con una dissolvenza breve (MenuPanelFade)
            foreach (var panel in new[] { mainPanel, pausePanel, settingsPanel, coopPanel, helpPanel, leavePanel, splitPanel })
                panel.gameObject.AddComponent<MenuPanelFade>();
        }

        static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        static void Rule(Transform parent, float y)
        {
            var line = Rect(parent, "Ottone", new Vector2(500, 1), new Vector2(0, y)).gameObject.AddComponent<Image>();
            line.color = new Color(0.64f, 0.49f, 0.28f, 0.55f); line.raycastTarget = false;
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            return rt;
        }

        static Text Label(Transform parent, string name, Vector2 pos, Vector2 size, int fs, TextAnchor a, string s, Color col, FontStyle style)
        {
            var rt = Rect(parent, name, size, pos);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = fs >= 32 || style == FontStyle.Italic ? HUD.Serif() : MenuUtility;
            t.fontSize = fs; t.alignment = a; t.text = s; t.color = col; t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        /// <summary>Campo di testo a riga singola nello stile del menu.</summary>
        static InputField MakeInput(Transform parent, Vector2 pos, Vector2 size, string placeholder)
        {
            var rt = Rect(parent, "Campo " + placeholder, size, pos);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.03f, 0.025f, 0.025f, 0.92f);
            var input = rt.gameObject.AddComponent<InputField>();
            var text = Label(rt, "Testo", Vector2.zero, size - new Vector2(24f, 4f), 22, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
            text.supportRichText = false;
            var ph = Label(rt, "Segnaposto", Vector2.zero, size - new Vector2(24f, 4f), 22, TextAnchor.MiddleLeft, placeholder, new Color(Dim.r, Dim.g, Dim.b, 0.45f), FontStyle.Italic);
            input.targetGraphic = img;
            input.textComponent = text;
            input.placeholder = ph;
            input.characterLimit = 40;
            input.shouldActivateOnSelect = false;       // arrivarci con la tastiera o il controller non apre la scrittura (e non intrappola la navigazione): la apre Invio, A o un clic
            return input;
        }

        static Button MakeButton(Transform parent, string label, Vector2 pos, UnityAction click)
        {
            var rt = Rect(parent, "Pulsante " + label, new Vector2(340, 54), pos);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = BtnColor;
            var edge = rt.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.50f);
            edge.effectDistance = new Vector2(1f, -1f);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.colors = ButtonColors();
            b.onClick.AddListener(click);
            bool goesBack = label == "Indietro" || label == "Annulla";
            b.onClick.AddListener(() => { if (goesBack) MenuSound.Back(); else MenuSound.Confirm(); });
            Label(rt, "Testo", Vector2.zero, new Vector2(340, 54), 24, TextAnchor.MiddleCenter, label, Ink, FontStyle.Normal);
            return b;
        }

        /// <summary>Cambia la misura di un pulsante gia' costruito (e del suo testo, che lo segue).</summary>
        static void ResizeButton(Button button, Vector2 size, int fontSize)
        {
            button.GetComponent<RectTransform>().sizeDelta = size;
            var text = button.GetComponentInChildren<Text>();
            if (text == null) return;
            text.rectTransform.sizeDelta = size;
            text.fontSize = fontSize;
        }

        /// <summary>Cursore orizzontale in stile hotel: binario scuro, riempimento ottone, maniglia avorio.</summary>
        static Slider MakeSlider(Transform parent, Vector2 pos, float init, float min, float max, float width = 420f)
        {
            var root = Rect(parent, "Cursore", new Vector2(width, 32), pos);
            var track = root.gameObject.AddComponent<Image>();
            track.color = new Color(0f, 0f, 0f, 0.6f);

            var fillArea = Stretch(root, "Riempimento");
            fillArea.offsetMin = new Vector2(5, 14); fillArea.offsetMax = new Vector2(-5, -14);
            var fill = Stretch(fillArea, "Fill");
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = new Color(0.78f, 0.62f, 0.36f);

            var handleArea = Stretch(root, "Area maniglia");
            handleArea.offsetMin = new Vector2(10, 0); handleArea.offsetMax = new Vector2(-10, 0);
            var handle = Rect(handleArea, "Maniglia", new Vector2(12, 0), Vector2.zero);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = Ink;

            var sl = root.gameObject.AddComponent<Slider>();
            sl.targetGraphic = handleImg;
            sl.fillRect = fill;
            sl.handleRect = handle;
            sl.minValue = min; sl.maxValue = max;
            sl.SetValueWithoutNotify(init);
            // sinistra e destra regolano il cursore (con la navigazione automatica saltavano a quello dell'altra colonna); su e giu' lo lasciano
            sl.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            var cb = sl.colors;
            cb.highlightedColor = cb.selectedColor = new Color(1f, 0.80f, 0.42f, 1f);      // la maniglia diventa ottone quando e' scelto
            sl.colors = cb;
            return sl;
        }
    }
}
