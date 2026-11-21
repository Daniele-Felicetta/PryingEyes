using System;
using System.Linq;
using Production.Saves;
using PryingEyes.Characters;

static partial class Program
{
    static void RunChecks()
    {
        var rec = new RunRecord { characterId = "c-1a2b3c4d", savedAt = "2026-11-18T10:00:00Z", savedBy = "0.9.0", branch = 2, floor = 7, seed = -123456, score = 4310, bellUsed = true, revealMaps = true };
        rec.carried.Add(JsonItem.Object().Set("id", "ossa").Set("n", 12));
        rec.carried.Add(JsonItem.Object().Set("id", "reliquie").Set("n", 2));
        rec.state.Set("weapons", JsonItem.Object().Set("current", 1).Set("slots", JsonItem.Array()
            .Add(JsonItem.Object().Set("idx", 0).Set("mode", 0).Set("upg", false).Set("mag", 7).Set("reserve", 30))
            .Add(JsonItem.Object().Set("idx", 5).Set("mode", 1).Set("upg", true).Set("mag", 20).Set("reserve", 90))));
        rec.state.Set("perks", JsonItem.Object().Set("inventory", 4660L).Set("reviveUsed", false));
        rec.extras.ObjectAt("future").Set("x", 1);

        string text = RunSchema.Format.Write(rec.ToJson());
        var read = RunSchema.Format.Read(text);
        var back = RunRecord.FromJson(read.Document);
        Check("RUN_ROUND_TRIP", read.Status == LoadStatus.Ok && back.characterId == rec.characterId && back.branch == 2 && back.floor == 7 && back.seed == -123456 && back.score == 4310 && back.bellUsed && back.revealMaps &&
            JsonItem.DeepEquals(back.carried, rec.carried) && JsonItem.DeepEquals(back.state, rec.state) && JsonItem.DeepEquals(back.extras, rec.extras) && back.Playable,
            text.Replace("\n", " ").Substring(0, 100), "every field, the backpack, the loadout of each system and the extras come back as they were");

        var wrecked = RunRecord.FromJson(JsonItem.Parse("{\"branch\":-4,\"floor\":-9,\"seed\":\"x\",\"score\":-50,\"carried\":[{\"id\":\"ossa\",\"n\":5},{\"id\":\"\",\"n\":3},{\"id\":\"legno\",\"n\":0},7,{\"id\":\"sangue\"}],\"state\":5,\"extras\":[]}"));
        Check("RUN_DAMAGE_READS_AS_SAFE_VALUES", wrecked.branch == 0 && wrecked.floor == 1 && wrecked.seed == 0 && wrecked.score == 0 && wrecked.carried.Count == 1 && wrecked.carried[0].Str("id") == "ossa" &&
            wrecked.state.IsObject && wrecked.state.Count == 0 && wrecked.extras.IsObject, wrecked.ToJson().ToJson(false), "negative numbers go to their floor, the backpack keeps only real entries, a state that is not an object is empty");
        Check("RUN_FLOOR_IS_BOUNDED", RunRecord.FromJson(JsonItem.Parse("{\"floor\":999999}")).floor == RunRecord.MaxFloor && !new RunRecord { floor = 0 }.Playable && !new RunRecord { branch = -1 }.Playable && new RunRecord().Playable,
            "floor 999999, floor 0, branch -1, a new record", "capped, not playable, not playable, playable");
        Check("RUN_NOTHING_READS_AS_A_FIRST_FLOOR", RunRecord.FromJson(null).floor == 1 && RunRecord.FromJson(JsonItem.Array()).floor == 1 && RunRecord.FromJson(JsonItem.Object()).carried.IsArray, "null, an array, an empty object", "a record that starts at the first floor with an empty backpack");

        // one run file per character, in the store the game uses (atomic writes, backups, never overwriting a newer file)
        var files = new FakeFiles();
        var store = new SaveStore(FakeFiles.P("saves", "runs"), RunSchema.Format, files);
        store.Save(rec.characterId, rec.ToJson());
        var loaded = store.Load(rec.characterId);
        Check("RUN_STORE_KEEPS_ONE_FILE_PER_CHARACTER", loaded.Loadable && RunRecord.FromJson(loaded.Document).floor == 7 && store.Ids().Count == 1 && store.Ids()[0] == rec.characterId, string.Join(",", store.Ids()), "one file named after the character, read back");
        files.Data[store.PathOf("c-newer")] = "{\"format\":\"pryingeyes.run\",\"version\":2,\"floor\":3}";
        Check("RUN_FROM_A_NEWER_GAME_IS_NEVER_TOUCHED", store.Load("c-newer").Result.Status == LoadStatus.TooNew && files.Data[store.PathOf("c-newer")].Contains("\"version\":2"), store.Load("c-newer").Result.Status.ToString(), "TooNew, the file as it was");
    }
}
