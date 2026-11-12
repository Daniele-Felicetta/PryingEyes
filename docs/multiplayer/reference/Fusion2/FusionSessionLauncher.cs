#if PRYINGEYES_FUSION2
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace PryingEyes.Multiplayer.Fusion2
{
    /// <summary>Dedicated bootstrap scene only. No automatic startup, no global config mutation.</summary>
    public sealed class FusionSessionLauncher : MonoBehaviour
    {
        public NetworkObject avatarPrefab, worldPrefab;
        public LocalSeatInput[] inputs;
        [Range(1, 4)] public int localPlayers = 1;
        [Range(1, 16)] public int humanCapacity = 8;
        // Assign a build/content hash shared by peers; never use a mutable branch name.
        public string contentRevision = "e8113f20-mp-schema1";
        public int runSeed = 12345;
        public NetworkRunner Runner { get; private set; }
        public FusionWorldState World { get; set; }
        public SessionRoster Roster { get; private set; }
        readonly Dictionary<PlayerRef, List<NetworkObject>> avatars = new Dictionary<PlayerRef, List<NetworkObject>>();
        NetworkEvents callbacks;
        bool startingOrStopping;

        public Task Host(string friendCode) => StartSession(GameMode.Host, friendCode);
        public Task Join(string friendCode) => StartSession(GameMode.Client, friendCode);
        public Task Local() => StartSession(GameMode.Single, null);
        public static string NewFriendCode() => Guid.NewGuid().ToString("N");

        async Task StartSession(GameMode mode, string code)
        {
            if (Runner != null || startingOrStopping) throw new InvalidOperationException("Stop the previous session first.");
            if (localPlayers < 1 || localPlayers > 4 || humanCapacity < localPlayers)
                throw new InvalidOperationException("Invalid local players or capacity.");
            if (mode != GameMode.Single && (string.IsNullOrWhiteSpace(code) || code.Length > 64))
                throw new ArgumentException("Use an exact friend code of 1..64 characters.");
            byte[] token = SessionAdmission.Create(localPlayers, contentRevision);
            if (avatarPrefab == null || worldPrefab == null) throw new InvalidOperationException("Missing network prefabs.");
            if (avatarPrefab.GetComponent<FusionSeatAvatar>() == null || avatarPrefab.GetComponent<NetworkTransform>() == null ||
                worldPrefab.GetComponent<FusionWorldState>() == null)
                throw new InvalidOperationException("Avatar needs FusionSeatAvatar/NetworkTransform; world needs FusionWorldState.");
            var assigned = new HashSet<int>();
            foreach (var source in inputs ?? Array.Empty<LocalSeatInput>())
                if (source == null || source.seat < 0 || source.seat >= localPlayers || !assigned.Add(source.seat))
                    throw new InvalidOperationException("Input sources must be unique and within the local seat range.");
            if (assigned.Count != localPlayers) throw new InvalidOperationException("Assign one input source per local seat.");
            Roster = new SessionRoster(humanCapacity);
            startingOrStopping = true;
            Runner = gameObject.AddComponent<NetworkRunner>();
            Runner.ProvideInput = true;
            callbacks = gameObject.AddComponent<NetworkEvents>();
            callbacks.PlayerJoined.AddListener(PlayerJoined);
            callbacks.PlayerLeft.AddListener(PlayerLeft);
            callbacks.OnInput.AddListener(ReadInput);
            callbacks.OnConnectRequest.AddListener(ConnectRequest);
            Runner.AddCallbacks(callbacks);
            try
            {
                var result = await Runner.StartGame(new StartGameArgs
                {
                    GameMode = mode, SessionName = code, PlayerCount = humanCapacity,
                    IsVisible = false, IsOpen = true, EnableClientSessionCreation = false,
                    ConnectionToken = token
                });
                if (!result.Ok) throw new InvalidOperationException("Fusion start failed: " + result.ShutdownReason);
                if (Runner.IsServer)
                {
                    var obj = Runner.Spawn(worldPrefab);
                    World = obj.GetComponent<FusionWorldState>();
                    World.BeginFloor(runSeed, 1);
                }
            }
            catch { await Shutdown(); throw; }
            finally { startingOrStopping = false; }
        }

        void ConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            if (SessionAdmission.TryRead(token, contentRevision, out int seats) &&
                Roster.PlayerCount + seats <= humanCapacity) request.Accept();
            else request.Refuse();
            // Concurrent requests are checked again against authoritative capacity in PlayerJoined.
        }

        void PlayerJoined(NetworkRunner runner, PlayerRef peer)
        {
            if (!runner.IsServer) return;
            int count = localPlayers;
            if (peer != runner.LocalPlayer)
            {
                var token = runner.GetPlayerConnectionToken(peer);
                if (!SessionAdmission.TryRead(token, contentRevision, out count))
                { runner.Disconnect(peer); return; }
            }
            if (!Roster.TryJoin(peer.RawEncoded, count)) { runner.Disconnect(peer); return; }
            if (avatars.ContainsKey(peer)) return;
            var spawned = new List<NetworkObject>();
            avatars.Add(peer, spawned);
            for (int seat = 0; seat < count; seat++)
            {
                int index = seat;
                spawned.Add(runner.Spawn(avatarPrefab, new Vector3(seat * 2, 2, 0), Quaternion.identity,
                    peer, (r, obj) => obj.GetComponent<FusionSeatAvatar>().SeatIndex = index));
            }
        }

        void PlayerLeft(NetworkRunner runner, PlayerRef peer)
        {
            if (!runner.IsServer) return;
            Roster.Leave(peer.RawEncoded);
            if (!avatars.TryGetValue(peer, out var objects)) return;
            foreach (var obj in objects) if (obj != null) runner.Despawn(obj);
            avatars.Remove(peer);
        }

        void ReadInput(NetworkRunner runner, NetworkInput input)
        {
            var frame = new FusionSeatInput();
            foreach (var source in inputs)
                if (source != null && source.isActiveAndEnabled)
                    frame.Set(source.seat, new SeatCommand { Move = source.Move, Turn = source.Turn, Fire = source.Fire });
            input.Set(frame);
        }

        public Task Stop()
        {
            if (startingOrStopping) throw new InvalidOperationException("Await the current session operation first.");
            return Shutdown();
        }

        async Task Shutdown()
        {
            var old = Runner;
            if (old == null) return;
            startingOrStopping = true;
            await old.Shutdown(destroyGameObject: false);
            // Runner is single-use. Never destroy the bootstrap GameObject or any unrelated runner.
            old.RemoveCallbacks(callbacks);
            callbacks.PlayerJoined.RemoveListener(PlayerJoined);
            callbacks.PlayerLeft.RemoveListener(PlayerLeft);
            callbacks.OnInput.RemoveListener(ReadInput);
            callbacks.OnConnectRequest.RemoveListener(ConnectRequest);
            Destroy(callbacks);
            Destroy(old);
            Runner = null; World = null; Roster = null; avatars.Clear();
            // Wait for deferred Unity destruction before another session can add callbacks on this object.
            await Task.Yield();
            startingOrStopping = false;
        }
    }
}
#endif
