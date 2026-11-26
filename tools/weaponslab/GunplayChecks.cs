using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Production.Weapons;
using LegacyDef = Legacy.WeaponDef;
using NewDef = Production.Weapons.WeaponDef;
using Old = Legacy.Gun.OldWeapon;

// T23, step 2: the arithmetic of the gunplay (the spread with movement and aim, the bloom and its recovery, the recoil of the view and its recovery, the damage at a distance and
// through enemies, the cone of the pellets) against the frozen copy of the expressions the game had before they moved to the package (LegacyGunplay.cs). Every comparison is bit for
// bit: the same input gives the same float, to the last bit. The inputs are the 30 weapons of the catalogue and a few made-up ones that sit on the edges of the expressions, over
// grids that include the values where each test of an expression flips (and just either side of them) and long sequences of events with a fixed seed.
static class GunplayChecks
{
    internal static long inputs;      // inputs compared, for the report

    // the made-up weapons: edges of the expressions that no weapon of the catalogue sits on (a range that is shorter than a tenth of a metre, a cone of many pellets, no spread at all)
    static (NewDef, LegacyDef)[] Weapons(NewDef[] added, LegacyDef[] old)
    {
        var all = new List<(NewDef, LegacyDef)>();
        for (int i = 0; i < added.Length; i++) all.Add((added[i], old[i]));
        all.Add(Made(("id", "edge_flat"), ("hip", 0f), ("ads", 0f), ("moveSpread", 0f), ("rangeNear", 10f), ("rangeFar", 10f), ("farMult", 0.3f), ("pellets", 1), ("pierce", 0), ("bloomMax", 0f), ("bloomShot", 0f), ("bloomRecover", 0f)));
        all.Add(Made(("id", "edge_inverted_ranges"), ("rangeNear", 30f), ("rangeFar", 20f), ("farMult", 0.5f), ("pierce", 4), ("pierceMult", 0.5f), ("bloomMax", 9f), ("bloomShot", 4f), ("bloomRecover", 0.5f)));
        all.Add(Made(("id", "x"), ("hip", 9f), ("ads", 4f), ("moveSpread", 3f), ("pellets", 9), ("cone", 7.5f), ("recV", 3f), ("recH", 2f), ("recRecover", 30f), ("headMult", 3.5f), ("pierce", 1), ("pierceMult", 1f)));
        // the first pellet of a weapon of more than four is kept tight: the weapons of the catalogue have one pellet or many, so these two sit on either side of that edge
        all.Add(Made(("id", "edge_four_pellets"), ("pellets", 4), ("cone", 5f)));
        all.Add(Made(("id", "edge_five_pellets"), ("pellets", 5), ("cone", 5f)));
        return all.ToArray();
    }

    static (NewDef, LegacyDef) Made(params (string, object)[] fields)
    {
        var n = new NewDef(); var o = new LegacyDef();
        foreach (var (name, value) in fields)
        {
            typeof(NewDef).GetField(name).SetValue(n, value);
            typeof(LegacyDef).GetField(name).SetValue(o, value);
        }
        return (n, o);
    }

    static bool Eq(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    static string Bits(float v) => v.ToString("R") + $" (0x{BitConverter.SingleToInt32Bits(v):x8})";

    sealed class Tally
    {
        public long runs; public int shown; public readonly List<string> problems = new List<string>();
        public void Same(float before, float after, Func<string> where)
        {
            runs++;
            if (!Eq(before, after) && problems.Count < 4) problems.Add($"{where()}: {Bits(before)} before, {Bits(after)} after");
            else if (!Eq(before, after)) shown++;
        }
        public void Same(bool before, bool after, Func<string> where)
        {
            runs++;
            if (before != after && problems.Count < 4) problems.Add($"{where()}: {before} before, {after} after");
            else if (before != after) shown++;
        }
        public bool Ok => problems.Count == 0 && shown == 0;
    }

    static void Report(string code, string subject, Tally t, string what)
    {
        inputs += t.runs;
        Program.Check(code, subject, t.Ok, t.Ok ? $"{t.runs} inputs ({what}): the same number to the last bit" : string.Join("; ", t.problems) + (t.shown > 0 ? $" (+{t.shown} more)" : ""),
            "the same expressions in the same order give the same float as the code the game had");
    }

    static readonly float[] Speeds = { -1f, 0f, 0.3f, 1.1f, 2.8f, 4.2f, 5.59f, 5.6f, 5.61f, 8f, 11.2f, 100f };
    static readonly float[] Aims = { -0.1f, 0f, 0.05f, 0.25f, 0.5f, 0.75f, 0.8f, 0.8001f, 1f, 1.2f };
    static readonly float[] Blooms = { 0f, 0.35f, 1f, 2.2f, 3f, 7.5f };
    static readonly (float, float)[] Multipliers = { (1f, 1f), (0.7f, 1f), (1f, 1.5f), (0.7f, 1.3f), (1.3f, 0.85f) };

    public static void Run(NewDef[] added, LegacyDef[] old)
    {
        var weapons = Weapons(added, old);
        Spread(weapons); FirstShot(); Pellets(weapons); Falloff(weapons); Pierces(weapons); Bloom(weapons); Kick(weapons); Recovery(); Contracts(weapons);
    }

    static void Spread((NewDef, LegacyDef)[] weapons)
    {
        var spread = new Tally(); var stance = new Tally(); var radius = new Tally();
        foreach (var (n, o) in weapons)
            foreach (float speed in Speeds) foreach (bool grounded in new[] { true, false }) foreach (bool crouched in new[] { true, false })
            {
                var s = new AimStance(speed, grounded, crouched);
                foreach (float ads in Aims) foreach (float bloom in Blooms) foreach (var (magic, ride) in Multipliers)
                {
                    float before = Old.CurrentSpread(o, speed, grounded, crouched, ads, bloom, magic, ride, out float move, out float air, out float crouch);
                    float after = WeaponSpread.Degrees(n, s, ads, bloom, magic, ride);
                    Func<string> where = () => $"{n.id} speed {speed} grounded {grounded} crouched {crouched} ads {ads} bloom {bloom} x{magic} x{ride}";
                    spread.Same(before, after, where);
                    stance.Same(move, s.Move, where); stance.Same(air, s.Air, where); stance.Same(crouch, s.Crouch, where);
                    radius.Same(Old.ConeRadius(before), WeaponSpread.ConeRadius(after), where);
                }
            }
        for (float degrees = -5f; degrees <= 60f; degrees += 0.125f) radius.Same(Old.ConeRadius(degrees), WeaponSpread.ConeRadius(degrees), () => $"{degrees} degrees");
        Report("GUNPLAY_SPREAD_EQUALS_LEGACY", "WeaponSpread.Degrees", spread, $"{weapons.Length} weapons x {Speeds.Length} speeds x on the ground or not x crouched or not x {Aims.Length} aims x {Blooms.Length} blooms x {Multipliers.Length} pairs of multipliers");
        Report("GUNPLAY_STANCE_EQUALS_LEGACY", "AimStance", stance, "the three numbers of the stance for every one of those");
        Report("GUNPLAY_CONE_RADIUS_EQUALS_LEGACY", "WeaponSpread.ConeRadius", radius, "every spread above, and every eighth of a degree from -5 to 60");
    }

    static void FirstShot()
    {
        var t = new Tally();
        foreach (float spread in new[] { 0f, 0.35f, 1f, 2.5f, 9f }) foreach (float elapsed in new[] { -1f, 0f, 0.39f, 0.4f, 0.4001f, 0.41f, 5f, 1000f })
            foreach (float ads in new[] { 0f, 0.5f, 0.79f, 0.8f, 0.8001f, 1f }) foreach (float move in new[] { 0f, 0.09f, 0.1f, 0.1001f, 0.5f, 1f })
                t.Same(Old.FirstShot(spread, elapsed, ads, move), WeaponSpread.FirstShot(spread, elapsed, ads, move), () => $"spread {spread} elapsed {elapsed} ads {ads} move {move}");
        Report("GUNPLAY_FIRST_SHOT_EQUALS_LEGACY", "WeaponSpread.FirstShot", t, "5 spreads x 8 pauses x 6 aims x 6 speeds, the edges of all three tests and just either side of them");
    }

    static void Pellets((NewDef, LegacyDef)[] weapons)
    {
        var t = new Tally();
        foreach (var (n, o) in weapons)
            foreach (float ads in Aims) for (int p = 0; p < 12; p++)
                t.Same(Old.PelletCone(o, ads, p), WeaponBallistics.PelletCone(n, ads, p), () => $"{n.id} ads {ads} pellet {p}");
        Report("GUNPLAY_PELLET_CONE_EQUALS_LEGACY", "WeaponBallistics.PelletCone", t, $"{weapons.Length} weapons x {Aims.Length} aims x 12 pellets");
    }

    static void Falloff((NewDef, LegacyDef)[] weapons)
    {
        var fall = new Tally(); var damage = new Tally();
        foreach (var (n, o) in weapons)
        {
            var distances = new List<float> { -1f, 0f, 250f, 1e6f };
            foreach (float edge in new[] { n.rangeNear, n.rangeFar, (n.rangeNear + n.rangeFar) / 2f, n.rangeFar * 2f })
            { distances.Add(edge); distances.Add(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(edge) - 1)); distances.Add(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(edge) + 1)); }
            for (float x = 0f; x <= 300f; x += 0.25f) distances.Add(x);
            foreach (float distance in distances)
            {
                float before = Old.Falloff(o, distance), after = WeaponBallistics.Falloff(n, distance);
                fall.Same(before, after, () => $"{n.id} distance {distance}");
                foreach (bool head in new[] { false, true }) foreach (float pierce in new[] { 1f, 0.65f, 0.4225f, 0.1f }) foreach (float perk in new[] { 1f, 1.25f, 1.5f }) foreach (bool upgraded in new[] { false, true })
                    damage.Same(Old.Damage(o, before, pierce, head, perk, upgraded), WeaponBallistics.Damage(n, after, pierce, head, perk, upgraded ? Old.UpgDamage : 1f),
                        () => $"{n.id} distance {distance} head {head} pierce x{pierce} perk x{perk} upgraded {upgraded}");
            }
        }
        Report("GUNPLAY_FALLOFF_EQUALS_LEGACY", "WeaponBallistics.Falloff", fall, $"{weapons.Length} weapons x about 1,200 distances: a quarter of a metre apart to 300 m, the near and far ranges, their midpoint, twice the far range, each with the float just below and just above");
        Report("GUNPLAY_DAMAGE_EQUALS_LEGACY", "WeaponBallistics.Damage", damage, "each of those distances x head or not x 4 pierce multipliers x 3 head perks x upgraded or not");
    }

    static void Pierces((NewDef, LegacyDef)[] weapons)
    {
        var t = new Tally();
        foreach (var (n, o) in weapons)
            foreach (int extra in new[] { 0, 1, 2, 5 })
            {
                int pierced = 0; float dmgMul = 1f; var step = Pierce.Begin();
                t.Same(dmgMul, step.Multiplier, () => $"{n.id} extra {extra} at the start");
                for (int enemy = 0; enemy < 14; enemy++)
                {
                    bool before = Old.PierceStep(o, extra, ref pierced, ref dmgMul), after = step.Continue(n, extra);
                    t.Same(before, after, () => $"{n.id} extra {extra} enemy {enemy}: goes on");
                    t.Same(dmgMul, step.Multiplier, () => $"{n.id} extra {extra} enemy {enemy}: multiplier");
                    t.Same(pierced == step.Passed, true, () => $"{n.id} extra {extra} enemy {enemy}: count {pierced} before, {step.Passed} after");
                }
            }
        Report("GUNPLAY_PIERCE_EQUALS_LEGACY", "Pierce", t, $"{weapons.Length} weapons x 4 extra pierces x 14 enemies in a row: whether the bullet goes on, how many it has passed and what the next one takes");
    }

    static void Bloom((NewDef, LegacyDef)[] weapons)
    {
        var t = new Tally();
        var dts = new[] { 0f, 0.001f, 0.0083f, 0.0167f, 0.033f, 0.1f, 1f };
        foreach (var (n, o) in weapons)
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var random = new Random(seed * 7919 + n.id.Length);
                float before = 0f, after = 0f;
                for (int event_ = 0; event_ < 2000; event_++)
                {
                    int kind = random.Next(3); float ads = Aims[random.Next(Aims.Length)], dt = dts[random.Next(dts.Length)];
                    if (kind == 0) { before = Old.BloomShot(before, o); after = WeaponBloom.Grown(after, n); }
                    else { before = Old.BloomRecover(before, o, dt, ads); after = WeaponBloom.Recovered(after, n, dt, ads); }
                    t.Same(before, after, () => $"{n.id} seed {seed} event {event_}");
                }
            }
        Report("GUNPLAY_BLOOM_EQUALS_LEGACY", "WeaponBloom", t, $"{weapons.Length} weapons x 3 seeds x 2,000 events (a shot, or time passing with an aim) in a row, compared after each");
    }

    static void Kick((NewDef, LegacyDef)[] weapons)
    {
        var t = new Tally();
        foreach (var (n, o) in weapons)
            for (int streak = 0; streak <= 40; streak++) foreach (float ads in Aims) foreach (float crouch in new[] { 0f, 1f }) foreach (float mult in new[] { 1f, 0.7f, 1.3f }) foreach (float noise in new[] { -1f, -0.5f, -0.1f, 0f, 0.3f, 1f })
            {
                Old.ViewKick(o, streak, ads, crouch, mult, noise, out float rv, out float rh);
                float damping = WeaponRecoil.Damping(ads, crouch, mult);
                Func<string> where = () => $"{n.id} streak {streak} ads {ads} crouch {crouch} x{mult} noise {noise}";
                t.Same(rv, WeaponRecoil.Vertical(n, streak, damping), () => "vertical: " + where());
                t.Same(rh, WeaponRecoil.Horizontal(n, streak, noise, damping), () => "horizontal: " + where());
            }
        Report("GUNPLAY_VIEW_KICK_EQUALS_LEGACY", "WeaponRecoil", t, $"{weapons.Length} weapons x 41 shots of a burst x {Aims.Length} aims x crouching or not x 3 multipliers x 6 draws of the random part, both the vertical and the horizontal kick");
    }

    static readonly string[] Private = { "targetPitch", "targetYaw", "pitch", "yaw", "recover", "delay" };

    static void Recovery()
    {
        var t = new Tally();
        var fields = Private.Select(name => typeof(ViewRecoil).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("ViewRecoil has no field " + name)).ToArray();
        float Hidden(ViewRecoil v, int i) => (float)fields[i].GetValue(v);
        var dts = new[] { 0f, 0.001f, 0.008f, 0.0167f, 0.033f, 0.05f, 0.1f, 0.25f };
        foreach (int mode in new[] { 0, 1, 2, 3 })        // on foot, on the bike, changing between them, and big kicks on foot that reach the two limits
            foreach (int seed in new[] { 11, 12, 13, 14, 15 })
            {
                var random = new Random(seed * 104729 + mode);
                var before = new Legacy.Gun.OldPlayerRecoil(); var after = new ViewRecoil();
                for (int step = 0; step < 4000; step++)
                {
                    double r = random.NextDouble(), kicks = mode == 3 ? 0.55 : 0.14;
                    if (r < kicks)
                    {
                        float scale = mode == 3 ? 3f : 1f;
                        float v = (float)(random.NextDouble() * 6 - 2) * scale, h = (float)(random.NextDouble() * 6 - 3) * scale, recover = (float)(5 + random.NextDouble() * 12);
                        before.Kick(v, h, recover); after.Kick(v, h, recover);
                    }
                    else if (r < kicks + 0.005) { before.Clear(); after.Clear(); }
                    else
                    {
                        float dt = dts[random.Next(dts.Length)];
                        bool bike = mode == 1 || (mode == 2 && random.Next(2) == 0);
                        if (bike) before.OnBike(dt); else before.OnFoot(dt);
                        after.Advance(dt);
                    }
                    var theirs = new[] { before.recTargetP, before.recTargetY, before.recP, before.recY, before.recRecover, before.recDelay };
                    for (int i = 0; i < theirs.Length; i++) { int k = i; t.Same(theirs[i], Hidden(after, i), () => $"mode {mode} seed {seed} step {step}: {Private[k]}"); }
                    t.Same(before.recP, after.Pitch, () => $"mode {mode} seed {seed} step {step}: Pitch"); t.Same(before.recY, after.Yaw, () => $"mode {mode} seed {seed} step {step}: Yaw");
                }
            }
        Report("GUNPLAY_VIEW_RECOVERY_EQUALS_LEGACY", "ViewRecoil", t, "4 ways of using it (on foot, on the bike, both, and big kicks that reach the ceiling and the side limits) x 5 seeds x 4,000 events (a kick, a reset, or time passing by 0 to a quarter of a second), the six numbers it keeps and the two it shows compared after each");
    }

    // ---- what the numbers must mean, whatever the old code did
    static void Contracts((NewDef, LegacyDef)[] weapons)
    {
        var problems = new List<string>(); int checks = 0;
        void Expect(bool ok, string text) { checks++; if (!ok && problems.Count < 5) problems.Add(text); }
        foreach (var (n, _) in weapons)
        {
            if (n.id.StartsWith("edge") || n.id == "x") continue;
            Expect(WeaponBallistics.Falloff(n, 0f) == 1f && WeaponBallistics.Falloff(n, n.rangeNear) == 1f, $"{n.id}: all the damage up to the near range");
            Expect(Math.Abs(WeaponBallistics.Falloff(n, n.rangeFar) - n.farMult) < 1e-5f, $"{n.id}: the far multiplier at the far range");
            Expect(WeaponBallistics.Falloff(n, n.rangeFar * 3f) == n.farMult || Math.Abs(WeaponBallistics.Falloff(n, n.rangeFar * 3f) - n.farMult) < 1e-6f, $"{n.id}: no less than the far multiplier beyond it");
            Expect(WeaponBallistics.Damage(n, 1f, 1f, false, 1f, 1f) == n.damage, $"{n.id}: a plain hit at point blank does the damage of the weapon");
            Expect(WeaponBallistics.Damage(n, 1f, 1f, true, 1f, 1f) > WeaponBallistics.Damage(n, 1f, 1f, false, 1f, 1f) || n.headMult <= 1f, $"{n.id}: a head hit does more");
            var still = new AimStance(0f, true, false);
            Expect(Math.Abs(WeaponSpread.Degrees(n, still, 0f, 0f, 1f, 1f) - n.hip) < 1e-6f && Math.Abs(WeaponSpread.Degrees(n, still, 1f, 0f, 1f, 1f) - n.ads) < 1e-6f, $"{n.id}: still and clean, the hip and the aim spread of the weapon");
            Expect(WeaponSpread.Degrees(n, new AimStance(10f, true, false), 0f, 0f, 1f, 1f) > n.hip || n.moveSpread <= 0f, $"{n.id}: running widens the cone");
            Expect(WeaponSpread.Degrees(n, new AimStance(0f, true, true), 0f, 0f, 1f, 1f) < n.hip || n.hip <= 0f, $"{n.id}: crouching narrows it");
            float bloom = 0f;
            for (int i = 0; i < 200; i++) bloom = WeaponBloom.Grown(bloom, n);
            Expect(Math.Abs(bloom - n.bloomMax) < 1e-5f, $"{n.id}: the bloom stops at its largest ({bloom} of {n.bloomMax})");
            for (int i = 0; i < 20000 && bloom > 0f; i++) bloom = WeaponBloom.Recovered(bloom, n, 0.05f, 0f);
            Expect(bloom == 0f, $"{n.id}: the bloom goes away and stops at zero");
            var pierce = Pierce.Begin(); int passed = 0;
            while (pierce.Continue(n, 0) && passed < 100) passed++;
            Expect(passed == n.pierce, $"{n.id}: passes through exactly {n.pierce} enemies besides the first (passed {passed})");
        }
        var v = new ViewRecoil();
        for (int i = 0; i < 10; i++) v.Kick(5f, 0f, 8f);
        Expect(v.Pitch >= 0f, "a kick lifts the view");
        float top = 0f; for (int i = 0; i < 120; i++) { v.Advance(1f / 60f); top = Math.Max(top, v.Pitch); }
        Expect(top <= 20f + 1e-4f, $"the climb has a ceiling of 20 degrees (reached {top})");
        for (int i = 0; i < 600; i++) v.Advance(1f / 60f);
        Expect(Math.Abs(v.Pitch) < 1e-3f && Math.Abs(v.Yaw) < 1e-3f, "the view comes back to rest by itself");
        v.Kick(3f, 9f, 8f);
        for (int i = 0; i < 30; i++) v.Advance(1f / 60f);
        Expect(v.Pitch != 0f || v.Yaw != 0f, "the view is away from rest before Clear");
        v.Clear(); Expect(v.Pitch == 0f && v.Yaw == 0f, "Clear puts the view back at once");
        inputs += checks;
        Program.Check("GUNPLAY_RULES_MEAN_WHAT_THEY_SAY", "WeaponBallistics, WeaponSpread, WeaponBloom, Pierce, ViewRecoil", problems.Count == 0,
            problems.Count == 0 ? $"{checks} statements about the numbers hold for the 30 weapons (falloff ends, damage at point blank, spread when still, running and crouching, the bloom's ceiling and floor, how many enemies a bullet passes, the ceiling and the return of the view)" : string.Join("; ", problems),
            "what the documentation of each function says");
    }
}
