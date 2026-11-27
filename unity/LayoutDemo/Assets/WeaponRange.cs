using System;
using System.Collections.Generic;
using System.Globalization;
using Production.Weapons;

// Independent caller of the weapons package, for a shooting range: weapons that belong to no game are fired at rows of targets and what each shot did is written down.
// The package gives the numbers (the cone a shot falls in, the damage at a distance and through targets, the kick of the view, the rounds and the cadence); this file
// does what a game does around them: it draws the random numbers, turns a cone into a place on a target and counts the hits. Nothing here uses the engine, so the
// same code runs in Unity (WeaponDemo.Verify) and on .NET.
public sealed class RangeScenario
{
    public readonly string Name;
    public readonly WeaponDef Weapon;
    public readonly bool Aim;                // down the sights
    public readonly int Shots;
    public readonly float[] Distances;       // the row of targets, nearest first, on the line of aim (metres)
    public readonly float Radius;            // of each target
    public readonly bool Head;               // the targets are heads: a hit takes the weapon's head multiplier

    public RangeScenario(string name, WeaponDef weapon, bool aim, int shots, float[] distances, float radius, bool head)
    {
        Name = name; Weapon = weapon; Aim = aim; Shots = shots; Distances = distances; Radius = radius; Head = head;
    }
}

public sealed class RangeReport
{
    public readonly RangeScenario Scenario;
    public readonly List<string> Lines = new List<string>();
    public readonly int[] Hits;              // hits on each target (a pellet that reaches a target counts once)
    public readonly float[] Least, Most;     // the smallest and the largest damage a hit on each target did
    public int Shots, Reloads, Pellets, PelletsOnTarget, Magazine, Reserve;
    public float PeakBloom, PeakPitch, FinalBloom, FinalPitch;

    public RangeReport(RangeScenario scenario)
    {
        Scenario = scenario;
        int n = scenario.Distances.Length;
        Hits = new int[n]; Least = new float[n]; Most = new float[n];
        for (int i = 0; i < n; i++) Least[i] = float.MaxValue;
    }

    public void Hit(int target, float damage)
    {
        Hits[target]++;
        if (damage < Least[target]) Least[target] = damage;
        if (damage > Most[target]) Most[target] = damage;
    }
}

public static class WeaponRange
{
    public const int Seed = 1;
    const float Step = 1f / 60f;             // the range waits for no frame: the same seed gives the same shots whatever the machine is doing

    /// <summary>Four weapons of a catalogue that belongs to no game, one for each thing the package does to a shot: a rifle (cadence, falloff, bloom, the kick of the view),
    /// a shotgun (pellets), a bolt action that goes through targets and a machine gun whose view climbs to its ceiling. The rules of a catalogue accept them. The bloom of the rifle
    /// and of the machine gun goes away slowly (1 degree a second, twice that when aiming), so that at their rates of fire each shot leaves more than the time between two shots
    /// takes away and the bloom builds up to its ceiling.</summary>
    public static List<WeaponDef> Catalogue() => new List<WeaponDef>
    {
        new WeaponDef
        {
            id = "range_rifle", name = "Range rifle", cls = "rifle", damage = 30f, headMult = 2f, rangeNear = 25f, rangeFar = 80f, farMult = 0.5f,
            rpm = 600f, mag = 30, reserve = 120, reserveMax = 240, reload = 1.8f, reloadEmpty = 2.2f,
            hip = 2f, ads = 0.6f, bloomShot = 0.35f, bloomMax = 3f, bloomRecover = 1f, recV = 1f, recH = 0.3f, recRecover = 8f,
        },
        new WeaponDef
        {
            id = "range_shotgun", name = "Range shotgun", cls = "shotgun", modes = new[] { FireMode.Pump }, damage = 12f, headMult = 1.5f, rangeNear = 10f, rangeFar = 30f, farMult = 0.25f,
            pellets = 8, cone = 4f, rpm = 70f, cycle = 0.85f, mag = 6, reserve = 30, reserveMax = 60, reload = 2.4f, reloadEmpty = 3f,
            hip = 3f, ads = 1.5f, bloomShot = 1f, bloomMax = 4f, bloomRecover = 5f, recV = 4f, recH = 1.2f, recRecover = 6f,
        },
        new WeaponDef
        {
            id = "range_bolt", name = "Range bolt action", cls = "rifle", modes = new[] { FireMode.Bolt }, damage = 90f, headMult = 2.5f, rangeNear = 50f, rangeFar = 120f, farMult = 0.6f,
            pierce = 2, pierceMult = 0.65f, rpm = 50f, cycle = 1.2f, mag = 5, reserve = 25, reserveMax = 50, reload = 2.8f, reloadEmpty = 3.2f,
            hip = 4f, ads = 0.15f, bloomShot = 1.2f, bloomMax = 3f, bloomRecover = 4f, recV = 3.5f, recH = 0.6f, recRecover = 5f,
        },
        new WeaponDef
        {
            id = "range_lmg", name = "Range machine gun", cls = "lmg", damage = 20f, headMult = 1.5f, rangeNear = 30f, rangeFar = 90f, farMult = 0.5f,
            rpm = 900f, mag = 100, reserve = 200, reserveMax = 300, reload = 4f, reloadEmpty = 5f,
            hip = 3f, ads = 1.2f, bloomShot = 0.2f, bloomMax = 4f, bloomRecover = 1f, recV = 2f, recH = 0.5f, recRecover = 4f,
        },
    };

    public static List<RangeScenario> Scenarios()
    {
        var c = Catalogue();
        return new List<RangeScenario>
        {
            new RangeScenario("Rifle, 40 shots down the sights at a target inside the full-damage range (one reload on the way)", c[0], true, 40, new[] { 20f }, 0.45f, false),
            new RangeScenario("Rifle, 20 shots at a board at the far range", c[0], true, 20, new[] { 80f }, 1.2f, false),
            new RangeScenario("Rifle, 20 shots at a head", c[0], true, 20, new[] { 20f }, 0.25f, true),
            new RangeScenario("Shotgun, 8 shots from the hip at a target", c[1], false, 8, new[] { 8f }, 0.45f, false),
            new RangeScenario("Bolt action, 5 shots at a row of four targets", c[2], true, 5, new[] { 15f, 17f, 19f, 21f }, 0.45f, false),
            new RangeScenario("Machine gun, 30 shots from the hip, the view climbing to its ceiling", c[3], false, 30, new[] { 15f }, 0.9f, false),
        };
    }

    static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

    /// <summary>A point of the disc of a given radius, uniformly: where in a cone a bullet goes (the cone is the package's, the draw is the caller's).</summary>
    static void Disc(Random rnd, float radius, out float x, out float y)
    {
        double r = Math.Sqrt(rnd.NextDouble()) * radius, a = rnd.NextDouble() * 2.0 * Math.PI;
        x = (float)(r * Math.Cos(a)); y = (float)(r * Math.Sin(a));
    }

    /// <summary>One weapon, one row of targets: the shots a person standing still would fire, one after the other, with the time they take.</summary>
    public static RangeReport Fire(RangeScenario s, int seed)
    {
        var d = s.Weapon;
        var rnd = new Random(seed);
        var rep = new RangeReport(s);
        var state = new WeaponState();
        state.SetAmmunition(d.mag, d.reserve);
        var view = new ViewRecoil();
        var timing = ReloadTiming.Magazine(d.reload, d.reloadEmpty);
        var stance = new AimStance(0f, true, false);       // standing still on the ground
        float ads = s.Aim ? 1f : 0f, bloom = 0f, sinceShot = 10f, time = 0f;
        int streak = 0;

        void Tick()
        {
            state.AdvanceReload(Step);
            state.AdvanceCadence(Step);
            view.Advance(Step);
            bloom = WeaponBloom.Recovered(bloom, d, Step, ads);
            sinceShot += Step; time += Step;
            if (view.Pitch > rep.PeakPitch) rep.PeakPitch = view.Pitch;
        }

        for (int shot = 1; shot <= s.Shots; shot++)
        {
            if (state.Magazine == 0 && state.BeginReload(d.mag, timing))
            {
                rep.Reloads++;
                rep.Lines.Add(F("{0,6:0.00} s  reload: the magazine is empty, {1} rounds in reserve", time, state.Reserve));
            }
            while (state.IsReloading || !state.Ready) Tick();
            if (!state.ConsumeRound()) break;                // nothing left to fire
            rep.Shots++;
            streak = sinceShot > 0.5f ? 0 : streak + 1;      // what counts as one burst is the caller's to say

            float cone = WeaponSpread.FirstShot(WeaponSpread.Degrees(d, stance, ads, bloom, 1f, 1f), sinceShot, ads, stance.Move);
            Disc(rnd, WeaponSpread.ConeRadius(cone), out float cx, out float cy);
            int onTarget = 0;
            float total = 0f, firstOffset = 0f;
            var damages = new List<string>();
            for (int p = 0; p < d.pellets; p++)
            {
                float px = cx, py = cy;
                if (d.pellets > 1)
                {
                    Disc(rnd, WeaponSpread.ConeRadius(WeaponBallistics.PelletCone(d, ads, p)), out float ox, out float oy);
                    px += ox; py += oy;
                }
                rep.Pellets++;
                var pierce = Pierce.Begin();
                for (int t = 0; t < s.Distances.Length; t++)
                {
                    float distance = s.Distances[t];
                    float offset = distance * (float)Math.Sqrt(px * px + py * py);      // how far from the line of aim the bullet is where the target stands
                    if (t == 0) firstOffset = offset;
                    if (offset > s.Radius) break;                                       // it passes by this target, and so by the ones behind it
                    float damage = WeaponBallistics.Damage(d, WeaponBallistics.Falloff(d, distance), pierce.Multiplier, s.Head, 1f, 1f);
                    rep.Hit(t, damage);
                    total += damage;
                    if (t == 0) { onTarget++; rep.PelletsOnTarget++; }
                    damages.Add(F("{0:0.00}", damage));
                    if (!pierce.Continue(d, 0)) break;
                }
            }
            state.RecordShot(60f / d.rpm);
            if (d.cycle > 0f) state.StartCycle(d.cycle);
            float bloomSeen = bloom;
            bloom = WeaponBloom.Grown(bloom, d);
            if (bloom > rep.PeakBloom) rep.PeakBloom = bloom;
            float damping = WeaponRecoil.Damping(ads, stance.Crouch, 1f);
            float up = WeaponRecoil.Vertical(d, streak, damping);
            float aside = WeaponRecoil.Horizontal(d, streak, (float)(rnd.NextDouble() * 2.0 - 1.0), damping);
            view.Kick(up, aside, d.recRecover);

            string what = d.pellets > 1 ? F("{0} of {1} pellets on the target for {2:0.00}", onTarget, d.pellets, total)
                : damages.Count == 0 ? F("missed by {0:0.00} m", firstOffset - s.Radius)
                : damages.Count == 1 ? F("hit the target at {0:0} m for {1}", s.Distances[0], damages[0])
                : F("went through {0} targets for {1}", damages.Count, string.Join(", ", damages));
            rep.Lines.Add(F("{0,6:0.00} s  shot {1,2}: cone {2:0.00} deg, bloom {3:0.00}; {4}; kick {5:0.00} up and {6:+0.00;-0.00} aside; magazine {7}",
                time, shot, cone, bloomSeen, what, up, aside, state.Magazine));
            sinceShot = 0f;
        }
        for (int i = 0; i < 180; i++) Tick();                // three seconds with the weapon lowered: the bloom goes and the view comes back
        rep.FinalBloom = bloom; rep.FinalPitch = view.Pitch;
        rep.Magazine = state.Magazine; rep.Reserve = state.Reserve;
        rep.Lines.Add(F("after three seconds: bloom {0:0.00}, view {1:0.00} deg, {2} rounds in the magazine and {3} in reserve", bloom, view.Pitch, state.Magazine, state.Reserve));
        return rep;
    }

    static void Require(bool ok, string what) { if (!ok) throw new Exception("WEAPON RANGE: " + what); }
    static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

    /// <summary>For a batch run: fires every scenario and checks what the package promises of a shot; returns the line to print, and throws when something is not as it says.</summary>
    public static string SelfCheck()
    {
        var catalogue = Catalogue();
        var indices = new List<int>();
        for (int i = 0; i < catalogue.Count; i++) indices.Add(i);
        Require(WeaponCatalogRules.Check(indices, catalogue) == null, "the rules of a catalogue refuse the weapons of the range");
        var broken = Catalogue();
        broken[1].damage = 0f;
        Require(WeaponCatalogRules.Check(indices, broken) == "WEAPON_STATS_INVALID: range_shotgun", "the rules of a catalogue accept a shotgun that does no damage");

        var scenarios = Scenarios();
        var reports = new List<RangeReport>();
        foreach (var s in scenarios) reports.Add(Fire(s, Seed));
        for (int i = 0; i < scenarios.Count; i++)
            Require(string.Join("\n", Fire(scenarios[i], Seed).Lines) == string.Join("\n", reports[i].Lines), "the same seed gave two different sets of shots: " + scenarios[i].Name);
        Require(string.Join("\n", Fire(scenarios[0], Seed + 1).Lines) != string.Join("\n", reports[0].Lines), "another seed gave the same shots");

        RangeReport near = reports[0], far = reports[1], head = reports[2], shotgun = reports[3], bolt = reports[4], lmg = reports[5];
        WeaponDef rifle = catalogue[0], pump = catalogue[1], rod = catalogue[2], belt = catalogue[3];
        Require(near.Hits[0] > 0 && Near(near.Least[0], rifle.damage) && Near(near.Most[0], rifle.damage), "a hit inside the near range is not the damage of the weapon");
        Require(far.Hits[0] > 0 && Near(far.Least[0], rifle.damage * rifle.farMult) && Near(far.Most[0], far.Least[0]), "a hit at the far range is not the far multiplier of the damage");
        Require(head.Hits[0] > 0 && Near(head.Least[0], rifle.damage * rifle.headMult) && Near(head.Most[0], head.Least[0]), "a hit on a head is not the head multiplier of the damage");
        Require(shotgun.Pellets == pump.pellets * shotgun.Shots && shotgun.PelletsOnTarget > 0 && shotgun.PelletsOnTarget <= shotgun.Pellets &&
            Near(shotgun.Least[0], pump.damage) && Near(shotgun.Most[0], pump.damage), "the pellets of the shotgun are not what the weapon says");
        Require(bolt.Shots == 5 && bolt.Hits[0] == bolt.Shots && bolt.Hits[1] == bolt.Shots && bolt.Hits[2] == bolt.Shots && bolt.Hits[3] == 0,
            "the bolt action does not go through exactly the targets it should (itself and two more)");
        Require(Near(bolt.Most[0], rod.damage) && Near(bolt.Most[1], rod.damage * rod.pierceMult) && Near(bolt.Most[2], rod.damage * rod.pierceMult * rod.pierceMult),
            "each target that the bolt goes through does not take the pierce multiplier less");
        Require(Near(near.PeakBloom, rifle.bloomMax) && Near(near.FinalBloom, 0f), "the bloom of the rifle has no ceiling, or does not go away");
        Require(near.PeakPitch > 0f && Math.Abs(near.FinalPitch) < 0.1f, "the view of the rifle does not kick, or does not come back");
        Require(lmg.Hits[0] > 0 && Near(lmg.Least[0], belt.damage) && Near(lmg.Most[0], belt.damage), "a hit of the machine gun is not the damage of the weapon");
        Require(lmg.PeakPitch > 19.9f && lmg.PeakPitch <= 20.01f && Math.Abs(lmg.FinalPitch) < 0.1f, "the view of the machine gun does not climb to its ceiling of 20 degrees, or goes past it, or does not come back");
        Require(near.Reloads == 1 && near.Shots + near.Magazine + near.Reserve == rifle.mag + rifle.reserve, "rounds were made or lost between the shots and the reload");
        Require(bolt.Reloads == 0 && bolt.Shots + bolt.Magazine + bolt.Reserve == rod.mag + rod.reserve, "the bolt action made or lost rounds");
        Require(lmg.Reloads == 0 && lmg.Shots + lmg.Magazine + lmg.Reserve == belt.mag + belt.reserve, "the machine gun made or lost rounds");

        return F("rifle: {0} shots, {1} hits of {2:0.00}, one reload, bloom up to {3:0.00} after a shot and back to 0, view up to {4:0.0} deg and back | far range: {5} hits of {6:0.00} | head: {7} hits of {8:0.00} | " +
            "shotgun: {9} of {10} pellets on the target, {11:0.00} each | bolt action through {12} targets: {13:0.00}, {14:0.00}, {15:0.00}, a fourth is never reached | " +
            "machine gun: view up to {16:0.0} deg, its ceiling, and back | the same seed, the same shots; the rules of a catalogue refuse a weapon that does no damage",
            near.Shots, near.Hits[0], near.Most[0], near.PeakBloom, near.PeakPitch, far.Hits[0], far.Most[0], head.Hits[0], head.Most[0],
            shotgun.PelletsOnTarget, shotgun.Pellets, shotgun.Most[0], rod.pierce + 1, bolt.Most[0], bolt.Most[1], bolt.Most[2], lmg.PeakPitch);
    }
}
