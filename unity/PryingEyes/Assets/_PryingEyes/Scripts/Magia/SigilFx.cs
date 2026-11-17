using UnityEngine;

namespace PryingEyes
{
    /// <summary>I proiettili col sigillo (2026-11-17): un'arma con un sigillo elementale (o l'Arma Maledetta) lo mostra su OGNI colpo, non
    /// solo sull'arma. Alla bocca un lampo del colore dell'elemento (lingua di fuoco, vapore freddo, arco, spruzzo di icore); lungo la
    /// traiettoria una traccia luminosa corta e spessa del suo colore e, dove il proiettile e' passato, quello che l'elemento lascia
    /// (fiamme e braci, schegge di ghiaccio e nebbia, archi di corrente, gocce di acido o di sangue, scintille rosse, fuoco scuro); nel
    /// punto colpito uno scoppio dell'elemento e il suo segno sul muro (bruciatura, brina, macchia verde, sangue, fuoco nero). Gli
    /// stati sul nemico restano quelli di Weapon.SigilHit. Con i pallettoni gli effetti si diradano (otto pallini x tutto sarebbe troppo).</summary>
    public static class SigilFx
    {
        /// <summary>Per le prove: colpi, tracce e impatti disegnati.</summary>
        public static int Shots, Tracers, Impacts;
        /// <summary>Per le prove: dove e' stato disegnato l'ultimo lampo alla bocca.</summary>
        public static Vector3 LastMuzzle;

        /// <summary>Il colore della traccia: l'elemento, sovraesposto (i valori oltre 1 vanno al bloom).</summary>
        public static Color TracerColor(int el)
        {
            Color c = MagicFx.OfInfusion(el);
            switch (el)
            {
                case 14: return new Color(1f, 0.62f, 0.16f) * 2.4f;
                case 17: return new Color(0.55f, 0.88f, 1f) * 2.0f;
                case 16: return new Color(0.7f, 0.88f, 1f) * 2.4f;
                case 15: return new Color(0.45f, 1f, 0.16f) * 1.9f;
                case 13: return new Color(0.85f, 0.04f, 0.05f) * 1.8f;
                case 12: return new Color(1f, 0.14f, 0.18f) * 2.3f;
                default: return new Color(1f, 0.16f, 0.06f) * 2.3f;
            }
        }

        // ------------------------------------------------------------------ bocca
        /// <summary>Il colpo parte: un lampo dell'elemento alla bocca (la fiammata dell'arma si tinge in Weapon.SigilShot).</summary>
        public static void Muzzle(int el, Vector3 at, Vector3 fwd)
        {
            Shots++;
            LastMuzzle = at;
            Color c = MagicFx.OfInfusion(el);
            MagicFx.Glow(at, c, 0.08f, 0.07f, 0.9f);
            Vector3 up = Vector3.up;
            switch (el)
            {
                case 14: MagicFx.Flame(at + fwd * 0.03f, fwd * 0.8f + up * 0.5f, 1.15f); break;
                case 17: MagicFx.Mist(at + fwd * 0.04f, fwd * 0.6f, MagicFx.Frost, 0.04f, 0.5f, 0.32f); break;
                case 16: MagicFx.Arc(at, at + fwd * 0.14f + Random.insideUnitSphere * 0.05f, MagicFx.Shock, 0.004f, 3, 0.7f); break;
                case 15: MagicFx.Drip(at + fwd * 0.03f, MagicFx.Acid, 0.006f); MagicFx.Mist(at + fwd * 0.04f, fwd * 0.4f, MagicFx.Acid, 0.03f, 0.4f, 0.25f); break;
                case 13: MagicFx.Mist(at + fwd * 0.04f, fwd * 0.5f, new Color(0.45f, 0.02f, 0.03f), 0.035f, 0.4f, 0.3f); break;
                case 12: MagicFx.Sparks(at + fwd * 0.03f, c, 4, 2.2f, 0.008f); break;
                default:
                    if (MagicVfx.On) MagicVfx.Flame(at + fwd * 0.03f, fwd * 0.6f + up * 0.5f, 1f, MagicVfx.CursedFire);
                    else MagicFx.Flame(at + fwd * 0.03f, up, 1f);
                    break;
            }
        }

        // ------------------------------------------------------------------ traiettoria
        /// <summary>La traccia da 'from' a 'to': un dardo corto e luminoso del colore dell'elemento e i segni che lascia lungo la via.
        /// 'multi': un pallino di un colpo a pallettoni (meno particelle).</summary>
        public static void Tracer(int el, Vector3 from, Vector3 to, float baseWidth, bool multi)
        {
            float d = Vector3.Distance(from, to);
            if (d < 1.5f) return;
            Tracers++;
            Vector3 dir = (to - from) / d;
            float w = el == 16 ? 0.007f : Mathf.Max(baseWidth, 0.009f) * 1.9f;
            Color tc = TracerColor(el);
            Fx.Tracer(from, to, el == 14 || el == 18 ? 130f : 170f, w, tc, el == 16 ? 2.4f : 1.6f);
            // il dardo e' veloce e a 60 fotogrammi si vede per tre o quattro: dietro resta per un quinto di secondo una linea sottile
            // dello stesso colore, che sfuma (nei colpi a pallettoni solo su qualche pallino)
            if (!multi || Random.value < 0.3f) Fx.Beam(from, to, 0.006f, tc * 0.8f);
            int n = Mathf.Clamp(Mathf.RoundToInt(d / 1.5f), 2, multi ? 3 : 12);
            Vector3 up = Vector3.up;
            for (int k = 0; k < n; k++)
            {
                Vector3 p = from + dir * (d * Random.Range(0.08f, 1f)) + Random.insideUnitSphere * 0.025f;
                switch (el)
                {
                    case 14:
                        MagicFx.Flame(p, up, 0.55f);
                        if (MagicVfx.On && Random.value < 0.6f)
                            MagicVfx.Emit(MagicVfx.Kind.Ember, p, Random.insideUnitSphere * 0.5f + up * 0.3f, Random.Range(0.005f, 0.009f), Random.Range(0.4f, 0.9f), MagicFx.Fire);
                        break;
                    case 17:
                        MagicFx.Mist(p, Random.insideUnitSphere * 0.2f, MagicFx.Frost, 0.03f, 0.6f, 0.3f);
                        if (MagicVfx.On && Random.value < 0.7f)
                            MagicVfx.Emit(MagicVfx.Kind.Shard, p, Random.insideUnitSphere * 0.6f, Random.Range(0.008f, 0.014f), 0.35f, new Color(0.82f, 0.94f, 1f));
                        break;
                    case 16:
                        if (Random.value < 0.85f) MagicFx.Arc(p, p + Random.onUnitSphere * Random.Range(0.12f, 0.3f), MagicFx.Shock, 0.004f, 3, 0.7f);
                        break;
                    case 15:
                        MagicFx.Drip(p, MagicFx.Acid, 0.006f);
                        if (Random.value < 0.5f) MagicFx.Mist(p, Random.insideUnitSphere * 0.1f, MagicFx.Acid, 0.02f, 0.5f, 0.2f);
                        break;
                    case 13:
                        MagicFx.Drip(p, new Color(0.4f, 0.01f, 0.01f), 0.006f);
                        if (MagicVfx.On && Random.value < 0.5f)
                            MagicVfx.Emit(MagicVfx.Kind.Drop, p, Random.insideUnitSphere * 0.4f, 0.008f, 0.45f, new Color(0.5f, 0.02f, 0.02f));
                        break;
                    case 12:
                        MagicFx.Sparks(p, MagicFx.Crit, 2, 1.6f, 0.007f);
                        break;
                    default:
                        if (MagicVfx.On)
                        {
                            MagicVfx.Flame(p, up, 0.6f, MagicVfx.CursedFire);
                            if (Random.value < 0.4f) MagicVfx.Emit(MagicVfx.Kind.Smoke, p, up * 0.2f, 0.03f, 0.9f, new Color(0.06f, 0.01f, 0.01f, 0.4f));
                        }
                        else MagicFx.Flame(p, up, 0.55f);
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ impatto
        /// <summary>Il colpo arriva in 'at' (n: normale della superficie, o l'opposto della direzione se e' un nemico): uno scoppio
        /// dell'elemento e il suo segno sul muro. 'multi': un pallino di un colpo a pallettoni (si diradano).</summary>
        public static void Impact(int el, Vector3 at, Vector3 n, bool enemy, bool multi)
        {
            if (multi && Random.value > 0.35f) return;
            Impacts++;
            Color c = MagicFx.OfInfusion(el);
            Vector3 p = at + n * 0.02f, up = Vector3.up;
            bool mark = !enemy && Random.value < 0.5f;
            // un lampo breve del colore dell'elemento davanti al punto colpito: su una parete scura e' quello che dice subito di che
            // elemento e' il colpo (la luce prende solo il mondo, mai l'arma)
            MagicFx.Flash(at + n * 0.12f, c, 2.4f, 3.2f, 0.13f);
            switch (el)
            {
                case 14:
                    if (MagicVfx.On) MagicVfx.Explosion(p, MagicFx.Fire, 0.16f, Color.white); else MagicFx.Burst(p, MagicFx.Fire, 0.1f);
                    MagicFx.Flame(p, n * 0.5f + up, 1.5f);
                    MagicVfx.Emit(MagicVfx.Kind.Ring, p, Vector3.zero, 0.16f, 0.16f, MagicFx.Fire);
                    if (mark) Fx.Splat(at, n, Random.onUnitSphere, new Color(0.03f, 0.025f, 0.02f, 0.9f), 0.2f);
                    break;
                case 17:
                    for (int i = 0; i < 6; i++)
                        MagicVfx.Emit(MagicVfx.Kind.Shard, p, n * Random.Range(1.2f, 2.8f) + Random.insideUnitSphere * 1.2f, Random.Range(0.014f, 0.024f), 0.5f, new Color(0.82f, 0.94f, 1f));
                    MagicFx.Mist(p, n * 0.4f, MagicFx.Frost, 0.07f, 0.8f, 0.4f);
                    MagicFx.Glow(p, MagicFx.Frost, 0.16f, 0.16f, 0.85f);
                    MagicVfx.Emit(MagicVfx.Kind.Ring, p, Vector3.zero, 0.16f, 0.18f, MagicFx.Frost);
                    if (mark) Fx.Splat(at, n, Random.onUnitSphere, new Color(0.78f, 0.9f, 1f, 0.75f), 0.2f, 40f);
                    break;
                case 16:
                    for (int i = 0; i < 3; i++) MagicFx.Arc(p, p + (Random.onUnitSphere + n) * 0.22f, MagicFx.Shock, 0.004f, 3, 0.7f);
                    MagicFx.Sparks(p, MagicFx.Shock, 8, 3f, 0.009f);
                    MagicFx.Glow(p, MagicFx.Shock, 0.14f, 0.1f, 0.9f);
                    MagicVfx.Emit(MagicVfx.Kind.Ring, p, Vector3.zero, 0.14f, 0.16f, MagicFx.Shock);
                    if (mark) Fx.Splat(at, n, Random.onUnitSphere, new Color(0.04f, 0.05f, 0.08f, 0.85f), 0.17f);
                    break;
                case 15:
                    for (int i = 0; i < 4; i++)
                        MagicVfx.Emit(MagicVfx.Kind.Drop, p, n * Random.Range(0.8f, 2f) + Random.insideUnitSphere * 0.9f, 0.01f, 0.5f, new Color(0.35f, 0.8f, 0.12f));
                    MagicFx.Mist(p, n * 0.3f, MagicFx.Acid, 0.06f, 0.6f, 0.3f);
                    MagicFx.Glow(p, MagicFx.Acid, 0.14f, 0.18f, 0.85f);
                    MagicVfx.Emit(MagicVfx.Kind.Ring, p, Vector3.zero, 0.15f, 0.18f, MagicFx.Acid);
                    if (mark) Fx.Splat(at, n, Random.onUnitSphere, new Color(0.22f, 0.5f, 0.06f, 0.9f), 0.26f, 40f);
                    break;
                case 13:
                    for (int i = 0; i < 5; i++)
                        MagicVfx.Emit(MagicVfx.Kind.Drop, p, n * Random.Range(0.8f, 2.2f) + Random.insideUnitSphere * 0.9f, 0.012f, 0.5f, new Color(0.5f, 0.02f, 0.02f));
                    MagicFx.Mist(p, n * 0.3f, new Color(0.45f, 0.02f, 0.03f), 0.06f, 0.5f, 0.3f);
                    MagicFx.Glow(p, MagicFx.Blood, 0.1f, 0.12f, 0.7f);
                    if (mark) Fx.Splat(at, n, Random.onUnitSphere, new Color(0.3f, 0.01f, 0.01f, 0.95f), 0.26f, 45f);
                    break;
                case 12:
                    MagicFx.Glow(p, MagicFx.Crit, 0.2f, 0.12f, 1f);
                    MagicFx.Sparks(p, MagicFx.Crit, 12, 3f, 0.01f);
                    MagicVfx.Emit(MagicVfx.Kind.Ring, p, Vector3.zero, 0.2f, 0.18f, MagicFx.Crit);
                    if (mark) Fx.Splat(at, n, Random.onUnitSphere, new Color(0.12f, 0.01f, 0.02f, 0.9f), 0.18f);
                    break;
                default:
                    if (MagicVfx.On)
                    {
                        MagicVfx.Explosion(p, MagicFx.Cursed, 0.1f, new Color(1f, 0.3f, 0.12f));
                        MagicVfx.Emit(MagicVfx.Kind.Smoke, p, up * 0.25f, 0.08f, 1f, new Color(0.06f, 0.01f, 0.01f, 0.5f));
                        if (Random.value < 0.35f) MagicVfx.Emit(MagicVfx.Kind.Rune, p + n * 0.03f, n * 0.05f, 0.04f, 0.7f, MagicFx.Cursed, 0f);
                    }
                    else MagicFx.Burst(p, MagicFx.Cursed, 0.08f);
                    if (mark) Fx.Splat(at, n, Random.onUnitSphere, new Color(0.04f, 0.0f, 0.01f, 0.92f), 0.24f, 40f);
                    break;
            }
        }
    }
}
