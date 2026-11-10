using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Suono d'ambiente dei piani: in chiesa un organo basso e sospeso e un'eco lunga di pietra, in albergo il ronzio
    /// della stanza vuota e un'eco corta; scricchiolii e, ogni tanto, un rintocco lontano. Nel teatro un'orchestra lontana che
    /// accorda e il brusio di una sala piena (con applausi e campanello di sala ogni tanto); nel sanatorio il ronzio dei neon
    /// (con carrelli che cigolano, monitor e portoni di ferro lontani). Volumi e riverbero seguono la stanza in cui si trova il
    /// giocatore, con dissolvenze lente.</summary>
    public class ProcAmbience : MonoBehaviour
    {
        static ProcAmbience I;
        AudioSource organ, room, wind, pad, murmur, buzz;
        AudioReverbFilter rev;
        float creakT = 5f, tollT = 45f;
        float applauseT = 60f, bellT = 110f, squeakT = 22f, beepT = 40f, clangT = 55f;
        // riverbero attuale e bersaglio: decay, room, reflections, reverb
        Vector4 curRev = new Vector4(1.0f, -1500f, -1400f, -900f), tgtRev;

        public static void Create()
        {
            if (I != null) return;
            I = new GameObject("ProcAmbience").AddComponent<ProcAmbience>();
        }

        AudioSource Mk(AudioClip c)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = c; s.loop = true; s.spatialBlend = 0f; s.volume = 0f; s.playOnAwake = false;
            if (c != null) { s.time = Random.value * c.length * 0.9f; s.Play(); }
            return s;
        }

        void Start()
        {
            SoundBank.Warm();
            SoundBank.WarmThemes();
            organ = Mk(SoundBank.Organ);
            room = Mk(SoundBank.RoomTone);
            wind = Mk(SoundBank.Wind);
            pad = Mk(SoundBank.TheatreTone);
            murmur = Mk(SoundBank.Murmur);
            buzz = Mk(SoundBank.ClinicBuzz);
            tgtRev = curRev;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var fd = FloorDirector.I;
            Room r = fd != null ? fd.PlayerRoom : null;
            Theme theme = fd != null && fd.cur != null ? fd.cur.plan.theme : Theme.Albergo;
            bool church = r != null && r.church && !r.IsLift;
            bool stage = theme == Theme.Teatro && r != null && !r.church && !r.IsLift;        // la sala e il palcoscenico
            bool clinic = theme == Theme.Sanatorio && r != null && !r.church && !r.IsLift;    // l'istituto
            bool big = r != null && (r.kind == RoomKind.Navata || r.kind == RoomKind.Presbiterio || r.kind == RoomKind.Navatella);
            float orgT = church ? (big ? 0.36f : 0.2f) : (stage || clinic ? 0.01f : 0.05f);
            float windT = r == null ? 0.1f : (big ? 0.26f : r.kind == RoomKind.Corridoio ? 0.07f : 0.12f);
            float roomT = church ? 0.12f : stage ? 0.12f : clinic ? 0.10f : 0.22f;
            float padT = 0f, murT = 0f, buzzT = 0f;
            if (stage)
            {
                bool hall = r.kind == RoomKind.Platea || r.kind == RoomKind.Palcoscenico || r.kind == RoomKind.Foyer;
                padT = hall ? 0.34f : 0.14f;
                murT = r.kind == RoomKind.Platea ? 0.16f : r.kind == RoomKind.Foyer ? 0.12f : 0.05f;
            }
            else if (clinic)
                buzzT = r.kind == RoomKind.Corridoio ? 0.22f : (r.kind == RoomKind.SalaOperatoria || r.kind == RoomKind.Obitorio) ? 0.24f : 0.13f;
            Blend(organ, orgT * GameSettings.Music, dt * 0.5f);
            Blend(room, roomT * GameSettings.Effects, dt * 0.8f);
            Blend(wind, windT * GameSettings.Effects, dt * 0.6f);
            Blend(pad, padT * GameSettings.Music, dt * 0.5f);
            Blend(murmur, murT * GameSettings.Effects, dt * 0.6f);
            Blend(buzz, buzzT * GameSettings.Effects, dt * 0.8f);

            // eco: pietra e volte alte in chiesa, stanze piccole in albergo, grandi sale e piastrelle nei temi nuovi
            if (r != null)
            {
                if (r.kind == RoomKind.Navata) tgtRev = new Vector4(3.6f, -800f, -1000f, -150f);
                else if (r.kind == RoomKind.Presbiterio) tgtRev = new Vector4(3.0f, -900f, -1100f, -250f);
                else if (r.kind == RoomKind.Cripta) tgtRev = new Vector4(2.6f, -900f, -1200f, -300f);
                else if (stage) tgtRev = ThemeReverb(r.kind, true);
                else if (clinic) tgtRev = ThemeReverb(r.kind, false);
                else if (r.church) tgtRev = new Vector4(2.1f, -1000f, -1200f, -450f);
                else if (r.kind == RoomKind.Atrio || r.kind == RoomKind.SalaBallo || r.kind == RoomKind.Ristorante) tgtRev = new Vector4(1.6f, -1200f, -1300f, -600f);
                else tgtRev = new Vector4(0.9f, -1500f, -1400f, -900f);
            }
            curRev = Vector4.Lerp(curRev, tgtRev, dt * 0.9f);
            if (rev == null)
            {
                var l = FindFirstObjectByType<AudioListener>();
                if (l != null) { rev = l.GetComponent<AudioReverbFilter>(); if (rev == null) rev = l.gameObject.AddComponent<AudioReverbFilter>(); rev.reverbPreset = AudioReverbPreset.User; }
            }
            if (rev != null)
            {
                rev.decayTime = curRev.x; rev.room = curRev.y; rev.reflectionsLevel = curRev.z; rev.reverbLevel = curRev.w;
                rev.decayHFRatio = 0.55f; rev.reflectionsDelay = 0.02f; rev.reverbDelay = 0.035f; rev.diffusion = 100f; rev.density = 100f; rev.dryLevel = 0f;
            }

            var cam = Camera.main;
            if (cam == null) return;
            creakT -= Time.deltaTime;
            if (creakT <= 0f)
            {
                creakT = Random.Range(7f, 17f);
                Vector2 o = Random.insideUnitCircle.normalized * Random.Range(6f, 22f);
                Sfx.At(SoundBank.Creak, cam.transform.position + new Vector3(o.x, Random.Range(0.5f, 3f), o.y), Random.Range(0.25f, 0.5f), Random.Range(0.85f, 1.15f), 4f, 36f);
            }
            tollT -= Time.deltaTime;
            if (tollT <= 0f)
            {
                tollT = Random.Range(55f, 110f);
                Sfx.At(SoundBank.Toll, cam.transform.position + new Vector3(Random.Range(-1f, 1f), 0.2f, Random.Range(-1f, 1f)).normalized * 45f, 0.35f, 1f, 8f, 120f);
            }
            if (theme == Theme.Teatro)
            {
                applauseT -= Time.deltaTime; bellT -= Time.deltaTime;
                if (applauseT <= 0f) { applauseT = Random.Range(70f, 150f); Far(cam, SoundBank.Applause, 24f, 46f, 0.4f, 1f, 70f); }
                if (bellT <= 0f) { bellT = Random.Range(110f, 210f); Far(cam, SoundBank.TheatreBell, 12f, 30f, 0.5f, 1f, 50f); }
            }
            else if (theme == Theme.Sanatorio)
            {
                squeakT -= Time.deltaTime; beepT -= Time.deltaTime; clangT -= Time.deltaTime;
                if (squeakT <= 0f) { squeakT = Random.Range(24f, 55f); Far(cam, SoundBank.Trolley, 8f, 26f, 0.5f, Random.Range(0.9f, 1.1f), 40f); }
                if (beepT <= 0f) { beepT = Random.Range(45f, 95f); Far(cam, SoundBank.Monitor, 10f, 28f, 0.45f, 1f, 40f); }
                if (clangT <= 0f) { clangT = Random.Range(60f, 120f); Far(cam, SoundBank.Clang, 14f, 40f, 0.55f, Random.Range(0.9f, 1.05f), 60f); }
            }
        }

        /// <summary>Riverbero (decadimento, stanza, riflessioni, coda) delle stanze del teatro e del sanatorio.</summary>
        static Vector4 ThemeReverb(RoomKind k, bool theatre)
        {
            if (theatre)
            {
                if (k == RoomKind.Platea) return new Vector4(2.8f, -800f, -1000f, -250f);
                if (k == RoomKind.Palcoscenico) return new Vector4(3.2f, -850f, -1000f, -250f);
                if (k == RoomKind.Foyer) return new Vector4(2.0f, -1100f, -1250f, -500f);
                return new Vector4(1.0f, -1400f, -1400f, -800f);
            }
            if (k == RoomKind.SalaOperatoria || k == RoomKind.Obitorio || k == RoomKind.Lavanderia) return new Vector4(1.9f, -950f, -1050f, -380f);
            if (k == RoomKind.Refettorio || k == RoomKind.Accettazione) return new Vector4(1.7f, -1050f, -1150f, -450f);
            if (k == RoomKind.Corridoio) return new Vector4(1.5f, -1000f, -1100f, -420f);
            return new Vector4(1.1f, -1300f, -1300f, -700f);
        }

        /// <summary>Un suono lontano da una direzione a caso, a una distanza tra dmin e dmax.</summary>
        static void Far(Camera cam, AudioClip clip, float dmin, float dmax, float vol, float pitch, float maxDist)
        {
            Vector2 o = Random.insideUnitCircle.normalized * Random.Range(dmin, dmax);
            Sfx.At(clip, cam.transform.position + new Vector3(o.x, Random.Range(0.5f, 2.5f), o.y), vol, pitch, 6f, maxDist);
        }

        static void Blend(AudioSource s, float target, float k)
        {
            if (s != null) s.volume = Mathf.Lerp(s.volume, target, Mathf.Clamp01(k));
        }
    }
}
