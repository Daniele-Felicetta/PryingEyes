using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    public partial class HUD
    {
        Text coopHeading;
        readonly Text[] coopNames = new Text[3], coopDetails = new Text[3];
        readonly Image[] coopHealth = new Image[3];
        readonly RectTransform[] coopRows = new RectTransform[3];
        float coopRefresh;

        void BuildCoopUI()
        {
            coopHeading = MakeText("Compagni", new Vector2(1, 1), new Vector2(-28, -26), new Vector2(300, 24),
                16, TextAnchor.UpperRight, "", Dim, FontStyle.Normal);
            for (int i = 0; i < coopRows.Length; i++)
            {
                var go = Child("Compagno" + i);
                var rt = Comp<RectTransform>(go);
                rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one;
                rt.anchoredPosition = new Vector2(-28, -54 - i * 58);
                rt.sizeDelta = new Vector2(280, 52);
                var bg = Comp<Image>(go);
                bg.color = new Color(0.02f, 0.02f, 0.025f, 0.70f); bg.raycastTarget = false;
                coopRows[i] = rt;
                coopNames[i] = MakeText("CompagnoNome" + i, Vector2.one, new Vector2(-8, -3), new Vector2(264, 22),
                    18, TextAnchor.UpperRight, "", Ink, FontStyle.Normal);
                coopNames[i].supportRichText = false;
                coopNames[i].resizeTextForBestFit = true;
                coopNames[i].resizeTextMinSize = 12; coopNames[i].resizeTextMaxSize = 18;
                coopNames[i].transform.SetParent(rt, false);
                coopDetails[i] = MakeText("CompagnoStato" + i, Vector2.one, new Vector2(-8, -25), new Vector2(264, 20),
                    14, TextAnchor.UpperRight, "", Dim, FontStyle.Normal);
                coopDetails[i].transform.SetParent(rt, false);
                var bar = new GameObject("Vita", typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(rt, false);
                var br = (RectTransform)bar.transform;
                br.anchorMin = br.anchorMax = br.pivot = new Vector2(0, 0);
                br.anchoredPosition = new Vector2(8, 2);
                br.sizeDelta = new Vector2(264, 3);
                coopHealth[i] = bar.GetComponent<Image>();
                coopHealth[i].raycastTarget = false;
                go.SetActive(false);
            }
        }

        void UpdateCoopUI()
        {
            coopRefresh -= Time.unscaledDeltaTime;
            if (coopRefresh > 0f) return;
            coopRefresh = 0.25f;
            var game = Coop.Game;
            int row = 0;
            if (Coop.Active && game != null)
                for (int slot = 0; slot < CoopProtocol.MaxPlayers; slot++)
                {
                    if (!game.Players.TryGetValue(slot, out var peer) || peer == null) continue;
                    coopRows[row].gameObject.SetActive(true);
                    coopNames[row].text = peer.playerName + (slot == 0 ? " · ospite" : "");
                    bool waiting = !peer.hasState || !peer.Shown;
                    float hp = waiting || !peer.last.alive ? 0f : Mathf.Clamp01(peer.last.hp);
                    var player = GameManager.I != null ? GameManager.I.player : null;
                    string distance = player != null ? " · " + Mathf.RoundToInt(Vector3.Distance(player.transform.position, peer.Feet)) + " m" : "";
                    coopDetails[row].text = waiting ? "Sta raggiungendo il piano…" : !peer.last.alive ? "Sta tornando all'ingresso…" :
                        "Vita " + Mathf.RoundToInt(hp * 100f) + "%" + distance;
                    coopHealth[row].rectTransform.sizeDelta = new Vector2(264f * hp, 3f);
                    coopHealth[row].color = hp <= 0.3f ? new Color(0.85f, 0.25f, 0.2f) : new Color(0.62f, 0.72f, 0.53f);
                    row++;
                }
            coopHeading.text = !Coop.Active ? "" : row == 0 ? "In attesa di compagni" : "Compagni · " + (row + 1) + "/4";
            for (int i = row; i < coopRows.Length; i++) coopRows[i].gameObject.SetActive(false);
        }
    }
}
