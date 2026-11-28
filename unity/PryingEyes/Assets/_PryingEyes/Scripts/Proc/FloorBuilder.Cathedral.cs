using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    public static partial class FloorBuilder
    {
        static Material cathedralBronze, cathedralPatina;

        static void CathedralDetails(GameObject room, Room r, Style style)
        {
            if (r.id != HubPlan.Sagrato && r.id != HubPlan.Nartece && r.id != HubPlan.Navata && r.id != HubPlan.Presbiterio) return;
            ShrineKit.Mats();
            if (cathedralBronze == null) cathedralBronze = ShrineKit.Lit("Cathedral aged bronze", new Color(0.55f, 0.34f, 0.14f), 0.26f, 0.39f);
            float x = M(r.x0 + r.x1) * 0.5f, start = M(r.z0), end = M(r.z1);
            var paving = new MeshBuilder();

            if (r.id == HubPlan.Sagrato)
            {
                // The fork leaves space for the well and meets the width of the portal at the threshold.
                float[] z = { start + 0.55f, start + 3.6f, start + 5.7f, start + 9.6f, start + 12.3f, end };
                float[] offset = { 0.78f, 0.78f, 2.35f, 2.35f, 0.78f, 0.78f };
                for (int i = 1; i < z.Length; i++)
                    foreach (int side in new[] { -1, 1 })
                        CathedralPaving(paving, style.trim, x + side * offset[i - 1], x + side * offset[i], z[i - 1], z[i], 0.76f);
                var relief = new MeshBuilder();
                CathedralEye(relief, style.trim, new Vector3(x, 5.53f, end - TH - 0.02f));
                Flush(room, "Cathedral eye", relief, false, null);
            }
            else
            {
                if (r.id == HubPlan.Presbiterio) end -= 4.8f; // Leave the altar and its candles on the original stone.
                CathedralPaving(paving, style.trim, x, x, start, end, 1.54f);
            }
            Flush(room, "Cathedral processional paving", paving, false, null);
        }

        static void CathedralPaving(MeshBuilder mesh, Material stone, float x0, float x1, float z0, float z1, float halfWidth)
        {
            // The floor collider remains the continuous original slab.
            Vector3 P(float offset, float z) => new Vector3(Mathf.Lerp(x0, x1, (z - z0) / (z1 - z0)) + offset, 0.016f, z);
            void Strip(Material material, float left, float right, float from, float to) =>
                mesh.QuadW(material, P(left, from), P(right, from), P(right, to), P(left, to), Vector3.up);
            const float Border = 0.13f, Fillet = 0.027f;
            float edge = halfWidth - Border, field = edge - Fillet;
            Strip(ShrineKit.StoneDark, -halfWidth, -edge, z0, z1);
            Strip(cathedralBronze, -edge, -field, z0, z1);
            Strip(cathedralBronze, field, edge, z0, z1);
            Strip(ShrineKit.StoneDark, edge, halfWidth, z0, z1);

            int tiles = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / 2.25f));
            float start = z0;
            for (int i = 0; i < tiles; i++)
            {
                float z = Mathf.Lerp(z0, z1, (i + 0.5f) / tiles);
                float from = z - 0.31f, to = z + 0.31f;
                Strip(stone, -field, field, start, from);
                mesh.QuadW(ShrineKit.StoneDark, P(0f, from), P(0.28f, z), P(0f, to), P(-0.28f, z), Vector3.up);
                foreach (int side in new[] { -1, 1 })
                {
                    float outside = side * field;
                    mesh.QuadW(stone, P(0f, from), P(outside, from), P(outside, z), P(side * 0.28f, z), Vector3.up);
                    mesh.QuadW(stone, P(side * 0.28f, z), P(outside, z), P(outside, to), P(0f, to), Vector3.up);
                }
                start = to;
            }
            Strip(stone, -field, field, start, z1);
        }

        static void CathedralEye(MeshBuilder mesh, Material stone, Vector3 centre)
        {
            if (cathedralPatina == null) cathedralPatina = ShrineKit.Lit("Cathedral verdigris", new Color(0.21f, 0.47f, 0.42f), 0.18f, 0.44f);
            Vector3 P(float x, float y, float depth) => centre + new Vector3(x, y, -depth);
            Vector3 Lens(float t, float sign, float width, float height, float depth) =>
                P(Mathf.Lerp(-width, width, t), sign * height * Mathf.Sin(t * Mathf.PI), depth);
            Vector3 Circle(float t, float radius, float depth)
            {
                float a = t * 2f * Mathf.PI;
                return P(radius * Mathf.Cos(a), radius * Mathf.Sin(a), depth);
            }

            const int Segments = 32;
            for (int i = 0; i < Segments; i++)
            {
                float a = i / (float)Segments, b = (i + 1f) / Segments;
                mesh.QuadW(ShrineKit.StoneDark, Lens(a, -1, 1.25f, 0.49f, 0.08f), Lens(b, -1, 1.25f, 0.49f, 0.08f),
                           Lens(b, 1, 1.25f, 0.49f, 0.08f), Lens(a, 1, 1.25f, 0.49f, 0.08f), Vector3.back);
                foreach (int side in new[] { -1, 1 })
                {
                    mesh.QuadW(stone, Lens(a, side, 1.46f, 0.64f, 0f), Lens(b, side, 1.46f, 0.64f, 0f),
                               Lens(b, side, 1.30f, 0.54f, 0.15f), Lens(a, side, 1.30f, 0.54f, 0.15f), Vector3.back);
                    mesh.QuadW(cathedralBronze, Lens(a, side, 1.30f, 0.54f, 0.15f), Lens(b, side, 1.30f, 0.54f, 0.15f),
                               Lens(b, side, 1.25f, 0.49f, 0.08f), Lens(a, side, 1.25f, 0.49f, 0.08f), Vector3.back);
                }
                mesh.QuadW(cathedralBronze, Circle(a, 0.39f, 0.08f), Circle(b, 0.39f, 0.08f),
                           Circle(b, 0.34f, 0.16f), Circle(a, 0.34f, 0.16f), Vector3.back);
                mesh.QuadW(cathedralPatina, Circle(a, 0.34f, 0.16f), Circle(b, 0.34f, 0.16f),
                           Circle(b, 0.15f, 0.19f), Circle(a, 0.15f, 0.19f), Vector3.back);
                mesh.QuadW(ShrineKit.Dark, Circle(a, 0.15f, 0.19f), Circle(b, 0.15f, 0.19f), P(0f, 0f, 0.19f), P(0f, 0f, 0.19f), Vector3.back);
            }
        }

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
