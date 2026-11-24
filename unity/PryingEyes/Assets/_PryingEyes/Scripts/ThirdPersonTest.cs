#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -tptest [-tpshots]: la terza persona (ThirdPersonView, SeatAvatar, SurvivorBody). Si guarda cosa
    /// vede la camera mentre disegna: in prima persona sta all'occhio e il corpo non c'e'; in terza persona sta a due metri dietro la
    /// spalla, non disegna arma e mani della prima persona e disegna il corpo con l'arma in mano; il raggio dell'occhio e quello della
    /// camera cadono sullo stesso punto del muro; con un sigillo sull'arma il lampo alla bocca esce dall'arma del corpo; in mira la camera
    /// si avvicina; un muro dietro le spalle la ferma; spenta, tutto torna com'era. Con -tpshots foto in unity_logs/terza-persona. Non tocca le preferenze del giocatore (GameSettings.thirdPersonOverride).</summary>
    public sealed class ThirdPersonTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;
        Camera watched;
        Transform feet;
        Vector3 feetInFrame;
        Vector3 seenPos; Quaternion seenRot; int seenMask, seenFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-tptest") >= 0) new GameObject("ThirdPersonTest").AddComponent<ThirdPersonTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] tptest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        // quello che la camera ha davanti mentre disegna: ThirdPersonView si e' iscritto prima, quindi qui la posa e' gia' quella di disegno
        void OnBegin(ScriptableRenderContext ctx, Camera c)
        {
            if (c != watched) return;
            seenPos = c.transform.position; seenRot = c.transform.rotation; seenMask = c.cullingMask; seenFrame = Time.frameCount;
            feetInFrame = c.WorldToViewportPoint(feet.position);
        }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            GameSettings.thirdPersonFramingOverride = GameSettings.DefaultThirdPersonFraming;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-tpshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            GameSettings.thirdPersonOverride = -1;
            GameSettings.thirdPersonFramingOverride = null;
            Weapon.debugAds = false;
            PlayerController.botDir = Vector3.zero;
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] tptest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void View(string name, bool tick = true)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/terza-persona"); Directory.CreateDirectory(folder);
            if (tick) LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, true);
        }

        /// <summary>Foto al buio (solo con -tpshots): tutte le lampade spente e la torcia accesa, il corpo con e senza la luce di riempimento.
        /// Niente controlli: serve a vedere se la schiena del personaggio e' leggibile o e' una macchia nera, e se il corpo fa ombra alla torcia.</summary>
        IEnumerator DarkShots()
        {
            if (!shots) yield break;
            var lamps = new List<Light>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l.enabled) continue;
                var par = l.transform.parent;
                if ((par != null && par.name == "TorciaRig") || l.name == "RiempimentoTerzaPersona") continue;
                lamps.Add(l);
            }
            bool torchWasLit = Torch.I != null && Torch.I.Lit;
            if (Torch.I != null && !torchWasLit) Torch.I.Toggle();
            Stealth.lumOverride = 0f;                   // la misura della luce (ogni 0,2 s) deve dire buio anche se le lampade si spengono solo per la foto
            yield return new WaitForSeconds(1.2f);
            // senza la luce di riempimento, poi con la luce com'e'
            foreach (var (tag, scale) in new[] { ("buio-senza", 0f), ("buio-con", 1f) })
            {
                ThirdPersonView.fillScale = scale;
                yield return new WaitForSeconds(0.3f);
                foreach (var l in lamps) if (l != null) l.enabled = false;         // la riserva di luci le riaccende ogni 0,2 s: si spengono subito prima di fotografare
                FillReport(tag);
                View(tag, false);
                foreach (var l in lamps) if (l != null) l.enabled = true;
            }
            ThirdPersonView.fillScale = 1f;
            Stealth.lumOverride = float.NaN;
            if (Torch.I != null && Torch.I.Lit && !torchWasLit) Torch.I.Toggle();
            yield return Frames(2);
        }

        /// <summary>Una riga di diagnosi sulla luce di riempimento e sul corpo (dove sono, che strati hanno): le foto al buio devono tornare con questi numeri.</summary>
        void FillReport(string tag)
        {
            var player = GameManager.I.player;
            var v = ThirdPersonView.Of(player);
            var f = v != null ? v.Fill : null;
            var av = FindFirstObjectByType<SeatAvatar>();
            var r = av != null ? av.GetComponentInChildren<Renderer>() : null;
            string lightInfo = f == null ? "nessuna luce" :
                $"en={f.enabled} int={f.intensity:F1} range={f.range:F1} pos={f.transform.position} datiStrati={(uint)f.GetUniversalAdditionalLightData().renderingLayers} luceStrati={f.renderingLayerMask}";
            string bodyInfo = r == null ? "nessun corpo" :
                $"strati={r.renderingLayerMask} centro={r.bounds.center} d={(f != null ? Vector3.Distance(f.transform.position, r.bounds.center) : 0f):F2}";
            Debug.Log($"[PryingEyes] tptest info {tag}: {lightInfo}; corpo {bodyInfo}; lum={Stealth.PlayerLum:F2}");
        }

        static GameObject Box(string name, Vector3 baseCenter, Vector3 size, Vector3 forward)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = baseCenter + Vector3.up * size.y * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            go.transform.localScale = size;
            Physics.SyncTransforms();
            return go;
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        /// <summary>Senza finestra (batchmode) la camera principale non disegna da sola: la si fa disegnare qui, su una RenderTexture piccola,
        /// cosi' OnBegin legge la posa e gli strati di quel momento (in una partita disegna a ogni fotogramma).</summary>
        void RenderNow()
        {
            seenFrame = -1;
            var rt = RenderTexture.GetTemporary(320, 180, 24);
            var prev = watched.targetTexture;
            watched.targetTexture = rt;
            watched.Render();
            watched.targetTexture = prev;
            RenderTexture.ReleaseTemporary(rt);
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            if (failed > 0) yield break;
            // URP azzera nella maschera di una luce i livelli che il progetto non definisce (TagManager.asset, RenderingLayerUtils.ToValidRenderingLayers):
            // tutti i bit che SurvivorLight usa devono avere un nome, altrimenti le luci per posto non illuminano niente
            uint used = 0u;
            for (int s = 0; s < SurvivorLight.Slots; s++) used |= SurvivorLight.BodyMask(s) | SurvivorLight.TorchMask(s) | SurvivorLight.FillMask(s);
            uint defined = RenderingLayerMask.GetDefinedRenderingLayersCombinedMaskValue();
            Check($"i livelli di luce dei corpi sono tutti definiti nel progetto (usati {used}, definiti {defined})", (defined & used) == used);
            player.invulnerable = true;
            float yawF = Mathf.Atan2(forward.x, forward.z);
            player.Teleport(arena + Vector3.up * 0.05f);
            player.yaw = yawF; player.pitch = 0.05f;
            watched = player.cam.GetComponent<Camera>();
            feet = player.transform;
            GameSettings.thirdPersonOverride = 0;
            yield return Frames(3);
            RenderPipelineManager.beginCameraRendering += OnBegin;
            yield return Frames(3);
            RenderNow();

            // 1) prima persona: la camera disegna dall'occhio, nessun corpo
            Check("prima persona: la camera disegna dall'occhio", seenFrame >= Time.frameCount - 2 && (seenPos - player.cam.position).magnitude < 0.01f);
            Check("prima persona: niente componente ne' corpo in terza persona", ThirdPersonView.Of(player) == null && FindObjectsByType<SeatAvatar>(FindObjectsSortMode.None).Length == 0);
            Check("prima persona: arma e mani si disegnano", (seenMask & (1 << Seats.ViewLayer)) != 0);

            // 2) terza persona
            GameSettings.thirdPersonOverride = 1;
            yield return new WaitForSeconds(0.7f);
            var view = ThirdPersonView.Of(player);
            Check("terza persona: c'e' il componente e la visuale e' tutta dietro la spalla", view != null && view.Blend > 0.99f);
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.beginCameraRendering += OnBegin;      // dopo ThirdPersonView, che si e' iscritto ora: qui la posa e' gia' quella di disegno
            yield return Frames(3);
            RenderNow();
            float away = (seenPos - player.cam.position).magnitude;
            Check($"la camera disegna {away:F2} m lontano dall'occhio", away > 1.2f && away < 2.7f);
            var avatar = FindFirstObjectByType<SeatAvatar>();
            Check("c'e' un corpo sopravvissuto e l'arma e' in mano", avatar != null && avatar.Body != null && avatar.Body.HasWeapon);
            Check("la camera non disegna arma e mani della prima persona e disegna il corpo",
                  avatar != null && (seenMask & (1 << Seats.ViewLayer)) == 0 && (seenMask & (1 << avatar.Layer)) != 0);
            Check("il corpo sta ai piedi del giocatore e guarda dove guarda lui",
                  avatar != null && Vector3.Distance(avatar.transform.position, player.transform.position) < 0.1f &&
                  Mathf.Abs(Mathf.DeltaAngle(avatar.transform.eulerAngles.y, player.yaw * Mathf.Rad2Deg)) < 2f);
            var fillLight = view != null ? view.Fill : null;
            uint fillLayers = fillLight != null ? fillLight.GetUniversalAdditionalLightData().renderingLayers : 0u;
            uint bodyLayers = avatar != null && avatar.Body != null ? SurvivorLight.BodyMask(avatar.Body.Slot) : 0u;
            Check($"c'e' una luce di riempimento che illumina solo il corpo (strati {fillLayers})",
                  fillLight != null && fillLight.enabled && fillLayers != 0u && (fillLayers & 1u) == 0u && (fillLayers & ~bodyLayers) == 0u);
            View("ferma");

            // 3) i colpi cadono dove sta il mirino: il raggio dalla camera e quello dall'occhio arrivano allo stesso punto del muro
            var wall = Box("Muro di prova", arena + forward * 3.4f, new Vector3(4f, 3f, 0.3f), forward);
            yield return Frames(4);
            RenderNow();
            Vector3 camDir = seenRot * Vector3.forward;
            bool hitCam = Physics.Raycast(seenPos, camDir, out var h1, 50f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
            bool hitEye = Physics.Raycast(player.cam.position, player.cam.forward, out var h2, 50f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
            Check($"il mirino e l'occhio inquadrano lo stesso punto (scarto {(h1.point - h2.point).magnitude * 100f:F0} cm)", hitCam && hitEye && (h1.point - h2.point).magnitude < 0.12f);
            View("muro");
            DestroyImmediate(wall); Physics.SyncTransforms();
            yield return DarkShots();

            // 3b) il sigillo sull'arma: il lampo alla bocca esce dall'arma del corpo che si vede, non da quella nascosta della prima persona
            // (che in terza persona sta a un palmo dall'occhio, cioe' dietro la spalla); e' il punto in cui il lavoro dei sigilli e quello
            // della terza persona si toccano, nato in due rami diversi
            var wpn = player.weapon;
            if (wpn != null && avatar != null && avatar.Body != null && avatar.Body.HasWeapon)
            {
                wpn.MaxAmmo();
                player.Arcana.ApplyInfusion(14, wpn.SlotIndex);
                int sigilShots = SigilFx.Shots;
                SigilFx.LastMuzzle = new Vector3(float.NaN, float.NaN, float.NaN);
                Vector3 mzBody = avatar.Body.Muzzle.position;       // nello stesso fotogramma del colpo: niente si e' ancora mosso
                wpn.DebugFire();
                bool sigilOk = SigilFx.Shots > sigilShots && !float.IsNaN(SigilFx.LastMuzzle.x);
                float dMuzzle = sigilOk ? Vector3.Distance(SigilFx.LastMuzzle, mzBody) : -1f;
                float dEyeMuzzle = sigilOk ? Vector3.Distance(SigilFx.LastMuzzle, player.cam.position) : -1f;
                Check($"terza persona: il lampo del sigillo esce dall'arma del corpo ({dMuzzle * 100f:F1} cm dalla sua bocca, {dEyeMuzzle:F2} m dall'occhio)", sigilOk && dMuzzle < 0.02f);
                player.Arcana.SlotInfusionLeft[wpn.SlotIndex] = 0f;
                yield return Frames(2);
            }
            else Check("terza persona: ci sono l'arma e il corpo per la prova del sigillo", false);

            // 4) in mira la camera si avvicina
            Weapon.debugAds = true;
            yield return new WaitForSeconds(0.7f);
            RenderNow();
            float awayAds = (seenPos - player.cam.position).magnitude;
            Check($"in mira la camera si avvicina ({away:F2} -> {awayAds:F2} m)", awayAds < away - 0.3f);
            View("mira");
            Weapon.debugAds = false;
            yield return new WaitForSeconds(0.5f);

            // 5) in cammino: il corpo corre con le gambe, la camera lo segue
            PlayerController.holdLook = false;          // botDir cammina solo con la rotazione automatica dell'autotest accesa
            PlayerController.botDir = forward * 0.6f;
            yield return new WaitForSeconds(0.9f);
            float speed = avatar != null && avatar.Body != null ? avatar.Body.input.velocity.magnitude : 0f;
            Check($"in cammino il corpo riceve la velocita' ({speed:F1} m/s)", speed > 1.2f);
            RenderNow();
            Check($"in cammino l'inquadratura include i piedi ({feetInFrame.y:P0})",
                  feetInFrame.z > 0f && feetInFrame.y > 0.015f && feetInFrame.y < 1f);
            View("cammino");
            PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
            yield return new WaitForSeconds(0.4f);

            // 6) un muro dietro le spalle ferma la camera
            Vector3 back = -(player.cam.rotation * Vector3.forward); back.y = 0f; back = back.normalized;
            var behind = Box("Muro dietro", player.transform.position + back * 0.9f, new Vector3(4f, 3f, 0.2f), back);
            yield return Frames(4);
            RenderNow();
            float awayWall = (seenPos - player.cam.position).magnitude;
            Check($"con un muro a 90 cm dietro la camera si ferma prima ({awayWall:F2} m)", awayWall < 1.05f);
            View("muro-dietro");
            Check("in uno spazio stretto il corpo lascia posto alle mani",
                  avatar != null && (seenMask & (1 << avatar.Layer)) == 0 && (seenMask & (1 << Seats.ViewLayer)) != 0);
            DestroyImmediate(behind); Physics.SyncTransforms();
            yield return new WaitForSeconds(0.10f);
            RenderNow();
            float opening = Vector3.Distance(seenPos, player.cam.position);
            yield return new WaitForSeconds(0.65f);
            RenderNow();
            float recovered = Vector3.Distance(seenPos, player.cam.position);
            Check($"la camera si riallontana gradualmente ({awayWall:F2}, {opening:F2}, {recovered:F2} m)",
                  opening < recovered - 0.4f && Mathf.Abs(recovered - away) < 0.25f);

            if (avatar != null && avatar.Body != null)
            {
                var body = avatar.Body;
                float standing = body.Hips.position.y - avatar.transform.position.y;
                float lowest = standing;
                bool airborne = false;
                float until = Time.time + 1.25f;
                player.Teleport(player.transform.position + Vector3.up * 1.35f);
                while (Time.time < until)
                {
                    yield return null;
                    airborne |= !body.input.grounded;
                    if (airborne && body.input.grounded)
                        lowest = Mathf.Min(lowest, body.Hips.position.y - avatar.transform.position.y);
                }
                Check("una caduta porta il corpo nella posa in aria e poi a terra", airborne && body.input.grounded);
                float settled = body.Hips.position.y - avatar.transform.position.y;
                Check($"il bacino ammortizza l'atterraggio e risale ({(settled - lowest) * 100f:F1} cm)",
                      settled - lowest > 0.035f && Mathf.Abs(settled - standing) < 0.03f);
            }
            else
            {
                Check("una caduta porta il corpo nella posa in aria e poi a terra", false);
                Check("il bacino ammortizza l'atterraggio e risale", false);
            }

            // 7) spenta: la camera torna all'occhio, il corpo e il componente spariscono
            GameSettings.thirdPersonOverride = 0;
            yield return new WaitForSeconds(0.7f);
            yield return Frames(3);
            RenderNow();
            Check("spenta: la camera torna all'occhio", (seenPos - player.cam.position).magnitude < 0.01f);
            Check("spenta: il componente, il corpo e la luce di riempimento spariscono",
                  ThirdPersonView.Of(player) == null && FindObjectsByType<SeatAvatar>(FindObjectsSortMode.None).Length == 0 && GameObject.Find("RiempimentoTerzaPersona") == null);
            Check("spenta: arma e mani tornano", (seenMask & (1 << Seats.ViewLayer)) != 0);
            player.invulnerable = false;
        }
    }
}
#endif
