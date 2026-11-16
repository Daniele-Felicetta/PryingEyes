using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Texture e materiali del Guardaroba fatti a mano a runtime (come GardenKit): il cielo notturno con la luna di sangue, la lava e il
    /// lago di lava che si vede sotto il vuoto. Una copia per sessione. La lava e' un materiale Unlit con un colore HDR: e' lei la sorgente
    /// di luce (le luci vere le mettono le ricette, vicino alle vasche), e il bloom fa il resto.</summary>
    static class GuardarobaKit
    {
        static Texture2D skyTex, lavaTex;
        static Material mSky, mLava;
        static readonly Color LavaHdr = new Color(2.6f, 2.1f, 1.9f, 1f);

        // ------------------------------------------------------------------ cielo
        /// <summary>Il cielo: una texture 2:1 (nadir in basso, zenit in alto) con l'orizzonte che brilla del rosso della lava, le stelle, una luna
        /// grande e rossa, nuvole e le sagome nere di creste e torri in fondo. Lo disegna lo shader PryingEyes/NightSky sulla cupola.</summary>
        public static Material Sky()
        {
            if (mSky != null) return mSky;
            const int W = 1024, Hh = 512;
            var rng = new Rng(4242);
            // le torri: azimut, semi-larghezza e altezza del fusto (gradi), altezza della guglia
            var towers = new List<(float az, float w, float h, float spire)>();
            for (int i = 0; i < 9; i++)
                towers.Add((rng.Range(0f, 360f), rng.Range(0.45f, 0.95f), rng.Range(4.5f, 9.5f), rng.Range(2.0f, 4.0f)));
            const float moonAz = 232f, moonEl = 27f, moonR = 3.7f;
            skyTex = GardenKit.Make("guardaroba_cielo", W, Hh, (x, y) =>
            {
                float u = x / (float)W, v = y / (float)Hh;
                float el = (v - 0.5f) * 180f, az = u * 360f;
                var zen = new Color(0.004f, 0.006f, 0.030f);
                var mid = new Color(0.030f, 0.013f, 0.050f);
                var hor = new Color(0.34f, 0.075f, 0.030f);
                Color c;
                if (el >= 0f)
                {
                    c = Color.Lerp(mid, zen, Mathf.Pow(Mathf.Clamp01(el / 90f), 0.55f));
                    c = Color.Lerp(c, hor, Mathf.Pow(1f - Mathf.Clamp01(el / 24f), 2.4f) * 0.9f);        // la luce della lava sull'orizzonte
                    // nuvole sparse
                    float n = GardenKit.Fbm(u, v, 6, 7);
                    float cl = Mathf.SmoothStep(0.50f, 0.74f, n) * Mathf.Clamp01((el - 3f) / 10f) * (1f - Mathf.Clamp01((el - 55f) / 30f));
                    c = Color.Lerp(c, new Color(0.075f, 0.030f, 0.050f), cl * 0.65f);
                    // stelle, piu' rade vicino all'orizzonte
                    float s = GardenKit.H(x, y, 91);
                    if (s > 0.9988f && el > 4f) c += Color.white * (0.30f + 0.7f * (s - 0.9988f) / 0.0012f) * Mathf.Clamp01((el - 4f) / 14f);
                    // la luna di sangue, con un alone
                    float sa = Mathf.Sin(el * Mathf.Deg2Rad) * Mathf.Sin(moonEl * Mathf.Deg2Rad) + Mathf.Cos(el * Mathf.Deg2Rad) * Mathf.Cos(moonEl * Mathf.Deg2Rad) * Mathf.Cos((az - moonAz) * Mathf.Deg2Rad);
                    float d = Mathf.Acos(Mathf.Clamp(sa, -1f, 1f)) * Mathf.Rad2Deg;
                    if (d < moonR)
                    {
                        float limb = 1f - 0.35f * Mathf.Pow(d / moonR, 3f);
                        float crater = 0.78f + 0.22f * GardenKit.Fbm(u * 2f, v * 2f, 12, 3);
                        c = new Color(0.95f, 0.26f, 0.15f) * limb * crater * 1.5f;
                    }
                    else if (d < moonR * 3.2f)
                        c += new Color(0.40f, 0.075f, 0.055f) * Mathf.Pow(1f - (d - moonR) / (moonR * 2.2f), 2f) * 0.8f;
                    // sagome: creste e torri contro il bagliore
                    float ridge = 0.8f + 4.2f * GardenKit.VN(az / 360f * 26f, 0.5f, 26, 3) + 1.5f * GardenKit.VN(az / 360f * 70f, 0.5f, 70, 5);
                    float top = ridge;
                    foreach (var t in towers)
                    {
                        float da = Mathf.Abs(Mathf.DeltaAngle(az, t.az));
                        if (da < t.w) top = Mathf.Max(top, el <= t.h ? t.h : t.h + (1f - da / t.w) * t.spire);
                    }
                    if (el < top)
                    {
                        float rim = Mathf.Clamp01(1f - (top - el) / 0.45f);
                        c = new Color(0.006f, 0.003f, 0.005f) + hor * rim * 0.55f;
                    }
                }
                else
                {
                    // sotto l'orizzonte: il rosso della lava che si spegne in basso (quasi sempre coperto dal lago)
                    c = Color.Lerp(hor * 0.85f, new Color(0.035f, 0.012f, 0.012f), Mathf.Pow(Mathf.Clamp01(-el / 90f), 0.4f));
                }
                c.a = 1f;
                return c;
            }, false);
            skyTex.wrapMode = TextureWrapMode.Repeat;
            skyTex.filterMode = FilterMode.Bilinear;
            var shader = Shader.Find("PryingEyes/NightSky");
            if (shader == null) { Debug.LogWarning("[PryingEyes] shader PryingEyes/NightSky mancante: cielo non disegnato"); return null; }
            mSky = new Material(shader) { name = "guardaroba_cielo" };
            mSky.SetTexture("_MainTex", skyTex);
            mSky.SetColor("_Tint", new Color(1.6f, 1.6f, 1.7f, 1f));
            return mSky;
        }

        /// <summary>La cupola intera: da -90 a +90 gradi, facce verso l'interno, con i limiti enormi (la segue la camera, vedi lo shader).</summary>
        public static Mesh SkyDome()
        {
            const int rings = 24, slices = 48;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float el = Mathf.Lerp(-90f, 90f, i / (float)rings) * Mathf.Deg2Rad;
                for (int j = 0; j <= slices; j++)
                {
                    float az = j * Mathf.PI * 2f / slices;
                    v.Add(new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el)) * 50f);
                    uv.Add(new Vector2(j / (float)slices, i / (float)rings));
                }
            }
            int row = slices + 1;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < slices; j++)
                {
                    int a = i * row + j, b = a + 1, c = a + row, d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            var m = new Mesh { name = "guardaroba_cupola" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 200000f);
            return m;
        }

        // ------------------------------------------------------------------ lava
        /// <summary>Lava: placche nere di crosta (celle di Worley) con le crepe incandescenti tra l'una e l'altra. Il colore HDR del materiale la
        /// fa brillare; LavaFlow la fa scorrere piano e le dona un respiro.</summary>
        public static Material Lava()
        {
            if (mLava != null) return mLava;
            const int S = 256, cells = 6;
            lavaTex = GardenKit.Make("guardaroba_lava", S, S, (x, y) =>
            {
                float fx = x / (float)S * cells, fy = y / (float)S * cells;
                int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
                float d1 = 9f, d2 = 9f;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int cx = ((ix + ox) % cells + cells) % cells, cy = ((iy + oy) % cells + cells) % cells;
                        float px = ix + ox + GardenKit.H(cx, cy, 31), py = iy + oy + GardenKit.H(cx, cy, 32);
                        float d = Mathf.Sqrt((fx - px) * (fx - px) + (fy - py) * (fy - py));
                        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                    }
                float n = GardenKit.Fbm(x / (float)S, y / (float)S, 6, 9);
                float crack = Mathf.Clamp01(1f - (d2 - d1) * 5.2f);
                crack = Mathf.Clamp01(crack * (0.70f + 0.60f * n));
                var crust = Color.Lerp(new Color(0.010f, 0.008f, 0.008f), new Color(0.060f, 0.028f, 0.018f), n);
                var hot = Color.Lerp(new Color(0.85f, 0.17f, 0.02f), new Color(1.0f, 0.66f, 0.14f), Mathf.Pow(crack, 2f));
                var c = Color.Lerp(crust, hot, Mathf.SmoothStep(0.08f, 0.85f, crack));
                // qualche macchia di magma anche sotto la crosta
                float spot = Mathf.SmoothStep(0.80f, 0.95f, GardenKit.Fbm(x / (float)S + 0.37f, y / (float)S + 0.11f, 4, 21));
                c = Color.Lerp(c, new Color(0.9f, 0.28f, 0.04f), spot * 0.6f);
                c.a = 1f;
                return c;
            });
            lavaTex.wrapMode = TextureWrapMode.Repeat;
            mLava = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "guardaroba_lava" };
            mLava.SetTexture("_BaseMap", lavaTex);
            mLava.SetColor("_BaseColor", LavaHdr);
            return mLava;
        }

        public static Color LavaBase => LavaHdr;
    }

    /// <summary>Fa scorrere la lava (lo scostamento della texture) e le da' un respiro lento di luminosita'. Uno per piano, sul materiale condiviso.</summary>
    public sealed class LavaFlow : MonoBehaviour
    {
        Material mat;
        Vector2 off;
        float t;

        void OnEnable()
        {
            mat = GuardarobaKit.Lava();
        }

        void Update()
        {
            if (mat == null) return;
            float dt = Time.deltaTime;
            t += dt;
            off += new Vector2(0.0105f, 0.0062f) * dt;
            mat.SetTextureOffset("_BaseMap", off);
            float k = 1f + 0.13f * Mathf.Sin(t * 1.3f) + 0.06f * Mathf.Sin(t * 3.7f + 1f);
            mat.SetColor("_BaseColor", GuardarobaKit.LavaBase * k);
        }

        void OnDestroy()
        {
            // il materiale e' condiviso con le vasche del prossimo piano: lo riporto com'era
            if (mat == null) return;
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
            mat.SetColor("_BaseColor", GuardarobaKit.LavaBase);
        }
    }
}
