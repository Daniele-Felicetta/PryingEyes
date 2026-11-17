using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    public partial class GameMenu
    {
        static Font utilityFont;
        static Font MenuUtility => utilityFont != null ? utilityFont :
            utilityFont = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 24);
        static readonly Color Brass = new Color(0.69f, 0.53f, 0.30f);

        /// <summary>"VERSIONE 0.6.2": <c>Application.version</c>, che Unity ricava da Player Settings (<c>bundleVersion</c> in ProjectSettings.asset).
        /// E' lo stesso numero del tag e dello zip di una release: si cambia in un posto solo, prima di costruire la release.</summary>
        public static string VersionText => "VERSIONE " + Application.version;

        static Image Plate(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var image = Rect(parent, name, size, pos).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }

        void BuildAtmosphere()
        {
            var art = Stretch(backdrop, "Architettura incisa").gameObject.AddComponent<MenuEngraving>();
            art.raycastTarget = false;
            Label(backdrop, "Hotel", new Vector2(-435, 321), new Vector2(330, 24), 13, TextAnchor.MiddleLeft,
                "G R A N D   H O T E L   /   L A   T O R R E", Brass, FontStyle.Normal);
            Label(backdrop, "Motto", new Vector2(435, 321), new Vector2(330, 24), 13, TextAnchor.MiddleRight,
                "NESSUNA STANZA È VUOTA", Dim, FontStyle.Normal);
            Plate(backdrop, "Fregio superiore", new Vector2(0, 298), new Vector2(1170, 1), new Color(0.69f, 0.53f, 0.30f, 0.4f));
            Plate(backdrop, "Fregio inferiore", new Vector2(0, -298), new Vector2(1170, 1), new Color(0.69f, 0.53f, 0.30f, 0.4f));
            footer = Label(backdrop, "Footer", new Vector2(0, -324), new Vector2(1120, 24), 12, TextAnchor.MiddleCenter, FooterKeys, Dim, FontStyle.Normal);
            // la versione, in basso a destra sulla riga dei suggerimenti e al fondo del fregio: su ogni pannello del menu, pausa compresa
            // (a destra dei suggerimenti piu' lunghi restano oltre 100 unita': lo controlla MenuReview)
            Label(backdrop, "Versione", new Vector2(505, -324), new Vector2(160, 24), 12, TextAnchor.MiddleRight, VersionText, Dim, FontStyle.Normal);
        }

        void BuildMainMenu()
        {
            mainPanel = Stretch(backdrop, "Principale");
            Plate(mainPanel, "Colonna del registro", new Vector2(-70, -2), new Vector2(1, 520), new Color(0.69f, 0.53f, 0.30f, 0.3f));
            Label(mainPanel, "Invito", new Vector2(-340, 247), new Vector2(420, 24), 13, TextAnchor.MiddleLeft,
                "IL VOSTRO SOGGIORNO COMINCIA QUI", Brass, FontStyle.Normal);
            Label(mainPanel, "Titolo", new Vector2(-340, 144), new Vector2(440, 190), 79, TextAnchor.MiddleLeft,
                "PRYING\nEYES", Ink, FontStyle.Normal);
            Label(mainPanel, "Sottotitolo", new Vector2(-340, 26), new Vector2(420, 34), 21, TextAnchor.MiddleLeft,
                "Il Grand Hotel non chiude mai.", Dim, FontStyle.Italic);
            string[] names = { "Gioca", "Cooperativa", "Schermo condiviso", "Come si gioca", "Impostazioni", "Esci" };
            UnityEngine.Events.UnityAction[] actions = { Play, OpenCoop, OpenSplit, () => OpenHelp(mainPanel), () => OpenSettings(mainPanel), Quit };
            for (int i = 0; i < names.Length; i++)
            {
                var button = MakeButton(mainPanel, names[i], new Vector2(-340, -39 - i * 45), actions[i]);
                button.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 42);
                var text = button.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft; text.rectTransform.sizeDelta = new Vector2(368, 40); text.fontSize = 21;
                Label(button.transform, "Invito", new Vector2(180, 0), new Vector2(24, 36), 20, TextAnchor.MiddleCenter,
                    "›", Brass, FontStyle.Normal);
                if (i == 0)
                {
                    button.GetComponent<Image>().color = new Color(0.29f, 0.23f, 0.13f);
                    Plate(button.transform, "Segnalibro", new Vector2(-207, 0), new Vector2(3, 40), Brass);
                }
            }
            var eye = Rect(mainPanel, "L'occhio dell'albergo", new Vector2(580, 470), new Vector2(278, 26));
            var engraving = eye.gameObject.AddComponent<MenuEngraving>(); engraving.hero = true; engraving.raycastTarget = false;
            Label(mainPanel, "Presenza", new Vector2(280, -236), new Vector2(480, 66), 25, TextAnchor.MiddleCenter,
                "Voi guardate.\nLui vi vede.", Ink, FontStyle.Italic);
        }

        RectTransform videoTab, comfortTab;
        Button videoTabButton, comfortTabButton, settingsBackButton, perspectivePrevious;
        Text vPerspective;

        void RefreshPerspective()
        {
            if (vPerspective != null) vPerspective.text = GameSettings.ThirdPerson ? "Terza persona" : "Prima persona";
        }

        void BuildSettings()
        {
            settingsPanel = Stretch(backdrop, "Impostazioni");
            Label(settingsPanel, "Titolo", new Vector2(-330, 239), new Vector2(440, 66), 43, TextAnchor.MiddleLeft,
                "Il vostro soggiorno", Ink, FontStyle.Normal);
            Label(settingsPanel, "Sottotitolo", new Vector2(330, 233), new Vector2(440, 42), 17, TextAnchor.MiddleRight,
                "IMPOSTAZIONI  /  REGISTRO PERSONALE", Dim, FontStyle.Normal);
            videoTabButton = MakeButton(settingsPanel, "Schermo", new Vector2(-285, 164), () => SettingsTab(true));
            comfortTabButton = MakeButton(settingsPanel, "Audio e comandi", new Vector2(285, 164), () => SettingsTab(false));
            videoTab = Rect(settingsPanel, "Schermo avanzato", new Vector2(1080, 330), new Vector2(0, -35));
            comfortTab = Rect(settingsPanel, "Comfort", new Vector2(1080, 370), new Vector2(0, -42));
            BuildDisplayControls();
            var view = Rect(comfortTab, "Visuale", new Vector2(460, 340), new Vector2(-285, 0));
            var audio = Rect(comfortTab, "Audio", new Vector2(460, 340), new Vector2(285, 0));
            // 0.8.0: la prospettiva (prima o terza persona) sta in cima alla colonna; le altre quattro righe si stringono di poco
            Choice(view, "Prospettiva", 0, 122, d => { GameSettings.SetThirdPerson(!GameSettings.ThirdPerson); RefreshPerspective(); }, out vPerspective, out perspectivePrevious, out _);
            RefreshPerspective();
            sBrightness = Row(view, 44, "Luminosità", GameSettings.Brightness, GameSettings.SetBrightness, out vBrightness);
            sFov = Row(view, -20, "Campo visivo", GameSettings.Fov, GameSettings.SetFov, out vFov, 60f, 105f, FovText);
            sSensitivity = Row(view, -84, "Sensibilità mira", GameSettings.Sensitivity, GameSettings.SetSensitivity, out vSensitivity, 0.25f, 3f, SensText);
            sMotion = Row(view, -148, "Oscillazione visuale", GameSettings.Motion, GameSettings.SetMotion, out vMotion);
            sMaster = Row(audio, 100, "Volume generale", GameSettings.Master, GameSettings.SetMaster, out vMaster);
            sEffects = Row(audio, 18, "Effetti sonori", GameSettings.Effects, GameSettings.SetEffects, out vEffects);
            sMusic = Row(audio, -64, "Musica", GameSettings.Music, GameSettings.SetMusic, out vMusic);
            // 0.7.2: il pannello dei compagni (cooperativa e schermo condiviso) si ingrandisce o si rimpicciolisce; si vede subito
            sCoopHud = Row(audio, -146, "HUD dei compagni", GameSettings.CoopHudScale, GameSettings.SetCoopHudScale, out vCoopHud,
                GameSettings.CoopHudMin, GameSettings.CoopHudMax);
            settingsBackButton = MakeButton(settingsPanel, "Indietro", new Vector2(-285, -260), Back);
            BuildDisplayConfirmation();
        }

        void SettingsTab(bool video)
        {
            videoTab.gameObject.SetActive(video); comfortTab.gameObject.SetActive(!video);
            settingsBackButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(video ? -285 : 0, -260);
            videoTabButton.GetComponent<Image>().color = video ? new Color(0.26f, 0.21f, 0.13f) : BtnColor;
            comfortTabButton.GetComponent<Image>().color = !video ? new Color(0.26f, 0.21f, 0.13f) : BtnColor;
            // con tastiera e controller le due schede si scelgono con sinistra e destra e portano in basso al primo comando della scheda aperta
            // (la navigazione automatica sceglierebbe il comando piu' vicino, non l'altra scheda)
            videoTabButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = comfortTabButton, selectOnDown = video ? (Selectable)modePrevious : perspectivePrevious };
            comfortTabButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = videoTabButton.interactable ? videoTabButton : null, selectOnDown = video ? (Selectable)aspectPrevious : sMaster };
        }
    }
}
