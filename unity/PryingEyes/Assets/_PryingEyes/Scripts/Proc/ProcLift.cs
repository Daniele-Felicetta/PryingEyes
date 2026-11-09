using System;
using System.Collections;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Ascensore di un piano: due ante che scorrono dentro il muro, lampada della cabina che sfarfalla, freccia
    /// sopra la porta. L'ascensore di arrivo si apre da solo e poi si richiude; quello di uscita resta sigillato finche'
    /// le candele non sono tutte accese, poi con F si scende al piano dopo.</summary>
    public class ProcLift : MonoBehaviour, IInteract
    {
        public Transform doorL, doorR;
        public Vector3 slideDir = Vector3.right;
        public float travel = 0.62f;
        public Light cabinLight;
        public PoolLight cabinPool;
        public Renderer indicator;
        public bool isExit;
        public bool unlocked;
        public Vector3 spawnPoint;        // dentro la cabina
        public Vector3 lookOut;           // direzione in cui guarda chi esce
        public Vector3 doorPoint;         // la soglia
        public Vector3 frontPoint;        // davanti alla porta, dal lato della stanza
        public Func<string> lockedText;
        public Action onEnter;

        Vector3 lClosed, rClosed;
        Material indMat;
        float lightBase = 1.6f;
        bool busy, isOpen;
        public bool IsOpen => isOpen;
        public bool Busy => busy;

        // IInteract: solo l'uscita
        public Vector3 Point => frontPoint + Vector3.up * 1.3f;
        public float Radius => 3.4f;
        public string Prompt => !isExit ? null : (unlocked ? (isOpen ? "SCENDI CON L'ASCENSORE" : null) : (lockedText != null ? lockedText() : "L'ASCENSORE E' SIGILLATO"));

        public void Use()
        {
            if (!isExit) return;
            if (!unlocked) { GameManager.I?.hud?.Toast("Le porte non si muovono. Qualcuno deve accendere le candele."); return; }
            onEnter?.Invoke();
        }

        void Start()
        {
            if (doorL != null) lClosed = doorL.position;
            if (doorR != null) rClosed = doorR.position;
            if (indicator != null)
            {
                indMat = new Material(indicator.sharedMaterial);
                indicator.sharedMaterial = indMat;
                indMat.SetColor("_EmissionColor", Color.black);
            }
        }

        void OnDestroy() { Interact.Remove(this); }

        public void SetOpenInstant(bool open)
        {
            if (doorL == null) return;
            if (lClosed == Vector3.zero && rClosed == Vector3.zero) { lClosed = doorL.position; rClosed = doorR.position; }
            isOpen = open;
            Slide(open ? 1f : 0f);
            if (cabinPool != null) cabinPool.wanted = open;
        }

        /// <summary>Apre con "ding", rombo delle ante e luce che si accende; onOpen parte a ante socchiuse.</summary>
        public void Open(Action onHalf = null)
        {
            if (busy || isOpen) { onHalf?.Invoke(); return; }
            StartCoroutine(OpenCo(onHalf));
        }

        public void Close()
        {
            if (busy || !isOpen) return;
            StartCoroutine(CloseCo());
        }

        IEnumerator OpenCo(Action onHalf)
        {
            busy = true;
            if (indMat != null) indMat.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.12f) * 4f);
            Sfx.At(SoundBank.Ding, doorPoint + Vector3.up * 1.4f, 0.75f, 1f, 3f, 45f);
            yield return new WaitForSeconds(0.7f);
            if (cabinPool != null) cabinPool.wanted = true; else if (cabinLight != null) cabinLight.enabled = true;
            Sfx.At(SoundBank.Doors, doorPoint, 0.6f, 1f, 2.5f, 30f);
            bool half = false;
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 1.3f);
                Slide(k);
                Buzz();
                if (!half && k > 0.45f) { half = true; onHalf?.Invoke(); }
                yield return null;
            }
            Slide(1f);
            if (!half) onHalf?.Invoke();
            isOpen = true;
            busy = false;
        }

        IEnumerator CloseCo()
        {
            busy = true;
            Sfx.At(SoundBank.Doors, doorPoint, 0.5f, 0.94f, 2.5f, 30f);
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                Slide(1f - Mathf.SmoothStep(0f, 1f, t / 1.3f));
                yield return null;
            }
            Slide(0f);
            isOpen = false;
            if (cabinPool != null) cabinPool.wanted = false; else if (cabinLight != null) cabinLight.enabled = false;
            if (indMat != null) indMat.SetColor("_EmissionColor", Color.black);
            busy = false;
        }

        void Update() { if (isOpen && cabinLight != null) Buzz(); }

        void Slide(float k)
        {
            if (doorL == null || doorR == null) return;
            doorL.position = lClosed - slideDir * (travel * k);
            doorR.position = rClosed + slideDir * (travel * k);
        }

        /// <summary>Neon stanco: ronzio di luce con qualche calo improvviso.</summary>
        void Buzz()
        {
            if (cabinLight == null) return;
            float n = Mathf.PerlinNoise(Time.time * 9f, 0.7f);
            cabinLight.intensity = lightBase * (UnityEngine.Random.value < 0.03f ? 0.15f : 0.8f + 0.25f * n);
        }

        // ------------------------------------------------------------------ costruzione
        /// <summary>Monta le ante, la luce e gli accessori nella cabina 'cab', aperta nel muro della stanza 'host' dall'apertura 'o'.</summary>
        public static ProcLift Build(FloorInstance fi, Room cab, Room host, Opening o, Transform parent, bool isExit)
        {
            bool ax = o.alongX;
            Vector3 e = ax ? Vector3.right : Vector3.forward;
            // da host verso cabina
            Vector3 n = ax ? (host.z1 == o.line ? Vector3.forward : Vector3.back) : (host.x1 == o.line ? Vector3.right : Vector3.left);
            float line = o.line * 0.1f, c = o.center * 0.1f, hw = o.width * 0.05f;
            Vector3 At(float s, float y, float d) => ax ? new Vector3(s, y, line + d * n.z) : new Vector3(line + d * n.x, y, s);

            var root = new GameObject(isExit ? "AscensoreUscita" : "AscensoreArrivo");
            root.transform.SetParent(parent, false);
            var lift = root.AddComponent<ProcLift>();
            lift.isExit = isExit;
            lift.slideDir = e;
            lift.travel = hw * 1.04f;

            var steel = ProcKit.Mat("acciaio");
            Transform Leaf(string nm, float s)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = nm;
                g.layer = HotelNav.DoorLayer;
                g.transform.SetParent(root.transform, false);
                g.transform.position = At(s, 1.2f, 0f);
                g.transform.localScale = ax ? new Vector3(hw * 0.99f, 2.4f, 0.06f) : new Vector3(0.06f, 2.4f, hw * 0.99f);
                g.GetComponent<MeshRenderer>().sharedMaterial = steel;
                return g.transform;
            }
            lift.doorL = Leaf("AntaA", c - hw * 0.5f);
            lift.doorR = Leaf("AntaB", c + hw * 0.5f);
            lift.lClosed = lift.doorL.position;
            lift.rClosed = lift.doorR.position;

            // cabina: centro e punto di partenza
            Vector3 center = new Vector3(cab.CenterX, 0f, cab.CenterZ);
            lift.spawnPoint = center + Vector3.up * 0.06f;
            lift.lookOut = -n;
            lift.doorPoint = At(c, 0f, 0f);
            lift.frontPoint = At(c, 0f, -1.1f);

            // accessori: corrimano sul fondo, pannello di luce al soffitto, freccia sopra la porta
            var brass = ProcKit.Mat("ottone");
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Util.Kill(rail.GetComponent<Collider>());
            rail.name = "Corrimano";
            rail.transform.SetParent(root.transform, false);
            rail.transform.position = center + n * 1.0f + Vector3.up * 0.95f;
            rail.transform.localScale = ax ? new Vector3(1.7f, 0.05f, 0.05f) : new Vector3(0.05f, 0.05f, 1.7f);
            rail.GetComponent<MeshRenderer>().sharedMaterial = brass;

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Util.Kill(panel.GetComponent<Collider>());
            panel.name = "PannelloLuce";
            panel.transform.SetParent(root.transform, false);
            panel.transform.position = center + Vector3.up * (cab.wallH - 0.02f);
            panel.transform.localScale = new Vector3(0.8f, 0.02f, 0.8f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = ProcKit.Mat("pannello_luce");
            panel.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var ind = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Util.Kill(ind.GetComponent<Collider>());
            ind.name = "Freccia";
            ind.transform.SetParent(root.transform, false);
            ind.transform.position = At(c, o.top + 0.28f, -TH() - 0.02f);
            ind.transform.localScale = ax ? new Vector3(0.34f, 0.14f, 0.03f) : new Vector3(0.03f, 0.14f, 0.34f);
            ind.GetComponent<MeshRenderer>().sharedMaterial = ProcKit.Mat("indicatore");
            ind.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lift.indicator = ind.GetComponent<MeshRenderer>();

            var lg = new GameObject("LuceCabina");
            lg.transform.SetParent(root.transform, false);
            lg.transform.position = center + Vector3.up * (cab.wallH - 0.35f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.93f, 0.78f);
            l.intensity = lift.lightBase;
            l.range = 5.5f;
            lift.cabinLight = l;
            lift.cabinPool = LightPool.Register(l, true, false);
            fi.lights.Add(l);

            if (isExit) Interact.Add(lift);
            return lift;
        }

        static float TH() => FloorBuilder.TH;
    }
}
