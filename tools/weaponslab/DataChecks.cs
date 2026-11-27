using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Production.Weapons;
using LegacyDef = Legacy.WeaponDef;
using NewDef = Production.Weapons.WeaponDef;

// T23, step 1: the weapon data (WeaponDef, Rgba and the rules of a catalogue) against the frozen copy of what the game had before they moved to the package (LegacyDefs.cs).
// What the lab cannot see is the file format: the reader of the game (Newtonsoft.Json) stays in the game, and the editor proves it there (the content hash and the nine invalid catalogues).
static class DataChecks
{
    const BindingFlags Public = BindingFlags.Instance | BindingFlags.Public;
    internal static int inputs;       // inputs run through the table of the rules, for the report

    public static void Run(JsonElement entries)
    {
        Structure();
        Defaults();
        Statics();
        GradeAndLabels();
        var json = entries.EnumerateArray().ToArray();
        var indices = json.Select(e => e.GetProperty("index").GetInt32()).ToArray();
        var added = json.Select(e => (NewDef)Fill(typeof(NewDef), e.GetProperty("definition"))).ToArray();
        var old = json.Select(e => (LegacyDef)Fill(typeof(LegacyDef), e.GetProperty("definition"))).ToArray();
        RealCatalogue(indices, added, old);
        Table(json, indices, added, old);
        Contract(indices, added);
    }

    // ---- the shape of the record: the columns of the catalogue, in the order its content hash is taken over
    static string Shape(Type t)
    {
        var under = Nullable.GetUnderlyingType(t);
        if (under != null) return Shape(under) + "?";
        if (t.IsArray) return Shape(t.GetElementType()) + "[]";
        if (t == typeof(Legacy.Color) || t == typeof(Rgba)) return "colour";
        return t.Name;
    }

    static void Structure()
    {
        var a = typeof(LegacyDef).GetFields(Public); var b = typeof(NewDef).GetFields(Public);
        var problems = new List<string>();
        if (a.Length != b.Length) problems.Add($"{a.Length} fields before, {b.Length} after");
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i].Name != b[i].Name) problems.Add($"field {i}: {a[i].Name} before, {b[i].Name} after");
            else if (Shape(a[i].FieldType) != Shape(b[i].FieldType)) problems.Add($"{a[i].Name}: {Shape(a[i].FieldType)} before, {Shape(b[i].FieldType)} after");
        }
        Program.Check("WEAPON_DEF_FIELDS_UNCHANGED", "WeaponDef", problems.Count == 0,
            problems.Count == 0 ? $"{a.Length} public fields with the same names, in the same order, of the same types (the colour type aside)" : string.Join("; ", problems),
            "the columns of the catalogue: same names, same order, same types");
        var enums = new List<string>();
        foreach (var pair in new[] { (typeof(Legacy.FireMode), typeof(FireMode)), (typeof(Legacy.ReloadStyle), typeof(ReloadStyle)) })
        {
            var x = Enum.GetNames(pair.Item1); var y = Enum.GetNames(pair.Item2);
            bool same = x.SequenceEqual(y) && x.All(n => (int)Enum.Parse(pair.Item1, n) == (int)Enum.Parse(pair.Item2, n));
            if (!same) enums.Add(pair.Item1.Name);
        }
        Program.Check("WEAPON_ENUMS_UNCHANGED", "FireMode, ReloadStyle", enums.Count == 0, enums.Count == 0 ? "same members, same names, same values" : "differ: " + string.Join(", ", enums),
            "the names the catalogue writes for the fire modes and the reload styles, and the values behind them");
    }

    static bool Same(object a, object b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a is float fa) return b is float fb && BitConverter.SingleToInt32Bits(fa) == BitConverter.SingleToInt32Bits(fb);
        if (a is Legacy.Color ca) return b is Rgba cb && Same(ca.r, cb.r) && Same(ca.g, cb.g) && Same(ca.b, cb.b) && Same(ca.a, cb.a);
        if (a is Array xa)
        {
            if (!(b is Array xb) || xa.Length != xb.Length) return false;
            for (int i = 0; i < xa.Length; i++) if (!Same(xa.GetValue(i), xb.GetValue(i))) return false;
            return true;
        }
        if (a.GetType().IsEnum) return b.GetType().IsEnum && a.ToString() == b.ToString() && Convert.ToInt64(a) == Convert.ToInt64(b);
        return a.Equals(b);
    }

    static void Defaults()
    {
        var o = new LegacyDef(); var n = new NewDef();
        var differing = new List<string>();
        int count = 0;
        foreach (var f in typeof(LegacyDef).GetFields(Public))
        {
            count++;
            if (!Same(f.GetValue(o), typeof(NewDef).GetField(f.Name).GetValue(n))) differing.Add(f.Name);
        }
        Program.Check("WEAPON_DEF_DEFAULTS_UNCHANGED", "WeaponDef", differing.Count == 0,
            differing.Count == 0 ? $"{count} fields, the same default value bit for bit (colours by their four components)" : "differ: " + string.Join(", ", differing),
            "a record that a catalogue leaves a column out of starts as it always did");
    }

    static void Statics()
    {
        bool names = LegacyDef.GradeNames.SequenceEqual(NewDef.GradeNames);
        bool colours = LegacyDef.GradeColors.Length == NewDef.GradeColors.Length;
        for (int i = 0; colours && i < LegacyDef.GradeColors.Length; i++) colours = Same(LegacyDef.GradeColors[i], NewDef.GradeColors[i]);
        Program.Check("WEAPON_GRADE_TABLES_UNCHANGED", "GradeNames, GradeColors", names && colours,
            $"names the same: {names}; {NewDef.GradeColors.Length} colours the same bit for bit: {colours}", "the five grades are called and coloured as before");
    }

    static void GradeAndLabels()
    {
        var problems = new List<string>();
        int runs = 0;
        for (int grade = -5; grade <= 12; grade++)
        {
            var o = new LegacyDef { grade = grade }; var n = new NewDef { grade = grade }; runs++;
            if (o.GradeName != n.GradeName) problems.Add($"grade {grade}: name {o.GradeName} before, {n.GradeName} after");
            if (!Same(o.GradeColor, n.GradeColor)) problems.Add($"grade {grade}: colour differs");
        }
        Program.Check("WEAPON_GRADE_CLAMP_UNCHANGED", "GradeName, GradeColor", problems.Count == 0,
            problems.Count == 0 ? $"{runs} grades from -5 to 12: the same name and the same colour, the ends held at 1 and 5" : string.Join("; ", problems), "a grade outside 1..5 is held to the nearest end");
        var labels = new List<string>();
        int modes = 0;
        for (int mode = -1; mode <= 9; mode++)
        {
            modes++;
            string before = new LegacyDef().ModeLabel((Legacy.FireMode)mode), after = new NewDef().ModeLabel((FireMode)mode);
            if (before != after) labels.Add($"mode {mode}: {before} before, {after} after");
        }
        Program.Check("WEAPON_MODE_LABELS_UNCHANGED", "ModeLabel", labels.Count == 0,
            labels.Count == 0 ? $"{modes} values (the 7 modes and 4 that are none): the same label" : string.Join("; ", labels), "the words the HUD writes for a fire mode");
        inputs += runs + modes;
    }

    // ---- the real catalogue, read by this lab with its own small reader (the game's reader is proved in the editor)
    static object FromJson(Type t, JsonElement v)
    {
        if (t == typeof(float)) return v.GetSingle();
        if (t == typeof(int)) return v.GetInt32();
        if (t == typeof(bool)) return v.GetBoolean();
        if (t == typeof(string)) return v.GetString();
        if (t.IsEnum) return Enum.Parse(t, v.GetString());
        if (t.IsArray)
        {
            var element = t.GetElementType(); var array = Array.CreateInstance(element, v.GetArrayLength()); int i = 0;
            foreach (var item in v.EnumerateArray()) array.SetValue(FromJson(element, item), i++);
            return array;
        }
        var under = Nullable.GetUnderlyingType(t);
        if (under != null) return v.ValueKind == JsonValueKind.Null ? null : FromJson(under, v);
        var c = v.EnumerateArray().Select(x => x.GetSingle()).ToArray();       // a colour: red, green, blue, alpha
        return Activator.CreateInstance(t, c[0], c[1], c[2], c[3]);
    }

    static object Fill(Type type, JsonElement definition)
    {
        object o = Activator.CreateInstance(type);
        foreach (var f in type.GetFields(Public)) f.SetValue(o, FromJson(f.FieldType, definition.GetProperty(f.Name)));
        return o;
    }

    static void RealCatalogue(int[] indices, NewDef[] added, LegacyDef[] old)
    {
        string a = WeaponCatalogRules.Check(indices, added), b = Legacy.LegacyCatalog.Check(indices, old);
        Program.Check("WEAPON_CATALOGUE_IS_VALID", "weapons.json", a == null && b == null, $"{added.Length} weapons; package: {a ?? "valid"}; before: {b ?? "valid"}", "the committed catalogue is valid, as it was");
        inputs++;
    }

    // ---- the rules of a catalogue over a table of inputs: each weapon of the catalogue, hit by each kind of fault in turn, checked by the package and by the old code
    enum Expect { Valid, Id, NonFinite, Stats }

    sealed class Case
    {
        public int k, n; public int[] idx; public NewDef[] added; public LegacyDef[] old;
        public void Set(string field, object value) { typeof(NewDef).GetField(field).SetValue(added[k], value); typeof(LegacyDef).GetField(field).SetValue(old[k], value); }
        public void SetModes(int count)
        {
            typeof(NewDef).GetField("modes").SetValue(added[k], Array.CreateInstance(typeof(FireMode), count));
            typeof(LegacyDef).GetField("modes").SetValue(old[k], Array.CreateInstance(typeof(Legacy.FireMode), count));
        }
    }

    sealed class Fault
    {
        public string kind, name; public Expect expect; public Action<Case> apply;
    }

    static IEnumerable<Fault> Faults()
    {
        Fault F(string kind, string name, Expect expect, Action<Case> apply) => new Fault { kind = kind, name = name, expect = expect, apply = apply };
        foreach (int shift in new[] { 1, -1 }) yield return F("index", "index " + (shift > 0 ? "+1" : "-1"), Expect.Id, c => c.idx[c.k] = c.k + shift);
        yield return F("index", "index -1 absolute", Expect.Id, c => c.idx[c.k] = -1);
        yield return F("index", "index at the top of the range", Expect.Id, c => c.idx[c.k] = int.MaxValue);
        yield return F("definition", "no definition", Expect.Id, c => { c.added[c.k] = null; c.old[c.k] = null; });
        foreach (var id in new object[] { "", "  ", null, "A1", "a", "9x", "a-b", "x y" })
            yield return F("id", "id " + (id == null ? "null" : "'" + id + "'"), Expect.Id, c => c.Set("id", id));
        yield return F("id", "id 'a1' (the shortest valid)", Expect.Valid, c => c.Set("id", "a1"));
        yield return F("id", "id of the next entry", Expect.Id, c => c.Set("id", c.added[(c.k + 1) % c.n].id));
        yield return F("id", "id of the previous entry", Expect.Id, c => c.Set("id", c.added[(c.k + c.n - 1) % c.n].id));
        foreach (var field in typeof(NewDef).GetFields(Public).Where(f => f.FieldType == typeof(float)).Select(f => f.Name))
            foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                yield return F("float", field + " = " + value, Expect.NonFinite, c => c.Set(field, value));
        yield return F("stats", "modes null", Expect.Stats, c => c.Set("modes", null));
        yield return F("stats", "modes empty", Expect.Stats, c => c.SetModes(0));
        foreach (int mag in new[] { 0, -3 }) yield return F("stats", "mag " + mag, Expect.Stats, c => c.Set("mag", mag));
        yield return F("stats", "mag 1 (the smallest valid)", Expect.Valid, c => c.Set("mag", 1));
        yield return F("stats", "reserve -1", Expect.Stats, c => c.Set("reserve", -1));
        yield return F("stats", "reserve 0 and reserveMax 0", Expect.Valid, c => { c.Set("reserve", 0); c.Set("reserveMax", 0); });
        yield return F("stats", "reserveMax below reserve", Expect.Stats, c => c.Set("reserveMax", c.added[c.k].reserve - 1));
        foreach (float damage in new[] { 0f, -5f }) yield return F("stats", "damage " + damage, Expect.Stats, c => c.Set("damage", damage));
        yield return F("stats", "damage 0.0001 (valid)", Expect.Valid, c => c.Set("damage", 0.0001f));
        foreach (float rpm in new[] { 0f, -1f }) yield return F("stats", "rpm " + rpm, Expect.Stats, c => c.Set("rpm", rpm));
        yield return F("stats", "reload 0", Expect.Stats, c => c.Set("reload", 0f));
        yield return F("stats", "reloadEmpty 0", Expect.Stats, c => c.Set("reloadEmpty", 0f));
        foreach (int grade in new[] { 0, 6 }) yield return F("stats", "grade " + grade, Expect.Stats, c => c.Set("grade", grade));
        foreach (int grade in new[] { 1, 5 }) yield return F("stats", "grade " + grade + " (valid)", Expect.Valid, c => c.Set("grade", grade));
        yield return F("stats", "price -1", Expect.Stats, c => c.Set("price", -1));
        yield return F("stats", "price 0 (valid)", Expect.Valid, c => c.Set("price", 0));
        // which problem is reported when there are two: the order of the checks is part of the text a person reads
        yield return F("order", "a non-finite number and damage 0: the number first", Expect.NonFinite, c => { c.Set("hip", float.NaN); c.Set("damage", 0f); });
        yield return F("order", "a bad id and a non-finite number: the id first", Expect.Id, c => { c.Set("id", "A1"); c.Set("hip", float.NaN); });
        yield return F("order", "two non-finite numbers: the one declared first", Expect.NonFinite, c => { c.Set("adsFov", float.PositiveInfinity); c.Set("damage", float.NaN); });
        yield return F("order", "bad stats here and a bad id in the next entry: here first", Expect.Stats, c => { c.Set("mag", 0); if (c.k + 1 < c.n) c.idx[c.k + 1] = -7; });
        yield return F("order", "a non-finite number here and a bad index in the next entry: here first", Expect.NonFinite, c => { c.Set("recV", float.NaN); if (c.k + 1 < c.n) c.idx[c.k + 1] = -7; });
        yield return F("order", "a bad index here and bad stats in the next entry: here first", Expect.Id, c => { c.idx[c.k] = -7; if (c.k + 1 < c.n) c.old[c.k + 1].mag = c.added[c.k + 1].mag = 0; });
    }

    static string Code(string message) => message == null ? "valid" : message.Substring(0, message.IndexOf(':'));

    sealed class Tally
    {
        public int runs, differ, unexpected;
        public readonly Dictionary<string, int> codes = new Dictionary<string, int>();
    }

    static void Table(JsonElement[] json, int[] indices, NewDef[] added, LegacyDef[] old)
    {
        var faults = Faults().ToArray();
        var byKind = new Dictionary<string, Tally>();
        var first = new Dictionary<string, string>();
        foreach (var fault in faults)
        {
            for (int k = 0; k < json.Length; k++)
            {
                var c = new Case { k = k, n = json.Length, idx = (int[])indices.Clone(), added = (NewDef[])added.Clone(), old = (LegacyDef[])old.Clone() };
                c.added[k] = (NewDef)Fill(typeof(NewDef), json[k].GetProperty("definition")); c.old[k] = (LegacyDef)Fill(typeof(LegacyDef), json[k].GetProperty("definition"));
                if (fault.kind == "order" && k + 1 < json.Length)
                {       // the next entry is written too in a few of the cases: it gets a fresh copy of its own
                    c.added[k + 1] = (NewDef)Fill(typeof(NewDef), json[k + 1].GetProperty("definition")); c.old[k + 1] = (LegacyDef)Fill(typeof(LegacyDef), json[k + 1].GetProperty("definition"));
                }
                fault.apply(c);
                string a = WeaponCatalogRules.Check(c.idx, c.added), b = Legacy.LegacyCatalog.Check(c.idx, c.old);
                string want = fault.expect == Expect.Valid ? "valid" : fault.expect == Expect.Id ? "WEAPON_ID_OR_INDEX" : fault.expect == Expect.NonFinite ? "WEAPON_NONFINITE" : "WEAPON_STATS_INVALID";
                if (!byKind.TryGetValue(fault.kind, out var row)) byKind[fault.kind] = row = new Tally();
                row.runs++;
                if (a != b) row.differ++;
                if (Code(a) != want) row.unexpected++;
                row.codes[Code(a)] = row.codes.TryGetValue(Code(a), out int number) ? number + 1 : 1;
                if ((a != b || Code(a) != want) && !first.ContainsKey(fault.kind))
                    first[fault.kind] = $"{fault.name} on entry {k}: the package says '{a ?? "valid"}', the old code says '{b ?? "valid"}', expected {want}";
                inputs++;
            }
        }
        foreach (var kind in new[] { "index", "definition", "id", "float", "stats", "order" })
        {
            var row = byKind[kind];
            string codes = string.Join(", ", row.codes.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}"));
            bool ok = row.differ == 0 && row.unexpected == 0;
            Program.Check("WEAPON_RULES_EQUAL_LEGACY_" + kind.ToUpperInvariant(), "WeaponCatalogRules.Check", ok,
                ok ? $"{row.runs} inputs ({json.Length} weapons x {row.runs / json.Length} faults of this kind): the same text as the old code in every one, and the text the fault calls for ({codes})" : first[kind],
                "the same message, or none, as the old checks, and the one the fault calls for");
        }
    }

    static void Contract(int[] indices, NewDef[] added)
    {
        int rejected = 0;
        foreach (Action invalid in new Action[] { () => WeaponCatalogRules.Check(null, added), () => WeaponCatalogRules.Check(indices, null), () => WeaponCatalogRules.Check(new int[0], added) })
            try { invalid(); } catch (ArgumentException) { rejected++; }
        Program.Check("WEAPON_RULES_REJECT_MISMATCHED_ARGUMENTS", "WeaponCatalogRules.Check", rejected == 3, rejected.ToString(), "3 mismatched calls rejected: no list, no indices, a different number of each");
        inputs += 3;
    }
}
