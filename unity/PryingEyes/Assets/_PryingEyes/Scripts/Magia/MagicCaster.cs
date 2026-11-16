using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Accesso allo stato magico del giocatore locale (come Perks per PlayerPerks).</summary>
    public static class Magic
    {
        static readonly MagicState fallback = new MagicState();
        public static MagicState State => GameManager.I != null && GameManager.I.player != null ? GameManager.I.player.Arcana : fallback;
    }

    /// <summary>La mano sinistra del giocatore: magie (Libro della Stregoneria) e sigilli sull'arma (Libro dell'Arsenale), con i loro
    /// gesti (OffHandAnims) e i loro effetti.
    /// Comandi: 3 toccato = la magia scelta, 3 tenuto (0,35 s) = il sigillo scelto sull'arma in mano; rotella premuta = la magia;
    /// 6 / 7 = magia / sigillo seguente. Controller: LB + RB (toccato o tenuto), LB + destra, LB + R3.
    /// L'essenza torna da sola (2 al secondo) e con le uccisioni. Le magie non si possono lanciare ricaricando, cambiando arma, col
    /// coltello, correndo o con un altro gesto in corso. Senza la mano libera (modello mancante) la magia parte subito dalla camera.</summary>
    public sealed class MagicCaster : MonoBehaviour
    {
        public static MagicCaster I;
        PlayerController player;
        Weapon weapon;
        float holdT = -1f;
        bool holdFired;
        // stato del gesto in corso
        int castNode = -1;
        bool castInfusion;
        OffHand castHand;
        string phase;                 // effetto per fotogramma: "flame", "spark", "frost", "trace", "trail", "ichor", "pour", "arcs", "blood", "veins", "burn", "drain"
        float phaseT;
        Enemy drainTarget;
        float drainT, drainTick;
        Light handLight;
        float auraTick;
        /// <summary>Per le prove: magie e sigilli andati a segno.</summary>
        public static int statCasts, statInfusions, statHits;
        public static string LastCast;

        public static MagicCaster Ensure(PlayerController p)
        {
            if (p == null) return null;
            var c = p.GetComponent<MagicCaster>();
            if (c == null) c = p.gameObject.AddComponent<MagicCaster>();
            c.player = p;
            I = c;
            return c;
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        MagicState S => player != null ? player.Arcana : Magic.State;

        // schermo condiviso: ogni giocatore ha il suo lanciatore; i comandi e i globali (GameManager.I.player, l'HUD) sono quelli del suo posto
        void Update() { using (Seats.Enter(this)) UpdateBody(); }

        void UpdateBody()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (player == null) return;
            weapon = player.weapon;
            float dt = Time.deltaTime;
            var s = player.Arcana;
            bool still = player.SpeedH < 0.3f || player.IsCrouching;
            s.Tick(dt, still);
            if (!player.alive) { if (castHand != null) castHand.Cancel(); return; }
            EssenzaTick(dt, s);
            PhaseTick(dt);
            UpdateAura();
            if (GameMenu.Blocked || GrimoireUI.Open) { holdT = -1f; return; }

            // ---- comandi: 3 (tocco magia, tenuto sigillo), rotella premuta, 6 e 7
            bool padSpellHeld = TouchControls.Held(TouchControls.Action.Spell), padSpellDown = TouchControls.Down(TouchControls.Action.Spell);
            if (SeatInput.GetKeyDown(KeyCode.Alpha3) || padSpellDown) { holdT = 0f; holdFired = false; }
            bool held = SeatInput.GetKey(KeyCode.Alpha3) || padSpellHeld;
            if (holdT >= 0f)
            {
                if (held)
                {
                    holdT += dt;
                    if (!holdFired && holdT >= 0.35f) { holdFired = true; TryInfusion(); }
                }
                else
                {
                    if (!holdFired) TryCast();
                    holdT = -1f;
                }
            }
            if (SeatInput.GetMouseButtonDown(2)) TryCast();
            if (SeatInput.GetKeyDown(KeyCode.Alpha6) || TouchControls.Down(TouchControls.Action.SpellNext)) CycleAndTell(Grimoire.Book.Stregoneria);
            if (SeatInput.GetKeyDown(KeyCode.Alpha7) || TouchControls.Down(TouchControls.Action.InfusionNext)) CycleAndTell(Grimoire.Book.Arsenale);
        }

        void CycleAndTell(Grimoire.Book b)
        {
            var s = S;
            uint m = s.Mask(b);
            if ((m >> Grimoire.FirstInner) == 0) { player.hud?.Toast(b == Grimoire.Book.Stregoneria ? "Non conosci ancora nessuna magia: cerca la stanza segreta del libro." : "Non conosci ancora nessun sigillo: cerca la stanza segreta del libro."); return; }
            s.Cycle(b);
            int sel = b == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion;
            if (sel >= 0) player.hud?.Toast((b == Grimoire.Book.Stregoneria ? "Magia: " : "Sigillo: ") + Grimoire.Of(b)[sel].name);
            Sfx.UI(SoundBank.MagiaScelta, 0.45f, b == Grimoire.Book.Stregoneria ? 1f : 0.8f);
        }

        /// <summary>La mano sinistra e' libera per un gesto? (niente ricarica, cambio, coltello, corsa, altro gesto)</summary>
        public bool HandFree(out string why)
        {
            why = null;
            if (weapon == null) { why = ""; return false; }
            if (!weapon.CanUseOffHand(out why)) return false;
            return true;
        }

        // ------------------------------------------------------------------ magie
        public bool TryCast() => TryCast(S.SelectedSpell);

        /// <summary>Lancia la magia 'node' (12..18) se si puo': paga, fa il gesto e al momento giusto l'effetto. Pubblico per le prove.</summary>
        public bool TryCast(int node)
        {
            var s = S;
            if (node < 0) { player.hud?.Toast("Non conosci ancora nessuna magia: cerca la stanza segreta del libro."); return false; }
            if (!s.CanCast(node, false, player.hp, out string why)) { if (!string.IsNullOrEmpty(why)) { player.hud?.Toast(why); Sfx.UI(SoundBank.BookNo, 0.35f); } return false; }
            if (!HandFree(out why)) { if (!string.IsNullOrEmpty(why)) player.hud?.Toast(why); return false; }
            s.Pay(node, false, out float hpCost);
            if (hpCost > 0f) { player.hp = Mathf.Max(1f, player.hp - hpCost); player.hud?.SetBars(player.hp, player.shield); player.hud?.Flash(MagicFx.Blood, 0.35f, 0.25f); }
            statCasts++;
            LastCast = Grimoire.Witch[node].name;
            var hand = weapon.OffHand;
            if (hand == null) { SpellEvent(node, "cast"); return true; }
            var anim = OffHandAnims.Spell(node, hand.OneHanded);
            anim.speed = s.CastSpeed;
            BeginGesture(node, false, hand);
            hand.Play(anim, ev => SpellEvent(node, ev));
            Sfx.At(SoundBank.MagiaGesto, hand.PalmWorld, 0.35f, Random.Range(0.95f, 1.08f), 1f, 8f);
            return true;
        }

        public bool TryInfusion() => TryInfusion(S.SelectedInfusion);

        /// <summary>Traccia il sigillo 'node' (12..18) sull'arma in mano. Pubblico per le prove.</summary>
        public bool TryInfusion(int node)
        {
            var s = S;
            if (node < 0) { player.hud?.Toast("Non conosci ancora nessun sigillo: cerca la stanza segreta del libro."); return false; }
            if (!s.CanCast(node, true, player.hp, out string why)) { if (!string.IsNullOrEmpty(why)) { player.hud?.Toast(why); Sfx.UI(SoundBank.BookNo, 0.35f); } return false; }
            if (!HandFree(out why)) { if (!string.IsNullOrEmpty(why)) player.hud?.Toast(why); return false; }
            s.Pay(node, true, out _);
            statInfusions++;
            LastCast = Grimoire.Arsenal[node].name;
            var hand = weapon.OffHand;
            int slot = weapon.SlotIndex;
            if (hand == null) { InfusionEvent(node, slot, "cast"); return true; }
            var anim = OffHandAnims.Infusion(node, hand.OneHanded);
            anim.speed = s.HandlingMult;
            BeginGesture(node, true, hand);
            hand.Play(anim, ev => InfusionEvent(node, slot, ev));
            return true;
        }

        void BeginGesture(int node, bool infusion, OffHand hand)
        {
            castNode = node; castInfusion = infusion; castHand = hand; phase = null;
        }

        void EndGesture()
        {
            castNode = -1; castHand = null; phase = null;
            if (handLight != null) { Destroy(handLight.gameObject); handLight = null; }
        }

        void SetPhase(string p, Color lightColor = default, float lightI = 0f)
        {
            phase = p; phaseT = 0f;
            if (lightI > 0f && castHand != null)
            {
                if (handLight == null)
                {
                    var go = new GameObject("LuceMano");
                    handLight = go.AddComponent<Light>();
                    handLight.type = LightType.Point; handLight.range = 2.6f; handLight.shadows = LightShadows.None;
                    // solo il mondo: a pochi centimetri dall'arma e dalla mano le brucerebbe fino al bianco (le particelle bastano a farle brillare)
                    Stealth.SetLightLayers(handLight, 1u);
                }
                handLight.color = lightColor; handLight.intensity = lightI;
            }
            else if (lightI <= 0f && handLight != null) { Destroy(handLight.gameObject); handLight = null; }
        }

        Transform Cam => player.cam;

        /// <summary>Dove guarda il mirino: il primo nemico sotto il mirino o il muro (fino a 60 m), per mandarci la magia dalla mano. Il
        /// nemico conta: la mano sta 15 cm a sinistra e sotto l'occhio, e una magia diretta al muro dietro di lui gli passerebbe accanto.</summary>
        Vector3 AimPoint()
        {
            Vector3 o = Cam.position, f = Cam.forward;
            float far = 60f;
            if (Physics.Raycast(o, f, out RaycastHit h, 60f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) far = h.distance;
            Vector3 q = o + f * far;
            float best = far;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.hp <= 0f) continue;
                MagicFx.Body(e, out Vector3 a, out Vector3 b, out float r);
                if (MagicFx.SegSeg(o, q, a, b, out float s, out _) < r * r) best = Mathf.Min(best, s * far);
            }
            return o + f * best;
        }

        void SpellEvent(int node, string ev)
        {
            var s = S;
            var hand = castHand;
            Vector3 palm = hand != null ? hand.PalmWorld : Cam.position + Cam.forward * 0.4f - Cam.right * 0.12f - Cam.up * 0.1f;
            switch (ev)
            {
                case "ignite": SetPhase("flame", MagicFx.Fire, 1.4f); Sfx.At(SoundBank.MagiaFuoco, palm, 0.5f, 1.15f, 1f, 10f); return;
                case "spark": SetPhase("spark", MagicFx.Shock, 1.1f); Sfx.At(SoundBank.MagiaScintille, palm, 0.45f, 1f, 1f, 10f); return;
                case "charge": SetPhase("frost", MagicFx.Frost, 0.8f); Sfx.At(SoundBank.MagiaGelo, palm, 0.3f, 1.4f, 1f, 10f); return;
                case "mark": SetPhase("blood", MagicFx.Blood, 0.7f); return;
                case "gather": SetPhase("gather", new Color(0.9f, 0.55f, 1f), 1.0f); return;
                case "veins": SetPhase("veins", MagicFx.Essence, 1.2f); Sfx.At(SoundBank.MagiaEssenza, palm, 0.6f, 1f, 1f, 12f); return;
                case "squeeze": if (drainTarget != null) EndDrain(); return;
                case "fine": case "annullata": if (drainTarget != null) EndDrain(); EndGesture(); return;
                case "cast": break;
                default: return;
            }
            // ---- la magia parte
            LastCast = Grimoire.Witch[node].name;
            float dmgK = s.SpellDamage, rangeK = s.SpellRange;
            Vector3 aim = AimPoint();
            switch (node)
            {
                case 12:
                {
                    SetPhase(null);
                    Vector3 from = palm + (aim - palm).normalized * 0.12f;
                    Fireball.Launch(from, aim - from, 140f * dmgK, 3.5f * rangeK);
                    Sfx.At(SoundBank.MagiaFuoco, palm, 0.8f, 0.9f, 1.5f, 20f);
                    break;
                }
                case 13: CastLightning(hand, dmgK, rangeK); SetPhase(null); break;
                case 14: CastFrost(palm, dmgK, rangeK); SetPhase("cone", MagicFx.Frost, 1.0f); break;
                case 15: StartDrain(rangeK); break;
                case 16: Blink(rangeK); SetPhase(null); break;
                case 17: Wave(palm, dmgK, rangeK); SetPhase(null); break;
                default:
                    s.StartEssenza();
                    MagicFx.Burst(palm, MagicFx.Essence, 0.6f);
                    MagicFx.Flash(palm, MagicFx.Essence, 6f, 8f, 0.6f);
                    player.hud?.Banner("Essenza Maledetta", "Per dodici secondi la magia non conosce prezzo.");
                    player.hud?.Flash(MagicFx.Essence, 0.7f, 0.5f);
                    Sfx.UI(SoundBank.MagiaEssenza, 0.8f, 0.8f);
                    SetPhase(null);
                    break;
            }
        }

        void CastLightning(OffHand hand, float dmgK, float rangeK)
        {
            Vector3 from = hand != null ? hand.TipWorld(1) : Cam.position + Cam.forward * 0.45f;
            var hit = new List<Enemy>();
            var first = MagicFx.Target(Cam.position, Cam.forward, 25f * rangeK, 10f);
            Sfx.At(SoundBank.MagiaFulmine, from, 0.9f, Random.Range(0.95f, 1.05f), 2f, 40f);
            MagicFx.Flash(from, MagicFx.Shock, 5f, 6f, 0.25f);
            if (first == null)
            {
                Vector3 end = AimPoint();
                end = from + Vector3.ClampMagnitude(end - from, 14f);
                for (int i = 0; i < 3; i++) MagicFx.Arc(from, end, MagicFx.Shock, 0.016f, 9, 0.18f);
                MagicFx.Sparks(end, MagicFx.Shock, 14, 4f);
                return;
            }
            Vector3 prev = from;
            var e = first;
            float dmg = 90f * dmgK;
            for (int k = 0; k < 5 && e != null; k++)
            {
                Vector3 p = e.AimPoint;
                for (int i = 0; i < 3; i++) MagicFx.Arc(prev, p, MagicFx.Shock, k == 0 ? 0.018f : 0.012f, 8, 0.2f);
                MagicFx.Sparks(p, MagicFx.Shock, 10, 3.5f);
                hit.Add(e);
                MagicFx.Hurt(e, dmg, p);
                statHits++;
                if (e != null && e.hp > 0f) EnemyStatus.Ensure(e)?.Stun(0.6f);
                prev = p;
                dmg *= 0.7f;
                e = MagicFx.NearestFrom(p, 6f * rangeK, hit);
            }
        }

        void CastFrost(Vector3 palm, float dmgK, float rangeK)
        {
            Vector3 f = Cam.forward;
            float range = 7f * rangeK;
            float cosMax = Mathf.Cos(28f * Mathf.Deg2Rad);
            var list = new List<Enemy>(Enemy.All);
            foreach (var e in list)
            {
                if (e == null || e.hp <= 0f) continue;
                Vector3 to = e.AimPoint - Cam.position;
                float d = to.magnitude;
                if (d > range || Vector3.Dot(to / Mathf.Max(0.01f, d), f) < cosMax || !HotelNav.ClearLine(Cam.position, e.AimPoint)) continue;
                var st = EnemyStatus.Ensure(e);
                bool wasCold = st != null && (st.ChillLevel > 0.1f || st.Frozen);
                MagicFx.Hurt(e, 60f * dmgK, e.AimPoint);
                statHits++;
                if (e != null && e.hp > 0f && st != null) st.Chill(wasCold ? 1.2f : 0.6f, 4f, 2.5f);
            }
            Sfx.At(SoundBank.MagiaGelo, palm, 0.9f, 1f, 2f, 30f);
            MagicFx.Flash(palm + f * 0.5f, MagicFx.Frost, 3f, 7f, 0.4f);
        }

        void StartDrain(float rangeK)
        {
            drainTarget = MagicFx.Target(Cam.position, Cam.forward, 12f * rangeK, 12f);
            drainT = 1.5f; drainTick = 0f;
            SetPhase("drain", MagicFx.Blood, 1.3f);
            Sfx.At(SoundBank.MagiaSangue, Cam.position + Cam.forward, 0.7f, 1f, 1.5f, 15f);
            if (drainTarget == null) player.hud?.Toast("Nessuno da prosciugare.");
        }

        void EndDrain()
        {
            drainTarget = null; drainT = 0f;
            if (phase == "drain") SetPhase(null);
        }

        void DrainTick(float dt)
        {
            var e = drainTarget;
            if (e == null || e.hp <= 0f || drainT <= 0f) { if (phase == "drain") SetPhase(null); drainTarget = null; return; }
            drainT -= dt;
            Vector3 palm = castHand != null ? castHand.PalmWorld : Cam.position + Cam.forward * 0.4f;
            if (!HotelNav.ClearLine(Cam.position, e.AimPoint)) { EndDrain(); return; }
            // il filo di sangue: gocce che corrono dal petto del nemico al palmo
            Vector3 a = e.AimPoint;
            for (int i = 0; i < 3; i++)
            {
                float t = Random.value;
                Vector3 p = Vector3.Lerp(a, palm, t) + Random.insideUnitSphere * 0.03f * (1f - t);
                Fx.Particle(1, true, p, (palm - a).normalized * 5f, Vector3.zero, 0.014f, 0.004f, 0.12f, MagicFx.Blood);
            }
            drainTick -= dt;
            if (drainTick <= 0f)
            {
                drainTick = 0.1f;
                float dmg = 4f * S.SpellDamage;
                float before = e.hp;
                MagicFx.Hurt(e, dmg, a, true);
                float dealt = Mathf.Max(0f, before - (e != null ? e.hp : 0f));
                player.Heal(dealt * 0.5f);
                statHits++;
                if (e == null || e.hp <= 0f)
                {
                    S.Essence = Mathf.Min(S.EssenceMax, S.Essence + 20f);
                    player.hud?.Flash(MagicFx.Blood, 0.4f, 0.3f);
                    EndDrain();
                }
            }
        }

        void Blink(float rangeK)
        {
            var cc = player.GetComponent<CharacterController>();
            Vector3 feet = player.transform.position;
            Vector3 dir = Cam.forward; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = player.transform.forward;
            dir.Normalize();
            float r = cc != null ? cc.radius : 0.38f, h = cc != null ? cc.height : 1.75f;
            float maxD = 8f * rangeK;
            Vector3 p1 = feet + Vector3.up * (r + 0.05f), p2 = feet + Vector3.up * (h - r);
            float dist = maxD;
            if (Physics.CapsuleCast(p1, p2, r * 0.95f, dir, out RaycastHit hit, maxD, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0f, hit.distance - 0.25f);
            Vector3 dest = feet;
            for (float d = dist; d >= 0.5f; d -= 0.5f)
            {
                Vector3 c = feet + dir * d;
                if (!Physics.Raycast(c + Vector3.up * 1.2f, Vector3.down, out RaycastHit g, 3.5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(g.point.y - feet.y) > 1.2f) continue;
                Vector3 q = g.point + Vector3.up * 0.02f;
                if (Physics.CheckCapsule(q + Vector3.up * (r + 0.06f), q + Vector3.up * (h - r), r * 0.9f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                dest = q; break;
            }
            for (int i = 0; i < 18; i++)
                Fx.Particle(0, false, feet + Vector3.up * Random.Range(0.2f, 1.7f) + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 0.8f, Vector3.up * 0.3f,
                            0.2f, 0.7f, 0.9f, new Color(0.06f, 0.03f, 0.09f), 0.6f);
            if ((dest - feet).sqrMagnitude < 0.25f) { player.hud?.Toast("L'ombra non trova spazio."); return; }
            player.Teleport(dest);
            for (int i = 0; i < 18; i++)
                Fx.Particle(0, true, dest + Vector3.up * Random.Range(0.2f, 1.7f) + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 1.2f, Vector3.zero,
                            0.15f, 0.6f, 0.6f, MagicFx.Shadow, 0.5f);
            player.hud?.Flash(new Color(0.25f, 0.1f, 0.4f), 0.45f, 0.18f);
            Sfx.UI(SoundBank.MagiaOmbra, 0.8f);
        }

        void Wave(Vector3 palm, float dmgK, float rangeK)
        {
            Vector3 f = Cam.forward; f.y = 0f; f.Normalize();
            float range = 6f * rangeK;
            Vector3 origin = player.transform.position + Vector3.up * 1f;
            MagicFx.Ring(origin + f * 0.4f, f, range, new Color(0.85f, 0.55f, 1f), 56, 200f);
            MagicFx.Flash(palm, new Color(0.85f, 0.55f, 1f), 5f, 8f, 0.35f);
            Sfx.At(SoundBank.MagiaOnda, palm, 0.95f, 1f, 2f, 40f);
            player.Shake(palm, 0.5f, 6f);
            var list = new List<Enemy>(Enemy.All);
            foreach (var e in list)
            {
                if (e == null || e.hp <= 0f) continue;
                Vector3 to = e.transform.position - player.transform.position; to.y = 0f;
                float d = to.magnitude;
                if (d > range || (d > 0.5f && Vector3.Dot(to / d, f) < -0.17f)) continue;
                if (!HotelNav.ClearLine(origin, e.AimPoint)) continue;
                float k = Mathf.Lerp(1f, 0.5f, d / range);
                MagicFx.Hurt(e, 70f * dmgK * k, e.AimPoint);
                statHits++;
                if (e == null || e.hp <= 0f) continue;
                var ag = e.GetComponent<UnityEngine.AI.NavMeshAgent>();
                Vector3 push = (d > 0.1f ? to / d : f) * (9f * k);
                if (ag != null && ag.enabled && ag.isOnNavMesh) ag.velocity += push;
                EnemyStatus.Ensure(e)?.Stun(0.5f);
            }
        }

        // ------------------------------------------------------------------ sigilli
        void InfusionEvent(int node, int slot, string ev)
        {
            var hand = castHand;
            var aura = weapon != null ? WeaponAura.For(weapon.ViewRootT, weapon.Rig) : null;
            Color c = MagicFx.OfInfusion(node);
            Vector3 palm = hand != null ? hand.PalmWorld : Cam.position + Cam.forward * 0.5f;
            switch (ev)
            {
                case "trace": SetPhase("trace", c, 0.6f); Sfx.At(SoundBank.MagiaTraccia, palm, 0.45f, 1f, 1f, 8f); return;
                case "trace_end": SetPhase(null); return;
                case "tap": if (hand != null) { MagicFx.Sparks(hand.TipWorld(0), c, 8, 1.2f, 0.006f); Sfx.At(SoundBank.MagiaTocco, hand.TipWorld(0), 0.5f, 1f, 1f, 6f); } return;
                case "grab": Sfx.At(SoundBank.MagiaPresa, palm, 0.5f, node == 18 ? 0.7f : 1f, 1f, 8f); if (node == 16) SetPhase("hold", c, 0.5f); return;
                case "cut": SetPhase("cut", c, 0.6f); aura?.Set(13, false, palm, 0.5f); Sfx.At(SoundBank.MagiaSangue, palm, 0.5f, 1.2f, 1f, 8f); return;
                case "drip": SetPhase("drip", c, 0.5f); return;
                case "ignite": SetPhase("flame", MagicFx.Fire, 1.4f); MagicFx.Burst(palm, MagicFx.Fire, 0.12f); Sfx.At(SoundBank.MagiaFuoco, palm, 0.55f, 1.2f, 1f, 10f); return;
                case "trail": SetPhase("trail", MagicFx.Fire, 1.5f); aura?.Set(14, false, palm, 0.55f); return;
                case "ichor": SetPhase("ichor", c, 0.8f); Sfx.At(SoundBank.MagiaIcore, palm, 0.5f, 1f, 1f, 8f); return;
                case "pour": SetPhase("pour", c, 0.8f); aura?.Set(15, false, palm, 0.4f); return;
                case "arcs": SetPhase("arcs", c, 1.2f); Sfx.At(SoundBank.MagiaScintille, palm, 0.6f, 1.1f, 1f, 10f); return;
                case "slap":
                    if (hand != null) { Vector3 b = hand.PartWorld(OffHand.Part.MagBase); MagicFx.Sparks(b, c, 26, 3.5f, 0.012f); MagicFx.Flash(b, c, 3f, 3f, 0.25f); }
                    aura?.Set(16, false, palm, 0.35f);
                    Sfx.At(SoundBank.MagiaSchiaffo, palm, 0.8f, 1f, 1f, 12f);
                    SetPhase(null);
                    return;
                case "press": SetPhase("frost", MagicFx.Frost, 0.8f); aura?.Set(17, false, palm, 0.6f); Sfx.At(SoundBank.MagiaGelo, palm, 0.55f, 1.3f, 1f, 10f); return;
                case "runes":
                    SetPhase("runes", MagicFx.Cursed, 1.6f);
                    if (hand != null) aura?.Set(18, true, hand.PartWorld(OffHand.Part.Muzzle), 0.9f);
                    Sfx.At(SoundBank.MagiaRune, palm, 0.9f, 1f, 1.5f, 15f);
                    return;
                case "burn": SetPhase("burn"); return;
                case "fine": EndGesture(); RefreshAura(); return;
                case "annullata": EndGesture(); RefreshAura(); return;
                case "cast": break;
                default: return;
            }
            // ---- il sigillo prende l'arma
            S.ApplyInfusion(node, slot);
            if (node == 18) { player.hud?.Banner("Arma Maledetta", "Quindici secondi: danno doppio, ogni sigillo insieme, munizioni infinite."); player.hud?.Flash(MagicFx.Cursed, 0.6f, 0.4f); }
            else player.hud?.Toast($"{Grimoire.Arsenal[node].name}: l'arma porta il sigillo per {MagicState.InfusionTime:F0} s.");
            // gli altri sigilli hanno gia' acceso l'arma nel gesto (taglio, scia, icore, schiaffo, brina, rune): Critici si accende ora
            if (aura != null && node == 12) aura.Set(12, false, hand != null ? hand.TipWorld(0) : palm, 0.5f);
            MagicFx.Flash(palm, c, 2.5f, 3f, 0.35f);
            Sfx.At(SoundBank.MagiaSigillo, palm, 0.7f, node == 18 ? 0.7f : 1f, 1f, 12f);
        }

        /// <summary>L'aspetto dell'arma in mano secondo lo stato (sigillo e Arma Maledetta), dopo un cambio d'arma o alla fine di un sigillo.</summary>
        public void RefreshAura()
        {
            if (weapon == null || weapon.ViewRootT == null) return;
            var aura = WeaponAura.For(weapon.ViewRootT, weapon.Rig);
            if (aura == null) return;
            int slot = weapon.SlotIndex;
            var s = S;
            bool cursed = s.CursedOn(slot);
            int inf = s.InfusionOn(slot);
            if (castNode >= 0 && castInfusion) return;          // durante il gesto lo decide il gesto
            aura.Set(cursed ? 18 : inf, cursed);
        }

        int lastAuraSlot = -2, lastAuraInf = -2; bool lastAuraCursed;
        Transform lastViewRoot;
        void UpdateAura()
        {
            if (weapon == null) return;
            int slot = weapon.SlotIndex;
            var s = S;
            int inf = s.InfusionOn(slot);
            bool cursed = s.CursedOn(slot);
            if (slot != lastAuraSlot || inf != lastAuraInf || cursed != lastAuraCursed || weapon.ViewRootT != lastViewRoot)
            {
                lastAuraSlot = slot; lastAuraInf = inf; lastAuraCursed = cursed; lastViewRoot = weapon.ViewRootT;
                RefreshAura();
            }
        }

        // ------------------------------------------------------------------ per fotogramma
        void PhaseTick(float dt)
        {
            if (drainTarget != null) DrainTick(dt);
            if (castHand == null || phase == null) { if (handLight != null && castHand == null) { Destroy(handLight.gameObject); handLight = null; } return; }
            phaseT += dt;
            var h = castHand;
            Vector3 palm = h.PalmWorld, n = h.PalmNormal;
            if (handLight != null) handLight.transform.position = palm + n * 0.18f + Vector3.up * 0.05f;
            switch (phase)
            {
                case "flame":
                    for (int i = 0; i < 3; i++) MagicFx.Flame(palm + n * 0.02f, n * 0.5f + Vector3.up * 0.5f, 0.9f + Mathf.Min(1f, phaseT * 3f));
                    break;
                case "spark":
                    for (int f = 0; f < 4; f++) if (Random.value < 0.45f) MagicFx.Arc(h.TipWorld(f), h.TipWorld(Random.Range(0, 5)), MagicFx.Shock, 0.004f, 3, 0.6f);
                    if (Random.value < 0.5f) MagicFx.Sparks(h.TipWorld(Random.Range(0, 5)), MagicFx.Shock, 2, 0.6f, 0.005f);
                    break;
                case "frost":
                    if (Random.value < 0.7f) MagicFx.Mist(palm + n * 0.015f + Random.insideUnitSphere * 0.02f, n * 0.08f + Vector3.up * 0.04f, MagicFx.Frost, 0.02f, 0.8f, 0.3f);
                    break;
                case "cone":
                    if (phaseT < 0.32f)
                        for (int i = 0; i < 7; i++)
                        {
                            Vector3 d = (Cam.forward + Random.insideUnitSphere * 0.38f).normalized;
                            MagicFx.Mist(palm + d * 0.05f, d * Random.Range(7f, 11f) * S.SpellRange, MagicFx.Frost, 0.06f, 0.75f, 0.32f);
                            if (Random.value < 0.4f) Fx.Particle(1, true, palm, d * Random.Range(5f, 9f), Vector3.down * 2f, 0.01f, 0.003f, 0.6f, Color.white);
                        }
                    else SetPhase(null);
                    break;
                case "blood":
                    if (Random.value < 0.4f) MagicFx.Glow(h.TipWorld(0), MagicFx.Blood, 0.012f, 0.2f, 0.5f);
                    if (Random.value < 0.4f) MagicFx.Glow(h.TipWorld(3), MagicFx.Blood, 0.012f, 0.2f, 0.5f);
                    break;
                case "drain":
                    if (Random.value < 0.6f) MagicFx.Glow(palm, MagicFx.Blood, 0.03f, 0.15f, 0.6f);
                    break;
                case "gather":
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 o = palm + Random.onUnitSphere * 0.15f;
                        Fx.Particle(0, true, o, (palm - o) * 6f, Vector3.zero, 0.02f, 0.005f, 0.16f, new Color(0.85f, 0.55f, 1f), 0.7f);
                    }
                    break;
                case "veins":
                    for (int i = 0; i < 2; i++)
                    {
                        int f = Random.Range(0, 5);
                        MagicFx.Glow(Vector3.Lerp(palm, h.TipWorld(f), Random.value), MagicFx.Essence, 0.008f, 0.3f, 0.7f);
                    }
                    if (Random.value < 0.5f) MagicFx.Flame(palm, Vector3.up, 0.6f);
                    break;
                case "trace":
                    // la scia del dito resta incisa nell'aria accanto all'arma per un attimo
                    Fx.Particle(0, true, h.TipWorld(0), Vector3.zero, Vector3.zero, 0.006f, 0.0045f, 1.3f, MagicFx.Crit, 0.9f);
                    break;
                case "cut":
                    if (Random.value < 0.8f) Fx.Particle(0, true, palm + Random.insideUnitSphere * 0.012f, Vector3.zero, Vector3.zero, 0.008f, 0.006f, 1.0f, MagicFx.Blood, 0.8f);
                    if (Random.value < 0.3f) MagicFx.Drip(palm, new Color(0.35f, 0.01f, 0.01f), 0.006f);
                    break;
                case "drip":
                    if (Random.value < 0.6f) MagicFx.Drip(palm + n * 0.01f + Random.insideUnitSphere * 0.01f, new Color(0.38f, 0.01f, 0.01f), 0.007f);
                    break;
                case "trail":
                    for (int i = 0; i < 3; i++) MagicFx.Flame(palm + n * 0.01f + Random.insideUnitSphere * 0.015f, Vector3.up, 0.8f);
                    break;
                case "ichor":
                    if (Random.value < 0.8f) MagicFx.Glow(palm + n * 0.012f + Random.insideUnitSphere * 0.01f, MagicFx.Acid, 0.012f, 0.25f, 0.6f);
                    if (Random.value < 0.3f) Fx.Particle(0, false, palm + n * 0.01f, Vector3.up * 0.05f, Vector3.zero, 0.006f, 0.01f, 0.4f, new Color(0.25f, 0.55f, 0.08f), 0.9f);
                    break;
                case "pour":
                    for (int i = 0; i < 2; i++) MagicFx.Drip(h.TipWorld(Random.Range(0, 4)), new Color(0.3f, 0.7f, 0.1f), 0.006f);
                    break;
                case "hold":
                case "arcs":
                    if (phase == "arcs")
                        for (int f = 0; f < 5; f++) if (Random.value < 0.5f) MagicFx.Arc(h.TipWorld(f), h.PartWorld(OffHand.Part.Mag) + Random.insideUnitSphere * 0.02f, MagicFx.Shock, 0.005f, 4, 0.5f);
                    break;
                case "runes":
                    if (Random.value < 0.7f) MagicFx.Flame(palm + Random.insideUnitSphere * 0.03f, Vector3.up, 0.7f);
                    if (Random.value < 0.4f) Fx.Particle(0, false, palm, Vector3.up * 0.25f, Vector3.zero, 0.02f, 0.08f, 0.9f, new Color(0.05f, 0.01f, 0.01f), 0.4f);
                    break;
                case "burn":
                    for (int f = 0; f < 5; f++) if (Random.value < 0.4f) Fx.Particle(0, false, h.TipWorld(f), Vector3.up * 0.18f, Vector3.zero, 0.01f, 0.05f, 0.9f, new Color(0.12f, 0.11f, 0.1f), 0.4f);
                    if (phaseT > 0.8f) SetPhase(null);
                    break;
            }
        }

        // ------------------------------------------------------------------ Essenza Maledetta
        void EssenzaTick(float dt, MagicState s)
        {
            if (s.EssenzaLeft <= 0f) return;
            player.Heal(6f * dt);
            auraTick -= dt;
            if (auraTick <= 0f)
            {
                auraTick = 0.5f;
                Vector3 me = player.transform.position;
                var list = new List<Enemy>(Enemy.All);
                foreach (var e in list)
                {
                    if (e == null || e.hp <= 0f) continue;
                    if ((e.transform.position - me).sqrMagnitude > 9f) continue;
                    MagicFx.Hurt(e, 10f, e.AimPoint, true);
                    if (e != null && e.hp > 0f) EnemyStatus.Ensure(e)?.Burn(8f, 1.2f);
                }
            }
            // braci rosse che salgono intorno alle braccia
            if (Random.value < 0.6f && Cam != null)
                Fx.Particle(4, true, Cam.position + Cam.forward * 0.45f + Cam.right * Random.Range(-0.3f, 0.3f) - Cam.up * Random.Range(0.2f, 0.35f), Vector3.up * 0.4f, Vector3.zero, 0.01f, 0.025f, 0.6f, Color.white, 0.7f);
        }

        // ------------------------------------------------------------------ lanci di mano sinistra
        /// <summary>Lancio dell'equipaggiamento con la mano sinistra: 'release' arriva quando l'oggetto lascia la mano, col punto (mondo).
        /// Falso se la mano non e' libera (allora il chiamante lancia come prima).</summary>
        public bool Throw(System.Action<Vector3> release)
        {
            if (weapon == null || weapon.OffHand == null || !HandFree(out _)) return false;
            var hand = weapon.OffHand;
            var anim = OffHandAnims.Throw(hand.OneHanded);
            BeginGesture(-1, false, hand);
            hand.Play(anim, ev =>
            {
                if (ev == "release") release?.Invoke(hand.PalmWorld);
                else if (ev == "fine" || ev == "annullata") EndGesture();
            });
            return true;
        }
    }
}
