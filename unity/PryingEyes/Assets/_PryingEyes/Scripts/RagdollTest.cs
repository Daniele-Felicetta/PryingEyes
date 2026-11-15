#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -ragdolltest [-ragdollshots]: i corpi che cadono davvero (EnemyRagdoll).
    /// Un colpo al petto spinge il monaco all'indietro, uno in fronte gli butta indietro la testa, un'esplosione lo solleva; i pezzi
    /// restano attaccati (niente arti che volano via), non passano il pavimento, si fermano entro pochi secondi e il cadavere sprofonda
    /// e sparisce come prima. I proiettili attraversano i corpi e colpiscono chi sta dietro; il giocatore non ci inciampa. Oltre
    /// MaxActive corpi fisici insieme, gli altri cadono con la vecchia animazione, senza errori.</summary>
    public sealed class RagdollTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ragdolltest") >= 0) new GameObject("RagdollTest").AddComponent<RagdollTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] ragdolltest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-ragdollshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] ragdolltest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void View(string name)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/ragdoll"); Directory.CreateDirectory(folder);
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, true);
        }

        static void Face(PlayerController player, Vector3 target)
        {
            Vector3 d = target - player.cam.position;
            player.yaw = Mathf.Atan2(d.x, d.z);
            player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        static Transform Bone(Enemy e, string name)
        {
            if (e == null) return null;
            foreach (var t in e.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            if (failed > 0) yield break;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            player.Teleport(arena - forward * 2.5f + Vector3.up * 0.05f); player.hp = 100; player.invulnerable = true;
            yield return null;
            Enemy.debugFreeze = true;
            Vector3 floorY = arena;
            // tre monaci in fila davanti, a 4,5 m; un quarto dietro al primo
            var a = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 2.0f - side * 1.4f);
            var b = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 2.0f);
            var c = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 2.0f + side * 1.4f);
            var behind = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 3.4f - side * 1.4f);
            foreach (var m in new[] { a, b, c, behind }) m.transform.rotation = Quaternion.LookRotation(-forward);
            yield return new WaitForSeconds(0.4f);
            Face(player, arena + forward * 2.0f + Vector3.up * 1.0f);
            yield return new WaitForSeconds(0.2f);
            View("prima");

            // l'osso del bastone corre lungo il bastone piantato (la capsula del ragdoll si appoggia a quell'asse)
            var staffBone = Bone(a, "bastone");
            float staffUp = staffBone != null ? staffBone.TransformDirection(Vector3.up).normalized.y : 0f;
            Check($"l'osso del bastone corre lungo il bastone ({staffUp:F2})", staffUp > 0.9f);
            var aPelvis = Bone(a, "bacino"); var bHead = Bone(b, "testa"); var bPelvis = Bone(b, "bacino"); var cPelvis = Bone(c, "bacino");
            Check("i monaci hanno le ossa del ragdoll", aPelvis != null && bHead != null && cPelvis != null);
            if (failed > 0) yield break;
            Vector3 a0 = aPelvis.position, bh0 = bHead.position, bp0 = bPelvis.position, c0 = cPelvis.position;
            Vector3 eye = player.cam.position;
            // colpo al petto, colpo in fronte, esplosione ai piedi del terzo
            Vector3 chest = Bone(a, "petto").position + Vector3.up * 0.05f;
            a.Damage(999f, chest, false);
            b.Damage(999f, bHead.position + Vector3.up * 0.08f, true);
            Enemy.NoteBlast(c0 + forward * 0.6f - Vector3.up * 0.7f);             // lo scoppio dietro di lui, vicino al pavimento
            c.Damage(999f, c0, false, true, DamageSource.Esplosione);
            yield return null;
            var ra = a != null ? a.GetComponent<EnemyRagdoll>() : null;
            var rb = b != null ? b.GetComponent<EnemyRagdoll>() : null;
            var rc = c != null ? c.GetComponent<EnemyRagdoll>() : null;
            Check("i tre caduti sono corpi fisici", ra != null && rb != null && rc != null);
            if (failed > 0) yield break;
            yield return new WaitForSeconds(0.12f);
            View("colpo");
            yield return new WaitForSeconds(0.5f);
            View("caduta");
            Vector3 away = (a0 - eye); away.y = 0f; away.Normalize();
            float pushA = Vector3.Dot(aPelvis.position - a0, away);
            Check($"il colpo al petto lo spinge all'indietro ({pushA:F2} m)", pushA > 0.15f);
            Vector3 awayB = (bh0 - eye); awayB.y = 0f; awayB.Normalize();
            float headBack = Vector3.Dot(bHead.position - bh0, awayB), pelvisBack = Vector3.Dot(bPelvis.position - bp0, awayB);
            Check($"il colpo in fronte butta indietro la testa ({headBack:F2} m, bacino {pelvisBack:F2} m)", headBack > 0.15f && headBack > pelvisBack);
            float lift = (cPelvis.position - c0).magnitude;
            Check($"l'esplosione lo scaglia ({lift:F2} m)", lift > 0.4f);

            // pezzi attaccati, sopra il pavimento, il proiettile passa
            bool attached = true, above = true;
            foreach (var r in new[] { ra, rb, rc })
            {
                var pel = r.Bodies[0].position;
                foreach (var body in r.Bodies)
                {
                    if ((body.position - pel).magnitude > 1.1f) attached = false;
                    if (body.position.y < floorY.y - 0.12f) above = false;
                }
            }
            Check("i pezzi restano attaccati", attached);
            Check("niente passa il pavimento", above);
            var cc = player.GetComponent<CharacterController>();
            bool ignored = true;
            foreach (var col in a.GetComponentsInChildren<Collider>()) if (col.enabled && cc != null && !Physics.GetIgnoreCollision(col, cc)) ignored = false;
            Check("il giocatore non inciampa nei corpi", ignored);
            Vector3 target = Bone(behind, "petto").position;
            Vector3 from = eye, dir = (target - from).normalized;
            bool hitBehind = false;
            if (Physics.Raycast(from, dir, out var hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                hitBehind = hit.collider.GetComponentInParent<Enemy>() == behind;
            Check("i proiettili attraversano i corpi e colpiscono chi sta dietro", hitBehind);

            yield return new WaitForSeconds(EnemyRagdoll.SettleTime + 0.4f);
            View("fermi");
            Check("si fermano entro pochi secondi", ra.Frozen && rb.Frozen && rc.Frozen && EnemyRagdoll.Active == 0);
            // il bastone (osso senza padre) cade anche lui: le due estremita' della sua capsula ora sono a terra
            float staffTop = 9f;
            var scap = ra.Staff != null ? ra.Staff.GetComponent<CapsuleCollider>() : null;
            if (scap != null)
            {
                Vector3 half = Vector3.up * Mathf.Max(0f, scap.height * 0.5f - scap.radius);
                staffTop = Mathf.Max(ra.Staff.transform.TransformPoint(scap.center + half).y, ra.Staff.transform.TransformPoint(scap.center - half).y) - floorY.y;
            }
            Check($"il bastone cade a terra con lui (estremita' piu' alta a {staffTop:F2} m)", staffTop < 0.4f);
            yield return new WaitForSeconds(2.2f);
            Check("poi il cadavere sparisce", a == null && b == null && c == null);

            // tanti insieme: oltre il tetto la vecchia caduta
            Enemy.ClearAll();
            yield return null;
            var many = new List<Enemy>();
            for (int i = 0; i < EnemyRagdoll.MaxActive + 3; i++)
                many.Add(Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * (1.0f + (i / 5) * 0.9f) + side * ((i % 5) - 2) * 0.8f));
            yield return new WaitForSeconds(0.3f);
            foreach (var m in many) if (m != null) m.Damage(999f, m.transform.position + Vector3.up * 1.2f, false);
            yield return null;
            int phys = 0;
            foreach (var m in many) if (m != null && m.GetComponent<EnemyRagdoll>() != null) phys++;
            Check($"al piu' {EnemyRagdoll.MaxActive} corpi fisici insieme ({phys} su {many.Count})", phys == EnemyRagdoll.MaxActive);
            yield return new WaitForSeconds(0.8f);
            View("tanti");
            Enemy.debugFreeze = false;
            player.invulnerable = false;
        }
    }
}
#endif
