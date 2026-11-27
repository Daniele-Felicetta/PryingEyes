#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -walltest: davanti a un espositore di armi del piano (il piu' vicino al giocatore: FloorDirector.PlaceWallBuys) verifica il prompt, l'acquisto con F, la spesa dei punti e
    /// la ricarica della riserva a meta' prezzo, poi l'animazione di ricarica con la foto a meta' (hotel_build/reload.png) e i contatori dell'HUD.
    /// Era una procedura di GameManager, portata cosi' com'era: la sonda stava a x -10, z 20,2, davanti all'espositore della VESPER-9 sul muro sud della hall classica, che i piani della torre non
    /// hanno (li' davanti alla sonda non c'e' niente, il prompt restava vuoto e i primi quattro controlli fallivano). Ora la sonda si mette a 2,2 m davanti all'espositore che il piano ha davvero (piu'
    /// vicino se un arredo copre la visuale): l'arma cambia con il piano e col seme, e i punti si portano al prezzo se non bastano (il controllo e' sulla meccanica, non sul listino).</summary>
    public sealed class WallTest : PlayerTest
    {
        protected override string Suite { get { return "walltest"; } }
        /// <summary>La procedura non ha mai contato le eccezioni, e la foto della ricarica senza scheda grafica (-nographics) fa lanciare a URP la NullReferenceException del render graph (una, la stessa prima del porting): i controlli restano quelli che ha sempre avuto.</summary>
        protected override string ExceptionCheck { get { return null; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<WallTest>("-walltest"); }

        /// <summary>L'espositore a pagamento piu' vicino a un punto, di un'arma che il giocatore non ha gia' in mano (null se il piano non ne ha: con un'arma che si ha gia' il prompt sarebbe quello
        /// delle munizioni, con un pannello gratuito "PRENDI", e questa prova guarda l'acquisto).</summary>
        static WallBuy NearestDisplay(Vector3 from, Weapon carried)
        {
            WallBuy best = null;
            float bestSqr = float.MaxValue;
            foreach (var w in FindObjectsByType<WallBuy>(FindObjectsSortMode.None))
            {
                if (w.Free || w.price <= 0 || carried.SlotOf(w.weaponIndex) >= 0) continue;
                float sqr = (w.transform.position - from).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = w; }
            }
            return best;
        }

        protected override IEnumerator Run()
        {
            var gm = GameManager.I;
            while (!gm.LevelReady || Time.timeSinceLevelLoad < 3f) yield return null;      // GameManager la faceva partire dopo 3 s di prova automatica
            var player = gm.player; var hud = gm.hud;
            if (!Check("il giocatore ha un'arma", player != null && player.weapon != null)) yield break;
            var display = NearestDisplay(player.transform.position, player.weapon);
            if (display == null) { NotApplicable("il piano non ha nessun espositore a pagamento di un'arma che il giocatore non ha (cambia seme o piano)"); yield break; }
            // in autotest la camera del giocatore punta ai nemici: uso una sonda fissa davanti all'espositore (il suo davanti guarda la stanza)
            var probe = new GameObject("WallTestProbe");
            string prompt = "";
            foreach (float dist in new[] { 2.2f, 1.8f, 1.4f, 1.0f })
            {
                probe.transform.position = display.transform.position + display.transform.forward * dist;
                probe.transform.rotation = Quaternion.LookRotation(display.transform.position - probe.transform.position);
                yield return null;
                WallBuy.Tick(probe.transform);
                prompt = hud != null ? hud.CurrentPrompt : "";
                if (prompt.Length > 0) break;
            }
            Debug.Log("[PryingEyes] walltest espositore " + display.displayName + " a " + display.price + " punti, prompt=\"" + prompt + "\" score=" + gm.score);
            Check("davanti all'espositore il prompt offre l'acquisto", prompt.StartsWith("ACQUISTA") && prompt.Contains("PUNTI"), "prompt \"" + prompt + "\"");
            int topUp = 0;
            if (gm.score < display.price) { topUp = display.price - gm.score; gm.AddPoints(topUp); }      // il listino cambia da un piano all'altro: qui si prova la meccanica
            int score0 = gm.score; string weapon0 = player.weapon.Def.name;
            WallBuy.TryBuy();
            Debug.Log("[PryingEyes] walltest dopo acquisto: score=" + gm.score + " arma=" + player.weapon.Def.name
                + " mag=" + player.weapon.mag + " riserva=" + player.weapon.reserve
                + " hud=" + (hud != null ? hud.CurrentAmmo + " " + hud.CurrentReserve : ""));
            int price = score0 - gm.score;
            Check("l'acquisto con F spende i punti e cambia l'arma", price > 0 && player.weapon.Def.name != weapon0,
                  "punti " + score0 + " -> " + gm.score + ", arma " + weapon0 + " -> " + player.weapon.Def.name + (topUp > 0 ? " (portati al prezzo: +" + topUp + ")" : ""));
            yield return null;
            gm.AddPoints(Mathf.Max(300, display.AmmoPrice));
            player.weapon.reserve = 5;
            WallBuy.Tick(probe.transform);
            string prompt2 = hud != null ? hud.CurrentPrompt : "";
            Debug.Log("[PryingEyes] walltest prompt munizioni=\"" + prompt2 + "\"");
            Check("con la stessa arma il prompt offre le munizioni", prompt2.StartsWith("MUNIZIONI"), "prompt \"" + prompt2 + "\"");
            int score1 = gm.score;
            WallBuy.TryBuy();
            Debug.Log("[PryingEyes] walltest dopo munizioni: score=" + gm.score
                + " riserva=" + player.weapon.reserve + "/" + player.weapon.reserveMax
                + " hud=" + (hud != null ? hud.CurrentAmmo + " " + hud.CurrentReserve : ""));
            int ammoCost = score1 - gm.score;
            float half = Mathf.Max(150f, price * 0.5f);
            Check("le munizioni costano meta' prezzo e riempiono la riserva", price > 0 && Mathf.Abs(ammoCost - half) <= 1f && player.weapon.reserve > 5,
                  "costo " + ammoCost + " (meta' prezzo " + half + "), riserva " + player.weapon.reserve + "/" + player.weapon.reserveMax);

            // animazione di ricarica: foto a metà e controllo che il contatore si aggiorni
            yield return new WaitForSeconds(0.25f);
            player.weapon.mag = 3;
            player.weapon.Reload();
            yield return new WaitForSeconds(0.70f);
            GameManager.CamShot(TestPaths.Out("reload.png"), Camera.main, true);
            string during = hud.CurrentAmmo + " " + hud.CurrentReserve;
            Debug.Log("[PryingEyes] walltest hud durante ricarica=" + during);
            yield return new WaitForSeconds(1.20f);
            string after = hud.CurrentAmmo + " " + hud.CurrentReserve;
            Debug.Log("[PryingEyes] walltest hud dopo ricarica=" + after);
            Check("la ricarica aggiorna i contatori dell'HUD", during != after && hud.CurrentAmmo == player.weapon.mag.ToString(),
                  "durante " + during + ", dopo " + after + ", arma " + player.weapon.mag + "/" + player.weapon.reserve);
            Destroy(probe);
        }
    }
}
#endif
