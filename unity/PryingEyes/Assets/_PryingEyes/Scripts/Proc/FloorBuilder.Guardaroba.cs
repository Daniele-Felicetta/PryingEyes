using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Il fuori del Guardaroba: fuori dai muri non c'e' il nero ma il cielo notturno con la luna rossa (visibile dalle terrazze e dalle
    /// finestre) e, molto sotto il piano, un lago di lava che illumina di rosso la nebbia. Il piano galleggia sopra la lava: chi cade nel vuoto
    /// ci va incontro finche' FloorDirector.FallGuard non lo riporta su (a -20 m).</summary>
    public static partial class FloorBuilder
    {
        /// <summary>Quota del lago di lava: sotto i piani e le cripte (-4,4 m), sopra la rete di sicurezza di FallGuard (-20 m).</summary>
        public const float LavaLakeY = -17f;

        static void BuildGuardarobaSky(FloorInstance fi)
        {
            var plan = fi.plan;
            var root = new GameObject("Cielo e lava");
            root.transform.SetParent(fi.root.transform, false);

            var sky = GuardarobaKit.Sky();
            if (sky != null)
            {
                var dome = new GameObject("Cielo");
                dome.transform.SetParent(root.transform, false);
                dome.AddComponent<MeshFilter>().sharedMesh = GuardarobaKit.SkyDome();
                var dr = dome.AddComponent<MeshRenderer>();
                dr.sharedMaterial = sky;
                dr.shadowCastingMode = ShadowCastingMode.Off; dr.receiveShadows = false;
            }

            // il lago: un piano enorme sotto il piano di gioco, con la lava che scorre (LavaFlow); senza collider
            float cx = M(plan.minX + plan.maxX) * 0.5f, cz = M(plan.minZ + plan.maxZ) * 0.5f, half = 600f, tile = 16f;
            var mb = new MeshBuilder();
            Vector3 a = new Vector3(cx - half, LavaLakeY, cz - half), b = new Vector3(cx - half, LavaLakeY, cz + half);
            Vector3 c = new Vector3(cx + half, LavaLakeY, cz + half), d = new Vector3(cx + half, LavaLakeY, cz - half);
            mb.Quad(GuardarobaKit.Lava(), a, b, c, d,
                    new Vector2(a.x / tile, a.z / tile), new Vector2(b.x / tile, b.z / tile), new Vector2(c.x / tile, c.z / tile), new Vector2(d.x / tile, d.z / tile), Vector3.up);
            var lake = new GameObject("Lago di lava");
            lake.transform.SetParent(root.transform, false);
            var mesh = mb.ToMesh("Lago di lava", out var mats);
            lake.AddComponent<MeshFilter>().sharedMesh = mesh;
            var lr = lake.AddComponent<MeshRenderer>();
            lr.sharedMaterials = mats;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
            root.AddComponent<LavaFlow>();
        }
    }
}
