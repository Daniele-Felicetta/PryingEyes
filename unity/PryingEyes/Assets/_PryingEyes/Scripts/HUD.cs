using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>HUD with proper corner anchors. Idempotent build: reuses scene children (editor-built) at runtime.</summary>
    public class HUD : MonoBehaviour
    {
        Slider hpBar, shieldBar;
        Text ammoText, waveText, scoreText, killsText, enemiesText, bannerText, bannerSub, toastText, killConfirm, cross;
        Image vignette;
        RectTransform hitRoot;      // hit marker: X di 4 tacche attorno al mirino
        Image[] hitTicks;
        Color hitCol = Color.white;
        float hitT, hitDur, hitPop, hitSize, killT, bannerT, toastT, vigT;

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
            MakeHitmarker();
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

        const int TexelsPerUnit = 8;       // risoluzione dello sprite della tacca
        const float TickCenter = 13f;      // distanza (unita UI) del centro di ogni tacca dal mirino, in diagonale
        static Sprite tickSprite;

        /// <summary>Tacca del hit marker: barra bianca con spigoli arrotondati, bordo antialias e alone scuro
        /// (resta leggibile su sfondi chiari). Il colore si cambia con Image.color; l'alone resta nero.</summary>
        static Sprite TickSprite()
        {
            if (tickSprite != null) return tickSprite;
            const int W = 48, H = 128;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[W * H];
            float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
            const float hx = 10.5f, hy = 48f, r = 3f;      // mezzo nucleo e raggio degli spigoli, in texel
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float qx = Mathf.Abs(x - cx) - (hx - r), qy = Mathf.Abs(y - cy) - (hy - r);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                              + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;             // distanza con segno dal bordo
                    float core = Mathf.Clamp01(0.5f - d);                          // nucleo bianco, bordo di 1 texel
                    float halo = 0.55f * Mathf.Pow(Mathf.Clamp01(1f - d / 12f), 2f); // alone nero che sfuma
                    float a = core + halo * (1f - core);
                    byte v = a > 0f ? (byte)Mathf.RoundToInt(255f * core / a) : (byte)0;
                    px[y * W + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(255f * a));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            tickSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return tickSprite;
        }

        void MakeHitmarker()
        {
            var root = Child("Hitmarker");
            hitRoot = Comp<RectTransform>(root);
            hitRoot.anchorMin = hitRoot.anchorMax = new Vector2(0.5f, 0.5f);
            hitRoot.pivot = new Vector2(0.5f, 0.5f);
            hitRoot.anchoredPosition = Vector2.zero; hitRoot.sizeDelta = Vector2.zero;
            var spr = TickSprite();
            hitTicks = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? 1f : -1f, sy = (i & 2) == 0 ? 1f : -1f;
                var go = SubChild(root, "Tick" + i);
                var rt = Comp<RectTransform>(go);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(spr.rect.width, spr.rect.height) / TexelsPerUnit;
                rt.anchoredPosition = new Vector2(sx, sy) * (TickCenter * 0.7071f);
                rt.localRotation = Quaternion.Euler(0f, 0f, sx * sy > 0f ? -45f : 45f);   // asse lungo la diagonale
                var img = Comp<Image>(go);
                img.sprite = spr; img.raycastTarget = false;
                hitTicks[i] = img;
            }
            root.SetActive(false);
        }

        // Pop veloce (le tacce partono piu larghe e rientrano in 0,08 s), poi restano piene e sfumano.
        void UpdateHitmarker(float dt)
        {
            if (hitT <= 0f || hitRoot == null) return;
            hitT -= dt;
            if (hitT <= 0f) { hitRoot.gameObject.SetActive(false); return; }
            float p = Mathf.Clamp01((hitDur - hitT) / 0.08f);
            float s = Mathf.Lerp(hitPop, 1f, 1f - (1f - p) * (1f - p)) * hitSize;
            hitRoot.localScale = new Vector3(s, s, 1f);
            var c = hitCol;
            c.a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(hitT / (hitDur * 0.55f)));
            foreach (var t in hitTicks) t.color = c;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateHitmarker(dt);
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
        /// <summary>Hit marker stile CoD: bianco al colpo; rosso, piu grande e piu lungo all'uccisione; la testa lo ingrandisce.</summary>
        public void Hitmarker(bool kill, bool headshot = false)
        {
            if (hitRoot == null) return;
            hitCol = kill ? new Color(1f, 0.18f, 0.14f) : Color.white;
            hitSize = (kill ? 1.2f : 1f) * (headshot ? 1.2f : 1f);
            hitPop = kill ? 1.6f : 1.4f;
            hitDur = kill ? 0.5f : 0.25f;
            hitT = hitDur;
            hitRoot.gameObject.SetActive(true);
            UpdateHitmarker(0f);
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
