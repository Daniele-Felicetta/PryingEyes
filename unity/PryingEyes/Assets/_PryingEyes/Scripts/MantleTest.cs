#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -mantletest [-mantleshots]: scavalcare e arrampicarsi (PlayerController.Mantle.cs).
    /// Nell'arena si mettono ostacoli veri: su un tavolo da 90 cm (profondo 1,4 m) si sale e si resta in piedi sopra; un muretto da
    /// 1 m spesso 25 cm si scavalca e si atterra dall'altra parte; un muro da 2,2 m e un gradino da 25 cm non chiedono lo scavalco.
    /// Ogni superamento dura meno di 0,7 s, la visuale si china e torna dritta, poi si cammina di nuovo.</summary>
    public sealed class MantleTest : PlayerTest
    {
        protected override string Suite { get { return "mantletest"; } }
        protected override string PhotoFlag { get { return "-mantleshots"; } }
        protected override string OutFolder { get { return "scavalco"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<MantleTest>("-mantletest"); }

        static GameObject Box(string name, Vector3 baseCenter, Vector3 size, Vector3 forward)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = baseCenter + Vector3.up * size.y * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            go.transform.localScale = size;
            Physics.SyncTransforms();
            return go;
        }

        IEnumerator Over(PlayerController player, Action<float> done)
        {
            float t0 = Time.time;
            while (player.Mantling && Time.time - t0 < 2f) yield return null;
            done(Time.time - t0);
        }

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            if (Failed > 0) yield break;
            player.invulnerable = true;
            float yawF = Mathf.Atan2(forward.x, forward.z);

            // 1) tavolo da 90 cm, profondo 1,4 m: ci si sale sopra
            var table = Box("Tavolo di prova", arena + forward * 1.5f, new Vector3(1.6f, 0.9f, 1.4f), forward);
            player.Teleport(arena + forward * 0.2f + Vector3.up * 0.05f);
            player.yaw = yawF; player.pitch = 0.15f;
            yield return null; yield return null;
            bool ok = player.TryMantle(forward);
            Check("davanti al tavolo da 90 cm si sale sopra", ok && !player.LastMantleVault);
            yield return new WaitForSeconds(0.12f);
            Photo("tavolo");
            float took = 9f;
            yield return Over(player, t => took = t + 0.12f);
            Vector3 p = player.transform.position;
            float along = Vector3.Dot(p - (arena + forward * 1.5f), forward);
            Check($"in {took:F2} s, in piedi sul tavolo (quota {p.y - arena.y:F2} m, {along:F2} m dal centro)",
                  took < 0.7f && Mathf.Abs(p.y - arena.y - 0.9f) < 0.1f && Mathf.Abs(along) < 0.7f);
            yield return new WaitForSeconds(0.2f);
            Check("poi la visuale torna dritta e si sta fermi", !player.Mantling && (Mathf.Abs(player.cam.eulerAngles.z) < 0.5f || Mathf.Abs(player.cam.eulerAngles.z - 360f) < 0.5f));
            DestroyImmediate(table);
            Physics.SyncTransforms();

            // 2) muretto da 1 m spesso 25 cm: si scavalca e si atterra dall'altra parte
            var wall = Box("Muretto di prova", arena + forward * 1.2f, new Vector3(2.2f, 1.0f, 0.25f), forward);
            player.Teleport(arena + forward * 0.35f + Vector3.up * 0.05f);
            player.yaw = yawF; player.pitch = 0.1f;
            yield return null; yield return null;
            ok = player.TryMantle(forward);
            Check("davanti al muretto da 1 m (25 cm) si scavalca", ok && player.LastMantleVault);
            yield return new WaitForSeconds(0.14f);
            Photo("muretto");
            took = 9f;
            yield return Over(player, t => took = t + 0.14f);
            p = player.transform.position;
            along = Vector3.Dot(p - (arena + forward * 1.2f), forward);
            Check($"in {took:F2} s, a terra dall'altra parte (quota {p.y - arena.y:F2} m, {along:F2} m oltre)",
                  took < 0.7f && Mathf.Abs(p.y - arena.y) < 0.12f && along > 0.3f);
            DestroyImmediate(wall);
            Physics.SyncTransforms();

            // 3) muro da 2,2 m e gradino da 25 cm: niente scavalco
            var high = Box("Muro di prova", arena + forward * 1.2f, new Vector3(3f, 2.2f, 0.3f), forward);
            player.Teleport(arena + forward * 0.35f + Vector3.up * 0.05f);
            player.yaw = yawF;
            yield return null; yield return null;
            Check("il muro da 2,2 m non si scavalca", !player.TryMantle(forward));
            DestroyImmediate(high);
            var step = Box("Gradino di prova", arena + forward * 1.2f, new Vector3(1.6f, 0.25f, 1.0f), forward);
            player.Teleport(arena + forward * 0.35f + Vector3.up * 0.05f);
            yield return null; yield return null;
            Check("il gradino da 25 cm si sale col passo, senza scavalco", !player.TryMantle(forward));
            DestroyImmediate(step);
            Physics.SyncTransforms();
            player.invulnerable = false;
        }
    }
}
#endif
