using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    public static partial class PerfProbe
    {
        // -perftest -perfhorde [-hordeout directory]: stesso ambiente, vuoto / limite del round / stress doppio.
        static List<Vector3> HordePositions(Room room,Camera camera)
        {
            var points=new List<Vector3>();
            for(float z=room.z0*0.1f+1;z<room.z1*0.1f-1;z+=1.3f)
                for(float x=room.x0*0.1f+1;x<room.x1*0.1f-1;x+=1.3f)
                {
                    var p=new Vector3(x,0,z);
                    if(!room.Contains(x,z,0.8f)||!NavMesh.SamplePosition(p,out var hit,0.25f,HotelNav.EnemyMask)||Mathf.Abs(hit.position.y)>0.3f)continue;
                    p=hit.position;
                    if(Vector3.Distance(p,camera.transform.position)<4||Physics.CheckCapsule(p+Vector3.up*0.5f,p+Vector3.up*1.7f,0.45f,HotelNav.EnvMask,QueryTriggerInteraction.Ignore))continue;
                    Vector3 top=p+Vector3.up*1.6f,screen=camera.WorldToViewportPoint(top);
                    if(screen.z<0||screen.x<0.08f||screen.x>0.92f||screen.y<0.12f||screen.y>0.88f||!HotelNav.ClearLine(camera.transform.position,top))continue;
                    points.Add(p);
                }
            points.Sort((a,b)=>(a-camera.transform.position).sqrMagnitude.CompareTo((b-camera.transform.position).sqrMagnitude));
            return points;
        }
        static int HordeVisible(Camera camera)
        {
            var planes=GeometryUtility.CalculateFrustumPlanes(camera);int visible=0;
            foreach(var enemy in Enemy.All)
            {
                foreach(var renderer in enemy.GetComponentsInChildren<Renderer>())
                    if(renderer.enabled&&GeometryUtility.TestPlanesAABB(planes,renderer.bounds)){visible++;break;}
            }
            return visible;
        }
        static IEnumerator RunHorde(FloorDirector fd,PlayerController player,Camera camera,RenderTexture rt,Texture2D pixel)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null){Debug.LogError("[PryingEyes] hordeperf richiede grafica reale");Application.Quit(2);yield break;}
            string output=Path.GetFullPath(Arg("-hordeout","unity_logs/horde"));Directory.CreateDirectory(output);
            Enemy.ClearAll();Enemy.debugNoSense=Enemy.debugFreeze=false;
            View view=default;List<Vector3> points=null;
            foreach(var candidate in Pick(fd.cur.plan))
            {
                player.Teleport(candidate.pos);player.yaw=candidate.yaw;player.pitch=0;
                yield return null;
                var free=HordePositions(candidate.room,camera);
                if(free.Count>=32){view=candidate;points=free;break;}
            }
            if(points==null){Debug.LogError("[PryingEyes] hordeperf nessuna sala con 32 posizioni libere in vista");Application.Quit(1);yield break;}
            Torch.I.on=true;Torch.I.drainMul=0;
            long allocationStart=GC.GetAllocatedBytesForCurrentThread();
            var allocationControl=new byte[4096];GC.KeepAlive(allocationControl);
            bool allocationCounter=GC.GetAllocatedBytesForCurrentThread()>allocationStart;
            var report=new StringBuilder("caso\tnemici\tvisibili_inizio\tvisibili_fine\tnascita_media_ms\tnascita_max_ms\tbyte_gestiti_nascita\tframe_medio_ms\tp95_ms\tp99_ms\tlogica_ms\trender_cpu_ms\tattesa_gpu_ms\trenderer\tvertici\n");
            var metadata=$"{SystemInfo.graphicsDeviceName}; 1920x1080; tema={fd.cur.plan.theme}; seme={fd.runSeed}; piano={fd.floor}; sala={view.room.kind}; luminosita={GameSettings.Brightness:F3}";
            metadata+="; contatore_allocazioni="+allocationCounter;
            Debug.Log("[PryingEyes] hordeperf "+metadata);File.WriteAllText(Path.Combine(output,"ambiente.txt"),metadata);
            foreach(string scenario in new[]{"vuoto","16-veglie-ferme","16-veglie-attive","16-misti-attivi","16-misti-senza-ombre","16-misti-fermi","32-veglie-stress"})
            {
                Enemy.ClearAll();Fx.Clear();yield return null;
                player.Teleport(view.pos);player.yaw=view.yaw;player.pitch=0;
                bool still=scenario.EndsWith("ferme")||scenario.EndsWith("fermi");int count=scenario=="vuoto"?0:scenario.StartsWith("32")?32:16;
                double spawnSum=0,spawnMax=0;long allocated=0;int renderers=0,vertices=0;
                for(int i=0;i<count;i++)
                {
                    var type=scenario.Contains("misti")?new[]{Enemy.EnemyType.Monaco,Enemy.EnemyType.Abate,Enemy.EnemyType.Portachiavi,Enemy.EnemyType.Veglia}[i%4]:Enemy.EnemyType.Veglia;
                    Vector3 position=points[i]+Vector3.up*(Enemy.IsFlying(type)?1.6f:0);
                    long bytes=GC.GetAllocatedBytesForCurrentThread();var clock=System.Diagnostics.Stopwatch.StartNew();
                    var enemy=Enemy.Spawn(type,position,puppet:still);
                    clock.Stop();allocated+=GC.GetAllocatedBytesForCurrentThread()-bytes;spawnSum+=clock.Elapsed.TotalMilliseconds;spawnMax=Math.Max(spawnMax,clock.Elapsed.TotalMilliseconds);
                    enemy.transform.rotation=Quaternion.LookRotation(player.transform.position+Vector3.up*1.3f-enemy.transform.position);
                    if(still)
                    {
                        enemy.enabled=false;
                        foreach(var animator in enemy.GetComponentsInChildren<Animator>())animator.enabled=false;
                    }
                    else enemy.MakeRelentless();
                    if(scenario.EndsWith("ombre"))foreach(var renderer in enemy.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;
                    renderers+=enemy.GetComponentsInChildren<Renderer>().Length;
                    foreach(var mesh in enemy.GetComponentsInChildren<MeshFilter>())vertices+=mesh.sharedMesh.vertexCount;
                    foreach(var skin in enemy.GetComponentsInChildren<SkinnedMeshRenderer>())vertices+=skin.sharedMesh.vertexCount;
                }
                for(int i=0;i<Warm;i++){yield return null;camera.Render();}
                LightPool.Tick(true);int visible=HordeVisible(camera);
                var frames=new List<double>(Frames);double logic=0,cpu=0,gpu=0,previous=Time.realtimeSinceStartupAsDouble;
                for(int i=0;i<Frames;i++)
                {
                    yield return null;double t1=Time.realtimeSinceStartupAsDouble;
                    camera.Render();double t2=Time.realtimeSinceStartupAsDouble;
                    RenderTexture.active=rt;pixel.ReadPixels(new Rect(0,0,1,1),0,0);RenderTexture.active=null;
                    double t3=Time.realtimeSinceStartupAsDouble;logic+=t1-previous;cpu+=t2-t1;gpu+=t3-t2;frames.Add((t3-previous)*1000);previous=t3;
                }
                frames.Sort();double sum=0;foreach(double time in frames)sum+=time;
                string row=string.Format(CultureInfo.InvariantCulture,"{0}\t{1}\t{2}\t{3}\t{4:F3}\t{5:F3}\t{6}\t{7:F3}\t{8:F3}\t{9:F3}\t{10:F3}\t{11:F3}\t{12:F3}\t{13}\t{14}",
                    scenario,count,visible,HordeVisible(camera),spawnSum/Math.Max(1,count),spawnMax,allocationCounter?allocated:-1,sum/Frames,frames[(int)(Frames*.95)],frames[(int)(Frames*.99)],logic/Frames*1000,cpu/Frames*1000,gpu/Frames*1000,renderers,vertices);
                report.AppendLine(row);File.WriteAllText(Path.Combine(output,"misure.tsv"),report.ToString());Debug.Log("[PryingEyes] hordeperf "+row);
                camera.Render();SavePng(rt,Path.Combine(output,scenario+".png"));
            }
            Enemy.ClearAll();Debug.Log("[PryingEyes] hordeperf RISULTATO: 7 scenari misurati");Application.Quit(0);
        }
    }
}
