using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Riflessi veri per stanza (2026-11-15): una sonda di riflessione per ogni stanza del piano, grande quanto la stanza e con
    /// la proiezione a scatola, fotografata una volta sola appena il piano e' costruito (una per fotogramma, con le luci della stanza
    /// accese: LightPool.HoldPoint), poi lasciata ferma. Prima tutte le superfici lucide (marmo a scacchi, ottone, vetro, legno
    /// laccato) riflettevano lo stesso cielo finto; ora riflettono le pareti, le finestre e le luci della loro stanza, al posto giusto
    /// (URP Forward+: fusione tra sonde e proiezione a scatola sono gia' accese in HotelURP). -noprobes per confrontare.</summary>
    public static class RoomProbes
    {
        public static int Baked { get; private set; }
        static readonly List<ReflectionProbe> probes = new List<ReflectionProbe>();

        public static IEnumerator Bake(FloorInstance fi)
        {
            Baked = 0;
            probes.Clear();
            if (fi == null || fi.plan == null || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noprobes") >= 0) yield break;
            var root = new GameObject("SondeRiflessi");
            root.transform.SetParent(fi.root.transform, false);
            int mask = ~((1 << 2) | (1 << 5) | (1 << HotelNav.EnemyLayer));   // niente modello in prima persona, interfaccia, nemici
            float t0 = Time.realtimeSinceStartup;
            foreach (var room in fi.plan.rooms)
            {
                if (room == null) continue;
                float x0 = room.x0 * 0.1f, x1 = room.x1 * 0.1f, z0 = room.z0 * 0.1f, z1 = room.z1 * 0.1f;
                float w = x1 - x0, d = z1 - z0, h = Mathf.Max(2.6f, room.wallH);
                if (w < 2f || d < 2f) continue;
                float y0 = 0f;
                if (HotelNav.Ground(new Vector3((x0 + x1) * 0.5f, 1.5f, (z0 + z1) * 0.5f), out Vector3 g)) y0 = g.y;
                Vector3 eye = new Vector3((x0 + x1) * 0.5f, y0 + Mathf.Min(1.8f, h * 0.45f), (z0 + z1) * 0.5f);
                var go = new GameObject("Sonda_" + room.id);
                go.transform.SetParent(root.transform, false);
                go.transform.position = eye;
                var p = go.AddComponent<ReflectionProbe>();
                probes.Add(p);
                p.mode = ReflectionProbeMode.Realtime;
                p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                p.resolution = w * d > 400f ? 256 : 128;
                p.hdr = true;
                p.boxProjection = true;
                // la scatola copre la stanza dal pavimento al soffitto (centro relativo alla sonda)
                p.size = new Vector3(w + 0.4f, h + 0.4f, d + 0.4f);
                p.center = new Vector3(0f, y0 + h * 0.5f - eye.y, 0f);
                p.blendDistance = 0.8f;
                p.importance = 1;
                p.intensity = 1f;
                p.cullingMask = mask;
                p.nearClipPlane = 0.1f;
                p.farClipPlane = Mathf.Max(w, d, h) * 1.6f + 5f;
                p.clearFlags = ReflectionProbeClearFlags.SolidColor;
                p.backgroundColor = RenderSettings.fogColor;
                p.shadowDistance = 0f;
                // le luci della stanza accese durante la foto (il pool tiene accese solo quelle vicine a chi gioca)
                LightPool.HoldPoint = eye;
                LightPool.Tick(false, eye);
                int id = p.RenderProbe();
                int guard = 0;
                do { yield return null; } while (!p.IsFinishedRendering(id) && ++guard < 30);
                Baked++;
            }
            LightPool.HoldPoint = null;
            LightPool.Tick(true);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] riflessi: {Baked} sonde in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
        }

        /// <summary>Rifa le foto delle sonde gia' messe (dopo 'wait' secondi): quando torna la corrente le stanze sono piu' luminose
        /// di quando sono state fotografate. Una sonda per fotogramma, con le luci della stanza tenute accese.</summary>
        public static IEnumerator Rebake(float wait)
        {
            if (wait > 0f) yield return new WaitForSeconds(wait);
            int n = 0;
            foreach (var p in probes.ToArray())
            {
                if (p == null) continue;
                Vector3 eye = p.transform.position;
                LightPool.HoldPoint = eye;
                LightPool.Tick(false, eye);
                int id = p.RenderProbe();
                int guard = 0;
                do { yield return null; } while (p != null && !p.IsFinishedRendering(id) && ++guard < 30);
                n++;
            }
            LightPool.HoldPoint = null;
            LightPool.Tick(true);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] riflessi rifatti: {n} sonde");
        }
    }
}
