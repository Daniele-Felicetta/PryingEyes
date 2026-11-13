using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    public sealed partial class ArchitectureTest
    {
        // -architecturetest -architecturesupply: rifornimento raggiungibile e acquistabile a serrande chiuse.
        IEnumerator RunSupply()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            Check("piano pronto per i rifornimenti", FloorDirector.I != null && FloorDirector.I.cur != null);
            if (failed > 0) yield break;
            var fi = FloorDirector.I.cur; var gm = GameManager.I; var player = gm.player;
            Enemy.ClearAll(); player.invulnerable = true;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            var regions = RoundRegions.Find(fi.plan);
            Check("arene presenti per il rifornimento", regions.Count > 0);
            foreach (var region in regions)
            {
                var supply = new List<WallBuy>();
                foreach (var wall in FindObjectsByType<WallBuy>(FindObjectsSortMode.None))
                {
                    var room = fi.RoomAt(wall.transform.position);
                    if (room != null && region.Has(room.id)) supply.Add(wall);
                }
                string label = "arena " + region.candleRoom;
                Check(label + " possiede un'arma rifornibile al suo interno", supply.Count > 0);
                if (supply.Count == 0) continue;
                supply.Sort((a, b) => a.price.CompareTo(b.price));
                var candle = fi.candles.Find(c => fi.RoomAt(c.transform.position)?.id == region.candleRoom);
                bool origin = candle != null && NavMesh.SamplePosition(candle.transform.position, out var sample, 2.5f, HotelNav.EnemyMask);
                Check(label + " punto iniziale raggiungibile", origin);
                if (!origin) continue;
                NavMesh.SamplePosition(candle.transform.position, out sample, 2.5f, HotelNav.EnemyMask);
                player.Teleport(sample.position);
                yield return new WaitForSeconds(0.1f);
                bool started = RoundDirector.I.TryStart(candle);
                Check(label + " chiude realmente le serrande", started && RoundDirector.I.Sealed);
                if (!started) continue;
                yield return new WaitForSeconds(0.65f);
                bool reachable = false;
                foreach (var wall in supply)
                {
                    Vector3 position = wall.transform.position + wall.transform.forward * 1.5f; position.y = 0.1f;
                    if (!NavMesh.SamplePosition(position, out var approach, 0.8f, HotelNav.EnemyMask)) continue;
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(player.transform.position, approach.position, HotelNav.EnemyMask, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                    reachable = true;
                    player.Teleport(approach.position);
                    Vector3 look = wall.transform.position - (approach.position + Vector3.up * player.eyeStand);
                    player.yaw = Mathf.Atan2(look.x, look.z); player.pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude);
                    player.weapon.ResetLoadout(); gm.AddPoints(500 - gm.score);
                    yield return new WaitForSeconds(0.1f);
                    Check(label + " espositore selezionabile con lo sguardo", WallBuy.Tick(player.cam));
                    Check(label + " arma acquistabile con i punti iniziali", wall.price <= 500);
                    WallBuy.TryBuy();
                    Check(label + " F acquista l'arma al prezzo esposto", player.weapon.CurrentIndex == wall.weaponIndex && gm.score == 500 - wall.price);
                    player.weapon.reserve = 0; gm.AddPoints(wall.AmmoPrice - gm.score);
                    WallBuy.Tick(player.cam);
                    if (shots)
                    {
                        yield return new WaitForSeconds(1.2f); // lascia esaurire il feedback dei punti assegnati dalla prova.
                        WallBuy.Tick(player.cam);
                        string folder = Path.GetFullPath("unity_logs/supply"); Directory.CreateDirectory(folder);
                        GameManager.CamShot(Path.Combine(folder, fi.plan.theme + "_arena_" + region.candleRoom + ".png"), null, true);
                    }
                    int before = gm.score, cost = wall.AmmoPrice;
                    WallBuy.TryBuy();
                    Check(label + " F riempie la riserva durante il round", player.weapon.reserve == player.weapon.reserveMax);
                    Check(label + " paga il prezzo delle sole munizioni", gm.score == before - cost);
                    before = gm.score; WallBuy.TryBuy();
                    Check(label + " riserva piena non costa altri punti", gm.score == before);
                    break;
                }
                Check(label + " accessibile dalla candela con le soglie sigillate", reachable);
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-supplybudget") >= 0)
                {
                    int count = 0, pistolRounds = 0, supplyRounds = 0;
                    var seen = new HashSet<Enemy>();
                    deadline = Time.time + 40;
                    while (Time.time < deadline && count < RoundDirector.I.Target)
                    {
                        foreach (var enemy in new List<Enemy>(Enemy.All))
                        {
                            if (!RoundDirector.I.Owns(enemy) || !seen.Add(enemy)) continue;
                            count++;
                            pistolRounds += Mathf.CeilToInt(enemy.hp / WeaponDefs.All[WeaponDefs.IndexOf("p9_pieta")].damage);
                            supplyRounds += Mathf.CeilToInt(enemy.hp / WeaponDefs.All[WeaponDefs.IndexOf("smg_tarantola")].damage);
                            Enemy.Remove(enemy); // registra la salute alla nascita, liberando il tetto dei vivi senza premi o gocce.
                        }
                        yield return null;
                    }
                    Check(label + " campiona l'intera ondata per il bilancio munizioni", count > 0 && count == RoundDirector.I.Target);
                    var pistol = WeaponDefs.All[WeaponDefs.IndexOf("p9_pieta")];
                    var smg = WeaponDefs.All[WeaponDefs.IndexOf("smg_tarantola")];
                    Debug.Log($"[PryingEyes] supplybudget {label} nemici={count} corpo a bruciapelo: pistola={pistolRounds}/{pistol.mag + pistol.reserve} colpi iniziali, mitra={supplyRounds}/{smg.mag + smg.reserve}, rifornimento={smg.reserveMax}");
                }
                RoundDirector.I.OnPlayerDied();
                yield return null;
            }
        }
    }
}
