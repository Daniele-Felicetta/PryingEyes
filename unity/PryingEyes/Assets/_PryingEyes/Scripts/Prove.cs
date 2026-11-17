using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Le Prove (2026-11-15): una sfida per ondata, una sì e una no dalla seconda, come le prove dei Black Ops Zombies. Durante
    /// l'ondata in basso al centro c'e' la sfida col contatore; superata, premio subito (punti, e da soli o sull'ospite anche una
    /// goccia); se l'ondata finisce prima, e' persa. Ognuno ha la sua (in cooperativa conta le proprie uccisioni, che arrivano a
    /// GameManager.CreditKill anche sugli ospitati).
    ///  - Testa: uccisioni alla testa; Coltello: col coltello (anche alle spalle); Esplosivi: con acquasanta, carillon, appestati
    ///    fatti scoppiare e trappole; Intatto: secondi di fila senza un graffio; Furia: tante uccisioni in 15 secondi.</summary>
    public sealed class Prove : MonoBehaviour
    {
        public enum Kind { Testa, Coltello, Esplosivi, Intatto, Furia }
        public static Prove I;
        public bool Active { get; private set; }
        public Kind kind;
        public int goal, progress;
        int seenRound = -1, startRound;
        bool sawFight;                         // la prova si perde quando finisce l'ondata in cui e' cominciata
        float sinceHurt, furyStart;
        int furyCount;
        float hudT;
        public static int completed;          // per le prove automatiche
        /// <summary>Per le prove automatiche (come Drops.disabled): la Prova di ogni ondata non si tira a sorte ma e' sempre questa. null =
        /// a sorte, come nel gioco. -survivaltest la fissa su Testa: le sue uccisioni non sono mai alla testa e non la completano mai, mentre
        /// Furia si completa da sola con le uccisioni a raffica, paga +520/+640 e lascia cadere un potenziamento.</summary>
        public static Kind? pinned;

        public static Prove Ensure(GameObject host)
        {
            var p = host.GetComponent<Prove>();
            if (p == null) p = host.AddComponent<Prove>();
            I = p;
            return p;
        }

        public string Label => kind switch
        {
            Kind.Testa => "Uccisioni alla testa",
            Kind.Coltello => "Uccisioni col coltello",
            Kind.Esplosivi => "Uccisioni con esplosioni, fuoco o fulmini",
            Kind.Intatto => "Secondi senza essere toccato",
            _ => "Uccisioni in 15 secondi",
        };

        /// <summary>Comincia una prova (pubblico per le prove automatiche).</summary>
        public void Begin(Kind k, int target)
        {
            kind = k; goal = Mathf.Max(1, target); progress = 0; Active = true;
            sinceHurt = 0f; furyCount = 0; furyStart = Time.time;
            startRound = RoundDirector.Round;
            sawFight = RoundDirector.I != null && RoundDirector.I.Fighting;
            GameManager.I?.hud?.Toast($"Prova dell'ondata: {Label.ToLower()} ({goal})");
            Sfx.UI(SoundBank.BookOpen, 0.6f, 1.1f);
            Refresh();
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] prova {k} obiettivo {goal}");
        }

        void Pick(int round)
        {
            var choices = new System.Collections.Generic.List<Kind> { Kind.Testa, Kind.Esplosivi, Kind.Intatto, Kind.Furia };
            if (round >= 3) choices.Add(Kind.Coltello);
            var k = pinned ?? choices[Random.Range(0, choices.Count)];
            int target = k switch
            {
                Kind.Testa => Mathf.Min(14, 6 + round / 2),
                Kind.Coltello => Mathf.Min(6, 3 + round / 4),
                Kind.Esplosivi => Mathf.Min(10, 4 + round / 3),
                Kind.Intatto => 40,
                _ => Mathf.Min(14, 8 + round / 3),
            };
            Begin(k, target);
        }

        void Update() { using (Seats.Enter(this)) UpdateBody(); }

        void UpdateBody()
        {
            var rd = RoundDirector.I;
            int round = RoundDirector.Round;
            bool fighting = rd != null && rd.Fighting;
            if (seenRound < 0) seenRound = round;
            if (round > seenRound)
            {
                seenRound = round;
                if (Active) Fail();
                if (SurvivalMode.Enabled && fighting && round >= 2 && round % 2 == 0 && !(rd != null && rd.Moth)) Pick(round);
            }
            if (!Active) return;
            if (fighting) sawFight = true;
            else if (sawFight && round == startRound) { Fail(); return; }      // ondata finita senza superarla
            if (kind == Kind.Intatto)
            {
                sinceHurt += Time.deltaTime;
                int s = Mathf.FloorToInt(sinceHurt);
                if (s != progress) { progress = s; if (progress >= goal) Complete(); }
            }
            if (kind == Kind.Furia && Time.time - furyStart > 15f) { furyCount = 0; furyStart = Time.time; progress = 0; }
            hudT -= Time.deltaTime;
            if (hudT <= 0f) { hudT = 0.25f; Refresh(); }
        }

        void Refresh()
        {
            if (!Active) { GameManager.I?.hud?.SetProva(null); return; }
            string extra = kind == Kind.Furia && furyCount > 0 ? $"  ({Mathf.CeilToInt(15f - (Time.time - furyStart))} s)" : "";
            GameManager.I?.hud?.SetProva($"PROVA · {Label}  {Mathf.Min(progress, goal)}/{goal}{extra}");
        }

        /// <summary>Un'uccisione di questo giocatore (GameManager.CreditKill).</summary>
        public static void OnKill(DamageSource src, bool headshot)
        {
            var p = I;
            if (p == null || !p.Active) return;
            switch (p.kind)
            {
                case Kind.Testa: if (headshot) p.progress++; break;
                case Kind.Coltello: if (src == DamageSource.Knife || src == DamageSource.Takedown) p.progress++; break;
                case Kind.Esplosivi: if (src == DamageSource.Esplosione) p.progress++; break;
                case Kind.Furia:
                    if (p.furyCount == 0) p.furyStart = Time.time;
                    p.furyCount++; p.progress = p.furyCount;
                    break;
                default: return;
            }
            if (p.progress >= p.goal) p.Complete(); else p.Refresh();
        }

        /// <summary>Questo giocatore ha preso un colpo (PlayerController.Hurt).</summary>
        public static void OnHurt()
        {
            var p = I;
            if (p == null || !p.Active || p.kind != Kind.Intatto) return;
            p.sinceHurt = 0f; p.progress = 0; p.Refresh();
        }

        void Complete()
        {
            Active = false;
            completed++;
            int round = Mathf.Max(1, RoundDirector.Round);
            int pts = 400 + 60 * round;
            var gm = GameManager.I;
            gm?.AddPoints(pts);
            gm?.hud?.Banner("PROVA SUPERATA", $"{Label}: +{pts} punti");
            Sfx.UI(SoundBank.ChestReveal, 0.8f, 1.05f);
            // da soli o sull'ospite anche una goccia ai piedi (le gocce le decide l'ospite)
            if (!Coop.IsClient && gm != null && gm.player != null)
            {
                var kinds = new[] { DropKind.Ammo, DropKind.Double, DropKind.Insta };
                Drops.Spawn(kinds[Random.Range(0, kinds.Length)], gm.player.transform.position + gm.player.transform.forward * 1.2f + Vector3.up * 0.2f);
            }
            Refresh();
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] prova {kind} superata: +{pts}");
        }

        void Fail()
        {
            Active = false;
            GameManager.I?.hud?.Toast("Prova non superata.");
            Refresh();
        }
    }
}
