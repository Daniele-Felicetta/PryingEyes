using System.Collections.Generic;
using Production.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes
{
    /// <summary>Arma del giocatore: due slot, 28 modelli (Resources/Armi), cinque gradi di potenza,
    /// rinculo della visuale con ritorno, dispersione che cresce coi colpi e col movimento, mira con mirini veri
    /// (collimati o con seconda camera per le ottiche), ricarica diversa per tipo, cambio arma, effetti ai colpi.
    /// Le armi si comprano sui muri con i punti (WallBuy.cs).</summary>
    public partial class Weapon : MonoBehaviour
    {
        // ------------------------------------------------------------------ slot e stato
        class Slot
        {
            public int idx = -1, mode; public bool upg;
            public readonly WeaponState state = new WeaponState();
            public int mag { get => state.Magazine; set => state.SetAmmunition(value, state.Reserve); }
            public int reserve { get => state.Reserve; set => state.SetAmmunition(state.Magazine, value); }
        }
        readonly Slot[] slots = { new Slot(), new Slot() };
        int cur;

        public int weaponIndex => Mathf.Max(0, slots[cur].idx);
        public int CurrentIndex => weaponIndex;
        public WeaponDef Def => WeaponDefs.All[weaponIndex];
        public int mag { get => slots[cur].mag; set => slots[cur].mag = value; }
        public int reserve { get => slots[cur].reserve; set => slots[cur].reserve = value; }
        public int magSize => MagFor(Def, slots[cur].upg);
        public int reserveMax => ReserveMaxFor(Def, slots[cur].upg);
        /// <summary>Consacrazione (Consacrazione.cs): un'arma consacrata ha il caricatore e la riserva di una volta e mezza, e fa piu' danno.</summary>
        public const float UpgMag = 1.5f, UpgDamage = 1.7f;
        /// <summary>Caricatore dell'arma: una volta e mezza se consacrata, +25% con il sigillo Caricatore Esteso (Libro dell'Arsenale).</summary>
        int MagFor(WeaponDef d, bool upg)
        {
            float m = OwnerMagic.MagMult;
            int baseMag = upg ? Mathf.CeilToInt(d.mag * UpgMag) : d.mag;
            return m > 1.001f ? Mathf.CeilToInt(baseMag * m) : baseMag;
        }
        static int ReserveMaxFor(WeaponDef d, bool upg) => upg ? Mathf.CeilToInt(d.reserveMax * UpgMag) : d.reserveMax;
        public bool Upgraded => slots[cur].idx >= 0 && slots[cur].upg;
        public bool IsUpgraded(int slot) => slots[slot].idx >= 0 && slots[slot].upg;
        public bool CanConsecrate => slots[cur].idx >= 0 && !slots[cur].upg && swapT <= 0f && !knifeOut;
        public int SlotIndex => cur;
        public int WeaponInSlot(int s) => slots[s].idx;
        public FireMode Mode => Def.modes[slots[cur].mode % Def.modes.Length];

        WeaponState State => slots[cur].state;
        public bool reloading => State.IsReloading;
        /// <summary>Per le prove: a che punto e' la ricarica (0..1; -1 fuori ricarica) e se la fa la mano sinistra.</summary>
        public float ReloadProgress => State.IsReloading ? State.ReloadProgress : -1f;
        public bool ReloadByHand => reloadAnim;
        /// <summary>Per le prove: a che punto e' il ciclo manuale dopo il colpo (pompa, leva, otturatore; -1 fermo).</summary>
        public float CycleProgress => cycleAnim;
        [Header("Stato")]
        public float ads;                      // 0..1 (con curva)
        public float recoil;                   // contraccolpo del modello (0..1), usato anche dal giocatore per un piccolo scatto
        public float bloom;                    // gradi di dispersione accumulata
        float adsRaw;

        [Header("Materiali (assegnati dal builder)")]
        public Material bodyMat, gripMat, flashMat;

        Transform cam;
        Transform viewRoot;
        WeaponRig rig;
        HUD hud;
        PlayerController player;
        PlayerPerks OwnerPerks => player != null ? player.PerkState : Perks.State;
        /// <summary>Lo stato magico di chi impugna l'arma (i sigilli dell'Arsenale valgono per tutte le sue armi).</summary>
        MagicState OwnerMagic => player != null ? player.Arcana : Magic.State;
        /// <summary>Cadenza: Doppio colpo (perk) per Raffica Mortale (sigillo).</summary>
        float RateK => OwnerPerks.RateMult * OwnerMagic.RateMult;
        GameObject flashA, flashB;
        Light flashLight;
        Light fill;
        /// <summary>Strato di rendering dell'arma e delle mani (il mondo sta nello strato 1): la torcia e le lampade del piano
        /// la illuminano (le lampade) o no (la torcia, che a 40 cm la brucerebbe), e la luce di riempimento vale solo qui.</summary>
        public const uint ViewLayer = 2u;
        Camera scopeCam;
        RenderTexture scopeRT;
        Renderer scopeRenderer;
        Material scopeMat;
        float scopeRadius = 0.0185f;
        Renderer reticleRenderer;
        MaterialPropertyBlock aimProperties;

        float fireT => State.FireDelay;
        float cycleT => State.CycleDelay;
        float reloadT => State.ReloadElapsed;
        bool reloadEmpty => State.ReloadStartedEmpty;
        float swapT, equipT, lastShot = -99f, flashT;
        int burstLeft, streak;
        bool triggerHeld, dryPlayed, sprintHeld;
        bool adsToggled;       // la mira a interruttore (GameSettings.AimToggle)
        float sprintBlend, kickBack, kickPitch, kickRoll, swayX, swayY, swayVx, swayVy, breath;
        float cycleAnim = -1f, cycleLen = 0.6f;
        float pendingSlotTime;
        int pendingSlot = -1;
        float hipYaw = -3.5f;

        // mani
        Transform handL, handR;
        Vector3 handLRest;
        /// <summary>I pezzi del guanto sinistro avvolto (presa naturale: restano spenti).</summary>
        Renderer[] wrappedL;
        Renderer[] handLRend;
        bool handLOnMag;
        GameObject handsGo;
        // mano destra troppo vicina all'occhio in mira (la leva): in mira si nasconde, il piano vicino la taglierebbe a brandelli
        Renderer[] handRRend;
        bool handRHideAds, handRVis = true;
        // braccia col gomito (genera_braccia.py): la manica si piega quando la mano si sposta rispetto all'arma
        ArmRig armR, armL;
        // la seconda mano che lascia l'arma: magie, sigilli tracciati sull'arma, lanci (OffHand, mano libera articolata HandRig)
        OffHand offHand;
        float holsterT;
        bool detonationShot;
        /// <summary>La seconda mano (null senza mano libera o senza mani su misura).</summary>
        public OffHand OffHand => offHand;
        public Transform ViewRootT => viewRoot;
        public WeaponRig Rig => rig;
        bool OffBusy => offHand != null && offHand.Busy;
        /// <summary>La sinistra fa un gesto (magia, sigillo, lancio), non la ricarica: la ricarica si puo' interrompere col cambio o il coltello.</summary>
        bool GestureBusy => OffBusy && !reloadAnim;
        // indice destro articolato: disteso lungo il telaio a riposo, sul grilletto quando si spara o si mira (TriggerFinger)
        TriggerFinger trigFinger;
        /// <summary>La mano destra articolata (presa realistica): al posto del guanto modellato e di TriggerFinger.</summary>
        GripHand gripHand;
        float trigBlend;
        /// <summary>Quanto l'indice sta sul grilletto (0 disteso, 1 sul grilletto): per le prove.</summary>
        public float TriggerBlend => trigBlend;

        public bool IsBound => cam != null && viewRoot != null;

        /// <summary>Posa al fianco del modello in prima persona (riferimento della camera) per classe d'arma. Le pistole, anche quelle
        /// urbane, stanno piu' in alto e piu' vicine al centro: dal fianco sporgerebbe solo uno spigolo del carrello. Usata anche da
        /// ArmiShots e, con gli stessi numeri, da modelli/hotel/genera_braccia.py per scegliere la posa delle braccia.</summary>
        // ------------------------------------------------------------------ presa realistica (2026-11-16)
        /// <summary>Armi tenute a distanza umana, con braccia vere (ArmRig.Body): al fianco le armi stavano a 50-62 cm dall'occhio
        /// con l'astina del fucile a un metro, e il braccio del modello era un tubo virtuale lungo quasi un metro. Prima il prototipo su
        /// fucile e pistola (questa tabella, tarata a mano), poi tutte le armi con i valori della loro classe (HoldOf); -presavecchia
        /// le riporta alla presa di prima (per confrontare). Per ognuna: dove sta l'arma al fianco (camera), di quanto e' girata, di
        /// quanto il tiratore gira il busto (la spalla sinistra avanti: e' cosi' che la sinistra arriva all'astina col gomito piegato) e
        /// di quanto la sinistra scorre indietro sull'astina (-1 = da sola: fin dove arriva il braccio, 28 cm davanti all'impugnatura).</summary>
        static readonly Dictionary<string, (Vector3 hip, float yaw, float stance, float slideL)> RealHoldTable = new Dictionary<string, (Vector3, float, float, float)>
        {
            // fucile: la destra a 30 cm davanti all'occhio (era a 63) e 5 sotto la spalla, la sinistra sull'astina a circa 65 (era a 99); busto girato di 35 gradi
            // la sinistra era modellata 37 cm davanti all'impugnatura, vicino al mirino: scorre 9 cm indietro sul binario dell'astina
            { "ar9_malleus", (new Vector3(0.12f, -0.19f, 0.29f), -8f, 35f, 0.09f) },
            // pistola: la destra a 34 cm (era a 51), busto quasi frontale
            { "p9_pieta", (new Vector3(0.095f, -0.15f, 0.33f), -3.5f, 10f, 0f) },
        };
        static int oldHold = -1;
        /// <summary>Rotazione del busto della presa realistica di quest'arma (0 = presa vecchia).</summary>
        float holdStance;
        /// <summary>Il moto del corpo per le due spalle in questo fotogramma (UpdatePose -> ArmRig.BodyMotion).</summary>
        Vector3 bodyR, bodyL;
        public static bool RealHold(WeaponDef d)
        {
            if (oldHold < 0) oldHold = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-presavecchia") >= 0 ? 1 : 0;
            return d != null && oldHold == 0 && !d.id.StartsWith("kn_");
        }

        /// <summary>La presa realistica di quest'arma: quella tarata a mano se c'e', se no quella della sua classe (pistole e revolver
        /// come la P9, mitragliette tra pistola e fucile, tutto il resto come l'AR-9).</summary>
        static (Vector3 hip, float yaw, float stance, float slideL) HoldOf(WeaponDef d)
        {
            if (RealHoldTable.TryGetValue(d.id, out var t)) return t;
            string cls = d.cls;
            if (cls == "PISTOLA MITRAGLIATRICE" || cls == "MICRO-MITRA") return (new Vector3(0.11f, -0.175f, 0.30f), -6f, 25f, -1f);
            if (cls != null && (cls.StartsWith("PISTOLA") || cls == "REVOLVER")) return (new Vector3(0.095f, -0.15f, 0.33f), -3.5f, 10f, 0f);
            // calcio dritto senza impugnatura a pistola: la destra stringe il collo del calcio col palmo in giu' e il polso devia di
            // lato; 6 cm piu' in basso l'avambraccio sale meno, l'arma girata un poco di piu' verso il centro segue l'avambraccio che
            // arriva da destra (e il gomito sta piu' in fuori, StraightStock)
            if (StraightStock(d)) return (new Vector3(0.13f, -0.25f, 0.29f), -13f, 35f, -1f);
            return (new Vector3(0.12f, -0.19f, 0.29f), -8f, 35f, -1f);
        }

        /// <summary>Calcio dritto, senza impugnatura a pistola (leva, otturatore, doppietta, pompa): la destra stringe il collo del calcio
        /// col palmo girato in giu', e il gomito destro sta piu' in fuori (come chi imbraccia una doppietta), se no l'avambraccio
        /// dovrebbe ruotare oltre i 90 gradi.</summary>
        internal static bool StraightStock(WeaponDef d)
        {
            string cls = d?.cls;
            return cls != null && (cls.Contains("LEVA") || cls.Contains("OTTURATORE") || cls == "DOPPIETTA" || cls.Contains("POMPA"));
        }

        /// <summary>La posa al fianco di quest'arma: quella della presa realistica se c'e', se no quella della sua classe.</summary>
        static Vector3 HipOf(WeaponDef d) => RealHold(d) ? HoldOf(d).hip : HipFor(d.cls);

        public static Vector3 HipFor(string cls)
        {
            if (cls == "PISTOLA MITRAGLIATRICE" || cls == "MICRO-MITRA") return new Vector3(0.21f, -0.19f, 0.54f);
            if (cls != null && (cls.StartsWith("PISTOLA") || cls == "REVOLVER")) return new Vector3(0.175f, -0.165f, 0.50f);
            return new Vector3(0.28f, -0.26f, 0.62f);
        }

        static Renderer[] WithSkin(Renderer[] rs, ArmRig arm)
        {
            if (arm == null || arm.Skin == null) return rs;
            var list = new List<Renderer>(rs ?? new Renderer[0]) { arm.Skin };
            return list.ToArray();
        }

        // coltello (Knife.cs): mentre fende, l'arma si abbassa, sparisce e sta zitta (niente fuoco, mira, ricarica, cambio)
        bool knifeOut;
        float knifeBlend;
        float throwT;                       // lancio dell'equipaggiamento: l'arma si abbassa e torna (Equipment)
        float inspectT;                     // ispezione (I): l'arma si gira a mostrare il fianco e torna
        const float InspectTime = 2.6f;
        /// <summary>Ispezione in corso (per le prove).</summary>
        public bool Inspecting => inspectT > 0f;

        /// <summary>L'arma si abbassa mezzo secondo mentre la mano lancia acquasanta o carillon.</summary>
        public void ThrowDip() { throwT = 0.5f; inspectT = 0f; }

        /// <summary>Ispezione dell'arma (I): pubblico per le prove.</summary>
        public void Inspect() { inspectT = InspectTime; Synth.Mech("draw", 0.35f, 0.9f); }
        public bool KnifeBusy => knifeOut;
        public bool CanKnife => swapT <= 0f && !GestureBusy;

        /// <summary>La mano sinistra puo' lasciare l'arma per un gesto? Niente durante la ricarica, il cambio, l'estrazione, il coltello,
        /// la corsa o un altro gesto. 'why' vuoto = no in silenzio (non serve dirlo).</summary>
        public bool CanUseOffHand(out string why)
        {
            why = "";
            if (cam == null || viewRoot == null || OffBusy) return false;
            if (knifeOut || knifeBlend > 0.01f || swapT > 0f || pendingSlot >= 0) return false;
            if (equipT > Def.drawTime * 0.5f) return false;
            if (sprintBlend > 0.3f) return false;
            if (reloading) { why = "Prima finisci di ricaricare."; return false; }
            inspectT = 0f;
            why = null;
            return true;
        }

        /// <summary>Il coltello esce (o rientra). Fuori: la ricarica in corso salta e la fiammata si spegne; dentro: l'arma risale.</summary>
        public void KnifeOut(bool on)
        {
            if (knifeOut == on) return;
            knifeOut = on;
            if (!on) return;
            offHand?.Cancel();
            if (reloading) CancelReload();
            burstLeft = 0; cycleAnim = -1f;
            if (flashA != null && flashA.activeSelf) { flashA.SetActive(false); flashB.SetActive(false); flashLight.intensity = 0f; }
        }

        // solo per le prove automatiche (-guntest): mira forzata, sparo singolo, immagine della seconda camera subito
        public static bool debugAds;
        public void DebugFire() { if (mag > 0 && cam != null && viewRoot != null) Shoot(Def); }
        /// <summary>Solo per le prove: nasconde arma e mani (le foto di cio' che c'e' davanti, -impactshots).</summary>
        public void DebugHideView(bool hide) { if (viewRoot != null) foreach (var r in viewRoot.GetComponentsInChildren<Renderer>(true)) r.enabled = !hide; }
        public void RenderScopeNow() { LateUpdate(); }
        public float LastShotTime => lastShot;
        public bool CanFireNow => State.Ready && swapT <= 0f &&
            !knifeOut && sprintBlend <= 0.35f && equipT <= Def.drawTime * 0.6f;

        // ------------------------------------------------------------------ avvio
        public void Bind(Transform camera, HUD h)
        {
            cam = camera;
            hud = h;
            player = GetComponent<PlayerController>();
            if (slots[0].idx < 0) GiveStart();
            BuildViewmodel();
            RefreshHud();
        }

        void Awake()
        {
            if (cam == null)
            {
                var t = transform.Find("MainCamera");
                if (t != null) cam = t;
            }
            if (hud == null) hud = FindAnyObjectByType<HUD>();
            if (player == null) player = GetComponent<PlayerController>();
            if (slots[0].idx < 0) GiveStart();
            if (cam != null) BuildViewmodel();
            RefreshHud();
        }

        void OnDestroy() => DestroyScope();

        void GiveStart()
        {
            int i = WeaponDefs.IndexOf("p9_pieta");
            slots[0] = new Slot { idx = i, mag = WeaponDefs.All[i].mag, reserve = WeaponDefs.All[i].reserve };
            slots[1] = new Slot();
            cur = 0;
        }

        /// <summary>Riparte da zero: pistola di partenza, secondo slot vuoto (nuova partita).</summary>
        public void ResetLoadout()
        {
            GiveStart();
            ResetActionState();
            swapT = 0f; pendingSlot = -1; knifeOut = false; knifeBlend = 0f;
            if (cam != null) BuildViewmodel();
            RefreshHud();
        }

        /// <summary>Riscrive HUD (munizioni, nome, grado, slot): si chiama quando l'HUD e' certamente costruito.</summary>
        public void Refresh() { RefreshHud(); }

        void RefreshHud()
        {
            if (hud == null) return;
            var d = Def;
            hud.SetAmmo(mag, reserve);
            hud.SetReloading(reloading);
            hud.SetWeapon(d, d.ModeLabel(Mode), cur, slots[0].idx >= 0 ? WeaponDefs.All[slots[0].idx] : null, slots[1].idx >= 0 ? WeaponDefs.All[slots[1].idx] : null,
                slots[cur].upg, IsUpgraded(0), IsUpgraded(1));
        }

        // ------------------------------------------------------------------ equipaggiamento
        /// <summary>Acquisto al muro o reset: se l'arma e' gia' in uno slot lo riempie; altrimenti occupa lo slot libero, e a slot
        /// pieni sostituisce quella in mano. full = caricatore e riserva pieni.</summary>
        public void Equip(int index, bool full = true)
        {
            index = Mathf.Clamp(index, 0, WeaponDefs.All.Length - 1);
            var d = WeaponDefs.All[index];
            int target = -1;
            for (int s = 0; s < 2; s++) if (slots[s].idx == index) target = s;
            if (target < 0) target = slots[0].idx < 0 ? 0 : slots[1].idx < 0 ? 1 : cur;
            // ricomprare l'arma che hai gia' consacrata la ricarica e basta: la consacrazione resta
            bool keepUpg = slots[target].idx == index && slots[target].upg;
            slots[target] = new Slot { idx = index, mag = MagFor(d, keepUpg), reserve = keepUpg ? Mathf.CeilToInt(d.reserve * UpgMag) : d.reserve, upg = keepUpg };
            if (!full) { slots[target].mag = Mathf.Min(MagFor(d, keepUpg), slots[target].mag); }
            bool same = target == cur;
            cur = target;
            ResetActionState();
            swapT = 0f; pendingSlot = -1;
            BuildViewmodel();
            equipT = d.drawTime;
            RefreshHud();
            if (!same) Synth.Mech("draw", 0.6f, 1f);
        }

        public int ReserveOf(int slot) => slots[slot].reserve;
        public int ReserveMaxOf(int slot) => slots[slot].idx < 0 ? 0 : ReserveMaxFor(WeaponDefs.All[slots[slot].idx], slots[slot].upg);
        public bool Owns(int index) => slots[0].idx == index || slots[1].idx == index;
        public int SlotOf(int index) => slots[0].idx == index ? 0 : slots[1].idx == index ? 1 : -1;

        public void FillReserve(int slot = -1)
        {
            int s = slot < 0 ? cur : slot;
            if (slots[s].idx < 0) return;
            slots[s].reserve = ReserveMaxFor(WeaponDefs.All[slots[s].idx], slots[s].upg);
            if (s == cur) RefreshHud();
        }

        public void AddAmmoFraction(float f)
        {
            for (int s = 0; s < 2; s++)
            {
                if (slots[s].idx < 0) continue;
                var d = WeaponDefs.All[slots[s].idx];
                int rm = ReserveMaxFor(d, slots[s].upg);
                slots[s].reserve = Mathf.Min(rm, slots[s].reserve + Mathf.CeilToInt(rm * f));
            }
            RefreshHud();
        }

        /// <summary>Il Reliquiario (Drops.cs): annulla la ricarica e riempie caricatore e riserva di entrambe le armi.</summary>
        public void MaxAmmo()
        {
            if (reloading) CancelReload();
            for (int s = 0; s < 2; s++)
            {
                if (slots[s].idx < 0) continue;
                var d = WeaponDefs.All[slots[s].idx];
                slots[s].mag = MagFor(d, slots[s].upg);
                slots[s].reserve = ReserveMaxFor(d, slots[s].upg);
            }
            RefreshHud();
        }

        /// <summary>Alla rinascita: caricatore pieno e riserva almeno quella di partenza, per entrambe le armi.</summary>
        public void RespawnAmmo()
        {
            for (int s = 0; s < 2; s++)
            {
                if (slots[s].idx < 0) continue;
                var d = WeaponDefs.All[slots[s].idx];
                slots[s].mag = MagFor(d, slots[s].upg);
                slots[s].reserve = Mathf.Min(ReserveMaxFor(d, slots[s].upg), Mathf.Max(slots[s].reserve, d.reserve));
            }
            ResetActionState();
            swapT = 0f; pendingSlot = -1;
            RefreshHud();
        }

        public void SwitchSlot(int s)
        {
            if (s < 0 || s > 1 || s == cur || slots[s].idx < 0 || swapT > 0f || knifeOut || GestureBusy) return;
            // l'arma in mano scende; a meta' si cambia il modello
            EndReloadAnim();
            State.CancelReload(); hud?.SetReloading(false);
            burstLeft = 0; cycleAnim = -1f;
            pendingSlot = s;
            swapT = 0.16f;
            pendingSlotTime = 0.16f;
            Synth.Mech("holster", 0.5f, 1f);
        }

        // I tempi e il rinculo appartengono all'arma estratta, non al prossimo slot o alla prossima vita.
        void ResetActionState()
        {
            State.ResetActions();
            burstLeft = 0; cycleAnim = -1f;
            triggerHeld = dryPlayed = false;
            bloom = recoil = 0f; streak = 0; streakT = 0f; lastSpread = 0f; lastShotPrev = -99f;
            adsRaw = ads = 0f;
            kickBack = kickPitch = kickRoll = 0f;
        }

        void ToggleMode()
        {
            var d = Def;
            if (d.modes.Length < 2) return;
            slots[cur].mode = (slots[cur].mode + 1) % d.modes.Length;
            burstLeft = 0;
            Synth.Mech("click", 0.5f, 1.2f);
            RefreshHud();
        }

        // ------------------------------------------------------------------ modello
        void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>Fiammata procedurale a stella (niente quadrato arancione).</summary>
        public static Texture2D BuildFlashStar()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float nx = (x + 0.5f) / S * 2f - 1f, ny = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float core = Mathf.Clamp01(1f - r * 1.7f);
                    float ang = Mathf.Atan2(ny, nx);
                    float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(2f * ang)), 10f) * Mathf.Clamp01(1.15f - r * 1.15f);
                    float a = Mathf.Clamp01(core * 1.25f + spike * 0.9f);
                    Color c = Color.Lerp(new Color(1f, 0.42f, 0.08f), new Color(1f, 0.98f, 0.85f), core);
                    px[y * S + x] = new Color(c.r, c.g, c.b, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        /// <summary>Usata dal builder della scena: il materiale della fiammata va salvato come asset (la variante trasparente resta nella build).</summary>
        public static void ConfigureFlash(Material m, Texture2D tex) => Fx.ConfigureUnlit(m, tex, true);

        static GameObject LoadPrefab(string id) => Resources.Load<GameObject>("Armi/" + id);

        /// <summary>Modello dell'arma per l'esposizione sul muro (senza mani, fiammata e luci).</summary>
        public static GameObject BuildDisplayModel(int index)
        {
            var d = WeaponDefs.All[index];
            var prefab = LoadPrefab(d.id);
            GameObject g;
            if (prefab != null) g = Instantiate(prefab);
            else
            {
                g = new GameObject("Arma");
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(g.transform, false);
                b.transform.localScale = new Vector3(0.05f, 0.1f, 0.7f);
            }
            g.name = "Arma";
            foreach (var c in g.GetComponentsInChildren<Collider>()) Destroy(c);
            return g;
        }

        void BuildViewmodel()
        {
            if (cam == null || !Application.isPlaying) return;
            offHand?.Cancel();
            offHand = null;
            if (viewRoot != null) { SafeDestroy(viewRoot.gameObject); viewRoot = null; }
            for (int i = cam.childCount - 1; i >= 0; i--)
            {
                var c = cam.GetChild(i);
                if (c != null && c.name == "Viewmodel") SafeDestroy(c.gameObject);
            }
            DestroyScope();
            var d = Def;
            var root = new GameObject("Viewmodel");
            root.layer = Seats.ViewLayer;       // arma e mani: nello split screen lo strato di questo giocatore (le camere degli altri non lo disegnano)
            root.transform.SetParent(cam, false);
            root.transform.localPosition = new Vector3(0.28f, -0.26f, 0.62f);
            root.transform.localRotation = Quaternion.identity;
            viewRoot = root.transform;

            var prefab = LoadPrefab(d.id);
            GameObject model;
            if (prefab != null) model = Instantiate(prefab, root.transform);
            else
            {
                Debug.LogWarning("[PryingEyes] prefab dell'arma mancante: Armi/" + d.id);
                model = new GameObject("Arma");
                model.transform.SetParent(root.transform, false);
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(model.transform, false);
                b.transform.localScale = new Vector3(0.05f, 0.1f, 0.7f);
                b.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            }
            model.name = "Arma";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Seats.ViewLayer;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            rig = model.GetComponent<WeaponRig>();
            if (rig == null) rig = model.AddComponent<WeaponRig>();
            rig.Setup();
            reticleRenderer = rig.reticle != null ? rig.reticle.GetComponent<Renderer>() : null;
            foreach (var r in rig.renderers) { if (r != null) { r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = ViewLayer; } }

            AddHands(root, d);
            BuildFlash(root, d);
            SetupScope(d);
            equipT = d.drawTime;
            cycleAnim = -1f;
            handLOnMag = rig.mag != null && d.hand > 0.5f;
            ApplyUpgradeLook();
        }

        /// <summary>La Consacrazione (altare): l'arma in mano diventa consacrata. Caricatore e riserva sono una volta e mezza e vengono
        /// riempiti, il danno sale di 1,7 volte, i metalli si tingono d'oro. Falso se e' gia' consacrata o stai cambiando arma.</summary>
        public bool Consecrate()
        {
            if (!CanConsecrate) return false;
            var s = slots[cur];
            s.upg = true;
            s.mag = MagFor(Def, true);
            s.reserve = ReserveMaxFor(Def, true);
            if (reloading) CancelReload();
            ApplyUpgradeLook();
            RefreshHud();
            return true;
        }

        /// <summary>Un materiale dell'arma in versione consacrata (uno per materiale, in cache): colore moltiplicato per un oro caldo, piu'
        /// metallico e lucido, e un velo di luce propria perche' l'oro si veda anche dove non arriva la luce. Il materiale condiviso non cambia.</summary>
        static Material GoldOf(Material m)
        {
            if (goldMats.TryGetValue(m, out var g) && g != null) return g;
            g = new Material(m) { name = m.name + "_Oro" };
            Color bc = g.GetColor("_BaseColor");
            g.SetColor("_BaseColor", new Color(bc.r * 1.75f, bc.g * 1.30f, bc.b * 0.55f, bc.a));
            g.SetFloat("_Metallic", Mathf.Max(0.82f, g.HasProperty("_Metallic") ? g.GetFloat("_Metallic") : 0f));
            g.SetFloat("_Smoothness", Mathf.Max(0.72f, g.HasProperty("_Smoothness") ? g.GetFloat("_Smoothness") : 0f));
            g.EnableKeyword("_EMISSION");
            g.SetColor("_EmissionColor", new Color(0.17f, 0.095f, 0.016f));
            g.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            goldMats[m] = g;
            return g;
        }
        static readonly Dictionary<Material, Material> goldMats = new Dictionary<Material, Material>();

        /// <summary>Incisione dorata dell'arma consacrata: i pezzi in metallo prendono la versione dorata del loro materiale (GoldOf).
        /// I vetri delle ottiche restano come sono. Il modello si ricostruisce a ogni cambio d'arma, quindi non c'e' niente da rimettere a posto.</summary>
        void ApplyUpgradeLook()
        {
            if (rig == null || rig.renderers == null || !slots[cur].upg) return;
            foreach (var r in rig.renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.name.Contains("Vetro") || m.name.Contains("Fx_") || !m.HasProperty("_BaseColor")) continue;
                    mats[i] = GoldOf(m);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        void BuildFlash(GameObject root, WeaponDef d)
        {
            Vector3 muzzle = rig.MuzzleLocal;
            Material mat = flashMat != null && flashMat.mainTexture != null ? flashMat : Fx.FlashMat;
            if (Fx.FlashMat == null) { var f = Fx.Unlit(Fx.StarTex(), true); Fx.FlashMat = f; mat = f; }
            flashA = MakeQuad("Flash", root.transform, muzzle, mat);
            flashB = MakeQuad("FlashLato", root.transform, muzzle, mat);
            var fb = new MaterialPropertyBlock();
            fb.SetColor("_BaseColor", Color.Lerp(Color.white, d.flashColor, 0.7f));
            flashA.GetComponent<MeshRenderer>().SetPropertyBlock(fb);
            flashB.GetComponent<MeshRenderer>().SetPropertyBlock(fb);
            flashA.SetActive(false); flashB.SetActive(false);
            // la luce del lampo sta 40 cm davanti alla bocca: con la luce alla mano l'intensita' col quadrato lavava via i guanti
            var fgo = new GameObject("FlashLight");
            fgo.transform.SetParent(root.transform, false);
            fgo.transform.localPosition = muzzle + new Vector3(0f, 0f, 0.4f);
            flashLight = fgo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = d.flashColor;
            flashLight.intensity = 0f;
            flashLight.range = 6f;
            flashLight.shadows = LightShadows.None;
            Stealth.SetLightLayers(flashLight, 3u);      // il lampo di bocca illumina mondo e arma
            // luce di riempimento dedicata all'arma (la hall e' buia e il metallo sparirebbe)
            fill = new GameObject("ViewmodelFill").AddComponent<Light>();
            fill.transform.SetParent(root.transform, false);
            // lontana dall'arma (dietro e sopra la spalla sinistra): a 30 cm l'intensita' sale col quadrato e il polimero nero esce bianco
            fill.transform.localPosition = new Vector3(-0.32f, 0.55f, -0.35f);
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.95f, 0.88f);
            // in Unity il diffuso e' albedo x illuminamento (senza 1/pi): E = intensita' / distanza^2, a 65 cm ~1.3 -> polimero nero a ~70/255
            fill.intensity = 0.75f;
            fill.range = 2.6f;
            fill.shadows = LightShadows.None;
            // solo l'arma e le mani: se illuminasse anche i muri vicini, al buio si vedrebbe un alone fantasma intorno al giocatore
            Stealth.SetLightLayers(fill, ViewLayer);
        }

        static GameObject MakeQuad(string name, Transform parent, Vector3 pos, Material m)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            var c = q.GetComponent<Collider>();
            if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.layer = Seats.ViewLayer;
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q;
        }

        // ------------------------------------------------------------------ mani (Resources/ManiArmi/<id>, da modelli/hotel/genera_mani2.py)
        /// <summary>Guanti e maniche. Ogni arma ha il suo paio, modellato sulla sua impugnatura (destra) e sulla sua astina o sul suo
        /// aggancio grip_l (sinistra): le dita avvolgono il pezzo vero e non serve spostare niente; il punto dell'Empty mano_s e' il
        /// palmo, quello che la ricarica muove. Se l'arma non ha mani su misura si ripiega sul paio unico (Resources/Mani, da
        /// genera_mani.py): destra sull'impugnatura, sinistra spostata su grip_l. Le armi a una mano nascondono la sinistra.</summary>
        void AddHands(GameObject root, WeaponDef d)
        {
            handL = handR = null; handLRend = null; handsGo = null; wrappedL = null;
            handRRend = null; handRHideAds = false; handRVis = true;
            armR = armL = null;
            hipYaw = RealHold(d) ? HoldOf(d).yaw : -3.5f;
            holdStance = RealHold(d) ? HoldOf(d).stance : 0f;
            trigFinger = null;
            gripHand = null;
            offHand = null;
            reloadAnim = false; magCarry = 0; pendingDrop = false;
            ClearProp();
            var prefab = Resources.Load<GameObject>("ManiArmi/" + d.id);
            bool onMeasure = prefab != null;
            if (!onMeasure) prefab = Resources.Load<GameObject>("Mani");
            if (prefab == null) return;
            var g = Instantiate(prefab, root.transform);
            handsGo = g;
            g.name = "Mani";
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.identity;
            foreach (var t in g.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "mano_d") handR = t;
                else if (t.name == "mano_s") handL = t;
                t.gameObject.layer = Seats.ViewLayer;
            }
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = ViewLayer; }
            if (!onMeasure && handR != null && d.handDrop != 0f) handR.position += root.transform.TransformVector(new Vector3(0f, -d.handDrop, 0f));
            if (onMeasure)
            {
                // con la presa realistica la pelle e' la manica neutra (una per tutte le armi, con le proporzioni vere)
                armR = ArmRig.Build(g.transform, handR, "d", RealHold(d));
                armL = ArmRig.Build(g.transform, handL, "s", RealHold(d));
                // spalle attaccate al corpo: nella posa al fianco coincidono con quelle modellate (la radice delle mani e' il viewRoot)
                Vector3 hipP = HipOf(d);
                Quaternion hipQ = Quaternion.Euler(0f, hipYaw, 0f);
                armR?.AnchorTo(cam, hipP + hipQ * armR.ShoulderLocal);
                armL?.AnchorTo(cam, hipP + hipQ * armL.ShoulderLocal);
                trigFinger = TriggerFinger.Build(handR);
                trigBlend = 0f;
                if (armR != null) armR.AdsDrop = 0.20f;
                if (armL != null) armL.AdsDrop = 0.04f;
                if (RealHold(d))
                {
                    // braccia vere: spalle del corpo col busto girato, omero di 30 cm, gomiti in giu' e in fuori
                    float st = HoldOf(d).stance;
                    // il gomito destro un po' in fuori (45 gradi): pendendo dritto l'avambraccio saliva ripido all'impugnatura e il polso
                    // doveva piegarsi di lato di 45-55 gradi per tenere l'arma in piano (il limite e' ~30)
                    Vector3 poleR = StraightStock(d) ? new Vector3(0.74f, -0.62f, -0.20f) : new Vector3(0.58f, -0.78f, -0.20f);
                    if (armR != null) { armR.Body = true; armR.Stance = st; armR.AdsDrop = 0f; armR.RestPole = armR.PoleHint = poleR; }
                    if (armL != null) { armL.Body = true; armL.Stance = st; armL.AdsDrop = 0f; armL.RestPole = armL.PoleHint = new Vector3(-0.35f, -0.92f, -0.15f); }
                    // in mira (0.7.1, scelti con la sonda di -armview -adsprobe su tutte le 30 armi, che pesa i campi articolari e il braccio
                    // davanti alla vista): col calcio la spalla destra va sotto il calcio (6 cm verso il centro, 16 su con la testa che scende
                    // sul calcio, 6 avanti) e il gomito gira liberamente attorno all'asse spalla-polso; la sinistra avanza verso l'astina (12 cm
                    // avanti e 10 su; sui fucili a calcio dritto resta dov'e': piu' su l'avambraccio sinistro copriva mezza vista, piu' avanti
                    // sulla carabina a leva il braccio si ripiegava del tutto). Le pistole non hanno calcio: le spalle restano dove sono (alzate,
                    // il braccio passava davanti agli occhi). Prima in mira il gomito restava giu' come a riposo e il polso destro usciva dai
                    // campi articolari su tutte le armi. Sulle quattro armi a calcio dritto (leva, otturatore, pompe) la mano destra modellata sul
                    // calcio chiede all'avambraccio 110-140 gradi di torsione: nessun gomito tiene insieme polso e vista libera, quindi il braccio
                    // destro resta quello della 0.7.0 (limite noto: va rifatta la presa)
                    bool pistol = d.hand <= 0.5f, straight = StraightStock(d);
                    if (armR != null)
                    {
                        armR.AdsSwivel = straight ? 40f : pistol ? 140f : 180f;
                        armR.AdsElbowRise = straight ? 0f : 0.20f;
                        armR.AdsShoulder = pistol || straight ? Vector3.zero : new Vector3(-0.06f, 0.16f, 0.06f);
                    }
                    if (armL != null)
                    {
                        armL.AdsSwivel = 140f; armL.AdsElbowRise = 0.20f;
                        armL.AdsShoulder = pistol || straight ? Vector3.zero : new Vector3(0f, 0.10f, 0.12f);
                    }
                }
            }
            if (handL != null)
            {
                if (!onMeasure)
                {
                    Vector3 hg = new Vector3(0f, 0.005f, 0.31f);          // dove la mano sinistra sta sul fucile d'assalto
                    Vector3 target = rig.gripL != null ? root.transform.InverseTransformPoint(rig.gripL.position) : hg;
                    handL.position += root.transform.TransformVector(target - hg);
                }
                // presa realistica: la sinistra piu' vicina all'impugnatura, lungo l'astina (asse dell'arma)
                if (RealHold(d))
                {
                    float sl = HoldOf(d).slideL;
                    if (sl < 0f) sl = Mathf.Max(0f, root.transform.InverseTransformPoint(handL.position).z - 0.28f);
                    if (sl > 0f) handL.position += root.transform.TransformVector(new Vector3(0f, 0f, -sl));
                }
                handLRest = root.transform.InverseTransformPoint(handL.position);
                var wrappedLeft = handL.GetComponentsInChildren<Renderer>(true);
                handLRend = WithSkin(wrappedLeft, armL);
                // la mano libera nasce qui, sotto mano_s, dove sta il guanto avvolto (OffHand)
                if (onMeasure) offHand = OffHand.Build(cam, root.transform, g.transform, handL, armL, rig, d, wrappedLeft);
                if (offHand != null) { offHand.RealHold = RealHold(d); offHand.InitParts(d.magUp); }
                // presa realistica a due mani: la sinistra sotto l'astina come la tiene un tiratore (la mano articolata al posto del guanto
                // modellato per la spalla finta); il nuovo riposo vale anche per le ricariche
                if (offHand != null && RealHold(d) && d.hand > 0.5f) { handLRest = offHand.SetNaturalRest(); wrappedL = wrappedLeft; }
            }
            if (handR != null)
            {
                // in mira l'occhio sta adsDist dietro il punto di mira: se il palmo destro risulta a meno di 17 cm dall'occhio
                // (la leva: il mirino e' sulla canna) il guanto riempie lo schermo
                handRRend = WithSkin(handR.GetComponentsInChildren<Renderer>(true), armR);
                // presa realistica: la destra e' la mano articolata (la stessa della sinistra, specchiata) al posto del guanto modellato
                if (RealHold(d) && onMeasure)
                {
                    gripHand = GripHand.Build(handR, armR, d, handRRend);
                    if (gripHand != null)
                    {
                        var list = new List<Renderer>();
                        foreach (var r in handRRend) if (r != null && System.Array.IndexOf(gripHand.Replaced, r) < 0) list.Add(r);
                        list.AddRange(gripHand.Renderers);
                        handRRend = list.ToArray();
                        trigFinger = null;
                    }
                }
                Vector3 hp = root.transform.InverseTransformPoint(handR.position);
                handRHideAds = d.adsDist - rig.AdsLocal.z + hp.z < 0.17f;
            }
            InitWorkHand(d);
        }

        // ------------------------------------------------------------------ ottica ingrandita (seconda camera)
        void DestroyScope()
        {
            if (scopeCam != null) SafeDestroy(scopeCam.gameObject);
            if (scopeRT != null) { scopeRT.Release(); SafeDestroy(scopeRT); }
            if (scopeMat != null) SafeDestroy(scopeMat);
            scopeCam = null; scopeRT = null; scopeRenderer = null; scopeMat = null;
        }

        void SetupScope(WeaponDef d)
        {
            if (rig.scopeLens == null || d.zoom < 1.4f) return;
            scopeRenderer = rig.scopeLens.GetComponent<Renderer>();
            if (scopeRenderer == null) return;
            scopeMat = scopeRenderer.material;          // istanza per arma
            var mf = rig.scopeLens.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) scopeRadius = Mathf.Max(mf.sharedMesh.bounds.extents.x, mf.sharedMesh.bounds.extents.z);
            scopeRT = new RenderTexture(768, 768, 16, RenderTextureFormat.ARGB32) { antiAliasing = 1, name = "ScopeRT", useMipMap = false };
            scopeRT.Create();
            var go = new GameObject("ScopeCam");
            go.transform.SetParent(cam, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            scopeCam = go.AddComponent<Camera>();
            var main = cam.GetComponent<Camera>();
            scopeCam.CopyFrom(main);
            scopeCam.ResetWorldToCameraMatrix();
            scopeCam.ResetProjectionMatrix();
            scopeCam.rect = new Rect(0f, 0f, 1f, 1f);          // la camera del giocatore ha la sua porzione di schermo: la texture dell'ottica e' tutta sua
            scopeCam.targetTexture = scopeRT;
            scopeCam.cullingMask = ~((1 << 2) | (1 << 5) | Seats.AllViewLayersMask);
            scopeCam.nearClipPlane = 0.15f;
            scopeCam.fieldOfView = 8f;
            scopeCam.aspect = 1f;
            scopeCam.enabled = false;
            var ad = scopeCam.GetUniversalAdditionalCameraData();
            ad.renderPostProcessing = false;
            ad.antialiasing = AntialiasingMode.None;
            ad.renderShadows = true;
            scopeMat.SetTexture("_ScopeTex", scopeRT);
            scopeMat.SetFloat("_Radius", scopeRadius);
            scopeMat.SetFloat("_Zoom", d.zoom);
            scopeMat.SetFloat("_Reticle", d.pattern == 3 ? 3f : 4f);
            scopeMat.SetFloat("_Lit", d.pattern == 3 ? 1f : 0f);
        }

        void LateUpdate() { using (Seats.Enter(this)) LateUpdateBody(); }

        /// <summary>Split screen: sposta arma e mani sullo strato di Unity di questo giocatore (le camere degli altri non lo disegnano).</summary>
        public void SetViewLayer(int layer)
        {
            if (viewRoot == null) return;
            foreach (var t in viewRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        void LateUpdateBody()
        {
            if (cam == null || rig == null) return;
            aimProperties ??= new MaterialPropertyBlock();
            // Un solo asse per proiettile, punto collimato e immagine ingrandita. Il modello può oscillare intorno ad esso.
            aimProperties.SetVector("_AimOrigin", cam.position);
            aimProperties.SetVector("_AimForward", cam.forward);
            aimProperties.SetVector("_AimRight", cam.right);
            aimProperties.SetVector("_AimUp", cam.up);
            if (reticleRenderer != null) reticleRenderer.SetPropertyBlock(aimProperties);
            if (scopeCam == null) return;
            bool on = ads > 0.35f;
            if (scopeMat != null) scopeMat.SetFloat("_Active", Mathf.Clamp01((ads - 0.45f) * 3.5f));
            if (!on) return;
            // il campo della seconda camera: l'ingrandimento e' quello rispetto a come l'occhio vede la lente
            float dist = Mathf.Max(0.03f, Vector3.Distance(cam.position, rig.scopeLens.position));
            var scale = rig.scopeLens.lossyScale;
            float radius = scopeRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float alpha = 2f * Mathf.Atan(radius / dist);
            float fov = 2f * Mathf.Atan(Mathf.Tan(alpha * 0.5f) / Mathf.Max(1f, Def.zoom)) * Mathf.Rad2Deg;
            scopeCam.fieldOfView = Mathf.Clamp(fov, 1f, 40f);
            scopeCam.ResetProjectionMatrix(); // non riutilizzare l'eventuale proiezione jitterata copiata dalla camera principale
            scopeCam.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            aimProperties.SetFloat("_AimScale", dist / Mathf.Max(0.0001f, radius));
            scopeRenderer.SetPropertyBlock(aimProperties);
            scopeCam.Render();
        }

        // ------------------------------------------------------------------ ciclo
        float LookMult => Mathf.Lerp(1f, Def.zoom > 1.4f ? Mathf.Clamp(1.1f / Mathf.Pow(Def.zoom, 0.78f), 0.2f, 1f) : 0.62f, ads);
        public float SensMult => LookMult;

        public void Tick(float dt, bool sprinting, float speed = 0f, bool grounded = true, bool crouched = false, Vector2 lookDelta = default)
        {
            if (cam == null || viewRoot == null) return;
            bool inputEnabled = !GameMenu.Blocked;
            var d = Def;
            sprintHeld = sprinting;
            sprintBlend = Mathf.MoveTowards(sprintBlend, sprinting ? 1f : 0f, dt / (sprinting ? 0.20f : 0.18f));
            // coltello: l'arma scende in 80 ms e sparisce; rientrando risale in 140 ms
            knifeBlend = Mathf.MoveTowards(knifeBlend, knifeOut ? 1f : 0f, dt / (knifeOut ? 0.08f : 0.14f));
            bool shown = knifeBlend < 0.999f;
            if (viewRoot.gameObject.activeSelf != shown) viewRoot.gameObject.SetActive(shown);

            // ----- cambio arma
            if (swapT > 0f)
            {
                swapT -= dt;
                if (pendingSlot >= 0 && swapT <= 0f)
                {
                    cur = pendingSlot;
                    pendingSlot = -1;
                    ResetActionState();
                    BuildViewmodel();
                    equipT = Def.drawTime;
                    RefreshHud();
                    Synth.Mech("draw", 0.6f, 1f);
                }
            }
            else if (inputEnabled && !GestureBusy)
            {
                if (SeatInput.GetKeyDown(KeyCode.Alpha1)) SwitchSlot(0);
                else if (SeatInput.GetKeyDown(KeyCode.Alpha2)) SwitchSlot(1);
                else if ((SeatInput.GetKeyDown(KeyCode.X) || TouchControls.Down(TouchControls.Action.Swap))) SwitchSlot(1 - cur);
                float wheel = SeatInput.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(wheel) > 0.01f) SwitchSlot(1 - cur);
            }
            d = Def; // il cambio può essersi concluso in questo stesso fotogramma
            // ispezione dell'arma: I a riposo; sparare, mirare, ricaricare, cambiare o il coltello la interrompono
            if (inputEnabled && SeatInput.GetKeyDown(KeyCode.I) && inspectT <= 0f && !reloading && ads < 0.1f && swapT <= 0f && !knifeOut && !OffBusy) Inspect();
            if (OffBusy) inspectT = 0f;
            AutoReloadHolstered(dt);
            if (inspectT > 0f && (triggerHeld || ads > 0.2f || reloading || swapT > 0f || knifeOut)) inspectT = 0f;
            if (inputEnabled && (SeatInput.GetKeyDown(KeyCode.B) || TouchControls.Down(TouchControls.Action.Mode))) ToggleMode();

            // ----- mira
            bool adsBlocked = sprinting || swapT > 0f || knifeOut || OffBusy || (reloading && Def.style != ReloadStyle.Tube);
            bool adsKey = SeatInput.GetMouseButton(1);
            if (GameSettings.AimToggle)       // Impostazioni > Comandi: la mira a interruttore; corsa, cambio, coltello e ricarica la abbassano e non riprende da sola
            {
                if (!inputEnabled || adsBlocked) adsToggled = false;
                else if (SeatInput.GetMouseButtonDown(1)) adsToggled = !adsToggled;
                adsKey = adsToggled;
            }
            else adsToggled = false;
            bool adsWant = inputEnabled && (adsKey || TouchControls.Held(TouchControls.Action.Aim) || debugAds) && !adsBlocked;
            adsRaw = Mathf.MoveTowards(adsRaw, adsWant ? 1f : 0f, dt * OwnerMagic.HandlingMult / Mathf.Max(0.05f, d.adsTime));
            ads = adsRaw * adsRaw * (3f - 2f * adsRaw);

            // ----- dispersione: cala da sola; il pavimento dipende dal movimento
            bloom = Mathf.Max(0f, bloom - d.bloomRecover * dt * (1f + ads));
            streakT -= dt;
            if (streakT <= 0f) streak = 0;

            // ----- ricarica
            if (reloading) TickReload(dt, d);
            if (inputEnabled && (SeatInput.GetKeyDown(KeyCode.R) || TouchControls.Down(TouchControls.Action.Reload)) && swapT <= 0f && !knifeOut && !OffBusy) Reload();

            // ----- fuoco
            // Conserva il ritardo del fotogramma quando parte il colpo, senza accumulare credito durante l'attesa.
            State.AdvanceCadence(dt);
            bool autoBot = inputEnabled && GameManager.autoTest && !GameManager.gunTest && !GameManager.noFire;
            bool press = inputEnabled && (SeatInput.GetMouseButtonDown(0) || TouchControls.Down(TouchControls.Action.Fire) || (autoBot && Time.time - lastShot > 0.2f));
            bool held = inputEnabled && (SeatInput.GetMouseButton(0) || TouchControls.Held(TouchControls.Action.Fire) || autoBot);
            if (!held) { triggerHeld = false; dryPlayed = false; }
            bool blocked = knifeOut || OffBusy || swapT > 0f || sprintBlend > 0.35f || equipT > d.drawTime * 0.6f || (reloading && d.style != ReloadStyle.Tube);
            if (reloading && d.style == ReloadStyle.Tube && held && mag > 0 && (press || State.RoundsLoaded > 0))
            {
                // sparare interrompe la ricarica a canna
                CancelReload();
            }
            if (!blocked && !reloading)
            {
                var m = Mode;
                if (burstLeft > 0 && fireT <= 0f)
                {
                    if (mag > 0) { Shoot(d); burstLeft--; if (burstLeft == 0) State.DelayAfterBurst(d.burstGap / RateK); }
                    else burstLeft = 0;
                }
                else if (fireT <= 0f && cycleT <= 0f)
                {
                    bool want = false;
                    switch (m)
                    {
                        case FireMode.Auto: want = held; break;
                        case FireMode.Burst: want = press; break;
                        default: want = press; break;   // semi, pompa, otturatore, leva, doppietta
                    }
                    if (m == FireMode.Auto && held && !triggerHeld) triggerHeld = true;
                    if (want)
                    {
                        if (mag > 0)
                        {
                            Shoot(d);
                            if (m == FireMode.Burst) burstLeft = d.burst - 1;
                        }
                        else if (!dryPlayed)
                        {
                            dryPlayed = true;
                            Synth.Mech("dry", 0.55f, 1f);
                            Reload();
                        }
                    }
                }
            }
            else if (!reloading && mag <= 0 && press && swapT <= 0f && !dryPlayed) { }

            // fiammata
            if (flashA != null && flashA.activeSelf && Time.time - lastShot > 0.05f)
            {
                flashA.SetActive(false); flashB.SetActive(false);
                flashLight.intensity = 0f;
            }
            if (cycleAnim >= 0f)
            {
                cycleAnim += dt / Mathf.Max(0.05f, cycleLen);
                if (cycleAnim >= 1f) cycleAnim = -1f;
            }

            // ----- animazioni di rinculo e dondolio
            kickBack = Mathf.Max(0f, kickBack - dt * (6.5f + d.kick * 1.5f));
            kickPitch *= Mathf.Max(0f, 1f - 11f * dt);
            kickRoll *= Mathf.Max(0f, 1f - 9f * dt);
            recoil = kickBack;
            // inerzia: il modello resta un poco indietro rispetto al movimento del mouse
            swayVx += (-lookDelta.x * 0.045f - swayX) * 90f * dt; swayVx *= Mathf.Max(0f, 1f - 11f * dt);
            swayVy += (-lookDelta.y * 0.045f - swayY) * 90f * dt; swayVy *= Mathf.Max(0f, 1f - 11f * dt);
            swayX = Mathf.Clamp(swayX + swayVx * dt, -2.6f, 2.6f);
            swayY = Mathf.Clamp(swayY + swayVy * dt, -2.6f, 2.6f);
            breath += dt;

            // ----- mirino a schermo (solo senza ottica: con il mirino fisico in mira sparisce)
            if (hud != null)
            {
                float spread = CurrentSpread(speed, grounded, crouched);
                float fov = cam.GetComponent<Camera>().fieldOfView;
                float px = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 360f;
                hud.SetCrosshair(px, ads, d.cls, d.pellets > 1);
                hud.SetAdsVignette(ads * (d.zoom > 1.4f ? 0.80f : 0.42f));
            }

            UpdatePose(dt, speed, sprinting, grounded);
            lastSpread = CurrentSpread(speed, grounded, crouched);
        }

        float streakT;
        float lastSpread;
        float moveNow, airNow, crouchNow;

        /// <summary>Semiapertura corrente del cono di dispersione (gradi).</summary>
        float CurrentSpread(float speed, bool grounded, bool crouched)
        {
            var d = Def;
            moveNow = Mathf.Clamp01(speed / 5.6f); airNow = grounded ? 0f : 1f; crouchNow = crouched ? 1f : 0f;
            float baseDeg = Mathf.Lerp(d.hip, d.ads, ads);
            float mv = 1f + d.moveSpread * moveNow * (1f - ads * 0.6f);
            float c = crouched ? 0.72f : 1f;
            float air = 1f + airNow * 1.6f;
            return (baseDeg * mv * c * air + bloom * Mathf.Lerp(1f, 0.5f, ads)) * OwnerMagic.SpreadMult;
        }

        // ------------------------------------------------------------------ ricarica
        public void Reload()
        {
            var d = Def;
            if (reloading || swapT > 0f || knifeOut || OffBusy) return;
            var timing = d.style == ReloadStyle.Tube ? ReloadTiming.PerRound(d.shellTime, .35f, .25f, .55f) :
                ReloadTiming.Magazine(d.reload, d.reloadEmpty);
            if (!State.BeginReload(magSize, timing)) return;
            burstLeft = 0;
            hud?.SetReloading(true);
            // con la presa realistica la ricarica la fa la mano sinistra (ReloadAnims): lo sgancio suona quando sfila il caricatore
            bool hands = StartReloadAnim(d, timing);
            if (!hands || d.style == ReloadStyle.Tube || d.style == ReloadStyle.Revolver || d.style == ReloadStyle.Break)
                Synth.Mech(d.style == ReloadStyle.Tube ? "shellopen" : d.style == ReloadStyle.Revolver ? "cylopen" : "magout", 0.55f, 1f);
        }

        void CancelReload()
        {
            EndReloadAnim();
            State.CancelReload();
            hud?.SetReloading(false);
            hud?.SetAmmo(mag, reserve);
        }

        void TickReload(float dt, WeaponDef d)
        {
            var step = State.AdvanceReload(dt * OwnerMagic.HandlingMult / OwnerPerks.ReloadTime);
            if (d.style == ReloadStyle.Tube)
            {
                for (int round = 0; round < step.RoundsInserted; round++)
                    Synth.Mech("shellin", 0.5f, Random.Range(0.95f, 1.1f));
            }
            else
            {
                if ((step.Events & ReloadEvents.MagazineInserted) != 0)
                    Synth.Mech(d.style == ReloadStyle.Revolver ? "cylshells" : d.style == ReloadStyle.Break ? "shellin" : "magin", 0.6f, 1f);
                if ((step.Events & ReloadEvents.ActionClosed) != 0)
                {
                    if (d.style == ReloadStyle.Revolver) Synth.Mech("cylclose", 0.6f, 1f);
                    else if (d.style == ReloadStyle.Break) Synth.Mech("breakclose", 0.6f, 1f);
                    else if (reloadEmpty) Synth.Mech(d.style == ReloadStyle.Pistol ? "slidefwd" : "boltfwd", 0.6f, 1f);
                }
            }
            if (step.RoundsInserted > 0 || (d.style != ReloadStyle.Tube && (step.Events & ReloadEvents.Completed) != 0))
                hud?.SetAmmo(mag, reserve);
            if ((step.Events & ReloadEvents.Completed) != 0)
            {
                hud?.SetReloading(false);
                if (d.style == ReloadStyle.Tube && d.cycle > 0f && reloadEmpty) StartCycle(d, false);
                // otturatore girevole: a vuoto la destra lo manovra dopo aver messo il caricatore (con la mano che ricarica)
                else if (reloadAnim && reloadEmpty && d.cycle > 0f && rig != null && rig.boltHandle != null && Mode == FireMode.Bolt) StartCycle(d, false);
            }
        }

        // ------------------------------------------------------------------ sparo
        static readonly RaycastHit[] hitBuf = new RaycastHit[24];

        void Shoot(WeaponDef d)
        {
            if (!State.ConsumeRound()) return;
            // Munizioni Fantasma (un colpo su otto torna nel caricatore) e Arma Maledetta (munizioni infinite)
            if (OwnerMagic.CursedOn(cur) || (OwnerMagic.GhostAmmo && Random.value < 0.125f)) mag = Mathf.Min(magSize, mag + 1);
            lastShot = Time.time;
            GameManager.shotsFired++;
            // i preti sentono lo sparo (e ne vedono il lampo): l'arma silenziata si sente da molto meno lontano
            Stealth.NoteShot(d.suppressed);
            Noise.Emit(cam.position, d.suppressed ? 11f : 42f, NoiseKind.Gunshot);
            var m = Mode;
            State.RecordShot((m == FireMode.Burst ? 60f / d.rpm : (d.cycle > 0f ? 0.08f : 60f / d.rpm)) / RateK);    // Doppio colpo +33%, Raffica Mortale +12%
            if (d.cycle > 0f && m != FireMode.Double) StartCycle(d, true);
            streak++; streakT = 0.28f;
            // rinculo della visuale: cresce un poco con la raffica, in mira e accovacciati e' piu' dolce
            float dampen = Mathf.Lerp(1f, 0.72f, ads) * (crouchNow > 0.5f ? 0.85f : 1f) * OwnerMagic.RecoilMult;     // Stabilita' Oscura: -30%
            float rv = d.recV * (1f + 0.035f * Mathf.Min(streak, 10)) * dampen;
            float rh = (Random.Range(-1f, 1f) * d.recH + Mathf.Sin(streak * 0.9f + d.id.Length) * d.recH * 0.45f) * dampen;
            if (player != null) player.Kick(rv, rh, d.recRecover);
            // contraccolpo del modello
            kickBack = Mathf.Min(1.2f, kickBack + 0.38f * d.kick * OwnerMagic.RecoilMult);
            kickPitch = Mathf.Min(14f, kickPitch + 1.6f * d.kick);
            kickRoll += Random.Range(-1f, 1f) * 1.2f * d.kick;
            bloom = Mathf.Min(d.bloomMax, bloom + d.bloomShot);

            // fiammata
            if (!d.suppressed)
            {
                float s = 0.13f * d.flash;
                flashA.SetActive(true); flashB.SetActive(true);
                flashA.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                flashA.transform.localScale = Vector3.one * Random.Range(0.82f, 1.15f) * s;
                // fiammata laterale: allungata lungo la canna
                flashB.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 0f, Random.Range(-8f, 8f));
                flashB.transform.localScale = new Vector3(s * 2.1f, s * 0.9f, 1f);
                // illuminamento = intensita' / distanza^2: davanti a un muro vicino si limita l'intensita' (come un'esposizione automatica)
                float wd = 3f;
                if (Physics.Raycast(cam.position, cam.forward, out RaycastHit fh, 3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) wd = fh.distance;
                float dm = Mathf.Max(0.2f, wd - (rig.MuzzleLocal.z + 0.4f));
                flashLight.intensity = Mathf.Min(1.6f * d.flash, 0.9f * dm * dm);
            }
            if (d.soundEvery <= 1 || streak % d.soundEvery == 1 || streak <= 1)
                Synth.Gun(d.snd, d.suppressed ? 0.35f : 0.62f, Random.Range(0.96f, 1.04f));
            if (d.fx == "flame" && rig.muzzle != null) Fx.Flame(rig.muzzle.position, cam.forward);
            SigilShot(d);       // il sigillo si vede anche alla bocca: lampo e fiammata del colore dell'elemento

            // bossolo
            if (d.casing != "none" && rig.eject != null && d.cycle <= 0f)
                EjectCasing(d);

            detonationShot = OwnerMagic.Detonation && Random.value < 0.0625f;
            FireRays(d);
            detonationShot = false;
            hud?.SetAmmo(mag, reserve);
            if (mag <= 0 && reserve > 0 && Mode != FireMode.Double) { /* la ricarica parte al prossimo grilletto */ }
        }

        void EjectCasing(WeaponDef d)
        {
            Vector3 pos = rig.eject.position;
            Vector3 vel = cam.right * Random.Range(1.4f, 2.4f) + cam.up * Random.Range(0.9f, 1.8f) - cam.forward * Random.Range(0.1f, 0.5f);
            Fx.Casing(pos, vel, cam.rotation * Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), 0f), d.casing);
        }

        void StartCycle(WeaponDef d, bool afterShot)
        {
            State.StartCycle((afterShot ? d.cycle : d.cycle * 0.7f) / RateK);
            cycleAnim = 0f; cycleLen = (afterShot ? d.cycle : d.cycle * 0.7f) / RateK;
            cycleDelayAfterShot = afterShot;
            cycleCasingDone = false;
            Synth.Mech(Mode == FireMode.Pump ? "pumpback" : Mode == FireMode.Lever ? "leverdown" : "boltup", 0.55f, 1f);
            cycleSound = 0;
        }
        bool cycleDelayAfterShot, cycleCasingDone;
        int cycleSound;

        void FireRays(WeaponDef d)
        {
            float spread = lastSpread;
            // la prima pallottola dopo una pausa, in mira e fermi, e' precisa (fucili di precisione)
            if (Time.time - lastShotPrev > 0.4f && ads > 0.8f && moveNow < 0.1f) spread *= 0.35f;
            lastShotPrev = Time.time;
            // col sigillo ogni colpo ha la sua traccia (SigilFx): il proiettile dice qual e' l'elemento
            bool tracer = d.tracerEvery <= 1f || (streak % Mathf.Max(1, Mathf.RoundToInt(d.tracerEvery))) == 0 || SigilElement() >= 0;
            Vector3 origin = cam.position;
            Vector3 muzzlePos = rig.muzzle != null ? rig.muzzle.position : origin + cam.forward * 0.6f;
            if (ThirdPersonView.MuzzleOf(player, out var bodyMuzzle)) muzzlePos = bodyMuzzle;      // terza persona: la traccia esce dall'arma del corpo che si vede
            var hitSet = new HashSet<Enemy>();
            for (int p = 0; p < d.pellets; p++)
            {
                Vector3 dir = Spread(cam.forward, spread);
                if (d.pellets > 1) dir = Spread(dir, d.cone * Mathf.Lerp(1f, 0.7f, ads) * (p == 0 && d.pellets > 4 ? 0.2f : 1f));
                ShootRay(d, origin, dir, hitSet, tracer && (d.pellets == 1 || p < d.tracerPellets), muzzlePos);
            }
        }
        float lastShotPrev = -99f;

        static Vector3 Spread(Vector3 fwd, float deg)
        {
            if (deg <= 0.0001f) return fwd;
            Vector2 c = Random.insideUnitCircle * Mathf.Tan(deg * Mathf.Deg2Rad);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 up = Vector3.Cross(fwd, right);
            return (fwd + right * c.x + up * c.y).normalized;
        }

        void ShootRay(WeaponDef d, Vector3 origin, Vector3 dir, HashSet<Enemy> hitSet, bool tracer, Vector3 muzzlePos)
        {
            int n = Physics.RaycastNonAlloc(origin, dir, hitBuf, d.rangeFar * 2f + 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // ordino per distanza
            for (int i = 1; i < n; i++)
            {
                var h = hitBuf[i]; int j = i - 1;
                while (j >= 0 && hitBuf[j].distance > h.distance) { hitBuf[j + 1] = hitBuf[j]; j--; }
                hitBuf[j + 1] = h;
            }
            float dmgMul = 1f;
            int pierced = 0;
            Vector3 end = origin + dir * Mathf.Min(d.rangeFar * 2f, 250f);
            Vector3 endN = -dir;                 // dove e come finisce il colpo (per l'impatto del sigillo)
            bool endEnemy = false, hitAny = false;
            var seen = new HashSet<Enemy>();
            for (int i = 0; i < n; i++)
            {
                var h = hitBuf[i];
                Enemy e = h.collider.GetComponentInParent<Enemy>();
                if (e == null)
                {
                    end = h.point; endN = h.normal; hitAny = true;
                    var shootable = h.collider.GetComponentInParent<IShootable>();     // botti d'olio santo
                    if (shootable != null) shootable.Shot(d.damage * dmgMul, h.point, dir);
                    else Fx.Impact(h, d.pellets > 1 ? 0.6f : 1f, dir);         // polvere, schegge, foro e suono della superficie (Surfaces)
                    break;
                }
                if (!seen.Add(e)) continue;
                bool head = h.collider.CompareTag("Head");
                if (!head)
                {
                    // la capsula del corpo copre l'occhio di un soffio (monaco: l'occhio sta a 20 cm dall'asse, la capsula ne ha 30):
                    // se il proiettile attraversa anche la sfera della testa dello stesso nemico, e' un colpo in testa
                    for (int k = i + 1; k < n && hitBuf[k].distance - h.distance < 1.2f; k++)
                    {
                        var ek = hitBuf[k].collider.GetComponentInParent<Enemy>();
                        if (ek == null) break;                                  // un muro: oltre non si conta
                        if (ek == e && hitBuf[k].collider.CompareTag("Head")) { head = true; break; }
                    }
                }
                float dist = h.distance;
                float fall = dist <= d.rangeNear ? 1f : Mathf.Lerp(1f, d.farMult, Mathf.Clamp01((dist - d.rangeNear) / Mathf.Max(0.1f, d.rangeFar - d.rangeNear)));
                float dmg = d.damage * fall * dmgMul * (head ? d.headMult * OwnerPerks.HeadMult : 1f) * (slots[cur].upg ? UpgDamage : 1f);
                if (Cattedrale.Catacombe.Attive) dmg *= Cattedrale.Fabbrica.DannoMul * (e == Enemy.Boss ? Cattedrale.Fabbrica.DannoBossMul : 1f);   // la Forgia, la Furia, la vetrata del Cardinale
                dmg = SigilDamage(e, dmg, h.point);
                bool first = hitSet.Add(e);
                Fx.Blood(h.point, dir, e.BloodTint, head);
                e.Damage(dmg, h.point, head, first);
                SigilHit(e, dmg, h.point, d);
                end = h.point; endN = -dir; endEnemy = true; hitAny = true;
                if (pierced >= d.pierce + OwnerMagic.ExtraPierce) break;
                pierced++;
                dmgMul *= d.pierceMult;
            }
            if (detonationShot && (end - origin).sqrMagnitude < 150f * 150f) { Detonate(end, d.damage); detonationShot = false; }
            int sig = SigilElement();
            if (sig >= 0 && hitAny) SigilFx.Impact(sig, end, endN, endEnemy, d.pellets > 1);
            if (tracer)
            {
                if (sig >= 0 && !d.beam) SigilFx.Tracer(sig, muzzlePos, end, d.tracerWidth, d.pellets > 1);
                else if (d.beam) Fx.Beam(muzzlePos, end, d.tracerWidth, sig >= 0 ? SigilFx.TracerColor(sig) : d.tracerColor);
                else Fx.Tracer(muzzlePos, end, 300f + d.damage * 0.5f, d.tracerWidth, d.tracerColor);
            }
        }

        // ------------------------------------------------------------------ posa del modello
        static float Band(float v, float a, float b) => Mathf.Clamp01((v - a) / Mathf.Max(0.0001f, b - a));
        static float Ease(float t) => t * t * (3f - 2f * t);

        void UpdatePose(float dt, float speed, bool sprinting, bool grounded)
        {
            if (viewRoot == null || rig == null) return;
            var d = Def;
            Vector3 hip = HipOf(d);
            Vector3 a = rig.AdsLocal;
            Vector3 adsP = new Vector3(0f, 0f, d.adsDist) - a;
            Vector3 pos = Vector3.Lerp(hip, adsP, ads);
            Quaternion rot = Quaternion.Euler(0f, hipYaw * (1f - ads), 0f);
            if (handRHideAds && handRRend != null)
            {
                bool seen = ads < 0.55f;
                if (seen != handRVis) { handRVis = seen; foreach (var r in handRRend) if (r != null) r.enabled = seen; }
            }
            // in mira si guardano il calcio e il dorso dell'arma di striscio: con la luce di riempimento sopra il polimero riflette (Fresnel) e sembra bianco
            if (fill != null) fill.intensity = Mathf.Lerp(0.55f, 0.15f, ads);

            // respiro
            float calm = (1f - ads * 0.7f) * GameSettings.Motion;
            pos.x += Mathf.Sin(Time.time * 1.7f) * 0.0030f * calm;
            pos.y += Mathf.Sin(Time.time * 2.3f) * 0.0024f * calm;
            // respiro del tiratore con ottica forte: oscillazione lenta del modello (si ferma tenendo il fiato: Shift)
            if (ads > 0.5f && d.zoom >= 3.9f)
            {
                float hold = !GameMenu.Blocked && (SeatInput.GetKey(KeyCode.LeftShift) || TouchControls.Held(TouchControls.Action.Sprint)) ? 0.12f : 1f;
                pos.x += Mathf.Sin(breath * 1.1f) * 0.0012f * hold * ads;
                pos.y += Mathf.Sin(breath * 1.7f + 1f) * 0.0016f * hold * ads;
            }

            // passo
            float bob = Mathf.Clamp01(speed / 5.5f) * (1f - ads * 0.82f) * GameSettings.Motion;
            pos.y += Mathf.Sin(Time.time * 8.6f) * 0.0075f * bob;
            pos.x += Mathf.Cos(Time.time * 4.3f) * 0.0065f * bob;

            // il corpo che porta le spalle (braccia vere): il petto che respira alza le spalle, camminando le spalle si alternano
            // avanti e indietro col passo (il cingolo scapolare gira contro il bacino), il contraccolpo spinge indietro la spalla destra
            // e la sinistra lo segue appena. Si muovono le spalle e l'arma: gomiti e polsi vivono di quello che c'e' in mezzo
            {
                float tt = Time.time;
                Vector3 common = new Vector3(0f, Mathf.Sin(tt * 2.3f) * 0.0022f * calm - Mathf.Abs(Mathf.Sin(tt * 4.3f)) * 0.0045f * bob,
                                             -Mathf.Sin(tt * 2.3f) * 0.0010f * calm);
                float swing = Mathf.Sin(tt * 4.3f) * 0.007f * bob;
                float kick = kickBack * d.kick;
                bodyR = common + new Vector3(0f, 0.004f * kick, swing - 0.022f * kick * (1f - ads * 0.5f));
                bodyL = common + new Vector3(0f, 0.003f * kick, -swing - 0.008f * kick);
            }

            // corsa: arma abbassata e ruotata
            if (sprintBlend > 0.001f)
            {
                float k = Ease(sprintBlend);
                pos += new Vector3(0.045f, -0.065f, -0.03f) * k;
                rot *= Quaternion.Euler(-10f * k, 20f * k, 13f * k);
            }

            // contraccolpo
            pos.z -= kickBack * 0.045f * d.kick * (1f - ads * 0.45f);
            pos.y += kickBack * 0.012f * d.kick;
            rot *= Quaternion.Euler(-kickPitch * (1f - ads * 0.55f), 0f, kickRoll * (1f - ads * 0.6f));

            // inerzia del mouse
            rot *= Quaternion.Euler(swayY * (1f - ads * 0.6f), swayX * (1f - ads * 0.6f), swayX * 0.4f * (1f - ads));
            pos.x += swayX * 0.0035f * (1f - ads * 0.7f);
            pos.y += swayY * 0.0035f * (1f - ads * 0.7f);

            // muro davanti: l'arma si ritrae e si alza invece di entrarci
            float len = 0.95f;
            if (Physics.SphereCast(cam.position, 0.06f, cam.forward, out RaycastHit wh, len, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
            {
                float push = Mathf.Clamp01((len - wh.distance) / len);
                pos.z -= push * 0.30f; pos.y -= push * 0.03f;
                rot *= Quaternion.Euler(-push * 28f, push * 8f, 0f);
            }

            // ricarica / cambio / ingresso
            float rp = -1f;
            Vector3 tiltPos = Vector3.zero; Vector3 tiltRot = Vector3.zero;
            if (reloading) rp = State.ReloadProgress;
            if (reloading && !reloadAnim)
            {
                float r = rp;
                float tilt = Mathf.Sin(Mathf.Clamp01(r / 0.92f) * Mathf.PI);
                if (d.style == ReloadStyle.Tube) tilt = Mathf.Clamp01(Band(r, 0.0f, 0.12f) - Band(r, 0.88f, 1f));
                tiltPos = new Vector3(-0.035f, -0.055f, -0.02f) * tilt;
                tiltRot = new Vector3(-15f * tilt, 10f * tilt, 22f * tilt);
                if (d.style == ReloadStyle.Tube) { tiltPos = new Vector3(-0.06f, -0.03f, -0.02f) * tilt; tiltRot = new Vector3(-8f * tilt, 30f * tilt, 38f * tilt); }
                if (d.style == ReloadStyle.Break) { tiltPos = new Vector3(-0.02f, -0.05f, 0f) * tilt; tiltRot = new Vector3(-6f * tilt, 8f * tilt, 18f * tilt); }
            }
            pos += tiltPos; rot *= Quaternion.Euler(tiltRot);
            float e = Mathf.Clamp01(equipT / Mathf.Max(0.05f, d.drawTime));
            e = e * e;
            pos += new Vector3(0.03f, -0.11f, -0.04f) * e;
            rot *= Quaternion.Euler(24f * e, 0f, 7f * e);
            equipT = Mathf.Max(0f, equipT - dt);
            if (swapT > 0f && pendingSlot >= 0)
            {
                float s = 1f - Mathf.Clamp01(swapT / pendingSlotTime);
                pos += new Vector3(0.03f, -0.14f, -0.05f) * s;
                rot *= Quaternion.Euler(28f * s, 0f, 9f * s);
            }

            if (knifeBlend > 0f)
            {
                float kb = Ease(knifeBlend);
                pos += new Vector3(0.03f, -0.16f, -0.05f) * kb;
                rot *= Quaternion.Euler(30f * kb, 0f, 10f * kb);
            }

            if (inspectT > 0f)
            {
                // sale e ruota a mostrare il fianco sinistro, resta con un'oscillazione lenta, torna
                float u = 1f - inspectT / InspectTime;
                float ik = u < 0.22f ? Ease(u / 0.22f) : u > 0.78f ? Ease((1f - u) / 0.22f) : 1f;
                float wob = Mathf.Sin(u * Mathf.PI * 3f) * 0.25f * ik;
                pos += new Vector3(-0.10f, 0.07f, -0.07f) * ik;
                rot *= Quaternion.Euler((-10f + wob * 8f) * ik, (52f + wob * 10f) * ik, (-34f + wob * 12f) * ik);
                inspectT = Mathf.Max(0f, inspectT - dt);
            }
            if (throwT > 0f)
            {
                float tk = Mathf.Sin(Mathf.Clamp01(1f - throwT / 0.5f) * Mathf.PI);
                pos += new Vector3(0.02f, -0.13f, -0.05f) * tk;
                rot *= Quaternion.Euler(26f * tk, -6f * tk, 9f * tk);
                throwT -= dt;
            }
            if (OffBusy)
            {
                // la seconda mano lavora: l'arma la regge la destra da sola (abbassata) o viene offerta di lato alla sinistra
                offHand.WeaponOffset(out Vector3 op, out Vector3 orr);
                pos += op;
                rot *= Quaternion.Euler(orr);
            }
            viewRoot.localPosition = pos;
            viewRoot.localRotation = rot;
            AnimateParts(rp, d);
            if (holdStance > 0f)
            {
                // presa realistica: mentre la sinistra lavora libera (magie, sigilli, lanci) il busto torna frontale e la spalla sinistra
                // torna dove i gesti la aspettano; le spalle girano insieme
                float fb = armL != null ? Mathf.Clamp01(armL.Free) : 0f;
                float st = holdStance * (1f - fb);
                if (armR != null) armR.Stance = st;
                if (armL != null) armL.Stance = st;
            }
            if (armR != null) armR.BodyMotion = bodyR;
            if (armL != null) armL.BodyMotion = bodyL;
            WorkHandTick();
            armR?.Solve(ads, dt);                // con la molla: in mira il gomito destro si apre di lato senza scatti
            gripHand?.AfterArmSolve();
            if (trigFinger != null || gripHand != null)
            {
                float sinceShot = Time.time - lastShot;
                bool wantTrigger = (triggerHeld || sinceShot < 0.3f || ads > 0.35f) && !reloading && swapT <= 0f;
                trigBlend = Mathf.MoveTowards(trigBlend, wantTrigger ? 1f : 0f, dt * (wantTrigger ? 10f : 3.5f));
                float squeeze = sinceShot < 0.09f ? 1f - sinceShot / 0.09f : 0f;
                if (gripHand != null) gripHand.Pose(trigBlend, squeeze, Mathf.Clamp01(kickBack));
                else trigFinger.Pose(trigBlend, squeeze);
            }
            WorkHandFingers();
            offHand?.Idle(dt, Mathf.Clamp01(kickBack));
            ReloadSeek();
            offHand?.Tick(dt);
            armL?.Solve(ads, dt);
            offHand?.AfterArmSolve();
            ReloadPostSolve();
        }

        void AnimateParts(float rp, WeaponDef d)
        {
            rig.Reset();
            float since = Time.time - lastShot;
            // ---- carrello / otturatore che arretra a ogni colpo (visibile)
            float cyc = Mathf.Exp(-since * 28f) * (since < 0.12f ? 1f : 0f);
            float slideBack = Mathf.Clamp01(1f - since / Mathf.Min(0.07f, 60f / d.rpm * 0.7f));
            if (since < 0.1f)
            {
                if (rig.slide != null) rig.Offset(rig.slide, Vector3.back * (0.034f * slideBack));
                if (rig.recip != null) rig.Offset(rig.recip, Vector3.back * (0.040f * slideBack));
                if (rig.hammer != null) rig.Rotate(rig.hammer, new Vector3(-35f * slideBack, 0f, 0f));
                if (rig.trigger != null) rig.Offset(rig.trigger, Vector3.back * (0.004f * slideBack));
            }
            // slide fermo indietro con l'arma scarica (pistole)
            if (rig.slide != null && mag <= 0 && !reloading && d.style == ReloadStyle.Pistol) rig.Offset(rig.slide, Vector3.back * 0.034f);

            // ---- cicli manuali dopo lo sparo
            if (cycleAnim >= 0f) AnimateCycle(d, cycleAnim);

            // ---- ricarica
            if (rp < 0f) MagVisibleAgain();                   // ricarica finita o interrotta: il caricatore torna visibile
            if (rp >= 0f)
            {
                bool tube = d.style == ReloadStyle.Tube;
                if (rig.mag != null && !tube && d.style != ReloadStyle.Revolver && d.style != ReloadStyle.Break && !reloadAnim)
                {
                    // fuori (0,12-0,28), la mano alla cintura e di ritorno col nuovo (0,28-0,50), dentro (0,52-0,68); il vuoto cade
                    // nella ricarica a vuoto (Weapon.MagDrop.cs)
                    float down = Mathf.Clamp01(Band(rp, 0.12f, 0.28f) - Band(rp, 0.52f, 0.68f));
                    float belt = Mathf.Clamp01(Band(rp, 0.28f, 0.36f) - Band(rp, 0.40f, 0.50f));
                    Vector3 magAxis = d.magUp ? Vector3.up : Vector3.down;
                    rig.Offset(rig.mag, magAxis * (0.24f * down + 0.22f * belt) + Vector3.back * (0.07f * belt) + Vector3.left * (0.05f * belt));
                    rig.Rotate(rig.mag, new Vector3(18f * down + 22f * belt, 0f, 0f));
                    MagDropTick(rp, d);
                }
                if (rig.charge != null)
                {
                    // con la mano: solo a vuoto, tirata da ChargeHook a ChargeBack e lasciata a ChargeGo (torna avanti di scatto)
                    float pull = reloadAnim
                        ? (reloadEmpty ? Mathf.Clamp01(Band(rp, ReloadAnims.ChargeHook, ReloadAnims.ChargeBack) - Band(rp, ReloadAnims.ChargeGo - 0.004f, ReloadAnims.ChargeGo + 0.022f)) : 0f)
                        : Mathf.Clamp01(Band(rp, 0.78f, 0.84f) - Band(rp, 0.88f, 0.94f));
                    rig.Offset(rig.charge, Vector3.back * (0.045f * pull));
                }
                if (rig.slide != null && d.style == ReloadStyle.Pistol && reloadEmpty)
                {
                    // con la mano: fermo indietro, la mano lo arretra ancora un poco e lo lascia
                    float held = reloadAnim
                        ? (rp < ReloadAnims.SlideGo ? 1f : 1f - Band(rp, ReloadAnims.SlideGo, ReloadAnims.SlideGo + 0.02f))
                        : 1f - Band(rp, 0.78f, 0.86f);
                    rig.Offset(rig.slide, Vector3.back * (0.034f * held));
                }
                if (rig.bolt != null && reloadEmpty && d.style == ReloadStyle.Mag && rig.boltHandle != null && !reloadAnim)
                {
                    float pull = Mathf.Clamp01(Band(rp, 0.74f, 0.80f) - Band(rp, 0.86f, 0.92f));
                    rig.Offset(rig.bolt, Vector3.back * (0.06f * pull));
                }
                if (rig.cyl != null && d.style == ReloadStyle.Revolver)
                {
                    float open = reloadAnim ? Mathf.Clamp01(Band(rp, ReloadAnims.CylOpen0, ReloadAnims.CylOpen1) - Band(rp, ReloadAnims.CylClose0, ReloadAnims.CylClose1))
                                            : Mathf.Clamp01(Band(rp, 0.10f, 0.22f) - Band(rp, 0.78f, 0.90f));
                    rig.Rotate(rig.cyl, new Vector3(0f, 0f, -78f * open));
                }
                if (rig.barrels != null && d.style == ReloadStyle.Break)
                {
                    float open = reloadAnim ? Mathf.Clamp01(Band(rp, ReloadAnims.BreakOpen0, ReloadAnims.BreakOpen1) - Band(rp, ReloadAnims.BreakClose0, ReloadAnims.BreakClose1))
                                            : Mathf.Clamp01(Band(rp, 0.10f, 0.24f) - Band(rp, 0.76f, 0.90f));
                    rig.Rotate(rig.barrels, new Vector3(32f * open, 0f, 0f));
                    // con la mano: attorno alla cerniera (sotto il retro delle canne), non attorno al perno del pezzo
                    if (reloadAnim && hasBreakHinge && open > 0f)
                    {
                        rig.Rotate(rig.barrels, Vector3.zero);
                        rig.barrels.RotateAround(viewRoot.TransformPoint(breakHinge), viewRoot.right, 32f * open);
                    }
                }
                if (rig.cover != null && d.style == ReloadStyle.Belt)
                {
                    float open = reloadAnim ? Mathf.Clamp01(Band(rp, ReloadAnims.CoverOpen0, ReloadAnims.CoverOpen1) - Band(rp, ReloadAnims.CoverClose0, ReloadAnims.CoverClose1))
                                            : Mathf.Clamp01(Band(rp, 0.06f, 0.18f) - Band(rp, 0.84f, 0.94f));
                    rig.Rotate(rig.cover, new Vector3(-75f * open, 0f, 0f));
                }
            }
            // ---- mano sinistra (durante un gesto la muove OffHand: qui solo la manica, che deve vedersi anche con le pistole)
            if (handL != null && OffBusy)
            {
                if (armL != null && armL.Skin != null) armL.Skin.enabled = true;
            }
            else if (handL != null)
            {
                bool vis = d.hand > 0.5f || (rp >= 0f && rp > 0.04f && rp < 0.96f);
                if (handLRend != null) foreach (var r in handLRend) if (r != null) r.enabled = vis;
                if (wrappedL != null && offHand != null && offHand.NaturalRest) foreach (var r in wrappedL) if (r != null && r is MeshRenderer) r.enabled = false;
                if (d.hand > 0.5f)
                {
                    Vector3 target = handLRest;
                    if (rp >= 0f)
                    {
                        if (handLOnMag && rig.mag != null && d.style != ReloadStyle.Tube)
                        {
                            float k = Band(rp, 0.04f, 0.14f) - Band(rp, 0.56f, 0.72f);
                            Vector3 grab = viewRoot.InverseTransformPoint(rig.mag.position) + new Vector3(0f, -0.07f, 0.02f);
                            target = Vector3.Lerp(handLRest, grab, Mathf.Clamp01(k));
                        }
                        else if (d.style == ReloadStyle.Tube || d.style == ReloadStyle.Break || d.style == ReloadStyle.Revolver)
                        {
                            float k = Band(rp, 0.04f, 0.14f) - Band(rp, 0.84f, 0.96f);
                            target = Vector3.Lerp(handLRest, handLRest + new Vector3(-0.02f, -0.10f, -0.12f), Mathf.Clamp01(k) * (0.6f + 0.4f * Mathf.Sin(reloadT * 9f) * 0.5f + 0.2f));
                        }
                    }
                    handL.position = viewRoot.TransformPoint(target);
                    if (rig.pump != null && cycleAnim >= 0f && (Mode == FireMode.Pump))
                    {
                        float travel = PumpTravel(cycleAnim);
                        handL.position += viewRoot.TransformVector(Vector3.back * (0.07f * travel));
                    }
                }
                else if (rp >= 0f)
                {
                    // armi a una mano: la sinistra viene sotto per il caricatore
                    float k = Band(rp, 0.04f, 0.16f) - Band(rp, 0.62f, 0.78f);
                    Vector3 grab = rig.mag != null ? viewRoot.InverseTransformPoint(rig.mag.position) + new Vector3(0f, -0.07f, 0.02f) : handLRest;
                    handL.position = viewRoot.TransformPoint(Vector3.Lerp(handLRest + new Vector3(-0.06f, -0.12f, -0.05f), grab, Mathf.Clamp01(k)));
                }
            }
        }

        static float PumpTravel(float c) => c < 0.5f ? Ease(c / 0.5f) : 1f - Ease((c - 0.5f) / 0.5f);

        void AnimateCycle(WeaponDef d, float c)
        {
            var m = Mode;
            if (m == FireMode.Pump && rig.pump != null)
            {
                float back = PumpTravel(Band(c, 0.08f, 0.92f));
                rig.Offset(rig.pump, Vector3.back * (0.065f * back));
                if (c > 0.38f && cycleSound == 0) { cycleSound = 1; Synth.Mech("pumpfwd", 0.55f, 1f); EjectCycleCasing(d); }
            }
            else if (m == FireMode.Lever && rig.lever != null)
            {
                float down = PumpTravel(Band(c, 0.05f, 0.95f));
                rig.Rotate(rig.lever, new Vector3(-55f * down, 0f, 0f));
                if (rig.bolt != null) rig.Offset(rig.bolt, Vector3.back * (0.045f * down));
                if (c > 0.4f && cycleSound == 0) { cycleSound = 1; Synth.Mech("leverup", 0.55f, 1f); EjectCycleCasing(d); }
            }
            else if (m == FireMode.Bolt && rig.bolt != null)
            {
                // alza la maniglia, tira indietro, spinge avanti, riabbassa (con la destra che la manovra: prima la mano ci arriva)
                bool hand = BoltByHand;
                float up = hand ? Band(c, BoltUp0, BoltUp1) - Band(c, BoltDown0, BoltDown1) : Band(c, 0.08f, 0.22f) - Band(c, 0.82f, 0.94f);
                float back = hand ? Band(c, BoltBack0, BoltBack1) - Band(c, BoltFwd0, BoltFwd1) : Band(c, 0.22f, 0.50f) - Band(c, 0.56f, 0.80f);
                rig.Offset(rig.bolt, Vector3.back * (0.075f * back));
                if (rig.boltHandle != null) rig.Rotate(rig.boltHandle, new Vector3(0f, 0f, -70f * Mathf.Clamp01(up)));
                else rig.Rotate(rig.bolt, new Vector3(0f, 0f, -70f * Mathf.Clamp01(up)));
                if (c > (hand ? 0.38f : 0.30f) && cycleSound == 0) { cycleSound = 1; Synth.Mech("boltback", 0.55f, 1f); EjectCycleCasing(d); }
                if (c > (hand ? 0.66f : 0.62f) && cycleSound == 1) { cycleSound = 2; Synth.Mech("boltfwd", 0.55f, 1f); }
            }
        }

        void EjectCycleCasing(WeaponDef d)
        {
            if (cycleCasingDone || d.casing == "none" || rig.eject == null) return;
            cycleCasingDone = true;
            EjectCasing(d);
        }
    }
}
