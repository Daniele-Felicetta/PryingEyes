using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>La corrente in cooperativa (Proc/Power.cs): l'ospitato che tira la leva lo chiede all'ospite, che accende e lo dice a
    /// tutti; chi entra tardi a corrente accesa lo riceve con lo stato del piano. Il quadro sta nello stesso posto su ogni macchina
    /// (stesso piano, ricerca deterministica), quindi basta un bit.</summary>
    public sealed partial class CoopGame
    {
        float powerAsked;

        public void RequestPower()
        {
            if (host || net == null || !Started || Power.On || Time.unscaledTime < powerAsked) return;
            powerAsked = Time.unscaledTime + 1f;
            net.SendToHost(CoopKind.UsePower, w.Reset().U8(EpochByte).ToArray());
        }

        /// <summary>Sull'ospite, appena accesa: a tutti.</summary>
        public void PowerChanged()
        {
            if (host && Power.On) SendToReady(CoopKind.PowerState, w.Reset().U8(EpochByte).Bool(true).ToArray());
        }

        void HostUsePower(int from, ref NetReader r)
        {
            int epoch = r.U8();
            if (!r.Ok || epoch != EpochByte || !IsReady(from) || Power.Panel == null || Power.On) return;
            Power.TurnOn(from);
        }

        void ReceivePower(ref NetReader r)
        {
            r.U8(); bool on = r.Bool();
            if (!r.Ok || !on) return;
            Power.TurnOn(-1);
        }

        void SendPowerTo(int slot)
        {
            if (Power.Panel != null && Power.On) net.SendToSlot(slot, CoopKind.PowerState, w.Reset().U8(EpochByte).Bool(true).ToArray());
        }
    }
}
