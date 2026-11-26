using PryingEyes.Deserto;
using UnityEngine;
using UnityEngine.Rendering;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>The rain of a race (PistaMode.Rain): what the eye sees of it. The grip is the circuit's business (Circuit.wet, the riders plan with it and the bikes feel it, all in pure code); here are the
    /// things drawn: a wet look on the ground (the global _PistaWet of the ground shader), streaks of rain round the camera, a spray behind every fast bike on the paved ground, and the grey sky and the thicker
    /// haze (PistaLook). Two particle systems in the world, made when a race with rain is set up and thrown away when the weather is clear again (or the mode ends); they follow the camera and the bikes in
    /// LateUpdate, after everything has moved.</summary>
    public sealed partial class PistaDirector
    {
        GameObject weatherRoot;
        ParticleSystem rainFx, sprayFx;
        Material rainMat, sprayMat;

        /// <summary>Streaks of rain emitted per second at rest and the most that is added by speed (the camera crosses the rain: at 280 km/h it meets about thirty times more of it than standing).</summary>
        public static float RainRate = 2300f, RainRateFast = 4000f;
        /// <summary>How hard it rains: the share of the streaks' length and alpha (the photographs try other values with -pistarainlevel).</summary>
        public static float RainAlpha = 0.42f;
        /// <summary>The look of the wet ground in the shader: how much it is darkened, how much smoother it is, and how much of the grey of the sky shines on it at a grazing angle (-pistawetdark, -pistawetgloss and -pistawetsheen).</summary>
        public static float WetDark = 0.30f, WetGloss = 0.45f, WetSheen = 0.45f;
        /// <summary>The colour of that sheen at full daylight: the pale grey-blue of a cloudy sky.</summary>
        public static Color SheenColour = new Color(0.62f, 0.67f, 0.74f, 1f);
        /// <summary>How much of the spray shows (the share of its alpha; -pistaspray in the photographs).</summary>
        public static float SprayGain = 1f;

        /// <summary>For the tests: the rain is falling, how many streaks and how many drops of spray are alive.</summary>
        public bool RainOn => weatherRoot != null;
        public int RainAlive => rainFx != null ? rainFx.particleCount : 0;
        public int SprayAlive => sprayFx != null ? sprayFx.particleCount : 0;

        /// <summary>The weather of the race being set up: rain made or thrown away, the ground wet or dry. Called wherever the sky is set (a friend's machine learns the weather from the roster).</summary>
        void WeatherSet(bool rain)
        {
            if (rain == (weatherRoot != null)) { Shader.SetGlobalFloat("_PistaWet", rain ? 1f : 0f); return; }
            if (!rain) { WeatherDestroy(); return; }
            RainAlpha = PistaLook.Arg("-pistarainalpha", RainAlpha); RainRate = PistaLook.Arg("-pistarainrate", RainRate); RainRateFast = PistaLook.Arg("-pistarainfast", RainRateFast); SprayGain = PistaLook.Arg("-pistaspray", SprayGain);
            WetDark = PistaLook.Arg("-pistawetdark", WetDark); WetGloss = PistaLook.Arg("-pistawetgloss", WetGloss); WetSheen = PistaLook.Arg("-pistawetsheen", WetSheen);
            weatherRoot = new GameObject("Pioggia");
            weatherRoot.transform.SetParent(transform, false);
            rainMat = Fx.Unlit(Fx.BeamTex(), false);
            sprayMat = Fx.Unlit(SprayTex(), false);
            rainFx = MakeSystem("Strisce di pioggia", rainMat, true);
            sprayFx = MakeSystem("Spruzzi", sprayMat, false);
            Shader.SetGlobalFloat("_PistaWet", 1f);
            Shader.SetGlobalVector("_PistaWetLook", new Vector4(WetDark, WetGloss, WetSheen, 0f));
        }

        static Texture2D sprayTex;

        /// <summary>A soft round puff of white mist, broken up by noise (the puff of the weapons' smoke is a warm dust colour: the spray was the colour of sand). Made once and kept, as the other textures of Fx are.</summary>
        static Texture2D SprayTex()
        {
            if (sprayTex != null) return sprayTex;
            const int T = 64;
            var tex = new Texture2D(T, T, TextureFormat.RGBA32, false) { name = "PistaSpruzzo", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[T * T];
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    float u = (x + 0.5f) / T * 2f - 1f, v = (y + 0.5f) / T * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = Mathf.PerlinNoise(u * 2.2f + 5f, v * 2.2f + 9f) * 0.6f + 0.4f;
                    float a = Mathf.Clamp01(1f - r); a = a * a * n;
                    px[y * T + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(a * 255f, 0f, 255f));
                }
            tex.SetPixels32(px); tex.Apply(false);
            return sprayTex = tex;
        }

        void WeatherDestroy()
        {
            if (weatherRoot != null) Destroy(weatherRoot);
            weatherRoot = null; rainFx = null; sprayFx = null;
            if (rainMat != null) Destroy(rainMat);
            if (sprayMat != null) Destroy(sprayMat);
            rainMat = sprayMat = null;
            Shader.SetGlobalFloat("_PistaWet", 0f);
            Shader.SetGlobalVector("_PistaWetLook", Vector4.zero);
            Shader.SetGlobalVector("_PistaWetSheen", Vector4.zero);
        }

        ParticleSystem MakeSystem(string name, Material m, bool streaks)
        {
            var go = new GameObject(name);
            go.transform.SetParent(weatherRoot.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = streaks ? new ParticleSystem.MinMaxCurve(0.7f, 1.1f) : new ParticleSystem.MinMaxCurve(0.55f, 1.0f);
            main.startSpeed = 0f;
            main.startSize = streaks ? new ParticleSystem.MinMaxCurve(0.012f, 0.026f) : new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
            main.startColor = streaks ? new Color(0.80f, 0.85f, 0.92f, RainAlpha) : new Color(0.84f, 0.87f, 0.91f, 0.16f);
            main.maxParticles = streaks ? 4200 : 1800;
            main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sh = ps.shape;
            if (streaks)
            {
                sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34f, 16f, 34f);
                var vo = ps.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.World; vo.x = 1.5f; vo.y = -14f; vo.z = 0.5f;
            }
            else
            {
                sh.enabled = false;
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.6f)));
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            if (streaks) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.02f; r.lengthScale = 8f; r.cameraVelocityScale = 0.02f; }
            ps.Play();
            return ps;
        }

        /// <summary>Every frame, once the camera and the bikes have moved: the rain round the camera (a box ahead of it, as big as the field of view needs, with more streaks the faster it goes), the spray
        /// behind the bikes that are fast on the paved ground, and the tint of the particles by the light of the day (they are not lit: at dusk their full colour would be white).</summary>
        void LateUpdate()
        {
            if (weatherRoot == null || cam == null || cam.cam == null) return;
            var ct = cam.cam.transform;
            float speed = myView != null ? Mathf.Max(0f, myView.shown.speed) : 0f;
            float fast = Mathf.Clamp01(speed / 70f);
            Vector3 fwd = ct.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            var rt = rainFx.transform;
            rt.position = ct.position + fwd * (11f + 8f * fast) + Vector3.up * 2.5f;
            var em = rainFx.emission; em.rateOverTime = Mathf.Lerp(RainRate, RainRateFast, fast);

            float lit = sky != null ? Mathf.Clamp(sky.LightLevel, 0.10f, 1f) : 1f;
            var tint = new Color(lit, lit, lit, 1f);
            if (rainMat != null) rainMat.SetColor("_BaseColor", tint);
            if (sprayMat != null) sprayMat.SetColor("_BaseColor", tint);
            Shader.SetGlobalVector("_PistaWetSheen", new Vector4(SheenColour.r * lit, SheenColour.g * lit, SheenColour.b * lit, 1f));

            // the spray: a little fan of drops from the rear wheel, backward and up, thicker and higher the faster the bike goes
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (v == null || v.rig == null || v.rig.root == null || !v.hasState) continue;
                var s = v.shown;
                if (s.crashed || s.speed < 14f) continue;
                if (s.surface != SurfaceKind.Asphalt && s.surface != SurfaceKind.Kerb && s.surface != SurfaceKind.Apron) continue;
                float k = Mathf.InverseLerp(14f, 75f, s.speed);
                float expect = Time.deltaTime * (50f + 190f * k);                      // drops this frame: the whole part and, by chance, the fraction
                int n = Mathf.Min(6, (int)expect + (Random.value < expect - (int)expect ? 1 : 0));
                var root = v.rig.root;
                for (int j = 0; j < n; j++)
                {
                    ep.position = root.position - root.forward * 0.66f + root.right * Random.Range(-0.10f, 0.10f) + Vector3.up * 0.10f;
                    ep.velocity = -root.forward * (s.speed * Random.Range(0.06f, 0.14f)) + Vector3.up * (0.9f + 2.2f * k * Random.value) + root.right * Random.Range(-0.7f, 0.7f);
                    ep.startSize = Random.Range(0.28f, 0.5f) * (0.7f + 0.6f * k);
                    ep.startLifetime = Random.Range(0.5f, 0.95f);
                    ep.startColor = new Color(0.84f, 0.87f, 0.91f, (0.07f + 0.13f * k) * SprayGain);
                    sprayFx.Emit(ep, 1);
                }
            }
        }
    }
}
