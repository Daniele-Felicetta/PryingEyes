#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-padtest -nowaves (SENZA -autotest: serve il menu), di solito in batchmode: il controller nei menu e nel gioco normale.
    /// Parte pura: la traduzione XInput -> navigazione (croce e stick con isteresi, un asse alla volta, A e B come fronti) e il blocco dei
    /// comandi di gioco con un menu aperto. Poi la ricerca automatica del controller (PadControls.TestPoll al posto di XInput): un controller
    /// fermo non si adotta, il primo su cui si preme si', dopo due secondi di assenza si lascia. Poi il menu vero con uno stato XInput finto
    /// (PadControls.Inject): croce e stick muovono la selezione, A apre, i cursori si regolano con sinistra e destra, B torna indietro e
    /// ritrova il pulsante di partenza, un campo di testo non intrappola, il primo comando senza selezione sceglie il pulsante, la cornice
    /// sta sulla voce scelta, A su "Gioca" apre "Come vuoi giocare?" (croce a destra e a sinistra tra "Standard" e "Il deserto", B torna a "Gioca").
    /// Infine in partita: A su "Standard" fa partire senza far saltare, Start apre e chiude la pausa, B in pausa riprende e in gioco
    /// non apre niente. Un controller fisico non c'e': la lettura vera di XInput la prova -splittest ("risponde senza errori").</summary>
    public sealed class PadMenuTest : PlayerTest
    {
        GameMenu menu;

        protected override string Suite { get { return "padtest"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<PadMenuTest>("-padtest"); }

        protected override void Finish() { PadControls.Inject(null); PadControls.TestPoll = null; PadControls.Disable(); }

        static PadControls.Raw Pad(ushort buttons = 0, int lx = 0, int ly = 0, int rt = 0) =>
            new PadControls.Raw { connected = true, buttons = buttons, lx = (short)lx, ly = (short)ly, rightTrigger = (byte)rt };

        static FieldInfo Field(string name) => typeof(GameMenu).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        T Get<T>(string name) => (T)Field(name).GetValue(menu);
        static GameObject Selected => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        static string SelName => Selected != null ? Selected.name : "-";
        bool PanelOpen(string field) => Get<RectTransform>(field).gameObject.activeSelf;

        /// <summary>Un tocco: lo stato resta premuto per due fotogrammi e poi si rilascia. In coda una pausa: uGUI non ripete un comando prima di un decimo di secondo.</summary>
        static IEnumerator Tap(ushort buttons = 0, int lx = 0, int ly = 0)
        {
            PadControls.Inject(Pad(buttons, lx, ly)); yield return null; yield return null;
            PadControls.Inject(Pad()); yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.16f);
        }

        // ------------------------------------------------------------------ parte pura

        void NavMapping()
        {
            var m = new PadControls.Mapper();
            m.Step(Pad(PadControls.DpadRight));
            Check("croce a destra: naviga a destra", m.NavX == 1 && m.NavY == 0 && !m.Confirm && !m.Cancel);
            m.Step(Pad());
            Check("croce rilasciata: ferma", m.NavX == 0 && m.NavY == 0);
            m.Step(Pad(PadControls.DpadDown));
            Check("croce in basso: naviga in basso", m.NavX == 0 && m.NavY == -1);
            m.Step(Pad(PadControls.DpadUp));
            Check("croce in alto: naviga in alto", m.NavY == 1);

            m.Step(Pad());
            m.Step(Pad(lx: 21000));
            Check("stick sinistro oltre il 60%: naviga", m.NavX == 1);
            m.Step(Pad(lx: 14000));
            Check("stick tra il 40% e il 60% dopo essere acceso: resta acceso (isteresi)", m.NavX == 1);
            m.Step(Pad(lx: 12000));
            Check("stick sotto il 40%: si spegne", m.NavX == 0);
            m.Step(Pad(lx: 17000));
            Check("stick al 52% da spento: non riparte (isteresi)", m.NavX == 0);
            m.Step(Pad(lx: -30000));
            Check("stick a sinistra: naviga a sinistra", m.NavX == -1);

            m.Step(Pad(lx: 30000, ly: 21000));
            Check("stick in diagonale: un solo asse, il piu' inclinato", m.NavX == 1 && m.NavY == 0);
            m.Step(Pad(lx: 21000, ly: 30000));
            Check("stick in diagonale verso l'alto: solo la verticale", m.NavX == 0 && m.NavY == 1);
            m.Step(Pad(PadControls.DpadUp, lx: 30000));
            Check("croce e stick insieme: vince la croce", m.NavX == 0 && m.NavY == 1);
            m.Step(Pad((ushort)(PadControls.DpadUp | PadControls.DpadRight)));
            Check("due tasti della croce insieme: vince la verticale", m.NavX == 0 && m.NavY == 1);

            m.Step(Pad());
            m.Step(Pad(PadControls.A));
            Check("A: conferma, una volta sola", m.Confirm && !m.Cancel);
            m.Step(Pad(PadControls.A));
            Check("A tenuto: non conferma di nuovo", !m.Confirm);
            m.Step(Pad());
            m.Step(Pad(PadControls.B));
            Check("B: indietro, una volta sola", m.Cancel && !m.Confirm);
            m.Step(Pad(PadControls.B));
            Check("B tenuto: non torna indietro di nuovo", !m.Cancel);
            m.Step(Pad());
            m.Step(Pad((ushort)(PadControls.LeftShoulder | PadControls.A)));
            Check("LB + A: conferma lo stesso (nei menu il secondo livello non c'e')", m.Confirm);

            m.Step(Pad());
            m.Step(Pad((ushort)(PadControls.A | PadControls.StartBtn | PadControls.DpadDown), rt: 255));
            Check("senza menu: A salta, RT spara, Start e' pausa", m.Down[(int)TouchControls.Action.Jump] && m.Held[(int)TouchControls.Action.Fire] && m.Down[(int)TouchControls.Action.Pause]);
            m.SuppressGameplay();
            Check("menu aperto: A non salta, RT non spara", !m.Down[(int)TouchControls.Action.Jump] && !m.Held[(int)TouchControls.Action.Fire] && !m.Held[(int)TouchControls.Action.Jump]);
            Check("menu aperto: Start resta (serve per riprendere), la navigazione e la conferma anche", m.Down[(int)TouchControls.Action.Pause] && m.NavY == -1 && m.Confirm);
            m.Step(Pad(PadControls.A, ly: 32767));
            m.SuppressGameplay();
            Check("menu aperto: lo stick non muove ne' guarda", m.Move == Vector2.zero && m.LookRate == Vector2.zero && m.NavY == 1);
            m.Silence();
            Check("gioco in secondo piano: niente di niente", !m.Confirm && m.NavY == 0 && m.Move == Vector2.zero);
            m.Step(default);
            Check("controller scollegato: niente navigazione", m.NavX == 0 && m.NavY == 0 && !m.Confirm && !m.Cancel);
        }

        // ------------------------------------------------------------------ ricerca automatica

        IEnumerator AutoDetect()
        {
            PadControls.Inject(null); PadControls.Disable();
            var slots = new PadControls.Raw[4];
            PadControls.TestPoll = slot => slots[slot];
            slots[2] = Pad();                                     // collegato, ma nessuno lo tocca
            PadControls.EnableAuto();
            yield return new WaitForSecondsRealtime(0.8f);
            Check("controller collegato ma fermo: non si adotta", PadControls.Index == -1 && !PadControls.Connected && !PadControls.Preferred);
            slots[2] = Pad(PadControls.LeftShoulder);             // lo si tocca
            yield return null; yield return null; yield return null;
            Check($"primo tasto premuto: si adotta lo slot 3 (indice {PadControls.Index})", PadControls.Index == 2 && PadControls.Connected && PadControls.Preferred);
            slots[2] = Pad();
            yield return new WaitForSecondsRealtime(0.3f);
            Check("rilasciato: resta adottato", PadControls.Index == 2 && PadControls.Connected);
            slots[2] = default;                                   // si scollega
            yield return new WaitForSecondsRealtime(0.8f);
            Check("scollegato da poco: lo slot si tiene (potrebbe tornare)", PadControls.Index == 2 && !PadControls.Connected);
            yield return new WaitForSecondsRealtime(2.0f);
            Check("scollegato da piu' di due secondi: si lascia e si torna a cercare", PadControls.Index == -1 && !PadControls.Connected && !PadControls.Preferred);
            slots[3] = Pad(PadControls.RightShoulder);            // ne arriva un altro, in un altro slot (non A: nel menu aperto confermerebbe 'Gioca')
            yield return new WaitForSecondsRealtime(1.0f);
            Check($"un altro controller premuto: si adotta (indice {PadControls.Index})", PadControls.Index == 3 && PadControls.Connected);
            PadControls.EnableAuto();
            Check("EnableAuto con uno slot gia' scelto non cambia niente", PadControls.Index == 3);
            PadControls.Enable(1);
            Check("Enable sceglie lo slot a mano e spegne la ricerca", PadControls.Index == 1);
            PadControls.Disable();
            PadControls.TestPoll = null;
            Check("Disable: nessun controller", PadControls.Index == -1 && !PadControls.Enabled && !PadControls.Connected);
        }

        // ------------------------------------------------------------------ il menu vero

        /// <summary>La cornice di selezione sta addosso alla voce scelta (centro e misura, in unita' della tela).</summary>
        bool RingOnSelection()
        {
            var ring = Get<RectTransform>("focusRing"); var backdrop = Get<RectTransform>("backdrop");
            var target = Selected != null ? Selected.transform as RectTransform : null;
            if (target == null || !ring.gameObject.activeSelf) return false;
            var c = new Vector3[4]; target.GetWorldCorners(c);
            Vector3 low = backdrop.InverseTransformPoint(c[0]), high = backdrop.InverseTransformPoint(c[2]);
            Vector2 center = (low + high) * 0.5f, size = new Vector2(high.x - low.x + 8f, high.y - low.y + 8f);
            return (ring.anchoredPosition - center).magnitude < 2f && (ring.sizeDelta - size).magnitude < 2f;
        }

        // ------------------------------------------------------------------ foto

        static string ArgAfter(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static IEnumerator Shot(string dir, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>-padtest -padshots CARTELLA con una finestra vera (non -batchmode; con -menureview perche' il gioco non riapplichi lo
        /// schermo salvato): foto del menu usato col controller, per guardare la cornice, i suggerimenti e la pausa. Le foto sono quelle dello
        /// schermo del gioco (ScreenCapture), non dello schermo del PC. Toccare il mouse sopra la finestra riaccende il puntatore.</summary>
        IEnumerator Shots(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (GameMenu.I == null && Time.realtimeSinceStartup < deadline) yield return null;
            menu = GameMenu.I;
            PadEventSystem.AlwaysFocused = true;
            if (EventSystem.current != null) EventSystem.current.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot(dir, "01-menu-tastiera");
            yield return Tap(ly: -30000);
            yield return Shot(dir, "02-menu-controller");
            yield return Tap(PadControls.DpadDown); yield return Tap(PadControls.DpadDown); yield return Tap(PadControls.DpadDown); yield return Tap(PadControls.DpadDown);
            yield return Tap(PadControls.A);
            yield return Shot(dir, "03-impostazioni-schermo");
            yield return Tap(PadControls.DpadDown); yield return Tap(PadControls.DpadDown);
            yield return Shot(dir, "04-impostazioni-schermo-riga");
            yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.DpadRight); yield return Tap(PadControls.A);
            yield return Tap(PadControls.DpadDown); yield return Tap(PadControls.DpadDown);
            yield return Shot(dir, "05-impostazioni-cursore");
            yield return Tap(PadControls.B);
            yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.A);
            yield return Shot(dir, "06-guida");
            yield return Tap(PadControls.B);
            yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.A);
            yield return Tap(PadControls.DpadUp);
            yield return Shot(dir, "07-cooperativa-campo");
            yield return Tap(PadControls.B); yield return Tap(PadControls.B);
            yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.DpadUp);
            yield return Tap(PadControls.A);                  // 'Gioca' chiede come giocare: la scelta e' 'Standard'
            yield return Tap(PadControls.DpadLeft);
            yield return Tap(PadControls.A);
            deadline = Time.realtimeSinceStartup + 120f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            var player = GameManager.I != null ? GameManager.I.player : null;
            if (player != null) player.invulnerable = true;
            yield return Shot(dir, "08-gioco-suggerimenti");
            yield return Tap(PadControls.StartBtn);
            yield return Shot(dir, "09-pausa-controller");
            yield return Tap(PadControls.StartBtn);
            Check("foto fatte", true);
        }

        protected override IEnumerator Run()
        {
            string shots = ArgAfter("-padshots");
            if (shots != null) { yield return Shots(shots); yield break; }
            NavMapping();
            yield return AutoDetect();

            float deadline = Time.realtimeSinceStartup + 30f;
            while (GameMenu.I == null && Time.realtimeSinceStartup < deadline) yield return null;
            menu = GameMenu.I;
            yield return null; yield return null;
            Check($"il menu c'e': principale, gioco fermo, 'Gioca' selezionato (menu {(menu != null)}, InMenu {GameMenu.InMenu}, Blocked {GameMenu.Blocked}, selezione {SelName}, EventSystem {(EventSystem.current != null)})",
                menu != null && GameMenu.InMenu && GameMenu.Blocked && SelName == "Pulsante Gioca");
            if (menu == null || !GameMenu.InMenu) yield break;
            float master0 = GameSettings.Master;

            // ---- menu principale: croce e stick muovono, la cornice segue
            yield return Tap(ly: -30000);
            Check($"stick in basso: 'Cooperativa' ({SelName})", SelName == "Pulsante Cooperativa");
            Check("dopo un comando col controller: modo navigazione, cursore nascosto, suggerimenti del controller", PadMenuInput.NavMode && !Cursor.visible && PadControls.Preferred
                && Get<Text>("footer").text.Contains("CROCE"));
            Check("la cornice sta sulla voce scelta", RingOnSelection());
            // voci del principale: Gioca, Cooperativa, Schermo condiviso, Personaggi, Come si gioca, Impostazioni, Esci
            yield return Tap(PadControls.DpadDown); yield return Tap(PadControls.DpadDown); yield return Tap(PadControls.DpadDown); yield return Tap(PadControls.DpadDown);
            Check($"croce in basso quattro volte: 'Impostazioni' ({SelName})", SelName == "Pulsante Impostazioni");
            yield return Tap(PadControls.DpadUp);
            Check($"croce in alto: 'Come si gioca' ({SelName})", SelName == "Pulsante Come si gioca");
            yield return Tap(PadControls.DpadDown);
            Check("la cornice segue anche dopo i passi", RingOnSelection());

            // ---- impostazioni: cinque schede, cursori e scelte
            bool uiSounds0 = GameSettings.UiSounds;
            yield return Tap(PadControls.A);
            Check($"A su 'Impostazioni': si apre, scelta la scheda 'Schermo' ({SelName})", PanelOpen("settingsPanel") && SelName == "Pulsante Schermo");
            yield return Tap(PadControls.DpadRight);
            Check($"croce a destra: la scheda 'Immagine', che si apre da sola ({SelName})", SelName == "Pulsante Immagine" && GameObject.Find("Scheda Immagine") != null && GameObject.Find("Scheda Schermo") == null);
            yield return Tap(PadControls.DpadRight);
            Check($"croce a destra: la scheda 'Audio' ({SelName})", SelName == "Pulsante Audio" && GameObject.Find("Scheda Audio") != null);
            Check("sulla scheda il suggerimento dice cosa contiene", Get<Text>("settingsHint").text.Contains("Volumi"));
            yield return Tap(PadControls.DpadDown);
            var master = Get<Slider>("sMaster");
            Check($"croce in basso dalla scheda: il primo cursore audio ({SelName})", Selected != null && Selected.GetComponent<Slider>() == master);
            Check("la cornice sta anche sul cursore", RingOnSelection());
            Check("il cursore ha il suo suggerimento sotto l'elenco", Get<Text>("settingsHint").text == "Il volume di tutto il gioco.");
            master.SetValueWithoutNotify(0.5f);
            yield return Tap(PadControls.DpadLeft);
            Check($"croce a sinistra sul cursore: lo regola ({master.value:F2}) e resta li'", master.value < 0.45f && Selected != null && Selected.GetComponent<Slider>() == master);
            yield return Tap(PadControls.DpadRight); yield return Tap(PadControls.DpadRight);
            Check($"croce a destra due volte: lo rialza ({master.value:F2})", master.value > 0.55f && Selected.GetComponent<Slider>() == master);
            yield return Tap(PadControls.DpadDown);
            Check($"croce in basso: il cursore sotto ({(Selected != null ? Selected.transform.parent.name : "-")})", Selected != null && Selected.GetComponent<Slider>() == Get<Slider>("sEffects"));
            for (int i = 0; i < 4; i++) yield return Tap(PadControls.DpadDown);
            var sounds = Selected != null ? Selected.GetComponent<MenuChoice>() : null;
            Check($"croce in basso quattro volte: la scelta 'Suoni dell'interfaccia' ({SelName})", sounds != null && Selected.name == "Scelta Suoni dell'interfaccia");
            Check("la cornice sta anche sulla scelta", RingOnSelection());
            yield return Tap(PadControls.DpadRight);
            Check($"croce a destra sulla scelta: cambia valore e resta li' ({sounds?.Value})", sounds != null && !GameSettings.UiSounds && sounds.Value == "Disattivati" && Selected == sounds.gameObject);
            yield return Tap(PadControls.A);
            Check($"A sulla scelta: cambia di nuovo ({sounds?.Value})", sounds != null && GameSettings.UiSounds && sounds.Value == "Attivi");
            GameSettings.SetUiSounds(uiSounds0);
            yield return Tap(PadControls.DpadDown);
            Check($"croce in basso dall'ultima riga: 'Indietro' ({SelName})", SelName == "Pulsante Indietro");
            yield return Tap(PadControls.DpadRight);
            Check($"croce a destra: 'Ripristina scheda' ({SelName})", SelName == "Pulsante Ripristina scheda");
            yield return Tap(PadControls.DpadLeft);
            yield return Tap(PadControls.B);
            Check($"B: torna al menu principale e ritrova 'Impostazioni' ({SelName})", PanelOpen("mainPanel") && SelName == "Pulsante Impostazioni");
            yield return Tap(PadControls.B);
            Check("B nel menu principale: non fa niente", PanelOpen("mainPanel") && GameMenu.InMenu);

            // ---- guida: tre pagine
            yield return Tap(PadControls.DpadUp);
            yield return Tap(PadControls.A);
            Check($"A su 'Come si gioca': si apre, scelta la pagina 'Obiettivo' ({SelName})", PanelOpen("helpPanel") && SelName == "Pulsante Obiettivo");
            Check("la guida nomina i tasti del controller", Get<Text>("helpRound").text.Contains("X tenuto"));
            yield return Tap(PadControls.DpadRight); yield return Tap(PadControls.DpadRight);
            Check($"croce a destra due volte: la pagina 'Controller', che si apre da sola ({SelName})", SelName == "Pulsante Controller" && GameObject.Find("Pagina Controller") != null && GameObject.Find("Pagina Obiettivo") == null);
            yield return Tap(PadControls.B);
            Check($"B: indietro, sul pulsante da cui si era partiti ({SelName})", PanelOpen("mainPanel") && SelName == "Pulsante Come si gioca");

            // ---- cooperativa: un campo di testo non intrappola il controller
            yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.DpadUp); yield return Tap(PadControls.DpadUp);
            Check($"croce in alto tre volte: 'Cooperativa' ({SelName})", SelName == "Pulsante Cooperativa");
            yield return Tap(PadControls.A);
            Check($"A su 'Cooperativa': si apre, scelto un pulsante ({SelName})", PanelOpen("coopPanel") && Selected != null && Selected.GetComponent<Button>() != null);
            yield return Tap(PadControls.DpadUp);
            var field = Selected != null ? Selected.GetComponent<InputField>() : null;
            Check($"croce in alto: il campo dell'indirizzo, senza che inizi la scrittura ({SelName})", field != null && !field.isFocused);
            yield return Tap(PadControls.A);
            Check("A sul campo: inizia la scrittura", field != null && field.isFocused);
            yield return Tap(PadControls.B);
            Check("B nel campo: chiude la scrittura e resta nella schermata", field != null && !field.isFocused && PanelOpen("coopPanel"));
            yield return Tap(PadControls.A); yield return Tap(PadControls.DpadDown);
            Check($"croce in basso nel campo: chiude la scrittura e passa al pulsante ({SelName})", field != null && !field.isFocused && Selected != null && Selected.GetComponent<Button>() != null);
            yield return Tap(PadControls.B);
            Check($"B: indietro, su 'Cooperativa' ({SelName})", PanelOpen("mainPanel") && SelName == "Pulsante Cooperativa");

            // ---- niente di selezionato: il primo comando sceglie il pulsante e basta
            EventSystem.current.SetSelectedGameObject(null);
            yield return Tap(ly: -30000);
            Check($"senza selezione, stick in basso: sceglie 'Gioca' e non si muove oltre ({SelName})", SelName == "Pulsante Gioca");
            yield return Tap(PadControls.DpadDown);
            Check($"il comando dopo si muove ({SelName})", SelName == "Pulsante Cooperativa");
            yield return Tap(PadControls.DpadUp);
            GameSettings.SetMaster(master0);

            // ---- la scelta della modalita': 'Gioca' chiede come giocare (standard o il deserto), col controller come col mouse
            bool mode0 = Deserto.DesertMode.Requested;       // la scelta si ricorda nelle preferenze: questa prova la rimette com'era
            yield return Tap(PadControls.A);
            string remembered = mode0 ? "Pulsante Il deserto" : "Pulsante Standard";
            Check($"A su 'Gioca': non parte, si apre 'Come vuoi giocare?' con la scelta dell'ultima volta ({SelName})", PanelOpen("modePanel") && !PanelOpen("mainPanel") && GameMenu.InMenu && SelName == remembered);
            yield return Tap(PadControls.DpadRight);
            Check($"croce a destra: 'Il deserto' ({SelName})", SelName == "Pulsante Il deserto");
            Check("la cornice sta anche sulla scelta della modalita'", RingOnSelection());
            yield return Tap(PadControls.DpadLeft);
            Check($"croce a sinistra: 'Standard' ({SelName})", SelName == "Pulsante Standard");
            yield return Tap(PadControls.B);
            Check($"B: indietro, sul pulsante da cui si era partiti ({SelName})", PanelOpen("mainPanel") && !PanelOpen("modePanel") && SelName == "Pulsante Gioca" && GameMenu.InMenu);
            yield return Tap(PadControls.A);
            yield return Tap(PadControls.DpadLeft);
            Check($"di nuovo 'Gioca', poi la croce a sinistra: 'Standard' ({SelName})", PanelOpen("modePanel") && SelName == "Pulsante Standard");

            // ---- in partita
            bool jumped = false;
            PadControls.Inject(Pad(PadControls.A));
            for (int i = 0; i < 6; i++) { jumped |= TouchControls.Down(TouchControls.Action.Jump); yield return null; }
            PadControls.Inject(Pad());
            for (int i = 0; i < 6; i++) { jumped |= TouchControls.Down(TouchControls.Action.Jump); yield return null; }
            Check("A su 'Standard': la partita parte nella modalita' standard e il giocatore non salta", !GameMenu.InMenu && !GameMenu.Blocked && !jumped && !Deserto.DesertMode.Requested);
            Deserto.DesertMode.Requested = mode0;            // la scelta di chi gioca come prima (la partita e' gia' partita)

            deadline = Time.realtimeSinceStartup + 120f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            var player = GameManager.I != null ? GameManager.I.player : null;
            Check("il piano c'e' e il giocatore e' vivo", player != null && player.alive);
            if (player == null) yield break;
            player.invulnerable = true;

            yield return Tap(PadControls.StartBtn);
            Check($"Start: la pausa si apre, scelto 'Riprendi' ({SelName})", GameMenu.Paused && GameMenu.Blocked && SelName == "Pulsante Riprendi");
            Check("la pausa nomina Start e mostra i tasti del controller", Get<Text>("pauseHint").text.Contains("Start") && Get<Text>("padHelp").gameObject.activeInHierarchy);
            yield return Tap(PadControls.B);
            Check("B in pausa: riprende", !GameMenu.Paused && !GameMenu.Blocked);
            yield return Tap(PadControls.B);
            Check("B in gioco: non apre la pausa (e' 'abbassati')", !GameMenu.Paused && !GameMenu.Blocked);

            yield return Tap(PadControls.StartBtn);
            yield return Tap(PadControls.DpadDown);
            yield return Tap(PadControls.A);
            Check($"pausa, croce in basso, A: le impostazioni ({SelName})", PanelOpen("settingsPanel"));
            yield return Tap(PadControls.B);
            Check($"B: torna alla pausa, su 'Impostazioni' ({SelName})", PanelOpen("pausePanel") && SelName == "Pulsante Impostazioni");
            yield return Tap(PadControls.StartBtn);
            Check("Start in pausa: riprende", !GameMenu.Paused && !GameMenu.Blocked);

            yield return Tap(PadControls.StartBtn);
            jumped = false;
            PadControls.Inject(Pad(PadControls.A));
            for (int i = 0; i < 6; i++) { jumped |= TouchControls.Down(TouchControls.Action.Jump); yield return null; }
            PadControls.Inject(Pad());
            for (int i = 0; i < 6; i++) { jumped |= TouchControls.Down(TouchControls.Action.Jump); yield return null; }
            Check($"A su 'Riprendi': riprende e il giocatore non salta", !GameMenu.Paused && !GameMenu.Blocked && !jumped);
            yield return new WaitForSecondsRealtime(0.3f);
            PadControls.Inject(Pad(PadControls.A));
            yield return null; yield return null;
            Check("A in gioco: il comando di salto c'e' di nuovo", TouchControls.Down(TouchControls.Action.Jump) || TouchControls.Held(TouchControls.Action.Jump));
            PadControls.Inject(null);
            player.invulnerable = false;
        }
    }
}
#endif
