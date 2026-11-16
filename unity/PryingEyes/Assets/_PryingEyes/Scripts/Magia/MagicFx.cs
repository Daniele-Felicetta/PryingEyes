using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    /// <summary>Gli effetti visivi delle magie e dei sigilli: fiamme, scintille e archi, brina, sangue, icore, lampi di luce. Tutto in
    /// coordinate del mondo: una fiamma nel palmo si emette dove sta il palmo a ogni fotogramma e lascia la scia se la mano si muove.
    /// Dal 2026-11-16 passano da MagicVfx (particelle vere, fuoco e fumo animati, fulmini ramificati); senza il suo shader restano i
    /// quad di Fx di prima.</summary>
    public static class MagicFx
    {
        public static readonly Color Fire = new Color(1f, 0.46f, 0.10f), Frost = new Color(0.62f, 0.88f, 1f), Shock = new Color(0.50f, 0.74f, 1f),
            Blood = new Color(0.78f, 0.03f, 0.04f), Acid = new Color(0.42f, 1f, 0.18f), Crit = new Color(1f, 0.12f, 0.16f),
            Shadow = new Color(0.42f, 0.16f, 0.72f), Cursed = new Color(0.95f, 0.06f, 0.04f), Essence = new Color(0.86f, 0.10f, 0.20f);

        /// <summary>Il colore dell'elemento di un sigillo dell'Arsenale (nodo 12..18).</summary>
        public static Color OfInfusion(int node) => node switch
        {
            12 => Crit, 13 => Blood, 14 => Fire, 15 => Acid, 16 => Shock, 17 => Frost, _ => Cursed,
        };

        public static Color OfSpell(int node) => node switch
        {
            12 => Fire, 13 => Shock, 14 => Frost, 15 => Blood, 16 => Shadow, 17 => new Color(0.9f, 0.55f, 1f), _ => Essence,
        };

        public static void Flame(Vector3 p, Vector3 up, float scale = 1f)
        {
            if (MagicVfx.On) { MagicVfx.Flame(p, up, scale, Color.white); return; }
            Fx.Particle(4, true, p + Random.insideUnitSphere * 0.006f * scale, up * Random.Range(0.25f, 0.6f) + Random.insideUnitSphere * 0.12f, Vector3.up * 0.3f,
                        0.02f * scale, Random.Range(0.045f, 0.075f) * scale, Random.Range(0.18f, 0.32f), Color.white, 0.9f);
        }

        public static void Glow(Vector3 p, Color c, float size, float life = 0.12f, float alpha = 0.6f)
        {
            // il bagliore nuovo e' una gaussiana: a parita' di misura sembra piu' piccolo del disco di Fx
            if (MagicVfx.On) { MagicVfx.Emit(MagicVfx.Kind.Glow, p, Vector3.zero, size * 1.5f, life, new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha * 1.2f))); return; }
            Fx.Particle(0, true, p, Vector3.zero, Vector3.zero, size, size * 1.2f, life, c, alpha);
        }

        public static void Sparks(Vector3 p, Color c, int n, float speed, float size = 0.012f)
        {
            if (MagicVfx.On)
            {
                for (int i = 0; i < n; i++)
                    MagicVfx.Emit(MagicVfx.Kind.Spark, p, Random.insideUnitSphere * speed + Vector3.up * speed * 0.15f, size * Random.Range(0.6f, 1.1f),
                                  Random.Range(0.15f, 0.42f), Color.Lerp(c, Color.white, 0.35f));
                return;
            }
            for (int i = 0; i < n; i++)
                Fx.Particle(1, true, p, Random.insideUnitSphere * speed, Vector3.down * 3f, size, size * 0.2f, Random.Range(0.12f, 0.3f), Color.Lerp(c, Color.white, 0.4f));
        }

        public static void Mist(Vector3 p, Vector3 vel, Color c, float size, float life = 0.7f, float alpha = 0.35f)
        {
            if (MagicVfx.On) { MagicVfx.Emit(MagicVfx.Kind.Mist, p, vel, size * 2.2f, life, new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha * 1.6f))); return; }
            Fx.Particle(0, true, p, vel, Vector3.zero, size, size * 3f, life, c, alpha);
        }

        public static void Drip(Vector3 p, Color c, float size = 0.008f)
        {
            if (MagicVfx.On) { MagicVfx.Emit(MagicVfx.Kind.Drop, p, Vector3.down * 0.3f + Random.insideUnitSphere * 0.06f, size * 1.5f, Random.Range(0.45f, 0.7f), c); return; }
            Fx.Particle(1, false, p, Vector3.down * 0.4f + Random.insideUnitSphere * 0.08f, Vector3.down * 7f, size, size * 0.6f, 0.45f, c);
        }

        /// <summary>Un arco frastagliato da a a b (fulmine): segmenti di raggio con uno scarto casuale perpendicolare.</summary>
        public static void Arc(Vector3 a, Vector3 b, Color c, float width = 0.012f, int seg = 7, float jitter = 0.12f)
        {
            // un arco breve e frastagliato, con un ramo se e' lungo (i fulmini lunghi li fa MagicCaster con MagicVfx.Lightning)
            if (MagicVfx.On) { MagicVfx.Lightning(a, b, c, width * 0.8f, Random.Range(0.05f, 0.09f), (a - b).sqrMagnitude > 0.25f ? 1 : 0, Mathf.Min(0.3f, jitter * 0.9f)); return; }
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Vector3 n1 = Vector3.Cross(d, Vector3.up);
            if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.Cross(d, Vector3.right);
            n1.Normalize();
            Vector3 n2 = Vector3.Cross(d / len, n1);
            Vector3 prev = a;
            for (int i = 1; i <= seg; i++)
            {
                float t = (float)i / seg;
                Vector3 p = a + d * t;
                if (i < seg)
                {
                    float j = jitter * len * 0.18f * Mathf.Sin(t * Mathf.PI);
                    p += n1 * Random.Range(-j, j) + n2 * Random.Range(-j, j);
                }
                Fx.Beam(prev, p, width, c);
                prev = p;
            }
        }

        public static Light Flash(Vector3 p, Color c, float intensity, float range, float dur)
        {
            var go = new GameObject("LampoMagia");
            go.transform.position = p;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            Stealth.SetLightLayers(l, 1u);          // solo il mondo: vicino alla mano brucerebbe arma e guanto
            go.AddComponent<LightFade>().Begin(l, dur);
            return l;
        }

        public static void Burst(Vector3 p, Color c, float scale)
        {
            if (MagicVfx.On) { MagicVfx.Explosion(p, c, scale, c == Fire ? Color.white : Color.Lerp(c, Color.white, 0.25f)); return; }
            for (int i = 0; i < 26; i++)
                Fx.Particle(4, true, p + Random.insideUnitSphere * 0.2f * scale, Random.insideUnitSphere * 5.5f * scale + Vector3.up * 1.2f, Vector3.up * 0.4f,
                            0.18f * scale, Random.Range(0.5f, 0.9f) * scale, Random.Range(0.35f, 0.65f), Color.white, 0.85f);
            for (int i = 0; i < 12; i++)
                Fx.Particle(0, false, p + Random.insideUnitSphere * 0.3f * scale, Random.insideUnitSphere * 2.2f * scale + Vector3.up * 0.9f, Vector3.zero,
                            0.3f * scale, Random.Range(1.2f, 1.8f) * scale, Random.Range(0.9f, 1.5f), new Color(0.16f, 0.13f, 0.12f), 0.45f);
            Sparks(p, c, 18, 7f * scale, 0.02f);
        }

        /// <summary>Un anello che si allarga (l'onda): sbuffi additivi che corrono in fuori nel piano della normale.</summary>
        public static void Ring(Vector3 c, Vector3 fwd, float radius, Color col, int n = 48, float arc = 360f)
        {
            Vector3 f = fwd.normalized;
            Vector3 s = Vector3.Cross(Vector3.up, f).normalized;
            if (s.sqrMagnitude < 0.5f) s = Vector3.right;
            if (MagicVfx.On)
            {
                // l'anello d'urto a terra e uno di fronte, poi sbuffi che corrono in fuori lungo l'arco
                MagicVfx.Emit(MagicVfx.Kind.GroundRing, c + Vector3.down * 0.95f, Vector3.zero, radius * 2.3f, 0.5f, col, 0f);
                MagicVfx.Emit(MagicVfx.Kind.Ring, c + f * 0.25f, Vector3.zero, 1.4f, 0.28f, col);
                for (int i = 0; i < n / 2; i++)
                {
                    float a = (i / (float)(n / 2 - 1) - 0.5f) * arc * Mathf.Deg2Rad;
                    Vector3 d = f * Mathf.Cos(a) + s * Mathf.Sin(a);
                    MagicVfx.Emit(MagicVfx.Kind.Mist, c + d * 0.3f + Vector3.down * Random.Range(0f, 0.7f), d * (radius / 0.42f) + Vector3.up * Random.Range(-0.1f, 0.3f),
                                  Random.Range(0.35f, 0.6f), Random.Range(0.45f, 0.65f), new Color(col.r, col.g, col.b, 0.55f));
                }
                return;
            }
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)(n - 1) - 0.5f) * arc * Mathf.Deg2Rad;
                Vector3 d = f * Mathf.Cos(a) + s * Mathf.Sin(a);
                Fx.Particle(0, true, c + d * 0.25f, d * (radius / 0.35f) + Vector3.up * Random.Range(-0.2f, 0.4f), Vector3.zero,
                            0.12f, Random.Range(0.45f, 0.7f), 0.4f, col, 0.5f);
            }
        }

        /// <summary>Il corpo di un nemico per i colpi che volano: un segmento verticale dai piedi alla testa con il suo raggio (l'occhio
        /// volante e' una sfera). AimPoint e' solo il centro: una fiamma che passa all'altezza del petto o della testa deve colpire.</summary>
        public static void Body(Enemy e, out Vector3 a, out Vector3 b, out float r)
        {
            Vector3 p = e.transform.position;
            if (e.Archetype == Enemy.EnemyType.Occhio) { a = b = p; r = 0.5f; return; }
            bool big = e.type == Enemy.EnemyType.Cardinale;
            bool tall = big || e.Archetype == Enemy.EnemyType.Abate || e.type == Enemy.EnemyType.Direttore;
            float h = big ? 2.6f : tall ? 2.2f : 1.8f;
            r = big ? 0.6f : tall ? 0.48f : 0.4f;
            a = p + Vector3.up * 0.3f;
            b = p + Vector3.up * (h - 0.25f);
        }

        /// <summary>Distanza al quadrato fra i segmenti p1-q1 e p2-q2; s e t dicono dove stanno i due punti piu' vicini (0..1 lungo
        /// ciascun segmento). Il metodo classico dei punti piu' vicini fra due segmenti, coi casi degeneri.</summary>
        public static float SegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            if (a <= 1e-8f && e <= 1e-8f) { s = 0f; t = 0f; return r.sqrMagnitude; }
            if (a <= 1e-8f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-8f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-10f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - (p2 + d2 * t)).sqrMagnitude;
        }

        /// <summary>Danno di una magia a un nemico: passa da Enemy.Damage (in cooperativa diventa una richiesta all'ospite) con la fonte
        /// Magia; con Luna Calante chi e' colpito rallenta.</summary>
        public static void Hurt(Enemy e, float dmg, Vector3 at, bool quiet = false)
        {
            if (e == null || e.hp <= 0f || dmg <= 0f) return;
            var st = EnemyStatus.Of(e);
            dmg *= st != null ? st.DamageTakenMult : 1f;
            e.Damage(dmg, at, false, !quiet, DamageSource.Magia, 0, quiet);
            if (e != null && e.hp > 0f && Magic.State.SpellSlows) EnemyStatus.Ensure(e)?.Slow(0.3f, 2f);
        }

        /// <summary>Il nemico piu' vicino alla linea di mira (entro 'deg' gradi e 'range' metri, a vista libera).</summary>
        public static Enemy Target(Vector3 eye, Vector3 fwd, float range, float deg, ICollection<Enemy> skip = null)
        {
            Enemy best = null; float bestScore = float.MaxValue;
            float cosMax = Mathf.Cos(deg * Mathf.Deg2Rad);
            foreach (var e in Enemy.All)
            {
                if (e == null || e.hp <= 0f || (skip != null && skip.Contains(e))) continue;
                Vector3 to = e.AimPoint - eye;
                float d = to.magnitude;
                if (d > range || d < 0.01f) continue;
                float c = Vector3.Dot(to / d, fwd);
                if (c < cosMax) continue;
                if (!HotelNav.ClearLine(eye, e.AimPoint)) continue;
                float score = (1f - c) * 40f + d * 0.05f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        public static Enemy NearestFrom(Vector3 p, float range, ICollection<Enemy> skip)
        {
            Enemy best = null; float bd = range * range;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.hp <= 0f || (skip != null && skip.Contains(e))) continue;
                float d = (e.AimPoint - p).sqrMagnitude;
                if (d > bd || !HotelNav.ClearLine(p, e.AimPoint)) continue;
                bd = d; best = e;
            }
            return best;
        }
    }

    /// <summary>La Fiamma Sacrilega in volo: un nucleo incandescente con la scia di fuoco e la sua luce; esplode contro un muro, un nemico
    /// o dopo due secondi: danno che cala col raggio e incendio.</summary>
    public sealed class Fireball : MonoBehaviour
    {
        Vector3 dir;
        float speed = 22f, life = 2.2f, radius, damage;
        Light glow;
        bool done, visual;

        /// <summary>'visual': la palla di un compagno vista qui (MagicReplay): vola ed esplode uguale, ma non ferisce e non incendia.</summary>
        public static Fireball Launch(Vector3 from, Vector3 dir, float damage, float radius, bool visual = false)
        {
            var go = new GameObject("FiammaSacrilega");
            go.transform.position = from;
            var f = go.AddComponent<Fireball>();
            f.dir = dir.normalized; f.damage = damage; f.radius = radius; f.visual = visual;
            f.glow = go.AddComponent<Light>();
            f.glow.type = LightType.Point; f.glow.color = MagicFx.Fire; f.glow.intensity = 2.6f; f.glow.range = 6f; f.glow.shadows = LightShadows.None;
            Stealth.SetLightLayers(f.glow, 1u);
            if (MagicVfx.On) return f;
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = Vector3.one * 0.13f;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", new Color(1f, 0.78f, 0.42f));
            core.GetComponent<MeshRenderer>().sharedMaterial = m;
            core.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return f;
        }

        /// <summary>La palla vista da vicino: il nucleo (due bagliori, bianco caldo dentro e arancio fuori, rinnovati a ogni fotogramma),
        /// lingue di fuoco tutt'intorno che restano indietro e si consumano (la scia), fumo scuro e braci.</summary>
        void Look(Vector3 p, float step)
        {
            MagicVfx.Emit(MagicVfx.Kind.Glow, p, Vector3.zero, 0.62f, 0.05f, new Color(1f, 0.5f, 0.16f, 0.85f));
            MagicVfx.Emit(MagicVfx.Kind.Glow, p, Vector3.zero, 0.22f, 0.04f, new Color(1f, 0.93f, 0.78f, 1f));
            for (int i = 0; i < 5; i++)
            {
                Vector3 q = p - dir * (step * i / 5f) + Random.insideUnitSphere * 0.065f;
                MagicVfx.Emit(MagicVfx.Kind.FireBig, q, -dir * Random.Range(0.5f, 1.8f) + Random.insideUnitSphere * 0.55f + Vector3.up * 0.25f,
                              Random.Range(0.17f, 0.3f), Random.Range(0.16f, 0.32f), Color.white, Random.Range(0f, 360f));
            }
            if (Random.value < 0.8f)
                MagicVfx.Emit(MagicVfx.Kind.Smoke, p - dir * 0.2f, Random.insideUnitSphere * 0.15f + Vector3.up * 0.25f, Random.Range(0.13f, 0.22f),
                              Random.Range(0.7f, 1.1f), new Color(0.09f, 0.08f, 0.075f, 0.42f));
            if (Random.value < 0.9f)
                MagicVfx.Emit(MagicVfx.Kind.Ember, p + Random.insideUnitSphere * 0.08f, -dir * Random.Range(1f, 3f) + Random.insideUnitSphere * 1.2f,
                              Random.Range(0.008f, 0.014f), Random.Range(0.4f, 0.9f), MagicFx.Fire);
        }

        void Update()
        {
            if (done) return;
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            float step = speed * dt;
            if (MagicVfx.On) Look(p, step);
            else for (int i = 0; i < 3; i++) MagicFx.Flame(p - dir * (step * i / 3f), -dir, 2.4f);
            if (glow != null) glow.intensity = 2.6f + Mathf.Sin(Time.time * 37f) * 0.5f;
            if (Physics.SphereCast(p, 0.1f, dir, out RaycastHit hit, step, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
            { Explode(hit.point + hit.normal * 0.1f); return; }
            // il tratto di questo fotogramma contro il corpo di ogni nemico (non solo il suo centro): il nucleo e' largo 6,5 cm
            Vector3 q = p + dir * step;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.hp <= 0f) continue;
                MagicFx.Body(e, out Vector3 ba, out Vector3 bb, out float br);
                br += 0.065f;
                if (MagicFx.SegSeg(p, q, ba, bb, out float s, out _) < br * br) { Explode(p + dir * (step * s)); return; }
            }
            transform.position = p + dir * step;
            life -= dt;
            if (life <= 0f) Explode(transform.position);
        }

        void Explode(Vector3 at)
        {
            done = true;
            MagicFx.Burst(at, MagicFx.Fire, 1f);
            MagicFx.Flash(at, MagicFx.Fire, 8f, 9f, 0.45f);
            Sfx.At(SoundBank.MagiaBoom, at, 0.9f, Random.Range(0.92f, 1.06f), 2.5f, 40f);
            GameManager.I?.player?.Shake(at, 0.6f, 10f);
            if (visual) { Destroy(gameObject, 0.05f); return; }
            var hit = new List<Enemy>();
            foreach (var e in Enemy.All) if (e != null && e.hp > 0f) hit.Add(e);
            foreach (var e in hit)
            {
                if (e == null || e.hp <= 0f) continue;
                float d = Vector3.Distance(e.AimPoint, at);
                if (d > radius || !HotelNav.ClearLine(at, e.AimPoint)) continue;
                float k = Mathf.Lerp(1f, 0.36f, d / radius);
                MagicFx.Hurt(e, damage * k, e.AimPoint);
                if (e != null && e.hp > 0f) EnemyStatus.Ensure(e)?.Burn(12f * Magic.State.SpellDamage, 4f);
            }
            Destroy(gameObject, 0.05f);
        }
    }

    /// <summary>Gli stati che magie e sigilli lasciano su un nemico: fuoco ed emorragia (danno nel tempo), corrosione (subisce di piu' da
    /// tutto), stordimento (si ferma, l'animazione rallenta), freddo (rallenta; abbastanza freddo congela: fermo, azzurro, l'animazione
    /// si blocca), rallentamento di Luna Calante. Danni ogni mezzo secondo, silenziosi (Enemy.Damage quiet), con la fonte Magia.
    /// In cooperativa gli stati li tiene l'ospite, che simula i nemici: un ospitato che mette uno stato su un pupazzo lo chiede
    /// all'ospite (StatusClaim; il danno nel tempo va a nome suo: 'claimer') e intanto ne vede l'aspetto; l'ospite racconta a tutti gli
    /// stati di ogni nemico (EnemyNet.status) e i pupazzi li mostrano, senza danni ne' soste (dal 2026-11-16, prima stordimento e
    /// rallentamenti degli ospitati si perdevano).</summary>
    public sealed class EnemyStatus : MonoBehaviour
    {
        Enemy e;
        NavMeshAgent agent;
        Animator anim;
        float baseSpeed, baseAnim = 1f;
        float burnDps, burnT, bleedDps, bleedT, acidT, stunT, slowK, slowT, chill, freezeT, tickT;
        int bleedStacks;
        // pupazzo: solo l'aspetto (lo stato vero lo tiene l'ospite): un attimo subito per chi lo mette, poi lo rinnova EnemyNet.status
        float vBurn, vBleed, vAcid, vStun, vSlow, vFreeze;
        int netFlags;
        /// <summary>Ospite: chi ha messo l'ultimo fuoco, sangue o acido (slot; 0 = io): a lui l'uccisione del danno nel tempo.</summary>
        public int claimer;
        /// <summary>Ospite: lo slot dell'ospitato di cui si sta applicando la richiesta (ApplyClaim); 0 fuori.</summary>
        static int claimFrom;
        /// <summary>Per -cooptest: stati chiesti dagli ospitati e applicati qui (ospite) e stati visti arrivare sui pupazzi (ospitato).</summary>
        public static int ClaimsApplied, NetSeen;
        public const int FBurn = 1, FBleed = 2, FAcid = 4, FStun = 8, FSlow = 16, FFreeze = 32;
        bool held;
        readonly List<Material> tinted = new List<Material>();
        readonly List<Color> tintBase = new List<Color>();
        float fxT;

        public float DamageTakenMult => acidT > 0f || vAcid > 0f ? 1.3f : 1f;
        public bool Frozen => freezeT > 0f || vFreeze > 0f;
        public bool Stunned => stunT > 0f || freezeT > 0f || vStun > 0f || vFreeze > 0f;
        public float ChillLevel => chill;

        /// <summary>Gli stati da raccontare agli altri (ospite).</summary>
        public int NetFlags => (burnT > 0f ? FBurn : 0) | (bleedT > 0f ? FBleed : 0) | (acidT > 0f ? FAcid : 0) | (stunT > 0f ? FStun : 0)
                               | (slowT > 0f ? FSlow : 0) | (freezeT > 0f ? FFreeze : 0);
        public static int FlagsOf(Enemy e) { var s = Of(e); return s != null ? s.NetFlags : 0; }

        /// <summary>Ospitato: gli stati di un nemico come li racconta l'ospite (solo l'aspetto sul pupazzo).</summary>
        public static void NetApply(Enemy e, int flags)
        {
            if (e == null || !e.puppet) return;
            var s = flags != 0 ? Ensure(e) : Of(e);
            if (s == null) return;
            if (flags != 0 && s.netFlags == 0) NetSeen++;
            s.netFlags = flags;
            const float hold = 0.4f;            // lo stato arriva ~10 volte al secondo, non affidabile: l'aspetto regge qualche pacchetto perso
            if ((flags & FBurn) != 0) s.vBurn = Mathf.Max(s.vBurn, hold);
            if ((flags & FBleed) != 0) s.vBleed = Mathf.Max(s.vBleed, hold);
            if ((flags & FAcid) != 0) s.vAcid = Mathf.Max(s.vAcid, hold);
            if ((flags & FStun) != 0) s.vStun = Mathf.Max(s.vStun, hold);
            if ((flags & FSlow) != 0) s.vSlow = Mathf.Max(s.vSlow, hold);
            if ((flags & FFreeze) != 0) s.vFreeze = Mathf.Max(s.vFreeze, hold);
        }

        /// <summary>Ospite: uno stato chiesto da un ospitato su un suo pupazzo (StatusClaim), con i valori tenuti nei limiti delle magie.</summary>
        public static void ApplyClaim(Enemy e, int from, int kind, float a, float b, float c)
        {
            if (e == null || e.puppet) return;
            var s = Ensure(e);
            if (s == null) return;
            a = Mathf.Clamp(a, 0f, 200f); b = Mathf.Clamp(b, 0f, 30f); c = Mathf.Clamp(c, 0f, 8f);
            claimFrom = from;
            try
            {
                switch (kind)
                {
                    case 1: s.Burn(a, b); break;
                    case 2: s.Bleed(a, b); break;
                    case 3: s.Corrode(Mathf.Min(a, 30f)); break;
                    case 4: s.Stun(Mathf.Min(a, 6f)); break;
                    case 5: s.Slow(Mathf.Min(a, 0.9f), b); break;
                    case 6: s.Chill(Mathf.Min(a, 2f), b, c); break;
                    default: return;
                }
                ClaimsApplied++;
            }
            finally { claimFrom = 0; }
        }

        /// <summary>Sul pupazzo uno stato non si applica: lo si chiede all'ospite e se ne mostra subito l'aspetto per un attimo.</summary>
        bool Claim(int kind, float a, float b, float c, ref float look, float t)
        {
            if (e == null || !e.puppet) return false;
            Coop.Game?.SendStatusClaim(e, kind, a, b, c);
            look = Mathf.Max(look, Mathf.Min(t, 1f));
            return true;
        }

        public static EnemyStatus Of(Enemy e) => e != null ? e.GetComponent<EnemyStatus>() : null;

        public static EnemyStatus Ensure(Enemy e)
        {
            if (e == null || e.hp <= 0f) return null;
            var s = e.GetComponent<EnemyStatus>();
            if (s == null)
            {
                s = e.gameObject.AddComponent<EnemyStatus>();
                s.e = e;
                s.agent = e.GetComponent<NavMeshAgent>();
                s.anim = e.GetComponentInChildren<Animator>();
                s.baseSpeed = e.speed;
                if (s.anim != null) s.baseAnim = s.anim.speed;
            }
            return s;
        }

        public void Burn(float dps, float t) { if (Claim(1, dps, t, 0f, ref vBurn, t)) return; burnDps = Mathf.Max(burnDps, dps); burnT = Mathf.Max(burnT, t); claimer = claimFrom; }
        public void Bleed(float dps, float t) { if (Claim(2, dps, t, 0f, ref vBleed, t)) return; bleedStacks = Mathf.Min(3, bleedStacks + 1); bleedDps = dps; bleedT = Mathf.Max(bleedT, t); claimer = claimFrom; }
        public void Corrode(float t) { if (Claim(3, t, 0f, 0f, ref vAcid, t)) return; acidT = Mathf.Max(acidT, t); claimer = claimFrom; }
        public void Stun(float t) { if (Claim(4, t, 0f, 0f, ref vStun, t)) return; stunT = Mathf.Max(stunT, t); }
        public void Slow(float k, float t) { if (Claim(5, k, t, 0f, ref vSlow, t)) return; slowK = Mathf.Max(slowK, k); slowT = Mathf.Max(slowT, t); }

        /// <summary>Freddo: 'amount' si somma (un colpo gelido 0,25, la Morsa 1); da 1 in su congela per 'freeze' secondi.</summary>
        public void Chill(float amount, float slowFor, float freeze)
        {
            if (Claim(6, amount, slowFor, freeze, ref vSlow, slowFor)) return;
            chill = Mathf.Min(1.5f, chill + amount);
            Slow(0.45f, slowFor);
            if (chill >= 1f && freezeT <= 0f) { freezeT = freeze; chill = 0f; SetTint(MagicFx.Frost * 1.6f); Sfx.At(SoundBank.MagiaGelo, transform.position + Vector3.up, 0.5f, 1.3f, 2f, 20f); }
        }

        void SetTint(Color c)
        {
            if (tinted.Count == 0)
                foreach (var m in e.flashMats)
                    if (m != null && m.HasProperty("_EmissionColor")) { tinted.Add(m); tintBase.Add(m.GetColor("_EmissionColor")); }
            foreach (var m in tinted) if (m != null) m.SetColor("_EmissionColor", c);
        }

        void ClearTint()
        {
            for (int i = 0; i < tinted.Count; i++) if (tinted[i] != null) tinted[i].SetColor("_EmissionColor", tintBase[i]);
            tinted.Clear(); tintBase.Clear();
        }

        void Hold(bool on, float animSpeed)
        {
            if (e.puppet) return;
            if (on)
            {
                if (!held) { held = true; e.enabled = false; }
                if (agent != null && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
                if (anim != null) anim.speed = animSpeed;
            }
            else if (held)
            {
                held = false;
                e.enabled = true;
                if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
                if (anim != null) anim.speed = baseAnim;
            }
        }

        void Update()
        {
            if (e == null) { Destroy(this); return; }
            if (e.hp <= 0f) { ClearTint(); Destroy(this); return; }
            float dt = Time.deltaTime;
            burnT -= dt; bleedT -= dt; acidT -= dt; stunT -= dt; slowT -= dt; freezeT -= dt;
            vBurn -= dt; vBleed -= dt; vAcid -= dt; vStun -= dt; vSlow -= dt; vFreeze -= dt;
            chill = Mathf.Max(0f, chill - dt * 0.25f);
            if (bleedT <= 0f) bleedStacks = 0;
            // danno nel tempo, ogni mezzo secondo (a nome di chi ha messo lo stato: in cooperativa l'uccisione va a lui)
            tickT -= dt;
            if (tickT <= 0f && !e.puppet)
            {
                tickT = 0.5f;
                float dmg = (burnT > 0f ? burnDps : 0f) + (bleedT > 0f ? bleedDps * bleedStacks : 0f) + (acidT > 0f ? 4f : 0f);
                if (dmg > 0f) { e.Damage(dmg * 0.5f * DamageTakenMult, e.AimPoint, false, false, DamageSource.Magia, claimer, true); if (e == null || e.hp <= 0f) return; }
            }
            bool burning = burnT > 0f || vBurn > 0f, bleeding = bleedT > 0f || vBleed > 0f, corroded = acidT > 0f || vAcid > 0f;
            bool stunned = stunT > 0f || vStun > 0f, cold = slowT > 0f || freezeT > 0f || vSlow > 0f || vFreeze > 0f;
            // effetti sul corpo
            fxT -= dt;
            if (fxT <= 0f)
            {
                fxT = 0.06f;
                Vector3 c = e.AimPoint;
                if (burning)
                {
                    for (int i = 0; i < 2; i++) MagicFx.Flame(c + Random.insideUnitSphere * 0.3f + Vector3.down * 0.3f, Vector3.up, 3.2f);
                    if (Random.value < 0.3f) MagicVfx.Emit(MagicVfx.Kind.Smoke, c + Vector3.up * 0.4f + Random.insideUnitSphere * 0.2f, Vector3.up * 0.6f, 0.3f, 1.6f, new Color(0.07f, 0.065f, 0.06f, 0.5f));
                }
                if (bleeding && Random.value < 0.5f) MagicFx.Drip(c + Random.insideUnitSphere * 0.25f, new Color(0.3f, 0.01f, 0.01f), 0.012f);
                if (corroded && Random.value < 0.45f) MagicFx.Mist(c + Random.insideUnitSphere * 0.3f, Vector3.up * 0.25f, MagicFx.Acid, 0.08f, 0.6f, 0.25f);
                if (stunned && Random.value < 0.6f) MagicFx.Arc(c + Random.insideUnitSphere * 0.35f, c + Random.insideUnitSphere * 0.35f, MagicFx.Shock, 0.008f, 4, 0.5f);
                if (cold && Random.value < 0.35f) MagicFx.Mist(c + Random.insideUnitSphere * 0.35f, Vector3.down * 0.1f, MagicFx.Frost, 0.07f, 0.8f, 0.22f);
            }
            if (e.puppet)
            {
                // il pupazzo congelato diventa azzurro come sull'ospite; finito tutto, il componente se ne va
                if (vFreeze > 0f) { if (tinted.Count == 0) SetTint(MagicFx.Frost * 1.6f); }
                else if (tinted.Count > 0) ClearTint();
                if (!burning && !bleeding && !corroded && !stunned && !cold && netFlags == 0) Destroy(this);
                return;
            }
            // movimento: congelato e stordito si fermano, il freddo e Luna Calante rallentano
            if (freezeT > 0f) Hold(true, 0f);
            else if (stunT > 0f) Hold(true, 0.18f);
            else
            {
                Hold(false, 1f);
                if (tinted.Count > 0) ClearTint();
                float k = slowT > 0f ? 1f - slowK : 1f;
                if (slowT <= 0f) slowK = 0f;
                e.speed = baseSpeed * k;
            }
            if (burnT <= 0f && bleedT <= 0f && acidT <= 0f && stunT <= 0f && slowT <= 0f && freezeT <= 0f && chill <= 0f)
            {
                e.speed = baseSpeed;
                Destroy(this);
            }
        }

        void OnDestroy()
        {
            if (e != null && e.hp > 0f) { Hold(false, 1f); e.speed = baseSpeed; }
            ClearTint();
        }
    }

    /// <summary>L'aspetto dell'arma col sigillo: i metalli prendono la luce dell'elemento (materiali propri con l'emissione accesa,
    /// intensita' per pezzo con un MaterialPropertyBlock, cosi' la brina o le rune possono correre da un punto all'altro dell'arma) e
    /// l'arma emette le sue particelle (fiamme sulla canna, archi, gocce, vapore, braci). Vive sul modello in prima persona.</summary>
    public sealed class WeaponAura : MonoBehaviour
    {
        WeaponRig rig;
        Transform viewRoot;
        readonly List<Renderer> parts = new List<Renderer>();
        readonly List<Material[]> original = new List<Material[]>();
        readonly List<Vector3> centers = new List<Vector3>();
        static readonly Dictionary<Material, Material> emissive = new Dictionary<Material, Material>();
        MaterialPropertyBlock mpb;
        int element = -1;
        bool cursed;
        Vector3 crawlFrom;
        float crawlT = -1f, crawlDur = 0.6f, fxT, appliedK = -1f;
        Color color;

        public static WeaponAura For(Transform viewRoot, WeaponRig rig)
        {
            if (viewRoot == null || rig == null) return null;
            var a = viewRoot.GetComponent<WeaponAura>();
            if (a == null) { a = viewRoot.gameObject.AddComponent<WeaponAura>(); a.Init(viewRoot, rig); }
            return a;
        }

        void Init(Transform vr, WeaponRig r)
        {
            viewRoot = vr; rig = r; mpb = new MaterialPropertyBlock();
            if (rig.renderers == null) return;
            foreach (var rr in rig.renderers)
            {
                if (rr == null || rr.transform == rig.reticle || rr.transform == rig.scopeLens) continue;
                var mats = rr.sharedMaterials;
                bool ok = false;
                foreach (var m in mats) if (m != null && !m.name.Contains("Vetro") && !m.name.Contains("Fx_") && m.HasProperty("_BaseColor")) ok = true;
                if (!ok) continue;
                parts.Add(rr);
                original.Add(mats);
                centers.Add(viewRoot.InverseTransformPoint(rr.bounds.center));
            }
        }

        static Material Emissive(Material m)
        {
            if (emissive.TryGetValue(m, out var e) && e != null) return e;
            e = new Material(m) { name = m.name + "_Sigillo" };
            e.EnableKeyword("_EMISSION");
            e.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (!e.HasProperty("_EmissionColor")) return e;
            e.SetColor("_EmissionColor", Color.black);
            emissive[m] = e;
            return e;
        }

        /// <summary>Accende (element 12..17, o 18 l'Arma Maledetta) o spegne (-1) l'aspetto. 'from' (mondo): da dove corre la luce.</summary>
        public void Set(int node, bool isCursed, Vector3? from = null, float crawl = 0.6f)
        {
            int want = isCursed ? 18 : node;
            if (want == element && isCursed == cursed && from == null) return;
            element = want; cursed = isCursed;
            color = MagicFx.OfInfusion(want);
            for (int i = 0; i < parts.Count; i++)
            {
                var r = parts[i];
                if (r == null) continue;
                var src = original[i];
                if (want < 0) { r.sharedMaterials = src; r.SetPropertyBlock(null); continue; }
                var mats = new Material[src.Length];
                for (int k = 0; k < src.Length; k++)
                {
                    var m = src[k];
                    mats[k] = m == null || m.name.Contains("Vetro") || m.name.Contains("Fx_") || !m.HasProperty("_BaseColor") ? m : Emissive(m);
                }
                r.sharedMaterials = mats;
            }
            if (want >= 0)
            {
                crawlFrom = from.HasValue ? viewRoot.InverseTransformPoint(from.Value) : Vector3.zero;
                crawlT = from.HasValue ? 0f : -1f;
                crawlDur = crawl;
            }
            appliedK = -1f;
        }

        /// <summary>La luce che corre sull'arma a partire da 'from' (mondo), per 'dur' secondi (la brina, le rune).</summary>
        public void Crawl(Vector3 from, float dur)
        {
            crawlFrom = viewRoot.InverseTransformPoint(from);
            crawlT = 0f; crawlDur = dur;
        }

        void Update()
        {
            if (element < 0 || viewRoot == null) return;
            float dt = Time.deltaTime;
            float pulse = cursed ? 0.75f + 0.25f * Mathf.Sin(Time.time * 6.3f) : 0.8f + 0.2f * Mathf.Sin(Time.time * 2.4f);
            float strength = cursed ? 2.2f : element == 14 ? 1.5f : 1.0f;
            if (crawlT >= 0f) crawlT += dt;
            float maxD = 0.9f;
            for (int i = 0; i < parts.Count; i++)
            {
                var r = parts[i];
                if (r == null) continue;
                float k = 1f;
                if (crawlT >= 0f)
                {
                    float d = Vector3.Distance(centers[i], crawlFrom) / maxD;
                    k = Mathf.Clamp01((crawlT / crawlDur - d) * 4f);
                }
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", color * (k * strength * pulse * 0.22f));
                r.SetPropertyBlock(mpb);
            }
            if (crawlT > crawlDur + 0.5f) crawlT = -1f;
            // particelle dell'elemento
            fxT -= dt;
            if (fxT > 0f) return;
            fxT = cursed ? 0.035f : 0.07f;
            Vector3 muzzle = rig.muzzle != null ? rig.muzzle.position : viewRoot.TransformPoint(rig.MuzzleLocal);
            Vector3 back = viewRoot.TransformPoint(new Vector3(0f, 0.04f, 0.1f));
            Vector3 p = Vector3.Lerp(back, muzzle, Random.value);
            switch (element)
            {
                case 14: MagicFx.Flame(p + Vector3.up * 0.015f, Vector3.up, 0.55f); break;
                case 17: if (Random.value < 0.6f) MagicFx.Mist(p, Vector3.down * 0.05f, MagicFx.Frost, 0.015f, 0.7f, 0.18f); break;
                case 16: if (Random.value < 0.5f) MagicFx.Arc(p, p + Random.insideUnitSphere * 0.05f, MagicFx.Shock, 0.003f, 3, 0.8f); break;
                case 15: if (Random.value < 0.35f) MagicFx.Drip(p - Vector3.up * 0.02f, MagicFx.Acid, 0.004f); break;
                case 13: if (Random.value < 0.3f) MagicFx.Drip(p - Vector3.up * 0.02f, new Color(0.35f, 0.01f, 0.01f), 0.004f); break;
                case 12: if (Random.value < 0.25f) MagicFx.Glow(p, MagicFx.Crit, 0.01f, 0.25f, 0.5f); break;
                default:
                    if (MagicVfx.On)
                    {
                        MagicVfx.Flame(p, Vector3.up, 0.45f, MagicVfx.CursedFire);
                        if (Random.value < 0.3f) MagicVfx.Emit(MagicVfx.Kind.Smoke, p, Vector3.up * 0.18f, 0.03f, 0.9f, new Color(0.06f, 0.01f, 0.01f, 0.4f));
                        if (Random.value < 0.12f) MagicVfx.Emit(MagicVfx.Kind.Rune, p + Vector3.up * 0.02f, Vector3.up * 0.03f, 0.018f, 0.7f, MagicFx.Cursed, 0f);
                        break;
                    }
                    MagicFx.Flame(p, Vector3.up, 0.45f);
                    if (Random.value < 0.5f) Fx.Particle(0, false, p, Vector3.up * 0.2f, Vector3.zero, 0.02f, 0.09f, 0.9f, new Color(0.08f, 0.02f, 0.02f), 0.35f);
                    break;
            }
        }
    }
}
