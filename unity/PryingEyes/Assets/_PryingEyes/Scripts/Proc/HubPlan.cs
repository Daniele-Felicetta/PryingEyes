using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    /// <summary>La pianta della cattedrale da ricostruire (la zona sicura del gioco): pura geometria in decimetri come i piani, costruita da
    /// FloorBuilder, arredata da RoomKits.Hub.cs. La pianta cambia con le opere costruite: senza le Volte la navata non ha tetto e si vede il
    /// cielo; le vetrate dei boss e il rosone esistono come finestre solo quando sono state fatte (prima, al loro posto, un muro cieco e il
    /// cartone del vetraio); senza il Campanile la torre e' un moncone. Il sagrato davanti, all'aperto, tiene il mercato.
    ///
    ///                      N (z grande)
    ///          [Cripta ][ Presbiterio ][Sacrestia]
    ///                  [Nav.O][  Navata  ][Nav.E]
    ///                  [Batt.][ Nartece  ][Camp.]
    ///          [               Sagrato                ]
    ///                      S (z piccola)
    /// </summary>
    public static class HubPlan
    {
        public const int Sagrato = 0, Battistero = 1, Nartece = 2, Campanile = 3, NavatellaO = 4, Navata = 5, NavatellaE = 6,
            Cripta = 7, Presbiterio = 8, Sacrestia = 9;
        /// <summary>Le quattro vetrate dei boss, sul cleristorio ovest della navata (z del centro, dm), nell'ordine dei rami.</summary>
        public static readonly int[] VetrateZ = { 290, 365, 440, 505 };
        public static readonly string[] VetrateOpere = { "vetrata_cardinale", "vetrata_direttore", "vetrata_primario", "vetrata_conte" };
        /// <summary>Il colore del vetro di ogni vetrata (la variante del materiale vetrata0..3).</summary>
        public static readonly int[] VetrateVarianti = { 2, 1, 3, 0 };
        /// <summary>Dove si nasce: in fondo al sagrato, guardando la facciata (m).</summary>
        public const float SpawnX = 18f, SpawnZ = 2.6f;

        /// <summary>Il deserto aggiunge l'armeria (le armi buone, a prezzo doppio) sul lato est della navatella.</summary>
        public const int Armeria = 10;
        /// <summary>Le scale del sagrato nel deserto: la porta larga nel parapetto sud, dove si scende sulla sabbia (m).</summary>
        public const float StairX0 = 11f, StairX1 = 25f;

        public static Plan Build(Func<string, bool> built, bool desert = false)
        {
            if (built == null) built = _ => false;
            var p = new Plan { seed = 1717, floor = 0, name = "La Cattedrale", epithet = "La tua cattedrale, da ricostruire", hub = true, theme = Theme.Albergo, startInChurch = true };
            bool volte = built("volte"), campanile = built("campanile");
            Room R(RoomKind kind, int x0, int z0, int x1, int z1, float wallH, CeilKind ceil, bool ridgeAlongX, float ridgeH, string label, bool church = true)
            {
                var r = new Room { id = p.rooms.Count, kind = kind, type = RoomType.Normale, church = church, x0 = x0, z0 = z0, x1 = x1, z1 = z1,
                    wallH = wallH, ridgeH = Math.Max(wallH, ridgeH), ceil = ceil, ridgeAlongX = ridgeAlongX, seed = 9100 + p.rooms.Count * 37, label = label, wing = 1 };
                if (ceil == CeilKind.Flat || ceil == CeilKind.Beams || ceil == CeilKind.Open) r.ridgeH = r.wallH;
                p.rooms.Add(r);
                return r;
            }
            R(RoomKind.Sagrato, 0, 0, 360, 180, 3f, CeilKind.Open, true, 3f, "Il sagrato");
            R(RoomKind.Cappella, 60, 180, 120, 240, 6f, CeilKind.BarrelRound, false, 7.6f, "Il battistero");
            R(RoomKind.Nartece, 120, 180, 240, 240, 6.5f, CeilKind.BarrelPointed, true, 8.4f, "Il nartece");
            var tower = R(RoomKind.Campanile, 240, 180, 300, 240, campanile ? 17f : 4.2f, CeilKind.Flat, true, 0f, "Il campanile");
            if (!campanile) tower.ruined = true;
            R(RoomKind.Navatella, 60, 240, 120, 540, 7f, CeilKind.BarrelRound, false, 8.8f, "La navatella ovest");
            var nave = R(RoomKind.Navata, 120, 240, 240, 540, PlanGen.CathedralLow, volte ? CeilKind.BarrelPointed : CeilKind.Flat, false, PlanGen.CathedralLow + 5.2f, "La navata");
            if (!volte) nave.ruined = true;
            R(RoomKind.Navatella, 240, 240, 300, 540, 7f, CeilKind.BarrelRound, false, 8.8f, "La navatella est");
            R(RoomKind.Cripta, 30, 540, 120, 630, 5f, CeilKind.BarrelRound, true, 6.6f, "La discesa alle catacombe");
            R(RoomKind.Presbiterio, 120, 540, 240, 660, PlanGen.CathedralLow, CeilKind.BarrelPointed, true, PlanGen.CathedralLow + 5.2f, "Il presbiterio");
            R(RoomKind.Sacrestia, 240, 540, 330, 630, 5f, CeilKind.Beams, true, 5f, "La sacrestia");
            if (desert) R(RoomKind.Sacrestia, 300, 240, 470, 380, 5f, CeilKind.Beams, true, 5f, "L'armeria");

            void Open(int a, int b, bool alongX, int line, int center, int width, OpenStyle style, float top = -1f)
            {
                var o = new Opening { a = a, b = b, alongX = alongX, line = line, center = center, width = width, style = style };
                Opening.Defaults(o);
                if (top > 0f) o.top = top;
                p.openings.Add(o);
            }
            void Window(int a, bool alongX, int line, int center, int width, OpenStyle style, float sill, float top, bool oneSided = false, int variant = 0)
            {
                p.openings.Add(new Opening { a = a, b = -1, alongX = alongX, line = line, center = center, width = width, style = style, sill = sill, top = top, oneSided = oneSided, variant = variant });
            }
            // porte: il portale della facciata, il nartece con le sue due cappelle, la porta della navata, le arcate, l'arco trionfale
            Open(Sagrato, Nartece, true, 180, 180, 32, OpenStyle.Arco, 4.6f);
            Open(Nartece, Battistero, false, 120, 210, 14, OpenStyle.Arco);
            Open(Nartece, Campanile, false, 240, 210, 12, OpenStyle.Porta);
            Open(Nartece, Navata, true, 240, 180, 36, OpenStyle.Arco, 5.2f);
            foreach (int z in VetrateZ)
            {
                Open(Navata, NavatellaO, false, 120, z, 46, OpenStyle.Arcata);
                Open(Navata, NavatellaE, false, 240, z, 46, OpenStyle.Arcata);
            }
            Open(Navata, Presbiterio, true, 540, 180, 72, OpenStyle.Arcata, 9.6f);
            Open(NavatellaO, Cripta, true, 540, 90, 16, OpenStyle.Arco);
            Open(Presbiterio, Cripta, false, 120, 600, 12, OpenStyle.Porta);
            Open(NavatellaE, Sacrestia, true, 540, 270, 14, OpenStyle.Porta);
            Open(Presbiterio, Sacrestia, false, 240, 600, 12, OpenStyle.Porta);
            if (desert)
            {
                // la porta dell'armeria (la lancetta della navatella che stava qui si toglie) e la porta larga del sagrato sulla sabbia
                Open(NavatellaE, Armeria, false, 300, 310, 14, OpenStyle.Arco);
                var exit = new Opening { a = Sagrato, b = -1, alongX = true, line = 0, center = (int)((StairX0 + StairX1) * 5f), width = (int)((StairX1 - StairX0) * 10f), style = OpenStyle.PortaLarga, noGlass = true };
                Opening.Defaults(exit);
                exit.top = Opening.ParapetTop;      // il sagrato ha un parapetto, non un muro di tre metri: piu' in alto l'intradosso e le spalle restavano in aria sopra la scalinata
                p.openings.Add(exit);
            }

            // finestre: le navatelle sul vuoto, le tre lancette dell'abside, le feritoie della cripta e della torre
            foreach (int z in VetrateZ)
            {
                Window(NavatellaO, false, 60, z, 12, OpenStyle.Lancetta, 1.9f, 5.0f, false, 1);
                if (!desert || z > 400) Window(NavatellaE, false, 300, z, 12, OpenStyle.Lancetta, 1.9f, 5.0f, false, 2);      // sul muro dell'armeria non c'e' piu' il vuoto
            }
            if (desert)
            {
                // l'armeria vede il deserto da finestre alte (i pannelli delle armi stanno piu' in basso)
                foreach (int x in new[] { 340, 385, 430 }) { Window(Armeria, true, 240, x, 12, OpenStyle.Lancetta, 2.7f, 4.5f, false, 2); Window(Armeria, true, 380, x, 12, OpenStyle.Lancetta, 2.7f, 4.5f, false, 2); }
                foreach (int z in new[] { 280, 340 }) Window(Armeria, false, 470, z, 12, OpenStyle.Lancetta, 2.7f, 4.5f, false, 2);
            }
            foreach (int x in new[] { 150, 180, 210 }) Window(Presbiterio, true, 660, x, 14, OpenStyle.Lancetta, 2.6f, 10.2f, false, 3);
            Window(Sacrestia, false, 330, 585, 14, OpenStyle.Finestra, 0.95f, 2.45f);
            Window(Cripta, false, 30, 585, 8, OpenStyle.Feritoia, 2.35f, 3.05f);
            Window(Battistero, false, 60, 210, 10, OpenStyle.Lancetta, 1.9f, 4.4f, false, 0);
            if (campanile)
            {
                Window(Campanile, false, 300, 195, 8, OpenStyle.Feritoia, 4.2f, 5.0f);
                Window(Campanile, false, 300, 222, 18, OpenStyle.Lancetta, 12.0f, 15.6f, false, 1);
            }
            // il cleristorio: a est vetri chiari da sempre; a ovest le vetrate dei boss, solo quelle fatte
            foreach (int z in VetrateZ) Window(Navata, false, 240, z, 16, OpenStyle.Lancetta, 8.2f, 11.6f, true, 1);
            for (int i = 0; i < VetrateZ.Length; i++)
                if (built(VetrateOpere[i])) Window(Navata, false, 120, VetrateZ[i], 16, OpenStyle.Lancetta, 8.2f, 11.6f, true, VetrateVarianti[i]);
            if (built("rosone")) Window(Navata, true, 240, 180, 30, OpenStyle.Lancetta, 7.4f, 12.4f, true, 3);
            p.ComputeBounds();
            return p;
        }

        /// <summary>Controlli della pianta (per tools/planlab e per l'avvio): ogni stanza si raggiunge dal sagrato, ogni porta sta su un muro
        /// che le due stanze condividono, ogni finestra su un muro della sua stanza. Vuoto se va tutto bene.</summary>
        public static List<string> Validate(Plan p)
        {
            var faults = new List<string>();
            var hops = p.Hops(Sagrato);
            for (int i = 0; i < p.rooms.Count; i++) if (hops[i] < 0) faults.Add("irraggiungibile: " + p.rooms[i].label);
            foreach (var o in p.openings)
            {
                var a = p.rooms[o.a];
                bool onA = o.alongX ? (o.line == a.z0 || o.line == a.z1) && o.Lo >= a.x0 && o.Hi <= a.x1
                                    : (o.line == a.x0 || o.line == a.x1) && o.Lo >= a.z0 && o.Hi <= a.z1;
                bool onB = o.b < 0 || (o.alongX ? (o.line == p.rooms[o.b].z0 || o.line == p.rooms[o.b].z1) && o.Lo >= p.rooms[o.b].x0 && o.Hi <= p.rooms[o.b].x1
                                               : (o.line == p.rooms[o.b].x0 || o.line == p.rooms[o.b].x1) && o.Lo >= p.rooms[o.b].z0 && o.Hi <= p.rooms[o.b].z1);
                if (!onA || !onB) faults.Add($"apertura fuori dal muro: {a.label} {o.line}/{o.center}");
                if (o.top > a.wallH + 0.01f && !(a.Open)) faults.Add($"apertura piu' alta del muro: {a.label} {o.top} > {a.wallH}");
            }
            return faults;
        }
    }
}
