#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -shafttest [-shaftshots]: i fasci di luce dalle finestre (LightShafts). Il piano ne ha; da
    /// un punto di lato il fascio e' acceso, guarda la camera e si vede; il pulviscolo ci galleggia dentro; stando sull'asse sfuma;
    /// nascosti non si vedono. Foto con e senza dallo stesso punto.</summary>
    public sealed class ShaftTest : PlayerTest
    {
        protected override string Suite { get { return "shafttest"; } }
        protected override string PhotoFlag { get { return "-shaftshots"; } }
        protected override string OutFolder { get { return "fasci"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<ShaftTest>("-shafttest"); }

        static void Face(PlayerController player, Vector3 target)
        {
            Vector3 d = target - player.cam.position;
            player.yaw = Mathf.Atan2(d.x, d.z);
            player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll();
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            player.invulnerable = true;
            var ls = LightShafts.I;
            Check($"il piano ha i fasci di luce ({(ls != null ? ls.Shafts.Count : 0)})", ls != null && ls.Shafts.Count > 0);
            if (Failed > 0) yield break;

            // un fascio da guardare di lato, da un punto del pavimento con la vista libera sul suo centro
            LightShafts.Shaft pick = null; Vector3 stand = Vector3.zero, mid = Vector3.zero;
            foreach (var s in ls.Shafts)
            {
                Vector3 m = s.origin + s.axis * (s.len * 0.5f);
                Vector3 side = Vector3.Cross(Vector3.up, s.axis); side.y = 0f;
                if (side.sqrMagnitude < 1e-4f) continue;
                side.Normalize();
                foreach (float d in new[] { 6f, -6f, 8f, -8f, 4.5f, -4.5f })
                {
                    Vector3 q = new Vector3(m.x, 0f, m.z) + side * d;
                    if (!HotelNav.Ground(q + Vector3.up * (m.y + 1f), out Vector3 g) || !NavMesh.SamplePosition(g, out _, 0.4f, NavMesh.AllAreas)) continue;
                    if (!HotelNav.ClearLine(g + Vector3.up * 1.6f, m)) continue;
                    pick = s; stand = g; mid = m;
                    break;
                }
                if (pick != null) break;
            }
            Check("uno si vede di lato", pick != null);
            if (Failed > 0) yield break;
            player.Teleport(stand + Vector3.up * 0.05f);
            yield return null;
            Face(player, mid);
            LightPool.Tick(true);
            yield return null; yield return null;
            Check("il fascio e' acceso col suo faro", pick.beam.gameObject.activeSelf && pick.light.enabled);
            Vector3 toCam = player.cam.position - pick.origin;
            Vector3 n = (toCam - pick.axis * Vector3.Dot(toCam, pick.axis)).normalized;
            Check($"guarda la camera ({Vector3.Dot(pick.beam.up, n):F2})", Vector3.Dot(pick.beam.up, n) > 0.95f);
            Check($"si vede (alfa {pick.shown:F3} su {pick.alpha:F3})", pick.shown > pick.alpha * 0.5f);
            Photo("con");
            LightShafts.Hidden = true;
            yield return null; yield return null;
            Check("nascosti non si vedono", !pick.beam.gameObject.activeSelf);
            Photo("senza");
            LightShafts.Hidden = false;
            yield return new WaitForSeconds(2.5f);
            Check($"il pulviscolo ci galleggia dentro ({pick.motes.particleCount} granelli)", pick.motes.particleCount > 3);
            // sull'asse (dove il fascio tocca il pavimento) sfuma
            Vector3 foot = pick.origin + pick.axis * pick.len;
            if (HotelNav.Ground(foot + Vector3.up * 1.2f, out Vector3 fg) && NavMesh.SamplePosition(fg, out var fh, 0.6f, NavMesh.AllAreas))
            {
                player.Teleport(fh.position + Vector3.up * 0.05f);
                yield return null; yield return null; yield return null;
                Vector3 c = player.cam.position, tc = c - pick.origin;
                float along = Mathf.Clamp(Vector3.Dot(tc, pick.axis), 0f, pick.len);
                float dist = (pick.origin + pick.axis * along - c).magnitude;
                Check($"vicino all'asse sfuma ({dist:F2} m dall'asse, alfa {pick.shown:F3})", dist > 1.5f || pick.shown < pick.alpha * 0.6f);
            }
            player.invulnerable = false;
        }
    }
}
#endif
