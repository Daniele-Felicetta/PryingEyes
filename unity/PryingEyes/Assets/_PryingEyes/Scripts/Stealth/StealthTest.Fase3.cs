using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Prova della fase 3 (perk, libri, Biblioteca, Scrigno, Consacrazione, penalita' di morte), con lo schema delle altre:
    ///   PryingEyes.exe -batchmode [-nographics] -autotest -stealthtest -booktest -seed N -floor N    esito in hotel_build/furtivo/libri.txt
    /// Uscita 0 ok, 1 fallito, 2 piano senza lo spiazzo che serve (cambia seme). Senza -nographics fa anche le foto.</summary>
    public partial class StealthTest
    {
        static void Aim(PlayerController pl, Vector3 target)
        {
            Vector3 e = pl.transform.position + Vector3.up * 1.6f;
            Vector3 d = target - e;
            pl.yaw = Mathf.Atan2(d.x, d.z);
            pl.pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude);
        }

        /// <summary>Un posto sulla navmesh a 'dist' m da 'target' (a quell'altezza, sopra l'oggetto: la linea di vista parte dagli occhi),
        /// di preferenza davanti ('front', orizzontale) e poi sempre piu' di lato, con la vista libera.</summary>
        static bool StandNear(Vector3 target, Vector3 front, float dist, out Vector3 pos, Vector3? from = null)
        {
            pos = target;
            front.y = 0f;
            front.Normalize();
            var path = new NavMeshPath();
            foreach (float ang in new[] { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 120f, -120f, 180f })
                foreach (float k in new[] { 1f, 0.85f, 1.2f, 0.7f })
                {
                    Vector3 q = target + Quaternion.Euler(0f, ang, 0f) * front * dist * k; q.y = 0f;
                    if (!NavMesh.SamplePosition(q, out NavMeshHit h, 0.5f, NavMesh.AllAreas)) continue;
                    if (!HotelNav.ClearLine(h.position + Vector3.up * 1.5f, target)) continue;
                    // con 'from': solo posti a cui si arriva a piedi da li'
                    if (from.HasValue && !(NavMesh.CalculatePath(from.Value, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)) continue;
                    pos = h.position;
                    return true;
                }
            return false;
        }

        /// <summary>Mappa a caratteri di una stanza (celle di 25 cm) per capire perche' qualcosa non si raggiunge:
        /// R navmesh raggiungibile da 'from', i navmesh ma isola staccata, # ostacolo fisico senza navmesh, + niente navmesh (vicino ai muri), O oggetto, S posto davanti.</summary>
        void DumpRoom(Room room, Vector3 from, Vector3 obj, Vector3 stand)
        {
            const float c = 0.25f;
            float T = FloorBuilder.TH;
            float x0 = room.x0 * 0.1f + T, x1 = room.x1 * 0.1f - T, z0 = room.z0 * 0.1f + T, z1 = room.z1 * 0.1f - T;
            var path = new NavMeshPath();
            Log($"mappa della stanza {room} (x {x0:F1}..{x1:F1}, z {z0:F1}..{z1:F1}), O = {obj}, S = {stand}");
            for (float z = z1 - c * 0.5f; z > z0; z -= c)
            {
                var sb = new System.Text.StringBuilder();
                for (float x = x0 + c * 0.5f; x < x1; x += c)
                {
                    char ch;
                    var p = new Vector3(x, 0f, z);
                    if (Mathf.Abs(x - obj.x) < c * 0.5f && Mathf.Abs(z - obj.z) < c * 0.5f) ch = 'O';
                    else if (Mathf.Abs(x - stand.x) < c * 0.5f && Mathf.Abs(z - stand.z) < c * 0.5f) ch = 'S';
                    else if (NavMesh.SamplePosition(p, out NavMeshHit h, 0.2f, NavMesh.AllAreas))
                        ch = NavMesh.CalculatePath(from, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete ? 'R' : 'i';
                    else ch = Physics.CheckBox(new Vector3(x, 0.95f, z), new Vector3(0.1f, 0.55f, 0.1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore) ? '#' : '+';
                    sb.Append(ch);
                }
                Log("   " + sb);
            }
        }

        IEnumerator CheckBooks()
        {
            var gm = GameManager.I;
            var fd = FloorDirector.I;
            var pl = gm.player;
            var wp = pl.weapon;
            bool passed = true;
            void Check(string what, bool ok, string detail = "")
            {
                Log((ok ? "ok       " : "FALLITO  ") + what + (detail.Length > 0 ? "   [" + detail + "]" : ""));
                if (!ok) passed = false;
            }
            bool shots = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            yield return new WaitForSeconds(2.0f);
            Drops.disabled = true;
            Enemy.ClearAll();
            PlayerController.holdLook = true;
            pl.invulnerable = true;
            Enemy.debugFreeze = true;
            Enemy.debugNoSense = true;
            var torch = Torch.I;
            if (torch != null) { torch.charge = 1f; if (shots && !torch.on) torch.Toggle(); }
            void SetScore(int n) { gm.AddPoints(n - gm.score); }

            // ----- 1) posizionamento
            Log("--- posizionamento");
            var lib = Biblioteca.Current; var chest = Scrigno.Current; var altar = Altare.Current;
            Log($"piano {fd.floor} seme {fd.runSeed}: stanze {fd.cur.plan.rooms.Count}, Biblioteca {(fd.libraryRoom != null ? fd.libraryRoom.ToString() : "-")}, Scrigno {(fd.chestRoom != null ? fd.chestRoom.ToString() : "-")}, Altare {(fd.altarRoom != null ? fd.altarRoom.ToString() : "-")}");
            Check("la Biblioteca c'e' con tre leggii", lib != null && lib.stands.Count == 3, lib != null ? lib.stands.Count + " leggii" : "assente");
            Check("lo Scrigno c'e'", chest != null);
            Check("l'altare della Consacrazione c'e'", altar != null);
            if (lib == null || chest == null || altar == null) { Log("manca un santuario: prova non applicabile su questo piano"); Application.Quit(2); yield break; }
            var ids = new HashSet<int> { fd.libraryRoom.id, fd.chestRoom.id, fd.altarRoom.id };
            Check("tre stanze diverse", ids.Count == 3);
            bool noCandle = true;
            foreach (int id in ids) if (fd.cur.plan.objectives.Contains(id)) noCandle = false;
            Check("nessuna nella stanza di una candela", noCandle);
            var regions = RoundRegions.Find(fd.cur.plan);
            bool outside = true;
            foreach (var g in regions) foreach (int id in ids) if (g.Has(id)) outside = false;
            Check("nessuna dentro una zona sigillabile", outside);
            var perks = new HashSet<PerkId>();
            foreach (var s in lib.stands) perks.Add(s.perk);
            Check("tre perk diversi", perks.Count == 3);
            // i posti da cui usarli: davanti al leggio di mezzo (dal lato della stanza), allo scrigno e all'altare, a 2 m circa
            Vector3 rowAxis = (lib.stands[2].transform.position - lib.stands[0].transform.position).normalized;
            Vector3 mid = lib.stands[1].transform.position;
            Vector3 libFront = Vector3.Cross(Vector3.up, rowAxis);
            if (Vector3.Dot(libFront, new Vector3(fd.libraryRoom.CenterX, 0f, fd.libraryRoom.CenterZ) - mid) < 0f) libFront = -libFront;
            var lift = fd.cur.arrival;
            Vector3? fromPos = null;
            if (lift != null && NavMesh.SamplePosition(lift.frontPoint, out NavMeshHit fh, 2.5f, NavMesh.AllAreas)) fromPos = fh.position;
            // (leggii, scrigno e altare sono ostacoli: ci si arriva davanti, a piedi dall'ascensore di arrivo)
            // pianta a griglia: l'altare sta nella stanza Segreta, che si raggiunge solo a carponi: a piedi si parte da dentro, davanti al buco
            Vector3? fromAltarPos = fromPos;
            if (fd.altarRoom.type == RoomType.Segreta) { fromAltarPos = null; if (fd.SecretEntry(fd.altarRoom, out Vector3 secretIn)) fromAltarPos = secretIn; }
            bool hasLib = StandNear(mid + Vector3.up * 1.3f, libFront, 1.9f, out Vector3 libNear, fromPos);
            bool hasChest = StandNear(chest.transform.position + Vector3.up * 1.4f, chest.transform.forward, 1.8f, out Vector3 chestStand, fromPos);
            bool hasAltar = StandNear(altar.transform.position + Vector3.up * 1.4f, altar.transform.forward, 2.0f, out Vector3 altarStand, fromAltarPos);
            Check("davanti a leggii, scrigno e altare c'e' un posto raggiungibile a piedi dall'arrivo", hasLib && hasChest && hasAltar, $"leggii {hasLib}, scrigno {hasChest}, altare {hasAltar}");
            if (fromPos.HasValue)
            {
                if (!hasLib) DumpRoom(fd.libraryRoom, fromPos.Value, mid, libNear);
                if (!hasChest) DumpRoom(fd.chestRoom, fromPos.Value, chest.transform.position, chestStand);
                if (!hasAltar && fromAltarPos.HasValue) DumpRoom(fd.altarRoom, fromAltarPos.Value, altar.transform.position, altarStand);
            }
            bool usable = fd.ShrineUsable(chest.transform.position, chest) && fd.ShrineUsable(altar.transform.position, altar);
            foreach (var s in lib.stands) usable &= fd.ShrineUsable(s.transform.position, s);
            Check("il controllo del gioco (ShrineUsable) approva tutti i santuari", usable);
            // la navmesh non passa dentro i santuari (sono ostacoli)
            bool blocked = true;
            foreach (var s in lib.stands)
                if (NavMesh.SamplePosition(s.transform.position + Vector3.up * 0.2f, out NavMeshHit bh, 0.15f, NavMesh.AllAreas)) blocked = false;
            if (NavMesh.SamplePosition(chest.transform.position + Vector3.up * 0.2f, out NavMeshHit ch0, 0.2f, NavMesh.AllAreas)) blocked = false;
            if (NavMesh.SamplePosition(altar.transform.position + Vector3.up * 0.2f, out NavMeshHit ah0, 0.2f, NavMesh.AllAreas)) blocked = false;
            Check("la navmesh non entra nei leggii, nello scrigno e nell'altare", blocked);

            // ----- 2) il prompt di ciascuno, guardandolo da vicino
            Log("--- prompt");
            IEnumerator Face(Vector3 standAt, Vector3 target)
            {
                pl.Teleport(standAt);
                Aim(pl, target);
                yield return null; yield return null;
            }
            SetScore(5000);
            if (hasLib)
            {
                yield return Face(libNear, lib.stands[1].Point);
                gm.hud.SetPrompt("");
                Interact.Tick(pl.cam);
                Log("prompt libro: " + gm.hud.CurrentPrompt);
                Check("il prompt del libro nomina il perk e il prezzo", gm.hud.CurrentPrompt.Contains(lib.stands[1].Def.name.ToUpperInvariant()) && gm.hud.CurrentPrompt.Contains("2000"), gm.hud.CurrentPrompt);
                if (shots)
                {
                    Shot("libri_libro");
                    // la fila intera, da piu' lontano
                    if (StandNear(mid + Vector3.up * 1.3f, libFront, 3.6f, out Vector3 far)) { yield return Face(far, mid + Vector3.up * 1.1f); Shot("libri_biblioteca"); }
                }
            }
            if (hasChest)
            {
                yield return Face(chestStand, chest.Point);
                gm.hud.SetPrompt("");
                Interact.Tick(pl.cam);
                Log("prompt scrigno: " + gm.hud.CurrentPrompt);
                Check("prompt dello Scrigno con il prezzo", gm.hud.CurrentPrompt.Contains("SCRIGNO") && gm.hud.CurrentPrompt.Contains("950"), gm.hud.CurrentPrompt);
                if (shots) Shot("libri_scrigno_chiuso");
            }
            if (hasAltar)
            {
                yield return Face(altarStand, altar.Point);
                gm.hud.SetPrompt("");
                Interact.Tick(pl.cam);
                Log("prompt altare: " + gm.hud.CurrentPrompt);
                Check("prompt dell'altare con il prezzo", gm.hud.CurrentPrompt.Contains("CONSACRAZIONE") && gm.hud.CurrentPrompt.Contains("3000"), gm.hud.CurrentPrompt);
                if (shots) Shot("libri_altare");
            }

            // ----- 3) la Biblioteca: punti che mancano, acquisto, gli altri libri si chiudono
            Log("--- Biblioteca");
            Perks.ResetRun();
            var s0 = lib.stands[0]; var s1 = lib.stands[1]; var s2 = lib.stands[2];
            SetScore(100);
            s0.Use();
            Check("con 100 punti il libro non si compra", !Perks.Has(s0.perk) && Perks.Count == 0 && gm.score == 100);
            SetScore(5000);
            int before = gm.score;
            s0.Use();
            Check("il primo libro costa 2000", before - gm.score == 2000 && Perks.Has(s0.perk) && Perks.Count == 1, $"{before - gm.score} punti");
            Check("gli altri due libri si richiudono", s1.Sealed && s2.Sealed && !s0.Sealed && s0.Taken);
            before = gm.score;
            s1.Use();
            Check("un libro sigillato non si compra", gm.score == before && !Perks.Has(s1.perk));
            Check("il prompt del libro sigillato lo dice", s1.Prompt != null && s1.Prompt.Contains("SIGILLATO"), s1.Prompt ?? "null");
            Check("il libro letto non ha piu' prompt", s0.Prompt == null);
            Check("il prossimo perk costa 2500", Perks.NextPrice == 2500, Perks.NextPrice.ToString());
            if (shots)
            {
                if (StandNear(mid + Vector3.up * 1.3f, libFront, 3.6f, out Vector3 again)) { yield return Face(again, mid + Vector3.up * 1.1f); yield return new WaitForSeconds(1.2f); Shot("libri_sigillati"); }
            }

            // ----- 4) prezzi progressivi e posti
            Log("--- posti e prezzi");
            Perks.ResetRun();
            var order = new[] { PerkId.PelleDiPietra, PerkId.ManiSvelte, PerkId.DoppioColpo, PerkId.Rinascita };
            var expect = new[] { 2000, 2500, 3000, 4000 };
            bool prices = true;
            for (int i = 0; i < 4; i++)
            {
                if (Perks.NextPrice != expect[i]) prices = false;
                Perks.Add(order[i]);
            }
            Check("prezzi progressivi 2000, 2500, 3000, 4000", prices);
            Check("quattro perk: posti pieni, niente piu' prezzo", Perks.Full && Perks.NextPrice == -1);
            Check("con i posti pieni non si aggiunge altro", !Perks.Add(PerkId.Velo) && Perks.Count == 4);
            Check("con i posti pieni la Biblioteca non si costruisce", Biblioteca.Place(fd.cur, fd.cur.plan, fd.libraryRoom, new Rng(1)) == null);
            Perks.ResetRun();
            Check("dopo il reset non c'e' nessun perk", Perks.Count == 0 && Perks.NextPrice == 2000);

            // ----- 5) gli effetti, uno per uno
            Log("--- effetti");
            Enemy.ClearAll();
            wp.Equip(WeaponDefs.IndexOf("p9_pieta"));
            Weapon.debugAds = false;
            yield return null;

            // Pelle di pietra
            pl.hp = 100f;
            Perks.Add(PerkId.PelleDiPietra);
            yield return null;
            Check("Pelle di pietra: vita massima 150 e subito piena", Mathf.Approximately(pl.MaxHp, 150f) && pl.hp >= 149.9f, $"hp {pl.hp:F0}/{pl.MaxHp:F0}");
            Check("Pelle di pietra: la barra si allunga", gm.hud.HpBarWidth > 320f, $"{gm.hud.HpBarWidth:F0} px");
            Perks.ResetRun(); pl.OnPerksChanged(false); pl.hp = 100f;

            // Mani svelte: tempo di ricarica con e senza
            float tBase = 0f, tPerk = 0f;
            for (int run = 0; run < 2; run++)
            {
                if (run == 1) Perks.Add(PerkId.ManiSvelte);
                wp.mag = 0; wp.reserve = 100;
                wp.Reload();
                float t0 = Time.time;
                while (wp.reloading && Time.time - t0 < 12f) yield return null;
                if (run == 0) tBase = Time.time - t0; else tPerk = Time.time - t0;
                yield return new WaitForSeconds(0.2f);
            }
            Check("Mani svelte: la ricarica dura il 60%", tBase > 0.3f && Mathf.Abs(tPerk / tBase - 0.6f) < 0.12f, $"{tBase:F2} s -> {tPerk:F2} s");
            Perks.ResetRun();

            // Doppio colpo: tempo tra due colpi con e senza
            float fBase = 0f, fPerk = 0f;
            for (int run = 0; run < 2; run++)
            {
                if (run == 1) Perks.Add(PerkId.DoppioColpo);
                yield return new WaitForSeconds(0.6f);
                float t0 = Time.time;
                for (int k = 0; k < 6; k++)
                {
                    wp.mag = wp.magSize;
                    wp.DebugFire();
                    float tw = Time.time;
                    while (!wp.CanFireNow && Time.time - tw < 3f) yield return null;
                }
                float avg = (Time.time - t0) / 6f;
                if (run == 0) fBase = avg; else fPerk = avg;
            }
            Check("Doppio colpo: la cadenza sale di un terzo", fBase > 0.05f && Mathf.Abs(fBase / fPerk - 1.33f) < 0.2f, $"{fBase * 1000f:F0} ms -> {fPerk * 1000f:F0} ms tra i colpi");
            Perks.ResetRun();

            // Rinascita
            Perks.Add(PerkId.Rinascita);
            pl.invulnerable = false;
            pl.hp = pl.MaxHp; pl.shield = 50f;
            pl.Hurt(9999f);
            Check("Rinascita: la prima morte del piano ti rialza sul posto", pl.alive && pl.hp > 40f && pl.hp < 60f && pl.Reviving, $"vivo {pl.alive}, hp {pl.hp:F0}");
            float hpNow = pl.hp;
            pl.Hurt(9999f);
            Check("Rinascita: tre secondi di tregua", pl.alive && Mathf.Approximately(pl.hp, hpNow));
            Check("Rinascita: l'icona si spegne (usata)", !Perks.ReviveReady);
            yield return new WaitForSeconds(3.2f);
            Perks.NewFloor();
            Check("Rinascita: al piano dopo e' di nuovo pronta", Perks.ReviveReady);
            Perks.ResetRun();
            pl.invulnerable = true; pl.hp = pl.MaxHp; pl.shield = 50f;

            // Passi di cenere: il raggio dei passi, camminando
            if (FindOpenSpot(fd.cur, out Vector3 spot, out Vector3 dir))
            {
                float rBase = 0f, rPerk = 0f;
                for (int run = 0; run < 2; run++)
                {
                    if (run == 1) Perks.Add(PerkId.PassiDiCenere);
                    pl.Teleport(NavPoint(spot - dir * 1.9f));
                    Aim(pl, spot + dir * 3f + Vector3.up * 1.6f);
                    Noise.lastStepRadius = 0f;
                    PlayerController.holdLook = false;           // col bot libero (nessun nemico) botDir fa camminare
                    PlayerController.botDir = dir;
                    float tw = Time.time;
                    while (Noise.lastStepRadius <= 0f && Time.time - tw < 2.5f) yield return null;
                    PlayerController.botDir = Vector3.zero;
                    PlayerController.holdLook = true;
                    if (run == 0) rBase = Noise.lastStepRadius; else rPerk = Noise.lastStepRadius;
                    yield return new WaitForSeconds(0.4f);
                }
                Check("Passi di cenere: il rumore dei passi cala del 40%", rBase > 1f && Mathf.Abs(rPerk / rBase - 0.6f) < 0.05f, $"{rBase:F1} m -> {rPerk:F1} m");
            }
            else Log("nessuno spiazzo per camminare (Passi di cenere provato solo a numeri)");
            Perks.ResetRun();
            Check("Passi di cenere: senza il perk il fattore e' 1", Mathf.Approximately(Perks.StepNoise, 1f));

            // Velo: portata della vista con e senza, abilita' attiva (luce fissa: la luce misurata oscilla)
            Stealth.lumOverride = 0.5f;
            yield return new WaitForSeconds(0.6f);               // Stealth.Tick rilegge la luce ogni 0,2 s
            float sBase = Stealth.SightRange(null);
            Perks.Add(PerkId.Velo);
            float sPerk = Stealth.SightRange(null);
            Check("Velo: la vista dei preti cala del 30%", Mathf.Abs(sPerk / sBase - 0.7f) < 0.02f, $"{sBase:F1} m -> {sPerk:F1} m");
            Check("Velo attivo: parte col tasto", Perks.TryVelo() && Perks.VeloOn);
            float sCloak = Stealth.SightRange(null);
            Check("Velo attivo: i preti ti vedono solo a due passi", sCloak <= 1.5f, $"{sCloak:F1} m");
            Check("Velo: non si riusa subito", !Perks.TryVelo());
            yield return new WaitForSeconds(5.3f);
            Check("Velo: dopo cinque secondi il mantello cade", !Perks.VeloOn && Mathf.Abs(Stealth.SightRange(null) - sPerk) < 0.5f);
            Check("Velo: resta il tempo di recupero", Perks.VeloCooldownFrac > 0.8f && !Perks.TryVelo(), $"{Perks.VeloCooldownFrac:F2}");
            Stealth.lumOverride = float.NaN;
            Perks.ResetRun();

            // Occhio della Torre: finestra dopo il ping
            Perks.Add(PerkId.Occhio);
            Check("Occhio: prima del ping le etichette sono quelle di sempre", !Perks.EyeOn);
            Perks.OnPing();
            Check("Occhio: dopo il ping si vedono i preti nei muri", Perks.EyeOn && Perks.EyeLeft > 5f, $"{Perks.EyeLeft:F1} s");
            Perks.ResetRun();
            Perks.OnPing();
            Check("senza il perk il ping non mostra nulla di piu'", !Perks.EyeOn);

            // Sete di sangue
            Perks.Add(PerkId.SeteDiSangue);
            var victim = Enemy.Spawn(Enemy.EnemyType.Monaco, NavPoint(pl.transform.position + Vector3.right * 6f));
            pl.hp = 40f;
            gm.OnEnemyKilled(victim, false, DamageSource.Gun);
            Check("Sete di sangue: chi spara non si cura", Mathf.Approximately(pl.hp, 40f), $"hp {pl.hp:F0}");
            gm.OnEnemyKilled(victim, false, DamageSource.Knife);
            Check("Sete di sangue: il coltello cura 25", Mathf.Abs(pl.hp - 65f) < 0.5f, $"hp {pl.hp:F0}");
            gm.OnEnemyKilled(victim, false, DamageSource.Takedown);
            Check("Sete di sangue: la presa alle spalle cura 40", Mathf.Abs(pl.hp - 100f) < 0.5f, $"hp {pl.hp:F0}");
            Perks.ResetRun();
            Enemy.ClearAll();

            // Occhio e Consacrazione sul danno: lo stesso colpo alla testa di un monaco fermo
            Log("--- danno");
            Enemy.debugNoSense = true;
            if (FindOpenSpot(fd.cur, out Vector3 sp2, out Vector3 dir2, 3.0f))       // il tiratore sta a 3 m: quel punto dev'essere libero, non un muro
            {
                var dummy = Enemy.Spawn(Enemy.EnemyType.Monaco, sp2);
                dummy.transform.rotation = Quaternion.LookRotation(-dir2);
                dummy.maxHp = dummy.hp = 100000f;               // i colpi non lo uccidono: si misura la vita persa
                pl.Teleport(NavPoint(sp2 - dir2 * 3.0f));
                Weapon.debugAds = true;
                yield return new WaitForSeconds(0.9f);
                // un colpo alla testa, mirato al centro del suo collider: restituisce la vita persa (o -1 senza collider)
                bool diagOnce = true;
                IEnumerator HeadShot(System.Action<float> done)
                {
                    Collider headCol = null;
                    foreach (var c in dummy.GetComponentsInChildren<Collider>()) if (c.CompareTag("Head")) headCol = c;
                    if (headCol == null) { done(-1f); yield break; }
                    Aim(pl, headCol.bounds.center);
                    yield return new WaitForSeconds(0.3f);
                    if (diagOnce)
                    {
                        diagOnce = false;
                        var hs = Physics.RaycastAll(pl.cam.position, pl.cam.forward, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        System.Array.Sort(hs, (a, b) => a.distance.CompareTo(b.distance));
                        var sb = new System.Text.StringBuilder();
                        for (int i = 0; i < hs.Length && i < 5; i++) sb.Append($"{hs[i].collider.name}({hs[i].collider.GetType().Name},{hs[i].collider.tag}) {hs[i].distance:F2} m; ");
                        Log($"raggio verso la testa: occhio a {headCol.bounds.center - dummy.transform.position} dal monaco, camera a {pl.cam.position.y - pl.transform.position.y:F2} m; {sb}");
                    }
                    dummy.hp = dummy.maxHp;
                    wp.mag = wp.magSize;
                    wp.DebugFire();
                    yield return null;
                    done(dummy.maxHp - dummy.hp);
                    yield return new WaitForSeconds(0.2f);
                }
                float hitBase = 0f, hitEye = 0f;
                yield return HeadShot(v => hitBase = v);
                Perks.Add(PerkId.Occhio);
                yield return HeadShot(v => hitEye = v);
                Perks.ResetRun();
                Log($"colpo alla testa: base {hitBase:F1}, con Occhio {hitEye:F1}");
                Check("Occhio: il colpo alla testa fa 1,25 volte", hitBase > 1f && Mathf.Abs(hitEye / hitBase - 1.25f) < 0.1f, $"{hitBase:F1} -> {hitEye:F1}");

                // Consacrazione: altare, effetti, danno
                Log("--- Consacrazione");
                SetScore(6000);
                wp.Equip(WeaponDefs.IndexOf("p9_pieta"));
                yield return new WaitForSeconds(0.8f);
                if (shots && hasLib)
                {
                    // l'arma com'e' prima dell'altare, nello stesso posto illuminato dove poi si fotografa quella d'oro
                    Weapon.debugAds = false;
                    Vector3 back = pl.transform.position;
                    yield return Face(libNear, mid + Vector3.up * 1.1f);
                    yield return new WaitForSeconds(0.6f);
                    Shot("libri_arma_base");
                    pl.Teleport(back);
                    Weapon.debugAds = true;
                    yield return new WaitForSeconds(0.3f);
                }
                int magBase = wp.magSize, resBase = wp.reserveMax;
                Check("prima dell'altare l'arma non e' consacrata", !wp.Upgraded && !gm.hud.CurrentWeaponName.Contains("CONSACRATA"));
                before = gm.score;
                altar.Use();
                Check("l'altare costa 3000 e consacra l'arma", wp.Upgraded && before - gm.score == 3000, $"{before - gm.score} punti");
                Check("caricatore e riserva x1,5, caricatore pieno", wp.magSize == Mathf.CeilToInt(magBase * 1.5f) && wp.reserveMax == Mathf.CeilToInt(resBase * 1.5f) && wp.mag == wp.magSize, $"{magBase} -> {wp.magSize}, riserva {resBase} -> {wp.reserveMax}");
                Check("il nome nell'HUD e' dorato con la scritta", gm.hud.CurrentWeaponName.Contains("CONSACRATA"), gm.hud.CurrentWeaponName);
                before = gm.score;
                altar.Use();
                Check("una volta sola per arma", gm.score == before);
                Check("il prompt dice che e' gia' consacrata", altar.Prompt != null && altar.Prompt.Contains("GIÀ CONSACRATA"), altar.Prompt ?? "null");
                yield return new WaitForSeconds(0.6f);
                float hitUpg = 0f;
                yield return HeadShot(v => hitUpg = v);
                Check("Consacrazione: il danno e' 1,7 volte", hitBase > 1f && Mathf.Abs(hitUpg / hitBase - Weapon.UpgDamage) < 0.12f, $"{hitBase:F1} -> {hitUpg:F1}");
                // ricomprare l'arma o riempirla non toglie la consacrazione, un'altra arma non e' consacrata, tornando indietro lo e' di nuovo
                wp.reserve = 0;
                wp.FillReserve();
                Check("riempire la riserva tiene la consacrazione", wp.Upgraded && wp.reserve == wp.reserveMax, $"{wp.reserve}/{wp.reserveMax}");
                wp.Equip(WeaponDefs.IndexOf("p9_pieta"));
                Check("ricomprare la stessa arma non la sconsacra", wp.Upgraded);
                int other = Scrigno.PickWeapon(wp, fd.floor);
                wp.Equip(other);
                Check("un'altra arma non e' consacrata", !wp.Upgraded);
                wp.SwitchSlot(0);
                yield return new WaitForSeconds(0.5f);
                Check("tornando alla prima e' di nuovo consacrata", wp.Upgraded && wp.CurrentIndex == WeaponDefs.IndexOf("p9_pieta"));
                if (shots && hasLib)
                {
                    Weapon.debugAds = false;
                    Enemy.ClearAll();
                    yield return Face(libNear, mid + Vector3.up * 1.1f);
                    yield return new WaitForSeconds(1.0f);
                    Shot("libri_arma_oro");
                }
                Enemy.ClearAll();
                Weapon.debugAds = false;
            }
            else Log("nessuno spiazzo per il colpo alla testa (Occhio e Consacrazione provati solo a numeri)");

            // ----- 6) lo Scrigno
            Log("--- Scrigno");
            wp.Equip(WeaponDefs.IndexOf("p9_pieta"));
            SetScore(10);
            chest.Use();
            Check("con 10 punti lo Scrigno non si apre", chest.phase == Scrigno.Phase.Closed && gm.score == 10);
            SetScore(5000);
            if (hasChest) yield return Face(chestStand, chest.Point);
            before = gm.score;
            chest.Use();
            Check("lo Scrigno costa 950 e si apre", before - gm.score == 950 && chest.phase == Scrigno.Phase.Opening, $"{before - gm.score} punti");
            Check("durante l'apertura non ha prompt", chest.Prompt == null);
            if (shots)
            {
                // la sequenza dell'apertura vista dal posto del giocatore (scrigno_apre_<centesimi di secondo>.png): si compone in un foglio
                float to0 = Time.time;
                foreach (float at in new[] { 0.10f, 0.35f, 0.65f, 0.95f, 1.20f, 1.40f, 1.60f, 2.00f })
                {
                    while (Time.time - to0 < at) yield return null;
                    GameManager.CamShot(Dir + "/scrigno_apre_" + Mathf.RoundToInt(at * 100f).ToString("D3") + ".png", null, false);
                }
                while (Time.time - to0 < 2.4f) yield return null;
                Shot("libri_scrigno_giro");
                yield return new WaitForSeconds(0.9f);
            }
            float tw0 = Time.time;
            while (chest.phase != Scrigno.Phase.Offering && Time.time - tw0 < 12f) yield return null;
            Check("dopo il giro offre un'arma", chest.phase == Scrigno.Phase.Offering && chest.OfferedWeapon >= 0, chest.phase.ToString());
            int offered = chest.OfferedWeapon;
            Check("l'arma offerta e' una che non hai", offered >= 0 && !wp.Owns(offered) && offered != WeaponDefs.IndexOf("p9_pieta"), offered >= 0 ? WeaponDefs.All[offered].name : "-");
            Log("prompt offerta: " + (chest.Prompt ?? "null"));
            if (shots)
            {
                yield return new WaitForSeconds(0.6f);
                Shot("libri_scrigno_arma");
                // di fianco: la volta, il fermo e il tesoro di profilo; poi si torna davanti
                if (StandNear(chest.transform.position + Vector3.up * 1.4f, chest.transform.right, 1.9f, out Vector3 sideStand))
                {
                    yield return Face(sideStand, chest.Point);
                    GameManager.CamShot(Dir + "/scrigno_lato.png", null, false);
                    yield return Face(chestStand, chest.Point);
                }
            }
            chest.Use();
            Check("F prende l'arma", offered >= 0 && wp.Owns(offered) && wp.CurrentIndex == offered, offered >= 0 ? WeaponDefs.All[offered].name : "-");
            if (shots)
            {
                // la chiusura: l'arma vola via, il coperchio cade, batte, il fermaglio scatta (scrigno_chiude_<centesimi>.png)
                float tc0 = Time.time;
                foreach (float at in new[] { 0.10f, 0.25f, 0.50f, 0.75f, 0.90f, 1.10f })
                {
                    while (Time.time - tc0 < at) yield return null;
                    GameManager.CamShot(Dir + "/scrigno_chiude_" + Mathf.RoundToInt(at * 100f).ToString("D3") + ".png", null, false);
                }
            }
            tw0 = Time.time;
            while (chest.phase != Scrigno.Phase.Closed && Time.time - tw0 < 5f) yield return null;
            Check("lo Scrigno si richiude", chest.phase == Scrigno.Phase.Closed);
            // tempo scaduto: l'arma si perde, i punti restano persi
            chest.rollTime = 0.8f; chest.offerTime = 2.0f;
            SetScore(2000);
            chest.Use();
            tw0 = Time.time;
            while (chest.phase != Scrigno.Phase.Offering && Time.time - tw0 < 6f) yield return null;
            int lost = chest.OfferedWeapon;
            tw0 = Time.time;
            while (chest.phase != Scrigno.Phase.Closed && Time.time - tw0 < 8f) yield return null;
            Check("se non la prendi in tempo lo Scrigno si richiude senza arma", chest.phase == Scrigno.Phase.Closed && lost >= 0 && !wp.Owns(lost) && gm.score == 2000 - Scrigno.Price, $"punti {gm.score}");
            // statistica dei gradi: piu' si scende, piu' pesano i gradi alti; mai la pistola di partenza
            int[] g1 = new int[6], g7 = new int[6];
            bool bad = false;
            for (int i = 0; i < 1500; i++)
            {
                int a = Scrigno.PickWeapon(wp, 1), b = Scrigno.PickWeapon(wp, 7);
                if (a == WeaponDefs.IndexOf("p9_pieta") || b == WeaponDefs.IndexOf("p9_pieta") || wp.Owns(a) || wp.Owns(b)) bad = true;
                g1[WeaponDefs.All[a].grade]++; g7[WeaponDefs.All[b].grade]++;
            }
            Log($"gradi al piano 1: {g1[1]} {g1[2]} {g1[3]} {g1[4]} {g1[5]}   al piano 7: {g7[1]} {g7[2]} {g7[3]} {g7[4]} {g7[5]}");
            Check("lo Scrigno non offre la pistola di partenza ne' armi che hai", !bad);
            Check("escono tutti i cinque gradi, e al piano 7 piu' alti", g7[5] > g1[5] && g1[1] > g7[1] && g7[5] > 0 && g1[5] > 0);

            // ----- 7) la morte pesa: meta' dei punti e l'ultimo perk
            Log("--- morte");
            Perks.ResetRun();
            Perks.Add(PerkId.PelleDiPietra); Perks.Add(PerkId.DoppioColpo);
            SetScore(3000);
            pl.invulnerable = false;
            Enemy.ClearAll();
            pl.Hurt(9999f);
            Check("morire costa meta' dei punti e l'ultimo perk", !pl.alive && gm.score == 1500 && Perks.Count == 1 && Perks.Has(PerkId.PelleDiPietra) && !Perks.Has(PerkId.DoppioColpo), $"vivo {pl.alive}, punti {gm.score}, perk {Perks.Count}");
            yield return new WaitForSeconds(4.0f);
            Check("rinasci con la vita massima del momento", pl.alive && Mathf.Approximately(pl.hp, pl.MaxHp), $"hp {pl.hp:F0}/{pl.MaxHp:F0}");
            Perks.ResetRun(); pl.OnPerksChanged(false);
            pl.invulnerable = true;

            // ----- 8) l'HUD con quattro perk
            if (shots)
            {
                Log("--- HUD");
                SetScore(1234);
                Perks.Add(PerkId.Velo); Perks.Add(PerkId.Occhio); Perks.Add(PerkId.SeteDiSangue); Perks.Add(PerkId.PassiDiCenere);
                Perks.TryVelo();
                Perks.OnPing();
                if (StandNear(mid + Vector3.up * 1.3f, libFront, 3.6f, out Vector3 hudStand)) yield return Face(hudStand, mid + Vector3.up * 1.0f);
                yield return new WaitForSeconds(0.9f);
                Shot("libri_hud_perk");
                Perks.ResetRun(); pl.OnPerksChanged(false);
                Perks.Add(PerkId.PelleDiPietra); Perks.Add(PerkId.ManiSvelte); Perks.Add(PerkId.DoppioColpo); Perks.Add(PerkId.Rinascita);
                yield return new WaitForSeconds(0.9f);
                Shot("libri_hud_perk2");
                Perks.ResetRun(); pl.OnPerksChanged(false);
            }
            if (torch != null && torch.on) torch.Toggle();
            Weapon.debugAds = false;
            PlayerController.botDir = Vector3.zero;

            Log(passed ? "ESITO: ok" : "ESITO: FALLITO");
            Application.Quit(passed ? 0 : 1);
        }
    }
}
