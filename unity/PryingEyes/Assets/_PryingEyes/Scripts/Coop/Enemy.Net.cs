using UnityEngine;

namespace PryingEyes
{
    /// <summary>Cose che l'ospite racconta degli attacchi di un nemico, perche' gli altri ne mostrino animazione e suono.</summary>
    public enum EnemyEvt : byte { Attack = 1, Strike, Cast, Hit, Whistle, Help }

    /// <summary>Lo stato di un nemico come viaggia in rete (non affidabile, ~10 Hz): 17 byte piu' l'id.</summary>
    public struct EnemyNet
    {
        public Vector3 pos;
        public float yaw, pitch;       // radianti
        public float speed;            // m/s
        public float hp;               // 0..1
        public Alert alert;
        public bool seeing, relentless;
        public int tgt;                // slot di chi sta guardando

        public void Write(NetWriter w)
        {
            w.V3(pos).U8(CoopProtocol.QuantYaw(yaw)).U8(CoopProtocol.QuantPitch(pitch))
             .U8(Mathf.Clamp(Mathf.RoundToInt(speed * 20f), 0, 255))
             .U8(Mathf.Clamp(Mathf.CeilToInt(hp * 255f), 1, 255))
             .U8((int)alert | (seeing ? 4 : 0) | (relentless ? 8 : 0) | ((tgt & 3) << 4));
        }

        public static EnemyNet Read(ref NetReader r)
        {
            var s = new EnemyNet { pos = r.V3() };
            s.yaw = CoopProtocol.DequantYaw((byte)r.U8());
            s.pitch = CoopProtocol.DequantPitch((byte)r.U8());
            s.speed = r.U8() / 20f;
            s.hp = r.U8() / 255f;
            int f = r.U8();
            s.alert = (Alert)(f & 3);
            s.seeing = (f & 4) != 0;
            s.relentless = (f & 8) != 0;
            s.tgt = (f >> 4) & 3;
            return s;
        }
    }

    /// <summary>Il nemico in cooperativa. Sull'ospite e' quello di sempre (pensa, cammina sulla navmesh, sceglie un bersaglio tra i giocatori);
    /// sugli ospitati e' un pupazzo: stesso corpo, stesse animazioni e stessi suoni, ma niente intelligenza, lo muove quello che l'ospite
    /// racconta (EnemyNet e gli eventi). Un colpo dato a un pupazzo viene girato all'ospite (HitClaim), che lo applica e paga i punti a chi ha sparato.</summary>
    public partial class Enemy
    {
        /// <summary>Pupazzo comandato dalla rete. Non serializzato, come gli altri campi aggiunti dopo.</summary>
        [System.NonSerialized] public bool puppet;
        /// <summary>Identificativo di rete: lo assegna l'ospite quando nasce, i pupazzi portano lo stesso.</summary>
        [System.NonSerialized] public int netId = -1;

        EnemyNet net;
        bool netInit;
        float speedSm, retargetT;
        int netTgt;

        // ------------------------------------------------------------------ ospite
        /// <summary>Lo stato da raccontare agli altri.</summary>
        public EnemyNet NetState()
        {
            var s = new EnemyNet
            {
                pos = transform.position,
                yaw = transform.eulerAngles.y * Mathf.Deg2Rad,
                pitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x) * Mathf.Deg2Rad,
                speed = vel.magnitude,
                hp = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 1f,
                alert = alert, seeing = seeing, relentless = relentless,
                tgt = tgt != null ? tgt.Slot : 0,
            };
            return s;
        }

        /// <summary>Chi caccia per round (onnisciente) insegue il piu' vicino, ma senza saltare da uno all'altro: cambia solo se l'altro e'
        /// molto piu' vicino. Da solo non c'e' niente da scegliere.</summary>
        void RetargetOmniscient(float dt, Vector3 gp)
        {
            if (Targets.Count < 2) return;
            retargetT -= dt;
            if (retargetT > 0f) return;
            retargetT = 0.6f + Random.value * 0.3f;
            var n = Targets.Nearest(gp);
            if (n == tgt) return;
            float dn = (n.Feet - gp).magnitude;
            float dc = tgt != null && tgt.Alive ? (tgt.Feet - gp).magnitude : float.MaxValue;
            if (dn < dc * 0.75f) SwitchTarget(n);
        }

        /// <summary>Un compagno se n'e' andato (o e' stato distrutto): chi lo guardava sceglie un altro bersaglio.</summary>
        public static void OnTargetGone(ITarget t)
        {
            foreach (var e in all)
            {
                if (e == null) continue;
                if (e.meleeVictim == t) e.meleeVictim = null;
                if (e.tgt == t) e.tgt = Targets.Nearest(e.transform.position);
            }
        }

        // ------------------------------------------------------------------ ospitato: pupazzo
        /// <summary>Un pupazzo, nato dall'annuncio dell'ospite.</summary>
        public static Enemy SpawnPuppet(int id, EnemyType t, in EnemyNet s)
        {
            var e = Spawn(t, s.pos, null, true);
            e.netId = id;
            e.ApplyNet(s, true);
            return e;
        }

        /// <summary>Il nuovo stato dall'ospite. Con 'snap' (appena nato) salta subito li' senza scivolare.</summary>
        public void ApplyNet(in EnemyNet s, bool snap = false)
        {
            if (dead) return;
            net = s;
            hp = Mathf.Max(0.01f, s.hp * maxHp);
            relentless = s.relentless;
            seeing = s.seeing;
            netTgt = s.tgt;
            if (snap || !netInit)
            {
                transform.position = s.pos;
                transform.rotation = Quaternion.Euler(Archetype == EnemyType.Occhio ? s.pitch * Mathf.Rad2Deg : 0f, s.yaw * Mathf.Rad2Deg, 0f);
                speedSm = s.speed;
                alert = s.alert;
                PuppetAlertVisual();
                netInit = true;
                return;
            }
            PuppetAlert(s.alert);
        }

        /// <summary>Dove guarda l'occhio: la visuale del giocatore che l'ospite dice essere il bersaglio.</summary>
        Transform PuppetLookAt()
        {
            if (netTgt == Coop.Slot) return Targets.Local.LookAt;
            var g = Coop.Game;
            return g != null && g.Players.TryGetValue(netTgt, out var rp) && rp != null ? rp.LookAt : null;
        }

        void PuppetAlertVisual()
        {
            if (eye == null) return;
            eye.lookAt = alert == Alert.Patrol || alert == Alert.Searching ? null : PuppetLookAt();
            eye.glowBoost = alert == Alert.Hunting ? 1.9f : alert == Alert.Suspicious ? 1.35f : 0.85f;
        }

        /// <summary>Cambio di allerta sull'ospite: qui solo i suoni e l'occhio (il grido, il fischietto e i richiami li fa l'ospite).</summary>
        void PuppetAlert(Alert a)
        {
            if (a == alert) { if (eye != null && eye.lookAt != null && alert == Alert.Hunting) eye.lookAt = PuppetLookAt(); return; }
            var old = alert;
            alert = a;
            Vector3 p = transform.position + Vector3.up * 1.6f;
            if (a == Alert.Suspicious) Sfx.At(SoundBank.Suspect, p, 0.55f, basePitch, 2f, 26f);
            else if (a == Alert.Hunting)
            {
                Sfx.At(SoundBank.AlertSting, p, 0.9f, basePitch * (Archetype == EnemyType.Abate ? 0.8f : 1f), 3f, 48f);
                if (old != Alert.Hunting && seeing && netTgt == Coop.Slot) Sfx.UI(SoundBank.AlertSting, 0.2f, 1.5f);   // il tuo cuore: ti ha visto
                if (eye != null) eye.Stare(1.0f);
            }
            PuppetAlertVisual();
        }

        /// <summary>Ogni fotogramma di un pupazzo: segue lo stato dell'ospite con un po' di morbidezza e fa quello che farebbe il nemico vero
        /// (camminata, passi, sfera magica, ronzio), senza pensare.</summary>
        void UpdatePuppet(float dt)
        {
            if (!netInit) return;
            flinchT -= dt;
            atkLock -= dt;
            if (strikeAt >= 0f) strikeAt -= dt;
            Vector3 gp = transform.position;
            Vector3 to = net.pos - gp;
            if (to.sqrMagnitude > 49f) gp = net.pos;                         // salto (serranda, spinta forte): niente scivolata
            else gp += to * (1f - Mathf.Exp(-12f * dt));
            transform.position = gp;
            var want = Quaternion.Euler(Archetype == EnemyType.Occhio ? net.pitch * Mathf.Rad2Deg : 0f, net.yaw * Mathf.Rad2Deg, 0f);
            if (!IsFlying(type) || atkLock <= 0)
                transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-10f * dt));
            speedSm = Mathf.Lerp(speedSm, net.speed, 1f - Mathf.Exp(-8f * dt));
            float v = speedSm;
            vel = transform.forward * v;

            if (Archetype == EnemyType.Monaco || Archetype == EnemyType.Abate)
            {
                bool abate = Archetype == EnemyType.Abate;
                float pv = abate ? Mathf.Clamp(v / (MonacoWalkSpeed * AbateScale * AbateAnimSpeed), 0.5f, 2.6f)
                                 : Mathf.Clamp(v / MonacoWalkSpeed, 0.6f, 2.2f);
                if (anim != null)
                {
                    anim.SetFloat("Velocita", v);
                    anim.SetFloat("PassoVel", pv);
                }
                if (v > 0.25f)
                {
                    stepT -= dt * pv * (abate ? AbateAnimSpeed : 1f);
                    if (stepT <= 0f)
                    {
                        stepT = 1.05f;
                        Vector3 tap = transform.TransformPoint(new Vector3(-0.31f, 0f, 0.37f));
                        if (abate) Sfx.At(SoundBank.Tap, tap, 0.95f, Random.Range(0.60f, 0.68f), 2f, 32f);
                        else Sfx.At(SoundBank.Tap, tap, 0.55f, Random.Range(0.92f, 1.08f), 1.5f, 24f);
                    }
                }
                if (strikeAt >= 0f) MeleeSwish();
                if (castAt >= 0f)
                {
                    // la sfera si carica sul bastone; il dardo arriva da solo (messaggio Bolt)
                    castAt -= dt;
                    float p = 1f - Mathf.Clamp01(castAt / Mathf.Max(0.1f, castWind));
                    if (castOrb != null)
                    {
                        castOrb.SetActive(castAt > 0f);
                        castOrb.transform.localScale = Vector3.one * Mathf.Lerp(0.04f, 0.20f, p);
                        if (castLight != null) castLight.intensity = castAt > 0f ? Mathf.Lerp(0.4f, 3.0f, p) : 0f;
                    }
                }
            }
            else if (loop != null) loop.pitch = basePitch * (0.85f + vel.magnitude / 14f);   // l'occhio accelera: il ronzio sale
            if (eye != null && IsFlying(type)) eye.lookAt = atkLock > 0 ? null : PuppetLookAt();
            if (alienRig != null) alienRig.Animate(phase, v, alert == Alert.Hunting, strikeAt >= 0f, FlightElapsed);
        }

        /// <summary>Un attacco, un lancio o un grido sull'ospite: qui l'animazione e il suono.</summary>
        public void PuppetEvent(EnemyEvt evt, Vector3 pos, float extra)
        {
            if (dead) return;
            switch (evt)
            {
                case EnemyEvt.Attack:
                    if (Archetype == EnemyType.Occhio)
                    {
                        strikeAt = FlightContact; atkLock = FlightDuration;
                        if (pos.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(pos.normalized, Vector3.up);
                        FlightAnticipation();
                    }
                    else
                    {
                        if (type == EnemyType.Portachiavi)
                        {
                            Sfx.At(SoundBank.KeeperBell, transform.position + Vector3.up * 1.4f, 0.65f, 1f, 2f, 28f);
                        }
                        strikeAt = MeleeWindup / MeleeRate; atkLock = MeleeDuration / MeleeRate + 0.05f; meleeSwished = false;
                        if (anim != null) { anim.ResetTrigger("Colpito"); anim.SetTrigger("Attacca"); }
                    }
                    break;
                case EnemyEvt.Strike:
                    strikeAt = -1f;
                    PlayMeleeContact(pos, extra > 0.5f);
                    break;
                case EnemyEvt.Cast:
                    if (anim != null) { anim.ResetTrigger("Colpito"); anim.SetTrigger("Lancia"); }
                    EnsureCastOrb();
                    castOrb.SetActive(true);
                    castAt = castWind; atkLock = castWind + 0.5f;
                    if (castLight != null) castLight.intensity = 0.2f;
                    Sfx.At(SoundBank.Whisper, castOrb.transform.position, 0.5f, Random.Range(0.85f, 1.05f), 2f, 24f);
                    break;
                case EnemyEvt.Hit:
                    BeginHitFlash();
                    if (Archetype == EnemyType.Monaco && atkLock <= 0f && anim != null) anim.SetTrigger("Colpito");
                    if (Archetype == EnemyType.Abate && atkLock <= 0f && anim != null && flinchT <= 0f) { anim.SetTrigger("Colpito"); flinchT = 1.5f; }
                    if (Archetype == EnemyType.Occhio && eye != null) eye.Hit();
                    break;
                case EnemyEvt.Whistle:
                    Sfx.At(SoundBank.Whistle, transform.position + Vector3.up * 1.6f, 1f, Random.Range(0.96f, 1.06f), 4f, 60f);
                    break;
                case EnemyEvt.Help:
                    Sfx.At(SoundBank.AlertSting, transform.position + Vector3.up * 2f, 0.9f, 0.62f, 4f, 55f);
                    break;
            }
        }

        /// <summary>Morto sull'ospite: il pupazzo crolla come il nemico vero (nessun punto: li paga l'ospite).</summary>
        public void PuppetDie(bool headshot, DamageSource src)
        {
            if (dead) return;
            Die(headshot, src);
        }

        /// <summary>Un colpo a un pupazzo: qui il lampo, il segnalino e il suono (previsione), il verdetto e i punti li da' l'ospite.</summary>
        void PuppetDamage(float dmg, Vector3 point, bool headshot, bool awardPoints, DamageSource src)
        {
            hp -= dmg;
            BeginHitFlash();
            GameManager.I.hud?.Hitmarker(hp <= 0f, headshot);
            GameManager.shotsHit++;
            Synth.Hit();
            Coop.Game?.SendHitClaim(this, dmg, point, headshot, awardPoints, src);
        }
    }
}
