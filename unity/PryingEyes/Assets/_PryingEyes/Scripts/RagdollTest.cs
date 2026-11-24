#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -ragdolltest [-ragdollshots]: i corpi che cadono davvero (EnemyRagdoll).
    /// Un colpo al petto spinge il monaco all'indietro, uno in fronte gli butta indietro la testa, un'esplosione lo solleva; i pezzi
    /// restano attaccati (niente arti che volano via), non passano il pavimento, si fermano entro pochi secondi e il cadavere sprofonda
    /// e sparisce come prima. I proiettili attraversano i corpi e colpiscono chi sta dietro; il giocatore non ci inciampa. Oltre
    /// MaxActive corpi fisici insieme, gli altri cadono con la vecchia animazione, senza errori.</summary>
    public sealed class RagdollTest : PlayerTest
    {
        protected override string Suite { get { return "ragdolltest"; } }
        protected override string PhotoFlag { get { return "-ragdollshots"; } }
        protected override string OutFolder { get { return "ragdoll"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<RagdollTest>("-ragdolltest"); }

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

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            if (Failed > 0) yield break;
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
            Photo("prima");

            // l'osso del bastone corre lungo il bastone piantato (la capsula del ragdoll si appoggia a quell'asse)
            var staffBone = Bone(a, "bastone");
            float staffUp = staffBone != null ? staffBone.TransformDirection(Vector3.up).normalized.y : 0f;
            Check($"l'osso del bastone corre lungo il bastone ({staffUp:F2})", staffUp > 0.9f);
            var aPelvis = Bone(a, "bacino"); var bHead = Bone(b, "testa"); var bPelvis = Bone(b, "bacino"); var cPelvis = Bone(c, "bacino");
            Check("i monaci hanno le ossa del ragdoll", aPelvis != null && bHead != null && cPelvis != null);
            if (Failed > 0) yield break;
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
            if (Failed > 0) yield break;
            yield return new WaitForSeconds(0.12f);
            Photo("colpo");
            yield return new WaitForSeconds(0.5f);
            Photo("caduta");
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
            Photo("fermi");
            Check("si fermano entro pochi secondi", ra.Frozen && rb.Frozen && rc.Frozen && EnemyRagdoll.Active == 0);
            yield return new WaitForSeconds(2.2f);
            Check("poi il cadavere sparisce", a == null && b == null && c == null);

            // il bastone (osso senza padre) cade anche lui: le due estremita' della sua capsula ora sono a terra. Si prova su un monaco solo:
            // con un altro a 1,4 m (come sopra) il bastone puo' posarsi contro di lui, a 0,6 m da terra, ed e' fisica giusta. Il corpo non
            // deve congelarsi finche' il bastone cade ancora: restava appeso a mezz'aria (a 0,9 m) in circa un caso su tre
            Enemy.ClearAll();
            yield return null;
            var lone = Enemy.Spawn(Enemy.EnemyType.Monaco, arena);                    // al centro del corridoio libero (da -3 a +3,6 m lungo forward)
            lone.transform.rotation = Quaternion.LookRotation(-forward);
            yield return new WaitForSeconds(0.4f);
            lone.Damage(999f, Bone(lone, "petto").position + Vector3.up * 0.05f, false);
            yield return null;
            var rl = lone != null ? lone.GetComponent<EnemyRagdoll>() : null;
            // l'estremita' piu' alta della capsula del bastone sopra il pavimento (9 se il bastone non c'e')
            float StaffTop()
            {
                var sc = rl != null && rl.Staff != null ? rl.Staff.GetComponent<CapsuleCollider>() : null;
                if (sc == null) return 9f;
                Vector3 hf = Vector3.up * Mathf.Max(0f, sc.height * 0.5f - sc.radius);
                return Mathf.Max(rl.Staff.transform.TransformPoint(sc.center + hf).y, rl.Staff.transform.TransformPoint(sc.center - hf).y) - floorY.y;
            }
            // l'altezza del bastone ogni 0,2 s fino a SettleTime + 0,4 s: * = corpo fermo (il bastone non deve cadere ancora dopo l'asterisco)
            var staffTrace = new System.Text.StringBuilder();
            for (float settleEnd = Time.time + EnemyRagdoll.SettleTime + 0.4f; Time.time < settleEnd;)
            {
                yield return new WaitForSeconds(0.2f);
                staffTrace.Append(StaffTop().ToString("F2")).Append(rl != null && rl.Frozen ? "* " : " ");
            }
            Debug.Log("[PryingEyes] ragdolltest altezza del bastone ogni 0,2 s (* = corpo fermo): " + staffTrace);
            float staffTop = StaffTop();
            Check($"il bastone cade a terra con lui (estremita' piu' alta a {staffTop:F2} m)", staffTop < 0.4f);

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
            Photo("tanti");
            Enemy.debugFreeze = false;
            player.invulnerable = false;
        }
    }
}
#endif
