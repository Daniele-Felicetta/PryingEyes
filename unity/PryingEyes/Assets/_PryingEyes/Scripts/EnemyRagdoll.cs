using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il corpo che cade davvero. Alla morte di un monaco (e di ogni suo costume) o dell'abate l'Animator si ferma, le ossa
    /// principali diventano corpi rigidi (capsule, giunti verso il padre con i limiti delle articolazioni) e il colpo spinge il corpo
    /// dove l'ha preso: la testa all'indietro con un colpo in fronte, il busto con la doppietta, tutto in aria con un'esplosione; chi
    /// correva cade in avanti. Dopo qualche secondo i corpi si fermano e CorpseSink abbassa il cadavere nel pavimento come prima.
    /// I pezzi stanno sullo strato Ignore Raycast: i proiettili e le linee di vista li attraversano (un corpo non fa da scudo a chi
    /// viene dietro) e il giocatore non ci inciampa. Al piu' MaxActive corpi fisici insieme: oltre, la vecchia animazione di caduta.
    /// -noragdoll la spegne (confronto A/B).</summary>
    public sealed class EnemyRagdoll : MonoBehaviour
    {
        public const int MaxActive = 10;
        public const float SettleTime = 2.8f;
        public static bool disabled;
        static readonly List<EnemyRagdoll> live = new List<EnemyRagdoll>();

        /// <summary>Corpi fisici che si muovono ancora (quelli fermi non contano).</summary>
        public static int Active { get { live.RemoveAll(r => r == null); return live.Count; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadArgs()
        {
            disabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noragdoll") >= 0;
            live.Clear();
        }

        // osso; osso dove finisce la capsula (null = la testa, lungo collo->testa per 'len'); padre fisico; raggio e massa;
        // 'ext' = quanto la capsula va oltre l'osso finale (mano, piede); limiti in gradi: twist attorno all'asse di flessione
        // (la destra del corpo; +twist porta un osso che scende verso dietro), swing1 di lato, swing2 attorno all'osso
        struct Spec
        {
            public string bone, end, parent;
            public float radius, mass, ext, low, high, swing1, swing2;
            public Spec(string bone, string end, string parent, float radius, float mass, float ext, float low, float high, float swing1, float swing2)
            {
                this.bone = bone; this.end = end; this.parent = parent; this.radius = radius; this.mass = mass; this.ext = ext;
                this.low = low; this.high = high; this.swing1 = swing1; this.swing2 = swing2;
            }
        }

        // ginocchio solo all'indietro, gomito solo in avanti, coscia in avanti fino a 90 gradi e indietro di 20
        static readonly Spec[] Specs =
        {
            new Spec("bacino", "spina", null, 0.15f, 12f, 0f, 0f, 0f, 0f, 0f),
            new Spec("petto", "collo", "bacino", 0.16f, 14f, 0f, -30f, 20f, 15f, 10f),
            new Spec("testa", null, "petto", 0.11f, 5f, 0.22f, -40f, 30f, 30f, 25f),
            new Spec("braccio_d", "avambraccio_d", "petto", 0.06f, 3f, 0f, -90f, 60f, 40f, 30f),
            new Spec("avambraccio_d", "mano_d", "braccio_d", 0.05f, 2f, 0.07f, -120f, 5f, 5f, 10f),
            new Spec("braccio_s", "avambraccio_s", "petto", 0.06f, 3f, 0f, -90f, 60f, 40f, 30f),
            new Spec("avambraccio_s", "mano_s", "braccio_s", 0.05f, 2f, 0.07f, -120f, 5f, 5f, 10f),
            new Spec("coscia_d", "stinco_d", "bacino", 0.085f, 8f, 0f, -90f, 20f, 25f, 10f),
            new Spec("stinco_d", "piede_d", "coscia_d", 0.065f, 4f, 0.05f, -5f, 115f, 3f, 5f),
            new Spec("coscia_s", "stinco_s", "bacino", 0.085f, 8f, 0f, -90f, 20f, 25f, 10f),
            new Spec("stinco_s", "piede_s", "coscia_s", 0.065f, 4f, 0.05f, -5f, 115f, 3f, 5f),
        };

        readonly List<Rigidbody> bodies = new List<Rigidbody>();
        readonly List<Collider> parts = new List<Collider>();
        float t;
        bool frozen;

        /// <summary>Quando il corpo si ferma: il punto del pavimento sotto il bacino (Enemy ci mette la pozza di sangue).</summary>
        public System.Action<Vector3> Settled;
        /// <summary>Il bastone lasciato andare (corpo rigido suo, senza giunto), o null.</summary>
        public Rigidbody Staff { get; private set; }

        /// <summary>I corpi rigidi, il bacino per primo.</summary>
        public IReadOnlyList<Rigidbody> Bodies => bodies;
        public bool Frozen => frozen;

        /// <summary>Trasforma il corpo in ragdoll. 'vel' = la velocita' che aveva; 'point'/'dir' = dove e verso dove l'ha preso il
        /// colpo, 'impulse' quanto forte (N*s; con 'blast' e' la velocita' data da un'esplosione centrata in 'point', in m/s).
        /// Null (e nulla cambia) se il corpo non ha le ossa o ci sono gia' MaxActive corpi fisici: resta l'animazione di caduta.</summary>
        public static EnemyRagdoll Begin(GameObject body, Animator anim, Vector3 vel, Vector3 point, Vector3 dir, float impulse, bool blast)
        {
            if (disabled || body == null || Active >= MaxActive) return null;
            // le ossa: transform senza Renderer (il bastone ha sia l'osso sia la mesh con lo stesso nome)
            var map = new Dictionary<string, Transform>();
            foreach (var tr in body.GetComponentsInChildren<Transform>(true))
                if (tr.GetComponent<Renderer>() == null && !map.ContainsKey(tr.name)) map[tr.name] = tr;
            foreach (var s in Specs) if (!map.ContainsKey(s.bone) || (s.end != null && !map.ContainsKey(s.end))) return null;
            if (!map.TryGetValue("collo", out var neck)) return null;
            if (anim != null) anim.enabled = false;
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            var r = body.AddComponent<EnemyRagdoll>();
            Vector3 right = body.transform.right, fwd = body.transform.forward;
            var made = new Dictionary<string, Rigidbody>();
            foreach (var s in Specs)
            {
                var b = map[s.bone];
                Vector3 endW;
                if (s.end != null)
                {
                    endW = map[s.end].position;
                    if (s.ext > 0f) endW += (endW - b.position).normalized * s.ext;
                }
                else
                {
                    Vector3 up = b.position - neck.position;
                    endW = b.position + (up.sqrMagnitude > 1e-6f ? up.normalized : body.transform.up) * s.ext;
                }
                float k = Mathf.Max(1e-4f, b.lossyScale.x);
                Vector3 d = b.InverseTransformPoint(endW);
                Vector3 ad = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
                int axis = ad.x > ad.y ? (ad.x > ad.z ? 0 : 2) : (ad.y > ad.z ? 1 : 2);
                float rad = s.radius / k;
                var cap = b.gameObject.AddComponent<CapsuleCollider>();
                cap.direction = axis;
                cap.radius = rad;
                cap.height = d.magnitude + rad * 2f;
                cap.center = d * 0.5f;
                b.gameObject.layer = 2;                                  // Ignore Raycast: niente proiettili ne' viste
                var rb = b.gameObject.AddComponent<Rigidbody>();
                rb.mass = s.mass;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.6f;
                rb.maxDepenetrationVelocity = 2f;                       // un braccio dentro un muro non fa esplodere il corpo
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12;
                rb.solverVelocityIterations = 4;
                rb.linearVelocity = vel;
                made[s.bone] = rb;
                r.bodies.Add(rb);
                r.parts.Add(cap);
                if (s.parent != null && made.TryGetValue(s.parent, out var pr))
                {
                    var j = b.gameObject.AddComponent<CharacterJoint>();
                    j.connectedBody = pr;
                    j.axis = b.InverseTransformDirection(right).normalized;
                    j.swingAxis = b.InverseTransformDirection(fwd).normalized;
                    j.lowTwistLimit = new SoftJointLimit { limit = s.low };
                    j.highTwistLimit = new SoftJointLimit { limit = s.high };
                    j.swing1Limit = new SoftJointLimit { limit = s.swing1 };
                    j.swing2Limit = new SoftJointLimit { limit = s.swing2 };
                    j.enableProjection = true;
                    j.enablePreprocessing = false;
                }
            }
            // il bastone e' un osso senza padre (l'animazione lo teneva in mano): lo lascia andare e cade da solo
            if (map.TryGetValue("bastone", out var staff)) r.Staff = DropStaff(body, staff, vel, r.parts);
            // i pezzi dello stesso corpo non si urtano fra loro; il giocatore non ci inciampa
            for (int a = 0; a < r.parts.Count; a++)
                for (int c = a + 1; c < r.parts.Count; c++) Physics.IgnoreCollision(r.parts[a], r.parts[c]);
            var player = GameManager.I != null ? GameManager.I.player : null;
            var cc = player != null ? player.GetComponent<CharacterController>() : null;
            if (cc != null) foreach (var p in r.parts) Physics.IgnoreCollision(p, cc);
            r.Push(point, dir, impulse, blast);
            live.Add(r);
            return r;
        }

        // il bastone del monaco (modelli/monaco/genera_monaco.py, build_staff): la base poggia a terra, la cima arriva a 1,48 m,
        // l'osso 'bastone' parte dall'impugnatura (a 1,07 m) e corre lungo il bastone. Misure del modello in metri.
        const float StaffBelow = 1.07f, StaffAbove = 0.41f, StaffRadius = 0.028f;

        /// <summary>Il bastone fa parte della mesh del corpo, pesato sull'osso 'bastone' (senza padre): una capsula lungo l'osso e un
        /// corpo rigido senza giunto, cosi' il monaco lo lascia andare e cade da solo.</summary>
        static Rigidbody DropStaff(GameObject body, Transform staff, Vector3 vel, List<Collider> parts)
        {
            float k = Mathf.Max(1e-4f, staff.lossyScale.x);
            float s = Mathf.Max(1e-4f, body.transform.lossyScale.x) / k;      // metri del modello -> unita' locali dell'osso
            var cap = staff.gameObject.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = StaffRadius * s;
            cap.height = (StaffBelow + StaffAbove) * s;
            cap.center = new Vector3(0f, (StaffAbove - StaffBelow) * 0.5f * s, 0f);
            staff.gameObject.layer = 2;
            var rb = staff.gameObject.AddComponent<Rigidbody>();
            rb.mass = 2.5f;
            rb.angularDamping = 0.3f;
            rb.maxDepenetrationVelocity = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = vel;
            rb.angularVelocity = staff.TransformDirection(Vector3.right) * Random.Range(-1.2f, 1.2f);
            foreach (var p in parts) Physics.IgnoreCollision(p, cap);
            parts.Add(cap);
            return rb;
        }

        void Push(Vector3 point, Vector3 dir, float impulse, bool blast)
        {
            if (impulse <= 0f || bodies.Count == 0) return;
            if (blast)
            {
                // ogni pezzo prende la stessa velocita' (meno con la distanza): il corpo vola intero, non si strappa ai giunti
                foreach (var rb in bodies) rb.AddExplosionForce(impulse, point, 5f, 0.8f, ForceMode.VelocityChange);
                return;
            }
            // il pezzo piu' vicino al colpo prende la spinta (al piu' 3,5 m/s), il bacino un quarto: il corpo arretra mentre crolla
            Rigidbody near = bodies[0];
            float best = float.MaxValue;
            for (int i = 0; i < bodies.Count; i++)
            {
                float dd = (parts[i].ClosestPoint(point) - point).sqrMagnitude;
                if (dd < best) { best = dd; near = bodies[i]; }
            }
            Vector3 dn = dir.sqrMagnitude > 1e-6f ? dir.normalized : -transform.forward;
            near.AddForceAtPosition(dn * Mathf.Min(impulse, near.mass * 3.5f), point, ForceMode.Impulse);
            if (near != bodies[0]) bodies[0].AddForce(dn * impulse * 0.25f, ForceMode.Impulse);
        }

        void FixedUpdate()
        {
            if (frozen) return;
            t += Time.fixedDeltaTime;
            bool still = t > 1.2f;
            if (still) foreach (var rb in bodies) if (rb != null && rb.linearVelocity.sqrMagnitude > 0.0225f) { still = false; break; }
            if (t < SettleTime && !still) return;
            // fermi: corpi cinematici, cosi' CorpseSink puo' abbassare il cadavere spostando la radice
            frozen = true;
            foreach (var rb in bodies)
                if (rb != null) { rb.interpolation = RigidbodyInterpolation.None; rb.isKinematic = true; }
            if (Staff != null) { Staff.interpolation = RigidbodyInterpolation.None; Staff.isKinematic = true; }
            live.Remove(this);
            if (Settled != null && bodies.Count > 0 && bodies[0] != null)
            {
                Vector3 pel = bodies[0].position;
                Vector3 floor = Physics.Raycast(pel + Vector3.up * 0.3f, Vector3.down, out var hit, 2.5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)
                    ? hit.point : new Vector3(pel.x, transform.position.y, pel.z);
                Settled(floor);
            }
        }

        void OnDestroy() { live.Remove(this); }
    }
}
