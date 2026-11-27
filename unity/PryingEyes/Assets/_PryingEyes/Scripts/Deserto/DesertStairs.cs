using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>La scala di un livello di torre o di catacomba. Ogni livello ha la stessa stanza della scala (PlanGen.StackLevel: la prima, 6,6 x 3,4 m,
    /// senza pavimento dal secondo livello in su e senza soffitto sotto l'ultimo): un pozzo aperto con due rampe a gradini sospesi, una a fianco
    /// dell'altra. Dal pianerottolo davanti alla porta (a est) la rampa del sud sale verso ovest fino al ripiano di mezzo (2,7 m); la rampa del nord
    /// risale verso est fino al pianerottolo del livello dopo (5,4 m). Alzata 19,3 cm, pedata 27,1 cm: sotto i 45 gradi della navmesh. Nei livelli
    /// senza pavimento il pianerottolo davanti alla porta c'e' comunque, fino alla linea di mezzo del muro. Una lanterna al centro del pozzo.
    /// Tutte le coordinate sono quelle del piano (x e z assoluti, y del livello): la radice del livello si alza dopo, a livello costruito.</summary>
    public static class DesertStairs
    {
        public const float Rise = 0.193f, Tread = 0.271f, Width = 1.40f, Platform = 1.20f;
        const float Thick = 0.30f;
        public const int Risers = 14;

        public static void Build(Transform parent, Plan plan, int level, int levels, bool church, bool roofExit = false)
        {
            ShrineKit.Mats();
            var room = plan.rooms[0];
            float ix0 = room.x0 * 0.1f + FloorBuilder.TH, ix1 = room.x1 * 0.1f - FloorBuilder.TH;
            float iz0 = room.z0 * 0.1f + FloorBuilder.TH, iz1 = room.z1 * 0.1f - FloorBuilder.TH;
            var root = new GameObject("Scala").transform;
            root.SetParent(parent, false);
            var mat = church ? ShrineKit.Stone : ShrineKit.Wood;

            void Box(string name, float x0, float x1, float y0, float y1, float z0, float z1)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = name;
                g.transform.SetParent(root, false);
                g.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
                g.transform.localScale = new Vector3(x1 - x0, y1 - y0, z1 - z0);
                g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }

            float uPlat = ix1 - Platform;
            // fino alla linea di mezzo del muro, come i pavimenti delle altre stanze: sotto la soglia della porta non deve restare un varco (15 cm bastano
            // a staccare la navmesh del pianerottolo da quella della sala)
            if (room.noFloor) Box("Pianerottolo d'ingresso", uPlat, ix1 + FloorBuilder.TH, -Thick, 0f, iz0, iz1);
            if (level >= levels - 1 && !roofExit) return;

            float uTop = uPlat - Tread * (Risers - 1);               // il bordo est del ripiano di mezzo
            for (int i = 1; i < Risers; i++)                          // rampa sud: dal pianerottolo al ripiano, verso ovest
            {
                float xb = uPlat - Tread * (i - 1), xa = xb - Tread;
                Box("Gradino", xa, xb, Rise * i - Thick, Rise * i, iz0, iz0 + Width);
            }
            Box("Ripiano", ix0, uTop, Rise * Risers - Thick, Rise * Risers, iz0, iz1);
            for (int j = 1; j < Risers; j++)                          // rampa nord: dal ripiano al pianerottolo del livello dopo, verso est
            {
                float xa = uTop + Tread * (j - 1), xb = xa + Tread;
                Box("Gradino", xa, xb, Rise * (Risers + j) - Thick, Rise * (Risers + j), iz1 - Width, iz1);
            }
            if (roofExit && level == levels - 1)
            {
                float roof = PlanGen.LevelHeight + 0.14f;
                Box("Roof step", uPlat, uPlat + 0.28f, roof - 0.14f - Thick, roof - 0.14f, iz1 - Width, iz1);
                Box("Roof landing", uPlat + 0.28f, ix1 + FloorBuilder.TH + 0.03f, roof - Thick, roof, iz0 - FloorBuilder.TH, iz1 + FloorBuilder.TH);
                // The last steps exit east onto the adjoining roof. The other edges guard the open shaft.
                Box("Stair well rail", uPlat + 0.28f, uPlat + 0.42f, roof, roof + 0.94f, iz0, iz1 - Width - 0.05f);
                Box("South roof rail", uPlat + 0.28f, ix1 + FloorBuilder.TH, roof, roof + 0.94f, iz0 - 0.14f, iz0);
                Box("North roof rail", uPlat + 0.28f, ix1 + FloorBuilder.TH, roof, roof + 0.94f, iz1, iz1 + 0.14f);
            }
            // il vano tra le due rampe e' largo 30 cm: nessuno ci cade, un parapetto non serve
            ShrineKit.PointLight(root, "Lanterna", new Vector3((ix0 + ix1) * 0.5f, 3.3f, (iz0 + iz1) * 0.5f), new Color(1f, 0.70f, 0.38f), 2.8f, 11f, 0.18f, 2.2f);
        }
    }
}
