using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>La mina del reliquiario (2026-11-15), il terzo equipaggiamento (tasto 5): un disco d'ottone con la croce e una cupola
    /// di vetro rosso, posato a terra un metro davanti. Si arma in 1,2 s (la spia lampeggia piano), poi aspetta: quando un nemico che
    /// cammina le arriva a 1,7 m la spia impazzisce, un bip, e dopo un quarto di secondo scoppia (520 di danno al centro, 110 a 4,5 m,
    /// con la linea libera; i corpi volano via, le botti vicine scoppiano a catena). Chi gioca non la fa scattare e non si fa male.
    /// Una all'inizio, una ogni quattro ondate, al massimo due in tasca e tre posate. Sta nel piano: scendendo sparisce. In cooperativa
    /// ognuno ha le sue e il danno passa dai colpi, come per l'acquasanta.</summary>
    public sealed class Mine : MonoBehaviour
    {
        public const float ArmTime = 1.2f, Trigger = 1.7f, Fuse = 0.25f, Radius = 4.5f;
        public const int MaxPlaced = 3;
        static readonly List<Mine> placed = new List<Mine>();
        public static IReadOnlyList<Mine> Placed { get { placed.RemoveAll(m => m == null); return placed; } }

        float age, fuse = -1f, scanT;
        bool done;
        Material dome;
        Light spia;
        /// <summary>Schermo condiviso: chi l'ha posata (le sue uccisioni e i suoi punti). null: giocatore singolo.</summary>
        Seat owner;

        public bool Armed => age >= ArmTime;
        public bool Triggered => fuse >= 0f;

        /// <summary>Dove si posa: un metro davanti a chi gioca, sul pavimento piano e vicino alla navmesh; altrimenti ai suoi piedi.</summary>
        public static Vector3 Spot(PlayerController p)
        {
            Vector3 fwd = new Vector3(Mathf.Sin(p.yaw), 0f, Mathf.Cos(p.yaw));
            Vector3 feet = p.transform.position;
            Vector3 from = feet + fwd * 1.0f + Vector3.up * 0.9f;
            if (HotelNav.ClearLine(feet + Vector3.up * 0.9f, from) &&
                Physics.Raycast(from, Vector3.down, out var hit, 2.5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) &&
                hit.normal.y > 0.85f && Mathf.Abs(hit.point.y - feet.y) < 0.6f &&
                NavMesh.SamplePosition(hit.point, out _, 0.6f, NavMesh.AllAreas))
                return hit.point;
            return feet;
        }

        public static Mine Place(Vector3 at, float yaw)
        {
            // la piu' vecchia di chi la posa scoppia: ne restano tre a testa
            Mine oldest = null; int mine = 0;
            foreach (var other in Placed) if (other.owner == Seats.Current) { if (oldest == null) oldest = other; mine++; }
            if (mine >= MaxPlaced && oldest != null) oldest.Explode();
            var go = new GameObject("Mina del reliquiario");
            var root = FloorDirector.I != null && FloorDirector.I.cur != null ? FloorDirector.I.cur.root : null;
            if (root != null) go.transform.SetParent(root.transform, false);
            go.transform.position = at + Vector3.up * 0.005f;
            go.transform.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
            var m = go.AddComponent<Mine>();
            m.owner = Seats.Current;
            m.Build();
            placed.Add(m);
            Sfx.At(SoundBank.DropTick, at, 0.6f, 0.8f, 1.5f, 15f);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] mina posata a {at}");
            return m;
        }

        void Build()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var brass = new Material(lit) { color = new Color(0.78f, 0.56f, 0.22f) };
            brass.SetFloat("_Metallic", 0.85f); brass.SetFloat("_Smoothness", 0.6f);
            var iron = new Material(lit) { color = new Color(0.12f, 0.12f, 0.13f) };
            iron.SetFloat("_Metallic", 0.6f); iron.SetFloat("_Smoothness", 0.35f);
            dome = new Material(lit) { color = new Color(0.5f, 0.04f, 0.03f) };
            dome.EnableKeyword("_EMISSION");
            dome.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            dome.SetColor("_EmissionColor", Color.black);
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.012f, 0f), new Vector3(0.24f, 0.012f, 0.24f), iron);
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.2f, 0.008f, 0.2f), brass);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.042f, 0.03f), new Vector3(0.02f, 0.008f, 0.12f), brass);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.042f, 0.05f), new Vector3(0.08f, 0.008f, 0.02f), brass);
            Piece(PrimitiveType.Sphere, new Vector3(0f, 0.045f, -0.045f), new Vector3(0.05f, 0.04f, 0.05f), dome);
            var lg = new GameObject("Spia");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0f, 0.12f, -0.045f);
            spia = lg.AddComponent<Light>();
            spia.type = LightType.Point; spia.color = new Color(1f, 0.12f, 0.06f); spia.range = 1.4f; spia.intensity = 0f; spia.shadows = LightShadows.None;
        }

        void Piece(PrimitiveType type, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Blink(bool on, float strength)
        {
            if (dome != null) dome.SetColor("_EmissionColor", on ? new Color(1f, 0.08f, 0.04f) * 3f * strength : Color.black);
            if (spia != null) spia.intensity = on ? 0.9f * strength : 0f;
        }

        void Update() { using (Seats.Enter(owner)) UpdateBody(); }

        void UpdateBody()
        {
            if (done) return;
            float dt = Time.deltaTime;
            age += dt;
            if (fuse >= 0f)
            {
                fuse += dt;
                Blink(Mathf.Repeat(fuse, 0.06f) < 0.03f, 1.3f);
                if (fuse >= Fuse) Explode();
                return;
            }
            if (!Armed) { Blink(Mathf.Repeat(age, 0.5f) < 0.08f, 0.6f); return; }
            Blink(Mathf.Repeat(age, 1.4f) < 0.07f, 1f);
            scanT -= dt;
            if (scanT > 0f) return;
            scanT = 0.08f;
            Vector3 p = transform.position;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.IsDead || Enemy.IsFlying(e.type)) continue;
                Vector3 q = e.transform.position;
                if (Mathf.Abs(q.y - p.y) > 1.2f || new Vector2(q.x - p.x, q.z - p.z).sqrMagnitude > Trigger * Trigger) continue;
                fuse = 0f;
                Sfx.At(SoundBank.Monitor, p, 0.9f, 1.6f, 2f, 25f);
                if (GameManager.autoTest) Debug.Log($"[PryingEyes] mina: {e.type} a {Vector3.Distance(q, p):F2} m, scatta");
                break;
            }
        }

        /// <summary>Lo scoppio: danno ai nemici entro 4,5 m con la linea libera, nessuno a chi gioca; le botti vicine a catena.</summary>
        public void Explode() { using (Seats.Enter(owner)) ExplodeBody(); }

        void ExplodeBody()
        {
            if (done) return;
            done = true;
            placed.Remove(this);
            Vector3 p = transform.position + Vector3.up * 0.15f;
            Fx.HolyBurst(p, new Color(1f, 0.55f, 0.3f));
            Seats.ShakeAll(p, 1.2f);
            Sfx.At(SoundBank.Blast, p, 1f, Random.Range(0.9f, 1.0f), 4f, 60f);
            Enemy.NoteBlast(p);
            foreach (var e in new List<Enemy>(Enemy.All))
            {
                if (e == null || e.IsDead) continue;
                Vector3 ep = e.AimPoint;
                float d = Vector3.Distance(ep, p);
                if (d > Radius || !HotelNav.ClearLine(p + Vector3.up * 0.2f, ep)) continue;
                e.Damage(Mathf.Lerp(520f, 110f, d / Radius), ep, false, true, DamageSource.Esplosione);
            }
            Barrels.I?.ExplodeNear(p, Radius);
            Destroy(gameObject);
        }

        void OnDestroy() { placed.Remove(this); }
    }
}
