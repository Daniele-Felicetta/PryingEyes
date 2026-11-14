using System;
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -gunplaytest: regressioni sull'arsenale tramite Tick e interazioni reali.</summary>
    public sealed class GunplayTest : MonoBehaviour
    {
        int checks, failures, exceptions;
        Weapon weapon;
        GameManager gm;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-gunplaytest") >= 0)
                new GameObject("GunplayTest").AddComponent<GunplayTest>();
        }

        void Awake() => Application.logMessageReceived += OnLog;
        void OnDestroy() => Application.logMessageReceived -= OnLog;
        void OnLog(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }
        void Check(string name, bool ok)
        {
            checks++;
            if (!ok) { failures++; Debug.Log("[PryingEyes] gunplaytest FALLITO " + name); }
        }

        IEnumerator Start()
        {
            var run = Run();
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("copione completo", false); break; }
                if (!more) break;
                yield return run.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] gunplaytest RISULTATO {(failures == 0 ? "SUPERATA" : "FALLITA")}: {checks - failures}/{checks}");
            Application.Quit(failures == 0 ? 0 : 1);
        }

        static void FireTouch(TouchPhase phase)
        {
            var area = Screen.safeArea;
            TouchControls.ProcessTouches(new[] { new Touch { fingerId = 77, phase = phase,
                position = new Vector2(area.x + area.width * 1200f / 1280f, area.yMax - area.height * 520f / 720f) } });
        }

        void Advance(float seconds)
        {
            for (float t = 0; t < seconds; t += 0.02f) weapon.Tick(0.02f, false);
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while ((GameManager.I == null || !GameManager.I.LevelReady) && Time.realtimeSinceStartup < deadline) yield return null;
            gm = GameManager.I;
            Check("livello avviato", gm != null && gm.LevelReady);
            if (!GameManager.proc) { gm.StopAllCoroutines(); gm.waveActive = false; gm.wave = 0; }
            gm.player.enabled = false;
            gm.player.invulnerable = true;
            Enemy.debugFreeze = Enemy.debugNoSense = true;
            Enemy.ClearAll();
            Perks.ResetRun();
            weapon = gm.player.weapon;
            var cam = gm.player.cam;
            // Isola le prove di munizioni dalle presenze del piano, mantenendo controller, arma e HUD reali.
            cam.position = new Vector3(1000f, 1.6f, 1000f); cam.rotation = Quaternion.identity;
            for (int i = 0; i < WeaponDefs.All.Length; i++)
            {
                var d = WeaponDefs.All[i];
                weapon.ResetLoadout(); weapon.Equip(i); Advance(1f);
                var prefab = Resources.Load<GameObject>("Armi/" + d.id);
                var parts = prefab != null ? prefab.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
                Check(d.id + " prefab e agganci", prefab != null && weapon.IsBound &&
                    Array.Exists(parts, t => t.name == "muzzle") && Array.Exists(parts, t => t.name == "ads") &&
                    (d.zoom <= 1.4f || Array.Exists(parts, t => t.name == "lente_oculare")));
                Weapon.debugAds = true; Advance(d.adsTime + 0.1f);
                Check(d.id + " entra in ADS", weapon.ads > 0.99f);
                Weapon.debugAds = false; Advance(1f);
                for (int mode = 0; mode < d.modes.Length; mode++)
                {
                    if (mode > 0) weapon.SendMessage("ToggleMode");
                    weapon.MaxAmmo(); Advance(1f);
                    int initial = weapon.mag;
                    FireTouch(TouchPhase.Began); weapon.Tick(0.02f, false);
                    FireTouch(TouchPhase.Stationary); Advance(1f);
                    FireTouch(TouchPhase.Ended);
                    int fired = initial - weapon.mag;
                    Check(d.id + " grilletto " + weapon.Mode, weapon.Mode == FireMode.Auto ? fired >= 2 :
                        weapon.Mode == FireMode.Burst ? fired == Mathf.Min(d.burst, initial) : fired == 1);
                }
                weapon.mag = 0; weapon.reserve = d.mag + 3;
                int ammo = weapon.mag + weapon.reserve;
                weapon.Reload(); Advance(20f);
                Check(d.id + " ricarica vuota conserva munizioni", !weapon.reloading && weapon.mag == d.mag && weapon.mag + weapon.reserve == ammo);
                weapon.mag = d.mag - 1; weapon.reserve = 3; ammo = weapon.mag + weapon.reserve;
                weapon.Reload(); Advance(20f);
                Check(d.id + " ricarica tattica conserva munizioni", !weapon.reloading && weapon.mag == d.mag && weapon.mag + weapon.reserve == ammo);
                if (d.style == ReloadStyle.Tube)
                {
                    weapon.mag = 0; weapon.reserve = d.reserve;
                    weapon.Reload(); weapon.Tick(0.1f, false); weapon.MaxAmmo(); Advance(20f);
                    Check(d.id + " Reliquiario durante ricarica", weapon.mag == weapon.magSize && weapon.reserve == weapon.reserveMax && !weapon.reloading);
                }
                Check(d.id + " Consacrazione", weapon.Consecrate());
                weapon.reserve = d.reserveMax; // sopra questo limite resta ancora spazio nell'arma consacrata
                var rack = WallBuy.Place(i, 200, cam.position + cam.forward * 2f, Quaternion.Euler(0, 180, 0), Color.white);
                Physics.SyncTransforms();
                gm.AddPoints(10000 - gm.score);
                Check(d.id + " espositore selezionabile", WallBuy.Tick(cam));
                WallBuy.TryBuy();
                Check(d.id + " munizioni consacrate acquistabili", weapon.reserve == weapon.reserveMax && gm.score == 10000 - rack.AmmoPrice);
                int score = gm.score;
                WallBuy.Tick(cam); WallBuy.TryBuy();
                Check(d.id + " riserva piena non costa punti", gm.score == score);
                Destroy(rack.gameObject);
                Debug.Log("[PryingEyes] gunplaytest esaminata " + d.id);
                yield return null;
            }
            weapon.ResetLoadout();
            weapon.Equip(WeaponDefs.IndexOf("bolt_rosario")); Advance(1f); weapon.DebugFire();
            weapon.SwitchSlot(0); Advance(0.5f);
            Check("ciclo del fucile non blocca la pistola", weapon.SlotIndex == 0 && weapon.CanFireNow);
            weapon.ResetLoadout();
            weapon.Equip(WeaponDefs.IndexOf("lmg_campana249")); Advance(1f); weapon.SwitchSlot(0);
            int shots = GameManager.shotsFired;
            FireTouch(TouchPhase.Began); weapon.Tick(0.17f, false); FireTouch(TouchPhase.Ended);
            Check("fine cambio non spara prima dell'estrazione", GameManager.shotsFired == shots);
            CheckHits(cam);
            foreach (string id in new[] { "smg_tarantola", "ar9_malleus" })
                foreach (int fps in new[] { 30, 60, 144 })
                {
                    weapon.ResetLoadout(); weapon.Equip(WeaponDefs.IndexOf(id)); Advance(1f);
                    int count = GameManager.shotsFired;
                    float step = 1f / fps;
                    FireTouch(TouchPhase.Began); weapon.Tick(step, false); FireTouch(TouchPhase.Stationary);
                    for (int frame = 1; frame < fps * 4; frame++) { weapon.mag = 999; weapon.Tick(step, false); }
                    FireTouch(TouchPhase.Ended);
                    int fired = GameManager.shotsFired - count;
                    float expected = 4f * weapon.Def.rpm / 60f;
                    Debug.Log($"[PryingEyes] gunplaytest cadenza {id} {fps} fps: {fired} colpi, attesi circa {expected:F1}");
                    Check(id + " cadenza stabile a " + fps + " fps", Mathf.Abs(fired - expected) <= 1.1f);
                    Fx.Clear(); yield return null;
                }
            weapon.ResetLoadout();
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.transform.position = new Vector3(1000f, -0.5f, 1000f);
            platform.transform.localScale = new Vector3(30f, 1f, 100f);
            Physics.SyncTransforms();
            var player = gm.player;
            player.Teleport(new Vector3(1000f, 0.1f, 1000f)); player.yaw = player.pitch = 0f;
            PlayerController.holdLook = true; player.enabled = true;
            MoveTouches(TouchPhase.Began, false, true);
            yield return null;
            MoveTouches(TouchPhase.Moved, false, true);
            yield return new WaitForSeconds(0.8f);
            Check("sprint raggiunge la velocità prevista", player.IsSprinting && player.SpeedH > 5f);
            MoveTouches(TouchPhase.Stationary, true, true);
            yield return new WaitForSeconds(0.12f);
            // La quota della camera distingue la scivolata dall'accovacciamento normale, anche senza dipendere da campi privati.
            yield return new WaitForSeconds(0.45f);
            Check("sprint più crouch abbassa la camera in scivolata", player.cam.localPosition.y < player.eyeCrouch - 0.04f);
            yield return new WaitForSeconds(0.65f);
            Check("la scivolata termina e il crouch rallenta", player.IsCrouching && player.SpeedH <= player.crouchSpeed + 0.15f);
            MoveTouches(TouchPhase.Stationary, false, true);
            yield return new WaitForSeconds(0.5f);
            MoveTouches(TouchPhase.Stationary, false, false);
            yield return new WaitForSeconds(0.5f);
            Check("rilasciare sprint ripristina la camminata", !player.IsSprinting && player.SpeedH <= player.walk + 0.15f);
            MoveTouches(TouchPhase.Stationary, false, false, 0.5f);
            yield return new WaitForSeconds(0.5f);
            Check("mezzo stick dà metà velocità", player.SpeedH > player.walk * 0.35f && player.SpeedH < player.walk * 0.55f);
            MoveTouches(TouchPhase.Ended, false, false);
            player.enabled = false; Destroy(platform);
        }

        static void MoveTouches(TouchPhase movePhase, bool crouch, bool sprint, float amount = 1f)
        {
            var area = Screen.safeArea;
            Touch Finger(int id, TouchPhase phase, float x, float y) => new Touch { fingerId = id, phase = phase,
                position = new Vector2(area.x + area.width * x / 1280f, area.yMax - area.height * y / 720f) };
            TouchControls.ProcessTouches(new[] {
                Finger(80, movePhase, 100f, movePhase == TouchPhase.Began ? 620f : 620f - 90f * amount),
                Finger(81, sprint ? TouchPhase.Began : TouchPhase.Ended, 200f, 500f),
                Finger(82, crouch ? TouchPhase.Began : TouchPhase.Ended, 1050f, 650f)
            });
        }

        void CheckHits(Transform cam)
        {
            Enemy Target(Vector3 pos)
            {
                var go = new GameObject("Bersaglio balistico");
                go.transform.position = pos; go.layer = HotelNav.EnemyLayer;
                var e = go.AddComponent<Enemy>(); e.enabled = false;
                e.type = Enemy.EnemyType.Monaco; e.hp = e.maxHp = 10000f;
                var body = go.AddComponent<CapsuleCollider>();
                body.height = 1.8f; body.radius = 0.3f; body.center = Vector3.up * 0.9f;
                var head = new GameObject("Testa"); head.layer = HotelNav.EnemyLayer; head.tag = "Head";
                head.transform.SetParent(go.transform, false); head.transform.localPosition = new Vector3(0, 1.6f, -0.2f);
                head.AddComponent<SphereCollider>().radius = 0.2f;
                return e;
            }
            var origin = new Vector3(1000, 0, 1000);
            var a = Target(origin + Vector3.forward * 4f);
            var b = Target(origin + Vector3.forward * 8f);
            cam.position = origin + Vector3.up * 0.8f;
            cam.rotation = Quaternion.identity;
            weapon.ResetLoadout(); Weapon.debugAds = true; Advance(1f);
            Physics.SyncTransforms();
            int points = gm.score;
            weapon.DebugFire();
            Check("hitscan al corpo applica danno e punti", Mathf.Abs(10000f - a.hp - weapon.Def.damage) < 0.01f && gm.score == points + Punti.Colpo);
            Check("arma senza penetrazione non colpisce il secondo", b.hp == 10000f);
            float hp = a.hp;
            cam.position = origin + Vector3.up * 1.6f; Advance(1f); Physics.SyncTransforms();
            weapon.DebugFire();
            Check("testa sovrapposta alla capsula dà il moltiplicatore", Mathf.Abs(hp - a.hp - weapon.Def.damage * weapon.Def.headMult) < 0.01f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = origin + Vector3.forward * 2f + Vector3.up;
            wall.transform.localScale = new Vector3(2f, 2f, 0.1f);
            cam.position = origin + Vector3.up * 0.8f; Physics.SyncTransforms();
            hp = a.hp; weapon.DebugFire();
            Check("muro interrompe il colpo", a.hp == hp);
            wall.SetActive(false);
            weapon.Equip(WeaponDefs.IndexOf("bolt_rosario")); Advance(1f); Physics.SyncTransforms();
            hp = a.hp; weapon.DebugFire();
            Check("penetrazione applica il danno ai due bersagli", Mathf.Abs(hp - a.hp - weapon.Def.damage) < 0.01f &&
                Mathf.Abs(10000f - b.hp - weapon.Def.damage * weapon.Def.pierceMult) < 0.05f);
            Weapon.debugAds = false;
            Destroy(wall); Destroy(a.gameObject); Destroy(b.gameObject);
        }
    }
}
