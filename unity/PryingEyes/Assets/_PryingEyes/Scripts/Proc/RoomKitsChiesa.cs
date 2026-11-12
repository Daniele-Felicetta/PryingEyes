using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    public static partial class RoomKits
    {
        static readonly Color CandleCol = new Color(1f, 0.70f, 0.38f);

        static void PortalCandles(Ctx c)
        {
            if (c.r.kind != RoomKind.Nartece && c.r.kind != RoomKind.Navata && c.r.kind != RoomKind.Presbiterio && c.r.kind != RoomKind.Cappella) return;
            foreach (Side side in new[] { Side.S, Side.N, Side.W, Side.E })
            {
                var openings = c.plan.OpeningsOn(c.r, side);
                var inward = Inward(side);
                foreach (var o in openings)
                {
                    if (o.style != OpenStyle.Arco || o.b < 0) continue;
                    var other = c.plan.rooms[o.a == c.r.id ? o.b : o.a];
                    if (!other.church) continue;
                    foreach (float s in new[] { o.Lo * 0.1f - 0.62f, o.Hi * 0.1f + 0.62f })
                    {
                        // L'anta aperta occupa il lato destro del nartece. Una candela dietro il legno lo illuminerebbe da dentro.
                        var along = o.alongX ? Vector3.right : Vector3.forward;
                        if (c.r.kind == RoomKind.Nartece && other.kind == RoomKind.Navata &&
                            Vector3.Dot(Vector3.Cross(Vector3.up, -inward), along) * (s - o.center * 0.1f) > 0f) continue;
                        if (s < (o.alongX ? c.ix0 : c.iz0) + 0.35f || s > (o.alongX ? c.ix1 : c.iz1) - 0.35f) continue;
                        if (openings.Exists(h => s > h.Lo * 0.1f - 0.18f && s < h.Hi * 0.1f + 0.18f)) continue;
                        var wall = o.alongX ? new Vector3(s, 1.67f, o.line * 0.1f) : new Vector3(o.line * 0.1f, 1.67f, s);
                        Sconce(c, wall + inward * (FloorBuilder.TH + 0.008f), inward, true, 0.44f, 4.8f, true);
                    }
                }
            }
        }

        /// <summary>Candelabro o stand di ceri: fiamme come mesh a parte (materiale "fiamma"), una luce calda in cima. 'objective'
        /// = uno di quelli da accendere per aprire l'ascensore (parte spento, si accende con F).</summary>
        static Candelabro Candle(Ctx c, string name, Vector3 pos, Vector3 face, bool lit, bool objective = false, bool check = true)
        {
            var go = Prop(c, name, pos, face, 1f, check);
            if (go == null) return null;
            var def = PropDefs.Get(name);
            var cd = go.AddComponent<Candelabro>();
            cd.objective = objective;
            var flames = new List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.StartsWith("fiamma")) { flames.Add(r); break; }
            cd.flames = flames.ToArray();
            // i candelabri dell'obiettivo, accesi, sono i fari del piano: luce piu' forte delle candele d'arredo
            var pl = AddLight(c, go.transform.position + Vector3.up * (def.size.y + 0.1f), CandleCol, (name == "chi_ceri" ? 2.6f : 3.8f) * (objective ? 1.9f : 1f), objective ? 8f : 6.5f, true, lit, go.transform);
            cd.glow = pl.l;
            cd.pool = pl;
            // l'alone freddo di chi aspetta di essere acceso non si abbassa con il buio: e' l'unico indizio da lontano
            if (objective) cd.hint = AddLight(c, go.transform.position + Vector3.up * (def.size.y * 0.55f), new Color(0.45f, 0.62f, 1f), 2.4f, 6.5f, false, !lit, go.transform, LightType.Point, false);
            cd.Setup(lit);
            if (objective) { c.fi.candles.Add(cd); Interact.Add(cd); }
            return cd;
        }

        // ---------------------------------------------------------------- navata
        static void Navata(Ctx c)
        {
            Vector3 ax = ChurchAxis(c);
            var fr = MakeFrame(c, ax);
            float Lf = fr.Lf, Lr = fr.Lr;
            float aisleHalf = 1.25f;
            // la navata centrale resta sempre libera, e davanti a ogni apertura dei muri lunghi (cappelle, arcate) si lascia una corsia fino
            // al centro: senza, le file di panche chiudono le cappelle in tasche
            Rect Box2(Vector3 p, Vector3 q) => new Rect(Mathf.Min(p.x, q.x), Mathf.Min(p.z, q.z), Mathf.Abs(p.x - q.x), Mathf.Abs(p.z - q.z));
            c.occ.Add(Box2(fr.P(0f, Lr * 0.5f - 1.15f), fr.P(Lf, Lr * 0.5f + 1.15f)));
            foreach (Side sd in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var o in c.plan.OpeningsOn(c.r, sd))
                {
                    if (o.b < 0 || o.oneSided) continue;
                    Vector3 inw = Inward(sd);
                    if (Mathf.Abs(Vector3.Dot(inw, fr.r)) < 0.5f) continue;                 // solo i muri lunghi
                    Vector3 wp = Mathf.Abs(inw.z) > 0.5f ? new Vector3(o.center * 0.1f, 0f, inw.z > 0 ? c.iz0 : c.iz1) : new Vector3(inw.x > 0 ? c.ix0 : c.ix1, 0f, o.center * 0.1f);
                    float fo = Vector3.Dot(wp - fr.o, fr.f);
                    // Nelle arcate rimane un corridoio centrale di 2,5 m. I blocchi di panche stanno tra i corridoi.
                    float hw = o.style == OpenStyle.Arcata ? 1.25f : o.width * 0.05f + 0.55f;
                    bool nearZero = Vector3.Dot(wp - fr.o, fr.r) < Lr * 0.5f;
                    c.occ.Add(Box2(fr.P(fo - hw, nearZero ? 0f : Lr * 0.5f), fr.P(fo + hw, nearZero ? Lr * 0.5f : Lr)));
                }
            // Lascia spazio anche dal lato del muro: ReserveOpenings occupa 1,1 m davanti alle arcate.
            float blockLen = Lr * 0.5f - aisleHalf - 1.18f - 0.26f;
            float sx = Mathf.Clamp(blockLen / PropDefs.Get("chi_panca").size.x, 0.42f, 1f);
            float pewW = 2.6f * sx;
            float f0 = 2.8f, fEnd = Lf - 7.8f;
            int rows = Mathf.Max(0, Mathf.FloorToInt((fEnd - f0) / 1.38f) + 1);
            int crookRow = rows > 3 && c.rng.Chance(0.55) ? c.rng.Range(1, rows - 1) : -1;     // una panca storta, mai nella navata centrale
            float crookSide = c.rng.Chance(0.5) ? -1f : 1f;
            for (int i = 0; i < rows; i++)
            {
                float f = f0 + i * 1.38f;
                foreach (float side in new[] { -1f, 1f })
                {
                    if (c.rng.Chance(0.08)) continue;             // panca portata via
                    if (i == crookRow && side == crookSide)
                    {
                        float rc = Lr * 0.5f + side * (aisleHalf + pewW * 0.5f + 0.15f);
                        Prop(c, "chi_panca", fr.P(f + 0.1f, rc), fr.f, sx, true, 0f, side * c.rng.Range(14f, 24f));
                        continue;
                    }
                    float rr = Lr * 0.5f + side * (aisleHalf + pewW * 0.5f + 0.12f + c.rng.Range(0f, 0.12f));
                    // la panca ha il davanti verso l'altare: +f
                    Prop(c, "chi_panca", fr.P(f, rr), fr.f, sx, true, 0f, c.rng.Range(-2.5f, 2.5f));
                }
            }
            // candelabri: all'ingresso e davanti all'altare, ai lati della navata centrale (mai dentro)
            foreach (float f in new[] { 1.3f, Lf - 6.2f, Lf - 4.4f })
                foreach (float side in new[] { -1f, 1f })
                    if (c.rng.Chance(0.75)) Candle(c, "chi_candelabro", fr.P(f, Lr * 0.5f + side * 2.2f), fr.r * -side, c.rng.Chance(0.65), false, true);
            // lampadari a corona sospesi alle capriate
            int nl = Mathf.Max(1, Mathf.RoundToInt((Lf - 6f) / 8f));
            for (int i = 0; i < nl; i++)
                Crown(c, fr.P(5f + i * ((Lf - 11f) / Mathf.Max(1, nl - 1) + (nl == 1 ? 1f : 0f)), Lr * 0.5f, c.r.wallH - 0.5f), c.rng.Chance(0.6));
            // pulpito contro il muro destro vicino all'altare
            Prop(c, "chi_pulpito", fr.P(Lf - 3.4f, Lr - 0.95f), -fr.r);
            if (c.rng.Chance(0.5)) Prop(c, "chi_statua", fr.P(Lf - 0.7f, 0.9f), fr.r);
        }

        /// <summary>Lampadario a corona di ferro con le candele, appeso a una catena. Luce calda grande, spesso spento.</summary>
        static void Crown(Ctx c, Vector3 top, bool lit, float gain = 1f, float range = 13f)
        {
            float drop = Mathf.Clamp(top.y - 2.7f, 0.4f, 3.4f);
            var g = Prop(c, "chi_lampadario", top + Vector3.down * drop, Vector3.forward, 1f, false);
            if (g == null) return;
            var chain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Util.Kill(chain.GetComponent<Collider>());
            chain.name = "Catena";
            chain.transform.SetParent(g.transform, false);
            chain.transform.localPosition = new Vector3(0f, 0.8f + drop * 0.5f, 0f);
            chain.transform.localScale = new Vector3(0.03f, drop * 0.5f, 0.03f);
            chain.GetComponent<MeshRenderer>().sharedMaterial = ProcKit.Mat("ferro");
            chain.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (lit)
            {
                var pl = AddLight(c, g.transform.position + Vector3.up * 0.3f, CandleCol, 12f * gain, range, true, true, g.transform);
                g.AddComponent<LampFlicker>().Init(pl);
            }
            else foreach (var r in g.GetComponentsInChildren<MeshRenderer>()) { foreach (var m in r.sharedMaterials) if (m != null && m.name.StartsWith("fiamma")) { r.enabled = false; break; } }
        }

        // ---------------------------------------------------------------- presbiterio
        static void Presbiterio(Ctx c)
        {
            Vector3 ax = ChurchAxis(c);
            var fr = MakeFrame(c, ax);
            float Lf = fr.Lf, Lr = fr.Lr;
            // altare e pala contro il muro in fondo
            Prop(c, "chi_pala", fr.P(Lf - 0.3f, Lr * 0.5f), -fr.f, 1f, !FarWallBlind(c, fr.f));    // senza controllo degli ingombri solo se il muro e' cieco
            Prop(c, "chi_altare", fr.P(Lf - 2.2f, Lr * 0.5f), -fr.f);
            Candle(c, "chi_candelabro", fr.P(Lf - 2.0f, Lr * 0.5f - 2.0f), -fr.f, true);
            Candle(c, "chi_candelabro", fr.P(Lf - 2.0f, Lr * 0.5f + 2.0f), -fr.f, true);
            Candle(c, "chi_candelabro", fr.P(Lf - 4.2f, Lr * 0.5f - 1.4f), -fr.f, c.rng.Chance(0.6));
            Candle(c, "chi_candelabro", fr.P(Lf - 4.2f, Lr * 0.5f + 1.4f), -fr.f, c.rng.Chance(0.6));
            // stalli del coro lungo i muri laterali
            int ns = Mathf.Max(1, Mathf.FloorToInt((Lf - 3.2f) / 2.9f));
            for (int i = 0; i < ns; i++)
                foreach (float side in new[] { 0f, 1f })
                {
                    float f = 1.0f + i * 2.9f + 1.45f;
                    Vector3 face = side == 0f ? fr.r : -fr.r;
                    Prop(c, "chi_stallo", fr.P(f, side == 0f ? 0.55f : Lr - 0.55f), face);
                }
            // organo contro un muro laterale, se c'e' posto
            if (c.rng.Chance(0.6)) Prop(c, "chi_organo", fr.P(1.0f + 1.4f, c.rng.Chance(0.5) ? 0.8f : Lr - 0.8f), fr.r, 1f, true);
            Prop(c, "chi_statua", fr.P(Lf - 0.7f, 0.9f), fr.r);
            Prop(c, "chi_statua", fr.P(Lf - 0.7f, Lr - 0.9f), -fr.r);
            // lampada del santissimo: rossa, bassa
            AddLight(c, fr.P(Lf - 2.4f, Lr * 0.5f, 3.2f), new Color(1f, 0.15f, 0.1f), 4f, 7f, false);
        }

        // ---------------------------------------------------------------- cappella
        static void Cappella(Ctx c)
        {
            Side ds = AltarEntrance(c);       // griglia: l'altare sta contro un muro senza porte (RoomKitsArena.cs)
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            Vector3 toDoor = -fr.f;
            var pg = Prop(c, "chi_pala", fr.P(Lf - 0.28f, Lr * 0.5f), toDoor, Mathf.Min(1f, (Lr - 0.3f) / 3.4f), !FarWallBlind(c, fr.f), 0f);
            Prop(c, "chi_altare", fr.P(Lf - 1.15f, Lr * 0.5f), toDoor, Mathf.Min(1f, (Lr - 0.4f) / 2.4f));
            Candle(c, "chi_ceri", fr.P(Lf - 1.0f, Lr * 0.5f - 1.5f), toDoor, c.rng.Chance(0.7));
            Candle(c, "chi_ceri", fr.P(Lf - 1.0f, Lr * 0.5f + 1.5f), toDoor, c.rng.Chance(0.7));
            Prop(c, "chi_inginocchiatoio", fr.P(Lf - 2.4f, Lr * 0.5f - 0.5f), fr.f, 1f, true, 0f, c.rng.Range(-10f, 10f));
            if (c.rng.Chance(0.6)) Prop(c, "chi_inginocchiatoio", fr.P(Lf - 2.6f, Lr * 0.5f + 0.7f), fr.f, 1f, true, 0f, c.rng.Range(-10f, 10f));
            if (c.rng.Chance(0.5)) Prop(c, "chi_statua", fr.P(Lf - 0.5f, 0.5f), fr.r);
        }

        // ---------------------------------------------------------------- nartece
        static void Nartece(Ctx c)
        {
            Vector3 ax = ChurchAxis(c);
            var fr = MakeFrame(c, ax);
            float Lf = fr.Lf, Lr = fr.Lr;
            Prop(c, "chi_fonte", fr.P(Lf * 0.5f, Lr * 0.5f - 4.5f), fr.f);
            Prop(c, "chi_fonte", fr.P(Lf * 0.5f, Lr * 0.5f + 4.5f), fr.f, 1f, true, 0f, 37f);
            Prop(c, "chi_statua", fr.P(Lf * 0.62f, 0.63f), fr.r);
            Prop(c, "chi_statua", fr.P(Lf * 0.38f, Lr - 0.58f), -fr.r);
            Candle(c, "chi_ceri", fr.P(0.4f, Lr * 0.5f - 2.0f), fr.f, c.rng.Chance(0.7));
            Candle(c, "chi_ceri", fr.P(0.4f, Lr * 0.5f + 2.0f), fr.f, c.rng.Chance(0.7));
            for (int i = 0; i < 2; i++)
                Prop(c, "chi_panca", fr.P(0.55f, Lr * 0.5f + (i == 0 ? -5.5f : 5.5f)), fr.f);
            Candle(c, "chi_candelabro", fr.P(Lf * 0.54f, 0.85f), fr.f, c.rng.Chance(0.8), false, true);
            Candle(c, "chi_candelabro", fr.P(Lf * 0.54f, Lr - 0.85f), fr.f, c.rng.Chance(0.8), false, true);
        }

        // ---------------------------------------------------------------- navata laterale
        static void Navatella(Ctx c)
        {
            Vector3 ax = ChurchAxis(c);
            var fr = MakeFrame(c, ax);
            float Lf = fr.Lf, Lr = fr.Lr;
            // quale lato e' l'arcata? quello verso la navata
            var nave = c.plan.FindIn(RoomKind.Navata, c.r.unit);
            Vector3 toNave = nave != null ? new Vector3(nave.CenterX - c.r.CenterX, 0f, nave.CenterZ - c.r.CenterZ) : fr.r;
            bool arcadeRight = Vector3.Dot(toNave, fr.r) > 0f;
            float outerR = arcadeRight ? 0f : Lr;
            Vector3 inw = arcadeRight ? fr.r : -fr.r;       // dal muro esterno verso la navata
            float sgn = arcadeRight ? 1f : -1f;
            int bays = Mathf.RoundToInt(Lf / FloorBuilder.Bay);
            for (int b = 1; b < bays; b++)
            {
                float f = b * FloorBuilder.Bay;
                float roll = c.rng.Range(0f, 1f);
                if (roll < 0.35f) Prop(c, "chi_confessionale", fr.P(f, outerR + sgn * 0.62f), inw);
                else if (roll < 0.6f) Prop(c, "chi_statua", fr.P(f, outerR + sgn * 0.5f), inw);
                else if (roll < 0.8f) Candle(c, "chi_ceri", fr.P(f, outerR + sgn * 0.3f), inw, c.rng.Chance(0.7));
            }
            // un po' di candelabri vicino alle arcate
            for (int b = 0; b < bays; b++)
                if (c.rng.Chance(0.3)) Candle(c, "chi_candelabro", fr.P((b + 0.5f) * FloorBuilder.Bay + 1.0f, Lr * 0.5f), inw, c.rng.Chance(0.6), false, false);
            if (c.rng.Chance(0.5)) Prop(c, "chi_panca", fr.P(Lf * c.rng.Range(0.2f, 0.8f), outerR + sgn * 0.5f), inw);
        }

        // ---------------------------------------------------------------- sacrestia
        static void Sacrestia(Ctx c)
        {
            Side ds = MainSide(c, RoomKind.Presbiterio);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            Prop(c, "arr_armadio", fr.P(Lf - 0.35f, 0.9f), -fr.f);
            Prop(c, "arr_armadio", fr.P(Lf - 0.35f, Lr - 0.9f), -fr.f);
            Prop(c, "arr_scrivania", fr.P(Lf * 0.5f, Lr * 0.5f), fr.f);
            Prop(c, "arr_sedia", fr.P(Lf * 0.5f - 0.8f, Lr * 0.5f + c.rng.Range(-0.2f, 0.2f)), fr.f, 1f, true, 0f, c.rng.Range(-40f, 40f));
            Candle(c, "chi_ceri", fr.P(0.4f, 0.5f), fr.f, c.rng.Chance(0.75));
            if (c.rng.Chance(0.5)) Prop(c, "chi_inginocchiatoio", fr.P(1.2f, Lr - 0.6f), -fr.r);
            PendantBulb(c, new Vector3(c.Center.x, c.r.wallH - 0.6f, c.Center.z), c.rng.Chance(0.5), 5f, 8f);
        }

        // ---------------------------------------------------------------- cripta
        static void Cripta(Ctx c)
        {
            Vector3 a = LongAxis(c);
            var fr = MakeFrame(c, a);
            float Lf = fr.Lf, Lr = fr.Lr;
            int n = Mathf.Max(1, Mathf.FloorToInt((Lf - 1.2f) / 3.0f));
            foreach (float rrf in new[] { 0.28f, 0.72f })
                for (int i = 0; i < n; i++)
                {
                    if (c.rng.Chance(0.12)) continue;
                    // il sarcofago e' lungo lungo la sua larghezza (X): lo si ruota perche' segua l'asse
                    Prop(c, "chi_sarcofago", fr.P(1.2f + 1.5f + i * 3.0f, Lr * rrf), fr.r, 1f, true, 0f, c.rng.Range(-4f, 4f));
                }
            for (int i = 0; i < 3; i++)
                Candle(c, "chi_ceri", fr.P(c.rng.Range(0.8f, Lf - 0.8f), c.rng.Chance(0.5) ? 0.4f : Lr - 0.4f), fr.r, c.rng.Chance(0.6));
            AddLight(c, c.Center + Vector3.up * (c.r.wallH - 0.4f), new Color(0.9f, 0.68f, 0.45f), 4f, 8f, true);
        }

        // ---------------------------------------------------------------- campanile
        static void Campanile(Ctx c)
        {
            Side ds = MainSide(c);
            var fr = MakeFrame(c, Inward(ds));
            Candle(c, "chi_candelabro", fr.P(fr.Lf * 0.5f, fr.Lr * 0.5f), fr.f, c.rng.Chance(0.7), false, false);
            AddLight(c, c.Center + Vector3.up * 4.4f, new Color(0.7f, 0.8f, 1f), 4f, 10f, false);
        }
    }
}
