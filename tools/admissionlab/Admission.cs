using System;
using System.Linq;
using PryingEyes.Multiplayer;

// The host's side of getting into a co-op session: the gate (one reply per challenge, a deadline, a capacity), the token a guest sends
// (protocol byte, seats, content revision) and the roster of connections and their seats.
static partial class Program
{
    static void AdmissionGateChecks()
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

    static void TokenChecks()
    {
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
    }

    static void RosterChecks()
    {
        var roster = new SessionRoster(6);
        Check(!roster.TryJoin(1, 0) && !roster.TryJoin(1, 5) && !roster.TryJoin(-1, 1), "Reject invalid seats/peer");
        Check(roster.TryJoin(10, 2) && roster.TryJoin(20, 4), "Mixed session 2 + 4 seats");
        Check(roster.PlayerCount == 6 && roster.Seats.Length == 6, "Human capacity counts seats");
        Check(!roster.TryJoin(30, 1), "Reject over-capacity");
        Check(roster.TryJoin(10, 2) && !roster.TryJoin(10, 1), "Admission retry idempotent, no silent resize");
        Check(roster.Owns(10, new PlayerSeat(10, 1)) && !roster.Owns(20, new PlayerSeat(10, 1)), "Authenticated ownership");
        Check(!roster.Owns(10, new PlayerSeat(10, 2)), "Reject unregistered local seat");
    }
}
