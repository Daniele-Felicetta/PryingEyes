using Production.Economy;
using UnityEngine;

// Independent consumer: a small base to rebuild, a market with a stock and a raid that is safe only when you come back.
// Coins, scenes and what a construction looks like live outside the reusable economy model.
public sealed class EconomyDemo : MonoBehaviour
{
    public Ledger Stash { get; private set; }
    public WorkTree Base { get; private set; }
    public Market Shop { get; private set; }
    public Expedition Trip { get; private set; }
    LootTable loot;
    System.Random rng;

    void Awake() => ResetDemo();

    public void ResetDemo()
    {
        Stash = new Ledger(Bundle.Of(("coin", 50)));
        Base = new WorkTree(new[] { new WorkTree.Work("wall", Bundle.Of(("stone", 10))), new WorkTree.Work("tower", Bundle.Of(("stone", 20), ("gem", 1)), "wall") });
        Shop = new Market(new[] { new Market.Offer("stone", Bundle.Of(("coin", 10)), Bundle.Of(("stone", 5)), 2) });
        Trip = new Expedition(Stash);
        loot = new LootTable(1, 1, new[] { new LootTable.Entry("stone", 1, 3, 3) });
        rng = new System.Random(1);
    }

    public bool Buy() => Shop.TryBuy("stone", Stash) == Market.Check.Ok;
    public bool Build(string id) => Base.TryBuild(id, Stash) == WorkTree.Check.Ok;

    /// <summary>One raid: three stones and a gem carried; coming back banks them, dying keeps half (rounded down).</summary>
    public void Raid(bool survive)
    {
        Trip.Begin();
        Trip.Pick(loot.Roll(rng.NextDouble));
        Trip.Pick(Bundle.Of(("gem", 1)));
        if (survive) Trip.Extract(); else Trip.Fail(_ => 0.5, out _);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(740, 20, 360, 230), GUI.skin.box);
        GUILayout.Label("Independent economy demo");
        GUILayout.Label($"Stash: {Stash.Snapshot()}");
        GUILayout.Label($"Built: {string.Join(", ", Base.BuiltIds())}   Stone on sale: {Shop.Left("stone")}");
        if (GUILayout.Button("Buy 5 stone (10 coins)")) Buy();
        if (GUILayout.Button("Build wall (10 stone)")) Build("wall");
        if (GUILayout.Button("Build tower (20 stone, 1 gem)")) Build("tower");
        if (GUILayout.Button("Raid and come back")) Raid(true);
        if (GUILayout.Button("Raid and die")) Raid(false);
        if (GUILayout.Button("Restock / reset")) { Shop.Restock(); }
        GUILayout.EndArea();
    }
}
