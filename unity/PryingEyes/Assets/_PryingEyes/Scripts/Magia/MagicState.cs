using System;

namespace PryingEyes
{
    /// <summary>Lo stato magico di un giocatore (come PlayerPerks: niente Unity, ognuno il suo in cooperativa): i nodi comprati nei due
    /// grimori, l'essenza (la riserva della mano sinistra), la magia e il sigillo scelti, i tempi di recupero, i sigilli attivi sulle
    /// due armi e i due supremi. Qui stanno anche tutti i numeri degli effetti passivi, cosi' i punti di aggancio (Weapon,
    /// PlayerController, MagicCaster) chiedono e basta.</summary>
    public sealed class MagicState
    {
        public uint Witch, Arsenal;
        public float Essence = StartEssence;
        public int SelectedSpell = -1, SelectedInfusion = -1;
        readonly float[] cooldown = new float[Grimoire.Count];
        public readonly int[] SlotInfusion = { -1, -1 };
        public readonly float[] SlotInfusionLeft = new float[2];
        public float EssenzaLeft, CursedLeft, RuneLeft;
        public int CursedSlot = -1;
        readonly Random rng = new Random(7919);
        public event Action Changed;

        public const float BaseMax = 100f, StartEssence = 60f, BaseRegen = 2.0f, KillEssence = 3f;
        public const float InfusionTime = 45f, InfusionMaxTime = 60f, EssenzaTime = 12f, CursedTime = 15f, RuneTime = 2.5f;

        public bool HasW(int i) => Grimoire.Has(Witch, i);
        public bool HasA(int i) => Grimoire.Has(Arsenal, i);
        public uint Mask(Grimoire.Book b) => b == Grimoire.Book.Stregoneria ? Witch : Arsenal;
        public bool AnySpell => (Witch >> Grimoire.FirstInner) != 0;
        public bool AnyInfusion => (Arsenal >> Grimoire.FirstInner) != 0;

        // ------------------------------------------------------------------ essenza
        public float EssenceMax => BaseMax + (HasW(10) ? 40f : 0f);
        /// <summary>Essenza al secondo: 2, +60% con Sorgente Nera, doppia da fermi con Radici Cave, tripla nell'Essenza Maledetta.</summary>
        public float Regen(bool still) => BaseRegen * (HasW(11) ? 1.6f : 1f) * (still && HasW(5) ? 2f : 1f) * (EssenzaLeft > 0f ? 3f : 1f);
        public float KillGain => KillEssence + (HasW(7) ? 5f : 0f);

        // ------------------------------------------------------------------ magie (Stregoneria)
        public float SpellDamage => (HasW(0) ? 1.25f : 1f) * (EssenzaLeft > 0f ? 1.6f : 1f);
        public float SpellRange => HasW(1) ? 1.35f : 1f;
        public float CooldownMult => (HasW(2) ? 0.7f : 1f) * (EssenzaLeft > 0f ? 0.25f : 1f);
        public float CastSpeed => HasW(3) ? 1.25f : 1f;
        public bool SpellSlows => HasW(4);
        public bool BloodPact => HasW(6);
        public bool Echo => HasW(8);
        public float DamageTakenMult => RuneLeft > 0f ? 0.65f : 1f;

        public static float SpellCost(int node) => node switch { 12 => 30f, 13 => 35f, 14 => 30f, 15 => 25f, 16 => 30f, 17 => 35f, 18 => 60f, _ => 0f };
        public static float SpellCooldown(int node) => node switch { 12 => 2.5f, 13 => 4f, 14 => 5f, 15 => 7f, 16 => 6f, 17 => 6f, 18 => 90f, _ => 1f };
        public static float InfusionCost(int node) => node == Grimoire.Supreme ? 60f : 40f;
        public static float InfusionCooldown(int node) => node == Grimoire.Supreme ? 90f : 0f;

        public float Cooldown(int node) => node >= 0 && node < Grimoire.Count ? cooldown[node] : 0f;
        public float CooldownFrac(int node, bool infusion)
        {
            float full = (infusion ? InfusionCooldown(node) : SpellCooldown(node)) * (infusion ? 1f : CooldownMult);
            return full <= 0f ? 0f : Math.Min(1f, Math.Max(0f, Cooldown(node) / full));
        }

        /// <summary>Si puo' lanciare la magia (o tracciare il sigillo)? 'why' dice perche' no.</summary>
        public bool CanCast(int node, bool infusion, float hp, out string why)
        {
            why = null;
            uint mask = infusion ? Arsenal : Witch;
            if (node < Grimoire.FirstInner || !Grimoire.Has(mask, node)) { why = infusion ? "Nessun sigillo per l'arma" : "Nessuna magia"; return false; }
            if (cooldown[node] > 0f) { why = $"Ancora {Math.Ceiling(cooldown[node])} s"; return false; }
            float cost = infusion ? InfusionCost(node) : (EssenzaLeft > 0f ? 0f : SpellCost(node));
            if (Essence + 0.01f < cost)
            {
                if (!infusion && BloodPact && hp - (cost - Essence) >= 20f) return true;
                why = "Essenza insufficiente";
                return false;
            }
            return true;
        }

        /// <summary>Paga la magia o il sigillo: essenza, e col Patto di Sangue la vita che manca (restituita in hpCost). Eco del Rito:
        /// una volta su cinque la magia e' gratis.</summary>
        public void Pay(int node, bool infusion, out float hpCost)
        {
            hpCost = 0f;
            float cost = infusion ? InfusionCost(node) : (EssenzaLeft > 0f ? 0f : SpellCost(node));
            if (!infusion && Echo && rng.NextDouble() < 0.2) cost = 0f;
            if (Essence >= cost) Essence -= cost;
            else { hpCost = cost - Essence; Essence = 0f; }
            cooldown[node] = infusion ? InfusionCooldown(node) : SpellCooldown(node) * CooldownMult;
            if (!infusion && HasW(9)) RuneLeft = RuneTime;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ sigilli (Arsenale)
        public float SpreadMult => HasA(0) ? 0.7f : 1f;
        public float ExecuteMult(float hpFrac) => HasA(1) && hpFrac < 0.34f ? 1.4f : 1f;
        public int ExtraPierce => HasA(2) ? 1 : 0;
        public float ImpactMult => HasA(3) ? 1.15f : 1f;
        public float RateMult => HasA(4) ? 1.12f : 1f;
        public bool Detonation => HasA(5);
        public bool GhostAmmo => HasA(6);
        public float MagMult => HasA(7) ? 1.25f : 1f;
        public float HandlingMult => HasA(8) ? 1.2f : 1f;
        public bool AutoReload => HasA(9);
        public float RecoilMult => HasA(10) ? 0.7f : 1f;
        public bool KillsExtend => HasA(11);

        /// <summary>Il sigillo elementale attivo sull'arma dello slot (12..17), -1 se nessuno.</summary>
        public int InfusionOn(int slot) => slot >= 0 && slot < 2 && SlotInfusionLeft[slot] > 0f ? SlotInfusion[slot] : -1;
        public bool CursedOn(int slot) => CursedLeft > 0f && CursedSlot == slot;

        /// <summary>Il sigillo e' stato tracciato sull'arma dello slot: elementale per 45 s, o l'Arma Maledetta per 15 s.</summary>
        public void ApplyInfusion(int node, int slot)
        {
            if (slot < 0 || slot > 1) return;
            if (node == Grimoire.Supreme) { CursedSlot = slot; CursedLeft = CursedTime; }
            else { SlotInfusion[slot] = node; SlotInfusionLeft[slot] = InfusionTime; }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ libro
        public Grimoire.Lock Status(Grimoire.Book b, int i) => Grimoire.Status(Mask(b), i);
        public int Price(Grimoire.Book b, int i) => Grimoire.Price(Mask(b), i);

        /// <summary>Compra un nodo (i punti li toglie il chiamante). Una magia o un sigillo nuovi diventano quelli scelti.</summary>
        public bool Buy(Grimoire.Book b, int i)
        {
            if (Status(b, i) != Grimoire.Lock.Libero) return false;
            if (b == Grimoire.Book.Stregoneria) Witch |= 1u << i; else Arsenal |= 1u << i;
            if (i >= Grimoire.FirstInner)
            {
                if (b == Grimoire.Book.Stregoneria) SelectedSpell = i; else SelectedInfusion = i;
            }
            if (b == Grimoire.Book.Stregoneria && i == 10) Essence = Math.Min(EssenceMax, Essence + 40f);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Sceglie una magia o un sigillo gia' posseduto (dal libro o con i tasti 6 e 7).</summary>
        public bool Select(Grimoire.Book b, int i)
        {
            if (i < Grimoire.FirstInner || !Grimoire.Has(Mask(b), i)) return false;
            if (b == Grimoire.Book.Stregoneria) SelectedSpell = i; else SelectedInfusion = i;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Passa alla magia (o al sigillo) seguente tra quelli posseduti.</summary>
        public bool Cycle(Grimoire.Book b)
        {
            uint m = Mask(b);
            int cur = b == Grimoire.Book.Stregoneria ? SelectedSpell : SelectedInfusion;
            int start = cur >= Grimoire.FirstInner ? cur - Grimoire.FirstInner : -1;
            for (int k = 1; k <= 7; k++)
            {
                int i = Grimoire.FirstInner + (((start + k) % 7) + 7) % 7;
                if (Grimoire.Has(m, i)) return Select(b, i) && i != cur;
            }
            return false;
        }

        // ------------------------------------------------------------------ tempo, uccisioni, partita
        public void OnKill()
        {
            Essence = Math.Min(EssenceMax, Essence + KillGain);
            if (KillsExtend)
                for (int s = 0; s < 2; s++)
                    if (SlotInfusionLeft[s] > 0f) SlotInfusionLeft[s] = Math.Min(InfusionMaxTime, SlotInfusionLeft[s] + 1.5f);
        }

        public void Tick(float dt, bool still)
        {
            if (float.IsNaN(dt) || dt <= 0f) return;
            Essence = Math.Min(EssenceMax, Essence + Regen(still) * dt);
            for (int i = 0; i < cooldown.Length; i++) if (cooldown[i] > 0f) cooldown[i] = Math.Max(0f, cooldown[i] - dt);
            for (int s = 0; s < 2; s++) if (SlotInfusionLeft[s] > 0f) SlotInfusionLeft[s] = Math.Max(0f, SlotInfusionLeft[s] - dt);
            EssenzaLeft = Math.Max(0f, EssenzaLeft - dt);
            CursedLeft = Math.Max(0f, CursedLeft - dt);
            RuneLeft = Math.Max(0f, RuneLeft - dt);
        }

        public void StartEssenza() { EssenzaLeft = EssenzaTime; Changed?.Invoke(); }

        /// <summary>Per le prove: tutti i tempi di recupero a zero.</summary>
        public void ClearCooldowns() => Array.Clear(cooldown, 0, cooldown.Length);

        public void ResetRun()
        {
            Witch = Arsenal = 0; Essence = StartEssence; SelectedSpell = SelectedInfusion = -1;
            Array.Clear(cooldown, 0, cooldown.Length);
            SlotInfusion[0] = SlotInfusion[1] = -1; SlotInfusionLeft[0] = SlotInfusionLeft[1] = 0f;
            EssenzaLeft = CursedLeft = RuneLeft = 0f; CursedSlot = -1;
            Changed?.Invoke();
        }

        /// <summary>Per salvare una spedizione (RunRecord.state["magic"]): i nodi comprati nei due grimori, l'essenza e la scelta fatta. I tempi di
        /// recupero e i sigilli accesi sulle armi non si salvano: ripartono da zero.</summary>
        public Production.Saves.JsonItem Capture() => Production.Saves.JsonItem.Object()
            .Set("witch", (long)Witch).Set("arsenal", (long)Arsenal).Set("essence", (double)Essence).Set("spell", SelectedSpell).Set("infusion", SelectedInfusion);

        /// <summary>Rimette quello che Capture ha scritto, senza fidarsi: la scelta vale solo se il nodo e' comprato, l'essenza sta nei suoi limiti.</summary>
        public void RestoreFrom(Production.Saves.JsonItem saved)
        {
            if (saved == null || !saved.IsObject) return;
            ResetRun();
            const uint known = (1u << Grimoire.Count) - 1u;      // un bit che questa versione non conosce non compra niente
            Witch = (uint)Math.Min((long)uint.MaxValue, Math.Max(0L, saved.Long("witch"))) & known;
            Arsenal = (uint)Math.Min((long)uint.MaxValue, Math.Max(0L, saved.Long("arsenal"))) & known;
            Essence = (float)Math.Max(0.0, Math.Min((double)EssenceMax, saved.Num("essence", StartEssence)));
            int spell = saved.Int("spell", -1), infusion = saved.Int("infusion", -1);
            SelectedSpell = spell >= 0 && spell < Grimoire.Count && HasW(spell) ? spell : -1;
            SelectedInfusion = infusion >= 0 && infusion < Grimoire.Count && HasA(infusion) ? infusion : -1;
            Changed?.Invoke();
        }

        /// <summary>Alla morte: l'essenza si svuota e i sigilli sulle armi si spengono; i nodi comprati restano (i grimori sono rari).</summary>
        public void OnDeath()
        {
            Essence = 0f;
            SlotInfusionLeft[0] = SlotInfusionLeft[1] = 0f;
            EssenzaLeft = CursedLeft = RuneLeft = 0f;
            Changed?.Invoke();
        }
    }
}
