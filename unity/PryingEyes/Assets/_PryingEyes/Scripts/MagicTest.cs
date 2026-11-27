#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -magictest: la magia a distanza dei nemici (Enemy.cs, LaunchBolts). Un nemico da 3000 punti vita (non muore prima di lanciare) sta a circa 13 m dal giocatore, su un
    /// punto della navmesh con la linea libera; il bot di -autotest non spara (HoldFire) e il giocatore e' invulnerabile. Il nemico carica la sfera sul bastone e lancia il dardo quando
    /// vede il giocatore fra 3 e 26 m: il log dice a che distanza e quanto danno fa ("magia colpisce il giocatore ...").
    /// Il 13 novembre 2026 (commit a675b19, "i nemici ordinari cercano la mischia, i dardi restano al Direttore") il monaco ha smesso di lanciare dardi (<c>volley</c> 0 in EnemyTemi.cs): la
    /// prova, scritta quando lo faceva, guarda ora i due casi: il monaco ti raggiunge e non lancia nulla, il Direttore (il ventaglio di tre dardi) lancia.
    /// Era una procedura di GameManager, portata cosi' com'era: metteva il monaco a x 0, z 6, un punto della hall classica che sui piani della torre e' fuori dalla navmesh ("Failed to create
    /// agent because it is not close enough to the NavMesh"), e anche sulla navmesh il monaco non avrebbe lanciato nulla.</summary>
    public sealed class MagicTest : PlayerTest
    {
        protected override string Suite { get { return "magictest"; } }
        /// <summary>Il bot non spara: i nemici da 3000 punti vita non devono morire sotto i suoi colpi mentre si guarda se lanciano.</summary>
        protected override bool HoldFire { get { return true; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<MagicTest>("-magictest"); }

        /// <summary>Un punto camminabile fra 5 e 17 m dal giocatore (13 m per primo, la distanza della prova di una volta; 7 e 5 m solo se piu' lontano la linea non e' libera), allo stesso livello
        /// e con la linea libera fino al petto di chi ci sta.</summary>
        static bool FindSpot(PlayerController pl, out Vector3 spot)
        {
            Vector3 eye = pl.cam.position, feet = pl.transform.position;
            foreach (float r in new[] { 13f, 11f, 15f, 9f, 17f, 7f, 5f })
                for (int k = 0; k < 16; k++)
                {
                    float a = k * 22.5f * Mathf.Deg2Rad;
                    Vector3 p = feet + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                    if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1.0f, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(hit.position.y - feet.y) > 0.5f) continue;
                    if (!HotelNav.ClearLine(eye, hit.position + Vector3.up * 1.2f)) continue;
                    spot = hit.position;
                    return true;
                }
            spot = Vector3.zero;
            return false;
        }

        /// <summary>Un nemico da 3000 punti vita sul punto dato, guardato per 'window' secondi; si ferma 2 s dopo il primo dardo, se ne parte uno (il tempo di leggere il danno nel log).</summary>
        IEnumerator Watch(Enemy.EnemyType type, Vector3 spot, PlayerController pl, float window, System.Action<bool, int, float, float> done)
        {
            int bolts0 = Enemy.statBolts;
            var enemy = Enemy.Spawn(type, spot);
            if (enemy == null) { done(false, 0, -1f, 99f); yield break; }
            enemy.hp = enemy.maxHp = 3000f;
            float t0 = Time.time, first = -1f, nearest = 99f;
            while (Time.time - t0 < window)
            {
                if (first < 0f && Enemy.statBolts > bolts0) first = Time.time - t0;
                if (first >= 0f && Time.time - t0 > first + 2f) break;
                if (enemy != null) nearest = Mathf.Min(nearest, Vector3.Distance(enemy.transform.position, pl.transform.position));
                yield return null;
            }
            int bolts = Enemy.statBolts - bolts0;
            if (enemy != null) { enemy.Damage(enemy.hp + 1f, enemy.AimPoint, false, false); Destroy(enemy.gameObject); }
            yield return null;
            done(true, bolts, first, nearest);
        }

        protected override IEnumerator Run()
        {
            var gm = GameManager.I;
            while (!gm.LevelReady || Time.timeSinceLevelLoad < 1f) yield return null;      // GameManager lo faceva dopo 1 s di prova automatica
            var pl = gm.player;
            pl.invulnerable = true;
            if (!FindSpot(pl, out Vector3 spot)) { NotApplicable("nessun punto della navmesh fra 5 e 17 m dal giocatore con la linea libera (cambia seme o piano)"); yield break; }
            Debug.Log($"[PryingEyes] magictest: nemico piazzato a {Vector3.Distance(spot, pl.transform.position):F0} m");

            // il monaco: dal 13 novembre ti raggiunge e non lancia dardi (vale 12 s: il suo ritmo di magia, quando la faceva, era uno ogni 2,6 s)
            bool monk = false; int bolts = 0; float first = -1f, nearest = 99f;
            yield return Watch(Enemy.EnemyType.Monaco, spot, pl, 12f, (placed, b, f, n) => { monk = placed; bolts = b; first = f; nearest = n; });
            if (!Check("il monaco e' piazzato", monk)) yield break;
            Check("il monaco ti raggiunge", nearest < 3.5f, $"a {nearest:F1} m al piu' vicino, partendo da {Vector3.Distance(spot, pl.transform.position):F0} m");
            Check("il monaco non lancia dardi: preme in mischia", bolts == 0, bolts == 0 ? "nessun dardo in 12 s" : $"{bolts} dardi, il primo dopo {first:F1} s");

            // il Direttore: carica la sfera e lancia il ventaglio da lontano
            bool director = false; int fanned = 0; float firstFan = -1f;
            yield return Watch(Enemy.EnemyType.Direttore, spot, pl, 40f, (placed, b, f, n) => { director = placed; fanned = b; firstFan = f; });
            if (!Check("il Direttore e' piazzato", director)) yield break;
            Check("il Direttore lancia la magia a distanza", fanned > 0,
                  fanned > 0 ? fanned + " dardi, il primo dopo " + firstFan.ToString("F1") + " s" : "nessun dardo in 40 s");
        }
    }
}
#endif
