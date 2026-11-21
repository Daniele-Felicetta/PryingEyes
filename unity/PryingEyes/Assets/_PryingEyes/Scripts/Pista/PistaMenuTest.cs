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
            Row("Aiuti alla guida").Step(1);
            Row("Ora del giorno").Step(1);
            Check("'Aiuti' e 'Ora del giorno' girano e rigirano", Value("Aiuti alla guida") == PistaMode.AidNames[PistaMode.Aids] && Value("Ora del giorno") == PistaMode.HourNames[PistaMode.HourChoice]);
            for (int i = 0; i < PistaMode.HourNames.Length; i++) Row("Ora del giorno").Step(1);
            for (int i = 0; i < PistaMode.AidNames.Length; i++) Row("Aiuti alla guida").Step(1);
            Check("dopo un giro intero i valori sono quelli di prima", Value("Ora del giorno") == PistaMode.HourNames[PistaMode.HourChoice]);
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
            Check("la croce in alto dal 'Via!' porta a 'Altra pista', e da li' all'ultima scelta", goNav.selectOnUp != null && goNav.selectOnUp.name == "Pulsante Altra pista" && otherNav.selectOnUp != null && otherNav.selectOnUp.name == "Scelta Ora del giorno",
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
            Check("riaprendola il circuito e le scelte ci sono ancora", Open("pistaPanel") && PistaMode.Chosen == seed2 && Text("Nome del circuito") == name2 && PistaMode.Laps == 2 && PistaMode.Rivals == 3 && Value("Giri") == "2 giri", Text("Nome del circuito"));

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
            Check("il menu non blocca i comandi della gara", !GameMenu.Blocked);
            if (shots) yield return Shot("menu_4_gara");
        }

        /// <summary>The licence panel from the setup, and a test started from it.</summary>
        IEnumerator LicenceFlow()
        {
            PistaLicences.ForgetAll();
            // the player's own choices, all different from what the test will ask for: the test must win
            PistaMode.Bike = 1; PistaMode.Aids = 0; PistaMode.Laps = 5; PistaMode.Rivals = 7; PistaMode.HourChoice = 1;
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
            Check("il regista sa che e' la prova e il registro e' ancora quello della preparazione", d.Licence != null && d.Licence.test.id == test.id && PistaLicences.Book.MedalOf(0) == Medal.Gold, d.Licence != null ? d.Licence.test.id : "nessuna prova");
            if (shots) yield return Shot("menu_7_patente_gara");
        }


        /// <summary>The garage from the setup: the showroom and the bars, the choice of bike and of colours, back to the setup, and the colours worn in the race.</summary>
        IEnumerator GarageFlow()
        {
            PistaMode.Bike = 0; PistaMode.Skin = -1; PistaMode.Aids = 1; PistaMode.Rivals = 3; PistaMode.Laps = 1;
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
            stage.SetSpin(300f);
            for (int i = 0; i < 4; i++) yield return null;
            if (shots) yield return Shot("menu_9_garage_125");

            var bikeNav = Row("Moto").navigation; var colourNav = Row("Colori").navigation; var doneNav = Find("Pulsante Fatto").GetComponent<Selectable>().navigation;
            Check("la croce: da 'Moto' si scende a 'Colori', poi a 'Fatto', e da 'Fatto' si risale a 'Colori'", bikeNav.selectOnDown != null && bikeNav.selectOnDown.name == "Scelta Colori" && colourNav.selectOnUp != null && colourNav.selectOnUp.name == "Scelta Moto"
                && colourNav.selectOnDown != null && colourNav.selectOnDown.name == "Pulsante Fatto" && doneNav.selectOnUp != null && doneNav.selectOnUp.name == "Scelta Colori");

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
