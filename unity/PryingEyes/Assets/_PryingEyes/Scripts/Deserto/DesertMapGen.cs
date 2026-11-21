using System;
using System.Collections.Generic;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>La minimappa del deserto (0.9.1), la parte pura: un quadrato di 64 m del mondo diventa un quadrato di 32 x 32 pixel (2 m al pixel), RGBA. Niente UnityEngine
    /// (la compila anche tools/desertlab, che ne fa un'immagine per guardarla senza Unity) e niente stato: lo stesso quadrato esce sempre uguale, quindi due quadrati vicini si
    /// toccano senza cuciture (le quote e i bordi dei luoghi si leggono anche un pixel oltre il quadrato). E' la carta di un cartografo: sabbia color pergamena ombreggiata dalla luce
    /// da nord-ovest, curve di livello ogni 3 m (piu' marcate ogni 15), pareti a tratteggio, le pozze delle saline, le impronte delle stanze dei luoghi con il muro a inchiostro
    /// (piene se hanno il tetto, tratteggiate se sono a cielo aperto), una croce sulle cappelle e sulle chiese, un rombo sulle torri, un arco sulle catacombe, e la cattedrale
    /// con la sua scalinata d'oro e il cerchio dove non nasce nessun nemico. Le cose che si muovono (te, i compagni, i nemici) le disegna il HUD sopra.</summary>
    public static class DesertMapGen
    {
        /// <summary>Il lato di un quadrato (m), i suoi pixel per lato e i metri per pixel.</summary>
        public const float TileSize = 64f;
        public const int TilePx = 32;
        public const float Mpp = TileSize / TilePx;
        /// <summary>La texture tiene Slots x Slots quadrati (640 m) e si legge a giro: la finestra del HUD (<see cref="WindowSize"/>, 512 m) non incontra mai due quadrati nello stesso posto.</summary>
        public const int Slots = 10;
        public const float WindowSize = 512f;

        /// <summary>Distanza (m) dalla cattedrale entro cui non nasce nessun nemico all'aperto (DesertDirector.SafeRadius): sulla carta e' il cerchio d'oro.</summary>
        public const float SafeRadius = 42f;

        public static int TileOf(float v) => (int)Math.Floor(v / TileSize);
        public static int SlotOf(int tile) => ((tile % Slots) + Slots) % Slots;

        // ------------------------------------------------------------------ tavolozza (0..1)
        static readonly float[] Paper = { 0.90f, 0.81f, 0.62f };
        static readonly float[] Ink = { 0.20f, 0.13f, 0.08f };
        static readonly float[] Contour = { 0.46f, 0.31f, 0.18f };
        static readonly float[] Water = { 0.64f, 0.75f, 0.79f }, WaterEdge = { 0.40f, 0.52f, 0.58f };
        static readonly float[] Roofed = { 0.58f, 0.41f, 0.29f }, OpenAir = { 0.76f, 0.60f, 0.44f }, StoneRoof = { 0.84f, 0.81f, 0.74f };
        static readonly float[] TowerFill = { 0.30f, 0.22f, 0.17f }, CryptFill = { 0.38f, 0.34f, 0.40f };
        static readonly float[] Gold = { 0.93f, 0.74f, 0.30f }, Cream = { 0.97f, 0.93f, 0.80f };
        /// <summary>Il colore di un quadrato ancora da dipingere: la carta scura dei posti non esplorati.</summary>
        public static readonly byte[] BlankColor = { 58, 47, 36, 255 };

        // cosa c'e' su un pixel (la priorita' sale con il numero)
        const byte Free = 0, RoofRoom = 1, OpenRoom = 2, CryptRoom = 3, StoneRoom = 4, TowerRoom = 5;

        [ThreadStatic] static float[] hbuf;
        [ThreadStatic] static byte[] occbuf;

        // ------------------------------------------------------------------ la cattedrale (le stanze del suo piano, tutte costruite)
        static Room[] hub;
        static readonly object hubGate = new object();

        static Room[] HubRooms()
        {
            if (hub != null) return hub;
            lock (hubGate)
            {
                if (hub == null) hub = HubPlan.Build(_ => true, true).rooms.ToArray();
                return hub;
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        static float Smooth(float a, float b, float v) { float t = Clamp01((v - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Riempie 'px' (TilePx x TilePx x 4 byte, riga 0 a sud) di colore carta non esplorata.</summary>
        public static void Blank(byte[] px)
        {
            for (int k = 0; k < TilePx * TilePx; k++) { px[k * 4] = BlankColor[0]; px[k * 4 + 1] = BlankColor[1]; px[k * 4 + 2] = BlankColor[2]; px[k * 4 + 3] = 255; }
        }

        /// <summary>I luoghi che toccano il quadrato (tx, tz): quelli con la pianta a meno di mezza diagonale dal suo centro. Il chiamante li tiene da parte per il pittore.</summary>
        public static float SiteReach => TileSize * 0.7072f + 12f;       // la mezza diagonale piu' i 9 m che un segno puo' sporgere dal centro di un luogo

        // ------------------------------------------------------------------ il quadrato
        /// <summary>Dipinge il quadrato (tx, tz) in 'px' (TilePx * TilePx * 4 byte). 'sites' sono i luoghi vicini (DesertWorld.SitesNearKnown a <see cref="SiteReach"/> dal centro).
        /// Le quote vengono da DesertWorld.Height: i luoghi attorno al quadrato devono essere gia' fatti (DesertWorld.KnownAround), altrimenti il pittore li farebbe lui.</summary>
        public static void Paint(int tx, int tz, IList<Site> sites, byte[] px)
        {
            const int G = TilePx + 2, O = TilePx + 2;
            float ox = tx * TileSize, oz = tz * TileSize;
            var h = hbuf ??= new float[G * G];
            for (int j = 0; j < G; j++)
                for (int i = 0; i < G; i++)
                    h[j * G + i] = DesertWorld.Height(ox + (i - 0.5f) * Mpp, oz + (j - 0.5f) * Mpp);

            // le stanze dei luoghi e della cattedrale su una griglia con un pixel di margine: il muro e' il bordo di cio' che e' occupato
            var occ = occbuf ??= new byte[O * O];
            Array.Clear(occ, 0, occ.Length);
            if (sites != null)
                foreach (var s in sites) MarkSite(occ, s, ox, oz);
            MarkCathedral(occ, ox, oz);

            for (int j = 0; j < TilePx; j++)
                for (int i = 0; i < TilePx; i++)
                {
                    int gi = i + 1, gj = j + 1;
                    float x = ox + (i + 0.5f) * Mpp, z = oz + (j + 0.5f) * Mpp;
                    float hc = h[gj * G + gi];
                    float gx = (h[gj * G + gi + 1] - h[gj * G + gi - 1]) / (2f * Mpp), gz = (h[(gj + 1) * G + gi] - h[(gj - 1) * G + gi]) / (2f * Mpp);
                    float nl = 1f / (float)Math.Sqrt(gx * gx + 1f + gz * gz);
                    // la luce viene da nord-ovest, alta una quarantina di gradi: il terreno piano vale 0,62
                    float lam = -gx * nl * -0.52f + nl * 0.62f + -gz * nl * 0.59f;
                    float shade = (lam - 0.62f) * 1.9f;
                    float slope = (float)Math.Sqrt(gx * gx + gz * gz);

                    // la carta: pergamena, appena tinta dalla regione (le saline bianche, le terre rosse, la citta' morta di cenere)
                    DesertWorld.TintAt(x, z, out float tr, out float tg, out float tb, out float desat);
                    float r = Paper[0] * (1f + (tr - 1f) * 0.75f), g = Paper[1] * (1f + (tg - 1f) * 0.75f), b = Paper[2] * (1f + (tb - 1f) * 0.75f);
                    float luma = 0.3f * r + 0.59f * g + 0.11f * b, ds = Math.Min(0.55f, desat * 0.6f);
                    r += (luma - r) * ds; g += (luma - g) * ds; b += (luma - b) * ds;
                    float up = Smooth(0f, 40f, hc - DesertWorld.GroundY);                    // piu' in alto, piu' chiaro: le mesa e i rilievi si staccano
                    r += (1f - r) * 0.16f * up; g += (1f - g) * 0.14f * up; b += (1f - b) * 0.10f * up;
                    // la grana della carta
                    float grain = (DesertNoise.Hash01(tx * TilePx + i, tz * TilePx + j, 4421) - 0.5f) * 0.05f;
                    r += grain; g += grain; b += grain;

                    // il terreno: ombra e luce
                    float k = shade >= 0f ? 1f + shade * 0.34f : 1f + shade * 0.52f;
                    r *= k; g *= k; b *= k;
                    if (shade < 0f)
                    {
                        float cool = Clamp01(-shade);
                        r *= 1f - 0.12f * cool; g *= 1f - 0.18f * cool; b *= 1f - 0.20f * cool;
                    }
                    // pareti: le curve si infittiscono e si aggiunge il tratteggio
                    float cliff = Smooth(0.85f, 1.6f, slope);
                    if (cliff > 0f)
                    {
                        float hatch = ((i + j) & 3) == 0 ? 0.74f : 1f;
                        float d = (1f - 0.30f * cliff) * (1f + (hatch - 1f) * cliff);
                        r *= d; g *= d; b *= d;
                    }
                    // le curve di livello: ogni 3 m una linea sottile, ogni 15 una piu' scura
                    float hm = hc - DesertWorld.GroundY;
                    float hr = h[gj * G + gi + 1] - DesertWorld.GroundY, hu = h[(gj + 1) * G + gi] - DesertWorld.GroundY;
                    float line = 0f;
                    if (hm > 0.5f)
                    {
                        if (Math.Floor(hm / 3f) != Math.Floor(hr / 3f) || Math.Floor(hm / 3f) != Math.Floor(hu / 3f))
                            line = (Math.Floor(hm / 15f) != Math.Floor(hr / 15f) || Math.Floor(hm / 15f) != Math.Floor(hu / 15f)) ? 0.40f : 0.17f;
                    }
                    if (line > 0f) { r += (Contour[0] - r) * line; g += (Contour[1] - g) * line; b += (Contour[2] - b) * line; }

                    // le pozze di salamoia
                    float brine = DesertWorld.Brine(x, z);
                    if (brine > 0.30f)
                    {
                        float w = Smooth(0.30f, 0.50f, brine);
                        float rim = 1f - Smooth(0.50f, 0.70f, brine);
                        float wr = Water[0] + (WaterEdge[0] - Water[0]) * rim, wg = Water[1] + (WaterEdge[1] - Water[1]) * rim, wb = Water[2] + (WaterEdge[2] - Water[2]) * rim;
                        r += (wr - r) * w * 0.92f; g += (wg - g) * w * 0.92f; b += (wb - b) * w * 0.92f;
                    }

                    // il cerchio dove non nasce nessun nemico: tratteggio d'oro attorno alla cattedrale
                    float dc = DesertWorld.DistToCathedral(x, z);
                    if (dc > SafeRadius - 1.6f && dc < SafeRadius + 1.6f && ((i + j) & 1) == 0) { r += (Gold[0] - r) * 0.75f; g += (Gold[1] - g) * 0.75f; b += (Gold[2] - b) * 0.75f; }

                    // le stanze: muro a inchiostro, dentro un lavaggio (a tratti se non c'e' il tetto)
                    byte here = occ[(j + 1) * O + i + 1];
                    if (here != Free)
                    {
                        // il muro e' il bordo di cio' che sta sopra: un pixel con un vicino di rango piu' basso (o senza stanza)
                        bool edge = occ[(j + 1) * O + i] < here || occ[(j + 1) * O + i + 2] < here || occ[j * O + i + 1] < here || occ[(j + 2) * O + i + 1] < here;
                        if (edge) { r = Ink[0]; g = Ink[1]; b = Ink[2]; }
                        else
                        {
                            float[] fill = here == TowerRoom ? TowerFill : here == CryptRoom ? CryptFill : here == StoneRoom ? StoneRoof : here == OpenRoom ? OpenAir : Roofed;
                            float a = here == OpenRoom ? (((i + j) % 3) == 0 ? 0.92f : 0.45f) : 0.94f;
                            // un po' dell'ombreggiatura resta, cosi' le stanze non sembrano adesivi
                            float sh = 1f + shade * 0.10f;
                            r += (fill[0] * sh - r) * a; g += (fill[1] * sh - g) * a; b += (fill[2] * sh - b) * a;
                        }
                    }

                    int o = (j * TilePx + i) * 4;
                    px[o] = ToByte(r); px[o + 1] = ToByte(g); px[o + 2] = ToByte(b); px[o + 3] = 255;
                }

            // le scale d'oro e i segni sopra ogni luogo
            PaintStairs(px, ox, oz);
            if (sites != null)
                foreach (var s in sites) PaintGlyph(px, s, ox, oz);
            PaintCathedralCross(px, ox, oz);
        }

        static byte ToByte(float v) => (byte)(Clamp01(v) * 255f + 0.5f);

        // ------------------------------------------------------------------ stanze sulla griglia
        static void FillRect(byte[] occ, float x0, float z0, float x1, float z1, float ox, float oz, byte kind)
        {
            const int O = TilePx + 2;
            // la cella (i, j) della griglia di margine e' il pixel (i - 1, j - 1) del quadrato: il suo centro sta a ox + (i - 0.5) * Mpp. Dentro se il centro e' nella stanza,
            // gonfiata di mezzo metro (i muri sono spessi e una stanza stretta non deve sparire)
            x0 -= 0.5f; z0 -= 0.5f; x1 += 0.5f; z1 += 0.5f;
            int i0 = (int)Math.Ceiling((x0 - ox) / Mpp + 0.5f), i1 = (int)Math.Ceiling((x1 - ox) / Mpp + 0.5f) - 1;
            int j0 = (int)Math.Ceiling((z0 - oz) / Mpp + 0.5f), j1 = (int)Math.Ceiling((z1 - oz) / Mpp + 0.5f) - 1;
            i0 = Math.Max(0, i0); i1 = Math.Min(O - 1, i1);
            j0 = Math.Max(0, j0); j1 = Math.Min(O - 1, j1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (occ[j * O + i] < kind) occ[j * O + i] = kind;
        }

        static void MarkSite(byte[] occ, Site s, float ox, float oz)
        {
            var plan = s.IsStack ? s.levels[Math.Min(s.entrance, s.levels.Length - 1)] : s.plan;
            if (plan == null) return;
            foreach (var r in plan.rooms)
            {
                byte kind = s.IsTower ? TowerRoom : s.IsCatacomb ? CryptRoom : (r.ruined || r.Open || r.crumble) ? OpenRoom : RoofRoom;
                FillRect(occ, r.x0 * 0.1f, r.z0 * 0.1f, r.x1 * 0.1f, r.z1 * 0.1f, ox, oz, kind);
            }
        }

        static void MarkCathedral(byte[] occ, float ox, float oz)
        {
            if (DesertWorld.DistToCathedral(ox + TileSize * 0.5f, oz + TileSize * 0.5f) > TileSize) return;
            foreach (var r in HubRooms())
                FillRect(occ, r.x0 * 0.1f, r.z0 * 0.1f, r.x1 * 0.1f, r.z1 * 0.1f, ox, oz, r.Open ? OpenRoom : StoneRoom);
        }

        // ------------------------------------------------------------------ segni
        static void Put(byte[] px, float wx, float wz, float ox, float oz, float[] c, float a = 1f)
        {
            int i = (int)Math.Floor((wx - ox) / Mpp), j = (int)Math.Floor((wz - oz) / Mpp);
            if (i < 0 || j < 0 || i >= TilePx || j >= TilePx) return;
            int o = (j * TilePx + i) * 4;
            px[o] = ToByte(px[o] / 255f + (c[0] - px[o] / 255f) * a); px[o + 1] = ToByte(px[o + 1] / 255f + (c[1] - px[o + 1] / 255f) * a); px[o + 2] = ToByte(px[o + 2] / 255f + (c[2] - px[o + 2] / 255f) * a);
        }

        // 9 x 9 pixel, riga 0 in alto
        static readonly string[] TowerGlyph = { "....#....", "...###...", "..#####..", ".###.###.", "####.####", ".###.###.", "..#####..", "...###...", "....#...." };
        static readonly string[] CryptGlyph = { "..#####..", ".#######.", "##.....##", "##.....##", "##.....##", "##.....##", "#########" };
        static readonly string[] CrossGlyph = { "...#...", "...#...", "#######", "...#...", "...#...", "...#...", "...#..." };

        static void Draw(byte[] px, string[] glyph, float cx, float cz, float ox, float oz, float[] ink, float[] halo)
        {
            int h = glyph.Length, w = glyph[0].Length;
            // il segno ha il centro nel punto (cx, cz) e i suoi pixel sono alti Mpp
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    float wx = cx + (col - (w - 1) * 0.5f) * Mpp, wz = cz + ((h - 1) * 0.5f - row) * Mpp;
                    char ch = glyph[row][col];
                    if (ch == '#') Put(px, wx, wz, ox, oz, ink);
                    else if (halo != null && NearInk(glyph, row, col)) Put(px, wx, wz, ox, oz, halo, 0.85f);
                }
        }

        static bool NearInk(string[] glyph, int row, int col)
        {
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    int r = row + dr, c = col + dc;
                    if (r >= 0 && c >= 0 && r < glyph.Length && c < glyph[0].Length && glyph[r][c] == '#') return true;
                }
            return false;
        }

        static void PaintGlyph(byte[] px, Site s, float ox, float oz)
        {
            float cx = s.CentreX, cz = s.CentreZ;
            if (cx + 5f * Mpp < ox || cx - 5f * Mpp > ox + TileSize || cz + 5f * Mpp < oz || cz - 5f * Mpp > oz + TileSize) return;
            if (s.IsTower) Draw(px, TowerGlyph, cx, cz, ox, oz, Ink, Cream);
            else if (s.IsCatacomb) Draw(px, CryptGlyph, cx, cz, ox, oz, Ink, Cream);
            else if (s.kind == SiteKind.Cappella || s.kind == SiteKind.Chiesa || s.church) Draw(px, CrossGlyph, cx, cz, ox, oz, Cream, Ink);
        }

        /// <summary>La scalinata del sagrato, in oro: li' si scende sulla sabbia e li' si torna.</summary>
        static void PaintStairs(byte[] px, float ox, float oz)
        {
            const float x0 = HubPlan.StairX0, x1 = HubPlan.StairX1, z0 = -6.2f, z1 = 0f;
            if (x1 < ox || x0 > ox + TileSize || z1 < oz || z0 > oz + TileSize) return;
            for (float z = z0 + Mpp * 0.5f; z < z1; z += Mpp * 0.5f)
                for (float x = x0 + Mpp * 0.25f; x < x1; x += Mpp * 0.5f)
                    Put(px, x, z, ox, oz, Gold, ((int)Math.Floor((z - z0) / Mpp) & 1) == 0 ? 0.95f : 0.70f);
        }

        /// <summary>Una croce chiara sulla navata: la cattedrale si riconosce da lontano, anche con la carta in piccolo.</summary>
        static void PaintCathedralCross(byte[] px, float ox, float oz)
        {
            if (DesertWorld.DistToCathedral(ox + TileSize * 0.5f, oz + TileSize * 0.5f) > TileSize) return;
            Draw(px, CrossGlyph, 18f, 40f, ox, oz, Ink, Cream);
        }
    }
}
