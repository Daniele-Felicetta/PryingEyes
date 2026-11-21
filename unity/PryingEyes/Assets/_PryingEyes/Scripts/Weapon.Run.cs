using Production.Saves;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Le armi in una spedizione sospesa (Characters/RunSave.cs): i due slot, quale e' in mano, il modo di fuoco, la consacrazione e le
    /// munizioni. Un'arma si salva col suo nome (WeaponDef.id, non la posizione nell'elenco: una versione nuova puo' aggiungerne in mezzo).
    /// La ricarica a meta', la fiammata e i sigilli accesi sull'arma non si salvano.</summary>
    public partial class Weapon
    {
        /// <summary>{"current": slot in mano, "slots": [{"id", "mode", "upg", "mag", "reserve"}, ...]}; uno slot vuoto e' un oggetto vuoto.</summary>
        public JsonItem CaptureLoadout()
        {
            var list = JsonItem.Array();
            for (int s = 0; s < 2; s++)
            {
                var slot = slots[s];
                if (slot.idx < 0) { list.Add(JsonItem.Object()); continue; }
                list.Add(JsonItem.Object().Set("id", WeaponDefs.All[slot.idx].id).Set("mode", slot.mode).Set("upg", slot.upg).Set("mag", slot.mag).Set("reserve", slot.reserve));
            }
            return JsonItem.Object().Set("current", cur).Set("slots", list);
        }

        /// <summary>Rimette le armi salvate senza fidarsi del file: un'arma che questa versione non conosce lascia lo slot vuoto, le munizioni stanno
        /// nei limiti dell'arma (con i suoi sigilli: il grimorio va rimesso prima), e se non resta niente di utilizzabile le armi di adesso non si toccano.</summary>
        public void RestoreLoadout(JsonItem saved)
        {
            if (saved == null || !saved.IsObject) return;
            var list = saved.Get("slots");
            if (list == null || !list.IsArray) return;
            var back = new[] { new Slot(), new Slot() };
            for (int s = 0; s < 2 && s < list.Count; s++)
            {
                var entry = list[s];
                if (entry == null || !entry.IsObject) continue;
                int idx = WeaponDefs.IndexOf(entry.Str("id"));
                if (idx < 0) continue;
                var def = WeaponDefs.All[idx];
                bool upgraded = entry.Bool("upg");
                back[s].idx = idx;
                back[s].upg = upgraded;
                back[s].mode = def.modes.Length > 0 ? Mathf.Clamp(entry.Int("mode"), 0, def.modes.Length - 1) : 0;
                back[s].state.SetAmmunition(Mathf.Clamp(entry.Int("mag"), 0, MagFor(def, upgraded)), Mathf.Clamp(entry.Int("reserve"), 0, ReserveMaxFor(def, upgraded)));
            }
            if (back[0].idx < 0 && back[1].idx < 0) return;
            int wanted = Mathf.Clamp(saved.Int("current"), 0, 1);
            if (back[0].idx < 0)                                   // lo slot 0 e' sempre pieno: l'unica arma passa li'
            {
                (back[0], back[1]) = (back[1], back[0]);
                wanted = 0;
            }
            if (back[wanted].idx < 0) wanted = 0;
            slots[0] = back[0]; slots[1] = back[1];
            cur = wanted;
            ResetActionState();
            swapT = 0f; pendingSlot = -1; knifeOut = false; knifeBlend = 0f;
            if (cam != null) BuildViewmodel();
            equipT = Def.drawTime;
            RefreshHud();
        }
    }
}
