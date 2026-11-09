using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Materiali e arredi che il generatore usa a runtime. Li compila l'editor (TorreBuilder.FillKit) e restano
    /// riferiti dalla scena, cosi' finiscono nella build senza passare da Resources. Ogni cosa ha un nome fisso
    /// (pav_*, mur_*, sof_*, chi_*, arr_*) con cui il generatore la chiede.</summary>
    public class ProcKit : MonoBehaviour
    {
        [Serializable] public struct MatRef { public string name; public Material mat; }
        [Serializable] public struct PrefabRef { public string name; public GameObject prefab; }

        public static ProcKit I;
        public MatRef[] mats = new MatRef[0];
        public PrefabRef[] prefabs = new PrefabRef[0];

        readonly Dictionary<string, Material> md = new Dictionary<string, Material>();
        readonly Dictionary<string, GameObject> pd = new Dictionary<string, GameObject>();
        static Material fallback;

        void Awake() { I = this; Index(); }

        public void Index()
        {
            I = this;
            md.Clear(); pd.Clear();
            foreach (var m in mats) if (m.mat != null) md[m.name] = m.mat;
            foreach (var p in prefabs) if (p.prefab != null) pd[p.name] = p.prefab;
        }

        public static Material Mat(string name)
        {
            if (I != null && I.md.Count == 0) I.Index();
            if (I != null && I.md.TryGetValue(name, out var m)) return m;
            if (fallback == null)
            {
                fallback = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.5f, 0.2f, 0.5f), name = "mancante" };
            }
            if (!warned.Contains(name)) { warned.Add(name); Debug.LogWarning("[PryingEyes] materiale del kit mancante: " + name); }
            return fallback;
        }

        public static bool HasMat(string name) { if (I != null && I.md.Count == 0) I.Index(); return I != null && I.md.ContainsKey(name); }

        public static GameObject Prefab(string name)
        {
            if (I != null && I.pd.Count == 0) I.Index();
            return I != null && I.pd.TryGetValue(name, out var p) ? p : null;
        }

        static readonly HashSet<string> warned = new HashSet<string>();
    }
}
