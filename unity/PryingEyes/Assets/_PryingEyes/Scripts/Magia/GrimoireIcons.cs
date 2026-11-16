using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Le icone dei 38 nodi dei grimori, disegnate a codice come quelle dei perk (PerkIcons): distanze con segno (cerchi, anelli,
    /// segmenti, poligoni, archi) campionate a 128 px con un pixel di sfumatura. Sono maschere bianche (la tinta la mette chi le usa:
    /// avorio per i nodi presi, grigio per quelli chiusi). Piu' il medaglione a spine delle tavole e il disco scuro che ci sta dentro.
    /// I simboli vengono dalle due tavole dell'utente: serpe, occhio, clessidra, mano con l'occhio, luna, albero, goccia, falce, sfera
    /// armillare, runa, cristallo, ampolla; fiamma, fulmine, fiocco di gelo, sangue, ala d'ombra, onda, testa di caprone; mirino,
    /// teschio nel mirino, proiettile che trapassa, esplosione, tre proiettili, bomba, proiettile fantasma, caricatore col piu',
    /// pistola, munizioni che girano, fucile con le frecce, clessidra di piombo; teschio, goccia, fiamma, icore, doppio fulmine,
    /// fiocco di gelo, corona.</summary>
    public static class GrimoireIcons
    {
        const int S = 128;
        static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
        static Sprite medal, disc, ring;

        // ------------------------------------------------------------------ distanze con segno (negative dentro), spazio [-1,1]
        static float Circle(Vector2 p, float x, float y, float r) => (p - new Vector2(x, y)).magnitude - r;
        static float Ring(Vector2 p, float x, float y, float r, float w) => Mathf.Abs((p - new Vector2(x, y)).magnitude - r) - w;
        static float Ellipse(Vector2 p, float x, float y, float rx, float ry)
        {
            Vector2 q = new Vector2((p.x - x) / rx, (p.y - y) / ry);
            return (q.magnitude - 1f) * Mathf.Min(rx, ry);
        }
        static float Seg(Vector2 p, float ax, float ay, float bx, float by, float r)
        {
            Vector2 a = new Vector2(ax, ay), b = new Vector2(bx, by), pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, Vector2.Dot(ba, ba)));
            return (pa - ba * h).magnitude - r;
        }
        static float Box(Vector2 p, float x, float y, float hx, float hy, float rr = 0f)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x - x) - hx + rr, Mathf.Abs(p.y - y) - hy + rr);
            return Vector2.Max(d, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - rr;
        }
        static float Poly(Vector2 p, params float[] xy)
        {
            int n = xy.Length / 2;
            float d = float.MaxValue; bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 vi = new Vector2(xy[i * 2], xy[i * 2 + 1]), vj = new Vector2(xy[j * 2], xy[j * 2 + 1]);
                Vector2 e = vj - vi, w = p - vi;
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Mathf.Max(1e-6f, Vector2.Dot(e, e)));
                d = Mathf.Min(d, b.sqrMagnitude);
                if ((vi.y > p.y) != (vj.y > p.y) && p.x < (vj.x - vi.x) * (p.y - vi.y) / (vj.y - vi.y) + vi.x) inside = !inside;
            }
            return (inside ? -1f : 1f) * Mathf.Sqrt(d);
        }
        /// <summary>Arco di cerchio (spessore w) tra gli angoli a0 e a1 (radianti, antiorario da +x).</summary>
        static float Arc(Vector2 p, float x, float y, float r, float a0, float a1, float w)
        {
            Vector2 q = p - new Vector2(x, y);
            float a = Mathf.Atan2(q.y, q.x);
            float mid = (a0 + a1) * 0.5f, half = Mathf.Abs(a1 - a0) * 0.5f;
            float da = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, mid * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            if (da <= half) return Mathf.Abs(q.magnitude - r) - w;
            Vector2 e0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, e1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r;
            return Mathf.Min((q - e0).magnitude, (q - e1).magnitude) - w;
        }
        static float U(float a, float b) => Mathf.Min(a, b);
        static float U(params float[] v) { float m = float.MaxValue; foreach (var x in v) m = Mathf.Min(m, x); return m; }
        static float Sub(float a, float b) => Mathf.Max(a, -b);
        static Vector2 Rot(Vector2 p, float deg) { float c = Mathf.Cos(deg * Mathf.Deg2Rad), s = Mathf.Sin(deg * Mathf.Deg2Rad); return new Vector2(c * p.x + s * p.y, -s * p.x + c * p.y); }

        // ------------------------------------------------------------------ simboli
        static float Flame(Vector2 p, float sc = 1f)
        {
            p /= sc;
            float outer = U(Circle(p, 0f, -0.28f, 0.42f), Poly(p, -0.40f, -0.18f, 0.0f, 0.78f, 0.40f, -0.18f));
            float tongue = U(Circle(p, -0.02f, -0.36f, 0.2f), Poly(p, -0.2f, -0.3f, 0.06f, 0.25f, 0.2f, -0.3f));
            return Sub(outer, tongue) * sc;
        }
        static float Bolt(Vector2 p, float sc = 1f)
        {
            p /= sc;
            return Poly(p, 0.18f, 0.85f, -0.38f, 0.02f, -0.02f, 0.02f, -0.22f, -0.85f, 0.40f, 0.08f, 0.02f, 0.08f) * sc;
        }
        static float Snow(Vector2 p)
        {
            float d = float.MaxValue;
            for (int k = 0; k < 6; k++)
            {
                Vector2 q = Rot(p, k * 60f);
                d = U(d, Seg(q, 0f, 0f, 0f, 0.8f, 0.055f), Seg(q, 0f, 0.45f, 0.2f, 0.62f, 0.045f), Seg(q, 0f, 0.45f, -0.2f, 0.62f, 0.045f));
            }
            return U(d, Circle(p, 0, 0, 0.12f));
        }
        static float Drop(Vector2 p, float sc = 1f)
        {
            p /= sc;
            return U(Circle(p, 0f, -0.25f, 0.45f), Poly(p, -0.39f, -0.04f, 0f, 0.8f, 0.39f, -0.04f)) * sc;
        }
        static float Skull(Vector2 p, float sc = 1f)
        {
            p /= sc;
            float head = U(Circle(p, 0f, 0.15f, 0.58f), Box(p, 0f, -0.35f, 0.32f, 0.22f, 0.08f));
            float holes = U(Circle(p, -0.22f, 0.08f, 0.15f), Circle(p, 0.22f, 0.08f, 0.15f), Poly(p, -0.07f, -0.2f, 0f, -0.05f, 0.07f, -0.2f));
            float teeth = U(Box(p, -0.1f, -0.48f, 0.025f, 0.12f), Box(p, 0.1f, -0.48f, 0.025f, 0.12f), Box(p, 0f, -0.48f, 0.025f, 0.12f));
            return Sub(Sub(head, holes), teeth) * sc;
        }
        static float Eye(Vector2 p, float sc = 1f)
        {
            p /= sc;
            float lid = Mathf.Max(Circle(p, 0f, -0.55f, 0.85f), Circle(p, 0f, 0.55f, 0.85f));
            float white = Sub(lid, U(Circle(p, 0f, 0f, 0.26f)));
            return U(white, Circle(p, 0f, 0f, 0.13f)) * sc;
        }
        static float Bullet(Vector2 p, float x, float y, float sc, float deg = 0f)
        {
            Vector2 q = Rot(new Vector2(p.x - x, p.y - y), deg) / sc;
            return U(Box(q, 0f, -0.25f, 0.16f, 0.42f), Ellipse(q, 0f, 0.18f, 0.16f, 0.36f), Box(q, 0f, -0.7f, 0.19f, 0.05f)) * sc;
        }
        static float Hourglass(Vector2 p)
        {
            float glass = U(Poly(p, -0.45f, 0.62f, 0.45f, 0.62f, 0.06f, 0f, -0.06f, 0f), Poly(p, -0.06f, 0f, 0.06f, 0f, 0.45f, -0.62f, -0.45f, -0.62f));
            float hollow = U(Poly(p, -0.32f, 0.52f, 0.32f, 0.52f, 0.02f, 0.06f, -0.02f, 0.06f), Poly(p, -0.02f, -0.06f, 0.02f, -0.06f, 0.32f, -0.52f, -0.32f, -0.52f));
            float sand = U(Poly(p, -0.18f, -0.52f, 0.18f, -0.52f, 0f, -0.3f), Poly(p, -0.14f, 0.32f, 0.14f, 0.32f, 0f, 0.1f));
            return U(Sub(glass, hollow), sand, Box(p, 0f, 0.7f, 0.55f, 0.06f), Box(p, 0f, -0.7f, 0.55f, 0.06f));
        }
        static float Crosshair(Vector2 p) =>
            U(Ring(p, 0, 0, 0.55f, 0.055f), Seg(p, 0f, 0.25f, 0f, 0.85f, 0.05f), Seg(p, 0f, -0.25f, 0f, -0.85f, 0.05f),
              Seg(p, 0.25f, 0f, 0.85f, 0f, 0.05f), Seg(p, -0.25f, 0f, -0.85f, 0f, 0.05f), Circle(p, 0, 0, 0.07f));

        static float Shape(int id, Vector2 p)
        {
            switch (id)
            {
                // ---- Stregoneria, esterni
                case 1:  // Lingua di Serpe: serpe a S e lingua biforcuta
                {
                    float body = U(Arc(p, 0.05f, 0.28f, 0.3f, Mathf.PI * 0.1f, Mathf.PI * 1.45f, 0.1f), Arc(p, -0.05f, -0.3f, 0.3f, -Mathf.PI * 0.9f, Mathf.PI * 0.45f, 0.1f));
                    float head = Ellipse(p, 0.36f, 0.42f, 0.16f, 0.11f);
                    return U(body, head, Seg(p, 0.5f, 0.45f, 0.72f, 0.5f, 0.025f), Seg(p, 0.72f, 0.5f, 0.85f, 0.6f, 0.022f), Seg(p, 0.72f, 0.5f, 0.86f, 0.42f, 0.022f));
                }
                case 2: return U(Eye(p, 0.95f), Poly(p, 0.55f, 0.15f, 0.95f, 0.0f, 0.55f, -0.1f));       // Occhio del Corvo: occhio col becco
                case 3: return Sub(Hourglass(p), Seg(p, -0.6f, 0.3f, 0.6f, -0.25f, 0.05f));               // Clessidra Rotta
                case 4:  // Gesto Rapido: mano aperta con l'occhio nel palmo
                {
                    float palm = Box(p, 0f, -0.2f, 0.38f, 0.38f, 0.18f);
                    float fingers = U(Box(p, -0.27f, 0.38f, 0.075f, 0.28f, 0.07f), Box(p, -0.09f, 0.45f, 0.075f, 0.33f, 0.07f), Box(p, 0.09f, 0.43f, 0.075f, 0.31f, 0.07f), Box(p, 0.26f, 0.33f, 0.07f, 0.24f, 0.07f));
                    float thumb = Seg(p, -0.32f, -0.3f, -0.66f, 0.02f, 0.085f);
                    return Sub(U(palm, fingers, thumb), Sub(Eye(p - new Vector2(0f, -0.2f), 0.42f), Circle(p, 0f, -0.2f, 0.04f)));
                }
                case 5: return Sub(Circle(p, 0f, 0f, 0.7f), Circle(p, 0.32f, 0.12f, 0.62f));              // Luna Calante
                case 6:  // Radici Cave: albero con chioma di rami e radici
                {
                    float trunk = Poly(p, -0.12f, -0.55f, 0.12f, -0.55f, 0.08f, 0.15f, -0.08f, 0.15f);
                    float br = U(Seg(p, 0f, 0.1f, -0.5f, 0.55f, 0.06f), Seg(p, 0f, 0.1f, 0.5f, 0.55f, 0.06f), Seg(p, 0f, 0.15f, 0f, 0.75f, 0.06f), Seg(p, -0.25f, 0.33f, -0.22f, 0.7f, 0.045f), Seg(p, 0.25f, 0.33f, 0.24f, 0.7f, 0.045f));
                    float roots = U(Seg(p, 0f, -0.5f, -0.55f, -0.75f, 0.05f), Seg(p, 0f, -0.5f, 0.55f, -0.75f, 0.05f), Seg(p, 0f, -0.5f, 0f, -0.82f, 0.05f));
                    return Sub(U(trunk, br, roots), Ellipse(p, 0f, -0.15f, 0.05f, 0.12f));
                }
                case 7: return U(Drop(p, 0.85f), Sub(Ring(p, 0f, -0.2f, 0.62f, 0.05f), Box(p, 0f, 0.3f, 0.3f, 0.4f)));   // Patto di Sangue: goccia nel cerchio
                case 8:  // Mietitura: falce
                    return U(Seg(p, -0.45f, -0.8f, 0.2f, 0.65f, 0.06f), Sub(Arc(p, 0.05f, 0.25f, 0.55f, Mathf.PI * 0.08f, Mathf.PI * 0.95f, 0.1f), Circle(p, 0.08f, 0.1f, 0.42f)));
                case 9:  // Eco del Rito: sfera armillare
                    return U(Ring(p, 0, 0, 0.62f, 0.05f), Sub(Ellipse(p, 0, 0, 0.66f, 0.24f), Ellipse(p, 0, 0, 0.56f, 0.15f)),
                             Sub(Ellipse(Rot(p, 60f), 0, 0, 0.66f, 0.24f), Ellipse(Rot(p, 60f), 0, 0, 0.56f, 0.15f)), Circle(p, 0, 0, 0.13f), Seg(p, 0f, -0.65f, 0f, -0.9f, 0.05f), Box(p, 0f, -0.9f, 0.3f, 0.05f));
                case 10: // Pelle di Runa: runa a rombo
                    return U(Sub(Poly(p, 0f, 0.8f, 0.55f, 0f, 0f, -0.8f, -0.55f, 0f), Poly(p, 0f, 0.62f, 0.4f, 0f, 0f, -0.62f, -0.4f, 0f)), Seg(p, 0f, 0.62f, 0f, -0.62f, 0.05f), Seg(p, -0.3f, 0.15f, 0.3f, -0.15f, 0.05f));
                case 11: // Pozzo Profondo: cristallo
                    return Sub(Poly(p, 0f, 0.85f, 0.38f, 0.35f, 0.3f, -0.6f, 0f, -0.85f, -0.3f, -0.6f, -0.38f, 0.35f), U(Seg(p, 0f, 0.85f, 0f, -0.85f, 0.03f), Seg(p, -0.38f, 0.35f, 0.38f, 0.35f, 0.03f)));
                case 12: // Sorgente Nera: ampolla
                {
                    float flask = U(Circle(p, 0f, -0.3f, 0.48f), Box(p, 0f, 0.35f, 0.13f, 0.3f), Box(p, 0f, 0.68f, 0.2f, 0.06f));
                    return Sub(flask, Sub(Circle(p, 0f, -0.3f, 0.36f), Box(p, 0f, 0f, 0.6f, 0.24f)));
                }
                // ---- Stregoneria, interni e supremo
                case 20: return Flame(p);
                case 21: return Bolt(p);
                case 22: return Snow(p);
                case 23: return U(Sub(Drop(p, 0.75f), Drop(p - new Vector2(0.08f, -0.05f), 0.32f)), Ring(p, 0f, -0.15f, 0.74f, 0.045f));
                case 24: // Passo d'Ombra: ala
                {
                    float wing = Poly(p, -0.75f, -0.3f, -0.4f, 0.55f, 0.15f, 0.8f, 0.75f, 0.55f, 0.35f, 0.3f, 0.55f, 0.05f, 0.2f, -0.05f, 0.35f, -0.35f, -0.05f, -0.3f);
                    return Sub(wing, U(Seg(p, -0.3f, 0.2f, 0.4f, 0.55f, 0.025f), Seg(p, -0.3f, 0.05f, 0.25f, 0.15f, 0.025f)));
                }
                case 25: return U(Circle(p, -0.55f, 0f, 0.13f), Arc(p, -0.55f, 0f, 0.42f, -0.9f, 0.9f, 0.06f), Arc(p, -0.55f, 0f, 0.72f, -0.75f, 0.75f, 0.06f), Arc(p, -0.55f, 0f, 1.02f, -0.6f, 0.6f, 0.06f));
                case 26: // Essenza Maledetta: testa di caprone con le corna
                {
                    float head = Poly(p, -0.32f, 0.3f, 0.32f, 0.3f, 0.2f, -0.45f, 0f, -0.75f, -0.2f, -0.45f);
                    float horns = U(Arc(p, -0.45f, 0.42f, 0.28f, -0.3f, Mathf.PI * 1.1f, 0.09f), Arc(p, 0.45f, 0.42f, 0.28f, -Mathf.PI * 0.1f, Mathf.PI * 1.3f, 0.09f));
                    float eyes = U(Poly(p, -0.24f, 0.08f, -0.06f, 0.0f, -0.2f, -0.06f), Poly(p, 0.24f, 0.08f, 0.06f, 0.0f, 0.2f, -0.06f));
                    return U(Sub(head, eyes), horns, Sub(Ring(p, 0f, -0.05f, 0.88f, 0.03f), Box(p, 0f, 0.6f, 0.5f, 0.3f)));
                }
                // ---- Arsenale, esterni
                case 30: return Crosshair(p);
                case 31: return U(Skull(p, 0.62f), Sub(Ring(p, 0, 0, 0.82f, 0.05f), U(Box(p, 0f, 0f, 0.08f, 1f), Box(p, 0f, 0f, 1f, 0.08f))));
                case 32: return U(Bullet(p, 0.1f, 0.1f, 0.9f, -45f), Seg(p, -0.6f, 0.15f, -0.25f, 0.5f, 0.04f), Seg(p, -0.45f, -0.2f, -0.1f, 0.15f, 0.04f), Seg(p, -0.25f, -0.5f, 0.1f, -0.15f, 0.04f));
                case 33: // Forza d'Impatto: stella dell'esplosione
                {
                    var xy = new float[32];
                    for (int k = 0; k < 16; k++) { float a = k * Mathf.PI / 8f, r = k % 2 == 0 ? 0.85f : 0.35f; xy[k * 2] = Mathf.Cos(a) * r; xy[k * 2 + 1] = Mathf.Sin(a) * r; }
                    return Sub(Poly(p, xy), Circle(p, 0, 0, 0.16f));
                }
                case 34: return U(Bullet(p, -0.42f, 0f, 0.7f), Bullet(p, 0f, 0.1f, 0.7f), Bullet(p, 0.42f, 0f, 0.7f));
                case 35: return U(Circle(p, -0.1f, -0.15f, 0.52f), Box(p, 0.25f, 0.42f, 0.12f, 0.1f), Seg(p, 0.3f, 0.5f, 0.55f, 0.72f, 0.035f), Circle(p, 0.62f, 0.78f, 0.1f));
                case 36: return U(Sub(Bullet(p, 0f, 0f, 0.8f), Bullet(p, 0f, 0f, 0.55f)), Arc(p, 0f, 0f, 0.78f, 0.3f, Mathf.PI * 1.6f, 0.05f), Poly(p, 0.62f, 0.62f, 0.88f, 0.38f, 0.5f, 0.3f));
                case 37: return U(Sub(Poly(p, -0.35f, 0.7f, 0.1f, 0.7f, 0.25f, -0.7f, -0.2f, -0.7f), Poly(p, -0.24f, 0.58f, 0.0f, 0.58f, 0.13f, -0.58f, -0.11f, -0.58f)), Box(p, 0.58f, 0.45f, 0.24f, 0.06f), Box(p, 0.58f, 0.45f, 0.06f, 0.24f));
                case 38: // Maneggevolezza: pistola
                    return U(Box(p, -0.05f, 0.25f, 0.62f, 0.15f, 0.04f), Poly(p, -0.38f, 0.12f, -0.08f, 0.12f, -0.2f, -0.7f, -0.55f, -0.7f), Sub(Ring(p, 0.06f, -0.05f, 0.14f, 0.035f), Box(p, 0.06f, 0.1f, 0.2f, 0.1f)));
                case 39: return U(Bullet(p, 0f, 0f, 0.62f), Arc(p, 0f, 0f, 0.75f, 0.4f, 2.7f, 0.06f), Arc(p, 0f, 0f, 0.75f, 3.54f, 5.84f, 0.06f), Poly(p, -0.62f, 0.62f, -0.82f, 0.25f, -0.4f, 0.35f), Poly(p, 0.62f, -0.62f, 0.82f, -0.25f, 0.4f, -0.35f));
                case 40: // Stabilita' Oscura: fucile con le frecce
                    return U(Box(p, 0.05f, 0.0f, 0.75f, 0.08f), Poly(p, -0.7f, 0.08f, -0.35f, 0.08f, -0.42f, -0.3f, -0.75f, -0.3f), Box(p, 0.1f, -0.15f, 0.06f, 0.15f),
                             Seg(p, 0.3f, 0.25f, 0.3f, 0.65f, 0.04f), Poly(p, 0.15f, 0.55f, 0.45f, 0.55f, 0.3f, 0.8f), Seg(p, 0.3f, -0.25f, 0.3f, -0.6f, 0.04f), Poly(p, 0.15f, -0.5f, 0.45f, -0.5f, 0.3f, -0.75f));
                case 41: return U(Hourglass(p * 1.1f) / 1.1f, Bullet(p, 0f, -0.42f, 0.32f));
                // ---- Arsenale, interni e supremo
                case 50: return U(Skull(p, 0.82f), Seg(p, -0.75f, -0.75f, -0.45f, -0.45f, 0.05f), Seg(p, 0.75f, -0.75f, 0.45f, -0.45f, 0.05f));
                case 51: return U(Drop(p, 0.9f), Drop(p - new Vector2(0.55f, -0.5f), 0.32f), Drop(p - new Vector2(-0.55f, -0.55f), 0.26f));
                case 52: return U(Flame(p, 0.85f), Bullet(p, 0f, -0.55f, 0.32f));
                case 53: return U(Skull(p - new Vector2(0f, 0.15f), 0.68f), Drop(p - new Vector2(-0.3f, -0.62f), 0.2f), Drop(p - new Vector2(0.25f, -0.7f), 0.22f));
                case 54: return U(Bolt(p - new Vector2(-0.28f, 0f), 0.78f), Bolt(p - new Vector2(0.3f, 0.05f), 0.78f));
                case 55: return Snow(p * 1.05f) / 1.05f;
                case 56: // Arma Maledetta: corona
                    return U(Poly(p, -0.7f, -0.35f, -0.75f, 0.45f, -0.38f, 0.1f, 0f, 0.6f, 0.38f, 0.1f, 0.75f, 0.45f, 0.7f, -0.35f), Box(p, 0f, -0.5f, 0.72f, 0.1f),
                             Circle(p, -0.75f, 0.52f, 0.09f), Circle(p, 0f, 0.68f, 0.09f), Circle(p, 0.75f, 0.52f, 0.09f));
                default: return Circle(p, 0, 0, 0.5f);
            }
        }

        static Sprite Render(System.Func<Vector2, float> f, string name, int size = S)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            float pix = 2f / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f) * 1.08f;
                    float d = f(p);
                    float a = Mathf.Clamp01(0.5f - d / (pix * 1.08f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);          // resta leggibile: GrimoirePage stampa le icone sulle pagine del libro sull'altare
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>L'icona di un nodo: Grimoire.Node.icon.</summary>
        public static Sprite Get(int icon)
        {
            if (cache.TryGetValue(icon, out var s) && s != null) return s;
            s = Render(p => Shape(icon, p), "Icona" + icon);
            cache[icon] = s;
            return s;
        }

        /// <summary>Il medaglione a spine delle tavole: anello con dodici spine in fuori e un filo di spine in dentro.</summary>
        public static Sprite Medal()
        {
            if (medal != null) return medal;
            medal = Render(p =>
            {
                float ringD = Ring(p, 0, 0, 0.74f, 0.075f);
                float a = Mathf.Atan2(p.y, p.x), r = p.magnitude;
                float spikesOut = r - (0.80f + 0.20f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a * 12f)), 18f));
                float spikesIn = (0.64f - 0.10f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a * 12f + Mathf.PI / 12f * 12f)), 24f)) - r;
                float outer = Mathf.Max(spikesOut, 0.66f - r);
                return U(ringD, outer, Mathf.Max(spikesIn, r - 0.75f));
            }, "Medaglione", 160);
            return medal;
        }

        public static Sprite Disc()
        {
            if (disc != null) return disc;
            disc = Render(p => Circle(p, 0, 0, 0.7f), "Disco", 96);
            return disc;
        }

        /// <summary>Un anello sottile (alone del nodo scelto, cornice delle icone dell'HUD).</summary>
        public static Sprite Halo()
        {
            if (ring != null) return ring;
            ring = Render(p => Ring(p, 0, 0, 0.86f, 0.04f), "Alone", 128);
            return ring;
        }
    }
}
