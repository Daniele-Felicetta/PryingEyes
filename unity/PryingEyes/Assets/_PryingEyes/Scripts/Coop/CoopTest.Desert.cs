using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Cattedrale;
using PryingEyes.Deserto;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>La prova della cooperativa nel deserto (0.9.1), a due processi: l'ospite sceglie il deserto (come dal menu), l'ospitato parte dove gioca lui. Si lancia con
    /// run_all.ps1 -Only j (ospite: -cooptest -coopdeserto -deserto -catacombe; ospitato: -cooptest -coopdeserto -catacombe, senza -deserto: segue l'ospite).
    /// Passi: 0 il deserto e' costruito sulle due macchine e i compagni si vedono; 1 stesso seme e stessa ora; 2 il compagno va a mezzo chilometro, vicino a un luogo: l'ospite costruisce
    /// la sabbia, la navmesh e il luogo anche li' (piu' punti di riferimento), e dei nemici nati sull'ospite arriva al compagno solo quello che ha vicino; 3 il compagno torna: il nemico
    /// lontano compare e quello di prima sparisce dalla sua vista senza morire; 4 un colpo mortale a un pupazzo: muore sull'ospite e i punti vanno a chi ha sparato; 5 l'ora cambiata
    /// dall'ospite arriva a tutti; 6 un boss caduto sull'ospite cade per tutti e il trofeo va nel magazzino di ognuno; 7 (solo con -cooppics e l'ospite con la grafica) le foto del compagno sulla sabbia.
    /// Con -coopleave (esce) o -coopcrash (processo ucciso) i passi sono 0, 1, 2 e 8: il compagno lontano se ne va e l'ospite prosegue da solo (nessun punto attorno a lui, un solo bersaglio,
    /// il luogo lontano si scarica, l'orologio va e un nemico gli da' la caccia). Con -coopmoto (run_all.ps1 -Only m) i passi sono 0, 1, 9 e 10: la moto (pe-coop-32), vista dall'altro processo.</summary>
    public sealed partial class CoopTest
    {
        /// <summary>-coopdeserto: il copione del deserto al posto di quello dei piani.</summary>
        static readonly bool DesertRun = HasArg("-coopdeserto");

        // dati passati da un passo all'altro e dall'ospite all'ospitato (messaggi di prova 6 e 7)
        int dSeed, dDay, dSeq;
        float dHour;
        int idA = -1, idB = -1, idSeq;
        Site farSite;
        Enemy enemyA, enemyB;
        static readonly Vector3 Stairs = new Vector3(18f, 0f, 2.2f);

        /// <summary>L'ospite racconta il seme e l'ora (6) o gli id dei due nemici della prova (7).</summary>
        void DesertReceive(int sub, ref NetReader r)
        {
            if (sub == 6)
            {
                int seed = r.I32(), day = r.I32();
                float hour = r.F32();
                if (r.Ok) { dSeed = seed; dDay = day; dHour = hour; dSeq++; }
            }
            else if (sub == 7)
            {
                int a = r.U16(), b = r.U16();
                if (r.Ok) { idA = a; idB = b; idSeq++; }
            }
        }

        // ------------------------------------------------------------------ aiuti
        static float FlatTo(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        static SiteRt RtOfSite(Site s)
        {
            var dd = FloorDirector.I != null ? FloorDirector.I.Desert : null;
            if (dd == null || s == null) return null;
            foreach (var rt in dd.Loaded) if (rt.site == s) return rt;
            return null;
        }

        /// <summary>Un luogo a piu' di 420 m dalla scalinata (il piu' vicino tra quelli): lo stesso su tutti e due i processi, il mondo e' del seme.</summary>
        static Site PickFarSite()
        {
            var list = new List<Site>();
            DesertWorld.SitesNear(Stairs.x, Stairs.z, 1400f, list);
            Site best = null;
            float bd = float.MaxValue;
            foreach (var s in list)
            {
                if (s == null || s.plan == null && s.levels == null) continue;
                float d = FlatTo(new Vector3(s.CentreX, 0f, s.CentreZ), Stairs);
                if (d < 420f || d >= bd) continue;
                bd = d; best = s;
            }
            return best;
        }

        /// <summary>Un luogo con un boss (torre o catacomba), il primo vicino alla cattedrale: lo stesso su tutti e due.</summary>
        static Site PickBossSite()
        {
            var list = new List<Site>();
            DesertWorld.SitesNear(0f, 0f, 1500f, list);
            foreach (var s in list) if (s != null && s.IsStack) return s;
            return null;
        }

        static bool NavAround(Vector3 center, float dist, out Vector3 p)
        {
            for (int k = 0; k < 24; k++)
            {
                float a = k * Mathf.PI * 2f / 24f;
                var q = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * dist;
                q.y = DesertWorld.Height(q.x, q.z) + 0.5f;
                if (NavMesh.SamplePosition(q, out var h, 6f, NavMesh.AllAreas) && FlatTo(h.position, center) > dist * 0.6f) { p = h.position; return true; }
            }
            p = center;
            return false;
        }

        bool Knows(int slot, int id)
        {
            foreach (int k in G.KnownEnemies(slot)) if (k == id) return true;
            return false;
        }

        static int BankTotal()
        {
            int n = 0;
            foreach (var r in Risorse.Tutte) n += Fabbrica.Banca.Get(r.id);
            return n;
        }

        /// <summary>Porta il giocatore sulla sabbia in un punto (un po' in alto: la sabbia sotto nasce mentre cade) e aspetta che tocchi terra.</summary>
        IEnumerator DGoTo(float x, float z)
        {
            var pl = GameManager.I.player;
            pl.Teleport(new Vector3(x, DesertWorld.Height(x, z) + 8f, z));
            yield return null;
            yield return Until(() => DesertTerrain.I != null && DesertTerrain.I.Ready(pl.transform.position, 14f), 45f);
            yield return Until(() => pl.GetComponent<CharacterController>().isGrounded, 15f);
        }

        // 0 ------------------------------------------------------------------ il deserto in due
        IEnumerator D0()
        {
            var g = G;
            var fd = FloorDirector.I;
            float t0 = Time.time;
            while (Time.time - t0 < 150f && !(g.FloorBuilt && (Coop.IsHost || g.Started) && ShownCount() >= Expected - 1 && fd != null && !fd.transitioning && fd.Desert != null)) yield return null;
            bool ok = g.FloorBuilt && ShownCount() >= Expected - 1 && fd != null && fd.Desert != null;
            Check("il deserto e' costruito e i compagni sono in vista", ok, $"{Time.time - t0:F1} s, epoca {g.Epoch}, vedo {ShownCount()} compagni su {Expected - 1}");
            if (!ok) { aborted = true; yield break; }
            Check("la partita e' nel deserto (anche per chi l'ha ereditato dall'ospite)", DesertMode.Active && fd.InHub && DesertTerrain.I != null && DesertSky.I != null, $"seme {DesertMode.Seed}");
            Safe();
            Drops.disabled = true;
            yield return new WaitForSeconds(2f);
        }

        // 1 ------------------------------------------------------------------ stesso mondo, stessa ora
        IEnumerator D1()
        {
            Safe();
            if (Coop.IsHost)
            {
                G.SendTest(w.Reset().U8(6).I32(DesertMode.Seed).I32(DesertMode.Clock.day).F32(DesertMode.Clock.hour).ToArray());
                yield return new WaitForSeconds(1f);
                yield break;
            }
            yield return Until(() => dSeq > 0, 10f);
            Check("l'ospitato ha il seme dell'ospite: lo stesso mondo", dSeq > 0 && dSeed == DesertMode.Seed, $"ospite {dSeed}, io {DesertMode.Seed}");
            float mine = DesertMode.Clock.day * 24f + DesertMode.Clock.hour, his = dDay * 24f + dHour;
            Check("l'ora del giorno e' quella dell'ospite (si riallinea ogni due secondi)", Mathf.Abs(mine - his) < 0.3f, $"io {mine:F2} h, ospite {his:F2} h");
        }

        // 2 ------------------------------------------------------------------ chi e' lontano: l'ospite costruisce anche li', e i nemici vanno solo a chi li ha vicino
        IEnumerator D2()
        {
            Safe();
            farSite = PickFarSite();
            Check("c'e' un luogo lontano dove mandare il compagno", farSite != null, farSite != null ? farSite.ToString() : "nessuno");
            if (farSite == null) { aborted = true; yield break; }
            float fx = farSite.fx1 + 45f, fz = farSite.CentreZ;
            var pl = GameManager.I.player;
            if (!Coop.IsHost)
            {
                yield return DGoTo(fx, fz);
                Check("il compagno e' arrivato lontano e tocca terra", FlatTo(pl.transform.position, Stairs) > 400f && pl.GetComponent<CharacterController>().isGrounded, $"a {FlatTo(pl.transform.position, Stairs):F0} m dalla scalinata");
                yield return Until(() => idSeq > 0, 150f);          // l'ospite aspetta fino a 100 s di costruire attorno a me, poi sceglie i nemici
                yield return new WaitForSeconds(1.5f);
                var map = G.EnemyMap;
                bool hasA = map.TryGetValue(idA, out var pa) && pa != null && pa.puppet;
                bool hasB = map.ContainsKey(idB);
                Check("il nemico che ho vicino compare come pupazzo", hasA, $"id {idA}");
                Check("quello che l'ospite ha a mezzo chilometro non mi viene mandato", !hasB, $"id {idB}");
                float worst = 0f;
                foreach (var e in Enemy.All) if (e != null && e.puppet) worst = Mathf.Max(worst, FlatTo(e.transform.position, pl.transform.position));
                Check("ogni pupazzo che vedo sta entro la portata dell'ospite", worst <= 250f, $"il piu' lontano a {worst:F0} m");
                yield break;
            }
            // ospite: aspetta che il compagno sia lontano, poi guarda se il deserto c'e' anche li'
            yield return Until(() => FlatTo(PeerPos(), Stairs) > 300f, 90f);
            Vector3 peer = PeerPos();
            Check("il compagno e' a mezzo chilometro dall'ospite", FlatTo(peer, Stairs) > 400f, $"{FlatTo(peer, Stairs):F0} m");
            bool shell = false, ground = false, nav = false;
            float tw = Time.time;
            yield return Until(() =>
            {
                var rt = RtOfSite(farSite);
                shell = rt != null && rt.ShellComplete;
                ground = DesertTerrain.I.Ready(peer, 14f);
                nav = DesertNav.ReadyAround(peer, 30f);
                return shell && ground && nav;
            }, 100f);
            Check("l'ospite ha costruito il luogo vicino al compagno", shell, shell ? "guscio completo" : "luogo non caricato o incompleto");
            Check("e la sabbia con il suo collider e la navmesh sotto di lui", ground && nav, $"sabbia {ground}, navmesh {nav}, dopo {Time.time - tw:F1} s");
            bool a = NavAround(peer, 35f, out Vector3 atA);
            var me = GameManager.I.player.transform.position;
            bool b = NavAround(me, 25f, out Vector3 atB);
            Check("ci sono punti di nascita sulla navmesh, vicino al compagno e vicino all'ospite", a && b, $"A {a}, B {b}");
            if (!a || !b) { aborted = true; yield break; }
            Enemy.debugFreeze = true;
            enemyA = Enemy.Spawn(Enemy.EnemyType.Monaco, atA);
            enemyB = Enemy.Spawn(Enemy.EnemyType.Monaco, atB);
            yield return new WaitForSeconds(2.5f);
            G.SendTest(w.Reset().U8(7).U16(enemyA.netId).U16(enemyB.netId).ToArray());
            bool knowsA = Knows(1, enemyA.netId), knowsB = Knows(1, enemyB.netId);
            Check("l'ospite manda al compagno il nemico che ha vicino e non quello a cinquecento metri", knowsA && !knowsB, $"A {knowsA}, B {knowsB}");
            yield return new WaitForSeconds(2f);
        }

        // 3 ------------------------------------------------------------------ il compagno torna: compare il nemico vicino alla scalinata, sparisce l'altro (senza morire)
        IEnumerator D3()
        {
            Safe();
            if (!Coop.IsHost)
            {
                yield return DGoTo(Stairs.x, Stairs.z + 70f);
                yield return Until(() => G.EnemyMap.ContainsKey(idB) && !G.EnemyMap.ContainsKey(idA), 30f);
                Check("tornando vicino all'ospite compare il suo nemico", G.EnemyMap.TryGetValue(idB, out var pb) && pb != null && pb.puppet, $"id {idB}");
                Check("quello di prima, ormai lontano, sparisce dalla mia vista", !G.EnemyMap.ContainsKey(idA), $"id {idA}");
                yield break;
            }
            yield return Until(() => FlatTo(PeerPos(), Stairs) < 120f, 90f);
            yield return Until(() => Knows(1, enemyB.netId) && !Knows(1, enemyA.netId), 30f);
            Check("l'ospite ora manda B e ritira A", Knows(1, enemyB.netId) && !Knows(1, enemyA.netId), $"A {Knows(1, enemyA.netId)}, B {Knows(1, enemyB.netId)}");
            Check("A e' ancora vivo sull'ospite: e' sparito solo dalla vista del compagno", enemyA != null && !enemyA.IsDead);
        }

        // 4 ------------------------------------------------------------------ un colpo mortale a un pupazzo del deserto
        IEnumerator D4()
        {
            Safe();
            var gm = GameManager.I;
            if (Coop.IsHost)
            {
                yield return Until(() => enemyB == null || enemyB.IsDead, 14f);
                Check("l'ospite vede morire il suo nemico per il colpo del compagno", enemyB == null || enemyB.IsDead);
                yield break;
            }
            if (!Primary) { yield return new WaitForSeconds(6f); yield break; }
            Enemy target = null;
            G.EnemyMap.TryGetValue(idB, out target);
            Check("c'e' il pupazzo da colpire", target != null && !target.IsDead, $"id {idB}");
            if (target == null) yield break;
            int s0 = gm.score, k0 = gm.kills;
            target.Damage(100000f, target.AimPoint, true, true, DamageSource.Gun);
            yield return Until(() => target == null || target.IsDead, 6f);
            yield return new WaitForSeconds(1.2f);
            Check("il pupazzo muore (lo dice l'ospite) e i punti vanno a chi ha sparato", (target == null || target.IsDead) && gm.score - s0 >= Punti.Testa && gm.kills == k0 + 1, $"+{gm.score - s0} punti, uccisioni +{gm.kills - k0}");
        }

        // 5 ------------------------------------------------------------------ l'ora e' dell'ospite
        IEnumerator D5()
        {
            Safe();
            if (Coop.IsHost)
            {
                DesertMode.Clock.hour = 15.5f;
                DesertMode.Clock.day = 2;
                yield return new WaitForSeconds(6f);
                yield break;
            }
            yield return Until(() => DesertMode.Clock.day == 2 && Mathf.Abs(DesertMode.Clock.hour - 15.5f) < 0.4f, 12f);
            Check("l'ora cambiata dall'ospite arriva anche qui", DesertMode.Clock.day == 2 && Mathf.Abs(DesertMode.Clock.hour - 15.5f) < 0.4f, $"giorno {DesertMode.Clock.day}, ore {DesertMode.Clock.hour:F2}");
        }

        // 6 ------------------------------------------------------------------ un boss caduto sull'ospite cade per tutti
        IEnumerator D6()
        {
            Safe();
            var site = PickBossSite();
            Check("c'e' un luogo con un boss", site != null, site != null ? site.ToString() : "nessuno");
            if (site == null) { aborted = true; yield break; }
            long key = DesertMode.Pack(site, site.boss, 0);
            int bank0 = BankTotal();
            yield return Barrier();         // tutti hanno scelto il luogo e contato il magazzino prima che l'ospite faccia cadere il boss
            if (aborted) yield break;
            if (Coop.IsHost)
            {
                Check("prima il boss e' in piedi", !DesertMode.BossesDown.Contains(key));
                FloorDirector.I.Desert.OnBossDown(site);
                Check("sull'ospite e' caduto e il suo trofeo e' nel magazzino", DesertMode.BossesDown.Contains(key) && BankTotal() > bank0, $"magazzino {bank0} -> {BankTotal()}");
                yield return new WaitForSeconds(4f);
                yield break;
            }
            yield return Until(() => DesertMode.BossesDown.Contains(key), 10f);
            Check("il boss caduto sull'ospite e' caduto anche qui", DesertMode.BossesDown.Contains(key));
            Check("e il trofeo e' nel mio magazzino", BankTotal() > bank0, $"magazzino {bank0} -> {BankTotal()}");
        }

        // 7 ------------------------------------------------------------------ (-cooppics, l'ospite con la grafica) il compagno sulla sabbia e la minimappa col suo puntino
        IEnumerator D7()
        {
            Safe();
            var pl = GameManager.I.player;
            if (!Coop.IsHost)
            {
                yield return DGoTo(Stairs.x + 5f, Stairs.z - 22f);          // sulla sabbia, davanti alla scalinata, in vista dell'ospite
                Vector3 to = Stairs - pl.transform.position;
                pl.yaw = Mathf.Atan2(to.x, to.z); pl.pitch = 0f;
                if (Torch.I != null && !Torch.I.Lit) Torch.I.Toggle();
                yield return new WaitForSeconds(14f);
                yield break;
            }
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Log("info: senza grafica (-nographics) niente foto"); yield break; }
            yield return Until(() => FlatTo(PeerPos(), Stairs) > 8f && FlatTo(PeerPos(), Stairs) < 45f, 60f);
            if (DesertSky.I != null) DesertSky.I.SetHour(10.5f, true);
            yield return new WaitForSeconds(2f);
            Vector3 peer = PeerPos();
            System.IO.Directory.CreateDirectory(PicDir);
            for (int k = 0; k < 2; k++)
            {
                // la prima guardando il compagno (corpo, nome, minimappa col suo puntino e il pannello dei compagni), la seconda lungo la sabbia, con il compagno di lato
                Vector3 target = k == 0 ? peer + Vector3.up * 1.2f : pl.transform.position + new Vector3(0f, 1.6f, -60f);
                Vector3 d = target - pl.cam.position;
                pl.yaw = Mathf.Atan2(d.x, d.z);
                pl.pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude);
                yield return new WaitForSeconds(1f);
                string path = System.IO.Path.Combine(PicDir, k == 0 ? "coop_deserto_compagno.png" : "coop_deserto_scalinata.png");
                GameManager.CamShot(path, null, true, 1600, 900);
                Check(k == 0 ? "foto del compagno nel deserto" : "foto della sabbia con il compagno di lato", System.IO.File.Exists(path), $"a {FlatTo(peer, pl.transform.position):F1} m");
            }
            if (DesertSky.I != null) DesertSky.I.Frozen = false;
        }

        // 8 ------------------------------------------------------------------ (-coopleave / -coopcrash con -coopdeserto) il compagno lontano se ne va: l'ospite prosegue da solo nel deserto
        IEnumerator D8()
        {
            if (Coop.IsClient)
            {
                yield return new WaitForSeconds(1.5f);                 // l'ultima sincronia deve partire davvero prima che io sparisca
                Log(Crash ? "me ne vado di colpo (processo ucciso)" : "me ne vado (chiusura normale)");
                Finish();
                if (Crash) { System.Threading.Thread.Sleep(400); System.Diagnostics.Process.GetCurrentProcess().Kill(); }
                yield break;
            }
            var g = G;
            var pl = GameManager.I.player;
            Check("prima che se ne vada, il compagno lontano e' un punto attorno a cui l'ospite costruisce", DesertAnchors.Others.Count == 1, DesertAnchors.Others.Count + " punti");
            yield return Until(() => g.Players.Count == 0, 40f);
            Check("il compagno se ne e' andato: il suo pupazzo non c'e' piu' sull'ospite", g.Players.Count == 0, g.Players.Count + " giocatori");
            yield return new WaitForSeconds(0.5f);                  // Destroy lavora a fine fotogramma: il bersaglio esce dall'elenco subito dopo
            Check("l'ospite non costruisce piu' attorno a lui e i nemici hanno di nuovo un solo bersaglio", DesertAnchors.Others.Count == 0 && Targets.Count == 1 && Targets.AnyAlive, $"punti {DesertAnchors.Others.Count}, bersagli {Targets.Count}");
            Check("l'elenco dei nemici che il compagno vedeva e' stato buttato", g.KnownEnemies(1).Count == 0, g.KnownEnemies(1).Count + " rimasti");
            if (farSite != null)
            {
                // senza piu' nessuno vicino il luogo lontano si scarica, se (come quasi sempre) sta oltre il raggio di carico dell'ospite piu' la soglia di scarico
                float limit = (farSite.IsTower ? DesertDirector.LoadTower : DesertDirector.LoadBuilding) + DesertDirector.Unhook;
                float away = DesertAnchors.NearestFootprint(farSite, pl.transform.position, out _);
                if (away > limit + 10f)
                {
                    yield return Until(() => RtOfSite(farSite) == null, 15f);
                    Check("il luogo lontano, senza piu' nessuno vicino, viene scaricato", RtOfSite(farSite) == null, $"a {away:F0} m dall'ospite, soglia {limit:F0} m");
                }
                else Log($"info: il luogo lontano e' a {away:F0} m dall'ospite (soglia di scarico {limit:F0} m): resta caricato, nessun controllo");
            }
            // da solo il gioco prosegue: l'orologio va, un nemico da' la caccia all'ospite
            Enemy.debugFreeze = false;
            if (enemyA != null) Enemy.Remove(enemyA);
            if (enemyB != null) Enemy.Remove(enemyB);
            Safe();
            float f0 = Time.frameCount, h0 = DesertMode.Clock.day * 24f + DesertMode.Clock.hour;
            Enemy chaser = null;
            if (NavAround(pl.transform.position, 14f, out Vector3 at)) { chaser = Enemy.Spawn(Enemy.EnemyType.Monaco, at); chaser.MakeRelentless(); }
            float d0 = chaser != null ? FlatTo(chaser.transform.position, pl.transform.position) : 0f;
            yield return new WaitForSeconds(6f);
            float d1 = chaser != null && !chaser.IsDead ? FlatTo(chaser.transform.position, pl.transform.position) : 0f;
            Check("un nemico caccia l'ospite da solo (si avvicina)", chaser != null && d1 < d0 - 1.5f, $"da {d0:F1} a {d1:F1} m");
            if (chaser != null) Enemy.Remove(chaser);
            float h1 = DesertMode.Clock.day * 24f + DesertMode.Clock.hour;
            Check("il gioco continua a girare e l'orologio del deserto va avanti", Time.frameCount > f0 + 10 && h1 > h0, $"ora {h0:F2} -> {h1:F2}");
        }

        // 9 e 10 -------------------------------------------------------------- (-coopmoto, 0.9.1+: la moto) chi guida si vede in sella, con la sua moto, dall'altro processo: prima l'ospitato, poi l'ospite
        static readonly bool MotoRun = HasArg("-coopmoto");

        IEnumerator D9() => MotoStep(!Coop.IsHost);

        IEnumerator D10() => MotoStep(Coop.IsHost);

        /// <summary>Un giro in moto di chi guida ('iDrive': sale sulla sua moto, accelera, curva da una parte e dall'altra, frena e scende) visto dall'altro processo: il compagno e' in sella (lo stato lo dice),
        /// la sua moto compare sotto il pupazzo, avanza nel verso in cui punta, ha le ruote che girano, si inclina in curva e, quando scende, sparisce.</summary>
        IEnumerator MotoStep(bool iDrive)
        {
            Safe();
            var g = G;
            var pl = GameManager.I.player;
            int other = Coop.IsHost ? 1 : 0;
            if (iDrive)
            {
                yield return new WaitForSeconds(2f);                               // l'altro ha il tempo di mettersi a guardare
                var bike = DesertBikes.Nearest(pl.transform.position, 80f);
                Check("la moto del mio posto e' parcheggiata ai piedi della scalinata", bike != null, bike != null ? $"a {FlatTo(bike.transform.position, pl.transform.position):F1} m" : "nessuna");
                if (bike == null) { aborted = true; yield break; }
                Vector3 start = bike.transform.position;
                pl.Mount(bike);
                Check("salgo in sella", pl.Riding && bike.rider == pl);
                DesertBike.botThrottle = 1f; DesertBike.botSteer = 0.6f;
                yield return new WaitForSeconds(4f);
                DesertBike.botSteer = -0.6f;
                yield return new WaitForSeconds(3f);
                float went = FlatTo(pl.transform.position, start);
                Check("ho corso: sono lontano dal posto di partenza", went > 30f && pl.RideSpeed > 8f, $"{went:F0} m, {pl.RideSpeed * 3.6f:F0} km/h");
                DesertBike.botThrottle = -1f; DesertBike.botSteer = 0f;
                yield return Until(() => Mathf.Abs(pl.RideSpeed) < 2f, 8f);
                DesertBike.BotReset();
                pl.RideRequestDismount();
                yield return Until(() => !pl.Riding, 6f);
                Check("scendo e la moto resta parcheggiata", !pl.Riding && bike.rider == null && bike.ColliderOn);
                yield return new WaitForSeconds(3f);                               // l'altro vede che sono sceso
                yield break;
            }
            RemotePlayer rp = null;
            yield return Until(() => g.Players.TryGetValue(other, out rp) && rp != null && rp.last.riding, 25f);
            Check("vedo il compagno in sella (il suo stato dice che guida)", rp != null && rp.last.riding);
            if (rp == null || !rp.last.riding) { aborted = true; yield break; }
            yield return Until(() => rp.Bike != null && rp.Bike.puppet && rp.Bike.gameObject.activeInHierarchy, 5f);
            var pb = rp.Bike;
            Check("la sua moto compare sotto il pupazzo", pb != null && pb.puppet && FlatTo(pb.transform.position, rp.transform.position) < 0.6f, pb != null ? $"scarto {FlatTo(pb.transform.position, rp.transform.position):F2} m" : "nessuna moto");
            if (pb == null) { aborted = true; yield break; }
            // sei secondi di corsa: la moto va nel verso in cui punta (a intervalli di mezzo secondo) e in curva si inclina
            float maxRoll = 0f, maxSpeed = 0f, tEnd = Time.time + 6f, tNext = Time.time + 0.5f;
            Vector3 prev = rp.transform.position;
            int good = 0, samples = 0;
            while (Time.time < tEnd)
            {
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(pb.RollVisual));
                if (Time.time >= tNext)
                {
                    Vector3 mv = rp.transform.position - prev; mv.y = 0f;
                    float sp = mv.magnitude / 0.5f;
                    maxSpeed = Mathf.Max(maxSpeed, sp);
                    if (sp > 4f)
                    {
                        samples++;
                        Vector3 fwd = new Vector3(Mathf.Sin(pb.Heading), 0f, Mathf.Cos(pb.Heading));
                        if (Vector3.Dot(mv.normalized, fwd) > 0.85f) good++;
                    }
                    prev = rp.transform.position; tNext = Time.time + 0.5f;
                }
                yield return null;
            }
            Check("la moto del compagno avanza nel verso in cui punta", samples >= 4 && good >= samples * 0.8f, $"{good} di {samples} intervalli, al massimo {maxSpeed * 3.6f:F0} km/h");
            Check("in curva la sua moto si inclina", maxRoll > 3f, $"{maxRoll:F1} gradi");
            float s0 = pb.WheelSpin;
            yield return new WaitForSeconds(0.03f);
            Check("le sue ruote girano", Mathf.Abs(Mathf.DeltaAngle(s0, pb.WheelSpin)) > 1f, $"{Mathf.DeltaAngle(s0, pb.WheelSpin):F0} gradi in 30 ms");
            yield return Until(() => !rp.last.riding, 25f);
            yield return new WaitForSeconds(0.6f);
            Check("quando scende la sua moto sparisce dalla mia vista", !rp.last.riding && (rp.Bike == null || !rp.Bike.gameObject.activeSelf), rp.Bike != null ? $"attiva {rp.Bike.gameObject.activeSelf}" : "nessuna");
        }
    }
}
