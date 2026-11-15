using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    public static partial class FloorBuilder
    {
        /// <summary>Lesene a muro delle sale-cattedrale (pareti da 13 m in su, solo ciclo a ondate): sopra l'ultima balconata un fascio di pietra
        /// sale fino alla volta, con tre mensole sotto e un capitello in cima, tra una finestra e l'altra. Solo visive (niente collider: il
        /// mobilio e le rampe stanno piu' in basso, e sporgono al massimo 40 cm), quindi non toccano la navmesh.</summary>
        static void CathedralRibs(MeshBuilder mb, Plan plan, Room r)
        {
            if (!plan.grid || r.IsLift || r.wallH < PlanGen.CathedralLow - 0.05f) return;
            var stone = Mat("cornice_pietra");
            if (stone == null) return;
            float yBase = r.GalleryLevels == 2 ? 8.9f : 5.3f;
            float yTop = r.wallH - 0.7f;
            if (yTop - yBase < 3f) return;
            float edge = TH + Mathf.Max(1.4f, r.cornerRadius + 0.9f);          // niente lesene negli angoli (e nelle curve della rotonda)

            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
            {
                bool ax = Plan.AlongX(s);
                float line = M(Plan.Line(r, s));
                float lo = ax ? M(r.x0) : M(r.z0), hi = ax ? M(r.x1) : M(r.z1);
                Vector3 inward = s == Side.S ? Vector3.forward : s == Side.N ? Vector3.back : s == Side.W ? Vector3.right : Vector3.left;
                Vector3 P(float u, float y, float d) => (ax ? new Vector3(u, y, line) : new Vector3(line, y, u)) + inward * (TH + d);
                Vector3 Sz(float along, float h, float depth) => ax ? new Vector3(along, h, depth) : new Vector3(depth, h, along);

                // le aperture e le porte dei giardini occupano un tratto di parete: le lesene stanno tra l'una e l'altra
                var busy = new List<Vector2>();
                foreach (var o in plan.OpeningsOn(r, s)) busy.Add(new Vector2(M(o.Lo) - 0.5f, M(o.Hi) + 0.5f));
                foreach (var o in AnnexDoorsOn(plan, r, s)) busy.Add(new Vector2(M(o.Lo) - 0.5f, M(o.Hi) + 0.5f));

                float span = hi - lo - 2f * edge;
                if (span < 3f) continue;
                int n = Mathf.Max(1, Mathf.RoundToInt(span / 4.5f));
                for (int i = 0; i <= n; i++)
                {
                    float u = lo + edge + span * i / n;
                    bool blocked = false;
                    foreach (var b in busy) if (u + 0.45f > b.x && u - 0.45f < b.y) { blocked = true; break; }
                    if (blocked) continue;
                    mb.Box(stone, P(u, (yBase + yTop) * 0.5f, 0.11f), Sz(0.52f, yTop - yBase, 0.22f));
                    mb.Box(stone, P(u, yTop - 0.15f, 0.17f), Sz(0.80f, 0.30f, 0.34f));
                    mb.Box(stone, P(u, yTop + 0.05f, 0.19f), Sz(0.92f, 0.12f, 0.38f));
                    mb.Box(stone, P(u, yBase - 0.12f, 0.13f), Sz(0.62f, 0.24f, 0.26f));
                    mb.Box(stone, P(u, yBase - 0.36f, 0.11f), Sz(0.50f, 0.24f, 0.22f));
                    mb.Box(stone, P(u, yBase - 0.62f, 0.09f), Sz(0.38f, 0.28f, 0.18f));
                }
            }
        }
    }
}
