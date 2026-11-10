using UnityEngine;
using System.Collections.Generic;

namespace PryingEyes
{
    /// <summary>Il modello 3D di un'arma (prefab Resources/Armi/&lt;id&gt;): trova per nome gli agganci (muzzle, ads, eject, grip_l)
    /// e le parti mobili (caricatore, carrello, pompa, tamburo...) e le muove da codice. Gli spostamenti sono nel riferimento
    /// dell'arma: +z avanti, +y su, +x destra.</summary>
    public class WeaponRig : MonoBehaviour
    {
        public Transform muzzle, ads, eject, gripL;
        public Transform mag, charge, slide, bolt, boltHandle, pump, cyl, lever, hammer, trigger, cover, belt, barrels, recip;
        public Transform[] lenses;
        public Renderer[] renderers;
        public Transform scopeLens;            // lente oculare di un'ottica ingrandita
        public Transform reticle;              // disco del reticolo collimato
        Dictionary<Transform, Vector3> restPos = new Dictionary<Transform, Vector3>();
        Dictionary<Transform, Quaternion> restRot = new Dictionary<Transform, Quaternion>();
        bool ready;

        void Awake() { Setup(); }

        public void Setup()
        {
            if (ready) return;
            ready = true;
            var all = GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in all) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            Transform G(string n) => byName.TryGetValue(n, out var t) ? t : null;
            muzzle = G("muzzle"); ads = G("ads"); eject = G("eject"); gripL = G("grip_l");
            mag = G("caricatore"); charge = G("charge"); slide = G("slide"); bolt = G("bolt"); boltHandle = G("bolt_handle");
            pump = G("pump"); cyl = G("cyl"); lever = G("lever"); hammer = G("hammer"); trigger = G("trigger");
            cover = G("cover"); belt = G("belt"); barrels = G("barrels"); recip = G("recip");
            scopeLens = G("lente_oculare"); reticle = G("retino");
            var lens = new List<Transform>();
            foreach (var t in all) if (t.name.StartsWith("lente_")) lens.Add(t);
            lenses = lens.ToArray();
            renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var t in new[] { mag, charge, slide, bolt, boltHandle, pump, cyl, lever, hammer, trigger, cover, belt, barrels, recip })
                if (t != null) { restPos[t] = t.localPosition; restRot[t] = t.localRotation; }
        }

        /// <summary>Posizione di riposo di una parte nel riferimento locale del suo genitore.</summary>
        public Vector3 Rest(Transform t) => t != null && restPos.TryGetValue(t, out var p) ? p : Vector3.zero;

        /// <summary>Sposta una parte dalla posizione di riposo (offset nel riferimento dell'arma, +z avanti).</summary>
        public void Offset(Transform t, Vector3 off)
        {
            if (t == null || !restPos.ContainsKey(t)) return;
            t.localPosition = restPos[t] + (t.parent != null ? t.parent.InverseTransformVector(transform.TransformVector(off)) : off);
        }

        /// <summary>Ruota una parte attorno al suo perno rispetto al riposo (angoli nel riferimento dell'arma).</summary>
        public void Rotate(Transform t, Vector3 euler)
        {
            if (t == null || !restRot.ContainsKey(t)) return;
            Quaternion q = Quaternion.Euler(euler);
            // la rotazione e' nel riferimento dell'arma: la porto nel riferimento del genitore
            Quaternion toParent = t.parent != null ? Quaternion.Inverse(t.parent.rotation) * transform.rotation : Quaternion.identity;
            t.localRotation = (toParent * q * Quaternion.Inverse(toParent)) * restRot[t];
        }

        public void Reset()
        {
            foreach (var kv in restPos) if (kv.Key != null) kv.Key.localPosition = kv.Value;
            foreach (var kv in restRot) if (kv.Key != null) kv.Key.localRotation = kv.Value;
        }

        public Vector3 AdsLocal => ads != null ? transform.InverseTransformPoint(ads.position) : new Vector3(0f, 0.07f, 0.15f);
        public Vector3 MuzzleLocal => muzzle != null ? transform.InverseTransformPoint(muzzle.position) : new Vector3(0f, 0.02f, 0.6f);
    }
}
