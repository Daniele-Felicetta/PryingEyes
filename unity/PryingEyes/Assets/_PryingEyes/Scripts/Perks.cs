using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    public enum PerkId { PelleDiPietra, ManiSvelte, DoppioColpo, Rinascita, PassiDiCenere, Velo, Occhio, SeteDiSangue }

    /// <summary>I perk della Torre (fase 3): gli stessi dei Black Ops Zombies (Juggernog, Speed Cola, Double Tap, Quick Revive...) ma
    /// custoditi in libri magici, uno per ciascuno. Quattro posti, prezzo progressivo (2000, 2500, 3000, 4000): il primo libro costa
    /// meno, l'ultimo di piu'. Si comprano nella Biblioteca di ogni piano (Biblioteca.cs): tre libri su tre leggii, preso uno gli
    /// altri si richiudono. Qui stanno lo stato e gli effetti; i punti di aggancio sono nei punti in cui l'effetto agisce
    /// (Weapon, PlayerController, Stealth, Guide, GameManager).</summary>
    public static class Perks
    {
        public const int Slots = 4;
        public static readonly int[] Prices = { 2000, 2500, 3000, 4000 };

        public sealed class Def
        {
            public PerkId id;
            public string name, effect, book;
            public Color color;
        }

        public static readonly Def[] All =
        {
            new Def { id = PerkId.PelleDiPietra, name = "Pelle di pietra", effect = "+50% di vita", book = "Il Libro della Pietra", color = new Color(0.90f, 0.46f, 0.30f) },
            new Def { id = PerkId.ManiSvelte, name = "Mani svelte", effect = "ricarichi il 40% piu' in fretta", book = "Il Libro delle Dita", color = new Color(0.40f, 0.66f, 1.00f) },
            new Def { id = PerkId.DoppioColpo, name = "Doppio colpo", effect = "cadenza di fuoco +33%", book = "Il Libro del Doppio", color = new Color(0.98f, 0.80f, 0.30f) },
            new Def { id = PerkId.Rinascita, name = "Rinascita", effect = "una rinascita a piano, sul posto", book = "Il Libro della Fenice", color = new Color(0.46f, 0.88f, 0.52f) },
            new Def { id = PerkId.PassiDiCenere, name = "Passi di cenere", effect = "i passi si sentono il 40% in meno", book = "Il Libro della Cenere", color = new Color(0.74f, 0.72f, 0.82f) },
            new Def { id = PerkId.Velo, name = "Velo", effect = "ti vedono il 30% in meno; [H] invisibile 5 s", book = "Il Libro del Velo", color = new Color(0.68f, 0.46f, 0.94f) },
            new Def { id = PerkId.Occhio, name = "Occhio della Torre", effect = "il ping mostra i nemici nei muri; testa x1,25", book = "Il Libro dell'Occhio", color = new Color(0.30f, 0.90f, 0.78f) },
            new Def { id = PerkId.SeteDiSangue, name = "Sete di sangue", effect = "le uccisioni col coltello ti curano", book = "Il Libro del Sangue", color = new Color(0.92f, 0.20f, 0.24f) },
        };

        public static Def Of(PerkId id) => All[(int)id];

        // ------------------------------------------------------------------ stato
        static readonly List<PerkId> owned = new List<PerkId>();
        public static int Count => owned.Count;
        public static PerkId At(int i) => owned[i];
        public static bool Has(PerkId p) => owned.Contains(p);
        public static bool Full => owned.Count >= Slots;
        /// <summary>Quanto costa il prossimo perk (dipende da quanti ne hai gia'); -1 con i quattro posti pieni.</summary>
        public static int NextPrice => Full ? -1 : Prices[owned.Count];

        /// <summary>Aggiunge un perk (senza pagare: lo fa chi chiama). Falso se c'e' gia' o i posti sono finiti.</summary>
        public static bool Add(PerkId p)
        {
            if (Has(p) || Full) return false;
            owned.Add(p);
            var pl = Player;
            if (pl != null) pl.OnPerksChanged(p == PerkId.PelleDiPietra);
            return true;
        }

        /// <summary>Alla morte si perde l'ultimo perk comprato (e meta' dei punti, in FloorDirector.OnRespawn).</summary>
        public static bool LoseLast(out PerkId lost)
        {
            lost = PerkId.PelleDiPietra;
            if (owned.Count == 0) return false;
            lost = owned[owned.Count - 1];
            owned.RemoveAt(owned.Count - 1);
            if (lost == PerkId.Velo) { veloLeft = 0f; veloCd = 0f; }
            var pl = Player;
            if (pl != null) pl.OnPerksChanged(false);
            return true;
        }

        public static void ResetRun()
        {
            owned.Clear();
            reviveUsed = false;
            veloLeft = veloCd = eyeLeft = 0f;
            Biblioteca.ResetRun();
        }

        /// <summary>Piano nuovo: la rinascita e' di nuovo disponibile, il mantello e l'occhio si spengono.</summary>
        public static void NewFloor()
        {
            reviveUsed = false;
            veloLeft = 0f; eyeLeft = 0f;
        }

        static PlayerController Player { get { var gm = GameManager.I; return gm != null ? gm.player : null; } }

        // ------------------------------------------------------------------ effetti passivi
        /// <summary>Pelle di pietra: la vita massima e' 150 invece di 100.</summary>
        public static float MaxHpMult => Has(PerkId.PelleDiPietra) ? 1.5f : 1f;
        public static float MaxHp => 100f * MaxHpMult;
        /// <summary>Mani svelte: durata della ricarica (moltiplicatore sul tempo).</summary>
        public static float ReloadTime => Has(PerkId.ManiSvelte) ? 0.6f : 1f;
        /// <summary>Doppio colpo: moltiplicatore sulla cadenza di fuoco.</summary>
        public static float RateMult => Has(PerkId.DoppioColpo) ? 1.33f : 1f;
        /// <summary>Passi di cenere: moltiplicatore sul raggio del rumore dei passi.</summary>
        public static float StepNoise => Has(PerkId.PassiDiCenere) ? 0.6f : 1f;
        /// <summary>Velo: moltiplicatore sulla portata della vista dei preti.</summary>
        public static float SightMult => Has(PerkId.Velo) ? 0.7f : 1f;
        /// <summary>Occhio della Torre: moltiplicatore sul danno alla testa.</summary>
        public static float HeadMult => Has(PerkId.Occhio) ? 1.25f : 1f;

        /// <summary>Sete di sangue: quanta vita rende un'uccisione. Il coltello fa rumore e rende poco, la presa alle spalle e'
        /// silenziosa e rende molto; chi spara non cura mai.</summary>
        public static float BloodHeal(DamageSource src)
        {
            if (!Has(PerkId.SeteDiSangue)) return 0f;
            return src == DamageSource.Takedown ? 40f : src == DamageSource.Knife ? 25f : 0f;
        }

        // ------------------------------------------------------------------ Rinascita
        static bool reviveUsed;
        public static bool ReviveReady => Has(PerkId.Rinascita) && !reviveUsed;
        /// <summary>Chiamato quando la vita arriva a zero: se la rinascita c'e' e non e' gia' stata usata su questo piano, la consuma.</summary>
        public static bool TakeRevive()
        {
            if (!ReviveReady) return false;
            reviveUsed = true;
            return true;
        }

        // ------------------------------------------------------------------ Velo attivo
        public const float VeloTime = 5f, VeloCooldown = 40f;
        static float veloLeft, veloCd;
        public static bool VeloOn => veloLeft > 0f;
        public static float VeloLeft => veloLeft;
        /// <summary>0..1: quanto manca prima di poterlo riusare (1 appena usato). Durante il mantello vale 1.</summary>
        public static float VeloCooldownFrac => veloLeft > 0f ? 1f : Mathf.Clamp01(veloCd / VeloCooldown);

        /// <summary>Si avvolge nel mantello: per 5 s i preti ti vedono solo a due passi. Poi 40 s di pausa.</summary>
        public static bool TryVelo()
        {
            if (!Has(PerkId.Velo) || veloLeft > 0f || veloCd > 0f) return false;
            veloLeft = VeloTime;
            veloCd = VeloCooldown + VeloTime;
            return true;
        }

        // ------------------------------------------------------------------ Occhio della Torre
        public const float EyeTime = 6f;
        static float eyeLeft;
        /// <summary>Per qualche secondo dopo un ping le etichette mostrano anche i nemici che non ti hanno ancora notato.</summary>
        public static bool EyeOn => eyeLeft > 0f;
        public static float EyeLeft => eyeLeft;
        public static void OnPing() { if (Has(PerkId.Occhio)) eyeLeft = EyeTime; }

        public static void Tick(float dt)
        {
            if (veloLeft > 0f) veloLeft = Mathf.Max(0f, veloLeft - dt);
            if (veloCd > 0f) veloCd = Mathf.Max(0f, veloCd - dt);
            if (eyeLeft > 0f) eyeLeft = Mathf.Max(0f, eyeLeft - dt);
        }
    }
}
