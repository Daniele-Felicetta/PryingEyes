using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Magie, stati dei nemici e annessi in cooperativa (dal 2026-11-16).
    ///
    /// Magie e sigilli: chi lancia manda agli altri cosa ha lanciato (SpellCast all'ospite, che lo gira come SpellFx a tutti gli altri);
    /// chi lo riceve lo vede partire dal pupazzo di chi lancia (MagicReplay), senza danni: i danni passano gia' da HitClaim.
    /// Stati (fuoco, sangue, acido, stordito, lento, freddo): li tiene l'ospite; un ospitato li chiede per i suoi pupazzi (StatusClaim) e
    /// l'ospite li racconta a tutti dentro lo stato dei nemici (EnemyNet.status).
    /// Annessi: cancelli e leve sono di tutti; un ospitato manda all'ospite cosa ha fatto (AnnexUse), l'ospite lo applica e lo racconta
    /// (AnnexState), anche a chi entra tardi (gli annessi gia' aperti).</summary>
    public sealed partial class CoopGame
    {
        const int MaxSpellTargets = 8;
        static readonly List<int> spellIds = new List<int>();
        static readonly List<Enemy> spellTargets = new List<Enemy>();

        // ------------------------------------------------------------------ magie e sigilli (solo grafica per gli altri)
        /// <summary>Chi lancia (ospite o ospitato): la magia o il sigillo e' partito ('to' = la mira, 'targets' = chi ha colpito, in ordine).</summary>
        public void SpellCast(int kind, int node, Vector3 from, Vector3 to, IList<Enemy> targets, int weaponSlot)
        {
            if (net == null || !FloorBuilt) return;
            if (host)
            {
                if (!AnyReady()) return;
                w.Reset().U8(EpochByte).U8(0);
                WriteSpell(kind, node, from, to, targets, weaponSlot);
                SendToReady(CoopKind.SpellFx, w.ToArray());
            }
            else
            {
                w.Reset().U8(EpochByte);
                WriteSpell(kind, node, from, to, targets, weaponSlot);
                net.SendToHost(CoopKind.SpellCast, w.ToArray());
            }
        }

        void WriteSpell(int kind, int node, Vector3 from, Vector3 to, IList<Enemy> targets, int weaponSlot)
        {
            w.U8(kind & 0xFF).U8(node & 0xFF).V3(from).V3(to).U8(Mathf.Clamp(weaponSlot, 0, 255));
            int n = 0;
            if (targets != null) foreach (var e in targets) if (e != null && e.netId >= 0 && n < MaxSpellTargets) n++;
            w.U8(n);
            int k = 0;
            if (targets != null)
                foreach (var e in targets)
                {
                    if (k >= n) break;
                    if (e != null && e.netId >= 0) { w.U16(e.netId); k++; }
                }
        }

        /// <summary>I nemici (ospite) o i pupazzi (ospitato) con quegli id di rete, quelli che ci sono ancora.</summary>
        List<Enemy> SpellTargets(List<int> ids)
        {
            spellTargets.Clear();
            foreach (int id in ids) { var e = EnemyById(id); if (e != null) spellTargets.Add(e); }
            return spellTargets;
        }

        static bool ReadSpell(ref NetReader r, out int kind, out int node, out Vector3 from, out Vector3 to, out int weaponSlot)
        {
            kind = r.U8(); node = r.U8(); from = r.V3(); to = r.V3(); weaponSlot = r.U8();
            int n = r.U8();
            spellIds.Clear();
            for (int i = 0; i < n && i < MaxSpellTargets; i++) spellIds.Add(r.U16());
            return r.Ok;
        }

        /// <summary>Ospite: la magia di un ospitato. La vedo io e la giro agli altri ospitati pronti.</summary>
        void HostSpellCast(int from, ref NetReader r)
        {
            int ep = r.U8();
            if (!ReadSpell(ref r, out int kind, out int node, out Vector3 a, out Vector3 b, out int ws)) return;
            if (ep != EpochByte || !IsReady(from)) return;
            MagicReplay.Play(from, kind, node, a, b, SpellTargets(spellIds), ws);
            w.Reset().U8(EpochByte).U8(from).U8(kind).U8(node).V3(a).V3(b).U8(ws).U8(spellIds.Count);
            foreach (int id in spellIds) w.U16(id);
            var data = w.ToArray();
            foreach (int s in net.RemoteSlots) if (s != from && IsReady(s)) net.SendToSlot(s, CoopKind.SpellFx, data, true);
        }

        /// <summary>Ospitato: la magia di un altro giocatore (l'ospite o un altro ospitato).</summary>
        void ReceiveSpellFx(ref NetReader r)
        {
            r.U8();                                  // epoca: l'ha gia' controllata Defer
            int caster = r.U8();
            if (!ReadSpell(ref r, out int kind, out int node, out Vector3 a, out Vector3 b, out int ws)) return;
            if (caster == Coop.Slot) return;
            MagicReplay.Play(caster, kind, node, a, b, SpellTargets(spellIds), ws);
        }

        // ------------------------------------------------------------------ stati dei nemici
        /// <summary>Ospitato: uno stato messo su un pupazzo (EnemyStatus), da applicare sul nemico vero dell'ospite.</summary>
        public void SendStatusClaim(Enemy e, int kind, float a, float b, float c)
        {
            if (host || net == null || !FloorBuilt || e == null || e.netId < 0) return;
            net.SendToHost(CoopKind.StatusClaim, w.Reset().U8(EpochByte).U16(e.netId).U8(kind).F32(a).F32(b).F32(c).ToArray());
        }

        void HostStatusClaim(int from, ref NetReader r)
        {
            int ep = r.U8(), id = r.U16(), kind = r.U8();
            float a = r.F32(), b = r.F32(), c = r.F32();
            if (!r.Ok || ep != EpochByte || !IsReady(from)) return;
            var e = EnemyById(id);
            if (e == null || e.IsDead) return;
            EnemyStatus.ApplyClaim(e, from, kind, a, b, c);
        }

        // ------------------------------------------------------------------ annessi
        static List<AnnexSite> AnnexSites
        {
            get { var fd = FloorDirector.I; return fd != null && fd.cur != null ? fd.cur.annexSites : null; }
        }

        static AnnexSite AnnexAt(int idx)
        {
            var list = AnnexSites;
            return list != null && idx >= 0 && idx < list.Count ? list[idx] : null;
        }

        /// <summary>Qui un annesso si e' aperto (what 255) o una sua leva e' stata tirata (what = indice della leva). L'ospite lo racconta a
        /// tutti, un ospitato lo dice all'ospite.</summary>
        public void AnnexChanged(AnnexSite site, int what)
        {
            if (net == null || !FloorBuilt || site == null) return;
            var list = AnnexSites;
            int idx = list != null ? list.IndexOf(site) : -1;
            if (idx < 0 || idx > 254) return;
            if (host) { if (AnyReady()) SendToReady(CoopKind.AnnexState, w.Reset().U8(EpochByte).U8(idx).U8(what & 0xFF).Bool(false).ToArray()); }
            else net.SendToHost(CoopKind.AnnexUse, w.Reset().U8(EpochByte).U8(idx).U8(what & 0xFF).ToArray());
        }

        void HostAnnexUse(int from, ref NetReader r)
        {
            int ep = r.U8(), idx = r.U8(), what = r.U8();
            if (!r.Ok || ep != EpochByte || !IsReady(from)) return;
            var site = AnnexAt(idx);
            if (site == null || !site.ApplyRemote(what, false)) return;
            if (what == 255) return;                 // l'apertura l'ha gia' raccontata a tutti AnnexSite.Open
            var data = w.Reset().U8(EpochByte).U8(idx).U8(what).Bool(false).ToArray();
            foreach (int s in net.RemoteSlots) if (s != from && IsReady(s)) net.SendToSlot(s, CoopKind.AnnexState, data, true);
        }

        void ReceiveAnnexState(ref NetReader r)
        {
            r.U8();
            int idx = r.U8(), what = r.U8();
            bool instant = r.Bool();
            if (!r.Ok) return;
            AnnexAt(idx)?.ApplyRemote(what, instant);
        }

        /// <summary>Ospite: a chi entra (o finisce di costruire il piano) gli annessi gia' aperti, senza animazione.</summary>
        void SendAnnexesTo(int slot)
        {
            var list = AnnexSites;
            if (list == null) return;
            for (int i = 0; i < list.Count && i < 255; i++)
                if (list[i] != null && list[i].opened)
                    net.SendToSlot(slot, CoopKind.AnnexState, w.Reset().U8(EpochByte).U8(i).U8(255).Bool(true).ToArray());
        }
    }
}
