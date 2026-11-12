using System;
using System.Linq;
using System.IO;
using PryingEyes;
using PryingEyes.Multiplayer;
using PryingEyes.Proc;
using PryingEyes.Multiplayer.FishNetBackend.EditorTools;

static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    static void Main()
    {
        CheckWorkspaceGuard();
        CheckAdmissionGate();
        CheckPlayerPerks();
        var diagonal = new SeatCommand { MoveX = 1, MoveZ = 1, Turn = 100, Fire = true, Use = true, Sequence = 4, Epoch = 2 }.Sanitized();
        Check(Math.Abs(diagonal.MoveX * diagonal.MoveX + diagonal.MoveZ * diagonal.MoveZ - 1) < .00001f &&
            diagonal.Turn == 1 && diagonal.Fire && diagonal.Use && diagonal.Sequence == 4 && diagonal.Epoch == 2, "Normalize diagonal without changing command identity");
        var extreme = new SeatCommand { MoveX = float.MaxValue, MoveZ = -float.MaxValue }.Sanitized();
        Check(!float.IsNaN(extreme.MoveX) && Math.Abs(extreme.MoveX + extreme.MoveZ) < .00001f && extreme.MoveX > .7f,
            "Normalize extreme finite values without overflow");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var sanitized = new SeatCommand { MoveX = invalid, MoveZ = 1, Turn = invalid }.Sanitized();
            Check(sanitized.MoveX == 0 && sanitized.MoveZ == 0 && sanitized.Turn == 0, "Reject non-finite axes");
        }
        Check(SeatCommand.IsNewer(0, uint.MaxValue), "Accept sequence rollover");
        Check(!SeatCommand.IsNewer(uint.MaxValue, 0) && !SeatCommand.IsNewer(2, 2) && !SeatCommand.IsNewer(1, 2), "Reject stale and duplicate input");
        const string revision = "build-a_1.0";
        for (int seats = 1; seats <= 4; seats++)
        {
            byte[] token = SessionAdmission.Create(seats, revision);
            Check(SessionAdmission.TryRead(token, revision, out int actual) && actual == seats, "Round-trip admission");
            Check(!SessionAdmission.TryRead(token, "build-b_1.0", out _), "Reject incompatible content");
            token[0]++;
            Check(!SessionAdmission.TryRead(token, revision, out _), "Reject incompatible protocol");
            token[0]--; token[1] = 255;
            Check(!SessionAdmission.TryRead(token, revision, out _), "Reject invalid seat count");
        }
        Check(!SessionAdmission.TryRead(null, revision, out _) && !SessionAdmission.TryRead(new byte[0], revision, out _), "Reject absent/truncated token");
        bool invalidRevision = false;
        try { SessionAdmission.Create(1, "bad|revision"); } catch (ArgumentException) { invalidRevision = true; }
        Check(invalidRevision, "Reject noncanonical revision");
        var roster = new SessionRoster(6);
        Check(!roster.AllReady, "Empty session cannot start");
        Check(!roster.TryJoin(1, 0) && !roster.TryJoin(1, 5) && !roster.TryJoin(-1, 1), "Reject invalid seats/peer");
        Check(roster.TryJoin(10, 2) && roster.TryJoin(20, 4), "Mixed session 2 + 4 seats");
        Check(roster.PlayerCount == 6 && roster.Seats.Length == 6, "Human capacity counts seats");
        Check(!roster.TryJoin(30, 1), "Reject over-capacity");
        Check(roster.TryJoin(10, 2) && !roster.TryJoin(10, 1), "Admission retry idempotent, no silent resize");
        Check(roster.Owns(10, new PlayerSeat(10, 1)) && !roster.Owns(20, new PlayerSeat(10, 1)), "Authenticated ownership");
        Check(!roster.Owns(10, new PlayerSeat(10, 2)), "Reject unregistered local seat");
        int epoch = roster.BeginFloor("geometry-a");
        Check(roster.WaitingPeers.OrderBy(p => p).SequenceEqual(new[] { 10, 20 }), "Both connections wait for current floor");
        Check(!roster.Acknowledge(99, epoch, "geometry-a"), "Unknown peer cannot acknowledge");
        Check(!roster.Acknowledge(10, epoch, "wrong"), "Geometry mismatch blocks");
        Check(roster.Acknowledge(10, epoch, "geometry-a") && !roster.AllReady, "Wait for remote geometry");
        Check(roster.Acknowledge(20, epoch, "geometry-a") && roster.AllReady, "All peers ready");
        Check(roster.WaitingPeers.Length == 0, "Ready connections leave timeout queue");
        int next = roster.BeginFloor("geometry-b");
        Check(!roster.AllReady && !roster.Acknowledge(10, epoch, "geometry-a"), "Ignore stale floor acknowledgement");
        roster.Acknowledge(10, next, "geometry-b");
        roster.Leave(20);
        Check(roster.AllReady && roster.PlayerCount == 2, "Disconnect removes readiness requirement and all seats");
        Check(roster.TryJoin(30, 1) && !roster.AllReady, "Late join closes barrier");
        roster.Acknowledge(30, next, "geometry-b");
        Check(roster.AllReady && roster.Seats[1].Equals(new PlayerSeat(10, 1)), "Late join preserves local identities");
        roster.Leave(30); roster.Leave(10);
        Check(!roster.AllReady && !roster.Owns(10, new PlayerSeat(10, 0)), "Empty / disconnected session");

        for (int count = 1; count <= 4; count++)
        {
            float area = 0;
            for (int seat = 0; seat < count; seat++)
            {
                var a = SplitLayout.ForSeat(seat, count);
                Check(a.X >= 0 && a.Y >= 0 && a.X + a.Width <= 1 && a.Y + a.Height <= 1, "Viewport bounds");
                area += a.Width * a.Height;
                for (int other = 0; other < seat; other++)
                {
                    var b = SplitLayout.ForSeat(other, count);
                    Check(a.X >= b.X + b.Width || b.X >= a.X + a.Width ||
                        a.Y >= b.Y + b.Height || b.Y >= a.Y + a.Height, "Viewports cannot overlap");
                }
            }
            Check(area == (count == 3 ? .75f : 1), "Viewport coverage");
        }
        bool rejected = false;
        try { SplitLayout.ForSeat(2, 2); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid seat rejected");

        // Exercise the actual production generator, including compound and themed floors.
        foreach (bool grid in new[] { true, false })
        foreach (int seed in new[] { 1, -12345, int.MaxValue })
        foreach (int floor in new[] { 1, 3, 6 })
        {
            PlanGen.UseGrid = !grid;
            var a = NetworkFloorPlan.Generate(seed, floor, grid);
            var b = NetworkFloorPlan.Generate(seed, floor, grid);
            Check(a.grid == grid && PlanGen.UseGrid == !grid, "Explicit generation mode preserves gameplay default");
            string digest = NetworkFloorPlan.Digest(a);
            Check(digest.Length == 64 && digest == NetworkFloorPlan.Digest(b), "Repeatable generated geometry");
            a.grid = !a.grid;
            Check(digest != NetworkFloorPlan.Digest(a), "Detect changed generator mode");
            a.grid = grid;
            var originalRole = a.rooms[0].type;
            a.rooms[0].type = originalRole == RoomType.Guardia ? RoomType.Normale : RoomType.Guardia;
            Check(digest != NetworkFloorPlan.Digest(a), "Detect changed room role");
            a.rooms[0].type = originalRole;
            a.rooms[0].seed++;
            Check(digest != NetworkFloorPlan.Digest(a), "Detect changed furnishing seed");
            a.rooms[0].seed--;
            a.openings[0].width++;
            Check(digest != NetworkFloorPlan.Digest(a), "Detect changed collision geometry");
            var candidates = NetworkFloorPlan.SpawnCandidates(b).Take(64).ToArray();
            Check(candidates.Length >= 16, "Generated floor offers candidate space for human capacity");
            Check(candidates.SequenceEqual(NetworkFloorPlan.SpawnCandidates(b).Take(64)), "Stable spawn search order");
            Check(candidates.Distinct().Count() == candidates.Length, "Unique initial spawn candidates");
        }
        var spawnPlan = new Plan { start = 0 };
        spawnPlan.rooms.Add(new Room { id = 0, x0 = 0, z0 = 0, x1 = 50, z1 = 50 });
        spawnPlan.rooms.Add(new Room { id = 1, x0 = 100, z0 = 0, x1 = 150, z1 = 50 });
        spawnPlan.rooms.Add(new Room { id = 2, x0 = 200, z0 = 0, x1 = 250, z1 = 50 });
        spawnPlan.openings.Add(new Opening { a = 0, b = 1 });
        var spawnPoints = NetworkFloorPlan.SpawnCandidates(spawnPlan).ToArray();
        Check(spawnPoints.Length == 18 && spawnPoints.All(p => p.x < 20), "Disconnected room excluded from spawn search");
        Check(spawnPoints.Take(9).All(p => p.x >= 1 && p.x <= 4 && p.z >= 1 && p.z <= 4), "Entrance first with wall margin");
        Check(spawnPoints.Skip(9).All(p => p.x >= 11), "Reachable room follows entrance");
        Console.WriteLine("PASS: " + checks + " assertions; roster, mixed seats, floor barrier, viewports, real generator.");
    }

    static void CheckWorkspaceGuard()
    {
        // All fixtures live beneath this test's ignored binary output; no real Git metadata is changed.
        string fixture = Path.Combine(AppContext.BaseDirectory, "guard-fixtures", Guid.NewGuid().ToString("N"));
        string repository = Path.Combine(fixture, "repository");
        string project = Path.Combine(repository, "unity", "PryingEyes");
        string assets = Path.Combine(project, "Assets");
        string metadata = Path.Combine(fixture, "worktree-metadata");
        Directory.CreateDirectory(assets); Directory.CreateDirectory(metadata);
        string marker = Path.Combine(repository, ".git");
        File.WriteAllText(marker, "gitdir: " + metadata);
        File.WriteAllText(Path.Combine(metadata, "HEAD"), "ref: refs/heads/feature/multiplayer\n");
        File.WriteAllText(Path.Combine(metadata, "gitdir"), marker);
        File.WriteAllText(Path.Combine(metadata, "commondir"), "..");
        SampleWorkspaceGuard.AssertCanCreate(project, assets);
        Check(true, "Allow linked feature/multiplayer worktree");
        File.WriteAllText(Path.Combine(metadata, "HEAD"), "ref: refs/heads/temi-fase2");
        CheckGuardRejects(project, assets, "Reject main development branch");
        File.WriteAllText(Path.Combine(metadata, "HEAD"), "e8113f209b6435dbebe052e11eac09853225d2c2");
        CheckGuardRejects(project, assets, "Reject detached worktree");
        File.WriteAllText(Path.Combine(metadata, "HEAD"), "ref: refs/heads/feature/multiplayer");
        File.WriteAllText(Path.Combine(metadata, "gitdir"), Path.Combine(fixture, "other.git"));
        CheckGuardRejects(project, assets, "Reject mismatched worktree backlink");
        File.WriteAllText(Path.Combine(metadata, "gitdir"), marker);
        CheckGuardRejects(project, fixture, "Reject writing outside project Assets");
        File.WriteAllText(marker, "invalid marker");
        CheckGuardRejects(project, assets, "Reject malformed worktree marker");
        File.Delete(marker); Directory.CreateDirectory(marker);
        CheckGuardRejects(project, assets, "Reject ordinary/main Git checkout");
    }

    static void CheckPlayerPerks()
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

    static void CheckGuardRejects(string project, string assets, string message)
    {
        bool rejected = false;
        try { SampleWorkspaceGuard.AssertCanCreate(project, assets); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }

    static void CheckAdmissionGate()
    {
        const string revision = "build-admission";
        var gate = new AdmissionGate(4, revision, 15);
        Check(gate.Begin(10, 100) && !gate.Begin(10, 114), "Repeated challenge cannot extend its deadline");
        Check(gate.TryAdmit(10, SessionAdmission.Create(3, revision), 114.999), "Valid reply before deadline admits all seats");
        Check(gate.Begin(20, 115) && !gate.TryAdmit(20, SessionAdmission.Create(2, revision), 116), "Recheck human capacity when reply arrives");
        Check(gate.Begin(30, 115) && gate.TryAdmit(30, SessionAdmission.Create(1, revision), 116) &&
            gate.Roster.PlayerCount == 4, "Remaining capacity permits another connection");
        Check(!gate.TryAdmit(10, SessionAdmission.Create(3, revision), 117) && !gate.Begin(10, 117), "Admitted peer cannot repeat its handshake");
        gate.Leave(10);
        Check(gate.Roster.PlayerCount == 1 && gate.Begin(10, 118) &&
            gate.TryAdmit(10, SessionAdmission.Create(2, revision), 119), "Disconnect releases all seats for a new handshake");
        Check(gate.Begin(40, 200) && !gate.TryAdmit(40, SessionAdmission.Create(1, revision), 215),
            "Exact deadline rejects reply even before periodic expiry runs");
        Check(gate.Expire(216).Length == 0 && !gate.TryAdmit(40, SessionAdmission.Create(1, revision), 216), "Consumed late reply cannot be retried");
        Check(gate.Begin(50, 300) && !gate.Begin(50, 314) && !gate.TryAdmit(50, SessionAdmission.Create(1, revision), 315),
            "Duplicate challenge does not revive a timed out handshake");
        gate.Begin(60, 400);
        Check(gate.Expire(414.999).Length == 0 && gate.Expire(415).SequenceEqual(new[] { 60 }) &&
            gate.Expire(416).Length == 0, "Expiry fires once, at the deadline");
        Check(!gate.TryAdmit(60, SessionAdmission.Create(1, revision), 416), "Expired peer cannot be admitted later");
        gate.Begin(70, 500);
        Check(!gate.TryAdmit(70, SessionAdmission.Create(1, "different-build"), 501) &&
            !gate.TryAdmit(70, SessionAdmission.Create(1, revision), 502), "Invalid first reply consumes the handshake");
        gate.Begin(80, 600); gate.Leave(80);
        Check(!gate.TryAdmit(80, SessionAdmission.Create(1, revision), 601), "Disconnect cancels pending handshake");
        var host = new AdmissionGate(4, revision, 15);
        Check(host.Begin(0, 0) && host.TryAdmit(0, SessionAdmission.Create(4, revision), 1) && host.Roster.PlayerCount == 4,
            "Connection zero supports four local players");
        foreach (double timeout in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { new AdmissionGate(4, revision, timeout); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Reject invalid admission timeout");
        }
        bool invalidClock = false;
        try { gate.Begin(90, double.NaN); } catch (ArgumentOutOfRangeException) { invalidClock = true; }
        Check(invalidClock, "Invalid clock cannot poison pending admissions");
    }
}
