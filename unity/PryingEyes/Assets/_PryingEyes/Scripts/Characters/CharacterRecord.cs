using System;
using System.Collections.Generic;
using System.Text;
using Production.Saves;

namespace PryingEyes.Characters
{
    public enum NameProblem { None, Empty, TooLong, NoLetters, Taken }

    /// <summary>Il nome di un personaggio: da 1 a 20 caratteri con almeno una lettera o una cifra, spazi singoli, niente caratteri di controllo.</summary>
    public static class CharacterNames
    {
        public const int MaxLength = 20;
        public const string Fallback = "Sopravvissuto";

        /// <summary>Toglie gli spazi ai bordi, riduce ogni serie di spazi (e a capo, tabulazioni) a uno e butta i caratteri di controllo. Il
        /// risultato puo' essere ancora troppo lungo o vuoto: 'problem' lo dice.</summary>
        public static string Clean(string raw, out NameProblem problem)
        {
            var sb = new StringBuilder();
            bool pendingSpace = false, letters = false;
            foreach (char c in raw ?? "")
            {
                if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
                if (char.IsControl(c)) continue;
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(c);
                if (char.IsLetterOrDigit(c)) letters = true;
            }
            string clean = sb.ToString();
            problem = clean.Length == 0 ? NameProblem.Empty : clean.Length > MaxLength ? NameProblem.TooLong : !letters ? NameProblem.NoLetters : NameProblem.None;
            return clean;
        }

        /// <summary>Per un file letto da disco: sempre un nome usabile (troppo lungo si accorcia senza spezzare una coppia di surrogati, vuoto diventa Fallback).</summary>
        public static string Tolerant(string raw)
        {
            string clean = Clean(raw, out var problem);
            if (problem == NameProblem.Empty || problem == NameProblem.NoLetters) return Fallback;
            if (clean.Length > MaxLength)
            {
                int cut = MaxLength;
                if (char.IsHighSurrogate(clean[cut - 1])) cut--;
                clean = clean.Substring(0, cut).TrimEnd();
            }
            return clean;
        }

        public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>Il nome va bene per un personaggio nuovo? 'taken' sono i nomi che ci sono gia' (maiuscole e minuscole non contano).
        /// 'clean' e' il nome ripulito, da usare se il risultato e' None.</summary>
        public static NameProblem Check(string raw, IEnumerable<string> taken, out string clean)
        {
            clean = Clean(raw, out var problem);
            if (problem != NameProblem.None) return problem;
            if (taken != null) foreach (string t in taken) if (Same(t, clean)) return NameProblem.Taken;
            return NameProblem.None;
        }

        /// <summary>Un nome libero che parte da 'baseName' ("Giocatore 2", poi "Giocatore 2 (2)", "Giocatore 2 (3)"...). Sta sempre nei venti caratteri.</summary>
        public static string Unique(string baseName, IEnumerable<string> taken)
        {
            var list = taken == null ? new List<string>() : new List<string>(taken);
            string name = Tolerant(baseName);
            if (!list.Exists(t => Same(t, name))) return name;
            for (int n = 2; n < 1000; n++)
            {
                string suffix = " (" + n + ")";
                string head = name;
                if (head.Length + suffix.Length > MaxLength)
                {
                    head = head.Substring(0, MaxLength - suffix.Length);
                    if (head.Length > 0 && char.IsHighSurrogate(head[head.Length - 1])) head = head.Substring(0, head.Length - 1);
                    head = head.TrimEnd();
                }
                string candidate = head + suffix;
                if (!list.Exists(t => Same(t, candidate))) return candidate;
            }
            return name;
        }
    }

    /// <summary>I conti di un personaggio, per il suo diario (e per chi li vorra' usare).</summary>
    public sealed class CharacterStats
    {
        public long kills, headshots, playSeconds;
        public int games, coopGames, splitGames;

        public static CharacterStats FromJson(JsonItem o)
        {
            var s = new CharacterStats();
            if (o == null || !o.IsObject) return s;
            s.kills = Math.Max(0, o.Long("kills")); s.headshots = Math.Max(0, o.Long("headshots")); s.playSeconds = Math.Max(0, o.Long("playSeconds"));
            s.games = Math.Max(0, o.Int("games")); s.coopGames = Math.Max(0, o.Int("coopGames")); s.splitGames = Math.Max(0, o.Int("splitGames"));
            return s;
        }

        public JsonItem ToJson() => JsonItem.Object().Set("kills", kills).Set("headshots", headshots).Set("playSeconds", playSeconds)
            .Set("games", games).Set("coopGames", coopGames).Set("splitGames", splitGames);
    }

    public static class CharacterOrigin
    {
        public const string New = "new", LegacyCathedral = "legacy-cathedral", Test = "test";
    }

    /// <summary>Il formato dei file dei personaggi (<c>characters/ID.json</c>) e come si rifanno le versioni vecchie. Per cambiare cio' che un
    /// file significa: alza <see cref="Version"/> e aggiungi UN passo da quella di prima (docs/engineering/save-system.md ha la ricetta e
    /// l'elenco di cosa e' gia' passato di qui).</summary>
    public static class CharacterSchema
    {
        public const string Kind = "pryingeyes.character";
        public const int Version = 1;

        public static readonly SaveFormat Format = new SaveFormat(Kind, Version
            // , new Migration(1, "cosa fa il passo, in una riga", doc => { ... })
        );

        /// <summary>Un identificativo nuovo ("c-" e otto cifre esadecimali): e' anche il nome del file.</summary>
        public static string NewId() => "c-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    /// <summary>Un personaggio com'e' in memoria. Il file e' il suo <see cref="ToJson"/> con formato e versione davanti. Cattedrale e extras
    /// restano alberi JSON che questo livello non interpreta: la cattedrale la capisce Fabbrica, ogni sistema futuro mette il suo sotto
    /// una chiave di 'extras' e i file gia' scritti non cambiano.</summary>
    public sealed class CharacterRecord
    {
        public const long MaxXp = 1_000_000_000_000L;

        public string id = "", name = CharacterNames.Fallback, look = CharacterLooks.Ids[0];
        public string createdAt = "", createdBy = "", savedAt = "", savedBy = "", origin = CharacterOrigin.New;
        public long xp;
        public CharacterStats stats = new CharacterStats();
        public JsonItem cathedral = JsonItem.Object();
        public JsonItem extras = JsonItem.Object();

        /// <summary>Si ricava sempre dall'esperienza (Leveling): non si salva come dato di partenza.</summary>
        public int Level => Leveling.LevelFor(xp);
        public string LookName => CharacterLooks.NameOf(look);

        /// <summary>Aggiunge esperienza; restituisce quanti livelli ha fatto salire.</summary>
        public int AddXp(long amount)
        {
            if (amount <= 0) return 0;
            int before = Level;
            xp = Math.Min(MaxXp, xp + amount);
            return Level - before;
        }

        public static CharacterRecord Create(string id, string name, string look, string nowIso, string gameVersion)
        {
            return new CharacterRecord
            {
                id = id, name = CharacterNames.Tolerant(name), look = CharacterLooks.Valid(look),
                createdAt = nowIso ?? "", createdBy = gameVersion ?? "", savedAt = nowIso ?? "", savedBy = gameVersion ?? "",
            };
        }

        /// <summary>Legge un documento gia' portato alla versione corrente. Non si fida di niente: un campo mancante o di un altro tipo
        /// torna al suo valore di partenza e quello che non ha senso viene corretto (livello ricalcolato, nome pulito, aspetto valido).</summary>
        public static CharacterRecord FromJson(JsonItem doc)
        {
            var r = new CharacterRecord();
            if (doc == null || !doc.IsObject) return r;
            r.id = doc.Str("id");
            r.name = CharacterNames.Tolerant(doc.Str("name"));
            r.look = CharacterLooks.Valid(doc.Str("look"));
            r.createdAt = doc.Str("createdAt"); r.createdBy = doc.Str("createdBy");
            r.savedAt = doc.Str("savedAt"); r.savedBy = doc.Str("savedBy");
            r.origin = doc.Str("origin", CharacterOrigin.New);
            r.xp = Math.Min(MaxXp, Math.Max(0, doc.Long("xp")));
            r.stats = CharacterStats.FromJson(doc.Get("stats"));
            var cathedral = doc.Get("cathedral");
            r.cathedral = cathedral != null && cathedral.IsObject ? cathedral : JsonItem.Object();
            CathedralJson.Tidy(r.cathedral);
            var extras = doc.Get("extras");
            r.extras = extras != null && extras.IsObject ? extras : JsonItem.Object();
            return r;
        }

        /// <summary>Il corpo del documento (la versione e il formato li mette SaveFormat). 'level' e' scritto per chi legge il file e non si rilegge mai.</summary>
        public JsonItem ToJson() => JsonItem.Object()
            .Set("id", id).Set("name", name).Set("look", look)
            .Set("createdAt", createdAt).Set("createdBy", createdBy).Set("savedAt", savedAt).Set("savedBy", savedBy).Set("origin", origin)
            .Set("xp", xp).Set("level", Level)
            .Set("stats", stats.ToJson()).Set("cathedral", cathedral).Set("extras", extras);
    }
}
