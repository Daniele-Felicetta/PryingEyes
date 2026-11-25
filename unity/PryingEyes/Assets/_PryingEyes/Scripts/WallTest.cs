#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -walltest: davanti all'espositore della VESPER-9 (sul muro sud della hall) verifica il prompt, l'acquisto con F, la spesa dei punti e
    /// la ricarica della riserva a meta' prezzo, poi l'animazione di ricarica con la foto a meta' (hotel_build/reload.png) e i contatori dell'HUD.
    /// Era una procedura di GameManager, portata cosi' com'e'.
    /// STATO: la scena e' quella della hall classica (la sonda sta a x -10, z 20,2, davanti al muro sud), che i piani della torre non hanno: li' davanti alla sonda non c'e'
    /// nessun espositore, il prompt resta vuoto, nessun acquisto avviene e i primi quattro controlli falliscono, come prima del porting (il vecchio log diceva prompt="").
    /// Il controllo dei contatori dell'HUD non dipende dall'espositore e regge. Non riparata.</summary>
    public sealed class WallTest : PlayerTest
    {
        protected override string Suite { get { return "walltest"; } }
        /// <summary>La procedura non ha mai contato le eccezioni, e la foto della ricarica senza scheda grafica (-nographics) fa lanciare a URP la NullReferenceException del render graph (una, la stessa prima del porting): i controlli restano quelli che ha sempre avuto.</summary>
        protected override string ExceptionCheck { get { return null; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<WallTest>("-walltest"); }

        protected override IEnumerator Run()
        {
            var gm = GameManager.I;
            while (!gm.LevelReady || Time.timeSinceLevelLoad < 3f) yield return null;      // GameManager la faceva partire dopo 3 s di prova automatica
            var player = gm.player; var hud = gm.hud;
            if (!Check("il giocatore ha un'arma", player != null && player.weapon != null)) yield break;
            // in autotest la camera del giocatore punta ai nemici: uso una sonda fissa davanti alla VESPER
            var probe = new GameObject("WallTestProbe");
            probe.transform.position = new Vector3(-10f, 1.55f, 20.2f);
            probe.transform.rotation = Quaternion.LookRotation(new Vector3(-10f, 1.55f, 22.42f) - probe.transform.position);
            yield return null;
            WallBuy.Tick(probe.transform);
            string prompt = hud != null ? hud.CurrentPrompt : "";
            Debug.Log("[PryingEyes] walltest prompt=\"" + prompt + "\" score=" + gm.score);
            Check("davanti all'espositore il prompt offre l'acquisto", prompt.StartsWith("ACQUISTA") && prompt.Contains("PUNTI"), "prompt \"" + prompt + "\"");
            int score0 = gm.score; string weapon0 = player.weapon.Def.name;
            WallBuy.TryBuy();
            Debug.Log("[PryingEyes] walltest dopo acquisto: score=" + gm.score + " arma=" + player.weapon.Def.name
                + " mag=" + player.weapon.mag + " riserva=" + player.weapon.reserve
                + " hud=" + (hud != null ? hud.CurrentAmmo + " " + hud.CurrentReserve : ""));
            int price = score0 - gm.score;
            Check("l'acquisto con F spende i punti e cambia l'arma", price > 0 && player.weapon.Def.name != weapon0,
                  "punti " + score0 + " -> " + gm.score + ", arma " + weapon0 + " -> " + player.weapon.Def.name);
            yield return null;
            gm.AddPoints(300);
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
