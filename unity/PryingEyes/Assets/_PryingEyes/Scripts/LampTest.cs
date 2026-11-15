#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -lamptest [-lampshots]: le lampade che si rompono (BreakableLamps). Il piano ne ha; una
    /// accesa colpita con un'arma vera si rompe: la luce si spegne e lascia il pool, il bersaglio sparisce, i materiali passano allo
    /// spento; un secondo colpo non fa niente; le candele non sono fragili.</summary>
    public sealed class LampTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-lamptest") >= 0) new GameObject("LampTest").AddComponent<LampTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] lamptest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-lampshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] lamptest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void View(string name)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/lampade"); Directory.CreateDirectory(folder);
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, true);
        }

        static void Face(PlayerController player, Vector3 target)
        {
            Vector3 d = target - player.cam.position;
            player.yaw = Mathf.Atan2(d.x, d.z);
            player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll();
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            player.invulnerable = true;
            var ls = BreakableLamps.I;
            Check($"il piano ha lampade fragili ({(ls != null ? ls.List.Count : 0)})", ls != null && ls.List.Count > 0);
            if (failed > 0) yield break;
            int candles = 0;
            foreach (var l in ls.List) if (l.Light != null && !l.Light.electric) candles++;
            Check("nessuna candela tra le fragili", candles == 0);

            // una lampada accesa, vista da un punto del pavimento a 2-5 m
            BreakableLamp pick = null; Vector3 stand = Vector3.zero;
            foreach (var l in ls.List)
            {
                if (l.Light == null || !l.Light.wanted) continue;
                Vector3 b = l.Bulb;
                for (int a = 0; a < 12 && pick == null; a++)
                    foreach (float d in new[] { 3f, 4f, 2.2f, 5f })
                    {
                        Vector3 q = b + Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward * d;
                        if (!HotelNav.Ground(q + Vector3.up * 1.5f, out Vector3 g) || Mathf.Abs(g.y - b.y) > 3f) continue;
                        if (!NavMesh.SamplePosition(g, out _, 0.4f, NavMesh.AllAreas)) continue;
                        if (Physics.Linecast(g + Vector3.up * 1.6f, b, out var h, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                            h.collider.GetComponentInParent<BreakableLamp>() != l) continue;
                        pick = l; stand = g;
                        break;
                    }
                if (pick != null) break;
            }
            Check("una lampada accesa si vede da vicino", pick != null);
            if (failed > 0) yield break;
            player.Teleport(stand + Vector3.up * 0.05f);
            yield return null;
            Face(player, pick.Bulb);
            LightPool.Tick(true);
            yield return null; yield return null;
            Check("prima e' accesa", pick.Light.l != null && pick.Light.l.enabled);
            View("accesa");
            // colpo con l'arma vera: lo stesso raggio dei proiettili
            Vector3 eye = player.cam.position, dir = (pick.Bulb - eye).normalized;
            bool hit = Physics.Raycast(eye, dir, out var rh, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && rh.collider.GetComponentInParent<IShootable>() != null;
            Check("il raggio del proiettile la prende", hit);
            if (hit) rh.collider.GetComponentInParent<IShootable>().Shot(30f, rh.point, dir);
            yield return null; yield return null;
            LightPool.Tick(true);
            yield return null;
            Check("si rompe: luce spenta e fuori dal pool", pick.Broken && !pick.Light.wanted && (pick.Light.l == null || !pick.Light.l.enabled));
            var col = pick.GetComponent<SphereCollider>();
            Check("il bersaglio sparisce", col == null || !col.enabled);
            bool off = true;
            foreach (var r in pick.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && ((m.name.Contains("lampadina") && !m.name.Contains("spenta")) || (m.name.Contains("paralume") && !m.name.Contains("spento")) || (m.name.Contains("neon") && !m.name.Contains("spento")))) off = false;
            Check("i materiali passano allo spento", off);
            yield return new WaitForSeconds(0.2f);
            View("rotta");
            pick.Shot(30f, pick.Bulb, dir);
            Check("un secondo colpo non fa niente", pick.Broken);
            player.invulnerable = false;
        }
    }
}
#endif
