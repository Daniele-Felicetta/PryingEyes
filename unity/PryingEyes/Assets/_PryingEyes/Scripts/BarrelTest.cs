#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -barreltest [-barrelshots]: botti d'olio santo e nemici che salgono dal pavimento.
    /// Le botti ci sono, nelle sale grandi e sulla navmesh; un colpo da 50 ne fa scoppiare una: i monaci attorno muoiono, chi gioca a
    /// 9 m resta illeso, la botte sparisce (niente collider); l'esplosione accende la vicina entro 4,5 m. Un monaco che nasce sale
    /// dal pavimento: parte sotto, in poco piu' di un secondo e' in piedi sulla navmesh e cammina.</summary>
    public sealed class BarrelTest : PlayerTest
    {
        protected override string Suite { get { return "barreltest"; } }
        protected override string PhotoFlag { get { return "-barrelshots"; } }
        protected override string OutFolder { get { return "botti"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<BarrelTest>("-barreltest"); }

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
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            var bs = Barrels.I;
            Check($"le botti d'olio santo ci sono ({(bs != null ? bs.List.Count : 0)})", bs != null && bs.List.Count > 0);
            if (Failed > 0) yield break;
            foreach (var b in bs.List)
            {
                var room = FloorDirector.I.cur.RoomAt(b.transform.position);
                Check($"botte {b.index} in una sala grande ({(room != null ? room.W * 0.1f : 0):F0}x{(room != null ? room.D * 0.1f : 0):F0} m)", room != null && room.W >= 100 && room.D >= 100);
            }
            // la botte da far scoppiare: una che abbia una compagna vicina, se c'e' (catena)
            Barrel target = bs.List[0], mate = null;
            foreach (var a in bs.List)
                foreach (var c in bs.List)
                    if (a != c && Vector3.Distance(a.transform.position, c.transform.position) < 3f) { target = a; mate = c; }
            Vector3 bp = target.transform.position;
            Vector3 away = (mate != null ? (bp - mate.transform.position) : Vector3.forward); away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            Vector3 stand = bp + away * 9f;
            if (!HotelNav.Ground(stand + Vector3.up, out Vector3 gs)) gs = bp + away * 9f;
            player.Teleport(gs + Vector3.up * 0.05f); player.hp = 100; player.shield = 50; player.invulnerable = false;
            yield return null;
            Face(player, bp + Vector3.up * 0.6f);
            if (Photos) { yield return new WaitForSeconds(0.3f); Photo("botte"); }
            Enemy.debugFreeze = true;
            // tre monaci attorno alla botte, allo stesso livello e in vista (vicino alle balconate un punto a caso finiva di sopra)
            var monks = new List<Enemy>();
            for (int i = 0; i < 24 && monks.Count < 3; i++)
            {
                float a = i * 0.785f + (i / 8) * 0.39f, r = 1.8f + (i / 8) * 0.5f;
                Vector3 q = bp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!UnityEngine.AI.NavMesh.SamplePosition(q, out var nh, 0.35f, UnityEngine.AI.NavMesh.AllAreas) || Mathf.Abs(nh.position.y - bp.y) > 0.2f) continue;
                if (!HotelNav.ClearLine(bp + Vector3.up * 0.6f, nh.position + Vector3.up * 1.2f)) continue;
                bool near = false;
                foreach (var m in monks) if (Vector3.Distance(m.transform.position, nh.position) < 1.0f) near = true;
                if (near) continue;
                monks.Add(Enemy.Spawn(Enemy.EnemyType.Monaco, nh.position));
            }
            Check($"tre monaci attorno alla botte ({monks.Count})", monks.Count == 3);
            yield return null;
            float health = player.hp + player.shield;
            int score0 = gm.score;
            target.Shot(50f, bp + Vector3.up * 0.5f, (bp - player.cam.position).normalized);
            yield return null;
            Check("un colpo da 50 la fa scoppiare", target.Gone);
            if (Photos) { yield return new WaitForSeconds(0.08f); Photo("botte_scoppio"); }
            yield return new WaitForSeconds(0.5f);
            int dead = monks.FindAll(m => m == null || m.IsDead).Count;
            Check($"i monaci attorno muoiono ({dead}/3)", dead == 3);
            Check("le uccisioni pagano", gm.score > score0);
            Check("chi gioca a 9 m resta illeso", Mathf.Abs(player.hp + player.shield - health) < 0.01f);
            var col = target.GetComponent<Collider>();
            Check("scoppiata, la botte non blocca piu'", col == null || !col.enabled);
            if (mate != null) Check("lo scoppio accende la botte vicina (catena)", mate.Gone);
            else Debug.Log("[PryingEyes] barreltest nessuna coppia di botti su questo piano: catena non provata");
            Enemy.debugFreeze = false;

            // nemico che sale dal pavimento
            Check("c'e' spazio per la salita", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            player.Teleport(arena + Vector3.up * 0.05f);
            yield return null;
            var riser = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 5f);
            Vector3 top = riser.transform.position;
            riser.RiseFromGround(1.2f);
            riser.MakeRelentless();
            yield return null;
            Check("nato, il monaco parte sotto il pavimento", riser.transform.position.y < top.y - 1.0f && riser.Rising);
            Face(player, top + Vector3.up * 0.8f);
            yield return new WaitForSeconds(0.6f);
            Photo("salita");
            yield return new WaitForSeconds(1.0f);
            Check("in poco piu' di un secondo e' in piedi", !riser.Rising && Mathf.Abs(riser.transform.position.y - top.y) < 0.15f);
            var agent = riser.GetComponent<UnityEngine.AI.NavMeshAgent>();
            Check("e torna sulla navmesh", agent != null && agent.enabled && agent.isOnNavMesh);
            Vector3 p1 = riser.transform.position;
            yield return new WaitForSeconds(1.0f);
            Check("e cammina verso il giocatore", Vector3.Distance(riser.transform.position, player.transform.position) < Vector3.Distance(p1, player.transform.position) - 0.3f);
            Enemy.ClearAll();
        }
    }
}
#endif
