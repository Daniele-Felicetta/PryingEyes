using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il contatore dei fotogrammi (Impostazioni > Gioco): in alto a sinistra, piccolo, "144 FPS  6,9 ms", sopra ogni altra cosa (anche
    /// del menu) e fuori dal mondo di gioco: ha un canvas suo e sopravvive al ritorno al menu. Si aggiorna quattro volte al secondo, sul tempo
    /// reale, quindi funziona anche in pausa. Non c'e' nelle prove automatiche.</summary>
    public sealed class FpsCounter : MonoBehaviour
    {
        static FpsCounter I;
        Text label;
        Canvas canvas;
        float elapsed, worst;
        int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (I != null || Application.isBatchMode) return;
            var go = new GameObject("FpsCounter");
            DontDestroyOnLoad(go);
            I = go.AddComponent<FpsCounter>();
        }

        void Awake()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var rt = new GameObject("Testo", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(12f, -10f);
            rt.sizeDelta = new Vector2(260f, 22f);
            label = rt.gameObject.AddComponent<Text>();
            label.font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Consolas", "Segoe UI", "Arial" }, 14);
            label.fontSize = 14;
            label.alignment = TextAnchor.UpperLeft;
            label.color = new Color(0.93f, 0.89f, 0.80f, 0.9f);
            label.raycastTarget = false;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1f, -1f);
            canvas.enabled = false;
        }

        void Update()
        {
            bool on = GameSettings.ShowFps;
            if (canvas.enabled != on) { canvas.enabled = on; elapsed = 0f; frames = 0; worst = 0f; }
            if (!on) return;
            float dt = Time.unscaledDeltaTime;
            elapsed += dt; frames++; worst = Mathf.Max(worst, dt);
            if (elapsed < 0.25f) return;
            float avg = elapsed / frames;
            label.text = Mathf.RoundToInt(1f / Mathf.Max(avg, 1e-5f)) + " FPS   " + (avg * 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + " ms   (peggio " + (worst * 1000f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms)";
            elapsed = 0f; frames = 0; worst = 0f;
        }
    }
}
