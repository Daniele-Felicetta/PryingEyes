using UnityEngine;

namespace PryingEyes
{
    /// <summary>Quello che ogni giocatore dice di se' una quindicina di volte al secondo: dove sta, dove guarda, come si muove, se ha la torcia
    /// accesa e quanto e' illuminato. L'ospite lo gira agli altri e lo usa per far vedere (e colpire) i giocatori dai nemici.</summary>
    public struct PlayerNet
    {
        public int epoch;                       // piano in cui si trova (CoopGame.Epoch)
        public Vector3 pos;                     // ai piedi
        public float yaw, pitch;                // radianti, come PlayerController
        public float speedH, lum, shotAge, hp;  // m/s, luce 0..1, secondi dall'ultimo sparo, vita 0..1
        public bool alive, crouch, sprint, torch, shotQuiet, invulnerable, velo;
        public int weapon;
        public float lean;                      // -1 sinistra .. +1 destra
        public float sight;                     // moltiplicatore della vista dei nemici (Perks.SightMult: 1, o 0.7 col Velo)

        public void Write(NetWriter w)
        {
            int flags = (alive ? 1 : 0) | (crouch ? 2 : 0) | (sprint ? 4 : 0) | (torch ? 8 : 0) | (shotQuiet ? 16 : 0) | (invulnerable ? 32 : 0) | (velo ? 64 : 0);
            w.U8(epoch).V3(pos).U8(CoopProtocol.QuantYaw(yaw)).U8(CoopProtocol.QuantPitch(pitch)).U8(flags)
             .U8(Mathf.Clamp(Mathf.RoundToInt(speedH * 20f), 0, 255)).U8(Mathf.Clamp(Mathf.RoundToInt(lum * 255f), 0, 255))
             .U8(Mathf.Clamp(Mathf.RoundToInt(shotAge * 100f), 0, 255)).U8(Mathf.Clamp(Mathf.RoundToInt(hp * 255f), 0, 255)).U8(weapon)
             .U8(Mathf.Clamp(Mathf.RoundToInt((Mathf.Clamp(lean, -1f, 1f) + 1f) * 127.5f), 0, 255)).U8(Mathf.Clamp(Mathf.RoundToInt(sight * 100f), 0, 255));
        }

        public static PlayerNet Read(ref NetReader r)
        {
            var s = new PlayerNet();
            s.epoch = r.U8();
            s.pos = r.V3();
            s.yaw = CoopProtocol.DequantYaw((byte)r.U8());
            s.pitch = CoopProtocol.DequantPitch((byte)r.U8());
            int f = r.U8();
            s.alive = (f & 1) != 0; s.crouch = (f & 2) != 0; s.sprint = (f & 4) != 0; s.torch = (f & 8) != 0; s.shotQuiet = (f & 16) != 0; s.invulnerable = (f & 32) != 0; s.velo = (f & 64) != 0;
            s.speedH = r.U8() / 20f;
            s.lum = r.U8() / 255f;
            s.shotAge = r.U8() / 100f;
            s.hp = r.U8() / 255f;
            s.weapon = r.U8();
            s.lean = r.U8() / 127.5f - 1f;
            s.sight = r.U8() / 100f;
            return s;
        }
    }

    /// <summary>Un compagno vista da questa macchina: un pupazzo con la torcia che segue la posa ricevuta. Non ha collider (i compagni non si
    /// bloccano ne' si coprono la mira); la sua torcia entra nel LightPool, quindi illumina il piano e conta nella luce che ti rende visibile.</summary>
    public sealed class RemotePlayer : MonoBehaviour, ITarget
    {
        public int slot;
        public string playerName;
        public PlayerNet last;                  // ultimo stato ricevuto
        public bool hasState;
        public float lastStateTime;
        /// <summary>Dove deve stare (ricevuto) e dove lo si disegna (liscio).</summary>
        Vector3 netPos;
        float netYaw, netPitch;
        Transform visual, legL, legR, headT, torchT;
        new TextMesh tag;                       // il nome sopra la testa (nasconde Component.tag: qui non serve quello di Unity)
        Light beam;
        float torchNear = 1f;
        Proc.PoolLight pooled;
        float walkPhase;
        bool shown;

        static readonly Color[] Coats =
        {
            new Color(0.24f, 0.27f, 0.18f), new Color(0.15f, 0.20f, 0.33f), new Color(0.36f, 0.14f, 0.15f), new Color(0.42f, 0.33f, 0.14f),
        };

        public Vector3 Feet => transform.position;
        /// <summary>Vivo e nel mio stesso piano (un compagno su un altro piano non e' un bersaglio per i miei nemici).</summary>
        public bool Alive => this != null && shown && last.alive;
        /// <summary>Il pupazzo e' disegnato (compagno nello stesso piano, gia' costruito).</summary>
        public bool Shown => shown;

        // ---- ITarget: quello che un nemico (sull'ospite) deve sapere di lui
        public int Slot => slot;
        public float Lean => last.lean;
        public Transform LookAt => headT;
        public Transform BeamT => beam != null && beam.enabled ? beam.transform : null;

        public Vector3 LeanedPoint(float h)
        {
            var pl = GameManager.I != null ? GameManager.I.player : null;
            return pl != null ? pl.LeanedPointFor(transform.position, netYaw, last.lean, h) : transform.position + Vector3.up * h;
        }

        public float SightRange(Enemy e)
        {
            return Stealth.SightRange(e, new Stealth.SightProfile
            {
                lum = last.lum, torch = last.torch, crouch = last.crouch, sprint = last.sprint, speedH = last.speedH,
                sightMult = last.sight > 0.01f ? last.sight : 1f, velo = last.velo,
                shotAge = last.shotAge + (Time.unscaledTime - lastStateTime), shotQuiet = last.shotQuiet,
            });
        }

        public float BodyHeight => last.crouch ? 1.15f : 1.75f;
        public void Hurt(float dmg, Vector3? origin = null) { if (!last.invulnerable) CoopGame.I?.HurtRemote(slot, dmg, origin); }
        public void Sedate(float seconds) { CoopGame.I?.SedateRemote(slot, seconds); }

        public static RemotePlayer Create(int slot, string name)
        {
            var go = new GameObject("Compagno " + slot);
            var rp = go.AddComponent<RemotePlayer>();
            rp.slot = slot; rp.playerName = string.IsNullOrEmpty(name) ? "Giocatore " + (slot + 1) : name;
            rp.Build();
            PlayerAvoidance.Bind(go, rp);
            if (Coop.IsHost) Targets.Add(rp);       // solo l'ospite fa girare i nemici: gli altri non hanno bisogno di bersagli
            return rp;
        }

        static Material Lit(Color c, float smooth = 0.2f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        Transform Part(PrimitiveType t, string n, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = n;
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g.transform;
        }

        void Build()
        {
            visual = new GameObject("Corpo").transform;
            visual.SetParent(transform, false);
            var coat = Lit(Coats[slot % Coats.Length]);
            var dark = Lit(new Color(0.08f, 0.08f, 0.09f));
            var skin = Lit(new Color(0.62f, 0.50f, 0.42f), 0.35f);
            // gambe: due perni all'anca che oscillano quando cammina
            legL = new GameObject("GambaS").transform; legL.SetParent(visual, false); legL.localPosition = new Vector3(-0.11f, 0.90f, 0f);
            legR = new GameObject("GambaD").transform; legR.SetParent(visual, false); legR.localPosition = new Vector3(0.11f, 0.90f, 0f);
            Part(PrimitiveType.Capsule, "Gamba", legL, new Vector3(0f, -0.45f, 0f), new Vector3(0.17f, 0.45f, 0.17f), dark);
            Part(PrimitiveType.Capsule, "Gamba", legR, new Vector3(0f, -0.45f, 0f), new Vector3(0.17f, 0.45f, 0.17f), dark);
            Part(PrimitiveType.Capsule, "Busto", visual, new Vector3(0f, 1.18f, 0f), new Vector3(0.42f, 0.40f, 0.26f), coat);
            headT = Part(PrimitiveType.Sphere, "Testa", visual, new Vector3(0f, 1.68f, 0f), new Vector3(0.24f, 0.27f, 0.25f), skin);
            // braccia in avanti verso la torcia
            var armL = Part(PrimitiveType.Capsule, "Braccio", visual, new Vector3(-0.27f, 1.25f, 0.16f), new Vector3(0.11f, 0.26f, 0.11f), coat);
            var armR = Part(PrimitiveType.Capsule, "Braccio", visual, new Vector3(0.27f, 1.25f, 0.16f), new Vector3(0.11f, 0.26f, 0.11f), coat);
            armL.localRotation = Quaternion.Euler(70f, 0f, 8f);
            armR.localRotation = Quaternion.Euler(70f, 0f, -8f);
            torchT = Part(PrimitiveType.Cylinder, "Torcia", visual, new Vector3(0.20f, 1.28f, 0.42f), new Vector3(0.06f, 0.11f, 0.06f), dark);
            torchT.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // il fascio parte dalla testa e guarda dove guarda lui (come la torcia vera, senza le ombre: ne bastano quelle del giocatore)
            var bg = new GameObject("Fascio");
            bg.transform.SetParent(transform, false);
            beam = bg.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 36f; beam.spotAngle = 72f; beam.innerSpotAngle = 52f;
            beam.color = new Color(1f, 0.93f, 0.80f);
            beam.intensity = Torch.BeamIntensity;
            beam.shadows = LightShadows.None;
            beam.cookie = Torch.Cookie();
            beam.bounceIntensity = 0f;
            pooled = Proc.LightPool.Register(beam, false, false);
            Stealth.SetLightLayers(beam, 1u);

            var tg = new GameObject("Nome");
            tg.transform.SetParent(transform, false);
            tg.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            tag = tg.AddComponent<TextMesh>();
            tag.font = HUD.Serif();
            tg.GetComponent<MeshRenderer>().sharedMaterial = tag.font.material;
            tag.text = playerName; tag.fontSize = 64; tag.characterSize = 0.045f;
            tag.anchor = TextAnchor.MiddleCenter; tag.alignment = TextAlignment.Center;
            tag.color = new Color(0.95f, 0.92f, 0.82f, 0.95f);
            SetShown(false);
        }

        /// <summary>Toglie la torcia dal LightPool: mentre il piano si costruisce non deve contare tra le sue luci (la firma del piano e'
        /// uguale su tutte le macchine solo cosi'), e prima di sparire non deve restare li' dentro.</summary>
        public void DropLight()
        {
            if (pooled != null) { Proc.LightPool.Remove(pooled); pooled = null; }
            if (beam != null) beam.enabled = false;
        }

        /// <summary>Piano costruito: la torcia torna nel LightPool (una volta sola).</summary>
        public void ReRegisterLight()
        {
            if (beam == null) return;
            DropLight();
            pooled = Proc.LightPool.Register(beam, false, last.torch && last.alive && shown);
            Stealth.SetLightLayers(beam, 1u);
        }

        public void SetName(string n)
        {
            playerName = string.IsNullOrEmpty(n) ? "Giocatore " + (slot + 1) : n;
            if (tag != null) tag.text = playerName;
        }

        void SetShown(bool on)
        {
            shown = on;
            if (visual != null) visual.gameObject.SetActive(on);
            if (tag != null) tag.gameObject.SetActive(on);
            if (beam != null) beam.enabled = on && last.torch && last.alive;
            if (pooled != null) pooled.wanted = on && last.torch && last.alive;
        }

        /// <summary>Un nuovo stato dalla rete. Se il compagno e' su un altro piano (epoca diversa) non lo si vede.</summary>
        public void Apply(PlayerNet s)
        {
            bool first = !hasState;
            last = s; hasState = true; lastStateTime = Time.unscaledTime;
            netPos = s.pos; netYaw = s.yaw; netPitch = s.pitch;
            bool visible = CoopGame.I != null && CoopGame.I.FloorBuilt && s.epoch == CoopGame.I.EpochByte;
            if (visible != shown || first) SetShown(visible);
            if (visible && (first || (transform.position - netPos).sqrMagnitude > 36f)) { transform.position = netPos; transform.rotation = Quaternion.Euler(0f, netYaw * Mathf.Rad2Deg, 0f); }
            if (visible)
            {
                if (beam != null) beam.enabled = s.torch && s.alive;
                if (pooled != null) pooled.wanted = s.torch && s.alive;
            }
        }

        void Update()
        {
            if (!hasState) return;
            // epoca cambiata sotto i piedi (siamo scesi noi, lui no): sparisce finche' non ci raggiunge
            bool visible = CoopGame.I != null && CoopGame.I.FloorBuilt && last.epoch == CoopGame.I.EpochByte;
            if (visible != shown) SetShown(visible);
            if (!shown) return;
            float dt = Time.deltaTime;
            Vector3 before = transform.position;
            transform.position = Vector3.Lerp(before, netPos, 1f - Mathf.Exp(-14f * dt));
            float yawDeg = Mathf.LerpAngle(transform.eulerAngles.y, netYaw * Mathf.Rad2Deg, 1f - Mathf.Exp(-16f * dt));
            transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            float v = dt > 0f ? new Vector2(transform.position.x - before.x, transform.position.z - before.z).magnitude / dt : 0f;
            walkPhase += dt * (3f + v * 1.6f);
            float swing = Mathf.Sin(walkPhase) * 32f * Mathf.Clamp01(v / 2.5f);
            if (legL != null) { legL.localRotation = Quaternion.Euler(swing, 0f, 0f); legR.localRotation = Quaternion.Euler(-swing, 0f, 0f); }
            // accovacciato: piu' basso; morto: a terra
            float targetScale = last.crouch ? 0.72f : 1f;
            var vs = visual.localScale;
            visual.localScale = new Vector3(1f, Mathf.Lerp(vs.y, targetScale, 1f - Mathf.Exp(-10f * dt)), 1f);
            visual.localRotation = Quaternion.Slerp(visual.localRotation, last.alive ? Quaternion.identity : Quaternion.Euler(-88f, 0f, 0f), 1f - Mathf.Exp(-8f * dt));
            visual.localPosition = Vector3.Lerp(visual.localPosition, last.alive ? Vector3.zero : new Vector3(0f, 0.18f, 0f), 1f - Mathf.Exp(-8f * dt));
            if (beam != null)
            {
                Vector3 head = transform.position + Vector3.up * (last.crouch ? 1.2f : 1.62f);
                beam.transform.position = head + transform.forward * 0.25f;
                beam.transform.rotation = Quaternion.Euler(netPitch * Mathf.Rad2Deg, yawDeg, 0f);      // come la camera di PlayerController: pitch positivo = guarda in basso
                if (beam.enabled)
                {
                    torchNear = Torch.AdaptProximity(torchNear, Torch.ProximityScale(beam.transform), dt);
                    beam.intensity = Torch.BeamIntensity * torchNear;
                    beam.spotAngle = Torch.BeamAngle(72, torchNear); beam.innerSpotAngle = beam.spotAngle * 0.72f;
                }
                else torchNear = 1f;
            }
            if (tag != null)
            {
                var pc = GameManager.I != null && GameManager.I.player != null ? GameManager.I.player.cam : null;
                if (pc != null)
                {
                    tag.transform.rotation = Quaternion.LookRotation(tag.transform.position - pc.position);
                    float d = Vector3.Distance(pc.position, tag.transform.position);
                    tag.gameObject.SetActive(d < 28f && last.alive);
                }
            }
        }

        void OnDestroy()
        {
            Targets.Remove(this);
            Enemy.OnTargetGone(this);            // chi lo seguiva sceglie un altro bersaglio
            if (pooled != null) pooled.wanted = false;
        }
    }
}
