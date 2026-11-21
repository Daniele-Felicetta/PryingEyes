using System.Collections.Generic;
using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    public enum PickKind { Points, Ammo, Medikit, Essence }

    /// <summary>Quello che si trova nel deserto, nelle stanze e in giro (oltre alle armi, che sono pannelli): punti (monete d'oro), munizioni (una
    /// cassetta), medikit (una scatola bianca con la croce rossa) ed essenza (un cristallo che brilla, la risorsa della mano sinistra). Si prende
    /// passandoci a un metro e mezzo; il segno resta in DesertMode.Taken, cosi' un luogo ricaricato non lo rimette. Una colonna di luce morbida
    /// del suo colore lo fa vedere da lontano. Tutto procedurale, nessuna luce vera (con decine in giro il costo sono pochi cubi).</summary>
    public sealed class DesertPickup : MonoBehaviour
    {
        public PickKind kind;
        public int amount;
        public long memory;
        const float Reach = 1.6f, Hover = 0.42f;

        float phase;
        Transform body;
        static readonly List<DesertPickup> live = new List<DesertPickup>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Material beamMat;
        public static int statSpawned, statTaken;
        public static IReadOnlyList<DesertPickup> Live => live;

        public static Color ColorOf(PickKind k)
        {
            switch (k)
            {
                case PickKind.Points: return new Color(1f, 0.78f, 0.24f);
                case PickKind.Ammo: return new Color(1f, 0.55f, 0.18f);
                case PickKind.Medikit: return new Color(1f, 0.20f, 0.22f);
                default: return new Color(0.34f, 0.82f, 1f);
            }
        }

        /// <summary>Mette un oggetto a terra (la quota e' quella data: chi chiama l'ha gia' trovata).</summary>
        public static DesertPickup Spawn(Transform parent, Vector3 pos, PickKind kind, int amount, long memory)
        {
            if (memory != 0 && DesertMode.Taken.Contains(memory)) return null;
            var go = new GameObject("Trovato_" + kind);
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.position = pos;
            var p = go.AddComponent<DesertPickup>();
            p.kind = kind; p.amount = amount; p.memory = memory;
            p.Build();
            live.Add(p);
            statSpawned++;
            return p;
        }

        void OnDestroy() { live.Remove(this); }

        void Build()
        {
            phase = Random.value * 6.28f;
            var col = ColorOf(kind);
            var b = new GameObject("Corpo");
            b.transform.SetParent(transform, false);
            body = b.transform;
            body.localPosition = new Vector3(0f, Hover, 0f);
            switch (kind)
            {
                case PickKind.Points:
                    for (int i = 0; i < 3; i++)
                        Part(PrimitiveType.Cylinder, new Vector3(0.025f * (i - 1), 0.04f * i - 0.04f, 0.02f * (1 - i)), new Vector3(0.20f, 0.012f, 0.20f), Mat("oro", col, 0.9f, 0.8f, 0.35f), new Vector3(8f * i, 40f * i, 4f * i));
                    break;
                case PickKind.Ammo:
                    Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.34f, 0.18f, 0.20f), Mat("cassa", new Color(0.20f, 0.23f, 0.12f), 0f, 0.35f, 0f), Vector3.zero);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.095f, 0f), new Vector3(0.36f, 0.02f, 0.22f), Mat("ottone", new Color(0.78f, 0.56f, 0.18f), 0.9f, 0.7f, 0f), Vector3.zero);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0.01f, 0.101f), new Vector3(0.12f, 0.05f, 0.01f), Mat("ambra", col, 0f, 0.3f, 2.2f), Vector3.zero);
                    break;
                case PickKind.Medikit:
                    Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.30f, 0.20f, 0.20f), Mat("bianco", new Color(0.88f, 0.88f, 0.84f), 0f, 0.5f, 0f), Vector3.zero);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0f, 0.101f), new Vector3(0.14f, 0.045f, 0.01f), Mat("croce", col, 0f, 0.3f, 2.0f), Vector3.zero);
                    Part(PrimitiveType.Cube, new Vector3(0f, 0f, 0.101f), new Vector3(0.045f, 0.14f, 0.01f), Mat("croce", col, 0f, 0.3f, 2.0f), Vector3.zero);
                    break;
                default:
                    Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.09f, 0.26f, 0.09f), Mat("cristallo", col, 0f, 0.9f, 2.6f), new Vector3(0f, 45f, 14f));
                    Part(PrimitiveType.Cube, new Vector3(0.06f, -0.04f, 0.02f), new Vector3(0.06f, 0.17f, 0.06f), Mat("cristallo", col, 0f, 0.9f, 2.6f), new Vector3(0f, 20f, -30f));
                    Part(PrimitiveType.Cube, new Vector3(-0.05f, -0.05f, -0.03f), new Vector3(0.05f, 0.14f, 0.05f), Mat("cristallo", col, 0f, 0.9f, 2.6f), new Vector3(20f, 70f, 24f));
                    break;
            }
            BuildBeam(col);
        }

        void Part(PrimitiveType t, Vector3 p, Vector3 s, Material m, Vector3 euler)
        {
            var g = GameObject.CreatePrimitive(t);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(body, false);
            g.transform.localPosition = p; g.transform.localScale = s; g.transform.localRotation = Quaternion.Euler(euler);
            var mr = g.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        void BuildBeam(Color col)
        {
            if (beamMat == null)
                beamMat = ShrineKit.Additive(ShrineKit.SoftTex("DesertFascio", 16, 64, (u, v) =>
                {
                    float x = u * 2f - 1f, lo = Mathf.Clamp01(v / 0.06f);
                    return Mathf.Exp(-x * x * 3.2f) * Mathf.Pow(1f - v, 1.6f) * (lo * lo * (3f - 2f * lo));
                }));
            var beam = new GameObject("Colonna").transform;
            beam.SetParent(transform, false);
            const float h = 2.8f;
            for (int i = 0; i < 2; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(beam, false);
                q.transform.localPosition = new Vector3(0, h * 0.5f, 0);
                q.transform.localScale = new Vector3(0.22f, h, 1f);
                q.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);
                var mr = q.GetComponent<MeshRenderer>();
                mr.sharedMaterial = beamMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var mpb = new MaterialPropertyBlock();
                var c = col * 0.6f; c.a = 0.34f;
                mpb.SetColor("_BaseColor", c);
                mr.SetPropertyBlock(mpb);
            }
        }

        static Material Mat(string id, Color col, float metallic, float smooth, float emit)
        {
            string key = id + (emit > 0f ? "+" : "") + col.r.ToString("F2") + col.g.ToString("F2") + col.b.ToString("F2");
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "trovato_" + key };
            m.SetColor("_BaseColor", emit > 0f ? col * 0.25f : col);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (emit > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", col * emit);
            }
            mats[key] = m;
            return m;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            float t = Time.time + phase;
            body.localPosition = new Vector3(0f, Hover + Mathf.Sin(t * 2.2f) * 0.05f, 0f);
            body.localRotation = Quaternion.Euler(0f, t * 70f % 360f, 0f);
            if (GameMenu.Blocked) return;
            if (Seats.Split)
            {
                // schermo condiviso: lo prende chi gli e' a portata (il primo dei posti) e il premio e' suo (punti, munizioni e vita sono del posto)
                foreach (var seat in Seats.All)
                {
                    var one = seat.player;
                    if (one == null || !one.alive || !InReach(one)) continue;
                    using (Seats.Enter(seat)) Take(one);
                    return;
                }
                return;
            }
            var pl = gm.player;
            if (!pl.alive) return;
            if (InReach(pl)) Take(pl);
        }

        bool InReach(PlayerController pl)
        {
            Vector3 d = pl.transform.position + Vector3.up * 0.9f - body.position;
            return d.sqrMagnitude <= Reach * Reach;
        }

        void Take(PlayerController pl)
        {
            var gm = GameManager.I;
            string text;
            switch (kind)
            {
                case PickKind.Points:
                    gm.AddPoints(amount);
                    text = $"+{amount} punti";
                    break;
                case PickKind.Ammo:
                    pl.weapon.AddAmmoFraction(0.5f);
                    text = "Munizioni";
                    break;
                case PickKind.Medikit:
                    pl.Heal(amount);
                    text = $"Medikit: +{amount} vita";
                    break;
                default:
                    var a = pl.Arcana;
                    a.Essence = Mathf.Min(a.EssenceMax, a.Essence + amount);
                    text = $"Essenza +{amount}";
                    break;
            }
            if (memory != 0) DesertMode.Taken.Add(memory);
            statTaken++;
            gm.hud?.Toast(text);
            Sfx.UI(SoundBank.PickCoins, kind == PickKind.Points ? 0.3f : 0.22f, kind == PickKind.Essence ? 1.4f : kind == PickKind.Points ? 1.2f : 0.95f);
            Destroy(gameObject);
        }
    }
}
