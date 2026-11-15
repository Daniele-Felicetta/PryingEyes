using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Lampade che si rompono sparandoci (2026-11-15): abat-jour, lampadine appese, applique elettriche, lampade da terra, neon e
    /// scialitiche. Un colpo basta: scoppio di vetro e scintille, la luce si spegne davvero (via dal pool), lampadina, paralume e tubo
    /// passano al materiale spento, i tremolii si fermano. La stanza si scurisce: con l'orda in sala le luci cadono una dopo l'altra.
    /// In cooperativa decide l'ospite: il colpo di un ospitato gli arriva (LampHit), la rottura va a tutti (LampBroken), a chi entra
    /// tardi quelle gia' rotte. Le candele non si rompono.</summary>
    public sealed class BreakableLamps : MonoBehaviour
    {
        public static BreakableLamps I;
        static readonly HashSet<string> Names = new HashSet<string> { "Abatjour", "Lampadina", "Neon", "Applique", "arr_lampada", "AstaScialitica" };
        readonly List<BreakableLamp> list = new List<BreakableLamp>();
        public IReadOnlyList<BreakableLamp> List => list;

        public static void Build(FloorInstance fi)
        {
            if (I != null) Destroy(I.gameObject);
            I = null;
            if (fi == null || fi.root == null) return;
            var go = new GameObject("LampadeFragili");
            go.transform.SetParent(fi.root.transform, false);
            I = go.AddComponent<BreakableLamps>();
            foreach (var pl in LightPool.All)
            {
                if (pl == null || !pl.electric || pl.l == null) continue;
                var p = pl.l.transform.parent;
                if (p == null || !Names.Contains(p.name) || p.GetComponent<BreakableLamp>() != null) continue;
                var b = p.gameObject.AddComponent<BreakableLamp>();
                b.Init(pl, I.list.Count);
                I.list.Add(b);
            }
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] lampade fragili: {I.list.Count}");
        }

        void OnDestroy() { if (I == this) I = null; }
    }

    public sealed class BreakableLamp : MonoBehaviour, IShootable
    {
        public int index;
        public bool Broken { get; private set; }
        public PoolLight Light => pl;
        PoolLight pl;
        SphereCollider col;

        public void Init(PoolLight p, int i)
        {
            pl = p;
            index = i;
            col = gameObject.AddComponent<SphereCollider>();
            float s = Mathf.Max(1e-4f, transform.lossyScale.x);
            col.center = transform.InverseTransformPoint(p.l.transform.position);
            col.radius = 0.17f / s;
        }

        public Vector3 Bulb => pl != null && pl.l != null ? pl.l.transform.position : transform.position;

        public void Shot(float dmg, Vector3 point, Vector3 dir)
        {
            if (Broken) return;
            if (Coop.IsClient) { Coop.Game?.LampHit(index); Fx.Impact(point, -dir, 0.4f); return; }
            Break(true);
            Coop.Game?.LampBroken(index);
        }

        /// <summary>Si rompe: 'fx' con scoppio e scintille (falso per chi entra tardi e le trova gia' rotte).</summary>
        public void Break(bool fx)
        {
            if (Broken) return;
            Broken = true;
            if (pl != null) { pl.wanted = false; if (pl.l != null) pl.l.enabled = false; }
            var lf = GetComponent<LampFlicker>(); if (lf != null) lf.enabled = false;
            var nf = GetComponent<NeonFlicker>(); if (nf != null) nf.enabled = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int k = 0; k < mats.Length; k++)
                {
                    var m = mats[k];
                    if (m == null) continue;
                    string n = m.name;
                    string off = n.Contains("paralume") && !n.Contains("spento") ? "paralume_spento"
                               : n.Contains("lampadina") && !n.Contains("spenta") ? "lampadina_spenta"
                               : n.Contains("neon") && !n.Contains("spento") ? "neon_spento" : null;
                    if (off == null || !ProcKit.HasMat(off)) continue;
                    mats[k] = ProcKit.Mat(off);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (col != null) col.enabled = false;
            if (!fx) return;
            Vector3 b = Bulb;
            Fx.EnemyImpact(b, Vector3.down, new Color(1f, 0.85f, 0.55f));
            Fx.EnemyImpact(b, Vector3.up, new Color(0.85f, 0.9f, 1f));
            Sfx.At(SoundBank.BulbPop, b, 0.9f, Random.Range(0.9f, 1.12f), 2f, 30f);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] lampada {index} ({name}) rotta");
        }
    }
}
