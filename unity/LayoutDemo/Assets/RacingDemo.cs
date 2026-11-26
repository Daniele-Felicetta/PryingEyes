using System.Collections.Generic;
using System.Globalization;
using Production.Racing;
using UnityEngine;

// Independent consumer: a bike that belongs to no game rides a circuit made out of a seed, alone, and the lap time is printed.
// The bike, the circuit and the race are all the package's: this project has no bike catalogue, no circuit names and no screens of any game.
public sealed class RacingDemo : MonoBehaviour
{
    public int Seed { get; private set; } = 7;
    public Circuit Circuit { get; private set; }
    public Racer Rider { get; private set; }
    public string Report { get; private set; } = "";

    void Awake() => Ride(Seed);

    /// <summary>One bike alone on the circuit of a seed: two laps from a standing start, in steps of 1/60 s. Nothing here waits for a frame of Unity,
    /// so the same seed gives the same lap time on every machine.</summary>
    public static Racer RideAlone(int seed, out Circuit circuit)
    {
        circuit = CircuitGen.Generate(seed);
        var race = new Race(circuit, 2, seed);
        var rider = race.AddAi("Demo", new BikeSpec { id = "demo", name = "Demo" }, 0, 1, 0.96f, 0f);
        race.PlaceAll(new List<Racer> { rider });
        race.StartCountdown();
        for (int i = 0; i < 60 * 600 && race.phase != RacePhase.Over; i++) race.Step(1f / 60f);
        return rider;
    }

    public static string Describe(Circuit circuit, Racer rider) =>
        string.Format(CultureInfo.InvariantCulture, "{0}: {1:0} m, best lap {2:0.00} s, {3} falls", circuit.name, circuit.length, rider.bestLap, rider.crashes);

    public void Ride(int seed)
    {
        Seed = seed;
        Rider = RideAlone(seed, out var circuit);
        Circuit = circuit;
        Report = Describe(circuit, Rider);
        Debug.Log(Report);
    }

    /// <summary>For a batch run (-executeMethod RacingDemo.Verify): the lap is a real lap, the same seed gives the very same time and another seed another circuit.</summary>
    public static void Verify()
    {
        var a = RideAlone(7, out var circuit);
        var again = RideAlone(7, out _);
        var other = RideAlone(8, out var circuit8);
        if (!a.finished || a.crashes != 0 || !(a.bestLap > 30f && a.bestLap < 300f)) throw new System.Exception("The lap of the demo is not a clean lap: " + Describe(circuit, a));
        if (a.bestLap != again.bestLap) throw new System.Exception("The same seed gave two lap times");
        if (circuit.length == circuit8.length) throw new System.Exception("Two seeds made the same circuit");
        Debug.Log("RACING_DEMO_OK " + Describe(circuit, a) + " | seed 8: " + Describe(circuit8, other));
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(380, 240, 340, 160), GUI.skin.box);
        GUILayout.Label("Independent racing demo");
        GUILayout.Label(Report);
        if (GUILayout.Button("Ride the next seed")) Ride(Seed + 1);
        GUILayout.EndArea();
    }
}
