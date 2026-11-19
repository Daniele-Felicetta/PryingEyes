using System.Collections.Generic;
using PryingEyes.Cattedrale;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>La cattedrale nel deserto: e' la stessa di sempre (HubPlan), ma poggia su un basamento di pietra alto come la scarpata di sabbia e
    /// dal sagrato parte una scalinata larga, che scende dal pavimento della chiesa (y = 0) alla sabbia (DesertWorld.GroundY). Qui anche le
    /// postazioni che il deserto sposta nella cattedrale: i leggii dei libri dei perk (Biblioteca dei Voti) e l'altare della consacrazione, che
    /// ci sono solo se le rispettive opere sono state costruite con le risorse portate dal deserto.</summary>
    public static class DesertCathedral
    {
        public const int Steps = 12;
        const float Run = 0.4f, Rise = 0.2f, Landing = 1.0f;

        /// <summary>Basamento e scalinata, figli della cattedrale (spariscono e si rifanno con lei). Il disegno e' una mesh sola con la pietra della facciata della chiesa
        /// (FacadeKit: zoccolo scuro, cornicione e lastre dei gradini piu' chiari, UV in metri); la fisica e' di scatole nascoste, identiche a quelle di sempre.</summary>
        public static void Build(FloorInstance fi)
        {
            ShrineKit.Mats();
            var root = new GameObject("Basamento e scalinata").transform;
            root.SetParent(fi.root.transform, false);
            var st = FacadeKit.For(fi.plan.theme, true, 0);
            var mb = new MeshBuilder();
            float bottom = DesertWorld.GroundY - 1.0f, top = -0.04f;
            // sotto ogni stanza (e un po' oltre i muri): una scarpata di pietra. Il pavimento della chiesa sta a y = 0, la sabbia piu' in basso.
            foreach (var r in fi.plan.rooms)
            {
                float x0 = r.x0 * 0.1f - 0.3f, x1 = r.x1 * 0.1f + 0.3f, z0 = r.z0 * 0.1f - 0.3f, z1 = r.z1 * 0.1f + 0.3f;
                var c = new Vector3((x0 + x1) * 0.5f, (top + bottom) * 0.5f, (z0 + z1) * 0.5f);
                var sz = new Vector3(x1 - x0, top - bottom, z1 - z0);
                Solid(root, "Basamento", c, sz);
                mb.Box(st.plinth, c, sz, 0f, false, false);
                // il cornicione sotto il pavimento (sporge 12 cm) e lo zoccolo al piede (8 cm): la scarpata non e' piu' un blocco liscio
                mb.Box(st.trim, new Vector3(c.x, top - 0.14f, c.z), new Vector3(sz.x + 0.24f, 0.28f, sz.z + 0.24f), 0f, true, true);
                mb.Box(st.trim, new Vector3(c.x, DesertWorld.GroundY + 0.19f, c.z), new Vector3(sz.x + 0.16f, 0.38f, sz.z + 0.16f), 0f, true, false);
            }
            // le torrette d'angolo del sagrato: dalla sabbia la cattedrale era un recinto basso con i merli e nessuna sagoma (le torri del deserto hanno le loro, FloorBuilder.Turrets). Una per angolo
            // in fuori del recinto di 36 x 18 m, di 2,6 m di lato (piu' strette di quelle delle torri: i banchi del mercato stanno a 3,2 m dal muro), di pietra piena: ci si sbatte contro
            var turrets = new List<(Vector3 centre, Vector3 size)>();
            FloorBuilder.Turrets(fi.plan, fi.plan.rooms[HubPlan.Sagrato], 0f, st, mb, true, 2.6f, turrets);
            foreach (var tt in turrets) Solid(root, "Torretta", tt.centre, tt.size);
            // la scalinata: pianerottolo davanti al parapetto, poi dodici gradini da 20 cm di alzata e 40 di pedata, con le guance di pietra ai lati
            float xa = HubPlan.StairX0, xb = HubPlan.StairX1, cx = (xa + xb) * 0.5f, w = xb - xa;
            float floorBot = DesertWorld.GroundY - 0.3f;
            const float Slab = 0.045f, Nose = 0.03f;
            // un gradino: la scatola di fisica fino alla quota vera, il disegno con l'alzata di pietra e sopra la lastra piu' chiara che sporge sul gradino di sotto
            void Block(string name, float z0, float z1, float topY, bool nose)
            {
                var c = new Vector3(cx, (topY + floorBot) * 0.5f, (z0 + z1) * 0.5f);
                Solid(root, name, c, new Vector3(w, topY - floorBot, z1 - z0));
                float bodyTop = topY - Slab;
                mb.Box(st.wall, new Vector3(cx, (bodyTop + floorBot) * 0.5f, c.z), new Vector3(w, bodyTop - floorBot, z1 - z0), 0f, false, false);
                float n = nose ? Nose : 0f;
                mb.Box(st.trim, new Vector3(cx, topY - Slab * 0.5f, c.z - n * 0.5f), new Vector3(w, Slab, z1 - z0 + n), 0f, true, true);
            }
            Block("Pianerottolo", -Landing, 0f, 0f, false);
            for (int i = 1; i <= Steps; i++)
            {
                float z1 = -Landing - Run * (i - 1), z0 = z1 - Run;
                Block("Gradino", z0, z1, -Rise * i, true);
            }
            // le guance: due muri a spiovente (la fisica resta a gradoni, tutta sotto il disegno) con il copertino di pietra
            float zEnd = -Landing - Run * Steps, slope = Rise / Run;
            float Ltop(float z) => z >= -Landing ? 1.0f : 1.0f + slope * (z + Landing);
            foreach (int side in new[] { -1, 1 })
            {
                float xo = side < 0 ? xa - 0.3f : xb + 0.3f, xi = side < 0 ? xa : xb, xc = (xo + xi) * 0.5f;
                Solid(root, "Guancia", new Vector3(xc, (1.0f + floorBot) * 0.5f, -Landing * 0.5f), new Vector3(0.3f, 1.0f - floorBot, Landing));
                for (int i = 1; i <= Steps; i++)
                {
                    float z1 = -Landing - Run * (i - 1), z0 = z1 - Run;
                    Solid(root, "Guancia", new Vector3(xc, (-Rise * i + 1.0f + floorBot) * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.3f, -Rise * i + 1.0f - floorBot, z1 - z0));
                }
                // le due facce del muro: piana sul pianerottolo, poi a spiovente fino al piede
                foreach (float fx in new[] { xo, xi })
                {
                    var face = new Vector3(fx < xc ? -1f : 1f, 0f, 0f);
                    mb.QuadW(st.wall, new Vector3(fx, floorBot, 0f), new Vector3(fx, floorBot, -Landing), new Vector3(fx, 1.0f, -Landing), new Vector3(fx, 1.0f, 0f), face);
                    mb.QuadW(st.wall, new Vector3(fx, floorBot, -Landing), new Vector3(fx, floorBot, zEnd), new Vector3(fx, Ltop(zEnd), zEnd), new Vector3(fx, 1.0f, -Landing), face);
                }
                mb.QuadW(st.wall, new Vector3(xo, floorBot, zEnd), new Vector3(xi, floorBot, zEnd), new Vector3(xi, Ltop(zEnd), zEnd), new Vector3(xo, Ltop(zEnd), zEnd), Vector3.back);
                // il copertino: una trave piana sul pianerottolo e una a spiovente sui gradini (si sovrappongono un poco al gomito)
                var up = Vector3.up;
                mb.Beam(st.trim, new Vector3(xc, 1.05f, 0f), new Vector3(xc, 1.05f, -Landing - 0.12f), 0.42f, 0.10f, up);
                mb.Beam(st.trim, new Vector3(xc, 1.05f, -Landing), new Vector3(xc, Ltop(zEnd) + 0.05f, zEnd), 0.42f, 0.10f, up);
                // un pilastro al piede della scalinata, con il suo cappello
                float pz = zEnd - 0.2f;
                var pc = new Vector3(xc, (floorBot + Ltop(zEnd) + 0.2f) * 0.5f, pz);
                Solid(root, "Pilastro", pc, new Vector3(0.5f, Ltop(zEnd) + 0.2f - floorBot, 0.5f));
                mb.Box(st.wall, pc, new Vector3(0.5f, Ltop(zEnd) + 0.2f - floorBot, 0.5f), 0f, false, false);
                mb.Box(st.trim, new Vector3(xc, Ltop(zEnd) + 0.24f, pz), new Vector3(0.62f, 0.08f, 0.62f), 0f, true, true);
            }
            var mesh = mb.ToMesh("Basamento e scalinata", out var mats);
            var g = new GameObject("Pietra");
            g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterials = mats;
            // due bracieri in cima alla scalinata, accesi: si vede da lontano dove si scende, anche di notte
            foreach (float bx in new[] { xa + 0.6f, xb - 0.6f })
            {
                var b = new GameObject("Braciere").transform;
                b.SetParent(root, false);
                b.position = new Vector3(bx, 0f, 0.6f);
                // un cesto di fuoco su un treppiede (tre gambe che si allargano al piede, un anello che le tiene, una coppa larga e bassa con l'orlo): non piu' un calice su uno stelo.
                // La coppa e' una mesh sola; dall'alto, dalla scala, si vede la brace dentro
                var bowl = new GameObject("Coppa");
                bowl.transform.SetParent(b, false);
                bowl.AddComponent<MeshFilter>().sharedMesh = BrazierMesh();
                bowl.AddComponent<MeshRenderer>().sharedMaterial = BrazierIron();
                var capsule = bowl.AddComponent<CapsuleCollider>(); capsule.center = new Vector3(0f, 0.55f, 0f); capsule.radius = 0.52f; capsule.height = 1.1f;
                var fire = ShrineKit.Prim(b, PrimitiveType.Cylinder, "Brace", ProcKit.HasMat("kit_brace") ? ProcKit.Mat("kit_brace") : ShrineKit.EmitOwn(new Color(1f, 0.42f, 0.12f), 3f),
                    new Vector3(0f, 0.80f, 0f), new Vector3(0.78f, 0.02f, 0.78f));
                fire.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ShrineKit.PointLight(b, "Fuoco", new Vector3(0f, 1.4f, 0f), new Color(1f, 0.52f, 0.2f), 5.5f, 14f);
                b.gameObject.AddComponent<BrazierFire>();
            }
            // due stendardi con l'occhio, su pali di ferro ai lati dello scalone: dalla sabbia fanno da cornice alla scalinata, dal sagrato si vedono dietro il parapetto, e il vento li muove
            int banner = 0;
            foreach (float sx in new[] { xa - 1.6f, xb + 1.6f })
                DesertBanner.Place(root, new Vector3(sx, 0f, 0.8f), Vector3.back, 0.7f + 2.1f * banner++);
        }

        static Mesh brazierMesh;
        static Material brazierIron;

        /// <summary>Il cesto di un braciere: una coppa larga e bassa con l'orlo (dall'alto si vede dentro), tre gambe che dal piede, largo, salgono e si stringono sotto la coppa con una curva
        /// dolce, un anello che le tiene e tre tacchi piatti al piede. Una mesh sola per tutti i bracieri (la coppa e l'orlo sono tornite da un profilo, raggio e quota; le gambe e l'anello sono
        /// tubi: DesertLathe). La parete interna della coppa e' il ritorno del profilo: le facce guardano da dove si vede la superficie, fuori sul fianco e dentro verso l'asse.</summary>
        static Mesh BrazierMesh()
        {
            if (brazierMesh != null) return brazierMesh;
            var bowl = DesertLathe.Turn("BraciereCoppa", new[]
            {
                new Vector2(0.00f, 0.58f), new Vector2(0.15f, 0.585f), new Vector2(0.31f, 0.62f), new Vector2(0.45f, 0.72f), new Vector2(0.54f, 0.86f), new Vector2(0.58f, 0.97f),     // fuori
                new Vector2(0.595f, 1.00f), new Vector2(0.57f, 1.015f), new Vector2(0.55f, 0.995f),                                                                                    // l'orlo
                new Vector2(0.51f, 0.90f), new Vector2(0.40f, 0.81f), new Vector2(0.25f, 0.75f), new Vector2(0.00f, 0.73f),                                                            // dentro, fino al fondo
            }, 28);
            var parts = new List<(Mesh mesh, Matrix4x4 at)> { (bowl, Matrix4x4.identity) };
            // una gamba (nel piano x-y, il raggio e' x) e il tacco al suo piede, poi tre volte attorno all'asse
            var leg = DesertLathe.Tube("BraciereGamba", new[] { new Vector3(0.60f, 0.015f, 0f), new Vector3(0.52f, 0.22f, 0f), new Vector3(0.40f, 0.42f, 0f), new Vector3(0.27f, 0.60f, 0f) },
                                       new[] { 0.050f, 0.043f, 0.038f, 0.034f }, 7);
            var heel = DesertLathe.Turn("BraciereTacco", new[] { new Vector2(0f, 0f), new Vector2(0.085f, 0f), new Vector2(0.085f, 0.03f), new Vector2(0.055f, 0.045f), new Vector2(0f, 0.045f) }, 12);
            for (int k = 0; k < 3; k++)
            {
                float yaw = 90f + k * 120f;                       // Quaternion.Euler(0, yaw, 0) gira x verso -z: il raggio della gamba segue lo stesso giro del piede
                parts.Add((leg, DesertLathe.At(Vector3.zero, yaw)));
                float rad = yaw * Mathf.Deg2Rad;
                parts.Add((heel, DesertLathe.At(new Vector3(Mathf.Cos(rad) * 0.60f, 0f, -Mathf.Sin(rad) * 0.60f))));
            }
            const int ringN = 32;
            var ringPath = new Vector3[ringN]; var ringR = new float[ringN];
            for (int i = 0; i < ringN; i++) { float a = i * Mathf.PI * 2f / ringN; ringPath[i] = new Vector3(Mathf.Cos(a) * 0.50f, 0.25f, Mathf.Sin(a) * 0.50f); ringR[i] = 0.022f; }
            parts.Add((DesertLathe.Tube("BraciereAnello", ringPath, ringR, 6, true), Matrix4x4.identity));
            brazierMesh = DesertLathe.Join("Braciere", parts);
            return brazierMesh;
        }

        /// <summary>Ghisa arrugginita: scura e poco metallica (il ferro del santuario, metallico al 0,85, senza sonde di riflessi all'aperto veniva nero come un secchio).</summary>
        static Material BrazierIron()
        {
            if (brazierIron != null) return brazierIron;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Braciere_Ghisa" };
            m.SetColor("_BaseColor", new Color(0.22f, 0.17f, 0.14f));
            m.SetFloat("_Metallic", 0.30f);
            m.SetFloat("_Smoothness", 0.38f);
            return brazierIron = m;
        }

        /// <summary>Il fuoco di un braciere: una fiamma di sprite additivi che sale, si assottiglia e vira dal giallo al rosso, un nucleo caldo, un filo di fumo e la brace che salta. Prima c'era solo
        /// un disco acceso sopra la coppa: di giorno un cilindro nero, e nemmeno un fuoco. Le particelle vanno in coordinate del mondo (la fiamma si piega con lo spostarsi del braciere, non
        /// con la rotazione di chi guarda); i materiali sono suoi e muoiono con lui.</summary>
        public sealed class BrazierFire : MonoBehaviour
        {
            /// <summary>Il numero di sistemi di particelle che il fuoco monta (fiamma, nucleo, fumo, brace): la prova controlla che ci siano tutti e che girino.</summary>
            public const int Systems = 4;
            Material flameMat, smokeMat, sparkMat;

            void Awake()
            {
                flameMat = Fx.Unlit(Fx.PuffTex(), true);
                smokeMat = Fx.Unlit(Fx.PuffTex(), false);
                sparkMat = Fx.Unlit(Fx.SparkTex(), true);
                var warm = new Gradient();
                warm.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.93f, 0.58f), 0f), new GradientColorKey(new Color(1f, 0.50f, 0.12f), 0.45f), new GradientColorKey(new Color(0.45f, 0.08f, 0.02f), 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.12f), new GradientAlphaKey(0.65f, 0.5f), new GradientAlphaKey(0f, 1f) });
                var core = new Gradient();
                core.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.98f, 0.80f), 0f), new GradientColorKey(new Color(1f, 0.72f, 0.28f), 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                var smoke = new Gradient();
                smoke.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                              new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.2f), new GradientAlphaKey(0f, 1f) });
                var spark = new Gradient();
                spark.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                              new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
                var shrink = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.12f));
                var grow = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 3.0f));
                // la fiamma larga e il nucleo stretto e piu' chiaro dentro di lei
                Make("Fiamma", flameMat, 0.88f, 0.30f, 0.55f, 1.05f, 0.6f, 1.4f, 0.50f, 0.80f, new Color(1f, 0.46f, 0.12f, 0.55f), 54, 64, -0.14f, 0.38f, shrink, warm, false);
                Make("Nucleo", flameMat, 0.86f, 0.16f, 0.38f, 0.72f, 0.35f, 0.85f, 0.26f, 0.44f, new Color(1f, 0.78f, 0.38f, 0.70f), 30, 36, -0.10f, 0.22f, shrink, core, false);
                Make("Fumo", smokeMat, 1.55f, 0.20f, 3.0f, 4.5f, 0.35f, 0.7f, 0.55f, 0.90f, new Color(0.26f, 0.24f, 0.22f, 0.30f), 7, 36, -0.04f, 0.35f, grow, smoke, true);
                Make("Scintille", sparkMat, 0.95f, 0.30f, 0.8f, 1.9f, 0.7f, 2.2f, 0.03f, 0.07f, new Color(1f, 0.62f, 0.22f, 1f), 11, 28, -0.05f, 0.50f, null, spark, false);
            }

            void Make(string name, Material mat, float y, float radius, float life0, float life1, float speed0, float speed1, float size0, float size1, Color start, float rate, int max,
                      float gravity, float noiseStrength, AnimationCurve size, Gradient colour, bool smoke)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, y, 0f);
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed0, speed1);
                main.startSize = new ParticleSystem.MinMaxCurve(size0, size1);
                main.startColor = start;
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles = max;
                main.gravityModifier = gravity;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = smoke ? 8f : 11f; sh.radius = radius;
                var em = ps.emission; em.rateOverTime = rate;
                var noise = ps.noise; noise.enabled = true; noise.strength = noiseStrength; noise.frequency = smoke ? 0.35f : 0.9f;
                if (size != null) { var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, size); }
                var col = ps.colorOverLifetime; col.enabled = true; col.color = colour;
                if (!smoke && size != null) { var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f); }
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                ps.Play();
            }

            float tintT;

            /// <summary>Il fumo e' un unlit: al crepuscolo e di notte, con l'esposizione alzata, veniva bianco come una nuvola. Ora segue la luce del giorno (DesertSky.LightLevel) e, quando questa
            /// cala, vira a un bruno caldo e piu' scuro, come se lo illuminasse da sotto il fuoco stesso.</summary>
            void Update()
            {
                tintT -= Time.deltaTime;
                if (tintT > 0f || smokeMat == null) return;
                tintT = 0.25f;
                var sky = DesertSky.I;
                float k = sky != null ? sky.LightLevel : 1f;
                var c = Color.Lerp(new Color(0.60f, 0.42f, 0.30f, 1f), Color.white, k) * Mathf.Lerp(0.55f, 1f, k);
                c.a = 1f;
                smokeMat.SetColor("_BaseColor", c);
            }

            void OnDestroy()
            {
                if (flameMat != null) Destroy(flameMat);
                if (smokeMat != null) Destroy(smokeMat);
                if (sparkMat != null) Destroy(sparkMat);
            }
        }

        /// <summary>Una scatola di fisica senza disegno (il disegno e' la mesh della pietra).</summary>
        static void Solid(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>I leggii dei libri e l'altare della consacrazione, se le loro opere sono state costruite. Prima della navmesh (sono ostacoli).</summary>
        public static void PlaceStations(FloorInstance fi)
        {
            var plan = fi.plan;
            var rng = new Rng(plan.seed + 411);
            if (Fabbrica.Costruita("biblioteca_voti") && Biblioteca.Current == null) Biblioteca.Place(fi, plan, plan.rooms[HubPlan.Navata], rng);
            if (Fabbrica.Costruita("consacrazione")) Altare.Place(fi, plan, plan.rooms[HubPlan.Presbiterio], rng);
        }

        /// <summary>La Biblioteca ha offerto tre libri e uno e' stato comprato: dopo un po' i leggii si rinnovano con tre perk nuovi (uno per
        /// bottino, senza dover tornare a un altro piano come nelle catacombe).</summary>
        public static void RefreshLibrary(FloorInstance fi)
        {
            var lib = Biblioteca.Current;
            if (lib != null)
            {
                if (lib.root != null) { fi.propRoots.Remove(lib.root.transform); lib.root.SetActive(false); Object.Destroy(lib.root); }
                Biblioteca.Clear();
            }
            if (!Fabbrica.Costruita("biblioteca_voti")) return;
            var plan = fi.plan;
            var rng = new Rng(plan.seed + 977 + Time.frameCount);
            Biblioteca.Place(fi, plan, plan.rooms[HubPlan.Navata], rng);
            Physics.SyncTransforms();
        }
    }
}
