using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    public sealed partial class ArchitectureTest
    {
        // -zombies -architecturetest -architectureaccess: prova fisica della prima porta a pagamento.
        IEnumerator RunAccess()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            var manager = RoomAccessDoors.I;
            Check("nuovo accesso alle aree disponibile", manager != null);
            if (manager == null) yield break;
            var fi = FloorDirector.I.cur; var gm = GameManager.I; var player = gm.player;
            // Ogni porta, da tutti e due i lati, in piedi a 1,5 m: il punto che si guarda per comprare deve vedersi. Il passaggio basso della stanza
            // Segreta (architrave a 1,3 m) aveva il punto proprio sull'architrave: la vista sbatteva nel muro e quella stanza non si poteva comprare.
            int unseen = 0;
            foreach (var each in manager.Doors)
                foreach (int side in new[] { each.opening.a, each.opening.b })
                {
                    Vector3 eye = each.Front(side); eye.y = each.shutter.center.y + player.eyeStand;
                    if (Interact.Visible(eye, each.Point, each.transform)) continue;
                    unseen++;
                    Debug.Log($"[PryingEyes] architecturetest porta non visibile: stanze {each.opening.a}-{each.opening.b}, lato {side}, stile {each.opening.style}, architrave {each.opening.top:F2} m, punto {each.Point}");
                }
            Check($"il punto di ogni porta si vede da entrambi i lati ({manager.Doors.Count} porte, {unseen} nascoste)", unseen == 0);
            Check("nessun obiettivo a candele", fi.candles.Count == 0);
            Check("nessuna candela interattiva nel piano", !System.Array.Exists(FindObjectsByType<Candelabro>(FindObjectsSortMode.None), c => c.objective));
            Check("stealth disabilitato", !Stealth.Enabled);
            int main = fi.plan.rooms.FindAll(r => !r.IsLift).Count;
            Check("da sette a nove aree principali", main >= 7 && main <= 9);
            Check("almeno due grandi atri a tre quote", fi.plan.rooms.FindAll(r => r.GalleryLevels == 2).Count >= 2);
            PaidRoomDoor door = null;
            foreach (var candidate in manager.Doors) if (candidate.TargetRoom >= 0) { door = candidate; break; }
            Check("porta verso una nuova area", door != null);
            if (door == null) yield break;
            var start = fi.plan.rooms[manager.Access.StartRoom];
            var opening = door.opening;
            Vector3 inward = opening.alongX ? Vector3.forward * Mathf.Sign(start.CenterZ - opening.line * 0.1f) : Vector3.right * Mathf.Sign(start.CenterX - opening.line * 0.1f);
            Vector3 position = door.Point + inward * 1.6f; position.y = 0.1f;
            Check("posto davanti alla porta", NavMesh.SamplePosition(position, out var origin, 0.8f, HotelNav.EnemyMask));
            Vector3 beyond = door.Point - inward * 1.6f; beyond.y = 0.1f;
            Check("pavimento oltre la porta", NavMesh.SamplePosition(beyond, out var target, 0.8f, NavMesh.AllAreas));
            var path = new NavMeshPath();
            bool connected = NavMesh.CalculatePath(origin.position, target.position, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
            Check("porta chiusa blocca il percorso dei nemici", !connected);
            player.Teleport(origin.position); player.invulnerable = true;
            Vector3 look = door.Point - (origin.position + Vector3.up * player.eyeStand);
            player.yaw = Mathf.Atan2(look.x, look.z); player.pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude);
            PlayerController.holdLook = false; PlayerController.botDir = -inward;
            yield return new WaitForSeconds(0.7f);
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("porta chiusa blocca il controller reale", Vector3.Dot(player.transform.position - door.shutter.center, inward) > 0.2f);
            int room = door.TargetRoom, cost = manager.Access.Price(room);
            gm.AddPoints(cost - 1 - gm.score);
            yield return null;
            Interact.Tick(player.cam);
            Check("F seleziona la porta", Interact.Current == door);
            Interact.TryUse();
            Check("punti insufficienti non aprono o addebitano", !manager.Access.IsOpen(room) && gm.score == cost - 1);
            gm.AddPoints(1); Interact.Tick(player.cam); Interact.TryUse();
            Check("pagamento apre la nuova area una sola volta", manager.Access.IsOpen(room) && gm.score == 0);
            yield return new WaitForSeconds(1.8f);
            connected = NavMesh.CalculatePath(origin.position, target.position, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
            Check("porta aperta abilita il percorso dei nemici", connected);
            PlayerController.holdLook = false; PlayerController.botDir = -inward;
            yield return new WaitForSeconds(0.8f);
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("controller attraversa la porta pagata", Vector3.Dot(player.transform.position - door.shutter.center, inward) < -0.5f);
            Check("nessun doppio pagamento", !manager.TryBuy(Targets.Local, room, 100000, out int duplicate) && duplicate == 0);
            var enemy = Enemy.Spawn(Enemy.EnemyType.Monaco, target.position + Vector3.up * 0.05f);
            Check("monaco più rapido fin dalla nascita", enemy.speed >= 2.08f);
            Check("nemico già in caccia senza rilevamento", enemy.alert == Alert.Hunting);
            Enemy.Remove(enemy);
        }
    }
}
