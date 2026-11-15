using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Effetti dei colpi: tracce luminose, polvere e scintille agli impatti, fori sui muri, bossoli che rimbalzano.
    /// Tutto a quad con materiali Unlit (niente ParticleSystem, che in build perde lo shader); riuso a pool.</summary>
    public class Fx : MonoBehaviour
    {
        static Fx I;
        static Mesh quadMesh;
        public static Material PuffMat, SparkMat, TracerMat, BeamMat, HoleMat, FlashMat, BrassMat, ShellMat;
        static Texture2D puffTex, sparkTex, tracerTex, beamTex, holeTex, starTex;

        class Item
        {
            public Transform t; public Renderer r; public MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            public float life, age, size0, size1, a0; public Vector3 vel, grav; public int kind; public Vector3 from, to; public float len;
            public bool alive;
            public Color col = Color.white;
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

        public static void Impact(Vector3 p, Vector3 n, float scale = 1f)
        {
            Ensure();
            I.DoImpact(p, n, scale);
        }

        void DoImpact(Vector3 p, Vector3 n, float scale)
        {
            if (camT == null && Camera.main != null) camT = Camera.main.transform;
            // polvere: poche nuvolette che salgono lente
            for (int i = 0; i < 4; i++)
            {
                var it = Spawn(PuffMat, Random.Range(0.55f, 0.95f));
                it.kind = 0;
                it.t.position = p + n * 0.02f + Random.insideUnitSphere * 0.02f;
                it.vel = (n * Random.Range(0.25f, 0.8f) + Random.insideUnitSphere * 0.35f + Vector3.up * 0.15f);
                it.grav = Vector3.zero;
                it.size0 = 0.05f * scale; it.size1 = Random.Range(0.22f, 0.36f) * scale;
                it.a0 = 0.30f;
            }
            // scintille: righe sottili additive
            for (int i = 0; i < 6; i++)
            {
                var it = Spawn(SparkMat, Random.Range(0.14f, 0.32f));
                it.kind = 1;
                it.t.position = p + n * 0.01f;
                it.vel = n * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 2.2f;
                it.grav = Vector3.down * 9f;
                it.size0 = 0.018f * scale; it.size1 = 0.004f; it.a0 = 1f;
            }
            // foro sul muro
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
                r.sharedMaterial = HoleMat;
                h = holes[holeNext] = new Item { t = go.transform, r = r };
            }
            holeNext = (holeNext + 1) % holes.Length;
            h.t.gameObject.SetActive(true);
            h.t.position = p + n * 0.004f;
            h.t.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float s = Random.Range(0.07f, 0.10f) * scale;
            h.t.localScale = new Vector3(s, s, 1f);
            h.age = 0f; h.life = 28f; h.alive = true;
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
                r.sharedMaterial = HoleMat;
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
            h.mpb.SetColor("_BaseColor", tint);
            h.r.SetPropertyBlock(h.mpb);
        }

        /// <summary>Colpo su un nemico: spruzzo di gocce e nebbiolina nel colore del suo sangue, e una macchia sul muro o sul pavimento
        /// dietro di lui se ce n'e' uno entro un paio di metri sulla traiettoria. Tinta con alfa 0 = solo polvere (pietra, falene).</summary>
        public static void Blood(Vector3 p, Vector3 dir, Color tint, bool head)
        {
            Ensure();
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Color mist = new Color(tint.r, tint.g, tint.b, 1f);
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
                h.mpb.SetColor("_BaseColor", new Color(h.col.r, h.col.g, h.col.b, h.col.a * fa));
                h.r.SetPropertyBlock(h.mpb);
            }
            // fori: sfumano alla fine
            for (int i = 0; i < holes.Length; i++)
            {
                var h = holes[i];
                if (h == null || !h.alive) continue;
                h.age += dt;
                if (h.age > h.life) { h.t.gameObject.SetActive(false); h.alive = false; continue; }
                if (h.age > h.life - 4f) { h.mpb.SetColor("_BaseColor", new Color(1, 1, 1, (h.life - h.age) / 4f)); h.r.SetPropertyBlock(h.mpb); }
                else if (h.age < dt * 2f + 0.001f) { h.mpb.SetColor("_BaseColor", Color.white); h.r.SetPropertyBlock(h.mpb); }
            }
        }

        static void SetAlpha(Item it, float a)
        {
            it.mpb.SetColor("_BaseColor", new Color(it.col.r, it.col.g, it.col.b, Mathf.Clamp01(a)));
            it.r.SetPropertyBlock(it.mpb);
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
