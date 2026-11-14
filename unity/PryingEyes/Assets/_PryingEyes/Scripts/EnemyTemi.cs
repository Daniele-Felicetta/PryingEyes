using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>I nemici dei piani a tema. Sono il monaco e l'abate con un altro costume: stesso scheletro, stesse clip, stessa
    /// intelligenza (Enemy.Stealth); cambiano il modello (Editor/NemiciTemiImport.cs), i numeri e un colpo di scena ciascuno.
    /// Teatro: la Maschera (l'usciere con la lanterna: fragile e svelto, al primo sguardo soffia il fischietto e richiama
    /// chi e' nei paraggi) e il Direttore (dirige un ventaglio di dardi). Sanatorio: l'Infermiere (la puntura ti
    /// addormenta le gambe: sedativo, 55% di velocita' per qualche secondo) e il Primario (grosso come l'abate, chiama gli
    /// infermieri). Il FloorDirector li sceglie al posto di monaco e abate (FloorDirector.Retheme).</summary>
    public partial class Enemy
    {
        // numeri di combattimento: i valori di partenza sono quelli del monaco, SetThemedStats li cambia
        float meleeDmg = 16f, meleeCd = 1.9f, meleeReach = 2.1f;
        float boltDmg = 10f, castCd = 2.6f, castWind = 0.9f;
        int volley = 0;                                   // i nemici ordinari pressano in mischia; i dardi restano una specialità del Direttore
        float whistleT = -99f, helpT = -99f;
        /// <summary>Contatori per la prova automatica (StealthTest -temitest): fischietti soffiati, richiami, dardi lanciati.</summary>
        public static int statWhistles, statHelps, statBolts;

        public static bool IsAlien(EnemyType t) => t == EnemyType.Veglia || t == EnemyType.Medusa || t == EnemyType.Portachiavi;
        public static bool IsThemed(EnemyType t) => t >= EnemyType.Maschera;

        void SetThemedStats()
        {
            switch (type)
            {
                case EnemyType.Maschera:
                    // svelto e fragile: la lanterna lo tradisce, il fischietto e' il suo vero colpo
                    (maxHp, speed, basePitch) = (150f, Random.Range(1.55f, 1.90f), Random.Range(0.96f, 1.06f));
                    (meleeDmg, meleeCd, meleeReach, volley) = (12f, 1.6f, 2.3f, 0);
                    break;
                case EnemyType.Direttore:
                    // lento e alto: telegrafa a lungo la carica e poi lancia un ventaglio di tre dardi (cinque da ferito)
                    (maxHp, speed, basePitch) = (330f, Random.Range(0.95f, 1.15f), Random.Range(0.74f, 0.82f));
                    (meleeDmg, meleeCd, meleeReach) = (18f, 2.3f, 2.5f);
                    (volley, boltDmg, castCd, castWind) = (3, 7f, 3.4f, 1.2f);
                    break;
                case EnemyType.Infermiere:
                    (maxHp, speed, basePitch) = (190f, Random.Range(1.35f, 1.70f), Random.Range(1.0f, 1.12f));
                    (meleeDmg, meleeCd, meleeReach, volley) = (10f, 1.5f, 2.2f, 0);
                    break;
                case EnemyType.Spazzino:
                    (maxHp, speed, basePitch) = (140f, Random.Range(1.65f, 1.90f), 1f);
                    (meleeDmg, meleeCd, meleeReach, volley) = (12f, 1.55f, 2.15f, 0);
                    break;
                case EnemyType.Vigile:
                    (maxHp, speed, basePitch) = (340f, Random.Range(1.05f, 1.20f), .72f);
                    (meleeDmg, meleeCd, meleeReach, volley) = (22f, 2.2f, 2.3f, 0);
                    break;
                default:                                   // Primario: l'abate del sanatorio, colpi e portata dell'abate
                    (maxHp, speed, basePitch) = (360f, Random.Range(1.0f, 1.2f), Random.Range(0.52f, 0.58f));
                    break;
            }
            hp = maxHp;
        }

        /// <summary>Dove carica la magia nel riferimento del prefab (non scalato): in cima al bastone. Il Direttore ha il
        /// diapason, piu' alto e piu' avanti: la sfera sta fra i due rebbi.</summary>
        Vector3 CastOrbLocal() => type == EnemyType.Direttore ? new Vector3(-0.263f, 1.617f, 0.3305f) : new Vector3(-0.30f, 1.35f, 0.40f);

        // ---------- modello ----------
        /// <summary>Prefab fatto da Editor/NemiciTemiImport.cs (Resources/Maschera, Direttore, Infermiere, Primario). I materiali
        /// del lampo del colpo sono copie per istanza; occhio e occhietto e le luci (lanterna, scialitica) restano fuori, cosi'
        /// il bagliore non si spegne. Se il prefab non c'e' ancora si presenta il monaco o l'abate.</summary>
        static GameObject BuildThemed(EnemyType t, out Material glow, out List<Material> flashes)
        {
            if (t == EnemyType.Spazzino || t == EnemyType.Vigile)
                return BuildCity(t, out glow, out flashes);
            var prefab = Resources.Load<GameObject>(t.ToString());
            if (prefab == null)
            {
                Debug.LogWarning($"[PryingEyes] manca Resources/{t}: al suo posto {BaseType(t)}");
                return BaseType(t) == EnemyType.Abate ? BuildAbate(out glow, out flashes) : BuildMonaco(out glow, out flashes);
            }
            var g = Object.Instantiate(prefab);
            g.name = t.ToString();
            flashes = new List<Material>();
            foreach (var r in g.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (!m.name.Contains("_Occhi") && !m.name.Contains("_Luce")) flashes.Add(m);
            // onda d'urto del colpo a terra: del colore del costume
            (Color dark, Color light) c = t switch
            {
                EnemyType.Maschera => (new Color(0.30f, 0.06f, 0.02f), new Color(1f, 0.46f, 0.12f)),
                EnemyType.Direttore => (new Color(0.20f, 0.22f, 0.30f), new Color(0.72f, 0.82f, 1f)),
                EnemyType.Infermiere => (new Color(0.10f, 0.22f, 0.08f), new Color(0.55f, 1f, 0.35f)),
                _ => (new Color(0.18f, 0.24f, 0.28f), new Color(0.80f, 1f, 0.95f)),
            };
            glow = Lit(c.dark, 0f, 0.4f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", c.light * 3f);
            return g;
        }

        // ---------- suono: ognuno ha la sua musichetta ----------
        void AttachThemedLoop()
        {
            switch (type)
            {
                case EnemyType.Maschera: loop = Sfx.Loop(gameObject, SoundBank.MusicBox, 0.40f, 1.5f, 24f, basePitch); break;
                case EnemyType.Direttore: loop = Sfx.Loop(gameObject, SoundBank.TheatreTone, 0.55f, 2f, 30f, basePitch); break;
                case EnemyType.Infermiere: loop = Sfx.Loop(gameObject, SoundBank.Monitor, 0.40f, 1.5f, 20f, basePitch); break;
                case EnemyType.Spazzino: loop = Sfx.Loop(gameObject, SoundBank.KeeperKeys, .20f, 1.5f, 20f, basePitch); break;
                case EnemyType.Vigile: loop = Sfx.Loop(gameObject, SoundBank.TheatreTone, .25f, 2f, 26f, basePitch); break;
                default: loop = Sfx.Loop(gameObject, SoundBank.ClinicBuzz, 0.55f, 2f, 28f, basePitch); break;
            }
        }

        // ---------- colpi di scena ----------
        /// <summary>Maschera: al primo sguardo soffia il fischietto (una volta ogni 14 s). Chi non sta gia' cacciando corre
        /// sul posto (NoiseKind.Alarm); il rumore e' forte e arriva lontano.</summary>
        void Whistle()
        {
            if (Time.time < whistleT) return;
            whistleT = Time.time + 14f;
            statWhistles++;
            Vector3 mouth = transform.position + Vector3.up * 1.6f;
            Sfx.At(SoundBank.Whistle, mouth, 1f, Random.Range(0.96f, 1.06f), 4f, 60f);
            Coop.Game?.EnemyEvent(this, EnemyEvt.Whistle, mouth);
            Noise.Emit(transform.position, 46f, NoiseKind.Alarm);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] maschera: fischietto, {all.Count - 1} altri nemici sul piano");
        }

        /// <summary>Primario: chiama due infermieri (una volta ogni 45 s). Arrivano fuori vista e vanno verso di lui.</summary>
        void CallHelp()
        {
            if (Time.time < helpT) return;
            helpT = Time.time + 45f;
            statHelps++;
            var fd = Proc.FloorDirector.I;
            if (fd == null) return;
            Sfx.At(SoundBank.AlertSting, transform.position + Vector3.up * 2f, 0.9f, 0.62f, 4f, 55f);
            Coop.Game?.EnemyEvent(this, EnemyEvt.Help, transform.position);
            fd.CallHelp(transform.position, 2);
            if (GameManager.autoTest) Debug.Log("[PryingEyes] primario: chiama rinforzi");
        }

        /// <summary>Magia a distanza: un dardo solo (monaco) o un ventaglio centrato sul giocatore (direttore, 3 dardi, 5 da ferito).</summary>
        void LaunchBolts(Vector3 tip, Vector3 aim, bool enraged)
        {
            int n = volley <= 1 ? 1 : volley + (enraged ? 2 : 0);
            statBolts += n;
            Vector3 to = aim - tip;
            for (int i = 0; i < n; i++)
            {
                float yaw = n == 1 ? 0f : (i - (n - 1) * 0.5f) * 13f;
                MagicBolt.Launch(tip, tip + Quaternion.Euler(0f, yaw, 0f) * to, boltDmg + Random.value * 5f);
            }
        }
    }
}
