using UnityEngine;

namespace PryingEyes
{
    /// <summary>Effetti dei colpi della medusa, tutti con il sistema Fx del gioco (quad Unlit in pool, nessun ParticleSystem):
    /// raccolta (puntini luminosi risucchiati nella bocca mentre gli organi si accendono), affondo (getto di muco dalla bocca),
    /// contatto (ombrello di gocce allungate, scintille, nebbia tossica, lampo, anello d'urto, macchie e luce verde), colpo ricevuto,
    /// scoppio alla morte, gocce che pendono dalle punte e spore che salgono dalla campana.</summary>
    public static class JellyFx
    {
        /// <summary>Spento (foto dell'editor, dove Fx non esiste), nessun effetto.</summary>
        public static bool Enabled = true;

        public static readonly Color Slime = new Color(0.50f, 0.80f, 0.16f);
        public static readonly Color Venom = new Color(0.72f, 1.00f, 0.32f);
        public static readonly Color Lime = new Color(0.55f, 1.00f, 0.15f);
        /// <summary>Macchie sui muri e sul pavimento (Fx.Blood: alfa 1 = sangue, qui bile verde).</summary>
        public static readonly Color Stain = new Color(0.30f, 0.52f, 0.05f, 1f);

        static Material puffMat, sparkMat;

        /// <summary>Particella con materiali propri (copie di quelli di Fx): le prove degli impatti contano solo i materiali standard, cosi'
        /// il getto dell'affondo, le gocce e le spore non passano per un falso impatto dopo un colpo a vuoto. Il contatto usa quelli standard.</summary>
        static void P(int kind, bool additive, Vector3 pos, Vector3 vel, Vector3 grav, float size0, float size1, float life, Color col, float alpha = 1f)
        {
            if (puffMat == null) { puffMat = Fx.Unlit(Fx.PuffTex(), false); puffMat.name = "Fx_PuffMuco"; sparkMat = Fx.Unlit(Fx.SparkTex(), true); sparkMat.name = "Fx_SparkMuco"; }
            Fx.Particle(kind, additive, pos, vel, grav, size0, size1, life, col, alpha, additive ? sparkMat : puffMat);
        }

        /// <summary>Raccolta: k va da 0 a 1 in 0,55 s. Puntini luminosi che arrivano da attorno e si schiantano nella bocca, e un alone che cresce.</summary>
        public static void Gather(Vector3 mouth, Vector3 up, float k)
        {
            if (!Enabled) return;
            int n = k < 0.25f ? 1 : k < 0.7f ? 2 : 3;
            Color c = Color.Lerp(Lime, Venom, k);
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                float r = Random.Range(0.55f, 1.2f);
                P(1, true, mouth + dir * r, -dir * (r / 0.30f), Vector3.zero, 0.055f, 0.012f, 0.30f, c, 0.9f);
            }
            P(0, true, mouth, Vector3.zero, Vector3.zero, 0.10f + 0.28f * k, 0.18f + 0.5f * k, 0.10f, Lime, 0.10f + 0.35f * k);
            if (k > 0.6f && Random.value < 0.35f)
                P(0, false, mouth - up * 0.05f, Vector3.down * 0.15f, Vector3.down * 3f, 0.02f, 0.012f, 0.6f, Slime, 0.95f);   // bava dai denti
        }

        /// <summary>Affondo: la bocca schizza un getto di muco davanti a se' (il contatto, se c'e', arriva dopo 0,16 s).</summary>
        public static void Lunge(Vector3 mouth, Vector3 dir)
        {
            if (!Enabled) return;
            for (int i = 0; i < 26; i++)
            {
                Vector3 v = dir * Random.Range(5f, 11f) + Random.insideUnitSphere * 1.3f;
                P(1, false, mouth + dir * Random.Range(0f, 0.25f), v, Vector3.down * 9.8f, Random.Range(0.03f, 0.06f), 0.015f, Random.Range(0.35f, 0.6f),
                            Color.Lerp(Slime, Venom, Random.value * 0.6f), 0.95f);
            }
            for (int i = 0; i < 12; i++)
                P(1, true, mouth, dir * Random.Range(6f, 13f) + Random.insideUnitSphere * 1.6f, Vector3.zero, 0.05f, 0.006f, Random.Range(0.18f, 0.32f), Venom, 1f);
            for (int i = 0; i < 4; i++)
                P(0, false, mouth + dir * 0.15f, dir * Random.Range(1.2f, 2.8f) + Random.insideUnitSphere * 0.4f, Vector3.zero, 0.12f, Random.Range(0.6f, 0.95f), 0.5f,
                            new Color(0.42f, 0.64f, 0.14f), 0.42f);
            P(0, true, mouth + dir * 0.1f, Vector3.zero, Vector3.zero, 0.3f, 1.0f, 0.12f, Color.Lerp(Lime, Color.white, 0.5f), 0.8f);
            Flash(mouth + dir * 0.3f, Lime, 2.2f, 6f, 0.25f);
        }

        /// <summary>Contatto con il bersaglio: 'back' e' la direzione che va dal bersaglio alla medusa (da li' parte lo schizzo).</summary>
        public static void Contact(Vector3 point, Vector3 back)
        {
            if (!Enabled) return;
            back = back.sqrMagnitude > 1e-6f ? back.normalized : Vector3.up;
            for (int i = 0; i < 40; i++)
            {
                Vector3 v = back * Random.Range(1.5f, 5.5f) + Random.insideUnitSphere * 2.6f + Vector3.up * Random.Range(0.2f, 2f);
                Fx.Particle(1, false, point + Random.insideUnitSphere * 0.05f, v, Vector3.down * 9.8f, Random.Range(0.025f, 0.055f), 0.012f, Random.Range(0.35f, 0.7f),
                            Color.Lerp(Slime, Venom, Random.value * 0.5f), 0.95f);
            }
            for (int i = 0; i < 16; i++)
                Fx.Particle(1, true, point, Random.insideUnitSphere * 6.5f + back * 2f, Vector3.down * 5f, 0.04f, 0.006f, Random.Range(0.2f, 0.45f), Venom, 1f);
            for (int i = 0; i < 5; i++)
                Fx.Particle(0, false, point + back * 0.1f + Random.insideUnitSphere * 0.15f, back * Random.Range(0.3f, 1f) + Random.insideUnitSphere * 0.5f, Vector3.up * 0.25f,
                            0.15f, Random.Range(0.8f, 1.3f), Random.Range(0.8f, 1.4f), new Color(0.40f, 0.62f, 0.14f), 0.5f);
            Fx.Particle(0, true, point, Vector3.zero, Vector3.zero, 0.5f, 1.5f, 0.14f, Color.Lerp(Lime, Color.white, 0.4f), 0.9f);
            ShockRing.Spawn(point, back, Lime, 1.25f, 0.4f);
            Fx.Blood(point, back, Stain, true);
            Flash(point + back * 0.5f, Lime, 1.8f, 7f, 0.32f);
        }

        /// <summary>Colpo ricevuto: poche gocce luminose dal punto colpito e un balenio degli organi.</summary>
        public static void Hit(Vector3 point, Vector3 dir)
        {
            if (!Enabled) return;
            for (int i = 0; i < 6; i++)
                P(1, false, point, dir * Random.Range(1.5f, 4.5f) + Random.insideUnitSphere * 1.2f, Vector3.down * 9.8f, Random.Range(0.025f, 0.045f), 0.01f,
                            Random.Range(0.3f, 0.55f), Color.Lerp(Slime, Venom, Random.value), 0.95f);
            for (int i = 0; i < 8; i++)
                P(1, true, point, dir * Random.Range(2f, 6f) + Random.insideUnitSphere * 2.2f, Vector3.zero, 0.04f, 0.006f, Random.Range(0.15f, 0.3f), Venom, 1f);
            P(0, true, point, Vector3.zero, Vector3.zero, 0.12f, 0.38f, 0.10f, Lime, 0.7f);
        }

        /// <summary>Morte: scoppio di muco e luce dagli organi, anello d'urto basso e nuvola verde.</summary>
        public static void Death(Vector3 center, Vector3 mouth)
        {
            if (!Enabled) return;
            for (int i = 0; i < 46; i++)
                P(1, false, center + Random.insideUnitSphere * 0.25f, Random.onUnitSphere * Random.Range(2f, 7f) + Vector3.up * 1.2f, Vector3.down * 9.8f,
                            Random.Range(0.03f, 0.07f), 0.015f, Random.Range(0.45f, 0.9f), Color.Lerp(Slime, Venom, Random.value * 0.6f), 0.95f);
            for (int i = 0; i < 26; i++)
                P(1, true, center, Random.onUnitSphere * Random.Range(3f, 9f), Vector3.down * 4f, 0.05f, 0.006f, Random.Range(0.25f, 0.55f), Venom, 1f);
            for (int i = 0; i < 8; i++)
                P(0, false, center + Random.insideUnitSphere * 0.4f, Random.insideUnitSphere * 1.6f + Vector3.up * 0.4f, Vector3.zero, 0.3f, Random.Range(1.4f, 2.2f),
                            Random.Range(1.0f, 1.8f), new Color(0.38f, 0.60f, 0.14f), 0.5f);
            P(0, true, center, Vector3.zero, Vector3.zero, 0.6f, 2.4f, 0.22f, Lime, 0.8f);
            ShockRing.Spawn(center, Vector3.up, Lime, 2.2f, 0.55f);
            // il lampo sta davanti alla campana, verso chi guarda e fuori dalle sue pareti: sulla superficie (o dentro, a 30 cm dalle
            // pareti) la brucerebbe di bianco, come mostrava la foto 'morta_subito'
            Vector3 toCam = Camera.main != null ? Camera.main.transform.position - center : Vector3.up * 3f;
            Flash(center + toCam.normalized * Mathf.Clamp(toCam.magnitude * 0.7f, 1.4f, 2.6f), Lime, 2.6f, 9f, 0.5f);
        }

        /// <summary>Una goccia di muco che si stacca dalla punta di un tentacolo.</summary>
        public static void Drip(Vector3 p)
        {
            if (!Enabled) return;
            P(0, false, p, Vector3.down * 0.15f, Vector3.down * 9.8f, 0.02f, 0.013f, Random.Range(0.7f, 1.1f), Color.Lerp(Slime, Venom, 0.3f), 0.9f);
        }

        /// <summary>Una spora luminosa che sale dalla campana.</summary>
        public static void Mote(Vector3 p)
        {
            if (!Enabled) return;
            P(0, true, p, Vector3.up * Random.Range(0.12f, 0.4f) + Random.insideUnitSphere * 0.12f, Vector3.zero, 0.035f, 0.004f, Random.Range(1.4f, 2.4f), Lime, 0.6f);
        }

        static void Flash(Vector3 p, Color c, float intensity, float range, float seconds)
        {
            var go = new GameObject("LampoMuco");
            go.transform.position = p;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            go.AddComponent<LightFade>().Begin(l, seconds);
        }
    }

    /// <summary>Anello d'urto sottile e luminoso che si allarga e sfuma: un quad additivo rivolto verso chi lo guarda, nel piano
    /// perpendicolare alla direzione dell'urto. Si distrugge da solo.</summary>
    public sealed class ShockRing : MonoBehaviour
    {
        static Mesh quad;
        static Material mat;
        static Texture2D tex;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        float t, dur, size;
        Color col;
        MeshRenderer rend;
        MaterialPropertyBlock mpb;

        /// <summary>radius: raggio finale in metri; normal: asse dell'anello (verso di chi lo vede).</summary>
        public static void Spawn(Vector3 pos, Vector3 normal, Color color, float radius, float seconds)
        {
            if (quad == null) quad = MakeQuad();
            if (tex == null) tex = MakeRingTex();
            if (mat == null) mat = Fx.Unlit(tex, true);
            var go = new GameObject("AnelloMuco");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 1e-6f ? normal : Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var ring = go.AddComponent<ShockRing>();
            ring.rend = r; ring.col = color; ring.dur = Mathf.Max(0.1f, seconds); ring.size = radius * 2f;
            ring.mpb = new MaterialPropertyBlock();
            ring.Apply(0f);
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            Apply(k);
            if (k >= 1f) Destroy(gameObject);
        }

        void Apply(float k)
        {
            float e = 1f - (1f - k) * (1f - k) * (1f - k);          // si allarga in fretta e rallenta
            transform.localScale = Vector3.one * Mathf.Lerp(0.12f, size, e);
            float a = Mathf.Pow(1f - k, 1.6f);
            mpb.SetColor(BaseColor, new Color(col.r, col.g, col.b, a));
            rend.SetPropertyBlock(mpb);
        }

        static Mesh MakeQuad()
        {
            var m = new Mesh { name = "AnelloMuco" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.1f));
            return m;
        }

        /// <summary>Anello nitido a r = 0,82 con un bagliore morbido dentro e una sfumatura fuori.</summary>
        static Texture2D MakeRingTex()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.045f, 2f));
                    float halo = Mathf.Exp(-Mathf.Pow((r - 0.78f) / 0.16f, 2f)) * 0.35f;
                    float inner = Mathf.Clamp01(1f - r / 0.8f) * 0.10f;
                    float a = Mathf.Clamp01(core + halo + inner) * Mathf.Clamp01((1f - r) * 6f);
                    px[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }
    }
}
