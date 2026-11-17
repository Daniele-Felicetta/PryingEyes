using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>"Come si gioca" (0.8.0): tre pagine, come le schede delle impostazioni: l'obiettivo e i consigli, i tasti della tastiera e del mouse,
    /// i tasti del controller. Dalle impostazioni, "Elenco dei comandi" apre la pagina del dispositivo in uso; Esc o B tornano da dove si era venuti.</summary>
    public partial class GameMenu
    {
        static readonly string[] HelpNames = { "Obiettivo", "Tastiera", "Controller" };
        static readonly string[] HelpHints =
        {
            "Che cosa fare e come cavarsela. Sinistra e destra cambiano pagina.",
            "Tutti i tasti della tastiera e del mouse. Sinistra e destra cambiano pagina.",
            "Tutti i tasti del controller. Sinistra e destra cambiano pagina.",
        };

        // tastiera e mouse, tre colonne: "tasto|che cosa fa", "#TITOLO" per un titolo, "" per una riga vuota
        static readonly string[] KeysMove =
        {
            "#MOVIMENTO", "W A S D|Muoviti", "Maiusc|Corri", "C / Ctrl|Abbassati", "Spazio|Salta e scavalca", "Q / E|Sporgiti ai lati", "F|Usa, compra, rialza",
            "", "#COMBATTIMENTO", "Mouse sx|Spara", "Mouse dx|Mira", "R|Ricarica", "1 / 2 / X|Cambia arma", "B|Modo di fuoco", "G|Coltello",
        };
        static readonly string[] KeysGear =
        {
            "#EQUIPAGGIAMENTO", "Z|Acquasanta", "4|Carillon", "5|Mina", "T|Torcia", "Y|Cambia la pila",
            "", "#MAGIE", "3|Magia (mano sinistra)", "3 (tenuto)|Sigillo sull'arma", "6|Magia seguente", "7|Sigillo seguente",
        };
        static readonly string[] KeysOther =
        {
            "#ORIENTARSI", "V|Mostra le porte", "M|Mappa", "N|Minimappa", "Tab (tieni)|Tabellone",
            "", "#ALTRO", "H|Velo, con il perk", "I|Osserva l'arma", "Esc|Pausa",
        };

        // il controller, tre colonne come la tastiera: "(tenuto)" e' tenendo premuto il tasto della riga sopra (lo stesso foglio del menu di pausa, PadSheet)
        static readonly string[] PadMove =
        {
            "#MOVIMENTO", "Stick sx|Muoviti", "(premuto)|Corri", "A|Salta", "B|Abbassati",
            "", "#MIRA", "Stick dx|Guarda", "(premuto)|Coltello", "LT|Mira", "L3 / R3|Sporgiti, con la mira",
        };
        static readonly string[] PadFight =
        {
            "#COMBATTIMENTO", "RT|Spara", "X|Ricarica", "(tenuto)|Usa e compra", "Y|Cambia arma", "(tenuto)|Modo di fuoco", "RB|Acquasanta",
            "", "#MAGIA", "LB|Magia (mano sinistra)", "(tenuto)|Sigillo sull'arma",
        };
        static readonly string[] PadCross =
        {
            "#CROCE", "Su|Torcia", "(tenuto)|Cambia la pila", "Giù|Carillon", "(tenuto)|Mina", "Sinistra|Mostra le porte", "(tenuto)|Velo, con il perk",
            "Destra|Magia seguente", "(tenuto)|Sigillo seguente", "", "#ALTRO", "Back|Mappa", "(tenuto)|Tabellone", "Start|Pausa",
        };

        RectTransform[] helpPages;
        Button[] helpTabs;
        Image[] helpUnderlines;
        Button helpBack;
        int helpPage;

        void BuildHelp()
        {
            helpPanel = Stretch(backdrop, "Guida");
            Label(helpPanel, "Titolo", new Vector2(-330, 246), new Vector2(440, 62), 43, TextAnchor.MiddleLeft, "La Torre", Ink, FontStyle.Normal);
            Label(helpPanel, "Sottotitolo", new Vector2(330, 241), new Vector2(440, 42), 17, TextAnchor.MiddleRight,
                "COME SI GIOCA  /  GUIDA DELL'OSPITE", Dim, FontStyle.Normal);
            helpPages = new RectTransform[HelpNames.Length];
            helpTabs = new Button[HelpNames.Length];
            helpUnderlines = new Image[HelpNames.Length];
            for (int i = 0; i < HelpNames.Length; i++)
            {
                int index = i;
                helpPages[i] = Stretch(helpPanel, "Pagina " + HelpNames[i]);
                helpTabs[i] = MakeButton(helpPanel, HelpNames[i], new Vector2(-204 + i * 204, 176), () => SelectHelpPage(index));
                ResizeButton(helpTabs[i], new Vector2(196, 40), 21);
                AddFeedback(helpTabs[i], HelpHints[i]).selected = () => SelectHelpPage(index);
                helpUnderlines[i] = Plate(helpTabs[i].transform, "Sottolineatura", new Vector2(0, -17), new Vector2(176, 3), Brass);
            }
            Plate(helpPanel, "Filetto", new Vector2(0, 148), new Vector2(1010, 1), new Color(0.69f, 0.53f, 0.30f, 0.45f));

            // pagina 1: l'obiettivo
            var goal = helpPages[0];
            Label(goal, "Obiettivo", new Vector2(0, 96), new Vector2(1000, 60), 23, TextAnchor.MiddleCenter,
                "Sopravvivi alle ondate. Dopo tre ondate puoi raggiungere l’ascensore e scendere.", Ink, FontStyle.Normal);
            helpMove = Label(goal, "Furtivo", new Vector2(0, 20), new Vector2(1000, 76), 21, TextAnchor.MiddleLeft, HelpMoveText(false), Dim, FontStyle.Normal);
            helpRound = Label(goal, "Round", new Vector2(0, -68), new Vector2(1000, 76), 21, TextAnchor.MiddleLeft, HelpRoundText(false), Dim, FontStyle.Normal);
            Label(goal, "Risorse", new Vector2(0, -156), new Vector2(1000, 76), 21, TextAnchor.MiddleLeft,
                "ESPLORA E SCEGLI\nCompra armi ai muri, cerca pile e libri nei santuari. Se muori perdi metà dei punti e l'ultimo perk acquistato.", Dim, FontStyle.Normal);

            // pagina 2: la tastiera, tre colonne
            HelpColumn(helpPages[1], "Movimento e combattimento", -335f, KeysMove);
            HelpColumn(helpPages[1], "Equipaggiamento e magie", 0f, KeysGear);
            HelpColumn(helpPages[1], "Orientarsi", 335f, KeysOther);

            // pagina 3: il controller, tre colonne
            HelpColumn(helpPages[2], "Movimento e mira", -335f, PadMove);
            HelpColumn(helpPages[2], "Combattimento e magia", 0f, PadFight);
            HelpColumn(helpPages[2], "Croce e altro", 335f, PadCross);

            helpBack = MakeButton(helpPanel, "Indietro", new Vector2(0, -266), () => Show(helpFrom));
            ResizeButton(helpBack, new Vector2(340, 44), 22);
            SelectHelpPage(0);
        }

        /// <summary>Una colonna di tasti: a sinistra i tasti, a destra cosa fanno. Sono due testi con lo stesso numero di righe, quindi si allineano.</summary>
        void HelpColumn(Transform parent, string name, float centerX, string[] lines)
        {
            var keys = new StringBuilder();
            var does = new StringBuilder();
            foreach (string line in lines)
            {
                if (line.StartsWith("#")) { keys.Append("<color=#B0874C>").Append(line.Substring(1)).Append("</color>\n"); does.Append('\n'); continue; }
                int bar = line.IndexOf('|');
                keys.Append(bar < 0 ? "" : line.Substring(0, bar)).Append('\n');
                does.Append(bar < 0 ? "" : line.Substring(bar + 1)).Append('\n');
            }
            float left = centerX - 160f;
            var k = Label(parent, name + " tasti", new Vector2(left + 52f, -42f), new Vector2(104f, 320f), 17, TextAnchor.UpperLeft, keys.ToString().TrimEnd('\n'), Ink, FontStyle.Normal);
            k.horizontalOverflow = HorizontalWrapMode.Overflow;       // un titolo e' piu' largo della colonna dei tasti: la riga accanto e' vuota
            Label(parent, name + " azioni", new Vector2(left + 104f + 110f, -42f), new Vector2(220f, 320f), 17, TextAnchor.UpperLeft, does.ToString().TrimEnd('\n'), Dim, FontStyle.Normal);
        }

        void SelectHelpPage(int index)
        {
            if (helpPages == null || index < 0 || index >= helpPages.Length) return;
            helpPage = index;
            for (int i = 0; i < helpPages.Length; i++)
            {
                bool on = i == index;
                helpPages[i].gameObject.SetActive(on);
                helpTabs[i].GetComponent<Image>().color = on ? new Color(0.26f, 0.21f, 0.13f) : BtnColor;
                helpUnderlines[i].enabled = on;
            }
            if (helpBack == null) return;
            for (int i = 0; i < helpTabs.Length; i++)
                helpTabs[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? helpTabs[i - 1] : null,
                    selectOnRight = i < helpTabs.Length - 1 ? helpTabs[i + 1] : null,
                    selectOnDown = helpBack,
                };
            helpBack.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = helpTabs[index] };
        }

        /// <summary>Apre la guida. Dalle impostazioni (<paramref name="controls"/>) va sulla pagina dei tasti del dispositivo in uso.</summary>
        void OpenHelp(RectTransform from, bool controls = false)
        {
            helpFrom = from;
            SelectHelpPage(controls ? (PadControls.Preferred ? 2 : 1) : 0);
            Show(helpPanel);
            EventSystem.current?.SetSelectedGameObject(helpTabs[helpPage].gameObject);
        }
    }
}
