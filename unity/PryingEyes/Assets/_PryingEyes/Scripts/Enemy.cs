using UnityEngine;
using System.Collections.Generic;

namespace PryingEyes
{
    /// <summary>Drone (flying) / Brute (walker): chase, attack, enrage <50%, separation, hit flash.</summary>
    public class Enemy : MonoBehaviour
    {
        public enum EnemyType { Drone, Brute }
        public EnemyType type;

        public float hp, maxHp, speed;
        float atkCd = 0f, flash = 0f;
        Vector3 vel;
        Transform playerT;
        Renderer[] renderers;

        static readonly List<Enemy> all = new List<Enemy>();

        public static Enemy Spawn(EnemyType t, Vector3 pos)
        {
            GameObject go = t == EnemyType.Drone ? BuildDrone() : BuildBrute();
            go.transform.position = pos;
            Enemy e = go.AddComponent<Enemy>();
            e.type = t;
            e.hp = e.maxHp = t == EnemyType.Drone ? 60f : 130f;
            e.speed = t == EnemyType.Drone ? 4.2f + Random.value * 1.5f : 2.6f + Random.value;
            e.playerT = GameManager.I.player.transform;
            e.renderers = go.GetComponentsInChildren<Renderer>();
            all.Add(e);
            GameManager.I.SetAlive(all.Count);
            return e;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            flash = Mathf.Max(0f, flash - dt * 5f);
            foreach (var r in renderers)
            {
                foreach (var m in r.materials)
                    if (m.HasProperty("_EmissiveIntensity"))
                        m.SetFloat("_EmissiveIntensity", m.GetFloat("_EmissiveIntensity") + 0f); // keep
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
                if (dist < 2.6f && atkCd <= 0f && GameManager.I.player.alive)
                {
                    atkCd = 1.1f;
                    GameManager.I.player.Hurt(8f + Random.value * 6f);
                }
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
                if (dist < 2.2f && atkCd <= 0f && GameManager.I.player.alive)
                {
                    atkCd = 1.3f;
                    GameManager.I.player.Hurt((enraged ? 18f : 14f) + Random.value * 8f);
                }
            }
        }

        public void Damage(float dmg, Vector3 point, bool headshot)
        {
            hp -= dmg;
            flash = 1f;
            GameManager.I.hud?.Hitmarker(headshot);
            vel += (transform.position - playerT.position).normalized * (type == EnemyType.Drone ? 2.2f : 0.9f);
            if (hp <= 0f) Die(headshot);
        }

        void Die(bool headshot)
        {
            all.Remove(this);
            GameManager.I.OnEnemyKilled(this, headshot);
            GameManager.I.SetAlive(all.Count);
            Destroy(gameObject);
        }

        // --- procedural bodies ---
        static GameObject BuildDrone()
        {
            var g = new GameObject("Drone");
            var armor = Lit(new Color(0.22f, 0.25f, 0.31f), 0.9f, 0.3f);
            var dark = Lit(new Color(0.08f, 0.09f, 0.12f), 0.8f, 0.6f);
            var eye = Lit(new Color(0.05f, 0.05f, 0.08f), 0.6f, 0.4f);
            eye.EnableKeyword("_EMISSION");
            eye.SetColor("_EmissionColor", new Color(1f, 0.18f, 0.47f));
            eye.SetFloat("_EmissiveIntensity", 5f);

            AddPrim(g, PrimitiveType.Sphere, new Vector3(0.72f, 0.5f, 0.72f), Vector3.zero, armor);
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
            return g;
        }

        static GameObject BuildBrute()
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

        static GameObject AddPrim(GameObject parent, PrimitiveType p, Vector3 scale, Vector3 pos, Material m, string tag = null, bool head = false)
        {
            var go = GameObject.CreatePrimitive(p);
            Destroy(go.GetComponent<Collider>());
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
