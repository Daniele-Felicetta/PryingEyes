#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Testing;
using UnityEngine;

namespace PryingEyes.Deserto
{
    public sealed partial class DesertTest
    {
        static bool IsOpenGround(Vector2 at, List<Site> sites, float clearance)
        {
            DesertWorld.SitesNear(at.x, at.y, clearance + 20f, sites);
            foreach (var site in sites)
                if (at.x > site.fx0 - clearance && at.x < site.fx1 + clearance &&
                    at.y > site.fz0 - clearance && at.y < site.fz1 + clearance) return false;
            float y = DesertWorld.Height(at.x, at.y);
            return Mathf.Abs(y - DesertWorld.Height(at.x + 2f, at.y)) < 0.75f &&
                Mathf.Abs(y - DesertWorld.Height(at.x, at.y + 2f)) < 0.75f;
        }

        static Vector2 ViewOf(Biome kind, bool slick = false)
        {
            var (x, z) = DesertBiomes.Landmark(kind);
            var sites = new List<Site>();
            Vector2 centre = new Vector2(x, z);
            for (int i = 0; i < 64; i++)
            {
                float angle = i * 2.399963f;
                Vector2 at = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Mathf.Sqrt(i) * 9f);
                var climate = DesertBiomes.At(at.x, at.y, DesertWorld.Seed);
                if (climate.Dominant != kind || !IsOpenGround(at, sites, slick ? 4f : 14f)) continue;
                if (!slick || DesertBiomes.IceSheet(at.x, at.y, DesertWorld.Seed, climate) > 0.85f) return at;
            }
            return centre;
        }

        IEnumerator WalkIntoBiome(PlayerController player, Biome kind)
        {
            var sites = new List<Site>();
            Vector2 before = default, after = default;
            Vector2 direction = kind == Biome.Forest ? Vector2.right : kind == Biome.Meadow ? Vector2.left : Vector2.up;
            Vector2 across = new Vector2(direction.y, direction.x);
            bool found = false;
            for (int band = 0; band < 16 && !found; band++)
            {
                Vector2 lateral = across * (80f + band * 18f);
                for (float d = 390f; d <= 840f; d += 8f)
                {
                    Vector2 middle = direction * d + lateral;
                    before = middle - direction * 24f;
                    after = middle + direction * 24f;
                    if (DesertBiomes.At(before.x, before.y, DesertWorld.Seed).Dominant != Biome.Desert ||
                        DesertBiomes.At(after.x, after.y, DesertWorld.Seed).Dominant != kind) continue;
                    found = true;
                    for (int step = 0; step <= 12 && found; step++)
                    {
                        Vector2 at = before + direction * (step * 4f);
                        found = IsOpenGround(at, sites, 5f) && !DesertWorld.LakeAt(at.x, at.y, out _, out _);
                    }
                    if (found) break;
                }
            }
            Check($"the world has a walkable desert-to-{kind} boundary", found);
            if (!found) yield break;
            yield return GoTo(before.x, before.y, 3f);
            player.yaw = Mathf.Atan2(direction.x, direction.y);
            player.pitch = 0.025f;
            player.parkourTestDirection = new Vector3(direction.x, 0f, direction.y);
            bool Entered()
            {
                Vector3 at = player.transform.position;
                return Vector2.Dot(new Vector2(at.x, at.z) - before, direction) > 26f &&
                    DesertBiomes.At(at.x, at.z, DesertWorld.Seed).Dominant == kind && player.FootSurface == (kind == Biome.Ice ? 5 : 4);
            }
            yield return WaitFor(Entered, 22f);
            player.parkourTestDirection = Vector3.zero;
            Check($"the player crosses from desert to {kind} on foot", Entered(),
                $"from {before:F1} toward {after:F1}, reached {player.transform.position:F1}, ground step {player.FootSurface}");
        }

        IEnumerator BiomeTest()
        {
            var player = Pl;
            bool heldLook = PlayerController.holdLook;
            int perspective = GameSettings.thirdPersonOverride;
            PlayerController.holdLook = true;
            GameSettings.thirdPersonOverride = 0;
            try
            {
                var start = player.transform.position;
                Check("the cathedral starts in the desert", DesertBiomes.At(start.x, start.z, DesertWorld.Seed).Dominant == Biome.Desert);
                if (DesertScatter.CanDraw)
                    Check("the outdoor ground atlas is bound to the terrain material", DesertTerrain.I != null && DesertTerrain.I.GroundDetailReady);
                foreach (Biome kind in new[] { Biome.Forest, Biome.Ice, Biome.Meadow })
                {
                    yield return WalkIntoBiome(player, kind);
                    Vector2 at = ViewOf(kind, kind == Biome.Ice);
                    yield return GoTo(at.x, at.y, 4f);
                    var p = player.transform.position;
                    var scatter = DesertScatter.I;
                    Prop focus = kind == Biome.Forest ? Prop.Broadleaf : kind == Biome.Ice ? Prop.GlacierShard : Prop.Wildflowers;
                    yield return WaitFor(() => scatter != null && scatter.InstancesNear(focus, p, 120f, out _) >= 10, 20f);
                    Vector3 nearest = p;
                    int scenery = scatter != null ? scatter.InstancesNear(focus, p, 120f, out nearest) : 0;
                    int surface = kind == Biome.Ice ? 5 : 4;
                    Check(kind + " streams terrain and its own scenery",
                        DesertBiomes.At(p.x, p.z, DesertWorld.Seed).Dominant == kind && scenery >= 10 &&
                        player.GetComponent<CharacterController>().isGrounded && player.FootSurface == surface,
                        $"position {p:F1}, {scenery} {focus}, step {player.FootSurface}");

                    if (DesertScatter.CanDraw && kind != Biome.Meadow)
                        Check(kind + " renders textured foliage", scatter != null && scatter.BiomeCanopiesReady);
                    float hour = kind == Biome.Forest ? 9.55f : kind == Biome.Ice ? 12.3f : 16.3f;
                    var view = nearest - p;
                    if (kind == Biome.Forest)
                    {
                        float sideView = Mathf.Atan2(view.x, view.z) * Mathf.Rad2Deg + 85f;
                        view = Quaternion.Euler(0f, sideView, 0f) * Vector3.forward * 20f;
                        for (int angle = 0; angle <= 180; angle += 30)
                        {
                            var direction = Quaternion.Euler(0f, sideView + angle, 0f) * Vector3.forward;
                            if (Physics.Raycast(p + Vector3.up * 1.5f, direction, 17f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                            view = direction * 20f;
                            break;
                        }
                    }
                    if (view.sqrMagnitude < 4f) view = Vector3.forward;
                    player.yaw = Mathf.Atan2(view.x, view.z);
                    player.pitch = kind == Biome.Meadow ? 0.16f : 0.04f;
                    DesertSky.I?.SetHour(hour, true);
                    yield return new WaitForSeconds(0.35f);
                    if (DesertScatter.CanDraw && TestRun.Has("-outdoorperf")) yield return PerfAt(kind.ToString(), 1.5f);
                    if (shots)
                    {
                        BiomePhoto(player, $"biome_{kind}");
                        if (kind == Biome.Ice)
                        {
                            player.yaw = 0.10f;
                            player.pitch = -0.08f;
                            yield return new WaitForSeconds(0.25f);
                            BiomePhoto(player, "biome_Ice_ridges");
                        }
                    }
                }
            }
            finally
            {
                player.parkourTestDirection = null;
                PlayerController.holdLook = heldLook;
                GameSettings.thirdPersonOverride = perspective;
                if (DesertSky.I != null) DesertSky.I.Frozen = false;
            }
        }

        void BiomePhoto(PlayerController player, string name)
        {
            renderFrame = Time.frameCount;
            player.weapon.DebugHideView(true);
            try { GameManager.CamShot(Path.Combine(TestRun.OutDir("desertotest"), name + ".png"), null, false, 1600, 900); }
            finally { player.weapon.DebugHideView(false); }
        }
    }
}
#endif
