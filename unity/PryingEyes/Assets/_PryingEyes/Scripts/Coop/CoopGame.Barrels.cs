using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Botti d'olio santo in cooperativa (Barrels): il colpo di un ospitato va all'ospite (BarrelHit), l'ospite fa scoppiare
    /// (danni compresi) e lo dice a tutti (BarrelGone); a chi entra tardi manda le botti gia' scoppiate, senza effetti.</summary>
    public sealed partial class CoopGame
    {
        public void BarrelHit(int index)
        {
            if (host || net == null || !Started || !FloorBuilt) return;
            net.SendToHost(CoopKind.BarrelHit, w.Reset().U8(EpochByte).U8(index).ToArray());
        }

        public void BarrelGone(int index)
        {
            if (host) SendToReady(CoopKind.BarrelGone, w.Reset().U8(EpochByte).U8(index).Bool(false).ToArray());
        }

        void SendBarrelsTo(int slot)
        {
            if (Barrels.I == null) return;
            foreach (var b in Barrels.I.List)
                if (b != null && b.Gone) net.SendToSlot(slot, CoopKind.BarrelGone, w.Reset().U8(EpochByte).U8(b.index).Bool(true).ToArray());
        }

        void HostBarrelHit(int from, ref NetReader r)
        {
            int ep = r.U8(), idx = r.U8();
            if (!r.Ok || ep != EpochByte || !IsReady(from)) return;
            var b = Barrels.I;
            if (b != null && idx < b.List.Count) b.List[idx].Trigger(0f);
        }

        void ReceiveBarrelGone(ref NetReader r)
        {
            r.U8(); int idx = r.U8(); bool silent = r.Bool();
            if (!r.Ok || Barrels.I == null || idx >= Barrels.I.List.Count) return;
            var b = Barrels.I.List[idx];
            if (silent) b.Vanish(); else b.Boom(false);
        }
    }
}
