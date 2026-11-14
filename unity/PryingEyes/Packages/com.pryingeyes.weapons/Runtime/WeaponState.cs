using System;

namespace Production.Weapons
{
    public readonly struct ReloadTiming
    {
        public readonly bool Incremental;
        public readonly float Tactical, Empty, RoundInterval, Opening, Closing, InsertFraction;

        ReloadTiming(bool incremental, float tactical, float empty, float interval, float opening, float closing, float fraction)
        {
            WeaponState.NonNegative(tactical); WeaponState.NonNegative(empty); WeaponState.NonNegative(interval);
            WeaponState.NonNegative(opening); WeaponState.NonNegative(closing); WeaponState.NonNegative(fraction);
            if (incremental && (interval <= 0 || fraction > 1)) throw new ArgumentOutOfRangeException(nameof(interval));
            Incremental = incremental; Tactical = tactical; Empty = empty; RoundInterval = interval;
            Opening = opening; Closing = closing; InsertFraction = fraction;
        }

        public static ReloadTiming Magazine(float tactical, float empty) => new ReloadTiming(false, tactical, empty, 0, 0, 0, 0);
        public static ReloadTiming PerRound(float interval, float opening, float closing, float insertFraction) =>
            new ReloadTiming(true, 0, 0, interval, opening, closing, insertFraction);
    }

    [Flags] public enum ReloadEvents { None = 0, MagazineInserted = 1, ActionClosed = 2, Completed = 4 }
    public readonly struct ReloadStep
    {
        public readonly int RoundsInserted;
        public readonly ReloadEvents Events;
        public ReloadStep(int rounds, ReloadEvents events) { RoundsInserted = rounds; Events = events; }
    }

    /// <summary>Authoritative mechanical state. Caller supplies elapsed seconds and maps events to presentation.
    /// No clock, input, weapon IDs, upgrades, damage, audio or rendering. Ammo restoration may exceed a normal
    /// capacity (e.g. a debug scenario); reloads never create rounds or exceed their captured capacity.</summary>
    public sealed class WeaponState
    {
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public bool IsReloading { get; private set; }
        public bool ReloadStartedEmpty { get; private set; }
        public float ReloadElapsed { get; private set; }
        public float ReloadDuration { get; private set; }
        public int RoundsLoaded { get; private set; }
        public float FireDelay { get; private set; }
        public float CycleDelay { get; private set; }
        public bool Ready => FireDelay <= 0 && CycleDelay <= 0 && !IsReloading;
        public float ReloadProgress => Math.Min(1f, ReloadElapsed / Math.Max(.05f, ReloadDuration));
        ReloadTiming timing;
        int capacity, plannedRounds, stage;

        internal static void NonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        }

        public void SetAmmunition(int magazine, int reserve)
        {
            if (magazine < 0 || reserve < 0) throw new ArgumentOutOfRangeException(nameof(magazine));
            Magazine = magazine; Reserve = reserve;
        }

        // Eligibility (input, reload interruption and animation locks) is decided by the caller.
        public bool ConsumeRound()
        {
            if (Magazine == 0) return false;
            Magazine--; return true;
        }

        public bool BeginReload(int magazineCapacity, ReloadTiming profile)
        {
            if (magazineCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(magazineCapacity));
            if (IsReloading || Magazine >= magazineCapacity || Reserve == 0) return false;
            int count = Math.Min(magazineCapacity - Magazine, Reserve);
            float duration = profile.Incremental ? profile.Opening + count * profile.RoundInterval + profile.Closing :
                Magazine == 0 ? profile.Empty : profile.Tactical;
            NonNegative(duration);
            timing = profile; capacity = magazineCapacity; plannedRounds = count;
            ReloadStartedEmpty = Magazine == 0; ReloadElapsed = 0; ReloadDuration = duration;
            RoundsLoaded = stage = 0; IsReloading = true; return true;
        }

        public void CancelReload() => IsReloading = false;

        public ReloadStep AdvanceReload(float seconds)
        {
            NonNegative(seconds);
            if (!IsReloading) return default;
            NonNegative(ReloadElapsed + seconds); ReloadElapsed += seconds;
            int inserted = 0; ReloadEvents events = ReloadEvents.None;
            if (timing.Incremental)
            {
                float elapsed = ReloadElapsed - timing.Opening;
                while (RoundsLoaded < plannedRounds && Reserve > 0 && Magazine < capacity &&
                    elapsed >= RoundsLoaded * timing.RoundInterval + timing.RoundInterval * timing.InsertFraction)
                {
                    Magazine++; Reserve--; RoundsLoaded++; inserted++;
                }
            }
            else
            {
                // One presentation stage per tick, preserving event order even for coarse time steps.
                if (stage == 0 && ReloadProgress > .50f) { stage = 1; events |= ReloadEvents.MagazineInserted; }
                else if (stage == 1 && ReloadProgress > .76f) { stage = 2; events |= ReloadEvents.ActionClosed; }
            }
            if (ReloadProgress >= 1)
            {
                if (!timing.Incremental)
                {
                    inserted = Math.Min(Math.Max(0, capacity - Magazine), Reserve);
                    Magazine += inserted; Reserve -= inserted;
                }
                IsReloading = false; events |= ReloadEvents.Completed;
            }
            return new ReloadStep(inserted, events);
        }

        public void AdvanceCadence(float seconds)
        {
            NonNegative(seconds);
            // Preserve the current frame's overshoot, but never bank idle-time shot credit.
            FireDelay = Math.Max(0, FireDelay) - seconds;
            if (CycleDelay > 0) CycleDelay -= seconds;
        }
        public void RecordShot(float interval)
        {
            NonNegative(interval); float next = FireDelay + interval;
            if (float.IsInfinity(next) || float.IsNaN(next)) throw new ArgumentOutOfRangeException(nameof(interval));
            FireDelay = next;
        }
        public void DelayAfterBurst(float seconds) { NonNegative(seconds); FireDelay = Math.Max(FireDelay, seconds); }
        public void StartCycle(float seconds) { NonNegative(seconds); CycleDelay = Math.Max(CycleDelay, seconds); }
        public void ResetActions()
        {
            CancelReload(); ReloadElapsed = 0; RoundsLoaded = stage = 0; FireDelay = CycleDelay = 0;
        }
    }
}
