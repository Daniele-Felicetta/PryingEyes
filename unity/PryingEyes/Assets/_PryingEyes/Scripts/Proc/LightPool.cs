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
        /// <summary>Lampada elettrica: senza corrente sta al 40%, poi si riaccende a ondata dal quadro (Power).</summary>
        public bool electric;
        /// <summary>Intensita' con la corrente piena e ritardo dell'ondata (distanza dal quadro / velocita').</summary>
        public float baseI, delay;
        /// <summary>-1 da vedere; 1 se un tremolio (LampFlicker, LightPulse, NeonFlicker) la riscrive a ogni fotogramma.</summary>
        public int driven = -1;
        /// <summary>Faceva ombra all'ultimo giro del pool: chi la fa gia' parte avvantaggiato (LightPool.ShadowKeep), cosi' due lampade a pari distanza non si danno il cambio a ogni giro.</summary>
        public bool casting;
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
        /// <summary>Quanto (m) una lampada che gia' fa ombra ha di vantaggio sulle altre, e di quanto puo' superare ShadowDist prima di perderla. Senza, chi sta sotto una lampada (due altre a pari distanza per il
        /// quarto posto) o a un passo da ShadowDist vedeva accendersi e spegnersi le ombre a ogni giro del pool, cinque volte al secondo, per l'oscillazione della testa (-desertonly luci: 48 cambi in 10 s).</summary>
        public const float ShadowKeep = 1.5f;
        /// <summary>Scala di tutte le luci del piano create dopo (le ricette di arredo la leggono in RoomKits.AddLight): piu' si scende, piu' buio.
        /// Con 1 i piani restano come nelle foto in editor.</summary>
        public static float Dim = 1f;
        /// <summary>Se impostato, il pool tiene accese le luci attorno a questo punto invece che attorno alla camera (RoomProbes mentre
        /// fotografa le stanze).</summary>
        public static Vector3? HoldPoint;

        public static PoolLight Register(Light l, bool canShadow, bool wanted = true, bool electric = false)
        {
            var e = new PoolLight { l = l, canShadow = canShadow, range = l.range, wanted = wanted, electric = electric, baseI = l.intensity };
            all.Add(e);
            l.shadows = LightShadows.None;
            l.enabled = wanted;
            Stealth.SetLightLayers(l, 3u);     // mondo + arma in mano (la torcia invece illumina solo il mondo)
            return e;
        }

        public static void Clear() { all.Clear(); }

        /// <summary>Le lampade del pool (le prove della corrente le leggono).</summary>
        public static IReadOnlyList<PoolLight> All => all;

        /// <summary>La corrente torna dal quadro in 'origin': ogni lampada elettrica si riaccende quando l'ondata (speed m/s) la
        /// raggiunge, con un filo di caso. Ritorna quanto ci mette ad arrivare all'ultima (tremolio compreso).</summary>
        public static float PowerCascade(Vector3 origin, float speed)
        {
            float last = 0f;
            foreach (var e in all)
            {
                if (!e.electric || e.l == null) continue;
                e.delay = Vector3.Distance(e.l.transform.position, origin) / speed + Random.Range(0f, 0.12f);
                last = Mathf.Max(last, e.delay);
            }
            return last + 0.45f;
        }

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

        bool lastFull;

        /// <summary>Dopo i tremolii (Update): le lampade elettriche alla scala della corrente. Con la corrente piena e l'ondata finita
        /// basta un ultimo passaggio, poi non si tocca piu' niente.</summary>
        void LateUpdate()
        {
            bool full = Power.On && Time.time - Power.OnTime > Power.CascadeTime + 0.5f;
            if (full && lastFull) return;
            lastFull = full;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (!e.electric || e.l == null || !e.l.enabled) continue;
                if (e.driven < 0)
                {
                    var go = e.l.gameObject;
                    e.driven = go.GetComponent<LampFlicker>() != null || go.GetComponent<LightPulse>() != null || go.GetComponent<NeonFlicker>() != null ? 1 : 0;
                }
                float s = Power.Scale(e.delay);
                if (e.driven == 1) { if (s != 1f) e.l.intensity *= s; }
                else
                {
                    float v = e.baseI * s;
                    if (e.l.intensity != v) e.l.intensity = v;
                }
            }
        }

        void Update()
        {
            t -= Time.unscaledDeltaTime;
            if (t > 0f) return;
            t = 0.2f;
            if (HoldPoint.HasValue) Tick(false, HoldPoint.Value); else Tick(true);
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
            // con un punto da tenere (le sonde dei riflessi) restano accese anche le luci attorno a chi gioca: rifacendo i riflessi a
            // partita in corso le lampade vicine non si spengono per un fotogramma
            Vector3? cam = null;
            if (at.HasValue && useCamera == false && Camera.main != null && HoldPoint.HasValue) cam = Camera.main.transform.position;
            // schermo condiviso: restano accese le luci attorno a ogni giocatore (la distanza e' quella dall'occhio piu' vicino)
            int viewN = 0;
            if (!at.HasValue && Seats.Split)
            {
                var eyes = Seats.Eyes();
                for (int v = 0; v < eyes.Count && v < views.Length; v++) views[viewN++] = eyes[v].position;
            }
            if (viewN == 0) { views[0] = p; viewN = 1; }
            var near = new List<(float d, int i)>();
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                Vector3 lp = e.l.transform.position;
                float d = (lp - views[0]).magnitude;
                for (int v = 1; v < viewN; v++) d = Mathf.Min(d, (lp - views[v]).magnitude);
                bool on = e.wanted && (d < e.range + CullExtra || (cam.HasValue && (lp - cam.Value).magnitude < e.range + CullExtra));
                if (e.l.enabled != on) e.l.enabled = on;
                e.l.shadows = LightShadows.None;
                if (e.canShadow && on && d < ShadowDist + (e.casting ? ShadowKeep : 0f)) near.Add((e.casting ? d - ShadowKeep : d, i));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            int shadowMax = viewN > 1 ? MaxShadows + viewN - 1 : MaxShadows;       // a schermo condiviso un'ombra in piu' per giocatore
            for (int i = 0; i < all.Count; i++) all[i].casting = false;
            for (int k = 0; k < near.Count && k < shadowMax; k++) { var e = all[near[k].i]; e.l.shadows = LightShadows.Soft; e.casting = true; }
            Enemy.UpdateBudgets(p);      // ombre e dettaglio dei corpi in base alla distanza e alla folla (Enemy.Budget.cs)
        }

        static readonly Vector3[] views = new Vector3[Seats.MaxSeats];
    }
}
