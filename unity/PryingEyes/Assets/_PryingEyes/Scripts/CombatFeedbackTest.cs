using System;
using System.Collections;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -combatfeedbacktest [-combatshots]: danno, direzione e collisioni reali dei dardi.</summary>
    public sealed class CombatFeedbackTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-combatfeedbacktest") >= 0)
                new GameObject("CombatFeedbackTest").AddComponent<CombatFeedbackTest>();
        }
        void Check(string name, bool ok)
        {
            checks++; if (!ok) failed++;
            Debug.Log("[PryingEyes] combatfeedbacktest " + (ok ? "ok " : "FALLITO ") + name);
        }
        void Log(string text, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }
        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            var sequence = Run();
            while (true)
            {
                bool more;
                try { more = sequence.MoveNext(); }
                catch (Exception ex) { Debug.LogException(ex); Check("sequenza completa", false); break; }
                if (!more) break;
                yield return sequence.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] combatfeedbacktest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }
        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            var gm = GameManager.I; var player = gm.player; var hud = gm.hud;
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = true;
            Vector3 original = player.transform.position, cameraPosition = player.cam.position;
            Quaternion cameraRotation = player.cam.rotation;
            player.enabled = false; player.invulnerable = false;
            Vector3 origin = new Vector3(0, 120, 0);
            player.Teleport(origin); player.cam.SetPositionAndRotation(origin + Vector3.up * 1.62f, Quaternion.identity);
            var controller = player.GetComponent<CharacterController>();
            controller.height = player.heightStand;
            player.hp = 100; player.shield = 0;
            Vector3[] directions = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            float[] angles = { 0, -90, 180, 90 };
            for (int i = 0; i < directions.Length; i++)
            {
                player.Hurt(1, origin + directions[i] * 5);
                float shown = hud.transform.Find("DamageDirection" + i).localEulerAngles.z;
                Check("direzione cardinale " + i, Mathf.Abs(Mathf.DeltaAngle(shown, angles[i])) < 1f);
            }
            Check("quattro direzioni contemporanee", hud.DamageDirectionCount == 4);
            player.cam.rotation = Quaternion.Euler(0, 90, 0);
            yield return null;
            Check("indicatore segue la rotazione della visuale", Mathf.Abs(Mathf.DeltaAngle(hud.transform.Find("DamageDirection0").localEulerAngles.z, 90)) < 1f);
            yield return new WaitForSeconds(0.9f);
            Check("gli indicatori scadono", hud.DamageDirectionCount == 0);
            player.invulnerable = true; player.Hurt(10, origin + Vector3.right * 5);
            Check("nessuna falsa direzione durante immunita", hud.DamageDirectionCount == 0);
            player.invulnerable = false;

            MagicBolt Bolt(float height = 1f, bool visual = false)
            {
                var bolt = MagicBolt.Launch(origin + new Vector3(-5, height, 0), origin + new Vector3(5, height, 0), 10, visual);
                bolt.speed = 100; bolt.enabled = false;
                return bolt;
            }
            void Reset() { player.hp = 100; player.shield = 0; }
            Reset(); var projectile = Bolt();
            Check("dardo senza collider e rigidbody ausiliari", projectile.GetComponentsInChildren<Collider>().Length == 0 && projectile.GetComponent<Rigidbody>() == null);
            player.weapon.Equip(WeaponDefs.IndexOf("snp_sentinella"));
            var scope = Array.Find(player.cam.GetComponentsInChildren<Camera>(true), c => c.name == "ScopeCam");
            bool Visible(Renderer renderer) => scope != null && (scope.cullingMask & (1 << renderer.gameObject.layer)) != 0;
            Check("dardo e scia visibili anche dalle ottiche", Array.TrueForAll(projectile.GetComponentsInChildren<Renderer>(), Visible));
            projectile.Advance(0.1f);
            Check("dardo non salta il giocatore con un passo di dieci metri", Mathf.Abs(player.hp - 85) < 0.01f);
            Check("scintille e polvere visibili anche dalle ottiche", Array.TrueForAll(GameObject.Find("Fx").GetComponentsInChildren<Renderer>(), Visible));
            player.weapon.ResetLoadout();
            projectile.Advance(0.1f);
            Check("un dardo infligge un solo danno", Mathf.Abs(player.hp - 85) < 0.01f);
            Reset(); projectile = Bolt(1, true); projectile.Advance(0.1f);
            Check("replica grafica non infligge danni", player.hp == 100);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = origin + new Vector3(-1, 1, 0); wall.transform.localScale = new Vector3(0.08f, 3, 3);
            Physics.SyncTransforms(); Reset(); projectile = Bolt(); projectile.Advance(0.1f);
            Check("parete sottile davanti al giocatore ferma il dardo", player.hp == 100);
            wall.transform.position = origin + new Vector3(1.5f, 1, 0); Physics.SyncTransforms();
            Reset(); projectile = Bolt(); projectile.Advance(0.1f);
            Check("giocatore prima della parete riceve il colpo", Mathf.Abs(player.hp - 85) < 0.01f);
            Destroy(wall); yield return null;
            controller.height = player.heightCrouch;
            Reset(); projectile = Bolt(1.65f); projectile.Advance(0.1f);
            Check("accovacciarsi evita un dardo alto", player.hp == 100); Destroy(projectile.gameObject);
            controller.height = player.heightStand;
            Reset(); projectile = Bolt(1.65f); projectile.Advance(0.1f);
            Check("lo stesso dardo alto colpisce in piedi", player.hp < 100);
            var peer = new Target { position = origin + Vector3.left * 2 }; Targets.Add(peer);
            Reset(); projectile = Bolt(); projectile.Advance(0.1f);
            Check("il primo bersaglio intercetta il dardo", peer.hits == 1 && player.hp == 100);
            Check("il danno conserva la provenienza", peer.source.HasValue && Vector3.Distance(peer.source.Value, origin + new Vector3(-5, 1, 0)) < 0.01f);
            Targets.Remove(peer);
            yield return null;
            Check("nessun dardo rimasto dopo gli impatti", FindObjectsByType<MagicBolt>(FindObjectsSortMode.None).Length == 0);

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-combatshots") >= 0)
            {
                var room = FloorDirector.I.cur.plan.rooms.Find(r => r.kind == RoomKind.Rotonda);
                Vector3 photo = room != null ? new Vector3(room.CenterX, 0.08f, room.CenterZ - Mathf.Min(room.W, room.D) * 0.025f) : original;
                player.Teleport(photo); player.cam.SetPositionAndRotation(photo + Vector3.up * 1.62f, Quaternion.Euler(8, 0, 0));
                yield return new WaitForSeconds(0.9f);
                Reset(); player.shield = 50; hud.SetBars(player.hp, player.shield); hud.Banner("", ""); hud.Toast("");
                Vector3 eye = player.cam.position, forward = player.cam.forward, right = player.cam.right;
                var bolt = MagicBolt.Launch(eye + forward * 2.7f + Vector3.up * 0.4f - right * 1.4f, eye + forward * 2.7f + Vector3.up * 0.4f + right * 5, 10, true); bolt.speed = 5f;
                player.Hurt(4, eye + right * 4);
                yield return new WaitForSeconds(0.12f);
                Fx.EnemyImpact(photo + Vector3.forward * 3 + Vector3.left * 1.2f + Vector3.up * 0.08f, Vector3.up, new Color(0.5f, 0.8f, 1));
                yield return new WaitForSeconds(0.03f);
                Debug.Log("[PryingEyes] combatfeedbacktest effetti " + Fx.Dump());
                LightPool.Tick(false, eye);
                string output = Path.GetFullPath("unity_logs/combat-feedback"); Directory.CreateDirectory(output);
                GameManager.CamShot(Path.Combine(output, "dardo-impatto.png"), player.cam.GetComponent<Camera>(), true);
                // Passaggio isolato: prova che le particelle producano pixel, non solo oggetti attivi nel pool.
                var effects = GameObject.Find("Fx").GetComponentsInChildren<Renderer>();
                var layers = Array.ConvertAll(effects, r => r.gameObject.layer);
                foreach (var effect in effects) effect.gameObject.layer = 31;
                var review = new GameObject("Verifica particelle").AddComponent<Camera>();
                Vector3 impact = photo + Vector3.forward * 3 + Vector3.left * 1.2f + Vector3.up * 0.08f;
                review.transform.SetPositionAndRotation(impact + new Vector3(0, 0.55f, -0.9f), Quaternion.LookRotation(new Vector3(0, -0.45f, 0.9f)));
                review.cullingMask = 1 << 31; review.clearFlags = CameraClearFlags.SolidColor;
                review.backgroundColor = Color.black; review.fieldOfView = 50; review.nearClipPlane = 0.01f;
                string pixelsFile = Path.Combine(output, "particelle.png");
                try
                {
                    GameManager.CamShot(pixelsFile, review, false, 512, 288);
                    var rendered = new Texture2D(2, 2); rendered.LoadImage(File.ReadAllBytes(pixelsFile));
                    int lit = 0; foreach (var pixel in rendered.GetPixels()) if (pixel.r + pixel.g + pixel.b > 0.05f) lit++;
                    Check("scintille e polvere producono pixel nel rendering", lit > 25);
                    Debug.Log("[PryingEyes] combatfeedbacktest pixel particelle " + lit);
                    Destroy(rendered);
                }
                finally
                {
                    for (int i = 0; i < effects.Length; i++) effects[i].gameObject.layer = layers[i];
                    Destroy(review.gameObject);
                }
            }
        }
        sealed class Target : ITarget
        {
            public Vector3 position; public int hits; public Vector3? source;
            public int Slot => 1; public Vector3 Feet => position; public bool Alive => true;
            public float Lean => 0; public float BodyHeight => 1.75f;
            public Vector3 LeanedPoint(float height) => position + Vector3.up * height;
            public Transform LookAt => null; public Transform BeamT => null;
            public float SightRange(Enemy enemy) => 0;
            public void Hurt(float damage, Vector3? origin = null) { hits++; source = origin; }
            public void Sedate(float seconds) { }
        }
    }
}
