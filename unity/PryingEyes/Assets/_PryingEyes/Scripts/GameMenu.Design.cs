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

        // ---------- i consigli sotto la frase ----------

        /// <summary>Un consiglio ogni 8 secondi sotto "Voi guardate. Lui vi vede.": ogni voce e' una frase vera sul gioco; se nomina un tasto, la
        /// seconda e' quella per il controller. Sono tutte cose scritte altrove nel gioco (guida, tabellone, commenti dei sistemi).</summary>
        static readonly string[][] Tips =
        {
            new[] { "I nemici ti inseguono subito: scale, balconate e ripari ti aprono una via di fuga." },
            new[] { "Le ondate partono da sole. Spendi i punti alle porte: le aree comprate restano aperte per tutti." },
            new[] { "Compra le armi ai muri e cerca pile e libri nei santuari." },
            new[] { "Se muori perdi metà dei punti e l'ultimo perk acquistato." },
            new[] { "Alle spalle di chi non ti ha visto il coltello (G) è silenzioso e uccide sul colpo.",
                    "Alle spalle di chi non ti ha visto il coltello (R3) è silenzioso e uccide sul colpo." },
            new[] { "La torcia (T) ti rende visibile da lontano: accendila solo quando serve.",
                    "La torcia (croce su) ti rende visibile da lontano: accendila solo quando serve." },
            new[] { "La pila della torcia si scarica: Y la cambia con una di scorta.",
                    "La pila della torcia si scarica: tieni premuta la croce su per cambiarla con una di scorta." },
            new[] { "Tieni premuto F per rialzare un compagno a terra.", "Tieni premuto X per rialzare un compagno a terra." },
            new[] { "Ogni cinque ondate arriva la Processione dei lumi." },
            new[] { "V mostra dove sono le porte quando non sai dove andare.", "La croce a sinistra mostra dove sono le porte quando non sai dove andare." },
            new[] { "Sporgiti con Q ed E per guardare oltre gli angoli.", "Mira con LT e premi L3 o R3 per sporgerti oltre gli angoli." },
            new[] { "M apre la mappa, N accende e spegne la minimappa.", "Back apre la mappa; tenuto premuto, il tabellone." },
            new[] { "Z lancia l'acquasanta, 4 il carillon, 5 piazza una mina.", "RB lancia l'acquasanta, la croce giù il carillon (tenuta, una mina)." },
        };

        public const float TipSeconds = 8f;
        Text tipLabel;
        int tipIndex = -1;
        float tipAt;
        bool tipPad;

        public static int TipCount => Tips.Length;
        /// <summary>Il consiglio <paramref name="index"/>, nella forma per tastiera o per controller (se ne ha una).</summary>
        public static string TipFor(int index, bool pad) { var t = Tips[index]; return pad && t.Length > 1 ? t[1] : t[0]; }
        string TipText(int index) => TipFor(index, PadControls.Preferred);

        /// <summary>Un consiglio alla volta, con una dissolvenza di mezzo secondo all'inizio e alla fine (ferma con l'oscillazione a zero).</summary>
        void UpdateTips()
        {
            if (tipLabel == null || !mainPanel.gameObject.activeSelf) return;
            float now = Time.unscaledTime;
            bool pad = PadControls.Preferred;
            if (tipIndex < 0) { tipIndex = (int)(Time.realtimeSinceStartup * 1000f) % Tips.Length; tipAt = now; tipLabel.text = TipText(tipIndex); tipPad = pad; }
            if (now - tipAt >= TipSeconds) { tipIndex = (tipIndex + 1) % Tips.Length; tipAt = now; tipLabel.text = TipText(tipIndex); tipPad = pad; }
            else if (pad != tipPad) { tipPad = pad; tipLabel.text = TipText(tipIndex); }
            float age = now - tipAt;
            float alpha = !Application.isPlaying || GameSettings.Motion < 0.01f ? 1f : Mathf.Clamp01(Mathf.Min(age, TipSeconds - age) / 0.5f);
            var color = tipLabel.color; color.a = 0.92f * alpha; tipLabel.color = color;
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
                var arrow = Label(button.transform, "Invito", new Vector2(180, 0), new Vector2(24, 36), 20, TextAnchor.MiddleCenter,
                    "›", Brass, FontStyle.Normal);
                var fx = button.gameObject.AddComponent<MenuButtonFx>();
                fx.label = text; fx.arrow = arrow;
                if (i == 0)
                {
                    button.GetComponent<Image>().color = new Color(0.29f, 0.23f, 0.13f);
                    Plate(button.transform, "Segnalibro", new Vector2(-207, 0), new Vector2(3, 40), Brass);
                }
            }
            var eye = Rect(mainPanel, "L'occhio dell'albergo", new Vector2(580, 470), new Vector2(278, 26));
            var engraving = eye.gameObject.AddComponent<MenuEngraving>(); engraving.hero = true; engraving.raycastTarget = false;
            Label(mainPanel, "Presenza", new Vector2(280, -208), new Vector2(480, 40), 25, TextAnchor.MiddleCenter,
                "Voi guardate. Lui vi vede.", Ink, FontStyle.Italic);
            Plate(mainPanel, "Filetto dei consigli", new Vector2(280, -231), new Vector2(120, 1), new Color(0.69f, 0.53f, 0.30f, 0.5f));
            Label(mainPanel, "Consiglio titolo", new Vector2(280, -243), new Vector2(480, 18), 12, TextAnchor.MiddleCenter, "CONSIGLIO", Brass, FontStyle.Normal);
            tipLabel = Label(mainPanel, "Consiglio", new Vector2(280, -274), new Vector2(520, 44), 15, TextAnchor.UpperCenter, "", Dim, FontStyle.Italic);
        }
    }
}
