using System;
using System.Collections.Generic;
using System.Globalization;

namespace Production.Layout
{
    /// <summary>
    /// Furniture and portals in local metres. Geometry is independent of rendering and game rules.
    /// <para>Part kinds: <c>box</c>, <c>cylinder</c>, <c>label</c>, <c>light</c> and <c>stairs</c>. A stair is one recipe entry
    /// that stands for a whole flight: <c>position</c> and <c>size</c> are the solid bounding box (x across, y total rise,
    /// z run), <c>yaw</c> (a multiple of 90) is the climbing direction (0 climbs towards +z) and <c>steps</c> is the number of
    /// risers. The plan holds the flight already expanded into solid boxes <c>&lt;id&gt;_01</c>...<c>&lt;id&gt;_NN</c> that
    /// rise from the floor, so renderers and physics only ever see boxes.</para>
    /// <para>A route point is a place to stand: <c>x</c>/<c>z</c> on the plan and <c>y</c> the height of the surface under the
    /// feet (0 is the room floor). The plan rejects points inside furniture and points in the air.</para>
    /// <para>A view is a named picture to take: where to stand (checked like a route point) and what to look at, in metres
    /// above the floor. The room's author chooses the views, so evidence is comparable between runs.</para>
    /// </summary>
    [Serializable]
    public sealed class RoomPlan
    {
        /// <summary>Limits a walking agent can climb: the largest riser and the smallest tread of a flight, and its least width.</summary>
        public const float MaxRiser = .2f, MinTread = .25f, MinStairWidth = 1.2f;
        const float Surface = .05f;     // how close a route height must be to the top of the solid it stands on

        [Serializable] public struct Vector
        {
            public float x, y, z;
            public Vector(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        }
        [Serializable] public struct Waypoint
        {
            public float x, y, z;
            public Waypoint(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        }
        [Serializable] public sealed class Part
        {
            public string id, kind, material, text;
            public Vector position, size;
            public float yaw, intensity, range;
            public int steps;
            public bool collider;
        }
        [Serializable] public sealed class Door
        {
            public string side;
            public float center, width, height;
        }
        [Serializable] public sealed class View
        {
            public string name;
            public Waypoint position;
            public Vector target;
        }
        [Serializable] public sealed class Recipe
        {
            public string id, name;
            public float width, depth;
            public Door[] doors;
            public Part[] parts;
            public Waypoint[] route;
            public View[] views;
        }

        public string id;
        public float width, depth;
        public Door[] doors;
        public Part[] parts;
        public Waypoint[] route;
        public View[] views;

        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static bool Valid(Vector p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        /// <summary>True when the part is turned a quarter turn, so its local x runs along the plan's z axis.</summary>
        static bool Turned(float yaw) => Math.Abs(yaw % 180) == 90;

        public static RoomPlan Create(Recipe recipe, float width, float depth)
        {
            if (recipe == null || !Finite(width) || !Finite(depth) || width < 20 || depth < 12 ||
                !Finite(recipe.width) || !Finite(recipe.depth) || recipe.width < 20 || recipe.depth < 12 ||
                recipe.doors == null || recipe.parts == null || recipe.route == null || recipe.route.Length < 2)
                throw new ArgumentException("ROOM_RECIPE_INVALID");
            float sx = width / recipe.width, sz = depth / recipe.depth;
            var plan = new RoomPlan { id = recipe.id, width = width, depth = depth };
            var portals = new List<Door>(); var sides = new HashSet<string>();
            foreach (var door in recipe.doors)
            {
                if (door.side != "front" && door.side != "back" && door.side != "left" && door.side != "right")
                    throw new ArgumentException("ROOM_DOOR_SIDE: " + door.side);
                bool front = door.side == "front" || door.side == "back";
                float scale = front ? sx : sz, extent = front ? width : depth;
                var d = new Door { side = door.side, center = door.center * scale, width = door.width * scale, height = door.height };
                if (!sides.Add(d.side) || !Finite(d.center) || !Finite(d.width) || !Finite(d.height) || d.width < 2.4f || d.height < 2.6f || Math.Abs(d.center) + d.width / 2 > extent / 2 - .4f)
                    throw new ArgumentException("ROOM_DOOR_CLEARANCE: " + d.side);
                portals.Add(d);
            }
            if (portals.Count < 2) throw new ArgumentException("ROOM_ESCAPE_ROUTE_MISSING");
            plan.doors = portals.ToArray();
            var parts = new List<Part>(); var ids = new HashSet<string>();
            foreach (var source in recipe.parts)
            {
                // Size is in the part's own frame: a quarter turn swaps which plan axis each side is stretched along.
                float scaleX = Turned(source.yaw) ? sz : sx, scaleZ = Turned(source.yaw) ? sx : sz;
                var p = new Part { id = source.id, kind = source.kind, material = source.material, text = source.text ?? "",
                    position = new Vector(source.position.x * sx, source.position.y, source.position.z * sz),
                    size = new Vector(source.size.x * scaleX, source.size.y, source.size.z * scaleZ), yaw = source.yaw,
                    collider = source.collider, intensity = source.intensity, range = source.range, steps = source.steps };
                if (string.IsNullOrWhiteSpace(p.id) || !ids.Add(p.id) || !Valid(p.position) || !Valid(p.size) ||
                    p.size.x <= 0 || p.size.y <= 0 || p.size.z <= 0 || !Finite(p.yaw) || !Finite(p.intensity) || !Finite(p.range))
                    throw new ArgumentException("ROOM_PART_INVALID: " + p.id);
                if (p.kind != "box" && p.kind != "cylinder" && p.kind != "label" && p.kind != "light" && p.kind != "stairs")
                    throw new ArgumentException("ROOM_PART_KIND: " + p.id);
                if (p.kind != "stairs" && p.steps != 0) throw new ArgumentException("ROOM_PART_INVALID: " + p.id + " (steps belong to stairs only)");
                if (p.kind == "stairs") CheckFlight(p);
                if (p.collider)
                {
                    if (p.kind == "label" || p.kind == "light") throw new ArgumentException("ROOM_COLLIDER_KIND: " + p.id);
                    if (p.yaw % 90 != 0) throw new ArgumentException("ROOM_SOLID_ROTATION: " + p.id);
                    float halfX = (p.yaw % 180 == 0 ? p.size.x : p.size.z) / 2;
                    float halfZ = (p.yaw % 180 == 0 ? p.size.z : p.size.x) / 2;
                    if (Math.Abs(p.position.x) + halfX > width / 2 || Math.Abs(p.position.z) + halfZ > depth / 2 || p.position.y - p.size.y / 2 < -.001f)
                        throw new ArgumentException("ROOM_SOLID_OUTSIDE: " + p.id);
                    foreach (var d in plan.doors)
                    {
                        bool front = d.side == "front" || d.side == "back";
                        float along = front ? p.position.x : p.position.z, halfAlong = front ? halfX : halfZ;
                        float inward = d.side == "front" ? depth / 2 - p.position.z : d.side == "back" ? depth / 2 + p.position.z :
                            d.side == "right" ? width / 2 - p.position.x : width / 2 + p.position.x;
                        float halfAcross = front ? halfZ : halfX;
                        if (p.position.y - p.size.y / 2 < d.height && p.position.y + p.size.y / 2 > .3f &&
                            Math.Abs(along - d.center) < d.width / 2 + .4f + halfAlong && inward - halfAcross < 2.5f)
                            throw new ArgumentException("PASSAGE_BLOCKED: " + p.id + " at " + d.side);
                    }
                }
                if (p.kind == "stairs") foreach (var step in Flight(p)) { if (!ids.Add(step.id)) throw new ArgumentException("ROOM_PART_INVALID: " + step.id); parts.Add(step); }
                else parts.Add(p);
            }
            plan.parts = parts.ToArray(); plan.route = new Waypoint[recipe.route.Length];
            for (int i = 0; i < plan.route.Length; i++)
            {
                var point = recipe.route[i];
                if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z) || point.y < 0) throw new ArgumentException("ROOM_ROUTE_INVALID");
                plan.route[i] = new Waypoint(point.x * sx, point.y, point.z * sz);
                if (!Finite(plan.route[i].x) || !Finite(plan.route[i].z)) throw new ArgumentException("ROOM_ROUTE_INVALID");
                CheckStanding(plan.parts, plan.route[i], "route point " + i);
            }
            plan.views = new View[recipe.views == null ? 0 : recipe.views.Length]; var names = new HashSet<string>();
            for (int i = 0; i < plan.views.Length; i++)
            {
                var view = recipe.views[i];
                if (view == null || string.IsNullOrWhiteSpace(view.name) || !names.Add(view.name) || !Finite(view.position.x) || !Finite(view.position.y) ||
                    !Finite(view.position.z) || view.position.y < 0 || !Valid(view.target))
                    throw new ArgumentException("ROOM_VIEW_INVALID: " + (view == null ? i.ToString() : view.name));
                plan.views[i] = new View { name = view.name, position = new Waypoint(view.position.x * sx, view.position.y, view.position.z * sz),
                    target = new Vector(view.target.x * sx, view.target.y, view.target.z * sz) };
                CheckStanding(plan.parts, plan.views[i].position, "view " + view.name);
            }
            return plan;
        }

        /// <summary>A flight must be climbable by a walking agent: not too steep, not too narrow.</summary>
        static void CheckFlight(Part p)
        {
            float riser = p.size.y / p.steps, tread = p.size.z / p.steps;
            if (!p.collider) throw new ArgumentException("ROOM_STAIRS_COLLIDER: " + p.id);
            if (p.steps < 2 || p.steps > 64) throw new ArgumentException("ROOM_STAIRS_STEPS: " + p.id + " has " + p.steps + ", expected 2 to 64");
            if (riser > MaxRiser + 1e-4f) throw new ArgumentException("ROOM_STAIRS_STEEP: " + p.id + " riser " + Number(riser) + " m, at most " + Number(MaxRiser));
            if (tread < MinTread - 1e-4f) throw new ArgumentException("ROOM_STAIRS_SHORT: " + p.id + " tread " + Number(tread) + " m, at least " + Number(MinTread));
            if (p.size.x < MinStairWidth - 1e-4f) throw new ArgumentException("ROOM_STAIRS_NARROW: " + p.id + " width " + Number(p.size.x) + " m, at least " + Number(MinStairWidth));
        }

        /// <summary>The solid boxes of a flight, lowest first: every step is a block from the floor up to its own tread.</summary>
        static IEnumerable<Part> Flight(Part p)
        {
            float riser = p.size.y / p.steps, tread = p.size.z / p.steps, floor = p.position.y - p.size.y / 2;
            int quarter = (int)Math.Round(p.yaw / 90f);
            quarter = (quarter % 4 + 4) % 4;                    // 0 climbs towards +z, 1 +x, 2 -z, 3 -x
            float dx = quarter == 1 ? 1 : quarter == 3 ? -1 : 0, dz = quarter == 0 ? 1 : quarter == 2 ? -1 : 0;
            for (int i = 0; i < p.steps; i++)
            {
                float along = -p.size.z / 2 + (i + .5f) * tread, height = (i + 1) * riser;
                yield return new Part { id = p.id + "_" + (i + 1).ToString("00", CultureInfo.InvariantCulture), kind = "box", material = p.material, text = "",
                    position = new Vector(p.position.x + dx * along, floor + height / 2, p.position.z + dz * along),
                    size = new Vector(p.size.x, height, tread), yaw = p.yaw, collider = true };
            }
        }

        /// <summary>A place to stand is either on the floor or on the top face of a solid, and never inside a solid a body would meet.</summary>
        static void CheckStanding(Part[] parts, Waypoint point, string what)
        {
            bool supported = point.y <= Surface;
            foreach (var s in parts)
            {
                if (!s.collider) continue;
                float halfX = (Turned(s.yaw) ? s.size.z : s.size.x) / 2, halfZ = (Turned(s.yaw) ? s.size.x : s.size.z) / 2;
                float dx = (point.x - s.position.x) / halfX, dz = (point.z - s.position.z) / halfZ;
                bool under = s.kind == "cylinder" ? dx * dx + dz * dz <= 1 : Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1;
                if (!under) continue;
                float bottom = s.position.y - s.size.y / 2, top = s.position.y + s.size.y / 2;
                if (Math.Abs(top - point.y) <= Surface) supported = true;
                else if (bottom < point.y + 1.8f && top > point.y + .3f)
                    throw new ArgumentException("ROOM_STAND_BLOCKED: " + what + " is inside " + s.id);
            }
            if (!supported) throw new ArgumentException("ROOM_STAND_FLOATING: " + what + " at height " + Number(point.y) + " m rests on nothing");
        }
    }
}
