using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Quello che si sente sulla sabbia oltre al vento di base (ProcAmbience, che qui segue il tempo): il sibilo dei granelli nelle raffiche e nella tempesta, i grilli dal crepuscolo
    /// (non nel vento forte), il gracchio degli avvoltoi che fanno la ruota di giorno, un ululato lontano di notte e il crepitio del fuoco di un campo quando ci si avvicina. Dentro i luoghi
    /// (DesertSky.Indoor) niente di tutto questo. I volumi seguono il cursore "Ambiente" e cambiano con dissolvenze lente.</summary>
    public sealed class DesertSound : MonoBehaviour
    {
        public static DesertSound I { get; private set; }

        Transform player;
        AudioSource sand, crickets, crackle;
        float cawT = 12f, howlT = 35f;

        // per le prove
        public float SandVolume => sand != null ? sand.volume : 0f;
        public float CricketVolume => crickets != null ? crickets.volume : 0f;
        public float CrackleVolume => crackle != null ? crackle.volume : 0f;
        public float CrackleDistance { get; private set; } = float.MaxValue;
        public int Caws { get; private set; }
        public int Howls { get; private set; }

        public static DesertSound Create(GameObject host, Transform followed)
        {
            var d = host.AddComponent<DesertSound>();
            d.player = followed;
            return d;
        }

        void Awake()
        {
            I = this;
            SoundBank.WarmDesert();
            sand = Loop("Sabbia", SoundBank.SandHiss, false);
            crickets = Loop("Grilli", SoundBank.Crickets, false);
            crackle = Loop("Crepitio", SoundBank.FireCrackle, true);
        }

        void OnDestroy() { if (I == this) I = null; }

        AudioSource Loop(string name, AudioClip clip, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.playOnAwake = false; s.volume = 0f;
            s.spatialBlend = spatial ? 1f : 0f;
            if (spatial) { s.minDistance = 1.5f; s.maxDistance = 34f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = 0f; }
            if (clip != null) { s.time = Random.value * clip.length * 0.9f; s.Play(); }
            return s;
        }

        static void Blend(AudioSource s, float target, float k)
        {
            if (s != null) s.volume = Mathf.Lerp(s.volume, target, Mathf.Clamp01(k));
        }

        void Update()
        {
            var sky = DesertSky.I;
            if (player == null || sky == null) return;
            float dt = Time.unscaledDeltaTime;
            var w = sky.Weather;
            float amb = GameSettings.Ambience;
            float outdoor = Mathf.Clamp01(1f - sky.Indoor * 1.6f);
            float night = sky.Night;

            // sabbia nel vento: appena un filo quando soffia forte, molto nella tempesta
            float hiss = Mathf.Max(0.16f * Mathf.SmoothStep(0f, 1f, (w.calmWind - 0.4f) / 0.4f), 0.75f * w.storm) * (0.8f + 0.2f * w.gust);
            Blend(sand, hiss * outdoor * amb, dt * 0.7f);
            // grilli: dal crepuscolo all'alba, meno col vento, nessuno nella tempesta
            Blend(crickets, 0.22f * night * (1f - w.storm) * (1f - 0.6f * w.wind) * outdoor * amb, dt * 0.5f);

            // crepitio del fuoco acceso piu' vicino
            float crackleT = 0f;
            var fires = DesertFires.I;
            if (fires != null && fires.NearestBurning(player.position, out var fp))
            {
                CrackleDistance = Vector3.Distance(fp, player.position);
                if (CrackleDistance < 40f)
                {
                    crackleT = 0.95f * fires.Lit;
                    crackle.transform.position = fp + Vector3.up * 0.6f;
                }
            }
            else CrackleDistance = float.MaxValue;
            Blend(crackle, crackleT * amb, dt * 1.2f);

            if (outdoor > 0.8f && sky.State.daylight > 0.3f && w.storm < 0.25f)
            {
                cawT -= Time.deltaTime;
                if (cawT <= 0f) { cawT = Random.Range(13f, 34f); PlayCaw(); }
            }
            if (outdoor > 0.8f && night > 0.8f && w.storm < 0.3f)
            {
                howlT -= Time.deltaTime;
                if (howlT <= 0f) { howlT = Random.Range(75f, 170f); PlayHowl(); }
            }
        }

        /// <summary>Un gracchio da un avvoltoio che fa la ruota entro 240 m, se ce n'e' uno (altrimenti non succede niente). Lo fanno anche le prove.</summary>
        public bool PlayCaw()
        {
            var life = DesertLife.I;
            if (life == null || player == null) return false;
            if (!life.NearestFlock(player.position, false, out var at, out float alt)) return false;
            var d = at - player.position; d.y = 0f;
            if (d.sqrMagnitude > 240f * 240f) return false;
            at += Vector3.up * alt;
            Sfx.At(SoundBank.Caw, at, 1f * GameSettings.Ambience, Random.Range(0.88f, 1.08f), 14f, 190f);
            Caws++;
            return true;
        }

        /// <summary>Un ululato da 120-210 m, in una direzione a caso.</summary>
        public bool PlayHowl()
        {
            if (player == null) return false;
            Vector2 o = Random.insideUnitCircle.normalized * Random.Range(120f, 210f);
            Sfx.At(SoundBank.Howl, player.position + new Vector3(o.x, 2f, o.y), 0.55f * GameSettings.Ambience, Random.Range(0.92f, 1.06f), 40f, 320f);
            Howls++;
            return true;
        }
    }
}
