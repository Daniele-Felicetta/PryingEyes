using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il pannello dei compagni (0.7.2: "piu' grande, piu' grafico, piu' accessibile, da ingrandire nelle impostazioni"). In
    /// cooperativa mostra gli altri giocatori della rete, nello schermo condiviso gli altri posti. Sta a sinistra, tra l'ondata e la vita
    /// (prima era in alto a destra, sotto la minimappa). Una scheda per compagno:
    /// - il numero del giocatore in un cerchio del suo colore (lo stesso del suo corpo) e una striscia dello stesso colore;
    /// - il nome, la vita in percentuale e una barra spessa (verde, ambra, rossa: il numero c'e' sempre, il colore non e' l'unico segno);
    /// - quanto e' lontano e una freccia che punta verso di lui rispetto a dove guardi;
    /// - a terra: la scheda pulsa di rosso, la barra diventa il tempo che resta per rialzarlo e una riga dice come fare; morto: grigia.
    /// La dimensione la sceglie il giocatore (Impostazioni, "HUD dei compagni", 60%-180%: GameSettings.CoopHudScale).</summary>
    public partial class HUD
    {
        const float CardW = 316f, CardH = 68f, CardGap = 8f, CardBar = 240f;
        const int MaxMates = 3;
        static readonly Color[] MateColors =
        {
            new Color(0.62f, 0.74f, 0.42f), new Color(0.47f, 0.64f, 0.96f), new Color(0.93f, 0.47f, 0.45f), new Color(0.95f, 0.78f, 0.38f),
        };
        static readonly Color CardFrame = new Color(0.86f, 0.76f, 0.55f, 0.42f), CardBg = new Color(0.07f, 0.065f, 0.06f, 0.86f), CardDown = new Color(0.46f, 0.04f, 0.04f, 0.90f),
            CardDead = new Color(0.10f, 0.10f, 0.10f, 0.70f), HpGood = new Color(0.55f, 0.82f, 0.50f), HpMid = new Color(0.96f, 0.74f, 0.27f),
            HpLow = new Color(0.94f, 0.28f, 0.22f), Bleed = new Color(1f, 0.30f, 0.24f);

        sealed class MateCard
        {
            public RectTransform root, arrow;
            public Image bg, strip, badge, fill;
            public Text num, name, pct, status, dist;
            public bool downed;
        }

        struct Mate
        {
            public string name, role;
            public int index, key;
            public bool waiting, alive, downed;
            public float hp, bleed;
            public Vector3 pos;
        }

        RectTransform coopPanel;
        Text coopHeading;
        readonly MateCard[] mateCards = new MateCard[MaxMates];
        readonly List<Mate> mates = new List<Mate>(4);
        readonly HashSet<int> matesDown = new HashSet<int>();
        float coopRefresh;
        /// <summary>Per le foto delle prove: una dimensione del pannello che non finisce nelle impostazioni salvate (negativo = quella scelta).</summary>
        internal static float coopScaleOverride = -1f;

        void BuildCoopUI()
        {
            if (coopPanel != null) return;
            var go = Child("PannelloCompagni");
            coopPanel = Comp<RectTransform>(go);
            coopPanel.anchorMin = coopPanel.anchorMax = coopPanel.pivot = new Vector2(0f, 1f);
            coopPanel.anchoredPosition = new Vector2(24f, -190f);       // sotto ondata, nemici e perk; sopra round, punti e vita
            coopPanel.sizeDelta = new Vector2(CardW, 22f + MaxMates * (CardH + CardGap));
            coopHeading = Txt(coopPanel, "Titolo", new Vector2(2f, 0f), new Vector2(CardW, 18f), 13, TextAnchor.UpperLeft, Dim, FontStyle.Bold);
            for (int i = 0; i < MaxMates; i++) mateCards[i] = BuildCard(i);
            go.SetActive(false);
        }

        MateCard BuildCard(int i)
        {
            var c = new MateCard();
            c.root = Box(coopPanel, "Compagno" + i, new Vector2(0f, -22f - i * (CardH + CardGap)), new Vector2(CardW, CardH));
            // una cornice d'ottone sottile attorno al fondo: la scheda si legge anche sul buio (prima il fondo scuro spariva)
            Img(c.root, "Cornice", Vector2.zero, new Vector2(CardW, CardH), CardFrame, null);
            c.bg = Img(c.root, "Fondo", new Vector2(1f, -1f), new Vector2(CardW - 2f, CardH - 2f), CardBg, null);
            c.strip = Img(c.root, "Colore", new Vector2(0f, 0f), new Vector2(5f, CardH), Color.white, null);
            c.badge = Img(c.root, "Numero", new Vector2(13f, -12f), new Vector2(44f, 44f), Color.white, CircleSprite());
            c.num = Txt(c.badge.rectTransform, "Cifra", Vector2.zero, new Vector2(44f, 44f), 24, TextAnchor.MiddleCenter, new Color(0.06f, 0.05f, 0.04f), FontStyle.Bold);
            c.num.GetComponent<Shadow>().enabled = false;
            c.name = Txt(c.root, "Nome", new Vector2(68f, -6f), new Vector2(168f, 24f), 19, TextAnchor.UpperLeft, Ink, FontStyle.Bold);
            c.name.supportRichText = false;
            c.name.resizeTextForBestFit = true; c.name.resizeTextMinSize = 12; c.name.resizeTextMaxSize = 19;
            c.pct = Txt(c.root, "Vita", new Vector2(226f, -6f), new Vector2(82f, 24f), 17, TextAnchor.UpperRight, Ink, FontStyle.Bold);
            Img(c.root, "Binario", new Vector2(68f, -34f), new Vector2(CardBar, 10f), new Color(0f, 0f, 0f, 0.65f), null);
            c.fill = Img(c.root, "Barra", new Vector2(68f, -34f), new Vector2(CardBar, 10f), HpGood, null);
            c.status = Txt(c.root, "Stato", new Vector2(68f, -47f), new Vector2(170f, 18f), 14, TextAnchor.UpperLeft, Dim, FontStyle.Normal);
            c.dist = Txt(c.root, "Distanza", new Vector2(206f, -47f), new Vector2(76f, 18f), 14, TextAnchor.UpperRight, Ink, FontStyle.Bold);
            var arrow = Img(c.root, "Freccia", new Vector2(298f, -56f), new Vector2(16f, 16f), Ink, ArrowSprite());
            c.arrow = arrow.rectTransform;
            c.arrow.pivot = new Vector2(0.5f, 0.5f);
            c.root.gameObject.SetActive(false);
            return c;
        }

        void UpdateCoopUI()
        {
            if (coopPanel == null) return;
            coopRefresh -= Time.unscaledDeltaTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            // le schede a terra pulsano a ogni fotogramma; il resto si aggiorna quattro volte al secondo
            for (int i = 0; i < MaxMates; i++)
            {
                var c = mateCards[i];
                if (c.downed && c.root.gameObject.activeSelf) c.bg.color = Color.Lerp(CardBg, CardDown, 0.55f + 0.45f * pulse);
            }
            if (coopRefresh > 0f) return;
            coopRefresh = 0.25f;
            GatherMates();
            bool show = Coop.Active || mates.Count > 0;
            if (coopPanel.gameObject.activeSelf != show) coopPanel.gameObject.SetActive(show);
            if (!show) return;
            float scale = coopScaleOverride > 0f ? coopScaleOverride : GameSettings.CoopHudScale;
            coopPanel.localScale = new Vector3(scale, scale, 1f);
            coopHeading.text = Coop.Active ? (mates.Count == 0 ? "IN ATTESA DI COMPAGNI" : "COMPAGNI  ·  " + (mates.Count + 1) + "/" + CoopProtocol.MaxPlayers) : "COMPAGNI";
            var me = GameManager.I != null ? GameManager.I.player : null;
            for (int i = 0; i < MaxMates; i++)
            {
                var c = mateCards[i];
                if (i >= mates.Count) { if (c.root.gameObject.activeSelf) c.root.gameObject.SetActive(false); continue; }
                if (!c.root.gameObject.activeSelf) c.root.gameObject.SetActive(true);
                FillCard(c, mates[i], me);
            }
            // un compagno appena caduto: lo si dice anche al centro dello schermo
            foreach (var m in mates)
            {
                if (m.downed) { if (matesDown.Add(m.key)) Toast($"{m.name} e' a terra: tieni {(PadControls.Preferred ? "X" : "F")} vicino a lui per rialzarlo."); }
                else matesDown.Remove(m.key);
            }
        }

        void GatherMates()
        {
            mates.Clear();
            var game = Coop.Game;
            if (Coop.Active && game != null)
            {
                for (int slot = 0; slot < CoopProtocol.MaxPlayers && mates.Count < MaxMates; slot++)
                {
                    if (!game.Players.TryGetValue(slot, out var peer) || peer == null) continue;
                    var s = peer.last;
                    mates.Add(new Mate
                    {
                        name = peer.playerName, role = slot == 0 ? "ospite" : "", index = slot, key = slot,
                        waiting = !peer.hasState || !peer.Shown, alive = s.alive, downed = s.alive && s.downed,
                        hp = Mathf.Clamp01(s.hp), bleed = s.bleed, pos = peer.Feet,
                    });
                }
            }
            else if (Seats.Split)
            {
                foreach (var st in Seats.All)
                {
                    var p = st.player;
                    if (st.hud == this || p == null || mates.Count >= MaxMates) continue;
                    mates.Add(new Mate
                    {
                        name = st.Name, role = "", index = st.index, key = 16 + st.index,
                        waiting = false, alive = p.alive, downed = p.alive && p.Downed,
                        hp = Mathf.Clamp01(p.hp / Mathf.Max(1f, p.MaxHp)), bleed = p.BleedLeft, pos = p.transform.position,
                    });
                }
            }
        }

        void FillCard(MateCard c, Mate m, PlayerController me)
        {
            var col = MateColors[Mathf.Clamp(m.index, 0, MateColors.Length - 1)];
            c.strip.color = col;
            c.badge.color = m.alive || m.waiting ? col : Color.Lerp(col, new Color(0.3f, 0.3f, 0.3f), 0.7f);
            c.num.text = (m.index + 1).ToString();
            c.name.text = string.IsNullOrEmpty(m.role) ? m.name : m.name + "  ·  " + m.role;
            c.downed = m.downed && !m.waiting;
            float bar;
            Color barCol;
            if (m.waiting)
            {
                c.bg.color = CardBg; c.pct.text = ""; c.status.text = "Sta raggiungendo il piano…";
                bar = 0f; barCol = HpGood;
            }
            else if (!m.alive)
            {
                c.bg.color = CardDead; c.pct.text = "MORTO"; c.pct.color = Dim; c.status.text = "Torna all'ingresso…";
                bar = 0f; barCol = HpLow;
            }
            else if (m.downed)
            {
                c.pct.text = "A TERRA"; c.pct.color = new Color(1f, 0.82f, 0.78f);
                c.status.text = $"Rialzalo: {Mathf.CeilToInt(m.bleed)} s";
                bar = Mathf.Clamp01(m.bleed / PlayerController.BleedOutTime); barCol = Bleed;
            }
            else
            {
                c.bg.color = CardBg;
                c.pct.text = Mathf.RoundToInt(m.hp * 100f) + "%"; c.pct.color = Ink;
                c.status.text = m.hp <= 0.3f ? "Ferito" : "";
                bar = m.hp; barCol = m.hp > 0.6f ? HpGood : m.hp > 0.3f ? HpMid : HpLow;
            }
            c.fill.rectTransform.sizeDelta = new Vector2(CardBar * bar, 10f);
            c.fill.color = barCol;
            bool where = me != null && !m.waiting && m.alive;
            float d = where ? Vector3.Distance(me.transform.position, m.pos) : 0f;
            c.dist.text = where ? Mathf.RoundToInt(d) + " m" : "";
            bool arrow = where && d > 2f;
            if (c.arrow.gameObject.activeSelf != arrow) c.arrow.gameObject.SetActive(arrow);
            if (arrow)
            {
                Vector3 to = m.pos - me.transform.position;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float rel = Mathf.DeltaAngle(me.yaw * Mathf.Rad2Deg, bearing);      // positivo = alla mia destra
                c.arrow.localRotation = Quaternion.Euler(0f, 0f, -rel);
                c.arrow.GetComponent<Image>().color = m.downed ? Bleed : Ink;
            }
        }

        // ------------------------------------------------------------------ mattoni
        static RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Color col, Sprite sprite)
        {
            var rt = Box(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = col; img.raycastTarget = false;
            if (sprite != null) img.sprite = sprite;
            return img;
        }

        static Text Txt(Transform parent, string name, Vector2 pos, Vector2 size, int fs, TextAnchor a, Color col, FontStyle style)
        {
            var rt = Box(parent, name, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Utility(); t.fontSize = fs; t.alignment = a; t.color = col; t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        static Sprite mateBadge;

        /// <summary>Un cerchio bianco a bordi morbidi (il numero del compagno), fatto una volta. La freccia e' quella dell'ascensore
        /// (HUD.Stealth.cs, ArrowSprite).</summary>
        static Sprite CircleSprite()
        {
            if (mateBadge != null) return mateBadge;
            const int N = 64;
            var px = new Color32[N * N];
            float r = N * 0.5f - 1.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - N * 0.5f, dy = y + 0.5f - N * 0.5f;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f) * 255f));
                }
            mateBadge = MateSprite(px, N, "CerchioCompagno");
            return mateBadge;
        }

        static Sprite MateSprite(Color32[] px, int n, string name)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
