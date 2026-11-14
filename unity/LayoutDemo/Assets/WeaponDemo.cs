using Production.Weapons;
using UnityEngine;

// Independent caller: no Prying Eyes catalog, Weapon, PlayerController, GameManager, HUD or audio.
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
    }
}
