using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Gli effetti visivi delle magie e dei sigilli (2026-11-16, al posto dei quad tondi di Fx): sistemi di particelle di Unity
    /// condivisi, uno per genere, nel mondo, con lo shader PryingEyes/MagicParticle e le trame di VfxTextures.
    ///  - fuoco (piccolo per mano e arma, grande per palle di fuoco, esplosioni e nemici in fiamme): lingue animate a 64 fotogrammi
    ///    sfumati, erose dall'eta' (si consumano dai bordi), con la turbolenza, che crescono e si raffreddano dal giallo al rosso;
    ///  - fumo (trasparente, illuminato dall'alto, che ribolle e si allarga), nebbia di gelo (meta' additiva), scintille allungate sulla
    ///    velocita' che rimbalzano sui muri, braci che vagano, bagliori, schegge di ghiaccio, gocce che cadono e muoiono dove toccano,
    ///    rune che si accendono e spengono, anelli d'urto (di fronte e a terra);
    ///  - fulmini: spezzate frastagliate (spostamento del punto medio) con i rami, ridisegnate a scatti finche' vivono, cuore bianco e
    ///    alone del colore;
    ///  - il cerchio del Dodecaedro (i grimori) che si apre davanti alla mano e ruota; la scia luminosa del dito che traccia un sigillo;
    ///    il nastro di sangue del Sigillo di Sangue.
    /// Senza lo shader (una build solo-script sopra una build vecchia) On e' falso e MagicFx usa i quad di Fx come prima.</summary>
    public static class MagicVfx
    {
        public enum Kind { Fire, FireBig, Smoke, Spark, Ember, Glow, Mist, Shard, Drop, Rune, Ring, GroundRing, Count }

        /// <summary>La tinta del fuoco maledetto (Essenza, Arma Maledetta): il giallo della trama diventa rosso cupo, l'arancio brace. Il
        /// cremisi dell'interfaccia (MagicFx.Essence) ha del blu e col bagliore il fuoco veniva rosa.</summary>
        public static readonly Color CursedFire = new Color(1f, 0.24f, 0.11f);

        static ParticleSystem[] sys;
        static bool tried;
        static GameObject root;
        static Shader shader;
        static Material beamMat, ribbonMat, circleMat;

        /// <summary>Gli effetti nuovi sono disponibili (lo shader c'e').</summary>
        public static bool On { get { Ensure(); return sys != null; } }

        /// <summary>Al caricamento del giocatore: le trame si cominciano a calcolare in un thread a parte.</summary>
        public static void Warm() { if (Shader.Find("PryingEyes/MagicParticle") != null) VfxTextures.Begin(); }

        // ------------------------------------------------------------------ costruzione
        static void Ensure()
        {
            if (sys != null || tried) return;
            tried = true;
            shader = Shader.Find("PryingEyes/MagicParticle");
            if (shader == null) { Debug.LogWarning("[PryingEyes] PryingEyes/MagicParticle non c'e' nella build: effetti delle magie semplici"); return; }
            try { Build(); }
            catch (System.Exception e)
            {
                // trame o sistemi non costruiti: si torna agli effetti di prima invece di lanciare a ogni fiammella
                Debug.LogException(e);
                if (root != null) Object.Destroy(root);
                root = null; sys = null;
            }
        }

        static void Build()
        {
            root = new GameObject("MagiaVfx");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<MagicVfxDriver>();
            var s = new ParticleSystem[(int)Kind.Count];
            const int Sheet = VfxTextures.SheetCols;

            // fuoco piccolo (mano, arma) e grande (palla di fuoco, esplosioni, nemici)
            for (int big = 0; big < 2; big++)
            {
                var m = Mat(VfxTextures.Id.Fire, big == 1 ? 1.6f : 1.6f, 1f, true, 0.30f, big == 1 ? 0.25f : 0.025f, true, 3002);
                var ps = Make(big == 1 ? "fuoco_grande" : "fuoco", m, big == 1 ? 1600 : 900, ParticleSystemRenderMode.Billboard, -0.04f, Sheet, Sheet, false, false);
                Gradient g = Grad(new[] { (Color.white, 0f), (new Color(1f, 0.93f, 0.86f), 0.35f), (new Color(0.72f, 0.5f, 0.4f), 1f) },
                                  new[] { (0.7f, 0f), (1f, 0.12f), (0.82f, 0.45f), (0f, 1f) });
                ColorLife(ps, g);
                SizeLife(ps, Curve((0f, 0.5f), (0.25f, 1f), (1f, big == 1 ? 0.85f : 0.7f)));
                Noise(ps, big == 1 ? 0.65f : 0.16f, big == 1 ? 1.3f : 7f, 1.3f);
                Drag(ps, big == 1 ? 1.4f : 1.8f);
                s[big == 1 ? (int)Kind.FireBig : (int)Kind.Fire] = ps;
            }
            {
                var ps = Make("fumo", Mat(VfxTextures.Id.Smoke, 1f, 0f, true, 0.45f, 0.3f, true, 3000), 700, ParticleSystemRenderMode.Billboard, -0.02f, Sheet, Sheet, false, true);
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.14f), (0.75f, 0.55f), (0f, 1f) }));
                SizeLife(ps, Curve((0f, 0.45f), (0.4f, 1f), (1f, 1.6f)));
                Spin(ps, 0.5f);
                Noise(ps, 0.22f, 0.8f, 0.6f);
                Drag(ps, 0.9f);
                s[(int)Kind.Smoke] = ps;
            }
            {
                var ps = Make("scintille", Mat(VfxTextures.Id.Spark, 5f, 1f, false, 0.3f, 0f, false, 3003), 900, ParticleSystemRenderMode.Stretch, 0.9f);
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (new Color(1f, 0.82f, 0.62f), 0.5f), (new Color(0.8f, 0.3f, 0.1f), 1f) }, new[] { (1f, 0f), (1f, 0.7f), (0f, 1f) }));
                SizeLife(ps, Curve((0f, 1f), (1f, 0.3f)));
                Bounce(ps, 0.35f, 0.5f, 0.25f);
                s[(int)Kind.Spark] = ps;
            }
            {
                var ps = Make("braci", Mat(VfxTextures.Id.Spark, 3f, 1f, false, 0.3f, 0f, false, 3003), 700, ParticleSystemRenderMode.Billboard, -0.03f);
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (new Color(1f, 0.65f, 0.45f), 0.6f), (new Color(0.6f, 0.2f, 0.1f), 1f) }, new[] { (0f, 0f), (1f, 0.1f), (1f, 0.7f), (0f, 1f) }));
                SizeLife(ps, Curve((0f, 1f), (1f, 0.4f)));
                Noise(ps, 0.35f, 1.6f, 0.8f);
                s[(int)Kind.Ember] = ps;
            }
            {
                var ps = Make("bagliori", Mat(VfxTextures.Id.Glow, 2.2f, 1f, false, 0.3f, 0.06f, false, 3001), 500);
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0.6f, 0.4f), (0f, 1f) }));
                SizeLife(ps, Curve((0f, 0.85f), (1f, 1.15f)));
                s[(int)Kind.Glow] = ps;
            }
            {
                var ps = Make("nebbia", Mat(VfxTextures.Id.Smoke, 1.5f, 0.55f, true, 0.5f, 0.25f, true, 3001), 700, ParticleSystemRenderMode.Billboard, 0f, Sheet, Sheet, false, true);
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.12f), (0.6f, 0.6f), (0f, 1f) }));
                SizeLife(ps, Curve((0f, 0.45f), (0.3f, 1f), (1f, 1.7f)));
                Spin(ps, 0.6f);
                Noise(ps, 0.3f, 1f, 0.8f);
                Drag(ps, 2.6f);
                s[(int)Kind.Mist] = ps;
            }
            {
                var ps = Make("schegge", Mat(VfxTextures.Id.Shard, 2.0f, 0.75f, false, 0.3f, 0f, false, 3002), 500, ParticleSystemRenderMode.Stretch, 0.5f);
                var r = ps.GetComponent<ParticleSystemRenderer>(); r.velocityScale = 0.018f; r.lengthScale = 2.4f;
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (1f, 0.7f), (0f, 1f) }));
                SizeLife(ps, Curve((0f, 1f), (1f, 0.6f)));
                Drag(ps, 1.6f);
                s[(int)Kind.Shard] = ps;
            }
            {
                var ps = Make("gocce", Mat(VfxTextures.Id.Drop, 1.1f, 0f, false, 0.3f, 0.02f, false, 3000), 700, ParticleSystemRenderMode.Stretch, 1f, 0, 0, false, true);
                var r = ps.GetComponent<ParticleSystemRenderer>(); r.velocityScale = 0.022f; r.lengthScale = 1.3f;
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (1f, 0.8f), (0f, 1f) }));
                Bounce(ps, 0f, 1f, 1f);
                s[(int)Kind.Drop] = ps;
            }
            {
                var ps = Make("rune", Mat(VfxTextures.Id.Runes, 3f, 1f, false, 0.3f, 0.05f, false, 3002), 300, ParticleSystemRenderMode.Billboard, 0f, VfxTextures.RuneCols, VfxTextures.RuneCols, true, false);
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.15f), (1f, 0.6f), (0f, 1f) }));
                SizeLife(ps, Curve((0f, 0.7f), (1f, 1.1f)));
                s[(int)Kind.Rune] = ps;
            }
            for (int ground = 0; ground < 2; ground++)
            {
                var ps = Make(ground == 1 ? "anelli_a_terra" : "anelli", Mat(VfxTextures.Id.Ring, 2.4f, 1f, false, 0.3f, ground == 1 ? 0.1f : 0.04f, false, 3002), 120,
                              ground == 1 ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard);
                ColorLife(ps, Grad(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0.7f, 0.4f), (0f, 1f) }));
                SizeLife(ps, Curve((0f, 0.12f), (0.3f, 0.78f), (1f, 1f)));
                s[ground == 1 ? (int)Kind.GroundRing : (int)Kind.Ring] = ps;
            }
            beamMat = Mat(VfxTextures.Id.Beam, 3.5f, 1f, false, 0.3f, 0f, false, 3003);
            ribbonMat = Mat(VfxTextures.Id.Ribbon, 0.9f, 0f, false, 0.3f, 0.02f, false, 3000);
            ribbonMat.SetFloat("_ScrollU", -2.2f);
            circleMat = Mat(VfxTextures.Id.Circle, 2.6f, 1f, false, 0.3f, 0.03f, false, 3002);
            sys = s;
        }

        static Material Mat(VfxTextures.Id id, float intensity, float additive, bool erosion, float edge, float soft, bool flipbook, int queue)
        {
            var m = new Material(shader) { name = "Magia_" + id };
            m.SetTexture("_MainTex", VfxTextures.Get(id));
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Additive", additive);
            m.SetFloat("_Erosion", erosion ? 1f : 0f);
            m.SetFloat("_EdgeSoft", edge);
            m.SetFloat("_SoftDist", soft);
            m.SetFloat("_Flipbook", flipbook ? 1f : 0f);
            m.renderQueue = queue;
            return m;
        }

        static ParticleSystem Make(string name, Material m, int max, ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard, float gravity = 0f,
                                   int tilesX = 0, int tilesY = 0, bool fixedFrame = false, bool sortByDistance = false)
        {
            var go = new GameObject("vfx_" + name);
            go.layer = 0;
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 5f; main.loop = true; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 0.1f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.sortMode = sortByDistance ? ParticleSystemSortMode.Distance : ParticleSystemSortMode.None;
            r.minParticleSize = 0f; r.maxParticleSize = 3f;
            r.alignment = ParticleSystemRenderSpace.View;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.03f; r.lengthScale = 1.5f; }
            var streams = new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
            if (tilesX > 0)
            {
                var ts = ps.textureSheetAnimation;
                ts.enabled = true;
                ts.mode = ParticleSystemAnimationMode.Grid;
                ts.numTilesX = tilesX; ts.numTilesY = tilesY;
                ts.animation = ParticleSystemAnimationType.WholeSheet;
                ts.cycleCount = 1;
                // le curve del modulo sono normalizzate: 0..1 = tutto il foglio
                if (fixedFrame) { ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f); ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f); }
                else
                {
                    ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                    ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f);
                    streams.Add(ParticleSystemVertexStream.UV2);
                    streams.Add(ParticleSystemVertexStream.AnimBlend);
                }
            }
            r.SetActiveVertexStreams(streams);
            ps.Play();
            return ps;
        }

        static Gradient Grad((Color c, float t)[] cols, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[cols.Length];
            for (int i = 0; i < cols.Length; i++) ck[i] = new GradientColorKey(cols[i].c, cols[i].t);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak);
            return g;
        }

        static AnimationCurve Curve(params (float t, float v)[] k)
        {
            var c = new AnimationCurve();
            foreach (var (t, v) in k) c.AddKey(t, v);
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        static void ColorLife(ParticleSystem ps, Gradient g) { var m = ps.colorOverLifetime; m.enabled = true; m.color = new ParticleSystem.MinMaxGradient(g); }
        static void SizeLife(ParticleSystem ps, AnimationCurve c) { var m = ps.sizeOverLifetime; m.enabled = true; m.size = new ParticleSystem.MinMaxCurve(1f, c); }
        static void Spin(ParticleSystem ps, float radPerSec) { var m = ps.rotationOverLifetime; m.enabled = true; m.z = new ParticleSystem.MinMaxCurve(-radPerSec, radPerSec); }

        static void Noise(ParticleSystem ps, float strength, float frequency, float scroll)
        {
            var n = ps.noise;
            n.enabled = true; n.strength = strength; n.frequency = frequency; n.scrollSpeed = scroll; n.damping = true;
            n.octaveCount = 2; n.quality = ParticleSystemNoiseQuality.Medium;
        }

        static void Drag(ParticleSystem ps, float drag)
        {
            var l = ps.limitVelocityOverLifetime;
            l.enabled = true; l.limit = 1000f; l.dampen = 0f; l.drag = drag;
            l.multiplyDragByParticleSize = false; l.multiplyDragByParticleVelocity = false;
        }

        static void Bounce(ParticleSystem ps, float bounce, float dampen, float lifeLoss)
        {
            var c = ps.collision;
            c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
            c.quality = ParticleSystemCollisionQuality.Low; c.collidesWith = HotelNav.EnvMask; c.enableDynamicColliders = false;
            c.bounce = bounce; c.dampen = dampen; c.lifetimeLoss = lifeLoss; c.radiusScale = 0.5f;
        }

        // ------------------------------------------------------------------ emissione
        public static void Emit(Kind k, Vector3 pos, Vector3 vel, float size, float life, Color col, float rotDeg = float.NaN)
        {
            Ensure();
            if (sys == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startSize = size, startLifetime = life, startColor = col, applyShapeToPosition = false,
                rotation = float.IsNaN(rotDeg) ? Random.Range(0f, 360f) : rotDeg,
            };
            sys[(int)k].Emit(ep, 1);
        }

        /// <summary>Una lingua di fuoco che sale lungo 'up' (scala 1 = una fiammella da mano, 5-7 cm).</summary>
        public static void Flame(Vector3 p, Vector3 up, float scale, Color tint)
        {
            bool big = scale >= 2f;
            float sq = Mathf.Sqrt(scale);
            Emit(big ? Kind.FireBig : Kind.Fire, p + Random.insideUnitSphere * 0.006f * scale,
                 up * Random.Range(0.22f, 0.5f) * sq + Random.insideUnitSphere * 0.07f * scale,
                 Random.Range(0.05f, 0.08f) * scale, Random.Range(0.26f, 0.42f) * (0.85f + 0.15f * sq), tint, Random.Range(-16f, 16f));
            if (Random.value < 0.07f)
                Emit(Kind.Ember, p, up * Random.Range(0.3f, 0.7f) * sq + Random.insideUnitSphere * 0.25f * sq, Random.Range(0.0025f, 0.005f) * sq,
                     Random.Range(0.6f, 1.3f), Color.Lerp(tint, MagicFx.Fire, 0.5f));
            if (Random.value < 0.035f * scale)
                Emit(Kind.Smoke, p + up * 0.05f * scale, up * 0.18f * sq + Random.insideUnitSphere * 0.04f, 0.05f * scale, Random.Range(0.9f, 1.5f),
                     new Color(0.10f, 0.09f, 0.085f, 0.32f));
        }

        /// <summary>L'esplosione: lampo, palla di fuoco che si gonfia e si consuma, fumo nero che sale, scintille che rimbalzano, braci
        /// che vagano e l'anello d'urto a terra. 'tint' colora il fuoco (bianco = fuoco vero).</summary>
        public static void Explosion(Vector3 p, Color c, float scale, Color tint)
        {
            float k = Mathf.Clamp01(scale);
            Emit(Kind.Glow, p, Vector3.zero, 1.7f * scale, 0.12f, Color.Lerp(c, Color.white, 0.5f));
            Emit(Kind.Glow, p, Vector3.zero, 1.0f * scale, 0.3f, new Color(c.r, c.g, c.b, 0.7f));
            int nf = Mathf.Max(4, Mathf.RoundToInt(26 * k));
            for (int i = 0; i < nf; i++)
            {
                Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 0.8f + 0.1f;
                Emit(Kind.FireBig, p + d * 0.12f * scale, d * Random.Range(2.2f, 5f) * scale + Vector3.up * 0.7f * scale,
                     Random.Range(0.42f, 0.85f) * scale, Random.Range(0.42f, 0.8f), tint, Random.Range(0f, 360f));
            }
            int ns = Mathf.Max(2, Mathf.RoundToInt(13 * k));
            for (int i = 0; i < ns; i++)
                Emit(Kind.Smoke, p + Random.insideUnitSphere * 0.35f * scale, Random.insideUnitSphere * 1.1f * scale + Vector3.up * Random.Range(0.5f, 1.1f) * scale,
                     Random.Range(0.7f, 1.2f) * scale, Random.Range(1.4f, 2.4f), new Color(0.075f, 0.065f, 0.06f, 0.7f));
            int nk = Mathf.Max(3, Mathf.RoundToInt(32 * k));
            for (int i = 0; i < nk; i++)
                Emit(Kind.Spark, p, Random.onUnitSphere * Random.Range(4f, 10f) * scale + Vector3.up * 2f * scale, Random.Range(0.012f, 0.022f) * Mathf.Max(0.4f, scale),
                     Random.Range(0.4f, 0.9f), Color.Lerp(c, Color.white, 0.5f));
            int ne = Mathf.Max(2, Mathf.RoundToInt(24 * k));
            for (int i = 0; i < ne; i++)
                Emit(Kind.Ember, p + Random.insideUnitSphere * 0.3f * scale, Random.insideUnitSphere * 2.2f * scale + Vector3.up * 1.3f * scale,
                     Random.Range(0.008f, 0.016f) * Mathf.Max(0.5f, scale), Random.Range(1f, 2.2f), c);
            if (scale >= 0.45f) Emit(Kind.GroundRing, p + Vector3.down * 0.3f * scale, Vector3.zero, 3.2f * scale, 0.42f, c, 0f);
        }

        // ------------------------------------------------------------------ fulmini
        sealed class Bolt
        {
            public LineRenderer core, glow;
            public readonly LineRenderer[] branch = new LineRenderer[3];
            public Vector3 a, b; public System.Func<Vector3> fromF, toF;
            public float life, age, width, jitter, regen; public int branches; public Color col; public bool alive;
        }

        static readonly List<Bolt> bolts = new List<Bolt>();
        static readonly AnimationCurve BoltWidth = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0.6f));
        static readonly List<Vector3> pts = new List<Vector3>(64);

        static LineRenderer Line(string name, Material m)
        {
            var go = new GameObject(name);
            go.layer = 0;
            go.transform.SetParent(root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = m;
            lr.useWorldSpace = true; lr.textureMode = LineTextureMode.Stretch; lr.alignment = LineAlignment.View;
            lr.numCapVertices = 0; lr.numCornerVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.lightProbeUsage = LightProbeUsage.Off; lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            lr.enabled = false;
            return lr;
        }

        /// <summary>Un fulmine da a a b per 'life' secondi: si ridisegna a scatti (ogni 35-55 ms) con lo stesso numero di rami.
        /// width = larghezza del cuore (l'alone e' 5 volte); from/to (facoltativi) lo tengono attaccato a punti che si muovono.</summary>
        public static void Lightning(Vector3 a, Vector3 b, Color c, float width, float life, int branches = 2, float jitter = 0.16f,
                                     System.Func<Vector3> from = null, System.Func<Vector3> to = null)
        {
            Ensure();
            if (sys == null) return;
            Bolt bt = null;
            foreach (var x in bolts) if (!x.alive) { bt = x; break; }
            if (bt == null)
            {
                if (bolts.Count >= 24) bt = bolts[0];
                else
                {
                    bt = new Bolt { core = Line("fulmine", beamMat), glow = Line("fulmine_alone", beamMat) };
                    for (int i = 0; i < bt.branch.Length; i++) bt.branch[i] = Line("fulmine_ramo", beamMat);
                    bolts.Add(bt);
                }
            }
            bt.a = a; bt.b = b; bt.fromF = from; bt.toF = to; bt.col = c; bt.width = width; bt.life = Mathf.Max(0.03f, life); bt.age = 0f;
            bt.jitter = jitter; bt.branches = Mathf.Clamp(branches, 0, bt.branch.Length); bt.regen = 0f; bt.alive = true;
            Shape(bt);
        }

        static void Jag(Vector3 a, Vector3 b, float jitter, int levels)
        {
            pts.Clear(); pts.Add(a); pts.Add(b);
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 n1 = Vector3.Cross(d, Vector3.up);
            if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.Cross(d, Vector3.right);
            n1.Normalize();
            Vector3 n2 = Vector3.Cross(d / len, n1);
            float off = len * jitter;
            for (int l = 0; l < levels; l++)
            {
                for (int i = pts.Count - 1; i > 0; i--)
                {
                    Vector3 m = (pts[i - 1] + pts[i]) * 0.5f + (n1 * Random.Range(-1f, 1f) + n2 * Random.Range(-1f, 1f)) * off;
                    pts.Insert(i, m);
                }
                off *= 0.52f;
            }
        }

        static void Shape(Bolt bt)
        {
            if (bt.fromF != null) bt.a = bt.fromF();
            if (bt.toF != null) bt.b = bt.toF();
            float len = (bt.b - bt.a).magnitude;
            int levels = len < 0.08f ? 3 : len < 0.5f ? 4 : len < 3f ? 5 : 6;
            Jag(bt.a, bt.b, bt.jitter, levels);
            var main = pts.ToArray();
            Set(bt.core, main, bt.width, 1f);
            Set(bt.glow, main, bt.width * 3.5f, 0.2f);
            for (int i = 0; i < bt.branch.Length; i++)
            {
                var lr = bt.branch[i];
                if (i >= bt.branches || main.Length < 5) { lr.enabled = false; continue; }
                int at = Random.Range(1, main.Length - 2);
                Vector3 s = main[at], fwd = (bt.b - bt.a).normalized;
                Vector3 dir = (Quaternion.AngleAxis(Random.Range(-50f, 50f), Random.onUnitSphere) * fwd).normalized;
                float bl = len * Random.Range(0.15f, 0.32f);
                Jag(s, s + dir * bl, bt.jitter * 1.3f, Mathf.Max(1, levels - 1));
                Set(lr, pts.ToArray(), bt.width * 0.55f, 0.75f);
            }
        }

        static void Set(LineRenderer lr, Vector3[] p, float w, float k)
        {
            lr.positionCount = p.Length;
            lr.SetPositions(p);
            lr.widthMultiplier = w;
            // sottile dove nasce (il dito, a pochi centimetri dall'occhio), pieno a meta', un po' piu' stretto dove colpisce
            lr.widthCurve = BoltWidth;
            lr.enabled = true;
            lr.startColor = lr.endColor = new Color(1f, 1f, 1f, k);
        }

        static void TickBolts(float dt)
        {
            foreach (var bt in bolts)
            {
                if (!bt.alive) continue;
                bt.age += dt;
                if (bt.age >= bt.life)
                {
                    bt.alive = false;
                    bt.core.enabled = bt.glow.enabled = false;
                    foreach (var br in bt.branch) br.enabled = false;
                    continue;
                }
                bt.regen -= dt;
                if (bt.regen <= 0f) { bt.regen = Random.Range(0.035f, 0.055f); Shape(bt); }
                float fade = 1f - bt.age / bt.life;
                float flick = 0.55f + 0.45f * Random.value;
                float a = Mathf.Clamp01(fade * 1.6f) * flick;
                Color hot = Color.Lerp(bt.col, Color.white, 0.75f), col = bt.col;
                Paint(bt.core, hot, a);
                Paint(bt.glow, col, a * 0.5f);
                foreach (var br in bt.branch) if (br.enabled) Paint(br, Color.Lerp(col, Color.white, 0.4f), a * 0.85f);
            }
        }

        static void Paint(LineRenderer lr, Color c, float a)
        {
            var g = new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
            lr.startColor = g; lr.endColor = new Color(g.r, g.g, g.b, g.a * 0.7f);
        }

        // ------------------------------------------------------------------ cerchio del Dodecaedro
        sealed class Circle
        {
            public GameObject go; public MeshRenderer mr; public Mesh mesh; public Color[] cols = new Color[4];
            public Vector3 c, n; public System.Func<Vector3> follow; public Vector3 offset;
            public float radius, life, age, spin, ang; public Color col; public bool alive;
        }

        static readonly List<Circle> circles = new List<Circle>();

        /// <summary>Il cerchio del Dodecaedro: si apre (da 60% a 100% della misura in 0,18 s) ruotando di 'spin' gradi al secondo,
        /// resta, e si spegne nell'ultimo 40% della vita. 'normal' = verso cui guarda; follow lo tiene attaccato a un punto che si muove.</summary>
        public static void MagicCircle(Vector3 c, Vector3 normal, float radius, Color col, float life, float spin, System.Func<Vector3> follow = null)
        {
            Ensure();
            if (sys == null) return;
            Circle ci = null;
            foreach (var x in circles) if (!x.alive) { ci = x; break; }
            if (ci == null)
            {
                if (circles.Count >= 8) ci = circles[0];
                else
                {
                    ci = new Circle { go = new GameObject("cerchio_dodecaedro") };
                    ci.go.transform.SetParent(root.transform, false);
                    ci.mesh = new Mesh { name = "cerchio" };
                    ci.mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                    ci.mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                    ci.mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                    ci.mesh.colors = ci.cols;
                    ci.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
                    ci.go.AddComponent<MeshFilter>().sharedMesh = ci.mesh;
                    ci.mr = ci.go.AddComponent<MeshRenderer>();
                    ci.mr.sharedMaterial = circleMat;
                    ci.mr.shadowCastingMode = ShadowCastingMode.Off; ci.mr.receiveShadows = false;
                    ci.mr.lightProbeUsage = LightProbeUsage.Off; ci.mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    circles.Add(ci);
                }
            }
            ci.c = c; ci.n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.forward; ci.follow = follow;
            ci.offset = follow != null ? c - follow() : Vector3.zero;
            ci.radius = radius; ci.life = Mathf.Max(0.1f, life); ci.age = 0f; ci.spin = spin; ci.ang = Random.Range(0f, 360f); ci.col = col; ci.alive = true;
            ci.go.SetActive(true);
            TickCircle(ci, 0f);
        }

        static void TickCircle(Circle ci, float dt)
        {
            ci.age += dt;
            if (ci.age >= ci.life) { ci.alive = false; ci.go.SetActive(false); return; }
            float t = ci.age / ci.life;
            float open = Mathf.Clamp01(ci.age / 0.18f);
            float s = ci.radius * 2f * Mathf.Lerp(0.6f, 1f, 1f - (1f - open) * (1f - open));
            float a = Mathf.Clamp01(ci.age / 0.08f) * (t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f);
            a *= 0.85f + 0.15f * Mathf.Sin(Time.time * 23f);
            ci.ang += ci.spin * dt;
            Vector3 c = ci.follow != null ? ci.follow() + ci.offset : ci.c;
            var tr = ci.go.transform;
            tr.SetPositionAndRotation(c, Quaternion.LookRotation(ci.n) * Quaternion.Euler(0f, 0f, ci.ang));
            tr.localScale = new Vector3(s, s, s);
            for (int i = 0; i < 4; i++) ci.cols[i] = new Color(ci.col.r, ci.col.g, ci.col.b, a);
            ci.mesh.colors = ci.cols;
        }

        // ------------------------------------------------------------------ la scia del dito e il nastro di sangue
        /// <summary>La scia luminosa di un dito che traccia un sigillo nell'aria: resta incisa 'time' secondi e sfuma.</summary>
        public sealed class Trail
        {
            readonly TrailRenderer tr;
            internal Trail(TrailRenderer t) { tr = t; }
            public void Begin(Vector3 p, Color c, float width, float time)
            {
                tr.transform.position = p;
                tr.Clear();
                tr.time = time; tr.widthMultiplier = width;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.Lerp(c, Color.white, 0.6f), 0f), new GradientColorKey(c, 0.25f), new GradientColorKey(c, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.emitting = true;
                tr.gameObject.SetActive(true);
            }
            public void Move(Vector3 p) { if (tr != null) tr.transform.position = p; }
            public void End() { if (tr != null) tr.emitting = false; }
        }

        static Trail trail;

        /// <summary>La scia del dito (una sola: una mano traccia un sigillo alla volta).</summary>
        public static Trail FingerTrail()
        {
            Ensure();
            if (sys == null) return null;
            if (trail != null) return trail;
            var go = new GameObject("scia_del_dito");
            go.transform.SetParent(root.transform, false);
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = beamMat;
            tr.minVertexDistance = 0.003f; tr.numCapVertices = 2; tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch; tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.6f);
            tr.emitting = false;
            return trail = new Trail(tr);
        }

        static LineRenderer streamRibbon, streamGlow;
        static float streamSeen;
        static readonly Vector3[] streamPts = new Vector3[28];
        static readonly AnimationCurve RibbonWidth = AnimationCurve.Linear(0f, 1f, 1f, 0.45f), RibbonGlowWidth = AnimationCurve.Linear(0f, 1f, 1f, 0.5f);

        /// <summary>Il filo di sangue dal nemico (a) al palmo (b): un nastro che scorre verso la mano lungo una curva che ondeggia, con
        /// l'alone; va chiamato a ogni fotogramma finche' dura (sparisce da solo quando non lo si chiama piu').</summary>
        public static void BloodStream(Vector3 a, Vector3 b, float t)
        {
            Ensure();
            if (sys == null) return;
            if (streamRibbon == null)
            {
                streamRibbon = Line("nastro_di_sangue", ribbonMat);
                streamRibbon.textureMode = LineTextureMode.Tile;
                streamGlow = Line("nastro_di_sangue_alone", beamMat);
            }
            const int N = 28;
            var p = streamPts;
            Vector3 d = b - a;
            float len = d.magnitude;
            Vector3 side = Vector3.Cross(d, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            Vector3 lift = Vector3.Cross(side, d).normalized;
            for (int i = 0; i < N; i++)
            {
                float u = (float)i / (N - 1);
                float env = Mathf.Sin(u * Mathf.PI);
                float w1 = Mathf.Sin(u * 9f - t * 7f) * 0.06f, w2 = Mathf.Sin(u * 5f + t * 4.3f) * 0.05f;
                p[i] = a + d * u + lift * (env * len * 0.12f) + (side * w1 + lift * w2) * env * Mathf.Min(1f, len * 0.3f);
            }
            streamRibbon.positionCount = N; streamRibbon.SetPositions(p);
            streamRibbon.widthCurve = RibbonWidth;
            streamRibbon.widthMultiplier = 0.03f;
            streamRibbon.startColor = new Color(0.30f, 0.01f, 0.01f, 1f); streamRibbon.endColor = new Color(0.42f, 0.02f, 0.02f, 1f);
            streamRibbon.material.mainTextureScale = new Vector2(Mathf.Max(1f, len * 2.2f), 1f);
            streamRibbon.enabled = true;
            streamGlow.positionCount = N; streamGlow.SetPositions(p);
            streamGlow.widthCurve = RibbonGlowWidth;
            streamGlow.widthMultiplier = 0.08f;
            streamGlow.startColor = streamGlow.endColor = new Color(MagicFx.Blood.r, MagicFx.Blood.g, MagicFx.Blood.b, 0.07f);
            streamGlow.enabled = true;
            streamSeen = Time.time;
            // gocce che corrono lungo il filo verso la mano
            for (int i = 0; i < 2; i++)
            {
                int k = Random.Range(1, N - 1);
                Vector3 tan = (p[k + 1] - p[k - 1]).normalized;
                Emit(Kind.Drop, p[k] + Random.insideUnitSphere * 0.012f, tan * Random.Range(3.5f, 6f), Random.Range(0.012f, 0.02f), 0.18f, new Color(0.5f, 0.02f, 0.02f, 1f));
            }
        }

        // ------------------------------------------------------------------ a ogni fotogramma
        internal static void Tick(float dt)
        {
            Shader.SetGlobalFloat("_PEMagicFog", !RenderSettings.fog ? 0f : RenderSettings.fogMode == FogMode.Linear ? 1f : RenderSettings.fogMode == FogMode.Exponential ? 2f : 3f);
            TickBolts(dt);
            foreach (var ci in circles) if (ci.alive) TickCircle(ci, dt);
            if (streamRibbon != null && streamRibbon.enabled && Time.time - streamSeen > 0.05f) { streamRibbon.enabled = false; streamGlow.enabled = false; }
        }
    }

    /// <summary>Muove fulmini, cerchi e nastri dopo che armi e mani sono al loro posto (LateUpdate tardivo).</summary>
    [DefaultExecutionOrder(300)]
    public sealed class MagicVfxDriver : MonoBehaviour
    {
        void LateUpdate() => MagicVfx.Tick(Time.deltaTime);
    }
}
