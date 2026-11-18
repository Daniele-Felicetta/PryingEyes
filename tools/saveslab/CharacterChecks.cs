using System;
using System.Collections.Generic;
using System.Linq;
using Production.Saves;
using PryingEyes.Characters;

static partial class Program
{
    // What a cattedrale.json of the 0.8.x game looks like, with a few entries a damaged or hand-edited file could hold.
    const string LegacyMessy = "{ \"versione\": 1, \"banca\": [ {\"id\":\"oboli\",\"n\":66}, {\"id\":\"ossa\",\"n\":0}, {\"id\":\"\",\"n\":5}, {\"id\":\"legno\",\"n\":-3}, 5, {\"id\":\"acciaio\",\"n\":3} ]," +
        " \"opere\": [\"volte\", \"\", 7, \"fonte\"], \"scorte\": [ {\"id\":\"rif_mina\",\"n\":2} ], \"bossVinti\": 3, \"spoglieRamo\": -1, \"spoglie\": [], \"benedizione\": \"\", \"ceri\": []," +
        " \"spedizioni\": 9, \"estrazioni\": 4, \"morti\": 2, \"bossUccisi\": 2, \"livelliScesi\": 11, \"ramoMassimo\": 2, \"campoDiUnaVersioneFutura\": 1 }";
    // The exact layout JsonUtility writes (four spaces), as found on a player's disk after one short trip.
    const string LegacyReal = "{\n    \"versione\": 1,\n    \"banca\": [\n        {\n            \"id\": \"oboli\",\n            \"n\": 66\n        }\n    ],\n    \"opere\": [],\n    \"scorte\": [\n        {\n            \"id\": \"rif_acquasanta\",\n            \"n\": 3\n        }\n    ],\n" +
        "    \"bossVinti\": 0,\n    \"spoglieRamo\": -1,\n    \"spoglie\": [],\n    \"benedizione\": \"\",\n    \"ceri\": [],\n    \"spedizioni\": 1,\n    \"estrazioni\": 0,\n    \"morti\": 0,\n    \"bossUccisi\": 0,\n    \"livelliScesi\": 1,\n    \"ramoMassimo\": 0\n}";

    static void CharacterChecks()
    {
        // ---- levels
        bool rises = true, inverse = true;
        for (int level = 2; level <= Leveling.MaxLevel; level++)
        {
            rises &= Leveling.XpToReach(level) > Leveling.XpToReach(level - 1);
            inverse &= Leveling.LevelFor(Leveling.XpToReach(level)) == level && Leveling.LevelFor(Leveling.XpToReach(level) - 1) == level - 1;
        }
        Check("LEVEL_CURVE_RISES", rises && Leveling.XpToReach(1) == 0 && Leveling.XpToReach(2) == 100 && Leveling.XpToReach(3) == 300 && Leveling.XpToReach(10) == 4500, "levels 1, 2, 3, 10", "0, 100, 300, 4500 and always up");
        Check("LEVEL_FOR_INVERTS_THE_CURVE", inverse && Leveling.LevelFor(0) == 1 && Leveling.LevelFor(-5) == 1 && Leveling.LevelFor(99) == 1 && Leveling.LevelFor(100) == 2 && Leveling.LevelFor(long.MaxValue) == Leveling.MaxLevel,
            "at and one below every threshold", "the exact level, 1 for nothing, the cap for everything");
        long previous = 0; int last = 1; bool never = true; var rng = new Random(5);
        for (int i = 0; i < 2000; i++) { previous += rng.Next(0, 400); int now = Leveling.LevelFor(previous); never &= now >= last; last = now; }
        Check("LEVEL_NEVER_GOES_DOWN_WITH_MORE_XP", never, "2000 growing totals", "a level never drops");
        Check("LEVEL_PROGRESS_AND_NEXT", Leveling.Progress(0) == 0 && Math.Abs(Leveling.Progress(200) - 0.5f) < 1e-6 && Leveling.Progress(long.MaxValue) == 1f && Leveling.ToNext(0) == 100 && Leveling.ToNext(250) == 50 && Leveling.ToNext(long.MaxValue) == 0,
            "0, 200, a huge total", "0, half way to level 3, full; 100 to go, 50 to go, none");
        Check("LEVEL_BONUSES_ARE_SMALL_AND_CAPPED", Leveling.StartPointsBonus(1) == 0 && Leveling.StartPointsBonus(2) == 10 && Leveling.StartPointsBonus(26) == 250 && Leveling.StartPointsBonus(99) == 250 &&
            Leveling.HpBonus(1) == 0 && Leveling.HpBonus(2) == 1 && Leveling.HpBonus(26) == 25 && Leveling.HpBonus(99) == 25, "levels 1, 2, 26, 99", "0, +10 and +1, +250 and +25 at the cap");
        Check("XP_AWARDS_RANK_THE_DEEDS", Leveling.KillAward(false, false) == 5 && Leveling.KillAward(true, false) == 8 && Leveling.KillAward(false, true) == 10 && Leveling.KillAward(true, true) == 10 &&
            Leveling.BossXp > Leveling.ReturnXp && Leveling.ReturnXp > Leveling.DescentXp && Leveling.DescentXp > Leveling.KnifeXp && Leveling.DeathXp > 0, "a kill, a headshot, a knife", "5, 8, 10; a boss is worth more than a safe return, more than a level, more than a kill");
        long fullTrip = 90 * Leveling.KillXp + Leveling.BossXp + 3 * Leveling.DescentXp + Leveling.ReturnXp;
        Check("LEVEL_PACE_IS_A_FEW_TRIPS", Leveling.LevelFor(fullTrip) >= 3 && Leveling.LevelFor(10 * fullTrip) >= 8 && Leveling.LevelFor(10 * fullTrip) <= 14, $"{fullTrip} xp for a whole branch; ten branches = level {Leveling.LevelFor(10 * fullTrip)}", "a branch gives the first levels, ten branches a character of level 8 to 14");

        // ---- looks and names
        Check("LOOKS_ARE_THE_FOUR_SURVIVORS", CharacterLooks.Ids.Length == 4 && CharacterLooks.Names.Length == 4 && CharacterLooks.Blurbs.Length == 4 && CharacterLooks.Ids.Distinct().Count() == 4 && CharacterLooks.IndexOf("reporter") == 2 &&
            CharacterLooks.IndexOf("alien") == -1 && CharacterLooks.Valid("alien") == "custode" && CharacterLooks.NameOf("alien") == "Il Custode", string.Join(",", CharacterLooks.Ids), "custode, dottoressa, reporter, cacciatrice, each with a name and a description");
        var taken = new HashSet<string>(); var got = new List<string>();
        for (int i = 0; i < 4; i++) { string look = CharacterLooks.Resolve("custode", taken); got.Add(look); taken.Add(look); }
        Check("LOOKS_ARE_NEVER_DOUBLED_IN_A_GAME", got.Distinct().Count() == 4 && got[0] == "custode" && got[1] == "dottoressa" && CharacterLooks.Resolve("reporter", new HashSet<string> { "custode" }) == "reporter" &&
            CharacterLooks.Resolve("alien", new HashSet<string> { "custode" }) == "dottoressa" && CharacterLooks.Resolve(null, null) == "custode", string.Join(",", got), "four who all want the Custode get four different looks; a free wish is kept");

        string Clean(string raw, out NameProblem problem) => CharacterNames.Clean(raw, out problem);
        NameProblem p;
        bool names = Clean("  Ada  ", out p) == "Ada" && p == NameProblem.None && Clean("Ada   Lovelace", out p) == "Ada Lovelace" && Clean("A\tb\nc", out p) == "A b c" && Clean("\u0001Ada\u0007", out p) == "Ada" &&
            Clean("Élodie", out p) == "Élodie" && p == NameProblem.None && Clean(new string('x', 20), out p) == new string('x', 20) && p == NameProblem.None;
        Check("NAMES_ARE_CLEANED", names, "spaces, tabs, control characters, an accent", "trimmed, single spaces, no control characters, accents kept");
        bool problems = Clean("", out p) == "" && p == NameProblem.Empty && Clean("   \t ", out p) == "" && p == NameProblem.Empty && Clean(null, out p) == "" && p == NameProblem.Empty && Clean("!!! ??", out p) == "!!! ??" && p == NameProblem.NoLetters &&
            Clean(new string('x', 21), out p).Length == 21 && p == NameProblem.TooLong;
        Check("NAMES_SAY_WHY_THEY_FAIL", problems, "empty, blanks, null, symbols only, 21 letters", "Empty, Empty, Empty, NoLetters, TooLong");
        string longEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 12)), cutEmoji = CharacterNames.Tolerant(longEmoji);
        bool noLone = !cutEmoji.Where((c, i) => (char.IsHighSurrogate(c) && !(i + 1 < cutEmoji.Length && char.IsLowSurrogate(cutEmoji[i + 1]))) || (char.IsLowSurrogate(c) && !(i > 0 && char.IsHighSurrogate(cutEmoji[i - 1])))).Any();
        Check("NAMES_FROM_DISK_ARE_ALWAYS_USABLE", CharacterNames.Tolerant("???") == CharacterNames.Fallback && CharacterNames.Tolerant("") == CharacterNames.Fallback && CharacterNames.Tolerant(new string('y', 40)) == new string('y', 20) &&
            cutEmoji.Length <= 20 && cutEmoji.Length > 0 && noLone && CharacterNames.Same("ada", "ADA") && !CharacterNames.Same("ada", "adam"), "symbols, nothing, 40 letters, 12 emoji", "a fallback or a cut that never splits a character; names differ only by case are the same");

        // ---- one name per character
        var existing = new[] { "Il Custode", "ada" };
        Check("NAMES_ARE_UNIQUE_IGNORING_CASE", CharacterNames.Check("  ADA ", existing, out string cleanAda) == NameProblem.Taken && cleanAda == "ADA" && CharacterNames.Check("Bo", existing, out _) == NameProblem.None &&
            CharacterNames.Check("", existing, out _) == NameProblem.Empty && CharacterNames.Check("Il  Custode", existing, out _) == NameProblem.Taken && CharacterNames.Check("Ada", null, out _) == NameProblem.None,
            "Ada against {Il Custode, ada}", "taken, free, empty, taken after cleaning, free with nobody else");
        Check("NAMES_GET_A_FREE_VARIANT", CharacterNames.Unique("Giocatore 2", new string[0]) == "Giocatore 2" && CharacterNames.Unique("Giocatore 2", new[] { "giocatore 2" }) == "Giocatore 2 (2)" &&
            CharacterNames.Unique("Giocatore 2", new[] { "Giocatore 2", "Giocatore 2 (2)" }) == "Giocatore 2 (3)" && CharacterNames.Unique("???", new[] { "Sopravvissuto" }) == "Sopravvissuto (2)",
            "Giocatore 2 taken once and twice, a fallback", "the number counts up and the name stays free");
        string full = new string('n', 20), fullFree = CharacterNames.Unique(full, new[] { full });
        string emoji10 = string.Concat(Enumerable.Repeat("\U0001F600", 10)), emojiFree = CharacterNames.Unique(emoji10, new[] { emoji10 });
        bool emojiClean = !emojiFree.Where((c, i) => (char.IsHighSurrogate(c) && !(i + 1 < emojiFree.Length && char.IsLowSurrogate(emojiFree[i + 1]))) || (char.IsLowSurrogate(c) && !(i > 0 && char.IsHighSurrogate(emojiFree[i - 1])))).Any();
        Check("NAMES_VARIANTS_STAY_SHORT", fullFree.Length <= CharacterNames.MaxLength && fullFree.EndsWith(" (2)") && CharacterNames.Check(fullFree, new[] { full }, out _) == NameProblem.None &&
            emojiFree.Length <= CharacterNames.MaxLength && emojiClean && !CharacterNames.Same(emojiFree, emoji10), fullFree + " / " + emojiFree.Length + " chars", "a twenty-letter name is cut to make room for the number and never splits an emoji");

        // ---- saving the cathedral never loses a field this version does not know
        var onDisk = JsonItem.Parse("{\"versione\":1,\"spedizioni\":3,\"banca\":[{\"id\":\"ossa\",\"n\":5}],\"fieldOfTomorrow\":{\"keep\":[1,2]}}");
        var typedNow = JsonItem.Parse("{\"versione\":1,\"spedizioni\":4,\"banca\":[{\"id\":\"ossa\",\"n\":9}],\"opere\":[\"fonte\"]}");
        CathedralJson.Overlay(onDisk, typedNow);
        Check("CATHEDRAL_SAVE_KEEPS_UNKNOWN_FIELDS", onDisk.Int("spedizioni") == 4 && onDisk.Path("banca", "0", "n").AsInt() == 9 && onDisk.Get("opere").Count == 1 && onDisk.Path("fieldOfTomorrow", "keep").Count == 2 &&
            string.Join(",", onDisk.Keys) == "versione,spedizioni,banca,fieldOfTomorrow,opere", onDisk.ToJson(), "known fields replaced in place, the new one added at the end, the unknown one kept");
        typedNow.Set("spedizioni", 99);
        Check("CATHEDRAL_OVERLAY_COPIES", onDisk.Int("spedizioni") == 4, "typed tree edited after the overlay", "the saved tree does not follow it");

        // ---- the record and its file
        var rec = CharacterRecord.Create("c-1a2b3c4d", "  Ada  ", "dottoressa", "2026-11-18T10:00:00Z", "0.9.0");
        rec.xp = 1234; rec.stats.kills = 50; rec.stats.games = 2;
        rec.cathedral.ArrayAt("banca").Add(JsonItem.Object().Set("id", "oboli").Set("n", 40));
        CathedralJson.Tidy(rec.cathedral);                 // reading a character tidies its cathedral (the lists always exist): start from the tidy form
        rec.extras.ObjectAt("future").Set("x", 1);
        string text = CharacterSchema.Format.Write(rec.ToJson());
        var read = CharacterSchema.Format.Read(text);
        var back = CharacterRecord.FromJson(read.Document);
        Check("RECORD_ROUND_TRIP", read.Status == LoadStatus.Ok && back.id == "c-1a2b3c4d" && back.name == "Ada" && back.look == "dottoressa" && back.xp == 1234 && back.Level == 5 && back.stats.kills == 50 && back.stats.games == 2 &&
            back.createdAt == "2026-11-18T10:00:00Z" && back.createdBy == "0.9.0" && back.origin == CharacterOrigin.New && JsonItem.DeepEquals(back.cathedral, rec.cathedral) && JsonItem.DeepEquals(back.extras, rec.extras),
            text.Replace("\n", " ").Substring(0, Math.Min(120, text.Length)), "every field survives, the cathedral and the extras unread but kept");
        Check("RECORD_LEVEL_IS_WRITTEN_AND_NEVER_TRUSTED", read.Document.Int("level") == 5 && CharacterRecord.FromJson(JsonItem.Parse("{\"xp\":0,\"level\":50}")).Level == 1 && CharacterRecord.FromJson(JsonItem.Parse("{\"xp\":99999999999999999999}")).xp == CharacterRecord.MaxXp,
            "a file that says level 50 with no experience", "level 1: it comes from the experience");
        var junk = CharacterRecord.FromJson(JsonItem.Parse("{\"id\":5,\"name\":\"   \",\"look\":\"alien\",\"xp\":-50,\"stats\":\"no\",\"cathedral\":[1],\"extras\":3,\"level\":77,\"origin\":7}"));
        Check("RECORD_READS_DAMAGE_AS_DEFAULTS", junk.id == "" && junk.name == CharacterNames.Fallback && junk.look == "custode" && junk.xp == 0 && junk.stats.kills == 0 && junk.cathedral.IsObject && junk.extras.IsObject && junk.Level == 1 &&
            CharacterRecord.FromJson(null).name == CharacterNames.Fallback && CharacterRecord.FromJson(JsonItem.Array()).Level == 1, "wrong types everywhere", "a usable character, never an exception");
        var tidy = CharacterRecord.FromJson(JsonItem.Parse("{\"cathedral\":{\"banca\":[{\"id\":\"a\",\"n\":0},{\"id\":\"b\",\"n\":2}],\"morti\":-4,\"spoglieRamo\":-9,\"opere\":[1,\"x\"],\"unknown\":{\"k\":1}}}"));
        Check("RECORD_TIDIES_THE_CATHEDRAL_AND_KEEPS_THE_UNKNOWN", tidy.cathedral.Get("banca").Count == 1 && tidy.cathedral.Int("morti") == 0 && tidy.cathedral.Int("spoglieRamo") == -1 && tidy.cathedral.Get("opere").Count == 1 &&
            tidy.cathedral.Path("unknown", "k").AsInt() == 1 && tidy.cathedral.Get("ceri").IsArray, tidy.cathedral.ToJson(), "empty entries dropped, counters not below zero, a field it does not know untouched");
        var rich = CharacterRecord.Create("c-9", "Bo", "reporter", "", "");
        Check("RECORD_ADD_XP_COUNTS_LEVELS", rich.AddXp(99) == 0 && rich.Level == 1 && rich.AddXp(1) == 1 && rich.Level == 2 && rich.AddXp(5000) >= 3 && rich.AddXp(-10) == 0 && rich.AddXp(0) == 0 && rich.xp >= 5100, "99, then 1, then 5000", "0, 1, 3 or more levels; negatives ignored");
        Check("NEW_IDS_ARE_FILE_NAMES", Enumerable.Range(0, 500).Select(_ => CharacterSchema.NewId()).Distinct().Count() == 500 && SaveStore.ValidId(CharacterSchema.NewId()) && CharacterSchema.NewId().StartsWith("c-") && CharacterSchema.NewId().Length == 10, CharacterSchema.NewId(), "500 different ids, each a valid file name");

        // ---- the format of a character file over time
        Check("CHARACTER_FORMAT_IS_VERSION_ONE", CharacterSchema.Format.Name == "pryingeyes.character" && CharacterSchema.Format.Current == CharacterSchema.Version && CharacterSchema.Format.Oldest == CharacterSchema.Version, $"v{CharacterSchema.Format.Current}, oldest v{CharacterSchema.Format.Oldest}",
            "the chain is empty until a release changes what a file means");
        var tomorrow = new SaveFormat(CharacterSchema.Kind, 2, new Migration(1, "everybody gets a title", d => d.SetDefault("title", "Novizio")));
        var upgradedToday = tomorrow.Read(text);
        Check("TODAYS_FILE_IS_UPGRADED_BY_TOMORROWS_CHAIN", upgradedToday.Status == LoadStatus.Upgraded && upgradedToday.Document.Str("title") == "Novizio" && CharacterRecord.FromJson(upgradedToday.Document).xp == 1234 &&
            JsonItem.DeepEquals(CharacterRecord.FromJson(upgradedToday.Document).cathedral, rec.cathedral), upgradedToday.ToString(), "a file written now gains what the next version adds and loses nothing");
        Check("TOMORROWS_FILE_IS_REFUSED_BY_TODAYS_GAME", CharacterSchema.Format.Read(tomorrow.Write(rec.ToJson())).Status == LoadStatus.TooNew, "a v2 file read by a game that writes v1", "TooNew: opened by nobody, overwritten by nobody");

        // ---- the old cathedral file becomes the first character
        Check("LEGACY_FILE_IS_READ_AND_TIDIED", LegacyCathedral.TryRead(LegacyMessy, out var cathedral, out long legacyXp, out string error) && error == null && cathedral.Get("banca").Count == 2 && cathedral.Path("banca", "0", "id").AsString() == "oboli" &&
            cathedral.Path("banca", "0", "n").AsInt() == 66 && cathedral.Path("banca", "1", "id").AsString() == "acciaio" && string.Join(",", cathedral.Get("opere").Items.Select(i => i.AsString())) == "volte,fonte" && cathedral.Int("bossVinti") == 3 &&
            cathedral.Int("campoDiUnaVersioneFutura") == 1, cathedral?.ToJson() ?? error, "junk entries dropped, real ones and an unknown field kept");
        Check("LEGACY_HISTORY_BECOMES_EXPERIENCE", legacyXp == 4 * Leveling.ReturnXp + 2 * Leveling.DeathXp + 2 * Leveling.BossXp + 11 * Leveling.DescentXp && Leveling.LevelFor(legacyXp) == 5 && legacyXp == 1190, $"{legacyXp} xp, level {Leveling.LevelFor(legacyXp)}",
            "4 returns, 2 deaths, 2 bosses, 11 levels = 1190 xp = level 5: a veteran does not start again");
        Check("LEGACY_REAL_FILE_SHAPE", LegacyCathedral.TryRead(LegacyReal, out var real, out long realXp, out _) && real.Path("banca", "0", "n").AsInt() == 66 && real.Path("scorte", "0", "id").AsString() == "rif_acquasanta" && realXp == Leveling.DescentXp && real.Int("spedizioni") == 1,
            $"{realXp} xp", "a file as JsonUtility writes it reads fine; one level descended is worth one descent");
        var character = LegacyCathedral.ToCharacter(cathedral, legacyXp, "c-00000001", "2026-11-18T10:00:00Z", "0.9.0");
        string legacyFile = CharacterSchema.Format.Write(character.ToJson());
        var legacyStore = new SaveStore("characters", CharacterSchema.Format, new FakeFiles(), () => Noon);
        legacyStore.Save(character.id, character.ToJson());
        var reloaded = CharacterRecord.FromJson(legacyStore.Load(character.id).Document);
        Check("LEGACY_BECOMES_THE_FIRST_CHARACTER", character.name == "Il Custode" && character.look == "custode" && character.origin == CharacterOrigin.LegacyCathedral && character.Level == 5 && reloaded.xp == legacyXp && reloaded.origin == CharacterOrigin.LegacyCathedral &&
            JsonItem.DeepEquals(reloaded.cathedral, cathedral) && legacyFile.Contains("\"origin\": \"legacy-cathedral\""), $"{character.name}, level {character.Level}", "a character named after the first survivor, saved and read back with the whole cathedral");
        bool refusedAll = !LegacyCathedral.TryRead("", out _, out _, out _) && !LegacyCathedral.TryRead("not json", out _, out _, out string e1) && e1 != null && !LegacyCathedral.TryRead("[1]", out _, out _, out _) &&
            !LegacyCathedral.TryRead("{\"versione\":2}", out _, out _, out string e2) && e2.Contains("piu' nuova") && LegacyCathedral.TryRead("{\"versione\":1}", out var empty, out long zero, out _) && zero == 0 && empty.Get("banca").IsArray && empty.Get("ceri").IsArray;
        Check("LEGACY_GARBAGE_AND_NEWER_FILES_ARE_REFUSED", refusedAll, "empty, not JSON, an array, version 2, and an empty v1 file", "the first four refused, the empty v1 file read as an empty cathedral");
        Check("LEGACY_HISTORY_CANNOT_GO_NEGATIVE", LegacyCathedral.HistoryXp(JsonItem.Parse("{\"estrazioni\":-5,\"morti\":-1}")) == 0 && LegacyCathedral.HistoryXp(null) == 0 && LegacyCathedral.HistoryXp(JsonItem.Parse("{\"bossUccisi\":2147483647,\"livelliScesi\":2147483647}")) > 0,
            "negative and enormous counters", "never below zero, never an overflow");
    }
}
