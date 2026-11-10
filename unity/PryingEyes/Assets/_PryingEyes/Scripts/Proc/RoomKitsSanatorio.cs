using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Sanatorio: l'istituto (accettazione, corsie, celle, servizi). Luce fredda di tubi al neon che sfarfallano, linoleum e
    /// vernice verde, letti in fila lungo i muri, carrelli. Gli arredi sono quelli dell'albergo (letti, comodini, banchi, tavoli
    /// d'acciaio) finche' la fase dei modelli non porta letti di ferro, flebo e lampada scialitica veri.</summary>
    public static partial class RoomKits
    {
        static readonly Color NeonCol = new Color(0.80f, 0.94f, 0.88f);

        /// <summary>Altezza a cui stanno le plafoniere: sotto il soffitto, o sotto le travi dove ci sono.</summary>
        static float NeonY(Ctx c) => c.r.wallH - (c.r.ceil == CeilKind.Beams ? 0.34f : 0.05f);

        /// <summary>Plafoniera a tubo: cassa di metallo, tubo che brilla, luce fredda con ombre e sfarfallio. Se 'on' e' falso e' morta.</summary>
        static void Neon(Ctx c, Vector3 pos, Vector3 along, float len, bool on, float intensity = 5.2f, float range = 8f)
        {
            var g = new GameObject(on ? "Neon" : "NeonSpento");
            g.transform.SetParent(c.props, false);
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(along);
            Renderer tube = null;
            void Part(Vector3 sc, Vector3 lp, Material m, bool isTube)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Util.Kill(p.GetComponent<Collider>());
                p.transform.SetParent(g.transform, false);
                p.transform.localPosition = lp; p.transform.localScale = sc;
                var mr = p.GetComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                if (isTube) tube = mr;
            }
            Part(new Vector3(0.18f, 0.045f, len), Vector3.zero, ProcKit.Mat("ferro"), false);
            Part(new Vector3(0.11f, 0.03f, len - 0.06f), new Vector3(0f, -0.032f, 0f), ProcKit.Mat(on ? "neon" : "neon_spento"), true);
            if (!on) return;
            var pl = AddLight(c, pos + Vector3.down * 0.45f, NeonCol, intensity, range, true, true, g.transform);
            g.AddComponent<NeonFlicker>().Init(pl, tube, ProcKit.Mat("neon"), ProcKit.Mat("neon_spento"));
        }

        /// <summary>Lampada scialitica sopra il tavolo operatorio: asta dal soffitto, piatto d'acciaio, disco che brilla e un faro bianco stretto.</summary>
        static void Scialitica(Ctx c, Vector3 pos)
        {
            float y = Mathf.Min(c.r.wallH - 1.0f, 2.55f);
            var g = new GameObject("Scialitica");
            g.transform.SetParent(c.props, false);
            g.transform.position = new Vector3(pos.x, 0f, pos.z);
            void Part(PrimitiveType t, Vector3 sc, Vector3 lp, string mat)
            {
                var p = GameObject.CreatePrimitive(t);
                Util.Kill(p.GetComponent<Collider>());
                p.transform.SetParent(g.transform, false);
                p.transform.localPosition = lp; p.transform.localScale = sc;
                var mr = p.GetComponent<MeshRenderer>();
                mr.sharedMaterial = ProcKit.Mat(mat);
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            float arm = c.r.wallH - y;
            Part(PrimitiveType.Cylinder, new Vector3(0.05f, arm * 0.5f, 0.05f), new Vector3(0f, y + arm * 0.5f, 0f), "acciaio");
            Part(PrimitiveType.Cylinder, new Vector3(0.95f, 0.035f, 0.95f), new Vector3(0f, y, 0f), "acciaio");
            Part(PrimitiveType.Cylinder, new Vector3(0.8f, 0.01f, 0.8f), new Vector3(0f, y - 0.034f, 0f), "neon");
            var pl = AddLight(c, g.transform.position + Vector3.up * (y - 0.15f), new Color(0.92f, 0.98f, 1f), 18f, 6.5f, true, true, g.transform, LightType.Spot);
            pl.l.spotAngle = 70f; pl.l.innerSpotAngle = 38f;
            pl.l.transform.rotation = Quaternion.LookRotation(Vector3.down);
        }

        /// <summary>Il corridoio dell'istituto: tubi al neon sull'asse (un terzo morti), carrelli e armadietti contro i muri.</summary>
        static void CorridoioIstituto(Ctx c, Frame fr)
        {
            float Lf = fr.Lf, Lr = fr.Lr;
            Vector3 a = fr.f;
            int n = Mathf.Max(2, Mathf.RoundToInt(Lf / 5f));
            for (int i = 0; i < n; i++)
                Neon(c, fr.P((i + 0.5f) / n * Lf, Lr * 0.5f, NeonY(c)), a, 1.25f, c.rng.Chance(0.68));
            if (c.rng.Chance(0.6)) Prop(c, "arr_carrello", fr.P(Lf * c.rng.Range(0.2f, 0.8f), c.rng.Chance(0.5f) ? 0.5f : Lr - 0.5f), fr.r, 1f, true, 0f, c.rng.Range(-20f, 20f));
            if (c.rng.Chance(0.4)) Prop(c, "arr_scaffale", fr.P(Lf * c.rng.Range(0.2f, 0.8f), Lr - 0.3f), -fr.r);
        }

        // ---------------------------------------------------------------- l'istituto
        /// <summary>L'accettazione: lo sportello con gli schedari dietro, una fila di sedie d'attesa contro un muro, un orologio, i tubi al neon.</summary>
        static void Accettazione(Ctx c)
        {
            Side ds = MainSide(c, RoomKind.Corridoio);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            Vector3 toDoor = -fr.f;
            Prop(c, "arr_chiavi", fr.P(Lf - 0.3f, Lr * 0.5f), toDoor, Mathf.Min(1f, (Lr - 0.8f) / 5.1f));
            Prop(c, "arr_banco", fr.P(Lf - 1.75f, Lr * 0.5f), toDoor, Mathf.Min(1f, (Lr - 1.6f) / 4.04f));
            bool left = c.rng.Chance(0.5);
            float rw = left ? 0.5f : Lr - 0.5f;
            Vector3 inL = left ? fr.r : -fr.r;
            int chairs = Mathf.Max(2, Mathf.FloorToInt((Lf - 4.2f) / 0.62f));
            ThemeRow(c, "arr_sedia", fr.P(2.4f, rw), fr.f * 0.62f, chairs, inL, 0.12);
            Prop(c, "arr_orologio", fr.P(Lf * 0.5f, left ? Lr - 0.3f : 0.3f), left ? -fr.r : fr.r);
            if (c.rng.Chance(0.5)) Prop(c, "arr_carrello", fr.P(Lf * 0.72f, left ? Lr - 0.6f : 0.6f), left ? -fr.r : fr.r, 1f, true, 0f, c.rng.Range(-30f, 30f));
            int n = Mathf.Clamp(Mathf.RoundToInt(Lf / 3.5f), 2, 4);
            for (int i = 0; i < n; i++)
                Neon(c, fr.P((i + 0.5f) / n * Lf, Lr * 0.5f, NeonY(c)), fr.r, 1.25f, c.rng.Chance(0.75), 5.6f, 9f);
        }

        /// <summary>La degenza (e la corsia, piu' grande): letti con il comodino in fila lungo i muri lunghi, la testiera al muro; una fila sola
        /// se la stanza e' stretta, due se resta un passaggio di 1,3 m in mezzo.</summary>
        static void Degenza(Ctx c, bool big)
        {
            Side ds = MainSide(c, RoomKind.Corridoio);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            var bd = PropDefs.Get("arr_letto");
            float bw = bd.size.x, bl = bd.size.z;
            bool both = Lr >= 2f * bl + 1.3f;
            if (both)       // passaggio libero in mezzo alle due file
            {
                Vector3 p0 = fr.P(0f, Lr * 0.5f - 0.65f), p1 = fr.P(Lf, Lr * 0.5f + 0.65f);
                c.occ.Add(Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.z, p1.z), Mathf.Max(p0.x, p1.x), Mathf.Max(p0.z, p1.z)));
            }
            float pitch = bw + 0.65f;
            int[] sides = both ? new[] { 0, 1 } : new[] { c.rng.Chance(0.5) ? 0 : 1 };
            foreach (int sd in sides)
            {
                float wallR = sd == 0 ? 0f : Lr, sgn = sd == 0 ? 1f : -1f;
                Vector3 inW = sd == 0 ? fr.r : -fr.r;
                for (float f = 1.2f + bw * 0.5f; f + bw * 0.5f < Lf - 0.2f; f += pitch)
                {
                    if (c.rng.Chance(big ? 0.1 : 0.22)) continue;                   // un letto e' stato portato via
                    var bed = Prop(c, "arr_letto", fr.P(f, wallR + sgn * (bl * 0.5f + 0.04f)), inW);
                    if (bed != null && c.rng.Chance(0.75)) Prop(c, "arr_comodino", fr.P(f + bw * 0.5f + 0.35f, wallR + sgn * 0.25f), inW);
                }
            }
            if (c.rng.Chance(0.5)) Prop(c, "arr_carrello", fr.P(Lf - 0.9f, Lr * 0.5f), -fr.f, 1f, true, 0f, c.rng.Range(-25f, 25f));
            int n = Mathf.Clamp(Mathf.RoundToInt(Lf / (big ? 3.4f : 4.2f)), 1, 5);
            for (int i = 0; i < n; i++)
                Neon(c, fr.P((i + 0.5f) / n * Lf, Lr * 0.5f, NeonY(c)), fr.r, 1.25f, c.rng.Chance(0.7), 5.0f, 8.5f);
        }

        /// <summary>La cella: una branda contro il muro in fondo, una lampadina nuda a volte spenta. La feritoia sul muro la fanno le finestre.</summary>
        static void Cella(Ctx c)
        {
            Side ds = MainSide(c);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            var bd = PropDefs.Get("arr_letto");
            bool left = c.rng.Chance(0.5);
            Prop(c, "arr_letto", fr.P(Lf - (bd.size.z * 0.5f + 0.04f), left ? bd.size.x * 0.5f + 0.1f : Lr - bd.size.x * 0.5f - 0.1f), -fr.f);
            if (c.rng.Chance(0.4)) Prop(c, "arr_sedia", fr.P(Lf * 0.5f, left ? Lr - 0.4f : 0.4f), left ? -fr.r : fr.r, 1f, true, 0f, c.rng.Range(-40f, 40f));
            PendantBulb(c, new Vector3(c.Center.x, c.r.wallH - 0.5f, c.Center.z), c.rng.Chance(0.55), 3.8f, 6f);
        }

        /// <summary>Il refettorio: lunghe tavolate di tavoli accostati con le sedie ai due lati (alcune mancano), il banco di distribuzione in fondo.</summary>
        static void Refettorio(Ctx c)
        {
            Vector3 a = LongAxis(c);
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            Prop(c, "arr_banco", fr.P(Lf - 0.9f, Lr * 0.5f), -fr.f, Mathf.Min(1f, (Lr - 1.2f) / 4.04f));
            int tables = Mathf.Max(1, Mathf.FloorToInt((Lr - 1.0f) / 2.6f));
            int n = Mathf.Max(2, Mathf.FloorToInt((Lf - 4.6f) / 1.0f));
            for (int j = 0; j < tables; j++)
            {
                float rr = (j + 0.5f) * (Lr / tables);
                for (int i = 0; i < n; i++)
                {
                    float f = 1.6f + i * 1.0f;                // 1 m tra i centri: i tavoli da 0,95 m non si toccano (il controllo ha un margine)
                    var t = Prop(c, "arr_tavolo", fr.P(f, rr), fr.f);
                    if (t == null) continue;
                    foreach (float s in new[] { -1f, 1f })
                        if (!c.rng.Chance(0.3)) Prop(c, "arr_sedia", fr.P(f, rr + s * 0.78f), -fr.r * s, 1f, true, 0f, c.rng.Range(-25f, 25f));
                }
            }
            int nl = Mathf.Clamp(Mathf.RoundToInt(Lf / 3.6f), 2, 6);
            for (int i = 0; i < nl; i++)
                Neon(c, fr.P((i + 0.5f) / nl * Lf, Lr * 0.5f, NeonY(c)), fr.r, 1.25f, c.rng.Chance(0.7), 5.2f, 9f);
        }

        /// <summary>La sala operatoria: il tavolo d'acciaio al centro sotto la lampada scialitica, carrelli, armadietti, neon bianchi forti.</summary>
        static void SalaOperatoria(Ctx c)
        {
            Vector3 a = LongAxis(c);
            Vector3 ctr = c.Center;
            var table = Prop(c, "arr_tavolo_acciaio", ctr, a, 1f, false);
            if (table != null)
            {
                Occupy(c, ctr, 1.9f, 0.9f, YawOf(a));
                Scialitica(c, ctr);
            }
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            ThemeRow(c, "arr_scaffale", fr.P(1.2f, 0.3f), fr.f * 1.3f, Mathf.Max(1, Mathf.FloorToInt((Lf - 2.4f) / 1.3f)), fr.r, 0.35);
            if (c.rng.Chance(0.8)) Prop(c, "arr_carrello", fr.P(Lf * 0.5f + 2.0f, Lr * 0.5f + 1.6f), fr.r, 1f, true, 0f, c.rng.Range(-30f, 30f));
            if (c.rng.Chance(0.6)) Prop(c, "arr_carrello", fr.P(Lf * 0.5f - 2.0f, Lr * 0.5f - 1.6f), -fr.r, 1f, true, 0f, c.rng.Range(-30f, 30f));
            int n = Mathf.Clamp(Mathf.RoundToInt(Lf / 4f), 2, 5);
            for (int i = 0; i < n; i++)
                Neon(c, fr.P((i + 0.5f) / n * Lf, Lr * (i % 2 == 0 ? 0.3f : 0.7f), NeonY(c)), fr.r, 1.25f, c.rng.Chance(0.8), 7.0f, 10f);
        }

        /// <summary>La lavanderia: una fila di vasche contro il muro lungo, tavoli d'acciaio per piegare, carrelli della biancheria.</summary>
        static void Lavanderia(Ctx c)
        {
            Vector3 a = LongAxis(c);
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            var vd = PropDefs.Get("arr_vasca");
            int n = Mathf.Max(1, Mathf.FloorToInt((Lf - 1.2f) / 1.0f));
            ThemeRow(c, "arr_vasca", fr.P(0.9f, Lr - (vd.size.z * 0.5f + 0.05f)), fr.f * 1.0f, n, -fr.r, 0.15);
            int tables = Mathf.Clamp(Mathf.FloorToInt(Lf / 3.2f), 1, 3);
            for (int i = 0; i < tables; i++)
                Prop(c, "arr_tavolo_acciaio", fr.P((i + 0.5f) * Lf / tables, Lr * 0.28f), fr.r, 1f, true, 0f, c.rng.Range(-8f, 8f));
            if (c.rng.Chance(0.7)) Prop(c, "arr_carrello", fr.P(Lf * c.rng.Range(0.3f, 0.7f), 0.55f), fr.r, 1f, true, 0f, c.rng.Range(-25f, 25f));
            int nl = Mathf.Clamp(Mathf.RoundToInt(Lf / 3.4f), 1, 4);
            for (int i = 0; i < nl; i++)
                Neon(c, fr.P((i + 0.5f) / nl * Lf, Lr * 0.5f, NeonY(c)), fr.r, 1.25f, c.rng.Chance(0.72), 5.2f, 8.5f);
        }

        /// <summary>L'archivio: doppie file di scaffali (due scaffali schiena a schiena) con corsie di 1,2 m, una scrivania, una lampada.</summary>
        static void Archivio(Ctx c)
        {
            Side ds = MainSide(c);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            int rows = Mathf.Clamp(Mathf.FloorToInt((Lr - 0.3f) / 2.1f), 1, 4);
            float span = rows * 2.1f - 1.2f;                           // larghezza occupata dalle file (la corsia in piu' resta ai lati)
            float r0 = (Lr - span) * 0.5f;
            for (int j = 0; j < rows; j++)
            {
                float rr = r0 + 0.45f + j * 2.1f;                      // centro della doppia fila: due scaffali da 0,45 m accostati
                for (float f = 2.4f; f + 0.6f < Lf - 0.3f; f += 1.25f)
                {
                    if (c.rng.Chance(0.2)) continue;
                    Prop(c, "arr_scaffale", fr.P(f, rr - 0.23f), -fr.r);
                    if (c.rng.Chance(0.9)) Prop(c, "arr_scaffale", fr.P(f, rr + 0.23f), fr.r);
                }
            }
            if (Lf > 5f && c.rng.Chance(0.6)) { Prop(c, "arr_scrivania", fr.P(1.0f, 0.5f), fr.r); Prop(c, "arr_sedia", fr.P(1.0f, 1.3f), -fr.r, 1f, true, 0f, c.rng.Range(-30f, 30f)); }
            int n = Mathf.Clamp(Mathf.RoundToInt(Lf / 3.2f), 1, 4);
            for (int i = 0; i < n; i++)
                Neon(c, fr.P((i + 0.5f) / n * Lf, Lr * 0.5f, NeonY(c)), fr.r, 1.25f, c.rng.Chance(0.6), 4.6f, 8f);
        }

        /// <summary>L'obitorio: tavoli d'acciaio in due colonne, armadietti freddi in fondo, un carrello; neon azzurri e forti.</summary>
        static void Obitorio(Ctx c)
        {
            Vector3 a = LongAxis(c);
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            int n = Mathf.Clamp(Mathf.FloorToInt((Lf - 1.0f) / 2.9f), 1, 3);
            foreach (float rr in new[] { Lr * 0.27f, Lr * 0.73f })
                for (int i = 0; i < n; i++)
                    if (!c.rng.Chance(0.2)) Prop(c, "arr_tavolo_acciaio", fr.P(1.2f + (i + 0.5f) * (Lf - 2.4f) / n, rr), fr.r);
            ThemeRow(c, "arr_scaffale", fr.P(Lf - 0.25f, 0.8f), fr.r * 1.25f, Mathf.Max(1, Mathf.FloorToInt((Lr - 0.4f) / 1.25f)), -fr.f, 0.1);
            if (c.rng.Chance(0.6)) Prop(c, "arr_carrello", fr.P(Lf * 0.5f, Lr * 0.5f), fr.r, 1f, true, 0f, c.rng.Range(-30f, 30f));
            int nl = Mathf.Clamp(Mathf.RoundToInt(Lf / 3.2f), 1, 4);
            for (int i = 0; i < nl; i++)
                Neon(c, fr.P((i + 0.5f) / nl * Lf, Lr * 0.5f, NeonY(c)), fr.r, 1.25f, c.rng.Chance(0.8), 6.4f, 9f);
        }

        /// <summary>La stanza dei medici: due scrivanie con le loro sedie, uno scaffale, una lampada da terra e un tubo al neon.</summary>
        static void SalaMedici(Ctx c)
        {
            Side ds = MainSide(c);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            int n = Mathf.Clamp(Mathf.FloorToInt(Lr / 2.0f), 1, 2);
            for (int i = 0; i < n; i++)
            {
                float r = (i + 0.5f) * Lr / n;
                if (Prop(c, "arr_scrivania", fr.P(Lf - 0.35f, r), -fr.f) != null)
                    Prop(c, "arr_sedia", fr.P(Lf - 1.05f, r + c.rng.Range(-0.2f, 0.2f)), fr.f, 1f, true, 0f, c.rng.Range(-35f, 35f));
            }
            bool left = c.rng.Chance(0.5);
            Prop(c, "arr_scaffale", fr.P(2.6f, left ? 0.3f : Lr - 0.3f), left ? fr.r : -fr.r);
            FloorLampProp(c, fr.P(2.2f, left ? Lr - 0.5f : 0.5f), fr.f, c.rng.Chance(0.6));
            Neon(c, new Vector3(c.Center.x, NeonY(c), c.Center.z), fr.r, 1.25f, c.rng.Chance(0.7), 5.0f, 8f);
        }
    }
}
