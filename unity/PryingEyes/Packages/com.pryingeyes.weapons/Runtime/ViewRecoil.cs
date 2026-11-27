namespace Production.Weapons
{
    /// <summary>The recoil of the view: a kick lifts a target, the target comes back to zero by itself, and the view follows the target a little behind. Pitch is up, yaw to one side, in degrees.
    /// The kick comes from a shot (<see cref="WeaponRecoil"/>) or from whatever else shakes the holder; the caller turns <see cref="Pitch"/> and <see cref="Yaw"/> into the view.</summary>
    public sealed class ViewRecoil
    {
        const float Ceiling = 20f;              // above ~20 degrees the holder looks at the ceiling and the kick is only an annoyance
        const float SideLimit = 14f;
        const float Pause = 0.05f;              // the target stays where the kick left it for this long before it comes back

        float targetPitch, targetYaw, pitch, yaw, recover = 8f, delay;

        /// <summary>Where the view is now: up.</summary>
        public float Pitch => pitch;

        /// <summary>Where the view is now: to the side.</summary>
        public float Yaw => yaw;

        /// <summary>Lifts the target by (<paramref name="v"/>, <paramref name="h"/>) degrees and sets how fast it comes back, in degrees per second.</summary>
        public void Kick(float v, float h, float recoverRate)
        {
            targetPitch += v;
            targetYaw += h;
            recover = recoverRate;
            delay = Pause;
            // the climb without compensation has a ceiling
            targetPitch = WeaponMath.Min(targetPitch, Ceiling);
            targetYaw = WeaponMath.Clamp(targetYaw, -SideLimit, SideLimit);
        }

        /// <summary>Lets <paramref name="dt"/> seconds pass: after the pause the target returns to zero (faster the higher it is), and the view closes on the target.</summary>
        public void Advance(float dt)
        {
            if (delay > 0f) delay -= dt;
            else
            {
                float rate = recover * (1f + WeaponMath.Abs(targetPitch) * 0.12f);
                targetPitch = WeaponMath.MoveTowards(targetPitch, 0f, rate * dt);
                targetYaw = WeaponMath.MoveTowards(targetYaw, 0f, rate * 0.6f * dt);
            }
            float fol = 1f - WeaponMath.Exp(-34f * dt);
            pitch += (targetPitch - pitch) * fol;
            yaw += (targetYaw - yaw) * fol;
        }

        /// <summary>Puts the view back at rest at once (a new life, getting on a vehicle). How fast it would have come back is kept.</summary>
        public void Clear()
        {
            pitch = yaw = targetPitch = targetYaw = 0f;
        }
    }
}
