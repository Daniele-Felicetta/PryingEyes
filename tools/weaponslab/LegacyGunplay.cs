// Frozen reference of the weapon arithmetic as the game had it in commit e25e832, before it moved to the package: the expressions of
// Scripts/Weapon.cs (CurrentSpread, Tick, Shoot, FireRays, Spread, ShootRay) and of Scripts/PlayerController.cs and PlayerController.Ride.cs (the recoil of the view).
// Verification only, never compiled into the game or the package. Each method is the old text with these edits and no other: the members of the class it lived in are
// parameters (Def -> d, ads, bloom, OwnerMagic.SpreadMult * RideSpreadMult -> magic and ride, Time.time - lastShotPrev -> elapsed, Random.Range(-1f, 1f) -> noise,
// slots[cur].upg ? UpgDamage : 1f -> upgraded), and the engine members it called are the stand-ins of Mathf below (the engine's own definitions, test for test).
// Updates need an intentional review of what the gunplay means.
using System;

namespace Legacy.Gun
{
    /// <summary>The engine's Mathf, as far as the gunplay used it: UnityEngine.Mathf's definitions, written the way the engine writes them.</summary>
    static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = (PI * 2F) / 360F;
        public static float Clamp01(float value) { if (value < 0F) return 0F; else if (value > 1F) return 1F; else return value; }
        public static float Clamp(float value, float min, float max) { if (value < min) value = min; else if (value > max) value = max; return value; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Abs(float f) { return Math.Abs(f); }
        public static float Sign(float f) { return f >= 0F ? 1F : -1F; }
        public static float Sin(float f) { return (float)Math.Sin(f); }
        public static float Tan(float f) { return (float)Math.Tan(f); }
        public static float Exp(float power) { return (float)Math.Exp(power); }
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Abs(target - current) <= maxDelta) return target;
            return current + Sign(target - current) * maxDelta;
        }
    }

    /// <summary>The numbers of Weapon.cs that its gunplay worked out, as they were.</summary>
    static class OldWeapon
    {
        public const float UpgDamage = 1.7f;

        /// <summary>Weapon.CurrentSpread: the stance fields (moveNow, airNow, crouchNow) are results here.</summary>
        public static float CurrentSpread(Legacy.WeaponDef d, float speed, bool grounded, bool crouched, float ads, float bloom, float magic, float ride,
            out float moveNow, out float airNow, out float crouchNow)
        {
            moveNow = Mathf.Clamp01(speed / 5.6f); airNow = grounded ? 0f : 1f; crouchNow = crouched ? 1f : 0f;
            float baseDeg = Mathf.Lerp(d.hip, d.ads, ads);
            float mv = 1f + d.moveSpread * moveNow * (1f - ads * 0.6f);
            float c = crouched ? 0.72f : 1f;
            float air = 1f + airNow * 1.6f;
            return (baseDeg * mv * c * air + bloom * Mathf.Lerp(1f, 0.5f, ads)) * magic * ride;
        }

        /// <summary>Weapon.FireRays: the first shot after a pause (elapsed = Time.time - lastShotPrev).</summary>
        public static float FirstShot(float spread, float elapsed, float ads, float moveNow)
        {
            if (elapsed > 0.4f && ads > 0.8f && moveNow < 0.1f) spread *= 0.35f;
            return spread;
        }

        /// <summary>Weapon.FireRays: the second Spread of a shot of several pellets (the cone argument).</summary>
        public static float PelletCone(Legacy.WeaponDef d, float ads, int p)
        {
            return d.cone * Mathf.Lerp(1f, 0.7f, ads) * (p == 0 && d.pellets > 4 ? 0.2f : 1f);
        }

        /// <summary>Weapon.Spread: the radius that scales the random point of the unit circle.</summary>
        public static float ConeRadius(float deg)
        {
            return Mathf.Tan(deg * Mathf.Deg2Rad);
        }

        /// <summary>Weapon.ShootRay: the fraction of the damage left at a distance.</summary>
        public static float Falloff(Legacy.WeaponDef d, float dist)
        {
            return dist <= d.rangeNear ? 1f : Mathf.Lerp(1f, d.farMult, Mathf.Clamp01((dist - d.rangeNear) / Mathf.Max(0.1f, d.rangeFar - d.rangeNear)));
        }

        /// <summary>Weapon.ShootRay: the damage of a hit (headPerk = OwnerPerks.HeadMult).</summary>
        public static float Damage(Legacy.WeaponDef d, float fall, float dmgMul, bool head, float headPerk, bool upgraded)
        {
            return d.damage * fall * dmgMul * (head ? d.headMult * headPerk : 1f) * (upgraded ? UpgDamage : 1f);
        }

        /// <summary>Weapon.ShootRay: after a hit, whether the bullet goes on (false = break), and what it costs.</summary>
        public static bool PierceStep(Legacy.WeaponDef d, int extraPierce, ref int pierced, ref float dmgMul)
        {
            if (pierced >= d.pierce + extraPierce) return false;
            pierced++;
            dmgMul *= d.pierceMult;
            return true;
        }

        /// <summary>Weapon.Shoot: bloom after a shot.</summary>
        public static float BloomShot(float bloom, Legacy.WeaponDef d)
        {
            bloom = Mathf.Min(d.bloomMax, bloom + d.bloomShot);
            return bloom;
        }

        /// <summary>Weapon.Tick: bloom after dt seconds.</summary>
        public static float BloomRecover(float bloom, Legacy.WeaponDef d, float dt, float ads)
        {
            bloom = Mathf.Max(0f, bloom - d.bloomRecover * dt * (1f + ads));
            return bloom;
        }

        /// <summary>Weapon.Shoot: the three lines of the recoil of the view (noise = Random.Range(-1f, 1f), recoilMult = OwnerMagic.RecoilMult).</summary>
        public static void ViewKick(Legacy.WeaponDef d, int streak, float ads, float crouchNow, float recoilMult, float noise, out float rv, out float rh)
        {
            float dampen = Mathf.Lerp(1f, 0.72f, ads) * (crouchNow > 0.5f ? 0.85f : 1f) * recoilMult;
            rv = d.recV * (1f + 0.035f * Mathf.Min(streak, 10)) * dampen;
            rh = (noise * d.recH + Mathf.Sin(streak * 0.9f + d.id.Length) * d.recH * 0.45f) * dampen;
        }
    }

    /// <summary>The fields of PlayerController that held the recoil of the view, and the three places that worked on them.</summary>
    sealed class OldPlayerRecoil
    {
        public float recTargetP, recTargetY, recP, recY, recRecover = 8f, recDelay;

        /// <summary>PlayerController.Kick</summary>
        public void Kick(float v, float h, float recover)
        {
            recTargetP += v;
            recTargetY += h;
            recRecover = recover;
            recDelay = 0.05f;
            // tetto della salita senza compensare: oltre ~20 gradi si guarda il soffitto e il rinculo diventa solo fastidio
            recTargetP = Mathf.Min(recTargetP, 20f);
            recTargetY = Mathf.Clamp(recTargetY, -14f, 14f);
        }

        /// <summary>PlayerController.Update, on foot ("rinculo della visuale").</summary>
        public void OnFoot(float dt)
        {
            if (recDelay > 0f) recDelay -= dt;
            else
            {
                float rate = recRecover * (1f + Mathf.Abs(recTargetP) * 0.12f);
                recTargetP = Mathf.MoveTowards(recTargetP, 0f, rate * dt);
                recTargetY = Mathf.MoveTowards(recTargetY, 0f, rate * 0.6f * dt);
            }
            float fol = 1f - Mathf.Exp(-34f * dt);
            recP += (recTargetP - recP) * fol;
            recY += (recTargetY - recY) * fol;
        }

        /// <summary>PlayerController.Ride: the same, written again for the bike (rate is called rr there).</summary>
        public void OnBike(float dt)
        {
            if (recDelay > 0f) recDelay -= dt;
            else
            {
                float rr = recRecover * (1f + Mathf.Abs(recTargetP) * 0.12f);
                recTargetP = Mathf.MoveTowards(recTargetP, 0f, rr * dt);
                recTargetY = Mathf.MoveTowards(recTargetY, 0f, rr * 0.6f * dt);
            }
            float fol = 1f - Mathf.Exp(-34f * dt);
            recP += (recTargetP - recP) * fol;
            recY += (recTargetY - recY) * fol;
        }

        /// <summary>PlayerController (respawn) and PlayerController.Ride (getting on): "recP = recY = recTargetP = recTargetY = 0f".</summary>
        public void Clear() { recP = recY = recTargetP = recTargetY = 0f; }
    }
}
