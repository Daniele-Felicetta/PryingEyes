using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    public static partial class RoomKits
    {
        /// <summary>Un lampione del sagrato del deserto: base di pietra, un fusto di ferro tornito (piede svasato, due anelli, un collare) e in cima una lanterna a sei facce con i suoi montanti,
        /// il vetro che brilla, il piatto, il tetto a cono e il puntale. Prima era un palo nero con una scatola. Il ferro e' una mesh sola, il vetro un'altra (e' l'unico pezzo che brilla di suo);
        /// la luce e' quella di sempre.</summary>
        static void HubLampPostDeserto(Ctx c, Vector3 at)
        {
            var root = new GameObject("Lampione");
            root.transform.SetParent(c.props, false);
            root.transform.position = at;
            var t = root.transform;
            ShrineKit.Prim(t, PrimitiveType.Cube, "Base", ShrineKit.Stone, new Vector3(0f, 0.2f, 0f), new Vector3(0.42f, 0.4f, 0.42f), default, true);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Cimasa", ShrineKit.Stone, new Vector3(0f, 0.41f, 0f), new Vector3(0.50f, 0.05f, 0.50f));
            var iron = Deserto.DesertLamps.IronMesh();
            var post = ShrineKit.LatheObj(t, "Ferro", ShrineKit.Iron, iron);
            post.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var cap = post.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0f, 1.7f, 0f); cap.radius = 0.09f; cap.height = 3.0f;
            var glass = ShrineKit.LatheObj(t, "Vetro", ShrineKit.EmitOwn(Lantern, 2.4f), Deserto.DesertLamps.GlassMesh());
            HubLight(c, at + Vector3.up * 3.2f, Lantern, 3.2f, 11f);
        }

        /// <summary>Nel deserto le armi buone si comprano solo qui, nella cattedrale, e costano il doppio dei punti.</summary>
        public const int ArmoryPriceMul = 2;

        /// <summary>L'armeria della cattedrale del deserto (HubPlan.Armeria): tutte le armi tranne la pistola di partenza, una per pannello sui muri,
        /// dalle piu' modeste vicino alla porta alle reliquie in fondo; ognuna al doppio del suo prezzo di sempre. Una luce calda in ogni angolo e
        /// un tavolo al centro.</summary>
        static void HubArmory(Ctx c)
        {
            ShrineKit.Mats();
            var picks = new List<int>();
            int start = WeaponDefs.IndexOf("p9_pieta");
            for (int i = 0; i < WeaponDefs.All.Length; i++)
            {
                var d = WeaponDefs.All[i];
                if (i != start && !d.cityOnly) picks.Add(i);
            }
            picks.Sort((a, b) =>
            {
                int g = WeaponDefs.All[a].grade.CompareTo(WeaponDefs.All[b].grade);
                return g != 0 ? g : WeaponDefs.All[a].price.CompareTo(WeaponDefs.All[b].price);
            });

            const float y = 1.55f, pitch = 1.62f, gap = 0.03f;
            var slots = new List<(Vector3 pos, Vector3 face)>();
            // un tratto di muro: i pannelli a passo fisso, con il varco della porta saltato
            void Run(bool alongX, float line, float from, float to, Vector3 inward, float doorFrom = 1e9f, float doorTo = -1e9f)
            {
                for (float s = from; s <= to + 0.001f; s += pitch)
                {
                    if (s > doorFrom - 0.8f && s < doorTo + 0.8f) continue;
                    Vector3 pos = alongX ? new Vector3(s, y, line - inward.z * gap) : new Vector3(line - inward.x * gap, y, s);
                    slots.Add((pos, inward));
                }
            }
            float doorZ = 31.0f;        // la porta dalla navatella est (HubPlan: x = 300 dm, z = 310 dm, larga 1,4 m)
            Run(false, c.ix0, c.iz0 + 1.0f, c.iz1 - 1.0f, Vector3.right, doorZ - 0.7f, doorZ + 0.7f);
            Run(true, c.iz0, c.ix0 + 1.0f, c.ix1 - 1.0f, Vector3.forward);
            Run(false, c.ix1, c.iz0 + 1.0f, c.iz1 - 1.0f, Vector3.left);
            Run(true, c.iz1, c.ix0 + 1.0f, c.ix1 - 1.0f, Vector3.back);
            int n = Mathf.Min(picks.Count, slots.Count);
            if (picks.Count > slots.Count) Debug.LogWarning($"[PryingEyes] armeria: {picks.Count} armi per {slots.Count} pannelli");
            for (int i = 0; i < n; i++)
            {
                var def = WeaponDefs.All[picks[i]];
                WallBuy.Place(picks[i], def.price * ArmoryPriceMul, slots[i].pos, Quaternion.LookRotation(slots[i].face), def.GradeColor);
            }
            foreach (float lx in new[] { 0.25f, 0.75f })
                foreach (float lz in new[] { 0.28f, 0.72f })
                    HubLight(c, new Vector3(Mathf.Lerp(c.ix0, c.ix1, lx), c.r.wallH - 0.7f, Mathf.Lerp(c.iz0, c.iz1, lz)), Warm, 4.2f, 10f);
            HubProp(c, "arr_tavolo", c.Center, Vector3.forward);
            HubCandles(c, c.Center + new Vector3(0.9f, 0f, 0.5f), 0f, 1);
        }
    }
}
