using PryingEyes.Proc;

namespace PryingEyes
{
    public sealed partial class CoopGame
    {
        int pendingDoor, pendingDoorCost, doorSequence;

        public void RequestRoom(int room)
        {
            if (host || net == null || !Started || pendingDoor != 0 || RoomAccessDoors.I == null) return;
            int price = RoomAccessDoors.I.Access.Price(room);
            var gm = GameManager.I;
            if (price <= 0 || gm.score < price) { gm.hud?.Toast("Punti insufficienti."); return; }
            // Reserve funds before sending. The matching reply settles exactly once, including a rejected race.
            pendingDoor = doorSequence = doorSequence % 65534 + 1;
            pendingDoorCost = price; gm.AddPoints(-price);
            net.SendToHost(CoopKind.BuyRoom, w.Reset().U8(EpochByte).U16(pendingDoor).U8(room).U16(price).ToArray());
        }

        void SettleRoom(int request, bool accepted)
        {
            if (pendingDoor == 0 || request != pendingDoor) return;
            if (!accepted) { GameManager.I.AddPoints(pendingDoorCost); GameManager.I.hud?.Toast("Acquisto annullato: punti restituiti."); }
            pendingDoor = pendingDoorCost = 0;
        }

        void RefundPendingRoom()
        {
            if (pendingDoor != 0) SettleRoom(pendingDoor, false);
        }

        byte[] RoomSnapshot()
        {
            var rooms = RoomAccessDoors.I.Access.OpenRooms;
            w.Reset().U8(EpochByte).U8(rooms.Count);
            foreach (int room in rooms) w.U8(room);
            return w.ToArray();
        }

        public void RoomsChanged()
        {
            if (host && RoomAccessDoors.I != null) SendToReady(CoopKind.RoomsState, RoomSnapshot());
        }

        void HostBuyRoom(int from, ref NetReader r)
        {
            int epoch = r.U8(), request = r.U16(), room = r.U8(), reserved = r.U16();
            if (!r.Ok) return;
            var doors = RoomAccessDoors.I;
            bool accepted = epoch == EpochByte && IsReady(from) && doors != null &&
                reserved == doors.Access.Price(room) && doors.TryBuy(Targets.OfSlot(from), room, reserved, out _);
            if (accepted) RoomsChanged();
            net.SendToSlot(from, CoopKind.RoomReply, w.Reset().U16(request).Bool(accepted).ToArray());
        }

        void ReceiveRooms(ref NetReader r)
        {
            r.U8(); int count = r.U8();
            if (count > 32) return;
            var rooms = new int[count];
            for (int i = 0; i < count; i++) rooms[i] = r.U8();
            if (r.Ok && RoomAccessDoors.I != null) RoomAccessDoors.I.ApplySnapshot(rooms, false);
        }
    }
}
