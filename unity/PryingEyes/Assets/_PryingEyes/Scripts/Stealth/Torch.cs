using UnityEngine;

namespace PryingEyes
{
    /// <summary>La torcia del giocatore: un faro con ombre che segue lo sguardo con un filo di ritardo, e una pila che si scarica.
    /// T accende e spegne, Y cambia la pila (con una di scorta in tasca). A pila scarica la luce si fa arancione, si stringe e
    /// sfarfalla; a zero si spegne e, se c'e' una pila di scorta, la cambia da sola (un secondo e mezzo di buio). Sfarfalla
    /// anche quando un prete ti cerca da vicino. Accesa ti rende visibile da lontano (vedi Stealth.SightRange).</summary>
    public class Torch : MonoBehaviour
    {
        public static Torch I;
        public bool on;
        public float charge = 1f;
        public int spares = 1;
        public const int MaxSpares = 5;
        /// <summary>Secondi di luce di una pila nuova.</summary>
        public const float FullSeconds = 210f;
        public float drainMul = 1f;

        const float BeamIntensity = 44f, SpillIntensity = 1.5f;

        Transform camT, rig;
        Light beam, spill;
        Quaternion aim;
        float swapT, swapTotal = 1.5f, stutterT, stutterCool, lowWarnT;
        bool resumeOn, inited;
        static Texture2D cookie;

        public bool Lit => on && charge > 0f && swapT <= 0f;
        public bool Swapping => swapT > 0f;
        public float SwapProgress => swapT > 0f ? 1f - swapT / swapTotal : 0f;
        public Light Beam => beam;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public void Init(Transform cam)
        {
            if (inited) return;
            inited = true;
            camT = cam;
            rig = new GameObject("TorciaRig").transform;
            rig.position = cam.position;
            rig.rotation = cam.rotation;
            aim = cam.rotation;

            var bg = new GameObject("Fascio");
            bg.transform.SetParent(rig, false);
            beam = bg.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 36f;
            beam.spotAngle = 72f;
            beam.innerSpotAngle = 52f;
            beam.color = new Color(1f, 0.93f, 0.80f);
            beam.intensity = BeamIntensity;
            beam.shadows = LightShadows.Soft;
            beam.shadowStrength = 1f;
            beam.cookie = Cookie();
            beam.bounceIntensity = 0f;
            beam.enabled = false;

            // alone largo e debole: la luce che rimbalza intorno al fascio, senza cui fuori dal cono e' tutto nero pece
            var sg = new GameObject("Alone");
            sg.transform.SetParent(rig, false);
            spill = sg.AddComponent<Light>();
            spill.type = LightType.Spot;
            spill.range = 7f;
            spill.spotAngle = 125f;
            spill.innerSpotAngle = 60f;
            spill.color = new Color(1f, 0.90f, 0.74f);
            spill.intensity = SpillIntensity;
            spill.shadows = LightShadows.None;
            spill.bounceIntensity = 0f;
            spill.enabled = false;
        }

        /// <summary>Punto caldo al centro, bordo morbido, un anello debole di riflettore e un po' di granulosita': il fascio di una
        /// torcia vera non e' un disco uniforme.</summary>
        public static Texture2D Cookie()
        {
            if (cookie != null) return cookie;
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CookieTorcia" };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Lerp(1f, 0.55f, Stealth.Smooth(0f, 0.85f, r));
                    float edge = 1f - Stealth.Smooth(0.70f, 1.0f, r);
                    float rr = (r - 0.62f) / 0.06f, r2 = (r - 0.44f) / 0.05f;
                    float ring = 1f + 0.10f * Mathf.Exp(-rr * rr) - 0.12f * Mathf.Exp(-r2 * r2);
                    float n = 1f + 0.06f * (Mathf.PerlinNoise(u * 5f + 3f, v * 5f + 8f) - 0.5f);
                    float a = Mathf.Clamp01(core * edge * ring * n);
                    byte b = (byte)Mathf.RoundToInt(a * 255f);
                    px[y * S + x] = new Color32(b, b, b, b);
                }
            t.SetPixels32(px);
            t.Apply(false);
            return cookie = t;
        }

        // ------------------------------------------------------------------ comandi
        public void Toggle()
        {
            if (swapT > 0f) return;
            var hud = GameManager.I != null ? GameManager.I.hud : null;
            if (!on)
            {
                if (charge <= 0f)
                {
                    Sfx.UI(SoundBank.TorchOff, 0.25f, 0.8f);
                    if (spares > 0) StartSwap(true);
                    else hud?.Toast("La torcia è scarica. Servono pile.");
                    return;
                }
                on = true;
                Sfx.UI(SoundBank.TorchOn, 0.4f, Random.Range(0.97f, 1.03f));
            }
            else
            {
                on = false;
                Sfx.UI(SoundBank.TorchOff, 0.4f, Random.Range(0.97f, 1.03f));
            }
        }

        public void AddSpare()
        {
            spares = Mathf.Min(MaxSpares, spares + 1);
        }

        void ManualSwap()
        {
            if (swapT > 0f || spares <= 0 || charge > 0.8f) return;
            StartSwap(on);
        }

        void StartSwap(bool resume)
        {
            if (spares <= 0) return;
            spares--;
            resumeOn = resume;
            on = false;
            swapT = swapTotal = 1.5f;
            Sfx.UI(SoundBank.BatterySwap, 0.55f);
        }

        /// <summary>Alla rinascita: pila nuova, torcia spenta; le pile di scorta restano.</summary>
        public void OnRespawn()
        {
            on = false; charge = 1f; swapT = 0f; stutterT = 0f;
        }

        /// <summary>Inizio partita: pila piena e una di scorta, per far capire come funziona.</summary>
        public void NewRun()
        {
            on = false; charge = 1f; spares = 1; swapT = 0f; stutterT = 0f;
        }

        // ------------------------------------------------------------------ aggiornamento
        void Update()
        {
            if (!inited) return;
            var gm = GameManager.I;
            float dt = Time.deltaTime;
            bool alive = gm == null || gm.player == null || gm.player.alive;
            if (!GameMenu.Blocked && alive)
            {
                if (Input.GetKeyDown(KeyCode.T)) Toggle();
                if (Input.GetKeyDown(KeyCode.Y)) ManualSwap();
            }
            if (!alive && on) on = false;

            if (swapT > 0f)
            {
                swapT -= dt;
                if (swapT <= 0f)
                {
                    swapT = 0f;
                    charge = 1f;
                    on = resumeOn;
                    lowWarnT = 0f;
                    gm?.hud?.Toast($"Pila nuova. Scorte: {spares}");
                }
            }
            else if (on)
            {
                charge = Mathf.Max(0f, charge - dt / FullSeconds * drainMul);
                if (charge <= 0f)
                {
                    on = false;
                    if (spares > 0) StartSwap(true);
                    else
                    {
                        Sfx.UI(SoundBank.TorchBuzz, 0.4f);
                        gm?.hud?.Toast("La torcia si è spenta. Nessuna pila di scorta.");
                    }
                }
                else if (charge < 0.2f)
                {
                    lowWarnT -= dt;
                    if (lowWarnT <= 0f && gm != null && gm.hud != null)
                    {
                        lowWarnT = 20f;
                        gm.hud.Toast(spares > 0 ? "La pila sta morendo. Y per cambiarla." : "La pila sta morendo. Nessuna scorta.");
                    }
                }
            }

            // ----- luce
            float k = 0f;
            if (Lit)
            {
                float c = charge;
                k = c >= 0.30f ? 1f : Mathf.Lerp(0.36f, 1f, Mathf.Clamp01(c / 0.30f));
                // sfarfallio: pila quasi scarica e prete vicino
                stutterCool -= dt;
                float danger = Stealth.Level > 0.35f ? (Stealth.Level - 0.35f) / 0.65f : 0f;
                float lowK = c < 0.22f ? 1f - c / 0.22f : 0f;
                float rate = 1.5f * lowK * lowK + danger * 2.2f;
                if (stutterT > 0f) stutterT -= dt;
                else if (stutterCool <= 0f && Random.value < rate * dt)
                {
                    stutterT = Random.Range(0.04f, 0.10f + 0.25f * Mathf.Max(lowK, danger));
                    stutterCool = 0.12f;
                    if (Random.value < 0.45f) Sfx.UI(SoundBank.TorchBuzz, 0.14f + 0.2f * Mathf.Max(lowK, danger), Random.Range(0.9f, 1.1f));
                }
                k *= stutterT > 0f ? Random.Range(0.02f, 0.22f) : 1f + 0.025f * Mathf.Sin(Time.time * 37f);
            }
            bool lit = k > 0.004f;
            if (beam.enabled != lit) { beam.enabled = lit; spill.enabled = lit; }
            if (lit)
            {
                float warm = Mathf.Clamp01(charge / 0.45f);
                beam.intensity = BeamIntensity * k;
                beam.color = Color.Lerp(new Color(1f, 0.58f, 0.26f), new Color(1f, 0.93f, 0.80f), warm);
                float full = Mathf.Clamp01(charge / 0.4f);
                beam.range = Mathf.Lerp(17f, 36f, full);
                beam.spotAngle = Mathf.Lerp(56f, 72f, full);
                beam.innerSpotAngle = beam.spotAngle * 0.72f;
                spill.intensity = SpillIntensity * k;
                spill.color = beam.color;
            }
            gm?.hud?.SetTorch(on, charge, spares, swapT > 0f, SwapProgress);
        }

        void LateUpdate()
        {
            if (!inited || camT == null) return;
            float dt = Time.deltaTime;
            // il fascio resta un attimo indietro rispetto allo sguardo: la mano che regge la torcia non e' incollata alla testa
            aim = Quaternion.Slerp(aim, camT.rotation, 1f - Mathf.Exp(-26f * dt));
            float fwd = 0.25f;
            if (Physics.Raycast(camT.position, camT.forward, out RaycastHit h, 0.5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                fwd = Mathf.Clamp(h.distance - 0.12f, 0.04f, 0.25f);
            rig.rotation = aim;
            rig.position = camT.position + camT.right * 0.08f - camT.up * 0.05f + camT.forward * fwd;
        }
    }
}
