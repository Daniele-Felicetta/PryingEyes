using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Un potenziamento a terra (vedi Drops): un oggetto che galleggia e gira con un bagliore del suo colore, tutto procedurale.
    /// Reliquiario: un forziere di legno con il coperchio a botte d'ottone e la serratura che brilla. Sguardo: un occhio che ti
    /// segue con l'iride rossa. Offerta: un calice d'ottone pieno d'oro fuso. Campana: una campana di bronzo con il battaglio che
    /// splende. Si prende passandoci a meno di due metri; dopo 30 s sparisce, lampeggiando gli ultimi otto.</summary>
    public sealed class DropPickup : MonoBehaviour
    {
        public DropKind kind;
        public const float PickRadius = 1.9f, Hover = 0.55f;

        float born, phase, spin;
        Transform body, beam;
        Light glow;
        bool shown = true;
        // fascio di luce e pozza sul pavimento: i modelli sono piccoli (20-30 cm), cosi' il potenziamento si nota anche da lontano
        Renderer[] beamRends;
        MaterialPropertyBlock beamMpb;
        Color beamColor;

        static Material wood, brass, bone, bronze, dark, emAmber, emRed, emGold, emWhite, beamMat, poolMat;
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        public float Age => Time.time - born;
        /// <summary>Cooperativa: identificativo di rete (lo da' l'ospite) e quando e' stata fatta l'ultima richiesta di raccolta.</summary>
        [System.NonSerialized] public int netId = -1;
        float claimAt = -99f;
        /// <summary>Cooperativa: un potenziamento nato da un po' (chi entra a partita in corso lo vede gia' invecchiato).</summary>
        public void SetAge(float age) { born = Time.time - Mathf.Max(0f, age); }
        /// <summary>Il centro dell'oggetto (per la distanza di raccolta e per le prove).</summary>
        public Vector3 Center => transform.position + Vector3.up * (Hover + 0.12f);

        public void Init(DropKind k)
        {
            kind = k;
            born = Time.time;
            phase = Random.value * 6.28f;
            Mats();
            var b = new GameObject("Corpo");
            b.transform.SetParent(transform, false);
            body = b.transform;
            body.localPosition = new Vector3(0f, Hover, 0f);
            switch (k)
            {
                case DropKind.Ammo: BuildChest(body); break;
                case DropKind.Insta: BuildEye(body); break;
                case DropKind.Double: BuildChalice(body); break;
                default: BuildBell(body); break;
            }
            var lg = new GameObject("Bagliore");
            lg.transform.SetParent(body, false);
            lg.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            glow = lg.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = k == DropKind.Ammo ? new Color(1f, 0.70f, 0.28f) : k == DropKind.Insta ? new Color(1f, 0.16f, 0.10f)
                : k == DropKind.Double ? new Color(1f, 0.84f, 0.30f) : new Color(0.72f, 0.86f, 1f);
            glow.intensity = 1.5f;
            glow.range = 6.5f;
            glow.shadows = LightShadows.None;
            var pulse = lg.AddComponent<LightPulse>();
            pulse.l = glow; pulse.baseI = 1.5f; pulse.amp = 0.28f; pulse.speed = 3.1f;
            body.localScale = Vector3.zero;
            BuildBeam(k);
        }

        /// <summary>Tre piani verticali incrociati (una colonna morbida che sale dal pavimento) e una pozza di luce a terra, nel colore
        /// del potenziamento: ambra il reliquiario, rosso lo sguardo, oro l'offerta, azzurro la campana. Sono figli della radice e non del
        /// corpo: l'occhio che ruota verso di te non deve inclinarli.</summary>
        void BuildBeam(DropKind k)
        {
            beamColor = k == DropKind.Ammo ? new Color(1f, 0.64f, 0.24f) : k == DropKind.Insta ? new Color(1f, 0.20f, 0.12f)
                : k == DropKind.Double ? new Color(1f, 0.86f, 0.34f) : new Color(0.60f, 0.80f, 1f);
            beamMpb = new MaterialPropertyBlock();
            beam = new GameObject("Fascio").transform;
            beam.SetParent(transform, false);
            beamRends = new Renderer[4];
            for (int i = 0; i < 3; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Piano" + (i + 1);
                Util.Kill(q.GetComponent<Collider>());
                q.transform.SetParent(beam, false);
                q.transform.localPosition = new Vector3(0f, 1.15f, 0f);
                q.transform.localRotation = Quaternion.Euler(0f, 60f * i, 0f);
                q.transform.localScale = new Vector3(0.62f, 2.3f, 1f);
                beamRends[i] = Soft(q.GetComponent<MeshRenderer>(), beamMat);
            }
            var pool = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pool.name = "Pozza";
            Util.Kill(pool.GetComponent<Collider>());
            pool.transform.SetParent(beam, false);
            pool.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pool.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
            beamRends[3] = Soft(pool.GetComponent<MeshRenderer>(), poolMat);
            beam.localScale = Vector3.zero;
        }

        static Renderer Soft(MeshRenderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        void Update()
        {
            float t = Time.time - born;
            // compare con un piccolo rimbalzo, poi galleggia
            float pop = Mathf.Clamp01(t / 0.35f);
            float sc = pop < 1f ? Mathf.Sin(pop * Mathf.PI * 0.5f) * (1f + 0.22f * Mathf.Sin(pop * Mathf.PI)) : 1f;
            body.localScale = Vector3.one * sc;
            beam.localScale = Vector3.one * sc;
            body.localPosition = new Vector3(0f, Hover + 0.05f * Mathf.Sin(Time.time * 2.1f + phase), 0f);
            var pl = GameManager.I != null ? GameManager.I.player : null;
            if (kind == DropKind.Insta && pl != null)
            {
                Vector3 to = pl.cam.position - body.position;
                if (to.sqrMagnitude > 0.01f) body.rotation = Quaternion.Slerp(body.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
            else
            {
                spin += 75f * Time.deltaTime;
                body.localRotation = Quaternion.Euler(0f, spin, kind == DropKind.Nuke ? 6f * Mathf.Sin(Time.time * 2.4f + phase) : 0f);
            }

            float left = Drops.Life - t;
            if (left <= 0f) { Drops.statExpired++; Drops.Forget(this); Destroy(gameObject); return; }
            // gli ultimi otto secondi lampeggia, sempre piu' svelto
            bool vis = left > 8f || Mathf.Repeat(Time.time * (left > 3f ? 3f : 7f), 1f) < 0.62f;
            if (vis != shown) { shown = vis; body.gameObject.SetActive(vis); beam.gameObject.SetActive(vis); }
            if (shown)
            {
                // il fascio respira con la luce (stessa velocita' del bagliore)
                float br = 0.84f + 0.16f * Mathf.Sin(Time.time * 3.1f + phase);
                beamMpb.SetColor("_BaseColor", new Color(beamColor.r, beamColor.g, beamColor.b, 0.34f * br));
                for (int i = 0; i < 3; i++) beamRends[i].SetPropertyBlock(beamMpb);
                beamMpb.SetColor("_BaseColor", new Color(beamColor.r, beamColor.g, beamColor.b, 0.50f * br));
                beamRends[3].SetPropertyBlock(beamMpb);
            }

            if (pl != null && pl.alive && !GameMenu.Blocked && sc > 0.9f)
            {
                Vector3 c = pl.transform.position + Vector3.up * 1.0f;
                if ((c - Center).sqrMagnitude < PickRadius * PickRadius)
                {
                    if (Coop.IsClient)
                    {
                        // ospitato: lo dice all'ospite, che decide chi l'ha preso e lo racconta a tutti (DropTaken)
                        if (Time.unscaledTime - claimAt > 1f) { claimAt = Time.unscaledTime; Coop.Game?.ClaimDrop(this); }
                    }
                    else Drops.Take(this, Coop.Slot);
                }
            }
        }

        void OnDestroy() { Drops.Forget(this); }

        // ------------------------------------------------------------------ materiali
        static Material Lit(string name, Color c, float metallic, float smooth, bool doubleSided = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = c };
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (doubleSided) m.SetFloat("_Cull", 0f);
            return m;
        }

        static Material Emit(string name, Color c, float k)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = Color.black };
            m.SetFloat("_Smoothness", 0.2f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * k);
            return m;
        }

        static void Mats()
        {
            if (wood != null) return;
            wood = Lit("Drop_Legno", new Color(0.16f, 0.085f, 0.045f), 0f, 0.35f);
            brass = Lit("Drop_Ottone", new Color(0.78f, 0.56f, 0.18f), 0.92f, 0.72f, true);
            bone = Lit("Drop_Osso", new Color(0.84f, 0.80f, 0.70f), 0f, 0.55f);
            bronze = Lit("Drop_Bronzo", new Color(0.36f, 0.23f, 0.11f), 0.9f, 0.62f, true);
            dark = Lit("Drop_Nero", new Color(0.015f, 0.012f, 0.012f), 0f, 0.8f);
            emAmber = Emit("Drop_Ambra", new Color(1f, 0.62f, 0.12f), 2.8f);
            emRed = Emit("Drop_Rosso", new Color(1f, 0.10f, 0.06f), 3.2f);
            emGold = Emit("Drop_Oro", new Color(1f, 0.78f, 0.24f), 2.4f);
            emWhite = Emit("Drop_Bianco", new Color(1f, 0.95f, 0.85f), 3.0f);
            // fascio (v = 0 e' il pavimento: piu' forte in basso, svanisce in alto) e pozza di luce: sfumature morbide, nessun bordo netto
            beamMat = ShrineKit.Additive(ShrineKit.SoftTex("DropFascio", 32, 64, (u, v) =>
            {
                float x = u * 2f - 1f;
                float lo = Mathf.Clamp01(v / 0.06f);
                return Mathf.Exp(-x * x * 3.2f) * Mathf.Pow(1f - v, 1.6f) * (lo * lo * (3f - 2f * lo));
            }));
            poolMat = ShrineKit.Additive(ShrineKit.SoftTex("DropPozza", 32, 32, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                float e = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y));
                return e * e;
            }));
        }

        // ------------------------------------------------------------------ forme
        static GameObject Prim(Transform parent, PrimitiveType t, string name, Material m, Vector3 pos, Vector3 scale, Vector3 euler = default)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            Util.Kill(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = scale;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        static GameObject LatheObj(Transform parent, string name, Material m, Mesh mesh)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        /// <summary>Reliquiario: forziere di legno (30 x 15 x 18 cm), coperchio a botte d'ottone, due fasce, serratura d'ambra.</summary>
        static void BuildChest(Transform p)
        {
            Prim(p, PrimitiveType.Cube, "Cassa", wood, new Vector3(0f, 0.04f, 0f), new Vector3(0.32f, 0.15f, 0.20f));
            // cilindro coricato lungo X: la scala y e' mezza lunghezza (33 cm in tutto)
            Prim(p, PrimitiveType.Cylinder, "Coperchio", brass, new Vector3(0f, 0.115f, 0f), new Vector3(0.19f, 0.165f, 0.19f), new Vector3(0f, 0f, 90f));
            foreach (float x in new[] { -0.105f, 0.105f })
                Prim(p, PrimitiveType.Cube, "Fascia", brass, new Vector3(x, 0.04f, 0f), new Vector3(0.024f, 0.158f, 0.208f));
            Prim(p, PrimitiveType.Cube, "Serratura", emAmber, new Vector3(0f, 0.07f, 0.103f), new Vector3(0.05f, 0.06f, 0.012f));
            Prim(p, PrimitiveType.Cube, "Gancio", brass, new Vector3(0f, 0.115f, 0.104f), new Vector3(0.034f, 0.028f, 0.014f));
        }

        /// <summary>Sguardo: bulbo d'osso di 22 cm con l'iride rossa che brilla e la pupilla verticale; guarda sempre il giocatore.</summary>
        static void BuildEye(Transform p)
        {
            Prim(p, PrimitiveType.Sphere, "Bulbo", bone, new Vector3(0f, 0.11f, 0f), Vector3.one * 0.23f);
            Prim(p, PrimitiveType.Sphere, "Iride", emRed, new Vector3(0f, 0.11f, 0.092f), new Vector3(0.135f, 0.135f, 0.05f));
            Prim(p, PrimitiveType.Sphere, "Pupilla", dark, new Vector3(0f, 0.11f, 0.116f), new Vector3(0.036f, 0.095f, 0.022f));
        }

        /// <summary>Offerta: calice d'ottone (22 cm) con l'oro fuso che luccica nella coppa.</summary>
        static void BuildChalice(Transform p)
        {
            LatheObj(p, "Calice", brass, Lathe("dropCalice", new[]
            {
                0.000f, 0.000f, 0.065f, 0.000f, 0.070f, 0.008f, 0.060f, 0.020f, 0.020f, 0.032f, 0.011f, 0.050f, 0.011f, 0.100f,
                0.022f, 0.118f, 0.055f, 0.145f, 0.072f, 0.185f, 0.074f, 0.215f, 0.068f, 0.218f, 0.064f, 0.190f, 0.050f, 0.150f, 0.000f, 0.140f,
            }, 28)).transform.localPosition = new Vector3(0f, -0.01f, 0f);
            LatheObj(p, "Oro", emGold, Lathe("dropOro", new[] { 0.062f, 0.186f, 0.000f, 0.186f }, 28)).transform.localPosition = new Vector3(0f, -0.01f, 0f);
            for (int i = 0; i < 3; i++)       // tre monete sul bordo, di taglio
            {
                float a = i * 2.094f + 0.4f;
                Prim(p, PrimitiveType.Cylinder, "Moneta", emGold, new Vector3(Mathf.Cos(a) * 0.052f, 0.215f, Mathf.Sin(a) * 0.052f),
                    new Vector3(0.05f, 0.004f, 0.05f), new Vector3(70f, a * 57.3f, 0f));
            }
        }

        /// <summary>Campana: bronzo di 23 cm con la spalla larga, battaglio bianco che splende, anello in cima.</summary>
        static void BuildBell(Transform p)
        {
            LatheObj(p, "Campana", bronze, Lathe("dropCampana", new[]
            {
                0.115f, 0.000f, 0.112f, 0.012f, 0.100f, 0.045f, 0.080f, 0.095f, 0.062f, 0.140f, 0.048f, 0.178f, 0.036f, 0.205f, 0.024f, 0.222f, 0.012f, 0.230f, 0.000f, 0.232f,
                0.000f, 0.215f, 0.022f, 0.208f, 0.035f, 0.190f, 0.050f, 0.160f, 0.068f, 0.120f, 0.088f, 0.075f, 0.104f, 0.030f, 0.108f, 0.000f, 0.115f, 0.000f,
            }, 30)).transform.localPosition = new Vector3(0f, -0.01f, 0f);
            Prim(p, PrimitiveType.Sphere, "Battaglio", emWhite, new Vector3(0f, 0.012f, 0f), Vector3.one * 0.05f);
            Prim(p, PrimitiveType.Cylinder, "Anello", bronze, new Vector3(0f, 0.24f, 0f), new Vector3(0.034f, 0.014f, 0.034f), new Vector3(0f, 0f, 90f));
        }

        /// <summary>Solido di rotazione. 'prof' sono coppie (raggio, quota), percorse con l'esterno a destra della direzione di marcia
        /// (verso l'alto: la normale esce dall'asse); le normali sono lisce lungo la curva tranne agli spigoli (oltre 40 gradi).</summary>
        public static Mesh Lathe(string name, float[] prof, int segs)
        {
            if (meshes.TryGetValue(name, out var cached) && cached != null) return cached;
            int n = prof.Length / 2;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            Vector2 P(int i) => new Vector2(prof[i * 2], prof[i * 2 + 1]);
            Vector2 SegN(int s)
            {
                Vector2 d = P(s + 1) - P(s);
                return d.sqrMagnitude < 1e-10f ? Vector2.right : new Vector2(d.y, -d.x).normalized;
            }
            for (int s = 0; s < n - 1; s++)
            {
                Vector2 ns = SegN(s);
                int baseIdx = v.Count;
                for (int e = 0; e < 2; e++)
                {
                    int pi = s + e, nb = e == 0 ? s - 1 : s + 1;
                    Vector2 nn = ns;
                    if (nb >= 0 && nb < n - 1)
                    {
                        Vector2 no = SegN(nb);
                        if (Vector2.Dot(ns, no) > Mathf.Cos(40f * Mathf.Deg2Rad)) nn = (ns + no).normalized;
                    }
                    for (int j = 0; j <= segs; j++)
                    {
                        float a = (float)j / segs * Mathf.PI * 2f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                        v.Add(new Vector3(prof[pi * 2] * ca, prof[pi * 2 + 1], prof[pi * 2] * sa));
                        nr.Add(new Vector3(nn.x * ca, nn.y, nn.x * sa).normalized);
                        uv.Add(new Vector2((float)j / segs, prof[pi * 2 + 1] * 4f));
                    }
                }
                int row = segs + 1;
                for (int j = 0; j < segs; j++)
                {
                    int a0 = baseIdx + j, b0 = baseIdx + row + j;      // a: anello s, b: anello s+1
                    tr.Add(a0); tr.Add(b0); tr.Add(b0 + 1);
                    tr.Add(a0); tr.Add(b0 + 1); tr.Add(a0 + 1);
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(tr, 0);
            m.RecalculateBounds();
            meshes[name] = m;
            return m;
        }
    }
}
