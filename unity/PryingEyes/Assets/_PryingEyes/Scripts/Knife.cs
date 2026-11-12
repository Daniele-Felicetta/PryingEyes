using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>MISERICORDIA, il coltello (tasto G, o COLTELLO sul touch): sta sempre in tasca, non si cambia arma. Un fendente o una
    /// stoccata a un metro e mezzo, 120 di danno, e si sente (rumore Other, 9 m): chi ti sente ti cerca. Alle spalle di un prete che
    /// cammina e non ti ha visto diventa la presa: scatta il prompt PRESA ALLE SPALLE, il colpo e' in silenzio, uccide sul colpo
    /// anche l'abate e paga 130 punti (Punti.Coltello). Mentre fende, l'arma si abbassa e sta zitta (Weapon.KnifeOut).
    /// Il modello e le mani sono quelli di Blender (Resources/Armi/kn_coltello, Resources/ManiArmi/kn_coltello): la mano e'
    /// modellata sul manico come per le pistole, quindi non c'e' niente da incollare a mano.</summary>
    public class Knife : MonoBehaviour
    {
        public static Knife I;

        /// <summary>Distanza (m) dal centro della visuale al bordo del corpo. La presa ha un metro in piu': il passo avanti che si fa
        /// mentre si colpisce non si vede ma c'e', e chi cammina piano alle spalle non va mancato per un palmo.</summary>
        public const float StabReach = 1.75f, TakedownReach = 2.35f;
        public const float StabDamage = 120f;
        /// <summary>Quanto lontano si sente un fendente che va a segno (la presa non fa rumore).</summary>
        public const float StabNoise = 9f;

        /// <summary>Conteggi per la prova automatica (-knifetest).</summary>
        public static int statTakedowns, statStabs, statWhiffs;
        public static bool lastSilent;

        enum Kind { Slash, Stab, Takedown }

        struct Key { public float t; public Vector3 pos, rot; public int ease; }
        static Key K(float t, float x, float y, float z, float rx, float ry, float rz, int ease = 0) =>
            new Key { t = t, pos = new Vector3(x, y, z), rot = new Vector3(rx, ry, rz), ease = ease };

        // Pose del modello nel riferimento della visuale (x destra, y su, z avanti; gradi: x = beccheggio, negativo = la punta sale).
        // Ogni chiave dice dove arriva e come: 0 liscio, 1 accelera (lo scatto), 2 rallenta (l'arrivo). La prima e l'ultima sono
        // fuori campo, in basso a destra: il coltello esce dal basso e rientra lì mentre l'arma risale.
        static readonly Key[] Slash =
        {
            K(0.00f, 0.30f, -0.52f, 0.30f, 26f, 8f, 14f),
            K(0.10f, 0.33f, -0.15f, 0.38f, -42f, 40f, 30f, 2),
            K(0.19f, 0.05f, -0.17f, 0.52f, -26f, -30f, -8f, 1),
            K(0.29f, -0.09f, -0.22f, 0.46f, -14f, -52f, -22f, 2),
            K(0.50f, 0.30f, -0.52f, 0.30f, 26f, 8f, 14f),
        };
        static readonly Key[] Stab =
        {
            K(0.00f, 0.30f, -0.52f, 0.30f, 26f, 8f, 14f),
            K(0.09f, 0.20f, -0.19f, 0.31f, -36f, -2f, 3f, 2),
            K(0.17f, 0.12f, -0.14f, 0.74f, -22f, 2f, 0f, 1),
            K(0.26f, 0.125f, -0.145f, 0.72f, -22f, 2f, 0f, 2),
            K(0.48f, 0.30f, -0.52f, 0.30f, 26f, 8f, 14f),
        };
        static readonly Key[] Take =
        {
            K(0.00f, 0.30f, -0.52f, 0.30f, 26f, 8f, 14f),
            K(0.16f, 0.20f, -0.02f, 0.34f, -86f, 4f, 12f, 2),
            K(0.36f, 0.07f, -0.19f, 0.68f, 24f, -6f, -6f, 1),
            K(0.56f, 0.075f, -0.20f, 0.66f, 28f, -8f, -16f, 2),
            K(0.76f, 0.16f, -0.24f, 0.50f, -8f, 0f, 4f),
            K(1.00f, 0.30f, -0.52f, 0.30f, 26f, 8f, 14f),
        };
        static readonly Key[][] Anims = { Slash, Stab, Take };
        static readonly float[] HitAt = { 0.19f, 0.17f, 0.36f };         // quando la lama arriva
        static readonly float[] SwishAt = { 0.10f, 0.09f, 0.20f };       // quando parte il fendente
        const float LeadIn = 0.07f;                                       // l'arma si abbassa prima che il coltello salga

        Transform cam, root;
        PlayerController player;
        Weapon weapon;
        Light fill;
        bool ready;

        bool playing, hitDone, swishDone;
        Kind kind;
        float t, cool;
        int combo;
        /// <summary>Solo per le prove: ferma l'animazione dov'e' (foto della posa).</summary>
        public bool debugFreeze;

        public bool Playing => playing;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public void Init(Transform camera)
        {
            if (ready) return;
            if (camera != null) cam = camera;
            var gm = GameManager.I;
            player = gm != null ? gm.player : null;
            weapon = player != null ? player.weapon : null;
            if (cam == null || player == null || weapon == null) return;
            BuildView();
            ready = true;
        }

        // ------------------------------------------------------------------ modello e mani
        void BuildView()
        {
            var go = new GameObject("ViewmodelColtello") { layer = 2 };
            go.transform.SetParent(cam, false);
            root = go.transform;
            SetPose(Slash[0]);

            var prefab = Resources.Load<GameObject>("Armi/kn_coltello");
            GameObject model;
            if (prefab != null) model = Instantiate(prefab, root);
            else
            {
                Debug.LogWarning("[PryingEyes] prefab del coltello mancante: Armi/kn_coltello");
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.transform.SetParent(root, false);
                model.transform.localScale = new Vector3(0.03f, 0.012f, 0.28f);
                model.transform.localPosition = new Vector3(0f, 0.076f, 0.15f);
            }
            model.name = "Coltello";
            if (prefab != null) { model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; }
            Prepare(model);

            var hands = Resources.Load<GameObject>("ManiArmi/kn_coltello");
            if (hands != null)
            {
                var h = Instantiate(hands, root);
                h.name = "Mani";
                h.transform.localPosition = Vector3.zero;
                h.transform.localRotation = Quaternion.identity;
                Prepare(h);
                // un coltello si tiene con una mano sola: la sinistra resta fuori campo
                foreach (var tr in h.GetComponentsInChildren<Transform>(true))
                    if (tr.name == "mano_s") tr.gameObject.SetActive(false);
            }

            // luce di riempimento come quella dell'arma (Weapon.BuildFlash): solo modello e mani, lontana, altrimenti il nero esce bianco
            fill = new GameObject("ColtelloFill").AddComponent<Light>();
            fill.transform.SetParent(root, false);
            fill.transform.localPosition = new Vector3(-0.32f, 0.55f, -0.35f);
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.95f, 0.88f);
            fill.intensity = 0.55f;
            fill.range = 2.6f;
            fill.shadows = LightShadows.None;
            Stealth.SetLightLayers(fill, Weapon.ViewLayer);
            go.SetActive(false);
        }

        static void Prepare(GameObject g)
        {
            foreach (var tr in g.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 2;
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.renderingLayerMask = Weapon.ViewLayer;
            }
        }

        // ------------------------------------------------------------------ animazione
        static void Eval(Key[] k, float t, out Vector3 p, out Quaternion r)
        {
            int i = 0;
            while (i < k.Length - 2 && t > k[i + 1].t) i++;
            Key a = k[i], b = k[i + 1];
            float u = Mathf.Clamp01((t - a.t) / Mathf.Max(0.0001f, b.t - a.t));
            u = b.ease == 1 ? u * u : b.ease == 2 ? 1f - (1f - u) * (1f - u) : u * u * (3f - 2f * u);
            p = Vector3.Lerp(a.pos, b.pos, u);
            r = Quaternion.Slerp(Quaternion.Euler(a.rot), Quaternion.Euler(b.rot), u);
        }

        void SetPose(Key k)
        {
            root.localPosition = k.pos;
            root.localRotation = Quaternion.Euler(k.rot);
        }

        void ApplyPose()
        {
            var keys = Anims[(int)kind];
            Eval(keys, Mathf.Clamp(t, 0f, keys[keys.Length - 1].t), out Vector3 p, out Quaternion r);
            // un muro davanti: il coltello si ritrae e si alza invece di entrarci (come l'arma)
            const float len = 0.95f;
            if (Physics.SphereCast(cam.position, 0.06f, cam.forward, out RaycastHit wh, len, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
            {
                float push = Mathf.Clamp01((len - wh.distance) / len);
                p.z -= push * 0.30f; p.y -= push * 0.03f;
                r *= Quaternion.Euler(-push * 28f, push * 8f, 0f);
            }
            root.localPosition = p;
            root.localRotation = r;
        }

        // ------------------------------------------------------------------ bersaglio
        /// <summary>Chi si colpisce adesso: il piu' vicino e piu' al centro fra chi e' a portata, nel cono davanti e con la linea libera.
        /// La presa ha la precedenza e una portata e un cono piu' larghi. 'silent' dice se il colpo e' una presa.</summary>
        Enemy FindTarget(bool silentOnly, out bool silent, out Vector3 point)
        {
            silent = false; point = Vector3.zero;
            if (cam == null) return null;
            Vector3 o = cam.position, f = cam.forward;
            Enemy best = null;
            float bestScore = float.MaxValue;
            var all = Enemy.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || e.IsDead) continue;
                if ((e.transform.position - o).sqrMagnitude > 49f) continue;            // oltre sei metri nemmeno si guarda
                bool can = e.CanTakedown(o);
                if (silentOnly && !can) continue;
                float d = e.KnifeReach(o, out Vector3 p);
                if (d > (can ? TakedownReach : StabReach)) continue;
                Vector3 to = p - o;
                float ang = to.sqrMagnitude > 0.01f ? Vector3.Angle(f, to) : 0f;
                if (ang > (can ? 60f : 46f)) continue;
                if (!HotelNav.ClearLine(o, p)) continue;
                float score = d + ang * 0.02f - (can ? 0.6f : 0f);
                if (score < bestScore) { bestScore = score; best = e; silent = can; point = p; }
            }
            return best;
        }

        /// <summary>Chiamata da PlayerController dopo i prompt dei muri e degli oggetti (che la cancellano a ogni fotogramma):
        /// se si puo' fare la presa lo dice.</summary>
        public void PromptTick()
        {
            if (!ready || playing || cool > 0f || GameMenu.Blocked || player == null || !player.alive) return;
            if (FindTarget(true, out _, out _) != null) GameManager.I.hud?.SetPrompt("PRESA ALLE SPALLE   [G]");
        }

        // ------------------------------------------------------------------ azione
        void Update()
        {
            if (!ready) { Init(cam); if (!ready) return; }          // l'arma puo' non essere ancora legata alla visuale quando parte il rig
            float dt = Time.deltaTime;
            bool alive = player.alive;
            if (cool > 0f) cool -= dt;
            if (playing)
            {
                if (!alive) { Stop(); return; }
                if (!debugFreeze) Advance(dt);
            }
            if (GameMenu.Blocked || !alive || playing || cool > 0f) return;
            if (Input.GetKeyDown(KeyCode.G) || TouchControls.Down(TouchControls.Action.Knife)) Begin();
        }

        /// <summary>Solo per le prove: parte come se si fosse premuto G.</summary>
        public void DebugAttack() { if (ready && !playing) Begin(); }

        /// <summary>Solo per le prove: ferma l'animazione a 'time' secondi (0 = la pose fuori campo) e la mostra.</summary>
        public void DebugShow(int animKind, float time)
        {
            if (!ready) return;
            kind = (Kind)Mathf.Clamp(animKind, 0, 2);
            t = time; playing = true; debugFreeze = true; hitDone = swishDone = true;
            root.gameObject.SetActive(true);
            weapon.KnifeOut(true);
            ApplyPose();
        }

        public void DebugHide() { debugFreeze = false; Stop(); }

        void Begin()
        {
            if (weapon == null || !weapon.CanKnife) return;
            FindTarget(false, out bool silent, out _);
            kind = silent ? Kind.Takedown : (combo++ & 1) == 0 ? Kind.Slash : Kind.Stab;
            t = -LeadIn;
            hitDone = swishDone = false;
            playing = true;
            root.gameObject.SetActive(true);
            weapon.KnifeOut(true);
            ApplyPose();
        }

        void Advance(float dt)
        {
            t += dt;
            var keys = Anims[(int)kind];
            if (!swishDone && t >= SwishAt[(int)kind])
            {
                swishDone = true;
                Sfx.UI(SoundBank.KnifeSwish, kind == Kind.Takedown ? 0.30f : 0.55f, Random.Range(0.95f, 1.08f));
            }
            if (!hitDone && t >= HitAt[(int)kind]) { hitDone = true; Strike(); }
            if (t >= keys[keys.Length - 1].t) { Stop(); cool = 0.06f; return; }
            ApplyPose();
        }

        void Stop()
        {
            if (!playing) return;
            playing = false;
            if (root != null) root.gameObject.SetActive(false);
            if (weapon != null) weapon.KnifeOut(false);
        }

        /// <summary>L'istante in cui la lama arriva: si guarda chi c'e' adesso (nel mezzo secondo di animazione puo' essersi girato,
        /// o essere morto per altra mano) e si decide se e' una presa, un colpo che fa rumore o aria.</summary>
        void Strike()
        {
            var e = FindTarget(false, out bool silent, out Vector3 pt);
            if (e == null)
            {
                statWhiffs++;
                player.Kick(0.35f, 0f, 10f);
                return;
            }
            lastSilent = silent;
            if (silent)
            {
                statTakedowns++;
                Sfx.At(SoundBank.KnifeStab, pt, 0.55f, Random.Range(0.84f, 0.94f), 2f, 14f);       // attutito, e nessun Noise.Emit
                e.Damage(e.hp + 1f, pt, false, false, DamageSource.Takedown);
                player.Kick(-1.6f, 0f, 9f);
            }
            else
            {
                statStabs++;
                Sfx.At(SoundBank.KnifeStab, pt, 0.95f, Random.Range(0.94f, 1.06f), 2f, 30f);
                Noise.Emit(pt, StabNoise, NoiseKind.Other);                                         // il rumore nasce dove la lama arriva (dalla visuale era attutito dal giocatore stesso)
                e.Damage(StabDamage, pt, false, true, DamageSource.Knife);
                if (e.hp > 0f) e.Provoke();                                                         // chi sopravvive sa dov'e' il giocatore
                player.Kick(0.9f, Random.Range(-0.5f, 0.5f), 10f);
            }
        }
    }
}
