using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>La corrente del piano, come nei Black Ops Zombies. Nel ciclo a ondate ogni piano parte con le sole luci d'emergenza:
    /// le lampade elettriche (lampadari, applique, lampade, neon, fari del teatro, luci d'ambiente; non candele, bracieri, luna,
    /// finestre e insegne d'uscita) restano al 40%. Il quadro elettrico (PowerPanel) sta sul muro dell'area a pagamento piu' lontana
    /// dall'arrivo: tirata la leva, la luce torna a ondata partendo dal quadro (28 m/s, ogni lampada con un tremolio) e da allora
    /// funzionano i libri dei perk, le trappole delle soglie e l'altare della Consacrazione (la cassa no: quella va sempre).
    /// Senza quadro (vecchio ciclo, piani senza un muro adatto) la corrente c'e' da subito. In cooperativa la accende l'ospite.</summary>
    public static class Power
    {
        public const float Emergency = 0.4f, Spread = 28f;
        public static bool On { get; private set; } = true;
        public static float OnTime { get; private set; } = -100f;
        public static PowerPanel Panel { get; private set; }
        /// <summary>Quanto ci mette l'ondata ad arrivare alla lampada piu' lontana (poi si rifanno i riflessi delle stanze).</summary>
        public static float CascadeTime { get; private set; }
        static float denyT;

        /// <summary>Piano nuovo: con un quadro si parte con le luci d'emergenza, senza si parte accesi.</summary>
        public static void Reset(PowerPanel panel)
        {
            Panel = panel;
            On = panel == null;
            OnTime = -100f;
            CascadeTime = 0f;
        }

        /// <summary>Accende: dal quadro (slot di chi ha tirato la leva; l'ospite lo dice a tutti) o dalla rete (slot -1).</summary>
        public static void TurnOn(int slot)
        {
            if (On) return;
            On = true;
            OnTime = Time.time;
            Vector3 origin = Panel != null ? Panel.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            CascadeTime = LightPool.PowerCascade(origin, Spread);
            if (Panel != null) Panel.Powered();
            Sfx.At(SoundBank.PowerOn, origin + Vector3.up * 0.3f, 1f, 1f, 6f, 90f);
            Seats.ForHuds(h => h.Banner("La corrente è tornata", "Perk, trappole e altare funzionano."));
            if (slot >= 0) Coop.Game?.PowerChanged();
            var fd = FloorDirector.I;
            if (fd != null)
            {
                fd.StartCoroutine(RoomProbes.Rebake(CascadeTime + 0.6f));   // i riflessi fotografati al buio d'emergenza
                fd.RefreshObjectiveNow();
            }
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] corrente accesa (slot {slot}), ondata di {CascadeTime:F1} s");
        }

        /// <summary>Per perk, trappole e altare: vero se c'e' corrente; senza, avvisa (al piu' una volta al secondo) e torna falso.</summary>
        public static bool Check()
        {
            if (On) return true;
            if (Time.unscaledTime > denyT)
            {
                denyT = Time.unscaledTime + 1f;
                GameManager.I?.hud?.Toast("Serve la corrente: trova il quadro elettrico.");
                Sfx.UI(SoundBank.BookNo, 0.5f);
            }
            return false;
        }

        /// <summary>Davanti alle scritte di perk, trappole e altare finche' manca la corrente.</summary>
        public static string Prefix => On ? "" : "SENZA CORRENTE — ";

        /// <summary>Le prove che usano trappole, perk o altare senza passare dal quadro: corrente piena subito, senza ondata.</summary>
        public static void ForceOn()
        {
            if (On) return;
            On = true;
            OnTime = Time.time - 100f;
            if (Panel != null) Panel.Powered(true);
        }

        /// <summary>Scala di una lampada elettrica adesso: emergenza finche' l'ondata (partita dal quadro) non la raggiunge, poi un
        /// tremolio (si accende, cala, riparte) e la luce piena.</summary>
        public static float Scale(float delay)
        {
            if (!On) return Emergency;
            float t = Time.time - OnTime - delay;
            if (t < 0f) return Emergency;
            if (t < 0.42f) return t < 0.07f ? 1.15f : t < 0.15f ? 0.12f : t < 0.23f ? 0.9f : t < 0.31f ? 0.3f : 1f;
            return 1f;
        }
    }

    /// <summary>Il quadro elettrico: armadietto di lamiera smaltata con spie, quadrante e cartello, interruttore a coltello sul fianco e
    /// tubi che salgono al soffitto; una lampada d'emergenza rossa pulsa sopra finche' manca la corrente. F lo aziona (gratis).</summary>
    public sealed class PowerPanel : MonoBehaviour, IInteract
    {
        Transform handle;
        Material red, green, cage;
        Light lamp;
        LightPulse pulse;
        float pull, target;
        Vector3 contact;
        public int RoomId { get; private set; } = -1;
        /// <summary>0 leva su (spento), 1 leva giu' (acceso).</summary>
        public float Pull => pull;

        public Vector3 Point => transform.position + transform.forward * 0.32f;
        public float Radius => 2.2f;
        public string Prompt => Power.On ? null : "TIRA LA LEVA — RIDAI LA CORRENTE ALL'ALBERGO";

        public void Use()
        {
            if (Power.On) return;
            if (Coop.IsClient) { Coop.Game?.RequestPower(); return; }
            Power.TurnOn(0);
        }

        /// <summary>La leva scende, le spie cambiano, scintille ai contatti, la lampada sopra passa al bianco caldo.</summary>
        public void Powered(bool instant = false)
        {
            target = 1f;
            if (instant) pull = 1f;
            if (red != null) red.SetColor("_EmissionColor", Color.black);
            if (green != null) green.SetColor("_EmissionColor", new Color(0.25f, 1f, 0.35f) * 3f);
            if (cage != null) cage.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.6f) * 2.2f);
            if (lamp != null) { lamp.color = new Color(1f, 0.86f, 0.62f); }
            if (pulse != null) { pulse.baseI = 1.1f; pulse.amp = 0f; }
            if (!instant)
            {
                Fx.EnemyImpact(contact, transform.forward, new Color(1f, 0.82f, 0.42f));
                Fx.EnemyImpact(contact + transform.up * 0.05f, transform.forward, new Color(0.7f, 0.85f, 1f));
            }
        }

        void Update()
        {
            if (handle == null) return;
            pull = Mathf.MoveTowards(pull, target, Time.deltaTime / 0.38f);
            float k = pull * pull * (3f - 2f * pull);
            handle.localRotation = Quaternion.Euler(Mathf.Lerp(14f, 168f, k), 0f, 0f);
        }

        void OnDestroy() { Interact.Remove(this); }

        // ------------------------------------------------------------------ dove
        /// <summary>Il quadro sul muro dell'area piu' lontana dall'arrivo (a parita', la piu' grande) dove c'e' un tratto di muro pieno,
        /// pavimento e navmesh davanti e spazio libero; null se nessuna area ha posto. Deterministico (stesso piano, stesso posto su
        /// tutte le macchine della cooperativa).</summary>
        public static PowerPanel Build(FloorInstance fi, RoomAccessState access)
        {
            if (fi == null || fi.plan == null || access == null) return null;
            var rooms = new List<Room>();
            foreach (var r in fi.plan.rooms)
                if (r != null && !r.IsLift && r.id != access.StartRoom && r.W >= 60 && r.D >= 60) rooms.Add(r);
            rooms.Sort((a, b) =>
            {
                int da = access.Depth(a.id), db = access.Depth(b.id);
                if (da != db) return db.CompareTo(da);
                int sa = a.W * a.D, sb = b.W * b.D;
                return sa != sb ? sb.CompareTo(sa) : a.id.CompareTo(b.id);
            });
            foreach (var r in rooms)
            {
                if (!FindWall(fi, r, out Vector3 pos, out Vector3 normal)) continue;
                var go = new GameObject("QuadroElettrico");
                go.transform.SetParent(fi.root.transform, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
                var panel = go.AddComponent<PowerPanel>();
                panel.RoomId = r.id;
                panel.BuildLook(Mathf.Clamp(r.wallH - 1.75f, 0.3f, 3f));
                Interact.Add(panel);
                if (GameManager.autoTest) Debug.Log($"[PryingEyes] quadro elettrico nell'area {r.id} (profondita' {access.Depth(r.id)}) a {pos:F1}");
                return panel;
            }
            return null;
        }

        static bool FindWall(FloorInstance fi, Room r, out Vector3 pos, out Vector3 normal)
        {
            pos = normal = Vector3.zero;
            float x0 = r.x0 * 0.1f, x1 = r.x1 * 0.1f, z0 = r.z0 * 0.1f, z1 = r.z1 * 0.1f;
            Vector3 center = new Vector3((x0 + x1) * 0.5f, 1.5f, (z0 + z1) * 0.5f);
            float y0 = 0f;
            if (HotelNav.Ground(center, out Vector3 g)) y0 = g.y;
            // i quattro muri, dal piu' lungo; su ognuno i punti dal centro verso gli angoli, ogni 0,8 m
            var sides = new List<(Vector3 inward, Vector3 along, Vector3 mid, float len)>
            {
                (Vector3.back, Vector3.right, new Vector3(center.x, 0f, z1), x1 - x0),
                (Vector3.forward, Vector3.right, new Vector3(center.x, 0f, z0), x1 - x0),
                (Vector3.left, Vector3.forward, new Vector3(x1, 0f, center.z), z1 - z0),
                (Vector3.right, Vector3.forward, new Vector3(x0, 0f, center.z), z1 - z0),
            };
            sides.Sort((a, b) => b.len.CompareTo(a.len));
            foreach (var s in sides)
            {
                int steps = Mathf.FloorToInt((s.len * 0.5f - 1.2f) / 0.8f);
                for (int i = 0; i <= steps * 2; i++)
                {
                    float t = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 0.8f;
                    Vector3 onWall = s.mid + s.along * t;
                    Vector3 from = onWall + s.inward * 1.6f + Vector3.up * (y0 + 1.3f);
                    if (!Physics.Raycast(from, -s.inward, out var hit, 2.4f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.distance < 0.9f || Vector3.Dot(hit.normal, s.inward) < 0.95f) continue;
                    // muro pieno per tutta la larghezza e l'altezza dell'armadietto (niente porte, finestre o colonne a meta')
                    bool solid = true;
                    foreach (float dx in new[] { -0.55f, 0f, 0.55f })
                        foreach (float dy in new[] { -0.55f, 0f, 0.6f })
                        {
                            if (dx == 0f && dy == 0f) continue;
                            Vector3 f2 = from + s.along * dx + Vector3.up * dy;
                            if (!Physics.Raycast(f2, -s.inward, out var h2, 2.4f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) ||
                                Mathf.Abs(h2.distance - hit.distance) > 0.06f) { solid = false; break; }
                        }
                    if (!solid) continue;
                    Vector3 front = hit.point + s.inward * 0.9f;
                    if (!HotelNav.Ground(front + Vector3.up * 0.5f, out Vector3 fg) || Mathf.Abs(fg.y - y0) > 0.3f) continue;
                    if (!NavMesh.SamplePosition(fg, out _, 0.5f, NavMesh.AllAreas)) continue;
                    if (AnnexRuntime.Forbidden(fi, fg)) continue;
                    // davanti libero da arredi (si deve poter stare in piedi a tirare la leva)
                    if (Physics.CheckBox(hit.point + s.inward * 0.6f + Vector3.up * 0.15f, new Vector3(0.5f, 0.75f, 0.38f),
                                         Quaternion.LookRotation(s.inward), HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    pos = new Vector3(hit.point.x, y0 + 1.25f, hit.point.z) + hit.normal * 0.004f;
                    normal = hit.normal;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ com'e' fatto
        void BuildLook(float conduit)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var enamel = new Material(lit) { color = new Color(0.16f, 0.20f, 0.18f) };
            enamel.SetFloat("_Metallic", 0.55f); enamel.SetFloat("_Smoothness", 0.45f);
            Material iron = ProcKit.HasMat("ferro") ? ProcKit.Mat("ferro") : new Material(lit) { color = new Color(0.12f, 0.12f, 0.13f) };
            Material brass = ProcKit.HasMat("ottone") ? ProcKit.Mat("ottone") : iron;
            var wood = new Material(lit) { color = new Color(0.22f, 0.11f, 0.05f) };
            wood.SetFloat("_Smoothness", 0.35f);
            var yellow = new Material(lit) { color = new Color(0.86f, 0.64f, 0.06f) };
            var black = new Material(lit) { color = new Color(0.02f, 0.02f, 0.02f) };
            var face = new Material(lit) { color = new Color(0.82f, 0.80f, 0.72f) };
            red = Emissive(lit, new Color(0.35f, 0.02f, 0.02f), new Color(1f, 0.08f, 0.05f) * 3f);
            green = Emissive(lit, new Color(0.02f, 0.25f, 0.05f), Color.black);
            cage = Emissive(lit, new Color(0.4f, 0.05f, 0.04f), new Color(1f, 0.1f, 0.06f) * 2.5f);

            Piece(PrimitiveType.Cube, new Vector3(0f, 0f, 0.09f), new Vector3(0.62f, 0.86f, 0.18f), enamel, transform);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0f, 0.183f), new Vector3(0.55f, 0.79f, 0.01f), enamel, transform);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.45f, 0.11f), new Vector3(0.68f, 0.04f, 0.22f), iron, transform);
            Piece(PrimitiveType.Cube, new Vector3(0f, -0.45f, 0.10f), new Vector3(0.64f, 0.035f, 0.20f), iron, transform);
            foreach (float y in new[] { 0.28f, -0.28f })
                Piece(PrimitiveType.Cylinder, new Vector3(0.285f, y, 0.19f), new Vector3(0.022f, 0.04f, 0.022f), brass, transform);
            Piece(PrimitiveType.Cube, new Vector3(-0.21f, 0.37f, 0.188f), new Vector3(0.05f, 0.02f, 0.012f), brass, transform);   // maniglia
            // cartello giallo con la saetta
            Piece(PrimitiveType.Cube, new Vector3(-0.08f, 0.17f, 0.19f), new Vector3(0.20f, 0.15f, 0.006f), yellow, transform);
            var b1 = Piece(PrimitiveType.Cube, new Vector3(-0.07f, 0.195f, 0.194f), new Vector3(0.016f, 0.07f, 0.004f), black, transform);
            b1.localRotation = Quaternion.Euler(0f, 0f, -25f);
            var b2 = Piece(PrimitiveType.Cube, new Vector3(-0.09f, 0.145f, 0.194f), new Vector3(0.016f, 0.07f, 0.004f), black, transform);
            b2.localRotation = Quaternion.Euler(0f, 0f, -25f);
            // spie e quadrante
            Piece(PrimitiveType.Sphere, new Vector3(0.17f, 0.30f, 0.19f), Vector3.one * 0.045f, red, transform);
            Piece(PrimitiveType.Sphere, new Vector3(0.07f, 0.30f, 0.19f), Vector3.one * 0.045f, green, transform);
            var dial = Piece(PrimitiveType.Cylinder, new Vector3(0.12f, 0.06f, 0.19f), new Vector3(0.13f, 0.006f, 0.13f), face, transform);
            dial.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var ring = Piece(PrimitiveType.Cylinder, new Vector3(0.12f, 0.06f, 0.186f), new Vector3(0.15f, 0.006f, 0.15f), brass, transform);
            ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var needle = Piece(PrimitiveType.Cube, new Vector3(0.135f, 0.075f, 0.195f), new Vector3(0.006f, 0.05f, 0.003f), black, transform);
            needle.localRotation = Quaternion.Euler(0f, 0f, -38f);
            // interruttore a coltello sul fianco destro di chi guarda il quadro dalla stanza
            Piece(PrimitiveType.Cube, new Vector3(-0.36f, -0.06f, 0.10f), new Vector3(0.09f, 0.34f, 0.10f), iron, transform);
            foreach (float y in new[] { 0.06f, -0.18f })
                Piece(PrimitiveType.Cube, new Vector3(-0.36f, y, 0.16f), new Vector3(0.07f, 0.035f, 0.035f), brass, transform);
            contact = transform.TransformPoint(new Vector3(-0.36f, -0.18f, 0.2f));
            var pivot = new GameObject("Leva").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(-0.36f, -0.06f, 0.17f);
            handle = pivot;
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.15f, 0f), new Vector3(0.03f, 0.30f, 0.018f), brass, pivot);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.28f, 0f), new Vector3(0.10f, 0.025f, 0.025f), iron, pivot);
            var grip = Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.30f, 0.02f), new Vector3(0.042f, 0.075f, 0.042f), wood, pivot);
            grip.localRotation = Quaternion.Euler(0f, 0f, 90f);
            pull = target = 0f;
            Update();
            // tubi che salgono al soffitto e scatola di derivazione
            foreach (float x in new[] { 0.2f, -0.14f })
                Piece(PrimitiveType.Cylinder, new Vector3(x, 0.47f + conduit * 0.5f, 0.05f), new Vector3(0.045f, conduit * 0.5f, 0.045f), iron, transform);
            Piece(PrimitiveType.Cube, new Vector3(0.03f, 0.47f + conduit, 0.06f), new Vector3(0.48f, 0.08f, 0.10f), iron, transform);
            // lampada d'emergenza nella gabbia, sopra
            Piece(PrimitiveType.Sphere, new Vector3(0f, 0.56f, 0.12f), Vector3.one * 0.075f, cage, transform);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.515f, 0.12f), new Vector3(0.09f, 0.018f, 0.09f), iron, transform);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.56f, 0.163f), new Vector3(0.09f, 0.007f, 0.007f), iron, transform);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.56f, 0.163f), new Vector3(0.007f, 0.09f, 0.007f), iron, transform);
            var lg = new GameObject("Luce del quadro");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0f, 0.56f, 0.35f);
            lamp = lg.AddComponent<Light>();
            lamp.type = LightType.Point; lamp.range = 4.2f; lamp.intensity = 1.5f; lamp.shadows = LightShadows.None;
            lamp.color = new Color(1f, 0.12f, 0.08f);
            pulse = lg.AddComponent<LightPulse>();
            pulse.l = lamp; pulse.baseI = 1.5f; pulse.amp = 0.6f; pulse.speed = 3.4f;
            // solido: non lo si attraversa, e la vista che lo cerca si ferma su di lui
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(-0.03f, 0f, 0.09f);
            box.size = new Vector3(0.72f, 0.88f, 0.18f);
        }

        static Material Emissive(Shader lit, Color albedo, Color emission)
        {
            var m = new Material(lit) { color = albedo };
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", emission);
            return m;
        }

        static Transform Piece(PrimitiveType type, Vector3 pos, Vector3 size, Material m, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
