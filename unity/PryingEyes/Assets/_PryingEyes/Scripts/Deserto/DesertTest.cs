#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Production.Economy;
using PryingEyes.Cattedrale;
using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Deserto
{
    /// <summary>-autotest -desertotest [-seed N] [-desertshots]: il deserto da soli, con il controller e le cose vere. Parte dalla cattedrale (profilo di
    /// prova azzerato), guarda la pianta, la scalinata e la sabbia, l'orologio e il cielo, l'armeria (prezzi doppi) e le opere che sbloccano libri e
    /// consacrazione (che poi costruisce davvero con le risorse, compra un libro, vede rinnovarsi la Biblioteca e consacra un'arma), poi va alla cappella di benvenuto (si costruisce, si arreda, si entra a piedi, ci sono oggetti da prendere), prende un oggetto e
    /// un'arma libera, sale a piedi al primo piano della torre di riferimento (sette piani), si porta all'ultimo e ammazza il boss (il trofeo va nel
    /// magazzino), passa la notte su sabbia aperta (nemici che girano), e cade nel vuoto (la rete di sicurezza lo riporta su). Con -desertshots fa anche
    /// le fotografie di unity_logs/prove/deserto (alba, mezzogiorno, tramonto, notte, interni). Non tocca il salvataggio di chi gioca.</summary>
    public sealed class DesertTest : MonoBehaviour
    {
        int checks, failed, errors;
        string firstError = "";
        bool shots, ended, ready;
        const float Budget = 1100f;        // la prova finisce comunque entro questo tempo (s)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-desertotest") < 0) return;
            Fabbrica.Azzera();               // profilo di prova: una cattedrale in rovina e un magazzino vuoto
            new GameObject("DesertTest").AddComponent<DesertTest>();
        }

        void Check(string name, bool ok, string detail = "")
        {
            checks++; if (!ok) failed++;
            Debug.Log("[PryingEyes] desertotest " + (ok ? "ok " : "FALLITO ") + name + (detail.Length > 0 ? " (" + detail + ")" : ""));
        }

        void OnLog(string text, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (Application.isBatchMode && text != null && text.StartsWith("Failed to capture screen shot")) return;      // messaggio noto di Unity senza finestra, innocuo (come in SeatsTest)
            errors++;
            if (firstError.Length == 0) firstError = text.Length > 160 ? text.Substring(0, 160) : text;
        }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }

        IEnumerator Start()
        {
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-desertshots") >= 0;
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

        string phaseName = "";
        readonly Dictionary<string, float[]> frames = new Dictionary<string, float[]>();      // fase -> { fotogrammi, somma, massimo, sopra 100 ms }

        void Update()
        {
            if (!ended && Time.realtimeSinceStartup > Budget) { Check("finita entro il tempo", false, $"{Budget:F0} s"); Finish(); }
            if (phaseName.Length == 0 || !ready) return;          // il caricamento (dietro la dissolvenza) non conta: si misura il gioco
            if (!frames.TryGetValue(phaseName, out var f)) frames[phaseName] = f = new float[4];
            float dt = Time.unscaledDeltaTime;
            f[0]++; f[1] += dt; if (dt > f[2]) f[2] = dt; if (dt > 0.1f) f[3]++;
        }

        void Finish()
        {
            if (ended) return;
            ended = true;
            PlayerController.botDir = Vector3.zero;
            Time.timeScale = 1f;
            float worst = 0f;
            foreach (var kv in frames)
            {
                var f = kv.Value;
                Debug.Log($"[PryingEyes] desertotest tempi {kv.Key}: {f[0]:F0} fotogrammi, media {f[1] / Mathf.Max(1f, f[0]) * 1000f:F1} ms, massimo {f[2] * 1000f:F0} ms, {f[3]:F0} sopra 100 ms");
                worst = Mathf.Max(worst, f[2]);
            }
            Check("nessun fotogramma oltre 1,2 secondi (un blocco visibile)", worst < 1.2f, $"il peggiore {worst * 1000f:F0} ms");
            Debug.Log($"[PryingEyes] desertotest stato finale: {Dd?.LoadedSites} luoghi caricati, {DesertNav.BuiltTiles} tasselli di navmesh, {DesertTerrain.I?.ChunkCount} blocchi di sabbia, {Enemy.All.Count} nemici, {DesertPickup.Live.Count} oggetti, {Time.realtimeSinceStartup:F0} s");
            Check("nessun errore o eccezione nel registro durante la prova", errors == 0, errors == 0 ? "" : errors + ", la prima: " + firstError);
            Debug.Log($"[PryingEyes] desertotest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        static FloorDirector Fd => FloorDirector.I;
        static GameManager Gm => GameManager.I;
        static PlayerController Pl => GameManager.I != null ? GameManager.I.player : null;
        static DesertDirector Dd => DesertDirector.I;

        // ------------------------------------------------------------------ strumenti
        IEnumerator Phase(string name, IEnumerator body)
        {
            Debug.Log("[PryingEyes] desertotest --- " + name);
            phaseName = name;
            while (true)
            {
                bool more;
                try { more = body.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check(name + ": nessuna eccezione", false, e.Message); phaseName = ""; yield break; }
                if (!more) { phaseName = ""; yield break; }
                yield return body.Current;
            }
        }

        IEnumerator WaitFor(Func<bool> cond, float timeout)
        {
            float t0 = Time.time;
            while (Time.time - t0 < timeout)
            {
                bool ok;
                try { ok = cond(); } catch { ok = false; }
                if (ok) yield break;
                yield return null;
            }
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
        static Vector3 Centre(Room r, float y) => new Vector3((r.x0 + r.x1) * 0.05f, y, (r.z0 + r.z1) * 0.05f);

        static SiteRt RtOf(Site s)
        {
            if (Dd == null || s == null) return null;
            foreach (var rt in Dd.Loaded) if (rt.site == s) return rt;
            return null;
        }

        static bool AllBuilt(SiteRt rt)
        {
            if (rt == null) return false;
            foreach (var f in rt.fis) if (f == null) return false;
            return true;
        }

        static int Bank() { int n = 0; foreach (var r in Risorse.Tutte) n += Fabbrica.Banca.Get(r.id); return n; }

        /// <summary>Porta il giocatore sulla sabbia in un punto (un po' in alto: la sabbia sotto nasce mentre cade) e aspetta che tocchi terra.</summary>
        IEnumerator GoTo(float x, float z, float lift = 8f)
        {
            var pl = Pl;
            pl.Teleport(new Vector3(x, DesertWorld.Height(x, z) + lift, z));
            yield return null;
            yield return WaitFor(() => DesertTerrain.I != null && DesertTerrain.I.Ready(pl.transform.position, 14f), 25f);
            yield return WaitFor(() => pl.GetComponent<CharacterController>().isGrounded, 12f);
        }

        /// <summary>Cammina lungo la navmesh fino a un punto, come farebbe il giocatore con la levetta (botDir). 'done' dice se e' arrivato.</summary>
        IEnumerator Walk(Vector3 to, float timeout, Action<bool> done, float arrive = 1.2f)
        {
            var pl = Pl; var path = new NavMeshPath();
            if (NavMesh.SamplePosition(to, out var snapped, 3f, NavMesh.AllAreas)) to = snapped.position;
            PlayerController.holdLook = false;
            float t0 = Time.time, until = t0 + timeout, nextTrace = t0 + 4f; bool ok = false;
            string trace = "";
            while (Time.time < until)
            {
                Vector3 pos = pl.transform.position;
                if (Flat(pos, to) < arrive && Mathf.Abs(pos.y - to.y) < 1.3f) { ok = true; break; }
                Vector3 next = to;
                bool onMesh = NavMesh.SamplePosition(pos, out var from, 2.5f, NavMesh.AllAreas);
                if (onMesh && NavMesh.CalculatePath(from.position, to, NavMesh.AllAreas, path) && path.corners.Length > 1)
                {
                    next = path.corners[1];
                    if (Flat(next, pos) < 0.5f && path.corners.Length > 2) next = path.corners[2];     // come il bot dei piani: l'angolo gia' raggiunto non conta
                }
                Vector3 d = next - pos; d.y = 0f;
                PlayerController.botDir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero;
                if (Time.time > nextTrace && trace.Length < 700)
                {
                    nextTrace += 4f;
                    trace += $" [{Time.time - t0:F0}s pos {pos:F1} sulla navmesh {onMesh} angoli {path.corners.Length} prossimo {next:F1} dir {PlayerController.botDir:F1}]";
                }
                yield return null;
            }
            PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
            if (!ok) Debug.Log("[PryingEyes] desertotest navmesh: " + NavInfo(pl.transform.position, to) + "; traccia:" + trace);
            done(ok);
        }

        /// <summary>Perche' una camminata non e' arrivata: dove sta il giocatore rispetto alla navmesh, la destinazione e il percorso.</summary>
        static string NavInfo(Vector3 pos, Vector3 to)
        {
            var path = new NavMeshPath();
            bool a = NavMesh.SamplePosition(pos, out var from, 2.5f, NavMesh.AllAreas);
            bool b = NavMesh.SamplePosition(to, out var dest, 3f, NavMesh.AllAreas);
            bool pc = a && NavMesh.CalculatePath(from.position, b ? dest.position : to, NavMesh.AllAreas, path);
            string last = path.corners.Length > 0 ? path.corners[path.corners.Length - 1].ToString("F1") : "-";
            var tri = NavMesh.CalculateTriangulation();
            return $"da {pos:F1}: sulla navmesh {a}" + (a ? $" ({from.position:F1})" : "") + $"; destinazione {to:F1} sulla navmesh {b}" + (b ? $" ({dest.position:F1})" : "")
                + $"; percorso {(pc ? path.status.ToString() : "nessuno")}, {path.corners.Length} angoli, ultimo {last}; {DesertNav.BuiltTiles} tasselli, copre qui {DesertNav.Covers(pos)}, {tri.indices.Length / 3} triangoli";
        }

        void Face(Vector3 target)
        {
            var pl = Pl;
            Vector3 d = target - pl.cam.position;
            pl.yaw = Mathf.Atan2(d.x, d.z);
            pl.pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude);
        }

        void Shot(string name)
        {
            if (!shots) return;
            Directory.CreateDirectory(TestPaths.Out("deserto"));
            GameManager.CamShot(TestPaths.Out("deserto/" + name + ".png"), null, true, 1600, 900);
        }

        /// <summary>Una fotografia a un'ora precisa (il tempo si ferma un attimo), poi il tempo riparte.</summary>
        IEnumerator Snap(string name, float hour, Vector3 look, float settle = 1.4f)
        {
            if (!shots) yield break;
            var sky = DesertSky.I;
            if (sky != null) sky.SetHour(hour, true);
            Face(look);
            yield return new WaitForSeconds(settle);
            Face(look);
            yield return null;
            Shot(name);
            yield return null; yield return null;
            if (sky != null) sky.Frozen = false;
        }

        static Vector2 OpenSand()
        {
            var list = new List<Site>();
            for (int k = 0; k < 24; k++)
            {
                float a = (k * 47f + 20f) * Mathf.Deg2Rad, r = 260f + 15f * (k % 4);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                DesertWorld.SitesNear(x, z, 110f, list);
                if (list.Count == 0) return new Vector2(x, z);
            }
            return new Vector2(300f, 300f);
        }

        // ------------------------------------------------------------------ la prova
        /// <summary>-desertonly a,b,c: solo queste fasi (dopo l'arrivo, che c'e' sempre): scalinata, cielo, cattedrale, opere, cappella, oggetti, suono, torre, boss, catacomba, notte, morte, lontano, caduta, foto.</summary>
        static bool Wants(string phase)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-desertonly") return Array.IndexOf(a[i + 1].Split(','), phase) >= 0;
            return true;
        }

        /// <summary>Le fasi lunghe girano solo se chieste con -desertonly (soak, varieta, prestazioni).</summary>
        static bool Explicit(string phase)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-desertonly") return Array.IndexOf(a[i + 1].Split(','), phase) >= 0;
            return false;
        }

        IEnumerator Run()
        {
            yield return Phase("arrivo", Arrival());
            if (!ready) yield break;
            if (Wants("scalinata")) yield return Phase("scalinata", Stairs());
            if (Wants("cielo")) yield return Phase("orologio e cielo", SkyAndClock());
            if (Wants("cattedrale")) yield return Phase("armeria e opere", Cathedral());
            if (Wants("opere")) yield return Phase("libri e consacrazione", Works());
            if (Wants("cappella")) yield return Phase("cappella", Chapel());
            if (Wants("oggetti")) yield return Phase("oggetti e armi libere", Loot());
            if (Wants("suono")) yield return Phase("suono e strada di casa", SoundAndCompass());
            if (Wants("torre")) yield return Phase("torre", Tower());
            if (Wants("boss")) yield return Phase("boss", Boss());
            if (Wants("catacomba")) yield return Phase("catacomba", Catacomb());
            if (Wants("notte")) yield return Phase("notte", Night());
            if (Wants("morte")) yield return Phase("morte", Death());
            if (Wants("lontano")) yield return Phase("lontano", Far());
            if (Explicit("soak")) yield return Phase("soak", Soak());
            if (Explicit("varieta")) yield return Phase("varieta' dei luoghi", Variety());
            if (Explicit("prestazioni")) yield return Phase("prestazioni", Perf());
            if (Wants("caduta")) yield return Phase("caduta", Fall());
            if (shots && Wants("foto")) yield return Phase("fotografie", Photos());
        }

        IEnumerator Arrival()
        {
            float t0 = Time.time;
            while ((Fd == null || !Fd.InHub || Fd.transitioning || Fd.cur == null || !DesertMode.Active || Fd.Desert == null) && Time.time - t0 < 90f) yield return null;
            yield return new WaitForSeconds(0.5f);
            var fd = Fd; var gm = Gm;
            Check("la partita e' nel deserto, da soli", DesertMode.Active && fd != null && fd.Desert != null && fd.InHub, fd == null ? "nessun regista" : $"Active {DesertMode.Active}, seme {DesertMode.Seed}");
            if (fd == null || fd.cur == null || gm == null || gm.player == null || fd.Desert == null) yield break;
            ready = true;
            gm.player.invulnerable = true;
            var plan = fd.cur.plan;
            Check("la cattedrale e' la pianta di sempre, con l'armeria", plan.hub && plan.rooms.Count > HubPlan.Armeria && HubPlan.Validate(plan).Count == 0, string.Join("; ", HubPlan.Validate(plan)));
            Check("c'e' la sabbia, il cielo e la navmesh", DesertTerrain.I != null && DesertSky.I != null && DesertNav.BuiltTiles > 0, $"{DesertTerrain.I?.ChunkCount} blocchi, {DesertNav.BuiltTiles} tasselli");
            var cm = Camera.main;
            Check("la camera ha lo sfondo a colore pieno e il piano lontano lungo (il cielo della scena non copre la cupola)", cm != null && cm.clearFlags == CameraClearFlags.SolidColor && cm.farClipPlane >= 1000f, cm == null ? "nessuna camera" : $"{cm.clearFlags}, piano lontano {cm.farClipPlane:F0} m");
            var below = new Vector3(18f, 0f, -12f);
            Check("la sabbia e la navmesh ai piedi della scalinata sono pronte", DesertTerrain.I != null && DesertTerrain.I.Ready(below, 45f) && DesertNav.ReadyAround(below, 40f));
            Check("l'opera dei libri e della consacrazione e' attiva solo nel deserto", Opere.IncludeDesert);
            var p = gm.player.transform.position;
            Check("si nasce sul sagrato, al piano della chiesa", Mathf.Abs(p.y) < 0.6f && DesertWorld.DistToCathedral(p.x, p.z) < 0.5f, p.ToString("F1"));
            Check("nessun nemico nella cattedrale", Enemy.All.Count == 0 || Dd == null, Enemy.All.Count + " nemici");
            Shot("01_arrivo");
        }

        IEnumerator Stairs()
        {
            var pl = Pl;
            float cx = (HubPlan.StairX0 + HubPlan.StairX1) * 0.5f;
            bool profile = true; string bad = "";
            for (int i = 1; i <= DesertCathedral.Steps; i++)
            {
                float z = -1f - 0.4f * (i - 1) - 0.2f;           // il mezzo del gradino i
                bool hit = Physics.Raycast(new Vector3(cx, 3f, z), Vector3.down, out var h, 10f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
                if (!hit || Mathf.Abs(h.point.y + 0.2f * i) > 0.06f) { profile = false; bad = $"gradino {i}: " + (hit ? h.point.y.ToString("F2") : "niente"); break; }
            }
            Check("la scalinata ha dodici gradini da venti centimetri", profile, bad);
            bool sand = Physics.Raycast(new Vector3(cx, 3f, -12f), Vector3.down, out var hs, 10f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hs.point.y - DesertWorld.GroundY) < 0.3f;
            Check("ai piedi della scalinata c'e' la sabbia alla quota giusta", sand, sand ? "" : "nessun suolo a quella quota");
            Check("la sabbia sotto la scalinata non copre i gradini", !Physics.Raycast(new Vector3(cx, DesertWorld.GroundY + 0.5f, -3.4f), Vector3.up, 1f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore));
            // giu' a piedi, dritti, come fa il giocatore
            pl.Teleport(new Vector3(cx, 0.06f, 1.4f));
            yield return new WaitForSeconds(0.3f);
            PlayerController.holdLook = false;
            float until = Time.time + 25f;
            while (Time.time < until && pl.transform.position.z > -9f) { PlayerController.botDir = Vector3.back; yield return null; }
            PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
            yield return new WaitForSeconds(0.6f);
            var p = pl.transform.position;
            Check("si scende la scalinata a piedi e si arriva sulla sabbia", p.z < -8f && Mathf.Abs(p.y - DesertWorld.GroundY) < 0.5f, p.ToString("F1"));
            // la navmesh: la sabbia si cammina, la cattedrale (sagrato e scalinata) no
            bool onSand = NavMesh.SamplePosition(new Vector3(cx, DesertWorld.GroundY, -14f), out _, 1.5f, NavMesh.AllAreas);
            bool onStairs = NavMesh.SamplePosition(new Vector3(cx, -1.2f, -3.4f), out _, 1.2f, NavMesh.AllAreas);
            bool inChurch = NavMesh.SamplePosition(new Vector3(cx, 0f, 8f), out _, 1.5f, NavMesh.AllAreas);
            Check("i nemici camminano sulla sabbia", onSand, onSand ? "" : NavInfo(new Vector3(cx, DesertWorld.GroundY, -14f), new Vector3(cx, DesertWorld.GroundY, -30f)));
            // i tasselli gia' costruiti restano: un tassello nuovo non cancella i vicini (Unity lo fa se non glielo si dice)
            yield return WaitFor(() => DesertNav.ReadyAround(pl.transform.position, 90f), 25f);
            string lost = "";
            foreach (var q in new[] { new Vector2(18f, -30f), new Vector2(-30f, -30f), new Vector2(-40f, -90f), new Vector2(18f, -100f), new Vector2(60f, -40f) })
                if (!NavMesh.SamplePosition(new Vector3(q.x, DesertWorld.GroundY, q.y), out _, 1.5f, NavMesh.AllAreas)) lost += $" ({q.x:F0}, {q.y:F0})";
            Check("la navmesh tiene tutti i tasselli costruiti attorno", lost.Length == 0, lost.Length == 0 ? "" : "senza navmesh:" + lost + "; " + DesertNav.BuiltTiles + " tasselli");
            Check("la cattedrale e' un santuario: niente navmesh sulla scalinata ne' dentro", !onStairs && !inChurch, $"scalinata {onStairs}, chiesa {inChurch}");
            // risalire
            yield return Snap("02_sabbia_cattedrale", 7.2f, new Vector3(cx, 11f, 0f));
        }

        IEnumerator SkyAndClock()
        {
            var clock = DesertMode.Clock; var sky = DesertSky.I;
            Check("si e' partiti la mattina presto", clock.hour >= DesertClock.StartHour - 0.01f && clock.hour < DesertClock.StartHour + 1.2f && !clock.IsNight, $"ore {clock.hour:F2}");
            float h0 = clock.hour;
            yield return new WaitForSeconds(4f);
            Check("l'orologio avanza (venti minuti al giorno)", clock.hour > h0 + 0.03f && clock.hour < h0 + 0.2f, $"{h0:F3} -> {clock.hour:F3}");
            if (sky == null) yield break;
            sky.SetHour(12f, true);
            yield return null; yield return null;
            Check("a mezzogiorno il sole e' alto e il cielo e' chiaro", sky.State.sunHeight > 0.9f && sky.State.daylight > 0.9f, $"sole {sky.State.sunHeight:F2}, luce {sky.State.daylight:F2}");
            float sun = 0f;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.enabled) sun = Mathf.Max(sun, l.intensity);
            Check("a mezzogiorno il sole e' acceso", sun > 1f, $"intensita' {sun:F2}");
            sky.SetHour(0f, true);
            yield return null; yield return null;
            Check("a mezzanotte e' buio, con le stelle", sky.State.night > 0.95f && sky.State.stars > 0.9f, $"notte {sky.State.night:F2}, stelle {sky.State.stars:F2}");
            sky.SetHour(17.5f, true);
            yield return null; yield return null;
            Check("al tramonto il sole e' basso e la luce e' radente", sky.State.sunHeight > 0.02f && sky.State.sunHeight < 0.25f && sky.State.twilight > 0.05f, $"sole {sky.State.sunHeight:F2}, crepuscolo {sky.State.twilight:F2}");
            sky.SetHour(18.4f, true);
            yield return null; yield return null;
            Check("dopo il tramonto il sole e' sotto l'orizzonte e c'e' il crepuscolo", sky.State.sunHeight < 0f && sky.State.twilight > 0.3f, $"sole {sky.State.sunHeight:F2}, crepuscolo {sky.State.twilight:F2}");
            sky.SetHour(7.2f, false);
        }

        IEnumerator Cathedral()
        {
            var fd = Fd; var plan = fd.cur.plan; var arm = plan.rooms[HubPlan.Armeria];
            int n = 0, bad = 0, free = 0; string first = "";
            foreach (var w in FindObjectsByType<WallBuy>(FindObjectsSortMode.None))
            {
                var p = w.transform.position;
                if (p.x < arm.x0 * 0.1f - 1f || p.x > arm.x1 * 0.1f + 1f || p.z < arm.z0 * 0.1f - 1f || p.z > arm.z1 * 0.1f + 1f) continue;
                n++;
                if (w.Free) free++;
                int want = WeaponDefs.All[w.weaponIndex].price * RoomKits.ArmoryPriceMul;
                if (w.price != want || w.Free) { bad++; if (first.Length == 0) first = $"{w.displayName}: {w.price} invece di {want}"; }
            }
            Check("l'armeria ha le armi buone sui muri", n >= 20, n + " pannelli");
            Check("nell'armeria tutto costa il doppio e niente e' gratis", n > 0 && bad == 0 && free == 0, bad == 0 ? $"{n} pannelli" : bad + " sbagliati, il primo " + first);
            Check("nell'armeria non c'e' la pistola di partenza", !HasPanel(WeaponDefs.IndexOf("p9_pieta")));
            bool lib = false, altar = false;
            foreach (var s in FindObjectsByType<CantiereOpera>(FindObjectsSortMode.None)) { if (s.opera == "biblioteca_voti") lib = true; if (s.opera == "altare") altar = true; }
            Check("la Biblioteca dei Voti e' un cantiere da costruire con le risorse della cattedrale", lib && !Fabbrica.Costruita("biblioteca_voti"), $"cantiere {lib}");
            // la consacrazione si sblocca dopo l'altare, come ogni opera con un prerequisito: il suo cantiere compare quando l'altare c'e'
            var cons = Opere.Of("consacrazione");
            bool inTree = false;
            foreach (var o in Opere.Elenco) if (o.id == "consacrazione") inTree = true;
            Check("l'Altare della Consacrazione e' tra le opere, dopo l'altare", inTree && cons != null && Array.IndexOf(cons.richiede, "altare") >= 0 && altar && !Fabbrica.Costruita("consacrazione"), $"nell'albero {inTree}, cantiere dell'altare {altar}");
            yield break;
        }

        // ------------------------------------------------------------------ le opere: libri e consacrazione, costruiti e usati davvero
        static T FindOne<T>(Func<T, bool> pick) where T : UnityEngine.Object
        {
            foreach (var x in FindObjectsByType<T>(FindObjectsSortMode.None)) if (pick(x)) return x;
            return null;
        }

        /// <summary>La cattedrale si e' rifatta dopo un'opera costruita: di nuovo ferma, con la sua pianta.</summary>
        IEnumerator WaitHub(float timeout = 30f)
        {
            yield return new WaitForSeconds(0.25f);
            float t0 = Time.time;
            while ((Fd == null || !Fd.InHub || Fd.transitioning || Fd.cur == null || Dd == null) && Time.time - t0 < timeout) yield return null;
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Si mette davanti a una cosa, la guarda e la usa con F come farebbe il giocatore (Interact.Tick e TryUse): dice se F l'avrebbe davvero scelta.</summary>
        IEnumerator UseWithF(IInteract it, string what)
        {
            var pl = Pl;
            Vector3 target = it.Point, from = target + Vector3.back * 1.3f;
            if (it is Component comp)
            {
                var f = comp.transform.forward; f.y = 0f;
                if (f.sqrMagnitude < 0.1f) f = Vector3.back;
                f.Normalize();
                // davanti, dietro o di lato: dove c'e' posto per stare in piedi (i leggii sono a un metro e tre quarti l'uno dall'altro)
                foreach (var d in new[] { f, -f, Vector3.Cross(f, Vector3.up), -Vector3.Cross(f, Vector3.up) })
                {
                    var q = target + d * 1.3f; q.y = 0.06f;
                    if (Physics.CheckCapsule(q + Vector3.up * 0.4f, q + Vector3.up * 1.5f, 0.3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    from = q; break;
                }
            }
            from.y = 0.06f;
            pl.Teleport(from);
            yield return null;
            Face(target);
            yield return new WaitForSeconds(0.15f);
            bool selected = Interact.Tick(pl.cam) && Interact.Current == it;
            Check(what + ": si guarda e si usa con F", selected, selected ? "" : $"F userebbe {Interact.Current?.GetType().Name ?? "niente"}");
            if (selected) Interact.TryUse(); else it.Use();
            yield return new WaitForSeconds(0.2f);
        }

        /// <summary>Costruisce un'opera con le sue risorse, dal suo cantiere e con F; aspetta che la cattedrale si rifaccia.</summary>
        IEnumerator BuildWork(string id, Bundle cost)
        {
            Fabbrica.Banca.Deposit(cost);
            var site = FindOne<CantiereOpera>(s => s.opera == id);
            Check($"il cantiere di {id} dice che si puo' costruire", site != null && site.Prompt != null && site.Prompt.StartsWith("COSTRUISCI"), site == null ? "nessun cantiere" : site.Prompt ?? "senza scritta");
            if (site == null) yield break;
            yield return UseWithF(site, "cantiere di " + id);
            yield return WaitHub();
            Check($"{id} e' costruita e la cattedrale si e' rifatta senza il suo cantiere", Fabbrica.Costruita(id) && FindOne<CantiereOpera>(s => s.opera == id) == null);
        }

        /// <summary>Costruisce la Biblioteca dei Voti e l'Altare della Consacrazione con le risorse della cattedrale, compra un libro con i punti (e il perk arriva), vede
        /// i leggii rinnovarsi, e consacra l'arma in mano all'altare: tutto con F, nella cattedrale in mezzo al deserto.</summary>
        IEnumerator Works()
        {
            var fd = Fd; var gm = Gm; var pl = Pl;
            if (fd == null || fd.cur == null) yield break;
            pl.Teleport(new Vector3(18f, 0.06f, 2.2f));
            yield return new WaitForSeconds(0.3f);
            // ---- la Biblioteca dei Voti
            yield return BuildWork("biblioteca_voti", Bundle.Of((Risorse.Legno, 30), (Risorse.Ossa, 30), (Risorse.Sangue, 2)));
            Check("la Biblioteca dei Voti e' pagata esattamente", Fabbrica.Banca.Get(Risorse.Legno) == 0 && Fabbrica.Banca.Get(Risorse.Ossa) == 0 && Fabbrica.Banca.Get(Risorse.Sangue) == 0, Fabbrica.Banca.Snapshot().ToString());
            var lib = Biblioteca.Current;
            Check("nella navata ci sono tre leggii con un libro ciascuno", lib != null && lib.stands.Count == 3, lib == null ? "nessuna Biblioteca" : lib.stands.Count + " leggii");
            if (lib == null || lib.stands.Count == 0) yield break;
            var nave = Fd.cur.plan.rooms[HubPlan.Navata];
            bool inNave = true;
            foreach (var st in lib.stands)
            {
                var q = st.transform.position;
                if (q.x < nave.x0 * 0.1f || q.x > nave.x1 * 0.1f || q.z < nave.z0 * 0.1f || q.z > nave.z1 * 0.1f) inNave = false;
            }
            Check("i leggii stanno dentro la navata", inNave);
            var perks = new HashSet<PerkId>();
            foreach (var st in lib.stands) perks.Add(st.perk);
            Check("i tre libri sono tre perk diversi", perks.Count == lib.stands.Count, perks.Count + " perk");
            // ---- si compra un libro con i punti
            gm.AddPoints(5000 - gm.score);
            var pick = lib.stands[lib.stands.Count / 2];
            int price = Perks.NextPrice, perks0 = Perks.Count, score0 = gm.score; var perk = pick.perk;
            yield return UseWithF(pick, "leggio della Biblioteca");
            Check("il libro costa quanto dice, da' il perk e toglie i punti", Perks.Has(perk) && Perks.Count == perks0 + 1 && gm.score == score0 - price, $"perk {Perks.Has(perk)}, punti {score0} -> {gm.score}, prezzo {price}");
            bool others = true;
            foreach (var st in lib.stands) if (st != pick && !st.Sealed) others = false;
            Check("preso un libro gli altri due si richiudono", pick.Taken && others && lib.chosen);
            // ---- dopo un po' i leggii si rinnovano (con il giocatore lontano)
            DesertDirector.LibraryRenewDelay = 2f;
            var away = new Vector3(18f, 0.06f, 2.2f);
            if (Flat(away, lib.root.transform.position) < 6f) away = new Vector3(18f, 0.06f, 14f);
            pl.Teleport(away);
            yield return WaitFor(() => Biblioteca.Current != null && Biblioteca.Current != lib, 14f);
            var lib2 = Biblioteca.Current;
            DesertDirector.LibraryRenewDelay = 45f;
            Check("la Biblioteca si rinnova con tre libri nuovi dopo un po'", lib2 != null && lib2 != lib && lib2.stands.Count >= 1 && !lib2.chosen, lib2 == null ? "nessuna Biblioteca" : lib2 == lib ? "la stessa di prima" : lib2.stands.Count + " leggii");
            bool fresh = lib2 != null;
            if (lib2 != null) foreach (var st in lib2.stands) if (Perks.Has(st.perk)) fresh = false;
            Check("i libri nuovi non sono quello che hai gia'", fresh);
            // ---- l'altare della consacrazione: servono le volte, l'altare maggiore e poi la sua opera
            yield return BuildWork("volte", Bundle.Of((Risorse.Legno, 40), (Risorse.Acciaio, 20)));
            yield return BuildWork("altare", Bundle.Of((Risorse.Ossa, 30), (Risorse.Legno, 20), (Risorse.Cuori, 3)));
            yield return BuildWork("consacrazione", Bundle.Of((Risorse.Acciaio, 40), (Risorse.Brace, 5), (Risorse.Cuori, 3), (Risorse.Reliquie, 1)));
            var altar = Altare.Current;
            Check("l'Altare della Consacrazione c'e' nella cattedrale", altar != null && fd.cur.RoomAt(altar.transform.position) != null, altar == null ? "nessun altare" : altar.transform.position.ToString("F1"));
            if (altar == null) yield break;
            var w = pl.weapon;
            Check("l'arma in mano si puo' consacrare", w.CanConsecrate && !w.Upgraded, w.Def.name);
            gm.AddPoints(4000 - gm.score);
            int s1 = gm.score;
            yield return UseWithF(altar, "altare della consacrazione");
            Check("la consacrazione costa quanto dice e l'arma resta consacrata", w.Upgraded && gm.score == s1 - Altare.Price, $"consacrata {w.Upgraded}, punti {s1} -> {gm.score}");
            Check("un'arma consacrata non si consacra due volte", altar.Prompt != null && altar.Prompt.Contains("GIÀ CONSACRATA"), altar.Prompt ?? "senza scritta");
            // il deserto non e' stato toccato dalle ricostruzioni
            Check("la cattedrale rifatta lascia il deserto com'e'", DesertTerrain.I != null && Dd != null && Fd.InHub && DesertNav.ReadyAround(new Vector3(18f, 0f, -12f), 40f), $"{DesertNav.BuiltTiles} tasselli");
            // fuori dal deserto le sue due opere escono dall'albero senza andare perse (restano nel file): tornando nel deserto ci sono ancora
            Opere.IncludeDesert = false; Fabbrica.AggiornaOpere();
            bool hidden = !Fabbrica.Albero.Exists("biblioteca_voti") && !Fabbrica.Albero.Exists("consacrazione") && Fabbrica.Costruita("altare");
            Opere.IncludeDesert = true; Fabbrica.AggiornaOpere();
            bool back = Fabbrica.Costruita("biblioteca_voti") && Fabbrica.Costruita("consacrazione") && Fabbrica.Costruita("altare");
            Check("senza il deserto le sue due opere non sono nell'albero ma non vanno perse: tornando ci sono ancora", hidden && back, $"nascoste {hidden}, ritrovate {back}");
        }

        static bool HasPanel(int weaponIndex)
        {
            foreach (var w in FindObjectsByType<WallBuy>(FindObjectsSortMode.None)) if (w.weaponIndex == weaponIndex) return true;
            return false;
        }

        IEnumerator Chapel()
        {
            var s = DesertWorld.SiteOf(0, -1);
            Check("la cappella di benvenuto e' dove deve", s != null && s.kind == SiteKind.Cappella && s.plan != null, s?.ToString() ?? "manca");
            if (s == null || s.plan == null) yield break;
            yield return GoTo(s.CentreX, s.fz0 - 12f);
            yield return WaitFor(() => { var rt = RtOf(s); return rt != null && AllBuilt(rt) && rt.DressedRooms >= rt.DressableRooms && DesertNav.Covers(new Vector3(s.CentreX, s.padY, s.CentreZ)); }, 70f);
            var rt0 = RtOf(s);
            Check("il luogo si costruisce quando ci si avvicina", rt0 != null && AllBuilt(rt0), rt0 == null ? "non caricato" : "");
            if (rt0 == null || !AllBuilt(rt0)) yield break;
            Check("le stanze si arredano", rt0.DressedRooms >= rt0.DressableRooms && rt0.DressedRooms > 0, $"{rt0.DressedRooms} di {rt0.DressableRooms}");
            Check("le stanze del piano sono quelle del luogo", rt0.fis[0].plan.rooms.Count == s.plan.rooms.Count, $"{rt0.fis[0].plan.rooms.Count} / {s.plan.rooms.Count}");
            int pickups = 0;
            foreach (var pk in DesertPickup.Live)
                if (pk != null && pk.transform.position.x > s.fx0 - 6f && pk.transform.position.x < s.fx1 + 6f && pk.transform.position.z > s.fz0 - 6f && pk.transform.position.z < s.fz1 + 6f) pickups++;
            Check("nelle stanze c'e' qualcosa da prendere", pickups >= 1, pickups + " oggetti");
            Vector3 to = Centre(s.plan.rooms[1], s.padY + 0.1f);
            bool entered = false;
            yield return Walk(to, 70f, ok => entered = ok);
            Check("dalla sabbia si entra nella cappella a piedi", entered, Pl.transform.position.ToString("F1") + " verso " + to.ToString("F1"));
            yield return LevelView("cappella");
            yield return Snap("06_cappella_interno", 9f, to + new Vector3(0f, 1.5f, 3f));
        }

        IEnumerator Loot()
        {
            var pl = Pl; var gm = Gm;
            DesertPickup target = null; float best = float.MaxValue;
            foreach (var pk in DesertPickup.Live)
            {
                if (pk == null) continue;
                float d = (pk.transform.position - pl.transform.position).sqrMagnitude;
                if (d < best) { best = d; target = pk; }
            }
            Check("ci sono oggetti da prendere vicino", target != null, DesertPickup.Live.Count + " in giro");
            if (target != null)
            {
                int taken0 = DesertPickup.statTaken, score0 = gm.score; var kind = target.kind; int amount = target.amount;
                long mem = target.memory;
                pl.Teleport(target.transform.position + new Vector3(0f, 0.06f, 0f));
                yield return new WaitForSeconds(0.6f);
                Check("passandoci vicino l'oggetto si prende", DesertPickup.statTaken == taken0 + 1 && (kind != PickKind.Points || gm.score == score0 + amount), $"{kind} {amount}, punti {score0} -> {gm.score}");
                Check("quello preso non rinasce (resta segnato)", mem == 0 || DesertMode.Taken.Contains(mem), "memoria " + mem);
            }
            // un'arma libera, su sabbia aperta
            yield return GoTo(60f, -60f, 4f);
            int start = WeaponDefs.IndexOf("p9_pieta"), idx = -1;
            for (int w = 0; w < WeaponDefs.All.Length; w++)
                if (w != start && !WeaponDefs.All[w].cityOnly && WeaponDefs.All[w].grade <= 1 && pl.weapon.SlotOf(w) < 0) { idx = w; break; }
            Check("tra le armi comuni ce n'e' una da trovare", idx >= 0);
            if (idx < 0) yield break;
            Vector3 flat = pl.cam.forward; flat.y = 0f; flat.Normalize();
            Vector3 at = pl.cam.position + flat * 1.3f;
            var wb = WallBuy.Place(idx, 0, at, Quaternion.LookRotation(-flat), WeaponDefs.All[idx].GradeColor, true);
            Face(at);
            yield return new WaitForSeconds(0.25f);
            int score1 = gm.score;
            bool seen = WallBuy.Tick(pl.cam);
            WallBuy.TryBuy();
            yield return new WaitForSeconds(0.2f);
            Check("un'arma libera si prende senza punti", wb != null && wb.Free && seen && pl.weapon.SlotOf(idx) >= 0 && gm.score == score1, $"vista {seen}, in mano {pl.weapon.SlotOf(idx) >= 0}, punti {score1} -> {gm.score}");
            if (wb != null) Destroy(wb.gameObject);
        }

        /// <summary>I passi sulla sabbia, il vento all'aperto (e non il ronzio di una stanza), l'eco della chiesa solo in chiesa, e la strada di casa scritta nell'obiettivo.</summary>
        IEnumerator SoundAndCompass()
        {
            var pl = Pl; var gm = Gm;
            var spot = OpenSand();
            yield return GoTo(spot.x, spot.y);
            Check("sulla sabbia i passi sono quelli della sabbia", pl.FootSurface == 3, "superficie " + pl.FootSurface);
            var clip = SoundBank.Step(3);
            Check("il passo sulla sabbia e' un suono di un quinto di secondo", clip != null && clip.length > 0.15f && clip.length < 0.3f, clip == null ? "nessun suono" : clip.length.ToString("F2") + " s");
            yield return new WaitForSeconds(7f);
            var amb = ProcAmbience.Instance; float a = GameSettings.Ambience;
            Check("all'aperto c'e' il vento e non il ronzio di una stanza ne' l'organo", amb != null && amb.WindVolume >= 0.08f * a && amb.RoomVolume <= 0.03f && amb.OrganVolume <= 0.03f, amb == null ? "nessun ambiente" : $"vento {amb.WindVolume:F2}, stanza {amb.RoomVolume:F2}, organo {amb.OrganVolume:F2}, ambiente {a:F2}");
            Check("all'aperto l'eco e' quasi nulla", amb != null && amb.ReverbDecay < 1.0f, amb == null ? "nessun ambiente" : $"decadimento {amb.ReverbDecay:F2} s");
            // la strada di casa: lontano dalla cattedrale l'obiettivo dice a che distanza e da che parte, rispetto a dove si guarda
            DesertWorld.CathedralCentre(out float cx, out float cz);
            var home = new Vector3(cx, pl.cam.position.y, cz);
            Face(home);
            yield return new WaitForSeconds(1.8f);
            string ahead = Dd != null ? Dd.WhereText() : "";
            Check("lontano da casa si legge a che distanza e da che parte e' la cattedrale (guardandola: davanti)", ahead.Contains("cattedrale a ") && ahead.EndsWith(", " + DesertDirector.Where8[0]), ahead);
            var p = pl.transform.position;
            Face(new Vector3(2f * p.x - cx, pl.cam.position.y, 2f * p.z - cz));
            yield return new WaitForSeconds(1.8f);
            string behind = Dd != null ? Dd.WhereText() : "";
            Check("voltandole le spalle la cattedrale e' dietro", behind.Contains("cattedrale a ") && behind.EndsWith(", " + DesertDirector.Where8[4]), behind);
            string obj = Fd.DesertObjective();
            Check("l'obiettivo e' su due righe: prima l'ora e la strada, poi il consiglio", obj.Contains("\n") && obj.Substring(0, obj.IndexOf('\n')).Contains("cattedrale a ") && obj.Substring(obj.IndexOf('\n')).Contains("Torri e catacombe"), obj.Replace("\n", " / "));
            Check("la prima riga dell'obiettivo sta nella larghezza della scritta", obj.IndexOf('\n') <= 110, obj.IndexOf('\n') + " caratteri");
            // in chiesa l'eco e' quello della chiesa, e non si cammina sulla sabbia
            var nave = Fd.cur.plan.rooms[HubPlan.Navata];
            pl.Teleport(Centre(nave, 0.06f));
            yield return new WaitForSeconds(5.5f);
            Check("nella navata l'eco e' quello della chiesa", amb != null && amb.ReverbDecay > 2.0f, amb == null ? "nessun ambiente" : $"decadimento {amb.ReverbDecay:F2} s");
            Check("nella navata i passi non sono quelli della sabbia", pl.FootSurface != 3, "superficie " + pl.FootSurface);
        }

        IEnumerator Tower()
        {
            var s = DesertWorld.SiteOf(0, -2);
            Check("la torre di riferimento ha molti piani", s != null && s.IsTower && s.LevelCount >= 7, s?.ToString() ?? "manca");
            if (s == null || !s.IsTower) yield break;
            yield return GoTo(s.CentreX, s.fz0 - 12f);
            yield return WaitFor(() => AllBuilt(RtOf(s)), 90f);
            var rt = RtOf(s);
            Check("tutti i piani della torre hanno il guscio", AllBuilt(rt), rt == null ? "non caricata" : "");
            if (!AllBuilt(rt)) yield break;
            yield return WaitFor(() => rt.DressedRooms > 3 && DesertNav.ReadyAround(new Vector3(s.CentreX, s.padY, s.CentreZ), 20f) && !DesertNav.Busy, 60f);
            Check("la torre e' alta quanto i suoi piani", s.Height >= 7 * PlanGen.LevelHeight, $"{s.Height:F0} m");
            Vector3 hall0 = Centre(PlanGen.HallRoom(s.levels[0]), s.LevelY(0) + 0.1f);
            Vector3 to = Centre(PlanGen.HallRoom(s.levels[1]), s.LevelY(1) + 0.1f);
            bool inside = false;
            yield return Walk(hall0, 90f, ok => inside = ok);
            Check("dalla sabbia si entra nella torre a piedi", inside, Pl.transform.position.ToString("F1") + " verso " + hall0.ToString("F1"));
            if (!inside) yield break;
            // la scala come la fa il giocatore: fino alla porta con la navmesh, poi su per le rampe da un punto all'altro
            var way = StairWay(s, 0);
            bool atDoor = false;
            yield return Walk(way[0], 45f, ok => atDoor = ok, 1.0f);
            bool climbed = false;
            if (atDoor) yield return Steer(way, 1, 12f, ok => climbed = ok);
            var pp = Pl.transform.position;
            Check("la scala porta a piedi al primo piano della torre", atDoor && climbed && pp.y > s.LevelY(1) - 0.7f, $"{pp:F1}, il piano e' a {s.LevelY(1):F1}");
            bool onHall = false;
            if (climbed) yield return Walk(to, 45f, ok => onHall = ok);
            Check("dal pianerottolo di sopra si arriva nella sala del primo piano", onHall, Pl.transform.position.ToString("F1") + " verso " + to.ToString("F1"));
            // i nemici seguono sulla scala solo se la navmesh unisce i piani
            var path = new NavMeshPath();
            bool a = NavMesh.SamplePosition(hall0, out var h0, 2f, NavMesh.AllAreas), b = NavMesh.SamplePosition(to, out var h1, 2f, NavMesh.AllAreas);
            bool joined = a && b && NavMesh.CalculatePath(h0.position, h1.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Check("la navmesh unisce i piani della torre (i nemici possono seguire sulla scala)", joined, joined ? "" : StairProbe(s, 0));
            yield return Snap("07_torre_primo_piano", 10f, to + new Vector3(0f, 1.5f, 3f));
        }

        /// <summary>I punti da toccare, in ordine, per salire a piedi la scala di un livello (DesertStairs): davanti alla porta, ai piedi della rampa sud, su per i
        /// gradini, il ripiano, la rampa nord e il pianerottolo del livello dopo.</summary>
        static List<Vector3> StairWay(Site s, int level)
        {
            var room = s.levels[level].rooms[0];
            float y0 = s.LevelY(level);
            float ix0 = room.x0 * 0.1f + FloorBuilder.TH, ix1 = room.x1 * 0.1f - FloorBuilder.TH, iz0 = room.z0 * 0.1f + FloorBuilder.TH, iz1 = room.z1 * 0.1f - FloorBuilder.TH;
            float R = DesertStairs.Rise, T = DesertStairs.Tread; int n = DesertStairs.Risers;
            float uPlat = ix1 - DesertStairs.Platform, uTop = uPlat - T * (n - 1);
            float zS = iz0 + DesertStairs.Width * 0.5f, zN = iz1 - DesertStairs.Width * 0.5f, zM = (iz0 + iz1) * 0.5f;
            var w = new List<Vector3>();
            w.Add(new Vector3(ix1 - 0.5f, y0 + 0.1f, zM));                                          // 0: davanti alla porta
            w.Add(new Vector3(uPlat + 0.3f, y0 + 0.1f, zS));                                        // 1: ai piedi della rampa sud
            foreach (int i in new[] { 2, 5, 8, 11, n - 1 }) w.Add(new Vector3(uPlat - T * (i - 1) - T * 0.5f, y0 + R * i + 0.1f, zS));
            w.Add(new Vector3(uTop - 0.6f, y0 + R * n + 0.1f, zS));                                 // il ripiano, lato sud
            w.Add(new Vector3(uTop - 0.6f, y0 + R * n + 0.1f, zN));                                 // il ripiano, lato nord
            foreach (int j in new[] { 2, 5, 8, 11, n - 1 }) w.Add(new Vector3(uTop + T * (j - 1) + T * 0.5f, y0 + R * (n + j) + 0.1f, zN));
            w.Add(new Vector3(uPlat + 0.5f, y0 + PlanGen.LevelHeight + 0.1f, zN));                  // il pianerottolo del livello dopo
            w.Add(new Vector3(ix1 - 0.5f, y0 + PlanGen.LevelHeight + 0.1f, zM));                    // davanti alla porta di sopra
            return w;
        }

        /// <summary>Spinge il giocatore da un punto al successivo in linea retta (come la levetta), a partire dal punto 'from'. 'done' dice se li ha toccati tutti.</summary>
        IEnumerator Steer(List<Vector3> pts, int from, float perPoint, Action<bool> done)
        {
            var pl = Pl; bool all = true;
            PlayerController.holdLook = false;
            for (int k = from; k < pts.Count; k++)
            {
                var p = pts[k]; float until = Time.time + perPoint; bool ok = false;
                while (Time.time < until)
                {
                    Vector3 pos = pl.transform.position;
                    if (Flat(pos, p) < 0.45f && Mathf.Abs(pos.y - p.y) < 0.9f) { ok = true; break; }
                    Vector3 d = p - pos; d.y = 0f;
                    PlayerController.botDir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero;
                    yield return null;
                }
                if (!ok) { all = false; Debug.Log($"[PryingEyes] desertotest scala: fermo a {pl.transform.position:F2} verso il punto {k} {p:F2}"); break; }
            }
            PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
            done(all);
        }

        /// <summary>Dove la navmesh si interrompe lungo la scala di un livello: per ogni punto dice se e' sulla navmesh (e di quanto manca in quota) e se si arriva dal precedente.</summary>
        static string StairProbe(Site s, int level)
        {
            var way = StairWay(s, level);
            var sb = new System.Text.StringBuilder();
            var path = new NavMeshPath(); NavMeshHit prev = default; bool hasPrev = false;
            void One(string name, Vector3 p)
            {
                bool on = NavMesh.SamplePosition(p, out var h, 0.6f, NavMesh.AllAreas);
                sb.Append($" [{name} {(on ? "si, dy " + (h.position.y - p.y).ToString("F2") : "no")}");
                if (on && hasPrev) sb.Append(NavMesh.CalculatePath(prev.position, h.position, NavMesh.AllAreas, path) ? " da prec. " + path.status : " da prec. nessuno");
                sb.Append(']');
                if (on) { prev = h; hasPrev = true; }
            }
            One("sala", Centre(PlanGen.HallRoom(s.levels[level]), s.LevelY(level) + 0.1f));
            for (int k = 0; k < way.Count; k++) One(k.ToString(), way[k]);
            One("sala sopra", Centre(PlanGen.HallRoom(s.levels[level + 1]), s.LevelY(level + 1) + 0.1f));
            return sb.ToString();
        }

        IEnumerator Boss()
        {
            var s = DesertWorld.SiteOf(0, -2);
            var rt = RtOf(s);
            if (s == null || !AllBuilt(rt)) { Check("il boss e' sull'ultimo piano", false, "torre non caricata"); yield break; }
            int top = s.boss;
            Check("il boss e' sull'ultimo piano della torre", top == s.LevelCount - 1 && PlanGen.HallRoom(s.levels[top]).type == RoomType.Boss, "piano " + top);
            var pl = Pl;
            Vector3 stand = Centre(s.levels[top].rooms[2], s.LevelY(top) + 0.1f);
            pl.Teleport(stand);
            yield return WaitFor(() => rt.boss != null, 100f);
            Check("il boss compare nell'ultima sala", rt.boss != null, rt.boss == null ? "non e' comparso" : "");
            if (rt.boss == null) yield break;
            var e = rt.boss;
            yield return BossPhoto("12_torre_sala_del_boss", e, rt, top);
            float y = e.transform.position.y;
            Check("il boss sta al piano alto e non e' un nemico comune", y > s.LevelY(top) - 1f && y < s.LevelY(top) + PlanGen.LevelHeight && e.maxHp >= 3000f, $"y {y:F1} (piano a {s.LevelY(top):F1}), vita {e.maxHp:F0}");
            int bank0 = Bank(), downs0 = DesertMode.BossesDown.Count;
            e.Damage(1e7f, e.transform.position + Vector3.up, false, false);
            yield return WaitFor(() => DesertMode.BossesDown.Count > downs0, 12f);
            Check("quando il boss cade il suo trofeo va nel magazzino della cattedrale", DesertMode.BossesDown.Count == downs0 + 1 && Bank() > bank0, $"magazzino {bank0} -> {Bank()}");
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>Una fotografia del boss nella sua sala. Sul posto la macchina finirebbe dentro il boss (che viene incontro al giocatore): il giocatore si mette
        /// a sei-dieci metri, su un punto della navmesh dello stesso piano da cui il boss si vede (nessun muro in mezzo), e lo guarda.</summary>
        IEnumerator BossPhoto(string name, Enemy boss, SiteRt rt, int unit)
        {
            if (!shots || boss == null) yield break;
            if (rt != null) yield return WaitFor(() => rt.UnitDressed(unit), 25f);      // le luci e gli arredi sono dell'arredo: senza, la sala e' al buio
            if (boss == null) yield break;
            Vector3 c = boss.transform.position, eye = c + Vector3.up * 1.4f;
            bool placed = false;
            foreach (float r in new[] { 8f, 6.5f, 10f, 5f })
            {
                for (int k = 0; k < 12 && !placed; k++)
                {
                    float a = k * 30f * Mathf.Deg2Rad;
                    var want = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if (!NavMesh.SamplePosition(want, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(hit.position.y - c.y) > 1.2f) continue;
                    if (Physics.Linecast(hit.position + Vector3.up * 1.6f, eye, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    Pl.Teleport(hit.position + Vector3.up * 0.05f);
                    placed = true;
                }
                if (placed) break;
            }
            if (!placed) Debug.Log("[PryingEyes] desertotest foto del boss: nessun punto sgombro attorno a " + c.ToString("F1"));
            // nelle sale buie si fa come il giocatore: torcia accesa (T)
            var torch = Torch.I; bool torchWas = torch != null && torch.on;
            if (torch != null && !torch.on) torch.Toggle();
            yield return null;
            Debug.Log($"[PryingEyes] desertotest foto del boss {name}: livello arredato {rt != null && rt.UnitDressed(unit)}, luci accese {LightPool.Active} di {LightPool.Count}, al chiuso {DesertSky.I?.Indoor:F2}, torcia {torch != null && torch.on}");
            if (boss != null) yield return Snap(name, 8f, boss.transform.position + Vector3.up * 1.4f, 0.4f);
            float spread = FrameSpread(out float mean);
            if (spread >= 0f) Check($"{name}: la sala del boss si vede (non e' una nebbia piena ne' un nero)", spread > 0.08f, $"scarto {spread:F3}, luminanza media {mean:F3}");
            if (Diag) yield return LightDiag(name);
            if (torch != null && torch.on && !torchWas) torch.Toggle();
        }

        static bool Diag => Array.IndexOf(Environment.GetCommandLineArgs(), "-desertdiag") >= 0;

        /// <summary>Quanto si distinguono i punti di quello che la camera vede (senza HUD): lo scarto fra il 5% piu' scuro e il 5% piu' chiaro dei punti, in luminanza
        /// 0-1. Una sala sepolta in una nebbia piena o nel nero da' meno di 0,05; una vista vera ne da' molto di piu'. -1 senza scheda grafica (-nographics).</summary>
        float FrameSpread(out float mean)
        {
            mean = 0f;
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return -1f;
            const int W = 320, H = 180;
            var rt = RenderTexture.GetTemporary(W, H, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            Destroy(tex);
            var lum = new float[px.Length];
            for (int i = 0; i < px.Length; i++) { lum[i] = (0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b) / 255f; mean += lum[i]; }
            mean /= lum.Length;
            Array.Sort(lum);
            return lum[(int)(lum.Length * 0.95f)] - lum[(int)(lum.Length * 0.05f)];
        }

        /// <summary>Il posto dove si e' si vede: ci si volta verso la direzione piu' libera (con la torcia accesa, come fa chi gioca nelle sale buie) e si controlla che
        /// l'immagine abbia un contrasto. Prese da sole le fotografie non lo dicono: la sala del boss della catacomba, sepolta in una nebbia piena (la foschia delle
        /// navate dipendeva dall'altezza assoluta), era una foto blu uniforme e nessun controllo se ne accorgeva. Senza scheda grafica non fa niente.</summary>
        IEnumerator LevelView(string what)
        {
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            float bestDist = -1f; Vector3 bestDir = Vector3.forward;
            for (int k = 0; k < 8; k++)
            {
                var d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                float dist = Physics.Raycast(cam.transform.position, d, out var h, 30f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) ? h.distance : 30f;
                if (dist > bestDist) { bestDist = dist; bestDir = d; }
            }
            Face(cam.transform.position + bestDir * Mathf.Min(bestDist, 12f));
            var torch = Torch.I; bool torchWas = torch != null && torch.on;
            if (torch != null && !torch.on) torch.Toggle();
            yield return null; yield return null; yield return null;
            Face(cam.transform.position + bestDir * Mathf.Min(bestDist, 12f));
            yield return null;
            float spread = FrameSpread(out float mean);
            if (shots)
            {
                var slug = new System.Text.StringBuilder("vista_");
                foreach (char ch in what) slug.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '_');
                Shot(slug.ToString());
            }
            if (torch != null && torch.on && !torchWas) torch.Toggle();
            if (spread >= 0f) Check($"{what}: l'immagine ha un contrasto (non e' una nebbia piena ne' un nero)", spread > 0.08f, $"scarto {spread:F3}, luminanza media {mean:F3}");
        }

        /// <summary>-desertdiag: perche' una sala non si vede. Scrive la camera (maschera, piani, occlusione), quanti renderer ci sono a 15 m e quanti sono accesi, nel campo
        /// e visti, e di cosa e' fatto il primo muro davanti (collider, renderer, materiale, shader, strato).</summary>
        static void RenderDiag(string name)
        {
            var cam = Camera.main;
            var pos = cam.transform.position;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int total = 0, on = 0, inView = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || r.bounds.SqrDistance(pos) > 225f) continue;
                total++;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                on++;
                if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) inView++;
            }
            // (Renderer.isVisible non dice niente qui: con -batchmode la camera dello schermo non disegna, le foto sono render a mano)
            Debug.Log($"[PryingEyes] desertotest diag {name}: camera maschera {cam.cullingMask:X}, vicino {cam.nearClipPlane:F2}, lontano {cam.farClipPlane:F0}; renderer a 15 m {total}, accesi {on}, nel campo {inView}");
            if (Physics.Raycast(pos, cam.transform.forward, out var h, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                var rr = h.collider.GetComponent<Renderer>() ?? h.collider.GetComponentInParent<Renderer>();
                string mat = rr == null ? "nessun renderer" : rr.sharedMaterial == null ? "senza materiale" : rr.sharedMaterial.name + " / " + (rr.sharedMaterial.shader != null ? rr.sharedMaterial.shader.name : "senza shader");
                Debug.Log($"[PryingEyes] desertotest diag {name}: davanti a {h.distance:F1} m c'e' {h.collider.name} (padre {h.collider.transform.parent?.name}, strato {LayerMask.LayerToName(h.collider.gameObject.layer)}); renderer {(rr == null ? "-" : rr.enabled + "/" + rr.shadowCastingMode)}; {mat}");
            }
            else Debug.Log($"[PryingEyes] desertotest diag {name}: davanti non c'e' niente entro 60 m");
        }

        /// <summary>-desertdiag: perche' una sala e' buia. Scrive la luce attorno alla camera (ambiente, nebbia, le lampade piu' vicine, la torcia) e fotografa il pavimento e il muro piu' vicino.</summary>
        IEnumerator LightDiag(string name)
        {
            var cam = Camera.main;
            Debug.Log($"[PryingEyes] desertotest diag {name}: ambiente {RenderSettings.ambientMode} {RenderSettings.ambientLight} sopra {RenderSettings.ambientSkyColor} intensita' {RenderSettings.ambientIntensity:F2}; nebbia {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogColor} densita' {RenderSettings.fogDensity:F3}; sfondo {cam.backgroundColor}, {cam.clearFlags}; posizione {cam.transform.position:F1}");
            var lights = new List<Light>(FindObjectsByType<Light>(FindObjectsSortMode.None));
            lights.RemoveAll(l => l == null || !l.enabled || l.type == LightType.Directional);
            lights.Sort((a, b) => (a.transform.position - cam.transform.position).sqrMagnitude.CompareTo((b.transform.position - cam.transform.position).sqrMagnitude));
            for (int i = 0; i < 6 && i < lights.Count; i++)
                Debug.Log($"[PryingEyes] desertotest diag lampada {i}: {lights[i].name} {lights[i].type} a {Vector3.Distance(lights[i].transform.position, cam.transform.position):F1} m, intensita' {lights[i].intensity:F2}, portata {lights[i].range:F1}, colore {lights[i].color}");
            // il muro piu' vicino (otto direzioni) e il pavimento
            float best = 1e9f; Vector3 bestDir = Vector3.forward;
            for (int k = 0; k < 8; k++)
            {
                var d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                if (Physics.Raycast(cam.transform.position, d, out var h, 60f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && h.distance < best) { best = h.distance; bestDir = d; }
            }
            Debug.Log($"[PryingEyes] desertotest diag muro piu' vicino a {best:F1} m, in {bestDir:F1}");
            RenderDiag(name);
            yield return Snap(name + "_muro", 8f, cam.transform.position + bestDir * Mathf.Min(best, 30f), 0.3f);
            RenderDiag(name + "_muro");
            yield return Snap(name + "_pavimento", 8f, cam.transform.position + Vector3.down * 1.6f + cam.transform.forward * 2.5f, 0.3f);
            RenderDiag(name + "_pavimento");
        }

        /// <summary>La catacomba piu' vicina alla cattedrale: dalla sabbia alla sala d'ingresso, giu' per le scale livello per livello fino alla sala del boss.</summary>
        IEnumerator Catacomb()
        {
            Site cata = NearestCatacomb(null);
            Check("c'e' una catacomba non lontana dalla cattedrale", cata != null, cata == null ? "nessuna entro sei celle" : cata.ToString());
            if (cata == null) yield break;
            int top = cata.entrance;
            Check("la catacomba scende da tre a cinque livelli, con il boss nel piu' profondo", cata.LevelCount >= 3 && cata.LevelCount <= 5 && top == cata.LevelCount - 1 && cata.boss == 0 && PlanGen.HallRoom(cata.levels[0]).type == RoomType.Boss, cata.ToString());
            yield return GoTo(cata.CentreX, cata.fz0 - 12f);
            yield return WaitFor(() => AllBuilt(RtOf(cata)), 90f);
            var rt = RtOf(cata);
            Check("tutti i livelli della catacomba hanno il guscio", AllBuilt(rt), rt == null ? "non caricata" : "");
            if (!AllBuilt(rt)) yield break;
            yield return WaitFor(() => rt.DressedRooms > 2 && DesertNav.ReadyAround(new Vector3(cata.CentreX, cata.padY, cata.CentreZ), 20f) && !DesertNav.Busy, 60f);
            // il pozzo: sopra il vano della scala la sabbia non c'e' (le scale salgono fin li')
            bool well = cata.WellRect(out var hr);
            bool open = false;
            if (well)
            {
                var from = new Vector3((hr.x0 + hr.x1) * 0.5f, cata.padY + 2f, (hr.z0 + hr.z1) * 0.5f);
                open = !Physics.Raycast(from, Vector3.down, 2.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);      // la sabbia sotto un luogo sta 8 cm sotto il pavimento: 2 m + 8 cm
            }
            Check("sopra la scala della catacomba la sabbia ha il pozzo", well && open, well && open ? "" : well ? "la sabbia copre il vano" : "nessun pozzo");
            // dalla sabbia alla sala d'ingresso, a piedi
            Vector3 entry = Centre(PlanGen.HallRoom(cata.levels[top]), cata.LevelY(top) + 0.1f);
            bool inside = false;
            yield return Walk(entry, 90f, ok => inside = ok);
            Check("dalla sabbia si entra nella catacomba a piedi", inside, Pl.transform.position.ToString("F1") + " verso " + entry.ToString("F1"));
            if (!inside) yield break;
            yield return LevelView("catacomba, sala d'ingresso");
            if (Diag) yield return LightDiag("10b_catacomba_ingresso");
            // giu' per le scale, un livello alla volta (le rampe di un livello vanno da questo a quello sopra: si fanno al contrario)
            bool all = true;
            for (int lv = top - 1; lv >= 0 && all; lv--)
            {
                var way = StairWay(cata, lv);
                way.Reverse();
                bool atTop = false;
                yield return Walk(way[0], 45f, ok => atTop = ok, 1.0f);
                if (atTop) yield return Steer(way, 1, 12f, ok => all = ok);
                else all = false;
                var pq = Pl.transform.position;
                if (all) all = pq.y < cata.LevelY(lv) + 0.9f && pq.y > cata.LevelY(lv) - 0.9f;
                if (!all) Debug.Log($"[PryingEyes] desertotest catacomba: fermo al livello {lv}, a {pq:F2} (il piano e' a {cata.LevelY(lv):F1})");
                else yield return LevelView($"catacomba, livello {lv} ({cata.LevelY(lv):F0} m)");
            }
            Check("giu' per le scale si arriva al livello del boss a piedi", all, Pl.transform.position.ToString("F1") + $", il livello 0 e' a {cata.LevelY(0):F1}");
            if (!all) yield break;
            Vector3 bossHall = Centre(PlanGen.HallRoom(cata.levels[0]), cata.LevelY(0) + 0.1f);
            bool inHall = false;
            yield return Walk(bossHall, 45f, ok => inHall = ok);
            Check("dal pianerottolo in fondo si arriva nella sala del boss", inHall, Pl.transform.position.ToString("F1") + " verso " + bossHall.ToString("F1"));
            yield return WaitFor(() => rt.boss != null, 60f);
            Check("il boss della catacomba compare nella sala in fondo", rt.boss != null, rt.boss == null ? "non e' comparso" : "");
            if (rt.boss == null) yield break;
            var e = rt.boss;
            yield return BossPhoto("11_catacomba_sala_del_boss", e, rt, cata.boss);
            int bank0 = Bank(), downs0 = DesertMode.BossesDown.Count;
            e.Damage(1e7f, e.transform.position + Vector3.up, false, false);
            yield return WaitFor(() => DesertMode.BossesDown.Count > downs0, 12f);
            Check("quando il boss della catacomba cade il suo trofeo va nel magazzino", DesertMode.BossesDown.Count == downs0 + 1 && Bank() > bank0, $"magazzino {bank0} -> {Bank()}");
        }

        IEnumerator Night()
        {
            var sky = DesertSky.I;
            var spot = OpenSand();
            if (sky != null) sky.SetHour(23.5f, false);
            yield return GoTo(spot.x, spot.y);
            yield return WaitFor(() => Dd != null && Dd.Wanderers >= 2, 80f);
            Check("di notte sulla sabbia aperta girano dei nemici", Dd != null && Dd.Wanderers >= 2, $"{Dd?.Wanderers} nemici a {spot}");
            Check("i nemici sono pochi e non intasano il gioco", Enemy.All.Count <= 24, Enemy.All.Count.ToString());
            Check("il bottino all'aperto nasce attorno al giocatore", Dd != null && Dd.OutdoorCaches > 0, $"{Dd?.OutdoorCaches} cumuli");
            if (sky != null) sky.SetHour(7.2f, false);
        }

        /// <summary>Morire nel deserto: a terra, poi dissanguato. Si perde meta' dei punti e l'ultimo perk, si rinasce sul sagrato (dove si e' nati) e il mondo
        /// resta com'era: lo stesso seme, la cattedrale e la sabbia al loro posto, l'orologio che non si ferma.</summary>
        IEnumerator Death()
        {
            var pl = Pl; var gm = Gm;
            var spot = OpenSand();
            yield return GoTo(spot.x, spot.y);
            while (Perks.Count > 0 && Perks.LoseLast(out _)) { }
            Perks.Add(PerkId.PassiDiCenere); Perks.Add(PerkId.ManiSvelte);
            gm.AddPoints(1000 - gm.score);
            int perks0 = Perks.Count, score0 = gm.score, seed0 = DesertMode.Seed, day0 = DesertMode.Clock.day;
            float t0 = day0 * 24f + DesertMode.Clock.hour;
            pl.invulnerable = false;
            pl.Hurt(99999f);
            yield return null; yield return null;
            Check("a zero punti vita si va a terra", pl.alive && pl.Downed, $"vivo {pl.alive}, a terra {pl.Downed}");
            pl.BleedOutNow();
            yield return null; yield return null;
            Check("dissanguati si muore", !pl.alive);
            yield return WaitFor(() => pl.alive, 12f);
            pl.invulnerable = true;
            Check("si rinasce", pl.alive);
            var p = pl.transform.position;
            Check("la morte costa meta' dei punti e l'ultimo perk, non il primo", gm.score == score0 - score0 / 20 * 10 && Perks.Count == perks0 - 1 && Perks.Has(PerkId.PassiDiCenere) && !Perks.Has(PerkId.ManiSvelte), $"punti {score0} -> {gm.score}, perk {perks0} -> {Perks.Count}");
            Check("si rinasce in cima alla scalinata, anche se l'ultima opera l'abbiamo costruita in fondo alla navata", Mathf.Abs(p.y) < 0.6f && Flat(p, new Vector3(18f, 0f, 2.2f)) < 3f, p.ToString("F1"));
            Check("il mondo e' lo stesso: seme, cattedrale, sabbia e cielo", DesertMode.Active && DesertMode.Seed == seed0 && Fd != null && Fd.InHub && Fd.Desert != null && DesertTerrain.I != null && DesertSky.I != null, $"seme {seed0} -> {DesertMode.Seed}");
            Check("l'orologio non si e' fermato ne' e' tornato indietro", DesertMode.Clock.day * 24f + DesertMode.Clock.hour > t0, $"{t0:F2} -> {DesertMode.Clock.day * 24f + DesertMode.Clock.hour:F2}");
            // la tregua della rinascita (Grace) toglie l'invulnerabilita' dopo 2,5 s: si aspetta e la si rimette, per le fasi dopo
            yield return new WaitForSeconds(2.8f);
            pl.invulnerable = true;
        }

        /// <summary>Il mondo e' quasi infinito: a 5, 9 e 17 km dalla cattedrale la sabbia si costruisce, i piedi stanno sulla navmesh e il primo edificio accanto si
        /// carica con tutti i suoi livelli (come a casa); oltre i 18 km chi cammina e' riportato dentro.</summary>
        IEnumerator Far()
        {
            var pl = Pl;
            float[] km = { 5f, 9f, 17f }, deg = { 37f, 151f, 262f };
            var list = new List<Site>();
            for (int i = 0; i < km.Length; i++)
            {
                float a = deg[i] * Mathf.Deg2Rad, r = km[i] * 1000f, x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                string at = $"a {km[i]:F0} km";
                yield return GoTo(x, z);
                var p = pl.transform.position;
                float h = DesertWorld.Height(p.x, p.z);
                Check($"{at} la sabbia regge i piedi", Mathf.Abs(p.y - h) < 3f && pl.GetComponent<CharacterController>().isGrounded, $"y {p.y:F1}, la sabbia a {h:F1}");
                yield return WaitFor(() => DesertNav.ReadyAround(pl.transform.position, 20f) && !DesertNav.Busy, 45f);
                Check($"{at} c'e' la navmesh sotto i piedi", NavMesh.SamplePosition(pl.transform.position, out _, 2f, NavMesh.AllAreas), $"{DesertNav.BuiltTiles} tasselli");
                // il primo edificio (non una torre: costa meno) a meno di 600 m: ci si porta accanto e si carica
                Site s = null;
                DesertWorld.SitesNear(x, z, 600f, list);
                foreach (var c in list) if (!c.IsTower && (s == null || Flat(new Vector3(c.CentreX, 0f, c.CentreZ), p) < Flat(new Vector3(s.CentreX, 0f, s.CentreZ), p))) s = c;
                if (s == null) { Debug.Log($"[PryingEyes] desertotest {at}: nessun edificio entro 600 m"); continue; }
                yield return GoTo(s.CentreX, s.fz0 - 12f);
                yield return WaitFor(() => AllBuilt(RtOf(s)), 90f);
                Check($"{at} l'edificio accanto si carica con tutti i livelli", AllBuilt(RtOf(s)), RtOf(s) == null ? "non caricato: " + s : s.ToString());
            }
            // il bordo: oltre il raggio del mondo si e' riportati dentro
            float ea = 20f * Mathf.Deg2Rad, er = DesertWorld.WorldRadius + 150f;
            pl.Teleport(new Vector3(Mathf.Cos(ea) * er, DesertWorld.Height(Mathf.Cos(ea) * er, Mathf.Sin(ea) * er) + 4f, Mathf.Sin(ea) * er));
            yield return WaitFor(() => { var q = pl.transform.position; return Mathf.Sqrt(q.x * q.x + q.z * q.z) < DesertWorld.WorldRadius; }, 5f);
            var e = pl.transform.position; float back = Mathf.Sqrt(e.x * e.x + e.z * e.z);
            Check("oltre i 18 km chi cammina e' riportato dentro il mondo", back < DesertWorld.WorldRadius, $"a {back:F0} m dalla cattedrale");
        }

        /// <summary>Solo con -desertonly soak (qualche minuto): il mondo si carica e si scarica mentre si cammina, e la memoria non deve crescere a ogni giro. Si visitano due torri e
        /// due catacombe lontane dalla cattedrale (a 800-1,800 m, a casa sono scaricate), si torna a casa e si contano gli oggetti vivi di Unity (mesh, materiali, texture): il
        /// primo giro carica anche le cose condivise, un giro identico dopo non deve lasciare niente di piu'.</summary>
        IEnumerator Soak()
        {
            var picks = new List<Site>();
            int towers = 0, catas = 0;
            for (int ring = 4; ring <= 9 && picks.Count < 4; ring++)
                for (int cx = -ring; cx <= ring && picks.Count < 4; cx++)
                    for (int cz = -ring; cz <= ring && picks.Count < 4; cz++)
                    {
                        if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) != ring) continue;
                        var s = DesertWorld.SiteOf(cx, cz);
                        if (s == null) continue;
                        if (s.IsTower && towers < 2) { picks.Add(s); towers++; }
                        else if (s.IsCatacomb && catas < 2) { picks.Add(s); catas++; }
                    }
            Check("ci sono due torri e due catacombe lontane da visitare", picks.Count == 4, $"{towers} torri, {catas} catacombe");
            if (picks.Count < 4) yield break;
            var home = OpenSand();
            const int Rounds = 3;
            var meshes = new int[Rounds]; var mats = new int[Rounds]; var texs = new int[Rounds]; var mem = new long[Rounds];
            for (int round = 0; round < Rounds; round++)
            {
                foreach (var s in picks)
                {
                    yield return GoTo(s.CentreX, s.fz0 - 12f);
                    yield return WaitFor(() => { var rt = RtOf(s); return AllBuilt(rt) && rt.DressedRooms > 0; }, 90f);
                    yield return new WaitForSeconds(1f);
                }
                yield return GoTo(home.x, home.y);
                yield return WaitFor(() => Dd != null && Dd.LoadedSites <= 2, 40f);
                yield return new WaitForSeconds(1f);
                yield return WaitFor(() => Dd != null && !Dd.Cleaning, 100f);      // la pulizia della memoria (DesertDirector.CleanTick) parte dopo l'ultimo luogo scaricato, al piu' ogni 60 s
                yield return new WaitForSeconds(1f);
                meshes[round] = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                mats[round] = Resources.FindObjectsOfTypeAll<Material>().Length;
                texs[round] = Resources.FindObjectsOfTypeAll<Texture>().Length;
                mem[round] = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
                Debug.Log($"[PryingEyes] desertotest soak giro {round + 1}: {meshes[round]} mesh, {mats[round]} materiali, {texs[round]} texture, {mem[round]} MB, {Dd?.LoadedSites} luoghi caricati, {DesertTerrain.I?.ChunkCount} blocchi di sabbia, {Dd?.Sweeps} pulizie");
            }
            int d = Rounds - 1;
            Check("la pulizia della memoria parte da sola dopo che un luogo si scarica", Dd != null && Dd.Sweeps >= Rounds, $"{Dd?.Sweeps} pulizie in {Rounds} giri");
            Check("una pulizia non ferma il gioco per piu' di mezzo secondo", Dd != null && Dd.WorstSweepMs < 500f, $"il fotogramma piu' lungo {Dd?.WorstSweepMs:F0} ms, l'ultima pulizia {Dd?.LastSweepMs:F0} ms");
            Check("un giro uguale al precedente non lascia mesh in giro", meshes[d] - meshes[d - 1] <= 40, $"{meshes[d - 1]} -> {meshes[d]}");
            Check("... ne' materiali", mats[d] - mats[d - 1] <= 40, $"{mats[d - 1]} -> {mats[d]}");
            Check("... ne' texture", texs[d] - texs[d - 1] <= 20, $"{texs[d - 1]} -> {texs[d]}");
            Check("... e la memoria non cresce di oltre 50 MB", mem[d] - mem[d - 1] <= 50, $"{mem[d - 1]} -> {mem[d]} MB");
            // la pulizia toglie solo quello che nessuno usa: con una finestra si guarda la sabbia e la navata (cielo, sabbia e arredi non devono essere strappati a chi li usa)
            yield return LevelView("sulla sabbia dopo le pulizie");
            var nave = Fd.cur.plan.rooms[HubPlan.Navata];
            Pl.Teleport(Centre(nave, 0.06f));
            yield return new WaitForSeconds(1f);
            yield return LevelView("nella navata dopo le pulizie");
        }

        /// <summary>Quanto costa disegnare quello che il giocatore vede, in millisecondi, a 1920x1080: si fa il render a mano della camera su una texture e si aspetta la GPU (si legge un
        /// pixel). Nove render, i primi due buttati (il primo di un posto nuovo paga ombre e blocchi), la mediana degli altri sette. -1 senza scheda grafica. Le prove di sempre non lo dicono:
        /// con -batchmode la camera dello schermo non disegna, e i tempi del registro sono quelli della logica del gioco.</summary>
        float RenderMs()
        {
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return -1f;
            var rt = RenderTexture.GetTemporary(1920, 1080, 24);
            var prev = cam.targetTexture; var old = RenderTexture.active;
            cam.targetTexture = rt;
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var ms = new float[9];
            for (int i = 0; i < ms.Length; i++)
            {
                float t0 = Time.realtimeSinceStartup;
                cam.Render();
                RenderTexture.active = rt;
                one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);                // aspetta che la GPU abbia finito
                ms[i] = (Time.realtimeSinceStartup - t0) * 1000f;
            }
            RenderTexture.active = old;
            cam.targetTexture = prev;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(one);
            var rest = new float[ms.Length - 2];
            Array.Copy(ms, 2, rest, 0, rest.Length);
            Array.Sort(rest);
            return rest[rest.Length / 2];
        }

        float perfWorst;

        /// <summary>Un punto della prova delle prestazioni: si aspetta che il mondo attorno si assesti e si misura.</summary>
        IEnumerator PerfAt(string what, float settle = 2f)
        {
            yield return new WaitForSeconds(settle);
            float ms = RenderMs();
            if (ms < 0f) yield break;
            perfWorst = Mathf.Max(perfWorst, ms);
            Debug.Log($"[PryingEyes] desertotest prestazioni {what}: {ms:F1} ms a 1920x1080");
            Check($"{what}: un fotogramma a 1920x1080 costa meno di 33 ms (30 al secondo)", ms < 33f, $"{ms:F1} ms");
        }

        /// <summary>Solo con -desertonly prestazioni (serve la scheda grafica, finestra nascosta): il costo di un fotogramma a 1920x1080 in sette posti: il sagrato verso la sabbia, la sabbia aperta
        /// con la torre in lontananza di giorno e di notte, il piede della torre, la navata, l'interno della cappella e la sala del boss di una catacomba. Sopra i 33 ms un posto e' un guaio.</summary>
        IEnumerator Perf()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Debug.Log("[PryingEyes] desertotest prestazioni: senza scheda grafica non si misura"); yield break; }
            var pl = Pl; var sky = DesertSky.I;
            perfWorst = 0f;
            if (sky != null) sky.SetHour(10f, true);
            pl.Teleport(new Vector3(18f, 0.06f, 1.0f));
            Face(new Vector3(18f, 1.2f, -120f));
            yield return PerfAt("sagrato verso la sabbia");
            var tower = DesertWorld.SiteOf(0, -2);
            if (tower != null)
            {
                var top = new Vector3(tower.CentreX, tower.padY + tower.Height * 0.6f, tower.CentreZ);
                yield return GoTo(tower.CentreX - 40f, tower.fz0 - 130f);
                Face(top);
                yield return PerfAt("sabbia aperta, la torre in lontananza (giorno)", 4f);
                if (sky != null) sky.SetHour(23.5f, true);
                yield return PerfAt("sabbia aperta, la torre in lontananza (notte)");
                if (sky != null) sky.SetHour(10f, true);
                yield return GoTo(tower.CentreX, tower.fz0 - 12f);
                yield return WaitFor(() => AllBuilt(RtOf(tower)), 90f);
                Face(new Vector3(tower.CentreX, tower.padY + tower.Height * 0.9f, tower.CentreZ));
                yield return PerfAt("al piede della torre, guardando in su", 3f);
            }
            var nave = Fd.cur.plan.rooms[HubPlan.Navata];
            pl.Teleport(Centre(nave, 0.06f));
            yield return PerfAt("nella navata della cattedrale");
            var chapel = DesertWorld.SiteOf(0, -1);
            if (chapel != null && chapel.plan != null)
            {
                yield return GoTo(chapel.CentreX, chapel.fz0 - 12f);
                yield return WaitFor(() => { var rt = RtOf(chapel); return rt != null && AllBuilt(rt) && rt.DressedRooms >= rt.DressableRooms; }, 70f);
                pl.Teleport(Centre(chapel.plan.rooms[1], chapel.padY + 0.1f));
                yield return PerfAt("dentro la cappella", 3f);
            }
            var cata = NearestCatacomb(null);
            if (cata != null)
            {
                yield return GoTo(cata.CentreX, cata.fz0 - 12f);
                yield return WaitFor(() => { var rt = RtOf(cata); return AllBuilt(rt) && rt.DressedRooms > 2; }, 90f);
                pl.Teleport(Centre(PlanGen.HallRoom(cata.levels[cata.boss]), cata.LevelY(cata.boss) + 0.1f));
                yield return PerfAt("nella sala del boss di una catacomba", 4f);
            }
            if (sky != null) sky.Frozen = false;
            Debug.Log($"[PryingEyes] desertotest prestazioni: il posto peggiore costa {perfWorst:F1} ms a 1920x1080");
        }

        /// <summary>La catacomba piu' vicina alla cattedrale entro sei celle (senza contare 'except').</summary>
        static Site NearestCatacomb(Site except)
        {
            Site cata = null; float bd = float.MaxValue;
            for (int cx = -6; cx <= 6; cx++)
                for (int cz = -6; cz <= 6; cz++)
                {
                    var c = DesertWorld.SiteOf(cx, cz);
                    if (c == null || !c.IsCatacomb || c == except) continue;
                    float d = c.CentreX * c.CentreX + c.CentreZ * c.CentreZ;
                    if (d < bd) { bd = d; cata = c; }
                }
            return cata;
        }

        /// <summary>La stanza di un edificio con la porta sulla sabbia (quella da cui si entra), o la prima.</summary>
        static Room EntryRoom(Plan p)
        {
            foreach (var o in p.openings)
                if (o.b < 0 && o.sill < 0.01f && !o.oneSided && o.a >= 0 && o.a < p.rooms.Count && !p.rooms[o.a].IsLift && !p.rooms[o.a].noFloor) return p.rooms[o.a];
            return p.rooms[0];
        }

        /// <summary>Solo con -desertonly varieta (alcuni minuti): un luogo di ogni genere e di ogni stato (casa, locanda, chiesa, albergo, teatro, sanatorio, complesso; interi, in rovina, divisi,
        /// che crollano) piu' una torre e una catacomba diverse da quelle di riferimento. Di ognuno si controlla che si carichi e si arredi, che dalla sabbia si entri a piedi nella stanza con
        /// la porta e, con una finestra (-desertshots), che l'immagine abbia contrasto; di torre e catacomba anche la sala del boss. Finora a piedi c'erano solo la cappella, una torre e una
        /// catacomba: un genere con una sala nera o sepolta nella nebbia non lo vedeva nessuno.</summary>
        IEnumerator Variety()
        {
            var picks = new List<Site>();
            var kinds = new HashSet<SiteKind>(); var states = new HashSet<BuildingState>();
            var refTower = DesertWorld.SiteOf(0, -2);
            Site tower = null;
            for (int ring = 1; ring <= 12; ring++)
                for (int cx = -ring; cx <= ring; cx++)
                    for (int cz = -ring; cz <= ring; cz++)
                    {
                        if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) != ring) continue;
                        var s = DesertWorld.SiteOf(cx, cz);
                        if (s == null || s == refTower) continue;
                        if (s.IsTower)
                        {
                            if (tower == null || (refTower != null && tower.theme == refTower.theme && s.theme != refTower.theme)) tower = s;
                        }
                        else if (!s.IsCatacomb && s.kind != SiteKind.Cappella && s.plan != null && picks.Count < 10 && (kinds.Add(s.kind) | states.Add(s.state))) picks.Add(s);
                    }
            var cata = NearestCatacomb(NearestCatacomb(null));
            Debug.Log($"[PryingEyes] desertotest varieta: {picks.Count} edifici, torre {tower}, catacomba {cata}");
            Check("ci sono edifici di piu' generi e stati da visitare", picks.Count >= 5 && kinds.Count >= 4, $"{picks.Count} edifici, {kinds.Count} generi, {states.Count} stati");
            foreach (var s in picks) yield return VisitBuilding(s);
            if (tower != null) yield return VisitStack(tower);
            if (cata != null) yield return VisitStack(cata);
        }

        IEnumerator VisitBuilding(Site s)
        {
            string name = $"{s.kind}/{s.state} ({s.theme}, {s.plan.rooms.Count} stanze)";
            yield return GoTo(s.CentreX, s.fz0 - 12f);
            yield return WaitFor(() => { var r = RtOf(s); return r != null && AllBuilt(r) && r.DressedRooms >= r.DressableRooms && DesertNav.Covers(new Vector3(s.CentreX, s.padY, s.CentreZ)); }, 70f);
            var rt = RtOf(s);
            bool built = rt != null && AllBuilt(rt) && rt.DressedRooms >= rt.DressableRooms;
            Check($"{name}: si costruisce e si arreda", built, rt == null ? "non caricato" : $"{rt.DressedRooms} stanze arredate di {rt.DressableRooms}");
            if (rt == null || !AllBuilt(rt)) yield break;
            Vector3 to = Centre(EntryRoom(s.plan), s.padY + 0.1f);
            bool entered = false;
            yield return Walk(to, 80f, ok => entered = ok);
            Check($"{name}: dalla sabbia si entra a piedi", entered, Pl.transform.position.ToString("F1") + " verso " + to.ToString("F1"));
            if (entered) yield return LevelView(name);
        }

        /// <summary>Una torre o una catacomba: il guscio di tutti i livelli, l'ingresso a piedi dalla sabbia e, col teletrasporto, la sala del boss (il boss compare e la sala si vede).</summary>
        IEnumerator VisitStack(Site s)
        {
            string name = $"{(s.IsTower ? "torre" : "catacomba")} {s.theme}, {s.LevelCount} livelli";
            yield return GoTo(s.CentreX, s.fz0 - 12f);
            yield return WaitFor(() => AllBuilt(RtOf(s)), 90f);
            var rt = RtOf(s);
            Check($"{name}: tutti i livelli hanno il guscio", AllBuilt(rt), rt == null ? "non caricata" : "");
            if (!AllBuilt(rt)) yield break;
            yield return WaitFor(() => rt.DressedRooms > 2 && DesertNav.ReadyAround(new Vector3(s.CentreX, s.padY, s.CentreZ), 20f) && !DesertNav.Busy, 60f);
            Vector3 entry = Centre(PlanGen.HallRoom(s.levels[s.entrance]), s.LevelY(s.entrance) + 0.1f);
            bool inside = false;
            yield return Walk(entry, 90f, ok => inside = ok);
            Check($"{name}: dalla sabbia si entra a piedi", inside, Pl.transform.position.ToString("F1") + " verso " + entry.ToString("F1"));
            if (inside) yield return LevelView(name + ", sala d'ingresso");
            int bossLevel = s.boss;
            Pl.Teleport(Centre(s.levels[bossLevel].rooms[2], s.LevelY(bossLevel) + 0.1f));
            yield return WaitFor(() => rt.boss != null, 100f);
            Check($"{name}: il boss compare nella sala dell'ultimo livello", rt.boss != null, rt.boss == null ? "non e' comparso" : "");
            if (rt.boss != null) yield return BossPhoto("boss_" + s.kind + "_" + s.theme, rt.boss, rt, bossLevel);
        }

        IEnumerator Fall()
        {
            var pl = Pl; var p = pl.transform.position;
            pl.Teleport(new Vector3(p.x, DesertWorld.Height(p.x, p.z) - 60f, p.z));
            yield return new WaitForSeconds(1.6f);
            var q = pl.transform.position;
            Check("chi cade sotto la sabbia torna sopra", q.y > DesertWorld.Height(q.x, q.z) - 6f, q.ToString("F1"));
        }

        IEnumerator Photos()
        {
            var pl = Pl;
            if (DesertSky.I != null) DesertSky.I.SetHour(7.2f, false);
            // l'alba dalla sommita' della scalinata, verso il largo
            pl.Teleport(new Vector3(18f, 0.06f, 1.0f));
            yield return new WaitForSeconds(1f);
            yield return Snap("03_alba_dal_sagrato", 6.4f, new Vector3(18f, 1.2f, -120f));
            // la torre di riferimento da lontano, a mezzogiorno e al tramonto
            var tower = DesertWorld.SiteOf(0, -2);
            if (tower != null)
            {
                yield return GoTo(tower.CentreX - 40f, tower.fz0 - 130f);
                yield return new WaitForSeconds(3f);
                var top = new Vector3(tower.CentreX, tower.padY + tower.Height * 0.6f, tower.CentreZ);
                yield return Snap("04_torre_mezzogiorno", 12.3f, top);
                yield return Snap("05_torre_tramonto", 17.7f, top);
                yield return Snap("05b_torre_crepuscolo", 18.3f, top);
                // dal piede, in su: si vede com'e' alta
                yield return GoTo(tower.CentreX, tower.fz0 - 9f);
                yield return Snap("08_torre_dal_piede", 11f, new Vector3(tower.CentreX, tower.padY + tower.Height, tower.CentreZ), 1.8f);
            }
            // la notte, dal sagrato
            pl.Teleport(new Vector3(18f, 0.06f, 1.0f));
            yield return new WaitForSeconds(1f);
            yield return Snap("09_notte_dal_sagrato", 23.2f, new Vector3(18f, 22f, -120f));
            // una catacomba: il pozzo nella sabbia
            Site cata = null; float bd = float.MaxValue;
            for (int cx = -6; cx <= 6; cx++)
                for (int cz = -6; cz <= 6; cz++)
                {
                    var c = DesertWorld.SiteOf(cx, cz);
                    if (c == null || !c.IsCatacomb) continue;
                    float d = c.CentreX * c.CentreX + c.CentreZ * c.CentreZ;
                    if (d < bd) { bd = d; cata = c; }
                }
            if (cata != null)
            {
                // di tre quarti, da lontano: si vede il fronte, il fianco e quanto e' lunga la sala d'ingresso
                yield return GoTo(cata.CentreX - 16f, cata.fz0 - 13f);
                yield return new WaitForSeconds(4f);
                yield return Snap("10_catacomba_ingresso", 8.5f, new Vector3(cata.CentreX, cata.padY + 2.2f, cata.CentreZ), 1.6f);
            }
        }
    }
}
#endif
