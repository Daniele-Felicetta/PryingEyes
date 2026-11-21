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
    /// is opened again, and "Via!" starts the race on that very circuit with the laps, the rivals and the bike that were chosen.</summary>
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
            Check("il nome e la lunghezza sono scritti", name1.Length > 3 && Text("Dati del circuito").EndsWith(" km"), name1 + " / " + Text("Dati del circuito"));
            Check("il circuito dell'anteprima e' quello del seme che si correra'", CircuitGen.Generate(seed1).name == name1, name1);
            Check("le righe dicono i valori ricordati", Value("Giri") == (PistaMode.Laps == 1 ? "1 giro" : PistaMode.Laps + " giri") && Value("Livello") == PistaMode.RankNames[PistaMode.Rank] && Value("Moto") == Bikes.Get(PistaMode.Bike).name,
                Value("Giri") + ", " + Value("Livello") + ", " + Value("Moto"));
            if (shots) yield return Shot("menu_2_preparazione");

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
            var giriNav = Row("Giri").navigation;
            Check("e dalla prima scelta si scende alla seconda", giriNav.selectOnDown != null && giriNav.selectOnDown.name == "Scelta Avversari" && giriNav.selectOnUp == null);

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
