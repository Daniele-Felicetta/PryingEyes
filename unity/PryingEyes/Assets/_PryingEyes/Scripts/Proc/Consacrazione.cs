using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>L'altare della Consacrazione (il Pack-a-Punch dei Black Ops Zombies): un blocco di pietra con una croce d'oro inlaiata,
    /// due candele e un martello. Per 3000 punti consacra l'arma che hai in mano: il danno sale di 1,7 volte, caricatore e riserva di una
    /// volta e mezza, i metalli si tingono d'oro e il nome nell'HUD diventa dorato. Ogni arma si consacra una volta sola.</summary>
    public sealed class Altare : MonoBehaviour, IInteract
    {
        public const int Price = 3000;
        public static Altare Current;
        public int uses;
        Light glow;
        float flare;

        public Vector3 Point => transform.position + Vector3.up * 0.95f;
        public float Radius => 2.7f;

        public string Prompt
        {
            get
            {
                var gm = GameManager.I;
                if (gm == null || gm.player == null) return null;
                var w = gm.player.weapon;
                if (w.Upgraded) return "L'ARMA IN MANO È GIÀ CONSACRATA";
                if (!w.CanConsecrate) return null;
                int have = gm.score;
                string cost = have >= Price ? $"{Price} punti" : $"{Price} punti (ti mancano {Price - have})";
                return $"CONSACRAZIONE — {w.Def.name}: danno x{Weapon.UpgDamage:0.#}, caricatore x{Weapon.UpgMag:0.#} — {cost}";
            }
        }

        public void Use()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            var w = gm.player.weapon;
            if (!w.CanConsecrate) return;
            if (gm.score < Price) { gm.hud?.Toast($"Servono {Price} punti."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
            string name = w.Def.name;
            if (!w.Consecrate()) return;
            gm.AddPoints(-Price);
            uses++;
            flare = 1f;
            Sfx.UI(SoundBank.Consecrate, 0.9f);
            Sfx.At(SoundBank.Consecrate, Point, 0.7f, 1f, 4f, 40f);
            gm.hud?.Banner("Consacrata", $"{name}: più danno, caricatore e riserva x{Weapon.UpgMag:0.#}.");
            gm.hud?.Flash(new Color(1f, 0.82f, 0.36f), 1.1f, 0.55f);
        }

        void Update()
        {
            if (glow == null) return;
            flare = Mathf.MoveTowards(flare, 0f, Time.deltaTime / 2.2f);
            ShrineKit.SetGlow(glow, Mathf.Lerp(1.5f, 5f, flare));      // la luce ha il respiro (LightPulse): si cambia la base, non l'intensita'
        }

        void OnDestroy() { Interact.Remove(this); if (Current == this) Current = null; }

        public static Altare Place(FloorInstance cur, Plan plan, Room room, Rng rng)
        {
            ShrineKit.Mats();
            bool ax = room.W >= room.D;
            Vector3 c = default;
            bool found = false;
            // l'altare e' lungo 1,5 m e largo 0,9: prima nel verso della stanza, poi di traverso
            for (int pass = 0; pass < 2 && !found; pass++)
            {
                bool tryX = pass == 0 ? ax : !ax;
                found = ShrineKit.FindSpot(plan, room, rng, tryX ? new Vector2(0.85f, 0.55f) : new Vector2(0.55f, 0.85f), 1.0f, null, 0f, out c);
                if (found) ax = tryX;
            }
            if (!found) return null;
            var go = new GameObject("Altare");
            go.transform.SetParent(cur.root.transform, false);
            go.transform.position = c;
            // la lunghezza segue l'asse scelto, la fronte guarda il centro della stanza
            Vector3 to = new Vector3(room.CenterX - c.x, 0f, room.CenterZ - c.z);
            float front = ax ? (to.z >= 0f ? 0f : 180f) : (to.x >= 0f ? 90f : 270f);
            go.transform.rotation = Quaternion.Euler(0f, front, 0f);
            cur.propRoots.Add(go.transform);
            var a = go.AddComponent<Altare>();
            a.Build();
            Interact.Add(a);
            Current = a;
            return a;
        }

        void Build()
        {
            var stone = ShrineKit.StoneDark; var iron = ShrineKit.Iron;
            var gold = ShrineKit.Emit(new Color(1f, 0.74f, 0.26f), 2.6f);
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Base", stone, new Vector3(0f, 0.09f, 0f), new Vector3(1.5f, 0.18f, 0.9f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Corpo", stone, new Vector3(0f, 0.49f, 0f), new Vector3(1.26f, 0.62f, 0.68f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Piano", stone, new Vector3(0f, 0.86f, 0f), new Vector3(1.5f, 0.12f, 0.9f));
            // croce d'oro inlaiata nel piano, con un anello attorno all'incrocio
            ShrineKit.Prim(transform, PrimitiveType.Cube, "CroceLunga", gold, new Vector3(0f, 0.9225f, 0f), new Vector3(0.06f, 0.006f, 0.66f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "CroceBraccio", gold, new Vector3(0f, 0.9225f, 0.12f), new Vector3(0.40f, 0.006f, 0.06f));
            ShrineKit.LatheObj(transform, "Anello", gold, ShrineKit.Lathe("santAnello", new[] { 0.31f, 0.9235f, 0.27f, 0.9235f }, 32)).transform.localPosition = new Vector3(0f, 0f, 0.12f);
            // l'occhio sul fronte: un disco d'oro con la pupilla scura
            ShrineKit.Prim(transform, PrimitiveType.Cylinder, "Occhio", gold, new Vector3(0f, 0.50f, 0.343f), new Vector3(0.24f, 0.005f, 0.24f), new Vector3(90f, 0f, 0f));
            ShrineKit.Prim(transform, PrimitiveType.Cylinder, "Pupilla", ShrineKit.Dark, new Vector3(0f, 0.50f, 0.347f), new Vector3(0.08f, 0.004f, 0.08f), new Vector3(90f, 0f, 0f));
            // due candele sul piano
            foreach (float x in new[] { -0.58f, 0.58f })
            {
                ShrineKit.Prim(transform, PrimitiveType.Cylinder, "Candela", ShrineKit.Wax, new Vector3(x, 1.055f, -0.28f), new Vector3(0.075f, 0.135f, 0.075f));
                ShrineKit.Prim(transform, PrimitiveType.Sphere, "Fiamma", ShrineKit.Emit(new Color(1f, 0.62f, 0.14f), 3.4f), new Vector3(x, 1.215f, -0.28f), new Vector3(0.04f, 0.075f, 0.04f));
                ShrineKit.PointLight(transform, "LuceCandela", new Vector3(x, 1.55f, -0.28f), new Color(1f, 0.62f, 0.26f), 0.6f, 5.2f, 0.3f, 7f);
            }
            // il martello, appoggiato di lato
            ShrineKit.Prim(transform, PrimitiveType.Cylinder, "Manico", ShrineKit.Wood, new Vector3(0.3f, 0.945f, -0.24f), new Vector3(0.024f, 0.15f, 0.024f), new Vector3(0f, 0f, 90f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Testa", iron, new Vector3(0.12f, 0.96f, -0.24f), new Vector3(0.075f, 0.07f, 0.075f));
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.46f, 0f);
            col.size = new Vector3(1.5f, 0.92f, 0.9f);
            // alta sul piano: a 60 cm dalla pietra la bruciava fino al bianco e la croce d'oro si perdeva
            glow = ShrineKit.PointLight(transform, "Luce", new Vector3(0f, 2.1f, 0.1f), new Color(1f, 0.82f, 0.4f), 1.5f, 7.5f, 0.12f, 1.8f);
        }
    }
}
