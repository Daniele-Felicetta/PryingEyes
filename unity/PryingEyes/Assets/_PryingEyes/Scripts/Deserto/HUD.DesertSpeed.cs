using PryingEyes.Deserto;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il tachimetro della moto (0.9.3): un quadrante tondo in basso, a sinistra del centro, che c'e' solo in sella. Lancetta da 0 a 120 km/h (270 gradi), numero grande in km/h, la fascia gialla
    /// del turbo (oltre la velocita' massima) e la spia TURBO che si accende quando il turbo e' inserito; in retromarcia il numero e' accompagnato da una R. E' fatto di una texture dipinta a codice
    /// (le tacche, la fascia, il bordo d'ottone: una volta sola per tutti i posti) e di pochi elementi; ogni posto dello schermo condiviso ha il suo, sulla sua porzione di schermo. E' un di piu': se
    /// inciampa tre volte si spegne per sempre e il resto dell'HUD va avanti.</summary>
    public partial class HUD
    {
        const float SpSize = 200f, SpFull = 120f, SpSweep = 270f, SpStart = 135f;
        static readonly Color SpInk = new Color(0.93f, 0.89f, 0.80f), SpAmber = new Color(1f, 0.62f, 0.16f), SpNeedleCol = new Color(1f, 0.40f, 0.14f);

        RectTransform spRoot, spNeedle;
        Text spNumber, spUnit, spTurbo;
        float spKmh = -1f, spNeedleDeg, spFullNow = SpFull;
        int spFaults, spShownKmh = -1, spShownAltitude = -1;
        bool spDead, spReverse;
        /// <summary>La moto di cui e' fatto il quadrante: la fascia gialla e il fondo scala sono i suoi (le moto della pista vanno oltre i 120).</summary>
        DesertBikeDef spDef;
        static Sprite spNeedleSprite;
        static readonly System.Collections.Generic.Dictionary<int, Sprite> spDials = new System.Collections.Generic.Dictionary<int, Sprite>();

        /// <summary>Per le prove: il tachimetro e' acceso, il numero che mostra (km/h), dove punta la lancetta (gradi in senso orario dall'alto: -135 a 0 km/h, +135 a 120) e se la spia del turbo e' accesa.</summary>
        public bool SpeedoShown => spRoot != null && spRoot.gameObject.activeSelf;
        public int SpeedoKmh => spNumber != null && int.TryParse(spNumber.text, out var v) ? v : -1;
        public float SpeedoNeedleDeg => spNeedleDeg;
        public bool SpeedoTurboLit => spDef != null && !spDef.IsPlane && spTurbo != null && spTurbo.color.a > 0.5f;
        public bool SpeedoReverseShown => spReverse;
        /// <summary>L'angolo della lancetta (gradi in senso orario dall'alto) per una velocita' in km/h.</summary>
        public static float SpeedoDegFor(float kmh) => -SpStart + SpSweep * Mathf.Clamp01(kmh / SpFull);

        void UpdateDesertSpeedUI()
        {
            if (spDead) return;
            try { TickDesertSpeed(); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                if (++spFaults >= 3) { spDead = true; if (spRoot != null) spRoot.gameObject.SetActive(false); }
            }
        }

        void TickDesertSpeed()
        {
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            bool show = DesertMode.Active && pl != null && pl.Riding && pl.alive && !GameMenu.Blocked;
            if (!show)
            {
                if (spRoot != null && spRoot.gameObject.activeSelf) spRoot.gameObject.SetActive(false);
                spKmh = -1f; spShownKmh = -1;
                return;
            }
            var def = pl.Bike != null ? pl.Bike.Def : DesertBikeDefs.Starter;
            if (spRoot != null && def != spDef) { Destroy(spRoot.gameObject); spRoot = null; }          // un'altra moto: un altro quadrante
            if (spRoot == null) BuildDesertSpeed(def);
            float target = Mathf.Abs(pl.RideSpeed) * 3.6f;
            if (!spRoot.gameObject.activeSelf) spRoot.gameObject.SetActive(true);
            // la lancetta e' un po' meccanica (si assesta in un decimo di secondo), il numero e' quello vero
            spKmh = spKmh < 0f ? target : Mathf.Lerp(spKmh, target, 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
            spNeedleDeg = -SpStart + SpSweep * Mathf.Clamp01(spKmh / spFullNow);
            spNeedle.localRotation = Quaternion.Euler(0f, 0f, -spNeedleDeg);
            int k = Mathf.RoundToInt(target);
            if (k != spShownKmh)
            {
                spShownKmh = k;
                spNumber.text = k.ToString();
                spNumber.color = target > def.top * 3.6f + 1f ? SpAmber : SpInk;
            }
            bool rev = pl.RideSpeed < -0.3f;
            if (rev != spReverse) { spReverse = rev; spUnit.text = rev ? "km/h  R" : "km/h"; }
            if (def.IsPlane)
            {
                int altitude = Mathf.RoundToInt(pl.RideAltitude);
                if (altitude != spShownAltitude) { spShownAltitude = altitude; spTurbo.text = "QUOTA " + altitude + " m"; }
            }
            else
            {
                var tc = spTurbo.color; tc.a = pl.RideBoosting ? 1f : 0.16f; if (!Mathf.Approximately(tc.a, spTurbo.color.a)) spTurbo.color = tc;
            }
        }

        static Text SpText(Transform parent, string name, Vector2 pos, Vector2 size, int fs, Color col, FontStyle style)
        {
            var t = Txt(parent, name, Vector2.zero, size, fs, TextAnchor.MiddleCenter, col, style);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.supportRichText = false;
            return t;
        }

        void BuildDesertSpeed(DesertBikeDef def)
        {
            spDef = def;
            // il fondo scala: 120 km/h per la moto di casa, 160 per le moto della pista (la piu' veloce arriva a 137 col turbo)
            float full = def.boost * 3.6f > SpFull - 2f ? 160f : SpFull;
            spFullNow = full;
            var go = new GameObject("TachimetroMoto", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            spRoot = (RectTransform)go.transform;
            spRoot.anchorMin = spRoot.anchorMax = new Vector2(0.5f, 0f);
            spRoot.pivot = new Vector2(0.5f, 0f);
            // a sinistra del centro: tra le barre della vita (a sinistra) e il testo della moto (al centro) non da' noia a niente; la minimappa e' in alto
            spRoot.anchoredPosition = new Vector2(-292f, 22f);
            spRoot.sizeDelta = new Vector2(SpSize, SpSize);
            DmImg("Fondo", spRoot, SpSize - 8f, new Color(0.06f, 0.05f, 0.045f, 0.66f), CircleSprite());
            DmImg("Quadrante", spRoot, SpSize, Color.white, SpeedDialSprite(def.top * 3.6f, def.boost * 3.6f, full));
            // i numeri sulle tacche grandi (ogni 20 km/h)
            float r = 61f;
            for (int v = 0; v <= (int)full; v += 20)
            {
                float phi = (-SpStart + SpSweep * Mathf.Clamp01(v / full)) * Mathf.Deg2Rad;
                var t = SpText(spRoot, "Num" + v, new Vector2(Mathf.Sin(phi), Mathf.Cos(phi)) * r, new Vector2(30f, 16f), 12, v > def.top * 3.6f ? SpAmber : SpInk, FontStyle.Bold);
                t.text = v.ToString();
            }
            spTurbo = SpText(spRoot, "Turbo", new Vector2(0f, 34f), new Vector2(60f, 14f), 11, SpAmber, FontStyle.Bold);
            spTurbo.text = def.IsPlane ? "QUOTA 0 m" : "TURBO";
            spNumber = SpText(spRoot, "Numero", new Vector2(0f, -52f), new Vector2(90f, 38f), 34, SpInk, FontStyle.Bold);
            spNumber.text = "0";
            spUnit = SpText(spRoot, "Unita", new Vector2(0f, -76f), new Vector2(70f, 14f), 11, new Color(SpInk.r, SpInk.g, SpInk.b, 0.75f), FontStyle.Bold);
            spUnit.text = "km/h";
            // la lancetta (con il fulcro a un quinto dalla base) e il mozzo
            var nImg = DmImg("Lancetta", spRoot, 0f, SpNeedleCol, SpeedNeedleSprite());
            spNeedle = nImg.rectTransform;
            spNeedle.sizeDelta = new Vector2(11f, 88f);
            spNeedle.pivot = new Vector2(0.5f, 0.2f);
            spNeedle.anchoredPosition = Vector2.zero;
            DmImg("MozzoOrlo", spRoot, 15f, new Color(0.05f, 0.04f, 0.035f, 0.95f), CircleSprite());
            DmImg("Mozzo", spRoot, 9f, SpNeedleCol, CircleSprite());
            spRoot.gameObject.SetActive(false);
            spReverse = false; spShownKmh = spShownAltitude = -1;
        }

        /// <summary>Un'immagine centrata sul padre (quadrata, di lato 'size').</summary>
        Image DmImg(string name, Transform parent, float size, Color col, Sprite sprite)
        {
            var img = DmMarker(name, parent, size).gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = col; img.raycastTarget = false;
            return img;
        }

        // ------------------------------------------------------------------ texture dipinte a codice
        /// <summary>Il quadrante: bordo d'ottone, fascia di 270 gradi (chiara fino alla velocita' massima, gialla nella zona del turbo, rossa oltre), tacche ogni 10 km/h e piu' lunghe ogni 20. Una volta sola.</summary>
        static Sprite SpeedDialSprite(float top, float boost, float full)
        {
            int key = Mathf.RoundToInt(top * 4f) * 1000000 + Mathf.RoundToInt(boost * 4f) * 1000 + Mathf.RoundToInt(full);
            if (spDials.TryGetValue(key, out var cached) && cached != null) return cached;
            const int N = 256;
            var px = new Color32[N * N];
            float c = N * 0.5f;
            var brass = new Color(0.76f, 0.60f, 0.30f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float phi = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;                    // gradi in senso orario dall'alto
                    Color col = Color.clear; float a = 0f;
                    // il bordo d'ottone: un anello pieno
                    float ring = Mathf.Clamp01(Mathf.Min(r - 121f, 125f - r) + 0.5f);
                    if (ring > 0f) { col = brass; a = ring * 0.95f; }
                    if (Mathf.Abs(phi) <= SpStart + 0.5f)
                    {
                        float kmh = (phi + SpStart) / SpSweep * full;
                        Color zone = kmh <= top ? new Color(0.84f, 0.85f, 0.82f) : kmh <= boost ? SpAmber : new Color(0.95f, 0.20f, 0.12f);
                        // la fascia
                        float band = Mathf.Clamp01(Mathf.Min(r - 111f, 116f - r) + 0.5f) * Mathf.Clamp01((SpStart - Mathf.Abs(phi)) * r * 0.01745f + 0.5f);
                        if (band > a) { col = zone; a = band * 0.95f; }
                        // le tacche: la piu' vicina, a multipli di 10 km/h (22,5 gradi)
                        float step = SpSweep / (full / 10f);
                        float nearest = Mathf.Round((phi + SpStart) / step) * step - SpStart;
                        float lateral = Mathf.Abs(Mathf.Sin((phi - nearest) * Mathf.Deg2Rad)) * r;
                        int idx = Mathf.RoundToInt((phi + SpStart) / step);
                        bool major = idx % 2 == 0;
                        float r0 = major ? 96f : 103f, half = major ? 1.4f : 0.9f;
                        float tick = Mathf.Clamp01(half + 0.5f - lateral) * Mathf.Clamp01(Mathf.Min(r - r0, 109f - r) + 0.5f);
                        if (tick > 0f && Mathf.Abs(nearest) <= SpStart + 0.1f)
                        {
                            float tk = (nearest + SpStart) / SpSweep * full;
                            Color tcol = tk <= top + 1f ? new Color(0.93f, 0.89f, 0.80f) : SpAmber;
                            if (tick > a) { col = tcol; a = tick * 0.95f; }
                        }
                    }
                    px[y * N + x] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(Mathf.Clamp01(a) * 255f));
                }
            return spDials[key] = MateSprite(px, N, "TachimetroQuadrante");
        }

        /// <summary>La lancetta: un triangolo lungo e sottile che parte dal fulcro (un quinto dalla base), con una coda corta, bianco: il colore lo da' l'immagine.</summary>
        static Sprite SpeedNeedleSprite()
        {
            if (spNeedleSprite != null) return spNeedleSprite;
            const int W = 16, H = 128;
            var px = new Color32[W * H];
            float hub = H * 0.2f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float half = y >= hub ? Mathf.Lerp(3.4f, 0.5f, (y - hub) / (H - hub)) : (y >= 6 ? 3.0f : 0f);
                    float a = Mathf.Clamp01(half - Mathf.Abs(x + 0.5f - W * 0.5f) + 0.5f);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "TachimetroLancetta", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return spNeedleSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.2f), 100f);
        }
    }
}
