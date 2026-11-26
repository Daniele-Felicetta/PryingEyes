using System;
using Production.Saves;

namespace PryingEyes.Characters
{
    /// <summary>La cattedrale di un personaggio come albero JSON: la stessa forma di Cattedrale.Fabbrica.Dati (che la legge con JsonUtility).
    /// Qui si sistema e basta; cosa significa ogni campo lo sa Fabbrica.</summary>
    public static class CathedralJson
    {
        /// <summary>Rimette in ordine cio' che un file rovinato o scritto a mano puo' avere storto, senza toccare quello che non conosce:
        /// le liste ci sono sempre, una voce senza nome o senza quantita' positiva si butta, i contatori non vanno sotto zero.</summary>
        public static void Tidy(JsonItem c)
        {
            if (c == null || !c.IsObject) return;
            foreach (string key in new[] { "banca", "scorte", "spoglie" }) TidyAmounts(c, key);
            foreach (string key in new[] { "opere", "ceri" }) TidyNames(c, key);
            foreach (string key in new[] { "bossVinti", "spedizioni", "estrazioni", "morti", "bossUccisi", "livelliScesi", "ramoMassimo" })
                if (c.Has(key)) c.Set(key, Math.Max(0, c.Int(key)));
            if (c.Has("spoglieRamo")) c.Set("spoglieRamo", Math.Max(-1, c.Int("spoglieRamo", -1)));
            if (c.Has("benedizione") && !c["benedizione"].IsString) c.Set("benedizione", "");
        }

        /// <summary>Scrive nella cattedrale salvata i campi che il gioco conosce ('typed', quelli che JsonUtility ha appena prodotto) senza
        /// toccare gli altri: un campo aggiunto da una versione piu' nuova dello stesso formato sopravvive al salvataggio di una piu' vecchia.</summary>
        public static void Overlay(JsonItem target, JsonItem typed)
        {
            if (target == null || !target.IsObject || typed == null || !typed.IsObject) return;
            foreach (string key in typed.Keys) target.Set(key, typed.Get(key).DeepClone());
        }

        static void TidyAmounts(JsonItem c, string key) => c.Set(key, TidyAmounts(c.Get(key)));

        /// <summary>Una lista di quantita' [{"id": nome, "n": quante}] rimessa in ordine: nuova, senza voci senza nome o senza quantita' positiva.</summary>
        public static JsonItem TidyAmounts(JsonItem list)
        {
            var tidy = JsonItem.Array();
            if (list != null && list.IsArray)
                foreach (var e in list.Items)
                {
                    if (e == null || !e.IsObject) continue;
                    string id = e.Str("id").Trim();
                    long n = e.Long("n");
                    if (id.Length == 0 || n <= 0) continue;
                    tidy.Add(JsonItem.Object().Set("id", id).Set("n", Math.Min(n, int.MaxValue)));
                }
            return tidy;
        }

        static void TidyNames(JsonItem c, string key)
        {
            var old = c.Get(key);
            var tidy = JsonItem.Array();
            if (old != null && old.IsArray)
                foreach (var e in old.Items)
                    if (e != null && e.IsString && e.AsString().Trim().Length > 0) tidy.Add(e.AsString().Trim());
            c.Set(key, tidy);
        }
    }

    /// <summary>Il vecchio salvataggio della cattedrale (<c>cattedrale.json</c>, fino alla 0.8.x: un solo magazzino per tutto il PC) e come
    /// diventa il primo personaggio. E' una migrazione di contenuto oltre che di forma: la storia contenuta nel file (spedizioni, boss,
    /// livelli scesi) vale esperienza nel sistema nuovo, cosi' chi ha gia' giocato non riparte dal livello 1.</summary>
    public static class LegacyCathedral
    {
        public const string FileName = "cattedrale.json";
        /// <summary>Il nome e l'aspetto del personaggio che nasce da quel file (lo slot 0 delle partite di una volta).</summary>
        public const string ImportedName = "Il Custode";
        public static string ImportedLook => CharacterLooks.Ids[0];

        /// <summary>Legge il testo del vecchio file. 'cathedral' e' il suo contenuto, sistemato; 'xp' quanto vale la storia che racconta.</summary>
        public static bool TryRead(string text, out JsonItem cathedral, out long xp, out string error)
        {
            cathedral = null; xp = 0; error = null;
            JsonItem doc;
            try { doc = JsonItem.Parse(text ?? ""); }
            catch (JsonSyntaxException e) { error = e.Message; return false; }
            if (!doc.IsObject) { error = "il file non e' un oggetto"; return false; }
            if (doc.Int("versione", 1) > 1) { error = "il file e' di una versione piu' nuova (" + doc.Int("versione") + ")"; return false; }
            CathedralJson.Tidy(doc);
            cathedral = doc;
            xp = HistoryXp(doc);
            return true;
        }

        /// <summary>Quanta esperienza vale la storia di una cattedrale: le risalite, le morti, i boss e i livelli scesi, ai prezzi di oggi.</summary>
        public static long HistoryXp(JsonItem cathedral)
        {
            if (cathedral == null || !cathedral.IsObject) return 0;
            long xp = (long)cathedral.Int("estrazioni") * Leveling.ReturnXp + (long)cathedral.Int("morti") * Leveling.DeathXp
                + (long)cathedral.Int("bossUccisi") * Leveling.BossXp + (long)cathedral.Int("livelliScesi") * Leveling.DescentXp;
            return Math.Max(0, xp);
        }

        /// <summary>Il personaggio che nasce dal vecchio file.</summary>
        public static CharacterRecord ToCharacter(JsonItem cathedral, long xp, string id, string nowIso, string gameVersion)
        {
            var r = CharacterRecord.Create(id, ImportedName, ImportedLook, nowIso, gameVersion);
            r.origin = CharacterOrigin.LegacyCathedral;
            r.cathedral = cathedral ?? JsonItem.Object();
            r.xp = Math.Min(CharacterRecord.MaxXp, Math.Max(0, xp));
            return r;
        }
    }
}
