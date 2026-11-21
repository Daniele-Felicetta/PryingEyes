using System;
using System.Globalization;
using System.Text;

namespace PryingEyes.Pista
{
    /// <summary>The prize of a licence test, from the time of the best clean flying lap.</summary>
    public enum Medal { None = 0, Bronze = 1, Silver = 2, Gold = 3 }

    /// <summary>One licence test: a given bike on a named circuit, three laps alone (a standing-start lap and two flying ones) and three times to beat. The lap that counts is the fastest flying lap
    /// with no fall in it. The targets are tied to the racing line the computer riders follow (about 1.08, 1.15 and 1.27 times the planned lap, to the half second), and the lab checks that a computer
    /// rider at full pace earns gold, one a little slower silver and a beginner bronze, so a medal is always within reach.</summary>
    public sealed class LicenceTest
    {
        /// <summary>The key of the test in the file of the player ("a1-1"): it never changes, whatever the order of the list.</summary>
        public readonly string id;
        /// <summary>0 A1, 1 A2, 2 A.</summary>
        public readonly int licence;
        public readonly string name, brief;
        /// <summary>The circuit (an index into Catalogue.All), the bike (an index into Bikes), the aid level and the hour of the day (the choices of PistaMode) the test is ridden with.</summary>
        public readonly int circuit, bike, aids, hour;
        /// <summary>The times (seconds) of the best clean flying lap for each medal.</summary>
        public readonly float gold, silver, bronze;

        public LicenceTest(string id, int licence, string name, string brief, int circuit, int bike, int aids, int hour, float gold, float silver, float bronze)
        {
            this.id = id; this.licence = licence; this.name = name; this.brief = brief;
            this.circuit = circuit; this.bike = bike; this.aids = aids; this.hour = hour;
            this.gold = gold; this.silver = silver; this.bronze = bronze;
        }

        /// <summary>The seed of the circuit of the test.</summary>
        public int Seed { get { return Catalogue.All[circuit].seed; } }

        /// <summary>The medal a lap time (seconds) earns: at most the target counts. A time that is not a time (zero, negative, not a number) earns nothing.</summary>
        public Medal Judge(float lapTime)
        {
            if (!(lapTime > 0f) || float.IsInfinity(lapTime)) return Medal.None;
            if (lapTime <= gold) return Medal.Gold;
            if (lapTime <= silver) return Medal.Silver;
            if (lapTime <= bronze) return Medal.Bronze;
            return Medal.None;
        }

        /// <summary>The time a medal asks for (0 for no medal).</summary>
        public float Target(Medal medal)
        {
            return medal == Medal.Gold ? gold : medal == Medal.Silver ? silver : medal == Medal.Bronze ? bronze : 0f;
        }
    }

    /// <summary>The licence tests of the track mode, in three licences of three tests, like the categories of a real motorcycle licence: A1 the small bikes (a 125, all the aids), A2 the middle ones
    /// (600 to 750, sporty aids) and A the big ones (1000 to 1300). Tests are not locked: any of them can be taken at any time, and a licence is obtained when its three tests have a medal.</summary>
    public static class Licences
    {
        /// <summary>Laps of a test: a standing-start lap and two flying laps, so one mistake can be made up for.</summary>
        public const int Laps = 3;
        /// <summary>A test is ridden alone: a lap against the clock (and the ghost of the best lap).</summary>
        public const int Rivals = 0;
        public const int PerLicence = 3;

        public static readonly string[] Names = { "Patente A1", "Patente A2", "Patente A" };
        public static readonly string[] Taglines =
        {
            "Le piccole: una 125 su tre circuiti, per imparare a farla girare. Aiuti completi.",
            "Le medie, dal 600 al 750. Aiuti sportivi: niente aiuto in piega.",
            "Le grandi, da mille a milletrecento. Non perdonano.",
        };

        public static readonly LicenceTest[] All =
        {
            // A1: Scintilla 125, all the aids, at noon
            new LicenceTest("a1-1", 0, "Il primo giro", "Scintilla 125 sul circuito piu' scorrevole: trova la linea e non frenare troppo tardi.", 0, 3, 0, 1, 85.5f, 91.0f, 100.5f),
            new LicenceTest("a1-2", 0, "Tornanti", "Una 125 in mezzo ai tornanti: conta la velocita' in curva, non quella sul dritto.", 5, 3, 0, 1, 108.0f, 115.0f, 127.5f),
            new LicenceTest("a1-3", 0, "Saliscendi", "In cresta la moto si alleggerisce, in discesa carica l'anteriore: frena prima, accelera in uscita.", 3, 3, 0, 1, 95.5f, 101.5f, 112.5f),
            // A2: 600 to 750, sporty aids, in the morning
            new LicenceTest("a2-1", 1, "Il rettilineo", "Argo 600: il lungo rettilineo premia l'uscita dall'ultima curva.", 1, 1, 1, 0, 90.0f, 95.5f, 105.5f),
            new LicenceTest("a2-2", 1, "In salita", "Tigre 675 sui tornanti in salita: in uscita la ruota dietro scivola, apri il gas piano.", 7, 4, 1, 0, 105.5f, 112.5f, 124.5f),
            new LicenceTest("a2-3", 1, "Corta e veloce", "Vigile 750 su pochi metri collinari: un errore costa l'intero giro.", 4, 2, 1, 0, 74.5f, 79.0f, 87.5f),
            // A: 1000 to 1300, sporty aids, in the afternoon
            new LicenceTest("a-1", 2, "Oltre 850 metri", "Argo 1000: il rettilineo piu' lungo che c'e', e la staccata in fondo fa il tempo.", 2, 0, 1, 2, 97.5f, 104.0f, 114.5f),
            new LicenceTest("a-2", 2, "Quindici tornanti", "Duca 1200 sul circuito piu' tecnico: coppia da vendere, gomma da risparmiare.", 6, 5, 1, 2, 114.5f, 121.5f, 134.5f),
            new LicenceTest("a-3", 2, "La pista di punta", "Gigante 1300 sul circuito piu' lungo: oltre quattro chilometri senza respiro.", 11, 6, 1, 2, 129.5f, 138.0f, 152.5f),
        };

        public static int Count { get { return All.Length; } }

        /// <summary>The index of the test with this key, or -1.</summary>
        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return -1;
        }

        public static string MedalName(Medal medal)
        {
            return medal == Medal.Gold ? "ORO" : medal == Medal.Silver ? "ARGENTO" : medal == Medal.Bronze ? "BRONZO" : "";
        }
    }

    /// <summary>A licence test being ridden: it feeds on the laps of the rider and says what each earned. Only a flying lap with no fall in it counts; the medal of the attempt is the one of its best lap.</summary>
    public sealed class LicenceRun
    {
        public readonly LicenceTest test;
        /// <summary>The best lap that counted (0 until there is one) and its medal.</summary>
        public float bestLap;
        public Medal medal;
        /// <summary>How many laps counted.</summary>
        public int counted;

        public LicenceRun(LicenceTest test) { this.test = test; }

        /// <summary>A lap of the rider is over: 'kind' 0 is the standing-start lap, 1 a flying one; 'clean' means no fall during it. The medal this lap earns (None when it does not count or is too slow);
        /// 'counts' says whether it counted at all.</summary>
        public Medal Lap(int kind, bool clean, float lapTime, out bool counts)
        {
            counts = kind == 1 && clean && lapTime > 0f && !float.IsNaN(lapTime) && !float.IsInfinity(lapTime);
            if (!counts) return Medal.None;
            counted++;
            Medal earned = test.Judge(lapTime);
            if (bestLap <= 0f || lapTime < bestLap) { bestLap = lapTime; medal = earned; }
            return earned;
        }
    }

    /// <summary>What a player has done in the licence tests: the best clean flying lap of every test (the medal is worked out from it, so it follows the targets), to and from a short text the
    /// preferences keep ("a1-1=83.412;a2-3=70.100"). Reading is forgiving: anything that is not a known test with a believable time is left out, and nothing throws.</summary>
    public sealed class LicenceBook
    {
        readonly float[] best = new float[Licences.Count];

        /// <summary>The best lap of a test (0: none yet).</summary>
        public float BestOf(int test) { return test >= 0 && test < best.Length ? best[test] : 0f; }

        public Medal MedalOf(int test) { return test >= 0 && test < best.Length && best[test] > 0f ? Licences.All[test].Judge(best[test]) : Medal.None; }

        /// <summary>Keep a lap if it is the best of its test; true when it is.</summary>
        public bool Submit(int test, float lapTime)
        {
            if (test < 0 || test >= best.Length || !(lapTime > 0f) || float.IsInfinity(lapTime)) return false;
            if (best[test] > 0f && lapTime >= best[test]) return false;
            best[test] = lapTime;
            return true;
        }

        /// <summary>How many tests have this medal or better (a test with no medal is never counted).</summary>
        public int Count(Medal atLeast)
        {
            int n = 0;
            for (int i = 0; i < best.Length; i++)
            {
                Medal m = MedalOf(i);
                if (m != Medal.None && m >= atLeast) n++;
            }
            return n;
        }

        /// <summary>A licence is obtained when all its tests have a medal, any medal.</summary>
        public bool Obtained(int licence)
        {
            int tests = 0;
            for (int i = 0; i < Licences.All.Length; i++)
            {
                if (Licences.All[i].licence != licence) continue;
                tests++;
                if (MedalOf(i) == Medal.None) return false;
            }
            return tests > 0;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < best.Length; i++)
            {
                if (!(best[i] > 0f)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(Licences.All[i].id).Append('=').Append(best[i].ToString("0.000", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static LicenceBook Parse(string text)
        {
            var book = new LicenceBook();
            if (string.IsNullOrEmpty(text)) return book;
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                int test = Licences.IndexOf(part.Substring(0, eq).Trim());
                float v;
                if (test < 0 || !float.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                if (float.IsNaN(v) || v <= 0f || v >= 3600f) continue;
                book.Submit(test, v);
            }
            return book;
        }
    }
}
