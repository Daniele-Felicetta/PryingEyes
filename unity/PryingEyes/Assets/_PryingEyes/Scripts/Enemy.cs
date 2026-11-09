using UnityEngine;
using System.Collections.Generic;

namespace PryingEyes
{
    /// <summary>Drone (flying) / Brute (walker) / Monaco (walker animato, modello da Blender):
    /// chase, attack, enrage <50%, separation, hit flash.</summary>
    public class Enemy : MonoBehaviour
    {
        public enum EnemyType { Drone, Brute, Monaco }
        public EnemyType type;

        public float hp, maxHp, speed;
        float atkCd = 0f, flash = 0f;
        // monaco: animazioni guidate da qui (Velocita, PassoVel, Attacca, Colpito, Morto)
        Animator anim;
        bool dead;
        float strikeAt = -1f, atkLock = 0f;
        const float MonacoWalkSpeed = 1.0f;   // m/s della camminata in Blender a velocita' 1
        Vector3 vel;
        Transform playerT;
        Renderer[] renderers;
        public List<Material> flashMats = new List<Material>();
        public Material glowMat;
        List<Transform> rotors = new List<Transform>();

        static readonly List<Enemy> all = new List<Enemy>();

        public static Enemy Nearest(Vector3 p)
        {
            Enemy best = null;
            float bd = float.MaxValue;
            foreach (var e in all)
            {
                if (e == null) continue;
                float d = (e.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        public static Enemy Spawn(EnemyType t, Vector3 pos)
        {
            Material glow;
            List<Material> flashes;
            GameObject go = t == EnemyType.Drone ? BuildDrone(out glow, out flashes)
                : t == EnemyType.Brute ? BuildBrute(out glow, out flashes)
                : BuildMonaco(out glow, out flashes);
            go.transform.position = pos;
            Enemy e = go.AddComponent<Enemy>();
            e.type = t;
            e.hp = e.maxHp = t == EnemyType.Drone ? 60f : t == EnemyType.Brute ? 130f : 170f;
            e.speed = t == EnemyType.Drone ? 5.0f + Random.value * 1.5f
                : t == EnemyType.Brute ? 3.4f + Random.value
                : 1.35f + Random.value * 0.35f;      // lento e ostinato
            e.anim = go.GetComponentInChildren<Animator>();
            e.playerT = GameManager.I.player.transform;
            e.renderers = go.GetComponentsInChildren<Renderer>();
            e.flashMats = flashes;
            e.glowMat = glow;
            foreach (var m in flashes) m.EnableKeyword("_EMISSION");
            foreach (var tr in go.GetComponentsInChildren<Transform>())
                if (tr.name == "Rotor") e.rotors.Add(tr);
            all.Add(e);
            GameManager.I.SetAlive(all.Count);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] spawn {t} hp={e.hp} vel={e.speed:F2} pos={pos}");
            return e;
        }

        void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;
            flash = Mathf.Max(0f, flash - dt * 5f);
            if (flash > 0f)
            {
                var ec = Color.white * flash;
                foreach (var m in flashMats) m.SetColor("_EmissionColor", ec);
            }
            atkCd -= dt;

            Vector3 gp = transform.position;
            Vector3 pp = playerT.position + Vector3.up * 1.5f;
            Vector3 toP = pp - gp; toP.y = 0f;
            float dist = toP.magnitude;
            if (dist > 0.01f) toP /= dist;

            Vector3 sep = Vector3.zero;
            foreach (var o in all)
            {
                if (o == this) continue;
                Vector3 d = gp - o.transform.position; d.y = 0f;
                float l = d.magnitude;
                if (l < 2f && l > 0.01f) sep += d.normalized * (2f - l) * 1.5f;
            }

            bool enraged = hp < maxHp * 0.5f;
            if (type == EnemyType.Drone)
            {
                float targetY = 2.4f + Mathf.Sin(Time.time * 2f) * 0.5f;
                Vector3 desired = toP * speed * (enraged ? 0.8f : 1f) + sep;
                vel = Vector3.Lerp(vel, desired, Mathf.Min(1f, 3f * dt));
                gp += vel * dt;
                gp.y += (targetY - gp.y) * Mathf.Min(1f, 3f * dt);
                transform.position = gp;
                transform.LookAt(new Vector3(pp.x, gp.y, pp.z));
                foreach (var r in rotors) r.Rotate(Vector3.up, 1200f * dt, Space.Self);
                if (dist < 3.0f && atkCd <= 0f && GameManager.I.player.alive)
                {
                    atkCd = 1.1f;
                    GameManager.I.player.Hurt(8f + Random.value * 6f);
                }
            }
            else if (type == EnemyType.Monaco)
            {
                UpdateMonaco(dt, gp, pp, toP, dist, sep, enraged);
            }
            else
            {
                float spd = speed * (enraged ? 1.25f : 1f);
                Vector3 desired = (dist > 1.7f ? toP * spd : Vector3.zero) + sep;
                vel = Vector3.Lerp(vel, desired, Mathf.Min(1f, 4f * dt));
                gp += vel * dt;
                gp.y = 0f;
                transform.position = gp;
                transform.LookAt(new Vector3(pp.x, gp.y, pp.z));
                if (dist < 2.6f && atkCd <= 0f && GameManager.I.player.alive)
                {
                    atkCd = 1.3f;
                    GameManager.I.player.Hurt((enraged ? 18f : 14f) + Random.value * 8f);
                    // lunge verso il giocatore
                    gp += toP * 0.3f;
                    transform.position = gp;
                }
            }
        }

        /// <summary>Monaco: si avvicina camminando, si ferma e pianta il bastone a terra; il danno e l'onda d'urto
        /// arrivano quando il bastone tocca terra (fotogramma 16 della clip, 0.53 s), solo se il giocatore e' a tiro.</summary>
        void UpdateMonaco(float dt, Vector3 gp, Vector3 pp, Vector3 toP, float dist, Vector3 sep, bool enraged)
        {
            atkLock -= dt;
            bool attacking = atkLock > 0f;
            float spd = speed * (enraged ? 1.25f : 1f);
            Vector3 desired = (!attacking && dist > 1.9f ? toP * spd : Vector3.zero) + sep * 0.5f;
            vel = Vector3.Lerp(vel, desired, Mathf.Min(1f, 3f * dt));
            gp += vel * dt;
            gp.y = 0f;
            transform.position = gp;
            Vector3 look = new Vector3(pp.x, gp.y, pp.z) - gp;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Mathf.Min(1f, 5f * dt));
            if (anim != null)
            {
                float v = new Vector3(vel.x, 0f, vel.z).magnitude;
                anim.SetFloat("Velocita", v);
                anim.SetFloat("PassoVel", Mathf.Clamp(v / MonacoWalkSpeed, 0.6f, 2.2f));
            }
            if (strikeAt >= 0f)
            {
                strikeAt -= dt;
                if (strikeAt < 0f)
                {
                    // punta del bastone nel riferimento del monaco (Blender 0.31, -0.37, 0 -> Unity -0.31, 0, 0.37)
                    if (glowMat != null) Shockwave.Spawn(transform.TransformPoint(new Vector3(-0.31f, 0f, 0.37f)), glowMat);
                    if (dist < 2.4f && GameManager.I.player.alive)
                        GameManager.I.player.Hurt((enraged ? 22f : 16f) + Random.value * 8f);
                    if (GameManager.autoTest) Debug.Log($"[PryingEyes] monaco: colpo a terra, giocatore a {dist:F1} m");
                }
            }
            else if (dist < 2.1f && atkCd <= 0f && GameManager.I.player.alive)
            {
                atkCd = 2.2f;
                strikeAt = 16f / 30f;
                atkLock = 1.25f;
                if (anim != null) anim.SetTrigger("Attacca");
            }
        }

        public void Damage(float dmg, Vector3 point, bool headshot)
        {
            if (dead) return;
            hp -= dmg;
            flash = 1f;
            foreach (var m in flashMats) m.SetColor("_EmissionColor", Color.white);
            GameManager.I.hud?.Hitmarker(hp <= 0f, headshot);
            GameManager.shotsHit++;
            Synth.Hit();
            vel += (transform.position - playerT.position).normalized *
                   (type == EnemyType.Drone ? 2.2f : type == EnemyType.Monaco ? 0.5f : 0.9f);
            // durante il colpo di bastone niente reazione: l'animazione dell'attacco non si interrompe
            if (type == EnemyType.Monaco && hp > 0f && atkLock <= 0f && anim != null) anim.SetTrigger("Colpito");
            if (hp <= 0f) Die(headshot);
        }

        void Die(bool headshot)
        {
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] morto {type} headshot={headshot}");
            all.Remove(this);
            GameManager.I.OnEnemyKilled(this, headshot);
            GameManager.I.SetAlive(all.Count);
            Synth.Kill();
            if (type == EnemyType.Monaco)
            {
                // niente detriti: crolla con l'animazione e sparisce dopo qualche secondo
                dead = true;
                foreach (var m in flashMats) m.SetColor("_EmissionColor", Color.black);
                if (anim != null) anim.SetBool("Morto", true);
                foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
                Destroy(gameObject, 5f);
                return;
            }
            Vector3 center = transform.position + Vector3.up * (type == EnemyType.Drone ? 0.5f : 1.2f);
            // i detriti usano il materiale dell'armatura: spegnere il lampo del colpo, sennò restano bianchi
            // incandescenti per tutta la loro vita (nessuno lo fa piu' sfumare, il nemico e' distrutto)
            foreach (var m in flashMats) m.SetColor("_EmissionColor", Color.black);
            if (flashMats.Count > 0)
            {
                var dm = flashMats[0];
                for (int i = 0; i < 6; i++)
                {
                    var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    d.transform.position = center + Random.insideUnitSphere * 0.5f;
                    d.transform.localScale = Vector3.one * Random.Range(0.1f, 0.28f);
                    d.GetComponent<MeshRenderer>().material = dm;
                    var rb = d.AddComponent<Rigidbody>();
                    rb.linearVelocity = new Vector3(Random.Range(-4f, 4f), Random.Range(3f, 7f), Random.Range(-4f, 4f));
                    rb.angularVelocity = Random.insideUnitSphere * 8f;
                    Destroy(d, 2.5f);
                }
            }
            if (glowMat != null) Shockwave.Spawn(transform.position, glowMat);
            Destroy(gameObject);
        }

        // --- procedural bodies ---
        static GameObject BuildDrone(out Material glow, out List<Material> flashes)
        {
            var g = new GameObject("Drone");
            var armor = Lit(new Color(0.22f, 0.25f, 0.31f), 0.9f, 0.3f);
            var dark = Lit(new Color(0.08f, 0.09f, 0.12f), 0.8f, 0.6f);
            var eye = Lit(new Color(0.05f, 0.05f, 0.08f), 0.6f, 0.4f);
            eye.EnableKeyword("_EMISSION");
            eye.SetColor("_EmissionColor", new Color(1f, 0.18f, 0.47f));
            eye.SetFloat("_EmissiveIntensity", 5f);

            AddPrim(g, PrimitiveType.Sphere, new Vector3(0.72f, 0.5f, 0.72f), Vector3.zero, armor, null, false, true);
            AddPrim(g, PrimitiveType.Sphere, new Vector3(0.22f, 0.22f, 0.22f), new Vector3(0, 0.02f, 0.32f), dark, "Head", true);
            AddPrim(g, PrimitiveType.Sphere, new Vector3(0.12f, 0.12f, 0.12f), new Vector3(0, 0.02f, 0.42f), eye, "Head", true);
            // 4 rotors
            foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                {
                    AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.07f, 0.1f, 0.07f), new Vector3(sx * 0.55f, 0.32f, sz * 0.55f), dark);
                    var disc = AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.6f, 0.02f, 0.6f), new Vector3(sx * 0.55f, 0.42f, sz * 0.55f), dark);
                    disc.name = "Rotor";
                }
            AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.02f, 0.3f, 0.14f), new Vector3(0, -0.42f, 0), dark).transform.Rotate(Mathf.PI, 0, 0);
            var rb = g.AddComponent<Rigidbody>();
            rb.useGravity = false; rb.isKinematic = true;
            glow = eye;
            flashes = new List<Material> { armor, dark };
            return g;
        }

        static GameObject BuildBrute(out Material glow, out List<Material> flashes)
        {
            var g = new GameObject("Brute");
            var armor = Lit(new Color(0.30f, 0.27f, 0.22f), 0.78f, 0.52f);
            var dark = Lit(new Color(0.11f, 0.12f, 0.15f), 0.85f, 0.55f);
            var visor = Lit(new Color(0.05f, 0.05f, 0.08f), 0.6f, 0.4f);
            visor.EnableKeyword("_EMISSION");
            visor.SetColor("_EmissionColor", new Color(1f, 0.18f, 0.47f));
            visor.SetFloat("_EmissiveIntensity", 4f);

            AddPrim(g, PrimitiveType.Cube, new Vector3(0.3f, 0.14f, 0.5f), new Vector3(-0.26f, 0.07f, -0.05f), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.3f, 0.14f, 0.5f), new Vector3(0.26f, 0.07f, -0.05f), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.24f, 0.55f, 0.28f), new Vector3(-0.26f, 0.42f, 0), armor);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.24f, 0.55f, 0.28f), new Vector3(0.26f, 0.42f, 0), armor);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.62f, 0.3f, 0.44f), new Vector3(0, 1.1f, 0), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.7f, 0.75f, 0.5f), new Vector3(0, 1.64f, 0), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.78f, 0.6f, 0.14f), new Vector3(0, 1.64f, 0.28f), armor);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.22f, 0.45f, 0.24f), new Vector3(-0.55f, 1.65f, 0), armor);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.22f, 0.45f, 0.24f), new Vector3(0.55f, 1.65f, 0), armor);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.4f, 0.32f, 0.4f), new Vector3(0, 2.1f, 0), dark, "Head", true);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.32f, 0.07f, 0.05f), new Vector3(0, 2.12f, 0.21f), visor, "Head", true);
            var cc = g.AddComponent<CapsuleCollider>();
            cc.height = 2.2f; cc.center = new Vector3(0, 1.1f, 0);
            glow = visor;
            flashes = new List<Material> { armor, dark };
            return g;
        }

        /// <summary>Prefab fatto da Editor/MonacoImport.cs (Resources/Monaco). I materiali del lampo sono copie
        /// per istanza, cosi' un monaco colpito non fa lampeggiare gli altri; occhio e occhietto restano fuori.</summary>
        static GameObject BuildMonaco(out Material glow, out List<Material> flashes)
        {
            var prefab = Resources.Load<GameObject>("Monaco");
            var g = Object.Instantiate(prefab);
            g.name = "Monaco";
            flashes = new List<Material>();
            foreach (var r in g.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (!m.name.StartsWith("Monaco_Occhi")) flashes.Add(m);
            // onda d'urto del colpo a terra: blu come l'occhietto
            glow = Lit(new Color(0.05f, 0.15f, 0.35f), 0f, 0.4f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(0.22f, 0.58f, 1f) * 3f);
            return g;
        }

        static Material Lit(Color c, float metallic, float roughness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", 1f - roughness);
            return m;
        }

        static GameObject AddPrim(GameObject parent, PrimitiveType p, Vector3 scale, Vector3 pos, Material m, string tag = null, bool head = false, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(p);
            if (!solid) Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().material = m;
            if (head)
            {
                go.tag = "Head";
                var sc = go.AddComponent<SphereCollider>();
                sc.isTrigger = false;
            }
            return go;
        }
    }
}
