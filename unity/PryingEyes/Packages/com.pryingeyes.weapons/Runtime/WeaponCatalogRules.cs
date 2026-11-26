using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Production.Weapons
{
    /// <summary>What a catalogue of weapons must satisfy once its records have been read, whatever the file format they were read from: identifiers and indices that only ever grow,
    /// numbers that are numbers, and statistics that make a weapon that can be fired and reloaded. Reading the file and checking its shape (which columns exist, of which type)
    /// stay with the reader, because they speak the file's own format.</summary>
    public static class WeaponCatalogRules
    {
        static readonly FieldInfo[] Fields = typeof(WeaponDef).GetFields(BindingFlags.Instance | BindingFlags.Public);

        /// <summary>The first problem of a catalogue, or null when it has none. <paramref name="indices"/> holds the index each record declares and <paramref name="definitions"/> the
        /// records, one for one, in file order. A problem is a code and its words (<c>WEAPON_ID_OR_INDEX</c>, <c>WEAPON_NONFINITE</c>, <c>WEAPON_STATS_INVALID</c>), the first that applies to the first record that has one.</summary>
        public static string Check(IReadOnlyList<int> indices, IReadOnlyList<WeaponDef> definitions)
        {
            if (indices == null || definitions == null || indices.Count != definitions.Count)
                throw new ArgumentException("one declared index for each definition is required");
            var ids = new HashSet<string>();
            for (int i = 0; i < definitions.Count; i++)
            {
                var d = definitions[i];
                if (indices[i] != i || d == null || string.IsNullOrWhiteSpace(d.id) || !Regex.IsMatch(d.id, "^[a-z][a-z0-9_]+$") || !ids.Add(d.id))
                    return "WEAPON_ID_OR_INDEX: append-only unique IDs and indices required";
                foreach (var field in Fields)
                    if (field.FieldType == typeof(float) && (float.IsNaN((float)field.GetValue(d)) || float.IsInfinity((float)field.GetValue(d))))
                        return "WEAPON_NONFINITE: " + d.id + "." + field.Name;
                if (d.modes == null || d.modes.Length == 0 || d.mag <= 0 || d.reserve < 0 || d.reserveMax < d.reserve ||
                    d.damage <= 0 || d.rpm <= 0 || d.reload <= 0 || d.reloadEmpty <= 0 || d.grade < 1 || d.grade > 5 || d.price < 0)
                    return "WEAPON_STATS_INVALID: " + d.id;
            }
            return null;
        }
    }
}
