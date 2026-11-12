using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Prova automatica della componente furtiva: PryingEyes.exe -batchmode -autotest -stealthtest -seed N (senza -nographics
    /// per le foto). Misura dopo quanto tempo un monaco fermo ti nota a varie distanze, al buio e con la torcia; prova l'udito, la pila
    /// che si scarica e si cambia, la raccolta di una pila, il ping e la freccia; fotografa l'HUD. Esito in hotel_build/furtivo/.</summary>
    public partial class StealthTest : MonoBehaviour
    {
        const string Dir = "C:/Users/danie/Desktop/prying-eyes/hotel_build/furtivo";
        string log;

        void Log(string s)
        {
            Debug.Log("[PryingEyes] furtivo " + s);
            File.AppendAllText(log, s + "\n");
        }

        static Camera hudCam;

        IEnumerator ReferenceShots(FloorDirector floor)
        {
            var plan = floor.cur.plan;
            var entrance = plan.Find(RoomKind.Nartece);
            var nave = plan.Find(RoomKind.Navata);
            var portal = plan.openings.Find(o => (o.a == entrance.id && o.b == nave.id) || (o.a == nave.id && o.b == entrance.id));
            if (portal == null)
            {
                Debug.LogError("Arco tra nartece e navata mancante");
                Application.Quit(1);
                yield break;
            }

            string output = System.IO.Path.GetFullPath(Dir + "/../fotorealismo");
            Directory.CreateDirectory(output);
            var pl = GameManager.I.player;
            var cam = pl.cam.GetComponent<Camera>();
            cam.cullingMask &= ~(1 << 2); // viewmodel su Ignore Raycast
            Enemy.ClearAll();
            Enemy.debugFreeze = true;
            Enemy.debugNoSense = true;
            PlayerController.holdLook = true;
            Vector3 threshold = portal.alongX ? new Vector3(portal.center, 0, portal.line) * 0.1f : new Vector3(portal.line, 0, portal.center) * 0.1f;
            Vector3 naveCenter = new Vector3(nave.CenterX, 0f, nave.CenterZ);
            Vector3 forward = portal.alongX ? Vector3.forward * Mathf.Sign(naveCenter.z - threshold.z) : Vector3.right * Mathf.Sign(naveCenter.x - threshold.x);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 hero = threshold - forward * 1.10f - right * 1.32f;
            var monk = Enemy.Spawn(Enemy.EnemyType.Monaco, hero);
            monk.transform.rotation = Quaternion.LookRotation(-forward + right * 0.38f);

            IEnumerator Capture(string name, Vector3 position, Vector3 look, float fov)
            {
                pl.Teleport(new Vector3(position.x, 0.05f, position.z));
                pl.yaw = Mathf.Atan2(forward.x, forward.z);
                yield return new WaitForSeconds(0.45f);
                cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position));
                cam.fieldOfView = fov;
                LightPool.Tick(false, position);
                GameManager.CamShot(output + "/" + name + ".png", cam, false);
            }

            yield return Capture("monaco_arco", threshold - forward * 4.12f - right * 0.45f + Vector3.up * 1.33f,
                                 threshold + forward * 1.35f + Vector3.up * 1.40f, 54f);
            yield return Capture("navata", threshold + forward * 1.25f + Vector3.up * 1.62f,
                                 naveCenter + Vector3.up * 1.9f, 60f);
            yield return Capture("monaco_dettaglio", hero - forward * 1.65f + right * 0.25f + Vector3.up * 1.52f,
                                 hero + Vector3.up * 1.49f, 50f);
            yield return Capture("monaco_laterale", threshold - forward * 4.09f - right * 2.42f + Vector3.up * 1.35f,
                                 threshold + forward * 1.25f + right * 0.34f + Vector3.up * 1.45f, 55f);
            var sanctuary = plan.Find(RoomKind.Presbiterio);
            int retableMeshes = 0;
            if (sanctuary != null)
            {
                Vector3 altar = new Vector3(sanctuary.CenterX, 0f, sanctuary.CenterZ);
                yield return Capture("presbiterio", Vector3.Lerp(naveCenter, altar, 0.46f) + Vector3.up * 1.54f,
                                     altar + Vector3.up * 1.50f, 56f);
                var glass = plan.openings.Find(o => o.a == sanctuary.id && o.style == OpenStyle.Lancetta);
                if (glass != null)
                {
                    float height = Mathf.Lerp(glass.sill, glass.top, 0.5f);
                    var window = glass.alongX ? new Vector3(glass.center * 0.1f, height, glass.line * 0.1f)
                        : new Vector3(glass.line * 0.1f, height, glass.center * 0.1f);
                    var inward = glass.alongX ? Vector3.forward * Mathf.Sign(altar.z - window.z)
                        : Vector3.right * Mathf.Sign(altar.x - window.x);
                    var beside = Vector3.Cross(Vector3.up, inward);
                    float fov = Mathf.Max(52f, 2f * Mathf.Atan((glass.top - glass.sill + 0.5f) / 6.5f) * Mathf.Rad2Deg);
                    yield return Capture("vetrata_dettaglio", window + inward * 3.4f + beside * 0.5f - Vector3.up * 0.36f, window, fov);
                }
                var retable = floor.cur.roomGo[sanctuary.id].transform.Find("Arredi/chi_pala");
                if (retable != null)
                    retableMeshes = System.Array.FindAll(retable.GetComponentsInChildren<MeshFilter>(), mf => mf.sharedMesh != null).Length;
            }
            int pews = 0;
            var props = floor.cur.roomGo[nave.id].transform.Find("Arredi");
            if (props != null)
                foreach (Transform item in props)
                    if (item.name == "chi_panca") pews++;
            var entranceKey = floor.cur.roomGo[entrance.id].transform.Find("Chiarore del nartece")?.GetComponent<Light>();
            int windows = floor.cur.lights.FindAll(light => light != null && light.cookie != null &&
                (light.name == "Luce della vetrata" || light == entranceKey)).Count;
            int bounces = floor.cur.lights.FindAll(light => light != null && light.name == "Rimbalzo della vetrata" && light.intensity > 0f).Count;
            Debug.Log($"[PryingEyes] confronto: {pews} panche in navata, foschia={(floor.cur.roomGo[nave.id].transform.Find("Foschia") != null)}, {windows} vetrate con proiezione, {bounces} rimbalzi");
            Debug.Log($"[PryingEyes] pala scolpita: {retableMeshes} mesh");
            if (entranceKey != null)
                Debug.Log($"[PryingEyes] chiarore del nartece: {entranceKey.transform.position}, intensità {entranceKey.intensity:F1}, ombre {entranceKey.shadows}");
            Application.Quit(CheckMonkFibers(monk) && windows > 0 && bounces > 0 && (sanctuary == null || retableMeshes >= 3) ? 0 : 1);
        }

        static bool CheckMonkFibers(Enemy monk)
        {
            var fibers = System.Array.Find(monk.GetComponentsInChildren<SkinnedMeshRenderer>(), r => r.name == "FiliSfilacciati");
            var animator = monk.GetComponentInChildren<Animator>();
            if (fibers == null || animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("[PryingEyes] fibre o animatore del monaco mancanti");
                return false;
            }
            var mesh = new Mesh();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Play("Fermo", 0, 0.1f);
            animator.Update(0f);
            fibers.BakeMesh(mesh);
            var idle = mesh.vertices;
            animator.Play("Cammina", 0, 0.37f);
            animator.Update(0f);
            fibers.BakeMesh(mesh);
            var walking = mesh.vertices;
            Destroy(mesh);
            float displacement = 0f;
            for (int i = 0; i < idle.Length; i++) displacement = Mathf.Max(displacement, (walking[i] - idle[i]).sqrMagnitude);
            int clips = animator.runtimeAnimatorController.animationClips.Length;
            Debug.Log($"[PryingEyes] fibre: {idle.Length} vertici, {clips} clip, spostamento fra pose {Mathf.Sqrt(displacement):F3} m");
            return clips == 5 && displacement > 0.000025f;
        }

        IEnumerator CheckAliens(Vector3 start, Vector3 dir)
        {
            var volume = FindAnyObjectByType<UnityEngine.Rendering.Volume>();
            if (volume == null || !volume.profile.TryGet(out ColorAdjustments grading))
            {
                Log("alieni: profilo di luminosità mancante");
                Application.Quit(1);
                yield break;
            }
            float previous = GameSettings.Brightness;
            bool hadSetting = PlayerPrefs.HasKey("video_brightness");
            GameSettings.SetBrightness(0f);
            float low = grading.postExposure.value;
            yield return null;
            GameManager.CamShot(Dir + "/luce_minima.png", null, false);
            GameSettings.SetBrightness(1f);
            float high = grading.postExposure.value;
            yield return null;
            GameManager.CamShot(Dir + "/luce_massima.png", null, false);
            GameSettings.SetBrightness(previous);
            if (!hadSetting) PlayerPrefs.DeleteKey("video_brightness");
            yield return null;
            GameManager.CamShot(Dir + "/luce_normale.png", null, false);
            bool passed = high > low + 2f && RenderSettings.fog && RenderSettings.fogMode == FogMode.Exponential;
            Log($"luminosità: esposizione {low:F2} → {high:F2}, nebbia={RenderSettings.fogMode}");

            Enemy.debugNoSense = true;
            Torch.I.Toggle();
            foreach (var type in new[] { Enemy.EnemyType.Falena, Enemy.EnemyType.Medusa, Enemy.EnemyType.Carapace })
            {
                var pos = start + dir * 3.3f + Vector3.up * (Enemy.IsFlying(type) ? 1.5f : 0f);
                var alien = Enemy.Spawn(type, pos);
                alien.transform.rotation = Quaternion.LookRotation(-dir);
                yield return new WaitForSeconds(0.45f);
                bool visible = alien.GetComponentInChildren<Renderer>() != null;
                bool weakSpot = System.Array.Exists(alien.GetComponentsInChildren<Collider>(), c => !c.isTrigger && c.CompareTag("Head"));
                var agent = alien.GetComponent<NavMeshAgent>();
                bool navigation = Enemy.IsFlying(type) ? agent == null : agent != null && agent.isOnNavMesh;
                GameManager.CamShot(Dir + "/alieno_" + type + ".png", null, false);
                alien.Damage(alien.hp + 1f, alien.AimPoint, false, false);
                Log($"{type}: modello={visible}, punto debole={weakSpot}, navigazione={navigation}, eliminato={alien.IsDead}");
                passed &= visible && weakSpot && navigation && alien.IsDead;
                Destroy(alien.gameObject);
            }
            Log(passed ? "alieni: OK" : "alieni: ERRORE");
            Application.Quit(passed ? 0 : 1);
        }

        /// <summary>-temitest: i quattro nemici di tema nel piano vero (foto con la torcia a 3-4 m), poi i loro colpi di scena:
        /// la Maschera soffia, l'Infermiere sedativo (gambe a 55%), il Primario chiama rinforzi, il Direttore lancia tre dardi.</summary>
        IEnumerator CheckThemed(Vector3 start, Vector3 dir)
        {
            var pl = GameManager.I.player;
            bool passed = true;
            Enemy.debugNoSense = true;
            if (!Torch.I.on) Torch.I.Toggle();
            var types = new[] { Enemy.EnemyType.Maschera, Enemy.EnemyType.Direttore, Enemy.EnemyType.Infermiere, Enemy.EnemyType.Primario };
            // la scena di prova parte in fondo alla sala e nel teatro li' c'e' il sipario (Tenda, nero e senza collider): qualche metro
            // piu' avanti la camera vede la sala e non la stoffa
            if (NavMesh.SamplePosition(start + dir * 2.8f, out NavMeshHit sh, 2f, NavMesh.AllAreas)) start = sh.position;
            pl.Teleport(start);
            yield return new WaitForSeconds(0.5f);
            Log($"torcia: accesa={Torch.I.on} luce={Torch.I.Lit} fascio={(Torch.I.Beam != null && Torch.I.Beam.enabled)}");
            foreach (var type in types)
            {
                var e = Enemy.Spawn(type, start + dir * (type == Enemy.EnemyType.Primario ? 4.4f : 3.6f));
                e.transform.rotation = Quaternion.LookRotation(-dir);
                yield return new WaitForSeconds(0.5f);
                bool visible = e.GetComponentInChildren<Renderer>() != null;
                bool weakSpot = System.Array.Exists(e.GetComponentsInChildren<Collider>(), c => !c.isTrigger && c.CompareTag("Head"));
                var agent = e.GetComponent<NavMeshAgent>();
                bool navigation = agent != null && agent.isOnNavMesh;
                int rends = e.GetComponentsInChildren<Renderer>().Length, lights = e.GetComponentsInChildren<Light>().Length;
                GameManager.CamShot(Dir + "/tema_" + type + ".png", null, false);
                e.transform.rotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(0f, 50f, 0f);      // di tre quarti: si vedono gli accessori
                yield return new WaitForSeconds(0.2f);
                GameManager.CamShot(Dir + "/tema_" + type + "_3q.png", null, false);
                e.Damage(e.hp + 1f, e.AimPoint, false, false);
                Log($"{type}: modello={visible} ({rends} renderer, {lights} luci), punto debole={weakSpot}, navigazione={navigation}, hp={e.maxHp}, eliminato={e.IsDead}");
                passed &= visible && weakSpot && navigation && e.IsDead;
                Destroy(e.gameObject);                  // il cadavere resterebbe sul posto (fuori da Enemy.All) e il prossimo ci nascerebbe sopra
                yield return null;
            }

            Enemy.ClearAll();
            Enemy.debugNoSense = false;
            Enemy.debugFreeze = false;
            pl.invulnerable = true;
            float t0;
            Enemy Face(Enemy.EnemyType type, float dist)
            {
                var e = Enemy.Spawn(type, start + dir * dist);
                e.transform.rotation = Quaternion.LookRotation(-dir);
                return e;
            }

            // la Maschera ti vede e soffia il fischietto
            int w0 = Enemy.statWhistles;
            var m = Face(Enemy.EnemyType.Maschera, 9f);
            t0 = Time.time;
            while (Time.time - t0 < 9f && Enemy.statWhistles == w0) yield return null;
            bool whistled = Enemy.statWhistles > w0;
            Log($"Maschera: fischietto={whistled} dopo {Time.time - t0:F1} s");
            passed &= whistled;

            // l'Infermiere ti raggiunge e la puntura ti addormenta le gambe (Sedate non fa nulla a un giocatore invulnerabile:
            // qui il giocatore e' vulnerabile per il tempo della prova, ma le punture sono 10 pf l'una)
            Enemy.ClearAll();
            yield return null;
            pl.sedT = 0f;
            pl.invulnerable = false;
            var n = Face(Enemy.EnemyType.Infermiere, 2.0f);
            t0 = Time.time;
            while (Time.time - t0 < 9f && pl.sedT <= 0f) yield return null;
            bool sedated = pl.sedT > 0f;
            Log($"Infermiere: sedativo={sedated} dopo {Time.time - t0:F1} s (sedT={pl.sedT:F1})");
            pl.invulnerable = true;
            passed &= sedated;

            // il Primario ti vede e chiama due infermieri (arrivano dopo qualche secondo)
            Enemy.ClearAll();
            yield return null;
            int h0 = Enemy.statHelps;
            var p = Face(Enemy.EnemyType.Primario, 9f);
            t0 = Time.time;
            while (Time.time - t0 < 9f && Enemy.statHelps == h0) yield return null;
            bool called = Enemy.statHelps > h0;
            int before = Enemy.All.Count;
            yield return new WaitForSeconds(6f);
            int after = Enemy.All.Count;
            Log($"Primario: richiamo={called}, nemici {before} -> {after}");
            passed &= called && after > before;

            // il Direttore carica e lancia un ventaglio di tre dardi
            Enemy.ClearAll();
            yield return null;
            int b0 = Enemy.statBolts;
            var d = Face(Enemy.EnemyType.Direttore, 11f);
            t0 = Time.time;
            while (Time.time - t0 < 12f && Enemy.statBolts - b0 < 3) yield return null;
            int bolts = Enemy.statBolts - b0;
            Log($"Direttore: dardi={bolts} dopo {Time.time - t0:F1} s");
            passed &= bolts >= 3;

            Log(passed ? "temi: OK" : "temi: ERRORE");
            Application.Quit(passed ? 0 : 1);
        }

        // ------------------------------------------------------------------ round (-roundtest)
        static Vector3 NavPoint(Vector3 p) => NavMesh.SamplePosition(p, out NavMeshHit h, 2f, NavMesh.AllAreas) ? h.position : p;
        static Vector3 DoorPoint(Opening o) => o.alongX ? new Vector3(o.center * 0.1f, 0f, o.line * 0.1f) : new Vector3(o.line * 0.1f, 0f, o.center * 0.1f);

        /// <summary>Verso l'interno della zona, attraverso l'apertura (orizzontale, unitario).</summary>
        static Vector3 InwardOf(FloorInstance fi, RoundRegion reg, Opening o)
        {
            var room = fi.plan.rooms[reg.Has(o.a) ? o.a : o.b];
            float s = o.alongX ? (room.CenterZ > o.line * 0.1f ? 1f : -1f) : (room.CenterX > o.line * 0.1f ? 1f : -1f);
            return o.alongX ? new Vector3(0f, 0f, s) : new Vector3(s, 0f, 0f);
        }

        sealed class RoundRun
        {
            public int maxAlive, outsiderIn, seenTarget;
            public float minDoor = 99f, minPlayer = 99f, startPlayer, took;
            public bool allInside = true;
        }

        /// <summary>Porta avanti un round gia' partito: uccide i nemici del round a ondate (li lascia accumulare fino al tetto o fino
        /// all'ultimo nato) e misura il massimo di vivi insieme, le nascite fuori dalla zona e, per il nemico di fuori, quanto si avvicina
        /// al giocatore (e alle soglie) e se riesce a entrare. Con la strada chiusa il nemico va al punto della navmesh piu' vicino al
        /// giocatore, che non e' per forza una porta: per questo si misura la distanza dal giocatore e non da una soglia sola.</summary>
        IEnumerator DriveRound(RoundDirector rd, int cap, Enemy outsider, List<Vector3> doors, PlayerController pl, RoundRun run,
                               string openShot = null, Vector3 aimAt = default)
        {
            Transform player = pl.transform;
            float tStart = Time.time;
            run.seenTarget = rd.Target;
            if (outsider != null) run.startPlayer = Vector3.Distance(outsider.transform.position, player.position);
            bool shotDone = openShot == null;
            if (!shotDone) { Vector3 d0 = aimAt - player.position; pl.yaw = Mathf.Atan2(d0.x, d0.z); pl.pitch = -0.05f; }
            while (rd.Sealed && Time.time - tStart < 170f)
            {
                run.maxAlive = Mathf.Max(run.maxAlive, rd.Alive);
                foreach (var e in Enemy.All)
                {
                    if (e == null || e.IsDead) continue;
                    if (e == outsider)
                    {
                        Vector3 ep = e.transform.position;
                        if (rd.InsideSealed(ep)) run.outsiderIn++;
                        foreach (var d in doors) run.minDoor = Mathf.Min(run.minDoor, Vector3.Distance(ep, d));
                        run.minPlayer = Mathf.Min(run.minPlayer, Vector3.Distance(ep, player.position));
                    }
                    else if (e.relentless && !rd.InsideSealed(e.transform.position)) run.allInside = false;
                }
                if (rd.Fighting && rd.Clock > 8f && (rd.Alive >= cap || (rd.Spawned >= rd.Target && rd.Alive > 0)))
                    foreach (var e in new List<Enemy>(Enemy.All))
                        if (e != null && !e.IsDead && e.relentless && e != outsider) e.Damage(100000f, e.AimPoint, false, true);
                if (!shotDone && rd.Sealed && !rd.Fighting)
                {
                    shotDone = true;
                    yield return new WaitForSeconds(0.4f);        // le serrande stanno salendo e il banner "Round N superato" e' in vista
                    Shot(openShot);
                }
                yield return new WaitForSeconds(0.25f);
            }
            run.took = Time.time - tStart;
        }

        /// <summary>Prova dei round: PryingEyes.exe -batchmode -nographics -autotest -stealthtest -roundtest -seed N -floor N (senza -nographics
        /// per le foto). Sceglie le zone sigillabili del piano e prova, nell'ordine: un round annullato dalla morte; un round intero con un
        /// nemico fuori che preme sulle serrande (soglie nella navmesh, serrande che fermano vista, colpi e percorsi, nascite solo dentro la
        /// zona, tetto dei vivi, premio, serrande su, il nemico di fuori entra a serrande alzate); un secondo round in un'altra zona (numero
        /// che continua, tono calmo, formula dei nemici, bonus); un round scaduto (tempo massimo ridotto: si chiude da solo, senza premio).
        /// Esito in hotel_build/furtivo/round.txt; uscita 0 ok, 1 fallito, 2 piano senza zone.</summary>
        IEnumerator CheckRounds()
        {
            var gm = GameManager.I;
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            var pl = gm.player;
            bool passed = true;
            void Check(string what, bool ok, string detail = "")
            {
                Log((ok ? "ok       " : "FALLITO  ") + what + (detail.Length > 0 ? "   [" + detail + "]" : ""));
                if (!ok) passed = false;
            }
            bool shots = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            yield return new WaitForSeconds(2.0f);
            Log($"piano {fd.floor} seme {fd.runSeed}: stanze={fd.cur.plan.rooms.Count} candele={fd.cur.candles.Count} zone={(rd != null ? rd.ZoneCount : -1)}");
            Check("RoundDirector presente e attivo", rd != null && RoundDirector.Enabled);
            if (rd == null) { Application.Quit(1); yield break; }
            Drops.disabled = true;           // le uccisioni di questa prova non devono far cadere potenziamenti a caso
            var cands = fd.cur.candles.FindAll(c => c != null && rd.HasZone(c));
            if (cands.Count == 0) { Log("nessuna zona sigillabile su questo piano: prova non applicabile (cambia seme)"); Application.Quit(2); yield break; }

            Enemy.ClearAll();
            PlayerController.holdLook = true;
            pl.invulnerable = true;
            Enemy.debugFreeze = false;
            var candle = cands[0];
            var reg = rd.RegionOf(candle);
            Log($"zone con candela: {cands.Count}; prima scelta: {reg}");
            int cap = RoundRules.AliveCap(reg.area);

            // il giocatore dentro la zona di una candela, vicino a lei, che la guarda
            void PlaceAt(Candelabro c)
            {
                Vector3 cp = c.transform.position;
                var room = fd.cur.RoomAt(cp);
                Vector3 toCenter = new Vector3(room.CenterX - cp.x, 0f, room.CenterZ - cp.z);
                Vector3 pos = NavPoint(cp + (toCenter.sqrMagnitude > 0.5f ? toCenter.normalized : Vector3.right) * 1.3f);
                Vector3 look = cp - pos;
                pl.Teleport(pos);
                pl.yaw = Mathf.Atan2(look.x, look.z);
                pl.pitch = 0f;
            }

            // ----- soglie: ogni apertura di taglio ha la sua area nella navmesh
            foreach (var o in reg.cuts)
            {
                int area = RoundDirector.AreaOf(o);
                bool marked = area >= HotelNav.FirstDoorArea && NavMesh.SamplePosition(DoorPoint(o) + Vector3.up * 0.1f, out NavMeshHit mh, 1.2f, 1 << area);
                Check("soglia marcata nella navmesh", marked, $"area {area}, apertura {o.style} larga {o.width * 0.1f:F1} m");
            }
            PlaceAt(candle);
            yield return new WaitForSeconds(0.5f);

            // ----- 1) il round parte e il giocatore muore a meta': il round salta
            Log("--- round annullato dalla morte");
            int round0 = RoundDirector.Round;
            candle.Use();
            yield return null;
            Check("la candela fa partire il round", rd.Fighting && RoundDirector.Round == round0 + 1, $"round {RoundDirector.Round}, tono {rd.Mood}, {rd.Target} nemici, massimo {cap} insieme");
            Check("HUD: numero del round", gm.hud.CurrentRound == RoundDirector.Round.ToString(), "'" + gm.hud.CurrentRound + "'");
            Check("navmesh sigillata (maschera dei nemici ridotta)", HotelNav.EnemyMask != NavMesh.AllAreas);
            yield return new WaitForSeconds(1.2f);
            bool allDown = true;
            foreach (var s in rd.Shutters) if (!s.IsDown) allDown = false;
            Check("serrande calate su tutte le aperture di taglio", rd.Shutters.Count == reg.cuts.Count && allDown, $"{rd.Shutters.Count}/{reg.cuts.Count}");
            foreach (var o in reg.cuts)
            {
                Vector3 dp = DoorPoint(o), n = InwardOf(fd.cur, reg, o);
                Vector3 inside = NavPoint(dp + n * 2.0f), outside = NavPoint(dp - n * 2.0f);
                bool blocked = Physics.Linecast(outside + Vector3.up * 1.2f, inside + Vector3.up * 1.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
                var path = new NavMeshPath();
                bool pathAll = NavMesh.CalculatePath(outside, inside, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                bool pathOpen = NavMesh.CalculatePath(outside, inside, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
                Check("serranda: ferma vista e colpi", blocked, o.style.ToString());
                Check("navmesh: tra fuori e dentro c'e' strada solo con le serrande alzate", pathAll && !pathOpen, $"tutte le aree {pathAll}, aree aperte {pathOpen}");
            }
            if (shots)
            {
                // foto: la prima serranda vista da dentro, a due metri e mezzo
                var o0 = reg.cuts[0];
                Vector3 dp0 = DoorPoint(o0), n0 = InwardOf(fd.cur, reg, o0);
                pl.Teleport(NavPoint(dp0 + n0 * 2.6f));
                pl.yaw = Mathf.Atan2(-n0.x, -n0.z); pl.pitch = -0.05f;
                yield return new WaitForSeconds(0.8f);
                Shot("round_serranda");
                PlaceAt(candle);
            }
            float t0 = Time.time;
            while (rd.Spawned < 1 && Time.time - t0 < 20f) yield return null;
            bool inZone = rd.Spawned >= 1;
            Enemy first = null;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.IsDead || !e.relentless) continue;
                if (first == null) first = e;
                if (!rd.InsideSealed(e.transform.position)) inZone = false;
            }
            Check("il primo nemico nasce dentro la zona", inZone, $"nati {rd.Spawned}, dopo {Time.time - t0:F1} s");
            if (shots)
            {
                // foto: il lampo della nascita (dura 0,7 s), guardando il punto; due fotogrammi del solo mondo per vedere come si spegne
                var flare = GameObject.Find("LampoRound");
                var fl = flare != null ? flare.GetComponent<Light>() : null;
                Log(fl != null ? $"lampo: acceso={fl.isActiveAndEnabled} intensita {fl.intensity:F1} raggio {fl.range:F1}, a {Vector3.Distance(flare.transform.position, pl.transform.position):F1} m dal giocatore"
                               : "lampo: assente");
                if (first != null) { Vector3 d = first.transform.position + Vector3.up - pl.transform.position; pl.yaw = Mathf.Atan2(d.x, d.z); pl.pitch = 0f; }
                yield return new WaitForSeconds(0.05f);
                GameManager.CamShot(Dir + "/round_lampo_a.png", null, false);
                yield return new WaitForSeconds(0.12f);
                GameManager.CamShot(Dir + "/round_lampo_b.png", null, false);
                Shot("round_nascita");
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-roundshots") >= 0) { Log("-roundshots: foto fatte, esco"); Application.Quit(0); yield break; }
            }
            pl.invulnerable = false;
            pl.Hurt(10000f);
            yield return new WaitForSeconds(0.6f);
            int relentlessLeft = 0;
            foreach (var e in Enemy.All) if (e != null && e.relentless) relentlessLeft++;
            Check("morte: il round salta, serrande tolte", !rd.Sealed && fd.cur.root.GetComponentsInChildren<Shutter>().Length == 0);
            Check("morte: navmesh libera, candela di nuovo spenta, numero del round restituito",
                  HotelNav.EnemyMask == NavMesh.AllAreas && !candle.lit && RoundDirector.Round == round0, $"round {RoundDirector.Round}");
            Check("morte: i nemici del round non ci sono piu'", relentlessLeft == 0, relentlessLeft + " rimasti");
            while (!pl.alive) yield return null;
            yield return new WaitForSeconds(3.2f);          // la tregua dopo la rinascita finisce da sola

            // ----- 2) round intero, con un nemico fuori che preme sulle serrande
            Log("--- round intero");
            Enemy.ClearAll();
            pl.invulnerable = true;
            PlaceAt(candle);
            var oa = reg.cuts[0];
            Vector3 dpa = DoorPoint(oa), na = InwardOf(fd.cur, reg, oa);
            var outsider = Enemy.Spawn(Enemy.EnemyType.Monaco, NavPoint(dpa - na * 5.5f));
            outsider.MakeRelentless();
            yield return new WaitForSeconds(0.5f);
            int score0 = gm.score;
            candle.Use();
            yield return null;
            Check("secondo avvio del round", rd.Fighting && RoundDirector.Round == round0 + 1, $"tono {rd.Mood}, {rd.Target} nemici");
            var run = new RoundRun();
            var doorPts = new List<Vector3>();
            foreach (var o in reg.cuts) doorPts.Add(DoorPoint(o));
            yield return DriveRound(rd, cap, outsider, doorPts, pl, run, shots ? "round_fine" : null, dpa + Vector3.up * 1.2f);
            Check("il round finisce da solo, serrande su", !rd.Sealed, $"{run.took:F0} s, nati {rd.Spawned}/{run.seenTarget}");
            Check("tutti i nemici del round nascono dentro la zona", run.allInside);
            Check("mai piu' vivi insieme del tetto", run.maxAlive <= cap, $"massimo {run.maxAlive}, tetto {cap}");
            Check("il nemico fuori non entra a serrande calate", run.outsiderIn == 0, $"entrato per {run.outsiderIn} campionamenti");
            // (informazione, non verifica: con la strada chiusa il nemico va al punto della navmesh piu' vicino al giocatore, che non e' per
            // forza una porta; la prova che non e' fermo a caso e' che a serrande alzate entra subito, qui sotto)
            Log($"info: il nemico fuori, dal giocatore {run.startPlayer:F1} -> {run.minPlayer:F1} m; soglia piu' vicina {run.minDoor:F1} m");
            Check("premio del round", gm.score - score0 >= RoundRules.Reward(RoundDirector.Round), $"+{gm.score - score0} punti");
            Check("candela contata, numero del round", candle.lit && RoundDirector.Round == round0 + 1, $"round {RoundDirector.Round}");
            Check("HUD: numero del round", gm.hud.CurrentRound == RoundDirector.Round.ToString());
            Check("navmesh libera", HotelNav.EnemyMask == NavMesh.AllAreas);
            if (fd.cur.candles.Count == 1) Check("ultima candela: l'uscita si apre", fd.ExitOpen);
            float tIn = Time.time;
            bool entered = false;
            while (Time.time - tIn < 25f && !entered)
            {
                var rr = outsider != null ? fd.cur.RoomAt(outsider.transform.position) : null;
                entered = rr != null && reg.Has(rr.id);
                yield return new WaitForSeconds(0.25f);
            }
            Check("a serrande alzate il nemico di fuori entra", entered, $"{Time.time - tIn:F1} s");

            // ----- 3) un'altra zona: il numero continua, nessuno ti cerca (tono calmo), i nemici seguono la formula, c'e' il bonus
            if (cands.Count >= 2)
            {
                Log("--- secondo round in un'altra zona");
                var candleB = cands[1];
                var regB = rd.RegionOf(candleB);
                int capB = RoundRules.AliveCap(regB.area);
                Enemy.ClearAll();
                pl.invulnerable = true;
                PlaceAt(candleB);
                yield return new WaitForSeconds(1.0f);       // la percezione dei preti si aggiorna: nessuno e' piu' in allarme
                int scoreB = gm.score;
                candleB.Use();
                yield return null;
                float mulB = rd.Mood == RoundMood.Calm ? RoundRules.CalmMul : rd.Mood == RoundMood.Alarm ? RoundRules.AlarmMul : 1f;
                int adoptedB = rd.Alive;                     // chi era gia' dentro la zona entra nel round e conta nel totale
                int expectB = Mathf.Max(2, Mathf.RoundToInt(RoundRules.Count(RoundDirector.Round, fd.floor) * mulB) - adoptedB);
                Check("secondo round: il numero continua", rd.Fighting && RoundDirector.Round == round0 + 2, $"round {RoundDirector.Round}, tono {rd.Mood}");
                Check("secondo round: tono calmo (nessuno ti cercava)", rd.Mood == RoundMood.Calm, rd.Mood.ToString());
                Check("secondo round: nemici secondo la formula", rd.Target == expectB, $"{rd.Target} nemici, attesi {expectB} ({adoptedB} gia' dentro); primo round {run.seenTarget}");
                Check("HUD: numero del round aggiornato", gm.hud.CurrentRound == (round0 + 2).ToString(), "'" + gm.hud.CurrentRound + "'");
                var runB = new RoundRun();
                yield return DriveRound(rd, capB, null, new List<Vector3>(), pl, runB);
                Check("secondo round: finisce, serrande su", !rd.Sealed, $"{runB.took:F0} s, nati {rd.Spawned}/{runB.seenTarget}");
                Check("secondo round: nascite solo dentro, tetto rispettato", runB.allInside && runB.maxAlive <= capB, $"massimo {runB.maxAlive}, tetto {capB}");
                int bonusB = RoundRules.Reward(round0 + 2) + (rd.Mood == RoundMood.Calm ? RoundRules.CalmBonus : 0);
                Check("secondo round: premio con il bonus del tono", gm.score - scoreB >= bonusB, $"+{gm.score - scoreB} punti, almeno {bonusB}");
            }

            // ----- 4) round scaduto: il tempo massimo (ridotto) finisce prima dei nemici
            if (cands.Count >= 3)
            {
                Log("--- round scaduto");
                var candleC = cands[2];
                Enemy.ClearAll();
                pl.invulnerable = true;
                PlaceAt(candleC);
                yield return new WaitForSeconds(0.6f);
                int scoreC = gm.score;
                // la candela vale 60 e, se nessun prete ti ha visto dall'ultima, anche il bonus Fantasma (FloorDirector.OnCandle)
                int ghostC = Stealth.Spotted == fd.GhostMark && Stealth.Hunters == 0 ? Punti.Fantasma : 0;
                float oldFs = RoundRules.Failsafe;
                RoundRules.Failsafe = 10f;
                int roundC = RoundDirector.Round + 1;
                candleC.Use();
                yield return null;
                float tC = Time.time;
                while (rd.Sealed && Time.time - tC < 40f) yield return new WaitForSeconds(0.25f);
                RoundRules.Failsafe = oldFs;
                int left = 0;
                foreach (var e in Enemy.All) if (e != null && !e.IsDead && e.relentless) left++;
                Check("round scaduto: si chiude da solo", !rd.Sealed && RoundDirector.Round == roundC && candleC.lit, $"{Time.time - tC:F0} s, round {RoundDirector.Round}");
                Check("round scaduto: nessun premio", gm.score - scoreC - ghostC < RoundRules.Reward(roundC), $"+{gm.score - scoreC} punti (la candela ne da' 60, il Fantasma {ghostC})");
                Check("round scaduto: nessun nemico del round rimasto", left == 0, left + " rimasti");
            }
            else Log("meno di tre zone su questo piano: round scaduto non provato");

            Log(passed ? "ESITO: ok" : "ESITO: FALLITO");
            Application.Quit(passed ? 0 : 1);
        }

        bool CheckExposure()
        {
            var origin = Vector3.up * 100f;
            var lamp = new GameObject("Faretto di prova");
            lamp.transform.position = origin;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 10f;
            light.spotAngle = 60f;
            light.intensity = 10f;
            LightPool.Register(light, true);
            var front = origin + Vector3.forward * 3f;
            float lit = LightPool.Exposure(front);
            float behind = LightPool.Exposure(origin - Vector3.forward * 3f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = origin + Vector3.forward * 1.5f;
            Physics.SyncTransforms();
            float occluded = LightPool.Exposure(front);
            Destroy(wall);
            Destroy(lamp);
            Log($"faretto: nel cono={lit:F2} fuori cono={behind:F2} dietro muro={occluded:F2}");
            return lit > 0f && behind == 0f && occluded == 0f;
        }

        /// <summary>Tre file: il gioco con l'HUD cosi' come lo vede la camera (dentro la foto l'HUD passa dal post-processing e viene scuro),
        /// il solo mondo e il solo HUD su fondo nero (resa fedele, come in gioco): si compongono in Python.</summary>
        static void Shot(string name)
        {
            GameManager.CamShot(Dir + "/" + name + ".png", null, true);
            GameManager.CamShot(Dir + "/" + name + "_mondo.png", null, false);
            if (hudCam == null)
            {
                var go = new GameObject("HudCam");
                go.transform.position = new Vector3(0f, 800f, 0f);
                hudCam = go.AddComponent<Camera>();
                hudCam.clearFlags = CameraClearFlags.SolidColor;
                hudCam.backgroundColor = new Color(0f, 0f, 0f, 1f);
                hudCam.nearClipPlane = 0.01f;
                hudCam.farClipPlane = 1f;
                hudCam.enabled = false;
                var d = hudCam.GetUniversalAdditionalCameraData();
                d.renderPostProcessing = false;
                d.antialiasing = AntialiasingMode.None;
            }
            GameManager.CamShot(Dir + "/" + name + "_hud.png", hudCam, true);
        }

        /// <summary>Cerca buchi nel pavimento del piano: raggi verso il basso su una griglia di 40 cm dentro ogni stanza (tranne gli ascensori,
        /// dove il pavimento e' la cabina) e lungo ogni apertura, attraverso lo spessore del muro. Un punto e' un buco se sotto non c'e' niente
        /// o se il primo appoggio e' a piu' di 25 cm sotto il pavimento. PryingEyes.exe -batchmode -nographics -autotest -stealthtest
        /// -holescan -seed N -floor N; esito in hotel_build/furtivo/buchi.txt, uscita 0 senza buchi, 1 con buchi.</summary>
        IEnumerator CheckHoles()
        {
            var fd = FloorDirector.I;
            yield return new WaitForSeconds(1.5f);
            var plan = fd.cur.plan;
            Log($"piano {fd.floor} seme {fd.runSeed}: {plan.rooms.Count} stanze, {plan.openings.Count} aperture");
            int samples = 0, holes = 0;
            var perRoom = new Dictionary<int, int>();
            var shown = new List<string>();
            bool Probe(float x, float z, string what)
            {
                samples++;
                bool ground = Physics.Raycast(new Vector3(x, 1.2f, z), Vector3.down, out RaycastHit h, 5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && h.point.y > -0.25f;
                if (ground) return false;
                holes++;
                if (shown.Count < 40) shown.Add($"({x:F1},{z:F1}) {what}");
                return true;
            }
            foreach (var r in plan.rooms)
            {
                if (r.IsLift) continue;
                int bad = 0;
                for (float x = r.x0 * 0.1f + 0.35f; x < r.x1 * 0.1f - 0.34f; x += 0.4f)
                    for (float z = r.z0 * 0.1f + 0.35f; z < r.z1 * 0.1f - 0.34f; z += 0.4f)
                        if (Probe(x, z, $"stanza {r.id} {r.kind}")) bad++;
                if (bad > 0) perRoom[r.id] = bad;
            }
            foreach (var o in plan.openings)
            {
                if (o.a < 0 || o.b < 0 || o.sill >= 0.5f) continue;         // finestre e aperture sull'esterno: non si cammina
                var ra = plan.rooms[o.a];
                var rb = plan.rooms[o.b];
                if (ra.IsLift || rb.IsLift) continue;
                for (float al = o.center * 0.1f - o.width * 0.05f + 0.2f; al <= o.center * 0.1f + o.width * 0.05f - 0.19f; al += 0.4f)
                    for (float ac = o.line * 0.1f - 0.6f; ac <= o.line * 0.1f + 0.61f; ac += 0.2f)
                        Probe(o.alongX ? al : ac, o.alongX ? ac : al, $"apertura {o.style} tra {o.a} e {o.b}");
            }
            // -holeat X,Z: cosa c'e' in quel punto (stanza, appoggio sotto i piedi, collider intorno)
            var cl = System.Environment.GetCommandLineArgs();
            int ai = System.Array.IndexOf(cl, "-holeat");
            if (ai >= 0 && ai + 1 < cl.Length)
            {
                var xz = cl[ai + 1].Split(',');
                float hx = float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), hz = float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture);
                var rm = fd.cur.RoomAt(new Vector3(hx, 0.5f, hz));
                Log($"punto ({hx:F1},{hz:F1}): stanza {(rm != null ? rm.id + " " + rm.kind + $" [{rm.x0 * 0.1f:F1}..{rm.x1 * 0.1f:F1}]x[{rm.z0 * 0.1f:F1}..{rm.z1 * 0.1f:F1}]" : "nessuna")}");
                for (float dz = -1.0f; dz <= 1.01f; dz += 0.5f)
                {
                    string row = "";
                    for (float dx = -1.0f; dx <= 1.01f; dx += 0.5f)
                    {
                        bool g = Physics.Raycast(new Vector3(hx + dx, 1.2f, hz + dz), Vector3.down, out RaycastHit gh, 5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
                        row += g ? $" {gh.point.y,5:F2}" : "  ----";
                    }
                    Log($"  z={hz + dz:F1}: {row}");
                }
                foreach (var col in Physics.OverlapSphere(new Vector3(hx, 0.3f, hz), 1.6f, ~0, QueryTriggerInteraction.Collide))
                    Log($"  collider {col.name} ({col.GetType().Name}, strato {col.gameObject.layer}, trigger={col.isTrigger}) in {(col.transform.parent != null ? col.transform.parent.name : "-")}, limiti y {col.bounds.min.y:F2}..{col.bounds.max.y:F2}");
            }
            foreach (var s in shown) Log("buco " + s);
            foreach (var kv in perRoom) Log($"stanza {kv.Key} ({plan.rooms[kv.Key].kind}): {kv.Value} punti senza pavimento");
            Log($"punti provati {samples}, senza pavimento {holes}");

            // rete di sicurezza (FloorDirector.FallGuard): un giocatore buttato nel vuoto torna sul pavimento
            var plr = GameManager.I.player;
            bool guardOk = true;
            if (fd.cur.arrival != null)
            {
                Vector3 sp = fd.cur.arrival.spawnPoint;
                plr.invulnerable = true;
                plr.Teleport(sp);
                yield return new WaitForSeconds(1.2f);                      // il tempo di posarsi e di essere ricordato come ultimo appoggio
                plr.Teleport(new Vector3(sp.x, -60f, sp.z));
                yield return new WaitForSeconds(1.0f);
                float back = Vector3.Distance(plr.transform.position, sp);
                guardOk = plr.transform.position.y > -1f && back < 3f;
                Log($"caduta: dopo il tuffo a -60 m il giocatore e' a y={plr.transform.position.y:F1}, a {back:F1} m dall'appoggio -> {(guardOk ? "ok" : "FALLITO")}");
            }
            bool allOk = holes == 0 && guardOk;
            Log(allOk ? "ESITO: ok" : "ESITO: FALLITO");
            Application.Quit(allOk ? 0 : 1);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            bool holeScan = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-holescan") >= 0;
            bool alienTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-alientest") >= 0;
            bool themeTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-temitest") >= 0;
            bool reference = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-refshot") >= 0;
            bool roundTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-roundtest") >= 0;
            bool knifeTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-knifetest") >= 0;
            bool dropTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-droptest") >= 0;
            var gm = GameManager.I;
            while (FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) yield return null;
            if (reference)
            {
                yield return ReferenceShots(FloorDirector.I);
                yield break;
            }
            log = Dir + (alienTest ? "/alieni.txt" : themeTest ? "/temi.txt" : roundTest ? "/round.txt" : knifeTest ? "/coltello.txt" : dropTest ? "/drop.txt" : holeScan ? "/buchi.txt" : "/furtivo.txt");
            File.WriteAllText(log, "");
            if (roundTest) { yield return CheckRounds(); yield break; }
            if (knifeTest) { yield return CheckKnife(); yield break; }
            if (dropTest) { yield return CheckDrops(); yield break; }
            if (holeScan) { yield return CheckHoles(); yield break; }
            yield return new WaitForSeconds(2.0f);
            var fd = FloorDirector.I;
            var pl = gm.player;
            var torch = Torch.I;
            Log($"piano {fd.floor}: stanze={fd.cur.plan.rooms.Count} candele={fd.cur.candles.Count} luci={LightPool.Count} preti={Enemy.All.Count}");
            int piles = fd.cur.root.GetComponentsInChildren<Pila>().Length;
            Log($"pile sul piano: {piles}");
            bool passed = piles > 0 && CheckExposure();

            // ----- scena di prova: il corridoio o la sala piu' lunga, il giocatore a un'estremita' che guarda lungo l'asse
            Enemy.ClearAll();
            PlayerController.holdLook = true;
            pl.invulnerable = true;
            Vector3 start = Vector3.zero, dir = Vector3.forward;
            float bestLen = 0f;
            foreach (var r in fd.cur.plan.rooms)
            {
                if (r.IsLift) continue;
                bool alongX = r.W >= r.D;
                float len = (alongX ? r.W : r.D) * 0.1f;
                if (len <= bestLen) continue;
                Vector3 d = alongX ? Vector3.right : Vector3.forward;
                Vector3 s = new Vector3(r.CenterX, 0f, r.CenterZ) - d * (len * 0.5f - 1.4f);
                if (!NavMesh.SamplePosition(s, out NavMeshHit nh, 2f, NavMesh.AllAreas)) continue;
                s = nh.position;
                if (Physics.Raycast(s + Vector3.up * 1.4f, d, out RaycastHit rh, 40f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && rh.distance < 14f) continue;
                bestLen = len; start = s; dir = d;
            }
            Log($"scena di prova: lunghezza {bestLen:F1} m, da ({start.x:F1},{start.z:F1}) verso ({dir.x:F0},{dir.z:F0})");
            if (bestLen < 14f) { Log("nessuna stanza abbastanza lunga: prova interrotta"); Application.Quit(1); yield break; }
            pl.Teleport(start);
            pl.yaw = Mathf.Atan2(dir.x, dir.z);
            pl.pitch = 0f;
            Enemy.debugFreeze = true;
            yield return new WaitForSeconds(0.6f);
            if (alienTest)
            {
                yield return CheckAliens(start, dir);
                yield break;
            }
            if (themeTest)
            {
                yield return CheckThemed(start, dir);
                yield break;
            }

            // ----- 1) vista: tempo per notarti, per distanza e condizione
            Log("vista: distanza condizione luce portata t_sospetto t_caccia stato_finale consapevolezza");
            float[] dists = { 3f, 8f, 13f, 19f };
            float maxD = Mathf.Min(bestLen - 3f, 22f);
            bool shotDark = false, shotTorch = false;
            // tre condizioni: buio nero (luce misurata forzata a 0, torcia spenta), con la luce vera del posto (torcia spenta), torcia accesa nel buio
            var conds = new[] { (name: "buio", torch: false, lum: 0f), (name: "luce", torch: false, lum: float.NaN), (name: "torcia", torch: true, lum: 0f) };
            foreach (var cond in conds)
                foreach (float D0 in dists)
                {
                    bool torchOn = cond.torch;
                    Stealth.lumOverride = cond.lum;
                    float D = Mathf.Min(D0, maxD);
                    Enemy.ClearAll();
                    if (torch.on != torchOn) torch.Toggle();
                    torch.charge = 1f;
                    yield return new WaitForSeconds(0.5f);
                    Vector3 ep = start + dir * D;
                    if (NavMesh.SamplePosition(ep, out NavMeshHit eh, 2f, NavMesh.AllAreas)) ep = eh.position;
                    var e = Enemy.Spawn(Enemy.EnemyType.Monaco, ep);
                    e.transform.rotation = Quaternion.LookRotation(-dir);
                    float tS = -1f, tH = -1f, t0 = Time.time;
                    float rangeSeen = 0f;
                    while (Time.time - t0 < 6.5f)
                    {
                        rangeSeen = Stealth.SightRange(e);
                        if (tS < 0f && e.alert != Alert.Patrol) tS = Time.time - t0;
                        if (tH < 0f && e.alert == Alert.Hunting) { tH = Time.time - t0; }
                        if (!shotDark && cond.name == "buio" && D0 == 8f && Time.time - t0 > 0.5f) { shotDark = true; Shot("vista_buio_8m"); }
                        if (!shotTorch && torchOn && D0 == 8f && Time.time - t0 > 0.5f) { shotTorch = true; Shot("vista_torcia_8m"); }
                        yield return null;
                    }
                    Log($"vista {D:F0} {cond.name} lum={Stealth.PlayerLum:F2} (esposizione {Stealth.lastExposure:F2}) portata={rangeSeen:F1} t_sospetto={(tS < 0 ? "mai" : tS.ToString("F1"))} t_caccia={(tH < 0 ? "mai" : tH.ToString("F1"))} finale={e.alert} aw={e.awareness:F2}");
                    if (D0 == 8f && cond.name == "buio") passed &= tS < 0f;
                    if (D0 == 8f && torchOn) passed &= tH >= 0f;
                }
            Stealth.lumOverride = float.NaN;
            if (torch.on) torch.Toggle();

            // ----- 2) udito: un colpo di pistola a 20 m e dietro un muro
            Enemy.ClearAll();
            yield return null;
            {
                Vector3 ep = start + dir * Mathf.Min(16f, maxD);
                if (NavMesh.SamplePosition(ep, out NavMeshHit eh, 2f, NavMesh.AllAreas)) ep = eh.position;
                var near = Enemy.Spawn(Enemy.EnemyType.Monaco, ep);
                near.transform.rotation = Quaternion.LookRotation(dir);       // di spalle: non ti vede
                Enemy.debugFreeze = false;
                yield return new WaitForSeconds(0.3f);
                Noise.Emit(start, 42f, NoiseKind.Gunshot);
                yield return new WaitForSeconds(0.4f);
                Log($"udito: sparo a {Vector3.Distance(start, near.transform.position):F0} m, prete di spalle -> {near.alert} aw={near.awareness:F2}");
                for (int k = 0; k < 6 && near.alert != Alert.Patrol; k++) yield return new WaitForSeconds(3f);
                Log($"udito: dopo 18 s senza altri rumori -> {near.alert} aw={near.awareness:F2}");
                // passi: sprint a 6 m
                Enemy.ClearAll(); yield return null;
                var ear = Enemy.Spawn(Enemy.EnemyType.Monaco, start + dir * 6f);
                ear.transform.rotation = Quaternion.LookRotation(dir);
                float tHeard = -1f, tt0 = Time.time;
                while (Time.time - tt0 < 6f)
                {
                    if (Mathf.Repeat(Time.time - tt0, 0.45f) < Time.deltaTime) Noise.Emit(start, 6.8f * 1.6f * 1.15f, NoiseKind.Step);
                    if (tHeard < 0f && ear.alert != Alert.Patrol) tHeard = Time.time - tt0;
                    yield return null;
                }
                Log($"udito: passi di corsa a 6 m -> sospetto dopo {(tHeard < 0 ? "mai" : tHeard.ToString("F1"))} s, stato {ear.alert}");
                Enemy.ClearAll(); yield return null;
                var far = Enemy.Spawn(Enemy.EnemyType.Monaco, start + dir * 14f);
                far.transform.rotation = Quaternion.LookRotation(dir);
                float tt1 = Time.time;
                while (Time.time - tt1 < 4f)
                {
                    if (Mathf.Repeat(Time.time - tt1, 0.45f) < Time.deltaTime) Noise.Emit(start, 6.8f * 0.4f * 1.15f, NoiseKind.Step);
                    yield return null;
                }
                Log($"udito: passi da accovacciato a 14 m -> {far.alert} aw={far.awareness:F2}");
            }

            // ----- 3) torcia: scarica e cambio pila
            Enemy.ClearAll();
            yield return null;
            torch.charge = 0.02f; torch.spares = 2;
            if (!torch.on) torch.Toggle();
            float c0 = Time.time;
            bool swapSeen = false;
            while (Time.time - c0 < 8f)
            {
                if (torch.Swapping) swapSeen = true;
                yield return null;
            }
            Log($"torcia: pila a 2% -> cambio visto={swapSeen} carica={torch.charge:F2} scorte={torch.spares} accesa={torch.on}");
            passed &= swapSeen && torch.on && torch.charge > 0.95f && torch.spares == 1;
            torch.charge = 0.12f;
            yield return new WaitForSeconds(0.5f);
            Shot("torcia_pila_bassa");
            torch.charge = 1f;
            // pila a terra davanti a noi
            {
                Vector3 pp = start + dir * 1.6f;
                var pila = Pila.Make(pp, 30f, fd.cur.root.transform);
                int before = torch.spares;
                yield return null;
                pl.pitch = 0.5f;
                yield return new WaitForSeconds(0.3f);
                Shot("pila_a_terra");
                pila.Use();
                yield return null;
                Log($"pila: raccolta -> scorte {before} -> {torch.spares}");
                passed &= torch.spares == before + 1;
                pl.pitch = 0f;
            }
            torch.spares = 1;
            if (!torch.on) torch.Toggle();

            // ----- 4) segnalini: tre preti in tre stati
            Enemy.ClearAll();
            yield return null;
            Enemy.debugFreeze = true;
            Enemy.debugNoSense = true;
            {
                Alert[] st = { Alert.Suspicious, Alert.Searching, Alert.Hunting };
                float[] dd = { 7f, 11f, 15f };
                float[] side = { -1.2f, 0.4f, 1.6f };
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 ep = start + dir * Mathf.Min(dd[i], maxD) + right * side[i];
                    if (NavMesh.SamplePosition(ep, out NavMeshHit eh, 2f, NavMesh.AllAreas)) ep = eh.position;
                    var e = Enemy.Spawn(i == 2 ? Enemy.EnemyType.Abate : Enemy.EnemyType.Monaco, ep);
                    e.transform.rotation = Quaternion.LookRotation(-dir);
                    e.DebugSetAlert(st[i]);
                }
                yield return new WaitForSeconds(0.8f);
                Shot("segnalini");
                torch.Toggle();
                yield return new WaitForSeconds(0.8f);
                Shot("segnalini_buio");
                torch.Toggle();
            }
            Enemy.ClearAll();
            yield return null;
            Enemy.debugFreeze = false;
            Enemy.debugNoSense = false;

            // ----- 4b) tutti i tipi pattugliano senza eccezioni (al buio, giocatore fermo): quanto si muovono e che stati attraversano
            Stealth.lumOverride = 0f;
            if (torch.on) torch.Toggle();
            foreach (var tp in new[] { Enemy.EnemyType.Monaco, Enemy.EnemyType.Abate, Enemy.EnemyType.Occhio })
            {
                Enemy.ClearAll();
                yield return null;
                Vector3 ep = start + dir * Mathf.Min(14f, maxD);
                if (tp != Enemy.EnemyType.Occhio && NavMesh.SamplePosition(ep, out NavMeshHit eh2, 2f, NavMesh.AllAreas)) ep = eh2.position;
                if (tp == Enemy.EnemyType.Occhio) ep.y = 2.0f;
                var e = Enemy.Spawn(tp, ep);
                Vector3 p0 = e.transform.position;
                float tp0 = Time.time, maxMoved = 0f;
                var seenStates = new HashSet<Alert>();
                while (Time.time - tp0 < 14f)
                {
                    maxMoved = Mathf.Max(maxMoved, Vector3.Distance(p0, e.transform.position));
                    seenStates.Add(e.alert);
                    yield return null;
                }
                var sb = new StringBuilder();
                foreach (var s in seenStates) sb.Append(s).Append('/');
                Log($"tipo {tp}: in 14 s al buio si e' allontanato al massimo di {maxMoved:F1} m dalla partenza, stati visti {sb}, stato finale {e.alert}");
            }
            Stealth.lumOverride = float.NaN;
            Enemy.ClearAll();
            yield return null;

            // ----- 5) ping e freccia
            pl.invulnerable = true;
            var guide = Guide.I;
            bool ok = guide.TryPing();
            bool cooldown = !guide.TryPing();
            float pingT0 = Time.time;
            yield return new WaitForSeconds(0.25f);
            Shot("ping_onda");
            yield return new WaitForSeconds(2.8f);
            // l'onda fa 38 m/s e il segnale compare quando ci arriva: su un piano grande il piu' lontano e' a oltre 120 m
            // (3 s fissi bastavano solo ai piani piccoli): si aspetta che compaiano tutti, al massimo 8 s dal lancio
            while (Time.time - pingT0 < 8f && guide.marks.Exists(m => !m.shown)) yield return null;
            int shown = 0; float farMark = 0f;
            foreach (var m in guide.marks) { if (m.shown) shown++; farMark = Mathf.Max(farMark, Vector3.Distance(m.pos, pl.transform.position)); }
            Log($"ping: lanciato={ok} segnali={guide.marks.Count} mostrati={shown} (candele sul piano {fd.cur.candles.Count}, il segnale piu' lontano e' a {farMark:F0} m, letti {Time.time - pingT0:F1} s dopo il lancio)");
            Shot("ping_segnali");
            var arrowLabel = System.Array.Find(gm.hud.GetComponentsInChildren<UnityEngine.UI.Text>(), label => label.name == "TestoAscensore");
            bool sealedArrow = arrowLabel != null && arrowLabel.text.Contains("sigillato");
            passed &= ok && cooldown && shown == fd.cur.candles.Count + 1 && !fd.ExitOpen && sealedArrow;
            foreach (var candle in fd.cur.candles) candle.Use();
            yield return new WaitForSeconds(0.35f);
            bool updated = guide.marks.Count == 1 && guide.marks[0].kind == 2;
            bool openArrow = arrowLabel != null && arrowLabel.text.StartsWith("ascensore") && !arrowLabel.text.Contains("sigillato");
            Log($"obiettivo completato: ascensore aperto={fd.ExitOpen}, ping aggiornato={updated}, freccia aggiornata={openArrow}");
            Shot("uscita_aperta");
            passed &= fd.ExitOpen && fd.cur.exit.unlocked && updated && openArrow;

            Log(passed ? "fine: OK" : "fine: ERRORE");
            Application.Quit(passed ? 0 : 1);
        }
    }
}
