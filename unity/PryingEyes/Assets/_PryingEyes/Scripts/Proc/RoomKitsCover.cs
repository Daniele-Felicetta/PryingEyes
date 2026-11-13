using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Ripari per le sale grandi. Il censimento (-roomcensus, 90 piani) dava, nelle sale sopra i 200 m2 (13 a piano), arredi solidi
    /// su appena il 3,6% del pavimento nelle stanze di guardia, il 5,4% nelle arene e piu' di mezza galleria a zero: sale enormi e vuote,
    /// senza niente dietro cui girare mentre l'orda ti insegue. Questa passata, l'ultima delle ricette (dopo colonne, panche e candelabro),
    /// porta la copertura verso un obiettivo per tipo di stanza (arena 11%, boss 10%, guardia 9%) con pezzi del tema: file di sedie, letti,
    /// panche, divani, banconi, armadi... Ogni pezzo (o fila di fino a tre affiancati, una barricata) sta ad almeno CoverGap dagli altri
    /// arredi e CoverWall dai muri, e non tocca i tracciati riservati davanti alle porte: le corsie restano larghe quanto serve alla capsula
    /// del giocatore e alla navmesh, e non si chiude nessuna tasca. Le stanze con un impianto proprio (navate, platea, corsie, rotonda) e le
    /// stanze speciali (partenza, negozio, biblioteca, segreta) non si toccano.
    /// Prima versione (visto dal censimento): anche i tracciati riservati contavano come arredi e si teneva CoverGap anche da loro; con
    /// quattro porte i tracciati a L dividono la stanza in quadranti e, allargati di 1,6 m per lato, non lasciavano un posto su venti.
    /// Seconda (la piu' importante): le sale con balconata (tre stanze grandi su quattro) riservavano a terra tutta la fascia sotto i solai,
    /// 4,2 m (6,8 nell'atrio) per lato su tutta la lunghezza piu' la testata, cioe' quasi tutto il pavimento. A terra sono ingombro vero solo
    /// le scale (da x0+2 per 7,2 m) e le colonne dei solai (ReserveBalconies): sotto i solai, a 3,6 m, restano 3,3 m liberi e ci stanno
    /// ripari bassi. Alle ricette casuali quella fascia resta vietata come prima (Ctx.underSlab, controllata da Free).</summary>
    public static partial class RoomKits
    {
        /// <summary>Spazio libero minimo (m) tra un riparo e gli altri arredi.</summary>
        const float CoverGap = 1.3f;
        /// <summary>Distanza minima (m) di un riparo dai muri: lungo il perimetro resta una corsia (gli arredi a muro li mettono le ricette).</summary>
        const float CoverWall = 1.2f;

        readonly struct Cover
        {
            public readonly string name;
            public readonly float weight;
            public readonly int row;        // quanti pezzi affiancati al massimo (una barricata)
            public Cover(string name, float weight, int row) { this.name = name; this.weight = weight; this.row = row; }
        }

        // albergo: mobili ammassati in mezzo alla sala, come dopo uno sgombero
        static readonly Cover[] CoverAlbergo =
        {
            new Cover("arr_divano", 3f, 2), new Cover("arr_banco", 1.4f, 1), new Cover("arr_pianoforte", 0.9f, 1), new Cover("arr_carrello", 1.5f, 1),
            new Cover("arr_armadio", 2f, 3), new Cover("arr_scaffale", 1.2f, 3), new Cover("arr_poltrona", 1.2f, 2), new Cover("arr_tavolo_acciaio", 1.2f, 2),
            new Cover("arr_letto", 0.8f, 1),
        };
        static readonly Cover[] CoverTeatro =
        {
            new Cover("tea_fila", 3f, 3), new Cover("tea_fila_b", 2f, 3), new Cover("tea_fila_c", 2f, 3), new Cover("tea_toeletta", 1.2f, 2),
            new Cover("tea_faro", 1.2f, 1), new Cover("tea_manichino", 1f, 1), new Cover("arr_divano", 1.2f, 2), new Cover("arr_banco", 1f, 1),
            new Cover("arr_pianoforte", 0.6f, 1), new Cover("arr_carrello", 1f, 1),
        };
        static readonly Cover[] CoverSanatorio =
        {
            new Cover("san_letto", 3f, 3), new Cover("san_carrello", 1.5f, 1), new Cover("san_flebo", 0.8f, 1), new Cover("san_sedia_ruote", 1.5f, 2),
            new Cover("san_paravento", 2.5f, 3), new Cover("arr_tavolo_acciaio", 1.5f, 2), new Cover("arr_scaffale", 1f, 3), new Cover("arr_banco", 0.7f, 1),
        };
        static readonly Cover[] CoverChiesa =
        {
            new Cover("chi_panca", 4f, 3), new Cover("chi_sarcofago", 1.4f, 1), new Cover("chi_statua", 1.2f, 1), new Cover("chi_stallo", 1.2f, 2),
            new Cover("chi_confessionale", 1f, 1), new Cover("chi_inginocchiatoio", 0.7f, 2), new Cover("chi_fonte", 0.5f, 1),
        };

        static Cover[] CoverKit(Ctx c) =>
            c.r.church ? CoverChiesa : c.plan.theme == Theme.Teatro ? CoverTeatro : c.plan.theme == Theme.Sanatorio ? CoverSanatorio : CoverAlbergo;

        /// <summary>Che parte del pavimento deve essere coperta da arredi solidi (0 = la stanza non riceve ripari).</summary>
        static float CoverTarget(Room r)
        {
            switch (r.type)
            {
                case RoomType.Candela: return 0.11f;
                case RoomType.Boss: return 0.10f;
                case RoomType.Guardia: return 0.09f;
                default: return 0f;
            }
        }

        /// <summary>Posto libero per un riparo: dentro i muri di almeno CoverWall, lontano CoverGap dagli altri arredi (c.footprints) e senza
        /// toccare nulla di quanto sta in c.occ (arredi e tracciati riservati: a questi ultimi basta un palmo, sono corsie gia' larghe 1,6 m).</summary>
        /// <summary>-nocover: niente ripari e riserve di prima (tutta la fascia sotto le balconate vietata), per confrontare due build a parita' di seme.</summary>
        static readonly bool noCover = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nocover") >= 0;

        /// <summary>Altezza massima (m) di un riparo sotto un solaio: il solaio sta a 3,6 m, ha 0,27 m di spessore e va lasciato un palmo di testa.</summary>
        const float UnderSlabMax = 3.0f;

        /// <summary>Riserve a terra di una sala con balconata (FloorBuilder.BuildGallery / BuildAtrium). Gli ingombri veri sono le due scale
        /// (da x0+2, 7,2 m di rampa, larghe 2,4 m con i parapetti ai lati, piu' mezzo metro di pianerottolo sotto il solaio) e le colonne dei
        /// solai; la fascia sotto i solai e' la stessa di prima (4,2 m per lato, 6,8 nell'atrio, piu' la testata) ma a parte, in underSlab.</summary>
        static void ReserveBalconies(Ctx c)
        {
            var r = c.r;
            float x0 = r.x0 * 0.1f, x1 = r.x1 * 0.1f, z0 = r.z0 * 0.1f, z1 = r.z1 * 0.1f;
            float x = x0 + 1.1f, end = x1 - 1.1f;
            float band = r.GalleryLevels == 2 ? 6.8f : 4.2f;
            float zs = z0 + 1.1f, zn = z1 - 1.1f - band;
            if (noCover)
            {
                c.occ.Add(new Rect(x, zs, end - x, band));
                c.occ.Add(new Rect(x, zn, end - x, band));
                c.occ.Add(new Rect(x1 - 1.1f - band, zs, band, c.D));
                return;
            }
            float stairEnd = x0 + 2f + 7.2f + 0.5f;
            c.occ.Add(new Rect(x, zs, stairEnd - x, band));
            c.occ.Add(new Rect(x, zn, stairEnd - x, band));
            c.underSlab.Add(new Rect(stairEnd, zs, end - stairEnd, band));
            c.underSlab.Add(new Rect(stairEnd, zn, end - stairEnd, band));
            c.underSlab.Add(new Rect(x1 - 1.1f - band, zs, band, c.D));
            // colonne dei solai (tutte e due le costruzioni): in testa alle scale e in fondo, ai lati lunghi; l'atrio ha anche quelle del
            // solaio alto, a 9,8 m dalla testata. Attorno a ciascuna un metro libero.
            float cz0 = z0 + 2.3f, cz1 = z1 - 2.3f;
            var cols = r.GalleryLevels == 2 ? new[] { x1 - 1.5f, x1 - 10.1f } : new[] { x1 - 2.35f };
            foreach (float cx in cols)
                foreach (float cz in new[] { cz0, cz1 })
                    c.occ.Add(new Rect(cx - 0.5f, cz - 0.5f, 1f, 1f));
        }

        static bool CoverFree(Ctx c, Vector3 pos, float w, float d, float yaw, bool low)
        {
            float s = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)), co = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad));
            float hx = (w * co + d * s) * 0.5f, hz = (w * s + d * co) * 0.5f;
            if (pos.x - hx < c.ix0 + CoverWall || pos.x + hx > c.ix1 - CoverWall || pos.z - hz < c.iz0 + CoverWall || pos.z + hz > c.iz1 - CoverWall) return false;
            if (c.r.cornerRadius > 0f && !FitsShape(c.r, pos, hx + CoverWall, hz + CoverWall)) return false;
            for (int i = 0; i < c.occ.Count; i++)
            {
                var o = c.occ[i];
                if (pos.x + hx + 0.12f > o.xMin && pos.x - hx - 0.12f < o.xMax && pos.z + hz + 0.12f > o.yMin && pos.z - hz - 0.12f < o.yMax) return false;
            }
            for (int i = 0; i < c.footprints.Count; i++)
            {
                var o = c.footprints[i];
                if (pos.x + hx + CoverGap > o.xMin && pos.x - hx - CoverGap < o.xMax && pos.z + hz + CoverGap > o.yMin && pos.z - hz - CoverGap < o.yMax) return false;
            }
            if (!low)
                for (int i = 0; i < c.underSlab.Count; i++)
                {
                    var o = c.underSlab[i];
                    if (pos.x + hx > o.xMin && pos.x - hx < o.xMax && pos.z + hz > o.yMin && pos.z - hz < o.yMax) return false;
                }
            return true;
        }

        static void ArenaCover(Ctx c)
        {
            var r = c.r;
            float area = c.W * c.D;
            if (noCover || area < 150f || r.IsLift) return;
            switch (r.kind)
            {
                case RoomKind.Corridoio: case RoomKind.Ascensore: case RoomKind.Rotonda: case RoomKind.Navata:
                case RoomKind.Navatella: case RoomKind.Platea: case RoomKind.Corsia: case RoomKind.Palco:
                    return;
            }
            float want = CoverTarget(r) * area;
            if (want <= c.propArea) return;
            var kit = CoverKit(c);
            var weights = new double[kit.Length];
            for (int i = 0; i < kit.Length; i++) weights[i] = kit[i].weight;
            // generatore a parte: non sposta i numeri che le ricette successive (luci, nascite) traggono da c.rng
            var rng = new Rng(r.seed ^ 0x3C6EF372);
            int placed = 0, cap = Mathf.Clamp(Mathf.RoundToInt(area / 9f), 6, 70), tries = 0, tooTall = 0, blocked = 0, under = 0;
            float before = c.propArea;
            for (; tries < 400 && c.propArea < want && placed < cap; tries++)
            {
                var item = kit[rng.Weighted(weights)];
                var def = PropDefs.Get(item.name);
                if (def.size.y > r.wallH - 0.4f) { tooTall++; continue; }
                int count = item.row > 1 ? (rng.Chance(0.6) ? item.row : rng.Range(1, item.row + 1)) : 1;
                Vector3 face = rng.Pick(Vector3.forward, Vector3.back, Vector3.right, Vector3.left);
                float jitter = count == 1 ? rng.Range(-14f, 14f) : 0f;
                float yaw = YawOf(face) + jitter;
                Vector3 side = Vector3.Cross(Vector3.up, face);
                float step = def.size.x + 0.08f;
                var pos = new Vector3(rng.Range(c.ix0 + CoverWall, c.ix1 - CoverWall), 0f, rng.Range(c.iz0 + CoverWall, c.iz1 - CoverWall));
                bool low = def.size.y <= UnderSlabMax, fits = true;
                for (int k = 0; k < count && fits; k++)
                    fits = CoverFree(c, pos + side * ((k - (count - 1) * 0.5f) * step), def.size.x, def.size.z, yaw, low);
                if (!fits) { blocked++; continue; }
                for (int k = 0; k < count; k++)
                {
                    var at = pos + side * ((k - (count - 1) * 0.5f) * step);
                    bool inSlab = c.underSlab.Exists(o => o.Contains(new Vector2(at.x, at.z)));
                    // il posto l'ha gia' verificato CoverFree (piu' severo di Free, che vieterebbe la fascia sotto i solai): qui senza controllo,
                    // e l'ingombro lo registra Occupy
                    if (Prop(c, item.name, at, face, 1f, false, 0f, jitter) == null) continue;
                    Occupy(c, at, def.size.x, def.size.z, yaw);
                    placed++;
                    if (inSlab) under++;
                }
            }
            if (census)
                Debug.Log($"[PryingEyes] COVER tema={c.plan.theme} stanza={r.kind} tipo={r.type} area={area:F0} voluto={want:F1} prima={before:F1} dopo={c.propArea:F1} pezzi={placed} tentativi={tries} troppo_alti={tooTall} bloccati={blocked} sotto_solaio={under} alto={r.wallH:F1}");
        }
    }
}
