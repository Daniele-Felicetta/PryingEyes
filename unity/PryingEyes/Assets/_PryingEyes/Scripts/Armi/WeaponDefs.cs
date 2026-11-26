using Production.Weapons;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Catalogo autorevole in Resources/Production/weapons.json; gli indici sono append-only.</summary>
    public static class WeaponDefs
    {
        public static WeaponCatalog Catalog { get; private set; }
        public static string ContentHash { get; private set; }
        public static readonly WeaponDef[] All = Build();

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return -1;
        }

        static WeaponDef[] Build()
        {
            var asset = Resources.Load<TextAsset>("Production/weapons");
            if (asset == null) throw new System.InvalidOperationException("WEAPON_CATALOG_MISSING: Production/weapons");
            Catalog = WeaponCatalog.Parse(asset.text);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                ContentHash = System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(
                    Catalog.ToJson().Replace("\r\n", "\n")))).Replace("-", "").ToLowerInvariant().Substring(0, 12);
            var result = new WeaponDef[Catalog.weapons.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Catalog.weapons[i].definition;
            return result;
        }
    }
}
