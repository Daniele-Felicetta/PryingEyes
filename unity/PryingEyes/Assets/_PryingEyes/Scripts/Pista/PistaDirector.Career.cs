namespace PryingEyes.Pista
{
    /// <summary>The career (Career.cs) in the race. When the table of results comes up with the career on (alone, never in a cooperative game), what the race gave goes into the player's book once: the purse of the place in an ordinary
    /// race or a round of a cup (plus the trophy when the round closed the season), the medals and the licence of a test. The table says it at the end of the line under its title ("premio 660 cr"). With the career off nothing is
    /// paid and the table is as it always was.</summary>
    public sealed partial class PistaDirector
    {
        bool careerPaid;

        /// <summary>For the tests and the table: what the last table of results paid, and the words it said (empty when it paid nothing).</summary>
        public int CareerPrize { get; private set; }
        public string CareerNote { get; private set; } = "";

        /// <summary>A race is set up: nothing has been paid yet.</summary>
        void CareerBegin()
        {
            careerPaid = false; CareerPrize = 0; CareerNote = "";
        }

        /// <summary>The table is up (or up again after the replay): the first time it pays, always it says so.</summary>
        void CareerResults()
        {
            if (!PistaMode.CareerOn || Coop.Active) { hud.SetResultsNote(""); return; }
            if (!careerPaid)
            {
                careerPaid = true;
                int pay;
                if (licence != null)
                {
                    int test = PistaMode.Licence;
                    var book = PistaLicences.Book;
                    Medal before = licenceBefore > 0f ? licence.test.Judge(licenceBefore) : Medal.None;
                    pay = Economy.LicencePay(before, book.MedalOf(test));
                    if (!licenceObtainedBefore && book.Obtained(licence.test.licence)) pay += Economy.LicenceBonus;
                }
                else
                {
                    int rank = CupActive ? cupSeason.rank : PistaMode.Rank;
                    pay = Economy.Winnings(me.position, me.finished, race.laps, race.racers.Count - 1, me.bikeIndex, rank);
                    if (CupActive && CupSeasonOver) pay += Economy.CupBounty(cupSeason.Trophy());
                }
                CareerPrize = PistaCareer.Pay(pay);
                CareerNote = CareerPrize > 0 ? "premio " + Economy.Format(CareerPrize) + " cr" : "";
            }
            hud.SetResultsNote(CareerNote);
        }
    }
}
