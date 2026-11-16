using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Lampadari che crollano, un trabocchetto dell'ambiente (2026-11-15). I lampadari appesi (le corone di ferro delle navate,
    /// i lampadari delle sale a balconata e delle sale con la fonte) si abbattono sparando al lampadario (60 punti di danno): la luce
    /// si spegne, la catena si spezza, il lampadario cade e schiaccia chi sta sotto (un metro e mezzo piu' la sua mezza larghezza: i
    /// nemici volano via dal punto d'impatto, i boss perdono un quarto della vita, chi gioca perde 60), poi resta a terra storto come
    /// ostacolo (NavMeshObstacle che scava la navmesh). Una volta sola. In cooperativa decide l'ospite: il colpo di un ospitato gli
    /// arriva (ChandelierHit), la caduta va a tutti (ChandelierDrop), a chi entra tardi quelli gia' a terra.</summary>
    public sealed class Chandeliers : MonoBehaviour
    {
        public static Chandeliers I;
        readonly List<Chandelier> list = new List<Chandelier>();
        public IReadOnlyList<Chandelier> List => list;

        public static void Build(FloorInstance fi)
        {
            if (I != null) Destroy(I.gameObject);
            I = null;
            if (fi == null || fi.root == null) return;
            var go = new GameObject("Lampadari");
            go.transform.SetParent(fi.root.transform, false);
            I = go.AddComponent<Chandeliers>();
            foreach (var t in fi.root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "chi_lampadario" || !t.gameObject.activeInHierarchy) continue;
                if (t.parent != null && t.parent.GetComponentInParent<Chandelier>() != null) continue;   // il modello dentro il prop ha lo stesso nome
                bool nested = false;
                for (var p = t.parent; p != null && !nested; p = p.parent) nested = p.name == "chi_lampadario";
                if (nested) continue;
                var ch = Chandelier.Make(t.gameObject, I.list.Count);
                if (ch != null) I.list.Add(ch);
            }
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] lampadari che crollano: {I.list.Count}");
        }

        void OnDestroy() { if (I == this) I = null; }
    }

    public sealed class Chandelier : MonoBehaviour, IShootable
    {
        public int index;
        public bool Fallen { get; private set; }
        public bool Falling { get; private set; }
        /// <summary>Dove tocca terra e il raggio di chi resta schiacciato.</summary>
        public Vector3 ImpactPoint => impact;
        public float Radius => radius;
        public IReadOnlyList<PoolLight> Lights => lights;
        public Transform Chain => chain;
        float hp = 60f, radius, half, drop, tilt;
        Vector3 impact, axis;
        Transform chain;
        readonly List<PoolLight> lights = new List<PoolLight>();

        /// <summary>Lo rende abbattibile se pende abbastanza in alto (sotto deve passarci un monaco) sopra un pavimento.</summary>
        public static Chandelier Make(GameObject g, int index)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            Bounds b = default;
            bool any = false;
            foreach (var r in rs)
            {
                if (r.name == "Catena") continue;
                if (any) b.Encapsulate(r.bounds); else b = r.bounds;
                any = true;
            }
            if (!any) return null;
            Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
            if (!Physics.Raycast(bottom + Vector3.down * 0.05f, Vector3.down, out var hit, 20f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return null;
            if (hit.distance < 2.1f) return null;
            var ch = g.AddComponent<Chandelier>();
            ch.index = index;
            ch.drop = hit.distance + 0.05f;
            ch.impact = hit.point;
            ch.half = Mathf.Max(b.extents.x, b.extents.z);
            ch.radius = 1.5f + ch.half * 0.6f;
            // la caduta e' decisa da subito (uguale su tutte le macchine): di quanto si storce e attorno a che asse
            var rng = new System.Random(index * 7919 + Mathf.RoundToInt(hit.point.x * 10f) * 31 + Mathf.RoundToInt(hit.point.z * 10f));
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            ch.axis = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            ch.tilt = 9f + (float)rng.NextDouble() * 9f;
            // il bersaglio dei proiettili, sullo strato di default (le viste dei nemici passano sotto: e' in alto)
            var sc = g.AddComponent<SphereCollider>();
            float s = Mathf.Max(1e-4f, g.transform.lossyScale.x);
            sc.center = g.transform.InverseTransformPoint(b.center);
            sc.radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 0.8f / s;
            // la catena: quella della corona, o una nuova dal lampadario al soffitto
            var existing = g.transform.Find("Catena");
            Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);
            if (existing != null) ch.chain = existing;
            else if (Physics.Raycast(top + Vector3.up * 0.05f, Vector3.up, out var up, 12f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && up.distance > 0.15f)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(c.GetComponent<Collider>());
                c.name = "Catena";
                c.transform.SetParent(g.transform.parent, false);
                c.transform.position = (top + up.point) * 0.5f;
                c.transform.localScale = new Vector3(0.03f, (up.point.y - top.y) * 0.5f, 0.03f);
                var mr = c.GetComponent<MeshRenderer>();
                if (ProcKit.HasMat("ferro")) mr.sharedMaterial = ProcKit.Mat("ferro");
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ch.chain = c.transform;
            }
            // le sue luci: le figlie e quelle che gli stanno appese al centro (il faro dei lampadari delle sale)
            foreach (var pl in LightPool.All)
            {
                if (pl.l == null) continue;
                Vector3 lp = pl.l.transform.position;
                bool child = pl.l.transform.IsChildOf(g.transform);
                bool near = new Vector2(lp.x - b.center.x, lp.z - b.center.z).magnitude < 0.9f && lp.y > b.min.y - 0.6f && lp.y < b.max.y + 2.5f;
                if (child || near) ch.lights.Add(pl);
            }
            return ch;
        }

        public void Shot(float dmg, Vector3 point, Vector3 dir)
        {
            if (Fallen || Falling) return;
            Fx.Impact(point, -dir, 0.5f, Surface.Metal, dir);
            if (Coop.IsClient) { Coop.Game?.ChandelierHit(index); return; }
            hp -= dmg;
            if (hp <= 0f) Drop(0);
        }

        /// <summary>Da soli o sull'ospite: cade, fa danno (le uccisioni a 'slot') e lo dice agli altri.</summary>
        public void Drop(int slot)
        {
            if (Fallen || Falling || Coop.IsClient) return;
            owner = Seats.Current;                      // schermo condiviso: chi l'ha fatto cadere incassa punti e uccisioni
            StartCoroutine(Fall(true, slot));
            Coop.Game?.ChandelierDropped(index);
        }

        Seat owner;

        /// <summary>Sugli ospitati: la caduta vista (il danno lo manda l'ospite); 'silent' = gia' a terra (chi entra tardi).</summary>
        public void Remote(bool silent)
        {
            if (Fallen || Falling) return;
            if (!silent) { StartCoroutine(Fall(false, -1)); return; }
            Snap();
            transform.position += Vector3.down * drop;
            transform.rotation = Quaternion.AngleAxis(tilt, axis) * transform.rotation;
            LieDown();
        }

        /// <summary>Si spegne, la catena si spezza (ne resta un terzo appeso al soffitto), il bersaglio sparisce.</summary>
        void Snap()
        {
            foreach (var pl in lights) { pl.wanted = false; if (pl.l != null) pl.l.enabled = false; }
            if (chain != null)
            {
                if (chain.IsChildOf(transform)) chain.SetParent(transform.parent, true);
                float halfLen = chain.lossyScale.y;                    // il cilindro di Unity e' alto 2: mezza lunghezza in metri
                Vector3 top = chain.position + chain.up * halfLen;
                var ls = chain.localScale; ls.y *= 0.33f; chain.localScale = ls;
                chain.position = top - chain.up * (halfLen * 0.33f);
            }
            var col = GetComponent<SphereCollider>();
            if (col != null) col.enabled = false;
        }

        IEnumerator Fall(bool authority, int slot)
        {
            Falling = true;
            Snap();
            Sfx.At(SoundBank.ChainSnap, transform.position, 0.9f, Random.Range(0.95f, 1.05f), 4f, 50f);
            Vector3 start = transform.position;
            Quaternion r0 = transform.rotation;
            float t = 0f, total = Mathf.Sqrt(2f * drop / 9.81f);
            while (t < total)
            {
                t += Time.deltaTime;
                float k = Mathf.Min(1f, t / total);
                transform.position = start + Vector3.down * Mathf.Min(drop, 0.5f * 9.81f * t * t);
                transform.rotation = Quaternion.AngleAxis(tilt * k * k, axis) * r0;
                yield return null;
            }
            transform.position = start + Vector3.down * drop;
            transform.rotation = Quaternion.AngleAxis(tilt, axis) * r0;
            using (Seats.Enter(owner)) Impact(authority, slot);
            Vector3 rest = transform.position;
            for (float u = 0f; u < 0.22f; u += Time.deltaTime)
            {
                transform.position = rest + Vector3.up * (Mathf.Sin(u / 0.22f * Mathf.PI) * 0.07f);
                yield return null;
            }
            transform.position = rest;
            LieDown();
        }

        void Impact(bool authority, int slot)
        {
            Vector3 c = impact + Vector3.up * 0.25f;
            Sfx.At(SoundBank.ChandelierCrash, c, 1f, Random.Range(0.92f, 1.04f), 6f, 70f);
            var grey = new Color(0.55f, 0.52f, 0.48f);
            for (int i = 0; i < 5; i++)
            {
                Vector3 off = Quaternion.Euler(0f, i * 72f, 0f) * Vector3.forward * (half * 0.7f + 0.3f);
                Fx.EnemyImpact(impact + off + Vector3.up * 0.05f, Vector3.up, grey);
            }
            Fx.EnemyImpact(c, Vector3.up, new Color(1f, 0.85f, 0.55f));
            Seats.ShakeAll(c, 1.1f, 14f);
            if (!authority) return;
            // chi sta sotto: i nemici volano via dal punto d'impatto (EnemyRagdoll), i boss perdono un quarto della vita
            Enemy.NoteBlast(impact);
            int kills = 0;
            foreach (var e in new List<Enemy>(Enemy.All))
            {
                if (e == null || e.IsDead) continue;
                Vector3 p = e.transform.position;
                if (Mathf.Abs(p.y - impact.y) > 2.5f || new Vector2(p.x - impact.x, p.z - impact.z).magnitude > radius) continue;
                float dmg = Enemy.IsBoss(e.type) ? e.maxHp * 0.25f : e.hp + 1f;
                e.Damage(dmg, e.AimPoint, false, true, DamageSource.Esplosione, Mathf.Max(0, slot));
                if (e.IsDead) kills++;
            }
            for (int i = 0, n = Targets.Count; i < n; i++)
            {
                var tg = Targets.At(i);
                if (tg == null || !tg.Alive) continue;
                Vector3 f = tg.Feet;
                if (Mathf.Abs(f.y - impact.y) > 2.5f || new Vector2(f.x - impact.x, f.z - impact.z).magnitude > radius * 0.75f) continue;
                tg.Hurt(60f, c);
                Seats.CountDamageTaken(tg);
            }
            if (kills >= 3) GameManager.I?.hud?.Toast($"Il lampadario ne ha schiacciati {kills}.");
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] lampadario {index} a terra: {kills} uccisi");
        }

        /// <summary>A terra, storto, spento: un ostacolo che scava la navmesh (i nemici ci girano attorno, si puo' usare come riparo).</summary>
        void LieDown()
        {
            Falling = false;
            Fallen = true;
            var ob = gameObject.GetComponent<NavMeshObstacle>();
            if (ob == null) ob = gameObject.AddComponent<NavMeshObstacle>();
            float s = Mathf.Max(1e-4f, transform.lossyScale.x);
            ob.shape = NavMeshObstacleShape.Capsule;
            ob.radius = Mathf.Max(0.3f, half * 0.8f) / s;
            ob.height = 1.2f / s;
            ob.center = new Vector3(0f, 0.6f / s, 0f);
            ob.carving = true;
            ob.carveOnlyStationary = true;
        }
    }
}
