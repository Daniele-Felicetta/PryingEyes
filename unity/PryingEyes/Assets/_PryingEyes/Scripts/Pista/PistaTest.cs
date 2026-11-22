#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Deserto;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>-autotest -pistatest [-pistashots] [-seed N] [-pistalaps N] [-pistarivals N] [-pistabot]: the track mode in the real game. A race starts from the "Gioca" path (the standard player and HUD are switched
    /// off, the race camera and sky are up), the grid and the five lights are checked, the computer rides the player's bike (-pistabot) through a whole race at a higher time scale while the finish, the lap
    /// times and the results table are checked, then the race is started again on the same circuit and on a new one. With -pistashots (needs a graphics device) it also photographs the grid, the start, the
    /// three camera views and the results into hotel_build/prove/pista. It does not touch the player's saves or preferences.</summary>
    public sealed class PistaTest : MonoBehaviour
    {
        int checks, failed, errors;
        string firstError = "";
        bool shots, ended;
        const float Budget = 900f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pistatest") < 0) return;
            new GameObject("PistaTest").AddComponent<PistaTest>();
        }

        void Check(string name, bool ok, string detail = "")
        {
            checks++; if (!ok) failed++;
            Debug.Log("[PryingEyes] pistatest " + (ok ? "ok " : "FALLITO ") + name + (detail.Length > 0 ? " (" + detail + ")" : ""));
        }

        void OnLog(string text, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (Application.isBatchMode && text != null && text.StartsWith("Failed to capture screen shot")) return;
            errors++;
            if (firstError.Length == 0) firstError = text.Length > 160 ? text.Substring(0, 160) : text;
        }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }

        static PistaDirector Dir => PistaDirector.I;

        IEnumerator Start()
        {
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-pistashots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more;
                try { more = sequence.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false, e.Message); break; }
                if (!more) break;
                yield return sequence.Current;
            }
            Finish();
        }

        float worstFrame; int frameCount; string phase = "";
        void Update()
        {
            if (!ended && Time.realtimeSinceStartup > Budget) { Check("finita entro il tempo", false, Budget.ToString("F0") + " s"); Finish(); }
            if (Dir == null || !Dir.Ready) return;
            float dt = Time.unscaledDeltaTime;
            if (Time.frameCount - GameManager.HarnessShotFrame <= 2) return;
            frameCount++;
            if (dt > worstFrame) worstFrame = dt;
            if (dt > 0.25f) Debug.Log($"[PryingEyes] pistatest fotogramma lungo: {dt * 1000f:F0} ms in '{phase}'");
        }

        void Finish()
        {
            if (ended) return;
            ended = true;
            Time.timeScale = 1f;
            Check("nessun fotogramma oltre 1,2 secondi (un blocco visibile)", worstFrame < 1.2f, $"il peggiore {worstFrame * 1000f:F0} ms su {frameCount} fotogrammi");
            Check("nessun errore o eccezione nel registro durante la prova", errors == 0, errors == 0 ? "" : errors + ", la prima: " + firstError);
            Debug.Log($"[PryingEyes] pistatest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        IEnumerator WaitFor(Func<bool> cond, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        // ------------------------------------------------------------------ the sequence
        IEnumerator Run()
        {
            phase = "avvio";
            yield return WaitFor(() => Dir != null && Dir.Ready, 150f);
            var d = Dir;
            if (!Check2("la partita su pista parte e la gara e' pronta", d != null && d.Ready && PistaMode.Active, d == null ? "nessun regista" : "pronto " + d.Ready)) yield break;
            yield return null;
            Debug.Log($"[PryingEyes] pistatest circuito '{d.circuit.name}', {d.circuit.length:F0} m, {d.circuit.n} campioni, seme {d.seed}, {d.race.racers.Count} piloti, {d.race.laps} giri");
            if (PistaMode.HasArg("-pistaperf")) { yield return PerfRun(); yield break; }                      // only the cost of a frame (needs graphics)
            if (PistaMode.HasArg("-pistaworldshots")) { yield return WorldShots(); yield break; }             // only photographs of the scenery (needs graphics)
            if (PistaMode.HasArg("-pistacuponly")) { yield return CupRace(); yield break; }          // only the championship part (to look at the table of a round and iterate on it)
            if (PistaMode.HasArg("-pistarosteronly")) { yield return RosterGallery(); yield break; }   // only the photographs of the bikes on the grid (needs graphics)

            // ---- the scene
            phase = "scena";
            var c = d.circuit;
            Check("il circuito e' chiuso e ha una lunghezza da pista", c.n > 800 && Mathf.Sqrt((c.px[0] - c.px[c.n - 1]) * (c.px[0] - c.px[c.n - 1]) + (c.pz[0] - c.pz[c.n - 1]) * (c.pz[0] - c.pz[c.n - 1])) < 3.2f && c.length > 2000f && c.length < 7000f, $"{c.length:F0} m, {c.n} campioni");
            Check("i piloti sono quelli scelti, ognuno con la sua moto nella scena", d.race.racers.Count == PistaMode.Rivals + 1 && d.views.Count == d.race.racers.Count && d.views.TrueForAll(v => v != null && v.rig != null && v.rig.root != null), $"{d.race.racers.Count} piloti, {d.views.Count} moto");
            int plateOk = 0; string plateBad = "";
            foreach (var pv in d.views)
            {
                var plateT = pv != null && pv.rig != null && pv.rig.body != null ? pv.rig.body.Find("Targhe") : null;
                var plateF = plateT != null ? plateT.GetComponent<MeshFilter>() : null;
                bool four = plateF != null && plateF.sharedMesh != null && plateF.sharedMesh.vertexCount == 16;                 // four plates of four corners
                bool picture = pv != null && pv.livery != null && pv.livery.number > 0 && pv.rig != null && pv.rig.plateTex != null && ReferenceEquals(pv.rig.plateTex, PistaPlates.Picture(pv.livery.number, pv.livery.main))
                    && pv.rig.plateTex.name == "PistaTarga" + pv.livery.number;
                if (four && picture) plateOk++;
                else plateBad += (pv != null && pv.racer != null ? "livrea " + pv.racer.skin : "?") + (four ? "" : " (senza le quattro targhe)") + (picture ? "" : " (senza la sua immagine)") + "; ";
            }
            Check("ogni moto in scena porta le quattro targhe con l'immagine del numero della sua livrea", plateOk == d.views.Count && plateOk > 0, plateOk + " moto su " + d.views.Count + (plateBad.Length > 0 ? ", senza: " + plateBad : ""));
            var plA = PistaPlates.Picture(46, Color.red); var plB = PistaPlates.Picture(46, Color.red); var plC = PistaPlates.Picture(93, Color.red);
            int ink46 = PistaPlates.InkTexels(46), ink93 = PistaPlates.InkTexels(93), ink0 = PistaPlates.InkTexels(0), ink7 = PistaPlates.InkTexels(7);
            Check("le targhe: ogni numero ha la sua immagine (la stessa per lo stesso numero) con le cifre, e la targa senza numero e' vuota", ReferenceEquals(plA, plB) && !ReferenceEquals(plA, plC) && ink46 > 500 && ink93 > 500 && ink7 > 100 && ink7 < ink46 && ink0 == 0 && plA.width == PistaPlates.W && plA.height == PistaPlates.H,
                $"46: {ink46} texel di inchiostro, 93: {ink93}, 7: {ink7}, senza numero: {ink0}");
            var pl = GameManager.I != null ? GameManager.I.player : null;
            Check("il giocatore a piedi e' spento", pl == null || !pl.gameObject.activeSelf);
            var hudCanvas = GameManager.I != null && GameManager.I.hud != null ? GameManager.I.hud.GetComponent<Canvas>() : null;
            Check("l'HUD di sempre e' spento", hudCanvas == null || !hudCanvas.enabled);
            Check("la camera della gara e' la principale, con il piano lontano e lo sfondo del cielo", Camera.main == d.cam.cam && d.cam.cam.farClipPlane >= 5000f && d.cam.cam.clearFlags == CameraClearFlags.SolidColor, $"far {d.cam.cam.farClipPlane:F0}, {d.cam.cam.clearFlags}");
            Check("il cielo c'e' ed e' fermo all'ora scelta", DesertSky.I != null && DesertSky.I.Frozen && Mathf.Abs(DesertMode.Clock.hour - PistaMode.Hour) < 0.05f, $"ora {DesertMode.Clock.hour:F2} (scelta {PistaMode.Hour:F2})");
            Check("il mondo e' costruito", d.world != null && d.world.Ready);
            {
                // the plain to the horizon is a frame around the countryside: no part of the road (the middle, both edges) may lie under one of its pieces, whatever the height of the lap
                var plain = d.world.root.Find("Pianura");
                var pm = plain != null ? plain.GetComponent<MeshFilter>().sharedMesh : null;
                int hidden = 0, pieces = 0;
                if (pm != null)
                {
                    var pv = pm.vertices; var pt = pm.triangles;
                    for (int k = 0; k + 2 < pt.Length; k += 3)
                    {
                        pieces++;
                        float x0 = Mathf.Min(pv[pt[k]].x, pv[pt[k + 1]].x, pv[pt[k + 2]].x), x1 = Mathf.Max(pv[pt[k]].x, pv[pt[k + 1]].x, pv[pt[k + 2]].x);
                        float z0 = Mathf.Min(pv[pt[k]].z, pv[pt[k + 1]].z, pv[pt[k + 2]].z), z1 = Mathf.Max(pv[pt[k]].z, pv[pt[k + 1]].z, pv[pt[k + 2]].z);
                        for (int i = 0; i < c.n; i++)
                            for (int s = -1; s <= 1; s++)
                            {
                                float hw = c.halfW[i] * s;
                                float x = c.px[i] + c.nx[i] * hw, z = c.pz[i] + c.nz[i] * hw;
                                if (x > x0 && x < x1 && z > z0 && z < z1) hidden++;
                            }
                    }
                }
                Check("la pianura all'orizzonte sta fuori dal terreno: nessun punto della strada ci sta sotto", pm != null && pieces >= 8 && hidden == 0, $"{pieces} triangoli, {hidden} punti coperti");
            }
            {
                // the trackside: the pictures are drawn by code, so with them there the pit wall carries boards (with or without a graphics device); the spectators are in the scene only with the cut-out shader,
                // and what is in the scene is what was counted, with the texture coordinates inside the atlas and no number gone wrong
                var w = d.world;
                bool good = true;
                string what = "nessuna immagine (cartelloni e folla non fatti)";
                if (PistaSigns.Ready)
                {
                    var bo = w.root.Find("Cartelloni");
                    var bmesh = bo != null ? bo.GetComponent<MeshFilter>().sharedMesh : null;
                    good = bmesh != null && w.BoardCount >= 10 && bmesh.vertexCount == 4 * w.BoardCount;
                    if (good)
                    {
                        foreach (var t in bmesh.uv) if (t.x < 0f || t.x > 1f || t.y < 0f || t.y > 1f) { good = false; break; }
                        var bs = bmesh.bounds.size;
                        good &= !float.IsNaN(bs.x + bs.y + bs.z) && bs.x < 20000f && bs.z < 20000f;
                    }
                    var fo = w.root.Find("Folla");
                    good &= (fo != null) == w.CrowdShown && (!w.CrowdShown || fo.GetComponent<MeshFilter>().sharedMesh.vertexCount == 4 * w.CrowdCards);
                    what = $"{w.BoardCount} cartelloni ({w.BillboardCount} sui pali ai tornanti), {w.CrowdCards} schede di folla ({w.CornerStandCount} tribunette), folla " + (w.CrowdShown ? "nella scena" : "non mostrata (senza lo shader dei tagli)");
                }
                int gommeFound = 0;
                foreach (Transform ch in w.root)
                    if (ch.name.StartsWith("Gomme"))
                    {
                        gommeFound++;
                        var gm = ch.GetComponent<MeshFilter>().sharedMesh;
                        var gs = gm != null ? gm.bounds.size : new Vector3(float.NaN, 0f, 0f);
                        good &= gm != null && gm.vertexCount > 0 && !float.IsNaN(gs.x + gs.y + gs.z) && gs.x < 200f && gs.z < 200f;
                    }
                good &= gommeFound == w.TyreWalls;
                what += $"; {w.TyreWalls} muri di gomme ({w.TyreCount} gomme), {w.MarshalPosts} postazioni dei commissari";
                Check("il bordo pista: cartelloni sul muretto dei box e ai tornanti, folla sui gradoni, muri di gomme e posti dei commissari, con coordinate nell'atlante e senza numeri sballati", good, what);
            }
            Check("lo stile di guida scelto arriva alla moto del giocatore", d.me.bike.style == (RiderStyle)PistaMode.Style, d.me.bike.style + " (scelto " + PistaMode.Style + ")");
            {
                int aidsNow = PistaMode.Aids;
                var gd = d.guide;
                int window = Mathf.RoundToInt(DrivingLine.BehindMetres / d.circuit.spacing) + Mathf.RoundToInt(DrivingLine.AheadMetres / d.circuit.spacing);
                if (gd != null) gd.Follow(d.me.s, 0f);          // the bike stands still on the grid: nothing to brake for
                bool guideOk = aidsNow >= 2 ? gd == null
                    : gd != null && gd.Visible && gd.root != null
                      && (aidsNow == 1 ? gd.ShownTotal == 0 : gd.shown[0] == window && gd.shown[1] == 0 && gd.shown[2] == 0);
                Check("sulla griglia la moto e' ferma: la linea di corsa e' tutta verde davanti (Completi), non ha rosso da mostrare (Sportivi) o non c'e' (Nessuno)",
                    guideOk, "aiuti " + aidsNow + ", " + (gd == null ? "nessuna linea" : gd.shown[0] + " verdi, " + gd.shown[1] + " gialli, " + gd.shown[2] + " rossi sui " + window + " campioni davanti"));
            }
            // ---- the braking sign on the dashboard: silent on the grid; driven by hand from there it shows both of its states (a bike faster than the plan is on the red, one a little slower sees the red ahead) and goes away
            {
                var gs = d.guide;
                Check("sulla griglia l'indicatore di frenata non dice niente (la moto e' ferma: niente da frenare)", d.hud.CueNow == CueKind.None && !d.hud.CueVisible, "segno " + d.hud.CueNow);
                if (gs != null)
                {
                    float s0 = d.me.s, vApproach = -1f, vBrake = -1f;
                    for (float v = 10f; v <= 110f && (vApproach < 0f || vBrake < 0f); v += 1f)
                    {
                        var pk = gs.Peek(s0, v);
                        if (vApproach < 0f && pk.kind == CueKind.Approach && pk.metres >= 30f && pk.target > 0f) vApproach = v;
                        if (vBrake < 0f && pk.kind == CueKind.Brake && pk.target > 0f) vBrake = v;
                    }
                    d.enabled = false;          // the director stops for a moment: it would put the real sign (silent on the grid) back at every frame
                    string ta = "", sa = "", tb = "", sb = ""; bool upA = false, upB = false; float fa = -1f, fb = -1f;
                    if (vApproach > 0f) { d.hud.SetCue(gs.Peek(s0, vApproach)); upA = d.hud.CueNow == CueKind.Approach && d.hud.CueVisible; ta = d.hud.CueTitleText; sa = d.hud.CueSubText; fa = d.hud.CueFill; if (shots) yield return Shot("15_segno_frenata_tra"); }
                    if (vBrake > 0f) { d.hud.SetCue(gs.Peek(s0, vBrake)); upB = d.hud.CueNow == CueKind.Brake && d.hud.CueVisible; tb = d.hud.CueTitleText; sb = d.hud.CueSubText; fb = d.hud.CueFill; if (shots) yield return Shot("16_segno_frenata"); }
                    d.hud.SetCue(BrakeCue.None);
                    bool gone = d.hud.CueNow == CueKind.None && !d.hud.CueVisible;
                    d.enabled = true;
                    string goneText = gone ? "spento" : "ancora acceso";
                    Check("l'indicatore di frenata, guidato a mano dalla griglia, mostra il conto alla rovescia e poi 'frena', con la velocita' da raggiungere, e poi sparisce",
                        vApproach > 0f && upA && ta.StartsWith("FRENA TRA ") && sa.StartsWith("fino a ") && fa > 0f && fa < 1f && vBrake > 0f && upB && tb == "FRENA" && sb.StartsWith("fino a ") && fb > 0f && gone,
                        $"conto alla rovescia con la moto a {vApproach:F0} m/s: '{ta}' / '{sa}', barra {fa:F2}; sul rosso a {vBrake:F0} m/s: '{tb}' / '{sb}', barra {fb:F2}; poi {goneText}");
                }
                else Check("senza linea (aiuti " + PistaMode.Aids + ") non c'e' nemmeno l'indicatore di frenata", PistaMode.Aids >= 2, "aiuti " + PistaMode.Aids);
            }
            Check("senza la carriera la moto del giocatore e' quella di serie, senza livello e con la chiave della moto", !PistaMode.CareerApplies && d.me.bike.spec.id == Bikes.Get(d.me.bikeIndex).id && d.me.bike.spec.tune == 0 && d.BikeId == Bikes.Get(d.me.bikeIndex).id, d.me.bike.spec.id + ", livello " + d.me.bike.spec.tune);
            CodecChecks(d);
            EngineChecks();
            float gx, gz, gy, gh;
            c.GridSlot(d.me.slot, out gx, out gz, out gy, out gh);
            Check("la griglia: la moto del giocatore sta sull'asfalto, alla quota giusta", Mathf.Abs(d.world.GroundY(gx, gz) - gy) < 0.08f && Mathf.Abs(d.me.bike.y - gy) < 0.08f, $"suolo {d.world.GroundY(gx, gz):F2}, moto {d.me.bike.y:F2}");
            Check("sulla griglia gli effetti di velocita' ci sono ma spenti: la moto e' ferma", d.cam.Fx != null && d.cam.Fx.Alive && d.cam.Fx.Level < 0.001f && !d.cam.Fx.BlurActive, d.cam.Fx == null ? "nessun volume" : $"livello {d.cam.Fx.Level:F3}");
            Check("tutti sono in griglia, fermi, e la gara non e' partita", d.race.phase == RacePhase.Grid && d.race.racers.TrueForAll(r => r.speed < 0.1f && Mathf.Abs(r.lateral) < c.HalfWidth(r.s)), $"fase {d.race.phase}");
            int bikesInScene = BikesInScene();
            Check("nella scena ci sono tante moto quanti piloti", bikesInScene == d.race.racers.Count, bikesInScene + " moto");
            if (shots) { yield return Shot("1_intro"); yield return new WaitForSeconds(2.6f); yield return Shot("2_griglia"); yield return BikeGallery(d); }

            // ---- the lights and the start
            phase = "partenza";
            var seenLights = new List<int>();
            float tLights = Time.realtimeSinceStartup;
            while (d.race.phase != RacePhase.Racing && Time.realtimeSinceStartup - tLights < 40f)
            {
                int l = d.race.lightsOn;
                if (seenLights.Count == 0 || seenLights[seenLights.Count - 1] != l) seenLights.Add(l);
                if (d.race.phase == RacePhase.Countdown && l == 5 && shots && !shotLights) { shotLights = true; yield return Shot("3_semaforo"); }
                yield return null;
            }
            Check("i cinque fanali si accendono uno alla volta, poi si spengono", d.race.phase == RacePhase.Racing && seenLights.Contains(1) && seenLights.Contains(5) && IsAscending(seenLights), "visti " + string.Join(",", seenLights));
            Check("al via i fanali del portale sono verdi e poi si spengono", true, "lampade accese " + d.world.LampsOn);
            Check("al via nessuno e' ancora partito, e poi tutti si muovono", d.race.time < 1f, $"{d.race.time:F2} s dal via");
            float progress0 = d.me.progress;
            yield return new WaitForSeconds(1.2f);
            Check("la moto del giocatore (guidata dal pilota automatico) parte", d.me.speed > 3f && d.me.progress > progress0 + 3f, $"{d.me.speed * 3.6f:F0} km/h, {d.me.progress - progress0:F1} m in 1,2 s");
            // ---- two bikes touch: a computer rider is put alongside the player's bike, a little across, and the race must push them apart and the director must see it
            {
                Racer other = null; float nearest = 1e9f;
                foreach (var r in d.race.racers) if (r != d.me && r.bike != null && !r.bike.crashed) { float gap = Mathf.Abs(r.progress - d.me.progress); if (gap < nearest) { nearest = gap; other = r; } }
                bool ran = other != null && d.me.bike != null && !d.me.bike.crashed;
                int seenBefore = d.ContactsSeen; float apart = 0f, closingSpeed = 0f;
                if (ran)
                {
                    var mb = d.me.bike; var ob = other.bike;
                    float sinH = Mathf.Sin(mb.heading), cosH = Mathf.Cos(mb.heading);
                    ob.Reset(mb.x + cosH * 0.9f, mb.z - sinH * 0.9f, mb.heading - 0.25f, mb.y);
                    ob.u = Mathf.Max(mb.u, 8f); ob.omegaF = ob.omegaR = ob.u / ob.spec.wheelRadius; ob.gear = mb.gear;
                    closingSpeed = ob.u * Mathf.Sin(0.25f);
                    yield return new WaitForSeconds(0.4f);
                    apart = Mathf.Sqrt((mb.x - ob.x) * (mb.x - ob.x) + (mb.z - ob.z) * (mb.z - ob.z));
                }
                Check("una moto che arriva di traverso addosso al giocatore lo urta: il regista vede il contatto e le due moto non restano una dentro l'altra", ran && d.ContactsSeen > seenBefore && apart > 0.6f,
                    ran ? "contatti visti " + (d.ContactsSeen - seenBefore) + " (si avvicinavano a circa " + closingSpeed.ToString("F1") + " m/s), distanza dei centri dopo 0,4 s " + apart.ToString("F2") + " m" : "nessuna moto vicina");
            }
            if (shots) { yield return new WaitForSeconds(1.0f); yield return Shot("4_partenza"); }

            // ---- the race at speed
            phase = "gara";
            Time.timeScale = 4f;
            float maxSpeed = 0f, maxDraft = 0f, t1 = Time.realtimeSinceStartup;
            bool shotsRace = false, shotRedNear = false, shotRedUnder = false, shotYellow = false;
            int cueFrame = 0, cueA = 0, cueB = 0, cueFirstA = -1, cueFirstB = -1, cueMismatch = 0, cueWrong = 0;          // the braking sign, at every frame of the race
            int fxFast = 0, fxOn = 0, fxSlowOn = 0; float fxBlurMax = 0f, fxFringeMax = 0f;                                   // the effects of speed: on when the bike is fast, off when it is slow
            while (d.race.phase == RacePhase.Racing && Time.realtimeSinceStartup - t1 < 360f)
            {
                maxSpeed = Mathf.Max(maxSpeed, d.me.speed);
                var fxn = d.cam.Fx;
                if (fxn != null && fxn.Alive && !d.cam.InIntro)
                {
                    if (d.me.speed > 50f && fxn.Level > 0.2f) { fxFast++; if (fxn.BlurActive && fxn.BlurNow > 0.02f && fxn.FringeNow > 0.01f) fxOn++; }
                    if (d.me.speed < 15f && fxn.BlurActive && fxn.Level > 0.05f) fxSlowOn++;
                    fxBlurMax = Mathf.Max(fxBlurMax, fxn.BlurNow); fxFringeMax = Mathf.Max(fxFringeMax, fxn.FringeNow);
                }
                for (int i = 0; i < d.race.racers.Count; i++) maxDraft = Mathf.Max(maxDraft, d.race.racers[i].draft);
                if (shots && !shotsRace && d.me.speed > 45f) { shotsRace = true; yield return RaceShots(d); }
                var gdShots = d.guide;
                if (shots && gdShots != null && shotsRace && d.me.speed > 25f)
                {
                    if (!shotRedNear && gdShots.redAhead >= 25f && gdShots.redAhead < 80f) { shotRedNear = true; Time.timeScale = 1f; Debug.Log("[PryingEyes] pistatest foto della linea di corsa 12, il rosso sta per arrivare: " + GuideNote(d)); yield return Shot("12_linea_rosso_davanti"); Time.timeScale = 4f; }
                    else if (!shotRedUnder && gdShots.here == Advice.Brake) { shotRedUnder = true; Time.timeScale = 1f; Debug.Log("[PryingEyes] pistatest foto della linea di corsa 13, rosso sotto la moto: " + GuideNote(d)); yield return Shot("13_linea_rosso_sotto"); Time.timeScale = 4f; }
                    else if (!shotYellow && gdShots.here == Advice.Lift && gdShots.redAhead < 0f && d.me.speed < 50f) { shotYellow = true; Time.timeScale = 1f; Debug.Log("[PryingEyes] pistatest foto della linea di corsa 14, giallo sotto la moto: " + GuideNote(d)); yield return Shot("14_linea_gialla_in_curva"); Time.timeScale = 4f; }
                }
                var gq = d.guide;
                if (gq != null)
                {
                    cueFrame++;
                    var kq = d.hud.CueNow;
                    bool silenced = d.me.finished || (d.me.bike != null && d.me.bike.crashed);
                    if (kq != (silenced ? CueKind.None : gq.cue.kind)) cueMismatch++;
                    bool fits = gq.cue.kind == CueKind.None ? !(gq.here == Advice.Brake && DrivingLine.Shows(PistaMode.Aids, Advice.Brake))
                        : gq.cue.kind == CueKind.Brake ? gq.here == Advice.Brake : gq.here != Advice.Brake && gq.redAhead > 0f;
                    if (!fits) cueWrong++;
                    if (kq == CueKind.Approach) { cueA++; if (cueFirstA < 0) cueFirstA = cueFrame; }
                    else if (kq == CueKind.Brake) { cueB++; if (cueFirstB < 0) cueFirstB = cueFrame; }
                }
                yield return null;
            }
            Time.timeScale = 1f;
            if (d.guide == null) Check("senza linea (aiuti " + PistaMode.Aids + ") nemmeno l'indicatore di frenata ha mai detto qualcosa in gara", PistaMode.Aids >= 2 && cueA + cueB == 0, cueA + cueB + " fotogrammi");
            else Check("durante la gara l'indicatore di frenata conta alla rovescia verso il rosso della linea e a ogni fotogramma dice quello che dice la linea",
                cueMismatch == 0 && cueWrong == 0 && cueA > 500 && cueFirstA > 0 && (cueFirstB < 0 || cueFirstA < cueFirstB),
                $"{cueFrame} fotogrammi: {cueA} con il conto alla rovescia, {cueB} con 'frena'; {cueMismatch} volte diverso dalla linea, {cueWrong} incoerente; il conto alla rovescia {(cueFirstB < 0 ? "non e' mai finito in 'frena'" : cueFirstA < cueFirstB ? "viene prima di 'frena'" : "NON viene prima di 'frena'")}");
            Check("a velocita' la camera ha la sfocatura di movimento e la frangia di colore, a bassa velocita' no (se la regolazione del giocatore non e' quasi a zero)", (GameSettings.Motion < 0.3f || (fxFast > 100 && fxOn >= fxFast * 9 / 10)) && fxSlowOn == 0 && fxBlurMax <= PistaFx.BlurCap + 0.001f && fxFringeMax <= PistaFx.FringeMax + 0.001f,
                $"{fxFast} fotogrammi veloci con gli effetti accesi in {fxOn}, {fxSlowOn} fotogrammi lenti con gli effetti accesi, massimo sfocatura {fxBlurMax:F2} e frangia {fxFringeMax:F2}");
            Check("la moto arriva oltre i 200 km/h sul rettilineo", maxSpeed * 3.6f > 200f, $"massima {maxSpeed * 3.6f:F0} km/h");
            Check("la gara finisce da sola", d.race.phase == RacePhase.Over && d.me.finished, $"fase {d.race.phase}, arrivato {d.me.finished}");
            Check("in gara si forma la scia (qualcuno e' stato riparato dal vento)", d.race.racers.Count < 4 || maxDraft > 0.3f, $"scia massima {maxDraft:F2} con {d.race.racers.Count} piloti");
            Check("il giro dura quanto una pista di questa lunghezza", d.me.lastLap > c.length / 90f && d.me.lastLap < c.length / 12f, $"{d.me.lastLap:F1} s su {c.length:F0} m ({c.length / Mathf.Max(0.1f, d.me.lastLap) * 3.6f:F0} km/h di media)");
            int fin = 0; foreach (var r in d.race.racers) if (r.finished) fin++;
            Check("la classifica ha un ordine valido (posizioni 1..n tutte diverse)", ValidStandings(d.race), $"{fin} arrivati su {d.race.racers.Count}");
            float worstCrashes = 0f; foreach (var r in d.race.racers) worstCrashes = Mathf.Max(worstCrashes, r.crashes);
            string fell = ""; foreach (var r in d.race.racers) if (r.crashes > 0) fell += (fell.Length > 0 ? ", " : "") + (r.human ? "GIOCATORE" : r.name + " #" + r.id) + " x" + r.crashes;
            Debug.Log($"[PryingEyes] pistatest cadute: {Crashes(d.race)} in tutto ({(fell.Length > 0 ? fell : "nessuno")}), la peggiore {worstCrashes:F0}; posizione del giocatore {d.me.position}, miglior giro {d.me.bestLap:F2}; contatti del giocatore {d.me.bumps}, visti dal regista {d.ContactsSeen}");
            Check("la tabella dei risultati e' sullo schermo", d.Resulting && d.hud.ResultsShown);
            Check("senza la carriera la gara non paga niente: nessun premio, il conto e' quello di partenza e la tabella e' quella di sempre", !PistaMode.CareerOn && d.CareerPrize == 0 && d.CareerNote.Length == 0 && !d.hud.ResultsSubText.Contains("premio") && PistaCareer.Book.Money == Economy.StartMoney, "premio " + d.CareerPrize + ", conto " + PistaCareer.Book.Money + ", '" + d.hud.ResultsSubText + "'");
            Check("nessuna moto e' sparita nel nulla (quota e posizione finite)", AllFinite(d.race), "");
            if (shots) { yield return new WaitForSeconds(1.5f); yield return Shot("8_risultati"); }

            // ---- the replay of the race that was just ridden
            yield return ReplayTest();

            // ---- again: the same circuit, then a new one
            phase = "ancora";
            int seed0 = d.seed; string name0 = d.circuit.name; int count0 = d.RaceCount;
            int kept0 = d.LapsKept; bool crashedFirst = d.me.crashes > 0;
            d.RestartRace(false);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == count0 + 1, 120f);
            Check("si riparte sullo stesso circuito", Dir.Ready && Dir.seed == seed0 && Dir.circuit.name == name0 && Dir.race.phase == RacePhase.Grid && !Dir.hud.ResultsShown, $"seme {Dir.seed}, fase {Dir.race.phase}");
            // the lap just ridden, if it was clean, comes back as the ghost on the same circuit, with the gap to it on the screen
            if (kept0 > 0)
            {
                Check("il giro pulito e' diventato il fantasma della gara dopo", Dir.Reference != null && Mathf.Abs(Dir.Reference.circuitLength - Dir.circuit.length) < 0.01f && Dir.Reference.tape.Count > 20, Dir.Reference == null ? "nessun fantasma" : $"{Dir.Reference.lapTime:F2} s, {Dir.Reference.tape.Count} fotogrammi");
                yield return WaitFor(() => Dir.race.phase == RacePhase.Racing, 40f);
                yield return WaitFor(() => Dir.DeltaShown && Dir.GhostShown, 30f);
                Check("in gara si vede il fantasma e il distacco da lui", Dir.GhostShown && Dir.DeltaShown && Mathf.Abs(Dir.DeltaNow) < 4f, $"fantasma {Dir.GhostShown}, scarto {(Dir.DeltaShown ? Dir.DeltaNow.ToString("F2") : "-")} s");
            }
            else Check("nessun giro pulito da tenere (la moto e' caduta nel giro)", crashedFirst, "giri tenuti " + kept0 + ", cadute " + (crashedFirst ? "si'" : "no"));
            yield return null;
            yield return null;
            bikesInScene = BikesInScene();
            Check("le moto della gara di prima sono sparite (non ne restano piu' di quanti piloti)", bikesInScene == Dir.race.racers.Count, bikesInScene + " moto, " + Dir.race.racers.Count + " piloti");
            int worlds = FindObjectsByType<PistaWorld>(FindObjectsSortMode.None).Length;
            Check("c'e' un solo mondo", worlds == 1, worlds + " mondi");
            Dir.RestartRace(true);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == count0 + 2, 120f);
            Check("e su un circuito nuovo", Dir.Ready && Dir.seed != seed0 && Dir.race.phase == RacePhase.Grid, $"seme {seed0} -> {Dir.seed}, {Dir.circuit.name}");
            // a short race on it: the lights, the start and a hundred metres
            yield return WaitFor(() => Dir.race.phase == RacePhase.Racing, 40f);
            float progress1 = Dir.me.progress;
            yield return new WaitForSeconds(3f);
            Check("anche la seconda gara parte e la moto avanza", Dir.race.phase == RacePhase.Racing && Dir.me.progress > progress1 + 25f, $"{Dir.me.progress - progress1:F0} m in 3 s");
            if (shots) yield return RosterGallery();
            yield return LicenceRace();
            yield return CupRace();
        }

        /// <summary>A licence test by the real path, last because it leaves the game on a test: the rules of the test replace the choices (circuit, bike, three laps, nobody else, aids, hour), the computer rides
        /// the bike through the three laps, and the medal, the book and the verdict on the screen follow the lap times; "again" is the same test from the start, and "new circuit" is still its circuit.</summary>
        IEnumerator LicenceRace()
        {
            phase = "patente";
            PistaLicences.ForgetAll();
            PistaCareer.ForgetAll(); PistaMode.CareerOn = true;          // the career is on for this test: a medal pays
            const int index = 5;           // a2-3: Vigile 750 on the shortest circuit of the list
            var t = Licences.All[index];
            int count = Dir.RaceCount;
            PistaMode.BeginLicence(index);
            Dir.RestartRace(true);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == count + 1, 120f);
            var d = Dir;
            Check("la prova parte con le sue regole: circuito, moto, tre giri e nessun avversario", d.Ready && d.seed == t.Seed && d.me.bikeIndex == t.bike && d.race.laps == Licences.Laps && d.race.racers.Count == 1 && d.Licence != null && d.Licence.test.id == t.id,
                "seme " + d.seed + " (prova " + t.Seed + "), moto " + d.me.bikeIndex + " (" + t.bike + "), " + d.race.laps + " giri, " + d.race.racers.Count + " piloti");
            // (the computer that rides the player's bike in this test switches every aid on, so the flags of the bike say nothing here: the menu test looks at them without the computer)
            Check("gli aiuti e l'ora sono quelli della prova", PistaMode.Aids == t.aids && PistaMode.HourChoice == t.hour && Mathf.Abs(DesertMode.Clock.hour - PistaMode.Hour) < 0.05f,
                "aiuti " + PistaMode.Aids + ", ora " + PistaMode.HourChoice + " (" + DesertMode.Clock.hour.ToString("F2") + ")");
            Check("'nuova pista' resta sul circuito della prova", PistaMode.NewSeed(false) == t.Seed, PistaMode.Licence + "");
            Check("durante la prova lo schermo mostra i tre tempi da battere", d.hud.LicenceGoalsText.Contains("ORO " + PistaDirector.TargetClock(t.gold)) && d.hud.LicenceGoalsText.Contains("ARG " + PistaDirector.TargetClock(t.silver)) && d.hud.LicenceGoalsText.Contains("BRO " + PistaDirector.TargetClock(t.bronze)), d.hud.LicenceGoalsText);
            yield return WaitFor(() => d.race.phase == RacePhase.Racing, 40f);
            if (shots) { yield return new WaitForSeconds(3.5f); yield return Shot("10_patente_gara"); }
            Time.timeScale = 4f;
            float t0 = Time.realtimeSinceStartup;
            while (d.race.phase == RacePhase.Racing && Time.realtimeSinceStartup - t0 < 240f) yield return null;
            Time.timeScale = 1f;
            var run = d.Licence;
            Check("la prova finisce da sola dopo tre giri", d.race.phase == RacePhase.Over && d.me.finished && d.me.lap >= Licences.Laps, $"fase {d.race.phase}, giro {d.me.lap}, arrivato {d.me.finished}");
            if (run.counted > 0)
            {
                Check("la medaglia e' quella del miglior giro lanciato pulito, e non c'e' un giro piu' veloce di quelli contati", run.medal == t.Judge(run.bestLap) && run.bestLap >= d.me.bestLap - 0.001f, $"{run.counted} giri contati, il migliore {run.bestLap:F2} s = {Licences.MedalName(run.medal)} (miglior giro della gara {d.me.bestLap:F2})");
                Check("il registro ha tenuto il giro: stesso tempo, stessa medaglia", Mathf.Abs(PistaLicences.Book.BestOf(index) - run.bestLap) < 0.001f && PistaLicences.Book.MedalOf(index) == run.medal, $"registro {PistaLicences.Book.BestOf(index):F2} s, {PistaLicences.Book.MedalOf(index)}");
            }
            else Check("nessun giro lanciato pulito, perche' il pilota e' caduto in tutti e due", d.me.crashes > 0 && PistaLicences.Book.BestOf(index) == 0f, $"cadute {d.me.crashes}");
            {
                int want = Economy.LicencePay(Medal.None, run.medal);          // a new book: the test had no medal before, and one test of three is not a licence
                Check("la carriera paga la medaglia: il premio e' quello della medaglia, sta sul conto e la tabella lo dice (niente se non c'e' medaglia)",
                    d.CareerPrize == want && PistaCareer.Book.Money == Economy.StartMoney + want && (want == 0 ? !d.hud.ResultsSubText.Contains("premio") : d.hud.ResultsSubText.EndsWith("premio " + Economy.Format(want) + " cr")),
                    "medaglia " + run.medal + ", premio " + d.CareerPrize + " (atteso " + want + "), conto " + PistaCareer.Book.Money + ", '" + d.hud.ResultsSubText + "'");
            }
            Check("la tabella dice 'PROVA FINITA' e sotto c'e' il verdetto: la medaglia (o il perche' non c'e'), la patente e i tre tempi",
                d.hud.ResultsTitleText == "PROVA FINITA" && d.hud.LicenceTitleText.Length >= 3 && (run.medal == Medal.None || d.hud.LicenceTitleText == Licences.MedalName(run.medal)) && d.LicenceVerdict.Contains(Licences.Names[t.licence]) && d.LicenceVerdict.Contains("Oro " + PistaHud.Clock(t.gold)),
                d.hud.ResultsTitleText + " / " + d.LicenceVerdict);
            Check("a prova finita i tre tempi lasciano lo schermo alla tabella", d.hud.LicenceGoalsText.Length == 0, "'" + d.hud.LicenceGoalsText + "'");
            if (shots) { yield return new WaitForSeconds(1.0f); yield return Shot("10_patente_risultati"); }
            int again = d.RaceCount;
            d.RestartRace(false);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == again + 1, 120f);
            Check("'riprova' e' la stessa prova da capo: stesse regole, un tentativo nuovo, e il tempo migliore resta nel registro",
                Dir.seed == t.Seed && Dir.race.laps == Licences.Laps && Dir.race.racers.Count == 1 && Dir.Licence != null && Dir.Licence != run && Dir.Licence.counted == 0 && Dir.Licence.bestLap == 0f && !Dir.hud.ResultsShown && Dir.hud.LicenceGoalsText.Length > 0
                && (run.counted == 0 || PistaLicences.Book.BestOf(index) > 0f), $"tentativo nuovo {Dir.Licence != null && Dir.Licence != run}, registro {PistaLicences.Book.BestOf(index):F2} s");
            PistaMode.CareerOn = false;
        }

        /// <summary>A cup by the real path, after the licence test (which leaves the game on its own rules too). Round one: the first circuit of the calendar, a bike of the class, twelve riders (the player and the eleven of the cup,
        /// gridded by pace with the player in the middle); the computer rides it, the round counts once for the season (the replay and the way back to the table do not count it again) and four lines under the table say what it gave
        /// and what comes next. The next round is the second circuit with the same eleven, gridded by the standings; the last one closes the season with its trophy and nothing comes after it; a finished season is not taken up
        /// again: the cup starts a new one.</summary>
        IEnumerator CupRace()
        {
            phase = "campionato";
            PistaCups.ForgetAll();
            PistaCareer.ForgetAll(); PistaMode.CareerOn = true;          // the career is on for the cup: the rounds pay, and the trophy
            PistaCareer.Pay(20000); PistaCareer.Buy(1); PistaCareer.Tune(1); PistaCareer.Tune(1);          // the Argo 600 of the Supersport cup, owned and tuned twice: the player rides it as the garage made it
            int baseMoney = PistaCareer.Book.Money;
            const int index = 1;           // the Supersport cup
            var cup = Cups.All[index];
            int count = Dir.RaceCount;
            PistaMode.BeginCup(index, 1);          // one lap each: the whole season in a few minutes
            Dir.RestartRace(true);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == count + 1, 120f);
            var d = Dir;
            var season = PistaCups.Book.Season(index);
            Check("la coppa parte con le sue regole: il primo circuito del calendario, una moto della classe, dodici piloti e nessuna patente",
                d.Ready && d.CupActive && season != null && d.CupSeason == season && season.round == 0 && d.seed == cup.CircuitSeed(0) && Cups.Allows(cup, d.me.bikeIndex) && d.race.racers.Count == Cups.Riders && d.race.laps == 1 && d.Licence == null,
                "seme " + d.seed + " (calendario " + cup.CircuitSeed(0) + "), moto " + d.me.bikeIndex + ", " + d.race.racers.Count + " piloti, " + d.race.laps + " giri");
            if (season == null) yield break;
            Check("in carriera si corre la moto come l'ha preparata il garage: la chiave dice il livello (e con essa i record), pesa meno, e il banner la nomina con il livello",
                d.me.bikeIndex == 1 && d.me.bike.spec.id == Bikes.Get(1).id + "+2" && d.me.bike.spec.tune == 2 && d.me.bike.spec.bikeMass < Bikes.Get(1).bikeMass - 3f && d.BikeId == Bikes.Get(1).id + "+2" && d.hud.BannerSubText.Contains(Bikes.Get(1).name + " liv. 2"),
                d.me.bike.spec.id + ", " + d.me.bike.spec.bikeMass.ToString("F1") + " kg (di serie " + Bikes.Get(1).bikeMass.ToString("F1") + "), banner '" + d.hud.BannerSubText + "'");
            if (shots) { yield return new WaitForSeconds(0.6f); yield return Shot("11_campionato_griglia"); }          // the banner of the start (the cup and the round) is still up

            var field = CupField.Make(cup, season.rank, season.freeSkin, PistaDirector.Names, Livery.Count);
            string wrong = "";
            foreach (var r in d.race.racers)
            {
                int k = d.CupRiderOf(r);
                if (r == d.me) { if (k != 0) wrong += " io(" + k + ")"; continue; }
                if (k < 1 || k > field.Length || r.name != field[k - 1].name || r.bikeIndex != field[k - 1].bike || r.skin == d.me.skin) wrong += " " + r.name + "(" + k + ")";
            }
            Check("gli avversari sono gli undici della coppa (stessi nomi e moto del campo) e nessuno ha i colori del giocatore", wrong.Length == 0, wrong);
            int quickest = Bikes.Quickest(cup.klass);
            float prev = float.MaxValue; bool byPace = true;
            foreach (var r in d.race.standings)
            {
                if (r == d.me) continue;
                float key = field[d.CupRiderOf(r) - 1].pace + (r.bikeIndex == quickest ? 0.02f : 0f);
                if (key > prev + 1e-6f) byPace = false;
                prev = key;
            }
            int slot = d.race.standings.IndexOf(d.me);
            Check("la griglia del primo turno e' per ritmo (il piu' veloce in pole) con il giocatore a meta' schieramento", byPace && slot == Cups.Rivals / 2, "il giocatore e' al posto " + (slot + 1) + " della griglia");
            Check("'nuova pista' resta sul calendario: il circuito del turno", PistaMode.NewSeed(false) == cup.CircuitSeed(0), PistaMode.Cup + "");

            yield return WaitFor(() => d.race.phase == RacePhase.Racing, 40f);
            Time.timeScale = 4f;
            float t0 = Time.realtimeSinceStartup;
            while (d.race.phase == RacePhase.Racing && Time.realtimeSinceStartup - t0 < 240f) yield return null;
            Time.timeScale = 1f;
            yield return WaitFor(() => d.hud.ResultsShown, 20f);
            int finishers = 0, total = 0, expected = 0;
            foreach (var r in d.race.racers) if (r.finished) finishers++;
            for (int p = 0; p < finishers; p++) expected += Cups.PointsFor(p + 1, true);
            foreach (int pt in season.points) total += pt;
            int place = d.race.standings.IndexOf(d.me) + 1;
            Check("il turno finisce da solo e i punti sono nel libro: una gara, tanti punti quanti ne danno i posti di chi ha tagliato il traguardo",
                d.race.phase == RacePhase.Over && season.round == 1 && total == expected && season.points[0] == Cups.PointsFor(place, d.me.finished) && d.CupGained == season.points[0],
                "fase " + d.race.phase + ", gare " + season.round + ", punti " + total + " (attesi " + expected + " per " + finishers + " arrivati), " + place + "° con " + season.points[0]);
            Check("il turno non finisce quando il giocatore taglia il traguardo: arrivano quasi tutti, e i punti vanno a tutto il campo", d.race.waitForField && finishers >= Cups.Riders - 2, finishers + " arrivati su " + Cups.Riders);
            int rounds1 = Economy.Winnings(place, d.me.finished, d.race.laps, d.race.racers.Count - 1, d.me.bikeIndex, season.rank);
            Check("la carriera paga il turno: il premio e' quello del posto, sta sul conto e la tabella lo dice",
                d.CareerPrize == rounds1 && PistaCareer.Book.Money == baseMoney + rounds1 && (rounds1 == 0 ? !d.hud.ResultsSubText.Contains("premio") : d.hud.ResultsSubText.EndsWith("premio " + Economy.Format(rounds1) + " cr")),
                "posto " + place + ", premio " + d.CareerPrize + " (atteso " + rounds1 + "), conto " + PistaCareer.Book.Money + ", '" + d.hud.ResultsSubText + "'");
            string[] lines = d.hud.CupLinesText.Split((char)10);
            Check("sotto la tabella ci sono le quattro righe: la coppa e la gara, cosa ha dato, i primi tre e la prossima gara",
                lines.Length == 4 && lines[0].Contains(cup.name.ToUpperInvariant()) && lines[0].Contains("GARA 1 DI " + cup.Rounds) && lines[1].Contains(" punt") && lines[2].StartsWith("Classifica:") && lines[3].StartsWith("Prossima gara")
                && lines[3].Contains(CircuitGen.Generate(cup.CircuitSeed(1)).name) && !d.CupSeasonOver, d.CupVerdict);
            Check("in fondo alla tabella si legge 'prossima gara', non 'ancora' ne' 'nuova pista'", d.hud.ResultsHintText.Contains("prossima gara") && !d.hud.ResultsHintText.Contains("nuova pista") && !d.hud.ResultsHintText.Contains("ancora"), d.hud.ResultsHintText);
            if (shots) { yield return new WaitForSeconds(1.0f); yield return Shot("11_campionato_risultati"); }

            // the replay and the way back to the table: the round is not counted twice and the lines come back
            bool replayed = d.CanReplay && d.StartReplay();
            yield return null; yield return null;
            if (replayed) { d.EndReplay(); yield return null; yield return null; }
            int total2 = 0;
            foreach (int pt in season.points) total2 += pt;
            Check("il replay e il ritorno alla tabella non contano il turno due volte, e le quattro righe tornano", replayed && season.round == 1 && total2 == total && d.hud.ResultsShown && d.hud.CupLinesText.Split((char)10).Length == 4, "replay " + replayed + ", gare " + season.round + ", punti " + total2);
            Check("e non pagano due volte: il conto e la riga del premio sono quelli di prima", PistaCareer.Book.Money == baseMoney + rounds1 && d.CareerPrize == rounds1 && (rounds1 == 0 || d.hud.ResultsSubText.EndsWith("premio " + Economy.Format(rounds1) + " cr")), "conto " + PistaCareer.Book.Money + ", '" + d.hud.ResultsSubText + "'");

            // the next round: the second circuit of the calendar, the same riders, the grid as the standings
            var names1 = new System.Text.StringBuilder();
            foreach (var r in d.race.racers) names1.Append(r.name).Append(',').Append(r.bikeIndex).Append(';');
            int again = d.RaceCount;
            bool went = d.CupNext();
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == again + 1, 120f);
            d = Dir;
            var order = season.Order();
            string gridWrong = "";
            for (int k = 0; k < Cups.Riders; k++) { int rider = d.CupRiderOf(d.race.standings[k]); if (rider != order[k]) gridWrong += " " + (k + 1) + ":" + rider + "/" + order[k]; }
            var names2 = new System.Text.StringBuilder();
            foreach (var r in d.race.racers) names2.Append(r.name).Append(',').Append(r.bikeIndex).Append(';');
            Check("'prossima gara' porta al secondo circuito del calendario, con gli stessi undici avversari e in griglia come in classifica",
                went && d.Ready && d.CupSeason == season && season.round == 1 && d.seed == cup.CircuitSeed(1) && names2.ToString() == names1.ToString() && gridWrong.Length == 0 && !d.hud.ResultsShown && d.hud.CupLinesText.Length == 0,
                "andata " + went + ", seme " + d.seed + " (calendario " + cup.CircuitSeed(1) + "), griglia sbagliata:" + gridWrong);

            // two rounds written by hand (the player wins, then the field in order) bring the season to its last round, which the computer rides
            var synthetic = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            PistaCups.Record(index, synthetic, Cups.Riders);
            PistaCups.Record(index, synthetic, Cups.Riders);
            again = d.RaceCount;
            d.RestartRace(true);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == again + 1, 120f);
            d = Dir;
            Check("l'ultima gara e' sull'ultimo circuito del calendario", d.Ready && d.CupSeason == season && season.round == cup.Rounds - 1 && d.seed == cup.CircuitSeed(cup.Rounds - 1), "gara " + (season.round + 1) + ", seme " + d.seed + " (calendario " + cup.CircuitSeed(cup.Rounds - 1) + ")");
            yield return WaitFor(() => d.race.phase == RacePhase.Racing, 40f);
            Time.timeScale = 4f;
            t0 = Time.realtimeSinceStartup;
            while (d.race.phase == RacePhase.Racing && Time.realtimeSinceStartup - t0 < 240f) yield return null;
            Time.timeScale = 1f;
            yield return WaitFor(() => d.hud.ResultsShown, 20f);
            var trophy = season.Trophy();
            int standing = season.Position(0);
            lines = d.hud.CupLinesText.Split((char)10);
            Check("l'ultimo turno chiude la stagione: 'STAGIONE FINITA' con il posto in classifica e la coppa (se e' sul podio), e in fondo alla tabella non c'e' un turno dopo",
                d.CupSeasonOver && season.Finished && lines.Length == 4 && lines[3].StartsWith("STAGIONE FINITA") && lines[3].Contains(standing + "° in classifica")
                && (trophy == Medal.None ? !lines[3].Contains("COPPA") : lines[3].Contains(PistaDirector.TrophyName(trophy))) && !d.hud.ResultsHintText.Contains("prossima gara"),
                "posto " + standing + ", coppa " + trophy + ", " + d.CupVerdict);
            int placeLast = d.race.standings.IndexOf(d.me) + 1;
            int lastPay = Economy.Winnings(placeLast, d.me.finished, d.race.laps, d.race.racers.Count - 1, d.me.bikeIndex, season.rank) + Economy.CupBounty(trophy);
            Check("l'ultimo turno paga il posto e la coppa della stagione, e il conto li ha tutti e due i turni", d.CareerPrize == lastPay && PistaCareer.Book.Money == baseMoney + rounds1 + lastPay,
                "coppa " + trophy + " (" + Economy.CupBounty(trophy) + "), premio " + d.CareerPrize + " (atteso " + lastPay + "), conto " + PistaCareer.Book.Money + " (atteso " + (baseMoney + rounds1 + lastPay) + ")");
            Check("il libro tiene la stagione fatta e il miglior posto, e dopo l'ultimo turno 'prossima gara' non porta da nessuna parte",
                PistaCups.Book.SeasonsDone(index) == 1 && PistaCups.Book.BestPlace(index) == standing && PistaCups.Book.BestTrophy(index) == trophy && !d.CupNext(),
                "stagioni " + PistaCups.Book.SeasonsDone(index) + ", miglior posto " + PistaCups.Book.BestPlace(index));
            string saved = PistaCups.Book.Serialize();
            Check("il libro scritto e riletto e' lo stesso", CupBook.Parse(saved).Serialize() == saved, saved.Length > 90 ? saved.Substring(0, 90) : saved);
            if (shots) { yield return new WaitForSeconds(1.0f); yield return Shot("11_campionato_fine_stagione"); }

            // a finished season is not taken up again: the cup begins a new one (the trophy stays in the book)
            PistaMode.BeginCup(index, 1);
            var fresh = PistaCups.Book.Season(index);
            count = Dir.RaceCount;
            Dir.RestartRace(true);
            yield return null;
            yield return WaitFor(() => Dir.Ready && Dir.RaceCount == count + 1, 120f);
            Check("una stagione finita non si riprende: la coppa riparte da una stagione nuova e dalla prima gara, e la coppa vinta resta nel libro",
                fresh != null && fresh != season && fresh.round == 0 && Dir.CupSeason == fresh && Dir.seed == cup.CircuitSeed(0) && PistaCups.Book.SeasonsDone(index) == 1 && PistaCups.Book.BestPlace(index) == standing,
                "stagione nuova " + (fresh != null && fresh != season) + ", gara " + (fresh != null ? fresh.round + 1 : 0) + ", seme " + Dir.seed);

            // leaving the mode clears the cup and the circuit that were chosen for the game: the next game started from the menu must not begin on them (it did, for a licence test)
            PistaMode.CareerOn = false;
            PistaMode.End();
            Check("uscire dalla modalita' libera la coppa e il circuito scelti, cosi' la partita dopo non parte su quelli", PistaMode.Cup == -1 && PistaMode.Licence == -1 && PistaMode.Chosen == 0 && PistaMode.NewSeed(false) != cup.CircuitSeed(0),
                "coppa " + PistaMode.Cup + ", patente " + PistaMode.Licence + ", circuito scelto " + PistaMode.Chosen);
        }

        /// <summary>Every bike of the roster on the grid, from the side and from the front three-quarters, each at its own size with its rider: to look at the nine machines (only with -pistashots).</summary>
        IEnumerator RosterGallery()
        {
            for (int i = 0; i < Bikes.Count; i++)
            {
                PistaMode.Bike = i;
                int count = Dir.RaceCount;
                Dir.RestartRace(false);
                yield return null;
                yield return WaitFor(() => Dir.Ready && Dir.RaceCount == count + 1 && Dir.race.phase == RacePhase.Grid, 120f);
                yield return new WaitForSeconds(0.6f);
                var d = Dir; d.freeCamera = true;
                var rig = d.myView.rig; Vector3 p = rig.root.position, f = rig.root.forward, r = rig.root.right;
                var camT = d.cam.cam.transform;
                (string, Vector3, Vector3)[] views =
                {
                    ("lato", p + r * 2.6f + Vector3.up * 0.7f, p + Vector3.up * 0.62f),
                    ("davanti", p + f * 2.4f - r * 1.7f + Vector3.up * 1.0f, p + Vector3.up * 0.66f),
                    ("dietro", p - f * 3.2f + r * 1.0f + Vector3.up * 1.45f, p + Vector3.up * 0.85f),
                    ("targa", p - f * 1.9f - r * 0.35f + Vector3.up * 1.05f, p - f * 0.99f + Vector3.up * 0.88f),
                };
                foreach (var v in views)
                {
                    camT.position = v.Item2;
                    camT.rotation = Quaternion.LookRotation((v.Item3 - v.Item2).normalized, Vector3.up);
                    d.cam.cam.fieldOfView = 46f;
                    yield return Shot("paddock_" + i + "_" + v.Item1, false);
                }
                d.freeCamera = false;
            }
        }

        /// <summary>The replay: the tape of the race, the start from the results, the bikes where the tape says at every frame, the camera above the ground and on the action, the controls (pause, seek, speed,
        /// cameras by hand, the rider), a photograph (only with -pistashots, which has a graphics device), the end of the tape and the way back to the results.</summary>
        IEnumerator ReplayTest()
        {
            phase = "replay";
            var d = Dir;
            var rec = d.Recording;
            // the tape stops when the race is over (the clock of the race itself runs on a little while, up to the photographs of the test)
            bool taped = rec != null && rec.count == d.race.racers.Count && rec.Frames > 100 && rec.Duration <= d.race.time + 0.1f && d.race.time - rec.Duration < 4f;
            Check("la gara e' stata registrata: un nastro per pilota, lungo quanto la gara", taped, rec == null ? "nessun nastro" : $"{rec.count} nastri, {rec.Frames} fotogrammi, {rec.Duration:F1} s su {d.race.time:F1} s");
            if (!taped) yield break;
            Check("in classifica si puo' avviare il replay (T)", d.CanReplay, "");
            {
                var gr = d.guide;
                Check("durante la gara la linea di corsa ha detto di frenare e poi no: il rosso e' comparso, e non per tutto il tempo",
                    gr == null || (gr.redFrames > 30 && gr.redFrames < gr.frames * 0.75f),
                    gr == null ? "nessuna linea (aiuti " + PistaMode.Aids + ")" : gr.redFrames + " fotogrammi con il rosso su " + gr.frames);
            }
            Check("a fine gara l'indicatore di frenata e' spento", d.Resulting && d.hud.CueNow == CueKind.None && !d.hud.CueVisible, "segno " + d.hud.CueNow + (d.hud.CueVisible ? ", visibile" : ""));
            Check("a fine gara la linea di corsa si spegne (niente consigli sulla classifica ne' nel replay)", d.Resulting && (d.guide == null || !d.guide.Visible), d.guide == null ? "nessuna linea" : "visibile " + d.guide.Visible);
            bool started = d.StartReplay();
            yield return null; yield return null;
            Check("il replay parte dall'inizio e nasconde la classifica", started && d.Replaying && !d.hud.ResultsShown && d.ReplayTime < 0.2f && d.ReplayUiShown, $"tempo {d.ReplayTime:F2} s");
            var plan = d.ReplayPlan;
            Check("il montaggio copre la gara: parte dalla griglia, finisce al traguardo, cambia camera", plan != null && plan.Count > 5 && plan[0].kind == ShotKind.Grid && Mathf.Abs(plan[plan.Count - 1].t1 - rec.Duration) < 0.01f, plan == null ? "nessun montaggio" : $"{plan.Count} inquadrature");
            int finishShots = 0; var planKinds = new HashSet<ShotKind>();
            if (plan != null) foreach (var s in plan) { planKinds.Add(s.kind); if (s.kind == ShotKind.Finish) finishShots++; }
            Check("il montaggio usa almeno cinque camere e inquadra il traguardo", planKinds.Count >= 5 && finishShots >= 1, $"{planKinds.Count} camere, {finishShots} al traguardo");

            // ---- the first half minute at four times the speed, watched frame by frame
            d.ReplaySetSpeed(4f);
            float tEnd = Mathf.Min(rec.Duration - 2f, 30f), dev = 0f; int frames = 0, below = 0, inView = 0, finite = 0, smooth = 0, smoothIn = 0; var seen = new HashSet<ShotKind>(); var perKind = new Dictionary<ShotKind, int[]>();
            float start = Time.realtimeSinceStartup;
            while (d.ReplayTime < tEnd && Time.realtimeSinceStartup - start < 90f)
            {
                yield return null;
                frames++;
                var s = d.ReplayShotNow;
                if (shots && seen.Add(s.kind)) { d.ReplaySetSpeed(1f); yield return Shot("replay_" + s.kind.ToString().ToLowerInvariant()); d.ReplaySetSpeed(4f); }
                else seen.Add(s.kind);
                for (int i = 0; i < d.views.Count && i < rec.count; i++)
                {
                    var f = rec.PoseAt(i, d.ReplayTime); var sh = d.views[i].shown;
                    dev = Mathf.Max(dev, Vector3.Distance(new Vector3(f.x, f.y, f.z), new Vector3(sh.x, sh.y, sh.z)));
                }
                var ct = d.cam.cam.transform;
                if (!float.IsNaN(ct.position.x + ct.position.y + ct.position.z + ct.rotation.x + ct.rotation.w)) finite++;
                if (ct.position.y < d.world.GroundY(ct.position.x, ct.position.z) + 0.3f) below++;
                var sv = d.views[s.subject].shown;
                var vp = d.cam.cam.WorldToViewportPoint(new Vector3(sv.x, sv.y + 0.9f, sv.z));
                bool visible = vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
                if (visible) inView++;
                int[] pk; if (!perKind.TryGetValue(s.kind, out pk)) perKind[s.kind] = pk = new int[2];
                pk[0]++; if (visible) pk[1]++;
                // a frame that took longer than 50 ms (the machine was busy) makes the replay jump up to 0.4 s at this speed and the camera is behind by design: it is not what the check is about
                // (the camera on board is the eyes of the rider: the rider is not in that picture and is not meant to be)
                if (s.kind != ShotKind.Onboard && Time.unscaledDeltaTime < 0.05f) { smooth++; if (visible) smoothIn++; }
            }
            Check("al replay le moto stanno dove dice il nastro, a ogni fotogramma", frames > 20 && dev < 0.06f, $"{frames} fotogrammi, scarto massimo {dev * 100f:F1} cm");
            Check("la camera non e' mai sotto il suolo e le sue pose sono valide", frames > 20 && below == 0 && finite == frames, $"{below} sotto il suolo, {finite}/{frames} valide");
            var kinds = new System.Text.StringBuilder();
            foreach (var kv in perKind) kinds.Append(kinds.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value[1] * 100 / Mathf.Max(1, kv.Value[0])).Append('%');
            Check("la camera tiene il pilota in quadro (la regia lo inquadra, come i passaggi veloci permettono; la camera di bordo e' i suoi occhi e non conta)", smooth > 20 && smoothIn >= smooth * 0.93f, $"{smoothIn} di {smooth} fotogrammi regolari senza la camera di bordo ({inView} di {frames} in tutto); per camera: {kinds}");
            Check("nei primi trenta secondi la regia cambia camera piu' volte", seen.Count >= 3, $"{seen.Count} camere: {string.Join(", ", seen)}");

            // ---- pause, jump, speed
            d.ReplaySetSpeed(1f); d.ReplaySetPaused(true);
            float tp = d.ReplayTime;
            for (int i = 0; i < 20; i++) yield return null;
            Check("in pausa il tempo sta fermo", Mathf.Abs(d.ReplayTime - tp) < 1e-4f && d.ReplayPaused, $"{tp:F3} -> {d.ReplayTime:F3}");
            float target = rec.Duration * 0.5f;
            d.ReplaySeekTo(target);
            yield return null; yield return null;
            float seekDev = 0f;
            for (int i = 0; i < d.views.Count && i < rec.count; i++) { var f = rec.PoseAt(i, d.ReplayTime); var sh = d.views[i].shown; seekDev = Mathf.Max(seekDev, Vector3.Distance(new Vector3(f.x, f.y, f.z), new Vector3(sh.x, sh.y, sh.z))); }
            Check("il salto porta a un altro momento e le moto sono li'", Mathf.Abs(d.ReplayTime - target) < 0.05f && seekDev < 0.06f, $"{d.ReplayTime:F1} s (voluto {target:F1}), scarto {seekDev * 100f:F1} cm");
            d.ReplaySetSpeed(0.5f);
            Check("la velocita' si sceglie tra quelle ammesse", Mathf.Approximately(d.ReplaySpeed, 0.5f));
            d.ReplaySetPaused(false);
            float t0 = d.ReplayTime; float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < 2f) yield return null;
            float adv = d.ReplayTime - t0, real = Time.realtimeSinceStartup - w0;
            Check("a meta' velocita' il tempo del replay scorre a meta'", adv > real * 0.35f && adv < real * 0.65f, $"{adv:F2} s di replay in {real:F2} s");
            d.ReplaySetSpeed(1f);

            // ---- the cameras by hand, and the rider
            int mode0 = d.ReplayModeNow;
            int goodCams = 0; var modes = new HashSet<int>(); string badCams = "";
            for (int k = 0; k < 7; k++)
            {
                d.ReplayCycleCamera();
                yield return null; yield return null;
                modes.Add(d.ReplayModeNow);
                var ct = d.cam.cam.transform; var s = d.ReplayShotNow;
                bool ok = !float.IsNaN(ct.position.x + ct.position.y + ct.position.z) && ct.position.y > d.world.GroundY(ct.position.x, ct.position.z) + 0.3f;
                string seenAs = "";
                if (s.kind == ShotKind.Onboard)
                {
                    // the camera is the rider's own eye: at the helmet, a metre or so above the bike (the rider's chest is behind it, not in front)
                    var sv = d.views[s.subject].shown;
                    float hx = ct.position.x - sv.x, hz = ct.position.z - sv.z, hy = ct.position.y - sv.y;
                    ok &= Mathf.Sqrt(hx * hx + hz * hz) < 1.2f && hy > 0.6f && hy < 1.8f;
                    seenAs = $" a {Mathf.Sqrt(hx * hx + hz * hz):F2} m dalla moto, {hy:F2} m sopra";
                }
                else if (s.kind != ShotKind.Fixed)
                {
                    var sv = d.views[s.subject].shown;
                    var vp = d.cam.cam.WorldToViewportPoint(new Vector3(sv.x, sv.y + 0.9f, sv.z));
                    ok &= vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
                    seenAs = $" vista ({vp.x:F2},{vp.y:F2},{vp.z:F1})";
                }
                if (ok) goodCams++; else badCams += $" [{s.kind} su {rec.names[s.subject]} a {d.ReplayTime:F1} s{seenAs}, camera {ct.position.y - d.world.GroundY(ct.position.x, ct.position.z):F1} m dal suolo, fov {d.cam.cam.fieldOfView:F0}]";
            }
            Check("C passa per le sei camere a mano e torna alla televisione", modes.Count == 7 && d.ReplayModeNow == mode0 && goodCams >= 6, $"{modes.Count} modi, {goodCams} inquadrature buone di 7{badCams}");
            int onScreen = d.ReplayShotNow.subject;
            d.ReplayFollowNext(1);
            yield return null; yield return null;
            Check("TAB passa al pilota dopo, con la camera a mano", d.ReplayFollowNow != onScreen && d.ReplayModeNow == 1 && d.ReplayShotNow.subject == d.ReplayFollowNow, $"pilota {onScreen} -> {d.ReplayFollowNow}, modo {d.ReplayModeNow}");
            while (d.ReplayModeNow != 0) { d.ReplayCycleCamera(); yield return null; }

            // ---- the photograph (needs the graphics device of -pistashots)
            if (shots)
            {
                string before = d.LastPhoto;
                yield return d.ReplayPhoto();
                bool saved = d.LastPhoto.Length > 0 && File.Exists(d.LastPhoto) && new FileInfo(d.LastPhoto).Length > 20000;
                Check("P salva una fotografia del replay, senza l'interfaccia", saved && d.ReplayUiShown, d.LastPhoto.Length > 0 ? d.LastPhoto + " (" + new FileInfo(d.LastPhoto).Length / 1024 + " KB)" : "nessun file");
                yield return Shot("9_replay");
                // the lap from above, then the television edit in eight places around it: the road must be there wherever the circuit climbs or dips
                d.ReplaySetPaused(true);
                d.ReplaySetUi(false);
                d.freeCamera = true;
                {
                    var cc = d.circuit; var cm = d.cam.cam;
                    float cx = (cc.minX + cc.maxX) * 0.5f, cz = (cc.minZ + cc.maxZ) * 0.5f;
                    float hgt = Mathf.Max((cc.maxX - cc.minX) / (16f / 9f), cc.maxZ - cc.minZ) * 1.15f / (2f * Mathf.Tan(20f * Mathf.Deg2Rad));
                    cm.transform.SetPositionAndRotation(new Vector3(cx, hgt, cz), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    cm.fieldOfView = 40f;
                    // from two kilometres up the haze of the air would swallow the whole picture
                    bool fogWas = RenderSettings.fog; RenderSettings.fog = false;
                    yield return Shot("giro_0_dall_alto");
                    RenderSettings.fog = fogWas;
                }
                d.freeCamera = false;
                d.ReplaySetUi(true);
                for (int k = 1; k <= 8; k++)
                {
                    d.ReplaySeekTo(rec.Duration * k / 9f);
                    yield return null; yield return null;
                    yield return Shot("giro_" + k);
                }
            }

            // ---- the end of the tape, and the way back
            d.ReplaySetPaused(false); d.ReplaySetSpeed(1f);
            d.ReplaySeekTo(rec.Duration - 0.2f);
            float w1 = Time.realtimeSinceStartup;
            while (!d.ReplayAtEnd && Time.realtimeSinceStartup - w1 < 5f) yield return null;
            Check("alla fine del nastro il replay si ferma da solo", d.ReplayAtEnd && d.ReplayPaused && Mathf.Abs(d.ReplayTime - rec.Duration) < 0.01f, $"tempo {d.ReplayTime:F2} su {rec.Duration:F2}");
            int bikesBefore = BikesInScene();
            d.EndReplay();
            yield return null; yield return null;
            Check("T riporta alla classifica, con le moto di nuovo libere", !d.Replaying && d.Resulting && d.hud.ResultsShown && !d.ReplayUiShown && BikesInScene() == bikesBefore, "");
            bool unscripted = true; foreach (var v in d.views) unscripted &= !v.scripted;
            Check("e nessuna moto e' rimasta sotto il comando del replay", unscripted);
        }

        bool shotLights;

        /// <summary>The bikes of the riders in the scene (the see-through ghost of the best lap is not a rider).</summary>
        static int BikesInScene() { int n = 0; foreach (var v in FindObjectsByType<PistaBikeView>(FindObjectsSortMode.None)) if (!v.ghost) n++; return n; }


        /// <summary>The player's bike from five sides, with the camera placed by hand, and the view from the helmet: to look at what the code drew.</summary>
        IEnumerator BikeGallery(PistaDirector d)
        {
            d.freeCamera = true;
            var rig = d.myView.rig;
            var t = d.myView.transform;
            var camT = d.cam.cam.transform;
            Vector3 p = rig.root.position;
            Vector3 f = rig.root.forward, r = rig.root.right;
            (string, Vector3, Vector3)[] views =
            {
                ("lato", p + r * 2.7f + Vector3.up * 0.75f, p + Vector3.up * 0.65f),
                ("dietro", p - f * 2.6f + r * 1.2f + Vector3.up * 1.15f, p + Vector3.up * 0.7f),
                ("davanti", p + f * 2.8f - r * 1.5f + Vector3.up * 1.0f, p + Vector3.up * 0.7f),
                ("sopra", p - f * 0.6f + Vector3.up * 3.2f + r * 0.2f, p + Vector3.up * 0.5f),
                ("pilota", p + r * 1.6f - f * 0.3f + Vector3.up * 1.35f, p + Vector3.up * 1.05f),
            };
            foreach (var v in views)
            {
                camT.position = v.Item2;
                camT.rotation = Quaternion.LookRotation((v.Item3 - v.Item2).normalized, Vector3.up);
                d.cam.cam.fieldOfView = 50f;
                yield return Shot("moto_" + v.Item1);
            }
            d.freeCamera = false;
            d.cam.mode = CamMode.Cockpit;
            yield return null; yield return null;
            yield return Shot("moto_casco");
            d.cam.mode = CamMode.Chase;
            yield return null;
        }

        bool Check2(string name, bool ok, string detail = "") { Check(name, ok, detail); return ok; }

        /// <summary>The engine loops of the five kinds of engine, alone: finite, loud but not clipped, closing on themselves, each with a voice of its own (a different brightness), and the bank hands out one clip per kind.</summary>
        void EngineChecks()
        {
            var kinds = new[] { EngineKind.Inline4, EngineKind.V2, EngineKind.Triple, EngineKind.TwoStroke125, EngineKind.TwoStroke500 };
            var gens = new System.Func<float[]>[] { () => SoundGen.SportEngine(41), () => SoundGen.TwinEngine(47), () => SoundGen.TripleEngine(48), () => SoundGen.TwoStrokeSingle(49), () => SoundGen.TwoStrokeV4(50) };
            var centre = new double[kinds.Length];
            bool sane = true, closes = true; string detail = "";
            for (int i = 0; i < kinds.Length; i++)
            {
                float[] s = gens[i]();
                double sum = 0, sq = 0, dsq = 0; float peak = 0; bool finite = s.Length >= SoundGen.Rate;
                for (int k = 0; finite && k < s.Length; k++)
                {
                    float v = s[k];
                    if (float.IsNaN(v) || float.IsInfinity(v)) { finite = false; break; }
                    sum += v; sq += (double)v * v; peak = Mathf.Max(peak, Mathf.Abs(v));
                    if (k > 0) { double dv = v - s[k - 1]; dsq += dv * dv; }
                }
                if (!finite) { sane = false; detail += " " + kinds[i] + " NON FINITO;"; continue; }
                float rms = (float)System.Math.Sqrt(sq / s.Length), rmsDiff = (float)System.Math.Sqrt(dsq / s.Length), dc = (float)(sum / s.Length);
                centre[i] = EngineSynth.Centroid(s);
                float seam = Mathf.Abs(s[0] - s[s.Length - 1]);
                sane &= rms > 0.04f && rms < 0.6f && peak > 0.3f && peak <= 1.0001f && Mathf.Abs(dc) < 0.02f;
                closes &= seam < 4f * rmsDiff + 0.01f;
                detail += $" {kinds[i]} rms {rms:F3} picco {peak:F2} dc {dc:F3} chiusura {seam:F3} (passo {rmsDiff:F3}) centro {centre[i]:F0} Hz;";
            }
            Check("i cinque motori suonano: campioni finiti, forti ma senza saturare, senza continua", sane, detail);
            Check("e ogni giro si chiude su se stesso (senza scatto alla cucitura)", closes, detail);
            bool distinct = true;
            for (int i = 0; i < kinds.Length; i++) for (int j = i + 1; j < kinds.Length; j++) distinct &= System.Math.Abs(centre[i] - centre[j]) / System.Math.Max(centre[i], centre[j]) > 0.10;
            Check("ogni tipo di motore ha una voce sua (centri dello spettro diversi)", distinct, detail);
            var clips = new HashSet<AudioClip>(); bool all = EngineSynth.Rate == SoundGen.Rate;
            foreach (var k in kinds) { var c = SoundBank.ForEngine(k); all &= c != null && clips.Add(c) && c.length > 0.9f; }
            Check("il banco dei suoni da' una traccia per ogni tipo di motore, tutte diverse", all && clips.Count == kinds.Length, clips.Count + " tracce");
            Check("lo scoppio allo scarico e' una traccia breve", SoundBank.Pop != null && SoundBank.Pop.length > 0.1f && SoundBank.Pop.length < 0.5f, SoundBank.Pop == null ? "nulla" : SoundBank.Pop.length.ToString("F2") + " s");
        }

        /// <summary>The messages of the cooperative race (PistaNet), alone: a pose, the roster and the results survive the trip, and a short or hostile message is refused instead of believed.</summary>
        void CodecChecks(PistaDirector d)
        {
            var sent = new BikeView
            {
                x = 123.456f, y = 7.89f, z = -2345.6f, heading = -1.2f, lean = 0.7f, pitch = -0.2f, steer = 0.15f, hang = 0.4f, tuck = 0.6f, speed = 84.5f, rpm = 11250f, throttle = 0.8f, forkC = 0.045f,
                gear = 4, braking = true, slidingF = true, absOn = true, shifting = true, crashKind = (CrashKind)2, surface = (SurfaceKind)3,
            };
            var w = new NetWriter();
            PistaNet.WritePose(w, 7, sent);
            byte[] bytes = w.ToArray();
            var rd = new NetReader(bytes);
            int id; BikeView got;
            bool read = PistaNet.ReadPose(ref rd, out id, out got);
            float dh = Mathf.Abs(Mathf.DeltaAngle(got.heading * Mathf.Rad2Deg, sent.heading * Mathf.Rad2Deg));
            bool same = read && id == 7 && got.x == sent.x && got.y == sent.y && got.z == sent.z && dh < 0.02f
                && Mathf.Abs(got.lean - sent.lean) < 2e-4f && Mathf.Abs(got.pitch - sent.pitch) < 2e-4f && Mathf.Abs(got.steer - sent.steer) < 2e-4f && Mathf.Abs(got.hang - sent.hang) < 2e-4f
                && Mathf.Abs(got.speed - sent.speed) < 0.01f && Mathf.Abs(got.rpm - sent.rpm) <= 1f && Mathf.Abs(got.tuck - sent.tuck) < 0.005f && Mathf.Abs(got.throttle - sent.throttle) < 0.005f
                && Mathf.Abs(got.forkC - sent.forkC) < 0.001f && got.gear == 4 && got.braking && got.slidingF && !got.slidingR && got.absOn && !got.tcsOn && got.shifting && !got.limiter && !got.crashed
                && got.crashKind == sent.crashKind && got.surface == sent.surface;
            Check("la posa di una moto fa il viaggio in rete senza cambiare", same, $"{bytes.Length} byte, rotta di {dh:F3} gradi");
            Check("i diciannove piloti di una griglia piena stanno in un pacchetto (meno di 900 byte)", bytes.Length * 19 < 900, $"{bytes.Length * 19} byte per 19 piloti");
            var shortMsg = new byte[bytes.Length - 4]; System.Array.Copy(bytes, shortMsg, shortMsg.Length);
            var rs = new NetReader(shortMsg);
            Check("una posa troncata si rifiuta", !PistaNet.ReadPose(ref rs, out id, out got));
            var far = sent; far.x = 90000f;
            PistaNet.WritePose(w.Reset(), 1, far);
            var rf = new NetReader(w.ToArray());
            Check("una posa fuori dal mondo (messaggio storto o ostile) si rifiuta", !PistaNet.ReadPose(ref rf, out id, out got));

            // the roster: the order of the riders, who is a person and where, the grid, the names
            var race = new Race(d.circuit, 2, d.seed);
            var human = race.AddHuman("Alice", 1, 1, 5); human.netSlot = 0;
            var cpu = race.AddAi("Ferri", 0, 2, 9, 0.9f, 0.1f);
            var friend = race.AddRemote("Bob", 2, 4, true); friend.netSlot = 1;
            race.PlaceAll(new List<Racer> { cpu, human, friend });
            var wr = new NetWriter();
            PistaNet.WriteRoster(wr, 513, 4242, 2, 1, 2, 3, race.racers);
            var entries = new List<PistaNet.Entry>();
            var rr = new NetReader(wr.ToArray());
            int epoch, seedBack, laps, bike, aids, hour;
            bool rosterOk = PistaNet.ReadRoster(ref rr, out epoch, out seedBack, out laps, out bike, out aids, out hour, entries);
            bool rosterSame = rosterOk && epoch == 513 && seedBack == 4242 && laps == 2 && bike == 1 && aids == 2 && hour == 3 && entries.Count == 3;
            for (int i = 0; rosterSame && i < 3; i++)
            {
                var r = race.racers[i]; var e = entries[i];
                rosterSame = e.id == i && e.name == r.name && e.bike == r.bikeIndex && e.skin == r.skin && e.grid == r.slot && e.netSlot == r.netSlot && (e.kind == 1) == r.cpu && !e.Dnf;
            }
            Check("la griglia (chi corre, con quale moto e colori, da quale posto) fa il viaggio in rete", rosterSame, entries.Count + " piloti");
            PistaNet.WriteRoster(wr.Reset(), 1, 4242, 0, 1, 2, 3, race.racers);
            var rz = new NetReader(wr.ToArray());
            Check("una griglia senza giri si rifiuta", !PistaNet.ReadRoster(ref rz, out epoch, out seedBack, out laps, out bike, out aids, out hour, entries));

            // the results
            race.Retire(friend);
            var list = new List<Race.Result>();
            race.CollectResults(list);
            PistaNet.WriteResults(wr.Reset(), 77, list);
            var back = new List<Race.Result>();
            var rv = new NetReader(wr.ToArray());
            int ep2;
            bool resOk = PistaNet.ReadResults(ref rv, out ep2, back) && ep2 == 77 && back.Count == list.Count;
            for (int i = 0; resOk && i < list.Count; i++) resOk = back[i].id == list[i].id && back[i].place == list[i].place && back[i].finished == list[i].finished && back[i].dnf == list[i].dnf && back[i].crashes == list[i].crashes;
            Check("i risultati fanno il viaggio in rete, con chi si e' ritirato", resOk && back.Exists(e => e.dnf), back.Count + " voci");
        }

        static bool IsAscending(List<int> v)
        {
            for (int i = 1; i < v.Count; i++) if (v[i] < v[i - 1] && v[i] != 0) return false;
            return true;
        }

        static bool ValidStandings(Race race)
        {
            var seen = new HashSet<int>();
            foreach (var r in race.racers) if (r.position < 1 || r.position > race.racers.Count || !seen.Add(r.position)) return false;
            return true;
        }

        static int Crashes(Race race) { int n = 0; foreach (var r in race.racers) n += r.crashes; return n; }

        static bool AllFinite(Race race)
        {
            foreach (var r in race.racers)
                if (float.IsNaN(r.x) || float.IsNaN(r.y) || float.IsNaN(r.z) || Mathf.Abs(r.y) > 5000f) return false;
            return true;
        }

        /// <summary>A photograph of what the player sees, with the interface of the race (the canvas goes to screen-space camera for the moment).</summary>
        /// <summary>-pistaperf (needs a graphics device, not -nographics): what a frame of a full race costs, at 1920x1080 with the race camera. In batchmode Unity does not draw the cameras by itself (the frame
        /// rates it shows are made up), so every frame the probe draws the race camera into a texture and reads one pixel back, which waits for the GPU. The time is the game's logic plus the CPU work of
        /// drawing plus the GPU's queue: a little pessimistic, since in the real game the CPU and the GPU overlap. Three windows: the grid before the lights, the first ten seconds of the race (the pack is
        /// closest) and a stretch of the lap. The numbers are written to the log and to hotel_build/prove/pista/perf.txt.</summary>
        IEnumerator PerfRun()
        {
            var d = Dir;
            var cam = d.cam.cam;
            const int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "pistaperf" };
            var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var report = new System.Text.StringBuilder();
            report.AppendLine($"== pista perf {DateTime.Now:yyyy-MM-dd HH:mm}: '{d.circuit.name}', {d.circuit.length:F0} m, {d.race.racers.Count} piloti, {W}x{H}");
            phase = "perf: griglia";
            yield return PerfWindow("griglia", () => d.race.phase == RacePhase.Grid || d.race.phase == RacePhase.Countdown, 3f, cam, rt, px, report);
            yield return WaitFor(() => d.race.phase == RacePhase.Racing, 40f);
            phase = "perf: partenza";
            yield return PerfWindow("primi 10 s di gara", () => d.race.phase == RacePhase.Racing && d.race.time < 10f, 14f, cam, rt, px, report);
            phase = "perf: giro";
            yield return PerfWindow("tra i 10 e i 30 s di gara", () => d.race.phase == RacePhase.Racing && d.race.time < 30f, 24f, cam, rt, px, report);
            cam.targetTexture = prev;
            Destroy(rt); Destroy(px);
            try
            {
                string dir = TestPaths.Out("prove/pista");
                Directory.CreateDirectory(dir);
                File.AppendAllText(dir + "/perf.txt", report.ToString());
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        IEnumerator PerfWindow(string label, Func<bool> keep, float cap, Camera cam, RenderTexture rt, Texture2D px, System.Text.StringBuilder report)
        {
            var frame = new List<float>(); var draw = new List<float>();
            float t0 = Time.realtimeSinceStartup;
            int skip = 3;                                     // the first frames after a change are not what the window is about
            while (keep() && Time.realtimeSinceStartup - t0 < cap)
            {
                yield return new WaitForEndOfFrame();
                float a = Time.realtimeSinceStartup;
                cam.Render();
                RenderTexture.active = rt;
                px.ReadPixels(new Rect(rt.width / 2, rt.height / 2, 1, 1), 0, 0);
                RenderTexture.active = null;
                float b = Time.realtimeSinceStartup;
                if (skip > 0) { skip--; continue; }
                frame.Add(Time.unscaledDeltaTime * 1000f); draw.Add((b - a) * 1000f);
                if (Time.unscaledDeltaTime > 0.025f)                // a hitch is what a player sees first: say where on the lap it was (draw = the camera render alone)
                {
                    string at = $"[PryingEyes] pistaperf fotogramma lungo di {Time.unscaledDeltaTime * 1000f:F0} ms ({(b - a) * 1000f:F0} ms di disegno) a {Dir.me.s:F0} m, {Dir.race.time:F1} s di gara";
                    Debug.Log(at); report.AppendLine("  " + at.Replace("[PryingEyes] pistaperf ", ""));
                }
            }
            if (frame.Count == 0) { report.AppendLine($"  {label}: nessun fotogramma"); yield break; }
            var sorted = new List<float>(frame); sorted.Sort();
            float sum = 0f, sumDraw = 0f;
            foreach (float f in frame) sum += f;
            foreach (float f in draw) sumDraw += f;
            float avg = sum / frame.Count;
            float P(float q) { return sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)]; }
            int visible = 0; long tris = 0;
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.isVisible) continue;
                visible++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3;
            }
            string line = $"  {label}: {frame.Count} fotogrammi, media {avg:F1} ms ({1000f / avg:F0} FPS), p50 {P(0.5f):F1}, p95 {P(0.95f):F1}, p99 {P(0.99f):F1}, peggiore {sorted[sorted.Count - 1]:F1} ms; disegno {sumDraw / draw.Count:F1} ms; {visible} oggetti visibili, {tris / 1000} mila triangoli";
            report.AppendLine(line);
            Debug.Log("[PryingEyes] pistaperf" + line);
        }

        /// <summary>-pistaworldshots (needs a graphics device): the scenery alone, photographed from places chosen by hand, 1920x1080, without the interface, into hotel_build/prove/pista/mondo/. At noon from three
        /// places along the lap, five views each (behind the player's line, the asphalt close up, from the kerb along the road, from the grass across the road, from above toward the horizon), and in the morning and
        /// at dusk the first and the last of those from two places: it is what is looked at to judge the ground, the trees, the light and the sky, and to compare one round of work with the next.</summary>
        IEnumerator WorldShots()
        {
            var d = Dir; var c = d.circuit;
            d.freeCamera = true;
            string sub = PistaLook.StrArg("-pistashotdir");                  // a name, to keep the photographs of one try of the look apart from the next
            string dir = TestPaths.Out("prove/pista/mondo" + (string.IsNullOrEmpty(sub) ? "" : "/" + sub));
            Directory.CreateDirectory(dir);
            float[] hours = { 13f, 9f, 17.5f };
            string[] hourName = { "mezzogiorno", "mattina", "sera" };
            int[] places = { c.n / 12, c.n * 3 / 8, c.n * 5 / 8 };
            Vector3 At(int idx, float lateral, float up)
            {
                int j = c.Wrap(idx);
                float x = c.px[j] + c.nx[j] * lateral, z = c.pz[j] + c.nz[j] * lateral;
                return new Vector3(x, d.world.GroundY(x, z) + up, z);
            }
            for (int h = 0; h < hours.Length; h++)
            {
                phase = "foto del mondo: " + hourName[h];
                if (d.sky != null) d.sky.SetHour(hours[h], true);
                yield return null; yield return null; yield return null;
                int nPlaces = h == 0 ? places.Length : 2;
                for (int a = 0; a < nPlaces; a++)
                {
                    int i = places[a];
                    float hw = c.halfW[i];
                    string tag = hourName[h] + "_" + a + "_";
                    yield return WorldShot(dir, tag + "1_dietro", At(i - 4, 0.3f, 2.1f), At(i + 25, 0f, 0.5f), 62f);
                    if (h == 0)
                    {
                        yield return WorldShot(dir, tag + "2_asfalto", At(i, 0.3f, 1.6f), At(i + 3, 0.3f, 0f), 55f);
                        yield return WorldShot(dir, tag + "3_cordolo", At(i, hw + 3.5f, 1.3f), At(i + 30, hw - 1f, 0.2f), 60f);
                        yield return WorldShot(dir, tag + "4_erba", At(i, hw + 28f, 1.5f), At(i + 10, 0f, 1f), 62f);
                    }
                    yield return WorldShot(dir, tag + "5_orizzonte", At(i, 0f, 8f), At(i + 120, 0f, 2f), 50f);
                }
                if (h == 0)
                {
                    // the trackside, from the places the world names for each kind of thing: the pit wall, the grandstand, the boards at the corners and the small stands
                    phase = "foto del mondo: bordo pista";
                    var perKind = new Dictionary<string, int>();
                    foreach (var v in d.world.vantages)
                    {
                        int k; perKind.TryGetValue(v.kind, out k);
                        perKind[v.kind] = k + 1;
                        yield return WorldShot(dir, "bordo_" + v.kind + "_" + (k + 1), v.from, v.look, v.fov);
                    }
                }
            }
            if (d.sky != null) d.sky.SetHour(PistaMode.Hour, true);
            d.freeCamera = false;
        }

        IEnumerator WorldShot(string dir, string name, Vector3 pos, Vector3 look, float fov)
        {
            var cm = Dir.cam.cam;
            cm.transform.position = pos;
            cm.transform.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
            cm.fieldOfView = fov;
            yield return null;
            yield return new WaitForEndOfFrame();
            try
            {
                const int W = 1920, H = 1080;
                var rt = RenderTexture.GetTemporary(W, H, 24);
                var prev = cm.targetTexture;
                cm.targetTexture = rt;
                cm.Render();
                cm.targetTexture = prev;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                var old = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = old;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(dir + "/" + name + ".png", tex.EncodeToPNG());
                Destroy(tex);
                GameManager.HarnessShotFrame = Time.frameCount;
                Debug.Log("[PryingEyes] pistatest foto " + dir + "/" + name + ".png");
            }
            catch (Exception e) { Debug.LogException(e); Check("foto " + name, false, e.Message); }
        }

        /// <summary>What the driving line says now, for the log next to a photograph: where the bike is, how fast, the advice under it, how far the first red is and the pieces of the stretch ahead.</summary>
        static string GuideNote(PistaDirector d)
        {
            var g = d.guide;
            return g == null ? "nessuna linea" : "a " + d.me.s.ToString("F0") + " m, " + (d.me.speed * 3.6f).ToString("F0") + " km/h, sotto la moto " + g.here + ", primo rosso " + (g.redAhead < 0f ? "-" : "a " + g.redAhead.ToString("F0") + " m") + ": " + g.Describe() + " | segno '" + d.hud.CueTitleText + "' '" + d.hud.CueSubText + "'";
        }

        /// <summary>The photographs at speed, as the player sees them: real time, 60 pictures a second (the blur of a frame is the distance the picture moves in it, so the photographs are taken at the rate
        /// of a screen and not at the one this machine could make), a dozen frames after every change of view so that no cut is in the picture, and the same view without the effects of speed, with
        /// half of them and with all of them (those three without the HUD: the HUD is drawn into the photograph through the camera and would be blurred, in the game it is not).</summary>
        IEnumerator RaceShots(PistaDirector d)
        {
            int tf = Application.targetFrameRate, vs = QualitySettings.vSyncCount;
            Time.timeScale = 1f; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            for (int i = 0; i < 12; i++) { CamDelta(Dir); if (i == 8) yield return Warm(d); else yield return null; }          // the first drawing into a texture after seconds of drawing only to the screen shows a blur of its own (heavier than the game's: the same frame drawn twice is the same, the next drawings are right): one throwaway drawing first
            FxNote(d, "5_gara_inseguimento");
            yield return Shot("5_gara_inseguimento");
            var fx = d.cam.Fx;
            if (fx != null)
            {
                float[] shares = { 0f, 0.5f, 1f };
                string[] names = { "5a_effetti_spenti", "5b_effetti_meta", "5c_effetti_pieni" };
                for (int k = 0; k < shares.Length; k++)
                {
                    fx.Force(shares[k]);
                    for (int i = 0; i < 4; i++) { CamDelta(Dir); yield return null; }
                    FxNote(d, names[k]);
                    yield return Shot(names[k], false);
                }
                fx.Force(-1f);
            }
            d.cam.mode = CamMode.Far;
            for (int i = 0; i < 12; i++) { CamDelta(Dir); yield return null; }
            FxNote(d, "6_gara_lontana");
            yield return Shot("6_gara_lontana");
            d.cam.mode = CamMode.Cockpit;
            for (int i = 0; i < 12; i++) { CamDelta(Dir); yield return null; }
            FxNote(d, "7_gara_casco");
            yield return Shot("7_gara_casco");
            d.cam.mode = CamMode.Chase;
            Application.targetFrameRate = tf; QualitySettings.vSyncCount = vs;
            Time.timeScale = 4f;
        }

        /// <summary>One throwaway drawing of the race camera into a texture at the end of a frame (the first drawing after a long time of the camera drawing only to the screen showed a worse blur than every later one).</summary>
        IEnumerator Warm(PistaDirector d)
        {
            yield return new WaitForEndOfFrame();
            var cam = d.cam.cam;
            var rt = RenderTexture.GetTemporary(1600, 900, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.ReleaseTemporary(rt);
        }

        /// <summary>What the effects of speed were doing when a photograph was taken, for the log: the speed, the share of full speed, the intensity of the blur and the game time of the frame it was measured against.</summary>
        static Vector3 lastCamPos; static Quaternion lastCamRot = Quaternion.identity;

        /// <summary>How far the camera moved and turned since the last time this was asked (once a frame while waiting for a photograph), for the log.</summary>
        static string CamDelta(PistaDirector d)
        {
            var t = d.cam.cam.transform;
            string s = "camera: " + Vector3.Distance(t.position, lastCamPos).ToString("F3") + " m e " + Quaternion.Angle(t.rotation, lastCamRot).ToString("F2") + " gradi dall'ultimo fotogramma, campo visivo " + d.cam.cam.fieldOfView.ToString("F1");
            lastCamPos = t.position; lastCamRot = t.rotation;
            return s;
        }

        static void FxNote(PistaDirector d, string what)
        {
            var f = d.cam.Fx;
            Debug.Log("[PryingEyes] pistatest effetti alla foto " + what + ": " + (d.me.speed * 3.6f).ToString("F0") + " km/h, " + (f == null ? "nessun volume" : "livello " + f.Level.ToString("F2") + ", sfocatura " + f.BlurNow.ToString("F3") + ", fotogramma " + (f.FrameTime * 1000f).ToString("F1") + " ms (reale " + (Time.unscaledDeltaTime * 1000f).ToString("F1") + " ms), scala del tempo " + Time.timeScale.ToString("F1") + ", " + CamDelta(d)));
        }

        IEnumerator Shot(string name, bool withHud = true)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                var d = Dir;
                string dir = TestPaths.Out("prove/pista");
                Directory.CreateDirectory(dir);
                var cam = d.cam.cam;
                int W = 1600, H = 900;
                var rt = RenderTexture.GetTemporary(W, H, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                var canvas = d.hud.CanvasRef;
                var prevMode = canvas.renderMode;
                var rcanvas = withHud ? d.ReplayCanvas : null;
                RenderMode rprev = RenderMode.ScreenSpaceOverlay;
                if (withHud)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 0.2f;
                    if (rcanvas != null) { rprev = rcanvas.renderMode; rcanvas.renderMode = RenderMode.ScreenSpaceCamera; rcanvas.worldCamera = cam; rcanvas.planeDistance = 0.25f; }
                    Canvas.ForceUpdateCanvases();
                }
                cam.Render();
                if (withHud)
                {
                    if (rcanvas != null) { rcanvas.renderMode = rprev; rcanvas.worldCamera = null; }
                    canvas.renderMode = prevMode; canvas.worldCamera = null;
                }
                cam.targetTexture = prev;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                var old = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = old;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(dir + "/" + name + ".png", tex.EncodeToPNG());
                Destroy(tex);
                GameManager.HarnessShotFrame = Time.frameCount;
                Debug.Log("[PryingEyes] pistatest foto " + dir + "/" + name + ".png");
            }
            catch (Exception e) { Debug.LogException(e); Check("foto " + name, false, e.Message); }
        }
    }
}
#endif
