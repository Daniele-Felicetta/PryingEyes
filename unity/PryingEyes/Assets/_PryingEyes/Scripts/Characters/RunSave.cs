using System;
using System.Collections.Generic;
using Production.Economy;
using Production.Saves;
using UnityEngine;

namespace PryingEyes.Characters
{
    /// <summary>L'equipaggiamento di chi gioca dentro una spedizione sospesa (RunRecord.state): un albero con una chiave per sistema (armi, perk,
    /// magie, equipaggiamento da lancio). Ogni sistema scrive e rilegge il suo, e un sistema nuovo aggiunge una chiave senza toccare i file gia'
    /// scritti: chi non trova la sua chiave resta com'e' all'inizio di una spedizione. Lo zaino e' un'altra cosa (RunRecord.carried) e si converte qui.</summary>
    public static class RunSave
    {
        /// <summary>Lo zaino salvato come lista di {"id", "n"}.</summary>
        public static JsonItem FromBundle(Bundle bundle)
        {
            var list = JsonItem.Array();
            if (bundle == null) return list;
            foreach (var e in bundle) list.Add(JsonItem.Object().Set("id", e.Key).Set("n", e.Value));
            return list;
        }

        /// <summary>Lo zaino che un file racconta; un file strano (somme che non entrano in un intero) vale uno zaino vuoto, non un errore.</summary>
        public static Bundle ToBundle(JsonItem carried)
        {
            if (carried == null || !carried.IsArray) return Bundle.Empty;
            var entries = new List<KeyValuePair<string, int>>();
            foreach (var e in carried.Items)
            {
                if (e == null || !e.IsObject) continue;
                string id = e.Str("id").Trim();
                int n = e.Int("n");
                if (id.Length > 0 && n > 0) entries.Add(new KeyValuePair<string, int>(id, n));
            }
            try { return new Bundle(entries); }
            catch (OverflowException) { return Bundle.Empty; }
        }

        /// <summary>Una spedizione sospesa che non si riprende ("Ricomincia dalla cattedrale"): si perde come una abbandonata, cioe' lo zaino conta come
        /// una morte (in parte salvo, il resto nelle spoglie del ramo). Il file va in runs/consumed (Roster.ClearRun).</summary>
        public static void Discard(CharacterRecord owner, RunRecord run)
        {
            if (owner == null || run == null) return;
            Cattedrale.Fabbrica.Seleziona(owner);
            Cattedrale.Fabbrica.Riprendi(run.branch, ToBundle(run.carried), run.bellUsed);
            Cattedrale.Fabbrica.Fallisce(run.score);
            Roster.ClearRun(owner);
        }

        /// <summary>Armi, perk, magie e lanci di questo giocatore, come albero.</summary>
        public static JsonItem CaptureKit(PlayerController player)
        {
            var state = JsonItem.Object();
            if (player == null) return state;
            if (player.weapon != null) state.Set("weapons", player.weapon.CaptureLoadout());
            var perks = player.PerkState.Capture();
            state.Set("perks", JsonItem.Object().Set("inventory", (long)perks.Inventory).Set("reviveUsed", perks.ReviveUsed));
            state.Set("magic", player.Arcana.Capture());
            var equipment = player.GetComponent<Equipment>();
            if (equipment != null) state.Set("equipment", JsonItem.Object().Set("grenades", equipment.grenades).Set("lures", equipment.lures).Set("mines", equipment.mines));
            return state;
        }

        /// <summary>Rimette l'equipaggiamento salvato, sopra quello di inizio spedizione. Ogni parte si controlla da sola: una che non va
        /// (un perk che questa versione non conosce, un'arma tolta) lascia stare le altre.</summary>
        public static void ApplyKit(PlayerController player, JsonItem state)
        {
            if (player == null || state == null || !state.IsObject) return;
            var magic = state.Get("magic");
            if (magic != null) player.Arcana.RestoreFrom(magic);              // prima delle armi: il Caricatore Esteso conta per le munizioni
            var perks = state.Get("perks");
            if (perks != null && perks.IsObject)
            {
                try
                {
                    uint inventory = (uint)Math.Max(0L, Math.Min((long)uint.MaxValue, perks.Long("inventory")));
                    player.PerkState.Restore(new PerkSnapshot { Inventory = inventory, ReviveUsed = perks.Bool("reviveUsed") });
                }
                catch (ArgumentException e) { Debug.LogWarning("[PryingEyes] spedizioni: i perk salvati non valgono (" + e.Message + "), restano quelli di inizio spedizione"); }
            }
            var weapons = state.Get("weapons");
            if (weapons != null && player.weapon != null) player.weapon.RestoreLoadout(weapons);
            var equipment = state.Get("equipment");
            if (equipment != null && equipment.IsObject)
                Equipment.Ensure(player).ResetRun(Mathf.Clamp(equipment.Int("grenades"), 0, Equipment.MaxGrenades),
                    Mathf.Clamp(equipment.Int("lures"), 0, Equipment.MaxLures), Mathf.Clamp(equipment.Int("mines"), 0, Equipment.MaxMines));
            player.OnPerksChanged(true);                                       // la vita massima dipende dai perk
        }
    }
}
