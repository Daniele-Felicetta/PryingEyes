#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -leantest -camshots: nel piccolo ascensore di arrivo, lean a destra e a sinistra con foto e numeri (spostamento della testa, inclinazione,
    /// punto del corpo visto dai nemici, spazio concesso dai muri), poi con un muro a 38 cm sulla destra la testa non puo' sporgere piu' di tanto
    /// (il lean si ferma a 0,64). Foto e leantest.txt in hotel_build/armi_gioco/. Era una procedura di GameManager.</summary>
    public sealed class LeanTest : PlayerTest
    {
        protected override string Suite { get { return "leantest"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<LeanTest>("-leantest"); }

        /// <summary>The lean is a debug value of the player: it goes back to the keys when the test ends.</summary>
        protected override void Finish() { PlayerController.debugLean = float.NaN; }

        protected override IEnumerator Run()
        {
            var gm = GameManager.I;
            while (!gm.LevelReady || Time.timeSinceLevelLoad < 2f) yield return null;      // GameManager la faceva partire dopo 2 s di prova automatica
            var player = gm.player;
            string dir = TestPaths.Out("armi_gioco");
            System.IO.Directory.CreateDirectory(dir);
            Enemy.ClearAll();
            player.invulnerable = true;
            player.weapon.Equip(WeaponDefs.IndexOf("ar9_malleus"));
            var cam = player.cam;
            GunTest.LookOpenest(player);
            yield return new WaitForSeconds(1.2f);
            int step = 0;
            foreach (float l in new[] { 0f, 1f, -1f, 1f })
            {
                PlayerController.debugLean = l;
                yield return new WaitForSeconds(1.0f);
                Vector3 off = cam.position - (player.transform.position + Vector3.up * 1.62f);
                float lateral = Vector3.Dot(off, new Vector3(Mathf.Cos(player.yaw), 0f, -Mathf.Sin(player.yaw)));
                float roll = cam.eulerAngles.z; if (roll > 180f) roll -= 360f;
                Vector3 head = player.LeanedPoint(1.58f), hip = player.LeanedPoint(0.9f);
                string msg = $"lean={l:F0} reale={player.lean:F2} testa laterale={lateral:F2} m quota={off.y:F3} m roll={roll:F1} gradi spazio_dx={player.LeanRoomPublic(1f):F2} spazio_sx={player.LeanRoomPublic(-1f):F2} " +
                             $"testa_per_i_nemici={(head - player.transform.position - Vector3.up * 1.58f).magnitude:F2} m bacino_per_i_nemici={(hip - player.transform.position - Vector3.up * 0.9f).magnitude:F2} m";
                Debug.Log("[PryingEyes] leantest " + msg);
                System.IO.File.AppendAllText(dir + "/leantest.txt", msg + "\n");
                GameManager.CamShot(dir + "/lean_" + (l == 0f ? "centro" : l > 0f ? "destra" : "sinistra") + ".png", null, true);
                step++;
                string seen = $"testa laterale {lateral:F2} m, roll {roll:F1} gradi";
                if (l == 0f) Check("senza lean la testa resta al centro e la vista dritta", Mathf.Abs(lateral) < 0.05f && Mathf.Abs(roll) < 1f, seen);
                else if (l > 0f) Check(step == 2 ? "lean a destra: la testa sporge a destra e la vista si inclina" : "di nuovo a destra dopo la sinistra: la testa torna a destra",
                                       lateral > 0.3f && roll < -10f, seen);
                else Check("lean a sinistra: la testa sporge a sinistra e la vista si inclina", lateral < -0.3f && roll > 10f, seen);
            }
            // con un muro a 33 cm sulla destra la testa non puo' sporgere piu' di tanto
            var right = new Vector3(Mathf.Cos(player.yaw), 0f, -Mathf.Sin(player.yaw));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "MuroProvaLean";
            wall.transform.position = player.transform.position + right * 0.38f + Vector3.up * 1.5f;
            wall.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Sin(player.yaw), 0f, Mathf.Cos(player.yaw)));
            wall.transform.localScale = new Vector3(0.10f, 3f, 3f);
            wall.transform.rotation = Quaternion.LookRotation(right) * Quaternion.Euler(0f, 90f, 0f);
            PlayerController.debugLean = 1f;
            yield return new WaitForSeconds(1.2f);
            string m2 = $"muro a destra: lean reale={player.lean:F2} spazio_dx={player.LeanRoomPublic(1f):F2}";
            Debug.Log("[PryingEyes] leantest " + m2);
            System.IO.File.AppendAllText(dir + "/leantest.txt", m2 + "\n");
            Check("con un muro a 38 cm sulla destra il lean si ferma prima del massimo", player.lean > 0.4f && player.lean < 0.85f, $"lean reale {player.lean:F2}, spazio a destra {player.LeanRoomPublic(1f):F2}");
            GameManager.CamShot(dir + "/lean_muro.png", null, true);
            Destroy(wall);
            PlayerController.debugLean = float.NaN;
        }
    }
}
#endif
