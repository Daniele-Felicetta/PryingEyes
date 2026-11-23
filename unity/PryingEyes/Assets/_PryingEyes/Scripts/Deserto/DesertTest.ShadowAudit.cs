#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Production.Rendering;
using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    public sealed partial class DesertTest
    {
        const float ShadowJump = 0.18f;

        IEnumerator ScanDesertShadows()
        {
            var sky = DesertSky.I;
            var sun = RenderSettings.sun;
            bool canRender = sky != null && sun != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            Check("shadow-audit: graphics device and directional light available", canRender);
            if (!canRender) yield break;

            var args = Environment.GetCommandLineArgs();
            var center = new Vector3((HubPlan.StairX0 + HubPlan.StairX1) * 0.5f, DesertWorld.GroundY + 0.3f, -5.5f);
            var routes = new List<(string name, Vector3 center, float near, float far)>();
            int siteArg = Array.IndexOf(args, "-shadow-site");
            if (siteArg >= 0)
            {
                var cell = siteArg + 1 < args.Length ? args[siteArg + 1].Split(',') : Array.Empty<string>();
                Site site = cell.Length == 2 && int.TryParse(cell[0], out int x) && int.TryParse(cell[1], out int z)
                    ? DesertWorld.SiteOf(x, z) : null;
                Check("shadow-audit: requested site exists", site != null, "use -shadow-site CELL_X,CELL_Z");
                if (site == null) yield break;
                routes.Add(ShadowRoute(site));
            }
            else
            {
                routes.Add(("cathedral-stair", center, 7f, 20f));
                var sites = new List<Site>();
                DesertWorld.SitesNear(center.x, center.z, 800f, sites);
                sites.RemoveAll(s => s.IsCatacomb);
                float DistanceSquared(Site s) => (new Vector2(s.CentreX - center.x, s.CentreZ - center.z)).sqrMagnitude;
                sites.Sort((a, b) => DistanceSquared(a).CompareTo(DistanceSquared(b)));
                for (int i = 0; i < Math.Min(sites.Count, 2); i++) routes.Add(ShadowRoute(sites[i]));
            }

            float hour = FlatArg("-shadow-hour", -1f);
            var hours = hour < 0f ? new[] { 8f, 16.5f } : new[] { Mathf.Repeat(hour, 24f) };
            int reportArg = Array.IndexOf(args, "-shadow-report");
            string report = reportArg >= 0 && reportArg + 1 < args.Length ? args[reportArg + 1] : TestPaths.Out("deserto/shadow-audit.csv");
            string reportDir = Path.GetDirectoryName(Path.GetFullPath(report));
            Directory.CreateDirectory(reportDir);
            var rows = new StringBuilder("seed,site,hour,previous_pose,pose,x,y,z,camera_x,camera_y,camera_z,yaw,pitch,previous_shadow,shadow\n");

            PlayerController.holdLook = true;
            ClearHorde();
            using var probe = new RenderedShadowProbe(Pl.cam.GetComponent<Camera>());
            sky.SetHour(11f, true);
            GameSettings.SetShadowRange(DesertSky.OutdoorShadowRange);
            renderFrame = Time.frameCount;
            bool sensitive = CheckShadowProbe(probe, sun, center);
            Check("shadow-audit: detects a known shadow being removed", sensitive);
            if (!sensitive) { sky.Frozen = false; yield break; }
            yield return null;

            foreach (var route in routes)
            {
                Vector3 origin = ShadowView(route.center, route.near, 0f);
                yield return GoTo(origin.x, origin.z, 0.1f);
                ClearHorde();
                probe.SetView(origin, route.center - origin);
                var receivers = probe.FindReceivers(HotelNav.EnvMask, route.far + 100f);
                Check("shadow-audit: route has visible receivers", receivers.Length > 0, $"{route.name}: {receivers.Length}");

                foreach (float lightHour in hours)
                {
                    sky.SetHour(lightHour, true);
                    float[] previous = null;
                    int changes = 0, comparisons = 0;
                    for (int i = 0; i < 13; i++)
                    {
                        float t = i / 12f;
                        float distance = Mathf.Lerp(route.near, route.far, t);
                        var eye = ShadowView(route.center, distance, Mathf.Lerp(-18f, 18f, t));
                        // The real player drives streaming and distance-based LOD while the probe samples a stable view.
                        Pl.Teleport(eye - Vector3.up * 1.7f);
                        yield return new WaitForSecondsRealtime(0.25f);
                        float pan = Mathf.Lerp(-20f, 20f, t);
                        Vector3 direction = Quaternion.Euler(0f, pan, 0f) * (route.center - eye);
                        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                        float pitch = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
                        probe.SetView(eye, direction);
                        renderFrame = Time.frameCount;
                        var current = probe.Sample(sun, receivers, HotelNav.EnvMask);
                        if (previous != null)
                            for (int p = 0; p < current.Length; p++)
                            {
                                if (float.IsNaN(previous[p]) || float.IsNaN(current[p])) continue;
                                comparisons++;
                                if (Mathf.Abs(current[p] - previous[p]) < ShadowJump) continue;
                                Vector3 point = receivers[p].point;
                                rows.AppendLine(FormattableString.Invariant(
                                    $"{DesertWorld.Seed},{route.name},{lightHour:F2},{i - 1},{i},{point.x:F2},{point.y:F2},{point.z:F2},{eye.x:F2},{eye.y:F2},{eye.z:F2},{yaw:F2},{pitch:F2},{previous[p]:F3},{current[p]:F3}"));
                                changes++;
                            }
                        previous = current;
                        yield return null;
                    }
                    Check("shadow-audit: route has comparable views", comparisons > 0);
                    Debug.Log($"[PryingEyes] shadow-audit: {route.name}, hour {lightHour:F2}, {comparisons} comparisons, {changes} candidates");
                }
            }

            sky.Frozen = false;
            File.WriteAllText(report, rows.ToString());
            Debug.Log($"[PryingEyes] shadow-audit: report {report}");
        }

        static (string, Vector3, float, float) ShadowRoute(Site site)
        {
            float edge = (site.fz1 - site.fz0) * 0.5f;
            return ($"{site.kind}_{site.cx}_{site.cz}", new Vector3(site.CentreX, site.padY + 2f, site.CentreZ), edge + 16f, edge + 80f);
        }

        static Vector3 ShadowView(Vector3 center, float distance, float angle)
        {
            var p = center + Quaternion.Euler(0f, angle, 0f) * (Vector3.back * distance);
            p.y = DesertWorld.Height(p.x, p.z) + 1.8f;
            return p;
        }

        static bool CheckShadowProbe(RenderedShadowProbe probe, Light sun, Vector3 origin)
        {
            var point = origin + 50f * Vector3.up;
            var receiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
            receiver.transform.position = point - Vector3.up * 0.25f;
            receiver.transform.localScale = new Vector3(10f, 0.5f, 10f);
            receiver.GetComponent<Renderer>().sharedMaterial = ShrineKit.Stone;
            var caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            caster.transform.position = point - sun.transform.forward * 4f;
            caster.transform.localScale = Vector3.one * 2.5f;
            caster.GetComponent<Collider>().enabled = false;
            var renderer = caster.GetComponent<Renderer>();
            renderer.sharedMaterial = ShrineKit.Stone;
            renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            try
            {
                var eye = point + new Vector3(0f, 4f, -10f);
                probe.SetView(eye, point - eye);
                Physics.SyncTransforms();
                if (!receiver.GetComponent<Collider>().Raycast(new Ray(point + Vector3.up, Vector3.down), out var sample, 2f)) return false;
                var points = new[] { sample };
                float before = probe.Sample(sun, points, HotelNav.EnvMask)[0];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                float after = probe.Sample(sun, points, HotelNav.EnvMask)[0];
                Debug.Log($"[PryingEyes] shadow-audit: control {before:F3} -> {after:F3}");
                return before - after >= ShadowJump;
            }
            finally
            {
                receiver.SetActive(false);
                caster.SetActive(false);
                UnityEngine.Object.Destroy(receiver);
                UnityEngine.Object.Destroy(caster);
            }
        }
    }
}
#endif
