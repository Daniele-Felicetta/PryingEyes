using UnityEngine;

namespace PryingEyes
{
    /// <summary>Rianimazione in cooperativa (0.7.2). Chi e' a terra lo dice a tutti nel suo stato (PlayerNet.downed, bleed); chi lo rialza
    /// (PlayerController.ReviveOthersTick, tenendo Usa per tre secondi) chiama ReviveRemote:
    /// - l'ospite manda Revived direttamente a chi e' a terra;
    /// - un ospitato manda ReviveClaim all'ospite, che controlla (chi rialza e' pronto e vicino, l'altro e' a terra) e rialza se stesso
    ///   (slot 0) o gira Revived a chi e' a terra.
    /// Chi riceve Revived si rialza da se' (PlayerController.ReviveBy): vita, tregua e scritte restano sue.</summary>
    public sealed partial class CoopGame
    {
        /// <summary>Quanto lontano puo' stare chi rialza secondo l'ospite (le pose arrivano a ~15 Hz: c'e' margine sul raggio vero).</summary>
        const float ReviveSlack = 4f;

        /// <summary>Qui (ospite o ospitato) ho finito di rialzare il compagno nello slot indicato.</summary>
        public void ReviveRemote(int slot)
        {
            if (net == null || slot == Coop.Slot) return;
            if (host) net.SendToSlot(slot, CoopKind.Revived, w.Reset().U8(Coop.Slot).ToArray());
            else net.SendToHost(CoopKind.ReviveClaim, w.Reset().U8(slot).ToArray());
        }

        /// <summary>Ospite: un ospitato dice di aver rialzato 'target'.</summary>
        void HostReviveClaim(int from, ref NetReader r)
        {
            int target = r.U8();
            if (!r.Ok || target == from || !IsReady(from)) return;
            Vector3 helper = Players.TryGetValue(from, out var rf) && rf != null ? rf.transform.position : Vector3.positiveInfinity;
            if (target == Coop.Slot)
            {
                var pl = GameManager.I != null ? GameManager.I.player : null;
                if (pl == null || !pl.Downed || !Near(helper, pl.transform.position)) return;
                pl.ReviveBy(NameOf(from));
                return;
            }
            if (!Players.TryGetValue(target, out var rt) || rt == null || !rt.last.alive || !rt.last.downed) return;
            if (!Near(helper, rt.transform.position)) return;
            net.SendToSlot(target, CoopKind.Revived, w.Reset().U8(from).ToArray());
        }

        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= ReviveSlack * ReviveSlack;

        /// <summary>Ospitato: un compagno mi ha rialzato.</summary>
        void ReceiveRevived(ref NetReader r)
        {
            int by = r.U8();
            if (!r.Ok) return;
            var pl = GameManager.I != null ? GameManager.I.player : null;
            if (pl != null && pl.Downed) pl.ReviveBy(NameOf(by));
        }
    }
}
