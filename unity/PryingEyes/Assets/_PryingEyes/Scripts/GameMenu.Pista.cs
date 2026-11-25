using System.Collections.Generic;
using PryingEyes.Pista;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La preparazione di una gara su pista (Scripts/Pista): compare dopo "Su pista" nella scelta della modalita' (GameMenu.Mode.cs), da soli o prima di ospitare una cooperativa.
    /// Giri, avversari, livello, moto, aiuti e ora del giorno (le scelte si ricordano, PistaMode), e a destra l'anteprima del circuito: e' esattamente quello che si correra'
    /// ("Altra pista" ne pesca un altro). "Via!" avvia la partita, o apre la porta se e' una cooperativa.</summary>
    public partial class GameMenu
    {
        RectTransform pistaPanel;
        RawImage pistaMap;
        Texture2D pistaMapTex;
        Text pistaName, pistaFacts, pistaBike, pistaSubtitle, pistaRecord;
        static string[] pistaNames;
        Circuit pistaCircuit;
        Button pistaGoButton, pistaOtherButton, pistaBackButton;
        readonly List<System.Action> pistaRefresh = new List<System.Action>();
        readonly List<MenuChoice> pistaRows = new List<MenuChoice>();
        int pistaSeed;
        string pistaSubtitleBase = "Un circuito nuovo a ogni gara: quello che vedi qui e' quello che corri.";
        static readonly System.Globalization.CultureInfo PistaIt = System.Globalization.CultureInfo.GetCultureInfo("it-IT");

        const float PistaLabelX = -478f, PistaChoiceX = -250f, PistaChoiceW = 300f;

        void BuildPista()
        {
            pistaPanel = Stretch(backdrop, "SuPista");
            Label(pistaPanel, "Titolo", new Vector2(0, 262), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Moto su pista", Ink, FontStyle.Normal);
            pistaSubtitle = Label(pistaPanel, "Sottotitolo", new Vector2(0, 214), new Vector2(1100, 30), 20, TextAnchor.MiddleCenter,
                "Un circuito nuovo a ogni gara: quello che vedi qui e' quello che corri.", Dim, FontStyle.Italic);

            float y = 170f;
            PistaRow("Circuito", y, () => PistaMode.CircuitIndex < 0 ? "Casuale" : PistaCircuitName(PistaMode.CircuitIndex), PistaCircuitStep); y -= 34f;
            PistaRow("Giri", y, () => PistaMode.Laps == 1 ? "1 giro" : PistaMode.Laps + " giri", d => PistaMode.Laps = StepIn(PistaMode.LapChoices, PistaMode.Laps, d)); y -= 34f;
            PistaRow("Avversari", y, () => PistaMode.Rivals == 0 ? "Nessuno: giro a tempo" : PistaMode.Rivals + " avversari", d => PistaMode.Rivals = StepIn(PistaMode.RivalChoices, PistaMode.Rivals, d)); y -= 34f;
            PistaRow("Livello", y, () => PistaMode.RankNames[PistaMode.Rank], d => PistaMode.Rank = Wrap(PistaMode.Rank + d, PistaMode.RankNames.Length)); y -= 34f;
            PistaRow("Moto", y, () => Bikes.Get(PistaMode.Bike).name, d => PistaMode.Bike = PistaMode.StepBike(PistaMode.Bike, d)); y -= 34f;          // in a career only the bikes that are owned
            PistaRow("Stile di guida", y, () => PistaMode.StyleNames[PistaMode.Style], d => PistaMode.Style = Wrap(PistaMode.Style + d, PistaMode.StyleNames.Length)); y -= 34f;
            PistaRow("Aiuti alla guida", y, () => PistaMode.AidNames[PistaMode.Aids], d => PistaMode.Aids = Wrap(PistaMode.Aids + d, PistaMode.AidNames.Length)); y -= 34f;
            PistaRow("Ora del giorno", y, () => PistaMode.HourNames[PistaMode.HourChoice], d => PistaMode.HourChoice = Wrap(PistaMode.HourChoice + d, PistaMode.HourNames.Length)); y -= 34f;
            PistaRow("Tempo", y, () => PistaMode.WeatherNames[PistaMode.WeatherChoice], d => PistaMode.WeatherChoice = Wrap(PistaMode.WeatherChoice + d, PistaMode.WeatherNames.Length));          // the rain: less grip, a grey sky, spray
            pistaBike = Label(pistaPanel, "Scheda moto", new Vector2(PistaChoiceX + 70f, -134f), new Vector2(660, 28), 18, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            // four lines broken by hand, in a box that ends before the "Altra pista" button (the box was 660 wide and ended 20 units under the button: the last word of a line was cut)
            Label(pistaPanel, "Nota aiuti", new Vector2(PistaChoiceX + 50f, -197f), new Vector2(640, 70), 16, TextAnchor.UpperCenter,
                "Aiuti. Completi: ABS, trazione, antimpennata, marce e aiuto in piega." + "\n"
                + "Sportivi: tutti tranne l'aiuto in piega. Nessuno: solo l'ABS, marce a mano." + "\n"
                + "Linea di corsa: Completi verde, giallo, rosso; Sportivi i segni di frenata." + "\n"
                + "Stile. Peso fuori: entra in fretta, piu' dritta. Moto in piega: lenta e stabile.", Dim, FontStyle.Normal);

            // l'anteprima del circuito
            var frame = Rect(pistaPanel, "CorniceMappa", new Vector2(292, 292), new Vector2(300, 40));
            var frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.color = new Color(0.03f, 0.05f, 0.05f, 0.96f); frameImage.raycastTarget = false;
            var edge = frame.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.50f); edge.effectDistance = new Vector2(1f, -1f);
            var mapRect = Rect(frame, "Mappa", new Vector2(276, 276), Vector2.zero);
            pistaMap = mapRect.gameObject.AddComponent<RawImage>();
            pistaMap.raycastTarget = false;
            pistaName = Label(pistaPanel, "Nome del circuito", new Vector2(300, -120), new Vector2(420, 30), 24, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            pistaFacts = Label(pistaPanel, "Dati del circuito", new Vector2(300, -146), new Vector2(460, 26), 17, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            pistaRecord = Label(pistaPanel, "Record del circuito", new Vector2(300, -171), new Vector2(460, 26), 17, TextAnchor.MiddleCenter, "", new Color(0.78f, 0.52f, 1f), FontStyle.Normal);
            pistaOtherButton = MakeButton(pistaPanel, "Altra pista", new Vector2(300, -208), PistaOther);
            pistaGoButton = MakeButton(pistaPanel, "Via!", new Vector2(-190, -262), PistaGo);
            pistaGoButton.GetComponentInChildren<Text>().text = "Via";          // the object stays "Pulsante Via!": the menu font draws the exclamation mark like an "l" ("Vial")
            pistaBackButton = MakeButton(pistaPanel, "Indietro", new Vector2(190, -262), () => Show(modePanel));
            licenceOpenButton = MakeButton(pistaPanel, "Patenti", new Vector2(468, 270), LicenceOpen);
            ResizeButton(licenceOpenButton, new Vector2(220, 42), 22);
            garageOpenButton = MakeButton(pistaPanel, "Garage", new Vector2(468, 222), GarageOpen);
            ResizeButton(garageOpenButton, new Vector2(220, 42), 22);
            cupOpenButton = MakeButton(pistaPanel, "Campionato", new Vector2(-468, 270), CupOpen);
            ResizeButton(cupOpenButton, new Vector2(220, 42), 22);

            // su e giu' scorrono tutta la schermata, dalle scelte al "Via!" (sinistra e destra cambiano il valore di una scelta)
            var chain = new List<Selectable> { cupOpenButton, licenceOpenButton, garageOpenButton };
            chain.AddRange(pistaRows);
            chain.Add(pistaOtherButton); chain.Add(pistaGoButton); chain.Add(pistaBackButton);
            for (int i = 0; i < chain.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = i > 0 ? chain[i - 1] : null, selectOnDown = i < chain.Count - 1 ? chain[i + 1] : null };
                chain[i].navigation = nav;
                AddFeedback(chain[i], null);
            }
            pistaPanel.gameObject.AddComponent<MenuPanelFade>();
        }

        /// <summary>Una riga a scelta (‹ valore ›) della preparazione: stessa forma di quelle delle impostazioni, al posto fisso della colonna di sinistra.</summary>
        void PistaRow(string title, float y, System.Func<string> text, System.Action<int> step)
        {
            var rowLabel = Label(pistaPanel, title, new Vector2(PistaLabelX, y), new Vector2(220, 30), 20, TextAnchor.MiddleLeft, title, Ink, FontStyle.Normal);
            var rt = Rect(pistaPanel, "Scelta " + title, new Vector2(PistaChoiceW, 32), new Vector2(PistaChoiceX, y));
            var plate = rt.gameObject.AddComponent<Image>();
            plate.color = BtnColor;
            var edge = rt.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.50f); edge.effectDistance = new Vector2(1f, -1f);
            var choice = rt.gameObject.AddComponent<MenuChoice>();
            choice.targetGraphic = plate;
            choice.colors = ButtonColors();
            choice.rowLabel = rowLabel;
            choice.textOn = Ink;
            choice.valueLabel = Label(rt, title + " valore", Vector2.zero, new Vector2(PistaChoiceW - 100f, 32), 20, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            Label(rt, "Freccia sinistra", new Vector2(-PistaChoiceW * 0.5f + 26f, 0), new Vector2(40, 32), 26, TextAnchor.MiddleCenter, "‹", Brass, FontStyle.Normal);
            Label(rt, "Freccia destra", new Vector2(PistaChoiceW * 0.5f - 26f, 0), new Vector2(40, 32), 26, TextAnchor.MiddleCenter, "›", Brass, FontStyle.Normal);
            choice.onStep = d => { step(d); RefreshPista(false); };
            pistaRefresh.Add(() => choice.Value = text());
            pistaRows.Add(choice);
        }

        /// <summary>Il valore successivo (o precedente) in un elenco, rigirando agli estremi; un valore che non c'e' riparte dal primo.</summary>
        static int StepIn(int[] values, int current, int direction)
        {
            int i = System.Array.IndexOf(values, current);
            if (i < 0) i = 0;
            return values[Wrap(i + direction, values.Length)];
        }

        /// <summary>"Su pista" e' stato scelto: si apre la preparazione. Il circuito dell'anteprima e' quello che poi si corre (PistaMode.Chosen lo porta fino alla partita).</summary>
        void ChoosePista()
        {
            PistaMode.Requested = true;
            if (PistaMode.Chosen == 0) PistaMode.Chosen = PistaMode.CircuitIndex >= 0 ? Catalogue.All[PistaMode.CircuitIndex].seed : Random.Range(1, 1 << 28);
            pistaSeed = PistaMode.Chosen;
            pistaSubtitleBase = modeForCoop ? "Gli altri entrano nella tua gara, fino a quattro: la minimappa mostra tutti."
                : "Un circuito nuovo a ogni gara: quello che vedi qui e' quello che corri.";
            cupOpenButton.gameObject.SetActive(!modeForCoop);              // il campionato, come le patenti, si corre da soli
            licenceOpenButton.gameObject.SetActive(!modeForCoop);          // le patenti si fanno da soli
            RefreshPista(true);
            Show(pistaPanel);
            var system = UnityEngine.EventSystems.EventSystem.current;
            if (system != null && pistaGoButton != null) system.SetSelectedGameObject(pistaGoButton.gameObject);       // Invio (o A) parte subito; la croce sale alle scelte
        }

        /// <summary>I valori delle righe, la scheda della moto e, se serve, l'anteprima del circuito.</summary>
        void RefreshPista(bool newMap)
        {
            foreach (var refresh in pistaRefresh) refresh();
            // with the career the line says the money and is shorter than the usual one, so that it stays clear of the Garage button that crosses it (the career never applies to a cooperative game: the base is the single player's)
            pistaSubtitle.text = PistaMode.CareerApplies ? "Il circuito che vedi e' quello che corri   -   Carriera: conto " + Economy.Format(PistaCareer.Book.Money) + " cr" : pistaSubtitleBase;
            var spec = PistaMode.SpecFor(PistaMode.Bike);          // the bike as it will be raced: with the level of tuning in a career
            float rpm;
            float hp = spec.PeakPowerKw(out rpm) * 1.36f;
            pistaBike.text = spec.name + (spec.tune > 0 ? " liv. " + spec.tune : "") + "  -  " + spec.klass + "  -  " + Mathf.RoundToInt(hp / 5f) * 5 + " CV  -  " + Mathf.RoundToInt(spec.Mass) + " kg con il pilota";
            if (newMap) RefreshPistaMap();
            RefreshPistaRecord();
        }

        void RefreshPistaMap()
        {
            var circuit = Circuits.Generate(pistaSeed);
            pistaCircuit = circuit;
            var fit = PistaMap.FitFor(circuit, 256, 14f);
            var tex = PistaMap.Paint(circuit, fit, 4.5f, "Anteprima della pista");
            if (pistaMapTex != null) Destroy(pistaMapTex);
            pistaMapTex = tex;
            pistaMap.texture = tex;
            pistaName.text = circuit.name;
            int named = Catalogue.IndexOf(pistaSeed);
            pistaFacts.text = (circuit.length / 1000f).ToString("0.0", PistaIt) + " km" + (named >= 0 ? "  -  " + Catalogue.All[named].tag : "  -  circuito unico");
            RefreshPistaRecord();
        }

        /// <summary>The line under the circuit: the best lap of the bike chosen, on a named circuit (the record depends on the bike, so it is worked out again when the bike changes).</summary>
        void RefreshPistaRecord()
        {
            if (pistaRecord == null || pistaCircuit == null) return;
            var spec = PistaMode.SpecFor(PistaMode.Bike);
            string called = spec.name + (spec.tune > 0 ? " liv. " + spec.tune : "");
            bool named = Catalogue.Named(pistaCircuit.seed);
            Ghost record = null;
            if (named) record = PistaRecords.Best(pistaCircuit, spec.id, 1) ?? PistaRecords.Best(pistaCircuit, spec.id, 0);
            pistaRecord.text = !named ? "" : record != null ? "Record con " + called + ":  " + PistaHud.Clock(record.lapTime) : "Nessun record con " + called;
        }

        /// <summary>The name of a named circuit (the generator makes it from the seed; the names are worked out once).</summary>
        static string PistaCircuitName(int index)
        {
            if (pistaNames == null)
            {
                pistaNames = new string[Catalogue.Count];
                for (int i = 0; i < pistaNames.Length; i++) pistaNames[i] = Circuits.Generate(Catalogue.All[i].seed).name;
            }
            return pistaNames[Mathf.Clamp(index, 0, pistaNames.Length - 1)];
        }

        /// <summary>The row 'Circuito': Casuale, then the named circuits one after the other (and round again).</summary>
        void PistaCircuitStep(int direction)
        {
            int index = Wrap(PistaMode.CircuitIndex + 1 + direction, Catalogue.Count + 1) - 1;
            PistaMode.CircuitIndex = index;
            PistaMode.Chosen = index >= 0 ? Catalogue.All[index].seed : Random.Range(1, 1 << 28);
            pistaSeed = PistaMode.Chosen;
            RefreshPistaMap();
        }

        /// <summary>"Altra pista": un circuito nuovo (il seme e' tutto quello che serve, la partita lo ridisegna uguale).</summary>
        void PistaOther()
        {
            PistaMode.CircuitIndex = -1;
            PistaMode.Chosen = Random.Range(1, 1 << 28);
            pistaSeed = PistaMode.Chosen;
            RefreshPista(true);
        }

        /// <summary>"Via!": da soli la gara parte, in cooperativa si apre la porta (gli altri entrano dove corre l'ospite).</summary>
        void PistaGo()
        {
            PistaMode.Requested = true;
            if (modeForCoop) { modeForCoop = false; HostNow(); return; }
            Play();
        }

        void PistaShow(RectTransform p)
        {
            if (pistaPanel != null) pistaPanel.gameObject.SetActive(p == pistaPanel);
        }

        /// <summary>Indietro (Esc, B) dalla preparazione: alla scelta della modalita'. Vero se ne ha gestito uno.</summary>
        bool PistaBack()
        {
            if (pistaPanel == null || !pistaPanel.gameObject.activeSelf) return false;
            MenuSound.Back();
            Show(modePanel);
            return true;
        }
    }
}
