#if PRYINGEYES_FISHNET
using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    /// <summary>Explicit bootstrap only. Start methods request a connection; status events describe its outcome.</summary>
    [RequireComponent(typeof(NetworkManager), typeof(FishNetSeatAuthenticator))]
    public sealed class FishNetSessionLauncher : MonoBehaviour
    {
        public NetworkObject avatarPrefab, worldPrefab;
        public LocalSeatInput[] inputs;
        public ushort port = 7807;
        public int runSeed = 12345;
        public string Status { get; private set; } = "Stopped";
        public string Failure { get; private set; }
        public FishNetWorldState World { get; internal set; }
        public SessionRoster Roster => admission.Roster;
        public int LocalPlayers => (admission != null ? admission : GetComponent<FishNetSeatAuthenticator>()).localPlayers;
        public bool CanConfigure => !configured || (!stopping && transport.GetConnectionState(true) == LocalConnectionState.Stopped &&
            transport.GetConnectionState(false) == LocalConnectionState.Stopped);
        public readonly HashSet<FishNetSeatAvatar> Avatars = new HashSet<FishNetSeatAvatar>();
        readonly HashSet<int> spawnedPeers = new HashSet<int>();
        NetworkManager manager;
        FishNetSeatAuthenticator admission;
        Tugboat transport;
        bool configured, hosting, stopping;

        public void Host() => StartHost(localOnly: false);
        public void Local() => StartHost(localOnly: true);
        public void SetLocalPlayerCount(int count)
        {
            Configure(); RequireStopped();
            if (count < 1 || count > SessionRoster.MaxLocalPlayers || count > admission.humanCapacity)
                throw new ArgumentOutOfRangeException(nameof(count));
            for (int seat = 0; seat < count; seat++)
                if (InputFor(seat) == null) throw new InvalidOperationException("Missing input source for requested seat.");
            admission.localPlayers = count;
        }

        public void Join(string address)
        {
            Configure(); RequireStopped();
            if (string.IsNullOrWhiteSpace(address) || address.Length > 253) throw new ArgumentException("Missing or invalid host address.");
            hosting = false; Failure = null; Status = "Connecting";
            if (!manager.ClientManager.StartConnection(address, port)) Fail("Client connection request rejected.");
        }

        void StartHost(bool localOnly)
        {
            Configure(); RequireStopped();
            hosting = true; Failure = null; Status = "Starting server";
            transport.SetServerBindAddress(localOnly ? "127.0.0.1" : "0.0.0.0", IPAddressType.IPv4);
            transport.SetServerBindAddress(localOnly ? "::1" : "::", IPAddressType.IPv6);
            transport.SetMaximumClients(admission.humanCapacity);
            if (!manager.ServerManager.StartConnection(port)) Fail("Server connection request rejected.");
        }

        void RequireStopped()
        {
            if (port == 0) throw new InvalidOperationException("Choose a nonzero port.");
            if (transport.GetConnectionState(true) != LocalConnectionState.Stopped ||
                transport.GetConnectionState(false) != LocalConnectionState.Stopped || stopping)
                throw new InvalidOperationException("Wait until the previous connection is stopped.");
        }

        void Configure()
        {
            if (configured) return;
            manager = GetComponent<NetworkManager>();
            admission = GetComponent<FishNetSeatAuthenticator>();
            transport = manager.TransportManager.Transport as Tugboat;
            if (transport == null || port == 0) throw new InvalidOperationException("Assign Tugboat and a nonzero port.");
            if (manager.GetComponent<global::FishNet.Component.Spawning.PlayerSpawner>() != null)
                throw new InvalidOperationException("Remove the default PlayerSpawner; this launcher spawns every seat.");
            if (avatarPrefab == null || worldPrefab == null || avatarPrefab.GetComponent<FishNetSeatAvatar>() == null ||
                worldPrefab.GetComponent<FishNetWorldState>() == null)
                throw new InvalidOperationException("Register and assign avatar/world prefabs.");
            var assigned = new HashSet<int>();
            foreach (var source in inputs ?? Array.Empty<LocalSeatInput>())
                if (source == null || source.seat < 0 || source.seat >= SessionRoster.MaxLocalPlayers || !assigned.Add(source.seat))
                    throw new InvalidOperationException("Assign distinct input sources for local seats.");
            for (int seat = 0; seat < admission.localPlayers; seat++)
                if (!assigned.Contains(seat)) throw new InvalidOperationException("Missing local seat input.");
            manager.ServerManager.SetAuthenticator(admission);
            manager.ServerManager.OnServerConnectionState += ServerChanged;
            manager.ClientManager.OnClientConnectionState += ClientChanged;
            manager.ClientManager.OnAuthenticated += ClientAuthenticated;
            manager.SceneManager.OnClientLoadedStartScenes += ScenesLoaded;
            manager.ServerManager.OnRemoteConnectionState += RemoteChanged;
            configured = true;
        }

        void ServerChanged(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started && hosting)
            {
                try
                {
                    var obj = Instantiate(worldPrefab, Vector3.zero, Quaternion.identity);
                    manager.ServerManager.Spawn(obj);
                    World = obj.GetComponent<FishNetWorldState>();
                    World.BeginFloor(runSeed, 1);
                    Status = "Connecting local client";
                    if (!manager.ClientManager.StartConnection("127.0.0.1", port)) Fail("Local client could not start.");
                }
                catch (Exception error) { Fail(error.Message); Debug.LogException(error, this); }
            }
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                spawnedPeers.Clear(); World = null;
                if (!stopping && hosting && Failure == null) Fail("Server stopped unexpectedly.");
                UpdateStopped();
            }
        }

        void ClientChanged(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            if (!stopping && Failure == null) Fail("Client disconnected before or during the session.");
            UpdateStopped();
        }
        void ClientAuthenticated() { if (!stopping) Status = "Connected; preparing world"; }

        void ScenesLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || stopping || !manager.ServerManager.Started || !spawnedPeers.Add(connection.ClientId)) return;
            try
            {
                foreach (var seat in Roster.Seats)
                {
                    if (seat.Peer != connection.ClientId) continue;
                    var obj = Instantiate(avatarPrefab, Vector3.zero, Quaternion.identity);
                    obj.GetComponent<FishNetSeatAvatar>().InitializeSeat(seat.LocalIndex);
                    manager.ServerManager.Spawn(obj, connection);
                    manager.SceneManager.AddOwnerToDefaultScene(obj);
                }
            }
            catch (Exception error) { Fail(error.Message); Debug.LogException(error, this); }
        }

        void RemoteChanged(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped) spawnedPeers.Remove(connection.ClientId);
            // FishNet despawns every owned avatar; the authenticator removes all roster seats.
        }

        public LocalSeatInput InputFor(int seat)
        {
            foreach (var source in inputs) if (source != null && source.seat == seat) return source;
            return null;
        }

        public void Stop()
        {
            if (!configured) return;
            stopping = true; Status = "Stopping";
            manager.ClientManager.StopConnection();
            manager.ServerManager.StopConnection(true);
            UpdateStopped();
        }
        void Fail(string message) { Failure = message; Stop(); }
        void UpdateStopped()
        {
            if (transport.GetConnectionState(true) != LocalConnectionState.Stopped ||
                transport.GetConnectionState(false) != LocalConnectionState.Stopped) return;
            stopping = false; hosting = false; Status = Failure == null ? "Stopped" : "Failed";
        }

        void OnDestroy()
        {
            if (!configured || manager == null) return;
            manager.ServerManager.OnServerConnectionState -= ServerChanged;
            manager.ClientManager.OnClientConnectionState -= ClientChanged;
            manager.ClientManager.OnAuthenticated -= ClientAuthenticated;
            manager.SceneManager.OnClientLoadedStartScenes -= ScenesLoaded;
            manager.ServerManager.OnRemoteConnectionState -= RemoteChanged;
        }
    }
}
#endif
