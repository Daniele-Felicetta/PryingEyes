using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Guardaroba, il castello del conte: la sala degli armadi (quattro muri di noce, lampadari a corona), la sartoria (tavoli da taglio, manichini,
    /// stoffe), lo spogliatoio, il sepolcreto dei vampiri (sarcofagi e ceri rossi), la forgia con le vasche di lava e la terrazza all'aperto
    /// (parapetto con i merli, luna rossa, bracieri, il lago di lava sotto). I pezzi del tema sono quelli di modelli/hotel/genera_arredi4.py
    /// (gu_*: armadi, bare, tavoli da sarto, incudini, crogiolo, gargoyle, bracieri, lampioni...); restano i pezzi gia' esistenti che vanno bene cosi'
    /// (candelabri, manichini, fornaci, panche, carrelli). Le ricette scelgono i pezzi per nome.</summary>
    public static partial class RoomKits
    {
        static readonly Color LavaLight = new Color(1f, 0.42f, 0.14f);
        static readonly Side[] GuardSides = { Side.S, Side.N, Side.W, Side.E };

        // ---------------------------------------------------------------- aiuti
        /// <summary>Tiene libero il pavimento davanti alle finestre basse: un armadio contro un vetro lo oscurerebbe (le feritoie non contano).</summary>
        static void ReserveWindows(Ctx c)
        {
            foreach (Side s in GuardSides)
                foreach (var o in c.plan.OpeningsOn(c.r, s))
                {
                    if (o.b >= 0 || o.oneSided || o.sill > 1.9f || o.style == OpenStyle.Feritoia) continue;
                    float w = o.width * 0.1f + 0.4f, depth = 0.95f;
                    Vector3 inw = Inward(s);
                    bool ax = Mathf.Abs(inw.z) > 0.5f;
                    Vector3 wallPt = ax ? new Vector3(o.center * 0.1f, 0f, inw.z > 0 ? c.iz0 : c.iz1) : new Vector3(inw.x > 0 ? c.ix0 : c.ix1, 0f, o.center * 0.1f);
                    Vector3 mid = wallPt + inw * (depth * 0.5f);
                    c.occ.Add(ax ? new Rect(mid.x - w * 0.5f, mid.z - depth * 0.5f, w, depth) : new Rect(mid.x - depth * 0.5f, mid.z - w * 0.5f, depth, w));
                }
        }

        /// <summary>Una fila di arredi a muro, a spalla a spalla (un poco allargati o stretti per riempire il tratto); 'skip' lascia qualche vuoto.
        /// Prop li scarta davanti alle porte. 'alt' (se c'e') prende il posto di 'name' con probabilita' 'altChance': l'armadio con le ante aperte
        /// tra quelli chiusi. Ritorna quanti ne ha messi.</summary>
        static int WallRun(Ctx c, string name, float width, Side s, double skip, string alt = null, double altChance = 0.0)
        {
            float L = WallLen(c, s);
            int n = Mathf.Max(1, Mathf.FloorToInt((L - 0.4f) / (width + 0.04f)));
            float sx = Mathf.Clamp((L - 0.4f) / (n * width), 0.9f, 1.18f);
            int placed = 0;
            for (int i = 0; i < n; i++)
            {
                if (skip > 0.0 && c.rng.Chance(skip)) continue;
                float t = 0.2f + (i + 0.5f) * (L - 0.4f) / n;
                string pick = alt != null && c.rng.Chance(altChance) ? alt : name;
                if (OnWall(c, pick, s, t, 0.03f, sx) != null) placed++;
            }
            return placed;
        }

        static Vector3 RandomFacing(Ctx c) => Quaternion.Euler(0f, c.rng.Range(0f, 360f), 0f) * Vector3.forward;

        /// <summary>Un pezzo grosso in un angolo libero, rivolto verso il centro della stanza: il primo dei quattro angoli (si parte da uno a caso) in cui ci sta.
        /// Nullo se non ce n'e' nessuno.</summary>
        static GameObject InCorner(Ctx c, string name, float inset = 0.25f)
        {
            var def = PropDefs.Get(name);
            float ex = def.size.x * 0.5f + inset, ez = def.size.z * 0.5f + inset;
            var corners = new[]
            {
                new Vector3(c.ix0 + ex, 0f, c.iz0 + ez), new Vector3(c.ix1 - ex, 0f, c.iz0 + ez),
                new Vector3(c.ix0 + ex, 0f, c.iz1 - ez), new Vector3(c.ix1 - ex, 0f, c.iz1 - ez),
            };
            int first = c.rng.Range(0, 4);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = corners[(first + i) % 4];
                Vector3 toCenter = new Vector3(c.Center.x - p.x, 0f, c.Center.z - p.z);
                var go = Prop(c, name, p, toCenter.sqrMagnitude > 0.01f ? toCenter.normalized : Vector3.forward);
                if (go != null) return go;
            }
            return null;
        }

        /// <summary>Un lampadina nuda a un'altezza raggiungibile (anche nelle sale alte: il filo sale fino al soffitto).</summary>
        static void LowBulb(Ctx c, Vector3 pos, bool on, float intensity, float range)
        {
            pos.y = Mathf.Min(c.r.wallH - 0.6f, 3.6f);
            PendantBulb(c, pos, on, intensity, range);
        }

        // ---------------------------------------------------------------- sala degli armadi
        /// <summary>Quattro muri di armadi di noce alti fino al soffitto basso, qualche candelabro e manichini tra gli armadi sparsi (ArenaCover); lampadari a
        /// corona sull'asse lungo, appesi a un'altezza da cui illuminano il pavimento anche nelle sale-cattedrale.</summary>
        static void SalaArmadi(Ctx c)
        {
            ReserveWindows(c);
            var fr = MakeFrame(c, LongAxis(c));
            float Lf = fr.Lf, Lr = fr.Lr;
            foreach (Side s in GuardSides) WallRun(c, "gu_armadio", 1.5f, s, 0.06, "gu_armadio_aperto", 0.2);
            int nl = Mathf.Max(1, Mathf.RoundToInt(Lf / 6.5f));
            float hang = Mathf.Min(c.r.wallH - 0.2f, 7.4f);
            for (int i = 0; i < nl; i++)
                Crown(c, fr.P((i + 0.5f) / nl * Lf, Lr * 0.5f, hang), c.rng.Chance(0.6), 1f, 14f);
            // candelabri accesi vicino ai muri, tra un armadio e l'altro
            int nc = Mathf.Clamp(Mathf.RoundToInt(Lf / 5f), 1, 4);
            for (int i = 0; i < nc; i++)
                Candle(c, "chi_candelabro", fr.P(c.rng.Range(1.6f, Lf - 1.6f), c.rng.Chance(0.5) ? 1.5f : Lr - 1.5f), fr.f, c.rng.Chance(0.7));
            int dummies = c.rng.Range(1, 3);
            for (int i = 0; i < dummies; i++)
                Prop(c, "tea_manichino", fr.P(c.rng.Range(2f, Lf - 2f), c.rng.Range(2f, Lr - 2f)), RandomFacing(c));
            int stands = c.rng.Range(1, 3);
            for (int i = 0; i < stands; i++)
                Prop(c, "gu_attaccapanni", fr.P(c.rng.Range(2f, Lf - 2f), c.rng.Range(2f, Lr - 2f)), RandomFacing(c));
        }

        // ---------------------------------------------------------------- sartoria
        /// <summary>La sartoria del conte: rotoli di stoffa (e scaffali) sui due muri lunghi, due file di tavoli da sarto con i manichini accanto,
        /// macchine da cucire contro i muri, gli specchi in fondo, lampadine nude sopra i tavoli e, in un angolo, le forbici giganti.</summary>
        static void Sartoria(Ctx c)
        {
            ReserveWindows(c);
            var fr = MakeFrame(c, LongAxis(c));
            float Lf = fr.Lf, Lr = fr.Lr;
            Side a = c.W >= c.D ? Side.S : Side.W, b = c.W >= c.D ? Side.N : Side.E;
            WallRun(c, "arr_scaffale", 1.25f, a, 0.18, "gu_stoffe", 0.65);
            WallRun(c, "arr_scaffale", 1.25f, b, 0.18, "gu_stoffe", 0.65);
            int nt = Mathf.Max(1, Mathf.FloorToInt((Lf - 3.2f) / 3.4f));
            for (int i = 0; i < nt; i++)
            {
                float f = 2.2f + (i + 0.5f) * (Lf - 4.4f) / nt;
                foreach (float rr in new[] { Lr * 0.34f, Lr * 0.66f })
                {
                    var t = Prop(c, "gu_tavolo_sarto", fr.P(f, rr), fr.r, 1f, true, 0f, c.rng.Range(-6f, 6f));
                    if (t == null) continue;
                    if (c.rng.Chance(0.6))
                        Prop(c, "tea_manichino", fr.P(f + c.rng.Range(-0.6f, 0.6f), rr + (c.rng.Chance(0.5) ? 1.25f : -1.25f)), RandomFacing(c));
                    LowBulb(c, fr.P(f, rr), c.rng.Chance(0.7), 5.4f, 8f);
                }
            }
            int machines = c.rng.Range(1, 4);
            for (int i = 0, made = 0; i < 12 && made < machines; i++)
                if (OnWall(c, "gu_macchina_cucire", c.rng.Chance(0.5) ? a : b, c.rng.Range(1.2f, Lf - 1.2f), 0.05f) != null) made++;
            int mirrors = Mathf.Clamp(Mathf.FloorToInt(Lr / 2.4f), 1, 3);
            for (int i = 0; i < mirrors; i++)
                Prop(c, c.rng.Chance(0.3) ? "tea_toeletta" : "gu_specchio", fr.P(Lf - 0.30f, (i + 0.5f) * Lr / mirrors), -fr.f);
            InCorner(c, "gu_forbici");
        }

        // ---------------------------------------------------------------- spogliatoio
        /// <summary>Lo spogliatoio d'arrivo: armadietti (armadi) sui due muri lunghi, due panche al centro, gli specchi in fondo e i manichini con i
        /// cappotti. E' il posto tranquillo del piano: una lampada sola e la corona.</summary>
        static void Spogliatoio(Ctx c)
        {
            ReserveWindows(c);
            var fr = MakeFrame(c, LongAxis(c));
            float Lf = fr.Lf, Lr = fr.Lr;
            Side a = c.W >= c.D ? Side.S : Side.W, b = c.W >= c.D ? Side.N : Side.E;
            WallRun(c, "gu_armadio", 1.5f, a, 0.10, "gu_armadio_aperto", 0.15);
            WallRun(c, "gu_armadio", 1.5f, b, 0.10, "gu_armadio_aperto", 0.15);
            int np = Mathf.Clamp(Mathf.FloorToInt((Lf - 2f) / 3.4f), 1, 4);
            for (int i = 0; i < np; i++)
                Prop(c, "chi_panca", fr.P(1.8f + (i + 0.5f) * (Lf - 3.6f) / np, Lr * 0.5f), fr.r, 1f, true, 0f, c.rng.Range(-4f, 4f));
            int mirrors = Mathf.Clamp(Mathf.FloorToInt(Lr / 2.6f), 1, 3);
            for (int i = 0; i < mirrors; i++)
                Prop(c, c.rng.Chance(0.3) ? "tea_toeletta" : "gu_specchio", fr.P(Lf - 0.30f, (i + 0.5f) * Lr / mirrors), -fr.f);
            int dummies = c.rng.Range(1, 3);
            for (int i = 0; i < dummies; i++)
                Prop(c, "tea_manichino", fr.P(c.rng.Range(1.6f, Lf - 1.6f), c.rng.Range(1.8f, Lr - 1.8f)), RandomFacing(c));
            int stands = c.rng.Range(1, 3);
            for (int i = 0; i < stands; i++)
                Prop(c, "gu_attaccapanni", fr.P(c.rng.Range(1.6f, Lf - 1.6f), c.rng.Range(1.8f, Lr - 1.8f)), RandomFacing(c));
            Crown(c, fr.P(Lf * 0.5f, Lr * 0.5f, Mathf.Min(c.r.wallH - 0.2f, 7.4f)), c.rng.Chance(0.7), 1f, 12f);
        }

        // ---------------------------------------------------------------- sepolcreto
        /// <summary>Il sepolcreto dei vampiri: due file di bare di basalto (alcune con il coperchio chiuso) lungo l'asse, un candelabro a ogni capo,
        /// gargoyle in fondo, qualche urna e ceri rossi sparsi. Bassa e quasi al buio.</summary>
        static void Sepolcreto(Ctx c)
        {
            ReserveWindows(c);
            var fr = MakeFrame(c, LongAxis(c));
            float Lf = fr.Lf, Lr = fr.Lr;
            int n = Mathf.Max(1, Mathf.FloorToInt((Lf - 3f) / 3.5f));
            float f0 = 1.9f + (Lf - 3.8f - (n - 1) * 3.5f) * 0.5f;
            foreach (float rr in new[] { Lr * 0.30f, Lr * 0.70f })
            {
                for (int i = 0; i < n; i++)
                {
                    if (c.rng.Chance(0.18)) continue;
                    Prop(c, c.rng.Chance(0.4) ? "gu_bara_chiusa" : "gu_bara", fr.P(f0 + 3.5f * i, rr), fr.r, 1f, true, 0f, c.rng.Range(-4f, 4f));
                }
                foreach (float f in new[] { 0.9f, Lf - 0.9f })
                    Candle(c, "chi_candelabro", fr.P(f, rr + (rr < Lr * 0.5f ? -1.1f : 1.1f)), fr.f, c.rng.Chance(0.8));
            }
            ThemeRow(c, "gu_gargoyle", fr.P(Lf - 0.8f, 1.2f), fr.r * Mathf.Max(2.5f, (Lr - 2.4f) / 3f), 4, -fr.f, 0.3);
            int urns = c.rng.Range(1, 4);
            for (int i = 0; i < urns; i++)
                Prop(c, "gu_urna", fr.P(c.rng.Range(1.2f, Lf - 1.2f), c.rng.Range(0.9f, Lr - 0.9f)), RandomFacing(c));
            int ceri = c.rng.Range(2, 5);
            for (int i = 0; i < ceri; i++)
                Candle(c, "chi_ceri", fr.P(c.rng.Range(1.5f, Lf - 1.5f), c.rng.Range(0.9f, Lr - 0.9f)), RandomFacing(c), c.rng.Chance(0.75));
        }

        // ---------------------------------------------------------------- forgia
        /// <summary>La forgia dove si fanno le forbici: fornaci contro un muro lungo, ognuna con la sua luce di fuoco davanti alla bocca; una o due vasche
        /// di lava con il bordo di pietra nel mezzo, un crogiolo, incudini, bracieri e carrelli di scorie tra una e l'altra, e le forbici giganti in un angolo.</summary>
        static void Forgia(Ctx c)
        {
            var fr = MakeFrame(c, LongAxis(c));
            float Lf = fr.Lf, Lr = fr.Lr;
            Side fs = c.W >= c.D ? Side.N : Side.E;
            float L = WallLen(c, fs);
            int n = Mathf.Max(1, Mathf.FloorToInt((L - 0.8f) / 2.2f));
            Vector3 inw = Inward(fs);
            for (int i = 0; i < n; i++)
            {
                var f = OnWall(c, "arr_fornello", fs, 0.4f + (i + 0.5f) * (L - 0.8f) / n, 0.03f);
                if (f == null) continue;
                var pl = AddLight(c, f.transform.position + inw * 1.1f + Vector3.up * 0.9f, LavaLight, 7f, 8f, false, true, f.transform, LightType.Point, true, false);
                var pulse = f.AddComponent<LightPulse>();
                pulse.l = pl.l; pulse.baseI = pl.l.intensity; pulse.amp = 0.2f; pulse.speed = 5f;
            }
            float area = c.W * c.D;
            int pools = area > 260f ? 2 : area > 110f ? 1 : 0;
            for (int k = 0; k < pools; k++)
                for (int tries = 0; tries < 40; tries++)
                {
                    float w = c.rng.Pick(2.2f, 2.8f, 3.4f), d = c.rng.Pick(3.0f, 4.0f, 4.8f);
                    Vector3 p = c.Center + new Vector3(c.rng.Range(-0.28f, 0.28f) * c.W, 0f, c.rng.Range(-0.28f, 0.28f) * c.D);
                    if (LavaPoolAt(c, p, w, d)) break;
                }
            // il crogiolo: il pezzo grosso della forgia, un paiolo di metallo fuso sopra la brace, con la luce che pulsa come quella delle fornaci
            for (int tries = 0; tries < 30 && Lf > 6f && Lr > 6f; tries++)
            {
                var cr = Prop(c, "gu_crogiolo", fr.P(c.rng.Range(2.4f, Lf - 2.4f), c.rng.Range(2.4f, Lr - 2.4f)), RandomFacing(c));
                if (cr == null) continue;
                var cl = AddLight(c, cr.transform.position + Vector3.up * 0.7f, LavaLight, 9f, 9f, false, true, cr.transform, LightType.Point, true, false);
                var cp = cr.AddComponent<LightPulse>();
                cp.l = cl.l; cp.baseI = cl.l.intensity; cp.amp = 0.22f; cp.speed = 4.2f;
                break;
            }
            int anvils = Mathf.Clamp(Mathf.RoundToInt(area / 60f), 1, 4);
            for (int i = 0; i < anvils; i++)
                Prop(c, "gu_incudine", fr.P(c.rng.Range(1.6f, Lf - 1.6f), c.rng.Range(1.6f, Lr - 1.6f)), RandomFacing(c));
            int baskets = c.rng.Range(1, 4);
            for (int i = 0; i < baskets; i++)
                Candle(c, "gu_braciere", fr.P(c.rng.Range(1.4f, Lf - 1.4f), c.rng.Range(1.4f, Lr - 1.4f)), RandomFacing(c), true);
            int carts = c.rng.Range(1, 3);
            for (int i = 0; i < carts; i++)
                Prop(c, "arr_carrello", fr.P(c.rng.Range(1.6f, Lf - 1.6f), c.rng.Range(1.6f, Lr - 1.6f)), RandomFacing(c));
            InCorner(c, "gu_forbici");
        }

        /// <summary>Una vasca di lava: bordo di pietra alto 55 cm (collider, ostacolo per la navmesh), superficie incandescente 33 cm piu' in basso con la
        /// sua luce che pulsa e il LavaPool che ferisce chi ci entra. 'pos' e' il centro, w x d la lava (il bordo e' fuori). Falso se non c'e' posto
        /// (un metro di passaggio tutt'attorno).</summary>
        static bool LavaPoolAt(Ctx c, Vector3 pos, float w, float d)
        {
            const float rim = 0.32f, h = 0.55f, y = 0.22f;
            float W = w + rim * 2f, D = d + rim * 2f;
            if (!Free(c, pos, W + 0.9f, D + 0.9f, 0f)) return false;
            var go = new GameObject("Vasca di lava");
            go.transform.SetParent(c.props, false);
            go.transform.position = pos;
            var stone = ProcKit.Mat("mur_pietra");
            var mb = new MeshBuilder();
            mb.Box(stone, new Vector3(0f, h * 0.5f, d * 0.5f + rim * 0.5f), new Vector3(W, h, rim));
            mb.Box(stone, new Vector3(0f, h * 0.5f, -d * 0.5f - rim * 0.5f), new Vector3(W, h, rim));
            mb.Box(stone, new Vector3(w * 0.5f + rim * 0.5f, h * 0.5f, 0f), new Vector3(rim, h, d));
            mb.Box(stone, new Vector3(-w * 0.5f - rim * 0.5f, h * 0.5f, 0f), new Vector3(rim, h, d));
            const float tile = 3f;
            Vector2 UV(float lx, float lz) => new Vector2((pos.x + lx) / tile, (pos.z + lz) / tile);
            mb.Quad(GuardarobaKit.Lava(), new Vector3(-w * 0.5f, y, -d * 0.5f), new Vector3(-w * 0.5f, y, d * 0.5f), new Vector3(w * 0.5f, y, d * 0.5f), new Vector3(w * 0.5f, y, -d * 0.5f),
                    UV(-w * 0.5f, -d * 0.5f), UV(-w * 0.5f, d * 0.5f), UV(w * 0.5f, d * 0.5f), UV(w * 0.5f, -d * 0.5f), Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("Vasca di lava", out var mats);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var (cx, cz, sx, sz) in new[] { (0f, d * 0.5f + rim * 0.5f, W, rim), (0f, -d * 0.5f - rim * 0.5f, W, rim), (w * 0.5f + rim * 0.5f, 0f, rim, d), (-w * 0.5f - rim * 0.5f, 0f, rim, d) })
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(cx, h * 0.5f, cz); bc.size = new Vector3(sx, h, sz);
            }
            var pl = AddLight(c, pos + Vector3.up * 1.2f, LavaLight, 11f, 9.5f, false, true, go.transform, LightType.Point, true, false);
            var pulse = go.AddComponent<LightPulse>();
            pulse.l = pl.l; pulse.baseI = pl.l.intensity; pulse.amp = 0.16f; pulse.speed = 2.6f;
            var lp = go.AddComponent<LavaPool>();
            lp.area = new Rect(pos.x - w * 0.5f, pos.z - d * 0.5f, w, d);
            lp.surfaceY = y;
            Occupy(c, pos, W, D, 0f);
            c.propCount++; c.propArea += W * D;
            return true;
        }

        // ---------------------------------------------------------------- terrazza
        /// <summary>La terrazza all'aperto: niente soffitto (FloorBuilder.Ceiling) e un parapetto con i merli sul vuoto. Una luna rossa e viola dall'alto, il cono
        /// dentro la terrazza (la luce di un faro senza ombre passerebbe attraverso i soffitti delle sale vicine), bracieri e lampioni accesi vicino ai
        /// muri, panche rivolte al vuoto lungo il parapetto e, sparsi come ripari, gargoyle, panche, urne e alberi morti. Il fuori lo costruisce BuildGuardarobaSky.</summary>
        static void Terrazza(Ctx c)
        {
            var r = c.r;
            float half = Mathf.Min(Mathf.Max(c.W, c.D) * 0.5f, 11f);
            const float H = 7.5f;
            float ang = Mathf.Clamp(2f * Mathf.Atan(half / H) * Mathf.Rad2Deg, 70f, 118f);
            var moon = AddLight(c, c.Center + Vector3.up * H, new Color(0.62f, 0.38f, 0.78f), 105f, H + 10f, false, true, null, LightType.Spot, true, false);
            moon.l.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            moon.l.spotAngle = ang; moon.l.innerSpotAngle = ang * 0.55f;
            moon.l.name = "Luce di luna";

            // il lato con piu' vuoto: li' si guarda fuori
            Side open = Side.S; float best = -1f;
            foreach (Side s in GuardSides)
                foreach (var seg in c.plan.Segments(r, s))
                    if (seg.other < 0 && (seg.a1 - seg.a0) > best) { best = seg.a1 - seg.a0; open = s; }
            if (best > 0f)
            {
                float L = WallLen(c, open);
                int nb = Mathf.Clamp(Mathf.FloorToInt(L / 7f), 1, 3);
                for (int i = 0; i < nb; i++)
                {
                    // 'panca contro il parapetto, il davanti verso il vuoto': OnWall gira i pezzi verso la stanza, qui si rigira
                    var bn = OnWall(c, "chi_panca", open, (i + 0.5f) * L / nb, 0.30f);
                    if (bn != null) bn.transform.rotation = Quaternion.Euler(0f, YawOf(-Inward(open)), 0f);
                }
            }
            // bracieri accesi: candelabri a un metro e mezzo dai muri
            int nc = Mathf.Clamp(Mathf.RoundToInt(c.W * c.D / 70f), 2, 6);
            for (int i = 0; i < nc; i++)
            {
                float d = c.rng.Range(1.2f, 2.2f);
                Vector3 p;
                switch (c.rng.Range(0, 4))
                {
                    case 0: p = new Vector3(c.rng.Range(c.ix0 + 1.2f, c.ix1 - 1.2f), 0f, c.iz0 + d); break;
                    case 1: p = new Vector3(c.rng.Range(c.ix0 + 1.2f, c.ix1 - 1.2f), 0f, c.iz1 - d); break;
                    case 2: p = new Vector3(c.ix0 + d, 0f, c.rng.Range(c.iz0 + 1.2f, c.iz1 - 1.2f)); break;
                    default: p = new Vector3(c.ix1 - d, 0f, c.rng.Range(c.iz0 + 1.2f, c.iz1 - 1.2f)); break;
                }
                Candle(c, c.rng.Chance(0.65) ? "gu_braciere" : "gu_lampione", p, Vector3.forward, true);
            }
            // ripari sparsi: gargoyle, panche, urne con i rami secchi, alberi morti nelle vasche
            var kit = new[] { ("gu_gargoyle", 2.0), ("chi_panca", 2.0), ("gu_urna", 1.2), ("gu_albero_secco", 0.8), ("chi_statua", 0.5) };
            double kitTotal = 0;
            foreach (var k in kit) kitTotal += k.Item2;
            int cover = Mathf.Clamp(Mathf.RoundToInt(c.W * c.D / 32f), 4, 14), made = 0;
            for (int tries = 0; tries < 160 && made < cover; tries++)
            {
                double roll = c.rng.Value() * kitTotal, acc = 0; string name = kit[0].Item1;
                foreach (var k in kit) { acc += k.Item2; if (roll < acc) { name = k.Item1; break; } }
                Vector3 pos = new Vector3(c.rng.Range(c.ix0 + 1.5f, c.ix1 - 1.5f), 0f, c.rng.Range(c.iz0 + 1.5f, c.iz1 - 1.5f));
                if (Prop(c, name, pos, c.rng.Chance(0.5) ? Vector3.forward : Vector3.right, 1f, true, 0f, c.rng.Range(-14f, 14f)) != null) made++;
            }
        }
    }
}
