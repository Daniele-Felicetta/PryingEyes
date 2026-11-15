using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Lampade che si rompono in cooperativa (Proc/BreakableLamps.cs): il colpo di un ospitato va all'ospite (LampHit), l'ospite
    /// la rompe e lo dice a tutti (LampBroken); a chi entra tardi manda quelle gia' rotte, senza scoppio. Le lampade sono le stesse su
    /// ogni macchina (stesso piano, stesso ordine del pool).</summary>
    public sealed partial class CoopGame
    {
        public void LampHit(int index)
        {
            if (host || net == null || !Started || !FloorBuilt) return;
            net.SendToHost(CoopKind.LampHit, w.Reset().U8(EpochByte).U16(index).ToArray());
        }

        public void LampBroken(int index)
        {
            if (host) SendToReady(CoopKind.LampBroken, w.Reset().U8(EpochByte).U16(index).Bool(false).ToArray());
        }

        void SendLampsTo(int slot)
        {
            if (BreakableLamps.I == null) return;
            foreach (var l in BreakableLamps.I.List)
                if (l != null && l.Broken) net.SendToSlot(slot, CoopKind.LampBroken, w.Reset().U8(EpochByte).U16(l.index).Bool(true).ToArray());
        }

        void HostLampHit(int from, ref NetReader r)
        {
            int ep = r.U8(), idx = r.U16();
            if (!r.Ok || ep != EpochByte || !IsReady(from)) return;
            var ls = BreakableLamps.I;
            if (ls == null || idx >= ls.List.Count || ls.List[idx].Broken) return;
            ls.List[idx].Break(true);
            LampBroken(idx);
        }

        void ReceiveLampBroken(ref NetReader r)
        {
            r.U8(); int idx = r.U16(); bool silent = r.Bool();
            if (!r.Ok || BreakableLamps.I == null || idx >= BreakableLamps.I.List.Count) return;
            BreakableLamps.I.List[idx].Break(!silent);
        }
    }
}
