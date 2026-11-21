#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly luci: "gli edifici a volte hanno ombre ... che spariscono e riappaiono" (segnalazione del 2026-11-20), dentro i luoghi. In ogni stanza solo le quattro lampade piu' vicine al giocatore fanno
    /// ombra (LightPool.Tick, ogni 0,2 s): chi sta sotto una lampada ne ha altre due a pari distanza dalle due parti, e con l'oscillazione della testa nella camminata (qualche centimetro) l'ordine tra la quarta e
    /// la quinta cambia a ogni giro: le loro ombre si accendono e si spengono cinque volte al secondo. La prova mette una fila di dodici lampade finte (4 m una dall'altra) lontano da tutto e fa girare il
    /// pool come se il giocatore cammini lungo la fila e come se stia fermo sotto una lampada, e conta i cambi di ombra di ogni lampada.</summary>
    public sealed partial class DesertTest
    {
        sealed class LightRun { public int toggles, maxLamp, maxCasters; public float metres; }

        /// <summary>Fa girare il pool lungo un percorso (un giro ogni 0,2 s di tempo simulato: tutto in un fotogramma, cosi' nessun giro vero si intromette) e conta quante volte l'ombra di una lampada cambia.</summary>
        static LightRun SimulateLightPool(List<PoolLight> lamps, System.Func<float, Vector3> path, float secs)
        {
            var r = new LightRun(); var perLamp = new int[lamps.Count]; var was = new bool[lamps.Count];
            bool first = true; Vector3 prev = path(0f);
            for (float t = 0f; t <= secs; t += 0.2f)
            {
                Vector3 p = path(t);
                r.metres += Flat(p, prev); prev = p;
                LightPool.Tick(false, p);
                int casters = 0;
                for (int i = 0; i < lamps.Count; i++)
                {
                    bool now = lamps[i].l.shadows != LightShadows.None;
                    if (now) casters++;
                    if (!first && now != was[i]) { r.toggles++; perLamp[i]++; }
                    was[i] = now;
                }
                r.maxCasters = Mathf.Max(r.maxCasters, casters);
                first = false;
            }
            foreach (int n in perLamp) r.maxLamp = Mathf.Max(r.maxLamp, n);
            return r;
        }

        IEnumerator LightShadowAudit()
        {
            const int Lamps = 12; const float Spacing = 4f;
            var root = new GameObject("LuciProva");
            Vector3 o = new Vector3(40000f, 50f, 40000f);                      // lontano da tutto: le lampade vere restano fuori da ogni raggio e si spengono per i giri di prova
            var lamps = new List<PoolLight>();
            for (int i = 0; i < Lamps; i++)
            {
                var g = new GameObject("Lampada" + i);
                g.transform.SetParent(root.transform, false);
                g.transform.position = o + new Vector3(i * Spacing, 3f, 0f);
                var l = g.AddComponent<Light>();
                l.type = LightType.Point; l.range = 12f; l.intensity = 2f;
                lamps.Add(LightPool.Register(l, true));
            }
            yield return null;
            float bob = 11.9f;                                                  // 1,9 passi al secondo
            // a) a piedi lungo la fila: 30 s a 1,4 m/s
            var walk = SimulateLightPool(lamps, t => o + new Vector3(1f + 1.4f * t, 1.6f + 0.03f * Mathf.Sin(t * bob), 1.5f), 30f);
            // b) fermo sotto la quinta lampada (a 20 m dall'inizio della fila), con l'oscillazione della testa e un filo di respiro: 10 s
            var stand = SimulateLightPool(lamps, t => o + new Vector3(5f * Spacing + 0.02f * Mathf.Sin(t * 7.3f), 1.6f + 0.03f * Mathf.Sin(t * bob), 1.5f + 0.02f * Mathf.Cos(t * 5.1f)), 10f);
            // c) fermo tra due lampade (a pari distanza): 10 s
            var between = SimulateLightPool(lamps, t => o + new Vector3(5f * Spacing + Spacing * 0.5f + 0.02f * Mathf.Sin(t * 7.3f), 1.6f + 0.03f * Mathf.Sin(t * bob), 1.5f + 0.02f * Mathf.Cos(t * 5.1f)), 10f);
            Debug.Log($"[PryingEyes] desertotest luci: a piedi lungo {walk.metres:F0} m {walk.toggles} cambi di ombra ({walk.toggles / Mathf.Max(1f, walk.metres):F2} al metro, al massimo {walk.maxLamp} per una lampada, mai piu' di {walk.maxCasters} lampade con l'ombra); fermo sotto una lampada per 10 s {stand.toggles} cambi (al massimo {stand.maxLamp} per una lampada); fermo tra due lampade per 10 s {between.toggles} cambi (al massimo {between.maxLamp})");
            Check("luci: lungo la fila non ci sono mai piu' ombre del massimo (4)", walk.maxCasters <= LightPool.MaxShadows && stand.maxCasters <= LightPool.MaxShadows && between.maxCasters <= LightPool.MaxShadows, $"{walk.maxCasters}, {stand.maxCasters}, {between.maxCasters} lampade con l'ombra");
            Check("luci: fermi sotto una lampada le ombre non lampeggiano (al massimo 2 cambi in 10 s)", stand.toggles <= 2, $"{stand.toggles} cambi, una lampada fino a {stand.maxLamp}");
            Check("luci: fermi tra due lampade le ombre non lampeggiano (al massimo 2 cambi in 10 s)", between.toggles <= 2, $"{between.toggles} cambi, una lampada fino a {between.maxLamp}");

            foreach (var e in lamps) LightPool.Remove(e);
            Object.Destroy(root);
            LightPool.Tick(true);                                               // le lampade vere tornano com'erano (il pool le ha spente per i giri di prova)
            yield return null;
        }
    }
}
#endif
