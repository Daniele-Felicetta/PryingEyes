using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Production.Weapons;

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
            Converters = new List<JsonConverter> { new StringEnumConverter { AllowIntegerValues = false }, new RgbaConverter() },
            ContractResolver = new RecordContract()
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
            var indices = new int[catalog.weapons.Length];
            var definitions = new WeaponDef[catalog.weapons.Length];
            for (int i = 0; i < indices.Length; i++) { indices[i] = catalog.weapons[i].index; definitions[i] = catalog.weapons[i].definition; }
            string problem = WeaponCatalogRules.Check(indices, definitions);       // identifiers and indices, finite numbers, statistics: the rules of a catalogue are the package's
            if (problem != null) throw new JsonException(problem);
            return catalog;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented, Settings);

        /// <summary>WeaponDef is a record of public fields: the columns of the catalogue are its fields, and its two computed properties (the name and the colour of the grade) are not data.
        /// The record said so with an attribute of this library until it moved to a package that cannot name the library; now the reader says it.</summary>
        sealed class RecordContract : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var members = base.GetSerializableMembers(objectType);
                if (objectType == typeof(WeaponDef)) members.RemoveAll(member => !(member is FieldInfo));
                return members;
            }
        }

        sealed class RgbaConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => type == typeof(Rgba) || type == typeof(Rgba?);
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null) { writer.WriteNull(); return; }
                var c = (Rgba)value;
                writer.WriteStartArray(); writer.WriteValue(c.r); writer.WriteValue(c.g); writer.WriteValue(c.b); writer.WriteValue(c.a); writer.WriteEndArray();
            }
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null && type == typeof(Rgba?)) return null;
                var values = JArray.Load(reader);
                if (values.Count != 4) throw new JsonException("COLOR_COMPONENTS: expected RGBA");
                foreach (var value in values) if (value.Type != JTokenType.Float && value.Type != JTokenType.Integer) throw new JsonException("COLOR_COMPONENT_TYPE");
                var c = new Rgba((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
                for (int i = 0; i < 4; i++) if (float.IsNaN(c[i]) || float.IsInfinity(c[i])) throw new JsonException("COLOR_NONFINITE");
                return c;
            }
        }
    }
}
