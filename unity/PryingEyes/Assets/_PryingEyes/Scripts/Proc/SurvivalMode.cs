using System;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Il ciclo della Torre: ondate globali che partono da sole, aree da aprire coi punti, nemici piu' veloci; niente candele da
    /// accendere e niente stealth (stile CoD Zombies, richiesta dell'utente del 2026-11-13). E' il ciclo predefinito della modalita'
    /// procedurale. Il vecchio ciclo a candele resta solo per le prove che lo verificano: -candles (o -legacycycle) lo sceglie ed e'
    /// implicito nei flag di LegacyFlags; -zombies riaccende comunque il nuovo ciclo (confronti a parita' di flag). -nowaves lascia
    /// ferme le ondate: piano, porte e punti restano quelli del nuovo ciclo (serve a fotografare e a misurare senza nemici).</summary>
    public static class SurvivalMode
    {
        /// <summary>Prove costruite sul ciclo a candele: candele da accendere, stealth, serrande di zona, coltello alle spalle, piante
        /// a ali (che non hanno una stanza di partenza). Con uno di questi flag il gioco gira come prima.</summary>
        static readonly string[] LegacyFlags =
        {
            "-candles", "-legacycycle", "-oldplan", "-stealthtest", "-roundtest", "-roundshots", "-knifetest", "-droptest",
            "-architecturesupply",
        };

        static int state;       // 0 = non letto, 1 = ciclo nuovo, 2 = ciclo a candele (solo la riga di comando: la modalita' procedurale si controlla ogni volta)
        static bool? noWaves;

        public static bool Enabled
        {
            get
            {
                if (!GameManager.proc) return false;
                if (state == 0) state = ReadCommandLine() ? 1 : 2;
                return state == 1;
            }
        }

        /// <summary>Solo per le prove: il nuovo ciclo senza ondate (nessun nemico nasce da solo). Vale con -nowaves e -noenemies; con -cooptest
        /// parte attivo e il copione lo toglie quando serve (Hold), cosi' le ondate non coprono i passi che contano punti e nemici uno per uno.</summary>
        public static bool HoldWaves => noWaves ??= Has("-nowaves") || Has("-noenemies") || Has("-cooptest");

        /// <summary>Solo per le prove: ferma o libera le ondate a mano.</summary>
        public static void Hold(bool on) => noWaves = on;

        static bool ReadCommandLine()
        {
            if (Has("-zombies")) return true;
            foreach (var flag in LegacyFlags) if (Has(flag)) return false;
            return true;
        }

        static bool Has(string flag) => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;

        // La pressione cresce fino alla decima ondata; resta possibile distanziare l'orda correndo.
        public static float SpeedMultiplier(int round, bool flying) =>
            flying ? 1.15f : Mathf.Lerp(1.6f, 2.2f, Mathf.Clamp01((round - 1) / 9f));

        /// <summary>Le candele restano nel piano come arredo (luce e fiamma) ma non si accendono piu' e non contano come obiettivo.</summary>
        public static void DecorationOnly(FloorInstance floor)
        {
            foreach (var candle in floor.candles)
            {
                candle.objective = false; candle.Setup(true); Interact.Remove(candle);
            }
            floor.candles.Clear();
        }
    }
}
