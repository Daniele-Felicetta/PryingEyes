#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly scale: la scalinata della cattedrale (dodici gradini da 20 cm di alzata e 40 di pedata, larga 14 m, tra il sagrato e la sabbia; dopo la moto e' la cosa che si usa di piu') vista da quattro
    /// parti. (1) La fisica contro il disegno: su una griglia di raggi dall'alto, dove le scatole nascoste (la fisica) stanno piu' in alto o piu' in basso della pietra che si vede, e' un gradino che non c'e' o
    /// un gradino che non si sente; le guance sono fatte apposta a gradoni sotto il disegno (non devono mai stare sopra). (2) Il suolo vero (tutti i collider, la sabbia compresa) lungo l'asse e ai piedi:
    /// la quota di ogni gradino, e niente gradino ne' buco tra l'ultimo e la sabbia. (3) Chi la scende e la sale a piedi, dritto e di traverso, a 60 fotogrammi al secondo: arriva? resta in aria? di quanto
    /// salta la camera da un fotogramma all'altro? (4) Con -desertshots, otto fotografie (dall'alto, a meta', dal basso, di lato, da vicino, da lontano, e due a sole basso) per vedere quello che i numeri non dicono.</summary>
    public sealed partial class DesertTest
    {
        sealed class StairWalk { public float camJumpMax, camJump95, bodyJumpMax, secs; public int frames, airFrames, bigJumps; public bool arrived; }

        /// <summary>Scende (o sale) la scalinata a piedi come il giocatore con la levetta: dritta per il mezzo (lateral 0) o di traverso (partendo di 'lateral' metri di lato, verso l'altro lato), e misura
        /// la camera sui gradini (da z -6,4 a 0,8).</summary>
        IEnumerator StairWalkRun(bool down, float lateral, StairWalk r, bool sprint = false)
        {
            var pl = Pl;
            float cx = (HubPlan.StairX0 + HubPlan.StairX1) * 0.5f;
            var cc = pl.GetComponent<CharacterController>();
            // la rotta: lateral > 0 parte a sinistra dell'asse e finisce sull'asse (25 gradi di traverso), in discesa e in salita
            float side = lateral > 0f ? 1f : 0f;
            Vector3 from = down ? new Vector3(cx + lateral * side, 0.06f, 1.4f) : new Vector3(cx - lateral * side, DesertWorld.GroundY + 0.06f, -9.5f);
            Vector3 dir = lateral > 0f ? new Vector3((down ? -1f : 1f) * Mathf.Sin(25f * Mathf.Deg2Rad), 0f, (down ? -1f : 1f) * Mathf.Cos(25f * Mathf.Deg2Rad)) : (down ? Vector3.back : Vector3.forward);
            pl.Teleport(from);
            // dopo il teletrasporto il regista sveglia le guardie del sagrato: una che si para davanti blocca la camminata (non e' la scala che si misura)
            ClearHorde();
            yield return new WaitForSeconds(0.4f);
            PlayerController.holdLook = false;
            PlayerController.botSprint = sprint;          // di corsa (5,9 m/s invece di 3,9): a ogni gradino da 40 cm la capsula cade 20 cm in piu' tempo di quanto ne serva a passarlo
            float t0 = Time.time, prevCam = pl.cam.position.y, prevBody = pl.transform.position.y, nextClear = Time.time + 1f;
            var jumps = new List<float>();
            while (Time.time - t0 < 25f && (down ? pl.transform.position.z > -9f : pl.transform.position.z < 1.4f))
            {
                PlayerController.botDir = dir;
                if (Time.time >= nextClear) { ClearHorde(true); nextClear = Time.time + 1f; }
                float z = pl.transform.position.z, cy = pl.cam.position.y, by = pl.transform.position.y;
                // un fotogramma lungo (macchina carica, caricamento di un pezzo di sabbia) sposta il corpo, e quindi la camera, di piu': lo scatto si riporta a un fotogramma da 60 al secondo
                float k = Mathf.Min(1f, (1f / 60f) / Mathf.Max(Time.deltaTime, 1e-4f));
                if (z > -6.4f && z < 0.8f)
                {
                    r.frames++;
                    if (!cc.isGrounded) r.airFrames++;
                    float j = Mathf.Abs(cy - prevCam) * k;
                    jumps.Add(j);
                    r.camJumpMax = Mathf.Max(r.camJumpMax, j);
                    r.bodyJumpMax = Mathf.Max(r.bodyJumpMax, Mathf.Abs(by - prevBody) * k);
                    if (j > 0.1f) r.bigJumps++;
                }
                prevCam = cy; prevBody = by;
                yield return null;
            }
            r.secs = Time.time - t0;
            r.arrived = down ? pl.transform.position.z <= -9f : pl.transform.position.z >= 1.4f;
            PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true; PlayerController.botSprint = false;
            jumps.Sort();
            r.camJump95 = jumps.Count > 0 ? jumps[Mathf.Min(jumps.Count - 1, Mathf.FloorToInt(jumps.Count * 0.95f))] : 0f;
        }

        IEnumerator CathedralStairsAudit()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            // in catena la fase che precede (motopiano) lascia il giocatore in sella, dove ha finito la corsa: le 'camminate' di sotto sarebbero corse in moto (il registro scriveva 'teleporto in sella',
            // la moto con la levetta andava per i fatti suoi e due camminate su quattro non arrivavano in 25 s); si scende, a piedi la fase fa sempre lo stesso a qualunque fase la preceda
            if (pl.Riding) { pl.RideRequestDismount(); yield return WaitFor(() => !pl.Riding, 10f); yield return new WaitForSeconds(0.6f); }
            Check("scale: il giocatore e' a piedi (non in sella)", !pl.Riding);
            ClearHorde();
            var root = GameObject.Find("Basamento e scalinata");
            Check("scale: c'e' la scalinata (Basamento e scalinata)", root != null);
            if (root == null) yield break;
            var pietra = root.transform.Find("Pietra");
            var mf = pietra != null ? pietra.GetComponent<MeshFilter>() : null;
            Check("scale: il disegno della pietra e' una mesh leggibile", mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable);
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) yield break;

            float xa = HubPlan.StairX0, xb = HubPlan.StairX1, zEnd = -1f - 0.4f * DesertCathedral.Steps;
            float cxm = (xa + xb) * 0.5f, groundY = DesertWorld.GroundY;
            // Stream the sand under the stairs before comparing its surface with the stone.
            pl.Teleport(new Vector3(cxm, 0.06f, 1.4f));
            var terr0 = DesertTerrain.I;
            yield return WaitFor(() => terr0 == null || terr0.Ready(new Vector3(cxm, groundY, zEnd - 4f), 12f), 30f);
            yield return new WaitForSeconds(0.5f);

            // ---- 1. la fisica contro il disegno: un collider di prova sulla stessa mesh, raggi dall'alto
            var vgo = new GameObject("PietraProva");
            vgo.transform.SetParent(root.transform, false);
            var vis = vgo.AddComponent<MeshCollider>();
            vis.sharedMesh = mf.sharedMesh;
            Physics.SyncTransforms();
            int treads = 0, treadBad = 0, cheeks = 0, cheekAbove = 0, missing = 0, capSkipped = 0;
            int footSamples = 0;
            float minSandClearance = float.PositiveInfinity;
            float treadWorst = 0f, cheekAboveWorst = 0f, cheekBelowWorst = 0f;
            var sb = new StringBuilder();
            for (float x = xa - 0.45f; x <= xb + 0.45f; x += 0.35f)
                for (float z = 1.2f; z >= zEnd - 0.5f; z -= 0.1f)
                {
                    // il cappello di ogni pilastro al piede (62 cm di lato, 12 cm piu' largo della scatola di fisica, un metro e venti sopra l'ultimo gradino) sporge di qualche centimetro da tutti i lati: non e' un gradino
                    if (z < zEnd + 0.2f && z > zEnd - 0.55f && (x < xa + 0.2f || x > xb - 0.2f)) { capSkipped++; continue; }
                    var from = new Vector3(x, 3f, z);
                    float yv = float.NaN, yp = float.NaN, sandY = float.NegativeInfinity;
                    if (vis.Raycast(new Ray(from, Vector3.down), out var hv, 8f)) yv = hv.point.y;
                    foreach (var h in Physics.RaycastAll(from, Vector3.down, 8f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                    {
                        if (h.collider != vis && h.collider.transform.parent == root.transform && (float.IsNaN(yp) || h.point.y > yp)) yp = h.point.y;
                        if (DesertTerrain.IsSand(h.collider)) sandY = Mathf.Max(sandY, h.point.y);
                    }
                    if (x > xa + 0.5f && x < xb - 0.5f && z > zEnd + 0.05f && z < zEnd + 0.35f
                        && !float.IsNaN(yv) && !float.IsNegativeInfinity(sandY))
                    {
                        footSamples++;
                        minSandClearance = Mathf.Min(minSandClearance, yv - sandY);
                    }
                    if (float.IsNaN(yv) || float.IsNaN(yp))
                    {
                        // fuori dalla larghezza dei gradini il cornicione di pietra sporge di 12 cm dal basamento (disegno senza fisica): non conta
                        if (!(float.IsNaN(yv) && float.IsNaN(yp))) { if (x >= xa && x <= xb) { missing++; if (sb.Length < 500) sb.Append($" [in {x:F2},{z:F2}: disegno {(float.IsNaN(yv) ? "niente" : yv.ToString("F2"))}, fisica {(float.IsNaN(yp) ? "niente" : yp.ToString("F2"))}]"); } else capSkipped++; }
                        continue;
                    }
                    bool onTread = x >= xa && x <= xb;
                    float d = yp - yv;
                    if (onTread)
                    {
                        treads++;
                        if (Mathf.Abs(d) > treadWorst) treadWorst = Mathf.Abs(d);
                        if (Mathf.Abs(d) > 0.06f) { treadBad++; if (sb.Length < 500) sb.Append($" [gradino in {x:F2},{z:F2}: fisica {yp:F2}, disegno {yv:F2}]"); }
                    }
                    else
                    {
                        cheeks++;
                        if (d > cheekAboveWorst) cheekAboveWorst = d;
                        if (-d > cheekBelowWorst) cheekBelowWorst = -d;
                        if (d > 0.06f) { cheekAbove++; if (sb.Length < 500) sb.Append($" [guancia in {x:F2},{z:F2}: fisica {yp:F2} sopra il disegno {yv:F2}]"); }
                    }
                }
            vgo.SetActive(false); Object.Destroy(vgo);          // Destroy lavora a fine fotogramma: spento subito, o i raggi del suolo qui sotto ne vedrebbero ancora il naso dei gradini (+3 cm)
            Physics.SyncTransforms();
            Debug.Log($"[PryingEyes] desertotest scale: fisica contro disegno: {treads} punti sui gradini (scarto massimo {treadWorst * 100f:F1} cm, {treadBad} oltre 6 cm), {cheeks} sulle guance (la fisica sopra il disegno al massimo di {cheekAboveWorst * 100f:F1} cm, {cheekAbove} oltre 6 cm; sotto fino a {cheekBelowWorst * 100f:F0} cm), {missing} con uno solo dei due, {capSkipped} sotto i cappelli dei pilastri e i cornicioni (non contati){sb}");
            Check("scale: sui gradini la fisica e il disegno stanno alla stessa quota (6 cm)", treads > 500 && treadBad == 0, $"{treads} punti, {treadBad} oltre 6 cm, scarto massimo {treadWorst * 100f:F1} cm");
            Check("scale: sulle guance la fisica non sta mai sopra il disegno (6 cm)", cheeks > 100 && cheekAbove == 0, $"{cheeks} punti, {cheekAbove} oltre 6 cm, al massimo {cheekAboveWorst * 100f:F1} cm");
            Check("scale: dove c'e' il disegno c'e' la fisica (e viceversa)", missing == 0, $"{missing} punti con uno solo dei due");
            Check("scale: the bottom tread is at least 1 cm above the sand", footSamples >= 50 && minSandClearance >= 0.01f,
                $"{footSamples} points, minimum clearance {minSandClearance * 100f:F2} cm");

            // ---- 2. il suolo vero (tutti i collider dell'ambiente, la sabbia compresa) lungo l'asse della scalinata e ai suoi piedi: dove la quota non e' quella dei gradini c'e' un gradino che non si vede
            int axisN = 0, axisBad = 0; float axisWorst = 0f;
            var sb3 = new StringBuilder();
            for (float z = -0.02f; z > zEnd + 0.01f; z -= 0.05f)
            {
                float rel = (-1f - z) / 0.4f, fr = rel - Mathf.Floor(rel);
                if (z < -1f ? (fr < 0.05f || fr > 0.95f) : Mathf.Abs(z + 1f) < 0.02f) continue;      // vicino ai piani delle alzate la quota cambia di 20 cm: non conta
                float expect = z >= -1f ? 0f : -0.2f * Mathf.Ceil(rel), y = TopY(cxm, z);
                axisN++;
                if (float.IsNaN(y)) { axisBad++; if (sb3.Length < 400) sb3.Append($" [in {z:F2}: niente]"); continue; }
                float d = Mathf.Abs(y - expect);
                axisWorst = Mathf.Max(axisWorst, d);
                if (d > 0.03f) { axisBad++; if (sb3.Length < 400) sb3.Append($" [in {z:F2}: {y:F2} invece di {expect:F2}]"); }
            }
            float footLip = TopY(cxm, zEnd + 0.1f) - TopY(cxm, zEnd - 0.1f);
            int footN = 0, footHoles = 0; float footMin = float.MaxValue, footMax = float.MinValue;
            for (float x = xa - 3f; x <= xb + 3f; x += 1f)
                for (float z = zEnd - 0.1f; z >= zEnd - 8f; z -= 0.5f)
                {
                    if (z > zEnd - 0.7f && (x < xa + 0.3f && x > xa - 0.7f || x > xb - 0.3f && x < xb + 0.7f)) continue;      // i due pilastri al piede (un metro e venti sulla sabbia, in vista)
                    float y = TopY(x, z);
                    footN++;
                    if (float.IsNaN(y)) { footHoles++; continue; }
                    footMin = Mathf.Min(footMin, y - groundY); footMax = Mathf.Max(footMax, y - groundY);
                }
            Debug.Log($"[PryingEyes] desertotest scale: il suolo lungo l'asse: {axisN} punti, scarto massimo dai gradini {axisWorst * 100f:F1} cm, {axisBad} oltre 3 cm{sb3}; ai piedi (sabbia a {groundY:F2}): {footN} punti, {footHoles} senza collider, quota da {footMin * 100f:F1} a {footMax * 100f:F1} cm sul piano della sabbia, l'ultimo gradino rispetto alla sabbia {footLip * 100f:F1} cm");
            Check("scale: lungo l'asse la quota del suolo e' quella dei gradini (3 cm)", axisN > 80 && axisBad == 0, $"{axisN} punti, {axisBad} oltre 3 cm, scarto massimo {axisWorst * 100f:F1} cm");
            Check("scale: ai piedi della scalinata la sabbia non fa gradino (3 cm) ne' buchi", footHoles == 0 && Mathf.Abs(footLip) <= 0.03f, $"{footHoles} buchi su {footN}, gradino {footLip * 100f:F1} cm");
            Check("scale: davanti alla scalinata la sabbia sta al suo piano (5 cm)", footMin >= -0.05f && footMax <= 0.05f, $"da {footMin * 100f:F1} a {footMax * 100f:F1} cm");

            // ---- 3. a piedi, a 60 fotogrammi al secondo: dritti per il mezzo e di traverso (25 gradi, da 5 m di lato), in discesa e in salita
            float fps = FlatArg("-stairsfps", 60f);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = Mathf.RoundToInt(fps);
            var downS = new StairWalk(); var upS = new StairWalk(); var downD = new StairWalk(); var upD = new StairWalk();
            yield return StairWalkRun(true, 0f, downS);
            yield return StairWalkRun(false, 0f, upS);
            yield return StairWalkRun(true, 5f, downD);
            yield return StairWalkRun(false, 5f, upD);
            var downR = new StairWalk(); var upR = new StairWalk();
            yield return StairWalkRun(true, 0f, downR, true);
            yield return StairWalkRun(false, 0f, upR, true);
            Application.targetFrameRate = -1;
            void Line(string name, StairWalk w) => Debug.Log($"[PryingEyes] desertotest scale: a piedi {name}: {w.frames} fotogrammi in {w.secs:F1} s sui gradini, salto massimo della camera da un fotogramma all'altro {w.camJumpMax * 100f:F1} cm (95% {w.camJump95 * 100f:F1}), {w.bigJumps} fotogrammi oltre 10 cm, il corpo al massimo {w.bodyJumpMax * 100f:F1} cm, in aria {w.airFrames} fotogrammi, {(w.arrived ? "arrivato" : "NON arrivato")}");
            Line("in discesa, dritto", downS); Line("in salita, dritto", upS); Line("in discesa, di traverso", downD); Line("in salita, di traverso", upD);
            Line("di corsa in discesa, dritto", downR); Line("di corsa in salita, dritto", upR);
            Check("scale: si scende e si sale a piedi, dritti e di traverso", downS.arrived && upS.arrived && downD.arrived && upD.arrived, $"{downS.secs:F1}, {upS.secs:F1}, {downD.secs:F1} e {upD.secs:F1} s");
            Check("scale: si scende e si sale anche di corsa", downR.arrived && upR.arrived, $"{downR.secs:F1} e {upR.secs:F1} s");
            float jumpWorst = Mathf.Max(Mathf.Max(downS.camJumpMax, upS.camJumpMax), Mathf.Max(downD.camJumpMax, upD.camJumpMax));
            Check("scale: sui gradini la camera non salta mai di piu' di 12 cm da un fotogramma all'altro (a 60 al secondo)", jumpWorst <= 0.12f, $"al massimo {jumpWorst * 100f:F1} cm");
            Check("scale: sui gradini non si resta in aria (meno di un fotogramma su venti)", downS.airFrames + upS.airFrames + downD.airFrames + upD.airFrames <= (downS.frames + upS.frames + downD.frames + upD.frames) / 20, $"{downS.airFrames + upS.airFrames + downD.airFrames + upD.airFrames} su {downS.frames + upS.frames + downD.frames + upD.frames}");

            // ---- 4. le fotografie (solo con -desertshots): dall'alto, a meta', dal basso, di lato, da vicino, da lontano, e due a sole basso (le ombre dei gradini)
            if (shots)
            {
                PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
                float sy = groundY + 0.06f;
                var views = new (string name, float hour, Vector3 from, Vector3 look)[]
                {
                    ("scale_1_dall_alto", 11.5f, new Vector3(cxm, 0.06f, 0.6f), new Vector3(cxm, groundY, -9f)),
                    ("scale_2_a_meta", 11.5f, new Vector3(cxm, -1.14f, -3.2f), new Vector3(cxm, groundY, -12f)),
                    ("scale_3_dal_basso", 11.5f, new Vector3(cxm, sy, -9f), new Vector3(cxm, 0.5f, 0f)),
                    ("scale_4_di_fianco", 11.5f, new Vector3(xa - 8f, sy, -9f), new Vector3(cxm - 3f, -1.2f, -3f)),
                    ("scale_5_da_vicino", 11.5f, new Vector3(cxm - 2.5f, 0.06f, -0.4f), new Vector3(cxm - 0.5f, -0.5f, -2.6f)),
                    ("scale_6_da_lontano", 11.5f, new Vector3(cxm, sy, -45f), new Vector3(cxm, 1f, 0f)),
                    ("scale_7_sera_dal_basso", 17.3f, new Vector3(cxm + 1f, sy, -12f), new Vector3(cxm, -1.2f, -3f)),
                    ("scale_8_sera_dall_alto", 17.3f, new Vector3(cxm, 0.06f, 0.6f), new Vector3(cxm, groundY, -9f)),
                };
                foreach (var v in views)
                {
                    pl.Teleport(v.from);
                    var terr = DesertTerrain.I;
                    yield return WaitFor(() => terr == null || terr.Ready(v.from, 0f), 30f);
                    yield return new WaitForSeconds(0.5f);
                    yield return Snap(v.name, v.hour, v.look);
                }
            }
            godMode = true;
        }

        /// <summary>La quota piu' alta dei collider dell'ambiente (la sabbia compresa) sotto il punto, o NaN se non c'e' niente.</summary>
        static float TopY(float x, float z)
        {
            float best = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(x, 8f, z), Vector3.down, 30f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
            return best;
        }
    }
}
#endif
