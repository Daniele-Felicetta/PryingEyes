using System.Collections.Generic;
using Production.Economy;
using PryingEyes.Cattedrale;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    public partial class HUD
    {
        sealed class HubCheatPage : HubPage
        {
            const int Points = 10000, Materials = 100;
            const float RowHeight = 62f, RowStride = 72f;
            static readonly string[] Labels = { "Punti", "Materiali", "Volo", "Invulnerabilità", "Cura completa", "Munizioni al massimo" };
            static readonly string[] Details =
            {
                "Aggiungi punti da spendere in partita.",
                "Aggiungi ogni materiale, inclusi i trofei, al magazzino della Cattedrale.",
                "Muoviti con WASD o lo stick. Sali con Spazio / A, scendi con Ctrl / B.",
                "Proteggiti da danni e sedativi.",
                "Ripristina salute e scudo.",
                "Riempi caricatori e riserve di entrambe le armi."
            };
            readonly Image[] backgrounds = new Image[Labels.Length];
            readonly Text[] values = new Text[Labels.Length];
            Text note;
            int selected;

            public HubCheatPage() { label = "Cheat"; }
            public override string Hint => "↑ ↓  scegli    ·    Invio / A  applica";
            public override void OnShow(HUD h) { selected = 0; }

            public override void Build(HUD h, RectTransform parent)
            {
                for (int i = 0; i < Labels.Length; i++)
                {
                    float y = i * RowStride;
                    backgrounds[i] = HubRect(parent, "Action" + i, 0f, y, HubContentW, RowHeight, HubCard);
                    HubText(parent, "Label" + i, 18f, y + 6f, 780f, 26f, 21, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold).text = Labels[i];
                    HubText(parent, "Detail" + i, 18f, y + 33f, 780f, 20f, 15, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal).text = Details[i];
                    values[i] = HubText(parent, "Value" + i, HubContentW - 200f, y, 182f, RowHeight, 18, TextAnchor.MiddleRight, HubBrassC, FontStyle.Bold);
                }
                note = HubText(parent, "Availability", 18f, Labels.Length * RowStride + 8f, HubContentW - 36f, 34f, 15,
                    TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
            }

            public override void Tick(HUD h, in HubInput inp, float dt)
            {
                var gm = GameManager.I;
                var player = gm != null ? gm.player : null;
                bool available = PlayerController.DesertCheatsAvailable && player != null && player.alive && !player.Downed;
                int clicked = -1;
                Vector2 pointer = inp.Local;
                if (inp.pointerIn && pointer.x >= 0f && pointer.x < HubContentW && pointer.y >= 0f)
                {
                    int row = (int)(pointer.y / RowStride);
                    if (row < Labels.Length && pointer.y - row * RowStride < RowHeight)
                    {
                        if (inp.moved || inp.click) selected = row;
                        if (inp.click) clicked = row;
                    }
                }
                selected = Mathf.Clamp(selected - inp.ny, 0, Labels.Length - 1);
                int action = clicked >= 0 ? clicked : inp.confirm ? selected : -1;
                if (available && action >= 0) Apply(h, gm, player, action);

                note.text = Coop.Active
                    ? "I cheat sono disponibili nelle partite locali."
                    : "I materiali vengono salvati nel personaggio. Volo e invulnerabilità si azzerano alla rinascita o uscendo dalla superficie.";
                values[0].text = "+" + HubNum(Points);
                values[1].text = $"+{Materials} per tipo";
                values[2].text = player != null && player.CheatFlight ? "ATTIVO" : "DISATTIVO";
                values[3].text = player != null && player.CheatInvulnerability ? "ATTIVA" : "DISATTIVA";
                values[4].text = values[5].text = "RIPRISTINA";
                for (int i = 0; i < Labels.Length; i++)
                {
                    backgrounds[i].color = available && i == selected ? HubCardHi : HubCard;
                    values[i].color = available ? HubBrassC : HubDimC;
                }
                if (available)
                {
                    if (player.CheatFlight) values[2].color = HubGreenC;
                    if (player.CheatInvulnerability) values[3].color = HubGreenC;
                }
            }

            static void Apply(HUD h, GameManager gm, PlayerController player, int action)
            {
                string message;
                switch (action)
                {
                    case 0:
                        int points = Mathf.Min(Points, int.MaxValue - gm.score);
                        gm.score += points;
                        h.SetScore(gm.score);
                        message = $"+{HubNum(points)} punti";
                        break;
                    case 1:
                        var bank = Fabbrica.Banca;
                        var grant = new List<KeyValuePair<string, int>>();
                        foreach (var resource in Risorse.Tutte)
                            if (Risorse.IsMaterial(resource.id))
                                grant.Add(new KeyValuePair<string, int>(resource.id, Mathf.Min(Materials, int.MaxValue - bank.Get(resource.id))));
                        bank.Deposit(new Bundle(grant));
                        Fabbrica.Salva();
                        message = "Materiali aggiunti al magazzino.";
                        break;
                    case 2:
                        if (player.Riding)
                        {
                            h.HubSay("Scendi dalla moto per attivare il volo.", true);
                            return;
                        }
                        player.SetCheatFlight(!player.CheatFlight);
                        message = player.CheatFlight ? "Volo attivo. Chiudi Select per volare." : "Volo disattivato.";
                        break;
                    case 3:
                        player.CheatInvulnerability = !player.CheatInvulnerability;
                        message = player.CheatInvulnerability ? "Invulnerabilità attiva." : "Invulnerabilità disattivata.";
                        break;
                    case 4:
                        player.shield = Mathf.Max(player.shield, 50f);
                        player.Heal(player.MaxHp);
                        message = "Salute e scudo ripristinati.";
                        break;
                    default:
                        if (player.weapon == null) return;
                        player.weapon.MaxAmmo();
                        message = "Munizioni ripristinate.";
                        break;
                }
                h.HubSay(message, false);
                Sfx.UI(SoundBank.BookSeal, 0.5f);
            }
        }
    }
}
