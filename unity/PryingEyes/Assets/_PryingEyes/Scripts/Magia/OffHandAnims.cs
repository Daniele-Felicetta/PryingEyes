using System.Collections.Generic;
using UnityEngine;
using K = PryingEyes.OffHand.Key;
using Sp = PryingEyes.OffHand.Space;
using Pt = PryingEyes.OffHand.Part;
using Ct = PryingEyes.OffHand.Contact;

namespace PryingEyes
{
    /// <summary>I gesti della mano sinistra, uno per magia e uno per sigillo (piu' il lancio dell'equipaggiamento). Spazio della camera:
    /// x a destra, y in su, z avanti; dell'arma: lo stesso con z verso la bocca. Le direzioni sono quelle delle dita e del dorso
    /// della mano sinistra (palmo in giu' = dorso +y; palmo in avanti con le dita in su = dita +y, dorso -z).
    ///
    /// Ogni gesto comincia staccando la mano dall'astina (le dita si aprono mentre il polso scende di 6 cm, cosi' non attraversa
    /// l'astina) o, con le armi a una mano, salendo dal basso fuori campo; finisce al contrario. Le magie lasciano l'arma alla sola
    /// destra, che la abbassa e la inclina; i sigilli girano l'arma col fianco sinistro verso chi guarda e la avvicinano, perche' la
    /// mano ci lavori sopra: il dito traccia l'occhio sul castello, il palmo scorre sulla canna, la mano stringe e schiaffeggia il
    /// caricatore, preme la brina sul fianco, versa l'icore nella finestra di espulsione.</summary>
    public static class OffHandAnims
    {
        // ------------------------------------------------------------------ mattoni
        static K Cam(float t, Vector3 pos, Vector3 fingers, Vector3 dorsal, HandPose pose, Ct c = Ct.Palm, int ease = 0, float tremor = 0f,
                     Vector3 pole = default, Vector3 shoulder = default) =>
            new K { t = t, space = Sp.Camera, pos = pos, fingers = fingers, dorsal = dorsal, pose = pose, contact = c, ease = ease, tremor = tremor, free = 1f, pole = pole, shoulder = shoulder };

        static K On(float t, Pt part, Vector3 off, Vector3 fingers, Vector3 dorsal, HandPose pose, Ct c = Ct.Palm, int ease = 0, float tremor = 0f, float barrel = 0f) =>
            new K { t = t, space = Sp.Weapon, part = part, pos = off, fingers = fingers, dorsal = dorsal, pose = pose, contact = c, ease = ease, tremor = tremor, free = 1f, barrel = barrel };

        static K GripAt(float t, int ease = 0) => new K { t = t, space = Sp.Grip, gripPose = true, ease = ease };
        static K GripOff(float t, HandPose pose, int ease = 0) => new K { t = t, space = Sp.Grip, pos = new Vector3(-0.025f, -0.06f, -0.035f), pose = pose, ease = ease, free = 0.5f };
        static K Below(float t, HandPose pose, int ease = 0) => new K { t = t, space = Sp.Below, pose = pose, ease = ease, free = 1f };

        static readonly Vector3 Fwd = Vector3.forward, Up = Vector3.up, Back = Vector3.back, Down = Vector3.down, Left = Vector3.left, Right = Vector3.right;

        /// <summary>Apertura e chiusura del gesto: dalla presa (o dal basso) e ritorno.</summary>
        static List<K> Open(bool oneHanded, float tRelease)
        {
            var l = new List<K>();
            if (oneHanded) l.Add(Below(0f, HandPoses.Relaxed));
            else { l.Add(GripAt(0f)); l.Add(GripOff(tRelease, HandPoses.Relaxed, 2)); }
            return l;
        }

        static void Close(List<K> l, bool oneHanded, float tEnd)
        {
            if (oneHanded) l.Add(Below(tEnd, HandPoses.Relaxed));
            else { l.Add(GripOff(tEnd - 0.12f, HandPoses.Relaxed)); l.Add(GripAt(tEnd, 2)); }
        }

        static OffHand.WKey W(float t, Vector3 pos, Vector3 rot, int ease = 0) => new OffHand.WKey { t = t, pos = pos, rot = rot, ease = ease };

        /// <summary>L'arma retta dalla sola destra mentre la sinistra fa il gesto: si abbassa e si inclina un poco.</summary>
        static OffHand.WKey[] OneHandCarry(bool oneHanded, float dur)
        {
            Vector3 p = oneHanded ? new Vector3(0.012f, -0.012f, 0f) : new Vector3(0.018f, -0.038f, -0.02f);
            Vector3 r = oneHanded ? new Vector3(2f, 2f, 3f) : new Vector3(6f, -3f, 7f);
            return new[] { W(0f, Vector3.zero, Vector3.zero), W(0.16f, p, r), W(dur - 0.2f, p, r), W(dur, Vector3.zero, Vector3.zero) };
        }

        /// <summary>Scarto e rotazione dell'arma offerta. Le armi a due mani girano di piu' la bocca a sinistra (36 gradi): avvicinate alla
        /// spalla mostrano il fianco sinistro e la canna attraversa la vista in diagonale, invece di puntare dritta davanti all'occhio.</summary>
        static Vector3 PresentP(bool one) => new Vector3(-0.13f, 0.045f, -0.11f);
        static Vector3 PresentR(bool one) => one ? new Vector3(9f, -15f, -24f) : new Vector3(6f, -36f, -20f);

        /// <summary>L'arma offerta alla mano sinistra: a sinistra, piu' vicina, col fianco sinistro e il dorso verso chi guarda. Le armi
        /// lunghe vengono anche portate a portata della spalla (pull: OffHand.ReachTarget), coi tempi della presentazione.</summary>
        static OffHand.WKey[] Present(float dur, bool one, params OffHand.WKey[] extra)
        {
            Vector3 p = PresentP(one), r = PresentR(one);
            var l = new List<OffHand.WKey> { W(0f, Vector3.zero, Vector3.zero), W(0.24f, p, r, 2) };
            l.AddRange(extra);
            l.Add(W(dur - 0.24f, p, r)); l.Add(W(dur, Vector3.zero, Vector3.zero));
            l.Sort((a, b) => a.t.CompareTo(b.t));
            for (int i = 1; i < l.Count - 1; i++) { var k = l[i]; k.pull = 1f; l[i] = k; }
            return l.ToArray();
        }

        static OffHand.Ev E(float t, string id) => new OffHand.Ev { t = t, id = id };

        static OffHand.Anim Make(string name, float dur, List<K> keys, OffHand.WKey[] w, params OffHand.Ev[] ev)
        {
            var evs = new List<OffHand.Ev>(ev);
            evs.Sort((a, b) => a.t.CompareTo(b.t));
            return new OffHand.Anim { name = name, duration = dur, keys = keys.ToArray(), weapon = w, events = evs.ToArray() };
        }

        static HandPose Tight(HandPose p, float k) => HandPose.Lerp(p, HandPoses.Fist, k);

        // ------------------------------------------------------------------ magie
        /// <summary>Il gesto della magia (nodo 12..18 della Stregoneria). Eventi: "cast" quando la magia parte, piu' quelli degli effetti.</summary>
        public static OffHand.Anim Spell(int node, bool oneHanded)
        {
            switch (node)
            {
                case 12: return Fiamma(oneHanded);
                case 13: return Fulmine(oneHanded);
                case 14: return Gelo(oneHanded);
                case 15: return Sangue(oneHanded);
                case 16: return Ombra(oneHanded);
                case 17: return Onda(oneHanded);
                default: return Essenza(oneHanded);
            }
        }

        /// <summary>Fiamma Sacrilega: palmo in su davanti al petto, la fiamma nasce nella coppa delle dita; la mano carica indietro verso la
        /// spalla e la scaglia in avanti aprendo le dita (lancio a spinta, palmo verso il bersaglio), poi accompagna in basso.</summary>
        static OffHand.Anim Fiamma(bool one)
        {
            const float D = 0.98f;
            var k = Open(one, 0.10f);
            k.Add(Cam(0.30f, new Vector3(-0.12f, -0.135f, 0.40f), new Vector3(0.28f, 0.22f, 0.93f), new Vector3(0.05f, -1f, 0.2f), HandPoses.Cup, Ct.Palm, 2));
            k.Add(Cam(0.42f, new Vector3(-0.125f, -0.12f, 0.385f), new Vector3(0.25f, 0.3f, 0.92f), new Vector3(0.05f, -1f, 0.25f), Tight(HandPoses.Cup, 0.15f), Ct.Palm, 0, 0.6f));
            k.Add(Cam(0.53f, new Vector3(-0.235f, -0.075f, 0.285f), new Vector3(0.12f, 0.85f, 0.5f), new Vector3(0.2f, 0.45f, -0.87f), Tight(HandPoses.Cup, 0.3f), Ct.Palm, 2, 0.8f,
                      new Vector3(-0.8f, -0.55f, -0.2f), new Vector3(0f, 0f, -0.04f)));
            k.Add(Cam(0.64f, new Vector3(-0.10f, -0.075f, 0.52f), new Vector3(0.08f, 0.62f, 0.78f), new Vector3(0f, 0.75f, -0.62f), HandPoses.Open, Ct.Palm, 1, 0f,
                      default, new Vector3(0.01f, 0f, 0.07f)));
            k.Add(Cam(0.75f, new Vector3(-0.05f, -0.13f, 0.48f), new Vector3(0.22f, 0.12f, 0.97f), new Vector3(0f, 1f, -0.15f), HandPoses.Relaxed, Ct.Palm, 2));
            Close(k, one, D);
            return Make("Fiamma Sacrilega", D, k, OneHandCarry(one, D), E(0.22f, "ignite"), E(0.62f, "cast"));
        }

        /// <summary>Catena del Fulmine: la mano sale col palmo avanti e le dita ad artiglio, tese e tremanti, le scintille corrono tra i
        /// polpastrelli; uno scatto in avanti con le dita che si spalancano scarica il fulmine, la mano rimbalza indietro e si scuote.</summary>
        static OffHand.Anim Fulmine(bool one)
        {
            const float D = 0.88f;
            var k = Open(one, 0.10f);
            k.Add(Cam(0.33f, new Vector3(-0.12f, -0.065f, 0.40f), new Vector3(0.14f, 0.86f, 0.49f), Back, HandPoses.Claw, Ct.Palm, 2, 2.4f));
            k.Add(Cam(0.44f, new Vector3(-0.13f, -0.06f, 0.37f), new Vector3(0.12f, 0.9f, 0.42f), Back, Tight(HandPoses.Claw, 0.12f), Ct.Palm, 0, 3.2f));
            k.Add(Cam(0.51f, new Vector3(-0.11f, -0.07f, 0.50f), new Vector3(0.1f, 0.6f, 0.79f), Back, HandPoses.Open, Ct.Palm, 1, 4f, default, new Vector3(0f, 0f, 0.05f)));
            k.Add(Cam(0.61f, new Vector3(-0.13f, -0.035f, 0.43f), new Vector3(0f, 0.92f, 0.38f), Back, HandPoses.Claw, Ct.Palm, 2, 1.2f));
            k.Add(Cam(0.71f, new Vector3(-0.155f, -0.115f, 0.40f), new Vector3(0.2f, 0.5f, 0.84f), new Vector3(-0.3f, 0.8f, -0.4f), HandPoses.Relaxed, Ct.Palm, 0, 1.5f));
            Close(k, one, D);
            return Make("Catena del Fulmine", D, k, OneHandCarry(one, D), E(0.20f, "spark"), E(0.49f, "cast"));
        }

        /// <summary>Morsa di Gelo: la mano si porta davanti col palmo verso il viso, le dita raccolte, e la brina si condensa; poi ruota il
        /// palmo in avanti e distende le dita unite (spinta piatta): il cono di gelo parte dal palmo e la mano resta tesa un istante.</summary>
        static OffHand.Anim Gelo(bool one)
        {
            const float D = 1.02f;
            var k = Open(one, 0.10f);
            k.Add(Cam(0.34f, new Vector3(-0.10f, -0.10f, 0.36f), new Vector3(0.1f, 1f, 0.15f), Fwd, HandPoses.Cup, Ct.Palm, 2, 0.5f));
            k.Add(Cam(0.45f, new Vector3(-0.095f, -0.085f, 0.38f), new Vector3(0.1f, 0.97f, 0.2f), Fwd, Tight(HandPoses.Cup, 0.2f), Ct.Palm, 0, 1f));
            k.Add(Cam(0.57f, new Vector3(-0.11f, -0.085f, 0.48f), new Vector3(0.07f, 0.76f, 0.64f), Back, HandPoses.Flat, Ct.Palm, 2, 0.3f, default, new Vector3(0f, 0f, 0.05f)));
            k.Add(Cam(0.74f, new Vector3(-0.11f, -0.083f, 0.495f), new Vector3(0.07f, 0.78f, 0.62f), Back, HandPoses.Flat, Ct.Palm, 0, 1.2f, default, new Vector3(0f, 0f, 0.05f)));
            k.Add(Cam(0.86f, new Vector3(-0.13f, -0.12f, 0.43f), new Vector3(0.2f, 0.45f, 0.87f), new Vector3(-0.2f, 0.85f, -0.4f), HandPoses.Relaxed, Ct.Palm, 0));
            Close(k, one, D);
            return Make("Morsa di Gelo", D, k, OneHandCarry(one, D), E(0.18f, "charge"), E(0.56f, "cast"));
        }

        /// <summary>Sigillo di Sangue: la mano fa le corna verso il bersaglio (il marchio), poi si chiude ad artiglio e tira lentamente verso
        /// il petto tremando mentre il filo di sangue scorre dal nemico al palmo; alla fine il pugno si serra di colpo.</summary>
        static OffHand.Anim Sangue(bool one)
        {
            const float D = 2.22f;
            var k = Open(one, 0.10f);
            k.Add(Cam(0.32f, new Vector3(-0.12f, -0.075f, 0.45f), new Vector3(0.05f, 0.8f, 0.6f), Back, HandPoses.Horns, Ct.Palm, 2, 0.4f));
            k.Add(Cam(0.46f, new Vector3(-0.12f, -0.073f, 0.46f), new Vector3(0.05f, 0.82f, 0.57f), Back, HandPoses.Claw, Ct.Palm, 1, 2f));
            k.Add(Cam(1.05f, new Vector3(-0.125f, -0.09f, 0.39f), new Vector3(0.1f, 0.9f, 0.4f), Back, Tight(HandPoses.Claw, 0.25f), Ct.Palm, 0, 3f));
            k.Add(Cam(1.75f, new Vector3(-0.145f, -0.115f, 0.335f), new Vector3(0.12f, 0.93f, 0.33f), Back, Tight(HandPoses.Claw, 0.55f), Ct.Palm, 0, 3.4f));
            k.Add(Cam(1.95f, new Vector3(-0.15f, -0.12f, 0.33f), new Vector3(0.12f, 0.93f, 0.33f), Back, HandPoses.Fist, Ct.Palm, 1, 1f));
            k.Add(Cam(2.08f, new Vector3(-0.17f, -0.15f, 0.36f), new Vector3(0.2f, 0.6f, 0.77f), new Vector3(-0.4f, 0.7f, -0.5f), Tight(HandPoses.Relaxed, 0.3f), Ct.Palm, 0));
            Close(k, one, D);
            return Make("Sigillo di Sangue", D, k, OneHandCarry(one, D), E(0.30f, "mark"), E(0.46f, "cast"), E(1.95f, "squeeze"));
        }

        /// <summary>Passo d'Ombra: la mano sale all'altezza del viso col pollice premuto sul medio, schiocca (il medio scende sul palmo,
        /// l'indice si distende) e nello schiocco si attraversa l'ombra; la mano ricade.</summary>
        static OffHand.Anim Ombra(bool one)
        {
            const float D = 0.64f;
            var k = Open(one, 0.08f);
            k.Add(Cam(0.25f, new Vector3(-0.105f, -0.03f, 0.36f), new Vector3(0.1f, 1f, 0.1f), new Vector3(-0.7f, 0f, -0.7f), HandPoses.SnapReady, Ct.Palm, 2));
            k.Add(Cam(0.30f, new Vector3(-0.10f, -0.028f, 0.365f), new Vector3(0.1f, 1f, 0.12f), new Vector3(-0.7f, 0f, -0.7f), HandPoses.SnapReady, Ct.Palm, 0, 1.2f));
            k.Add(Cam(0.335f, new Vector3(-0.10f, -0.05f, 0.37f), new Vector3(0.12f, 0.98f, 0.15f), new Vector3(-0.7f, 0f, -0.7f), HandPoses.Snapped, Ct.Palm, 1));
            k.Add(Cam(0.46f, new Vector3(-0.165f, -0.165f, 0.36f), new Vector3(0.2f, 0.55f, 0.8f), new Vector3(-0.5f, 0.6f, -0.6f), HandPoses.Relaxed, Ct.Palm, 2));
            Close(k, one, D);
            return Make("Passo d'Ombra", D, k, OneHandCarry(one, D), E(0.33f, "cast"));
        }

        /// <summary>Onda del Dodecaedro: la mano si ritira al petto chiusa a pugno raccogliendo l'energia, poi esplode in una spinta a palmo
        /// aperto con le dita rovesciate all'indietro; la spalla si protende, l'onda parte dal palmo.</summary>
        static OffHand.Anim Onda(bool one)
        {
            const float D = 0.92f;
            var k = Open(one, 0.10f);
            k.Add(Cam(0.36f, new Vector3(-0.14f, -0.14f, 0.285f), new Vector3(0.2f, 0.6f, 0.77f), new Vector3(-1f, 0f, 0.1f), HandPoses.Fist, Ct.Palm, 2, 1.6f,
                      new Vector3(-0.9f, -0.4f, -0.2f), new Vector3(0f, 0f, -0.03f)));
            k.Add(Cam(0.52f, new Vector3(-0.10f, -0.085f, 0.52f), new Vector3(0f, 0.8f, 0.6f), Back, HandPoses.Thrust, Ct.Palm, 1, 0f, default, new Vector3(0.01f, 0f, 0.08f)));
            k.Add(Cam(0.64f, new Vector3(-0.10f, -0.083f, 0.525f), new Vector3(0f, 0.8f, 0.6f), Back, HandPoses.Thrust, Ct.Palm, 0, 0.8f, default, new Vector3(0.01f, 0f, 0.08f)));
            k.Add(Cam(0.77f, new Vector3(-0.12f, -0.12f, 0.43f), new Vector3(0.2f, 0.45f, 0.87f), new Vector3(-0.3f, 0.85f, -0.4f), HandPoses.Relaxed, Ct.Palm, 0));
            Close(k, one, D);
            return Make("Onda del Dodecaedro", D, k, OneHandCarry(one, D), E(0.18f, "gather"), E(0.50f, "cast"));
        }

        /// <summary>Essenza Maledetta: la mano sale davanti al viso col palmo verso di te, aperta; le vene rosse le salgono sul guanto mentre
        /// le dita si chiudono lentamente ad artiglio con un tremito che cresce; il pugno si serra e la trasformazione esplode.</summary>
        static OffHand.Anim Essenza(bool one)
        {
            const float D = 1.74f;
            var k = Open(one, 0.10f);
            k.Add(Cam(0.46f, new Vector3(-0.09f, -0.04f, 0.34f), new Vector3(0.02f, 1f, 0.1f), Fwd, HandPoses.Open, Ct.Palm, 2, 0.8f,
                      new Vector3(-0.5f, -0.6f, -0.6f)));
            k.Add(Cam(1.05f, new Vector3(-0.095f, -0.035f, 0.33f), new Vector3(0.02f, 1f, 0.12f), Fwd, HandPoses.Claw, Ct.Palm, 0, 4.2f, new Vector3(-0.5f, -0.6f, -0.6f)));
            k.Add(Cam(1.19f, new Vector3(-0.10f, -0.05f, 0.335f), new Vector3(0.02f, 1f, 0.12f), Fwd, HandPoses.Fist, Ct.Palm, 1, 2f, new Vector3(-0.5f, -0.6f, -0.6f)));
            k.Add(Cam(1.42f, new Vector3(-0.16f, -0.14f, 0.36f), new Vector3(0.2f, 0.6f, 0.77f), new Vector3(-0.4f, 0.7f, -0.5f), HandPoses.Fist, Ct.Palm, 2, 0.8f));
            Close(k, one, D);
            return Make("Essenza Maledetta", D, k, OneHandCarry(one, D), E(0.26f, "veins"), E(1.18f, "cast"));
        }

        // ------------------------------------------------------------------ sigilli sull'arma
        /// <summary>Il gesto del sigillo (nodo 12..18 dell'Arsenale). "cast" e' quando il sigillo prende l'arma.</summary>
        public static OffHand.Anim Infusion(int node, bool oneHanded)
        {
            switch (node)
            {
                case 12: return Critici(oneHanded);
                case 13: return Emorragia(oneHanded);
                case 14: return Incendio(oneHanded);
                case 15: return Corrosione(oneHanded);
                case 16: return Folgore(oneHanded);
                case 17: return Brina(oneHanded);
                default: return Maledetta(oneHanded);
            }
        }

        /// <summary>Danni Critici: l'indice si posa sul fianco sinistro del castello e traccia un cerchio con l'occhio dentro (la scia rossa
        /// resta incisa), poi va alla mira e la batte due volte: l'occhio si accende sull'arma.</summary>
        static OffHand.Anim Critici(bool one)
        {
            const float D = 1.62f;
            var k = Open(one, 0.10f);
            Vector3 fin = new Vector3(0.82f, -0.12f, 0.55f), dor = new Vector3(-0.35f, 1f, 0f);
            const float r = 0.021f, at = 0.12f;
            k.Add(On(0.30f, Pt.LeftSide, new Vector3(-0.03f, r, 0f), fin, dor, HandPoses.Point, Ct.Index, 2, 0f, at));
            // il cerchio sul fianco (piano z-y dell'arma), da ore dodici in senso orario
            for (int i = 0; i <= 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                k.Add(On(0.38f + i * 0.065f, Pt.LeftSide, new Vector3(-0.002f, Mathf.Cos(a) * r, Mathf.Sin(a) * r), fin, dor, HandPoses.Point, Ct.Index, 3, 0.15f, at));
            }
            // la pupilla: un tratto verticale nel centro
            k.Add(On(0.99f, Pt.LeftSide, new Vector3(-0.002f, 0.008f, 0f), fin, dor, HandPoses.Point, Ct.Index, 0, 0f, at));
            k.Add(On(1.06f, Pt.LeftSide, new Vector3(-0.002f, -0.008f, 0f), fin, dor, HandPoses.Point, Ct.Index, 3, 0f, at));
            // la mira, due colpetti col polpastrello
            Vector3 sf = new Vector3(0.55f, -0.55f, 0.6f), sd = new Vector3(-0.5f, 0.6f, -0.2f);
            k.Add(On(1.16f, Pt.Sight, new Vector3(-0.02f, 0.03f, -0.015f), sf, sd, HandPoses.Point, Ct.Index, 0));
            k.Add(On(1.21f, Pt.Sight, new Vector3(-0.012f, 0.012f, -0.01f), sf, sd, HandPoses.Point, Ct.Index, 1));
            k.Add(On(1.26f, Pt.Sight, new Vector3(-0.02f, 0.03f, -0.015f), sf, sd, HandPoses.Point, Ct.Index, 2));
            k.Add(On(1.31f, Pt.Sight, new Vector3(-0.012f, 0.012f, -0.01f), sf, sd, HandPoses.Point, Ct.Index, 1));
            k.Add(On(1.38f, Pt.Sight, new Vector3(-0.04f, 0.06f, -0.03f), sf, sd, HandPoses.Relaxed, Ct.Index, 2));
            Close(k, one, D);
            return Make("Danni Critici", D, k, Present(D, one), E(0.38f, "trace"), E(1.08f, "trace_end"), E(1.21f, "tap"), E(1.31f, "tap"), E(1.32f, "cast"));
        }

        /// <summary>Sanguinamento: la mano afferra la canna davanti e la stringe scorrendo all'indietro verso il castello (le rune del sangue
        /// si accendono dietro il palmo), poi si apre sopra il castello e il sangue gocciola sull'arma; la mano si scuote.</summary>
        static OffHand.Anim Emorragia(bool one)
        {
            const float D = 1.74f;
            var k = Open(one, 0.10f);
            Vector3 gf = new Vector3(1f, -0.25f, 0.1f), gd = Up;
            k.Add(On(0.30f, Pt.Barrel, new Vector3(-0.01f, 0.045f, 0f), gf, gd, HandPoses.Open, Ct.Palm, 2, 0f, 0.78f));
            k.Add(On(0.42f, Pt.Barrel, new Vector3(-0.006f, 0.014f, 0f), gf, gd, HandPoses.Grip, Ct.Palm, 1, 0.5f, 0.78f));
            k.Add(On(0.66f, Pt.Barrel, new Vector3(-0.006f, 0.014f, 0f), gf, gd, Tight(HandPoses.Grip, 0.2f), Ct.Palm, 3, 1.6f, 0.46f));
            k.Add(On(0.90f, Pt.Barrel, new Vector3(-0.006f, 0.014f, 0f), gf, gd, Tight(HandPoses.Grip, 0.3f), Ct.Palm, 3, 1.8f, 0.14f));
            Vector3 df = new Vector3(0.45f, -0.1f, 0.9f), dd = new Vector3(0f, 1f, 0.1f);
            k.Add(On(1.06f, Pt.Receiver, new Vector3(-0.012f, 0.085f, 0.01f), df, dd, HandPoses.Cup, Ct.Palm, 2, 0.4f));
            k.Add(On(1.24f, Pt.Receiver, new Vector3(-0.012f, 0.08f, 0.01f), df, dd, HandPoses.Open, Ct.Palm, 0, 1.4f));
            k.Add(On(1.32f, Pt.Receiver, new Vector3(0.006f, 0.086f, 0.0f), df, dd, HandPoses.Open, Ct.Palm, 0, 0.4f));
            k.Add(On(1.39f, Pt.Receiver, new Vector3(-0.03f, 0.082f, 0.01f), df, dd, HandPoses.Relaxed, Ct.Palm, 0));
            Close(k, one, D);
            return Make("Sanguinamento", D, k, Present(D, one), E(0.42f, "grab"), E(0.48f, "cut"), E(1.08f, "drip"), E(1.26f, "cast"));
        }

        /// <summary>Incendiario: la mano sopra la finestra di espulsione schiocca le dita e il fuoco le nasce nel palmo; il palmo piatto scorre
        /// sul dorso della canna dal castello alla bocca lasciando le fiamme, la canna si arroventa; in fondo la mano scuote via il fuoco.</summary>
        static OffHand.Anim Incendio(bool one)
        {
            const float D = 1.62f;
            var k = Open(one, 0.10f);
            Vector3 sf = new Vector3(0.25f, 0.35f, 0.9f), sd = new Vector3(-0.3f, 0.9f, -0.3f);
            k.Add(On(0.28f, Pt.Eject, new Vector3(-0.025f, 0.075f, -0.01f), sf, sd, HandPoses.SnapReady, Ct.Palm, 2));
            k.Add(On(0.36f, Pt.Eject, new Vector3(-0.025f, 0.072f, -0.008f), sf, sd, HandPoses.SnapReady, Ct.Palm, 0, 1f));
            k.Add(On(0.40f, Pt.Eject, new Vector3(-0.025f, 0.066f, -0.005f), sf, sd, HandPoses.Snapped, Ct.Palm, 1));
            Vector3 ff = new Vector3(0.05f, -0.12f, 1f), fd = new Vector3(0f, 1f, 0.12f);
            k.Add(On(0.52f, Pt.Barrel, new Vector3(-0.005f, 0.026f, 0f), ff, fd, HandPoses.Flat, Ct.Palm, 2, 0f, 0f));
            k.Add(On(0.72f, Pt.Barrel, new Vector3(-0.005f, 0.024f, 0f), ff, fd, HandPoses.Flat, Ct.Palm, 3, 0.3f, 0.38f));
            k.Add(On(0.92f, Pt.Barrel, new Vector3(-0.005f, 0.024f, 0f), ff, fd, HandPoses.Flat, Ct.Palm, 3, 0.3f, 0.74f));
            k.Add(On(1.06f, Pt.Barrel, new Vector3(-0.005f, 0.026f, 0f), ff, fd, HandPoses.Flat, Ct.Palm, 3, 0.2f, 1f));
            k.Add(On(1.18f, Pt.Barrel, new Vector3(-0.04f, 0.085f, -0.05f), new Vector3(0.1f, 0.4f, 0.9f), new Vector3(-0.3f, 0.85f, -0.4f), HandPoses.Open, Ct.Palm, 2, 0.6f, 1f));
            k.Add(On(1.27f, Pt.Barrel, new Vector3(-0.02f, 0.09f, -0.06f), new Vector3(0.1f, 0.4f, 0.9f), new Vector3(-0.3f, 0.85f, -0.4f), HandPoses.Relaxed, Ct.Palm, 0, 0.6f, 1f));
            Close(k, one, D);
            return Make("Incendiario", D, k, Present(D, one), E(0.40f, "ignite"), E(0.52f, "trail"), E(1.06f, "cast"));
        }

        /// <summary>Corrosivo: la mano a coppa col palmo in su accanto alla finestra di espulsione: l'icore verde vi sgorga; la mano si
        /// inclina e lo versa nel castello, poi scuote via le ultime gocce.</summary>
        static OffHand.Anim Corrosione(bool one)
        {
            const float D = 1.72f;
            var k = Open(one, 0.10f);
            Vector3 cf = new Vector3(0.6f, 0.3f, 0.75f);
            k.Add(On(0.32f, Pt.Eject, new Vector3(-0.045f, 0.06f, -0.01f), cf, Down, HandPoses.Cup, Ct.Palm, 2, 0.3f));
            k.Add(On(0.52f, Pt.Eject, new Vector3(-0.042f, 0.062f, -0.01f), cf, Down, Tight(HandPoses.Cup, 0.08f), Ct.Palm, 0, 0.6f));
            k.Add(On(0.70f, Pt.Eject, new Vector3(-0.02f, 0.055f, -0.005f), new Vector3(0.75f, -0.3f, 0.6f), new Vector3(-0.7f, 0.7f, 0f), HandPoses.Cup, Ct.Palm, 0, 0.4f));
            k.Add(On(1.00f, Pt.Eject, new Vector3(-0.018f, 0.052f, -0.005f), new Vector3(0.78f, -0.35f, 0.52f), new Vector3(-0.72f, 0.69f, 0f), HandPoses.Cup, Ct.Palm, 0, 0.3f));
            k.Add(On(1.12f, Pt.Eject, new Vector3(-0.05f, 0.08f, -0.02f), cf, new Vector3(-0.2f, -0.9f, 0f), HandPoses.Relaxed, Ct.Palm, 2));
            k.Add(On(1.20f, Pt.Eject, new Vector3(-0.035f, 0.08f, -0.02f), cf, new Vector3(-0.2f, -0.9f, 0f), HandPoses.Open, Ct.Palm, 0));
            k.Add(On(1.27f, Pt.Eject, new Vector3(-0.065f, 0.08f, -0.02f), cf, new Vector3(-0.2f, -0.9f, 0f), HandPoses.Relaxed, Ct.Palm, 0));
            Close(k, one, D);
            return Make("Corrosivo", D, k, Present(D, one), E(0.32f, "ichor"), E(0.70f, "pour"), E(1.02f, "cast"));
        }

        /// <summary>Elettrificato: la mano stringe il caricatore di lato; le dita si spalancano e gli archi corrono tra polpastrelli e metallo;
        /// poi la mano scende sotto il fondello e lo schiaffeggia verso l'alto: scintille, il caricatore resta a ronzare d'azzurro.</summary>
        static OffHand.Anim Folgore(bool one)
        {
            const float D = 1.52f;
            var k = Open(one, 0.10f);
            Vector3 mf = new Vector3(0.45f, -0.3f, 0.84f), md = Left;
            k.Add(On(0.32f, Pt.Mag, new Vector3(-0.024f, -0.025f, 0f), mf, md, HandPoses.Grip, Ct.Palm, 2));
            k.Add(On(0.56f, Pt.Mag, new Vector3(-0.05f, -0.03f, -0.005f), mf, md, HandPoses.Claw, Ct.Palm, 0, 3f));
            k.Add(On(0.74f, Pt.Mag, new Vector3(-0.045f, -0.035f, -0.005f), mf, md, HandPoses.Claw, Ct.Palm, 0, 3.4f));
            Vector3 bf = new Vector3(0.2f, 0.3f, 0.93f);
            k.Add(On(0.88f, Pt.MagBase, new Vector3(0f, -0.065f, 0f), bf, Down, HandPoses.Flat, Ct.Palm, 2));
            k.Add(On(0.96f, Pt.MagBase, new Vector3(0f, -0.012f, 0f), bf, Down, HandPoses.Flat, Ct.Palm, 1));
            k.Add(On(1.07f, Pt.MagBase, new Vector3(-0.01f, -0.075f, 0f), bf, Down, HandPoses.Relaxed, Ct.Palm, 2));
            Close(k, one, D);
            var an = Make("Elettrificato", D, k, Present(D, one, W(0.96f, PresentP(one) + new Vector3(0f, 0.015f, 0f), PresentR(one) + new Vector3(-4f, 0f, 0f), 1),
                                                            W(1.04f, PresentP(one), PresentR(one), 2)),
                          E(0.32f, "grab"), E(0.44f, "arcs"), E(0.96f, "slap"), E(0.97f, "cast"));
            an.reachOffset = new Vector3(0.02f, 0.07f, 0.02f);     // il caricatore e il suo fondello stanno sotto l'arma: piu' in alto, in vista
            return an;
        }

        /// <summary>Gelido: il palmo piatto si appoggia sul fianco sinistro dell'arma e preme; la brina corre dal palmo su tutta l'arma; la
        /// mano si stacca piano lasciando il vapore.</summary>
        static OffHand.Anim Brina(bool one)
        {
            const float D = 1.72f;
            var k = Open(one, 0.10f);
            Vector3 pf = new Vector3(0f, 0.15f, 1f), pd = Left;
            const float at = 0.25f;
            k.Add(On(0.35f, Pt.LeftSide, new Vector3(-0.03f, 0f, 0f), pf, pd, HandPoses.Flat, Ct.Palm, 2, 0f, at));
            k.Add(On(0.46f, Pt.LeftSide, new Vector3(-0.004f, 0f, 0f), pf, pd, HandPoses.Flat, Ct.Palm, 2, 0.5f, at));
            k.Add(On(1.08f, Pt.LeftSide, new Vector3(-0.003f, 0f, 0f), pf, pd, HandPoses.Flat, Ct.Palm, 0, 0.7f, at));
            k.Add(On(1.38f, Pt.LeftSide, new Vector3(-0.07f, 0.02f, -0.02f), pf, pd, HandPoses.Relaxed, Ct.Palm, 2, 0f, at));
            Close(k, one, D);
            return Make("Gelido", D, k, Present(D, one), E(0.46f, "press"), E(1.06f, "cast"));
        }

        /// <summary>Arma Maledetta: la mano afferra la canna a meta' e la stringe; l'arma sussulta e trema mentre le rune si accendono dalla
        /// bocca al calcio; la mano viene respinta indietro aperta, fumante, e l'arma resta avvolta dall'aura.</summary>
        static OffHand.Anim Maledetta(bool one)
        {
            const float D = 2.22f;
            var k = Open(one, 0.10f);
            Vector3 gf = new Vector3(1f, -0.25f, 0.1f);
            k.Add(On(0.34f, Pt.Barrel, new Vector3(-0.01f, 0.05f, 0f), gf, Up, HandPoses.Open, Ct.Palm, 2, 0f, 0.55f));
            k.Add(On(0.48f, Pt.Barrel, new Vector3(-0.006f, 0.013f, 0f), gf, Up, Tight(HandPoses.Grip, 0.35f), Ct.Palm, 1, 1f, 0.55f));
            k.Add(On(1.40f, Pt.Barrel, new Vector3(-0.006f, 0.013f, 0f), gf, Up, Tight(HandPoses.Grip, 0.45f), Ct.Palm, 0, 5f, 0.55f));
            k.Add(On(1.50f, Pt.Barrel, new Vector3(-0.07f, 0.10f, -0.07f), new Vector3(0.3f, 0.5f, 0.8f), new Vector3(-0.4f, 0.8f, -0.4f), HandPoses.Open, Ct.Palm, 1, 2.5f, 0.55f));
            k.Add(On(1.72f, Pt.Barrel, new Vector3(-0.06f, 0.09f, -0.08f), new Vector3(0.3f, 0.5f, 0.8f), new Vector3(-0.4f, 0.8f, -0.4f), HandPoses.Claw, Ct.Palm, 0, 1.6f, 0.55f));
            Close(k, one, D);
            // l'arma sussulta mentre le rune la prendono
            var shake = new List<OffHand.WKey>();
            Vector3 p = PresentP(one), r = PresentR(one);
            for (int i = 0; i < 9; i++)
            {
                float t = 0.52f + i * 0.1f;
                float s = (i % 2 == 0 ? 1f : -1f) * (1f - i / 10f);
                shake.Add(W(t, p + new Vector3(0.004f * s, 0.003f * s, 0f), r + new Vector3(1.4f * s, 0.8f * s, 2.2f * s), 0));
            }
            shake.Add(W(1.5f, p + new Vector3(0f, 0f, 0.012f), r + new Vector3(-3f, 0f, 0f), 1));
            return Make("Arma Maledetta", D, k, Present(D, one, shake.ToArray()), E(0.48f, "grab"), E(0.55f, "runes"), E(1.48f, "cast"), E(1.50f, "burn"));
        }

        // ------------------------------------------------------------------ lancio dell'equipaggiamento
        /// <summary>Lancio di mano sinistra (acquasanta, carillon): la mano prende l'oggetto, carica in basso a sinistra e lo scaglia in
        /// avanti e in alto; "take" quando l'oggetto compare nella mano, "release" quando parte.</summary>
        public static OffHand.Anim Throw(bool oneHanded)
        {
            const float D = 0.74f;
            var k = Open(oneHanded, 0.08f);
            k.Add(Cam(0.22f, new Vector3(-0.21f, -0.15f, 0.30f), new Vector3(0.2f, 0.7f, 0.68f), new Vector3(-0.6f, 0.1f, -0.8f), HandPoses.Grip, Ct.Palm, 2, 0f,
                      new Vector3(-0.85f, -0.45f, -0.25f), new Vector3(0f, 0f, -0.03f)));
            k.Add(Cam(0.40f, new Vector3(-0.10f, -0.055f, 0.53f), new Vector3(0.1f, 0.5f, 0.86f), new Vector3(0f, 0.86f, -0.5f), HandPoses.Open, Ct.Palm, 1, 0f,
                      default, new Vector3(0.01f, 0f, 0.07f)));
            k.Add(Cam(0.53f, new Vector3(-0.04f, -0.13f, 0.47f), new Vector3(0.25f, 0.1f, 0.96f), new Vector3(0f, 1f, -0.1f), HandPoses.Relaxed, Ct.Palm, 2));
            Close(k, oneHanded, D);
            return Make("Lancio", D, k, OneHandCarry(oneHanded, D), E(0.10f, "take"), E(0.38f, "release"));
        }
    }
}
