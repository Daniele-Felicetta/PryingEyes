using UnityEngine;

namespace PryingEyes.Cattedrale
{
    /// <summary>Le modalita' del gioco a due zone e il bottino delle uccisioni.
    /// <para>Catacombe: i piani sono le catacombe sotto la cattedrale (cancelli e scale al posto degli ascensori, un tema per ramo, le risorse
    /// che cadono). E' il gioco normale; le prove automatiche restano sulla Torre di prima, salvo -catacombe o -cattedrale, e -torre torna alla
    /// Torre anche giocando.</para>
    /// <para>Cattedrale: da soli si parte dalla cattedrale, si sceglie il ramo e si risale con il bottino. In cooperativa e nello schermo
    /// condiviso si scende di piano in piano come prima (la cattedrale e' di una persona sola, per ora) e il bottino va subito nel magazzino.</para></summary>
    public static class Catacombe
    {
        static bool? attive, cattedrale;

        static bool Arg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs()) if (s == a) return true;
            return false;
        }

        public static bool Attive => attive ??= !Arg("-torre") && (!(GameManager.autoTest || VideoSettings.Automated) || Arg("-catacombe") || Arg("-cattedrale") || Arg("-cattedraletest"));

        /// <summary>Si parte dalla cattedrale (deciso all'inizio della partita: la cooperativa e lo schermo condiviso si sanno solo allora).</summary>
        public static bool Hub => Attive && !Coop.Active && !Seats.Split && (cattedrale ??= !Arg("-nocattedrale") && (!(GameManager.autoTest || VideoSettings.Automated) || Arg("-cattedrale") || Arg("-cattedraletest")));

        /// <summary>Per le prove: dimentica le scelte lette dalla riga di comando.</summary>
        public static void Reset() { attive = null; cattedrale = null; }

        /// <summary>Per le prove (-cattedraletest): il prossimo nemico ucciso lascia di sicuro qualcosa.</summary>
        public static bool forceNext;

        /// <summary>Chiamato da Enemy.Die su ogni macchina (anche per i pupazzi della cooperativa): ognuno tira a sorte il suo bottino.</summary>
        public static void OnKill(Enemy e, DamageSource src)
        {
            if (!Attive || !GameManager.proc || e == null || src == DamageSource.Scoppio) return;
            var fd = Proc.FloorDirector.I;
            if (fd == null || fd.InHub || fd.floor < 1) return;
            int ramo = Rami.RamoDi(fd.floor);
            bool knife = src == DamageSource.Knife || src == DamageSource.Takedown;
            var loot = Bottino.Uccisione(e.type.ToString(), ramo, knife, Fabbrica.BottinoMul, Uniform);
            if (forceNext && loot.IsEmpty) loot = Production.Economy.Bundle.One(Risorse.Ossa, 1);
            forceNext = false;
            if (!loot.IsEmpty) RisorsaPickup.Spawn(loot, e.transform.position);
        }

        /// <summary>Random.value di Unity puo' dare 1: il pacchetto vuole [0, 1).</summary>
        public static double Uniform() => Mathf.Min(Random.value, 0.99999f);
    }
}
