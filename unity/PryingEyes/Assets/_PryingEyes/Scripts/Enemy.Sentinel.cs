using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    public partial class Enemy
    {
        const float SentinelRange = 18f, SentinelStandoff = 7f;
        const float SentinelWindup = 0.85f, SentinelRecovery = 0.66f;
        SentinelRig sentinel;
        Vector3 sentinelAim;
        float sentinelTimer;
        bool sentinelCharging;

        static GameObject BuildSentinel(out Material glow, out List<Material> flashes)
        {
            var actor = SentinelRig.Create();
            if (actor == null) return BuildOcchio(out glow, out flashes);
            glow = null;
            flashes = new List<Material>();
            return actor;
        }

        // This runs before the ordinary flight movement. The aim is committed when
        // the shutters open, so a sideways dodge or cover still defeats the shot.
        bool UpdateSentinelAttack(float dt, Vector3 center, Vector3 chest, bool canSee)
        {
            if (sentinelTimer > 0f)
            {
                sentinelTimer = Mathf.Max(0f, sentinelTimer - dt);
                if (sentinelCharging && sentinelTimer == 0f)
                {
                    Vector3 muzzle = center + transform.forward * 0.46f;
                    if (!HotelNav.ClearLine(center, muzzle)) muzzle = center;
                    MagicBolt.Launch(muzzle, muzzle + sentinelAim, 16f, sentinel: true);
                    PlaySentinelShot();
                    Coop.Game?.EnemyEvent(this, EnemyEvt.SentinelShot, Vector3.zero);
                }
                return true;
            }

            Vector3 aim = chest - center;
            if (atkCd > 0f || !canSee || aim.sqrMagnitude > SentinelRange * SentinelRange || aim.sqrMagnitude < 0.1f)
                return false;
            aim.Normalize();
            if (Vector3.Dot(transform.forward, aim) < 0.96f) return false;

            atkCd = Random.Range(3.5f, 4.15f);
            vel = Vector3.zero;
            PlaySentinelCharge(aim);
            Coop.Game?.EnemyEvent(this, EnemyEvt.SentinelCharge, aim);
            return true;
        }

        void PlaySentinelCharge(Vector3 aim)
        {
            sentinelAim = aim;
            transform.rotation = Quaternion.LookRotation(aim);
            sentinelCharging = true;
            sentinelTimer = SentinelWindup;
            sentinel?.Charge(SentinelWindup);
            Sfx.At(SoundBank.EyeStare, transform.position, 0.6f, 0.72f, 2f, 24f);
        }

        void PlaySentinelShot()
        {
            sentinelCharging = false;
            sentinelTimer = SentinelRecovery;
            sentinel?.Fire(SentinelRecovery);
        }
    }
}
