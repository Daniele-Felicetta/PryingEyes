#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.IO;
using PryingEyes.Testing;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.Deserto
{
    public sealed partial class DesertTest
    {
        void SwimShot(string name, int width = 1600, int height = 900)
        {
            if (!shots) return;
            string folder = TestRun.OutDir("desertotest");
            renderFrame = Time.frameCount;
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, false, width, height);
        }

        IEnumerator SwimFilm(string shot, int count)
        {
            if (!shots || !TestRun.Has("-swimsequence")) yield break;
            float oldCapture = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 24f;
                for (int i = 0; i < count; i++)
                {
                    yield return null;
                    SwimShot($"{shot}_{i:000}", 768, 432);
                }
                yield return null;
            }
            finally { Time.captureDeltaTime = oldCapture; }
        }

        void CheckSwimWire()
        {
            var writer = new NetWriter();
            var original = new PlayerNet { swimming = true, swimPhase = 0.974f, swimEffort = 0.52f, heading = -0.85f, gun = 255 };
            original.Write(writer);
            var reader = new NetReader(writer.ToArray());
            var copy = PlayerNet.Read(ref reader);
            Check("co-op keeps the stroke phase, effort and traversal flags",
                reader.Ok && reader.Left == 0 && copy.swimming && !copy.gliding && copy.gun == 255 &&
                Mathf.Abs(original.swimPhase - copy.swimPhase) < 1f / 256f &&
                Mathf.Abs(original.swimEffort - copy.swimEffort) < 0.004f);
        }

        IEnumerator SwimTest()
        {
            var player = Pl;
            int perspective = GameSettings.thirdPersonOverride;
            Vector3? framing = GameSettings.thirdPersonFramingOverride;
            bool heldLook = PlayerController.holdLook;
            var lakes = DesertWorld.Lakes;
            Check("two lakes generate around the cathedral", lakes.Count == 2, $"{lakes.Count} lakes");
            CheckSwimWire();
            if (lakes.Count < 2) yield break;
            try
            {
                var lake = lakes[0];
                var bank = lake.PointAt(-Mathf.PI / 2f, 1.13f);
                GameSettings.thirdPersonOverride = 0;
                GameSettings.thirdPersonFramingOverride = GameSettings.DefaultThirdPersonFraming;
                PlayerController.holdLook = true;
                if (shots) DesertSky.I?.SetHour(11.5f, true);
                yield return GoTo(bank.x, bank.z, 3f);
                player.yaw = 0f;
                player.pitch = 0.17f;
                var surface = System.Array.Find(FindObjectsByType<DesertLakeSurface>(FindObjectsSortMode.None),
                    item => Mathf.Abs(item.transform.position.x - lake.X) < 0.01f);
                if (shots)
                {
                    yield return new WaitForSeconds(0.24f);
                    player.weapon.DebugHideView(true);
                    try { SwimShot("nuoto_riva"); }
                    finally { player.weapon.DebugHideView(false); }
                }
                Check("the dry bank uses the walking controller", !player.Swimming && player.GetComponent<CharacterController>().isGrounded);
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    Check("near the bank the camera supplies scene colour for refraction",
                        player.cam.TryGetComponent(out UniversalAdditionalCameraData data) && data.requiresColorTexture);
                    if (TestRun.Has("-outdoorperf")) yield return PerfAt("Lake shore", 1.5f);
                }
                player.parkourTestDirection = Vector3.forward;
                yield return WaitFor(() => player.Swimming, 10f);
                Check("walking into deep water starts swimming", player.Swimming, player.transform.position.ToString("F2"));
                yield return new WaitForSeconds(1.15f);
                var left = player.cam.Find("Parkour hands/Left wrist");
                var right = player.cam.Find("Parkour hands/Right wrist");
                int contactsBefore = surface != null ? surface.Contacts : 0;
                SwimShot("nuoto_prima_a");
                float[] minHand = { float.MaxValue, float.MaxValue }, maxHand = { float.MinValue, float.MinValue };
                bool armsShown = left != null && right != null;
                for (int i = 0; i < 16 && armsShown; i++)
                {
                    yield return new WaitForSeconds(0.076f);
                    float ly = left.position.y - lake.Surface, ry = right.position.y - lake.Surface;
                    minHand[0] = Mathf.Min(minHand[0], ly); minHand[1] = Mathf.Min(minHand[1], ry);
                    maxHand[0] = Mathf.Max(maxHand[0], ly); maxHand[1] = Mathf.Max(maxHand[1], ry);
                    armsShown = left.gameObject.activeInHierarchy && right.gameObject.activeInHierarchy;
                }
                Check("first-person wrists move through opposing strokes with the weapon stowed",
                    armsShown && minHand[0] < -0.09f && minHand[1] < -0.09f && maxHand[0] > 0.07f && maxHand[1] > 0.07f &&
                    player.weapon.ParkourStow > 0.9f, $"wrists relative to water {minHand[0]:F2}..{maxHand[0]:F2} / {minHand[1]:F2}..{maxHand[1]:F2} m");
                Check("alternate catches leave separate water impacts", surface != null && surface.Contacts >= contactsBefore + 2,
                    $"{(surface != null ? surface.Contacts - contactsBefore : 0)} impacts in a stroke cycle");
                SwimShot("nuoto_prima_b");
                yield return SwimFilm("prima", 32);

                GameSettings.thirdPersonOverride = 1;
                player.yaw = 0.27f;
                player.pitch = 0.21f;
                yield return new WaitForSeconds(0.42f);
                SwimShot("nuoto_terza_a");
                var avatar = FindFirstObjectByType<SeatAvatar>();
                var body = avatar != null ? avatar.Body : null;
                float blend = ThirdPersonView.Of(player)?.Blend ?? 0f;
                float reach = body != null ? body.Head.position.z - body.Hips.position.z : 0f;
                float headHeight = body != null ? body.Head.position.y - lake.Surface : float.NegativeInfinity;
                Check("third person renders the swimming survivor without a weapon",
                    blend > 0.95f && body != null && body.input.swimming && !body.HasWeapon && reach > 0.24f && headHeight > -0.02f,
                    $"view {blend:F2}, head forward {reach:F2} m, head above water {headHeight:F2} m");
                yield return new WaitForSeconds(0.3f);
                SwimShot("nuoto_terza_b");
                yield return SwimFilm("terza", 32);

                player.parkourTestDirection = Vector3.right;
                yield return new WaitForSeconds(0.38f);
                Check("a change of direction banks the swimmer and keeps continuity of movement",
                    player.Swimming && player.SwimTurn > 0.18f && player.SpeedH > 1.2f,
                    $"turn {player.SwimTurn:F2}, speed {player.SpeedH:F2} m/s");

                player.parkourTestDirection = Vector3.zero;
                yield return new WaitForSeconds(0.9f);
                Check("at rest the swimmer floats at the surface and treads water", player.Swimming &&
                    player.SpeedH < 0.10f && player.SwimEffort < 0.08f &&
                    Mathf.Abs(player.cam.position.y - lake.Surface - 0.30f) < 0.15f, $"eye {player.cam.position.y - lake.Surface:F2} m above water");

                Vector3 toBank = new Vector3(bank.x - player.transform.position.x, 0f, bank.z - player.transform.position.z).normalized;
                player.parkourTestDirection = toBank;
                yield return WaitFor(() => !player.Swimming && player.transform.position.z < bank.z, 18f);
                Check("swimming back up the bank restores walking and the weapon",
                    !player.Swimming && player.GetComponent<CharacterController>().isGrounded && player.weapon.ParkourStow < 0.1f);

                player.parkourTestDirection = Vector3.zero;
                if (shots)
                {
                    GameSettings.thirdPersonOverride = 0;
                    player.Teleport(new Vector3(lake.X - 1.15f, lake.Surface + 1.4f, lake.Z - 5.1f));
                    player.yaw = 0.15f;
                    player.pitch = 0.10f;
                    player.swimTestVertical = 1f;
                    yield return WaitFor(() => player.Swimming && lake.Surface - player.cam.position.y >= 3.9f, 8f);
                    player.swimTestVertical = 0f;
                    yield return new WaitForSeconds(0.35f);
                    SwimShot("nuoto_arcata");
                    player.parkourTestDirection = Vector3.forward * 0.46f;
                    yield return SwimFilm("arcata", 30);
                    player.parkourTestDirection = Vector3.zero;
                }
                var otherLake = lakes[1];
                GameSettings.thirdPersonOverride = 0;
                player.Teleport(new Vector3(otherLake.X - 2.25f, otherLake.Surface + 3f, otherLake.Z - 3.8f));
                yield return WaitFor(() => player.Swimming, 7f);
                Check("falling into the second lake also starts swimming", player.Swimming);
                SwimShot("nuoto_ingresso");
                yield return SwimFilm("ingresso", 27);

                player.yaw = 0.05f;
                player.pitch = 0.28f;
                player.swimTestVertical = 1f;
                yield return WaitFor(() => otherLake.Surface - player.cam.position.y >= 2.8f, 7f);
                player.swimTestVertical = 0f;
                float diveY = player.cam.position.y;
                yield return new WaitForSeconds(0.75f);
                Check("the player can dive, stay underwater and hold depth",
                    player.Swimming && player.SwimSubmersion > 0.95f && player.cam.position.y < otherLake.Surface - 2.45f &&
                    Mathf.Abs(player.cam.position.y - diveY) < 0.68f, $"eye {otherLake.Surface - player.cam.position.y:F2} m below water");
                var gardens = FindObjectsByType<DesertLakeLife>(FindObjectsSortMode.None);
                var reef = System.Array.Find(gardens, item => Mathf.Abs(item.transform.position.x - otherLake.X) < 0.01f);
                Check("both basins have distinct submerged landmarks, rooted vegetation and live fish",
                    gardens.Length == 2 && System.Array.TrueForAll(gardens, item => item.Landmark != null && item.FishCount >= 40 && item.ReefPatches >= 70) &&
                    reef != null && reef.Landmark.name == "Drowned courtyard" && reef.Landmark.GetComponentsInChildren<Collider>().Length >= 12);
                var bed = new Vector3(otherLake.X, otherLake.Surface - DesertLake.Depth, otherLake.Z);
                yield return WaitFor(() => DesertNav.ReadyAround(bed, 8f), 14f);
                Check("ground enemies have no path on the deep lake bed",
                    DesertNav.ReadyAround(bed, 8f) && !NavMesh.SamplePosition(bed, out _, 1.6f, NavMesh.AllAreas));
                SwimShot("nuoto_fondale_prima");
                player.parkourTestDirection = Vector3.forward * 0.60f;
                yield return SwimFilm("fondale_prima", 26);
                GameSettings.thirdPersonOverride = 1;
                player.pitch = 0.34f;
                yield return new WaitForSeconds(0.46f);
                Vector3 underwaterStart = player.transform.position;
                yield return new WaitForSeconds(0.45f);
                Check("the third-person swimmer travels underwater with the submerged pose",
                    player.Swimming && player.SwimSubmersion > 0.95f && player.transform.position.z > underwaterStart.z + 0.24f &&
                    ThirdPersonView.Of(player)?.Blend > 0.95f);
                SwimShot("nuoto_fondale_terza");
                yield return SwimFilm("fondale_terza", 26);
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && TestRun.Has("-outdoorperf"))
                    yield return PerfAt("Lake bed", 0.35f);

                player.parkourTestDirection = Vector3.zero;
                player.swimTestVertical = -1f;
                yield return WaitFor(() => player.cam.position.y > otherLake.Surface + 0.15f, 8f);
                player.swimTestVertical = 0f;
                yield return new WaitForSeconds(0.48f);
                Check("ascent restores the surface stroke and clears the underwater filter",
                    player.Swimming && player.SwimSubmersion < 0.08f && player.cam.position.y > otherLake.Surface + 0.22f &&
                    (!player.cam.TryGetComponent(out AudioLowPassFilter lowpass) || !lowpass.enabled));
                SwimShot("nuoto_riemersione");
            }
            finally
            {
                player.parkourTestDirection = null;
                player.swimTestVertical = 0f;
                PlayerController.holdLook = heldLook;
                GameSettings.thirdPersonOverride = perspective;
                GameSettings.thirdPersonFramingOverride = framing;
                if (DesertSky.I != null) DesertSky.I.Frozen = false;
            }
        }
    }
}
#endif
