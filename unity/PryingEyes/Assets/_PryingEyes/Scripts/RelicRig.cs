using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    public sealed partial class AlienRig
    {
        readonly List<Mesh> relicMeshes = new();
        readonly List<Texture2D> relicTextures = new();

        void DressRelic()
        {
            foreach (var material in new[] { Shell, membrane })
            {
                bool cloth = kind == Enemy.EnemyType.Veglia && material == membrane;
                bool wood = kind == Enemy.EnemyType.Portachiavi && material == Shell;
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Patina " + (cloth ? "tessuto" : wood ? "legno" : "reliquia") };
                var pixels = new Color[size * size];
                var rng = new System.Random(cloth ? 571 : 913);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float n = (float)rng.NextDouble();
                        float stain = Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
                        float grain = wood ? 0.08f * Mathf.Sin(x * 0.7f + Mathf.Sin(y * 0.12f)) :
                            cloth ? ((x % 3 == 0 || y % 3 == 0) ? -0.16f : 0.04f) : 0.04f * Mathf.Sin(y * 2.3f);
                        float value = Mathf.Clamp01(0.60f + stain * 0.24f + n * 0.10f + grain);
                        pixels[y * size + x] = new Color(value, value, value, 1f);
                    }
                tex.SetPixels(pixels); tex.Apply(true, true);
                material.SetTexture("_BaseMap", tex);
                material.SetTextureScale("_BaseMap", Vector2.one * (cloth ? 12f : wood ? 2f : 3f));
                relicTextures.Add(tex);
            }
        }

        Transform RelicPart(Transform parent, string name, MeshBuilder builder)
        {
            var t = Joint(parent, name, Vector3.zero);
            var mesh = builder.ToMesh(name, out Material[] materials);
            relicMeshes.Add(mesh);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return t;
        }

        // Profilo tornito: coppe, campane e cera hanno un volume vero, anche viste dal basso.
        static void Turn(MeshBuilder mb, Material mat, Vector3 origin, Vector2[] profile, int sides = 32)
        {
            for (int row = 0; row < profile.Length - 1; row++)
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 P(Vector2 v, float angle) => origin + new Vector3(Mathf.Cos(angle) * v.x, v.y, Mathf.Sin(angle) * v.x);
                    Vector3 N(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var p = profile[row]; var q = profile[row + 1];
                    Vector3 na = (N(a) * (q.y - p.y) + Vector3.up * (p.x - q.x)).normalized;
                    Vector3 nb = (N(b) * (q.y - p.y) + Vector3.up * (p.x - q.x)).normalized;
                    mb.QuadSmooth(mat, P(p, a), P(p, b), P(q, b), P(q, a), na, nb, nb, na,
                        new Vector2((float)i / sides, p.y), new Vector2((i + 1f) / sides, p.y),
                        new Vector2((i + 1f) / sides, q.y), new Vector2((float)i / sides, q.y));
                }
        }

        static void Ring(MeshBuilder mb, Material mat, Vector3 center, float radius, float wire)
        {
            const int around = 24, section = 6;
            for (int i = 0; i < around; i++)
                for (int j = 0; j < section; j++)
                {
                    Vector3 P(int u, int v)
                    {
                        float a = u * Mathf.PI * 2f / around, b = v * Mathf.PI * 2f / section;
                        return center + new Vector3(Mathf.Cos(a) * (radius + wire * Mathf.Cos(b)),
                            Mathf.Sin(a) * (radius + wire * Mathf.Cos(b)), wire * Mathf.Sin(b));
                    }
                    var a = P(i, j); var b = P(i + 1, j); var c = P(i + 1, j + 1); var d = P(i, j + 1);
                    Vector3 outward = (a + b + c + d) * 0.25f - center;
                    float angle = (i + 0.5f) * Mathf.PI * 2f / around;
                    outward -= new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                    mb.QuadW(mat, a, b, c, d, outward);
                }
        }

        Vector3 Vigil()
        {
            var mb = new MeshBuilder();
            // Un cero scavato: bocca scura, maschera di cera, bordo colato e stoppino vivo.
            Turn(mb, Shell, Vector3.zero, new[] { new Vector2(0.12f, -0.26f), new Vector2(0.16f, -0.12f),
                new Vector2(0.14f, 0.12f), new Vector2(0.15f, 0.22f), new Vector2(0.11f, 0.25f),
                new Vector2(0.085f, 0.20f), new Vector2(0.085f, 0.14f) });
            Turn(mb, Glow, Vector3.up * 0.22f, new[] { new Vector2(0.005f, 0), new Vector2(0.035f, 0.045f),
                new Vector2(0.018f, 0.095f), new Vector2(0.001f, 0.16f) }, 16);
            for (int side = -1; side <= 1; side += 2)
            {
                Piece(model, new Vector3(side * 0.063f, 0.08f, 0.132f), new Vector3(0.058f, 0.025f, 0.006f), membrane);
                Piece(model, new Vector3(side * 0.063f, 0.076f, 0.137f), new Vector3(0.020f, 0.006f, 0.003f), Glow);
            }
            Piece(model, new Vector3(0, -0.07f, 0.163f), new Vector3(0.03f, 0.07f, 0.009f), membrane);
            mb.Beam(Shell, new Vector3(0, 0.07f, 0.15f), new Vector3(0, -0.02f, 0.173f), 0.022f, 0.022f, Vector3.forward);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                Vector3 p = new Vector3(Mathf.Sin(a) * 0.14f, 0.14f, Mathf.Cos(a) * 0.14f);
                mb.Beam(Shell, p, p - Vector3.up * (0.12f + 0.08f * Mathf.Sin(i * 1.7f)), 0.025f, 0.025f, Vector3.forward);
            }
            RelicPart(model, "Volto di cera e stoppino", mb);
            // Tre teli plissettati, aperti davanti: il volto resta leggibile e colpibile.
            for (int panel = 0; panel < 3; panel++)
            {
                var cloth = new MeshBuilder();
                float from = 35f + panel * 96f;
                Vector3 P(int u, int v)
                {
                    float t = v / 10f, angle = (from + u * 96f / 20f) * Mathf.Deg2Rad;
                    float folds = 0.025f * Mathf.Cos(u * Mathf.PI * 0.8f) * (0.3f + t);
                    float radius = Mathf.Lerp(0.18f, 0.48f, t) + folds;
                    float rag = t * t * (0.055f * Mathf.Sin(u * 2.3f + panel) + 0.035f * Mathf.Cos(u * 4f));
                    return new Vector3(Mathf.Sin(angle) * radius, 0.24f - t * 0.95f + rag, Mathf.Cos(angle) * radius);
                }
                for (int v = 0; v < 10; v++)
                    for (int u = 0; u < 20; u++)
                    {
                        if (v == 9 && (u + panel) % 7 == 0) continue;
                        var a = P(u, v); var b = P(u + 1, v); var c = P(u + 1, v + 1); var d = P(u, v + 1);
                        cloth.QuadW(membrane, a, b, c, d, new Vector3(a.x, 0f, a.z));
                        // Orlo ocra consumato, cucito nella stessa geometria.
                        if (u == 0 || u == 19 || v == 8)
                            cloth.Beam(Shell, a, u == 0 || u == 19 ? d : b, 0.007f, 0.007f, Vector3.forward);
                    }
                var veil = Limb(Vector3.zero, Vector3.forward, panel * 1.9f, panel == 1 ? 4f : 9f);
                RelicPart(veil, "Velo votivo " + panel, cloth);
            }
            return new Vector3(0f, 0.03f, 0.15f);
        }

        Vector3 KeyKeeper()
        {
            var mb = new MeshBuilder();
            mb.Box(Shell, new Vector3(0, 0.83f, 0), new Vector3(0.68f, 0.55f, 0.68f));
            // Bordi in ottone e pannelli scavati del vecchio casellario della reception.
            for (int side = -1; side <= 1; side += 2)
            {
                mb.Box(membrane, new Vector3(side * 0.31f, 0.83f, 0.345f), new Vector3(0.035f, 0.59f, 0.025f));
                mb.Box(membrane, new Vector3(0, 0.83f + side * 0.25f, 0.35f), new Vector3(0.67f, 0.027f, 0.035f));
                for (int row = 0; row < 3; row++)
                {
                    Vector3 p = new Vector3(side * 0.15f, 0.68f + row * 0.14f, 0.35f);
                    mb.Box(membrane, p, new Vector3(0.22f, 0.105f, 0.025f));
                    mb.Box(Shell, p + Vector3.forward * 0.018f, new Vector3(0.175f, 0.065f, 0.015f));
                    mb.Box(membrane, p + new Vector3(0, -0.015f, 0.035f), new Vector3(0.055f, 0.012f, 0.035f));
                }
            }
            // Campanello della reception sul dorso, con collare e battaglio sospeso.
            Turn(mb, membrane, new Vector3(0, 1.08f, 0), new[] { new Vector2(0.29f, 0), new Vector2(0.30f, 0.035f),
                new Vector2(0.22f, 0.08f), new Vector2(0.19f, 0.20f), new Vector2(0.10f, 0.27f), new Vector2(0.015f, 0.29f) });
            Turn(mb, membrane, new Vector3(0, 1.37f, 0), new[] { new Vector2(0.018f, 0), new Vector2(0.018f, 0.06f),
                new Vector2(0.065f, 0.06f), new Vector2(0.065f, 0.08f), new Vector2(0, 0.08f) }, 20);
            Ring(mb, membrane, new Vector3(0, 0.99f, 0.40f), 0.10f, 0.027f);
            Piece(model, new Vector3(0, 0.985f, 0.405f), new Vector3(0.10f, 0.10f, 0.02f), Glow);
            mb.Box(Glow, new Vector3(0, 0.89f, 0.405f), new Vector3(0.045f, 0.11f, 0.02f));
            RelicPart(model, "Casellario e campanello", mb);
            for (int i = 0; i < 4; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float z = i < 2 ? 0.24f : -0.24f;
                var leg = Limb(new Vector3(side * 0.30f, 0.64f, z), Vector3.forward, (i == 0 || i == 3) ? 0f : Mathf.PI, side * 14f);
                var key = new MeshBuilder();
                Ring(key, membrane, Vector3.zero, 0.085f, 0.025f);
                Vector3 elbow = new Vector3(side * 0.25f, -0.18f, z * 0.5f), foot = new Vector3(side * 0.31f, -0.55f, z);
                key.Beam(membrane, Vector3.zero, elbow, 0.055f, 0.055f, Vector3.forward);
                key.Beam(membrane, elbow, foot, 0.052f, 0.052f, Vector3.forward);
                for (int tooth = 0; tooth < 3; tooth++)
                    key.Box(membrane, foot + new Vector3(side * (0.03f + tooth * 0.045f), 0, 0), new Vector3(0.035f, 0.07f + tooth % 2 * 0.04f, 0.085f));
                RelicPart(leg, "Chiave snodata " + i, key);
            }
            return new Vector3(0, 0.96f, 0.41f);
        }
    }
}
