using System;
using System.Collections.Generic;

namespace Production.Layout
{
    /// <summary>Furniture and portals in local metres. Geometry is independent of rendering and game rules.</summary>
    [Serializable]
    public sealed class RoomPlan
    {
        [Serializable] public struct Vector
        {
            public float x, y, z;
            public Vector(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        }
        [Serializable] public sealed class Part
        {
            public string id, kind, material, text;
            public Vector position, size;
            public float yaw, intensity, range;
            public bool collider;
        }
        [Serializable] public sealed class Door
        {
            public string side;
            public float center, width, height;
        }
        [Serializable] public sealed class Recipe
        {
            public string id, name;
            public float width, depth;
            public Door[] doors;
            public Part[] parts;
            public DistrictPlan.Point[] route;
        }

        public string id;
        public float width, depth;
        public Door[] doors;
        public Part[] parts;
        public DistrictPlan.Point[] route;

        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static bool Valid(Vector p) => Finite(p.x) && Finite(p.y) && Finite(p.z);

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
                var p = new Part { id = source.id, kind = source.kind, material = source.material, text = source.text ?? "",
                    position = new Vector(source.position.x * sx, source.position.y, source.position.z * sz),
                    size = new Vector(source.size.x * sx, source.size.y, source.size.z * sz), yaw = source.yaw,
                    collider = source.collider, intensity = source.intensity, range = source.range };
                if (string.IsNullOrWhiteSpace(p.id) || !ids.Add(p.id) || !Valid(p.position) || !Valid(p.size) ||
                    p.size.x <= 0 || p.size.y <= 0 || p.size.z <= 0 || !Finite(p.yaw) || !Finite(p.intensity) || !Finite(p.range))
                    throw new ArgumentException("ROOM_PART_INVALID: " + p.id);
                if (p.kind != "box" && p.kind != "cylinder" && p.kind != "label" && p.kind != "light")
                    throw new ArgumentException("ROOM_PART_KIND: " + p.id);
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
                parts.Add(p);
            }
            plan.parts = parts.ToArray(); plan.route = new DistrictPlan.Point[recipe.route.Length];
            for (int i = 0; i < plan.route.Length; i++)
            {
                var point = recipe.route[i];
                if (!Finite(point.x) || !Finite(point.z)) throw new ArgumentException("ROOM_ROUTE_INVALID");
                plan.route[i] = new DistrictPlan.Point(point.x * sx, point.z * sz);
                if (!Finite(plan.route[i].x) || !Finite(plan.route[i].z)) throw new ArgumentException("ROOM_ROUTE_INVALID");
            }
            return plan;
        }
    }
}
