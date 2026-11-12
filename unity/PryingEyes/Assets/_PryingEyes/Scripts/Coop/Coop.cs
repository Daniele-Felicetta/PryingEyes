using System.Collections.Generic;
using System.Net.NetworkInformation;
using UnityEngine;

namespace PryingEyes
{
    public enum CoopRole { Off, Host, Client }

    /// <summary>Facciata della cooperativa per il resto del gioco: sa solo "da solo / ospite / ospitato" e inoltra gli eventi a CoopGame. Non nomina
    /// FishNet (sta tutto in FishNetCoopNet, dietro PRYINGEYES_FISHNET): nella build web la cooperativa semplicemente non esiste e ogni
    /// chiamata qui sotto non fa niente. Da solo (Role == Off) il gioco si comporta esattamente come prima.
    /// L'ospite fa girare il mondo (nemici, round, gocce) e lo racconta agli altri; ogni giocatore muove se stesso e vede il mondo con i
    /// pupazzi (Enemy.puppet) che l'ospite gli manda.</summary>
    public static class Coop
    {
        public static CoopRole Role { get; private set; } = CoopRole.Off;
        public static bool Active => Role != CoopRole.Off;
        public static bool IsHost => Role == CoopRole.Host;
        public static bool IsClient => Role == CoopRole.Client;
        /// <summary>Qui si simula il mondo (nemici, round, gocce): da solo o come ospite.</summary>
        public static bool Sim => Role != CoopRole.Client;
        /// <summary>Il mio posto tra i giocatori: 0 l'ospite, 1..3 gli ospitati.</summary>
        public static int Slot { get; internal set; }
        public static CoopGame Game { get; private set; }
        /// <summary>Ospitato che ha chiesto di entrare e aspetta il saluto dell'ospite: la partita non e' ancora partita.</summary>
        public static bool WaitingForHost => Role == CoopRole.Client && (Game == null || !Game.Started);
        /// <summary>Ultimo esito (per il menu): "Connessione...", "Rifiutato: ...".</summary>
        public static string Status => Game != null ? Game.Status : lastStatus;
        static string lastStatus = "";

        public static bool Available
        {
            get
            {
#if PRYINGEYES_FISHNET
                return true;
#else
                return false;
#endif
            }
        }

        // Lanciato da riga di comando (le prove a due processi): il nome e l'indirizzo restano in memoria e non toccano le preferenze,
        // che sono condivise con la copia del gioco dell'utente.
        static bool fromArgs;
        static string argName;

        public static string PlayerName
        {
            get => fromArgs && argName != null ? argName : PlayerPrefs.GetString("coopName", "");
            set { if (!fromArgs) PlayerPrefs.SetString("coopName", (value ?? "").Trim()); }
        }
        public static string LastAddress
        {
            get => PlayerPrefs.GetString("coopAddr", "127.0.0.1");
            set { if (!fromArgs) PlayerPrefs.SetString("coopAddr", (value ?? "").Trim()); }
        }

        /// <summary>Gli indirizzi IPv4 di questa macchina, da dire agli amici (menu di pausa dell'ospite): prima quelli delle schede con un router
        /// (gateway), che sono la rete vera o la VPN; se non ce ne sono, tutti quelli attivi. Non esce niente dal computer: si legge e si mostra.</summary>
        public static string LocalAddresses(int max = 4)
        {
            try
            {
                var withGateway = new List<string>();
                var others = new List<string>();
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    bool gw = false;
                    foreach (var g in props.GatewayAddresses)
                        if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)) gw = true;
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                        string s = ua.Address.ToString();
                        if (s.StartsWith("169.254.")) continue;          // indirizzo automatico: la scheda non ha una rete
                        (gw ? withGateway : others).Add(s);
                    }
                }
                var list = withGateway.Count > 0 ? withGateway : others;
                return string.Join(", ", list.GetRange(0, System.Math.Min(System.Math.Max(1, max), list.Count)));
            }
            catch (System.Exception) { return ""; }
        }

        static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs()) if (s == a) return true;
            return false;
        }

        static string ArgStr(string name, string def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        /// <summary>Da riga di comando, per le prove con due processi: -coophost [-cooplocal] oppure -coopjoin indirizzo, con -coopport N e -coopname nome.</summary>
        public static void BootFromArgs()
        {
            if (Active) return;
            bool host = HasArg("-coophost"), join = HasArg("-coopjoin");
            if (!host && !join) return;
            fromArgs = true;
            argName = ArgStr("-coopname", null);
            ushort port = ushort.TryParse(ArgStr("-coopport", ""), out ushort p) ? p : CoopProtocol.DefaultPort;
            if (host) StartHost(port, HasArg("-cooplocal"));
            else StartJoin(ArgStr("-coopjoin", "127.0.0.1"), port);
        }

        public static bool StartHost(ushort port = CoopProtocol.DefaultPort, bool localOnly = false)
        {
            if (Active) return false;
#if PRYINGEYES_FISHNET
            KeepRunning(true);
            var net = FishNetCoopNet.Create();
            Role = CoopRole.Host; Slot = 0;
            Game = CoopGame.Begin(net, true);
            if (!net.StartHost(port, localOnly)) { Stop(net.Status); return false; }
            Debug.Log($"[PryingEyes] coop: ospito sulla porta {port}{(localOnly ? " (solo locale)" : "")}");
            return true;
#else
            lastStatus = "La cooperativa non c'e' in questa versione";
            return false;
#endif
        }

        public static bool StartJoin(string address, ushort port = CoopProtocol.DefaultPort)
        {
            if (Active) return false;
            address = (address ?? "").Trim();
            if (address.Length == 0) { lastStatus = "Scrivi l'indirizzo dell'ospite"; return false; }
#if PRYINGEYES_FISHNET
            LastAddress = address;
            KeepRunning(true);
            var net = FishNetCoopNet.Create();
            Role = CoopRole.Client; Slot = 0;
            Game = CoopGame.Begin(net, false);
            if (!net.StartClient(address, port)) { Stop(net.Status); return false; }
            Debug.Log($"[PryingEyes] coop: mi collego a {address}:{port}");
            return true;
#else
            lastStatus = "La cooperativa non c'e' in questa versione";
            return false;
#endif
        }

        /// <summary>In rete il gioco non deve fermarsi quando la finestra perde il fuoco (un Alt-Tab di uno farebbe cadere o bloccare gli altri):
        /// di serie "Run in background" e' spento, quindi lo si accende finche' dura la partita in cooperativa.</summary>
        static bool prevRunInBackground, keeping;
        static void KeepRunning(bool on)
        {
            if (on && !keeping) { prevRunInBackground = Application.runInBackground; keeping = true; Application.runInBackground = true; }
            else if (!on && keeping) { keeping = false; Application.runInBackground = prevRunInBackground; }
        }

        // ---- eventi del piano, chiamati da FloorDirector (non fanno niente da soli)
        public static void OnFloorStart(int floor) => Game?.OnFloorStart(floor);
        public static void OnFloorBuilt() => Game?.OnFloorBuilt();
        public static void OnDescendBegin(int toFloor) => Game?.OnDescendBegin(toFloor);

        /// <summary>Chiude la sessione. Chi era ospitato resta nel piano e prosegue da solo (i pupazzi spariscono, il piano ricomincia a popolarsi).</summary>
        public static void Stop(string why = null)
        {
            if (why != null) lastStatus = why;
            var g = Game;
            Game = null;
            var wasRole = Role;
            Role = CoopRole.Off; Slot = 0;
            KeepRunning(false);
            if (g != null) g.Close(wasRole);
        }
    }
}
