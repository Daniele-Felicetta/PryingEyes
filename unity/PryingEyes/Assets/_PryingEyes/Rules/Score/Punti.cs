namespace PryingEyes
{
    /// <summary>Da dove arriva il colpo che ferisce un nemico: decide quanto vale l'uccisione (e se fa rumore).</summary>
    public enum DamageSource
    {
        Gun,        // arma da fuoco
        Knife,      // coltello, in faccia: rumoroso come una rissa
        Takedown,   // coltello alle spalle su chi non ti ha notato: nessun rumore
        Nuke,       // la Campana: uccide senza pagare l'uccisione, il premio e' il bonus fisso
        Esplosione, // scoppio dell'Appestato o la sua nube: paga come un'arma
        Scoppio,    // l'Appestato che scoppia da solo vicino a te: nessun premio
        Magia,      // magie della mano sinistra, sigilli sull'arma, danni nel tempo (fuoco, emorragia, corrosione): paga come un'arma
        Moto,       // la moto del deserto che investe chi cammina (PlayerController.Ride.Plow.cs): paga mezza uccisione, senza rischio e senza munizioni
    }

    /// <summary>Tabella dei punti, quella dei Black Ops Zombies: 10 a ogni colpo che non uccide, 60 per l'uccisione, 100 se
    /// letale alla testa, 130 col coltello. (Prima il colpo letale pagava anche i suoi 10: 60 al corpo, 110 alla testa.)</summary>
    public static class Punti
    {
        /// <summary>L'ultima fonte di danno: la rete accetta le fonti fino a questa (i numeri viaggiano, le nuove vanno in fondo).</summary>
        public const DamageSource LastSource = DamageSource.Moto;
        public const int Colpo = 10, Uccisione = 60, Testa = 100, Coltello = 130, Investito = 30;
        /// <summary>Bonus per una candela accesa senza che nessun prete ti abbia visto dall'ultima (FloorDirector.OnCandle).</summary>
        public const int Fantasma = 200;
        /// <summary>Per le prove automatiche (-survivaltest): i punti pagati a questa macchina per i colpi che non uccidono (Colpo l'uno),
        /// compresi quelli che lo scoppio di un Appestato fa a chi gli sta accanto e sopravvive.</summary>
        public static int paidForHits;

        public static int PerUccisione(DamageSource src, bool headshot)
        {
            if (src == DamageSource.Nuke || src == DamageSource.Scoppio) return 0;
            if (src == DamageSource.Knife || src == DamageSource.Takedown) return Coltello;
            if (src == DamageSource.Moto) return Investito;           // la moto non spara alla testa e non ha rischio: la meta' di un'uccisione (le medaglie delle serie pagano a parte)
            return headshot ? Testa : Uccisione;
        }
    }
}
