using System.Collections.Generic;
using System.Text;
using PryingEyes.Deserto;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    public partial class HUD
    {
        /// <summary>I punti cardinali a parole (0 nord, 90 est), per dire da che parte sta una cosa.</summary>
        static string HubCompass(float dx, float dz)
        {
            float a = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            string[] names = { "nord", "nord-est", "est", "sud-est", "sud", "sud-ovest", "ovest", "nord-ovest" };
            return names[Mathf.RoundToInt(a / 45f) % 8];
        }

        static string HubDistance(float m) =>
            m < 1000f ? Mathf.RoundToInt(m) + " m" : (m / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " km";

        /// <summary>La pagina Mappa del menu di Select: la carta della minimappa (DesertMapTiles, 512 m di sabbia intorno a chi gioca, il nord in alto) in grande, con te, la cattedrale e il garage (sul bordo
        /// se sono fuori dalla carta), le tue moto, i compagni e i nemici svegli; a destra l'ora, dove sei, quanto manca a casa, dove hai lasciato le moto e l'obiettivo. Non ha comandi suoi: e' una carta da leggere.</summary>
        public sealed class HubMapPage : HubPage
        {
            const float Side = 480f, Scale = Side / DesertMapGen.WindowSize, EnemyRange = 90f, Col = Side + 40f, ColW = HubContentW - Col;
            RectTransform win;
            RawImage paper;
            Image youRim, you, homeRim, home, homeBox, homeBoxRim, garageRim, garage;
            Text homeLabel, garageLabel;
            readonly Image[] mateRims = new Image[3], mateCores = new Image[3];
            readonly List<Image> enemyRims = new List<Image>(), enemyCores = new List<Image>(), bikeRims = new List<Image>();
            Text clockText, whereText, bikeText, goalText;
            float tintT, textT;
            Color tint = Color.white;
            readonly StringBuilder sb = new StringBuilder();

            public HubMapPage() { label = "Mappa"; }

            public override string Hint => "Q E  LB RB: pagina     1-5: scheda     Esc  M  B  Select: chiudi";

            static Image Square(HUD h, string name, Transform parent, float size, Color col)
            {
                var img = h.DmMarker(name, parent, size).gameObject.AddComponent<Image>();
                img.color = col; img.raycastTarget = false;
                return img;
            }

            static Vector2 At(Vector3 world, Vector3 centre) => new Vector2((world.x - centre.x) * Scale, (world.z - centre.z) * Scale);
            static bool Inside(Vector2 at) => Mathf.Abs(at.x) < Side * 0.5f + 5f && Mathf.Abs(at.y) < Side * 0.5f + 5f;
            static Vector2 OnEdge(Vector2 at, float inset) => at * ((Side * 0.5f - inset) / Mathf.Max(0.01f, Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y))));

            public override void Build(HUD h, RectTransform parent)
            {
                HubRect(parent, "Bordo", 0f, 0f, Side + 8f, Side + 8f, HubBrassC);
                HubRect(parent, "BordoDentro", 2f, 2f, Side + 4f, Side + 4f, HubBar);
                win = Box(parent, "Finestra", new Vector2(4f, -4f), new Vector2(Side, Side));
                win.gameObject.AddComponent<RectMask2D>();
                var paperRt = Box(win, "Carta", Vector2.zero, new Vector2(Side, Side));
                paper = paperRt.gameObject.AddComponent<RawImage>();
                paper.raycastTarget = false;
                try { Proc.MapAssets.Ensure(); Img(win, "Ombra", Vector2.zero, new Vector2(Side, Side), new Color(1f, 1f, 1f, 0.85f), Proc.MapAssets.Shade()); }
                catch (System.Exception e) { Debug.LogWarning("[PryingEyes] pagina Mappa: niente ombra sul bordo (" + e.Message + ")"); }
                // il nord in un tondo scuro in alto, e la scala: cento metri
                Img(win, "NordFondo", new Vector2(Side * 0.5f - 10f, -2f), new Vector2(20f, 20f), DmBoard, CircleSprite());
                var north = HubText(win, "Nord", Side * 0.5f - 10f, 2f, 20f, 18f, 13, TextAnchor.MiddleCenter, new Color(0.93f, 0.85f, 0.66f), FontStyle.Bold);
                north.text = "N"; north.supportRichText = false;
                float bar = 100f * Scale;
                HubRect(win, "ScalaFilo", 12f, Side - 14f, bar, 2f, DmInk);
                HubRect(win, "ScalaA", 12f, Side - 18f, 2f, 10f, DmInk);
                HubRect(win, "ScalaB", 12f + bar - 2f, Side - 18f, 2f, 10f, DmInk);
                var scale = HubText(win, "ScalaTesto", 12f, Side - 34f, 70f, 14f, 11, TextAnchor.UpperLeft, DmInk, FontStyle.Bold);
                scale.text = "100 m"; scale.supportRichText = false;

                // le cose che si muovono: il centro della finestra e' chi gioca
                garageRim = Square(h, "GarageOrlo", win, 20f, DmRim);
                garage = Square(h, "Garage", garageRim.transform, 14f, DmBike);
                garageLabel = HubText(garageRim.transform, "GarageNome", 24f, 2f, 60f, 16f, 11, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                garageLabel.text = "Garage"; garageLabel.supportRichText = false;
                homeBoxRim = Square(h, "CattedraleOrlo", win, 22f, DmRim);
                homeBox = Square(h, "Cattedrale", homeBoxRim.transform, 16f, DesertHome.Gold);
                homeBoxRim.rectTransform.localRotation = homeBox.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                homeLabel = HubText(win, "CattedraleNome", 0f, 0f, 80f, 16f, 11, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                homeLabel.text = "Cattedrale"; homeLabel.supportRichText = false;
                homeLabel.rectTransform.anchorMin = homeLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                for (int i = 0; i < mateRims.Length; i++)
                {
                    mateRims[i] = h.DmDisc("Compagno" + i, win, 13f, DmRim);
                    mateCores[i] = h.DmDisc("Colore", mateRims[i].transform, 9f, Color.white);
                    mateRims[i].gameObject.SetActive(false);
                }
                homeRim = h.DmArrowImg("CasaOrlo", win, 24f, DmRim);
                home = h.DmArrowImg("Casa", win, 17f, DesertHome.Gold);
                youRim = h.DmArrowImg("TuOrlo", win, 24f, DmRim);
                you = h.DmArrowImg("Tu", win, 17f, DmRed);

                // la colonna di destra
                var title = HubText(parent, "Titolo", Col, 0f, ColW, 34f, 28, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold, true);
                title.text = "Le terre esterne";
                clockText = HubText(parent, "Ora", Col, 36f, ColW, 20f, 14, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
                Section(parent, "DOVE SEI", 70f);
                whereText = HubText(parent, "Dove", Col, 90f, ColW, 74f, 15, TextAnchor.UpperLeft, HubInkC, FontStyle.Normal);
                Section(parent, "LE TUE MOTO", 172f);
                bikeText = HubText(parent, "Moto", Col, 192f, ColW, 128f, 14, TextAnchor.UpperLeft, HubInkC, FontStyle.Normal);
                Section(parent, "OBIETTIVO", 326f);
                goalText = HubText(parent, "Obiettivo", Col, 346f, ColW, 52f, 14, TextAnchor.UpperLeft, HubInkC, FontStyle.Normal);
                // la legenda
                HubRect(parent, "LegendaFilo", Col, 410f, ColW, 1f, new Color(HubBrassC.r, HubBrassC.g, HubBrassC.b, 0.35f));
                var circle = CircleSprite();
                float lx = Col, ly = 420f;
                LegendDot(parent, circle, ref lx, ly, DmRed, "Tu");
                LegendDot(parent, null, ref lx, ly, DesertHome.Gold, "Cattedrale");
                LegendDot(parent, null, ref lx, ly, DmBike, "Garage");
                lx = Col; ly = 446f;
                LegendDot(parent, circle, ref lx, ly, DmBike, "Moto");
                LegendDot(parent, circle, ref lx, ly, MateColors[1], "Compagni");
                LegendDot(parent, circle, ref lx, ly, DmRed, "Nemici");
            }

            static void Section(Transform parent, string text, float y)
            {
                var t = HubText(parent, "Sezione", Col, y, ColW, 16f, 12, TextAnchor.MiddleLeft, HubBrassC, FontStyle.Bold);
                t.text = text; t.supportRichText = false;
                HubRect(parent, "SezioneFilo", Col, y + 17f, ColW, 1f, new Color(HubBrassC.r, HubBrassC.g, HubBrassC.b, 0.25f));
            }

            static void LegendDot(Transform parent, Sprite sprite, ref float x, float y, Color c, string text)
            {
                Img(parent, "Segno", new Vector2(x, -(y + 3f)), new Vector2(11f, 11f), c, sprite);
                var t = HubText(parent, "SegnoNome", x + 16f, y, 110f, 18f, 13, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
                t.text = text; t.supportRichText = false;
                x += 16f + 14f + text.Length * 7.4f;
            }

            void AddBikeDot(HUD h)
            {
                var rim = h.DmDisc("Moto" + bikeRims.Count, win, 15f, DmRim);
                h.DmDisc("Colore", rim.transform, 10f, DmBike);
                rim.gameObject.SetActive(false);
                bikeRims.Add(rim);
            }

            void AddEnemyDot(HUD h)
            {
                var rim = h.DmDisc("Nemico" + enemyRims.Count, win, 8f, DmRim);
                var core = h.DmDisc("Nucleo", rim.transform, 5f, DmRed);
                rim.transform.SetSiblingIndex(youRim != null ? youRim.transform.GetSiblingIndex() : rim.transform.GetSiblingIndex());
                rim.gameObject.SetActive(false);
                enemyRims.Add(rim); enemyCores.Add(core);
            }

            public override void OnShow(HUD h)
            {
                var tiles = h.DesertPaper();
                if (paper != null) paper.texture = tiles.Texture;
                textT = 0f; tintT = 0f;
            }

            public override void Tick(HUD h, in HubInput inp, float dt)
            {
                var gm = GameManager.I;
                var pl = gm != null ? gm.player : null;
                if (pl == null) return;
                Vector3 p = pl.transform.position;
                var tiles = h.DesertPaper();
                if (paper.texture != tiles.Texture) paper.texture = tiles.Texture;
                tiles.Update(p.x, p.z);
                paper.uvRect = DesertMapTiles.UvRect(p.x, p.z);
                tintT -= dt;
                if (tintT <= 0f)
                {
                    tintT = 0.25f;
                    float day = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(DesertMode.Clock.Now.daylight * 1.25f));
                    tint = Color.Lerp(new Color(0.46f, 0.55f, 0.85f), Color.white, day);
                    if (p.y < DesertWorld.Height(p.x, p.z) - 3f) tint *= 0.55f;
                    tint.a = 1f;
                }
                paper.color = tint;
                var rot = Quaternion.Euler(0f, 0f, -pl.yaw * Mathf.Rad2Deg);
                youRim.rectTransform.localRotation = rot; you.rectTransform.localRotation = rot;

                // la cattedrale: un rombo d'oro dove sta, o una freccia d'oro sul bordo che dice da che parte
                DesertWorld.CathedralCentre(out float hx, out float hz);
                float dx = hx - p.x, dz = hz - p.z;
                var homeAt = new Vector2(dx * Scale, dz * Scale);
                bool homeIn = Inside(homeAt);
                homeBoxRim.gameObject.SetActive(homeIn); homeLabel.gameObject.SetActive(homeIn);
                homeRim.gameObject.SetActive(!homeIn); home.gameObject.SetActive(!homeIn);
                if (homeIn)
                {
                    homeBoxRim.rectTransform.anchoredPosition = homeAt;
                    homeLabel.rectTransform.anchoredPosition = homeAt + new Vector2(16f, 8f);
                }
                else
                {
                    var at = OnEdge(homeAt, 14f);
                    var hr = Quaternion.Euler(0f, 0f, -Mathf.Atan2(dx, dz) * Mathf.Rad2Deg);
                    homeRim.rectTransform.anchoredPosition = at; home.rectTransform.anchoredPosition = at;
                    homeRim.rectTransform.localRotation = hr; home.rectTransform.localRotation = hr;
                }

                // il garage: un quadrato turchese a meta' della fila delle piazzole (sul bordo se e' fuori)
                int slot = DesertBikes.SlotOf(pl);
                Vector3 g0 = DesertBikes.HomeFor(slot < 0 ? 0 : slot);
                Vector3 gc = g0 + new Vector3(DesertGarage.Pitch * (DesertGarage.Bays - 1) * 0.5f, 0f, 0f);
                var gAt = At(gc, p);
                bool gIn = Inside(gAt);
                if (!gIn) gAt = OnEdge(gAt, 11f);
                garageRim.rectTransform.anchoredPosition = gAt;
                garageLabel.gameObject.SetActive(gIn);

                // le tue moto, parcheggiate: tondi turchesi (in sella sei la freccia rossa)
                int nb = 0;
                foreach (var b in DesertBike.All)
                {
                    if (b == null || b.puppet || b.slot != slot || b.rider != null) continue;
                    while (bikeRims.Count <= nb) AddBikeDot(h);
                    var bat = At(b.transform.position, p);
                    if (!Inside(bat)) bat = OnEdge(bat, 9f);
                    var rim = bikeRims[nb++];
                    if (!rim.gameObject.activeSelf) rim.gameObject.SetActive(true);
                    rim.rectTransform.anchoredPosition = bat;
                }
                for (; nb < bikeRims.Count; nb++) if (bikeRims[nb].gameObject.activeSelf) bikeRims[nb].gameObject.SetActive(false);

                // i compagni
                int m = 0;
                var game = Coop.Game;
                if (Coop.Active && game != null)
                    for (int s = 0; s < CoopProtocol.MaxPlayers && m < mateRims.Length; s++)
                    {
                        if (!game.Players.TryGetValue(s, out var peer) || peer == null || !peer.hasState || !peer.Shown) continue;
                        var at = At(peer.Feet, p);
                        var rim = mateRims[m]; var core = mateCores[m];
                        m++;
                        if (!peer.last.alive || !Inside(at)) { rim.gameObject.SetActive(false); continue; }
                        bool down = peer.last.downed;
                        rim.gameObject.SetActive(true);
                        rim.rectTransform.anchoredPosition = at;
                        rim.color = down ? Color.Lerp(DmRim, DmRed, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) : DmRim;
                        core.color = MateColors[Mathf.Clamp(s, 0, MateColors.Length - 1)];
                    }
                if (Seats.Split)
                    for (int si = 0; si < Seats.All.Count && m < mateRims.Length; si++)
                    {
                        var other = Seats.All[si];
                        if (other.player == null || other.player == pl) continue;
                        var at = At(other.player.transform.position, p);
                        var rim = mateRims[m]; var core = mateCores[m];
                        m++;
                        if (!other.player.alive || !Inside(at)) { rim.gameObject.SetActive(false); continue; }
                        rim.gameObject.SetActive(true);
                        rim.rectTransform.anchoredPosition = at;
                        rim.color = DmRim;
                        core.color = MateColors[Mathf.Clamp(other.index, 0, MateColors.Length - 1)];
                    }
                for (; m < mateRims.Length; m++) if (mateRims[m].gameObject.activeSelf) mateRims[m].gameObject.SetActive(false);

                // i nemici svegli, vicini
                int n = 0;
                foreach (var e in Enemy.All)
                {
                    if (e == null || e.IsDead || e.dormant) continue;
                    Vector3 d = e.transform.position - p; d.y = 0f;
                    if (d.sqrMagnitude > EnemyRange * EnemyRange) continue;
                    if (n >= 48) break;
                    while (enemyRims.Count <= n) AddEnemyDot(h);
                    var rim = enemyRims[n]; var core = enemyCores[n];
                    bool boss = Enemy.IsBoss(e.type);
                    if (!rim.gameObject.activeSelf) rim.gameObject.SetActive(true);
                    rim.rectTransform.anchoredPosition = At(e.transform.position, p);
                    float sz = boss ? 14f : 9f;
                    rim.rectTransform.sizeDelta = new Vector2(sz, sz); core.rectTransform.sizeDelta = new Vector2(sz - 3f, sz - 3f);
                    core.color = boss ? new Color(0.60f, 0.04f, 0.04f) : DmRed;
                    n++;
                }
                for (; n < enemyRims.Count; n++) if (enemyRims[n].gameObject.activeSelf) enemyRims[n].gameObject.SetActive(false);

                // i testi della colonna: cinque volte al secondo bastano
                textT -= dt;
                if (textT > 0f) return;
                textT = 0.2f;
                float hour = DesertMode.Clock.hour;
                int hh = Mathf.FloorToInt(hour) % 24, mm = Mathf.FloorToInt((hour - Mathf.Floor(hour)) * 60f) % 60;
                var sky = DesertMode.Clock.Now;
                string phase = sky.night > 0.5f ? "notte" : sky.twilight > 0.5f ? "alba o tramonto" : "giorno";
                clockText.text = $"ore {hh:00}:{mm:00}, {phase}";
                float groundY = DesertWorld.Height(p.x, p.z);
                float homeDist = Mathf.Sqrt(dx * dx + dz * dz);
                float garDist = Mathf.Sqrt((gc.x - p.x) * (gc.x - p.x) + (gc.z - p.z) * (gc.z - p.z));
                sb.Length = 0;
                sb.Append(DesertMode.BiomeName(DesertBiomes.At(p.x, p.z, DesertWorld.Seed).Dominant)).Append('\n');
                sb.Append("x ").Append(Mathf.RoundToInt(p.x)).Append("   z ").Append(Mathf.RoundToInt(p.z));
                if (p.y < groundY - 3f) sb.Append("   sottoterra");
                sb.Append('\n');
                sb.Append("Cattedrale: ").Append(homeDist < 12f ? "sei qui" : HubDistance(homeDist) + " a " + HubCompass(dx, dz)).Append('\n');
                sb.Append("Garage: ").Append(garDist < 12f ? "sei qui" : HubDistance(garDist) + " a " + HubCompass(gc.x - p.x, gc.z - p.z));
                whereText.text = sb.ToString();

                sb.Length = 0;
                int shown = 0, total = 0;
                foreach (var b in DesertBike.All)
                {
                    if (b == null || b.puppet || b.slot != slot) continue;
                    total++;
                    if (shown >= 6) continue;
                    shown++;
                    string state;
                    if (b.rider != null) state = "in sella";
                    else
                    {
                        Vector3 bd = b.transform.position - p; bd.y = 0f;
                        Vector3 hd = b.transform.position - b.home; hd.y = 0f;
                        state = hd.sqrMagnitude < 4f ? "nel garage" : HubDistance(bd.magnitude) + " a " + HubCompass(bd.x, bd.z);
                    }
                    sb.Append("<color=#4DCCD1>●</color> ").Append(b.Def != null ? b.Def.name : "Moto").Append("  <color=#9a8f78>").Append(state).Append("</color>\n");
                }
                if (total > shown) sb.Append("<color=#9a8f78>e altre ").Append(total - shown).Append("</color>");
                if (total == 0) sb.Append("<color=#9a8f78>Nessuna: il garage e' vuoto.</color>");
                bikeText.text = sb.ToString().TrimEnd('\n');
                string goal = h.objectiveText != null ? h.objectiveText.text : "";
                goalText.text = string.IsNullOrEmpty(goal) ? "<color=#9a8f78>Niente di preciso: il deserto e' tuo.</color>" : goal;
            }
        }
    }
}
