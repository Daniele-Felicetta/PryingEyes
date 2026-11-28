#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Testing
{
    public sealed class GrappleTest : PlayerTest
    {
        protected override string Suite => "grappletest";
        protected override string PhotoFlag => "-grappleshots";
        protected override float TimeLimit => 90f;
        protected override bool HoldFire => true;
        Material stone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() => InstallWhen<GrappleTest>("-grappletest");

        GameObject Solid(Vector3 at, Vector3 scale)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.position = at;
            box.transform.localScale = scale;
            if (stone != null) box.GetComponent<Renderer>().sharedMaterial = stone;
            Physics.SyncTransforms();
            return box;
        }

        static IEnumerator Until(Func<bool> ready, float seconds)
        {
            float end = Time.time + seconds;
            while (!ready() && Time.time < end) yield return null;
        }

        static void Aim(PlayerController player, Vector3 target, bool launch = true)
        {
            Vector3 aim = (target - player.cam.position).normalized;
            player.yaw = Mathf.Atan2(aim.x, aim.z);
            player.pitch = -Mathf.Asin(aim.y);
            player.grappleTestPressed = launch;
        }

        static void Pad(ushort buttons = 0, bool fire = false) => PadControls.Inject(new PadControls.Raw
        {
            connected = true, buttons = buttons, leftTrigger = 255, rightTrigger = fire ? (byte)255 : (byte)0
        });

        protected override void Finish()
        {
            PadControls.Inject(null);
            PadControls.Disable();
        }

        protected override IEnumerator Run()
        {
            while (FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) yield return null;
            PlayerController player = GameManager.I.player;
            GameSettings.thirdPersonOverride = 0;
            PlayerController.holdLook = true;
            PlayerController.botDir = Vector3.zero;
            Vector3 origin = new Vector3(0f, 160f, 0f);
            if (Photos)
            {
                stone = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                stone.SetColor("_BaseColor", new Color(0.24f, 0.21f, 0.17f));
                Light light = new GameObject("Grapple test light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(32f, 160f, 0f);
            }
            Solid(origin - Vector3.up * 0.5f, new Vector3(100f, 1f, 100f));
            GameObject wall = Solid(origin + new Vector3(0f, 4f, 23f), new Vector3(12f, 8f, 1f));
            player.Teleport(origin + Vector3.up * 0.04f);
            yield return null;
            yield return null;
            Aim(player, origin + new Vector3(0f, 5f, 22.5f));
            yield return Until(() => player.GrappleAttached, 1f);
            Check("Hook attaches to a wall in front of the player", player.GrappleAttached);
            Check("Hook tip is on the near face", Mathf.Abs(player.GrappleTip.z - 22.5f) < 0.1f);
            yield return Until(() => player.SpeedH >= 8f, 1f);
            Check("Winch accelerates the player toward the wall", player.SpeedH >= 8f && player.transform.position.z > 0.6f);
            float steerFrom = player.transform.position.x;
            player.parkourTestDirection = Vector3.right;
            yield return Until(() => player.transform.position.x - steerFrom > 0.22f || !player.GrappleAttached, 0.55f);
            float steered = player.transform.position.x - steerFrom;
            Check("Steering while reeling changes the approach", steered > 0.22f && player.GrappleAttached, $"Moved {steered:F2} m to the right");
            player.parkourTestDirection = null;
            Photo("01-wall-cable");
            float z = player.transform.position.z;
            player.grappleTestPressed = true;
            yield return new WaitForSeconds(0.13f);
            Check("Releasing keeps forward momentum", !player.GrappleAttached && player.transform.position.z > z + 0.65f);

            player.Teleport(origin + Vector3.up * 0.04f);
            yield return null;
            Aim(player, origin + new Vector3(0f, 5f, 22.5f));
            yield return Until(() => player.GrappleAttached, 1f);
            wall.transform.position += Vector3.right * 1.25f;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Check("A second shot follows its moving surface", player.GrappleAttached && Mathf.Abs(player.GrappleTip.x - 1.25f) < 0.12f);
            Solid(origin + new Vector3(0f, 3f, 7f), new Vector3(5f, 6f, 0.6f));
            yield return Until(() => !player.GrappleAttached, 0.4f);
            Check("A newly occluded cable disconnects", !player.GrappleAttached && player.transform.position.z < 6f);

            Solid(origin + new Vector3(0f, 13f, 0.5f), new Vector3(5f, 1f, 5f));
            player.Teleport(origin + Vector3.up * 0.04f);
            yield return null;
            Aim(player, origin + new Vector3(0f, 12.5f, 0.6f));
            yield return Until(() => player.GrappleAttached, 1f);
            Check("The hook can attach overhead", player.GrappleAttached && player.GrappleTip.y > origin.y + 12f);
            yield return Until(() => !player.GrappleAttached, 2f);
            Check("The capsule reaches the ceiling without passing through it", !player.GrappleAttached &&
                player.transform.position.y > origin.y + 10f && player.transform.position.y + player.BodyHeight <= origin.y + 12.56f);

            Aim(player, origin + new Vector3(0f, 0f, -2f));
            yield return Until(() => player.GrappleAttached, 1f);
            Check("The hook can attach to the ground during a fall", player.GrappleAttached && Mathf.Abs(player.GrappleTip.y - origin.y) < 0.1f);
            player.Teleport(origin + Vector3.up * 0.04f);
            Check("Teleport clears the cable immediately", !player.GrappleVisible);

            GameSettings.thirdPersonOverride = 1;
            player.Teleport(origin + new Vector3(0f, 0.04f, 14f));
            yield return new WaitForSeconds(0.25f);
            Aim(player, origin + new Vector3(1.25f, 4f, 22.5f));
            yield return Until(() => player.GrappleAttached, 1f);
            yield return null;
            SurvivorBody body = FindAnyObjectByType<SeatAvatar>()?.Body;
            Check("The third-person survivor keeps the gun and carries the harness winch", player.GrappleAttached &&
                body != null && body.HasWeapon && player.transform.Find("Grapple/Grapple winch")?.gameObject.activeInHierarchy == true);
            Photo("02-third-person");

            GameSettings.thirdPersonOverride = 0;
            Vector3 combatStart = origin + new Vector3(-20f, 0.04f, -18f);
            Vector3 combatTarget = origin + new Vector3(-20f, 4f, 25f);
            Solid(combatTarget, new Vector3(6f, 8f, 1f));
            player.Teleport(combatStart);
            yield return null;
            Aim(player, combatTarget - Vector3.forward * 0.5f, launch: false);
            Weapon gun = player.weapon;
            Pad();
            yield return Until(() => gun.ads >= 0.95f, 1f);
            Pad(PadControls.RightStick);
            yield return Until(() => player.GrappleVisible, 1f);
            Check("The controller can launch the hook without dropping weapon aim",
                player.GrappleVisible && !player.GrappleAttached && gun.ads >= 0.95f);

            float shotAt = gun.LastShotTime;
            Pad(fire: true);
            yield return Until(() => gun.LastShotTime > shotAt, 0.18f);
            Check("The gun fires while the hook is in flight",
                player.GrappleVisible && !player.GrappleAttached && gun.LastShotTime > shotAt);
            Pad();
            yield return Until(() => player.GrappleAttached && gun.CanFireNow, 1f);
            shotAt = gun.LastShotTime;
            Pad(fire: true);
            yield return Until(() => gun.LastShotTime > shotAt, 0.18f);
            Check("The gun fires in aim while the winch pulls",
                player.GrappleAttached && gun.ads >= 0.95f && gun.LastShotTime > shotAt);
            Photo("03-grapple-and-aim");

            Pad(PadControls.X);
            yield return null;
            yield return null;
            Pad();
            yield return Until(() => gun.reloading, 0.25f);
            Check("Reload starts with the cable under tension", player.GrappleAttached && gun.reloading);
            player.grappleTestPressed = true;
            yield return null;
            yield return null;
            Check("Releasing the cable preserves a reload already in progress", !player.GrappleAttached && gun.reloading);

            var writer = new NetWriter();
            new PlayerNet { grappling = true, grappleAttached = true, grappleTip = origin }.Write(writer);
            new PlayerNet { gliding = true }.Write(writer);
            var reader = new NetReader(writer.ToArray());
            PlayerNet shot = PlayerNet.Read(ref reader), glide = PlayerNet.Read(ref reader);
            Check("Co-op snapshots keep hook position and the following snapshot",
                shot.grappling && shot.grappleAttached && shot.grappleTip == origin && glide.gliding && !glide.grappling);
        }
    }
}
#endif
