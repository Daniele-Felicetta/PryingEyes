using UnityEngine;

namespace PryingEyes.Deserto
{
    public static partial class PropMeshes
    {
        internal const int BiomeTreeKinds = (int)Prop.FrostFir - (int)Prop.Broadleaf + 1;
        static Mesh[] alternateTrees;

        internal static bool IsBiomeTree(Prop kind) => kind == Prop.Broadleaf || kind == Prop.Fir || kind == Prop.FrostFir;

        static Mesh BiomeTree(Prop kind, bool distant, bool alternate = false) =>
            Pista.PistaWorld.WorldTree(kind != Prop.Broadleaf, kind == Prop.FrostFir, distant, alternate);

        internal static Mesh AlternateTree(Prop kind, bool distant)
        {
            alternateTrees ??= new Mesh[BiomeTreeKinds * 2];
            int slot = (int)kind - (int)Prop.Broadleaf + (distant ? BiomeTreeKinds : 0);
            return alternateTrees[slot] ??= BiomeTree(kind, distant, true);
        }

        internal static Mesh LakeCobble() => Rock("Rounded lake cobbles", 2327, Color.white, Color.white, true);

        static Mesh MeadowGrass(bool flowers)
        {
            var mesh = new PM(Surf.Organic);
            var rng = new System.Random(flowers ? 6791 : 5077);
            for (int i = 0; i < (flowers ? 25 : 44); i++)
            {
                float angle = (float)rng.NextDouble() * 2f * Mathf.PI;
                float radius = Mathf.Sqrt((float)rng.NextDouble()) * (flowers ? 1.34f : 1.68f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 root = outward * radius;
                float length = Mathf.Lerp(0.13f, 0.49f, (float)rng.NextDouble());
                if (i % 6 == 0) length *= 1.4f;
                Blade(mesh, root, outward * (0.06f + 0.58f * length),
                    Mathf.Lerp(0.013f, 0.039f, (float)rng.NextDouble()), length, C(0.13f, 0.23f, 0.065f),
                    i % 4 == 0 ? C(0.57f, 0.51f, 0.19f) : C(0.37f, 0.52f, 0.19f));
                if (!flowers || i % 3 != 0) continue;

                Vector3 bloom = root + outward * length * 0.22f + Vector3.up * (length + 0.18f);
                Blade(mesh, root, outward * length * 0.22f, 0.027f, length + 0.18f, C(0.18f, 0.31f, 0.07f), C(0.40f, 0.54f, 0.17f));
                Color petals = i % 9 == 0 ? C(0.91f, 0.71f, 0.18f) : i % 9 == 3 ? C(0.71f, 0.42f, 0.81f) : C(0.87f, 0.86f, 0.73f);
                int heart = mesh.Vn(bloom, Vector3.up, C(0.82f, 0.63f, 0.30f), 0.95f);
                for (int j = 0; j < 5; j++)
                {
                    float phi = j * Mathf.PI * 0.4f;
                    Vector3 ray = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
                    Vector3 side = Vector3.Cross(ray, Vector3.up);
                    int a = mesh.Vn(bloom + ray * 0.049f + side * 0.030f, Vector3.up, petals, 1f);
                    int b = mesh.Vn(bloom + ray * 0.117f + Vector3.up * 0.034f, Vector3.up, petals, 1f);
                    int c = mesh.Vn(bloom + ray * 0.049f - side * 0.030f, Vector3.up, petals, 1f);
                    mesh.Tri(heart, a, b); mesh.Tri(heart, b, c);
                    mesh.Tri(heart, b, a); mesh.Tri(heart, c, b);
                }
            }
            return mesh.Done(flowers ? "Meadow wildflowers" : "Meadow grass");
        }

        static Mesh ForestFern()
        {
            var mesh = new PM(Surf.Organic);
            var rng = new System.Random(14921);
            for (int frond = 0; frond < 6; frond++)
            {
                float angle = frond * Mathf.PI / 3f + (float)rng.NextDouble() * 0.47f;
                Vector3 outwards = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 side = Vector3.Cross(outwards, Vector3.up);
                float scale = Mathf.Lerp(0.65f, 1.15f, (float)rng.NextDouble());
                Color dark = C(0.11f, 0.24f, 0.065f), tip = C(0.34f, 0.49f, 0.16f);
                Vector3 Point(float t) => outwards * (t * 0.92f * scale) + Vector3.up * ((0.60f * Mathf.Sin(t * 2.2f) + 0.1f * t) * scale);
                for (int pair = 1; pair <= 8; pair++)
                {
                    float t = pair / 9f;
                    Vector3 root = Point(t), next = Point((pair + 1) / 9f);
                    int stem = mesh.Vn(root, Vector3.up, dark, t);
                    mesh.Vn(root + side * 0.009f, Vector3.up, tip, t);
                    mesh.Vn(next, Vector3.up, tip, t);
                    mesh.Tri(stem, stem + 1, stem + 2);
                    mesh.Tri(stem, stem + 2, stem + 1);
                    float reach = scale * (0.07f + 0.24f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.62f)));
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 centre = root + side * (sign * 0.52f * reach) + outwards * 0.046f + Vector3.up * 0.016f;
                        Vector3 end = root + side * (sign * reach) + outwards * (reach * 0.48f) - Vector3.up * (reach * 0.25f);
                        Vector3 normal = (Vector3.up + side * (sign * 0.29f)).normalized;
                        int start = mesh.Vn(root, normal, dark, t);
                        mesh.Vn(centre - outwards * 0.040f, normal, Color.Lerp(dark, tip, 0.56f), t);
                        mesh.Vn(end, normal, tip, t);
                        mesh.Vn(centre + outwards * 0.032f, normal, tip, t);
                        mesh.Tri(start, start + 1, start + 2); mesh.Tri(start, start + 2, start + 3);
                        mesh.Tri(start, start + 2, start + 1); mesh.Tri(start, start + 3, start + 2);
                    }
                }
            }
            return mesh.Done("Woodland ferns");
        }

        static Mesh GlacierShard()
        {
            var mesh = new PM(Surf.GlacialIce);
            var rng = new System.Random(9017);
            for (int shard = 0; shard < 5; shard++)
            {
                float angle = shard * 2.399963f;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float size = shard == 0 ? 1f : Mathf.Lerp(0.37f, 0.84f, (float)rng.NextDouble());
                Vector3 centre = outward * (shard == 0 ? 0f : 0.45f + size * 0.24f);
                Vector3 crest = centre + Vector3.up * (1.13f + size * 1.09f) * size + outward * size * 0.56f;
                for (int side = 0; side < 6; side++)
                {
                    float a = side * Mathf.PI / 3f, b = (side + 1) * Mathf.PI / 3f;
                    Vector3 left = centre + new Vector3(Mathf.Sin(a) * 0.70f, -0.18f, Mathf.Cos(a) * 0.54f) * size;
                    Vector3 right = centre + new Vector3(Mathf.Sin(b) * 0.70f, -0.18f, Mathf.Cos(b) * 0.54f) * size;
                    Vector3 normal = Vector3.Cross(right - left, crest - left).normalized;
                    Color cool = Color.Lerp(C(0.26f, 0.51f, 0.66f), C(0.55f, 0.76f, 0.84f), (float)rng.NextDouble());
                    int i0 = mesh.Vn(left, normal, cool * 0.80f);
                    int i1 = mesh.Vn(right, normal, cool * 0.94f);
                    int i2 = mesh.Vn(crest, normal, C(0.75f, 0.86f, 0.89f));
                    mesh.Tri(i0, i1, i2);
                }
            }
            return mesh.Done("Split glacial ice");
        }
    }
}
