#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using Production.Layout;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>Native acceptance loop: -autotest -nofire -citytest [-cityshots].</summary>
    public sealed partial class CityTest : PlayerTest
    {
        [Serializable] sealed class Finding { public string code, subject, observed, expected; public bool passed; }
        [Serializable] sealed class Report { public string operation = "city.runtime", revision, unity, reproduce, recipeSha256; public DistrictPlan layout; public RoomEvidence[] rooms; public CityWaveProfile survival; public CityAreaDefinition[] areas; public bool ok; public Finding[] findings; }
        [Serializable] sealed class RoomEvidence { public string id, block, sourceSha256; public RoomPlan plan; }
        readonly List<Finding> findings = new List<Finding>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<CityTest>("-citytest"); }

        protected override string Suite { get { return "citytest"; } }
        protected override string PhotoFlag { get { return "-cityshots"; } }
        /// <summary>Le foto, result.json e report.json (quello che legge tools/citylab.py) stanno nella cartella che la pagina del prototipo nomina.</summary>
        protected override string OutFolder { get { return "city-runtime"; } }
        /// <summary>Il bot di -autotest non spara in citta': era -citytest nella lista di GameManager.</summary>
        protected override bool HoldFire { get { return true; } }
        /// <summary>La prova non ha mai contato le eccezioni (una che lancia la sequenza e' il controllo RUNTIME_EXCEPTION del rapporto): i controlli restano quelli che ha sempre avuto.</summary>
        protected override string ExceptionCheck { get { return null; } }

        /// <summary>Un controllo del rapporto: entra in report.json con il suo codice, la cosa guardata, quello che si e' visto e quello che si voleva (tools/citylab.py li legge),
        /// e nel log e in result.json con il nome "CODICE: cosa" e il resto come dettaglio.</summary>
        void Record(string code, string subject, bool passed, string observed, string expected)
        {
            findings.Add(new Finding { code = code, subject = subject, passed = passed, observed = observed, expected = expected });
            Check(code + ": " + subject, passed, observed + "; expected " + expected);
        }

        protected override void Finish()
        {
            PlayerController.botDir = Vector3.zero;
            // se la sequenza ha lanciato, la base ha gia' segnato "sequenza completa": il rapporto lo dice col codice che ha sempre avuto
            if (Failed > findings.FindAll(f => !f.passed).Count)
                findings.Add(new Finding { code = "RUNTIME_EXCEPTION", subject = "city", passed = false, observed = "the sequence threw: see the player log", expected = "no exceptions" });
            var city = FindFirstObjectByType<CityDirector>();
            var report = new Report { recipeSha256 = city != null ? city.recipeSha256 : "", survival = city != null ? city.survival : null, areas = city != null ? city.areas : null,
                rooms = System.Array.ConvertAll(FindObjectsByType<CityRoom>(FindObjectsSortMode.None), r => new RoomEvidence { id = r.recipeId, block = r.name, sourceSha256 = r.sourceSha256, plan = r.Plan }),
                layout = city != null ? JsonUtility.FromJson<DistrictPlan>(city.layoutJson) : null, ok = findings.TrueForAll(f => f.passed), findings = findings.ToArray(),
                revision = CoopProtocol.ContentRevision, unity = Application.unityVersion,
                reproduce = "PryingEyes.exe -batchmode -autotest -nofire -citytest -cityshots" };
            File.WriteAllText(Path.Combine(TestRun.OutDir(OutFolder), "report.json"), JsonUtility.ToJson(report, true));
        }

        void CheckRig(Enemy enemy)
        {
            bool guard = enemy.type == Enemy.EnemyType.Vigile;
            var attachment = enemy.transform.Find(guard ? "Left arm/Riot shield" : "Right arm/Grabber");
            var mesh = attachment != null ? attachment.GetComponentInChildren<MeshFilter>() : null;
            float size = mesh == null ? 0 : (guard ? mesh.transform.lossyScale.x : mesh.transform.lossyScale.y);
            Record("ATTACHMENT_SCALE", enemy.type.ToString(), Mathf.Abs(size - (guard ? 0.65f : 1.05f)) < 0.01f, size.ToString("F3"), guard ? "shield width 0.65 m" : "tool length 1.05 m");
        }

        protected override IEnumerator Run()
        {
            yield return new WaitForSeconds(0.3f);
            var gm = GameManager.I; var city = FindFirstObjectByType<CityDirector>();
            Record("CITY_BOOT", "scene", city != null && gm != null && !GameManager.proc, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, "Citta, non-procedural");
            if (city == null || gm == null) yield break;
            var layout = JsonUtility.FromJson<DistrictPlan>(city.layoutJson);
            Record("LAYOUT_PROVENANCE", "district", layout != null && city.recipeSha256.Length == 64, city.recipeSha256, "baked layout and recipe SHA256");
            if (layout == null) yield break;
            foreach (var block in layout.blocks)
            {
                var root = GameObject.Find(block.id); var floor = root != null ? root.transform.Find("Floor") : null;
                bool matches = floor != null && Vector2.Distance(new Vector2(floor.position.x, floor.position.z), new Vector2(block.building.x, block.building.z)) < .001f &&
                    Mathf.Abs(floor.lossyScale.x - block.width) < .001f && Mathf.Abs(floor.lossyScale.z - block.depth) < .001f;
                Record("BUILDING_PLAN_MATCH", block.id, matches, matches.ToString(), "scene equals shared layout");
            }
            for (int i = 0; i < gm.spawnPoints.Length; i++)
                Record("SPAWN_PLAN_MATCH", "spawn " + i, i < layout.spawns.Length && Vector2.Distance(new Vector2(gm.spawnPoints[i].position.x, gm.spawnPoints[i].position.z), new Vector2(layout.spawns[i].x, layout.spawns[i].z)) < .001f, gm.spawnPoints[i].position.ToString(), "scene equals shared layout");
            for (int i = 0; i < city.shops.Length; i++)
                Record("PANEL_PLAN_MATCH", "panel " + i, i < layout.panels.Length && Vector2.Distance(new Vector2(city.shops[i].position.x, city.shops[i].position.z), new Vector2(layout.panels[i].position.x, layout.panels[i].position.z)) < .001f && city.shops[i].yaw == layout.panels[i].yaw, city.shops[i].position.ToString(), "scene equals shared layout");
            var player = gm.player; player.invulnerable = true;
            Record("WAVE_AUTOSTART", "wave", gm.wave == 1 && gm.waveActive, gm.wave.ToString(), "1, active");
            gm.StopAllCoroutines(); Enemy.ClearAll();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-cityrigonly") >= 0)
            {
                foreach (var type in new[] { Enemy.EnemyType.Spazzino, Enemy.EnemyType.Vigile })
                { var enemy = Enemy.Spawn(type, new Vector3(0, 0.1f, 10)); CheckRig(enemy); Enemy.Remove(enemy); }
                yield break;
            }
            var path = new NavMeshPath();
            var access = RunAccess(); while (access.MoveNext()) yield return access.Current;
            Vector3 street = Vector3.up * 0.1f;
            var floors = new List<Transform>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "Floor" && t.parent != null && t.parent.name.StartsWith("Building_")) floors.Add(t);
            Record("BUILDING_COUNT", "district", floors.Count == 4, floors.Count.ToString(), "4");
            foreach (var floor in floors)
            {
                bool a = NavMesh.SamplePosition(street, out var from, 1, HotelNav.EnemyMask);
                bool b = NavMesh.SamplePosition(floor.position + Vector3.up * 0.3f, out var to, 1, HotelNav.EnemyMask);
                bool connected = a && b && NavMesh.CalculatePath(from.position, to.position, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
                Record("BUILDING_PATH", floor.parent.name, connected, path.status.ToString(), "complete street-to-interior path");
                if (!connected) continue;
                player.Teleport(from.position); PlayerController.holdLook = false;
                float until = Time.time + 30;
                while (Time.time < until && Vector3.Distance(player.transform.position, to.position) > 0.6f)
                {
                    if (NavMesh.SamplePosition(player.transform.position, out from, 1, HotelNav.EnemyMask))
                        NavMesh.CalculatePath(from.position, to.position, HotelNav.EnemyMask, path);
                    Vector3 next = to.position;
                    if (path.corners.Length > 1) next = path.corners[1];
                    Vector3 direction = next - player.transform.position; direction.y = 0;
                    PlayerController.botDir = direction.normalized;
                    yield return null;
                }
                PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
                Record("CONTROLLER_ENTRANCE", floor.parent.name, Vector3.Distance(player.transform.position, to.position) <= 0.6f,
                    player.transform.position.ToString(), to.position.ToString());
            }
            var rooms = RunRooms();
            while (rooms.MoveNext()) yield return rooms.Current;
            foreach (var spawn in gm.spawnPoints)
            {
                bool sample = NavMesh.SamplePosition(spawn.position, out var from, 1, HotelNav.EnemyMask) && NavMesh.SamplePosition(street, out var to, 1, HotelNav.EnemyMask);
                bool connected = sample && NavMesh.CalculatePath(from.position, street, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
                Record("SPAWN_PATH", spawn.name, connected, path.status.ToString(), "complete spawn-to-street path");
            }
            var walls = FindObjectsByType<WallBuy>(FindObjectsSortMode.None);
            Record("SHOP_COUNT", "district", walls.Length == 3, walls.Length.ToString(), "3");
            foreach (var wall in walls)
            {
                Vector3 approach = wall.transform.position + wall.transform.forward * 1.7f; approach.y = 0.2f;
                bool sample = NavMesh.SamplePosition(approach, out var hit, 1, HotelNav.EnemyMask);
                Record("SHOP_APPROACH", wall.name, sample, approach.ToString(), "navigable");
                if (!sample) continue;
                player.Teleport(hit.position);
                var look = wall.transform.position - (hit.position + Vector3.up * player.eyeStand);
                player.yaw = Mathf.Atan2(look.x, look.z); player.pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude);
                player.weapon.ResetLoadout(); gm.AddPoints(wall.price - gm.score);
                yield return new WaitForSeconds(0.15f);
                bool selected = WallBuy.Tick(player.cam); WallBuy.TryBuy();
                Record("SHOP_PURCHASE", wall.name, selected && player.weapon.CurrentIndex == wall.weaponIndex && gm.score == 0,
                    $"selected={selected}, weapon={player.weapon.CurrentIndex}, score={gm.score}", $"weapon={wall.weaponIndex}, score=0");
                player.weapon.reserve = 0; gm.AddPoints(wall.AmmoPrice); WallBuy.Tick(player.cam); WallBuy.TryBuy();
                Record("SHOP_REFILL", wall.name, gm.score == 0 && player.weapon.reserve == player.weapon.reserveMax,
                    $"reserve={player.weapon.reserve}, score={gm.score}", "full reserve, score=0");
                if (Photos && wall.weaponIndex == WeaponDefs.IndexOf("p9_ronda"))
                {
                    yield return new WaitForSeconds(1.8f);
                    GameManager.CamShot(PhotoPath("ronda-shop"), null, true);
                }
            }
            player.Teleport(street); player.yaw = player.pitch = 0; gm.hud?.SetPrompt("");
            foreach (var type in new[] { Enemy.EnemyType.Spazzino, Enemy.EnemyType.Vigile })
            {
                var enemy = Enemy.Spawn(type, new Vector3(0, 0.1f, 10));
                Record("ENEMY_MODEL", type.ToString(), enemy != null && enemy.type == type, enemy != null ? enemy.name : "null", type.ToString());
                if (enemy == null) continue;
                CheckRig(enemy);
                int damage = GameManager.damageTaken;
                float until = Time.time + 16; bool windup = false;
                while (Time.time < until && GameManager.damageTaken == damage)
                {
                    windup |= enemy.PreparingMelee;
                    yield return null;
                }
                Record("ENEMY_PURSUIT_MELEE", type.ToString(), windup && GameManager.damageTaken > damage,
                    $"windup={windup}, contacts={GameManager.damageTaken - damage}, position={enemy.transform.position}", "pursuit, windup and melee contact");
                if (Photos) { GameManager.CamShot(PhotoPath(type.ToString()), null, true); }
                Enemy.Remove(enemy);
            }
            if (Photos)
            {
                player.Teleport(new Vector3(-3, 0.1f, -8)); player.yaw = 0.4f; player.pitch = -0.08f;
                yield return new WaitForSeconds(0.2f);
                GameManager.CamShot(PhotoPath("district"), null, true);
            }
            player.Teleport(street); gm.waveActive = false; gm.StartWave(1);
            int defeated = 0; bool onlyCityEnemies = true, relentless = true, scaled = true; float waveDeadline = Time.time + 110;
            while (Time.time < waveDeadline && (gm.wave < 3 || gm.waveActive))
            {
                foreach (var enemy in new List<Enemy>(Enemy.All))
                {
                    if (enemy == null || enemy.IsDead) continue;
                    onlyCityEnemies &= enemy.type == Enemy.EnemyType.Spazzino || enemy.type == Enemy.EnemyType.Vigile;
                    relentless &= enemy.relentless;
                    bool guard = enemy.type == Enemy.EnemyType.Vigile;
                    float health = (guard ? 340 : 140) * city.survival.Health(gm.wave);
                    float lowSpeed = (guard ? 1.05f : 1.65f) * city.survival.Speed(gm.wave);
                    float highSpeed = (guard ? 1.20f : 1.90f) * city.survival.Speed(gm.wave);
                    scaled &= Mathf.Abs(enemy.maxHp - health) < .01f && enemy.speed >= lowSpeed - .01f && enemy.speed <= highSpeed + .01f;
                    defeated++; enemy.Damage(100000, enemy.AimPoint, false);
                }
                yield return null;
            }
            int expected = city.survival.Count(1) + city.survival.Count(2) + city.survival.Count(3);
            Record("WAVE_PROGRESS", "three complete waves", gm.wave == 3 && !gm.waveActive && defeated == expected,
                $"wave={gm.wave}, active={gm.waveActive}, defeated={defeated}", $"wave=3, inactive, {expected} enemies");
            Record("WAVE_CITY_ROSTER", "spawn rules", onlyCityEnemies, onlyCityEnemies.ToString(), "urban enemies only");
            Record("WAVE_RELENTLESS", "urban pursuit", relentless && defeated > 0, relentless.ToString(), "every wave enemy hunts without stealth activation");
            Record("WAVE_SCALING", "urban pressure", scaled && defeated > 0, scaled.ToString(), "configured health and speed progression applied");
            gm.StartWave(5);
            float capDeadline = Time.time + city.survival.aliveLimit * city.survival.interval + 5;
            while (Enemy.All.Count < city.survival.aliveLimit && Time.time < capDeadline) yield return null;
            yield return new WaitForSeconds(city.survival.interval * 2);
            Record("WAVE_POPULATION_CAP", "fifth wave", Enemy.All.Count == city.survival.aliveLimit && gm.waveActive,
                $"living={Enemy.All.Count}, active={gm.waveActive}", $"living={city.survival.aliveLimit}, active=True");
            if (Photos) GameManager.CamShot(PhotoPath("city-wave5-population"), null, true);
            gm.StopAllCoroutines(); Enemy.ClearAll();
        }
    }
}
#endif
