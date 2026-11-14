using UnityEngine;

namespace PryingEyes
{
    /// <summary>City-specific shopping and survival integration.</summary>
    public sealed partial class CityDirector : MonoBehaviour
    {
        [System.Serializable]
        public struct Shop
        {
            public string weaponId;
            public Vector3 position;
            public float yaw;
        }

        public Shop[] shops;
        [HideInInspector] public string layoutJson, recipeSha256;

        void Start()
        {
            if (shops == null) return;
            foreach (var shop in shops)
                Place(shop.weaponId, shop.position, Quaternion.Euler(0, shop.yaw, 0));
        }

        static void Place(string id, Vector3 p, Quaternion rot)
        {
            int index = WeaponDefs.IndexOf(id);
            if (index < 0) { Debug.LogError("[City] weapon missing: " + id); return; }
            var def = WeaponDefs.All[index];
            WallBuy.Place(index, def.price, p, rot, def.GradeColor);
        }
    }
}
