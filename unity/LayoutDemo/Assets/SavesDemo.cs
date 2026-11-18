using Production.Saves;
using UnityEngine;

// Independent consumer: a counter saved in a file, and a "new version" of the game that changes what the file means.
// The file is read through a chain, so a save written by the old version is upgraded instead of lost.
public sealed class SavesDemo : MonoBehaviour
{
    public const string Kind = "demo.counter";
    // Version 1 stored "count". Version 2 stores the same number as "clicks" and adds a name.
    public static readonly SaveFormat V1 = new SaveFormat(Kind, 1);
    public static readonly SaveFormat V2 = new SaveFormat(Kind, 2,
        new Migration(1, "count is renamed clicks and a name is added", doc => { doc.Rename("count", "clicks"); doc.SetDefault("name", "Visitor"); }));

    public SaveStore Store { get; private set; }
    public JsonItem Counter { get; private set; }
    string folder;

    void Awake() => Open(System.IO.Path.Combine(Application.temporaryCachePath, "saves-demo"));

    /// <summary>Opens the store with the new format: a file the old format wrote is upgraded on the way in.</summary>
    public void Open(string where)
    {
        folder = where;
        Store = new SaveStore(folder, V2);
        var load = Store.Load("counter");
        Counter = load.Loadable ? load.Document : JsonItem.Object().Set("clicks", 0).Set("name", "Visitor");
    }

    public void Click() { Counter.Set("clicks", Counter.Int("clicks") + 1); Store.Save("counter", Counter); }

    /// <summary>What the previous version of the game would have written.</summary>
    public static void WriteOldFile(string where, int count) => new SaveStore(where, V1).Save("counter", JsonItem.Object().Set("count", count));

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(740, 270, 360, 150), GUI.skin.box);
        GUILayout.Label("Independent saves demo");
        GUILayout.Label($"{Counter.Str("name")}: {Counter.Int("clicks")} clicks");
        if (GUILayout.Button("Click (saved at once)")) Click();
        if (GUILayout.Button("Pretend the old version saved 41, reopen")) { WriteOldFile(folder, 41); Open(folder); }
        GUILayout.EndArea();
    }
}
