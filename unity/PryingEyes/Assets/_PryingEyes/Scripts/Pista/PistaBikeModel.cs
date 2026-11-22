using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Pista
{
    /// <summary>The bikes drawn in Blender (modelli/moto/bike.py, Resources/Moto/moto0.fbx, moto1.fbx and moto2.fbx for the superbike, the supersport and the naked roadster): the chassis (wheels, fork, swing arm, frame, engine,
    /// exhaust) and the bodywork. Every part of the file is one object whose name says the node of the rig that moves it (body, swing, rear, steer, front, and screen: the windscreen is hidden in the first-person view), and the
    /// name of each material is its role (paint, paint2, frame, metal, steel, titan, engine, black, carbon, seat, glass, gold, caliper, grip, chain, radiator, tyre, lens, taillight): this class turns each role into a material
    /// of the game and puts the part under its node, where it is.  The number plates, their backs and the rider are not in the file (PistaBikeMesh and PistaRiderModel make them). Without the file (a build that lacks it)
    /// the bike is the one PistaBikeMesh draws by code.</summary>
    public static class PistaBikeModel
    {
        static readonly GameObject[] prefabs = new GameObject[3];
        static readonly bool[] looked = new bool[3];
        static readonly Dictionary<string, Material> shared = new Dictionary<string, Material>();
        /// <summary>Tests and comparisons: draw the bike by code even when the model is there.</summary>
        public static bool Disabled;

        static readonly string[] needed = { "body", "swing", "rear", "steer", "front" };

        /// <summary>The model of a style, or null: the file is read once, and a file without one of its nodes is as good as none.</summary>
        public static GameObject Prefab(int style)
        {
            style = Mathf.Clamp(style, 0, 2);
            if (!looked[style])
            {
                looked[style] = true;
                var pf = Resources.Load<GameObject>("Moto/moto" + style);
                if (pf != null)
                {
                    var have = new HashSet<string>();
                    foreach (var mf in pf.GetComponentsInChildren<MeshFilter>(true)) have.Add(mf.name);
                    foreach (var n in needed)
                        if (!have.Contains(n)) { Debug.LogWarning("[PistaBike] the model of style " + style + " has no part '" + n + "': the bike is drawn by code"); pf = null; break; }
                }
                prefabs[style] = pf;
            }
            return prefabs[style];
        }

        public static bool Available(int style) { return !Disabled && Prefab(style) != null; }

        static Material Shared(string role)
        {
            if (shared.TryGetValue(role, out var m) && m != null) return m;
            switch (role)
            {
                case "engine": m = PistaTex.Lit("Blocco", new Color(0.17f, 0.17f, 0.18f), 0.45f, 0.8f); break;
                case "black": m = PistaTex.Lit("Plastica", new Color(0.02f, 0.02f, 0.022f), 0.30f, 0f); break;
                case "carbon": m = PistaTex.Lit("Carbonio", new Color(0.03f, 0.03f, 0.033f), 0.60f, 0.2f); break;
                case "caliper": m = PistaTex.Lit("Pinza", new Color(0.62f, 0.05f, 0.04f), 0.65f, 0.5f); break;
                case "grip": m = PistaTex.Lit("Manopola", new Color(0.02f, 0.02f, 0.022f), 0.20f, 0f); break;
                case "chain": m = PistaTex.Lit("Catena", new Color(0.30f, 0.27f, 0.14f), 0.45f, 0.8f); break;
                case "radiator": m = PistaTex.Lit("Radiatore", new Color(0.03f, 0.03f, 0.035f), 0.35f, 0.5f); break;
                default: m = PistaTex.Lit("Ruolo_" + role, new Color(0.8f, 0.1f, 0.8f), 0.3f); break;          // a role nobody knows: magenta, to be seen
            }
            shared[role] = m;
            return m;
        }

        /// <summary>The material of the game for a role of the file: the livery's own for the paint and the lights, the materials the kit made for the others, then the ones made here.</summary>
        static Material Role(PistaBikeRig rig, string name, System.Func<string, Material> kit)
        {
            string role = name.Replace(" (Instance)", "").Trim();
            switch (role)
            {
                case "paint": return rig.paint;
                case "paint2": return rig.paint2;
                case "lens": return rig.lensMat;
                case "taillight": return rig.tailMat;
            }
            return kit(role) ?? Shared(role);
        }

        /// <summary>Put the model of this style on the rig (its nodes exist, empty): false, and the rig untouched, when the model is not there. 'kit' gives the materials that the styles share (frame, metal, steel, titan, seat, glass, gold, tyre).</summary>
        public static bool TryAttach(PistaBikeRig rig, int style, System.Func<string, Material> kit)
        {
            var pf = Disabled ? null : Prefab(style);
            if (pf == null) return false;
            // the paint materials read the part of the livery from their own _Part (the model has no vertex tags): the paint is part 0, the accent paint 1, the tyres 9
            rig.paint.SetFloat("_Part", 0f);
            rig.paint2.SetFloat("_Part", 1f);
            var tyre = kit("tyre");
            if (tyre != null) tyre.SetFloat("_Part", 9f);
            var inst = Object.Instantiate(pf, rig.body);
            inst.name = "Modello";
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            var nodes = new Dictionary<string, Transform> { { "body", rig.body }, { "screen", rig.body }, { "swing", rig.swing }, { "rear", rig.rearPivot }, { "steer", rig.steer }, { "front", rig.frontPivot } };
            foreach (var mr in new List<MeshRenderer>(inst.GetComponentsInChildren<MeshRenderer>(true)))
            {
                if (!nodes.TryGetValue(mr.name, out var target)) continue;
                var ms = mr.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = Role(rig, ms[i] != null ? ms[i].name : "", kit);
                mr.sharedMaterials = ms;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.transform.SetParent(target, true);
                if (mr.name == "screen") rig.screenParts.Add(mr);
            }
            Object.Destroy(inst);
            return true;
        }
    }
}
