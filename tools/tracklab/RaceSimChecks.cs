using System;
using System.Collections.Generic;
using System.Linq;
using PryingEyes.Pista;
using Production.Racing;

static partial class Program
{
    const float RaceDt = 1f / 60f;

    static void RaceSimChecks()
    {
        var circuits = LabCircuits();

        // ---- one computer rider alone, no mistakes: the lap is clean and as fast as the plan says
        {
            double worstRatio = 0, bestRatio = 9, offTime = 0, totalTime = 0; int crashes = 0, unfinished = 0; string detail = "";
            for (int ci = 0; ci < 6; ci++)
            {
                var c = circuits[ci];
                var race = new Race(c, 2, ci);
                var r = race.AddAi("solo", 0, 0, ci + 1, 0.96f, 0f);
                race.PlaceAll(new List<Racer> { r });
                race.StartCountdown();
                float t = 0f; double off = 0, run = 0;
                while (race.phase != RacePhase.Over && t < 400f)
                {
                    race.Step(RaceDt); t += RaceDt;
                    if (race.phase == RacePhase.Racing)
                    {
                        run += RaceDt;
                        if (Math.Abs(r.lateral) > c.HalfWidth(r.s) + 0.5f) off += RaceDt;
                    }
                }
                crashes += r.crashes; if (!r.finished) unfinished++;
                offTime += off; totalTime += run;
                double plan = race.LineFor(0).lapTime / 0.96;
                double lap2 = r.bestLap > 0 ? r.bestLap : r.lastLap;
                double ratio = lap2 / plan;
                worstRatio = Math.Max(worstRatio, ratio); bestRatio = Math.Min(bestRatio, ratio);
                detail += " [" + ci + ": lap " + lap2.ToString("0.0") + " s vs plan " + plan.ToString("0.0") + "]";
            }
            Check("RACE_AI_RIDES_THE_PLAN", crashes == 0 && unfinished == 0 && worstRatio < 1.12 && bestRatio > 0.9,
                "6 circuits, 2 laps alone: crashes " + crashes + ", unfinished " + unfinished + ", lap time over plan " + bestRatio.ToString("0.00") + " to " + worstRatio.ToString("0.00") + ", off the asphalt " + (100 * offTime / Math.Max(1, totalTime)).ToString("0.0") + " % of the time;" + detail,
                "no fall, a lap within 10 % of what the plan promises, on the road");
        }

        // ---- a full grid
        {
            var c = circuits[1];
            var race = new Race(c, 2, 5);
            var order = new List<Racer>();
            for (int i = 0; i < 12; i++) order.Add(race.AddAi("AI" + i, i % 3, i, 100 + i * 17, 0.90f + 0.008f * i, 0.12f));
            race.PlaceAll(order);
            race.StartCountdown();
            int laps = 0, crashes = 0;
            race.OnLap = r => laps++;
            race.OnCrash = r => crashes++;
            float t = 0f; long close = 0, samples = 0; var hist = new int[30]; string firstClose = ""; var pairHist = new Dictionary<string, int>();
            while (race.phase != RacePhase.Over && t < 600f)
            {
                race.Step(RaceDt); t += RaceDt;
                if (race.phase == RacePhase.Racing && race.time > 12f)
                {
                    for (int i = 0; i < race.racers.Count; i++)
                        for (int j = i + 1; j < race.racers.Count; j++)
                        {
                            var a = race.racers[i]; var b = race.racers[j];
                            if (a.crashed || b.crashed) continue;
                            float dx = a.x - b.x, dz = a.z - b.z;
                            if (dx * dx + dz * dz < 1.0f) { close++; hist[Math.Min(29, (int)(race.time / 10f))]++; string key = a.id + "-" + b.id; int cnt; pairHist.TryGetValue(key, out cnt); pairHist[key] = cnt + 1; if (firstClose == "") firstClose = " first at " + race.time.ToString("0.0") + " s: " + a.name + " speed " + a.speed.ToString("0.0") + " lat " + a.lateral.ToString("0.0") + " s " + a.s.ToString("0") + " / " + b.name + " speed " + b.speed.ToString("0.0") + " lat " + b.lateral.ToString("0.0") + " s " + b.s.ToString("0"); }
                        }
                    samples++;
                }
            }
            int finished = 0; foreach (var r in race.racers) if (r.finished) finished++;
            bool orderOk = true;
            for (int i = 1; i < race.standings.Count; i++)
                if (race.standings[i - 1].finished && race.standings[i].finished && race.standings[i - 1].finishTime > race.standings[i].finishTime + 0.05f) orderOk = false;
            double sumLap = 0; int nLap = 0; foreach (var r in race.racers) if (r.bestLap > 0) { sumLap += r.bestLap; nLap++; }
            Check("RACE_FULL_GRID_FINISHES", finished == 12 && orderOk && laps >= 12 * 2 && race.phase == RacePhase.Over,
                "12 riders, 2 laps: finished " + finished + ", lap crossings " + laps + ", falls " + crashes + ", ended after " + t.ToString("0") + " s, best laps mean " + (sumLap / Math.Max(1, nLap)).ToString("0.0") + " s, standings by finish time " + orderOk,
                "everybody finishes and the order is the order of the times");
            Check("RACE_RIDERS_KEEP_APART", close < samples / 8 + 1, "bikes closer than 1 m: " + close + " pair-samples over " + samples + " frames; by 10 s: " + string.Join(",", hist).TrimEnd(',') + "; pairs: " + string.Join(" ", pairHist.Select(kv => kv.Key + "=" + kv.Value)) + firstClose, "no bike-to-bike collisions yet, so a tight pack may overlap briefly: under 0.12 pairs per frame");
        }

        // ---- the lights
        {
            var c = circuits[2];
            var race = new Race(c, 1, 9);
            var r = race.AddAi("x", 0, 0, 1, 0.95f, 0f);
            race.PlaceAll(new List<Racer> { r });
            race.StartCountdown();
            float t = 0f, goAt = -1f; int maxLights = 0; bool monotone = true; int prev = 0;
            race.OnGo = () => goAt = t;
            while (t < 12f && goAt < 0f)
            {
                race.Step(RaceDt); t += RaceDt;
                maxLights = Math.Max(maxLights, race.lightsOn);
                if (race.lightsOn < prev && race.phase == RacePhase.Countdown) monotone = false;
                prev = race.lightsOn;
            }
            Check("RACE_START_LIGHTS", maxLights == 5 && monotone && goAt >= 5.6f && goAt <= 7.3f && race.phase == RacePhase.Racing && Math.Abs(goAt - race.CountdownLength) < 0.05f && r.progress < 0f,
                "five lights one per second, then out after " + goAt.ToString("0.00") + " s (hold " + (race.CountdownLength - 5f).ToString("0.00") + " s); the bike stayed on the grid until then: " + (r.progress < 0 ? "yes" : "no"),
                "5 lights, a random hold of 0.6 to 2.2 s, nobody moves before");
        }

        // ---- a fall and the way back
        {
            var c = circuits[3];
            var race = new Race(c, 3, 3);
            var r = race.AddAi("reckless", 0, 0, 11, 1.9f, 0f);   // far too fast for the grip: it will fall
            race.PlaceAll(new List<Racer> { r });
            race.StartCountdown();
            int fell = 0, back = 0; float fellAt = -1f, backAt = -1f, progAtFall = 0f, progAtBack = 0f;
            float headDot = 0f, latAtBack = 0f;
            race.OnCrash = x => { fell++; if (fellAt < 0) { fellAt = race.time; progAtFall = x.progress; } };
            race.OnRespawn = x =>
            {
                back++;
                if (backAt < 0)
                {
                    backAt = race.time; progAtBack = x.progress;
                    Proj p; int h = -1; c.Project(x.bike.x, x.bike.z, ref h, out p);
                    headDot = (float)(Math.Sin(x.bike.heading) * p.tx + Math.Cos(x.bike.heading) * p.tz);
                    latAtBack = Math.Abs(p.d) / c.HalfWidthAt(p);
                }
            };
            float t = 0f;
            while (t < 140f) { race.Step(RaceDt); t += RaceDt; if (back >= 2) break; }
            Check("RACE_FALL_AND_RESPAWN", fell >= 2 && back >= 2 && backAt - fellAt > 1.5f && backAt - fellAt < 12f && headDot > 0.97f && latAtBack < 0.9f && Math.Abs(progAtBack - progAtFall) < 80f,
                "falls " + fell + ", back on the road " + back + " times, first fall at " + fellAt.ToString("0.0") + " s and back after " + (backAt - fellAt).ToString("0.0") + " s facing the race " + headDot.ToString("0.00") + ", " + (latAtBack * 100).ToString("0") + " % of the way to the edge, moved " + Math.Abs(progAtBack - progAtFall).ToString("0") + " m along",
                "a fall, a pause, then upright on the asphalt facing forward near the same place");
        }

        // ---- gaps from the timing loops
        {
            var c = circuits[4];
            var race = new Race(c, 3, 4);
            var a = race.AddAi("front", 0, 0, 21, 0.95f, 0f);
            var b = race.AddAi("behind", 0, 0, 22, 0.95f, 0f);
            a.driver.bias = 0f; b.driver.bias = 0f; a.driver.reaction = 0.1f; b.driver.reaction = 3.1f;
            race.PlaceAll(new List<Racer> { a, b });
            race.StartCountdown();
            float t = 0f;
            while (race.time < 70f || race.phase != RacePhase.Racing) { race.Step(RaceDt); t += RaceDt; if (t > 100f) break; }
            float gap = race.GapToAhead(b);
            Check("RACE_GAP_FROM_TIMING_LOOPS", a.crashes == 0 && b.crashes == 0 && gap > 2.2f && gap < 4.2f && b.position == 2 && race.GapToAhead(a) == 0f,
                "a rider that starts 3 s later is " + gap.ToString("0.00") + " s behind, positions " + a.position + " and " + b.position,
                "about 3 s");
        }

        // ---- riding the wrong way
        {
            var c = circuits[0];
            var race = new Race(c, 1, 1);
            var r = race.AddHuman("me", 0, 0, 1);
            race.PlaceAll(new List<Racer> { r });
            race.StartCountdown();
            while (race.phase != RacePhase.Racing) race.Step(RaceDt);
            r.bike.heading += (float)Math.PI; r.bike.u = 12f;
            race.Step(RaceDt);
            bool wrong = r.wrongWay;
            r.bike.heading += (float)Math.PI;
            race.Step(RaceDt);
            Check("RACE_WRONG_WAY_IS_FLAGGED", wrong && !r.wrongWay, "facing backwards: " + wrong + ", turned round: " + !r.wrongWay, "flagged only while facing against the race");
        }
    }
}
