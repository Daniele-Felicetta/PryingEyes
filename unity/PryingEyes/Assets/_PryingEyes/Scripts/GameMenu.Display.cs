using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace PryingEyes
{
    /// <summary>La scheda Schermo delle impostazioni: modalita', formato, risoluzione, frequenza, VSync e limite dei fotogrammi, con la prova di
    /// quindici secondi prima di tenere la nuova immagine (se non si conferma torna quella di prima). A differenza delle altre schede non salva
    /// a ogni passo: le scelte restano in una bozza (displayDraft) finche' non si preme "Applica schermo".</summary>
    public partial class GameMenu
    {
        VideoSettings.Config displayDraft, displayPrevious;
        int aspectFilter;
        bool displayPending;
        float displayDeadline;
        RectTransform displayConfirm;
        Text displayNote, previewCountdown, displayActual;
        MenuChoice modeChoice, aspectChoice, resolutionChoice, rateChoice, vsyncChoice, fpsChoice;
        Button applyDisplayButton;

        static string ModeName(FullScreenMode mode) =>
            mode == FullScreenMode.Windowed ? "Finestra" : mode == FullScreenMode.ExclusiveFullScreen ? "Schermo intero esclusivo" : "Schermo intero senza bordi";
        static readonly string[] AspectNames = { "Tutti i formati", "16:9 · panoramico", "21:9 · ultrawide", "16:10" };

        void BuildDisplayPage(SettingsPage p)
        {
            modeChoice = AddChoice(p, "Modalità schermo", "Schermo intero senza bordi è il più comodo; l'esclusivo permette di scegliere la frequenza del monitor.",
                () => ModeName(displayDraft.mode), d =>
                {
                    var modes = new[] { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
                    displayDraft.mode = modes[Wrap(System.Array.IndexOf(modes, displayDraft.mode) + d, modes.Length)];
                    displayDraft.hz = 0; NormalizeDisplay();
                });
            aspectChoice = AddChoice(p, "Formato", "Restringe l'elenco delle risoluzioni alle proporzioni dello schermo.",
                () => AspectNames[aspectFilter], d => { aspectFilter = Wrap(aspectFilter + d, AspectNames.Length); NormalizeDisplay(); });
            resolutionChoice = AddChoice(p, "Risoluzione", "Le dimensioni dell'immagine, in punti.",
                () => displayDraft.width + " × " + displayDraft.height, d =>
                {
                    var modes = VideoSettings.Modes(displayDraft.mode, aspectFilter);
                    if (modes.Count == 0) return;
                    int i = modes.IndexOf(new Vector2Int(displayDraft.width, displayDraft.height));
                    var r = modes[Wrap(i + d, modes.Count)]; displayDraft.width = r.x; displayDraft.height = r.y;
                    displayDraft.hz = 0; RefreshDisplayLabels();
                });
            rateChoice = AddChoice(p, "Frequenza monitor", "Solo nello schermo intero esclusivo si sceglie la frequenza; negli altri modi segue quella del desktop.",
                () => displayDraft.mode != FullScreenMode.ExclusiveFullScreen ? "Desktop · " + VideoSettings.Current.hz + " Hz"
                    : displayDraft.hz == 0 ? "Automatica" : displayDraft.hz + " Hz", d =>
                {
                    var rates = VideoSettings.Rates(displayDraft);
                    displayDraft.hz = rates[Wrap(rates.IndexOf(displayDraft.hz) + d, rates.Count)]; RefreshDisplayLabels();
                });
            vsyncChoice = AddChoice(p, "Sincronizzazione verticale", "Evita lo strappo dell'immagine. Se è attiva, il limite dei fotogrammi non conta.",
                () => displayDraft.vsync == 1 ? "Attiva" : "Disattiva", d => { displayDraft.vsync = 1 - displayDraft.vsync; RefreshDisplayLabels(); });
            fpsChoice = AddChoice(p, "Limite fotogrammi", "Con la VSync spenta limita il lavoro della scheda video.",
                () => displayDraft.vsync == 1 ? "Controllato dal VSync" : displayDraft.fps < 0 ? "Senza limite" : displayDraft.fps + " FPS", d =>
                {
                    var caps = VideoSettings.FrameLimits;
                    displayDraft.fps = caps[Wrap(System.Array.IndexOf(caps, displayDraft.fps) + d, caps.Length)]; RefreshDisplayLabels();
                });
            float y = RowY(p.next++);
            displayActual = Label(p.root, "Schermo attuale", new Vector2(LabelX, y), new Vector2(LabelWidth, 30), 15, TextAnchor.MiddleLeft, "", Brass, FontStyle.Normal);
            applyDisplayButton = MakeButton(p.root, "Applica schermo", new Vector2(ControlX, y), PreviewDisplay);
            ResizeButton(applyDisplayButton, new Vector2(ControlWidth, 36), 21);
            AddFeedback(applyDisplayButton, "Prova subito le impostazioni: hai quindici secondi per confermarle, poi tornano quelle di prima.");
            p.rows.Add(applyDisplayButton);
            displayNote = Label(p.root, "Nota schermo", new Vector2(0, RowY(p.next++) - 4f), new Vector2(1000, 44), 16, TextAnchor.UpperCenter, "", Dim, FontStyle.Normal);
            SyncDisplay();
        }

        static int Wrap(int i, int n) => n <= 0 ? 0 : (i % n + n) % n;

        void SyncDisplay()
        {
            if (modeChoice == null) return;
            displayDraft = VideoSettings.Current;
            if (displayDraft.mode != FullScreenMode.ExclusiveFullScreen) displayDraft.hz = 0;
            aspectFilter = 0;
            RefreshDisplayLabels();
        }

        void NormalizeDisplay()
        {
            var modes = VideoSettings.Modes(displayDraft.mode, aspectFilter);
            if (modes.Count > 0 && !modes.Contains(new Vector2Int(displayDraft.width, displayDraft.height)))
            {
                var r = modes[modes.Count - 1]; displayDraft.width = r.x; displayDraft.height = r.y;
            }
            RefreshDisplayLabels();
        }

        void RefreshDisplayLabels()
        {
            if (modeChoice == null || pages.Count == 0) return;
            foreach (var refresh in pages[0].refresh) refresh();
            rateChoice.interactable = displayDraft.mode == FullScreenMode.ExclusiveFullScreen && VideoSettings.Rates(displayDraft).Count > 1;
            fpsChoice.interactable = displayDraft.vsync == 0;
            bool available = VideoSettings.Modes(displayDraft.mode, aspectFilter).Count > 0;
            applyDisplayButton.interactable = available;
            displayNote.text = !available ? "Il monitor non offre risoluzioni per questo formato in modalità esclusiva." :
                displayDraft.mode == FullScreenMode.ExclusiveFullScreen ? "Modalità e frequenze rilevate dal monitor. Hai 15 secondi per confermare la nuova immagine." :
                "In finestra e senza bordi la frequenza segue il desktop. Le finestre troppo grandi vengono adattate allo schermo.";
            displayActual.text = "ATTUALE  " + Screen.width + " × " + Screen.height + "  /  " + VideoSettings.Aspect(Screen.width, Screen.height);
            RebuildNav();
        }

        void BuildDisplayConfirmation()
        {
            displayConfirm = Stretch(backdrop, "Conferma schermo");
            var veil = displayConfirm.gameObject.AddComponent<Image>(); veil.color = new Color(0.008f, 0.02f, 0.025f, 0.98f);
            Label(displayConfirm, "Titolo", new Vector2(0, 112), new Vector2(900, 70), 42, TextAnchor.MiddleCenter, "L'immagine è nitida?", Ink, FontStyle.Normal);
            previewCountdown = Label(displayConfirm, "Conto alla rovescia", new Vector2(0, 35), new Vector2(900, 70), 21, TextAnchor.MiddleCenter, "", Dim, FontStyle.Normal);
            MakeButton(displayConfirm, "Mantieni impostazioni", new Vector2(-220, -70), ConfirmDisplay);
            MakeButton(displayConfirm, "Ripristina", new Vector2(220, -70), CancelDisplayPreview);
            displayConfirm.gameObject.SetActive(false);
        }

        void PreviewDisplay()
        {
            if (displayPending) return;
            displayPrevious = VideoSettings.Current;
            displayDraft = VideoSettings.Validate(displayDraft);
            displayPending = true; displayDeadline = Time.realtimeSinceStartup + 15f;
            var group = settingsPanel.GetComponent<CanvasGroup>() ?? settingsPanel.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            VideoSettings.Apply(displayDraft);
            displayConfirm.gameObject.SetActive(true); displayConfirm.SetAsLastSibling();
            EventSystem.current?.SetSelectedGameObject(displayConfirm.GetComponentInChildren<Button>().gameObject);
        }

        void ConfirmDisplay()
        {
            if (!displayPending) return;
            // Persist what Unity actually applied, including any platform resolution fallback.
            var actual = VideoSettings.Current; actual.hz = displayDraft.hz;
            VideoSettings.Save(actual);
            displayPending = false; displayConfirm.gameObject.SetActive(false); SyncDisplay();
            settingsPanel.GetComponent<CanvasGroup>().interactable = true;
            EventSystem.current?.SetSelectedGameObject(applyDisplayButton.gameObject);
        }

        void CancelDisplayPreview()
        {
            if (!displayPending) return;
            VideoSettings.Apply(displayPrevious);
            displayPending = false;
            if (settingsPanel != null && settingsPanel.TryGetComponent<CanvasGroup>(out var group)) group.interactable = true;
            if (displayConfirm != null) displayConfirm.gameObject.SetActive(false);
            SyncDisplay();
            if (applyDisplayButton != null) EventSystem.current?.SetSelectedGameObject(applyDisplayButton.gameObject);
        }

        void UpdateDisplayPreview()
        {
            if (!displayPending) return;
            float left = displayDeadline - Time.realtimeSinceStartup;
            previewCountdown.text = "Ripristino automatico tra " + Mathf.Max(0, Mathf.CeilToInt(left)) + " secondi.\n" +
                (PadControls.Preferred ? "A conferma la scelta · B ripristina" : "Invio conferma la scelta · Esc ripristina");
            if (left <= 0 || Input.GetKeyDown(KeyCode.Escape) || PadControls.Cancel) CancelDisplayPreview();
        }
    }
}
