using System.Collections;
using System.Collections.Generic;
using PryingEyes.Deserto;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>Setting up a race and taking it down: the circuit from the seed, the rules and the riders (the player, the computer riders with their pace and their bikes, in a cooperative game the friends),
    /// the scenery built a few pieces per frame behind the loading screen, the bikes in the scene, the grid, the map and the first flight of the camera. On a friend's machine the circuit and the scenery
    /// are made from the seed, and the riders from the roster the host sends (PistaDirector.Coop.cs).</summary>
    public sealed partial class PistaDirector
    {
        internal static readonly string[] Names =
        {
            "Valli", "Ferri", "Bosi", "Riva", "Neri", "Serra", "Costa", "Longo", "Moro", "Fanti", "Gallo", "Conti", "Sala", "Lenzi", "Vitale", "Pace",
            "Orsi", "Greco", "Monti", "Rota", "Pellini", "Zanetti", "Barbieri", "Cattaneo", "Ruggeri",
        };

        static readonly System.Globalization.CultureInfo It = System.Globalization.CultureInfo.GetCultureInfo("it-IT");

        /// <summary>The game starts the director: the interface first (the loading screen), then the camera and the sky, then the first race.</summary>
        public void Begin() { StartCoroutine(Boot()); }

        IEnumerator Boot()
        {
            hud = new PistaHud();
            hud.SetLoading(0f, "Preparo la pista");
            PistaDetail.Begin();                // the textures of the ground are made on a worker thread while the rest loads
            PistaFoliage.Begin();               // so are the cards of the leaves and the fir fronds
            PistaSigns.Begin();                 // and the boards and the crowd of the trackside
            yield return null;
            cam = PistaCamera.Create();
            sky = DesertSky.Create(cam.cam);
            ApplySky();
            yield return SetupRace(seed, false);
        }

        /// <summary>The look of the day (the hour chosen in the menu) on a camera that may have been given back its clear flags by the sky of a previous race.</summary>
        void ApplySky()
        {
            if (sky == null) return;
            PistaLook.Apply(PistaMode.Rain);                          // a circuit is seen from far: the haze of the desert hides the next corner, and its white sky does not suit green country (PistaLook)
            sky.Frozen = true;
            sky.SetHour(PistaMode.SkyHour(PistaMode.Hour), true);
            sky.ForceStorm(0f);
            if (cam != null && cam.cam != null) cam.cam.farClipPlane = 6000f;
            WeatherSet(PistaMode.Rain);                 // the rain, the wet look of the ground (PistaDirector.Weather.cs)
        }

        IEnumerator SetupRace(int raceSeed, bool again)
        {
            seed = raceSeed;
            PistaMode.Begin(seed);
            CleanupRace();
            hud.SetLoadingTrack("");
            hud.SetLoading(0.02f, "Disegno il tracciato");
            yield return null;
            ApplySky();
            circuit = Circuits.Generate(seed);
            circuit.wet = PistaMode.Rain ? 1f : 0f;                // the water on the road: the riders plan with it and the bikes feel it
            string km = (circuit.length / 1000f).ToString("0.0", It) + " km";
            hud.SetLoadingTrack(circuit.name + "   -   " + km);
            hud.SetLoading(0.08f, "Preparo la gara");
            yield return null;

            // a friend's machine does not make the race: it has the circuit from the seed and waits for the riders from the host (CoopOnRoster)
            bool friendMachine = Coop.IsClient;
            coopRace = Coop.Active;
            if (!friendMachine)
            {
                race = new Race(circuit, PistaMode.Laps, seed);
                BuildRiders();
                HookRace();
                CoopRiders();
            }

            world = PistaWorld.Create(circuit, seed, transform);
            var build = world.Build();
            while (build.MoveNext()) { hud.SetLoading(0.10f + 0.80f * world.Progress, world.Stage); yield return null; }

            if (friendMachine)
            {
                hud.SetLoading(0.92f, "Aspetto la griglia dall'ospite");
                yield return null;
                rosterDone = false;
                Coop.OnFloorBuilt();            // the scenery is built: the host answers with who rides
                float waited = 0f;
                while (!rosterDone && Coop.IsClient && waited < 90f) { waited += Time.unscaledDeltaTime; yield return null; }
                if (!rosterDone)
                {
                    // the host never answered or the connection is gone: a race of our own
                    coopRace = false;
                    race = new Race(circuit, PistaMode.Laps, seed);
                    BuildRiders();
                    HookRace();
                }
            }
            hud.SetLoading(0.93f, "Metto le moto in griglia");
            yield return null;

            started = false; acc = 0f; pendingShift = 0; lastLights = 0; goFlash = 0f; held = default(BikeInputs); resultsShown = false;
            input.Reset();
            BuildViews();
            if (guide != null) guide.Destroy();
            guide = PistaGuide.Build(world.transform, race.LineFor(me.bike.spec), PistaMode.Aids);          // the driving line of the bike the player rides, as the aids level shows it
            world.SetLamps(0);
            hud.BuildMap(circuit, race.racers.Count, circuit.name + "  -  " + km);
            hud.HideResults();
            TimingBegin();
            LicenceBegin();
            CupBegin();
            CareerBegin();
            if (PistaMode.HasArg("-pistabot"))
            {
                autopilot = new BikeDriver(me.bike, circuit, race.LineFor(me.bike.spec), seed ^ 0x3131) { pace = 0.97f, mistakes = 0.02f };
                autopilot.Reset(); autopilotIsBot = true;
            }
            // the first flight of the camera: from high in front of the grid back to the player's bike
            float gx, gz, gy, gh;
            circuit.GridSlot(me.slot, out gx, out gz, out gy, out gh);
            Vector3 f = new Vector3(Mathf.Sin(gh), 0f, Mathf.Cos(gh)), r = new Vector3(f.z, 0f, -f.x);
            cam.Snap();
            cam.StartIntro(new Vector3(gx, gy + 24f, gz) + f * 52f + r * 16f, 3.4f);
            startWait = again ? 3.2f : 4.6f;
            if (!again) hud.SetHints(HintText(), 16f);
            hud.HideLoading();
            hud.Banner(circuit.name, race.laps + (race.laps == 1 ? " giro" : " giri") + "   -   " + race.racers.Count + " piloti   -   " + MyBikeName(), 3.4f);
            if (CupActive) CupBanner();
            else if (licence != null) hud.Banner(Licences.Names[licence.test.licence] + "  -  " + licence.test.name, circuit.name + "   -   " + Bikes.Get(me.bikeIndex).name + "   -   per l'oro " + PistaHud.Clock(licence.test.gold), 4.2f, LicenceColor(Medal.Gold));
            RaceCount++;
            Ready = true;
            Debug.Log($"[PryingEyes] pista: seme {seed}, {circuit.name}, {circuit.length:F0} m, {race.racers.Count} piloti, {race.laps} giri" + (coopRace ? (Coop.IsHost ? ", ospite" : ", ospitato") : ""));
            if (!friendMachine && Coop.Active) Coop.OnFloorBuilt();       // the host: the race is built, the friends waiting to be welcomed can come in
        }

        string HintText()
        {
            string gears = PistaMode.Aids == 2 ? "   E / Q  marce" : "";
            string go = coopRace && Coop.IsHost ? "     INVIO / A  via" : "";
            return "W  gas     S  freno anteriore     SPAZIO  freno posteriore     A / D  inclina e sterza     MAIUSC  nasconditi     I J K L  sposta il peso" + gears + "     C  camera     V  guarda dietro     G  fantasma     R  rientra in pista" + go + "\n"
                + "Pad:   RT gas   LT freno   stick sinistro sterza   stick destro sposta il peso (anche avanti e indietro)   A  nasconditi   RB / LB  marce   Y  camera   B  rientra";
        }

        // ------------------------------------------------------------------ the riders
        /// <summary>The skin of a friend's bike by the place in the network game (the computer riders never take it: see BuildRiders).</summary>
        static int FriendSkin(int slot, int mine)
        {
            int[] pref = { 2, 4, 5, 6, 7 };
            int k = Mathf.Max(0, slot - 1), n = 0;
            for (int i = 0; i < pref.Length; i++)
            {
                if (pref[i] == mine) continue;
                if (n++ == k) return pref[i];
            }
            return pref[pref.Length - 1];
        }

        void BuildRiders()
        {
            cupIndex = -1; cupSeason = null; cupField = null; cupRider.Clear();
            if (PistaMode.Cup >= 0 && PistaMode.Cup < Cups.Count && !Coop.Active) { cupIndex = PistaMode.Cup; BuildCupRiders(); return; }
            var rng = new System.Random(seed * 7 + 13);
            int rivals = PistaMode.Rivals, rank = PistaMode.Rank;
            var mine = Characters.Progress.Mine;
            string myName = mine != null && !string.IsNullOrEmpty(mine.name) ? mine.name : "Tu";
            if (Coop.Active && Coop.Game != null) myName = Coop.Game.NameOf(Coop.Slot);
            int bikeMe = PistaMode.Bike;
            me = race.AddHuman(myName, bikeMe, PistaMode.SkinFor(bikeMe), seed + 5, PistaMode.SpecFor(bikeMe));
            me.netSlot = Coop.Active ? Coop.Slot : -1;
            PistaMode.ApplyAids(me.bike, PistaMode.Aids);

            float basePace = rank == 0 ? 0.885f : rank == 1 ? 0.935f : 0.968f;
            float spread = rank == 0 ? 0.06f : rank == 1 ? 0.045f : 0.027f;
            float mistakes = rank == 0 ? 0.28f : rank == 1 ? 0.12f : 0.04f;
            var names = new List<string>(Names);
            for (int i = names.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = names[i]; names[i] = names[j]; names[j] = t; }
            var skins = new List<int>();
            var reserved = new HashSet<int>();
            if (Coop.IsHost) for (int s = 1; s < CoopProtocol.MaxPlayers; s++) reserved.Add(FriendSkin(s, me.skin));       // the three places of the friends have their own colours
            for (int i = 0; i < Livery.Count; i++) if (i != me.skin && !reserved.Contains(i)) skins.Add(i);
            for (int i = skins.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = skins[i]; skins[i] = skins[j]; skins[j] = t; }
            var ais = new List<Racer>();
            var keys = new Dictionary<Racer, float>();
            // the rivals ride the class of the player's bike: a race is between bikes that are a match for each other
            string klass = Bikes.Get(bikeMe).klass;
            int[] classMates = Bikes.OfClass(klass);
            int quickest = Bikes.Quickest(klass);
            for (int i = 0; i < rivals; i++)
            {
                double roll = rng.NextDouble();
                int bike = classMates[Mathf.Min(classMates.Length - 1, (int)(roll * classMates.Length))];
                float pace = basePace + spread * (float)(rng.NextDouble() - 0.5) * 2f;
                var r = race.AddAi(names[i % names.Count], bike, skins[i % skins.Count], seed * 31 + i * 17 + 3, pace, mistakes);
                ais.Add(r);
                keys[r] = pace + (bike == quickest ? 0.02f : 0f) + (float)(rng.NextDouble() - 0.5) * 0.03f;
            }
            ais.Sort((a, b) => keys[b].CompareTo(keys[a]));
            // the grid: the faster riders in front, the player in the middle of the field
            int total = ais.Count + 1, mySlot = Mathf.Min(ais.Count, ais.Count / 2);
            var order = new List<Racer>();
            int k = 0;
            for (int slot = 0; slot < total; slot++) order.Add(slot == mySlot ? me : ais[k++]);
            // the friends who are already in the game wait behind everybody until their machines say they have built the circuit
            if (Coop.IsHost && Coop.Game != null)
            {
                Coop.Game.PistaFriends(slotsTmp);
                foreach (int s in slotsTmp) order.Add(NewFriend(s));
            }
            race.PlaceAll(order);
        }

        static int SkinForBike(int bike) { return Bikes.Get(bike).skin; }

        /// <summary>The name of the player's bike for the banners: the bike, and the level of tuning it has in this race.</summary>
        string MyBikeName()
        {
            var s = me.bike != null ? me.bike.spec : Bikes.Get(me.bikeIndex);
            return s.name + (s.tune > 0 ? " liv. " + s.tune : "");
        }

        void BuildViews()
        {
            views.Clear();
            for (int i = 0; i < race.racers.Count; i++) AddView(race.racers[i]);
        }

        /// <summary>A bike in the scene for a rider: the one simulated here is pushed every step, the others wait for the poses that arrive.</summary>
        PistaBikeView AddView(Racer r)
        {
            var v = PistaBikeView.Create(transform, r, r == me, Bikes.Get(r.bikeIndex).meshStyle, r == me ? PistaMode.RiderLivery(r.skin) : Livery.For(r.skin));           // the player's own rider wears the outfit of the garage
            views.Add(v);
            if (r == me) myView = v;
            if (r.bike != null) v.Push(MakeView(r));
            else v.Receive(RemotePose(r));
            return v;
        }

        static BikeView RemotePose(Racer r)
        {
            var v = new BikeView { x = r.x, y = r.y, z = r.z, heading = r.heading, speed = r.speed, lean = r.lean, rpm = 1500f, gear = 1 };
            return v;
        }

        // ------------------------------------------------------------------ taking it down
        void CleanupRace()
        {
            Ready = false;
            for (int i = 0; i < views.Count; i++) if (views[i] != null) Destroy(views[i].gameObject);
            views.Clear();
            myView = null;
            if (guide != null) { guide.Destroy(); guide = null; }
            if (world != null) Destroy(world.gameObject);
            world = null; race = null; me = null; circuit = null; autopilot = null; autopilotIsBot = false;
            resultsShown = false;
            TimingEnd();
            ReplayClear();
            rosterDone = false; overSent = false; lastPose.Clear();
            if (hud != null) { hud.HideResults(); hud.SetStatus(""); }
        }
    }
}
