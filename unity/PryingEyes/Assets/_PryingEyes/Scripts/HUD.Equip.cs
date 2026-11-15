using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Equipaggiamento e Prova nell'HUD: sopra le armi, a destra, quante acquasante (Z), carillon (4) e mine (5) restano; in basso al
    /// centro, sopra i messaggi, la Prova dell'ondata col suo contatore (in oro).</summary>
    public partial class HUD
    {
        Text equipText, provaText;

        void BuildEquipUI()
        {
            equipText = MakeText("Equip", new Vector2(1, 0), new Vector2(-28, 178), new Vector2(460, 20), 13, TextAnchor.LowerRight, "", Dim, FontStyle.Normal);
            provaText = MakeText("Prova", new Vector2(0.5f, 0f), new Vector2(0, 128), new Vector2(760, 24), 16, TextAnchor.MiddleCenter, "", Gold, FontStyle.Bold);
        }

        public void SetEquipment(int grenades, int lures, int mines = 0)
        {
            if (equipText == null) return;
            equipText.text = $"Z  ACQUASANTA ×{grenades}      4  CARILLON ×{lures}      5  MINA ×{mines}";
        }

        public void SetProva(string s)
        {
            if (provaText == null) return;
            provaText.text = s ?? "";
        }
    }
}
