#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;
using Debug = UnityEngine.Debug;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -jellytest [-jellyshots]: la medusa vera nel gioco. Struttura dello scheletro, battito della
    /// campana, funi che ondeggiano e non attraversano il pavimento, raccolta/affondo/recupero (organi che si accendono, bocca e funi che vanno
    /// in avanti), effetti del contatto (particelle, anello, luce), colpo ricevuto, morte (funi flosce, organi spenti), replica di rete,
    /// costo per fotogramma. Con -jellyshots salva le foto in unity_logs/jelly/.</summary>
    public sealed class JellyTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        Vector3 arena, forward;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-jellytest") >= 0)
                new GameObject("JellyTest").AddComponent<JellyTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] jellytest " + (ok ? "ok " : "FALLITO ") + name); }
        void LogHandler(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += LogHandler;
            // una foto allunga il fotogramma: senza questo tetto la fase dell'attacco (0,71 s) salterebbe dentro un solo passo
            float maxDelta = Time.maximumDeltaTime;
            Time.maximumDeltaTime = 0.05f;
            var run = Run();
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break;
                yield return run.Current;
            }
            Time.maximumDeltaTime = maxDelta;
            PlayerController.botDir = Vector3.zero;
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] jellytest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void Photo(string name)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-jellyshots") < 0) return;
            string output = Path.GetFullPath("unity_logs/jelly");
            Directory.CreateDirectory(output);
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(output, name + ".png"), null, true);
        }

        Enemy Spawn(Vector3? at = null)
        {
            Fx.Clear();
            var player = GameManager.I.player;
            player.Teleport(arena + Vector3.up * 0.05f); player.hp = 100; player.shield = 50; player.invulnerable = false;
            player.yaw = Mathf.Atan2(forward.x, forward.z); player.pitch = 0;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            var enemy = Enemy.Spawn(Enemy.EnemyType.Medusa, at ?? arena + forward * 1.9f + Vector3.up * 1.5f);
            enemy.transform.rotation = Quaternion.LookRotation(-forward);
            enemy.MakeRelentless();
            return enemy;
        }

        IEnumerator WaitPreparation(Enemy enemy)
        {
            float end = Time.time + 6f;
            while (!enemy.PreparingFlight && Time.time < end) yield return null;
        }

        IEnumerator WaitContact(Enemy enemy)
        {
            float end = Time.time + 2f;
            while (enemy.PreparingFlight && Time.time < end) yield return null;
        }

        /// <summary>Particelle e anelli del contatto nel campo visivo (materiali standard di Fx: quelli che le prove dell'affondo contano).</summary>
        static int VisibleImpactParticles()
        {
            var root = GameObject.Find("Fx");
            if (root == null) return 0;
            int count = 0;
            var cam = Camera.main;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || (r.sharedMaterial != Fx.SparkMat && r.sharedMaterial != Fx.PuffMat)) continue;
                Vector3 s = cam.WorldToViewportPoint(r.transform.position);
                if (s.z > cam.nearClipPlane && s.x > 0 && s.x < 1 && s.y > 0 && s.y < 1 && r.transform.localScale.x > 0.001f) count++;
            }
            return count;
        }

        /// <summary>Particelle con i materiali propri della medusa (getto, raccolta, gocce, spore).</summary>
        static int OwnParticles()
        {
            var root = GameObject.Find("Fx");
            if (root == null) return 0;
            int count = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.sharedMaterial != null && r.sharedMaterial.name.EndsWith("Muco")) count++;
            return count;
        }

        IEnumerator Run()
        {
            float end = Time.realtimeSinceStartup + 60f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < end) yield return null;
            Check("arena fisica libera", MeleeTest.FindArena(out arena, out forward));
            if (failed > 0) yield break;
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = false;
            Torch.I.on = true; GameManager.I.hud.Banner("", ""); GameManager.I.hud.Toast("");
            var player = GameManager.I.player;

            // ---- struttura e vita a riposo (lontana dal giocatore: non attacca, finche' non la si aizza)
            var e = Spawn(arena + forward * 4.5f + Vector3.up * 1.6f);
            var rig = e.GetComponent<JellyRig>();
            Check("la medusa e' il prefab con JellyRig", rig != null && e.GetComponentsInChildren<SkinnedMeshRenderer>().Length >= 6);
            if (rig == null) yield break;
            Check("scheletro: 8 settori di campana, labbra e almeno 40 funi", rig.BellCount == 8 && rig.LipCount >= 6 && rig.RopeCount >= 40);
            Check("punti deboli: bocca e corpo con collider", Array.Exists(e.GetComponentsInChildren<Collider>(), c => c.CompareTag("Head") && !c.isTrigger)
                && e.GetComponent<SphereCollider>() != null);
            Enemy.debugFreeze = true;                                                  // ferma: solo l'animazione
            yield return new WaitForSeconds(0.3f);
            float rmin = float.MaxValue, rmax = 0f, tipMove = 0f;
            Vector3 tip0 = rig.TentacleTipsLocal();
            float t0 = Time.time;
            while (Time.time - t0 < 2.4f)
            {
                float r = rig.RimRadius();
                rmin = Mathf.Min(rmin, r); rmax = Mathf.Max(rmax, r);
                tipMove = Mathf.Max(tipMove, (rig.TentacleTipsLocal() - tip0).magnitude);
                yield return null;
            }
            Check($"la campana pulsa (raggio del bordo {rmin:F3}..{rmax:F3} m)", rmax - rmin > 0.03f);
            Check($"i tentacoli ondeggiano e seguono ({tipMove:F3} m)", tipMove > 0.02f);
            Check("le funi non attraversano il pavimento", rig.LowestTipY() >= arena.y - 0.05f);
            Check("nessun numero impazzito nelle funi", rig.AllFinite());
            float glowIdle = rig.GlowLevel;
            Vector3 idleTips = rig.TentacleTipsLocal();
            Photo("riposo");

            // ---- costo di un passo di animazione
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) rig.Tick(1f / 60f);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 200.0;
            Debug.Log($"[PryingEyes] jellytest costo Tick {ms:F3} ms");
            Check($"un passo di animazione costa poco ({ms:F2} ms)", ms < 0.9);
            Enemy.Remove(e); yield return null;

            // ---- affondo vero: raccolta, affondo, contatto, recupero. La medusa attacca appena e' a portata: i valori di riposo sono
            // quelli di prima (ferma e lontana), e le misure si prendono per tempo di volo (rig.FlightTime), non per fotogrammi
            e = Spawn(); rig = e.GetComponent<JellyRig>();
            yield return WaitPreparation(e);
            Check("prepara il colpo", e.PreparingFlight);
            float health = player.hp + player.shield;
            float hi = 0f, bestForward = -9f, strikeZ = float.MinValue;
            Vector3 gatherTips = Vector3.zero;
            bool gathered = false, struck = false;
            float guard = Time.time + 3f;
            while (e.PreparingFlight && Time.time < guard)
            {
                float ft = rig.FlightTime;
                hi = Mathf.Max(hi, rig.GlowLevel);
                Vector3 m = e.transform.InverseTransformPoint(rig.MouthPoint) - e.transform.InverseTransformPoint(rig.BellCenter);
                bestForward = Mathf.Max(bestForward, m.z);
                if (ft >= 0.55f) strikeZ = Mathf.Max(strikeZ, rig.TentacleTipsLocal().z);
                if (!gathered && ft >= 0.40f)
                {
                    gathered = true;
                    gatherTips = rig.TentacleTipsLocal();
                    Check("la raccolta ha effetti propri (puntini che convergono)", OwnParticles() > 0);
                    Photo("raccolta");
                }
                if (!struck && ft >= 0.62f)
                {
                    struck = true;
                    Photo("affondo");
                }
                yield return null;
            }
            Check($"gli organi si accendono nella raccolta ({glowIdle:F2} -> {hi:F2})", hi > glowIdle * 1.8f && hi > 2f);
            Check($"i tentacoli si arrotolano in su e indietro ({gatherTips - idleTips})", gathered && gatherTips.y > idleTips.y + 0.15f);
            Check($"nell'affondo la bocca va in avanti ({bestForward:F2} m)", bestForward > 0.10f);
            yield return null;
            Check("contatto sul giocatore", player.hp + player.shield < health - 0.1f);
            Check("contatto: particelle nel campo visivo", VisibleImpactParticles() > 0);
            Check("contatto: anello d'urto", GameObject.Find("AnelloMuco") != null);
            Check("contatto: lampo di luce verde", GameObject.Find("LampoMuco") != null);
            Photo("contatto");
            // le funi, che nella raccolta sono andate su e indietro, arrivano in avanti verso il contatto e subito dopo (la molla le insegue)
            float lashEnd = Time.time + 0.2f;
            while (Time.time < lashEnd) { strikeZ = Mathf.Max(strikeZ, rig.TentacleTipsLocal().z); yield return null; }
            Check($"le funi frustano in avanti ({strikeZ - gatherTips.z:F2} m oltre la raccolta)", gathered && strikeZ > gatherTips.z + 0.35f);
            yield return new WaitForSeconds(0.7f);
            Check("recupero: gli organi tornano a riposo", rig.GlowLevel < hi * 0.7f);
            Check("nessun numero impazzito dopo l'affondo", rig.AllFinite());

            // ---- colpo ricevuto
            Enemy.debugFreeze = true;
            yield return new WaitForSeconds(1.0f);
            float before = rig.GlowLevel;
            e.Damage(1f, rig.BellCenter + Vector3.up * 0.2f + forward * 0.3f, false);
            float peak = 0f;
            float hitEnd = Time.time + 0.20f;                    // a tempo, non a fotogrammi: in batchmode girano a migliaia al secondo
            while (Time.time < hitEnd) { peak = Mathf.Max(peak, rig.GlowLevel); yield return null; }
            Check($"colpita: gli organi balenano ({before:F2} -> {peak:F2})", peak > before * 1.5f);
            Check("colpita: gocce luminose", OwnParticles() > 0);
            Photo("colpita");

            // ---- morte: funi flosce, organi spenti
            Vector3 tipsAlive = rig.TentacleTipsLocal();
            // la torcia del giocatore e' accesa nella prova: finche' la medusa e' viva e vicina sfarfalla, ma da morta il fascio pieno a un
            // metro brucia di bianco il gel e l'interno chiari (e' luce vera, non un difetto degli effetti): la foto della morte la tiene spenta
            var torchBeam = GameObject.Find("Fascio");
            if (torchBeam != null) torchBeam.SetActive(false);
            e.Damage(9999f, rig.BellCenter, false);
            yield return new WaitForSeconds(0.2f);
            Check("morta: scoppio di muco nel campo visivo", VisibleImpactParticles() > 0 || OwnParticles() > 0);
            Check("morta: anello d'urto", GameObject.Find("AnelloMuco") != null);
            Photo("morta_subito");
            yield return new WaitForSeconds(2.6f);
            Check("morta: organi spenti", rig.dead && rig.GlowLevel < 0.35f);
            Check("morta: nessun numero impazzito", rig.AllFinite());
            Photo("morta");
            if (torchBeam != null) torchBeam.SetActive(true);
            Enemy.Remove(e); yield return null;
            Enemy.debugFreeze = false;

            // ---- replica di rete: l'attacco dell'ospite si vede anche sul compagno
            var state = new EnemyNet { pos = arena + forward * 2.2f + Vector3.up * 1.5f, hp = 1, alert = Alert.Hunting };
            var replica = Enemy.SpawnPuppet(9878, Enemy.EnemyType.Medusa, state);
            var rrig = replica.GetComponent<JellyRig>();
            Check("replica con JellyRig", rrig != null);
            yield return new WaitForSeconds(0.5f);
            float idleR = rrig != null ? rrig.GlowLevel : 0f;
            replica.PuppetEvent(EnemyEvt.Attack, -forward, 0);
            float hiR = 0f;
            float t2 = Time.time;
            while (Time.time - t2 < 0.5f) { if (rrig != null) hiR = Mathf.Max(hiR, rrig.GlowLevel); yield return null; }
            Check($"replica: gli organi si accendono nella raccolta ({idleR:F2} -> {hiR:F2})", hiR > idleR * 1.8f);
            Fx.Clear();
            replica.PuppetEvent(EnemyEvt.Strike, player.cam.position + player.cam.forward * 0.4f, 0);
            Check("replica: colpo mancato senza impatto", VisibleImpactParticles() == 0);
            replica.PuppetEvent(EnemyEvt.Strike, player.cam.position + player.cam.forward * 0.4f, 1);
            yield return null;
            Check("replica: impatto del colpo riuscito", VisibleImpactParticles() > 0);
            Enemy.Remove(replica);
        }
    }
}
#endif
