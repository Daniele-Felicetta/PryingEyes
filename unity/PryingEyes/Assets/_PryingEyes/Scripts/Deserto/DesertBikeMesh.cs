using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>La moto (DesertBike), disegnata a codice con DesertLathe: telaio, forcella e manubrio, serbatoio, sella, motore con le alette, scarico, parafanghi, due ruote a raggi, il faro e il fanalino.
    /// Tutte le misure sono in metri nel sistema della moto (x a destra, y in alto, z avanti, origine a terra a meta' dell'interasse). Le mesh si costruiscono una volta sola e si condividono tra
    /// tutte le moto; solo le due luci hanno il materiale proprio (si accendono una per una). Il sistema e' una piccola gerarchia:
    /// radice (posizione e rotta) -> corpo (beccheggio e rollio) -> telaio e motore, ruota dietro, sterzo (asse inclinato di 22 gradi) -> forcella, manubrio, faro, ruota davanti.
    /// Il perno dello sterzo e' inclinato, quindi il gruppo ha il proprio sistema: le parti dello sterzo si disegnano nel sistema della moto e si portano li' con la matrice inversa.</summary>
    public static partial class DesertBikeMesh
    {
        public const float WheelRadius = 0.34f, WheelBase = 1.38f;
        /// <summary>Quota dell'occhio del pilota nel sistema della moto (davanti alla sella, dietro il manubrio).</summary>
        public static readonly Vector3 Eye = new Vector3(0f, 1.40f, -0.28f);
        static readonly Vector3 Head = new Vector3(0f, 0.98f, 0.43f), AxleFront = new Vector3(0f, WheelRadius, WheelBase * 0.5f), AxleRear = new Vector3(0f, WheelRadius, -WheelBase * 0.5f);
        static readonly Matrix4x4 I = Matrix4x4.identity;

        /// <summary>I pezzi mobili di una moto costruita: lo sterzo e le due ruote girano, il corpo si inclina, le luci si accendono.</summary>
        public sealed class Rig
        {
            public Transform root, body, steer, frontPivot, rearPivot, arms;
            /// <summary>I guanti (figli dello sterzo, uno per lato: 0 sinistra, 1 destra): quello destro si spegne da solo quando la destra impugna l'arma.</summary>
            public readonly Transform[] glove = new Transform[2];
            bool rightHidden;
            public Light headlight;
            public Material lensMat, tailMat;
            public Quaternion steerRest;
            /// <summary>Gli avambracci (figli del corpo) e i due estremi di ognuno: il pugno sulla manopola (nel sistema dello sterzo, che gira) e il gomito (fermo, nel sistema del corpo).</summary>
            public readonly Transform[] sleeve = new Transform[2];
            public readonly Vector3[] handPt = new Vector3[2], elbowPt = new Vector3[2];
            /// <summary>L'occhio del pilota nel sistema del corpo (le moto della pista hanno il loro: DesertBikeDef.eye).</summary>
            public Vector3 eye = Eye;
            /// <summary>Il cupolino delle moto della pista: chi guida in prima persona ci guarda attraverso, e non si vede (<see cref="SetScreens"/>).</summary>
            public readonly List<Renderer> screens = new List<Renderer>();
            /// <summary>I materiali fatti apposta per questa moto (la vernice, la targa, i fari): se ne vanno con lei.</summary>
            public readonly List<Material> ownedMats = new List<Material>();

            public void SetScreens(bool visible)
            {
                foreach (var r in screens) if (r != null && r.enabled != visible) r.enabled = visible;
            }

            public bool ArmsShown => arms != null && arms.gameObject.activeSelf;

            /// <summary>Le braccia si vedono solo dal posto del pilota (in prima persona): gli avambracci e i guanti insieme.</summary>
            public void ShowArms(bool on)
            {
                if (arms != null && arms.gameObject.activeSelf != on) arms.gameObject.SetActive(on);
                for (int i = 0; i < 2; i++) if (sleeve[i] != null && sleeve[i].gameObject.activeSelf != on) sleeve[i].gameObject.SetActive(on);
                ApplyRight();
            }

            /// <summary>Il braccio destro (avambraccio e guanto) si vede o no, restando le braccia come sono: in sella con l'arma in mano la destra non e' sul manubrio e al suo posto c'e' il braccio dell'arma.</summary>
            public void ShowRightArm(bool on)
            {
                rightHidden = !on;
                ApplyRight();
            }

            void ApplyRight()
            {
                bool v = ArmsShown && !rightHidden;
                if (glove[1] != null && glove[1].gameObject.activeSelf != v) glove[1].gameObject.SetActive(v);
                if (sleeve[1] != null && sleeve[1].gameObject.activeSelf != v) sleeve[1].gameObject.SetActive(v);
            }

            /// <summary>Ogni avambraccio e' un tubo lungo uno (lungo z) che si allunga e si gira ogni volta dal gomito, fermo nel corpo, al pugno, che segue il manubrio: cosi' girando lo sterzo il braccio non gira
            /// tutto insieme alla forcella (la spalla finirebbe in mezzo alla vista) ma si piega verso la manopola. Il gomito sta sotto il bordo basso dello schermo: si vede solo l'avambraccio che esce da sotto.</summary>
            public void PoseArms()
            {
                if (!ArmsShown) return;
                for (int i = 0; i < 2; i++)
                {
                    var a = sleeve[i];
                    if (a == null) continue;
                    Vector3 elbow = body.TransformPoint(elbowPt[i]);
                    Vector3 d = steer.TransformPoint(handPt[i]) - elbow;
                    float len = d.magnitude;
                    if (len < 0.05f) continue;
                    a.SetPositionAndRotation(elbow, Quaternion.LookRotation(d / len, body.up));
                    a.localScale = new Vector3(1f, 1f, len);
                }
            }
        }

        sealed class Kit
        {
            public Mesh frame, engine, exhaust, paint, seat, fork, bars, grips, steerPaint, plate, lens, sleeve, tail, tyre, wheelMetalFront, wheelMetalRear, trim, trimSteer, plateRear, glass;
            public readonly Mesh[] gloves = new Mesh[2];            // un guanto per lato (0 sinistra, 1 destra)
            public Material mPaint, mFrame, mMetal, mSteel, mRubber, mSeat, mPlate, mCloth, mGlove, mBrass, mGlass;
            public Matrix4x4 steerM;
            public Quaternion steerRot;
            public readonly Vector3[] handPt = new Vector3[2], elbowPt = new Vector3[2];
        }

        static Kit kit;

        // ------------------------------------------------------------------ materiali
        static Material Lit(string name, Color c, float metal, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Moto_" + name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        static Material Glow(string name, Color c, float k)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Moto_" + name };
            m.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
            m.SetFloat("_Smoothness", 0.7f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * k);
            return m;
        }

        // ------------------------------------------------------------------ piccoli attrezzi
        static Vector3[] P(params float[] c)
        {
            var o = new Vector3[c.Length / 3];
            for (int i = 0; i < o.Length; i++) o[i] = new Vector3(c[i * 3], c[i * 3 + 1], c[i * 3 + 2]);
            return o;
        }

        /// <summary>Una spezzata di Catmull-Rom per i punti dati: i tubi del telaio e dello scarico non sembrano poligoni.</summary>
        static Vector3[] Smooth(Vector3[] p, int per)
        {
            var o = new List<Vector3>();
            for (int i = 0; i < p.Length - 1; i++)
            {
                Vector3 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Length - 1)];
                for (int k = 0; k < per; k++)
                {
                    float t = k / (float)per, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(p[p.Length - 1]);
            return o.ToArray();
        }

        static (Mesh mesh, Matrix4x4 at) Tube(string name, Vector3[] path, float r, int sides = 6) => Tube(name, path, t => r, sides);

        static (Mesh mesh, Matrix4x4 at) Tube(string name, Vector3[] path, System.Func<float, float> radius, int sides = 6)
        {
            var rs = new float[path.Length];
            for (int i = 0; i < rs.Length; i++) rs[i] = radius(i / (float)Mathf.Max(1, path.Length - 1));
            return (DesertLathe.Tube(name, path, rs, sides), I);
        }

        static Matrix4x4 At(Vector3 pos, Quaternion rot) => Matrix4x4.TRS(pos, rot, Vector3.one);
        static Matrix4x4 At(Vector3 pos, Quaternion rot, Vector3 scale) => Matrix4x4.TRS(pos, rot, scale);

        /// <summary>Un parallelepipedo con le facce piatte, centrato sull'origine.</summary>
        static Mesh Cube(string name, Vector3 size)
        {
            var v = new List<Vector3>(24); var n = new List<Vector3>(24); var uv = new List<Vector2>(24); var t = new List<int>(36);
            Vector3 h = size * 0.5f;
            void Face(Vector3 nrm, Vector3 u, Vector3 w)
            {
                int b = v.Count;
                Vector3 c = Vector3.Scale(nrm, h);
                v.Add(c - u - w); v.Add(c + u - w); v.Add(c + u + w); v.Add(c - u + w);
                for (int i = 0; i < 4; i++) n.Add(nrm);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
                if (Vector3.Dot(Vector3.Cross(v[b + 1] - v[b], v[b + 2] - v[b]), nrm) >= 0f) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
            }
            Face(Vector3.forward, Vector3.right * h.x, Vector3.up * h.y);
            Face(Vector3.back, Vector3.right * h.x, Vector3.up * h.y);
            Face(Vector3.right, Vector3.forward * h.z, Vector3.up * h.y);
            Face(Vector3.left, Vector3.forward * h.z, Vector3.up * h.y);
            Face(Vector3.up, Vector3.right * h.x, Vector3.forward * h.z);
            Face(Vector3.down, Vector3.right * h.x, Vector3.forward * h.z);
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>I pezzi mandati nel sistema dello sterzo (che e' inclinato): la matrice di ogni pezzo e' moltiplicata per l'inversa di quella del gruppo.</summary>
        static List<(Mesh mesh, Matrix4x4 at)> ToSteer(List<(Mesh mesh, Matrix4x4 at)> l, Matrix4x4 inv)
        {
            var o = new List<(Mesh, Matrix4x4)>(l.Count);
            foreach (var p in l) o.Add((p.mesh, inv * p.at));
            return o;
        }

        /// <summary>Un solido di rotazione disteso lungo z (la sua y diventa la z della moto, la sua z diventa -y): il serbatoio e la sella.</summary>
        static readonly Quaternion Lay = Quaternion.Euler(90f, 0f, 0f);

        // ------------------------------------------------------------------ costruzione delle mesh condivise
        static void Ensure()
        {
            if (kit != null && kit.frame != null && kit.mPaint != null) return;
            var k = new Kit();
            k.steerRot = Quaternion.FromToRotation(Vector3.up, (Head - AxleFront).normalized);
            k.steerM = Matrix4x4.TRS(Head, k.steerRot, Vector3.one);
            Matrix4x4 inv = k.steerM.inverse;
            float R = WheelRadius;
            var det = MakeDetails(k.steerRot, inv);                          // i dettagli meccanici (DesertBikeMesh.Details.cs): ognuno finisce nella lista del suo materiale

            // ---- telaio (nero)
            var frame = new List<(Mesh mesh, Matrix4x4 at)>();
            foreach (float s in new[] { -1f, 1f })
            {
                frame.Add(Tube("culla", Smooth(P(s * 0.06f, 0.93f, 0.40f, s * 0.09f, 0.66f, 0.29f, s * 0.10f, 0.43f, 0.12f, s * 0.09f, 0.37f, -0.12f), 4), 0.026f));
                frame.Add(Tube("traliccio", Smooth(P(s * 0.08f, 0.86f, -0.05f, s * 0.07f, 0.87f, -0.40f, s * 0.06f, 0.85f, -0.78f), 4), 0.016f));
                frame.Add(Tube("puntone", Smooth(P(s * 0.09f, 0.39f, -0.14f, s * 0.075f, 0.62f, -0.30f, s * 0.07f, 0.86f, -0.46f), 4), 0.016f));
                frame.Add(Tube("forcellone", Smooth(P(s * 0.09f, 0.40f, -0.14f, s * 0.10f, 0.37f, -0.42f, s * 0.105f, R, -WheelBase * 0.5f), 4), 0.022f));
                frame.Add((Cube("pedana", new Vector3(0.09f, 0.025f, 0.13f)), At(new Vector3(s * 0.20f, 0.36f, -0.06f), Quaternion.identity)));
            }
            frame.Add(Tube("trave", Smooth(P(0f, 0.975f, 0.40f, 0f, 0.985f, 0.12f, 0f, 0.90f, -0.12f), 4), 0.030f));
            frame.Add(Tube("asse", P(-0.12f, R, AxleRear.z, 0.12f, R, AxleRear.z), 0.018f));
            frame.AddRange(det.frame);
            k.frame = DesertLathe.Join("Moto telaio", frame);

            // ---- motore (alluminio) e scarico (acciaio)
            var eng = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (Cube("carter", new Vector3(0.20f, 0.26f, 0.34f)), At(new Vector3(0f, 0.45f, 0.02f), Quaternion.identity)),
                (Fins(), At(new Vector3(0f, 0.585f, 0.11f), Quaternion.Euler(18f, 0f, 0f))),
                (Cube("testa", new Vector3(0.14f, 0.09f, 0.15f)), At(new Vector3(0f, 0.775f, 0.175f), Quaternion.Euler(18f, 0f, 0f))),
                (DesertLathe.Turn("coperchio", new[] { new Vector2(0f, -0.016f), new Vector2(0.095f, -0.016f), new Vector2(0.095f, 0.016f), new Vector2(0f, 0.016f) }, 14), At(new Vector3(0.115f, 0.43f, -0.01f), Quaternion.Euler(0f, 0f, 90f))),
            };
            eng.AddRange(det.metal);
            k.engine = DesertLathe.Join("Moto motore", eng);
            var ex = new List<(Mesh mesh, Matrix4x4 at)>
            {
                Tube("scarico", Smooth(P(0.075f, 0.66f, 0.22f, 0.13f, 0.56f, 0.31f, 0.165f, 0.42f, 0.28f, 0.175f, 0.36f, 0.10f, 0.18f, 0.38f, -0.25f, 0.18f, 0.50f, -0.60f, 0.17f, 0.56f, -0.92f), 5),
                    t => t < 0.62f ? 0.021f : Mathf.Lerp(0.021f, 0.047f, Mathf.SmoothStep(0f, 1f, (t - 0.62f) / 0.12f)), 8),
            };
            ex.AddRange(det.steel);
            k.exhaust = DesertLathe.Join("Moto scarico", ex);

            // ---- vernice (rosso sbiadito dal sole): serbatoio e parafango dietro (i convogliatori piatti ai lati del serbatoio, nella prima prova, sembravano pannelli: tolti)
            var paint = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (DesertLathe.Turn("serbatoio", new[] { new Vector2(0f, -0.21f), new Vector2(0.05f, -0.20f), new Vector2(0.085f, -0.16f), new Vector2(0.105f, -0.10f), new Vector2(0.115f, -0.02f),
                    new Vector2(0.115f, 0.06f), new Vector2(0.105f, 0.13f), new Vector2(0.08f, 0.18f), new Vector2(0.04f, 0.205f), new Vector2(0f, 0.21f) }, 14),
                    At(new Vector3(0f, 0.99f, 0.14f), Lay, new Vector3(0.95f, 1f, 0.85f))),
            };
            var rearFender = Smooth(Arc(AxleRear, 0.40f, 12f, 128f, 7), 3);
            paint.Add((DesertLathe.Tube("parafango dietro", rearFender, Fill(rearFender.Length, 0.032f), 6), At(Vector3.zero, Quaternion.identity, new Vector3(2.4f, 1f, 1f))));
            k.paint = DesertLathe.Join("Moto vernice", paint);

            // ---- sella
            var seat = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (DesertLathe.Turn("sella", new[] { new Vector2(0f, -0.33f), new Vector2(0.04f, -0.32f), new Vector2(0.08f, -0.28f), new Vector2(0.10f, -0.18f), new Vector2(0.105f, 0f),
                    new Vector2(0.10f, 0.18f), new Vector2(0.085f, 0.27f), new Vector2(0.05f, 0.32f), new Vector2(0f, 0.34f) }, 12), At(new Vector3(0f, 0.885f, -0.28f), Lay, new Vector3(1.15f, 1f, 0.42f))),
            };
            seat.AddRange(det.seat);
            k.seat = DesertLathe.Join("Moto sella", seat);
            k.trim = DesertLathe.Join("Moto ottone", det.trim);
            k.plateRear = DesertLathe.Join("Moto targa dietro", det.plateRear);

            // ---- fanalino (la mesh sola: il materiale e' di ogni moto)
            k.tail = Cube("fanalino", new Vector3(0.07f, 0.035f, 0.016f));

            // ---- sterzo: forcella, manubrio, impugnature, faro, targa, parafango davanti, lente
            var fork = new List<(Mesh mesh, Matrix4x4 at)>();
            var bars = new List<(Mesh mesh, Matrix4x4 at)>();
            var grips = new List<(Mesh mesh, Matrix4x4 at)>();
            var sp = new List<(Mesh mesh, Matrix4x4 at)>();
            var plate = new List<(Mesh mesh, Matrix4x4 at)>();
            var lens = new List<(Mesh mesh, Matrix4x4 at)>();
            foreach (float s in new[] { -1f, 1f })
            {
                // i foderi (nel sistema dello sterzo l'asse e' y, quindi si disegnano dritti)
                fork.Add(Tube("stelo", P(s * 0.075f, 0.14f, 0f, s * 0.075f, -0.26f, 0f), 0.019f, 8));
                fork.Add(Tube("fodero", P(s * 0.075f, -0.20f, 0f, s * 0.075f, -0.70f, 0f), 0.025f, 8));
            }
            fork.Add((Cube("piastra", new Vector3(0.21f, 0.04f, 0.075f)), At(new Vector3(0f, 0.115f, 0f), Quaternion.identity)));
            fork.Add((Cube("piastra bassa", new Vector3(0.20f, 0.035f, 0.07f)), At(new Vector3(0f, -0.05f, 0f), Quaternion.identity)));
            // ... ma il manubrio, il faro e la targa nascono nel sistema della moto: la matrice dello sterzo li porta li'
            var barsBike = new List<(Mesh mesh, Matrix4x4 at)>();
            barsBike.Add(Tube("manubrio", Smooth(HandleBar(k.steerRot), 4), 0.012f, 8));
            foreach (float s in new[] { -1f, 1f })
            {
                var a = GripAt(k.steerRot, s, 0.28f); var b = GripAt(k.steerRot, s, 0.43f);
                var gripBike = new List<(Mesh mesh, Matrix4x4 at)> { Tube("manopola", new[] { a, b }, 0.019f, 8) };
                grips.AddRange(ToSteer(gripBike, inv));
            }
            barsBike.Add((DesertLathe.Turn("faro", new[] { new Vector2(0f, -0.035f), new Vector2(0.075f, -0.035f), new Vector2(0.075f, 0.03f), new Vector2(0.06f, 0.04f), new Vector2(0f, 0.04f) }, 16), At(new Vector3(0f, 1.02f, 0.58f), Lay)));
            bars.AddRange(ToSteer(barsBike, inv));
            var plateBike = new List<(Mesh mesh, Matrix4x4 at)> { (Cube("targa", new Vector3(0.20f, 0.17f, 0.012f)), At(new Vector3(0f, 0.895f, 0.605f), k.steerRot)) };
            plate.AddRange(ToSteer(plateBike, inv));
            var lensBike = new List<(Mesh mesh, Matrix4x4 at)> { (DesertLathe.Turn("lente", new[] { new Vector2(0.066f, 0f), new Vector2(0f, 0f) }, 16), At(new Vector3(0f, 1.02f, 0.625f), Lay)) };
            lens.AddRange(ToSteer(lensBike, inv));
            var frontFender = Smooth(Arc(Vector3.zero, 0.395f, 18f, 132f, 7), 3);
            var fenderBike = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (DesertLathe.Tube("parafango davanti", frontFender, Fill(frontFender.Length, 0.03f), 6), At(AxleFront, Quaternion.identity, new Vector3(2.2f, 1f, 1f))),
            };
            sp.AddRange(ToSteer(fenderBike, inv));
            fork.AddRange(det.fork);
            k.fork = DesertLathe.Join("Moto forcella", fork);
            bars.AddRange(det.bars);
            k.bars = DesertLathe.Join("Moto manubrio", bars);
            grips.AddRange(det.grips);
            k.grips = DesertLathe.Join("Moto manopole", grips);
            k.trimSteer = DesertLathe.Join("Moto ottone sterzo", det.trimSteer);
            k.glass = DesertLathe.Join("Moto vetri", det.glass);
            k.steerPaint = DesertLathe.Join("Moto parafango", sp);
            k.plate = DesertLathe.Join("Moto targa", plate);
            k.lens = DesertLathe.Join("Moto lente", lens);

            // ---- le braccia del pilota (si vedono solo in prima persona, dal suo posto): l'avambraccio e' un tubo di lunghezza uno (Rig.PoseArms lo mette ogni volta tra il gomito e il pugno), il guanto
            // avvolge la manopola e gira con lo sterzo. Nella prima prova la manica era un tubo unico dalla spalla in un pezzo con la forcella: in curva la spalla girava con lo sterzo e attraversava la vista.
            int ai = 0;
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 g0 = GripAt(k.steerRot, s, 0.285f), g1 = GripAt(k.steerRot, s, 0.395f);
                var fist = new[] { g0, Vector3.Lerp(g0, g1, 0.5f), g1 };
                k.gloves[ai] = DesertLathe.Join(ai == 0 ? "Moto guanto sinistro" : "Moto guanto destro", ToSteer(new List<(Mesh mesh, Matrix4x4 at)> { (DesertLathe.Tube("guanto", fist, new[] { 0.028f, 0.036f, 0.027f }, 8), I) }, inv));
                k.handPt[ai] = inv.MultiplyPoint3x4(GripAt(k.steerRot, s, 0.34f));
                k.elbowPt[ai] = new Vector3(s * 0.36f, 0.99f, -0.27f);
                ai++;
            }
            k.sleeve = DesertLathe.Tube("Moto avambraccio", new[] { Vector3.zero, new Vector3(0f, 0f, 0.5f), Vector3.forward }, new[] { 0.040f, 0.037f, 0.031f }, 8);

            // ---- ruota (asse lungo x, nel piano yz): pneumatico, cerchio, mozzo e raggi
            var tyre = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (DesertLathe.Turn("gomma", new[] { new Vector2(0.235f, -0.05f), new Vector2(0.29f, -0.058f), new Vector2(0.325f, -0.046f), new Vector2(0.340f, -0.022f),
                    new Vector2(0.340f, 0.022f), new Vector2(0.325f, 0.046f), new Vector2(0.29f, 0.058f), new Vector2(0.235f, 0.05f) }, 28), At(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))),
            };
            tyre.AddRange(det.tread);
            k.tyre = DesertLathe.Join("Moto gomma", tyre);
            var wmFront = WheelMetal(); wmFront.AddRange(det.discFront);
            var wmRear = WheelMetal(); wmRear.AddRange(det.discRear);
            k.wheelMetalFront = DesertLathe.Join("Moto raggi davanti", wmFront);
            k.wheelMetalRear = DesertLathe.Join("Moto raggi dietro", wmRear);

            // ---- materiali condivisi (la luce di sole, a mezzogiorno, brucia i colori chiari: tinte sobrie)
            k.mPaint = Lit("Vernice", new Color(0.46f, 0.14f, 0.09f), 0.12f, 0.50f);
            k.mFrame = Lit("Telaio", new Color(0.035f, 0.035f, 0.04f), 0.30f, 0.55f);
            k.mMetal = Lit("Alluminio", new Color(0.40f, 0.40f, 0.42f), 0.30f, 0.50f);
            k.mSteel = Lit("Acciaio", new Color(0.26f, 0.24f, 0.23f), 0.30f, 0.45f);
            k.mRubber = Lit("Gomma", new Color(0.025f, 0.025f, 0.027f), 0f, 0.12f);
            k.mSeat = Lit("Sella", new Color(0.05f, 0.044f, 0.040f), 0f, 0.30f);
            k.mPlate = Lit("Targa", new Color(0.50f, 0.48f, 0.40f), 0f, 0.30f);
            k.mCloth = Lit("Manica", new Color(0.46f, 0.41f, 0.28f), 0f, 0.15f);          // un giubbotto di tela cachi consumata; nero nella prima prova (in ombra non si distingueva dai guanti) e ancora quasi nero a 0,23 (avambracci pesanti contro la sabbia)
            k.mGlove = Lit("Guanto", new Color(0.20f, 0.12f, 0.07f), 0f, 0.35f);          // cuoio
            k.mGlass = Lit("Vetro", new Color(0.50f, 0.55f, 0.60f), 0.5f, 0.92f);          // il vetro degli specchietti: il cielo che riflette, di giorno, e' un'emissione (SetGlassLight)
            k.mGlass.EnableKeyword("_EMISSION");
            k.mGlass.SetColor("_EmissionColor", new Color(0.30f, 0.37f, 0.45f));
            k.mBrass = Lit("Ottone", new Color(0.62f, 0.46f, 0.20f), 0.75f, 0.55f);        // i filetti del serbatoio, il tappo, le pinze dei freni e la cornice del faro
            kit = k;
        }

        /// <summary>Cerchio, mozzo e sedici raggi di una ruota (asse lungo x): una lista nuova a ogni chiamata, perche' Join consuma i pezzi (davanti e dietro aggiungono ognuna il proprio disco).</summary>
        static List<(Mesh mesh, Matrix4x4 at)> WheelMetal()
        {
            var wm = new List<(Mesh mesh, Matrix4x4 at)>
            {
                (DesertLathe.Turn("cerchio", new[] { new Vector2(0.245f, -0.045f), new Vector2(0.245f, 0.045f), new Vector2(0.228f, 0.045f), new Vector2(0.228f, -0.045f) }, 28), At(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))),
                (DesertLathe.Turn("mozzo", new[] { new Vector2(0f, -0.07f), new Vector2(0.04f, -0.07f), new Vector2(0.04f, 0.07f), new Vector2(0f, 0.07f) }, 12), At(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))),
            };
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f + side * 22.5f;
                    var rot = Quaternion.Euler(a, 0f, 0f);
                    wm.Add((Cube("raggio", new Vector3(0.010f, 0.19f, 0.010f)), At(rot * new Vector3(side == 0 ? -0.022f : 0.022f, 0.135f, 0f), rot)));
                }
            return wm;
        }

        /// <summary>Un cilindro con le alette di raffreddamento (asse y).</summary>
        static Mesh Fins()
        {
            var pr = new List<Vector2> { new Vector2(0f, -0.11f), new Vector2(0.068f, -0.11f) };
            for (int i = 0; i < 5; i++)
            {
                float y = -0.09f + i * 0.045f;
                pr.Add(new Vector2(0.068f, y)); pr.Add(new Vector2(0.086f, y)); pr.Add(new Vector2(0.086f, y + 0.014f)); pr.Add(new Vector2(0.068f, y + 0.014f));
            }
            pr.Add(new Vector2(0.068f, 0.11f)); pr.Add(new Vector2(0f, 0.11f));
            return DesertLathe.Turn("alette", pr.ToArray(), 14);
        }

        /// <summary>Un arco attorno a 'center' nel piano yz: l'angolo e' misurato dalla linea orizzontale in avanti, salendo e tornando indietro (come la ruota vista da destra).</summary>
        static Vector3[] Arc(Vector3 center, float radius, float a0, float a1, int n)
        {
            var o = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)(n - 1)) * Mathf.Deg2Rad;
                o[i] = center + new Vector3(0f, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius);
            }
            return o;
        }

        static float[] Fill(int n, float r)
        {
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = r;
            return o;
        }

        /// <summary>Il manubrio nel sistema della moto: i punti sono dati nel sistema dello sterzo (y lungo l'asse, z in avanti e in alto) e portati fuori.</summary>
        static Vector3[] HandleBar(Quaternion steerRot)
        {
            var l = P(-0.43f, 0.20f, -0.13f, -0.36f, 0.175f, -0.07f, -0.20f, 0.155f, -0.015f, 0f, 0.15f, 0f, 0.20f, 0.155f, -0.015f, 0.36f, 0.175f, -0.07f, 0.43f, 0.20f, -0.13f);
            for (int i = 0; i < l.Length; i++) l[i] = Head + steerRot * l[i];
            return l;
        }

        /// <summary>Un punto del manubrio, nel sistema della moto: 'x' e' la distanza dal centro (m), s il lato.</summary>
        static Vector3 GripAt(Quaternion steerRot, float s, float x)
        {
            float u = Mathf.InverseLerp(0.20f, 0.43f, x);
            var local = new Vector3(s * x, Mathf.Lerp(0.155f, 0.20f, u), Mathf.Lerp(-0.015f, -0.13f, u));
            return Head + steerRot * local;
        }

        // ------------------------------------------------------------------ montaggio di una moto
        static Transform Part(Transform parent, string name, Mesh mesh, Material mat, bool shadows = true)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        static Transform Wheel(Transform parent, string name, Vector3 at, bool front)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = at;
            Part(pivot, "Gomma", kit.tyre, kit.mRubber);
            Part(pivot, front ? "Cerchio, raggi e disco" : "Cerchio, raggi, disco e corona", front ? kit.wheelMetalFront : kit.wheelMetalRear, kit.mMetal);
            return pivot;
        }

        /// <summary>Costruisce una moto sotto 'parent' (la radice: un oggetto a terra, girato come la rotta). 'withArms': le braccia del pilota, per chi la guida in prima persona.</summary>
        public static Rig Build(Transform parent, bool withArms)
        {
            Ensure();
            var rig = new Rig { root = parent };
            rig.body = new GameObject("Corpo").transform;
            rig.body.SetParent(parent, false);
            Part(rig.body, "Telaio", kit.frame, kit.mFrame);
            Part(rig.body, "Motore", kit.engine, kit.mMetal);
            Part(rig.body, "Scarico", kit.exhaust, kit.mSteel);
            Part(rig.body, "Vernice", kit.paint, kit.mPaint);
            Part(rig.body, "Sella", kit.seat, kit.mSeat);
            Part(rig.body, "Ottone", kit.trim, kit.mBrass);
            Part(rig.body, "Targa dietro", kit.plateRear, kit.mPlate);
            rig.rearPivot = Wheel(rig.body, "Ruota dietro", AxleRear, false);

            rig.tailMat = Glow("Fanalino", new Color(1f, 0.08f, 0.04f), 1.2f);
            var tl = Part(rig.body, "Fanalino", kit.tail, rig.tailMat, false);
            tl.localPosition = new Vector3(0f, 0.80f, -0.985f);
            tl.localRotation = Quaternion.Euler(-14f, 0f, 0f);

            rig.steer = new GameObject("Sterzo").transform;
            rig.steer.SetParent(rig.body, false);
            rig.steer.localPosition = Head;
            rig.steer.localRotation = rig.steerRest = kit.steerRot;
            Part(rig.steer, "Forcella", kit.fork, kit.mMetal);
            Part(rig.steer, "Manubrio e faro", kit.bars, kit.mFrame);
            Part(rig.steer, "Manopole", kit.grips, kit.mRubber);
            Part(rig.steer, "Parafango", kit.steerPaint, kit.mPaint);
            Part(rig.steer, "Targa", kit.plate, kit.mPlate);
            Part(rig.steer, "Ottone sterzo", kit.trimSteer, kit.mBrass);
            Part(rig.steer, "Vetri", kit.glass, kit.mGlass, false);
            rig.lensMat = Glow("Faro", new Color(1f, 0.93f, 0.78f), 0.9f);
            Part(rig.steer, "Lente", kit.lens, rig.lensMat, false);
            rig.frontPivot = Wheel(rig.steer, "Ruota davanti", kit.steerM.inverse.MultiplyPoint3x4(AxleFront), true);

            rig.arms = new GameObject("Guanti").transform;
            rig.arms.SetParent(rig.steer, false);
            for (int i = 0; i < 2; i++)
            {
                rig.glove[i] = Part(rig.arms, i == 0 ? "Guanto sinistro" : "Guanto destro", kit.gloves[i], kit.mGlove, false);
                rig.sleeve[i] = Part(rig.body, i == 0 ? "Avambraccio sinistro" : "Avambraccio destro", kit.sleeve, kit.mCloth, false);
                rig.handPt[i] = kit.handPt[i];
                rig.elbowPt[i] = kit.elbowPt[i];
            }
            rig.ShowArms(withArms);

            // il faro: un cono di luce davanti, spento di giorno (DesertBike lo accende al buio)
            var lg = new GameObject("Luce del faro");
            lg.transform.SetParent(rig.body, false);
            lg.transform.localPosition = new Vector3(0f, 1.02f, 0.64f);
            lg.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = 55f; l.spotAngle = 68f; l.innerSpotAngle = 34f;
            l.color = new Color(1f, 0.93f, 0.80f);
            l.intensity = 105f;                    // la torcia del giocatore ne ha 118 (Torch.BeamIntensity): a 9, nella prima prova, la sabbia davanti restava buia
            l.shadows = LightShadows.None;
            l.bounceIntensity = 0f;
            l.enabled = false;
            rig.headlight = l;
            return rig;
        }

        /// <summary>Il vetro degli specchietti riflette il cielo: di giorno un grigio-azzurro chiaro, di notte (o al chiuso) quasi niente. Il materiale e' uno solo per tutte le moto: lo aggiorna chi le accende.</summary>
        public static void SetGlassLight(float day)
        {
            if (kit != null && kit.mGlass != null) kit.mGlass.SetColor("_EmissionColor", new Color(0.30f, 0.37f, 0.45f) * Mathf.Clamp01(day));
        }

        /// <summary>Dove tocca terra ognuna delle due ruote (nel sistema della moto), per i raggi che ne misurano la quota.</summary>
        public static Vector3 FrontContact => new Vector3(0f, 0f, WheelBase * 0.5f);
        public static Vector3 RearContact => new Vector3(0f, 0f, -WheelBase * 0.5f);
    }
}
