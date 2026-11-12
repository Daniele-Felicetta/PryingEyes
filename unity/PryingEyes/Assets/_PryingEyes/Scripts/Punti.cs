namespace PryingEyes
{
    /// <summary>Da dove arriva il colpo che ferisce un nemico: decide quanto vale l'uccisione (e se fa rumore).</summary>
    public enum DamageSource
    {
        Gun,        // arma da fuoco
        Knife,      // coltello, in faccia: rumoroso come una rissa
        Takedown,   // coltello alle spalle su chi non ti ha notato: nessun rumore
        Nuke,       // la Campana: uccide senza pagare l'uccisione, il premio e' il bonus fisso
    }

    /// <summary>Tabella dei punti, quella dei Black Ops Zombies: 10 a ogni colpo che non uccide, 60 per l'uccisione, 100 se
    /// letale alla testa, 130 col coltello. (Prima il colpo letale pagava anche i suoi 10: 60 al corpo, 110 alla testa.)</summary>
    public static class Punti
    {
        public const int Colpo = 10, Uccisione = 60, Testa = 100, Coltello = 130;
        /// <summary>Bonus per una candela accesa senza che nessun prete ti abbia visto dall'ultima (FloorDirector.OnCandle).</summary>
        public const int Fantasma = 200;

        public static int PerUccisione(DamageSource src, bool headshot)
        {
            if (src == DamageSource.Nuke) return 0;
            if (src == DamageSource.Knife || src == DamageSource.Takedown) return Coltello;
            return headshot ? Testa : Uccisione;
        }
    }
}
