using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes.EditorTools
{
    /// <summary>Render and validate menu layouts without running the game or changing player display preferences.
    /// 0.8.0: the five settings tabs (rows, hints, navigation chains, the values each row writes and the reset of each tab), the three guide
    /// pages, the tips under the quote and the pause summary. The settings the test changes are put back at the end.</summary>
    public static class MenuReview
    {
        static int failures;
        static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Debug.Log("MENU REVIEW " + (ok ? "PASS " : "FAIL ") + label);
        }

        static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner);

        public static void Run()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../unity_logs/menu-review"));
            Directory.CreateDirectory(output);
            var restore = Snapshot();
            try { RunChecks(output); }
            finally { restore(); }
            File.WriteAllText(Path.Combine(output, "result.txt"), failures == 0 ? "PASS" : "FAIL " + failures);
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void RunChecks(string output)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 10f;
            GameMenu.Create();
            var menu = GameMenu.I;
            if (menu.GetComponent<Canvas>() == null) menu.SendMessage("Awake");
            var canvas = menu.GetComponent<Canvas>();
            menu.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;

            var version = menu.transform.Find("Velo/Versione")?.GetComponent<Text>();
            Check("version is x.y.z (" + Application.version + ")", System.Text.RegularExpressions.Regex.IsMatch(Application.version, @"^\d+\.\d+\.\d+$"));
            Check("version label says " + Application.version, version != null && version.text == GameMenu.VersionText);
            var wide = VideoSettings.Modes(FullScreenMode.Windowed, 2);
            Check("QHD ultrawide preset", wide.Contains(new Vector2Int(3440, 1440)));
            Check("ultrawide filter", wide.TrueForAll(r => VideoSettings.Aspect(r.x, r.y) == "21:9 ULTRAWIDE"));
            var invalid = VideoSettings.Validate(new VideoSettings.Config { width = -1, height = 0, mode = (FullScreenMode)999, hz = -3, vsync = 99, fps = 777 });
            Check("invalid settings constrained", invalid.width == 640 && invalid.height == 360 && invalid.mode == FullScreenMode.FullScreenWindow && invalid.hz == 0 && invalid.vsync == 1 && invalid.fps == -1);

            CheckTips(menu);
            CheckValues(menu);

            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(3440, 1440), new Vector2Int(1024, 768) })
            {
                var rt = new RenderTexture(size.x, size.y, 24); rt.Create(); camera.targetTexture = rt;
                canvas.scaleFactor = Mathf.Min(size.x / 1280f, size.y / 720f);
                menu.SendMessage("Show", menu.transform.Find("Velo/Principale").GetComponent<RectTransform>());
                menu.SendMessage("UpdateTips");                      // un consiglio in vista, come nel gioco
                Render("main", size, camera, menu, output);

                Click("Impostazioni");
                Click("Schermo");                       // si riapre sull'ultima scheda usata: qui si parte dalla prima
                CheckPage(menu, "Schermo", 0, 6, 7);
                Render("display", size, camera, menu, output);
                Click("Immagine"); CheckPage(menu, "Immagine", 5, 3, 8); Render("image", size, camera, menu, output);
                Click("Audio"); CheckPage(menu, "Audio", 5, 1, 6); Render("audio", size, camera, menu, output);
                Click("Comandi"); CheckPage(menu, "Comandi", 2, 5, 8); Render("controls", size, camera, menu, output);
                Click("Gioco"); CheckPage(menu, "Gioco", 5, 4, 9); Render("game", size, camera, menu, output);
                Click("Schermo");
                Choice("Formato").Step(1); Choice("Formato").Step(1);
                Check("ultrawide choice", GameObject.Find("Formato valore").GetComponent<Text>().text.Contains("21:9"));
                Render("ultrawide-options", size, camera, menu, output);
                Click("Comandi"); Click("Elenco dei comandi");
                Check("the controls list opens the keyboard page", GameObject.Find("Pagina Tastiera") != null && GameObject.Find("Pagina Obiettivo") == null);
                Render("help-keys-from-settings", size, camera, menu, output);
                Click("Indietro");
                Check("back from the guide returns to the settings", GameObject.Find("Pulsante Ripristina scheda") != null);
                Click("Indietro");
                Click("Come si gioca"); CheckHelpColumns(menu); Render("help", size, camera, menu, output);
                Click("Tastiera"); CheckHelpColumns(menu); Render("help-keys", size, camera, menu, output);
                Click("Controller"); CheckHelpColumns(menu); Render("help-pad", size, camera, menu, output);
                Click("Indietro"); Click("Cooperativa"); Render("coop", size, camera, menu, output); Click("Indietro");
                GameManager.proc = true;        // ai piani dell'hotel i bottoni dello schermo condiviso sono accesi e il pannello dice quanti controller servono
                Click("Schermo condiviso"); Render("split", size, camera, menu, output);
                // con un monitor solo il bottone c'e' lo stesso: lo dice e, premuto, spiega come averne due (il gioco li conta all'avvio)
                Check("one monitor: the monitor button says there is only one", HasMonitorButton(menu) && HasText(menu, "Monitor: uno solo"));
                Check("one monitor: the line counts the monitors", HasTextContaining(menu, "Monitor collegati: 1"));
                menu.SendMessage("SplitCycleMonitors");
                Check("one monitor: the button explains what to do", HasText(menu, "Il gioco vede un solo monitor"));
                Render("split-1-monitor-hint", size, camera, menu, output);
                Click("Indietro");
                // con piu' monitor collegati la riga ha due bottoni e il pannello dice come si dividono i giocatori (due monitor, poi il massimo: quattro)
                SplitScreen.TestConnected = 2; SplitScreen.TestWanted = 2;
                Click("Schermo condiviso"); Render("split-2-monitor", size, camera, menu, output);
                Check("two monitors: the monitor button is on the panel", HasMonitorButton(menu));
                Check("two monitors: the panel says how the players are divided", HasText(menu, "Su 2 monitor: 2 giocatori 1+1"));
                Click("Indietro");
                SplitScreen.TestConnected = 4; SplitScreen.TestWanted = 4;
                Click("Schermo condiviso"); Render("split-4-monitor", size, camera, menu, output);
                Check("four monitors: the panel says how the players are divided", HasText(menu, "Su 4 monitor: 2 giocatori 1+1"));
                Click("Indietro");
                SplitScreen.TestConnected = 0; SplitScreen.TestWanted = 0;
                // i personaggi (0.9): l'elenco, il nuovo, l'eliminazione e la scelta di chi gioca ai posti dello schermo condiviso
                Click("Personaggi"); Render("characters", size, camera, menu, output);
                Click("Nuovo personaggio"); Render("character-new", size, camera, menu, output); Click("Annulla");
                Click("Elimina"); Render("character-delete", size, camera, menu, output); Click("Annulla");
                Click("Indietro");
                Check("the main menu names the character that plays", HasText(menu, "Prova") || HasText(menu, "crea il tuo personaggio"));
                Check("the main menu offers the wiki, and the wiki is there to open", HasText(menu, "Wiki") && WikiLink.Available);
                var tester = Characters.Roster.Local;
                if (tester != null)
                {
                    var worst = new Characters.RunRecord { branch = 3, floor = 12, seed = 11, score = 1234567, savedAt = "2026-11-18T10:00:00Z" };
                    worst.carried = Characters.RunSave.FromBundle(Production.Economy.Bundle.Of((Cattedrale.Risorse.Ossa, 9999), (Cattedrale.Risorse.Legno, 9999), (Cattedrale.Risorse.Acciaio, 9999),
                        (Cattedrale.Risorse.Brace, 9999), (Cattedrale.Risorse.Cuori, 9999), (Cattedrale.Risorse.Sangue, 9999), (Cattedrale.Risorse.Reliquie, 9999),
                        (Cattedrale.Risorse.Oscurita, 9999), (Cattedrale.Risorse.Fede, 9999), (Cattedrale.Risorse.Gemme, 9999), (Cattedrale.Risorse.Oboli, 9999)));
                    Check("a suspended expedition is written", Characters.Roster.SaveRun(tester, worst) && Characters.Roster.HasRun(tester));
                    Click("Personaggi");
                    Check("the character list names the suspended expedition", Field<Text>(menu, "charBody").text.Contains("Spedizione sospesa"));
                    Render("characters-run", size, camera, menu, output);
                    Click("Indietro");
                    Click("Gioca"); Render("run", size, camera, menu, output);
                    Check("Gioca asks what to do with a suspended expedition", HasText(menu, "Una spedizione ti aspetta") && GameObject.Find("Pulsante Riprendi la spedizione") != null && GameObject.Find("Pulsante Ricomincia dalla cattedrale") != null);
                    Check("the suspended expedition panel tells what the backpack holds", HasText(menu, "Nello zaino: 9999 Ossa"));
                    Click("Indietro");
                    Check("Indietro returns to the main menu", GameObject.Find("Pulsante Gioca") != null);
                    Characters.Roster.ClearRun(tester);
                    Check("the suspended expedition is gone", !Characters.Roster.HasRun(tester));
                    // la scelta della modalita': Gioca, senza spedizione sospesa, chiede standard o deserto (non si preme nessuno dei due: partirebbe la partita
                    // e la scelta si scriverebbe nelle preferenze di chi gioca)
                    Click("Gioca"); Render("mode", size, camera, menu, output);
                    Check("Gioca asks how to play: standard or the desert", HasText(menu, "Come vuoi giocare?") && GameObject.Find("Pulsante Standard") != null && GameObject.Find("Pulsante Il deserto") != null);
                    Check("the mode panel describes both ways", HasText(menu, "I piani sotto la cattedrale") && HasText(menu, "Un deserto enorme"));
                    Click("Indietro");
                    Check("Indietro returns from the mode choice to the main menu", GameObject.Find("Pulsante Gioca") != null && !HasText(menu, "Come vuoi giocare?"));
                }
                Click("Schermo condiviso"); Click("3 giocatori"); Render("split-cast", size, camera, menu, output);
                Check("the cast panel lists three seats", HasText(menu, "Giocatore 3:"));
                Click("Indietro"); Click("Indietro");
                GameManager.proc = false;
                if (size.x == 1024)
                {
                    menu.SendMessage("Play"); menu.SendMessage("Pause");
                    Check("pause stops the world", GameMenu.Paused && GameMenu.Blocked && Time.timeScale == 0f);
                    var stats = Field<Text>(menu, "pauseStats");
                    stats.text = "ONDATA XXIII  ·  PIANO 12  ·  1.234.567 PUNTI  ·  12345 UCCISIONI  ·  123:45";      // il caso peggiore
                    Render("pause", size, camera, menu, output);
                    Click("Torna al menu"); Render("leave", size, camera, menu, output); Click("Annulla");
                    GameMenu.TestCanSave = true;                // con il salvataggio i bottoni sono tre
                    Click("Torna al menu"); Render("leave-save", size, camera, menu, output);
                    Check("the leave panel offers to save", GameObject.Find("Pulsante Salva e torna al menu") != null);
                    Click("Annulla");
                    GameMenu.TestCanSave = null;
                    Click("Riprendi"); Check("resume releases input", !GameMenu.Blocked && Time.timeScale == 1f);
                }
                camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt);
            }
            UnityEngine.Object.DestroyImmediate(menu.gameObject);
        }

        // ---------- le impostazioni: cosa scrive ogni riga ----------

        static void CheckValues(GameMenu menu)
        {
            Click("Impostazioni");
            // un tono ogni volta che si conferma, tranne con "Suoni dell'interfaccia" spenti
            Click("Audio");
            int requests = MenuSound.Requests;
            MenuSound.Confirm();
            Check("a confirm tone is requested", MenuSound.Requests == requests + 1);
            var sounds = Choice("Suoni dell'interfaccia");
            sounds.Step(1);
            Check("ui sounds can be switched off (" + sounds.Value + ")", !GameSettings.UiSounds && sounds.Value == "Disattivati");
            requests = MenuSound.Requests;
            MenuSound.Confirm();
            Check("no tone with the ui sounds off", MenuSound.Requests == requests);
            sounds.Step(1);
            Check("ui sounds back on", GameSettings.UiSounds && sounds.Value == "Attivi");

            var sliders = menu.GetComponentsInChildren<Slider>();
            sliders[3].value = 0.4f; sliders[4].value = 0.25f;
            Check("ambience slider writes the setting", Mathf.Abs(GameSettings.Ambience - 0.4f) < 0.001f && Text("Ambiente valore") == "40%");
            Check("menu theme slider writes the setting", Mathf.Abs(GameSettings.MenuMusic - 0.25f) < 0.001f && Text("Tema del menu valore") == "25%");
            Click("Ripristina scheda");
            Check("audio reset restores the defaults", GameSettings.Ambience == 1f && GameSettings.MenuMusic == 1f && GameSettings.Master == 1f && GameSettings.UiSounds
                && Text("Ambiente valore") == "100%" && Mathf.Abs(sliders[3].value - 1f) < 0.001f);

            Click("Immagine");
            sliders = menu.GetComponentsInChildren<Slider>();
            sliders[2].value = 0.3f; sliders[3].value = 0.2f; sliders[4].value = 0.1f;
            Check("film grain, vignette and bloom write their settings", Mathf.Abs(GameSettings.GrainAmount - 0.3f) < 0.001f
                && Mathf.Abs(GameSettings.VignetteAmount - 0.2f) < 0.001f && Mathf.Abs(GameSettings.BloomAmount - 0.1f) < 0.001f && Text("Grana della pellicola valore") == "30%");
            var shadows = Choice("Qualità delle ombre");
            shadows.Step(1);
            Check("shadow quality wraps from high to low (" + shadows.Value + ")", GameSettings.ShadowQuality == 0 && shadows.Value == "Bassa");
            shadows.Step(-1);
            Check("shadow quality steps back (" + shadows.Value + ")", GameSettings.ShadowQuality == 2 && shadows.Value == "Alta");
            var scale = Choice("Risoluzione di rendering");
            scale.Step(-1);
            Check("render scale steps down (" + scale.Value + ")", GameSettings.RenderScalePercent == 90 && scale.Value == "90%");
            var aa = Choice("Anti-aliasing");
            aa.Step(1);
            Check("anti-aliasing wraps to off (" + aa.Value + ")", GameSettings.Msaa == 1 && aa.Value == "Disattivato");
            Click("Ripristina scheda");
            Check("image reset restores the defaults", GameSettings.GrainAmount == 1f && GameSettings.RenderScalePercent == 100 && GameSettings.Msaa == 4
                && GameSettings.ShadowQuality == 2 && Choice("Anti-aliasing").Value == "MSAA 4×" && Choice("Risoluzione di rendering").Value == "100%  (nativa)");

            Click("Comandi");
            sliders = menu.GetComponentsInChildren<Slider>();
            sliders[1].value = 2f;
            Check("controller sensitivity writes the setting", Mathf.Abs(GameSettings.PadSensitivity - 2f) < 0.001f && Text("Sensibilità controller valore") == "2.00×");
            Choice("Asse Y del mouse").Step(1); Choice("Asse Y del controller").Step(1);
            Choice("Mira").Step(1); Choice("Abbassarsi").Step(1); Choice("Corsa").Step(1);
            Check("the two inverted axes and the three toggles switch on", GameSettings.InvertMouseY && GameSettings.InvertPadY && GameSettings.AimToggle
                && GameSettings.CrouchToggle && GameSettings.SprintToggle && Choice("Corsa").Value == "Interruttore");
            Click("Ripristina scheda");
            Check("controls reset restores the defaults", !GameSettings.InvertMouseY && !GameSettings.InvertPadY && !GameSettings.AimToggle && !GameSettings.CrouchToggle
                && !GameSettings.SprintToggle && GameSettings.PadSensitivity == 1f && Choice("Mira").Value == "Tieni premuto");

            // dalla 0.9.1 il contatore dei fotogrammi e' acceso di base: la scheda parte dai valori di base anche se questo PC ne ricorda altri (un Ripristina scheda di una versione prima scriveva 'spento')
            GameSettings.SetShowFps(true); GameSettings.SetPauseOnFocusLoss(true); GameSettings.SetThirdPerson(false);
            Click("Gioco");
            var perspective = Choice("Prospettiva");
            Check("the perspective starts in first person (" + perspective.Value + ")", !GameSettings.ThirdPerson && perspective.Value == "Prima persona");
            perspective.Step(1);
            Check("the perspective row switches to the third person (" + perspective.Value + ")", GameSettings.ThirdPerson && perspective.Value == "Terza persona");
            Choice("Mirino").Step(1);
            Check("crosshair steps to the dot (" + Choice("Mirino").Value + ")", GameSettings.Crosshair == GameSettings.CrosshairDot && Choice("Mirino").Value == "Solo il punto");
            Choice("Mirino").Step(1); Choice("Mirino").Step(1);
            Check("crosshair wraps back to classic", GameSettings.Crosshair == GameSettings.CrosshairClassic);
            Choice("Contatore fotogrammi").Step(1); Choice("Pausa a finestra inattiva").Step(1);
            Check("fps counter off (it starts on since 0.9.1), pause on focus loss off", !GameSettings.ShowFps && !GameSettings.PauseOnFocusLoss);
            sliders = menu.GetComponentsInChildren<Slider>();
            sliders[0].value = 3.5f; sliders[1].value = 0.3f; sliders[2].value = -0.6f;
            Check("third-person camera sliders write distance, height and shoulder",
                GameSettings.ThirdPersonFraming == new Vector3(-0.6f, 0.3f, 3.5f) && Text("Spalla 3ª persona valore") == "Sx 60 cm");
            sliders[3].value = 0.5f;
            Check("visual motion writes the setting", Mathf.Abs(GameSettings.Motion - 0.5f) < 0.001f);
            Click("Ripristina scheda");
            Check("game reset restores the defaults", GameSettings.ShowFps && GameSettings.PauseOnFocusLoss && GameSettings.Motion == 1f && GameSettings.CoopHudScale == 1f
                && !GameSettings.ThirdPerson && Choice("Prospettiva").Value == "Prima persona"
                && GameSettings.ThirdPersonFraming == GameSettings.DefaultThirdPersonFraming && Text("Spalla 3ª persona valore") == "Dx 60 cm");

            Click("Schermo");
            Check("the display tab has no reset button", GameObject.Find("Pulsante Ripristina scheda") == null);
            Click("Indietro");
        }

        /// <summary>Le righe di una scheda: quanti cursori e scelte, un suggerimento che sta in due righe per ognuna, la catena di navigazione
        /// da una scheda in giu' fino a "Indietro" che passa per tutte le righe accese.</summary>
        static void CheckPage(GameMenu menu, string tab, int sliders, int choices, int rows)
        {
            Check(tab + " has " + sliders + " sliders", menu.GetComponentsInChildren<Slider>().Length == sliders);
            Check(tab + " has " + choices + " choices", menu.GetComponentsInChildren<MenuChoice>().Length == choices);
            var hintLabel = Field<Text>(menu, "settingsHint");
            var panel = menu.transform.Find("Velo/Impostazioni");
            int hints = 0;
            foreach (var feedback in menu.GetComponentsInChildren<MenuFeedback>())
            {
                if (!feedback.transform.IsChildOf(panel)) continue;
                hints++;
                hintLabel.text = feedback.Hint;
                Canvas.ForceUpdateCanvases();
                Check(tab + " hint of " + feedback.name + " fits two lines", !string.IsNullOrEmpty(feedback.Hint) && hintLabel.preferredHeight <= hintLabel.rectTransform.rect.height + 2f);
            }
            // schede e "Indietro" (e "Ripristina scheda") hanno un suggerimento come ogni riga
            bool hasReset = GameObject.Find("Pulsante Ripristina scheda") != null;
            Check(tab + " has a hint on every row, tab, Indietro" + (hasReset ? " and reset" : ""), hints == rows + 5 + 1 + (hasReset ? 1 : 0));
            var start = GameObject.Find("Pulsante " + tab).GetComponent<Selectable>();
            int live = 0;                                           // le righe accese: quelle spente (la frequenza fuori dall'esclusivo...) si saltano
            foreach (var row in panel.Find("Scheda " + tab).GetComponentsInChildren<Selectable>()) if (row.IsInteractable()) live++;
            int steps = 0;
            var at = start;
            while (at != null && steps < 20)
            {
                var down = at.FindSelectableOnDown();
                if (down == null) break;
                at = down; steps++;
            }
            Check(tab + " chain runs tab > " + live + " live rows > Indietro (" + steps + " steps, ends on " + (at != null ? at.name : "-") + ")",
                steps == live + 1 && at != null && at.name == "Pulsante Indietro");
            var left = start.FindSelectableOnLeft(); var right = start.FindSelectableOnRight();
            Check(tab + " tab has its neighbours", (tab == "Schermo" ? left == null : left != null) && (tab == "Gioco" ? right == null : right != null));
            hintLabel.text = "";
        }

        static void CheckTips(GameMenu menu)
        {
            var label = Field<Text>(menu, "tipLabel");
            Check("there are at least ten tips", GameMenu.TipCount >= 10);
            for (int i = 0; i < GameMenu.TipCount; i++)
                foreach (bool pad in new[] { false, true })
                {
                    label.text = GameMenu.TipFor(i, pad);
                    Canvas.ForceUpdateCanvases();
                    Check("tip " + i + (pad ? " (controller)" : "") + " fits two lines (" + label.preferredHeight.ToString("0") + ")", label.text.Length > 20 && label.preferredHeight <= label.rectTransform.rect.height + 2f);
                }
            label.text = "";
        }

        /// <summary>Nella guida dei tasti ogni colonna e' fatta di due testi (i tasti e cosa fanno) che si allineano solo se hanno le stesse righe.</summary>
        static void CheckHelpColumns(GameMenu menu)
        {
            foreach (var (pageName, columns) in new[]
            {
                ("Pagina Tastiera", new[] { "Movimento e combattimento", "Equipaggiamento e magie", "Orientarsi" }),
                ("Pagina Controller", new[] { "Movimento e mira", "Combattimento e magia", "Croce e altro" }),
            })
            {
                var page = GameObject.Find(pageName);
                if (page == null) continue;     // la pagina aperta e' un'altra
                foreach (var column in columns)
                {
                    var a = page.transform.Find(column + " tasti")?.GetComponent<Text>();
                    var b = page.transform.Find(column + " azioni")?.GetComponent<Text>();
                    Check("help column " + column + " aligns", a != null && b != null && a.text.Split('\n').Length == b.text.Split('\n').Length);
                    // il tasto piu' largo deve restare nella sua colonna (104 unita'): il testo ha l'eccedenza aperta e andrebbe sopra l'azione
                    if (a == null) continue;
                    string original = a.text;
                    foreach (var line in original.Split('\n'))
                    {
                        if (line.Length == 0 || line.Contains("<color")) continue;          // un titolo: la riga accanto e' vuota
                        a.text = line;
                        Check("help key '" + line + "' fits its column (" + a.preferredWidth.ToString("0") + ")", a.preferredWidth <= 104f);
                    }
                    a.text = original;
                }
            }
        }

        // ---------- aiuti ----------

        static bool HasText(GameMenu menu, string start)
        {
            foreach (var t in menu.GetComponentsInChildren<Text>()) if (t.text.StartsWith(start, StringComparison.Ordinal)) return true;
            return false;
        }

        static bool HasTextContaining(GameMenu menu, string part)
        {
            foreach (var t in menu.GetComponentsInChildren<Text>()) if (t.text.Contains(part)) return true;
            return false;
        }

        static bool HasMonitorButton(GameMenu menu) => HasText(menu, "Monitor: ");

        static void Click(string label)
        {
            var go = GameObject.Find("Pulsante " + label);
            if (go == null) throw new InvalidOperationException(label);
            go.GetComponent<Button>().onClick.Invoke();
        }

        static MenuChoice Choice(string title)
        {
            var go = GameObject.Find("Scelta " + title);
            if (go == null) throw new InvalidOperationException("Scelta " + title);
            return go.GetComponent<MenuChoice>();
        }

        static string Text(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<Text>().text : null;
        }

        /// <summary>Le impostazioni come sono adesso, e il modo di rimetterle: la prova le cambia tutte.</summary>
        static Action Snapshot()
        {
            float master = GameSettings.Master, effects = GameSettings.Effects, music = GameSettings.Music, ambience = GameSettings.Ambience, menuMusic = GameSettings.MenuMusic;
            float brightness = GameSettings.Brightness, fov = GameSettings.Fov, grain = GameSettings.GrainAmount, vignette = GameSettings.VignetteAmount, bloom = GameSettings.BloomAmount;
            float sensitivity = GameSettings.Sensitivity, padSensitivity = GameSettings.PadSensitivity, motion = GameSettings.Motion, coopHud = GameSettings.CoopHudScale;
            Vector3 framing = GameSettings.ThirdPersonFraming;
            bool thirdPerson = GameSettings.ThirdPerson;
            var cameraKeys = new[] { "video_thirdperson", "video_third_distance", "video_third_height", "video_third_shoulder" };
            var cameraKeysPresent = Array.ConvertAll(cameraKeys, PlayerPrefs.HasKey);
            int scale = GameSettings.RenderScalePercent, msaa = GameSettings.Msaa, shadows = GameSettings.ShadowQuality, aim = GameSettings.AimMode, crouch = GameSettings.CrouchMode,
                sprint = GameSettings.SprintMode, crosshair = GameSettings.Crosshair;
            bool ui = GameSettings.UiSounds, invertY = GameSettings.InvertMouseY, invertPad = GameSettings.InvertPadY, fps = GameSettings.ShowFps, pause = GameSettings.PauseOnFocusLoss;
            return () =>
            {
                GameSettings.SetMaster(master); GameSettings.SetEffects(effects); GameSettings.SetMusic(music); GameSettings.SetAmbience(ambience); GameSettings.SetMenuMusic(menuMusic);
                GameSettings.SetBrightness(brightness); GameSettings.SetFov(fov); GameSettings.SetGrain(grain); GameSettings.SetVignette(vignette); GameSettings.SetBloom(bloom);
                GameSettings.SetSensitivity(sensitivity); GameSettings.SetPadSensitivity(padSensitivity); GameSettings.SetMotion(motion); GameSettings.SetCoopHudScale(coopHud);
                GameSettings.SetRenderScale(scale); GameSettings.SetMsaa(msaa); GameSettings.SetShadowQuality(shadows);
                GameSettings.SetAimMode(aim); GameSettings.SetCrouchMode(crouch); GameSettings.SetSprintMode(sprint); GameSettings.SetCrosshair(crosshair);
                GameSettings.SetUiSounds(ui); GameSettings.SetInvertMouseY(invertY); GameSettings.SetInvertPadY(invertPad); GameSettings.SetShowFps(fps); GameSettings.SetPauseOnFocusLoss(pause);
                GameSettings.SetThirdPerson(thirdPerson); GameSettings.SetThirdDistance(framing.z); GameSettings.SetThirdHeight(framing.y); GameSettings.SetThirdShoulder(framing.x);
                for (int i = 0; i < cameraKeys.Length; i++) if (!cameraKeysPresent[i]) PlayerPrefs.DeleteKey(cameraKeys[i]);
                PlayerPrefs.Save();
            };
        }

        /// <summary>La versione sta a destra sulla riga dei suggerimenti dei tasti: non deve toccare quelli della tastiera ne' quelli del controller
        /// (il piu' lungo dei due decide). Le larghezze sono quelle vere del testo nel carattere del menu.</summary>
        static void CheckVersionClear(string name, GameMenu menu)
        {
            var version = menu.transform.Find("Velo/Versione")?.GetComponent<Text>();
            var footer = menu.transform.Find("Velo/Footer")?.GetComponent<Text>();
            if (version == null || footer == null) { Check(name + " version label and footer exist", false); return; }
            string shown = footer.text;
            float versionLeft = version.rectTransform.anchoredPosition.x + version.rectTransform.rect.width * 0.5f - version.preferredWidth;
            foreach (string hints in new[] { GameMenu.FooterKeys, GameMenu.FooterPad })
            {
                footer.text = hints;
                float footerRight = footer.rectTransform.anchoredPosition.x + footer.preferredWidth * 0.5f;
                Check(name + " version clear of footer by " + (versionLeft - footerRight).ToString("0") + " units", versionLeft - footerRight >= 24f);
            }
            footer.text = shown;
        }

        static void Render(string name, Vector2Int size, Camera camera, GameMenu menu, string output)
        {
            foreach (var art in menu.GetComponentsInChildren<MenuEngraving>()) { art.SetAllDirty(); }
            // sulle impostazioni il suggerimento della prima riga e' in vista, come quando ci si posa sopra
            var panel = menu.transform.Find("Velo/Impostazioni");
            var hintLabel = Field<Text>(menu, "settingsHint");
            if (panel != null && panel.gameObject.activeInHierarchy && string.IsNullOrEmpty(hintLabel.text))
                foreach (var feedback in panel.GetComponentsInChildren<MenuFeedback>()) if (feedback.GetComponent<Slider>() != null || feedback.GetComponent<MenuChoice>() != null) { hintLabel.text = feedback.Hint; break; }
            Canvas.ForceUpdateCanvases();
            camera.Render(); Canvas.ForceUpdateCanvases();
            foreach (var art in menu.GetComponentsInChildren<MenuEngraving>())
                Check("engraving rendered " + art.name, art.canvasRenderer.GetMesh().vertexCount > 0);
            foreach (var text in menu.GetComponentsInChildren<Text>())
            {
                Check(name + " text fits " + text.name, text.preferredHeight <= text.rectTransform.rect.height + 2f);
                var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
                foreach (var p in corners)
                {
                    var screen = camera.WorldToViewportPoint(p);
                    if (screen.x < -0.001f || screen.y < -0.001f || screen.x > 1.001f || screen.y > 1.001f)
                    { Check(name + " text in screen " + text.name, false); break; }
                }
            }
            CheckVersionClear(name, menu);
            camera.Render();
            var before = RenderTexture.active; RenderTexture.active = camera.targetTexture;
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply(); RenderTexture.active = before;
            File.WriteAllBytes(Path.Combine(output, name + "-" + size.x + "x" + size.y + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            if (panel != null) hintLabel.text = "";
        }
    }
}
