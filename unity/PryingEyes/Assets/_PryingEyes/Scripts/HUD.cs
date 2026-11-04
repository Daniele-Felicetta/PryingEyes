using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>HUD with proper corner anchors. Idempotent build: reuses scene children (editor-built) at runtime.</summary>
    public class HUD : MonoBehaviour
    {
        Slider hpBar, shieldBar;
        Text ammoText, waveText, scoreText, killsText, enemiesText, bannerText, bannerSub, toastText, killConfirm, cross;
        Image hitmarker, vignette;
        float hitT, killT, bannerT, toastT, vigT;

        void Awake() { EnsureBuilt(); }

        GameObject Child(string name)
        {
            var t = transform.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go;
        }

        static GameObject SubChild(GameObject parent, string name)
        {
            var t = parent.transform.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static T Comp<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        void EnsureBuilt()
        {
            var canvas = Comp<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = Comp<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            Comp<GraphicRaycaster>(gameObject);

            hpBar = MakeBar("HpBar", new Vector2(0, 0), new Vector2(20, 20), new Vector2(240, 20), new Color(0.8f, 0.15f, 0.15f));
            shieldBar = MakeBar("ShieldBar", new Vector2(0, 0), new Vector2(20, 46), new Vector2(240, 12), new Color(0.2f, 0.7f, 1f));
            ammoText = MakeText("Ammo", new Vector2(1, 0), new Vector2(-28, 24), new Vector2(200, 36), 24, TextAnchor.LowerRight, "30 / 120");
            waveText = MakeText("Wave", new Vector2(0, 1), new Vector2(20, -16), new Vector2(300, 30), 22, TextAnchor.UpperLeft, "ONDATA 1");
            scoreText = MakeText("Score", new Vector2(0, 1), new Vector2(20, -46), new Vector2(300, 26), 18, TextAnchor.UpperLeft, "SCORE 0");
            killsText = MakeText("Kills", new Vector2(0, 1), new Vector2(20, -70), new Vector2(300, 26), 18, TextAnchor.UpperLeft, "KILLS 0");
            enemiesText = MakeText("Enemies", new Vector2(0, 1), new Vector2(20, -94), new Vector2(300, 26), 18, TextAnchor.UpperLeft, "NEMICI 0");
            cross = MakeText("Cross", new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(30, 30), 24, TextAnchor.MiddleCenter, "+");
            hitmarker = MakeImage("Hitmarker", new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(44, 44), Color.white);
            hitmarker.enabled = false;
            killConfirm = MakeText("KillConfirm", new Vector2(0.5f, 0.5f), new Vector2(0, 52), new Vector2(300, 28), 20, TextAnchor.MiddleCenter, "");
            bannerText = MakeText("Banner", new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(700, 48), 38, TextAnchor.MiddleCenter, "");
            bannerSub = MakeText("BannerSub", new Vector2(0.5f, 1), new Vector2(0, -152), new Vector2(700, 28), 18, TextAnchor.MiddleCenter, "");
            toastText = MakeText("Toast", new Vector2(0.5f, 0), new Vector2(0, 96), new Vector2(800, 26), 16, TextAnchor.MiddleCenter, "");
            vignette = MakeImage("Vignette", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Color(0.8f, 0, 0, 0.35f));
            var vr = vignette.GetComponent<RectTransform>();
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
            vignette.enabled = false;
        }

        Slider MakeBar(string name, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = Child(name);
            var rt = Comp<RectTransform>(go);
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x == 1 ? 1 : 0, anchor.y == 1 ? 1 : 0);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = Comp<Image>(go); img.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            var fill = SubChild(go, "Fill");
            var frt = Comp<RectTransform>(fill);
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            var fimg = Comp<Image>(fill); fimg.color = c;
            var sl = Comp<Slider>(go);
            sl.fillRect = frt; sl.minValue = 0; sl.maxValue = 1; sl.value = 1;
            return sl;
        }

        Text MakeText(string name, Vector2 anchor, Vector2 pos, Vector2 size, int fs, TextAnchor a, string s)
        {
            var go = Child(name);
            var rt = Comp<RectTransform>(go);
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x == 1 ? 1 : (anchor.x == 0 ? 0 : 0.5f), anchor.y == 1 ? 1 : (anchor.y == 0 ? 0 : 0.5f));
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = Comp<Text>(go);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fs; t.alignment = a; t.text = s;
            t.color = Color.white;
            return t;
        }

        Image MakeImage(string name, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = Child(name);
            var rt = Comp<RectTransform>(go);
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var i = Comp<Image>(go); i.color = c;
            return i;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (hitT > 0f) { hitT -= dt; if (hitT <= 0f && hitmarker != null) hitmarker.enabled = false; }
            if (killT > 0f) { killT -= dt; if (killT <= 0f && killConfirm != null) killConfirm.text = ""; }
            if (bannerT > 0f) { bannerT -= dt; if (bannerT <= 0f) { if (bannerText != null) bannerText.text = ""; if (bannerSub != null) bannerSub.text = ""; } }
            if (toastT > 0f) { toastT -= dt; if (toastT <= 0f && toastText != null) toastText.text = ""; }
            if (vigT > 0f && vignette != null)
            {
                vigT -= dt;
                if (vigT <= 0f) vignette.enabled = false;
                else { var c = vignette.color; c.a = 0.35f * (vigT / 0.5f); vignette.color = c; }
            }
        }

        public void SetBars(float hp, float shield) { if (hpBar == null) return; hpBar.value = hp / 100f; shieldBar.value = shield / 50f; }
        public void SetAmmo(int m, int r) { if (ammoText == null) return; ammoText.text = m + " / " + r; }
        public void SetWave(int w) { if (waveText == null) return; waveText.text = "ONDATA " + w; }
        public void SetScore(int s) { if (scoreText == null) return; scoreText.text = "SCORE " + s; }
        public void SetKills(int k) { if (killsText == null) return; killsText.text = "KILLS " + k; }
        public void SetEnemiesLeft(int n) { if (enemiesText == null) return; enemiesText.text = "NEMICI " + n; }
        public void Hitmarker(bool kill)
        {
            if (hitmarker == null) return;
            hitmarker.enabled = true;
            hitmarker.color = kill ? Color.red : Color.white;
            hitT = 0.12f;
        }
        public void KillConfirm(string s) { if (killConfirm == null) return; killConfirm.text = s; killT = 1.5f; }
        public void Banner(string t, string sub) { if (bannerText == null) return; bannerText.text = t; bannerSub.text = sub; bannerT = 3f; }
        public void Toast(string s) { if (toastText == null) return; toastText.text = s; toastT = 4f; }
        public void DamageFlash()
        {
            if (vignette == null) return;
            var c = vignette.color; c.a = 0.35f; vignette.color = c;
            vignette.enabled = true;
            vigT = 0.5f;
        }
        void HideVignette() { if (vignette != null) vignette.enabled = false; }
    }
}
