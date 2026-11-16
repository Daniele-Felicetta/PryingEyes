using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>La mano sinistra libera (modelli/hotel/genera_braccia.py -- libera): lo stesso guanto delle mani su misura, ma in posa
    /// neutra e con un perno a ogni articolazione (L_&lt;dito&gt;_1..4, L_pollice_0..3, L_polso, L_palmo, L_dorso). Qui diventa una mano
    /// vera: un osso del palmo sul polso e tre falangi per dito, una pelle calcolata a runtime (pesi per distanza dalle capsule delle
    /// falangi, sfumati alle articolazioni e tra un dito e l'altro, la manichetta divisa tra palmo e avambraccio) e le pose in angoli
    /// anatomici (HandPose) con i campi articolari della mano umana. Riferimento del palmo: +Z verso le dita, +Y il dorso, +X il pollice
    /// (per la sinistra). Senza i perni Build torna null.
    ///
    /// La manichetta (il polsino del guanto, dietro il polso) e' dell'avambraccio, non della mano: ha un osso suo (UpdateCuff) che sta
    /// sul polso, allineato all'avambraccio e girato con la mano attorno al suo asse (la prono-supinazione e' dell'avambraccio, la
    /// flessione e la deviazione no). La pelle passa dal palmo alla manichetta in 3 cm attorno al polso: il polso si piega li', non
    /// 6 cm piu' indietro dentro la manica (prima la manichetta era rigida col palmo e la mano sembrava avere un collo).</summary>
    public sealed class HandRig
    {
        public const int ThumbIndex = 4;
        static readonly string[] Names = { "indice", "medio", "anulare", "mignolo" };
        // raggi del guanto alle articolazioni (m): genera_monaco.FINGERS e THUMB_R per l'ingrasso del guanto (1,10)
        static readonly float[,] Radii =
        {
            { 0.0110f, 0.0101f, 0.0087f, 0.0074f },
            { 0.0113f, 0.0103f, 0.0089f, 0.0076f },
            { 0.0107f, 0.0098f, 0.0084f, 0.0070f },
            { 0.0095f, 0.0086f, 0.0074f, 0.0063f },
            { 0.0149f, 0.0127f, 0.0111f, 0.0092f },
        };

        public readonly Transform Root, Palm;
        /// <summary>L'osso della manichetta (indice 16 della pelle): sul polso, lungo l'avambraccio (UpdateCuff).</summary>
        Transform cuff;
        readonly Transform[,] bone = new Transform[5, 3];
        readonly Vector3[,] J = new Vector3[5, 4];                 // articolazioni nella posa di legame, riferimento del palmo
        readonly Quaternion[,] restRot = new Quaternion[5, 3];     // rotazioni di legame delle falangi, riferimento del palmo
        readonly Vector3[] flexAxis = new Vector3[5];
        readonly float[] restSpread = new float[4], restMcp = new float[4], restPip = new float[4], restDip = new float[4];
        float thumbSign = 1f, spreadSign = 1f;
        Vector3 thumbAbdAxis, thumbOppAxis, thumbRollAxis, thumbFlexAxis;
        public readonly Vector3 PalmCenter, DorsalPoint, CuffPoint;
        readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
        readonly List<Renderer> renderers = new List<Renderer>();
        /// <summary>La posa applicata all'ultimo Apply (gia' limitata ai campi articolari).</summary>
        public HandPose Current;
        /// <summary>Tremito (gradi): la tensione di una magia che si carica. Rumore a 9-13 Hz su ogni articolazione.</summary>
        public float Tremor;
        float tremorT;

        public IReadOnlyList<Renderer> Renderers => renderers;

        HandRig(Transform root, Transform palm, Vector3 palmCenter, Vector3 dorsal, Vector3 cuff)
        {
            Root = root; Palm = palm; PalmCenter = palmCenter; DorsalPoint = dorsal; CuffPoint = cuff;
        }

        static Transform Find(Transform r, string n)
        {
            foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }

        /// <summary>Punto del polpastrello di un dito (0..3) o del pollice (4), nel mondo: dove si tocca l'arma o nasce una scintilla.
        /// L'ultima falange guarda lungo +Z verso la punta: il polpastrello sta a 3/4 della sua lunghezza, un filo verso il palmo.</summary>
        public Vector3 Fingertip(int f)
        {
            var b = bone[f, 2];
            float len = (J[f, 3] - J[f, 2]).magnitude;
            return b.TransformPoint(new Vector3(0f, -Radii[f, 3] * 0.5f, len * 0.78f));
        }

        /// <summary>Una falange (f 0..4, i 0..2): per attaccarci effetti.</summary>
        public Transform Bone(int f, int i) => bone[Mathf.Clamp(f, 0, 4), Mathf.Clamp(i, 0, 2)];

        /// <summary>Centro del palmo (lato palmare, 1,5 cm sotto il centro della mano), nel mondo.</summary>
        public Vector3 PalmPoint => Palm.TransformPoint(PalmCenter - Vector3.up * 0.012f);
        /// <summary>Il verso in cui guarda il palmo (lato palmare), nel mondo.</summary>
        public Vector3 PalmNormal => -Palm.up;

        public void SetVisible(bool v)
        {
            foreach (var r in renderers) if (r != null) r.enabled = v;
        }

        public bool Visible => renderers.Count > 0 && renderers[0] != null && renderers[0].enabled;

        /// <summary>root = istanza della mano libera; il guanto prende i materiali glove/guard (quelli delle mani su misura dell'arma,
        /// cosi' la mano libera e quella avvolta sono la stessa). La manichetta si lega dritta, nell'asse della mano: a runtime la porta
        /// l'osso della manichetta lungo l'avambraccio vero (UpdateCuff).</summary>
        public static HandRig Build(Transform root, Material glove, Material guard)
        {
            if (root == null) return null;
            Transform tw = Find(root, "L_polso"), tp = Find(root, "L_palmo"), td = Find(root, "L_dorso"), tc = Find(root, "L_manichetta");
            if (tw == null || tp == null || td == null) return null;
            var jt = new Transform[5, 4];
            for (int f = 0; f < 4; f++)
                for (int i = 0; i < 4; i++) if ((jt[f, i] = Find(root, $"L_{Names[f]}_{i + 1}")) == null) return null;
            for (int i = 0; i < 4; i++) if ((jt[4, i] = Find(root, $"L_pollice_{i}")) == null) return null;

            // il palmo: polso come origine, verso il medio e verso il dorso
            Vector3 fwd = (jt[1, 0].position - tw.position).normalized;
            Vector3 up = Vector3.ProjectOnPlane(td.position - tp.position, fwd).normalized;
            var palmGo = new GameObject("osso_palmo");
            palmGo.layer = root.gameObject.layer;
            var palm = palmGo.transform;
            palm.SetParent(root, false);
            palm.SetPositionAndRotation(tw.position, Quaternion.LookRotation(fwd, up));
            Vector3 P(Transform t) => palm.InverseTransformPoint(t.position);
            var h = new HandRig(root, palm, P(tp), P(td), tc != null ? P(tc) : new Vector3(0f, 0f, -0.07f));
            for (int f = 0; f < 5; f++) for (int i = 0; i < 4; i++) h.J[f, i] = P(jt[f, i]);
            h.thumbSign = h.J[4, 3].x >= 0f ? 1f : -1f;
            h.BuildBones();
            h.MeasureRest();
            // la manichetta: sul polso, legata nell'asse della mano (dritta)
            var cuffGo = new GameObject("osso_manichetta");
            cuffGo.layer = root.gameObject.layer;
            h.cuff = cuffGo.transform;
            h.cuff.SetParent(root, false);
            h.cuff.SetPositionAndRotation(palm.position, palm.rotation);

            // pelle: guanto e rinforzo diventano SkinnedMeshRenderer sulle ossa (palmo, 15 falangi, manichetta)
            var bones = new List<Transform> { palm };
            for (int f = 0; f < 5; f++) for (int i = 0; i < 3; i++) bones.Add(h.bone[f, i]);
            bones.Add(h.cuff);
            var mfs = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in mfs)
            {
                if (mf.sharedMesh == null) continue;
                bool isGuard = mf.name.Contains("rinforzo");
                var smr = h.SkinPart(mf, bones.ToArray(), isGuard);
                if (smr == null) continue;
                var mat = isGuard ? guard : glove;
                if (mat != null) smr.sharedMaterial = mat;
                h.skins.Add(smr);
                h.renderers.Add(smr);
            }
            if (h.skins.Count == 0) return null;
            h.Current = h.RestPose();
            return h;
        }

        void BuildBones()
        {
            for (int f = 0; f < 5; f++)
            {
                Transform parent = Palm;
                for (int i = 0; i < 3; i++)
                {
                    var go = new GameObject(f < 4 ? $"osso_{Names[f]}_{i + 1}" : $"osso_pollice_{i}");
                    go.layer = Root.gameObject.layer;
                    var t = go.transform;
                    t.SetParent(parent, false);
                    Vector3 d = J[f, i + 1] - J[f, i];
                    Vector3 upHint = Vector3.ProjectOnPlane(Vector3.up, d);
                    if (upHint.sqrMagnitude < 1e-8f) upHint = Vector3.ProjectOnPlane(Vector3.forward, d);
                    restRot[f, i] = Quaternion.LookRotation(d, upHint);
                    t.SetPositionAndRotation(Palm.TransformPoint(J[f, i]), Palm.rotation * restRot[f, i]);
                    bone[f, i] = t;
                    parent = t;
                }
            }
        }

        static float Deg(float r) => r * Mathf.Rad2Deg;

        /// <summary>Gli angoli della posa di legame (dalla geometria dei perni) e gli assi di ogni articolazione, con il verso deciso
        /// provando: flettere porta la punta verso il palmo (-Y), aprire verso il pollice, l'abduzione del pollice verso il palmo,
        /// l'opposizione verso il mignolo.</summary>
        void MeasureRest()
        {
            // apertura: rotazione attorno al dorso (Y) che porta +Z verso il pollice
            spreadSign = (Quaternion.AngleAxis(5f, Vector3.up) * Vector3.forward).x * thumbSign > 0f ? 1f : -1f;
            for (int f = 0; f < 4; f++)
            {
                Vector3 d0 = (J[f, 1] - J[f, 0]).normalized, d1 = (J[f, 2] - J[f, 1]).normalized, d2 = (J[f, 3] - J[f, 2]).normalized;
                Vector3 proj = Vector3.ProjectOnPlane(d0, Vector3.up).normalized;
                Vector3 ax = Vector3.Cross(Vector3.up, proj).normalized;
                if ((Quaternion.AngleAxis(10f, ax) * proj).y > 0f) ax = -ax;
                flexAxis[f] = ax;
                restSpread[f] = Deg(Mathf.Atan2(proj.x * thumbSign, proj.z));
                restMcp[f] = Deg(Mathf.Asin(Mathf.Clamp(-d0.y, -1f, 1f)));
                restPip[f] = Vector3.SignedAngle(d0, d1, ax);
                restDip[f] = Vector3.SignedAngle(d1, d2, ax);
            }
            // pollice: metacarpo dal CMC; la flessione chiude verso il centro del palmo, l'abduzione porta verso il lato palmare
            Vector3 t0 = (J[4, 1] - J[4, 0]).normalized, t1 = (J[4, 2] - J[4, 1]).normalized;
            thumbRollAxis = t0;
            Vector3 toPalm = Vector3.ProjectOnPlane(PalmCenter - Vector3.up * 0.02f - J[4, 1], t1).normalized;
            thumbFlexAxis = Vector3.Cross(t1, toPalm).normalized;
            if (Vector3.Dot(Quaternion.AngleAxis(10f, thumbFlexAxis) * t1, toPalm) < 0f) thumbFlexAxis = -thumbFlexAxis;
            thumbAbdAxis = Vector3.Cross(t0, Vector3.down).normalized;
            if (thumbAbdAxis.sqrMagnitude < 1e-6f) thumbAbdAxis = Vector3.forward;
            if ((Quaternion.AngleAxis(10f, thumbAbdAxis) * t0).y > t0.y) thumbAbdAxis = -thumbAbdAxis;
            thumbOppAxis = Vector3.up;
            if ((Quaternion.AngleAxis(10f, thumbOppAxis) * t0).x * thumbSign > t0.x * thumbSign) thumbOppAxis = -thumbOppAxis;
        }

        /// <summary>La posa di legame in angoli (quella del modello: le dita appena piegate, il pollice neutro).</summary>
        public HandPose RestPose()
        {
            var p = new HandPose();
            for (int f = 0; f < 4; f++) p.SetFinger(f, restSpread[f], restMcp[f], restPip[f], restDip[f]);
            return p;
        }

        // ------------------------------------------------------------------ campi articolari
        /// <summary>Limita la posa alla mano umana: MCP da -30 (iperestensione) a 92, PIP da 0 a 110, DIP da -10 a 88 e mai piu' della PIP
        /// + 10 (non si piega l'ultima falange da sola), apertura entro 25 gradi dalla direzione naturale del dito; pollice entro i suoi.</summary>
        public HandPose Limit(HandPose p)
        {
            for (int f = 0; f < 4; f++)
            {
                float s = Mathf.Clamp(p.Spread(f), restSpread[f] - 22f, restSpread[f] + 22f);
                float m = Mathf.Clamp(p.Mcp(f), -30f, 92f);
                float pi = Mathf.Clamp(p.Pip(f), -2f, 110f);
                float d = Mathf.Clamp(p.Dip(f), -10f, Mathf.Min(88f, pi + 12f));
                // con le dita chiuse a pugno non si apre a ventaglio (i legamenti collaterali sono tesi)
                float closed = Mathf.Clamp01((m - 45f) / 40f);
                s = Mathf.Lerp(s, restSpread[f] * 0.2f, closed * 0.6f);
                p.SetFinger(f, s, m, pi, d);
            }
            p.tAbd = Mathf.Clamp(p.tAbd, -30f, 52f);
            p.tOpp = Mathf.Clamp(p.tOpp, -22f, 78f);
            p.tMcp = Mathf.Clamp(p.tMcp, -15f, 66f);
            p.tIp = Mathf.Clamp(p.tIp, -22f, 86f);
            return p;
        }

        /// <summary>Le rotazioni (riferimento del palmo, rispetto alla posa di legame) delle tre falangi di un dito o del pollice.</summary>
        void ChainRots(in HandPose pose, int f, float n0, float n1, out Quaternion q0, out Quaternion q1, out Quaternion q2)
        {
            if (f < 4)
            {
                float ds = (pose.Spread(f) - restSpread[f]) * spreadSign + n1 * 0.4f;
                float dm = pose.Mcp(f) - restMcp[f] + n0, dp = pose.Pip(f) - restPip[f] + n1, dd = pose.Dip(f) - restDip[f] + n0 * 0.6f;
                Vector3 ax = flexAxis[f];
                q0 = Quaternion.AngleAxis(ds, Vector3.up) * Quaternion.AngleAxis(dm, ax);
                q1 = q0 * Quaternion.AngleAxis(dp, ax);
                q2 = q1 * Quaternion.AngleAxis(dd, ax);
                return;
            }
            q0 = Quaternion.AngleAxis(pose.tOpp + n0 * 0.5f, thumbOppAxis) * Quaternion.AngleAxis(pose.tAbd, thumbAbdAxis)
                 * Quaternion.AngleAxis(0.45f * pose.tOpp, thumbRollAxis);
            q1 = q0 * Quaternion.AngleAxis(pose.tMcp + n0, thumbFlexAxis);
            q2 = q1 * Quaternion.AngleAxis(pose.tIp, thumbFlexAxis);
        }

        /// <summary>Mette la mano nella posa (limitata), piu' il tremito. Le falangi ruotano attorno ai loro perni, in catena.</summary>
        public void Apply(HandPose pose, float dt = 0f)
        {
            if (Palm == null) return;
            pose = Limit(pose);
            Current = pose;
            float tr = Tremor;
            if (tr > 0.01f) tremorT += dt;
            Quaternion pr = Palm.rotation;
            for (int f = 0; f < 5; f++)
            {
                float n0 = 0f, n1 = 0f;
                if (tr > 0.01f) { n0 = Mathf.Sin(tremorT * (11.3f + f * 1.7f) + f) * tr; n1 = Mathf.Sin(tremorT * (9.1f + f * 2.3f) + 2f * f) * tr; }
                ChainRots(pose, f, n0, n1, out var q0, out var q1, out var q2);
                bone[f, 0].rotation = pr * (q0 * restRot[f, 0]);
                bone[f, 1].rotation = pr * (q1 * restRot[f, 1]);
                bone[f, 2].rotation = pr * (q2 * restRot[f, 2]);
            }
        }

        /// <summary>Porta la manichetta lungo l'avambraccio: sul polso (l'origine del palmo), con l'asse della mano piegato sull'asse
        /// dell'avambraccio ('forearm', mondo, dal gomito al polso) per la via piu' corta. Cosi' la manichetta prende dalla mano solo la
        /// rotazione attorno all'avambraccio (prono-supinazione) e lascia al polso flessione e deviazione. Zero = rigida col palmo.</summary>
        public void UpdateCuff(Vector3 forearm)
        {
            if (cuff == null || Palm == null) return;
            Quaternion pr = Palm.rotation;
            Quaternion q = forearm.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(pr * Vector3.forward, forearm.normalized) * pr : pr;
            cuff.SetPositionAndRotation(Palm.position, q);
        }

        /// <summary>Un punto di contatto (0 polso, 1 palmo, 2 indice, 3 medio, 4 pollice, 5 mignolo) nel riferimento del palmo per una
        /// posa, senza toccare le ossa (cinematica diretta): serve a mettere il polpastrello o il palmo esattamente su un punto.</summary>
        public Vector3 ContactInPalm(HandPose pose, int contact)
        {
            switch (contact)
            {
                case 0: return Vector3.zero;
                case 1: return PalmCenter - Vector3.up * 0.012f;
            }
            int f = contact == 2 ? 0 : contact == 3 ? 1 : contact == 4 ? 4 : 3;
            pose = Limit(pose);
            ChainRots(pose, f, 0f, 0f, out var q0, out var q1, out var q2);
            Vector3 p = J[f, 0] + q0 * (J[f, 1] - J[f, 0]) + q1 * (J[f, 2] - J[f, 1]);
            Vector3 seg = q2 * (J[f, 3] - J[f, 2]);
            return p + seg * 0.78f + (q2 * restRot[f, 2]) * new Vector3(0f, -Radii[f, 3] * 0.5f, 0f);
        }

        // ------------------------------------------------------------------ posa dalle articolazioni
        /// <summary>La posa che porta le falangi sulle articolazioni date (riferimento del palmo): dita[f][0..3] = MCP, PIP, DIP, punta;
        /// pollice[0..3] = CMC, MCP, IP, punta. Dita: angoli esatti (la piega e' planare come in genera_mani2); pollice: ricerca
        /// dell'abduzione e dell'opposizione che allineano il metacarpo, poi le pieghe.</summary>
        public HandPose PoseFromJoints(Vector3[][] dita, Vector3[] pollice)
        {
            var p = RestPose();
            for (int f = 0; f < 4 && f < dita.Length; f++)
            {
                var d = dita[f];
                Vector3 d0 = (d[1] - d[0]).normalized, d1 = (d[2] - d[1]).normalized, d2 = (d[3] - d[2]).normalized;
                Vector3 proj = Vector3.ProjectOnPlane(d0, Vector3.up);
                if (proj.sqrMagnitude < 1e-8f) proj = Vector3.forward;
                proj.Normalize();
                float spread = Deg(Mathf.Atan2(proj.x * thumbSign, proj.z));
                float mcp = Deg(Mathf.Asin(Mathf.Clamp(-d0.y, -1f, 1f)));
                Vector3 ax = Quaternion.AngleAxis((spread - restSpread[f]) * spreadSign, Vector3.up) * flexAxis[f];
                p.SetFinger(f, spread, mcp, Vector3.SignedAngle(d0, d1, ax), Vector3.SignedAngle(d1, d2, ax));
            }
            if (pollice != null && pollice.Length >= 4)
            {
                Vector3 want = (pollice[1] - pollice[0]).normalized;
                Vector3 r0 = (J[4, 1] - J[4, 0]).normalized;
                float bestA = 0f, bestO = 0f, bestE = float.MaxValue;
                for (float step = 8f, ca = 0f, co = 0f; step >= 0.5f; step *= 0.5f)
                {
                    for (int ia = -4; ia <= 4; ia++)
                        for (int io = -4; io <= 4; io++)
                        {
                            float a = Mathf.Clamp(ca + ia * step, -30f, 52f), o = Mathf.Clamp(co + io * step, -22f, 78f);
                            Vector3 dir = Quaternion.AngleAxis(o, thumbOppAxis) * Quaternion.AngleAxis(a, thumbAbdAxis) * r0;
                            float e = 1f - Vector3.Dot(dir, want);
                            if (e < bestE) { bestE = e; bestA = a; bestO = o; }
                        }
                    ca = bestA; co = bestO;
                }
                Quaternion q0 = Quaternion.AngleAxis(bestO, thumbOppAxis) * Quaternion.AngleAxis(bestA, thumbAbdAxis) * Quaternion.AngleAxis(0.45f * bestO, thumbRollAxis);
                Vector3 ax = q0 * thumbFlexAxis;
                Vector3 c0 = q0 * (J[4, 2] - J[4, 1]).normalized;          // direzione della falange prossimale senza piega
                Vector3 w1 = (pollice[2] - pollice[1]).normalized, w2 = (pollice[3] - pollice[2]).normalized;
                float mcp = Vector3.SignedAngle(c0, Vector3.ProjectOnPlane(w1, ax), ax);
                Vector3 c1 = Quaternion.AngleAxis(mcp, ax) * q0 * (J[4, 3] - J[4, 2]).normalized;
                float ip = Vector3.SignedAngle(c1, Vector3.ProjectOnPlane(w2, ax), ax);
                p.tAbd = bestA; p.tOpp = bestO; p.tMcp = mcp; p.tIp = ip;
            }
            return p;
        }

        /// <summary>Il riferimento del palmo (polso, rotazione) costruito da tre punti come quelli della mano libera: polso, nocca del
        /// medio, e il verso del dorso (dorso - palmo).</summary>
        public static Quaternion PalmFrame(Vector3 wrist, Vector3 middleMcp, Vector3 dorsalDir)
        {
            Vector3 fwd = (middleMcp - wrist).normalized;
            Vector3 up = Vector3.ProjectOnPlane(dorsalDir, fwd);
            if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(Vector3.up, fwd);
            return Quaternion.LookRotation(fwd, up.normalized);
        }

        // ------------------------------------------------------------------ pelle
        static readonly Dictionary<Mesh, BoneWeight[]> weightCache = new Dictionary<Mesh, BoneWeight[]>();

        SkinnedMeshRenderer SkinPart(MeshFilter mf, Transform[] bones, bool isGuard)
        {
            var src = mf.sharedMesh;
            if (!src.isReadable) { Debug.LogWarning("[PryingEyes] mano libera: mesh non leggibile, resta rigida: " + src.name); return null; }
            var go = new GameObject(mf.name + "_pelle");
            go.layer = mf.gameObject.layer;
            go.transform.SetParent(Root, false);
            go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
            go.transform.localScale = Vector3.one;
            Vector3 ls = mf.transform.lossyScale, ps = go.transform.lossyScale;
            go.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
            var mesh = Object.Instantiate(src);
            mesh.name = src.name + "_pelle";
            var toPalm = Palm.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var vs = src.vertices;
            if (!weightCache.TryGetValue(src, out var bw) || bw == null || bw.Length != vs.Length)
            {
                bw = new BoneWeight[vs.Length];
                for (int i = 0; i < vs.Length; i++) bw[i] = Weigh(toPalm.MultiplyPoint3x4(vs[i]), isGuard);
                weightCache[src] = bw;
            }
            mesh.boneWeights = bw;
            var bind = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) bind[b] = bones[b].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = Palm;
            smr.quality = SkinQuality.Bone4;
            smr.updateWhenOffscreen = true;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                smr.sharedMaterials = mr.sharedMaterials;
                smr.renderingLayerMask = mr.renderingLayerMask;
            }
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = true;
            mf.gameObject.SetActive(false);
            return smr;
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            Vector3 ab = b - a;
            float l2 = Mathf.Max(1e-10f, ab.sqrMagnitude);
            t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
            return (p - (a + ab * t)).magnitude;
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Distanza (con segno) del punto dalla superficie del palmo, una scatola smussata dal polso alle nocche.</summary>
        float PalmDist(Vector3 p)
        {
            float zMax = Mathf.Max(J[0, 0].z, J[1, 0].z) - 0.004f;
            Vector3 c = new Vector3(0.002f * thumbSign, -0.002f, (zMax - 0.075f) * 0.5f);
            Vector3 e = new Vector3(0.040f, 0.012f, (zMax + 0.075f) * 0.5f);
            Vector3 q = new Vector3(Mathf.Abs(p.x - c.x) - e.x, Mathf.Abs(p.y - c.y) - e.y, Mathf.Abs(p.z - c.z) - e.z);
            Vector3 qp = Vector3.Max(q, Vector3.zero);
            return qp.magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - 0.008f;
        }

        /// <summary>Pesi di un vertice (riferimento del palmo): sceglie la parte piu' vicina (palmo o un dito) con una sfumatura tra le
        /// due piu' vicine (la membrana tra le dita), poi divide il peso del dito tra le sue falangi attorno alle articolazioni.</summary>
        BoneWeight Weigh(Vector3 p, bool isGuard)
        {
            var w = new Dictionary<int, float>(6);
            void Add(int b, float v) { if (v <= 1e-5f) return; w.TryGetValue(b, out float o); w[b] = o + v; }

            float dPalm = PalmDist(p);
            int f1 = -1, f2 = -1; float d1 = float.MaxValue, d2 = float.MaxValue;
            var segI = new int[5]; var segT = new float[5];
            if (!isGuard)
            {
                for (int f = 0; f < 5; f++)
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < 3; i++)
                    {
                        float d = SegDist(p, J[f, i], J[f, i + 1], out float t) - Mathf.Lerp(Radii[f, i], Radii[f, i + 1], t);
                        if (d < best) { best = d; segI[f] = i; segT[f] = t; }
                    }
                    if (best < d1) { d2 = d1; f2 = f1; d1 = best; f1 = f; }
                    else if (best < d2) { d2 = best; f2 = f; }
                }
            }
            // parti: il palmo vince se e' chiaramente piu' vicino; tra due dita vicini (membrana) si divide
            float wPalm, wF1, wF2;
            if (isGuard || f1 < 0 || d1 > dPalm + 0.003f) { wPalm = 1f; wF1 = wF2 = 0f; }
            else
            {
                const float tau = 0.0022f;
                float e1 = 1f, e2 = f2 >= 0 ? Mathf.Exp(-(d2 - d1) / tau) : 0f, ep = Mathf.Exp(-(dPalm - d1) / tau) * 0.6f;
                float s = e1 + e2 + ep;
                wF1 = e1 / s; wF2 = e2 / s; wPalm = ep / s;
            }
            void Finger(int f, float share)
            {
                if (share <= 0f || f < 0) return;
                // coordinata lungo la catena: prima della nocca il palmo, poi le falangi con le articolazioni sfumate
                Vector3 a0 = J[f, 0], a1 = J[f, 1];
                float len0 = (a1 - a0).magnitude;
                float sAlong = Vector3.Dot(p - a0, (a1 - a0) / Mathf.Max(1e-6f, len0));
                float r0 = Radii[f, 0];
                float toFinger = Smooth(-0.55f * r0, 0.45f * r0, sAlong);
                Add(0, share * (1f - toFinger));
                float fs = share * toFinger;
                int i = segI[f];
                float t = segT[f];
                float segLen = (J[f, i + 1] - J[f, i]).magnitude;
                float sIn = t * segLen;
                // vicino all'articolazione d'inizio (non la nocca) si mescola con la falange prima; vicino alla fine con quella dopo
                float wPrev = i > 0 ? 0.5f * (1f - Smooth(0f, 0.75f * Radii[f, i], sIn)) : 0f;
                float wNext = i < 2 ? 0.5f * Smooth(segLen - 0.75f * Radii[f, i + 1], segLen, sIn) : 0f;
                int b = 1 + f * 3 + i;
                Add(b, fs * (1f - wPrev - wNext));
                if (wPrev > 0f) Add(b - 1, fs * wPrev);
                if (wNext > 0f) Add(b + 1, fs * wNext);
            }
            Finger(f1, wF1);
            Finger(f2, wF2);
            if (wPalm > 0f) Add(0, wPalm);
            // dietro il polso il peso del palmo passa alla manichetta (osso 16): tutta da 2 cm dietro, niente da 0,8 cm davanti
            if (w.TryGetValue(0, out float wp0))
            {
                float wc = 1f - Smooth(-0.020f, 0.008f, p.z);
                if (wc > 1e-4f) { w[0] = wp0 * (1f - wc); Add(16, wp0 * wc); }
            }
            // i quattro piu' pesanti, normalizzati
            var list = new List<KeyValuePair<int, float>>(w);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            float sum = 0f;
            for (int k = 0; k < list.Count && k < 4; k++) sum += list[k].Value;
            if (sum <= 1e-6f) return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            var r = new BoneWeight();
            for (int k = 0; k < list.Count && k < 4; k++)
            {
                float v = list[k].Value / sum;
                switch (k)
                {
                    case 0: r.boneIndex0 = list[k].Key; r.weight0 = v; break;
                    case 1: r.boneIndex1 = list[k].Key; r.weight1 = v; break;
                    case 2: r.boneIndex2 = list[k].Key; r.weight2 = v; break;
                    default: r.boneIndex3 = list[k].Key; r.weight3 = v; break;
                }
            }
            return r;
        }
    }
}
