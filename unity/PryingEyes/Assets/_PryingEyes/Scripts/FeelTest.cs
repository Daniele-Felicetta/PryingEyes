#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -feeltest [-feelshots]: rifiniture di grafica e di comandi sul piano vero. Le sonde dei riflessi
    /// sono fotografate (una per stanza); il tabellone si apre e mostra i numeri; l'ispezione dell'arma parte, gira l'arma e torna;
    /// sparare la interrompe; il corpo di un monaco ucciso sprofonda e sparisce. Foto (tabellone, ispezione) con -feelshots.</summary>
    public sealed class FeelTest : PlayerTest
    {
        protected override string Suite { get { return "feeltest"; } }
        protected override string PhotoFlag { get { return "-feelshots"; } }
        protected override string OutFolder { get { return "rifiniture"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<FeelTest>("-feeltest"); }

        protected override void Finish()
        {
            HUD.forceScoreboard = false;
        }

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            var gm = GameManager.I; var player = gm.player; var hud = gm.hud;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            deadline = Time.realtimeSinceStartup + 20;
            int rooms = FloorDirector.I.cur.plan.rooms.Count;
            while (RoomProbes.Baked < rooms / 2 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(0.5f);
            int probes = FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Length;
            Check($"le sonde dei riflessi sono fotografate ({RoomProbes.Baked} su {rooms} stanze, {probes} sonde)", RoomProbes.Baked >= rooms / 2 && probes >= RoomProbes.Baked);
            Check("finite le foto il pool delle luci torna alla camera", LightPool.HoldPoint == null);

            // tabellone
            gm.score = 1234; gm.kills = 7; GameManager.headshots = 3;
            HUD.forceScoreboard = true;
            yield return new WaitForSeconds(0.4f);
            Check("il tabellone si apre", hud.ScoreboardOpen);
            var right = hud.transform.Find("TabelloneDestra")?.GetComponent<UnityEngine.UI.Text>();
            Check("il tabellone mostra punti e uccisioni", right != null && right.text.Contains("1.234") && right.text.Contains("3 (42%)"));
            Photo("tabellone");
            HUD.forceScoreboard = false;
            yield return new WaitForSeconds(0.4f);
            Check("lasciato Tab il tabellone si chiude", !hud.ScoreboardOpen);

            // ispezione dell'arma
            var w = player.weapon;
            w.Inspect();
            yield return new WaitForSeconds(1.1f);
            Check("l'ispezione e' in corso", w.Inspecting);
            Photo("ispezione");
            yield return new WaitForSeconds(1.8f);
            Check("l'ispezione finisce da sola", !w.Inspecting);

            // il corpo sprofonda
            Check("c'e' spazio per la prova del corpo", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            Enemy.debugFreeze = true;
            var monk = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 4f);
            yield return null;
            Vector3 p0 = monk.transform.position;
            monk.Damage(monk.hp + 5f, monk.AimPoint, false, false);
            yield return new WaitForSeconds(4.3f);
            bool sunk = monk == null || monk.transform.position.y < p0.y - 0.3f;
            Check("il corpo sprofonda nel pavimento", sunk);
            yield return new WaitForSeconds(1.2f);
            Check("e poi sparisce", monk == null);
            Enemy.debugFreeze = false;
        }
    }
}
#endif
