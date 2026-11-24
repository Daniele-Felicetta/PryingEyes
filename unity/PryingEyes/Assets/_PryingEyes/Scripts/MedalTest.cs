#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -medaltest: le medaglie (Medals.cs). Tre uccisioni a catena danno Doppietta e Tripletta coi
    /// loro punti e il riquadro in alto; dopo una pausa la catena riparte; cinque di fila alla testa danno Mira divina; tre nello stesso
    /// scoppio Fuoco purificatore; venticinque senza ferite Intoccabile (una ferita azzera la serie); con la vita al 10% Ultimo respiro;
    /// tre di coltello Lama santa.</summary>
    public sealed class MedalTest : PlayerTest
    {
        protected override string Suite { get { return "medaltest"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<MedalTest>("-medaltest"); }

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll();
            var gm = GameManager.I; var player = gm.player;
            player.invulnerable = true;
            Medals.Reset();
            yield return new WaitForSeconds(1f);

            // a catena
            int s0 = gm.score;
            for (int i = 0; i < 3; i++) { gm.CreditKill(DamageSource.Gun, false); yield return new WaitForSeconds(0.2f); }
            int kills3 = 3 * Punti.PerUccisione(DamageSource.Gun, false);
            Check($"tre a catena: Doppietta e Tripletta ({string.Join(", ", Medals.Awarded)})", Medals.Awarded.Count == 2 && Medals.Awarded[0] == "Doppietta" && Medals.Awarded[1] == "Tripletta");
            Check($"coi loro punti ({gm.score - s0} = {kills3} + 70)", gm.score - s0 == kills3 + 70);
            Check($"il riquadro in alto la mostra ({gm.hud?.MedalShown})", gm.hud != null && gm.hud.MedalShown == "TRIPLETTA");
            yield return new WaitForSeconds(1f);
            gm.CreditKill(DamageSource.Gun, false);
            Check("dopo una pausa la catena riparte (nessuna medaglia)", Medals.Awarded.Count == 2);

            // alla testa, di fila (lontane: niente catena)
            for (int i = 0; i < 5; i++) { yield return new WaitForSeconds(0.8f); gm.CreditKill(DamageSource.Gun, true); }
            Check($"cinque di fila alla testa: Mira divina ({Medals.Last})", Medals.Last == "Mira divina");

            // tre nello stesso scoppio
            yield return new WaitForSeconds(1f);
            for (int i = 0; i < 3; i++) gm.CreditKill(DamageSource.Esplosione, false);
            Check("tre nello stesso scoppio: Fuoco purificatore", Medals.Awarded.Contains("Fuoco purificatore"));

            // senza ferite
            yield return new WaitForSeconds(1f);
            Medals.OnHurt();
            int before = Medals.Awarded.FindAll(n => n == "Intoccabile").Count;
            for (int i = 0; i < 24; i++) { gm.CreditKill(DamageSource.Gun, false); if (i % 3 == 2) yield return new WaitForSeconds(0.8f); }
            Check("ventiquattro senza ferite: non ancora", Medals.Awarded.FindAll(n => n == "Intoccabile").Count == before);
            yield return new WaitForSeconds(0.8f);
            gm.CreditKill(DamageSource.Gun, false);
            Check("la venticinquesima: Intoccabile", Medals.Awarded.FindAll(n => n == "Intoccabile").Count == before + 1);

            // all'ultimo respiro
            yield return new WaitForSeconds(1f);
            player.hp = player.MaxHp * 0.1f;
            gm.CreditKill(DamageSource.Gun, false);
            Check($"con la vita al 10%: Ultimo respiro ({Medals.Last})", Medals.Last == "Ultimo respiro");
            player.hp = player.MaxHp;

            // di coltello
            yield return new WaitForSeconds(1f);
            for (int i = 0; i < 3; i++) { gm.CreditKill(DamageSource.Knife, false); yield return new WaitForSeconds(0.9f); }
            Check("tre di coltello: Lama santa", Medals.Awarded.Contains("Lama santa"));
            player.invulnerable = false;
        }
    }
}
#endif
