using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Le arene dei round (pianta a griglia, stanze di tipo Candela): colonne di pietra e qualche panca sparse nella stanza, come
    /// ostacoli intorno a cui girare mentre l'orda ti insegue. Le colonne si mettono prima della ricetta della stanza (che le aggira); le
    /// panche dopo, solo dove resta posto. I tracciati davanti alle porte restano liberi (ReserveSpokes), cosi' niente chiude una tasca.</summary>
    public static partial class RoomKits
    {
        /// <summary>Un reticolo di colonne a ~5 m, mai nei tracciati riservati. Una stanza di 9 x 9 m ne ha fino a quattro, una doppia sei.</summary>
        static void ArenaColumns(Ctx c)
        {
            float W = c.W, D = c.D;
            int nx = Mathf.Max(2, Mathf.RoundToInt(W / 5.2f)), nz = Mathf.Max(2, Mathf.RoundToInt(D / 5.2f));
            int made = 0, cap = Mathf.Clamp(Mathf.RoundToInt(W*D/80f),3,12);
            var placed = new List<Vector3>();
            for (int i = 0; i < nx && made < cap; i++)
                for (int j = 0; j < nz && made < cap; j++)
                {
                    if (nx * nz > 6 && c.rng.Chance(0.3)) continue;
                    var pos = new Vector3(c.ix0 + (i + 0.5f) * W / nx, 0f, c.iz0 + (j + 0.5f) * D / nz);
                    if (Column(c, pos)) { made++; placed.Add(pos); }
                }
            // i tracciati riservati tra le porte (ReserveSpokes) possono aver bloccato tutto il reticolo, soprattutto nelle stanze lunghe con
            // porte su piu' lati: in quel caso si prova a caso, a un paio di metri dai muri e ad almeno 4,4 m dalle altre colonne
            int want = cap;
            for (int tries = 0; tries < 120 && made < want; tries++)
            {
                var pos = new Vector3(c.rng.Range(c.ix0 + 1.6f, c.ix1 - 1.6f), 0f, c.rng.Range(c.iz0 + 1.6f, c.iz1 - 1.6f));
                bool near = false;
                foreach (var q in placed) if ((q - pos).sqrMagnitude < 4.4f * 4.4f) { near = true; break; }
                if (near) continue;
                if (Column(c, pos)) { made++; placed.Add(pos); }
            }
        }

        /// <summary>Una colonna dal pavimento al soffitto: base e capitello di pietra squadrata e un fusto tondo. Solida (la navmesh la aggira).</summary>
        static bool Column(Ctx c, Vector3 pos)
        {
            float H = FloorBuilder.CeilHeight(c.r, pos.x, pos.z);
            float radius = Mathf.Clamp(0.28f + H * 0.014f, 0.32f, 0.44f);
            float w = radius * 2f + 0.16f;
            if (!Free(c, pos, w, w, 0f, 0.35f)) return false;
            var go = new GameObject("Colonna");
            go.transform.SetParent(c.props, false);
            go.transform.position = pos;
            var stone = ProcKit.Mat("mur_pietra");
            var trim = ProcKit.Mat("cornice_pietra");
            void Part(PrimitiveType t, Vector3 lp, Vector3 sc, Material m)
            {
                var p = GameObject.CreatePrimitive(t);
                Util.Kill(p.GetComponent<Collider>());
                p.name = t == PrimitiveType.Cylinder ? "Fusto" : "Blocco";
                p.transform.SetParent(go.transform, false);
                p.transform.localPosition = lp; p.transform.localScale = sc;
                var mr = p.GetComponent<MeshRenderer>();
                mr.sharedMaterial = m;
            }
            Part(PrimitiveType.Cylinder, new Vector3(0f, H * 0.5f, 0f), new Vector3(radius * 2f, H * 0.5f, radius * 2f), stone);
            Part(PrimitiveType.Cube, new Vector3(0f, 0.16f, 0f), new Vector3(w, 0.32f, w), trim);
            Part(PrimitiveType.Cube, new Vector3(0f, 0.38f, 0f), new Vector3(w * 0.82f, 0.12f, w * 0.82f), trim);
            Part(PrimitiveType.Cube, new Vector3(0f, H - 0.16f, 0f), new Vector3(w, 0.32f, w), trim);
            Part(PrimitiveType.Cube, new Vector3(0f, H - 0.38f, 0f), new Vector3(w * 0.82f, 0.12f, w * 0.82f), trim);
            var cc = go.AddComponent<CapsuleCollider>();
            cc.radius = radius + 0.04f; cc.height = H; cc.center = new Vector3(0f, H * 0.5f, 0f);
            Occupy(c, pos, w, w, 0f);
            c.propCount++; c.propArea += w * w;
            return true;
        }

        /// <summary>Qualche panca (o divano) come riparo basso, ruotata a caso, dove c'e' posto. Non chiude passaggi: Prop controlla la mappa
        /// degli ingombri e i tracciati riservati.</summary>
        static void ArenaBenches(Ctx c)
        {
            string bench = c.r.church ? "chi_panca" : c.plan.theme == Theme.Teatro ? "tea_fila" : c.plan.theme == Theme.Sanatorio ? "san_letto" : "arr_divano";
            int want = Mathf.Clamp(Mathf.RoundToInt(c.W*c.D/65f),3,14), made = 0;
            for (int i = 0; i < 120 && made < want; i++)
            {
                var pos = new Vector3(c.rng.Range(c.ix0 + 1.6f, c.ix1 - 1.6f), 0f, c.rng.Range(c.iz0 + 1.6f, c.iz1 - 1.6f));
                Vector3 face = c.rng.Chance(0.5) ? Vector3.forward : Vector3.right;
                if (c.rng.Chance(0.5)) face = -face;
                if (Prop(c, bench, pos, face, 1f, true, 0f, c.rng.Range(-12f, 12f)) != null) made++;
            }
        }

        /// <summary>Il muro che sta di fronte a chi guarda lungo 'fwd' (la direzione di un frame di stanza).</summary>
        static Side Ahead(Vector3 fwd) => Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z) ? (fwd.x > 0f ? Side.E : Side.W) : (fwd.z > 0f ? Side.N : Side.S);

        /// <summary>Vero se il muro in fondo (guardando lungo 'fwd') non ha nessuna porta verso un'altra stanza (le finestre non contano).</summary>
        static bool FarWallBlind(Ctx c, Vector3 fwd)
        {
            foreach (var o in c.plan.OpeningsOn(c.r, Ahead(fwd))) if (o.b >= 0 && !o.oneSided) return false;
            return true;
        }

        /// <summary>Il lato d'ingresso per le ricette con altare e pala contro il muro in fondo (presbiterio, cappella). Nelle piante di prima erano
        /// stanze con una porta sola e il muro in fondo era cieco; nella griglia una stanza ha porte su piu' lati, anche su quello di fronte al
        /// passaggio principale, e la pala (3,4 m, messa senza controllare gli ingombri) chiuderebbe l'arco (visto: seme 4059 piano 1, la
        /// cappella tra l'arena e il Negozio isolava Negozio, seconda arena e boss). Si sceglie un ingresso il cui muro opposto e' cieco: il
        /// passaggio principale se va bene, poi gli altri lati con una porta (la piu' larga per prima), poi uno qualunque. Se tutti e quattro
        /// i muri hanno una porta resta il principale e il chiamante controlla gli ingombri (FarWallBlind).</summary>
        static Side AltarEntrance(Ctx c)
        {
            Side main = MainSide(c);
            if (!c.plan.grid) return main;
            Side best = main; float bs = float.MinValue;
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
            {
                if (!FarWallBlind(c, Inward(s))) continue;
                float score = s == main ? 2000f : 0f, widest = 0f;
                foreach (var o in c.plan.OpeningsOn(c.r, s)) if (o.b >= 0 && !o.oneSided) widest = Mathf.Max(widest, o.width);
                if (widest > 0f) score += 1000f + widest;
                if (score > bs) { bs = score; best = s; }
            }
            return best;
        }

        /// <summary>Pianta a griglia: ogni stanza di chiesa ha il suo asse, lungo il lato lungo; nelle quasi quadrate punta verso il presbiterio piu'
        /// vicino. Il presbiterio guarda da un ingresso (AltarEntrance) al muro in fondo, che non ha porte.</summary>
        static Vector3 GridChurchAxis(Ctx c)
        {
            var r = c.r;
            if (r.kind == RoomKind.Presbiterio) return Inward(AltarEntrance(c));
            Room alt = null; float bd = float.MaxValue;
            foreach (var q in c.plan.rooms)
            {
                if (q.kind != RoomKind.Presbiterio || q == r) continue;
                float d = (new Vector2(q.CenterX - r.CenterX, q.CenterZ - r.CenterZ)).sqrMagnitude;
                if (d < bd) { bd = d; alt = q; }
            }
            Vector3 to = alt != null ? new Vector3(alt.CenterX - r.CenterX, 0f, alt.CenterZ - r.CenterZ) : Vector3.right;
            bool longX = r.W > r.D + 25 || (Mathf.Abs(r.W - r.D) <= 25 && Mathf.Abs(to.x) >= Mathf.Abs(to.z));
            if (r.D > r.W + 25) longX = false;
            if (longX) return to.x >= 0f ? Vector3.right : Vector3.left;
            return to.z >= 0f ? Vector3.forward : Vector3.back;
        }
    }
}
