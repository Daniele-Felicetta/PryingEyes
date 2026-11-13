using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -flyingmeleetest: affondi reali e schivate del controller.</summary>
    public sealed class FlyingMeleeTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        Vector3 arena, forward;
        static readonly FieldInfo strike = typeof(Enemy).GetField("strikeAt", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo attackLock = typeof(Enemy).GetField("atkLock", BindingFlags.Instance | BindingFlags.NonPublic);
        static bool Preparing(Enemy e) => (float)strike.GetValue(e) >= 0;
        static bool Recovering(Enemy e) => !Preparing(e) && (float)attackLock.GetValue(e) > 0;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(),"-flyingmeleetest")>=0)
                new GameObject("FlyingMeleeTest").AddComponent<FlyingMeleeTest>();
        }
        void Check(string name,bool ok){checks++;if(!ok)failed++;Debug.Log("[PryingEyes] flyingmeleetest "+(ok?"ok ":"FALLITO ")+name);}
        void Log(string message,string stack,LogType type){if(type==LogType.Exception)exceptions++;}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Log;
            var run=Run();
            while(true)
            {
                bool more;try{more=run.MoveNext();}catch(Exception e){Debug.LogException(e);Check("sequenza completa",false);break;}
                if(!more)break;yield return run.Current;
            }
            PlayerController.botDir=Vector3.zero;
            Check("nessuna eccezione runtime",exceptions==0);
            Debug.Log($"[PryingEyes] flyingmeleetest RISULTATO {(failed==0?"SUPERATA":"FALLITA")}: {checks-failed}/{checks}");
            Application.Quit(failed==0?0:1);
        }
        void Photo(Enemy enemy,string phase)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-flyingshots")<0)return;
            string output=Path.GetFullPath("unity_logs/flying-melee");Directory.CreateDirectory(output);
            LightPool.Tick(false,Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(output,enemy.type+"_"+phase+".png"),null,true);
        }
        Enemy Spawn(Enemy.EnemyType type)
        {
            Fx.Clear();
            var player=GameManager.I.player;
            player.Teleport(arena+Vector3.up*0.05f);player.hp=100;player.shield=50;player.invulnerable=false;
            player.yaw=Mathf.Atan2(forward.x,forward.z);player.pitch=0;
            PlayerController.holdLook=true;PlayerController.botDir=Vector3.zero;
            var enemy=Enemy.Spawn(type,arena+forward*1.7f+Vector3.up*1.35f);
            enemy.transform.rotation=Quaternion.LookRotation(-forward);enemy.MakeRelentless();return enemy;
        }
        IEnumerator WaitPreparation(Enemy enemy)
        {
            float end=Time.time+5;
            while(!Preparing(enemy)&&Time.time<end)yield return null;
            Check(enemy.type+" prepara il colpo",Preparing(enemy));
        }
        IEnumerator WaitContact(Enemy enemy)
        {
            float end=Time.time+2;
            while(Preparing(enemy)&&Time.time<end)yield return null;
        }
        int ImpactParticles(bool visible=false)
        {
            var root=GameObject.Find("Fx");if(root==null)return 0;
            int count=0;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if(!renderer.enabled||(renderer.sharedMaterial!=Fx.SparkMat&&renderer.sharedMaterial!=Fx.PuffMat))continue;
                Vector3 screen=Camera.main.WorldToViewportPoint(renderer.transform.position);
                if(!visible||(screen.z>Camera.main.nearClipPlane&&screen.x>0&&screen.x<1&&screen.y>0&&screen.y<1&&renderer.transform.localScale.x>0.001f))count++;
            }
            return count;
        }
        IEnumerator Run()
        {
            float end=Time.realtimeSinceStartup+60;
            while((FloorDirector.I==null||FloorDirector.I.cur==null||FloorDirector.I.transitioning)&&Time.realtimeSinceStartup<end)yield return null;
            Check("arena fisica libera",MeleeTest.FindArena(out arena,out forward));if(failed>0)yield break;
            Enemy.ClearAll();Enemy.debugFreeze=Enemy.debugNoSense=false;
            Torch.I.on=true;GameManager.I.hud.Banner("","");GameManager.I.hud.Toast("");
            var player=GameManager.I.player;
            foreach(var type in new[]{Enemy.EnemyType.Occhio,Enemy.EnemyType.Veglia,Enemy.EnemyType.Medusa})
            {
                var enemy=Spawn(type);yield return WaitPreparation(enemy);
                float health=player.hp+player.shield;Vector3 start=enemy.transform.position;
                yield return new WaitForSeconds(0.25f);
                Check(type+" nessun danno anticipato",Mathf.Abs(player.hp+player.shield-health)<0.01f);
                Check(type+" prepara senza inseguire",Vector3.Distance(start,enemy.transform.position)<0.06f);
                Photo(enemy,"preparazione");
                yield return WaitContact(enemy);
                yield return null;
                Check(type+" contatto sul bersaglio fermo",player.hp+player.shield<health-0.1f);
                Check(type+" recupero dopo il contatto",Recovering(enemy));
                Check(type+" affondo fisico visibile",Vector3.Distance(start,enemy.transform.position)>0.55f);
                Check(type+" provenienza del danno visibile",GameManager.I.hud.DamageDirectionCount>0);
                Check(type+" particelle del contatto nel campo visivo",ImpactParticles(true)>0);
                Photo(enemy,"contatto");
                health=player.hp+player.shield;Vector3 contact=enemy.transform.position;
                yield return new WaitForSeconds(0.25f);
                Check(type+" recupero senza danni ripetuti",Mathf.Abs(player.hp+player.shield-health)<0.01f);
                Check(type+" non insegue durante il recupero",Vector3.Distance(contact,enemy.transform.position)<0.06f);
                Enemy.Remove(enemy);yield return null;
                enemy=Spawn(type);
                enemy.transform.position=arena+forward*0.65f+Vector3.up*1.35f;
                yield return WaitPreparation(enemy);health=player.hp+player.shield;
                yield return WaitContact(enemy);
                Check(type+" contatto valido anche da molto vicino",player.hp+player.shield<health-0.1f);
                Check(type+" affondo vicino non attraversa il bersaglio",Vector3.Dot(enemy.transform.position-player.transform.position,forward)>0.35f);
                Enemy.Remove(enemy);yield return null;
                foreach(string escape in new[]{"schiena","laterale","arretramento","parete","bersaglio uscito"})
                {
                    enemy=Spawn(type);yield return WaitPreparation(enemy);
                    health=player.hp+player.shield;Quaternion facing=enemy.transform.rotation;
                    GameObject wall=null;
                    if(escape=="schiena")player.Teleport(enemy.transform.position+forward*1.4f-Vector3.up*1.3f);
                    else if(escape=="laterale"){PlayerController.holdLook=false;PlayerController.botDir=Vector3.Cross(Vector3.up,forward);}
                    else if(escape=="arretramento"){PlayerController.holdLook=false;PlayerController.botDir=-forward;}
                    else if(escape=="bersaglio uscito")Enemy.OnTargetGone(Targets.Local);
                    else
                    {
                        wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                        wall.transform.SetPositionAndRotation((enemy.transform.position+player.transform.position)*0.5f+Vector3.up*0.5f,Quaternion.LookRotation(forward));
                        wall.transform.localScale=new Vector3(3,3,0.08f);Physics.SyncTransforms();
                    }
                    yield return WaitContact(enemy);
                    PlayerController.botDir=Vector3.zero;PlayerController.holdLook=true;
                    Check(type+" "+escape+" evita il danno",Mathf.Abs(player.hp+player.shield-health)<0.01f);
                    Check(type+" "+escape+" mantiene la direzione impegnata",Quaternion.Angle(facing,enemy.transform.rotation)<2);
                    Check(type+" "+escape+" non produce falso impatto",ImpactParticles()==0);
                    if(escape=="laterale"||escape=="arretramento")Check(type+" schivata con controller reale",Vector3.Distance(arena,player.transform.position)>1.2f);
                    if(escape=="parete")Check(type+" parete ferma anche il corpo",!Physics.CheckSphere(enemy.transform.position,0.4f,HotelNav.EnvMask,QueryTriggerInteraction.Ignore));
                    if(wall!=null)Destroy(wall);
                    Enemy.Remove(enemy);yield return null;
                }
            }
            var state=new EnemyNet {pos=arena+forward*1.7f+Vector3.up*1.3f,hp=1,alert=Alert.Hunting};
            var replica=Enemy.SpawnPuppet(9877,Enemy.EnemyType.Veglia,state);
            replica.PuppetEvent(EnemyEvt.Attack,-forward,0);
            state.yaw=Mathf.Atan2(forward.x,forward.z)+Mathf.PI/2;
            replica.ApplyNet(state);float before=player.hp+player.shield;
            yield return new WaitForSeconds(0.4f);
            Check("replica conserva la preparazione",Preparing(replica));
            Check("replica conserva la direzione anche con uno stato precedente",Vector3.Angle(replica.transform.forward,-forward)<2);
            yield return new WaitForSeconds(0.36f);
            Check("replica passa al recupero",Recovering(replica));
            Check("replica non infligge danno",Mathf.Abs(player.hp+player.shield-before)<0.01f);
            Fx.Clear();replica.PuppetEvent(EnemyEvt.Strike,player.cam.position+player.cam.forward*0.4f,0);
            Check("replica non mostra impatto su colpo mancato",ImpactParticles()==0);
            replica.PuppetEvent(EnemyEvt.Strike,player.cam.position+player.cam.forward*0.4f,1);
            yield return null;
            Check("replica mostra impatto ricevuto dall'ospite",ImpactParticles(true)>0);
            yield return new WaitForSeconds(0.5f);
            Check("replica termina il recupero",!Recovering(replica));
            Enemy.Remove(replica);
        }
    }
}
