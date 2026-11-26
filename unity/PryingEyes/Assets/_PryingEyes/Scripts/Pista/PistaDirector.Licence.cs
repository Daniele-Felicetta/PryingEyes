using UnityEngine;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>A licence test (Licences.cs) in the race. The laps of the player are scored by a LicenceRun (only a flying lap with no fall in it counts); a lap that earns a medal says so on the screen as soon as it is
    /// over and goes into the book of the player if it is the best of the test; the table of results gives the verdict: the medal, the times to beat, the best time of the book and, when the third test of a
    /// licence has just got its medal, the licence. An ordinary race, and any cooperative game, has none of this.</summary>
    public sealed partial class PistaDirector
    {
        LicenceRun licence;
        float licenceBefore;
        bool licenceObtainedBefore, lapCounted;
        int lapKindDone;

        /// <summary>For the interface and the tests: the test being ridden (null in an ordinary race) and the medal the last lap earned.</summary>
        public LicenceRun Licence => licence;
        public Medal LastLapMedal { get; private set; }
        /// <summary>The lines of the verdict shown with the results (for the tests): the medal first.</summary>
        public string LicenceVerdict { get; private set; } = "";

        /// <summary>A race is set up: if the menu asked for a licence test (alone only) a fresh attempt starts, with the best time and the licences of the book as they are now.</summary>
        void LicenceBegin()
        {
            licence = null; LastLapMedal = Medal.None; LicenceVerdict = ""; lapCounted = false; lapKindDone = 0;
            if (hud != null) hud.SetLicenceGoals(null);
            int i = PistaMode.Licence;
            if (i < 0 || i >= Licences.Count || Coop.Active) return;
            licence = new LicenceRun(Licences.All[i]);
            if (hud != null) hud.SetLicenceGoals(LicenceGoalsLine(licence.test));
            var book = PistaLicences.Book;
            licenceBefore = book.BestOf(i);
            licenceObtainedBefore = book.Obtained(licence.test.licence);
        }

        /// <summary>The player has just finished a lap (TimingLapDone): the licence scores it and, if it counted, the book keeps it when it is the best of the test.</summary>
        void LicenceLap(Racer r, int kind, bool clean)
        {
            LastLapMedal = Medal.None; lapCounted = false; lapKindDone = kind;
            if (licence == null) return;
            LastLapMedal = licence.Lap(kind, clean, r.lastLap, out lapCounted);
            if (lapCounted) PistaLicences.Submit(PistaMode.Licence, r.lastLap);
        }

        /// <summary>The three times to beat on the screen during the test, each in the colour of its medal (the HUD reads rich text here).</summary>
        internal static string LicenceGoalsLine(LicenceTest t)
        {
            return "<color=#FFCC33>ORO " + TargetClock(t.gold) + "</color>  <color=#D6E0EB>ARG " + TargetClock(t.silver) + "</color>  <color=#D18547>BRO " + TargetClock(t.bronze) + "</color>";
        }

        /// <summary>A target as the corner of the screen writes it: the targets are whole half seconds, so one decimal says it all (1:14.5).</summary>
        internal static string TargetClock(float seconds)
        {
            int tenths = Mathf.RoundToInt(seconds * 10f);
            return (tenths / 600) + ":" + ((tenths / 10) % 60).ToString("00") + "." + (tenths % 10);
        }

        internal static Color? LicenceColor(Medal medal)
        {
            return medal == Medal.Gold ? new Color(1f, 0.80f, 0.20f) : medal == Medal.Silver ? new Color(0.84f, 0.88f, 0.92f) : medal == Medal.Bronze ? new Color(0.82f, 0.52f, 0.26f) : (Color?)null;
        }

        /// <summary>What the banner of a lap says after the time in a licence test: the medal, or why there is none.</summary>
        string LicenceLapText(bool record)
        {
            string s;
            if (lapCounted) s = LastLapMedal != Medal.None ? "   -   " + Licences.MedalName(LastLapMedal) : "   -   troppo lento: per il bronzo serve " + PistaHud.Clock(licence.test.bronze);
            else s = lapKindDone == 0 ? "   -   giro di lancio: non conta" : "   -   con una caduta: non conta";
            return s + (record ? "   -   NUOVO RECORD" : "");
        }

        /// <summary>The race is over: the verdict of the test under the table of results (nothing in an ordinary race).</summary>
        void LicenceResults()
        {
            if (licence == null) { hud.SetLicence(null, Color.white); return; }
            hud.SetLicenceGoals(null);            // the table says the three times itself
            var t = licence.test;
            int index = PistaMode.Licence;
            var book = PistaLicences.Book;
            Medal medal = licence.medal;
            string title = licence.counted == 0 ? "NESSUN GIRO VALIDO" : medal == Medal.None ? "NESSUNA MEDAGLIA" : Licences.MedalName(medal);
            Color color = LicenceColor(medal) ?? PistaHud.Dim;

            string targets = "Oro " + PistaHud.Clock(t.gold) + "     Argento " + PistaHud.Clock(t.silver) + "     Bronzo " + PistaHud.Clock(t.bronze);
            string mine;
            if (licence.counted == 0) mine = "Serve un giro lanciato senza cadute: il primo giro e' di lancio e una caduta lo annulla";
            else
            {
                Medal next = medal == Medal.None ? Medal.Bronze : medal == Medal.Bronze ? Medal.Silver : medal == Medal.Silver ? Medal.Gold : Medal.None;
                mine = "Il tuo giro: " + PistaHud.Clock(licence.bestLap);
                if (next != Medal.None) mine += "   -   per " + (next == Medal.Bronze ? "il bronzo" : next == Medal.Silver ? "l'argento" : "l'oro") + " servono " + (licence.bestLap - t.Target(next)).ToString("0.00", It) + " secondi in meno";
            }
            string kept = "";
            bool newBest = licence.counted > 0 && (licenceBefore <= 0f || licence.bestLap < licenceBefore - 0.0005f);
            if (newBest) kept = "NUOVO MIGLIORE DELLA PROVA";
            else if (book.BestOf(index) > 0f) kept = "Il tuo migliore: " + (book.MedalOf(index) != Medal.None ? Licences.MedalName(book.MedalOf(index)) + "  " : "") + PistaHud.Clock(book.BestOf(index));
            if (!licenceObtainedBefore && book.Obtained(t.licence)) kept = (kept.Length > 0 ? kept + "   -   " : "") + Licences.Names[t.licence].ToUpperInvariant() + " OTTENUTA";

            hud.SetLicence(title, color, Licences.Names[t.licence] + "  -  " + t.name, targets, mine, kept);
            hud.SetResultsTitle("PROVA FINITA");
            LicenceVerdict = title + " | " + Licences.Names[t.licence] + "  -  " + t.name + " | " + targets + " | " + mine + " | " + kept;
        }
    }
}
