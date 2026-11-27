using Production.Weapons;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il pacchetto delle armi porta i colori (traccia, lampo, mirino, grado) come Rgba: non conosce il motore. Il gioco li fa diventare Color qui, al confine.</summary>
    public static class WeaponColors
    {
        public static Color ToColor(this Rgba c) => new Color(c.r, c.g, c.b, c.a);
    }
}
