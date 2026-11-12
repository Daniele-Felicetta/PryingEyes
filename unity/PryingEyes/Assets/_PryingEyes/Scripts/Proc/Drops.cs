using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    public enum DropKind { Ammo, Insta, Double, Nuke }

    /// <summary>I quattro potenziamenti che cadono dai nemici, come i power-up dei Black Ops Zombies:
    ///  - Reliquiario: caricatori e riserve di tutte e due le armi al massimo;
    ///  - Sguardo: per 30 s ogni colpo uccide (un occhio che ti guarda: il rosso e' quello dell'avviso dei preti);
    ///  - Offerta: per 30 s i punti valgono il doppio;
    ///  - Campana: uccide chi e' nel round e chi e' entro 22 m, +400 punti; ma il rintocco si sente per tutto il piano (Alarm, 55 m):
    ///    chi sopravvive viene a cercarti. Nel gioco furtivo e' un'arma a doppio taglio.
    /// Cadono dall'uccisione di un nemico (4,5% a colpo, il doppio col coltello e il quadruplo con la presa alle spalle), al massimo
    /// quattro per piano, nell'ordine di un mazzo mescolato (prima i quattro, poi di nuovo): nessuno passa un'ora senza vedere una
    /// Campana o l'altra cosa. Restano a terra 30 s (lampeggiano gli ultimi 8) e si prendono passandoci vicino. La Notte delle
    /// Falene lascia sempre un Reliquiario (RoundDirector). Stato dei timer qui; il pickup e' DropPickup.cs; i timer nell'HUD
    /// sono in HUD.Punti.cs.</summary>
    public static class Drops
    {
        public const float Duration = 30f;               // secondi di Sguardo e di Offerta
        public const float Life = 30f;                   // quanto resta a terra
        public const int PerFloor = 4;
        public const float KillChance = 0.045f, KnifeMul = 2f, TakedownMul = 4f;
        public const int NukePoints = 400;
        public const float NukeRadius = 22f, NukeHearing = 55f;

        static float instaUntil, doubleUntil;
        static int onFloor;
        static readonly List<DropKind> bag = new List<DropKind>();
        static readonly List<DropPickup> live = new List<DropPickup>();

        /// <summary>Sguardo: ogni colpo uccide.</summary>
        public static bool InstaKill => Time.time < instaUntil;
        /// <summary>Offerta: i punti guadagnati valgono il doppio.</summary>
        public static bool DoublePoints => Time.time < doubleUntil;
        public static float InstaLeft => Mathf.Max(0f, instaUntil - Time.time);
        public static float DoubleLeft => Mathf.Max(0f, doubleUntil - Time.time);

        /// <summary>Dove e' morto l'ultimo nemico (il Reliquiario della Notte delle Falene cade li').</summary>
        public static Vector3 lastKillPos;
        public static IReadOnlyList<DropPickup> Live => live;
        public static int OnFloor => onFloor;

        /// <summary>Per le prove (-droptest): conteggi, e il prossimo nemico ucciso lascia di sicuro un potenziamento.</summary>
        public static int statSpawned, statPicked, statExpired;
        public static bool forceNext;
        /// <summary>Per le prove dei round e del coltello: niente cadute a caso (forceNext le forza lo stesso).</summary>
        public static bool disabled;

        // ------------------------------------------------------------------ caduta
        /// <summary>Chiamato da GameManager.OnEnemyKilled a ogni uccisione (nei piani).</summary>
        public static void OnKill(Enemy e, DamageSource src)
        {
            if (e == null) return;
            lastKillPos = e.transform.position;
            if (src == DamageSource.Nuke) return;                     // la Campana non ne fa cadere altri
            if (disabled && !forceNext) return;
            if (onFloor >= PerFloor && !forceNext) return;
            float p = KillChance * (src == DamageSource.Takedown ? TakedownMul : src == DamageSource.Knife ? KnifeMul : 1f);
            if (forceNext) { p = 1f; forceNext = false; }
            if (Random.value >= p) return;
            Spawn(NextKind(), lastKillPos);
        }

        static DropKind NextKind()
        {
            if (bag.Count == 0)
            {
                for (int i = 0; i < 4; i++) bag.Add((DropKind)i);
                for (int i = bag.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (bag[i], bag[j]) = (bag[j], bag[i]); }
            }
            var k = bag[bag.Count - 1];
            bag.RemoveAt(bag.Count - 1);
            return k;
        }

        /// <summary>Fa cadere un potenziamento: a terra sotto 'pos' (sulla navmesh, raggiungibile) e con il suono che lo annuncia.
        /// Non conta nel tetto del piano se e' un regalo di un round (counted = false).</summary>
        public static DropPickup Spawn(DropKind kind, Vector3 pos, bool counted = true)
        {
            Vector3 g = pos;
            if (Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out RaycastHit h, 6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) g = h.point;
            if (NavMesh.SamplePosition(g, out NavMeshHit nh, 2f, NavMesh.AllAreas)) g = nh.position;
            var go = new GameObject("Drop_" + kind);
            go.transform.position = g;
            var pk = go.AddComponent<DropPickup>();
            pk.Init(kind);
            live.Add(pk);
            if (counted) onFloor++;
            statSpawned++;
            Sfx.At(SoundBank.DropAppear, g + Vector3.up * 0.8f, 0.85f, 1f, 3f, 45f);
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] drop {kind} a {g} ({onFloor}/{PerFloor} sul piano)");
            return pk;
        }

        public static void Forget(DropPickup p) { live.Remove(p); }

        // ------------------------------------------------------------------ effetti
        /// <summary>Il giocatore ha preso un potenziamento.</summary>
        public static void Apply(DropKind kind)
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            var hud = gm.hud;
            statPicked++;
            switch (kind)
            {
                case DropKind.Ammo:
                    gm.player.weapon.MaxAmmo();
                    Sfx.UI(SoundBank.PickAmmo, 0.8f);
                    hud?.Flash(new Color(1f, 0.78f, 0.35f), 0.35f, 0.22f);
                    hud?.Banner("Reliquiario", "Munizioni al massimo.");
                    break;
                case DropKind.Insta:
                    instaUntil = Time.time + Duration;
                    lastShownInsta = Mathf.CeilToInt(Duration);
                    Sfx.UI(SoundBank.PickEye, 0.85f);
                    hud?.Flash(new Color(0.9f, 0.1f, 0.06f), 0.5f, 0.32f);
                    hud?.Banner("Sguardo", "Ogni colpo uccide.");
                    break;
                case DropKind.Double:
                    doubleUntil = Time.time + Duration;
                    lastShownDouble = Mathf.CeilToInt(Duration);
                    Sfx.UI(SoundBank.PickCoins, 0.8f);
                    hud?.Flash(new Color(1f, 0.82f, 0.3f), 0.4f, 0.26f);
                    hud?.Banner("Offerta", "I punti valgono il doppio.");
                    break;
                default:
                    Toll(gm);
                    break;
            }
        }

        /// <summary>La Campana: lampo bianco, rintocco, chi e' nel round o entro 22 m cade (nessun punto a testa, 400 in tutto), e il
        /// suono arriva fino a 55 m: i sopravvissuti vengono a cercarti.</summary>
        static void Toll(GameManager gm)
        {
            var pl = gm.player;
            var hud = gm.hud;
            Vector3 pp = pl.transform.position;
            Sfx.UI(SoundBank.NukeToll, 0.95f);
            hud?.Flash(Color.white, 1.7f, 0.95f);
            var rd = RoundDirector.I;
            int killed = 0;
            foreach (var e in new List<Enemy>(Enemy.All))
            {
                if (e == null || e.IsDead) continue;
                bool mine = rd != null && rd.Owns(e);
                if (!mine && Vector3.Distance(e.transform.position, pp) > NukeRadius) continue;
                RoundFlare.At(e.transform.position + Vector3.up * 0.4f);
                e.Damage(e.hp + 1f, e.AimPoint, false, false, DamageSource.Nuke);
                killed++;
            }
            gm.AddPoints(NukePoints);
            Noise.Emit(pp, NukeHearing, NoiseKind.Alarm);          // dopo: lo sentono i sopravvissuti
            hud?.Banner("Campana", killed > 0 ? "Le presenze vicine cadono. Tutto il piano ha sentito." : "Tutto il piano ha sentito.");
            statNuked += killed;
        }
        public static int statNuked;

        // ------------------------------------------------------------------ tempo
        static int lastShownInsta, lastShownDouble;

        /// <summary>Ogni fotogramma (StealthRig): negli ultimi cinque secondi di un potenziamento un tic al secondo; alla fine una nota che scende.</summary>
        public static void Tick(float dt)
        {
            Beat(InstaLeft, ref lastShownInsta);
            Beat(DoubleLeft, ref lastShownDouble);
        }

        static void Beat(float left, ref int shown)
        {
            int s = left > 0f ? Mathf.CeilToInt(left) : 0;
            if (s == shown) return;
            if (s < shown)
            {
                if (s == 0) Sfx.UI(SoundBank.DropEnd, 0.5f);
                else if (s <= 5) Sfx.UI(SoundBank.DropTick, 0.4f);
            }
            shown = s;
        }

        // ------------------------------------------------------------------ azzeramenti
        /// <summary>Nuova partita.</summary>
        public static void ResetRun()
        {
            instaUntil = doubleUntil = 0f;
            lastShownInsta = lastShownDouble = 0;
            bag.Clear();
            NewFloor();
            statSpawned = statPicked = statExpired = statNuked = 0;
        }

        /// <summary>Nuovo piano: i potenziamenti a terra spariscono con il vecchio; i timer in corso continuano.</summary>
        public static void NewFloor()
        {
            foreach (var p in live.ToArray()) if (p != null) Object.Destroy(p.gameObject);
            live.Clear();
            onFloor = 0;
        }

        /// <summary>Il giocatore e' morto: i timer si spengono (i potenziamenti a terra restano).</summary>
        public static void OnPlayerDied()
        {
            instaUntil = doubleUntil = 0f;
            lastShownInsta = lastShownDouble = 0;
        }
    }
}
