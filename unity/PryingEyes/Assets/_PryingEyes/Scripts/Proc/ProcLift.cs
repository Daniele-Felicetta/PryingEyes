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
        /// <summary>Catacombe (BuildGate): il cancello di ferro che sale al posto delle ante, e cosa dice quando e' aperto.</summary>
        public Transform grate;
        Vector3 grateClosed;
        public Func<string> openText;
        public string lockedToast = "Le porte non si muovono. Qualcuno deve accendere le candele.";
        bool fire;                        // la luce e' una torcia (fiamma che guizza) e non il neon della cabina

        Vector3 lClosed, rClosed;
        Material indMat;
        float lightBase = 1.6f;
        bool busy, isOpen;
        public bool IsOpen => isOpen;
        public bool Busy => busy;

        // IInteract: solo l'uscita
        public Vector3 Point => frontPoint + Vector3.up * 1.3f;
        public float Radius => 3.4f;
        public string Prompt => !isExit ? null : (unlocked ? (isOpen ? (openText != null ? openText() : "SCENDI CON L'ASCENSORE") : null) : (lockedText != null ? lockedText() : "L'ASCENSORE È SIGILLATO"));

        public void Use()
        {
            if (!isExit) return;
            if (!unlocked) { GameManager.I?.hud?.Toast(lockedToast); return; }
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
            if (grate != null) { isOpen = open; Slide(open ? 1f : 0f); if (cabinPool != null) cabinPool.wanted = true; return; }
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
            if (grate != null)
            {
                // il cancello: un colpo di ferro, poi la catena che lo tira su (niente campanello d'ascensore)
                Sfx.At(SoundBank.Clang, doorPoint + Vector3.up * 1.2f, 0.7f, 0.82f, 3f, 40f);
                yield return new WaitForSeconds(0.35f);
                Sfx.At(SoundBank.StoneGrind, doorPoint + Vector3.up * 1.2f, 0.55f, 1.15f, 3f, 34f);
            }
            else
            {
                Sfx.At(SoundBank.Ding, doorPoint + Vector3.up * 1.4f, 0.75f, 1f, 3f, 45f);
                yield return new WaitForSeconds(0.7f);
            }
            if (cabinPool != null) cabinPool.wanted = true; else if (cabinLight != null) cabinLight.enabled = true;
            if (grate == null) Sfx.At(SoundBank.Doors, doorPoint, 0.6f, 1f, 2.5f, 30f);
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
            if (grate != null) Sfx.At(SoundBank.StoneGrind, doorPoint + Vector3.up * 1.2f, 0.5f, 1.05f, 3f, 30f);
            else Sfx.At(SoundBank.Doors, doorPoint, 0.5f, 0.94f, 2.5f, 30f);
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                Slide(1f - Mathf.SmoothStep(0f, 1f, t / 1.3f));
                yield return null;
            }
            Slide(0f);
            isOpen = false;
            if (grate != null) Sfx.At(SoundBank.Clang, doorPoint + Vector3.up * 0.3f, 0.6f, 0.75f, 3f, 34f);     // la torcia resta accesa
            else if (cabinPool != null) cabinPool.wanted = false; else if (cabinLight != null) cabinLight.enabled = false;
            if (indMat != null) indMat.SetColor("_EmissionColor", Color.black);
            busy = false;
        }

        void Update() { if ((isOpen || fire) && cabinLight != null) Buzz(); }

        void Slide(float k)
        {
            if (grate != null) { grate.position = grateClosed + Vector3.up * (travel * k); return; }
            if (doorL == null || doorR == null) return;
            doorL.position = lClosed - slideDir * (travel * k);
            doorR.position = rClosed + slideDir * (travel * k);
        }

        /// <summary>Neon stanco: ronzio di luce con qualche calo improvviso.</summary>
        void Buzz()
        {
            if (cabinLight == null) return;
            if (fire) { cabinLight.intensity = lightBase * (0.82f + 0.22f * Mathf.PerlinNoise(Time.time * 6.5f, 0.3f) + 0.06f * Mathf.Sin(Time.time * 23f)); return; }
            float n = Mathf.PerlinNoise(Time.time * 9f, 0.7f);
            cabinLight.intensity = lightBase * (UnityEngine.Random.value < 0.03f ? 0.15f : 0.8f + 0.25f * n);
        }

        // ------------------------------------------------------------------ costruzione
        /// <summary>Monta le ante, la luce e gli accessori nella cabina 'cab', aperta nel muro della stanza 'host' dall'apertura 'o'.</summary>
        public static ProcLift Build(FloorInstance fi, Room cab, Room host, Opening o, Transform parent, bool isExit)
        {
            if (fi.plan.catacombe) return BuildGate(fi, cab, host, o, parent, isExit);
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

        // ------------------------------------------------------------------ catacombe
        /// <summary>Catacombe: la cabina e' il pianerottolo di una scala di pietra chiuso da un cancello di ferro che sale (la saracinesca di una
        /// cripta). Quello d'arrivo ha i gradini che salgono verso il buio (la scala da cui sei sceso); quello d'uscita il pozzo nero dove la
        /// scala continua verso il livello sotto, con la sua sponda di pietra e il corrimano. Una torcia a muro al posto del neon, una lanterna
        /// di ferro sopra l'arco al posto della freccia. Stessi punti (nascita, soglia, davanti) e stesso Open/Close degli ascensori.</summary>
        static ProcLift BuildGate(FloorInstance fi, Room cab, Room host, Opening o, Transform parent, bool isExit)
        {
            ShrineKit.Mats();
            bool ax = o.alongX;
            Vector3 e = ax ? Vector3.right : Vector3.forward;
            Vector3 n = ax ? (host.z1 == o.line ? Vector3.forward : Vector3.back) : (host.x1 == o.line ? Vector3.right : Vector3.left);
            float line = o.line * 0.1f, c = o.center * 0.1f, hw = o.width * 0.05f;
            Vector3 At(float s, float y, float d) => ax ? new Vector3(s, y, line + d * n.z) : new Vector3(line + d * n.x, y, s);
            float depth = (ax ? cab.D : cab.W) * 0.1f - 0.3f;            // dalla soglia al muro di fondo, dentro i muri
            float width = (ax ? cab.W : cab.D) * 0.1f - 0.36f;
            float mid = (ax ? (cab.x0 + cab.x1) : (cab.z0 + cab.z1)) * 0.05f;   // il centro della cabina lungo il muro

            var root = new GameObject(isExit ? "CancelloUscita" : "ScalaArrivo");
            root.transform.SetParent(parent, false);
            var lift = root.AddComponent<ProcLift>();
            lift.isExit = isExit;
            lift.fire = true;
            lift.travel = 2.55f;
            lift.lockedToast = "Il cancello non si muove. Prima le ondate.";

            // il cancello: sbarre verticali con le punte in basso, tre traverse, un solo collider per tutto (strato delle porte: la navmesh non lo vede)
            var grate = new GameObject("Cancello");
            grate.layer = HotelNav.DoorLayer;
            grate.transform.SetParent(root.transform, false);
            grate.transform.position = At(c, 0f, 0f);
            grate.transform.rotation = Quaternion.LookRotation(n, Vector3.up);
            var box = grate.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.2f, 0f);
            box.size = new Vector3(hw * 2f, 2.4f, 0.12f);
            int bars = Mathf.Max(5, Mathf.RoundToInt(hw * 2f / 0.13f));
            for (int i = 0; i <= bars; i++)
            {
                float x = -hw + 0.04f + (hw * 2f - 0.08f) * i / bars;
                ShrineKit.Prim(grate.transform, PrimitiveType.Cube, "Sbarra", ShrineKit.Iron, new Vector3(x, 1.24f, 0f), new Vector3(0.034f, 2.32f, 0.034f));
                ShrineKit.Prim(grate.transform, PrimitiveType.Cube, "Punta", ShrineKit.Iron, new Vector3(x, 0.07f, 0f), new Vector3(0.05f, 0.05f, 0.05f), new Vector3(0f, 0f, 45f));
            }
            foreach (float y in new[] { 0.3f, 1.25f, 2.2f })
                ShrineKit.Prim(grate.transform, PrimitiveType.Cube, "Traversa", ShrineKit.Iron, new Vector3(0f, y, 0f), new Vector3(hw * 2f, 0.05f, 0.045f));
            foreach (var r in grate.GetComponentsInChildren<Renderer>()) r.gameObject.layer = HotelNav.DoorLayer;
            lift.grate = grate.transform;
            lift.grateClosed = grate.transform.position;

            Vector3 center = new Vector3(cab.CenterX, 0f, cab.CenterZ);
            lift.spawnPoint = center + Vector3.up * 0.06f;
            lift.lookOut = -n;
            lift.doorPoint = At(c, 0f, 0f);
            lift.frontPoint = At(c, 0f, -1.1f);
            var stone = ShrineKit.Stone;
            var props = new GameObject(isExit ? "Pozzo" : "Scala");
            props.transform.SetParent(root.transform, false);
            if (isExit)
            {
                // il pozzo: il fondo nero dove la scala scende, una sponda di pietra davanti e il corrimano che si perde nel buio
                float d0 = depth * 0.42f, d1 = depth;
                var pit = ShrineKit.Prim(props.transform, PrimitiveType.Cube, "Buio", ShrineKit.Dark, At(mid, 0.004f, (d0 + d1) * 0.5f), Vector3.one);
                pit.transform.localScale = ax ? new Vector3(width, 0.006f, d1 - d0) : new Vector3(d1 - d0, 0.006f, width);
                var curb = ShrineKit.Prim(props.transform, PrimitiveType.Cube, "Sponda", stone, At(mid, 0.07f, d0 - 0.06f), Vector3.one, default, true);
                curb.transform.localScale = ax ? new Vector3(width, 0.14f, 0.12f) : new Vector3(0.12f, 0.14f, width);
                for (int k = 0; k < 4; k++)
                {
                    // i primi gradini dentro il buio, sempre piu' scuri: si vede da dove si scende
                    float dd = d0 + 0.1f + k * 0.26f;
                    if (dd > d1 - 0.1f) break;
                    var st = ShrineKit.Prim(props.transform, PrimitiveType.Cube, "Gradino", k < 2 ? ShrineKit.StoneDark : ShrineKit.Dark, At(mid, 0.009f, dd), Vector3.one);
                    st.transform.localScale = ax ? new Vector3(width * (0.92f - k * 0.06f), 0.006f, 0.18f) : new Vector3(0.18f, 0.006f, width * (0.92f - k * 0.06f));
                }
                float side = width * 0.5f - 0.05f;
                foreach (float sgn in new[] { -1f, 1f })
                    for (int k = 0; k < 3; k++)
                    {
                        float dd = d0 + k * (d1 - d0) / 3f, h = 0.95f - k * 0.3f;
                        ShrineKit.Prim(props.transform, PrimitiveType.Cube, "Montante", ShrineKit.Iron, At(mid + sgn * side, h * 0.5f, dd), new Vector3(0.03f, h, 0.03f));
                    }
            }
            else
            {
                // la scala da cui sei sceso: cinque gradini verso il fondo e, in cima, il buio della rampa che continua
                int steps = 5;
                float run = Mathf.Min(0.3f, (depth * 0.5f) / steps), d0 = depth - steps * run;
                for (int k = 0; k < steps; k++)
                {
                    float top = 0.19f * (k + 1);
                    var st = ShrineKit.Prim(props.transform, PrimitiveType.Cube, "Gradino", stone, At(mid, top * 0.5f, d0 + run * (k + 0.5f)), Vector3.one, default, true);
                    st.transform.localScale = ax ? new Vector3(width, top, run) : new Vector3(run, top, width);
                }
                var dark = ShrineKit.Prim(props.transform, PrimitiveType.Cube, "Buio", ShrineKit.Dark, At(mid, 1.75f, depth - 0.01f), Vector3.one);
                dark.transform.localScale = ax ? new Vector3(width * 0.9f, 1.3f, 0.01f) : new Vector3(0.01f, 1.3f, width * 0.9f);
            }

            // la torcia a muro, sul fianco del pianerottolo
            Vector3 torch = At(mid + (width * 0.5f - 0.02f), 1.9f, depth * 0.3f);
            ShrineKit.Prim(props.transform, PrimitiveType.Cube, "Staffa", ShrineKit.Iron, torch + Vector3.down * 0.12f, new Vector3(0.05f, 0.28f, 0.05f));
            var flame = ShrineKit.Prim(props.transform, PrimitiveType.Sphere, "Fiamma", ProcKit.HasMat("fiamma") ? ProcKit.Mat("fiamma") : ShrineKit.EmitOwn(new Color(1f, 0.55f, 0.18f), 3f),
                torch + Vector3.up * 0.06f, new Vector3(0.09f, 0.16f, 0.09f));
            flame.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var lg = new GameObject("Torcia");
            lg.transform.SetParent(root.transform, false);
            lg.transform.position = torch + Vector3.up * 0.15f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.62f, 0.32f);
            lift.lightBase = 1.5f;
            l.intensity = lift.lightBase;
            l.range = 6.5f;
            lift.cabinLight = l;
            lift.cabinPool = LightPool.Register(l, true, false);
            fi.lights.Add(l);

            // sopra l'arco, dal lato della stanza: l'architrave di pietra e la lanterna di ferro che si accende quando il cancello e' aperto
            var lintel = ShrineKit.Prim(root.transform, PrimitiveType.Cube, "Architrave", stone, At(c, o.top + 0.16f, -TH() - 0.05f), Vector3.one);
            lintel.transform.localScale = ax ? new Vector3(hw * 2f + 0.5f, 0.24f, 0.1f) : new Vector3(0.1f, 0.24f, hw * 2f + 0.5f);
            var lantern = ShrineKit.Prim(root.transform, PrimitiveType.Cube, "Lanterna", ShrineKit.Iron, At(c, o.top + 0.5f, -TH() - 0.1f), new Vector3(0.14f, 0.2f, 0.14f));
            var glass = ShrineKit.Prim(lantern.transform, PrimitiveType.Cube, "Vetro", ProcKit.Mat("indicatore"), Vector3.zero, new Vector3(0.8f, 0.7f, 1.06f));
            glass.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lift.indicator = glass.GetComponent<MeshRenderer>();

            if (isExit) Interact.Add(lift);
            return lift;
        }

        static float TH() => FloorBuilder.TH;
    }
}
