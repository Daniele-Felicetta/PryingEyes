using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>HUD built in code: HP/shield bars, ammo, wave, score, kills, enemies, crosshair, hitmarker, banner, toast, vignette.</summary>
    public class HUD : MonoBehaviour
    {
        Slider hpBar, shieldBar;
        Text ammoText, waveText, scoreText, killsText, enemiesText, bannerText, bannerSub, toastText;
        Image hitmarker, vignette;
        Text killConfirm;
        float hitT, killT, bannerT, toastT;
        bool built = false;

        void Awake()
        {
            EnsureBuilt();
        }

        void EnsureBuilt()
        {
            if (built) return;
            built = true;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.AddComponent<GraphicRaycaster>();
            var root = new GameObject("Root");
            root.transform.SetParent(transform, false);

            // bars bottom-left
            hpBar = MakeBar(root.transform, new Vector2(20, 40), new Vector2(260, 22), new Color(0.8f, 0.15f, 0.15f));
            shieldBar = MakeBar(root.transform, new Vector2(20, 68), new Vector2(260, 14), new Color(0.2f, 0.7f, 1f));

            // ammo bottom-right
            ammoText = MakeText(root.transform, new Vector2(-220, 40), new Vector2(200, 40), 32, TextAnchor.MiddleRight, "30 / 120");
            // top-left: wave/score/kills/enemies
            waveText = MakeText(root.transform, new Vector2(20, -20), new Vector2(300, 30), 22, TextAnchor.UpperLeft, "ONDATA 1");
            scoreText = MakeText(root.transform, new Vector2(20, -50), new Vector2(300, 26), 20, TextAnchor.UpperLeft, "SCORE 0");
            killsText = MakeText(root.transform, new Vector2(20, -76), new Vector2(300, 26), 20, TextAnchor.UpperLeft, "KILLS 0");
            enemiesText = MakeText(root.transform, new Vector2(20, -102), new Vector2(300, 26), 20, TextAnchor.UpperLeft, "NEMICI 0");

            // crosshair center
            var ch = MakeText(root.transform, new Vector2(-15, -20), new Vector2(30, 40), 28, TextAnchor.MiddleCenter, "+");
            // hitmarker
            hitmarker = MakeImage(root.transform, new Vector2(-25, -25), new Vector2(50, 50), Color.white);
            hitmarker.enabled = false;
            killConfirm = MakeText(root.transform, new Vector2(-150, 40), new Vector2(300, 30), 22, TextAnchor.MiddleCenter, "");
            // banner center-top
            bannerText = MakeText(root.transform, new Vector2(-300, -140), new Vector2(600, 50), 42, TextAnchor.MiddleCenter, "");
            bannerSub = MakeText(root.transform, new Vector2(-300, -190), new Vector2(600, 30), 20, TextAnchor.MiddleCenter, "");
            // toast bottom-center
            toastText = MakeText(root.transform, new Vector2(-400, 110), new Vector2(800, 30), 18, TextAnchor.MiddleCenter, "");
            // damage vignette
            vignette = MakeImage(root.transform, new Vector2(0, 0), new Vector2(4000, 4000), new Color(0.8f, 0, 0, 0.35f));
            vignette.enabled = false;
        }

        Slider MakeBar(Transform p, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Bar");
            go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 0);
            rt.anchoredPosition = pos + size / 2; rt.sizeDelta = size;
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

        Text MakeText(Transform p, Vector2 pos, Vector2 size, int fs, TextAnchor a, string s)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fs; t.alignment = a; t.text = s;
            t.color = Color.white;
            return t;
        }

        Image MakeImage(Transform p, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Img");
            go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
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
        public void SetWave(int w) { waveText.text = "ONDATA " + w; }
        public void SetScore(int s) { scoreText.text = "SCORE " + s; }
        public void SetKills(int k) { killsText.text = "KILLS " + k; }
        public void SetEnemiesLeft(int n) { enemiesText.text = "NEMICI " + n; }
        public void Hitmarker(bool kill)
        {
            hitmarker.enabled = true;
            hitmarker.color = kill ? Color.red : Color.white;
            hitT = 0.12f;
        }
        public void KillConfirm(string s) { killConfirm.text = s; killT = 1.5f; }
        public void Banner(string t, string sub) { bannerText.text = t; bannerSub.text = sub; bannerT = 3f; }
        public void Toast(string s) { toastText.text = s; toastT = 4f; }
        public void DamageFlash() { vignette.enabled = true; CancelInvoke(nameof(HideVignette)); Invoke(nameof(HideVignette), 0.18f); }
        void HideVignette() { vignette.enabled = false; }
    }
}
