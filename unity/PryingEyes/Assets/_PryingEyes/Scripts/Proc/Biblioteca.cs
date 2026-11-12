using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>La Biblioteca di un piano: tre leggii di pietra in fila, su ognuno un libro magico che galleggia e gira col sigillo acceso
    /// del colore del suo perk. Si paga col prezzo progressivo (Perks.Prices: 2000, 2500, 3000, 4000 secondo quanti ne hai gia'); preso
    /// un libro gli altri due si richiudono e si posano sul leggio. Ogni piano ne ha una, con tre perk a caso tra quelli che non hai
    /// (se hai gia' tutti i quattro posti pieni non c'e'). Sta in una stanza grande lontana dai candelabri.</summary>
    public sealed class Biblioteca
    {
        public static Biblioteca Current;
        public readonly List<BookStand> stands = new List<BookStand>();
        public Room room;
        public GameObject root;
        public bool chosen;

        public static void ResetRun() { Current = null; }
        public static void Clear() { Current = null; }

        /// <summary>Tre perk che non hai ancora, a caso (meno se ne restano meno).</summary>
        public static List<PerkId> Offer(Rng rng)
        {
            var pool = new List<PerkId>();
            foreach (var d in Perks.All) if (!Perks.Has(d.id)) pool.Add(d.id);
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            if (pool.Count > 3) pool.RemoveRange(3, pool.Count - 3);
            return pool;
        }

        /// <summary>Costruisce la Biblioteca in 'room'. Nulla se non c'e' spazio per la fila di leggii o se non c'e' piu' niente da offrire.</summary>
        public static Biblioteca Place(FloorInstance cur, Plan plan, Room room, Rng rng)
        {
            ShrineKit.Mats();
            if (Perks.Full) return null;
            var offer = Offer(rng);
            if (offer.Count == 0) return null;
            int n = offer.Count;
            const float spacing = 1.75f;
            float half = spacing * (n - 1) * 0.5f + 0.5f;
            bool ax = room.W >= room.D, found = false;
            Vector3 c = default;
            for (int pass = 0; pass < 2 && !found; pass++)
            {
                bool tryX = pass == 0 ? ax : !ax;
                found = ShrineKit.FindSpot(plan, room, rng, tryX ? new Vector2(half, 0.62f) : new Vector2(0.62f, half), 1.0f, null, 0f, out c);
                if (found) ax = tryX;
            }
            if (!found) return null;
            var root = new GameObject("Biblioteca");
            var lib = new Biblioteca { room = room, root = root };
            root.transform.SetParent(cur.root.transform, false);
            root.transform.position = c;
            cur.propRoots.Add(root.transform);
            for (int i = 0; i < n; i++)
            {
                float off = (i - (n - 1) * 0.5f) * spacing;
                var go = new GameObject("Leggio" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = ax ? new Vector3(off, 0f, 0f) : new Vector3(0f, 0f, off);
                var st = go.AddComponent<BookStand>();
                st.Init(lib, offer[i]);
                lib.stands.Add(st);
            }
            Current = lib;
            return lib;
        }

        /// <summary>Un libro e' stato scelto: gli altri si richiudono, quello letto sale e svanisce.</summary>
        public void OnBought(BookStand taken)
        {
            chosen = true;
            foreach (var s in stands)
            {
                if (s == taken) s.Take();
                else s.Seal();
            }
        }
    }

    /// <summary>Un leggio con il suo libro. Premere F davanti paga e legge il libro (vedi Biblioteca).</summary>
    public sealed class BookStand : MonoBehaviour, IInteract
    {
        public PerkId perk;
        public bool Sealed, Taken;
        Biblioteca lib;
        Transform book;
        Light glow;
        float phase, spin, gone, settle;
        const float Hover = 1.32f, Lying = 0.965f;

        static readonly float[] Plinth =
        {
            0.000f, 0.000f, 0.300f, 0.000f, 0.300f, 0.070f, 0.250f, 0.090f, 0.250f, 0.130f, 0.190f, 0.160f, 0.165f, 0.220f, 0.150f, 0.400f,
            0.150f, 0.640f, 0.175f, 0.720f, 0.230f, 0.770f, 0.235f, 0.800f, 0.320f, 0.835f, 0.330f, 0.855f, 0.330f, 0.905f, 0.300f, 0.918f, 0.000f, 0.918f,
        };
        static readonly float[] RuneRing = { 0.262f, 0.9195f, 0.208f, 0.9195f };

        public Vector3 Point => transform.position + Vector3.up * 1.2f;
        public float Radius => 2.6f;
        public Perks.Def Def => Perks.Of(perk);

        public string Prompt
        {
            get
            {
                if (Taken) return null;
                if (Sealed) return "IL LIBRO È SIGILLATO — ne hai già scelto un altro";
                var d = Def;
                if (Perks.Full) return $"{d.book.ToUpperInvariant()} — i quattro posti sono pieni";
                int price = Perks.NextPrice, have = GameManager.I != null ? GameManager.I.score : 0;
                string cost = have >= price ? $"{price} punti" : $"{price} punti (ti mancano {price - have})";
                return $"{d.book.ToUpperInvariant()}: {d.name.ToUpperInvariant()} — {d.effect} — {cost}";
            }
        }

        public void Use()
        {
            var gm = GameManager.I;
            if (gm == null || Sealed || Taken) return;
            if (Perks.Full) { gm.hud?.Toast("Hai già quattro libri: le mani sono piene."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
            int price = Perks.NextPrice;
            if (gm.score < price) { gm.hud?.Toast($"Servono {price} punti."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
            if (Perks.Has(perk)) return;
            gm.AddPoints(-price);
            Perks.Add(perk);
            var d = Def;
            Sfx.UI(SoundBank.BookOpen, 0.85f);
            gm.hud?.Banner(d.name, d.effect);
            gm.hud?.Flash(d.color, 0.9f, 0.4f);
            if (lib != null) lib.OnBought(this);
        }

        public void Init(Biblioteca l, PerkId p)
        {
            lib = l; perk = p;
            phase = Random.value * 6.28f;
            var d = Def;
            ShrineKit.Mats();
            ShrineKit.LatheObj(transform, "Piedistallo", ShrineKit.Stone, ShrineKit.Lathe("santLeggio", Plinth, 28));
            ShrineKit.LatheObj(transform, "Rune", ShrineKit.Emit(d.color, 1.7f), ShrineKit.Lathe("santLeggioRune", RuneRing, 28));
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.46f, 0f);
            col.size = new Vector3(0.66f, 0.92f, 0.66f);
            book = BuildBook(transform, d.color);
            book.localPosition = new Vector3(0f, Hover, 0f);
            glow = ShrineKit.PointLight(transform, "Luce", new Vector3(0f, 1.4f, 0f), d.color, 1.5f, 6.5f, 0.22f, 2.2f);
            Interact.Add(this);
        }

        void OnDestroy() { Interact.Remove(this); }

        /// <summary>Si richiude e si posa piatto sul leggio: la luce si spegne.</summary>
        public void Seal()
        {
            if (Sealed || Taken) return;
            Sealed = true;
            Sfx.At(SoundBank.BookSeal, Point, 0.9f, Random.Range(0.92f, 1.08f), 2f, 30f);
        }

        /// <summary>E' il libro letto: sale e si dissolve in un lampo del suo colore.</summary>
        public void Take() { Taken = true; }

        void Update()
        {
            float dt = Time.deltaTime;
            if (Taken)
            {
                gone += dt;
                float k = Mathf.Clamp01(gone / 0.9f);
                book.localPosition = new Vector3(0f, Hover + k * 0.9f, 0f);
                book.localScale = Vector3.one * (1f - k * k);
                spin += 420f * dt;
                book.localRotation = Quaternion.Euler(20f, spin, 0f);
                if (glow != null) glow.intensity = Mathf.Lerp(3.2f, 0f, k);
                if (k >= 1f) { book.gameObject.SetActive(false); if (glow != null) glow.enabled = false; enabled = false; }
                return;
            }
            if (Sealed)
            {
                settle = Mathf.Min(1f, settle + dt / 0.7f);
                float k = settle * settle * (3f - 2f * settle);
                var p = book.localPosition;
                book.localPosition = new Vector3(0f, Mathf.Lerp(p.y, Lying, k), 0f);
                book.localRotation = Quaternion.Slerp(book.localRotation, Quaternion.Euler(0f, spin, 0f), k);
                if (glow != null) glow.intensity = Mathf.Lerp(glow.intensity, 0f, k);
                return;
            }
            spin += 62f * dt;
            float t = Time.time + phase;
            book.localPosition = new Vector3(0f, Hover + 0.055f * Mathf.Sin(t * 1.7f), 0f);
            book.localRotation = Quaternion.Euler(20f + 4f * Mathf.Sin(t * 1.1f), spin, 0f);
        }

        // ------------------------------------------------------------------ il libro
        /// <summary>Un libro chiuso di 20 x 28 cm, cuoio scuro con gli angoli e la fascia d'ottone, il taglio in carta, un sigillo che brilla.
        /// Sta coricato (la copertina sopra, il dorso a -X) e il chiamante lo gira.</summary>
        public static Transform BuildBook(Transform parent, Color glowColor)
        {
            var b = new GameObject("Libro").transform;
            b.SetParent(parent, false);
            var lea = ShrineKit.Leather; var brass = ShrineKit.Brass;
            ShrineKit.Prim(b, PrimitiveType.Cube, "CopertinaSotto", lea, new Vector3(0f, -0.031f, 0f), new Vector3(0.205f, 0.012f, 0.285f));
            ShrineKit.Prim(b, PrimitiveType.Cube, "CopertinaSopra", lea, new Vector3(0f, 0.031f, 0f), new Vector3(0.205f, 0.012f, 0.285f));
            ShrineKit.Prim(b, PrimitiveType.Cube, "Dorso", lea, new Vector3(-0.1f, 0f, 0f), new Vector3(0.016f, 0.074f, 0.285f));
            ShrineKit.Prim(b, PrimitiveType.Cube, "Pagine", ShrineKit.Paper, new Vector3(0.004f, 0f, 0f), new Vector3(0.19f, 0.052f, 0.27f));
            foreach (float sx in new[] { -0.093f, 0.093f })
                foreach (float sz in new[] { -0.131f, 0.131f })
                    ShrineKit.Prim(b, PrimitiveType.Cube, "Angolo", brass, new Vector3(sx, 0.0375f, sz), new Vector3(0.038f, 0.004f, 0.038f));
            ShrineKit.Prim(b, PrimitiveType.Cube, "Fascia", brass, new Vector3(0.099f, 0f, 0f), new Vector3(0.012f, 0.078f, 0.07f));
            ShrineKit.Prim(b, PrimitiveType.Cylinder, "Cornice", brass, new Vector3(0f, 0.0368f, 0f), new Vector3(0.125f, 0.0012f, 0.125f));
            ShrineKit.Prim(b, PrimitiveType.Cylinder, "Sigillo", ShrineKit.Emit(glowColor, 2.8f), new Vector3(0f, 0.0382f, 0f), new Vector3(0.097f, 0.0012f, 0.097f));
            ShrineKit.Prim(b, PrimitiveType.Cylinder, "Nucleo", ShrineKit.Dark, new Vector3(0f, 0.0392f, 0f), new Vector3(0.036f, 0.0012f, 0.036f));
            return b;
        }
    }
}
