#if PRYINGEYES_FISHNET
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    /// <summary>Opt-in two-process transport check; each process drives and observes real network avatars.</summary>
    public sealed class FishNetNetworkSmoke : MonoBehaviour
    {
        [Serializable] public sealed class Result
        {
            public bool passed, host;
            public int localSeats, totalSeats, assertions;
            public string error, unityVersion;
        }
        readonly Result result = new Result();
        FishNetSessionLauncher session;
        FishNetSeatAvatar driven;
        Vector3 ownStart, remoteStart;
        FishNetSeatAvatar remote;
        GameObject oldGeometry;
        double deadline, sampledAt, readyAt;
        bool sampled, remoteMoved, changedFloor, finished;
        bool observedIgnition, observedExit, holdingInteraction, completed, remoteInteractionTested;
        double interactionAt, exitAt, completeAt, remoteInteractionAt;
        int expectedLit;
        int remoteInteractionPhase;
        string reportPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Arg("-mpSmokeNetwork") != null)
                new GameObject("Multiplayer Network Smoke").AddComponent<FishNetNetworkSmoke>();
        }
        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        void Start()
        {
            try
            {
                reportPath = Arg("-mpReport");
                string role = Arg("-mpSmokeNetwork");
                if (role != "host" && role != "client") throw new ArgumentException("Choose host or client.");
                result.host = role == "host"; result.unityVersion = Application.unityVersion;
                result.localSeats = int.Parse(Arg("-mpSeats") ?? "2");
                result.totalSeats = int.Parse(Arg("-mpTotalSeats") ?? "4");
                session = FindAnyObjectByType<FishNetSessionLauncher>();
                foreach (var input in session.inputs) { input.useKeyboard = false; input.Submit(Vector2.zero, 0, false); }
                session.port = ushort.Parse(Arg("-mpPort") ?? "17807");
                session.SetLocalPlayerCount(result.localSeats);
                if (result.host) session.Host(); else session.Join(Arg("-mpAddress") ?? "127.0.0.1");
                deadline = Time.realtimeSinceStartupAsDouble + 180;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        void Update()
        {
            if (finished) return;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now > deadline) throw new Exception("Network smoke timeout; " + session.Status + "; avatars=" + session.Avatars.Count);
                if (session.Failure != null)
                {
                    if (completed && session.Failure == "Client disconnected before or during the session.")
                    { Finish(true, null); return; }
                    throw new Exception(session.Failure);
                }
                var world = session.World;
                if (world == null || !world.LocalReady || !world.MovementEnabled) return;
                if (world.Failure != null) throw new Exception(world.Failure);
                var avatars = session.Avatars.ToArray();
                if (avatars.Length != result.totalSeats || avatars.Any(a => a.PlacedEpoch != world.Descriptor.Epoch)) return;
                if (driven == null)
                {
                    Assert(avatars.Count(a => a.IsOwner) == result.localSeats, "Correct local ownership count");
                    Assert(avatars.Count(a => a.IsOwner && a.GetComponentInChildren<Camera>(true).enabled) == result.localSeats,
                        "All owned seat cameras enabled");
                    Assert(avatars.Where(a => !a.IsOwner).All(a => !a.GetComponentInChildren<Camera>(true).enabled), "Remote cameras disabled");
                    Assert(avatars.Select(a => a.OwnerId + ":" + a.SeatIndex).Distinct().Count() == result.totalSeats, "Unique peer/seat identities");
                    driven = avatars.Single(a => a.IsOwner && a.SeatIndex == 0);
                    remote = avatars.First(a => !a.IsOwner && a.SeatIndex == 0);
                    ownStart = driven.transform.position; remoteStart = remote.transform.position;
                    oldGeometry = world.GetComponent<FishNetProceduralFloor>().Current.root;
                    session.InputFor(0).Submit(new Vector2(.5f, 0), .5f, false);
                    sampledAt = now;
                }
                if (!sampled && now - sampledAt >= .7)
                {
                    session.InputFor(0).Submit(Vector2.zero, 0, false);
                    Assert(Vector3.Distance(ownStart, driven.transform.position) > .05f, "Owned input travels through host simulation");
                    sampled = true;
                }
                if (world.Descriptor.Floor == 1 && Vector3.Distance(remoteStart, remote.transform.position) > .05f) remoteMoved = true;
                if (result.host && sampled && remoteMoved && !changedFloor)
                {
                    world.BeginFloor(world.Descriptor.RunSeed, 2); changedFloor = true;
                    // BeginFloor invalidates readiness; wait for both builders and all placement snapshots.
                    return;
                }
                if (world.Descriptor.Floor < 2 || !sampled) return;
                if (readyAt == 0)
                {
                    Assert(remoteMoved, "Observed movement from the other process");
                    Assert(oldGeometry == null || !oldGeometry.activeInHierarchy, "Replaced previous floor geometry");
                    Assert(avatars.All(a => a.PlacedEpoch == world.Descriptor.Epoch), "All local and remote seats on current floor");
                    readyAt = now;
                }
                var objectives = world.GetComponent<FishNetTowerObjectives>();
                if (objectives == null || objectives.Epoch != world.Descriptor.Epoch) return;
                if (world.Descriptor.Floor == 2)
                {
                    if (objectives.LitCount > 0) observedIgnition = true;
                    if (objectives.ExitUnlocked) observedExit = true;
                    if (result.host) DriveObjectives(objectives, now);
                    else session.InputFor(0).Submit(Vector2.zero, 0, false,
                        !observedIgnition && (int)((now - readyAt) * 5) % 2 == 0);
                    return;
                }
                if (!completed)
                {
                    Assert(observedIgnition, "Candle ignition replicated to this process");
                    Assert(observedExit, "Completed objectives unlock the exit on this process");
                    Assert(world.Descriptor.Floor == 3, "Validated exit interaction advances the procedural floor");
                    completed = true; completeAt = now;
                }
                if (now - completeAt > (result.host ? 2 : 3)) Finish(true, null);
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        void DriveObjectives(FishNetTowerObjectives objectives, double now)
        {
            var current = session.World.GetComponent<FishNetProceduralFloor>().Current;
            if (!remoteInteractionTested)
            {
                if (remoteInteractionAt == 0)
                {
                    Assert(objectives.ObjectiveCount > 0, "Procedural floor contains cooperative objectives");
                    remote.PlaceAt(new Vector3(10000, .1f, 10000), session.World.Descriptor.Epoch);
                    remoteInteractionAt = now;
                }
                if (remoteInteractionPhase == 0)
                {
                    if (now - remoteInteractionAt < .6) return;
                    Assert(objectives.LitCount == 0, "Remote interaction out of range cannot ignite objectives");
                    var first = current.candles.First();
                    PlaceForInteraction(first, first.transform, remote);
                    remoteInteractionAt = now; remoteInteractionPhase = 1;
                }
                if (objectives.LitCount == 0)
                {
                    if (now - remoteInteractionAt > 5) throw new Exception("Remote seat interaction did not reach the host.");
                    return;
                }
                Assert(true, "Remote seat input ignites an authoritative candle");
                remoteInteractionTested = true;
            }
            if (holdingInteraction)
            {
                if (now - interactionAt < .15) return;
                session.InputFor(0).Submit(Vector2.zero, 0, false);
                if (objectives.LitCount < expectedLit)
                {
                    if (now - interactionAt > 2) throw new Exception("Authoritative candle interaction did not complete.");
                    return;
                }
                holdingInteraction = false; interactionAt = now;
                return;
            }
            if (now - interactionAt < .15) return;
            if (!objectives.ExitUnlocked)
            {
                var candle = current.candles.First(c => !c.lit);
                PlaceForInteraction(candle, candle.transform);
                expectedLit = objectives.LitCount + 1; holdingInteraction = true; interactionAt = now;
                session.InputFor(0).Submit(Vector2.zero, 0, false, true);
                return;
            }
            if (exitAt == 0) { exitAt = now; return; }
            if (now - exitAt < 1) return; // Allow the remote process to observe the unlocked snapshot.
            PlaceForInteraction(current.exit, current.exit.transform);
            session.InputFor(0).Submit(Vector2.zero, 0, false, true);
        }
        void PlaceForInteraction(PryingEyes.Proc.IInteract target, Transform root, FishNetSeatAvatar actor = null)
        {
            // Test-only server placement. The interaction itself still passes through ordinary seat input/authority.
            for (int i = 0; i < 8; i++)
            {
                var direction = Quaternion.Euler(0, i * 45, 0) * Vector3.forward;
                var feet = target.Point - direction * 1.5f; feet.y = .1f;
                var origin = feet + Vector3.up * 1.62f;
                var delta = target.Point - origin;
                if (Physics.Raycast(origin, delta.normalized, out var hit, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.transform != root && !hit.transform.IsChildOf(root)) continue;
                (actor != null ? actor : driven).PlaceAt(feet, session.World.Descriptor.Epoch, i * 45);
                return;
            }
            throw new Exception("No test interaction position has a clear line of sight.");
        }
        void Assert(bool condition, string message)
        { if (!condition) throw new Exception(message); result.assertions++; }
        void Finish(bool passed, string error)
        {
            if (finished) return; finished = true; result.passed = passed; result.error = error;
            if (reportPath != null) File.WriteAllText(reportPath, JsonUtility.ToJson(result, true));
            Debug.Log("[MultiplayerNetworkSmoke] " + JsonUtility.ToJson(result));
            if (session != null) session.Stop();
            Application.Quit(passed ? 0 : 1);
        }
    }
}
#endif
