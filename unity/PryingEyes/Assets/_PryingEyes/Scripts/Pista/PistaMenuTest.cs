#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes.Pista
{
    /// <summary>-pistamenutest [-pistashots] (WITHOUT -autotest, which has no menu; in -batchmode the characters are the test ones and the preferences are not touched): the way into a race from the menu.
    /// "Gioca" asks how to play with three modes, "Su pista" opens the setup panel (the "Via!" button selected, a picture of the circuit that will be raced), the rows step and the values show, the
    /// circuit picture follows "Altra pista", up and down walk the whole screen, Esc / B go back to the modes with "Su pista" still selected, the circuit and the choices are still there when the panel
    /// is opened again, and "Via!" starts the race on that very circuit with the laps, the rivals and the bike that were chosen. With -pistamenulicence the same way in goes to the licence panel instead:
    /// the "Patenti" button, the nine tests with their bikes and medals, the line under the one chosen, the cross, the book of times, back, and the click that starts a test with the rules of that test.</summary>
    public sealed class PistaMenuTest : MonoBehaviour
    {
        int checks, failed, errors;
        string firstError = "";
        bool shots, ended;
        GameMenu menu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pistamenutest") < 0) return;
            new GameObject("PistaMenuTest").AddComponent<PistaMenuTest>();
        }

        void Check(string name, bool ok, string detail = "")
        {
            checks++; if (!ok) failed++;
            Debug.Log("[PryingEyes] pistamenutest " + (ok ? "ok " : "FALLITO ") + name + (detail.Length > 0 ? " (" + detail + ")" : ""));
        }

        bool Check2(string name, bool ok, string detail = "") { Check(name, ok, detail); return ok; }

        void OnLog(string text, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (Application.isBatchMode && text != null && text.StartsWith("Failed to capture screen shot")) return;
            errors++;
            if (firstError.Length == 0) firstError = text.Length > 160 ? text.Substring(0, 160) : text;
        }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }

        IEnumerator Start()
        {
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-pistashots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more;
                try { more = sequence.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false, e.Message); break; }
                if (!more) break;
                yield return sequence.Current;
            }
            Finish();
        }

        void Update()
        {
            if (!ended && Time.realtimeSinceStartup > 400f) { Check("finita entro il tempo", false, "400 s"); Finish(); }
        }

        void Finish()
        {
            if (ended) return;
            ended = true;
            Time.timeScale = 1f;
            Check("nessun errore o eccezione nel registro durante la prova", errors == 0, errors == 0 ? "" : errors + ", la prima: " + firstError);
            Debug.Log($"[PryingEyes] pistamenutest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        IEnumerator WaitFor(Func<bool> cond, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        // ------------------------------------------------------------------ helpers on the private parts of the menu
        const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        object Call(string method, params object[] args) { return typeof(GameMenu).GetMethod(method, All).Invoke(menu, args); }
        T Field<T>(string name) { return (T)typeof(GameMenu).GetField(name, All).GetValue(menu); }
        bool Open(string field) { return Field<RectTransform>(field).gameObject.activeSelf; }
        static string Sel { get { var s = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null; return s != null ? s.name : "-"; } }
        static GameObject Find(string name) { return GameObject.Find(name); }
        MenuChoice Row(string title) { var g = Find("Scelta " + title); return g != null ? g.GetComponent<MenuChoice>() : null; }
        string Text(string objectName) { var g = Find(objectName); return g != null && g.GetComponent<Text>() != null ? g.GetComponent<Text>().text : "?"; }
        string Value(string row) { return Row(row) != null ? Row(row).Value : "?"; }

        // ------------------------------------------------------------------ the sequence
        IEnumerator Run()
        {
            yield return WaitFor(() => GameMenu.I != null && GameMenu.InMenu, 60f);
            menu = GameMenu.I;
            bool licenceFlow = PistaMode.HasArg("-pistamenulicence");
            if (!Check2("il menu principale e' aperto", menu != null && GameMenu.InMenu && Open("mainPanel"))) yield break;
            yield return null;

            // ---- "Gioca" asks how to play
            Call("PlayClicked");
            yield return null; yield return null;
            Check("'Gioca' apre la scelta della modalita'", Open("modePanel") && !Open("mainPanel"));
            Check("ci sono tre scelte: Standard, Il deserto, Su pista", Find("Pulsante Standard") != null && Find("Pulsante Il deserto") != null && Find("Pulsante Su pista") != null);
            var pista = Find("Pulsante Su pista").GetComponent<Button>();
            Check("'Su pista' si puo' scegliere", pista.interactable);
            if (shots) yield return Shot("menu_1_modalita");

            // ---- the setup panel (a click or a press leaves the button selected, as here)
            EventSystem.current.SetSelectedGameObject(pista.gameObject);
            pista.onClick.Invoke();
            yield return null; yield return null;
            Check("'Su pista' apre la preparazione della gara", Open("pistaPanel") && !Open("modePanel"));
            Check("il pulsante 'Via!' e' gia' scelto: Invio parte", Sel == "Pulsante Via!", Sel);
            Check("il pulsante di partenza si legge 'Via' (il carattere del menu disegna il punto esclamativo come una elle: 'Vial')", Find("Pulsante Via!").GetComponentInChildren<Text>().text == "Via");
            int seed1 = PistaMode.Chosen;
            Check("c'e' un circuito scelto", seed1 != 0, seed1.ToString());
            var map = Field<RawImage>("pistaMap");
            var tex = map.texture as Texture2D;
            int painted = tex != null ? Painted(tex) : 0;
            Check("la mappa del circuito e' disegnata nell'anteprima", tex != null && tex.width == 256 && painted > 600, tex == null ? "senza immagine" : painted + " punti");
            string name1 = Text("Nome del circuito");
            Check("il nome e la lunghezza sono scritti", name1.Length > 3 && Text("Dati del circuito").Contains(" km"), name1 + " / " + Text("Dati del circuito"));
            Check("il circuito dell'anteprima e' quello del seme che si correra'", CircuitGen.Generate(seed1).name == name1, name1);
            Check("le righe dicono i valori ricordati", Value("Giri") == (PistaMode.Laps == 1 ? "1 giro" : PistaMode.Laps + " giri") && Value("Livello") == PistaMode.RankNames[PistaMode.Rank] && Value("Moto") == Bikes.Get(PistaMode.Bike).name,
                Value("Giri") + ", " + Value("Livello") + ", " + Value("Moto"));
            if (shots) yield return Shot("menu_2_preparazione");
            if (licenceFlow) { yield return LicenceFlow(); yield break; }
            if (PistaMode.HasArg("-pistamenugarage")) { yield return GarageFlow(); yield break; }
            if (PistaMode.HasArg("-pistamenucup")) { yield return CupFlow(); yield break; }
            if (PistaMode.HasArg("-pistamenucareer")) { yield return CareerFlow(); yield break; }

            // ---- the note about the aids and the style is read whole: four lines, and its box ends before the "Altra pista" button (it used to run 20 units under it and cut the last words of a line)
            var note = Find("Nota aiuti"); var other = Find("Pulsante Altra pista");
            bool clear = false; string where = "-";
            if (note != null && other != null)
            {
                var a = new Vector3[4]; var b = new Vector3[4];
                note.GetComponent<RectTransform>().GetWorldCorners(a);
                other.GetComponent<RectTransform>().GetWorldCorners(b);
                int lines = note.GetComponent<Text>().cachedTextGenerator.lineCount;
                clear = (a[2].x <= b[0].x || a[2].y <= b[0].y || a[0].y >= b[2].y) && lines == 4;
                where = "la nota arriva a x " + a[2].x.ToString("F0") + ", 'Altra pista' parte da x " + b[0].x.ToString("F0") + ", " + lines + " righe";
            }
            Check("la nota sugli aiuti sta in quattro righe e non finisce sotto 'Altra pista'", clear, where);
            string noteText = note != null ? note.GetComponent<Text>().text : "";
            Check("la nota dice cosa fa la linea di corsa a ogni livello di aiuti, e che il peso si sposta con I J K L lo dice la barra dei tasti in gara", noteText.Contains("Linea di corsa") && noteText.Contains("Completi verde, giallo, rosso") && noteText.Contains("Sportivi i segni di frenata") && !noteText.Contains("(E / Q)"), noteText.Replace("\n", " / "));

            // ---- the rows step
            int laps0 = PistaMode.Laps;
            Row("Giri").Step(1);
            Check("'Giri' avanza al valore dopo", PistaMode.Laps == PistaMode.LapChoices[(Array.IndexOf(PistaMode.LapChoices, laps0) + 1) % PistaMode.LapChoices.Length] && Value("Giri").Contains(PistaMode.Laps.ToString()), Value("Giri"));
            while (PistaMode.Laps != 2) Row("Giri").Step(1);
            Check("'Giri' arriva a 2 giri", Value("Giri") == "2 giri", Value("Giri"));
            Row("Giri").Step(-1);
            Check("'Giri' torna indietro a 1 giro", PistaMode.Laps == 1 && Value("Giri") == "1 giro", Value("Giri"));
            while (PistaMode.Laps != 2) Row("Giri").Step(1);
            while (PistaMode.Rivals != 3) Row("Avversari").Step(1);
            Check("'Avversari' arriva a 3 avversari", Value("Avversari") == "3 avversari", Value("Avversari"));
            Row("Avversari").Step(-1);
            Check("'Avversari' a zero e' un giro a tempo", PistaMode.Rivals == 0 && Value("Avversari").Contains("Nessuno"), Value("Avversari"));
            Row("Avversari").Step(1);
            Check("e si torna a 3 avversari", PistaMode.Rivals == 3);
            while (PistaMode.Bike != 1) Row("Moto").Step(1);
            Check("'Moto' passa all'Argo 600 e la scheda lo dice", Value("Moto") == "Argo 600" && Text("Scheda moto").Contains("Supersport") && Text("Scheda moto").Contains("CV"), Text("Scheda moto"));
            Check("gli aiuti cominciano da 'Completi' (con la tastiera l'aiuto in piega tiene in sella)", PistaMode.Aids == 0 && Value("Aiuti alla guida") == "Completi", Value("Aiuti alla guida") + " (" + PistaMode.Aids + ")");
            Row("Aiuti alla guida").Step(1);
            Row("Ora del giorno").Step(1);
            Check("'Aiuti' e 'Ora del giorno' girano e rigirano", Value("Aiuti alla guida") == PistaMode.AidNames[PistaMode.Aids] && Value("Ora del giorno") == PistaMode.HourNames[PistaMode.HourChoice]);
            for (int i = 0; i < PistaMode.HourNames.Length; i++) Row("Ora del giorno").Step(1);
            for (int i = 0; i < PistaMode.AidNames.Length; i++) Row("Aiuti alla guida").Step(1);
            Check("dopo un giro intero i valori sono quelli di prima", Value("Ora del giorno") == PistaMode.HourNames[PistaMode.HourChoice]);
            Check("la riga 'Tempo' c'e', parte dal sereno e lo dice", Row("Tempo") != null && PistaMode.WeatherChoice == 0 && !PistaMode.Rain && Value("Tempo") == "Sereno", Value("Tempo"));
            Row("Tempo").Step(1);
            Check("'Tempo' passa alla pioggia e la ricorda", PistaMode.WeatherChoice == 1 && PistaMode.Rain && Value("Tempo") == "Pioggia", Value("Tempo"));
            Row("Tempo").Step(-1);
            Check("un passo indietro e' di nuovo il sereno", PistaMode.WeatherChoice == 0 && !PistaMode.Rain && Value("Tempo") == "Sereno", Value("Tempo"));
            Row("Tempo").Step(1);          // and it stays on rain: the race of the picture below is wet
            Row("Stile di guida").Step(1);
            Check("'Stile di guida' passa a 'Moto in piega' e lo ricorda", PistaMode.Style == 2 && Value("Stile di guida") == "Moto in piega", Value("Stile di guida"));
            for (int i = 0; i < PistaMode.StyleNames.Length; i++) Row("Stile di guida").Step(1);
            Check("dopo un giro intero lo stile e' quello di prima", PistaMode.Style == 2 && Value("Stile di guida") == PistaMode.StyleNames[PistaMode.Style]);
            while (PistaMode.Style != 1) Row("Stile di guida").Step(1);
            Check("'Circuito' parte da 'Casuale' e dice che e' un circuito unico", PistaMode.CircuitIndex == -1 && Value("Circuito") == "Casuale" && Text("Dati del circuito").Contains("circuito unico") && Text("Record del circuito") == "", Value("Circuito") + " / " + Text("Dati del circuito"));
            Row("Circuito").Step(1);
            string first = CircuitGen.Generate(Catalogue.All[0].seed).name;
            Check("'Circuito' passa al primo circuito con nome, lo disegna e ne dice il carattere", PistaMode.CircuitIndex == 0 && PistaMode.Chosen == Catalogue.All[0].seed && Value("Circuito") == first && Text("Nome del circuito") == first
                && Text("Dati del circuito").Contains(Catalogue.All[0].tag) && Text("Record del circuito").StartsWith("Nessun record"), Value("Circuito") + " / " + Text("Dati del circuito") + " / " + Text("Record del circuito"));
            Row("Circuito").Step(-1);
            Check("e torna a 'Casuale'", PistaMode.CircuitIndex == -1 && Value("Circuito") == "Casuale", Value("Circuito"));
            Row("Circuito").Step(-1);
            Check("all'indietro da 'Casuale' si arriva all'ultimo circuito con nome", PistaMode.CircuitIndex == Catalogue.Count - 1 && PistaMode.Chosen == Catalogue.All[Catalogue.Count - 1].seed, Value("Circuito"));
            Row("Circuito").Step(1);
            Check("e avanti si riparte da 'Casuale'", PistaMode.CircuitIndex == -1 && Value("Circuito") == "Casuale");

            // ---- another circuit
            Find("Pulsante Altra pista").GetComponent<Button>().onClick.Invoke();
            yield return null;
            int seed2 = PistaMode.Chosen;
            string name2 = Text("Nome del circuito");
            Check("'Altra pista' sceglie un altro circuito e lo disegna", seed2 != 0 && seed2 != seed1 && CircuitGen.Generate(seed2).name == name2 && Field<RawImage>("pistaMap").texture != null, seed1 + " -> " + seed2);
            if (shots) yield return Shot("menu_3_altra_pista");

            // ---- up and down walk the whole screen
            var goNav = Find("Pulsante Via!").GetComponent<Selectable>().navigation;
            var otherNav = Find("Pulsante Altra pista").GetComponent<Selectable>().navigation;
            Check("la croce in alto dal 'Via!' porta a 'Altra pista', e da li' all'ultima scelta", goNav.selectOnUp != null && goNav.selectOnUp.name == "Pulsante Altra pista" && otherNav.selectOnUp != null && otherNav.selectOnUp.name == "Scelta Tempo",
                (goNav.selectOnUp != null ? goNav.selectOnUp.name : "-") + ", " + (otherNav.selectOnUp != null ? otherNav.selectOnUp.name : "-"));
            var firstNav = Row("Circuito").navigation;
            Check("e dalla prima scelta si scende alla seconda e si sale a 'Garage'", firstNav.selectOnDown != null && firstNav.selectOnDown.name == "Scelta Giri" && firstNav.selectOnUp != null && firstNav.selectOnUp.name == "Pulsante Garage");

            // ---- back to the modes
            Call("PistaBack");
            yield return null; yield return null;
            Check("indietro porta alla scelta della modalita' con 'Su pista' ancora scelto", Open("modePanel") && !Open("pistaPanel") && Sel == "Pulsante Su pista", Sel);
            EventSystem.current.SetSelectedGameObject(Find("Pulsante Su pista"));
            Find("Pulsante Su pista").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("riaprendola il circuito e le scelte ci sono ancora", Open("pistaPanel") && PistaMode.Chosen == seed2 && Text("Nome del circuito") == name2 && PistaMode.Laps == 2 && PistaMode.Rivals == 3 && Value("Giri") == "2 giri" && PistaMode.WeatherChoice == 1 && Value("Tempo") == "Pioggia", Text("Nome del circuito"));

            // ---- "Via!": the race of the picture
            Find("Pulsante Via!").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("'Via!' toglie il menu e la partita e' su pista", !GameMenu.InMenu && PistaMode.Requested);
            yield return WaitFor(() => PistaDirector.I != null && PistaDirector.I.Ready, 150f);
            var d = PistaDirector.I;
            if (!Check2("la gara e' pronta", d != null && d.Ready && PistaMode.Active)) yield break;
            yield return null;
            Check("si corre sul circuito dell'anteprima", d.seed == seed2 && d.circuit.name == name2, d.seed + " " + d.circuit.name);
            Check("con i giri, i piloti e la moto scelti", d.race.laps == 2 && d.race.racers.Count == 4 && d.me.bikeIndex == 1, d.race.laps + " giri, " + d.race.racers.Count + " piloti, moto " + d.me.bikeIndex);
            Check("e il tempo scelto: piove (il circuito e' bagnato, la scena ha la pioggia e il terreno e' bagnato)", d.circuit.wet == 1f && d.RainOn && Shader.GetGlobalFloat("_PistaWet") == 1f, "acqua " + d.circuit.wet + ", pioggia " + d.RainOn);
            Check("il menu non blocca i comandi della gara", !GameMenu.Blocked);
            if (shots) yield return Shot("menu_4_gara");
        }

        /// <summary>The licence panel from the setup, and a test started from it.</summary>
        IEnumerator LicenceFlow()
        {
            PistaLicences.ForgetAll();
            // the player's own choices, all different from what the test will ask for: the test must win
            PistaMode.Bike = 1; PistaMode.Aids = 0; PistaMode.Laps = 5; PistaMode.Rivals = 7; PistaMode.HourChoice = 1; PistaMode.WeatherChoice = 1;
            var open = Find("Pulsante Patenti");
            var garage = Find("Pulsante Garage");
            Check("la preparazione ha il pulsante 'Patenti' (sopra 'Garage': la croce ci sale due volte dalla prima scelta)", open != null && garage != null && Row("Circuito").navigation.selectOnUp == garage.GetComponent<Selectable>()
                && garage.GetComponent<Selectable>().navigation.selectOnUp == open.GetComponent<Selectable>(), garage != null ? garage.name : "-");
            if (open == null) yield break;
            EventSystem.current.SetSelectedGameObject(open);           // a click or a press leaves the button selected, as here
            open.GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("'Patenti' apre le patenti e chiude la preparazione", Open("licencePanel") && !Open("pistaPanel"));
            string bad = "";
            for (int i = 0; i < Licences.Count; i++)
            {
                var t = Licences.All[i];
                var cell = Find("Prova " + (i + 1));
                if (cell == null) { bad += " manca" + (i + 1); continue; }
                string name = CellText(cell, "Nome"), bike = CellText(cell, "Moto"), medal = CellText(cell, "Medaglia"), time = CellText(cell, "Tempo");
                if (!name.EndsWith(t.name) || bike != Bikes.Get(t.bike).name || medal != "" || time != "da fare") bad += " " + (i + 1) + "[" + name + "|" + bike + "|" + medal + "|" + time + "]";
            }
            Check("ci sono nove prove, ognuna con il suo nome e la sua moto, e nessuna medaglia ancora", bad.Length == 0, bad);
            bool heads = true;
            for (int l = 0; l < Licences.Names.Length; l++) heads &= Text("Intestazione " + (l + 1)) == Licences.Names[l] && Text("Stato " + (l + 1)) == "0 su 3";
            Check("le tre patenti hanno il loro nome e '0 su 3'", heads, Text("Intestazione 1") + " " + Text("Stato 1"));
            var first = Licences.All[0];
            Check("la prima prova senza medaglia e' gia' scelta e sotto si legge cosa chiede", Sel == "Prova 1" && Text("Dettaglio") == first.brief && Text("Dettaglio tempi").Contains(CircuitGen.Generate(first.Seed).name)
                && Text("Dettaglio tempi").Contains("Oro " + PistaHud.Clock(first.gold)), Sel + " / " + Text("Dettaglio tempi"));
            if (shots) yield return Shot("menu_5_patenti");

            var n1 = Find("Prova 1").GetComponent<Selectable>().navigation;
            var n9 = Find("Prova 9").GetComponent<Selectable>().navigation;
            var back = Find("Pulsante Indietro").GetComponent<Selectable>().navigation;
            Check("la croce: destra passa alla prova dopo, giu' alla patente dopo, dall'ultima all''Indietro' e da li' si risale",
                n1.selectOnRight != null && n1.selectOnRight.name == "Prova 2" && n1.selectOnDown != null && n1.selectOnDown.name == "Prova 4" && n1.selectOnUp == null && n1.selectOnLeft == null
                && n9.selectOnDown != null && n9.selectOnDown.name == "Pulsante Indietro" && n9.selectOnRight == null && back.selectOnUp != null && back.selectOnUp.name == "Prova 8");
            EventSystem.current.SetSelectedGameObject(Find("Prova 5"));
            yield return null;
            var fifth = Licences.All[4];
            Check("scegliere un'altra prova cambia la riga sotto", Text("Dettaglio") == fifth.brief && Text("Dettaglio tempi").Contains("aiuti " + PistaMode.AidNames[fifth.aids].ToLowerInvariant()), Text("Dettaglio"));

            // the book: a gold, a silver and a bronze in the first licence (so it is obtained), and a time too slow for a medal in the second
            PistaLicences.Submit(0, Licences.All[0].gold - 0.5f);
            PistaLicences.Submit(1, Licences.All[1].silver - 0.5f);
            PistaLicences.Submit(2, Licences.All[2].bronze - 0.5f);
            PistaLicences.Submit(3, Licences.All[3].bronze + 8f);
            Call("RefreshLicences");
            yield return null;
            var c1 = Find("Prova 1").transform.Find("Medaglia").GetComponent<Text>();
            Check("le medaglie del libro sono sulle prove, ognuna del suo colore, e un tempo senza medaglia si legge lo stesso",
                CellText(Find("Prova 1"), "Medaglia") == "ORO" && CellText(Find("Prova 2"), "Medaglia") == "ARGENTO" && CellText(Find("Prova 3"), "Medaglia") == "BRONZO" && CellText(Find("Prova 4"), "Medaglia") == ""
                && c1.color == PistaDirector.LicenceColor(Medal.Gold).Value && CellText(Find("Prova 4"), "Tempo") == PistaHud.Clock(Licences.All[3].bronze + 8f) && CellText(Find("Prova 1"), "Tempo") == PistaHud.Clock(Licences.All[0].gold - 0.5f),
                CellText(Find("Prova 1"), "Medaglia") + " " + CellText(Find("Prova 2"), "Medaglia") + " " + CellText(Find("Prova 3"), "Medaglia") + " " + CellText(Find("Prova 4"), "Tempo"));
            Check("la prima patente e' ottenuta, la seconda no, e il riepilogo conta le medaglie", Text("Stato 1") == "OTTENUTA" && Text("Stato 2") == "0 su 3" && Text("Riepilogo") == "Patenti 1/3   -   Oro 1   Argento 1   Bronzo 1", Text("Stato 1") + " / " + Text("Stato 2") + " / " + Text("Riepilogo"));
            if (shots) yield return Shot("menu_6_patenti_medaglie");

            // back to the setup (the button that opened the licences is still the chosen one), and open them again: the first test without a medal is chosen
            Call("LicenceBack");
            yield return null; yield return null;
            Check("indietro porta alla preparazione con il pulsante 'Patenti' ancora scelto", Open("pistaPanel") && !Open("licencePanel") && Sel == "Pulsante Patenti", Sel);
            EventSystem.current.SetSelectedGameObject(Find("Pulsante Patenti"));
            Find("Pulsante Patenti").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("riaprendole e' scelta la prima prova senza medaglia", Open("licencePanel") && Sel == "Prova 4", Sel);

            // a click starts the test: its circuit, bike, aids, hour and three laps win over the choices of the setup, and nobody else is on the grid
            const int index = 4;
            var test = Licences.All[index];
            EventSystem.current.SetSelectedGameObject(Find("Prova " + (index + 1)));
            Find("Prova " + (index + 1)).GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("un clic su una prova toglie il menu e la partita e' su pista", !GameMenu.InMenu && PistaMode.Requested && PistaMode.Licence == index, "prova " + PistaMode.Licence);
            yield return WaitFor(() => PistaDirector.I != null && PistaDirector.I.Ready, 150f);
            var d = PistaDirector.I;
            if (!Check2("la gara e' pronta", d != null && d.Ready && PistaMode.Active)) yield break;
            yield return null;
            Check("si corre sul circuito della prova, con la sua moto, tre giri e nessun avversario (le scelte della preparazione non contano)",
                d.seed == test.Seed && d.me.bikeIndex == test.bike && d.race.laps == Licences.Laps && d.race.racers.Count == 1,
                "seme " + d.seed + " (prova " + test.Seed + "), moto " + d.me.bikeIndex + " (" + test.bike + "), " + d.race.laps + " giri, " + d.race.racers.Count + " piloti");
            Check("gli aiuti e l'ora sono quelli della prova", d.me.bike.tcs == (test.aids < 2) && d.me.bike.leanAssist == (test.aids == 0) && PistaMode.Aids == test.aids && PistaMode.HourChoice == test.hour,
                "aiuti " + PistaMode.Aids + " (prova " + test.aids + "), ora " + PistaMode.HourChoice + " (prova " + test.hour + ")");
            Check("la prova e' sempre sull'asciutto, anche se la preparazione dice pioggia: i tempi delle medaglie sono fatti sull'asciutto", PistaMode.WeatherChoice == 0 && !PistaMode.Rain && d.circuit.wet == 0f && !d.RainOn && Shader.GetGlobalFloat("_PistaWet") == 0f,
                "tempo " + PistaMode.WeatherChoice + ", acqua " + d.circuit.wet + ", pioggia " + d.RainOn);
            Check("il regista sa che e' la prova e il registro e' ancora quello della preparazione", d.Licence != null && d.Licence.test.id == test.id && PistaLicences.Book.MedalOf(0) == Medal.Gold, d.Licence != null ? d.Licence.test.id : "nessuna prova");
            if (shots) yield return Shot("menu_7_patente_gara");
        }


        /// <summary>The rig wears this rider (the leathers, their second tone and the helmet) on a bike with this paint.</summary>
        static bool RigWears(PistaBikeRig rig, Livery rider, Color paint)
        {
            return rig != null && rig.suit != null && rig.suit2 != null && rig.helmet != null && rig.paint != null
                && rig.suit.GetColor("_BaseColor") == rider.suit && rig.suit2.GetColor("_BaseColor") == rider.suit2 && rig.helmet.GetColor("_BaseColor") == rider.helmet && rig.paint.GetColor("_BaseColor") == paint;
        }

        /// <summary>What is wrong with the paint of a rig, as a sentence (empty when nothing): every painted material wears the PistaPaint shader (and the machine supports it), and every part drawn with it has a mesh
        /// tagged with the number the shader expects for that part (a wrong tag would draw a leg with the pattern of a helmet).</summary>
        static string PaintProblems(PistaBikeRig rig)
        {
            if (rig == null) return "nessuna moto";
            var bad = new System.Text.StringBuilder();
            foreach (var m in new[] { rig.paint, rig.paint2, rig.suit, rig.suit2, rig.helmet })
                if (m == null || m.shader == null || m.shader.name != "PryingEyes/PistaPaint" || !m.shader.isSupported)
                    bad.Append(" materiale " + (m == null ? "mancante" : m.name + " con " + (m.shader != null ? m.shader.name : "-")));
            var want = new System.Collections.Generic.Dictionary<string, int> { { "Vernice", 0 }, { "Vernice2", 1 }, { "Parafango", 1 }, { "Tuta", 2 }, { "Gobba e spalle", 3 }, { "Coscia e stinco", 4 }, { "Ginocchio", 5 }, { "Casco", 8 }, { "Gomma", 9 } };
            int painted = 0;
            foreach (var r in rig.root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                // the rider drawn in Blender (PistaRiderModel) names its parts by material: PistaPaint's _Part (8 the helmet, 20-27 the suit and its pieces)
                bool paintMat = false, byMaterial = false;
                foreach (var pm in r.sharedMaterials)
                {
                    if (pm == null || pm.shader == null || pm.shader.name != "PryingEyes/PistaPaint") continue;
                    paintMat = true;
                    if (pm.HasProperty("_Part") && pm.GetFloat("_Part") >= 0f)
                    {
                        byMaterial = true;
                        int part = Mathf.RoundToInt(pm.GetFloat("_Part"));
                        if (part != 0 && part != 1 && part != 8 && part != 9 && part != 30 && part != 31 && (part < 20 || part > 27)) bad.Append(" " + r.name + " ha una parte " + part + " che la vernice non conosce");
                    }
                }
                // the body of the bike drawn in Blender carries the light that reaches each vertex in its colour (the green channel): a file that lost it on the way in (no colours at all) would draw the bike without occlusion
                if (r.name == "body" && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null)
                {
                    var bodyMesh = r.GetComponent<MeshFilter>().sharedMesh;
                    var bodyColours = bodyMesh.colors32;
                    int shaded = 0;
                    if (bodyColours != null && bodyColours.Length == bodyMesh.vertexCount) foreach (var bc in bodyColours) if (bc.g < 235) shaded++;
                    if (shaded < bodyMesh.vertexCount / 100) bad.Append(" il corpo del modello non porta l'occlusione cotta (" + shaded + " vertici in ombra su " + bodyMesh.vertexCount + ")");
                }
                // the rider drawn in Blender carries his own occlusion too (the arms against the trunk, the thighs against the pelvis): the torso is the part that has the most of it
                if (r.name == "torso" && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null)
                {
                    var torsoMesh = r.GetComponent<MeshFilter>().sharedMesh;
                    var torsoColours = torsoMesh.colors32;
                    int shaded = 0;
                    if (torsoColours != null && torsoColours.Length == torsoMesh.vertexCount) foreach (var tc in torsoColours) if (tc.g < 235) shaded++;
                    if (shaded < torsoMesh.vertexCount / 100) bad.Append(" il busto del pilota non porta l'occlusione cotta (" + shaded + " vertici in ombra su " + torsoMesh.vertexCount + ")");
                }
                if (!paintMat) continue;
                painted++;
                if (byMaterial) continue;
                var mat = r.sharedMaterial;
                int expect;
                if (r.name.StartsWith("Braccio")) expect = 6;
                else if (r.name.StartsWith("Avambraccio")) expect = 7;
                else if (!want.TryGetValue(r.name, out expect)) { bad.Append(" parte senza numero: " + r.name); continue; }
                var mesh = r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
                var c = mesh != null ? mesh.colors32 : null;
                if (c == null || c.Length == 0 || c.Length != mesh.vertexCount || c[0].r != expect || c[c.Length - 1].r != expect) bad.Append(" " + r.name + " non e' marcata " + expect);
            }
            if (painted < 16) bad.Append(" solo " + painted + " parti dipinte (ne servono 16: vernice, vernice2, parafango, tuta, gobba, due gambe, due ginocchia, due braccia, due avambracci, casco, due gomme)");
            return bad.ToString();
        }

        /// <summary>How many pixels of a render texture are the magenta of the error shader (a shader that did not compile draws everything with it).</summary>
        static int MagentaPixels(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            int n = 0;
            foreach (var p in copy.GetPixels32()) if (p.r > 170 && p.b > 170 && p.g < 90) n++;
            Destroy(copy);
            return n;
        }

        /// <summary>The garage from the setup: the showroom and the bars, the choice of bike, colours and rider, back to the setup, and the colours and the outfit worn in the race.</summary>
        IEnumerator GarageFlow()
        {
            PistaMode.Bike = 0; PistaMode.Skin = -1; PistaMode.Outfit = -1; PistaMode.Aids = 1; PistaMode.Rivals = 3; PistaMode.Laps = 1;
            var open = Find("Pulsante Garage");
            var licences = Find("Pulsante Patenti");
            var up = Row("Circuito").navigation.selectOnUp;
            Check("la preparazione ha il pulsante 'Garage' sotto 'Patenti' e la croce ci sale dalla prima scelta",
                open != null && licences != null && up != null && up.gameObject == open && licences.GetComponent<Selectable>().navigation.selectOnDown != null && licences.GetComponent<Selectable>().navigation.selectOnDown.gameObject == open, up != null ? up.name : "-");
            if (open == null) yield break;
            EventSystem.current.SetSelectedGameObject(open);           // a click or a press leaves the button selected, as here
            open.GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null; yield return null;
            Check("'Garage' apre il garage e chiude la preparazione, e la scelta della moto e' gia' scelta", Open("garagePanel") && !Open("pistaPanel") && Sel == "Scelta Moto", Sel);
            var stage = menu.GarageStageForTest;
            Check("la vetrina e' accesa: una camera sua, una texture sua, la moto 0 sul piatto", stage != null && stage.Picture != null && stage.Cam != null && stage.Active && stage.Bike == 0 && stage.Rig != null && Field<RawImage>("garagePicture").texture == stage.Picture,
                stage == null ? "nessuna vetrina" : "moto " + stage.Bike);
            var f0 = BikeSheet.Of(0);
            Check("il nome, la frase e le sette cifre sono quelli della moto", Text("Nome moto") == Bikes.Get(0).name + "  -  " + Bikes.Get(0).klass && Text("Descrizione moto") == BikeSheet.Blurb(0)
                && Text("Cifra Velocita'") == Mathf.RoundToInt(f0.topKmh) + " km/h" && Text("Cifra Potenza").EndsWith(" CV") && Text("Cifra Coppia") == Mathf.RoundToInt(f0.torque) + " Nm" && Text("Cifra Ripresa").Contains("0-100") && Text("Cifra Frenata").Contains("100-0")
                && Text("Cifra Leggerezza") == Mathf.RoundToInt(f0.kg) + " kg" && Text("Cifra Maneggevolezza") == BikeSheet.HandlingWord(f0), Text("Nome moto") + " / " + Text("Cifra Velocita'") + " / " + Text("Cifra Ripresa"));
            string bars = "";
            for (int i = 0; i < BikeSheet.BarCount; i++)
            {
                var fill = Find("Riempimento " + BikeSheet.BikeBarNames[i]);
                float w = fill != null ? fill.GetComponent<RectTransform>().sizeDelta.x : -1f, want = Mathf.Max(2f, 250f * BikeSheet.Value(f0, (BikeBar)i));
                if (fill == null || Mathf.Abs(w - want) > 0.5f) bars += " " + BikeSheet.BikeBarNames[i] + ":" + w + "/" + want;
            }
            Check("le sette barre sono lunghe quanto dicono le cifre", bars.Length == 0, bars);
            stage.SetSpin(38f);
            for (int i = 0; i < 4; i++) yield return null;
            string stats;
            float lit = LitShare(stage.Picture, shots ? "garage_vetrina_" + PistaMode.Bike : null, out stats);
            Check("la vetrina disegna la moto (non e' un riquadro vuoto)", lit > 0.03f && lit < 0.80f, "pixel accesi " + (lit * 100f).ToString("F1") + " %, " + stats);
            string paintBad = PaintProblems(stage.Rig);
            int pink = MagentaPixels(stage.Picture);
            Check("la vernice, la tuta e il casco usano lo shader della vernice, ogni parte e' marcata col suo numero e la vetrina non ha pixel rosa (lo shader che non compila)", paintBad.Length == 0 && pink == 0,
                (paintBad.Length == 0 ? "tutto a posto" : paintBad) + ", pixel rosa " + pink);
            var seenPattern = new bool[6]; bool liveriesOk = true; int matts = 0;
            for (int i = 0; i < Livery.Count; i++)
            {
                var l = Livery.For(i);
                if (l.matt) matts++;
                if (l.pattern < 0 || l.pattern > 5 || l.suitPattern < 0 || l.suitPattern > 4 || l.helmetPattern < 0 || l.helmetPattern > 3 || l.flake < 0f || l.flake > 1f) liveriesOk = false; else seenPattern[l.pattern] = true;
            }
            Check("le dodici livree hanno disegni validi, tutti e sei i disegni della moto si vedono almeno una volta e solo la nera e' opaca",
                liveriesOk && Array.TrueForAll(seenPattern, x => x) && matts == 1 && Livery.For(6).matt, liveriesOk ? "disegni della moto " + string.Join("", Array.ConvertAll(seenPattern, x => x ? "1" : "0")) : "un disegno fuori misura");
            if (shots) yield return Shot("menu_8_garage");

            // the bike: the row steps, the showroom follows, the figures change
            string speed0 = Text("Cifra Velocita'");
            while (PistaMode.Bike != 3) Row("Moto").Step(1);
            var f3 = BikeSheet.Of(3);
            Check("'Moto' passa alla Scintilla 125: il nome, la frase, le cifre e la moto sul piatto cambiano", Text("Nome moto").StartsWith(Bikes.Get(3).name) && Text("Descrizione moto") == BikeSheet.Blurb(3) && Text("Cifra Velocita'") == Mathf.RoundToInt(f3.topKmh) + " km/h"
                && Text("Cifra Velocita'") != speed0 && Text("Cifra Leggerezza") == Mathf.RoundToInt(f3.kg) + " kg" && stage.Bike == 3 && Value("Moto") == Bikes.Get(3).name, Text("Nome moto") + " / " + Text("Cifra Velocita'") + " / moto sul piatto " + stage.Bike);

            // the colours: 'Di serie' first, then the twelve liveries, round again
            Check("'Colori' parte da 'Di serie' e il campione e' quello della moto", Value("Colori") == "Di serie" && PistaMode.Skin == -1 && Find("Colore principale").GetComponent<Image>().color == Livery.For(Bikes.Get(3).skin).main, Value("Colori"));
            Row("Colori").Step(1);
            Check("avanti: la prima livrea, e la moto sul piatto la indossa", PistaMode.Skin == 0 && Value("Colori") == Livery.For(0).name && stage.Skin == 0 && Find("Colore principale").GetComponent<Image>().color == Livery.For(0).main
                && Find("Colore secondario").GetComponent<Image>().color == Livery.For(0).accent, Value("Colori"));
            Row("Colori").Step(-1); Row("Colori").Step(-1);
            Check("all'indietro da 'Di serie' si arriva all'ultima livrea", PistaMode.Skin == Livery.Count - 1 && Value("Colori") == Livery.For(Livery.Count - 1).name, Value("Colori"));
            PistaMode.Skin = 4; Call("RefreshGarage");
            yield return null;
            Check("la livrea scelta resta scelta anche cambiando moto", Value("Colori") == Livery.For(4).name && stage.Skin == 4, Value("Colori"));

            // the rider: 'Come la moto' first, then the leathers and the helmet of the twelve liveries; the paint of the bike stays as it was chosen
            var own = Livery.For(4);
            Check("'Pilota' parte da 'Come la moto': la tuta e il casco sono quelli della livrea della moto, sul piatto e nei tre campioni",
                Value("Pilota") == "Come la moto" && PistaMode.Outfit == -1 && stage.Outfit == -1 && RigWears(stage.Rig, own, own.main)
                && Find("Colore tuta").GetComponent<Image>().color == own.suit && Find("Colore tuta secondario").GetComponent<Image>().color == own.suit2 && Find("Colore casco").GetComponent<Image>().color == own.helmet, Value("Pilota"));
            Row("Pilota").Step(1);
            yield return null;
            var first = Livery.For(0);
            Check("avanti: il pilota indossa la tuta e il casco della prima livrea, e la vernice della moto resta quella scelta",
                PistaMode.Outfit == 0 && Value("Pilota") == first.name && stage.Outfit == 0 && RigWears(stage.Rig, first, own.main) && Value("Colori") == own.name
                && Find("Colore tuta").GetComponent<Image>().color == first.suit && Find("Colore tuta secondario").GetComponent<Image>().color == first.suit2 && Find("Colore casco").GetComponent<Image>().color == first.helmet, Value("Pilota"));
            Row("Pilota").Step(-1); Row("Pilota").Step(-1);
            Check("all'indietro da 'Come la moto' si arriva all'ultima livrea", PistaMode.Outfit == Livery.Count - 1 && Value("Pilota") == Livery.For(Livery.Count - 1).name, Value("Pilota"));
            PistaMode.Outfit = 7; PistaMode.Bike = 0; Call("RefreshGarage");
            yield return null;
            Check("l'abbigliamento scelto resta scelto anche cambiando moto", Value("Pilota") == Livery.For(7).name && stage.Outfit == 7 && stage.Bike == 0 && Value("Colori") == own.name, Value("Pilota"));
            PistaMode.Bike = 3; Call("RefreshGarage");
            yield return null;
            stage.SetSpin(300f);
            for (int i = 0; i < 4; i++) yield return null;
            if (shots) yield return Shot("menu_9_garage_125");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pistabikeshots") >= 0) yield return BikeShots(stage);

            var bikeNav = Row("Moto").navigation; var colourNav = Row("Colori").navigation; var outfitNav = Row("Pilota").navigation; var doneNav = Find("Pulsante Fatto").GetComponent<Selectable>().navigation;
            Check("la croce: da 'Moto' si scende a 'Colori', a 'Pilota' e a 'Fatto', e da 'Fatto' si risale a 'Pilota'", bikeNav.selectOnDown != null && bikeNav.selectOnDown.name == "Scelta Colori" && colourNav.selectOnUp != null && colourNav.selectOnUp.name == "Scelta Moto"
                && colourNav.selectOnDown != null && colourNav.selectOnDown.name == "Scelta Pilota" && outfitNav.selectOnUp != null && outfitNav.selectOnUp.name == "Scelta Colori"
                && outfitNav.selectOnDown != null && outfitNav.selectOnDown.name == "Pulsante Fatto" && doneNav.selectOnUp != null && doneNav.selectOnUp.name == "Scelta Pilota");

            // back to the setup: the button that opened the garage is the chosen one, the setup says the bike, the showroom is off
            Call("GarageBack");
            yield return null; yield return null;
            Check("'Fatto' porta alla preparazione con 'Garage' ancora scelto, la moto nella riga e la vetrina spenta", Open("pistaPanel") && !Open("garagePanel") && Sel == "Pulsante Garage" && Value("Moto") == Bikes.Get(3).name && !stage.Active, Sel + " / " + Value("Moto"));

            // the race wears the colours of the garage, and no computer rider has them
            Find("Pulsante Via!").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            yield return WaitFor(() => PistaDirector.I != null && PistaDirector.I.Ready, 150f);
            var d = PistaDirector.I;
            if (!Check2("la gara e' pronta", d != null && d.Ready && PistaMode.Active)) yield break;
            yield return null;
            bool borrowed = false;
            foreach (var r in d.race.racers) if (r != d.me && r.skin == 4) borrowed = true;
            Check("si corre con la moto e i colori del garage, e nessun avversario ha quei colori", d.me.bikeIndex == 3 && d.me.skin == 4 && !borrowed, "moto " + d.me.bikeIndex + ", livrea " + d.me.skin);
            var myRig = d.views[d.race.racers.IndexOf(d.me)].rig;
            Check("il pilota in gara indossa la tuta e il casco scelti nel garage, su una moto con la vernice scelta", RigWears(myRig, Livery.For(7), Livery.For(4).main), "abbigliamento " + PistaMode.Outfit);
        }

        /// <summary>The career from the garage: off, then on (the bike chosen becomes one that is owned and the button says the money), the plate going to bikes that are not owned with their price and "Compra", a purchase that
        /// the money does not cover and one that it does, the workshop (a level of tuning for its price, the top level, a licence that lends the bike as it comes), the setup panel that only steps through the bikes owned, the panel of a cooperative game that ignores the career, and the cups (one without a bike of its class cannot start).</summary>
        IEnumerator CareerFlow()
        {
            PistaCareer.ForgetAll(); PistaCups.ForgetAll();
            PistaMode.CareerOn = false;
            PistaMode.Bike = 0; PistaMode.Skin = -1; PistaMode.Outfit = -1; PistaMode.Aids = 1; PistaMode.Rivals = 3; PistaMode.Laps = 1; PistaMode.Rank = 1;
            var open = Find("Pulsante Garage");
            if (open == null) { Check("la preparazione ha il pulsante Garage", false); yield break; }
            EventSystem.current.SetSelectedGameObject(open);
            open.GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null; yield return null;
            var stage = menu.GarageStageForTest;
            Check("il garage ha il pulsante della carriera, spenta: dice cosi', non c'e' 'Compra' e il nome non ha prezzo",
                Open("garagePanel") && Find("Pulsante Carriera") != null && CareerLabel() == "Carriera: spenta" && Find("Pulsante Compra") == null && Text("Nome moto") == Bikes.Get(0).name + "  -  " + Bikes.Get(0).klass && !PistaMode.CareerOn, CareerLabel() + " / " + Text("Nome moto"));
            Row("Moto").Step(1);
            yield return null;
            Check("senza carriera tutte le moto sono tue: la riga passa all'Argo 600 e la scelta la segue", PistaMode.Bike == 1 && stage.Bike == 1 && Find("Pulsante Compra") == null, "moto " + PistaMode.Bike);
            if (shots) yield return Shot("menu_14_garage_libero");

            // the career on: the bike chosen is read again, and only a bike owned can be
            Find("Pulsante Carriera").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("accesa la carriera: il pulsante dice il conto, la moto scelta diventa quella che si ha (la Scintilla 125) e sul piatto c'e' lei, 'tua'",
                PistaMode.CareerOn && CareerLabel() == "Carriera: " + Economy.Format(Economy.StartMoney) + " cr" && PistaMode.Bike == Economy.StarterBike && stage.Bike == Economy.StarterBike && Text("Nome moto") == "Scintilla 125  -  125 GP  -  tua" && Find("Pulsante Compra") == null,
                CareerLabel() + " / " + Text("Nome moto") + " / moto " + PistaMode.Bike);
            Check("la nota dice cosa e' cambiato", Text("Nota carriera").StartsWith("Carriera accesa"), Text("Nota carriera"));

            // window shopping: the plate goes to a bike that is not owned, the bike chosen does not
            Row("Moto").Step(1);
            yield return null;
            int tigre = 4, tigrePrice = Economy.Price(tigre);
            Check("sfogliando, il piatto va alla moto dopo e ne dice il prezzo, ma la moto scelta resta quella che si ha", stage.Bike == tigre && PistaMode.Bike == Economy.StarterBike && Value("Moto") == Bikes.Get(tigre).name
                && Text("Nome moto") == Bikes.Get(tigre).name + "  -  " + Bikes.Get(tigre).klass + "  -  " + Economy.Format(tigrePrice) + " cr", Text("Nome moto") + " / scelta " + PistaMode.Bike);
            var bikeNav = Row("Moto").navigation; var outfitNav = Row("Pilota").navigation; var doneNav = Find("Pulsante Fatto").GetComponent<Selectable>().navigation;
            var buy = Find("Pulsante Compra");
            Check("compare 'Compra' con il prezzo, e la croce lo mette tra 'Pilota' e 'Fatto'", buy != null && buy.GetComponentInChildren<Text>().text == "Compra  -  " + Economy.Format(tigrePrice) + " cr" && outfitNav.selectOnDown != null && outfitNav.selectOnDown.name == "Pulsante Compra"
                && buy.GetComponent<Selectable>().navigation.selectOnDown != null && buy.GetComponent<Selectable>().navigation.selectOnDown.name == "Pulsante Fatto" && doneNav.selectOnUp != null && doneNav.selectOnUp.name == "Pulsante Compra"
                && bikeNav.selectOnUp != null && bikeNav.selectOnUp.name == "Pulsante Carriera", buy != null ? buy.GetComponentInChildren<Text>().text : "senza pulsante");
            if (shots) yield return Shot("menu_15_garage_compra");

            // not enough money: it says what is missing and nothing changes
            buy.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Check("senza i soldi la moto non si compra: la nota dice quanto manca, il conto e le moto sono quelli di prima e 'Compra' c'e' ancora",
                !PistaCareer.Book.Owns(tigre) && PistaCareer.Book.Money == Economy.StartMoney && Text("Nota carriera") == "Ti mancano " + Economy.Format(tigrePrice - Economy.StartMoney) + " cr per la " + Bikes.Get(tigre).name && Find("Pulsante Compra") != null && PistaMode.Bike == Economy.StarterBike,
                Text("Nota carriera"));

            // the money arrives and it is bought
            PistaCareer.Pay(7000);
            Call("RefreshGarage");
            yield return null;
            int rest = Economy.StartMoney + 7000 - tigrePrice;
            Check("arrivati i soldi il pulsante della carriera dice il conto nuovo", CareerLabel() == "Carriera: " + Economy.Format(Economy.StartMoney + 7000) + " cr", CareerLabel());
            Find("Pulsante Compra").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("comprata: e' tua (il conto scende del prezzo), e' la moto scelta, 'Compra' sparisce, la nota lo dice e la croce torna alla riga della moto",
                PistaCareer.Book.Owns(tigre) && PistaCareer.Book.Money == rest && PistaMode.Bike == tigre && Find("Pulsante Compra") == null && Text("Nota carriera") == "Comprata: " + Bikes.Get(tigre).name + "  -  conto " + Economy.Format(rest) + " cr"
                && CareerLabel() == "Carriera: " + Economy.Format(rest) + " cr" && Text("Nome moto").EndsWith("tua") && Sel == "Scelta Moto", Text("Nota carriera") + " / " + CareerLabel() + " / " + Sel);
            Row("Moto").Step(1);
            yield return null;
            Check("la moto dopo non e' tua: la scelta resta quella appena comprata e 'Compra' torna", stage.Bike == tigre + 1 && PistaMode.Bike == tigre && Find("Pulsante Compra") != null, "piatto " + stage.Bike + ", scelta " + PistaMode.Bike);
            Row("Moto").Step(-1);
            yield return null;
            Check("e tornando indietro 'Compra' non c'e' piu'", stage.Bike == tigre && PistaMode.Bike == tigre && Find("Pulsante Compra") == null);

            // the workshop: a bike that is owned has "Potenzia" with the price of its next level, and the line above it says what that level gives
            int tigreTune1 = Economy.TunePrice(tigre, 1), tigreTune2 = Economy.TunePrice(tigre, 2), tigreTune3 = Economy.TunePrice(tigre, 3);
            string lightBefore = Text("Cifra Leggerezza");
            var tune = Find("Pulsante Potenzia");
            Check("una moto che si ha ha 'Potenzia' con il prezzo del primo livello, la nota dice cosa dara' e la croce lo mette tra 'Pilota' e 'Fatto', come 'Compra'",
                tune != null && tune.GetComponentInChildren<Text>().text == "Potenzia  -  " + Economy.Format(tigreTune1) + " cr" && Find("Pulsante Compra") == null && Text("Nota carriera") == "Livello 1:   " + Tuning.Describe(1)
                && Row("Pilota").navigation.selectOnDown != null && Row("Pilota").navigation.selectOnDown.name == "Pulsante Potenzia"
                && tune.GetComponent<Selectable>().navigation.selectOnDown != null && tune.GetComponent<Selectable>().navigation.selectOnDown.name == "Pulsante Fatto"
                && Find("Pulsante Fatto").GetComponent<Selectable>().navigation.selectOnUp != null && Find("Pulsante Fatto").GetComponent<Selectable>().navigation.selectOnUp.name == "Pulsante Potenzia",
                tune != null ? tune.GetComponentInChildren<Text>().text + " / " + Text("Nota carriera") : "senza pulsante");
            if (shots) yield return Shot("menu_18_garage_potenzia");

            // not enough money: it says what is missing and nothing changes
            tune.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Check("senza i soldi la moto non si potenzia: la nota dice quanto manca, il conto e il livello sono quelli di prima e 'Potenzia' c'e' ancora",
                PistaCareer.Book.Level(tigre) == 0 && PistaCareer.Book.Money == rest && Text("Nota carriera") == "Ti mancano " + Economy.Format(tigreTune1 - rest) + " cr per il livello 1 della " + Bikes.Get(tigre).name && Find("Pulsante Potenzia") != null && Text("Nome moto").EndsWith("tua"),
                Text("Nota carriera"));

            // the money arrives and one level is bought: the figures, the name and the sentence are those of the bike as it is now
            PistaCareer.Pay(5000);
            Call("RefreshGarage");
            yield return null;
            int money1 = rest + 5000;
            Find("Pulsante Potenzia").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            var figure1 = BikeSheet.Of(tigre, 1);
            Check("potenziata: il livello sale e il conto scende del prezzo, la nota lo dice, il nome dice il livello, la frase aggiunge la messa a punto e il peso e' quello della moto potenziata; 'Potenzia' chiede il secondo livello",
                PistaCareer.Book.Level(tigre) == 1 && PistaCareer.Book.Money == money1 - tigreTune1
                && Text("Nota carriera") == "Potenziata: " + Bikes.Get(tigre).name + ", livello 1  -  conto " + Economy.Format(money1 - tigreTune1) + " cr"
                && Text("Nome moto") == Bikes.Get(tigre).name + "  -  " + Bikes.Get(tigre).klass + "  -  tua  -  livello 1"
                && Text("Descrizione moto").Contains("Messa a punto 1 di 3:   " + Tuning.Describe(1))
                && Text("Cifra Leggerezza") == Mathf.RoundToInt(figure1.kg) + " kg" && Text("Cifra Leggerezza") != lightBefore
                && Find("Pulsante Potenzia") != null && Find("Pulsante Potenzia").GetComponentInChildren<Text>().text == "Potenzia  -  " + Economy.Format(tigreTune2) + " cr" && CareerLabel() == "Carriera: " + Economy.Format(money1 - tigreTune1) + " cr",
                Text("Nota carriera") + " / " + Text("Nome moto") + " / " + Text("Cifra Leggerezza") + " (prima " + lightBefore + ")");

            // up to the top level: the button is gone, the cross is back on the row of the bike, the account has paid the three levels
            PistaCareer.Pay(10000);
            Call("RefreshGarage");
            yield return null;
            Find("Pulsante Potenzia").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Find("Pulsante Potenzia").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            int money3 = money1 - tigreTune1 + 10000 - tigreTune2 - tigreTune3;
            Check("al terzo livello e' al massimo: 'Potenzia' sparisce, la croce torna alla riga della moto e il conto e' sceso dei tre prezzi",
                PistaCareer.Book.Level(tigre) == Tuning.MaxLevel && PistaCareer.Book.Money == money3 && Find("Pulsante Potenzia") == null && Sel == "Scelta Moto"
                && Text("Nome moto").EndsWith("tua  -  livello 3") && Text("Nota carriera") == "Potenziata: " + Bikes.Get(tigre).name + ", livello 3  -  conto " + Economy.Format(money3) + " cr",
                Text("Nota carriera") + " / " + Sel + " / " + Text("Nome moto"));
            if (shots) yield return Shot("menu_19_garage_livello3");

            // every bike has its own level: the one before is as it came
            Row("Moto").Step(-1);
            yield return null;
            Check("la Scintilla 125 e' ancora come e' venuta: niente livello, e 'Potenzia' chiede il prezzo del suo primo livello",
                stage.Bike == Economy.StarterBike && Text("Nome moto") == "Scintilla 125  -  125 GP  -  tua" && Find("Pulsante Potenzia") != null
                && Find("Pulsante Potenzia").GetComponentInChildren<Text>().text == "Potenzia  -  " + Economy.Format(Economy.TunePrice(Economy.StarterBike, 1)) + " cr", Text("Nome moto"));
            Row("Moto").Step(1);
            yield return null;

            // what the race takes: the tuned copy with its own key and a lighter bike; a licence lends the bike as it comes, even one that is owned and tuned
            var raced = PistaMode.SpecFor(tigre);
            Check("la moto che la gara prende e' quella potenziata: la chiave dice il livello, pesa meno, e le altre moto e quelle di serie non sono toccate",
                raced.id == Bikes.Get(tigre).id + "+3" && raced.tune == Tuning.MaxLevel && raced.bikeMass < Bikes.Get(tigre).bikeMass && PistaMode.TuneOf(Economy.StarterBike) == 0
                && PistaMode.SpecFor(Economy.StarterBike).id == Bikes.Get(Economy.StarterBike).id && Bikes.Get(tigre).bikeMass == 181f, raced.id + ", " + raced.bikeMass.ToString("F1") + " kg");
            int chosenSeed = PistaMode.Chosen;
            PistaMode.BeginLicence(0);
            bool lent = PistaMode.TuneOf(tigre) == 0 && PistaMode.SpecFor(tigre).id == Bikes.Get(tigre).id && PistaMode.SpecFor(tigre).tune == 0;
            PistaMode.End();
            PistaMode.Chosen = chosenSeed;
            Check("una patente presta la moto com'e': anche una moto che si ha e si e' potenziata e' di serie in prova, e a prova finita il livello torna", lent && PistaMode.TuneOf(tigre) == Tuning.MaxLevel, "in prova " + lent + ", dopo livello " + PistaMode.TuneOf(tigre));

            // the setup panel: the subtitle says the money, and the row only goes through the bikes owned
            Call("GarageBack");
            yield return null; yield return null;
            Check("la preparazione dice il conto sotto il titolo e la riga 'Moto' mostra la moto scelta", Open("pistaPanel") && Text("Sottotitolo").EndsWith("Carriera: conto " + Economy.Format(PistaCareer.Book.Money) + " cr") && Value("Moto") == Bikes.Get(tigre).name, Text("Sottotitolo") + " / " + Value("Moto"));
            if (shots) { yield return new WaitForSecondsRealtime(0.5f); yield return Shot("menu_16_preparazione_carriera"); }          // after the panel has faded in
            Check("la scheda della moto, alla preparazione, e' quella della moto potenziata: dice il livello e il peso sceso",
                Text("Scheda moto").StartsWith(Bikes.Get(tigre).name + " liv. 3  -  " + Bikes.Get(tigre).klass) && Text("Scheda moto").Contains(Mathf.RoundToInt(PistaMode.SpecFor(tigre).Mass) + " kg con il pilota"), Text("Scheda moto"));
            Row("Moto").Step(1);
            Check("la riga 'Moto' salta le moto che non si hanno: dalla Tigre 675 si torna alla Scintilla 125", PistaMode.Bike == Economy.StarterBike && Value("Moto") == Bikes.Get(Economy.StarterBike).name, Value("Moto"));
            Row("Moto").Step(1);
            Check("e da li' alla Tigre 675", PistaMode.Bike == tigre, Value("Moto"));
            Row("Moto").Step(-1);
            Check("all'indietro lo stesso", PistaMode.Bike == Economy.StarterBike, Value("Moto"));
            PistaMode.Bike = 0;          // a bike that is not owned, left by an earlier choice: read as the first one that is
            Check("una scelta di prima su una moto che non si ha vale la prima che si ha", PistaMode.Bike == Economy.StarterBike && PistaMode.StepBike(PistaMode.Bike, 1) == tigre, "moto " + PistaMode.Bike);

            // the panel of a cooperative game: the host picks the bike of the whole table, so the career does not limit it and says nothing of money
            PistaMode.ForCoop = true;
            PistaMode.Bike = 2;          // the Vigile 750: not owned
            Call("RefreshPista", false);
            yield return null;
            Check("per la cooperativa la carriera non limita la scelta: la moto scelta e' quella scelta anche se non si ha, e la riga passa a tutte", PistaMode.Bike == 2 && !PistaMode.CareerApplies && PistaMode.StepBike(2, 1) == 3 && PistaMode.StepBike(2, -1) == 1, "moto " + PistaMode.Bike);
            Check("e la preparazione per la cooperativa non dice il conto", !Text("Sottotitolo").Contains("Carriera"), Text("Sottotitolo"));
            Check("e la moto della cooperativa e' di serie: nessun livello, anche su una moto che si e' potenziata, e la scheda non lo dice", PistaMode.TuneOf(tigre) == 0 && PistaMode.SpecFor(tigre).id == Bikes.Get(tigre).id && !Text("Scheda moto").Contains("liv."), PistaMode.SpecFor(tigre).id + " / " + Text("Scheda moto"));
            PistaMode.ForCoop = false;
            Call("RefreshPista", false);
            yield return null;
            Check("finita la cooperativa la carriera torna: la moto che non si ha vale di nuovo la prima che si ha, e il conto e' sotto il titolo", PistaMode.CareerApplies && PistaMode.Bike == Economy.StarterBike && Text("Sottotitolo").Contains("Carriera: conto"), "moto " + PistaMode.Bike + " / " + Text("Sottotitolo"));

            // the cups: the Supersport one can be ridden (the Tigre 675 is of its class), the Superbike one cannot (no Superbike)
            var cupOpen = Find("Pulsante Campionato");
            EventSystem.current.SetSelectedGameObject(cupOpen);
            cupOpen.GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            EventSystem.current.SetSelectedGameObject(Find("Coppa 2"));
            yield return null;
            Check("la Coppa Supersport si puo' correre: si sale in sella alla Tigre 675 potenziata, la moto di quella classe che si ha", Text("Nota").StartsWith("In sella: " + Bikes.Get(tigre).name + " liv. 3") && Text("Invio").StartsWith("Invio: gara 1"), Text("Nota").Replace((char)10, '/') + " | " + Text("Invio"));
            EventSystem.current.SetSelectedGameObject(Find("Coppa 3"));
            yield return null;
            Check("la Coppa Superbike no: la nota dice che serve una moto di quella classe e Invio dice che non si parte", Text("Nota").StartsWith("Serve una moto della classe Superbike") && Text("Invio").StartsWith("Non si parte"), Text("Nota").Replace((char)10, '/') + " | " + Text("Invio"));
            if (shots) yield return Shot("menu_17_coppa_senza_moto");
            Find("Coppa 3").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("un clic su quella coppa non la fa partire: il menu resta aperto e nessuna stagione comincia", Open("cupPanel") && GameMenu.InMenu && !PistaMode.Active && PistaCups.Book.Season(2) == null, "campionato aperto " + Open("cupPanel"));
            PistaCareer.Pay(20000); PistaCareer.Buy(0);          // an Argo 1000: the Superbike cup is open now
            Call("RefreshCup");
            yield return null;
            Check("comprata una Superbike la coppa si puo' correre, in sella all'Argo 1000", Text("Nota").StartsWith("In sella: " + Bikes.Get(0).name) && Text("Invio").StartsWith("Invio: gara 1") && PistaMode.CupAvailable(Cups.All[2]) && PistaMode.CupBike(Cups.All[2]) == 0, Text("Nota").Replace((char)10, '/'));
            Find("Pulsante Indietro").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;

            // off again: every bike is there, and the setup panel says nothing of money
            open = Find("Pulsante Garage");
            EventSystem.current.SetSelectedGameObject(open);
            open.GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null; yield return null;
            Find("Pulsante Carriera").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Row("Moto").Step(1);
            Check("spenta la carriera tutte le moto tornano tue: nessun prezzo, nessun 'Compra', e la riga le attraversa tutte", !PistaMode.CareerOn && CareerLabel() == "Carriera: spenta" && Find("Pulsante Compra") == null && !Text("Nome moto").Contains(" cr") && PistaMode.Bike == stage.Bike, CareerLabel() + " / " + Text("Nome moto"));
            Call("GarageBack");
            yield return null; yield return null;
            Check("e la preparazione non dice piu' il conto", !Text("Sottotitolo").Contains("Carriera"), Text("Sottotitolo"));
        }

        string CareerLabel() { var g = Find("Pulsante Carriera"); return g != null && g.GetComponentInChildren<Text>() != null ? g.GetComponentInChildren<Text>().text : "?"; }

        /// <summary>The championship from the setup: the three cups with their calendar and field, a season and a trophy written in the book, the standings, the two-step restart, back to the setup, the cup in progress
        /// chosen again, and a click on a finished cup that starts a new season and its first round.</summary>
        IEnumerator CupFlow()
        {
            PistaCups.ForgetAll();
            PistaMode.Bike = 0; PistaMode.Skin = -1; PistaMode.Aids = 1; PistaMode.Rank = 1; PistaMode.Laps = 2; PistaMode.Rivals = 3;
            var open = Find("Pulsante Campionato");
            var licences = Find("Pulsante Patenti");
            var above = licences != null ? licences.GetComponent<Selectable>().navigation.selectOnUp : null;
            Check("la preparazione ha il pulsante 'Campionato' in cima alla croce, sopra 'Patenti'", open != null && above != null && above.gameObject == open, above != null ? above.name : "-");
            if (open == null) yield break;
            EventSystem.current.SetSelectedGameObject(open);           // a click or a press leaves the button selected, as here
            open.GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("'Campionato' apre il campionato e chiude la preparazione, e la prima coppa e' gia' scelta", Open("cupPanel") && !Open("pistaPanel") && Sel == "Coppa 1", Sel);

            string bad = "";
            for (int i = 0; i < Cups.Count; i++)
            {
                var cup = Cups.All[i];
                var cell = Find("Coppa " + (i + 1));
                if (cell == null) { bad += " manca" + (i + 1); continue; }
                string name = CellText(cell, "Nome"), klass = CellText(cell, "Classe"), state = CellText(cell, "Stagione"), prize = CellText(cell, "Premio");
                if (name != cup.name || !klass.Contains(cup.klass) || !klass.Contains(cup.Rounds + " gare") || state != "Stagione da cominciare" || prize != "Nessuna coppa vinta") bad += " " + (i + 1) + "[" + name + "|" + klass + "|" + state + "|" + prize + "]";
            }
            Check("ci sono tre coppe, ognuna con il suo nome, la sua classe e nessuna stagione ancora", bad.Length == 0, bad);
            Check("il riepilogo dice i punti dei posti e che non c'e' nessuna coppa", Text("Riepilogo") == "Punti " + string.Join(" ", Cups.PointsTable) + "   -   Coppe: oro 0  argento 0  bronzo 0", Text("Riepilogo"));

            // under the cup chosen: its line, the calendar, the field, the bike and the level, and what Invio does
            var c0 = Cups.All[0];
            int bike0 = Cups.BikesOf(c0)[0];
            string circuit0 = CircuitGen.Generate(c0.CircuitSeed(0)).name;
            string calendar = "";
            for (int r = 0; r < c0.Rounds; r++) if (!Text("Gara " + (r + 1)).StartsWith((r + 1) + "  " + CircuitGen.Generate(c0.CircuitSeed(r)).name + "  -  ")) calendar += " " + (r + 1);
            Check("sotto la coppa scelta si leggono la sua frase, il calendario (la prima gara e' la prossima) e cosa fa Invio",
                Text("Sottotitolo") == c0.tagline && calendar.Length == 0 && Text("Gara 1 stato") == "prossima" && Text("Gara 2 stato") == "" && Text("Invio") == "Invio: gara 1 di " + c0.Rounds + " a " + circuit0 + "  -  " + c0.laps + " giri",
                Text("Sottotitolo") + " / " + Text("Gara 1") + " / " + Text("Invio") + calendar);
            var field0 = CupField.Make(c0, PistaMode.Rank, PistaMode.SkinFor(bike0), PistaDirector.Names, Livery.Count);
            bool rows = Text("Intestazione classifica").StartsWith("Gli avversari");
            for (int i = 0; i < Cups.Rivals; i++) rows &= Text("Pilota " + (i + 1)) == field0[i].name && Text("Pilota " + (i + 1) + " valore") == Bikes.Get(field0[i].bike).name;
            rows &= Text("Pilota 12") == PistaDirector.CupPlayerName() && Text("Pilota 12 valore") == Bikes.Get(bike0).name;
            Check("prima del via la tabella mostra gli undici avversari con la loro moto, e il giocatore con la sua", rows, Text("Intestazione classifica") + " / " + Text("Pilota 1") + " " + Text("Pilota 1 valore") + " / " + Text("Pilota 12") + " " + Text("Pilota 12 valore"));
            string note = Text("Nota");
            Check("la nota dice la moto (la prima della classe: quella della preparazione e' di un'altra) e il livello degli avversari",
                note.StartsWith("In sella: " + Bikes.Get(bike0).name) && note.Contains("non e' di questa classe") && note.Contains("livello esperto, come nella preparazione"), note.Replace((char)10, '/'));
            Check("senza una stagione 'Ricomincia' non c'e'", Find("Pulsante Ricomincia") == null);
            if (shots) yield return Shot("menu_10_campionato_prima");

            // the cross: right to the next cup, down to 'Indietro' (no season, no 'Ricomincia') and back up to the cup
            var n1 = Find("Coppa 1").GetComponent<Selectable>().navigation; var n3 = Find("Coppa 3").GetComponent<Selectable>().navigation; var back = Find("Pulsante Indietro").GetComponent<Selectable>().navigation;
            Check("la croce: destra passa alla coppa dopo, giu' va a 'Indietro' e da li' si risale alla coppa scelta",
                n1.selectOnRight != null && n1.selectOnRight.name == "Coppa 2" && n1.selectOnLeft == null && n1.selectOnDown != null && n1.selectOnDown.name == "Pulsante Indietro" && n3.selectOnRight == null && n3.selectOnLeft != null && n3.selectOnLeft.name == "Coppa 2"
                && back.selectOnUp != null && back.selectOnUp.name == "Coppa 1");
            EventSystem.current.SetSelectedGameObject(Find("Coppa 2"));
            yield return null;
            var c1 = Cups.All[1];
            Check("scegliere un'altra coppa cambia la frase, il calendario e la riga di Invio", Text("Sottotitolo") == c1.tagline && Text("Gara 1").Contains(CircuitGen.Generate(c1.CircuitSeed(0)).name) && Text("Invio").Contains(CircuitGen.Generate(c1.CircuitSeed(0)).name)
                && Text("Intestazione calendario").EndsWith(c1.klass), Text("Sottotitolo") + " / " + Text("Invio"));

            // the book: two rounds of the Supersport cup (the player third, then first) and a finished Superbike season with the player winning every round
            PistaCups.Begin(1, 1, 0);
            PistaCups.Record(1, new[] { 5, 6, 0, 1, 2, 3, 4, 7, 8, 9, 10, 11 }, 12);
            PistaCups.Record(1, new[] { 0, 5, 6, 1, 2, 3, 4, 7, 8, 9, 10, 11 }, 12);
            PistaCups.Begin(2, 0, 0);
            for (int r = 0; r < 4; r++) PistaCups.Record(2, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 12);
            Call("RefreshCup");
            yield return null;
            var f1 = CupField.Make(c1, 1, 0, PistaDirector.Names, Livery.Count);
            string you = PistaDirector.CupPlayerName();
            Check("la stagione scritta nel libro si legge sulla coppa: la gara a cui si e' arrivati, il posto e i punti", CellText(Find("Coppa 2"), "Stagione") == "Gara 3 di 4  -  2° con 41 punti", CellText(Find("Coppa 2"), "Stagione"));
            Check("la classifica e' quella dei punti (il giocatore secondo con 41) e il calendario segna le gare fatte e la prossima",
                Text("Intestazione classifica") == "Classifica dopo 2 gare" && Text("Pilota 1") == "1  " + f1[4].name && Text("Pilota 1 valore") == "45 pt" && Text("Pilota 2") == "2  " + you && Text("Pilota 2 valore") == "41 pt"
                && Text("Pilota 3") == "3  " + f1[5].name && Text("Pilota 3 valore") == "36 pt" && Text("Pilota 12 valore") == "8 pt" && Text("Gara 1 stato") == "fatta" && Text("Gara 2 stato") == "fatta" && Text("Gara 3 stato") == "prossima" && Text("Gara 4 stato") == "",
                Text("Intestazione classifica") + " / " + Text("Pilota 1") + " " + Text("Pilota 1 valore") + " / " + Text("Pilota 2") + " " + Text("Pilota 2 valore") + " / " + Text("Gara 3 stato"));
            Check("il giocatore e' in color ambra, la riga di Invio corre la terza gara e la nota dice che il livello e' fissato",
                Find("Pilota 2").GetComponent<Text>().color == new Color(1f, 0.74f, 0.18f) && Text("Invio").StartsWith("Invio: gara 3 di 4 a " + CircuitGen.Generate(c1.CircuitSeed(2)).name) && Text("Nota").Contains("fissato all'inizio della stagione"),
                Text("Invio") + " / " + Text("Nota").Replace((char)10, '/'));
            var reset = Find("Pulsante Ricomincia");
            Check("con una stagione in corso c'e' 'Ricomincia': la croce ci scende dalla coppa e da li' risale alla coppa o va a 'Indietro'",
                reset != null && Find("Coppa 2").GetComponent<Selectable>().navigation.selectOnDown == reset.GetComponent<Selectable>() && reset.GetComponent<Selectable>().navigation.selectOnUp.name == "Coppa 2"
                && reset.GetComponent<Selectable>().navigation.selectOnDown.name == "Pulsante Indietro" && Find("Pulsante Indietro").GetComponent<Selectable>().navigation.selectOnUp == reset.GetComponent<Selectable>());
            if (shots) yield return Shot("menu_11_campionato_stagione");

            // the finished season: the trophy on the cup and in the summary, the table says the season is over, Invio begins a new one, and there is nothing to restart
            EventSystem.current.SetSelectedGameObject(Find("Coppa 3"));
            yield return null;
            var c2 = Cups.All[2];
            Check("una stagione finita con la vittoria da' la coppa d'oro sulla coppa e nel riepilogo",
                CellText(Find("Coppa 3"), "Stagione") == "Finita  -  1° con 100 punti" && CellText(Find("Coppa 3"), "Premio") == "Migliore: COPPA D'ORO" && Find("Coppa 3").transform.Find("Premio").GetComponent<Text>().color == PistaDirector.LicenceColor(Medal.Gold).Value
                && Text("Riepilogo") == "Punti " + string.Join(" ", Cups.PointsTable) + "   -   Coppe: oro 1  argento 0  bronzo 0", CellText(Find("Coppa 3"), "Premio") + " / " + Text("Riepilogo"));
            Check("sotto una stagione finita la tabella dice che e' finita, Invio comincia una stagione nuova e non c'e' 'Ricomincia'",
                Text("Intestazione classifica") == "Classifica dopo 4 gare  -  stagione finita" && Text("Pilota 1") == "1  " + you && Text("Pilota 1 valore") == "100 pt" && Text("Invio") == "Invio: nuova stagione, gara 1 di 4 a " + CircuitGen.Generate(c2.CircuitSeed(0)).name + "  -  " + c2.laps + " giri" && Find("Pulsante Ricomincia") == null,
                Text("Intestazione classifica") + " / " + Text("Invio"));
            if (shots) yield return Shot("menu_12_campionato_coppa");

            // 'Ricomincia' asks twice: the first press only asks, another cup chosen and back puts the question away, the second press throws the season away and begins one at the level of now
            EventSystem.current.SetSelectedGameObject(Find("Coppa 2"));
            yield return null;
            PistaMode.Rank = 2;
            reset = Find("Pulsante Ricomincia");
            var label = reset.GetComponentInChildren<Text>();
            reset.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Check("'Ricomincia' chiede conferma e non butta ancora nulla", label.text == "Sicuro? Perdi i punti" && PistaCups.Book.Season(1).round == 2 && PistaCups.Book.Season(1).points[0] == 41, label.text + ", gare " + PistaCups.Book.Season(1).round);
            EventSystem.current.SetSelectedGameObject(Find("Coppa 1"));
            yield return null;
            EventSystem.current.SetSelectedGameObject(Find("Coppa 2"));
            yield return null;
            Check("scegliendo un'altra coppa e tornando la domanda e' tolta (il pulsante dice di nuovo 'Ricomincia') e la stagione c'e' ancora", label.text == "Ricomincia" && PistaCups.Book.Season(1).round == 2, label.text);
            reset.GetComponent<Button>().onClick.Invoke();
            yield return null;
            reset.GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            var again = PistaCups.Book.Season(1);
            Check("il secondo 'Ricomincia' butta la stagione e ne comincia una al livello di adesso: nessuna gara, nessun punto, la coppa d'oro dell'altra coppa resta, e la scelta torna alla coppa",
                again.round == 0 && again.points[0] == 0 && again.rank == 2 && PistaCups.Book.BestTrophy(2) == Medal.Gold && Find("Pulsante Ricomincia") == null && Sel == "Coppa 2" && Text("Intestazione classifica").StartsWith("Gli avversari") && Text("Nota").Contains("come nella preparazione"),
                "gare " + again.round + ", livello " + again.rank + ", " + Sel);

            // back to the setup (the button that opened the championship is still the chosen one); a season in progress is the cup chosen when it is opened again
            PistaCups.Record(1, new[] { 0, 5, 6, 1, 2, 3, 4, 7, 8, 9, 10, 11 }, 12);
            bool handled = (bool)Call("CupBackKey");
            yield return null; yield return null;
            Check("indietro porta alla preparazione con il pulsante 'Campionato' ancora scelto", handled && Open("pistaPanel") && !Open("cupPanel") && Sel == "Pulsante Campionato", Sel);
            EventSystem.current.SetSelectedGameObject(Find("Pulsante Campionato"));
            Find("Pulsante Campionato").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("riaprendolo e' scelta la coppa con la stagione in corso", Open("cupPanel") && Sel == "Coppa 2" && Text("Invio").StartsWith("Invio: gara 2 di 4 a " + CircuitGen.Generate(c1.CircuitSeed(1)).name), Sel + " / " + Text("Invio"));

            // a click on the finished cup: a new season (the level of the setup now, the bike of the setup because it is of the class) and the first round of the calendar, with the cup's laps and the eleven riders
            EventSystem.current.SetSelectedGameObject(Find("Coppa 3"));
            Find("Coppa 3").GetComponent<Button>().onClick.Invoke();
            yield return null; yield return null;
            Check("un clic su una coppa toglie il menu e la partita e' su pista, in quella coppa", !GameMenu.InMenu && PistaMode.Requested && PistaMode.Cup == 2, "coppa " + PistaMode.Cup);
            yield return WaitFor(() => PistaDirector.I != null && PistaDirector.I.Ready, 150f);
            var d = PistaDirector.I;
            if (!Check2("la gara e' pronta", d != null && d.Ready && PistaMode.Active)) yield break;
            yield return null;
            var fresh = PistaCups.Book.Season(2);
            Check("la stagione e' nuova (al livello della preparazione), la coppa d'oro resta nel libro e si corre la prima gara del calendario",
                fresh != null && fresh.round == 0 && fresh.rank == 2 && d.CupSeason == fresh && PistaCups.Book.BestTrophy(2) == Medal.Gold && PistaCups.Book.SeasonsDone(2) == 1 && d.seed == c2.CircuitSeed(0), "gara " + (fresh != null ? fresh.round + 1 : 0) + ", seme " + d.seed);
            Check("con i giri della coppa, i dodici piloti e la moto della preparazione (e' della classe), e gli aiuti sono quelli del giocatore",
                d.race.laps == c2.laps && d.race.racers.Count == Cups.Riders && d.me.bikeIndex == 0 && d.me.bike.tcs == (PistaMode.Aids < 2) && d.me.bike.leanAssist == (PistaMode.Aids == 0), d.race.laps + " giri, " + d.race.racers.Count + " piloti, moto " + d.me.bikeIndex);
            Check("il regista sa che e' la coppa e non c'e' nessuna patente", d.CupActive && d.Licence == null && PistaMode.Licence == -1, "");
            if (shots) yield return Shot("menu_13_campionato_gara");
        }

        /// <summary>How much of a picture is lit (the stage has a nearly black background): the share of pixels clearly brighter than it.</summary>
        static float LitShare(RenderTexture rt, string saveAs, out string stats)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            int n = 0; long r = 0, g = 0, b = 0; var all = copy.GetPixels32();
            foreach (var p in all) { r += p.r; g += p.g; b += p.b; if (p.r + p.g + p.b > 3 * 52) n++; }
            var mid = all[all.Length / 2 + rt.width / 2];
            stats = "media " + (r / all.Length) + "," + (g / all.Length) + "," + (b / all.Length) + ", centro " + mid.r + "," + mid.g + "," + mid.b;
            if (saveAs != null)
            {
                try { string dir = TestPaths.Out("prove/pista"); Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, saveAs + ".png"), copy.EncodeToPNG()); } catch { }
            }
            Destroy(copy);
            return n / (float)(rt.width * rt.height);
        }

        static string CellText(GameObject cell, string child) { var t = cell.transform.Find(child); return t != null && t.GetComponent<Text>() != null ? t.GetComponent<Text>().text : "?"; }

        /// <summary>How many pixels of a picture are nearly opaque (the picture of the circuit is not readable from the CPU: it is copied through a render texture).</summary>
        static int Painted(Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            int n = 0;
            foreach (var p in copy.GetPixels32()) if (p.a > 200) n++;
            Destroy(copy);
            return n;
        }

        // ------------------------------------------------------------------ a picture of what the menu shows
        /// <summary>-pistabikeshots (with -pistashots not needed): the bikes of the garage photographed close up from the side, the front, the tail and the rider, into hotel_build/prove/pista/bici_N_vista.png, to look at the models.
        /// -pistabikes 0,2,3 picks the bikes (the default is one of each shape and the smallest).</summary>
        IEnumerator BikeShots(GarageStage stage)
        {
            string dir = TestPaths.Out("prove/pista");
            Directory.CreateDirectory(dir);
            int[] bikes = { 0, 2, 3, 4 };
            int skin0 = PistaMode.Skin, outfit0 = PistaMode.Outfit;
            PistaMode.Skin = -1; PistaMode.Outfit = -1;                 // every bike in the livery it comes in, with its own rider
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-pistabikes")
                {
                    var parts = a[i + 1].Split(',');
                    bikes = new int[parts.Length];
                    for (int k = 0; k < parts.Length; k++) int.TryParse(parts[k], out bikes[k]);
                }
            var views = new (string name, float spin, float dist, float camY, float aim, float fov)[]
            {
                ("lato", 90f, 6.4f, 1.0f, 0.60f, 24f), ("davanti", 30f, 6.2f, 1.25f, 0.68f, 24f), ("dietro", 212f, 6.2f, 1.35f, 0.70f, 24f), ("pilota", 62f, 3.4f, 1.45f, 0.98f, 28f), ("motore", 90f, 3.6f, 0.55f, 0.46f, 28f),
            };
            foreach (int b in bikes)
            {
                PistaMode.Bike = Mathf.Clamp(b, 0, Bikes.Count - 1);
                Call("RefreshGarage");
                yield return null; yield return null;
                foreach (var v in views)
                {
                    yield return new WaitForEndOfFrame();
                    var tex = stage.Snapshot(v.spin, v.dist, v.camY, v.aim, v.fov, 1280, 800);
                    File.WriteAllBytes(dir + "/bici_" + PistaMode.Bike + "_" + v.name + ".png", tex.EncodeToPNG());
                    Destroy(tex);
                }
                Debug.Log("[PryingEyes] pistamenutest foto della moto " + PistaMode.Bike + " in " + dir);
            }
            PistaMode.Bike = 3; PistaMode.Skin = skin0; PistaMode.Outfit = outfit0;                      // the flow goes on from the Scintilla 125, as it found it
            Call("RefreshGarage");
            yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                string dir = TestPaths.Out("prove/pista");
                Directory.CreateDirectory(dir);
                var canvas = menu.GetComponent<Canvas>();
                var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
                if (cam == null) { Check("foto " + name, false, "nessuna camera"); yield break; }
                int W = 1600, H = 900;
                var rt = RenderTexture.GetTemporary(W, H, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                var mode = canvas.renderMode;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 0.2f;
                Canvas.ForceUpdateCanvases();
                cam.Render();
                canvas.renderMode = mode; canvas.worldCamera = null;
                cam.targetTexture = prev;
                var old = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = old;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                Debug.Log("[PryingEyes] pistamenutest foto " + dir + "/" + name + ".png");
            }
            catch (Exception e) { Debug.LogException(e); Check("foto " + name, false, e.Message); }
        }
    }
}
#endif
