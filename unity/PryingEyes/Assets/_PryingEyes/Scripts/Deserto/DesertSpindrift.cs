using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    /// <summary>La sabbia che il vento stacca dalle creste delle dune: veli sottili che partono dalla cresta e scorrono sottovento per qualche secondo, la firma di ogni deserto visto da lontano. Ogni
    /// secondo e mezzo, con un po' di vento, si cerca una cresta davanti al giocatore (la quota del terreno, funzione pura: un massimo lungo il vento, con la faccia sottovento piu' ripida, e piatta
    /// per traverso) e la si fa fumare per una decina di secondi; ne fumano al piu' sette alla volta. Un solo sistema di particelle con le emissioni a mano, non illuminato (si moltiplica per
    /// <see cref="DesertSky.LightLevel"/>, come la polvere); nella tempesta se ne occupa DesertDust.</summary>
    public sealed class DesertSpindrift : MonoBehaviour
    {
        public static DesertSpindrift I { get; private set; }

        struct Crest { public Vector3 pos; public Vector2 perp; public float life, acc, len; }

        const int MaxCrests = 7;
        /// <summary>Per le prove: moltiplica la frequenza con cui si cercano le creste (1 in gioco).</summary>
        public static float Boost = 1f;

        Transform player;
        /// <summary>La camera del giocatore attorno a cui nasce (schermo condiviso: una per posto); senza, quella principale.</summary>
        [System.NonSerialized] public Camera view;
        ParticleSystem ps;
        Material mat;
        Texture2D veil;
        readonly List<Crest> crests = new List<Crest>();
        float search;

        /// <summary>Quante creste hanno cominciato a fumare da quando c'e' il deserto, e quante fumano adesso.</summary>
        public int Plumes { get; private set; }
        public int Smoking => crests.Count;
        public int Alive => ps != null ? ps.particleCount : 0;

        public static DesertSpindrift Create(GameObject host, Transform followed)
        {
            var d = host.AddComponent<DesertSpindrift>();
            d.player = followed;
            return d;
        }

        /// <summary>Un velo: morbido per traverso e anche alle due estremita' (la striscia dei raggi, BeamTex, ha le punte nette e a bassa opacita' si leggeva come tanti mattoncini bianchi), con un po' di
        /// grana perche' due veli non siano uguali. L'asse lungo e' v, quello del moto con il rendering Stretch.</summary>
        static Texture2D MakeVeil()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Velo di sabbia" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float across = Mathf.Clamp01(1f - Mathf.Abs(u)); across *= across;
                    float along = Mathf.Clamp01(1f - Mathf.Abs(v)); along = along * Mathf.Sqrt(along);
                    float grain = 0.70f + 0.30f * Mathf.PerlinNoise(u * 3f + 5f, v * 3f + 1f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(255f * across * along * grain, 0f, 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        void Awake()
        {
            if (I == null) I = this;          // con piu' posti ce n'e' uno per ognuno: I e' il primo (il posto 0)
            veil = MakeVeil();
            mat = Fx.Unlit(veil, false);
            var go = new GameObject("Sabbia dalle creste");
            go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 3f; main.startSpeed = 0f; main.startSize = 0.5f; main.maxParticles = 1400; main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.6f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.12f; r.lengthScale = 2.2f; r.cameraVelocityScale = 0f;
            ps.Play();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (mat != null) Destroy(mat);
            if (veil != null) Destroy(veil);
        }

        void Update()
        {
            var sky = DesertSky.I;
            if (sky == null || player == null || ps == null) return;
            var w = sky.Weather;
            // la sabbia in aria e' sabbia illuminata: del colore della sabbia al sole (un po' piu' chiara), quindi dorata a mezzogiorno e arancio al tramonto, come quella a terra (una tinta
            // pallida sulla sabbia arancione si leggeva come bianca)
            float lit = Mathf.Clamp(sky.LightLevel, 0.06f, 1f);
            var sunLight = RenderSettings.sun;
            Color sc = sunLight != null && sunLight.enabled ? sunLight.color : new Color(0.7f, 0.78f, 1f);
            mat.SetColor("_BaseColor", new Color(0.98f * sc.r * lit, 0.74f * sc.g * lit, 0.44f * sc.b * lit, 1f));
            // all'aperto, con un po' di vento e senza tempesta (che ha le sue strisce)
            var p = player.position;
            bool on = (1f - sky.IndoorFor(view)) > 0.9f && w.wind > 0.10f && w.storm < 0.5f &&
                DesertBiomes.At(p.x, p.z, DesertWorld.Seed).Desert > 0.55f;
            float dt = Time.deltaTime;
            for (int i = crests.Count - 1; i >= 0; i--)
            {
                var c = crests[i];
                c.life -= dt; c.acc += dt;
                if (c.life <= 0f || !on) { crests.RemoveAt(i); continue; }
                while (c.acc >= 0.12f) { c.acc -= 0.12f; Emit(c, w); }
                crests[i] = c;
            }
            search -= dt;
            if (on && crests.Count < MaxCrests && search <= 0f) { search = 1.5f / Mathf.Max(0.1f, Boost); TryCrest(w); }
        }

        /// <summary>Tre granelli lungo la cresta: partono un poco sopra la sabbia e vanno sottovento, un filo in su.</summary>
        void Emit(Crest c, WeatherState w)
        {
            var wd3 = new Vector3(w.dirX, 0f, w.dirZ);
            float speed = 2.5f + 8f * w.wind + 3f * w.gust;
            var side = new Vector3(c.perp.x, 0f, c.perp.y);
            for (int i = 0; i < 3; i++)
            {
                var pos = c.pos + side * Random.Range(-c.len * 0.5f, c.len * 0.5f);
                pos.y = DesertWorld.Height(pos.x, pos.z) + Random.Range(0.10f, 0.50f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = pos,
                    velocity = wd3 * (speed * Random.Range(0.75f, 1.25f)) + new Vector3(0f, Random.Range(0.2f, 0.9f), 0f),
                    startLifetime = Random.Range(2.4f, 4.2f),
                    startSize = Random.Range(0.70f, 1.60f),
                    startColor = new Color(1f, 1f, 1f, Random.Range(0.07f, 0.15f)),
                };
                ps.Emit(ep, 1);
            }
        }

        /// <summary>Una cresta davanti al giocatore, tra 45 e 230 m. Le dune vere hanno la cresta stretta (una faccia lunga e dolce che sale, poi la caduta): cercare un massimo locale in un punto
        /// a caso ne trovava una ogni duecento (misurato su 200 punti di vista a caso del campo di dune: 3 % delle ricerche, 0 % in un decimo dei posti). Si cerca allora il punto piu' alto di una
        /// linea di sessanta metri lungo il vento passante per il punto provato, lo si accetta se sta nella parte di mezzo e se cinquanta centimetri piu' in basso a trenta metri di sopravvento e
        /// ottanta di sottovento (la faccia sottovento e' la ripida), e se per traverso a otto metri non differisce piu' di novanta (una cresta lunga, non un cocuzzolo): cosi' una ricerca di otto
        /// punti ne trova una nel 65 % dei casi (10 % dei posti: 44 %), e il passo tra le ricerche, un secondo e mezzo, la rende rara. Non si sceglie una cresta a meno di venti metri da una che gia' fuma.</summary>
        void TryCrest(WeatherState w)
        {
            var cam = view != null ? view : Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : player.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            var wd = new Vector2(w.dirX, w.dirZ);
            if (wd.sqrMagnitude < 1e-4f) return;
            wd.Normalize();
            var perp = new Vector2(-wd.y, wd.x);
            Vector3 p = player.position;
            for (int k = 0; k < 8; k++)
            {
                float a = Random.Range(-65f, 65f) * Mathf.Deg2Rad, dist = Random.Range(45f, 230f);
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float x = p.x + (fwd.x * ca - fwd.z * sa) * dist, z = p.z + (fwd.x * sa + fwd.z * ca) * dist;
                float best = float.MinValue; int bi = 0;
                for (int i = -15; i <= 15; i++)
                {
                    float h = DesertWorld.Height(x + wd.x * i * 2f, z + wd.y * i * 2f);
                    if (h > best) { best = h; bi = i; }
                }
                if (bi < -12 || bi > 12) continue;                  // il massimo e' in fondo alla linea: la cresta, se c'e', sta piu' in la'
                float cx = x + wd.x * bi * 2f, cz = z + wd.y * bi * 2f;
                float hu = DesertWorld.Height(cx - wd.x * 30f, cz - wd.y * 30f), hd = DesertWorld.Height(cx + wd.x * 30f, cz + wd.y * 30f);
                if (!(best > hu + 0.5f && best > hd + 0.8f)) continue;
                float s1 = DesertWorld.Height(cx + perp.x * 8f, cz + perp.y * 8f), s2 = DesertWorld.Height(cx - perp.x * 8f, cz - perp.y * 8f);
                if (Mathf.Abs(s1 - best) > 0.9f || Mathf.Abs(s2 - best) > 0.9f) continue;
                bool taken = false;
                for (int j = 0; j < crests.Count && !taken; j++)
                {
                    float dx = crests[j].pos.x - cx, dz = crests[j].pos.z - cz;
                    taken = dx * dx + dz * dz < 400f;
                }
                if (taken) continue;
                crests.Add(new Crest { pos = new Vector3(cx, best, cz), perp = perp, life = Random.Range(7f, 13f), len = Random.Range(12f, 26f) });
                Plumes++;
                return;
            }
        }
    }
}
