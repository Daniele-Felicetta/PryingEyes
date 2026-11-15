using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace PryingEyes
{
    public partial class GameMenu
    {
        VideoSettings.Config displayDraft, displayPrevious;
        int aspectFilter;
        bool displayPending;
        float displayDeadline;
        RectTransform displayConfirm;
        Text modeValue, resolutionValue, aspectValue, refreshValue, vsyncValue, fpsValue, displayNote, previewCountdown;
        Button applyDisplayButton;
        Button ratePrevious, rateNext, fpsPrevious, fpsNext;
        Text displayActual;

        void BuildDisplayControls()
        {
            Choice(videoTab, "Modalità schermo", -285, 120, d =>
            {
                var modes = new[] { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
                displayDraft.mode = modes[Wrap(System.Array.IndexOf(modes, displayDraft.mode) + d, modes.Length)];
                displayDraft.hz = 0; NormalizeDisplay();
            }, out modeValue);
            Choice(videoTab, "Formato", 285, 120, d => { aspectFilter = Wrap(aspectFilter + d, 4); NormalizeDisplay(); }, out aspectValue);
            Choice(videoTab, "Risoluzione", -285, 30, d =>
            {
                var modes = VideoSettings.Modes(displayDraft.mode, aspectFilter);
                if (modes.Count == 0) return;
                int i = modes.IndexOf(new Vector2Int(displayDraft.width, displayDraft.height));
                var r = modes[Wrap(i + d, modes.Count)]; displayDraft.width = r.x; displayDraft.height = r.y;
                displayDraft.hz = 0; RefreshDisplayLabels();
            }, out resolutionValue);
            Choice(videoTab, "Frequenza monitor", 285, 30, d =>
            {
                var rates = VideoSettings.Rates(displayDraft);
                displayDraft.hz = rates[Wrap(rates.IndexOf(displayDraft.hz) + d, rates.Count)]; RefreshDisplayLabels();
            }, out refreshValue, out ratePrevious, out rateNext);
            Choice(videoTab, "Sincronizzazione verticale", -285, -60, d => { displayDraft.vsync = 1 - displayDraft.vsync; RefreshDisplayLabels(); }, out vsyncValue);
            Choice(videoTab, "Limite fotogrammi", 285, -60, d =>
            {
                var caps = VideoSettings.FrameLimits;
                displayDraft.fps = caps[Wrap(System.Array.IndexOf(caps, displayDraft.fps) + d, caps.Length)]; RefreshDisplayLabels();
            }, out fpsValue, out fpsPrevious, out fpsNext);
            displayNote = Label(videoTab, "Nota schermo", new Vector2(0, -135), new Vector2(1040, 42), 16,
                TextAnchor.MiddleLeft, "", Dim, FontStyle.Normal);
            displayActual = Label(videoTab, "Schermo attuale", new Vector2(-285, -174), new Vector2(490, 42), 15,
                TextAnchor.MiddleLeft, "", Brass, FontStyle.Normal);
            applyDisplayButton = MakeButton(videoTab, "Applica schermo", new Vector2(285, -225), PreviewDisplay);
            applyDisplayButton.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 42);
        }

        static int Wrap(int i, int n) => n <= 0 ? 0 : (i % n + n) % n;
        void Choice(Transform parent, string title, float x, float y, System.Action<int> change, out Text value)
        { Choice(parent, title, x, y, change, out value, out _, out _); }
        void Choice(Transform parent, string title, float x, float y, System.Action<int> change, out Text value, out Button previous, out Button next)
        {
            Label(parent, title, new Vector2(x, y + 31), new Vector2(460, 28), 17, TextAnchor.MiddleLeft, title.ToUpperInvariant(), Dim, FontStyle.Normal);
            Plate(parent, title + " fondo", new Vector2(x, y - 3), new Vector2(460, 44), BtnColor);
            value = Label(parent, title + " valore", new Vector2(x, y - 3), new Vector2(342, 40), 21, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            previous = MakeButton(parent, title + " ‹", new Vector2(x - 209, y - 3), () => change(-1));
            next = MakeButton(parent, title + " ›", new Vector2(x + 209, y - 3), () => change(1));
            foreach (var b in new[] { previous, next })
            {
                b.GetComponent<RectTransform>().sizeDelta = new Vector2(42, 44);
                var t = b.GetComponentInChildren<Text>(); t.rectTransform.sizeDelta = new Vector2(40, 40);
                t.text = b == previous ? "‹" : "›"; t.fontSize = 25;
            }
        }

        void SyncDisplay()
        {
            if (modeValue == null) return;
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
            modeValue.text = displayDraft.mode == FullScreenMode.Windowed ? "Finestra" : displayDraft.mode == FullScreenMode.ExclusiveFullScreen ? "Schermo intero esclusivo" : "Schermo intero senza bordi";
            aspectValue.text = new[] { "Tutti i formati", "16:9 · panoramico", "21:9 · ultrawide", "16:10" }[aspectFilter];
            resolutionValue.text = displayDraft.width + " × " + displayDraft.height;
            refreshValue.text = displayDraft.mode != FullScreenMode.ExclusiveFullScreen ? "Desktop · " + VideoSettings.Current.hz + " Hz" : displayDraft.hz == 0 ? "Automatica" : displayDraft.hz + " Hz";
            vsyncValue.text = displayDraft.vsync == 1 ? "Attiva" : "Disattiva";
            fpsValue.text = displayDraft.vsync == 1 ? "Controllato dal VSync" : displayDraft.fps < 0 ? "Senza limite" : displayDraft.fps + " FPS";
            ratePrevious.interactable = rateNext.interactable = displayDraft.mode == FullScreenMode.ExclusiveFullScreen && VideoSettings.Rates(displayDraft).Count > 1;
            fpsPrevious.interactable = fpsNext.interactable = displayDraft.vsync == 0;
            bool available = VideoSettings.Modes(displayDraft.mode, aspectFilter).Count > 0;
            applyDisplayButton.interactable = available;
            displayNote.text = !available ? "Il monitor non offre risoluzioni per questo formato in modalità esclusiva." :
                displayDraft.mode == FullScreenMode.ExclusiveFullScreen ? "Modalità e frequenze rilevate dal monitor. Hai 15 secondi per confermare la nuova immagine." :
                "In finestra e senza bordi la frequenza segue il desktop. Le finestre troppo grandi vengono adattate allo schermo.";
            displayActual.text = "ATTUALE  " + Screen.width + " × " + Screen.height + "  /  " + VideoSettings.Aspect(Screen.width, Screen.height);
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
            previewCountdown.text = "Ripristino automatico tra " + Mathf.Max(0, Mathf.CeilToInt(left)) + " secondi.\nInvio conferma la scelta · Esc ripristina";
            if (left <= 0 || Input.GetKeyDown(KeyCode.Escape)) CancelDisplayPreview();
        }
    }
}
