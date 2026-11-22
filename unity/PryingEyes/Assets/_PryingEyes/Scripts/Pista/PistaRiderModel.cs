using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Pista
{
    /// <summary>The bones of the rider drawn in Blender (modelli/moto/rider.py, Resources/Moto/pilota.fbx): two bones for each arm and each leg, the rest lengths and the rest joints the pose works from.
    /// The torso, the helmet, the gloves and the boots are rigid and live in the rig of the bike (hips, torso, head, steer, body); the limbs are skinned over these bones, which are put in the world every
    /// frame from the shoulder, the grip, the hip and the peg: two-bone IK with the elbow and the knee bending toward a pole (the knee of the inside leg goes out when the rider hangs off).</summary>
    public sealed class PistaRiderRig
    {
        public readonly Transform[] upper = new Transform[2], fore = new Transform[2], thigh = new Transform[2], shin = new Transform[2];
        /// <summary>The joints at rest: the shoulder in the frame of the torso, the wrist in the frame of the steering, the hip in the frame of the hips, the ankle in the frame of the body (index 0 left, 1 right).</summary>
        public readonly Vector3[] shoulderLocal = new Vector3[2], wristLocal = new Vector3[2], hipLocal = new Vector3[2], ankleRest = new Vector3[2];
        public float armL1, armL2, legL1, legL2;

        /// <summary>Put a bone at 'a' pointing to 'b', stretched so that it ends at 'b' (a bone of length 'restLen' in the frame of its parent at rest).</summary>
        static void Place(Transform bone, Vector3 a, Vector3 b, float restLen, Vector3 up)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            bone.SetPositionAndRotation(a, Quaternion.LookRotation(d / len, up));
            float ps = bone.parent != null ? Mathf.Max(0.01f, bone.parent.lossyScale.z) : 1f;
            bone.localScale = new Vector3(1f, 1f, len / (restLen * ps));
        }

        /// <summary>The middle joint of a two-bone chain from 'root' to 'target', bent toward 'pole' (out of reach the chain straightens).</summary>
        static Vector3 MidJoint(Vector3 root, Vector3 target, float l1, float l2, Vector3 pole)
        {
            Vector3 d = target - root;
            float dist = d.magnitude;
            if (dist < 1e-5f) return root + pole.normalized * l1;
            Vector3 dir = d / dist;
            dist = Mathf.Clamp(dist, Mathf.Abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            float a = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 p = pole - dir * Vector3.Dot(pole, dir);
            if (p.sqrMagnitude < 1e-6f) p = Vector3.Cross(dir, Vector3.right);
            return root + dir * a + p.normalized * h;
        }

        static bool logged;

        public void Pose(PistaBikeRig rig, in BikeView v, float hang)
        {
            Transform body = rig.body;
            Vector3 up = body.up;
            float ps = Mathf.Max(0.01f, body.lossyScale.z);
            for (int i = 0; i < 2; i++)
            {
                float sd = i == 0 ? -1f : 1f;
                // the arm: from the shoulder to the grip, the elbow down and a little out (modelli/moto/rider.py ELBOW_OUT: the same 0.20, so that the bent arm of the file is the arm of the first frame;
                // 0.42 put the elbows wider than the grips)
                Vector3 s = rig.torso.TransformPoint(shoulderLocal[i]);
                Vector3 h = rig.steer.TransformPoint(wristLocal[i]);
                Vector3 pole = body.right * (sd * 0.20f) + body.forward * 0.05f - up * 0.55f;
                Vector3 e = MidJoint(s, h, armL1 * ps, armL2 * ps, pole);
                Place(upper[i], s, e, armL1, up);
                Place(fore[i], e, h, armL2, up);
                // the leg: from the hip to the peg, the knee forward; the knee of the inside leg goes out, the other presses in
                Vector3 hp = rig.hips.TransformPoint(hipLocal[i]);
                Vector3 an = body.TransformPoint(ankleRest[i]);
                float inside = Mathf.Max(0f, hang * sd), outside = Mathf.Max(0f, -hang * sd);
                Vector3 lp = body.forward + up * 0.15f + body.right * (sd * (1.7f * inside - 0.35f * outside));
                Vector3 k = MidJoint(hp, an, legL1 * ps, legL2 * ps, lp);
                Place(thigh[i], hp, k, legL1, up);
                Place(shin[i], k, an, legL2, up);
                if (PistaRiderModel.Verbose && !logged && i == 1)
                {
                    logged = true;
                    Debug.Log("[PistaRider] first pose, right side, in the frame of the body: shoulder " + body.InverseTransformPoint(s).ToString("F3") + " elbow " + body.InverseTransformPoint(e).ToString("F3") + " grip "
                        + body.InverseTransformPoint(h).ToString("F3") + " hip " + body.InverseTransformPoint(hp).ToString("F3") + " knee " + body.InverseTransformPoint(k).ToString("F3") + " ankle " + body.InverseTransformPoint(an).ToString("F3")
                        + " | bone scales " + upper[i].localScale.z.ToString("F3") + " " + fore[i].localScale.z.ToString("F3") + " " + thigh[i].localScale.z.ToString("F3") + " " + shin[i].localScale.z.ToString("F3"));
                }
            }
        }
    }

    /// <summary>Builds the rider of Resources/Moto/pilota.fbx on a bike's rig: the pieces of the FBX are moved under the nodes of the rig (keeping where they are), the limbs are skinned over two bones, and the
    /// materials are made from the names the FBX gives them (the role of a piece: suit, hump, arm_upper, arm_fore, leg_thigh, leg_shin, slider, glove, boot, helmet, seal, visor), which PistaPaint.shader reads
    /// as the part of the livery. Without the FBX (a build that lacks it) the bike keeps the rider that PistaBikeMesh draws by code.</summary>
    public static class PistaRiderModel
    {
        const string Path = "Moto/pilota";
        static GameObject prefab;
        static bool looked;
        /// <summary>Tests and comparisons: draw the rider by code even when the model is there.</summary>
        public static bool Disabled;
        /// <summary>-pistariderlog: write the rest joints of the model and the first pose of a bike in the log (to see why a limb is where it is).</summary>
        public static readonly bool Verbose = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pistariderlog") >= 0;
        static readonly Dictionary<Mesh, Mesh> skinned = new Dictionary<Mesh, Mesh>();
        static bool modelLogged;

        public static GameObject Prefab
        {
            get
            {
                if (!looked) { looked = true; prefab = Resources.Load<GameObject>(Path); }
                return prefab;
            }
        }

        public static bool Available => !Disabled && Prefab != null;

        static Material Part(PistaBikeRig rig, Material src, int part, string nm)
        {
            var m = new Material(src) { name = src.name + "_" + nm };
            m.SetFloat("_Part", part);
            m.SetFloat("_AO", 1f);
            rig.extraMats.Add(m);
            return m;
        }

        static Material Leather(PistaBikeRig rig, string nm, Color c, float smooth)
        {
            var m = PistaTex.Paint(nm, c, c, 5, 0.25f, 0f, smooth);
            m.SetFloat("_Part", 27);
            m.SetFloat("_AO", 1f);
            rig.extraMats.Add(m);
            return m;
        }

        /// <summary>Skin a limb mesh over two bones: the weights come from the side of the plane at the elbow (or the knee) that a vertex is on, blended over 'blend' metres. The mesh is made once and shared.
        /// 'toBody' carries the vertices of the FBX node into the frame of the body (the importer may leave the axis conversion in the node), where the joints are.</summary>
        static Mesh Skin(Mesh src, Matrix4x4 toBody, Vector3 j0, Vector3 j1, Vector3 j2, float blend, out Matrix4x4[] bind)
        {
            Vector3 d1 = (j1 - j0).normalized, d2 = (j2 - j1).normalized;
            Quaternion r0 = Quaternion.LookRotation(d1, Vector3.up), r1 = Quaternion.LookRotation(d2, Vector3.up);
            bind = new[] { Matrix4x4.TRS(j0, r0, Vector3.one).inverse, Matrix4x4.TRS(j1, r1, Vector3.one).inverse };
            if (skinned.TryGetValue(src, out var have)) return have;
            var m = Object.Instantiate(src);
            m.name = src.name + " (pelle)";
            Vector3 bis = (d1 + d2).normalized;
            var verts = m.vertices;
            var nrm = m.normals;
            bool hasNormals = nrm != null && nrm.Length == verts.Length;
            Matrix4x4 nm = toBody.inverse.transpose;
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = toBody.MultiplyPoint3x4(verts[i]);
                if (hasNormals) nrm[i] = nm.MultiplyVector(nrm[i]).normalized;
            }
            m.vertices = verts;
            if (hasNormals) m.normals = nrm;
            if (toBody.determinant < 0f)
                for (int s = 0; s < m.subMeshCount; s++) { var tr = m.GetTriangles(s); System.Array.Reverse(tr); m.SetTriangles(tr, s); }
            m.RecalculateBounds();
            var w = new BoneWeight[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float t = Mathf.Clamp01((Vector3.Dot(verts[i] - j1, bis) + blend) / (2f * blend));
                t = t * t * (3f - 2f * t);
                w[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f - t, boneIndex1 = 1, weight1 = t };
            }
            m.boneWeights = w;
            m.bindposes = bind;
            skinned[src] = m;
            return m;
        }

        /// <summary>Where the right grip of a bike is, in the frame of the bike (the left one is mirrored): a bike with clip-ons (styles 0 and 1) and the naked roadster, whose bars are higher and nearer. The model
        /// is drawn for the first; the gloves and the hands of the arms move by the difference.</summary>
        static Vector3 GripOf(int style) { return style < 2 ? new Vector3(0.30f, 0.905f, 0.40f) : new Vector3(0.30f, 1.00f, 0.34f); }

        /// <summary>How far the grip of this style is from the one the model was drawn for, on side 'L' or 'R', in the frame of the bike.</summary>
        static Vector3 GripDelta(Dictionary<string, Vector3> J, string side, int style)
        {
            Vector3 g = GripOf(style);
            if (side == "L") g.x = -g.x;
            Vector3 modelGrip = J.TryGetValue("grip_" + side, out var m) ? m : GripOf(0);
            return g - modelGrip;
        }

        /// <summary>Put the rider of the model on the rig: false (and the rig untouched) when the model is not there.</summary>
        public static bool TryAttach(PistaBikeRig rig, int style = 0)
        {
            var pf = Prefab;
            if (pf == null || Disabled) return false;
            Transform body = rig.body;
            var inst = Object.Instantiate(pf, body);
            inst.name = "Pilota";
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            var J = new Dictionary<string, Vector3>();
            var nodes = new Dictionary<string, Transform>();
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (t == inst.transform) continue;
                if (t.name.StartsWith("J_")) J[t.name.Substring(2)] = body.InverseTransformPoint(t.position);
                else nodes[t.name] = t;
            }
            if (Verbose && !modelLogged)
                foreach (var kv in nodes)
                {
                    var mf0 = kv.Value.GetComponent<MeshFilter>();
                    if (mf0 == null || mf0.sharedMesh == null) continue;
                    var bb0 = mf0.sharedMesh.bounds;
                    Debug.Log("[PistaRider] node " + kv.Key + " local pos " + kv.Value.localPosition.ToString("F3") + " euler " + kv.Value.localEulerAngles.ToString("F1") + " scale " + kv.Value.localScale.ToString("F3")
                        + " | mesh bounds centre " + bb0.center.ToString("F3") + " size " + bb0.size.ToString("F3") + " | in the frame of the body " + body.InverseTransformPoint(kv.Value.TransformPoint(bb0.center)).ToString("F3"));
                }
            string[] need = { "hip_c", "head", "shoulder_R", "shoulder_L", "elbow_R", "elbow_L", "wrist_R", "wrist_L", "hip_R", "hip_L", "knee_R", "knee_L", "ankle_R", "ankle_L" };
            foreach (var n in need)
                if (!J.ContainsKey(n)) { Object.Destroy(inst); Debug.LogWarning("[PistaRider] the model has no joint " + n); return false; }

            var rr = new PistaRiderRig();
            // ---- the rigid nodes of the bike's rig
            rig.hips = new GameObject("Bacino").transform;
            rig.hips.SetParent(body, false);
            rig.hipBase = J["hip_c"];
            rig.hips.localPosition = rig.hipBase;
            rig.torso = new GameObject("Busto").transform;
            rig.torso.SetParent(rig.hips, false);
            rig.head = new GameObject("Testa").transform;
            rig.head.SetParent(rig.torso, false);
            rig.head.localPosition = J["head"] - J["hip_c"];
            rig.eye = new GameObject("Occhio").transform;
            rig.eye.SetParent(rig.head, false);
            rig.eye.localPosition = new Vector3(0f, 0.02f, 0.09f);

            // ---- the materials, one per role
            var roles = new Dictionary<string, Material>
            {
                { "suit", Part(rig, rig.suit, 20, "busto") },
                { "hump", Part(rig, rig.suit2, 21, "gobba") },
                { "arm_upper", Part(rig, rig.suit, 24, "braccio") },
                { "arm_fore", Part(rig, rig.suit2, 25, "avambraccio") },
                { "leg_thigh", Part(rig, rig.suit2, 22, "coscia") },
                { "leg_shin", Part(rig, rig.suit2, 23, "stinco") },
                { "slider", Part(rig, rig.suit, 26, "paraginocchio") },
                { "glove", Leather(rig, "Guanto", new Color(0.055f, 0.055f, 0.06f), 0.34f) },
                { "boot", Leather(rig, "Stivale", new Color(0.05f, 0.05f, 0.055f), 0.36f) },
                { "helmet", Part(rig, rig.helmet, 8, "casco") },
                { "seal", PistaTex.Lit("Guarnizione", new Color(0.02f, 0.02f, 0.022f), 0.30f) },
                { "visor", PistaTex.Lit("Visiera", new Color(0.05f, 0.06f, 0.08f), 0.95f, 0.2f) },
            };
            rig.extraMats.Add(roles["seal"]); rig.extraMats.Add(roles["visor"]);
            Material Role(string name)
            {
                string key = name.Replace(" (Instance)", "").Trim();
                return roles.TryGetValue(key, out var m) ? m : rig.suit;
            }
            void Dress(Renderer r, bool hideInCockpit)
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = Role(ms[i] != null ? ms[i].name : "");
                r.sharedMaterials = ms;
                r.shadowCastingMode = ShadowCastingMode.On;
                if (hideInCockpit) rig.riderParts.Add(r);
            }

            // ---- the pieces
            foreach (var kv in nodes)
            {
                string n = kv.Key;
                Transform t = kv.Value;
                var mr = t.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                if (n == "torso" || n == "hump") { t.SetParent(rig.torso, true); Dress(mr, true); }
                else if (n.StartsWith("helmet_")) { t.SetParent(rig.head, true); Dress(mr, true); }
                else if (n.StartsWith("boot_")) { t.SetParent(body, true); Dress(mr, true); }
                else if (n.StartsWith("glove_"))
                {
                    t.position += body.TransformVector(GripDelta(J, n.EndsWith("L") ? "L" : "R", style));
                    t.SetParent(rig.steer, true);
                    Dress(mr, false);
                }
            }

            // ---- the limbs: two bones each, rest pose from the empties
            Vector3 hipC = J["hip_c"];
            for (int i = 0; i < 2; i++)
            {
                string s = i == 0 ? "L" : "R";
                Vector3 sh = J["shoulder_" + s], el = J["elbow_" + s], wr = J["wrist_" + s], hp = J["hip_" + s], kn = J["knee_" + s], an = J["ankle_" + s];
                rr.shoulderLocal[i] = sh - hipC;
                rr.hipLocal[i] = hp - hipC;
                rr.ankleRest[i] = an;
                rr.wristLocal[i] = rig.steer.InverseTransformPoint(body.TransformPoint(wr + GripDelta(J, s, style)));
                rr.armL1 = (el - sh).magnitude; rr.armL2 = (wr - el).magnitude;
                rr.legL1 = (kn - hp).magnitude; rr.legL2 = (an - kn).magnitude;
                rr.upper[i] = Bone(body, "Braccio alto " + s, sh, el);
                rr.fore[i] = Bone(body, "Braccio basso " + s, el, wr);
                rr.thigh[i] = Bone(body, "Coscia " + s, hp, kn);
                rr.shin[i] = Bone(body, "Stinco " + s, kn, an);
                Limb(rig, nodes, "arm_" + s, rr.upper[i], rr.fore[i], sh, el, wr, 0.045f, true, Role);          // hidden in the first-person view: the eye is next to the shoulders, and at full tuck an arm fills a fifth of the picture (the gloves stay)
                Limb(rig, nodes, "leg_" + s, rr.thigh[i], rr.shin[i], hp, kn, an, 0.060f, true, Role);
            }
            // what is left of the FBX (the empties, the nodes that moved away) is no longer needed
            foreach (var t in new List<Transform>(inst.GetComponentsInChildren<Transform>(true)))
                if (t != null && t != inst.transform && t.parent == inst.transform) Object.Destroy(t.gameObject);
            Object.Destroy(inst);
            rig.riderRig = rr;
            if (Verbose && !modelLogged)
            {
                modelLogged = true;
                var sb = new System.Text.StringBuilder("[PistaRider] joints (bike frame):");
                foreach (var kv in J) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value.ToString("F3"));
                sb.Append(" | arm ").Append(rr.armL1.ToString("F3")).Append('/').Append(rr.armL2.ToString("F3")).Append(" leg ").Append(rr.legL1.ToString("F3")).Append('/').Append(rr.legL2.ToString("F3"));
                Debug.Log(sb.ToString());
            }
            return true;
        }

        static Transform Bone(Transform parent, string name, Vector3 a, Vector3 b)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = a;
            t.localRotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
            return t;
        }

        static void Limb(PistaBikeRig rig, Dictionary<string, Transform> nodes, string node, Transform b0, Transform b1, Vector3 j0, Vector3 j1, Vector3 j2, float blend, bool hide, System.Func<string, Material> role)
        {
            if (!nodes.TryGetValue(node, out var t)) return;
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (mf == null || mr == null) return;
            var mesh = Skin(mf.sharedMesh, rig.body.worldToLocalMatrix * t.localToWorldMatrix, j0, j1, j2, blend, out var bind);
            var ms = mr.sharedMaterials;
            for (int i = 0; i < ms.Length; i++) ms[i] = role(ms[i] != null ? ms[i].name : "");
            var go = new GameObject(node);
            go.transform.SetParent(rig.body, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = new[] { b0, b1 };
            smr.rootBone = rig.body;
            smr.sharedMaterials = ms;
            smr.quality = SkinQuality.Bone2;
            smr.updateWhenOffscreen = false;
            var bb = mesh.bounds;
            bb.Expand(0.5f);
            smr.localBounds = bb;
            smr.shadowCastingMode = ShadowCastingMode.On;
            if (hide) rig.riderParts.Add(smr);
            Object.Destroy(t.gameObject);
        }
    }
}
