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
                var plan = marker.Plan; bool geometry = true, collision = true, palette = true, emission = true;
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
                                float intensity = (float)material["emission"];
                                var glow = renderer.sharedMaterial.GetColor("_EmissionColor");
                                emission &= renderer.sharedMaterial.IsKeywordEnabled("_EMISSION") == (intensity > 0) &&
                                    Mathf.Abs(glow.r - color.r * intensity) < .0001f && Mathf.Abs(glow.g - color.g * intensity) < .0001f &&
                                    Mathf.Abs(glow.b - color.b * intensity) < .0001f;
                            }
                    }
                }
                Check("ROOM_PARTS_MATCH", marker.recipeId, geometry, plan.parts.Length + " parts", "baked placements and sizes equal recipe");
                Check("ROOM_COLLIDERS_MATCH", marker.recipeId, collision, collision.ToString(), "planned solids have correct collision bounds");
                Check("ROOM_PALETTE_MATCH", marker.recipeId, palette, palette.ToString(), "materials retain recipe colors");
                Check("ROOM_EMISSION_MATCH", marker.recipeId, emission, emission.ToString(), "emissive keyword and color retain recipe intensity");
                // A place to stand: the plan's x/z, and y is the height of the surface under the feet (the room floor is 0).
                Vector3 Stand(RoomPlan.Waypoint spot) => marker.transform.TransformPoint(new Vector3(spot.x, .35f + spot.y, spot.z));
                float Surface(float height) => marker.transform.TransformPoint(new Vector3(0, .24f + height, 0)).y;
                var first = plan.route[0]; player.Teleport(Stand(first));
                yield return new WaitForSeconds(.2f);
                var path = new NavMeshPath();
                for (int leg = 1; leg < plan.route.Length; leg++)
                {
                    var point = plan.route[leg]; Vector3 goal = Stand(point);
                    bool target = NavMesh.SamplePosition(goal, out var to, .8f, HotelNav.EnemyMask);
                    bool source = NavMesh.SamplePosition(player.transform.position, out var from, .8f, HotelNav.EnemyMask);
                    bool connected = target && source && NavMesh.CalculatePath(from.position, to.position, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
                    Check("ROOM_ROUTE_NAV", marker.recipeId + " leg " + leg, connected, path.status.ToString(), "complete path around furniture, up stairs and through exits");
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
                    if (point.y > .5f)
                        Check("ROOM_ROUTE_ELEVATED", marker.recipeId + " leg " + leg, Mathf.Abs(player.transform.position.y - Surface(point.y)) < .3f,
                            $"feet at {player.transform.position.y:F2} m", $"standing on the surface at {Surface(point.y):F2} m");
                }
                // Whoever stands on the highest surface of the route is reached by a walker that starts on the floor: the stairs work for enemies too.
                var perch = System.Array.FindLast(plan.route, spot => spot.y > .5f);
                if (perch.y > .5f)
                {
                    bool deckNav = NavMesh.SamplePosition(Stand(perch), out var deck, 1, HotelNav.EnemyMask);
                    bool groundNav = NavMesh.SamplePosition(Stand(first), out var ground, 1, HotelNav.EnemyMask);
                    Check("ROOM_PERCH_NAV", marker.recipeId, deckNav && groundNav, $"deck={deckNav}, floor={groundNav}", "navigation under the highest route point and under the first one");
                    if (deckNav && groundNav)
                    {
                        player.Teleport(deck.position);
                        var climber = Enemy.Spawn(Enemy.EnemyType.Spazzino, ground.position);
                        int hits = GameManager.damageTaken; float highest = ground.position.y, until = Time.time + 40;
                        while (climber != null && Time.time < until && GameManager.damageTaken == hits)
                        { highest = Mathf.Max(highest, climber.transform.position.y); yield return null; }
                        Check("ROOM_ENEMY_CLIMB", marker.recipeId, GameManager.damageTaken > hits && highest > deck.position.y - .6f,
                            $"contacts={GameManager.damageTaken - hits}, highest={highest:F2} m", $"enemy climbs to the player standing at {deck.position.y:F2} m");
                        if (climber != null) Enemy.Remove(climber);
                    }
                }
                if (shots)
                {
                    var frontSign = System.Array.Find(plan.parts, p => p.kind == "label" && p.position.z > plan.depth / 2);
                    foreach (var view in plan.views)
                    {
                        player.Teleport(Stand(view.position));
                        Aim(marker.transform.TransformPoint(new Vector3(view.target.x, .24f + view.target.y, view.target.z)));
                        yield return new WaitForSeconds(.3f);
                        bool inside = Mathf.Abs(view.position.x) < plan.width / 2 && Mathf.Abs(view.position.z) < plan.depth / 2;
                        if (frontSign != null && inside) Check("ROOM_SIGN_HIDDEN_INSIDE", marker.recipeId + " view " + view.name, !marker.transform.Find(frontSign.id).GetComponent<Renderer>().enabled,
                            "outside sign viewed from inside", "no mirrored lettering through the wall");
                        GameManager.CamShot(Path.Combine(output, marker.recipeId + "_" + view.name + ".png"), null, true);
                    }
                    player.Teleport(marker.transform.TransformPoint(new Vector3(0, .2f, plan.depth / 2 + 5)));
                    Aim(marker.transform.TransformPoint(new Vector3(0, 3.9f, plan.depth / 2)));
                    yield return new WaitForSeconds(.3f);
                    if (frontSign != null) Check("ROOM_SIGN_VISIBLE_OUTSIDE", marker.recipeId, marker.transform.Find(frontSign.id).GetComponent<Renderer>().enabled,
                        "outside sign viewed from the street", "readable lettering on the front");
                    GameManager.CamShot(Path.Combine(output, marker.recipeId + "_exterior.png"), null, true);
                }
                // A second way out: a walker crosses the other door of the room to reach someone standing outside it.
                var side = System.Array.Find(plan.doors, d => d.side != "front");
                if (side != null)
                {
                    bool onWall = side.side == "left" || side.side == "right";        // a side wall; otherwise the back wall
                    float sign = side.side == "right" ? 1 : -1, half = (onWall ? plan.width : plan.depth) / 2, scale = (onWall ? plan.width : plan.depth) / (onWall ? 25 : 15);
                    Vector3 Local(float outward, float lateral) => onWall ? new Vector3(sign * outward, .35f, lateral) : new Vector3(lateral, .35f, sign * outward);
                    NavMesh.SamplePosition(marker.transform.TransformPoint(Local(half + 3.5f, side.center)), out var standing, 1, HotelNav.EnemyMask); player.Teleport(standing.position);
                    NavMesh.SamplePosition(marker.transform.TransformPoint(Local(-3.2f * scale, side.center)), out var origin, 1, HotelNav.EnemyMask);
                    var enemy = Enemy.Spawn(Enemy.EnemyType.Spazzino, origin.position);
                    int contacts = GameManager.damageTaken; bool crossed = false; float deadline = Time.time + 30;
                    while (enemy != null && Time.time < deadline && GameManager.damageTaken == contacts)
                    {
                        var local = marker.transform.InverseTransformPoint(enemy.transform.position);
                        float outward = onWall ? sign * local.x : sign * local.z, lateral = onWall ? local.z : local.x;
                        crossed |= outward > half + .2f && Mathf.Abs(lateral - side.center) < side.width / 2 + .5f;
                        yield return null;
                    }
                    Check("ROOM_ENEMY_ESCAPE_PURSUIT", marker.recipeId, crossed && GameManager.damageTaken > contacts,
                        $"crossed={crossed}, contacts={GameManager.damageTaken - contacts}", $"enemy crosses the {side.side} exit and reaches the player");
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
