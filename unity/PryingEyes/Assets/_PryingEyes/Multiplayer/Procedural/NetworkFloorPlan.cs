using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using PryingEyes.Proc;

namespace PryingEyes.Multiplayer
{
    /// <summary>Uses the real generator, not a multiplayer copy. Does not build Unity objects or consume Unity random.</summary>
    public static class NetworkFloorPlan
    {
        // Reachable rooms, entrance first. Physics must still reject furniture/doors and occupied capsules.
        public static IEnumerable<(float x, float z)> SpawnCandidates(Plan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.start < 0 || plan.start >= plan.rooms.Count) throw new ArgumentException("Invalid entrance.", nameof(plan));
            var hops = plan.Hops(plan.start);
            foreach (var room in plan.rooms.Where(r => hops[r.id] >= 0).OrderBy(r => hops[r.id]).ThenBy(r => r.id))
            {
                // Integer decimetres keep the candidate order independent of floating point accumulation.
                // 1m wall margin; 1.5m between distinct reserved spawn points.
                for (int z = room.z0 + 10; z <= room.z1 - 10; z += 15)
                for (int x = room.x0 + 10; x <= room.x1 - 10; x += 15)
                    yield return (x * .1f, z * .1f);
            }
        }

        public static Plan Generate(int runSeed, int floor, bool useGrid = true)
        {
            if (floor < 1 || floor > 10000) throw new ArgumentOutOfRangeException(nameof(floor));
            // PlanGen's mode is a legacy static. Explicit network mode must not depend on a peer's CLI/single-player state.
            // Main-thread only, like the existing generator. Restore the caller's mode after generation.
            bool previous = PlanGen.UseGrid;
            try
            {
                PlanGen.UseGrid = useGrid;
                var plan = PlanGen.Generate(unchecked(runSeed + floor * 1013), floor);
                if (plan == null) throw new InvalidOperationException("Floor generation failed: " + PlanGen.LastFailure);
                return plan;
            }
            finally { PlanGen.UseGrid = previous; }
        }

        // Ordered, explicit binary schema; SHA256, never string.GetHashCode. Includes all stored Plan fields.
        // Bump the session content revision if the generator, builder, kits or this schema changes.
        public static string Digest(Plan p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            using (var bytes = new MemoryStream())
            {
                using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
                {
                    w.Write(2); w.Write(p.seed); w.Write(p.floor); w.Write(p.name); w.Write(p.epithet);
                    w.Write(p.start); w.Write(p.exit); w.Write(p.minX); w.Write(p.minZ); w.Write(p.maxX); w.Write(p.maxZ);
                    w.Write(p.startInChurch); w.Write((int)p.theme); w.Write(p.units); w.Write(p.grid); w.Write(p.rooms.Count);
                    foreach (var r in p.rooms)
                    {
                        w.Write(r.id); w.Write((int)r.kind); w.Write((int)r.type); w.Write(r.church);
                        w.Write(r.x0); w.Write(r.z0); w.Write(r.x1); w.Write(r.z1);
                        w.Write(r.wallH); w.Write(r.ridgeH); w.Write((int)r.ceil); w.Write(r.ridgeAlongX);
                        w.Write(r.seed); w.Write(r.label); w.Write(r.variant); w.Write(r.wing); w.Write(r.unit);
                    }
                    w.Write(p.openings.Count);
                    foreach (var o in p.openings)
                    {
                        w.Write(o.a); w.Write(o.b); w.Write(o.alongX); w.Write(o.line); w.Write(o.center);
                        w.Write(o.width); w.Write((int)o.style); w.Write(o.sill); w.Write(o.top);
                        w.Write(o.oneSided); w.Write(o.variant);
                    }
                    w.Write(p.objectives.Count);
                    foreach (int id in p.objectives) w.Write(id);
                }
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
