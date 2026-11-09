using System.Collections;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>L'ascensore centrale della parete ovest: si accende la freccia, "ding", le ante scorrono dentro il muro,
    /// la luce della cabina sfarfalla ed esce un monaco. Poi le porte si richiudono.</summary>
    public class Elevator : MonoBehaviour
    {
        public static Elevator I;
        public Transform doorL, doorR, spawn;
        public Light cabinLight;
        public Renderer indicator;
        public Color indicatorOn = new Color(1f, 0.5f, 0.12f) * 4f;
        const float Slide = 1.15f;         // ogni anta rientra nel muro della sua larghezza

        Vector3 lClosed, rClosed;
        Material indMat;
        float lightBase;
        bool busy;

        void Awake() { I = this; }

        void Start()
        {
            if (doorL != null) lClosed = doorL.position;
            if (doorR != null) rClosed = doorR.position;
            if (indicator != null)
            {
                indMat = new Material(indicator.sharedMaterial);
                indicator.sharedMaterial = indMat;
                indMat.SetColor("_EmissionColor", Color.black);
            }
            if (cabinLight != null) { lightBase = cabinLight.intensity; cabinLight.enabled = false; }
        }

        /// <summary>Fa arrivare l'ascensore; onOpen parte quando le ante sono a meta' (li' nasce il monaco).
        /// Falso se l'ascensore sta gia' lavorando.</summary>
        public bool Arrive(System.Action onOpen)
        {
            if (busy || doorL == null || doorR == null) return false;
            StartCoroutine(Cycle(onOpen));
            return true;
        }

        IEnumerator Cycle(System.Action onOpen)
        {
            busy = true;
            Vector3 door = (lClosed + rClosed) * 0.5f;
            if (indMat != null) indMat.SetColor("_EmissionColor", indicatorOn);
            Sfx.At(SoundBank.Ding, door + Vector3.up * 1.4f, 0.75f, 1f, 3f, 45f);
            yield return new WaitForSeconds(0.9f);
            if (cabinLight != null) cabinLight.enabled = true;
            Sfx.At(SoundBank.Doors, door, 0.6f, 1f, 2.5f, 30f);
            bool spawned = false;
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 1.3f);
                SetDoors(k);
                Buzz();
                if (!spawned && k > 0.45f) { spawned = true; onOpen?.Invoke(); }
                yield return null;
            }
            SetDoors(1f);
            if (!spawned) onOpen?.Invoke();
            if (GameManager.autoTest) { Debug.Log("[PryingEyes] ascensore aperto"); GameManager.I?.OnLiftOpen(); }
            for (float t = 0f; t < 4.5f; t += Time.deltaTime) { Buzz(); yield return null; }
            Sfx.At(SoundBank.Doors, door, 0.5f, 0.94f, 2.5f, 30f);
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                SetDoors(1f - Mathf.SmoothStep(0f, 1f, t / 1.3f));
                yield return null;
            }
            SetDoors(0f);
            if (cabinLight != null) cabinLight.enabled = false;
            if (indMat != null) indMat.SetColor("_EmissionColor", Color.black);
            busy = false;
        }

        void SetDoors(float k)
        {
            doorL.position = lClosed + Vector3.forward * (Slide * k);
            doorR.position = rClosed + Vector3.back * (Slide * k);
        }

        /// <summary>Neon stanco: ronzio di luce con qualche calo improvviso.</summary>
        void Buzz()
        {
            if (cabinLight == null) return;
            float n = Mathf.PerlinNoise(Time.time * 9f, 0.7f);
            cabinLight.intensity = lightBase * (Random.value < 0.03f ? 0.15f : 0.8f + 0.25f * n);
        }

        public bool Busy => busy;
    }
}
