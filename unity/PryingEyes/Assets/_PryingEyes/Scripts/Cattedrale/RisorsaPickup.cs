using System.Collections.Generic;
using Production.Economy;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace PryingEyes.Cattedrale
{
    /// <summary>Le risorse lasciate da un nemico (o le tue spoglie): un piccolo oggetto del colore della risorsa che galleggia e gira sul
    /// pavimento. Passandoci vicino vola verso di te e finisce nello zaino (Fabbrica.Raccogli). Ossa bianche, legno, lingotti d'acciaio;
    /// le rare (brace, cuori, ampolle di sangue) e quelle dei boss (cristalli, gemme, reliquiari) brillano e hanno una colonna di luce.
    /// Tutto procedurale e senza luci: con decine a terra il costo resta quello di qualche cubo. Restano 90 secondi, le spoglie per sempre.
    /// In cooperativa ognuno vede e raccoglie le sue: il bottino lo tira a sorte ogni macchina (Raccolto.cs).</summary>
    public sealed class RisorsaPickup : MonoBehaviour
    {
        public const float Magnete = 3.4f, Presa = 0.75f, Vita = 90f, Hover = 0.32f;
        public Bundle loot = Bundle.Empty;
        public bool spoglie;
        float born, phase, flight;
        Transform body, beam;
        bool flying;
        static readonly List<RisorsaPickup> live = new List<RisorsaPickup>();
        static readonly List<PlayerController> players = new List<PlayerController>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Material beamMat;
        public static IReadOnlyList<RisorsaPickup> Live => live;
        /// <summary>Per le prove: quante ne sono nate e quante sono state prese.</summary>
        public static int statSpawned, statTaken;

        public static RisorsaPickup Spawn(Bundle loot, Vector3 pos, bool spoglie = false)
        {
            if (loot == null || loot.IsEmpty) return null;
            Vector3 g = pos;
            if (Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out RaycastHit h, 6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) g = h.point;
            if (NavMesh.SamplePosition(g, out NavMeshHit nh, 1.5f, NavMesh.AllAreas)) g = nh.position;
            var go = new GameObject(spoglie ? "Spoglie" : "Risorse");
            go.transform.position = g + new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
            var p = go.AddComponent<RisorsaPickup>();
            p.loot = loot; p.spoglie = spoglie;
            p.Build();
            live.Add(p);
            statSpawned++;
            return p;
        }

        public static void ClearAll()
        {
            foreach (var p in live.ToArray()) if (p != null) Destroy(p.gameObject);
            live.Clear();
        }

        /// <summary>La risorsa che da' la forma: la piu' preziosa del mucchio.</summary>
        string Main()
        {
            string best = null; int grade = -1;
            foreach (var e in loot)
            {
                var d = Risorse.Of(e.Key);
                int gr = d == null ? 0 : (int)d.grado;
                if (gr > grade) { grade = gr; best = e.Key; }
            }
            return best;
        }

        void Build()
        {
            born = Time.time; phase = Random.value * 6.28f;
            string id = Main();
            var def = Risorse.Of(id);
            var col = def != null ? new Color(def.r, def.g, def.b) : Color.white;
            var b = new GameObject("Corpo");
            b.transform.SetParent(transform, false);
            body = b.transform;
            body.localPosition = new Vector3(0f, Hover, 0f);
            var m = Mat(id, col, def != null && def.grado >= Grado.Rara);
            if (spoglie) { Part(PrimitiveType.Sphere, new Vector3(0, 0.05f, 0), new Vector3(0.22f, 0.26f, 0.22f), Mat("ossa", new Color(0.93f, 0.89f, 0.78f), false), Quaternion.identity); }
            switch (id)
            {
                case Risorse.Ossa:
                    Part(PrimitiveType.Capsule, Vector3.zero, new Vector3(0.06f, 0.13f, 0.06f), m, Quaternion.Euler(0, 0, 90));
                    Part(PrimitiveType.Sphere, new Vector3(-0.13f, 0, 0), Vector3.one * 0.075f, m, Quaternion.identity);
                    Part(PrimitiveType.Sphere, new Vector3(0.13f, 0, 0), Vector3.one * 0.075f, m, Quaternion.identity);
                    break;
                case Risorse.Legno:
                    Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.07f, 0.07f, 0.26f), m, Quaternion.Euler(0, 0, 12));
                    Part(PrimitiveType.Cube, new Vector3(0.05f, -0.03f, 0.02f), new Vector3(0.06f, 0.05f, 0.2f), m, Quaternion.Euler(0, 25, -8));
                    break;
                case Risorse.Acciaio:
                    Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.16f, 0.05f, 0.08f), m, Quaternion.identity);
                    Part(PrimitiveType.Cube, new Vector3(0, 0.05f, 0), new Vector3(0.12f, 0.04f, 0.06f), m, Quaternion.identity);
                    break;
                case Risorse.Brace:
                    Part(PrimitiveType.Sphere, Vector3.zero, new Vector3(0.12f, 0.09f, 0.11f), m, Quaternion.identity);
                    break;
                case Risorse.Cuori:
                    Part(PrimitiveType.Sphere, new Vector3(-0.03f, 0.02f, 0), Vector3.one * 0.1f, m, Quaternion.identity);
                    Part(PrimitiveType.Sphere, new Vector3(0.03f, 0.02f, 0), Vector3.one * 0.1f, m, Quaternion.identity);
                    Part(PrimitiveType.Cube, new Vector3(0, -0.035f, 0), new Vector3(0.085f, 0.085f, 0.085f), m, Quaternion.Euler(0, 0, 45));
                    break;
                case Risorse.Sangue:
                    Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.07f, 0.07f, 0.07f), m, Quaternion.identity);
                    Part(PrimitiveType.Cylinder, new Vector3(0, 0.09f, 0), new Vector3(0.03f, 0.03f, 0.03f), Mat("acciaio", new Color(0.7f, 0.75f, 0.82f), false), Quaternion.identity);
                    break;
                case Risorse.Reliquie:
                    Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.16f, 0.1f, 0.1f), m, Quaternion.identity);
                    Part(PrimitiveType.Cube, new Vector3(0, 0.08f, 0), new Vector3(0.03f, 0.08f, 0.03f), m, Quaternion.identity);
                    Part(PrimitiveType.Cube, new Vector3(0, 0.1f, 0), new Vector3(0.07f, 0.025f, 0.03f), m, Quaternion.identity);
                    break;
                case Risorse.Oboli:
                    Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.12f, 0.012f, 0.12f), m, Quaternion.Euler(80, 0, 0));
                    break;
                default:      // cristalli e gemme: due prismi incrociati
                    Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.07f, 0.2f, 0.07f), m, Quaternion.Euler(0, 45, 12));
                    Part(PrimitiveType.Cube, new Vector3(0.05f, -0.03f, 0), new Vector3(0.05f, 0.13f, 0.05f), m, Quaternion.Euler(0, 20, -28));
                    break;
            }
            if (def != null && def.grado >= Grado.Rara || spoglie) BuildBeam(col, def != null && def.grado == Grado.Boss || spoglie ? 3.2f : 1.6f);
        }

        void Part(PrimitiveType t, Vector3 p, Vector3 s, Material m, Quaternion r)
        {
            var g = GameObject.CreatePrimitive(t);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(body, false);
            g.transform.localPosition = p; g.transform.localScale = s; g.transform.localRotation = r;
            var mr = g.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        void BuildBeam(Color col, float height)
        {
            // lo stesso fascio morbido dei potenziamenti (ShrineKit): forte in basso, svanisce in alto, nessun bordo netto
            if (beamMat == null)
                beamMat = Proc.ShrineKit.Additive(Proc.ShrineKit.SoftTex("RisorsaFascio", 16, 64, (u, v) =>
                {
                    float x = u * 2f - 1f, lo = Mathf.Clamp01(v / 0.06f);
                    return Mathf.Exp(-x * x * 3.2f) * Mathf.Pow(1f - v, 1.6f) * (lo * lo * (3f - 2f * lo));
                }));
            beam = new GameObject("Colonna").transform;
            beam.SetParent(transform, false);
            for (int i = 0; i < 2; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(beam, false);
                q.transform.localPosition = new Vector3(0, height * 0.5f, 0);
                q.transform.localScale = new Vector3(0.16f, height, 1f);
                q.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);
                var mr = q.GetComponent<MeshRenderer>();
                mr.sharedMaterial = beamMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var mpb = new MaterialPropertyBlock();
                var c = col * 0.55f; c.a = 0.35f;
                mpb.SetColor("_BaseColor", c);
                mr.SetPropertyBlock(mpb);
            }
        }

        static Material Mat(string id, Color col, bool glow)
        {
            string key = (id ?? "?") + (glow ? "+" : "");
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "risorsa_" + key };
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Smoothness", id == Risorse.Acciaio || id == Risorse.Reliquie || id == Risorse.Oboli ? 0.75f : 0.35f);
            m.SetFloat("_Metallic", id == Risorse.Acciaio || id == Risorse.Reliquie || id == Risorse.Oboli ? 0.85f : 0f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", col * (glow ? 2.6f : 0.22f));
            mats[key] = m;
            return m;
        }

        void OnDestroy() { live.Remove(this); }

        void Update()
        {
            float age = Time.time - born;
            if (!spoglie && age > Vita) { Destroy(gameObject); return; }
            float grow = Mathf.Clamp01(age / 0.35f);
            body.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, grow) * (spoglie ? 1.4f : 1f);
            Seats.Players(players);
            PlayerController near = null; float best = float.MaxValue;
            foreach (var pl in players)
            {
                if (pl == null || !pl.alive) continue;
                float d = Vector3.Distance(pl.transform.position + Vector3.up * 0.9f, body.position);
                if (d < best) { best = d; near = pl; }
            }
            if (near != null && (flying || best < Magnete * (spoglie ? 0.6f : 1f)))
            {
                flying = true;
                flight += Time.deltaTime;
                Vector3 target = near.transform.position + Vector3.up * 0.9f;
                body.position = Vector3.MoveTowards(body.position, target, (3f + flight * 14f) * Time.deltaTime);
                if (beam != null) beam.gameObject.SetActive(false);
                if (Vector3.Distance(body.position, target) < Presa) { Take(); return; }
            }
            else
            {
                body.localPosition = new Vector3(0f, Hover + Mathf.Sin(Time.time * 2.2f + phase) * 0.05f, 0f);
                body.localRotation = Quaternion.Euler(0f, (Time.time * 70f + phase * 57f) % 360f, 0f);
            }
        }

        void Take()
        {
            statTaken++;
            if (spoglie) Fabbrica.SpoglieRiprese(); else Fabbrica.Raccogli(loot);
            var def = Risorse.Of(Main());
            var col = def != null ? new Color(def.r, def.g, def.b) : Color.white;
            string text = (spoglie ? "Le tue spoglie: " : "+ ") + Risorse.Testo(loot);
            Seats.ForHuds(h => h.RisorsaFeed(text, col));
            Sfx.UI(SoundBank.PickCoins, def != null && def.grado >= Grado.Rara ? 0.42f : 0.22f, def != null && def.grado >= Grado.Rara ? 0.8f : 1.25f);
            Destroy(gameObject);
        }
    }
}
