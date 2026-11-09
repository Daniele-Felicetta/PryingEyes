using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Suono d'ambiente dei piani: in chiesa un organo basso e sospeso e un'eco lunga di pietra, in albergo il ronzio
    /// della stanza vuota e un'eco corta; scricchiolii e, ogni tanto, un rintocco lontano. Volumi e riverbero seguono la stanza
    /// in cui si trova il giocatore, con dissolvenze lente.</summary>
    public class ProcAmbience : MonoBehaviour
    {
        static ProcAmbience I;
        AudioSource organ, room, wind;
        AudioReverbFilter rev;
        float creakT = 5f, tollT = 45f;
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
            organ = Mk(SoundBank.Organ);
            room = Mk(SoundBank.RoomTone);
            wind = Mk(SoundBank.Wind);
            tgtRev = curRev;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var fd = FloorDirector.I;
            Room r = fd != null ? fd.PlayerRoom : null;
            bool church = r != null && r.church && !r.IsLift;
            bool big = r != null && (r.kind == RoomKind.Navata || r.kind == RoomKind.Presbiterio || r.kind == RoomKind.Navatella);
            float orgT = church ? (big ? 0.36f : 0.2f) : 0.05f;
            float windT = r == null ? 0.1f : (big ? 0.26f : r.kind == RoomKind.Corridoio ? 0.07f : 0.12f);
            float roomT = church ? 0.12f : 0.22f;
            Blend(organ, orgT * GameSettings.Music, dt * 0.5f);
            Blend(room, roomT * GameSettings.Effects, dt * 0.8f);
            Blend(wind, windT * GameSettings.Effects, dt * 0.6f);

            // eco: pietra e volte alte in chiesa, stanze piccole in albergo
            if (r != null)
            {
                if (r.kind == RoomKind.Navata) tgtRev = new Vector4(3.6f, -800f, -1000f, -150f);
                else if (r.kind == RoomKind.Presbiterio) tgtRev = new Vector4(3.0f, -900f, -1100f, -250f);
                else if (r.kind == RoomKind.Cripta) tgtRev = new Vector4(2.6f, -900f, -1200f, -300f);
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
        }

        static void Blend(AudioSource s, float target, float k)
        {
            if (s != null) s.volume = Mathf.Lerp(s.volume, target, Mathf.Clamp01(k));
        }
    }
}
