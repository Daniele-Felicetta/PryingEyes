using UnityEngine;

namespace PryingEyes.Proc
{
    public sealed class GalleryRoute
    {
        public Room room;
        public Vector3[] points;
    }

    public static partial class FloorBuilder
    {
        public const float GalleryHeight = 3.6f, GalleryWidth = 2.4f;

        static void BuildGallery(FloorInstance fi, Room room, GameObject parent)
        {
            float start = room.x0 * 0.1f + 2f, top = start + 7.2f, end = room.x1 * 0.1f - 2f;
            float south = room.z0 * 0.1f + 2f, north = room.z1 * 0.1f - 2f - GalleryWidth;
            const float h = GalleryHeight, width = GalleryWidth;
            var mb = new MeshBuilder();
            var stone = Mat("mur_pietra"); var floor = Mat("pav_parquet"); var iron = Mat("ferro");
            void Slab(float x0, float x1, float z0, float z1)
            {
                mb.Box(stone, new Vector3((x0 + x1) * 0.5f, h - 0.14f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.26f, z1 - z0));
                mb.QuadW(floor, new Vector3(x0, h, z0), new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, h, z0), Vector3.up);
            }
            Slab(top, end, south, south + width);
            Slab(top, end, north, north + width);
            Slab(end - width, end, south + width, north);

            void Rail(Vector3 a, Vector3 b)
            {
                mb.Beam(iron, a + Vector3.up * 1.02f, b + Vector3.up * 1.02f, 0.07f, 0.07f, Vector3.up);
                mb.Beam(iron, a + Vector3.up * 0.45f, b + Vector3.up * 0.45f, 0.035f, 0.035f, Vector3.up);
                int posts = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 1.2f));
                for (int i = 0; i <= posts; i++)
                    mb.Box(iron, Vector3.Lerp(a, b, (float)i / posts) + Vector3.up * 0.5f, new Vector3(0.045f, 1.04f, 0.045f));
            }
            foreach (float z in new[] { south, north })
            {
                // Diciotto alzate da 20 cm: stessa scala per giocatore e navmesh dei nemici.
                for (int i = 0; i < 18; i++)
                {
                    float y = (i + 1) * 0.2f;
                    mb.Box(stone, new Vector3(start + (i + 0.5f) * 0.4f, y * 0.5f, z + width * 0.5f), new Vector3(0.4f, y, width));
                }
                Rail(new Vector3(start, 0, z), new Vector3(top, h, z));
                Rail(new Vector3(start, 0, z + width), new Vector3(top, h, z + width));
            }
            Rail(new Vector3(top, h, south), new Vector3(end, h, south));
            Rail(new Vector3(top, h, north + width), new Vector3(end, h, north + width));
            Rail(new Vector3(top, h, south + width), new Vector3(end - width, h, south + width));
            Rail(new Vector3(top, h, north), new Vector3(end - width, h, north));
            Rail(new Vector3(end, h, south), new Vector3(end, h, north + width));
            Rail(new Vector3(end - width, h, south + width), new Vector3(end - width, h, north));
            foreach (float x in new[] { top + 0.35f, end - 0.35f })
                foreach (float z in new[] { south + 0.35f, north + width - 0.35f })
                    mb.Box(stone, new Vector3(x, (h - 0.27f) * 0.5f, z), new Vector3(0.25f, h - 0.27f, 0.25f));
            // Architettura, non arredo: le superfici superiori devono entrare nella navmesh come pavimento.
            Flush(parent, "Balconata a due scale", mb, true, null);
            var points = new[] {
                new Vector3(start - 1f, 0.05f, south + width * 0.5f),
                new Vector3(top + 0.6f, h + 0.05f, south + width * 0.5f),
                new Vector3(end - width * 0.5f, h + 0.05f, south + width * 0.5f),
                new Vector3(end - width * 0.5f, h + 0.05f, north + width * 0.5f),
                new Vector3(top + 0.6f, h + 0.05f, north + width * 0.5f),
                new Vector3(start - 1f, 0.05f, north + width * 0.5f)
            };
            fi.galleries.Add(new GalleryRoute { room = room, points = points });
            fi.groundSpawns.Add(points[2]); fi.groundSpawns.Add(points[3]);
            foreach (float z in new[] { south + width * 0.5f, north + width * 0.5f })
            {
                var go = new GameObject("Luce sotto balconata"); go.transform.SetParent(parent.transform, false);
                go.transform.position = new Vector3((top + end) * 0.5f, h - 0.65f, z);
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 8f;
                light.color = new Color(1f, 0.77f, 0.48f); light.intensity = 1.4f; light.shadows = LightShadows.None;
                LightPool.Register(light, false, true);
            }
        }
    }
}
