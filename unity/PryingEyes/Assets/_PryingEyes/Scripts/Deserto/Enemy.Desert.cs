using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il nemico nel deserto non e' a furtivita': niente coni di vista, niente udito, niente rumore dei passi. Chi nasce nel deserto
    /// <b>riposa</b> (<see cref="dormant"/>): gira nella sua zona (la stanza di una guardia, un tratto di sabbia per chi vaga) senza notare niente, finche'
    /// il regista (DesertDirector.WakeTick) lo sveglia perche' il giocatore e' a meno di 30 m (48 di notte), o perche' lo hanno colpito, o perche' uno
    /// vicino si e' svegliato (il branco). Da sveglio e' come un nemico di round: sa dove sei e ti viene incontro. Se il giocatore scappa lontano
    /// (oltre <see cref="DesertDirector.LeashDist"/>) per qualche secondo, torna a riposare e a girare dov'era.</summary>
    public partial class Enemy
    {
        /// <summary>Riposa: non vede e non sente, gira nella sua zona. Non serializzato: un campo in piu' obbligherebbe alla build completa.</summary>
        [System.NonSerialized] public bool dormant;
        /// <summary>Da quanti secondi (di gioco) e' sveglio e lontano dal giocatore (lo conta il regista del deserto).</summary>
        [System.NonSerialized] public float farT;

        /// <summary>Entro quanti metri uno che si sveglia sveglia anche i vicini.</summary>
        public const float PackWakeRadius = 16f;

        static readonly List<Enemy> packTmp = new List<Enemy>();

        /// <summary>Lo mette a riposo: a pattugliare la sua zona, senza notare niente.</summary>
        public void Doze()
        {
            if (dead || puppet || relentless) return;
            dormant = true;
            farT = 0f;
            awareness = 0f; seeing = false; seenCache = false; lostT = 0f; hasGoal = false;
            waitT = Random.Range(1f, 3f);
            alert = Alert.Patrol;
            if (eye != null) { eye.lookAt = null; eye.glowBoost = 0.85f; }
            if (jelly != null) jelly.glowBoost = 0.85f;
        }

        /// <summary>Si sveglia e ti caccia (il grido e il ringhio di sempre). Con 'pack' sveglia anche chi riposa a meno di <see cref="PackWakeRadius"/>.</summary>
        public void WakeUp(bool pack = true)
        {
            if (!dormant || dead || puppet) return;
            dormant = false;
            farT = 0f;
            if (tgt == null || !tgt.Alive) SwitchTarget(Targets.Nearest(transform.position));
            SetAlert(Alert.Hunting);
            if (!pack) return;
            packTmp.Clear();
            float r2 = PackWakeRadius * PackWakeRadius;
            foreach (var o in all)
                if (o != null && o != this && o.dormant && !o.dead && (o.transform.position - transform.position).sqrMagnitude < r2) packTmp.Add(o);
            foreach (var o in packTmp) o.WakeUp(false);      // il richiamo di un Primario puo' far nascere altri nemici: per questo la lista e' una copia
            packTmp.Clear();
        }
    }
}
