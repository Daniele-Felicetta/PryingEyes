using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le mesh del lampione del sagrato del deserto (RoomKits.HubLampPostDeserto): il ferro, tornito e a tubi, e il vetro della lanterna. Due mesh sole, per tutti i lampioni: il ferro dal
    /// piede svasato alla lanterna (un fusto con due anelli e un collare, il piatto su cui sta la lanterna, sei montanti con una fascia a meta', il tetto a cono con il puntale), il vetro un prisma
    /// a sei facce che si allarga a mezz'altezza. Le quote sono dal suolo: la base di pietra arriva a 0,43 m.</summary>
    public static class DesertLamps
    {
        static Mesh iron, glass;

        public static Mesh IronMesh()
        {
            if (iron != null) return iron;
            var shaft = DesertLathe.Turn("LampioneFusto", new[]
            {
                new Vector2(0.000f, 0.42f), new Vector2(0.120f, 0.42f), new Vector2(0.120f, 0.47f), new Vector2(0.095f, 0.50f), new Vector2(0.075f, 0.62f), new Vector2(0.055f, 0.78f),     // il piede svasato
                new Vector2(0.055f, 1.05f), new Vector2(0.078f, 1.07f), new Vector2(0.078f, 1.11f), new Vector2(0.055f, 1.13f),                                                          // il primo anello
                new Vector2(0.050f, 1.50f), new Vector2(0.050f, 2.12f),
                new Vector2(0.072f, 2.14f), new Vector2(0.072f, 2.20f), new Vector2(0.050f, 2.22f),                                                                                      // il collare
                new Vector2(0.045f, 2.50f), new Vector2(0.045f, 2.88f),
                new Vector2(0.090f, 2.90f), new Vector2(0.170f, 2.93f), new Vector2(0.190f, 2.95f), new Vector2(0.190f, 2.99f), new Vector2(0.000f, 2.99f),                           // il piatto della lanterna
            }, 12);
            var roof = DesertLathe.Turn("LampioneTetto", new[]
            {
                new Vector2(0.000f, 3.43f), new Vector2(0.210f, 3.43f), new Vector2(0.210f, 3.47f), new Vector2(0.190f, 3.49f), new Vector2(0.170f, 3.50f),                           // l'orlo del tetto
                new Vector2(0.120f, 3.63f), new Vector2(0.065f, 3.76f), new Vector2(0.045f, 3.80f),                                                                                      // il cono
                new Vector2(0.060f, 3.83f), new Vector2(0.045f, 3.86f), new Vector2(0.000f, 3.88f),                                                                                      // il puntale
            }, 12);
            var parts = new List<(Mesh mesh, Matrix4x4 at)> { (shaft, Matrix4x4.identity), (roof, Matrix4x4.identity) };
            // sei montanti agli spigoli del prisma di vetro
            var bar = DesertLathe.Tube("LampioneMontante", new[] { new Vector3(0.158f, 2.98f, 0f), new Vector3(0.158f, 3.20f, 0f), new Vector3(0.158f, 3.44f, 0f) }, new[] { 0.013f, 0.013f, 0.013f }, 5);
            for (int k = 0; k < 6; k++) parts.Add((bar, DesertLathe.At(Vector3.zero, k * 60f)));
            // la fascia a meta': un anello che li lega
            const int n = 24;
            var ring = new Vector3[n]; var rr = new float[n];
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; ring[i] = new Vector3(Mathf.Cos(a) * 0.162f, 3.21f, Mathf.Sin(a) * 0.162f); rr[i] = 0.011f; }
            parts.Add((DesertLathe.Tube("LampioneFascia", ring, rr, 5, true), Matrix4x4.identity));
            return iron = DesertLathe.Join("LampioneFerro", parts);
        }

        public static Mesh GlassMesh()
        {
            if (glass != null) return glass;
            return glass = DesertLathe.Turn("LampioneVetro", new[]
            {
                new Vector2(0.000f, 3.00f), new Vector2(0.125f, 3.00f), new Vector2(0.142f, 3.20f), new Vector2(0.128f, 3.42f), new Vector2(0.000f, 3.42f),
            }, 6);
        }
    }
}
