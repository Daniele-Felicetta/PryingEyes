using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    public static partial class RoomKits
    {
        /// <summary>Lampada da comodino o da terra: paralume che brilla e luce calda (a volte e' bruciata).</summary>
        static void NightLamp(Ctx c, Vector3 pos, bool on)
        {
            var g = new GameObject("Abatjour");
            g.transform.SetParent(c.props, false);
            g.transform.position = pos;
            var sh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Util.Kill(sh.GetComponent<Collider>());
            sh.transform.SetParent(g.transform, false);
            sh.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            sh.transform.localScale = new Vector3(0.2f, 0.11f, 0.2f);
            sh.GetComponent<MeshRenderer>().sharedMaterial = ProcKit.Mat(on ? "paralume" : "paralume_spento");
            sh.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var base_ = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Util.Kill(base_.GetComponent<Collider>());
            base_.transform.SetParent(g.transform, false);
            base_.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            base_.transform.localScale = new Vector3(0.1f, 0.06f, 0.1f);
            base_.GetComponent<MeshRenderer>().sharedMaterial = ProcKit.Mat("ottone");
            if (on) AddLight(c, pos + Vector3.up * 0.25f, new Color(1f, 0.72f, 0.42f), 2.0f, 6.5f, false, true, g.transform);
        }

        static void PendantBulb(Ctx c, Vector3 pos, bool on, float intensity = 6f, float range = 9f)
        {
            var g = new GameObject(on ? "Lampadina" : "LampadinaSpenta");
            g.transform.SetParent(c.props, false);
            g.transform.position = pos;
            var cord = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Util.Kill(cord.GetComponent<Collider>());
            cord.transform.SetParent(g.transform, false);
            float len = c.r.wallH - pos.y;
            cord.transform.localPosition = new Vector3(0f, len * 0.5f, 0f);
            cord.transform.localScale = new Vector3(0.015f, len * 0.5f, 0.015f);
            cord.GetComponent<MeshRenderer>().sharedMaterial = ProcKit.Mat("ferro");
            cord.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Bulb(c, pos, 0.14f, on ? "lampadina" : "lampadina_spenta", g.transform);
            if (on)
            {
                var pl = AddLight(c, pos + Vector3.down * 0.15f, new Color(1f, 0.76f, 0.5f), intensity, range, true, true, g.transform);
                if (c.rng.Chance(0.45)) g.AddComponent<LampFlicker>().Init(pl);
            }
        }

        static void FloorLampProp(Ctx c, Vector3 pos, Vector3 face, bool on)
        {
            var g = Prop(c, "arr_lampada", pos, face);
            if (g != null && on) AddLight(c, pos + Vector3.up * 1.25f, new Color(1f, 0.72f, 0.42f), 4.5f, 8f, true, true, g.transform);
        }

        // ---------------------------------------------------------------- stanze d'albergo
        static void Corridoio(Ctx c)
        {
            Vector3 a = LongAxis(c);
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            // corsia libera al centro: un carrello e una palma su due muri opposti chiudevano il corridoio per la navmesh (serve 0,8 m)
            Vector3 lane0 = fr.P(0f, Lr * 0.5f - 0.8f), lane1 = fr.P(Lf, Lr * 0.5f + 0.8f);
            c.occ.Add(Rect.MinMaxRect(Mathf.Min(lane0.x, lane1.x), Mathf.Min(lane0.z, lane1.z), Mathf.Max(lane0.x, lane1.x), Mathf.Max(lane0.z, lane1.z)));
            // il teatro (dietro le quinte) e il sanatorio hanno i loro corridoi
            if (c.plan.theme == Theme.Teatro) { CorridoioTeatro(c, fr); return; }
            if (c.plan.theme == Theme.Sanatorio) { CorridoioIstituto(c, fr); return; }
            // applique ogni ~5,5 m, alternate sui due muri
            int n = Mathf.Max(2, Mathf.RoundToInt(Lf / 5.5f));
            for (int i = 0; i < n; i++)
            {
                float f = (i + 0.5f) / n * Lf;
                bool left = i % 2 == 0;
                Vector3 pt = fr.P(f, left ? 0.02f : Lr - 0.02f, 2.1f);
                Vector3 inw = left ? fr.r : -fr.r;
                // niente applique davanti a una porta
                if (!Free(c, fr.P(f, left ? 0.3f : Lr - 0.3f), 0.5f, 0.3f, 0f, 0f)) continue;
                Sconce(c, pt, inw, c.rng.Chance(0.62f));
            }
            // un po' di disordine in fondo
            if (c.rng.Chance(0.4)) Prop(c, "arr_carrello", fr.P(Lf * c.rng.Range(0.2f, 0.8f), c.rng.Chance(0.5f) ? 0.5f : Lr - 0.5f), fr.r, 1f, true, 0f, c.rng.Range(-20f, 20f));
            if (c.rng.Chance(0.35)) Prop(c, "arr_palma", fr.P(Lf * c.rng.Range(0.2f, 0.8f), Lr - 0.45f), -fr.r, 1f, true, 0f, c.rng.Range(0f, 360f));
        }

        static void Atrio(Ctx c)
        {
            Side ds = MainSide(c, RoomKind.Corridoio);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            Vector3 toDoor = -fr.f;
            // reception: mobile delle chiavi contro il muro in fondo, banco davanti
            float sx = Mathf.Min(1f, (Lr - 0.8f) / 5.1f);
            Prop(c, "arr_chiavi", fr.P(Lf - 0.3f, Lr * 0.5f), toDoor, sx);
            // un solo banco, lasciando liberi i due lati: dietro al banco non deve restare una tasca chiusa (l'ascensore puo' aprirsi li')
            Prop(c, "arr_banco", fr.P(Lf - 1.75f, Lr * 0.5f), toDoor);
            // salotto contro un muro laterale
            bool left = c.rng.Chance(0.5);
            float rw = left ? 0.6f : Lr - 0.6f;
            Vector3 inL = left ? fr.r : -fr.r;
            float fs = Lf * 0.42f;
            Prop(c, "arr_divano", fr.P(fs, rw + (left ? 0.1f : -0.1f)), inL);
            Prop(c, "arr_tavolino", fr.P(fs, left ? 2.3f : Lr - 2.3f), inL);
            Prop(c, "arr_poltrona", fr.P(fs - 2.0f, left ? 2.2f : Lr - 2.2f), Quaternion.Euler(0f, left ? 40f : -40f, 0f) * -fr.f);
            FloorLampProp(c, fr.P(fs + 1.7f, rw), inL, c.rng.Chance(0.7));
            // palme agli angoli, orologio
            Prop(c, "arr_palma", fr.P(0.7f, 0.7f), fr.f, 1f, true, 0f, c.rng.Range(0f, 360f));
            Prop(c, "arr_palma", fr.P(0.7f, Lr - 0.7f), fr.f, 1f, true, 0f, c.rng.Range(0f, 360f));
            Prop(c, "arr_orologio", fr.P(Lf * 0.5f, left ? Lr - 0.3f : 0.3f), left ? -fr.r : fr.r);
            if (c.rng.Chance(0.5)) Prop(c, "arr_carrello", fr.P(Lf * 0.8f, left ? Lr - 0.6f : 0.6f), left ? -fr.r : fr.r, 1f, true, 0f, c.rng.Range(-30f, 30f));
            Chandelier(c, new Vector3(c.Center.x, c.r.wallH - 1.25f, c.Center.z), 0.9f, c.rng.Chance(0.75), 17f, 15f);
        }

        static void Camera(Ctx c, bool suite)
        {
            var rng = c.rng;
            Side ds = MainSide(c, RoomKind.Corridoio);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            bool left = rng.Chance(0.5);
            Vector3 inL = left ? fr.r : -fr.r;                  // dal muro del letto verso la stanza
            float wallR = left ? 0f : Lr;
            float sgn = left ? 1f : -1f;
            var bd = PropDefs.Get("arr_letto");
            float bedF = Mathf.Clamp(Lf - 1.35f - (suite ? 0.4f : 0f), 1.5f, Lf - 1.0f);
            var bed = Prop(c, "arr_letto", fr.P(bedF, wallR + sgn * (bd.size.z * 0.5f + 0.04f)), inL);
            if (bed != null)
            {
                foreach (float k in new[] { -1f, 1f })
                    Prop(c, "arr_comodino", fr.P(bedF + k * 1.2f, wallR + sgn * 0.25f), inL);
                bool on = rng.Chance(0.5);
                NightLamp(c, fr.P(bedF + 1.2f, wallR + sgn * 0.25f, 0.55f), on);
            }
            // armadio contro il muro opposto a quello del letto, vicino alla porta
            float oppR = left ? Lr : 0f;
            Prop(c, "arr_armadio", fr.P(1.1f, oppR - sgn * 0.35f), -inL);
            // scrivania contro il muro in fondo e sedia un po' storta
            float dr = Mathf.Clamp(Lr * rng.Range(0.35f, 0.65f), 1.0f, Lr - 1.0f);
            if (Prop(c, "arr_scrivania", fr.P(Lf - 0.35f, dr), -fr.f) != null)
                Prop(c, "arr_sedia", fr.P(Lf - 1.05f, dr + rng.Range(-0.2f, 0.2f)), fr.f, 1f, true, 0f, rng.Range(-35f, 35f));
            if (suite)
            {
                Prop(c, "arr_divano", fr.P(Lf * 0.5f, oppR - sgn * 0.62f), -inL);
                Prop(c, "arr_poltrona", fr.P(Lf * 0.5f + 1.7f, oppR - sgn * 0.8f), -inL, 1f, true, 0f, rng.Range(-25f, 25f));
                Prop(c, "arr_tavolino", fr.P(Lf * 0.5f, oppR - sgn * 1.9f), -inL);
            }
            if (rng.Chance(0.35)) Prop(c, "arr_palma", fr.P(Lf - 0.6f, oppR - sgn * 0.6f), fr.f, 1f, true, 0f, rng.Range(0f, 360f));
            PendantBulb(c, new Vector3(c.Center.x, c.r.wallH - 0.5f, c.Center.z), rng.Chance(0.6), suite ? 7.5f : 6f, suite ? 11f : 9f);
        }

        static void Servizio(Ctx c)
        {
            Side ds = MainSide(c);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            Prop(c, "arr_scaffale", fr.P(Lf - 0.25f, Lr * 0.5f), -fr.f, Mathf.Min(1f, (Lr - 0.2f) / 1.2f));
            if (Lr > 2.3f && Lf > 2.6f) Prop(c, "arr_carrello", fr.P(Lf * 0.5f, 0.55f), fr.r, 1f, true, 0f, c.rng.Range(-25f, 25f));
            PendantBulb(c, new Vector3(c.Center.x, c.r.wallH - 0.6f, c.Center.z), c.rng.Chance(0.5), 4.5f, 7f);
        }

        static void Ristorante(Ctx c)
        {
            Vector3 a = LongAxis(c);
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            // bar contro il muro in fondo
            Prop(c, "arr_banco", fr.P(Lf - 0.8f, Lr * 0.5f), -fr.f);
            int nf = Mathf.Max(1, Mathf.FloorToInt((Lf - 2.6f) / 2.3f)), nr = Mathf.Max(1, Mathf.FloorToInt((Lr - 1.0f) / 2.3f));
            for (int i = 0; i < nf; i++)
                for (int j = 0; j < nr; j++)
                {
                    if (c.rng.Chance(0.12)) continue;
                    float f = 1.6f + i * 2.3f + (Lf - 2.6f - nf * 2.3f) * 0.2f, rr = (j + 0.5f) * (Lr / nr);
                    var t = Prop(c, "arr_tavolo", fr.P(f, rr), fr.f);
                    if (t == null) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        if (c.rng.Chance(0.25)) continue;
                        Vector3 dir = k == 0 ? fr.f : k == 1 ? -fr.f : k == 2 ? fr.r : -fr.r;
                        Prop(c, "arr_sedia", fr.P(f, rr) + dir * 0.78f, -dir, 1f, true, 0f, c.rng.Range(-30f, 30f));
                    }
                }
            int nl = Mathf.Max(1, Mathf.RoundToInt(Lf / 5f));
            for (int i = 0; i < nl; i++)
                Chandelier(c, fr.P((i + 0.5f) / nl * Lf, Lr * 0.5f, c.r.wallH - 1.3f), 0.8f, c.rng.Chance(0.55), 14f, 13f);
        }

        static void Cucina(Ctx c)
        {
            Vector3 a = LongAxis(c);
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            int n = Mathf.Max(1, Mathf.FloorToInt(Lf / 1.9f));
            for (int i = 0; i < n; i++)
                Prop(c, "arr_fornello", fr.P((i + 0.5f) * (Lf / n), Lr - 0.45f), -fr.r, 1f, true, 0f);
            for (int i = 0; i < Mathf.Max(1, n - 1); i++)
                Prop(c, "arr_tavolo_acciaio", fr.P((i + 0.5f) * (Lf / Mathf.Max(1, n - 1)), Lr * 0.4f), fr.f);
            Prop(c, "arr_scaffale", fr.P(0.3f, 0.3f), fr.f);
            PendantBulb(c, new Vector3(c.Center.x, c.r.wallH - 0.6f, c.Center.z), c.rng.Chance(0.55), 7f, 10f);
        }

        static void SalaBallo(Ctx c)
        {
            Vector3 a = LongAxis(c);
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            Prop(c, "arr_pianoforte", fr.P(Lf - 1.6f, Lr * 0.5f), -fr.f);
            // sedie lungo i muri lunghi
            int n = Mathf.Max(2, Mathf.FloorToInt((Lf - 2f) / 1.4f));
            for (int i = 0; i < n; i++)
                foreach (float rr in new[] { 0.4f, Lr - 0.4f })
                {
                    if (c.rng.Chance(0.3)) continue;
                    Prop(c, "arr_sedia", fr.P(1.2f + i * 1.4f, rr), rr < 1f ? fr.r : -fr.r, 1f, true, 0f, c.rng.Range(-20f, 20f));
                }
            int nl = Mathf.Max(2, Mathf.RoundToInt(Lf / 5.5f));
            for (int i = 0; i < nl; i++)
                Chandelier(c, fr.P((i + 0.5f) / nl * Lf, Lr * 0.5f, c.r.wallH - 1.6f), 1.1f, c.rng.Chance(0.5), 18f, 16f);
        }
    }
}
