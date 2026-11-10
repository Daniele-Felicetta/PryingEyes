using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>HUD con ancore agli angoli, costruito a runtime (riusa i figli se esistono gia').
    /// Tono "grand hotel": Georgia di sistema (niente font da scaricare), avorio su nero, tutto in italiano,
    /// niente scritte da sala giochi. L'ondata e' un numero romano, i nemici sono "presenze"; l'hit marker resta
    /// quello stile CoD.</summary>
    public class HUD : MonoBehaviour
    {
        Slider hpBar, shieldBar;
        Text ammoText, reserveText, reloadText, waveLabel, waveText, enemiesText, scoreText, bannerText, bannerSub, toastText;
        Text promptText, weaponNameText, objectiveText;
        Image fadeImg;
        Image vignette, adsVig;
        RectTransform hitRoot;      // hit marker: X di 4 tacche attorno al mirino
        Image[] hitTicks;
        RectTransform[] xDash;      // mirino: quattro trattini attorno al centro
        Image[] xDashImg;
        Image xDot;                 // punto centrale (solo DMR, in mira)
        float xGap = 8f, xGapTarget = 8f, xAds;
        string xCls = "";
        bool xShot;
        Text weaponSubText, slotText;
        Color hitCol = Color.white;
        float hitT, hitDur, hitPop, hitSize, bannerT, toastT, vigT, hpNow = 100f;
        static Font serif;
        static readonly Color Ink = new Color(0.93f, 0.89f, 0.80f);
        static readonly Color Dim = new Color(0.74f, 0.67f, 0.56f, 0.92f);
        static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");
        const float BannerTime = 4.5f, ToastTime = 4.5f;

        void Awake() { EnsureBuilt(); }

        public static Font Serif()
        {
            if (serif == null)
                serif = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Palatino Linotype", "Book Antiqua", "Times New Roman" }, 32);
            return serif;
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

            // in alto a sinistra: ondata in numeri romani, presenze, punti
            waveLabel = MakeText("WaveLabel", new Vector2(0, 1), new Vector2(26, -16), new Vector2(300, 22), 15, TextAnchor.UpperLeft, "ondata", Dim, FontStyle.Italic);
            waveText = MakeText("Wave", new Vector2(0, 1), new Vector2(24, -34), new Vector2(300, 56), 46, TextAnchor.UpperLeft, "I", Ink, FontStyle.Normal);
            enemiesText = MakeText("Enemies", new Vector2(0, 1), new Vector2(26, -92), new Vector2(320, 24), 17, TextAnchor.UpperLeft, "", Dim, FontStyle.Italic);
            scoreText = MakeText("Score", new Vector2(0, 1), new Vector2(26, -114), new Vector2(320, 22), 15, TextAnchor.UpperLeft, "", Dim, FontStyle.Normal);
            // in basso a sinistra: vita e scudo, sottili e senza etichette
            hpBar = MakeBar("HpBar", new Vector2(0, 0), new Vector2(28, 30), new Vector2(220, 6), new Color(0.62f, 0.10f, 0.09f));
            shieldBar = MakeBar("ShieldBar", new Vector2(0, 0), new Vector2(28, 42), new Vector2(220, 4), new Color(0.56f, 0.68f, 0.80f));
            // in basso a destra: colpi nel caricatore grandi, riserva piccola
            ammoText = MakeText("Ammo", new Vector2(1, 0), new Vector2(-92, 16), new Vector2(160, 58), 48, TextAnchor.LowerRight, "30", Ink, FontStyle.Normal);
            reserveText = MakeText("Reserve", new Vector2(1, 0), new Vector2(-26, 24), new Vector2(70, 30), 20, TextAnchor.LowerRight, "/ 120", Dim, FontStyle.Normal);
            reloadText = MakeText("Reload", new Vector2(1, 0), new Vector2(-28, 76), new Vector2(220, 22), 15, TextAnchor.LowerRight, "", Dim, FontStyle.Italic);
            weaponNameText = MakeText("WeaponName", new Vector2(1, 0), new Vector2(-26, 100), new Vector2(420, 24), 19, TextAnchor.LowerRight, "", Dim, FontStyle.Normal);
            weaponSubText = MakeText("WeaponSub", new Vector2(1, 0), new Vector2(-26, 122), new Vector2(520, 20), 13, TextAnchor.LowerRight, "", Dim, FontStyle.Italic);
            slotText = MakeText("Slots", new Vector2(1, 0), new Vector2(-26, 142), new Vector2(520, 20), 13, TextAnchor.LowerRight, "", Dim, FontStyle.Normal);
            // prompt delle armi sul muro (acquisto con F)
            promptText = MakeText("Prompt", new Vector2(0.5f, 0.5f), new Vector2(0, -118), new Vector2(1000, 28), 18, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            MakeCrosshair();
            MakeHitmarker();
            bannerText = MakeText("Banner", new Vector2(0.5f, 1), new Vector2(0, -118), new Vector2(1000, 66), 54, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            bannerSub = MakeText("BannerSub", new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(1000, 30), 20, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            toastText = MakeText("Toast", new Vector2(0.5f, 0), new Vector2(0, 108), new Vector2(1000, 26), 17, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            objectiveText = MakeText("Objective", new Vector2(0, 1), new Vector2(26, -138), new Vector2(560, 24), 16, TextAnchor.UpperLeft, "", Dim, FontStyle.Italic);
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
            t.font = Serif();
            t.fontSize = fs; t.alignment = a; t.text = s;
            t.fontStyle = style;
            t.color = col;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
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
            UpdateCrosshair(dt);
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
                float low = hpNow < 35f ? (35f - hpNow) / 35f * 0.55f : 0f;
                float a = Mathf.Max(low, vigT / 0.6f * 0.85f);
                vignette.color = new Color(1f, 1f, 1f, a);
            }
        }

        public void SetBars(float hp, float shield)
        {
            if (hpBar == null) return;
            hpNow = hp;
            hpBar.value = hp / 100f;
            shieldBar.value = shield / 50f;
        }

        public void SetAmmo(int m, int r)
        {
            if (ammoText == null) return;
            ammoText.text = m.ToString();
            ammoText.color = m <= 5 ? new Color(0.85f, 0.25f, 0.2f) : Ink;
            reserveText.text = "/ " + r;
        }

        public void SetReloading(bool on) { if (reloadText != null) reloadText.text = on ? "ricarica…" : ""; }
        public void SetObjective(string s) { if (objectiveText != null) objectiveText.text = s ?? ""; }
        public void SetWaveLabel(string s) { if (waveLabel != null) waveLabel.text = s ?? ""; }
        public void SetFade(float a) { if (fadeImg != null) { fadeImg.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a)); fadeImg.transform.SetAsLastSibling(); } }
        public void SetWave(int w) { if (waveText != null) waveText.text = Roman(w); }
        public void SetPrompt(string s) { if (promptText != null) promptText.text = s ?? ""; }
        public string CurrentPrompt => promptText != null ? promptText.text : "";
        public string CurrentAmmo => ammoText != null ? ammoText.text : "";
        public string CurrentReserve => reserveText != null ? reserveText.text : "";
        public void SetWeaponName(string s) { if (weaponNameText != null) weaponNameText.text = s ?? ""; }

        /// <summary>Arma in mano: nome nel colore del grado, riga con grado, tipo e modalita' di fuoco, i due slot.</summary>
        public void SetWeapon(WeaponDef d, string mode, int slot, WeaponDef a, WeaponDef b)
        {
            if (weaponNameText == null || d == null) return;
            weaponNameText.text = d.name;
            weaponNameText.color = d.GradeColor;
            weaponSubText.text = $"GRADO {Roman(d.grade)} · {d.GradeName.ToUpperInvariant()}  —  {d.cls}  —  {mode}";
            string s1 = a != null ? a.name : "—", s2 = b != null ? b.name : "—";
            string c1 = slot == 0 ? "#EDE3CC" : "#8A7F6C", c2 = slot == 1 ? "#EDE3CC" : "#8A7F6C";
            slotText.supportRichText = true;
            slotText.text = $"<color={c1}>[1] {s1}</color>     <color={c2}>[2] {s2}</color>";
        }
        public void SetScore(int s) { if (scoreText != null) scoreText.text = s > 0 ? string.Format(It, "{0:N0} punti", s) : ""; }
        public void SetEnemiesLeft(int n)
        {
            if (enemiesText == null) return;
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

        public void DamageFlash() { vigT = 0.6f; }
    }
}
