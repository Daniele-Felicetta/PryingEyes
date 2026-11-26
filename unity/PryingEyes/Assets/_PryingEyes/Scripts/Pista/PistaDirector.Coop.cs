using System.Collections.Generic;
using UnityEngine;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>The cooperative race. Every machine rides its own bike with its own physics (nobody waits for the network to steer) and sends its pose twenty times a second; the host rides the computer
    /// riders, relays everybody's pose to everybody, gives the go (Enter, when every friend has built the circuit) and says how the race ended. A friend is a Racer without a bike here, drawn easing
    /// toward the pose that arrives (PistaBikeView.Render) and ranked by the same rules as everybody (Race.Progress), so the order on the screen is the one on the map.
    /// The roster (who rides, with which colours, from which place on the grid) comes from the host: a friend builds the circuit from the seed, tells the host it is ready (FloorReady, the usual way
    /// of the cooperative game) and receives the roster in return. Somebody who joins a race on the grid takes the next place at the back and the roster is sent again; somebody who comes in after
    /// the start waits for the next race (CanWelcome). A friend who leaves retires; if the connection to the host is lost the race ends where it is.</summary>
    public sealed partial class PistaDirector
    {
        bool coopRace;                    // this race is played over the network (as the host or as a friend)
        bool rosterDone;                  // a friend's machine: the host's roster has been applied, the race exists
        bool overSent;                    // the host: the results of this race have been sent
        float poseT, statusT;
        readonly List<int> slotsTmp = new List<int>();
        readonly List<PistaNet.Entry> rosterTmp = new List<PistaNet.Entry>();
        readonly List<Race.Result> resultsTmp = new List<Race.Result>();
        readonly NetWriter nw = new NetWriter();
        /// <summary>The host: the last pose that came from each friend, as it came (it is relayed to the others as it is; what is drawn here is eased).</summary>
        readonly Dictionary<int, BikeView> lastPose = new Dictionary<int, BikeView>();

        /// <summary>The host may welcome a friend now: the race is built and on the grid (a race that has started makes the newcomer wait for the next one).</summary>
        public static bool CanWelcome { get { var d = I; return d != null && d.Ready && d.race != null && d.race.phase == RacePhase.Grid && !d.started; } }

        // ------------------------------------------------------------------ riders
        /// <summary>After the riders of a race have been made (here: forget the poses of the race before).</summary>
        void CoopRiders() { lastPose.Clear(); }

        /// <summary>The host: a friend of the game as a rider (no bike here: the poses come from their machine).</summary>
        Racer NewFriend(int slot)
        {
            var cg = Coop.Game;
            var r = race.AddRemote(cg != null ? cg.NameOf(slot) : "Giocatore " + (slot + 1), PistaMode.Bike, FriendSkin(slot, me != null ? me.skin : -1), true);
            r.netSlot = slot;
            return r;
        }

        Racer FriendOfSlot(int slot)
        {
            if (race == null) return null;
            for (int i = 0; i < race.racers.Count; i++) if (race.racers[i].friend && race.racers[i].netSlot == slot) return race.racers[i];
            return null;
        }

        void HideView(int id)
        {
            if (id >= 0 && id < views.Count && views[id] != null) views[id].gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ the host
        /// <summary>A friend has built the circuit of this race and waits for the roster. On the grid a friend who is not in the race yet takes the next place at the back.</summary>
        public void CoopClientReady(int slot)
        {
            var cg = Coop.Game;
            if (!coopRace || !Coop.IsHost || race == null || cg == null) return;
            bool added = false;
            if (FriendOfSlot(slot) == null)
            {
                if (race.phase != RacePhase.Grid || started) return;           // too late: the next race
                var r = NewFriend(slot);
                race.PlaceOnGrid(r, race.racers.Count - 1);
                if (Ready) AddView(r);
                hud.Banner(r.name + " e' in griglia", "", 2.4f);
                added = true;
            }
            SendRoster(slot);
            if (added) SendRosterToAll(slot);                                    // the others learn who has come
        }

        void SendRoster(int slot)
        {
            var cg = Coop.Game;
            if (cg == null || race == null) return;
            PistaNet.WriteRoster(nw.Reset(), cg.PistaEpoch, seed, race.laps, PistaMode.Bike, PistaMode.Aids, PistaMode.HourChoice, PistaMode.WeatherChoice, race.racers);
            cg.PistaSendSlot(slot, CoopKind.PistaRoster, nw.ToArray(), true);
        }

        void SendRosterToAll(int except)
        {
            var cg = Coop.Game;
            if (cg == null) return;
            cg.PistaFriends(slotsTmp);
            for (int i = 0; i < slotsTmp.Count; i++) if (slotsTmp[i] != except && cg.PistaReady(slotsTmp[i])) SendRoster(slotsTmp[i]);
        }

        /// <summary>A friend left the game: their rider retires and the others are told.</summary>
        public void CoopPeerLeft(int slot)
        {
            if (!coopRace || race == null) return;
            var r = FriendOfSlot(slot);
            if (r == null) return;
            race.Retire(r);
            HideView(r.id);
            lastPose.Remove(r.id);
            hud.Banner(r.name + " si e' ritirato", "", 2.4f);
            SendRosterToAll(-1);
        }

        /// <summary>A friend's pose, as it came.</summary>
        public void CoopOnPose(int from, ref NetReader r)
        {
            var cg = Coop.Game;
            if (!coopRace || race == null || cg == null || !Coop.IsHost) return;
            int ep = r.U8();
            int id; BikeView v;
            if (!PistaNet.ReadPose(ref r, out id, out v) || ep != cg.EpochByte) return;
            if (id < 0 || id >= race.racers.Count) return;
            var rc = race.racers[id];
            if (!rc.friend || rc.netSlot != from || rc.dnf) return;             // a friend can only move their own bike
            lastPose[id] = v;
            if (id < views.Count && views[id] != null) views[id].Receive(v);
        }

        /// <summary>The go: every friend has built the circuit (and the host has pressed Enter), or the automatic runs have the friends they expect.</summary>
        bool CoopCanStart()
        {
            var cg = Coop.Game;
            if (cg == null) return true;
            cg.PistaFriends(slotsTmp);
            for (int i = 0; i < slotsTmp.Count; i++) if (!cg.PistaReady(slotsTmp[i])) return false;      // somebody is still loading
            if (PistaMode.HasArg("-pistabot"))
            {
                int want = 0;
                PistaMode.TryArg("-pistacoopwait", out want);
                return slotsTmp.Count >= want && Time.timeSinceLevelLoad > 1f;
            }
            return input.startPressed;
        }

        void CoopCountdownStarted()
        {
            var cg = Coop.Game;
            if (!coopRace || !Coop.IsHost || cg == null) return;
            cg.PistaFriends(slotsTmp);
            for (int i = 0; i < slotsTmp.Count; i++)
                if (cg.PistaReady(slotsTmp[i])) cg.PistaSendSlot(slotsTmp[i], CoopKind.PistaStart, nw.Reset().U16(cg.PistaEpoch).ToArray(), true);
            hud.SetStatus("");
        }

        void SendResults()
        {
            var cg = Coop.Game;
            if (cg == null || race == null) return;
            race.CollectResults(resultsTmp);
            PistaNet.WriteResults(nw.Reset(), cg.PistaEpoch, resultsTmp);
            byte[] data = nw.ToArray();
            cg.PistaFriends(slotsTmp);
            for (int i = 0; i < slotsTmp.Count; i++) if (cg.PistaReady(slotsTmp[i])) cg.PistaSendSlot(slotsTmp[i], CoopKind.PistaOver, data, true);
        }

        /// <summary>The host: everybody's pose, to every friend (all but their own bike): the other friends as they came, the computer riders and the host as they are here.</summary>
        void RelayPoses()
        {
            var cg = Coop.Game;
            if (cg == null || race == null) return;
            cg.PistaFriends(slotsTmp);
            for (int k = 0; k < slotsTmp.Count; k++)
            {
                int slot = slotsTmp[k];
                if (!cg.PistaReady(slot)) continue;
                int n = 0;
                for (int i = 0; i < race.racers.Count; i++) { var rc = race.racers[i]; if (rc.netSlot != slot && !rc.dnf) n++; }
                nw.Reset().U8(cg.EpochByte).U8(n);
                for (int i = 0; i < race.racers.Count; i++)
                {
                    var rc = race.racers[i];
                    if (rc.netSlot == slot || rc.dnf) continue;
                    BikeView v;
                    if (rc.bike != null) v = MakeView(rc);
                    else if (!lastPose.TryGetValue(rc.id, out v)) v = RemotePose(rc);
                    PistaNet.WritePose(nw, rc.id, v);
                }
                cg.PistaSendSlot(slot, CoopKind.PistaPoses, nw.ToArray(), false);
            }
        }

        // ------------------------------------------------------------------ a friend
        void SendMyPose()
        {
            var cg = Coop.Game;
            if (cg == null || race == null || me == null || me.bike == null || !rosterDone) return;
            nw.Reset().U8(cg.EpochByte);
            PistaNet.WritePose(nw, me.id, MakeView(me));
            cg.PistaSendHost(CoopKind.PistaPose, nw.ToArray(), false);
        }

        /// <summary>The host has started another race: build it again (the same circuit or another) and say when it is built.</summary>
        public void CoopNewRace(int newSeed)
        {
            if (Coop.IsHost) return;
            StopAllCoroutines();
            StartCoroutine(SetupRace(newSeed, true));
        }

        /// <summary>A message of the host about the race: who rides, the go, the poses of the others, the results.</summary>
        public void CoopOnMessage(CoopKind kind, ref NetReader r)
        {
            var cg = Coop.Game;
            if (Coop.IsHost || cg == null) return;
            switch (kind)
            {
                case CoopKind.PistaRoster: OnRoster(ref r); break;
                case CoopKind.PistaStart:
                {
                    int ep = r.U16();
                    if (!r.Ok || ep != cg.PistaEpoch || race == null || race.phase != RacePhase.Grid || started) break;
                    started = true;
                    race.StartCountdown();
                    lastLights = 0;
                    hud.SetStatus("");
                    hud.Banner("PRONTI", circuit.name, 2.2f);
                    break;
                }
                case CoopKind.PistaPoses:
                {
                    int ep = r.U8(), n = r.U8();
                    if (!r.Ok || race == null || ep != cg.EpochByte) break;
                    for (int i = 0; i < n; i++)
                    {
                        int id; BikeView v;
                        if (!PistaNet.ReadPose(ref r, out id, out v)) break;
                        if (id < 0 || id >= race.racers.Count || id >= views.Count) continue;
                        var rc = race.racers[id];
                        if (!rc.remote || rc.dnf || views[id] == null) continue;
                        views[id].Receive(v);
                    }
                    break;
                }
                case CoopKind.PistaOver:
                {
                    int ep;
                    if (!PistaNet.ReadResults(ref r, out ep, resultsTmp) || ep != cg.PistaEpoch || race == null) break;
                    race.ApplyResults(resultsTmp);
                    break;
                }
            }
        }

        /// <summary>The roster of the race: the first time, the race is made here (the rules of the host, the riders in the host's order, the places on the grid); later, riders who have joined or left.</summary>
        void OnRoster(ref NetReader r)
        {
            var cg = Coop.Game;
            int epoch, rseed, laps, bike, aids, hour, weather;
            if (!PistaNet.ReadRoster(ref r, out epoch, out rseed, out laps, out bike, out aids, out hour, out weather, rosterTmp)) return;
            if (cg == null || epoch != cg.PistaEpoch || circuit == null) return;
            if (rseed != seed) { CoopNewRace(rseed); return; }                       // not the circuit that was built: build the one of the host
            if (race == null)
            {
                if (world == null || !world.Ready) return;
                PistaMode.Override(laps, bike, aids, hour, weather);
                circuit.wet = PistaMode.Rain ? 1f : 0f;                      // the host's weather: before the race is made, so that the plan and the bikes know the road
                ApplySky();
                race = new Race(circuit, laps, seed) { externalEnd = true };
                for (int i = 0; i < rosterTmp.Count; i++) FriendRacer(rosterTmp[i]);
                for (int i = 0; i < rosterTmp.Count; i++) race.PlaceOnGrid(race.racers[i], rosterTmp[i].grid);
                HookRace();
                rosterDone = true;
                return;
            }
            for (int i = race.racers.Count; i < rosterTmp.Count; i++)
            {
                var rc = FriendRacer(rosterTmp[i]);
                race.PlaceOnGrid(rc, rosterTmp[i].grid);
                if (Ready) AddView(rc);
                hud.Banner(rc.name + " e' in griglia", "", 2.4f);
            }
            for (int i = 0; i < rosterTmp.Count && i < race.racers.Count; i++)
                if (rosterTmp[i].Dnf && !race.racers[i].dnf) { race.Retire(race.racers[i]); HideView(i); hud.Banner(race.racers[i].name + " si e' ritirato", "", 2.4f); }
        }

        /// <summary>A rider of the host's roster as it is here: me with my own bike, everybody else a pose that arrives.</summary>
        Racer FriendRacer(PistaNet.Entry e)
        {
            Racer rc;
            if (e.kind == 0 && e.netSlot == Coop.Slot)
            {
                rc = race.AddHuman(e.name, e.bike, e.skin, seed + 5);
                PistaMode.ApplyAids(rc.bike, PistaMode.Aids);
                me = rc;
            }
            else rc = race.AddRemote(e.name, e.bike, e.skin, e.kind == 0);
            rc.netSlot = e.netSlot;
            return rc;
        }

        // ------------------------------------------------------------------ every step and every frame
        /// <summary>After every step of the physics: the riders who are somebody else's are where their poses say (as they are drawn), so the rules see them like everybody.</summary>
        void CoopStep()
        {
            if (!coopRace || race == null) return;
            int n = Mathf.Min(race.racers.Count, views.Count);
            for (int i = 0; i < n; i++)
            {
                var rc = race.racers[i];
                if (!rc.remote || views[i] == null || !views[i].hasState) continue;
                var s = views[i].shown;
                race.SetRemote(rc, s.x, s.y, s.z, s.heading, s.speed, s.lean, s.crashed);
            }
        }

        void CoopUpdate(float dt)
        {
            if (!coopRace || race == null) return;
            if (!Coop.Active) { CoopLost(); return; }
            poseT -= Time.unscaledDeltaTime;
            if (poseT <= 0f)
            {
                poseT = 0.05f;
                if (Coop.IsClient) SendMyPose(); else RelayPoses();
            }
            if (Coop.IsHost && race.phase == RacePhase.Over && !overSent) { overSent = true; SendResults(); }
            statusT -= Time.unscaledDeltaTime;
            if (statusT <= 0f) { statusT = 0.4f; UpdateStatus(); }
        }

        /// <summary>What the grid is waiting for, in a line at the bottom of the screen.</summary>
        void UpdateStatus()
        {
            string s = "";
            var cg = Coop.Game;
            if (race != null && race.phase == RacePhase.Grid && !started && cg != null)
            {
                if (Coop.IsHost)
                {
                    cg.PistaFriends(slotsTmp);
                    string waiting = "";
                    for (int i = 0; i < slotsTmp.Count; i++) if (!cg.PistaReady(slotsTmp[i])) waiting += (waiting.Length > 0 ? ", " : "") + cg.NameOf(slotsTmp[i]);
                    s = waiting.Length > 0 ? "In attesa di " + waiting + "..." : slotsTmp.Count > 0 ? "Tutti pronti   -   INVIO (A) per partire" : "In attesa dei compagni   -   INVIO (A) per partire da solo";
                }
                else s = "In attesa che " + cg.NameOf(0) + " dia il via";
            }
            hud.SetStatus(s);
        }

        /// <summary>The connection is gone: the race ends where it is (nobody will say how it would have ended), and a new one can be started alone.</summary>
        void CoopLost()
        {
            coopRace = false;
            hud.SetStatus("");
            if (race == null) return;
            race.externalEnd = false;
            for (int i = 0; i < race.racers.Count; i++)
            {
                var rc = race.racers[i];
                if (rc.remote) { race.Retire(rc); HideView(i); }      // nobody sends poses any more: the friends are gone, and the computer riders of a friend's machine were the host's
            }
            bool running = race.phase == RacePhase.Racing || race.phase == RacePhase.Countdown;
            hud.Banner("Connessione persa", running ? "la gara finisce qui" : "si parte da soli", 3f, new Color(0.96f, 0.24f, 0.18f));
            if (running) race.ForceOver();
        }
    }
}
