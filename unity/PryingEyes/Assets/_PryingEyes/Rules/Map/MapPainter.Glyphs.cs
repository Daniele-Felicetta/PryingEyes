using System;
using System.Collections.Generic;

namespace PryingEyes.Proc.MapArt
{
    public sealed partial class MapPainter
    {
        // ------------------------------------------------------------------ l'occhio
        /// <summary>L'occhio dell'albergo: una mandorla, l'iride che guarda verso 'look' (metri dal centro) e la pupilla a fessura. 'open' va da
        /// 0 (chiuso: una riga) a 1 (spalancato). E' il segno del culto e guarda dalle macchie, dai sigilli e dall'angolo del foglio.</summary>
        // liste di lavoro dell'occhio: la vista lo ridisegna a ogni fotogramma e non deve lasciare spazzatura (si usa solo dal filo principale)
        static readonly List<V2> eyeTop = new List<V2>(16), eyeBot = new List<V2>(16), eyePoly = new List<V2>(32);

        public static void Eye(Pen ink, V2 c, float w, float open, V2 look, Col sclera, Col iris, Col pupil, Col lid)
        {
            open = open < 0f ? 0f : open > 1f ? 1f : open;
            float lw = Math.Max(0.07f, w * 0.045f);
            var top = eyeTop; var bot = eyeBot; var poly = eyePoly;
            top.Clear(); bot.Clear(); poly.Clear();
            if (open < 0.05f)
            {
                for (int i = 0; i <= 8; i++) { float t = -1f + 2f * i / 8f; top.Add(c + new V2(t * w * 0.5f, -(1f - t * t) * w * 0.045f)); }
                ink.Path(top, false, lw * 1.3f, lid, 0.15f);
                return;
            }
            const int N = 12;
            float hMax = w * 0.27f * open;
            for (int i = 0; i <= N; i++)
            {
                float t = -1f + 2f * i / N, h = hMax * (float)Math.Pow(Math.Max(0f, 1f - t * t), 0.85);
                top.Add(c + new V2(t * w * 0.5f, h)); bot.Add(c + new V2(t * w * 0.5f, -h * 0.92f));
            }
            poly.AddRange(top);
            for (int i = bot.Count - 2; i >= 1; i--) poly.Add(bot[i]);
            ink.Fill(poly, sclera);
            // iride e pupilla, tenute dentro la mandorla
            float ir = Math.Min(hMax * 0.96f, w * 0.17f);
            V2 lk = new V2(Math.Max(-w * 0.16f, Math.Min(w * 0.16f, look.x)), Math.Max(-hMax * 0.30f, Math.Min(hMax * 0.30f, look.y)));
            V2 ic = c + lk;
            ink.Disc(ic, ir, iris, 16);
            ink.Blob(ic, ir * 0.30f, ir * 0.86f, pupil, 5, 0f, 12);
            ink.Disc(ic + new V2(-ir * 0.30f, ir * 0.34f), ir * 0.13f, Col.F(1f, 1f, 1f, 0.55f), 6);
            // le palpebre
            ink.Path(poly, true, lw, lid, 0.15f);
            ink.Path(top, false, lw * 1.5f, lid, 0.1f);
        }

        // ------------------------------------------------------------------ il sigillo di ceralacca
        /// <summary>Il sigillo su una porta chiusa: ceralacca con l'occhio impresso, due gocce e il riflesso.</summary>
        public static void Seal(Pen ink, V2 c, float r, int sealSeed, float crack = 0f)
        {
            ink.Blob(c + new V2(0.05f, -0.06f), r * 1.05f, r * 1.05f, Col.F(0f, 0f, 0f, 0.38f), sealSeed + 1, 0.10f, 18);        // l'ombra
            ink.Blob(c, r, r, MapStyle.Wax, sealSeed, 0.12f, 22);
            ink.Ring(c, r * 0.80f, r * 0.11f, MapStyle.WaxHi.A(0.60f), 18, 0f);
            ink.Blob(c + new V2(r * 0.18f, -r * 1.02f), r * 0.20f, r * 0.34f, MapStyle.Wax, sealSeed + 2, 0.1f, 8);              // una goccia
            ink.Blob(c + new V2(-r * 0.46f, -r * 0.94f), r * 0.14f, r * 0.20f, MapStyle.Wax, sealSeed + 3, 0.1f, 8);
            Eye(ink, c, r * 1.22f, 0.9f, new V2(0f, 0f), MapStyle.Wax.Mul(0.72f), MapStyle.WaxHi.Mul(0.9f), Col.F(0.08f, 0.01f, 0.01f), MapStyle.WaxHi.Mul(1.1f));
            ink.Arc(c, r * 0.9f, 2.2f, 2.9f, r * 0.10f, Col.F(1f, 0.75f, 0.6f, 0.45f), 6, 0f);                               // il riflesso
            if (crack > 0f)
            {
                // ceralacca spezzata: la crepa taglia il sigillo da parte a parte
                ink.Ink(c + new V2(-r * 0.2f, r * 0.95f), c + new V2(r * 0.12f, -r * 0.95f), r * 0.12f, MapStyle.Ink.A(0.9f), 0.3f);
            }
        }

        // ------------------------------------------------------------------ porte da comprare
        /// <summary>I sigilli sulle porte chiuse che si possono comprare, e il loro prezzo.</summary>
        public Art Doors()
        {
            var art = new Art();
            var ink = new Pen(art.ink, s.seed + 71) { wobble = 0.5f };
            if (!s.paidDoors) return art;
            var taken = new List<V2>();
            for (int i = 0; i < plan.openings.Count; i++)
            {
                var o = plan.openings[i];
                if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || s.door[i] != DoorState.Sealed) continue;
                if (!s.IsSeen(o.a) && !s.IsSeen(o.b)) continue;
                V2 c = o.alongX ? new V2(o.center * 0.1f, o.line * 0.1f) : new V2(o.line * 0.1f, o.center * 0.1f);
                Seal(ink, c, 1.35f, o.line * 31 + o.center);
                // due porte vicine (un corridoio di porte) hanno i prezzi a meno di una scritta di distanza (una scritta e' larga fino a 9 metri nei
                // piani grandi, dove la carta scende a 3-4 px al metro): il secondo scende sotto il primo
                var at = c + new V2(0f, -2.9f);
                for (int tries = 0; tries < 4; tries++)
                {
                    bool clash = false;
                    foreach (var q in taken) if (Math.Abs(q.x - at.x) < 9f && Math.Abs(q.y - at.y) < 3.2f) { clash = true; break; }
                    if (!clash) break;
                    at = new V2(at.x, at.y - 3.4f);
                }
                taken.Add(at);
                art.labels.Add(new MapLabel { pos = at, text = s.doorPrice[i].ToString(), kind = LabelKind.Price, owner = -1, color = MapStyle.Red });
            }
            return art;
        }

        // ------------------------------------------------------------------ il sangue
        public Art Blood()
        {
            var art = new Art();
            var pen = new Pen(art.stain, s.seed + 83) { wobble = 0f };
            for (int i = 0; i < s.blood.Count; i++)
            {
                var b = s.blood[i];
                float size = 2.2f + b.size * 4.6f;
                int cell = (int)(Noise.R(i, 3, s.seed) * 2.99f);
                float rot = Noise.R(i, 4, s.seed) * 6.2831853f;
                pen.Stamp(cell, new V2(b.x, b.z), size, rot, MapStyle.Blood.A(0.36f + 0.34f * b.size));
            }
            return art;
        }

        // ------------------------------------------------------------------ i punti d'interesse
        static readonly Col Violet = Col.F(0.62f, 0.40f, 1.00f), Orange = Col.F(1.00f, 0.58f, 0.16f), Gold = Col.F(1.00f, 0.82f, 0.30f),
            Cyan = Col.F(0.30f, 0.68f, 0.78f), Flame = Col.F(1.0f, 0.55f, 0.14f);
        static readonly Col[] DropColors = { Col.F(1.0f, 0.70f, 0.20f), Col.F(0.95f, 0.18f, 0.14f), Col.F(1.0f, 0.86f, 0.30f), Col.F(0.30f, 0.60f, 1.0f) };

        public Art Pois()
        {
            var art = new Art();
            var ink = new Pen(art.ink, s.seed + 91) { wobble = 0.55f, tremor = 0.05f };
            for (int i = 0; i < s.pois.Count; i++)
            {
                var p = s.pois[i];
                V2 c = new V2(p.x, p.z);
                PaintPoi(ink, art, p, c, i);
            }
            Notes(art);
            return art;
        }

        // ------------------------------------------------------------------ gli appunti di chi e' passato prima
        static readonly string[] NoteTexts =
        {
            "NON GUARDARE\nGLI OCCHI", "LA CANDELA\nNON BASTA", "SENTO UN\nRESPIRO", "L'ASCENSORE\nMENTE", "NON FERMARTI",
            "SONO GIA' PASSATO\nDI QUI", "LE PORTE\nSI RICORDANO", "CI VEDONO\nDAL BUIO", "NON FIDARTI\nDELLE LUCI", "CONTA\nI PASSI",
        };

        /// <summary>Quattro appunti a mano sulla carta, due a sinistra e due a destra del piano: non dicono niente di utile, ed e' il punto. Stanno a
        /// 14-36 metri dai muri (nel foglio grande, che inquadra tutto, si vedono; nella minimappa quasi mai) e cambiano da un piano all'altro.</summary>
        void Notes(Art art)
        {
            var b = PlanBounds(0f);
            int first = (int)(Noise.R(0, 1, s.seed) * NoteTexts.Length) % NoteTexts.Length;
            for (int i = 0; i < 4; i++)
            {
                bool left = i % 2 == 0;
                float along = Noise.R(i, 2, s.seed), away = Noise.R(i, 3, s.seed), tilt = Noise.R(i, 4, s.seed) - 0.5f;
                float x = left ? b.x0 - 14f - 22f * away : b.x1 + 14f + 22f * away;
                float y = b.y0 + b.H * (i < 2 ? 0.60f + 0.28f * along : 0.12f + 0.28f * along);          // due fasce, una in alto e una in basso: non si toccano
                var color = i == 3 ? MapStyle.Red.A(0.85f) : MapStyle.Ink.A(0.72f);
                art.labels.Add(new MapLabel { pos = new V2(x, y), text = NoteTexts[(first + i * 3) % NoteTexts.Length], kind = LabelKind.Note, rot = tilt * 0.26f, owner = -1, color = color });
            }
        }

        static void Halo(Pen ink, V2 c, float r, Col ring)
        {
            ink.Disc(c, r * 1.12f, MapStyle.Paper.A(0.70f), 18);
            ink.Ring(c, r * 1.12f, 0.15f, ring.A(0.80f), 18, 0.2f);
        }

        void PaintPoi(Pen ink, Art art, MapPoi p, V2 c, int index)
        {
            PoiGlyph(ink, p, c);
            string label = p.label;
            if (!string.IsNullOrEmpty(label))
            {
                string text = p.price > 0 ? label + "\n" + p.price : label;
                art.labels.Add(new MapLabel { pos = LabelSpot(p, c, label), text = text, kind = LabelKind.Poi, owner = p.room, color = MapStyle.Ink });
            }
        }

        /// <summary>Dove sta la scritta di un segno: dentro la stanza, dal lato opposto al muro piu' vicino (i segni stanno sui muri, e una scritta
        /// centrata li' finirebbe a cavallo del muro, sopra il corridoio). Contro un muro a destra o a sinistra la scritta va di fianco, contro uno
        /// in alto o in basso sotto o sopra il segno.</summary>
        V2 LabelSpot(MapPoi p, V2 c, string label)
        {
            var below = c + new V2(0f, -2.3f);
            if (p.room < 0 || p.room >= plan.rooms.Count) return below;
            var r = plan.rooms[p.room];
            var b = new R2(r.x0 * 0.1f, r.z0 * 0.1f, r.x1 * 0.1f, r.z1 * 0.1f);
            float dl = c.x - b.x0, dr = b.x1 - c.x, db = c.y - b.y0, dt = b.y1 - c.y;
            float nearest = Math.Min(Math.Min(dl, dr), Math.Min(db, dt));
            if (nearest > 3.5f) return below;                                              // in mezzo alla stanza: sotto il segno
            // meta' della larghezza della scritta in metri: circa 0,7 m a lettera, ma mai oltre mezza stanza
            int longest = 0;
            foreach (var line in label.Split('\n')) longest = Math.Max(longest, line.Length);
            float half = Math.Min(0.7f * Math.Max(longest, 4), b.W * 0.45f);
            if (nearest == dl) return new V2(c.x + 2.0f + half, c.y);
            if (nearest == dr) return new V2(c.x - 2.0f - half, c.y);
            float x = b.W > 2f * half ? Math.Max(b.x0 + half, Math.Min(b.x1 - half, c.x)) : c.x;
            return nearest == db ? new V2(x, c.y + 2.6f) : new V2(x, c.y - 2.6f);
        }

        /// <summary>Il segno di un punto d'interesse (senza la scritta): lo usano anche la legenda del foglio grande e le prove.</summary>
        public static void PoiGlyph(Pen ink, MapPoi p, V2 c)
        {
            var line = MapStyle.Ink;
            float R = 1.6f;
            switch (p.kind)
            {
                case PoiKind.Weapon:
                {
                    Halo(ink, c, R, MapStyle.Brass);
                    // sagoma di pistola: carrello, canna, impugnatura e ponticello
                    ink.Rect(new R2(c.x - 0.95f, c.y + 0.05f, c.x + 0.75f, c.y + 0.50f), line);
                    ink.Line(c + new V2(0.75f, 0.28f), c + new V2(1.15f, 0.28f), 0.20f, line, false);
                    ink.Quad4(c + new V2(-0.80f, 0.05f), c + new V2(-0.35f, 0.05f), c + new V2(-0.55f, -0.90f), c + new V2(-1.0f, -0.90f), line);
                    ink.Arc(c + new V2(0.0f, 0.0f), 0.38f, 3.3f, 6.0f, 0.12f, line, 6, 0f);
                    break;
                }
                case PoiKind.Biblioteca:
                {
                    Halo(ink, c, R, Violet);
                    var page = Violet.Mul(0.95f).A(0.92f);
                    ink.Quad4(c + new V2(-1.15f, -0.65f), c + new V2(-0.05f, -0.50f), c + new V2(-0.05f, 0.75f), c + new V2(-1.15f, 0.62f), page);
                    ink.Quad4(c + new V2(0.05f, -0.50f), c + new V2(1.15f, -0.65f), c + new V2(1.15f, 0.62f), c + new V2(0.05f, 0.75f), page);
                    ink.Path(new[] { c + new V2(-1.15f, -0.65f), c + new V2(-0.05f, -0.50f), c + new V2(-0.05f, 0.75f), c + new V2(-1.15f, 0.62f) }, true, 0.11f, line, 0f);
                    ink.Path(new[] { c + new V2(0.05f, -0.50f), c + new V2(1.15f, -0.65f), c + new V2(1.15f, 0.62f), c + new V2(0.05f, 0.75f) }, true, 0.11f, line, 0f);
                    for (int k = 0; k < 3; k++) { float y = 0.35f - k * 0.38f; ink.Line(c + new V2(-0.95f, y), c + new V2(-0.28f, y - 0.05f), 0.07f, line.A(0.7f), false); ink.Line(c + new V2(0.28f, y - 0.05f), c + new V2(0.95f, y), 0.07f, line.A(0.7f), false); }
                    break;
                }
                case PoiKind.Scrigno:
                {
                    Halo(ink, c, R, Orange);
                    ink.Rect(new R2(c.x - 1.05f, c.y - 0.75f, c.x + 1.05f, c.y + 0.15f), Orange.Mul(0.9f).A(0.95f));
                    var lid = new List<V2>();
                    for (int k = 0; k <= 10; k++) { float a = 3.1416f * k / 10f; lid.Add(c + new V2((float)Math.Cos(a) * 1.05f, 0.15f + (float)Math.Sin(a) * 0.62f)); }
                    ink.Fill(lid, Orange.A(0.95f));
                    ink.Path(lid, true, 0.12f, line, 0f);
                    ink.Path(new[] { c + new V2(-1.05f, -0.75f), c + new V2(1.05f, -0.75f), c + new V2(1.05f, 0.15f), c + new V2(-1.05f, 0.15f) }, true, 0.12f, line, 0f);
                    ink.Disc(c + new V2(0f, 0.12f), 0.20f, line, 8);
                    break;
                }
                case PoiKind.Altare:
                {
                    Halo(ink, c, R, Gold);
                    ink.Disc(c, R * 0.8f, Gold.A(0.50f), 18);
                    ink.Line(c + new V2(0f, -1.0f), c + new V2(0f, 1.05f), 0.26f, line, false);
                    ink.Line(c + new V2(-0.62f, 0.45f), c + new V2(0.62f, 0.45f), 0.26f, line, false);
                    for (int k = 0; k < 8; k++) { float a = k * 0.7854f + 0.39f; ink.Line(c + V2.Polar(a, R * 1.28f), c + V2.Polar(a, R * 1.62f), 0.10f, Gold.Mul(0.8f).A(0.9f), false); }
                    break;
                }
                case PoiKind.Quadro:
                {
                    Col lamp = p.state != 0 ? Gold : MapStyle.Red;
                    Halo(ink, c, R, lamp);
                    ink.Rect(new R2(c.x - 0.8f, c.y - 1.0f, c.x + 0.8f, c.y + 1.0f), MapStyle.Steel.A(0.9f));
                    ink.Path(new[] { c + new V2(-0.8f, -1.0f), c + new V2(0.8f, -1.0f), c + new V2(0.8f, 1.0f), c + new V2(-0.8f, 1.0f) }, true, 0.11f, line, 0f);
                    var bolt = new[] { c + new V2(0.18f, 0.82f), c + new V2(-0.46f, -0.05f), c + new V2(-0.04f, -0.05f), c + new V2(-0.20f, -0.82f), c + new V2(0.48f, 0.12f), c + new V2(0.06f, 0.12f) };
                    ink.Fill(bolt, lamp.A(0.98f));
                    break;
                }
                case PoiKind.Candela:
                {
                    Halo(ink, c, R, Flame);
                    ink.Line(c + new V2(0f, -1.0f), c + new V2(0f, -0.25f), 0.24f, line, false);
                    var fl = new[] { c + new V2(0f, 1.05f), c + new V2(0.45f, 0.25f), c + new V2(0.38f, -0.15f), c + new V2(0f, -0.28f), c + new V2(-0.38f, -0.15f), c + new V2(-0.45f, 0.25f) };
                    ink.Fill(fl, (p.state != 0 ? Flame : Flame.Mul(0.55f)).A(0.95f));
                    ink.Path(fl, true, 0.09f, line, 0f);
                    break;
                }
                case PoiKind.Drop:
                {
                    var dc = DropColors[Math.Max(0, Math.Min(3, (int)p.tint))];
                    Halo(ink, c, R, dc);
                    var star = new List<V2>();
                    for (int k = 0; k < 10; k++) star.Add(c + V2.Polar(1.5707963f + k * 0.6283185f, (k & 1) == 0 ? 1.15f : 0.5f));
                    ink.Fill(star, dc.A(0.97f));
                    ink.Path(star, true, 0.09f, line, 0f);
                    break;
                }
                case PoiKind.Lever:
                {
                    Halo(ink, c, R * 0.85f, MapStyle.Red);
                    ink.Disc(c + new V2(0f, -0.5f), 0.42f, line, 10);
                    ink.Line(c + new V2(0f, -0.5f), c + new V2(0.55f, 0.75f), 0.20f, line, false);
                    ink.Disc(c + new V2(0.55f, 0.78f), 0.26f, MapStyle.Red, 8);
                    break;
                }
                case PoiKind.Barrel:
                {
                    ink.Disc(c, 0.9f, MapStyle.Red.A(0.35f), 14);
                    ink.Ring(c, 0.9f, 0.12f, line, 14, 0.2f);
                    ink.Line(c + new V2(-0.9f, 0f), c + new V2(0.9f, 0f), 0.09f, line, false);
                    break;
                }
                case PoiKind.Annex:
                {
                    Halo(ink, c, R, MapStyle.Verdigris);
                    ink.Rect(new R2(c.x - 0.65f, c.y - 0.85f, c.x + 0.65f, c.y + 0.55f), MapStyle.Verdigris.Mul(1.2f).A(0.95f));
                    for (int k = -1; k <= 1; k++) ink.Rect(new R2(c.x + k * 0.46f - 0.17f, c.y + 0.55f, c.x + k * 0.46f + 0.17f, c.y + 0.95f), MapStyle.Verdigris.Mul(1.2f).A(0.95f));
                    ink.Line(c + new V2(0f, -0.85f), c + new V2(0f, 0.2f), 0.14f, line, false);
                    break;
                }
                case PoiKind.ArrivalLift:
                case PoiKind.ExitLift:
                    LiftBox(ink, c, p);
                    break;
            }
        }

        /// <summary>La cabina dell'ascensore: un quadratino con le porte e la freccia (giu' all'arrivo, su all'uscita). All'uscita, sigillata e' una X rossa.</summary>
        static void LiftBox(Pen ink, V2 c, MapPoi p)
        {
            float h = 1.5f;
            var line = MapStyle.Ink;
            bool exit = p.kind == PoiKind.ExitLift;
            Col fill = exit ? (p.state != 0 ? Col.F(0.80f, 0.64f, 0.30f, 0.95f) : Col.F(0.40f, 0.42f, 0.46f, 0.95f)) : Col.F(0.46f, 0.52f, 0.48f, 0.95f);
            var box = new R2(c.x - h, c.y - h, c.x + h, c.y + h);
            ink.Rect(box, fill);
            ink.Path(new[] { new V2(box.x0, box.y0), new V2(box.x1, box.y0), new V2(box.x1, box.y1), new V2(box.x0, box.y1) }, true, 0.22f, line, 0.2f);
            ink.Line(new V2(c.x, box.y0), new V2(c.x, box.y1), 0.10f, line.A(0.7f), false);
            // la freccia: un triangolo che punta in giu' (arrivo) o in su (uscita)
            float dir = exit ? 1f : -1f;
            ink.Fill(new[] { c + new V2(-0.55f, -0.30f * dir), c + new V2(0.55f, -0.30f * dir), c + new V2(0f, 0.55f * dir) }, line.A(0.85f));
            if (exit && p.state == 0)
            {
                ink.Ink(new V2(box.x0 + 0.2f, box.y0 + 0.2f), new V2(box.x1 - 0.2f, box.y1 - 0.2f), 0.22f, MapStyle.Red, 0.2f);
                ink.Ink(new V2(box.x0 + 0.2f, box.y1 - 0.2f), new V2(box.x1 - 0.2f, box.y0 + 0.2f), 0.22f, MapStyle.Red, 0.2f);
            }
        }
    }
}
