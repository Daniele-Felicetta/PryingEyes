using UnityEngine;

namespace PryingEyes
{
    /// <summary>La parte dell'Enemy che serve al coltello (Knife.cs): a che distanza sta il suo corpo, se gli sei alle spalle,
    /// se lo si puo' accoltellare in silenzio e come reagisce se sopravvive. Niente campi serializzati (solo NonSerialized):
    /// un campo in piu' cambierebbe il formato dei dati e obbligherebbe alla build completa.</summary>
    public partial class Enemy
    {
        [System.NonSerialized] Collider[] knifeCols;

        /// <summary>Cammina (monaco, abate e i costumi di tema): l'unico che si puo' sorprendere alle spalle. Chi vola ha la
        /// vista tutto intorno e non ha una schiena.</summary>
        public bool IsWalker => Archetype == EnemyType.Monaco || Archetype == EnemyType.Abate;

        /// <summary>Distanza (m) da 'from' al punto piu' vicino del suo corpo, e quel punto. Il centro dell'origine non basta: l'abate
        /// e' largo un metro, l'occhio galleggia a un metro e mezzo da terra.</summary>
        public float KnifeReach(Vector3 from, out Vector3 point)
        {
            if (knifeCols == null) knifeCols = GetComponentsInChildren<Collider>();
            float best = float.MaxValue;
            point = AimPoint;
            foreach (var c in knifeCols)
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                Vector3 p = c.ClosestPoint(from);
                float d = (p - from).sqrMagnitude;
                if (d < best) { best = d; point = p; }
            }
            return best == float.MaxValue ? Vector3.Distance(from, point) : Mathf.Sqrt(best);
        }

        /// <summary>Il punto 'from' sta alle sue spalle? (angolo orizzontale tra dove guarda e dove sta chi lo guarda)</summary>
        public bool IsBehind(Vector3 from, float minAngle = 100f)
        {
            Vector3 f = Flat(transform.forward), t = Flat(from - transform.position);
            if (f.sqrMagnitude < 1e-4f || t.sqrMagnitude < 1e-4f) return false;
            return Vector3.Angle(f, t) > minAngle;
        }

        /// <summary>Si puo' fare la presa: cammina, non ti ha visto e non ti sta cacciando, e gli sei dietro. Chi nasce in un round
        /// (relentless) sa sempre dov'e' il giocatore e si gira: per lui niente.</summary>
        public bool CanTakedown(Vector3 from) =>
            !dead && !relentless && IsWalker && alert != Alert.Hunting && !seeing && IsBehind(from);

        /// <summary>L'ha colpito il giocatore e non e' morto: adesso sa dov'e'. Il grido richiama gli altri (SetAlert).</summary>
        public void Provoke()
        {
            if (dead || Omniscient) return;
            awareness = 1f;
            lostT = 0f;
            lastKnown = GameManager.I.player.transform.position;
            if (alert != Alert.Hunting) SetAlert(Alert.Hunting);
        }
    }
}
