#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -magictest: la magia a distanza del monaco (Enemy.cs, LaunchBolts). Piazza un monaco da 3000 punti vita a 13 m dal giocatore
    /// (non muore prima di lanciare) e lascia giocare il bot di -autotest; il monaco carica la sfera sul bastone e lancia il dardo quando vede il giocatore
    /// fra 3 e 26 m: il log dice a che distanza ("mago lancia magia da ...") e quanto danno fa ("magia colpisce il giocatore ...").
    /// Era una procedura di GameManager, portata cosi' com'e': il vecchio -magictest giocava fino al settantesimo secondo dell'uscita automatica (69 s dal monaco),
    /// e la prova guarda la stessa finestra, chiusa prima se un dardo e' gia' partito (piu' due secondi, per il log del danno).
    /// STATO: il monaco sta a x 0, z 6, coordinate della hall classica: sui piani della torre quel punto e' fuori dalla navmesh ("Failed to create agent because it is not
    /// close enough to the NavMesh"), il monaco non si muove e non lancia nulla, e il vecchio log non aveva nessuna riga "mago lancia magia" in 76 s. Il controllo fallisce
    /// come prima del porting. Non riparata.</summary>
    public sealed class MagicTest : PlayerTest
    {
        protected override string Suite { get { return "magictest"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<MagicTest>("-magictest"); }

        protected override IEnumerator Run()
        {
            var gm = GameManager.I;
            while (!gm.LevelReady || Time.timeSinceLevelLoad < 1f) yield return null;      // GameManager lo faceva dopo 1 s di prova automatica
            int bolts0 = Enemy.statBolts;
            var monaco = Enemy.Spawn(Enemy.EnemyType.Monaco, new Vector3(0f, 0.05f, 6f));
            if (monaco != null) monaco.hp = monaco.maxHp = 3000f;   // non muore prima di lanciare
            Debug.Log("[PryingEyes] magictest: monaco piazzato a 13 m");
            if (!Check("il monaco e' piazzato", monaco != null)) yield break;
            bool onNav = NavMesh.SamplePosition(monaco.transform.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas);
            float t0 = Time.time, first = -1f;
            while (Time.time - t0 < 69f)
            {
                if (first < 0f && Enemy.statBolts > bolts0) first = Time.time - t0;
                if (first >= 0f && Time.time - t0 > first + 2f) break;
                yield return null;
            }
            int bolts = Enemy.statBolts - bolts0;
            Check("il monaco lancia la magia a distanza", bolts > 0,
                  bolts > 0 ? bolts + " dardi, il primo dopo " + first.ToString("F1") + " s" : "nessun dardo in " + (Time.time - t0).ToString("F0") + " s (monaco sulla navmesh: " + (onNav ? "si" : "no") + ")");
        }
    }
}
#endif
