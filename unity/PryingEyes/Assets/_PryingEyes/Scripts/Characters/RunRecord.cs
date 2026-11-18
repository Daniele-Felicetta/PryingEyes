using System;
using Production.Saves;

namespace PryingEyes.Characters
{
    /// <summary>Il formato dei file delle spedizioni sospese (<c>characters/runs/ID.json</c>, uno per personaggio). Si cambia come quello dei
    /// personaggi: alza <see cref="Version"/> e aggiungi un passo (docs/engineering/save-system.md).</summary>
    public static class RunSchema
    {
        public const string Kind = "pryingeyes.run";
        public const int Version = 1;

        public static readonly SaveFormat Format = new SaveFormat(Kind, Version
            // , new Migration(1, "cosa fa il passo, in una riga", doc => { ... })
        );
    }

    /// <summary>Una spedizione sospesa ("Salva e torna al menu"): dove sei (ramo, piano, seme), cosa hai (punti, zaino) e com'eri equipaggiato.
    /// Quello che il personaggio possiede per sempre (cattedrale, esperienza) sta nel suo file e non qui; la spedizione riprende all'inizio del
    /// piano, con questi oggetti. L'equipaggiamento sta in 'state', un albero che questo livello non interpreta: ogni sistema (armi, perk,
    /// magie, equipaggiamento) scrive e rilegge il suo sotto una chiave, e una chiave nuova non cambia i file gia' scritti.</summary>
    public sealed class RunRecord
    {
        public const int MaxFloor = 1000;

        public string characterId = "", savedAt = "", savedBy = "";
        public int branch, floor = 1, seed, score;
        public bool bellUsed, revealMaps;
        /// <summary>Lo zaino: [{"id": risorsa, "n": quanti}].</summary>
        public JsonItem carried = JsonItem.Array();
        public JsonItem state = JsonItem.Object();
        public JsonItem extras = JsonItem.Object();

        /// <summary>Si puo' riprendere: un piano vero in un ramo vero.</summary>
        public bool Playable => floor >= 1 && floor <= MaxFloor && branch >= 0;

        /// <summary>Legge un documento gia' portato alla versione corrente senza fidarsi di niente: un campo mancante torna al suo valore di
        /// partenza, i numeri si riportano nei limiti, lo zaino perde le voci senza nome o senza quantita'.</summary>
        public static RunRecord FromJson(JsonItem doc)
        {
            var r = new RunRecord();
            if (doc == null || !doc.IsObject) return r;
            r.characterId = doc.Str("characterId"); r.savedAt = doc.Str("savedAt"); r.savedBy = doc.Str("savedBy");
            r.branch = Math.Max(0, doc.Int("branch")); r.floor = Math.Min(MaxFloor, Math.Max(1, doc.Int("floor", 1)));
            r.seed = doc.Int("seed"); r.score = Math.Max(0, doc.Int("score"));
            r.bellUsed = doc.Bool("bellUsed"); r.revealMaps = doc.Bool("revealMaps");
            r.carried = CathedralJson.TidyAmounts(doc.Get("carried"));
            var state = doc.Get("state"); r.state = state != null && state.IsObject ? state : JsonItem.Object();
            var extras = doc.Get("extras"); r.extras = extras != null && extras.IsObject ? extras : JsonItem.Object();
            return r;
        }

        public JsonItem ToJson() => JsonItem.Object()
            .Set("characterId", characterId).Set("savedAt", savedAt).Set("savedBy", savedBy)
            .Set("branch", branch).Set("floor", floor).Set("seed", seed).Set("score", score)
            .Set("bellUsed", bellUsed).Set("revealMaps", revealMaps)
            .Set("carried", carried).Set("state", state).Set("extras", extras);
    }
}
