#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -eyetest [-eyeshots]: gli occhi di vetro del piano. Ce ne sono tre, distanti tra loro, sulla
    /// navmesh; uno preso con F conta 1/3, uno colpito da un proiettile conta lo stesso; presi tutti e tre l'arma in mano viene
    /// consacrata e arrivano i punti. Foto di un occhio da vicino con -eyeshots.</summary>
    public sealed class EyesTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-eyetest") >= 0) new GameObject("EyesTest").AddComponent<EyesTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] eyetest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-eyeshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] eyetest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true;
            var he = HiddenEyes.I;
            Check($"gli occhi di vetro ci sono ({(he != null ? he.Total : 0)})", he != null && he.Total == 3);
            if (he == null || he.Total == 0) yield break;
            for (int i = 0; i < he.Eyes.Count; i++)
                for (int j = i + 1; j < he.Eyes.Count; j++)
                    Check($"occhi {i} e {j} lontani tra loro", Vector3.Distance(he.Eyes[i].transform.position, he.Eyes[j].transform.position) >= 12f);
            foreach (var e in he.Eyes)
                Check($"{e.name} sopra la navmesh", UnityEngine.AI.NavMesh.SamplePosition(e.transform.position - Vector3.up * 1.35f, out _, 0.6f, UnityEngine.AI.NavMesh.AllAreas));
            var first = he.Eyes[0];
            if (shots)
            {
                player.Teleport(first.transform.position - Vector3.up * 1.3f + Vector3.forward * 1.6f);
                yield return null;
                Vector3 d = first.transform.position - player.cam.position;
                player.yaw = Mathf.Atan2(d.x, d.z); player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
                yield return new WaitForSeconds(0.6f);
                string folder = Path.GetFullPath("unity_logs/occhi"); Directory.CreateDirectory(folder);
                LightPool.Tick(false, player.cam.position);
                GameManager.CamShot(Path.Combine(folder, "occhio.png"), null, true);
            }
            int score0 = gm.score;
            first.Use();
            yield return null;
            Check("preso con F conta 1", he.Found == 1);
            if (he.Total > 1)
            {
                he.Eyes[1].Shot(30f, he.Eyes[1].transform.position, Vector3.forward);
                yield return null;
                Check("colpito da un proiettile conta lo stesso", he.Found == 2);
            }
            var w = player.weapon;
            bool could = w.CanConsecrate;
            if (he.Total > 2) { he.Eyes[2].Use(); yield return null; }
            Check("presi tutti, il piano li conta", he.Found == he.Total);
            Check(could ? "l'arma in mano e' consacrata" : "arrivano i punti", could ? !w.CanConsecrate : gm.score >= score0 + 3500);
            Check("e arrivano almeno 1000 punti", gm.score >= score0 + 1000);
        }
    }
}
#endif
