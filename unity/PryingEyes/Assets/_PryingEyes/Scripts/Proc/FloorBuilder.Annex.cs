using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>La geometria degli annessi (PlanAnnex.cs): la torre con la cella in alto, la cripta sotto il pavimento. Il giardino sta in
    /// FloorBuilder.Garden.cs. Tutto e' costruito con MeshBuilder come il resto del piano; i pezzi che si muovono (la parete finta, la
    /// grata, il sigillo della botola) sono oggetti a parte, su HotelNav.DoorLayer: la navmesh li ignora (passa attraverso, come per le
    /// porte) e la fisica li vede, quindi basta spegnere il collider per aprire.
    /// Convenzioni: la direzione 'gradi' e' quella della pianta (0 = +z, 90 = +x, angoli crescenti in senso orario visti dall'alto); il
    /// fusto e' di 24 pannelli da 15 gradi e il vano della porta ne occupa tre, centrati su 'yaw'; la scala e' di cunei da 22,5 gradi con
    /// alzata di 20 cm (come le balconate: stessa quota per giocatore e navmesh).</summary>
    public static partial class FloorBuilder
    {
        const int Panels = 24;
        const float PanelDeg = 15f, StepRise = 0.2f, StepSector = 22.5f, StepThick = 0.32f, Newel = 0.45f, DoorTop = 2.3f, RailTop = 1.06f;

        static float ShaftIn => PlanAnnexGen.ShaftInner * 0.1f;
        static float ShaftOut => PlanAnnexGen.ShaftOuter * 0.1f;

        // ------------------------------------------------------------------ geometria di base
        public static Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }
        /// <summary>Verso degli angoli crescenti (la tangente al cerchio).</summary>
        static Vector3 Tang(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)); }
        static Vector3 Pol(Vector3 axis, float r, float deg, float y) => new Vector3(axis.x, 0f, axis.z) + Dir(deg) * r + Vector3.up * y;
        static float Norm360(float a) { a %= 360f; return a < 0f ? a + 360f : a; }

        /// <summary>Faccia cilindrica tra due angoli: guarda verso l'asse ('inward') o verso fuori, con le normali per vertice.</summary>
        static void CylFace(MeshBuilder mb, Material m, Vector3 axis, float r, float a0, float a1, float y0, float y1, bool inward)
        {
            if (y1 - y0 < 0.002f) return;
            Vector3 p00 = Pol(axis, r, a0, y0), p10 = Pol(axis, r, a1, y0), p11 = Pol(axis, r, a1, y1), p01 = Pol(axis, r, a0, y1);
            float k = inward ? -1f : 1f;
            Vector3 n0 = Dir(a0) * k, n1 = Dir(a1) * k;
            float u0 = a0 * Mathf.Deg2Rad * r, u1 = a1 * Mathf.Deg2Rad * r;
            mb.QuadSmooth(m, p00, p10, p11, p01, n0, n1, n1, n0, new Vector2(u0, y0), new Vector2(u1, y0), new Vector2(u1, y1), new Vector2(u0, y1));
        }

        static void RingCap(MeshBuilder mb, Material m, Vector3 axis, float rIn, float rOut, float a0, float a1, float y, bool up)
        {
            mb.QuadW(m, Pol(axis, rIn, a0, y), Pol(axis, rOut, a0, y), Pol(axis, rOut, a1, y), Pol(axis, rIn, a1, y), up ? Vector3.up : Vector3.down);
        }

        static void RadialFace(MeshBuilder mb, Material m, Vector3 axis, float rIn, float rOut, float a, float y0, float y1, Vector3 facing)
        {
            if (y1 - y0 < 0.002f) return;
            mb.QuadW(m, Pol(axis, rIn, a, y0), Pol(axis, rOut, a, y0), Pol(axis, rOut, a, y1), Pol(axis, rIn, a, y1), facing);
        }

        /// <summary>Un pilastro tondo.</summary>
        static void Pillar(MeshBuilder mb, Material m, Vector3 axis, float r, float y0, float y1, int segs, bool capTop)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * 360f / segs, a1 = (i + 1) * 360f / segs;
                CylFace(mb, m, axis, r, a0, a1, y0, y1, false);
                if (capTop) RingCap(mb, m, axis, 0f, r, a0, a1, y1, true);
            }
        }

        /// <summary>Il fusto della scala: 24 pannelli da 15 gradi, spessi da rIn a rOut, con il vano della porta (tre pannelli centrati su
        /// 'yaw') alto fino a 'doorTop' (quota assoluta) e l'architrave sopra. Non chiude ne' in alto ne' in basso.</summary>
        static void BuildTube(MeshBuilder mb, Material inner, Material outer, Vector3 axis, float rIn, float rOut, float y0, float y1, float yaw, float doorTop)
        {
            for (int j = 0; j < Panels; j++)
            {
                float a0 = yaw - 22.5f + j * PanelDeg, a1 = a0 + PanelDeg;
                bool gap = j < 3;
                float ys = gap ? doorTop : y0;
                CylFace(mb, inner, axis, rIn, a0, a1, ys, y1, true);
                CylFace(mb, outer, axis, rOut, a0, a1, ys, y1, false);
                if (gap) RingCap(mb, inner, axis, rIn, rOut, a0, a1, doorTop, false);
            }
            RadialFace(mb, inner, axis, rIn, rOut, yaw - 22.5f, y0, doorTop, Tang(yaw - 22.5f));
            RadialFace(mb, inner, axis, rIn, rOut, yaw + 22.5f, y0, doorTop, -Tang(yaw + 22.5f));
        }

        /// <summary>Il tappo del vano: gli stessi tre pannelli, chiusi (faccia interna, esterna, stipiti, testa e piede). E' un solido: ha il
        /// collider. Stesso materiale e stesse UV del fusto, cosi' chiuso e' una parete come le altre.</summary>
        static void BuildTubePlug(MeshBuilder mb, Material m, Vector3 axis, float rIn, float rOut, float y0, float y1, float yaw)
        {
            float a0 = yaw - 22.5f, a1 = yaw + 22.5f;
            for (int j = 0; j < 3; j++)
            {
                float b0 = a0 + j * PanelDeg, b1 = b0 + PanelDeg;
                CylFace(mb, m, axis, rIn, b0, b1, y0, y1, true);
                CylFace(mb, m, axis, rOut, b0, b1, y0, y1, false);
                RingCap(mb, m, axis, rIn, rOut, b0, b1, y1, true);
                RingCap(mb, m, axis, rIn, rOut, b0, b1, y0, false);
            }
            RadialFace(mb, m, axis, rIn, rOut, a0, y0, y1, -Tang(a0));
            RadialFace(mb, m, axis, rIn, rOut, a1, y0, y1, Tang(a1));
        }

        /// <summary>Un gradino a cuneo (settore tra due raggi e due angoli) con il piano a 'yTop' e lo spessore 'thick': sei facce, niente
        /// sovrapposizioni con i vicini.</summary>
        static void Wedge(MeshBuilder mb, Material top, Material side, Vector3 axis, float r0, float r1, float a0, float a1, float yTop, float thick)
        {
            float yb = yTop - thick;
            mb.QuadW(top, Pol(axis, r0, a0, yTop), Pol(axis, r1, a0, yTop), Pol(axis, r1, a1, yTop), Pol(axis, r0, a1, yTop), Vector3.up);
            mb.QuadW(side, Pol(axis, r0, a0, yb), Pol(axis, r1, a0, yb), Pol(axis, r1, a1, yb), Pol(axis, r0, a1, yb), Vector3.down);
            mb.QuadW(side, Pol(axis, r0, a0, yb), Pol(axis, r1, a0, yb), Pol(axis, r1, a0, yTop), Pol(axis, r0, a0, yTop), -Tang(a0));
            mb.QuadW(side, Pol(axis, r0, a1, yb), Pol(axis, r1, a1, yb), Pol(axis, r1, a1, yTop), Pol(axis, r0, a1, yTop), Tang(a1));
            CylFace(mb, side, axis, r1, a0, a1, yb, yTop, false);
            CylFace(mb, side, axis, r0, a0, a1, yb, yTop, true);
        }

        /// <summary>Lastra rettangolare [x0,x1] x [z0,z1] (m) con un foro tondo di raggio 'r' centrato in 'c': piano a 'y', spessa 'thick'. Il
        /// piano guarda in su, il fondo in giu', la parete del foro verso l'asse. Con 'outerSides' anche i quattro fianchi esterni. Gli angoli
        /// condivisi (giro + spigoli del rettangolo) fanno combaciare i quadrilateri senza buchi.</summary>
        static void HoledSlab(MeshBuilder mb, Material top, Material side, float x0, float z0, float x1, float z1, Vector3 c, float r, float y, float thick,
                              bool outerSides, int segs = 32)
        {
            var angles = new List<float>();
            for (int i = 0; i < segs; i++) angles.Add(i * 360f / segs);
            foreach (float cx in new[] { x0, x1 })
                foreach (float cz in new[] { z0, z1 })
                    angles.Add(Norm360(Mathf.Atan2(cx - c.x, cz - c.z) * Mathf.Rad2Deg));
            angles.Sort();
            var u = new List<float>();
            foreach (float a in angles) if (u.Count == 0 || a - u[u.Count - 1] > 0.05f) u.Add(a);
            if (u.Count > 1 && 360f - u[u.Count - 1] + u[0] < 0.05f) u.RemoveAt(u.Count - 1);
            int n = u.Count;
            float yb = y - thick;
            Vector3 Rect(float a, float yy)
            {
                Vector3 d = Dir(a);
                float t = float.MaxValue;
                if (d.x > 1e-5f) t = Mathf.Min(t, (x1 - c.x) / d.x); else if (d.x < -1e-5f) t = Mathf.Min(t, (x0 - c.x) / d.x);
                if (d.z > 1e-5f) t = Mathf.Min(t, (z1 - c.z) / d.z); else if (d.z < -1e-5f) t = Mathf.Min(t, (z0 - c.z) / d.z);
                return new Vector3(c.x + d.x * t, yy, c.z + d.z * t);
            }
            for (int i = 0; i < n; i++)
            {
                float a0 = u[i], a1 = u[(i + 1) % n];
                mb.QuadW(top, Pol(c, r, a0, y), Rect(a0, y), Rect(a1, y), Pol(c, r, a1, y), Vector3.up);
                mb.QuadW(side, Pol(c, r, a0, yb), Rect(a0, yb), Rect(a1, yb), Pol(c, r, a1, yb), Vector3.down);
                if (a1 < a0) a1 += 360f;
                CylFace(mb, side, c, r, a0, a1, yb, y, true);
                if (outerSides)
                {
                    Vector3 p0 = Rect(a0, yb), p1 = Rect(a1, yb), q0 = Rect(a0, y), q1 = Rect(a1, y);
                    Vector3 mid = (p0 + p1) * 0.5f - new Vector3(c.x, p0.y, c.z);
                    mb.QuadW(side, p0, p1, q1, q0, mid);
                }
            }
        }

        /// <summary>Ringhiera tonda (montanti a ogni angolo di pannello e due correnti) attorno a un foro, con un varco di tre pannelli che
        /// comincia al pannello 'gapStart' della griglia ('grid' e' l'angolo del pannello zero). Poggia su 'y0'.</summary>
        static void RailRing(MeshBuilder mb, Material iron, Vector3 axis, float r, float y0, float grid, int gapStart)
        {
            for (int b = 0; b < Panels; b++)
            {
                int rel = ((b - gapStart) % Panels + Panels) % Panels;
                bool interior = rel == 1 || rel == 2;                  // un montante dentro il varco non serve
                float a0 = grid + b * PanelDeg, a1 = a0 + PanelDeg;
                if (!interior) mb.Box(iron, Pol(axis, r, a0, y0 + RailTop * 0.5f), new Vector3(0.05f, RailTop, 0.05f));
                if (rel < 3) continue;                                 // il pannello b e' nel varco
                Vector3 p0 = Pol(axis, r, a0, y0), p1 = Pol(axis, r, a1, y0);
                mb.Beam(iron, p0 + Vector3.up * (RailTop - 0.03f), p1 + Vector3.up * (RailTop - 0.03f), 0.06f, 0.06f, Vector3.up);
                mb.Beam(iron, p0 + Vector3.up * 0.5f, p1 + Vector3.up * 0.5f, 0.035f, 0.035f, Vector3.up);
            }
        }

        /// <summary>Il pannello della griglia (0..23) il cui centro e' piu' vicino all'angolo 'deg', dato 'grid' (angolo del pannello zero),
        /// e da cui far cominciare un varco di tre pannelli centrato su quell'angolo.</summary>
        static int GapStartAround(float grid, float deg)
        {
            int center = Mathf.RoundToInt((Norm360(deg - grid) - PanelDeg * 0.5f) / PanelDeg);
            return ((center - 1) % Panels + Panels) % Panels;
        }

        /// <summary>Un anello luminoso piatto (segmenti) in un piano: centro, due assi, raggi interno ed esterno, rivolto verso 'facing'.</summary>
        static void GlowRing(MeshBuilder mb, Material m, Vector3 c, Vector3 ax, Vector3 ay, float r0, float r1, Vector3 facing, int segs = 20)
        {
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs, b = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 D(float t, float r) => c + ax * (Mathf.Cos(t) * r) + ay * (Mathf.Sin(t) * r);
                mb.QuadW(m, D(a, r0), D(a, r1), D(b, r1), D(b, r0), facing);
            }
        }

        /// <summary>Una lingua di fiamma: ottaedro allungato (base larga un terzo dell'altezza, punta in alto), 8 facce triangolari. Piu' somigliante
        /// a una fiamma di un cubo emissivo, che con l'esposizione del gioco usciva come un blocco bianco.</summary>
        static void Flame(MeshBuilder mb, Material m, Vector3 foot, float width, float height, float yaw)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            float ring = height * 0.32f, rad = width * 0.5f * 1.4142f;
            Vector3 top = foot + Vector3.up * height, bottom = foot;
            var r = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 0.5f;
                r[i] = foot + Vector3.up * ring + q * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rad;
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 p0 = r[i], p1 = r[(i + 1) % 4];
                Vector3 outward = new Vector3((p0.x + p1.x) * 0.5f - foot.x, 0f, (p0.z + p1.z) * 0.5f - foot.z).normalized;
                mb.QuadW(m, p0, p1, top, top, outward + Vector3.up * 0.4f);
                mb.QuadW(m, p0, p1, bottom, bottom, outward - Vector3.up * 0.6f);
            }
        }

        // ------------------------------------------------------------------ sito e oggetti
        static AnnexSite NewSite(FloorInstance fi, Annex a)
        {
            var go = new GameObject($"Annesso {a.id} {a.kind}");
            go.transform.SetParent(fi.root.transform, false);
            var site = go.AddComponent<AnnexSite>();
            site.data = a; site.fi = fi; site.host = fi.plan.rooms[a.host];
            var props = new GameObject("Arredi dell'annesso");
            props.transform.SetParent(go.transform, false);
            site.props = props.transform;
            fi.propRoots.Add(props.transform);
            fi.annexSites.Add(site);
            return site;
        }

        static GameObject FlushObj(Transform parent, string name, MeshBuilder mb, bool collide, int layer = 0)
        {
            if (mb.Empty) return null;
            var mesh = mb.ToMesh(parent.name + "_" + name, out var mats);
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (collide) g.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (layer != 0) g.layer = layer;
            return g;
        }

        /// <summary>Un arredo del kit (candelabro, sarcofago, altare...) in un punto: come RoomKits.Prop ma senza il contesto di una stanza.</summary>
        public static GameObject AnnexProp(Transform parent, string name, Vector3 pos, float yaw, float scale = 1f)
        {
            var def = PropDefs.Get(name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            var pf = ProcKit.Prefab(name);
            if (pf != null)
            {
                var inst = UnityEngine.Object.Instantiate(pf, go.transform);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Util.Kill(cube.GetComponent<Collider>());
                cube.name = "Segnaposto";
                cube.transform.SetParent(go.transform, false);
                cube.transform.localPosition = def.center;
                cube.transform.localScale = def.size;
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.35f, 0.33f, 0.31f) };
                cube.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            if (def.solid)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = def.center;
                bc.size = def.size;
            }
            return go;
        }

        /// <summary>Sigillo luminoso del colore del cancello: anelli e raggi.</summary>
        public static Color GateColor(GateKind g) => g == GateKind.Sigilli ? new Color(0.75f, 0.30f, 1.0f) : g == GateKind.Tributo ? new Color(1.0f, 0.74f, 0.20f)
            : g == GateKind.Segreto ? new Color(0.30f, 0.016f, 0.012f) : new Color(0.30f, 1.0f, 0.55f);

        // ------------------------------------------------------------------ costruzione per sala
        /// <summary>Costruisce gli annessi che stanno dentro la sala (torre, cripta); il giardino sta fuori e lo fa BuildGardens.</summary>
        static void BuildAnnexesIn(FloorInstance fi, Room room)
        {
            foreach (var a in fi.plan.annexes)
            {
                if (a.host != room.id) continue;
                if (a.kind == AnnexKind.Torre) BuildTower(fi, a);
                else if (a.kind == AnnexKind.Cripta) BuildCrypt(fi, a);
            }
        }

        /// <summary>Il pavimento della sala madre di una cripta: la solita lastra ma col foro della botola.</summary>
        static void CryptHostFloor(MeshBuilder shell, Style st, Room r, Annex a)
        {
            var axis = new Vector3(a.AxisX, 0f, a.AxisZ);
            HoledSlab(shell, st.floor, Mat("mur_cripta"), M(r.x0), M(r.z0), M(r.x1), M(r.z1), axis, ShaftIn, 0f, 0.5f, false);
        }

        // ------------------------------------------------------------------ cripta
        static void BuildCrypt(FloorInstance fi, Annex a)
        {
            var site = NewSite(fi, a);
            var tr = site.transform;
            float Y0 = a.FloorY;                                   // quota del pavimento della camera (negativa)
            var axis = new Vector3(a.AxisX, 0f, a.AxisZ);
            float yaw = a.yaw, rIn = ShaftIn, rOut = ShaftOut;
            Material stone = Mat("mur_cripta"), floorM = Mat("pav_cripta"), vault = Mat("sof_volta"), iron = Mat("ferro"), trim = Mat("cornice_pietra");
            Vector3 fwd = Dir(yaw), right = Vector3.Cross(Vector3.up, fwd);
            Vector3 L(float uu, float vv, float yy) => new Vector3(axis.x, 0f, axis.z) + fwd * uu + right * vv + Vector3.up * yy;
            float cx0 = M(a.x0), cz0 = M(a.z0), cx1 = M(a.x1), cz1 = M(a.z1);

            var mb = new MeshBuilder();
            // fusto e scala: la scala scende in senso orario e l'ultimo gradino finisce davanti al vano, rivolto verso la camera
            BuildTube(mb, stone, stone, axis, rIn, rOut, Y0, -0.5f, yaw, Y0 + DoorTop);
            int steps = Mathf.RoundToInt(-Y0 / StepRise) - 1;
            for (int k = 1; k <= steps; k++)
            {
                float a0 = yaw - 22.5f - StepSector * (steps - k + 1);
                Wedge(mb, floorM, stone, axis, Newel, rIn, a0, a0 + StepSector, -StepRise * k, StepThick);
            }
            Pillar(mb, stone, axis, Newel, Y0, -0.6f, 14, true);
            // la camera: pavimento, volta con il foro del fusto, quattro muri dentro il rettangolo
            mb.Box(stone, new Vector3((cx0 + cx1) * 0.5f, Y0 - 0.25f, (cz0 + cz1) * 0.5f), new Vector3(cx1 - cx0, 0.5f, cz1 - cz0), 0f, false, true);
            mb.QuadW(floorM, new Vector3(cx0, Y0, cz0), new Vector3(cx0, Y0, cz1), new Vector3(cx1, Y0, cz1), new Vector3(cx1, Y0, cz0), Vector3.up);
            float ceilY = Y0 + PlanAnnexGen.CryptHeight + 0.1f;     // sotto la volta
            HoledSlab(mb, stone, vault, cx0, cz0, cx1, cz1, axis, rOut, ceilY + 0.5f, 0.5f, false);
            const float wt = 0.3f;
            float wy = (Y0 + ceilY) * 0.5f, wh = ceilY - Y0;
            mb.Box(stone, new Vector3((cx0 + cx1) * 0.5f, wy, cz0 + wt * 0.5f), new Vector3(cx1 - cx0, wh, wt));
            mb.Box(stone, new Vector3((cx0 + cx1) * 0.5f, wy, cz1 - wt * 0.5f), new Vector3(cx1 - cx0, wh, wt));
            mb.Box(stone, new Vector3(cx0 + wt * 0.5f, wy, (cz0 + cz1) * 0.5f), new Vector3(wt, wh, cz1 - cz0 - 2f * wt));
            mb.Box(stone, new Vector3(cx1 - wt * 0.5f, wy, (cz0 + cz1) * 0.5f), new Vector3(wt, wh, cz1 - cz0 - 2f * wt));
            // cornice alla base dei muri e nervature della volta lungo la camera
            for (int i = 0; i < 5; i++)
            {
                float uu = 3.6f + i * 1.6f;
                Vector3 p0 = L(uu, -3.6f, ceilY - 0.12f), p1 = L(uu, 3.6f, ceilY - 0.12f);
                mb.Beam(trim, p0, p1, 0.28f, 0.24f, Vector3.up);
            }
            var shell = FlushObj(tr, "Cripta", mb, true);

            // ringhiera attorno alla botola, sul pavimento della sala; il varco sta sopra i primi gradini
            float entry = yaw - 22.5f - StepSector * steps + StepSector * 0.5f;
            float grid = yaw - 22.5f;
            var rails = new MeshBuilder();
            RailRing(rails, iron, axis, rIn + 0.2f, 0f, grid, GapStartAround(grid, entry));
            FlushObj(tr, "Ringhiera", rails, true);

            // il sigillo: un disco di pietra a filo del pavimento che chiude il foro (su DoorLayer: la navmesh passa, la fisica no)
            var closer = new GameObject("Sigillo");
            closer.transform.SetParent(tr, false);
            var sm = new MeshBuilder();
            float sr = rIn - 0.01f;
            for (int i = 0; i < 32; i++)
            {
                float s0 = i * 360f / 32f, s1 = (i + 1) * 360f / 32f;
                RingCap(sm, floorM, axis, 0f, sr, s0, s1, -0.002f, true);
                RingCap(sm, stone, axis, 0f, sr, s0, s1, -0.502f, false);
                CylFace(sm, stone, axis, sr, s0, s1, -0.502f, -0.002f, false);
            }
            var disc = FlushObj(closer.transform, "Disco", sm, true, HotelNav.DoorLayer);
            site.closer = closer.transform;
            site.closerCol = disc != null ? disc.GetComponent<Collider>() : null;
            site.sigilMat = ShrineKit.EmitOwn(GateColor(a.gate), 1.6f);
            var gm = new MeshBuilder();
            Vector3 gc = new Vector3(axis.x, 0.006f, axis.z);
            GlowRing(gm, site.sigilMat, gc, Vector3.right, Vector3.forward, 1.55f, 1.63f, Vector3.up);
            GlowRing(gm, site.sigilMat, gc, Vector3.right, Vector3.forward, 2.03f, 2.10f, Vector3.up);
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = Dir(i * 45f);
                gm.QuadW(site.sigilMat, gc + d * 0.35f + Tang(i * 45f) * 0.03f, gc + d * 0.35f - Tang(i * 45f) * 0.03f, gc + d * 1.5f - Tang(i * 45f) * 0.03f, gc + d * 1.5f + Tang(i * 45f) * 0.03f, Vector3.up);
            }
            FlushObj(closer.transform, "Sigillo luminoso", gm, false);
            site.gatePoint = new Vector3(axis.x, 0.25f, axis.z);
            site.gateRadius = 3.6f;
            site.outside = new Vector3(axis.x, 0f, axis.z) + Dir(entry) * (rIn + 0.9f);
            site.inside = L(7.0f, 0f, Y0 + 0.1f);
            // altare del tributo: accanto alla ringhiera, a lato dell'ingresso
            site.altarPos = Pol(axis, rIn + 1.9f, entry + 70f, 0f);
            site.altarYaw = YawOfDir(axis - site.altarPos);

            // arredi: sarcofagi lungo i muri, candelabri accesi in fondo, altare e scrigno
            var root = site.props;
            foreach (float vv in new[] { -2.95f, 2.95f })
                foreach (float uu in new[] { 4.6f, 7.4f })
                    AnnexProp(root, "chi_sarcofago", L(uu, vv, Y0), YawOfDir(fwd), 1f);
            AnnexProp(root, "chi_altare", L(9.2f, 0f, Y0), YawOfDir(-fwd), 0.9f);
            foreach (float vv in new[] { -3.2f, 3.2f })
            {
                AnnexProp(root, "chi_candelabro", L(9.25f, vv, Y0), 0f, 1f);
                ShrineKit.PointLight(tr, "Luce della cripta", L(9.25f, vv, Y0 + 1.7f), new Color(1f, 0.62f, 0.30f), 2.6f * LightPool.Dim, 7f, 0.25f, 3.1f);
            }
            foreach (float vv in new[] { -3.2f, 3.2f })
                AnnexProp(root, "chi_candelabro", L(3.4f, vv, Y0), 0f, 1f);
            ShrineKit.PointLight(tr, "Luce della scala", Pol(axis, rIn - 0.5f, entry + 90f, Y0 + 2.2f), new Color(1f, 0.62f, 0.30f), 2.2f * LightPool.Dim, 6f, 0.25f, 2.7f);
            ShrineKit.PointLight(tr, "Luce della cripta", L(6.0f, 0f, Y0 + 2.6f), new Color(0.95f, 0.70f, 0.45f), 2.8f * LightPool.Dim, 9f, 0.15f, 2.2f);
            site.rewardPos = L(7.2f, 0f, Y0);
            site.rewardYaw = YawOfDir(-fwd);
            site.keyPos = L(9.2f, 0f, Y0 + 1.0f);
        }

        static float YawOfDir(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        // ------------------------------------------------------------------ torre
        static void BuildTower(FloorInstance fi, Annex a)
        {
            var site = NewSite(fi, a);
            var tr = site.transform;
            float E = a.FloorY;                                    // quota del pavimento della cella
            var axis = new Vector3(a.AxisX, 0f, a.AxisZ);
            float yaw = a.yaw, rIn = ShaftIn, rOut = ShaftOut;
            Material stone = Mat("mur_pietra"), slab = Mat("pav_lastre"), trim = Mat("cornice_pietra"), wood = Mat("trave"), iron = Mat("ferro"), cellFloor = Mat("pav_parquet");
            float cx0 = M(a.x0), cz0 = M(a.z0), cx1 = M(a.x1), cz1 = M(a.z1);
            float halfX = (cx1 - cx0) * 0.5f, halfZ = (cz1 - cz0) * 0.5f;

            var mb = new MeshBuilder();
            BuildTube(mb, stone, stone, axis, rIn, rOut, 0f, E - 0.5f, yaw, DoorTop);
            int steps = Mathf.RoundToInt(E / StepRise);
            for (int k = 1; k <= steps; k++)
            {
                float a0 = yaw + 22.5f + StepSector * (k - 1);
                Wedge(mb, slab, stone, axis, Newel, rIn, a0, a0 + StepSector, StepRise * k, StepThick);
            }
            float exitA = yaw + 22.5f + StepSector * (steps - 1) + StepSector * 0.5f;     // il centro dell'ultimo gradino
            Pillar(mb, stone, axis, Newel, 0f, E + 1.0f, 14, true);
            // la soletta della cella con il foro della scala
            HoledSlab(mb, cellFloor, stone, cx0, cz0, cx1, cz1, axis, rIn, E, 0.5f, true);
            // pareti: quattro lati, ognuno con una finestra al centro (sopra la navata)
            const float wt = 0.3f, H = PlanAnnexGen.CellHeight, sill = 1.0f, wtop = 2.7f, ww = 1.5f;
            void Wall(float x0, float z0, float x1, float z1)
            {
                bool alongX = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0);
                float len = alongX ? x1 - x0 : z1 - z0;
                float cxm = (x0 + x1) * 0.5f, czm = (z0 + z1) * 0.5f;
                Vector3 P(float s, float yy) => alongX ? new Vector3(x0 + s, yy, czm) : new Vector3(cxm, yy, z0 + s);
                Vector3 Sz(float l, float h) => alongX ? new Vector3(l, h, wt) : new Vector3(wt, h, l);
                float half = len * 0.5f;
                mb.Box(stone, P(half - ww * 0.5f - (half - ww * 0.5f) * 0.5f, E + H * 0.5f), Sz(half - ww * 0.5f, H));
                mb.Box(stone, P(half + ww * 0.5f + (half - ww * 0.5f) * 0.5f, E + H * 0.5f), Sz(half - ww * 0.5f, H));
                mb.Box(stone, P(half, E + sill * 0.5f), Sz(ww, sill));
                mb.Box(stone, P(half, E + (wtop + H) * 0.5f), Sz(ww, H - wtop));
                mb.Box(trim, P(half, E + sill + 0.04f), Sz(ww + 0.3f, 0.08f), 0f, true, true);
            }
            float i0 = cx0 + wt * 0.5f, i1 = cx1 - wt * 0.5f, j0 = cz0 + wt * 0.5f, j1 = cz1 - wt * 0.5f;
            Wall(cx0, j0, cx1, j0); Wall(cx0, j1, cx1, j1);
            Wall(i0, cz0 + wt, i0, cz1 - wt); Wall(i1, cz0 + wt, i1, cz1 - wt);
            // soffitto a travi
            float ry = E + H;
            mb.Box(stone, new Vector3((cx0 + cx1) * 0.5f, ry + 0.2f, (cz0 + cz1) * 0.5f), new Vector3(cx1 - cx0, 0.4f, cz1 - cz0));
            int beams = Mathf.Max(3, Mathf.RoundToInt((cz1 - cz0) / 1.6f));
            for (int i = 0; i <= beams; i++)
            {
                float z = Mathf.Lerp(cz0 + 0.4f, cz1 - 0.4f, i / (float)beams);
                mb.Beam(wood, new Vector3(cx0 + wt, ry - 0.12f, z), new Vector3(cx1 - wt, ry - 0.12f, z), 0.22f, 0.24f, Vector3.up);
            }
            var shellGo = FlushObj(tr, "Torre", mb, true);

            // ringhiera attorno al foro della cella (il varco sta sull'ultimo gradino) e braciere in cima al fusto centrale
            var rails = new MeshBuilder();
            float grid = yaw - 22.5f;
            RailRing(rails, iron, axis, rIn + 0.15f, E, grid, GapStartAround(grid, exitA));
            FlushObj(tr, "Ringhiera", rails, true);
            var brazier = new MeshBuilder();
            // un cubo emissivo a 3,2 EV usciva bianco: braci sotto e tre lingue sottili di fiamma, piu' scure
            var fireMat = ShrineKit.Emit(new Color(1f, 0.42f, 0.10f), 1.5f);
            var emberMat = ShrineKit.Emit(new Color(0.85f, 0.18f, 0.04f), 1.1f);
            brazier.Box(iron, new Vector3(axis.x, E + 1.12f, axis.z), new Vector3(0.7f, 0.14f, 0.7f));
            brazier.Box(emberMat, new Vector3(axis.x, E + 1.21f, axis.z), new Vector3(0.52f, 0.05f, 0.52f));
            Flame(brazier, fireMat, new Vector3(axis.x, E + 1.20f, axis.z), 0.30f, 0.62f, 12f);
            Flame(brazier, fireMat, new Vector3(axis.x + 0.11f, E + 1.20f, axis.z - 0.07f), 0.19f, 0.40f, 40f);
            Flame(brazier, fireMat, new Vector3(axis.x - 0.10f, E + 1.20f, axis.z + 0.08f), 0.17f, 0.34f, 71f);
            FlushObj(tr, "Braciere", brazier, false);
            ShrineKit.PointLight(tr, "Luce del braciere", new Vector3(axis.x, E + 1.9f, axis.z), new Color(1f, 0.58f, 0.24f), 3.4f * LightPool.Dim, 11f, 0.22f, 3.6f);
            // lumi lungo la scala, a un giro l'uno dall'altro
            for (int k = 8; k < steps - 6; k += 12)
            {
                float ak = yaw + 22.5f + StepSector * (k - 1) + StepSector * 0.5f;
                ShrineKit.PointLight(tr, "Luce della torre", Pol(axis, rIn - 0.45f, ak, StepRise * k + 1.9f), new Color(1f, 0.64f, 0.32f), 2.4f * LightPool.Dim, 6.5f, 0.25f, 2.9f);
            }
            // luci della cella
            foreach (float sx in new[] { -1f, 1f })
                ShrineKit.PointLight(tr, "Luce della cella", new Vector3((cx0 + cx1) * 0.5f + sx * (halfX - 1.3f), E + 2.6f, (cz0 + cz1) * 0.5f - sx * (halfZ - 1.3f)), new Color(1f, 0.70f, 0.42f), 2.6f * LightPool.Dim, 9f, 0.18f, 2.4f);

            // il tappo del vano al piede della torre: parete finta (stessa pietra) oppure grata di ferro, su DoorLayer
            var closer = new GameObject(a.secret ? "Parete finta" : "Grata");
            closer.transform.SetParent(tr, false);
            var cm = new MeshBuilder();
            if (a.secret) BuildTubePlug(cm, stone, axis, rIn, rOut, 0f, DoorTop, yaw);
            else
            {
                float mid = (rIn + rOut) * 0.5f;
                for (int i = 0; i <= 9; i++)
                {
                    float ang = yaw - 20f + i * 40f / 9f;
                    cm.Box(iron, Pol(axis, mid, ang, DoorTop * 0.5f), new Vector3(0.05f, DoorTop, 0.05f), ang);
                }
                for (float yy = 0.3f; yy < DoorTop; yy += 0.9f)
                    for (int i = 0; i < 9; i++)
                    {
                        float a0 = yaw - 20f + i * 40f / 9f, a1 = yaw - 20f + (i + 1) * 40f / 9f;
                        cm.Beam(iron, Pol(axis, mid, a0, yy), Pol(axis, mid, a1, yy), 0.06f, 0.06f, Vector3.up);
                    }
            }
            var cg = FlushObj(closer.transform, "Tappo", cm, true, HotelNav.DoorLayer);
            site.closer = closer.transform;
            site.closerCol = cg != null ? cg.GetComponent<Collider>() : null;
            // il sigillo luminoso sulla faccia esterna, all'altezza degli occhi
            site.sigilMat = ShrineKit.EmitOwn(GateColor(a.gate), a.secret ? 0.9f : 1.5f);
            var sg = new MeshBuilder();
            Vector3 face = Pol(axis, rOut + 0.014f, yaw, 1.55f);
            GlowRing(sg, site.sigilMat, face, Tang(yaw), Vector3.up, 0.27f, 0.31f, Dir(yaw));
            GlowRing(sg, site.sigilMat, face, Tang(yaw), Vector3.up, 0.09f, 0.13f, Dir(yaw), 12);
            FlushObj(closer.transform, "Sigillo luminoso", sg, false);
            site.gatePoint = Pol(axis, rOut + 0.1f, yaw, 1.5f);
            site.gateRadius = 3.0f;
            site.outside = Pol(axis, rOut + 1.2f, yaw, 0f);
            site.inside = Pol(axis, rIn - 0.7f, yaw + 90f, 0.1f);
            // altare del tributo: davanti alla porta, a lato
            site.altarPos = Pol(axis, rOut + 1.9f, yaw + 24f, 0f);
            site.altarYaw = YawOfDir(axis - site.altarPos);
            // il tesoro: nell'angolo della cella piu' lontano dall'uscita della scala
            var corners = new List<(float d, Vector3 p)>();
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var p = new Vector3((cx0 + cx1) * 0.5f + sx * (halfX - 1.5f), E, (cz0 + cz1) * 0.5f + sz * (halfZ - 1.5f));
                    corners.Add((Vector3.Angle(Dir(exitA), new Vector3(p.x - axis.x, 0f, p.z - axis.z)), p));
                }
            corners.Sort((p, q) => q.d.CompareTo(p.d));
            site.rewardPos = corners[0].p;
            site.rewardYaw = YawOfDir(axis - corners[0].p);
            site.keyPos = corners[1].p;                        // la chiave (se la torre ne custodisce una) su un piedistallo nell'angolo accanto
            site.cellFloorY = E;
        }
    }
}
