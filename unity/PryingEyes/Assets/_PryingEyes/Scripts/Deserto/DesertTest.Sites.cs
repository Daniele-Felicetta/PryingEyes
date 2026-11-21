#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly pelle: "gli edifici a volte hanno ... oggetti che spariscono e riappaiono" (segnalazione del 2026-11-20). Un luogo si vede in due modi: il guscio vero (entro 280 m, 340 per le
    /// torri) e, da lontano, la pelle (SiteFacade: una buccia di facciate fino a 760 m). Il passaggio dall'uno all'altra era il sospettato: la pelle nasce spenta per i luoghi che hanno gia' il guscio e allora non
    /// viene costruita (il direttore costruisce solo le pelli accese), quindi quando il guscio si scarica, a 370 m da chi se ne va, il luogo potrebbe restare vuoto per i fotogrammi che servono a costruirla.
    /// La prova tiene il conto, a ogni fotogramma, di quanto di ogni luogo entro 700 m si disegna (tutto, a pezzi, niente) mentre la moto va lontano dal punto di partenza e torna, e dopo una teleportata
    /// che lascia in un colpo il luogo con la pelle piu' lunga da costruire; un luogo che si vedeva intero e smette di vederlo e' un luogo che sparisce. Misura quanti fotogrammi dura (la costruzione di una
    /// pelle e' a fotogrammi, non a secondi: un pezzo o piu' per fotogramma).</summary>
    public sealed partial class DesertTest
    {
        enum SiteLook { Blank, Partial, Full }

        sealed class SiteRec
        {
            public Site site; public SiteLook last = SiteLook.Blank;
            public bool inLost; public float lostRun, bothRun; public int lostRunFrames; public Vector3 lostAt;
        }

        /// <summary>Il conto, fotogramma per fotogramma, di quanto di ogni luogo entro 'watch' m si disegna: tutto (il guscio vero e' completo, o la pelle e' accesa e finita), a pezzi (una torre con qualche livello, una
        /// pelle in costruzione) o niente. Una volta che un luogo si vedeva tutto e smette (entro 'watch' m) comincia una 'perdita', e finisce quando si rivede tutto.</summary>
        sealed class SiteLedger
        {
            public readonly float watch;
            public int frames, followed, lostRuns, lostFrames, lostMaxFrames, blankRuns, bothFrames;
            public float lostSecs, lostMax, bothMax;
            public readonly List<string> events = new List<string>();
            readonly Dictionary<Site, SiteRec> recs = new Dictionary<Site, SiteRec>();
            readonly List<Site> list = new List<Site>();
            readonly List<(int frames, string text)> runs = new List<(int, string)>();

            public SiteLedger(float watchRadius) { watch = watchRadius; }

            public static SiteLook LookOf(Site s, out bool both)
            {
                var rt = RtOf(s); var skin = Dd != null ? Dd.FarSkinOf(s) : null;
                bool shell = rt != null && rt.ShellComplete, skinOn = skin != null && skin.Shown;
                both = shell && skinOn;
                if (shell || (skinOn && skin.Done)) return SiteLook.Full;
                bool some = false;
                if (rt != null) foreach (var f in rt.fis) if (f != null) { some = true; break; }
                return some || (skinOn && skin.Vertices > 0) ? SiteLook.Partial : SiteLook.Blank;
            }

            public static string Why(Site s)
            {
                var rt = RtOf(s); var skin = Dd != null ? Dd.FarSkinOf(s) : null;
                return $"{s.kind}/{s.state} cella {s.cx},{s.cz}: guscio {(rt == null ? "non c'e'" : rt.ShellComplete ? "completo" : "a meta'")}, pelle {(skin == null ? "non c'e'" : (skin.Shown ? "accesa" : "spenta") + (skin.Done ? ", finita" : $", non finita ({skin.Vertices} vertici, {skin.Pieces} pezzi)"))}";
            }

            void EndRun(SiteRec r)
            {
                lostMax = Mathf.Max(lostMax, r.lostRun); lostMaxFrames = Mathf.Max(lostMaxFrames, r.lostRunFrames);
                runs.Add((r.lostRunFrames, $"{r.site.kind}/{r.site.state} {r.site.cx},{r.site.cz}: {r.lostRunFrames} fotogrammi, {r.lostRun:F2} s, a {SiteEdge(r.site, r.lostAt):F0} m dal bordo"));
                r.inLost = false;
            }

            public void Sample(Vector3 pos, float dt)
            {
                frames++;
                DesertWorld.SitesNearKnown(pos.x, pos.z, 900f, list);
                foreach (var s in list)
                {
                    float edge = SiteEdge(s, pos);
                    if (edge > 830f) continue;
                    if (!recs.TryGetValue(s, out var r)) { r = new SiteRec { site = s }; recs[s] = r; followed++; }
                    var look = LookOf(s, out bool both);
                    bool near = edge <= watch;
                    if (near && r.last == SiteLook.Full && look != SiteLook.Full)
                    {
                        r.inLost = true; r.lostRun = 0f; r.lostRunFrames = 0; r.lostAt = pos;
                        lostRuns++;
                        if (look == SiteLook.Blank) blankRuns++;
                        if (events.Count < 6) events.Add($"a {edge:F0} m dal bordo ({Why(s)}), fotogramma {Time.frameCount}");
                    }
                    if (r.inLost)
                    {
                        if (near && look != SiteLook.Full) { r.lostRun += dt; r.lostRunFrames++; lostFrames++; lostSecs += dt; }
                        else EndRun(r);
                    }
                    if (both) { bothFrames++; r.bothRun += dt; bothMax = Mathf.Max(bothMax, r.bothRun); }
                    else r.bothRun = 0f;
                    r.last = look;
                }
            }

            public string Report(string tag)
            {
                foreach (var r in recs.Values) if (r.inLost) EndRun(r);
                runs.Sort((a, b) => b.frames.CompareTo(a.frames));
                var sb = new StringBuilder();
                for (int i = 0; i < runs.Count && i < 5; i++) sb.Append(i == 0 ? " Le piu' lunghe: " : "; ").Append(runs[i].text);
                return $"pelle {tag}: {frames} fotogrammi, {followed} luoghi seguiti; {lostRuns} volte un luogo entro {watch:F0} m smette di vedersi tutto ({blankRuns} partendo da niente), in tutto {lostFrames} fotogrammi e {lostSecs:F3} s (la piu' lunga {lostMaxFrames} fotogrammi, {lostMax:F3} s); " +
                    $"guscio completo e pelle accesa insieme: {bothFrames} fotogrammi, al massimo {bothMax:F2} s{sb}";
            }
        }

        SiteLedger flatLedger;                           // chi lo imposta (la fase 'pelle') ne riceve un campione a ogni fotogramma di RideFlat

        /// <summary>Quanti luoghi distinti passano entro 'radius' m dal bordo della loro pianta lungo la rotta.</summary>
        static int SitesAlong(List<Vector3> route, float radius)
        {
            var seen = new HashSet<Site>(); var list = new List<Site>();
            for (int i = 0; i < route.Count - 1; i++)
            {
                float len = Flat(route[i], route[i + 1]);
                for (float d = 0f; d <= len; d += 32f)
                {
                    Vector3 p = Vector3.Lerp(route[i], route[i + 1], len < 0.1f ? 0f : d / len);
                    DesertWorld.SitesNear(p.x, p.z, radius, list);
                    foreach (var s in list) seen.Add(s);
                }
            }
            return seen.Count;
        }

        /// <summary>Un luogo sparisce se per piu' di questi fotogrammi (a 60 al secondo: un decimo di secondo) resta meno che intero entro 700 m.</summary>
        const int SkinGapFrames = 6;

        IEnumerator SkinHandoverAudit()
        {
            var pl = Pl; var dd = Dd;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset(); Weapon.rideBotFire = false;
            motoWorst = 0f;
            ClearHorde();
            bool dropsWere = Drops.disabled;
            Drops.ResetRun(); Drops.disabled = true;
            Check("pelle: c'e' il direttore del deserto", dd != null);
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("pelle: c'e' una moto", bike != null);
            if (dd == null || bike == null) { Drops.disabled = dropsWere; yield break; }

            // ---- 1. la partenza: sabbia aperta, dove i luoghi attorno sono caricati dall'arrivo e le pelle di parecchi non sono mai state costruite
            DesertSky.I?.SetHour(11f, true);
            var open = OpenSand(500f);                                       // come motopiano: fuori dal sagrato (rastrelliere, lampioni, banchi: la rotta cieca non li conosce) e dalla fitta di luoghi attorno alla cattedrale, dove non c'e' rotta sgombra
            Vector3 spot = new Vector3(open.x, 0f, open.y);
            yield return GoTo(spot.x, spot.z, 2f);
            yield return WaitFor(() => LoadQuiet(dd), 90f);
            yield return FlatSettle(pl);
            int shells = 0, unbuilt = 0;
            foreach (var rt in dd.Loaded)
            {
                if (!rt.ShellComplete) continue;
                shells++;
                var sk = dd.FarSkinOf(rt.site);
                if (sk == null || sk.Vertices == 0) unbuilt++;
            }
            Debug.Log($"[PryingEyes] desertotest pelle: alla partenza {dd.LoadedSites} luoghi caricati, {shells} con il guscio completo, di cui {unbuilt} con la pelle lontana mai costruita (se il loro guscio si scaricasse adesso, il luogo resterebbe vuoto finche' la pelle non si costruisce); {dd.FarSkins} pelli in tutto");
            Check("pelle: alla partenza ci sono luoghi con il guscio completo (altrimenti la prova non misura niente)", shells >= 1, $"{shells} gusci");

            // la rotta sgombra (come quella di motopiano) che passa vicino a piu' luoghi: sono i luoghi che si caricano e si scaricano che interessano
            List<Vector3> route = null; float heading = 0f, maxSlope = 0f; int passed = -1;
            foreach (float slope in new[] { 0.18f, 0.3f, 0.45f })
            {
                for (int k = 0; k < 16; k++)
                {
                    float h = k * (Mathf.PI * 2f / 16f);
                    var r = PlanFlatRoute(spot, h, 16, slope, out float ms);
                    if (r.Count - 1 < 13) continue;
                    int n = SitesAlong(r, 300f);
                    if (n > passed) { passed = n; route = r; heading = h; maxSlope = ms; }
                }
                if (route != null) break;
            }
            if (route == null) route = BestFlatRoute(spot, 16, out heading, out maxSlope);
            float len = (route.Count - 1) * 64f;
            Debug.Log($"[PryingEyes] desertotest pelle: rotta da {spot:F0}, direzione {heading * Mathf.Rad2Deg:F0} gradi, {route.Count - 1} tratti ({len:F0} m), pendenza massima {Mathf.Atan(maxSlope) * Mathf.Rad2Deg:F1} gradi, passa a meno di 300 m da {Mathf.Max(passed, SitesAlong(route, 300f))} luoghi");
            Check("pelle: c'e' una rotta sgombra di almeno 800 m dalla partenza", len >= 800f, $"{len:F0} m");
            if (route.Count < 3) { Drops.disabled = dropsWere; yield break; }

            godMode = true; pl.invulnerable = true;
            bool quick = FlatArg("-pellequick", 0f) > 0f;                    // -pellequick 1: solo la teleportata (pochi secondi), senza le due corse
            int loaded0 = dd.SitesLoadedTotal, unloaded0 = dd.SitesUnloadedTotal;
            var ledOut = new SiteLedger(700f); var stOut = new FlatStats { tag = "pelle, andando via", boost = false };
            var ledBack = new SiteLedger(700f); var stBack = new FlatStats { tag = "pelle, tornando", boost = false };
            if (!quick)
            {
                // ---- 2. andando via: i luoghi che avevano il guscio dall'inizio si scaricano
                Vector3 dir = route[1] - route[0]; dir.y = 0f;
                yield return PlowStart(bike, pl.transform.position, Mathf.Atan2(dir.x, dir.z));
                flatLedger = ledOut;
                yield return RideFlat(route, false, stOut);
                flatLedger = null;
                Debug.Log("[PryingEyes] desertotest " + ledOut.Report("andando via"));
                foreach (var e in ledOut.events) Debug.Log("[PryingEyes] desertotest pelle andando via: un luogo smette di vedersi " + e);

                // ---- 3. tornando: i luoghi si ricaricano, la pelle lascia il posto al guscio
                var back = new List<Vector3>(route); back.Reverse();
                if (pl.Riding) pl.AbortRide();
                yield return GoTo(back[0].x, back[0].z, 2f);                 // la rotta di ritorno parte dove l'altra e' finita (di solito a qualche metro): la stessa strada sgombra, al contrario
                Vector3 dirBack = back[1] - back[0]; dirBack.y = 0f;
                yield return PlowStart(bike, pl.transform.position, Mathf.Atan2(dirBack.x, dirBack.z));
                flatLedger = ledBack;
                yield return RideFlat(back, false, stBack);
                flatLedger = null;
                Debug.Log("[PryingEyes] desertotest " + ledBack.Report("tornando"));
                foreach (var e in ledBack.events) Debug.Log("[PryingEyes] desertotest pelle tornando: un luogo smette di vedersi " + e);
            }
            int loaded = dd.SitesLoadedTotal - loaded0, unloaded = dd.SitesUnloadedTotal - unloaded0;

            // ---- 4. il caso peggiore: lasciare in un colpo il luogo con il guscio completo e la pelle piu' lunga da costruire che non e' mai stata costruita (come rinascere alla scalinata dopo essere morti lontano):
            // il suo guscio esce dal raggio nel giro di Plan dopo e la pelle, nata spenta, si costruisce solo da allora, a un pezzo o piu' per fotogramma
            if (pl.Riding) pl.AbortRide();
            yield return GoTo(spot.x, spot.z, 2f);
            yield return WaitFor(() => LoadQuiet(dd), 90f);
            yield return new WaitForSeconds(1f);
            var unbuiltKinds = new List<string>(); int unbuiltN = 0, worstPieces = 0; Site worstSite = null;
            foreach (var rt in dd.Loaded)
            {
                if (!rt.ShellComplete) continue;
                var sk = dd.FarSkinOf(rt.site);
                if (sk == null || sk.Vertices > 0) continue;
                unbuiltN++; unbuiltKinds.Add($"{rt.site.kind}/{rt.site.state} {sk.Pieces} pezzi");
                if (sk.Pieces > worstPieces) { worstPieces = sk.Pieces; worstSite = rt.site; }
            }
            var ledJump = new SiteLedger(700f);
            for (int i = 0; i < 3; i++) { ledJump.Sample(pl.transform.position, Time.unscaledDeltaTime); yield return null; }
            Vector2 away;
            if (worstSite != null)
            {
                // a 445 m dal bordo di quel luogo (oltre il raggio di tenuta: 430 m per una torre, 370 per un edificio), dalla parte opposta alla partenza, e dentro i 700 m che si guardano
                var c = new Vector2(worstSite.CentreX, worstSite.CentreZ);
                var dirA = c - new Vector2(spot.x, spot.z); dirA = dirA.sqrMagnitude < 1f ? Vector2.right : dirA.normalized;
                away = c;
                for (float rr = 300f; rr < 900f; rr += 5f) { away = c + dirA * rr; if (SiteEdge(worstSite, new Vector3(away.x, 0f, away.y)) >= 445f) break; }
            }
            else away = new Vector2(spot.x - Mathf.Sin(heading) * 450f, spot.z - Mathf.Cos(heading) * 450f);
            pl.Teleport(new Vector3(away.x, DesertWorld.Height(away.x, away.y) + 2f, away.y));
            float tj = Time.time;
            while (Time.time - tj < 4f) { ledJump.Sample(pl.transform.position, Time.unscaledDeltaTime); yield return null; }
            Debug.Log($"[PryingEyes] desertotest pelle: prima della teleportata {unbuiltN} luoghi con il guscio completo e la pelle mai costruita ({(unbuiltKinds.Count > 0 ? string.Join(", ", unbuiltKinds) : "nessuno")}); si va a {(worstSite != null ? SiteEdge(worstSite, new Vector3(away.x, 0f, away.y)).ToString("F0") : "-")} m dal bordo di {(worstSite != null ? worstSite.kind + "/" + worstSite.state : "nessun luogo")}");
            Debug.Log("[PryingEyes] desertotest " + ledJump.Report("dopo la teleportata"));
            foreach (var e in ledJump.events) Debug.Log("[PryingEyes] desertotest pelle dopo la teleportata: un luogo smette di vedersi " + e);
            yield return GoTo(spot.x, spot.z, 2f);

            if (!quick) Debug.Log($"[PryingEyes] desertotest pelle: durante le due corse si sono caricati {loaded} luoghi e scaricati {unloaded}; la prima corsa {(stOut.reached ? "e' arrivata" : "NON e' arrivata")} ({stOut.dist:F0} m in {stOut.secs:F0} s), la seconda {(stBack.reached ? "e' arrivata" : "NON e' arrivata")} ({stBack.dist:F0} m in {stBack.secs:F0} s)");
            if (!quick)
            {
                Check("pelle: le due corse sono arrivate in fondo", stOut.reached && stBack.reached, $"{stOut.dist:F0} m e {stBack.dist:F0} m su {len:F0}");
                Check("pelle: le corse hanno scaricato e ricaricato dei luoghi (almeno due per verso)", loaded >= 2 && unloaded >= 2, $"{loaded} caricati, {unloaded} scaricati");
                Check($"pelle: andando via nessun luogo entro 700 m resta meno che intero per piu' di {SkinGapFrames} fotogrammi", ledOut.lostMaxFrames <= SkinGapFrames, $"{ledOut.lostRuns} volte, la piu' lunga {ledOut.lostMaxFrames} fotogrammi ({ledOut.lostMax:F3} s), in tutto {ledOut.lostFrames}");
                Check($"pelle: tornando nessun luogo entro 700 m resta meno che intero per piu' di {SkinGapFrames} fotogrammi", ledBack.lostMaxFrames <= SkinGapFrames, $"{ledBack.lostRuns} volte, la piu' lunga {ledBack.lostMaxFrames} fotogrammi ({ledBack.lostMax:F3} s), in tutto {ledBack.lostFrames}");
            }
            Check($"pelle: lasciando in un colpo il luogo con la pelle mai costruita (teleportata) nessuno entro 700 m resta meno che intero per piu' di {SkinGapFrames} fotogrammi", ledJump.lostMaxFrames <= SkinGapFrames, $"{ledJump.lostRuns} volte, la piu' lunga {ledJump.lostMaxFrames} fotogrammi ({ledJump.lostMax:F3} s); {unbuiltN} luoghi avevano la pelle mai costruita, al massimo {worstPieces} pezzi");
            Check("pelle: la pelle non resta accesa sul guscio completo per piu' di mezzo secondo", Mathf.Max(ledOut.bothMax, ledBack.bothMax, ledJump.bothMax) <= 0.5f, $"al massimo {Mathf.Max(ledOut.bothMax, ledBack.bothMax, ledJump.bothMax):F2} s");

            if (pl.Riding) pl.AbortRide();
            DesertBike.BotReset();
            godMode = true; pl.invulnerable = true;
            Drops.disabled = dropsWere;
        }
    }
}
#endif
