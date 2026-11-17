using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Le due tavole dell'utente come arte dei grimori (2026-11-17): "Potenziamenti delle Armi - I 19 Sigilli dell'Arsenale
    /// Maledetto" e "Perk del Dodecaedro Arcano Maledetto - Le 19 Virtu' della Stregoneria". modelli/grimori/prepara_tavole.py le prepara
    /// (svuota i diciannove medaglioni, rifa' quelli mancanti, sfuma il bordo) e scrive in Resources/Grimori/ due PNG come TextAsset
    /// (tavola_*.bytes, decodificati qui con LoadImage: Unity non li tocca) e tavole.json con grandezza e posizione dei vani, nei pixel
    /// della tavola. GrimoireUI ci disegna sopra le icone e gli stati dei nodi; il libro sull'altare (GrimoireAltar) usa il ritaglio del
    /// libro aperto (altare_*.bytes) e la sua maschera di luce (luce_*.bytes). Indici dei vani come i nodi: 0..11 esterni, 12..17 interni,
    /// 18 il supremo (al centro, dove la tavola ha gia' il suo emblema: il teschio coi fucili, il caprone).</summary>
    public static class GrimoirePage
    {
        /// <summary>La doppia pagina del libro sull'altare: il ritaglio della tavola, meta' per foglio.</summary>
        public static readonly Rect LeftPage = new Rect(0f, 0f, 0.5f, 1f), RightPage = new Rect(0.5f, 0f, 0.5f, 1f);

        public sealed class Plate
        {
            public Grimoire.Book book;
            public Texture2D tex;
            public int w, h;
            /// <summary>Centro di ogni vano nei pixel della tavola (x da sinistra, y dall'alto) e raggio del vano scuro.</summary>
            public readonly Vector2[] slot = new Vector2[Grimoire.Count];
            public readonly float[] radius = new float[Grimoire.Count];
            /// <summary>false: mancano i file (segnaposto scuro con la stessa disposizione, il libro funziona lo stesso).</summary>
            public bool real;
        }

        [Serializable] public sealed class PlateData { public string book; public int w, h; public float[] outer, inner, center; }
        [Serializable] public sealed class PlateFile { public PlateData[] plates; }

        static readonly Dictionary<Grimoire.Book, Plate> plates = new Dictionary<Grimoire.Book, Plate>();
        static readonly Dictionary<Grimoire.Book, Texture2D> altars = new Dictionary<Grimoire.Book, Texture2D>();
        static readonly Dictionary<Grimoire.Book, Texture2D> glows = new Dictionary<Grimoire.Book, Texture2D>();
        static PlateFile file;
        static Texture2D card;

        static string Key(Grimoire.Book b) => b == Grimoire.Book.Stregoneria ? "stregoneria" : "arsenale";

        // ------------------------------------------------------------------ la tavola
        /// <summary>La tavola del libro (la carica la prima volta).</summary>
        public static Plate Get(Grimoire.Book book)
        {
            if (plates.TryGetValue(book, out var p) && p.tex != null) return p;
            p = Load(book);
            plates[book] = p;
            return p;
        }

        static Plate Load(Grimoire.Book book)
        {
            var p = new Plate { book = book };
            string key = Key(book);
            try
            {
                if (file == null)
                {
                    var js = Resources.Load<TextAsset>("Grimori/tavole");
                    if (js != null) file = JsonUtility.FromJson<PlateFile>(js.text);
                }
                PlateData data = null;
                if (file != null && file.plates != null)
                    foreach (var d in file.plates) if (d != null && d.book == key) data = d;
                var png = Resources.Load<TextAsset>("Grimori/tavola_" + key);
                if (data != null && png != null && data.outer != null && data.outer.Length >= 36 && data.inner != null && data.inner.Length >= 18 && data.center != null && data.center.Length >= 3)
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Tavola_" + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                    if (t.LoadImage(png.bytes, true))
                    {
                        p.tex = t; p.w = t.width; p.h = t.height; p.real = true;
                        for (int i = 0; i < 12; i++) { p.slot[i] = new Vector2(data.outer[i * 3], data.outer[i * 3 + 1]); p.radius[i] = data.outer[i * 3 + 2]; }
                        for (int k = 0; k < 6; k++) { p.slot[12 + k] = new Vector2(data.inner[k * 3], data.inner[k * 3 + 1]); p.radius[12 + k] = data.inner[k * 3 + 2]; }
                        p.slot[Grimoire.Supreme] = new Vector2(data.center[0], data.center[1]); p.radius[Grimoire.Supreme] = data.center[2];
                        return p;
                    }
                    UnityEngine.Object.Destroy(t);
                }
            }
            catch (Exception e) { Debug.LogWarning("[PryingEyes] tavola del grimorio " + key + ": " + e.Message); }
            return Placeholder(p);
        }

        /// <summary>Senza i file: una tavola scura con i vani sulla stessa disposizione (dodici in giro, sei a esagono, uno al centro).</summary>
        static Plate Placeholder(Plate p)
        {
            p.w = 1448; p.h = 1086; p.real = false;
            var t = new Texture2D(8, 6, TextureFormat.RGBA32, false) { name = "Tavola_segnaposto", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 6; y++) for (int x = 0; x < 8; x++) t.SetPixel(x, y, new Color(0.10f + 0.02f * y, 0.025f, 0.02f, 1f));
            t.Apply(false, true);
            p.tex = t;
            var c = new Vector2(p.w * 0.5f, p.h * 0.45f);
            for (int i = 0; i < 12; i++) { float a = (120f - 30f * i) * Mathf.Deg2Rad; p.slot[i] = c + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * (p.h * 0.30f); p.radius[i] = 48f; }
            for (int k = 0; k < 6; k++) { float a = (90f - 60f * k) * Mathf.Deg2Rad; p.slot[12 + k] = c + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * (p.h * 0.155f); p.radius[12 + k] = 42f; }
            p.slot[Grimoire.Supreme] = c; p.radius[Grimoire.Supreme] = 100f;
            return p;
        }

        // ------------------------------------------------------------------ la pergamena della scheda
        static float[] lattice;
        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
        static float Noise(float x, float y)
        {
            if (lattice == null) { lattice = new float[256 * 256]; for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++) lattice[j * 256 + i] = Hash(i, j); }
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = xi & 255, y0 = yi & 255, x1 = (x0 + 1) & 255, y1 = (y0 + 1) & 255;
            float a = lattice[y0 * 256 + x0], b = lattice[y0 * 256 + x1], c = lattice[y1 * 256 + x0], d = lattice[y1 * 256 + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        static float Fbm(float x, float y) => Noise(x, y) * 0.55f + Noise(x * 2.1f + 17f, y * 2.1f + 5f) * 0.28f + Noise(x * 4.3f + 3f, y * 4.3f + 29f) * 0.17f;

        public const int CardW = 512, CardH = 400;

        /// <summary>La pergamena della scheda del nodo, dello stesso tono delle tavole: carta macchiata, bordo strappato e bruciato, qualche
        /// colatura di sangue dal bordo alto. Alfa 0 fuori dal foglio. Una sola per tutti i libri.</summary>
        public static Texture2D Card()
        {
            if (card != null) return card;
            var px = new Color32[CardW * CardH];
            var rnd = new System.Random(77);
            var drips = new List<Vector3>();           // x, lunghezza, larghezza
            for (int i = 0; i < 6; i++) drips.Add(new Vector3(40f + (float)rnd.NextDouble() * (CardW - 80f), 26f + (float)rnd.NextDouble() * (i % 3 == 0 ? 110f : 48f), 3f + (float)rnd.NextDouble() * 4f));
            for (int y = 0; y < CardH; y++)
                for (int x = 0; x < CardW; x++)
                {
                    float dl = x, dr = CardW - 1 - x, dt = CardH - 1 - y, db = y;                 // la riga 0 e' in basso
                    float torn = (Fbm(x * 0.05f + 7f, y * 0.05f) - 0.5f) * 22f + (Noise(x * 0.3f, y * 0.3f) - 0.5f) * 4f;
                    float edge = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dt, db)) + torn - 6f;
                    if (edge < 0f) { px[y * CardW + x] = new Color32(0, 0, 0, 0); continue; }
                    float n = Fbm(x * 0.016f + 11f, y * 0.016f);
                    float stains = Mathf.Clamp01((Fbm(x * 0.008f + 50f, y * 0.008f + 7f) - 0.50f) * 4f);
                    var c = Color.Lerp(new Color(0.76f, 0.63f, 0.44f), new Color(0.58f, 0.45f, 0.30f), n);
                    c = Color.Lerp(c, new Color(0.40f, 0.28f, 0.17f), stains * 0.55f);
                    c *= 0.93f + Noise(x * 0.9f, y * 0.9f) * 0.12f;
                    float burn = 1f - Mathf.Clamp01(edge / 24f);
                    c = Color.Lerp(c, new Color(0.30f, 0.16f, 0.07f), burn * burn * 0.92f);
                    c = Color.Lerp(c, new Color(0.05f, 0.03f, 0.02f), Mathf.Clamp01((burn - 0.78f) * 4.5f));
                    // colature dal bordo alto
                    float fromTop = CardH - 1 - y;
                    foreach (var d in drips)
                    {
                        if (fromTop > d.y) continue;
                        float w = d.z * (1f - 0.45f * fromTop / d.y);                                  // si assottiglia scendendo
                        float k = Mathf.Clamp01(w - Mathf.Abs(x - d.x - Mathf.Sin(fromTop * 0.07f) * 1.2f) + 0.5f);
                        if (k > 0f) c = Color.Lerp(c, new Color(0.34f, 0.025f, 0.015f), k * 0.88f);
                    }
                    px[y * CardW + x] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(edge / 1.5f));
                }
            card = new Texture2D(CardW, CardH, TextureFormat.RGBA32, true) { name = "Scheda del grimorio", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            card.SetPixels32(px);
            card.Apply(true, true);
            return card;
        }

        // ------------------------------------------------------------------ il libro sull'altare
        /// <summary>Le pagine per il libro aperto sull'altare: il libro della tavola (la doppia pagina) e la maschera di luce (le parti rosse
        /// accese: il sangue, le rune). Senza i file: carta scura e niente luce.</summary>
        public static Texture2D Page3D(Grimoire.Book book, out Texture2D glowTex)
        {
            if (altars.TryGetValue(book, out var t) && t != null) { glowTex = glows[book]; return t; }
            string key = Key(book);
            t = Decode("Grimori/altare_" + key, "Altare_" + key, new Color(0.14f, 0.07f, 0.05f), true);
            var g = Decode("Grimori/luce_" + key, "Luce_" + key, Color.black, false);
            altars[book] = t; glows[book] = g;
            glowTex = g;
            return t;
        }

        static Texture2D Decode(string resource, string name, Color fallback, bool srgb)
        {
            var asset = Resources.Load<TextAsset>(resource);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, !srgb) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            if (asset != null && t.LoadImage(asset.bytes, true)) return t;
            t.SetPixels(new[] { fallback, fallback, fallback, fallback });
            t.Apply(true, true);
            return t;
        }
    }
}
