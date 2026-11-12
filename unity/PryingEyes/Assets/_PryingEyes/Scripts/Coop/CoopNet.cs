using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Trasporto della cooperativa, senza niente di FishNet: il gioco (CoopGame) parla solo con questa classe, quindi la
    /// build web, dove FishNet non c'e', compila lo stesso. L'ospite e' lo slot 0 e non ha una connessione verso se stesso: gli altri
    /// (1..3) sono connessioni vere. Tutti i callback arrivano sul thread principale.</summary>
    public abstract class CoopNet : MonoBehaviour
    {
        /// <summary>Ospite: il server e' partito. Ospitato: connesso, ammesso e salutato dall'ospite.</summary>
        public abstract bool Ready { get; }
        public abstract bool IsHost { get; }
        /// <summary>Testo di stato per il menu ("Connessione a 192.168.1.5...", "Rifiutato: versione diversa").</summary>
        public abstract string Status { get; }
        /// <summary>Slot dei giocatori collegati (solo l'ospite ne ha: i suoi ospiti).</summary>
        public abstract IReadOnlyCollection<int> RemoteSlots { get; }

        public abstract void SendToHost(CoopKind kind, byte[] data, bool reliable = true);
        public abstract void SendToSlot(int slot, CoopKind kind, byte[] data, bool reliable = true);
        /// <summary>Dall'ospite: a tutti i giocatori collegati tranne (eventualmente) uno slot.</summary>
        public abstract void SendToAll(CoopKind kind, byte[] data, bool reliable = true, int exceptSlot = -1);
        public abstract void Shutdown();

        /// <summary>Ospite: uno slot e' stato assegnato a chi ha superato l'ammissione / se n'e' andato.</summary>
        public event Action<int> PeerJoined, PeerLeft;
        /// <summary>Un messaggio (slot di chi lo manda: per un ospitato e' sempre 0, l'ospite).</summary>
        public event Action<int, CoopKind, byte[]> Message;
        /// <summary>Ospitato: l'ospite ha detto si e il canale e' pronto per i messaggi del gioco.</summary>
        public event Action Authenticated;
        /// <summary>Connessione chiusa o rifiutata; il testo dice perche'.</summary>
        public event Action<string> Closed;

        protected void RaisePeerJoined(int slot) => PeerJoined?.Invoke(slot);
        protected void RaisePeerLeft(int slot) => PeerLeft?.Invoke(slot);
        protected void RaiseMessage(int fromSlot, CoopKind kind, byte[] data) => Message?.Invoke(fromSlot, kind, data);
        protected void RaiseAuthenticated() => Authenticated?.Invoke();
        protected void RaiseClosed(string why) => Closed?.Invoke(why);
    }
}
