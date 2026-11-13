using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -meleetest [-meleeshots]: clip, preparazione, contatto, recupero e schivate reali.</summary>
    public sealed class MeleeTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        Vector3 arena, forward;
        bool shots;
        Camera review;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-meleetest") >= 0) new GameObject("MeleeTest").AddComponent<MeleeTest>();
        }
        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] meleetest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }
        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-meleeshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            PlayerController.botDir = Vector3.zero;
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] meleetest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }
        bool FindArena()
        {
            foreach (var point in FloorDirector.I.cur.groundSpawns)
            {
                if (point.y > 0.5f) continue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 direction = Quaternion.Euler(0, i * 45, 0) * Vector3.forward;
                    bool free = true;
                    for (float d = -3; d <= 3.6f; d += 0.6f)
                    {
                        Vector3 at = point + direction * d;
                        if (!NavMesh.SamplePosition(at, out var sample, 0.25f, NavMesh.AllAreas) ||
                            Mathf.Abs(sample.position.y - point.y) > 0.2f ||
                            Physics.CheckCapsule(at + Vector3.up * 0.5f, at + Vector3.up * 1.4f, 0.42f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) { free = false; break; }
                    }
                    if (free) { arena = point; forward = direction; return true; }
                }
            }
            return false;
        }
        void Photo(Enemy enemy, string phase)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/melee"); Directory.CreateDirectory(folder);
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(folder, enemy.type + "_" + phase + ".png"), null, true);
            if (review == null)
            {
                review = new GameObject("Revisione mischia").AddComponent<Camera>();
                review.CopyFrom(Camera.main); review.enabled = false; review.cullingMask &= ~(1 << 2);
                var data = review.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true; data.volumeLayerMask = Camera.main.GetUniversalAdditionalCameraData().volumeLayerMask;
            }
            Vector3 eye = enemy.transform.position + enemy.transform.right * 2.5f + enemy.transform.forward * 2.7f + Vector3.up * 1.5f;
            review.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(enemy.transform.position + Vector3.up * 1.1f - eye));
            review.fieldOfView = 50;
            GameManager.CamShot(Path.Combine(folder, enemy.type + "_" + phase + "_lato.png"), review, false);
        }
        float AnimatedHeight(Enemy enemy)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            var mesh = new Mesh();
            foreach (var skin in enemy.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.BakeMesh(mesh);
                foreach (var vertex in mesh.vertices)
                {
                    float y = skin.transform.TransformPoint(vertex).y;
                    min = Mathf.Min(min, y); max = Mathf.Max(max, y);
                }
            }
            Destroy(mesh); return max - min;
        }
        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var player = GameManager.I.player; var hud = GameManager.I.hud;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("spazio di prova libero sulla navmesh reale", FindArena());
            if (failed > 0) yield break;
            if (!Torch.I.on) Torch.I.Toggle();
            hud.Banner("", ""); hud.Toast("");
            Enemy Spawn(Enemy.EnemyType type)
            {
                player.Teleport(arena + Vector3.up * 0.05f); player.hp = 100; player.shield = 50; player.sedT = 0; player.invulnerable = false;
                player.yaw = Mathf.Atan2(forward.x, forward.z); player.pitch = 0;
                var enemy = Enemy.Spawn(type, arena + forward * 1.8f);
                enemy.transform.rotation = Quaternion.LookRotation(-forward); enemy.MakeRelentless(); return enemy;
            }
            var types = new[] { Enemy.EnemyType.Monaco, Enemy.EnemyType.Abate, Enemy.EnemyType.Maschera, Enemy.EnemyType.Direttore, Enemy.EnemyType.Infermiere, Enemy.EnemyType.Primario };
            var puppet = Enemy.SpawnPuppet(9876, Enemy.EnemyType.Monaco,
                new EnemyNet { pos = arena + forward * 3, hp = 1, alert = Alert.Patrol });
            puppet.PuppetEvent(EnemyEvt.Attack, Vector3.zero, 0);
            yield return new WaitForSeconds(0.4f);
            Check("replica conserva la preparazione per la durata della clip", puppet.PreparingMelee);
            yield return new WaitForSeconds(0.25f);
            Check("replica passa al recupero dopo il contatto", !puppet.PreparingMelee && puppet.RecoveringMelee);
            yield return new WaitForSeconds(0.9f);
            Check("replica termina il recupero", !puppet.RecoveringMelee);
            Enemy.Remove(puppet); yield return null;
            foreach (var type in types)
            {
                var enemy = Spawn(type);
                if (type == Enemy.EnemyType.Monaco) enemy.Damage(1, enemy.AimPoint, false, false);
                deadline = Time.time + 5;
                while (!enemy.PreparingMelee && Time.time < deadline) yield return null;
                Check(type + " prepara il colpo", enemy.PreparingMelee);
                float health = player.hp + player.shield;
                yield return new WaitForSeconds(0.4f);
                Check(type + " nessun danno prima del contatto", Mathf.Abs(player.hp + player.shield - health) < 0.01f);
                var animator = enemy.GetComponentInChildren<Animator>();
                var clips = animator.GetCurrentAnimatorClipInfo(0);
                Check(type + " usa la nuova clip mischia", Array.Exists(clips, c => c.clip.name == "mischia"));
                float height = AnimatedHeight(enemy);
                Check(type + " conserva figura eretta e scala", height > 1f && height < 4f);
                Debug.Log($"[PryingEyes] meleetest altezza animata {type} {height:F2} m");
                Transform staff = Array.Find(enemy.GetComponentsInChildren<Transform>(), t => t.name == "bastone" && t.GetComponent<Renderer>() == null);
                Quaternion windup = staff != null ? staff.rotation : Quaternion.identity;
                Photo(enemy, "preparazione");
                deadline = Time.time + 2;
                while (enemy.PreparingMelee && Time.time < deadline) yield return null;
                yield return null; // leggere la posa dopo la valutazione dell'Animator del fotogramma del contatto.
                Check(type + " danno al contatto", player.hp + player.shield < health - 0.1f && enemy.RecoveringMelee);
                float phase = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Check(type + " contatto nella fase di affondo", phase > 0.4f && phase < 0.65f);
                Debug.Log($"[PryingEyes] meleetest fase contatto {type} {phase:F3}");
                Check(type + " arma animata dal caricamento all'affondo", staff != null && Quaternion.Angle(windup, staff.rotation) > 20);
                Photo(enemy, "contatto");
                health = player.hp + player.shield;
                yield return new WaitForSeconds(0.25f);
                Check(type + " recupero senza danni ripetuti", Mathf.Abs(player.hp + player.shield - health) < 0.01f && enemy.RecoveringMelee);
                Photo(enemy, "recupero");
                Enemy.Remove(enemy); yield return null;
            }
            var guarded = Spawn(Enemy.EnemyType.Monaco);
            deadline = Time.time + 5; while (!guarded.PreparingMelee && Time.time < deadline) yield return null;
            guarded.PuppetEvent(EnemyEvt.Hit, guarded.AimPoint, 0);
            guarded.Damage(1, guarded.AimPoint, false, false);
            yield return new WaitForSeconds(0.15f);
            var guardedClips = guarded.GetComponentInChildren<Animator>().GetCurrentAnimatorClipInfo(0);
            Check("colpi ricevuti non desincronizzano il gesto gia avviato", Array.Exists(guardedClips, c => c.clip.name == "mischia"));
            Enemy.Remove(guarded); yield return null;
            var abandoned = Spawn(Enemy.EnemyType.Monaco);
            deadline = Time.time + 5; while (!abandoned.PreparingMelee && Time.time < deadline) yield return null;
            float beforeGone = player.hp + player.shield;
            Enemy.OnTargetGone(Targets.Local);
            yield return new WaitForSeconds(0.8f);
            Check("bersaglio uscito annulla il contatto senza riassegnarlo", Mathf.Abs(player.hp + player.shield - beforeGone) < 0.01f);
            Enemy.Remove(abandoned); yield return null;
            foreach (string escape in new[] { "arretramento", "schiena", "parete" })
            {
                var enemy = Spawn(Enemy.EnemyType.Monaco);
                deadline = Time.time + 5; while (!enemy.PreparingMelee && Time.time < deadline) yield return null;
                Check(escape + " parte dalla preparazione", enemy.PreparingMelee);
                float health = player.hp + player.shield; Quaternion facing = enemy.transform.rotation;
                GameObject wall = null;
                if (escape == "arretramento") { PlayerController.holdLook = false; PlayerController.botDir = -forward; }
                else if (escape == "schiena") player.Teleport(enemy.transform.position + forward * 1.2f);
                else
                {
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.SetPositionAndRotation((enemy.transform.position + player.transform.position) * 0.5f + Vector3.up, Quaternion.LookRotation(forward));
                    wall.transform.localScale = new Vector3(3, 2.4f, 0.08f); Physics.SyncTransforms();
                }
                deadline = Time.time + 2; while (enemy.PreparingMelee && Time.time < deadline) yield return null;
                PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
                Check(escape + " evita il danno", Mathf.Abs(player.hp + player.shield - health) < 0.01f);
                Check(escape + " non fa ruotare il nemico durante il gesto", Quaternion.Angle(facing, enemy.transform.rotation) < 2);
                if (escape == "arretramento") Check("schivata con controller reale", Vector3.Distance(arena, player.transform.position) > 1.2f);
                if (wall != null) Destroy(wall);
                Enemy.Remove(enemy); yield return null;
            }
        }
    }
}
