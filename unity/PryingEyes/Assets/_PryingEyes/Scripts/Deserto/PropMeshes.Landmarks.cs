using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le cose grandi e fatte dall'uomo che stanno sulla sabbia fuori dai luoghi: la tenda, il fuoco con le pietre, le casse, il pozzo, l'obelisco, il mulino a vento
    /// (la torre; il rotore e' una mesh a parte che gira) e il carro rotto. Fatti coi pezzi piatti (Poly, Box, Frustum) e con quelli tondi di PropMeshes.cs.
    /// Le fiamme del fuoco e il bagliore della brace hanno luce propria (UV0.x, DesertProp.shader): di giorno il fuoco e' spento.</summary>
    public static partial class PropMeshes
    {
        // ------------------------------------------------------------------ pezzi piatti
        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        /// <summary>Un poligono convesso piatto che guarda dalla parte di 'nrm' (i triangoli si girano da soli).</summary>
        static void Poly(PM m, Vector3 nrm, Color col, params Vector3[] pts)
        {
            var idx = new int[pts.Length];
            for (int i = 0; i < pts.Length; i++) idx[i] = m.Vn(pts[i], nrm, col);
            bool flip = Vector3.Dot(Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]), nrm) < 0f;
            for (int i = 1; i < pts.Length - 1; i++)
            {
                if (flip) m.Tri(idx[0], idx[i + 1], idx[i]); else m.Tri(idx[0], idx[i], idx[i + 1]);
            }
        }

        /// <summary>Un poligono sottile (una vela, un'ala, una tela): si vede da tutte e due le parti, col colore 'under' sotto.</summary>
        static void Poly2(PM m, Vector3 nrm, Color top, Color under, params Vector3[] pts)
        {
            Poly(m, nrm, top, pts);
            Poly(m, -nrm, under, pts);
        }

        /// <summary>Un parallelepipedo con la base in 'foot', ruotato di yaw e inclinabile; facce piatte, senza fondo.</summary>
        static void Box(PM m, Vector3 foot, Vector3 size, float yaw, Color side, Color top, float tiltX = 0f, float tiltZ = 0f)
        {
            var q = Quaternion.Euler(tiltX, yaw, tiltZ);
            Vector3 P(float x, float y, float z) => foot + q * new Vector3(x, y, z);
            float hx = size.x * 0.5f, hz = size.z * 0.5f, h = size.y;
            Vector3 a = P(-hx, 0f, -hz), b = P(hx, 0f, -hz), c = P(hx, 0f, hz), d = P(-hx, 0f, hz);
            Vector3 e = P(-hx, h, -hz), f = P(hx, h, -hz), g = P(hx, h, hz), i = P(-hx, h, hz);
            Poly(m, q * Vector3.back, Shade(side, 1f), a, b, f, e);
            Poly(m, q * Vector3.right, Shade(side, 0.9f), b, c, g, f);
            Poly(m, q * Vector3.forward, Shade(side, 0.97f), c, d, i, g);
            Poly(m, q * Vector3.left, Shade(side, 0.84f), d, a, e, i);
            Poly(m, q * Vector3.up, top, e, f, g, i);
        }

        /// <summary>Un tronco di piramide a base quadrata (altezza h, lato w0 in basso e w1 in alto) a fasce di colore, con la punta se tip > 0.</summary>
        static void Frustum(PM m, Vector3 foot, float h, float w0, float w1, float yaw, int bands, Func<float, Color> colAtY, float tip = 0f)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + q * new Vector3(x, y, z);
            var cs = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            float slope = (w0 - w1) * 0.5f / h;
            for (int f = 0; f < 4; f++)
            {
                var ca = cs[f]; var cb = cs[(f + 1) % 4];
                var od = (ca + cb).normalized;
                var nrm = q * new Vector3(od.x, slope, od.y).normalized;
                for (int k = 0; k < bands; k++)
                {
                    float y0 = h * k / bands, y1 = h * (k + 1) / bands;
                    float h0 = Mathf.Lerp(w0, w1, y0 / h) * 0.5f, h1 = Mathf.Lerp(w0, w1, y1 / h) * 0.5f;
                    Poly(m, nrm, colAtY((k + 0.5f) / bands), P(ca.x * h0, y0, ca.y * h0), P(cb.x * h0, y0, cb.y * h0), P(cb.x * h1, y1, cb.y * h1), P(ca.x * h1, y1, ca.y * h1));
                }
            }
            if (tip > 0f)
            {
                float hw = w1 * 0.5f;
                var apex = P(0f, h + tip, 0f);
                for (int f = 0; f < 4; f++)
                {
                    var ca = cs[f]; var cb = cs[(f + 1) % 4];
                    var od = (ca + cb).normalized;
                    Poly(m, q * new Vector3(od.x * tip, hw, od.y * tip).normalized, colAtY(1f), P(ca.x * hw, h, ca.y * hw), P(cb.x * hw, h, cb.y * hw), apex);
                }
            }
        }

        // ------------------------------------------------------------------ la tenda
        static Mesh Tent()
        {
            var m = new PM(Surf.Organic);
            Color canvas = C(0.66f, 0.60f, 0.47f), canvasB = C(0.58f, 0.52f, 0.40f), door = C(0.10f, 0.08f, 0.06f), wood = C(0.30f, 0.22f, 0.14f), rope = C(0.40f, 0.34f, 0.24f);
            const float L = 1.6f, W = 1.15f, H = 1.55f;
            Poly(m, new Vector3(0f, W, -H), canvas, new Vector3(-L, 0f, -W), new Vector3(L, 0f, -W), new Vector3(L, H, 0f), new Vector3(-L, H, 0f));
            Poly(m, new Vector3(0f, W, H), canvasB, new Vector3(-L, 0f, W), new Vector3(L, 0f, W), new Vector3(L, H, 0f), new Vector3(-L, H, 0f));
            Poly(m, Vector3.right, canvasB, new Vector3(L, 0f, -W), new Vector3(L, 0f, W), new Vector3(L, H, 0f));
            Poly(m, Vector3.left, canvas, new Vector3(-L, 0f, -W), new Vector3(-L, 0f, W), new Vector3(-L, H, 0f));
            Poly(m, Vector3.left, door, new Vector3(-L - 0.02f, 0f, -0.62f), new Vector3(-L - 0.02f, 0f, 0.62f), new Vector3(-L - 0.02f, 1.12f, 0f));
            Func<int, int, Color> w = (i, j) => wood;
            Tube(m, new[] { new Vector3(-L - 0.06f, 0f, 0f), new Vector3(-L - 0.06f, H + 0.28f, 0f) }, new[] { 0.035f, 0.025f }, 5, w, 0f, 0f, true);
            Tube(m, new[] { new Vector3(L + 0.06f, 0f, 0f), new Vector3(L + 0.06f, H + 0.28f, 0f) }, new[] { 0.035f, 0.025f }, 5, w, 0f, 0f, true);
            Tube(m, new[] { new Vector3(-L - 0.06f, H + 0.22f, 0f), new Vector3(L + 0.06f, H + 0.22f, 0f) }, new[] { 0.012f, 0.012f }, 4, (i, j) => rope);
            Blob(m, new Vector3(-L - 0.5f, 0.12f, 0.9f), new Vector3(0.45f, 0.11f, 0.13f), 111, 0.1f, C(0.30f, 0.14f, 0.12f), C(0.52f, 0.26f, 0.20f), 8, 4, 0.5f);
            return m.Done("Tent");
        }

        // ------------------------------------------------------------------ il fuoco
        static Mesh FirePit()
        {
            var m = new PM();
            Color dark = C(0.26f, 0.24f, 0.22f), light = C(0.58f, 0.54f, 0.48f);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f + 0.2f;
                Blob(m, new Vector3(Mathf.Cos(a) * 0.68f, 0.10f, Mathf.Sin(a) * 0.68f), new Vector3(0.21f, 0.15f, 0.18f), 80 + i, 0.2f, dark, light, 6, 4, 0.5f);
            }
            Blob(m, new Vector3(0f, 0.04f, 0f), new Vector3(0.58f, 0.06f, 0.58f), 90, 0.05f, C(0.08f, 0.07f, 0.07f), C(0.17f, 0.15f, 0.14f), 10, 3, 0.5f);
            Func<int, int, Color> bark = (i, j) => (j % 2 == 0) ? C(0.20f, 0.14f, 0.10f) : C(0.34f, 0.25f, 0.17f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.571f + 0.5f; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Tube(m, new[] { d * 0.55f + Vector3.up * 0.07f, d * 0.22f + Vector3.up * 0.26f, -d * 0.05f + Vector3.up * 0.34f }, new[] { 0.07f, 0.066f, 0.055f }, 5, bark, 0f, 0f, true);
            }
            m.emit = 0.45f;                                                         // la brace brilla anche di giorno
            Blob(m, new Vector3(0f, 0.17f, 0f), new Vector3(0.30f, 0.12f, 0.30f), 91, 0.3f, C(0.80f, 0.20f, 0.03f), C(1.0f, 0.46f, 0.10f), 8, 4, 0.5f);
            m.emit = 0.9f;                                                          // le fiamme (si accendono con la notte)
            Func<int, int, Color> flame = (i, j) => Color.Lerp(C(1.0f, 0.28f, 0.03f), C(1.0f, 0.80f, 0.28f), i / 3f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.571f + 0.9f; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.13f;
                float hh = 0.62f + 0.28f * ((i * 7) % 3) / 2f;
                Tube(m, new[] { d + Vector3.up * 0.16f, d * 0.7f + Vector3.up * (0.16f + hh * 0.4f), d * 0.3f + Vector3.up * (0.16f + hh * 0.75f), Vector3.up * (0.16f + hh) },
                     new[] { 0.19f, 0.15f, 0.08f, 0.01f }, 6, flame);
            }
            m.emit = 0f;
            return m.Done("FirePit");
        }

        // ------------------------------------------------------------------ casse e sacchi
        static Mesh Crate()
        {
            var m = new PM(Surf.Organic);
            Color wood = C(0.42f, 0.30f, 0.18f), woodTop = C(0.52f, 0.38f, 0.23f), woodB = C(0.34f, 0.24f, 0.15f), woodTopB = C(0.44f, 0.32f, 0.20f);
            Box(m, new Vector3(0f, -0.04f, 0f), new Vector3(0.95f, 0.84f, 0.95f), 12f, wood, woodTop);
            Box(m, new Vector3(0.08f, 0.8f, 0.04f), new Vector3(0.8f, 0.66f, 0.8f), -22f, woodB, woodTopB);
            Box(m, new Vector3(1.0f, -0.04f, 0.55f), new Vector3(0.72f, 0.56f, 0.72f), 41f, wood, woodTop);
            Blob(m, new Vector3(-0.95f, 0.26f, -0.25f), new Vector3(0.40f, 0.30f, 0.33f), 120, 0.15f, C(0.45f, 0.38f, 0.26f), C(0.68f, 0.60f, 0.42f), 9, 5, 0.3f);
            Blob(m, new Vector3(-0.7f, 0.2f, 0.55f), new Vector3(0.34f, 0.22f, 0.3f), 121, 0.15f, C(0.42f, 0.35f, 0.24f), C(0.64f, 0.56f, 0.40f), 9, 5, 0.3f);
            return m.Done("Crate");
        }

        // ------------------------------------------------------------------ il pozzo
        static Mesh Well()
        {
            var m = new PM(Surf.Stone);
            Func<int, int, Color> stone = (i, j) => ((i + j) % 2 == 0) ? C(0.40f, 0.37f, 0.32f) : C(0.54f, 0.50f, 0.44f);
            Tube(m, new[] { new Vector3(0f, -0.1f, 0f), new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0.6f, 0f), new Vector3(0f, 0.95f, 0f) }, new[] { 1.0f, 1.0f, 0.98f, 0.96f }, 14, stone);
            const int segs = 14;
            Vector3 Pp(float a, float r, float y) => new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
            var disc = new Vector3[segs];
            for (int j = 0; j < segs; j++)
            {
                float a0 = j * Mathf.PI * 2f / segs, a1 = (j + 1) * Mathf.PI * 2f / segs, am = (a0 + a1) * 0.5f;
                Poly(m, Vector3.up, C(0.56f, 0.52f, 0.46f), Pp(a0, 0.96f, 0.95f), Pp(a1, 0.96f, 0.95f), Pp(a1, 0.62f, 0.95f), Pp(a0, 0.62f, 0.95f));
                Poly(m, new Vector3(-Mathf.Sin(am), 0f, -Mathf.Cos(am)), C(0.22f, 0.20f, 0.18f), Pp(a0, 0.62f, 0.95f), Pp(a1, 0.62f, 0.95f), Pp(a1, 0.62f, 0.5f), Pp(a0, 0.62f, 0.5f));
                disc[j] = Pp(a0, 0.62f, 0.5f);
            }
            Poly(m, Vector3.up, C(0.02f, 0.02f, 0.03f), disc);
            Color wood = C(0.30f, 0.22f, 0.14f);
            Func<int, int, Color> w = (i, j) => wood;
            Tube(m, new[] { new Vector3(-0.85f, 0.9f, 0f), new Vector3(-0.85f, 2.0f, 0f) }, new[] { 0.07f, 0.06f }, 5, w, 0f, 0f, true);
            Tube(m, new[] { new Vector3(0.85f, 0.9f, 0f), new Vector3(0.85f, 2.0f, 0f) }, new[] { 0.07f, 0.06f }, 5, w, 0f, 0f, true);
            Tube(m, new[] { new Vector3(-0.98f, 1.95f, 0f), new Vector3(0.98f, 1.95f, 0f) }, new[] { 0.06f, 0.06f }, 5, w, 0f, 0f, true);
            Tube(m, new[] { new Vector3(0f, 1.9f, 0f), new Vector3(0f, 1.1f, 0f) }, new[] { 0.012f, 0.012f }, 3, (i, j) => C(0.42f, 0.36f, 0.26f));
            Blob(m, new Vector3(0f, 1.0f, 0f), new Vector3(0.17f, 0.14f, 0.17f), 131, 0.1f, C(0.20f, 0.15f, 0.10f), C(0.36f, 0.27f, 0.18f), 8, 4, 0.2f);
            return m.Done("Well");
        }

        // ------------------------------------------------------------------ l'obelisco
        static Mesh Obelisk()
        {
            var m = new PM(Surf.Stone);
            Func<float, Color> plinth = y => C(0.42f, 0.39f, 0.34f);
            Frustum(m, new Vector3(0f, -0.2f, 0f), 0.75f, 2.4f, 1.9f, 6f, 1, plinth);
            Frustum(m, new Vector3(0f, 0.5f, 0f), 0.5f, 1.8f, 1.5f, 6f, 1, plinth);
            Func<float, Color> carved = y => (((int)(y * 40f)) % 3 == 0 && y > 0.12f && y < 0.9f) ? C(0.38f, 0.34f, 0.28f) : C(0.60f, 0.56f, 0.47f);
            Frustum(m, new Vector3(0f, 0.95f, 0f), 7.6f, 1.2f, 0.62f, 6f, 40, carved, 0.9f);
            return m.Done("Obelisk");
        }

        // ------------------------------------------------------------------ il mulino a vento
        /// <summary>Dove il rotore e' fissato, nel sistema della torre (m): in alto, sul lato +z.</summary>
        public static readonly Vector3 WindmillHub = new Vector3(0f, 7.6f, 2.35f);

        static Mesh Windmill()
        {
            var m = new PM(Surf.Stone);
            Func<int, int, Color> stone = (i, j) => (((i % 2) + (j % 2)) % 2 == 0) ? C(0.46f, 0.42f, 0.35f) : C(0.57f, 0.53f, 0.45f);
            float[] ys = { -0.15f, 1.6f, 3.4f, 5.2f, 7.0f, 8.4f };
            float[] rs = { 2.7f, 2.52f, 2.3f, 2.08f, 1.86f, 1.74f };
            var path = new Vector3[ys.Length];
            for (int i = 0; i < ys.Length; i++) path[i] = new Vector3(0f, ys[i], 0f);
            Tube(m, path, rs, 16, stone);
            Tube(m, new[] { new Vector3(0f, 8.35f, 0f), new Vector3(0f, 9.0f, 0f), new Vector3(0f, 9.9f, 0f), new Vector3(0.25f, 10.5f, 0.1f) }, new[] { 1.95f, 1.55f, 0.8f, 0.05f }, 16,
                 (i, j) => (j % 2 == 0) ? C(0.30f, 0.15f, 0.11f) : C(0.40f, 0.21f, 0.15f), 0f, 0f, true);
            float RAt(float y) => 2.7f - (y + 0.15f) * (2.7f - 1.74f) / 8.55f;
            Color dark = C(0.03f, 0.025f, 0.02f);
            float zc0 = -(RAt(0f) + 0.03f), zc1 = -(RAt(2.0f) + 0.03f), ze0 = -(RAt(0f) - 0.11f), ze1 = -(RAt(2.0f) - 0.11f);
            Poly(m, Vector3.back, dark, new Vector3(-0.55f, 0f, ze0), new Vector3(0f, 0f, zc0), new Vector3(0f, 2.0f, zc1), new Vector3(-0.55f, 2.0f, ze1));
            Poly(m, Vector3.back, dark, new Vector3(0f, 0f, zc0), new Vector3(0.55f, 0f, ze0), new Vector3(0.55f, 2.0f, ze1), new Vector3(0f, 2.0f, zc1));
            // due feritoie
            for (int k = 0; k < 2; k++)
            {
                float y = 3.4f + k * 2.6f, x = k == 0 ? 0.9f : -0.9f, r = RAt(y) * 0.93f;
                float z = -Mathf.Sqrt(Mathf.Max(0.1f, r * r - x * x));
                Poly(m, Vector3.back, dark, new Vector3(x - 0.12f, y, z - 0.03f), new Vector3(x + 0.12f, y, z - 0.03f), new Vector3(x + 0.12f, y + 0.55f, z - 0.03f), new Vector3(x - 0.12f, y + 0.55f, z - 0.03f));
            }
            Tube(m, new[] { new Vector3(0f, WindmillHub.y, 0.3f), new Vector3(0f, WindmillHub.y, WindmillHub.z + 0.1f) }, new[] { 0.16f, 0.14f }, 6, (i, j) => C(0.28f, 0.20f, 0.13f), 0f, 0f, true);
            return m.Done("Windmill");
        }

        static Mesh rotor;

        /// <summary>Le quattro pale a graticcio del mulino (una rotta), con il mozzo in (0,0,0) e il piano nell'XY locale: gira attorno a z.</summary>
        public static Mesh Rotor()
        {
            if (rotor != null) return rotor;
            var m = new PM(Surf.Organic);
            Color wood = C(0.30f, 0.22f, 0.15f), cloth = C(0.64f, 0.57f, 0.44f), clothB = C(0.52f, 0.45f, 0.34f);
            Blob(m, Vector3.zero, new Vector3(0.34f, 0.34f, 0.3f), 130, 0.1f, C(0.20f, 0.15f, 0.10f), wood, 8, 5, 1f);
            float[] len = { 6.2f, 6.2f, 3.6f, 5.4f };
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.35f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f); var side = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f);
                Tube(m, new[] { d * 0.25f, d * (len[i] * 0.5f), d * len[i] }, new[] { 0.07f, 0.06f, 0.04f }, 4, (k, j) => wood);
                for (int k = 0; k < 4; k++)
                {
                    float r0 = 1.0f + k * 1.25f, r1 = r0 + 1.05f;
                    if (r1 > len[i] - 0.1f) break;
                    if (i == 3 && k == 2) continue;                              // una vela strappata
                    var col = (k % 2 == 0) ? cloth : clothB;
                    Poly2(m, Vector3.forward, col, Shade(col, 0.85f), d * r0 + side * 0.06f, d * r1 + side * 0.06f, d * r1 + side * 1.31f, d * r0 + side * 1.31f);
                }
            }
            rotor = m.Done("WindmillRotor");
            return rotor;
        }

        // ------------------------------------------------------------------ il carro rotto
        static void Wheel(PM m, Vector3 c, float r, bool flat)
        {
            Color iron = C(0.22f, 0.20f, 0.18f), wood = C(0.34f, 0.25f, 0.16f);
            const int n = 14;
            var rim = new Vector3[n + 1]; var radii = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                rim[i] = c + (flat ? new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r) : new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
                radii[i] = 0.05f;
            }
            Tube(m, rim, radii, 5, (i, j) => (j % 2 == 0) ? iron : wood);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f + 0.3f;
                var dir = flat ? new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) : new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Tube(m, new[] { c, c + dir * r }, new[] { 0.032f, 0.026f }, 4, (i, j) => wood);
            }
            Blob(m, c, new Vector3(0.09f, 0.09f, 0.09f), 140, 0.05f, iron, wood, 6, 4, 1f);
        }

        static Mesh Wagon()
        {
            var m = new PM(Surf.Organic);
            Color wood = C(0.34f, 0.25f, 0.16f), woodB = C(0.26f, 0.19f, 0.12f);
            Box(m, new Vector3(0f, 0.5f, 0f), new Vector3(3.0f, 0.14f, 1.3f), 0f, woodB, wood);
            Box(m, new Vector3(0f, 0.64f, -0.62f), new Vector3(3.0f, 0.5f, 0.08f), 0f, wood, wood);
            Box(m, new Vector3(0f, 0.64f, 0.62f), new Vector3(3.0f, 0.36f, 0.08f), 0f, wood, wood);
            Box(m, new Vector3(1.46f, 0.64f, 0f), new Vector3(0.08f, 0.5f, 1.3f), 0f, wood, wood);
            for (int k = 0; k < 4; k++)
            {
                float x = -1.1f + k * 0.75f;
                var pts = new Vector3[7]; var rad = new float[7];
                for (int i = 0; i < 7; i++) { float a = Mathf.PI * i / 6f; pts[i] = new Vector3(x, 0.7f + Mathf.Sin(a) * 1.0f, -0.6f * Mathf.Cos(a)); rad[i] = 0.025f; }
                Tube(m, pts, rad, 4, (i, j) => woodB);
            }
            Wheel(m, new Vector3(-0.9f, 0.5f, -0.72f), 0.52f, false);
            Wheel(m, new Vector3(0.95f, 0.5f, -0.72f), 0.52f, false);
            Wheel(m, new Vector3(-0.9f, 0.5f, 0.72f), 0.52f, false);
            Wheel(m, new Vector3(1.9f, 0.07f, 1.6f), 0.5f, true);
            Func<int, int, Color> w = (i, j) => wood;
            Tube(m, new[] { new Vector3(1.4f, 0.55f, -0.4f), new Vector3(2.4f, 0.25f, -0.55f), new Vector3(3.5f, 0.06f, -0.7f) }, new[] { 0.05f, 0.045f, 0.04f }, 5, w, 0f, 0f, true);
            Tube(m, new[] { new Vector3(1.4f, 0.55f, 0.4f), new Vector3(2.4f, 0.25f, 0.5f), new Vector3(3.3f, 0.06f, 0.6f) }, new[] { 0.05f, 0.045f, 0.04f }, 5, w, 0f, 0f, true);
            return m.Done("Wagon");
        }
    }
}
