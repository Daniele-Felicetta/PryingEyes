using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    public static class ProductionCatalog
    {
        static string Hash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        public static void VerifyMigration()
        {
            string fixture = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../tools/fixtures/weapon-prefix-v1.json"));
            var prefix = (JArray)JObject.Parse(File.ReadAllText(fixture))["definitions"];
            foreach (var old in prefix)
            {
                int i = (int)old["index"];
                if (WeaponDefs.All[i].id != (string)old["id"] || Hash(JsonConvert.SerializeObject(WeaponDefs.All[i], WeaponCatalog.Settings)) != (string)old["sha256"])
                    throw new Exception("WEAPON_MIGRATION_CHANGED: index " + i);
            }
            VerifyParser();
            Debug.Log("[Production] migration PASS: all fields preserved for " + prefix.Count + " existing weapons");
        }

        public static void VerifyParser()
        {
            string source = Resources.Load<TextAsset>("Production/weapons").text;
            int rejected = 0;
            void Reject(Action<JObject> mutate)
            {
                var copy = JObject.Parse(source); mutate(copy);
                try { WeaponCatalog.Parse(copy.ToString()); }
                catch (JsonException) { rejected++; return; }
                throw new Exception("INVALID_CATALOG_ACCEPTED");
            }
            Reject(c => ((JObject)c["weapons"][0]["definition"]).Remove("damage"));
            Reject(c => c["weapons"][0]["definition"]["damage"] = "25");
            Reject(c => c["weapons"][0]["definition"]["typoDamage"] = 25);
            Reject(c => c["weapons"][0]["definition"]["modes"] = new JArray("invalid"));
            Reject(c => c["weapons"][1]["definition"]["id"] = c["weapons"][0]["definition"]["id"]);
            Reject(c => c["weapons"][0]["index"] = 2);
            Reject(c => c["weapons"][0]["definition"]["reserve"] = 99999);
            Reject(c => c["weapons"][0]["definition"]["flashColor"] = new JArray(1, 0));
            Reject(c => c["schemaVersion"] = 2);
            Debug.Log("[Production] parser PASS: " + rejected + " invalid catalogs rejected");
        }

        public static void BuildAsset()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-asset");
            if (at < 0 || at + 1 >= args.Length) throw new Exception("ASSET_ID_REQUIRED");
            string id = args[at + 1];
            if (WeaponDefs.IndexOf(id) < 0) throw new Exception("ASSET_UNKNOWN: " + id);
            VerifyParser();
            var city = JsonUtility.FromJson<CityBuilder.Recipe>(File.ReadAllText(Path.Combine(Application.dataPath, "_PryingEyes/City/citta.json")));
            if (WeaponDefs.Catalog.weapons[WeaponDefs.IndexOf(id)].source["family"] == null || Array.IndexOf(city.shopWeapons, id) < 0)
                ArmiImport.ImportOne(id);
            BuildPlayer.City();
        }
    }
}
