using Production.Survival;
using UnityEngine;

// Independent population simulation: no game enemies, balance, scene globals or catalogs.
public sealed class WaveDemo : MonoBehaviour
{
    public WaveSchedule Schedule { get; private set; }
    public int Living { get; private set; }
    void Awake() => ResetDemo();
    public void ResetDemo() { Schedule = new WaveSchedule(12, 3, .5f); Living = 0; }
    public void Advance(float seconds)
    {
        if (Schedule.Advance(seconds, Living)) { Schedule.CommitSpawn(); Living++; }
    }
    public void DefeatOne() { if (Living > 0) Living--; }
    void Update() => Advance(Time.deltaTime);
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20, 250, 340, 160), GUI.skin.box);
        GUILayout.Label("Independent wave scheduling demo");
        GUILayout.Label($"Pending {Schedule.Pending}   Living {Living}/3");
        GUILayout.Label(Schedule.Complete(Living) ? "Wave complete" : "Spawning pauses at population limit");
        if (GUILayout.Button("Defeat one")) DefeatOne();
        if (GUILayout.Button("Reset wave")) ResetDemo();
        GUILayout.EndArea();
    }
}
