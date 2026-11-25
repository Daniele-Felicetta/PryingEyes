#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly moto: la moto del deserto (DesertBike, PlayerController.Ride.cs) con il controller vero e comandi forzati (DesertBike.bot*): c'e' ai piedi della scalinata e poggia a terra, Usa
    /// la sceglie e il giocatore sale (collider spento, arma nascosta), gas, velocita' massima, turbo, sterzo (la moto si inclina), freno e retromarcia, freno a mano (si deraPa), urto contro il muro
    /// della cattedrale (danno e arresto), discesa da fermi e frenando da soli, volo e atterraggio, fari di notte, un percorso lungo di oltre un chilometro in cui il deserto si costruisce davanti
    /// (nessuna caduta, nessun fotogramma lungo, velocita' media alta), la morte in sella lontano da casa (la moto resta li' e, alla rinascita, torna a casa), la moto di un compagno (puppet) e,
    /// con -desertshots, le fotografie.</summary>
    public sealed partial class DesertTest
    {
        float motoWorst;

        static DesertBike FirstBike()
        {
            foreach (var b in DesertBike.All) if (b != null && !b.puppet) return b;
            return null;
        }

        IEnumerator MotoCamera()
        {
            var player = Pl;
            var bike = FirstBike();
            var camera = player.cam.GetComponent<Camera>();
            Check("motocamera: bike and rendering camera are available",
                bike != null && camera != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null);
            if (bike == null || camera == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;

            PlayerController.holdLook = true;
            PlayerController.botDir = Vector3.zero;
            DesertBike.BotReset();
            Vector2 sand = OpenSand(80f);
            yield return GoTo(sand.x, sand.y, 2f);
            Vector3 start = player.transform.position;
            GroundAt(start.x, start.z + 1.8f, out float ground);
            bike.Park(new Vector3(start.x, ground, start.z + 1.8f), 0f);
            player.yaw = 0f;
            player.pitch = 0.12f;

            int perspective = GameSettings.thirdPersonOverride;
            Vector3? framing = GameSettings.thirdPersonFramingOverride;
            bool playerEnabled = player.enabled;
            float captureStep = Time.captureDeltaTime;
            var target = RenderTexture.GetTemporary(320, 180, 24);
            var previousTarget = camera.targetTexture;
            Vector3 rendered = default;
            int mask = 0;
            GameObject wall = null;
            void Capture(ScriptableRenderContext _, Camera current)
            {
                if (current != camera) return;
                rendered = current.transform.position;
                mask = current.cullingMask;
            }
            Vector3 Sample()
            {
                renderFrame = Time.frameCount;
                RenderPipelineManager.beginCameraRendering += Capture;
                try { camera.Render(); }
                finally { RenderPipelineManager.beginCameraRendering -= Capture; }
                return rendered;
            }

            try
            {
                GameSettings.thirdPersonOverride = 1;
                GameSettings.thirdPersonFramingOverride = GameSettings.DefaultThirdPersonFraming;
                yield return new WaitForSeconds(0.65f);
                Time.captureDeltaTime = 1f / 60f;
                camera.targetTexture = target;
                Vector3 footView = Sample();
                Check("motocamera: starts in third person", Vector3.Distance(footView, player.cam.position) > 1.2f);

                bike.Use();
                yield return null;
                float mountCut = Vector3.Distance(footView, Sample());
                Check("motocamera: mounting continues from the visible shoulder camera",
                    player.Riding && mountCut < 0.40f, $"{mountCut:F2} m on the first frame");
                yield return new WaitForSeconds(0.55f);
                Vector3 rideView = Sample();
                Vector3 normalOffset = rideView - bike.EyeWorld;
                var rider = FindFirstObjectByType<SeatAvatar>();
                Check("motocamera: third person frames the seated rider from outside",
                    normalOffset.z < -2f && rider != null && rider.Body != null && rider.Body.SeatAmount > 0.9f
                    && (mask & (1 << rider.Layer)) != 0 && !bike.ArmsShown, $"camera offset {normalOffset:F2}");
                player.enabled = false;
                yield return Frames(3);
                Check("motocamera: a paused controller keeps rendering from outside the seated rider",
                    Vector3.Distance(Sample(), rideView) < 0.05f);
                player.enabled = playerEnabled;

                GameSettings.thirdPersonFramingOverride = new Vector3(-0.8f, 0.5f, 4.0f);
                yield return new WaitForSeconds(0.8f);
                Vector3 adjusted = Sample() - bike.EyeWorld;
                Check("motocamera: distance, height and shoulder change the mounted view",
                    adjusted.z < normalOffset.z - 1f && adjusted.y > normalOffset.y + 0.35f
                    && adjusted.x < -0.6f, $"camera offset {adjusted:F2}");
                Shot("motocamera_terza_persona");

                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = bike.EyeWorld + Vector3.back * 0.7f;
                wall.transform.localScale = new Vector3(4f, 4f, 0.12f);
                Physics.SyncTransforms();
                yield return null;
                Check("motocamera: a close wall switches to the eye and hides the rider",
                    Vector3.Distance(Sample(), bike.EyeWorld) < 0.08f && rider != null
                    && (mask & (1 << rider.Layer)) == 0 && bike.ArmsShown);
                DestroyImmediate(wall);
                Physics.SyncTransforms();
                GameSettings.thirdPersonFramingOverride = GameSettings.DefaultThirdPersonFraming;
                yield return new WaitForSeconds(0.85f);
                rideView = Sample();

                player.RideRequestDismount();
                float largestStep = 0f;
                Vector3 previous = rideView;
                for (int i = 0; i < 36; i++)
                {
                    yield return null;
                    Vector3 current = Sample();
                    largestStep = Mathf.Max(largestStep, Vector3.Distance(previous, current));
                    previous = current;
                }
                Check("motocamera: dismounting returns to third person without a cut",
                    !player.Riding && largestStep < 0.55f && Vector3.Distance(previous, player.cam.position) > 1.2f
                        && (mask & (1 << Seats.ViewLayer)) == 0, $"largest step {largestStep:F2} m");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.ReleaseTemporary(target);
                Time.captureDeltaTime = captureStep;
                player.enabled = playerEnabled;
                GameSettings.thirdPersonOverride = perspective;
                GameSettings.thirdPersonFramingOverride = framing;
                if (wall != null) DestroyImmediate(wall);
                if (player.Riding) player.AbortRide();
            }
        }

        static bool GroundAt(float x, float z, out float y)
        {
            y = DesertWorld.Height(x, z);
            if (Physics.Raycast(new Vector3(x, y + 40f, z), Vector3.down, out var h, 90f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) { y = h.point.y; return true; }
            return false;
        }

        /// <summary>Quanto la moto sta sopra il terreno (m): positivo se vola, negativo se e' sprofondata. Il collider della moto si spegne un attimo: il raggio non deve trovarlo.</summary>
        static float Gap(DesertBike b)
        {
            var box = b.GetComponent<BoxCollider>();
            bool was = box != null && box.enabled;
            if (box != null) box.enabled = false;
            bool hit = Physics.Raycast(b.transform.position + Vector3.up * 2f, Vector3.down, out var h, 6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
            if (box != null) box.enabled = was;
            return hit ? b.transform.position.y - h.point.y : 99f;
        }

        static float Feel(Vector3 from, Vector3 dir, float dist)
        {
            if (Physics.SphereCast(from + Vector3.up * 0.9f, 0.5f, dir, out var h, dist, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && h.normal.y < 0.6f) return h.distance;
            return dist;
        }

        /// <summary>Il pilota automatico delle prove: punta verso 'dir' e schiva quello che c'e' davanti (tre antenne a 30 m, un muro o un sasso e' un ostacolo, una duna no).</summary>
        void Autopilot(Vector3 dir)
        {
            var pl = Pl;
            float h = pl.RideHeading;
            Vector3 pos = pl.transform.position, fwd = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h));
            float diff = Mathf.DeltaAngle(h * Mathf.Rad2Deg, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
            float steer = Mathf.Clamp(diff / 25f, -1f, 1f);
            float dC = Feel(pos, fwd, 30f), dL = Feel(pos, Quaternion.Euler(0f, -28f, 0f) * fwd, 30f), dR = Feel(pos, Quaternion.Euler(0f, 28f, 0f) * fwd, 30f);
            if (dC < 26f) steer = dL > dR ? -1f : 1f;
            else if (dL < 12f) steer = Mathf.Max(steer, 0.6f);
            else if (dR < 12f) steer = Mathf.Min(steer, -0.6f);
            DesertBike.botSteer = steer;
        }

        /// <summary>Dove la minimappa deve mettere il tondo di una moto parcheggiata, in pixel dal centro (est a destra, nord in alto), guardando dal giocatore.</summary>
        static Vector2 BikeDotWant(PlayerController pl, DesertBike bike) =>
            new Vector2((bike.transform.position.x - pl.transform.position.x) * HUD.DesertMapPixelsPerMetre, (bike.transform.position.z - pl.transform.position.z) * HUD.DesertMapPixelsPerMetre);

        /// <summary>Guida con il pilota automatico verso 'dir' finche' 'until' e' vero (o scade 'timeout').</summary>
        IEnumerator RideUntil(Func<bool> until, float timeout, Vector3 dir)
        {
            float t0 = Time.time;
            while (Time.time - t0 < timeout && Pl.Riding)
            {
                bool done; try { done = until(); } catch { done = false; }
                if (done) yield break;
                Autopilot(dir);
                motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                yield return null;
            }
        }

        IEnumerator Moto()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            motoWorst = 0f;
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("moto: c'e' una moto parcheggiata", bike != null);
            if (bike == null) yield break;
            Vector3 home = bike.transform.position;
            float toStairs = Flat(home, new Vector3(HubPlan.SpawnX, home.y, -5.8f));
            Check("moto: sta ai piedi della scalinata, verso il deserto (tra 3 e 16 m dal suo piede)", toStairs > 3f && toStairs < 16f, $"{toStairs:F1} m, in {home:F1}");
            Vector3 south = new Vector3(0f, 0f, -1f);

            // ---- parcheggiata: a terra, con il suo collider, e la scelta di Usa
            yield return GoTo(home.x - 2.2f, home.z + 0.3f, 2f);
            yield return new WaitForSeconds(0.9f);
            float gap = Gap(bike);
            Check("moto: poggia sul terreno", bike.Settled && Mathf.Abs(gap) < 0.2f, $"scarto {gap:F2} m, posata {bike.Settled}");
            Check("moto: da parcheggiata ha il collider", bike.ColliderOn);
            Check("moto: i fari sono spenti, il motore e' muto", !bike.HeadlightOn && bike.EngineVolume < 0.02f, $"volume {bike.EngineVolume:F3}");
            var hudM = Gm.hud;
            yield return WaitFor(() => hudM != null && hudM.DesertMapShown && hudM.DesertMapBikeShown, 6f);
            var dotWant = BikeDotWant(pl, bike);
            Check("moto: parcheggiata e' un tondo sulla minimappa, dove sta (a 2 m: meno di 3 px dal centro, non nel centro)", hudM != null && hudM.DesertMapBikeShown && (hudM.DesertMapBikeAt - dotWant).magnitude < 0.3f && dotWant.magnitude > 0.4f,
                hudM == null ? "nessun HUD" : $"visibile {hudM.DesertMapBikeShown}, a {hudM.DesertMapBikeAt}, attesa a {dotWant}");
            if (shots)
            {
                yield return Snap("moto_a_fronte", 10.5f, bike.Point + Vector3.up * 0.05f, 1.0f);
                yield return GoTo(home.x + 3.0f, home.z - 1.2f, 2f);
                yield return Snap("moto_b_lato", 10.5f, bike.Point, 1.0f);
                yield return GoTo(home.x + 0.5f, home.z + 3.4f, 2f);
                yield return Snap("moto_c_dietro", 10.5f, bike.Point, 1.0f);
                // la scalinata dall'alto: la moto laggiu', col deserto dietro
                pl.Teleport(new Vector3(21.5f, 0.2f, 2.4f));
                yield return new WaitForSeconds(0.5f);
                yield return Snap("moto_d_dalla_scalinata", 11.0f, bike.Point, 1.0f);
            }
            yield return GoTo(home.x - 2.0f, home.z + 0.2f, 2f);
            yield return new WaitForSeconds(0.4f);
            Face(bike.Point);
            yield return null; yield return null;
            Interact.Tick(pl.cam);
            var near = Interact.Current as DesertBike;
            Check("moto: guardandola da vicino Usa la sceglie", near == bike && Interact.Current.Prompt == "Sali in moto", near == null ? "niente" : near.name);
            Interact.TryUse();
            yield return null; yield return null;
            Check("moto: Usa mette in sella", pl.Riding && bike.rider == pl && pl.Bike == bike);
            Check("moto: in sella il collider si spegne e l'arma sparisce", !bike.ColliderOn && !pl.weapon.ViewRootT.gameObject.activeSelf);
            Check("moto: il corpo del giocatore sta sulla moto", Flat(pl.transform.position, bike.transform.position) < 0.3f, $"{Flat(pl.transform.position, bike.transform.position):F2} m");
            yield return WaitFor(() => hudM != null && !hudM.DesertMapBikeShown, 3f);
            Check("moto: in sella il tondo della minimappa si spegne (sei la freccia rossa)", hudM != null && hudM.DesertMapShown && !hudM.DesertMapBikeShown, hudM == null ? "nessun HUD" : $"mappa accesa {hudM.DesertMapShown}, tondo {hudM.DesertMapBikeShown}");
            Check("moto: da ferma non si muove", Mathf.Abs(pl.RideSpeed) < 0.3f);
            yield return WaitFor(() => hudM != null && hudM.SpeedoShown, 3f);
            Check("moto: in sella c'e' il tachimetro, a zero, con la lancetta a inizio scala e la spia del turbo spenta", hudM != null && hudM.SpeedoShown && hudM.SpeedoKmh == 0 && Mathf.Abs(hudM.SpeedoNeedleDeg - HUD.SpeedoDegFor(0f)) < 3f && !hudM.SpeedoTurboLit && !hudM.SpeedoReverseShown,
                hudM == null ? "nessun HUD" : $"acceso {hudM.SpeedoShown}, numero {hudM.SpeedoKmh}, lancetta {hudM.SpeedoNeedleDeg:F0} gradi, turbo {hudM.SpeedoTurboLit}, retromarcia {hudM.SpeedoReverseShown}");
            pl.yaw = pl.RideHeading; pl.pitch = 0.16f;
            yield return new WaitForSeconds(0.8f);          // il passaggio della camera sull'occhio del pilota
            if (shots) Shot("moto_e_cabina");

            // ---- gas
            Vector3 p0 = pl.transform.position; float t0 = Time.time;
            DesertBike.botThrottle = 1f;
            yield return RideUntil(() => pl.RideSpeed > 14f, 10f, south);
            float tAcc = Time.time - t0;
            Check("moto: col gas a tavoletta arriva a 50 km/h in meno di 4 s", pl.RideSpeed > 14f && tAcc < 4f, $"{pl.RideSpeed * 3.6f:F0} km/h in {tAcc:F1} s");
            int speedoNow = hudM != null ? hudM.SpeedoKmh : -1, speedoWant = Mathf.RoundToInt(Mathf.Abs(pl.RideSpeed) * 3.6f);
            Check("moto: il tachimetro dice la velocita' vera (il numero in km/h e la lancetta sul numero)", hudM != null && Mathf.Abs(speedoNow - speedoWant) <= 3 && Mathf.Abs(hudM.SpeedoNeedleDeg - HUD.SpeedoDegFor(speedoNow)) < 10f,
                hudM == null ? "nessun HUD" : $"numero {speedoNow} km/h, velocita' vera {speedoWant}, lancetta {hudM.SpeedoNeedleDeg:F0} gradi (per {speedoNow} km/h: {HUD.SpeedoDegFor(speedoNow):F0})");
            Vector3 fwd = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
            Vector3 moved = pl.transform.position - p0; moved.y = 0f;
            Check("moto: va nel verso in cui punta", moved.sqrMagnitude > 4f && Vector3.Dot(moved.normalized, fwd) > 0.9f, $"spostata di {moved.magnitude:F1} m, coseno {Vector3.Dot(moved.normalized, fwd):F2}");
            Check("moto: il motore canta e le ruote girano", bike.EngineVolume > 0.12f && bike.EnginePitch > 0.9f && Mathf.Abs(bike.SteerVisual) < 30f, $"volume {bike.EngineVolume:F2}, tono {bike.EnginePitch:F2}");
            yield return RideUntil(() => pl.RideSpeed > 21f, 14f, south);
            float vTop = pl.RideSpeed;
            Check("moto: raggiunge la velocita' massima (21-24,5 m/s)", vTop > 21f && vTop < 24.5f, $"{vTop:F1} m/s = {vTop * 3.6f:F0} km/h");
            Check("moto: alla velocita' massima il tachimetro e' tra 72 e 90 km/h e la spia del turbo e' spenta", hudM != null && hudM.SpeedoKmh >= 72 && hudM.SpeedoKmh <= 90 && !hudM.SpeedoTurboLit,
                hudM == null ? "nessun HUD" : $"numero {hudM.SpeedoKmh} km/h, turbo {hudM.SpeedoTurboLit}");
            if (shots) Shot("moto_f_in_marcia");
            DesertBike.botBoost = true;
            yield return RideUntil(() => pl.RideSpeed > 27f, 14f, south);
            float vBoost = pl.RideSpeed;
            yield return RideUntil(() => false, 2.0f, south);
            Check("moto: col turbo supera i 27 m/s senza arrivare a 32,5", vBoost > 27f && pl.RideSpeed < 32.5f, $"{vBoost:F1} poi {pl.RideSpeed:F1} m/s");
            Check("moto: col turbo il tachimetro passa i 97 km/h e la spia del turbo e' accesa", hudM != null && hudM.SpeedoKmh > 97 && hudM.SpeedoTurboLit, hudM == null ? "nessun HUD" : $"numero {hudM.SpeedoKmh} km/h, turbo {hudM.SpeedoTurboLit}");
            if (shots) Shot("moto_g_turbo");
            DesertBike.botBoost = false;
            yield return null; yield return null;
            Check("moto: lasciato il turbo la spia si spegne", hudM != null && !hudM.SpeedoTurboLit);

            // ---- sterzo: la moto gira e si inclina dalla parte della curva
            float h0 = pl.RideHeading, bestRoll = 0f;
            DesertBike.botSteer = 1f;
            t0 = Time.time;
            while (Time.time - t0 < 4f && Mathf.Abs(Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, pl.RideHeading * Mathf.Rad2Deg)) < 55f)
            {
                bestRoll = Mathf.Min(bestRoll, bike.RollVisual);
                motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                yield return null;
            }
            float turned = Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, pl.RideHeading * Mathf.Rad2Deg);
            Check("moto: sterzando a destra gira a destra e si inclina a destra", turned > 40f && bestRoll < -8f, $"girata di {turned:F0} gradi, rollio {bestRoll:F1}");
            Check("moto: il manubrio e le ruote sono girati", bike.SteerVisual > 10f, $"{bike.SteerVisual:F0} gradi");
            if (shots) Shot("moto_h_in_curva");
            DesertBike.botSteer = 0f;

            // ---- freno e retromarcia
            DesertBike.botThrottle = -1f;
            t0 = Time.time;
            yield return RideUntil(() => pl.RideSpeed < 1f, 8f, new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading)));
            Check("moto: frenando si ferma in meno di 4 s", pl.RideSpeed < 1f && Time.time - t0 < 4f, $"{pl.RideSpeed:F1} m/s dopo {Time.time - t0:F1} s");
            yield return RideUntil(() => pl.RideSpeed < -2f, 5f, new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading)));
            Check("moto: tenendo il freno va in retromarcia", pl.RideSpeed < -2f, $"{pl.RideSpeed:F1} m/s");
            yield return null; yield return null;
            Check("moto: in retromarcia il tachimetro lo dice (R) e conta la velocita' senza segno", hudM != null && hudM.SpeedoReverseShown && hudM.SpeedoKmh >= 6, hudM == null ? "nessun HUD" : $"numero {hudM.SpeedoKmh} km/h, R {hudM.SpeedoReverseShown}");
            DesertBike.botThrottle = 0f;
            yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 0.5f, 6f, south);
            Check("moto: lasciato il gas si ferma da sola", Mathf.Abs(pl.RideSpeed) < 0.5f, $"{pl.RideSpeed:F2} m/s");

            // ---- freno a mano: la velocita' resta dov'era e la moto si mette di traverso
            Vector3 dirNow = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
            DesertBike.botThrottle = 1f;
            yield return RideUntil(() => pl.RideSpeed > 14f, 10f, dirNow);
            float slipMax = 0f; Vector3 last = pl.transform.position;
            DesertBike.botSteer = 1f; DesertBike.botHandbrake = true;
            t0 = Time.time;
            while (Time.time - t0 < 1.6f && pl.Riding)
            {
                yield return null;
                Vector3 now = pl.transform.position, v = now - last; v.y = 0f; last = now;
                if (v.magnitude > 0.002f)
                {
                    float hh = pl.RideHeading;
                    slipMax = Mathf.Max(slipMax, Vector3.Angle(v, new Vector3(Mathf.Sin(hh), 0f, Mathf.Cos(hh))));
                }
            }
            DesertBike.botHandbrake = false; DesertBike.botSteer = 0f; DesertBike.botThrottle = 0f;
            Check("moto: col freno a mano in curva la moto deraPa (la velocita' non segue la rotta)", slipMax > 6f, $"scarto massimo {slipMax:F1} gradi");
            yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 1f, 8f, south);

            // ---- urto contro il muro della cattedrale: danno e arresto
            pl.AbortRide();
            GroundAt(14.2f, 4.0f, out float wy);
            bike.Park(new Vector3(14.2f, wy, 4.0f), 0f);              // verso nord, contro il muro del nartece (z = 17,85)
            // la sabbia davanti deve avere il suo collider: altrimenti il governatore della moto (per progetto, DesertTerrain.Ready) la tiene a 7 m/s e l'urto non c'e' da misurare. Dopo la gita
            // lontana della fase minimappa (che precede questa) i blocchi attorno alla cattedrale si stanno rifacendo: la prima volta che le due fasi giravano insieme il controllo e' fallito cosi'
            yield return WaitFor(() => DesertTerrain.I == null || DesertTerrain.I.Ready(new Vector3(14.2f, 0f, 30f), 20f), 30f);
            godMode = false; pl.invulnerable = false;
            yield return null;
            float hpBefore = pl.hp + pl.shield;
            pl.Mount(bike);
            yield return null;
            DesertBike.botThrottle = 1f;
            float vWall = 0f, tWall = Time.time;
            while (pl.Riding && pl.RideCrashCooldown <= 0f && Time.time - tWall < 9f) { vWall = Mathf.Max(vWall, pl.RideSpeed); yield return null; }
            DesertBike.botThrottle = 0f;
            yield return new WaitForSeconds(0.4f);
            bool wallReady = DesertTerrain.I == null || DesertTerrain.I.Ready(new Vector3(14.2f, 0f, 30f), 20f);
            Check("moto: contro il muro a piu' di 50 km/h c'e' l'urto, che ferisce", pl.RideCrashCooldown > 0f || pl.hp + pl.shield < hpBefore - 3f, $"vita {hpBefore:F0} -> {pl.hp + pl.shield:F0}, al massimo {vWall * 3.6f:F0} km/h, sabbia davanti pronta {wallReady}");
            Check("moto: dopo l'urto la moto e' ferma o quasi", Mathf.Abs(pl.RideSpeed) < 6f, $"{pl.RideSpeed:F1} m/s");
            Check("moto: l'urto non uccide con la vita piena", pl.alive && !pl.Downed, $"vita {pl.hp:F0}");
            godMode = true; pl.invulnerable = true;
            DesertBike.BotReset();
            if (pl.Riding) pl.AbortRide();

            // ---- in aria e atterraggio (a mezz'aria il terreno non c'e': si vola e si ricade)
            yield return GoTo(home.x - 4f, home.z - 6f, 2f);
            bike.Park(pl.transform.position, Mathf.PI);
            pl.Mount(bike);
            yield return new WaitForSeconds(0.5f);
            var up = pl.transform.position + Vector3.up * 7f;
            pl.Teleport(up);
            yield return null; yield return null;
            Check("moto: sollevata nel vuoto stacca le ruote da terra", !pl.RideGrounded);
            yield return WaitFor(() => pl.RideGrounded, 4f);
            Check("moto: ricade e atterra", pl.RideGrounded && Mathf.Abs(Gap(bike)) < 0.6f, $"scarto {Gap(bike):F2} m");
            yield return new WaitForSeconds(0.4f);
            if (pl.Riding) { DesertBike.botThrottle = 0f; yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 0.6f, 4f, south); }

            // ---- fari di notte
            var sky = DesertSky.I;
            if (sky != null)
            {
                sky.SetHour(23f, true);
                yield return new WaitForSeconds(1.2f);
                Check("moto: di notte il faro si accende", bike.HeadlightOn);
                if (shots)
                {
                    DesertBike.botThrottle = 1f;
                    yield return RideUntil(() => pl.RideSpeed > 12f, 8f, south);
                    Shot("moto_i_notte_in_marcia");
                    DesertBike.botThrottle = 0f;
                }
                sky.SetHour(12f, true);
                yield return new WaitForSeconds(1.2f);
                Check("moto: di giorno il faro e' spento", !bike.HeadlightOn);
                sky.Frozen = false;
            }

            // ---- scendere frenando da soli
            if (pl.Riding)
            {
                DesertBike.botThrottle = 1f;
                yield return RideUntil(() => pl.RideSpeed > 12f, 8f, south);
                pl.RideRequestDismount();
                DesertBike.botThrottle = 0f;
                yield return WaitFor(() => !pl.Riding, 10f);
            }
            else
            {
                Check("moto: in sella per la prova della discesa", false);
            }
            Check("moto: scende da sola appena si ferma", !pl.Riding && bike.rider == null);
            yield return null; yield return null;
            float away = Flat(pl.transform.position, bike.transform.position);
            Check("moto: il pilota scende di lato, a un metro o poco piu', in uno spazio libero", away > 0.7f && away < 2.4f && !Physics.CheckCapsule(pl.transform.position + Vector3.up * 0.45f, pl.transform.position + Vector3.up * 1.3f, 0.3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore), $"{away:F2} m");
            Check("moto: da parcheggiata torna ad avere il collider, l'arma torna in mano", bike.ColliderOn && pl.weapon.ViewRootT.gameObject.activeSelf);
            Check("moto: da scesi si cammina di nuovo (velocita' del corpo zero)", pl.SpeedH < 0.5f);
            yield return null; yield return null;
            Check("moto: scesi, il tachimetro sparisce", hudM != null && !hudM.SpeedoShown);
            yield return WaitFor(() => hudM != null && hudM.DesertMapBikeShown, 3f);
            yield return null; yield return null;
            var dotWant2 = BikeDotWant(pl, bike);
            Check("moto: scesi, il tondo e' di nuovo sulla minimappa, dove l'hai lasciata", hudM != null && hudM.DesertMapBikeShown && (hudM.DesertMapBikeAt - dotWant2).magnitude < 0.3f, hudM == null ? "nessun HUD" : $"visibile {hudM.DesertMapBikeShown}, a {hudM.DesertMapBikeAt}, attesa a {dotWant2}");

            // ---- il percorso lungo: il deserto si costruisce davanti, la moto non cade nel vuoto e non si blocca
            yield return GoTo(home.x, home.z - 4f, 2f);
            bike.Park(pl.transform.position, Mathf.PI);
            pl.Mount(bike);
            yield return new WaitForSeconds(0.6f);
            Vector3[] legs = { new Vector3(0f, 0f, -380f), new Vector3(380f, 0f, 0f), new Vector3(0f, 0f, 380f) };
            Vector3 cur = pl.transform.position, start = cur;
            float tRoute = Time.time, maxDrop = 0f, worstBefore = motoWorst; motoWorst = 0f;
            int arrived = 0;
            DesertBike.botThrottle = 1f;
            for (int leg = 0; leg < legs.Length && pl.Riding; leg++)
            {
                Vector3 target = cur + legs[leg];
                DesertBike.botBoost = leg == legs.Length - 1;
                float lt0 = Time.time;
                while (pl.Riding && Time.time - lt0 < 45f)
                {
                    Vector3 pos = pl.transform.position;
                    if (Flat(pos, target) < 25f) { arrived++; break; }
                    Vector3 want = target - pos; want.y = 0f;
                    Autopilot(want.normalized);
                    float ground = DesertWorld.Height(pos.x, pos.z);
                    maxDrop = Mathf.Max(maxDrop, ground - pos.y);
                    motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                    yield return null;
                }
                cur = pl.transform.position;
            }
            float routeSecs = Time.time - tRoute;
            float routeDist = 0f;
            Vector3 d3 = cur - start; routeDist = new Vector2(d3.x, d3.z).magnitude;
            Debug.Log($"[PryingEyes] desertotest moto percorso: {arrived}/{legs.Length} tratti, {routeSecs:F0} s, {DesertTerrain.I?.ChunkCount} blocchi di sabbia, {Dd?.LoadedSites} luoghi caricati, il fotogramma peggiore {motoWorst * 1000f:F0} ms (prima {worstBefore * 1000f:F0})");
            Check("moto: percorre i tre tratti (oltre un chilometro) in sella", arrived == legs.Length && pl.Riding, $"{arrived}/{legs.Length}, a {routeDist:F0} m dal via in linea d'aria, {routeSecs:F0} s");
            Check("moto: lungo il percorso non cade mai sotto la sabbia", maxDrop < 6f, $"{maxDrop:F1} m sotto il terreno al massimo");
            Check("moto: lungo il percorso nessun fotogramma oltre mezzo secondo (i blocchi nascono in tempo)", motoWorst < 0.5f, $"{motoWorst * 1000f:F0} ms");
            DesertBike.botBoost = false; DesertBike.botThrottle = 0f;
            yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 1f, 8f, south);

            // ---- morire in sella lontano da casa: chi va a terra e' sbalzato e la moto resta li' dov'e'; chi rinasce alla scalinata la ritrova dov'era nata
            Vector3 farAt = bike.transform.position;
            float awayHome = Flat(farAt, bike.home);
            if (pl.Riding)
            {
                godMode = false; pl.invulnerable = false;
                pl.Hurt(99999f);
                yield return null; yield return null;
                Check("moto: andando a terra in sella si e' sbalzati e la moto resta dov'e'", pl.Downed && !pl.Riding && bike.rider == null && Flat(bike.transform.position, farAt) < 2f, $"a terra {pl.Downed}, in sella {pl.Riding}, spostata di {Flat(bike.transform.position, farAt):F1} m");
                pl.BleedOutNow();
                yield return null; yield return null;
                yield return WaitFor(() => pl.alive, 12f);
                pl.invulnerable = true;
                yield return new WaitForSeconds(0.3f);
                Check("moto: chi rinasce alla scalinata ritrova la sua moto dov'era nata", awayHome > 100f && Flat(bike.transform.position, bike.home) < 0.6f && bike.rider == null && bike.ColliderOn, $"lasciata a {awayHome:F0} m da casa, ora a {Flat(bike.transform.position, bike.home):F1} m");
                yield return new WaitForSeconds(2.8f);                    // la tregua della rinascita finisce
                pl.invulnerable = true; godMode = true;
            }
            else Check("moto: in sella alla fine del percorso per la prova della morte", false);

            // ---- la moto di un compagno: senza pilota vero, posata dalla rete (cooperativa), non si monta
            yield return GoTo(home.x + 14f, home.z - 22f, 2f);
            Vector3 pp = pl.transform.position + new Vector3(5f, 0f, 0f);
            GroundAt(pp.x, pp.z, out float py); pp.y = py;
            var pup = DesertBike.Create(null, pp, 0f, "Moto di un compagno");
            pup.SetPuppet(true);
            // a second of game time, not a number of frames: without a window a frame lasts a millisecond and the roll (a smoothing in time) would hardly start
            for (float t = 0f; t < 1.2f; t += Mathf.Max(Time.deltaTime, 0.001f)) { pup.Pose(pp, 0f, true, new Vector3(0f, 0f, 18f), 0.5f, 1f, false, false, false, Mathf.Max(Time.deltaTime, 0.001f)); yield return null; }
            Check("moto: quella di un compagno non si monta e non ha collider", pup.puppet && pup.Prompt == null && !pup.ColliderOn);
            Check("moto: quella di un compagno in corsa ha le ruote che girano e si inclina", Mathf.Abs(pup.WheelSpin) > 0.1f && pup.RollVisual < -3f, $"ruota {pup.WheelSpin:F0}, rollio {pup.RollVisual:F1}");
            if (shots)
            {
                if (sky != null) sky.SetHour(17f, true);
                for (float t = 0f; t < 1.8f; t += Mathf.Max(Time.deltaTime, 0.001f))
                {
                    pup.Pose(pp, 0f, true, new Vector3(0f, 0f, 18f), 0.5f, 1f, false, false, false, Mathf.Max(Time.deltaTime, 0.001f));
                    Face(pup.Point);
                    yield return null;
                }
                Shot("moto_l_compagno_in_corsa");
                if (sky != null) sky.Frozen = false;
            }
            pup.SetPuppet(false);
            UnityEngine.Object.Destroy(pup.gameObject);
            yield return null;
            DesertBike.BotReset();
            godMode = true;
        }

        bool crashSeen; float crashSpeed;

        /// <summary>Una corsa contro il muro del nartece (z = 17,85, a x 14,2) partendo da 'runUp' metri: dice se l'urto e' stato contato (crashSeen) e la velocita' massima prima del muro (crashSpeed).</summary>
        IEnumerator WallRun(DesertBike bike, float runUp)
        {
            var pl = Pl;
            if (pl.Riding) pl.AbortRide();
            float z0 = 17.85f - runUp;
            GroundAt(14.2f, z0, out float y0);
            bike.Park(new Vector3(14.2f, y0, z0), 0f);
            yield return null;
            pl.Mount(bike);
            yield return null;
            crashSeen = false; crashSpeed = 0f;
            DesertBike.botThrottle = 1f;
            float t0 = Time.time;
            while (pl.Riding && Time.time - t0 < 5f)
            {
                crashSpeed = Mathf.Max(crashSpeed, pl.RideSpeed);
                if (pl.RideCrashCooldown > 0f) { crashSeen = true; break; }
                if (Time.time - t0 > 0.6f && pl.RideSpeed < 0.3f) break;                       // si e' fermata contro il muro senza urto
                yield return null;
            }
            DesertBike.botThrottle = 0f;
            yield return new WaitForSeconds(0.25f);
            if (pl.Riding) pl.AbortRide();
        }

        /// <summary>-desertonly motourto: lo stesso muro preso piu' volte a tre velocita'. L'urto non deve dipendere da come i fotogrammi spezzano la frenata: il corpo tocca il muro a meta' di un fotogramma e si
        /// ferma nel successivo, e una perdita di velocita' contata un fotogramma alla volta mancava l'urto in una corsa su sei a 50 km/h (e piu' spesso a 36). Sotto gli 8 m/s, invece, un muro e' solo un
        /// muro: nessun urto.</summary>
        IEnumerator MotoCrashes()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("urti: c'e' una moto", bike != null);
            if (bike == null) yield break;
            yield return WaitFor(() => DesertTerrain.I == null || DesertTerrain.I.Ready(new Vector3(14.2f, 0f, 20f), 24f), 30f);
            // partenze (m dal muro) e velocita' attese al muro (m/s): 2,5 -> 6,9 | 5,9 -> 10 | 13,85 -> 13,6
            float[] runUps = { 2.5f, 5.9f, 13.85f };
            string[] names = { "25 km/h", "36 km/h", "49 km/h" };
            const int n = 6;
            var hits = new int[3]; var vmin = new[] { 99f, 99f, 99f }; var vmax = new float[3];
            for (int k = 0; k < 3; k++)
                for (int i = 0; i < n; i++)
                {
                    yield return WallRun(bike, runUps[k]);
                    if (crashSeen) hits[k]++;
                    vmin[k] = Mathf.Min(vmin[k], crashSpeed); vmax[k] = Mathf.Max(vmax[k], crashSpeed);
                }
            DesertBike.BotReset();
            Check($"urti: a {names[0]} contro il muro (da {vmin[0]:F1} a {vmax[0]:F1} m/s) non c'e' urto, in nessuna delle {n} corse", hits[0] == 0, $"{hits[0]} urti contati su {n}");
            Check($"urti: a {names[1]} (da {vmin[1]:F1} a {vmax[1]:F1} m/s) l'urto c'e' in tutte le {n} corse", hits[1] == n, $"{hits[1]} urti contati su {n}");
            Check($"urti: a {names[2]} (da {vmin[2]:F1} a {vmax[2]:F1} m/s) l'urto c'e' in tutte le {n} corse", hits[2] == n, $"{hits[2]} urti contati su {n}");
            Debug.Log($"[PryingEyes] desertotest motourto: urti contati {hits[0]}/{n} a {names[0]}, {hits[1]}/{n} a {names[1]}, {hits[2]}/{n} a {names[2]}");
            godMode = true;
        }

        /// <summary>Guida verso un punto, senza schivare niente (dentro la cattedrale le antenne dell'autopilota vedrebbero sempre un muro): il manubrio punta al bersaglio e il gas tiene 'speed' m/s.</summary>
        IEnumerator RideTo(Vector3 target, float tol, float timeout, float speed)
        {
            var pl = Pl;
            float t0 = Time.time;
            Vector3 prevPos = pl.transform.position;
            while (pl.Riding && Time.time - t0 < timeout)
            {
                // un corpo in sella che salta di piu' di quindici metri in un fotogramma non e' una corsa: lo si scrive (una volta, in una catena di otto fasi, il pilota e' stato trovato a 190 m dal portale)
                Vector3 nowPos = pl.transform.position;
                if ((nowPos - prevPos).sqrMagnitude > 225f)
                    Debug.Log($"[PryingEyes] desertotest SALTO in sella: da {prevPos:F1} a {nowPos:F1} in un fotogramma (dt {Time.unscaledDeltaTime * 1000f:F0} ms, {pl.RideSpeed:F1} m/s, scala del tempo {Time.timeScale:F2}, verso {target:F1}, a {Time.time - t0:F1} s)");
                prevPos = nowPos;
                Vector3 want = target - pl.transform.position; want.y = 0f;
                if (want.magnitude < tol) break;
                float diff = Mathf.DeltaAngle(pl.RideHeading * Mathf.Rad2Deg, Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg);
                DesertBike.botSteer = Mathf.Clamp(diff / 25f, -1f, 1f);
                float v = Mathf.Abs(diff) > 45f ? Mathf.Min(speed, 3f) : speed;
                DesertBike.botThrottle = pl.RideSpeed < v ? 1f : (pl.RideSpeed > v + 2f ? -0.3f : 0f);
                motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                yield return null;
            }
            DesertBike.botThrottle = 0f; DesertBike.botSteer = 0f;
        }

        /// <summary>-desertonly motocattedrale: la moto dentro la cattedrale (il documento diceva 'non provato'): a mezzogiorno, dalla moto parcheggiata si sale la scalinata del sagrato, si passa dal portale (x 16,4-19,6, z 18) nel nartece,
        /// dove il tetto accende il faro anche di giorno, e si percorre la navata (senza le Volte non ha tetto) fino a quello che la sbarra, si fa dietrofront, si esce e si scende sul sagrato. Con -desertshots, quattro foto dalla sella.</summary>
        IEnumerator MotoCathedral()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            motoWorst = 0f;
            var sk = DesertSky.I;
            if (sk != null) sk.SetHour(12f, true);          // di giorno: se il faro si accende nel nartece e' per il tetto, non per la notte
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("cattedrale in moto: c'e' la moto", bike != null);
            if (bike == null) yield break;
            Vector3 home = bike.transform.position;
            yield return GoTo(home.x - 2.0f, home.z + 0.2f, 2f);
            yield return new WaitForSeconds(0.4f);
            pl.Mount(bike);
            yield return new WaitForSeconds(0.7f);
            string trail = "";
            void Mark(string what) { var p = pl.transform.position; trail += $" {what}({p.x:F1},{p.y:F1},{p.z:F1})"; }
            // la scalinata (x 11-25, dal piede a z -5,8 al sagrato a z 0) e il sagrato
            yield return RideTo(new Vector3(21.5f, 0f, -2f), 3f, 15f, 9f); Mark("scala");
            yield return RideTo(new Vector3(18f, 0f, 9f), 3f, 20f, 9f); Mark("sagrato");
            Vector3 pos = pl.transform.position;
            Check("cattedrale in moto: sale la scalinata e arriva sul sagrato (z oltre 6, a quota del sagrato)", pl.Riding && pos.z > 6f && pos.y > -0.8f, $"a ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}):{trail}");
            if (shots) Shot("moto_cat_a_sagrato");
            // il portale e il nartece (x 12-24, z 18-24)
            yield return RideTo(new Vector3(18f, 0f, 21f), 1.2f, 15f, 5f); Mark("nartece");
            pos = pl.transform.position;
            Check("cattedrale in moto: passa dal portale ed entra nel nartece", pl.Riding && pos.z > 19f && Mathf.Abs(pos.x - 18f) < 4f, $"a ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}):{trail}");
            // ferma sotto il tetto a botte del nartece (c'e' sempre, con o senza le opere): il faro si accende anche a mezzogiorno
            float tb = Time.time;
            while (pl.Riding && Time.time - tb < 4f && pl.RideSpeed > 0.4f) { DesertBike.botThrottle = -1f; DesertBike.botSteer = 0f; yield return null; }
            DesertBike.botThrottle = 0f;
            yield return WaitFor(() => sk != null && sk.Indoor > 0.5f, 3f);
            yield return new WaitForSeconds(0.2f);
            Check("cattedrale in moto: sotto il tetto del nartece, a mezzogiorno, il faro e' acceso", sk != null && sk.Indoor > 0.5f && sk.Night < 0.28f && bike.HeadlightOn, $"faro {bike.HeadlightOn}, al chiuso {(sk != null ? sk.Indoor : -1f):F2}, notte {(sk != null ? sk.Night : -1f):F2}");
            if (shots) Shot("moto_cat_b_nartece");
            // la navata (x 12-24, z 24-54): oltre l'arco a z 24 e fin dove una cosa in mezzo la sbarra (per chiunque, a piedi come in moto): la prova dice cosa
            yield return RideTo(new Vector3(18f, 0f, 44f), 2f, 25f, 6f); Mark("navata");
            pos = pl.transform.position;
            Check("cattedrale in moto: percorre la navata dopo l'arco (z oltre 33)", pl.Riding && pos.z > 33f, $"a ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}):{trail}");
            {
                // cosa ha davanti, a pochi metri: il nome dell'oggetto e dei suoi genitori
                float yaw = pl.RideHeading * Mathf.Rad2Deg;
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, from = pl.transform.position + Vector3.up * 0.8f;
                string blocker = "niente a 2,5 m";
                if (Physics.SphereCast(from, 0.25f, fwd, out var ahead, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    blocker = ahead.collider.name; var tr = ahead.collider.transform.parent;
                    for (int up = 0; up < 3 && tr != null; up++, tr = tr.parent) blocker += " < " + tr.name;
                    blocker += $" a {ahead.distance:F1} m";
                }
                Debug.Log($"[PryingEyes] desertotest motocattedrale: in navata la moto e' ferma a z {pos.z:F1}, davanti: {blocker}");
            }
            // la navata senza le Volte non ha tetto (al chiuso 0): di giorno il faro si spegne; con le Volte seguirebbe il tetto. Qui vale la regola, qualunque sia la pianta
            yield return WaitFor(() => sk == null || sk.Indoor < 0.5f, 3f);
            yield return new WaitForSeconds(0.3f);
            bool dark = sk != null && (sk.Night > 0.28f || sk.Indoor > 0.5f);
            Check("cattedrale in moto: in navata il faro segue il tetto (acceso solo se c'e' o se e' notte)", bike.HeadlightOn == dark, $"faro {bike.HeadlightOn}, al chiuso {(sk != null ? sk.Indoor : -1f):F2}, notte {(sk != null ? sk.Night : -1f):F2}");
            if (shots) Shot("moto_cat_c_navata");
            // dietrofront (si gira in un punto largo: i 12 m della navata) e fuori
            yield return RideTo(new Vector3(18f, 0f, 28f), 3f, 20f, 4f); Mark("ritorno_navata");
            yield return RideTo(new Vector3(18f, 0f, 21f), 2.5f, 15f, 5f); Mark("ritorno_nartece");
            if (shots) Shot("moto_cat_d_ritorno");
            yield return RideTo(new Vector3(18f, 0f, 12f), 3f, 15f, 6f); Mark("ritorno_sagrato");
            pos = pl.transform.position;
            Check("cattedrale in moto: dopo il dietrofront esce dal portale sul sagrato", pl.Riding && pos.z < 16f, $"a ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}):{trail}");
            DesertBike.botThrottle = 0f; DesertBike.botSteer = 0f;
            yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 0.6f, 6f, new Vector3(0f, 0f, -1f));
            pl.RideRequestDismount();
            yield return WaitFor(() => !pl.Riding, 10f);
            Check("cattedrale in moto: scende sul sagrato e la moto resta li' parcheggiata", !pl.Riding && bike.rider == null && bike.ColliderOn, $"in sella {pl.Riding}");
            Check("cattedrale in moto: nessun fotogramma oltre mezzo secondo", motoWorst < 0.5f, $"{motoWorst * 1000f:F0} ms");
            if (sk != null) sk.Frozen = false;
            DesertBike.BotReset();
            godMode = true;
        }

        /// <summary>-desertonly motosuono: i suoni a codice della moto (il loop del motore, l'urto, il vento di corsa) scritti come WAV in unity_logs/moto per misurarli e ascoltarli fuori dal gioco.</summary>
        IEnumerator MotoSound()
        {
            yield return null;
            var eng = SoundGen.Engine(31); var crash = SoundGen.BikeCrash(32); var wind = SoundGen.SandHiss(21);
            WriteWav("unity_logs/moto/motore.wav", eng); WriteWav("unity_logs/moto/urto.wav", crash); WriteWav("unity_logs/moto/vento.wav", wind);
            Check("moto: i tre suoni a codice (motore, urto, vento) hanno i campioni", eng.Length > 40000 && crash.Length > 20000 && wind.Length > 40000, $"{eng.Length} / {crash.Length} / {wind.Length}");
            // misure sui campioni (non ho ascoltato niente: queste sono le cose che un orecchio noterebbe subito e un numero vede)
            float peak = 0f; double sum = 0;
            foreach (var v in eng) { peak = Mathf.Max(peak, Mathf.Abs(v)); sum += v; }
            float dc = (float)(sum / eng.Length);
            Check("moto: il motore non satura e non ha componente continua", peak <= 0.95f && peak > 0.3f && Mathf.Abs(dc) < 0.005f, $"picco {peak:F2}, continua {dc:F4}");
            var steps = new float[eng.Length - 1];
            for (int i = 0; i < steps.Length; i++) steps[i] = Mathf.Abs(eng[i + 1] - eng[i]);
            Array.Sort(steps);
            float typical = steps[(int)(steps.Length * 0.995f)], seam = Mathf.Abs(eng[0] - eng[eng.Length - 1]);
            Check("moto: il loop del motore si chiude senza scatti (il salto alla giuntura non supera il passo tipico)", seam <= typical * 1.5f, $"giuntura {seam:F4}, passo tipico {typical:F4}");
            float e80 = EnergyAbove(eng, 80f), e150 = EnergyAbove(eng, 150f);
            Check("moto: il motore si sente da un altoparlante piccolo (oltre il 55 % dell'energia sopra gli 80 Hz e il 25 % sopra i 150, al tono nativo: la prima versione aveva il 34 % e il 7 %)", e80 > 0.55f && e150 > 0.25f, $"sopra 80 Hz {e80 * 100f:F0} %, sopra 150 Hz {e150 * 100f:F0} %");
            float c150 = EnergyAbove(crash, 150f);
            Check("moto: l'urto non e' solo un tonfo grave (oltre il 18 % dell'energia sopra i 150 Hz: la prima versione aveva il 2 %)", c150 > 0.18f, $"sopra 150 Hz {c150 * 100f:F0} %");
        }

        /// <summary>Quanta parte dell'energia sta sopra 'hz' (spettro di potenza di una finestra di Hann sui campioni, FFT a 2^17 punti).</summary>
        static float EnergyAbove(float[] s, float hz)
        {
            int n = 1 << 17, m = Mathf.Min(s.Length, n);
            var re = new double[n]; var im = new double[n];
            for (int i = 0; i < m; i++) re[i] = s[i] * (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (m - 1)));
            Fft(re, im);
            double tot = 0, above = 0;
            for (int k = 1; k < n / 2; k++)
            {
                double p = re[k] * re[k] + im[k] * im[k];
                tot += p;
                if (k * (double)SoundGen.Rate / n >= hz) above += p;
            }
            return tot > 0 ? (float)(above / tot) : 0f;
        }

        /// <summary>FFT iterativa a base 2 sul posto.</summary>
        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
        }

        static void WriteWav(string path, float[] s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var f = new BinaryWriter(File.Create(path)))
            {
                var ascii = System.Text.Encoding.ASCII;
                int bytes = s.Length * 2;
                f.Write(ascii.GetBytes("RIFF")); f.Write(36 + bytes); f.Write(ascii.GetBytes("WAVEfmt "));
                f.Write(16); f.Write((short)1); f.Write((short)1); f.Write(SoundGen.Rate); f.Write(SoundGen.Rate * 2); f.Write((short)2); f.Write((short)16);
                f.Write(ascii.GetBytes("data")); f.Write(bytes);
                foreach (var v in s) f.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
        }
    }
}
#endif
