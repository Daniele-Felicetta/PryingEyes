using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    /// <summary>I diavoli di sabbia: nelle ore calde del giorno, con l'aria quasi ferma, una colonna di polvere che gira su se stessa nasce lontano (160-420 m, davanti a chi guarda), cammina
    /// storta sulla sabbia a passo d'uomo per un minuto e mezzo e si spegne. E' un solo sistema di particelle che segue un punto a terra (le particelle stanno nel sistema: salgono e girano attorno
    /// all'asse, la colonna si allarga con la quota). Non fa male e non spinge nessuno: serve a far sembrare vivo l'orizzonte. Se ne vede al piu' uno alla volta, a un buon tiro di schioppo.</summary>
    public sealed class DesertDevils : MonoBehaviour
    {
        public static DesertDevils I { get; private set; }

        const float MinDist = 160f, MaxDist = 420f, GoneDist = 760f;

        Transform player;
        /// <summary>La camera del giocatore attorno a cui nasce (schermo condiviso: una per posto); senza, quella principale.</summary>
        [System.NonSerialized] public Camera view;
        ParticleSystem ps;
        Material mat;
        System.Random rng = new System.Random(20261119);
        bool alive, forced;
        Vector3 pos;
        float heading, speed, age, life, hotK, nextT, siteT, strength;
        readonly System.Collections.Generic.List<Site> near = new System.Collections.Generic.List<Site>();

        /// <summary>C'e' un diavolo in giro (per le prove).</summary>
        public bool Active => alive;
        public Vector3 Position => pos;
        public int Particles => ps != null ? ps.particleCount : 0;

        public static DesertDevils Create(GameObject host, Transform followed)
        {
            var d = host.AddComponent<DesertDevils>();
            d.player = followed;
            return d;
        }

        float R() => (float)rng.NextDouble();

        /// <summary>Un altro posto dello schermo condiviso ha il suo: un seme diverso, perche' i diavoli dei due non nascano uguali.</summary>
        public void Reseed(int salt) { rng = new System.Random(20261119 + 7919 * salt); nextT = 25f + R() * 35f; }

        void Awake()
        {
            if (I == null) I = this;          // con piu' posti ce n'e' uno per ognuno: I e' il primo (il posto 0)
            mat = Fx.Unlit(Fx.PuffTex(), false);
            nextT = 25f + R() * 35f;
            Build();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (mat != null) Destroy(mat);
        }

        void Build()
        {
            var go = new GameObject("Diavolo di sabbia");
            go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.6f, 6.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startColor = new Color(0.82f, 0.64f, 0.42f, 0.30f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.283f);
            main.maxParticles = 300;
            main.gravityModifier = 0f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.0f; sh.radiusThickness = 0.35f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var em = ps.emission; em.rateOverTime = 0f;
            // sale a 6,5 m/s e gira attorno all'asse (2,6 radianti al secondo), allargandosi piano: una colonna di una trentina di metri (le particelle vivono cinque secondi e mezzo)
            var vo = ps.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.Local;
            vo.x = 0f; vo.y = 6.5f; vo.z = 0f;
            vo.orbitalX = 0f; vo.orbitalY = 2.6f; vo.orbitalZ = 0f;
            vo.radial = 0.7f;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.9f; noise.frequency = 0.22f; noise.scrollSpeed = 0.4f;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.45f, 1.1f), new Keyframe(1f, 2.5f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
        }

        /// <summary>Per le prove: un diavolo subito, qui, a piena forza e che non si spegne finche' non lo si toglie.</summary>
        public void ForceAt(Vector3 p)
        {
            alive = true; forced = true; pos = p; age = 8f; life = 1e6f; speed = 3.2f; heading = R() * 6.283f; hotK = 1f;
            ps.transform.position = pos;
        }

        /// <summary>Per le prove: lo toglie.</summary>
        public void Clear()
        {
            alive = false; forced = false; nextT = 30f + R() * 40f;
            if (ps != null) { var em = ps.emission; em.rateOverTime = 0f; }
        }

        void Update()
        {
            var sky = DesertSky.I;
            if (sky == null || player == null || ps == null) return;
            var st = sky.State; var wx = sky.Weather;
            // nelle ore col sole alto, con poco vento e senza tempesta; al chiuso non si cerca nemmeno
            bool hot = st.sunHeight > 0.42f && wx.storm < 0.25f && wx.wind < 0.75f && sky.IndoorFor(view) < 0.5f;
            float dt = Time.deltaTime;
            hotK = Mathf.MoveTowards(hotK, hot || forced ? 1f : 0f, dt * 0.25f);
            // il colore non e' illuminato: si accorda alla luce del giorno (e al sole basso diventa quello della luce)
            var main = ps.main;
            float day = Mathf.Lerp(0.35f, 1f, st.daylight);
            main.startColor = new Color(0.82f * day, 0.64f * day, 0.42f * day, 0.30f);

            if (!alive)
            {
                nextT -= dt;
                if (hot && nextT <= 0f && !TrySpawn()) nextT = 3f;
                return;
            }

            age += dt;
            // cammina storto: la direzione cambia piano con un rumore lento, e gira al largo dai luoghi
            heading += (Mathf.PerlinNoise(age * 0.11f, 3.7f) - 0.5f) * 1.2f * dt;
            siteT -= dt;
            if (siteT <= 0f)
            {
                siteT = 0.5f;
                DesertWorld.SitesNearKnown(pos.x + Mathf.Cos(heading) * 30f, pos.z + Mathf.Sin(heading) * 30f, 30f, near);
                if (near.Count > 0) heading += 0.9f;
            }
            pos += new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)) * (speed * dt);
            pos.y = DesertWorld.Height(pos.x, pos.z);
            ps.transform.position = pos;
            // nasce in sei secondi, si spegne in otto (o prima, se l'aria cambia); lontano si toglie
            strength = Mathf.Clamp01(age / 6f) * Mathf.Clamp01((life - age) / 8f) * hotK;
            var em = ps.emission; em.rateOverTime = 52f * strength;
            float dx = pos.x - player.position.x, dz = pos.z - player.position.z;
            if (!forced && (age > life || dx * dx + dz * dz > GoneDist * GoneDist || (hotK <= 0f && strength <= 0.01f)))
            {
                alive = false; em.rateOverTime = 0f; nextT = 30f + R() * 70f;
            }
        }

        bool TrySpawn()
        {
            var fwd = player.forward; fwd.y = 0f;
            float a0 = fwd.sqrMagnitude > 0.01f ? Mathf.Atan2(fwd.z, fwd.x) : R() * 6.283f;
            for (int k = 0; k < 6; k++)
            {
                float a = a0 + (R() - 0.5f) * 2.4f, d = MinDist + (MaxDist - MinDist) * R();
                float x = player.position.x + Mathf.Cos(a) * d, z = player.position.z + Mathf.Sin(a) * d;
                if (DesertWorld.DistToCathedral(x, z) < 150f) continue;
                DesertWorld.SitesNearKnown(x, z, 45f, near);
                if (near.Count > 0) continue;
                alive = true; forced = false; pos = new Vector3(x, DesertWorld.Height(x, z), z);
                heading = R() * 6.283f; speed = 2.4f + 2.2f * R(); age = 0f; life = 70f + 40f * R();
                ps.transform.position = pos;
                return true;
            }
            return false;
        }
    }
}
