using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace PryingEyes
{
    /// <summary>Drone (flying) / Brute (walker) / Monaco (walker animato, modello da Blender):
    /// chase, attack, enrage <50%, separation, hit flash.
    /// Chi cammina usa la navmesh (aggira gli ostacoli, sale lo scalone); il drone segue lo stesso percorso in volo e
    /// non attraversa muri e soffitti. Si colpisce solo da vicino in 3D e con la linea libera: prima bastava la
    /// distanza orizzontale, e dal piano terra si colpiva chi stava sul soppalco.</summary>
    public class Enemy : MonoBehaviour
    {
        public enum EnemyType { Drone, Brute, Monaco }
        public EnemyType type;

        public float hp, maxHp, speed;
        float atkCd = 0f, flash = 0f, repathT = 0f, phase;
        // monaco: animazioni guidate da qui (Velocita, PassoVel, Attacca, Colpito, Morto)
        Animator anim;
        bool dead;
        float strikeAt = -1f, atkLock = 0f;
        // monaco: magia a distanza (carica nell'orbita del bastone, poi dardo)
        float castAt = -1f;
        GameObject castOrb;
        Light castLight;
        const float MonacoWalkSpeed = 1.0f;   // m/s della camminata in Blender a velocita' 1
        const float MaxStepUp = 1.4f;         // dislivello massimo tra i piedi per colpire in mischia
        Vector3 vel;
        Transform playerT;
        NavMeshAgent agent;
        NavMeshPath path;
        readonly Vector3[] corners = new Vector3[32];
        int cornerCount;
        Renderer[] renderers;
        AudioSource loop;                     // litania del monaco / ronzio del drone
        float stepT, basePitch = 1f;
        public List<Material> flashMats = new List<Material>();
        public Material glowMat;
        List<Transform> rotors = new List<Transform>();

        static readonly List<Enemy> all = new List<Enemy>();
        public static IReadOnlyList<Enemy> All => all;

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
            EnemyMats.Ensure();
            Material glow;
            List<Material> flashes;
            GameObject go = t == EnemyType.Drone ? BuildDrone(out glow, out flashes)
                : t == EnemyType.Brute ? BuildBrute(out glow, out flashes)
                : BuildMonaco(out glow, out flashes);
            // chi cammina nasce sulla navmesh (lo spawn sullo scalone finiva dentro i gradini)
            if (t != EnemyType.Drone && HotelNav.Ready && NavMesh.SamplePosition(pos, out NavMeshHit nh, 4f, NavMesh.AllAreas))
                pos = nh.position;
            go.transform.position = pos;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = HotelNav.EnemyLayer;
            Enemy e = go.AddComponent<Enemy>();
            e.type = t;
            e.hp = e.maxHp = t == EnemyType.Drone ? 60f : t == EnemyType.Brute ? 130f : 170f;
            // passo da orda: nessuno corre, il giocatore ha il tempo di mirare e comprare
            e.speed = t == EnemyType.Drone ? 2.7f + Random.value * 0.8f
                : t == EnemyType.Brute ? 1.6f + Random.value * 0.45f
                : 1.20f + Random.value * 0.30f;      // lento e ostinato
            e.phase = Random.value * 10f;
            e.anim = go.GetComponentInChildren<Animator>();
            e.playerT = GameManager.I.player.transform;
            e.renderers = go.GetComponentsInChildren<Renderer>();
            e.flashMats = flashes;
            e.glowMat = glow;
            foreach (var m in flashes) m.EnableKeyword("_EMISSION");
            foreach (var tr in go.GetComponentsInChildren<Transform>())
                if (tr.name == "Rotor") e.rotors.Add(tr);
            if (t != EnemyType.Drone && HotelNav.Ready) e.AddAgent();
            // suoni attaccati: il monaco lo senti prima di vederlo, il drone ronza (con effetto Doppler)
            e.basePitch = t == EnemyType.Monaco ? Random.Range(0.92f, 1.06f) : Random.Range(0.9f, 1.1f);
            if (t == EnemyType.Monaco) e.loop = Sfx.Loop(go, SoundBank.Chant, 0.55f, 1.5f, 22f, e.basePitch);
            else if (t == EnemyType.Drone) e.loop = Sfx.Loop(go, SoundBank.Buzz, 0.32f, 1.5f, 24f, e.basePitch, 1f, 1f);
            e.stepT = Random.value;
            all.Add(e);
            GameManager.I.SetAlive(all.Count);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] spawn {t} hp={e.hp} vel={e.speed:F2} pos={pos} nav={(e.agent != null && e.agent.isOnNavMesh)}");
            return e;
        }

        void AddAgent()
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = type == EnemyType.Brute ? 0.45f : 0.35f;
            agent.height = 2f;
            agent.baseOffset = 0f;
            agent.speed = speed;
            agent.acceleration = type == EnemyType.Brute ? 14f : 6f;
            agent.angularSpeed = 0f;
            agent.updateRotation = false;           // la rotazione la decide Face(): verso il giocatore da vicino
            agent.autoBraking = true;
            agent.stoppingDistance = type == EnemyType.Brute ? 1.5f : 1.3f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.avoidancePriority = Random.Range(30, 70);
            if (!agent.isOnNavMesh) { Destroy(agent); agent = null; }
        }

        bool OnNav => agent != null && agent.enabled && agent.isOnNavMesh;

        void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;
            flash = Mathf.Max(0f, flash - dt * 5f);
            if (flash > 0f)
            {
                // mantiene la debole autoilluminazione di base sotto il lampo bianco del colpo
                var ec = new Color(0.035f, 0.035f, 0.04f) + Color.white * (flash * 0.95f);
                foreach (var m in flashMats) m.SetColor("_EmissionColor", ec);
            }
            atkCd -= dt;
            repathT -= dt;
            // la litania si calcola in un thread: il monaco nato prima che fosse pronta la attacca adesso
            if (loop == null && type == EnemyType.Monaco)
            {
                var chant = SoundBank.Chant;
                if (chant != null) loop = Sfx.Loop(gameObject, chant, 0.55f, 1.5f, 22f, basePitch);
            }

            Vector3 gp = transform.position;
            Vector3 feet = playerT.position;
            Vector3 chest = feet + Vector3.up * 1.3f;
            Vector3 toP = feet - gp; toP.y = 0f;
            float hd = toP.magnitude;                       // distanza orizzontale
            if (hd > 0.01f) toP /= hd;
            float dy = Mathf.Abs(feet.y - gp.y);            // dislivello tra i piedi (soppalco: 4.2 m)

            Vector3 sep = Vector3.zero;
            foreach (var o in all)
            {
                if (o == this) continue;
                Vector3 d = gp - o.transform.position; d.y = 0f;
                float l = d.magnitude;
                if (l < 2f && l > 0.01f) sep += d.normalized * (2f - l) * 1.5f;
            }

            bool enraged = hp < maxHp * 0.5f;
            if (type == EnemyType.Drone) UpdateDrone(dt, gp, chest, sep, enraged);
            else if (type == EnemyType.Monaco) UpdateMonaco(dt, gp, feet, chest, toP, hd, dy, sep, enraged);
            else UpdateBrute(dt, gp, feet, chest, toP, hd, dy, sep, enraged);
        }

        // ---------- chi cammina ----------
        /// <summary>Insegue il giocatore sulla navmesh; senza navmesh torna al vecchio inseguimento in linea retta.</summary>
        void Walk(float dt, Vector3 gp, Vector3 feet, Vector3 toP, float hd, Vector3 sep, float spd, bool hold)
        {
            if (OnNav)
            {
                agent.speed = spd;
                agent.isStopped = hold;
                if (hold) agent.velocity = Vector3.zero;
                else if (repathT <= 0f) { agent.SetDestination(feet); repathT = 0.25f; }
                vel = agent.velocity;
                return;
            }
            float stopAt = type == EnemyType.Brute ? 1.7f : 1.9f;
            Vector3 desired = (!hold && hd > stopAt ? toP * spd : Vector3.zero) + sep * (type == EnemyType.Monaco ? 0.5f : 1f);
            vel = Vector3.Lerp(vel, desired, Mathf.Min(1f, (type == EnemyType.Brute ? 4f : 3f) * dt));
            gp += vel * dt;
            gp.y = 0f;
            transform.position = gp;
        }

        /// <summary>Da vicino e con la vista libera guarda il giocatore, altrimenti guarda dove cammina.</summary>
        void Face(float dt, Vector3 gp, Vector3 feet, float hd, bool see, float turn)
        {
            Vector3 hv = new Vector3(vel.x, 0f, vel.z);
            Vector3 look = (see && hd < 4.5f) || hv.sqrMagnitude < 0.05f ? new Vector3(feet.x - gp.x, 0f, feet.z - gp.z) : hv;
            if (look.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Mathf.Min(1f, turn * dt));
        }

        /// <summary>Mischia valida: abbastanza vicino in orizzontale, quasi alla stessa quota e niente in mezzo.</summary>
        bool InReach(Vector3 from, Vector3 chest, float hd, float dy, float range) =>
            hd < range && dy < MaxStepUp && HotelNav.ClearLine(from, chest);

        void UpdateBrute(float dt, Vector3 gp, Vector3 feet, Vector3 chest, Vector3 toP, float hd, float dy, Vector3 sep, bool enraged)
        {
            Vector3 eye = gp + Vector3.up * 1.6f;
            bool see = hd < 6f && HotelNav.ClearLine(eye, chest);
            Walk(dt, gp, feet, toP, hd, sep, speed * (enraged ? 1.25f : 1f), false);
            Face(dt, transform.position, feet, hd, see, 10f);
            float v = new Vector3(vel.x, 0f, vel.z).magnitude;
            if (v > 0.5f)
            {
                stepT -= dt * v / 2f;
                if (stepT <= 0f)
                {
                    stepT = 0.5f;
                    Sfx.At(SoundBank.BruteStep, transform.position, 0.6f, Random.Range(0.9f, 1.1f), 2f, 26f);
                }
            }
            if (atkCd <= 0f && GameManager.I.player.alive && InReach(eye, chest, hd, dy, 2.4f))
            {
                atkCd = 2.1f;
                HitPlayer((enraged ? 14f : 10f) + Random.value * 5f, hd, dy);
                // lunge verso il giocatore
                if (OnNav) agent.Move(toP * 0.3f);
                else transform.position += toP * 0.3f;
            }
        }

        /// <summary>Monaco: si avvicina camminando, si ferma e pianta il bastone a terra; il danno e l'onda d'urto
        /// arrivano quando il bastone tocca terra (fotogramma 16 della clip, 0.53 s), solo se il giocatore e' a tiro.</summary>
        /// <summary>Sfera di carica sulla punta del bastone, creata alla prima magia.</summary>
        void EnsureCastOrb()
        {
            if (castOrb != null) return;
            var mat = EnemyMats.Glow(new Color(0.30f, 0.75f, 1f), 2.2f);
            castOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(castOrb.GetComponent<Collider>());
            castOrb.name = "SferaMagica";
            castOrb.transform.SetParent(transform, false);
            castOrb.transform.localPosition = new Vector3(-0.30f, 1.35f, 0.40f);
            castOrb.transform.localScale = Vector3.one * 0.001f;
            castOrb.GetComponent<MeshRenderer>().material = mat;
            castLight = castOrb.AddComponent<Light>();
            castLight.type = LightType.Point;
            castLight.color = new Color(0.35f, 0.8f, 1f);
            castLight.intensity = 0f;
            castLight.range = 5f;
            castLight.shadows = LightShadows.None;
            castOrb.SetActive(false);
        }

        void UpdateMonaco(float dt, Vector3 gp, Vector3 feet, Vector3 chest, Vector3 toP, float hd, float dy, Vector3 sep, bool enraged)
        {
            atkLock -= dt;
            bool casting = castAt >= 0f;
            bool attacking = atkLock > 0f;
            Vector3 eye = gp + Vector3.up * 1.5f;
            bool see = HotelNav.ClearLine(eye, chest);
            Walk(dt, gp, feet, toP, hd, sep, speed * (enraged ? 1.25f : 1f), attacking || casting);
            Face(dt, transform.position, feet, hd, see, 5f);
            float v = new Vector3(vel.x, 0f, vel.z).magnitude;
            if (anim != null)
            {
                anim.SetFloat("Velocita", v);
                anim.SetFloat("PassoVel", Mathf.Clamp(v / MonacoWalkSpeed, 0.6f, 2.2f));
            }
            // il bastone batte a terra mentre cammina: tok... tok...
            if (v > 0.25f)
            {
                stepT -= dt * Mathf.Clamp(v / MonacoWalkSpeed, 0.6f, 2.2f);
                if (stepT <= 0f)
                {
                    stepT = 1.05f;
                    Sfx.At(SoundBank.Tap, transform.TransformPoint(new Vector3(-0.31f, 0f, 0.37f)), 0.55f,
                           Random.Range(0.92f, 1.08f), 1.5f, 24f);
                }
            }
            if (castAt >= 0f)
            {
                // carica la sfera sul bastone, poi lancia il dardo verso il petto del giocatore
                castAt -= dt;
                float p = 1f - Mathf.Clamp01(castAt / 0.9f);
                if (castOrb != null)
                {
                    castOrb.SetActive(true);
                    castOrb.transform.localScale = Vector3.one * Mathf.Lerp(0.04f, 0.20f, p);
                    if (castLight != null) castLight.intensity = Mathf.Lerp(0.4f, 3.0f, p);
                }
                if (castAt <= 0f)
                {
                    Vector3 tip = castOrb != null
                        ? castOrb.transform.position
                        : transform.TransformPoint(new Vector3(-0.30f, 1.35f, 0.40f));
                    MagicBolt.Launch(tip, chest, 8f + Random.value * 4f);
                    if (castOrb != null) castOrb.SetActive(false);
                    if (castLight != null) castLight.intensity = 0f;
                    if (GameManager.autoTest) Debug.Log($"[PryingEyes] mago lancia magia da {hd:F1} m");
                }
            }
            else if (strikeAt >= 0f)
            {
                strikeAt -= dt;
                if (strikeAt < 0f)
                {
                    // punta del bastone nel riferimento del monaco (Blender 0.31, -0.37, 0 -> Unity -0.31, 0, 0.37)
                    Vector3 tip = transform.TransformPoint(new Vector3(-0.31f, 0f, 0.37f));
                    if (glowMat != null) Shockwave.Spawn(tip, glowMat);
                    Sfx.At(SoundBank.Strike, tip, 0.85f, Random.Range(0.95f, 1.05f), 3f, 35f);
                    Vector3 p = playerT.position;
                    float hd2 = new Vector2(p.x - tip.x, p.z - tip.z).magnitude;
                    float dy2 = Mathf.Abs(p.y - tip.y);
                    if (GameManager.I.player.alive && InReach(tip + Vector3.up * 0.6f, p + Vector3.up * 1.3f, hd2, dy2, 2.4f))
                        HitPlayer((enraged ? 18f : 13f) + Random.value * 6f, hd2, dy2);
                    else if (GameManager.autoTest) Debug.Log($"[PryingEyes] monaco: colpo a vuoto, giocatore a {hd2:F1} m, dislivello {dy2:F1} m");
                }
            }
            else if (atkCd <= 0f && GameManager.I.player.alive && InReach(eye, chest, hd, dy, 2.1f))
            {
                atkCd = 2.2f;
                strikeAt = 16f / 30f;
                atkLock = 1.25f;
                if (anim != null) anim.SetTrigger("Attacca");
            }
            else if (atkCd <= 0f && GameManager.I.player.alive && see && hd > 3.0f && hd < 26f)
            {
                // a distanza il monaco diventa un mago: telegrafa la carica e lancia
                atkCd = 3.2f + Random.value * 0.8f;
                castAt = 0.9f;
                atkLock = 1.4f;
                EnsureCastOrb();
                castOrb.SetActive(true);
                if (castLight != null) castLight.intensity = 0.2f;
                if (anim != null) anim.SetTrigger("Attacca");
                Sfx.At(SoundBank.Whisper, castOrb.transform.position, 0.5f,
                       Random.Range(0.85f, 1.05f), 2f, 24f);
            }
        }

        // ---------- drone ----------
        /// <summary>Con la vista libera vola dritto verso il giocatore, poco sopra la testa; altrimenti segue il percorso
        /// della navmesh 2 m sopra terra (sale lungo lo scalone invece di restare sotto il soppalco). Scivola sui muri
        /// invece di attraversarli.</summary>
        void UpdateDrone(float dt, Vector3 gp, Vector3 chest, Vector3 sep, bool enraged)
        {
            bool see = HotelNav.ClearLine(gp, chest);
            float bob = Mathf.Sin(Time.time * 2f + phase) * 0.35f;
            Vector3 target;
            if (see || !HotelNav.Ready) target = chest + Vector3.up * (0.9f + bob);
            else
            {
                if (repathT <= 0f) { RePath(gp); repathT = 0.5f; }
                target = NextCorner(gp) + Vector3.up * (2.2f + bob);
            }
            Vector3 to = target - gp;
            float d = to.magnitude;
            float spd = speed * (enraged ? 0.8f : 1f);
            Vector3 desired = (d > 0.01f ? to / d * spd * Mathf.Clamp01(d / 1.2f) : Vector3.zero) + sep;
            vel = Vector3.Lerp(vel, desired, Mathf.Min(1f, 3f * dt));

            Vector3 step = vel * dt;
            float len = step.magnitude;
            const float R = 0.45f;
            if (len > 1e-4f && Physics.SphereCast(gp, R, step / len, out RaycastHit hit, len + 0.05f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
            {
                vel = Vector3.ProjectOnPlane(vel, hit.normal);
                step = vel * dt;
                len = step.magnitude;
                if (len > 1e-4f && Physics.SphereCast(gp, R, step / len, out hit, len + 0.05f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                    step = step / len * Mathf.Max(0f, hit.distance - 0.02f);
            }
            gp += step;
            gp.y = Mathf.Clamp(gp.y, 0.9f, 8.3f);
            transform.position = gp;
            Vector3 look = new Vector3(chest.x - gp.x, 0f, chest.z - gp.z);
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
            foreach (var r in rotors) r.Rotate(Vector3.up, 1200f * dt, Space.Self);
            if (loop != null) loop.pitch = basePitch * (0.85f + vel.magnitude / 14f);   // accelera: il ronzio sale

            float dist3 = (chest - gp).magnitude;
            if (see && dist3 < 2.6f && atkCd <= 0f && GameManager.I.player.alive)
            {
                atkCd = 1.9f;
                HitPlayer(5f + Random.value * 4f, dist3, Mathf.Abs(chest.y - gp.y));
            }
        }

        void RePath(Vector3 gp)
        {
            cornerCount = 0;
            if (!HotelNav.Ground(gp, out Vector3 from)) return;
            if (!NavMesh.SamplePosition(playerT.position, out NavMeshHit th, 2.5f, NavMesh.AllAreas)) return;
            if (path == null) path = new NavMeshPath();
            if (NavMesh.CalculatePath(from, th.position, NavMesh.AllAreas, path))
                cornerCount = path.GetCornersNonAlloc(corners);
        }

        Vector3 NextCorner(Vector3 gp)
        {
            for (int i = 0; i < cornerCount; i++)
            {
                Vector3 c = corners[i];
                if (new Vector2(c.x - gp.x, c.z - gp.z).sqrMagnitude > 1.5f * 1.5f) return c;
            }
            return cornerCount > 0 ? corners[cornerCount - 1] : playerT.position;
        }

        void HitPlayer(float dmg, float dist, float dy)
        {
            GameManager.I.player.Hurt(dmg);
            GameManager.damageTaken++;
            if (GameManager.autoTest)
                Debug.Log($"[PryingEyes] danno {type} {dmg:F0} a {dist:F1} m, dislivello {dy:F1} m, nemico y={transform.position.y:F1}");
        }

        public void Damage(float dmg, Vector3 point, bool headshot, bool awardPoints = true)
        {
            if (dead) return;
            if (awardPoints) GameManager.I.AddPoints(10);   // +10 per colpo a segno
            hp -= dmg;
            flash = 1f;
            foreach (var m in flashMats) m.SetColor("_EmissionColor", Color.white);
            GameManager.I.hud?.Hitmarker(hp <= 0f, headshot);
            GameManager.shotsHit++;
            Synth.Hit();
            Vector3 push = (transform.position - playerT.position);
            push.y = 0f;
            push = push.normalized * (type == EnemyType.Drone ? 2.2f : type == EnemyType.Monaco ? 0.5f : 0.9f);
            if (OnNav) agent.velocity += push;
            else vel += push;
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
            if (agent != null) agent.enabled = false;
            if (loop != null) loop.Stop();
            if (type == EnemyType.Monaco)
            {
                // niente detriti: crolla con l'animazione e sparisce dopo qualche secondo
                dead = true;
                Sfx.At(SoundBank.MonkDeath, transform.position + Vector3.up, 0.8f, basePitch, 2f, 30f);
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
                    d.layer = HotelNav.EnemyLayer;    // i detriti non bloccano la vista degli altri nemici
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
            // composito quasi nero, pannelli più chiari, emissioni smorzate
            var armor = EnemyMats.Drone(new Color(0.95f, 0.98f, 1.05f));
            var dark = EnemyMats.Drone(new Color(0.42f, 0.45f, 0.52f));
            var eye = EnemyMats.Glow(new Color(1f, 0.12f, 0.30f), 1.0f);
            var belly = EnemyMats.Glow(new Color(1f, 0.38f, 0.10f), 0.5f);

            // scafo corazzato e piastre
            AddPrim(g, PrimitiveType.Sphere, new Vector3(0.78f, 0.52f, 0.78f), Vector3.zero, armor, null, false, true);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.58f, 0.12f, 0.72f), new Vector3(0, 0.21f, 0), armor);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.50f, 0.08f, 0.62f), new Vector3(0, -0.23f, 0), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.10f, 0.30f, 0.50f), new Vector3(-0.43f, 0.02f, 0), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.10f, 0.30f, 0.50f), new Vector3(0.43f, 0.02f, 0), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.44f, 0.14f, 0.16f), new Vector3(0, 0.11f, 0.33f), armor);

            // gimbal e sensore
            AddPrim(g, PrimitiveType.Sphere, new Vector3(0.24f, 0.24f, 0.24f), new Vector3(0, 0.01f, 0.35f), dark);
            AddPrim(g, PrimitiveType.Sphere, new Vector3(0.11f, 0.11f, 0.11f), new Vector3(0, 0.01f, 0.47f), eye, "Head", true);
            var iris = AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.30f, 0.015f, 0.30f), new Vector3(0, 0.01f, 0.45f), armor);
            iris.transform.Rotate(90f, 0f, 0f);

            // antenna
            AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.014f, 0.34f, 0.014f), new Vector3(0.18f, 0.45f, 0.02f), dark);
            AddPrim(g, PrimitiveType.Sphere, new Vector3(0.032f, 0.032f, 0.032f), new Vector3(0.18f, 0.62f, 0.02f), eye);

            // bracci, motori, rotori e gabbie di protezione
            foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                {
                    var arm = AddPrim(g, PrimitiveType.Cube, new Vector3(0.07f, 0.05f, 0.62f),
                        new Vector3(sx * 0.30f, 0.17f, sz * 0.30f), armor);
                    arm.transform.Rotate(0f, Mathf.Atan2(sx, sz) * Mathf.Rad2Deg, 0f);

                    AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.075f, 0.11f, 0.075f),
                        new Vector3(sx * 0.58f, 0.31f, sz * 0.58f), dark);
                    var disc = AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.60f, 0.016f, 0.60f),
                        new Vector3(sx * 0.58f, 0.42f, sz * 0.58f), dark);
                    disc.name = "Rotor";

                    AddPrim(g, PrimitiveType.Cube, new Vector3(0.76f, 0.014f, 0.03f),
                        new Vector3(sx * 0.58f, 0.38f, sz * 0.58f), armor);
                    AddPrim(g, PrimitiveType.Cube, new Vector3(0.03f, 0.014f, 0.76f),
                        new Vector3(sx * 0.58f, 0.38f, sz * 0.58f), armor);
                }

            // pattini di atterraggio
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.07f, 0.03f, 0.72f), new Vector3(-0.24f, -0.45f, 0), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.07f, 0.03f, 0.72f), new Vector3(0.24f, -0.45f, 0), dark);
            for (int i = -1; i <= 1; i += 2)
            {
                AddPrim(g, PrimitiveType.Cube, new Vector3(0.10f, 0.10f, 0.05f), new Vector3(-0.24f, -0.39f, i * 0.28f), dark);
                AddPrim(g, PrimitiveType.Cube, new Vector3(0.10f, 0.10f, 0.05f), new Vector3(0.24f, -0.39f, i * 0.28f), dark);
            }

            // pungiglione e luce ventrale
            AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.02f, 0.30f, 0.14f), new Vector3(0, -0.42f, 0), dark).transform.Rotate(Mathf.PI, 0f, 0f);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.22f, 0.03f, 0.12f), new Vector3(0, -0.29f, 0.13f), belly);

            // strisce di stato e luce del sensore: definiscono lo scafo nel buio
            var strip = EnemyMats.Glow(new Color(1f, 0.10f, 0.22f), 0.55f);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.018f, 0.018f, 0.30f), new Vector3(-0.445f, 0.10f, 0), strip);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.018f, 0.018f, 0.30f), new Vector3(0.445f, 0.10f, 0), strip);
            var droneEyeLight = new GameObject("LuceSensore").AddComponent<Light>();
            droneEyeLight.transform.SetParent(g.transform, false);
            droneEyeLight.transform.localPosition = new Vector3(0, 0.01f, 0.52f);
            droneEyeLight.type = LightType.Point;
            droneEyeLight.color = new Color(1f, 0.15f, 0.35f);
            droneEyeLight.intensity = 0.55f;
            droneEyeLight.range = 2.2f;
            droneEyeLight.shadows = LightShadows.None;

            var rb = g.AddComponent<Rigidbody>();
            rb.useGravity = false; rb.isKinematic = true;
            glow = eye;
            flashes = new List<Material> { armor, dark };
            return g;
        }

        static GameObject BuildBrute(out Material glow, out List<Material> flashes)
        {
            var g = new GameObject("Brute");
            // ferro scuro arrugginito, visore ambra smorzato: più tetro, meno "giocattolo"
            var armor = EnemyMats.Armor(new Color(1.0f, 0.95f, 0.88f));
            var dark = EnemyMats.Armor(new Color(0.46f, 0.46f, 0.44f));
            var visor = EnemyMats.Glow(new Color(1f, 0.28f, 0.06f), 0.9f);

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
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.27f, 0.052f, 0.05f), new Vector3(0, 2.12f, 0.21f), visor, "Head", true);

            // piastra pettorale, addome e schiena
            var chest = AddPrim(g, PrimitiveType.Cube, new Vector3(0.62f, 0.48f, 0.10f), new Vector3(0, 1.68f, 0.33f), armor);
            chest.transform.Rotate(7f, 0f, 0f);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.46f, 0.12f, 0.34f), new Vector3(0, 1.37f, 0.10f), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.42f, 0.10f, 0.34f), new Vector3(0, 1.24f, 0.10f), dark);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.56f, 0.48f, 0.10f), new Vector3(0, 1.62f, -0.30f), armor);
            for (int i = -1; i <= 1; i += 2)
                AddPrim(g, PrimitiveType.Cube, new Vector3(0.16f, 0.06f, 0.04f), new Vector3(i * 0.20f, 1.70f, -0.36f), dark);

            // spallacci a strati
            var pauldronL = AddPrim(g, PrimitiveType.Cube, new Vector3(0.34f, 0.12f, 0.44f), new Vector3(-0.58f, 1.92f, 0), armor);
            pauldronL.transform.Rotate(0f, 0f, 10f);
            var pauldronR = AddPrim(g, PrimitiveType.Cube, new Vector3(0.34f, 0.12f, 0.44f), new Vector3(0.58f, 1.92f, 0), armor);
            pauldronR.transform.Rotate(0f, 0f, -10f);

            // pistoni alle ginocchia e ai gomiti
            for (int i = -1; i <= 1; i += 2)
            {
                AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.055f, 0.30f, 0.055f), new Vector3(i * 0.26f, 0.56f, 0.20f), dark);
                AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.035f, 0.24f, 0.035f), new Vector3(i * 0.58f, 1.36f, 0.12f), dark);
            }

            // cavi dorsali e cresta dell'elmetto
            for (int i = -1; i <= 1; i += 2)
            {
                var cable = AddPrim(g, PrimitiveType.Cylinder, new Vector3(0.018f, 0.42f, 0.018f),
                    new Vector3(i * 0.16f, 1.72f, -0.22f), dark);
                cable.transform.Rotate(0f, 0f, i * 12f);
            }
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.06f, 0.10f, 0.42f), new Vector3(0, 2.30f, -0.02f), armor);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.06f, 0.035f, 0.035f), new Vector3(-0.12f, 2.14f, 0.215f), visor, "Head", true);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.06f, 0.035f, 0.035f), new Vector3(0.12f, 2.14f, 0.215f), visor, "Head", true);

            // fessura luminosa sul petto e luce del visore
            var seam = EnemyMats.Glow(new Color(1f, 0.30f, 0.07f), 0.5f);
            AddPrim(g, PrimitiveType.Cube, new Vector3(0.20f, 0.018f, 0.02f), new Vector3(0, 1.52f, 0.40f), seam);
            var visorLight = new GameObject("LuceVisore").AddComponent<Light>();
            visorLight.transform.SetParent(g.transform, false);
            visorLight.transform.localPosition = new Vector3(0, 2.06f, 0.36f);
            visorLight.type = LightType.Point;
            visorLight.color = new Color(1f, 0.35f, 0.10f);
            visorLight.intensity = 0.50f;
            visorLight.range = 2.5f;
            visorLight.shadows = LightShadows.None;
            // debole bagliore del reattore: fa leggere le piastre nel buio
            var coreLight = new GameObject("LuceNucleo").AddComponent<Light>();
            coreLight.transform.SetParent(g.transform, false);
            coreLight.transform.localPosition = new Vector3(0, 1.55f, 0.52f);
            coreLight.type = LightType.Point;
            coreLight.color = new Color(1f, 0.38f, 0.12f);
            coreLight.intensity = 0.32f;
            coreLight.range = 2.6f;
            coreLight.shadows = LightShadows.None;

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
