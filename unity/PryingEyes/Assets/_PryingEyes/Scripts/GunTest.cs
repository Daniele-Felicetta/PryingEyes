#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using Production.Weapons;
using UnityEngine;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -guntest [-armi id1,id2] [-fxtest] [-espositori]: la rassegna delle armi. Per ognuna foto in mano, in mira, allo sparo e a meta' ricarica,
    /// piu' i numeri misurati (colpi in 4 s, cadenza, rinculo massimo; le dispersioni sono quelle della scheda): foto in hotel_build/armi_gioco/, numeri in guntest.txt.
    /// Il bot non spara e i nemici si tolgono prima di ogni arma. Era una procedura di GameManager.</summary>
    public sealed class GunTest : PlayerTest
    {
        protected override string Suite { get { return "guntest"; } }
        /// <summary>Il bot di -autotest non spara: la prova usa l'arma da sola (era il flag di questa prova in una lista di GameManager).</summary>
        protected override bool HoldFire { get { return true; } }
        /// <summary>La procedura non ha mai contato le eccezioni, e ogni foto che scatta senza scheda grafica (-nographics) fa lanciare a URP la NullReferenceException del render graph (121 in una rassegna intera, le stesse prima del porting): i controlli restano quelli che ha sempre avuto.</summary>
        protected override string ExceptionCheck { get { return null; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<GunTest>("-guntest"); }

        /// <summary>Per le prove: ferma la rotazione automatica dell'autotest e guarda dove c'e' piu' spazio (foto con profondita',
        /// niente muro a 30 cm, e il muro di prova del lean resta dove l'ho messo). Restituisce lo spazio libero in quella direzione.</summary>
        internal static float LookOpenest(PlayerController player)
        {
            PlayerController.holdLook = true;
            var c = player.cam;
            float bestD = 0f, bestA = 0f;
            for (int a = 0; a < 24; a++)
            {
                float ang = a * Mathf.PI * 2f / 24f;
                var dv = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                float dd = 8f;
                if (Physics.Raycast(c.position, dv, out RaycastHit rh, 8f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) dd = rh.distance;
                if (dd > bestD) { bestD = dd; bestA = ang; }
            }
            player.yaw = bestA; player.pitch = 0f;
            return bestD;
        }

        protected override IEnumerator Run()
        {
            var gm = GameManager.I;
            while (!gm.LevelReady || Time.timeSinceLevelLoad < 2f) yield return null;      // GameManager la faceva partire dopo 2 s di prova automatica
            var player = gm.player;
            string dir = TestPaths.Out("armi_gioco");
            System.IO.Directory.CreateDirectory(dir);
            string log = dir + "/guntest.txt";
            System.IO.File.WriteAllText(log, "id grado classe colpi_in_4s rpm_misurata kick_max_gradi dispersione_mano dispersione_mira\n");
            string only = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-armi") only = args[i + 1];
            Enemy.ClearAll();
            var w = player.weapon;
            var cam = player.cam;
            Cursor.lockState = CursorLockMode.None;
            float lookOpenDist = LookOpenest(player);   // tutte le foto guardano lungo la linea piu' libera: tracce, fiamme e raggi hanno spazio per vedersi
            int measured = 0;
            for (int idx = 0; idx < WeaponDefs.All.Length; idx++)
            {
                var d = WeaponDefs.All[idx];
                if (only != null && System.Array.IndexOf(only.Split(','), d.id) < 0) continue;
                Weapon.debugAds = false;
                w.Equip(idx);
                player.invulnerable = true;
                // il goccia a goccia dei nemici riempie la scena mentre si prova: fuori tutti prima di ogni arma (le foto devono vedere l'arma)
                Enemy.ClearAll();
                yield return new WaitForSeconds(0.9f);
                GameManager.CamShot(dir + "/" + d.id + "_mano.png", null, true);
                // in mira
                Weapon.debugAds = true;
                yield return new WaitForSeconds(Mathf.Max(0.8f, d.adsTime * 4f));
                w.RenderScopeNow();
                GameManager.CamShot(dir + "/" + d.id + "_mira.png", null, true);
                // allo sparo: fiammata, bossolo, traccia
                Weapon.debugAds = false;
                Enemy.ClearAll();
                yield return new WaitForSeconds(0.5f);
                w.mag = d.mag;
                w.DebugFire();
                yield return null;   // un fotogramma: tracce e raggi nascono orientati solo al primo aggiornamento di Fx
                if (d.fx == "flame")
                {
                    // il getto di fiamme si allunga solo dopo qualche colpo: grilletto tenuto per un terzo di secondo prima della foto
                    float tf = Time.time;
                    while (Time.time - tf < 0.35f) { if (w.CanFireNow) w.DebugFire(); yield return null; }
                }
                if (d.fx == "flame" || d.beam) Debug.Log("[PryingEyes] fxdump " + d.id + " " + Fx.Dump());
                GameManager.CamShot(dir + "/" + d.id + "_sparo.png", null, true);
                yield return new WaitForSeconds(0.4f);
                // ricarica a meta'
                w.mag = Mathf.Max(0, d.mag / 3);
                w.Reload();
                yield return new WaitForSeconds(Mathf.Max(0.3f, d.style == ReloadStyle.Tube ? 0.7f : d.reload * 0.4f));
                GameManager.CamShot(dir + "/" + d.id + "_ricarica.png", null, true);
                yield return new WaitForSeconds(d.reloadEmpty + d.shellTime * 8f + 1f);
                // misura: grilletto tenuto per 4 s con caricatore senza fondo; cadenza = colpi tra il primo e l'ultimo
                w.mag = 999; w.reserve = 999;
                int shots0 = GameManager.shotsFired;
                float t0 = Time.time, tFirst = -1f, tLast = -1f;
                int lastCount = shots0;
                float maxKick = 0f;
                while (Time.time - t0 < 4f)
                {
                    if (w.CanFireNow) w.DebugFire();
                    if (GameManager.shotsFired != lastCount) { lastCount = GameManager.shotsFired; if (tFirst < 0f) tFirst = Time.time; tLast = Time.time; }
                    maxKick = Mathf.Max(maxKick, player.ViewKickPitch);
                    yield return null;
                }
                int fired = GameManager.shotsFired - shots0;
                float rpmMeasured = fired > 1 && tLast > tFirst ? (fired - 1) / (tLast - tFirst) * 60f : 0f;
                System.IO.File.AppendAllText(log, $"{d.id} {d.grade} {d.cls.Replace(' ', '_')} {fired} {rpmMeasured:F0} {maxKick:F1} {d.hip:F2} {d.ads:F2}\n");
                Debug.Log($"[PryingEyes] guntest {d.id}: {fired} colpi in 4 s ({rpmMeasured:F0} al minuto, scheda {d.rpm:F0}), kick massimo {maxKick:F1} gradi");
                Check(d.id + ": spara nella misura di 4 s", fired > 0, $"{fired} colpi, {rpmMeasured:F0} al minuto (scheda {d.rpm:F0}), kick massimo {maxKick:F1} gradi");
                measured++;
                yield return new WaitForSeconds(0.6f);
            }
            Check("almeno un'arma in rassegna", measured > 0, only == null ? measured + " armi" : measured + " armi su -armi " + only);
            Weapon.debugAds = false;
            if (TestRun.Has("-fxtest"))
            {
                // prova degli effetti a quad (fiamma, raggio, traccia) senza arma: foto subito dopo la nascita
                Enemy.ClearAll();
                Vector3 o = cam.position + cam.forward * 0.9f + Vector3.down * 0.25f + cam.right * 0.15f;
                for (int pass = 0; pass < 3; pass++)
                {
                    Fx.Clear();
                    if (pass == 0) for (int i = 0; i < 8; i++) Fx.Flame(o + cam.forward * (i * 0.15f), cam.forward);
                    if (pass == 1) Fx.Beam(o, o + cam.forward * 8f, 0.04f, new Color(1f, 0.6f, 0.1f));
                    if (pass == 2) Fx.Tracer(o + cam.right * 0.3f, o + cam.right * 0.3f + cam.forward * 20f, 30f, 0.03f, new Color(1f, 0.9f, 0.4f));
                    yield return null; yield return null;
                    GameManager.CamShot(dir + "/fxtest" + pass + ".png", null, true);
                    Debug.Log("[PryingEyes] fxtest" + pass + " " + Fx.Dump());
                }
            }
            // espositori: tre armi sul muro davanti al giocatore (come i muri dei piani); con -armi solo se c'e' anche -espositori
            if (only == null || TestRun.Has("-espositori"))
            {
                // i pannelli non devono finire dentro un muro: si guarda dove c'e' piu' spazio
                Enemy.ClearAll();
                lookOpenDist = LookOpenest(player);
                yield return null; yield return null;
                var fwd = new Vector3(cam.forward.x, 0f, cam.forward.z).normalized;
                float place = Mathf.Clamp(lookOpenDist - 0.5f, 1.3f, 2.4f);
                float spread = place < 2f ? 1.25f : 1.55f;
                string[] ids3 = { "p9_pieta", "ar9_malleus", "pu_pupilla" };
                for (int k = 0; k < 3; k++)
                {
                    int wi = WeaponDefs.IndexOf(ids3[k]);
                    Vector3 pos = cam.position + fwd * place + cam.right * ((k - 1) * spread);
                    pos.y = cam.position.y - 0.05f;
                    WallBuy.Place(wi, WeaponDefs.All[wi].price, pos, Quaternion.LookRotation(-fwd), WeaponDefs.All[wi].GradeColor.ToColor());
                }
                yield return new WaitForSeconds(0.6f);
                GameManager.CamShot(dir + "/espositori.png", null, true);
            }
            Debug.Log("[PryingEyes] guntest finito");
        }
    }
}
#endif
