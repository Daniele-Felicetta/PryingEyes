using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni del giocatore e dell'arma (2D). Stessa interfaccia di prima, ma i suoni vengono da SoundGen:
    /// prima erano sei bip a onda quadra suonati tutti dallo stesso punto.</summary>
    public static class Synth
    {
        public static void Shot() => Sfx.UI(SoundBank.Shot, 0.55f, Random.Range(0.96f, 1.04f));
        public static void Hit() => Sfx.UI(SoundBank.HitTick, 0.4f, Random.Range(0.97f, 1.03f));
        public static void Kill() => Sfx.UI(SoundBank.Kill, 0.5f);
        public static void Reload() => Sfx.UI(SoundBank.Reload, 0.55f);
        public static void Hurt() => Sfx.UI(SoundBank.Hurt, 0.6f, Random.Range(0.94f, 1.06f));
        /// <summary>Acquisto al muro: campanello breve + tic della cassa.</summary>
        public static void Buy() { Sfx.UI(SoundBank.Bell, 0.30f, 1.8f); Sfx.UI(SoundBank.HitTick, 0.35f, 1.4f); }
        public static void Empty() => Sfx.UI(SoundBank.HitTick, 0.25f, 0.75f);
        /// <summary>Passo del giocatore: 0 marmo, 1 moquette, 2 legno.</summary>
        public static void Step(int surface, float vol) => Sfx.UI(SoundBank.Step(surface), vol, Random.Range(0.92f, 1.08f));
        /// <summary>L'inizio ondata adesso lo annunciano campanello e ascensore (GameManager): niente fischio arcade.</summary>
        public static void Wave() { }
    }
}
