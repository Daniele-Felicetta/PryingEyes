using System;
using System.Collections.Generic;

namespace PryingEyes
{
    public enum PerkId { PelleDiPietra, ManiSvelte, DoppioColpo, Rinascita, PassiDiCenere, Velo, Occhio, SeteDiSangue }

    public struct PerkSnapshot
    {
        // Four ordered nibbles: zero is empty, otherwise PerkId + 1. Order determines the death penalty.
        public uint Inventory;
        public bool ReviveUsed;
        public float VeloLeft, VeloCooldownLeft, EyeLeft;
    }

    /// <summary>Per-player gameplay state, shared by single-player and network adapters; no Unity or transport dependency.</summary>
    public sealed class PlayerPerks
    {
        public const int Slots = 4;
        public static readonly int[] Prices = { 2000, 2500, 3000, 4000 };
        public const float VeloTime = 5, VeloCooldown = 40, EyeTime = 6;
        readonly List<PerkId> owned = new List<PerkId>();
        bool reviveUsed;
        float veloLeft, veloCd, eyeLeft;
        public event Action<bool> Changed;
        public int Count => owned.Count;
        public bool Full => Count >= Slots;
        public int NextPrice => Full ? -1 : Prices[Count];
        public PerkId At(int index) => owned[index];
        public bool Has(PerkId id) => owned.Contains(id);
        public bool Add(PerkId id)
        {
            if ((uint)id > (uint)PerkId.SeteDiSangue || Full || Has(id)) return false;
            owned.Add(id); Changed?.Invoke(id == PerkId.PelleDiPietra); return true;
        }
        public bool LoseLast(out PerkId lost)
        {
            lost = PerkId.PelleDiPietra;
            if (Count == 0) return false;
            lost = owned[Count - 1]; owned.RemoveAt(Count - 1);
            if (lost == PerkId.Velo) veloLeft = veloCd = 0;
            Changed?.Invoke(false); return true;
        }
        public void ResetRun()
        {
            owned.Clear(); reviveUsed = false; veloLeft = veloCd = eyeLeft = 0; Changed?.Invoke(false);
        }
        public void NewFloor() { reviveUsed = false; veloLeft = eyeLeft = 0; }
        public float MaxHpMult => Has(PerkId.PelleDiPietra) ? 1.5f : 1;
        public float MaxHp => 100 * MaxHpMult;
        public float ReloadTime => Has(PerkId.ManiSvelte) ? .6f : 1;
        public float RateMult => Has(PerkId.DoppioColpo) ? 1.33f : 1;
        public float StepNoise => Has(PerkId.PassiDiCenere) ? .6f : 1;
        public float SightMult => Has(PerkId.Velo) ? .7f : 1;
        public float HeadMult => Has(PerkId.Occhio) ? 1.25f : 1;
        public float BloodHeal(DamageSource source) => !Has(PerkId.SeteDiSangue) ? 0
            : source == DamageSource.Takedown ? 40 : source == DamageSource.Knife ? 25 : 0;
        public bool ReviveReady => Has(PerkId.Rinascita) && !reviveUsed;
        public bool TakeRevive() { if (!ReviveReady) return false; reviveUsed = true; return true; }
        public bool VeloOn => veloLeft > 0;
        public float VeloLeft => veloLeft;
        public float VeloCooldownFrac => veloLeft > 0 ? 1 : Math.Min(1, Math.Max(0, veloCd / VeloCooldown));
        public bool TryVelo()
        {
            if (!Has(PerkId.Velo) || veloLeft > 0 || veloCd > 0) return false;
            veloLeft = VeloTime; veloCd = VeloCooldown + VeloTime; return true;
        }
        public bool EyeOn => eyeLeft > 0;
        public float EyeLeft => eyeLeft;
        public void OnPing() { if (Has(PerkId.Occhio)) eyeLeft = EyeTime; }
        public void Tick(float dt)
        {
            if (!Finite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            veloLeft = Math.Max(0, veloLeft - dt); veloCd = Math.Max(0, veloCd - dt); eyeLeft = Math.Max(0, eyeLeft - dt);
        }
        public PerkSnapshot Capture()
        {
            uint inventory = 0;
            for (int i = 0; i < Count; i++) inventory |= ((uint)owned[i] + 1) << (i * 4);
            return new PerkSnapshot { Inventory = inventory, ReviveUsed = reviveUsed,
                VeloLeft = veloLeft, VeloCooldownLeft = veloCd, EyeLeft = eyeLeft };
        }
        public void Restore(PerkSnapshot snapshot)
        {
            if ((snapshot.Inventory >> 16) != 0 || !InRange(snapshot.VeloLeft, VeloTime) ||
                !InRange(snapshot.VeloCooldownLeft, VeloCooldown + VeloTime) || !InRange(snapshot.EyeLeft, EyeTime))
                throw new ArgumentException("Invalid perk snapshot.", nameof(snapshot));
            var incoming = new List<PerkId>(); bool empty = false;
            for (int i = 0; i < Slots; i++)
            {
                int value = (int)((snapshot.Inventory >> (i * 4)) & 15);
                if (value == 0) { empty = true; continue; }
                var id = (PerkId)(value - 1);
                if (empty || value > 8 || incoming.Contains(id)) throw new ArgumentException("Invalid perk inventory.", nameof(snapshot));
                incoming.Add(id);
            }
            // Validate everything before changing live state.
            owned.Clear(); owned.AddRange(incoming); reviveUsed = snapshot.ReviveUsed;
            veloLeft = snapshot.VeloLeft; veloCd = snapshot.VeloCooldownLeft; eyeLeft = snapshot.EyeLeft;
            Changed?.Invoke(false);
        }
        static bool InRange(float value, float max) => Finite(value) && value >= 0 && value <= max;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
