using System;
using PryingEyes;

// The perks of one player (Scripts/PlayerPerks.cs, priced by Scripts/Punti.cs): every local actor owns its own, none is shared.
static partial class Program
{
    static void PlayerPerksChecks()
    {
        var one = new PlayerPerks(); var two = new PlayerPerks();
        bool healed = false; one.Changed += heal => healed = heal;
        Check(one.NextPrice == 2000 && one.MaxHp == 100, "New actor starts with no perks");
        Check(one.Add(PerkId.PelleDiPietra) && healed && one.MaxHp == 150, "Health perk notifies its own actor");
        Check(two.Count == 0 && two.MaxHp == 100, "Other local actor does not inherit perks");
        Check(!one.Add(PerkId.PelleDiPietra) && !one.Add((PerkId)(-1)) && !one.Add((PerkId)8), "Reject duplicates and unknown perks");
        Check(one.Add(PerkId.Rinascita) && one.TakeRevive() && !one.TakeRevive(), "One revive per actor per floor");
        Check(two.Add(PerkId.Rinascita) && two.TakeRevive(), "Revive usage is independent per actor");
        Check(one.Add(PerkId.Velo) && one.TryVelo() && !one.TryVelo(), "Cloak activation respects its cooldown");
        Check(one.Add(PerkId.Occhio) && one.Full && one.NextPrice == -1 && !one.Add(PerkId.ManiSvelte), "Four ordered perk slots");
        one.OnPing(); one.Tick(2);
        Check(one.VeloLeft == 3 && one.EyeLeft == 4 && one.VeloCooldownFrac == 1, "Per-actor ability timers");
        var snapshot = one.Capture(); var copy = new PlayerPerks(); copy.Restore(snapshot);
        Check(copy.Count == 4 && copy.At(0) == PerkId.PelleDiPietra && copy.At(3) == PerkId.Occhio &&
            copy.VeloLeft == one.VeloLeft && copy.EyeLeft == one.EyeLeft && !copy.ReviveReady, "Snapshot preserves order and active ability state");
        copy.NewFloor();
        Check(copy.ReviveReady && !copy.VeloOn && !copy.EyeOn && !copy.TryVelo(), "Floor resets active effects and revive without erasing cooldown");
        copy.Tick(43); Check(copy.TryVelo(), "Cloak becomes available after remaining cooldown");
        Check(copy.LoseLast(out var last) && last == PerkId.Occhio && copy.NextPrice == 4000, "Death removes the last purchased perk");
        Check(copy.LoseLast(out last) && last == PerkId.Velo && !copy.VeloOn && copy.VeloCooldownFrac == 0, "Losing cloak clears its state");
        foreach (var invalid in new[] {
            new PerkSnapshot { Inventory = 0x11 }, new PerkSnapshot { Inventory = 0x101 },
            new PerkSnapshot { Inventory = 9 }, new PerkSnapshot { Inventory = 0x10000 },
            new PerkSnapshot { VeloLeft = float.NaN }, new PerkSnapshot { VeloCooldownLeft = 46 },
            new PerkSnapshot { EyeLeft = -1 }, new PerkSnapshot { VeloLeft = float.PositiveInfinity } })
        {
            bool rejected = false;
            try { one.Restore(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected && one.Capture().Inventory == snapshot.Inventory && one.VeloLeft == 3,
                "Malformed snapshot cannot partially change live actor state");
        }
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false; try { one.Tick(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && one.VeloLeft == 3, "Reject invalid simulation time without changing actor state");
        }
        one.ResetRun(); Check(one.Count == 0 && !one.VeloOn && !one.EyeOn && one.NextPrice == 2000, "Reset affects only its actor");
        Check(two.Count == 1 && !two.ReviveReady, "Other actor survives reset unchanged");
    }
}
