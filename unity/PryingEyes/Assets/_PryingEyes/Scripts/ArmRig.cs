using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Braccio in prima persona con il gomito vero (modelli/hotel/genera_braccia.py). La manica dell'FBX diventa una
    /// SkinnedMeshRenderer su quattro ossa (mano, avambraccio, torsione dell'avambraccio, braccio) e a ogni fotogramma una cinematica
    /// inversa a due ossa porta il gomito dove deve stare perche' l'avambraccio arrivi al polso. Il guanto resta rigido con la mano
    /// (figlio di mano_X). Senza i giunti polso_X / gomito_X / spalla_X (mani vecchie) Build torna null e la manica resta rigida.
    ///
    /// Biomeccanica (2026-11-16):
    ///  - torsione: la pronazione e la supinazione non avvengono al polso ma lungo l'avambraccio (radio che ruota sull'ulna). Un osso di
    ///    torsione a meta' avambraccio prende meta' del rollio della mano rispetto al gomito e la manica lo distribuisce dal polsino al
    ///    gomito: la mano che si gira col palmo in su non strizza piu' il polsino come una caramella;
    ///  - campi articolari: il gomito si chiude fino a 28 gradi tra braccio e avambraccio (flessione massima ~150) e non va mai in
    ///    iperestensione (176 al massimo);
    ///  - comodita' del polso: con il braccio libero (Free) si cerca, tra le rotazioni del gomito attorno all'asse spalla-polso, quella
    ///    che tiene la mano entro ~32 gradi dall'avambraccio, con una molla perche' il gomito non scatti; un suggerimento (PoleHint)
    ///    dice dove preferirebbe stare (giu' e in fuori di norma, alto quando la mano passa davanti al viso);
    ///  - spalla: col braccio libero la spalla segue il corpo (la camera) anche in profondita' e si sposta di ShoulderOffset (la spalla
    ///    che si protende quando il braccio si allunga).
    /// A riposo (Free = 0) il braccio e' esattamente quello di prima. Funziona anche in edit-mode (ArmiShots).</summary>
    public sealed class ArmRig
    {
        readonly Transform root, hand, foreBone, upperBone, twistBone;
        readonly Vector3 shoulder, wristInHand, poleRest, normalRest, handRefLocal, handAxisLocal;
        readonly float lu, lf;
        public readonly SkinnedMeshRenderer Skin;
        Transform body;
        Vector3 shoulderBody;

        /// <summary>In mira la spalla scende di tanto (metri, riferimento del corpo).</summary>
        public float AdsDrop;
        /// <summary>0 = braccio dell'arma (com'era), 1 = braccio libero: spalla col corpo, gomito che cerca il polso comodo.</summary>
        public float Free;
        /// <summary>Dove il gomito preferirebbe puntare (riferimento del corpo, cioe' della camera) e quanto conta (0..1).</summary>
        public Vector3 PoleHint = new Vector3(-0.55f, -0.83f, -0.1f);
        public float PoleWeight;
        /// <summary>Spostamento della spalla (riferimento del corpo) col braccio libero: avanti quando il braccio si allunga.</summary>
        public Vector3 ShoulderOffset;
        /// <summary>Limite di comodita' del polso (gradi tra l'asse della mano e quello dell'avambraccio) che la ricerca del gomito rispetta.</summary>
        public float WristLimit = 32f;
        public const float MinElbowDeg = 28f, MaxElbowDeg = 176f;
        /// <summary>Il braccio libero e' un braccio vero: omero di 30 cm (quello dell'arma e' virtuale, 0,35-0,75 m, perche' il modello in
        /// prima persona sta piu' avanti di una presa vera) e spalla dove sta una spalla, 19 cm di lato, 24 sotto e 3 dietro l'occhio.
        /// L'osso del braccio della manica si accorcia davvero (scala lungo l'osso): la manica resta attaccata alla spalla vera.</summary>
        public const float UpperReal = 0.30f, ShoulderSide = 0.19f, ShoulderDown = 0.24f, ShoulderBack = 0.03f, ProtractMax = 0.12f;
        readonly float side;
        float swivel, swivelVel;

        /// <summary>Spalla a riposo nel riferimento della radice delle mani (= quello dell'arma).</summary>
        public Vector3 ShoulderLocal => shoulder;
        /// <summary>L'osso di torsione dell'avambraccio: la manichetta della mano libera si appende qui.</summary>
        public Transform ForearmTwist => twistBone;
        /// <summary>L'osso dell'avambraccio (al gomito, guarda il polso).</summary>
        public Transform Forearm => foreBone;
        public float UpperLength => lu;
        public float ForeLength => lf;
        /// <summary>Angolo del gomito all'ultimo Solve (gradi tra braccio e avambraccio) e scarto del polso dall'avambraccio: per le prove.</summary>
        public float LastElbowDeg { get; private set; }
        public float LastWristDeg { get; private set; }
        public float LastRollDeg { get; private set; }

        public void AnchorTo(Transform bodyT, Vector3 shoulderInBody) { body = bodyT; shoulderBody = shoulderInBody; }

        static readonly Dictionary<Mesh, Mesh> skinned = new Dictionary<Mesh, Mesh>();

        ArmRig(Transform root, Transform hand, Transform fore, Transform upper, Transform twist, Vector3 shoulder, Vector3 wristInHand,
               Vector3 poleRest, Vector3 normalRest, Vector3 handRefLocal, float lu, float lf, SkinnedMeshRenderer skin, float side)
        {
            this.side = side;
            this.root = root; this.hand = hand; foreBone = fore; upperBone = upper; twistBone = twist; this.shoulder = shoulder;
            this.wristInHand = wristInHand; this.poleRest = poleRest; this.normalRest = normalRest; this.handRefLocal = handRefLocal;
            handAxisLocal = (-wristInHand).sqrMagnitude > 1e-8f ? (-wristInHand).normalized : Vector3.forward;
            this.lu = lu; this.lf = lf; Skin = skin;
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
            var tb = new GameObject("osso_torsione_" + side).transform;
            tb.SetParent(root, false);
            tb.localPosition = E0;
            tb.localRotation = fb.localRotation;
            var ub = new GameObject("osso_braccio_" + side).transform;
            ub.SetParent(root, false);
            ub.localPosition = S0;
            ub.localRotation = Quaternion.LookRotation(E0 - S0, n0);
            foreach (var t in new[] { hb, fb, tb, ub }) t.gameObject.layer = layer;

            // la pelle: copia della mesh con pesi e pose di legame, una volta per mesh (cambiare arma non la ricalcola)
            var go = new GameObject(sleeve.name + "_pelle");
            go.layer = layer;
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(sleeve.transform.position, sleeve.transform.rotation);
            go.transform.localScale = Vector3.one;
            Vector3 ls = sleeve.transform.lossyScale, ps = go.transform.lossyScale;
            go.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
            var bones = new[] { hb, fb, ub, tb };
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
                    // la torsione: vicino al polso l'avambraccio ruota con la mano, vicino al gomito no (radio sull'ulna)
                    float ts = 1f - Smooth(0.18f * lf, 0.92f * lf, s);
                    float wt = wf * ts;
                    wf -= wt;
                    bw[i] = new BoneWeight { boneIndex0 = 0, weight0 = wh, boneIndex1 = 1, weight1 = wf, boneIndex2 = 2, weight2 = wu, boneIndex3 = 3, weight3 = wt };
                }
                mesh.boneWeights = bw;
                skinned[src] = mesh;
            }
            var bind = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) bind[b] = bones[b].worldToLocalMatrix * go.transform.localToWorldMatrix;
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
            // riferimento del rollio: il 'su' dell'avambraccio di legame visto dalla mano (rollio 0 nella posa modellata)
            Vector3 refLocal = hand.InverseTransformDirection(root.TransformDirection(n0));
            return new ArmRig(root, hand, fb, ub, tb, S0, wih, pole, n0, refLocal, lu, lf, smr, side == "s" ? -1f : 1f);
        }

        static Vector3 ElbowAt(Vector3 S, Vector3 dir, float a, float h, Vector3 pole) => S + dir * a + pole * h;

        /// <summary>Gomito a due ossa verso il polso attuale (riferimento della radice delle mani). Va chiamata dopo che la mano e'
        /// stata messa al suo posto nel fotogramma (Weapon.AnimateParts). dt serve alla molla del gomito col braccio libero.</summary>
        public void Solve(float ads = 0f, float dt = 0f)
        {
            if (hand == null || foreBone == null) return;
            Vector3 W = root.InverseTransformPoint(hand.TransformPoint(wristInHand));
            Vector3 S = shoulder;
            float free = Mathf.Clamp01(Free);
            // col braccio libero l'omero torna lungo come un omero vero
            float lu = Mathf.Lerp(this.lu, UpperReal, free);
            if (body != null)
            {
                // di lato e in altezza la spalla sta col corpo; in profondita' resta con l'arma: in mira l'arma viene a 30 cm
                // dall'occhio (al fianco sta a 60) e con la spalla ferma il braccio virtuale, lungo per la posa al fianco, si
                // ripiegherebbe col gomito davanti alla mano. Col braccio libero la spalla e' tutta del corpo.
                Vector3 sb = root.InverseTransformPoint(body.TransformPoint(shoulderBody + Vector3.down * (AdsDrop * Mathf.Clamp01(ads))));
                S = new Vector3(sb.x, sb.y, shoulder.z);
                if (free > 0f)
                {
                    Vector3 sb2 = new Vector3(side * ShoulderSide, -ShoulderDown, -ShoulderBack) + ShoulderOffset;
                    Vector3 sf = root.InverseTransformPoint(body.TransformPoint(sb2));
                    // la spalla si protende verso la mano quando il braccio non ci arriva (fino a 12 cm, la scapola che scivola)
                    Vector3 toW = W - sf;
                    float over = toW.magnitude - (lu + lf) * 0.985f;
                    if (over > 0f) sf += toW.normalized * Mathf.Min(over, ProtractMax);
                    S = Vector3.Lerp(S, sf, free);
                }
            }
            Vector3 sw = W - S;
            float d = sw.magnitude;
            if (d < 1e-5f) return;
            Vector3 dir = sw / d;
            // campi articolari del gomito: tra 28 e 176 gradi fra braccio e avambraccio; fuori portata la spalla (fuori campo) scivola
            float dmax = Mathf.Sqrt(lu * lu + lf * lf - 2f * lu * lf * Mathf.Cos(MaxElbowDeg * Mathf.Deg2Rad));
            float dmin = Mathf.Sqrt(lu * lu + lf * lf - 2f * lu * lf * Mathf.Cos(MinElbowDeg * Mathf.Deg2Rad));
            if (d > dmax) { S = W - dir * dmax; d = dmax; }
            else if (d < dmin) { S = W - dir * dmin; d = dmin; }
            float a = (lu * lu - lf * lf + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, lu * lu - a * a));
            Vector3 pr = poleRest;
            if (free > 0f && body != null)
            {
                Vector3 hint = root.InverseTransformDirection(body.TransformDirection(PoleHint.normalized));
                pr = Vector3.Slerp(poleRest, hint, free * Mathf.Clamp01(0.5f + PoleWeight)).normalized;
            }
            Vector3 pole = pr - dir * Vector3.Dot(pr, dir);
            pole = pole.sqrMagnitude > 1e-8f ? pole.normalized : normalRest;
            if (free > 0.01f)
            {
                // il gomito ruota attorno all'asse spalla-polso fino a tenere il polso comodo (poca rotazione se non serve)
                Vector3 axisH = root.InverseTransformDirection(hand.TransformDirection(handAxisLocal));
                float best = swivel, bestCost = float.MaxValue;
                for (int k = -7; k <= 7; k++)
                {
                    float phi = k * 10f;
                    Vector3 pk = Quaternion.AngleAxis(phi, dir) * pole;
                    Vector3 Ek = ElbowAt(S, dir, a, h, pk);
                    float dev = Vector3.Angle(axisH, (W - Ek).normalized);
                    float over = Mathf.Max(0f, dev - WristLimit);
                    float cost = over * over + 0.012f * phi * phi + 0.03f * (phi - swivel) * (phi - swivel);
                    if (cost < bestCost) { bestCost = cost; best = phi; }
                }
                if (dt > 0f)
                {
                    // molla smorzata: il gomito arriva in ~0,15 s senza oscillare
                    float k2 = 240f, c = 2f * Mathf.Sqrt(k2);
                    swivelVel += (k2 * (best - swivel) - c * swivelVel) * dt;
                    swivel += swivelVel * dt;
                }
                else swivel = best;
                pole = Quaternion.AngleAxis(swivel * free, dir) * pole;
            }
            else { swivel = 0f; swivelVel = 0f; }
            Vector3 E = ElbowAt(S, dir, a, h, pole);
            Vector3 n = Vector3.Cross(E - S, W - E);
            n = n.sqrMagnitude > 1e-10f ? n.normalized : normalRest;
            foreBone.localPosition = E;
            foreBone.localRotation = Quaternion.LookRotation(W - E, n);
            upperBone.localPosition = S;
            upperBone.localRotation = Quaternion.LookRotation(E - S, n);
            // la manica del braccio si accorcia con l'omero (la sua pelle e' legata all'omero virtuale dell'arma)
            float uk = Mathf.Max(0.05f, (E - S).magnitude / Mathf.Max(0.05f, this.lu));
            upperBone.localScale = Mathf.Abs(uk - 1f) < 0.002f ? Vector3.one : new Vector3(1f, 1f, uk);
            // torsione: il rollio della mano attorno all'avambraccio, rispetto al 'su' del gomito; meta' all'osso di meta' avambraccio
            Vector3 fa = (W - E).normalized;
            Vector3 foreUp = Vector3.ProjectOnPlane(foreBone.localRotation * Vector3.up, fa);
            Vector3 handRef = Vector3.ProjectOnPlane(root.InverseTransformDirection(hand.TransformDirection(handRefLocal)), fa);
            float roll = foreUp.sqrMagnitude > 1e-8f && handRef.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(foreUp, handRef, fa) : 0f;
            LastRollDeg = roll;
            if (twistBone != null)
            {
                twistBone.localPosition = E;
                twistBone.localRotation = Quaternion.AngleAxis(roll * 0.5f, fa) * foreBone.localRotation;
            }
            LastElbowDeg = Vector3.Angle(S - E, W - E);
            LastWristDeg = Vector3.Angle(root.InverseTransformDirection(hand.TransformDirection(handAxisLocal)), fa);
        }
    }
}
