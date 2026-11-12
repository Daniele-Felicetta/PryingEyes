using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{


    /// <summary>I perk della Torre (fase 3): gli stessi dei Black Ops Zombies (Juggernog, Speed Cola, Double Tap, Quick Revive...) ma
    /// custoditi in libri magici, uno per ciascuno. Quattro posti, prezzo progressivo (2000, 2500, 3000, 4000): il primo libro costa
    /// meno, l'ultimo di piu'. Si comprano nella Biblioteca di ogni piano (Biblioteca.cs): tre libri su tre leggii, preso uno gli
    /// altri si richiudono. Qui stanno lo stato e gli effetti; i punti di aggancio sono nei punti in cui l'effetto agisce
    /// (Weapon, PlayerController, Stealth, Guide, GameManager).</summary>
    public static class Perks
    {
        public const int Slots = PlayerPerks.Slots;
        public static readonly int[] Prices = PlayerPerks.Prices;

        public sealed class Def
        {
            public PerkId id;
            public string name, effect, book;
            public Color color;
        }

        public static readonly Def[] All =
        {
            new Def { id = PerkId.PelleDiPietra, name = "Pelle di pietra", effect = "+50% di vita", book = "Il Libro della Pietra", color = new Color(0.90f, 0.46f, 0.30f) },
            new Def { id = PerkId.ManiSvelte, name = "Mani svelte", effect = "ricarichi il 40% più in fretta", book = "Il Libro delle Dita", color = new Color(0.40f, 0.66f, 1.00f) },
            new Def { id = PerkId.DoppioColpo, name = "Doppio colpo", effect = "cadenza di fuoco +33%", book = "Il Libro del Doppio", color = new Color(0.98f, 0.80f, 0.30f) },
            new Def { id = PerkId.Rinascita, name = "Rinascita", effect = "una rinascita a piano, sul posto", book = "Il Libro della Fenice", color = new Color(0.46f, 0.88f, 0.52f) },
            new Def { id = PerkId.PassiDiCenere, name = "Passi di cenere", effect = "i passi si sentono il 40% in meno", book = "Il Libro della Cenere", color = new Color(0.74f, 0.72f, 0.82f) },
            new Def { id = PerkId.Velo, name = "Velo", effect = "ti vedono il 30% in meno; [H] invisibile 5 s", book = "Il Libro del Velo", color = new Color(0.68f, 0.46f, 0.94f) },
            new Def { id = PerkId.Occhio, name = "Occhio della Torre", effect = "il ping mostra i nemici nei muri; testa x1,25", book = "Il Libro dell'Occhio", color = new Color(0.30f, 0.90f, 0.78f) },
            new Def { id = PerkId.SeteDiSangue, name = "Sete di sangue", effect = "le uccisioni col coltello ti curano", book = "Il Libro del Sangue", color = new Color(0.92f, 0.20f, 0.24f) },
        };

        public static Def Of(PerkId id) => All[(int)id];

        // Legacy single-player facade. Actors and network modules use their explicit PlayerPerks instance.
        static readonly PlayerPerks fallback = new PlayerPerks();
        static PlayerController Player => GameManager.I != null ? GameManager.I.player : null;
        public static PlayerPerks State => Player != null ? Player.PerkState : fallback;
        public static int Count => State.Count;
        public static PerkId At(int i) => State.At(i);
        public static bool Has(PerkId p) => State.Has(p);
        public static bool Full => State.Full;
        public static int NextPrice => State.NextPrice;
        public static bool Add(PerkId p) => State.Add(p);
        public static bool LoseLast(out PerkId lost) => State.LoseLast(out lost);
        public static void ResetRun() { State.ResetRun(); Biblioteca.ResetRun(); }
        public static void NewFloor() => State.NewFloor();
        public static float MaxHpMult => State.MaxHpMult;
        public static float MaxHp => State.MaxHp;
        public static float ReloadTime => State.ReloadTime;
        public static float RateMult => State.RateMult;
        public static float StepNoise => State.StepNoise;
        public static float SightMult => State.SightMult;
        public static float HeadMult => State.HeadMult;
        public static float BloodHeal(DamageSource src) => State.BloodHeal(src);
        public static bool ReviveReady => State.ReviveReady;
        public static bool TakeRevive() => State.TakeRevive();
        public const float VeloTime = PlayerPerks.VeloTime, VeloCooldown = PlayerPerks.VeloCooldown;
        public static bool VeloOn => State.VeloOn;
        public static float VeloLeft => State.VeloLeft;
        public static float VeloCooldownFrac => State.VeloCooldownFrac;
        public static bool TryVelo() => State.TryVelo();
        public const float EyeTime = PlayerPerks.EyeTime;
        public static bool EyeOn => State.EyeOn;
        public static float EyeLeft => State.EyeLeft;
        public static void OnPing() => State.OnPing();
        public static void Tick(float dt) => State.Tick(dt);
    }
}