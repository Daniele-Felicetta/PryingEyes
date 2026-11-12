using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>La partita in cooperativa vista dal gioco: chi c'e', in che piano siamo, dove sono i compagni. Parla solo con CoopNet (il
    /// trasporto) e con le classi del gioco; il mondo condiviso (nemici, candele, gocce) sta in CoopGame.World.cs.
    ///
    /// Regole del piano: l'ospite decide in che piano si e' e gli da' un'epoca (numero che cresce a ogni cambio). Ogni macchina costruisce il
    /// piano da se' dal seme, quindi uguale per tutti; i messaggi del mondo portano l'epoca e si applicano solo quando il piano e' costruito e
    /// l'epoca e' quella giusta (gli altri si tengono da parte o si scartano).</summary>
    public sealed partial class CoopGame : MonoBehaviour
    {
        public static CoopGame I;
        CoopNet net;
        bool host;
        public bool Started { get; private set; }               // ospitato: ha ricevuto il saluto, la partita e' partita
        public string Status => net != null ? net.Status : "";
        public bool IsHost => host;

        // ---- piano ed epoca
        public int Epoch { get; private set; }
        public int EpochByte => Epoch & 0xFF;
        int epochFloor;                                           // il piano a cui si riferisce l'epoca
        bool builtLocal, readySent;
        /// <summary>Il piano di questa macchina e' costruito ed e' quello dell'epoca corrente.</summary>
        public bool FloorBuilt
        {
            get
            {
                var fd = FloorDirector.I;
                return builtLocal && Epoch > 0 && fd != null && fd.floor == epochFloor;
            }
        }

        // ---- giocatori
        /// <summary>Gli altri giocatori (slot -> pupazzo), mai se stessi.</summary>
        public readonly Dictionary<int, RemotePlayer> Players = new Dictionary<int, RemotePlayer>();
        readonly Dictionary<int, string> names = new Dictionary<int, string>();
        readonly Dictionary<int, PlayerNet> states = new Dictionary<int, PlayerNet>();     // ospite: l'ultimo stato di ognuno
        readonly Dictionary<int, int> readyEpoch = new Dictionary<int, int>();             // ospite: l'epoca che ognuno ha costruito
        readonly List<int> pendingWelcome = new List<int>();
        float sendT;
        readonly NetWriter w = new NetWriter();

        public static string MyName => string.IsNullOrEmpty(Coop.PlayerName) ? "Giocatore " + (Coop.Slot + 1) : Coop.PlayerName;
        public string NameOf(int slot) => slot == Coop.Slot ? MyName : names.TryGetValue(slot, out var n) ? n : "Giocatore " + (slot + 1);

        public static CoopGame Begin(CoopNet net, bool host)
        {
            var go = new GameObject("CoopGame");
            var g = go.AddComponent<CoopGame>();
            g.net = net; g.host = host; I = g;
            net.PeerJoined += g.OnPeerJoined;
            net.PeerLeft += g.OnPeerLeft;
            net.Message += g.OnMessage;
            net.Authenticated += g.OnAuthenticated;
            net.Closed += g.OnClosed;
            if (host) { g.Started = true; g.names[0] = MyName; }
            return g;
        }

        public void Close(CoopRole was)
        {
            if (net != null)
            {
                net.PeerJoined -= OnPeerJoined; net.PeerLeft -= OnPeerLeft; net.Message -= OnMessage;
                net.Authenticated -= OnAuthenticated; net.Closed -= OnClosed;
                net.Shutdown();
                net = null;
            }
            foreach (var p in Players.Values) if (p != null) { p.DropLight(); Destroy(p.gameObject); }
            Players.Clear();
            if (was == CoopRole.Client && Started)
            {
                // chi era ospitato resta nel piano e prosegue da solo: via i pupazzi, il piano ricomincera' a popolarsi
                Enemy.ClearAll();
                GameManager.I?.hud?.Toast("Connessione persa: prosegui da solo.");
            }
            if (I == this) I = null;
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ collegamento
        void OnAuthenticated()
        {
            // ammesso (versione compatibile, posto libero): dico chi sono, l'ospite risponde col saluto
            net.SendToHost(CoopKind.Hello, w.Reset().Str(Coop.PlayerName).ToArray());
        }

        void OnClosed(string why)
        {
            Debug.Log("[PryingEyes] coop: chiusa (" + why + ")");
            Coop.Stop(why);
        }

        void OnPeerJoined(int slot) => Debug.Log($"[PryingEyes] coop: slot {slot} collegato");

        void OnPeerLeft(int slot)
        {
            Debug.Log($"[PryingEyes] coop: slot {slot} se n'e' andato ({NameOf(slot)})");
            RemovePlayer(slot);
            pendingWelcome.Remove(slot);
            states.Remove(slot); readyEpoch.Remove(slot);
            net.SendToAll(CoopKind.PlayerLeft, w.Reset().U8(slot).ToArray());
            HostPlayerGone(slot);
            names.Remove(slot);
        }

        RemotePlayer GetPlayer(int slot)
        {
            if (slot == Coop.Slot || slot < 0 || slot >= CoopProtocol.MaxPlayers) return null;
            if (Players.TryGetValue(slot, out var rp) && rp != null) return rp;
            rp = RemotePlayer.Create(slot, NameOf(slot));
            Players[slot] = rp;
            return rp;
        }

        void RemovePlayer(int slot)
        {
            if (Players.TryGetValue(slot, out var rp) && rp != null) { rp.DropLight(); Destroy(rp.gameObject); }
            Players.Remove(slot);
        }

        // ------------------------------------------------------------------ messaggi
        void OnMessage(int from, CoopKind kind, byte[] data)
        {
            if (kind == CoopKind.Test) { CoopTest.OnNet(from, data); return; }       // solo la prova a due processi lo usa
            if (Defer(kind, data)) return;                 // ospitato: messaggio del mondo per un piano che non c'e' (ancora): da parte o buttato
            var r = new NetReader(data);
            if (host) HostMessage(from, kind, ref r); else ClientMessage(kind, ref r);
        }

        void HostMessage(int from, CoopKind kind, ref NetReader r)
        {
            switch (kind)
            {
                case CoopKind.Hello:
                {
                    string n = r.Str();
                    names[from] = string.IsNullOrWhiteSpace(n) ? "Giocatore " + (from + 1) : n;
                    TryWelcome(from);
                    break;
                }
                case CoopKind.PlayerState:
                {
                    var s = PlayerNet.Read(ref r);
                    if (!r.Ok || !names.ContainsKey(from)) return;
                    states[from] = s;
                    GetPlayer(from)?.Apply(s);
                    break;
                }
                case CoopKind.FloorReady:
                {
                    int ep = r.U16();
                    if (!r.Ok) break;
                    readyEpoch[from] = ep;
                    Debug.Log($"[PryingEyes] coop: {NameOf(from)} ha costruito il piano (epoca {ep})");
                    if (ep == (Epoch & 0xFFFF)) HostWorldReady(from);       // gli racconto il piano: nemici, candele, uscita, round, gocce
                    break;
                }
                default:
                    HostWorldMessage(from, kind, ref r);
                    break;
            }
        }

        void ClientMessage(CoopKind kind, ref NetReader r)
        {
            switch (kind)
            {
                case CoopKind.Welcome:
                {
                    int slot = r.U8(), seed = r.I32(), floor = r.I32(), ep = r.U16(), n = r.U8();
                    if (!r.Ok || Started) return;
                    Coop.Slot = slot;
                    Epoch = ep; epochFloor = floor;
                    for (int i = 0; i < n; i++)
                    {
                        int s = r.U8(); string nm = r.Str();
                        if (!r.Ok) break;
                        names[s] = nm;
                        if (s != slot) GetPlayer(s);
                    }
                    Started = true;
                    Debug.Log($"[PryingEyes] coop: ammesso come slot {slot} ({MyName}), piano {floor} seme {seed} epoca {ep}, {n} giocatori");
                    GameManager.I?.BeginCoopRun(seed, floor);
                    break;
                }
                case CoopKind.PlayerJoined:
                {
                    int s = r.U8(); string nm = r.Str();
                    if (!r.Ok || s == Coop.Slot) return;
                    names[s] = nm;
                    GetPlayer(s)?.SetName(nm);
                    GameManager.I?.hud?.Toast(nm + " e' entrato nella partita.");
                    break;
                }
                case CoopKind.PlayerLeft:
                {
                    int s = r.U8();
                    if (!r.Ok) return;
                    GameManager.I?.hud?.Toast(NameOf(s) + " ha lasciato la partita.");
                    RemovePlayer(s);
                    names.Remove(s);
                    break;
                }
                case CoopKind.PlayersState:
                {
                    int n = r.U8();
                    for (int i = 0; i < n; i++)
                    {
                        int s = r.U8();
                        var st = PlayerNet.Read(ref r);
                        if (!r.Ok) return;
                        GetPlayer(s)?.Apply(st);
                    }
                    break;
                }
                case CoopKind.FloorChange:
                {
                    int seed = r.I32(), floor = r.I32(), ep = r.U16();
                    if (!r.Ok) return;
                    Epoch = ep; epochFloor = floor;
                    readySent = false;
                    var fd = FloorDirector.I;
                    if (fd != null) fd.CoopFollow(seed, floor);
                    TrySendReady();
                    ReplayBuffered();
                    break;
                }
                default:
                    ClientWorldMessage(kind, ref r);
                    break;
            }
        }

        // ------------------------------------------------------------------ saluto (ospite)
        void TryWelcome(int slot)
        {
            var fd = FloorDirector.I;
            if (fd == null || fd.floor == 0 || Epoch == 0)
            {
                if (!pendingWelcome.Contains(slot)) pendingWelcome.Add(slot);      // la partita dell'ospite non e' ancora partita
                return;
            }
            w.Reset().U8(slot).I32(fd.runSeed).I32(epochFloor).U16(Epoch);
            var list = new List<int> { 0 };
            foreach (int s in net.RemoteSlots) if (s != slot && names.ContainsKey(s)) list.Add(s);
            w.U8(list.Count);
            foreach (int s in list) w.U8(s).Str(NameOf(s));
            net.SendToSlot(slot, CoopKind.Welcome, w.ToArray());
            net.SendToAll(CoopKind.PlayerJoined, w.Reset().U8(slot).Str(NameOf(slot)).ToArray(), true, slot);
            GetPlayer(slot);
            GameManager.I?.hud?.Toast(NameOf(slot) + " e' entrato nella partita.");
            Debug.Log($"[PryingEyes] coop: {NameOf(slot)} (slot {slot}) salutato: piano {epochFloor} seme {fd.runSeed} epoca {Epoch}");
            HostWorldWelcome(slot);
        }

        // ------------------------------------------------------------------ eventi del piano (da FloorDirector)
        /// <summary>FloorDirector.GoTo comincia: l'ospite annuncia il piano (se non l'ha gia' fatto scendendo).</summary>
        public void OnFloorStart(int floor)
        {
            builtLocal = false; readySent = false;
            foreach (var rp in Players.Values) if (rp != null) rp.DropLight();
            if (host && epochFloor != floor) AnnounceFloor(floor);
            OnFloorStartWorld();
        }

        /// <summary>L'ospite decide la discesa: tutti cominciano la dissolvenza insieme.</summary>
        public void OnDescendBegin(int toFloor)
        {
            if (host) AnnounceFloor(toFloor);
        }

        void AnnounceFloor(int floor)
        {
            Epoch++; epochFloor = floor;
            readyEpoch.Clear();
            var fd = FloorDirector.I;
            net.SendToAll(CoopKind.FloorChange, w.Reset().I32(fd != null ? fd.runSeed : 0).I32(floor).U16(Epoch).ToArray());
            Debug.Log($"[PryingEyes] coop: piano {floor}, epoca {Epoch}");
        }

        /// <summary>Il piano di questa macchina e' costruito (nemici esclusi: li manda l'ospite).</summary>
        public void OnFloorBuilt()
        {
            builtLocal = true;
            foreach (var rp in Players.Values) if (rp != null) rp.ReRegisterLight();
            TrySendReady();
            if (host)
            {
                readyEpoch[0] = Epoch;
                var list = new List<int>(pendingWelcome);
                pendingWelcome.Clear();
                foreach (int s in list) TryWelcome(s);
            }
            OnFloorBuiltWorld();
            ReplayBuffered();
        }

        void TrySendReady()
        {
            if (host || readySent || !FloorBuilt || net == null) return;
            readySent = true;
            net.SendToHost(CoopKind.FloorReady, w.Reset().U16(Epoch).ToArray());
            Debug.Log($"[PryingEyes] coop: piano {epochFloor} costruito (epoca {Epoch}), lo dico all'ospite");
        }

        // ------------------------------------------------------------------ ogni fotogramma
        PlayerNet MyState()
        {
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            var s = new PlayerNet { epoch = FloorBuilt ? EpochByte : 0 };
            if (pl == null) return s;
            s.pos = pl.transform.position; s.yaw = pl.yaw; s.pitch = pl.pitch;
            s.alive = pl.alive; s.crouch = pl.IsCrouching; s.sprint = pl.IsSprinting;
            s.torch = Torch.I != null && Torch.I.Lit;
            s.speedH = pl.SpeedH;
            s.lum = Mathf.Clamp01(Stealth.PlayerLum);
            s.shotAge = Mathf.Min(2.5f, Mathf.Max(0f, Time.time - Stealth.lastShotTime)); s.shotQuiet = Stealth.lastShotQuiet;
            s.hp = Mathf.Clamp01(pl.hp / Mathf.Max(1f, pl.MaxHp));
            s.invulnerable = pl.invulnerable;
            s.lean = pl.lean; s.sight = Perks.SightMult; s.velo = Perks.VeloOn;
            return s;
        }

        /// <summary>Lo stato dell'ospite (slot 0) come lo vedono gli altri.</summary>
        public PlayerNet HostState => MyState();

        /// <summary>Solo per la prova a due processi (CoopTest): un messaggio libero all'altro lato (l'ospite a tutti, l'ospitato all'ospite).</summary>
        internal void SendTest(byte[] data)
        {
            if (net == null) return;
            if (host) net.SendToAll(CoopKind.Test, data); else net.SendToHost(CoopKind.Test, data);
        }

        // ------------------------------------------------------------------ prove (solo con -autotest)
        float dbgT, walkT;
        static readonly bool WalkArg = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopwalk") >= 0;

        /// <summary>Per le prove a due processi: ogni 4 secondi una riga con dove sono io e dove vedo i compagni; con -coopwalk il giocatore
        /// automatico gira in cerchio invece di restare fermo, cosi' la posa cambia.</summary>
        void DebugTick()
        {
            var pl = GameManager.I != null ? GameManager.I.player : null;
            if (WalkArg && FloorBuilt && pl != null)
            {
                walkT += Time.deltaTime;
                PlayerController.botDir = new Vector3(Mathf.Sin(walkT * 0.35f + Coop.Slot), 0f, Mathf.Cos(walkT * 0.35f + Coop.Slot));
            }
            dbgT -= Time.unscaledDeltaTime;
            if (dbgT > 0f) return;
            dbgT = 4f;
            string me = pl != null ? System.FormattableString.Invariant($"pos {pl.transform.position.x:F1} {pl.transform.position.y:F1} {pl.transform.position.z:F1} yaw {pl.yaw * Mathf.Rad2Deg:F0}") : "-";
            var sb = new System.Text.StringBuilder($"[PryingEyes] coop stato: io {MyName} slot {Coop.Slot} {me} epoca {Epoch} piano {(FloorDirector.I != null ? FloorDirector.I.floor : -1)} costruito {FloorBuilt};");
            foreach (var kv in Players)
            {
                var p = kv.Value;
                if (p == null) continue;
                sb.Append(System.FormattableString.Invariant($" {p.playerName}[{kv.Key}] pos {p.last.pos.x:F1} {p.last.pos.y:F1} {p.last.pos.z:F1} yaw {p.last.yaw * Mathf.Rad2Deg:F0} epoca {p.last.epoch} torcia {p.last.torch} vivo {p.last.alive} visto {p.Shown};"));
            }
            Debug.Log(sb.ToString());
        }

        void Update()
        {
            if (net == null) return;
            if (CoopTest.Wanted) CoopTest.Ensure(this);
            if (!host && !Started) return;
            if (GameManager.autoTest && !CoopTest.Wanted) DebugTick();
            WorldUpdate();
            sendT -= Time.unscaledDeltaTime;
            if (sendT > 0f) return;
            sendT = 1f / 15f;
            var me = MyState();
            if (!host)
            {
                w.Reset(); me.Write(w);
                net.SendToHost(CoopKind.PlayerState, w.ToArray(), false);
                return;
            }
            foreach (int to in net.RemoteSlots)
            {
                if (!names.ContainsKey(to) || pendingWelcome.Contains(to)) continue;
                w.Reset();
                int count = 1;
                foreach (int o in net.RemoteSlots) if (o != to && states.ContainsKey(o)) count++;
                w.U8(count).U8(0); me.Write(w);
                foreach (int o in net.RemoteSlots)
                    if (o != to && states.TryGetValue(o, out var st)) { w.U8(o); st.Write(w); }
                net.SendToSlot(to, CoopKind.PlayersState, w.ToArray(), false);
            }
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }
    }
}
