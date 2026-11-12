using UnityEngine;

namespace PryingEyes
{
    /// <summary>Parte "round" del nemico (RoundDirector): chi nasce in una stanza sigillata non pattuglia e non ha bisogno di
    /// vederti ne' di sentirti, ti viene incontro dal primo istante, come nella hall classica.</summary>
    public partial class Enemy
    {
        /// <summary>Nemico da round: onnisciente, sempre all'attacco (vedi Omniscient in Enemy.Stealth.cs). Non serializzato di proposito:
        /// un campo in piu' cambierebbe il formato dei dati di Enemy e obbligherebbe alla build completa (~700 MB riscritti).</summary>
        [System.NonSerialized] public bool relentless;

        /// <summary>Lo toglie dalla pattuglia e lo mette all'attacco senza passare da SetAlert: niente grido, niente fischietto
        /// della Maschera, niente richiamo del Primario (un round non deve chiamare altra gente da fuori).</summary>
        public void MakeRelentless()
        {
            relentless = true;
            alert = Alert.Hunting;
            awareness = 1f;
            if (eye != null) { eye.lookAt = GameManager.I.player.cam; eye.glowBoost = 1.9f; }
        }

        /// <summary>Lo sposta (una serranda che cala lo toglie dalla soglia). Chi cammina passa dall'agente, chi vola no.</summary>
        public void Nudge(Vector3 pos)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Warp(pos);
            else transform.position = pos;
        }

        /// <summary>Toglie un nemico senza che sia stato ucciso (round annullato o scaduto): niente punti, niente conteggio uccisioni.</summary>
        public static void Remove(Enemy e)
        {
            if (e == null) return;
            all.Remove(e);
            if (GameManager.I != null) GameManager.I.SetAlive(all.Count);
            if (e.loop != null) e.loop.Stop();
            Destroy(e.gameObject);
        }
    }
}
