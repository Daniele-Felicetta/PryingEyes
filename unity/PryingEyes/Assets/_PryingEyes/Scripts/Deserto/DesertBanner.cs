using System.Collections.Generic;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Uno stendardo con l'occhio d'oro (il simbolo del gioco): un telo rosso scuro appeso a una traversa di ferro in cima a un palo, che il vento del deserto muove. Il telo e' una griglia di
    /// vertici con due facce (una dritta e una rovesciata, perche' la luce e le normali siano giuste da tutti e due i lati), ferma alla traversa e libera in basso, dove finisce a coda di rondine.
    /// A ogni fotogramma i vertici si spostano sottovento (DesertSky.Weather: la forza e la direzione del vento, le raffiche) e un'onda corre dall'alto verso l'orlo, piu' larga con il vento.
    /// Il disegno del telo (trama, macchie, sporco in basso, bordo d'oro, l'occhio con i raggi e i galloni) e' dipinto una volta sola in una texture, per punto, con distanze.</summary>
    public sealed class DesertBanner : MonoBehaviour
    {
        public const float Width = 1.3f, Height = 3.4f, Notch = 0.45f;
        const int Cols = 8, Rows = 24;
        const float PoleHeight = 5.7f, BarHeight = 5.25f;

        Mesh mesh;
        Vector3[] rest, pos;
        int half;
        float phase;
        int slot;                // da lontano si aggiorna un fotogramma su tre: lo scarto fra uno stendardo e l'altro (0..2)
        bool wall;               // appeso a un muro (Hang): il vento non lo spinge mai dentro il muro
        static Texture2D emblem;
        static Material cloth;

        /// <summary>Vero per uno stendardo appeso a un muro.</summary>
        public bool OnWall => wall;
        /// <summary>Per uno stendardo di muro: la distanza minima (m) fra un vertice del telo e la faccia del muro, nel fotogramma in cui il vento lo spinge di piu' verso il muro.</summary>
        public float MinGap { get; private set; } = float.MaxValue;

        /// <summary>Quanto si e' mosso, dalla posizione di riposo, il vertice che ha mosso di piu' nella riga alta (ferma alla traversa) e in quella bassa (libera): la prova controlla che l'alta
        /// stia ferma e la bassa si muova.</summary>
        public float TopOffset { get; private set; }
        public float HemOffset { get; private set; }

        /// <summary>Costruisce uno stendardo con il piede in 'at' (coordinate del padre); il telo guarda verso 'facing' (un versore sul piano) e l'onda parte da 'phase'. 'scale' lo ingrandisce
        /// tutto (una torre ne vuole uno grande, per vederlo da lontano); 'solid' gli da' i collider (non servono sul tetto di una torre).</summary>
        public static DesertBanner Place(Transform parent, Vector3 at, Vector3 facing, float phase, float scale = 1f, bool solid = true)
        {
            ShrineKit.Mats();
            var root = new GameObject("Stendardo").transform;
            root.SetParent(parent, false);
            root.localPosition = at;
            root.localRotation = Quaternion.LookRotation(-facing, Vector3.up);          // la faccia dritta del telo guarda lungo -z locale
            root.localScale = Vector3.one * scale;
            void Cast(GameObject g) { g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            ShrineKit.Prim(root, PrimitiveType.Cube, "Base", ShrineKit.Stone, new Vector3(0f, 0.22f, 0f), new Vector3(0.46f, 0.44f, 0.46f), default, solid);
            float half = (PoleHeight - 0.44f) * 0.5f;
            Cast(ShrineKit.Prim(root, PrimitiveType.Cylinder, "Palo", ShrineKit.Iron, new Vector3(0f, 0.44f + half, 0f), new Vector3(0.09f, half, 0.09f), default, solid));
            Cast(ShrineKit.Prim(root, PrimitiveType.Cylinder, "Traversa", ShrineKit.Iron, new Vector3(0f, BarHeight, 0f), new Vector3(0.05f, 0.80f, 0.05f), new Vector3(0f, 0f, 90f)));
            foreach (float sx in new[] { -0.82f, 0.82f })
                Cast(ShrineKit.Prim(root, PrimitiveType.Sphere, "Pomo", ShrineKit.Brass, new Vector3(sx, BarHeight, 0f), new Vector3(0.09f, 0.09f, 0.09f)));
            Cast(ShrineKit.Prim(root, PrimitiveType.Sphere, "Puntale", ShrineKit.Brass, new Vector3(0f, PoleHeight + 0.06f, 0f), new Vector3(0.17f, 0.17f, 0.17f)));
            Cast(ShrineKit.Prim(root, PrimitiveType.Cylinder, "Punta", ShrineKit.Brass, new Vector3(0f, PoleHeight + 0.26f, 0f), new Vector3(0.035f, 0.14f, 0.035f)));
            var go = new GameObject("Telo");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, BarHeight - 0.04f, 0.05f);
            var b = go.AddComponent<DesertBanner>();
            b.phase = phase;
            b.slot = Mathf.FloorToInt(phase * 10f) % 3;
            b.Build();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Cloth();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
            b.Animate(0f, default);
            return b;
        }

        /// <summary>Il tetto di questa stanza e' una lastra piana (FloorBuilder.HasRoof e AddRoof): non e' ne' a falde ne' a volta ne' una guglia, e c'e'.</summary>
        static bool FlatRoof(Room r) =>
            !(r.IsLift || r.noCeil || r.ruined || r.Open || r.ceil == CeilKind.Open || r.kind == RoomKind.Campanile
              || (r.ceil == CeilKind.Gable && r.ridgeH - r.wallH > 0.4f)
              || ((r.ceil == CeilKind.BarrelRound || r.ceil == CeilKind.BarrelPointed) && r.ridgeH - r.wallH > 0.3f));

        /// <summary>L'albero con lo stendardo in mezzo al tetto piano piu' grande dell'ultimo livello di una torre intera (il soffitto a travi dentro non conta: da fuori e' una lastra), doppio di quello
        /// della cattedrale: da lontano, dove la visuale rasente passa sopra il parapetto e arriva al telo, e' quello che segna la torre sull'orizzonte. Da vicino il parapetto lo nasconde (un tetto di
        /// 43 m non si vede da terra): per questo ci sono anche gli stendardi di muro (OnTowerWalls). Guarda verso la cattedrale (all'origine), da dove arriva chi gioca, e il suo dondolio parte da un
        /// punto che dipende dal luogo. Appartiene alla pelle di chi sta lontano (SiteFacade), non al guscio: c'e' prima di questo e resta quando il guscio c'e'. Nessun collider (sul tetto non
        /// ci si arriva). Falso (null) se la torre non e' intera o non ha un tetto piano.</summary>
        public static DesertBanner OnTower(Transform parent, Site site)
        {
            if (!site.IsTower || site.state != BuildingState.Intact || site.levels == null) return null;
            int top = site.LevelCount - 1;
            Room best = null; int area = 0;
            foreach (var r in site.levels[top].rooms)
            {
                if (!FlatRoof(r)) continue;
                if (r.W * r.D > area) { area = r.W * r.D; best = r; }
            }
            if (best == null) return null;
            var face = new Vector3(-best.CenterX, 0f, -best.CenterZ);
            face = face.sqrMagnitude < 1f ? Vector3.back : face.normalized;
            return Place(parent, new Vector3(best.CenterX, site.LevelY(top) + best.wallH + 0.14f, best.CenterZ), face, ((site.cx * 7 + site.cz * 13) & 15) * 0.4f, 2.0f, false);
        }

        // ------------------------------------------------------------------ gli stendardi di muro
        const float WallScale = 3.2f, ArmLen = 0.30f, ClothZ = 0.05f, MaxIn = 0.14f, MinWall = 7.5f;
        static Mesh bracket;

        /// <summary>Il sostegno di uno stendardo di muro (in lunghezze del telo, la radice e' la traversa): la traversa con due pomi, e due braccetti dritti verso il muro con la loro piastra.</summary>
        static Mesh BracketMesh()
        {
            if (bracket != null) return bracket;
            var parts = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (DesertLathe.Tube("StendardoTraversa", new[] { new Vector3(-0.80f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0.80f, 0f, 0f) }, new[] { 0.026f, 0.026f, 0.026f }, 6), Matrix4x4.identity),
            };
            var knob = DesertLathe.Turn("StendardoPomo", new[] { new Vector2(0f, -0.05f), new Vector2(0.03f, -0.042f), new Vector2(0.048f, -0.015f), new Vector2(0.048f, 0.015f), new Vector2(0.03f, 0.042f), new Vector2(0f, 0.05f) }, 8);
            foreach (float sx in new[] { -0.82f, 0.82f })
                parts.Add((knob, Matrix4x4.TRS(new Vector3(sx, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one)));
            foreach (float sx in new[] { -0.5f, 0.5f })
                parts.Add((DesertLathe.Tube("StendardoBraccio", new[] { new Vector3(sx, 0f, 0f), new Vector3(sx, 0f, ArmLen - 0.06f), new Vector3(sx, 0f, ArmLen - 0.055f), new Vector3(sx, 0f, ArmLen + 0.02f) },
                                            new[] { 0.028f, 0.028f, 0.07f, 0.07f }, 6), Matrix4x4.identity));
            return bracket = DesertLathe.Join("StendardoSostegno", parts);
        }

        /// <summary>Uno stendardo appeso al muro (una torre ha il suo tetto troppo alto per vederci qualcosa da terra): una traversa di ferro sporge dalla facciata con due braccetti e il telo scende davanti al
        /// muro, 70 cm fuori. 'wallPoint' e' un punto sulla faccia esterna del muro (la x e la z; la quota e' 'barY'), 'outward' il versore orizzontale che esce dal muro; il telo guarda fuori. Il vento lo muove
        /// come gli altri ma non lo spinge mai dentro il muro: lo spostamento verso il muro si ferma a 'MaxIn' lunghezze (e il telo resta a 30 cm dalla facciata, dove la cornice sporge 22 cm).</summary>
        public static DesertBanner Hang(Transform parent, Vector3 wallPoint, Vector3 outward, float barY, float phase, float scale = WallScale)
        {
            ShrineKit.Mats();
            var root = new GameObject("Stendardo di muro").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(wallPoint.x, barY, wallPoint.z) + outward * (ArmLen * scale);
            root.localRotation = Quaternion.LookRotation(-outward, Vector3.up);          // la faccia dritta del telo guarda lungo -z locale: fuori dal muro
            root.localScale = Vector3.one * scale;
            var iron = new GameObject("Sostegno");
            iron.transform.SetParent(root, false);
            iron.AddComponent<MeshFilter>().sharedMesh = BracketMesh();
            var ir = iron.AddComponent<MeshRenderer>();
            ir.sharedMaterial = ShrineKit.Iron;
            ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var go = new GameObject("Telo");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, -0.04f, ClothZ);
            var b = go.AddComponent<DesertBanner>();
            b.phase = phase;
            b.slot = Mathf.FloorToInt(phase * 10f) % 3;
            b.wall = true;
            b.Build();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Cloth();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
            b.Animate(0f, default);
            return b;
        }

        /// <summary>Gli stendardi di muro di una torre intera: uno per lato, sul tratto di muro esterno piu' lungo dell'ultimo livello (almeno 7,5 m: il telo e' largo 4,2 m e il vento lo sposta di un
        /// metro per parte), appeso sotto la cornice del tetto. Falso (lista vuota)
        /// se la torre non e' intera. Come l'albero (OnTower) appartengono alla pelle di chi sta lontano e restano quando c'e' il guscio.</summary>
        public static List<DesertBanner> OnTowerWalls(Transform parent, Site site)
        {
            var res = new List<DesertBanner>();
            if (!site.IsTower || site.state != BuildingState.Intact || site.levels == null) return res;
            int top = site.LevelCount - 1;
            var plan = site.levels[top];
            for (int si = 0; si < 4; si++)
            {
                var side = (Side)si;
                Room bestR = null; Seg bestS = null; int bestLen = 0;
                foreach (var r in plan.rooms)
                {
                    if (!FlatRoof(r)) continue;                                          // un muro che regge un tetto a falde o a volta non ha la cornice piana
                    foreach (var sg in plan.Segments(r, side))
                        if (sg.other < 0 && sg.a1 - sg.a0 > bestLen) { bestLen = sg.a1 - sg.a0; bestR = r; bestS = sg; }
                }
                if (bestR == null || bestLen < MinWall * 10f) continue;
                float along = (bestS.a0 + bestS.a1) * 0.05f, line = Plan.Line(bestR, side) * 0.1f;
                var outward = side == Side.S ? Vector3.back : side == Side.N ? Vector3.forward : side == Side.W ? Vector3.left : Vector3.right;
                bool ax = Plan.AlongX(side);
                var wallPoint = (ax ? new Vector3(along, 0f, line) : new Vector3(line, 0f, along)) + outward * FloorBuilder.TH;          // la faccia esterna del muro
                float barY = site.LevelY(top) + bestR.wallH - 0.95f;                                       // sotto la cornice (che sta fra 30 e 4 cm sotto il tetto)
                res.Add(Hang(parent, wallPoint, outward, barY, ((site.cx * 7 + site.cz * 13 + si * 5) & 15) * 0.4f));
            }
            return res;
        }

        void Build()
        {
            int nv = (Cols + 1) * (Rows + 1);
            half = nv;
            rest = new Vector3[nv * 2]; pos = new Vector3[nv * 2];
            var uv = new Vector2[nv * 2]; var tris = new List<int>();
            for (int r = 0; r <= Rows; r++)
            {
                float t = r / (float)Rows;
                float tail = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.45f) / 0.55f));          // la coda di rondine riguarda solo la parte bassa
                for (int c = 0; c <= Cols; c++)
                {
                    float u = c / (float)Cols;
                    var p = new Vector3((u - 0.5f) * Width, -Height * t + Notch * (1f - Mathf.Abs(2f * u - 1f)) * tail, 0f);
                    int i = r * (Cols + 1) + c;
                    rest[i] = p; rest[nv + i] = p;
                    uv[i] = uv[nv + i] = new Vector2(u, 1f + p.y / Height);          // la posizione nel disegno e' quella del punto: la coda di rondine taglia il disegno, non lo schiaccia
                }
            }
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int tl = r * (Cols + 1) + c, tr = tl + 1, bl = tl + Cols + 1, br = bl + 1;
                    tris.Add(tl); tris.Add(tr); tris.Add(br); tris.Add(tl); tris.Add(br); tris.Add(bl);                                   // la faccia dritta (normale verso -z), in senso orario per chi guarda
                    tris.Add(half + tl); tris.Add(half + br); tris.Add(half + tr); tris.Add(half + tl); tris.Add(half + bl); tris.Add(half + br);        // la rovescia
                }
            mesh = new Mesh { name = "Stendardo" };
            mesh.MarkDynamic();
            mesh.vertices = rest; mesh.uv = uv; mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(new Vector3(0f, -Height * 0.5f, 0f), new Vector3(Width + 3.4f, Height + 0.8f, 3.4f));        // il telo non sta mai fuori di qui: niente ricalcolo a ogni fotogramma
        }

        void Update()
        {
            // da lontano (oltre 150 m) si aggiorna un fotogramma su tre, sfalsati da uno stendardo all'altro: il movimento si vede lo stesso e costa un terzo
            // (150 m da ogni giocatore: nello schermo condiviso conta l'occhio piu' vicino)
            if (Seats.EyeDistanceSqr(transform.position) > 150f * 150f && Camera.main != null && (Time.frameCount + slot) % 3 != 0) return;
            var sky = DesertSky.I;
            Animate(Time.time, sky != null ? sky.Weather : default);
        }

        /// <summary>Mette i vertici dove li vuole il vento a questo istante.</summary>
        void Animate(float time, in WeatherState w)
        {
            float wind = Mathf.Max(w.wind, 0.2f), gust = Mathf.Max(w.gust, 0.15f);
            var wd = transform.InverseTransformDirection(new Vector3(w.dirX, 0f, w.dirZ));            // la direzione del vento nel piano del telo (x lungo il telo, z fuori)
            if (w.dirX == 0f && w.dirZ == 0f) wd = new Vector3(0.3f, 0f, -0.9f);
            float push = 0.10f + 1.05f * wind * (0.65f + 0.35f * gust);                               // di quanto va sottovento l'orlo (m)
            float amp = 0.030f + 0.15f * gust * wind + 0.05f * wind;                                  // l'ampiezza dell'onda
            float freq = 0.8f + 2.0f * wind;
            float top = 0f, hem = 0f, gap = float.MaxValue, wallScaleNow = wall ? transform.lossyScale.y : 1f;
            for (int r = 0; r <= Rows; r++)
            {
                float t = r / (float)Rows, t2 = t * t;
                for (int c = 0; c <= Cols; c++)
                {
                    int i = r * (Cols + 1) + c;
                    var p = rest[i];
                    float u = c / (float)Cols;
                    float wave = Mathf.Sin(6.2832f * (t * 1.35f - time * freq * 0.45f) + u * 3.1f + phase) * amp * t
                               + Mathf.Sin(6.2832f * (t * 2.6f - time * freq * 0.8f) + u * 5.3f + phase * 1.7f) * amp * 0.35f * t;
                    float dx = wd.x * push * t2 + wave * 0.35f;
                    float dz = wd.z * push * t2 + wave;
                    if (wall)
                    {
                        // +z e' verso il muro: il vento che lo spinge la' lo appoggia piano (un quarto) e non lo fa mai entrare; verso fuori si muove libero
                        if (dz > 0f) dz = Mathf.Min(dz * 0.25f, MaxIn);
                    }
                    float len = Mathf.Max(0.3f, -rest[i].y);
                    var q = new Vector3(p.x + dx, p.y + (dx * dx + dz * dz) / (2f * len), dz);           // l'orlo che si alza di lato si accorcia in basso
                    pos[i] = q; pos[half + i] = q;
                    if (wall) gap = Mathf.Min(gap, (ArmLen - ClothZ - dz) * wallScaleNow);
                    float off = (q - p).magnitude;
                    if (r == 0) top = Mathf.Max(top, off);
                    if (r == Rows) hem = Mathf.Max(hem, off);
                }
            }
            TopOffset = top; HemOffset = hem;
            if (wall) MinGap = Mathf.Min(MinGap, gap);
            mesh.vertices = pos;
            mesh.RecalculateNormals();
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }

        // ------------------------------------------------------------------ il materiale e il disegno
        static Material Cloth()
        {
            if (cloth != null) return cloth;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Stendardo" };
            m.SetTexture("_BaseMap", Emblem());
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.10f);
            m.SetFloat("_Metallic", 0f);
            return cloth = m;
        }

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }
        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        }
        /// <summary>La copertura (0..1) di una forma a distanza 'd' dal suo bordo (negativa dentro): un bordo sfumato di un pixel e mezzo (un pixel e' circa 5 mm).</summary>
        static float Cover(float d) => Mathf.Clamp01(0.5f - d / 0.0075f);
        static Color Over(Color under, Color over, float k) => Color.Lerp(under, over, Mathf.Clamp01(k));
        /// <summary>La distanza di un punto da un segmento.</summary>
        static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / Mathf.Max(1e-6f, vx * vx + vy * vy));
            float dx = wx - vx * t, dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Texture2D Emblem()
        {
            if (emblem != null) return emblem;
            const int W = 256, H = 768;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float Y = ((y + 0.5f) / H - 0.5f) * Height;           // metri dal centro del telo, in alto positivi
                for (int x = 0; x < W; x++)
                {
                    float X = ((x + 0.5f) / W - 0.5f) * Width;
                    px[y * W + x] = Paint(X, Y);
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "StendardoOcchio", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return emblem = tex;
        }

        static Color Paint(float X, float Y)
        {
            var gold = new Color(0.86f, 0.66f, 0.24f); var darkGold = new Color(0.40f, 0.24f, 0.07f);
            float top = Height * 0.5f;                                   // Y del lembo alto
            // la stoffa: rosso scuro con trama, macchie larghe e sporco che sale dal basso
            float weave = 0.94f + 0.06f * Mathf.Sin(X * 190f) * Mathf.Sin(Y * 170f);
            float mottle = 0.86f + 0.28f * Noise(X * 2.3f + 5f, Y * 1.7f + 9f) + 0.10f * (Noise(X * 9f, Y * 7f) - 0.5f);
            float grime = Smooth(top - 1.7f, -top, Y) * (0.55f + 0.45f * Noise(X * 3.1f, Y * 2.2f + 3f));
            var c = new Color(0.43f, 0.045f, 0.06f) * (weave * mottle) * (1f - 0.42f * grime);
            c.a = 1f;
            // il lembo alto, con la tasca della traversa e il suo punto di cucitura
            if (Y > top - 0.17f) c = Over(c, new Color(0.22f, 0.02f, 0.03f), 0.55f);
            c = Over(c, darkGold, Cover(Mathf.Abs(Y - (top - 0.17f)) - 0.004f) * 0.8f);
            // il bordo d'oro: due fili ai lati, uno sotto la tasca e uno che segue la coda di rondine
            float edge = Width * 0.5f;
            float dBorder = Mathf.Abs(Mathf.Abs(X) - (edge - 0.075f));
            c = Over(c, gold, Cover(dBorder - 0.016f));
            c = Over(c, gold, Cover(Mathf.Abs(Mathf.Abs(X) - (edge - 0.125f)) - 0.006f) * 0.9f);
            c = Over(c, gold, Cover(Mathf.Abs(Y - (top - 0.27f)) - 0.012f) * (Mathf.Abs(X) < edge - 0.07f ? 1f : 0f));
            float yb = -top + Notch * (1f - Mathf.Abs(X) / edge);                                    // l'orlo basso
            float slope = Notch / edge, cosS = 1f / Mathf.Sqrt(1f + slope * slope);
            c = Over(c, gold, Cover(Mathf.Abs((Y - (yb + 0.12f)) * cosS) - 0.013f) * (Mathf.Abs(X) < edge - 0.07f ? 1f : 0f));
            c = Over(c, gold, Cover(Mathf.Abs((Y - (yb + 0.19f)) * cosS) - 0.005f) * (Mathf.Abs(X) < edge - 0.13f ? 0.9f : 0f));
            // l'occhio: una mandorla, l'iride con la pupilla a fessura, e i raggi sopra e sotto
            const float cy = 0.40f, a = 0.44f, b = 0.235f;
            float ex = X, ey = Y - cy;
            float lens = b * Mathf.Pow(Mathf.Max(0f, 1f - (ex / a) * (ex / a)), 0.85f);           // la mezza altezza della mandorla in X
            float dLens = Mathf.Abs(ey) - lens;                                                      // negativa dentro
            if (Mathf.Abs(ex) > a) dLens = Mathf.Max(dLens, Mathf.Abs(ex) - a);
            // i raggi
            for (int k = 0; k <= 12; k++)
            {
                float ang = (-78f + 156f * k / 12f) * Mathf.Deg2Rad;                                   // dalla verticale, a ventaglio verso l'alto
                float sx = Mathf.Sin(ang), sy = Mathf.Cos(ang);
                float edgeR = 1f / Mathf.Sqrt((sx / a) * (sx / a) + (sy / b) * (sy / b));
                float r0 = edgeR + 0.07f, r1 = r0 + ((k & 1) == 0 ? 0.20f : 0.11f);
                c = Over(c, gold, Cover(Seg(ex, ey, sx * r0, sy * r0, sx * r1, sy * r1) - 0.011f));
            }
            for (int k = 0; k <= 4; k++)                                                              // e pochi sotto, piu' corti
            {
                float ang = (180f - 50f + 100f * k / 4f) * Mathf.Deg2Rad;
                float sx = Mathf.Sin(ang), sy = Mathf.Cos(ang);
                float edgeR = 1f / Mathf.Sqrt((sx / a) * (sx / a) + (sy / b) * (sy / b));
                float r0 = edgeR + 0.07f, r1 = r0 + 0.10f;
                c = Over(c, gold, Cover(Seg(ex, ey, sx * r0, sy * r0, sx * r1, sy * r1) - 0.010f));
            }
            // il contorno e il bianco dell'occhio (un crema con l'ombra delle palpebre)
            c = Over(c, gold, Cover(dLens - 0.026f) * (dLens > 0f ? 1f : 0f));
            c = Over(c, darkGold, Cover(dLens - 0.012f) * (dLens > -0.0001f ? 1f : 0f));
            float lid = Mathf.Clamp01(Mathf.Abs(ey) / Mathf.Max(0.02f, lens));
            var white = Color.Lerp(new Color(0.95f, 0.82f, 0.50f), new Color(0.70f, 0.48f, 0.20f), lid * lid);
            c = Over(c, white, Cover(dLens));
            // l'iride: anello ambra con il bordo scuro, la pupilla verticale e un riflesso
            float ir = Mathf.Sqrt(ex * ex + ey * ey);
            var iris = Color.Lerp(new Color(0.85f, 0.46f, 0.10f), new Color(0.30f, 0.12f, 0.04f), Smooth(0.07f, 0.165f, ir));
            iris = Color.Lerp(iris, new Color(0.14f, 0.05f, 0.02f), Smooth(0.145f, 0.168f, ir));
            c = Over(c, iris, Cover(ir - 0.168f) * Cover(dLens + 0.004f));
            float pupil = Mathf.Sqrt((ex / 0.032f) * (ex / 0.032f) + (ey / 0.125f) * (ey / 0.125f)) - 1f;
            c = Over(c, new Color(0.02f, 0.01f, 0.01f), Cover(pupil * 0.03f) * Cover(ir - 0.168f) * Cover(dLens + 0.004f));
            c = Over(c, new Color(1f, 0.93f, 0.7f), Cover(Mathf.Sqrt((ex + 0.055f) * (ex + 0.055f) + (ey - 0.07f) * (ey - 0.07f)) - 0.02f) * Cover(dLens + 0.004f));
            // tre galloni sotto l'occhio (la coda di rondine ripetuta)
            for (int k = 0; k < 3; k++)
            {
                float yc = -0.34f - 0.19f * k - 0.30f * Mathf.Abs(X);
                c = Over(c, gold, Cover(Mathf.Abs((Y - yc) * 0.96f) - (0.016f - 0.003f * k)) * (Mathf.Abs(X) < 0.46f ? 1f : 0f));
            }
            c.a = 1f;
            return c;
        }
    }
}
