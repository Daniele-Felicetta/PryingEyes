using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>La modalita' "il deserto": un deserto enorme e quasi infinito, procedurale, con il ciclo del giorno e della notte. Si parte dalla
    /// cattedrale di sempre e si scende dalle scale del sagrato sulla sabbia; edifici interi, spezzati, in rovina, torri altissime e catacombe
    /// (con il boss sull'ultimo piano) stanno sparsi sulla sabbia, fatti con le stanze dei piani. Libri dei perk, consacrazione (pack-a-punch) e
    /// armi buone sono nella cattedrale; in giro si trovano solo armi comuni, punti, munizioni, medikit ed essenza.
    /// Questa classe tiene lo stato della modalita'; il lavoro lo fanno i componenti di Scripts/Deserto (DesertDirector in testa). Il mondo e'
    /// una funzione pura del seme (DesertWorld, provata da tools/desertlab): non si salva niente, si ricostruisce uguale.</summary>
    public static class DesertMode
    {
        const string ModePref = "PryingEyes.GameMode";

        /// <summary>La partita in corso e' nel deserto.</summary>
        public static bool Active { get; private set; }
        internal static bool OnSurface => Active && Proc.FloorDirector.I != null && Proc.FloorDirector.I.InHub;
        /// <summary>Il seme del mondo della partita in corso.</summary>
        public static int Seed { get; private set; }
        /// <summary>L'orologio del giorno: gira solo nel deserto.</summary>
        public static readonly DesertClock Clock = new DesertClock();

        /// <summary>Cio' che nel deserto e' gia' stato preso o ucciso in questa partita (i luoghi si scaricano e si ricaricano: il bottino non rinasce).
        /// La chiave la fa <see cref="Pack"/>.</summary>
        public static readonly System.Collections.Generic.HashSet<long> Taken = new System.Collections.Generic.HashSet<long>();
        public static readonly System.Collections.Generic.HashSet<long> BossesDown = new System.Collections.Generic.HashSet<long>();

        /// <summary>Una chiave unica per una cosa di un luogo: il luogo (cella), il livello e il numero dell'oggetto.</summary>
        public static long Pack(Site s, int level, int index) => ((long)(s.cx + 4096) << 40) ^ ((long)(s.cz + 4096) << 24) ^ ((long)(level & 0xff) << 16) ^ (long)(index & 0xffff);

        static bool? requested;
        /// <summary>Scelto nel menu per la prossima partita nuova (si ricorda tra una sessione e l'altra). Da riga di comando: -deserto.</summary>
        public static bool Requested
        {
            get
            {
                if (requested == null)
                {
                    var args = System.Environment.GetCommandLineArgs();
                    bool arg = System.Array.IndexOf(args, "-deserto") >= 0 || System.Array.IndexOf(args, "-desertotest") >= 0;
                    requested = arg || (!GameManager.autoTest && SafePref() == 1);
                }
                return requested.Value;
            }
            set
            {
                requested = value;
                if (GameManager.autoTest) return;       // le prove non toccano le preferenze del giocatore
                try { PlayerPrefs.SetInt(ModePref, value ? 1 : 0); PlayerPrefs.Save(); } catch { /* le preferenze non servono per giocare */ }
            }
        }

        static int SafePref() { try { return PlayerPrefs.GetInt(ModePref, 0); } catch { return 0; } }

        /// <summary>Comincia una partita nel deserto: il mondo del seme, la mattina presto.</summary>
        public static void Begin(int seed)
        {
            Active = true;
            Seed = seed;
            DesertWorld.Reset(seed);
            Clock.hour = DesertClock.StartHour;
            Clock.day = 0;
            Clock.speed = 1f;
            Taken.Clear();
            BossesDown.Clear();
            Cattedrale.Opere.IncludeDesert = true;      // le opere che sbloccano libri e consacrazione (Cattedrale/Catalogo.cs)
            Cattedrale.Fabbrica.AggiornaOpere();
        }

        /// <summary>Finita la partita (menu, morte definitiva): si torna ai piani. Il mondo si libera con i suoi componenti.</summary>
        public static void End()
        {
            Active = false;
            Cattedrale.Opere.IncludeDesert = false;
            Cattedrale.Fabbrica.AggiornaOpere();
        }

        /// <summary>Il seme di una partita nuova: da -seed nelle prove, a caso altrimenti.</summary>
        public static int NewSeed()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-seed" && int.TryParse(a[i + 1], out int v) && v != 0) return v;
            return Random.Range(1, 1 << 28);
        }

        /// <summary>Quanto e' pericoloso un punto del deserto (1 = vicino alla cattedrale, fino a 4 lontanissimo): scala vita e velocita' dei nemici
        /// e il bottino. Cresce con la distanza, 1,5 km per punto.</summary>
        public static float Danger(float x, float z)
        {
            float d = Mathf.Sqrt(x * x + z * z);
            return 1f + Mathf.Min(3f, d / 1500f);
        }

        static readonly System.Collections.Generic.List<Site> dropTmp = new System.Collections.Generic.List<Site>();

        /// <summary>Il ramo (0..3) delle risorse che lascia un nemico ucciso in questo punto: se un luogo e' a meno di 90 m il suo ramo, altrimenti
        /// quello che dice la distanza dalla cattedrale (piu' lontano, piu' rare le risorse).</summary>
        public static int DropBranch(Vector3 p)
        {
            DesertWorld.SitesNearKnown(p.x, p.z, 90f, dropTmp);
            Site best = null; float bd = float.MaxValue;
            foreach (var s in dropTmp)
            {
                float dx = Mathf.Max(Mathf.Max(s.fx0 - p.x, p.x - s.fx1), 0f), dz = Mathf.Max(Mathf.Max(s.fz0 - p.z, p.z - s.fz1), 0f);
                float d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = s; }
            }
            if (best != null) return BranchOf(best);
            return Mathf.Clamp((int)((Danger(p.x, p.z) - 1f) / 0.75f), 0, 3);
        }

        /// <summary>Il ramo delle catacombe (0..3) da cui un luogo prende il boss e il suo tesoro: dipende dal luogo, non dal caso.</summary>
        public static int BranchOf(Site s) => (int)((uint)DesertNoise.Hash(s.cx, s.cz, Seed + 91) % 4u);
    }
}
