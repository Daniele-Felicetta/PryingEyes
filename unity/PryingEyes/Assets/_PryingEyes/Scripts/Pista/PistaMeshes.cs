using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Pista
{
    /// <summary>A small mesh builder for the track and its scenery: one vertex list and one triangle list per submesh (one per material), oriented boxes with flat faces, and the conversion to a Mesh
    /// (32-bit indices when the vertices do not fit 16 bits).</summary>
    public sealed class MB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int>[] tris;
        /// <summary>An optional second set of four-component coordinates (the road ribbon keeps its lateral offset, its distance along the lap and the line of the rubber there). Null unless UseSecondUv was called.</summary>
        public List<Vector4> uv1;
        /// <summary>Optional vertex colours (the trees keep the tint of a tree and the wind weight of a leaf card in them). Null unless UseColors was called; vertices made with Vert are white.</summary>
        public List<Color32> colors;

        public MB(int submeshes)
        {
            tris = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) tris[i] = new List<int>();
        }

        /// <summary>Give this builder the second set of coordinates (zero for every vertex that does not set its own).</summary>
        public MB UseSecondUv() { uv1 = new List<Vector4>(); return this; }

        /// <summary>Give this builder vertex colours (white for every vertex that does not set its own).</summary>
        public MB UseColors() { colors = new List<Color32>(); return this; }

        public int Count => v.Count;

        public int Vert(Vector3 p, Vector3 nrm, Vector2 tex)
        {
            v.Add(p); n.Add(nrm); uv.Add(tex);
            if (uv1 != null) uv1.Add(Vector4.zero);
            if (colors != null) colors.Add(new Color32(255, 255, 255, 255));
            return v.Count - 1;
        }

        /// <summary>A vertex with its own colour (the builder must have been given colours with UseColors).</summary>
        public int VertC(Vector3 p, Vector3 nrm, Vector2 tex, Color32 col)
        {
            int i = Vert(p, nrm, tex);
            colors[i] = col;
            return i;
        }

        public void Tri(int sub, int a, int b, int c) { var t = tris[sub]; t.Add(a); t.Add(b); t.Add(c); }

        /// <summary>A quad a-b-c-d (counter-clockwise seen from the side the normal points to).</summary>
        public void Quad(int sub, int a, int b, int c, int d) { Tri(sub, a, b, c); Tri(sub, a, c, d); }

        /// <summary>A flat quad with its own vertices; the winding is fixed so that the face looks the way 'nrm' says.</summary>
        public void Face(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, float u1 = 1f, float v1 = 1f)
        {
            int i = Vert(a, nrm, new Vector2(0f, 0f)); Vert(b, nrm, new Vector2(u1, 0f)); Vert(c, nrm, new Vector2(u1, v1)); Vert(d, nrm, new Vector2(0f, v1));
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) >= 0f) Quad(sub, i, i + 1, i + 2, i + 3);
            else Quad(sub, i, i + 3, i + 2, i + 1);
        }

        /// <summary>A box centred at 'c' with the axes 'r' (x), 'u' (y), 'f' (z), unit vectors, and half sizes 'h'. Faces are flat.</summary>
        public void Box(int sub, Vector3 c, Vector3 r, Vector3 u, Vector3 f, Vector3 h, bool bottom = true)
        {
            Vector3 rx = r * h.x, uy = u * h.y, fz = f * h.z;
            Face(sub, c - rx + uy - fz, c + rx + uy - fz, c + rx + uy + fz, c - rx + uy + fz, u);            // top
            Face(sub, c - rx - uy + fz, c + rx - uy + fz, c + rx + uy + fz, c - rx + uy + fz, f);            // front
            Face(sub, c + rx - uy - fz, c - rx - uy - fz, c - rx + uy - fz, c + rx + uy - fz, -f);           // back
            Face(sub, c + rx - uy + fz, c + rx - uy - fz, c + rx + uy - fz, c + rx + uy + fz, r);            // right
            Face(sub, c - rx - uy - fz, c - rx - uy + fz, c - rx + uy + fz, c - rx + uy - fz, -r);           // left
            if (bottom) Face(sub, c - rx - uy - fz, c + rx - uy - fz, c + rx - uy + fz, c - rx - uy + fz, -u);
        }

        /// <summary>A box standing on a heading (radians, 0 = +z): 'size' is the full width, height and length; 'at' is the middle of its base.</summary>
        public void BoxOn(int sub, Vector3 at, float heading, Vector3 size, bool bottom = false)
        {
            Vector3 f = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)), r = new Vector3(f.z, 0f, -f.x);
            Box(sub, at + Vector3.up * (size.y * 0.5f), r, Vector3.up, f, size * 0.5f, bottom);
        }

        public Mesh ToMesh(string name, bool recalcNormals = false)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            if (uv1 != null) m.SetUVs(1, uv1);
            if (colors != null) m.SetColors(colors);
            m.subMeshCount = Mathf.Max(1, tris.Length);
            for (int i = 0; i < tris.Length; i++) m.SetTriangles(tris[i], i);
            if (recalcNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The materials of the submeshes that have triangles, in order, with the submesh index remapped (empty submeshes are dropped so that no draw call is wasted).</summary>
        public static Mesh Compact(MB b, string name, Material[] all, out Material[] used, bool recalcNormals = false)
        {
            var keep = new List<int>();
            for (int i = 0; i < b.tris.Length; i++) if (b.tris[i].Count > 0) keep.Add(i);
            var m = new Mesh { name = name };
            if (b.v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(b.v); m.SetNormals(b.n); m.SetUVs(0, b.uv);
            if (b.uv1 != null) m.SetUVs(1, b.uv1);
            if (b.colors != null) m.SetColors(b.colors);
            m.subMeshCount = Mathf.Max(1, keep.Count);
            used = new Material[Mathf.Max(1, keep.Count)];
            for (int k = 0; k < keep.Count; k++) { m.SetTriangles(b.tris[keep[k]], k); used[k] = all[keep[k]]; }
            if (keep.Count == 0) used[0] = all[0];
            if (recalcNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Make a renderer for a compacted mesh under a parent.</summary>
        public static GameObject Spawn(string name, Transform parent, Mesh mesh, Material[] mats, bool shadows = true, bool receive = true)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = receive;
            return g;
        }
    }

    /// <summary>Procedural textures for the ground, the road and the scenery: tileable value noise in a few octaves coloured between two tones. Everything is made once at the start of a race.</summary>
    public static class PistaTex
    {
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>The white noise of the lattice point (x, y): 0..1, the same for the same arguments on every machine.</summary>
        public static float H2(int x, int y, int seed) { return Hash(x, y, seed); }

        /// <summary>Tileable smooth noise: a lattice of 'cells' x 'cells' random values, wrapped, interpolated with a smoothstep.</summary>
        public static float Noise(float u, float v, int cells, int seed)
        {
            float fx = u * cells, fy = v * cells;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            int xa = ((x0 % cells) + cells) % cells, xb = (xa + 1) % cells, ya = ((y0 % cells) + cells) % cells, yb = (ya + 1) % cells;
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        public static float Fbm(float u, float v, int baseCells, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(u, v, cells, seed + o * 31);
                norm += amp; amp *= 0.5f; cells *= 2;
            }
            return sum / norm;
        }

        /// <summary>A square tileable texture: 'fine' is the amount of per-pixel speckle, 'coarse' the amount of large patches, between colours a and b.</summary>
        public static Texture2D Make(string name, int size, Color a, Color b, int baseCells, int octaves, float coarse, float fine, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float low = Fbm(u, v, baseCells, octaves, seed);
                    float hi = Hash(x, y, seed + 7);
                    float t = Mathf.Clamp01(0.5f + (low - 0.5f) * coarse + (hi - 0.5f) * fine);
                    px[y * size + x] = Color.Lerp(a, b, t);
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>A flat colour material of the Lit shader of the pipeline, with an optional texture tiled over its UV.</summary>
        public static Material Lit(string name, Color c, float smooth, float metal = 0f, Texture2D tex = null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Pista_" + name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return m;
        }

        /// <summary>The paint of the bikes and the riders (PistaPaint.shader): a base colour and an accent, the pattern of the livery, the clear coat (0 matt .. 1 gloss), the share of metallic flake and the
        /// smoothness of the base layer. The shader is a resource, so it is always in the build; if it were not, a plain Lit of the same colour takes its place.</summary>
        public static Material Paint(string name, Color c, Color accent, int pattern, float coat, float flake, float smooth)
        {
            var sh = Shader.Find("PryingEyes/PistaPaint");
            if (sh == null || !sh.isSupported) return Lit(name, c, smooth, 0.1f);
            var m = new Material(sh) { name = "Pista_" + name };
            m.SetColor("_BaseColor", c);
            m.SetColor("_AccentColor", accent);
            m.SetFloat("_Pattern", pattern);
            m.SetFloat("_Coat", coat);
            m.SetFloat("_Flake", flake);
            m.SetFloat("_Smooth", smooth);
            return m;
        }

        public static Material Glow(string name, Color baseColor, Color emission, float smooth = 0.6f)
        {
            var m = Lit(name, baseColor, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            return m;
        }
    }
}
