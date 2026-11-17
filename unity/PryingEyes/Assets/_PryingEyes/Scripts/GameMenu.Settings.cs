using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Le impostazioni (0.8.0): cinque schede (Schermo, Immagine, Audio, Comandi, Gioco), ognuna un elenco di righe con l'etichetta a sinistra
    /// e il comando a destra, un suggerimento che spiega la riga scelta e un "Ripristina scheda". Ogni riga e' un cursore (Slider) o una scelta a
    /// valori in fila (MenuChoice); sinistra e destra le regolano, su e giu' passano alla riga vicina, e sulle schede cambiano scheda. Le righe
    /// salvano subito (PlayerPrefs): "Indietro" non chiede niente. Tranne lo schermo: quello si prova e si conferma (GameMenu.Display.cs).</summary>
    public partial class GameMenu
    {
        // l'elenco di una scheda, in unita' del canvas di riferimento (1280x720): etichetta, comando, una riga ogni 42
        const float RowTop = 112f, RowStep = 42f, LabelX = -275f, LabelWidth = 330f, ControlX = 170f, ControlWidth = 520f, SliderWidth = 380f;
        static float RowY(int row) => RowTop - row * RowStep;

        sealed class SettingsPage
        {
            public string name, hint;
            public RectTransform root;
            public Button tab;
            public Image underline;
            public readonly List<Selectable> rows = new List<Selectable>();
            public readonly List<Action> refresh = new List<Action>();
            public Action reset;           // null: la scheda non ha valori da ripristinare (lo schermo si conferma a parte)
            public int next;               // la prossima riga libera
        }

        static readonly string[] PageNames = { "Schermo", "Immagine", "Audio", "Comandi", "Gioco" };
        static readonly string[] PageHints =
        {
            "Modalità, formato, risoluzione e frequenza dello schermo. Sinistra e destra cambiano scheda, giù entra nell'elenco.",
            "Luminosità, ombre, nitidezza e gli effetti della pellicola. Sinistra e destra cambiano scheda, giù entra nell'elenco.",
            "Volumi, tema del menu e suoni dell'interfaccia. Sinistra e destra cambiano scheda, giù entra nell'elenco.",
            "Sensibilità, assi invertiti e comandi a interruttore. Sinistra e destra cambiano scheda, giù entra nell'elenco.",
            "Prospettiva, mirino, effetti della visuale, HUD dei compagni, contatore dei fotogrammi e pausa. Sinistra e destra cambiano scheda, giù entra nell'elenco.",
        };
        static readonly string[] ShadowNames = { "Bassa", "Media", "Alta" };
        static readonly string[] CrosshairNames = { "Classico", "Solo il punto", "Nessuno" };

        readonly List<SettingsPage> pages = new List<SettingsPage>();
        SettingsPage page;                  // la scheda aperta
        Text settingsHint;
        Button settingsBackButton, resetButton, controlsListButton;
        Slider sAmbience, sMenuMusic, sPadSens, sGrain, sVignette, sBloom;
        Text vAmbience, vMenuMusic, vPadSens, vGrain, vVignette, vBloom;

        void BuildSettings()
        {
            settingsPanel = Stretch(backdrop, "Impostazioni");
            Label(settingsPanel, "Titolo", new Vector2(-330, 246), new Vector2(440, 62), 43, TextAnchor.MiddleLeft, "Il vostro soggiorno", Ink, FontStyle.Normal);
            Label(settingsPanel, "Sottotitolo", new Vector2(330, 241), new Vector2(440, 42), 17, TextAnchor.MiddleRight,
                "IMPOSTAZIONI  /  REGISTRO PERSONALE", Dim, FontStyle.Normal);
            for (int i = 0; i < PageNames.Length; i++)
            {
                var p = new SettingsPage { name = PageNames[i], hint = PageHints[i] };
                p.root = Stretch(settingsPanel, "Scheda " + p.name);
                p.tab = MakeButton(settingsPanel, p.name, new Vector2(-408 + i * 204, 176), () => SelectPage(p));
                ResizeButton(p.tab, new Vector2(196, 40), 21);
                var feedback = AddFeedback(p.tab, p.hint);
                feedback.selected = () => SelectPage(p);        // con la tastiera o il controller basta posarsi sulla scheda per aprirla
                p.underline = Plate(p.tab.transform, "Sottolineatura", new Vector2(0, -17), new Vector2(176, 3), Brass);
                pages.Add(p);
            }
            Plate(settingsPanel, "Filetto", new Vector2(0, 148), new Vector2(1010, 1), new Color(0.69f, 0.53f, 0.30f, 0.45f));
            BuildDisplayPage(pages[0]);
            BuildImagePage(pages[1]);
            BuildAudioPage(pages[2]);
            BuildControlsPage(pages[3]);
            BuildGamePage(pages[4]);
            settingsHint = Label(settingsPanel, "Suggerimento", new Vector2(0, -224), new Vector2(1000, 44), 18, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            settingsBackButton = MakeButton(settingsPanel, "Indietro", new Vector2(-190, -266), Back);
            resetButton = MakeButton(settingsPanel, "Ripristina scheda", new Vector2(190, -266), ResetPage);
            foreach (var b in new[] { settingsBackButton, resetButton }) ResizeButton(b, new Vector2(340, 44), 22);
            AddFeedback(settingsBackButton, "Torna indietro: le scelte sono già salvate.");
            AddFeedback(resetButton, "Riporta ai valori iniziali le voci di questa scheda.");
            BuildDisplayConfirmation();
            SelectPage(pages[0]);
        }

        // ---------- le schede ----------

        void BuildImagePage(SettingsPage p)
        {
            sBrightness = AddSlider(p, "Luminosità", "Regola l'esposizione dell'immagine: più alta, ombre meno cupe.",
                GameSettings.Brightness, 0f, 1f, GameSettings.SetBrightness, Pct, out vBrightness);
            sFov = AddSlider(p, "Campo visivo", "Quanto si vede ai lati. Più ampio mostra di più, ma allarga i bordi.",
                GameSettings.Fov, 60f, 105f, GameSettings.SetFov, FovText, out vFov);
            AddChoice(p, "Qualità delle ombre", "Ombre meno nitide e meno lontane alleggeriscono molto la scheda video.",
                () => ShadowNames[GameSettings.ShadowQuality],
                d => GameSettings.SetShadowQuality(Wrap(GameSettings.ShadowQuality + d, ShadowNames.Length)));
            AddChoice(p, "Risoluzione di rendering", "Disegna il gioco più piccolo e lo ingrandisce: più fotogrammi, meno nitidezza.",
                () => GameSettings.RenderScalePercent + (GameSettings.RenderScalePercent == 100 ? "%  (nativa)" : "%"),
                d => GameSettings.SetRenderScale(GameSettings.RenderScales[Wrap(Array.IndexOf(GameSettings.RenderScales, GameSettings.RenderScalePercent) + d, GameSettings.RenderScales.Length)]));
            AddChoice(p, "Anti-aliasing", "Smussa i bordi degli oggetti. Costa un po' di scheda video.",
                () => GameSettings.Msaa <= 1 ? "Disattivato" : "MSAA " + GameSettings.Msaa + "×",
                d => GameSettings.SetMsaa(GameSettings.MsaaSamples[Wrap(Array.IndexOf(GameSettings.MsaaSamples, GameSettings.Msaa) + d, GameSettings.MsaaSamples.Length)]));
            sGrain = AddSlider(p, "Grana della pellicola", "Il rumore dell'immagine che la rende granulosa. A zero sparisce.",
                GameSettings.GrainAmount, 0f, 1f, GameSettings.SetGrain, Pct, out vGrain);
            sVignette = AddSlider(p, "Vignettatura", "L'oscuramento dei bordi dello schermo.",
                GameSettings.VignetteAmount, 0f, 1f, GameSettings.SetVignette, Pct, out vVignette);
            sBloom = AddSlider(p, "Bagliore", "L'alone che circonda le luci forti.",
                GameSettings.BloomAmount, 0f, 1f, GameSettings.SetBloom, Pct, out vBloom);
            p.reset = GameSettings.ResetImage;
        }

        void BuildAudioPage(SettingsPage p)
        {
            sMaster = AddSlider(p, "Volume generale", "Il volume di tutto il gioco.",
                GameSettings.Master, 0f, 1f, GameSettings.SetMaster, Pct, out vMaster, MenuSound.Preview.Master);
            sEffects = AddSlider(p, "Effetti sonori", "Spari, passi, nemici, porte e rumori della stanza.",
                GameSettings.Effects, 0f, 1f, GameSettings.SetEffects, Pct, out vEffects, MenuSound.Preview.Effects);
            sMusic = AddSlider(p, "Musica", "Il valzer del grammofono e la musica delle ondate.",
                GameSettings.Music, 0f, 1f, GameSettings.SetMusic, Pct, out vMusic);
            sAmbience = AddSlider(p, "Ambiente", "Il ronzio dell'albergo, il vento, i brusii, gli scricchiolii e i rintocchi lontani.",
                GameSettings.Ambience, 0f, 1f, GameSettings.SetAmbience, Pct, out vAmbience, MenuSound.Preview.Ambience);
            sMenuMusic = AddSlider(p, "Tema del menu", "La musica del menu principale. Sta sotto il volume della Musica.",
                GameSettings.MenuMusic, 0f, 1f, GameSettings.SetMenuMusic, Pct, out vMenuMusic);
            AddToggle(p, "Suoni dell'interfaccia", "I toni dei pulsanti, delle scelte e dei cursori del menu.",
                () => GameSettings.UiSounds, GameSettings.SetUiSounds, "Disattivati", "Attivi");
            p.reset = GameSettings.ResetAudio;
        }

        void BuildControlsPage(SettingsPage p)
        {
            sSensitivity = AddSlider(p, "Sensibilità mira", "La velocità con cui il mouse gira la visuale.",
                GameSettings.Sensitivity, 0.25f, 3f, GameSettings.SetSensitivity, SensText, out vSensitivity);
            sPadSens = AddSlider(p, "Sensibilità controller", "La velocità con cui lo stick destro gira la visuale.",
                GameSettings.PadSensitivity, GameSettings.PadSensitivityMin, GameSettings.PadSensitivityMax, GameSettings.SetPadSensitivity, SensText, out vPadSens);
            AddToggle(p, "Asse Y del mouse", "Normale: il mouse in avanti guarda in alto. Invertito: guarda in basso.",
                () => GameSettings.InvertMouseY, GameSettings.SetInvertMouseY, "Normale", "Invertito");
            AddToggle(p, "Asse Y del controller", "Normale: lo stick in avanti guarda in alto. Invertito: guarda in basso.",
                () => GameSettings.InvertPadY, GameSettings.SetInvertPadY, "Normale", "Invertito");
            AddToggle(p, "Mira", "Tasto destro del mouse: tenuto premuto per mirare, oppure un tocco accende e spegne la mira.",
                () => GameSettings.AimToggle, on => GameSettings.SetAimMode(on ? GameSettings.ModeToggle : GameSettings.ModeHold), "Tieni premuto", "Interruttore");
            AddToggle(p, "Abbassarsi", "C o Ctrl: tenuto premuto, oppure un tocco ti abbassa e uno ti rialza (anche saltando).",
                () => GameSettings.CrouchToggle, on => GameSettings.SetCrouchMode(on ? GameSettings.ModeToggle : GameSettings.ModeHold), "Tieni premuto", "Interruttore");
            AddToggle(p, "Corsa", "Maiusc: tenuto premuto, oppure un tocco avvia la corsa, che si spegne se ti fermi, prendi la mira o spari.",
                () => GameSettings.SprintToggle, on => GameSettings.SetSprintMode(on ? GameSettings.ModeToggle : GameSettings.ModeHold), "Tieni premuto", "Interruttore");
            controlsListButton = AddButtonRow(p, "Elenco dei comandi", "Tutti i tasti della tastiera e del controller.", () => OpenHelp(settingsPanel, true));
            p.reset = GameSettings.ResetControls;
        }

        void BuildGamePage(SettingsPage p)
        {
            // 0.8.0: prima o terza persona; la visuale cambia subito, anche in partita (docs/game/third-person.md)
            AddToggle(p, "Prospettiva", "Prima persona: dagli occhi del personaggio. Terza persona: camera dietro la spalla, lo vedi per intero.",
                () => GameSettings.ThirdPerson, GameSettings.SetThirdPerson, "Prima persona", "Terza persona");
            AddChoice(p, "Mirino", "Il segno al centro dello schermo.",
                () => CrosshairNames[GameSettings.Crosshair], d => GameSettings.SetCrosshair(Wrap(GameSettings.Crosshair + d, CrosshairNames.Length)));
            sMotion = AddSlider(p, "Oscillazione visuale", "Il dondolio dell'immagine quando cammini e il movimento del menu. A zero tutto resta fermo.",
                GameSettings.Motion, 0f, 1f, GameSettings.SetMotion, Pct, out vMotion);
            // 0.7.2: il pannello dei compagni (cooperativa e schermo condiviso) si ingrandisce o si rimpicciolisce; si vede subito
            sCoopHud = AddSlider(p, "HUD dei compagni", "La dimensione del pannello dei compagni, in cooperativa e nello schermo condiviso.",
                GameSettings.CoopHudScale, GameSettings.CoopHudMin, GameSettings.CoopHudMax, GameSettings.SetCoopHudScale, Pct, out vCoopHud);
            AddToggle(p, "Contatore fotogrammi", "Mostra in alto a sinistra i fotogrammi al secondo e i millisecondi di ogni fotogramma.",
                () => GameSettings.ShowFps, GameSettings.SetShowFps, "Nascosto", "Visibile");
            AddToggle(p, "Pausa a finestra inattiva", "Il gioco si ferma da solo quando passi a un'altra finestra. In cooperativa la pausa blocca solo i tuoi comandi.",
                () => GameSettings.PauseOnFocusLoss, GameSettings.SetPauseOnFocusLoss, "Disattivata", "Attiva");
            p.reset = GameSettings.ResetGame;
        }

        // ---------- le righe ----------

        static ColorBlock ButtonColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(2.0f, 2.3f, 2.0f, 1f);
            colors.pressedColor = new Color(0.7f, 0.6f, 0.5f, 1f);
            colors.selectedColor = new Color(1.7f, 2.0f, 1.8f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);       // spento: piu' scuro, senza il velo che lascerebbe vedere il bordo d'ottone sotto
            return colors;
        }

        static MenuFeedback AddFeedback(Selectable selectable, string hint, bool quiet = false)
        {
            var feedback = selectable.gameObject.AddComponent<MenuFeedback>();
            feedback.hint = hint; feedback.quiet = quiet;
            return feedback;
        }

        /// <summary>Una riga con un cursore: etichetta, cursore e valore. Il cursore scatta ogni volta che si muove (un tono piu' acuto verso il
        /// massimo), e i tre volumi fanno sentire un campione di quello che si sta regolando.</summary>
        Slider AddSlider(SettingsPage p, string title, string hint, float init, float min, float max, Action<float> set, Func<float, string> format,
            out Text value, MenuSound.Preview? preview = null)
        {
            float y = RowY(p.next++);
            Label(p.root, title, new Vector2(LabelX, y), new Vector2(LabelWidth, 30), 20, TextAnchor.MiddleLeft, title, Ink, FontStyle.Normal);
            var slider = MakeSlider(p.root, new Vector2(ControlX - ControlWidth * 0.5f + SliderWidth * 0.5f, y), init, min, max, SliderWidth);
            var text = Label(p.root, title + " valore", new Vector2(ControlX + ControlWidth * 0.5f - 65f, y), new Vector2(120, 30), 20,
                TextAnchor.MiddleLeft, format(init), Ink, FontStyle.Normal);
            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                text.text = format(v);
                if (preview.HasValue) MenuSound.PreviewVolume(preview.Value); else MenuSound.Slide(Mathf.InverseLerp(min, max, v));
            });
            AddFeedback(slider, hint, true);
            p.rows.Add(slider);
            value = text;
            return slider;
        }

        /// <summary>Una riga con una scelta a valori in fila: etichetta e "‹ valore ›". Il valore lo dice <paramref name="text"/>, che si rilegge
        /// dopo ogni passo e quando la scheda si ripristina.</summary>
        MenuChoice AddChoice(SettingsPage p, string title, string hint, Func<string> text, Action<int> step)
        {
            float y = RowY(p.next++);
            var rowLabel = Label(p.root, title, new Vector2(LabelX, y), new Vector2(LabelWidth, 30), 20, TextAnchor.MiddleLeft, title, Ink, FontStyle.Normal);
            var rt = Rect(p.root, "Scelta " + title, new Vector2(ControlWidth, 36), new Vector2(ControlX, y));
            var plate = rt.gameObject.AddComponent<Image>();
            plate.color = BtnColor;
            var edge = rt.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.50f);
            edge.effectDistance = new Vector2(1f, -1f);
            var choice = rt.gameObject.AddComponent<MenuChoice>();
            choice.targetGraphic = plate;
            choice.colors = ButtonColors();
            choice.rowLabel = rowLabel;
            choice.textOn = Ink;
            choice.valueLabel = Label(rt, title + " valore", Vector2.zero, new Vector2(ControlWidth - 100f, 32), 20, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            Label(rt, "Freccia sinistra", new Vector2(-ControlWidth * 0.5f + 26f, 0), new Vector2(40, 32), 26, TextAnchor.MiddleCenter, "‹", Brass, FontStyle.Normal);
            Label(rt, "Freccia destra", new Vector2(ControlWidth * 0.5f - 26f, 0), new Vector2(40, 32), 26, TextAnchor.MiddleCenter, "›", Brass, FontStyle.Normal);
            choice.onStep = d => { step(d); choice.Value = text(); };
            choice.Value = text();
            p.refresh.Add(() => choice.Value = text());
            AddFeedback(choice, hint);
            p.rows.Add(choice);
            return choice;
        }

        /// <summary>Due valori (acceso e spento, normale e invertito...) in una riga a scelta.</summary>
        MenuChoice AddToggle(SettingsPage p, string title, string hint, Func<bool> get, Action<bool> set, string off, string on) =>
            AddChoice(p, title, hint, () => get() ? on : off, d => set(!get()));

        /// <summary>Una riga che e' un pulsante a tutta larghezza del comando (apre un'altra schermata).</summary>
        Button AddButtonRow(SettingsPage p, string title, string hint, UnityEngine.Events.UnityAction click)
        {
            float y = RowY(p.next++);
            Label(p.root, title + " etichetta", new Vector2(LabelX, y), new Vector2(LabelWidth, 30), 20, TextAnchor.MiddleLeft, title, Ink, FontStyle.Normal);
            var button = MakeButton(p.root, title, new Vector2(ControlX, y), click);
            ResizeButton(button, new Vector2(ControlWidth, 36), 20);
            AddFeedback(button, hint);
            p.rows.Add(button);
            return button;
        }

        // ---------- schede, navigazione, ripristino ----------

        void SelectPage(SettingsPage p)
        {
            if (p == null) return;
            page = p;
            foreach (var q in pages)
            {
                bool on = q == p;
                q.root.gameObject.SetActive(on);
                q.tab.GetComponent<Image>().color = on ? new Color(0.26f, 0.21f, 0.13f) : BtnColor;
                q.underline.enabled = on;
            }
            if (resetButton == null) return;                      // la costruzione non e' finita
            resetButton.gameObject.SetActive(p.reset != null);
            settingsBackButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(p.reset != null ? -190f : 0f, -266f);
            RebuildNav();
        }

        /// <summary>Le catene di navigazione della scheda aperta: le schede a sinistra e destra e giu' sul primo comando, le righe una sotto l'altra
        /// (saltando quelle spente), e in fondo "Indietro" e "Ripristina". Esplicite: con la navigazione automatica un cursore e una scelta, che
        /// hanno larghezze diverse, si scavalcavano.</summary>
        void RebuildNav()
        {
            if (page == null || resetButton == null || settingsBackButton == null) return;
            var chain = new List<Selectable>();
            foreach (var row in page.rows) if (row != null && row.IsInteractable()) chain.Add(row);
            Selectable back = settingsBackButton, reset = page.reset != null ? (Selectable)resetButton : null;
            Selectable first = chain.Count > 0 ? chain[0] : back, last = chain.Count > 0 ? chain[chain.Count - 1] : (Selectable)page.tab;
            for (int i = 0; i < pages.Count; i++)
                pages[i].tab.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? pages[i - 1].tab : null,
                    selectOnRight = i < pages.Count - 1 ? pages[i + 1].tab : null,
                    selectOnDown = first,
                };
            for (int i = 0; i < chain.Count; i++)
                chain[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? chain[i - 1] : page.tab,
                    selectOnDown = i < chain.Count - 1 ? chain[i + 1] : back,
                };
            settingsBackButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = last, selectOnRight = reset };
            resetButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = last, selectOnLeft = back };
        }

        /// <summary>PagSu e PagGiu passano da una scheda all'altra, da qualunque riga.</summary>
        void CyclePage(int direction)
        {
            int at = pages.IndexOf(page);
            var next = pages[Wrap(at + direction, pages.Count)];
            SelectPage(next);
            EventSystem.current?.SetSelectedGameObject(next.tab.gameObject);
        }

        void ResetPage()
        {
            if (page == null || page.reset == null) return;
            page.reset();
            SyncSettings();
            MenuSound.Confirm();
            if (settingsHint != null) settingsHint.text = "Fatto: la scheda " + page.name + " è tornata ai valori iniziali.";
        }

        /// <summary>Riallinea cursori, scelte e schermo con i valori in uso (all'apertura delle impostazioni e dopo un ripristino).</summary>
        void SyncSettings()
        {
            SyncSliders();
            foreach (var p in pages) foreach (var refresh in p.refresh) refresh();
            SyncDisplay();
        }

        void SyncSliders()
        {
            SetSlider(sMaster, vMaster, GameSettings.Master);
            SetSlider(sEffects, vEffects, GameSettings.Effects);
            SetSlider(sMusic, vMusic, GameSettings.Music);
            SetSlider(sAmbience, vAmbience, GameSettings.Ambience);
            SetSlider(sMenuMusic, vMenuMusic, GameSettings.MenuMusic);
            SetSlider(sBrightness, vBrightness, GameSettings.Brightness);
            SetSlider(sFov, vFov, GameSettings.Fov, FovText);
            SetSlider(sGrain, vGrain, GameSettings.GrainAmount);
            SetSlider(sVignette, vVignette, GameSettings.VignetteAmount);
            SetSlider(sBloom, vBloom, GameSettings.BloomAmount);
            SetSlider(sSensitivity, vSensitivity, GameSettings.Sensitivity, SensText);
            SetSlider(sPadSens, vPadSens, GameSettings.PadSensitivity, SensText);
            SetSlider(sMotion, vMotion, GameSettings.Motion);
            SetSlider(sCoopHud, vCoopHud, GameSettings.CoopHudScale);
        }

        static void SetSlider(Slider s, Text v, float x, Func<float, string> format = null)
        {
            if (s == null) return;
            s.SetValueWithoutNotify(x);
            v.text = (format ?? Pct)(x);
        }

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
        static string SensText(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";
        static string FovText(float v) => Mathf.RoundToInt(v) + "°";
    }
}
