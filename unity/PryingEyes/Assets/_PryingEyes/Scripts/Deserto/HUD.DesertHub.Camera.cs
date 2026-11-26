using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    public partial class HUD
    {
        /// <summary>La pagina Telecamera del menu di Select: le impostazioni della camera in una colonna di righe (campo visivo, prospettiva, intensita' dei movimenti, sensibilita' del mouse e del controller, assi Y
        /// invertiti) e il bottone per tornare ai valori di partenza; a destra un ventaglio che mostra il campo visivo. Sono le stesse impostazioni del menu di pausa (GameSettings, che le salva da sola): qui si
        /// cambiano senza lasciare la partita. Con le frecce o la croce si cambia il valore (tenendo premuto corre), con il mouse si trascina lo slider, si clicca l'interruttore o si gira la rotella.</summary>
        public sealed class HubCameraPage : HubPage
        {
            sealed class Setting
            {
                public string title, note;
                public bool toggle, action;
                public float min, max, step, def;
                public System.Func<float> get;
                public System.Action<float> set;
                public System.Func<float, string> fmt;
                public RectTransform root;
                public Image back, ring, track, fill, handle, pill, knob;
                public Text valueText;
                public Rect rect, trackRect;
            }

            const float ListW = 640f, RowH = 42f, RowGap = 3f, TrackX = 350f, TrackW = 180f;
            const float FanX = 668f, FanW = HubContentW - FanX, FanH = 300f, FanSlivers = 40;
            readonly List<Setting> settings = new List<Setting>();
            readonly List<Image> slivers = new List<Image>();
            Image edgeL, edgeR;
            Text fanValue, fanNote;
            int row;
            bool dragging;
            float shownFov = -1f;

            public HubCameraPage() { label = "Telecamera"; }

            public override string Hint => "Su e giù: scegli     Sinistra e destra: cambia (tieni premuto per correre)     Mouse: trascina, clic, rotella     Q E  LB RB: pagina     Esc  B  Select: chiudi";

            static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
            static string Two(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

            void Add(Setting s) { settings.Add(s); }

            public override void Build(HUD h, RectTransform parent)
            {
                Add(new Setting
                {
                    title = "Campo visivo", note = "L'angolo della camera. In sella si allarga da solo con la velocità, fino a 17° in più.",
                    min = 60f, max = 105f, step = 1f, def = 75f, get = () => GameSettings.Fov, set = GameSettings.SetFov, fmt = v => Mathf.RoundToInt(v) + "°",
                });
                Add(new Setting
                {
                    title = "Prospettiva", note = "Camera dagli occhi o dietro il personaggio, anche in moto.",
                    toggle = true, def = 0f, get = () => GameSettings.ThirdPerson ? 1f : 0f, set = v => GameSettings.SetThirdPerson(v > 0.5f), fmt = v => v > 0.5f ? "Terza persona" : "Prima persona",
                });
                Add(new Setting
                {
                    title = "Distanza 3ª persona", note = "Distanza dal personaggio, a piedi e in sella.",
                    min = GameSettings.ThirdDistanceMin, max = GameSettings.ThirdDistanceMax, step = 0.05f, def = GameSettings.ThirdDistanceDefault,
                    get = () => GameSettings.ThirdPersonFraming.z, set = GameSettings.SetThirdDistance, fmt = GameMenu.CameraMeters,
                });
                Add(new Setting
                {
                    title = "Altezza 3ª persona", note = "Posizione verticale della camera.",
                    min = GameSettings.ThirdHeightMin, max = GameSettings.ThirdHeightMax, step = 0.05f, def = GameSettings.ThirdHeightDefault,
                    get = () => GameSettings.ThirdPersonFraming.y, set = GameSettings.SetThirdHeight, fmt = GameMenu.CameraHeight,
                });
                Add(new Setting
                {
                    title = "Spalla 3ª persona", note = "Spostamento a sinistra, al centro o a destra.",
                    min = GameSettings.ThirdShoulderMin, max = GameSettings.ThirdShoulderMax, step = 0.05f, def = GameSettings.ThirdShoulderDefault,
                    get = () => GameSettings.ThirdPersonFraming.x, set = GameSettings.SetThirdShoulder, fmt = GameMenu.CameraShoulder,
                });
                Add(new Setting
                {
                    title = "Intensità dei movimenti", note = "Vibrazioni, inclinazione in curva, rinculo e scossoni.",
                    min = 0f, max = 1f, step = 0.05f, def = 1f, get = () => GameSettings.Motion, set = GameSettings.SetMotion, fmt = Pct,
                });
                Add(new Setting
                {
                    title = "Sensibilità del mouse", note = "Quanto gira la camera a ogni movimento del mouse.",
                    min = 0.25f, max = 3f, step = 0.05f, def = 1f, get = () => GameSettings.Sensitivity, set = GameSettings.SetSensitivity, fmt = Two,
                });
                Add(new Setting
                {
                    title = "Sensibilità del controller", note = "Quanto gira la camera con lo stick destro.",
                    min = GameSettings.PadSensitivityMin, max = GameSettings.PadSensitivityMax, step = 0.05f, def = 1f, get = () => GameSettings.PadSensitivity, set = GameSettings.SetPadSensitivity, fmt = Two,
                });
                Add(new Setting
                {
                    title = "Asse Y del mouse invertito", note = "Il mouse in avanti guarda in basso.",
                    toggle = true, def = 0f, get = () => GameSettings.InvertMouseY ? 1f : 0f, set = v => GameSettings.SetInvertMouseY(v > 0.5f), fmt = v => v > 0.5f ? "Invertito" : "Normale",
                });
                Add(new Setting
                {
                    title = "Asse Y del controller invertito", note = "Lo stick in avanti guarda in basso.",
                    toggle = true, def = 0f, get = () => GameSettings.InvertPadY ? 1f : 0f, set = v => GameSettings.SetInvertPadY(v > 0.5f), fmt = v => v > 0.5f ? "Invertito" : "Normale",
                });
                Add(new Setting { title = "Valori di partenza", note = "Riporta tutte queste impostazioni a quelle con cui il gioco nasce.", action = true });

                var circle = CircleSprite();
                for (int i = 0; i < settings.Count; i++)
                {
                    var s = settings[i];
                    float y = i * (RowH + RowGap);
                    s.rect = new Rect(0f, y, ListW, RowH);
                    s.root = Box(parent, "Impostazione" + i, new Vector2(0f, -y), new Vector2(ListW, RowH));
                    s.ring = Img(s.root, "Anello", new Vector2(-3f, 3f), new Vector2(ListW + 6f, RowH + 6f), HubGoldRing, null);
                    s.back = Img(s.root, "Fondo", Vector2.zero, new Vector2(ListW, RowH), HubCard, null);
                    var t = HubText(s.root, "Titolo", 14f, 2f, 330f, 20f, 16, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                    t.text = s.title; t.supportRichText = false;
                    var n = HubText(s.root, "Nota", 14f, 22f, 330f, 20f, 10, TextAnchor.UpperLeft, HubDimC, FontStyle.Normal);
                    n.text = s.note; n.supportRichText = false;
                    if (s.action)
                    {
                        s.pill = Img(s.root, "Bottone", new Vector2(TrackX, -9f), new Vector2(ListW - TrackX - 14f, RowH - 18f), new Color(0.40f, 0.12f, 0.08f, 1f), null);
                        s.valueText = HubText(s.root, "Valore", TrackX, 0f, ListW - TrackX - 14f, RowH, 15, TextAnchor.MiddleCenter, HubInkC, FontStyle.Bold);
                        s.valueText.text = "RIPRISTINA";
                    }
                    else if (s.toggle)
                    {
                        s.pill = Img(s.root, "Interruttore", new Vector2(TrackX, -(RowH * 0.5f - 12f)), new Vector2(46f, 24f), HubLockedBack, circle);
                        s.knob = Img(s.root, "Pomello", new Vector2(TrackX + 3f, -(RowH * 0.5f - 9f)), new Vector2(18f, 18f), HubInkC, circle);
                        s.valueText = HubText(s.root, "Valore", TrackX + 58f, 0f, ListW - TrackX - 70f, RowH, 15, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                        s.trackRect = new Rect(s.rect.x + TrackX - 4f, s.rect.y + 8f, 190f, RowH - 16f);
                    }
                    else
                    {
                        s.track = Img(s.root, "Guida", new Vector2(TrackX, -(RowH * 0.5f - 4f)), new Vector2(TrackW, 8f), new Color(0.05f, 0.04f, 0.04f, 1f), null);
                        s.fill = Img(s.root, "Pieno", new Vector2(TrackX, -(RowH * 0.5f - 4f)), new Vector2(TrackW * 0.5f, 8f), HubBrassC, null);
                        s.handle = Img(s.root, "Maniglia", new Vector2(TrackX, -(RowH * 0.5f - 11f)), new Vector2(14f, 22f), HubInkC, null);
                        s.valueText = HubText(s.root, "Valore", TrackX + TrackW + 16f, 0f, ListW - TrackX - TrackW - 24f, RowH, 16, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                        s.trackRect = new Rect(s.rect.x + TrackX - 8f, s.rect.y + 6f, TrackW + 16f, RowH - 12f);
                    }
                    s.valueText.supportRichText = false;
                }

                // il ventaglio del campo visivo
                HubRect(parent, "VentaglioFondo", FanX, 0f, FanW, FanH, HubCard);
                HubRect(parent, "VentaglioFilo", FanX, 0f, FanW, 2f, HubBrassC);
                var ft = HubText(parent, "VentaglioTitolo", FanX + 14f, 8f, FanW - 28f, 18f, 12, TextAnchor.MiddleLeft, HubBrassC, FontStyle.Bold);
                ft.text = "CAMPO VISIVO"; ft.supportRichText = false;
                fanValue = HubText(parent, "VentaglioValore", FanX, 28f, FanW, 40f, 32, TextAnchor.MiddleCenter, HubInkC, FontStyle.Bold, true);
                fanValue.supportRichText = false;
                var fan = Box(parent, "Ventaglio", new Vector2(FanX, 0f), new Vector2(FanW, FanH));
                float ax = FanW * 0.5f, ay = FanH - 22f;
                for (int i = 0; i < FanSlivers; i++)
                {
                    var rt = Box(fan, "Raggio" + i, new Vector2(ax, -ay), new Vector2(5f, 190f));
                    rt.pivot = new Vector2(0.5f, 0f);
                    var img = rt.gameObject.AddComponent<Image>();
                    img.color = new Color(0.98f, 0.80f, 0.40f, 0.10f); img.raycastTarget = false;
                    slivers.Add(img);
                }
                edgeL = FanEdge(fan, ax, ay); edgeR = FanEdge(fan, ax, ay);
                // il tuo occhio, all'apice
                Img(fan, "Occhio", new Vector2(ax - 9f, -(ay - 6f)), new Vector2(18f, 18f), HubInkC, circle);
                Img(fan, "Pupilla", new Vector2(ax - 4f, -(ay - 1f)), new Vector2(8f, 8f), new Color(0.07f, 0.05f, 0.04f), circle);
                fanNote = HubText(parent, "VentaglioNota", FanX, FanH + 8f, FanW, 90f, 12, TextAnchor.UpperLeft, HubDimC, FontStyle.Normal);
                fanNote.text = "Le impostazioni si salvano da sole e sono condivise con il menu di pausa.";
            }

            static Image FanEdge(RectTransform fan, float ax, float ay)
            {
                var rt = Box(fan, "Bordo", new Vector2(ax, -ay), new Vector2(2f, 200f));
                rt.pivot = new Vector2(0.5f, 0f);
                var img = rt.gameObject.AddComponent<Image>();
                img.color = new Color(0.98f, 0.80f, 0.40f, 0.85f); img.raycastTarget = false;
                return img;
            }

            void DrawFan(float fov)
            {
                if (Mathf.Approximately(fov, shownFov)) return;
                shownFov = fov;
                float half = fov * 0.5f;
                for (int i = 0; i < slivers.Count; i++)
                    slivers[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-half, half, i / (float)(slivers.Count - 1)));
                edgeL.rectTransform.localRotation = Quaternion.Euler(0f, 0f, half);
                edgeR.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -half);
            }

            public override void OnShow(HUD h) { dragging = false; shownFov = -1f; }

            static float Snap(Setting s, float v)
            {
                v = Mathf.Clamp(v, s.min, s.max);
                return s.step > 0f ? Mathf.Clamp(s.min + Mathf.Round((v - s.min) / s.step) * s.step, s.min, s.max) : v;
            }

            void Set(Setting s, float v)
            {
                float old = s.get();
                v = s.toggle ? (v > 0.5f ? 1f : 0f) : Snap(s, v);
                if (Mathf.Abs(v - old) < 1e-4f) return;
                s.set(v);
            }

            void Reset(HUD h)
            {
                foreach (var s in settings) if (!s.action) s.set(s.def);
                h.HubSay("Impostazioni della telecamera riportate ai valori di partenza.", false);
                Sfx.UI(SoundBank.MagiaSigillo, 0.5f, 1.2f);
            }

            public override void Tick(HUD h, in HubInput inp, float dt)
            {
                int hover = -1;
                Vector2 q = inp.Local;
                if (inp.pointerIn) for (int i = 0; i < settings.Count; i++) if (settings[i].rect.Contains(q)) hover = i;
                if (inp.moved && hover >= 0 && hover != row) { row = hover; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
                if (inp.ny != 0) { int nr = Mathf.Clamp(row - inp.ny, 0, settings.Count - 1); if (nr != row) { row = nr; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); } }
                var cur = settings[row];

                // tastiera e controller: sinistra e destra cambiano, invio gira l'interruttore o preme il bottone
                if (inp.nx != 0 && !cur.action)
                {
                    if (cur.toggle) { Set(cur, cur.get() > 0.5f ? 0f : 1f); Sfx.UI(SoundBank.MagiaScelta, 0.3f, 1.2f); }
                    else Set(cur, cur.get() + inp.nx * cur.step);
                }
                if (inp.confirm)
                {
                    if (cur.action) Reset(h);
                    else if (cur.toggle) { Set(cur, cur.get() > 0.5f ? 0f : 1f); Sfx.UI(SoundBank.MagiaScelta, 0.3f, 1.2f); }
                }

                // il mouse: clic sulla riga, trascinare lo slider, la rotella
                if (inp.pointerIn && hover >= 0)
                {
                    var s = settings[hover];
                    if (inp.click)
                    {
                        row = hover;
                        if (s.action) Reset(h);
                        else if (s.toggle) { Set(s, s.get() > 0.5f ? 0f : 1f); Sfx.UI(SoundBank.MagiaScelta, 0.3f, 1.2f); }
                        else if (s.trackRect.Contains(q)) dragging = true;
                    }
                    if (inp.wheel != 0f && !s.action && !s.toggle) { row = hover; Set(s, s.get() + (inp.wheel > 0f ? 1f : -1f) * s.step); }
                }
                if (dragging)
                {
                    if (!inp.held) dragging = false;
                    else
                    {
                        var s = settings[row];
                        if (!s.toggle && !s.action) Set(s, Mathf.Lerp(s.min, s.max, Mathf.Clamp01((q.x - (s.rect.x + TrackX)) / TrackW)));
                    }
                }

                // il disegno
                for (int i = 0; i < settings.Count; i++)
                {
                    var s = settings[i];
                    bool selected = i == row;
                    s.back.color = selected ? Color.Lerp(HubCard, Color.white, 0.10f) : HubCard;
                    s.ring.enabled = selected;
                    if (s.action) { s.pill.color = selected ? new Color(0.62f, 0.18f, 0.12f, 1f) : new Color(0.40f, 0.12f, 0.08f, 1f); continue; }
                    float v = s.get();
                    s.valueText.text = s.fmt(v);
                    if (s.toggle)
                    {
                        bool on = v > 0.5f;
                        s.pill.color = on ? new Color(0.50f, 0.16f, 0.10f, 1f) : HubLockedBack;
                        s.knob.rectTransform.anchoredPosition = new Vector2(TrackX + (on ? 25f : 3f), -(RowH * 0.5f - 9f));
                        s.knob.color = on ? HubInkC : HubDimC;
                    }
                    else
                    {
                        float t = Mathf.InverseLerp(s.min, s.max, v);
                        s.fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, TrackW * t), 8f);
                        s.handle.rectTransform.anchoredPosition = new Vector2(TrackX + TrackW * t - 7f, -(RowH * 0.5f - 11f));
                        s.handle.color = selected ? Color.white : HubInkC;
                    }
                }
                float fov = GameSettings.Fov;
                fanValue.text = Mathf.RoundToInt(fov) + "°";
                DrawFan(fov);
            }
        }
    }
}
