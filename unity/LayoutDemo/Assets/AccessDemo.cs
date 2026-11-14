using Production.Survival;
using UnityEngine;

// Independent consumer: points and interaction live outside the reusable access model.
public sealed class AccessDemo : MonoBehaviour
{
    public AreaAccess Access { get; private set; }
    public int Balance { get; private set; }
    void Awake() => ResetDemo();
    public void ResetDemo()
    {
        Access = new AreaAccess(new[] { new AreaAccess.Area("start", 0), new AreaAccess.Area("shop", 750), new AreaAccess.Area("roof", 1250) },
            new[] { new AreaAccess.Link("start", "shop"), new AreaAccess.Link("shop", "roof") }, new[] { "start" });
        Balance = 1000;
    }
    public bool Buy(string destination)
    {
        if (!Access.TryBuy(destination == "shop" ? "start" : "shop", destination, Balance, out int charge)) return false;
        Balance -= charge; return true;
    }
    public void Earn() => Balance += 500;
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(380, 20, 340, 200), GUI.skin.box);
        GUILayout.Label("Independent paid-access demo");
        GUILayout.Label($"Points {Balance}   Shop {Access.IsOpen("shop")}   Roof {Access.IsOpen("roof")}");
        if (GUILayout.Button("Buy shop (750)")) Buy("shop");
        if (GUILayout.Button("Buy roof (1250, after shop)")) Buy("roof");
        if (GUILayout.Button("Earn 500 points")) Earn();
        if (GUILayout.Button("Reset access")) ResetDemo();
        GUILayout.EndArea();
    }
}
