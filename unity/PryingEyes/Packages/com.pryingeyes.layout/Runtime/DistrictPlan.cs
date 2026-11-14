using System;
using System.Collections.Generic;

namespace Production.Layout
{
    /// <summary>All coordinates are metres. A compact 2x2 district; no rendering, content IDs or game state.</summary>
    [Serializable]
    public sealed class DistrictPlan
    {
        [Serializable] public struct Point
        {
            public float x, z;
            public Point(float x, float z) { this.x = x; this.z = z; }
        }
        [Serializable] public sealed class Block
        {
            public string id;
            public Point pavement, building, entrance, lamp;
            public float pavementWidth, pavementDepth, width, depth, height;
            public int facing;
            public bool alternate;
        }
        [Serializable] public struct Panel
        {
            public Point position;
            public int yaw;
        }

        public int seed;
        public float halfWidth, halfDepth, roadHalfWidth;
        public Block[] blocks;
        public Point[] spawns;
        public Panel[] panels;

        /// <summary>Rejects nonfinite dimensions, roads narrower than 8 m and buildings smaller than 20x12 m.</summary>
        public static DistrictPlan Create(int seed, float halfWidth, float halfDepth, float roadHalfWidth)
        {
            bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
            if (!Finite(halfWidth) || !Finite(halfDepth) || !Finite(roadHalfWidth) || !Finite(halfWidth * 2) || !Finite(halfDepth * 2) || roadHalfWidth < 4 ||
                halfWidth - roadHalfWidth - 9 < 20 || halfDepth - roadHalfWidth - 9 < 12)
                throw new ArgumentException("Roads require 8 m clearance and buildings require 20x12 m; dimensions must be finite.");
            var plan = new DistrictPlan { seed = seed, halfWidth = halfWidth, halfDepth = halfDepth, roadHalfWidth = roadHalfWidth };
            var blocks = new List<Block>(); var random = new Random(seed);
            float pw = halfWidth - roadHalfWidth - 3, pd = halfDepth - roadHalfWidth - 3;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float cx = sx * (halfWidth + roadHalfWidth) / 2, cz = sz * (halfDepth + roadHalfWidth) / 2;
                    var building = new Point(cx + sx * 2, cz + sz * 2);
                    blocks.Add(new Block { id = $"Building_{sx}_{sz}", pavement = new Point(cx, cz), building = building,
                        pavementWidth = pw, pavementDepth = pd, width = pw - 6, depth = pd - 6,
                        height = 8 + random.Next(0, 4) * 2.5f, facing = -sz, alternate = sx != sz,
                        entrance = new Point(building.x, building.z - sz * (pd - 6) / 2),
                        lamp = new Point(cx - sx * (pw / 2 - 1), cz - sz * (pd / 2 - 1)) });
                }
            plan.blocks = blocks.ToArray();
            float x = (halfWidth + roadHalfWidth) / 2 + 2;
            float z = (halfDepth + roadHalfWidth) / 2 + 2 - (pd - 6) / 2 - .6f;
            plan.panels = new[] { new Panel { position = new Point(-x + 8, -z), yaw = 0 },
                new Panel { position = new Point(x - 8, -z), yaw = 0 }, new Panel { position = new Point(-x + 8, z), yaw = 180 } };
            plan.spawns = new[] { new Point(0, -halfDepth + 5), new Point(0, halfDepth - 5),
                new Point(-halfWidth + 5, 0), new Point(halfWidth - 5, 0) };
            return plan;
        }
    }
}
