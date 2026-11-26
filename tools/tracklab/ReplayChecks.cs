using System;
using System.Collections.Generic;
using PryingEyes.Pista;
using Production.Racing;

/// <summary>The replay, measured: a race is recorded the way the game records it (a frame of every rider every third step of the physics, from the lights going out), the falls and the overtakes are found in it,
/// and the television director cuts it: the edit must cover the whole race without a gap, start on the grid and end at the line, cut to every fall, show the player often but not always, change camera and rider
/// all the time, put its cameras beside the road and not on it, frame the rider at a sensible size, and give the same edit for the same race.</summary>
static partial class Program
{
    static PoseFrame FrameOfRacer(Racer r, bool fall)
    {
        return new PoseFrame { x = r.x, y = r.y, z = r.z, heading = r.heading, lean = r.lean, speed = r.speed, flags = (ushort)(r.crashed || fall ? PoseFrame.Crashed : 0), rpm = 6000f, gear = 3 };
    }

    /// <summary>Race a field of computer riders and record it; with 'injectFall' rider 3 is made to lie fallen between 25 and 27.5 s, so that there is always a fall to find.</summary>
    static RaceRecording RecordRace(Circuit c, int laps, int riders, int seed, bool injectFall, out Race race)
    {
        race = new Race(c, laps, seed);
        var order = new List<Racer>();
        for (int i = 0; i < riders; i++) order.Add(race.AddAi("AI" + i, i % 3, i, seed * 13 + i * 17, 0.92f + 0.007f * i, 0.30f));
        race.PlaceAll(order);
        race.StartCountdown();
        var names = new string[riders];
        for (int i = 0; i < riders; i++) names[i] = order[i].name;
        var rec = new RaceRecording(riders, names, riders / 2, c.length, laps);
        int step = 0; float t = 0f;
        while (race.phase != RacePhase.Over && t < 900f)
        {
            race.Step(RaceDt); t += RaceDt;
            if (race.phase != RacePhase.Racing && race.phase != RacePhase.Over) continue;
            if (step++ % 3 != 0) continue;
            for (int i = 0; i < riders; i++)
            {
                float ft = rec.tapes[i].Count * PoseTape.FrameTime;
                rec.Add(i, FrameOfRacer(order[i], injectFall && i == 3 && ft >= 25f && ft < 27.5f), order[i].progress);
            }
        }
        for (int i = 0; i < riders; i++) rec.finish[i] = order[i].finishTime;
        rec.Analyse();
        return rec;
    }

    static bool SameShot(in Shot a, in Shot b)
    {
        return a.t0 == b.t0 && a.t1 == b.t1 && a.subject == b.subject && a.kind == b.kind && a.why == b.why && a.ax == b.ax && a.ay == b.ay && a.az == b.az && a.param == b.param;
    }

    static void ReplayChecks()
    {
        bool shape = true, leaderOk = true, fallsFound = true, overtakesOk = true, covers = true, lengths = true, bookends = true, fallsCut = true, playerShare = true, variety = true, deterministic = true, differs = false;
        bool anchors = true, framing = true, fixedSees = true, fixedClose = true, finite = true;
        string dShape = "", dCovers = "", dLengths = "", dBook = "", dFalls = "", dShare = "", dVariety = "", dAnchors = "", dFraming = "", dFixed = "", dClose = "", dFound = "";
        int[] circuits = { 4242, 22, 205 };
        foreach (int cs in circuits)
        {
            var c = Circuits.Generate(cs);
            Race race; var rec = RecordRace(c, 2, 12, cs, true, out race);
            float T = rec.Duration; int me = rec.me;
            // ---- the recording
            bool same = true; for (int i = 1; i < rec.count; i++) same &= rec.tapes[i].Count == rec.tapes[0].Count;
            bool lenOk = Math.Abs(T - race.time) < 0.25f;
            shape &= same && lenOk && rec.count == 12;
            dShape += " [" + cs + ": " + rec.Frames + " frames, " + T.ToString("F1") + " s of " + race.time.ToString("F1") + "]";
            int winner = -1; float tWin = 1e9f;
            for (int r = 0; r < rec.count; r++) if (rec.finish[r] >= 0f && rec.finish[r] < tWin) { tWin = rec.finish[r]; winner = r; }
            leaderOk &= winner >= 0 && rec.LeaderAt(tWin - 0.3f) == winner;
            // ---- what happened
            bool injected = false;
            foreach (var m in rec.crashes) if (m.rider == 3 && Math.Abs(m.t - 25f) < 0.3f) injected = true;
            bool natural = true;
            foreach (var r in race.racers) if (r.crashes > 0) { bool found = false; foreach (var m in rec.crashes) if (m.rider == r.id) found = true; natural &= found; }
            fallsFound &= injected && natural;
            dFound += " [" + cs + ": " + rec.crashes.Count + " falls, " + rec.overtakes.Count + " overtakes]";
            foreach (var m in rec.overtakes) overtakesOk &= m.t >= 6f && m.rider != m.other && Math.Abs(rec.ProgressAt(m.other, m.t) - m.progress) < 31f;
            // ---- the edit
            var plan = ReplayEdit.Plan(rec, c, cs);
            bool contiguous = plan.Count > 3 && plan[0].t0 == 0f && Math.Abs(plan[plan.Count - 1].t1 - T) < 1e-3f;
            for (int i = 1; i < plan.Count; i++) contiguous &= Math.Abs(plan[i].t0 - plan[i - 1].t1) < 1e-3f;
            covers &= contiguous;
            dCovers += " [" + cs + ": " + plan.Count + " shots over " + T.ToString("F0") + " s]";
            float minLen = 1e9f, maxLen = 0f;
            foreach (var s in plan) { minLen = Math.Min(minLen, s.Length); maxLen = Math.Max(maxLen, s.Length); }
            lengths &= minLen >= 2.0f && maxLen <= 12.5f;
            dLengths += " [" + cs + ": " + minLen.ToString("F1") + " to " + maxLen.ToString("F1") + " s]";
            // start and end
            bool startOk = plan.Count > 0 && plan[0].kind == ShotKind.Grid && plan[0].subject == me;
            bool finishOk = false;
            foreach (var s in plan) if (s.kind == ShotKind.Finish && s.subject == winner && s.t0 <= tWin && s.t1 >= tWin) finishOk = true;
            float tMe = rec.finish[me]; bool meOk = tMe < 0f || me == winner;
            if (!meOk) foreach (var s in plan) if (s.subject == me && s.t0 <= tMe && s.t1 >= tMe) meOk = true;
            bookends &= startOk && finishOk && meOk;
            dBook += " [" + cs + ": grid " + startOk + ", winner " + finishOk + ", player " + meOk + "]";
            // every fall has a cut
            float tailStart = T; foreach (var s in plan) if (s.kind == ShotKind.Finish) { tailStart = s.t0; break; }
            int falls = 0, cut = 0;
            foreach (var m in rec.crashes)
            {
                if (m.t < 6f || m.t > tailStart - 3.5f) continue;
                falls++;
                foreach (var s in plan) if (s.t0 <= m.t && s.t1 >= m.t && ((s.subject == m.rider) || s.why == ShotWhy.Crash)) { cut++; break; }
            }
            fallsCut &= cut == falls;
            dFalls += " [" + cs + ": " + cut + " of " + falls + " falls cut to]";
            // the player on screen
            float withMe = 0f; foreach (var s in plan) if (s.subject == me) withMe += s.Length;
            float share = withMe / T;
            playerShare &= share > 0.30f && share < 0.75f;
            dShare += " [" + cs + ": " + (share * 100f).ToString("F0") + " %]";
            // the cameras
            var kinds = new HashSet<ShotKind>(); bool noRepeat = true; float onboard = 0f;
            for (int i = 0; i < plan.Count; i++)
            {
                kinds.Add(plan[i].kind);
                if (plan[i].kind == ShotKind.Onboard) onboard += plan[i].Length;
                if (i > 0 && plan[i].kind == plan[i - 1].kind && plan[i].subject == plan[i - 1].subject) noRepeat = false;
            }
            variety &= kinds.Count >= 5 && noRepeat && onboard / T < 0.25f;
            dVariety += " [" + cs + ": " + kinds.Count + " cameras, onboard " + (onboard / T * 100f).ToString("F0") + " %, repeats " + !noRepeat + "]";
            // the same edit twice, and another for another seed
            var again = ReplayEdit.Plan(rec, c, cs); var other = ReplayEdit.Plan(rec, c, cs + 1);
            deterministic &= again.Count == plan.Count; for (int i = 0; deterministic && i < plan.Count; i++) deterministic &= SameShot(plan[i], again[i]);
            if (other.Count != plan.Count) differs = true; else for (int i = 0; i < plan.Count; i++) if (!SameShot(plan[i], other[i])) differs = true;
            // the places of the cameras
            int hint = -1, anchored = 0, fixedShots = 0, fixedSeen = 0, fixedNear = 0, sampled = 0, sized = 0;
            float farthest = 0f;
            float worstOff = 1e9f, worstHeight = 1e9f;
            foreach (var s in plan)
            {
                if (s.Anchored)
                {
                    anchored++;
                    Proj pr; c.Project(s.ax, s.az, ref hint, out pr);
                    int side = pr.d >= 0f ? 1 : 0;
                    float barrier = c.HalfWidthAt(pr) + c.Lerp(c.run[side], pr.i, pr.t);
                    float off = Math.Abs(pr.d) - barrier, height = s.ay - c.SurfaceY(pr.i, pr.t, pr.d);
                    worstOff = Math.Min(worstOff, off); worstHeight = Math.Min(worstHeight, height);
                    anchors &= off > 1.5f && off < 70f && height > 0.4f;
                }
                for (float t = s.t0; t <= s.t1; t += 0.1f)
                {
                    CamPose p;
                    if (!ShotCamera.Pose(s, rec, t, out p)) continue;
                    finite &= !float.IsNaN(p.x + p.y + p.z + p.lx + p.ly + p.lz + p.fov) && !float.IsInfinity(p.x + p.y + p.z + p.lx + p.ly + p.lz + p.fov) && p.fov > 5f && p.fov < 80f;
                    if (s.kind == ShotKind.Pan || s.kind == ShotKind.Grid || s.kind == ShotKind.Finish)
                    {
                        float dx = p.lx - p.x, dy = p.ly - p.y, dz = p.lz - p.z, dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        float frame = 2f * dist * (float)Math.Tan(p.fov * 0.5f * Math.PI / 180f);
                        sampled++;
                        if (2f / frame > 0.03f && 2f / frame < 0.6f) sized++;
                    }
                }
                if (s.kind == ShotKind.Fixed && s.pass - 0.6f >= s.t0)
                {
                    fixedShots++;
                    CamPose p; ShotCamera.Pose(s, rec, s.t0, out p);
                    bool all = true;
                    var atPass = rec.PoseAt(s.subject, s.pass);
                    float gap = (float)Math.Sqrt((atPass.x - p.x) * (atPass.x - p.x) + (atPass.z - p.z) * (atPass.z - p.z));
                    farthest = Math.Max(farthest, gap);
                    if (gap < 30f) fixedNear++;
                    foreach (float back in new[] { 0.6f, 0.3f })
                    {
                        var f = rec.PoseAt(s.subject, s.pass - back);
                        float ax = p.lx - p.x, ay = p.ly - p.y, az = p.lz - p.z, sx = f.x - p.x, sy = f.y + 0.9f - p.y, sz = f.z - p.z;
                        // inside the picture of a 16:10 window: the turn left or right against half the width of the lens, the tilt against half its height
                        double yaw = Math.Abs(RacingMath.WrapPi((float)(Math.Atan2(ax, az) - Math.Atan2(sx, sz)))) * 180.0 / Math.PI;
                        double tilt = Math.Abs(Math.Atan2(ay, Math.Sqrt(ax * ax + az * az)) - Math.Atan2(sy, Math.Sqrt(sx * sx + sz * sz))) * 180.0 / Math.PI;
                        double halfWide = Math.Atan(Math.Tan(p.fov * 0.5 * Math.PI / 180.0) * 1.6) * 180.0 / Math.PI;
                        all &= yaw < halfWide - 3.0 && tilt < p.fov * 0.5 - 2.0;
                    }
                    if (all) fixedSeen++;
                }
            }
            dAnchors += " [" + cs + ": " + anchored + " fixed cameras, nearest " + worstOff.ToString("F1") + " m beyond the barrier, lowest " + worstHeight.ToString("F1") + " m up]";
            framing &= sampled > 0 && sized >= sampled * 0.85;
            dFraming += " [" + cs + ": " + sized + " of " + sampled + " frames with the rider at a good size]";
            fixedSees &= fixedShots == 0 || fixedSeen >= fixedShots * 0.8;
            dFixed += " [" + cs + ": " + fixedSeen + " of " + fixedShots + " low cameras see the rider come]";
            fixedClose &= fixedShots == 0 || fixedNear >= fixedShots * 0.85;
            dClose += " [" + cs + ": " + fixedNear + " of " + fixedShots + " within 30 m, farthest " + farthest.ToString("F0") + " m]";
        }
        Check("REPLAY_RECORDING_HAS_EVERY_RIDER_AND_THE_WHOLE_RACE", shape, "frames and duration against the race:" + dShape, "all tapes the same length, as long as the race to a quarter of a second");
        Check("REPLAY_WINNER_LEADS_JUST_BEFORE_THE_LINE", leaderOk, "the distance along the race agrees with the finish", "the leader a moment before the winner's time is the winner");
        Check("REPLAY_FINDS_THE_FALLS", fallsFound, "the injected fall of rider 3 at 25 s and every rider that fell in the race:" + dFound, "found");
        Check("REPLAY_OVERTAKES_ARE_REAL", overtakesOk, "after six seconds, between two different riders no more than thirty metres apart", "none against it");
        Check("REPLAY_EDIT_COVERS_THE_RACE_WITHOUT_A_GAP", covers, "shots end to end from 0 to the end:" + dCovers, "contiguous");
        Check("REPLAY_EDIT_SHOTS_ARE_WATCHABLE_LENGTHS", lengths, "shortest and longest:" + dLengths, "between 2 and 12.5 s");
        Check("REPLAY_EDIT_STARTS_ON_THE_GRID_AND_ENDS_AT_THE_LINE", bookends, "first shot on the player's grid, the winner crossing the line, the player crossing it:" + dBook, "all three");
        Check("REPLAY_EDIT_CUTS_TO_EVERY_FALL", fallsCut, "falls between the start and the finish shots:" + dFalls, "every one has its shot (or the shot of a fall a few seconds before)");
        Check("REPLAY_EDIT_SHOWS_THE_PLAYER_OFTEN_NOT_ALWAYS", playerShare, "share of the time on the player:" + dShare, "between 30 and 75 %");
        Check("REPLAY_EDIT_CHANGES_CAMERA_AND_RIDER", variety, "kinds of camera used, onboard share, repeats:" + dVariety, "five or more cameras, no two shots in a row alike, onboard under a quarter");
        Check("REPLAY_EDIT_IS_REPEATABLE_AND_DEPENDS_ON_THE_SEED", deterministic && differs, "same race and seed: " + deterministic + ", another seed: " + (differs ? "another edit" : "the same edit"), "same edit, other seed other edit");
        Check("REPLAY_CAMERAS_ARE_BESIDE_THE_ROAD_NOT_ON_IT", anchors, "distance beyond the barrier and height above the ground:" + dAnchors, "1.5 to 70 m beyond the barrier, over 0.4 m up");
        Check("REPLAY_PAN_KEEPS_THE_RIDER_AT_A_GOOD_SIZE", framing, "the rider's height against the picture:" + dFraming, "between 3 and 60 % for 85 % of the frames");
        Check("REPLAY_LOW_CAMERA_SEES_THE_RIDER_COME", fixedSees, "the rider inside the lens a moment before the pass:" + dFixed, "at least 80 % of them");
        Check("REPLAY_LOW_CAMERA_IS_CLOSE_TO_THE_RIDER", fixedClose, "the distance from the low camera to the rider at the pass:" + dClose, "under 30 m for at least 85 % of them");
        Check("REPLAY_CAMERA_POSES_ARE_FINITE", finite, "every camera of every shot at ten a second", "no NaN, a field of view between 5 and 80 degrees");
    }
}
