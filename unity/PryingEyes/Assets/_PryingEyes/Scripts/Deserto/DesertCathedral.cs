using PryingEyes.Cattedrale;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>La cattedrale nel deserto: e' la stessa di sempre (HubPlan), ma poggia su un basamento di pietra alto come la scarpata di sabbia e
    /// dal sagrato parte una scalinata larga, che scende dal pavimento della chiesa (y = 0) alla sabbia (DesertWorld.GroundY). Qui anche le
    /// postazioni che il deserto sposta nella cattedrale: i leggii dei libri dei perk (Biblioteca dei Voti) e l'altare della consacrazione, che
    /// ci sono solo se le rispettive opere sono state costruite con le risorse portate dal deserto.</summary>
    public static class DesertCathedral
    {
        public const int Steps = 12;
        const float Run = 0.4f, Rise = 0.2f, Landing = 1.0f;

        /// <summary>Basamento e scalinata, figli della cattedrale (spariscono e si rifanno con lei).</summary>
        public static void Build(FloorInstance fi)
        {
            ShrineKit.Mats();
            var root = new GameObject("Basamento e scalinata").transform;
            root.SetParent(fi.root.transform, false);
            float bottom = DesertWorld.GroundY - 1.0f, top = -0.04f;
            // sotto ogni stanza (e un po' oltre i muri): una scarpata di pietra. Il pavimento della chiesa sta a y = 0, la sabbia piu' in basso.
            foreach (var r in fi.plan.rooms)
            {
                float x0 = r.x0 * 0.1f - 0.3f, x1 = r.x1 * 0.1f + 0.3f, z0 = r.z0 * 0.1f - 0.3f, z1 = r.z1 * 0.1f + 0.3f;
                ShrineKit.Prim(root, PrimitiveType.Cube, "Basamento", ShrineKit.StoneDark,
                    new Vector3((x0 + x1) * 0.5f, (top + bottom) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, top - bottom, z1 - z0), default, true);
            }
            // la scalinata: pianerottolo davanti al parapetto, poi dodici gradini da 20 cm di alzata e 40 di pedata, con le guance di pietra ai lati
            float xa = HubPlan.StairX0, xb = HubPlan.StairX1, cx = (xa + xb) * 0.5f, w = xb - xa;
            float floorBot = DesertWorld.GroundY - 0.3f;
            void Block(string name, float z0, float z1, float topY, Material m)
            {
                ShrineKit.Prim(root, PrimitiveType.Cube, name, m, new Vector3(cx, (topY + floorBot) * 0.5f, (z0 + z1) * 0.5f), new Vector3(w, topY - floorBot, z1 - z0), default, true);
            }
            void Cheek(string name, float z0, float z1, float topY)
            {
                foreach (float sx in new[] { xa - 0.15f, xb + 0.15f })
                    ShrineKit.Prim(root, PrimitiveType.Cube, name, ShrineKit.StoneDark, new Vector3(sx, (topY + floorBot) * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.3f, topY - floorBot, z1 - z0), default, true);
            }
            Block("Pianerottolo", -Landing, 0f, 0f, ShrineKit.Stone);
            Cheek("Guancia", -Landing, 0f, 1.0f);
            for (int i = 1; i <= Steps; i++)
            {
                float z1 = -Landing - Run * (i - 1), z0 = z1 - Run, topY = -Rise * i;
                Block("Gradino", z0, z1, topY, ShrineKit.Stone);
                Cheek("Guancia", z0, z1, topY + 1.0f);
            }
            // due bracieri in cima alla scalinata, accesi: si vede da lontano dove si scende, anche di notte
            foreach (float bx in new[] { xa + 0.6f, xb - 0.6f })
            {
                var b = new GameObject("Braciere").transform;
                b.SetParent(root, false);
                b.position = new Vector3(bx, 0f, 0.6f);
                ShrineKit.Prim(b, PrimitiveType.Cylinder, "Coppa", ShrineKit.Iron, new Vector3(0f, 0.55f, 0f), new Vector3(0.7f, 0.55f, 0.7f), default, true);
                var fire = ShrineKit.Prim(b, PrimitiveType.Cylinder, "Brace", ProcKit.HasMat("kit_brace") ? ProcKit.Mat("kit_brace") : ShrineKit.EmitOwn(new Color(1f, 0.42f, 0.12f), 3f),
                    new Vector3(0f, 1.06f, 0f), new Vector3(0.6f, 0.02f, 0.6f));
                fire.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ShrineKit.PointLight(b, "Fuoco", new Vector3(0f, 1.5f, 0f), new Color(1f, 0.52f, 0.2f), 5.5f, 14f);
            }
        }

        /// <summary>I leggii dei libri e l'altare della consacrazione, se le loro opere sono state costruite. Prima della navmesh (sono ostacoli).</summary>
        public static void PlaceStations(FloorInstance fi)
        {
            var plan = fi.plan;
            var rng = new Rng(plan.seed + 411);
            if (Fabbrica.Costruita("biblioteca_voti") && Biblioteca.Current == null) Biblioteca.Place(fi, plan, plan.rooms[HubPlan.Navata], rng);
            if (Fabbrica.Costruita("consacrazione")) Altare.Place(fi, plan, plan.rooms[HubPlan.Presbiterio], rng);
        }

        /// <summary>La Biblioteca ha offerto tre libri e uno e' stato comprato: dopo un po' i leggii si rinnovano con tre perk nuovi (uno per
        /// bottino, senza dover tornare a un altro piano come nelle catacombe).</summary>
        public static void RefreshLibrary(FloorInstance fi)
        {
            var lib = Biblioteca.Current;
            if (lib != null)
            {
                if (lib.root != null) { fi.propRoots.Remove(lib.root.transform); lib.root.SetActive(false); Object.Destroy(lib.root); }
                Biblioteca.Clear();
            }
            if (!Fabbrica.Costruita("biblioteca_voti")) return;
            var plan = fi.plan;
            var rng = new Rng(plan.seed + 977 + Time.frameCount);
            Biblioteca.Place(fi, plan, plan.rooms[HubPlan.Navata], rng);
            Physics.SyncTransforms();
        }
    }
}
