using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>La moto investe chi cammina. Oltre PlowMinSpeed un monaco sulla strada non e' un muro: il corpo del pilota (CharacterController) lo attraversa, il monaco prende un colpo che dipende dalla
    /// velocita' (Enemy.PlowDamagePerMps a ogni m/s, fonte DamageSource.Moto) e vola via, e la moto perde un po' di velocita' per ogni corpo (il 5% un monaco, il 13% un abate) senza che sia un urto
    /// contro un muro (la frenata da urto, in RideTick, conta solo quello che il corpo non ha attraversato). Sotto la soglia, e contro chi non si investe (Enemy.Plowable: volanti, statua, boss), la capsula
    /// del nemico resta solida come prima: la moto si ferma contro di lui.
    /// Il corpo del pilota deve attraversarlo gia' nello spostamento in cui lo colpisce, quindi la sagoma della moto (dal parafango dietro alla ruota davanti, piu' la strada che fa in un fotogramma e mezzo)
    /// e' piu' larga del contatto vero: il colpo e il passaggio libero vengono sempre prima della capsula. Chi sopravvive resta 'investito' (senza capsula per il pilota, ne' un secondo colpo) finche' non gli
    /// sta a piu' di due metri e mezzo, poi torna solido.</summary>
    public partial class PlayerController
    {
        /// <summary>Sotto questa velocita' (m/s, 20 km/h) la moto non investe: spinge contro il corpo e si ferma.</summary>
        public const float PlowMinSpeed = 5.5f;
        const float PlowLossPerMass = 0.05f, PlowFree = 2.6f;
        readonly List<Enemy> ridePlowed = new List<Enemy>();
        readonly List<Enemy> plowHits = new List<Enemy>();
        readonly List<Collider> plowCols = new List<Collider>();
        float plowSoundAt;
        /// <summary>Per le prove: quanti nemici ha investito il pilota dall'ultima volta che e' salito in sella.</summary>
        public int RidePlowCount { get; private set; }

        /// <summary>I corpi che la capsula del pilota ignora in questo momento (per le prove).</summary>
        public int RidePlowedNow => ridePlowed.Count;

        /// <summary>Il corpo del pilota attraversa (on) o urta di nuovo (off) tutte le capsule di un nemico.</summary>
        void PlowIgnore(Enemy e, bool on)
        {
            if (cc == null || e == null || !cc.enabled) return;
            plowCols.Clear();
            e.GetComponentsInChildren(true, plowCols);
            for (int i = 0; i < plowCols.Count; i++)
            {
                var c = plowCols[i];
                if (c != null && c.enabled && c.gameObject.activeInHierarchy) Physics.IgnoreCollision(c, cc, on);
            }
        }

        /// <summary>Fine della corsa (si scende, si cade, si muore): chi era stato investito torna solido.</summary>
        void RidePlowEnd()
        {
            for (int i = 0; i < ridePlowed.Count; i++) PlowIgnore(ridePlowed[i], false);
            ridePlowed.Clear();
        }

        /// <summary>Chiamata da RideTick prima di muovere il corpo: investe chi sta nella sagoma della moto. 'hv' e' la velocita' orizzontale che il corpo sta per fare (rotta piu' scivolamento di traverso: in
        /// derapata il corpo va di lato, e la sagoma segue il suo moto, non la rotta); 'along' la sua parte lungo la rotta. Toglie ad 'hv' la velocita' che i corpi costano; dice se e' cambiata.</summary>
        bool RidePlow(float dt, ref Vector3 hv, float along)
        {
            Vector3 p = transform.position;
            // chi era gia' stato investito e ora e' lontano torna solido; chi e' caduto non c'e' piu'
            for (int i = ridePlowed.Count - 1; i >= 0; i--)
            {
                var e = ridePlowed[i];
                if (e == null || e.IsDead) { ridePlowed.RemoveAt(i); continue; }
                Vector3 d = e.transform.position - p; d.y = 0f;
                if (d.sqrMagnitude > PlowFree * PlowFree) { PlowIgnore(e, false); ridePlowed.RemoveAt(i); }
            }
            float speed = hv.magnitude;
            if (speed < PlowMinSpeed || along < -0.5f) return false;              // piano, o in retromarcia (al massimo 5 m/s): un corpo resta un ostacolo
            Vector3 dir = hv / speed;

            // la sagoma della moto vista dall'alto: dal parafango dietro (0,45 m) alla ruota davanti (0,95 m) piu' la strada di un fotogramma e mezzo, larga quanto il manubrio e il corpo
            Vector3 a = p - dir * 0.45f, ab = dir * (0.45f + 0.95f + speed * dt * 1.5f);
            float abLen2 = ab.sqrMagnitude;
            float half = Mathf.Max(0.5f, (cc != null ? cc.radius : 0.4f) + 0.1f);
            plowHits.Clear();
            var all = Enemy.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || !e.Plowable || ridePlowed.Contains(e)) continue;
                Vector3 ep = e.transform.position;
                if (Mathf.Abs(ep.y - p.y) > 1.3f) continue;                    // un altro piano: il tetto sopra, la scala sotto
                Vector3 rel = ep - a; rel.y = 0f;
                float t = Mathf.Clamp01(Vector3.Dot(rel, ab) / abLen2);
                Vector3 off = rel - ab * t;
                float reach = half + e.BodyRadius;
                if (off.sqrMagnitude > reach * reach) continue;
                if (Physics.Linecast(p + Vector3.up * 0.7f, ep + Vector3.up * 0.7f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;      // un muro in mezzo: non e' lui
                plowHits.Add(e);
            }
            if (plowHits.Count == 0) return false;

            float loss = 0f;
            for (int i = 0; i < plowHits.Count; i++)
            {
                var e = plowHits[i];
                if (e == null) continue;
                PlowIgnore(e, true);                                           // prima del colpo e dello spostamento: il corpo lo attraversa, non vi si ferma
                ridePlowed.Add(e);
                Vector3 at = e.transform.position + Vector3.up * 1.15f;
                Fx.Blood(at, dir, e.BloodTint, false);
                loss += PlowLossPerMass * e.PlowMass * e.PlowMass;          // il peso conta al quadrato: un monaco il 5%, un abate il 13%
                RidePlowCount++;
                e.Damage(Enemy.PlowDamagePerMps * speed, at, false, true, DamageSource.Moto);
            }
            loss = Mathf.Min(loss, 0.5f);
            hv *= 1f - loss;
            // il colpo si sente nella moto e nella visuale: poco per un corpo, di piu' per una fila
            float k = Mathf.Clamp01(loss * 3f);
            if (bike != null && Time.time >= plowSoundAt) { bike.Impact(0.15f + 0.4f * k); plowSoundAt = Time.time + 0.2f; }          // una fila di corpi e' un tonfo, non cinque sovrapposti
            Kick(0.9f + 1.4f * k, Random.Range(-1f, 1f) * 1.3f, 7f);
            fovKick -= 2f + 3f * k;
            rideDip = Mathf.Max(rideDip, 0.04f + 0.05f * k);
            return true;
        }
    }
}
