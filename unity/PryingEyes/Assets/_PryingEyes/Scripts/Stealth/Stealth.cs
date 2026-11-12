using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PryingEyes
{
    /// <summary>Stati di allerta di un prete (monaco, abate, occhio): pattuglia, sospetto (ha sentito o intravisto qualcosa),
    /// caccia (ti ha visto) e ricerca (ti ha perso: va nell'ultimo punto noto e si guarda intorno).</summary>
    public enum Alert { Patrol, Suspicious, Hunting, Searching }

    /// <summary>Tipi di rumore. Il raggio lo decide chi lo emette; chi ascolta lo scala in base ai muri e a come sente.</summary>
    public enum NoiseKind { Step, Gunshot, Ping, Candle, Shout, Other, Alarm }

    /// <summary>Componente furtiva dei piani: quanto sei visibile (luce, torcia, movimento, spari), quanto e' vicina la
    /// minaccia (cuore, sfarfallio della torcia, vignetta) e i rumori che i preti possono sentire.
    /// Nella hall classica (niente piani) tutto questo e' spento e i nemici restano onniscienti come prima.</summary>
    public static class Stealth
    {
        public static bool Enabled => GameManager.proc;

        /// <summary>Smoothstep alla GLSL: 0 sotto a, 1 sopra b, morbido in mezzo. (Mathf.SmoothStep fa altro: interpola tra due valori con t in 0..1.)</summary>
        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Strati di luce: 1 = mondo, 2 = arma e mani. URP legge la maschera dai suoi dati aggiuntivi, non da Light.renderingLayerMask.</summary>
        public static void SetLightLayers(Light l, uint mask)
        {
            if (l == null) return;
            var ad = l.GetUniversalAdditionalLightData();
            if (ad != null) ad.renderingLayers = mask;
        }

        // ---- quanto sei visibile -------------------------------------------------------------
        /// <summary>Luce che arriva sul giocatore dalle lampade, dalle candele e dalle finestre (0 buio nero .. 1 pieno), senza la torcia.</summary>
        public static float PlayerLum;
        /// <summary>Solo per le prove: se non e' NaN, vale al posto della luce misurata (per provare il buio nero in una stanza illuminata).</summary>
        public static float lumOverride = float.NaN;
        /// <summary>Distanza (m) a cui ti vede un prete al buio nero, da fermo; in piena luce; e con la torcia accesa.</summary>
        public const float DarkSight = 3.2f, LitSight = 15f, TorchSight = 24f;
        public static float lastShotTime = -99f;
        public static bool lastShotQuiet;
        public static void NoteShot(bool quiet) { lastShotTime = Time.time; lastShotQuiet = quiet; }

        /// <summary>Entro quanti metri un prete ti nota, ora. Dipende dalla luce intorno a te, dalla torcia, da come ti muovi
        /// (corsa, passo, accovacciato, fermo) e dal lampo di un colpo appena sparato; l'occhio vede un po' piu' lontano.</summary>
        public static float SightRange(Enemy e)
        {
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            if (pl == null) return 0f;
            return SightRange(e, new SightProfile
            {
                lum = PlayerLum,
                torch = Torch.I != null && Torch.I.Lit,
                crouch = pl.IsCrouching, sprint = pl.IsSprinting, speedH = pl.SpeedH,
                sightMult = Perks.SightMult, velo = Perks.VeloOn,
                shotAge = Time.time - lastShotTime, shotQuiet = lastShotQuiet,
            });
        }

        /// <summary>Tutto quello che serve per sapere quanto e' visibile un giocatore: lo costruisce il giocatore locale da se' (SightRange(e)) e,
        /// per i compagni in cooperativa, arriva dalla rete (PlayerNet).</summary>
        public struct SightProfile
        {
            public float lum, speedH, sightMult, shotAge;
            public bool torch, crouch, sprint, shotQuiet, velo;
        }

        public static float SightRange(Enemy e, in SightProfile s)
        {
            float r = Mathf.Lerp(DarkSight, LitSight, Mathf.Clamp01(s.lum));
            if (s.torch) r = Mathf.Max(r, TorchSight);
            float stance;
            if (s.crouch) stance = s.speedH > 0.6f ? 0.72f : 0.55f;
            else if (s.sprint) stance = 1.4f;
            else stance = s.speedH > 0.6f ? 1f : 0.82f;
            r *= stance * s.sightMult;                // Velo: ti vedono il 30% in meno
            if (s.shotAge < 0.6f && !s.shotQuiet) r = Mathf.Max(r, 34f);
            if (e != null)
            {
                if (e.Archetype == Enemy.EnemyType.Occhio) r *= 1.2f;
                else if (e.Archetype == Enemy.EnemyType.Abate) r *= 0.92f;
            }
            if (s.velo) r = Mathf.Min(r, 1.5f);       // Velo attivo (5 s): solo a due passi (EvalVision) ti notano ancora
            return r;
        }

        // ---- minaccia ------------------------------------------------------------------------
        /// <summary>0..1, lisciata: quanto e' vicino un prete che ti cerca (o che ti ha visto). Guida cuore, torcia e vignetta.</summary>
        public static float Level;
        public static float HunterDist = 99f;
        public static int Hunters, Aware;
        /// <summary>Quante volte un prete e' passato alla caccia dall'inizio della partita (mai azzerato: chi lo usa confronta due letture).</summary>
        public static int Spotted;
        public static float lastExposure;
        static float lumT;

        public static void Tick(float dt)
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            Vector3 p = gm.player.transform.position;
            lumT -= dt;
            if (lumT <= 0f)
            {
                lumT = 0.2f;
                float sum = Proc.LightPool.Exposure(p + Vector3.up * 1.2f);
                float target = 1f - Mathf.Exp(-sum * 0.75f);
                PlayerLum = float.IsNaN(lumOverride) ? Mathf.Lerp(PlayerLum, target, 0.45f) : lumOverride;
                lastExposure = sum;
            }
            float best = 0f, hd = 99f;
            int hunters = 0, aware = 0;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.IsDead) continue;
                float d = Vector3.Distance(e.transform.position, p);
                float w = e.alert == Alert.Hunting ? 1f : e.alert == Alert.Searching ? 0.55f : e.alert == Alert.Suspicious ? 0.32f : 0.2f;
                float prox = Mathf.Clamp01(1f - d / 22f);
                best = Mathf.Max(best, w * prox);
                if (e.alert == Alert.Hunting) { hunters++; hd = Mathf.Min(hd, d); }
                if (e.alert != Alert.Patrol) aware++;
            }
            if (!gm.player.alive) best = 0f;
            Level = Mathf.MoveTowards(Level, best, dt * (best > Level ? 1.2f : 0.35f));
            HunterDist = hd;
            Hunters = hunters;
            Aware = aware;
        }

        public static void Reset()
        {
            Level = 0f; HunterDist = 99f; Hunters = 0; Aware = 0;
        }
    }

    /// <summary>Rumori che i preti possono sentire: spari, passi, ping, candele accese, grida.</summary>
    public static class Noise
    {
        /// <summary>Solo per le prove: il raggio dell'ultimo rumore emesso e quello dell'ultimo passo (Passi di cenere lo riduce).</summary>
        public static float lastRadius, lastStepRadius;
        public static NoiseKind lastKind;

        public static void Emit(Vector3 pos, float radius, NoiseKind kind)
        {
            lastRadius = radius; lastKind = kind;
            if (kind == NoiseKind.Step) lastStepRadius = radius;
            if (!Stealth.Enabled) return;
            Coop.Game?.NoiseHeard(pos, radius, kind);       // cooperativa: se non sono l'ospite lo dico a lui, che ha i nemici che ascoltano
            Hear(pos, radius, kind);
        }

        /// <summary>I preti veri ascoltano (i pupazzi degli ospitati non pensano). Anche l'ospite lo usa per i rumori dei compagni.</summary>
        public static void Hear(Vector3 pos, float radius, NoiseKind kind)
        {
            var all = Enemy.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var e = all[i];
                if (e != null) e.Hear(pos, radius, kind);
            }
        }
    }
}
