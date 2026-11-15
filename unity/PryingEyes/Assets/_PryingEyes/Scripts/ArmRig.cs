using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Braccio in prima persona con il gomito vero (modelli/hotel/genera_braccia.py). La manica dell'FBX diventa una
    /// SkinnedMeshRenderer su tre ossa (mano, avambraccio, braccio) e a ogni fotogramma una cinematica inversa a due ossa porta il
    /// gomito dove deve stare perche' l'avambraccio arrivi al polso. La spalla resta ferma nel riferimento del modello in prima persona:
    /// a riposo il braccio e' esattamente quello modellato, e si piega solo quando la mano si sposta rispetto all'arma (ricarica,
    /// pompa, otturatore, mano sinistra che va al caricatore). Il guanto resta rigido con la mano (figlio di mano_X).
    /// Senza i giunti polso_X / gomito_X / spalla_X (mani vecchie) Build torna null e la manica resta rigida con la mano, come prima.
    /// Funziona anche in edit-mode (ArmiShots).</summary>
    public sealed class ArmRig
    {
        readonly Transform root, hand, foreBone, upperBone;
        readonly Vector3 shoulder, wristInHand, poleRest, normalRest;
        readonly float lu, lf;
        public readonly SkinnedMeshRenderer Skin;
        // spalla attaccata al corpo (alla camera) invece che all'arma: in mira, al rinculo, in corsa e al cambio d'arma i gomiti
        // si piegano invece di muoversi rigidi con l'arma
        Transform body;
        Vector3 shoulderBody;

        /// <summary>In mira la spalla scende di tanto (metri, riferimento del corpo): l'avambraccio destro, che in mira passa a 15-30 cm
        /// dall'occhio, esce dal basso invece di riempire mezzo schermo.</summary>
        public float AdsDrop;

        /// <summary>Spalla a riposo nel riferimento della radice delle mani (= quello dell'arma).</summary>
        public Vector3 ShoulderLocal => shoulder;

        /// <summary>Da qui in poi la spalla segue il riferimento 'body' (la camera) nel punto shoulderInBody, di lato e in altezza
        /// (la profondita' resta quella dell'arma): Weapon lo calcola dalla posa al fianco, cosi' a riposo il braccio resta
        /// esattamente quello modellato.</summary>
        public void AnchorTo(Transform bodyT, Vector3 shoulderInBody) { body = bodyT; shoulderBody = shoulderInBody; }

        static readonly Dictionary<Mesh, Mesh> skinned = new Dictionary<Mesh, Mesh>();

        ArmRig(Transform root, Transform hand, Transform fore, Transform upper, Vector3 shoulder, Vector3 wristInHand,
               Vector3 poleRest, Vector3 normalRest, float lu, float lf, SkinnedMeshRenderer skin)
        {
            this.root = root; this.hand = hand; foreBone = fore; upperBone = upper; this.shoulder = shoulder;
            this.wristInHand = wristInHand; this.poleRest = poleRest; this.normalRest = normalRest; this.lu = lu; this.lf = lf; Skin = skin;
        }

        static Transform Child(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>root = radice delle mani (riferimento dell'arma), hand = Empty mano_d / mano_s, side = "d" / "s".</summary>
        public static ArmRig Build(Transform root, Transform hand, string side)
        {
            if (root == null || hand == null) return null;
            Transform wj = Child(hand, "polso_" + side), ej = Child(hand, "gomito_" + side), sj = Child(hand, "spalla_" + side);
            if (wj == null || ej == null || sj == null) return null;
            MeshFilter sleeve = null;
            foreach (var mf in hand.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.EndsWith("_manica")) { sleeve = mf; break; }
            if (sleeve == null || sleeve.sharedMesh == null) return null;
            var src = sleeve.sharedMesh;
            if (!src.isReadable) { Debug.LogWarning("[PryingEyes] braccio: manica non leggibile, resta rigida: " + src.name); return null; }

            Vector3 W0 = root.InverseTransformPoint(wj.position), E0 = root.InverseTransformPoint(ej.position), S0 = root.InverseTransformPoint(sj.position);
            float lf = (E0 - W0).magnitude, lu = (S0 - E0).magnitude;
            if (lf < 0.05f || lu < 0.05f) return null;
            Vector3 n0 = Vector3.Cross(E0 - S0, W0 - E0);
            if (n0.sqrMagnitude < 1e-10f) n0 = Vector3.Cross(E0 - S0, Vector3.up);
            n0.Normalize();
            Vector3 dir0 = (W0 - S0).normalized;
            Vector3 pole = E0 - (S0 + dir0 * Vector3.Dot(E0 - S0, dir0));
            pole = pole.sqrMagnitude > 1e-10f ? pole.normalized : n0;

            int layer = sleeve.gameObject.layer;
            var hb = new GameObject("osso_mano_" + side).transform;
            hb.SetParent(hand, false);
            hb.position = wj.position;
            hb.localRotation = Quaternion.identity;
            var fb = new GameObject("osso_avambraccio_" + side).transform;
            fb.SetParent(root, false);
            fb.localPosition = E0;
            fb.localRotation = Quaternion.LookRotation(W0 - E0, n0);
            var ub = new GameObject("osso_braccio_" + side).transform;
            ub.SetParent(root, false);
            ub.localPosition = S0;
            ub.localRotation = Quaternion.LookRotation(E0 - S0, n0);
            foreach (var t in new[] { hb, fb, ub }) t.gameObject.layer = layer;

            // la pelle: copia della mesh con pesi e pose di legame, una volta per mesh (cambiare arma non la ricalcola)
            var go = new GameObject(sleeve.name + "_pelle");
            go.layer = layer;
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(sleeve.transform.position, sleeve.transform.rotation);
            go.transform.localScale = Vector3.one;
            Vector3 ls = sleeve.transform.lossyScale, ps = go.transform.lossyScale;
            go.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
            var bones = new[] { hb, fb, ub };
            if (!skinned.TryGetValue(src, out var mesh) || mesh == null)
            {
                mesh = Object.Instantiate(src);
                mesh.name = src.name + "_pelle";
                var toRoot = root.worldToLocalMatrix * sleeve.transform.localToWorldMatrix;
                var vs = src.vertices;
                var bw = new BoneWeight[vs.Length];
                for (int i = 0; i < vs.Length; i++)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(vs[i]);
                    float t1 = Mathf.Clamp01(Vector3.Dot(p - W0, E0 - W0) / (lf * lf));
                    float d1 = (p - (W0 + (E0 - W0) * t1)).sqrMagnitude;
                    float t2 = Mathf.Clamp01(Vector3.Dot(p - E0, S0 - E0) / (lu * lu));
                    float d2 = (p - (E0 + (S0 - E0) * t2)).sqrMagnitude;
                    float s = d1 <= d2 ? t1 * lf : lf + t2 * lu;
                    // il polsino e le pieghe sopra la manichetta seguono la mano; al gomito avambraccio e braccio si mescolano
                    float wh = 1f - Smooth(0.030f, 0.090f, s);
                    float u = Smooth(lf - 0.055f, lf + 0.055f, s);
                    float wu = (1f - wh) * u, wf = (1f - wh) * (1f - u);
                    bw[i] = new BoneWeight { boneIndex0 = 0, weight0 = wh, boneIndex1 = 1, weight1 = wf, boneIndex2 = 2, weight2 = wu };
                }
                mesh.boneWeights = bw;
                skinned[src] = mesh;
            }
            var bind = new Matrix4x4[3];
            for (int b = 0; b < 3; b++) bind[b] = bones[b].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = fb;
            smr.quality = SkinQuality.Bone4;
            smr.updateWhenOffscreen = true;
            var mr = sleeve.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                smr.sharedMaterials = mr.sharedMaterials;
                smr.shadowCastingMode = mr.shadowCastingMode;
                smr.receiveShadows = mr.receiveShadows;
                smr.renderingLayerMask = mr.renderingLayerMask;
            }
            else smr.shadowCastingMode = ShadowCastingMode.Off;
            sleeve.gameObject.SetActive(false);
            Vector3 wih = hand.InverseTransformPoint(wj.position);
            return new ArmRig(root, hand, fb, ub, S0, wih, pole, n0, lu, lf, smr);
        }

        /// <summary>Gomito a due ossa verso il polso attuale (riferimento della radice delle mani). Va chiamata dopo che la mano e'
        /// stata messa al suo posto nel fotogramma (Weapon.AnimateParts).</summary>
        public void Solve(float ads = 0f)
        {
            if (hand == null || foreBone == null) return;
            Vector3 W = root.InverseTransformPoint(hand.TransformPoint(wristInHand));
            Vector3 S = shoulder;
            if (body != null)
            {
                // di lato e in altezza la spalla sta col corpo; in profondita' resta con l'arma: in mira l'arma viene a 30 cm
                // dall'occhio (al fianco sta a 60) e con la spalla ferma il braccio virtuale, lungo per la posa al fianco, si
                // ripiegherebbe col gomito davanti alla mano
                Vector3 sb = root.InverseTransformPoint(body.TransformPoint(shoulderBody + Vector3.down * (AdsDrop * Mathf.Clamp01(ads))));
                S = new Vector3(sb.x, sb.y, shoulder.z);
            }
            Vector3 sw = W - S;
            float d = sw.magnitude;
            if (d < 1e-5f) return;
            Vector3 dir = sw / d;
            // fuori portata: la spalla (fuori campo) scivola verso la mano invece di staccare il polso
            float dmax = (lu + lf) * 0.999f, dmin = Mathf.Abs(lu - lf) + 0.01f;
            if (d > dmax) { S = W - dir * dmax; d = dmax; }
            else if (d < dmin) { S = W - dir * dmin; d = dmin; }
            float a = (lu * lu - lf * lf + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, lu * lu - a * a));
            Vector3 pole = poleRest - dir * Vector3.Dot(poleRest, dir);
            pole = pole.sqrMagnitude > 1e-8f ? pole.normalized : normalRest;
            Vector3 E = S + dir * a + pole * h;
            Vector3 n = Vector3.Cross(E - S, W - E);
            n = n.sqrMagnitude > 1e-10f ? n.normalized : normalRest;
            foreBone.localPosition = E;
            foreBone.localRotation = Quaternion.LookRotation(W - E, n);
            upperBone.localPosition = S;
            upperBone.localRotation = Quaternion.LookRotation(E - S, n);
        }
    }
}
