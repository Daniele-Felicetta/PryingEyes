using System;
using System.Collections.Generic;
using PryingEyes.Pista;

static partial class Program
{
    /// <summary>The host's race with a friend as a pose: the host's own rider (driven by the line follower), two computer riders and a friend whose bike is simulated "on his machine" (a second model driven
    /// by another line follower) and whose pose is fed in every step, like the network does. Everything runs in the same files the game compiles.</summary>
    sealed class CoopRig
    {
        public const float Dt = 1f / 60f;
        public readonly Race host;
        public readonly Racer me, ai1, ai2, friend;
        public readonly BikeModel friendBike;
        readonly BikeDriver friendDriver, myDriver;
        public bool feedFriend = true;
        public int overs;

        public CoopRig(Circuit c, int laps, float friendPace)
        {
            host = new Race(c, laps, 11);
            me = host.AddHuman("Host", 0, 0, 5); me.netSlot = 0;
            ai1 = host.AddAi("Ferri", 1, 2, 7, 0.93f, 0.05f);
            ai2 = host.AddAi("Longo", 0, 3, 9, 0.90f, 0.05f);
            friend = host.AddRemote("Friend", 0, 4, true); friend.netSlot = 1;
            host.PlaceAll(new List<Racer> { ai1, me, ai2, friend });
            friendBike = new BikeModel(Bikes.Get(0), 21);
            float gx, gz, gy, gh;
            c.GridSlot(friend.slot, out gx, out gz, out gy, out gh);
            friendBike.Reset(gx, gz, gh, gy);
            friendDriver = new BikeDriver(friendBike, c, host.LineFor(0), 33) { pace = friendPace, mistakes = 0f };
            friendDriver.Reset();
            myDriver = new BikeDriver(me.bike, c, host.LineFor(0), 35) { pace = 0.97f, mistakes = 0f };
            myDriver.Reset();
            host.OnOver = () => overs++;
        }

        public void Step()
        {
            if (host.phase == RacePhase.Racing)
            {
                me.input = myDriver.Drive(Dt);
                if (feedFriend) friendBike.Step(Dt, host.circuit, friendDriver.Drive(Dt));
            }
            host.Step(Dt);
            if (feedFriend) host.SetRemote(friend, friendBike.x, friendBike.y, friendBike.z, friendBike.heading, Math.Max(friendBike.u, 0f), friendBike.lean, friendBike.crashed);
        }
    }

    /// <summary>A friend's machine seen from outside: every rider arrives as a pose (in the roster order of the host), the end of the race is the host's word.</summary>
    sealed class Mirror
    {
        public readonly Race race;
        public int overs;

        public Mirror(Race host, Circuit c, int laps)
        {
            race = new Race(c, laps, 11) { externalEnd = true };
            race.OnOver = () => overs++;
            foreach (var r in host.racers) race.AddRemote(r.name, r.bikeIndex, r.skin, r.human || r.friend);
            for (int i = 0; i < host.racers.Count; i++) { race.racers[i].netSlot = host.racers[i].netSlot; race.PlaceOnGrid(race.racers[i], host.racers[i].slot); }
        }

        /// <summary>One step, then the poses of the host's riders as they are after the host's step (what arrives over the network).</summary>
        public void Step(Race host)
        {
            race.Step(CoopRig.Dt);
            for (int i = 0; i < host.racers.Count; i++)
            {
                var h = host.racers[i];
                if (h.dnf) continue;
                race.SetRemote(race.racers[i], h.x, h.y, h.z, h.heading, h.speed, h.lean, h.crashed);
            }
        }
    }

    static void CoopChecks()
    {
        var c = LabCircuits()[2];

        // ---- the race waits for a friend who is slower: it ends when the people have finished, not when the host has
        {
            var rig = new CoopRig(c, 1, 0.88f);
            var mirror = new Mirror(rig.host, c, 1);
            rig.host.StartCountdown(); mirror.race.StartCountdown();
            float t = 0f, overAt = -1f;
            while (rig.host.phase != RacePhase.Over && t < 500f)
            {
                rig.Step(); mirror.Step(rig.host); t += CoopRig.Dt;
                if (rig.host.phase == RacePhase.Over && overAt < 0f) overAt = rig.host.time;
            }
            var me = rig.me; var fr = rig.friend;
            bool bothFinished = me.finished && fr.finished;
            bool waited = bothFinished && me.finishTime < fr.finishTime && overAt >= fr.finishTime - 0.05f;
            Check("COOP_HOST_WAITS_FOR_A_FRIEND", rig.host.phase == RacePhase.Over && waited && rig.overs == 1,
                "host finished at " + me.finishTime.ToString("0.0") + " s, the friend (a pose, 12 % slower) at " + fr.finishTime.ToString("0.0") + " s, race over at " + overAt.ToString("0.0") + " s, OnOver fired " + rig.overs + " time(s)",
                "over only after both people finished, once");
            Check("COOP_REMOTE_PROGRESS_FROM_POSES", fr.progress >= c.length && fr.lap >= 1 && fr.position >= me.position,
                "the friend's rider has ridden " + fr.progress.ToString("0") + " m of " + c.length.ToString("0") + " from poses alone, place " + fr.position + " vs the host's " + me.position,
                "a full lap from poses, behind the faster host");
            bool orderOk = true;
            for (int i = 1; i < rig.host.standings.Count; i++)
            {
                var a = rig.host.standings[i - 1]; var b = rig.host.standings[i];
                if (a.finished && b.finished && a.finishTime > b.finishTime + 0.05f) orderOk = false;
                if (!a.finished && b.finished) orderOk = false;
            }
            Check("COOP_STANDINGS_BY_THE_SAME_RULES", orderOk, "order " + string.Join(" ", rig.host.standings.ConvertAll(r => r.name + (r.finished ? "(" + r.finishTime.ToString("0.0") + ")" : "(" + r.progress.ToString("0") + " m)"))), "finishers by time, then the others by distance");

            // the mirror: its own rules never end the race; the host's word does, and it is the same everywhere
            bool selfOver = mirror.race.phase == RacePhase.Over;
            for (int k = 0; k < 600; k++) mirror.Step(rig.host);       // ten more seconds
            selfOver |= mirror.race.phase == RacePhase.Over;
            var results = new List<Race.Result>();
            rig.host.CollectResults(results);
            mirror.race.ApplyResults(results);
            bool same = mirror.race.phase == RacePhase.Over && mirror.overs == 1;
            for (int i = 0; i < rig.host.standings.Count; i++)
            {
                var h = rig.host.standings[i]; var m = mirror.race.standings[i];
                same &= h.id == m.id && h.finished == m.finished && h.dnf == m.dnf && Math.Abs(h.finishTime - m.finishTime) < 1e-4f && m.final == h.position && m.position == h.position;
            }
            Check("COOP_END_IS_THE_HOSTS_WORD", !selfOver && same, "the friend's own rules ended the race: " + selfOver + "; after the host's results the order is the host's: " + same + ", OnOver fired " + mirror.overs + " time(s)", "never over by itself, then identical to the host");

            // the host's word wins even against what the pose of a computer rider says here
            var swapped = new List<Race.Result>(results);
            int a2 = rig.host.standings.Count - 2, b2 = rig.host.standings.Count - 1;
            int idA = rig.host.standings[a2].id, idB = rig.host.standings[b2].id;
            for (int i = 0; i < swapped.Count; i++)
            {
                var e = swapped[i];
                if (e.id == idA) e.place = b2 + 1; else if (e.id == idB) e.place = a2 + 1;
                swapped[i] = e;
            }
            mirror.race.ApplyResults(swapped);
            Check("COOP_HOSTS_ORDER_BEATS_LOCAL_ORDER", mirror.race.standings[a2].id == idB && mirror.race.standings[b2].id == idA && mirror.overs == 1,
                "the last two riders swapped by the host: " + mirror.race.standings[a2].name + " now before " + mirror.race.standings[b2].name + ", OnOver still fired " + mirror.overs + " time(s)", "the order follows the host, the end is announced once");
        }

        // ---- a friend leaves in the middle of the race
        {
            var rig = new CoopRig(c, 1, 0.92f);
            var mirror = new Mirror(rig.host, c, 1);
            rig.host.StartCountdown(); mirror.race.StartCountdown();
            float t = 0f, retiredAt = -1f;
            while (rig.host.phase != RacePhase.Over && t < 500f)
            {
                if (retiredAt < 0f && rig.host.phase == RacePhase.Racing && rig.host.time > 25f) { rig.host.Retire(rig.friend); rig.feedFriend = false; retiredAt = rig.host.time; }
                rig.Step(); mirror.Step(rig.host); t += CoopRig.Dt;
            }
            var me = rig.me; var fr = rig.friend;
            Check("COOP_LEAVER_DOES_NOT_BLOCK_THE_END", rig.host.phase == RacePhase.Over && me.finished && fr.dnf && !fr.finished && rig.host.time <= me.finishTime + 0.5f,
                "the friend left at " + retiredAt.ToString("0.0") + " s; the host finished at " + me.finishTime.ToString("0.0") + " s and the race was over at " + rig.host.time.ToString("0.0") + " s",
                "the race ends when the host finishes, without waiting for the rider who left");
            Check("COOP_LEAVER_IS_LAST", fr.position == rig.host.racers.Count && rig.host.standings[rig.host.standings.Count - 1] == fr,
                "the friend who left is " + fr.position + " of " + rig.host.racers.Count + " although he had ridden " + fr.progress.ToString("0") + " m", "last, whatever his distance");
            var results = new List<Race.Result>();
            rig.host.CollectResults(results);
            mirror.race.ApplyResults(results);
            var mfr = mirror.race.racers[fr.id];
            Check("COOP_LEAVER_IS_RETIRED_EVERYWHERE", mfr.dnf && mirror.race.standings[mirror.race.standings.Count - 1] == mfr, "on the other machine the rider who left is " + (mfr.dnf ? "retired" : "still riding") + " and " + mfr.position + " of " + mirror.race.racers.Count, "retired and last on every machine");
        }

        // ---- a newcomer on the grid, a lost connection
        {
            var race = new Race(c, 2, 3);
            var me = race.AddHuman("Host", 0, 0, 5);
            var ai = race.AddAi("Ferri", 1, 2, 7, 0.93f, 0.05f);
            race.PlaceAll(new List<Racer> { ai, me });
            var late = race.AddRemote("Late", 0, 5, true);
            race.PlaceOnGrid(late, race.racers.Count - 1);
            float gx, gz, gy, gh; c.GridSlot(late.slot, out gx, out gz, out gy, out gh);
            Check("COOP_NEWCOMER_TAKES_THE_NEXT_PLACE", late.slot == 2 && late.slot != ai.slot && late.slot != me.slot && Math.Abs(late.x - gx) < 1e-4f && Math.Abs(late.z - gz) < 1e-4f && late.progress < 0f,
                "a friend who joins on the grid sits in place " + late.slot + " behind the others, " + (-late.progress).ToString("0") + " m before the line", "a free place at the back, behind the line");
            int overs = 0; race.OnOver = () => overs++;
            race.StartCountdown();
            for (int i = 0; i < 60 * 12 && race.phase != RacePhase.Racing; i++) race.Step(CoopRig.Dt);
            for (int i = 0; i < 300; i++) race.Step(CoopRig.Dt);
            race.ForceOver(); race.ForceOver();
            Check("COOP_LOST_CONNECTION_ENDS_THE_RACE", race.phase == RacePhase.Over && overs == 1, "forced over while riding: " + race.phase + ", OnOver fired " + overs + " time(s)", "over at once, announced once");
        }
    }
}
