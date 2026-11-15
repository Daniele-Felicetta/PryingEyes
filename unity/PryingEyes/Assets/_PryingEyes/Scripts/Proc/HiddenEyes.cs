using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Gli occhi di vetro (2026-11-15): la caccia segreta di ogni piano. Tre occhi nascosti: prima dentro gli annessi (torre,
    /// cripta, giardino), poi sulle balconate, poi negli angoli piu' lontani dall'arrivo. Galleggiano all'altezza del viso, si girano a
    /// guardarti quando sei vicino, mandano un filo di luce verdastra e respirano piano (si sentono da pochi metri). Si prendono con F
    /// o sparandogli. Trovati tutti e tre: l'arma che hai in mano viene consacrata gratis (se lo e' gia', punti) e 1000 punti.
    /// Ognuno trova i suoi (lo stato e' di questa macchina, come i tesori degli annessi).</summary>
    public sealed class HiddenEyes : MonoBehaviour
    {
        public static HiddenEyes I;
        readonly List<GlassEye> eyes = new List<GlassEye>();
        public IReadOnlyList<GlassEye> Eyes => eyes;
        public int Found { get; private set; }
        public int Total => eyes.Count;

        public static HiddenEyes Build(FloorInstance fi)
        {
            if (fi == null || fi.plan == null) return null;
            var c = fi.root.AddComponent<HiddenEyes>();
            I = c;
            var rng = new System.Random(fi.plan.seed * 17 + 3);
            var spots = new List<Vector3>();
            // 1) dentro gli annessi
            foreach (var s in fi.annexSites)
                if (s != null && HotelNav.Ground(s.inside + Vector3.up * 0.5f, out Vector3 g)) spots.Add(g);
            // 2) sulle balconate, 3) negli angoli lontani dall'arrivo
            Vector3 arrival = fi.arrival != null ? fi.arrival.spawnPoint : Vector3.zero;
            var gallery = new List<Vector3>();
            var far = new List<Vector3>(fi.groundSpawns);
            foreach (var p in fi.groundSpawns) if (p.y > 2.5f) gallery.Add(p);
            for (int i = gallery.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (gallery[i], gallery[j]) = (gallery[j], gallery[i]); }
            spots.AddRange(gallery);
            far.Sort((a, b) => (b - arrival).sqrMagnitude.CompareTo((a - arrival).sqrMagnitude));
            spots.AddRange(far);
            foreach (var p in spots)
            {
                if (c.eyes.Count >= 3) break;
                bool spaced = true;
                foreach (var e in c.eyes) if ((e.transform.position - p).sqrMagnitude < 12f * 12f) { spaced = false; break; }
                if (!spaced) continue;
                var go = new GameObject("OcchioDiVetro_" + c.eyes.Count);
                go.transform.SetParent(c.transform, false);
                go.transform.position = p + Vector3.up * 1.35f;
                var eye = go.AddComponent<GlassEye>();
                eye.owner = c;
                eye.Build();
                c.eyes.Add(eye);
            }
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] occhi di vetro: {c.eyes.Count} ({fi.annexSites.Count} annessi)");
            return c;
        }

        internal void Collect(GlassEye e)
        {
            Found++;
            var gm = GameManager.I;
            Sfx.UI(SoundBank.PickEye, 0.9f, 1f);
            if (Found < Total)
            {
                gm?.hud?.Toast($"Occhio di vetro {Found}/{Total}. Ti guardano ancora.");
                return;
            }
            var w = gm != null && gm.player != null ? gm.player.weapon : null;
            bool blessed = w != null && w.CanConsecrate && w.Consecrate();
            gm?.AddPoints(blessed ? 1000 : 3500);
            gm?.hud?.Banner("LA VISIONE", blessed ? "Tre occhi aperti: l'arma che hai in mano e' consacrata." : "Tre occhi aperti: +3500 punti.");
            Sfx.UI(SoundBank.Consecrate, 0.9f, 1f);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] occhi di vetro: tutti e {Total}, {(blessed ? "arma consacrata" : "punti")}");
        }

        void OnDestroy() { if (I == this) I = null; }
    }

    public sealed class GlassEye : MonoBehaviour, IInteract, IShootable
    {
        internal HiddenEyes owner;
        bool taken;
        Transform ball;
        Light glow;
        AudioSource breath;
        float baseY;

        public Vector3 Point => transform.position;
        public float Radius => 2.2f;
        public string Prompt => taken ? null : "PRENDI L'OCCHIO DI VETRO";
        public bool Taken => taken;

        public void Build()
        {
            baseY = transform.position.y;
            var white = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.92f, 0.90f, 0.86f) };
            white.SetFloat("_Smoothness", 0.92f);
            white.EnableKeyword("_EMISSION");
            white.SetColor("_EmissionColor", new Color(0.10f, 0.16f, 0.12f));
            white.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var iris = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.10f, 0.42f, 0.30f) };
            iris.SetFloat("_Smoothness", 0.95f);
            iris.EnableKeyword("_EMISSION");
            iris.SetColor("_EmissionColor", new Color(0.25f, 0.9f, 0.55f) * 1.2f);
            iris.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var pupil = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.black };
            ball = new GameObject("Bulbo").transform;
            ball.SetParent(transform, false);
            Piece(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.16f, white, ball);
            Piece(PrimitiveType.Sphere, new Vector3(0f, 0f, 0.062f), new Vector3(0.085f, 0.085f, 0.04f), iris, ball);
            Piece(PrimitiveType.Sphere, new Vector3(0f, 0f, 0.078f), new Vector3(0.036f, 0.036f, 0.016f), pupil, ball);
            var col = gameObject.AddComponent<SphereCollider>();
            col.radius = 0.11f;
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(0.4f, 1f, 0.6f); glow.range = 1.8f; glow.intensity = 0.7f; glow.shadows = LightShadows.None;
            Interact.Add(this);
        }

        static void Piece(PrimitiveType t, Vector3 pos, Vector3 size, Material m, Transform parent)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        public void Use() => Take();
        public void Shot(float dmg, Vector3 point, Vector3 dir) => Take();

        void Take()
        {
            if (taken) return;
            taken = true;
            Fx.EnemyImpact(transform.position, Vector3.up, new Color(0.4f, 1f, 0.6f));
            Interact.Remove(this);
            owner?.Collect(this);
            Destroy(gameObject);
        }

        void Update()
        {
            if (taken) return;
            var cam = GameManager.I != null && GameManager.I.player != null ? GameManager.I.player.cam : null;
            float t = Time.time + transform.GetSiblingIndex() * 1.7f;
            transform.position = new Vector3(transform.position.x, baseY + Mathf.Sin(t * 1.3f) * 0.04f, transform.position.z);
            if (cam == null) return;
            Vector3 to = cam.position - transform.position;
            float d = to.magnitude;
            // da vicino si gira a guardarti; da lontano vaga
            Quaternion want = d < 9f ? Quaternion.LookRotation(to) : Quaternion.Euler(Mathf.Sin(t * 0.4f) * 25f, t * 18f, 0f);
            ball.rotation = Quaternion.Slerp(ball.rotation, want, 1f - Mathf.Exp(-3f * Time.deltaTime));
            if (glow != null) glow.intensity = 0.55f + Mathf.Sin(t * 2.1f) * 0.2f;
            if (breath == null && d < 14f)
            {
                breath = Sfx.Loop(gameObject, SoundBank.VigilBreath, 0.22f, 0.8f, 7f, 1.3f);
            }
        }

        void OnDestroy() => Interact.Remove(this);
    }
}
