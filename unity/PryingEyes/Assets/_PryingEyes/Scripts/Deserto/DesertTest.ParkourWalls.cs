#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Proc;
using PryingEyes.Testing;
using UnityEngine;

namespace PryingEyes
{
    public partial class PlayerController
    {
        internal void BeginWallAudit(Vector3 feet)
        {
            cc.height = heightStand;
            cc.center = Vector3.up * (heightStand * 0.5f);
            Teleport(feet);
            jumpBuf = 0.15f;
        }

        internal bool WallAuditClear(Vector3 feet) => StandFree(feet, heightStand, cc.radius - cc.skinWidth);

        internal void StepWallAuditMantle(float dt) => MantleTick(dt);

        internal int WallAuditMotion => transferTime > 0f ? -1 : (int)climbMove;
        internal static string WallAuditMotionName(int motion) => motion == -1 ? "Transfer" : ((ClimbMove)motion).ToString();

        internal bool StepWallAudit(float dt, Vector3 facing, float sideways, bool jump)
        {
            Vector3 wish = facing + Vector3.Cross(Vector3.up, facing) * sideways;
            if (jump && OnClimbWall) jumpBuf = 0.26f;
            if (ParkourStep(dt, true, false, 1f, sideways, wish, facing)) return true;
            if (transferTime <= 0f) return false;
            vel.y -= gravity * dt;
            cc.Move(vel * dt);
            return true;
        }
    }
}

namespace PryingEyes.Deserto
{
    public sealed partial class DesertTest
    {
        const float AuditLift = 220f;
        static readonly Vector3[] AuditOutward = { Vector3.back, Vector3.forward, Vector3.left, Vector3.right };
        static readonly float[] AuditFootHeights = { 0.08f, 0.70f, 1.45f, 2.20f, 3.00f, 3.80f };
        static readonly float[] AuditClimbDirections = { 0f, -1f, 1f };

        IEnumerator ParkourWalls()
        {
            int radius = 3;
            string cell = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-wallradius" && int.TryParse(args[i + 1], out int value)) radius = Mathf.Clamp(value, 1, 12);
                if (args[i] == "-wallcell") cell = args[i + 1];
            }
            var sites = new List<Site>();
            var seen = new HashSet<Site>();
            void Add(Site site)
            {
                if (site != null && seen.Add(site) && (cell == null || cell == $"{site.cx},{site.cz}")) sites.Add(site);
            }
            Add(DesertWorld.Welcome());
            Add(DesertWorld.Landmark());
            for (int x = -radius; x <= radius; x++)
                for (int z = -radius; z <= radius; z++)
                    Add(DesertWorld.SiteOf(x, z));
            if (cell != null)
            {
                var parts = cell.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int z))
                    Add(DesertWorld.SiteOf(x, z));
            }

            var pl = Pl;
            var controller = pl.GetComponent<CharacterController>();
            Vector3 saved = pl.transform.position;
            var report = Path.Combine(TestRun.OutDir("deserto"), $"parkour-walls-{DesertWorld.Seed}{(sites.Count == 1 ? $"-{sites[0].cx}_{sites[0].cz}" : "")}.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            using var csv = new StreamWriter(report);
            csv.WriteLine("seed,kind,state,cell_x,cell_z,level,room,side,action,transition,initial_overlap,start_x,start_y,start_z,previous_x,previous_y,previous_z,hit_x,hit_y,hit_z,end_x,end_y,end_z");
            int attempts = 0, leaks = 0, overlaps = 0, affected = 0;
            phaseName = ""; // The audit deliberately runs many controller steps between rendered frames.
            try
            {
                AuditWallControl(pl, controller);
                foreach (var site in sites)
                {
                    int beforeSite = leaks, beforeOverlaps = overlaps, startLevel = site.IsCatacomb ? site.entrance : 0;
                    int endLevel = site.IsTower ? site.LevelCount : startLevel + 1;
                    for (int level = startLevel; level < endLevel; level++)
                    {
                        var holder = new GameObject("Parkour wall audit shell");
                        try
                        {
                            var floor = SiteRt.BuildShellOf(site, level, holder.transform);
                            float y = (site.IsStack ? site.LevelY(level) : site.padY) + AuditLift;
                            floor.root.transform.position = new Vector3(0f, y, 0f);
                            Physics.SyncTransforms();
                            var geometry = new DesertCensus();
                            foreach (var filter in floor.root.GetComponentsInChildren<MeshFilter>())
                                if (filter.name == "Guscio") geometry.Add(filter.gameObject);
                            var plan = site.IsStack ? site.levels[level] : site.plan;
                            foreach (var room in plan.rooms)
                                for (int side = 0; side < AuditOutward.Length; side++)
                                {
                                    var outward = AuditOutward[side];
                                    var face = (Side)side;
                                    bool alongX = Plan.AlongX(face);
                                    float line = Plan.Line(room, face) * 0.1f;
                                    foreach (var span in plan.Segments(room, face))
                                    {
                                        if (span.other >= 0) continue;
                                        int samples = Mathf.Max(1, Mathf.CeilToInt((span.a1 - span.a0) * 0.1f / 0.75f));
                                        for (int k = 0; k < samples; k++)
                                        {
                                            float along = Mathf.Lerp(span.a0, span.a1, (k + 0.5f) / samples) * 0.1f;
                                            var basePoint = alongX ? new Vector3(along, y, line) : new Vector3(line, y, along);
                                            foreach (float height in AuditFootHeights)
                                            {
                                                if (height > Mathf.Max(room.wallH, room.ridgeH) - 0.15f) break;
                                                Vector3 from = basePoint + outward * 0.90f + Vector3.up * height;
                                                ProbeWall(site, level, room, face, geometry, from, outward);
                                            }
                                        }
                                    }
                                }
                        }
                        finally
                        {
                            pl.BeginWallAudit(saved);
                            Scrap(holder);
                        }
                        yield return null;
                    }
                    if (leaks > beforeSite) affected++;
                    if (leaks > beforeSite || overlaps > beforeOverlaps)
                        Debug.Log($"[PryingEyes] parkourwalls seed {DesertWorld.Seed} {site}: {leaks - beforeSite} clear-start crossings, {overlaps - beforeOverlaps} starting overlapped");
                }
            }
            finally { pl.BeginWallAudit(saved); }
            Check("parkourwalls: generated sites and exterior probes were exercised", sites.Count > 0 && attempts > 0,
                $"{sites.Count} sites, {attempts} attempts");
            Check("parkourwalls: solid exterior faces stop the controller", leaks == 0,
                $"{leaks} clear-start crossings, {affected}/{sites.Count} sites, {overlaps} starting overlapped, {attempts} attempts, {report}");

            void ProbeWall(Site site, int level, Room room, Side face, DesertCensus geometry, Vector3 from, Vector3 outward)
            {
                pl.BeginWallAudit(from);
                bool overlap = !pl.WallAuditClear(from);
                controller.Move(-outward * 1.50f);
                attempts++;
                if (CrossedWall(geometry, from, pl.transform.position, out var contact))
                    Record(site, level, room, face, "push", "", overlap, from, from, contact, pl.transform.position);
                if (overlap)
                {
                    Vector3 above = from + Vector3.up * 2.4f;
                    pl.BeginWallAudit(above);
                    if (!pl.WallAuditClear(above)) return;
                    attempts++;
                    for (int frame = 0; frame < 12 && !controller.isGrounded; frame++)
                    {
                        Vector3 previous = pl.transform.position;
                        controller.Move(Vector3.down * 0.25f);
                        if (!CrossedWall(geometry, previous, pl.transform.position, out contact)) continue;
                        Record(site, level, room, face, "drop", "", false, above, previous, contact, pl.transform.position);
                        break;
                    }
                    return;
                }

                pl.BeginWallAudit(from);
                attempts++;
                if (pl.TryMantle(-outward))
                {
                    for (int frame = 0; frame < 20 && pl.Mantling; frame++)
                    {
                        Vector3 previous = pl.transform.position;
                        pl.StepWallAuditMantle(0.04f);
                        if (!CrossedWall(geometry, previous, pl.transform.position, out contact)) continue;
                        Record(site, level, room, face, "mantle", "", false, from, previous, contact, pl.transform.position);
                        break;
                    }
                }

                foreach (float direction in AuditClimbDirections)
                {
                    pl.BeginWallAudit(from);
                    attempts++;
                    for (int frame = 0; frame < 28; frame++)
                    {
                        Vector3 previous = pl.transform.position;
                        int before = pl.WallAuditMotion;
                        bool moved = pl.StepWallAudit(0.05f, -outward, direction, jump: frame == 13);
                        if (CrossedWall(geometry, previous, pl.transform.position, out contact))
                        {
                            string action = direction == 0f ? "climb" : direction < 0f ? "climb-left" : "climb-right";
                            string transition = PlayerController.WallAuditMotionName(before) + ">" + PlayerController.WallAuditMotionName(pl.WallAuditMotion);
                            Record(site, level, room, face, action, transition, false, from, previous, contact, pl.transform.position);
                            break;
                        }
                        if (!moved) break;
                    }
                }
            }

            void Record(Site site, int level, Room room, Side face, string action, string transition, bool overlap,
                Vector3 from, Vector3 previous, Vector3 hit, Vector3 end)
            {
                if (overlap) overlaps++; else leaks++;
                csv.WriteLine($"{DesertWorld.Seed},{site.kind},{site.state},{site.cx},{site.cz},{level},{room.id},{face},{action},{transition},{overlap},{Coordinates(from)},{Coordinates(previous)},{Coordinates(hit)},{Coordinates(end)}");
            }
        }

        static bool CrossedWall(DesertCensus geometry, Vector3 from, Vector3 to, out Vector3 contact)
        {
            contact = default;
            Vector3 horizontal = Vector3.ProjectOnPlane(to - from, Vector3.up);
            Vector3 torso = Vector3.up * 0.68f;
            return horizontal.sqrMagnitude > 0.007f * 0.007f && geometry.CrossesWall(from + torso, to + torso, out contact);
        }

        static string Coordinates(Vector3 p) =>
            FormattableString.Invariant($"{p.x:F3},{p.y - AuditLift:F3},{p.z:F3}");

        void AuditWallControl(PlayerController pl, CharacterController controller)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, AuditLift, 0f);
            wall.transform.localScale = new Vector3(0.3f, 4f, 4f);
            var geometry = new DesertCensus();
            geometry.Add(wall);
            var from = wall.transform.position + Vector3.right;
            var travel = Vector3.left * 2f;
            Physics.SyncTransforms();
            pl.BeginWallAudit(from);
            controller.Move(travel);
            bool blocked = !CrossedWall(geometry, from, pl.transform.position, out _);
            wall.GetComponent<Collider>().enabled = false;
            pl.BeginWallAudit(from);
            controller.Move(travel);
            bool detected = CrossedWall(geometry, from, pl.transform.position, out _);
            Vector3 gap = from + Vector3.forward * 3f;
            pl.BeginWallAudit(gap);
            controller.Move(travel);
            bool gapAccepted = !CrossedWall(geometry, gap, pl.transform.position, out _);
            Destroy(wall);
            Check("parkourwalls: the audit distinguishes a solid wall, missing collision and a clear opening",
                blocked && detected && gapAccepted, $"blocked {blocked}, detected {detected}, opening {gapAccepted}");
        }
    }
}
#endif
