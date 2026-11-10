using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Animazione a codice dell'occhio volante (nessuna clip): il globo segue lo sguardo con piccole saccadi,
    /// le palpebre sbattono e si stringono, nervo e tentacoli ondeggiano e restano indietro rispetto al moto.
    /// Tutto in coordinate del mondo (la direzione di ogni osso si ricava dall'osso figlio), cosi' non dipende da come
    /// l'FBX ha orientato gli assi locali. Scheletro e mesh: modelli/occhio/genera_occhio.py.</summary>
    public class EyeRig : MonoBehaviour
    {
        class Chain
        {
            public Transform[] bones;
            public Quaternion[] rest;
            public Vector3[] along;      // direzione dell'osso verso il successivo, nel suo sistema locale
            public float phase, freq, amp, drag;
        }

        /// <summary>Bersaglio dello sguardo (la testa del giocatore).</summary>
        public Transform lookAt;

        const float LidUpMax = 44f, LidDownMax = 34f;     // gradi a palpebra chiusa (come nelle anteprime di Blender)

        Transform globe, lidUp, lidDown, tip;
        Quaternion globeRest, upRest, downRest;
        Vector3 globeAlong;
        readonly List<Chain> chains = new List<Chain>();
        readonly List<Renderer> irisRenderers = new List<Renderer>();
        MaterialPropertyBlock mpb;
        bool inited;

        Vector3 lastPos, vel;
        float open = 1f, openTarget = 1f;
        float blinkT = 2.5f, blinkPhase = -1f;
        float hitT, stareT, limp;
        Vector3 sacc, saccTarget;
        float saccT;
        float clock;
        public bool dead;
        /// <summary>Moltiplica il bagliore dell'iride: piu' forte quando il prete e' allertato.</summary>
        public float glowBoost = 1f;

        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        public void Init()
        {
            if (inited) return;
            inited = true;
            var all = GetComponentsInChildren<Transform>(true);
            Transform F(string n) { foreach (var t in all) if (t.name == n) return t; return null; }
            globe = F("occhio"); lidUp = F("palpebra_su"); lidDown = F("palpebra_giu"); tip = F("occhio_punta");
            if (globe == null || lidUp == null || lidDown == null || tip == null)
            {
                Debug.LogError("[EyeRig] ossa mancanti: occhio/palpebre");
                enabled = false;
                return;
            }
            globeRest = globe.localRotation; upRest = lidUp.localRotation; downRest = lidDown.localRotation;
            globeAlong = globe.InverseTransformPoint(tip.position).normalized;

            var rnd = new System.Random(Random.Range(1, int.MaxValue));
            var names = new List<string> { "nervo" };
            for (int i = 0; i < 8; i++) names.Add("tent" + i);
            foreach (var prefix in names)
            {
                var list = new List<Transform>();
                for (int j = 0; ; j++)
                {
                    var b = F(prefix + "_" + j);
                    if (b == null) break;
                    list.Add(b);
                }
                if (list.Count < 2) continue;
                var c = new Chain
                {
                    bones = list.ToArray(),
                    rest = new Quaternion[list.Count],
                    along = new Vector3[list.Count],
                };
                for (int j = 0; j < list.Count; j++)
                {
                    c.rest[j] = list[j].localRotation;
                    if (j < list.Count - 1) c.along[j] = list[j].InverseTransformPoint(list[j + 1].position).normalized;
                }
                int n = list.Count;
                c.along[n - 1] = Quaternion.Inverse(c.rest[n - 1]) * c.along[n - 2];
                bool nerve = prefix == "nervo";
                c.phase = (float)rnd.NextDouble() * 6.28f;
                c.freq = nerve ? Random.Range(1.5f, 2.0f) : Random.Range(2.6f, 3.8f);
                c.amp = nerve ? 6.5f : 13f;
                c.drag = nerve ? 0.45f : 0.75f;
                chains.Add(c);
            }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.name == "globo" || r.name.StartsWith("occhietto")) irisRenderers.Add(r);
            mpb = new MaterialPropertyBlock();
            lastPos = transform.position;
        }

        void Awake() { Init(); }

        void LateUpdate() { Tick(Time.deltaTime); }

        // ---- comandi dal nemico ----
        /// <summary>Colpito: strizza gli occhi e frusta i tentacoli.</summary>
        public void Hit() { hitT = 0.22f; }
        /// <summary>Sguardo fisso prima dell'attacco: spalanca le palpebre, l'iride si accende, i tentacoli si agitano.</summary>
        public void Stare(float seconds) { stareT = seconds; }
        /// <summary>Morto: palpebre calate, iride spenta, tentacoli flosci.</summary>
        public void Die() { dead = true; }

        /// <summary>Aggiornamento di un passo. In edit-mode (foto) si chiama a mano.</summary>
        public void Tick(float dt)
        {
            if (!inited) Init();
            if (!enabled || dt <= 0f) return;
            clock += dt;
            Vector3 v = (transform.position - lastPos) / dt;
            lastPos = transform.position;
            vel = Vector3.Lerp(vel, v, 1f - Mathf.Exp(-6f * dt));
            hitT = Mathf.Max(0f, hitT - dt);
            stareT = Mathf.Max(0f, stareT - dt);
            if (dead) limp = Mathf.Min(1f, limp + dt * 1.6f);

            AimGlobe(dt);
            Lids(dt);
            Chains(dt);
            Glow();
        }

        void AimGlobe(float dt)
        {
            Quaternion restWorld = globe.parent.rotation * globeRest;
            Vector3 restDir = restWorld * globeAlong;
            Vector3 want = restDir;
            if (lookAt != null && !dead)
            {
                Vector3 to = (lookAt.position - globe.position).normalized;
                want = Vector3.RotateTowards(restDir, to, 38f * Mathf.Deg2Rad, 0f);
            }
            saccT -= dt;
            if (saccT <= 0f)
            {
                saccT = Random.Range(0.35f, 1.7f);
                saccTarget = Random.insideUnitSphere * (stareT > 0f ? 0.015f : 0.07f);
            }
            sacc = Vector3.Lerp(sacc, saccTarget, 1f - Mathf.Exp(-16f * dt));
            want = (want + sacc).normalized;
            Quaternion target = Quaternion.FromToRotation(restDir, want) * restWorld;
            globe.rotation = Quaternion.Slerp(globe.rotation, target, 1f - Mathf.Exp(-14f * dt));
        }

        void Lids(float dt)
        {
            // bersaglio: 1 = riposo, 0 = chiuso, > 1 spalancato
            float t = 1f;
            if (dead) t = 0.12f;
            else if (hitT > 0f) t = 0.25f;
            else if (stareT > 0f) t = 1.38f;
            open = Mathf.Lerp(open, t, 1f - Mathf.Exp(-(t < open ? 22f : 12f) * dt));
            // battito, ogni tanto doppio
            blinkT -= dt;
            if (blinkT <= 0f && blinkPhase < 0f && !dead && stareT <= 0f)
            {
                blinkPhase = 0f;
                blinkT = Random.value < 0.25f ? 0.28f : Random.Range(2.4f, 6.5f);
            }
            float b = 0f;
            if (blinkPhase >= 0f)
            {
                blinkPhase += dt / 0.17f;
                b = Mathf.Sin(Mathf.PI * Mathf.Clamp01(blinkPhase));
                if (blinkPhase >= 1f) blinkPhase = -1f;
            }
            float eff = Mathf.Lerp(open, 0.02f, b);
            Vector3 axis = transform.right;
            lidUp.rotation = Quaternion.AngleAxis((1f - eff) * LidUpMax, axis) * (lidUp.parent.rotation * upRest);
            lidDown.rotation = Quaternion.AngleAxis(-(1f - eff) * LidDownMax, axis) * (lidDown.parent.rotation * downRest);
        }

        void Chains(float dt)
        {
            float speed = vel.magnitude;
            Vector3 vdir = speed > 0.05f ? vel / speed : Vector3.zero;
            float dragK = Mathf.Clamp01(speed / 3.5f);
            float gain = (1f + (hitT > 0f ? 1.6f : 0f) + (stareT > 0f ? 1.1f : 0f)) * (1f - limp);
            float fgain = stareT > 0f || hitT > 0f ? 1.8f : 1f;
            foreach (var c in chains)
            {
                Quaternion prevRot = c.bones[0].parent.rotation;
                for (int j = 0; j < c.bones.Length; j++)
                {
                    var b = c.bones[j];
                    Quaternion restWorld = prevRot * c.rest[j];
                    Vector3 restDir = restWorld * c.along[j];
                    float u = (j + 1f) / c.bones.Length;
                    Vector3 d = restDir;
                    if (dragK > 0.001f) d = Vector3.Slerp(d, -vdir, dragK * c.drag * u);
                    if (limp > 0f) d = Vector3.Slerp(d, Vector3.down, limp * (0.35f + 0.65f * u));
                    float a = clock * c.freq * fgain + c.phase - j * 0.85f;
                    Vector3 s1 = Vector3.Cross(d, Vector3.up);
                    if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.Cross(d, Vector3.right);
                    s1.Normalize();
                    Vector3 s2 = Vector3.Cross(d, s1);
                    float amp = c.amp * (0.25f + u) * gain;
                    d = Quaternion.AngleAxis(Mathf.Sin(a) * amp, s1) * Quaternion.AngleAxis(Mathf.Cos(a * 0.8f + 1.3f) * amp * 0.7f, s2) * d;
                    b.rotation = Quaternion.FromToRotation(restDir, d) * restWorld;
                    prevRot = b.rotation;
                }
            }
        }

        void Glow()
        {
            // l'iride brilla in ambra (si legge nel buio); piu' forte quando fissa, spenta da morto
            float k = dead ? 0.05f : 1f + (stareT > 0f ? 2.2f : 0f) + (hitT > 0f ? 1.2f : 0f);
            k *= 1f + 0.12f * Mathf.Sin(clock * 3.1f);
            if (!dead) k *= glowBoost;
            var col = Color.white * (0.7f * k);
            foreach (var r in irisRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(Emission, col);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
