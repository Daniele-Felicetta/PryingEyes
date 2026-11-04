using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>HUD with proper corner anchors (works at any aspect). Built in code.</summary>
    public class HUD : MonoBehaviour
    {
        Slider hpBar, shieldBar;
        Text ammoText, waveText, scoreText, killsText, enemiesText, bannerText, bannerSub, toastText, killConfirm, cross;
        Image hitmarker, vignette;
        float hitT, killT, bannerT, toastT;
        bool built = false;

        void Awake() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (built) return;
            built = true;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            gameObject.AddComponent<GraphicRaycaster>();

            hpBar = MakeBar(new Vector2(0, 0), new Vector2(20, 20), new Vector2(240, 20), new Color(0.8f, 0.15f, 0.15f));
            shieldBar = MakeBar(new Vector2(0, 0), new Vector2(20, 46), new Vector2(240, 12), new Color(0.2f, 0.7f, 1f));
            ammoText = MakeText(new Vector2(1, 0), new Vector2(-20, 24), new Vector2(200, 36), 26, TextAnchor.LowerRight, "30 / 120");
            waveText = MakeText(new Vector2(0, 1), new Vector2(20, -16), new Vector2(300, 30), 22, TextAnchor.UpperLeft, "ONDATA 1");
            scoreText = MakeText(new Vector2(0, 1), new Vector2(20, -46), new Vector2(300, 26), 18, TextAnchor.UpperLeft, "SCORE 0");
            killsText = MakeText(new Vector2(0, 1), new Vector2(20, -70), new Vector2(300, 26), 18, TextAnchor.UpperLeft, "KILLS 0");
            enemiesText = MakeText(new Vector2(0, 1), new Vector2(20, -94), new Vector2(300, 26), 18, TextAnchor.UpperLeft, "NEMICI 0");
            cross = MakeText(new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(30, 30), 24, TextAnchor.MiddleCenter, "+");
            hitmarker = MakeImage(new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(44, 44), Color.white);
            hitmarker.enabled = false;
            killConfirm = MakeText(new Vector2(0.5f, 0.5f), new Vector2(0, 52), new Vector2(300, 28), 20, TextAnchor.MiddleCenter, "");
            bannerText = MakeText(new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(700, 48), 38, TextAnchor.MiddleCenter, "");
            bannerSub = MakeText(new Vector2(0.5f, 1), new Vector2(0, -152), new Vector2(700, 28), 18, TextAnchor.MiddleCenter, "");
            toastText = MakeText(new Vector2(0.5f, 0), new Vector2(0, 96), new Vector2(800, 26), 16, TextAnchor.MiddleCenter, "");
            vignette = MakeImage(new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Color(0.8f, 0, 0, 0.35f));
            var vr = vignette.GetComponent<RectTransform>();
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
            vignette.enabled = false;
        }

        Slider MakeBar(Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Bar");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x == 1 ? 1 : 0, anchor.y == 1 ? 1 : 0);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>(); img.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            var fill = new GameObject("Fill");
            fill.transform.SetParent(go.transform, false);
            var frt = fill.AddComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            var fimg = fill.AddComponent<Image>(); fimg.color = c;
            var sl = go.AddComponent<Slider>();
            sl.fillRect = frt; sl.minValue = 0; sl.maxValue = 1; sl.value = 1;
            return sl;
        }

        Text MakeText(Vector2 anchor, Vector2 pos, Vector2 size, int fs, TextAnchor a, string s)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x == 1 ? 1 : (anchor.x == 0 ? 0 : 0.5f), anchor.y == 1 ? 1 : (anchor.y == 0 ? 0 : 0.5f));
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fs; t.alignment = a; t.text = s;
            t.color = Color.white;
            return t;
        }

        Image MakeImage(Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Img");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var i = go.AddComponent<Image>(); i.color = c;
            return i;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (hitT > 0f) { hitT -= dt; if (hitT <= 0f) hitmarker.enabled = false; }
            if (killT > 0f) { killT -= dt; if (killT <= 0f) killConfirm.text = ""; }
            if (bannerT > 0f) { bannerT -= dt; if (bannerT <= 0f) { bannerText.text = ""; bannerSub.text = ""; } }
            if (toastT > 0f) { toastT -= dt; if (toastT <= 0f) toastText.text = ""; }
        }

        public void SetBars(float hp, float shield) { EnsureBuilt(); hpBar.value = hp / 100f; shieldBar.value = shield / 50f; }
        public void SetAmmo(int m, int r) { EnsureBuilt(); ammoText.text = m + " / " + r; }
        public void SetWave(int w) { EnsureBuilt(); waveText.text = "ONDATA " + w; }
        public void SetScore(int s) { EnsureBuilt(); scoreText.text = "SCORE " + s; }
        public void SetKills(int k) { EnsureBuilt(); killsText.text = "KILLS " + k; }
        public void SetEnemiesLeft(int n) { EnsureBuilt(); enemiesText.text = "NEMICI " + n; }
        public void Hitmarker(bool kill)
        {
            EnsureBuilt();
            hitmarker.enabled = true;
            hitmarker.color = kill ? Color.red : Color.white;
            hitT = 0.12f;
        }
        public void KillConfirm(string s) { EnsureBuilt(); killConfirm.text = s; killT = 1.5f; }
        public void Banner(string t, string sub) { EnsureBuilt(); bannerText.text = t; bannerSub.text = sub; bannerT = 3f; }
        public void Toast(string s) { EnsureBuilt(); toastText.text = s; toastT = 4f; }
        public void DamageFlash() { EnsureBuilt(); vignette.enabled = true; CancelInvoke(nameof(HideVignette)); Invoke(nameof(HideVignette), 0.18f); }
        void HideVignette() { vignette.enabled = false; }
    }
}
