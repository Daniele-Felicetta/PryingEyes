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
    public sealed partial class DesertTest : MonoBehaviour
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
            // il codice del gioco toglie l'invulnerabilita' in piu' punti (a fine di una ricostruzione della cattedrale, dopo la rinascita): chi prova a camminare non deve finire a terra
            if (godMode && Pl != null && Pl.alive && !Pl.Downed && !Pl.invulnerable) Pl.invulnerable = true;
            if (!frames.TryGetValue(phaseName, out var f)) frames[phaseName] = f = new float[4];
            float dt = Time.unscaledDeltaTime;
            // un fotogramma lungo si spiega col contesto: dopo un salto del giocatore (si carica tutto un mondo attorno) e' normale, camminando no
            var p = Pl != null ? Pl.transform.position : Vector3.zero;
            if ((p - lastPos).sqrMagnitude > 400f) jumpedAt = Time.realtimeSinceStartup;
            lastPos = p;
            // il fotogramma in cui la prova stessa ha disegnato a mano (venti o piu' disegni di fila) e il successivo non dicono niente del gioco
            // lo stesso per le foto che scatta la prova automatica da sola (GameManager.HarnessShotFrame: 150-650 ms l'una; il fotogramma lungo a 3 s e a 42 s della camminata era quello)
            if (Time.frameCount - renderFrame <= 1 || Time.frameCount - GameManager.HarnessShotFrame <= 2) return;
            f[0]++; f[1] += dt; if (dt > f[2]) f[2] = dt; if (dt > 0.1f) f[3]++;
            if (dt > 0.15f) Debug.Log($"[PryingEyes] desertotest fotogramma lungo: {dt * 1000f:F0} ms in '{phaseName}', {Time.realtimeSinceStartup - jumpedAt:F1} s dopo l'ultimo salto del giocatore");
        }

        Vector3 lastPos; float jumpedAt;
        int renderFrame = -100;
        bool godMode = true;                // false solo mentre si prova la morte

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
            Vector3 still = pl.transform.position; float stillSince = Time.time;
            while (Time.time < until)
            {
                Vector3 pos = pl.transform.position;
                if (Flat(pos, to) < arrive && Mathf.Abs(pos.y - to.y) < 1.3f) { ok = true; break; }
                if (Flat(pos, still) > 0.3f) { still = pos; stillSince = Time.time; }
                else if (Time.time - stillSince > 2.5f) { ClearEnemiesAround(pos, 2.6f); stillSince = Time.time; }       // fermo da un po': una guardia sveglia gli sbarra la strada
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
            if (!ok) Debug.Log("[PryingEyes] desertotest navmesh: " + NavInfo(pl.transform.position, to) + "; traccia:" + trace + "; " + Blocker(pl.transform, to));
            done(ok);
        }

        /// <summary>Chi sta addosso al giocatore e lo ferma (una guardia svegliata che gli sbarra una porta o la scala): si toglie. Queste prove sono sulla geometria e sulla navmesh, non sul
        /// combattimento, e chi prova non spara; i boss restano (hanno almeno 3.000 di vita, come dice la prova della torre).</summary>
        static void ClearEnemiesAround(Vector3 at, float radius)
        {
            foreach (var g in new List<Enemy>(Enemy.All))
            {
                if (g == null || g.maxHp >= 3000f || (g.transform.position - at).sqrMagnitude > radius * radius) continue;
                Debug.Log($"[PryingEyes] desertotest: tolgo {g.name} che sbarra la strada a {g.transform.position:F1} (il giocatore e' a {at:F1})");
                Enemy.Remove(g);
            }
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
            // con -autotest il giocatore ruota da solo (0,8 rad/s senza un bersaglio) se holdLook e' falso: la mira sbagliava di quanto girava durante l'attesa
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            var sky = DesertSky.I;
            if (sky != null) sky.SetHour(hour, true);
            Face(look);
            yield return new WaitForSeconds(settle);
            Face(look);
            yield return null;
            var cm = Pl.cam.GetComponent<Camera>();
            if (sky != null && cm != null)
            {
                // dov'e' il sole nell'inquadratura e di quanto la mira manca il punto voluto (per capire una foto che non mostra quello che doveva)
                var st = sky.State;
                var vp = cm.WorldToViewportPoint(cm.transform.position + new Vector3(st.sunX, st.sunY, st.sunZ) * 500f);
                Debug.Log($"[PryingEyes] desertotest foto {name}: il giocatore e' a {Pl.transform.position:F1}, il sole e' a ({vp.x:F2}, {vp.y:F2}) {(vp.z > 0f ? "davanti" : "dietro")}, la mira manca di {Vector3.Angle(cm.transform.forward, look - cm.transform.position):F1} gradi, il bersaglio e' a {Vector3.Distance(cm.transform.position, look):F1} m");
            }
            Shot(name);
            yield return null; yield return null;
            if (sky != null) sky.Frozen = false;
        }

        static Vector2 OpenSand(float minRadius = 240f)
        {
            var list = new List<Site>();
            for (int k = 0; k < 90; k++)
            {
                float a = (k * 47f + 20f) * Mathf.Deg2Rad, r = minRadius + 18f * (k % 7) + 10f * (k / 7);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                DesertWorld.SitesNear(x, z, 75f, list);
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
            if (Wants("minimappa")) yield return Phase("minimappa", MiniMap());
            if (Wants("torre")) yield return Phase("torre", Tower());
            if (Wants("boss")) yield return Phase("boss", Boss());
            if (Wants("catacomba")) yield return Phase("catacomba", Catacomb());
            if (Wants("notte")) yield return Phase("notte", Night());
            if (Wants("morte")) yield return Phase("morte", Death());
            if (Wants("lontano")) yield return Phase("lontano", Far());
            if (Explicit("soak")) yield return Phase("soak", Soak());
            if (Explicit("varieta")) yield return Phase("varieta' dei luoghi", Variety());
            if (Explicit("prestazioni")) yield return Phase("prestazioni", Perf());
            if (Explicit("costi")) yield return Phase("costi", Costs());
            if (Explicit("esterni")) yield return Phase("esterni", Exteriors());
            if (Explicit("rovine")) yield return Phase("rovine", Ruins());
            if (Explicit("muri")) yield return Phase("muri da vicino", CloseWalls());
            if (Explicit("portale")) yield return Phase("portale della cattedrale", CathedralDoor());
            if (Explicit("orizzonte")) yield return Phase("orizzonte", Horizon());
            if (Explicit("paesaggio")) yield return Phase("paesaggio", Landscape());
            if (Explicit("nuvole")) yield return Phase("nuvole", CloudsPhase());
            if (Explicit("vita")) yield return Phase("vita del deserto", Life());
            if (Explicit("cose")) yield return Phase("cose sulla sabbia", Props());
            if (Explicit("galleria")) yield return Phase("galleria delle cose", PropGallery());
            if (Explicit("rilievi")) yield return Phase("rilievi", ButteShots());
            if (Explicit("look")) yield return Phase("tarature del look", LookSweep());
            if (Explicit("orme")) yield return Phase("orme sulla sabbia", PrintsTest());
            if (Explicit("suolo")) yield return Phase("suolo da vicino", GroundShots());
            if (Explicit("creste")) yield return Phase("sabbia dalle creste", SpindriftTest());
            if (Explicit("salina")) yield return Phase("pozze sulle saline", BrineTest());
            if (Explicit("cammino")) yield return Phase("cammino", WalkProbe());
            if (Explicit("percorso")) yield return Phase("percorso", RouteProbe());
            if (Explicit("moto")) yield return Phase("moto", Moto());
            if (Explicit("motosuono")) yield return Phase("motosuono", MotoSound());
            if (Explicit("motourto")) yield return Phase("motourto", MotoCrashes());
            if (Explicit("motocattedrale")) yield return Phase("motocattedrale", MotoCathedral());
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
                if (!NavMesh.SamplePosition(new Vector3(q.x, DesertWorld.Height(q.x, q.y), q.y), out _, 1.5f, NavMesh.AllAreas)) lost += $" ({q.x:F0}, {q.y:F0})";       // la sabbia attorno non e' piu' piatta: si cerca alla sua quota
            Check("la navmesh tiene tutti i tasselli costruiti attorno", lost.Length == 0, lost.Length == 0 ? "" : "senza navmesh:" + lost + "; " + DesertNav.BuiltTiles + " tasselli");
            Check("la cattedrale e' un santuario: niente navmesh sulla scalinata ne' dentro", !onStairs && !inChurch, $"scalinata {onStairs}, chiesa {inChurch}");
            // le cose sulla sabbia: in superficie si disegnano (il taglio di quando si e' sottoterra non deve scattare), e le cose pesanti lontane hanno la versione ridotta
            var scat = DesertScatter.I;
            if (scat != null) yield return WaitFor(() => scat.CellsReady >= 20 && scat.FullDetail + scat.ReducedDetail > 20, 40f);
            Check("sulla sabbia le cose sono mandate alla scheda: il taglio da sottoterra non scatta in superficie", scat != null && !scat.Underground, scat == null ? "manca il regista delle cose" : $"giocatore a {pl.transform.position.y:F1}, la sabbia a {DesertWorld.Height(pl.transform.position.x, pl.transform.position.z):F1}");
            Check("le cose pesanti (sassi, massi, cactus, cespugli) lontane hanno la versione ridotta (sono molte di piu' di quelle entro 70 m)", scat != null && scat.ReducedDetail > 0 && scat.ReducedDetail > scat.FullDetail, scat == null ? "manca" : $"{scat.FullDetail} con tutti i triangoli, {scat.ReducedDetail} ridotte");
            // i due bracieri in cima alla scalinata hanno il fuoco (fiamma, nucleo, fumo e brace: DesertCathedral.BrazierFire), non solo un disco acceso sopra una coppa nera
            int braziers = 0, burning = 0, tripods = 0;
            foreach (var bf in FindObjectsByType<DesertCathedral.BrazierFire>(FindObjectsSortMode.None))
            {
                braziers++;
                foreach (var ps in bf.GetComponentsInChildren<ParticleSystem>()) if (ps.isPlaying && ps.emission.rateOverTime.constant > 0f) burning++;
                // il cesto sta su un treppiede: la mesh e' larga piu' di 1,2 m (il calice di prima, col piede svasato, era 0,94) e alta tra 1 e 1,1 m
                foreach (var mf in bf.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && mf.sharedMesh.name == "Braciere" && mf.sharedMesh.bounds.size.x > 1.2f && mf.sharedMesh.bounds.size.y > 1.0f && mf.sharedMesh.bounds.size.y < 1.1f) tripods++;
            }
            Check("i due bracieri in cima alla scalinata hanno il fuoco: fiamma, nucleo, fumo e brace", braziers == 2 && burning == 2 * DesertCathedral.BrazierFire.Systems, $"{braziers} bracieri, {burning} sistemi di particelle accesi su {2 * DesertCathedral.BrazierFire.Systems}");
            Check("i bracieri sono cesti su un treppiede (larghi piu' di 1,2 m al piede, alti 1 m), non calici su uno stelo", braziers == 2 && tripods == 2, $"{tripods} treppiedi su {braziers} bracieri");
            // i due stendardi con l'occhio ai lati dello scalone: il telo sta fermo alla traversa e il vento ne muove l'orlo (DesertBanner)
            yield return new WaitForSeconds(1.0f);
            int hubBanners = 0; float topMoved = 0f, hemMoved = 0f;
            foreach (var bn in FindObjectsByType<DesertBanner>(FindObjectsSortMode.None))
            {
                if (bn.transform.position.sqrMagnitude > 60f * 60f) continue;               // quelli sul tetto delle torri (si vedono da lontano) non contano qui
                hubBanners++; topMoved = Mathf.Max(topMoved, bn.TopOffset); hemMoved = Mathf.Max(hemMoved, bn.HemOffset);
            }
            Check("i due stendardi con l'occhio stanno ai lati dello scalone: il telo e' fermo alla traversa e il vento ne muove l'orlo", hubBanners == 2 && topMoved < 0.001f && hemMoved > 0.05f,
                $"{hubBanners} stendardi, la riga alta si e' mossa di {topMoved * 100f:F1} cm, l'orlo di {hemMoved * 100f:F0} cm");
            // risalire
            yield return Snap("02_sabbia_cattedrale", 7.2f, new Vector3(cx, 3.2f, 0f));
            yield return GoTo(cx, -22f);
            yield return Snap("02b_cattedrale_dalla_sabbia", 7.2f, new Vector3(cx, 2.4f, 0f));
            if (shots)
            {
                // il fuoco di un braciere da quattro metri, di giorno e di sera, dal sagrato; poi di nuovo sulla sabbia (le prove dopo questa si aspettano di essere li')
                pl.Teleport(new Vector3(HubPlan.StairX0 + 3.0f, 0.06f, 3.4f));
                yield return new WaitForSeconds(1.0f);
                var fire = new Vector3(HubPlan.StairX0 + 0.6f, 1.3f, 0.6f);
                yield return Snap("02c_braciere_giorno", 12.0f, fire, 1.6f);
                yield return Snap("02d_braciere_sera", 18.4f, fire, 1.6f);
                // un banco del mercato da vicino (di giorno) e un lampione acceso di notte, sempre dal sagrato
                pl.Teleport(new Vector3(9.0f, 0.06f, 5.0f));
                yield return new WaitForSeconds(0.8f);
                yield return Snap("02e_banco", 10.0f, new Vector3(3.4f, 1.9f, 4.9f), 1.2f);
                pl.Teleport(new Vector3(14.5f, 0.06f, 9.5f));
                yield return new WaitForSeconds(0.8f);
                yield return Snap("02f_lampione_notte", 22.5f, new Vector3(11.2f, 2.9f, 5.2f), 1.2f);
                yield return GoTo(cx, -22f);
            }
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
            Check("a mezzogiorno il sole e' acceso", sun > 0.6f, $"intensita' {sun:F2}");
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
            Vector3 target = it.Point;
            // i posti dove si sta in piedi (una capsula libera) per guardarla: davanti, dietro e di lato (i leggii sono a un metro e tre quarti l'uno dall'altro), a 1,3 m e poi piu' vicino e piu' lontano
            var spots = new List<Vector3>();
            if (it is Component comp)
            {
                var f = comp.transform.forward; f.y = 0f;
                if (f.sqrMagnitude < 0.1f) f = Vector3.back;
                f.Normalize();
                foreach (float dist in new[] { 1.3f, 1.0f, 1.7f })
                    foreach (var d in new[] { f, -f, Vector3.Cross(f, Vector3.up), -Vector3.Cross(f, Vector3.up) })
                    {
                        var q = target + d * dist; q.y = 0.06f;
                        if (Physics.CheckCapsule(q + Vector3.up * 0.4f, q + Vector3.up * 1.5f, 0.3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                        spots.Add(q);
                    }
            }
            if (spots.Count == 0) spots.Add(new Vector3(target.x, 0.06f, target.z - 1.3f));
            // il primo posto da cui F sceglie proprio questa cosa: un leggio piu' vicino all'occhio la puo' rubare (e' successo una volta, al cantiere delle volte, in una serie di giri tutti uguali), come a un giocatore, che si sposterebbe
            bool selected = false;
            foreach (var from in spots)
            {
                pl.Teleport(from);
                yield return null;
                Face(target);
                yield return new WaitForSeconds(0.15f);
                selected = Interact.Tick(pl.cam) && Interact.Current == it;
                if (selected) break;
            }
            Check(what + ": si guarda e si usa con F", selected, selected ? "" : $"F userebbe {Interact.Current?.GetType().Name ?? "niente"} da tutti i {spots.Count} posti provati");
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
            var s = DesertWorld.Welcome();
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
            // con -autotest il giocatore ruota da solo se holdLook e' falso: chi guarda in una direzione deve tenerla (da solo, senza le fasi che la fermano prima, la prova girava con lui)
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
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
            // la freccia di casa (0.9.1): in alto, punta alla cattedrale rispetto a dove si guarda e dice quanto manca
            var hud = gm.hud;
            Check("la freccia di casa e' accesa, punta davanti (guardando la cattedrale) e dice la distanza", hud != null && hud.GuideArrowShown && Mathf.Abs(Mathf.DeltaAngle(hud.GuideArrowAngle, 0f)) < 10f && hud.GuideArrowText.StartsWith("cattedrale  ·  ") && hud.GuideArrowText.EndsWith("m"),
                hud == null ? "nessun HUD" : $"accesa {hud.GuideArrowShown}, {hud.GuideArrowAngle:F0} gradi, '{hud.GuideArrowText}'");
            Check("le distanze a parole: 140 m, 800 m e 1,4 km", DesertHome.Distance(140f) == "140 m" && DesertHome.Distance(820f) == "800 m" && DesertHome.Distance(1440f) == "1,4 km", DesertHome.Distance(140f) + ", " + DesertHome.Distance(820f) + ", " + DesertHome.Distance(1440f));
            var p = pl.transform.position;
            Face(new Vector3(2f * p.x - cx, pl.cam.position.y, 2f * p.z - cz));
            yield return new WaitForSeconds(1.8f);
            string behind = Dd != null ? Dd.WhereText() : "";
            Check("voltandole le spalle la cattedrale e' dietro", behind.Contains("cattedrale a ") && behind.EndsWith(", " + DesertDirector.Where8[4]), behind);
            Check("voltandole le spalle la freccia di casa punta dietro", hud != null && hud.GuideArrowShown && Mathf.Abs(Mathf.DeltaAngle(hud.GuideArrowAngle, 180f)) < 10f, hud == null ? "nessun HUD" : $"accesa {hud.GuideArrowShown}, {hud.GuideArrowAngle:F0} gradi");
            string obj = Fd.DesertObjective();
            Check("l'obiettivo e' su due righe: prima l'ora e la strada, poi il consiglio", obj.Contains("\n") && obj.Substring(0, obj.IndexOf('\n')).Contains("cattedrale a ") && obj.Substring(obj.IndexOf('\n')).Contains("Torri e catacombe"), obj.Replace("\n", " / "));
            Check("la prima riga dell'obiettivo sta nella larghezza della scritta", obj.IndexOf('\n') <= 110, obj.IndexOf('\n') + " caratteri");
            // in chiesa l'eco e' quello della chiesa, e non si cammina sulla sabbia
            var nave = Fd.cur.plan.rooms[HubPlan.Navata];
            pl.Teleport(Centre(nave, 0.06f));
            yield return new WaitForSeconds(5.5f);
            Check("nella navata l'eco e' quello della chiesa", amb != null && amb.ReverbDecay > 2.0f, amb == null ? "nessun ambiente" : $"decadimento {amb.ReverbDecay:F2} s");
            Check("nella navata i passi non sono quelli della sabbia", pl.FootSurface != 3, "superficie " + pl.FootSurface);
            Check("in cattedrale la freccia di casa e' spenta", hud != null && !hud.GuideArrowShown, hud == null ? "nessun HUD" : $"accesa {hud.GuideArrowShown}");
        }

        /// <summary>La minimappa (0.9.1): accesa dall'arrivo, la carta si dipinge tutta ed e' centrata su chi gioca, mostra la cattedrale e le sue scale d'oro, segna i nemici svegli vicini e
        /// non quelli che dormono, mette la freccia di casa sul bordo quando la cattedrale e' fuori dalla finestra, cambia quando ci si sposta di un chilometro, N la spegne e la notte la fa blu.</summary>
        IEnumerator MiniMap()
        {
            var pl = Pl; var hud = Gm.hud;
            Check("c'e' l'HUD", hud != null, hud == null ? "nessun HUD" : "");
            if (hud == null) yield break;
            var clock = DesertMode.Clock; float hour0 = clock.hour, speed0 = clock.speed;
            Fd.DebugShowMap(false, true);
            pl.Teleport(new Vector3(HubPlan.SpawnX, 0.12f, HubPlan.SpawnZ));
            yield return null;
            yield return WaitFor(() => pl.GetComponent<CharacterController>().isGrounded, 8f);
            yield return WaitFor(() => hud.DesertMapShown && hud.DesertMapWanted > 0 && hud.DesertMapReady >= hud.DesertMapWanted, 40f);
            Check("la minimappa e' accesa dall'arrivo", hud.DesertMapShown, "");
            Check("la carta si dipinge tutta (da 81 a 100 quadrati da 64 m attorno a chi gioca)", hud.DesertMapWanted >= 81 && hud.DesertMapReady == hud.DesertMapWanted, $"{hud.DesertMapReady} pronti su {hud.DesertMapWanted}, {hud.DesertMapPaper?.Painted} dipinti, {hud.DesertMapPaper?.Failed} falliti");
            Check("nessun quadrato ha dato errore", hud.DesertMapPaper != null && hud.DesertMapPaper.Failed == 0, hud.DesertMapPaper == null ? "nessuna carta" : hud.DesertMapPaper.Failed + " falliti");
            var uv = hud.DesertMapUv; float tw = DesertMapGen.Slots * DesertMapGen.TileSize;
            float ucx = (uv.x + uv.width * 0.5f) * tw, ucz = (uv.y + uv.height * 0.5f) * tw;
            Vector3 pp = pl.transform.position;
            Check("la finestra e' centrata su chi gioca e mostra 512 m", Mathf.Abs(Mathf.Repeat(ucx - pp.x + tw * 0.5f, tw) - tw * 0.5f) < 1f && Mathf.Abs(Mathf.Repeat(ucz - pp.z + tw * 0.5f, tw) - tw * 0.5f) < 1f && Mathf.Abs(uv.width * tw - DesertMapGen.WindowSize) < 0.5f,
                $"centro ({ucx:F1}, {ucz:F1}) mod {tw:F0}, giocatore ({pp.x:F1}, {pp.z:F1}), larghezza {uv.width * tw:F1} m");
            bool stone = hud.DesertMapPaper.TryGetPixel(18f, 30f, out var cs) && Mathf.Abs(cs.r - 214) <= 6 && Mathf.Abs(cs.g - 207) <= 6 && Mathf.Abs(cs.b - 189) <= 6;
            Check("sulla carta la navata della cattedrale e' pietra chiara", stone, $"colore {cs}");
            bool gold = hud.DesertMapPaper.TryGetPixel(18f, -3f, out var cg) && cg.r > 190 && cg.r > cg.g + 20 && cg.g > cg.b + 35;
            Check("sulla carta la scalinata del sagrato e' d'oro", gold, $"colore {cg}");
            Check("con la cattedrale nella finestra non c'e' la freccia di casa sul bordo", !hud.DesertMapHomeShown, "");
            // la tua moto, parcheggiata ai piedi della scalinata a una dozzina di metri: un tondo turchese sulla carta, dove sta davvero
            var ownBike = DesertBikes.Own(pl);
            var bikeWant = ownBike != null ? BikeDotWant(pl, ownBike) : Vector2.zero;
            Check("la minimappa segna la tua moto parcheggiata, dove sta (a una dozzina di metri: alcuni pixel dal centro)", ownBike != null && hud.DesertMapBikeShown && (hud.DesertMapBikeAt - bikeWant).magnitude < 0.6f && bikeWant.magnitude > 2f,
                $"visibile {hud.DesertMapBikeShown}, a {hud.DesertMapBikeAt}, attesa a {bikeWant}");

            // i nemici: svegli e vicini si segnano, chi dorme no
            // (chi dorme sta fuori dai 30 m, 48 di notte, in cui il regista lo sveglierebbe: a 51 m e' ancora dentro la minimappa, che per questo deve escluderlo da sola)
            var snap1 = NavMesh.SamplePosition(pp + new Vector3(24f, 0f, -14f), out var s1, 12f, NavMesh.AllAreas) ? s1.position : pp + new Vector3(24f, 0f, -14f);
            var snap2 = NavMesh.SamplePosition(pp + new Vector3(-38f, 0f, -34f), out var s2, 12f, NavMesh.AllAreas) ? s2.position : pp + new Vector3(-38f, 0f, -34f);
            var awake = Enemy.Spawn(Enemy.EnemyType.Monaco, snap1); var asleep = Enemy.Spawn(Enemy.EnemyType.Monaco, snap2);
            if (asleep != null) asleep.Doze();
            yield return null; yield return null; yield return new WaitForSeconds(0.2f);
            int expect = 0;
            foreach (var e in Enemy.All) { if (e == null || e.IsDead || e.dormant) continue; var d = e.transform.position - pl.transform.position; d.y = 0f; if (d.sqrMagnitude <= 90f * 90f) expect++; }
            Check("la minimappa segna i nemici svegli entro 90 m e non quelli che dormono", awake != null && asleep != null && expect >= 1 && hud.DesertMapEnemyDots == expect, $"segnati {hud.DesertMapEnemyDots}, svegli vicini {expect}");
            // con -desertshots: il colpo d'occhio dal sagrato, con la carta, il puntino del nemico sveglio e il resto dell'HUD (nessun controllo: si guarda)
            yield return Snap("20_minimappa_scalinata", 10.5f, new Vector3(HubPlan.SpawnX, 1.6f, HubPlan.SpawnZ - 60f));
            if (awake != null) Enemy.Remove(awake);
            if (asleep != null) Enemy.Remove(asleep);

            // la notte e il giorno
            clock.speed = 0f; clock.hour = 12f;
            yield return new WaitForSeconds(0.6f);
            var dayTint = hud.DesertMapTint;
            clock.hour = 1f;
            yield return new WaitForSeconds(0.6f);
            var nightTint = hud.DesertMapTint;
            clock.hour = hour0; clock.speed = speed0;
            Check("a mezzogiorno la carta e' chiara, di notte blu e scura", dayTint.r > 0.92f && nightTint.r < 0.50f && nightTint.b > nightTint.r, $"giorno {dayTint}, notte {nightTint}");

            // N la spegne e la riaccende
            Fd.DebugShowMap(false, false);
            yield return null; yield return null;
            bool offWhenOff = !hud.DesertMapShown;
            Fd.DebugShowMap(false, true);
            yield return null; yield return null;
            Check("N spegne la minimappa e la riaccende", offWhenOff && hud.DesertMapShown, $"spenta {offWhenOff}, riaccesa {hud.DesertMapShown}");

            // seicento metri piu' in la (su sabbia libera): la carta si rifa' tutta, e la cattedrale resta indicata sul bordo, dalla parte giusta
            var spot = OpenSand(600f);
            yield return GoTo(spot.x, spot.y);
            yield return WaitFor(() => hud.DesertMapWanted > 0 && hud.DesertMapReady >= hud.DesertMapWanted, 40f);
            Vector3 far = pl.transform.position;
            Check("lontano la carta si e' rifatta tutta attorno a chi gioca", hud.DesertMapWanted >= 81 && hud.DesertMapReady == hud.DesertMapWanted && hud.DesertMapPaper.IsReady(DesertMapGen.TileOf(far.x), DesertMapGen.TileOf(far.z)), $"{hud.DesertMapReady} pronti su {hud.DesertMapWanted} a ({far.x:F0}, {far.z:F0}), {hud.DesertMapPaper.Failed} falliti");
            DesertWorld.CathedralCentre(out float hx, out float hz);
            var toHome = new Vector2(hx - far.x, hz - far.z);
            var homeAt = hud.DesertMapHomeAt;
            // sul bordo della carta (a 85 px dal centro sull'asse piu' lungo), con la stessa direzione di casa (il nord e' in alto: x a est, y a nord)
            float edge = Mathf.Max(Mathf.Abs(homeAt.x), Mathf.Abs(homeAt.y));
            Check("lontano la freccia d'oro sta sul bordo della carta e guarda verso la cattedrale", hud.DesertMapHomeShown && edge > 78f && edge < 92f && Vector2.Angle(homeAt, toHome) < 4f,
                $"visibile {hud.DesertMapHomeShown}, a {homeAt} (bordo {edge:F0}), casa a {toHome} (angolo {Vector2.Angle(homeAt, toHome):F1})");
            // la moto e' ancora a casa, vicino alla cattedrale: la freccia d'oro dice gia' da che parte sta, un secondo segno ci finirebbe sotto
            Check("lontano, con la moto ancora a casa, il tondo non si sovrappone alla freccia d'oro", ownBike != null && !hud.DesertMapBikeShown, $"visibile {hud.DesertMapBikeShown}");
            if (ownBike != null)
            {
                var side = new Vector2(-toHome.y, toHome.x).normalized;
                // lasciata a 120 m di fianco (nella finestra, che arriva a 256 m): il tondo sta dov'e', a 45 px dal centro; con -desertshots lo si guarda
                var bikeNear = new Vector3(far.x + side.x * 120f, 0f, far.z + side.y * 120f); bikeNear.y = DesertWorld.Height(bikeNear.x, bikeNear.z);
                ownBike.Park(bikeNear, ownBike.Heading);
                yield return WaitFor(() => hud.DesertMapBikeShown, 3f);
                yield return null; yield return null;
                var wantNear = BikeDotWant(pl, ownBike);
                Check("lasciata a 120 m di fianco, nella finestra, il tondo della moto sta dov'e' (a 45 px dal centro)", hud.DesertMapBikeShown && (hud.DesertMapBikeAt - wantNear).magnitude < 0.6f && wantNear.magnitude > 40f,
                    $"visibile {hud.DesertMapBikeShown}, a {hud.DesertMapBikeAt}, attesa a {wantNear}");
                if (shots)
                {
                    var homeDirB = new Vector3(hx - far.x, 0f, hz - far.z).normalized;
                    yield return Snap("23_minimappa_moto", 14f, far + Quaternion.Euler(0f, 55f, 0f) * homeDirB * 90f + Vector3.up * 1.5f);
                    clock.hour = hour0; clock.speed = speed0;
                }
                // lasciata a 330 m di fianco (fuori dalla finestra e lontano dalla freccia): il tondo sta sul bordo, dalla sua parte
                var bikeFar = new Vector3(far.x + side.x * 330f, 0f, far.z + side.y * 330f); bikeFar.y = DesertWorld.Height(bikeFar.x, bikeFar.z);
                ownBike.Park(bikeFar, ownBike.Heading);
                yield return WaitFor(() => hud.DesertMapBikeShown, 3f);
                yield return null; yield return null;
                var bikeAt = hud.DesertMapBikeAt;
                float bikeEdge = Mathf.Max(Mathf.Abs(bikeAt.x), Mathf.Abs(bikeAt.y));
                Check("lasciata lontano, fuori dalla finestra, il tondo della moto sta sul bordo e guarda dalla sua parte", hud.DesertMapBikeShown && bikeEdge > 84f && bikeEdge < 92f && Vector2.Angle(bikeAt, side) < 4f,
                    $"visibile {hud.DesertMapBikeShown}, a {bikeAt} (bordo {bikeEdge:F0}), moto a {side * 330f} (angolo {Vector2.Angle(bikeAt, side):F1})");
                ownBike.Park(ownBike.home, ownBike.homeHeading);
            }
            bool far2 = hud.DesertMapPaper.TryGetPixel(far.x, far.z, out var cf);
            Check("lontano la carta e' carta di sabbia e non la scura dei posti non esplorati", far2 && (cf.r > 120 || cf.g > 100), $"colore {cf}");
            if (shots)
            {
                // lontano da casa si guarda un po' di lato: la freccia d'oro in alto non e' al centro e quella sul bordo della carta e' ben distinta (di notte e di giorno)
                var homeDir = new Vector3(hx - far.x, 0f, hz - far.z).normalized;
                var lookAt = far + Quaternion.Euler(0f, 55f, 0f) * homeDir * 90f + Vector3.up * 1.5f;
                yield return Snap("21_minimappa_lontano_notte", 22.5f, lookAt);
                yield return Snap("22_minimappa_lontano_giorno", 14f, lookAt);
                clock.hour = hour0; clock.speed = speed0;
            }

            // il costo di un fotogramma di minimappa quando tutto e' pronto
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 300; i++) hud.DesertMapPaper.Update(far.x, far.z);
            double us = sw.Elapsed.TotalMilliseconds / 300.0 * 1000.0;
            Check($"un fotogramma di minimappa a carta pronta costa {us:F0} microsecondi", us < 500.0, "");
            pl.Teleport(new Vector3(HubPlan.SpawnX, 0.12f, HubPlan.SpawnZ));
            yield return null;
        }

        IEnumerator Tower()
        {
            var s = DesertWorld.Landmark();
            Check("la torre di riferimento ha molti piani", s != null && s.IsTower && s.LevelCount >= 7, s?.ToString() ?? "manca");
            if (s == null || !s.IsTower) yield break;
            yield return GoTo(s.CentreX, s.fz0 - 12f);
            yield return WaitFor(() => AllBuilt(RtOf(s)), 90f);
            var rt = RtOf(s);
            Check("tutti i piani della torre hanno il guscio", AllBuilt(rt), rt == null ? "non caricata" : "");
            if (!AllBuilt(rt)) yield break;
            // una torre intera ha sul tetto un albero con lo stendardo con l'occhio e, sotto la cornice, uno stendardo appeso a ogni muro lungo; il vento li muove, e quelli di muro non li spinge mai
            // dentro il muro (sono della pelle di chi sta lontano: ci sono prima del guscio); una torre in rovina non ne ha
            int mast = 0, walls = 0; float mastHem = 0f, wallHem = float.MaxValue, wallGap = float.MaxValue;
            foreach (var bn in FindObjectsByType<DesertBanner>(FindObjectsSortMode.None))
            {
                var bp = bn.transform.position;
                if (Mathf.Abs(bp.x - s.CentreX) < 40f && Mathf.Abs(bp.z - s.CentreZ) < 40f && bp.y > s.padY + s.Height * 0.7f)
                {
                    if (bn.OnWall) { walls++; wallHem = Mathf.Min(wallHem, bn.HemOffset); wallGap = Mathf.Min(wallGap, bn.MinGap); }
                    else { mast++; mastHem = Mathf.Max(mastHem, bn.HemOffset); }
                }
            }
            bool bannersOk = s.state == BuildingState.Intact ? mast == 1 && mastHem > 0.05f && walls >= 3 && walls <= 4 && wallHem > 0.03f && wallGap >= 0.25f : mast == 0 && walls == 0;
            Check("una torre intera ha sul tetto l'albero con lo stendardo con l'occhio e uno stendardo appeso a ogni muro lungo, che il vento muove senza spingerli dentro il muro (una in rovina non ne ha)", bannersOk,
                $"{s.state}: {mast} sul tetto, {walls} di muro" + (mast > 0 ? $", l'orlo del tetto si e' mosso di {mastHem * 100f:F0} cm" : "") + (walls > 0 ? $", il piu' fermo di muro di {wallHem * 100f:F0} cm, il telo non e' mai stato a meno di {wallGap * 100f:F0} cm dal muro" : ""));
            // le torrette d'angolo: la facciata dell'ultimo piano sale oltre i merli (1,7 m sopra il tetto) fino ai tetti a piramide (6 m e piu')
            float roofTop = s.padY + s.Height, turretTop = 0f; int facades = 0;
            foreach (var fr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (fr.name != "Facciata") continue;
                var bc = fr.bounds.center;
                if (Mathf.Abs(bc.x - s.CentreX) > 40f || Mathf.Abs(bc.z - s.CentreZ) > 40f) continue;
                facades++; turretTop = Mathf.Max(turretTop, fr.bounds.max.y);
            }
            Check("la torre ha le torrette ai suoi angoli: la facciata sale oltre i merli fino ai tetti a piramide", s.state == BuildingState.Intact ? facades > 0 && turretTop >= roofTop + 5f : true,
                $"{facades} facciate, la piu' alta sta {turretTop - roofTop:F1} m sopra il tetto");
            if (shots)
            {
                // la torre da 90 m dal lato dell'ingresso (l'albero sul tetto e il profilo), dal lato della cattedrale e da est (gli stendardi di muro), poi di nuovo al piede
                float gz = s.fz0 - 90f;
                Pl.Teleport(new Vector3(s.CentreX, DesertWorld.Height(s.CentreX, gz) + 0.06f, gz));
                yield return new WaitForSeconds(0.6f);
                yield return Snap("13_torre_stendardo", 12f, new Vector3(s.CentreX, s.padY + s.Height - 1f, s.CentreZ), 1.2f);
                float nz = s.fz1 + 75f;
                Pl.Teleport(new Vector3(s.CentreX, DesertWorld.Height(s.CentreX, nz) + 0.06f, nz));
                yield return new WaitForSeconds(0.6f);
                yield return Snap("13b_torre_stendardi_nord", 12f, new Vector3(s.CentreX, s.padY + s.Height - 9f, s.fz1), 1.2f);
                float ex = s.fx1 + 60f;
                Pl.Teleport(new Vector3(ex, DesertWorld.Height(ex, s.CentreZ) + 0.06f, s.CentreZ));
                yield return new WaitForSeconds(0.6f);
                yield return Snap("13c_torre_stendardi_est", 12f, new Vector3(s.fx1, s.padY + s.Height - 9f, s.CentreZ), 1.2f);
                float tz = s.fz0 - 40f, tx = s.fx0 - 18f;
                Pl.Teleport(new Vector3(tx, DesertWorld.Height(tx, tz) + 0.06f, tz));
                yield return new WaitForSeconds(0.6f);
                yield return Snap("13d_torre_torretta", 12f, new Vector3(s.fx0 + 6f, s.padY + s.Height + 2f, s.fz0 + 6f), 1.2f);
                Pl.Teleport(new Vector3(s.CentreX, DesertWorld.Height(s.CentreX, s.fz0 - 12f) + 0.06f, s.fz0 - 12f));
                yield return new WaitForSeconds(0.4f);
            }
            yield return WaitFor(() => rt.DressedRooms > 3 && DesertNav.ReadyAround(new Vector3(s.CentreX, s.padY, s.CentreZ), 20f) && !DesertNav.Busy, 60f);
            Check("la torre e' alta quanto i suoi piani", s.Height >= 7 * PlanGen.LevelHeight, $"{s.Height:F0} m");
            Vector3 hall0 = Centre(PlanGen.HallRoom(s.levels[0]), s.LevelY(0) + 0.1f);
            Vector3 to = Centre(PlanGen.HallRoom(s.levels[1]), s.LevelY(1) + 0.1f);
            bool inside = false;
            yield return Walk(hall0, 90f, ok => inside = ok);
            Check("dalla sabbia si entra nella torre a piedi", inside, Pl.transform.position.ToString("F1") + " verso " + hall0.ToString("F1"));
            if (!inside) yield break;
            // le guardie scendono sulla scala a incontrarlo (una, a mezza rampa, ha fermato la prova): chi prova non spara, quindi non deve trovarle in mezzo, come nella catacomba
            foreach (var g in new List<Enemy>(Enemy.All)) if (g != null) Enemy.Remove(g);
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
                Vector3 still = pl.transform.position; float stillSince = Time.time;
                while (Time.time < until)
                {
                    Vector3 pos = pl.transform.position;
                    if (Flat(pos, p) < 0.45f && Mathf.Abs(pos.y - p.y) < 0.9f) { ok = true; break; }
                    if (Flat(pos, still) > 0.2f) { still = pos; stillSince = Time.time; }
                    else if (Time.time - stillSince > 1.5f) { ClearEnemiesAround(pos, 2.0f); stillSince = Time.time; }
                    Vector3 d = p - pos; d.y = 0f;
                    PlayerController.botDir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero;
                    yield return null;
                }
                if (!ok)
                {
                    all = false;
                    Debug.Log($"[PryingEyes] desertotest scala: fermo a {pl.transform.position:F2} verso il punto {k} {p:F2}");
                    Debug.Log("[PryingEyes] desertotest scala: " + Blocker(pl.transform, p));
                    break;
                }
            }
            PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
            done(all);
        }

        /// <summary>Perche' il giocatore non avanza: il primo collider sulla strada verso il punto (una sfera larga come il giocatore) e tutti quelli che gli stanno attorno.</summary>
        static string Blocker(Transform pl, Vector3 target)
        {
            var sb = new System.Text.StringBuilder("bloccato da:");
            Vector3 c = pl.position + Vector3.up * 0.9f, d = target - pl.position; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f && Physics.SphereCast(c, 0.25f, d.normalized, out var h, 2f, ~0, QueryTriggerInteraction.Ignore)) sb.Append($" primo sulla strada {Describe(h.collider)} a {h.distance:F2} m;");
            else sb.Append(" nessun collider sulla strada;");
            foreach (var col in Physics.OverlapSphere(c, 1.6f, ~0, QueryTriggerInteraction.Ignore)) sb.Append(" | " + Describe(col));
            return sb.ToString();
        }

        static string Describe(Collider c)
        {
            var t = c.transform; string path = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
            return $"{path} [{c.GetType().Name}, strato {LayerMask.LayerToName(c.gameObject.layer)}, centro {c.bounds.center:F2}, misure {c.bounds.size:F2}]";
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
            var s = DesertWorld.Landmark();
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
                // le guardie si svegliano a 30 m e si stringono sulla scala: chi prova non spara, quindi non deve trovarle in mezzo (non e' la prova dei nemici); il boss resta
                foreach (var g in new List<Enemy>(Enemy.All)) if (g != null && g != rt.boss) Enemy.Remove(g);
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
            // da qui le cose che stanno sopra la sabbia non si vedono: non si mandano alla scheda (pesavano 2,5 ms nella sala del boss)
            var scatUnder = DesertScatter.I;
            yield return null;
            Check("sotto la sabbia le cose che ci stanno sopra non si mandano alla scheda", scatUnder != null && scatUnder.Underground, scatUnder == null ? "manca il regista delle cose" : $"giocatore a {Pl.transform.position.y:F1}, la sabbia a {DesertWorld.Height(Pl.transform.position.x, Pl.transform.position.z):F1}");
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
            godMode = false;
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
            godMode = true;
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
        string renderSamples = "";

        /// <summary>Il costo, in ms, di un fotogramma della camera a 1920x1080 (CPU e GPU, attesa compresa): mediana di nove disegni, dopo 'warm' disegni di riscaldamento. Dopo qualche secondo
        /// di pausa la scheda scende di frequenza e il primo disegno costa due o tre volte il normale (la prova del 18 novembre misurava 11-27 ms il primo disegno in ogni posto e 3-10 tutti i
        /// seguenti, e prendeva il freddo per un guaio del gioco): con 24 disegni di riscaldamento si misura il passo a regime, che e' quello di chi gioca. I nove campioni restano in
        /// renderSamples.</summary>
        float RenderMs(int warm = 24)
        {
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return -1f;
            renderFrame = Time.frameCount;
            var rt = RenderTexture.GetTemporary(1920, 1080, 24);
            var prev = cam.targetTexture; var old = RenderTexture.active;
            cam.targetTexture = rt;
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var ms = new float[9];
            for (int i = -warm; i < ms.Length; i++)
            {
                float t0 = Time.realtimeSinceStartup;
                cam.Render();
                RenderTexture.active = rt;
                one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);                // aspetta che la GPU abbia finito
                if (i >= 0) ms[i] = (Time.realtimeSinceStartup - t0) * 1000f;
            }
            RenderTexture.active = old;
            cam.targetTexture = prev;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(one);
            renderFrame = Time.frameCount;
            renderSamples = string.Join(" ", Array.ConvertAll(ms, m => m.ToString("F1")));
            var sorted = (float[])ms.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
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
        /// con la torre in lontananza di giorno e di notte, il piede della torre, la navata, l'interno della cappella e la sala del boss di una catacomba; poi la sabbia piena di cose in
        /// ognuna delle sei regioni, un campo acceso di notte (luci, fumo, tende) e la tempesta di sabbia. Sopra i 33 ms un posto e' un guaio.</summary>
        IEnumerator Perf()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Debug.Log("[PryingEyes] desertotest prestazioni: senza scheda grafica non si misura"); yield break; }
            var pl = Pl; var sky = DesertSky.I;
            perfWorst = 0f;
            if (sky != null) sky.SetHour(10f, true);
            pl.Teleport(new Vector3(18f, 0.06f, 1.0f));
            Face(new Vector3(18f, 1.2f, -120f));
            yield return PerfAt("sagrato verso la sabbia");
            var tower = DesertWorld.Landmark();
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
            var chapel = DesertWorld.Welcome();
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
            // ---- il deserto piu' vivo: la sabbia piena di cose in ogni regione, un campo acceso di notte, la tempesta di sabbia
            var sc = DesertScatter.I;
            if (sc != null)
            {
                var campTask = FindProps(Prop.FirePit);
                if (sky != null) sky.SetHour(10f, true);
                foreach (var kv in RegionSpots())
                {
                    yield return GoTo(kv.Value.x, kv.Value.y);
                    yield return WaitFor(() => sc.CellsPending == 0 && sc.CellsReady >= 60, 40f);
                    float eye = DesertWorld.Height(kv.Value.x, kv.Value.y) + 1.7f;
                    Face(new Vector3(kv.Value.x + 40f * Mathf.Sin(0.6f), eye - 1.0f, kv.Value.y + 40f * Mathf.Cos(0.6f)));
                    yield return PerfAt($"sabbia aperta nella regione {kv.Key} (di giorno)", 3f);
                }
                yield return WaitFor(() => campTask.IsCompleted, 120f);
                if (campTask.IsCompleted && !campTask.IsFaulted && campTask.Result.TryGetValue(Prop.FirePit, out var fire))
                {
                    yield return GoTo(fire.x + 14f, fire.z);
                    yield return WaitFor(() => sc.Fires.Count > 0, 25f);
                    if (sky != null) sky.SetHour(23f, true);
                    Face(fire + Vector3.up * 0.8f);
                    yield return PerfAt("un campo di notte (fuoco, luci, fumo, tende)", 4f);
                }
                if (sky != null) { sky.SetHour(10f, true); sky.ForceStorm(1f); }
                yield return PerfAt("sabbia aperta nella tempesta di sabbia (giorno)", 6f);
                if (sky != null) sky.ForceStorm(-1f);
            }
            if (sky != null) sky.Frozen = false;
            Debug.Log($"[PryingEyes] desertotest prestazioni: il posto peggiore costa {perfWorst:F1} ms a 1920x1080");
        }

        /// <summary>Solo con -desertonly costi (come prestazioni: serve la scheda grafica): dove vanno i millisecondi. In quattro posti (il sagrato, la sabbia delle dune, l'interno della cappella, la
        /// sala del boss di una catacomba) misura il fotogramma intero e poi spegnendo, una alla volta, le cose sulla sabbia, la vita, le particelle, le luci puntuali, le ombre del sole, i luoghi
        /// lontani, la sabbia stessa e il post-processing. Non controlla niente: scrive una riga per posto e serve a scegliere che cosa alleggerire.</summary>
        IEnumerator Costs()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Debug.Log("[PryingEyes] desertotest costi: senza scheda grafica non si misura"); yield break; }
            var pl = Pl; var sky = DesertSky.I; var sc = DesertScatter.I;
            if (sky != null) sky.SetHour(10f, true);
            pl.Teleport(new Vector3(18f, 0.06f, 1.0f));
            Face(new Vector3(18f, 1.2f, -120f));
            yield return CostAt("sagrato verso la sabbia");
            var spots = RegionSpots();
            if (sc != null && spots.TryGetValue(Region.Dunes, out var dune))
            {
                yield return GoTo(dune.x, dune.y);
                yield return WaitFor(() => sc.CellsPending == 0 && sc.CellsReady >= 60, 40f);
                float eye = DesertWorld.Height(dune.x, dune.y) + 1.7f;
                Face(new Vector3(dune.x + 40f * Mathf.Sin(0.6f), eye - 1.0f, dune.y + 40f * Mathf.Cos(0.6f)));
                yield return CostAt("sabbia aperta nelle dune");
            }
            var chapel = DesertWorld.Welcome();
            if (chapel != null && chapel.plan != null)
            {
                yield return GoTo(chapel.CentreX, chapel.fz0 - 12f);
                yield return WaitFor(() => { var rt = RtOf(chapel); return rt != null && AllBuilt(rt) && rt.DressedRooms >= rt.DressableRooms; }, 70f);
                pl.Teleport(Centre(chapel.plan.rooms[1], chapel.padY + 0.1f));
                yield return CostAt("dentro la cappella");
            }
            var cata = NearestCatacomb(null);
            if (cata != null)
            {
                yield return GoTo(cata.CentreX, cata.fz0 - 12f);
                yield return WaitFor(() => { var rt = RtOf(cata); return AllBuilt(rt) && rt.DressedRooms > 2; }, 90f);
                pl.Teleport(Centre(PlanGen.HallRoom(cata.levels[cata.boss]), cata.LevelY(cata.boss) + 0.1f));
                yield return CostAt("nella sala del boss di una catacomba");
            }
            if (sky != null) sky.Frozen = false;
        }

        IEnumerator CostAt(string place)
        {
            yield return new WaitForSeconds(3f);
            float cold = RenderMs(0); string coldSamples = renderSamples;         // il primo disegno dopo la pausa, senza riscaldare: serve a vedere quanto pesa il freddo
            float all = RenderMs();
            var sb = new System.Text.StringBuilder($"[PryingEyes] desertotest costi {place}: tutto {all:F1} ms (a freddo {cold:F1}: {coldSamples})");
            var sc = DesertScatter.I; var life = DesertLife.I;
            var lights = new List<Light>(); foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type != LightType.Directional && l.enabled) lights.Add(l);
            var parts = new List<ParticleSystemRenderer>(FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None));
            var skins = new List<GameObject>(); foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name.StartsWith("Lontano_")) skins.Add(t.gameObject);
            var volumes = new List<UnityEngine.Rendering.Volume>(FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None));
            var sun = RenderSettings.sun; var sunShadows = sun != null ? sun.shadows : LightShadows.None;
            var terrain = DesertTerrain.I;
            sb.Append($" [{lights.Count} luci, {parts.Count} particelle, {skins.Count} luoghi lontani, {(sc != null ? sc.Instances : 0)} cose]");
            var steps = new List<(string, Action, Action)>
            {
                ("le cose sulla sabbia", () => { if (sc != null) sc.enabled = false; }, () => { if (sc != null) sc.enabled = true; }),
                ("la vita", () => { if (life != null) life.enabled = false; }, () => { if (life != null) life.enabled = true; }),
                ("le particelle", () => { foreach (var p in parts) if (p != null) p.enabled = false; }, () => { foreach (var p in parts) if (p != null) p.enabled = true; }),
                ("le luci puntuali", () => { foreach (var l in lights) if (l != null) l.enabled = false; }, () => { foreach (var l in lights) if (l != null) l.enabled = true; }),
                ("le ombre del sole", () => { if (sun != null) sun.shadows = LightShadows.None; }, () => { if (sun != null) sun.shadows = sunShadows; }),
                ("i luoghi lontani", () => { foreach (var g in skins) if (g != null) g.SetActive(false); }, () => { foreach (var g in skins) if (g != null) g.SetActive(true); }),
                ("la sabbia", () => { if (terrain != null) terrain.gameObject.SetActive(false); }, () => { if (terrain != null) terrain.gameObject.SetActive(true); }),
                ("il post-processing", () => { foreach (var v in volumes) if (v != null) v.enabled = false; }, () => { foreach (var v in volumes) if (v != null) v.enabled = true; }),
            };
            foreach (var (name, off, on) in steps)
            {
                off(); yield return null; yield return null;
                float ms = RenderMs(8);
                on(); yield return null;
                sb.Append($", senza {name} {ms:F1}");
            }
            yield return null; yield return null;
            sb.Append($", di nuovo tutto {RenderMs(8):F1}");
            Debug.Log(sb.ToString());
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
            var refTower = DesertWorld.Landmark();
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
            if (entered) yield return DeepestRoom(s, name);
        }

        /// <summary>Dentro: la stanza piu' lontana dall'ingresso, contando le porte del piano, si raggiunge a piedi. Le porte dei muri crollati sono varchi senza architrave (Opening.Gap):
        /// con un architrave a 1,5 m non ci passava nessuno, ne' il giocatore ne' un nemico (e la porta d'ingresso di tre luoghi di questa prova era cosi').</summary>
        IEnumerator DeepestRoom(Site s, string name)
        {
            var p = s.plan; var entry = EntryRoom(p);
            var depth = new Dictionary<int, int> { { entry.id, 0 } };
            var queue = new Queue<int>(); queue.Enqueue(entry.id);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                foreach (var o in p.openings)
                {
                    if (o.b < 0 || o.oneSided || o.sill >= 0.01f || o.style == OpenStyle.Passaggio || o.style == OpenStyle.Ascensore) continue;
                    int v = o.a == u ? o.b : o.b == u ? o.a : -1;
                    if (v < 0 || depth.ContainsKey(v)) continue;
                    var rv = p.rooms[v];
                    if (rv.IsLift || rv.noFloor || rv.Open) continue;
                    depth[v] = depth[u] + 1; queue.Enqueue(v);
                }
            }
            Room far = null; int best = 1;
            foreach (var kv in depth) if (kv.Value > best) { best = kv.Value; far = p.rooms[kv.Key]; }
            if (far == null) yield break;                                   // una capanna: dopo l'ingresso non c'e' altro
            Vector3 to = Centre(far, s.padY + 0.1f);
            bool ok = false;
            yield return Walk(to, 90f, r => ok = r);
            Check($"{name}: dentro si arriva a piedi fino alla stanza piu' lontana ({best} porte dall'ingresso)", ok, Pl.transform.position.ToString("F1") + " verso " + to.ToString("F1") + $", stanza {far.id} {far.kind}");
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

        /// <summary>Solo con -desertonly esterni -desertshots: le fotografie degli esterni. Di ogni luogo scelto (un edificio per genere e stato, due torri, due catacombe, la cappella) quattro
        /// scorci da terra dagli spigoli e uno dall'alto, a mezzogiorno; e la cattedrale da quattro lati. Servono a guardare con gli occhi che cosa si vede da fuori: tetti, muri
        /// sopra i tetti bassi, rovine, finestre. Non misura niente: i controlli sono che il luogo si costruisca.</summary>
        IEnumerator Exteriors()
        {
            if (!shots) { Debug.Log("[PryingEyes] desertotest esterni: senza -desertshots non c'e' niente da fotografare"); yield break; }
            var sky = DesertSky.I;
            if (sky != null) sky.SetHour(10.5f, true);
            var picks = new List<Site>();
            var seen = new HashSet<string>();
            var towers = new List<Site>(); var catas = new List<Site>();
            var chapel = DesertWorld.Welcome();
            for (int ring = 1; ring <= 10; ring++)
                for (int cx = -ring; cx <= ring; cx++)
                    for (int cz = -ring; cz <= ring; cz++)
                    {
                        if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) != ring) continue;
                        var s = DesertWorld.SiteOf(cx, cz);
                        if (s == null) continue;
                        if (s.IsTower) { if (towers.Count < 2) towers.Add(s); }
                        else if (s.IsCatacomb) { if (catas.Count < 1) catas.Add(s); }
                        else if (s.plan != null && picks.Count < 12 && seen.Add(s.kind + "/" + s.state)) picks.Add(s);
                    }
            if (chapel != null) picks.Insert(0, chapel);
            picks.AddRange(towers); picks.AddRange(catas);
            Debug.Log($"[PryingEyes] desertotest esterni: {picks.Count} luoghi");
            int n = 0;
            foreach (var s in picks)
            {
                n++;
                float h = s.IsTower ? Mathf.Min(s.Height, 40f) : 6f;
                float cxm = s.CentreX, czm = s.CentreZ, midY = s.padY + (s.IsTower ? Mathf.Min(s.Height * 0.4f, 20f) : 2.5f);
                float dist = Mathf.Max(18f, 0.45f * Mathf.Max(s.fx1 - s.fx0, s.fz1 - s.fz0) + (s.IsTower ? 40f : 14f));
                yield return GoTo(cxm, s.fz0 - 14f);
                yield return WaitFor(() => AllBuilt(RtOf(s)), 90f);
                Check($"esterno {s.kind}/{s.state}: costruito", AllBuilt(RtOf(s)), "");
                yield return new WaitForSeconds(1f);
                string nm = $"ext_{n:00}_{s.kind}_{s.state}";
                var spots = new[]
                {
                    new Vector2(cxm - dist, czm - dist), new Vector2(cxm + dist, czm - dist), new Vector2(cxm + dist, czm + dist), new Vector2(cxm - dist, czm + dist),
                };
                for (int i = 0; i < spots.Length; i++)
                {
                    yield return GoTo(spots[i].x, spots[i].y);
                    Face(new Vector3(cxm, midY, czm));
                    yield return new WaitForSeconds(0.9f);
                    Face(new Vector3(cxm, midY, czm));
                    yield return null;
                    Shot(nm + "_" + (char)('a' + i));
                    yield return null;
                }
                // dall'alto: il tetto
                var up = new Vector3(cxm, s.padY + (s.IsTower ? Mathf.Min(s.Height * 0.5f, 30f) : 26f) + 30f, czm - dist * 1.15f);
                Pl.Teleport(up);
                Face(new Vector3(cxm, s.padY + (s.IsTower ? Mathf.Min(s.Height, 36f) * 0.6f : 3f), czm));
                yield return null;
                Shot(nm + "_up");
                yield return null;
            }
            // la cattedrale da quattro lati
            var views = new[] { new Vector3(-55f, 0f, 33f), new Vector3(105f, 0f, 33f), new Vector3(23f, 0f, 130f), new Vector3(23f, 0f, -80f) };
            for (int i = 0; i < views.Length; i++)
            {
                yield return GoTo(views[i].x, views[i].z);
                Face(new Vector3(23f, 7f, 33f));
                yield return new WaitForSeconds(1f);
                Face(new Vector3(23f, 7f, 33f));
                yield return null;
                Shot("ext_cattedrale_" + (char)('a' + i));
                yield return null;
            }
            if (sky != null) sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly muri -desertshots (nessun controllo sulle foto: si guardano): i muri da vicino, da fuori. Di sette luoghi (la cappella e un edificio per genere e stato) e di una torre, per ognuno
        /// dei quattro lati: un raggio dall'esterno verso il centro, all'altezza degli occhi, trova la prima superficie (la pelle del muro) e si fanno due foto: una a due metri guardando il muro di fronte e una a due
        /// metri dal muro ma due metri e mezzo piu' in la', di sbieco (a luce radente si vedono le facce che lampeggiano, le fessure e i pezzi mancanti). A mezzogiorno e mezza. Il solo controllo e' che di ogni luogo
        /// si trovi il muro da almeno due lati su quattro: altrimenti la fase non avrebbe fotografato niente.</summary>
        IEnumerator CloseWalls()
        {
            if (!shots) { Debug.Log("[PryingEyes] desertotest muri: senza -desertshots non c'e' niente da fotografare"); yield break; }
            var sky = DesertSky.I;
            if (sky != null) sky.SetHour(11.5f, true);
            var picks = new List<Site>();
            var seen = new HashSet<string>();
            Site tower = null;
            var chapel = DesertWorld.Welcome();
            for (int ring = 1; ring <= 10; ring++)
                for (int cx = -ring; cx <= ring; cx++)
                    for (int cz = -ring; cz <= ring; cz++)
                    {
                        if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) != ring) continue;
                        var s = DesertWorld.SiteOf(cx, cz);
                        if (s == null) continue;
                        if (s.IsTower) { if (tower == null) tower = s; }
                        else if (!s.IsCatacomb && s.plan != null && picks.Count < 6 && seen.Add(s.kind + "/" + s.state)) picks.Add(s);
                    }
            if (chapel != null) picks.Insert(0, chapel);
            if (tower != null) picks.Add(tower);
            Debug.Log($"[PryingEyes] desertotest muri: {picks.Count} luoghi");
            int n = 0, photos = 0;
            foreach (var s in picks)
            {
                n++;
                float cxm = s.CentreX, czm = s.CentreZ;
                yield return GoTo(cxm, s.fz0 - 14f);
                yield return WaitFor(() => AllBuilt(RtOf(s)), 90f);
                Check($"muri {s.kind}/{s.state}: costruito", AllBuilt(RtOf(s)), "");
                yield return new WaitForSeconds(1.5f);                      // i collider dei muri entrano nella fisica al prossimo passo
                string nm = $"muro_{n:00}_{s.kind}_{s.state}";
                // prima si cercano i quattro muri (i raggi partono da fuori, 10 m oltre il bordo), poi si fanno le foto
                var wall = new Vector3[4]; var dirs = new Vector3[4]; var found = new bool[4];
                for (int side = 0; side < 4; side++)
                {
                    Vector3 dir = side == 0 ? Vector3.forward : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.left;      // da fuori verso il centro
                    Vector3 along = Vector3.Cross(Vector3.up, dir);
                    float half = (side % 2 == 0 ? s.fz1 - s.fz0 : s.fx1 - s.fx0) * 0.5f, wide = side % 2 == 0 ? s.fx1 - s.fx0 : s.fz1 - s.fz0;
                    dirs[side] = dir;
                    foreach (float off in new[] { 0.2f, -0.2f, 0f })
                    {
                        var origin = new Vector3(cxm, 0f, czm) - dir * (half + 10f) + along * (off * wide);
                        origin.y = DesertWorld.Height(origin.x, origin.z) + 1.6f;
                        if (Physics.Raycast(origin, dir, out var hit, 14f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) { wall[side] = hit.point; found[side] = true; break; }
                    }
                }
                int hits = 0;
                foreach (bool f in found) if (f) hits++;
                Check($"muri {s.kind}/{s.state}: il muro si trova da almeno due lati su quattro", hits >= 2, $"trovato da {hits}");
                for (int side = 0; side < 4; side++)
                {
                    if (!found[side]) continue;
                    Vector3 dir = dirs[side], along = Vector3.Cross(Vector3.up, dir), w = wall[side];
                    string tag = nm + "_" + "SWNE"[side];
                    for (int view = 0; view < 2; view++)
                    {
                        Vector3 stand = w - dir * 2f + (view == 1 ? along * 2.5f : Vector3.zero);
                        yield return GoTo(stand.x, stand.z);
                        PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
                        var look = new Vector3(w.x, Pl.cam.position.y, w.z);
                        Face(look);
                        yield return new WaitForSeconds(0.4f);
                        Face(look);
                        yield return null;
                        Shot(tag + (view == 0 ? "_f" : "_o"));
                        photos++;
                        yield return null;
                    }
                }
            }
            Debug.Log($"[PryingEyes] desertotest muri: {photos} foto");
            if (sky != null) sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly portale -desertshots (nessun controllo sulle foto: si guardano): il muro della cattedrale dove c'e' il portale, da vicino. Il giocatore riferiva che e' "un po' buggato"
        /// e la fase dei muri da vicino non passa dalla cattedrale (non e' un luogo del mondo ma la pianta della zona sicura, HubPlan). Si va nel sagrato (x 0-36, z 0-18 m; il muro della facciata e' a z = 18, il portale
        /// a x = 18, largo 3,2 e alto 4,6) e si fotografa da otto punti: dalla nascita, a sei metri, a due, di sbieco da sinistra e da destra, in su verso l'architrave, il muro del battistero e il tratto di
        /// muro fuori dalla chiesa; poi due dall'interno, dal nartece verso la porta. A tre ore: l'alba (alle sette si nasce, il sole radente fa vedere le facce che lampeggiano e le ombre sbagliate),
        /// mezzogiorno e mezza e il tramonto.</summary>
        IEnumerator CathedralDoor()
        {
            if (!shots) { Debug.Log("[PryingEyes] desertotest portale: senza -desertshots non c'e' niente da fotografare"); yield break; }
            var sky = DesertSky.I;
            const float wallZ = 18f, doorX = 18f;
            var views = new (string name, float x, float z, Vector3 look)[]
            {
                ("a_nascita", HubPlan.SpawnX, HubPlan.SpawnZ, new Vector3(doorX, 3.0f, wallZ)),
                ("b_sei_metri", doorX, wallZ - 6f, new Vector3(doorX, 2.6f, wallZ)),
                ("c_due_metri", doorX, wallZ - 2.2f, new Vector3(doorX, 2.4f, wallZ)),
                ("d_sinistra", doorX - 6.5f, wallZ - 2.2f, new Vector3(doorX - 3.5f, 2.6f, wallZ)),
                ("e_destra", doorX + 6.5f, wallZ - 2.2f, new Vector3(doorX + 3.5f, 2.6f, wallZ)),
                ("f_architrave", doorX, wallZ - 3.2f, new Vector3(doorX, 6.4f, wallZ)),
                ("g_battistero", 9f, wallZ - 2.5f, new Vector3(9f, 2.8f, wallZ)),
                ("h_fuori_chiesa", 3f, wallZ - 2.5f, new Vector3(3f, 2.5f, wallZ)),
                ("i_dentro_porta", doorX, wallZ + 4.2f, new Vector3(doorX, 2.4f, wallZ)),
                ("j_dentro_sbieco", doorX - 3.5f, wallZ + 2.0f, new Vector3(doorX, 2.6f, wallZ)),
            };
            int photos = 0;
            foreach (float hour in new[] { 7.3f, 11.5f, 17.3f })
            {
                if (sky != null) sky.SetHour(hour, true);
                foreach (var v in views)
                {
                    yield return GoTo(v.x, v.z);
                    PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
                    Face(v.look);
                    yield return new WaitForSeconds(0.5f);
                    Face(v.look);
                    yield return null;
                    Shot($"portale_{(int)hour:00}_{v.name}");
                    photos++;
                    yield return null;
                }
            }
            Debug.Log($"[PryingEyes] desertotest portale: {photos} foto");
            if (sky != null) sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly rovine (senza GPU, anche con -nographics): la prova dei muri. Costruisce il guscio di una trentina di luoghi (edifici interi, in rovina, spezzati, in crollo; tre torri,
        /// due catacombe) due volte, con i muri della 0.9.0 (FloorBuilder.LegacyWalls) e con quelli di adesso, e su ogni costruzione conta con DesertCensus i triangoli capovolti, le facce sovrapposte a filo e i
        /// buchi visti da fuori (raggi dall'altezza degli occhi). La prova passa se i muri di adesso non hanno nessun difetto e se la stessa prova li trova nei muri della 0.9.0 (altrimenti non misura niente).</summary>
        IEnumerator Ruins()
        {
            var sites = new List<Site>(); var seen = new HashSet<Site>();
            int towers = 0, catas = 0, houses = 0;
            void Take(Site s)
            {
                if (s == null || !seen.Add(s)) return;
                if (s.IsTower) { if (towers >= 3) return; towers++; }
                else if (s.IsCatacomb) { if (catas >= 2) return; catas++; }
                else if (s.plan == null || houses >= 28) return;
                else houses++;
                sites.Add(s);
            }
            Take(DesertWorld.Welcome()); Take(DesertWorld.Landmark());
            for (int ring = 1; ring <= 10; ring++)
                for (int cx = -ring; cx <= ring; cx++)
                    for (int cz = -ring; cz <= ring; cz++)
                        if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) == ring) Take(DesertWorld.SiteOf(cx, cz));
            Debug.Log($"[PryingEyes] desertotest rovine: {sites.Count} luoghi ({houses} edifici, {towers} torri, {catas} catacombe)");
            Check("ci sono luoghi da guardare", sites.Count >= 10 && houses >= 8, $"{sites.Count} luoghi");
            var flipped = new int[2]; var overlaps = new int[2]; var facadeOv = new int[2]; var leaks = new int[2]; var panes = new int[2]; var rays = new int[2]; var tris = new long[2]; var hiddenOv = new int[2];
            var allKinds = new Dictionary<string, int>();         // le facce sovrapposte che si vedono, per coppia di materiali, sommate su tutti i luoghi (con i muri di adesso)
            var rng = new System.Random(DesertWorld.Seed * 31 + 7);
            // i fotogrammi di questa fase non dicono niente del gioco: costruisce gusci interi in un fotogramma, apposta
            phaseName = "";
            int pngSites = 0;
            bool pngBoth = false;           // -rovineboth: disegna anche con i muri della 0.9.0 (il prima, da mettere accanto al dopo)
            string pngMatch = null;         // -rovinematch Locanda/Collapse|Casa/Ruin: disegna i luoghi la cui etichetta (genere/stato) contiene uno dei testi, oltre ai primi N
            { var args = Environment.GetCommandLineArgs(); for (int i = 0; i < args.Length - 1; i++) { if (args[i] == "-rovinepng") int.TryParse(args[i + 1], out pngSites); if (args[i] == "-rovinematch") pngMatch = args[i + 1]; } pngBoth = Array.IndexOf(args, "-rovineboth") >= 0; }
            int siteIndex = 0;
            try
            {
                foreach (var s in sites)
                {
                    siteIndex++;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var census = new DesertCensus();
                        var units = new List<int>();
                        if (!s.IsStack) units.Add(0);
                        else if (s.IsCatacomb) units.Add(s.entrance);
                        else for (int u = 0; u < s.LevelCount; u++) units.Add(u);
                        foreach (int u in units)
                        {
                            var holder = new GameObject("Rovine").transform;
                            FloorBuilder.LegacyWalls = pass == 0;
                            FloorInstance fi;
                            try { fi = SiteRt.BuildShellOf(s, u, holder); }
                            finally { FloorBuilder.LegacyWalls = false; }
                            fi.root.transform.position = new Vector3(0f, s.IsStack ? s.LevelY(u) : s.padY, 0f);
                            census.Add(fi.root);
                            Scrap(holder.gameObject);
                            yield return null;
                        }
                        census.CountFlips();
                        yield return null;
                        yield return census.CountOverlaps();
                        yield return null;
                        yield return census.ShootLeaks(rng, 2400, s.padY, 10f);
                        string tag = pass == 0 ? "0.9.0" : "ora";
                        Debug.Log($"[PryingEyes] desertotest rovine {s.kind}/{s.state} ({s.theme}, cella {s.cx},{s.cz}) {tag}: {census.Summary()}");
                        foreach (var smp in census.Samples) Debug.Log("[PryingEyes] desertotest rovine     " + smp);
                        foreach (var op in census.TopOverlapPairs(4)) Debug.Log("[PryingEyes] desertotest rovine     sovrapposte per coppia: " + op);
                        bool wantPng = siteIndex <= pngSites;
                        if (!wantPng && pngMatch != null) foreach (var part in pngMatch.Split('|')) if ($"{s.kind}/{s.state}".Contains(part)) wantPng = true;
                        if (wantPng && (pass == 1 || pngBoth))
                        {
                            // quello che si vede da fuori, disegnato dalla CPU: da due angoli a un metro e settanta e da uno a sei metri (sulla duna); le torri da piu' lontano
                            float rad = census.Radius, top = census.TopY - s.padY;
                            float dist = s.IsTower ? Mathf.Min(70f, rad + top * 0.9f) : rad + 9f;
                            Vector3 ctr = census.Centre; ctr.y = s.padY + (s.IsTower ? Mathf.Min(top * 0.4f, 16f) : Mathf.Min(top * 0.4f, 3.5f));
                            var views = new[] { ("sw", 225f, 1.7f), ("ne", 45f, 1.7f), ("se", 135f, 6f) };
                            foreach (var (vn, az, eyeH) in views)
                            {
                                float a = az * Mathf.Deg2Rad;
                                var eye = new Vector3(ctr.x + Mathf.Sin(a) * dist, s.padY + eyeH, ctr.z + Mathf.Cos(a) * dist);
                                yield return census.Render(TestPaths.Out($"deserto/census/{siteIndex:00}_{s.kind}_{s.state}_{tag}_{vn}.png"), eye, ctr, 640, 360, 70f, s.padY);
                                foreach (var smp in census.Samples) if (smp.StartsWith("magenta")) Debug.Log($"[PryingEyes] desertotest rovine     {vn}: {smp}");
                                census.Samples.RemoveAll(x => x.StartsWith("magenta"));
                            }
                        }
                        flipped[pass] += census.Flipped; overlaps[pass] += census.Overlaps; facadeOv[pass] += census.OverlapsFacade; leaks[pass] += census.LeaksWall + census.LeaksFlat; panes[pass] += census.LeaksPane; rays[pass] += census.Rays; tris[pass] += census.Tris; hiddenOv[pass] += census.Hidden;
                        if (pass == 1) foreach (var kv in census.OverlapKinds) { allKinds.TryGetValue(kv.Key, out int k0); allKinds[kv.Key] = k0 + kv.Value; }
                    }
                }
            }
            finally { FloorBuilder.LegacyWalls = false; }
            for (int pass = 0; pass < 2; pass++)
                Debug.Log($"[PryingEyes] desertotest rovine TOTALE {(pass == 0 ? "0.9.0" : "ora")}: {tris[pass]} triangoli, capovolti {flipped[pass]}, sovrapposti a filo {overlaps[pass]} ({facadeOv[pass]} di facciata; e {hiddenOv[pass]} nascosti, innocui), buchi {leaks[pass]} su {rays[pass]} raggi, vetri visti da dietro {panes[pass]}");
            var kindList = new List<KeyValuePair<string, int>>(allKinds);
            kindList.Sort((p, q) => q.Value.CompareTo(p.Value));
            for (int k = 0; k < kindList.Count && k < 30; k++) Debug.Log($"[PryingEyes] desertotest rovine sovrapposte (ora) {kindList[k].Value} x {kindList[k].Key}");
            // Le soglie sono la misura del prima e del dopo sugli stessi luoghi (e non zero): in una rovina un raggio entra legittimamente da una porta, da una finestra rotta o da un crollo e vede il retro
            // di una faccia interna; sui tramezzi di due stanze crollate resta una fascia in cima dove si vede attraverso (documentato in docs/game/the-desert.md). Servono a vedere che i difetti tornino.
            Check("muri di adesso: nessun triangolo capovolto", flipped[1] == 0, $"{flipped[1]} (0.9.0: {flipped[0]})");
            Check("muri di adesso: le facce di facciata sovrapposte a filo (che si vedono) sono il 5% o meno di quelle della 0.9.0", facadeOv[1] * 20 <= facadeOv[0], $"{facadeOv[1]} (0.9.0: {facadeOv[0]}; in tutto, con gli interni che non si toccano: {overlaps[1]} contro {overlaps[0]})");
            Check("muri di adesso: da fuori si vede dentro i muri il 40% delle volte della 0.9.0 o meno", leaks[1] * 100 <= leaks[0] * 40, $"{leaks[1]} buchi su {rays[1]} raggi (0.9.0: {leaks[0]} su {rays[0]})");
            Check("muri di adesso: i vetri delle finestre si vedono anche da fuori (2% o meno dei raggi sui vetri visti da dietro)", panes[1] * 50 <= panes[0], $"{panes[1]} raggi su vetri visti da dietro (0.9.0: {panes[0]})");
            Check("la prova dei muri vede i difetti della 0.9.0 (se no non misura niente)", flipped[0] > 0 && facadeOv[0] > 0 && leaks[0] > 0 && panes[0] > 0, $"capovolti {flipped[0]}, sovrapposti di facciata {facadeOv[0]}, buchi {leaks[0]}, vetri {panes[0]}");
        }

        /// <summary>Distrugge un guscio costruito solo per contarne i triangoli, con le sue mesh (non vanno via con l'oggetto).</summary>
        static void Scrap(GameObject go)
        {
            var meshes = new List<Mesh>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
            UnityEngine.Object.DestroyImmediate(go);
            foreach (var m in meshes) UnityEngine.Object.DestroyImmediate(m);
        }

        /// <summary>Solo con -desertonly orizzonte: le pelle dei luoghi lontani (SiteFacade) e le regioni. Da tre punti (il sagrato, la prima regione, una regione lontana) si aspetta che
        /// tutte le pelle entro 760 m siano costruite e si guarda l'orizzonte in quattro direzioni (con -desertshots anche le foto, di giorno e al tramonto). Controlli: ci sono
        /// luoghi all'orizzonte, le pelle si finiscono, nessuna resta accesa dove il guscio vero e' completo, e il costo si misura.</summary>
        IEnumerator Horizon()
        {
            var sky = DesertSky.I;
            var spots = new[] { ("dal sagrato", new Vector2(23f, -70f)), ("nella prima regione", new Vector2(330f, 240f)), ("in una regione lontana", new Vector2(2300f, -1900f)), ("in un'altra regione", new Vector2(-2600f, 1500f)) };
            int w = 0;
            foreach (var (name, at) in spots)
            {
                w++;
                if (sky != null) sky.SetHour(10.5f, true);
                float t0 = Time.realtimeSinceStartup;
                yield return GoTo(at.x, at.y);
                var region = DesertWorld.RegionAt(at.x, at.y);
                yield return WaitFor(() => Dd != null && Dd.FarSkins > 0 && Dd.FarSkinsPending == 0, 90f);
                float took = Time.realtimeSinceStartup - t0;
                Check($"orizzonte {name} ({region}): le pelle lontane accese sono tutte costruite", Dd != null && Dd.FarSkins > 0 && Dd.FarSkinsPending == 0, $"{Dd?.FarSkinsDone}/{Dd?.FarSkins} costruite ({Dd?.FarSkinsShown} accese, {Dd?.FarSkinsPending} da finire) in {took:F1} s, {Dd?.FarVertices} vertici");
                yield return new WaitForSeconds(1.5f);
                int onBoth = 0;
                foreach (var rt in Dd.Loaded)
                {
                    var f = Dd.FarSkinOf(rt.site);
                    if (f != null && rt.ShellComplete && f.Shown) onBoth++;
                }
                Check($"orizzonte {name}: dove il guscio vero e' completo la pelle lontana e' spenta", onBoth == 0, $"{onBoth} luoghi con tutti e due, {Dd.LoadedSites} gusci, {Dd.FarSkinsShown} pelle accese");
                Check($"orizzonte {name}: le pelle pesano poco", Dd.FarVertices < 1500000, $"{Dd.FarVertices} vertici in {Dd.FarSkins} pelle");
                if (!shots) continue;
                float eye = DesertWorld.Height(at.x, at.y) + 1.7f;
                for (int d = 0; d < 4; d++)
                {
                    float a = (d * 90f + 20f) * Mathf.Deg2Rad;
                    Face(new Vector3(at.x + Mathf.Sin(a) * 500f, eye + 4f, at.y + Mathf.Cos(a) * 500f));
                    yield return new WaitForSeconds(0.8f);
                    Face(new Vector3(at.x + Mathf.Sin(a) * 500f, eye + 4f, at.y + Mathf.Cos(a) * 500f));
                    yield return null;
                    Shot($"orizzonte_{w}_{region}_{(char)('a' + d)}");
                    yield return null;
                }
                if (sky != null) sky.SetHour(19.1f, true);
                float a2 = 110f * Mathf.Deg2Rad;
                Face(new Vector3(at.x + Mathf.Sin(a2) * 500f, eye + 4f, at.y + Mathf.Cos(a2) * 500f));
                yield return new WaitForSeconds(1.4f);
                Shot($"orizzonte_{w}_{region}_sera");
                yield return null;
            }
            if (sky != null) sky.Frozen = false;
        }

        /// <summary>Un punto panoramico in una regione: il piu' rilevato della zona attorno al suo centro (il suo punto sta sopra la media del terreno a 160 m e il terreno attorno varia molto),
        /// con la pendenza dolce per starci in piedi, senza luoghi a meno di 70 m; e la direzione (radianti, x = cos, z = sin) in cui davanti il terreno sale di piu' a 220 m: un muro di
        /// duna o di mesa e' l'inquadratura che dice com'e' fatto il paesaggio.</summary>
        static bool Vantage(Region reg, out Vector2 at, out float dir)
        {
            at = default; dir = 0f;
            if (!RegionSpots().TryGetValue(reg, out var c)) return false;
            var list = new List<Site>();
            float best = float.MinValue;
            for (float dx = -900f; dx <= 900f; dx += 45f)
                for (float dz = -900f; dz <= 900f; dz += 45f)
                {
                    float x = c.x + dx, z = c.y + dz;
                    if (DesertWorld.RegionWeight(reg, x, z) < 0.9f) continue;
                    DesertWorld.SitesNear(x, z, 70f, list);
                    if (list.Count > 0) continue;
                    float h0 = DesertWorld.Height(x, z);
                    if (Mathf.Abs(DesertWorld.Height(x + 3f, z) - DesertWorld.Height(x - 3f, z)) > 1.8f || Mathf.Abs(DesertWorld.Height(x, z + 3f) - DesertWorld.Height(x, z - 3f)) > 1.8f) continue;
                    float sum = 0f, lo = h0, hi = h0;
                    for (int k = 0; k < 16; k++)
                    {
                        float a = k * Mathf.PI / 8f, hh = DesertWorld.Height(x + Mathf.Cos(a) * 160f, z + Mathf.Sin(a) * 160f);
                        sum += hh; lo = Mathf.Min(lo, hh); hi = Mathf.Max(hi, hh);
                    }
                    float score = (h0 - sum / 16f) + 0.5f * (hi - lo);
                    if (score > best) { best = score; at = new Vector2(x, z); }
                }
            if (best == float.MinValue) return false;
            float h00 = DesertWorld.Height(at.x, at.y), rise = float.MinValue;
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8f, hh = DesertWorld.Height(at.x + Mathf.Cos(a) * 220f, at.y + Mathf.Sin(a) * 220f);
                if (hh - h00 > rise) { rise = hh - h00; dir = a; }
            }
            return true;
        }

        /// <summary>Solo con -desertonly paesaggio -desertshots (nessun controllo: foto): il colpo d'occhio. Il primo sguardo dal sagrato (all'alba della partenza, di giorno, al tramonto, di notte, nella
        /// tempesta) e, da un punto panoramico di tre regioni (le dune, le terre aspre, la terra santa), il paesaggio di giorno, il sole basso in faccia, la sera e la notte. Serve a giudicare
        /// il cielo, la luce, il grading e il rilievo senza indovinare dai numeri.</summary>
        IEnumerator Landscape()
        {
            var sky = DesertSky.I;
            if (!shots || sky == null) { Debug.Log("[PryingEyes] desertotest paesaggio: senza foto non c'e' niente da fare"); yield break; }
            Pl.Teleport(new Vector3(18f, 0.06f, 1.0f));
            yield return new WaitForSeconds(1.5f);
            var look = new Vector3(18f, 1.6f, -150f);
            foreach (var (nm, hr) in new[] { ("alba", 7.1f), ("giorno", 11.0f), ("tramonto", 17.6f), ("notte", 22.6f) })
                yield return Snap($"paesaggio_0_sagrato_{nm}", hr, look, 1.0f);
            sky.ForceStorm(1f);
            yield return Snap("paesaggio_0_sagrato_tempesta", 11.0f, look, 3.0f);
            sky.ForceStorm(-1f);
            int n = 0;
            foreach (var reg in new[] { Region.Dunes, Region.Badlands, Region.Holy })
            {
                n++;
                if (!Vantage(reg, out var at, out float dir)) { Debug.Log($"[PryingEyes] desertotest paesaggio: nessun punto panoramico nella regione {reg}"); continue; }
                sky.SetHour(11.0f, true);
                yield return GoTo(at.x, at.y);
                yield return WaitFor(() => DesertScatter.I == null || (DesertScatter.I.CellsPending == 0 && DesertScatter.I.CellsReady >= 40), 40f);
                float eye = DesertWorld.Height(at.x, at.y) + 1.7f;
                var ahead = new Vector3(at.x + Mathf.Cos(dir) * 500f, eye + 3f, at.y + Mathf.Sin(dir) * 500f);
                Debug.Log($"[PryingEyes] desertotest paesaggio {reg}: da ({at.x:F0}, {at.y:F0}) a {eye:F1} m, si guarda verso {dir * Mathf.Rad2Deg:F0} gradi");
                yield return Snap($"paesaggio_{n}_{reg}_giorno", 11.0f, ahead, 2.0f);
                var sunLow = DesertClock.Evaluate(17.4f);
                var toSun = new Vector3(at.x + sunLow.sunX * 500f, eye + sunLow.sunY * 500f + 12f, at.y + sunLow.sunZ * 500f);
                yield return Snap($"paesaggio_{n}_{reg}_sole", 17.4f, toSun, 1.5f);
                yield return Snap($"paesaggio_{n}_{reg}_sera", 17.8f, ahead, 1.5f);
                yield return Snap($"paesaggio_{n}_{reg}_notte", 22.6f, ahead, 1.5f);
            }
            sky.Frozen = false;
        }

        /// <summary>Un punto in mezzo a ciascuna delle sei regioni (a piu' di 200 m dal confine con un'altra), il piu' vicino alla cattedrale, e uno spazio libero attorno.</summary>
        static Dictionary<Region, Vector2> RegionSpots()
        {
            var found = new Dictionary<Region, Vector2>();
            var list = new List<Site>();
            for (float r = 0f; r < 9000f && found.Count < 6; r += 120f)
                for (int k = 0; k < Mathf.Max(1, (int)(r / 120f) * 6); k++)
                {
                    float a = k * Mathf.PI * 2f / Mathf.Max(1, (int)(r / 120f) * 6);
                    float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                    var reg = DesertWorld.RegionAt(x, z);
                    if (found.ContainsKey(reg) || DesertWorld.RegionWeight(reg, x, z) < 0.99f) continue;
                    DesertWorld.SitesNear(x, z, 60f, list);
                    if (list.Count == 0) found[reg] = new Vector2(x, z);
                }
            return found;
        }

        /// <summary>La copia di un genere di cosa sulla sabbia piu' vicina a un punto, tra i quadrati entro 'radius' (la funzione pura che le piazza, la stessa del regista).</summary>
        static bool NearestProp(Prop kind, Vector2 from, int radius, out PropItem best)
        {
            best = default;
            float bd = float.MaxValue; bool found = false;
            var list = new List<PropItem>();
            int cx0 = Mathf.FloorToInt(from.x / DesertScatterGen.Chunk), cz0 = Mathf.FloorToInt(from.y / DesertScatterGen.Chunk);
            for (int i = -radius; i <= radius; i++)
                for (int j = -radius; j <= radius; j++)
                {
                    DesertScatterGen.Build(cx0 + i, cz0 + j, list);
                    foreach (var it in list)
                    {
                        if (it.kind != (byte)kind) continue;
                        float d = (it.x - from.x) * (it.x - from.x) + (it.z - from.y) * (it.z - from.y);
                        if (d < bd) { bd = d; best = it; found = true; }
                    }
                }
            return found;
        }

        /// <summary>Solo con -desertonly galleria -desertshots (nessun controllo: foto): ogni genere di cosa sulla sabbia visto da vicino, con il sole basso di lato (le 16:12), dalla copia piu' vicina
        /// al punto di una regione dove di solito sta: sassi, massi, lastre, cactus, alberi morti, cespugli, colonne e stele. Serve a giudicare la forma, la grana, il colore e l'ombra di contatto.</summary>
        IEnumerator PropGallery()
        {
            var sky = DesertSky.I; var sc = DesertScatter.I;
            if (!shots || sky == null || sc == null) { Debug.Log("[PryingEyes] desertotest galleria: senza foto non c'e' niente da fare"); yield break; }
            var spots = RegionSpots();
            var sun = DesertClock.Evaluate(16.2f);
            var sunH = new Vector2(sun.sunX, sun.sunZ).normalized;
            var perp = new Vector2(-sunH.y, sunH.x);
            // genere, regione dove sta, distanza dalla camera (m) e a che altezza (m, a scala 1) si guarda
            var plan = new (Prop kind, Region reg, float dist, float lookY)[]
            {
                (Prop.RockTan, Region.Dunes, 3.2f, 0.30f), (Prop.RockWhite, Region.Holy, 3.2f, 0.30f), (Prop.RockDark, Region.Dunes, 3.2f, 0.30f), (Prop.RockRed, Region.Badlands, 3.2f, 0.30f),
                (Prop.BoulderTan, Region.Dunes, 7.0f, 0.90f), (Prop.BoulderRed, Region.Badlands, 7.0f, 0.90f), (Prop.Slab, Region.Badlands, 6.5f, 1.10f),
                (Prop.Cactus, Region.Dunes, 5.0f, 1.20f), (Prop.DeadTree, Region.Dunes, 7.5f, 1.70f), (Prop.Bush, Region.Dunes, 3.4f, 0.35f),
                (Prop.Column, Region.Holy, 6.0f, 1.00f), (Prop.Stele, Region.Holy, 6.0f, 1.10f), (Prop.Pebbles, Region.Dunes, 2.4f, 0.10f),
            };
            foreach (var e in plan)
            {
                if (!spots.TryGetValue(e.reg, out var at)) { Debug.Log($"[PryingEyes] desertotest galleria: nessun punto nella regione {e.reg}"); continue; }
                if (!NearestProp(e.kind, at, 5, out var it)) { Debug.Log($"[PryingEyes] desertotest galleria: nessun {e.kind} vicino a ({at.x:F0}, {at.y:F0})"); continue; }
                float d = e.dist * Mathf.Max(0.8f, it.scale * 0.75f);
                var view = new Vector2(it.x, it.z) + (perp * 0.87f + sunH * 0.5f) * d;
                sky.SetHour(16.2f, true);
                yield return GoTo(view.x, view.y, 5f);
                yield return WaitFor(() => sc.CellsPending == 0, 25f);
                var look = new Vector3(it.x, it.y + e.lookY * it.scale + 0.15f, it.z);
                Debug.Log($"[PryingEyes] desertotest galleria {e.kind}: a ({it.x:F0}, {it.z:F0}), scala {it.scale:F2}, vista da {d:F1} m");
                yield return Snap($"galleria_{e.kind}", 16.2f, look, 1.0f);
            }
            if (sky != null) sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly look -desertshots (nessun controllo: foto): lo stesso panorama di una vedetta delle dune (al mattino, di giorno e col sole basso in faccia) con sette tarature
        /// diverse della luce del giorno, del cielo e dell'alone del sole, una dopo l'altra, per scegliere quella che sta meglio guardando le foto (look_<nome>_mattino / _giorno / _sole). Rimette tutto com'era.</summary>
        IEnumerator LookSweep()
        {
            var sky = DesertSky.I;
            if (!shots || sky == null) { Debug.Log("[PryingEyes] desertotest look: senza foto non c'e' niente da fare"); yield break; }
            if (!Vantage(Region.Dunes, out var at, out float dir)) { Debug.Log("[PryingEyes] desertotest look: nessun punto panoramico nelle dune"); yield break; }
            sky.SetHour(11.0f, true);
            yield return GoTo(at.x, at.y);
            yield return WaitFor(() => DesertScatter.I == null || (DesertScatter.I.CellsPending == 0 && DesertScatter.I.CellsReady >= 40), 40f);
            float eye = DesertWorld.Height(at.x, at.y) + 1.7f;
            var ahead = new Vector3(at.x + Mathf.Cos(dir) * 500f, eye + 3f, at.y + Mathf.Sin(dir) * 500f);
            var sunLow = DesertClock.Evaluate(17.4f);
            var toSun = new Vector3(at.x + sunLow.sunX * 500f, eye + sunLow.sunY * 500f + 12f, at.y + sunLow.sunZ * 500f);
            float gain0 = DesertSky.DaySkyGain, zenith0 = DesertSky.DayZenithGain, cloud0 = DesertSky.DayCloudGain, glow0 = DesertSky.SunGlow, light0 = DesertSky.DayLight;
            // nome, quanta luce di giorno (sole e ambiente), guadagno dello zenit e dell'orizzonte, bagliore del sole. Per scegliere i valori: il cielo (un blu piu' o meno carico), quanta luce
            // vuole la sabbia, e l'alone del sole (piu' o meno largo)
            var variants = new (string name, float light, float zenith, float horizon, float glow)[]
            {
                ("a_nuovo", light0, zenith0, gain0, glow0),
            };
            foreach (var v in variants)
            {
                DesertSky.DayLight = v.light; DesertSky.DayZenithGain = v.zenith; DesertSky.DaySkyGain = v.horizon; DesertSky.SunGlow = v.glow;
                yield return Snap($"look_{v.name}_mattino", 7.3f, ahead, 1.6f);
                yield return Snap($"look_{v.name}_giorno", 11.0f, ahead, 1.6f);
                yield return Snap($"look_{v.name}_pomeriggio", 15.5f, ahead, 1.6f);
                yield return Snap($"look_{v.name}_sole", 17.4f, toSun, 1.6f);
                yield return Snap($"look_{v.name}_tramonto", 17.9f, ahead, 1.6f);
            }
            DesertGrade.Tweak = null; DesertSky.ExposureOffset = 0f; DesertSky.DayZenithGain = zenith0; DesertSky.DaySkyGain = gain0; DesertSky.DayCloudGain = cloud0; DesertSky.TestTonemap = -1; DesertSky.SunGlow = glow0; DesertSky.DayLight = light0;
            sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly rilievi -desertshots (nessun controllo: foto): i rilievi isolati (DesertWorld.Buttes). I due piu' vicini alla cattedrale, ognuno visto da lontano
        /// (il profilo sull'orizzonte, col sole basso alle spalle e un po' di lato) e dal pietrisco ai piedi della parete (le strisce di roccia, i solchi). Serve a giudicare la forma, la roccia e
        /// quanto si vedono da lontano.</summary>
        IEnumerator ButteShots()
        {
            var sky = DesertSky.I;
            if (!shots || sky == null) { Debug.Log("[PryingEyes] desertotest rilievi: senza foto non c'e' niente da fare"); yield break; }
            var found = new List<(float d, float x, float z, float r, float h, float a, float asp)>();
            for (int i = -4; i <= 4; i++)
                for (int j = -4; j <= 4; j++)
                    if (DesertWorld.ButteOfCell(i, j, out float px, out float pz, out float r, out float h, out float a, out float asp))
                        found.Add((DesertWorld.DistToCathedral(px, pz), px, pz, r, h, a, asp));
            found.Sort((p, q) => p.d.CompareTo(q.d));
            Debug.Log($"[PryingEyes] desertotest rilievi: {found.Count} nei 8,1 km attorno, il piu' vicino a {(found.Count > 0 ? found[0].d : 0f):F0} m dalla cattedrale");
            var sun = DesertClock.Evaluate(16.2f);
            var sunH = new Vector2(sun.sunX, sun.sunZ).normalized;
            for (int n = 0; n < Mathf.Min(2, found.Count); n++)
            {
                var b = found[n];
                float cy = DesertWorld.Height(b.x, b.z);
                // da lontano: sul lato del sole (il sole alle spalle, 35 gradi di lato), a 500 m dal centro
                var side = new Vector2(sunH.x * Mathf.Cos(0.61f) - sunH.y * Mathf.Sin(0.61f), sunH.x * Mathf.Sin(0.61f) + sunH.y * Mathf.Cos(0.61f));
                var far = new Vector2(b.x, b.z) + side * 500f;
                sky.SetHour(16.2f, true);
                yield return GoTo(far.x, far.y, 5f);
                yield return WaitFor(() => DesertScatter.I == null || DesertScatter.I.CellsPending == 0, 25f);
                Debug.Log($"[PryingEyes] desertotest rilievo {n + 1}: al centro ({b.x:F0}, {b.z:F0}), alto {b.h:F0} m, raggio {b.r:F0} m, a {b.d:F0} m dalla cattedrale");
                yield return Snap($"rilievo_{n + 1}_lontano", 16.2f, new Vector3(b.x, cy + b.h * 0.55f, b.z), 1.2f);
                // dal piede: sul lato corto, nel pietrisco, dalla parte del sole
                var perp = new Vector2(-Mathf.Sin(b.a), Mathf.Cos(b.a));
                if (Vector2.Dot(perp, sunH) < 0f) perp = -perp;
                var foot = new Vector2(b.x, b.z) + perp * (b.r * b.asp * 1.45f);
                yield return GoTo(foot.x, foot.y, 3f);
                yield return WaitFor(() => DesertScatter.I == null || DesertScatter.I.CellsPending == 0, 25f);
                yield return Snap($"rilievo_{n + 1}_piede", 16.2f, new Vector3(b.x, cy + b.h * 0.8f, b.z), 1.2f);
            }
            sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly nuvole -desertshots (nessun controllo: foto): il cielo con tre coperture (DesertSky.CloudOverride 0,12 / 0,27 / 0,45) a tre ore (mezzogiorno, il
        /// pomeriggio, il tramonto), una volta dalla parte opposta al sole e una verso di lui: la forma e la luce delle nuvole e i raggi di sole. Serve a giudicare il cielo a occhio.</summary>
        IEnumerator CloudsPhase()
        {
            var sky = DesertSky.I;
            if (!shots || sky == null) { Debug.Log("[PryingEyes] desertotest nuvole: senza foto non c'e' niente da fare"); yield break; }
            Pl.Teleport(new Vector3(18f, 0.06f, 1.0f));
            yield return new WaitForSeconds(1.5f);
            Vector3 eye = new Vector3(18f, 1.6f, 1.0f);
            foreach (float cover in new[] { 0.12f, 0.27f, 0.45f })
            {
                DesertSky.CloudOverride = cover;
                foreach (var (nm, hr) in new[] { ("mezzogiorno", 12.0f), ("pomeriggio", 16.4f), ("tramonto", 17.9f) })
                {
                    var ev = DesertClock.Evaluate(hr);
                    var flat = new Vector3(-ev.sunX, 0f, -ev.sunZ);
                    flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                    var away = eye + flat * 300f + Vector3.up * 150f;                       // il cielo opposto al sole, a quasi trenta gradi
                    var toSun = eye + new Vector3(ev.sunX, ev.sunY, ev.sunZ) * 300f;
                    string tag = $"nuvole_{(int)(cover * 100):D2}_{nm}";
                    yield return Snap(tag + "_lontano", hr, away, 1.2f);
                    yield return Snap(tag + "_sole", hr, toSun, 1.2f);
                }
            }
            DesertSky.CloudOverride = -1f;
            sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly creste: la sabbia che il vento stacca dalle creste (DesertSpindrift). Da un punto panoramico delle dune, con la ricerca delle creste a ritmo triplo e un
        /// vento che c'e' di sicuro, si controlla che almeno due creste comincino a fumare, che ci siano particelle vive e che non siano troppe; con -desertshots tre fotografie (sole alto alle
        /// spalle, il sole basso di lato e di fronte) per vedere se i veli si leggono e non sembrano trattini.</summary>
        IEnumerator SpindriftTest()
        {
            var sp = DesertSpindrift.I; var sky = DesertSky.I;
            Check("creste: il sistema c'e'", sp != null, "DesertSpindrift.I e' nullo");
            if (sp == null || sky == null) yield break;
            if (!Vantage(Region.Dunes, out var at, out float dir)) { Debug.Log("[PryingEyes] desertotest creste: nessun punto panoramico nelle dune"); yield break; }
            sky.SetHour(16.4f, true);
            yield return GoTo(at.x, at.y);
            yield return WaitFor(() => DesertScatter.I == null || DesertScatter.I.CellsPending == 0, 25f);
            float eye = DesertWorld.Height(at.x, at.y) + 1.7f;
            var ahead = new Vector3(at.x + Mathf.Cos(dir) * 400f, eye + 6f, at.y + Mathf.Sin(dir) * 400f);
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Face(ahead);
            DesertSpindrift.Boost = 8f;                 // la ricerca e' a caso (e la direzione del vento cambia con l'ora): a ritmo triplo e in 25 s due creste erano al limite (un giro 2, il successivo 1)
            int p0 = sp.Plumes;
            yield return WaitFor(() => sp.Plumes - p0 >= 2 && sp.Alive > 20, 45f);
            float w = sky.Weather.wind;
            Check("creste: con un po' di vento almeno due creste cominciano a fumare", sp.Plumes - p0 >= 2, $"{sp.Plumes - p0} creste in 45 s, vento {w:F2}");
            Check("creste: ci sono particelle vive e non sono troppe", sp.Alive > 20 && sp.Alive <= 1400, $"{sp.Alive} particelle, {sp.Smoking} creste che fumano");
            Debug.Log($"[PryingEyes] desertotest creste: {sp.Plumes - p0} creste, {sp.Alive} particelle, vento {w:F2}");
            if (shots)
            {
                yield return new WaitForSeconds(4f);
                yield return Snap("creste_avanti", 16.4f, ahead, 1.2f);
                var sunLow = DesertClock.Evaluate(17.4f);
                var toSun = new Vector3(at.x + sunLow.sunX * 500f, eye + sunLow.sunY * 500f + 12f, at.y + sunLow.sunZ * 500f);
                yield return new WaitForSeconds(3f);
                yield return Snap("creste_sole", 17.4f, toSun, 1.2f);
                var side = new Vector3(at.x + Mathf.Cos(dir + 1.2f) * 400f, eye + 6f, at.y + Mathf.Sin(dir + 1.2f) * 400f);
                yield return Snap("creste_lato", 17.0f, side, 1.2f);
            }
            DesertSpindrift.Boost = 1f;
            if (sky != null) sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly suolo -desertshots (nessun controllo: foto): la sabbia da vicino in cinque punti aperti attorno alla cattedrale. Per ognuno, di traverso al sole (la luce
        /// radente fa leggere il rilievo): al mattino il suolo a pochi metri, la sera il suolo a pochi metri e la distesa fino a settanta metri. Serve a giudicare le increspature (regolari? storte?),
        /// i sassi sparsi, il grano e le ombre, senza indovinare dai numeri.</summary>
        IEnumerator GroundShots()
        {
            var sky = DesertSky.I;
            if (!shots || sky == null) { Debug.Log("[PryingEyes] desertotest suolo: senza foto non c'e' niente da fare"); yield break; }
            var list = new List<Site>();
            int n = 0;
            for (int k = 0; k < 60 && n < 5; k++)
            {
                float a = (k * 61f + 15f) * Mathf.Deg2Rad, r = 420f + 130f * (k % 5);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                DesertWorld.SitesNear(x, z, 80f, list);
                if (list.Count > 0) continue;
                if (Mathf.Abs(DesertWorld.Height(x + 3f, z) - DesertWorld.Height(x - 3f, z)) > 0.8f || Mathf.Abs(DesertWorld.Height(x, z + 3f) - DesertWorld.Height(x, z - 3f)) > 0.8f) continue;
                n++;
                var reg = DesertWorld.RegionAt(x, z);
                sky.SetHour(9.0f, true);
                yield return GoTo(x, z, 4f);
                yield return WaitFor(() => DesertScatter.I == null || DesertScatter.I.CellsPending == 0, 25f);
                Vector3 p = Pl.transform.position;
                var st = DesertClock.Evaluate(17.0f);
                var sunH = new Vector2(st.sunX, st.sunZ).normalized;
                var side = new Vector3(-sunH.y, 0f, sunH.x);
                var near = p + side * 4.5f; near.y = DesertWorld.Height(near.x, near.z);
                var far = p + side * 70f; far.y = DesertWorld.Height(far.x, far.z);
                Debug.Log($"[PryingEyes] desertotest suolo {n}: da ({p.x:F0}, {p.z:F0}) nella regione {reg}");
                yield return Snap($"suolo_{n}_{reg}_mattino", 9.0f, near, 1.0f);
                yield return Snap($"suolo_{n}_{reg}_sera_vicino", 17.0f, near, 1.0f);
                yield return Snap($"suolo_{n}_{reg}_sera_lontano", 17.0f, far, 1.0f);
                if (n == 1 || n == 3)
                {
                    // il pavimento di ciottoli, dove il rumore non lo mette: forzato (lo shader legge _DSPebbleBoost), da vicino a luce radente e a mezzogiorno
                    Shader.SetGlobalFloat("_DSPebbleBoost", 1f);
                    yield return Snap($"suolo_{n}_{reg}_ciottoli_sera", 17.0f, near, 1.0f);
                    yield return Snap($"suolo_{n}_{reg}_ciottoli_giorno", 11.5f, near, 1.0f);
                    Shader.SetGlobalFloat("_DSPebbleBoost", 0f);
                }
            }
            Shader.SetGlobalFloat("_DSPebbleBoost", 0f);
            sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly salina: le pozze sulle saline (una pellicola d'acqua sul sale che specchia il cielo). Si va in mezzo alla prima salina che c'e' (dal centro della regione si
        /// cerca entro 1,5 km), si sceglie un punto in piena acqua (piatto, senza luoghi a meno di 90 m e con l'acqua tutt'attorno) e uno sulla riva asciutta con un lago davanti; si controlla che ci
        /// siano e che un passo nell'acqua non lasci l'impronta mentre uno sulla sabbia asciutta si'. Con -desertshots le fotografie: dalla riva verso il lago all'alba, a mezzogiorno, al tramonto e
        /// di notte, dall'acqua verso il sole basso e le stelle, e il pelo dell'acqua da vicino.</summary>
        IEnumerator BrineTest()
        {
            var sky = DesertSky.I; var pl = Pl;
            if (!RegionSpots().TryGetValue(Region.Salt, out var c)) { Debug.Log("[PryingEyes] desertotest salina: nessuna salina entro 9 km"); yield break; }
            var list = new List<Site>();
            Vector2 inLake = default, shore = default; float shoreDir = 0f; bool haveLake = false, haveShore = false;
            float bestLake = -1f, bestShore = -1f;
            for (float dx = -1500f; dx <= 1500f; dx += 50f)
                for (float dz = -1500f; dz <= 1500f; dz += 50f)
                {
                    float x = c.x + dx, z = c.y + dz;
                    if (DesertWorld.RegionWeight(Region.Salt, x, z) < 0.99f) continue;
                    float b0 = DesertWorld.Brine(x, z);
                    bool lakeCand = b0 > 0.95f, shoreCand = b0 <= 0f;
                    if (!lakeCand && !shoreCand) continue;
                    // quanta acqua c'e' su un anello di 60 m e su uno di 120 m (un lago vero, non una pozzanghera)
                    float ring = 0f;
                    for (int k = 0; k < 12; k++)
                    {
                        float a = k * Mathf.PI / 6f;
                        ring += DesertWorld.Brine(x + Mathf.Cos(a) * 60f, z + Mathf.Sin(a) * 60f) + DesertWorld.Brine(x + Mathf.Cos(a) * 120f, z + Mathf.Sin(a) * 120f);
                    }
                    ring /= 24f;
                    // la riva: asciutta qui, e a 70 e a 130 m in una direzione c'e' acqua piena
                    float shoreScore = -1f, shoreA = 0f;
                    if (shoreCand)
                        for (int k = 0; k < 16; k++)
                        {
                            float a = k * Mathf.PI / 8f, sc = Mathf.Min(DesertWorld.Brine(x + Mathf.Cos(a) * 70f, z + Mathf.Sin(a) * 70f), DesertWorld.Brine(x + Mathf.Cos(a) * 130f, z + Mathf.Sin(a) * 130f));
                            if (sc > shoreScore) { shoreScore = sc; shoreA = a; }
                        }
                    bool takeLake = lakeCand && ring > bestLake, takeShore = shoreCand && shoreScore > 0.9f && shoreScore + ring > bestShore;
                    if (!takeLake && !takeShore) continue;
                    // solo ora i controlli cari (il terreno, i luoghi): piatto e senza luoghi a meno di 90 m
                    if (Mathf.Abs(DesertWorld.Height(x + 3f, z) - DesertWorld.Height(x - 3f, z)) > 0.6f || Mathf.Abs(DesertWorld.Height(x, z + 3f) - DesertWorld.Height(x, z - 3f)) > 0.6f) continue;
                    DesertWorld.SitesNear(x, z, 90f, list);
                    if (list.Count > 0) continue;
                    if (takeLake) { bestLake = ring; inLake = new Vector2(x, z); haveLake = true; }
                    if (takeShore) { bestShore = shoreScore + ring; shore = new Vector2(x, z); shoreDir = shoreA; haveShore = true; }
                }
            Check("salina: ci sono laghi sulla salina", haveLake && haveShore, $"{(haveLake ? "un punto in piena acqua" : "nessun punto in piena acqua")}, {(haveShore ? "una riva" : "nessuna riva")} entro 1,5 km da ({c.x:F0}, {c.y:F0})");
            if (!haveLake || !haveShore) yield break;
            Debug.Log($"[PryingEyes] desertotest salina: lago a ({inLake.x:F0}, {inLake.y:F0}), riva a ({shore.x:F0}, {shore.y:F0}) verso {shoreDir * Mathf.Rad2Deg:F0} gradi");
            // un passo in acqua non lascia l'impronta, uno sulla sabbia asciutta si'
            var pr = DesertPrints.I;
            if (pr != null && sky != null)
            {
                sky.SetHour(11.0f, true);
                yield return GoTo(inLake.x, inLake.y, 3f);
                int s0 = pr.Stamped;
                DesertPrints.Stamp(pl.transform.position, Vector3.forward);
                DesertPrints.Stamp(pl.transform.position + Vector3.right * 0.4f, Vector3.forward);
                Check("salina: un passo nell'acqua non lascia l'impronta", pr.Stamped == s0, $"{pr.Stamped - s0} impronte nell'acqua (brine {DesertWorld.Brine(pl.transform.position.x, pl.transform.position.z):F2})");
                var dry = OpenSand();
                yield return GoTo(dry.x, dry.y, 3f);
                int s1 = pr.Stamped;
                DesertPrints.Stamp(pl.transform.position, Vector3.forward);
                DesertPrints.Stamp(pl.transform.position + Vector3.right * 0.4f, Vector3.forward);
                Check("salina: un passo sulla sabbia asciutta lo lascia", pr.Stamped > s1, $"{pr.Stamped - s1} impronte sulla sabbia");
            }
            if (!shots || sky == null) yield break;
            // dalla riva verso il lago
            yield return GoTo(shore.x, shore.y, 4f);
            yield return WaitFor(() => DesertScatter.I == null || DesertScatter.I.CellsPending == 0, 25f);
            float eye = DesertWorld.Height(shore.x, shore.y) + 1.7f;
            var lake = new Vector3(shore.x + Mathf.Cos(shoreDir) * 300f, eye + 2f, shore.y + Mathf.Sin(shoreDir) * 300f);
            foreach (var (nm, hr) in new[] { ("alba", 7.1f), ("giorno", 11.5f), ("tramonto", 17.4f), ("notte", 22.6f) })
                yield return Snap($"salina_riva_{nm}", hr, lake, 1.2f);
            // dall'acqua: verso il sole basso (la scia di scintille), e di notte verso il cielo
            yield return GoTo(inLake.x, inLake.y, 4f);
            yield return WaitFor(() => DesertScatter.I == null || DesertScatter.I.CellsPending == 0, 25f);
            eye = DesertWorld.Height(inLake.x, inLake.y) + 1.7f;
            foreach (var (nm, hr) in new[] { ("alba", 7.1f), ("tramonto", 17.4f) })
            {
                var st = DesertClock.Evaluate(hr);
                var toSun = new Vector3(inLake.x + st.sunX * 500f, eye + st.sunY * 500f + 14f, inLake.y + st.sunZ * 500f);
                yield return Snap($"salina_acqua_sole_{nm}", hr, toSun, 1.2f);
            }
            var away = new Vector3(inLake.x + Mathf.Cos(shoreDir + 2.0f) * 300f, eye + 60f, inLake.y + Mathf.Sin(shoreDir + 2.0f) * 300f);
            yield return Snap("salina_acqua_notte", 22.6f, away, 1.2f);
            var feet = new Vector3(inLake.x + Mathf.Cos(shoreDir) * 3.5f, DesertWorld.Height(inLake.x, inLake.y), inLake.y + Mathf.Sin(shoreDir) * 3.5f);
            yield return Snap("salina_acqua_vicino", 11.5f, feet, 1.2f);
            sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly orme: le impronte sulla sabbia. Si cammina per qualche secondo su sabbia aperta, di traverso al sole (a luce radente ogni buca ha una parete accesa e una in
        /// ombra); si controlla che ogni passo lasci la sua impronta, vicino ai piedi e posata sul terreno, che in aria non ne resti nessuna e che il vento le cancelli. Con -desertshots tre
        /// fotografie: la scia vista da chi l'ha fatta, le ultime impronte da vicino e le stesse a mezzogiorno (quando si vedono appena).</summary>
        IEnumerator PrintsTest()
        {
            var pr = DesertPrints.I;
            Check("orme: il sistema c'e'", pr != null, "DesertPrints.I e' nullo");
            if (pr == null) yield break;
            var sky = DesertSky.I; var pl = Pl;
            const float hour = 17.3f;
            var at = OpenSand();
            if (sky != null) sky.SetHour(hour, true);
            yield return GoTo(at.x, at.y);
            yield return WaitFor(() => DesertScatter.I == null || DesertScatter.I.CellsPending == 0, 25f);
            pr.Age(10000f);                                             // via quelle di prima
            yield return null;
            int s0 = pr.Stamped;
            var st = DesertClock.Evaluate(hour);
            var sunH = new Vector2(st.sunX, st.sunZ).normalized;
            var dir = new Vector3(-sunH.y, 0f, sunH.x);
            Vector3 start = pl.transform.position;
            PlayerController.holdLook = false; PlayerController.botDir = dir;
            float t0 = Time.time;
            yield return WaitFor(() => pr.Stamped - s0 >= 16, 14f);
            PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
            yield return null;
            Vector3 end = pl.transform.position;
            int made = pr.Stamped - s0;
            Debug.Log($"[PryingEyes] desertotest orme: {made} impronte in {Time.time - t0:F1} s, {Flat(start, end):F1} m di strada, {pr.Alive} ancora sulla sabbia");
            Check("orme: camminando sulla sabbia ogni passo lascia un'impronta", made >= 8, $"{made} impronte in {Time.time - t0:F1} s (da {start:F1} a {end:F1})");
            Check("orme: restano tutte (il vento non ne ha ancora cancellata nessuna)", pr.Alive == Mathf.Min(made, DesertPrints.Max), $"{pr.Alive} vive su {made}");
            bool got = pr.Newest(out var last);
            float ground = got ? DesertWorld.Height(last.x, last.z) : 0f;
            Check("orme: l'ultima e' vicino ai piedi e posata sul terreno", got && Flat(last, end) < 1.6f && Mathf.Abs(last.y - ground) < 0.3f,
                got ? $"a {Flat(last, end):F2} m dai piedi, {last.y - ground:F2} m dal suolo" : "nessuna");
            int s1 = pr.Stamped;
            DesertPrints.Stamp(new Vector3(end.x, 4000f, end.z), Vector3.forward);
            Check("orme: in aria, senza sabbia sotto il piede, non ne resta nessuna", pr.Stamped == s1, $"{pr.Stamped - s1} in piu'");
            if (shots)
            {
                var back = new Vector3(start.x, DesertWorld.Height(start.x, start.z) + 0.2f, start.z);
                yield return Snap("orme_scia", hour, back, 1.0f);
                var near = new Vector3(end.x - dir.x * 2.4f, DesertWorld.Height(end.x - dir.x * 2.4f, end.z - dir.z * 2.4f), end.z - dir.z * 2.4f);
                yield return Snap("orme_vicino", hour, near, 1.0f);
                yield return Snap("orme_vicino_giorno", 11.5f, near, 1.0f);
            }
            pr.Age(DesertPrints.Life + 2f);
            yield return null; yield return null;
            Check("orme: dopo un minuto e mezzo il vento le ha cancellate tutte", pr.Alive == 0, $"{pr.Alive} ancora vive");
            if (sky != null) sky.Frozen = false;
        }

        /// <summary>Solo con -desertonly cose: le cose sulla sabbia (sassi, cactus, alberi, ossa, colonne) nelle sei regioni. Da un punto in mezzo a ciascuna si aspetta che i quadrati attorno
        /// siano calcolati e si controlla che ci sia roba (di piu' generi), che i collider ci siano e che il costo sia basso; con -desertshots tre fotografie per regione.</summary>
        IEnumerator Props()
        {
            var sc = DesertScatter.I;
            Check("c'e' il regista delle cose sulla sabbia", sc != null, sc == null ? "manca" : "ok");
            if (sc == null) yield break;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;      // le foto di Face: niente rotazione automatica
            var sky = DesertSky.I;
            if (sky != null) sky.SetHour(10.2f, true);
            var spots = RegionSpots();
            Check("si trova un punto in ognuna delle sei regioni", spots.Count == 6, $"{spots.Count} regioni");
            foreach (var kv in spots)
            {
                var reg = kv.Key; var at = kv.Value;
                yield return GoTo(at.x, at.y);
                yield return WaitFor(() => sc.CellsPending > 0 ? false : sc.CellsReady >= 60 && sc.Instances > 100, 40f);
                yield return new WaitForSeconds(1.5f);
                int kinds = 0, total = sc.Instances;
                for (int k = 0; k < (int)Prop.Count; k++) if (sc.CountOf((Prop)k) > 0) kinds++;
                Check($"cose {reg}: ci sono cose sulla sabbia ({total}) di piu' generi ({kinds})", total > 150 && kinds >= 6, $"{sc.CellsReady} quadrati, {total} copie, {kinds} generi, {sc.Colliders} collider");
                Check($"cose {reg}: le cose che fermano chi cammina hanno il collider", sc.Colliders > 0 || reg == Region.Salt, $"{sc.Colliders} collider");
                if (!shots) continue;
                float eye = DesertWorld.Height(at.x, at.y) + 1.7f;
                for (int d = 0; d < 3; d++)
                {
                    float a = (d * 120f + 35f) * Mathf.Deg2Rad;
                    Face(new Vector3(at.x + Mathf.Sin(a) * 40f, eye - 3.2f + d * 1.5f, at.y + Mathf.Cos(a) * 40f));
                    yield return new WaitForSeconds(0.7f);
                    Face(new Vector3(at.x + Mathf.Sin(a) * 40f, eye - 3.2f + d * 1.5f, at.y + Mathf.Cos(a) * 40f));
                    yield return null;
                    Shot($"cose_{reg}_{(char)('a' + d)}");
                    yield return null;
                }
            }
            if (sky != null) sky.Frozen = false;
        }

        /// <summary>Un punto a 'dist' metri da un bersaglio da cui lo si vede senza un edificio in mezzo (e che non e' dentro uno): prova otto direzioni, a partire da quella scelta, e prende la prima libera.</summary>
        static Vector2 Vantage(Vector3 target, float dist, float first = 0.9f)
        {
            var sites = new List<Site>();
            for (int k = 0; k < 8; k++)
            {
                float a = first + k * 0.785f;
                float x = target.x + Mathf.Cos(a) * dist, z = target.z + Mathf.Sin(a) * dist;
                bool clear = true;
                for (int s = 0; s <= 12 && clear; s++)
                {
                    float t = s / 12f, px = Mathf.Lerp(x, target.x, t), pz = Mathf.Lerp(z, target.z, t);
                    DesertWorld.SitesNearKnown(px, pz, 6f, sites);
                    foreach (var st in sites)
                        if (px > st.fx0 - 4f && px < st.fx1 + 4f && pz > st.fz0 - 4f && pz < st.fz1 + 4f) { clear = false; break; }
                }
                if (clear) return new Vector2(x, z);
            }
            return new Vector2(target.x + Mathf.Cos(first) * dist, target.z + Mathf.Sin(first) * dist);
        }

        /// <summary>Scrive nel registro quali cose sulla sabbia ci sono entro r metri da un punto (lo dice la funzione pura): per capire che cosa c'e' in una fotografia.</summary>
        static void LogNearProps(Vector3 p, float r)
        {
            var list = new List<PropItem>(); var sb = new System.Text.StringBuilder();
            int c0x = Mathf.FloorToInt(p.x / DesertScatterGen.Chunk), c0z = Mathf.FloorToInt(p.z / DesertScatterGen.Chunk);
            for (int cx = c0x - 1; cx <= c0x + 1; cx++)
                for (int cz = c0z - 1; cz <= c0z + 1; cz++)
                {
                    DesertScatterGen.Build(cx, cz, list);
                    foreach (var it in list)
                    {
                        float dx = it.x - p.x, dz = it.z - p.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d < r) sb.Append($"{(Prop)it.kind} a {d:F1} m, ");
                    }
                }
            Debug.Log($"[PryingEyes] desertotest cose entro {r:F0} m da ({p.x:F0}, {p.z:F0}): {(sb.Length > 0 ? sb.ToString() : "nessuna")}");
        }

        /// <summary>Il posto, vicino alla cattedrale, di ognuna di queste cose sulla sabbia: lo dice la funzione pura (senza caricare niente), su un thread perche' sono migliaia di quadrati.</summary>
        static System.Threading.Tasks.Task<Dictionary<Prop, Vector3>> FindProps(params Prop[] kinds)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                var found = new Dictionary<Prop, Vector3>(); var list = new List<PropItem>();
                for (int ring = 0; ring <= 56 && found.Count < kinds.Length; ring++)
                    for (int cx = -ring; cx <= ring; cx++)
                        for (int cz = -ring; cz <= ring; cz++)
                        {
                            if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) != ring) continue;
                            DesertScatterGen.Build(cx, cz, list);
                            foreach (var it in list)
                            {
                                var k = (Prop)it.kind;
                                if (Array.IndexOf(kinds, k) < 0 || found.ContainsKey(k)) continue;
                                found[k] = new Vector3(it.x, it.y, it.z);
                            }
                        }
                return found;
            });
        }

        /// <summary>Solo con -desertonly vita: la vita del deserto (animali che scappano, sterpaglie, avvoltoi di giorno e pipistrelli di notte sulle rovine), i campi con le tende e il fuoco
        /// (spento di giorno, acceso di notte con la sua luce), le cose grandi (pozzo, obelisco, mulino, carro, casse) e la tempesta di sabbia a comando. Con -desertshots le fotografie.</summary>
        IEnumerator Life()
        {
            var life = DesertLife.I; var fires = DesertFires.I; var dust = DesertDust.I; var sc = DesertScatter.I; var sky = DesertSky.I; var snd = DesertSound.I;
            bool all = life != null && fires != null && dust != null && sc != null && sky != null && snd != null;
            Check("c'e' la vita del deserto: animali, fuochi, sabbia che vola, suoni, scatter e cielo", all, $"{life != null} {fires != null} {dust != null} {snd != null} {sc != null} {sky != null}");
            if (!all) yield break;
            float amb = GameSettings.Ambience;
            // i suoni del deserto sono veri: la durata giusta, voce udibile, nessun numero impazzito
            var soundNotes = new List<string>(); bool soundsOk = true;
            foreach (var (nm, clip, lo, hi) in new[] { ("sibilo", SoundBank.SandHiss, 9f, 11f), ("grilli", SoundBank.Crickets, 7.5f, 8.5f), ("crepitio", SoundBank.FireCrackle, 6.5f, 7.5f), ("gracchio", SoundBank.Caw, 0.5f, 1.3f), ("ululato", SoundBank.Howl, 3f, 4f) })
            {
                float peak = 0f, sum = 0f; bool finite = clip != null;
                if (clip != null)
                {
                    var d = new float[clip.samples];
                    if (!clip.GetData(d, 0)) finite = false;
                    else foreach (float v in d) { if (float.IsNaN(v) || float.IsInfinity(v)) { finite = false; break; } peak = Mathf.Max(peak, Mathf.Abs(v)); sum += v * v; }
                    float rms = Mathf.Sqrt(sum / Mathf.Max(1, d.Length));
                    bool good = finite && clip.length >= lo && clip.length <= hi && peak > 0.3f && peak <= 1.01f && rms > 0.02f && rms < 0.5f;
                    soundNotes.Add($"{nm} {clip.length:F1} s rms {rms:F2}{(good ? "" : " ?")}");
                    soundsOk &= good;
                }
                else { soundNotes.Add(nm + " manca"); soundsOk = false; }
            }
            Check("i cinque suoni del deserto (sibilo, grilli, crepitio, gracchio, ululato) esistono e si sentono", soundsOk, string.Join(", ", soundNotes));
            var findTask = FindProps(Prop.FirePit, Prop.Tent, Prop.Well, Prop.Obelisk, Prop.Windmill, Prop.Wagon, Prop.Crate);
            var spots = RegionSpots();
            var dune = spots.ContainsKey(Region.Dunes) ? spots[Region.Dunes] : OpenSand();

            // ---- di giorno nelle dune: animali e sterpaglie
            sky.SetHour(10.2f, true);
            yield return GoTo(dune.x, dune.y);
            yield return WaitFor(() => life.LizardCount + life.ScorpionCount + life.BeetleCount >= 6 && life.WeedCount >= 3, 25f);
            yield return new WaitForSeconds(1.5f);
            int critters = life.LizardCount + life.ScorpionCount + life.BeetleCount;
            Check("di giorno sulla sabbia ci sono animali e sterpaglie", critters >= 6 && life.WeedCount >= 3, $"{life.LizardCount} lucertole, {life.ScorpionCount} scorpioni, {life.BeetleCount} scarabei, {life.WeedCount} sterpaglie, vento {sky.Weather.wind:F2}");
            yield return WaitFor(() => snd.CricketVolume < 0.01f, 15f);          // i grilli della sera prima calano con una costante di due secondi: se le fasi precedenti hanno finito di notte, ci vuole un momento
            Check("di giorno col tempo buono non ci sono grilli e il sibilo della sabbia e' appena un filo", snd.CricketVolume < 0.02f && snd.SandVolume <= 0.2f * Mathf.Max(amb, 0.01f), $"grilli {snd.CricketVolume:F2}, sibilo {snd.SandVolume:F2}, ambiente {amb:F2}");
            if (life.NearestCritter(Pl.transform.position, -1, out var cp))
            {
                int before = life.Fled;
                Pl.Teleport(new Vector3(cp.x + 2.0f, cp.y + 1.2f, cp.z));
                yield return new WaitForSeconds(0.7f);
                Check("un animale a due metri dal giocatore scappa", life.Fled > before, $"{life.Fled - before} in fuga");
            }
            else Check("c'e' un animale da avvicinare", false, "nessuno");
            if (shots)
            {
                life.Calm = true;
                for (int k = 0; k < 3; k++)
                {
                    if (!life.NearestCritter(Pl.transform.position, k, out var q)) continue;
                    Pl.Teleport(q + new Vector3(1.7f, 1.0f, 0.3f));
                    yield return new WaitForSeconds(1.0f);
                    if (!life.NearestCritter(Pl.transform.position, k, out q)) continue;
                    Face(q + Vector3.up * 0.08f); yield return null; Face(q + Vector3.up * 0.08f); yield return null;
                    Shot("vita_animale_" + (k == 0 ? "lucertola" : k == 1 ? "scorpione" : "scarabeo"));
                    yield return null;
                }
                life.Calm = false;
                // una sterpaglia messa apposta davanti alla camera, a 7 m (la sagoma da lontano) e a 2,6 m (i fusti, i mazzi e le punte): quelle vere rotolano col vento e al momento della foto
                // possono essere altrove (le prime foto della sterpaglia mostravano un sasso, perche' la mira seguiva una che era gia' sparita)
                PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
                var weedCam = Pl.cam.GetComponent<Camera>();
                var weedFwd = Pl.cam.forward; weedFwd.y = 0f;
                weedFwd = weedFwd.sqrMagnitude < 0.01f ? Vector3.forward : weedFwd.normalized;
                foreach (var (weedDist, weedShot) in new[] { (7f, "vita_sterpaglia"), (2.6f, "vita_sterpaglia_da_vicino") })
                {
                    var weedAt = Pl.transform.position + weedFwd * weedDist; weedAt.y = DesertWorld.Height(weedAt.x, weedAt.z);
                    life.PlaceWeed(weedAt, 1.1f);
                    yield return null;
                    if (!life.NearestWeed(Pl.transform.position, out var weedNow)) continue;
                    Face(weedNow + Vector3.up * 0.35f); yield return null; Face(weedNow + Vector3.up * 0.35f); yield return null;
                    var weedVp = weedCam.WorldToViewportPoint(weedNow + Vector3.up * 0.35f);
                    Debug.Log($"[PryingEyes] desertotest sterpaglia {weedShot}: a {Vector3.Distance(weedCam.transform.position, weedNow):F1} m, nel quadro a ({weedVp.x:F2}, {weedVp.y:F2}), {life.WeedCount} sterpaglie");
                    Shot(weedShot);
                    yield return null;
                }
            }

            // ---- gli avvoltoi di giorno e i pipistrelli di notte, sulla torre di riferimento
            var tower = DesertWorld.Landmark();
            if (tower != null)
            {
                yield return GoTo(tower.CentreX, tower.fz0 - 130f);
                // il branco della torre compare piano (4 s) mentre quelli del posto di prima si dissolvono: si aspetta proprio lui, quello che gira sopra la torre
                Vector3 flockAt = default; float alt = 0f;
                yield return WaitFor(() => life.NearestFlock(new Vector3(tower.CentreX, 0f, tower.CentreZ), false, out flockAt, out alt) && Mathf.Abs(flockAt.x - tower.CentreX) < 5f && Mathf.Abs(flockAt.z - tower.CentreZ) < 5f, 30f);
                yield return new WaitForSeconds(1.5f);
                bool overTower = life.NearestFlock(new Vector3(tower.CentreX, 0f, tower.CentreZ), false, out flockAt, out alt) && Mathf.Abs(flockAt.x - tower.CentreX) < 5f && Mathf.Abs(flockAt.z - tower.CentreZ) < 5f;
                Check("di giorno gli avvoltoi fanno la ruota sopra la torre", life.BirdCount >= 3 && overTower && alt >= tower.Height + 10f, $"{life.BirdCount} uccelli, il branco della torre gira a {alt:F0} m sul terreno, torre alta {tower.Height:F0} m");
                int cawsBefore = snd.Caws;
                bool cawed = snd.PlayCaw();
                Check("un avvoltoio gracchia", cawed && snd.Caws == cawsBefore + 1, $"{snd.Caws - cawsBefore} gracchi");
                if (shots && overTower)
                {
                    var look = flockAt + Vector3.up * alt;
                    Face(look); yield return new WaitForSeconds(0.6f); Face(look); yield return null;
                    Shot("vita_avvoltoi");
                    yield return null;
                }
                sky.SetHour(22f, true);
                yield return WaitFor(() => life.BatCount >= 5, 30f);
                yield return new WaitForSeconds(6f);
                Check("di notte i pipistrelli girano attorno alle rovine e gli avvoltoi non ci sono piu'", life.BatCount >= 5 && life.BirdCount == 0, $"{life.BatCount} pipistrelli, {life.BirdCount} avvoltoi");
                yield return WaitFor(() => snd.CricketVolume >= 0.08f * amb, 8f);
                int howlsBefore = snd.Howls;
                bool howled = snd.PlayHowl();
                Check("di notte cantano i grilli e si sente un ululato lontano", snd.CricketVolume >= 0.08f * amb && howled && snd.Howls == howlsBefore + 1, $"grilli {snd.CricketVolume:F2} (ambiente {amb:F2}), {snd.Howls - howlsBefore} ululati");
                sky.SetHour(10.2f, true);
            }

            // ---- il campo: tende e fuoco, spento di giorno e acceso di notte
            yield return WaitFor(() => findTask.IsCompleted, 120f);
            var found = findTask.IsCompleted && !findTask.IsFaulted ? findTask.Result : new Dictionary<Prop, Vector3>();
            Check("si trovano un campo e le cose grandi vicino alla cattedrale", found.ContainsKey(Prop.FirePit) && found.Count >= 5, $"{found.Count} generi su 7: {string.Join(", ", found.Keys)}");
            if (found.TryGetValue(Prop.FirePit, out var fire))
            {
                yield return GoTo(fire.x + 16f, fire.z);
                yield return WaitFor(() => sc.Fires.Count > 0 && sc.CountOf(Prop.Tent) > 0, 25f);
                yield return new WaitForSeconds(1f);
                Check("di giorno il fuoco del campo e' spento, le tende ci sono", fires.Burning == 0 && sc.CountOf(Prop.Tent) > 0 && snd.CrackleVolume < 0.05f, $"{fires.Burning} fuochi accesi, {sc.CountOf(Prop.Tent)} tende, {sc.Fires.Count} fuochi vicini, crepitio {snd.CrackleVolume:F2}");
                if (shots) { Face(fire + Vector3.up * 0.6f); yield return new WaitForSeconds(0.6f); Face(fire + Vector3.up * 0.6f); yield return null; Shot("vita_campo_giorno"); yield return null; }
                sky.SetHour(22f, true);
                yield return WaitFor(() => fires.Burning >= 1, 8f);
                yield return new WaitForSeconds(1.5f);
                Check("di notte il fuoco e' acceso: luce, fiamme e fumo", fires.Burning >= 1 && fires.Lit > 0.9f, $"{fires.Burning} fuochi accesi, notte {fires.Lit:F2}");
                yield return WaitFor(() => snd.CrackleVolume >= 0.3f * amb, 6f);
                Check("di notte vicino al fuoco si sente il crepitio, a pochi metri dalla fiamma", snd.CrackleVolume >= 0.3f * amb && snd.CrackleDistance < 40f, $"volume {snd.CrackleVolume:F2} (ambiente {amb:F2}), il fuoco e' a {snd.CrackleDistance:F0} m");
                if (shots)
                {
                    Face(fire + Vector3.up * 0.6f); yield return new WaitForSeconds(0.5f); Face(fire + Vector3.up * 0.6f); yield return null; Shot("vita_campo_notte_a"); yield return null;
                    var farV = Vantage(fire, 48f, 0.4f);
                    yield return GoTo(farV.x, farV.y);
                    yield return new WaitForSeconds(1f);
                    Face(fire + Vector3.up * 0.6f); yield return new WaitForSeconds(0.5f); Face(fire + Vector3.up * 0.6f); yield return null; Shot("vita_campo_notte_b"); yield return null;
                    LogNearProps(Pl.transform.position, 8f);
                    sky.SetHour(10.5f, true);
                    yield return new WaitForSeconds(1.2f);
                    Face(fire + Vector3.up * 0.6f); yield return new WaitForSeconds(0.4f); Face(fire + Vector3.up * 0.6f); yield return null; Shot("vita_campo_giorno_b"); yield return null;
                }
                sky.SetHour(10.5f, true);
            }

            // ---- le cose grandi, una per genere
            foreach (var (kind, dist, high, name) in new[] { (Prop.Well, 13f, 0.8f, "pozzo"), (Prop.Obelisk, 40f, 4.5f, "obelisco"), (Prop.Windmill, 48f, 5.5f, "mulino"), (Prop.Wagon, 12f, 0.7f, "carro"), (Prop.Crate, 8f, 0.6f, "casse"), (Prop.Tent, 10f, 0.9f, "tenda") })
            {
                if (!found.TryGetValue(kind, out var at)) { Check($"{name}: c'e' vicino alla cattedrale", false, "non trovato entro 3,5 km"); continue; }
                var vp = Vantage(at, dist);
                yield return GoTo(vp.x, vp.y);
                yield return WaitFor(() => sc.CountOf(kind) > 0, 25f);
                yield return new WaitForSeconds(1f);
                Check($"{name}: e' sulla sabbia e si vede", sc.CountOf(kind) > 0 && (kind != Prop.Windmill || sc.Windmills > 0), $"{sc.CountOf(kind)} copie, {sc.Windmills} rotori");
                if (!shots) continue;
                var look = at + Vector3.up * high;
                Face(look); yield return new WaitForSeconds(0.6f); Face(look); yield return null;
                Shot("vita_" + name);
                yield return null;
            }

            // ---- un diavolo di sabbia a comando: gira, ha molte particelle e si toglie
            var dev = DesertDevils.I;
            Check("c'e' il regista dei diavoli di sabbia", dev != null, dev == null ? "manca" : "ok");
            if (dev != null)
            {
                sky.SetHour(12.0f, true);
                yield return GoTo(dune.x, dune.y);
                var fwd = Pl.cam.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
                var dp = Pl.transform.position + fwd * 90f; dp.y = DesertWorld.Height(dp.x, dp.z);
                dev.ForceAt(dp);
                yield return new WaitForSeconds(9f);
                Check("un diavolo di sabbia a comando gira: e' vivo e ha molte particelle", dev.Active && dev.Particles > 30, $"{dev.Particles} particelle, a {Vector3.Distance(dev.Position, Pl.transform.position):F0} m");
                if (shots)
                {
                    var devLook = dev.Position + Vector3.up * 9f;
                    Face(devLook); yield return new WaitForSeconds(0.6f); Face(devLook); yield return null;
                    Shot("vita_diavolo"); yield return null;
                }
                dev.Clear();
                Check("tolto il diavolo non ce n'e' piu'", !dev.Active, dev.Active ? "ancora vivo" : "ok");
                sky.SetHour(10.2f, true);
            }

            // ---- la tempesta di sabbia a comando
            sky.SetHour(10.2f, true);
            yield return GoTo(dune.x, dune.y);
            yield return new WaitForSeconds(1.5f);
            float fogBefore = RenderSettings.fogDensity, sunBefore = RenderSettings.sun != null ? RenderSettings.sun.intensity : 0f;
            var pamb = ProcAmbience.Instance; float windBefore = pamb != null ? pamb.WindVolume : 0f;
            sky.ForceStorm(1f);
            yield return new WaitForSeconds(4.5f);
            float fogStorm = RenderSettings.fogDensity;
            Check("nella tempesta la nebbia si infittisce e il sole cala", fogStorm > fogBefore * 2.5f && RenderSettings.sun != null && RenderSettings.sun.intensity < sunBefore * 0.7f, $"nebbia {fogBefore:F4} -> {fogStorm:F4}, sole {sunBefore:F2} -> {(RenderSettings.sun != null ? RenderSettings.sun.intensity : 0f):F2}");
            Check("nella tempesta la sabbia vola e il vento e' forte", dust.StreakRate > 150f && dust.Alive > 50 && sky.Weather.wind > 0.9f, $"{dust.StreakRate:F0} strisce al secondo, {dust.Alive} granelli vivi, vento {sky.Weather.wind:F2}");
            Check("nella tempesta le sterpaglie corrono e gli animali si riparano", life.WeedCount >= 10 && life.LizardCount + life.ScorpionCount + life.BeetleCount == 0, $"{life.WeedCount} sterpaglie, {life.LizardCount + life.ScorpionCount + life.BeetleCount} animali");
            yield return WaitFor(() => snd.SandVolume >= 0.3f * amb && (pamb == null || pamb.WindVolume > windBefore + 0.1f * amb), 8f);
            Check("nella tempesta si sente il sibilo della sabbia e il vento sale", snd.SandVolume >= 0.3f * amb && (pamb == null || pamb.WindVolume > windBefore + 0.1f * amb) && snd.CricketVolume < 0.02f, $"sibilo {snd.SandVolume:F2}, vento {(pamb != null ? pamb.WindVolume : 0f):F2} (prima {windBefore:F2}), grilli {snd.CricketVolume:F2}, ambiente {amb:F2}");
            if (shots)
            {
                float a0 = Mathf.Atan2(sky.Weather.dirX, sky.Weather.dirZ);
                var eye = Pl.cam.position;
                Face(eye + new Vector3(Mathf.Sin(a0 + 3.14f), 0.05f, Mathf.Cos(a0 + 3.14f)) * 50f); yield return new WaitForSeconds(0.7f); Face(eye + new Vector3(Mathf.Sin(a0 + 3.14f), 0.05f, Mathf.Cos(a0 + 3.14f)) * 50f); yield return null;
                Shot("vita_tempesta_a"); yield return null;
                Face(eye + new Vector3(Mathf.Sin(a0 + 1.57f), 0.02f, Mathf.Cos(a0 + 1.57f)) * 50f); yield return new WaitForSeconds(0.7f); Face(eye + new Vector3(Mathf.Sin(a0 + 1.57f), 0.02f, Mathf.Cos(a0 + 1.57f)) * 50f); yield return null;
                Shot("vita_tempesta_b"); yield return null;
            }
            sky.ForceStorm(-1f);
            yield return new WaitForSeconds(3.5f);
            Check("passata la tempesta la nebbia torna com'era", Mathf.Abs(RenderSettings.fogDensity - fogBefore) < fogBefore * 0.4f, $"nebbia {RenderSettings.fogDensity:F4} (prima {fogBefore:F4})");
            sky.Frozen = false;
        }

        IEnumerator Fall()
        {
            var pl = Pl; var p = pl.transform.position;
            pl.Teleport(new Vector3(p.x, DesertWorld.Height(p.x, p.z) - 60f, p.z));
            // il giocatore e' riportato all'ultimo punto sicuro; se la prova precedente lo aveva spostato lontano (qui di 29 km) quel pezzo di sabbia e' stato scaricato (dal 0.9.1 la sabbia
            // fuori da ogni finestra si butta: prima restava in memoria per sempre) e va ricostruito, quindi il rientro vero puo' richiedere qualche secondo e un secondo giro della rete di sicurezza
            float t0 = Time.time;
            yield return new WaitForSeconds(1.6f);
            yield return WaitFor(() => { var s = pl.transform.position; return s.y > DesertWorld.Height(s.x, s.z) - 6f && pl.GetComponent<CharacterController>().isGrounded; }, 15f);
            var q = pl.transform.position;
            Check("chi cade sotto la sabbia torna sopra", q.y > DesertWorld.Height(q.x, q.z) - 6f, $"{q.ToString("F1")} dopo {Time.time - t0:F1} s, {FloorDirector.statFalls} cadute in tutto");
        }

        /// <summary>Il risultato di una camminata misurata (WalkMeasure): i fotogrammi di chi cammina, senza quelli che l'harness stesso rende lunghi con le sue foto.</summary>
        sealed class WalkResult
        {
            public float walked, seconds, p50, p95, p99, max;
            public int n, over33, over50, over100, over250, hitchesFromSteps, hitchesFromUnloads, hitchesOther;
            public bool arrived;
            public readonly List<string> worst = new List<string>();
        }

        /// <summary>Cammina a piedi (botDir, come con la levetta; se una cosa blocca la strada fa un passo di lato) da dove sta fino a 'target' e misura ogni fotogramma: i singhiozzi di chi cammina
        /// (un luogo che si costruisce, un tassello della navmesh, un quadrato di sabbia, la pulizia della memoria) sono la peggior sensazione in un gioco e nessun'altra prova li vede (le altre
        /// teletrasportano o scattano foto, e una foto da sola costa 150-650 ms). Non conta i fotogrammi che l'harness rende lunghi con le sue foto (renderFrame, HarnessShotFrame). Per ogni
        /// fotogramma sopra i 33 ms dice se in quel fotogramma il direttore ha fatto un passo lento (30 ms o piu'), e per i piu' lunghi dove erano e cosa faceva il direttore.</summary>
        IEnumerator WalkMeasure(Vector3 target, float timeout, WalkResult r)
        {
            var pl = Pl; var dirc = DesertDirector.I;
            var dts = new List<float>();
            float t0 = Time.time, until = t0 + timeout, stillSince = Time.time, turnUntil = 0f, turnSign = 1f, walked = 0f;
            Vector3 start = pl.transform.position, still = start, last = start;
            int slowSeen = dirc != null ? dirc.SlowSteps : 0;
            PlayerController.holdLook = false;
            while (Time.time < until)
            {
                Vector3 pos = pl.transform.position;
                walked += Flat(pos, last); last = pos;
                if (Flat(pos, target) < 8f) { r.arrived = true; break; }
                if (Flat(pos, still) > 0.5f) { still = pos; stillSince = Time.time; }
                else if (Time.time - stillSince > 1.0f) { turnUntil = Time.time + 1.4f; turnSign = -turnSign; stillSince = Time.time; ClearEnemiesAround(pos, 2.0f); }
                Vector3 d = target - pos; d.y = 0f; d.Normalize();
                if (Time.time < turnUntil) d = Quaternion.Euler(0f, 75f * turnSign, 0f) * d;
                PlayerController.botDir = d;
                float dt = Time.unscaledDeltaTime;
                int slowNow = dirc != null ? dirc.SlowSteps : 0;
                bool fromStep = slowNow > slowSeen; slowSeen = slowNow;
                bool fromUnload = dirc != null && Time.frameCount - dirc.LastUnloadFrame <= 1;       // dt e' la durata del fotogramma di prima: se il direttore ha scaricato un luogo li', Destroy ha lavorato a fine fotogramma
                if (Time.frameCount - renderFrame > 1 && Time.frameCount - GameManager.HarnessShotFrame > 2)
                {
                    dts.Add(dt);
                    if (dt > 0.033f)
                    {
                        if (fromStep) r.hitchesFromSteps++; else if (fromUnload) r.hitchesFromUnloads++; else r.hitchesOther++;
                        if (dt > 0.05f && r.worst.Count < 30)
                            r.worst.Add($"{dt * 1000f:F0} ms a {walked:F0} m ({Time.time - t0:F0} s){(fromStep ? ", nel fotogramma di un passo lento del direttore" : "")}{(fromUnload ? $", nel fotogramma dello scarico di {dirc.LastUnloadWhat} ({dirc.LastUnloadObjects} oggetti)" : "")}, luoghi caricati {(dirc != null ? dirc.LoadedSites : -1)}, pelli in costruzione {(dirc != null ? dirc.FarSkinsPending : -1)}, pulizie {(dirc != null ? dirc.Sweeps : -1)}, navmesh occupata {DesertNav.Busy}");
                    }
                }
                yield return null;
            }
            PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
            dts.Sort();
            r.n = dts.Count; r.walked = walked; r.seconds = Time.time - t0;
            float Pc(float f) => r.n == 0 ? 0f : dts[Mathf.Min(r.n - 1, Mathf.FloorToInt(r.n * f))] * 1000f;
            r.p50 = Pc(0.5f); r.p95 = Pc(0.95f); r.p99 = Pc(0.99f); r.max = Pc(1f);
            foreach (var x in dts) { if (x > 0.033f) r.over33++; if (x > 0.05f) r.over50++; if (x > 0.1f) r.over100++; if (x > 0.25f) r.over250++; }
        }

        static string WalkLine(string label, WalkResult r) =>
            $"[PryingEyes] desertotest {label}: {r.walked:F0} m in {r.seconds:F0} s, {r.n} fotogrammi, mediana {r.p50:F1} ms, 95% {r.p95:F1} ms, 99% {r.p99:F1} ms, piu' lungo {r.max:F0} ms; oltre 33 ms {r.over33} (di cui {r.hitchesFromSteps} nel fotogramma di un passo lento del direttore e {r.hitchesFromUnloads} in quello dello scarico di un luogo), 50 ms {r.over50}, 100 ms {r.over100}, 250 ms {r.over250}";

        /// <summary>Camminare sulla sabbia dal piede della scalinata fino alla torre di riferimento (309 m), come chi gioca e senza foto, misurando i fotogrammi (WalkMeasure). Se una cosa blocca la
        /// strada (un sasso, un cactus) fa un passo di lato. Tutto cio' che e' a portata si costruisce all'arrivo, quindi su questa strada quasi nessun luogo si carica: lo fa il percorso lungo.</summary>
        IEnumerator WalkProbe()
        {
            var s = DesertWorld.Landmark();
            yield return GoTo(18f, -10f);
            var target = new Vector3(s.CentreX, 0f, s.fz1 + 25f);
            var r = new WalkResult();
            yield return WalkMeasure(target, 170f, r);
            Debug.Log(WalkLine("cammino", r));
            foreach (var w in r.worst) Debug.Log("[PryingEyes] desertotest cammino, fotogramma lungo: " + w);
            Check("si cammina fino alla torre di riferimento sulla sabbia", r.arrived, $"{r.walked:F0} m in {r.seconds:F0} s, a {Flat(Pl.transform.position, target):F0} m dal punto");
            Check("camminando nessun fotogramma e' un blocco visibile (oltre mezzo secondo)", r.n > 300 && r.max < 500f, $"{r.n} fotogrammi, il piu' lungo {r.max:F0} ms, oltre 100 ms {r.over100}");
        }

        /// <summary>Una direzione da percorrere a piedi per 'len' metri dal punto 'from' (solo verso sud: la cattedrale sta a nord), scelta perche' fa caricare il piu' possibile di luoghi (che
        /// entrano nel raggio di caricamento strada facendo e non erano gia' li' al via) e non ne tocca nessuno (a meno di 18 m), ne' una mesa (a meno di 40 m), ne' la cattedrale. Dice il punto
        /// d'arrivo e quanti luoghi si caricheranno; falso se ogni direzione tocca qualcosa.</summary>
        static bool PickRoute(Vector3 from, float len, out Vector3 end, out int loads)
        {
            end = from; loads = -1;
            var list = new List<Site>(); var seen = new Dictionary<Site, float>();
            for (int k = 0; k < 24; k++)
            {
                float a = (96f + k * 7f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                bool bad = false; seen.Clear();
                for (float d = 0f; d <= len && !bad; d += 20f)
                {
                    float qx = from.x + dir.x * d, qz = from.z + dir.z * d;
                    if (d > 40f && DesertWorld.DistToCathedral(qx, qz) < 25f) { bad = true; break; }
                    if (d > 0f && DesertWorld.ButteTouches(qx - 20f, qz - 20f, qx + 20f, qz + 20f, 40f)) { bad = true; break; }
                    DesertWorld.SitesNear(qx, qz, DesertDirector.LoadTower, list);
                    foreach (var s in list)
                    {
                        float dx = Mathf.Max(Mathf.Max(s.fx0 - qx, qx - s.fx1), 0f), dz = Mathf.Max(Mathf.Max(s.fz0 - qz, qz - s.fz1), 0f);
                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                        if (dist < 18f) { bad = true; break; }
                        if (!seen.TryGetValue(s, out float least) || dist < least) seen[s] = dist;
                    }
                }
                if (bad) continue;
                int n = 0;
                foreach (var kv in seen)
                {
                    var s = kv.Key;
                    float dx = Mathf.Max(Mathf.Max(s.fx0 - from.x, from.x - s.fx1), 0f), dz = Mathf.Max(Mathf.Max(s.fz0 - from.z, from.z - s.fz1), 0f);
                    float atStart = Mathf.Sqrt(dx * dx + dz * dz);
                    float radius = s.IsTower ? DesertDirector.LoadTower : DesertDirector.LoadBuilding;
                    if (atStart > radius + 10f && kv.Value <= radius - 10f) n++;
                }
                if (n > loads) { loads = n; end = from + dir * len; }
            }
            return loads >= 0;
        }

        /// <summary>Il direttore ha finito di costruire cio' che e' a portata: tutti i gusci dei luoghi caricati e tutte le pelli lontane accese.</summary>
        static bool LoadQuiet(DesertDirector d)
        {
            if (d == null || d.FarSkinsPending > 0) return false;
            foreach (var rt in d.Loaded) if (!rt.ShellComplete) return false;
            return true;
        }

        /// <summary>Solo con -desertonly percorso: il cammino vero, lungo (1300 m dal piede della scalinata, nella direzione che fa caricare piu' luoghi senza toccarne nessuno). Quello di
        /// 309 m (cammino) non attraversa quasi nessun confine di caricamento, perche' tutto cio' che e' a portata si costruisce all'arrivo: qui i luoghi entrano nel raggio e ne escono mentre
        /// si cammina, e si vede cosa costa a chi gioca. Il tempo e' fermo alle 9 (a meta' strada non fa notte e non arrivano i vagabondi). Dice i fotogrammi, i luoghi caricati e scaricati e i passi
        /// di lavoro lenti del direttore; non disegna (con -batchmode la camera non disegna): misura la logica, non la scheda.</summary>
        IEnumerator RouteProbe()
        {
            const float len = 1300f;
            var from = new Vector3(18f, 0f, -10f);
            bool picked = PickRoute(from, len, out var end, out int planned);
            Check("c'e' una strada di 1300 m dal piede della scalinata che passa vicino a dei luoghi senza toccarne nessuno", picked && planned >= 1, picked ? $"verso ({end.x:F0}, {end.z:F0}), {planned} luoghi si caricheranno strada facendo" : "ogni direzione tocca qualcosa");
            if (!picked) yield break;
            var sky = DesertSky.I;
            if (sky != null) sky.SetHour(9f, true);
            yield return GoTo(from.x, from.z);
            var dirc = DesertDirector.I;
            yield return WaitFor(() => LoadQuiet(dirc), 45f);
            yield return new WaitForSeconds(1f);
            dirc.ResetStepStats();
            int loads0 = dirc.SitesLoadedTotal, unloads0 = dirc.SitesUnloadedTotal, sweeps0 = dirc.Sweeps;
            var r = new WalkResult();
            var target = new Vector3(end.x, 0f, end.z);
            yield return WalkMeasure(target, 640f, r);
            if (sky != null) sky.Frozen = false;
            int loads = dirc.SitesLoadedTotal - loads0, unloads = dirc.SitesUnloadedTotal - unloads0;
            float left = Flat(Pl.transform.position, target);
            Debug.Log(WalkLine("percorso", r));
            Debug.Log($"[PryingEyes] desertotest percorso: luoghi caricati {loads} (previsti {planned}), scaricati {unloads}, passi di lavoro di 30 ms o piu' {dirc.SlowSteps}, il peggiore {dirc.WorstStepMs} ms ({dirc.WorstStepWhat}), pulizie della memoria {dirc.Sweeps - sweeps0}, a {left:F0} m dal punto");
            foreach (var w in r.worst) Debug.Log("[PryingEyes] desertotest percorso, fotogramma lungo: " + w);
            Check("si cammina per 1300 m caricando e scaricando luoghi strada facendo", r.arrived && loads >= 1, $"{r.walked:F0} m in {r.seconds:F0} s, a {left:F0} m dal punto, {loads} luoghi caricati e {unloads} scaricati");
            Check("camminando per tutta la strada nessun fotogramma e' un blocco visibile (oltre mezzo secondo)", r.n > 1000 && r.max < 500f, $"{r.n} fotogrammi, il piu' lungo {r.max:F0} ms, oltre 100 ms {r.over100}");
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
            var tower = DesertWorld.Landmark();
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
