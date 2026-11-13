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
        /// <summary>Scala di tutte le luci del piano create dopo (le ricette di arredo la leggono in RoomKits.AddLight): piu' si scende, piu' buio.
        /// Con 1 i piani restano come nelle foto in editor.</summary>
        public static float Dim = 1f;

        public static PoolLight Register(Light l, bool canShadow, bool wanted = true)
        {
            var e = new PoolLight { l = l, canShadow = canShadow, range = l.range, wanted = wanted };
            all.Add(e);
            l.shadows = LightShadows.None;
            l.enabled = wanted;
            Stealth.SetLightLayers(l, 3u);     // mondo + arma in mano (la torcia invece illumina solo il mondo)
            return e;
        }

        public static void Clear() { all.Clear(); }

        /// <summary>Toglie una lampada dal pool (la torcia di un compagno, in cooperativa, mentre il piano si costruisce).</summary>
        public static void Remove(PoolLight e) { if (e != null) all.Remove(e); }

        /// <summary>Stima la luce diretta sul giocatore, tenendo conto di distanza, coni e ostacoli.</summary>
        public static float Exposure(Vector3 p)
        {
            float sum = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (!e.wanted || e.l == null || !e.l.isActiveAndEnabled) continue;
                float rg = e.range;
                Vector3 origin = e.l.transform.position;
                Vector3 toPoint = p - origin;
                float d = toPoint.magnitude;
                if (d >= rg) continue;
                float f = 1f - d / rg;
                if (e.l.type == LightType.Spot && d > 0.001f)
                {
                    float angle = Vector3.Angle(e.l.transform.forward, toPoint);
                    float outer = e.l.spotAngle * 0.5f;
                    if (angle >= outer) continue;
                    float inner = e.l.innerSpotAngle * 0.5f;
                    if (angle > inner) f *= 1f - Stealth.Smooth(inner, outer, angle);
                }
                if (f <= 0f || !HotelNav.ClearLine(p, origin)) continue;
                sum += e.l.intensity * f * f / (1f + d * d);
            }
            return sum;
        }
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
            Enemy.UpdateBudgets(p);      // ombre e dettaglio dei corpi in base alla distanza e alla folla (Enemy.Budget.cs)
        }
    }
}
