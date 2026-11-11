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
    public class StealthTest : MonoBehaviour
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

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            bool alienTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-alientest") >= 0;
            bool themeTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-temitest") >= 0;
            bool reference = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-refshot") >= 0;
            var gm = GameManager.I;
            while (FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) yield return null;
            if (reference)
            {
                yield return ReferenceShots(FloorDirector.I);
                yield break;
            }
            log = Dir + (alienTest ? "/alieni.txt" : themeTest ? "/temi.txt" : "/furtivo.txt");
            File.WriteAllText(log, "");
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
            yield return new WaitForSeconds(0.25f);
            Shot("ping_onda");
            yield return new WaitForSeconds(2.8f);
            int shown = 0; foreach (var m in guide.marks) if (m.shown) shown++;
            Log($"ping: lanciato={ok} segnali={guide.marks.Count} mostrati={shown} (candele sul piano {fd.cur.candles.Count})");
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
