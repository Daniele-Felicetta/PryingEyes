using System.Collections.Generic;
using Production.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    // Il caricatore vuoto che cade (2026-11-15). Nella ricarica a vuoto, appena il caricatore si sfila, una sua copia a scala reale
    // passa nel mondo (strato e luci del mondo, ombre) e cade con la fisica davanti ai piedi, rimbalza e fa rumore; quello vero
    // sparisce mentre la mano va alla cintura e ricompare quando torna con il nuovo. Nella ricarica tattica la mano porta il
    // caricatore alla cintura e torna con l'altro, senza buttarlo. Al piu' dieci caricatori a terra, ognuno per quindici secondi.
    public partial class Weapon
    {
        bool magDropped, magHidden;
        Renderer[] magRends;
        Transform magRendsOf;

        /// <summary>Dentro la ricarica di un'arma a caricatore (AnimateParts): butta il vuoto e nasconde quello vero finche' la mano
        /// non torna dalla cintura.</summary>
        void MagDropTick(float rp, WeaponDef d)
        {
            if (rp < 0.1f) magDropped = false;
            if (reloadEmpty && !magDropped && rp >= 0.13f)
            {
                magDropped = true;
                DropMag(d);
            }
            SetMagVisible(!(reloadEmpty && magDropped && rp >= 0.13f && rp < 0.36f));
        }

        /// <summary>Fuori dalla ricarica (cambio d'arma, ricarica interrotta): il caricatore torna visibile.</summary>
        void MagVisibleAgain()
        {
            if (magHidden) SetMagVisible(true);
        }

        void SetMagVisible(bool on)
        {
            if (rig == null || rig.mag == null) return;
            if (magRendsOf != rig.mag) { magRends = rig.mag.GetComponentsInChildren<Renderer>(true); magRendsOf = rig.mag; }
            if (magHidden == !on) return;
            magHidden = !on;
            foreach (var r in magRends) if (r != null) r.enabled = on;
        }

        void DropMag(WeaponDef d)
        {
            var src = rig != null ? rig.mag : null;
            if (src == null) return;
            var copy = Instantiate(src.gameObject, src.position, src.rotation);
            copy.name = "Caricatore vuoto";
            copy.transform.localScale = src.lossyScale;
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
            // la pelle del sigillo (WeaponAura) e' calcolata sull'arma in mano: il caricatore che cade non se la porta dietro
            foreach (var ov in copy.GetComponentsInChildren<MeshRenderer>(true)) if (ov != null && ov.name == WeaponAura.OverlayName) DestroyImmediate(ov.gameObject);
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            Bounds b = default;
            bool any = false;
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                r.renderingLayerMask = 1u;                         // le luci del mondo, non quella dell'arma in mano
                r.shadowCastingMode = ShadowCastingMode.On;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            if (!any) { Destroy(copy); return; }
            var bc = copy.AddComponent<BoxCollider>();
            bc.center = copy.transform.InverseTransformPoint(b.center);
            Vector3 ls = copy.transform.InverseTransformVector(b.size);
            bc.size = new Vector3(Mathf.Max(0.02f, Mathf.Abs(ls.x)), Mathf.Max(0.02f, Mathf.Abs(ls.y)), Mathf.Max(0.02f, Mathf.Abs(ls.z))) * 0.9f;
            var rb = copy.AddComponent<Rigidbody>();
            rb.mass = 0.35f;
            rb.angularDamping = 0.4f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var cc = GetComponent<CharacterController>();
            Vector3 carry = cc != null ? cc.velocity * 0.6f : Vector3.zero;
            Vector3 down = d.magUp ? cam.up * 0.4f : -cam.up * 0.8f;
            rb.linearVelocity = carry + down + cam.right * Random.Range(-0.15f, 0.1f);
            rb.angularVelocity = new Vector3(Random.Range(-5f, 5f), Random.Range(-2f, 2f), Random.Range(-4f, 4f));
            if (cc != null) Physics.IgnoreCollision(bc, cc);
            DroppedMag.Track(copy.AddComponent<DroppedMag>());
        }
    }

    /// <summary>Un caricatore vuoto a terra: rumore a ogni urto (piu' forte il primo), sparisce dopo quindici secondi; al piu' dieci.</summary>
    public sealed class DroppedMag : MonoBehaviour
    {
        const int Max = 10;
        static readonly Queue<DroppedMag> all = new Queue<DroppedMag>();
        float born, lastHit = -1f;
        int hits;
        public static int Count { get { while (all.Count > 0 && all.Peek() == null) all.Dequeue(); return all.Count; } }

        public static void Track(DroppedMag m)
        {
            m.born = Time.time;
            all.Enqueue(m);
            while (all.Count > Max) { var old = all.Dequeue(); if (old != null) Destroy(old.gameObject); }
        }

        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v < 0.6f || Time.time - lastHit < 0.06f || hits > 6) return;
            lastHit = Time.time;
            hits++;
            Sfx.At(SoundBank.Casing("big"), transform.position, Mathf.Clamp01(v / 4f) * (hits == 1 ? 0.9f : 0.5f), Random.Range(0.5f, 0.62f), 1.5f, 18f);
        }

        void Update()
        {
            if (Time.time - born > 15f) Destroy(gameObject);
        }
    }
}
