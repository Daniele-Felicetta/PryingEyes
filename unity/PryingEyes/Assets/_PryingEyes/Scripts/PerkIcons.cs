using UnityEngine;

namespace PryingEyes
{
    /// <summary>Le icone dei perk, disegnate a codice (nessun file): ogni simbolo e' una distanza con segno (cerchi, segmenti, poligoni)
    /// campionata in 64 x 64 pixel con il bordo sfumato di un pixel. Quadrato scuro a spigoli tondi, cornice e simbolo nel colore
    /// del perk. Pelle di pietra uno scudo, Mani svelte una freccia che gira, Doppio colpo due proiettili, Rinascita una croce
    /// ansata, Passi di cenere due impronte, Velo un mantello con gli occhi, Occhio della Torre un occhio, Sete di sangue una goccia.</summary>
    public static class PerkIcons
    {
        const int S = 64;
        static readonly Sprite[] cache = new Sprite[8];
        static Sprite empty, white;

        public static Sprite Get(PerkId id)
        {
            int i = (int)id;
            if (cache[i] == null) cache[i] = Make(id, true);
            return cache[i];
        }

        /// <summary>Il posto vuoto: stesso quadrato, cornice spenta, niente simbolo.</summary>
        public static Sprite Empty()
        {
            if (empty == null) empty = Make(PerkId.PelleDiPietra, false);
            return empty;
        }

        /// <summary>Un quadrato bianco pieno (per le barre e le sovrapposizioni a riempimento radiale).</summary>
        public static Sprite White()
        {
            if (white == null)
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Bianco" };
                var px = new Color32[16];
                for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                t.SetPixels32(px); t.Apply(false, true);
                white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            }
            return white;
        }

        // ------------------------------------------------------------------ distanze con segno (negative dentro)
        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
        static float Ellipse(Vector2 p, Vector2 c, Vector2 r) { float m = Mathf.Min(r.x, r.y); return (((p - c) / r).magnitude - 1f) * m; }

        static float Seg(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        static float Poly(Vector2 p, Vector2[] v)
        {
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                float h = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, (w - e * h).magnitude);
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) inside = !inside;
            }
            return inside ? -d : d;
        }

        static float RoundBox(Vector2 p, float half, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - Vector2.one * (half - r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        static float Cover(float d) => Mathf.Clamp01(0.5f - d * (S * 0.5f));

        // ------------------------------------------------------------------ i simboli
        static readonly Vector2[] Shield = { new Vector2(-0.62f, 0.66f), new Vector2(0.62f, 0.66f), new Vector2(0.62f, 0.05f), new Vector2(0f, -0.78f), new Vector2(-0.62f, 0.05f) };
        static readonly Vector2[] Ghost =
        {
            new Vector2(0f, 0.78f), new Vector2(0.30f, 0.60f), new Vector2(0.46f, 0.22f), new Vector2(0.58f, -0.30f), new Vector2(0.72f, -0.72f),
            new Vector2(0.46f, -0.58f), new Vector2(0.24f, -0.76f), new Vector2(0f, -0.58f), new Vector2(-0.24f, -0.76f), new Vector2(-0.46f, -0.58f),
            new Vector2(-0.72f, -0.72f), new Vector2(-0.58f, -0.30f), new Vector2(-0.46f, 0.22f), new Vector2(-0.30f, 0.60f),
        };
        static readonly Vector2[] Arrow = { new Vector2(0.704f, 0.150f), new Vector2(0.313f, 0.066f), new Vector2(0.330f, 0.400f) };
        static readonly Vector2[] Drop = { new Vector2(0f, 0.80f), new Vector2(-0.38f, -0.04f), new Vector2(0.38f, -0.04f) };

        static float Bullet(Vector2 p, float x)
        {
            var v = new[] { new Vector2(x - 0.17f, -0.62f), new Vector2(x + 0.17f, -0.62f), new Vector2(x + 0.17f, 0.10f), new Vector2(x, 0.64f), new Vector2(x - 0.17f, 0.10f) };
            return Poly(p, v);
        }

        static float Foot(Vector2 p, Vector2 c)
        {
            float d = Ellipse(p, c + new Vector2(0f, 0.07f), new Vector2(0.16f, 0.26f));
            d = Mathf.Min(d, Ellipse(p, c + new Vector2(0f, -0.27f), new Vector2(0.12f, 0.11f)));
            d = Mathf.Min(d, Circle(p, c + new Vector2(-0.10f, 0.40f), 0.055f));
            d = Mathf.Min(d, Circle(p, c + new Vector2(0f, 0.45f), 0.058f));
            d = Mathf.Min(d, Circle(p, c + new Vector2(0.10f, 0.40f), 0.055f));
            return d;
        }

        /// <summary>Distanza dal simbolo del perk (negativa dentro). 'p' va da -1 a 1, y verso l'alto.</summary>
        static float Glyph(PerkId id, Vector2 p)
        {
            switch (id)
            {
                case PerkId.PelleDiPietra:
                {
                    float shield = Poly(p, Shield);
                    float inner = Mathf.Abs(Poly(p / 0.66f + new Vector2(0f, 0.06f), Shield) * 0.66f) - 0.035f;       // una seconda linea dentro lo scudo
                    return Mathf.Max(shield, -inner);
                }
                case PerkId.ManiSvelte:
                {
                    float len = p.magnitude;
                    float ring = Mathf.Abs(len - 0.52f) - 0.11f;
                    float a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                    float gap = Mathf.Min(a - 12f, 74f - a) * Mathf.Deg2Rad * Mathf.Max(0.3f, len);       // positiva dentro la bocca dell'anello
                    ring = Mathf.Max(ring, gap);
                    return Mathf.Min(ring, Poly(p, Arrow));
                }
                case PerkId.DoppioColpo:
                {
                    float b = Mathf.Min(Bullet(p, -0.27f), Bullet(p, 0.27f));
                    float seam = Mathf.Min(Seg(p, new Vector2(-0.5f, -0.06f), new Vector2(-0.04f, -0.06f), 0.025f), Seg(p, new Vector2(0.04f, -0.06f), new Vector2(0.5f, -0.06f), 0.025f));
                    return Mathf.Max(b, -seam);
                }
                case PerkId.Rinascita:
                {
                    float d = Mathf.Abs(Circle(p, new Vector2(0f, 0.34f), 0.26f)) - 0.075f;
                    d = Mathf.Min(d, Seg(p, new Vector2(0f, 0.08f), new Vector2(0f, -0.72f), 0.08f));
                    return Mathf.Min(d, Seg(p, new Vector2(-0.42f, -0.18f), new Vector2(0.42f, -0.18f), 0.08f));
                }
                case PerkId.PassiDiCenere:
                    return Mathf.Min(Foot(p, new Vector2(-0.34f, -0.22f)), Foot(p, new Vector2(0.34f, 0.18f)));
                case PerkId.Velo:
                {
                    float cloak = Poly(p, Ghost);
                    float eyes = Mathf.Min(Ellipse(p, new Vector2(-0.17f, 0.22f), new Vector2(0.07f, 0.11f)), Ellipse(p, new Vector2(0.17f, 0.22f), new Vector2(0.07f, 0.11f)));
                    return Mathf.Max(cloak, -eyes);
                }
                case PerkId.Occhio:
                {
                    float lens = Mathf.Max(Circle(p, new Vector2(0f, -0.62f), 1.0f), Circle(p, new Vector2(0f, 0.62f), 1.0f));
                    float ring = Mathf.Abs(p.magnitude - 0.27f) - 0.05f;
                    return Mathf.Min(Mathf.Max(lens, -ring), Circle(p, Vector2.zero, 0.12f));
                }
                default:        // Sete di sangue
                {
                    float d = Mathf.Min(Circle(p, new Vector2(0f, -0.20f), 0.42f), Poly(p, Drop));
                    float glint = Seg(p, new Vector2(-0.18f, -0.30f), new Vector2(-0.18f, -0.10f), 0.035f);
                    return Mathf.Max(d, -glint);
                }
            }
        }

        static Sprite Make(PerkId id, bool glyph)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = glyph ? "Perk" + id : "PerkVuoto", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            Color col = glyph ? Perks.Of(id).color : new Color(0.55f, 0.50f, 0.44f);
            Color back = new Color(0.035f, 0.030f, 0.034f, glyph ? 0.74f : 0.42f);
            Color edge = glyph ? col * 0.92f : col * 0.55f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2((x + 0.5f) / S * 2f - 1f, (y + 0.5f) / S * 2f - 1f);
                    float box = RoundBox(p, 0.94f, 0.24f);
                    float inBox = Cover(box);
                    float border = Cover(Mathf.Abs(box + 0.05f) - 0.05f);
                    Color c = back;
                    c = Color.Lerp(c, new Color(edge.r, edge.g, edge.b, 1f), border);
                    if (glyph)
                    {
                        float g = Cover(Glyph(id, p));
                        c = Color.Lerp(c, new Color(Mathf.Min(1f, col.r * 1.08f), Mathf.Min(1f, col.g * 1.08f), Mathf.Min(1f, col.b * 1.08f), 1f), g);
                    }
                    c.a *= inBox;
                    px[y * S + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
