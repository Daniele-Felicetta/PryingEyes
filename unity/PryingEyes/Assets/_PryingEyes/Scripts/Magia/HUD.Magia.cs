using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La mano sinistra nell'HUD, in basso a destra sopra l'equipaggiamento: la barra dell'essenza, la magia scelta [3] e il
    /// sigillo scelto [3 tenuto] con il quadrante del tempo di recupero, il tempo che resta al sigillo sull'arma in mano e i due supremi.
    /// Compare solo quando hai comprato almeno un nodo di un grimorio.</summary>
    public partial class HUD
    {
        Slider essenceBar;
        Image spellIcon, spellCool, spellFrame, infIcon, infCool, infFrame;
        Text spellKey, infKey, magicLine, essenceText, cycleHint;
        int shownPad = -1;               // il dispositivo dei suggerimenti scritti (si riscrivono quando cambia; -1 = mai scritti)
        RectTransform infTimer;
        Image infTimerFill;
        GameObject magicRoot;
        const float MagicY = 206f, MagicIcon = 38f;

        static readonly Color EssenceCol = new Color(0.78f, 0.16f, 0.30f), Bone = new Color(0.93f, 0.86f, 0.72f);

        void BuildMagicUI()
        {
            magicRoot = Child("Magia");
            var mrt = Comp<RectTransform>(magicRoot);
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one; mrt.offsetMin = mrt.offsetMax = Vector2.zero;
            essenceBar = MakeBar("Essenza", new Vector2(1, 0), new Vector2(-28, MagicY), new Vector2(220, 5), EssenceCol);
            essenceBar.transform.SetParent(magicRoot.transform, true);
            essenceText = MakeText("EssenzaNum", new Vector2(1, 0), new Vector2(-254, MagicY - 6), new Vector2(120, 16), 11, TextAnchor.LowerRight, "", Dim, FontStyle.Bold);
            essenceText.transform.SetParent(magicRoot.transform, true);
            float y = MagicY + 12f;
            MakeSlot("Magia", new Vector2(-28f - MagicIcon, y), out spellFrame, out spellIcon, out spellCool, out spellKey, "3");
            MakeSlot("Sigillo", new Vector2(-28f - MagicIcon * 2f - 14f, y), out infFrame, out infIcon, out infCool, out infKey, "3 ▮");
            magicLine = MakeText("MagiaRiga", new Vector2(1, 0), new Vector2(-28f - MagicIcon * 2f - 26f, y + 10f), new Vector2(420, 18), 12, TextAnchor.LowerRight, "", Bone, FontStyle.Normal);
            magicLine.transform.SetParent(magicRoot.transform, true);
            // 0.7.1: come si cambiano magia e sigillo (prima non lo diceva niente: "non c'e' un modo per cambiare i sigilli")
            cycleHint = MakeText("MagiaCambio", new Vector2(1, 0), new Vector2(-28f, y - 30f), new Vector2(300, 14), 10, TextAnchor.UpperRight, "", Dim, FontStyle.Bold);
            cycleHint.transform.SetParent(magicRoot.transform, true);
            var tgo = Child("SigilloTempo");
            tgo.transform.SetParent(magicRoot.transform, false);
            infTimer = Comp<RectTransform>(tgo);
            infTimer.anchorMin = infTimer.anchorMax = new Vector2(1, 0); infTimer.pivot = new Vector2(1, 0);
            infTimer.anchoredPosition = new Vector2(-28f - MagicIcon - 14f, y - 4f);
            infTimer.sizeDelta = new Vector2(MagicIcon, 2.5f);
            var bg = Comp<Image>(tgo); bg.color = new Color(0f, 0f, 0f, 0.5f); bg.raycastTarget = false;
            var fgo = SubChild(tgo, "Riempimento");
            var frt = Comp<RectTransform>(fgo);
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero; frt.pivot = new Vector2(0f, 0.5f);
            infTimerFill = Comp<Image>(fgo); infTimerFill.raycastTarget = false;
            infTimerFill.sprite = PerkIcons.White(); infTimerFill.type = Image.Type.Filled; infTimerFill.fillMethod = Image.FillMethod.Horizontal;
            magicRoot.SetActive(false);
        }

        void MakeSlot(string name, Vector2 pos, out Image frame, out Image icon, out Image cool, out Text key, string keyText)
        {
            frame = MakeImage(name + "Cornice", new Color(0.05f, 0.02f, 0.02f, 0.78f));
            frame.sprite = GrimoireIcons.Disc();
            PlaceMagic(frame, pos, MagicIcon);
            var halo = MakeImage(name + "Alone", new Color(0.55f, 0.32f, 0.20f, 0.9f));
            halo.sprite = GrimoireIcons.Halo();
            PlaceMagic(halo, pos, MagicIcon);
            halo.transform.SetParent(magicRoot.transform, true);
            icon = MakeImage(name + "Icona", Bone);
            PlaceMagic(icon, pos + new Vector2(-MagicIcon * 0.18f, MagicIcon * 0.18f), MagicIcon * 0.64f);
            cool = MakeImage(name + "Recupero", new Color(0f, 0f, 0f, 0.66f));
            cool.sprite = GrimoireIcons.Disc();
            cool.type = Image.Type.Filled; cool.fillMethod = Image.FillMethod.Radial360; cool.fillOrigin = (int)Image.Origin360.Top; cool.fillClockwise = false;
            PlaceMagic(cool, pos, MagicIcon);
            key = MakeText(name + "Tasto", new Vector2(1, 0), pos + new Vector2(0f, -15f), new Vector2(MagicIcon, 14f), 10, TextAnchor.UpperCenter, keyText, Dim, FontStyle.Bold);
            foreach (var g in new Graphic[] { frame, icon, cool, key }) g.transform.SetParent(magicRoot.transform, true);
        }

        static void PlaceMagic(Image img, Vector2 pos, float size)
        {
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
        }

        int shownSpell = -2, shownInf = -2;

        void UpdateMagicUI(float dt)
        {
            if (magicRoot == null) return;
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            if (pl == null) { if (magicRoot.activeSelf) magicRoot.SetActive(false); return; }
            var s = pl.Arcana;
            bool show = s.Witch != 0 || s.Arsenal != 0;
            if (magicRoot.activeSelf != show) magicRoot.SetActive(show);
            if (!show) return;
            // i tasti sotto le icone e il cambio, per la tastiera o per il controller
            bool pad = PadControls.Preferred;
            if ((pad ? 1 : 0) != shownPad)
            {
                shownPad = pad ? 1 : 0;
                spellKey.text = pad ? "LB" : "3";
                infKey.text = pad ? "LB ▮" : "3 ▮";
                cycleHint.text = pad ? "CAMBIA: \u2190 sigillo  \u00b7  magia \u2192" : "CAMBIA: 6 magia  \u00b7  7 sigillo";
            }
            essenceBar.value = s.EssenceMax > 0f ? s.Essence / s.EssenceMax : 0f;
            essenceText.text = $"ESSENZA {Mathf.FloorToInt(s.Essence)}";
            // magia
            int sp = s.SelectedSpell;
            if (sp != shownSpell) { shownSpell = sp; spellIcon.sprite = sp >= 0 ? GrimoireIcons.Get(Grimoire.Witch[sp].icon) : null; }
            spellIcon.enabled = sp >= 0;
            float sc = sp >= 0 ? s.CooldownFrac(sp, false) : 0f;
            spellCool.fillAmount = sc;
            spellCool.enabled = sc > 0f;
            bool spellReady = sp >= 0 && sc <= 0f && (s.EssenzaLeft > 0f || s.Essence >= MagicState.SpellCost(sp) || (s.BloodPact && pl.hp > MagicState.SpellCost(sp) - s.Essence + 20f));
            spellIcon.color = spellReady ? (s.EssenzaLeft > 0f ? Color.Lerp(Bone, MagicFx.Essence, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f)) : Bone) : new Color(0.55f, 0.5f, 0.45f, 0.75f);
            // sigillo
            int inf = s.SelectedInfusion;
            if (inf != shownInf) { shownInf = inf; infIcon.sprite = inf >= 0 ? GrimoireIcons.Get(Grimoire.Arsenal[inf].icon) : null; }
            infIcon.enabled = inf >= 0;
            float ic = inf >= 0 ? s.CooldownFrac(inf, true) : 0f;
            infCool.fillAmount = ic; infCool.enabled = ic > 0f;
            bool infReady = inf >= 0 && ic <= 0f && s.Essence >= MagicState.InfusionCost(inf);
            infIcon.color = infReady ? Bone : new Color(0.55f, 0.5f, 0.45f, 0.75f);
            // il sigillo sull'arma in mano
            int slot = pl.weapon != null ? pl.weapon.SlotIndex : 0;
            int on = s.InfusionOn(slot);
            bool cursed = s.CursedOn(slot);
            float left = cursed ? s.CursedLeft : on >= 0 ? s.SlotInfusionLeft[slot] : 0f;
            float full = cursed ? MagicState.CursedTime : MagicState.InfusionTime;
            infTimer.gameObject.SetActive(left > 0f);
            if (left > 0f)
            {
                infTimerFill.fillAmount = Mathf.Clamp01(left / full);
                infTimerFill.color = MagicFx.OfInfusion(cursed ? 18 : on);
            }
            string line = "";
            if (s.EssenzaLeft > 0f) line = $"ESSENZA MALEDETTA  {Mathf.CeilToInt(s.EssenzaLeft)} s";
            else if (cursed) line = $"ARMA MALEDETTA  {Mathf.CeilToInt(s.CursedLeft)} s";
            else if (on >= 0) line = $"{Grimoire.Arsenal[on].name.ToUpperInvariant()}  {Mathf.CeilToInt(left)} s";
            else if (sp >= 0) line = Grimoire.Witch[sp].name;
            magicLine.text = line;
            magicLine.color = s.EssenzaLeft > 0f || cursed ? MagicFx.Essence : Bone;
        }
    }
}
