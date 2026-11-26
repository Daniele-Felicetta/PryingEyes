using Production.Weapons;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>I sigilli del Libro dell'Arsenale sui colpi dell'arma (2026-11-16). Gli esterni valgono sempre (Forza d'Impatto, Colpo
    /// di Grazia, Detonazione, Ricarica Arcana qui; cadenza, caricatore, mira, ricarica, rinculo, dispersione, perforazione e munizioni
    /// fantasma nei loro punti di Weapon.cs); gli interni sono il sigillo elementale tracciato sull'arma dalla mano sinistra (per 45 s su
    /// quell'arma): critici, emorragia, fuoco, corrosione, elettricita', gelo. L'Arma Maledetta li porta tutti, raddoppia il danno e non
    /// consuma munizioni per 15 s.</summary>
    public partial class Weapon
    {
        /// <summary>Per le prove: l'ultimo colpo e' stato critico.</summary>
        public static bool lastCrit;

        /// <summary>Il danno di un proiettile sul nemico con i sigilli: Forza d'Impatto, Colpo di Grazia, corrosione del bersaglio, Arma
        /// Maledetta (x2) e il critico (x2,5, un colpo su quattro) del sigillo Danni Critici.</summary>
        float SigilDamage(Enemy e, float dmg, Vector3 p)
        {
            var m = OwnerMagic;
            lastCrit = false;
            dmg *= m.ImpactMult;
            if (e != null && e.maxHp > 0f) dmg *= m.ExecuteMult(e.hp / e.maxHp);
            var st = EnemyStatus.Of(e);
            if (st != null) dmg *= st.DamageTakenMult;
            bool cursed = m.CursedOn(cur);
            int inf = m.InfusionOn(cur);
            if (cursed) dmg *= 2f;
            if ((inf == 12 || cursed) && Random.value < 0.25f)
            {
                dmg *= 2.5f;
                lastCrit = true;
                MagicFx.Sparks(p, MagicFx.Crit, 7, 2.2f, 0.01f);
                MagicFx.Glow(p, MagicFx.Crit, 0.12f, 0.15f, 0.7f);
            }
            return dmg;
        }

        /// <summary>Gli stati che il sigillo elementale lascia sul nemico colpito. Coi pallettoni le probabilita' si dividono per i pallini.</summary>
        void SigilHit(Enemy e, float dmg, Vector3 p, WeaponDef d)
        {
            var m = OwnerMagic;
            bool cursed = m.CursedOn(cur);
            int inf = m.InfusionOn(cur);
            if ((inf < 0 && !cursed) || e == null || e.hp <= 0f) return;
            float pk = 1f / Mathf.Max(1, d.pellets);
            var st = EnemyStatus.Ensure(e);
            if (st == null) return;
            if ((inf == 13 || cursed) && Random.value < Mathf.Max(0.12f, pk)) st.Bleed(8f, 6f);
            if (inf == 14 || cursed) st.Burn(10f, 4f);
            if (inf == 15 || cursed) st.Corrode(5f);
            if (inf == 17 || cursed) st.Chill(0.25f * Mathf.Max(0.3f, pk), 3f, 2f);
            if ((inf == 16 || cursed) && Random.value < (cursed ? 0.2f : 0.35f) * Mathf.Max(0.25f, pk))
            {
                st.Stun(0.4f);
                // l'arco salta a due nemici vicini, col 40% del colpo
                var skip = new System.Collections.Generic.List<Enemy> { e };
                Vector3 from = e.AimPoint;
                for (int k = 0; k < 2; k++)
                {
                    var n = MagicFx.NearestFrom(from, 5f, skip);
                    if (n == null) break;
                    skip.Add(n);
                    Vector3 to = n.AimPoint;
                    MagicFx.Arc(from, to, MagicFx.Shock, 0.01f, 6, 0.2f);
                    n.Damage(dmg * 0.4f, to, false, true, DamageSource.Magia);
                    if (n != null && n.hp > 0f) EnemyStatus.Ensure(n)?.Stun(0.3f);
                    from = to;
                }
            }
        }

        /// <summary>L'elemento che l'arma in mano porta sui colpi: 12..17 il sigillo, 18 l'Arma Maledetta, -1 nessuno.</summary>
        int SigilElement()
        {
            var m = OwnerMagic;
            return m.CursedOn(cur) ? 18 : m.InfusionOn(cur);
        }

        MaterialPropertyBlock flashMpb;
        bool flashTinted;

        /// <summary>Il colpo col sigillo (2026-11-17): alla bocca il lampo dell'elemento (SigilFx.Muzzle), la fiammata dell'arma e la
        /// sua luce si tingono del suo colore, l'arma si accende un attimo di piu'. Senza sigillo rimette il colore dell'arma, se l'aveva
        /// cambiato.</summary>
        void SigilShot(WeaponDef d)
        {
            int sig = SigilElement();
            if (sig < 0 && !flashTinted) return;
            if (flashA == null || flashB == null || flashLight == null) return;
            Color fc = sig >= 0 ? Color.Lerp(MagicFx.OfInfusion(sig), Color.white, 0.2f) : Color.Lerp(Color.white, d.flashColor.ToColor(), 0.7f);
            if (flashMpb == null) flashMpb = new MaterialPropertyBlock();
            flashMpb.SetColor("_BaseColor", fc);
            flashA.GetComponent<MeshRenderer>().SetPropertyBlock(flashMpb);
            flashB.GetComponent<MeshRenderer>().SetPropertyBlock(flashMpb);
            flashLight.color = sig >= 0 ? MagicFx.OfInfusion(sig) : d.flashColor.ToColor();
            flashTinted = sig >= 0;
            if (sig < 0) return;
            Vector3 mz = rig != null && rig.muzzle != null ? rig.muzzle.position : cam.position + cam.forward * 0.6f;
            if (ThirdPersonView.MuzzleOf(player, out var bodyMuzzle)) mz = bodyMuzzle;      // terza persona: il lampo esce dall'arma del corpo che si vede
            SigilFx.Muzzle(sig, mz, cam.forward);
            var aura = viewRoot != null ? viewRoot.GetComponent<WeaponAura>() : null;
            if (aura != null) aura.Pulse(0.85f);
        }

        /// <summary>Detonazione: un colpo su sedici esplode dove arriva (2,5 m, 60% del danno dell'arma che cala col raggio).</summary>
        void Detonate(Vector3 at, float baseDmg)
        {
            MagicFx.Burst(at, MagicFx.Fire, 0.45f);
            MagicFx.Flash(at, MagicFx.Fire, 4f, 5f, 0.3f);
            Sfx.At(SoundBank.MagiaBoom, at, 0.55f, 1.25f, 2f, 30f);
            var list = new System.Collections.Generic.List<Enemy>(Enemy.All);
            foreach (var e in list)
            {
                if (e == null || e.hp <= 0f) continue;
                float dd = Vector3.Distance(e.AimPoint, at);
                if (dd > 2.5f || !HotelNav.ClearLine(at + Vector3.up * 0.1f, e.AimPoint)) continue;
                e.Damage(baseDmg * 0.6f * Mathf.Lerp(1f, 0.4f, dd / 2.5f), e.AimPoint, false, true, DamageSource.Esplosione);
            }
        }

        /// <summary>Ricarica Arcana: l'arma nella fondina si ricarica da sola tre secondi dopo che serve.</summary>
        void AutoReloadHolstered(float dt)
        {
            if (!OwnerMagic.AutoReload) { holsterT = 0f; return; }
            int o = 1 - cur;
            if (slots[o].idx < 0) { holsterT = 0f; return; }
            var od = WeaponDefs.All[slots[o].idx];
            int cap = MagFor(od, slots[o].upg);
            if (slots[o].mag >= cap || slots[o].reserve <= 0) { holsterT = 0f; return; }
            holsterT += dt;
            if (holsterT < 3f) return;
            holsterT = 0f;
            int take = Mathf.Min(cap - slots[o].mag, slots[o].reserve);
            slots[o].state.SetAmmunition(slots[o].mag + take, slots[o].reserve - take);
        }

        /// <summary>Per le prove: munizioni dello slot (caricatore, riserva).</summary>
        public Vector2Int AmmoOf(int slot) => new Vector2Int(slots[slot].mag, slots[slot].reserve);
    }
}
