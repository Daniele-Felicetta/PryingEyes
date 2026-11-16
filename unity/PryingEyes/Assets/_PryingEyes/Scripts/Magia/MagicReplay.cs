using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Le magie e i sigilli degli altri giocatori della cooperativa come li vede questa macchina (dal 2026-11-16): solo grafica e
    /// suono, niente danni ne' stati (i danni passano da HitClaim, gli stati da StatusClaim e EnemyNet.status). L'effetto parte dalla mano
    /// sinistra del pupazzo di chi lancia (RemotePlayer.HandPoint) verso la mira che ha mandato; il fulmine salta sugli stessi nemici (per
    /// id di rete); il sigillo lascia una luce colorata sulla mano destra del pupazzo finche' dura e finche' ha in mano quell'arma.</summary>
    public static class MagicReplay
    {
        public const int Spell = 0, Seal = 1;

        /// <summary>Per -cooptest: magie e sigilli degli altri riprodotti qui.</summary>
        public static int Count;

        /// <summary>Chi lancia: manda la magia agli altri (solo in cooperativa).</summary>
        public static void Send(int kind, int node, Vector3 from, Vector3 to, IList<Enemy> targets = null, int weaponSlot = 0)
        {
            if (!Coop.Active || Coop.Game == null) return;
            Coop.Game.SpellCast(kind, node, from, to, targets, weaponSlot);
        }

        /// <summary>La magia 'node' (12..18) o il sigillo del giocatore nello slot 'caster', come arriva dalla rete.</summary>
        public static void Play(int caster, int kind, int node, Vector3 from, Vector3 to, List<Enemy> targets, int weaponSlot)
        {
            Count++;
            RemotePlayer rp = null;
            var g = Coop.Game;
            if (g != null && g.Players.TryGetValue(caster, out var p) && p != null && p.Shown) rp = p;
            Vector3 hand = rp != null ? rp.HandPoint : from;
            Vector3 fwd = rp != null ? rp.Forward : (to - from).sqrMagnitude > 1e-4f ? (to - from).normalized : Vector3.forward;
            if (kind == Seal)
            {
                SealFx(hand, fwd, node);
                if (rp != null) RemoteSeal.Begin(rp, node, weaponSlot);
                return;
            }
            switch (node)
            {
                case 12:
                {
                    Vector3 dir = to - hand;
                    if (dir.sqrMagnitude < 1e-4f) dir = fwd;
                    Fireball.Launch(hand + dir.normalized * 0.12f, dir, 0f, 0f, true);
                    MagicFx.Flash(hand, MagicFx.Fire, 3f, 4f, 0.25f);
                    Sfx.At(SoundBank.MagiaFuoco, hand, 0.8f, 0.9f, 1.5f, 20f);
                    break;
                }
                case 13: Lightning(hand, to, targets); break;
                case 14: Frost(hand, (to - from).sqrMagnitude > 1e-4f ? (to - from).normalized : fwd, Vector3.Distance(from, to) / 7f); break;
                case 15: Drain(hand, rp, targets != null && targets.Count > 0 ? targets[0] : null); break;
                case 16:
                    MagicCaster.ShadowPuff(from, -1f);
                    MagicCaster.ShadowPuff(to, 1f);
                    Sfx.At(SoundBank.MagiaOmbra, to + Vector3.up, 0.8f, 1f, 2f, 25f);
                    break;
                case 17: Wave(from, to); break;
                default: Essenza(hand, fwd); break;
            }
        }

        static void Lightning(Vector3 hand, Vector3 end, List<Enemy> targets)
        {
            Sfx.At(SoundBank.MagiaFulmine, hand, 0.9f, Random.Range(0.95f, 1.05f), 2f, 40f);
            MagicFx.Flash(hand, MagicFx.Shock, 5f, 6f, 0.25f);
            Vector3 prev = hand;
            int k = 0;
            if (targets != null)
                foreach (var e in targets)
                {
                    if (e == null) continue;
                    Vector3 q = e.AimPoint;
                    Bolt(prev, q, k == 0);
                    prev = q; k++;
                }
            if (k == 0) Bolt(hand, hand + Vector3.ClampMagnitude(end - hand, 14f), true);
        }

        static void Bolt(Vector3 a, Vector3 b, bool first)
        {
            if (MagicVfx.On) { MagicVfx.Lightning(a, b, MagicFx.Shock, first ? 0.022f : 0.015f, 0.3f, first ? 3 : 2, 0.17f); MagicCaster.BoltHit(b); }
            else { for (int i = 0; i < 3; i++) MagicFx.Arc(a, b, MagicFx.Shock, first ? 0.018f : 0.012f, 8, 0.2f); MagicFx.Sparks(b, MagicFx.Shock, 10, 3.5f); }
        }

        /// <summary>Il cono di gelo: per un terzo di secondo vapore, schegge e luccichii dalla mano lungo la mira.</summary>
        static void Frost(Vector3 hand, Vector3 dir, float rk)
        {
            rk = Mathf.Clamp(rk, 0.5f, 2f);
            Sfx.At(SoundBank.MagiaGelo, hand, 0.9f, 1f, 2f, 30f);
            MagicFx.Flash(hand + dir * 0.5f, MagicFx.Frost, 3f, 7f, 0.4f);
            ReplayEmitter.Run(0.32f, t =>
            {
                for (int i = 0; i < 6; i++)
                {
                    Vector3 d = (dir + Random.insideUnitSphere * 0.36f).normalized;
                    MagicFx.Mist(hand + d * 0.05f, d * Random.Range(7f, 11f) * rk, MagicFx.Frost, 0.06f, 0.75f, 0.32f);
                }
                for (int i = 0; i < 4; i++)
                {
                    Vector3 d = (dir + Random.insideUnitSphere * 0.3f).normalized;
                    MagicVfx.Emit(MagicVfx.Kind.Shard, hand + d * 0.08f, d * Random.Range(9f, 15f) * rk, Random.Range(0.012f, 0.022f), Random.Range(0.3f, 0.45f), new Color(0.75f, 0.92f, 1f));
                }
                MagicFx.Glow(hand + dir * 0.05f, MagicFx.Frost, 0.12f, 0.06f, 0.7f);
                return true;
            });
        }

        /// <summary>Il filo di sangue dal petto del nemico alla mano del compagno, per il tempo dell'Emorragia (o finche' il nemico muore).</summary>
        static void Drain(Vector3 hand0, RemotePlayer rp, Enemy target)
        {
            Sfx.At(SoundBank.MagiaSangue, hand0, 0.7f, 1f, 1.5f, 15f);
            if (target == null) return;
            ReplayEmitter.Run(1.5f, t =>
            {
                if (target == null || target.hp <= 0f) return false;
                Vector3 hand = rp != null ? rp.HandPoint : hand0, a = target.AimPoint;
                if (MagicVfx.On) MagicVfx.BloodStream(a, hand, t);
                else for (int i = 0; i < 3; i++)
                    {
                        float k = Random.value;
                        Fx.Particle(1, true, Vector3.Lerp(a, hand, k), (hand - a).normalized * 5f, Vector3.zero, 0.014f, 0.004f, 0.12f, MagicFx.Blood);
                    }
                return true;
            });
        }

        static void Wave(Vector3 origin, Vector3 to)
        {
            Vector3 f = to - origin; f.y = 0f;
            float range = Mathf.Clamp(f.magnitude, 1f, 12f);
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Color c = new Color(0.85f, 0.55f, 1f);
            MagicFx.Ring(origin + f * 0.4f, f, range, c, 56, 200f);
            MagicFx.Flash(origin + f * 0.4f, c, 5f, 8f, 0.35f);
            Sfx.At(SoundBank.MagiaOnda, origin, 0.95f, 1f, 2f, 40f);
            GameManager.I?.player?.Shake(origin, 0.35f, 6f);
        }

        /// <summary>L'Essenza Maledetta: il cerchio del Dodecaedro davanti alla mano, le rune che girano, il fuoco maledetto.</summary>
        static void Essenza(Vector3 hand, Vector3 fwd)
        {
            if (MagicVfx.On)
            {
                MagicVfx.MagicCircle(hand + fwd * 0.03f, fwd, 0.2f, MagicFx.Essence, 1.1f, 50f);
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized, up = Vector3.Cross(fwd, right);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * 30f * Mathf.Deg2Rad;
                    Vector3 d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    MagicVfx.Emit(MagicVfx.Kind.Rune, hand + d * 0.2f, d * 0.12f, 0.035f, 0.9f, MagicFx.Essence, 0f);
                }
                MagicVfx.Explosion(hand + fwd * 0.05f, MagicVfx.CursedFire, 0.3f, MagicVfx.CursedFire);
            }
            else MagicFx.Burst(hand, MagicFx.Essence, 0.6f);
            MagicFx.Flash(hand, MagicFx.Essence, 6f, 8f, 0.6f);
            Sfx.At(SoundBank.MagiaEssenza, hand, 0.8f, 0.8f, 2f, 25f);
        }

        /// <summary>Il sigillo che si chiude: un piccolo cerchio del Dodecaedro e tre rune che salgono, del colore del sigillo.</summary>
        static void SealFx(Vector3 hand, Vector3 fwd, int node)
        {
            Color c = node == Grimoire.Supreme ? MagicVfx.CursedFire : MagicFx.OfInfusion(node);
            if (MagicVfx.On)
            {
                MagicVfx.MagicCircle(hand + fwd * 0.02f, fwd, 0.07f, c, 0.6f, 120f);
                for (int i = 0; i < 3; i++) MagicVfx.Emit(MagicVfx.Kind.Rune, hand + Random.insideUnitSphere * 0.05f, Vector3.up * 0.06f, 0.03f, 0.7f, c, 0f);
            }
            MagicFx.Flash(hand, c, 2.5f, 3f, 0.35f);
            Sfx.At(SoundBank.MagiaSigillo, hand, 0.7f, node == Grimoire.Supreme ? 0.7f : 1f, 1f, 12f);
        }
    }

    /// <summary>Un effetto di qualche frazione di secondo che va emesso fotogramma per fotogramma (cono di gelo, filo di sangue).</summary>
    sealed class ReplayEmitter : MonoBehaviour
    {
        System.Func<float, bool> tick;
        float t, dur;

        public static void Run(float dur, System.Func<float, bool> tick)
        {
            var r = new GameObject("MagiaDiUnCompagno").AddComponent<ReplayEmitter>();
            r.dur = dur; r.tick = tick;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t > dur || tick == null || !tick(t)) Destroy(gameObject);
        }
    }

    /// <summary>Il sigillo sull'arma di un compagno: luccichii del suo colore sulla mano destra del pupazzo, finche' il sigillo dura e il
    /// compagno ha in mano l'arma che lo porta (PlayerNet.weapon).</summary>
    sealed class RemoteSeal : MonoBehaviour
    {
        RemotePlayer rp;
        int node, slot;
        float until, fxT;

        public static void Begin(RemotePlayer rp, int node, int slot)
        {
            var s = rp.GetComponent<RemoteSeal>();
            if (s == null) s = rp.gameObject.AddComponent<RemoteSeal>();
            s.rp = rp; s.node = node; s.slot = slot;
            s.until = Time.time + (node == Grimoire.Supreme ? MagicState.CursedTime : MagicState.InfusionTime);
        }

        void Update()
        {
            if (rp == null || Time.time > until) { Destroy(this); return; }
            if (!rp.Shown || !rp.last.alive || rp.last.weapon != slot) return;
            fxT -= Time.deltaTime;
            if (fxT > 0f) return;
            fxT = 0.08f;
            Color c = node == Grimoire.Supreme ? MagicVfx.CursedFire : MagicFx.OfInfusion(node);
            Vector3 q = rp.GunPoint;
            MagicFx.Glow(q + Random.insideUnitSphere * 0.05f, c, 0.03f, 0.12f, 0.6f);
            if (Random.value < 0.4f) MagicFx.Sparks(q, c, 1, 0.4f, 0.006f);
        }
    }
}
