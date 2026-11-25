#if PRYINGEYES_FISHNET
using System;
using FishNet.Authenticating;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    public struct SeatAdmissionRequest : IBroadcast
    {
        public byte[] Token;
    }

    public struct SeatAdmissionChallenge : IBroadcast { }

    /// <summary>Compatibility and seat admission, not account authentication. Assign to ServerManager before starting.</summary>
    public sealed class FishNetSeatAuthenticator : Authenticator
    {
        [Range(1, 4)] public int localPlayers = 1;
        [Range(1, 16)] public int humanCapacity = 8;
        public string contentRevision = "0b9e9d7-d28c34dcfb80-mp-schema3";
        [Min(1)] public float admissionTimeoutSeconds = 15;
        public SessionRoster Roster => gate?.Roster;
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;
        AdmissionGate gate;

        public override void InitializeOnce(NetworkManager manager)
        {
            if (Initialized) return;
            // Fail configuration before registering any callback or accepting network traffic.
            SessionAdmission.Create(localPlayers, contentRevision);
            if (humanCapacity < localPlayers || humanCapacity > 16 || admissionTimeoutSeconds < 1 ||
                float.IsNaN(admissionTimeoutSeconds) || float.IsInfinity(admissionTimeoutSeconds))
                throw new InvalidOperationException("Invalid capacity or admission timeout.");
            gate = new AdmissionGate(humanCapacity, contentRevision, admissionTimeoutSeconds);
            base.InitializeOnce(manager);
            manager.ServerManager.RegisterBroadcast<SeatAdmissionRequest>(ReceiveAdmission, requireAuthentication: false);
            manager.ClientManager.RegisterBroadcast<SeatAdmissionChallenge>(ReceiveChallenge);
            manager.ServerManager.OnRemoteConnectionState += RemoteStateChanged;
            manager.ServerManager.OnServerConnectionState += ServerStateChanged;
        }

        public override void OnRemoteConnection(NetworkConnection connection)
        {
            if (!gate.Begin(connection.ClientId, Time.realtimeSinceStartupAsDouble))
            { OnAuthenticationResult?.Invoke(connection, false); return; }
            NetworkManager.ServerManager.Broadcast(connection, new SeatAdmissionChallenge(), requireAuthenticated: false);
        }

        void ReceiveChallenge(SeatAdmissionChallenge message, Channel channel)
        {
            // Server issues the challenge only after FishNet's own version handshake has completed.
            NetworkManager.ClientManager.Broadcast(new SeatAdmissionRequest
            { Token = SessionAdmission.Create(localPlayers, contentRevision) }, Channel.Reliable);
        }

        void ReceiveAdmission(NetworkConnection connection, SeatAdmissionRequest message, Channel channel)
        {
            // Never accept an unsolicited/repeated handshake or a caller-provided connection identity.
            bool admitted = gate.TryAdmit(connection.ClientId, message.Token, Time.realtimeSinceStartupAsDouble);
            OnAuthenticationResult?.Invoke(connection, admitted);
        }

        void RemoteStateChanged(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            gate.Leave(connection.ClientId);
        }

        void ServerStateChanged(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            gate = new AdmissionGate(humanCapacity, contentRevision, admissionTimeoutSeconds);
        }

        void Update()
        {
            if (!Initialized) return;
            foreach (int peer in gate.Expire(Time.realtimeSinceStartupAsDouble))
                if (NetworkManager.ServerManager.Clients.TryGetValue(peer, out var connection))
                    OnAuthenticationResult?.Invoke(connection, false);
        }

        void OnDestroy()
        {
            if (!Initialized || NetworkManager == null) return;
            NetworkManager.ServerManager.UnregisterBroadcast<SeatAdmissionRequest>(ReceiveAdmission);
            NetworkManager.ClientManager.UnregisterBroadcast<SeatAdmissionChallenge>(ReceiveChallenge);
            NetworkManager.ServerManager.OnRemoteConnectionState -= RemoteStateChanged;
            NetworkManager.ServerManager.OnServerConnectionState -= ServerStateChanged;
        }
    }
}
#endif
