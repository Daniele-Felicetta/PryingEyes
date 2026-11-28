using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    sealed class DesertLakeLife : MonoBehaviour
    {
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Fish> fish = new List<Fish>();
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        DesertLake lake;
        Material material;
        System.Random random;
        int variant;
        static readonly Color GrassShadow = new Color(0.065f, 0.21f, 0.12f), GrassTip = new Color(0.38f, 0.56f, 0.19f);
        static readonly Color ReedShadow = new Color(0.12f, 0.24f, 0.09f), ReedTip = new Color(0.51f, 0.58f, 0.22f);

        sealed class Fish
        {
            public Transform body;
            public int school;
            public float angle, lane, depth;
        }

        internal int FishCount => fish.Count;
        internal int ReefPatches { get; private set; }
        internal Transform Landmark { get; private set; }

        internal void Build(DesertLake value, int style)
        {
            var shader = Resources.Load<Shader>("Shaders/LakeLife");
            if (shader == null) return;
            lake = value;
            variant = style;
            random = new System.Random(unchecked(DesertWorld.Seed * 17119 + style * 197));
            material = new Material(shader);
            material.SetFloat("_WaterLevel", lake.Surface);
            BuildLandmark();
            BuildGarden();
            BuildSchools();
        }

        Vector3 Bed(float x, float z, float lift = 0f)
        {
            float px = lake.X + x, pz = lake.Z + z;
            return new Vector3(px, lake.Surface - DesertLake.BedDepth(lake.RadiusAt(px, pz)) + lift, pz);
        }

        float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

        Transform NewGroup(string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(transform, false);
            return group;
        }

        void Tint(Renderer renderer, Color color)
        {
            renderer.sharedMaterial = material;
            properties.SetColor("_Tint", color);
            renderer.SetPropertyBlock(properties);
        }

        GameObject Display(string name, Transform parent, Mesh mesh, Vector3 position, Vector3 size, Quaternion rotation, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.transform.localScale = size;
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Tint(renderer, color);
            return obj;
        }

        void Stone(string name, Vector3 position, Vector3 size, Quaternion rotation, Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(Landmark, false);
            block.transform.SetPositionAndRotation(position, rotation);
            block.transform.localScale = size;
            Tint(block.GetComponent<Renderer>(), color);
        }

        void BuildLandmark()
        {
            Landmark = NewGroup(variant == 0 ? "Sunken arcade" : "Drowned courtyard");
            Color stone = new Color(0.53f, 0.45f, 0.31f);
            Color trim = new Color(0.37f, 0.43f, 0.35f);
            var profile = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.49f, 0f), new Vector2(0.49f, 0.12f), new Vector2(0.38f, 0.19f),
                new Vector2(0.29f, 0.29f), new Vector2(0.29f, 2.21f), new Vector2(0.37f, 2.38f),
                new Vector2(0.45f, 2.43f), new Vector2(0.45f, 2.56f), new Vector2(0f, 2.56f)
            };
            var column = DesertLathe.Turn("Carved limestone shaft", profile, 14);
            meshes.Add(column);
            float lane = variant == 0 ? 2.4f : 1.4f;
            float bed = Bed(0f, lane).y - 0.13f;
            Vector3 At(float x, float y, float z) => new Vector3(lake.X + x, bed + y, lake.Z + z);
            if (variant == 0)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    BuildColumn(column, At(side * 1.64f, 0f, lane), 1f, stone);
                    BuildColumn(column, Bed(side * 4.1f, lane, -0.20f), side == -1 ? 0.63f : 0.79f, trim);
                }
                for (int i = 0; i < 11; i++)
                {
                    float theta = i * Mathf.PI / 10f;
                    Vector3 p = At(Mathf.Cos(theta) * 1.64f, 2.48f + Mathf.Sin(theta) * 1.64f, lane);
                    Stone("Arch voussoir", p, new Vector3(0.42f, 0.49f, 0.72f), Quaternion.Euler(0f, 0f, theta * Mathf.Rad2Deg - 90f), i == 5 ? trim : stone);
                }
            }
            else
            {
                for (int i = 0; i < 7; i++)
                {
                    float theta = i * 2f * Mathf.PI / 7f;
                    float x = Mathf.Cos(theta) * 3.8f, z = lane + Mathf.Sin(theta) * 3.8f;
                    BuildColumn(column, Bed(x, z, -0.20f), 0.44f + (i % 3) * 0.24f, i % 2 == 0 ? stone : trim);
                }
                Stone("Foundation", At(0f, 0.16f, lane), new Vector3(3.5f, 0.38f, 2.8f), Quaternion.Euler(0, 22, 0), stone);
                Stone("Carved stela", At(0f, 1.18f, lane), new Vector3(1.36f, 2.05f, 0.43f), Quaternion.Euler(4f, 22f, -12f), trim);
                Stone("Broken pediment", At(0.7f, 0.6f, lane - 1.7f), new Vector3(1.4f, 0.58f, 0.48f), Quaternion.Euler(15f, 40f, -28f), stone);
            }
            for (int i = 0; i < 12; i++)
            {
                float x = Range(-1.7f, 1.7f), z = lane - 2.2f - i * 0.57f;
                Stone("Broken paving", Bed(x, z, -0.07f), new Vector3(Range(0.48f, 1.06f), 0.27f, Range(0.52f, 0.94f)),
                    Quaternion.Euler(Range(-8f, 8f), Range(-20f, 20f), Range(-10f, 10f)), i % 3 == 0 ? trim : stone);
            }
        }

        void BuildColumn(Mesh column, Vector3 position, float height, Color color)
        {
            var piece = Display("Eroded column", Landmark, column, position, new Vector3(1f, height, 1f), Quaternion.identity, color);
            var collider = piece.AddComponent<BoxCollider>();
            collider.center = Vector3.up * 1.27f;
            collider.size = new Vector3(0.73f, 2.55f, 0.73f);
        }

        void BuildGarden()
        {
            Transform garden = NewGroup("Eelgrass, pondweed and river stones");
            var rock = Own(PropMeshes.LakeCobble());
            var blades = new ColouredMesh();
            for (int i = 0; i < 78; i++)
            {
                float angle = i * (2.399963f) + variant * 0.75f;
                float radius = i < 44 ? Range(0.64f, 0.90f) : i < 66 ? Range(0.31f, 0.64f) : Range(0.16f, 0.30f);
                var (x, z) = lake.PointAt(angle, radius);
                Vector3 root = Bed(x - lake.X, z - lake.Z, -0.09f);
                if (i % 3 == 0)
                    Display("Waterworn stone", garden, rock, root, new Vector3(Range(0.49f, 1.6f), Range(0.26f, 0.62f), Range(0.50f, 1.25f)),
                        Quaternion.Euler(Range(-12f, 12f), angle * Mathf.Rad2Deg, 0f),
                        new Color(Range(0.26f, 0.40f), Range(0.27f, 0.38f), Range(0.18f, 0.29f)));
                bool pondweed = i % 3 == variant;
                for (int j = 0; j < 20; j++)
                {
                    float offset = j < 10 ? 0.75f : 1.55f;
                    float px = x + Range(-offset, offset), pz = z + Range(-offset, offset);
                    float height = DesertLake.BedDepth(lake.RadiusAt(px, pz)) - 0.22f;
                    if (height > 0.36f) AddBlade(blades, Bed(px - lake.X, pz - lake.Z, -0.065f), pondweed, height);
                }
                ReefPatches++;
            }
            Display("Aquatic plant beds", garden, Own(blades.Finish("Swaying aquatic plants")), transform.position, Vector3.one, Quaternion.identity, Color.white);
            BuildShore(rock);
        }

        void BuildShore(Mesh rock)
        {
            Transform shore = NewGroup("Reeds and stones at the waterline");
            var reeds = new ColouredMesh();
            for (int patch = 0; patch < 9; patch++)
            {
                float heading = patch * 2.399963f + variant * 0.65f + Range(-0.10f, 0.10f);
                for (int stem = 0; stem < 38; stem++)
                {
                    var (x, z) = lake.PointAt(heading + Range(-0.13f, 0.13f), Range(0.95f, 1.065f));
                    Vector3 root = new Vector3(x, DesertWorld.Height(x, z) - 0.07f, z);
                    AddReed(reeds, root);
                }
                var (px, pz) = lake.PointAt(heading, Range(0.961f, 1.018f));
                Display("Shore cobble", shore, rock, new Vector3(px, DesertWorld.Height(px, pz) - 0.08f, pz),
                    new Vector3(Range(0.76f, 1.5f), Range(0.30f, 0.65f), Range(0.68f, 1.28f)),
                    Quaternion.Euler(Range(-13f, 13f), heading * Mathf.Rad2Deg, Range(-15f, 15f)), new Color(0.38f, 0.31f, 0.21f));
                Vector3 nearby = new Vector3(px + Mathf.Cos(heading) * 0.85f, 0f, pz + Mathf.Sin(heading) * 0.85f);
                nearby.y = DesertWorld.Height(nearby.x, nearby.z) - 0.043f;
                Display("Tumbled pebbles", shore, rock, nearby, new Vector3(0.54f, 0.24f, 0.69f),
                    Quaternion.Euler(0f, heading * Mathf.Rad2Deg + 32f, 0f), new Color(0.31f, 0.29f, 0.23f));
            }
            Display("Shore sedges", shore, Own(reeds.Finish("Reeds with seed heads")), transform.position, Vector3.one, Quaternion.identity, Color.white);
        }

        void AddReed(ColouredMesh mesh, Vector3 root)
        {
            float angle = Range(0f, Mathf.PI * 2f), height = Range(0.66f, 1.6f);
            Vector3 edge = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 bend = Vector3.Cross(Vector3.up, edge) * Range(0.09f, 0.37f);
            Color green = Color.Lerp(new Color(0.12f, 0.19f, 0.061f), new Color(0.54f, 0.50f, 0.23f), Range(0f, 1f));
            bool head = Range(0f, 1f) > 0.66f;
            int first = mesh.Count;
            for (int step = 0; step <= 6; step++)
            {
                float t = step / 6f;
                Vector3 centre = root - transform.position + Vector3.up * (t * height) + bend * (t * t);
                bool seedHead = head && step >= 5;
                Color tint = seedHead ? new Color(0.34f, 0.23f, 0.12f) : green * (0.65f + t * 0.43f);
                float radius = seedHead && step == 5 ? 0.039f : Mathf.Lerp(0.023f, 0.003f, t);
                mesh.Vertex(centre - edge * radius, tint, new Vector2(t, 1f));
                mesh.Vertex(centre + edge * radius, tint, new Vector2(t, 1f));
                if (step > 0) mesh.Quad(first + step * 2 - 2, first + step * 2 - 1, first + step * 2 + 1, first + step * 2);
            }
        }

        void AddBlade(ColouredMesh blades, Vector3 root, bool pondweed, float maxHeight)
        {
            float angle = Range(0f, Mathf.PI * 2f), height = Mathf.Min(maxHeight, Range(0.40f, pondweed ? 2.1f : 1.54f));
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 across = Vector3.Cross(direction, Vector3.up) * Range(0.047f, 0.114f);
            Color color = Color.Lerp(pondweed ? ReedShadow : GrassShadow, pondweed ? ReedTip : GrassTip, Range(0f, 1f));
            int first = blades.Count;
            for (int k = 0; k <= 5; k++)
            {
                float t = k / 5f;
                Vector3 p = root - transform.position + Vector3.up * (height * t) + direction * (t * t * height * 0.47f);
                float leafWidth = pondweed ? 0.14f + Mathf.Sin(t * Mathf.PI) * (1.43f + 0.36f * Mathf.Sin(k * 1.8f + angle)) : Mathf.Lerp(1f, 0.06f, t);
                Vector3 width = across * leafWidth;
                Color leaf = color * Mathf.Lerp(0.54f, 1.10f, t);
                blades.Vertex(p - width, leaf * 0.91f, new Vector2(t, 1f));
                blades.Vertex(p + direction * (width.magnitude * 0.22f), leaf * 1.13f, new Vector2(t, 1f));
                blades.Vertex(p + width, leaf * 0.86f, new Vector2(t, 1f));
                if (k > 0)
                {
                    int next = first + k * 3;
                    blades.Quad(next - 3, next - 2, next + 1, next);
                    blades.Quad(next - 2, next - 1, next + 2, next + 1);
                }
            }
        }

        void BuildSchools()
        {
            Mesh model = Own(FishMesh());
            Transform schools = NewGroup("Lake fish at four depths");
            Color[] colors = { new Color(0.91f, 0.82f, 0.51f), new Color(0.54f, 0.91f, 0.94f),
                new Color(0.96f, 0.57f, 0.31f), new Color(0.74f, 0.89f, 0.56f) };
            for (int group = 0; group < colors.Length; group++)
                for (int i = 0; i < (group == 3 ? 8 : 12); i++)
                {
                    var f = new Fish { school = group, angle = Range(-0.22f, 0.22f), lane = Range(-1.65f, 1.65f), depth = Range(-0.37f, 0.37f) };
                    f.body = Display("Lake fish", schools, model, Bed(0f, 0f, 5f), Vector3.one * (group == 3 ? Range(0.55f, 0.75f) : Range(0.32f, 0.52f)),
                        Quaternion.identity, colors[group]).transform;
                    MoveFish(f, Time.time, 0f);
                    fish.Add(f);
                }
        }

        static Mesh FishMesh()
        {
            var body = new ColouredMesh();
            float[] z = { -0.37f, -0.23f, 0.04f, 0.25f, 0.39f, 0.45f };
            float[] width = { 0.023f, 0.052f, 0.116f, 0.108f, 0.073f, 0.012f };
            for (int ring = 0; ring < z.Length; ring++)
                for (int i = 0; i < 10; i++)
                {
                    float a = i * (2f * Mathf.PI / 10f);
                    float y = Mathf.Sin(a);
                    Color color = Color.Lerp(new Color(0.21f, 0.33f, 0.36f), new Color(0.95f, 0.93f, 0.75f), (1f - y) * 0.5f);
                    body.Vertex(new Vector3(Mathf.Cos(a) * width[ring], y * width[ring] * 1.42f, z[ring]), color,
                        new Vector2(Mathf.InverseLerp(0.10f, -0.65f, z[ring]), 2f));
                    if (ring == 0) continue;
                    int next = (i + 1) % 10, start = (ring - 1) * 10;
                    body.Quad(start + i, start + next, start + next + 10, start + i + 10);
                }
            Color fin = new Color(0.54f, 0.51f, 0.35f);
            body.Fin(new Vector3(0f, 0f, -0.30f), new Vector3(0f, 0.22f, -0.65f), new Vector3(0f, 0.015f, -0.53f), fin);
            body.Fin(new Vector3(0f, 0f, -0.30f), new Vector3(0f, 0.015f, -0.53f), new Vector3(0f, -0.18f, -0.65f), fin);
            body.Fin(new Vector3(0f, 0.08f, -0.17f), new Vector3(0f, 0.25f, 0.04f), new Vector3(0f, 0.11f, 0.18f), fin);
            for (int side = -1; side <= 1; side += 2)
            {
                body.Fin(new Vector3(side * 0.07f, -0.03f, 0.12f), new Vector3(side * 0.21f, -0.11f, -0.11f), new Vector3(side * 0.08f, -0.08f, -0.04f), fin);
                Vector3 eye = new Vector3(side * 0.105f, 0.031f, 0.27f);
                Color pupil = new Color(0.025f, 0.068f, 0.065f);
                body.Fin(eye + Vector3.up * 0.025f, eye - Vector3.up * 0.022f - Vector3.forward * 0.020f, eye + Vector3.forward * 0.028f, pupil);
            }
            return body.Finish("Lake fish with articulated tail");
        }

        void Update()
        {
            if (fish.Count == 0) return;
            var player = LocalTarget.P;
            if (player == null || !DesertLakeSurface.Near(player.transform.position) && !Seats.Split) return;
            foreach (var f in fish) MoveFish(f, Time.time, Time.deltaTime);
        }

        void MoveFish(Fish f, float time, float dt)
        {
            bool bottom = f.school == 3;
            float speed = f.school % 2 == 1 ? -0.17f : 0.19f + f.school * 0.035f;
            float a = time * speed + f.angle + f.school * 2.08f + variant;
            float rx = (bottom ? 6.0f : 4.6f + f.school * 2.4f) + f.lane;
            float rz = (bottom ? 7.8f : 4.3f + f.school * 1.6f) + f.lane * 0.7f;
            float depth = (bottom ? 4.9f : 1.10f + f.school * 1.20f) + f.depth;
            Vector3 target = new Vector3(lake.X + Mathf.Cos(a) * rx, lake.Surface - depth +
                Mathf.Sin(a * 1.6f + f.lane) * 0.38f, lake.Z + Mathf.Sin(a) * rz);
            var player = LocalTarget.P;
            if (dt > 0f && player != null && player.cam != null)
            {
                Vector3 away = target - player.cam.position;
                target += away.normalized * (1f - Mathf.InverseLerp(0.4f, 2.35f, away.magnitude)) * 1.6f;
            }
            target.y = Mathf.Clamp(target.y, lake.Surface - DesertLake.BedDepth(lake.RadiusAt(target.x, target.z)) + 0.45f, lake.Surface - 0.36f);
            Vector3 prior = f.body.position;
            Vector3 heading = new Vector3(-Mathf.Sin(a) * rx * speed, 0.15f * Mathf.Cos(a * 1.6f + f.lane), Mathf.Cos(a) * rz * speed);
            float blend = dt > 0f ? 1f - Mathf.Exp(-5f * dt) : 1f;
            f.body.position = Vector3.Lerp(prior, target, blend);
            Vector3 motion = f.body.position - prior;
            f.body.rotation = Quaternion.Slerp(f.body.rotation, Quaternion.LookRotation(dt > 0f && motion.sqrMagnitude > 1e-5f ? motion : heading), blend);
        }

        Mesh Own(Mesh mesh) { meshes.Add(mesh); return mesh; }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            foreach (var mesh in meshes) Destroy(mesh);
        }

        sealed class ColouredMesh
        {
            readonly List<Vector3> points = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<Vector2> modelData = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            public int Count => points.Count;
            public void Vertex(Vector3 p, Color color, Vector2 data) { points.Add(p); colors.Add(color); modelData.Add(data); }
            public void Quad(int a, int b, int c, int d)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
            public void Fin(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                int start = Count;
                foreach (Vector3 p in new[] { a, b, c }) Vertex(p, color, new Vector2(Mathf.InverseLerp(0.10f, -0.65f, p.z), 2f));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(points);
                mesh.SetColors(colors);
                mesh.SetUVs(1, modelData);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
