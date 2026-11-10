using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace PryingEyes
{
    /// <summary>Occhio volante (modello da Blender, animato da EyeRig) / Abate (il monaco ingrandito, nero e oro) / Monaco
    /// (walker animato, modello da Blender): chase, attack, enrage <50%, separation, hit flash.
    /// Chi cammina usa la navmesh (aggira gli ostacoli, sale lo scalone); l'occhio segue lo stesso percorso in volo e
    /// non attraversa muri e soffitti. Si colpisce solo da vicino in 3D e con la linea libera: prima bastava la
    /// distanza orizzontale, e dal piano terra si colpiva chi stava sul soppalco.</summary>
    public partial class Enemy : MonoBehaviour
    {
        public enum EnemyType { Occhio, Abate, Monaco, Falena, Medusa, Carapace }
        public EnemyType type;

        public float hp, maxHp, speed;
        float atkCd = 0f, flash = 0f, repathT = 0f, phase;
        // monaco: animazioni guidate da qui (Velocita, PassoVel, Attacca, Colpito, Morto)
        Animator anim;
        bool dead;
        float strikeAt = -1f, atkLock = 0f, flinchT = 0f;
        // monaco: magia a distanza (carica nell'orbita del bastone, poi dardo)
        float castAt = -1f;
        GameObject castOrb;
        Light castLight;
        const float MonacoWalkSpeed = 1.0f;   // m/s della camminata in Blender a velocita' 1
        const float AbateAnimSpeed = 0.85f;   // l'abate usa le clip del monaco, un po' piu' lente
        const float AbateScale = 1.30f;       // scala orizzontale del prefab (AbateImport.Scale)
        const float MaxStepUp = 1.4f;         // dislivello massimo tra i piedi per colpire in mischia
        Vector3 vel;
        Transform playerT;
        NavMeshAgent agent;
        NavMeshPath path;
        readonly Vector3[] corners = new Vector3[32];
        int cornerCount;
        Renderer[] renderers;
        AudioSource loop;                     // litania del monaco / ronzio dell'occhio
        float stepT, basePitch = 1f;
        public List<Material> flashMats = new List<Material>();
        public Material glowMat;
        EyeRig eye;                           // occhio volante: palpebre, sguardo e tentacoli

        static readonly List<Enemy> all = new List<Enemy>();
        public static IReadOnlyList<Enemy> All => all;

        /// <summary>Dove mira il bot di prova: il centro dell'occhio (l'origine e' li'), il torace di chi cammina
        /// (l'origine e' ai piedi: prima mirava sempre 80 cm sopra l'origine e mancava l'occhio di mezzo metro).</summary>
        public Vector3 AimPoint => Archetype == EnemyType.Occhio
            ? transform.position
            : transform.position + Vector3.up * (Archetype == EnemyType.Abate ? 1.1f : 0.8f);

        /// <summary>Toglie tutti i nemici (cambio piano).</summary>
        public static void ClearAll()
        {
            foreach (var e in all) if (e != null) Destroy(e.gameObject);
            all.Clear();
        }

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

        public static Enemy Spawn(EnemyType t, Vector3 pos, Vector3? investigate = null)
        {
            Material glow;
            List<Material> flashes;
            EnemyType archetype = BaseType(t);
            GameObject go = t != archetype ? BuildAlien(t, out glow, out flashes)
                : t == EnemyType.Occhio ? BuildOcchio(out glow, out flashes)
                : t == EnemyType.Abate ? BuildAbate(out glow, out flashes) : BuildMonaco(out glow, out flashes);
            // chi cammina nasce sulla navmesh (lo spawn sullo scalone finiva dentro i gradini)
            if (archetype != EnemyType.Occhio && HotelNav.Ready && NavMesh.SamplePosition(pos, out NavMeshHit nh, 4f, NavMesh.AllAreas))
                pos = nh.position;
            go.transform.position = pos;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = HotelNav.EnemyLayer;
            Enemy e = go.AddComponent<Enemy>();
            e.type = t;
            e.hp = e.maxHp = t == EnemyType.Occhio ? 80f : t == EnemyType.Abate ? 320f : 200f;
            // passo da orda: nessuno corre, ma non si fermano e non ti lasciano il tempo di comprare con calma
            e.speed = t == EnemyType.Occhio ? 2.5f + Random.value * 0.8f
                : t == EnemyType.Abate ? 1.10f + Random.value * 0.25f   // pesante: lento e inesorabile
                : 1.30f + Random.value * 0.30f;      // ostinato
            e.phase = Random.value * 10f;
            e.anim = go.GetComponentInChildren<Animator>();
            if (t == EnemyType.Abate && e.anim != null) e.anim.speed = AbateAnimSpeed;
            e.playerT = GameManager.I.player.transform;
            e.renderers = go.GetComponentsInChildren<Renderer>();
            e.flashMats = flashes;
            e.glowMat = glow;
            foreach (var m in flashes) m.EnableKeyword("_EMISSION");
            e.eye = go.GetComponent<EyeRig>();
            if (e.eye != null) e.eye.lookAt = Stealth.Enabled ? null : GameManager.I.player.cam;
            if (archetype != EnemyType.Occhio && HotelNav.Ready) e.AddAgent();
            // suoni attaccati: il monaco lo senti prima di vederlo, l'occhio ha un ronzio umido (con effetto Doppler)
            e.basePitch = t == EnemyType.Monaco ? Random.Range(0.92f, 1.06f)
                : t == EnemyType.Abate ? Random.Range(0.58f, 0.66f)       // la litania dell'abate e' un'ottava sotto
                : Random.Range(0.9f, 1.1f);
            e.alienRig = go.GetComponent<AlienRig>();
            if (t != archetype) e.SetAlienStats();
            if (t == EnemyType.Carapace) e.loop = Sfx.Loop(go, SoundBank.EyeHum, 0.38f, 1.5f, 22f, e.basePitch);
            else if (archetype == EnemyType.Occhio) e.loop = Sfx.Loop(go, SoundBank.EyeHum, 0.30f, 1.5f, 24f, e.basePitch, 1f, 1f);
            else e.AttachChant();
            e.stepT = Random.value;
            all.Add(e);
            GameManager.I.SetAlive(all.Count);
            // furtivita': nei piani si nasce a pattugliare (o a controllare un punto); nella hall classica si caccia subito
            e.alert = Stealth.Enabled ? Alert.Patrol : Alert.Hunting;
            e.waitT = Random.Range(0.4f, 2.5f);
            if (investigate.HasValue) e.Investigate(investigate.Value);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] spawn {t} hp={e.hp} vel={e.speed:F2} pos={pos} nav={(e.agent != null && e.agent.isOnNavMesh)} stato={e.alert}");
            return e;
        }

        void AddAgent()
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = Archetype == EnemyType.Abate ? 0.5f : 0.35f;
            agent.height = Archetype == EnemyType.Abate ? 2.4f : 2f;
            agent.baseOffset = 0f;
            agent.speed = speed;
            agent.acceleration = Archetype == EnemyType.Abate ? 4f : 6f;
            agent.angularSpeed = 0f;
            agent.updateRotation = false;           // la rotazione la decide Face(): verso il giocatore da vicino
            agent.autoBraking = true;
            agent.stoppingDistance = Archetype == EnemyType.Abate ? 1.8f : 1.3f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.avoidancePriority = Random.Range(30, 70);
            if (!agent.isOnNavMesh) { Destroy(agent); agent = null; }
        }

        bool OnNav => agent != null && agent.enabled && agent.isOnNavMesh;

        /// <summary>Litania del monaco; quella dell'abate e' piu' forte e si sente da piu' lontano.</summary>
        void AttachChant()
        {
            var chant = SoundBank.Chant;
            if (chant == null) return;
            bool abate = Archetype == EnemyType.Abate;
            loop = Sfx.Loop(gameObject, chant, abate ? 0.75f : 0.55f, 1.5f, abate ? 30f : 22f, basePitch);
        }

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
            if (loop == null && (type == EnemyType.Monaco || type == EnemyType.Abate)) AttachChant();

            Vector3 gp = transform.position;
            Vector3 feet = playerT.position;
            // il corpo del giocatore e' dove sta davvero: con il lean la testa sporge dal riparo, il bacino no.
            // Se il petto e' coperto ma la testa si vede, si prende di mira la testa.
            var pl = GameManager.I.player;
            Vector3 chest = pl.LeanedPoint(1.3f);
            if (Mathf.Abs(pl.lean) > 0.05f)
            {
                Vector3 eyeE = gp + Vector3.up * (type == EnemyType.Carapace ? EyeHeight : 1.9f);
                if (!HotelNav.ClearLine(eyeE, chest))
                {
                    Vector3 headP = pl.LeanedPoint(1.58f);
                    if (HotelNav.ClearLine(eyeE, headP)) chest = headP;
                }
            }
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
            // furtivita' (Enemy.Stealth.cs): vista, udito, stato di allerta e dove andare
            SenseTick(dt, gp, pl);
            PlanMove(dt, gp, feet);
            if (Archetype == EnemyType.Occhio) UpdateOcchio(dt, gp, chest, sep, enraged);
            else if (Archetype == EnemyType.Monaco) UpdateMonaco(dt, gp, feet, chest, toP, hd, dy, sep, enraged);
            else UpdateAbate(dt, gp, feet, chest, toP, hd, dy, sep, enraged);
            if (alienRig != null) alienRig.Animate(phase, vel.magnitude, alert == Alert.Hunting, strikeAt >= 0f);
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
            float stopAt = Archetype == EnemyType.Abate ? 2.0f : 1.9f;
            Vector3 desired = (!hold && hd > stopAt ? toP * spd : Vector3.zero) + sep * (Archetype == EnemyType.Monaco ? 0.5f : 1f);
            vel = Vector3.Lerp(vel, desired, Mathf.Min(1f, 3f * dt));
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

        /// <summary>Abate: avanza lento e inesorabile e, a tiro, pianta il bastone-ostensorio a terra (la clip del monaco,
        /// un po' piu' lenta): il danno e l'onda d'urto arrivano quando il bastone tocca terra, solo se il giocatore e'
        /// a tiro. Niente magie, ma colpisce piu' forte e da piu' lontano. Incassa i proiettili senza barcollare a
        /// ogni colpo: una reazione al massimo ogni secondo e mezzo.</summary>
        void UpdateAbate(float dt, Vector3 gp, Vector3 feet, Vector3 chest, Vector3 toP, float hd, float dy, Vector3 sep, bool enraged)
        {
            atkLock -= dt;
            flinchT -= dt;
            bool attacking = atkLock > 0f;
            bool carapace = type == EnemyType.Carapace;
            Vector3 eye = gp + Vector3.up * (carapace ? EyeHeight : 1.9f);
            bool see = HotelNav.ClearLine(eye, chest);
            bool hunting = alert == Alert.Hunting;
            Vector3 toG = moveGoal - gp; toG.y = 0f;
            float hdG = toG.magnitude; if (hdG > 0.01f) toG /= hdG;
            Walk(dt, gp, moveGoal, toG, hdG, sep, speed * moveMul * (enraged ? 1.3f : 1f), attacking || moveHold);
            // a caccia guarda il giocatore solo se lo vede; arrivato dove l'ha perso si guarda intorno come in ricerca
            if (hunting && !scanning) Face(dt, transform.position, feet, hd, see && seeing, 4f); else FaceCalm(dt, hunting ? 4f : 3.2f);
            float v = new Vector3(vel.x, 0f, vel.z).magnitude;
            // la camminata va a passo con il terreno: la clip e' del monaco, qui scalata e rallentata
            float pv = Mathf.Clamp(v / (MonacoWalkSpeed * AbateScale * AbateAnimSpeed), 0.5f, 2.6f);
            if (anim != null)
            {
                anim.SetFloat("Velocita", v);
                anim.SetFloat("PassoVel", pv);
            }
            if (v > 0.25f)
            {
                stepT -= dt * pv * AbateAnimSpeed;
                if (stepT <= 0f)
                {
                    stepT = 1.05f;
                    Sfx.At(SoundBank.Tap, transform.TransformPoint(new Vector3(-0.31f, 0f, 0.37f)), 0.95f,
                           Random.Range(0.60f, 0.68f), 2f, 32f);
                }
            }
            if (strikeAt >= 0f)
            {
                strikeAt -= dt;
                if (strikeAt < 0f)
                {
                    // punta del bastone nel riferimento del monaco (Blender 0.31, -0.37, 0 -> Unity -0.31, 0, 0.37)
                    Vector3 tip = transform.TransformPoint(carapace ? new Vector3(0f, 0f, 0.68f) : new Vector3(-0.31f, 0f, 0.37f));
                    if (glowMat != null) Shockwave.Spawn(tip, glowMat);
                    Sfx.At(SoundBank.Strike, tip, 1f, Random.Range(0.70f, 0.78f), 3f, 42f);
                    Vector3 p = playerT.position;
                    float hd2 = new Vector2(p.x - tip.x, p.z - tip.z).magnitude;
                    float dy2 = Mathf.Abs(p.y - tip.y);
                    if (GameManager.I.player.alive && InReach(tip + Vector3.up * 0.6f, p + Vector3.up * 1.3f, hd2, dy2, 3.1f))
                        HitPlayer((enraged ? 28f : 22f) + Random.value * 8f, hd2, dy2);
                    else if (GameManager.autoTest) Debug.Log($"[PryingEyes] abate: colpo a vuoto, giocatore a {hd2:F1} m, dislivello {dy2:F1} m");
                }
            }
            else if (hunting && atkCd <= 0f && GameManager.I.player.alive && InReach(eye, chest, hd, dy, 2.8f))
            {
                atkCd = 2.5f;
                strikeAt = 16f / 30f / AbateAnimSpeed;
                atkLock = 1.5f;
                if (anim != null) anim.SetTrigger("Attacca");
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
            bool hunting = alert == Alert.Hunting;
            Vector3 toG = moveGoal - gp; toG.y = 0f;
            float hdG = toG.magnitude; if (hdG > 0.01f) toG /= hdG;
            Walk(dt, gp, moveGoal, toG, hdG, sep, speed * moveMul * (enraged ? 1.25f : 1f), attacking || casting || moveHold);
            if (hunting && !scanning) Face(dt, transform.position, feet, hd, see && seeing, 5f); else FaceCalm(dt, hunting ? 5f : 3.5f);
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
                    MagicBolt.Launch(tip, chest, 10f + Random.value * 5f);
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
                        HitPlayer((enraged ? 22f : 16f) + Random.value * 6f, hd2, dy2);
                    else if (GameManager.autoTest) Debug.Log($"[PryingEyes] monaco: colpo a vuoto, giocatore a {hd2:F1} m, dislivello {dy2:F1} m");
                }
            }
            else if (hunting && atkCd <= 0f && GameManager.I.player.alive && InReach(eye, chest, hd, dy, 2.1f))
            {
                atkCd = 1.9f;
                strikeAt = 16f / 30f;
                atkLock = 1.25f;
                if (anim != null) anim.SetTrigger("Attacca");
            }
            else if (hunting && seeing && atkCd <= 0f && GameManager.I.player.alive && see && hd > 3.0f && hd < 26f)
            {
                // a distanza il monaco diventa un mago: telegrafa la carica e lancia
                atkCd = 2.6f + Random.value * 0.8f;
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

        // ---------- occhio volante ----------
        /// <summary>Con la vista libera vola dritto verso il giocatore, all'altezza dei suoi occhi (ognuno a una quota un po'
        /// diversa); altrimenti segue il percorso della navmesh 2 m sopra terra (sale lungo lo scalone invece di restare
        /// sotto il soppalco). Scivola sui muri invece di attraversarli. Guarda sempre il giocatore; da vicino lo fissa
        /// (palpebre spalancate, iride accesa) e dopo mezzo secondo colpisce.</summary>
        void UpdateOcchio(float dt, Vector3 gp, Vector3 chest, Vector3 sep, bool enraged)
        {
            bool hunting = alert == Alert.Hunting;
            // a caccia insegue il giocatore (o l'ultimo punto in cui l'ha visto); altrimenti pattuglia a due metri da terra
            Vector3 aimAt = hunting && !seeing ? lastKnown + Vector3.up * 1.3f : chest;
            bool see = HotelNav.ClearLine(gp, aimAt);
            float bob = Mathf.Sin(Time.time * 1.7f + phase) * 0.22f;
            float height = 0.10f + (phase % 1f) * 0.55f;
            Vector3 target;
            if (hunting)
            {
                if (see || !HotelNav.Ready) target = aimAt + Vector3.up * (height + bob);
                else
                {
                    if (repathT <= 0f) { RePath(gp, seeing ? playerT.position : lastKnown); repathT = 0.5f; }
                    target = NextCorner(gp) + Vector3.up * (2.0f + bob);
                }
            }
            else
            {
                if (repathT <= 0f) { RePath(gp, moveGoal); repathT = 0.6f; }
                target = (HotelNav.Ready ? NextCorner(gp) : moveGoal) + Vector3.up * (2.0f + bob);
            }
            Vector3 to = target - gp;
            float d = to.magnitude;
            float spd = speed * moveMul * (enraged ? 1.2f : 1f);       // ferito si agita e accelera
            Vector3 desired = (d > 0.01f && !moveHold ? to / d * spd * Mathf.Clamp01(d / 1.2f) : Vector3.zero) + sep;
            vel = Vector3.Lerp(vel, desired, Mathf.Min(1f, 3f * dt));

            Vector3 step = vel * dt;
            float len = step.magnitude;
            const float R = 0.42f;
            if (len > 1e-4f && Physics.SphereCast(gp, R, step / len, out RaycastHit hit, len + 0.05f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
            {
                vel = Vector3.ProjectOnPlane(vel, hit.normal);
                step = vel * dt;
                len = step.magnitude;
                if (len > 1e-4f && Physics.SphereCast(gp, R, step / len, out hit, len + 0.05f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                    step = step / len * Mathf.Max(0f, hit.distance - 0.02f);
            }
            gp += step;
            float ceil = FloorDirectorCeiling(gp);
            gp.y = Mathf.Clamp(gp.y, 0.95f, ceil);
            transform.position = gp;
            // la direzione dello sguardo e' l'asse Z: verso la testa del giocatore, in 3D, con un po' di ritardo
            Vector3 look = playerT.position + Vector3.up * 1.6f - gp;
            if (hunting && look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-4.5f * dt));
            else if (!hunting) FaceCalm(dt, 2.6f);
            if (loop != null) loop.pitch = basePitch * (0.85f + vel.magnitude / 14f);   // accelera: il ronzio sale

            float dist3 = (chest - gp).magnitude;
            if (strikeAt >= 0f)
            {
                strikeAt -= dt;
                if (strikeAt < 0f && GameManager.I.player.alive && dist3 < 3.4f && HotelNav.ClearLine(gp, chest))
                    HitPlayer((type == EnemyType.Medusa ? 15f : 8f) + Random.value * 4f, dist3, Mathf.Abs(chest.y - gp.y));
            }
            else if (hunting && see && dist3 < 2.7f && atkCd <= 0f && GameManager.I.player.alive)
            {
                atkCd = type == EnemyType.Falena ? 1.3f : type == EnemyType.Medusa ? 2.6f : 1.8f;
                strikeAt = 0.5f;
                if (eye != null) eye.Stare(0.9f);
                Sfx.At(SoundBank.EyeStare, gp, 0.55f, Random.Range(0.92f, 1.08f), 2f, 22f);
            }
        }

        /// <summary>Quota massima di un occhio sopra il punto: nei piani procedurali il soffitto della stanza, altrimenti quello della hall.</summary>
        static float FloorDirectorCeiling(Vector3 p)
        {
            var d = Proc.FloorDirector.I;
            if (d == null || d.cur == null) return 8.3f;
            return Mathf.Max(1.3f, d.cur.CeilingAt(p) - 0.7f);
        }

        void RePath(Vector3 gp, Vector3 dest)
        {
            cornerCount = 0;
            pathDest = dest;
            if (!HotelNav.Ground(gp, out Vector3 from)) return;
            if (!NavMesh.SamplePosition(dest, out NavMeshHit th, 2.5f, NavMesh.AllAreas)) return;
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
            return cornerCount > 0 ? corners[cornerCount - 1] : pathDest;
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
            push = push.normalized * (Archetype == EnemyType.Occhio ? 2.2f : Archetype == EnemyType.Monaco ? 0.5f : 0.25f);
            if (OnNav) agent.velocity += push;
            else vel += push;
            // durante il colpo di bastone niente reazione: l'animazione dell'attacco non si interrompe
            if (Archetype == EnemyType.Monaco && hp > 0f && atkLock <= 0f && anim != null) anim.SetTrigger("Colpito");
            if (Archetype == EnemyType.Abate && hp > 0f && atkLock <= 0f && flinchT <= 0f && anim != null)
            {
                anim.SetTrigger("Colpito");
                flinchT = 1.5f;
            }
            if (Archetype == EnemyType.Occhio && eye != null) eye.Hit();
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
            if (alienRig != null) alienRig.Collapse();
            if (Archetype == EnemyType.Monaco || Archetype == EnemyType.Abate)
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
            if (Archetype == EnemyType.Occhio)
            {
                // cade flaccido: palpebre calate, iride spenta, tentacoli penzolanti; poi sparisce
                dead = true;
                if (eye != null) eye.Die();
                Sfx.At(SoundBank.EyeBurst, transform.position, 0.8f, basePitch, 2f, 30f);
                foreach (var m in flashMats) m.SetColor("_EmissionColor", Color.black);
                foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
                // il corpo continua a cadere come un oggetto fisico, ma i proiettili e il giocatore lo ignorano
                var body = GetComponent<SphereCollider>();
                var rb = GetComponent<Rigidbody>();
                if (body != null && rb != null)
                {
                    foreach (var tr in GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 2;
                    body.enabled = true;
                    var cc = GameManager.I.player.GetComponent<CharacterController>();
                    if (cc != null) Physics.IgnoreCollision(body, cc);
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.mass = 2f;
                    rb.linearDamping = 0.5f;
                    rb.angularDamping = 1.5f;
                    rb.linearVelocity = vel * 0.6f + Vector3.up * 0.8f;
                    rb.angularVelocity = Random.insideUnitSphere * 2.5f;
                }
                Destroy(gameObject, 7f);
                return;
            }
            Vector3 center = transform.position + Vector3.up * 1.2f;
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

        // --- corpi ---
        /// <summary>Prefab fatto da Editor/OcchioImport.cs (Resources/Occhio). Il lampo del colpo agisce sulla carne e
        /// sui bordi delle palpebre (copie per istanza); l'iride la gestisce EyeRig con la sua emissione.</summary>
        static GameObject BuildOcchio(out Material glow, out List<Material> flashes)
        {
            var prefab = Resources.Load<GameObject>("Occhio");
            var g = Object.Instantiate(prefab);
            g.name = "Occhio";
            flashes = new List<Material>();
            foreach (var r in g.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (m.name.StartsWith("Occhio_Carne") || m.name.StartsWith("Occhio_Bordo")) flashes.Add(m);
            // onda d'urto: ambra come l'iride
            glow = Lit(new Color(0.35f, 0.15f, 0.02f), 0f, 0.4f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.12f) * 3f);
            return g;
        }

        /// <summary>Prefab fatto da Editor/AbateImport.cs (Resources/Abate). Come il monaco: i materiali del lampo sono copie
        /// per istanza (occhio e occhietto restano fuori); onda d'urto del bastone in oro.</summary>
        static GameObject BuildAbate(out Material glow, out List<Material> flashes)
        {
            var prefab = Resources.Load<GameObject>("Abate");
            var g = Object.Instantiate(prefab);
            g.name = "Abate";
            flashes = new List<Material>();
            foreach (var r in g.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (!m.name.StartsWith("Abate_Occhi")) flashes.Add(m);
            glow = Lit(new Color(0.35f, 0.2f, 0.03f), 0f, 0.4f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(1f, 0.62f, 0.14f) * 3f);
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
    }
}
