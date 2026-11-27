using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Deserto
{
    static class DesertTowerTraversal
    {
        public static void Balcony(Transform floor, Room room)
        {
            ShrineKit.Mats();
            var at = new GameObject("Tower balcony").transform;
            at.SetParent(floor, false);
            at.localPosition = new Vector3(room.x1 * 0.1f + FloorBuilder.TH - 0.04f, 0f, PlanGen.TowerBalconyCenter(room) * 0.1f);
            Box("Stone landing", new Vector3(0.85f, -0.13f, 0f), new Vector3(1.78f, 0.26f, 2.42f), ShrineKit.Stone);
            Box("Outer parapet", new Vector3(1.65f, 0.45f, 0f), new Vector3(0.20f, 0.90f, 2.38f), ShrineKit.StoneDark);
            Box("Coping", new Vector3(1.65f, 0.94f, 0f), new Vector3(0.27f, 0.10f, 2.48f), ShrineKit.Stone);
            for (int i = -1; i <= 1; i += 2)
                Box("Stone corbel", new Vector3(0.35f, -0.43f, i * 0.78f), new Vector3(0.80f, 0.38f, 0.27f), ShrineKit.StoneDark);

            void Box(string name, Vector3 center, Vector3 size, Material material)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = name;
                part.transform.SetParent(at, false);
                part.transform.localPosition = center;
                part.transform.localScale = size;
                part.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }
    }
}
