using System.Collections.Generic;
using Production.Weapons;
using UnityEngine;

// Independent caller: no Prying Eyes catalog, Weapon, PlayerController, GameManager, HUD or audio.
// Two things are shown: the state of one weapon (rounds, cadence, reload), and a shooting range (WeaponRange.cs) where weapons of a catalogue that belongs to no game
// are fired at rows of targets with the numbers of the package alone.
public sealed class WeaponDemo : MonoBehaviour
{
    public WeaponState State { get; private set; }
    public string lastAction = "Ready";
    void Awake() => ResetDemo();
    public void ResetDemo() { State = new WeaponState(); State.SetAmmunition(8, 32); lastAction = "Ready"; }
    public bool FireOnce()
    {
        if (!State.Ready || !State.ConsumeRound()) return false;
        State.RecordShot(.2f); lastAction = "Fired"; return true;
    }
    public bool Reload() => State.BeginReload(8, ReloadTiming.Magazine(1.2f, 1.7f));
    public void Advance(float seconds)
    {
        var step = State.AdvanceReload(seconds); State.AdvanceCadence(seconds);
        if (step.Events != ReloadEvents.None) lastAction = step.Events.ToString();
    }
    void Update() => Advance(Time.deltaTime);

    int scenario;
    string rangeTitle = "";
    readonly List<string> rangeLines = new List<string>();

    public void FireRange()
    {
        var all = WeaponRange.Scenarios();
        var s = all[scenario++ % all.Count];
        var report = WeaponRange.Fire(s, WeaponRange.Seed);
        rangeTitle = s.Name;
        rangeLines.Clear(); rangeLines.AddRange(report.Lines);
        Debug.Log(s.Name + "\n" + string.Join("\n", report.Lines));
    }

    /// <summary>For a batch run (-executeMethod WeaponDemo.Verify): what each shot of each scenario did is printed, and the line that follows says what the package promised and held.</summary>
    public static void Verify()
    {
        foreach (var s in WeaponRange.Scenarios())
            Debug.Log(s.Name + "\n" + string.Join("\n", WeaponRange.Fire(s, WeaponRange.Seed).Lines));
        Debug.Log("WEAPON_DEMO_OK " + WeaponRange.SelfCheck());
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20, 20, 340, 210), GUI.skin.box);
        GUILayout.Label("Independent weapon-state demo");
        GUILayout.Label($"Magazine {State.Magazine}/8   Reserve {State.Reserve}");
        GUILayout.Label(State.IsReloading ? $"Reloading {State.ReloadProgress:P0}" : lastAction);
        GUI.enabled = State.Ready && State.Magazine > 0;
        if (GUILayout.Button("Fire")) FireOnce();
        GUI.enabled = !State.IsReloading && State.Magazine < 8 && State.Reserve > 0;
        if (GUILayout.Button("Reload")) Reload();
        GUI.enabled = true;
        if (GUILayout.Button("Reset")) ResetDemo();
        GUILayout.EndArea();

        GUILayout.BeginArea(new Rect(20, 430, 1080, 175), GUI.skin.box);       // under the panels of the other demos, which fill the three columns above
        GUILayout.Label("Independent shooting range");
        GUILayout.Label(rangeTitle.Length == 0 ? "Fire a scenario: its shots are printed here and in the log." : rangeTitle);
        for (int i = System.Math.Max(0, rangeLines.Count - 3); i < rangeLines.Count; i++) GUILayout.Label(rangeLines[i]);
        if (GUILayout.Button("Fire the next scenario")) FireRange();
        GUILayout.EndArea();
    }
}
