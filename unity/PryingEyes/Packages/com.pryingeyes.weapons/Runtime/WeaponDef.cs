namespace Production.Weapons
{
    public enum FireMode { Auto, Semi, Burst, Pump, Bolt, Double, Lever }
    public enum ReloadStyle { Mag, Pistol, Tube, Revolver, Break, Belt }

    /// <summary>The record of one weapon: balance, handling, sight and the hints for how it looks and sounds. Angles are in degrees and times in seconds.
    /// It is plain data, one public field for each number of the catalogue, and the fields are the catalogue's own columns: their names and their order are what a
    /// catalogue file says and what its content hash is taken over, so neither changes without a new version of the catalogue. The package reads none of the hints
    /// of the last block (sight, sounds, casings, tracers, colours, hands): they travel with the record for whoever draws and plays the weapon, who finds its model by <see cref="id"/>.</summary>
    public class WeaponDef
    {
        public string id, name, cls;
        public int grade = 1;                 // 1 ordinary .. 5 relic
        public int price;
        public bool cityOnly;
        public FireMode[] modes = { FireMode.Auto };

        // damage
        public float damage = 25f;            // per bullet (or pellet) at point blank
        public float headMult = 2f;
        public float rangeNear = 15f, rangeFar = 60f, farMult = 0.6f;   // full damage up to rangeNear, then falling to farMult * damage at rangeFar
        public int pellets = 1;
        public float cone = 0f;               // half-angle of the cone of the pellets (degrees)
        public int pierce = 0;                // enemies passed through in addition to the first
        public float pierceMult = 0.65f;      // the damage is multiplied by this for each enemy passed through

        // rate of fire
        public float rpm = 600f;
        public int burst = 3;                 // shots per burst (FireMode.Burst)
        public float burstGap = 0.12f;        // pause after the burst (s)
        public float cycle = 0f;              // manual action cycle (bolt, pump, lever): seconds from a shot to the next

        // ammunition
        public int mag = 30, reserve = 120, reserveMax = 240;
        public float reload = 1.8f, reloadEmpty = 2.2f;
        public ReloadStyle style = ReloadStyle.Mag;
        public float shellTime = 0.5f;        // tube reload: seconds for each round

        // accuracy (half-angle in degrees)
        public float hip = 2f, ads = 0.6f;
        public float moveSpread = 1.0f;       // largest multiplier when running (1 + moveSpread)
        public float bloomShot = 0.35f;       // degrees added by each shot
        public float bloomMax = 3.0f;
        public float bloomRecover = 6f;       // degrees per second

        // recoil
        public float recV = 1f, recH = 0.3f;  // recoil of the view at each shot (degrees: vertical, and horizontal at random)
        public float recRecover = 8f;         // the view coming back (degrees per second)
        public float kick = 1f;               // scale of the kick of the model

        // handling
        public float adsTime = 0.16f;
        public float drawTime = 0.35f;
        public float moveMult = 1f;           // walking speed
        public float adsMoveMult = 0.65f;

        // sight
        public string sight = "iron";         // iron | peep | reflex | holo | ring | acog | scope
        public float zoom = 1f;               // magnification of the optic (a second camera when above 1.4)
        public float adsFov = 62f;
        public float adsDist = 0.34f;         // distance of the aim point from the camera when aiming
        public int pattern = 0;               // reticle: 0 dot, 1 ring and dot, 2 ring, 3 chevron, 4 cross
        public float dotSize = 1f;

        // look and sound
        public string snd = "rifle";          // set of firing sounds
        public float flash = 1f;
        public bool suppressed;
        public string casing = "rifle";       // pistol | rifle | big | shell | none
        public float tracerEvery = 3f;
        public Rgba tracerColor = new Rgba(1f, 0.82f, 0.5f);
        public float tracerWidth = 0.010f;
        public int tracerPellets = 2;         // how many pellets of a shot show a tracer
        public bool beam;                     // an instant beam instead of a travelling tracer
        public string fx = "";                // "flame" = a jet of flames
        public int soundEvery = 1;            // the sound plays every n shots (flames: one continuous roar)
        public Rgba flashColor = new Rgba(1f, 0.62f, 0.28f);
        public Rgba? reticle;                 // colour of the collimated reticle (red when absent)
        public float hand = 1f;               // 1 = two hands, 0 = the left hand does not hold the weapon
        public float handDrop;                // lowers the right hand (m): pistols have the barrel lower than the rifles the hands were made for
        public bool magUp;                    // the magazine comes out upward (a compact gun with the magazine above the barrel)

        public string GradeName => GradeNames[WeaponMath.Clamp(grade, 1, 5) - 1];
        public Rgba GradeColor => GradeColors[WeaponMath.Clamp(grade, 1, 5) - 1];
        public string ModeLabel(FireMode m)
        {
            switch (m)
            {
                case FireMode.Auto: return "AUTO";
                case FireMode.Semi: return "SEMI";
                case FireMode.Burst: return "RAFFICA";
                case FireMode.Pump: return "POMPA";
                case FireMode.Bolt: return "OTTURATORE";
                case FireMode.Double: return "DOPPIETTA";
                default: return "LEVA";
            }
        }

        public static readonly string[] GradeNames = { "Ordinaria", "Scelta", "Rara", "Epica", "Reliquia" };
        public static readonly Rgba[] GradeColors =
        {
            new Rgba(0.72f, 0.72f, 0.70f), new Rgba(0.45f, 0.85f, 0.45f), new Rgba(0.38f, 0.70f, 1.00f),
            new Rgba(0.72f, 0.50f, 1.00f), new Rgba(1.00f, 0.78f, 0.28f),
        };
    }
}
