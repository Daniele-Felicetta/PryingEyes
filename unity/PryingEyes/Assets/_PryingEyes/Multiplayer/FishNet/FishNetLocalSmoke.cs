#if PRYINGEYES_FISHNET
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    /// <summary>Explicit CLI-only integration check. No installation or connection without -mpSmokeLocal.</summary>
    public sealed class FishNetLocalSmoke : MonoBehaviour
    {
        [Serializable] public sealed class Result
        {
            public bool passed;
            public int assertions, completedLocalPlayerCounts;
            public string unityVersion, error;
        }
        readonly Result result = new Result();
        FishNetSessionLauncher session;
        FishNetSeatAvatar driven;
        FishNetSeatAvatar[] others;
        Vector3 before;
        Vector3[] otherPositions;
        float yaw;
        int count = 1, phase, nextEpoch;
        double deadline;
        GameObject oldGeometry;
        bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Environment.GetCommandLineArgs().Contains("-mpSmokeLocal")) return;
            new GameObject("Multiplayer Local Smoke").AddComponent<FishNetLocalSmoke>();
        }

        void Start()
        {
            result.unityVersion = Application.unityVersion;
            session = FindAnyObjectByType<FishNetSessionLauncher>();
            deadline = Time.realtimeSinceStartupAsDouble + 120;
            if (session == null) Finish(false, "Probe launcher missing. Open only the generated probe scene.");
        }
        void Update()
        {
            if (finished) return;
            try
            {
                if (phase != 2 && Time.realtimeSinceStartupAsDouble > deadline) throw new Exception("Timed out in phase " + phase + " / local players " + count);
                if (session.Failure != null) throw new Exception(session.Failure);
                if (session.World != null && session.World.Failure != null) throw new Exception(session.World.Failure);
                switch (phase)
                {
                    case 0:
                        foreach (var input in session.inputs) { input.useKeyboard = false; input.Submit(Vector2.zero, 0, false); }
                        session.SetLocalPlayerCount(count); session.Local(); phase = 1; break;
                    case 1:
                        if (!Ready()) return;
                        var avatars = session.Avatars.OrderBy(a => a.SeatIndex).ToArray();
                        Assert(avatars.Length == count, "One avatar per local seat");
                        for (int i = 0; i < avatars.Length; i++)
                        {
                            Assert(avatars[i].SeatIndex == i && avatars[i].IsOwner, "Local ownership and stable seat index");
                            var camera = avatars[i].GetComponentInChildren<Camera>(true);
                            var viewport = SplitLayout.ForSeat(i, count);
                            Assert(camera.enabled && camera.rect == new Rect(viewport.X, viewport.Y, viewport.Width, viewport.Height), "Independent camera viewport");
                            Assert(avatars[i].GetComponentInChildren<AudioListener>(true).enabled == (i == 0), "Exactly one local audio listener");
                        }
                        driven = avatars[0]; others = avatars.Skip(1).ToArray();
                        before = driven.transform.position; yaw = driven.transform.eulerAngles.y;
                        otherPositions = others.Select(a => a.transform.position).ToArray();
                        session.InputFor(0).Submit(new Vector2(.5f, 0), .5f, false);
                        deadline = Time.realtimeSinceStartupAsDouble + .4; phase = 2; break;
                    case 2:
                        // This phase intentionally waits; the global timeout is its movement sampling deadline.
                        break;
                    case 3:
                        if (!Ready() || session.World.Descriptor.Epoch != nextEpoch) return;
                        Assert(oldGeometry == null || !oldGeometry.activeInHierarchy, "Old floor collision removed");
                        foreach (var avatar in session.Avatars) Assert(avatar.PlacedEpoch == nextEpoch, "Avatar repositioned for next floor");
                        session.Stop(); deadline = Time.realtimeSinceStartupAsDouble + 30; phase = 4; break;
                    case 4:
                        if (!session.CanConfigure || session.World != null || session.Avatars.Count != 0) return;
                        Assert(true, "Session stopped and all avatars cleaned up");
                        result.completedLocalPlayerCounts = count;
                        if (++count > 4) { Finish(true, null); return; }
                        deadline = Time.realtimeSinceStartupAsDouble + 120; phase = 0; break;
                }
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        void LateUpdate()
        {
            if (finished || phase != 2 || Time.realtimeSinceStartupAsDouble < deadline) return;
            try
            {
                session.InputFor(0).Submit(Vector2.zero, 0, false);
                Assert(HorizontalDistance(before, driven.transform.position) > .05f, "Selected local input moves its avatar");
                Assert(Mathf.Abs(Mathf.DeltaAngle(yaw, driven.transform.eulerAngles.y)) > 5, "Selected local input rotates its avatar");
                for (int i = 0; i < others.Length; i++) Assert(HorizontalDistance(otherPositions[i], others[i].transform.position) < .02f, "Other local input remains neutral");
                oldGeometry = session.World.GetComponent<FishNetProceduralFloor>().Current.root;
                var descriptor = session.World.Descriptor;
                session.World.BeginFloor(descriptor.RunSeed, descriptor.Floor + 1);
                nextEpoch = session.World.Descriptor.Epoch;
                deadline = Time.realtimeSinceStartupAsDouble + 120; phase = 3;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        bool Ready() => session.World != null && session.World.LocalReady && session.World.MovementEnabled &&
            session.Avatars.Count > 0 && session.Avatars.All(a => a.PlacedEpoch == session.World.Descriptor.Epoch);
        static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        void Assert(bool value, string message) { if (!value) throw new Exception(message); result.assertions++; }
        void Finish(bool passed, string error)
        {
            if (finished) return; finished = true;
            result.passed = passed; result.error = error;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-mpReport");
            if (index >= 0 && index + 1 < args.Length) File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true));
            Debug.Log("[MultiplayerSmoke] " + JsonUtility.ToJson(result));
            if (session != null) session.Stop();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
#else
            Application.Quit(passed ? 0 : 1);
#endif
        }
    }
}
#endif
