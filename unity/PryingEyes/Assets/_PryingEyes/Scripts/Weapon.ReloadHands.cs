using Production.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    // La ricarica fatta dalla mano sinistra (2026-11-16, presa realistica). La coreografia e' in ReloadAnims (chiavi della mano libera
    // sull'arma e sulle sue parti, legate al tempo della ricarica di WeaponState); qui si avvia e si tiene al passo, e la mano porta
    // le cose: il caricatore si attacca al palmo quando la mano lo prende (nel riferimento della posa d'inserimento, cosi' torna
    // esattamente al suo posto) e nel pozzetto scorre lungo il suo asse; a vuoto ne cade una copia con la fisica (DropMag) e quello
    // vero ricompare in mano quando torna dalla cintura; cartucce e caricatore rapido sono piccoli solidi tenuti tra le dita.
    public partial class Weapon
    {
        bool reloadAnim;           // la ricarica in corso la fa la mano sinistra
        int magCarry;              // 0 niente, 1 da agganciare al palmo, 2 nella mano
        Matrix4x4 magRel;          // il caricatore nel riferimento dell'osso del palmo
        bool pendingDrop;
        GameObject handProp;       // cartucce o caricatore rapido nella mano
        Matrix4x4 propRel;

        /// <summary>Con -ricaricavecchia le ricariche di prima (l'arma si inclina e la mano scivola sul caricatore), per il confronto.</summary>
        static bool? oldReload;
        static bool OldReload => oldReload ??= System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ricaricavecchia") >= 0;

        /// <summary>Dopo State.BeginReload: la coreografia della ricarica per la mano libera; falso se resta quella di prima.</summary>
        bool StartReloadAnim(WeaponDef d, ReloadTiming timing)
        {
            reloadAnim = false; magCarry = 0; pendingDrop = false;
            ClearProp();
            if (offHand == null || rig == null || !RealHold(d) || offHand.Busy || OldReload) return false;
            int rounds = Mathf.Min(magSize - mag, reserve);
            var a = ReloadAnims.Build(d, offHand, rig, reloadEmpty, State.ReloadDuration, rounds, timing.Opening, timing.RoundInterval, timing.InsertFraction);
            if (a == null) return false;
            reloadAnim = offHand.PlayExternal(a, OnReloadEvent);
            return reloadAnim;
        }

        /// <summary>Prima di OffHand.Tick: la mano al punto della ricarica; finita la ricarica, alla fine dell'animazione.</summary>
        void ReloadSeek()
        {
            if (!reloadAnim) return;
            if (offHand == null || !offHand.External) { reloadAnim = false; magCarry = 0; ClearProp(); return; }
            offHand.Seek(reloading ? State.ReloadElapsed : offHand.CurrentAnim.duration);
        }

        /// <summary>Ricarica interrotta: la mano torna sull'astina (OffHand.Cancel) e si lascia quello che portava.</summary>
        void EndReloadAnim()
        {
            if (reloadAnim && offHand != null && offHand.External) offHand.Cancel();
            reloadAnim = false; magCarry = 0; pendingDrop = false;
            ClearProp();
        }

        void OnReloadEvent(string id)
        {
            var d = Def;
            switch (id)
            {
                case "take": magCarry = 1; Synth.Mech("magout", 0.5f, 1f); break;
                case "drop": pendingDrop = true; if (d.style == ReloadStyle.Pistol) Synth.Mech("magout", 0.5f, 1.08f); break;
                case "fetch": magCarry = 1; SetMagVisible(true); break;
                case "seat": magCarry = 0; break;
                case "slap": Synth.Mech("magin", 0.28f, 1.3f); break;
                case "charge": Synth.Mech("boltback", 0.45f, 1.05f); break;
                case "slide": Synth.Mech("boltback", 0.40f, 1.3f); break;
                case "cover": Synth.Mech("cylopen", 0.40f, 0.75f); break;
                case "shell": AttachProp(1); break;
                case "shell2": AttachProp(2); break;
                case "loader": AttachProp(6); break;
                case "shellin": case "loaderin": ClearProp(); break;
                case "casings": EjectFrom(d, 2); break;
                case "casings6": EjectFrom(d, 6); break;
                case "fine": case "annullata":
                    reloadAnim = false; magCarry = 0; pendingDrop = false;
                    ClearProp();
                    MagVisibleAgain();
                    break;
            }
        }

        /// <summary>Dopo armL.Solve: il caricatore (e quello che la mano porta) segue il palmo dov'e' davvero arrivato.</summary>
        void ReloadPostSolve()
        {
            if (!reloadAnim || offHand == null || rig == null) return;
            var a = offHand.CurrentAnim;
            if (a == null) return;
            Transform palm = offHand.PalmBone;
            Matrix4x4 palmM = Matrix4x4.TRS(palm.position, palm.rotation, Vector3.one);
            if (rig.mag != null && magCarry > 0)
            {
                // rig.mag qui e' al suo posto (WeaponRig.Reset in AnimateParts)
                if (magCarry == 1)
                {
                    Matrix4x4 key = offHand.KeyPalmWorld(a.seatKey, out Vector3 kp, out Quaternion kq) ? Matrix4x4.TRS(kp, kq, Vector3.one) : palmM;
                    magRel = key.inverse * Matrix4x4.TRS(rig.mag.position, rig.mag.rotation, Vector3.one);
                    magCarry = 2;
                }
                var m = palmM * magRel;
                Vector3 p = m.GetColumn(3);
                Quaternion q = m.rotation;
                if (a.insert >= 0f && offHand.Clock >= a.insert)
                {
                    // nel pozzetto il caricatore scorre lungo il suo asse fino al suo posto, e si raddrizza entrando
                    Vector3 rest = rig.mag.position;
                    Quaternion rq = rig.mag.rotation;
                    Vector3 ax = viewRoot.TransformDirection(a.magAxis).normalized;
                    float o = Mathf.Max(0f, Vector3.Dot(p - rest, ax));
                    q = Quaternion.Slerp(rq, q, Mathf.Clamp01(o / 0.06f));
                    p = rest + ax * o;
                }
                rig.mag.SetPositionAndRotation(p, q);
            }
            if (pendingDrop)
            {
                pendingDrop = false;
                DropMag(Def);
                SetMagVisible(false);
                magCarry = 0;
            }
            if (handProp != null)
            {
                var m = palmM * propRel;
                handProp.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            }
        }

        /// <summary>Quello che la mano porta: 1 una cartuccia, 2 due cartucce affiancate, 6 il caricatore rapido del revolver.
        /// Nel riferimento del palmo: z verso le dita, y il dorso, x il pollice (la mano libera e' una sinistra).</summary>
        void AttachProp(int kind)
        {
            ClearProp();
            if (offHand == null || viewRoot == null) return;
            var root = new GameObject("NellaMano");
            root.layer = Seats.ViewLayer;
            root.transform.SetParent(viewRoot, false);
            Material shell = Fx.ShellPlastic, brass = Fx.Brass;
            Material dark = rig != null && rig.renderers != null && rig.renderers.Length > 0 && rig.renderers[0] != null ? rig.renderers[0].sharedMaterial : brass;
            void Cyl(Vector3 pos, float r, float h, Material m)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(c.GetComponent<Collider>());
                c.layer = Seats.ViewLayer;
                c.transform.SetParent(root.transform, false);
                c.transform.localPosition = pos;
                c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);       // l'asse del cilindro lungo z
                c.transform.localScale = new Vector3(r * 2f, h * 0.5f, r * 2f);
                var mr = c.GetComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.renderingLayerMask = ViewLayer;
            }
            if (kind == 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    float an = i * Mathf.PI / 3f;
                    Cyl(new Vector3(Mathf.Cos(an) * 0.0108f, Mathf.Sin(an) * 0.0108f, 0.016f), 0.0047f, 0.032f, brass);
                }
                Cyl(new Vector3(0f, 0f, -0.004f), 0.017f, 0.010f, dark);
                Cyl(new Vector3(0f, 0f, -0.016f), 0.007f, 0.016f, dark);
                propRel = Matrix4x4.TRS(new Vector3(0.004f, -0.042f, 0.058f), Quaternion.identity, Vector3.one);
            }
            else
            {
                int n = kind == 2 ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    float x = (i - (n - 1) * 0.5f) * 0.0208f;
                    Cyl(new Vector3(x, 0f, 0.0065f), 0.0102f, 0.057f, shell);
                    Cyl(new Vector3(x, 0f, -0.0285f), 0.0106f, 0.014f, brass);
                }
                propRel = Matrix4x4.TRS(new Vector3(0.014f, -0.026f, 0.070f), Quaternion.identity, Vector3.one);
            }
            handProp = root;
        }

        void ClearProp()
        {
            if (handProp != null) Destroy(handProp);
            handProp = null;
        }

        /// <summary>I bossoli che escono dalle camere aperte (doppietta, tamburo): cadono indietro e in basso.</summary>
        void EjectFrom(WeaponDef d, int n)
        {
            if (cam == null || viewRoot == null) return;
            Transform src = rig != null ? (rig.barrels != null ? rig.barrels : rig.cyl != null ? rig.cyl : rig.eject) : null;
            Vector3 from = src != null ? src.position : viewRoot.position;
            string kind = n == 2 ? "shell" : (d.casing == "none" ? "pistol" : d.casing);
            for (int i = 0; i < n; i++)
            {
                Vector3 vel = -cam.forward * Random.Range(0.3f, 0.8f) - cam.up * Random.Range(0.3f, 0.7f) + cam.right * Random.Range(-0.3f, 0.3f);
                Fx.Casing(from + Random.insideUnitSphere * 0.012f, vel, cam.rotation * Quaternion.Euler(Random.Range(60f, 120f), 0f, Random.Range(-20f, 20f)), kind);
            }
        }
    }
}
