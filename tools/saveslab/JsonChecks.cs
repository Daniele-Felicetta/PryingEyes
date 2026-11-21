using System;
using System.Linq;
using System.Text;
using Production.Saves;

static partial class Program
{
    static bool Fails(string json, out JsonSyntaxException error)
    {
        try { JsonItem.Parse(json); error = null; return false; }
        catch (JsonSyntaxException e) { error = e; return true; }
    }

    static void JsonChecks()
    {
        var doc = JsonItem.Parse("{\"a\":1,\"b\":-2.5e3,\"c\":\"x\",\"d\":true,\"e\":null,\"f\":[1,2,{}],\"g\":{}}");
        Check("JSON_PARSES_EVERY_KIND", doc.Int("a") == 1 && doc.Num("b") == -2500 && doc.Str("c") == "x" && doc.Bool("d") && doc.Get("e").IsNull &&
            doc.Get("f").IsArray && doc.Get("f").Count == 3 && doc.Get("f")[2].IsObject && doc.Get("g").IsObject && doc.Get("g").Count == 0, doc.ToJson(), "numbers, strings, booleans, null, arrays and objects");
        Check("JSON_KEEPS_FIELD_ORDER", string.Join(",", doc.Keys) == "a,b,c,d,e,f,g" && doc.ToJson().IndexOf("\"a\"") < doc.ToJson().IndexOf("\"g\""), string.Join(",", doc.Keys), "a,b,c,d,e,f,g");

        var big = JsonItem.Parse("{\"seed\":9007199254740993,\"t\":1e3,\"neg\":-0}");
        Check("JSON_NUMBER_KEEPS_ITS_TEXT", big.Long("seed") == 9007199254740993L && big.ToJson().Contains("9007199254740993") && big.Get("t").NumberText == "1e3" && big.Int("t") == 1000,
            big.ToJson(), "an integer above 2^53 comes back exactly, 1e3 keeps its text and reads as 1000");
        Check("JSON_NUMBER_READS_ARE_SAFE", JsonItem.Parse("1.9").AsLong() == 1 && JsonItem.Parse("1e30").AsInt() == int.MaxValue && JsonItem.Parse("-1e30").AsInt() == int.MinValue &&
            JsonItem.Parse("\"7\"").AsInt(5) == 5 && JsonItem.Parse("true").AsString("z") == "z", "1.9, 1e30, text, bool", "truncate, clamp, fallback on a wrong kind");

        string nasty = "quote\" back\\ slash/ nl\n cr\r tab\t bell\u0007 nul\u0001 accent \u00e8 emoji \U0001F600 lone\uD800 end";
        var s = JsonItem.Object().Set("k\"ey", nasty);
        var back = JsonItem.Parse(s.ToJson());
        Check("JSON_STRINGS_ROUND_TRIP", back.Str("k\"ey") == nasty, "escapes, control characters, accents, a surrogate pair and a lone surrogate", "identical after write and read");
        string text = s.ToJson();
        Check("JSON_WRITER_OUTPUT_IS_VALID_UTF8", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)) == text, "lone surrogate written as an escape", "encodes to UTF-8 and decodes back unchanged");
        Check("JSON_READS_UNICODE_ESCAPES", JsonItem.Parse("\"\\u00e8\\uD83D\\uDE00\\u0041\"").AsString() == "\u00e8\U0001F600A", "e-grave, emoji, A", "decoded");

        var complex = JsonItem.Parse("{\"a\":[1,{\"b\":[true,null,\"x\"]},[]],\"c\":{\"d\":{}}}");
        Check("JSON_PRETTY_AND_COMPACT_AGREE", JsonItem.DeepEquals(JsonItem.Parse(complex.ToJson(true)), complex) && JsonItem.DeepEquals(JsonItem.Parse(complex.ToJson(false)), complex) && complex.ToJson(true).Contains("\n"),
            "both forms read back to the same tree", "equal");
        Check("JSON_SKIPS_BYTE_ORDER_MARK", JsonItem.Parse("\uFEFF{\"a\":1}").Int("a") == 1, "BOM then object", "read");
        var dup = JsonItem.Parse("{\"a\":1,\"b\":2,\"a\":3}");
        Check("JSON_DUPLICATE_FIELD_LAST_WINS", dup.Int("a") == 3 && string.Join(",", dup.Keys) == "a,b", dup.ToJson(), "first place, last value");

        string[] bad = { "", "   ", "{\"a\":1,}", "[1,]", "{\"a\" 1}", "01", "1.", ".5", "-", "NaN", "Infinity", "\"abc", "{\"a\":1} x", "{'a':1}", "[1 2]", "{\"a\":\"\\x\"}",
            "\"line\nbreak\"", "tru", "nul", "{\"a\":}", "[", "{", "{\"a\":1", "{1:2}", "{\"\":1}", "\"\\u12\"", "\"\\uZZZZ\"", "1e", "1e+", "--1", "+1", "0x10", "[1,,2]", "{\"a\":1,,\"b\":2}" };
        var notRejected = bad.Where(b => !Fails(b, out _)).ToArray();
        Check("JSON_REJECTS_BAD_TEXT", notRejected.Length == 0, notRejected.Length == 0 ? bad.Length + " malformed texts refused" : "accepted: " + string.Join(" | ", notRejected), "every malformed text refused");
        Fails("{\n  \"a\": 1,\n  \"b\": oops\n}", out var where);
        Check("JSON_ERROR_NAMES_THE_PLACE", where != null && where.Line == 3 && where.Column == 8, where == null ? "no error" : where.Message, "line 3, column 8");

        string deep = new string('[', 100) + new string(']', 100), shallow = new string('[', 60) + new string(']', 60);
        Check("JSON_NESTING_IS_BOUNDED", Fails(deep, out _) && !Fails(shallow, out _), "100 levels refused, 60 read", "a limit of " + JsonItem.MaxDepth);

        var edit = JsonItem.Object().Set("a", 1).Set("b", "two").Set("c", true);
        edit.Set("a", 10);
        Check("JSON_SET_KEEPS_THE_PLACE", string.Join(",", edit.Keys) == "a,b,c" && edit.Int("a") == 10, edit.ToJson(), "a changed in place");
        Check("JSON_SET_DEFAULT_ONLY_WHEN_ABSENT", edit.SetDefault("a", 99).Int("a") == 10 && edit.SetDefault("z", 5).Int("z") == 5, edit.ToJson(), "a kept, z added");
        edit.Rename("b", "beta");
        Check("JSON_RENAME_KEEPS_THE_PLACE", string.Join(",", edit.Keys) == "a,beta,c,z" && edit.Str("beta") == "two" && !edit.Has("b"), edit.ToJson(), "b renamed beta where b was");
        edit.Set("c2", "old"); edit.Rename("c", "c2");
        Check("JSON_RENAME_REPLACES_A_NAMESAKE", edit.Bool("c2") && edit.Keys.Count(k => k == "c2") == 1 && !edit.Has("c"), edit.ToJson(), "the renamed value wins");
        Check("JSON_RENAME_OF_ABSENT_IS_FALSE", !edit.Rename("nothing", "x") && edit.Remove("a") && !edit.Remove("a") && !edit.Has("a"), "absent, then remove twice", "false, true, false");
        var tree = JsonItem.Object();
        tree.ObjectAt("cathedral").ArrayAt("bank").Add(JsonItem.Object().Set("id", "bones").Set("n", 3));
        Check("JSON_PATH_AND_CREATING_PARTS", tree.Path("cathedral", "bank", "0", "id").AsString() == "bones" && tree.Path("cathedral", "nothing", "x") == null && tree.Path("cathedral", "bank", "5") == null &&
            tree.ObjectAt("cathedral") == tree.Get("cathedral") && tree.ArrayAt("cathedral").IsArray, tree.ToJson(), "found when there, null when not; ObjectAt reuses or replaces");
        var walk = JsonItem.Object().Set("a", 1).Set("b", 2).Set("c", 3);
        foreach (string key in walk.Keys) walk.Rename(key, key.ToUpperInvariant());
        Check("JSON_KEYS_ARE_A_COPY", string.Join(",", walk.Keys) == "A,B,C", walk.ToJson(), "renaming while walking the keys is safe");
        var original = JsonItem.Parse("{\"a\":[1,{\"b\":2}]}");
        var clone = original.DeepClone();
        clone.Get("a")[1].Set("b", 99);
        Check("JSON_CLONE_IS_INDEPENDENT", original.Get("a")[1].Int("b") == 2 && clone.Get("a")[1].Int("b") == 99, original.ToJson(), "the original is unchanged");
        Check("JSON_DEEP_EQUALS", JsonItem.DeepEquals(JsonItem.Parse("{\"a\":1,\"b\":[1,2]}"), JsonItem.Parse("{\"b\":[1,2],\"a\":1.0}")) && !JsonItem.DeepEquals(JsonItem.Parse("[1,2]"), JsonItem.Parse("[2,1]")) &&
            !JsonItem.DeepEquals(JsonItem.Parse("{\"a\":1}"), JsonItem.Parse("{\"a\":1,\"b\":2}")), "field order and 1 vs 1.0", "objects ignore order and numbers compare by value, arrays keep theirs");
        Check("JSON_MISSING_AND_MISTYPED_READ_AS_FALLBACK", JsonItem.Parse("{\"a\":\"text\"}").Int("a", 7) == 7 && JsonItem.Parse("{}").Str("a", "d") == "d" && JsonItem.Parse("[1]").Get("a") == null && JsonItem.Parse("5").Count == 0,
            "text read as int, absent field, field of an array", "the fallback, never an exception");
        Reject<ArgumentException>("JSON_REFUSES_NAN", () => JsonItem.From(double.NaN));
        Reject<InvalidOperationException>("JSON_SET_NEEDS_AN_OBJECT", () => JsonItem.Array().Set("a", 1));
        Check("JSON_NUMBER_TEXT_GRAMMAR", new[] { "0", "-0", "12", "1.5", "1e9", "1E-9", "-1.25e+3" }.All(JsonItem.IsNumberText) && !new[] { "", "01", "1.", "+1", "1e", "0x1", " 1" }.Any(JsonItem.IsNumberText) &&
            JsonItem.FromNumberText("1.5") != null && JsonItem.FromNumberText("1,5") == null, "valid and invalid number texts", "the JSON grammar");
    }
}
