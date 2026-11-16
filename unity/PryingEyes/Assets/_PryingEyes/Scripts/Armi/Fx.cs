using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Effetti dei colpi: tracce luminose, polvere e scintille agli impatti, fori sui muri, bossoli che rimbalzano.
    /// Tutto a quad con materiali Unlit (niente ParticleSystem, che in build perde lo shader); riuso a pool.</summary>
    /// <summary>Luce che si spegne in fretta e poi sparisce (lampi delle esplosioni).</summary>
    public sealed class LightFade : MonoBehaviour
    {
        Light l; float dur, t, i0;
        public void Begin(Light light, float seconds) { l = light; dur = seconds; i0 = light.intensity; }
        void Update()
        {
            t += Time.deltaTime;
            if (l != null) l.intensity = i0 * Mathf.Clamp01(1f - t / dur) * Mathf.Clamp01(1f - t / dur);
            if (t >= dur) Destroy(gameObject);
        }
    }

    public class Fx : MonoBehaviour
    {
        static Fx I;
        static Mesh quadMesh;
        public static Material PuffMat, SparkMat, TracerMat, BeamMat, HoleMat, FlashMat, BrassMat, ShellMat, ChipMat;
        static Texture2D puffTex, sparkTex, tracerTex, beamTex, holeTex, starTex, chipTex;

        class Item
        {
            public Transform t; public Renderer r; public MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            public float life, age, size0, size1, a0; public Vector3 vel, grav; public int kind; public Vector3 from, to; public float len;
            public bool alive;
            public Color col = Color.white;
            // schegge (kind 5): allungamento, rotazione nel piano della vista, pavimento su cui rimbalzano, attrito dell'aria
            public float stretch = 1f, spin, ang, floorY, drag; public int bounces;
            // fori e macchie: la luce che li tinge e quando ricalcolarla
            public Color light = Color.white; public float lightT;
        }
        readonly List<Item> items = new List<Item>();
        readonly Queue<Item> free = new Queue<Item>();
        readonly Item[] holes = new Item[48];
        int holeNext;
        readonly Item[] stains = new Item[72];      // macchie di sangue su muri e pavimenti (stesso materiale dei fori, texture propria)
        int stainNext;
        static Texture2D bloodTex, poolTex;
        readonly List<GameObject> casings = new List<GameObject>();
        Transform camT;
        readonly Vector3[] eyePos = new Vector3[Seats.MaxSeats];

        static void Ensure()
        {
            if (I != null) return;
            var go = new GameObject("Fx");
            DontDestroyOnLoad(go);
            I = go.AddComponent<Fx>();
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            Load();
        }

        // ---------- materiali
        static Material Res(string name) => Resources.Load<Material>("Armi/" + name);

        static void Load()
        {
            PuffMat = Res("Fx_Puff") ?? Unlit(PuffTex(), false);
            SparkMat = Res("Fx_Spark") ?? Unlit(SparkTex(), true);
            TracerMat = Res("Fx_Tracer") ?? Unlit(TracerTex(), true);
            BeamMat = Res("Fx_Beam") ?? Unlit(BeamTex(), true);
            HoleMat = Res("Fx_Hole") ?? Unlit(HoleTex(), false);
            FlashMat = Res("Fx_Flash") ?? Unlit(StarTex(), true);
            BrassMat = Res("Armi_Ottone") ?? Lit(new Color(0.78f, 0.55f, 0.20f), 1f, 0.72f);
            ShellMat = Res("Armi_Cartuccia") ?? Lit(new Color(0.55f, 0.05f, 0.04f), 0f, 0.5f);
            ChipMat = new Material(PuffMat) { name = "Fx_Scheggia" };
            ChipMat.SetTexture("_BaseMap", ChipTex());
            ChipMat.SetTexture("_MainTex", ChipTex());
        }

        /// <summary>Fori e macchie: Unlit a trasparenza (il Lit trasparente del progetto conserva i riflessi anche dove l'alfa e' zero e
        /// disegnava un quadrato chiaro attorno a ogni foro); la luce la mettono loro, ricalcolata ogni 0,15 s (LightOf).</summary>
        static Material DecalMat => HoleMat;

        /// <summary>Il colore della luce che arriva su un foro o una macchia (lampade, ambiente, torcia), con un minimo per non sparire.</summary>
        static Color LightOf(Vector3 p, Vector3 n)
        {
            var c = Surfaces.LightColorAt(p + n * 0.05f, n);
            return new Color(Mathf.Clamp(c.r, 0.035f, 1.25f), Mathf.Clamp(c.g, 0.035f, 1.25f), Mathf.Clamp(c.b, 0.035f, 1.25f), 1f);
        }

        public static Material Unlit(Texture2D tex, bool additive)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ConfigureUnlit(m, tex, additive);
            return m;
        }

        /// <summary>Unlit trasparente (additivo o a transizione alfa). Chiamata anche dall'importer, che salva i materiali.</summary>
        public static void ConfigureUnlit(Material m, Texture2D tex, bool additive)
        {
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        static Material Lit(Color c, float metallic, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c; m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            return m;
        }

        // ---------- texture procedurali
        const int T = 64;
        static Texture2D Make(System.Func<float, float, Color> f, bool mips = false)
        {
            var tex = new Texture2D(T, T, TextureFormat.RGBA32, mips) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[T * T];
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                    px[y * T + x] = f((x + 0.5f) / T * 2f - 1f, (y + 0.5f) / T * 2f - 1f);
            tex.SetPixels32(px);
            tex.Apply(mips);
            return tex;
        }
        public static Texture2D PuffTex() => puffTex != null ? puffTex : puffTex = Make((u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float n = Mathf.PerlinNoise(u * 2.4f + 7f, v * 2.4f + 3f) * 0.5f + 0.5f;
            float a = Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r) * n;
            return new Color(0.78f, 0.74f, 0.68f, a);
        });
        public static Texture2D SparkTex() => sparkTex != null ? sparkTex : sparkTex = Make((u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01(1f - r); a *= a;
            return new Color(1f, 0.74f, 0.35f, a);
        });
        public static Texture2D TracerTex() => tracerTex != null ? tracerTex : tracerTex = Make((u, v) =>
        {
            // asse lungo v: testa luminosa in alto (v=1), coda che sfuma; sottile in u
            float w = Mathf.Clamp01(1f - Mathf.Abs(u) * 1.0f); w *= w;
            float head = Mathf.Clamp01((v + 1f) * 0.5f);
            float a = w * Mathf.Pow(head, 1.6f);
            return new Color(1f, 0.82f, 0.50f, a);
        });
        public static Texture2D BeamTex() => beamTex != null ? beamTex : beamTex = Make((u, v) =>
        {
            // striscia uniforme lungo v (il raggio e' una linea, non un fuso): profilo morbido in u, estremi che sfumano appena
            float w = Mathf.Clamp01(1f - Mathf.Abs(u)); w = Mathf.Pow(w, 1.8f);
            float ends = Mathf.Clamp01((1f - Mathf.Abs(v)) * 14f);
            return new Color(1f, 1f, 1f, w * ends);
        });
        public static Texture2D HoleTex() => holeTex != null ? holeTex : holeTex = Make((u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float n = Mathf.PerlinNoise(u * 4f + 11f, v * 4f + 5f);
            float core = Mathf.Clamp01((0.34f + n * 0.08f - r) * 12f);
            float halo = Mathf.Clamp01(1f - r) * 0.5f * (0.5f + n);
            float a = Mathf.Clamp01(core + halo * (1f - core));
            return new Color(0.03f, 0.03f, 0.035f, a * 0.85f);
        }, true);
        public static Texture2D StarTex() => starTex != null ? starTex : starTex = Weapon.BuildFlashStar();

        /// <summary>Scheggia: un poligono irregolare a bordo netto, un filo piu' chiaro in alto (si legge come un pezzetto solido).</summary>
        public static Texture2D ChipTex() => chipTex != null ? chipTex : chipTex = Make((u, v) =>
        {
            float ang = Mathf.Atan2(v, u);
            float edge = 0.55f + 0.22f * Mathf.PerlinNoise(Mathf.Cos(ang) * 1.3f + 2f, Mathf.Sin(ang) * 1.3f + 8f) + 0.12f * Mathf.Sin(ang * 3f + 0.7f);
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01((edge - r) * 10f);
            float shade = 0.78f + 0.22f * (v * 0.5f + 0.5f);
            return new Color(shade, shade, shade, a);
        });

        static readonly Texture2D[] holeTexs = new Texture2D[7];

        /// <summary>Il foro di ogni superficie (bianco = colore della superficie scheggiata, illuminato dalla scena): intonaco con cratere e
        /// alone di polvere, pietra con l'orlo chiaro scheggiato, piastrelle con le crepe, legno con le fibre strappate lungo la venatura,
        /// metallo con l'ammaccatura lucida e l'alone bruciato, stoffa col buco sfilacciato, vetro con la crepa a stella.</summary>
        public static Texture2D HoleTex(Surface s)
        {
            int i = (int)s;
            if (holeTexs[i] != null) return holeTexs[i];
            System.Func<float, float, Color> f;
            switch (s)
            {
                case Surface.Stone: f = (u, v) => Crater(u, v, 0.17f, 0.46f, 0.0f, new Color(0.84f, 0.82f, 0.78f), 9.5f); break;
                case Surface.Tile: f = (u, v) =>
                {
                    var c = Crater(u, v, 0.16f, 0.36f, 0.0f, new Color(0.94f, 0.94f, 0.92f), 12f);
                    float crack = Cracks(u, v, 6, 0.2f, 0.9f, 0.030f, 7.1f);
                    return Over(c, new Color(0.10f, 0.10f, 0.10f, crack * 0.85f));
                }; break;
                case Surface.Wood: f = (u, v) =>
                {
                    // fibre strappate lungo la venatura (asse u): un'ellisse chiara a strisce, il buco scuro al centro
                    float r = Mathf.Sqrt(u * u * 0.6f + v * v * 3.0f);
                    float streak = Mathf.PerlinNoise(u * 2.2f + 5f, v * 18f + 3f);
                    float edge = Mathf.Clamp01((1f - Mathf.Abs(u)) * 5f) * Mathf.Clamp01((1f - Mathf.Abs(v)) * 5f);    // mai fino al bordo del quad
                    float fib = Mathf.Clamp01((0.62f + 0.25f * streak - r) * 6f) * (0.55f + 0.45f * streak) * edge;
                    float rc = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Clamp01((0.19f + 0.05f * Mathf.PerlinNoise(u * 5f + 1f, v * 5f) - rc) * 14f);
                    var raw = new Color(0.76f, 0.62f, 0.45f);
                    var col = Color.Lerp(raw, new Color(0.06f, 0.04f, 0.03f), core);
                    return new Color(col.r, col.g, col.b, Mathf.Clamp01(Mathf.Max(core, fib * 0.9f)));
                }; break;
                case Surface.Metal: f = (u, v) =>
                {
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = Mathf.PerlinNoise(u * 6f + 2f, v * 6f + 9f);
                    float core = Mathf.Clamp01((0.20f + 0.03f * n - r) * 16f);
                    float ring = Mathf.Clamp01((0.40f + 0.05f * n - r) * 12f) * (1f - core);
                    float scorch = Mathf.Clamp01((0.78f - r) * 3f) * (1f - ring - core) * 0.55f * (0.6f + 0.4f * n);
                    Color col = core > 0.5f ? new Color(0.10f, 0.10f, 0.11f) : ring > 0.2f ? new Color(0.82f, 0.82f, 0.84f) : new Color(0.10f, 0.09f, 0.08f);
                    return new Color(col.r, col.g, col.b, Mathf.Clamp01(core + ring + scorch));
                }; break;
                case Surface.Fabric: f = (u, v) =>
                {
                    float ang = Mathf.Atan2(v, u);
                    float r = Mathf.Sqrt(u * u + v * v);
                    float edge = 0.34f + 0.12f * Mathf.PerlinNoise(Mathf.Cos(ang) * 4f + 3f, Mathf.Sin(ang) * 4f + 5f);
                    float a = Mathf.Clamp01((edge - r) * 7f);
                    return new Color(0.03f, 0.025f, 0.02f, a * 0.95f);
                }; break;
                case Surface.Glass: f = (u, v) =>
                {
                    float r = Mathf.Sqrt(u * u + v * v);
                    float radial = Cracks(u, v, 10, 0.07f, 0.92f, 0.022f, 3.3f);
                    float ang = Mathf.Atan2(v, u);
                    float arcs = 0f;
                    foreach (float rr in new[] { 0.34f, 0.62f })
                    {
                        float on = Mathf.PerlinNoise(Mathf.Cos(ang) * 2.5f + rr * 9f, Mathf.Sin(ang) * 2.5f) > 0.48f ? 1f : 0f;
                        arcs = Mathf.Max(arcs, on * Mathf.Clamp01(1f - Mathf.Abs(r - rr) / 0.018f));
                    }
                    float core = Mathf.Clamp01((0.07f - r) * 30f);
                    float a = Mathf.Clamp01(Mathf.Max(Mathf.Max(radial, arcs) * 0.8f, core));
                    Color col = core > 0.5f ? new Color(0.05f, 0.05f, 0.06f) : new Color(0.92f, 0.96f, 1f);
                    return new Color(col.r, col.g, col.b, a);
                }; break;
                default: f = (u, v) => Crater(u, v, 0.23f, 0.56f, 0.95f, new Color(0.80f, 0.78f, 0.74f), 7f); break;   // intonaco
            }
            holeTexs[i] = MakeBig(96, f);
            holeTexs[i].name = "foro_" + s;
            return holeTexs[i];
        }

        /// <summary>Cratere: buco scuro, orlo scheggiato del colore della superficie, alone di polvere fino a 'halo' (0 = niente).</summary>
        static Color Crater(float u, float v, float core, float rim, float halo, Color rimColor, float sharp)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float ang = Mathf.Atan2(v, u);
            float n = Mathf.PerlinNoise(Mathf.Cos(ang) * 2.2f + 4f, Mathf.Sin(ang) * 2.2f + 1f);
            float m = Mathf.PerlinNoise(u * 7f + 3f, v * 7f + 6f);
            float c = Mathf.Clamp01((core + 0.05f * m - r) * 16f);
            float ring = Mathf.Clamp01((rim * (0.8f + 0.4f * n) - r) * sharp) * (0.75f + 0.25f * m);
            float dust = halo > 0f ? Mathf.Clamp01(1f - r / halo) * 0.28f * (0.5f + m) : 0f;
            Color col = Color.Lerp(rimColor * (0.85f + 0.15f * m), new Color(0.05f, 0.05f, 0.05f), c);
            return new Color(col.r, col.g, col.b, Mathf.Clamp01(Mathf.Max(c, Mathf.Max(ring * 0.92f, dust))));
        }

        /// <summary>Crepe dritte che partono dal centro: 'count' raggi da r0 a r1, larghi 'w' e piu' sottili verso la punta.</summary>
        static float Cracks(float u, float v, int count, float r0, float r1, float w, float seed)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            if (r < r0 || r > r1) return 0f;
            float a = 0f;
            for (int k = 0; k < count; k++)
            {
                float ang = (k + 0.35f * Mathf.Sin(k * 12.9898f + seed)) / count * Mathf.PI * 2f;
                float len = Mathf.Lerp(r0 + 0.25f, r1, 0.5f + 0.5f * Mathf.Sin(k * 78.233f + seed));
                if (r > len) continue;
                // distanza dal raggio, con un filo di zig-zag
                float wob = 0.025f * Mathf.Sin(r * 31f + k * 2.1f);
                float d = Mathf.Abs(-Mathf.Sin(ang + wob) * u + Mathf.Cos(ang + wob) * v);
                if (Mathf.Cos(ang) * u + Mathf.Sin(ang) * v < 0f) continue;
                float width = w * (1f - (r - r0) / Mathf.Max(0.01f, len - r0) * 0.7f);
                a = Mathf.Max(a, Mathf.Clamp01(1f - d / width));
            }
            return a;
        }

        static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a < 1e-4f) return new Color(0, 0, 0, 0);
            Color c = (over * over.a + under * under.a * (1f - over.a)) / a;
            return new Color(c.r, c.g, c.b, a);
        }

        // ---------- creazione elementi
        Item Spawn(Material mat, float life)
        {
            Item it = free.Count > 0 ? free.Dequeue() : null;
            if (it == null)
            {
                var go = new GameObject("fx");
                go.layer = 0; // effetti nel mondo, visibili anche dalla camera delle ottiche; nessun collider
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = quadMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                it = new Item { t = go.transform, r = r };
            }
            it.r.sharedMaterial = mat;
            it.r.enabled = true;
            it.life = life; it.age = 0f; it.alive = true; it.col = Color.white;
            it.t.localScale = Vector3.zero;   // un elemento riciclato non deve restare visibile (scala vecchia, magari un raggio di 400 m) prima del primo Update
            it.t.gameObject.SetActive(true);
            items.Add(it);
            return it;
        }

        /// <summary>Colpo fisico: polvere di tessuto e schegge opache brevi. Niente bagliori da incantesimo o disco davanti alla visuale.</summary>
        public static void MeleeImpact(Vector3 p, Vector3 normal)
        {
            Ensure();
            for (int i = 0; i < 6; i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.12f, 0.24f));
                it.kind = 1; it.t.position = p + normal * 0.025f;
                it.vel = normal * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 1.3f;
                it.grav = Vector3.down * 4f;
                it.size0 = Random.Range(0.012f, 0.022f); it.size1 = 0.003f;
                it.a0 = 1f; it.col = new Color(0.48f, 0.39f, 0.29f);
            }
            for (int i = 0; i < 2; i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.18f, 0.28f));
                it.kind = 0; it.t.position = p + normal * 0.035f;
                it.vel = normal * 0.25f + Random.insideUnitSphere * 0.25f;
                it.grav = Vector3.zero; it.size0 = 0.055f; it.size1 = 0.22f;
                it.a0 = 0.5f; it.col = new Color(0.62f, 0.54f, 0.43f);
            }
        }

        /// <summary>Contatto magico dei nemici: scintille radenti e pulviscolo, senza un disco opaco che copra il bersaglio.</summary>
        public static void EnemyImpact(Vector3 p, Vector3 normal, Color tint)
        {
            Ensure();
            float peak = Mathf.Max(1f, Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b)));
            tint /= peak; tint.a = 1f;
            for (int i = 0; i < 9; i++)
            {
                var it = I.Spawn(SparkMat, Random.Range(0.16f, 0.34f));
                it.kind = 1; it.t.position = p + normal * 0.025f;
                it.vel = normal * Random.Range(0.5f, 2f) + Random.insideUnitSphere * 2.8f;
                it.grav = Vector3.down * 6f; it.size0 = 0.022f; it.size1 = 0.002f;
                it.a0 = 1f; it.col = Color.Lerp(tint, Color.white, 0.35f);
            }
            for (int i = 0; i < 3; i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.25f, 0.45f));
                it.kind = 0; it.t.position = p + normal * 0.04f;
                it.vel = normal * 0.6f + Random.insideUnitSphere * 0.45f; it.grav = Vector3.zero;
                it.size0 = 0.04f; it.size1 = 0.35f; it.a0 = 0.24f; it.col = Color.Lerp(tint, Color.gray, 0.65f);
            }
        }

        // ---------- impatti per superficie (2026-11-15)
        /// <summary>Come reagisce una famiglia di superficie a un proiettile: polvere, schegge (che cadono e rimbalzano sul pavimento),
        /// scintille, misura del foro (la texture e' HoleTex) e volume del suono.</summary>
        struct Recipe
        {
            public int puffs; public float puffSize, puffLife, puffAlpha; public Color dust;
            public int chips; public float chipSize, chipSpeed, chipStretch, chipLife, chipGrav, chipDrag, chipAlpha; public Color chip0, chip1;
            public int sparks; public float sparkSpeed, sparkLife; public Color spark;
            public float hole0, hole1;
            public float vol, ricochet;
        }

        static readonly Recipe[] recipes =
        {
            // intonaco: nuvola chiara e larga, briciole bianche, niente scintille
            new Recipe { puffs = 5, puffSize = 0.42f, puffLife = 1.25f, puffAlpha = 0.40f, dust = new Color(0.80f, 0.78f, 0.74f),
                         chips = 6, chipSize = 0.012f, chipSpeed = 2.6f, chipStretch = 1f, chipLife = 1.3f, chipGrav = 9.8f, chipAlpha = 1f,
                         chip0 = new Color(0.88f, 0.86f, 0.82f), chip1 = new Color(0.66f, 0.64f, 0.60f),
                         hole0 = 0.085f, hole1 = 0.12f, vol = 0.34f },
            // pietra e marmo: schegge grigie veloci, poca polvere, una scintilla ogni tanto
            new Recipe { puffs = 3, puffSize = 0.26f, puffLife = 0.9f, puffAlpha = 0.32f, dust = new Color(0.62f, 0.60f, 0.56f),
                         chips = 8, chipSize = 0.010f, chipSpeed = 3.6f, chipStretch = 1f, chipLife = 1.2f, chipGrav = 9.8f, chipAlpha = 1f,
                         chip0 = new Color(0.74f, 0.72f, 0.68f), chip1 = new Color(0.44f, 0.43f, 0.41f),
                         sparks = 2, sparkSpeed = 3f, sparkLife = 0.12f, spark = new Color(1f, 0.86f, 0.6f),
                         hole0 = 0.07f, hole1 = 0.09f, vol = 0.40f, ricochet = 0.06f },
            // piastrelle e porcellana: schegge bianche che volano lontano, crepe nel foro
            new Recipe { puffs = 2, puffSize = 0.2f, puffLife = 0.7f, puffAlpha = 0.28f, dust = new Color(0.86f, 0.86f, 0.84f),
                         chips = 9, chipSize = 0.011f, chipSpeed = 4.2f, chipStretch = 1.2f, chipLife = 1.2f, chipGrav = 9.8f, chipAlpha = 1f,
                         chip0 = new Color(0.96f, 0.96f, 0.94f), chip1 = new Color(0.74f, 0.76f, 0.74f),
                         hole0 = 0.08f, hole1 = 0.10f, vol = 0.40f },
            // legno: schegge lunghe chiare (il legno vivo sotto la vernice), segatura scura
            new Recipe { puffs = 2, puffSize = 0.18f, puffLife = 0.8f, puffAlpha = 0.24f, dust = new Color(0.52f, 0.42f, 0.30f),
                         chips = 8, chipSize = 0.009f, chipSpeed = 3.0f, chipStretch = 3.5f, chipLife = 1.5f, chipGrav = 9.8f, chipAlpha = 1f,
                         chip0 = new Color(0.80f, 0.66f, 0.47f), chip1 = new Color(0.36f, 0.25f, 0.15f),
                         hole0 = 0.06f, hole1 = 0.08f, vol = 0.40f },
            // metallo: scintille fitte, un lampo, un filo di fumo, ammaccatura lucida; rimbalza spesso
            new Recipe { puffs = 1, puffSize = 0.12f, puffLife = 0.5f, puffAlpha = 0.18f, dust = new Color(0.26f, 0.26f, 0.27f),
                         sparks = 13, sparkSpeed = 4.6f, sparkLife = 0.32f, spark = new Color(1f, 0.74f, 0.35f),
                         hole0 = 0.045f, hole1 = 0.06f, vol = 0.45f, ricochet = 0.25f },
            // stoffa, moquette, cuoio: uno sbuffo del colore del tessuto, fili che galleggiano, foro piccolo
            new Recipe { puffs = 3, puffSize = 0.16f, puffLife = 0.8f, puffAlpha = 0.30f, dust = new Color(0.36f, 0.31f, 0.28f),
                         chips = 5, chipSize = 0.006f, chipSpeed = 1.0f, chipStretch = 2.2f, chipLife = 1.6f, chipGrav = 1.4f, chipDrag = 2.5f, chipAlpha = 0.9f,
                         chip0 = new Color(0.42f, 0.36f, 0.32f), chip1 = new Color(0.20f, 0.17f, 0.15f),
                         hole0 = 0.035f, hole1 = 0.05f, vol = 0.22f },
            // vetro e specchi: schegge chiare che luccicano, crepa a stella
            new Recipe { chips = 10, chipSize = 0.012f, chipSpeed = 3.5f, chipStretch = 1.6f, chipLife = 1.1f, chipGrav = 9.8f, chipAlpha = 0.85f,
                         chip0 = new Color(0.86f, 0.90f, 0.95f), chip1 = new Color(0.52f, 0.58f, 0.64f),
                         sparks = 5, sparkSpeed = 2.6f, sparkLife = 0.16f, spark = new Color(0.85f, 0.92f, 1f),
                         hole0 = 0.13f, hole1 = 0.18f, vol = 0.45f },
        };

        /// <summary>Per le prove: impatti fatti, suoni partiti, rimbalzi, superficie e texture del foro dell'ultimo impatto.</summary>
        public static int ImpactCount, ImpactSounds, Ricochets;
        public static Surface LastSurface { get; private set; }
        public static Texture LastHoleTexture { get; private set; }
        public static float LastLight { get; private set; }
        static float lastImpactSound = -1f;

        /// <summary>Colpo su un punto di una superficie nota (botti, lampade, lampadari, ...).</summary>
        public static void Impact(Vector3 p, Vector3 n, float scale = 1f, Surface surface = Surface.Plaster, Vector3 incoming = default)
        {
            Ensure();
            I.DoImpact(p, n, scale, surface, incoming, null);
        }

        /// <summary>Colpo su una superficie trovata da un raggio: la famiglia (Surfaces) decide polvere, schegge, scintille, foro e suono.</summary>
        public static Surface Impact(RaycastHit h, float scale, Vector3 incoming)
        {
            Ensure();
            var mat = Surfaces.MaterialAt(h, out var owner);
            var s = Surfaces.Of(mat, owner != null ? owner.name : h.collider != null ? h.collider.name : null);
            I.DoImpact(h.point, h.normal, scale, s, incoming, mat);
            return s;
        }

        static Color Shade(Color c, float light) => new Color(c.r * light, c.g * light, c.b * light, c.a);
        static Color Tinted(Color c, Color light) => new Color(c.r * light.r, c.g * light.g, c.b * light.b, c.a);

        void DoImpact(Vector3 p, Vector3 n, float scale, Surface surface, Vector3 incoming, Material mat)
        {
            if (camT == null && Camera.main != null) camT = Camera.main.transform;
            var rc = recipes[(int)surface];
            // gli effetti sono Unlit: prendono la luce del punto, cosi' al buio non brillano
            float light = Mathf.Clamp(Surfaces.LightAt(p + n * 0.05f), 0.04f, 1.3f);
            LastLight = light;
            Color dust = rc.dust;
            if (surface == Surface.Fabric && mat != null && mat.HasProperty("_BaseColor"))
            {
                var bc = mat.GetColor("_BaseColor");
                if (bc.grayscale < 0.85f) dust = Color.Lerp(dust, bc, 0.6f);   // la moquette rossa fa polvere rossa
            }
            dust = Shade(dust, light);
            // polvere: nuvolette che si allargano e salgono lente
            for (int i = 0; i < rc.puffs; i++)
            {
                var it = Spawn(PuffMat, rc.puffLife * Random.Range(0.8f, 1.2f));
                it.kind = 0;
                it.t.position = p + n * 0.02f + Random.insideUnitSphere * 0.02f;
                it.vel = n * Random.Range(0.2f, 0.75f) + Random.insideUnitSphere * 0.3f + Vector3.up * 0.12f;
                it.grav = Vector3.zero;
                it.size0 = 0.05f * scale; it.size1 = rc.puffSize * Random.Range(0.75f, 1.25f) * scale;
                it.a0 = rc.puffAlpha; it.col = dust;
            }
            // schegge: volano via dal muro, cadono, rimbalzano sul pavimento e si fermano
            int chips = Mathf.RoundToInt(rc.chips * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(scale)));
            if (chips > 0)
            {
                float floorY = -1e4f;
                if (Physics.Raycast(p + n * 0.05f, Vector3.down, out var fh, 3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) floorY = fh.point.y;
                for (int i = 0; i < chips; i++)
                {
                    var it = Spawn(ChipMat, rc.chipLife * Random.Range(0.75f, 1.25f));
                    it.kind = 5;
                    it.t.position = p + n * 0.012f;
                    Vector3 v = n * Random.Range(0.5f, 1.1f) + Random.insideUnitSphere * 0.8f + Vector3.up * 0.2f;
                    it.vel = v.normalized * rc.chipSpeed * Random.Range(0.5f, 1.2f);
                    it.grav = Vector3.down * rc.chipGrav;
                    it.size0 = it.size1 = rc.chipSize * Random.Range(0.6f, 1.45f) * Mathf.Sqrt(Mathf.Max(0.3f, scale));
                    it.stretch = rc.chipStretch * Random.Range(0.7f, 1.3f);
                    it.a0 = rc.chipAlpha;
                    it.col = Shade(Color.Lerp(rc.chip0, rc.chip1, Random.value), light);
                    it.spin = Random.Range(-900f, 900f); it.ang = Random.Range(0f, 360f);
                    it.floorY = floorY; it.bounces = 0; it.drag = rc.chipDrag;
                }
            }
            // scintille: righe sottili additive (metallo; qualcuna dalla pietra; luccichii dal vetro)
            for (int i = 0; i < rc.sparks; i++)
            {
                var it = Spawn(SparkMat, rc.sparkLife * Random.Range(0.5f, 1.1f));
                it.kind = 1;
                it.t.position = p + n * 0.01f;
                it.vel = n * Random.Range(0.4f, 1f) * rc.sparkSpeed + Random.insideUnitSphere * rc.sparkSpeed * 0.55f;
                it.grav = Vector3.down * 9f;
                it.size0 = (surface == Surface.Glass ? 0.010f : 0.016f) * scale; it.size1 = 0.003f; it.a0 = 1f;
                it.col = rc.spark;
            }
            if (surface == Surface.Metal)
            {
                // il lampo del colpo sul metallo
                var it = Spawn(SparkMat, 0.05f);
                it.kind = 4; it.t.position = p + n * 0.03f; it.vel = Vector3.zero; it.grav = Vector3.zero;
                it.size0 = 0.05f * scale; it.size1 = 0.12f * scale; it.a0 = 0.9f;
            }
            // rimbalzo: una traccia corta che riparte riflessa, col suo fischio
            bool ricochet = rc.ricochet > 0f && incoming.sqrMagnitude > 0.01f && Random.value < rc.ricochet;
            if (ricochet)
            {
                Vector3 r = Vector3.Reflect(incoming.normalized, n);
                r = (r + Random.insideUnitSphere * 0.35f + n * 0.15f).normalized;
                Tracer(p + n * 0.02f, p + r * Random.Range(4f, 9f), 150f, 0.006f, new Color(1f, 0.78f, 0.45f));
                Ricochets++;
            }
            // foro: texture, misura e lucido della superficie, illuminato dalla scena
            var h = holes[holeNext];
            if (h == null)
            {
                var go = new GameObject("foro");
                go.layer = 0; // effetti nel mondo, visibili anche dalla camera delle ottiche; nessun collider
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterial = DecalMat;
                h = holes[holeNext] = new Item { t = go.transform, r = r };
            }
            holeNext = (holeNext + 1) % holes.Length;
            var tex = HoleTex(surface);
            h.col = Color.white;
            h.light = LightOf(p, n); h.lightT = Time.time + 0.15f;
            h.mpb.SetTexture("_BaseMap", tex);
            h.mpb.SetColor("_BaseColor", h.light);
            h.r.SetPropertyBlock(h.mpb);
            h.t.gameObject.SetActive(true);
            h.t.position = p + n * 0.004f;
            h.t.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = Random.Range(rc.hole0, rc.hole1) * scale;
            h.t.localScale = new Vector3(s, s, 1f);
            h.age = 0f; h.life = 28f; h.alive = true;
            // suono: uno ogni 45 ms al massimo (i pallettoni di una fucilata ne fanno uno), solo se vicino
            bool near = Seats.Split ? Seats.AnyPlayerWithin(p, 45f) : camT != null && (camT.position - p).sqrMagnitude < 45f * 45f;
            if (near && Time.unscaledTime - lastImpactSound > 0.045f)
            {
                lastImpactSound = Time.unscaledTime;
                Sfx.At(SoundBank.Impact(surface), p + n * 0.05f, rc.vol * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(scale)), Random.Range(0.92f, 1.08f), 1.5f, 30f);
                ImpactSounds++;
            }
            if (ricochet) Sfx.At(SoundBank.Ricochet, p + n * 0.05f, 0.30f, Random.Range(0.9f, 1.12f), 2f, 35f);
            ImpactCount++; LastSurface = surface; LastHoleTexture = tex;
        }

        // ---------- sangue (2026-11-15)
        static Texture2D MakeBig(int n, System.Func<float, float, Color> f)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = f((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Schizzo: macchia centrale dal bordo frastagliato, goccioline attorno e una scia (verso +u: la direzione del colpo).
        /// Bianco: la tinta la da' il blocco di proprieta' (rosso scuro, verde dell'appestato, ...).</summary>
        static Texture2D BloodTex() => bloodTex != null ? bloodTex : bloodTex = MakeBig(128, (u, v) =>
        {
            float ang = Mathf.Atan2(v, u);
            float edge = 0.42f + 0.13f * Mathf.PerlinNoise(Mathf.Cos(ang) * 1.7f + 3f, Mathf.Sin(ang) * 1.7f + 7f)
                         + 0.10f * Mathf.PerlinNoise(Mathf.Cos(ang) * 5f + 11f, Mathf.Sin(ang) * 5f + 2f);
            float su = u < 0f ? u : u * 0.55f;                           // la macchia si allunga verso +u
            float r = Mathf.Sqrt(su * su + v * v);
            float a = Mathf.Clamp01((edge - r) * 18f);
            // goccioline: celle di una griglia sparsa, una su tre accesa, piu' fitte lungo la scia
            for (int k = 0; k < 14; k++)
            {
                float cx = Mathf.Sin(k * 12.9898f) * 0.5f + 0.5f, cy = Mathf.Sin(k * 78.233f) * 0.5f + 0.5f;
                Vector2 c = new Vector2(Mathf.Lerp(-0.2f, 0.95f, cx), Mathf.Lerp(-0.75f, 0.75f, cy));
                float rr = 0.025f + 0.05f * Mathf.Abs(Mathf.Sin(k * 3.7f));
                float d = Vector2.Distance(new Vector2(u, v), c);
                a = Mathf.Max(a, Mathf.Clamp01((rr - d) * 60f));
            }
            float shade = 0.72f + 0.28f * Mathf.PerlinNoise(u * 6f + 1f, v * 6f + 9f);
            return new Color(shade, shade, shade, a * 0.92f);
        });

        /// <summary>Pozza: un disco irregolare piu' scuro al centro.</summary>
        static Texture2D PoolTex() => poolTex != null ? poolTex : poolTex = MakeBig(128, (u, v) =>
        {
            float ang = Mathf.Atan2(v, u);
            float edge = 0.62f + 0.16f * Mathf.PerlinNoise(Mathf.Cos(ang) * 1.4f + 5f, Mathf.Sin(ang) * 1.4f + 1f)
                         + 0.08f * Mathf.PerlinNoise(Mathf.Cos(ang) * 4f + 9f, Mathf.Sin(ang) * 4f + 4f);
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Clamp01((edge - r) * 14f);
            float shade = 0.62f + 0.38f * Mathf.Clamp01(r / edge);
            return new Color(shade, shade, shade, a * 0.95f);
        });

        Item NextStain(Texture2D tex)
        {
            var h = stains[stainNext];
            if (h == null)
            {
                var go = new GameObject("macchia");
                go.layer = 0;
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterial = DecalMat;            // tinta dalla luce del punto (LightOf): al buio non si vede, sotto la torcia si'
                h = stains[stainNext] = new Item { t = go.transform, r = r };
            }
            stainNext = (stainNext + 1) % stains.Length;
            h.mpb.SetTexture("_BaseMap", tex);
            h.t.gameObject.SetActive(true);
            h.age = 0f; h.alive = true;
            return h;
        }

        void PlaceStain(Texture2D tex, Vector3 p, Vector3 n, Vector3 along, float size, float stretch, Color tint, float life, float grow)
        {
            var h = NextStain(tex);
            Vector3 up = Vector3.ProjectOnPlane(along, n);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Random.onUnitSphere, n);
            // l'asse u della texture (la scia) va lungo la direzione del colpo proiettata sul muro
            h.t.position = p + n * Random.Range(0.004f, 0.009f);
            h.t.rotation = Quaternion.LookRotation(-n, Vector3.Cross(-n, up.normalized));    // +X locale = direzione del colpo
            h.size1 = size; h.size0 = grow > 0f ? size * 0.35f : size; h.len = grow; h.a0 = stretch;
            h.life = life; h.col = tint;
            h.t.localScale = new Vector3(h.size0 * stretch, h.size0, 1f);
            h.light = LightOf(p, n); h.lightT = Time.time + 0.15f;
            h.mpb.SetColor("_BaseColor", Tinted(tint, h.light));
            h.r.SetPropertyBlock(h.mpb);
        }

        /// <summary>Colpo su un nemico: spruzzo di gocce e nebbiolina nel colore del suo sangue, e una macchia sul muro o sul pavimento
        /// dietro di lui se ce n'e' uno entro un paio di metri sulla traiettoria. Tinta con alfa 0 = solo polvere (pietra, falene).</summary>
        public static void Blood(Vector3 p, Vector3 dir, Color tint, bool head)
        {
            Ensure();
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            // gocce e nebbiolina sono Unlit: prendono la luce del punto (al buio il sangue non brilla)
            Color mist = Shade(new Color(tint.r, tint.g, tint.b, 1f), Mathf.Clamp(Surfaces.LightAt(p), 0.05f, 1.3f));
            bool dust = tint.a <= 0f;
            int drops = dust ? 0 : head ? 12 : 7;
            for (int i = 0; i < drops; i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.25f, 0.5f));
                it.kind = 0; it.t.position = p;
                it.vel = dir * Random.Range(1.5f, 4.5f) + Random.insideUnitSphere * 1.6f + Vector3.up * 0.6f;
                it.grav = Vector3.down * 9.8f;
                it.size0 = Random.Range(0.015f, 0.03f); it.size1 = it.size0 * 0.7f;
                it.a0 = 1f; it.col = mist * 0.8f;
            }
            for (int i = 0; i < (head ? 3 : 2); i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.3f, 0.55f));
                it.kind = 0; it.t.position = p + dir * 0.05f;
                it.vel = dir * Random.Range(0.4f, 1.0f) + Random.insideUnitSphere * 0.3f;
                it.grav = Vector3.zero;
                it.size0 = 0.06f; it.size1 = head ? 0.42f : 0.28f;
                it.a0 = dust ? 0.45f : 0.55f; it.col = dust ? Color.Lerp(mist, Color.gray, 0.3f) : mist;
            }
            if (dust) return;
            if (Physics.Raycast(p + dir * 0.15f, dir, out var wh, 2.4f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                I.PlaceStain(BloodTex(), wh.point, wh.normal, dir, Random.Range(0.28f, 0.5f) * (head ? 1.35f : 1f), Random.Range(1.0f, 1.5f), tint, 40f, 0f);
            if (Random.value < 0.45f && Physics.Raycast(p, Vector3.down, out var fh, 2.6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                I.PlaceStain(BloodTex(), fh.point, fh.normal, dir, Random.Range(0.22f, 0.4f), Random.Range(1.0f, 1.3f), tint, 40f, 0f);
        }

        /// <summary>Pozza di sangue sotto chi muore: cresce in un secondo e mezzo, resta, sfuma.</summary>
        public static void BloodPool(Vector3 pos, Color tint, float scale = 1f)
        {
            Ensure();
            if (!Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out var fh, 2.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return;
            var c = new Color(tint.r * 0.8f, tint.g * 0.8f, tint.b * 0.8f, 1f);
            I.PlaceStain(PoolTex(), fh.point, fh.normal, Random.onUnitSphere, Random.Range(1.0f, 1.45f) * scale, Random.Range(1.0f, 1.25f), c, 45f, 1.6f);
        }

        // ---------- varianti dell'orda
        /// <summary>Scoppio dell'Appestato: bile verde che schizza, una nuvola che si allarga e un lampo di luce verde.</summary>
        public static void PlagueBurst(Vector3 p)
        {
            Ensure();
            for (int i = 0; i < 24; i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.6f, 1.2f));
                it.kind = 0; it.t.position = p + Random.insideUnitSphere * 0.3f;
                it.vel = Random.insideUnitSphere * 3.4f + Vector3.up * 1.1f; it.grav = Vector3.down * 1.2f;
                it.size0 = 0.25f; it.size1 = Random.Range(1.2f, 2.1f); it.a0 = 0.65f; it.col = new Color(0.40f, 0.60f, 0.14f);
            }
            for (int i = 0; i < 26; i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.35f, 0.7f));
                it.kind = 0; it.t.position = p;
                it.vel = Random.insideUnitSphere * 7f + Vector3.up * 2.2f; it.grav = Vector3.down * 9.8f;
                it.size0 = Random.Range(0.03f, 0.07f); it.size1 = it.size0 * 0.6f; it.a0 = 1f; it.col = new Color(0.32f, 0.48f, 0.08f);
            }
            for (int i = 0; i < 10; i++)
            {
                var it = I.Spawn(SparkMat, Random.Range(0.2f, 0.4f));
                it.kind = 1; it.t.position = p;
                it.vel = Random.insideUnitSphere * 6f + Vector3.up; it.grav = Vector3.down * 6f;
                it.size0 = 0.035f; it.size1 = 0.005f; it.a0 = 1f; it.col = new Color(0.6f, 1f, 0.3f);
            }
            for (int i = 0; i < 5; i++)
            {
                Vector3 d = Random.onUnitSphere; if (d.y > 0.3f) d.y = -d.y;
                if (Physics.Raycast(p, d, out var h, 3.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                    I.PlaceStain(BloodTex(), h.point, h.normal, d, Random.Range(0.45f, 0.8f), 1.2f, new Color(0.20f, 0.26f, 0.05f, 1f), 40f, 0f);
            }
            var go = new GameObject("LampoScoppio");
            go.transform.position = p;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(0.55f, 1f, 0.25f); l.intensity = 6f; l.range = 9f; l.shadows = LightShadows.None;
            Destroy(go, 0.22f);
        }

        /// <summary>Esplosione dell'acquasanta (azzurra) o del carillon (calda): lampo di luce, nuvola che si allarga, gocce e schegge.</summary>
        public static void HolyBurst(Vector3 p, Color tint)
        {
            Ensure();
            for (int i = 0; i < 22; i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.7f, 1.4f));
                it.kind = 0; it.t.position = p + Random.insideUnitSphere * 0.4f;
                it.vel = Random.insideUnitSphere * 4.5f + Vector3.up * 1.6f; it.grav = Vector3.down * 0.8f;
                it.size0 = 0.4f; it.size1 = Random.Range(1.6f, 2.6f); it.a0 = 0.55f; it.col = Color.Lerp(tint, new Color(0.7f, 0.7f, 0.72f), 0.55f);
            }
            for (int i = 0; i < 36; i++)
            {
                var it = I.Spawn(SparkMat, Random.Range(0.3f, 0.7f));
                it.kind = 1; it.t.position = p;
                it.vel = Random.insideUnitSphere * 11f + Vector3.up * 3f; it.grav = Vector3.down * 9f;
                it.size0 = 0.05f; it.size1 = 0.008f; it.a0 = 1f; it.col = Color.Lerp(tint, Color.white, 0.5f);
            }
            for (int i = 0; i < 18; i++)
            {
                var it = I.Spawn(PuffMat, Random.Range(0.4f, 0.8f));
                it.kind = 0; it.t.position = p;
                it.vel = Random.insideUnitSphere * 6f + Vector3.up * 3f; it.grav = Vector3.down * 9.8f;
                it.size0 = 0.04f; it.size1 = 0.02f; it.a0 = 0.9f; it.col = tint;
            }
            var go = new GameObject("LampoEsplosione");
            go.transform.position = p + Vector3.up * 0.4f;
            var l = go.AddComponent<Light>();
            // con l'ombra: senza, il lampo passava i muri e accendeva la stanza accanto (dura un terzo di secondo, il costo e' breve)
            l.type = LightType.Point; l.color = Color.Lerp(tint, Color.white, 0.4f); l.intensity = 9f; l.range = 10f; l.shadows = LightShadows.Hard;
            go.AddComponent<LightFade>().Begin(l, 0.35f);
        }

        /// <summary>Uno sbuffo della nube tossica: sale lento e si allarga.</summary>
        public static void ToxicPuff(Vector3 p)
        {
            Ensure();
            var it = I.Spawn(PuffMat, Random.Range(1.6f, 2.4f));
            it.kind = 0; it.t.position = p;
            it.vel = Vector3.up * Random.Range(0.1f, 0.3f) + Random.insideUnitSphere * 0.25f; it.grav = Vector3.zero;
            it.size0 = 0.5f; it.size1 = Random.Range(1.4f, 2.0f); it.a0 = 0.20f; it.col = new Color(0.33f, 0.52f, 0.12f);
        }

        /// <summary>Anello di fuoco del Cardinale: lingue di fiamma che corrono dal centro fino al raggio.</summary>
        public static void FireRing(Vector3 c, float radius)
        {
            Ensure();
            for (int i = 0; i < 56; i++)
            {
                float a = i / 56f * Mathf.PI * 2f + Random.Range(-0.05f, 0.05f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var it = I.Spawn(SparkMat, Random.Range(0.45f, 0.8f));
                it.kind = 4; it.t.position = c + d * Random.Range(0.8f, 1.6f) + Vector3.up * 0.25f;
                it.vel = d * (radius / 0.55f) * Random.Range(0.8f, 1.05f) + Vector3.up * Random.Range(0.6f, 1.6f);
                it.grav = Vector3.zero;
                it.size0 = 0.35f; it.size1 = Random.Range(0.9f, 1.4f); it.a0 = 0.85f;
            }
        }

        /// <summary>Traccia luminosa da 'from' a 'to', a velocita' finita.</summary>
        public static void Tracer(Vector3 from, Vector3 to, float speed = 280f, float width = 0.010f, Color? color = null)
        {
            Ensure();
            float d = Vector3.Distance(from, to);
            if (d < 1.5f) return;
            var it = I.Spawn(TracerMat, d / speed + 0.12f);
            if (color.HasValue) it.col = color.Value;
            it.kind = 2; it.from = from; it.to = to; it.len = Mathf.Min(5f + speed * 0.015f, d);
            it.vel = (to - from) / Mathf.Max(0.001f, d) * speed;
            it.size0 = width; it.a0 = 1f;
            it.t.position = from;
        }

        /// <summary>Raggio istantaneo (armi aliene): una striscia luminosa da 'from' a 'to' che svanisce in un attimo.</summary>
        public static void Beam(Vector3 from, Vector3 to, float width, Color color)
        {
            Ensure();
            float d = Vector3.Distance(from, to);
            if (d < 0.5f) return;
            // alone arancio largo e nucleo caldo stretto: sommati in additivo danno il raggio incandescente
            for (int i = 0; i < 2; i++)
            {
                var it = I.Spawn(BeamMat, 0.20f);
                it.kind = 3; it.from = from; it.to = to; it.len = d;
                it.size0 = i == 0 ? width * 4.5f : width * 1.15f;
                it.col = i == 0 ? color : Color.Lerp(color, Color.white, 0.7f);
                it.a0 = i == 0 ? 0.55f : 1f;
                it.t.position = from;
            }
        }

        /// <summary>Getto di fiamme: due lingue per colpo che escono dalla bocca, si allargano e sfumano dal giallo al rosso.</summary>
        public static void Flame(Vector3 pos, Vector3 dir)
        {
            Ensure();
            // 18 colpi al secondo x 4 lingue: i quad si sommano in additivo e vicino alla bocca il getto e' bianco-giallo e denso
            for (int i = 0; i < 4; i++)
            {
                var it = I.Spawn(SparkMat, Random.Range(0.26f, 0.42f));
                it.kind = 4;
                it.t.position = pos + dir * (i * 0.05f) + Random.insideUnitSphere * 0.012f;
                it.vel = dir * Random.Range(6.5f, 10f) + Random.insideUnitSphere * 1.1f + Vector3.up * 0.9f;
                it.grav = Vector3.zero;
                it.size0 = 0.13f; it.size1 = Random.Range(0.50f, 0.80f); it.a0 = 0.8f;
            }
        }

        /// <summary>Bossolo: piccolo solido con fisica; tipo pistol | rifle | big | shell.</summary>
        public static void Casing(Vector3 pos, Vector3 vel, Quaternion rot, string kind)
        {
            Ensure();
            if (kind == "none") return;
            I.DoCasing(pos, vel, rot, kind);
        }

        void DoCasing(Vector3 pos, Vector3 vel, Quaternion rot, string kind)
        {
            GameObject go = null;
            if (casings.Count >= 24)
            {
                go = casings[0];
                casings.RemoveAt(0);
                if (go == null) { casings.RemoveAll(c => c == null); return; }
            }
            float r, h;
            switch (kind) { case "pistol": r = 0.0048f; h = 0.019f; break; case "big": r = 0.0105f; h = 0.075f; break; case "shell": r = 0.0102f; h = 0.070f; break; default: r = 0.0057f; h = 0.045f; break; }
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.layer = 2;
                go.transform.SetParent(transform, false);
                go.AddComponent<Rigidbody>();
                go.AddComponent<CasingSound>();
                var rend = go.GetComponent<MeshRenderer>();
                rend.shadowCastingMode = ShadowCastingMode.Off;
            }
            casings.Add(go);
            go.SetActive(true);
            go.transform.position = pos;
            go.transform.rotation = rot * Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(r * 2f, h * 0.5f, r * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = kind == "shell" ? ShellMat : BrassMat;
            var rb = go.GetComponent<Rigidbody>();
            rb.mass = 0.02f; rb.linearDamping = 0.1f; rb.angularDamping = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.linearVelocity = vel;
            rb.angularVelocity = Random.insideUnitSphere * 18f;
            var cc = go.GetComponent<Collider>();
            var player = GameManager.I != null && GameManager.I.player != null ? GameManager.I.player.GetComponent<CharacterController>() : null;
            if (player != null && cc != null) Physics.IgnoreCollision(cc, player);
            var cs = go.GetComponent<CasingSound>();
            cs.kind = kind; cs.hits = 0; cs.life = 7f;
        }

        void Update()
        {
            if (camT == null && Camera.main != null) camT = Camera.main.transform;
            float dt = Time.deltaTime;
            // schermo condiviso: ogni effetto si volta verso l'occhio che gli e' piu' vicino (di solito quello di chi l'ha provocato)
            int eyeN = 0;
            if (Seats.Split)
            {
                var eyes = Seats.Eyes();
                eyeN = Mathf.Min(eyes.Count, eyePos.Length);
                for (int e = 0; e < eyeN; e++) eyePos[e] = eyes[e].position;
            }
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                it.age += dt;
                float k = Mathf.Clamp01(it.age / it.life);
                if (it.age >= it.life)
                {
                    it.t.gameObject.SetActive(false);
                    it.alive = false;
                    free.Enqueue(it);
                    items[i] = items[items.Count - 1];
                    items.RemoveAt(items.Count - 1);
                    continue;
                }
                Vector3 camPos = camT != null ? camT.position : Vector3.zero;
                if (eyeN > 1)
                {
                    float best = float.MaxValue;
                    for (int e = 0; e < eyeN; e++)
                    {
                        float d = (eyePos[e] - it.t.position).sqrMagnitude;
                        if (d < best) { best = d; camPos = eyePos[e]; }
                    }
                }
                if (it.kind == 2)
                {
                    // traccia: segmento [testa - lunghezza, testa] lungo from->to
                    Vector3 dir = (it.to - it.from).normalized;
                    float total = Vector3.Distance(it.from, it.to);
                    float head = Mathf.Min(it.age * it.vel.magnitude, total);
                    float tail = Mathf.Max(0f, head - it.len);
                    Vector3 a = it.from + dir * tail, b = it.from + dir * head;
                    Vector3 mid = (a + b) * 0.5f;
                    float len = Mathf.Max(0.02f, head - tail);
                    Vector3 toCam = (camPos - mid);
                    Vector3 up = dir;
                    Vector3 side = Vector3.Cross(up, toCam).normalized;
                    Vector3 face = Vector3.Cross(side, up);
                    it.t.position = mid;
                    it.t.rotation = Quaternion.LookRotation(-face, up);
                    it.t.localScale = new Vector3(it.size0, len, 1f);
                    float fade = head >= total ? Mathf.Clamp01(1f - (it.age * it.vel.magnitude - total) / it.len) : 1f;
                    SetAlpha(it, fade * (it.age < 0.02f ? it.age / 0.02f : 1f));
                    continue;
                }
                if (it.kind == 3)
                {
                    // raggio: striscia piena da 'from' a 'to', luminosa e sottile che si allarga e sfuma
                    Vector3 dir = (it.to - it.from).normalized;
                    Vector3 mid = (it.from + it.to) * 0.5f;
                    Vector3 toCam = camPos - mid;
                    Vector3 side = Vector3.Cross(dir, toCam).normalized;
                    Vector3 face = Vector3.Cross(side, dir);
                    it.t.position = mid;
                    it.t.rotation = Quaternion.LookRotation(-face, dir);
                    float w = it.size0 * (1f + k * 1.6f);
                    it.t.localScale = new Vector3(w, it.len, 1f);
                    SetAlpha(it, it.a0 * Mathf.Pow(1f - k, 1.4f));
                    continue;
                }
                if (it.kind == 5)
                {
                    // scheggia: vola, cade, rimbalza un paio di volte sul pavimento, si ferma e sfuma alla fine
                    if (it.bounces < 3)
                    {
                        it.vel += it.grav * dt;
                        if (it.drag > 0f) it.vel *= Mathf.Max(0f, 1f - it.drag * dt);
                        it.t.position += it.vel * dt;
                        it.ang += it.spin * dt;
                        Vector3 q = it.t.position;
                        if (q.y < it.floorY + 0.004f && it.vel.y < 0f)
                        {
                            q.y = it.floorY + 0.004f; it.t.position = q;
                            if (++it.bounces >= 3 || -it.vel.y < 0.6f) { it.bounces = 3; it.vel = Vector3.zero; it.spin = 0f; }
                            else { it.vel = new Vector3(it.vel.x * 0.45f, -it.vel.y * 0.3f, it.vel.z * 0.45f); it.spin *= 0.5f; }
                        }
                    }
                    it.t.rotation = Quaternion.LookRotation(it.t.position - camPos) * Quaternion.Euler(0f, 0f, it.ang);
                    it.t.localScale = new Vector3(it.size0 * it.stretch, it.size0, 1f);
                    SetAlpha(it, it.a0 * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.7f) / 0.3f))));
                    continue;
                }
                it.vel += it.grav * dt;
                it.t.position += it.vel * dt;
                if (it.kind == 0) it.vel *= Mathf.Max(0f, 1f - 2.2f * dt);
                float s = Mathf.Lerp(it.size0, it.size1, it.kind == 0 || it.kind == 4 ? Mathf.Sqrt(k) : k);
                Vector3 toC = camPos - it.t.position;
                if (it.kind == 1)
                {
                    // scintilla: allungata lungo la velocita'
                    Vector3 v = it.vel;
                    float sp = v.magnitude;
                    Vector3 up = sp > 0.01f ? v / sp : Vector3.up;
                    Vector3 side = Vector3.Cross(up, toC).normalized;
                    Vector3 face = Vector3.Cross(side, up);
                    it.t.rotation = Quaternion.LookRotation(-face, up);
                    it.t.localScale = new Vector3(s, s * (2f + sp * 0.9f), 1f);
                    SetAlpha(it, 1f - k);
                }
                else if (it.kind == 4)
                {
                    // fiamma: sfera luminosa che sale, dal giallo caldo al rosso scuro
                    it.t.rotation = Quaternion.LookRotation(-toC);
                    it.t.localScale = new Vector3(s, s, 1f);
                    it.col = Color.Lerp(new Color(1f, 0.88f, 0.45f), new Color(1f, 0.22f, 0.04f), Mathf.Sqrt(k));
                    SetAlpha(it, it.a0 * Mathf.Pow(1f - k, 1.2f) * Mathf.Clamp01(it.age / 0.012f));
                }
                else
                {
                    it.t.rotation = Quaternion.LookRotation(-toC);
                    it.t.localScale = new Vector3(s, s, 1f);
                    SetAlpha(it, it.a0 * (1f - k) * Mathf.Clamp01(it.age / 0.05f));
                }
            }
            // macchie: si allargano un attimo (la pozza cresce), poi sfumano alla fine
            for (int i = 0; i < stains.Length; i++)
            {
                var h = stains[i];
                if (h == null || !h.alive) continue;
                h.age += dt;
                if (h.age > h.life) { h.t.gameObject.SetActive(false); h.alive = false; continue; }
                float g = h.len > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(h.age / h.len)) : 1f;
                float sx = Mathf.Lerp(h.size0, h.size1, g);
                h.t.localScale = new Vector3(sx * h.a0, sx, 1f);
                float fa = h.age > h.life - 6f ? (h.life - h.age) / 6f : 1f;
                if (Time.time >= h.lightT) { h.lightT = Time.time + 0.15f; h.light = LightOf(h.t.position, -h.t.forward); }
                var sc = Tinted(h.col, h.light);
                h.mpb.SetColor("_BaseColor", new Color(sc.r, sc.g, sc.b, h.col.a * fa));
                h.r.SetPropertyBlock(h.mpb);
            }
            // fori: sfumano alla fine
            for (int i = 0; i < holes.Length; i++)
            {
                var h = holes[i];
                if (h == null || !h.alive) continue;
                h.age += dt;
                if (h.age > h.life) { h.t.gameObject.SetActive(false); h.alive = false; continue; }
                // la luce che lo tinge (la torcia che passa lo accende), e la dissolvenza degli ultimi 4 s
                float fa = h.age > h.life - 4f ? (h.life - h.age) / 4f : 1f;
                bool relight = Time.time >= h.lightT;
                if (relight) { h.lightT = Time.time + 0.15f; h.light = LightOf(h.t.position, -h.t.forward); }
                if (relight || fa < 1f) { h.mpb.SetColor("_BaseColor", new Color(h.light.r, h.light.g, h.light.b, fa)); h.r.SetPropertyBlock(h.mpb); }
            }
        }

        static void SetAlpha(Item it, float a)
        {
            it.mpb.SetColor("_BaseColor", new Color(it.col.r, it.col.g, it.col.b, Mathf.Clamp01(a)));
            it.r.SetPropertyBlock(it.mpb);
        }

        /// <summary>Solo per le prove: elementi vivi di un tipo (5 = schegge; 'resting' solo quelle ferme sul pavimento).</summary>
        public static int Count(int kind, bool resting = false)
        {
            if (I == null) return 0;
            int n = 0;
            foreach (var it in I.items) if (it.kind == kind && (!resting || it.bounces >= 3)) n++;
            return n;
        }

        /// <summary>Solo per le prove: i fori vivi (posizione, scala, texture, materiale, visibilita') e l'alfa media di ogni texture dei fori.</summary>
        public static string HoleDebug()
        {
            if (I == null) return "Fx non creato";
            var sb = new System.Text.StringBuilder();
            for (int s = 0; s < 7; s++)
            {
                var px = HoleTex((Surface)s).GetPixels32();
                double a = 0; foreach (var c in px) a += c.a;
                sb.Append($"{(Surface)s} alfa {a / px.Length / 255.0:F2}; ");
            }
            foreach (var h in I.holes)
            {
                if (h == null || !h.alive) continue;
                var tex = h.mpb.GetTexture("_BaseMap");
                sb.Append($"\n  foro {h.t.position:F2} sc {h.t.localScale.x:F3} tex {(tex != null ? tex.name : "null")} mat {h.r.sharedMaterial?.name} en {h.r.enabled} vis {h.r.isVisible} age {h.age:F1}");
            }
            return sb.ToString();
        }

        /// <summary>Solo per le prove: quanto in alto sta la scheggia ferma piu' alta sopra il suo pavimento (deve essere ~4 mm).</summary>
        public static float RestingChipLift()
        {
            float worst = 0f;
            if (I == null) return worst;
            foreach (var it in I.items) if (it.kind == 5 && it.bounces >= 3 && it.floorY > -1e3f) worst = Mathf.Max(worst, it.t.position.y - it.floorY);
            return worst;
        }

        /// <summary>Solo per le prove: riepilogo degli elementi vivi (tipo, posizione, scala, visibilita').</summary>
        public static string Dump()
        {
            if (I == null) return "Fx non creato";
            var sb = new System.Text.StringBuilder();
            sb.Append("items=").Append(I.items.Count).Append(" camT=").Append(I.camT != null);
            int n = 0;
            foreach (var it in I.items)
            {
                if (n++ >= 6) break;
                sb.Append($" [k{it.kind} pos={it.t.position:F2} sc={it.t.localScale:F3} vis={it.r.isVisible} en={it.r.enabled} act={it.t.gameObject.activeInHierarchy} mat={(it.r.sharedMaterial != null ? it.r.sharedMaterial.name : "null")} age={it.age:F3}/{it.life:F2}]");
            }
            return sb.ToString();
        }

        /// <summary>Toglie tutto (cambio piano).</summary>
        public static void Clear()
        {
            if (I == null) return;
            foreach (var it in I.items) { it.t.gameObject.SetActive(false); I.free.Enqueue(it); }
            I.items.Clear();
            foreach (var h in I.holes) if (h != null) { h.t.gameObject.SetActive(false); h.alive = false; }
            foreach (var h in I.stains) if (h != null) { h.t.gameObject.SetActive(false); h.alive = false; }
            foreach (var c in I.casings) if (c != null) c.SetActive(false);
        }
    }

    /// <summary>Piccolo tintinnio quando un bossolo tocca terra.</summary>
    public class CasingSound : MonoBehaviour
    {
        public string kind; public int hits; public float life;
        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f) gameObject.SetActive(false);
        }
        void OnCollisionEnter(Collision c)
        {
            if (hits >= 2 || c.relativeVelocity.magnitude < 0.6f) return;
            hits++;
            var clip = SoundBank.Casing(kind);
            if (clip != null) Sfx.At(clip, transform.position, Mathf.Clamp01(c.relativeVelocity.magnitude * 0.12f) * 0.5f,
                Random.Range(0.9f, 1.15f), 1f, 14f);
        }
    }
}
