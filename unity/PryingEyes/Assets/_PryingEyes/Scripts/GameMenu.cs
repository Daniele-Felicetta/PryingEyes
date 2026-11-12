using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Menu principale, pausa (Esc) e impostazioni audio, costruiti a runtime come l'HUD.
    /// All'avvio il gioco resta fermo sul menu; "Gioca" fa partire la prima ondata. In pausa il tempo si ferma,
    /// l'HUD si nasconde e il mouse torna libero. Blocked dice agli altri script di non prendere input.</summary>
    public class GameMenu : MonoBehaviour
    {
        public static GameMenu I;
        public static bool Blocked { get; private set; }
        public static bool InMenu { get; private set; }
        public static bool Paused { get; private set; }

        static readonly Color Ink = new Color(0.93f, 0.89f, 0.80f);
        static readonly Color Dim = new Color(0.74f, 0.67f, 0.56f, 0.92f);
        static readonly Color BtnColor = new Color(0.18f, 0.135f, 0.085f, 0.95f);

        RectTransform backdrop, mainPanel, pausePanel, settingsPanel, settingsFrom, coopPanel, helpPanel, helpFrom, leavePanel;
        Slider sMaster, sEffects, sMusic, sBrightness, sSensitivity, sFov, sMotion;
        Text vMaster, vEffects, vMusic, vBrightness, vSensitivity, vFov, vMotion, coopStatus, pauseNet, pauseHint;
        InputField coopName, coopAddr;
        Button hostButton, joinButton;
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
            Show(mainPanel);
            Refresh();
        }

        void Start()
        {
            Refresh();
            // prova senza mouse: -autoplay preme "Gioca" da solo (per far girare tutto il percorso del giocatore vero, non solo quello del bot)
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-autoplay") { Play(); break; }
        }

        void Update()
        {
            if (Blocked) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (coopPanel.gameObject.activeSelf && coopStatus != null)
            {
                coopStatus.text = Coop.Available ? Coop.Status : "La cooperativa non è disponibile in questa versione.";
                hostButton.interactable = joinButton.interactable = Coop.Available && !Coop.Active;
            }
            if (!Input.GetKeyDown(KeyCode.Escape) && !TouchControls.Down(TouchControls.Action.Pause)) return;
            if (settingsPanel.gameObject.activeSelf) { Back(); return; }
            if (leavePanel.gameObject.activeSelf) { Show(pausePanel); return; }
            if (helpPanel.gameObject.activeSelf) { Show(helpFrom); return; }
            if (coopPanel.gameObject.activeSelf) { CoopBack(); return; }
            if (InMenu) return;
            if (Paused) Resume(); else Pause();
        }

        // ---------- stato ----------

        void OnApplicationFocus(bool focused)
        {
            if (!focused && !GameManager.autoTest) Pause();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && !GameManager.autoTest) Pause();
        }

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            Blocked = InMenu = Paused = false;
            Time.timeScale = 1f;
        }

        void OpenHelp(RectTransform from) { helpFrom = from; Show(helpPanel); }

        void AskLeave()
        {
            leaveNote.text = "La partita attuale verrà chiusa. Progressi, armi e punti di questa discesa andranno persi." +
                (Coop.IsHost ? "\nI compagni verranno scollegati e potranno proseguire da soli." : "");
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
        static string NetInfo(int maxAddr = 4)
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

        void Pause()
        {
            if (InMenu || Paused) return;
            Paused = true;
            if (pauseNet != null) pauseNet.text = NetInfo();
            if (pauseHint != null) pauseHint.text = Coop.Active ? "Gli altri continuano a giocare. Esc per riprendere." : "La partita è sospesa. Esc per riprendere.";
            Show(pausePanel);
            Refresh();
        }

        void Resume()
        {
            Paused = false;
            Show(null);
            Refresh();
        }

        void OpenSettings(RectTransform from)
        {
            settingsFrom = from;
            SyncSliders();
            Show(settingsPanel);
        }

        void Back()
        {
            PlayerPrefs.Save();
            Show(settingsFrom);
        }

        static void Quit() => Application.Quit();

        void Show(RectTransform p)
        {
            mainPanel.gameObject.SetActive(p == mainPanel);
            pausePanel.gameObject.SetActive(p == pausePanel);
            settingsPanel.gameObject.SetActive(p == settingsPanel);
            coopPanel.gameObject.SetActive(p == coopPanel);
            helpPanel.gameObject.SetActive(p == helpPanel);
            leavePanel.gameObject.SetActive(p == leavePanel);
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                var first = p != null ? p.GetComponentInChildren<Button>() : null;
                if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
            }
        }

        void Refresh()
        {
            Blocked = InMenu || Paused;
            Time.timeScale = Blocked && !Coop.Active ? 0f : 1f;     // in cooperativa gli altri non si fermano: la pausa blocca solo i comandi
            backdrop.gameObject.SetActive(Blocked);
            var hud = GameManager.I != null && GameManager.I.hud != null ? GameManager.I.hud.GetComponent<Canvas>() : null;
            if (hud != null) hud.enabled = !Blocked;
            bool alive = GameManager.I == null || GameManager.I.player == null || GameManager.I.player.alive;
            if (!Blocked && alive) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void SyncSliders()
        {
            SetSlider(sMaster, vMaster, GameSettings.Master);
            SetSlider(sEffects, vEffects, GameSettings.Effects);
            SetSlider(sMusic, vMusic, GameSettings.Music);
            SetSlider(sBrightness, vBrightness, GameSettings.Brightness);
            SetSlider(sSensitivity, vSensitivity, GameSettings.Sensitivity, SensText);
            SetSlider(sFov, vFov, GameSettings.Fov, FovText);
            SetSlider(sMotion, vMotion, GameSettings.Motion);
        }

        static void SetSlider(Slider s, Text v, float x, System.Func<float, string> format = null)
        {
            if (s == null) return;
            s.SetValueWithoutNotify(x);
            v.text = (format ?? Pct)(x);
        }

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
        static string SensText(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";
        static string FovText(float v) => Mathf.RoundToInt(v) + "°";

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
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            backdrop = Stretch(transform as RectTransform, "Velo");
            var veil = backdrop.gameObject.AddComponent<Image>();
            veil.color = new Color(0.025f, 0.032f, 0.04f, 0.90f);

            mainPanel = Stretch(backdrop, "Principale");
            Label(mainPanel, "Titolo", new Vector2(0, 220), new Vector2(1000, 90), 76, TextAnchor.MiddleCenter, "PRYING EYES", Ink, FontStyle.Normal);
            Label(mainPanel, "Sottotitolo", new Vector2(0, 150), new Vector2(1000, 34), 24, TextAnchor.MiddleCenter, "Il Grand Hotel non chiude mai.", Dim, FontStyle.Italic);
            Rule(mainPanel, 110);
            Rule(mainPanel, -210);
            MakeButton(mainPanel, "Gioca", new Vector2(0, 70), Play);
            MakeButton(mainPanel, "Cooperativa", new Vector2(0, 10), OpenCoop);
            MakeButton(mainPanel, "Come si gioca", new Vector2(0, -50), () => OpenHelp(mainPanel));
            MakeButton(mainPanel, "Impostazioni", new Vector2(0, -110), () => OpenSettings(mainPanel));
            MakeButton(mainPanel, "Esci", new Vector2(0, -170), Quit);
            // tre righe corte (una sola, lunga, usciva dallo schermo): movimento, armi, furtivo
            Label(mainPanel, "Comandi", new Vector2(0, -236), new Vector2(1200, 26), 17, TextAnchor.MiddleCenter,
                "WASD muovi · Shift corri · C accovacciati · Spazio salta · Q/E sporgi · F usa e compra · Esc pausa", Dim, FontStyle.Italic);
            Label(mainPanel, "ComandiArmi", new Vector2(0, -262), new Vector2(1200, 26), 17, TextAnchor.MiddleCenter,
                "mouse mira · click sinistro spara · tasto destro mira · R ricarica · B modo di fuoco · 1/2 o rotellina cambia arma", Dim, FontStyle.Italic);
            Label(mainPanel, "ComandiFurtivo", new Vector2(0, -288), new Vector2(1200, 26), 17, TextAnchor.MiddleCenter,
                "G coltello (alle spalle) · T torcia · Y cambia pila · V ping · M mappa · H il Velo (col libro)", Dim, FontStyle.Italic);

            pausePanel = Stretch(backdrop, "Pausa");
            Label(pausePanel, "Titolo", new Vector2(0, 170), new Vector2(1000, 80), 60, TextAnchor.MiddleCenter, "In pausa", Ink, FontStyle.Normal);
            pauseHint = Label(pausePanel, "Sottotitolo", new Vector2(0, 100), new Vector2(1000, 34), 22, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            MakeButton(pausePanel, "Riprendi", new Vector2(0, 10), Resume);
            MakeButton(pausePanel, "Impostazioni", new Vector2(0, -60), () => OpenSettings(pausePanel));
            MakeButton(pausePanel, "Come si gioca", new Vector2(0, -130), () => OpenHelp(pausePanel));
            MakeButton(pausePanel, "Torna al menu", new Vector2(0, -200), AskLeave);
            MakeButton(pausePanel, "Esci dal gioco", new Vector2(0, -270), Quit);
            pauseNet = Label(pausePanel, "Rete", new Vector2(0, 62), new Vector2(1100, 36), 16, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);

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

            settingsPanel = Stretch(backdrop, "Impostazioni");
            Label(settingsPanel, "Titolo", new Vector2(0, 200), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Impostazioni", Ink, FontStyle.Normal);
            var view = Rect(settingsPanel, "Visuale", new Vector2(460, 420), new Vector2(-285, 0));
            var audio = Rect(settingsPanel, "Audio", new Vector2(460, 420), new Vector2(285, 0));
            sBrightness = Row(view, 112, "Luminosità", GameSettings.Brightness, GameSettings.SetBrightness, out vBrightness);
            sFov = Row(view, 30, "Campo visivo", GameSettings.Fov, GameSettings.SetFov, out vFov, 60f, 105f, FovText);
            sSensitivity = Row(view, -52, "Sensibilità mira", GameSettings.Sensitivity, GameSettings.SetSensitivity, out vSensitivity, 0.25f, 3f, SensText);
            sMotion = Row(view, -134, "Oscillazione visuale", GameSettings.Motion, GameSettings.SetMotion, out vMotion);
            sMaster = Row(audio, 112, "Volume generale", GameSettings.Master, GameSettings.SetMaster, out vMaster);
            sEffects = Row(audio, 30, "Effetti sonori", GameSettings.Effects, GameSettings.SetEffects, out vEffects);
            sMusic = Row(audio, -52, "Musica", GameSettings.Music, GameSettings.SetMusic, out vMusic);
            Label(audio, "Comfort", new Vector2(0, -134), new Vector2(440, 68), 18, TextAnchor.MiddleLeft,
                "Riduci l'oscillazione per una visuale più stabile. Le modifiche si applicano subito.", Dim, FontStyle.Italic);
            MakeButton(settingsPanel, "Indietro", new Vector2(0, -240), Back);

            helpPanel = Stretch(backdrop, "Guida");
            Label(helpPanel, "Titolo", new Vector2(0, 235), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "La Torre", Ink, FontStyle.Normal);
            Label(helpPanel, "Obiettivo", new Vector2(0, 150), new Vector2(1000, 60), 23, TextAnchor.MiddleCenter,
                "Accendi tutte le candele del piano. Raggiungi l'ascensore aperto e scendi.", Ink, FontStyle.Normal);
            Label(helpPanel, "Furtivo", new Vector2(0, 60), new Vector2(1000, 76), 21, TextAnchor.MiddleLeft,
                "MUOVITI NELL'OMBRA\nTorcia, corsa e spari attirano le presenze. Accovacciati e usa i ripari. Il coltello alle spalle è silenzioso.", Dim, FontStyle.Normal);
            Label(helpPanel, "Round", new Vector2(0, -35), new Vector2(1000, 76), 21, TextAnchor.MiddleLeft,
                "PREPARATI PRIMA DI ACCENDERE\nAlcune candele abbassano le serrande: sopravvivi al round per riaprirle. Fai scorta di munizioni prima di accenderle.", Dim, FontStyle.Normal);
            Label(helpPanel, "Risorse", new Vector2(0, -130), new Vector2(1000, 76), 21, TextAnchor.MiddleLeft,
                "ESPLORA E SCEGLI\nCompra armi ai muri, cerca pile e libri nei santuari. Se muori perdi metà dei punti e l'ultimo perk acquistato.", Dim, FontStyle.Normal);
            MakeButton(helpPanel, "Indietro", new Vector2(0, -240), () => Show(helpFrom));
        }

        Slider Row(Transform parent, float y, string name, float init, UnityAction<float> set, out Text value,
            float min = 0f, float max = 1f, System.Func<float, string> format = null)
        {
            format = format ?? Pct;
            Label(parent, name, new Vector2(-50, y + 36), new Vector2(320, 32), 22, TextAnchor.MiddleLeft, name, Ink, FontStyle.Normal);
            var vt = Label(parent, name + " valore", new Vector2(160, y + 36), new Vector2(100, 32), 22, TextAnchor.MiddleRight, format(init), Ink, FontStyle.Normal);
            value = vt;
            var sl = MakeSlider(parent, new Vector2(0, y), init, min, max);
            sl.onValueChanged.AddListener(v => { set(v); vt.text = format(v); });
            return sl;
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
            t.font = HUD.Serif();
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
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.6f, 1.35f, 1.0f, 1f);
            cb.pressedColor = new Color(0.7f, 0.6f, 0.5f, 1f);
            cb.selectedColor = new Color(1.5f, 1.3f, 1.0f, 1f);
            b.colors = cb;
            b.onClick.AddListener(click);
            Label(rt, "Testo", Vector2.zero, new Vector2(340, 54), 24, TextAnchor.MiddleCenter, label, Ink, FontStyle.Normal);
            return b;
        }

        /// <summary>Cursore orizzontale in stile hotel: binario scuro, riempimento ottone, maniglia avorio.</summary>
        static Slider MakeSlider(Transform parent, Vector2 pos, float init, float min, float max)
        {
            var root = Rect(parent, "Cursore", new Vector2(420, 32), pos);
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
            return sl;
        }
    }
}
