using System;
using System.Collections.Generic;
using System.Linq;
using Production.Saves;

static partial class Program
{
    /// <summary>A format with three versions, the way a real game grows one: v1 had 'gold', v2 turns it into a bank list, v3 adds a title.</summary>
    static SaveFormat ThreeVersions(List<string> log = null) => new SaveFormat("lab.hero", 3,
        new Migration(1, "gold becomes a bank entry", doc => { log?.Add("1"); long gold = doc.Long("gold"); doc.Remove("gold"); doc.ArrayAt("bank").Add(JsonItem.Object().Set("id", "gold").Set("n", gold)); }),
        new Migration(2, "a title for everybody", doc => { log?.Add("2"); doc.SetDefault("title", "Newcomer"); }));

    static string V1Text => "{\"format\":\"lab.hero\",\"version\":1,\"name\":\"Ada\",\"gold\":12,\"future\":{\"keep\":true}}";

    static void FormatChecks()
    {
        var format = ThreeVersions();
        var doc = JsonItem.Object().Set("name", "Ada").Set("x", 1);
        string written = format.Write(doc);
        Check("FORMAT_WRITE_PUTS_FORMAT_AND_VERSION_FIRST", written.StartsWith("{\n  \"format\": \"lab.hero\",\n  \"version\": 3,\n  \"name\": \"Ada\"") && !doc.Has("format") && !doc.Has("version"),
            written.Replace("\n", " "), "format and version first, the argument untouched");
        var read = format.Read(written);
        Check("FORMAT_CURRENT_VERSION_READS_AS_IT_IS", read.Status == LoadStatus.Ok && read.Applied.Count == 0 && read.Document.Str("name") == "Ada" && read.FoundVersion == 3, read.ToString(), "Ok, no step");

        var ran = new List<string>();
        var up = ThreeVersions(ran).Read(V1Text);
        Check("FORMAT_UPGRADES_STEP_BY_STEP", up.Status == LoadStatus.Upgraded && string.Join(",", ran) == "1,2" && up.Document.Int("version") == 3 && up.FoundVersion == 1 && up.Applied.Count == 2 &&
            up.Applied[0].StartsWith("v1 -> v2") && up.Applied[1].StartsWith("v2 -> v3"), up.ToString(), "v1 -> v2 -> v3 in order");
        Check("FORMAT_UPGRADE_KEEPS_AND_ADDS", up.Document.Path("bank", "0", "n").AsInt() == 12 && !up.Document.Has("gold") && up.Document.Str("title") == "Newcomer" && up.Document.Path("future", "keep").AsBool() && up.Document.Str("name") == "Ada",
            up.Document.ToJson(), "gold moved, title added, a field nobody knows survives");
        var ran2 = new List<string>();
        var mid = ThreeVersions(ran2).Read("{\"format\":\"lab.hero\",\"version\":2,\"name\":\"Bo\"}");
        Check("FORMAT_UPGRADES_FROM_THE_MIDDLE", mid.Status == LoadStatus.Upgraded && string.Join(",", ran2) == "2" && mid.Document.Str("title") == "Newcomer", mid.ToString(), "only the second step runs");

        Check("FORMAT_TOO_NEW_IS_REFUSED", format.Read("{\"format\":\"lab.hero\",\"version\":4,\"name\":\"Zed\"}").Status == LoadStatus.TooNew && format.Read("{\"format\":\"lab.hero\",\"version\":4}").Document == null,
            "version 4 for a game that writes 3", "TooNew, no document");
        var late = new SaveFormat("lab.hero", 3, new Migration(2, "only from v2", d => { }));
        Check("FORMAT_TOO_OLD_IS_REFUSED", late.Oldest == 2 && late.Read(V1Text).Status == LoadStatus.TooOld && late.Read("{\"format\":\"lab.hero\",\"version\":2}").Loadable, "v1 against a chain that starts at v2", "TooOld, and v2 still loads");
        Check("FORMAT_OTHER_DOCUMENTS_ARE_REFUSED", format.Read("{\"format\":\"other\",\"version\":1}").Status == LoadStatus.WrongFormat && format.Read("{\"version\":1}").Status == LoadStatus.WrongFormat &&
            format.Read("{\"format\":3,\"version\":1}").Status == LoadStatus.WrongFormat, "another name, no name, a number as the name", "WrongFormat");
        string[] corrupt = { "not json", "", "[1,2]", "5", "null", "{\"format\":\"lab.hero\"}", "{\"format\":\"lab.hero\",\"version\":0}", "{\"format\":\"lab.hero\",\"version\":-1}",
            "{\"format\":\"lab.hero\",\"version\":2.5}", "{\"format\":\"lab.hero\",\"version\":\"2\"}", "{\"format\":\"lab.hero\",\"version\":null}", "{\"format\":\"lab.hero\",\"version\":99999999999}", "{\"format\":\"lab.hero\",\"version\":1" };
        var wrong = corrupt.Where(c => format.Read(c).Status != LoadStatus.Corrupt && format.Read(c).Status != LoadStatus.WrongFormat).ToArray();
        Check("FORMAT_DAMAGE_IS_NEVER_AN_EXCEPTION", wrong.Length == 0 && format.Read(null).Status == LoadStatus.Corrupt, wrong.Length == 0 ? corrupt.Length + " damaged texts answered" : "loaded: " + string.Join(" | ", wrong), "Corrupt or WrongFormat for each");
        Check("FORMAT_BAD_VERSION_IS_CORRUPT", format.Read("{\"format\":\"lab.hero\",\"version\":0}").Status == LoadStatus.Corrupt && format.Read("{\"format\":\"lab.hero\"}").Status == LoadStatus.Corrupt &&
            format.Read("{\"format\":\"lab.hero\",\"version\":2.5}").Status == LoadStatus.Corrupt, "version 0, none, 2.5", "Corrupt");

        var failing = new SaveFormat("lab.hero", 3, new Migration(1, "fine", d => d.Set("a", 1)), new Migration(2, "explodes", d => throw new InvalidOperationException("boom")));
        var failed = failing.Read(V1Text);
        Check("FORMAT_FAILED_STEP_IS_REPORTED", failed.Status == LoadStatus.MigrationFailed && failed.Document == null && failed.Error.Contains("v2 -> v3") && failed.Error.Contains("explodes") && failed.Error.Contains("boom") && failed.Applied.Count == 1,
            failed.ToString(), "MigrationFailed naming the step, no document");

        Reject<ArgumentException>("FORMAT_REJECTS_A_GAP", () => new SaveFormat("x", 4, new Migration(1, "a", d => { }), new Migration(3, "c", d => { })));
        Reject<ArgumentException>("FORMAT_REJECTS_A_MISSING_LAST_STEP", () => new SaveFormat("x", 3, new Migration(1, "a", d => { })));
        Reject<ArgumentException>("FORMAT_REJECTS_TWO_STEPS_FROM_ONE_VERSION", () => new SaveFormat("x", 3, new Migration(1, "a", d => { }), new Migration(1, "b", d => { }), new Migration(2, "c", d => { })));
        Reject<ArgumentException>("FORMAT_REJECTS_A_STEP_PAST_CURRENT", () => new SaveFormat("x", 2, new Migration(1, "a", d => { }), new Migration(2, "b", d => { })));
        Reject<ArgumentException>("FORMAT_REJECTS_VERSION_ZERO", () => new SaveFormat("x", 0));
        Reject<ArgumentException>("FORMAT_REJECTS_A_BLANK_NAME", () => new SaveFormat(" ", 1));
        Reject<ArgumentException>("FORMAT_REJECTS_A_STEP_FROM_ZERO", () => new Migration(0, "a", d => { }));
        Reject<ArgumentException>("FORMAT_REJECTS_A_STEP_WITHOUT_A_NOTE", () => new Migration(1, " ", d => { }));
        Reject<ArgumentNullException>("FORMAT_REJECTS_A_STEP_WITHOUT_A_BODY", () => new Migration(1, "a", null));
        var ordered = new SaveFormat("x", 4, new Migration(3, "c", d => { }), new Migration(1, "a", d => { }), new Migration(2, "b", d => { }));
        Check("FORMAT_STEPS_ARE_SORTED", string.Join(",", ordered.Steps.Select(s => s.From)) == "1,2,3" && ordered.Oldest == 1 && new SaveFormat("x", 5).Oldest == 5, "declared 3,1,2", "1,2,3 and Oldest is the lowest");
        Reject<ArgumentException>("FORMAT_WRITE_NEEDS_AN_OBJECT", () => format.Write(JsonItem.Array()));
        var twice = format.Read(format.Write(up.Document));
        Check("FORMAT_UPGRADED_DOCUMENT_WRITES_AS_CURRENT", twice.Status == LoadStatus.Ok && JsonItem.DeepEquals(twice.Document, up.Document), "write the upgraded document and read it again", "Ok and the same");
    }
}
