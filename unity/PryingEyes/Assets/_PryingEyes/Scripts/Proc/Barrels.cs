using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    /// <summary>Qualcosa che reagisce ai colpi d'arma pur non essendo un nemico (Weapon.ShootRay): le botti d'olio santo.</summary>
    public interface IShootable { void Shot(float dmg, Vector3 point, Vector3 dir); }
}

namespace PryingEyes.Proc
{
    /// <summary>Botti d'olio santo (2026-11-15): un pericolo dell'ambiente da usare contro l'orda. Da tre a cinque per piano, nelle sale
    /// grandi lontano dalle porte, a volte in coppia. Bastano un paio di colpi (40 punti di danno) o un'esplosione vicina: scoppiano in
    /// una palla di fuoco (520 al centro, 120 a 5,5 m, chi gioca fino a 45) e accendono le botti entro 4,5 m, a catena. Ostacolo vero
    /// per tutti (NavMeshObstacle che scava la navmesh), che sparisce con lo scoppio.
    /// Cooperativa: la posizione viene dalla pianta (uguale ovunque); il colpo di un ospitato va all'ospite (BarrelHit), l'ospite fa
    /// scoppiare, fa danno e lo dice a tutti (BarrelGone, anche a chi entra tardi).</summary>
    public sealed class Barrels : MonoBehaviour
    {
        public static Barrels I;
        readonly List<Barrel> list = new List<Barrel>();
        public IReadOnlyList<Barrel> List => list;

        public static Barrels Build(FloorInstance fi)
        {
            if (fi == null || fi.plan == null) return null;
            var c = fi.root.AddComponent<Barrels>();
            I = c;
            var rng = new System.Random(fi.plan.seed * 31 + 7);
            var doors = new List<Vector3>();
            if (RoomAccessDoors.I != null) foreach (var d in RoomAccessDoors.I.Doors) if (d != null && d.Shutter != null) doors.Add(d.Shutter.center);
            var pts = new List<Vector3>(fi.groundSpawns);
            // ordine deterministico ma rimescolato col seme della pianta
            for (int i = pts.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pts[i], pts[j]) = (pts[j], pts[i]); }
            int want = 3 + rng.Next(3);
            foreach (var p0 in pts)
            {
                if (c.list.Count >= want) break;
                var room = fi.RoomAt(p0);
                if (room == null || room.W < 100 || room.D < 100) continue;             // solo sale grandi: almeno 10 x 10 m (W e D in decimetri)
                if (!HotelNav.Ground(p0 + Vector3.up, out Vector3 p)) continue;
                if (Physics.CheckCapsule(p + Vector3.up * 1.08f, p + Vector3.up * 1.4f, 1.0f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;   // sfera bassa sopra il pavimento
                bool farDoor = true;
                foreach (var d in doors) if ((d - p).sqrMagnitude < 3.5f * 3.5f) { farDoor = false; break; }
                if (!farDoor) continue;
                bool spaced = true;
                foreach (var b in c.list) if ((b.transform.position - p).sqrMagnitude < 9f * 9f) { spaced = false; break; }
                if (!spaced) continue;
                c.Add(p);
                // a volte una seconda botte accanto: le catene sono il bello
                if (rng.NextDouble() < 0.5 && c.list.Count < want)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    Vector3 q = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.95f;
                    if (HotelNav.Ground(q + Vector3.up, out Vector3 gq) && Mathf.Abs(gq.y - p.y) < 0.1f
                        && !Physics.CheckCapsule(gq + Vector3.up * 0.52f, gq + Vector3.up * 1.2f, 0.45f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                        c.Add(gq);
                }
            }
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] botti d'olio santo: {c.list.Count}");
            return c;
        }

        void Add(Vector3 p)
        {
            var go = new GameObject("Botte_" + list.Count);
            go.transform.SetParent(transform, false);
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, (list.Count * 73f) % 360f, 0f);
            var b = go.AddComponent<Barrel>();
            b.index = list.Count;
            b.Build();
            list.Add(b);
        }

        /// <summary>Un'esplosione nel punto: le botti entro 'radius' scoppiano, prima le piu' vicine (sugli ospitati lo chiede all'ospite).</summary>
        public void ExplodeNear(Vector3 p, float radius, Barrel except = null)
        {
            foreach (var b in list)
            {
                if (b == null || b == except || b.Gone) continue;
                float d = Vector3.Distance(b.transform.position + Vector3.up * 0.5f, p);
                if (d >= radius) continue;
                if (Coop.IsClient) Coop.Game?.BarrelHit(b.index);
                else b.Trigger(0.08f + d * 0.04f);
            }
        }
    }

    public sealed class Barrel : MonoBehaviour, IShootable
    {
        public int index;
        float hp = 40f;
        bool armed;
        public bool Gone { get; private set; }
        NavMeshObstacle obstacle;
        internal Collider body;
        GameObject look;

        public void Build()
        {
            look = new GameObject("Botte");
            look.transform.SetParent(transform, false);
            var wood = Mat(new Color(0.36f, 0.22f, 0.12f), 0.25f);
            var iron = ProcKit.HasMat("ferro") ? ProcKit.Mat("ferro") : Mat(new Color(0.12f, 0.12f, 0.13f), 0.5f);
            var paint = Mat(new Color(0.55f, 0.06f, 0.05f), 0.3f);
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.47f, 0f), new Vector3(0.58f, 0.47f, 0.58f), wood);
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.47f, 0f), new Vector3(0.64f, 0.30f, 0.64f), wood);    // la pancia della botte
            foreach (float y in new[] { 0.10f, 0.34f, 0.60f, 0.84f })
                Piece(PrimitiveType.Cylinder, new Vector3(0f, y, 0f), new Vector3(y > 0.2f && y < 0.7f ? 0.655f : 0.60f, 0.022f, y > 0.2f && y < 0.7f ? 0.655f : 0.60f), iron);
            // la croce rossa dell'olio santo, su due lati
            foreach (int s in new[] { 1, -1 })
            {
                Piece(PrimitiveType.Cube, new Vector3(0f, 0.48f, 0.325f * s), new Vector3(0.05f, 0.24f, 0.012f), paint);
                Piece(PrimitiveType.Cube, new Vector3(0f, 0.52f, 0.325f * s), new Vector3(0.17f, 0.05f, 0.012f), paint);
            }
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.47f, 0f); cap.radius = 0.32f; cap.height = 0.94f;
            body = cap;
            obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.center = new Vector3(0f, 0.5f, 0f); obstacle.radius = 0.36f; obstacle.height = 1f;
            obstacle.carving = true; obstacle.carveOnlyStationary = true;
        }

        static Material Mat(Color c, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        void Piece(PrimitiveType type, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(look.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// <summary>Gia' scoppiata prima che arrivassi (cooperativa, chi entra tardi): sparisce senza effetti.</summary>
        public void Vanish()
        {
            if (Gone) return;
            Gone = true;
            if (look != null) look.SetActive(false);
            if (body != null) body.enabled = false;
            if (obstacle != null) obstacle.enabled = false;
        }

        /// <summary>Un colpo d'arma: sull'ospitato lo dice all'ospite, altrimenti toglie vita alla botte e scoppia sotto zero.</summary>
        public void Shot(float dmg, Vector3 point, Vector3 dir)
        {
            if (Gone || armed) return;
            Fx.Impact(point, -dir, 0.6f);
            if (Coop.IsClient) { Coop.Game?.BarrelHit(index); return; }
            hp -= dmg;
            if (hp <= 0f) Trigger(0f);
        }

        /// <summary>Scoppio dopo 'delay' secondi (solo da soli o sull'ospite: decide lui).</summary>
        public void Trigger(float delay)
        {
            if (Gone || armed || Coop.IsClient) return;
            armed = true;
            StartCoroutine(BoomAfter(delay));
        }

        IEnumerator BoomAfter(float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Boom(true);
        }

        /// <summary>Lo scoppio: 'authority' (da soli o sull'ospite) fa danno, accende le vicine e lo dice agli altri; sugli ospitati solo
        /// l'effetto (il danno lo manda l'ospite).</summary>
        public void Boom(bool authority)
        {
            if (Gone) return;
            Gone = true;
            Vector3 c = transform.position + Vector3.up * 0.6f;
            Fx.HolyBurst(c, new Color(1f, 0.55f, 0.2f));
            Fx.FireRing(transform.position, 3.2f);
            Fx.BloodPool(transform.position, new Color(0.05f, 0.04f, 0.035f, 1f), 1.3f);       // bruciatura a terra
            Sfx.At(SoundBank.Blast, c, 1f, 0.8f, 4f, 70f);
            Sfx.At(SoundBank.Ignite, c, 0.9f, 0.6f, 3f, 40f);
            GameManager.I?.player?.Shake(c, 1.4f, 15f);
            if (look != null) look.SetActive(false);
            if (body != null) body.enabled = false;
            if (obstacle != null) obstacle.enabled = false;
            if (!authority) return;
            // le altre botti non fanno da scudo all'onda (la botte accanto copriva i monaci dietro di lei)
            var shields = new List<Collider>();
            if (Barrels.I != null) foreach (var o in Barrels.I.List) if (o != null && o.body != null && o.body.enabled) { o.body.enabled = false; shields.Add(o.body); }
            var list = new List<Enemy>(Enemy.All);
            Enemy.NoteBlast(c);                                   // chi muore ora vola via dal centro (EnemyRagdoll)
            foreach (var e in list)
            {
                if (e == null || e.IsDead) continue;
                Vector3 ep = e.AimPoint;
                float d = Vector3.Distance(ep, c);
                bool clear = d <= 5.5f && HotelNav.ClearLine(c, ep);
                if (GameManager.autoTest && d < 8f) Debug.Log($"[PryingEyes] botte {index}: {e.type} a {d:F2} m, linea {(clear ? "libera" : "coperta")}, vita {e.hp:F0}");
                if (!clear) continue;
                e.Damage(Mathf.Lerp(520f, 120f, d / 5.5f), ep, false, true, DamageSource.Esplosione);
            }
            for (int i = 0, n = Targets.Count; i < n; i++)
            {
                var t = Targets.At(i);
                if (t == null || !t.Alive) continue;
                Vector3 bodyP = t.Feet + Vector3.up * 1f;
                float d = Vector3.Distance(bodyP, c);
                if (d > 5.5f || !HotelNav.ClearLine(c, bodyP)) continue;
                t.Hurt(Mathf.Lerp(45f, 8f, d / 5.5f), c);
                if (t is LocalTarget) GameManager.damageTaken++;
            }
            foreach (var sh in shields) if (sh != null) sh.enabled = true;
            Barrels.I?.ExplodeNear(c, 4.5f, this);
            Coop.Game?.BarrelGone(index);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] botte {index} scoppiata");
        }
    }
}
