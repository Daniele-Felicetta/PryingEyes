#if PRYINGEYES_FISHNET
using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using PryingEyes.Multiplayer.FishNetBackend;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>L'unico pacchetto di rete della cooperativa: il tipo (CoopKind) e i suoi byte (CoopWire).</summary>
    public struct CoopPacket : IBroadcast
    {
        public byte Kind;
        public byte[] Data;
    }

    /// <summary>Trasporto della cooperativa sopra FishNet (Tugboat, UDP): il server c'e' solo sull'ospite e il suo giocatore sta fuori dalla rete
    /// (slot 0, nessun client verso se stesso). L'ammissione (versione del contenuto, capienza, tempo massimo) la fa l'autenticatore di ChatGPT
    /// (FishNetSeatAuthenticator); tutto il resto viaggia come CoopPacket attraverso Broadcast, senza oggetti di rete da generare: la scena e i nemici
    /// li costruisce ogni macchina da se', come nel gioco da solo.</summary>
    public sealed class FishNetCoopNet : CoopNet
    {
        NetworkManager manager;
        Tugboat transport;
        FishNetSeatAuthenticator auth;
        SinglePrefabObjects spawnables;
        readonly Dictionary<int, int> slotOfClient = new Dictionary<int, int>();        // ClientId di FishNet -> slot
        readonly Dictionary<int, NetworkConnection> connOfSlot = new Dictionary<int, NetworkConnection>();
        readonly List<int> slotsView = new List<int>();
        bool host, ready, stopping, started;
        string status = "Spento";

        public override bool Ready => ready;
        public override bool IsHost => host;
        public override string Status => status;
        public override IReadOnlyCollection<int> RemoteSlots => slotsView;

        /// <summary>Costruisce a runtime tutto lo stack di FishNet (NetworkManager, Tugboat, autenticatore). L'oggetto resta spento finche' non e'
        /// montato: cosi' gli Awake dei componenti girano in ordine, con tutto gia' al suo posto.</summary>
        public static FishNetCoopNet Create()
        {
            var go = new GameObject("CoopNet");
            go.SetActive(false);
            var tug = go.AddComponent<Tugboat>();
            go.AddComponent<TransportManager>().Transport = tug;
            go.AddComponent<ServerManager>().SetStartOnHeadless(false);     // il gioco in batchmode non deve partire come server da solo
            var admission = go.AddComponent<FishNetSeatAuthenticator>();
            admission.localPlayers = 1;
            admission.humanCapacity = CoopProtocol.MaxPlayers - 1;           // l'ospite non e' una connessione
            admission.contentRevision = CoopProtocol.Revision;
            var nm = go.AddComponent<NetworkManager>();
            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = "CoopSpawnables";
            nm.SpawnablePrefabs = prefabs;   // niente oggetti di rete: l'elenco e' vuoto
            var net = go.AddComponent<FishNetCoopNet>();
            net.manager = nm; net.transport = tug; net.auth = admission;
            net.spawnables = prefabs;
            go.SetActive(true);
            return net;
        }

        // ------------------------------------------------------------------ avvio
        public bool StartHost(ushort port, bool localOnly = false)
        {
            host = true; status = "Avvio del server";
            transport.SetMaximumClients(CoopProtocol.MaxPlayers - 1);
            transport.SetTimeout(CoopProtocol.TimeoutSeconds, true);        // di serie sono 30 minuti: un giocatore che sparisce resterebbe nel gioco
            transport.SetServerBindAddress(localOnly ? "127.0.0.1" : "0.0.0.0", FishNet.Transporting.IPAddressType.IPv4);
            transport.SetServerBindAddress(localOnly ? "::1" : "::", FishNet.Transporting.IPAddressType.IPv6);
            manager.ServerManager.OnServerConnectionState += ServerState;
            manager.ServerManager.OnAuthenticationResult += AuthResult;
            manager.ServerManager.OnRemoteConnectionState += RemoteState;
            manager.ServerManager.RegisterBroadcast<CoopPacket>(ServerPacket);
            manager.ServerManager.SetAuthenticator(auth);
            started = true;
            if (!manager.ServerManager.StartConnection(port)) { status = "Il server non parte (porta occupata?)"; RaiseClosed(status); return false; }
            return true;
        }

        public bool StartClient(string address, ushort port)
        {
            host = false; status = "Connessione a " + address + "...";
            transport.SetTimeout(CoopProtocol.TimeoutSeconds, false);       // un ospite che non risponde piu': dopo questi secondi si prosegue da soli
            manager.ClientManager.OnClientConnectionState += ClientState;
            manager.ClientManager.OnAuthenticated += ClientAuthenticated;
            manager.ClientManager.RegisterBroadcast<CoopPacket>(ClientPacket);
            manager.ServerManager.SetAuthenticator(auth);        // inizializza anche la risposta alla sfida di ammissione
            started = true;
            if (!manager.ClientManager.StartConnection(address, port)) { status = "Connessione non avviata"; RaiseClosed(status); return false; }
            return true;
        }

        // ------------------------------------------------------------------ ospite
        void ServerState(ServerConnectionStateArgs a)
        {
            if (a.ConnectionState == LocalConnectionState.Started) { ready = true; status = "In attesa di giocatori"; }
            else if (a.ConnectionState == LocalConnectionState.Stopped)
            {
                bool wasReady = ready;
                ready = false;
                slotOfClient.Clear(); connOfSlot.Clear(); slotsView.Clear();
                if (!stopping) { status = wasReady ? "Il server si e' fermato" : "Il server non e' partito"; RaiseClosed(status); }
            }
        }

        void AuthResult(NetworkConnection conn, bool passed)
        {
            if (conn == null) return;
            if (!passed) { Debug.Log("[PryingEyes] coop: un'ammissione e' stata rifiutata (versione del gioco diversa, albergo pieno o tempo scaduto)"); return; }
            int slot = 1;
            while (connOfSlot.ContainsKey(slot) && slot < CoopProtocol.MaxPlayers) slot++;
            if (slot >= CoopProtocol.MaxPlayers || connOfSlot.ContainsKey(slot)) { conn.Disconnect(true); return; }
            slotOfClient[conn.ClientId] = slot;
            connOfSlot[slot] = conn;
            RefreshSlots();
            RaisePeerJoined(slot);
        }

        void RemoteState(NetworkConnection conn, RemoteConnectionStateArgs a)
        {
            if (a.ConnectionState != RemoteConnectionState.Stopped || conn == null) return;
            if (!slotOfClient.TryGetValue(conn.ClientId, out int slot)) return;
            slotOfClient.Remove(conn.ClientId);
            connOfSlot.Remove(slot);
            RefreshSlots();
            RaisePeerLeft(slot);
        }

        void RefreshSlots()
        {
            slotsView.Clear();
            foreach (var k in connOfSlot.Keys) slotsView.Add(k);
            slotsView.Sort();
        }

        void ServerPacket(NetworkConnection conn, CoopPacket p, Channel ch)
        {
            if (conn == null || !slotOfClient.TryGetValue(conn.ClientId, out int slot)) return;     // chi non e' ammesso non parla
            RaiseMessage(slot, (CoopKind)p.Kind, p.Data);
        }

        // ------------------------------------------------------------------ ospitato
        void ClientState(ClientConnectionStateArgs a)
        {
            if (a.ConnectionState == LocalConnectionState.Started) status = "Connesso, in ammissione";
            else if (a.ConnectionState == LocalConnectionState.Stopped)
            {
                bool wasReady = ready;
                ready = false;
                if (!stopping) { status = wasReady ? "Connessione persa" : "Connessione rifiutata o senza risposta"; RaiseClosed(status); }
            }
        }

        void ClientAuthenticated()
        {
            ready = true; status = "Ammesso";
            RaiseAuthenticated();
        }

        void ClientPacket(CoopPacket p, Channel ch) => RaiseMessage(0, (CoopKind)p.Kind, p.Data);

        // ------------------------------------------------------------------ invio
        static Channel Ch(bool reliable, int len) => reliable || len > CoopProtocol.MaxUnreliable ? Channel.Reliable : Channel.Unreliable;

        public override void SendToHost(CoopKind kind, byte[] data, bool reliable = true)
        {
            if (!started || host || manager == null || !manager.ClientManager.Started) return;
            manager.ClientManager.Broadcast(new CoopPacket { Kind = (byte)kind, Data = data }, Ch(reliable, data.Length));
        }

        public override void SendToSlot(int slot, CoopKind kind, byte[] data, bool reliable = true)
        {
            if (!started || !host || !connOfSlot.TryGetValue(slot, out var conn)) return;
            manager.ServerManager.Broadcast(conn, new CoopPacket { Kind = (byte)kind, Data = data }, true, Ch(reliable, data.Length));
        }

        public override void SendToAll(CoopKind kind, byte[] data, bool reliable = true, int exceptSlot = -1)
        {
            if (!started || !host || connOfSlot.Count == 0) return;
            var pkt = new CoopPacket { Kind = (byte)kind, Data = data };
            var ch = Ch(reliable, data.Length);
            if (exceptSlot > 0 && connOfSlot.TryGetValue(exceptSlot, out var skip)) manager.ServerManager.BroadcastExcept(skip, pkt, true, ch);
            else manager.ServerManager.Broadcast(pkt, true, ch);
        }

        // ------------------------------------------------------------------ chiusura
        public override void Shutdown()
        {
            if (stopping) return;
            stopping = true; ready = false; status = "Spento";
            if (manager != null)
            {
                if (manager.ClientManager != null) manager.ClientManager.StopConnection();
                if (manager.ServerManager != null) manager.ServerManager.StopConnection(true);
            }
            StartCoroutine(DestroyAfterFlush());
        }

        IEnumerator DestroyAfterFlush()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (spawnables != null) Destroy(spawnables);
            if (manager == null) return;
            if (manager.ServerManager != null)
            {
                manager.ServerManager.OnServerConnectionState -= ServerState;
                manager.ServerManager.OnAuthenticationResult -= AuthResult;
                manager.ServerManager.OnRemoteConnectionState -= RemoteState;
                manager.ServerManager.UnregisterBroadcast<CoopPacket>(ServerPacket);
            }
            if (manager.ClientManager != null)
            {
                manager.ClientManager.OnClientConnectionState -= ClientState;
                manager.ClientManager.OnAuthenticated -= ClientAuthenticated;
                manager.ClientManager.UnregisterBroadcast<CoopPacket>(ClientPacket);
            }
        }
    }
}
#endif
