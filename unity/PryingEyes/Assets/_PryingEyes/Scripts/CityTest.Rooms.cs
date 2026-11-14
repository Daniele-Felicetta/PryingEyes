using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Production.Layout;
using Newtonsoft.Json.Linq;
using System.IO;

namespace PryingEyes
{
    public sealed partial class CityTest
    {
        IEnumerator RunRooms()
        {
            var markers = FindObjectsByType<CityRoom>(FindObjectsSortMode.None);
            var gm = GameManager.I; var player = gm.player;
            var catalog = JObject.Parse(Resources.Load<TextAsset>("Production/rooms").text);
            foreach (var marker in markers)
            {
                var plan = marker.Plan; bool geometry = true, collision = true, palette = true;
                foreach (var part in plan.parts)
                {
                    var child = marker.transform.Find(part.id);
                    if (child == null) { geometry = collision = false; continue; }
                    Vector3 center = marker.transform.TransformPoint(new Vector3(part.position.x, part.position.y + .24f, part.position.z));
                    geometry &= Vector3.Distance(child.position, center) < .001f;
                    var collider = child.GetComponent<Collider>();
                    collision &= part.collider == (collider != null && collider.enabled);
                    if (part.kind == "box" || part.kind == "cylinder")
                    {
                        var renderer = child.GetComponent<Renderer>();
                        Vector3 size = new Vector3(part.size.x, part.size.y, part.size.z);
                        if (Mathf.Abs(part.yaw % 180) == 90) size = new Vector3(size.z, size.y, size.x);
                        geometry &= renderer != null && Vector3.Distance(renderer.bounds.size, size) < .002f;
                        if (collider != null) collision &= Vector3.Distance(collider.bounds.size, size) < .002f;
                        foreach (var material in catalog["materials"])
                            if ((string)material["id"] == part.material)
                            {
                                var color = renderer.sharedMaterial.GetColor("_BaseColor");
                                palette &= Mathf.Abs(color.r - (float)material["color"][0]) < .0001f &&
                                    Mathf.Abs(color.g - (float)material["color"][1]) < .0001f && Mathf.Abs(color.b - (float)material["color"][2]) < .0001f;
                            }
                    }
                }
                Check("ROOM_PARTS_MATCH", marker.recipeId, geometry, plan.parts.Length + " parts", "baked placements and sizes equal recipe");
                Check("ROOM_COLLIDERS_MATCH", marker.recipeId, collision, collision.ToString(), "planned solids have correct collision bounds");
                Check("ROOM_PALETTE_MATCH", marker.recipeId, palette, palette.ToString(), "materials retain recipe colors");
                var first = plan.route[0]; player.Teleport(marker.transform.TransformPoint(new Vector3(first.x, .35f, first.z)));
                yield return new WaitForSeconds(.2f);
                var path = new NavMeshPath();
                for (int leg = 1; leg < plan.route.Length; leg++)
                {
                    var point = plan.route[leg]; Vector3 goal = marker.transform.TransformPoint(new Vector3(point.x, .35f, point.z));
                    bool target = NavMesh.SamplePosition(goal, out var to, .8f, HotelNav.EnemyMask);
                    bool source = NavMesh.SamplePosition(player.transform.position, out var from, .8f, HotelNav.EnemyMask);
                    bool connected = target && source && NavMesh.CalculatePath(from.position, to.position, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
                    Check("ROOM_ROUTE_NAV", marker.recipeId + " leg " + leg, connected, path.status.ToString(), "complete path around furniture and through exits");
                    if (!connected) break;
                    PlayerController.holdLook = false;
                    float deadline = Time.time + 25;
                    while (Time.time < deadline && Vector3.Distance(player.transform.position, to.position) > .6f)
                    {
                        if (NavMesh.SamplePosition(player.transform.position, out from, .8f, HotelNav.EnemyMask))
                            NavMesh.CalculatePath(from.position, to.position, HotelNav.EnemyMask, path);
                        Vector3 next = to.position;
                        if (path.corners.Length > 1)
                        {
                            next = path.corners[1];
                            if (Vector3.Distance(next, player.transform.position) < .4f && path.corners.Length > 2) next = path.corners[2];
                        }
                        var direction = next - player.transform.position; direction.y = 0; PlayerController.botDir = direction.normalized;
                        yield return null;
                    }
                    PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
                    Check("ROOM_ROUTE_CONTROLLER", marker.recipeId + " leg " + leg, Vector3.Distance(player.transform.position, to.position) <= .6f,
                        player.transform.position.ToString(), to.position.ToString());
                }
                if (shots)
                {
                    var frontSign = System.Array.Find(plan.parts, p => p.kind == "label" && p.position.z > plan.depth / 2);
                    player.Teleport(marker.transform.TransformPoint(new Vector3(-8, .35f, 5.2f)));
                    Aim(marker.transform.TransformPoint(new Vector3(0, 1.2f, -2.4f)));
                    yield return new WaitForSeconds(.3f);
                    if (frontSign != null) Check("ROOM_SIGN_HIDDEN_INSIDE", marker.recipeId, !marker.transform.Find(frontSign.id).GetComponent<Renderer>().enabled,
                        "outside sign viewed from inside", "no mirrored lettering through the wall");
                    GameManager.CamShot(Path.Combine(output, marker.recipeId + "_interior.png"), null, true);
                    player.Teleport(marker.transform.TransformPoint(new Vector3(0, .2f, plan.depth / 2 + 5)));
                    Aim(marker.transform.TransformPoint(new Vector3(0, 3.9f, plan.depth / 2)));
                    yield return new WaitForSeconds(.3f);
                    if (frontSign != null) Check("ROOM_SIGN_VISIBLE_OUTSIDE", marker.recipeId, marker.transform.Find(frontSign.id).GetComponent<Renderer>().enabled,
                        "outside sign viewed from the street", "readable lettering on the front");
                    GameManager.CamShot(Path.Combine(output, marker.recipeId + "_exterior.png"), null, true);
                }
                var side = System.Array.Find(plan.doors, d => d.side == "right");
                if (side != null)
                {
                    var outside = marker.transform.TransformPoint(new Vector3(plan.width / 2 + 3.5f, .35f, side.center));
                    NavMesh.SamplePosition(outside, out var standing, 1, HotelNav.EnemyMask); player.Teleport(standing.position);
                    var spawn = marker.transform.TransformPoint(new Vector3(-3.2f * plan.width / 25, .35f, side.center));
                    NavMesh.SamplePosition(spawn, out var origin, 1, HotelNav.EnemyMask);
                    var enemy = Enemy.Spawn(Enemy.EnemyType.Spazzino, origin.position);
                    int contacts = GameManager.damageTaken; bool crossed = false; float deadline = Time.time + 30;
                    while (enemy != null && Time.time < deadline && GameManager.damageTaken == contacts)
                    {
                        var local = marker.transform.InverseTransformPoint(enemy.transform.position);
                        crossed |= local.x > plan.width / 2 + .2f && Mathf.Abs(local.z - side.center) < side.width / 2 + .5f;
                        yield return null;
                    }
                    Check("ROOM_ENEMY_ESCAPE_PURSUIT", marker.recipeId, crossed && GameManager.damageTaken > contacts,
                        $"crossed={crossed}, contacts={GameManager.damageTaken - contacts}", "enemy crosses side exit and reaches the player");
                    if (enemy != null) Enemy.Remove(enemy);
                }
            }
        }

        void Aim(Vector3 target)
        {
            var player = GameManager.I.player; var look = target - player.cam.position;
            player.yaw = Mathf.Atan2(look.x, look.z); player.pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude);
        }
    }
}
