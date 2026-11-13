using UnityEngine;

namespace PryingEyes.Proc
{
    public static partial class FloorBuilder
    {
        static void BuildRoundedCorners(Room room, Style style, GameObject parent)
        {
            var mb = new MeshBuilder();
            float r = room.cornerRadius - TH, height = room.wallH;
            for (int corner = 0; corner < 4; corner++)
            {
                bool east = corner == 0 || corner == 3, north = corner < 2;
                var center = new Vector3(east ? room.x1 * 0.1f - room.cornerRadius : room.x0 * 0.1f + room.cornerRadius,
                    0, north ? room.z1 * 0.1f - room.cornerRadius : room.z0 * 0.1f + room.cornerRadius);
                for (int segment = 0; segment < 20; segment++)
                {
                    float a = (corner * 90f + segment * 4.5f) * Mathf.Deg2Rad;
                    float b = (corner * 90f + (segment + 1) * 4.5f) * Mathf.Deg2Rad;
                    Vector3 da = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), db = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                    Vector3 pa = center + da * r, pb = center + db * r;
                    void Face(Material material, float bottom, float top)
                    {
                        mb.QuadSmooth(material, pa + Vector3.up * bottom, pb + Vector3.up * bottom,
                            pb + Vector3.up * top, pa + Vector3.up * top, -da, -db, -db, -da,
                            new Vector2(a * r, bottom), new Vector2(b * r, bottom), new Vector2(b * r, top), new Vector2(a * r, top));
                    }
                    float dado = style.dado != null ? style.dadoH : 0f;
                    if (dado > 0f) Face(style.dado, 0f, dado);
                    Face(style.wall, dado, height);
                    Vector3 qa = center + da * (r + TH), qb = center + db * (r + TH);
                    mb.QuadW(style.outer, qa, qa + Vector3.up * height, qb + Vector3.up * height, qb, da + db);
                    mb.QuadW(style.trim, pa + Vector3.up * height, pb + Vector3.up * height, qb + Vector3.up * height, qa + Vector3.up * height, Vector3.up);
                    mb.Beam(style.trim, pa + Vector3.up * 0.12f, pb + Vector3.up * 0.12f, 0.07f, 0.24f, Vector3.up);
                    mb.Beam(style.trim, pa + Vector3.up * (height - 0.16f), pb + Vector3.up * (height - 0.16f), 0.12f, 0.20f, Vector3.up);
                }
            }
            Flush(parent, "Pareti curve", mb, true, null);
        }
    }
}
