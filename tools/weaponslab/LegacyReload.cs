using System;

// Frozen reference of Weapon.Reload/TickReload in commit 6fcbecf. Verification only,
// never compiled into the game or reusable package. Updates require an intentional behavior review.
sealed class LegacyReload
{
    public int mag, reserve, loaded, stage, planned;
    public float elapsed, duration, shell;
    public bool active, empty, tube;
    readonly int capacity;
    public LegacyReload(int mag, int reserve, int capacity, bool tube, float normal, float empty, float shell)
    {
        this.mag = mag; this.reserve = reserve; this.capacity = capacity; this.tube = tube; this.shell = shell;
        if (mag >= capacity || reserve <= 0) return;
        active = true; this.empty = mag <= 0;
        if (tube) { planned = Math.Min(capacity - mag, reserve); duration = .35f + planned * shell + .25f; }
        else duration = this.empty ? empty : normal;
    }
    public (int rounds, int events) Tick(float dt)
    {
        if (!active) return (0, 0);
        elapsed += dt;
        float progress = Math.Min(1f, elapsed / Math.Max(.05f, duration));
        int inserted = 0, events = 0;
        if (tube)
        {
            float t = elapsed - .35f;
            while (loaded < planned && t >= loaded * shell + shell * .55f)
            { mag++; reserve--; loaded++; inserted++; }
            if (progress >= 1) { active = false; events = 4; }
            return (inserted, events);
        }
        if (stage == 0 && progress > .50f) { stage = 1; events = 1; }
        else if (stage == 1 && progress > .76f) { stage = 2; events = 2; }
        if (progress >= 1)
        {
            inserted = Math.Min(capacity - mag, reserve); mag += inserted; reserve -= inserted;
            active = false; events |= 4;
        }
        return (inserted, events);
    }
}
