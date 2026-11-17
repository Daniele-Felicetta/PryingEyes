using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Lo Scrigno (la scatola misteriosa dei Black Ops Zombies): un baule di legno annerito e ferro rugginoso, incatenato a un
    /// occhio. La serratura e' un occhio di ferro: a baule chiuso le palpebre sono serrate (si socchiudono appena ti avvicini e ti
    /// segue con lo sguardo), quando paghi 950 punti si spalancano, il fermaglio scatta, il coperchio a botte si alza e sbatte sul fermo.
    /// Dentro, tra monete ossidate e ossa, si accende una luce fredda: nebbia, polvere e una colonna pallida da cui salgono, sempre piu'
    /// lente, le armi di tutti e cinque i gradi; si ferma su una a caso (piu' si scende, piu' pesano i gradi alti, mai quelle che hai gia'
    /// ne' la pistola di partenza). Hai nove secondi per prenderla con F: se non lo fai l'arma rientra, il coperchio cade e i punti sono
    /// persi. A tasche piene sostituisce l'arma in mano. I tempi dell'apertura seguono il suono (SoundGen.ChestOpen): scatto a 0 s, gemito
    /// dei cardini da 0,18 s, tonfo a 1,32 s, rintocco a 1,40 s. L'aspetto (texture, mesh, materiali) e' in ScrignoArt.cs.</summary>
    public sealed class Scrigno : MonoBehaviour, IInteract
    {
        public const int Price = 950;
        public enum Phase { Closed, Opening, Offering, Closing }
        public static Scrigno Current;

        public Phase phase = Phase.Closed;
        public int uses;
        /// <summary>Prezzo di questo scrigno (quello del negozio e' Price; il tesoro di un annesso e' gratis), grado minimo dell'arma e se
        /// si apre una volta sola (il tesoro degli annessi: una volta presa l'arma resta vuoto).</summary>
        [System.NonSerialized] public int price = Price, minGrade = 1;       // NonSerialized: il formato dei dati della build non cambia (basta -scriptsOnly)
        [System.NonSerialized] public bool oneShot, spent;
        public int OfferedWeapon => offered;
        public float rollTime = 3.2f, offerTime = 9f;
        /// <summary>Quanto sono aperte le palpebre dell'occhio della serratura (0 serrate, 1 spalancate): le prove lo leggono.</summary>
        public float EyeOpen => eyeOpen;
        /// <summary>Quanti oggetti disegna lo scrigno (le prove controllano che resti piccolo).</summary>
        public int RendererCount { get; private set; }
        /// <summary>Dove sta l'occhio della serratura nel mondo (le prove lo inquadrano da vicino).</summary>
        public Vector3 EyePoint => eyeRoot != null ? eyeRoot.position : Point;

        // ---- angoli e tempi (secondi)
        const float LidMax = 116f;             // il coperchio resta appoggiato al fermo, oltre la verticale
        const float Unlatch = 0.18f;           // il fermaglio scatta e il coperchio trema, fermo
        const float OpenDur = 1.30f;           // il coperchio arriva al fermo
        const float RollAt = 1.0f;             // l'arma comincia a salire quando il coperchio e' quasi su
        const float CloseDelay = 0.25f;        // prima l'arma rientra, poi cala il coperchio
        const float CloseDur = 0.62f;          // la caduta fino al bordo
        const float ClosedGlow = 0f, OpenGlow = 0.90f;       // luce fredda: a cassa chiusa niente (la tradisce solo l'occhio rosso), aperta un chiarore di tomba
        const float RestY = 0.52f, ShowY = 0.98f;            // altezza dell'arma: dentro il tesoro e in mostra
        const float HingeY = 0.5f, HingeZ = -0.315f;         // la cerniera, sul bordo posteriore della cassa
        const float R = 0.315f;                              // raggio della volta
        const float EyeY = 0.225f, EyeZ = 0.358f;            // il centro dell'occhio sulla fronte della cassa
        const float LidUpMax = 64f, LidDownMax = 36f;        // gradi delle palpebre spalancate
        const int Puffs = 5, Motes = 12;

        static readonly Color Cold = ScrignoArt.Cold;
        static readonly Color EyeRed = ScrignoArt.EyeRed;

        Transform lid, clasp, spinRoot, shaft, treasure, eyeRoot, eyeBall, pivotUp, pivotDown;
        GameObject preview;
        Light glow, belly, eyeLight;
        Material heapMat, irisMat;
        Renderer mouth, halo;
        Renderer[] shaftPlanes, puffR;
        MaterialPropertyBlock shaftMpb, mouthMpb, puffMpb, haloMpb;
        Transform[] motes, puffs;
        float[] moteSpeed, moteSeed, moteRad, puffSeed;
        readonly List<Mesh> madeMeshes = new List<Mesh>();
        int offered = -1;
        float spin, openAge, closeAge, closeFrom, angle, flare;
        float eyeOpen, blinkT = 2f, blinkPhase = -1f, irisLast = -1f;
        bool taken, flashed;

        static Material shaftMat, mouthMat, haloMat, puffMat, shadowMat, stainMat;

        public Vector3 Point => transform.position + Vector3.up * 0.6f;
        public float Radius => 2.5f;

        public string Prompt
        {
            get
            {
                var gm = GameManager.I;
                if (phase == Phase.Closed)
                {
                    if (spent) return null;
                    if (price <= 0) return "SCRIGNO ANTICO — un'arma rara, gratis";
                    int have = gm != null ? gm.score : 0;
                    return have >= price ? $"SCRIGNO — un'arma a caso — {price} punti" : $"SCRIGNO — un'arma a caso — {price} punti (ti mancano {price - have})";
                }
                if (phase == Phase.Offering && offered >= 0)
                {
                    var d = WeaponDefs.All[offered];
                    return $"PRENDI {d.name} — {d.cls.ToLowerInvariant()}, grado {d.grade} ({d.GradeName.ToLowerInvariant()})";
                }
                return null;
            }
        }

        public void Use()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            if (phase == Phase.Offering && offered >= 0)
            {
                var d = WeaponDefs.All[offered];
                gm.player.weapon.Equip(offered);
                gm.hud?.Toast(d.name + " acquisita");
                Sfx.UI(SoundBank.PickAmmo, 0.5f, 1.2f);
                taken = true;
                BeginClose();       // l'offerta finisce: il giro la vede e richiude
                return;
            }
            if (phase != Phase.Closed || spent) return;
            if (gm.score < price) { gm.hud?.Toast($"Servono {price} punti."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
            if (price > 0) gm.AddPoints(-price);
            uses++;
            offered = PickWeapon(gm.player.weapon, FloorDirector.I != null ? FloorDirector.I.floor : 1, minGrade);
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------------ l'arma estratta
        static float GradeWeight(int grade, int floor)
        {
            float f = Mathf.Clamp01((floor - 1) / 6f);
            float[] low = { 30f, 28f, 22f, 12f, 5f }, high = { 8f, 16f, 26f, 30f, 22f };
            int g = Mathf.Clamp(grade, 1, 5) - 1;
            return Mathf.Lerp(low[g], high[g], f);
        }

        /// <summary>Un'arma a caso tra quelle che non hai, pesata sul grado e sul piano.</summary>
        public static int PickWeapon(Weapon w, int floor, int minGrade = 1)
        {
            int start = WeaponDefs.IndexOf("p9_pieta");
            var pool = new List<int>(); var weights = new List<float>(); float sum = 0f;
            // 'minGrade': i tesori degli annessi danno armi di grado alto; se le hai gia' tutte si ripiega su qualunque arma che non hai
            for (int pass = 0; pass < 2 && pool.Count == 0; pass++)
            {
                int gmin = pass == 0 ? minGrade : 1;
                for (int i = 0; i < WeaponDefs.All.Length; i++)
                {
                    if (i == start || WeaponDefs.All[i].cityOnly || WeaponDefs.All[i].grade < gmin || (w != null && w.Owns(i))) continue;
                    float wt = GradeWeight(WeaponDefs.All[i].grade, floor);
                    pool.Add(i); weights.Add(wt); sum += wt;
                }
            }
            if (pool.Count == 0) return start;
            float r = Random.value * sum;
            for (int k = 0; k < pool.Count; k++) { r -= weights[k]; if (r <= 0f) return pool[k]; }
            return pool[pool.Count - 1];
        }

        // ------------------------------------------------------------------ curve del coperchio
        static float Sm(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Gradi del coperchio 't' secondi dopo l'inizio dell'apertura: fermo e tremante mentre scatta il fermaglio, poi sale pesante
        /// (parte piano, prende velocita') e sbatte sul fermo a OpenDur, dove rimbalza due volte.</summary>
        static float OpenAngle(float t)
        {
            if (t <= Unlatch) { float u = t / Unlatch; return 4f * u * (1f - u) * Mathf.Abs(Mathf.Sin(t * 150f)); }       // tremito: sale e si spegne, a zero a tutti e due i capi
            float s = (t - Unlatch) / (OpenDur - Unlatch);
            if (s < 1f) return LidMax * s * s * (1.6f - 0.6f * s);
            float b = t - OpenDur;
            return LidMax - 9f * Mathf.Abs(Mathf.Sin(b * 17f)) * Mathf.Exp(-b * 9f);
        }

        /// <summary>Gradi del coperchio 't' secondi dopo che comincia a cadere: accelera, batte sul bordo a CloseDur e rimbalza un attimo.</summary>
        static float CloseAngle(float t, float from)
        {
            if (t <= 0f) return from;
            float s = t / CloseDur;
            if (s < 1f) return from * (1f - s * s);
            float b = t - CloseDur;
            return 8f * Mathf.Abs(Mathf.Sin(b * 22f)) * Mathf.Exp(-b * 14f);
        }

        void BeginClose()
        {
            closeFrom = angle;
            closeAge = 0f;
            phase = Phase.Closing;
        }

        // ------------------------------------------------------------------ il giro
        IEnumerator Run()
        {
            phase = Phase.Opening;
            taken = false; flashed = false; openAge = 0f; flare = 0f;
            Sfx.At(SoundBank.ChestOpen, transform.position, 0.95f, 1f, 3f, 45f);
            // il coperchio lo muove Update(); qui si aspetta che sia quasi su
            while (openAge < RollAt) yield return null;
            float el = 0f, next = 0f;
            int shown = -1;
            while (el < rollTime)
            {
                el += Time.deltaTime;
                if (el >= next)
                {
                    int pick;
                    do pick = Random.Range(0, WeaponDefs.All.Length); while (pick == shown || pick == offered || WeaponDefs.All[pick].cityOnly);
                    shown = pick;
                    ShowWeapon(shown);
                    Sfx.UI(SoundBank.DropTick, 0.4f, Mathf.Lerp(1.25f, 0.8f, el / rollTime));
                    float k = el / rollTime;
                    next = el + Mathf.Lerp(0.07f, 0.45f, k * k);
                }
                spin += 140f * Time.deltaTime;
                PoseWeapon(el, 0f);
                yield return null;
            }
            ShowWeapon(offered);
            Sfx.UI(SoundBank.ChestReveal, 0.75f);
            flare = 1f;
            phase = Phase.Offering;
            float left = offerTime, since = el;
            while (phase == Phase.Offering && left > 0f)
            {
                left -= Time.deltaTime;
                since += Time.deltaTime;
                spin += 60f * Time.deltaTime;
                PoseWeapon(since, 0.05f * Mathf.Sin(Time.time * 2.4f));
                if (preview != null)
                {
                    bool vis = left > 2.5f || Mathf.Repeat(Time.time * 5f, 1f) < 0.65f;
                    if (preview.activeSelf != vis) preview.SetActive(vis);
                }
                yield return null;
            }
            if (phase == Phase.Offering)
            {
                GameManager.I?.hud?.Toast("Lo Scrigno si richiude: l'arma è sparita.");
                Sfx.At(SoundBank.DropEnd, transform.position + Vector3.up * 0.9f, 0.5f, 0.9f, 2f, 22f);
                BeginClose();
            }
            // chiusura: l'arma rientra nel tesoro (o, se l'hai presa, svanisce in alto in un lampo), poi cala il coperchio
            bool wasTaken = taken;
            if (wasTaken) flare = 1f;
            if (preview != null && !preview.activeSelf) preview.SetActive(true);
            while (closeAge < CloseDelay)
            {
                float k = Sm(closeAge / CloseDelay);
                if (spinRoot != null)
                {
                    spin += 90f * Time.deltaTime;
                    spinRoot.localRotation = Quaternion.Euler(0f, spin, 0f);
                    spinRoot.localPosition = new Vector3(0f, wasTaken ? Mathf.Lerp(ShowY, ShowY + 0.35f, k) : Mathf.Lerp(ShowY, RestY, k), 0f);
                    spinRoot.localScale = Vector3.one * (wasTaken ? Mathf.Lerp(1f, 0f, k) : Mathf.Lerp(1f, 0.3f, k));
                }
                yield return null;
            }
            if (preview != null) Destroy(preview);
            preview = null;
            Sfx.At(SoundBank.ChestClose, transform.position, 0.9f, 1f, 3f, 40f);
            while (closeAge < CloseDelay + CloseDur + 0.55f) yield return null;
            angle = 0f;
            Pose(0f);
            offered = -1;
            // il tesoro di un annesso si prende una volta sola; se l'arma e' rientrata (non l'hai presa in tempo) resta li' per un'altra volta
            if (oneShot && wasTaken) spent = true;
            phase = Phase.Closed;
        }

        void ShowWeapon(int idx)
        {
            if (preview != null) Destroy(preview);
            preview = Weapon.BuildDisplayModel(idx);
            if (preview == null) return;
            preview.transform.SetParent(spinRoot, false);
            preview.transform.localPosition = Vector3.zero;
            preview.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);     // la canna di lato: si vede girare
            foreach (var r in preview.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var mb in preview.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
        }

        /// <summary>L'arma esce dal tesoro: sale in mezzo secondo, ingrandendosi, poi resta in mostra e gira ('bob' e' l'ondeggio).</summary>
        void PoseWeapon(float since, float bob)
        {
            if (spinRoot == null) return;
            float up = Mathf.Clamp01((since - 0f) / 0.55f);
            float e = 1f - (1f - up) * (1f - up) * (1f - up);
            spinRoot.localPosition = new Vector3(0f, Mathf.Lerp(RestY, ShowY, e) + bob, 0f);
            spinRoot.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, e);
            spinRoot.localRotation = Quaternion.Euler(0f, spin, 0f);
        }

        // ------------------------------------------------------------------ animazione
        void Update()
        {
            if (lid == null) return;
            float dt = Time.deltaTime;
            EyeUpdate(dt);
            if (phase == Phase.Closed) return;
            if (phase == Phase.Opening || phase == Phase.Offering) { openAge += dt; angle = OpenAngle(openAge); }
            else { closeAge += dt; angle = CloseAngle(closeAge - CloseDelay, closeFrom); }
            // il lampo del tesoro: con il rintocco che segue il tonfo del coperchio (e, dopo, all'apparire dell'arma e quando la prendi)
            if (!flashed && phase != Phase.Closing && openAge >= OpenDur + 0.12f) { flashed = true; flare = Mathf.Max(flare, 0.7f); }
            flare = Mathf.MoveTowards(flare, 0f, dt / 0.9f);
            Pose(dt);
        }

        /// <summary>L'occhio della serratura: serrato a baule chiuso, si socchiude con un filo di luce rossa quando il giocatore e' vicino
        /// (si e' accorto di te), si spalanca quando paghi e ti segue con lo sguardo finche' il baule e' aperto, sbatte le palpebre ogni tanto.</summary>
        void EyeUpdate(float dt)
        {
            if (eyeBall == null) return;
            var gm = GameManager.I;
            Transform cam = gm != null && gm.player != null ? gm.player.cam : null;
            float dist = cam != null ? Vector3.Distance(cam.position, eyeRoot.position) : 99f;
            float target;
            switch (phase)
            {
                case Phase.Opening: target = Sm((openAge - 0.10f) / 0.40f); break;
                case Phase.Offering: target = 1f; break;
                case Phase.Closing: target = 0f; break;
                default: target = spent ? 0f : (dist < 4.2f ? Mathf.Lerp(0.16f, 0.05f, Mathf.InverseLerp(1.2f, 4.2f, dist)) : 0f); break;
            }
            if (eyeOpen <= 0.0005f && target <= 0.0005f && irisLast <= 0.0005f) return;       // lontano e chiuso: niente da fare
            float rate = phase == Phase.Opening ? 7f : phase == Phase.Closing ? 5f : 1.5f;
            eyeOpen = Mathf.MoveTowards(eyeOpen, target, dt * rate);
            // un battito di palpebre ogni tanto, quando guarda
            if (eyeOpen > 0.9f)
            {
                blinkT -= dt;
                if (blinkT <= 0f && blinkPhase < 0f) { blinkPhase = 0f; blinkT = Random.Range(3.2f, 6.5f); }
            }
            float blink = 0f;
            if (blinkPhase >= 0f)
            {
                blinkPhase += dt / 0.20f;
                if (blinkPhase >= 1f) blinkPhase = -1f; else blink = Mathf.Sin(blinkPhase * Mathf.PI);
            }
            float o = eyeOpen * (1f - blink);
            pivotUp.localRotation = Quaternion.Euler(-LidUpMax * o, 0f, 0f);
            pivotDown.localRotation = Quaternion.Euler(LidDownMax * o, 0f, 0f);
            // lo sguardo: verso il giocatore, entro un cono (non si storce)
            if (cam != null && o > 0.02f)
            {
                Vector3 d = eyeRoot.InverseTransformPoint(cam.position);
                if (d.sqrMagnitude > 1e-4f)
                {
                    d.Normalize();
                    if (d.z < 0.35f) { d.z = 0.35f; d.Normalize(); }                     // mai oltre un cono di circa 70 gradi dalla fronte
                    eyeBall.localRotation = Quaternion.Slerp(eyeBall.localRotation, Quaternion.LookRotation(d, Vector3.up), 1f - Mathf.Exp(-7f * dt));
                }
            }
            // la brace dell'iride e la luce rossa sul legno
            float beat = 1f + 0.18f * Mathf.Sin(Time.time * 2.2f);
            float iris = o * 1.0f * beat;
            if (Mathf.Abs(iris - irisLast) > 0.002f)
            {
                irisLast = iris;
                irisMat.SetColor("_EmissionColor", EyeRed * Mathf.Max(iris, 0.0f));
                ShrineKit.SetGlow(eyeLight, Mathf.Lerp(0f, 0.55f, o));
                // l'alone rosso sul legno: una luce vera, a pochi centimetri dalle assi, le brucerebbe; un velo additivo no
                bool lit = o > 0.01f;
                if (halo.enabled != lit) halo.enabled = lit;
                if (lit)
                {
                    haloMpb.SetColor("_BaseColor", new Color(1f, 0.10f, 0.04f, Mathf.Min(1f, o * 0.42f * beat)));
                    halo.SetPropertyBlock(haloMpb);
                }
            }
        }

        /// <summary>Mette in posa tutto cio' che dipende dall'angolo del coperchio: cerniera, fermaglio, luci, tesoro, fascio, nebbia, polvere.</summary>
        void Pose(float dt)
        {
            float k = Sm(angle / LidMax);
            lid.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            bool seen = angle > 0.4f;
            if (treasure.gameObject.activeSelf != seen) treasure.gameObject.SetActive(seen);
            // il fermaglio: scatta in fuori all'inizio, torna con uno scatto dopo che il coperchio ha battuto
            float c = phase == Phase.Closing ? 1f - Sm((closeAge - CloseDelay - CloseDur - 0.05f) / 0.14f) : Sm(openAge / 0.12f);
            if (phase == Phase.Closed) c = 0f;
            clasp.localRotation = Quaternion.Euler(-52f * c, 0f, 0f);
            // il tesoro e le luci crescono con l'apertura; il lampo li gonfia per un attimo
            float boost = 1f + flare * 1.2f;
            heapMat.SetColor("_EmissionColor", Cold * (Mathf.Lerp(0.0f, 0.018f, k) * boost));
            ShrineKit.SetGlow(glow, Mathf.Lerp(ClosedGlow, OpenGlow, k) + flare * 0.50f);
            ShrineKit.SetGlow(belly, k * 0.14f + flare * 0.22f);
            // il fascio di luce, il bagliore sulla bocca della cassa e la nebbia che sale: pallidi e radi
            float sh = k * k * (0.17f + 0.16f * flare);
            bool on = sh > 0.008f;
            if (shaft.gameObject.activeSelf != on) shaft.gameObject.SetActive(on);
            if (mouth.enabled != on) mouth.enabled = on;
            if (on)
            {
                float gutter = 0.90f + 0.10f * Mathf.Sin(Time.time * 8.3f) * Mathf.Sin(Time.time * 2.3f + 1f);        // il fascio vacilla appena, come una luce che sta per spegnersi
                shaftMpb.SetColor("_BaseColor", new Color(0.62f, 0.78f, 1.10f, Mathf.Min(1f, sh * gutter)));
                foreach (var r in shaftPlanes) r.SetPropertyBlock(shaftMpb);
                mouthMpb.SetColor("_BaseColor", new Color(0.60f, 0.78f, 1.10f, Mathf.Min(1f, k * (0.05f + 0.12f * flare))));
                mouth.SetPropertyBlock(mouthMpb);
            }
            float t = Time.time;
            var cam = GameManager.I != null && GameManager.I.player != null ? GameManager.I.player.cam : null;
            Quaternion face = cam != null ? cam.rotation : Quaternion.identity;
            bool mist = k > 0.40f;
            for (int i = 0; i < puffs.Length; i++)
            {
                var p = puffs[i];
                if (p.gameObject.activeSelf != mist) p.gameObject.SetActive(mist);
                if (!mist) continue;
                float f = Mathf.Repeat(t * 0.20f + puffSeed[i], 1f);
                float a = Mathf.Sin(f * Mathf.PI) * (0.20f + 0.16f * flare) * Mathf.Clamp01((k - 0.40f) * 3f);
                p.localPosition = new Vector3(Mathf.Sin(puffSeed[i] * 17f + t * 0.35f) * 0.17f, 0.56f + f * 1.15f, Mathf.Cos(puffSeed[i] * 11f + t * 0.3f) * 0.09f);
                p.localScale = Vector3.one * Mathf.Lerp(0.40f, 1.0f, f);
                p.rotation = face;
                puffMpb.SetColor("_BaseColor", new Color(0.55f, 0.72f, 1.0f, a));
                puffR[i].SetPropertyBlock(puffMpb);
            }
            // polvere che sale dal tesoro
            bool mv = k > 0.4f;
            for (int i = 0; i < motes.Length; i++)
            {
                var m = motes[i];
                if (m.gameObject.activeSelf != mv) m.gameObject.SetActive(mv);
                if (!mv) continue;
                float f = Mathf.Repeat(t * moteSpeed[i] * (1f + 2f * flare) + moteSeed[i], 1f);
                float a = moteSeed[i] * 40f + t * 0.5f;
                float rr = moteRad[i] * (0.45f + 0.55f * f);
                m.localPosition = new Vector3(Mathf.Cos(a) * rr, 0.6f + f * 1.45f, Mathf.Sin(a) * rr * 0.62f);
                m.localScale = Vector3.one * (0.015f * Mathf.Sin(f * Mathf.PI) * (0.5f + 0.7f * k));
            }
        }

        // ------------------------------------------------------------------ costruzione
        public static Scrigno Place(FloorInstance cur, Plan plan, Room room, Rng rng)
        {
            ShrineKit.Mats();
            if (!ShrineKit.FindSpot(plan, room, rng, new Vector2(0.95f, 0.95f), 0.9f, null, 0f, out Vector3 c)) return null;
            var go = new GameObject("Scrigno");
            go.transform.SetParent(cur.root.transform, false);
            go.transform.position = c;
            // la fronte guarda il centro della stanza, a scatti di 90 gradi
            Vector3 to = new Vector3(room.CenterX - c.x, 0f, room.CenterZ - c.z);
            float yaw = to.sqrMagnitude < 0.01f ? 0f : Mathf.Round(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg / 90f) * 90f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cur.propRoots.Add(go.transform);
            var s = go.AddComponent<Scrigno>();
            s.Build();
            Interact.Add(s);
            Current = s;
            return s;
        }

        /// <summary>Uno scrigno in un punto preciso (il tesoro di un annesso): non e' lo scrigno del negozio, quindi non diventa 'Current'.</summary>
        public static Scrigno PlaceAt(FloorInstance cur, Vector3 pos, float yaw, int price, int minGrade, bool oneShot)
        {
            ShrineKit.Mats();
            var go = new GameObject("Scrigno antico");
            go.transform.SetParent(cur.root.transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cur.propRoots.Add(go.transform);
            var s = go.AddComponent<Scrigno>();
            s.price = price; s.minGrade = minGrade; s.oneShot = oneShot;
            s.Build();
            Interact.Add(s);
            return s;
        }

        void OnDestroy()
        {
            Interact.Remove(this);
            if (Current == this) Current = null;
            if (heapMat != null) Destroy(heapMat);
            if (irisMat != null) Destroy(irisMat);
            foreach (var m in madeMeshes) if (m != null) Destroy(m);
        }

        // il cumulo del tesoro: (raggio, altezza) dal bordo verso il centro; la cassa e' piena fino a poco sopra il bordo (0,50)
        static readonly float[] HeapProfile = { 0.50f, 0.34f, 0.44f, 0.38f, 0.36f, 0.44f, 0.26f, 0.50f, 0.14f, 0.545f, 0.0f, 0.57f };

        static float HeapY(float r)
        {
            for (int i = 0; i < HeapProfile.Length - 2; i += 2)
            {
                float r0 = HeapProfile[i], y0 = HeapProfile[i + 1], r1 = HeapProfile[i + 2], y1 = HeapProfile[i + 3];
                if (r <= r0 && r >= r1) return Mathf.Lerp(y1, y0, (r - r1) / (r0 - r1));
            }
            return r > HeapProfile[0] ? HeapProfile[1] : HeapProfile[HeapProfile.Length - 1];
        }

        /// <summary>I materiali e le sfumature condivisi da tutti gli scrigni (si fanno una volta sola).</summary>
        static void Materials()
        {
            ScrignoArt.Mats();
            if (shaftMat != null) return;
            // fascio e bagliore: sfumature morbide, nessun bordo netto
            shaftMat = ShrineKit.Additive(ShrineKit.SoftTex("ScrignoFascio", 32, 64, (u, v) =>
            {
                float x = u * 2f - 1f;                                       // v = 0 e' la bocca della cassa: la colonna e' piu' forte in basso e svanisce in alto
                float lo = Mathf.Clamp01(v / 0.10f);
                return Mathf.Exp(-x * x * 3.4f) * Mathf.Pow(1f - v, 1.5f) * (lo * lo * (3f - 2f * lo));
            }));
            mouthMat = ShrineKit.Additive(ShrineKit.SoftTex("ScrignoBocca", 32, 32, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float e = Mathf.Clamp01(1f - r);
                return e * e * 0.9f;
            }));
            haloMat = ShrineKit.Additive(ShrineKit.SoftTex("ScrignoAlone", 32, 32, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                float e = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y));
                return e * e * e;
            }));
            puffMat = ScrignoArt.AlphaBlend(ScrignoArt.PuffTex(), true);
            // un'ombra morbida sotto la cassa (la appoggia al pavimento) e una macchia scura che cola davanti
            shadowMat = ScrignoArt.AlphaBlend(ScrignoArt.StainTex("ScrignoOmbra", 11, 0.15f), false);
            shadowMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.62f));
            stainMat = ScrignoArt.AlphaBlend(ScrignoArt.StainTex("ScrignoMacchia", 23, 0.9f), false);
            stainMat.SetColor("_BaseColor", new Color(0.02f, 0.0f, 0.0f, 0.70f));
        }

        /// <summary>Un pezzo con la sua mesh, il suo materiale e un posto (per le parti che si muovono: occhio, palpebre).</summary>
        static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        void Build()
        {
            Materials();
            var wood = ScrignoArt.Wood; var iron = ScrignoArt.Iron; var lining = ScrignoArt.Lining; var brass = ScrignoArt.Brass;
            Vector2 wt = ScrignoArt.WoodTile, it = ScrignoArt.IronTile, lt = ScrignoArt.LiningTile;
            heapMat = ShrineKit.EmitOwn(Cold, 0.02f);
            heapMat.color = new Color(0.045f, 0.034f, 0.022f);         // il letto del cumulo: terra e bronzo annerito, opaco; le monete sopra fanno i riflessi
            heapMat.SetFloat("_Metallic", 0f);
            heapMat.SetFloat("_Smoothness", 0.10f);
            irisMat = ShrineKit.EmitOwn(EyeRed, 0f);
            Vector3 Z = Vector3.zero, One = Vector3.one;
            Mesh Bx(Vector3 size, Vector2 tile, float ox = 0f, float oy = 0f) => ScrignoArt.Box(size, tile, new Vector2(ox, oy));
            int renderers = 0;

            // ---- un'ombra morbida sotto la cassa e una macchia scura davanti: sul pavimento, senza profondita' (non fanno z-fighting)
            var quad = ScrignoArt.Plane();
            Part(transform, "OmbraSotto", quad, shadowMat, new Vector3(0f, 0.012f, 0f), new Vector3(90f, 0f, 0f), new Vector3(1.85f, 1.45f, 1f));
            Part(transform, "Macchia", quad, stainMat, new Vector3(0.28f, 0.013f, 0.62f), new Vector3(90f, 17f, 0f), new Vector3(1.05f, 0.80f, 1f));
            renderers += 2;

            // ---- la cassa: assi di legno annerito, zoccolo e fasce di ferro rugginoso, serratura a occhio, tutto fuso in pochi oggetti
            var body = new GameObject("Corpo").transform;
            body.SetParent(transform, false);
            var bag = new ScrignoArt.MeshBag();
            bag.Add(wood, Bx(new Vector3(0.96f, 0.08f, 0.58f), wt, 0.15f, 0f), new Vector3(0f, 0.04f, 0f));
            bag.Add(wood, Bx(new Vector3(1.0f, 0.5f, 0.04f), wt, 0f, 0f), new Vector3(0f, 0.25f, -0.29f));
            bag.Add(wood, Bx(new Vector3(1.0f, 0.5f, 0.04f), wt, 0.37f, 0f), new Vector3(0f, 0.25f, 0.29f));
            foreach (float s in new[] { -1f, 1f })
                bag.Add(wood, Bx(new Vector3(0.04f, 0.5f, 0.54f), wt, 0.2f * s + 0.5f, 0f), new Vector3(0.48f * s, 0.25f, 0f));
            // la fodera: velluto annerito dentro le pareti e sul fondo
            bag.Add(lining, Bx(new Vector3(0.92f, 0.43f, 0.006f), lt), new Vector3(0f, 0.285f, -0.267f));
            bag.Add(lining, Bx(new Vector3(0.92f, 0.43f, 0.006f), lt, 0.3f, 0.2f), new Vector3(0f, 0.285f, 0.267f));
            foreach (float s in new[] { -1f, 1f })
                bag.Add(lining, Bx(new Vector3(0.006f, 0.43f, 0.528f), lt, 0.5f * s, 0.4f), new Vector3(0.457f * s, 0.285f, 0f));
            bag.Add(lining, Bx(new Vector3(0.90f, 0.01f, 0.52f), lt), new Vector3(0f, 0.08f, 0f));
            // ferri: zoccolo, collare sul bordo, spigoli, fasce verticali davanti, dietro e ai lati
            bag.Add(iron, Bx(new Vector3(1.04f, 0.06f, 0.66f), it), new Vector3(0f, 0.03f, 0f));
            foreach (float z in new[] { -0.316f, 0.316f })
                bag.Add(iron, Bx(new Vector3(1.02f, 0.035f, 0.014f), it, z, 0.1f), new Vector3(0f, 0.485f, z));
            foreach (float x in new[] { -0.506f, 0.506f })
                bag.Add(iron, Bx(new Vector3(0.014f, 0.035f, 0.62f), it, x, 0.3f), new Vector3(x, 0.485f, 0f));
            foreach (float x in new[] { -0.4825f, 0.4825f })
                foreach (float z in new[] { -0.2925f, 0.2925f })
                    bag.Add(iron, Bx(new Vector3(0.05f, 0.5f, 0.05f), it, x, z), new Vector3(x, 0.25f, z));
            foreach (float x in new[] { -0.30f, 0.30f })
                foreach (float z in new[] { -0.316f, 0.316f })
                    bag.Add(iron, Bx(new Vector3(0.075f, 0.5f, 0.012f), it, x * 2f, z), new Vector3(x, 0.25f, z));
            foreach (float x in new[] { -0.506f, 0.506f })
                bag.Add(iron, Bx(new Vector3(0.012f, 0.5f, 0.075f), it, x * 3f, 0.5f), new Vector3(x, 0.25f, 0f));
            // chiodi a testa tonda sulle fasce
            var rivet = ScrignoArt.Sphere(8, 5);
            foreach (float y in new[] { 0.075f, 0.175f, 0.275f, 0.375f, 0.455f })
            {
                foreach (float x in new[] { -0.30f, 0.30f })
                    foreach (float z in new[] { -0.322f, 0.322f })
                        bag.Add(iron, rivet, new Vector3(x, y, z), Z, new Vector3(0.026f, 0.026f, 0.012f));
                foreach (float x in new[] { -0.512f, 0.512f })
                    bag.Add(iron, rivet, new Vector3(x, y, 0f), Z, new Vector3(0.012f, 0.026f, 0.026f));
            }
            // le maniglie ai lati: una piastra e un anello che pende
            var ring = ScrignoArt.Torus("scrignoAnello", 0.040f, 0.0075f, 18, 8);
            foreach (float s in new[] { -1f, 1f })
            {
                bag.Add(iron, Bx(new Vector3(0.012f, 0.105f, 0.07f), it, s, 0.7f), new Vector3(0.518f * s, 0.34f, 0f));
                bag.Add(iron, ring, new Vector3(0.533f * s, 0.295f, 0f), new Vector3(0f, 0f, 90f), One);
            }
            // la piastra del fermaglio sotto il bordo, dove si aggancia la linguetta
            bag.Add(iron, Bx(new Vector3(0.085f, 0.075f, 0.008f), it, 0.2f, 0.2f), new Vector3(0f, 0.41f, 0.316f));
            // l'occhio: una piastra di ferro ovale su un bordo piu' largo, la raggiera di punte e la buca scura dove sta il globo
            var cyl = ScrignoArt.Cyl(20);
            bag.Add(iron, cyl, new Vector3(0f, EyeY, 0.324f), new Vector3(90f, 0f, 0f), new Vector3(0.335f, 0.012f, 0.225f));
            bag.Add(iron, cyl, new Vector3(0f, EyeY, 0.336f), new Vector3(90f, 0f, 0f), new Vector3(0.295f, 0.012f, 0.190f));
            bag.Add(ScrignoArt.Socket, cyl, new Vector3(0f, EyeY, 0.3435f), new Vector3(90f, 0f, 0f), new Vector3(0.225f, 0.004f, 0.130f));
            var cone = ScrignoArt.Cone(10);
            for (int i = 0; i < 12; i++)
            {
                float th = i * 30f * Mathf.Deg2Rad;
                if (i == 3) continue;                                                // in alto passa la linguetta
                float cx = Mathf.Cos(th), cy = Mathf.Sin(th);
                Vector3 baseP = new Vector3(cx * 0.165f, EyeY + cy * 0.108f, 0.331f);
                bag.Add(iron, cone, baseP, new Vector3(0f, 0f, i * 30f - 90f), new Vector3(0.026f, 0.052f, 0.026f));
            }
            // cerniere sul retro
            foreach (float x in new[] { -0.25f, 0.25f })
                bag.Add(brass, Bx(new Vector3(0.10f, 0.075f, 0.012f), it), new Vector3(x, 0.455f, HingeZ - 0.004f));
            renderers += bag.Emit(body, "Corpo", ShadowCastingMode.On).Count;
            madeMeshes.AddRange(bag.made);

            // ---- il tesoro: monete ossidate, ossa, un teschio e qualche pietra spenta, in un cumulo; si accende solo a coperchio aperto
            treasure = new GameObject("Tesoro").transform;                      // acceso solo a coperchio aperto: chiuso non si vede, non va disegnato
            treasure.SetParent(transform, false);
            var heap = ShrineKit.LatheObj(treasure, "Cumulo", heapMat, ShrineKit.Lathe("scrignoCumulo", HeapProfile, 24));
            heap.transform.localScale = new Vector3(1f, 1f, 0.58f);
            renderers++;
            var rnd = new System.Random(Mathf.RoundToInt(transform.position.x * 37f) * 7919 + Mathf.RoundToInt(transform.position.z * 53f));
            float Rf(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            float OnHeap(float x, float z) => HeapY(Mathf.Sqrt(x * x + (z / 0.58f) * (z / 0.58f)));
            var tb = new ScrignoArt.MeshBag();
            for (int i = 0; i < 160; i++)
            {
                float a = Rf(0f, Mathf.PI * 2f), rr = Mathf.Sqrt(Rf(0f, 1f));
                float x = Mathf.Cos(a) * rr * 0.42f, z = Mathf.Sin(a) * rr * 0.24f;
                tb.Add(i % 3 == 0 ? ScrignoArt.CoinB : ScrignoArt.CoinA, cyl, new Vector3(x, OnHeap(x, z) + 0.010f, z), new Vector3(Rf(-30f, 30f), Rf(0f, 180f), Rf(-30f, 30f)), new Vector3(0.060f, 0.0045f, 0.060f));
            }
            var bone = ScrignoArt.Bone; var knob = ScrignoArt.Sphere(8, 6);
            foreach (var bn in new[] { (x: -0.16f, z: 0.06f, yaw: 25f, len: 0.30f), (x: 0.13f, z: -0.05f, yaw: -40f, len: 0.27f), (x: 0.02f, z: 0.12f, yaw: 82f, len: 0.24f) })
            {
                float y = OnHeap(bn.x, bn.z) + 0.030f;
                var q = Quaternion.Euler(0f, bn.yaw, 0f);
                tb.Add(bone, cyl, new Vector3(bn.x, y, bn.z), new Vector3(0f, bn.yaw, 90f - Rf(0f, 14f)), new Vector3(0.024f, bn.len, 0.024f));
                foreach (float e in new[] { -0.5f, 0.5f })
                {
                    Vector3 end = new Vector3(bn.x, y, bn.z) + q * new Vector3(e * bn.len, 0f, 0f);
                    foreach (float s in new[] { -0.011f, 0.011f })
                        tb.Add(bone, knob, end + q * new Vector3(0f, 0f, s), Z, new Vector3(0.030f, 0.030f, 0.030f));
                }
            }
            // il teschio, mezzo sepolto, con le orbite verso l'alto
            {
                Vector3 sp = new Vector3(-0.27f, OnHeap(-0.27f, 0.05f) + 0.052f, 0.05f);
                var sq = Quaternion.Euler(-28f, 38f, 8f);
                void Sk(Material m, Mesh me, Vector3 local, Vector3 scale) => tb.Add(m, me, sp + sq * local, sq.eulerAngles, scale);
                Sk(bone, knob, new Vector3(0f, 0f, 0f), new Vector3(0.150f, 0.130f, 0.160f));                       // cranio
                Sk(bone, knob, new Vector3(0f, -0.050f, 0.040f), new Vector3(0.100f, 0.075f, 0.100f));              // zigomi e mascella alta
                Sk(bone, Bx(new Vector3(0.072f, 0.026f, 0.082f), Vector2.one), new Vector3(0f, -0.098f, 0.032f), One);       // mascella
                Sk(ScrignoArt.Socket, knob, new Vector3(-0.032f, 0.000f, 0.064f), new Vector3(0.048f, 0.052f, 0.040f));    // orbite
                Sk(ScrignoArt.Socket, knob, new Vector3(0.032f, 0.000f, 0.064f), new Vector3(0.048f, 0.052f, 0.040f));
                Sk(ScrignoArt.Socket, knob, new Vector3(0f, -0.034f, 0.086f), new Vector3(0.022f, 0.034f, 0.020f));        // naso
            }
            // tre pietre spente: rosso scuro, viola e un bianco di ghiaccio
            var gemCols = new[] { new Color(0.80f, 0.04f, 0.06f), new Color(0.45f, 0.15f, 0.75f), new Color(0.55f, 0.75f, 1f), new Color(0.80f, 0.04f, 0.06f) };
            for (int i = 0; i < gemCols.Length; i++)
            {
                float a = Rf(0f, Mathf.PI * 2f), rr = Mathf.Sqrt(Rf(0.2f, 0.9f));
                float x = Mathf.Cos(a) * rr * 0.34f, z = Mathf.Sin(a) * rr * 0.20f;
                tb.Add(ScrignoArt.Gem(gemCols[i], 0.30f), knob, new Vector3(x, OnHeap(x, z) + 0.016f, z), new Vector3(Rf(0f, 40f), Rf(0f, 180f), Rf(0f, 40f)), new Vector3(0.042f, 0.032f, 0.042f));
            }
            renderers += tb.Emit(treasure, "Tesoro", ShadowCastingMode.Off).Count;
            madeMeshes.AddRange(tb.made);

            // ---- il coperchio a botte, incernierato sul bordo posteriore: legno fuori, velluto dentro, due cerchi, fianchi, dorsale, fermaglio
            var hinge = new GameObject("Cerniera");
            hinge.transform.SetParent(transform, false);
            hinge.transform.localPosition = new Vector3(0f, HingeY, HingeZ);
            lid = hinge.transform;
            var lb = new ScrignoArt.MeshBag();
            var vault = ScrignoArt.Barrel("scrignoVolta2", 1.04f, R, 0.03f, R, wt);
            lb.Add(wood, vault, Z, Z, One, 0);
            lb.Add(lining, vault, Z, Z, One, 1);
            var hoop = ScrignoArt.Barrel("scrignoCerchio2", 0.08f, R + 0.014f, 0.032f, R, it);
            foreach (float x in new[] { -0.30f, 0.30f }) { lb.Add(iron, hoop, new Vector3(x, 0f, 0f), Z, One, 0); lb.Add(iron, hoop, new Vector3(x, 0f, 0f), Z, One, 1); }
            var cap = ScrignoArt.Barrel("scrignoTesta", 0.012f, R + 0.014f, R + 0.014f, R, it);
            foreach (float x in new[] { -0.526f, 0.526f }) lb.Add(iron, cap, new Vector3(x, 0f, 0f), Z, One, 0);
            lb.Add(iron, Bx(new Vector3(0.92f, 0.016f, 0.07f), it, 0.6f, 0.4f), new Vector3(0f, R + 0.004f, R));
            foreach (float x in new[] { -0.25f, 0.25f })
                lb.Add(brass, cyl, new Vector3(x, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.046f, 0.10f, 0.046f));
            // il ponticello che porta il cardine della linguetta fuori dalla curva del coperchio
            lb.Add(iron, Bx(new Vector3(0.075f, 0.05f, 0.036f), it, 0.1f, 0.9f), new Vector3(0f, 0.075f, 0.624f));
            renderers += lb.Emit(lid, "Volta", ShadowCastingMode.On).Count;
            madeMeshes.AddRange(lb.made);
            // la linguetta (il fermaglio): pende davanti al cardine e scatta in fuori
            var cl = new GameObject("Fermaglio");
            cl.transform.SetParent(lid, false);
            cl.transform.localPosition = new Vector3(0f, 0.075f, 0.645f);
            clasp = cl.transform;
            Part(clasp, "Linguetta", ScrignoArt.Box(new Vector3(0.062f, 0.17f, 0.010f), it, new Vector2(0.4f, 0.4f)), iron, new Vector3(0f, -0.085f, 0f), Z, One);
            Part(clasp, "Perno", knob, brass, Z, Z, new Vector3(0.026f, 0.026f, 0.026f));
            renderers += 3;

            // ---- l'occhio della serratura: un globo ingiallito con l'iride che brucia e la pupilla verticale, tra due palpebre di ferro
            eyeRoot = new GameObject("Occhio").transform;
            eyeRoot.SetParent(transform, false);
            eyeRoot.localPosition = new Vector3(0f, EyeY, EyeZ);
            eyeBall = new GameObject("Globo").transform;
            eyeBall.SetParent(eyeRoot, false);
            Part(eyeBall, "Sclera", knob, ScrignoArt.Sclera, Z, Z, One * 0.124f);
            Part(eyeBall, "Iride", knob, irisMat, new Vector3(0f, 0f, 0.049f), Z, new Vector3(0.076f, 0.076f, 0.042f));
            Part(eyeBall, "Pupilla", knob, ScrignoArt.Socket, new Vector3(0f, 0f, 0.0665f), Z, new Vector3(0.014f, 0.052f, 0.012f));
            var shell = ScrignoArt.Dome("scrignoPalpebra", 0.071f, 0.008f, 18, 8);
            pivotUp = new GameObject("PalpebraSu").transform;
            pivotUp.SetParent(eyeRoot, false);
            Part(pivotUp, "Guscio", shell, iron, Z, Z, One);
            pivotDown = new GameObject("PalpebraGiu").transform;
            pivotDown.SetParent(eyeRoot, false);
            Part(pivotDown, "Guscio", shell, iron, Z, new Vector3(0f, 0f, 180f), One);
            renderers += 5;
            // il velo rosso sul legno intorno all'occhio (spento finche' le palpebre sono serrate)
            haloMpb = new MaterialPropertyBlock();
            halo = Part(transform, "AloneOcchio", quad, haloMat, new Vector3(0f, EyeY, 0.352f), Z, new Vector3(1.0f, 0.66f, 1f)).GetComponent<MeshRenderer>();
            halo.receiveShadows = false;
            halo.enabled = false;
            renderers++;

            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.34f, 0f);
            col.size = new Vector3(1.04f, 0.68f, 0.66f);

            // ---- il fascio di luce (tre piani verticali incrociati), il bagliore sulla bocca, la nebbia e la polvere: spenti finche' e' chiuso
            shaftMpb = new MaterialPropertyBlock();
            mouthMpb = new MaterialPropertyBlock();
            puffMpb = new MaterialPropertyBlock();
            shaft = new GameObject("Fascio").transform;
            shaft.SetParent(transform, false);
            shaft.localPosition = new Vector3(0f, 0.66f, 0f);          // la colonna nasce sopra il cumulo: sovrapposta, lo velava tutto di bianco
            shaftPlanes = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Piano" + (i + 1);
                Util.Kill(q.GetComponent<Collider>());
                q.transform.SetParent(shaft, false);
                q.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                q.transform.localRotation = Quaternion.Euler(0f, 60f * i, 0f);
                q.transform.localScale = new Vector3(0.85f, 2.0f, 1f);
                var qr = q.GetComponent<MeshRenderer>();
                qr.sharedMaterial = shaftMat;
                qr.shadowCastingMode = ShadowCastingMode.Off;
                qr.receiveShadows = false;
                shaftPlanes[i] = qr;
            }
            shaft.gameObject.SetActive(false);
            var mo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mo.name = "Bocca";
            Util.Kill(mo.GetComponent<Collider>());
            mo.transform.SetParent(transform, false);
            mo.transform.localPosition = new Vector3(0f, 0.585f, 0f);
            mo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            mo.transform.localScale = new Vector3(0.95f, 0.62f, 1f);
            mouth = mo.GetComponent<MeshRenderer>();
            mouth.sharedMaterial = mouthMat;
            mouth.shadowCastingMode = ShadowCastingMode.Off;
            mouth.receiveShadows = false;
            mouth.enabled = false;
            puffs = new Transform[Puffs]; puffR = new Renderer[Puffs]; puffSeed = new float[Puffs];
            for (int i = 0; i < Puffs; i++)
            {
                var pq = GameObject.CreatePrimitive(PrimitiveType.Quad);
                pq.name = "Nebbia" + i;
                Util.Kill(pq.GetComponent<Collider>());
                pq.transform.SetParent(transform, false);
                var pr = pq.GetComponent<MeshRenderer>();
                pr.sharedMaterial = puffMat;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.receiveShadows = false;
                puffs[i] = pq.transform; puffR[i] = pr; puffSeed[i] = i / (float)Puffs + Rf(-0.04f, 0.04f);
                pq.SetActive(false);
            }
            motes = new Transform[Motes]; moteSpeed = new float[Motes]; moteSeed = new float[Motes]; moteRad = new float[Motes];
            var moteMat = ShrineKit.Emit(new Color(0.62f, 0.78f, 1f), 2.4f);
            for (int i = 0; i < Motes; i++)
            {
                motes[i] = ShrineKit.Prim(transform, PrimitiveType.Sphere, "Scintilla", moteMat, Vector3.zero, Vector3.one * 0.015f).transform;
                moteSpeed[i] = Rf(0.14f, 0.36f); moteSeed[i] = Rf(0f, 1f); moteRad[i] = Rf(0.10f, 0.34f);
                motes[i].gameObject.SetActive(false);
            }

            var sr = new GameObject("ArmaGira");
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = new Vector3(0f, ShowY, 0f);
            spinRoot = sr.transform;
            // in alto e un po' dietro: a 25 cm dal legno lo bruciava, a 30 cm dall'arma in mostra la faceva bianca
            glow = ShrineKit.PointLight(transform, "Luce", new Vector3(0f, 1.7f, -0.3f), Cold, ClosedGlow, 5.2f, 0.18f, 2.0f);
            belly = ShrineKit.PointLight(transform, "LuceInterna", new Vector3(0f, 0.82f, 0.44f), new Color(0.55f, 0.72f, 1f), 0f, 2.2f, 0.22f, 3.1f);
            eyeLight = ShrineKit.PointLight(transform, "LuceOcchio", new Vector3(0f, 0.80f, 0.95f), EyeRed, 0f, 3.2f, 0.20f, 1.3f);
            RendererCount = renderers;
            angle = 0f;
            Pose(0f);
        }
    }
}
