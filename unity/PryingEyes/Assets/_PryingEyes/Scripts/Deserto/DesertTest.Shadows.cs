#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly ombre: "gli edifici a volte hanno ombre che spariscono e riappaiono" (segnalazione del 2026-11-20). Il deserto allunga la distanza delle ombre del sole (da 70 a 210 m: le dune e le torri
    /// hanno ombre lunghe) e la riporta a quella della stanza quando si sta sotto un tetto (DesertSky: un raggio in alto ogni 0,12 s). Prima passava dall'una all'altra in un fotogramma: a ogni porta, a ogni
    /// tettoia, tutte le ombre oltre i 70 m sparivano di botto e, uscendo, ricomparivano di botto. La prova mette un tetto finto sopra la testa e legge la distanza vera dell'asset della pipeline a ogni
    /// fotogramma: di quanto cambia in 50 ms (il massimo), se scende davvero alla distanza della stanza, se risale uscendo, se una tettoia di passaggio (0,3 s) la lascia in pace e se non oscilla.</summary>
    public sealed partial class DesertTest
    {
        /// <summary>Registra (tempo, distanza delle ombre) a ogni fotogramma per 'secs' secondi.</summary>
        static IEnumerator RecordShadow(UniversalRenderPipelineAsset urp, float secs, List<Vector2> log)
        {
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < secs) { log.Add(new Vector2(Time.unscaledTime, urp.shadowDistance)); yield return null; }
        }

        /// <summary>Il massimo cambio di distanza in una finestra di 'window' secondi.</summary>
        static float ShadowMaxChange(List<Vector2> log, float window)
        {
            float worst = 0f; int j = 0;
            for (int i = 0; i < log.Count; i++)
            {
                while (log[i].x - log[j].x > window) j++;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = j; k <= i; k++) { lo = Mathf.Min(lo, log[k].y); hi = Mathf.Max(hi, log[k].y); }
                worst = Mathf.Max(worst, hi - lo);
            }
            return worst;
        }

        /// <summary>Quante volte la distanza cambia verso (di almeno 2 m alla volta: l'ultimo metro della discesa di prima, quando il tetto e' gia' via, non e' un'oscillazione): una discesa o una salita pulita ne ha zero.</summary>
        static int ShadowReversals(List<Vector2> log)
        {
            int rev = 0, dir = 0; float anchor = log.Count > 0 ? log[0].y : 0f;
            foreach (var s in log)
            {
                float d = s.y - anchor;
                if (Mathf.Abs(d) < 2f) continue;
                int nd = d > 0f ? 1 : -1;
                if (dir != 0 && nd != dir) rev++;
                dir = nd; anchor = s.y;
            }
            return rev;
        }

        static GameObject MakeRoof(Vector3 eye)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "TettoiaProva";
            g.transform.position = eye + Vector3.up * 3f;
            g.transform.localScale = new Vector3(8f, 0.3f, 8f);
            var mr = g.GetComponent<MeshRenderer>(); if (mr != null) Object.Destroy(mr);
            Physics.SyncTransforms();
            return g;
        }

        IEnumerator ShadowRangeAudit()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            ClearHorde();
            var sky = DesertSky.I;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Check("ombre: c'e' il cielo del deserto", sky != null);
            Check("ombre: c'e' l'asset della pipeline URP", urp != null);
            if (sky == null || urp == null) yield break;
            sky.SetHour(11f, true);                                    // di giorno, il tempo fermo
            var open = OpenSand(300f);
            yield return GoTo(open.x, open.y, 2f);
            yield return new WaitForSeconds(3f);                       // all'aria aperta da un po'
            float outDist = urp.shadowDistance, roomDist = outDist / DesertSky.OutdoorShadowRange;
            Debug.Log($"[PryingEyes] desertotest ombre: all'aperto le ombre vanno a {outDist:F0} m, in una stanza a {roomDist:F0} m (il cielo dice che la camera {(sky.IndoorFor(pl.cam.GetComponent<Camera>()) > 0.01f ? "e' al chiuso" : "e' all'aperto")})");

            // a) entrare: un tetto a tre metri sopra la testa, quattro secondi
            var enter = new List<Vector2>();
            var roof = MakeRoof(pl.cam.position);
            yield return RecordShadow(urp, 4f, enter);
            float inDist = urp.shadowDistance;
            // b) uscire: via il tetto, quattro secondi
            Object.Destroy(roof);
            var leave = new List<Vector2>();
            yield return RecordShadow(urp, 4f, leave);
            float backDist = urp.shadowDistance;
            // c) una tettoia di passaggio: 0,3 s sopra la testa
            var brief = new List<Vector2>();
            roof = MakeRoof(pl.cam.position);
            float tb = Time.unscaledTime;
            while (Time.unscaledTime - tb < 0.3f) { brief.Add(new Vector2(Time.unscaledTime, urp.shadowDistance)); yield return null; }
            Object.Destroy(roof);
            yield return RecordShadow(urp, 2.5f, brief);
            float briefMin = outDist; foreach (var s in brief) briefMin = Mathf.Min(briefMin, s.y);

            float enterStep = ShadowMaxChange(enter, 0.05f), leaveStep = ShadowMaxChange(leave, 0.05f);
            int enterRev = ShadowReversals(enter), leaveRev = ShadowReversals(leave);
            Debug.Log($"[PryingEyes] desertotest ombre: entrando {outDist:F0} -> {inDist:F0} m (al massimo {enterStep:F1} m in 50 ms, {enterRev} inversioni), uscendo {inDist:F0} -> {backDist:F0} m (al massimo {leaveStep:F1} m in 50 ms, {leaveRev} inversioni), una tettoia di 0,3 s: la distanza scende al minimo a {briefMin:F0} m ({enter.Count + leave.Count + brief.Count} fotogrammi registrati)");
            Check("ombre: sotto un tetto la distanza delle ombre scende a quella della stanza (3%)", Mathf.Abs(inDist - roomDist) <= roomDist * 0.03f, $"{inDist:F1} m, la stanza {roomDist:F1} m");
            Check("ombre: uscendo torna a quella del deserto (3%)", Mathf.Abs(backDist - outDist) <= outDist * 0.03f, $"{backDist:F1} m, il deserto {outDist:F1} m");
            Check("ombre: entrando e uscendo la distanza non cambia di piu' di 25 m in 50 ms (a scatto erano 140)", enterStep <= 25f && leaveStep <= 25f, $"entrando {enterStep:F1} m, uscendo {leaveStep:F1} m");
            Check("ombre: la distanza va in un verso solo, senza oscillare", enterRev == 0 && leaveRev == 0, $"{enterRev} + {leaveRev} inversioni");
            Check("ombre: una tettoia di passaggio (0,3 s) non le tocca (3%)", briefMin >= outDist * 0.97f, $"scende al minimo a {briefMin:F1} m su {outDist:F1}");
            sky.Frozen = false;
            godMode = true;
        }
    }
}
#endif
