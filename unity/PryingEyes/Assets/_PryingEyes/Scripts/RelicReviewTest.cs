using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
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
            bool worldView=false;
            foreach(var point in Proc.FloorDirector.I.cur.groundSpawns)
            {
                if(point.y>0.5f)continue;
                Vector3 position=point+Vector3.up*1.55f+Vector3.forward*1.8f;
                if(Physics.CheckCapsule(position-Vector3.up*0.65f,position+Vector3.up*0.4f,0.42f,HotelNav.EnvMask,QueryTriggerInteraction.Ignore)||
                    Physics.CheckCapsule(point+Vector3.up*0.5f,point+Vector3.up*1.4f,0.42f,HotelNav.EnvMask,QueryTriggerInteraction.Ignore)||
                    !HotelNav.ClearLine(point+Vector3.up*1.62f,position))continue;
                gm.player.Teleport(point+Vector3.up*0.05f);
                gm.player.cam.SetPositionAndRotation(point+Vector3.up*1.62f,Quaternion.identity);
                var actor=Enemy.Spawn(Enemy.EnemyType.Veglia,position,puppet:true);actor.enabled=false;
                actor.transform.rotation=Quaternion.Euler(0,180,0);
                Torch.I.on=true;
                yield return new WaitForSeconds(0.8f);
                Proc.LightPool.Tick(false,gm.player.cam.position);
                GameManager.CamShot(Path.Combine(output,"Veglia_ambiente.png"),null,true);
                float end=Time.time+0.4f;
                while(Time.time<end){actor.GetComponent<AlienRig>().Animate(0,1,true,true);yield return null;}
                GameManager.CamShot(Path.Combine(output,"Veglia_ambiente_preparazione.png"),null,true);
                Enemy.Remove(actor);yield return null;worldView=true;break;
            }
            Check("Veglia vista con luce e ambiente reali",worldView);
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
                    Check(kind + " normali valide " + mf.name, Array.TrueForAll(mf.sharedMesh.normals, n => float.IsFinite(n.x) && float.IsFinite(n.y) && float.IsFinite(n.z) && n.sqrMagnitude > 0.9f));
                    count += mf.sharedMesh.vertexCount;
                }
                Check(kind + " geometria e budget", count > 1000 && count < 18000);
                Debug.Log($"[PryingEyes] relictest geometria {kind}: {count} vertici");
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
                Mesh owned = Array.Find(meshes, mf => mf.sharedMesh.name == (kind == Enemy.EnemyType.Veglia ? "Maschera votiva di cera" : "Casellario e campanello")).sharedMesh;
                var ownedMaterials = new HashSet<Material>();
                foreach (var renderer in enemy.GetComponentsInChildren<Renderer>())
                    foreach (var material in renderer.sharedMaterials) ownedMaterials.Add(material);
                if (kind == Enemy.EnemyType.Veglia)
                {
                    enemy.transform.rotation = Quaternion.identity;
                    var iris=enemy.transform.Find("Corpo/Iride della Veglia");
                    var head=enemy.transform.Find("Corpo/Nucleo");
                    float end=Time.time+0.4f;
                    while(Time.time<end){rig.Animate(0,1,true,true);yield return null;}
                    Check("Veglia prepara il contatto con iride e corpo",iris.localScale.x>1.25f && Quaternion.Angle(Quaternion.identity,iris.parent.localRotation)>8);
                    Check("Veglia punto debole segue la maschera animata",Vector3.Distance(head.position,iris.position)<0.001f);
                    GameManager.CamShot(Path.Combine(output,"Veglia_preparazione.png"),cam,false,960,960);
                    end=Time.time+0.4f;
                    while(Time.time<end){rig.Animate(0,1,true,false);yield return null;}
                    Check("Veglia recupera dopo la preparazione",iris.localScale.x<1.05f);
                    rig.Collapse();
                    var fire=enemy.transform.Find("Corpo/Fiamma 0").GetComponent<Renderer>().sharedMaterials;
                    Check("Veglia spegne le fiamme alla morte",Array.TrueForAll(fire,m=>m.GetColor("_EmissionColor").maxColorComponent<0.001f));
                }
                Destroy(enemy.gameObject); yield return null; yield return null;
                Check(kind + " mesh rilasciata", owned == null);
                bool released=true; foreach(var material in ownedMaterials) released &= material==null;
                Check(kind+" materiali rilasciati",released);
            }
        }
    }
}
