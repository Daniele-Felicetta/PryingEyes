namespace Production.Weapons
{
    /// <summary>What a bullet of a weapon does on its way, in numbers: how much of its damage is left at a distance, what the damage of a hit is made of and the cone its pellets spread in.
    /// Where the bullet goes and whom it hits are the caller's; so is what the damage is then multiplied by in its game (perks, upgrades, the enemy's own armour).</summary>
    public static class WeaponBallistics
    {
        /// <summary>The fraction of the damage that is left at <paramref name="distance"/>: all of it up to the near range, then a straight line down to the far multiplier at the far range.</summary>
        public static float Falloff(WeaponDef d, float distance)
        {
            return distance <= d.rangeNear ? 1f : WeaponMath.Lerp(1f, d.farMult, WeaponMath.Clamp01((distance - d.rangeNear) / WeaponMath.Max(0.1f, d.rangeFar - d.rangeNear)));
        }

        /// <summary>The damage of a bullet (or a pellet) that reaches an enemy: the damage of the weapon, the <paramref name="fall"/> of <see cref="Falloff"/>, what the enemies it went through
        /// before took (<see cref="Pierce.Multiplier"/>), the head (the weapon's multiplier times the holder's) and the upgrade, multiplied in this order.</summary>
        public static float Damage(WeaponDef d, float fall, float pierceMultiplier, bool head, float headMultiplier, float upgradeMultiplier)
        {
            return d.damage * fall * pierceMultiplier * (head ? d.headMult * headMultiplier : 1f) * upgradeMultiplier;
        }

        /// <summary>The half-angle, in degrees, of the cone the pellets of a shot spread in, around where the shot itself goes: narrower when aiming, and a first pellet that is kept tight
        /// in a weapon of more than four. <paramref name="pellet"/> counts from 0.</summary>
        public static float PelletCone(WeaponDef d, float ads, int pellet)
        {
            return d.cone * WeaponMath.Lerp(1f, 0.7f, ads) * (pellet == 0 && d.pellets > 4 ? 0.2f : 1f);
        }
    }

    /// <summary>One bullet's passage through the enemies on its way: it goes through as many as the weapon allows (and the holder's extra), losing damage at each.
    /// Make it with <see cref="Begin"/>: the default value of a struct has no damage left.</summary>
    public struct Pierce
    {
        float multiplier;
        int passed;

        public static Pierce Begin() => new Pierce { multiplier = 1f, passed = 0 };

        /// <summary>What the damage of the next enemy is multiplied by: 1 for the first, then <see cref="WeaponDef.pierceMult"/> for each one passed.</summary>
        public float Multiplier => multiplier;

        /// <summary>Enemies the bullet has gone through so far.</summary>
        public int Passed => passed;

        /// <summary>Called after a hit. False: the bullet stops here. True: it goes on, and the next enemy takes less.</summary>
        public bool Continue(WeaponDef d, int extra)
        {
            if (passed >= d.pierce + extra) return false;
            passed++;
            multiplier *= d.pierceMult;
            return true;
        }
    }
}
