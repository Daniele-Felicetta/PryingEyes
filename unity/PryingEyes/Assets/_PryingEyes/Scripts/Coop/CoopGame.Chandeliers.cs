using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Lampadari che crollano in cooperativa (Proc/Chandeliers.cs): il colpo di un ospitato va all'ospite (ChandelierHit),
    /// l'ospite lo fa cadere (danni compresi, le uccisioni a chi ha sparato) e lo dice a tutti (ChandelierDrop); a chi entra tardi
    /// manda quelli gia' a terra, senza caduta. I lampadari sono gli stessi su ogni macchina (stesso piano, stesso ordine).</summary>
    public sealed partial class CoopGame
    {
        public void ChandelierHit(int index)
        {
            if (host || net == null || !Started || !FloorBuilt) return;
            net.SendToHost(CoopKind.ChandelierHit, w.Reset().U8(EpochByte).U8(index).ToArray());
        }

        public void ChandelierDropped(int index)
        {
            if (host) SendToReady(CoopKind.ChandelierDrop, w.Reset().U8(EpochByte).U8(index).Bool(false).ToArray());
        }

        void SendChandeliersTo(int slot)
        {
            if (Chandeliers.I == null) return;
            foreach (var c in Chandeliers.I.List)
                if (c != null && (c.Fallen || c.Falling)) net.SendToSlot(slot, CoopKind.ChandelierDrop, w.Reset().U8(EpochByte).U8(c.index).Bool(true).ToArray());
        }

        void HostChandelierHit(int from, ref NetReader r)
        {
            int ep = r.U8(), idx = r.U8();
            if (!r.Ok || ep != EpochByte || !IsReady(from)) return;
            var cs = Chandeliers.I;
            if (cs != null && idx < cs.List.Count) cs.List[idx].Drop(from);
        }

        void ReceiveChandelierDrop(ref NetReader r)
        {
            r.U8(); int idx = r.U8(); bool silent = r.Bool();
            if (!r.Ok || Chandeliers.I == null || idx >= Chandeliers.I.List.Count) return;
            Chandeliers.I.List[idx].Remote(silent);
        }
    }
}
