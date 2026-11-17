using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Animazione a codice della medusa volante (nessuna clip): la campana pulsa a settori (contrazione veloce, rilascio lento),
    /// i tentacoli, le braccia orali, i drappi e i fili sono funi simulate (Verlet con una molla verso la posa) che restano indietro
    /// rispetto al moto e frustano nell'affondo, la bocca si apre a petali, gli organi brillano di verde acido al ritmo del battito.
    /// L'attacco segue FlightElapsed (Enemy.FlyingMelee): raccolta 0-0,55 s, affondo fino a 0,71 s, recupero fino a 1,13 s.
    /// Tutto si ricava dalle posizioni delle ossa (nessuna dipendenza dagli assi locali dell'FBX). Scheletro e mesh:
    /// modelli/medusa/genera_medusa.py. Gli effetti dei colpi sono in JellyFx.</summary>
    public sealed class JellyRig : MonoBehaviour
    {
        // ---------------------------------------------------------------- tipi
        enum RopeKind { Tentacle, Arm, Drape, Thread }

        /// <summary>Una catena di ossa simulata come fune: n ossa, n+1 punti (l'ultimo e' la punta virtuale).</summary>
        sealed class Rope
        {
            public RopeKind kind;
            public Transform[] bones;
            public Quaternion[] rest;       // rotazione locale a riposo di ogni osso
            public Vector3[] along;         // direzione verso il giunto successivo, nel sistema locale dell'osso
            public float[] len;             // lunghezza di ogni segmento
            public Vector3[] p, prev, want; // punti simulati, punti del passo prima, forma desiderata
            public float phase, freq, sway; // ondeggiamento proprio
            public float k0, k1, damp;      // rigidezza alla radice e in punta, resistenza del mezzo
            public Vector3 radial;          // verso fuori dalla bocca (braccia) o dalla campana, sistema della creatura
            public float floorY = float.NegativeInfinity;
            public float floorT;
            public bool primed;
        }

        /// <summary>Un settore della campana (tre ossa lungo il meridiano) o un petalo di labbro.</summary>
        sealed class Petal
        {
            public Transform[] bones;
            public Quaternion[] rest;
            public Vector3 axis;            // asse di rotazione (tangente), nel sistema dell'osso di riferimento
            public float sign;              // +1: l'angolo positivo chiude la campana / apre il labbro
            public float lag;               // sfasamento nel battito
        }

        // ---------------------------------------------------------------- stato
        /// <summary>Morta: campana ammosciata, funi flosce, organi che si spengono.</summary>
        public bool dead;
        /// <summary>Moltiplica il bagliore degli organi (piu' forte quando e' allertata).</summary>
        public float glowBoost = 1f;

        Transform body, mouth, neck0, neck1;
        Vector3 bodyRestPos;
        Quaternion bodyRestRot, mouthRestRot;
        Vector3 bodyAxisRight;              // asse del beccheggio, nel sistema dell'osso corpo
        float pitchSign = 1f;
        readonly List<Petal> bell = new List<Petal>();
        readonly List<Petal> lips = new List<Petal>();
        readonly List<Rope> ropes = new List<Rope>();
        readonly List<Renderer> glowGel = new List<Renderer>(), glowOrgans = new List<Renderer>(), glowInner = new List<Renderer>();
        Light organLight;
        float lightBase;
        MaterialPropertyBlock mpb;
        bool inited;

        // pilotaggio dal nemico
        float phase;
        float speedIn;
        bool hunting;
        float flightTime = -1f;

        // dinamica
        float clock, pulse, lastStep = 1f / 60f;
        Vector3 lastPos, vel;
        float lean, roll;
        float hitT, spasm, limp, deadT;
        float glow = 1f;
        bool inFlight, strikeFired;
        float gatherFxT, dripT, moteT;
        int frame, skipOffset;
        float accum;
        Vector3 impulseDir;

        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        static readonly float[] BellAngles = { 7f, 10f, 14f };       // gradi per livello a contrazione piena (idle)

        /// <summary>Distanza dal punto di vista piu' vicino (la aggiorna Enemy.Budget); 0 se non c'e' un nemico.</summary>
        Enemy enemy;

        // ---------------------------------------------------------------- avvio
        public void Init()
        {
            if (inited) return;
            inited = true;
            var all = GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>(all.Length);
            foreach (var t in all) byName[t.name] = t;
            Transform F(string n) => byName.TryGetValue(n, out var t) ? t : null;
            body = F("corpo"); mouth = F("bocca"); neck0 = F("collo_0"); neck1 = F("collo_1");
            if (body == null || mouth == null)
            {
                Debug.LogError("[JellyRig] ossa mancanti: corpo/bocca");
                enabled = false;
                return;
            }
            enemy = GetComponent<Enemy>();
            bodyRestPos = body.parent.InverseTransformPoint(body.position);
            bodyRestRot = body.localRotation;
            mouthRestRot = mouth.localRotation;
            bodyAxisRight = Quaternion.Inverse(body.rotation) * transform.right;
            pitchSign = ProbePitchSign();

            // campana: 8 settori da 3 ossa
            for (int i = 0; i < 8; i++)
            {
                var pe = MakePetal(F($"campana_{i}_0"), F($"campana_{i}_1"), F($"campana_{i}_2"), body.position, body, true);
                if (pe == null) continue;
                pe.lag = i * 0.011f;
                bell.Add(pe);
            }
            // labbra: petali di due ossa attorno alla bocca
            for (int i = 0; i < 12; i++)
            {
                var a = F($"labbro_{i}_0");
                if (a == null) continue;
                var pe = MakePetal(a, F($"labbro_{i}_1"), null, mouth.position, mouth, false);
                if (pe != null) lips.Add(pe);
            }
            // funi: tentacoli, braccia orali, drappi del bordo, fili
            var groups = new Dictionary<string, SortedList<int, Transform>>();
            foreach (var t in all)
            {
                int us = t.name.LastIndexOf('_');
                if (us <= 0 || !int.TryParse(t.name.Substring(us + 1), out int idx)) continue;
                string head = t.name.Substring(0, us);
                if (!(head.StartsWith("tent_") || head.StartsWith("braccio_") || head.StartsWith("drappo_") || head.StartsWith("filo_"))) continue;
                if (!groups.TryGetValue(head, out var list)) groups[head] = list = new SortedList<int, Transform>();
                list[idx] = t;
            }
            var rnd = new System.Random(Random.Range(1, int.MaxValue));
            foreach (var kv in groups)
            {
                var rope = MakeRope(kv.Key, kv.Value, rnd);
                if (rope != null) ropes.Add(rope);
            }
            // luci e bagliori
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "m_gel") glowGel.Add(r);
                else if (r.name == "m_luce") glowOrgans.Add(r);
                else if (r.name == "m_interno") glowInner.Add(r);
            }
            organLight = GetComponentInChildren<Light>(true);
            if (organLight != null) lightBase = organLight.intensity;
            mpb = new MaterialPropertyBlock();
            lastPos = transform.position;
            phase = Random.value * 10f;
            pulse = Random.value;
            skipOffset = Random.Range(0, 4);                // chi e' lontano si aggiorna a fotogrammi diversi dagli altri
        }

        void Awake() { Init(); }

        /// <summary>Beccheggio positivo = la bocca va in avanti (il segno dipende da come l'FBX ha orientato l'osso).</summary>
        float ProbePitchSign()
        {
            Vector3 m0 = mouth.position;
            Quaternion saved = body.rotation;
            body.rotation = Quaternion.AngleAxis(10f, transform.right) * saved;
            float dz = Vector3.Dot(mouth.position - m0, transform.forward);
            body.rotation = saved;
            return dz >= 0f ? 1f : -1f;
        }

        /// <summary>Un petalo con asse tangente e verso di apertura ricavati dalle posizioni delle ossa.</summary>
        Petal MakePetal(Transform a, Transform b, Transform c, Vector3 center, Transform axisFrame, bool closing)
        {
            if (a == null || b == null) return null;
            var bones = c != null ? new[] { a, b, c } : new[] { a, b };
            Transform tip = c != null ? c : b;
            var pe = new Petal { bones = bones, rest = new Quaternion[bones.Length] };
            for (int k = 0; k < bones.Length; k++) pe.rest[k] = bones[k].localRotation;
            Vector3 rel = tip.position - center;
            rel -= transform.up * Vector3.Dot(rel, transform.up);
            if (rel.sqrMagnitude < 1e-6f) return null;
            Vector3 axisWorld = Vector3.Cross(transform.up, rel.normalized).normalized;
            pe.axis = Quaternion.Inverse(axisFrame.rotation) * axisWorld;
            // verso: l'angolo positivo avvicina la punta all'asse? (campana: si', cosi' si chiude; labbro: no, cosi' si apre)
            float r0 = Horizontal(tip.position - center);
            Quaternion saved = a.rotation;
            a.rotation = Quaternion.AngleAxis(6f, axisWorld) * saved;
            float r1 = Horizontal(tip.position - center);
            a.rotation = saved;
            bool inward = r1 < r0;
            pe.sign = (inward == closing) ? 1f : -1f;
            return pe;
        }

        float Horizontal(Vector3 v) { v -= transform.up * Vector3.Dot(v, transform.up); return v.magnitude; }

        Rope MakeRope(string head, SortedList<int, Transform> list, System.Random rnd)
        {
            int n = list.Count;
            if (n < 2) return null;
            var bones = new Transform[n];
            list.Values.CopyTo(bones, 0);
            var kind = head.StartsWith("tent_") ? RopeKind.Tentacle : head.StartsWith("braccio_") ? RopeKind.Arm : head.StartsWith("drappo_") ? RopeKind.Drape : RopeKind.Thread;
            var r = new Rope
            {
                kind = kind, bones = bones, rest = new Quaternion[n], along = new Vector3[n], len = new float[n],
                p = new Vector3[n + 1], prev = new Vector3[n + 1], want = new Vector3[n + 1],
            };
            for (int j = 0; j < n; j++) r.rest[j] = bones[j].localRotation;
            for (int j = 0; j < n - 1; j++)
            {
                Vector3 d = bones[j + 1].position - bones[j].position;
                r.len[j] = d.magnitude;
                r.along[j] = bones[j].InverseTransformDirection(d).normalized;
            }
            r.len[n - 1] = r.len[n - 2];
            r.along[n - 1] = Quaternion.Inverse(r.rest[n - 1]) * r.along[n - 2];   // l'ultimo osso non ha figli: stessa direzione del precedente
            r.phase = (float)rnd.NextDouble() * 6.28f;
            switch (kind)
            {
                case RopeKind.Tentacle: r.freq = Random.Range(0.8f, 1.5f); r.sway = 9f; r.k0 = 15f; r.k1 = 2.4f; r.damp = 1.1f; break;
                case RopeKind.Arm: r.freq = Random.Range(1.3f, 2.1f); r.sway = 8f; r.k0 = 18f; r.k1 = 4.5f; r.damp = 1.5f; break;
                case RopeKind.Drape: r.freq = Random.Range(1.0f, 1.9f); r.sway = 11f; r.k0 = 10f; r.k1 = 3.2f; r.damp = 1.6f; break;
                default: r.freq = Random.Range(1.4f, 2.6f); r.sway = 14f; r.k0 = 6f; r.k1 = 1.6f; r.damp = 1.8f; break;
            }
            Vector3 rel = bones[0].position - (kind == RopeKind.Arm ? mouth.position : body.position);
            rel -= transform.up * Vector3.Dot(rel, transform.up);
            r.radial = transform.InverseTransformDirection(rel.sqrMagnitude > 1e-6f ? rel.normalized : transform.forward);
            return r;
        }

        // ---------------------------------------------------------------- comandi dal nemico
        /// <summary>Pilotaggio di ogni fotogramma (come AlienRig.Animate): fase individuale, velocita', caccia, e il tempo
        /// dell'affondo (-1 se non sta attaccando).</summary>
        public void Drive(float p, float speed, bool isHunting, bool striking, float flight)
        {
            phase = p;
            speedIn = speed;
            hunting = isHunting;
            flightTime = flight;
        }

        /// <summary>Colpita: spasmo della campana, bocca spalancata, organi che balenano e funi sbattute via dal colpo.</summary>
        public void Hit(Vector3 point)
        {
            if (!inited) Init();
            if (dead) return;
            hitT = 0.32f;
            spasm = 1f;
            Vector3 away = body != null ? body.position + transform.up * 0.15f - point : Vector3.zero;
            if (away.sqrMagnitude < 1e-4f) away = -transform.forward;
            away.Normalize();
            Kick(away * 2.6f, 0.4f);
            JellyFx.Hit(point, away);
        }

        /// <summary>Morta: la campana si affloscia, le funi diventano pesanti, gli organi si spengono; scoppio di muco.</summary>
        public void Die()
        {
            if (!inited) Init();
            if (dead) return;
            dead = true;
            deadT = 0f;
            glow = Mathf.Min(glow, 1.5f);   // lo spasmo dell'ultimo colpo non deve gonfiare l'interno fino al bianco: da qui si spegne
            Kick(Vector3.down * 0.8f, 0.5f);
            JellyFx.Death(body != null ? body.position + transform.up * 0.2f : transform.position, MouthPoint);
        }

        /// <summary>Il punto davanti alla bocca (per gli effetti).</summary>
        public Vector3 MouthPoint => mouth != null ? mouth.position - transform.up * 0.06f : transform.position;

        /// <summary>Centro della campana (per gli effetti e le luci).</summary>
        public Vector3 BellCenter => body != null ? body.position + transform.up * 0.2f : transform.position;

        // ---------------------------------------------------------------- per le prove (JellyTest) e le foto
        public int BellCount => bell.Count;
        public int LipCount => lips.Count;
        public int RopeCount => ropes.Count;
        /// <summary>Livello del bagliore degli organi (1 = a riposo).</summary>
        public float GlowLevel => glow;
        /// <summary>Tempo dell'attacco dall'inizio (0..1,13 s), -1 se non attacca: lo stesso valore che Enemy passa a Drive.</summary>
        public float FlightTime => flightTime;

        /// <summary>Tutti i punti delle funi sono numeri finiti (nessuna esplosione numerica).</summary>
        public bool AllFinite()
        {
            foreach (var r in ropes)
                foreach (var p in r.p)
                    if (float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z)) return false;
            return true;
        }

        /// <summary>Media delle punte dei tentacoli, nel sistema della creatura (x destra, y su, z avanti) e relativa al centro della campana.</summary>
        public Vector3 TentacleTipsLocal()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            Vector3 c = transform.InverseTransformPoint(BellCenter);
            foreach (var r in ropes)
            {
                if (r.kind != RopeKind.Tentacle || !r.primed) continue;
                sum += transform.InverseTransformPoint(r.p[r.bones.Length]) - c;
                n++;
            }
            return n > 0 ? sum / n : Vector3.zero;
        }

        /// <summary>Quota piu' bassa tra le punte dei tentacoli.</summary>
        public float LowestTipY()
        {
            float y = float.MaxValue;
            foreach (var r in ropes)
                if (r.kind == RopeKind.Tentacle && r.primed) y = Mathf.Min(y, r.p[r.bones.Length].y);
            return y;
        }

        /// <summary>Distanza orizzontale (nel sistema della creatura) del bordo del settore 0 dall'asse: misura la contrazione della campana.</summary>
        public float RimRadius()
        {
            if (bell.Count == 0) return 0f;
            Vector3 d = transform.InverseTransformPoint(bell[0].bones[bell[0].bones.Length - 1].position) - transform.InverseTransformPoint(body.position);
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>Carica l'asset in anticipo: il costo resta nel caricamento del piano, non nel primo spawn.</summary>
        public static void Warmup() { Resources.Load<GameObject>("Medusa"); }

        /// <summary>Dà una velocita' a tutte le funi (piu' forte in punta): schiocco dell'affondo, colpo ricevuto.</summary>
        void Kick(Vector3 v, float tipBias)
        {
            foreach (var r in ropes)
            {
                if (!r.primed) continue;
                int n = r.bones.Length;
                float w = r.kind == RopeKind.Arm ? 0.7f : r.kind == RopeKind.Tentacle ? 1f : 0.55f;
                for (int j = 1; j <= n; j++)
                {
                    float u = (float)j / n;
                    r.prev[j] -= v * w * (1f - tipBias + tipBias * u * 2f) * lastStep;
                }
            }
        }

        // ---------------------------------------------------------------- ciclo
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            frame++;
            if (enemy == null) enemy = GetComponent<Enemy>();         // Spawn lo aggiunge dopo aver istanziato il prefab
            float dist = enemy != null ? enemy.LastViewDistance : 0f;
            accum += dt;
            int every = dist > 40f ? 4 : dist > 24f ? 2 : 1;
            if (every > 1 && (frame + skipOffset) % every != 0) return;
            float step = Mathf.Min(accum, 0.08f);
            accum = 0f;
            Tick(step);
        }

        /// <summary>Un passo di animazione (in edit-mode, per le foto, lo si chiama a mano).</summary>
        public void Tick(float dt)
        {
            if (!inited) Init();
            if (!enabled || dt <= 0f) return;
            clock += dt;
            Vector3 pos = transform.position;
            bool teleported = (pos - lastPos).sqrMagnitude > 2.5f * 2.5f;
            vel = Vector3.Lerp(vel, (pos - lastPos) / dt, 1f - Mathf.Exp(-6f * dt));
            lastPos = pos;
            hitT = Mathf.Max(0f, hitT - dt);
            spasm = Mathf.Max(0f, spasm - dt * 3.2f);
            if (dead) { deadT += dt; limp = Mathf.Min(1f, limp + dt * 1.2f); }

            // ---- tempi dell'attacco
            float gather = 0f, thrust = 0f;
            if (!dead && flightTime >= 0f)
            {
                float W = Enemy.FlightWindup, C = Enemy.FlightContact, D = Enemy.FlightDuration;
                float t = flightTime;
                if (!inFlight) { inFlight = true; strikeFired = false; gatherFxT = 0f; }
                gather = t < W ? Ease(t / W) : t < C ? 1f - Ease((t - W) / (C - W)) : 0f;
                thrust = t < W ? 0f : t < C ? Ease((t - W) / (C - W)) : Mathf.Pow(1f - Ease(Mathf.Clamp01((t - C) / (D - C))), 0.8f);
                if (t < W)
                {
                    gatherFxT -= dt;
                    if (gatherFxT <= 0f) { gatherFxT = 0.02f; JellyFx.Gather(MouthPoint, transform.up, Ease(t / W)); }
                }
                else if (!strikeFired)
                {
                    strikeFired = true;
                    Vector3 dir = transform.forward;
                    JellyFx.Lunge(MouthPoint, dir);
                    Kick(dir * 5.5f - transform.up * 1.2f, 0.85f);        // le funi frustano in avanti
                }
            }
            else inFlight = false;

            // ---- battito
            float rate = 0.5f + (hunting ? 0.3f : 0f) + Mathf.Clamp01(speedIn / 3f) * 0.18f;
            pulse += dt * rate * (1f + 0.5f * spasm);
            float idle = PulseCurve(pulse + 0.0f);
            float attack = -0.5f * gather + 1.25f * Mathf.Pow(thrust, 0.8f);
            float blend = Mathf.Max(gather, thrust);
            float contract = Mathf.Lerp(idle, attack, blend) + 0.55f * spasm;
            if (dead) contract = Mathf.Lerp(contract, 0.55f + 0.2f * limp, limp);

            // ---- assetto
            float leanT = dead ? 0f : hunting ? 8f + Mathf.Clamp(speedIn, 0f, 3f) * 3.5f : 2f + speedIn * 2f;
            lean = Mathf.Lerp(lean, leanT, 1f - Mathf.Exp(-3f * dt));
            float pitch = lean - 26f * gather + 64f * thrust;
            Vector3 lv = transform.InverseTransformDirection(vel);
            float rollT = -Mathf.Clamp(lv.x * 3.5f, -9f, 9f) + 2.2f * Mathf.Sin(clock * 0.7f + phase);
            roll = Mathf.Lerp(roll, rollT, 1f - Mathf.Exp(-3f * dt));
            float tremble = gather * 1.8f * Mathf.Sin(clock * 47f);
            float bob = dead ? 0f : 0.055f * idle * (1f - blend);

            // ---- osso corpo
            Quaternion bodyRest = body.parent.rotation * bodyRestRot;
            Quaternion q = Quaternion.AngleAxis(pitch * pitchSign, transform.right) * Quaternion.AngleAxis(roll + tremble, transform.forward);
            body.rotation = q * bodyRest;
            body.position = body.parent.TransformPoint(bodyRestPos) + transform.up * bob;

            // ---- bocca e collo
            float open = dead ? 0.6f * limp : 0.16f + 0.10f * Mathf.Sin(clock * 1.3f + phase) + 0.35f * gather + 0.5f * thrust + 0.5f * spasm + 0.12f * contract;
            if (!dead && hunting) open += 0.12f * (0.5f + 0.5f * Mathf.Sin(clock * 3.3f));
            float wag = dead ? 0f : 5f * Mathf.Sin(clock * 2.1f + phase) * (hunting ? 1f : 0.4f);
            mouth.rotation = Quaternion.AngleAxis(wag, transform.right) * (mouth.parent.rotation * mouthRestRot);
            foreach (var l in lips) ApplyPetal(l, open * 30f, 0.72f, mouth, 0f);

            // ---- campana
            foreach (var s in bell)
            {
                float c = contract;
                if (!dead && blend < 0.2f) c = Mathf.Lerp(PulseCurve(pulse - s.lag), c, blend / 0.2f) + 0.55f * spasm;
                ApplyPetal(s, c * 1f, 1f, body, 1f);
            }

            // ---- funi
            if (teleported) foreach (var r in ropes) r.primed = false;
            Vector3 up = transform.up, fwd = transform.forward;
            Vector3 gatherTent = (up * 0.85f - fwd * 0.45f).normalized;
            Vector3 thrustDir = (fwd * 0.95f - up * 0.25f).normalized;
            float stiffScale = 1f - limp * 0.93f;
            Vector3 gravity = dead ? Vector3.down * 7f : Vector3.down * 1.3f;
            float ratio = Mathf.Clamp(dt / Mathf.Max(lastStep, 1e-3f), 0.4f, 2.5f);
            lastStep = dt;
            foreach (var r in ropes)
            {
                // il pavimento sotto la punta (cinque volte al secondo per tentacolo): le funi non lo attraversano
                if (r.kind == RopeKind.Tentacle && Time.time >= r.floorT)
                {
                    r.floorT = Time.time + Random.Range(0.18f, 0.32f);
                    r.floorY = float.NegativeInfinity;
                    if (r.primed)
                    {
                        Vector3 from = r.p[r.bones.Length] + Vector3.up * 0.7f;
                        if (Physics.Raycast(from, Vector3.down, out var hit, 4.5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) r.floorY = hit.point.y + 0.03f;
                    }
                }
                StepRope(r, dt, ratio, gather, thrust, gatherTent, thrustDir, limp, stiffScale, gravity);
            }

            // ---- bagliori
            Glow(dt, gather, thrust, contract);
            Ambient(dt);
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Contrazione veloce (potenza), rilascio lento che decelera.</summary>
        static float PulseCurve(float x)
        {
            x -= Mathf.Floor(x);
            const float a = 0.26f;
            if (x < a) { float t = x / a; return t * t * (3f - 2f * t); }
            float r = (x - a) / (1f - a);
            return Mathf.Pow(1f - r, 1.9f);
        }

        /// <summary>Ruota il petalo (3 ossa del meridiano o 2 del labbro) attorno al suo asse tangente: l'angolo si somma lungo la catena.</summary>
        void ApplyPetal(Petal s, float amount, float scale, Transform frame, float levelMode)
        {
            Vector3 axisWorld = frame.rotation * s.axis;
            Quaternion pr = s.bones[0].parent.rotation;
            for (int k = 0; k < s.bones.Length; k++)
            {
                float deg = levelMode > 0.5f ? BellAngles[Mathf.Min(k, 2)] * amount : amount * (k == 0 ? 1f : 0.65f) * scale;
                Quaternion restWorld = pr * s.rest[k];
                s.bones[k].rotation = Quaternion.AngleAxis(deg * s.sign, axisWorld) * restWorld;
                pr = s.bones[k].rotation;
            }
        }

        // ---------------------------------------------------------------- funi
        void StepRope(Rope r, float dt, float ratio, float gather, float thrust, Vector3 gatherTent, Vector3 thrustDir, float limpK, float stiffScale, Vector3 gravity)
        {
            int n = r.bones.Length;
            Quaternion parentRot = r.bones[0].parent.rotation;
            Vector3 rootPos = r.bones[0].position;
            // forma desiderata: la posa a riposo data l'orientazione del genitore, piu' ondeggiamento, raccolta e affondo
            Vector3 radialW = transform.TransformDirection(r.radial);
            Vector3 gDir = r.kind == RopeKind.Tentacle ? gatherTent
                : r.kind == RopeKind.Arm ? (radialW * 0.75f + transform.up * 0.45f).normalized
                : (transform.up * 0.6f + radialW * 0.4f).normalized;
            float gK = r.kind == RopeKind.Tentacle ? 0.85f : r.kind == RopeKind.Arm ? 0.8f : r.kind == RopeKind.Drape ? 0.5f : 0.7f;
            float tK = r.kind == RopeKind.Tentacle ? 0.8f : r.kind == RopeKind.Arm ? 0.95f : r.kind == RopeKind.Drape ? 0.35f : 0.5f;
            r.want[0] = rootPos;
            Quaternion rw = parentRot;
            for (int j = 0; j < n; j++)
            {
                Quaternion restWorld = rw * r.rest[j];
                Vector3 restDir = restWorld * r.along[j];
                float u = (j + 1f) / n;
                Vector3 d = restDir;
                // ondeggiamento: due onde sfasate lungo la fune
                float a = clock * r.freq * (1f + 0.9f * gather) + r.phase - j * 0.85f;
                Vector3 s1 = Vector3.Cross(d, Vector3.up);
                if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.Cross(d, Vector3.right);
                s1.Normalize();
                Vector3 s2 = Vector3.Cross(d, s1);
                float amp = r.sway * (0.25f + u) * (1f - limpK) * (1f + 0.8f * gather + 0.5f * spasm);
                d = Quaternion.AngleAxis(Mathf.Sin(a) * amp, s1) * Quaternion.AngleAxis(Mathf.Cos(a * 0.8f + 1.3f) * amp * 0.7f, s2) * d;
                if (gather > 0f) d = Vector3.Slerp(d, gDir, Mathf.Clamp01(gather * (0.12f + 0.88f * u) * gK));
                if (thrust > 0f) d = Vector3.Slerp(d, thrustDir, Mathf.Clamp01(thrust * (0.10f + 0.90f * u) * tK));
                if (limpK > 0f) d = Vector3.Slerp(d, Vector3.down, limpK * (0.3f + 0.7f * u));
                r.want[j + 1] = r.want[j] + d * r.len[j];
                rw = Quaternion.FromToRotation(restDir, d) * restWorld;
            }
            if (!r.primed)
            {
                for (int j = 0; j <= n; j++) { r.p[j] = r.want[j]; r.prev[j] = r.want[j]; }
                r.primed = true;
            }
            // integrazione (Verlet) con molla verso la forma desiderata, piu' forte alla radice
            float damp = Mathf.Exp(-r.damp * dt);
            r.p[0] = r.want[0];
            r.prev[0] = r.want[0];
            for (int j = 1; j <= n; j++)
            {
                float u = (float)j / n;
                float k = Mathf.Lerp(r.k0, r.k1, u) * stiffScale;
                Vector3 cur = r.p[j];
                Vector3 v = (cur - r.prev[j]) * (damp * Mathf.Min(1f, ratio));
                r.prev[j] = cur;
                cur += v + gravity * (dt * dt);
                cur += (r.want[j] - cur) * (1f - Mathf.Exp(-k * dt));
                if (cur.y < r.floorY) cur.y = r.floorY;
                r.p[j] = cur;
            }
            // lunghezze: ogni punto resta alla distanza giusta dal precedente
            for (int j = 1; j <= n; j++)
            {
                Vector3 a0 = r.p[j - 1], dir = r.p[j] - a0;
                float l = dir.magnitude;
                dir = l > 1e-5f ? dir / l : (r.want[j] - r.want[j - 1]).normalized;
                Vector3 np = a0 + dir * r.len[j - 1];
                if (np.y < r.floorY) np.y = r.floorY;
                r.p[j] = np;
            }
            // ossa: ogni osso guarda verso il punto successivo
            Quaternion pr = parentRot;
            for (int j = 0; j < n; j++)
            {
                Vector3 d = r.p[j + 1] - r.p[j];
                if (d.sqrMagnitude < 1e-10f) d = r.want[j + 1] - r.want[j];
                d.Normalize();
                Quaternion restWorld = pr * r.rest[j];
                r.bones[j].rotation = Quaternion.FromToRotation(restWorld * r.along[j], d) * restWorld;
                pr = r.bones[j].rotation;
            }
        }

        // ---------------------------------------------------------------- luce
        void Glow(float dt, float gather, float thrust, float contract)
        {
            float target = dead ? 0.04f : 1f + 0.3f * contract + 2.6f * gather + 3.2f * thrust + 1.6f * spasm + (hunting ? 0.35f : 0f);
            target *= dead ? 1f : glowBoost * (1f + 0.10f * Mathf.Sin(clock * 3.1f + phase));
            glow = Mathf.Lerp(glow, target, 1f - Mathf.Exp(-(target > glow ? 14f : dead ? 1.1f : 5f) * dt));
            // organi e canali: verde acido che vira al giallo quando carica. L'emissione si somma intera sopra il gel e passa dal
            // bloom: tenuta bassa e col tetto, altrimenti la campana diventa un blocco bianco (foto di -jellyshots)
            float charge = dead ? 0f : Mathf.Clamp01(gather * 0.7f + thrust * 0.9f + spasm * 0.5f);   // morta resta verde
            Color hot = Color.Lerp(new Color(0.62f, 1f, 0.16f), new Color(1f, 0.95f, 0.45f), charge);
            Color gelTint = Color.Lerp(new Color(0.60f, 1f, 0.45f), new Color(1f, 0.95f, 0.60f), charge);
            float cap = Mathf.Min(glow, 2.8f);
            SetEmission(glowOrgans, 0, hot * (1.8f * cap));
            SetEmission(glowInner, 0, hot * (1.1f * cap));
            SetEmission(glowGel, 0, gelTint * (0.30f * Mathf.Min(glow, 2.2f)));
            SetEmission(glowGel, 1, gelTint * (0.55f * Mathf.Min(glow, 2.2f)));
            if (organLight != null)
            {
                organLight.color = Color.Lerp(new Color(0.55f, 1f, 0.2f), new Color(1f, 0.95f, 0.5f), Mathf.Clamp01(gather + thrust));
                organLight.intensity = lightBase * (0.35f + 0.65f * Mathf.Min(glow, 3.5f));
                organLight.enabled = glow > 0.06f;
            }
        }

        void SetEmission(List<Renderer> list, int material, Color c)
        {
            foreach (var r in list)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb, material);
                mpb.SetColor(Emission, c);
                r.SetPropertyBlock(mpb, material);
            }
        }

        /// <summary>Gocce di muco che cadono dalle punte e spore luminose che salgono: solo vicino a chi guarda.</summary>
        void Ambient(float dt)
        {
            float dist = enemy != null ? enemy.LastViewDistance : 0f;
            if (dist > 24f) return;
            if (dead && deadT > 3f) return;
            dripT -= dt;
            if (dripT <= 0f && ropes.Count > 0)
            {
                dripT = Random.Range(0.12f, 0.35f) * (dead ? 0.6f : 1f);
                var r = ropes[Random.Range(0, ropes.Count)];
                if (r.primed && r.kind != RopeKind.Thread) JellyFx.Drip(r.p[r.bones.Length]);
            }
            moteT -= dt;
            if (moteT <= 0f && !dead)
            {
                moteT = Random.Range(0.10f, 0.28f);
                JellyFx.Mote(BellCenter + Random.insideUnitSphere * 0.55f);
            }
        }
    }
}
