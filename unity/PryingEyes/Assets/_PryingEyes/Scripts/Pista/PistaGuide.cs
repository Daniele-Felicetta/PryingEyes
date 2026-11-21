using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The driving line drawn on the road: a narrow ribbon along the racing line the computer riders follow, in front of the player's bike, green where the bike may accelerate, yellow where it holds its speed and red
    /// where it is faster than the plan allows and must brake (DrivingLine.Advise, from the speed plan of the bike the player rides and the speed of the bike now: the red begins at the braking point of a rider at that speed and
    /// shrinks as the bike brakes, and a bike that stands still sees only green). What is drawn depends on the aids level of the rider (DrivingLine.Shows: Completi all three colours, Sportivi only the red, Nessuno nothing at all,
    /// and then there is no guide). The ribbon is one strip of two vertices per sample of the lap, made once per race with the same height function as the road and the physics (it follows the camber) and floating a few centimetres
    /// above the asphalt and the painted lines, so that it does not flicker against them; every frame only the triangles of the stretch ahead are given to the three colours (three submeshes, three materials).</summary>
    public sealed class PistaGuide
    {
        /// <summary>How far the ribbon floats above the asphalt (m: the road itself sits 1.4 cm above its base, the painted lines at 2 cm) and half of its width.</summary>
        const float Lift = 0.06f, HalfWidth = 0.21f;

        public GameObject root { get; private set; }
        /// <summary>How many pieces of the ribbon of each colour (Go, Lift, Brake) are on the road now, and for how many frames of the race some red was up out of all of them: for the checks.</summary>
        public readonly int[] shown = new int[3];
        public int redFrames, frames;
        /// <summary>What the line says under the bike now, and how far ahead of it (m) the first red begins (-1: none in the stretch ahead): whatever the aids show.</summary>
        public Advice here { get; private set; }
        public float redAhead { get; private set; }
        public int ShownTotal { get { return shown[0] + shown[1] + shown[2]; } }
        public bool Visible { get { return root != null && root.activeSelf; } }

        readonly RacingLine line;
        readonly Advice[] plan;
        readonly int aids;
        readonly int behind;
        readonly Advice[] win;          // the advice for the stretch from a few metres behind the bike to 300 m ahead of it, made again every frame
        readonly List<int>[] lists = { new List<int>(), new List<int>(), new List<int>() };
        Mesh mesh;
        Material[] all;

        PistaGuide(RacingLine line, int aids)
        {
            this.line = line; this.aids = aids;
            plan = DrivingLine.Classify(line);
            behind = Mathf.RoundToInt(DrivingLine.BehindMetres / line.c.spacing);
            win = new Advice[behind + Mathf.RoundToInt(DrivingLine.AheadMetres / line.c.spacing)];
        }

        /// <summary>The ribbon of a race for a racing line and an aids level, or null when the level shows nothing of it.</summary>
        public static PistaGuide Build(Transform parent, RacingLine line, int aids)
        {
            if (line == null || !(DrivingLine.Shows(aids, Advice.Go) || DrivingLine.Shows(aids, Advice.Lift) || DrivingLine.Shows(aids, Advice.Brake))) return null;
            var g = new PistaGuide(line, aids);
            var c = line.c;
            int n = c.n;
            var verts = new Vector3[2 * n]; var norms = new Vector3[2 * n];
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                verts[2 * i] = Edge(line, i, -HalfWidth); verts[2 * i + 1] = Edge(line, i, HalfWidth);
                norms[2 * i] = norms[2 * i + 1] = Vector3.up;
                for (int k = 0; k < 2; k++)
                {
                    var v = verts[2 * i + k];
                    minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z);
                }
            }
            g.all = new[]
            {
                // pure hues (a channel left at zero stays at zero when the sun and the glow push the others up: the red stays red and the yellow yellow)
                PistaTex.Glow("Guida verde", new Color(0.00f, 0.62f, 0.10f), new Color(0.00f, 0.25f, 0.04f), 0.1f),
                PistaTex.Glow("Guida gialla", new Color(0.85f, 0.62f, 0.00f), new Color(0.35f, 0.22f, 0.00f), 0.1f),
                PistaTex.Glow("Guida rossa", new Color(0.80f, 0.00f, 0.00f), new Color(0.35f, 0.00f, 0.00f), 0.1f),
            };
            g.mesh = new Mesh { name = "Linea di corsa" };
            g.mesh.SetVertices(verts); g.mesh.SetNormals(norms);
            g.mesh.subMeshCount = 3;
            for (int k = 0; k < 3; k++) g.mesh.SetTriangles(g.lists[k], k, false);
            g.mesh.bounds = new Bounds(new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f), new Vector3(maxX - minX + 4f, maxY - minY + 4f, maxZ - minZ + 4f));
            g.root = MB.Spawn("Linea di corsa", parent, g.mesh, g.all, false, false);
            return g;
        }

        /// <summary>The edge of the ribbon at sample i: the line's lateral offset plus 'side', in the frame of the road, at the height of the road there.</summary>
        static Vector3 Edge(RacingLine line, int i, float side)
        {
            var c = line.c;
            float d = line.off[i] + side;
            return new Vector3(c.px[i] + c.nx[i] * d, c.SurfaceY(i, 0f, d) + Lift, c.pz[i] + c.nz[i] * d);
        }

        /// <summary>The bike is at 's' metres along the lap with this speed (m/s): the stretch of road from a few metres behind it to 300 m ahead is given its colours.</summary>
        public void Follow(float s, float speed)
        {
            if (root == null || mesh == null) return;
            var c = line.c;
            int n = c.n, i0 = c.Wrap((int)(c.WrapS(s) / c.spacing));
            DrivingLine.Window(line, plan, i0, speed, behind, win);
            for (int k = 0; k < 3; k++) { lists[k].Clear(); shown[k] = 0; }
            here = win[behind];
            int firstRed = -1;
            for (int q = 0; q < win.Length; q++)
            {
                Advice a = win[q];
                if (q >= behind && firstRed < 0 && a == Advice.Brake) firstRed = q - behind;
                if (!DrivingLine.Shows(aids, a)) continue;
                int i = c.Wrap(i0 + q - behind), j = i + 1 == n ? 0 : i + 1;
                var t = lists[(int)a];
                t.Add(2 * i); t.Add(2 * j); t.Add(2 * j + 1);
                t.Add(2 * i); t.Add(2 * j + 1); t.Add(2 * i + 1);
                shown[(int)a]++;
            }
            for (int k = 0; k < 3; k++) mesh.SetTriangles(lists[k], k, false);
            redAhead = firstRed < 0 ? -1f : firstRed * c.spacing;
            frames++;
            if (shown[(int)Advice.Brake] > 0) redFrames++;
        }

        /// <summary>The stretch ahead of the bike as pieces of advice with their lengths in metres, 'V40 G6 R12' (V verde, the green: Go; G giallo, the yellow: Lift; R rosso, the red: Brake): for the log of a test.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            float sp = line.c.spacing;
            int q = behind;
            while (q < win.Length)
            {
                int r = q; while (r < win.Length && win[r] == win[q]) r++;
                sb.Append("VGR"[(int)win[q]]).Append(Mathf.RoundToInt((r - q) * sp)).Append(' ');
                q = r;
            }
            return sb.ToString().TrimEnd();
        }

        public void SetVisible(bool on) { if (root != null) root.SetActive(on); }

        /// <summary>The mesh and the materials are made here and not by the scene: they go with the guide.</summary>
        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            if (mesh != null) Object.Destroy(mesh);
            if (all != null) foreach (var m in all) if (m != null) Object.Destroy(m);
            root = null; mesh = null; all = null;
        }
    }
}
