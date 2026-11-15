using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    public enum TrapKind : byte { Fuoco, Fulmine }

    /// <summary>Trappole delle soglie (2026-11-15), come quelle di CoD Zombies. Su alcune porte a pagamento (al massimo tre per piano,
    /// varchi abbastanza larghi), una volta aperte, una leva sul muro accanto al varco accende la trappola per 20 s (1000 punti), poi
    /// restano 40 s di ricarica. Fuoco: lingue di fiamma dal pavimento della soglia. Fulmine: archi azzurri da stipite a stipite.
    /// I nemici che ci passano muoiono (il capo perde l'8% della vita al secondo) e le uccisioni pagano chi l'ha accesa; chi gioca e ci
    /// entra si scotta (30 al secondo). L'ospite (o chi gioca da solo) decide e fa danno, gli ospitati vedono lo stato
    /// (CoopGame.Traps.cs: richiesta con i punti riservati, risposta, stato a tutti).</summary>
    public sealed class DoorTraps : MonoBehaviour
    {
        public static DoorTraps I;
        public const int Price = 1000;
        public const float Duration = 20f, Cooldown = 40f;
        readonly List<DoorTrap> traps = new List<DoorTrap>();
        public IReadOnlyList<DoorTrap> Traps => traps;

        public static DoorTraps Build(FloorInstance floor)
        {
            var doors = RoomAccessDoors.I;
            if (doors == null || floor == null) return null;
            var c = floor.root.AddComponent<DoorTraps>();
            I = c;
            int n = 0, offset = FloorDirector.I != null ? FloorDirector.I.floor : 0;
            for (int i = 0; i < doors.Doors.Count && n < 3; i++)
            {
                var d = doors.Doors[i];
                if (d == null || d.Opening == null || d.Shutter == null) continue;
                if (d.Opening.width < 12 || d.Opening.top < 2.0f || d.Opening.sill > 0.1f) continue;   // varchi stretti, bassi o finestre
                if ((i + offset) % 2 != 0) continue;
                var t = DoorTrap.Make(c, d, n, (TrapKind)((i / 2 + offset) % 2));
                if (t != null) { c.traps.Add(t); n++; }
            }
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] trappole delle soglie: {n} su {doors.Doors.Count} porte");
            return c;
        }

        void OnDestroy() { if (I == this) I = null; }
    }

    public sealed class DoorTrap : MonoBehaviour
    {
        public int index;
        public TrapKind kind;
        /// <summary>0 pronta, 1 accesa, 2 in ricarica.</summary>
        public byte state;
        /// <summary>Secondi rimasti nello stato (accesa o in ricarica).</summary>
        public float timer;
        public PaidRoomDoor door;
        int activator;                     // slot di chi l'ha accesa (0 = questa macchina / l'ospite)
        Vector3 center, along, across;
        float halfW, halfD = 0.9f, top;
        Light glow;
        AudioSource hum;
        float humVol, fxT, dmgT;
        readonly List<TrapLever> levers = new List<TrapLever>();
        public IReadOnlyList<TrapLever> Levers => levers;

        public string Name => kind == TrapKind.Fuoco ? "FUOCO" : "FULMINE";
        /// <summary>Si usa solo con la porta aperta (la serranda alzata): prima e' un pezzo di muro.</summary>
        public bool Available => door != null && door.Shutter != null && door.Shutter.IsUp;
        public Vector3 Center => center;

        public static DoorTrap Make(DoorTraps owner, PaidRoomDoor door, int index, TrapKind kind)
        {
            var go = new GameObject("Trappola_" + index);
            go.transform.SetParent(owner.transform, false);
            var t = go.AddComponent<DoorTrap>();
            t.index = index; t.kind = kind; t.door = door;
            var sh = door.Shutter;
            t.center = sh.center;
            t.along = sh.alongX ? Vector3.right : Vector3.forward;
            t.across = sh.alongX ? Vector3.forward : Vector3.right;
            t.halfW = door.Opening.width * 0.05f;
            t.top = Mathf.Min(door.Opening.top, 3.2f);
            go.transform.position = t.center;
            // una leva per faccia del muro, accanto al varco (dove c'e' muro pieno)
            foreach (int side in new[] { 1, -1 })
            {
                var lever = TrapLever.Place(t, side);
                if (lever != null) t.levers.Add(lever);
            }
            if (t.levers.Count == 0) { Object.Destroy(go); return null; }
            t.glow = new GameObject("LuceTrappola").AddComponent<Light>();
            t.glow.transform.SetParent(go.transform, false);
            t.glow.transform.position = t.center + Vector3.up * 1.2f;
            t.glow.type = LightType.Point;
            t.glow.color = kind == TrapKind.Fuoco ? new Color(1f, 0.55f, 0.18f) : new Color(0.45f, 0.70f, 1f);
            t.glow.range = 7f;
            t.glow.intensity = 0f;
            t.glow.shadows = LightShadows.None;
            return t;
        }

        /// <summary>Uso da questa macchina (leva): da solo o sull'ospite paga e accende; sugli ospitati chiede all'ospite.</summary>
        public void Use()
        {
            if (!Available || state != 0) { Sfx.UI(SoundBank.BookNo, 0.5f); return; }
            if (Coop.IsClient) { Coop.Game?.RequestTrap(index); return; }
            var gm = GameManager.I;
            if (gm.score < DoorTraps.Price) { gm.hud?.Toast("Punti insufficienti."); Sfx.UI(SoundBank.BookNo, 0.5f); return; }
            gm.AddPoints(-DoorTraps.Price);
            Activate(0);
        }

        /// <summary>Sull'ospite, la richiesta di un compagno (i punti li ha gia' riservati lui).</summary>
        public bool HostActivate(int slot)
        {
            if (!Available || state != 0) return false;
            Activate(slot);
            return true;
        }

        void Activate(int slot)
        {
            activator = slot;
            state = 1; timer = DoorTraps.Duration;
            Feedback();
            Coop.Game?.TrapsChanged();
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] trappola {index} ({Name}) accesa da {slot}");
        }

        void Feedback()
        {
            if (kind == TrapKind.Fuoco) Sfx.At(SoundBank.Ignite, center + Vector3.up, 1f, 0.7f, 3f, 40f);
            else Sfx.At(SoundBank.Clang, center + Vector3.up, 0.9f, 1.6f, 3f, 40f);
            foreach (var l in levers) l.Pull();
        }

        /// <summary>Sugli ospitati: lo stato che manda l'ospite.</summary>
        public void ApplyState(int st, float t)
        {
            bool lit = st == 1 && state != 1;
            state = (byte)Mathf.Clamp(st, 0, 2);
            timer = t;
            if (lit) Feedback();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (state != 0)
            {
                timer -= dt;
                if (timer <= 0f && !Coop.IsClient)
                {
                    if (state == 1) { state = 2; timer = DoorTraps.Cooldown; }
                    else { state = 0; timer = 0f; }
                    Coop.Game?.TrapsChanged();
                }
                timer = Mathf.Max(0f, timer);
            }
            bool on = state == 1;
            if (glow != null) glow.intensity = on ? (kind == TrapKind.Fuoco ? 3.2f + Mathf.PerlinNoise(Time.time * 9f, index) * 2.4f
                                                        : (Random.value < 0.35f ? 5f : 1.2f)) : 0f;
            UpdateHum(on, dt);
            if (!on) return;
            fxT -= dt;
            if (fxT <= 0f)
            {
                fxT = kind == TrapKind.Fuoco ? 0.045f : 0.07f;
                if (kind == TrapKind.Fuoco) FireFx(); else ArcFx();
            }
            if (Coop.IsClient) return;
            dmgT -= dt;
            if (dmgT <= 0f) { dmgT = 0.2f; Hurt(0.2f); }
        }

        void UpdateHum(bool on, float dt)
        {
            if (hum == null)
            {
                hum = Sfx.Loop(gameObject, kind == TrapKind.Fuoco ? SoundBank.Wind : SoundBank.TorchBuzz, 0.9f, 2f, 26f, kind == TrapKind.Fuoco ? 1.7f : 0.55f);
                if (hum == null) return;
                humVol = hum.volume; hum.volume = 0f;
            }
            hum.volume = Mathf.MoveTowards(hum.volume, on ? humVol : 0f, dt * Mathf.Max(0.01f, humVol) * 3f);
        }

        Vector3 Floor(float a, float c) => center + along * a + across * c;

        void FireFx()
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = Floor(Random.Range(-halfW * 0.9f, halfW * 0.9f), Random.Range(-halfD * 0.7f, halfD * 0.7f)) + Vector3.up * 0.05f;
                Fx.Flame(p, (Vector3.up + Random.insideUnitSphere * 0.25f).normalized);
            }
        }

        void ArcFx()
        {
            float h0 = Random.Range(0.2f, top - 0.3f), h1 = Mathf.Clamp(h0 + Random.Range(-0.8f, 0.8f), 0.2f, top - 0.3f);
            float c = Random.Range(-halfD * 0.5f, halfD * 0.5f);
            Vector3 a = Floor(-halfW + 0.05f, c) + Vector3.up * h0, b = Floor(halfW - 0.05f, c + Random.Range(-0.2f, 0.2f)) + Vector3.up * h1;
            Vector3 mid = (a + b) * 0.5f + Random.insideUnitSphere * 0.25f;
            var col = new Color(0.55f, 0.78f, 1f);
            Fx.Beam(a, mid, 0.012f, col);
            Fx.Beam(mid, b, 0.012f, col);
            if (Random.value < 0.5f) Fx.EnemyImpact(Random.value < 0.5f ? a : b, Random.value < 0.5f ? along : -along, col);
        }

        bool Inside(Vector3 p, float pad)
        {
            Vector3 l = p - center;
            return Mathf.Abs(Vector3.Dot(l, along)) < halfW + pad && Mathf.Abs(Vector3.Dot(l, across)) < halfD + pad && l.y > -0.6f && l.y < top + 0.3f;
        }

        /// <summary>Danno di un passo (ospite o da solo): i nemici dentro muoiono (il capo perde l'8% al secondo), chi gioca si scotta.</summary>
        void Hurt(float step)
        {
            var list = new List<Enemy>(Enemy.All);
            foreach (var e in list)
            {
                if (e == null || e.IsDead) continue;
                Vector3 p = e.transform.position + Vector3.up * (Enemy.IsFlying(e.type) ? 0f : 0.9f);
                if (!Inside(p, 0.35f)) continue;
                if (Enemy.IsBoss(e.type)) e.Damage(e.maxHp * 0.08f * step, p, false, false, DamageSource.Esplosione, activator);
                else e.Damage(e.hp + 1f, p, false, false, DamageSource.Esplosione, activator);
            }
            for (int i = 0, n = Targets.Count; i < n; i++)
            {
                var t = Targets.At(i);
                if (t == null || !t.Alive || !Inside(t.Feet + Vector3.up * 0.8f, 0f)) continue;
                t.Hurt(30f * step, center + Vector3.up);
                if (t is LocalTarget) GameManager.damageTaken++;
            }
        }
    }

    /// <summary>La leva di una trappola, su una faccia del muro accanto al varco: piastra di ferro, maniglia che scende quando la
    /// trappola si accende, spia (verde pronta, rossa accesa, ambra in ricarica).</summary>
    public sealed class TrapLever : MonoBehaviour, IInteract
    {
        DoorTrap trap;
        Transform handle;
        Material lamp;
        Light lampLight;
        float pull;

        public Vector3 Point => transform.position;
        public float Radius => 2.4f;
        public string Prompt
        {
            get
            {
                if (trap == null || !trap.Available) return null;
                if (trap.state == 1) return $"TRAPPOLA DI {trap.Name} ACCESA — {Mathf.CeilToInt(trap.timer)} s";
                if (trap.state == 2) return $"TRAPPOLA DI {trap.Name} IN RICARICA — {Mathf.CeilToInt(trap.timer)} s";
                return $"ACCENDI LA TRAPPOLA DI {trap.Name} — {DoorTraps.Price} PUNTI";
            }
        }
        public void Use() => trap?.Use();

        /// <summary>Cerca muro pieno accanto al varco su quella faccia (prima a destra, poi a sinistra); null se non c'e' posto.</summary>
        public static TrapLever Place(DoorTrap t, int side)
        {
            var sh = t.door.Shutter;
            Vector3 along = sh.alongX ? Vector3.right : Vector3.forward, across = sh.alongX ? Vector3.forward : Vector3.right;
            float halfW = t.door.Opening.width * 0.05f;
            foreach (int dir in new[] { 1, -1 })
            {
                Vector3 onWall = sh.center + along * (dir * (halfW + 0.55f)) + Vector3.up * 1.25f;
                Vector3 from = onWall + across * (side * 0.8f);
                if (!Physics.Raycast(from, -across * side, out var hit, 1.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.distance < 0.45f || hit.distance > 0.75f || Vector3.Dot(hit.normal, across * side) < 0.9f) continue;
                // niente leve sopra un'altra apertura o fuori dalla stanza (dietro deve esserci pavimento)
                if (!Physics.Raycast(from, Vector3.down, 1.6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                var go = new GameObject("Leva");
                go.transform.SetParent(t.transform, false);
                go.transform.position = hit.point + hit.normal * 0.035f;
                go.transform.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);
                var lv = go.AddComponent<TrapLever>();
                lv.trap = t;
                lv.BuildLook(t.kind);
                Interact.Add(lv);
                return lv;
            }
            return null;
        }

        void BuildLook(TrapKind kind)
        {
            Material iron = ProcKit.HasMat("ferro") ? ProcKit.Mat("ferro") : new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.12f, 0.12f, 0.13f) };
            Material brass = ProcKit.HasMat("ottone") ? ProcKit.Mat("ottone") : iron;
            Piece(PrimitiveType.Cube, new Vector3(0f, 0f, 0.012f), new Vector3(0.30f, 0.44f, 0.03f), iron, transform);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.19f, -0.005f), new Vector3(0.24f, 0.035f, 0.02f), brass, transform);
            var pivot = new GameObject("Maniglia").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0f, 0.04f, -0.03f);
            handle = pivot;
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.11f, 0f), new Vector3(0.03f, 0.11f, 0.03f), iron, pivot);
            Piece(PrimitiveType.Sphere, new Vector3(0f, 0.22f, 0f), new Vector3(0.055f, 0.055f, 0.055f), brass, pivot);
            lamp = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.1f, 0.1f, 0.1f) };
            lamp.EnableKeyword("_EMISSION");
            lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Piece(PrimitiveType.Sphere, new Vector3(0f, -0.15f, -0.01f), new Vector3(0.05f, 0.05f, 0.05f), lamp, transform);
            lampLight = new GameObject("Spia").AddComponent<Light>();
            lampLight.transform.SetParent(transform, false);
            lampLight.transform.localPosition = new Vector3(0f, -0.15f, -0.12f);
            lampLight.type = LightType.Point; lampLight.range = 1.6f; lampLight.intensity = 0.8f; lampLight.shadows = LightShadows.None;
            // un simbolo inciso sopra la leva: fiamma o saetta (due barre d'ottone)
            if (kind == TrapKind.Fuoco) Piece(PrimitiveType.Sphere, new Vector3(0f, 0.30f, -0.005f), new Vector3(0.07f, 0.10f, 0.02f), brass, transform);
            else
            {
                var a = Piece(PrimitiveType.Cube, new Vector3(-0.02f, 0.31f, -0.005f), new Vector3(0.02f, 0.09f, 0.02f), brass, transform);
                a.localRotation = Quaternion.Euler(0f, 0f, 25f);
                var b = Piece(PrimitiveType.Cube, new Vector3(0.02f, 0.26f, -0.005f), new Vector3(0.02f, 0.09f, 0.02f), brass, transform);
                b.localRotation = Quaternion.Euler(0f, 0f, 25f);
            }
        }

        static Transform Piece(PrimitiveType type, Vector3 pos, Vector3 size, Material m, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        public void Pull() { pull = 1f; }

        void Update()
        {
            if (trap == null) return;
            float target = trap.state == 1 ? 1f : 0f;
            pull = Mathf.MoveTowards(pull, target, Time.deltaTime * (target > pull ? 6f : 1.2f));
            if (handle != null) handle.localRotation = Quaternion.Euler(-150f * pull, 0f, 0f);
            Color c = !trap.Available ? new Color(0.05f, 0.05f, 0.05f)
                : trap.state == 0 ? new Color(0.25f, 1f, 0.35f) : trap.state == 1 ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.62f, 0.12f);
            if (lamp != null) lamp.SetColor("_EmissionColor", c * 2f);
            if (lampLight != null) { lampLight.color = c; lampLight.intensity = trap.Available ? 0.8f : 0f; }
        }

        void OnDestroy() => Interact.Remove(this);
    }
}
