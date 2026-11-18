using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le parti mobili della vita del deserto (DesertLife): lo sciacallo del cielo (avvoltoio) e il pipistrello, ciascuno in corpo e due ali (le ali battono, quindi sono
    /// pezzi a parte che il codice ruota), la lucertola (corpo e coda), lo scorpione, lo scarabeo, la sterpaglia che rotola. Tutti guardano verso +z locale, le ali stanno sull'asse x.</summary>
    public enum LifeMesh { BirdBody, BirdWingL, BirdWingR, BatBody, BatWingL, BatWingR, LizardBody, LizardTail, Scorpion, Beetle, Tumbleweed, Count }

    public static partial class PropMeshes
    {
        static Mesh[] lifeMeshes;

        public static Mesh Life(LifeMesh k)
        {
            if (lifeMeshes == null) lifeMeshes = new Mesh[(int)LifeMesh.Count];
            return lifeMeshes[(int)k] ??= BuildLife(k);
        }

        static void ClearExtra()
        {
            if (lifeMeshes != null) foreach (var m in lifeMeshes) if (m != null) UnityEngine.Object.Destroy(m);
            lifeMeshes = null;
            if (rotor != null) UnityEngine.Object.Destroy(rotor);
            rotor = null;
        }

        static Mesh BuildLife(LifeMesh k)
        {
            switch (k)
            {
                case LifeMesh.BirdBody: return BirdBody();
                case LifeMesh.BirdWingL: return BirdWing(-1f);
                case LifeMesh.BirdWingR: return BirdWing(1f);
                case LifeMesh.BatBody: return BatBody();
                case LifeMesh.BatWingL: return BatWing(-1f);
                case LifeMesh.BatWingR: return BatWing(1f);
                case LifeMesh.LizardBody: return LizardBody();
                case LifeMesh.LizardTail: return LizardTail();
                case LifeMesh.Scorpion: return Scorpion();
                case LifeMesh.Beetle: return Beetle();
                default: return Tumbleweed();
            }
        }

        // ------------------------------------------------------------------ l'avvoltoio
        static Mesh BirdBody()
        {
            var m = new PM();
            Color dark = C(0.10f, 0.08f, 0.06f), mid = C(0.22f, 0.17f, 0.12f);
            Blob(m, new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.13f, 0.50f), 150, 0.06f, dark, mid, 9, 6, 1f);
            Blob(m, new Vector3(0f, 0.01f, 0.40f), new Vector3(0.14f, 0.12f, 0.12f), 151, 0.05f, C(0.50f, 0.46f, 0.38f), C(0.74f, 0.70f, 0.60f), 8, 5, 1f);     // la gorgiera chiara
            Blob(m, new Vector3(0f, 0.05f, 0.57f), new Vector3(0.075f, 0.075f, 0.11f), 152, 0.05f, C(0.50f, 0.34f, 0.30f), C(0.70f, 0.52f, 0.46f), 8, 5, 1f);      // la testa spelata
            Tube(m, new[] { new Vector3(0f, 0.045f, 0.66f), new Vector3(0f, 0.02f, 0.78f) }, new[] { 0.035f, 0.006f }, 4, (i, j) => C(0.62f, 0.56f, 0.40f));
            Poly2(m, Vector3.up, dark, mid, new Vector3(-0.10f, 0f, -0.40f), new Vector3(0.10f, 0f, -0.40f), new Vector3(0.22f, 0f, -0.98f), new Vector3(-0.22f, 0f, -0.98f));
            return m.Done("BirdBody");
        }

        /// <summary>Un'ala: parte dalla spalla (origine) e va verso +x (o -x se side e' -1); piatta, scura sopra e piu' chiara sotto.</summary>
        static Mesh BirdWing(float side)
        {
            var m = new PM();
            Color top = C(0.12f, 0.09f, 0.07f), under = C(0.34f, 0.28f, 0.22f);
            var p = new[]
            {
                new Vector3(0.10f, 0f, 0.30f), new Vector3(0.70f, 0.02f, 0.34f), new Vector3(1.20f, 0.03f, 0.22f), new Vector3(1.48f, 0.03f, 0.04f),
                new Vector3(1.32f, 0.02f, -0.20f), new Vector3(1.05f, 0.01f, -0.40f), new Vector3(0.70f, 0f, -0.44f), new Vector3(0.10f, 0f, -0.38f)
            };
            for (int i = 0; i < p.Length; i++) p[i].x *= side;
            Poly2(m, Vector3.up, top, under, p);
            return m.Done("BirdWing");
        }

        // ------------------------------------------------------------------ il pipistrello
        static Mesh BatBody()
        {
            var m = new PM();
            Color dark = C(0.05f, 0.045f, 0.045f), mid = C(0.12f, 0.10f, 0.10f);
            Blob(m, Vector3.zero, new Vector3(0.07f, 0.06f, 0.13f), 160, 0.05f, dark, mid, 8, 5, 1f);
            Blob(m, new Vector3(0f, 0.02f, 0.15f), new Vector3(0.05f, 0.045f, 0.055f), 161, 0.05f, dark, mid, 7, 4, 1f);
            Poly(m, Vector3.back, mid, new Vector3(-0.035f, 0.06f, 0.15f), new Vector3(-0.02f, 0.13f, 0.14f), new Vector3(-0.005f, 0.06f, 0.15f));       // le orecchie
            Poly(m, Vector3.back, mid, new Vector3(0.005f, 0.06f, 0.15f), new Vector3(0.02f, 0.13f, 0.14f), new Vector3(0.035f, 0.06f, 0.15f));
            return m.Done("BatBody");
        }

        static Mesh BatWing(float side)
        {
            var m = new PM();
            Color top = C(0.08f, 0.065f, 0.065f), under = C(0.16f, 0.12f, 0.12f);
            var p = new[]
            {
                new Vector3(0.04f, 0f, 0.08f), new Vector3(0.30f, 0.02f, 0.16f), new Vector3(0.56f, 0.04f, 0.30f), new Vector3(0.66f, 0.03f, 0.10f),
                new Vector3(0.50f, 0.02f, -0.03f), new Vector3(0.42f, 0.01f, -0.16f), new Vector3(0.30f, 0f, -0.05f), new Vector3(0.20f, 0f, -0.18f), new Vector3(0.05f, 0f, -0.14f)
            };
            for (int i = 0; i < p.Length; i++) p[i].x *= side;
            Poly2(m, Vector3.up, top, under, p);
            return m.Done("BatWing");
        }

        // ------------------------------------------------------------------ la lucertola, lo scorpione, lo scarabeo
        static Mesh LizardBody()
        {
            var m = new PM();
            Color dark = C(0.12f, 0.10f, 0.06f), light = C(0.36f, 0.30f, 0.18f);
            Blob(m, new Vector3(0f, 0.05f, 0f), new Vector3(0.07f, 0.04f, 0.15f), 170, 0.08f, dark, light, 8, 5, 0.4f);
            Blob(m, new Vector3(0f, 0.05f, 0.2f), new Vector3(0.05f, 0.035f, 0.07f), 171, 0.05f, dark, light, 8, 5, 0.4f);
            Func<int, int, Color> leg = (i, j) => C(0.22f, 0.18f, 0.10f);
            foreach (float z in new[] { 0.10f, -0.08f })
                foreach (float s in new[] { -1f, 1f })
                    Tube(m, new[] { new Vector3(s * 0.06f, 0.04f, z), new Vector3(s * 0.13f, 0.025f, z + 0.03f), new Vector3(s * 0.16f, 0.0f, z + 0.08f) }, new[] { 0.014f, 0.011f, 0.008f }, 4, leg);
            return m.Done("LizardBody");
        }

        /// <summary>La coda: parte da (0,0,0) e va verso -z (la lucertola la aggancia dietro al corpo e la fa ondeggiare).</summary>
        static Mesh LizardTail()
        {
            var m = new PM();
            Tube(m, new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, -0.004f, -0.12f), new Vector3(0f, -0.01f, -0.26f), new Vector3(0f, -0.014f, -0.42f) }, new[] { 0.035f, 0.025f, 0.014f, 0.003f }, 5,
                 (i, j) => (i % 2 == 0) ? C(0.14f, 0.12f, 0.07f) : C(0.34f, 0.28f, 0.16f), 0f, 0f, true);
            return m.Done("LizardTail");
        }

        static Mesh Scorpion()
        {
            var m = new PM();
            Color dark = C(0.10f, 0.06f, 0.03f), light = C(0.28f, 0.17f, 0.07f);
            Func<int, int, Color> seg = (i, j) => Color.Lerp(dark, light, 0.35f + 0.5f * (i % 2));
            Blob(m, new Vector3(0f, 0.035f, 0f), new Vector3(0.07f, 0.03f, 0.11f), 180, 0.08f, dark, light, 8, 5, 0.4f);
            Tube(m, new[] { new Vector3(0f, 0.04f, -0.09f), new Vector3(0f, 0.05f, -0.2f), new Vector3(0f, 0.14f, -0.27f), new Vector3(0f, 0.24f, -0.24f), new Vector3(0f, 0.30f, -0.14f), new Vector3(0f, 0.29f, -0.06f) },
                 new[] { 0.024f, 0.021f, 0.019f, 0.017f, 0.014f, 0.004f }, 5, seg, 0f, 0f, true);
            Blob(m, new Vector3(0f, 0.27f, -0.04f), new Vector3(0.018f, 0.02f, 0.03f), 181, 0.05f, C(0.1f, 0.05f, 0.03f), C(0.2f, 0.1f, 0.05f), 6, 4, 1f);
            foreach (float s in new[] { -1f, 1f })
            {
                Tube(m, new[] { new Vector3(s * 0.05f, 0.03f, 0.09f), new Vector3(s * 0.11f, 0.03f, 0.19f), new Vector3(s * 0.08f, 0.03f, 0.27f) }, new[] { 0.013f, 0.012f, 0.01f }, 4, seg);
                Blob(m, new Vector3(s * 0.075f, 0.03f, 0.31f), new Vector3(0.035f, 0.016f, 0.05f), 182 + (int)s, 0.06f, dark, light, 6, 4, 1f);
                for (int k = 0; k < 4; k++)
                {
                    float z = 0.04f - k * 0.04f;
                    Tube(m, new[] { new Vector3(s * 0.05f, 0.03f, z), new Vector3(s * 0.12f, 0.05f, z + 0.02f), new Vector3(s * 0.17f, 0f, z + 0.05f) }, new[] { 0.007f, 0.006f, 0.004f }, 3, seg);
                }
            }
            return m.Done("Scorpion");
        }

        static Mesh Beetle()
        {
            var m = new PM();
            Color dark = C(0.03f, 0.04f, 0.035f), light = C(0.14f, 0.19f, 0.15f);
            Blob(m, new Vector3(0f, 0.03f, -0.01f), new Vector3(0.05f, 0.035f, 0.065f), 190, 0.04f, dark, light, 8, 5, 0.4f);
            Blob(m, new Vector3(0f, 0.025f, 0.065f), new Vector3(0.03f, 0.022f, 0.025f), 191, 0.04f, dark, light, 6, 4, 0.4f);
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 3; k++)
                {
                    float z = 0.03f - k * 0.035f;
                    Tube(m, new[] { new Vector3(s * 0.04f, 0.02f, z), new Vector3(s * 0.08f, 0.03f, z + 0.01f), new Vector3(s * 0.1f, 0f, z + 0.03f) }, new[] { 0.005f, 0.004f, 0.003f }, 3, (i, j) => dark);
                }
            return m.Done("Beetle");
        }

        // ------------------------------------------------------------------ la sterpaglia
        static Mesh Tumbleweed()
        {
            var m = new PM(Surf.Organic); var rng = new System.Random(200);
            Func<int, int, Color> twig = (i, j) => Color.Lerp(C(0.46f, 0.37f, 0.22f), C(0.74f, 0.64f, 0.42f), ((i * 7 + j * 3) % 5) / 4f);
            for (int k = 0; k < 10; k++)
            {
                var axis = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var v = Vector3.Cross(axis, u);
                float r = 0.36f + (float)rng.NextDouble() * 0.14f;
                const int n = 12;
                var ring = new Vector3[n + 1]; var rad = new float[n + 1];
                for (int i = 0; i <= n; i++) { float a = i * Mathf.PI * 2f / n; ring[i] = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r * (0.92f + 0.16f * (float)rng.NextDouble()); rad[i] = 0.017f; }
                Tube(m, ring, rad, 4, twig);
            }
            for (int k = 0; k < 22; k++)
            {
                var d = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
                Tube(m, new[] { d * 0.1f, d * (0.22f + (float)rng.NextDouble() * 0.3f) }, new[] { 0.014f, 0.006f }, 3, twig);
            }
            return m.Done("Tumbleweed");
        }
    }
}
