using UnityEngine;

namespace PryingEyes
{
    // Le moto del deserto investono chi cammina (PlayerController.Ride.Plow.cs): chi si puo' investire, quanto e' largo, quanto pesa e quanto danno fa ogni metro al secondo di velocita'.
    public partial class Enemy
    {
        /// <summary>Il danno di una moto per ogni m/s di velocita': a 45 km/h (12,5 m/s) un monaco di 200 punti vita cade, a 61 km/h ne restano -72, e un abate (320) ci vuole oltre i 72 km/h.</summary>
        public const float PlowDamagePerMps = 16f;

        /// <summary>Chi puo' essere investito da una moto: chi cammina. Restano ostacoli i volanti (la loro capsula non tocca il pilota), la statua di pietra, il boss e chi sta uscendo dal pavimento.</summary>
        public bool Plowable => !dead && !IsFlying(type) && type != EnemyType.Statua && !IsBoss(type) && maxHp < 1000f && riseT < 0f;

        /// <summary>Il raggio del corpo di chi cammina, per la sagoma della moto (la capsula del monaco e' 0,3; l'abate e' piu' grosso).</summary>
        public float BodyRadius => Archetype == EnemyType.Abate ? 0.5f : 0.35f;

        /// <summary>Quanto pesa per la moto: 1 un monaco, 1,6 un abate. Ogni corpo investito toglie alla moto il 5% della velocita' per il quadrato di questo numero.</summary>
        public float PlowMass => Mathf.Clamp(maxHp / 200f, 0.6f, 2.5f);
    }
}
