using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    public static partial class FloorBuilder
    {
        /// <summary>Costruisce il sacrario del grimorio (PlanAnnex: uno per piano, fuori da un muro esterno). Chiamato dopo le stanze.</summary>
        static void BuildSacrari(FloorInstance fi)
        {
            foreach (var a in fi.plan.annexes)
                if (a.kind == AnnexKind.Sacrario) BuildSacrario(fi, a);
        }

        /// <summary>La stanza segreta del libro (2026-11-16). Dalla sala madre si vede solo muro: un tratto di muro con un occhio scolpito
        /// appena acceso di rosso, da cui escono sussurri. Premuto l'occhio (F) la parete rientra e scorre di lato: dietro c'e' un cunicolo
        /// basso (1,3 m, si passa accovacciati: niente navmesh, i nemici non entrano) e poi la camera, pietra della cripta e volta a botte,
        /// un pentacolo rosso inciso nel pavimento con i ceri sulle punte, due sarcofagi, l'occhio della Torre sul fondo e davanti
        /// l'altare col grimorio aperto (GrimoireAltar: il libro della Stregoneria o dell'Arsenale, scelto dal seme del piano).</summary>
        static void BuildSacrario(FloorInstance fi, Annex a)
        {
            var site = NewSite(fi, a);
            var tr = site.transform;
            var rng = new Rng(a.seed);
            Material stone = Mat("mur_cripta"), floorM = Mat("pav_cripta"), vault = Mat("sof_volta"), iron = Mat("ferro"), trim = Mat("cornice_pietra");

            // riferimento: 't' si allontana dal muro della sala, 's' corre lungo il muro dal centro del cunicolo
            Vector3 outV = a.side == Side.N ? Vector3.forward : a.side == Side.S ? Vector3.back : a.side == Side.E ? Vector3.right : Vector3.left;
            bool alongX = Plan.AlongX(a.side);
            Vector3 al = alongX ? Vector3.right : Vector3.forward;
            Vector3 origin = alongX ? new Vector3(M(a.center), 0f, M(a.line)) : new Vector3(M(a.line), 0f, M(a.center));
            Vector3 G(float t, float s, float y = 0f) => origin + outV * t + al * s + Vector3.up * y;
            Vector3 Sz(float tLen, float h, float sLen) => alongX ? new Vector3(sLen, h, tLen) : new Vector3(tLen, h, sLen);
            int sign = (a.side == Side.N || a.side == Side.E) ? 1 : -1;
            int n0 = alongX ? a.z0 : a.x0, n1 = alongX ? a.z1 : a.x1;
            float tA = sign > 0 ? M(n0 - a.line) : M(a.line - n1);
            float tB = sign > 0 ? M(n1 - a.line) : M(a.line - n0);
            float sLo = M((alongX ? a.x0 : a.z0) - a.center), sHi = M((alongX ? a.x1 : a.z1) - a.center);
            const float wt = 0.3f, passH = 1.3f;
            float halfP = a.width * 0.05f;
            float WH = PlanAnnexGen.SacWallH, VH = PlanAnnexGen.SacVaultH;
            float tI0 = tA + wt, tI1 = tB - wt, sI0 = sLo + wt, sI1 = sHi - wt;
            float sC = (sI0 + sI1) * 0.5f;

            var mb = new MeshBuilder();
            // ---- il cunicolo: pavimento, fianchi e soffitto a 1,3 m (dal filo interno del muro della sala al muro della camera)
            float p0 = TH, p1 = tA + wt;
            mb.QuadW(floorM, G(p0, -halfP), G(p1, -halfP), G(p1, halfP), G(p0, halfP), Vector3.up);
            foreach (float sg in new[] { -1f, 1f })
                mb.Box(stone, G((p0 + p1) * 0.5f, sg * (halfP + wt * 0.5f), (passH + 0.3f) * 0.5f), Sz(p1 - p0, passH + 0.3f, wt));
            mb.Box(stone, G((p0 + p1) * 0.5f, 0f, passH + 0.15f), Sz(p1 - p0, 0.3f, halfP * 2f + wt * 2f));
            // ---- la camera: pavimento e quattro muri (quello vicino col vano del cunicolo)
            mb.QuadW(floorM, G(tI0, sI0), G(tI1, sI0), G(tI1, sI1), G(tI0, sI1), Vector3.up);
            foreach (float sg in new[] { sLo + wt * 0.5f, sHi - wt * 0.5f })
                mb.Box(stone, G((tA + tB) * 0.5f, sg, WH * 0.5f), Sz(tB - tA, WH, wt));
            mb.Box(stone, G(tB - wt * 0.5f, sC, WH * 0.5f), Sz(wt, WH, sI1 - sI0));
            mb.Box(stone, G(tA + wt * 0.5f, (sI0 - halfP) * 0.5f, WH * 0.5f), Sz(wt, WH, Mathf.Max(0.05f, -halfP - sI0)));
            mb.Box(stone, G(tA + wt * 0.5f, (halfP + sI1) * 0.5f, WH * 0.5f), Sz(wt, WH, Mathf.Max(0.05f, sI1 - halfP)));
            mb.Box(stone, G(tA + wt * 0.5f, 0f, (passH + WH) * 0.5f), Sz(wt, WH - passH, halfP * 2f));
            // ---- la volta a botte (segmento di cerchio lungo 't') e le lunette di testa
            float span = sI1 - sI0, rise = VH - WH;
            float R = (rise * rise + span * span * 0.25f) / (2f * rise), yc = VH - R;
            float alpha = Mathf.Acos(Mathf.Clamp(span * 0.5f / R, -1f, 1f));
            const int N = 14;
            System.Func<int, Vector2> arcPt = step =>
            {
                float th = Mathf.Lerp(Mathf.PI - alpha, alpha, step / (float)N);
                return new Vector2(sC + R * Mathf.Cos(th), yc + R * Mathf.Sin(th));
            };
            for (int k = 0; k < N; k++)
            {
                Vector2 q0 = arcPt(k), q1 = arcPt(k + 1);
                Vector3 mid = G((tI0 + tI1) * 0.5f, (q0.x + q1.x) * 0.5f, (q0.y + q1.y) * 0.5f);
                Vector3 center = G((tI0 + tI1) * 0.5f, sC, yc);
                mb.QuadW(vault, G(tI0, q0.x, q0.y), G(tI1, q0.x, q0.y), G(tI1, q1.x, q1.y), G(tI0, q1.x, q1.y), (center - mid).normalized);
                // lunette: dalla quota d'imposta alla volta, sui due muri di testa
                foreach (float tt in new[] { tI0, tI1 })
                {
                    Vector3 f = tt == tI0 ? outV : -outV;
                    mb.QuadW(stone, G(tt, q0.x, WH), G(tt, q1.x, WH), G(tt, q1.x, q1.y), G(tt, q0.x, q0.y), f);
                }
            }
            // costoloni: archi di pietra sotto la volta, ogni 1,6 m
            for (float tt = tI0 + 0.9f; tt < tI1 - 0.5f; tt += 1.6f)
                for (int k = 0; k < N; k++)
                {
                    Vector2 q0 = arcPt(k), q1 = arcPt(k + 1);
                    Vector3 d0 = (G(0f, sC, yc) - G(0f, q0.x, q0.y)).normalized * 0.09f, d1 = (G(0f, sC, yc) - G(0f, q1.x, q1.y)).normalized * 0.09f;
                    mb.Beam(trim, G(tt, q0.x, q0.y) + d0, G(tt, q1.x, q1.y) + d1, 0.22f, 0.16f, (d0 + d1).normalized);
                }
            // cornice d'imposta lungo i muri lunghi
            foreach (float sg in new[] { sI0 + 0.06f, sI1 - 0.06f })
                mb.Beam(trim, G(tI0, sg, WH - 0.08f), G(tI1, sg, WH - 0.08f), 0.18f, 0.16f, Vector3.up);
            FlushObj(tr, "Sacrario", mb, true);
            // pavimenti pieni sotto il cunicolo e la camera (niente cadute tra i triangoli)
            var floorGo = new GameObject("Pavimento del sacrario");
            floorGo.transform.SetParent(tr, false);
            var fb = floorGo.AddComponent<BoxCollider>();
            fb.center = G((tI0 + tI1) * 0.5f, sC, -0.25f); fb.size = Sz(tI1 - tI0, 0.5f, sI1 - sI0);
            var pb = floorGo.AddComponent<BoxCollider>();
            pb.center = G((p0 + p1) * 0.5f, 0f, -0.25f); pb.size = Sz(p1 - p0 + 0.1f, 0.5f, halfP * 2f);

            // ---- il pentacolo inciso davanti all'altare: anello e stella di luce rossa a filo del pavimento
            var altarPos = G(tI1 - 1.15f, sC);
            var star = G(tI1 - 3.05f, sC, 0.006f);
            var runeMat = ShrineKit.EmitOwn(new Color(0.9f, 0.05f, 0.04f), 1.15f);
            var glyph = new MeshBuilder();
            Vector3 ax = -outV, ay = al;
            GlowRing(glyph, runeMat, star, ay, ax, 1.42f, 1.50f, Vector3.up, 48);
            GlowRing(glyph, runeMat, star, ay, ax, 1.62f, 1.66f, Vector3.up, 48);
            var pts = new Vector3[5];
            for (int i = 0; i < 5; i++) { float th = Mathf.PI * 0.5f + i * Mathf.PI * 2f / 5f; pts[i] = star + (ax * Mathf.Sin(th) + ay * Mathf.Cos(th)) * 1.44f; }
            for (int i = 0; i < 5; i++)
            {
                Vector3 pA = pts[i], pB = pts[(i + 2) % 5], d = (pB - pA).normalized, n = Vector3.Cross(Vector3.up, d) * 0.035f;
                glyph.QuadW(runeMat, pA - n, pB - n, pB + n, pA + n, Vector3.up);
            }
            FlushObj(tr, "Pentacolo", glyph, false);
            // ---- arredi: ceri sulle punte, sarcofagi lungo i fianchi, candelabri e l'occhio in fondo
            var props = site.props;
            float back = YawOfDir(-outV);
            foreach (var p in pts) AnnexProp(props, "chi_ceri", new Vector3(p.x, 0f, p.z) + (p - star).normalized * 0.22f, rng.Range(0f, 360f), 0.55f);
            if (sI1 - sI0 > 4.6f)
                foreach (float sg in new[] { sI0 + 0.55f, sI1 - 0.55f })
                    AnnexProp(props, "chi_sarcofago", G(tI0 + 1.5f, sg), YawOfDir(outV) + 90f, 0.92f);
            foreach (float sg in new[] { sI0 + 0.5f, sI1 - 0.5f })
                AnnexProp(props, "chi_candelabro", G(tI1 - 0.5f, sg), 0f, 1f);
            AnnexProp(props, "chi_occhio", G(tI1 - 0.18f, sC, 2.05f), back, 0.6f);
            // catene che pendono dalla volta
            var chains = new MeshBuilder();
            for (int c = 0; c < 4; c++)
            {
                float tt = Mathf.Lerp(tI0 + 1.2f, tI1 - 1.6f, c / 3f), ss = sC + (c % 2 == 0 ? -1f : 1f) * rng.Range(0.9f, span * 0.38f);
                float topY = yc + Mathf.Sqrt(Mathf.Max(0f, R * R - (ss - sC) * (ss - sC))) - 0.05f, len = rng.Range(0.9f, 1.8f);
                for (float y = topY; y > topY - len; y -= 0.07f)
                {
                    bool odd = Mathf.RoundToInt((topY - y) / 0.07f) % 2 == 1;
                    Vector3 c0 = G(tt, ss, y), c1 = G(tt, ss, y - 0.075f);
                    chains.Beam(iron, c0, c1, odd ? 0.035f : 0.012f, odd ? 0.012f : 0.035f, outV);
                }
            }
            FlushObj(props, "Catene", chains, false);
            // ---- luci: due ceri caldi in fondo, il rosso del pentacolo, la luce del cunicolo che si vede dalla sala solo a parete aperta
            foreach (float sg in new[] { sI0 + 0.5f, sI1 - 0.5f })
                ShrineKit.PointLight(tr, "Luce del sacrario", G(tI1 - 0.5f, sg, 1.75f), new Color(1f, 0.55f, 0.28f), 2.2f * LightPool.Dim, 6.5f, 0.28f, 3.4f);
            ShrineKit.PointLight(tr, "Bagliore del pentacolo", star + Vector3.up * 0.6f, new Color(1f, 0.12f, 0.08f), 1.6f * LightPool.Dim, 5.5f, 0.35f, 1.4f);
            ShrineKit.PointLight(tr, "Luce del cunicolo", G(tA + wt + 0.6f, 0f, 1.05f), new Color(0.95f, 0.25f, 0.15f), 0.9f * LightPool.Dim, 3.2f, 0.3f, 2f);

            // ---- la parete finta nel muro della sala: stessa finitura del muro, con l'occhio scolpito che respira di rosso
            var st = StyleOf(site.host, fi.plan.theme);
            var closer = new GameObject("Parete del sacrario");
            closer.transform.SetParent(tr, false);
            var cm = new MeshBuilder();
            float fT = -TH + 0.004f, bT = TH - 0.01f;
            float dado = st.dado != null && st.dadoH > 0.01f ? Mathf.Min(st.dadoH, passH) : 0f;
            if (dado > 0f) cm.QuadW(st.dado, G(fT, halfP, 0f), G(fT, -halfP, 0f), G(fT, -halfP, dado), G(fT, halfP, dado), -outV);
            cm.QuadW(st.wall != null ? st.wall : stone, G(fT, halfP, dado), G(fT, -halfP, dado), G(fT, -halfP, passH), G(fT, halfP, passH), -outV);
            cm.QuadW(stone, G(bT, -halfP, 0f), G(bT, halfP, 0f), G(bT, halfP, passH), G(bT, -halfP, passH), outV);
            foreach (float sg in new[] { -1f, 1f })
                cm.QuadW(stone, G(fT, sg * halfP, 0f), G(bT, sg * halfP, 0f), G(bT, sg * halfP, passH), G(fT, sg * halfP, passH), al * sg);
            cm.QuadW(stone, G(fT, -halfP, passH), G(bT, -halfP, passH), G(bT, halfP, passH), G(fT, halfP, passH), Vector3.up);
            var cgo = FlushObj(closer.transform, "Tappo", cm, false, HotelNav.DoorLayer);
            if (cgo != null)
            {
                var bc = cgo.AddComponent<BoxCollider>();
                bc.center = cgo.transform.InverseTransformPoint(G(0f, 0f, passH * 0.5f));
                bc.size = Sz(TH * 2f - 0.01f, passH, halfP * 2f);
                site.closerCol = bc;
            }
            site.closer = closer.transform;
            site.sigilMat = ShrineKit.EmitOwn(GateColor(a.gate), 0.9f);
            var eye = new MeshBuilder();
            Vector3 ec = G(fT - 0.004f, 0f, 0.78f);
            GlowRing(eye, site.sigilMat, ec, al, Vector3.up, 0.20f, 0.225f, -outV, 32);
            GlowRing(eye, site.sigilMat, ec, al, Vector3.up, 0.045f, 0.075f, -outV, 16);
            // la mandorla dell'occhio: due archi
            for (int k = 0; k < 12; k++)
            {
                float u0 = Mathf.Lerp(-0.17f, 0.17f, k / 12f), u1 = Mathf.Lerp(-0.17f, 0.17f, (k + 1) / 12f);
                float h0 = 0.09f * (1f - (u0 / 0.17f) * (u0 / 0.17f)), h1 = 0.09f * (1f - (u1 / 0.17f) * (u1 / 0.17f));
                foreach (float sgn in new[] { -1f, 1f })
                {
                    Vector3 a0 = ec + al * u0 + Vector3.up * (sgn * h0), a1 = ec + al * u1 + Vector3.up * (sgn * h1);
                    eye.QuadW(site.sigilMat, a0 - Vector3.up * 0.007f, a1 - Vector3.up * 0.007f, a1 + Vector3.up * 0.007f, a0 + Vector3.up * 0.007f, -outV);
                }
            }
            // le crepe da cui filtra la luce, lungo il contorno della parete finta
            foreach (float sg in new[] { -1f, 1f })
                eye.QuadW(site.sigilMat, G(fT - 0.002f, sg * (halfP - 0.004f), 0.02f), G(fT - 0.002f, sg * (halfP - 0.012f), 0.02f), G(fT - 0.002f, sg * (halfP - 0.012f), passH - 0.02f), G(fT - 0.002f, sg * (halfP - 0.004f), passH - 0.02f), -outV);
            FlushObj(closer.transform, "Occhio scolpito", eye, false);
            // i sussurri: si sentono da vicino, smettono quando la parete si apre (AnnexSite.Open spegne il cancello)
            var whisper = SoundBank.Whisper;
            if (whisper != null) Sfx.Loop(closer, whisper, 0.22f, 1.2f, 9f, 0.8f);

            site.gatePoint = G(fT - 0.05f, 0f, 0.8f);
            site.gateRadius = 2.4f;
            site.outside = G(-1.3f, 0f);
            site.inside = G((tI0 + tI1) * 0.5f, sC);
            site.rewardPos = altarPos;
            site.rewardYaw = YawOfDir(outV);             // l'altare guarda il fondo: chi legge sta tra il cunicolo e l'altare e guarda verso +z del libro
            site.keyPos = G(tI0 + 1.2f, sC);
            site.cellFloorY = 0f;
            site.secretOut = outV;
            site.secretAlong = al;
        }
    }
}
