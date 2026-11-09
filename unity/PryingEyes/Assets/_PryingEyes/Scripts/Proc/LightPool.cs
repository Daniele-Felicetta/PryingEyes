using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Una lampada gestita dal pool: 'wanted' dice se dovrebbe essere accesa (candelabri non accesi, lampade rotte).</summary>
    public sealed class PoolLight
    {
        public Light l;
        public bool canShadow;
        public float range;
        public bool wanted = true;
    }

    /// <summary>Con la luce in tempo reale ogni stanza ha le sue lampade, ma solo quelle vicine al giocatore servono:
    /// le lontane si spengono e solo le piu' vicine tra le "buone" (lampadari, candelabri, applique) fanno ombra.
    /// Si aggiorna 5 volte al secondo.</summary>
    public class LightPool : MonoBehaviour
    {
        static readonly List<PoolLight> all = new List<PoolLight>();
        static Transform target;
        float t;
        const float CullExtra = 14f;
        public static int MaxShadows = 4;
        public static float ShadowDist = 11f;

        public static PoolLight Register(Light l, bool canShadow, bool wanted = true)
        {
            var e = new PoolLight { l = l, canShadow = canShadow, range = l.range, wanted = wanted };
            all.Add(e);
            l.shadows = LightShadows.None;
            l.enabled = wanted;
            return e;
        }

        public static void Clear() { all.Clear(); }
        public static int Count => all.Count;
        public static int Active { get { int n = 0; foreach (var e in all) if (e.l != null && e.l.enabled) n++; return n; } }

        void Update()
        {
            t -= Time.unscaledDeltaTime;
            if (t > 0f) return;
            t = 0.2f;
            Tick(true);
        }

        /// <summary>Aggiorna subito. Senza giocatore (foto in editor) si passa un punto.</summary>
        public static void Tick(bool useCamera, Vector3? at = null)
        {
            Vector3 p;
            if (at.HasValue) p = at.Value;
            else
            {
                if (target == null)
                {
                    var c = Camera.main;
                    if (c == null) return;
                    target = c.transform;
                }
                p = target.position;
            }
            all.RemoveAll(e => e.l == null);
            var near = new List<(float d, int i)>();
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                float d = (e.l.transform.position - p).magnitude;
                bool on = e.wanted && d < e.range + CullExtra;
                if (e.l.enabled != on) e.l.enabled = on;
                e.l.shadows = LightShadows.None;
                if (e.canShadow && on && d < ShadowDist) near.Add((d, i));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            for (int k = 0; k < near.Count && k < MaxShadows; k++) all[near[k].i].l.shadows = LightShadows.Soft;
        }
    }
}
