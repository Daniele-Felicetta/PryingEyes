using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    /// <summary>Le varianti dell'orda (2026-11-15): il monaco e l'abate con un altro corpo e un'altra regola, perche' le ondate non siano
    /// tutte uguali. Stesso scheletro, stesse clip, stessa navigazione; cambiano il modello (vestito qui a runtime), i numeri e una
    /// meccanica ciascuno:
    ///  - Appestato: lento e gonfio di pustole verdi. Arrivato a due passi scoppia; scoppia anche quando muore. Lo scoppio ferisce chi e'
    ///    vicino, nemici compresi, e lascia una nube tossica per qualche secondo: sparargli quando e' in mezzo all'orda conviene.
    ///  - Flagellante: saio rosso scuro, veloce e fragile; da quattro-sei metri si lancia addosso al bersaglio.
    ///  - Statua: un monaco di pietra che si muove solo quando nessuno lo guarda, e allora corre. I colpi la scheggiano appena (40%),
    ///    lo sguardo la ferma: il gioco e' tenerla d'occhio mentre gli altri ti girano attorno.
    ///  - Cardinale: il capo delle ondate decime. Grosso, in porpora, con la barra della vita in alto; anello di fuoco attorno a se',
    ///    ventaglio di dardi, monaci richiamati. A meta' vita si infuria (piu' svelto, attacchi piu' frequenti). Lascia munizioni e 1000 punti.
    /// In cooperativa l'ospite decide (scoppi, danni, chi guarda la statua) e gli altri vedono gli effetti (Die, VariantPuppetTick).</summary>
    public partial class Enemy
    {
        public static bool IsVariant(EnemyType t) => t >= EnemyType.Appestato;
        public static bool IsBoss(EnemyType t) => t == EnemyType.Cardinale;
        /// <summary>Il capo vivo sul piano (uno alla volta), per la barra della vita dell'HUD.</summary>
        public static Enemy Boss { get; private set; }
        /// <summary>Il nome del capo nella barra in alto (null: il Cardinale delle ondate ottave).</summary>
        public static string BossTitle { get; private set; }

        /// <summary>Catacombe: questo nemico e' il boss del ramo. Vita da capo, la barra in alto con il suo nome; 'count' tinge di cremisi e nero il
        /// Cardinale che fa da Conte nel Guardaroba.</summary>
        public static void MakeBranchBoss(Enemy e, string title, float hp, bool count)
        {
            if (e == null) return;
            e.maxHp = Mathf.Max(e.maxHp, hp);
            e.hp = e.maxHp;
            Boss = e;
            BossTitle = title;
            if (count)
                foreach (var r in e.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.materials)
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * new Color(0.75f, 0.32f, 0.36f));
        }

        // stato delle varianti
        bool bursted;
        float leapT = -1f, leapCd;
        Vector3 leapDir;
        float watchT;
        bool watched;
        Light statueEye;
        AudioSource grind;
        float grindVol;
        float ringCd = 6f, volleyCd = 4f, summonCd = 14f, ringAt = -1f;
        bool enragedBoss;
        GameObject ringFx;

        const float BurstRadius = 3.6f;

        // ------------------------------------------------------------------ numeri
        void SetVariantStats()
        {
            switch (type)
            {
                case EnemyType.Appestato:
                    (maxHp, speed, basePitch) = (230f, Random.Range(0.95f, 1.10f), Random.Range(0.74f, 0.82f));
                    (meleeDmg, meleeCd, meleeReach, volley) = (0f, 9f, 0f, 0);
                    break;
                case EnemyType.Flagellante:
                    (maxHp, speed, basePitch) = (95f, Random.Range(2.35f, 2.75f), Random.Range(1.16f, 1.28f));
                    (meleeDmg, meleeCd, meleeReach, volley) = (11f, 1.05f, 2.0f, 0);
                    leapCd = Random.Range(0f, 0.35f);
                    break;
                case EnemyType.Statua:
                    (maxHp, speed, basePitch) = (720f, 5.2f, 0.5f);
                    (meleeDmg, meleeCd, meleeReach, volley) = (38f, 1.5f, 2.1f, 0);
                    break;
                case EnemyType.Cardinale:
                    int players = Mathf.Max(1, Targets.Count);
                    int round = Mathf.Max(1, Proc.RoundDirector.Round);
                    (maxHp, speed, basePitch) = (3200f + 1300f * (players - 1) + 120f * round, Random.Range(1.05f, 1.15f), 0.44f);
                    (meleeDmg, meleeCd, meleeReach, volley) = (30f, 2.0f, 3.1f, 0);
                    (boltDmg, castCd, castWind) = (9f, 5.5f, 1.0f);
                    break;
            }
            hp = maxHp;
        }

        /// <summary>Danno ricevuto: la pietra si scheggia appena.</summary>
        float VariantDamageScale(DamageSource src) =>
            type == EnemyType.Statua ? (src == DamageSource.Knife || src == DamageSource.Takedown ? 0.15f : 0.40f) : 1f;

        // ------------------------------------------------------------------ modelli
        /// <summary>Monaco o abate dal prefab, poi il vestito della variante: tinte per istanza, scala, pezzi aggiunti.</summary>
        static GameObject BuildVariant(EnemyType t, out Material glow, out List<Material> flashes)
        {
            bool heavy = t == EnemyType.Cardinale;
            var g = heavy ? BuildAbate(out glow, out flashes) : BuildMonaco(out glow, out flashes);
            g.name = t.ToString();
            switch (t)
            {
                case EnemyType.Appestato:
                    Tint(g, "Stoffa", new Color(0.55f, 0.62f, 0.38f), 0.85f);
                    Tint(g, "Pelle", new Color(0.72f, 0.86f, 0.62f), 1f);
                    Tint(g, "Volto", new Color(0.70f, 0.84f, 0.60f), 1f);
                    g.transform.localScale = Vector3.Scale(g.transform.localScale, new Vector3(1.16f, 1.0f, 1.16f));
                    AddPustules(g, out var pus);
                    flashes.Add(pus);
                    glow = EnemyMats.Glow(new Color(0.55f, 1f, 0.25f), 0.6f);
                    break;
                case EnemyType.Flagellante:
                    Tint(g, "Stoffa", new Color(0.62f, 0.13f, 0.10f), 0.9f);
                    Tint(g, "Fibre", new Color(0.62f, 0.13f, 0.10f), 0.9f);
                    Tint(g, "Pelle", new Color(1.0f, 0.86f, 0.82f), 1f);
                    g.transform.localScale = Vector3.Scale(g.transform.localScale, new Vector3(0.94f, 1.03f, 0.94f));
                    glow = EnemyMats.Glow(new Color(1f, 0.25f, 0.12f), 0.6f);
                    break;
                case EnemyType.Statua:
                    Petrify(g, flashes);
                    g.transform.localScale = g.transform.localScale * 1.08f;
                    glow = EnemyMats.Glow(new Color(0.9f, 0.85f, 0.75f), 0.4f);
                    break;
                case EnemyType.Cardinale:
                    // il saio dell'abate e' quasi nero: per la porpora serve un moltiplicatore forte (il monaco lavora gia' cosi', x5)
                    Tint(g, "Stoffa", new Color(4.6f, 0.34f, 0.30f), 1.05f);
                    Tint(g, "Sottoveste", new Color(0.95f, 0.92f, 0.86f), 1f);
                    g.transform.localScale = g.transform.localScale * 1.18f;
                    AddMitre(g, flashes);
                    glow = EnemyMats.Glow(new Color(1f, 0.32f, 0.10f), 0.7f);
                    break;
            }
            return g;
        }

        /// <summary>Moltiplica il colore dei materiali (gia' copie per istanza) il cui nome contiene 'part'.</summary>
        static void Tint(GameObject g, string part, Color c, float smooth)
        {
            foreach (var r in g.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    if (!m.name.Contains(part) || !m.HasProperty("_BaseColor")) continue;
                    var b = m.GetColor("_BaseColor");
                    m.SetColor("_BaseColor", new Color(b.r * c.r, b.g * c.g, b.b * c.b, b.a));
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", m.GetFloat("_Smoothness") * smooth);
                }
        }

        static Transform Bone(GameObject g, string name)
        {
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static Mesh lumpMesh;
        /// <summary>Sfera deformata (bubboni, pancia gonfia): una sola mesh condivisa.</summary>
        static Mesh Lump()
        {
            if (lumpMesh != null) return lumpMesh;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
            var m = Object.Instantiate(src);
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i];
                float n = Mathf.PerlinNoise(p.x * 3.1f + 4.2f, p.y * 3.1f + p.z * 2.3f) - 0.5f;
                v[i] = p * (1f + n * 0.35f);
            }
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.name = "Bubbone";
            return lumpMesh = m;
        }

        /// <summary>Pancia gonfia sotto il saio e bubboni verdi che pulsano di luce propria, sulla spina e sul petto.</summary>
        static void AddPustules(GameObject g, out Material pus)
        {
            var skin = Lit(new Color(0.34f, 0.40f, 0.26f), 0f, 0.45f);
            skin.name = "Appestato_Pelle";
            pus = Lit(new Color(0.30f, 0.45f, 0.10f), 0f, 0.25f);
            pus.name = "Appestato_Bubboni";
            pus.EnableKeyword("_EMISSION");
            pus.SetColor("_EmissionColor", new Color(0.45f, 1f, 0.18f) * 1.4f);
            pus.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var spine = Bone(g, "spina") ?? Bone(g, "petto") ?? g.transform;
            var chest = Bone(g, "petto") ?? spine;
            void Piece(Transform parent, Vector3 world, Vector3 size, Material m)
            {
                var go = new GameObject("bubbone");
                go.transform.SetParent(parent, true);
                go.transform.position = world;
                go.transform.localRotation = Random.rotation;
                var s = go.transform.lossyScale;
                go.transform.localScale = new Vector3(size.x / Mathf.Max(0.001f, s.x), size.y / Mathf.Max(0.001f, s.y), size.z / Mathf.Max(0.001f, s.z)) * 1f;
                go.AddComponent<MeshFilter>().sharedMesh = Lump();
                go.AddComponent<MeshRenderer>().sharedMaterial = m;
            }
            Vector3 fwd = g.transform.forward, up = Vector3.up, right = g.transform.right;
            Vector3 belly = g.transform.position + up * 1.02f + fwd * 0.15f;
            // tre rigonfiamenti che si accavallano (una sola sfera sembrava un pallone attaccato al saio)
            Piece(spine, belly, new Vector3(0.40f, 0.44f, 0.34f), skin);
            Piece(spine, belly + right * 0.13f + up * 0.10f - fwd * 0.03f, new Vector3(0.26f, 0.30f, 0.24f), skin);
            Piece(spine, belly - right * 0.15f - up * 0.08f - fwd * 0.02f, new Vector3(0.24f, 0.26f, 0.22f), skin);
            for (int i = 0; i < 9; i++)
            {
                float a = i * 2.39996f;
                Vector3 p = belly + right * (Mathf.Cos(a) * 0.18f) + up * (Mathf.Sin(a * 1.3f) * 0.20f) + fwd * (0.14f + 0.05f * (i % 2));
                float s = Random.Range(0.06f, 0.11f);
                Piece(spine, p, new Vector3(s, s, s), pus);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = g.transform.position + up * (1.25f + 0.07f * i) - fwd * 0.16f + right * (i % 2 == 0 ? -0.12f : 0.13f);
                float s = Random.Range(0.07f, 0.12f);
                Piece(chest, p, new Vector3(s, s, s), pus);
            }
        }

        /// <summary>Tutto in pietra: un materiale per istanza (lampeggia quando lo colpisci), occhi spenti.</summary>
        static void Petrify(GameObject g, List<Material> flashes)
        {
            Material stone;
            if (Proc.ProcKit.HasMat("pietra_scultura")) stone = new Material(Proc.ProcKit.Mat("pietra_scultura"));
            else stone = Lit(new Color(0.55f, 0.53f, 0.49f), 0f, 0.85f);
            stone.name = "Statua_Pietra";
            // grigio pietra scura, un filo verdastra (muschio): la pietra del kit e' quasi bianca e la statua si confondeva coi monaci
            if (stone.HasProperty("_BaseColor")) stone.SetColor("_BaseColor", stone.GetColor("_BaseColor") * new Color(0.50f, 0.52f, 0.47f));
            if (stone.HasProperty("_Smoothness")) stone.SetFloat("_Smoothness", 0.12f);
            stone.EnableKeyword("_EMISSION");
            stone.SetColor("_EmissionColor", Color.black);
            stone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            flashes.Clear();
            flashes.Add(stone);
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = stone;
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Mitria del cardinale: due lame di stoffa bianca e oro sopra la testa.</summary>
        static void AddMitre(GameObject g, List<Material> flashes)
        {
            var head = Bone(g, "testa");
            if (head == null) return;
            var cloth = Lit(new Color(0.86f, 0.82f, 0.74f), 0f, 0.6f);
            cloth.name = "Cardinale_Mitria";
            var gold = Lit(new Color(0.78f, 0.56f, 0.18f), 0.9f, 0.35f);
            gold.name = "Cardinale_Oro";
            flashes.Add(cloth);
            flashes.Add(gold);
            Vector3 top = head.position + Vector3.up * 0.30f;
            for (int i = 0; i < 2; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(go.GetComponent<Collider>());
                go.name = "mitria";
                go.transform.SetParent(head, true);
                go.transform.position = top + g.transform.forward * (i == 0 ? 0.035f : -0.035f) + Vector3.up * 0.06f;
                go.transform.rotation = g.transform.rotation * Quaternion.Euler(i == 0 ? -12f : 12f, 0f, 45f);
                var s = go.transform.lossyScale;
                go.transform.localScale = new Vector3(0.22f / s.x, 0.22f / s.y, 0.025f / s.z);
                go.GetComponent<MeshRenderer>().sharedMaterial = cloth;
            }
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(band.GetComponent<Collider>());
            band.name = "mitria_fascia";
            band.transform.SetParent(head, true);
            band.transform.position = top - Vector3.up * 0.05f;
            band.transform.rotation = g.transform.rotation;
            var bs = band.transform.lossyScale;
            band.transform.localScale = new Vector3(0.21f / bs.x, 0.025f / bs.y, 0.21f / bs.z);
            band.GetComponent<MeshRenderer>().sharedMaterial = gold;
        }

        // ------------------------------------------------------------------ comportamento
        /// <summary>Prima del passo normale del monaco o dell'abate. Vero = questo fotogramma e' gia' deciso (statua guardata).</summary>
        bool VariantTick(float dt, Vector3 gp, Vector3 chest, float hd, float dy)
        {
            switch (type)
            {
                case EnemyType.Appestato:
                    if (!bursted && alert == Alert.Hunting && tgt != null && tgt.Alive && hd < 1.9f && dy < 1.4f
                        && HotelNav.ClearLine(gp + Vector3.up * 1.2f, chest))
                    {
                        Burst(0);
                        return true;
                    }
                    return false;
                case EnemyType.Flagellante:
                    return FlagellantTick(dt, gp, chest, hd, dy);
                case EnemyType.Statua:
                    return StatueTick(dt, gp);
                case EnemyType.Cardinale:
                    CardinalTick(dt, gp, chest, hd);
                    return false;
            }
            return false;
        }

        /// <summary>Sugli ospitati: la statua si anima solo quando si muove davvero; il cardinale tiene aggiornata la barra.</summary>
        void VariantPuppetTick(float dt)
        {
            if (type == EnemyType.Statua && anim != null)
            {
                bool moving = new Vector3(vel.x, 0f, vel.z).sqrMagnitude > 0.04f;
                anim.speed = moving ? 1.55f : 0f;
                StatueLook(moving);
            }
            if (type == EnemyType.Cardinale && Boss == null) Boss = this;
        }

        // ---- flagellante: il balzo
        bool FlagellantTick(float dt, Vector3 gp, Vector3 chest, float hd, float dy)
        {
            leapCd -= dt;
            if (leapT >= 0f)
            {
                leapT -= dt;
                float k = Mathf.Clamp01(leapT / 0.32f);
                Vector3 step = leapDir * (9f * k + 2f) * dt;
                if (!Physics.CapsuleCast(gp + Vector3.up * 0.4f, gp + Vector3.up * 1.5f, 0.3f, leapDir, out _, step.magnitude + 0.05f,
                        HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                {
                    if (OnNav) { agent.Move(step); transform.position = agent.nextPosition; }
                    else transform.position += step;
                }
                vel = leapDir * 6f;
                if (anim != null) { anim.SetFloat("Velocita", 3.5f); anim.SetFloat("PassoVel", 2.2f); }
                if (leapT < 0f && tgt != null && tgt.Alive && hd < meleeReach + 0.6f && atkCd <= 0f) BeginMelee(meleeCd);
                return true;
            }
            if (alert == Alert.Hunting && leapCd <= 0f && strikeAt < 0f && atkLock <= 0f && tgt != null && tgt.Alive && hd > 2.6f && hd < 7.5f
                && dy < 0.8f && HotelNav.ClearLine(gp + Vector3.up * 1.4f, chest))
            {
                leapDir = tgt.Feet - gp; leapDir.y = 0f; leapDir.Normalize();
                transform.rotation = Quaternion.LookRotation(leapDir);
                leapT = 0.32f;
                leapCd = Random.Range(3.5f, 5.5f);
                Sfx.At(SoundBank.Whisper, gp + Vector3.up * 1.5f, 0.9f, 1.5f, 2f, 26f);
                Coop.Game?.EnemyEvent(this, EnemyEvt.Attack, Vector3.zero);
                return true;
            }
            return false;
        }

        // ---- statua: si muove solo quando nessuno la guarda
        bool StatueTick(float dt, Vector3 gp)
        {
            watchT -= dt;
            if (watchT <= 0f) { watchT = 0.08f; watched = AnyoneWatches(gp + Vector3.up * 1.2f); }
            if (watched)
            {
                if (OnNav) { agent.isStopped = true; agent.velocity = Vector3.zero; }
                vel = Vector3.zero;
                if (anim != null) anim.speed = 0f;
                StatueLook(false);
                return true;
            }
            if (OnNav) agent.isStopped = false;
            if (anim != null) anim.speed = 1.55f;
            StatueLook(true);
            return false;
        }

        /// <summary>Qualcuno la guarda: e' nel suo campo visivo (55 gradi dall'asse della visuale, quasi lo schermo intero) e niente in mezzo.</summary>
        static bool AnyoneWatches(Vector3 p)
        {
            for (int i = 0, n = Targets.Count; i < n; i++)
            {
                var t = Targets.At(i);
                if (t == null || !t.Alive || t.LookAt == null) continue;
                Vector3 eye = t.LookAt.position;
                Vector3 to = p - eye;
                float d = to.magnitude;
                if (d < 0.01f) return true;
                if (Vector3.Dot(t.LookAt.forward, to / d) < 0.57f) continue;       // ~55 gradi
                if (d > 60f) continue;
                if (HotelNav.ClearLine(eye, p)) return true;
            }
            return false;
        }

        /// <summary>Gli occhi della statua: brace rossa quando si muove, spenti quando la guardi; la pietra che gratta.</summary>
        void StatueLook(bool moving)
        {
            if (statueEye == null)
            {
                var head = Bone(gameObject, "testa");
                var go = new GameObject("OcchiStatua");
                go.transform.SetParent(head != null ? head : transform, false);
                go.transform.position = (head != null ? head.position : transform.position + Vector3.up * 1.55f) + transform.forward * 0.16f + Vector3.up * 0.04f;
                statueEye = go.AddComponent<Light>();
                statueEye.type = LightType.Point;
                statueEye.color = new Color(1f, 0.22f, 0.08f);
                statueEye.range = 2.2f;
                statueEye.shadows = LightShadows.None;
            }
            statueEye.intensity = Mathf.MoveTowards(statueEye.intensity, moving ? 2.2f : 0f, Time.deltaTime * 12f);
            if (grind == null)
            {
                grind = Sfx.Loop(gameObject, SoundBank.StoneGrind, 0.8f, 1.5f, 26f, 1f);
                if (grind == null) return;
                grindVol = grind.volume;           // volume gia' scalato dalle impostazioni degli effetti
                grind.volume = 0f;
            }
            grind.volume = Mathf.MoveTowards(grind.volume, moving ? grindVol : 0f, Time.deltaTime * Mathf.Max(0.01f, grindVol) * 8f);
        }

        // ---- appestato: lo scoppio
        /// <summary>Lo scoppio. Sull'ospite (o da solo) fa danno a giocatori e nemici e lascia la nube; ovunque fa vedere l'effetto.
        /// 'claimer' e' chi l'ha fatto scoppiare sparandogli (0 = questa macchina), -1 se e' scoppiato da solo vicino a te.</summary>
        void Burst(int claimer)
        {
            if (bursted) return;
            bursted = true;
            Vector3 c = transform.position + Vector3.up * 1.0f;
            Fx.PlagueBurst(c);
            Seats.ShakeAll(c, 0.8f, 10f);
            Proc.Barrels.I?.ExplodeNear(c, BurstRadius);
            Sfx.At(SoundBank.Burst, c, 1f, Random.Range(0.92f, 1.06f), 3f, 40f);
            if (!puppet)
            {
                for (int i = 0, n = Targets.Count; i < n; i++)
                {
                    var t = Targets.At(i);
                    if (t == null || !t.Alive) continue;
                    Vector3 body = t.Feet + Vector3.up * 1.0f;
                    float d = Vector3.Distance(body, c);
                    if (d > BurstRadius || !HotelNav.ClearLine(c, body)) continue;
                    t.Hurt(Mathf.Lerp(34f, 10f, d / BurstRadius), c);
                    Seats.CountDamageTaken(t);
                }
                NoteBlast(c);                                         // chi muore ora vola via dal centro (EnemyRagdoll)
                foreach (var o in all.ToArray())
                {
                    if (o == null || o == this || o.dead) continue;
                    Vector3 op = o.transform.position + Vector3.up * 1.0f;
                    float d = Vector3.Distance(op, c);
                    if (d > BurstRadius || !HotelNav.ClearLine(c, op)) continue;
                    o.Damage(Mathf.Lerp(320f, 90f, d / BurstRadius), op, false, claimer >= 0, DamageSource.Esplosione, Mathf.Max(0, claimer));
                }
                ToxicCloud.Spawn(transform.position, 6.5f, true);
                if (!dead)
                {
                    hp = 0f;
                    Die(false, claimer >= 0 ? DamageSource.Esplosione : DamageSource.Scoppio, Mathf.Max(0, claimer));
                }
            }
            else ToxicCloud.Spawn(transform.position, 6.5f, false);
        }

        // ---- cardinale: il capo
        void CardinalTick(float dt, Vector3 gp, Vector3 chest, float hd)
        {
            if (Boss == null || Boss.dead) Boss = this;
            if (!enragedBoss && hp < maxHp * 0.5f)
            {
                enragedBoss = true;
                speed *= 1.35f;
                if (anim != null) anim.speed = AbateAnimSpeed * 1.25f;
                Sfx.At(SoundBank.BossRoar, gp + Vector3.up * 2.5f, 1f, 0.8f, 4f, 70f);
                GameManager.I?.hud?.Toast("Il Cardinale si infuria");
            }
            float rate = enragedBoss ? 1.45f : 1f;
            ringCd -= dt * rate; volleyCd -= dt * rate; summonCd -= dt * rate;
            bool hunting = alert == Alert.Hunting && tgt != null && tgt.Alive;
            // anello di fuoco: telegrafato (cerchio che si accende ai piedi), poi l'onda che brucia chi e' entro 7 m e a vista
            if (ringAt >= 0f)
            {
                ringAt -= dt;
                if (ringFx != null)
                {
                    float k = 1f - Mathf.Clamp01(ringAt / 1.1f);
                    ringFx.transform.localScale = new Vector3(1.5f + k * 12.5f, 0.02f, 1.5f + k * 12.5f);
                }
                if (ringAt < 0f)
                {
                    if (ringFx != null) Destroy(ringFx);
                    if (glowMat != null) Shockwave.Spawn(gp, glowMat);
                    Fx.FireRing(gp, 7f);
                    Seats.ShakeAll(gp, 0.9f, 14f);
                    Sfx.At(SoundBank.Ignite, gp + Vector3.up, 1f, 0.55f, 4f, 50f);
                    for (int i = 0, n = Targets.Count; i < n; i++)
                    {
                        var t = Targets.At(i);
                        if (t == null || !t.Alive) continue;
                        Vector3 f = t.Feet;
                        float d = new Vector2(f.x - gp.x, f.z - gp.z).magnitude;
                        if (d > 7f || Mathf.Abs(f.y - gp.y) > 2.2f || !HotelNav.ClearLine(gp + Vector3.up, f + Vector3.up)) continue;
                        t.Hurt(enragedBoss ? 28f : 22f, gp);
                        Seats.CountDamageTaken(t);
                    }
                }
                return;
            }
            if (!hunting) return;
            if (ringCd <= 0f && hd < 8f)
            {
                ringCd = Random.Range(8f, 11f);
                ringAt = 1.1f;
                ringFx = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(ringFx.GetComponent<Collider>());
                ringFx.name = "AnelloDiFuoco";
                ringFx.transform.position = gp + Vector3.up * 0.04f;
                ringFx.transform.localScale = new Vector3(1.5f, 0.02f, 1.5f);
                if (glowMat != null) ringFx.GetComponent<MeshRenderer>().material = new Material(glowMat);
                Sfx.At(SoundBank.BossRoar, gp + Vector3.up * 2.5f, 0.8f, 1.0f, 4f, 60f);
                return;
            }
            if (volleyCd <= 0f && hd > 4f && hd < 30f && HotelNav.ClearLine(gp + Vector3.up * 2.4f, chest))
            {
                volleyCd = Random.Range(5f, 7f);
                volley = enragedBoss ? 7 : 5;
                LaunchBolts(gp + Vector3.up * 2.6f + transform.forward * 0.6f, chest, false);
                volley = 0;
                Sfx.At(SoundBank.Whisper, gp + Vector3.up * 2.5f, 1f, 0.5f, 3f, 40f);
                return;
            }
            if (summonCd <= 0f)
            {
                summonCd = Random.Range(18f, 24f);
                Proc.FloorDirector.I?.CallHelp(gp, enragedBoss ? 4 : 3);
                Sfx.At(SoundBank.Toll, gp + Vector3.up * 3f, 0.9f, 0.7f, 4f, 70f);
                GameManager.I?.hud?.Toast("Il Cardinale chiama i suoi monaci");
            }
        }

        /// <summary>Morte delle varianti (dopo quella normale del monaco/abate): l'appestato scoppia, il capo paga, la statua si spegne.</summary>
        void VariantDie(DamageSource src, int claimer)
        {
            switch (type)
            {
                case EnemyType.Appestato:
                    if (!bursted) Burst(src == DamageSource.Scoppio ? -1 : claimer);
                    break;
                case EnemyType.Statua:
                    if (statueEye != null) statueEye.intensity = 0f;
                    if (grind != null) grind.volume = 0f;
                    if (anim != null) anim.speed = 1f;
                    break;
                case EnemyType.Cardinale:
                    if (Boss == this) Boss = null;
                    if (ringFx != null) Destroy(ringFx);
                    Sfx.At(SoundBank.BossRoar, transform.position + Vector3.up * 2.5f, 1f, 0.6f, 4f, 80f);
                    if (!puppet)
                    {
                        // il premio: 1000 punti a chi l'ha abbattuto e munizioni per tutti
                        if (claimer > 0 && Coop.Game != null) Coop.Game.AwardPoints(claimer, 1000);
                        else GameManager.I.AddPoints(1000);
                        Drops.Spawn(DropKind.Ammo, transform.position + Vector3.up * 0.2f);
                        GameManager.I?.hud?.Toast("Il Cardinale e' caduto: +1000");
                    }
                    break;
            }
        }
    }
}
