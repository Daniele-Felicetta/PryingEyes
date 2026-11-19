using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    /// <summary>La sabbia che vola. Quando il vento si fa forte (oltre 0,35) qualche striscia di granelli passa di traverso; nella tempesta (DesertWeather) ne passano centinaia al secondo e grandi
    /// nuvole di polvere rotolano sul terreno, tutte nella direzione del vento. Due sistemi di particelle nel mondo (non seguono la camera: la camera attraversa la sabbia) che partono da una
    /// scatola controvento al giocatore; al chiuso si fermano.</summary>
    public sealed class DesertDust : MonoBehaviour
    {
        public static DesertDust I { get; private set; }

        Transform player;
        ParticleSystem streaks, clouds;
        Material streakMat, cloudMat;

        /// <summary>Granelli (strisce) emessi al secondo in questo momento, per le prove.</summary>
        public float StreakRate { get; private set; }
        public float CloudRate { get; private set; }
        public int Alive => (streaks != null ? streaks.particleCount : 0) + (clouds != null ? clouds.particleCount : 0);

        public static DesertDust Create(GameObject host, Transform followed)
        {
            var d = host.AddComponent<DesertDust>();
            d.player = followed;
            return d;
        }

        /// <summary>Un passo sulla sabbia: due o tre sbuffi bassi color sabbia che salgono un poco e si allargano (di corsa piu' grandi e piu' veloci), scuri di notte, come ogni effetto non illuminato.
        /// Li chiama PlayerController a ogni passo sulla sabbia.</summary>
        public static void Footstep(Vector3 feet, float speed, bool sprint)
        {
            var sky = DesertSky.I;
            float day = sky != null ? Mathf.Lerp(0.16f, 1f, sky.State.daylight) * (1f - 0.5f * sky.Night) : 1f;
            float k = Mathf.Clamp01(speed / 8f);
            var tint = new Color(0.78f * day, 0.62f * day, 0.42f * day);
            int n = sprint ? 3 : 2;
            for (int i = 0; i < n; i++)
            {
                var vel = new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.22f, 0.5f), Random.Range(-0.35f, 0.35f)) * (0.6f + k);
                Fx.Particle(0, false, feet + new Vector3(Random.Range(-0.12f, 0.12f), 0.05f, Random.Range(-0.12f, 0.12f)), vel, Vector3.zero, 0.05f, 0.20f + 0.20f * k, 0.8f + 0.5f * k, tint, 0.24f);
            }
        }

        void Awake()
        {
            I = this;
            streakMat = Fx.Unlit(Fx.BeamTex(), false);
            cloudMat = Fx.Unlit(Fx.PuffTex(), false);
            streaks = Make("Striscie di sabbia", streakMat, true);
            clouds = Make("Nuvole di polvere", cloudMat, false);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (streakMat != null) Destroy(streakMat);
            if (cloudMat != null) Destroy(cloudMat);
        }

        ParticleSystem Make(string name, Material m, bool streak)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = streak ? new ParticleSystem.MinMaxCurve(1.2f, 2.4f) : new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, streak ? 3f : 1.5f);
            main.startSize = streak ? new ParticleSystem.MinMaxCurve(0.025f, 0.06f) : new ParticleSystem.MinMaxCurve(3.5f, 8f);
            main.startColor = streak ? new Color(0.80f, 0.62f, 0.40f, 0.20f) : new Color(0.85f, 0.68f, 0.45f, 0.07f);        // le strisce di sabbia: 0,42 le faceva trattini bianchi a sole radente
            main.maxParticles = streak ? 700 : 90;
            main.gravityModifier = 0f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = streak ? new Vector3(34f, 9f, 10f) : new Vector3(60f, 3f, 20f);
            var em = ps.emission; em.rateOverTime = 0f;
            var vo = ps.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.World;
            vo.x = 0f; vo.y = 0f; vo.z = 0f;
            var noise = ps.noise; noise.enabled = true; noise.strength = streak ? 0.6f : 1.2f; noise.frequency = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (!streak)
            {
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            if (streak) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 5f; r.cameraVelocityScale = 0f; }
            ps.Play();
            return ps;
        }

        void LateUpdate()
        {
            var sky = DesertSky.I;
            if (sky == null || player == null) return;
            var w = sky.Weather;
            float outdoor = 1f - sky.Indoor;
            var wd = new Vector3(w.dirX, 0f, w.dirZ);
            float speed = 3f + 26f * w.wind;
            Vector3 p = player.position;
            streaks.transform.position = p + new Vector3(-wd.x * 16f, 1.6f, -wd.z * 16f);          // (si muovono i due sistemi, mai l'oggetto che li porta: e' quello del regista)
            streaks.transform.rotation = Quaternion.LookRotation(wd, Vector3.up);
            StreakRate = (Mathf.Max(0f, w.wind - 0.35f) * 30f + w.storm * 320f) * outdoor;
            CloudRate = (w.storm * 20f + Mathf.Max(0f, w.wind - 0.6f) * 4f) * outdoor;
            Drive(streaks, StreakRate, wd * speed);
            Drive(clouds, CloudRate, wd * (speed * 0.7f));
            // le particelle sono Unlit: senza questo il loro colore pieno, all'esposizione del tramonto o della notte, e' bianco (trattini bianchi a sole radente)
            float lit = Mathf.Clamp(sky.LightLevel, 0.06f, 1f);
            var tint = new Color(lit, lit, lit, 1f);
            if (streakMat != null) streakMat.SetColor("_BaseColor", tint);
            if (cloudMat != null) cloudMat.SetColor("_BaseColor", tint);
            // le nuvole stanno basse, sul terreno; le strisce a tutte le quote davanti agli occhi
            clouds.transform.position = new Vector3(p.x - wd.x * 28f, p.y + 0.6f, p.z - wd.z * 28f);
            clouds.transform.rotation = Quaternion.LookRotation(wd, Vector3.up);
        }

        static void Drive(ParticleSystem ps, float rate, Vector3 v)
        {
            var em = ps.emission; em.rateOverTime = rate;
            var vo = ps.velocityOverLifetime; vo.x = v.x; vo.y = 0f; vo.z = v.z;
        }
    }
}
