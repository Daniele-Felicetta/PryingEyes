using System.Collections.Generic;
using UnityEngine;
using K = PryingEyes.OffHand.Key;
using Sp = PryingEyes.OffHand.Space;
using Pt = PryingEyes.OffHand.Part;
using Ct = PryingEyes.OffHand.Contact;

namespace PryingEyes
{
    /// <summary>Le ricariche fatte dalla mano sinistra (2026-11-16, presa realistica). Ogni stile di ricarica ha la sua coreografia,
    /// scritta come i gesti delle magie (OffHand: chiavi della mano nello spazio della camera o dell'arma, sulle sue parti, con la
    /// posa delle dita) ma legata al tempo della ricarica (WeaponState.ReloadProgress: OffHand.PlayExternal e Seek), cosi' i suoni e
    /// gli eventi dello stato cadono sui gesti giusti anche con i perk che la accorciano:
    ///  - caricatore: la sinistra lascia l'astina, afferra il caricatore di lato (palmo sul fianco, dita attorno al davanti), lo sfila
    ///    lungo il suo asse; a vuoto lo lascia cadere, se no lo porta alla cintura; torna dal basso con quello nuovo (il caricatore
    ///    viaggia nella mano: Weapon lo attacca al palmo), lo allinea sotto il pozzetto, lo infila, ci da' un colpo col palmo da sotto
    ///    e, se l'arma era vuota, tira la leva d'armamento (o batte il fermo dell'otturatore) prima di tornare sull'astina;
    ///  - pistola: con le pistole a una mano la sinistra sale dal basso; a vuoto il caricatore cade da solo, se no la mano lo
    ///    prende; il nuovo entra dal basso con un colpo di palmo e a vuoto la mano arretra il carrello da sopra (presa a pugno);
    ///  - a canna (pompa, leva): l'arma gira la finestra verso la mano; una cartuccia alla volta dalla cintura, tenuta tra pollice e
    ///    dita, spinta dentro col pollice;
    ///  - doppietta: le canne si aprono, la mano toglie i bossoli, porta due cartucce nelle camere e richiude le canne da sotto;
    ///  - tamburo (revolver): le dita spingono fuori il tamburo, l'arma punta in alto e il pollice batte l'estrattore, il caricatore
    ///    rapido entra dal basso, poi le dita richiudono il tamburo;
    ///  - nastro: la mano alza il coperchio, cambia la cassetta come un caricatore, posa il nastro e chiude il coperchio con un colpo.
    /// Le parti che si muovono (leva d'armamento, carrello, canne, coperchio, tamburo) le muove Weapon.AnimateParts coi tempi di qui;
    /// la mano sta sui loro punti (OffHand.Part), quindi le segue.</summary>
    public static class ReloadAnims
    {
        // ------------------------------------------------------------------ tempi (frazioni della ricarica a caricatore)
        // lo stato fa suonare l'inserimento a 0,50 e la chiusura dell'otturatore a 0,76 (WeaponState.AdvanceReload)
        public const float MagTake = 0.17f, MagDrop = 0.27f, MagFetch = 0.37f, MagInsert = 0.45f, MagSeat = 0.50f;
        /// <summary>Leva d'armamento: presa, tirata fino in fondo, lasciata (torna avanti da sola e chiude l'otturatore).</summary>
        public const float ChargeHook = 0.66f, ChargeBack = 0.73f, ChargeGo = 0.76f;
        /// <summary>Pistola a vuoto: il pollice sinistro preme il fermo del carrello, che torna avanti a SlideGo.</summary>
        public const float SlideGrab = 0.70f, SlideBack = 0.70f, SlideGo = 0.735f;
        /// <summary>Canne della doppietta e tamburo del revolver: aperti e richiusi.</summary>
        public const float BreakOpen0 = 0.06f, BreakOpen1 = 0.18f, BreakClose0 = 0.70f, BreakClose1 = 0.78f;
        public const float CylOpen0 = 0.07f, CylOpen1 = 0.17f, CylClose0 = 0.72f, CylClose1 = 0.80f;
        /// <summary>Coperchio della mitragliatrice a nastro: alzato dalla mano, chiuso con un colpo.</summary>
        public const float CoverOpen0 = 0.08f, CoverOpen1 = 0.15f, CoverClose0 = 0.78f, CoverClose1 = 0.84f;
        /// <summary>La cassetta del nastro: presa, buttata, presa la nuova, infilata (dopo il coperchio).</summary>
        const float BeltTake = 0.23f, BeltDrop = 0.31f, BeltFetch = 0.41f, BeltInsert = 0.46f;

        // ------------------------------------------------------------------ mattoni
        static float D;
        static float T(float u) => u * D;

        static K Cam(float u, Vector3 pos, Vector3 fingers, Vector3 dorsal, HandPose pose, Ct c = Ct.Palm, int ease = 0) =>
            new K { t = T(u), space = Sp.Camera, pos = pos, fingers = fingers, dorsal = dorsal, pose = pose, contact = c, ease = ease, free = 1f };

        static K On(float u, Pt part, Vector3 off, Vector3 fingers, Vector3 dorsal, HandPose pose, Ct c = Ct.Palm, int ease = 0, float barrel = 0f) =>
            new K { t = T(u), space = Sp.Weapon, part = part, pos = off, fingers = fingers, dorsal = dorsal, pose = pose, contact = c, ease = ease, free = 1f, barrel = barrel };

        static K GripAt(float u, int ease = 0) => new K { t = T(u), space = Sp.Grip, gripPose = true, ease = ease };
        static K GripOff(float u, HandPose pose, int ease = 0) => new K { t = T(u), space = Sp.Grip, pos = new Vector3(-0.02f, -0.055f, -0.03f), pose = pose, ease = ease, free = 0.5f };
        static K Below(float u, HandPose pose, int ease = 0) => new K { t = T(u), space = Sp.Below, pose = pose, ease = ease, free = 1f };

        static OffHand.WKey W(float u, Vector3 pos, Vector3 rot, int ease = 0) => new OffHand.WKey { t = T(u), pos = pos, rot = rot, ease = ease };
        static OffHand.Ev E(float u, string id) => new OffHand.Ev { t = T(u), id = id };

        /// <summary>Dove la ricarica porta il punto su cui lavora la mano (spazio della camera): davanti al petto, poco a sinistra del
        /// centro, nel terzo basso dello schermo; a 45 cm dalla spalla sinistra.</summary>
        static readonly Vector3 MagWork = new Vector3(-0.01f, -0.185f, 0.37f), TopWork = new Vector3(-0.02f, -0.13f, 0.36f);
        static readonly Vector3 PistolWork = new Vector3(-0.03f, -0.225f, 0.41f), SlideWork = new Vector3(-0.02f, -0.15f, 0.41f);
        // la culatta della doppietta piu' in basso e piu' avanti (0.7.1): a 16 cm sotto l'occhio la mano che infila le cartucce dall'alto
        // portava l'avambraccio sinistro in mezzo allo schermo
        static readonly Vector3 PortWork = new Vector3(-0.02f, -0.20f, 0.37f), BreechWork = new Vector3(-0.01f, -0.235f, 0.40f);
        // il coperchio della mitragliatrice piu' in basso (0.7.1): a 12 cm sotto l'occhio la mano che lo alza portava l'avambraccio
        // sinistro di traverso sulla parte alta dello schermo
        static readonly Vector3 CylWork = new Vector3(-0.03f, -0.16f, 0.32f), CoverWork = new Vector3(0.02f, -0.20f, 0.52f);

        /// <summary>La cintura (le tasche dei caricatori), appena fuori dall'inquadratura in basso a sinistra (spazio della camera).</summary>
        static readonly Vector3 Pouch = new Vector3(-0.15f, -0.40f, 0.22f);
        static readonly Vector3 PouchF = new Vector3(0.15f, 0.85f, 0.45f), PouchD = new Vector3(-0.85f, 0.1f, -0.5f);

        static HandPose MagHold => HandPose.Lerp(HandPoses.Grip, HandPoses.Fist, 0.18f);
        static HandPose Hook => HandPose.Fingers(28f, 82f, 0.25f, 0.55f).Thumb(10f, 20f, 14f, 10f);
        static HandPose ShellHold => new HandPose()
            .Finger(0, 3f, 30f, 38f, 18f).Finger(1, 0f, 34f, 46f, 26f).Finger(2, -5f, 58f, 80f, 48f).Finger(3, -11f, 64f, 84f, 50f)
            .Thumb(22f, 36f, 14f, 8f);
        static HandPose LoaderHold => HandPose.Fingers(42f, 52f, 0.6f, 0.55f).Thumb(16f, 30f, 18f, 14f);

        static OffHand.Anim Make(string name, List<K> keys, List<OffHand.WKey> w, List<OffHand.Ev> ev)
        {
            keys.Sort((a, b) => a.t.CompareTo(b.t));
            w.Sort((a, b) => a.t.CompareTo(b.t));
            ev.Sort((a, b) => a.t.CompareTo(b.t));
            return new OffHand.Anim { name = name, duration = D, keys = keys.ToArray(), weapon = w.ToArray(), events = ev.ToArray() };
        }

        static int IndexOfKey(List<K> keys, float t)
        {
            for (int i = 0; i < keys.Count; i++) if (Mathf.Abs(keys[i].t - t) < 1e-4f) return i;
            return -1;
        }

        // ------------------------------------------------------------------ ingresso
        /// <summary>La ricarica dell'arma d (presa realistica) per la mano libera oh. duration: secondi di tutta la ricarica (per le
        /// ricariche a colpo singolo: apertura, colpi e chiusura come in WeaponState); rounds: quanti colpi entrano.</summary>
        public static OffHand.Anim Build(WeaponDef d, OffHand oh, WeaponRig rig, bool empty, float duration, int rounds, float opening, float interval, float insertFrac)
        {
            if (d == null || oh == null || rig == null || duration <= 0.05f) return null;
            D = duration;
            bool one = oh.OneHanded;
            switch (d.style)
            {
                case ReloadStyle.Mag: return rig.mag != null ? Magazine(d, oh, rig, empty, one) : null;
                case ReloadStyle.Belt: return rig.mag != null ? Belt(d, oh, rig, empty, one) : null;
                case ReloadStyle.Pistol: return rig.mag != null ? Pistol(d, oh, rig, empty, one) : null;
                case ReloadStyle.Tube: return Tube(d, oh, rig, empty, one, rounds, opening, interval, insertFrac);
                case ReloadStyle.Break: return rig.barrels != null ? Break(d, oh, rig, empty) : null;
                case ReloadStyle.Revolver: return rig.cyl != null ? Revolver(d, oh, rig, empty) : null;
            }
            return null;
        }

        // ------------------------------------------------------------------ caricatore (fucili, mitra)
        // la sinistra afferra il caricatore da davanti: palmo sul davanti del caricatore, dita che girano sul fianco destro, pollice
        // sul fianco sinistro verso il pozzetto (spazio dell'arma: x destra, y su, z verso la bocca)
        static readonly Vector3 MagF = new Vector3(1f, -0.25f, 0.12f), MagD = new Vector3(0.12f, 0.05f, 1f);
        static readonly Vector3 TopF = new Vector3(0.25f, -0.05f, 0.97f), TopD = new Vector3(0f, 1f, 0.1f);
        // il colpo di palmo sotto il fondello, e il caricatore della pistola tenuto per il fondello: la mano come sull'astina (dita avanti
        // a destra, dorso in basso a sinistra), che il polso fa senza girarsi oltre i suoi gradi
        static readonly Vector3 SlapF = new Vector3(0.62f, 0.18f, 0.76f), SlapD = new Vector3(-0.55f, -0.83f, 0f);

        static OffHand.Anim Magazine(WeaponDef d, OffHand oh, WeaponRig rig, bool empty, bool one)
        {
            bool up = d.magUp;
            Vector3 axis = oh.MagAxis;                      // verso cui il caricatore si sfila (spazio dell'arma)
            Vector3 f = up ? TopF : MagF, dor = up ? TopD : MagD;
            var k = new List<K>();
            var ev = new List<OffHand.Ev>();
            if (one) k.Add(Below(0f, HandPoses.Relaxed));
            else { k.Add(GripAt(0f)); k.Add(GripOff(0.07f, HandPoses.Relaxed, 2)); }
            // al caricatore: prima accanto, aperta; poi la presa
            k.Add(On(0.13f, Pt.MagGrip, (up ? Vector3.up * 0.03f : new Vector3(-0.015f, -0.01f, 0.03f)), f, dor, HandPoses.Open, Ct.Palm, 2));
            var seatK = On(MagTake, Pt.MagGrip, Vector3.zero, f, dor, MagHold, Ct.Palm, 2);
            k.Add(seatK);
            ev.Add(E(MagTake, "take"));
            // fuori lungo il suo asse, poi via
            k.Add(On(0.22f, Pt.MagGrip, axis * 0.035f, f, dor, MagHold, Ct.Palm, 1));
            k.Add(On(MagDrop, Pt.MagGrip, axis * 0.10f + Vector3.left * 0.02f, f, dor, empty ? HandPoses.Open : MagHold, Ct.Palm, 3));
            if (empty) ev.Add(E(MagDrop, "drop"));
            k.Add(Cam(0.34f, Pouch, PouchF, PouchD, empty ? HandPoses.Relaxed : MagHold, Ct.Palm, 2));
            k.Add(Cam(MagFetch, Pouch + new Vector3(0f, -0.02f, 0f), PouchF, PouchD, MagHold, Ct.Palm, 0));
            ev.Add(E(MagFetch, "fetch"));
            // il nuovo: allineato sotto il pozzetto, poi dentro
            k.Add(On(MagInsert, Pt.MagGrip, axis * 0.055f + Vector3.forward * 0.006f, f, dor, MagHold, Ct.Palm, 2));
            var seat = On(MagSeat, Pt.MagGrip, Vector3.zero, f, dor, MagHold, Ct.Palm, 1);
            k.Add(seat);
            ev.Add(E(MagSeat, "seat"));
            // il colpo di palmo (sopra per il caricatore in alto)
            if (!up)
            {
                k.Add(On(0.535f, Pt.MagBase, new Vector3(0f, -0.035f, 0f), SlapF, SlapD, HandPoses.Flat, Ct.Palm, 2));
                k.Add(On(0.56f, Pt.MagBase, new Vector3(0f, -0.004f, 0f), SlapF, SlapD, HandPoses.Flat, Ct.Palm, 1));
                ev.Add(E(0.56f, "slap"));
                k.Add(On(0.60f, Pt.MagBase, new Vector3(-0.015f, -0.035f, 0f), SlapF, SlapD, HandPoses.Relaxed, Ct.Palm, 2));
            }
            else k.Add(On(0.58f, Pt.MagGrip, new Vector3(-0.01f, 0.03f, 0f), f, dor, HandPoses.Relaxed, Ct.Palm, 2));
            Vector3 cp = oh.PartPoint(Pt.Charge);
            bool chargeOk = rig.charge != null && (cp.z > oh.Surface.Receiver.z + 0.03f || Mathf.Abs(cp.x) > 0.022f);
            if (empty && chargeOk)
            {
                // la leva d'armamento dietro, sopra il castello: indice e medio la agganciano da davanti, la tirano fino in fondo e la lasciano
                Vector3 cf = new Vector3(0.80f, -0.15f, 0.58f), cd = new Vector3(0f, 1f, 0.25f);
                k.Add(On(0.62f, Pt.Charge, new Vector3(-0.03f, 0.035f, 0.035f), cf, cd, HandPoses.Relaxed, Ct.Index, 2));
                k.Add(On(ChargeHook, Pt.Charge, new Vector3(-0.004f, 0.006f, 0.010f), cf, cd, Hook, Ct.Index, 2));
                k.Add(On(ChargeBack, Pt.Charge, new Vector3(-0.004f, 0.006f, 0.010f), cf, cd, Hook, Ct.Index, 3));
                k.Add(On(ChargeGo, Pt.Charge, new Vector3(-0.025f, 0.03f, -0.015f), cf, cd, HandPoses.Open, Ct.Index, 1));
                ev.Add(E(ChargeHook, "charge"));
            }
            else if (empty && rig.boltHandle == null)
            {
                // niente leva a portata: il palmo batte il fermo dell'otturatore sul fianco sinistro del castello (l'otturatore girevole
                // lo manovra la destra a ricarica finita)
                // il fermo sta sul fianco sinistro del castello inferiore, sopra il pozzetto: il palmo ci batte di piatto
                Vector3 bf = new Vector3(0.05f, 0.25f, 0.97f), bd = Vector3.left;
                Vector3 at = new Vector3(0f, 0.05f, -0.022f);
                k.Add(On(0.66f, Pt.MagGrip, at + new Vector3(-0.035f, 0f, 0f), bf, bd, HandPoses.Flat, Ct.Palm, 2));
                k.Add(On(0.70f, Pt.MagGrip, at + new Vector3(-0.002f, 0f, 0f), bf, bd, HandPoses.Flat, Ct.Palm, 1));
                ev.Add(E(0.70f, "slap"));
                k.Add(On(0.75f, Pt.MagGrip, at + new Vector3(-0.05f, -0.03f, -0.01f), bf, bd, HandPoses.Relaxed, Ct.Palm, 2));
            }
            if (one) k.Add(Below(0.95f, HandPoses.Relaxed, 2));
            else { k.Add(GripOff(0.86f, HandPoses.Relaxed, 2)); k.Add(GripAt(0.95f, 2)); k.Add(GripAt(1f)); }
            if (one) k.Add(Below(1f, HandPoses.Relaxed));
            // l'arma: ruota il pozzetto verso la mano (dorso a destra), sale un poco e si avvicina; sussulta al colpo di palmo; a vuoto
            // gira il fianco per la leva
            var w = new List<OffHand.WKey>();
            Vector3 r1 = up ? new Vector3(-8f, 10f, -8f) : new Vector3(-10f, 14f, -28f);
            Vector3 p1 = oh.OffsetToBring(oh.PartPoint(Pt.MagGrip), r1, up ? TopWork : MagWork);
            Vector3 r2 = r1 + new Vector3(4f, 10f, 4f);
            Vector3 p2 = chargeOk ? oh.OffsetToBring(cp, r2, new Vector3(0.0f, -0.17f, 0.38f)) : p1;
            if (!chargeOk) r2 = r1;
            w.Add(W(0f, Vector3.zero, Vector3.zero));
            w.Add(W(0.12f, p1, r1, 2));
            w.Add(W(0.53f, p1, r1));
            w.Add(W(0.565f, p1 + new Vector3(0f, 0.008f, 0f), r1 + new Vector3(-3f, 0f, 0f), 1));
            w.Add(W(0.60f, p1, r1, 2));
            if (empty && chargeOk) { w.Add(W(0.66f, p2, r2, 2)); w.Add(W(0.77f, p2, r2)); w.Add(W(0.79f, p2 + new Vector3(0f, 0f, 0.006f), r2, 1)); }
            else if (empty && rig.boltHandle == null) { w.Add(W(0.70f, p1, r1)); w.Add(W(0.72f, p1 + new Vector3(0.006f, 0f, 0f), r1 + new Vector3(0f, 0f, 2f), 1)); w.Add(W(0.76f, p1, r1, 2)); }
            w.Add(W(0.92f, Vector3.zero, Vector3.zero, 2));
            var a = Make(empty ? "Ricarica a vuoto" : "Ricarica", k, w, ev);
            a.take = T(MagTake); a.drop = empty ? T(MagDrop) : -1f; a.fetch = T(MagFetch); a.insert = T(MagInsert); a.seat = T(MagSeat);
            a.seatKey = IndexOfKey(new List<K>(a.keys), T(MagSeat));
            a.magAxis = axis;
            return a;
        }

        /// <summary>Mitragliatrice a nastro: coperchio alzato dalla mano, cassetta cambiata come un caricatore, nastro posato sul
        /// vassoio, coperchio chiuso con un colpo di palmo; a vuoto la leva d'armamento come per i fucili.</summary>
        static OffHand.Anim Belt(WeaponDef d, OffHand oh, WeaponRig rig, bool empty, bool one)
        {
            if (rig.cover == null) return Magazine(d, oh, rig, empty, one);
            Vector3 axis = oh.MagAxis;
            var k = new List<K>();
            var ev = new List<OffHand.Ev>();
            k.Add(GripAt(0f));
            // il coperchio: le dita sotto il bordo di dietro, poi su
            Vector3 cf = new Vector3(0.75f, 0.05f, 0.66f), cd = new Vector3(0f, 1f, -0.3f);
            k.Add(On(0.045f, Pt.Cover, new Vector3(-0.03f, 0.03f, -0.02f), cf, cd, HandPoses.Relaxed, Ct.Index, 2));
            k.Add(On(CoverOpen0, Pt.Cover, new Vector3(-0.004f, -0.004f, -0.004f), cf, cd, Hook, Ct.Index, 2));
            k.Add(On(CoverOpen1, Pt.Cover, new Vector3(-0.004f, -0.004f, -0.004f), cf, cd, Hook, Ct.Index, 3));
            ev.Add(E(CoverOpen0, "cover"));
            // la cassetta, come un caricatore
            k.Add(On(0.19f, Pt.MagGrip, new Vector3(-0.015f, -0.01f, 0.03f), MagF, MagD, HandPoses.Open, Ct.Palm, 2));
            k.Add(On(BeltTake, Pt.MagGrip, Vector3.zero, MagF, MagD, MagHold, Ct.Palm, 2));
            ev.Add(E(BeltTake, "take"));
            k.Add(On(0.27f, Pt.MagGrip, axis * 0.035f, MagF, MagD, MagHold, Ct.Palm, 1));
            k.Add(On(BeltDrop, Pt.MagGrip, axis * 0.10f + Vector3.left * 0.02f, MagF, MagD, empty ? HandPoses.Open : MagHold, Ct.Palm, 3));
            if (empty) ev.Add(E(BeltDrop, "drop"));
            k.Add(Cam(0.37f, Pouch, PouchF, PouchD, empty ? HandPoses.Relaxed : MagHold, Ct.Palm, 2));
            k.Add(Cam(BeltFetch, Pouch + new Vector3(0f, -0.02f, 0f), PouchF, PouchD, MagHold, Ct.Palm, 0));
            ev.Add(E(BeltFetch, "fetch"));
            k.Add(On(BeltInsert, Pt.MagGrip, axis * 0.055f, MagF, MagD, MagHold, Ct.Palm, 2));
            k.Add(On(MagSeat, Pt.MagGrip, Vector3.zero, MagF, MagD, MagHold, Ct.Palm, 1));
            ev.Add(E(MagSeat, "seat"));
            // il nastro posato sul vassoio: pizzico da sinistra a destra sotto il coperchio alzato
            Vector3 bf = new Vector3(0.9f, -0.2f, 0.4f), bd = Vector3.up;
            k.Add(On(0.58f, Pt.Cover, new Vector3(-0.07f, -0.025f, 0.03f), bf, bd, HandPoses.Pinch, Ct.Index, 2));
            k.Add(On(0.66f, Pt.Cover, new Vector3(0.0f, -0.025f, 0.03f), bf, bd, HandPoses.Pinch, Ct.Index, 3));
            ev.Add(E(0.60f, "belt"));
            // il coperchio chiuso col palmo
            Vector3 sf = new Vector3(0.6f, -0.3f, 0.74f), sd = Vector3.up;
            k.Add(On(0.73f, Pt.Cover, new Vector3(0f, 0.07f, 0.03f), sf, sd, HandPoses.Flat, Ct.Palm, 2));
            k.Add(On(CoverClose1, Pt.Cover, new Vector3(0f, 0.012f, 0.03f), sf, sd, HandPoses.Flat, Ct.Palm, 1));
            ev.Add(E(CoverClose1, "slap"));
            k.Add(On(0.88f, Pt.Cover, new Vector3(-0.03f, 0.06f, 0.0f), sf, sd, HandPoses.Relaxed, Ct.Palm, 2));
            k.Add(GripOff(0.92f, HandPoses.Relaxed, 2));
            k.Add(GripAt(0.98f, 2));
            k.Add(GripAt(1f));
            var w = new List<OffHand.WKey>();
            Vector3 r1 = new Vector3(-6f, 8f, -14f);
            Vector3 p1 = oh.OffsetToBring(oh.PartPoint(Pt.Cover), r1, CoverWork);
            w.Add(W(0f, Vector3.zero, Vector3.zero));
            w.Add(W(0.10f, p1, r1, 2));
            w.Add(W(CoverClose0, p1, r1));
            w.Add(W(CoverClose1 + 0.01f, p1 + new Vector3(0f, -0.006f, 0f), r1 + new Vector3(2f, 0f, 0f), 1));
            w.Add(W(0.90f, p1, r1, 2));
            w.Add(W(0.98f, Vector3.zero, Vector3.zero, 2));
            var a = Make(empty ? "Ricarica a vuoto" : "Ricarica", k, w, ev);
            a.take = T(BeltTake); a.drop = empty ? T(BeltDrop) : -1f; a.fetch = T(BeltFetch); a.insert = T(BeltInsert); a.seat = T(MagSeat);
            a.seatKey = IndexOfKey(new List<K>(a.keys), T(MagSeat));
            a.magAxis = axis;
            return a;
        }

        // ------------------------------------------------------------------ pistola
        static OffHand.Anim Pistol(WeaponDef d, OffHand oh, WeaponRig rig, bool empty, bool one)
        {
            Vector3 axis = oh.MagAxis;
            // il caricatore della pistola sta nell'impugnatura: si tiene per il fondello col palmo sotto
            Vector3 f = SlapF, dor = SlapD;
            var k = new List<K>();
            var ev = new List<OffHand.Ev>();
            if (one) k.Add(Below(0f, HandPoses.Relaxed));
            else { k.Add(GripAt(0f)); k.Add(GripOff(0.06f, HandPoses.Relaxed, 2)); }
            if (empty)
            {
                // il pollice destro preme lo sgancio: il vuoto cade da solo; la mano va gia' alla cintura
                ev.Add(E(0.10f, "drop"));
                if (!one) k.Add(Cam(0.22f, Pouch + new Vector3(0.04f, 0.06f, 0.03f), PouchF, PouchD, HandPoses.Relaxed, Ct.Palm, 2));
            }
            else
            {
                // tattica: la mano raccoglie il caricatore che scende e lo porta via
                k.Add(On(0.11f, Pt.MagBase, axis * 0.05f + Vector3.left * 0.01f, f, dor, HandPoses.Open, Ct.Palm, 2));
                k.Add(On(MagTake - 0.02f, Pt.MagBase, Vector3.zero, f, dor, MagHold, Ct.Palm, 2));
                ev.Add(E(MagTake - 0.02f, "take"));
                k.Add(On(0.23f, Pt.MagBase, axis * 0.09f, f, dor, MagHold, Ct.Palm, 1));
            }
            k.Add(Cam(0.31f, Pouch, PouchF, PouchD, empty ? HandPoses.Relaxed : MagHold, Ct.Palm, 2));
            k.Add(Cam(MagFetch - 0.03f, Pouch + new Vector3(0f, -0.02f, 0f), PouchF, PouchD, MagHold, Ct.Palm, 0));
            ev.Add(E(MagFetch - 0.03f, "fetch"));
            k.Add(On(0.41f, Pt.MagBase, axis * 0.075f + Vector3.forward * 0.004f, f, dor, MagHold, Ct.Palm, 2));
            var seat = On(0.47f, Pt.MagBase, Vector3.zero, f, dor, MagHold, Ct.Palm, 1);
            k.Add(seat);
            ev.Add(E(0.47f, "seat"));
            // il colpo di palmo sul fondello
            k.Add(On(0.50f, Pt.MagBase, new Vector3(0f, -0.03f, 0f), SlapF, SlapD, HandPoses.Flat, Ct.Palm, 2));
            k.Add(On(0.525f, Pt.MagBase, new Vector3(0f, -0.004f, 0f), SlapF, SlapD, HandPoses.Flat, Ct.Palm, 1));
            ev.Add(E(0.525f, "slap"));
            if (empty && rig.slide != null)
            {
                // il fermo del carrello: sul fianco sinistro del telaio sopra il ponticello; la mano sale lungo l'impugnatura (come
                // sull'astina) e il pollice lo preme verso il basso: il carrello torna avanti da solo
                Vector3 stop = new Vector3(-0.012f, 0.072f, 0.032f);
                HandPose thumbUp = HandPose.Lerp(HandPoses.Grip, HandPoses.Relaxed, 0.4f).Thumb(-8f, 0f, 2f, 2f);
                HandPose thumbDown = HandPose.Lerp(HandPoses.Grip, HandPoses.Relaxed, 0.4f).Thumb(4f, 14f, 22f, 18f);
                k.Add(On(0.62f, Pt.MagBase, stop + new Vector3(-0.025f, -0.01f, -0.01f), SlapF, SlapD, thumbUp, Ct.Thumb, 2));
                k.Add(On(0.68f, Pt.MagBase, stop + new Vector3(-0.004f, 0.006f, 0f), SlapF, SlapD, thumbUp, Ct.Thumb, 2));
                k.Add(On(SlideGo - 0.005f, Pt.MagBase, stop + new Vector3(-0.004f, -0.004f, 0f), SlapF, SlapD, thumbDown, Ct.Thumb, 1));
                k.Add(On(0.80f, Pt.MagBase, stop + new Vector3(-0.03f, -0.03f, -0.02f), SlapF, SlapD, HandPoses.Relaxed, Ct.Thumb, 2));
            }
            if (one) { k.Add(Below(0.92f, HandPoses.Relaxed, 2)); k.Add(Below(1f, HandPoses.Relaxed)); }
            else { k.Add(GripOff(0.86f, HandPoses.Relaxed, 2)); k.Add(GripAt(0.95f, 2)); k.Add(GripAt(1f)); }
            var w = new List<OffHand.WKey>();
            Vector3 r1 = new Vector3(-14f, 10f, -24f), r2 = new Vector3(-6f, -18f, -6f);
            Vector3 p1 = oh.OffsetToBring(oh.PartPoint(Pt.MagBase), r1, PistolWork);
            Vector3 p2 = p1;
            r2 = r1;
            w.Add(W(0f, Vector3.zero, Vector3.zero));
            w.Add(W(0.10f, p1, r1, 2));
            w.Add(W(0.50f, p1, r1));
            w.Add(W(0.535f, p1 + new Vector3(0f, 0.01f, 0f), r1 + new Vector3(-4f, 0f, 0f), 1));
            w.Add(W(0.57f, p1, r1, 2));
            if (empty && rig.slide != null) { w.Add(W(0.63f, p2, r2, 2)); w.Add(W(0.745f, p2, r2)); w.Add(W(0.77f, p2 + new Vector3(0f, 0f, 0.008f), r2, 1)); }
            w.Add(W(0.90f, Vector3.zero, Vector3.zero, 2));
            var a = Make(empty ? "Ricarica a vuoto" : "Ricarica", k, w, ev);
            a.take = empty ? -1f : T(MagTake - 0.02f); a.drop = empty ? T(0.10f) : -1f; a.fetch = T(MagFetch - 0.03f); a.insert = T(0.41f); a.seat = T(0.47f);
            a.seatKey = IndexOfKey(new List<K>(a.keys), T(0.47f));
            a.magAxis = axis;
            return a;
        }

        static HandPose Tight(HandPose p, float k) => HandPose.Lerp(p, HandPoses.Fist, k);

        // ------------------------------------------------------------------ a canna (pompa, leva): una cartuccia alla volta
        static OffHand.Anim Tube(WeaponDef d, OffHand oh, WeaponRig rig, bool empty, bool one, int rounds, float opening, float interval, float insertFrac)
        {
            rounds = Mathf.Max(1, rounds);
            var k = new List<K>();
            var ev = new List<OffHand.Ev>();
            float u0 = opening / D, ui = interval / D;
            // la finestra di caricamento sotto il castello; la cartuccia lungo le dita, il pollice dietro: si spinge in avanti
            Vector3 f = new Vector3(0.05f, 0.30f, 0.95f), dor = new Vector3(-0.6f, -0.8f, 0f);
            if (one) k.Add(Below(0f, HandPoses.Relaxed));
            else { k.Add(GripAt(0f)); k.Add(GripOff(u0 * 0.5f, HandPoses.Relaxed, 2)); }
            for (int i = 0; i < rounds; i++)
            {
                float s = u0 + i * ui;
                k.Add(Cam(s + ui * 0.18f, Pouch + new Vector3(0.05f, 0.08f, 0.04f), PouchF, PouchD, ShellHold, Ct.Thumb, 2));
                ev.Add(E(s + ui * 0.20f, "shell"));
                k.Add(On(s + ui * 0.40f, Pt.Port, new Vector3(-0.012f, -0.022f, -0.05f), f, dor, ShellHold, Ct.Thumb, 2));
                k.Add(On(s + ui * insertFrac, Pt.Port, new Vector3(-0.004f, -0.012f, 0.012f), f, dor, Tight(ShellHold, 0.15f), Ct.Thumb, 1));
                ev.Add(E(s + ui * insertFrac, "shellin"));
                k.Add(On(s + ui * 0.72f, Pt.Port, new Vector3(-0.02f, -0.045f, -0.02f), f, dor, HandPoses.Relaxed, Ct.Thumb, 2));
            }
            if (one) { k.Add(Below(0.97f, HandPoses.Relaxed, 2)); k.Add(Below(1f, HandPoses.Relaxed)); }
            else { k.Add(GripOff(Mathf.Lerp(u0 + rounds * ui, 1f, 0.35f), HandPoses.Relaxed, 2)); k.Add(GripAt(Mathf.Lerp(u0 + rounds * ui, 1f, 0.85f), 2)); k.Add(GripAt(1f)); }
            // l'arma gira la pancia verso la mano e si avvicina
            var w = new List<OffHand.WKey>();
            Vector3 r1 = new Vector3(-8f, 10f, -38f);
            Vector3 p1 = oh.OffsetToBring(oh.PartPoint(Pt.Port), r1, PortWork);
            w.Add(W(0f, Vector3.zero, Vector3.zero));
            w.Add(W(Mathf.Max(0.02f, u0 * 0.9f), p1, r1, 2));
            for (int i = 0; i < rounds; i++)
            {
                float s = u0 + i * ui + ui * insertFrac;
                w.Add(W(s, p1 + new Vector3(0f, 0f, -0.004f), r1 + new Vector3(-1.5f, 0f, 0f), 1));
                w.Add(W(s + ui * 0.15f, p1, r1, 2));
            }
            w.Add(W(Mathf.Lerp(u0 + rounds * ui, 1f, 0.2f), p1, r1));
            w.Add(W(1f, Vector3.zero, Vector3.zero, 2));
            var a = Make("Ricarica a colpo singolo", k, w, ev);
            return a;
        }

        // ------------------------------------------------------------------ doppietta
        static OffHand.Anim Break(WeaponDef d, OffHand oh, WeaponRig rig, bool empty)
        {
            var k = new List<K>();
            var ev = new List<OffHand.Ev>();
            // le canne si aprono: la sinistra le accompagna giu' dall'astina (che ruota con loro)
            Vector3 af = new Vector3(0.62f, 0.18f, 0.76f), ad = new Vector3(-0.55f, -0.83f, 0f);
            k.Add(GripAt(0f));
            k.Add(On(BreakOpen0, Pt.Forend, new Vector3(-0.004f, -0.022f, 0f), af, ad, HandPoses.Grip, Ct.Palm, 2));
            k.Add(On(BreakOpen1, Pt.Forend, new Vector3(-0.004f, -0.022f, 0f), af, ad, HandPoses.Grip, Ct.Palm, 3));
            // i bossoli li butta l'estrattore quando le canne sono aperte
            if (empty) ev.Add(E(BreakOpen1, "casings"));
            // due cartucce dalla cintura nelle camere
            k.Add(Cam(0.40f, Pouch + new Vector3(0.06f, 0.07f, 0.04f), PouchF, PouchD, ShellHold, Ct.Thumb, 2));
            ev.Add(E(0.41f, "shell2"));
            // il palmo sopra la culatta aperta, le cartucce sotto il palmo lungo le canne: si spingono dentro
            Vector3 sf = new Vector3(0.05f, -0.50f, 0.86f), sd = new Vector3(0f, 0.86f, 0.50f);
            k.Add(On(0.47f, Pt.Breech, new Vector3(0f, 0.035f, -0.03f), sf, sd, ShellHold, Ct.Palm, 2));
            k.Add(On(0.53f, Pt.Breech, new Vector3(0f, 0.016f, -0.004f), sf, sd, Tight(ShellHold, 0.1f), Ct.Palm, 1));
            ev.Add(E(0.53f, "shellin"));
            // sotto l'astina aperta e su: le canne si chiudono
            k.Add(On(0.62f, Pt.Forend, new Vector3(-0.01f, -0.06f, 0f), af, ad, HandPoses.Open, Ct.Palm, 2));
            k.Add(On(BreakClose0, Pt.Forend, new Vector3(-0.004f, -0.022f, 0f), af, ad, HandPoses.Grip, Ct.Palm, 2));
            k.Add(On(BreakClose1, Pt.Forend, new Vector3(-0.004f, -0.022f, 0f), af, ad, HandPoses.Grip, Ct.Palm, 1));
            k.Add(GripAt(0.86f, 2));
            k.Add(GripAt(1f));
            var w = new List<OffHand.WKey>();
            // la bocca in basso a sinistra: le canne aperte scendono e la culatta guarda chi carica
            Vector3 r1 = new Vector3(12f, 20f, -18f);
            Vector3 p1 = oh.OffsetToBring(oh.PartPoint(Pt.Breech), r1, BreechWork);
            w.Add(W(0f, Vector3.zero, Vector3.zero));
            w.Add(W(0.14f, p1, r1, 2));
            w.Add(W(0.32f, p1, r1 + new Vector3(8f, 0f, 0f), 2));      // bocca ancora piu' in giu': i bossoli scivolano fuori
            w.Add(W(0.42f, p1, r1, 2));
            w.Add(W(0.70f, p1, r1));
            w.Add(W(BreakClose1, p1 + new Vector3(0f, 0.012f, 0f), r1 + new Vector3(-6f, 0f, 0f), 1));
            w.Add(W(0.90f, Vector3.zero, Vector3.zero, 2));
            return Make("Ricarica della doppietta", k, w, ev);
        }

        // ------------------------------------------------------------------ revolver
        static OffHand.Anim Revolver(WeaponDef d, OffHand oh, WeaponRig rig, bool empty)
        {
            var k = new List<K>();
            var ev = new List<OffHand.Ev>();
            k.Add(Below(0f, HandPoses.Relaxed));
            // le dita spingono il tamburo fuori dal lato sinistro
            Vector3 cf = new Vector3(0.75f, 0.35f, 0.55f), cd = new Vector3(-0.3f, -0.85f, 0.4f);
            k.Add(On(CylOpen0, Pt.Cylinder, new Vector3(-0.03f, -0.035f, 0f), cf, cd, HandPoses.Relaxed, Ct.Middle, 2));
            k.Add(On(CylOpen1, Pt.Cylinder, new Vector3(-0.012f, -0.004f, 0f), cf, cd, HandPoses.Grip, Ct.Middle, 3));
            // bocca in alto: il pollice batte l'estrattore
            Vector3 ef = new Vector3(0.3f, -0.2f, 0.93f), ed = new Vector3(-0.85f, -0.3f, 0.4f);
            k.Add(On(0.24f, Pt.Cylinder, new Vector3(-0.02f, -0.01f, 0.04f), ef, ed, HandPoses.Relaxed, Ct.Thumb, 2));
            k.Add(On(0.29f, Pt.Cylinder, new Vector3(-0.005f, 0f, 0.012f), ef, ed, HandPoses.Grip, Ct.Thumb, 1));
            ev.Add(E(0.30f, empty ? "casings6" : "pluck"));
            // il caricatore rapido dalla cintura, dentro le camere, girato e lasciato
            k.Add(Cam(0.38f, Pouch + new Vector3(0.05f, 0.06f, 0.04f), PouchF, PouchD, LoaderHold, Ct.Palm, 2));
            ev.Add(E(0.39f, "loader"));
            Vector3 lf = new Vector3(0.1f, 0.2f, 0.97f), ld = new Vector3(-0.6f, -0.8f, 0.1f);
            k.Add(On(0.45f, Pt.Cylinder, new Vector3(-0.004f, -0.004f, -0.075f), lf, ld, LoaderHold, Ct.Palm, 2));
            k.Add(On(MagSeat, Pt.Cylinder, new Vector3(-0.002f, -0.002f, -0.042f), lf, ld, LoaderHold, Ct.Palm, 1));
            ev.Add(E(MagSeat, "loaderin"));
            k.Add(On(0.56f, Pt.Cylinder, new Vector3(-0.02f, -0.02f, -0.07f), lf, ld, HandPoses.Relaxed, Ct.Palm, 2));
            // le dita richiudono il tamburo
            k.Add(On(0.66f, Pt.Cylinder, new Vector3(-0.035f, -0.01f, 0f), cf, cd, HandPoses.Relaxed, Ct.Middle, 2));
            k.Add(On(CylClose1, Pt.Cylinder, new Vector3(-0.012f, -0.004f, 0f), cf, cd, HandPoses.Open, Ct.Middle, 1));
            k.Add(Below(0.93f, HandPoses.Relaxed, 2));
            k.Add(Below(1f, HandPoses.Relaxed));
            var w = new List<OffHand.WKey>();
            Vector3 r1 = new Vector3(-8f, -6f, -20f);
            Vector3 p1 = oh.OffsetToBring(oh.PartPoint(Pt.Cylinder), r1, CylWork);
            w.Add(W(0f, Vector3.zero, Vector3.zero));
            w.Add(W(0.10f, p1, r1, 2));
            w.Add(W(0.22f, p1, r1));
            w.Add(W(0.28f, p1 + new Vector3(0f, 0.05f, -0.02f), new Vector3(-62f, -8f, -12f), 2));     // bocca in alto per l'estrattore
            w.Add(W(0.33f, p1 + new Vector3(0f, 0.05f, -0.02f), new Vector3(-62f, -8f, -12f)));
            w.Add(W(0.40f, p1, r1 + new Vector3(18f, 0f, 0f), 2));                                         // bocca un poco in giu' per il caricatore
            w.Add(W(0.58f, p1, r1 + new Vector3(18f, 0f, 0f)));
            w.Add(W(0.68f, p1, r1, 2));
            w.Add(W(CylClose1, p1, r1 + new Vector3(0f, 0f, 6f), 1));
            w.Add(W(0.90f, Vector3.zero, Vector3.zero, 2));
            return Make("Ricarica del revolver", k, w, ev);
        }
    }
}
