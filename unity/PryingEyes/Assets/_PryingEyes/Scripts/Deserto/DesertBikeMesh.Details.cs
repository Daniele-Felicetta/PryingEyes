using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>I dettagli meccanici della moto (0.9.3), cuciti nelle mesh condivise che DesertBikeMesh.Ensure costruisce: ogni pezzo va nella lista del suo materiale (nero, alluminio, acciaio, gomma, cuoio), quindi
    /// i disegni di una moto restano pochi. Sul corpo: gli ammortizzatori con le loro molle, la catena con le due corone, il cavalletto ripiegato, il maniglione dietro, la targa dietro, il carburatore e il filtro dell'aria,
    /// la leva del cambio, i filetti e il tappo del serbatoio, le ginocchiere. Sulle ruote: il tassellato del pneumatico, il disco del freno (davanti e dietro) e la corona. Sullo sterzo: i soffietti della forcella,
    /// la pinza e il tubo del freno davanti, le leve, gli specchietti, le staffe e la cornice del faro. Le misure sono in metri nel sistema della moto (x a destra, y in alto, z avanti, origine a terra a meta'
    /// dell'interasse), tranne quelle dello sterzo, che sono nel suo sistema (y lungo la forcella, inclinata di 22 gradi) e quelle delle ruote, nel sistema della ruota (asse lungo x).</summary>
    public static partial class DesertBikeMesh
    {
        /// <summary>Le liste dei pezzi di ogni mesh condivisa: Ensure le unisce a quelle di prima prima di fonderle.</summary>
        sealed class Details
        {
            // sistema della moto
            public readonly List<(Mesh mesh, Matrix4x4 at)> frame = NewList(), metal = NewList(), steel = NewList(), seat = NewList(), trim = NewList(), plateRear = NewList();
            // sistema della ruota
            public readonly List<(Mesh mesh, Matrix4x4 at)> tread = NewList(), discFront = NewList(), discRear = NewList();
            // sistema dello sterzo
            public readonly List<(Mesh mesh, Matrix4x4 at)> fork = NewList(), bars = NewList(), grips = NewList(), trimSteer = NewList(), glass = NewList();
        }

        /// <summary>Un solido di rotazione con l'asse lungo x (le ruote, i dischi, i coperchi del motore).</summary>
        static readonly Quaternion AxisX = Quaternion.Euler(0f, 0f, 90f);

        static List<(Mesh mesh, Matrix4x4 at)> NewList() => new List<(Mesh mesh, Matrix4x4 at)>();

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>Un punto del manubrio nel sistema dello sterzo ('x' e' la distanza dal centro, s il lato): lo stesso di GripAt, senza portarlo fuori nel sistema della moto.</summary>
        static Vector3 BarLocal(float s, float x)
        {
            float u = Mathf.InverseLerp(0.20f, 0.43f, x);
            return V(s * x, Mathf.Lerp(0.155f, 0.20f, u), Mathf.Lerp(-0.015f, -0.13f, u));
        }

        static Details MakeDetails(Quaternion steerRot, Matrix4x4 inv)
        {
            var d = new Details();
            BodyDetails(d);
            WheelDetails(d);
            SteerDetails(d, steerRot, inv);
            return d;
        }

        // ------------------------------------------------------------------ corpo
        static void BodyDetails(Details d)
        {
            // ---- ammortizzatori posteriori: corpo nero, stelo cromato e molla, inclinati in avanti di una ventina di gradi
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 top = V(s * 0.115f, 0.84f, -0.47f), bot = V(s * 0.115f, 0.40f, -0.62f);
                Vector3 dir = (top - bot).normalized;
                d.frame.Add(Tube("ammortizzatore", new[] { bot, bot + dir * 0.19f }, 0.0155f, 8));
                d.metal.Add(Tube("stelo", new[] { bot + dir * 0.19f, top - dir * 0.025f }, 0.0075f, 8));
                d.frame.Add(Tube("attacco alto", new[] { V(s * 0.07f, 0.86f, -0.46f), top }, 0.009f, 6));
                d.frame.Add(Tube("attacco basso", new[] { V(s * 0.103f, 0.345f, -0.62f), bot }, 0.011f, 6));
                Vector3 w = Vector3.Cross(dir, Vector3.right).normalized, u = Vector3.Cross(w, dir).normalized;
                Vector3 s0 = bot + dir * 0.18f, s1 = top - dir * 0.07f;
                var coil = new Vector3[8 * 8 + 1];
                for (int i = 0; i < coil.Length; i++)
                {
                    float t = i / (float)(coil.Length - 1), phi = t * 8f * Mathf.PI * 2f;
                    coil[i] = Vector3.Lerp(s0, s1, t) + (u * Mathf.Cos(phi) + w * Mathf.Sin(phi)) * 0.026f;
                }
                d.metal.Add(Tube("molla", coil, 0.0048f, 5));
            }

            // ---- coda: il maniglione che chiude il telaietto, due stanghette e la targa sotto il fanalino
            var loop = Smooth(P(0.06f, 0.85f, -0.78f, 0.055f, 0.86f, -0.90f, 0.035f, 0.865f, -0.97f, -0.035f, 0.865f, -0.97f, -0.055f, 0.86f, -0.90f, -0.06f, 0.85f, -0.78f), 3);
            d.frame.Add(Tube("maniglione", loop, 0.011f, 6));
            foreach (float s in new[] { -1f, 1f }) d.frame.Add(Tube("sostegno targa", new[] { V(s * 0.055f, 0.86f, -0.90f), V(s * 0.05f, 0.70f, -0.99f) }, 0.007f, 6));
            d.plateRear.Add((Cube("targa dietro", new Vector3(0.17f, 0.115f, 0.008f)), At(V(0f, 0.625f, -0.998f), Quaternion.Euler(12f, 0f, 0f))));

            // ---- il cavalletto ripiegato sotto il forcellone sinistro e la leva del cambio
            d.frame.Add(Tube("cavalletto", Smooth(P(-0.12f, 0.335f, -0.10f, -0.15f, 0.31f, -0.24f, -0.155f, 0.305f, -0.40f), 3), 0.011f, 6));
            d.frame.Add((Cube("piede del cavalletto", new Vector3(0.045f, 0.012f, 0.06f)), At(V(-0.155f, 0.296f, -0.41f), Quaternion.identity)));
            d.metal.Add(Tube("leva del cambio", Smooth(P(-0.10f, 0.385f, 0.06f, -0.17f, 0.375f, 0.09f, -0.225f, 0.355f, 0.07f), 3), 0.007f, 6));
            d.metal.Add((Cube("pedale del cambio", new Vector3(0.026f, 0.012f, 0.030f)), At(V(-0.232f, 0.352f, 0.068f), Quaternion.identity)));

            // ---- motore: il coperchio sinistro (come il destro), il carburatore dietro la testa e il filtro dell'aria
            d.metal.Add((DesertLathe.Turn("coperchio sinistro", new[] { new Vector2(0f, -0.014f), new Vector2(0.075f, -0.014f), new Vector2(0.075f, 0.014f), new Vector2(0f, 0.014f) }, 14), At(V(-0.115f, 0.43f, -0.01f), AxisX)));
            d.metal.Add((DesertLathe.Turn("carburatore", new[] { new Vector2(0f, -0.045f), new Vector2(0.024f, -0.045f), new Vector2(0.024f, 0.045f), new Vector2(0f, 0.045f) }, 12), At(V(0f, 0.80f, 0.06f), Lay)));
            d.metal.Add((DesertLathe.Turn("filtro", new[] { new Vector2(0f, -0.035f), new Vector2(0.034f, -0.035f), new Vector2(0.040f, -0.026f), new Vector2(0.040f, 0.022f), new Vector2(0.026f, 0.035f), new Vector2(0f, 0.035f) }, 14), At(V(0f, 0.80f, -0.02f), Lay)));

            // ---- catena e pignone del motore (acciaio)
            Sprocket(d.steel, V(-0.112f, 0.40f, -0.12f), 0f, 0.040f, 12, 0);
            Chain(d.steel);

            // ---- serbatoio: due filetti d'ottone attorno e il tappo (la stessa matrice del serbatoio: il filetto gira sulle stesse quattordici facce), le ginocchiere di cuoio sulle fiancate
            Matrix4x4 tankAt = At(V(0f, 0.99f, 0.14f), Lay, V(0.95f, 1f, 0.85f));
            foreach (float y0 in new[] { 0.008f, 0.034f })
                d.trim.Add((DesertLathe.Turn("filetto", new[] { new Vector2(0.1177f, y0), new Vector2(0.1177f, y0 + 0.012f), new Vector2(0.1130f, y0 + 0.012f), new Vector2(0.1130f, y0), new Vector2(0.1177f, y0) }, 14), tankAt));
            d.trim.Add((DesertLathe.Turn("tappo", new[] { new Vector2(0f, 0f), new Vector2(0.030f, 0f), new Vector2(0.030f, 0.008f), new Vector2(0.025f, 0.013f), new Vector2(0f, 0.013f) }, 16), At(V(0f, 1.078f, 0.205f), Quaternion.identity)));
            foreach (float s in new[] { -1f, 1f }) d.seat.Add((Cube("ginocchiera", new Vector3(0.012f, 0.085f, 0.15f)), At(V(s * 0.1055f, 0.985f, 0.13f), Quaternion.identity)));
        }

        /// <summary>La catena: due tratti dritti, uno sopra e uno sotto, tra il pignone del motore e la corona della ruota; ogni tratto e' una fila di maglie alternate larghe e strette.</summary>
        static void Chain(List<(Mesh mesh, Matrix4x4 at)> into)
        {
            var a = new Vector2(-0.12f, 0.40f);                                    // (z, y) del pignone
            var b = new Vector2(AxleRear.z, AxleRear.y);                           // e della corona
            const float ra = 0.040f, rb = 0.092f, xa = -0.108f, xb = -0.086f;
            Vector2 dd = b - a;
            float L = dd.magnitude, ang = Mathf.Atan2(dd.y, dd.x), off = Mathf.Acos(Mathf.Clamp((ra - rb) / L, -1f, 1f));
            for (int run = 0; run < 2; run++)
            {
                float phi = ang + (run == 0 ? off : -off);
                var n = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
                Vector2 p1 = a + n * ra, p2 = b + n * rb;
                Vector3 s3 = V(xa, p1.y, p1.x), e3 = V(xb, p2.y, p2.x), dir = (e3 - s3);
                float len = dir.magnitude; dir /= len;
                int m = Mathf.Max(2, Mathf.RoundToInt(len / 0.0125f));
                for (int i = 0; i < m; i++)
                {
                    Vector3 pos = Vector3.Lerp(s3, e3, (i + 0.5f) / m);
                    bool inner = (i & 1) == 0;
                    into.Add((Cube("maglia", new Vector3(inner ? 0.007f : 0.012f, 0.0075f, len / m * 0.92f)), At(pos, Quaternion.LookRotation(dir, Vector3.up))));
                }
            }
        }

        /// <summary>Una corona dentata, disegnata attorno all'asse x in 'c': un anello (o un disco pieno se 'rIn' e' zero) con i denti, e dei bracci fino al mozzo (r = 0,038) per chi ha l'anello.</summary>
        static void Sprocket(List<(Mesh mesh, Matrix4x4 at)> into, Vector3 c, float rIn, float rOut, int teeth, int arms)
        {
            const float t = 0.003f;
            if (rIn > 0.001f) into.Add((DesertLathe.Turn("corona", new[] { new Vector2(rOut, -t), new Vector2(rOut, t), new Vector2(rIn, t), new Vector2(rIn, -t), new Vector2(rOut, -t) }, 24), At(c, AxisX)));
            else into.Add((DesertLathe.Turn("pignone", new[] { new Vector2(rOut, -t), new Vector2(rOut, t), new Vector2(0f, t), new Vector2(0f, -t), new Vector2(rOut, -t) }, 16), At(c, AxisX)));
            for (int i = 0; i < teeth; i++)
            {
                var rot = Quaternion.Euler(i * 360f / teeth, 0f, 0f);
                into.Add((Cube("dente", new Vector3(0.007f, 0.011f, 0.0085f)), At(c + rot * V(0f, rOut + 0.003f, 0f), rot)));
            }
            for (int i = 0; i < arms; i++)
            {
                var rot = Quaternion.Euler(i * 360f / arms + 20f, 0f, 0f);
                float r0 = 0.038f, len = rIn - r0 + 0.006f;
                into.Add((Cube("braccio", new Vector3(0.006f, len, 0.014f)), At(c + rot * V(0f, r0 + len * 0.5f - 0.003f, 0f), rot)));
            }
        }

        /// <summary>Un disco del freno attorno all'asse x, a distanza 'x' dal centro: l'anello e i bracci che lo tengono al mozzo.</summary>
        static void BrakeDisc(List<(Mesh mesh, Matrix4x4 at)> into, float x, float rIn, float rOut, int arms)
        {
            const float t = 0.0035f;
            into.Add((DesertLathe.Turn("disco", new[] { new Vector2(rOut, -t), new Vector2(rOut, t), new Vector2(rIn, t), new Vector2(rIn, -t), new Vector2(rOut, -t) }, 32), At(V(x, 0f, 0f), AxisX)));
            for (int i = 0; i < arms; i++)
            {
                var rot = Quaternion.Euler(i * 360f / arms + 12f, 0f, 0f);
                float r0 = 0.038f, len = rIn - r0 + 0.006f;
                into.Add((Cube("braccio", new Vector3(0.006f, len, 0.014f)), At(rot * V(x, r0 + len * 0.5f - 0.003f, 0f), rot)));
            }
        }

        // ------------------------------------------------------------------ ruote
        static void WheelDetails(Details d)
        {
            // il tassellato: una fila di tasselli per lato, a spina di pesce, sfalsati di mezzo passo (il pneumatico e' alto 0,340 al centro e scende alle spalle: i tasselli affondano di un centimetro)
            const int n = 30;
            for (int i = 0; i < n; i++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var rot = Quaternion.Euler((i + (side > 0 ? 0.5f : 0f)) * 360f / n, 0f, 0f);
                    d.tread.Add((Cube("tassello", new Vector3(0.030f, 0.022f, 0.017f)), At(rot * V(side * 0.021f, 0.336f, 0f), rot * Quaternion.Euler(0f, side * 24f, 0f))));
                }
            // davanti il disco sta a destra, dietro a destra il disco e a sinistra la corona (col distanziale che la porta fuori dal mozzo)
            BrakeDisc(d.discFront, 0.046f, 0.105f, 0.150f, 5);
            BrakeDisc(d.discRear, 0.046f, 0.070f, 0.118f, 4);
            Sprocket(d.discRear, V(-0.082f, 0f, 0f), 0.060f, 0.086f, 22, 4);
            d.discRear.Add((DesertLathe.Turn("distanziale", new[] { new Vector2(0.03f, -0.0075f), new Vector2(0.03f, 0.0075f), new Vector2(0f, 0.0075f), new Vector2(0f, -0.0075f), new Vector2(0.03f, -0.0075f) }, 12), At(V(-0.0775f, 0f, 0f), AxisX)));
            // la pinza dietro (ottone), sull'angolo in alto davanti del disco, tenuta dal forcellone da una barretta
            d.trim.Add((Cube("pinza dietro", new Vector3(0.034f, 0.05f, 0.06f)), At(AxleRear + V(0.046f, 0.075f, 0.078f), Quaternion.Euler(-40f, 0f, 0f))));
            d.frame.Add(Tube("barretta pinza", new[] { AxleRear + V(0.058f, 0.075f, 0.078f), V(0.10f, 0.36f, -0.50f) }, 0.008f, 6));
        }

        // ------------------------------------------------------------------ sterzo
        static void SteerDetails(Details d, Quaternion steerRot, Matrix4x4 inv)
        {
            Vector3 axle = V(0f, -0.6908f, 0f);                                    // l'asse della ruota davanti nel sistema dello sterzo
            foreach (float s in new[] { -1f, 1f })
            {
                // il soffietto di gomma tra la piastra bassa e il fodero: otto pieghe
                var pr = new List<Vector2>();
                for (int i = 0; i <= 8; i++) pr.Add(new Vector2(i % 2 == 0 ? 0.024f : 0.033f, i * 0.01625f));
                d.grips.Add((DesertLathe.Turn("soffietto", pr.ToArray(), 12), At(V(s * 0.075f, -0.198f, 0f), Quaternion.identity)));

                // la leva (alluminio, rastremata) col suo supporto nero, davanti alla manopola: va in avanti e curva verso il basso; i punti sono scarti nel sistema della moto, portati nel sistema dello sterzo
                Vector3 l0 = BarLocal(s, 0.262f) + V(0f, -0.012f, 0f);
                Vector3[] lo = { V(0f, 0f, 0f), V(s * 0.014f, -0.002f, 0.058f), V(s * 0.026f, -0.016f, 0.115f), V(s * 0.032f, -0.042f, 0.158f) };
                var lever = new Vector3[lo.Length];
                for (int i = 0; i < lo.Length; i++) lever[i] = l0 + Quaternion.Inverse(steerRot) * lo[i];
                d.fork.Add(Tube("leva", Smooth(lever, 3), t => Mathf.Lerp(0.0085f, 0.0055f, t), 6));
                d.bars.Add((Cube("supporto leva", new Vector3(0.045f, 0.032f, 0.045f)), At(BarLocal(s, 0.262f) + V(0f, -0.004f, 0.006f), Quaternion.identity)));

                // lo specchietto: lo stelo sale dal supporto e finisce sotto il guscio rotondo, che guarda all'indietro e un poco in dentro; il vetro e' un disco chiaro dentro il bordo (un materiale a parte, che di giorno riflette il cielo)
                Vector3 b0 = BarLocal(s, 0.250f);
                d.bars.Add(Tube("stelo specchietto", Smooth(new[] { b0, b0 + V(s * 0.004f, 0.050f, -0.010f), b0 + V(s * 0.014f, 0.100f, -0.022f), b0 + V(s * 0.022f, 0.128f, -0.030f) }, 3), 0.0055f, 6));
                Vector3 c = b0 + V(s * 0.028f, 0.157f, -0.042f), back = V(-s * 0.22f, 0.15f, -1f).normalized;
                var q = Quaternion.FromToRotation(Vector3.up, back);
                d.bars.Add((DesertLathe.Turn("specchietto", new[] { new Vector2(0f, -0.025f), new Vector2(0.016f, -0.0245f), new Vector2(0.028f, -0.018f), new Vector2(0.0335f, -0.008f), new Vector2(0.0345f, 0.010f), new Vector2(0.031f, 0.013f), new Vector2(0f, 0.013f) }, 16), At(c, q)));
                d.glass.Add((DesertLathe.Turn("vetro", new[] { new Vector2(0f, 0f), new Vector2(0.029f, 0f), new Vector2(0.029f, 0.0016f), new Vector2(0f, 0.0016f) }, 16), At(c + back * 0.0138f, q)));

                // la staffa del faro: dalla cima del fodero a un lato del faro
                d.bars.AddRange(ToSteer(new List<(Mesh mesh, Matrix4x4 at)> { Tube("staffa faro", new[] { Head + steerRot * V(s * 0.075f, 0.03f, 0f), V(s * 0.072f, 1.015f, 0.575f) }, 0.0075f, 6) }, inv));
            }

            // la pinza davanti (ottone, a destra, dietro il disco) tenuta alla gamba da una barretta, e il tubo del freno che scende dalla leva destra lungo la gamba
            d.trimSteer.Add((Cube("pinza davanti", new Vector3(0.034f, 0.085f, 0.07f)), At(axle + V(0.046f, 0f, -0.128f), Quaternion.identity)));
            d.fork.Add(Tube("barretta pinza davanti", new[] { axle + V(0.062f, 0f, -0.115f), axle + V(0.075f, 0f, -0.03f) }, 0.008f, 6));
            var hose = Smooth(new[] { V(0.262f, 0.158f, -0.035f), V(0.236f, 0.118f, 0.012f), V(0.166f, 0.078f, 0.046f), V(0.102f, 0.022f, 0.042f), V(0.090f, -0.20f, 0.038f), V(0.088f, -0.50f, 0.036f), V(0.074f, -0.62f, -0.02f), V(0.058f, -0.646f, -0.100f) }, 4);
            d.grips.Add(Tube("tubo del freno", hose, 0.0045f, 6));

            // la cornice d'ottone attorno al vetro del faro
            d.trimSteer.AddRange(ToSteer(new List<(Mesh mesh, Matrix4x4 at)> { (DesertLathe.Turn("cornice", new[] { new Vector2(0.081f, 0.024f), new Vector2(0.081f, 0.047f), new Vector2(0.063f, 0.047f), new Vector2(0.063f, 0.024f), new Vector2(0.081f, 0.024f) }, 24), At(V(0f, 1.02f, 0.58f), Lay)) }, inv));
        }
    }
}
