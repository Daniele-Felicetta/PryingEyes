#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -magiatest [-seed S -floor F] [-magiashots] [-magiafast]: la stanza segreta del libro, i due
    /// grimori, le braccia e la seconda mano alla prova nel gioco vero.
    ///  1. sacrario: uno solo nel piano; la parete si usa con F da davanti; chiusa non si passa; aperta, in piedi non si passa (il cunicolo e'
    ///     alto 1,3 m) e accovacciati si arriva alla camera col CharacterController vero; i nemici non hanno strada; l'altare ha il libro
    ///     del piano;
    ///  2. grimori: il libro si apre (comandi bloccati), i prezzi crescono come detto, un interno senza esterno accanto non si compra, il
    ///     supremo chiede quattro interni; si comprano tutti i 38 nodi dei due libri;
    ///  3. seconda mano su un fucile e su una pistola: le sette magie e i sette sigilli partono, la mano libera prende il posto del guanto
    ///     avvolto e lo restituisce, gli effetti arrivano (danno al bersaglio, blink, cura, stati del sigillo sui colpi), il gomito resta
    ///     tra 28 e 176 gradi; con -magiashots le foto dei gesti (hotel_build/magia/).
    /// Esito in hotel_build/magia/magia.txt; uscita 0 se tutto passa.</summary>
    public sealed class MagiaTest : MonoBehaviour
    {
        int checks, failed;
        readonly StringBuilder report = new StringBuilder();
        bool shots;
        string outDir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-magiatest") >= 0)
                new GameObject("MagiaTest").AddComponent<MagiaTest>();
        }

        static bool HasArg(string a) => Array.IndexOf(Environment.GetCommandLineArgs(), a) >= 0;

        void Check(string name, bool ok, string detail = "")
        {
            checks++; if (!ok) failed++;
            string line = (ok ? "ok " : "FALLITO ") + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail);
            report.AppendLine(line);
            Debug.Log("[PryingEyes] magiatest " + line);
        }

        IEnumerator Start()
        {
            shots = HasArg("-magiashots");
            outDir = TestPaths.Out("magia");
            System.IO.Directory.CreateDirectory(outDir);
            var seq = Run();
            while (true)
            {
                bool more;
                try { more = seq.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa senza eccezioni", false, e.Message); break; }
                if (!more) break;
                yield return seq.Current;
            }
            PlayerController.botDir = Vector3.zero; PlayerController.botCrouch = false;
            string res = $"RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}";
            report.AppendLine(res);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "magia.txt"), report.ToString());
            Debug.Log("[PryingEyes] magiatest " + res);
            Application.Quit(failed == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ foto (con il canvas del grimorio quando e' aperto)
        void Shot(string name)
        {
            if (!shots) return;
            var cam = Camera.main;
            if (cam == null) return;
            const int W = 1280, H = 720;
            var rt = RenderTexture.GetTemporary(W, H, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var canvases = new List<Canvas>();
            if (GameManager.I != null && GameManager.I.hud != null) canvases.Add(GameManager.I.hud.GetComponent<Canvas>());
            if (GrimoireUI.Open) canvases.Add(GrimoireUI.I.GetComponent<Canvas>());
            foreach (var c in canvases) { if (c == null) continue; c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 0.2f; }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            foreach (var c in canvases) { if (c == null) continue; c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
            cam.targetTexture = prev;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);
            // una foto non deve fermare la prova: Windows a volte tiene aperto il file del giro prima (anteprime, indicizzazione:
            // errore 1224) e l'eccezione ucciderebbe la coroutine a meta'
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, name + ".png"), png); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PryingEyes] magiatest: foto {name} non scritta ({ex.Message}), provo {name}_b.png");
                try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, name + "_b.png"), png); } catch (System.Exception) { }
            }
        }

        // ------------------------------------------------------------------ percorso
        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 90f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning || GameManager.I == null || GameManager.I.player == null) && Time.realtimeSinceStartup < deadline) yield return null;
            var fi = FloorDirector.I.cur; var gm = GameManager.I; var player = gm.player;
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = true;
            player.invulnerable = true; PlayerController.holdLook = true;
            yield return new WaitForSeconds(0.5f);

            // ---- 1. il sacrario
            var sacs = fi.annexSites.FindAll(x => x.data.kind == AnnexKind.Sacrario);
            Check("un solo sacrario del grimorio nel piano", sacs.Count == 1, $"{sacs.Count}");
            if (sacs.Count == 1) yield return Sacrario(fi, gm, player, sacs[0]);

            // ---- 2. i grimori
            gm.score = 200000;
            yield return Books(gm, player);

            // ---- 3. la seconda mano: fucile, poi pistola, in un punto aperto (9 m liberi davanti)
            Vector3 open = OpenSpot(fi, out float openYaw);
            player.Teleport(open);
            player.yaw = openYaw; player.pitch = 0f;
            yield return new WaitForSeconds(0.4f);
            int rifle = WeaponDefs.IndexOf("ar9_malleus"), pistol = WeaponDefs.IndexOf("p9_pieta");
            foreach (int w in new[] { rifle, pistol })
            {
                if (w < 0) continue;
                player.weapon.Equip(w);
                player.Teleport(open); player.yaw = openYaw; player.pitch = 0f;
                yield return new WaitForSeconds(1.2f);
                yield return OffHand(player, WeaponDefs.All[w].id, open, openYaw);
            }
        }

        IEnumerator Sacrario(FloorInstance fi, GameManager gm, PlayerController player, AnnexSite s)
        {
            string tag = $"sacrario {s.data} (libro {(s.data.book == 0 ? "Stregoneria" : "Arsenale")})";
            Check($"{tag}: parete finta sul livello delle porte, chiusa", s.closerCol != null && s.closerCol.enabled && s.closerCol.gameObject.layer == HotelNav.DoorLayer && !s.opened);
            bool okOut = NavMesh.SamplePosition(s.outside, out var no, 1.5f, NavMesh.AllAreas);
            Check($"{tag}: davanti alla parete c'e' pavimento camminabile", okOut);
            if (!okOut) yield break;
            Check($"{tag}: la parete si usa con F da davanti", Interact.Visible(no.position + Vector3.up * 1.62f, s.gatePoint, s.closer) && s.GatePrompt() != null, s.GatePrompt());
            Check($"{tag}: la pianta conosce la camera (sala madre)", fi.RoomAt(s.inside) != null && fi.RoomAt(s.rewardPos) != null);
            Check($"{tag}: niente nascite nemiche dentro", AnnexRuntime.Forbidden(fi, s.inside) && AnnexRuntime.Forbidden(fi, s.rewardPos));
            Check($"{tag}: l'altare custodisce il libro del piano", s.altar != null && (int)s.altar.book == s.data.book && s.altar.Prompt != null, s.altar != null ? s.altar.Prompt : "nessun altare");
            // i nemici non hanno strada fino all'altare
            var path = new NavMeshPath();
            bool enemyWay = NavMesh.SamplePosition(s.rewardPos, out var nr, 1.0f, NavMesh.AllAreas) && NavMesh.CalculatePath(no.position, nr.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Check($"{tag}: i nemici non hanno strada fino all'altare (navmesh)", !enemyWay);

            Vector3 outV = s.secretOut, through = s.gatePoint + outV * 1.6f;
            player.Teleport(no.position);
            player.yaw = Mathf.Atan2(outV.x, outV.z); player.pitch = 0.05f;
            yield return new WaitForSeconds(0.6f);
            Shot("sacrario_parete");
            // chiusa: non si passa nemmeno accovacciati
            yield return Push(player, outV, true, 2.5f);
            Check($"{tag}: chiusa, non si passa", !Inside(s, player.transform.position));
            player.Teleport(no.position);
            yield return new WaitForSeconds(0.3f);
            s.GateUse();
            Check($"{tag}: premuto l'occhio, la parete si apre", s.opened && !s.closerCol.enabled);
            yield return new WaitForSeconds(2.2f);
            Shot("sacrario_aperta");
            // in piedi no, accovacciati si'
            yield return Push(player, outV, false, 3f);
            Check($"{tag}: in piedi il cunicolo non si passa", !Inside(s, player.transform.position), $"fermo a {player.transform.position}");
            player.Teleport(no.position);
            yield return new WaitForSeconds(0.3f);
            yield return Push(player, outV, true, 9f);
            bool inside = Inside(s, player.transform.position);
            Check($"{tag}: accovacciati si arriva nella camera", inside, $"fermo a {player.transform.position}");
            if (!inside) { player.Teleport(s.inside); yield return new WaitForSeconds(0.3f); }
            // in piedi dentro la camera, davanti all'altare
            PlayerController.botCrouch = false;
            yield return new WaitForSeconds(0.6f);
            Vector3 toAltar = s.rewardPos - player.transform.position; toAltar.y = 0f;
            player.yaw = Mathf.Atan2(toAltar.x, toAltar.z); player.pitch = 0.18f;
            yield return new WaitForSeconds(0.4f);
            Shot("sacrario_camera");
            // a portata di F dall'altare
            Vector3 stand = s.rewardPos - toAltar.normalized * 1.25f;
            player.Teleport(new Vector3(stand.x, s.rewardPos.y, stand.z));
            player.yaw = Mathf.Atan2(toAltar.x, toAltar.z); player.pitch = 0.42f;
            yield return new WaitForSeconds(0.5f);
            Shot("sacrario_altare");
            Check($"{tag}: il grimorio si legge con F da davanti all'altare", s.altar != null && Interact.Visible(player.cam, s.altar.Point, s.altar.transform) && Vector3.Distance(player.cam.position, s.altar.Point) < s.altar.Radius);
        }

        static bool Inside(AnnexSite s, Vector3 p) => p.x >= s.data.x0 * 0.1f && p.x <= s.data.x1 * 0.1f && p.z >= s.data.z0 * 0.1f && p.z <= s.data.z1 * 0.1f;

        IEnumerator Push(PlayerController player, Vector3 dir, bool crouch, float seconds)
        {
            PlayerController.holdLook = false;          // con holdLook il gioco ignora la direzione del bot
            PlayerController.botCrouch = crouch;
            yield return new WaitForSeconds(0.35f);
            PlayerController.botDir = dir;
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
            PlayerController.botDir = Vector3.zero;
            yield return new WaitForSeconds(0.2f);
            PlayerController.holdLook = true;
        }

        // ------------------------------------------------------------------ grimori
        IEnumerator Books(GameManager gm, PlayerController player)
        {
            var s = player.Arcana;
            s.ResetRun();
            foreach (Grimoire.Book b in new[] { Grimoire.Book.Stregoneria, Grimoire.Book.Arsenale })
            {
                string tag = b == Grimoire.Book.Stregoneria ? "Stregoneria" : "Arsenale";
                GrimoireUI.Show(b, null);
                yield return new WaitForSeconds(0.4f);
                Check($"{tag}: il libro si apre e blocca i comandi", GrimoireUI.Open && GameMenu.Blocked);
                var ui = GrimoireUI.I;
                ui.Selected = 0;
                yield return null;
                Shot($"libro_{tag}_vuoto");
                // regole del dodecaedro
                Check($"{tag}: un interno senza esterno accanto non si compra", !ui.Activate(12) && !Grimoire.Has(s.Mask(b), 12));
                Check($"{tag}: il supremo e' chiuso", !ui.Activate(18));
                int score0 = gm.score;
                int p0 = s.Price(b, 0);
                Check($"{tag}: il primo esterno costa 1250", p0 == 1250, $"{p0}");
                Check($"{tag}: si compra il primo esterno", ui.Activate(0) && Grimoire.Has(s.Mask(b), 0) && gm.score == score0 - 1250, $"punti {score0} -> {gm.score}");
                Check($"{tag}: il secondo esterno costa 1500", s.Price(b, 1) == 1500, $"{s.Price(b, 1)}");
                Check($"{tag}: con l'esterno accanto l'interno si compra (3000)", s.Price(b, 12) == 3000 && ui.Activate(12) && Grimoire.Has(s.Mask(b), 12));
                Check($"{tag}: l'interno comprato e' quello scelto", (b == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion) == 12);
                // tutti gli esterni (sbloccano gli interni accanto), poi gli interni, poi il supremo
                for (int i = 1; i < 12; i++) ui.Activate(i);
                for (int i = 13; i < 18; i++) ui.Activate(i);
                Check($"{tag}: i 12 esterni e i 6 interni comprati", Grimoire.CountTier(s.Mask(b), Grimoire.Tier.Esterno) == 12 && Grimoire.CountTier(s.Mask(b), Grimoire.Tier.Interno) == 6);
                ui.Selected = 18;
                yield return null;
                Shot($"libro_{tag}_quasi");
                Check($"{tag}: il supremo si compra con quattro interni (9000)", s.Price(b, 18) == 9000 && ui.Activate(18) && Grimoire.Has(s.Mask(b), 18));
                Check($"{tag}: i 19 nodi sono tuoi", Grimoire.CountTier(s.Mask(b), Grimoire.Tier.Supremo) == 1 && s.Mask(b) == (1u << 19) - 1u);
                ui.Selected = 14;
                yield return new WaitForSeconds(0.2f);
                Shot($"libro_{tag}_pieno");
                GrimoireUI.Hide();
                yield return null;
                Check($"{tag}: chiuso il libro i comandi tornano", !GrimoireUI.Open && !GameMenu.Blocked);
            }
        }

        // ------------------------------------------------------------------ seconda mano
        /// <summary>Un punto a terra con nove metri liberi davanti (per le magie a bersaglio e il passo d'ombra).</summary>
        static Vector3 OpenSpot(FloorInstance fi, out float yaw)
        {
            yaw = 0f;
            foreach (var p in fi.groundSpawns)
            {
                if (p.y > 1f || AnnexRuntime.Forbidden(fi, p)) continue;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 45f * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    Vector3 eye = p + Vector3.up * 1.2f;
                    if (Physics.SphereCast(eye, 0.45f, d, out _, 10f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    if (!NavMesh.SamplePosition(p + d * 5.5f, out _, 0.6f, NavMesh.AllAreas)) continue;
                    yaw = a;
                    return p;
                }
            }
            return fi.arrival != null ? fi.arrival.spawnPoint : Vector3.zero;
        }

        IEnumerator OffHand(PlayerController player, string weaponId, Vector3 open, float openYaw)
        {
            var w = player.weapon;
            var s = player.Arcana;
            var hand = w.OffHand;
            Check($"{weaponId}: la seconda mano c'e' (mano libera articolata)", hand != null && hand.Hand != null);
            if (hand == null) yield break;
            var mc = MagicCaster.I;
            Check($"{weaponId}: il lanciatore c'e'", mc != null);
            if (mc == null) yield break;
            player.yaw = openYaw; player.pitch = 0f;
            yield return new WaitForSeconds(0.3f);
            Shot($"{weaponId}_riposo");
            // magie: un bersaglio fermo davanti
            for (int node = 12; node <= 18; node++)
            {
                s.Essence = s.EssenceMax; s.ClearCooldowns(); s.EssenzaLeft = 0f;
                var dummy = Dummy(player, 5.5f);
                float hp0 = dummy != null ? dummy.hp : 0f;
                if (dummy != null && node != 16 && node != 18)
                {
                    // le magie dirette partono col mirino sul petto del bersaglio, come farebbe chi gioca (il Passo d'Ombra resta in piano)
                    Vector3 to = dummy.AimPoint - player.cam.position;
                    player.yaw = Mathf.Atan2(to.x, to.z);
                    player.pitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f));
                    yield return new WaitForSeconds(0.1f);
                }
                Vector3 pos0 = player.transform.position;
                player.hp = player.MaxHp * 0.5f;
                float hpP0 = player.hp;
                string name = Grimoire.Witch[node].name;
                bool started = mc.TryCast(node);
                Check($"{weaponId}: la magia {name} parte", started);
                if (!started) { Enemy.ClearAll(); continue; }
                yield return Gesture(hand, $"{weaponId}_magia_{node}", w);
                yield return new WaitForSeconds(node == 12 ? 0.6f : 0.25f);
                switch (node)
                {
                    case 16: Check($"{weaponId}: {name} sposta il giocatore", (player.transform.position - pos0).magnitude > 3f, $"{(player.transform.position - pos0).magnitude:F1} m"); break;
                    case 18: Check($"{weaponId}: {name} accende l'Essenza Maledetta", s.EssenzaLeft > 0f); break;
                    case 15: Check($"{weaponId}: {name} ferisce e cura", (dummy == null || dummy.hp < hp0) && player.hp > hpP0, $"bersaglio {hp0:F0}->{(dummy != null ? dummy.hp : 0f):F0}, vita {hpP0:F0}->{player.hp:F0}"); break;
                    default: Check($"{weaponId}: {name} ferisce il bersaglio", dummy == null || dummy.hp < hp0, $"{hp0:F0}->{(dummy != null ? dummy.hp : 0f):F0}"); break;
                }
                Enemy.ClearAll();
                player.Teleport(open); player.yaw = openYaw; player.pitch = 0f;
                yield return new WaitForSeconds(0.2f);
            }
            s.EssenzaLeft = 0f;
            // sigilli: ogni sigillo sull'arma, poi un colpo sul bersaglio
            for (int node = 12; node <= 18; node++)
            {
                s.Essence = s.EssenceMax; s.ClearCooldowns();
                string name = Grimoire.Arsenal[node].name;
                bool started = mc.TryInfusion(node);
                Check($"{weaponId}: il sigillo {name} parte", started);
                if (!started) continue;
                yield return Gesture(hand, $"{weaponId}_sigillo_{node}", w);
                int slot = w.SlotIndex;
                bool on = node == 18 ? s.CursedOn(slot) : s.InfusionOn(slot) == node;
                Check($"{weaponId}: {name} resta sull'arma", on);
                var aura = w.ViewRootT != null ? w.ViewRootT.GetComponent<WeaponAura>() : null;
                Check($"{weaponId}: {name} accende l'arma", aura != null);
                var dummy = Dummy(player, 4f);
                yield return new WaitForSeconds(0.2f);
                if (dummy != null)
                {
                    player.yaw = Mathf.Atan2(dummy.AimPoint.x - player.cam.position.x, dummy.AimPoint.z - player.cam.position.z);
                    Vector3 to = dummy.AimPoint - player.cam.position;
                    player.pitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f));
                    yield return new WaitForSeconds(0.15f);
                    int magBefore = w.mag;
                    float hpd = dummy.hp;
                    // l'elettricita scatta a caso (un colpo su tre circa): si spara finche arriva lo stato, al massimo 12 colpi
                    int shots = node == 16 ? 12 : 3;
                    for (int k = 0; k < shots && dummy != null && dummy.hp > 0f; k++)
                    {
                        if (w.mag <= 0) w.MaxAmmo();
                        w.DebugFire();
                        yield return new WaitForSeconds(0.25f);
                        if (node == 16 && dummy != null && dummy.GetComponent<EnemyStatus>() != null) break;
                    }
                    var st = dummy != null ? dummy.GetComponent<EnemyStatus>() : null;
                    bool effect = node == 12 ? (dummy == null || dummy.hp < hpd) : (dummy == null || st != null);
                    Check($"{weaponId}: {name} sui colpi", effect, st != null ? "stato presente" : "");
                    if (node == 18) Check($"{weaponId}: Arma Maledetta non consuma munizioni", w.mag >= magBefore, $"{magBefore}->{w.mag}");
                    Enemy.ClearAll();
                }
                player.yaw = openYaw; player.pitch = 0f;
                yield return new WaitForSeconds(0.3f);
            }
            // il lancio di mano sinistra (acquasanta)
            var eq = player.GetComponent<Equipment>();
            if (eq != null)
            {
                eq.grenades = 2;
                bool thrown = mc.Throw(p => eq.Throw(0));
                Check($"{weaponId}: il lancio di mano sinistra parte", thrown);
                if (thrown) yield return Gesture(hand, $"{weaponId}_lancio", w);
            }
            w.Refresh();
        }

        /// <summary>Segue un gesto fino alla fine: la mano libera prende il posto del guanto avvolto, il gomito resta nei suoi gradi, il
        /// palmo non attraversa la camera; foto a 6 tempi.</summary>
        IEnumerator Gesture(OffHand hand, string name, Weapon w)
        {
            float minElbow = 999f, maxElbow = 0f, maxWrist = 0f;
            bool sawFree = false;
            float[] at = { 0.12f, 0.28f, 0.44f, 0.6f, 0.76f, 0.92f };
            int next = 0;
            float until = Time.time + 6f;
            var arm = ArmOf(w);
            while (hand != null && hand.Busy && Time.time < until)
            {
                if (hand.FreeVisible) sawFree = true;
                if (arm != null && arm.Free > 0.5f)
                {
                    minElbow = Mathf.Min(minElbow, arm.LastElbowDeg); maxElbow = Mathf.Max(maxElbow, arm.LastElbowDeg);
                    maxWrist = Mathf.Max(maxWrist, arm.LastWristDeg);
                }
                if (next < at.Length && hand.Progress >= at[next]) { Shot($"{name}_{next}"); next++; }
                yield return null;
            }
            Check($"{name}: il gesto finisce", hand != null && !hand.Busy);
            Check($"{name}: la mano libera ha preso il posto del guanto", sawFree);
            Check($"{name}: e poi lo ha restituito", hand != null && !hand.FreeVisible);
            if (maxElbow > 0f) Check($"{name}: gomito tra 28 e 176 gradi", minElbow >= ArmRig.MinElbowDeg - 0.5f && maxElbow <= ArmRig.MaxElbowDeg + 0.5f, $"{minElbow:F0}..{maxElbow:F0}, polso fino a {maxWrist:F0}");
            // un gesto col braccio sempre teso vuol dire che l'arma o la mano stanno oltre la portata della spalla
            if (maxElbow > 0f) Check($"{name}: il gomito si piega (sotto 150 gradi)", minElbow < 150f, $"minimo {minElbow:F0}");
            report.AppendLine($"   {name}: gomito {minElbow:F0}..{maxElbow:F0} gradi, polso fino a {maxWrist:F0}");
        }

        static ArmRig ArmOf(Weapon w)
        {
            var f = typeof(Weapon).GetField("armL", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f != null ? f.GetValue(w) as ArmRig : null;
        }

        /// <summary>Un monaco fermo a 'dist' metri davanti, sulla navmesh (o il piu' vicino possibile).</summary>
        static Enemy Dummy(PlayerController player, float dist)
        {
            Vector3 f = new Vector3(Mathf.Sin(player.yaw), 0f, Mathf.Cos(player.yaw));
            Vector3 p = player.transform.position + f * dist;
            for (float d = dist; d > 1.5f; d -= 0.5f)
            {
                Vector3 q = player.transform.position + f * d;
                if (NavMesh.SamplePosition(q, out var h, 0.8f, NavMesh.AllAreas) && HotelNav.ClearLine(player.cam.position, h.position + Vector3.up)) { p = h.position; break; }
            }
            var e = Enemy.Spawn(Enemy.EnemyType.Monaco, p);
            if (e != null) { e.hp = e.maxHp = 5000f; }
            return e;
        }
    }
}
#endif
