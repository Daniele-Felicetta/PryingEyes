using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PryingEyes
{
    public sealed class WeaponCatalog
    {
        public int schemaVersion = 1;
        public Entry[] weapons;
        public sealed class Entry
        {
            public int index;
            public WeaponDef definition;
            public JObject source;
        }

        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            TypeNameHandling = TypeNameHandling.None,
            Converters = new List<JsonConverter> { new StringEnumConverter { AllowIntegerValues = false }, new ColorConverter() }
        };

        public static WeaponCatalog Parse(string json)
        {
            var root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if ((int?)root["schemaVersion"] != 1 || !(root["weapons"] is JArray entries) || entries.Count == 0)
                throw new JsonException("CATALOG_SCHEMA: expected schemaVersion 1 and a nonempty weapons array");
            var fields = typeof(WeaponDef).GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var entry in entries)
            {
                if (!(entry is JObject item) || item["index"]?.Type != JTokenType.Integer) throw new JsonException("WEAPON_INDEX_TYPE");
                if (!(entry["definition"] is JObject definition)) throw new JsonException("WEAPON_DEFINITION_MISSING");
                foreach (var field in fields)
                {
                    var value = definition[field.Name];
                    if (value == null) throw new JsonException("WEAPON_FIELD_MISSING: " + field.Name);
                    bool wrong = field.FieldType == typeof(float) ? value.Type != JTokenType.Float && value.Type != JTokenType.Integer :
                        field.FieldType == typeof(int) ? value.Type != JTokenType.Integer :
                        field.FieldType == typeof(bool) ? value.Type != JTokenType.Boolean :
                        field.FieldType == typeof(string) ? value.Type != JTokenType.String : false;
                    if (wrong) throw new JsonException("WEAPON_FIELD_TYPE: " + field.Name);
                }
            }
            var catalog = root.ToObject<WeaponCatalog>(JsonSerializer.Create(Settings));
            var ids = new HashSet<string>();
            for (int i = 0; i < catalog.weapons.Length; i++)
            {
                var entry = catalog.weapons[i]; var d = entry.definition;
                if (entry.index != i || d == null || string.IsNullOrWhiteSpace(d.id) || !Regex.IsMatch(d.id, "^[a-z][a-z0-9_]+$") || !ids.Add(d.id))
                    throw new JsonException("WEAPON_ID_OR_INDEX: append-only unique IDs and indices required");
                foreach (var field in fields)
                    if (field.FieldType == typeof(float) && (float.IsNaN((float)field.GetValue(d)) || float.IsInfinity((float)field.GetValue(d))))
                        throw new JsonException("WEAPON_NONFINITE: " + d.id + "." + field.Name);
                if (d.modes == null || d.modes.Length == 0 || d.mag <= 0 || d.reserve < 0 || d.reserveMax < d.reserve ||
                    d.damage <= 0 || d.rpm <= 0 || d.reload <= 0 || d.reloadEmpty <= 0 || d.grade < 1 || d.grade > 5 || d.price < 0)
                    throw new JsonException("WEAPON_STATS_INVALID: " + d.id);
            }
            return catalog;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented, Settings);

        sealed class ColorConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => type == typeof(Color) || type == typeof(Color?);
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null) { writer.WriteNull(); return; }
                var c = (Color)value;
                writer.WriteStartArray(); writer.WriteValue(c.r); writer.WriteValue(c.g); writer.WriteValue(c.b); writer.WriteValue(c.a); writer.WriteEndArray();
            }
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null && type == typeof(Color?)) return null;
                var values = JArray.Load(reader);
                if (values.Count != 4) throw new JsonException("COLOR_COMPONENTS: expected RGBA");
                foreach (var value in values) if (value.Type != JTokenType.Float && value.Type != JTokenType.Integer) throw new JsonException("COLOR_COMPONENT_TYPE");
                var c = new Color((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
                for (int i = 0; i < 4; i++) if (float.IsNaN(c[i]) || float.IsInfinity(c[i])) throw new JsonException("COLOR_NONFINITE");
                return c;
            }
        }
    }
}
