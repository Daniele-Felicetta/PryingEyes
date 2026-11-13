using UnityEngine;

namespace PryingEyes
{
    public partial class Enemy
    {
        public const float MeleeWindup = 18f / 30f, MeleeDuration = 38f / 30f;
        bool meleeSwished;
        ITarget meleeVictim;
        float meleeAdvance;
        public bool PreparingMelee => Archetype != EnemyType.Occhio && strikeAt >= 0f && castAt < 0f;
        public bool RecoveringMelee => Archetype != EnemyType.Occhio && atkLock > 0f && strikeAt < 0f && castAt < 0f;
        float MeleeRate => Archetype == EnemyType.Abate ? AbateAnimSpeed : 1f;

        bool InMeleeArc(Vector3 feet)
        {
            Vector3 to = feet - transform.position; to.y = 0;
            return to.sqrMagnitude < 0.01f || Vector3.Dot(transform.forward, to.normalized) >= 0.57f;
        }

        void BeginMelee(float cooldown)
        {
            atkCd = cooldown; strikeAt = MeleeWindup / MeleeRate;
            atkLock = MeleeDuration / MeleeRate + 0.05f;
            meleeSwished = false; meleeVictim = tgt;
            Vector3 approach=tgt.Feet-transform.position;approach.y=0;
            meleeAdvance=Mathf.Min(0.36f,Mathf.Max(0,approach.magnitude-0.95f));
            if (type == EnemyType.Portachiavi) Sfx.At(SoundBank.KeeperBell, transform.position + Vector3.up * 1.4f, 0.65f, 1, 2, 28);
            if (anim != null) { anim.ResetTrigger("Colpito"); anim.SetTrigger("Attacca"); }
            Coop.Game?.EnemyEvent(this, EnemyEvt.Attack, Vector3.zero);
        }

        void MeleeSwish()
        {
            if (meleeSwished || strikeAt > 4f / 30f / MeleeRate) return;
            meleeSwished = true;
            Sfx.At(SoundBank.KnifeSwish, transform.position + Vector3.up * 1.2f, 0.8f, Archetype == EnemyType.Abate ? 0.58f : 0.72f, 2, 24);
        }

        void AdvanceMelee(float before,float after,float dt)
        {
            float moveTime=0.18f/MeleeRate;
            float Curve(float remaining)=>Mathf.SmoothStep(0,1,Mathf.Clamp01((moveTime-remaining)/moveTime));
            float distance=meleeAdvance*(Curve(after)-Curve(before));
            if(distance<=0)return;
            Vector3 direction=transform.forward;direction.y=0;direction.Normalize();
            if(Physics.CapsuleCast(transform.position+Vector3.up*0.4f,transform.position+Vector3.up*1.5f,0.3f,direction,out var hit,distance+0.02f,HotelNav.EnvMask,QueryTriggerInteraction.Ignore))
                distance=Mathf.Max(0,hit.distance-0.02f);
            Vector3 start=transform.position;
            if(OnNav){agent.Move(direction*distance);transform.position=agent.nextPosition;}
            else transform.position+=direction*distance;
            if(dt>0)vel=(transform.position-start)/dt;
        }

        void ResolveMelee(float damage, float reach)
        {
            var victim = meleeVictim;
            Vector3 feet = victim != null ? victim.Feet : transform.position + transform.forward * reach;
            Vector3 delta = feet - transform.position;
            float horizontal = new Vector2(delta.x, delta.z).magnitude, vertical = Mathf.Abs(delta.y);
            Vector3 contact = feet + Vector3.up * (victim != null ? Mathf.Min(1.1f, victim.BodyHeight * 0.65f) : 1f);
            bool hit = victim != null && victim.Alive && InMeleeArc(feet) &&
                InReach(transform.position + Vector3.up * 1.4f, contact, horizontal, vertical, reach);
            if (hit)
            {
                victim.Hurt(damage, transform.position);
                if (victim is LocalTarget) GameManager.damageTaken++;
                if (type == EnemyType.Infermiere) victim.Sedate(2.8f);
            }
            Vector3 effect = hit ? MeleeContactPoint(victim) : contact;
            PlayMeleeContact(effect, hit);
            Coop.Game?.EnemyEvent(this, EnemyEvt.Strike, effect, hit ? 1 : 0);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] mischia {type}: {(hit ? "contatto" : "a vuoto")} distanza={horizontal:F2} dislivello={vertical:F2}");
            meleeVictim = null;
        }

        Vector3 MeleeContactPoint(ITarget victim)
        {
            Vector3 point = victim.LookAt != null ? victim.LookAt.position - Vector3.up * 0.12f
                : victim.LeanedPoint(Mathf.Min(1.5f, victim.BodyHeight * 0.85f));
            Vector3 toward = transform.position - point; toward.y = 0;
            return point + toward.normalized * Mathf.Min(0.4f, toward.magnitude * 0.5f);
        }

        void PlayMeleeContact(Vector3 position, bool hit)
        {
            if (!hit) return; // il colpo a vuoto ha soltanto il fruscio del gesto, nessun falso impatto a terra.
            if (glowMat != null) Fx.EnemyImpact(position, -transform.forward, glowMat.GetColor("_EmissionColor"));
            Sfx.At(SoundBank.Strike, position, 0.75f, Archetype == EnemyType.Abate ? 0.72f : 0.95f, 2, 30);
        }
    }
}
