#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using PryingEyes.Testing;
using UnityEngine;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>-pistacooptest, in two processes (tools/coop/run_coop.ps1: the host with -coophost, the friend with -coopjoin, both -autotest -nographics): the cooperative race. The host chooses the track mode
    /// (-pista) and gives the go by itself when the friend it expects (-pistacoopwait 1) has built the circuit; both ride their bikes with the bot (-pistabot) through one lap against a few computer riders
    /// (-pistarivals N, ridden by the host). The friend checks that it has the circuit of the host's seed and the rules of the host (laps), the roster (itself, the host as a friend, the computer riders as
    /// riders without a bike here), that the go comes from the host, that everybody moves (poses arriving), and that the race ends with the host's results (the order the host gave, the same on both sides).
    /// The host checks that the friend takes its place on the grid, moves, and finishes. Both exit 0 when every check holds.
    /// <c>-pistacoopscenario</c> picks what happens after the first race: <c>race</c> (default) stops there; <c>again</c> has the host start another race, built by both, and the friend leave while it is on
    /// (the host checks that the friend retires and the race goes on); <c>hostquits</c> has the host leave in the middle of the race (the friend checks that the race ends where it is, "Connessione persa",
    /// and that it can start a race of its own).</summary>
    public sealed class PistaCoopTest : PlayerTest
    {
        bool prepOk;
        bool hostHere;                    // this process is the host (set once, so that it still holds when the connection is gone)
        int cueFrames, cueApproach, cueBrake, cueMismatch, cueWrong;          // the braking sign, at every frame of the race

        protected override string Suite { get { return "pistacooptest"; } }
        protected override string Role { get { return hostHere ? "ospite" : "ospitato"; } }
        protected override string OutFolder { get { return "pistacooptest-" + Role; } }          // the two processes do not write each other's result
        protected override float TimeLimit { get { return 420f; } }
        protected override string ExceptionCheck { get { return "nessun errore o eccezione nel registro durante la prova"; } }
        protected override bool IsError(string message, LogType type) { return type == LogType.Exception || type == LogType.Error; }
        protected override void Finish() { Time.timeScale = 1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<PistaCoopTest>("-pistacooptest"); }

        static PistaDirector Dir => PistaDirector.I;

        void Update()
        {
            if (Coop.IsHost) hostHere = true;
            SampleCue();
        }

        /// <summary>One frame of the race: the braking sign on the dashboard against the driving line it is made from.</summary>
        void SampleCue()
        {
            var d = Dir;
            if (d == null || !d.Ready || d.race == null || d.me == null || d.hud == null || d.guide == null || d.race.phase != RacePhase.Racing) return;
            var g = d.guide;
            cueFrames++;
            bool silenced = d.me.finished || (d.me.bike != null && d.me.bike.crashed);
            var shown = d.hud.CueNow;
            if (shown != (silenced ? CueKind.None : g.cue.kind)) cueMismatch++;
            bool fits = g.cue.kind == CueKind.None ? !(g.here == Advice.Brake && DrivingLine.Shows(PistaMode.Aids, Advice.Brake))
                : g.cue.kind == CueKind.Brake ? g.here == Advice.Brake : g.here != Advice.Brake && g.redAhead > 0f;
            if (!fits) cueWrong++;
            if (shown == CueKind.Approach) cueApproach++; else if (shown == CueKind.Brake) cueBrake++;
        }

        IEnumerator WaitFor(Func<bool> cond, float seconds)
        {
            float t = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t < seconds) yield return null;
        }

        static int ArgInt(string name, int def) { int v; return PistaMode.TryArg(name, out v) ? v : def; }

        static string ArgStr(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        // ------------------------------------------------------------------ the scenarios
        protected override IEnumerator Run()
        {
            string scenario = ArgStr("-pistacoopscenario", "race");
            foreach (var y in Prepare(1)) yield return y;
            if (!prepOk) yield break;
            var d = Dir;

            if (scenario == "hostquits")
            {
                if (hostHere)
                {
                    // leave in the middle of the race, without a word: the friend has to cope
                    yield return new WaitForSeconds(6f);
                    Check("l'ospite se ne va con la gara in corso", d.race.phase == RacePhase.Racing, d.race.phase.ToString());
                    yield break;
                }
                yield return WaitFor(() => d.race.phase == RacePhase.Over, 60f);
                Check("la connessione persa chiude la gara dove e'", d.race.phase == RacePhase.Over && !Coop.Active, d.race.phase + (Coop.Active ? ", in rete" : ", fuori rete"));
                yield return WaitFor(() => d.Resulting, 10f);
                Check("la classifica compare lo stesso", d.Resulting);
                Check("nessuno e' ancora in pista in rete: i piloti degli altri sono ritirati", d.race.racers.TrueForAll(r => !r.remote || r.dnf));
                Check("non aspetto piu' la fine decisa da altri", !d.race.externalEnd);
                // and a race of its own can start
                int count = d.RaceCount;
                d.RestartRace(false);
                yield return WaitFor(() => d.RaceCount > count && d.Ready, 90f);
                if (!Check("una nuova gara da solo e' costruita", d.RaceCount > count && d.Ready && d.race != null && d.me != null, "gare " + d.RaceCount)) yield break;
                yield return WaitFor(() => d.race.phase == RacePhase.Racing, 40f);
                Check("parte da sola, senza aspettare nessuno", d.race.phase == RacePhase.Racing, d.race.phase.ToString());
                yield return WaitFor(() => d.me.progress > 120f, 40f);
                Check("e la mia moto va", d.me.progress > 120f, d.me.progress.ToString("F0") + " m");
                yield break;
            }

            foreach (var y in Finishing()) yield return y;
            if (scenario == "again")
            {
                int count = d.RaceCount;
                if (hostHere)
                {
                    yield return new WaitForSeconds(2f);
                    d.RestartRace(false);                                   // "ancora": the same circuit, a new race for everybody
                }
                yield return WaitFor(() => d.RaceCount > count && d.Ready, 120f);
                if (!Check("la seconda gara e' costruita da tutti e due", d.RaceCount == count + 1 && d.Ready && d.race != null, "gare " + d.RaceCount)) yield break;
                Check("la seconda gara e' una gara nuova: nessuno ha gia' finito", d.race.racers.TrueForAll(r => !r.finished) && !d.Resulting);
                foreach (var y in Prepare(2)) yield return y;
                if (!prepOk) yield break;
                if (!hostHere) yield break;                                  // the friend leaves while the race is on: the host checks what happens
                var friend = d.race.racers.Find(r => r.friend);
                yield return WaitFor(() => friend != null && friend.dnf, 25f);
                Check("il compagno che se ne va si ritira", friend != null && friend.dnf);
                Check("la sua moto sparisce dalla scena", friend != null && friend.id < d.views.Count && !d.views[friend.id].gameObject.activeSelf);
                Check("la gara continua per gli altri", d.race.phase == RacePhase.Racing, d.race.phase.ToString());
                Check("la classifica resta valida", ValidStandings(d.race, true));
                yield break;
            }
            // the host stays long enough for the friend to have the results and check them
            yield return new WaitForSeconds(hostHere ? 8f : 2f);
        }

        /// <summary>A built race, its roster, the go from the host, everybody moving. 'n' is the number of the race in this game (the first is 1).</summary>
        IEnumerable Prepare(int n)
        {
            prepOk = false;
            int expectSeed = ArgInt("-pistacoopseed", 0);
            int rivals = ArgInt("-pistarivals", 11);
            yield return WaitFor(() => Dir != null && Dir.Ready && Dir.RaceCount >= n, 240f);
            if (Coop.IsHost) hostHere = true;
            bool host = hostHere;
            var d = Dir;
            if (!Check("la gara " + n + " e' costruita e pronta", d != null && d.Ready && PistaMode.Active && d.race != null && d.me != null, d == null ? "nessun regista" : "pronto " + d.Ready)) yield break;
            Check("la partita e' in rete e su pista", Coop.Active && (host ? Coop.IsHost : Coop.IsClient), host ? "ospite" : "ospitato");
            if (expectSeed != 0) Check("il circuito e' quello del seme dell'ospite", d.seed == expectSeed, d.seed + " (atteso " + expectSeed + ")");
            int expectRain = ArgInt("-pistacooprain", -1);
            if (expectRain >= 0)
                Check("il tempo e' quello dell'ospite (" + (expectRain == 1 ? "piove" : "sereno") + "), su questa macchina come sull'altra: il circuito, la scena e il terreno", PistaMode.WeatherChoice == expectRain && d.circuit.wet == (float)expectRain && d.RainOn == (expectRain == 1) && Shader.GetGlobalFloat("_PistaWet") == (float)expectRain,
                    "tempo " + PistaMode.WeatherChoice + ", acqua " + d.circuit.wet + ", pioggia " + d.RainOn + " (dell'ospite " + expectRain + ")");
            if (n == 1)
            {
                var pl = GameManager.I != null ? GameManager.I.player : null;
                Check("nessun compagno a piedi: su pista le moto sono i compagni", pl == null || !pl.gameObject.activeSelf);
                Check("nessun pupazzo di compagno creato", Coop.Game != null && Coop.Game.Players.Count == 0, Coop.Game != null ? Coop.Game.Players.Count.ToString() : "-");
            }

            // ---- the roster
            if (host)
            {
                yield return WaitFor(() => d.race.racers.Exists(r => r.friend && !r.dnf), 150f);
                var friend = d.race.racers.Find(r => r.friend);
                if (!Check("l'ospitato e' entrato in griglia", friend != null, friend != null ? friend.name + ", posto " + friend.slot : "nessuno")) yield break;
                Check("e' l'ospitato (slot di rete 1), senza una moto simulata qui", friend.netSlot == 1 && friend.remote && friend.bike == null && !friend.cpu, "slot " + friend.netSlot);
                Check("tutti i piloti: io, gli avversari e il compagno", d.race.racers.Count == rivals + 2, d.race.racers.Count + " su " + (rivals + 2));
                Check("le moto nella scena sono una per pilota", d.views.Count == d.race.racers.Count && d.views.TrueForAll(v => v != null && v.rig != null), d.views.Count + " moto");
                Check("la griglia dell'ospitato e' libera e distinta dalle altre", d.race.racers.FindAll(r => r.slot == friend.slot).Count == 1, "posto " + friend.slot);
            }
            else
            {
                Check("io sono lo slot dell'ospitato e ho la mia moto", d.me.netSlot == Coop.Slot && Coop.Slot >= 1 && d.me.bike != null && d.me.human && !d.me.remote, "slot " + Coop.Slot);
                Check("tutti i piloti dell'ospite sono qui: io, il suo, i piloti del computer", d.race.racers.Count == rivals + 2, d.race.racers.Count + " su " + (rivals + 2));
                var hostRider = d.race.racers.Find(r => r.netSlot == 0);
                Check("l'ospite e' un compagno senza moto qui", hostRider != null && hostRider.friend && hostRider.remote && hostRider.bike == null, hostRider != null ? hostRider.name : "assente");
                int cpus = d.race.racers.FindAll(r => r.cpu && r.remote && r.bike == null).Count;
                Check("i piloti del computer sono pose che arrivano", cpus == rivals, cpus + " su " + rivals);
                Check("le regole sono quelle dell'ospite (giri), non le mie", d.race.laps == ArgInt("-pistacooplaps", 1) && d.race.externalEnd, d.race.laps + " giri");
                Check("le moto nella scena sono una per pilota", d.views.Count == d.race.racers.Count && d.views.TrueForAll(v => v != null && v.rig != null), d.views.Count + " moto");
                Check("la fine della gara non la decido io", d.race.externalEnd);
            }

            // ---- the go
            yield return WaitFor(() => d.race.phase != RacePhase.Grid, 120f);
            Check("la gara parte (il via lo da' l'ospite)", d.race.phase == RacePhase.Countdown || d.race.phase == RacePhase.Racing, d.race.phase.ToString());
            yield return WaitFor(() => d.race.phase == RacePhase.Racing, 40f);
            if (!Check("i semafori si spengono e si corre", d.race.phase == RacePhase.Racing)) yield break;

            // ---- everybody moves
            yield return WaitFor(() => d.race.racers.TrueForAll(r => r.dnf || r.progress > 120f), 70f);
            string moved = "";
            foreach (var r in d.race.racers) if (!r.dnf && r.progress <= 120f) moved += r.name + " " + r.progress.ToString("F0") + " m; ";
            Check("tutti i piloti, miei, dei compagni e del computer, avanzano sul circuito", moved.Length == 0, moved.Length == 0 ? "" : "fermi: " + moved);
            Check("le posizioni sono una classifica vera", ValidStandings(d.race, false), "io " + d.me.position + "°");
            prepOk = moved.Length == 0;
        }

        /// <summary>The end of a race: the finish line, the results on both screens, the host's order on the friend's.</summary>
        IEnumerable Finishing()
        {
            var d = Dir;
            bool host = hostHere;
            float t = Time.realtimeSinceStartup;
            yield return WaitFor(() => d.race.phase == RacePhase.Over, 220f);
            if (!Check("la gara finisce", d.race.phase == RacePhase.Over, d.race.phase + " dopo " + (Time.realtimeSinceStartup - t).ToString("F0") + " s")) yield break;
            yield return WaitFor(() => d.Resulting, 10f);
            Check("la classifica e' sullo schermo di tutti e due", d.Resulting);
            if (!host)
            {
                // the end is the host's: wait for its results to arrive (they come a moment after the host's own end)
                yield return WaitFor(() => d.race.racers.TrueForAll(r => r.final >= 1), 15f);
            }
            Check("io ho tagliato il traguardo", d.me.finished && d.me.finishTime > 20f, d.me.finishTime.ToString("F2") + " s");
            string unfinished = "";
            foreach (var r in d.race.racers) if ((r.human || r.friend) && !r.dnf && !r.finished) unfinished += r.name + " ";
            Check("i piloti veri, io e i compagni, hanno tutti finito", unfinished.Length == 0, unfinished);
            Check("la classifica e' valida", ValidStandings(d.race, false), "io " + d.me.position + "°");
            if (!host) Check("l'ordine e' quello che ha detto l'ospite", d.race.racers.TrueForAll(r => r.final >= 1) && InOrder(d.race), Order(d.race));
            // ---- the driving line and the braking sign of this machine: each builds its own from the same racing line, with the aids of the HOST's game (the host chose them, the friend has its own choice and must not use it)
            int expectAids = ArgInt("-pistacoopaids", -1);
            if (expectAids >= 0)
            {
                var g = d.guide;
                Check("gli aiuti di questa macchina sono quelli dell'ospite, non quelli scelti qui", PistaMode.Aids == expectAids, "aiuti " + PistaMode.Aids + " (dell'ospite " + expectAids + ")");
                Check("la linea di corsa c'e' su questa macchina con gli aiuti dell'ospite (solo il rosso con Sportivi)", expectAids >= 2 ? g == null : g != null && g.frames > 500 && (expectAids != 1 || g.shown[0] + g.shown[1] == 0), g == null ? "nessuna linea" : g.frames + " fotogrammi, " + g.redFrames + " con il rosso");
                Check("l'indicatore di frenata di questa macchina conta alla rovescia e dice quello che dice la sua linea, a ogni fotogramma", expectAids >= 2 ? cueApproach + cueBrake == 0 : cueMismatch == 0 && cueWrong == 0 && cueApproach > 200,
                    cueFrames + " fotogrammi di gara: " + cueApproach + " con il conto alla rovescia, " + cueBrake + " con 'frena', " + cueMismatch + " diverso dalla linea, " + cueWrong + " incoerente");
            }
            Debug.Log("[PryingEyes] pistacooptest classifica " + d.circuit.name + ": " + Order(d.race));
        }

        static bool ValidStandings(Race race, bool allowRetired)
        {
            var seen = new HashSet<int>();
            foreach (var r in race.racers)
            {
                if (allowRetired && r.dnf) continue;
                if (r.position < 1 || r.position > race.racers.Count || !seen.Add(r.position)) return false;
            }
            return true;
        }

        static bool InOrder(Race race)
        {
            for (int i = 0; i < race.standings.Count; i++) if (race.standings[i].final != i + 1) return false;
            return true;
        }

        static string Order(Race race)
        {
            var s = "";
            foreach (var r in race.standings) s += r.position + "." + r.name + (r.finished ? " " + r.finishTime.ToString("F1") : r.dnf ? " rit" : "") + (r.final >= 0 ? "[" + r.final + "]" : "") + "  ";
            return s;
        }
    }
}
#endif
