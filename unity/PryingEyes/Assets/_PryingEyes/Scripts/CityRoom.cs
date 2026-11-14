using UnityEngine;
using Production.Layout;

namespace PryingEyes
{
    /// <summary>Baked room recipe and geometry, available to navigation/visual acceptance tools.</summary>
    public sealed class CityRoom : MonoBehaviour
    {
        public string recipeId, sourceSha256, planJson;
        public RoomPlan Plan => JsonUtility.FromJson<RoomPlan>(planJson);
    }
}
