using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Trappole delle soglie in cooperativa (DoorTraps): l'ospitato riserva i 1000 punti e chiede, l'ospite accende (se la
    /// trappola e' pronta e la porta aperta) e risponde; lo stato di tutte le trappole (pronta, accesa, in ricarica, secondi rimasti)
    /// va a tutti a ogni cambio e a chi entra tardi. Danni e uccisioni li fa solo l'ospite.</summary>
    public sealed partial class CoopGame
    {
        int pendingTrap, trapSequence;

        public void RequestTrap(int index)
        {
            if (host || net == null || !Started || pendingTrap != 0 || DoorTraps.I == null) return;
            var gm = GameManager.I;
            if (gm.score < DoorTraps.Price) { gm.hud?.Toast("Punti insufficienti."); return; }
            pendingTrap = trapSequence = trapSequence % 65534 + 1;
            gm.AddPoints(-DoorTraps.Price);
            net.SendToHost(CoopKind.UseTrap, w.Reset().U8(EpochByte).U16(pendingTrap).U8(index).ToArray());
        }

        void SettleTrap(int request, bool accepted)
        {
            if (pendingTrap == 0 || request != pendingTrap) return;
            if (!accepted) { GameManager.I.AddPoints(DoorTraps.Price); GameManager.I.hud?.Toast("Trappola non disponibile: punti restituiti."); }
            pendingTrap = 0;
        }

        byte[] TrapSnapshot()
        {
            var traps = DoorTraps.I.Traps;
            w.Reset().U8(EpochByte).U8(traps.Count);
            foreach (var t in traps) w.U8(t.state).U16((ushort)Mathf.Clamp(Mathf.RoundToInt(t.timer * 10f), 0, 65535));
            return w.ToArray();
        }

        public void TrapsChanged()
        {
            if (host && DoorTraps.I != null) SendToReady(CoopKind.TrapsState, TrapSnapshot());
        }

        void HostUseTrap(int from, ref NetReader r)
        {
            int epoch = r.U8(), request = r.U16(), index = r.U8();
            if (!r.Ok) return;
            var traps = DoorTraps.I;
            bool accepted = epoch == EpochByte && IsReady(from) && traps != null && index < traps.Traps.Count && traps.Traps[index].HostActivate(from);
            net.SendToSlot(from, CoopKind.TrapReply, w.Reset().U16(request).Bool(accepted).ToArray());
        }

        void ReceiveTraps(ref NetReader r)
        {
            r.U8(); int count = r.U8();
            if (count > 16 || DoorTraps.I == null) return;
            var traps = DoorTraps.I.Traps;
            for (int i = 0; i < count; i++)
            {
                int st = r.U8(); float tm = r.U16() * 0.1f;
                if (!r.Ok) return;
                if (i < traps.Count) traps[i].ApplyState(st, tm);
            }
        }
    }
}
