using UnityEngine;
using UnityEngine.AI;

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

        /// <summary>Guardia (pianta a griglia): pattuglia solo la sua stanza, tra due angoli (x, z in metri). Quando ti perde o finisce di
        /// cercarti ci torna da solo, perche' la prossima meta e' sempre dentro. Non serializzato, come relentless.</summary>
        [System.NonSerialized] public bool hasHome;
        [System.NonSerialized] public Vector2 homeMin, homeMax;

        public void SetHome(Vector2 min, Vector2 max)
        {
            hasHome = true; homeMin = min; homeMax = max;
        }

        /// <summary>Un punto a caso della stanza di casa, sulla navmesh, ad almeno 3,5 m da dove sta; se non si trova, dove sta.</summary>
        Vector3 PickHomeGoal(Vector3 gp)
        {
            for (int tries = 0; tries < 12; tries++)
            {
                var p = new Vector3(Random.Range(homeMin.x, homeMax.x), gp.y, Random.Range(homeMin.y, homeMax.y));
                if (Flat(p - gp).magnitude < 3.5f) continue;
                if (!NavMesh.SamplePosition(p, out NavMeshHit h, 1.8f, NavMesh.AllAreas)) continue;
                if (h.position.x < homeMin.x - 0.6f || h.position.x > homeMax.x + 0.6f || h.position.z < homeMin.y - 0.6f || h.position.z > homeMax.y + 0.6f) continue;
                return h.position;
            }
            return gp;
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
