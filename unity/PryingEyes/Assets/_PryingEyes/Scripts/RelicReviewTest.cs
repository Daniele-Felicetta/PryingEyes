using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -relictest: viste del modello vero, animazione, collider e rilascio risorse.</summary>
    public sealed class RelicReviewTest : MonoBehaviour
    {
        int checks, failed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-relictest") >= 0)
                new GameObject("RelicReviewTest").AddComponent<RelicReviewTest>();
        }
        void Check(string name, bool ok)
        {
            checks++;
            if (!ok) failed++;
            Debug.Log("[PryingEyes] relictest " + (ok ? "ok " : "FALLITO ") + name);
        }
        IEnumerator Start()
        {
            var sequence = Run();
            while (true)
            {
                bool more;
                try { more = sequence.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break;
                yield return sequence.Current;
            }
            Debug.Log($"[PryingEyes] relictest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }
        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while ((Proc.FloorDirector.I == null || Proc.FloorDirector.I.cur == null || Proc.FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            var gm = GameManager.I;
            Check("piano pronto", Proc.FloorDirector.I != null && Proc.FloorDirector.I.cur != null);
            gm.player.enabled = false; gm.player.invulnerable = true;
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = true;
            string output = Path.GetFullPath("unity_logs/relics"); Directory.CreateDirectory(output);
            const int layer = 13;
            var cam = new GameObject("Camera modelli").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.022f, 0.026f, 0.033f);
            cam.cullingMask = 1 << layer; cam.fieldOfView = 38f; cam.aspect = 1f; cam.nearClipPlane = 0.03f;
            RenderSettings.fog = false;
            RenderSettings.ambientSkyColor = new Color(0.38f, 0.41f, 0.47f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.21f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.07f, 0.06f);
            var light = new GameObject("Luce modelli").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.6f; light.color = new Color(1f, 0.89f, 0.74f);
            light.transform.rotation = Quaternion.Euler(40f, 145f, 0f);
            var fill = new GameObject("Luce bordo").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 1f; fill.color = new Color(0.57f, 0.73f, 1f);
            fill.transform.rotation = Quaternion.Euler(15f, -35f, 0f);
            var origin = new Vector3(1000, 0, 1000);
            foreach (var kind in new[] { Enemy.EnemyType.Veglia, Enemy.EnemyType.Portachiavi })
            {
                var enemy = Enemy.Spawn(kind, origin, puppet: true); enemy.enabled = false;
                foreach (var t in enemy.GetComponentsInChildren<Transform>()) t.gameObject.layer = layer;
                var rig = enemy.GetComponent<AlienRig>();
                int count = 0;
                foreach (var mf in enemy.GetComponentsInChildren<MeshFilter>())
                {
                    Check(kind + " mesh finita " + mf.name, Array.TrueForAll(mf.sharedMesh.vertices, v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)));
                    count += mf.sharedMesh.vertexCount;
                }
                Check(kind + " geometria e budget", count > 1000 && count < 18000);
                Check(kind + " punto debole", Array.Exists(enemy.GetComponentsInChildren<Collider>(), c => c.CompareTag("Head") && !c.isTrigger));
                var joint = rig.transform.Find("Corpo/Arto");
                rig.Animate(0f, 1f, false, false);
                var rotation = joint.localRotation;
                yield return new WaitForSeconds(0.23f);
                rig.Animate(0f, 1f, true, false);
                Check(kind + " animazione", Quaternion.Angle(rotation, joint.localRotation) > 0.05f);
                Vector3 center = origin + Vector3.up * (kind == Enemy.EnemyType.Veglia ? -0.12f : 0.78f);
                foreach (int angle in new[] { 0, 35, 90, 180 })
                {
                    enemy.transform.rotation = Quaternion.Euler(0, angle, 0);
                    cam.transform.SetPositionAndRotation(center + new Vector3(0, 0.20f, 2.8f), Quaternion.LookRotation(new Vector3(0, -0.20f, -2.8f)));
                    yield return null;
                    GameManager.CamShot(Path.Combine(output, kind + "_" + angle + ".png"), cam, false, 960, 960);
                }
                var meshes = enemy.GetComponentsInChildren<MeshFilter>();
                Mesh owned = Array.Find(meshes, mf => mf.sharedMesh.name == (kind == Enemy.EnemyType.Veglia ? "Volto di cera e stoppino" : "Casellario e campanello")).sharedMesh;
                Destroy(enemy.gameObject); yield return null; yield return null;
                Check(kind + " mesh rilasciata", owned == null);
            }
        }
    }
}
