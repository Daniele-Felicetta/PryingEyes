using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Equipaggiamento da lancio (2026-11-15), il letale e il tattico di CoD Zombies in versione dell'albergo:
    ///  - granata d'acquasanta (Z): ampolla di vetro con l'acqua benedetta che brilla; miccia di 2 s, esplosione di luce bianca e acqua
    ///    che ferisce tutto entro 5 m (420 al centro, 90 al bordo), anche chi l'ha lanciata (poco). Due all'inizio, piu' due a ogni
    ///    ondata, al massimo quattro;
    ///  - carillon (4): scatoletta di legno che, appena si posa, suona la sua ninna nanna per 7 s: i nemici entro 35 m (non il capo, non
    ///    la statua) lasciano chi stavano cacciando e vanno da lui, poi esplode (650 al centro). Uno all'inizio, uno ogni tre ondate,
    ///    al massimo due.
    /// In cooperativa ognuno ha i suoi; il danno passa dai colpi (sugli ospitati diventa HitClaim verso l'ospite) e il carillon di un
    /// ospitato lo annuncia all'ospite (NoiseKind.Lure), che ha i nemici da attirare.</summary>
    public sealed class Equipment : MonoBehaviour
    {
        public static Equipment I;
        public int grenades = 2, lures = 1;
        public const int MaxGrenades = 4, MaxLures = 2;
        float throwCd;
        int seenRound = -1;
        PlayerController player;

        public static Equipment Ensure(PlayerController p)
        {
            if (p == null) return null;
            var e = p.GetComponent<Equipment>();
            if (e == null) e = p.gameObject.AddComponent<Equipment>();
            e.player = p;
            I = e;
            return e;
        }

        void Start() => GameManager.I?.hud?.SetEquipment(grenades, lures);

        void Update()
        {
            if (player == null) player = GetComponent<PlayerController>();
            float dt = Time.deltaTime;
            throwCd -= dt;
            // a ogni ondata nuova (anche sugli ospitati, che ricevono il numero dall'ospite) il rifornimento
            int round = RoundDirector.Round;
            if (seenRound < 0) seenRound = round;
            else if (round > seenRound)
            {
                for (int r = seenRound + 1; r <= round; r++)
                {
                    grenades = Mathf.Min(MaxGrenades, grenades + 2);
                    if (r % 3 == 0) lures = Mathf.Min(MaxLures, lures + 1);
                }
                seenRound = round;
                GameManager.I?.hud?.SetEquipment(grenades, lures);
            }
            if (GameMenu.Blocked || player == null || player.hp <= 0f || throwCd > 0f) return;
            if (Input.GetKeyDown(KeyCode.Z) && grenades > 0) Throw(0);
            else if (Input.GetKeyDown(KeyCode.Alpha4) && lures > 0) Throw(1);
        }

        /// <summary>Lancio dalla mano destra, un po' sotto e a destra della visuale, a 13 m/s piu' una spinta verso l'alto e la velocita'
        /// di chi lancia. 0 = granata, 1 = carillon. Pubblico per le prove.</summary>
        public void Throw(int kind)
        {
            if (player == null) player = GetComponent<PlayerController>();
            var cam = player.cam;
            throwCd = 0.75f;
            Vector3 origin = cam.position + cam.right * 0.18f - cam.up * 0.10f + cam.forward * 0.40f;
            var cc = player.GetComponent<CharacterController>();
            Vector3 vel = cam.forward * 13f + Vector3.up * 3.0f + (cc != null ? cc.velocity * 0.6f : Vector3.zero);
            Thrown.Spawn(kind, origin, vel, cc);
            if (kind == 0) grenades--; else lures--;
            player.GetComponent<Weapon>()?.ThrowDip();
            Sfx.At(SoundBank.KnifeSwish, origin, 0.7f, kind == 0 ? 0.85f : 0.7f, 1.5f, 12f);
            GameManager.I?.hud?.SetEquipment(grenades, lures);
        }
    }

    /// <summary>L'oggetto lanciato: ampolla o carillon con fisica vera (rimbalza su muri e pavimenti, non urta chi l'ha lanciato).</summary>
    public sealed class Thrown : MonoBehaviour
    {
        int kind;
        float age, playT = -1f;
        bool done;
        Rigidbody rb;
        Light glow;
        AudioSource song;

        public static Thrown Spawn(int kind, Vector3 pos, Vector3 vel, Collider thrower)
        {
            var go = new GameObject(kind == 0 ? "Acquasanta" : "Carillon");
            go.transform.position = pos;
            go.transform.rotation = Random.rotation;
            var t = go.AddComponent<Thrown>();
            t.kind = kind;
            if (kind == 0) t.BuildFlask(); else t.BuildBox();
            var col = go.AddComponent<SphereCollider>();
            col.radius = kind == 0 ? 0.06f : 0.075f;
            col.material = new PhysicsMaterial { bounciness = kind == 0 ? 0.35f : 0.15f, dynamicFriction = 0.6f, staticFriction = 0.7f,
                bounceCombine = PhysicsMaterialCombine.Maximum };
            if (thrower != null) Physics.IgnoreCollision(col, thrower);
            t.rb = go.AddComponent<Rigidbody>();
            t.rb.mass = 0.4f;
            t.rb.linearVelocity = vel;
            t.rb.angularVelocity = Random.insideUnitSphere * 9f;
            t.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            t.rb.interpolation = RigidbodyInterpolation.Interpolate;
            // i nemici non lo urtano: niente spinte assurde al carillon che tutti vogliono raggiungere
            foreach (var e in Enemy.All) if (e != null) foreach (var ec in e.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, ec);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] lanciato {go.name} da {pos} a {vel.magnitude:F1} m/s");
            return t;
        }

        void BuildFlask()
        {
            var glass = Mat(new Color(0.55f, 0.75f, 0.85f), 0.9f, new Color(0.35f, 0.7f, 1f) * 1.6f);
            var cork = Mat(new Color(0.35f, 0.22f, 0.12f), 0.2f, Color.black);
            Piece(PrimitiveType.Sphere, Vector3.zero, new Vector3(0.10f, 0.10f, 0.10f), glass);
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.065f, 0f), new Vector3(0.035f, 0.03f, 0.035f), glass);
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.10f, 0f), new Vector3(0.04f, 0.012f, 0.04f), cork);
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(0.55f, 0.8f, 1f); glow.range = 2.2f; glow.intensity = 0.9f; glow.shadows = LightShadows.None;
        }

        void BuildBox()
        {
            var wood = Mat(new Color(0.42f, 0.24f, 0.12f), 0.35f, Color.black);
            var brass = Mat(new Color(0.85f, 0.62f, 0.25f), 0.7f, Color.black);
            Piece(PrimitiveType.Cube, Vector3.zero, new Vector3(0.13f, 0.08f, 0.10f), wood);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.045f, 0f), new Vector3(0.135f, 0.012f, 0.105f), brass);
            Piece(PrimitiveType.Cylinder, new Vector3(0.075f, 0.01f, 0f), new Vector3(0.012f, 0.03f, 0.012f), brass).localRotation = Quaternion.Euler(0f, 0f, 90f);
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, 0.78f, 0.45f); glow.range = 2.5f; glow.intensity = 0f; glow.shadows = LightShadows.None;
        }

        static Material Mat(Color c, float smooth, Color emission)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            m.SetFloat("_Smoothness", smooth);
            if (emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return m;
        }

        Transform Piece(PrimitiveType type, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void Update()
        {
            if (done) return;
            float dt = Time.deltaTime;
            age += dt;
            if (kind == 0)
            {
                if (glow != null) glow.intensity = 0.9f + Mathf.Sin(age * 18f) * 0.3f;
                if (age >= 2.0f) Explode(5f, 420f, 90f, 30f);
                return;
            }
            // carillon: comincia a suonare quando si posa (o dopo 2,5 s comunque)
            if (playT < 0f && (age > 2.5f || (age > 0.25f && rb != null && rb.linearVelocity.magnitude < 0.35f)))
            {
                playT = 0f;
                song = Sfx.Loop(gameObject, SoundBank.MusicBox, 0.9f, 2f, 40f, 1.05f);
                Lure.Start(transform, 7f);
                if (Coop.IsClient) Coop.Game?.NoiseHeard(transform.position, 7f, NoiseKind.Lure);      // l'ospite ha i nemici
                if (GameManager.autoTest) Debug.Log($"[PryingEyes] carillon suona a {transform.position}");
            }
            if (playT >= 0f)
            {
                playT += dt;
                if (glow != null) glow.intensity = 1.2f + Mathf.Sin(playT * 6f) * 0.5f;
                if (song != null) song.pitch = 1.05f - Mathf.Clamp01((playT - 5.5f) / 1.5f) * 0.35f;     // la molla si scarica
                if (playT >= 7f) Explode(5f, 650f, 150f, 30f);
            }
        }

        /// <summary>Esplosione: luce, acqua o schegge, onda; danno ai nemici entro 'radius' con la linea libera (dal centro al bordo da
        /// 'center' a 'edge'), a chi l'ha lanciato al massimo 'self'. Sugli ospitati i colpi diventano HitClaim (Enemy.Damage sui pupazzi).</summary>
        void Explode(float radius, float center, float edge, float self)
        {
            done = true;
            Vector3 p = transform.position + Vector3.up * 0.1f;
            Fx.HolyBurst(p, kind == 0 ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.75f, 0.4f));
            GameManager.I?.player?.Shake(p, kind == 0 ? 1f : 1.3f);
            Sfx.At(SoundBank.Blast, p, 1f, kind == 0 ? Random.Range(0.95f, 1.05f) : 0.85f, 4f, 60f);
            var list = new List<Enemy>(Enemy.All);
            Enemy.NoteBlast(p);                                   // chi muore ora vola via dal centro (EnemyRagdoll)
            foreach (var e in list)
            {
                if (e == null || e.IsDead) continue;
                Vector3 ep = e.AimPoint;
                float d = Vector3.Distance(ep, p);
                if (d > radius || !HotelNav.ClearLine(p + Vector3.up * 0.2f, ep)) continue;
                e.Damage(Mathf.Lerp(center, edge, d / radius), ep, false, true, DamageSource.Esplosione);
            }
            var me = Targets.Local;
            if (me != null && me.Alive)
            {
                Vector3 body = me.Feet + Vector3.up * 1f;
                float d = Vector3.Distance(body, p);
                if (d < radius && HotelNav.ClearLine(p + Vector3.up * 0.2f, body)) me.Hurt(Mathf.Lerp(self, 4f, d / radius), p);
            }
            Barrels.I?.ExplodeNear(p, radius);              // le botti d'olio vicine scoppiano a catena
            Lure.Stop(transform);
            Destroy(gameObject);
        }
    }

    /// <summary>Il carillon come bersaglio: i nemici attirati lo inseguono come un giocatore (ITarget), finche' suona.</summary>
    public sealed class Lure : ITarget
    {
        static readonly List<Lure> active = new List<Lure>();
        Transform at;
        float until;
        public const float Range = 35f;

        public static void Start(Transform t, float seconds)
        {
            active.Add(new Lure { at = t, until = Time.time + seconds });
        }

        /// <summary>Sull'ospite: il carillon di un ospitato (NoiseKind.Lure), un segnaposto invisibile nel punto.</summary>
        public static void StartAt(Vector3 pos, float seconds)
        {
            var go = new GameObject("CarillonCompagno");
            go.transform.position = pos;
            Object.Destroy(go, seconds + 0.5f);
            Start(go.transform, seconds);
        }

        public static void Stop(Transform t) => active.RemoveAll(l => l.at == t);

        /// <summary>Il carillon che attira chi sta in 'p', se ce n'e' uno acceso abbastanza vicino.</summary>
        public static bool Pull(Vector3 p, out ITarget lure)
        {
            lure = null;
            active.RemoveAll(l => l.at == null || Time.time > l.until);
            float best = Range * Range;
            foreach (var l in active)
            {
                float d = (l.at.position - p).sqrMagnitude;
                if (d < best) { best = d; lure = l; }
            }
            return lure != null;
        }

        public int Slot => 0;
        public Vector3 Feet => at != null ? at.position : Vector3.zero;
        public bool Alive => at != null && Time.time <= until;
        public float Lean => 0f;
        public float BodyHeight => 0.3f;
        public Vector3 LeanedPoint(float h) => Feet + Vector3.up * Mathf.Min(h, 0.3f);
        public Transform LookAt => at;
        public Transform BeamT => null;
        public float SightRange(Enemy e) => Range;
        public void Hurt(float dmg, Vector3? origin = null) { }
        public void Sedate(float seconds) { }
    }
}
