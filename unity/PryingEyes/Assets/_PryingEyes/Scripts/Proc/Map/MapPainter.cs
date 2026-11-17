using System;
using System.Collections.Generic;

namespace PryingEyes.Proc.MapArt
{
    /// <summary>I colori della mappa: inchiostro, matita rossa, ceralacca, ottone (quello del menu) e l'acquerello dei ruoli.</summary>
    public static class MapStyle
    {
        public static readonly Col Ink = Col.F(0.070f, 0.052f, 0.040f);
        public static readonly Col InkFaint = Col.F(0.12f, 0.09f, 0.065f, 0.55f);
        public static readonly Col Red = Col.F(0.66f, 0.06f, 0.05f);
        public static readonly Col Blood = Col.F(0.42f, 0.02f, 0.025f);
        public static readonly Col Wax = Col.F(0.50f, 0.045f, 0.055f);
        public static readonly Col WaxHi = Col.F(0.78f, 0.21f, 0.16f);
        public static readonly Col Brass = Col.F(0.69f, 0.53f, 0.30f);
        public static readonly Col Steel = Col.F(0.33f, 0.36f, 0.40f);
        public static readonly Col Smudge = Col.F(0.038f, 0.030f, 0.026f);
        public static readonly Col Paper = Col.F(0.82f, 0.71f, 0.50f);
        public static readonly Col Verdigris = Col.F(0.19f, 0.36f, 0.33f);

        /// <summary>Il colore del ruolo: lo stesso degli stipiti delle porte (FloorBuilder.FrameColor; la prova -maptest controlla che siano uguali).</summary>
        public static Col RoleTint(RoomType t)
        {
            switch (t)
            {
                case RoomType.Candela: return Col.F(1.00f, 0.42f, 0.10f);
                case RoomType.Negozio: return Col.F(1.00f, 0.78f, 0.22f);
                case RoomType.Biblioteca: return Col.F(0.62f, 0.40f, 1.00f);
                case RoomType.Boss: return Col.F(0.90f, 0.10f, 0.14f);
                case RoomType.Segreta: return Col.F(0.30f, 0.95f, 0.85f);
                case RoomType.Partenza: return Col.F(0.35f, 1.00f, 0.55f);
                case RoomType.Guardia: return Col.F(0.55f, 0.66f, 0.80f);
                default: return Col.F(0f, 0f, 0f, 0f);
            }
        }
    }

    /// <summary>Il pittore della pianta: da una MapScene (cosa si sa del piano) disegna le tre mesh e le etichette di ogni parte. E' puro (niente
    /// Unity): lo usa il gioco, e planlab lo rasterizza in un PNG per vederlo. Ogni stanza si dipinge da sola, cosi' chi lo usa puo' tenere in
    /// memoria quelle che non sono cambiate.</summary>
    public sealed partial class MapPainter
    {
        /// <summary>Metri -> coordinata di texture dell'acquerello.</summary>
        public const float WashUv = 0.115f;
        public const float WallW = 0.46f, ThinW = 0.16f, FineW = 0.10f;

        public readonly MapScene s;
        readonly Plan plan;
        readonly Dictionary<Opening, int> openingIndex = new Dictionary<Opening, int>();
        readonly List<MapProp>[] propsByRoom;

        public MapPainter(MapScene scene)
        {
            s = scene; plan = scene.plan;
            for (int i = 0; i < plan.openings.Count; i++) openingIndex[plan.openings[i]] = i;
            propsByRoom = new List<MapProp>[plan.rooms.Count];
            foreach (var p in scene.props)
            {
                if (p.room < 0 || p.room >= propsByRoom.Length) continue;
                (propsByRoom[p.room] ?? (propsByRoom[p.room] = new List<MapProp>())).Add(p);
            }
        }

        public static R2 RectOf(Room r) => new R2(r.x0 * 0.1f, r.z0 * 0.1f, r.x1 * 0.1f, r.z1 * 0.1f);

        /// <summary>L'ingombro di tutto il piano, in metri, con un margine.</summary>
        public R2 PlanBounds(float margin = 0f)
        {
            bool any = false; R2 b = default;
            foreach (var r in plan.rooms)
            {
                var q = RectOf(r);
                b = any ? b.Union(q) : q; any = true;
            }
            return b.Grow(margin);
        }

        // ------------------------------------------------------------------ una stanza
        public Art Room(int id)
        {
            var art = new Art();
            var r = plan.rooms[id];
            if (r.IsLift) return art;
            if (s.seen[id] == RoomSeen.Seen) PaintSeen(r, art);
            else if (s.seen[id] == RoomSeen.Rumor) PaintRumor(r, art);
            return art;
        }

        static List<V2> Jitter(List<V2> poly, float amp, int seed)
        {
            var o = new List<V2>(poly.Count);
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                o.Add(new V2(p.x + Noise.V1(p.x * 0.31f + p.y * 0.17f, seed) * amp, p.y + Noise.V1(p.y * 0.29f - p.x * 0.13f, seed + 3) * amp));
            }
            return o;
        }

        Col WashOf(Room r)
        {
            var tint = MapStyle.RoleTint(r.type);
            Col c;
            if (tint.a == 0) c = Col.F(0.54f, 0.42f, 0.27f, 0.42f);
            else
            {
                // l'acquerello lasciato troppo tempo in cantina: il colore del ruolo c'e', ma sbiadito e sporco
                float tr = tint.r / 255f, tg = tint.g / 255f, tb = tint.b / 255f;
                float lum = 0.30f * tr + 0.59f * tg + 0.11f * tb, keep = 0.58f;
                tr = lum + (tr - lum) * keep; tg = lum + (tg - lum) * keep; tb = lum + (tb - lum) * keep;
                c = Col.Lerp(Col.F(0.56f, 0.45f, 0.31f), Col.F(tr, tg, tb), 0.52f).A(0.50f);
            }
            if (r.church) c = Col.Lerp(c, Col.F(0.40f, 0.46f, 0.52f), 0.38f).A(c.a / 255f);
            return c;
        }

        void PaintSeen(Room r, Art art)
        {
            var rect = RectOf(r);
            var wash = new Pen(art.wash, r.seed) { wobble = 0f };
            var ink = new Pen(art.ink, r.seed + 1);
            float rad = r.cornerRadius;

            // l'acquerello, con il bordo che sbava un poco oltre i muri
            var poly = Jitter(Pen.RoundedRect(rect.Grow(0.18f), rad + 0.18f, 6), 0.34f, r.seed);
            var wc = WashOf(r);
            wash.FillTex(poly, wc, WashUv);
            // il pigmento si raccoglie lungo il bordo: una riga appena piu' scura
            ink.Path(poly, true, 0.55f, wc.Mul(0.55f).MulA(0.50f), 0.5f);
            PaintShade(r, art);

            PaintFloorPattern(r, rect, ink);
            PaintProps(r, ink);
            PaintGalleries(r, ink);
            PaintWalls(r, ink);

            // il nome, e il ruolo sopra (quelli piccoli non hanno spazio)
            float area = rect.W * rect.H;
            if (area >= 30f)
            {
                var c = rect.Center;
                string role = RoomNames.Role(r.type);
                art.labels.Add(new MapLabel { pos = new V2(c.x, c.y - (role.Length > 0 ? 1.2f : 0f)), text = RoomNames.Of(r), kind = LabelKind.Room, owner = r.id, color = MapStyle.Ink });
                if (role.Length > 0)
                    art.labels.Add(new MapLabel { pos = new V2(c.x, c.y + 2.6f), text = role, kind = LabelKind.Role, owner = r.id, color = Col.Lerp(MapStyle.Ink, MapStyle.RoleTint(r.type), 0.35f) });
            }
        }

        void PaintRumor(Room r, Art art)
        {
            var rect = RectOf(r);
            var wash = new Pen(art.wash, r.seed + 7) { wobble = 0f };
            var ink = new Pen(art.ink, r.seed + 7) { tremor = 0.30f };
            float rad = r.cornerRadius;
            // una macchia d'inchiostro che copre la stanza, con il bordo mangiato: macchiata dalla texture dell'acquerello e piu' nera al centro
            var poly = Jitter(Resample(Pen.RoundedRect(rect.Grow(-0.35f), rad, 5), true, 2.2f), 0.75f, r.seed + 11);
            ink.FillTexGrad(poly, MapStyle.Smudge.A(0.80f), MapStyle.Smudge.A(0.34f), WashUv);
            wash.FillTexGrad(poly, MapStyle.Smudge.A(0.70f), MapStyle.Smudge.A(0.40f), WashUv);
            Ribbon(art.wash, poly, true, 1f, 3.0f, 0.42f, MapStyle.Smudge, false, false, r.seed + 14);
            // piu' nera al centro: macchie morbide e larghe (il buio si addensa dove non si e' mai guardato)
            float minSide = Math.Min(rect.W, rect.H);
            for (int i = 0; i < 3; i++)
                ink.Glow(rect.Center + new V2((Noise.R(i, 60, r.seed) - 0.5f) * rect.W * 0.35f, (Noise.R(i, 61, r.seed) - 0.5f) * rect.H * 0.35f),
                    minSide * (0.42f + 0.12f * i), MapStyle.Smudge.A(0.40f), 24);
            // le dita dell'inchiostro: macchie morbide lungo il bordo, dentro e fuori
            var rimPts = Resample(poly, true, 1.6f);
            int fingers = Math.Max(6, rimPts.Count / 2);
            for (int i = 0; i < fingers; i++)
            {
                V2 p = rimPts[(int)(Noise.R(i, 70, r.seed) * rimPts.Count) % rimPts.Count];
                V2 toward = (rect.Center - p).Norm;
                float rr = 1.0f + Noise.R(i, 71, r.seed) * 2.6f;
                ink.Glow(p - toward * (Noise.R(i, 72, r.seed) * 1.6f - 0.4f), rr, MapStyle.Smudge.A(0.24f), 12);
            }
            // sopra, il tratteggio fitto di chi ha cancellato a penna
            var hatch = new Pen(art.ink, r.seed + 9) { wobble = 0.6f };
            hatch.Hatch(rect.Grow(-1.2f), 0.72f, 1.05f, 0.08f, Col.F(0.60f, 0.48f, 0.30f, 0.30f), r.seed, 0.12f);
            hatch.Hatch(rect.Grow(-1.8f), -0.62f, 1.9f, 0.07f, Col.F(0.60f, 0.48f, 0.30f, 0.16f), r.seed + 5, 0.30f);
            var rim = new Pen(art.ink, r.seed + 13);
            for (int i = 0; i < poly.Count; i++)
            {
                if ((i & 1) == 0) continue;
                rim.Dashed(poly[i], poly[(i + 1) % poly.Count], 0.14f, Col.F(0.60f, 0.48f, 0.30f, 0.55f), 1.2f, 0.9f);
            }
            var c = rect.Center;
            art.labels.Add(new MapLabel { pos = c, text = "?", kind = LabelKind.Question, owner = r.id, color = Col.F(0.80f, 0.70f, 0.52f, 0.75f) });
            // gli occhi: due o tre, mai al centro (li' c'e' il punto interrogativo)
            int n = 2 + (int)(Noise.R(r.id, 4, s.seed) * 2.5f);
            for (int i = 0; i < n; i++)
            {
                float ang = Noise.R(r.id, 20 + i, s.seed) * 6.2831853f, dist = (0.22f + Noise.R(r.id, 30 + i, s.seed) * 0.20f) * minSide;
                art.eyes.Add(new MapEye { pos = c + V2.Polar(ang, dist), size = Math.Min(5.2f, 2.2f + minSide * 0.07f) * (0.8f + 0.4f * Noise.R(r.id, 40 + i, s.seed)), owner = r.id, phase = Noise.R(r.id, 50 + i, s.seed) * 20f });
            }
        }

        // ------------------------------------------------------------------ il pavimento
        void PaintFloorPattern(Room r, R2 rect, Pen ink)
        {
            var inner = rect.Grow(-0.4f);
            if (inner.W <= 0.5f || inner.H <= 0.5f) return;
            var faint = MapStyle.Ink.A(0.075f);
            switch (plan.theme)
            {
                case Theme.Sanatorio:
                    ink.Hatch(inner, 0f, 2.2f, 0.07f, faint, r.seed + 20);
                    ink.Hatch(inner, 1.5707963f, 2.2f, 0.07f, faint, r.seed + 21);
                    break;
                case Theme.Teatro:
                    ink.Hatch(inner, r.kind == RoomKind.Platea ? 0f : 0.7854f, r.kind == RoomKind.Platea ? 1.6f : 2.0f, 0.07f, faint, r.seed + 22);
                    break;
                case Theme.Guardaroba:
                    ink.Hatch(inner, 0.5f, 2.6f, 0.07f, faint, r.seed + 23);
                    ink.Hatch(inner, -0.5f, 2.6f, 0.07f, faint, r.seed + 24);
                    break;
                default:
                    if (r.church)
                    {
                        ink.Hatch(inner, 0f, 3.2f, 0.07f, faint, r.seed + 25);
                        ink.Hatch(inner, 1.5707963f, 3.2f, 0.07f, faint, r.seed + 26);
                    }
                    else ink.Hatch(inner, 0.7854f, 1.8f, 0.07f, faint, r.seed + 27);
                    break;
            }
        }

        /// <summary>Le passerelle: due righe tratteggiate lungo il percorso del piano di sopra, con una freccia a ogni estremo (le scale).</summary>
        void PaintGalleries(Room r, Pen ink)
        {
            foreach (var g in s.galleries)
            {
                if (g.room != r.id || g.points == null || g.points.Length < 2) continue;
                var dash = MapStyle.Ink.A(0.60f);
                for (int i = 0; i + 1 < g.points.Length; i++)
                {
                    V2 a = g.points[i], b = g.points[i + 1];
                    V2 nrm = (b - a).Norm.Perp * 1.0f;
                    ink.Dashed(a + nrm, b + nrm, FineW, dash, 0.9f, 0.55f);
                    ink.Dashed(a - nrm, b - nrm, FineW, dash, 0.9f, 0.55f);
                }
                // frecce di salita ai due estremi
                Arrow(ink, g.points[0], (g.points[1] - g.points[0]).Norm, 1.5f, dash);
                int n = g.points.Length;
                Arrow(ink, g.points[n - 1], (g.points[n - 2] - g.points[n - 1]).Norm, 1.5f, dash);
            }
        }

        static void Arrow(Pen ink, V2 tip, V2 dir, float size, Col c)
        {
            V2 side = dir.Perp;
            ink.Line(tip, tip + dir * size + side * size * 0.55f, FineW * 1.3f, c, false);
            ink.Line(tip, tip + dir * size - side * size * 0.55f, FineW * 1.3f, c, false);
        }

        // ------------------------------------------------------------------ i muri
        static readonly Side[] Sides = { Side.S, Side.N, Side.W, Side.E };

        bool DrawsWall(Room r, int other)
        {
            if (other < 0) return true;
            var so = s.seen[other];
            if (so != RoomSeen.Seen) return true;
            return r.id < other;
        }

        void PaintWalls(Room r, Pen ink)
        {
            float radius = r.cornerRadius;
            foreach (var side in Sides)
            {
                bool alongX = Plan.AlongX(side);
                int line = Plan.Line(r, side);
                Plan.Span(r, side, out int lo, out int hi);
                var segs = plan.Segments(r, side);
                var ops = plan.OpeningsOn(r, side);
                ops.Sort((p, q) => p.center.CompareTo(q.center));
                foreach (var seg in segs)
                {
                    if (!DrawsWall(r, seg.other)) continue;
                    // le aperture di questo tratto
                    float cursor = seg.a0 * 0.1f;
                    float end = seg.a1 * 0.1f;
                    if (radius > 0f)
                    {
                        if (seg.a0 == lo) cursor += radius;
                        if (seg.a1 == hi) end -= radius;
                    }
                    foreach (var o in ops)
                    {
                        if (o.center < seg.a0 || o.center > seg.a1) continue;
                        float g0 = Math.Max(cursor, o.Lo * 0.1f), g1 = Math.Min(end, o.Hi * 0.1f);
                        if (g0 > cursor) WallRun(ink, alongX, line * 0.1f, cursor, g0);
                        cursor = Math.Max(cursor, g1);
                        PaintOpening(r, o, side, ink);
                    }
                    if (end > cursor) WallRun(ink, alongX, line * 0.1f, cursor, end);
                }
            }
            if (radius > 0f) RoundedCorners(r, ink, radius);
        }

        void WallRun(Pen ink, bool alongX, float line, float a, float b)
        {
            if (b - a < 0.05f) return;
            if (alongX) ink.Ink(new V2(a, line), new V2(b, line), WallW, MapStyle.Ink);
            else ink.Ink(new V2(line, a), new V2(line, b), WallW, MapStyle.Ink);
        }

        void RoundedCorners(Room r, Pen ink, float radius)
        {
            var rect = RectOf(r);
            ink.Arc(new V2(rect.x1 - radius, rect.y0 + radius), radius, -1.5707963f, 0f, WallW, MapStyle.Ink);
            ink.Arc(new V2(rect.x1 - radius, rect.y1 - radius), radius, 0f, 1.5707963f, WallW, MapStyle.Ink);
            ink.Arc(new V2(rect.x0 + radius, rect.y1 - radius), radius, 1.5707963f, 3.1415927f, WallW, MapStyle.Ink);
            ink.Arc(new V2(rect.x0 + radius, rect.y0 + radius), radius, 3.1415927f, 4.712389f, WallW, MapStyle.Ink);
        }

        // ------------------------------------------------------------------ le aperture
        static bool IsWindow(Opening o) => o.b < 0 || o.oneSided || o.style == OpenStyle.Finestra || o.style == OpenStyle.Lancetta || o.style == OpenStyle.Feritoia;

        void PaintOpening(Room r, Opening o, Side side, Pen ink)
        {
            bool alongX = o.alongX;
            V2 c = alongX ? new V2(o.center * 0.1f, o.line * 0.1f) : new V2(o.line * 0.1f, o.center * 0.1f);
            V2 axis = alongX ? new V2(1f, 0f) : new V2(0f, 1f);
            V2 inward = side == Side.S ? new V2(0f, 1f) : side == Side.N ? new V2(0f, -1f) : side == Side.W ? new V2(1f, 0f) : new V2(-1f, 0f);
            float w = o.width * 0.1f;
            var inkc = MapStyle.Ink;

            if (IsWindow(o))
            {
                // finestra: tre righe sottili nello spessore del muro
                for (int k = -1; k <= 1; k++)
                    ink.Line(c - axis * (w * 0.5f) + inward * (k * 0.16f), c + axis * (w * 0.5f) + inward * (k * 0.16f), FineW * 0.8f, inkc.A(0.85f), false);
                ink.Line(c - axis * (w * 0.5f) - inward * 0.25f, c - axis * (w * 0.5f) + inward * 0.25f, FineW, inkc, false);
                ink.Line(c + axis * (w * 0.5f) - inward * 0.25f, c + axis * (w * 0.5f) + inward * 0.25f, FineW, inkc, false);
                return;
            }

            int index; openingIndex.TryGetValue(o, out index);
            var state = s.paidDoors ? s.door[index] : DoorState.Open;
            // gli stipiti
            ink.Line(c - axis * (w * 0.5f) - inward * 0.46f, c - axis * (w * 0.5f) + inward * 0.46f, WallW * 0.62f, inkc, false);
            ink.Line(c + axis * (w * 0.5f) - inward * 0.46f, c + axis * (w * 0.5f) + inward * 0.46f, WallW * 0.62f, inkc, false);

            if (o.style == OpenStyle.Ascensore) return;       // la cabina la disegna il pittore dei punti d'interesse
            if (o.style == OpenStyle.Passaggio)
            {
                // il passaggio basso: un tratteggio rosso scuro e due "denti" (ci si passa chinati)
                ink.Dashed(c - axis * (w * 0.5f), c + axis * (w * 0.5f), ThinW, MapStyle.Red.A(0.85f), 0.35f, 0.22f);
                ink.Line(c - axis * (w * 0.28f) - inward * 0.6f, c - axis * (w * 0.28f) + inward * 0.6f, FineW, MapStyle.Red.A(0.7f), false);
                ink.Line(c + axis * (w * 0.28f) - inward * 0.6f, c + axis * (w * 0.28f) + inward * 0.6f, FineW, MapStyle.Red.A(0.7f), false);
                return;
            }
            if (state != DoorState.Open)
            {
                // la serranda: un blocco d'acciaio con le righe in diagonale
                float t = 0.34f;
                var box = new R2(c.x - (alongX ? w * 0.5f : t), c.y - (alongX ? t : w * 0.5f), c.x + (alongX ? w * 0.5f : t), c.y + (alongX ? t : w * 0.5f));
                ink.Rect(box, MapStyle.Steel.A(0.92f));
                ink.Hatch(box, 0.785f, 0.42f, FineW * 0.8f, MapStyle.Ink.A(0.75f));
                return;
            }
            bool arch = o.style == OpenStyle.Arco || o.style == OpenStyle.Arcata;
            if (arch)
            {
                // arcata: due pilastri pieni e un filo teso tra i due
                ink.Rect(new R2(c.x - (alongX ? w * 0.5f : 0.3f), c.y - (alongX ? 0.3f : w * 0.5f), c.x - (alongX ? w * 0.5f - 0.5f : -0.3f), c.y - (alongX ? -0.3f : w * 0.5f - 0.5f)), inkc);
                ink.Rect(new R2(c.x + (alongX ? w * 0.5f - 0.5f : -0.3f), c.y + (alongX ? -0.3f : w * 0.5f - 0.5f), c.x + (alongX ? w * 0.5f : 0.3f), c.y + (alongX ? 0.3f : w * 0.5f)), inkc);
                ink.Dashed(c - axis * (w * 0.5f - 0.5f), c + axis * (w * 0.5f - 0.5f), FineW, inkc.A(0.5f), 0.5f, 0.45f);
                return;
            }
            // porta: l'anta e il suo arco di apertura
            bool left = Noise.R(o.line, o.center, s.seed) < 0.5f;
            V2 hinge = c + axis * (left ? -w * 0.5f : w * 0.5f);
            V2 closedDir = axis * (left ? 1f : -1f);
            float leaf = w * (o.style == OpenStyle.PortaLarga ? 0.5f : 0.92f);
            float a0 = (float)Math.Atan2(closedDir.y, closedDir.x);
            float sweep = V2.Cross(closedDir, inward) > 0f ? 1.5707963f : -1.5707963f;
            ink.Line(hinge, hinge + inward * leaf, FineW * 1.2f, inkc.A(0.9f), false);
            ink.Arc(hinge, leaf, a0, a0 + sweep, FineW * 0.9f, inkc.A(0.55f), 8, 0.4f);
            if (o.style == OpenStyle.PortaLarga)
            {
                V2 hinge2 = c + axis * (left ? w * 0.5f : -w * 0.5f);
                V2 closed2 = -closedDir;
                float b0 = (float)Math.Atan2(closed2.y, closed2.x);
                float sweep2 = V2.Cross(closed2, inward) > 0f ? 1.5707963f : -1.5707963f;
                ink.Line(hinge2, hinge2 + inward * leaf, FineW * 1.2f, inkc.A(0.9f), false);
                ink.Arc(hinge2, leaf, b0, b0 + sweep2, FineW * 0.9f, inkc.A(0.55f), 8, 0.4f);
            }
        }
    }
}
