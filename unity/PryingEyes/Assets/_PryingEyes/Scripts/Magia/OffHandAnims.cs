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
    /// destra, che la abbassa e la inclina; i sigilli (rifatti il 2026-11-17) girano l'arma sul fianco, col sinistro verso chi guarda e
    /// il fondo verso la mano, e la avvicinano e la alzano: la sinistra lavora SOTTO l'arma e il calore sale da li' (WeaponAura). Il
    /// dito batte il metallo sotto il castello, la mano stringe la canna da sotto, il palmo scorre sul fondo, il palmo piatto preme la
    /// brina, la coppa versa l'icore; la folgore resta sul caricatore.</summary>
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
        static OffHand.WKey[] Present(float dur, bool one, params OffHand.WKey[] extra) => PresentAt(PresentP(one), PresentR(one), dur, extra);

        static OffHand.WKey[] PresentAt(Vector3 p, Vector3 r, float dur, params OffHand.WKey[] extra)
        {
            var l = new List<OffHand.WKey> { W(0f, Vector3.zero, Vector3.zero), W(0.24f, p, r, 2) };
            l.AddRange(extra);
            l.Add(W(dur - 0.24f, p, r)); l.Add(W(dur, Vector3.zero, Vector3.zero));
            l.Sort((a, b) => a.t.CompareTo(b.t));
            for (int i = 1; i < l.Count - 1; i++) { var k = l[i]; k.pull = 1f; l[i] = k; }
            return l.ToArray();
        }

        /// <summary>Per tarare la posa senza ricostruire (prove): -sigilpose roll,imbardata,beccheggio,x,y,z (gradi e metri; per le armi a
        /// due mani) e -sigilreach x,y,z (il punto di lavoro). Senza argomenti valgono i numeri qui sotto.</summary>
        static float[] CmdNumbers(string name, int n)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] != name) continue;
                var s = a[i + 1].Split(',');
                if (s.Length != n) return null;
                var r = new float[n];
                for (int k = 0; k < n; k++)
                    if (!float.TryParse(s[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r[k])) return null;
                return r;
            }
            return null;
        }

        static readonly float[] poseArgs = CmdNumbers("-sigilpose", 6), reachArgs = CmdNumbers("-sigilreach", 3);

        /// <summary>L'arma offerta per i gesti SOTTO l'arma (i sigilli, 2026-11-17): piu' girata sul fianco (la mano lavora sul fondo e il
        /// calore sale sul fianco sinistro, che resta rivolto a chi guarda) e piu' in alto, cosi' il fondo, la mano e la salita del calore
        /// stanno nel quadro.</summary>
        static Vector3 UnderP(bool one) => poseArgs != null ? new Vector3(poseArgs[3], poseArgs[4], poseArgs[5]) : one ? new Vector3(-0.10f, 0.13f, -0.04f) : new Vector3(-0.12f, 0.16f, -0.10f);
        static Vector3 UnderR(bool one) => poseArgs != null ? new Vector3(poseArgs[2], poseArgs[1], poseArgs[0]) : one ? new Vector3(6f, -14f, -36f) : new Vector3(-12f, -30f, -44f);
        static readonly Vector3 UnderReach = reachArgs != null ? new Vector3(reachArgs[0], reachArgs[1], reachArgs[2]) : new Vector3(0.03f, 0.12f, 0.03f);

        static OffHand.WKey[] PresentUnder(float dur, bool one, params OffHand.WKey[] extra) => PresentAt(UnderP(one), UnderR(one), dur, extra);

        static OffHand.Ev E(float t, string id) => new OffHand.Ev { t = t, id = id };

        static OffHand.Anim Make(string name, float dur, List<K> keys, OffHand.WKey[] w, params OffHand.Ev[] ev)
        {
            var evs = new List<OffHand.Ev>(ev);
            evs.Sort((a, b) => a.t.CompareTo(b.t));
            return new OffHand.Anim { name = name, duration = dur, keys = keys.ToArray(), weapon = w, events = evs.ToArray() };
        }

        static HandPose Tight(HandPose p, float k) => HandPose.Lerp(p, HandPoses.Fist, k);

        /// <summary>Il palmo in su SOTTO l'arma (2026-11-17: i sigilli scaldano l'arma dal basso, la mano lavora sul suo fondo): 'along'
        /// 0..1 lungo il sotto davanti al caricatore (Pt.Under), 'dy' metri sotto la superficie (0,010-0,014 a contatto, 0,05 staccata),
        /// dita avanti e un poco a destra, dorso in giu'. Con le armi a una mano la sinistra arriva dal basso, fuori campo.</summary>
        static readonly Vector3 UnderFingers = new Vector3(0.25f, 0.25f, 0.93f);
        static K Un(float t, float along, float dy, HandPose pose, int ease = 0, float tremor = 0f, Ct c = Ct.Palm) =>
            On(t, Pt.Under, new Vector3(-0.01f, -dy, 0f), UnderFingers, Down, pose, c, ease, tremor, along);

        /// <summary>I gesti sotto l'arma vogliono il punto di lavoro piu' in alto (come il caricatore): la mano resta in vista.</summary>
        static OffHand.Anim Underneath(OffHand.Anim a)
        {
            a.reachOffset = UnderReach;
            return a;
        }

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

        /// <summary>Danni Critici: l'indice sotto l'arma colpisce tre volte il metallo, sempre piu' avanti (castello, mezzo, canna): a ogni
        /// colpo una crepa rossa si accende sotto la punta e sale sul fianco; poi le crepe si allargano su tutta l'arma e battono.</summary>
        static OffHand.Anim Critici(bool one)
        {
            const float D = 1.62f;
            var k = Open(one, 0.10f);
            float[] at = { 0.04f, 0.46f, 0.88f };
            k.Add(Un(0.28f, at[0], 0.050f, HandPoses.Point, 2, 0f, Ct.Index));
            var ev = new List<OffHand.Ev>();
            for (int s = 0; s < 3; s++)
            {
                float t = 0.40f + s * 0.30f;
                if (s > 0) k.Add(Un(t - 0.10f, at[s], 0.045f, HandPoses.Point, 2, 0.2f, Ct.Index));
                k.Add(Un(t, at[s], 0.006f, HandPoses.Point, 1, 0f, Ct.Index));            // il colpo: il polpastrello tocca il metallo
                k.Add(Un(t + 0.07f, at[s], 0.040f, HandPoses.Point, 2, 0f, Ct.Index));
                ev.Add(E(t, "stab"));
            }
            k.Add(Un(1.20f, 0.90f, 0.085f, HandPoses.Relaxed, 2, 0.4f, Ct.Index));
            Close(k, one, D);
            ev.Add(E(1.10f, "cast"));
            return Underneath(Make("Danni Critici", D, k, PresentUnder(D, one), ev.ToArray()));
        }

        /// <summary>Sanguinamento: la mano afferra la canna da sotto, le dita chiuse attorno al metallo, e la strizza scorrendo all'indietro
        /// dalla bocca verso il castello: il sangue esce dal metallo dove passa e sale sui fianchi (rosso scuro, lucido, che cola dal fondo);
        /// sotto il castello la mano si apre a coppa e raccoglie le gocce, poi si scuote.</summary>
        static OffHand.Anim Emorragia(bool one)
        {
            const float D = 1.74f;
            var k = Open(one, 0.10f);
            k.Add(Un(0.30f, 0.85f, 0.050f, HandPoses.Open, 2));
            k.Add(Un(0.42f, 0.85f, 0.010f, HandPoses.Grip, 1, 0.5f));
            k.Add(Un(0.66f, 0.55f, 0.010f, Tight(HandPoses.Grip, 0.2f), 3, 1.6f));
            k.Add(Un(0.90f, 0.22f, 0.010f, Tight(HandPoses.Grip, 0.3f), 3, 1.8f));
            k.Add(Un(1.06f, 0.05f, 0.040f, HandPoses.Cup, 2, 0.4f));
            k.Add(Un(1.24f, 0.05f, 0.040f, HandPoses.Open, 0, 1.4f));
            k.Add(Un(1.39f, 0.05f, 0.085f, HandPoses.Relaxed, 2));
            Close(k, one, D);
            return Underneath(Make("Sanguinamento", D, k, PresentUnder(D, one), E(0.42f, "grab"), E(0.48f, "cut"), E(1.08f, "drip"), E(1.26f, "cast")));
        }

        /// <summary>Incendiario: sotto l'arma la mano schiocca le dita e il fuoco le nasce nel palmo (in su, verso il metallo); il palmo
        /// piatto sale a toccare il fondo del castello e scorre sotto la canna fino alla bocca: dove passa il metallo si arroventa dal
        /// basso, il calore sale sui fianchi in lingue; in fondo la mano si stacca e scuote via il fuoco.</summary>
        static OffHand.Anim Incendio(bool one)
        {
            const float D = 1.70f;
            var k = Open(one, 0.10f);
            k.Add(Un(0.28f, 0.05f, 0.055f, HandPoses.SnapReady, 2));
            k.Add(Un(0.36f, 0.05f, 0.052f, HandPoses.SnapReady, 0, 1f));
            k.Add(Un(0.40f, 0.05f, 0.050f, HandPoses.Snapped, 1));
            k.Add(Un(0.52f, 0.05f, 0.012f, HandPoses.Flat, 2));
            k.Add(Un(0.76f, 0.40f, 0.012f, HandPoses.Flat, 3, 0.3f));
            k.Add(Un(1.00f, 0.80f, 0.012f, HandPoses.Flat, 3, 0.3f));
            k.Add(Un(1.10f, 1.00f, 0.014f, HandPoses.Flat, 3, 0.2f));
            k.Add(Un(1.22f, 1.00f, 0.075f, HandPoses.Open, 2, 0.6f));
            k.Add(Un(1.32f, 0.95f, 0.095f, HandPoses.Relaxed, 0, 0.6f));
            Close(k, one, D);
            return Underneath(Make("Incendiario", D, k, PresentUnder(D, one), E(0.40f, "ignite"), E(0.52f, "trail"), E(1.10f, "cast")));
        }

        /// <summary>Corrosivo: la mano a coppa, palmo in su, sale sotto il castello e l'icore verde vi sgorga; la coppa tocca il metallo e
        /// l'acido risale sui fianchi (fanghiglia che ribolle, gocce che si staccano dal fondo); scivola lenta sotto la canna e si stacca
        /// scrollando le ultime gocce.</summary>
        static OffHand.Anim Corrosione(bool one)
        {
            const float D = 1.72f;
            var k = Open(one, 0.10f);
            k.Add(Un(0.32f, 0.12f, 0.060f, HandPoses.Cup, 2, 0.3f));
            k.Add(Un(0.52f, 0.12f, 0.030f, Tight(HandPoses.Cup, 0.08f), 0, 0.6f));
            k.Add(Un(0.70f, 0.14f, 0.016f, HandPoses.Cup, 0, 0.4f));
            k.Add(Un(1.00f, 0.55f, 0.016f, HandPoses.Cup, 3, 0.4f));
            k.Add(Un(1.14f, 0.80f, 0.020f, HandPoses.Cup, 3, 0.3f));
            k.Add(Un(1.26f, 0.85f, 0.085f, HandPoses.Open, 2));
            k.Add(Un(1.36f, 0.80f, 0.100f, HandPoses.Relaxed, 0, 1f));
            Close(k, one, D);
            return Underneath(Make("Corrosivo", D, k, PresentUnder(D, one), E(0.32f, "ichor"), E(0.70f, "pour"), E(1.14f, "cast")));
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

        /// <summary>Gelido: il palmo piatto sale sotto il castello e preme in su contro il metallo: la brina si condensa dal palmo; poi
        /// scorre piano, sempre premuto, fino alla canna e il gelo avanza a lingue verso l'alto; la mano si stacca lasciando il vapore.</summary>
        static OffHand.Anim Brina(bool one)
        {
            const float D = 1.74f;
            var k = Open(one, 0.10f);
            k.Add(Un(0.30f, 0.10f, 0.060f, HandPoses.Flat, 2));
            k.Add(Un(0.46f, 0.10f, 0.008f, HandPoses.Flat, 2, 0.4f));
            k.Add(Un(0.80f, 0.45f, 0.008f, HandPoses.Flat, 3, 0.7f));
            k.Add(Un(1.14f, 0.90f, 0.008f, HandPoses.Flat, 3, 0.7f));
            k.Add(Un(1.34f, 0.92f, 0.075f, HandPoses.Relaxed, 2));
            Close(k, one, D);
            return Underneath(Make("Gelido", D, k, PresentUnder(D, one), E(0.46f, "press"), E(1.14f, "cast")));
        }

        /// <summary>Arma Maledetta: la mano afferra la canna da sotto, a meta', e la stringe; l'arma sussulta e trema mentre il fuoco scuro
        /// sale dal basso e le rune si accendono; la mano viene respinta indietro aperta, fumante, e l'arma resta avvolta dall'aura.</summary>
        static OffHand.Anim Maledetta(bool one)
        {
            const float D = 2.22f;
            var k = Open(one, 0.10f);
            k.Add(Un(0.34f, 0.50f, 0.060f, HandPoses.Open, 2));
            k.Add(Un(0.48f, 0.50f, 0.010f, Tight(HandPoses.Grip, 0.35f), 1, 1f));
            k.Add(Un(1.40f, 0.50f, 0.010f, Tight(HandPoses.Grip, 0.45f), 0, 5f));
            k.Add(Un(1.50f, 0.50f, 0.095f, HandPoses.Open, 1, 2.5f));
            k.Add(Un(1.72f, 0.45f, 0.085f, HandPoses.Claw, 0, 1.6f));
            Close(k, one, D);
            // l'arma sussulta mentre le rune la prendono
            var shake = new List<OffHand.WKey>();
            Vector3 p = UnderP(one), r = UnderR(one);
            for (int i = 0; i < 9; i++)
            {
                float t = 0.52f + i * 0.1f;
                float s = (i % 2 == 0 ? 1f : -1f) * (1f - i / 10f);
                shake.Add(W(t, p + new Vector3(0.004f * s, 0.003f * s, 0f), r + new Vector3(1.4f * s, 0.8f * s, 2.2f * s), 0));
            }
            shake.Add(W(1.5f, p + new Vector3(0f, 0f, 0.012f), r + new Vector3(-3f, 0f, 0f), 1));
            return Underneath(Make("Arma Maledetta", D, k, PresentUnder(D, one, shake.ToArray()), E(0.48f, "grab"), E(0.55f, "runes"), E(1.48f, "cast"), E(1.50f, "burn")));
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
