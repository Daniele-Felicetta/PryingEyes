using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>HUD agli angoli: dati di combattimento in sans, titoli dell'hotel in serif.
    /// Risorse a sinistra, arma a destra; il centro resta dedicato a mira, contatti e interazioni.</summary>
    public partial class HUD : MonoBehaviour
    {
        Slider hpBar, shieldBar;
        Text ammoText, reserveText, reloadText, waveLabel, waveText, enemiesText, scoreText, bannerText, bannerSub, toastText;
        Text promptText, weaponNameText, objectiveText, healthText;
        Image fadeImg;
        Image vignette, adsVig;
        RectTransform hitRoot;      // hit marker: X di 4 tacche attorno al mirino
        Image[] hitTicks;
        RectTransform[] xDash;      // mirino: quattro trattini attorno al centro
        Image[] xDashImg;
        Image xDot;                 // punto centrale (solo DMR, in mira)
        float xGap = 8f, xGapTarget = 8f, xAds;
        int ammoNow, reserveNow, clipCapacity = 15;
        bool reloadActive;
        string xCls = "";
        bool xShot;
        Text weaponSubText, slotText;
        Color hitCol = Color.white;
        float hitT, hitDur, hitPop, hitSize, bannerT, toastT, vigT, hpNow = 100f;
        static Font serif, utility;
        static readonly Color Ink = new Color(0.93f, 0.89f, 0.80f);
        static readonly Color Dim = new Color(0.80f, 0.81f, 0.80f, 0.96f);
        static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");
        const float BannerTime = 4.5f, ToastTime = 4.5f;

        void Awake() { EnsureBuilt(); }

        public static Font Serif()
        {
            if (serif == null)
                serif = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Palatino Linotype", "Book Antiqua", "Times New Roman" }, 32);
            return serif;
        }

        static Font Utility()
        {
            if (utility == null) utility = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Liberation Sans" }, 32);
            return utility;
        }

        /// <summary>Numero romano (ondata III).</summary>
        public static string Roman(int n)
        {
            if (n <= 0) return n.ToString();
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] s = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(s[i]); n -= v[i]; }
            return sb.ToString();
        }

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
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Comp<GraphicRaycaster>(gameObject);

            // vignetta per primo: sta sotto a tutto il resto
            vignette = MakeImage("Vignette", Color.clear);
            vignette.sprite = VignetteSprite();
            var vr = vignette.GetComponent<RectTransform>();
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;

            // vignettatura nera in mira: restringe lo sguardo sul mirino (piu' forte con le ottiche ingrandite)
            adsVig = MakeImage("AdsVignette", Color.clear);
            adsVig.sprite = AdsVignetteSprite();
            var ar = adsVig.GetComponent<RectTransform>();
            ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one;
            ar.offsetMin = Vector2.zero; ar.offsetMax = Vector2.zero;

            BuildHudShade("FondoVita", false);
            BuildHudShade("FondoArma", true);

            // componente furtiva (HUD.Stealth.cs): torcia, segnalini dei preti, freccia dell'ascensore, ping
            BuildStealthUI();
            BuildRoundUI();          // HUD.Round.cs: il numero del round, rosso in basso a sinistra
            BuildPuntiUI();          // HUD.Punti.cs: "+N" accanto al mirino, timer dei potenziamenti, lampo della Campana
            BuildPerksUI();          // HUD.Perks.cs: i quattro posti dei perk sotto l'obiettivo
            BuildCoopUI();

            // Navigazione in alto; risorse di combattimento ai due angoli inferiori.
            waveLabel = MakeText("WaveLabel", new Vector2(0, 1), new Vector2(28, -26), new Vector2(100, 22), 13, TextAnchor.UpperLeft, "ONDATA", Dim, FontStyle.Bold);
            waveText = MakeText("Wave", new Vector2(0, 1), new Vector2(118, -21), new Vector2(190, 32), 26, TextAnchor.UpperLeft, "I", Ink, FontStyle.Bold);
            enemiesText = MakeText("Enemies", new Vector2(0, 1), new Vector2(28, -88), new Vector2(320, 22), 15, TextAnchor.UpperLeft, "", Dim, FontStyle.Normal);
            scoreText = MakeText("Score", Vector2.zero, new Vector2(28, 91), new Vector2(290, 38), 30, TextAnchor.LowerLeft, "0", Ink, FontStyle.Bold);
            MakeText("ScoreLabel", Vector2.zero, new Vector2(28, 128), new Vector2(260, 18), 11, TextAnchor.LowerLeft, "PUNTI", Dim, FontStyle.Bold);
            healthText = MakeText("Health", Vector2.zero, new Vector2(28, 62), new Vector2(340, 22), 16, TextAnchor.LowerLeft, "100 / 100", Ink, FontStyle.Bold);
            hpBar = MakeBar("HpBar", Vector2.zero, new Vector2(28, 50), new Vector2(220, 7), new Color(0.86f, 0.88f, 0.84f));
            shieldBar = MakeBar("ShieldBar", Vector2.zero, new Vector2(28, 42), new Vector2(220, 3), new Color(0.48f, 0.73f, 0.95f));
            ammoText = MakeText("Ammo", new Vector2(1, 0), new Vector2(-105, 22), new Vector2(170, 68), 60, TextAnchor.LowerRight, "30", Ink, FontStyle.Bold);
            reserveText = MakeText("Reserve", new Vector2(1, 0), new Vector2(-28, 32), new Vector2(78, 32), 23, TextAnchor.LowerRight, "/ 120", Dim, FontStyle.Normal);
            reloadText = MakeText("Reload", new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(420, 24), 16, TextAnchor.MiddleCenter, "", Ink, FontStyle.Bold);
            weaponNameText = MakeText("WeaponName", new Vector2(1, 0), new Vector2(-28, 92), new Vector2(420, 26), 21, TextAnchor.LowerRight, "", Ink, FontStyle.Bold);
            weaponSubText = MakeText("WeaponSub", new Vector2(1, 0), new Vector2(-28, 120), new Vector2(520, 22), 13, TextAnchor.LowerRight, "", Dim, FontStyle.Normal);
            slotText = MakeText("Slots", new Vector2(1, 0), new Vector2(-28, 150), new Vector2(520, 22), 13, TextAnchor.LowerRight, "", Dim, FontStyle.Normal);
            // prompt delle armi sul muro (acquisto con F)
            promptText = MakeText("Prompt", new Vector2(0.5f, 0.5f), new Vector2(0, -118), new Vector2(900, 40), 18, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            MakeCrosshair();
            MakeHitmarker();
            BuildDamageDirections();
            bannerText = MakeText("Banner", new Vector2(0.5f, 1), new Vector2(0, -118), new Vector2(1000, 66), 54, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            bannerSub = MakeText("BannerSub", new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(1000, 30), 20, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            toastText = MakeText("Toast", new Vector2(0.5f, 0), new Vector2(0, 75), new Vector2(400, 58), 15, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            objectiveText = MakeText("Objective", new Vector2(0, 1), new Vector2(26, -138), new Vector2(560, 24), 16, TextAnchor.UpperLeft, "", Dim, FontStyle.Italic);
            bannerText.font = Serif(); bannerSub.font = Serif();
            foreach (var text in new[] { weaponNameText, weaponSubText, slotText })
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 11; text.resizeTextMaxSize = text.fontSize;
            }
            objectiveText.fontStyle = FontStyle.Normal;
            objectiveText.rectTransform.anchoredPosition = new Vector2(28, -60);
            promptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            fadeImg = MakeImage("Fade", Color.clear);
            var fr = fadeImg.GetComponent<RectTransform>();
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
            // scritte della versione precedente rimaste nella scena: spente
            foreach (var old in new[] { "Kills", "Cross", "KillConfirm" })
            {
                var t = transform.Find(old);
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        static Sprite hudShade;
        void BuildHudShade(string name, bool right)
        {
            if (hudShade == null)
            {
                const int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = new Color(0, 0, 0, 0.68f * Mathf.Pow(1f - x / 31f, 0.8f) * Mathf.Pow(1f - y / 31f, 1.5f));
                tex.SetPixels(pixels); tex.Apply();
                hudShade = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.zero);
            }
            var image = MakeImage(name, Color.white); image.sprite = hudShade;
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(right ? 1 : 0, 0);
            rt.pivot = Vector2.zero; rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(right ? 560 : 410, 270);
            rt.localScale = new Vector3(right ? -1 : 1, 1, 1);
        }

        Slider MakeBar(string name, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = Child(name);
            var rt = Comp<RectTransform>(go);
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x == 1 ? 1 : 0, anchor.y == 1 ? 1 : 0);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = Comp<Image>(go); img.color = new Color(0f, 0f, 0f, 0.45f); img.raycastTarget = false;
            var fill = SubChild(go, "Fill");
            var frt = Comp<RectTransform>(fill);
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            var fimg = Comp<Image>(fill); fimg.color = c; fimg.raycastTarget = false;
            var sl = Comp<Slider>(go);
            sl.fillRect = frt; sl.minValue = 0; sl.maxValue = 1; sl.value = 1;
            sl.interactable = false;
            return sl;
        }

        Text MakeText(string name, Vector2 anchor, Vector2 pos, Vector2 size, int fs, TextAnchor a, string s, Color col, FontStyle style)
        {
            var go = Child(name);
            go.SetActive(true);
            var rt = Comp<RectTransform>(go);
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x == 1 ? 1 : (anchor.x == 0 ? 0 : 0.5f), anchor.y == 1 ? 1 : (anchor.y == 0 ? 0 : 0.5f));
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = Comp<Text>(go);
            t.font = Utility();
            t.fontSize = fs; t.alignment = a; t.text = s;
            t.fontStyle = style;
            t.color = col;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var sh = Comp<Shadow>(go);
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        Image MakeImage(string name, Color c)
        {
            var go = Child(name);
            var rt = Comp<RectTransform>(go);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var i = Comp<Image>(go); i.color = c; i.raycastTarget = false;
            return i;
        }

        static Sprite adsVigSprite;
        static Sprite AdsVignetteSprite()
        {
            if (adsVigSprite != null) return adsVigSprite;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v) / 1.414f;
                    float a = Mathf.SmoothStep(0.30f, 1f, r);
                    px[y * S + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(255f * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return adsVigSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        float adsVigTarget;
        /// <summary>Forza della vignettatura di mira (0..1).</summary>
        public void SetAdsVignette(float a) { adsVigTarget = a; }

        /// <summary>Bordi rossi che sfumano al centro (colpi subiti, vita bassa): prima era un velo rosso piatto.</summary>
        static Sprite VignetteSprite()
        {
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v) / 1.414f;
                    float a = Mathf.SmoothStep(0.25f, 1f, r);
                    px[y * S + x] = new Color32(140, 8, 6, (byte)Mathf.RoundToInt(255f * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Mirino: quattro trattini sottili attorno al centro. Forma per arma, si apre con la dispersione
        /// (passi, salto, colpi) e in mira si spegne, perche' il mirino fisico sull'arma e' gia' al centro.</summary>
        void MakeCrosshair()
        {
            var root = Child("Crosshair");
            var rt = Comp<RectTransform>(root);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero;
            xDash = new RectTransform[4]; xDashImg = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var go = SubChild(root, "Trattino" + i);
                var r = Comp<RectTransform>(go);
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                xDash[i] = r;
                var img = Comp<Image>(go);
                img.color = new Color(1f, 0.97f, 0.9f, 0.85f);
                img.raycastTarget = false;
                xDashImg[i] = img;
                var sh = Comp<Shadow>(go);
                sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
                sh.effectDistance = new Vector2(1f, -1f);
            }
            var dotGo = SubChild(root, "Punto");
            var dr = Comp<RectTransform>(dotGo);
            dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 0.5f);
            xDot = Comp<Image>(dotGo);
            xDot.color = new Color(1f, 0.97f, 0.9f, 0f);
            xDot.raycastTarget = false;
        }

        /// <summary>Mirino a schermo: gap in pixel di riferimento (720p) dalla dispersione reale dell'arma; ads (0..1) lo spegne
        /// perche' il mirino fisico e' gia' al centro. Il fucile a pompa ha trattini piu' lunghi.</summary>
        public void SetCrosshair(float gapPx, float ads, string cls, bool shotgun)
        {
            xGapTarget = Mathf.Clamp(gapPx, 4f, 90f); xAds = ads; xCls = cls; xShot = shotgun;
        }

        void UpdateCrosshair(float dt)
        {
            if (xDash == null) return;
            xGap += (xGapTarget - xGap) * Mathf.Min(1f, 20f * dt);
            if (adsVig != null) adsVig.color = new Color(1f, 1f, 1f, Mathf.Clamp01(adsVigTarget));
            float len = xShot ? 10f : 6f;
            float a = 0.85f * Mathf.Clamp01(1f - xAds * 1.6f);
            for (int i = 0; i < 4; i++)
            {
                bool vert = i < 2;
                xDash[i].sizeDelta = vert ? new Vector2(2f, len) : new Vector2(len, 2f);
                float d = xGap;
                xDash[i].anchoredPosition = i == 0 ? new Vector2(0, d) : i == 1 ? new Vector2(0, -d) : i == 2 ? new Vector2(d, 0) : new Vector2(-d, 0);
                var c = xDashImg[i].color; c.a = a; xDashImg[i].color = c;
            }
            xDot.rectTransform.sizeDelta = Vector2.one * 2f;
            var dc = xDot.color; dc.a = 0.7f * a; xDot.color = dc;
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

        /// <summary>Alfa di una scritta che compare e svanisce: entra in 0,4 s, esce nell'ultimo secondo.</summary>
        static float FadeAlpha(float left, float total) =>
            Mathf.Clamp01((total - left) / 0.4f) * Mathf.Clamp01(left / 1f);

        static void SetAlpha(Text t, Color baseCol, float a)
        {
            if (t == null) return;
            var c = baseCol; c.a *= a; t.color = c;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateHitmarker(dt);
            UpdateDamageDirections(dt);
            UpdateCrosshair(dt);
            UpdateStealthUI(dt);
            UpdateRoundUI(dt);
            UpdatePuntiUI(dt);
            UpdatePerksUI(dt);
            UpdateCoopUI();
            if (bannerT > 0f)
            {
                bannerT -= dt;
                float a = FadeAlpha(bannerT, BannerTime);
                SetAlpha(bannerText, Ink, a);
                SetAlpha(bannerSub, Dim, a);
                if (bannerT <= 0f) { bannerText.text = ""; bannerSub.text = ""; }
            }
            if (toastT > 0f)
            {
                toastT -= dt;
                SetAlpha(toastText, Dim, FadeAlpha(toastT, ToastTime));
                if (toastT <= 0f) toastText.text = "";
            }
            if (vignette != null)
            {
                // colpo appena preso + bordo fisso quando la vita e' bassa
                vigT = Mathf.Max(0f, vigT - dt);
                float threshold = Perks.MaxHp * 0.35f;
                float low = Mathf.Clamp01((threshold - hpNow) / threshold) * 0.55f;
                float a = Mathf.Max(low, vigT / 0.6f * 0.85f);
                vignette.color = new Color(1f, 1f, 1f, a);
            }
        }

        public void SetBars(float hp, float shield)
        {
            if (hpBar == null) return;
            hpNow = hp;
            // Pelle di pietra: la vita massima sale a 150 e la barra si allunga di un mezzo (la lunghezza dice quanta ne hai)
            float mx = Perks.MaxHp;
            hpBar.value = hp / mx;
            healthText.text = "VITA  " + Mathf.CeilToInt(Mathf.Max(0f, hp)) + " / " + Mathf.RoundToInt(mx) + (shield > 0f ? "   SCUDO " + Mathf.CeilToInt(shield) : "");
            healthText.color = hp < mx * 0.35f ? RoundHot : Ink;
            hpBar.fillRect.GetComponent<Image>().color = hp < mx * 0.35f ? RoundHot : Ink;
            hpBar.GetComponent<RectTransform>().sizeDelta = new Vector2(220f * mx / 100f, 7f);
            shieldBar.value = shield / 50f;
        }

        public void SetAmmo(int m, int r)
        {
            if (ammoText == null) return;
            ammoNow = Mathf.Max(0, m); reserveNow = Mathf.Max(0, r);
            ammoText.text = ammoNow.ToString();
            reserveText.text = "/ " + reserveNow;
            UpdateAmmoStatus();
        }

        public void SetReloading(bool on) { reloadActive = on; UpdateAmmoStatus(); }
        void UpdateAmmoStatus()
        {
            if (ammoText == null || reloadText == null) return;
            ammoText.color = ammoNow <= Mathf.Max(1, Mathf.CeilToInt(clipCapacity * 0.2f)) ? RoundHot : Ink;
            reloadText.text = reloadActive ? "RICARICA…" : ammoNow > 0 ? "" : reserveNow > 0 ? "CARICATORE VUOTO · RICARICA" : "MUNIZIONI ESAURITE";
            reloadText.color = !reloadActive && ammoNow == 0 && reserveNow == 0 ? RoundHot : Ink;
        }
        public void SetObjective(string s) { if (objectiveText != null) objectiveText.text = s ?? ""; }
        public void SetWaveLabel(string s) { if (waveLabel != null) waveLabel.text = s?.ToUpperInvariant() ?? ""; }
        public void SetFade(float a) { if (fadeImg != null) { fadeImg.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a)); fadeImg.transform.SetAsLastSibling(); } }
        public void SetWave(int w) { if (waveText != null) waveText.text = Roman(w); }
        public void SetPrompt(string s) { if (promptText != null) promptText.text = s ?? ""; }
        public string CurrentPrompt => promptText != null ? promptText.text : "";
        public string CurrentAmmo => ammoText != null ? ammoText.text : "";
        public string CurrentReserve => reserveText != null ? reserveText.text : "";
        public string CurrentWeaponName => weaponNameText != null ? weaponNameText.text : "";
        /// <summary>Quanto e' lunga la barra della vita (220 con 100 di vita massima, 330 con Pelle di pietra).</summary>
        public float HpBarWidth => hpBar != null ? hpBar.GetComponent<RectTransform>().sizeDelta.x : 0f;
        public void SetWeaponName(string s) { if (weaponNameText != null) weaponNameText.text = s ?? ""; }

        /// <summary>Arma in mano: nome nel colore del grado, riga con grado, tipo e modalita' di fuoco, i due slot.</summary>
        public void SetWeapon(WeaponDef d, string mode, int slot, WeaponDef a, WeaponDef b, bool upg = false, bool upgA = false, bool upgB = false)
        {
            if (weaponNameText == null || d == null) return;
            clipCapacity = upg ? Mathf.CeilToInt(d.mag * Weapon.UpgMag) : d.mag;
            UpdateAmmoStatus();
            weaponNameText.supportRichText = true;
            // arma consacrata (Consacrazione): nome in oro con la scritta accanto
            weaponNameText.text = upg ? d.name + "  <size=13><color=#F5C84C>CONSACRATA</color></size>" : d.name;
            weaponNameText.color = upg ? Gold : d.GradeColor;
            weaponSubText.text = $"{d.cls}  ·  {mode}  ·  {d.GradeName.ToUpperInvariant()}";
            string s1 = a != null ? (upgA ? a.name + " +" : a.name) : "—", s2 = b != null ? (upgB ? b.name + " +" : b.name) : "—";
            string c1 = slot == 0 ? "#EDE3CC" : "#929694", c2 = slot == 1 ? "#EDE3CC" : "#929694";
            slotText.supportRichText = true;
            slotText.text = $"<color={c1}>[1] {s1}</color>     <color={c2}>[2] {s2}</color>";
        }
        public void SetScore(int s) { if (scoreText != null) scoreText.text = string.Format(It, "{0:N0}", Mathf.Max(0, s)); }
        public void SetEnemiesLeft(int n)
        {
            if (enemiesText == null) return;
            // nei piani il numero esatto delle presenze sarebbe un indizio gratis: al buio non lo si sa
            if (Stealth.Enabled) { enemiesText.text = ""; return; }
            enemiesText.text = n <= 0 ? "" : n == 1 ? "una presenza" : n + " presenze";
        }

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

        public void Banner(string t, string sub)
        {
            if (bannerText == null) return;
            bannerText.text = t; bannerSub.text = sub;
            bannerT = BannerTime;
            SetAlpha(bannerText, Ink, 0f); SetAlpha(bannerSub, Dim, 0f);
        }

        public void Toast(string s)
        {
            if (toastText == null) return;
            toastText.text = s;
            toastT = ToastTime;
            SetAlpha(toastText, Dim, 0f);
        }

        public void DamageFlash(Vector3? origin = null)
        {
            vigT = 0.6f;
            if (origin.HasValue) ShowDamageDirection(origin.Value);
        }
    }
}
