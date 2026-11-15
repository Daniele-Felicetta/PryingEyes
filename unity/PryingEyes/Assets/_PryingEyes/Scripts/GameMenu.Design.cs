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
            Label(backdrop, "Footer", new Vector2(0, -324), new Vector2(1120, 24), 12, TextAnchor.MiddleCenter,
                "WASD / FRECCE   NAVIGA       ·       INVIO   SCEGLI       ·       ESC   INDIETRO", Dim, FontStyle.Normal);
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
            string[] names = { "Gioca", "Cooperativa", "Come si gioca", "Impostazioni", "Esci" };
            UnityEngine.Events.UnityAction[] actions = { Play, OpenCoop, () => OpenHelp(mainPanel), () => OpenSettings(mainPanel), Quit };
            for (int i = 0; i < names.Length; i++)
            {
                var button = MakeButton(mainPanel, names[i], new Vector2(-340, -39 - i * 49), actions[i]);
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
        Button videoTabButton, comfortTabButton, settingsBackButton;

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
            sBrightness = Row(view, 100, "Luminosità", GameSettings.Brightness, GameSettings.SetBrightness, out vBrightness);
            sFov = Row(view, 18, "Campo visivo", GameSettings.Fov, GameSettings.SetFov, out vFov, 60f, 105f, FovText);
            sSensitivity = Row(view, -64, "Sensibilità mira", GameSettings.Sensitivity, GameSettings.SetSensitivity, out vSensitivity, 0.25f, 3f, SensText);
            sMotion = Row(view, -146, "Oscillazione visuale", GameSettings.Motion, GameSettings.SetMotion, out vMotion);
            sMaster = Row(audio, 100, "Volume generale", GameSettings.Master, GameSettings.SetMaster, out vMaster);
            sEffects = Row(audio, 18, "Effetti sonori", GameSettings.Effects, GameSettings.SetEffects, out vEffects);
            sMusic = Row(audio, -64, "Musica", GameSettings.Music, GameSettings.SetMusic, out vMusic);
            Label(audio, "Comfort", new Vector2(0, -132), new Vector2(440, 66), 17, TextAnchor.MiddleLeft,
                "Riduci l'oscillazione per una visuale più stabile. Audio e comandi si applicano subito.", Dim, FontStyle.Normal);
            settingsBackButton = MakeButton(settingsPanel, "Indietro", new Vector2(-285, -260), Back);
            BuildDisplayConfirmation();
        }

        void SettingsTab(bool video)
        {
            videoTab.gameObject.SetActive(video); comfortTab.gameObject.SetActive(!video);
            settingsBackButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(video ? -285 : 0, -260);
            videoTabButton.GetComponent<Image>().color = video ? new Color(0.26f, 0.21f, 0.13f) : BtnColor;
            comfortTabButton.GetComponent<Image>().color = !video ? new Color(0.26f, 0.21f, 0.13f) : BtnColor;
        }
    }
}
