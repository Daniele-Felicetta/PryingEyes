using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Lo Scrigno (la scatola misteriosa dei Black Ops Zombies): un forziere di legno con le fasce di ferro, la cassa vuota e foderata
    /// di velluto, pieno di monete e gemme fino al bordo, il coperchio a botte con la cerniera dietro. Per 950 punti il fermaglio scatta, il
    /// coperchio si alza, sbatte sul fermo e dentro si accende il tesoro: una colonna di luce sale dalla cassa e da li' salgono, sempre piu'
    /// lente, le armi di tutti e cinque i gradi; si ferma su una a caso (piu' si scende, piu' pesano i gradi alti, mai quelle che hai gia' ne'
    /// la pistola di partenza). Hai nove secondi per prenderla con F: se non lo fai l'arma rientra, il coperchio cade e i punti sono persi.
    /// A tasche piene sostituisce l'arma in mano. I tempi dell'apertura seguono il suono (SoundGen.ChestOpen): scatto a 0 s, gemito dei
    /// cardini da 0,18 s, tonfo a 1,32 s, due note di vetro a 1,45 s.</summary>
    public sealed class Scrigno : MonoBehaviour, IInteract
    {
        public const int Price = 950;
        public enum Phase { Closed, Opening, Offering, Closing }
        public static Scrigno Current;

        public Phase phase = Phase.Closed;
        public int uses;
        public int OfferedWeapon => offered;
        public float rollTime = 3.2f, offerTime = 9f;

        // ---- angoli e tempi (secondi)
        const float LidMax = 116f;             // il coperchio resta appoggiato al fermo, oltre la verticale
        const float Unlatch = 0.18f;           // il fermaglio scatta e il coperchio trema, fermo
        const float OpenDur = 1.30f;           // il coperchio arriva al fermo
        const float RollAt = 1.0f;             // l'arma comincia a salire quando il coperchio e' quasi su
        const float CloseDelay = 0.25f;        // prima l'arma rientra, poi cala il coperchio
        const float CloseDur = 0.62f;          // la caduta fino al bordo
        const float ClosedGlow = 0.5f, OpenGlow = 2.2f;      // luce ambra: a cassa chiusa un filo (si trova al buio), aperta di piu'
        const float RestY = 0.52f, ShowY = 0.98f;            // altezza dell'arma: dentro il tesoro e in mostra
        const float HingeY = 0.5f, HingeZ = -0.315f;         // la cerniera, sul bordo posteriore della cassa
        const float R = 0.315f;                              // raggio della volta

        static readonly Color Amber = new Color(1f, 0.62f, 0.2f);
        static readonly Color HeapColor = new Color(1f, 0.6f, 0.15f);

        Transform lid, clasp, spinRoot, shaft, treasure;
        GameObject preview;
        Light glow;
        Material heapMat, keyMat;
        Renderer mouth;
        Renderer[] shaftPlanes;
        MaterialPropertyBlock shaftMpb, mouthMpb;
        Transform[] motes;
        float[] moteSpeed, moteSeed, moteRad;
        int offered = -1;
        float spin, openAge, closeAge, closeFrom, angle, flare;
        bool taken, flashed;

        static Material velvet, coinA, coinB, shaftMat, mouthMat;

        public Vector3 Point => transform.position + Vector3.up * 0.6f;
        public float Radius => 2.5f;

        public string Prompt
        {
            get
            {
                var gm = GameManager.I;
                if (phase == Phase.Closed)
                {
                    int have = gm != null ? gm.score : 0;
                    return have >= Price ? $"SCRIGNO — un'arma a caso — {Price} punti" : $"SCRIGNO — un'arma a caso — {Price} punti (ti mancano {Price - have})";
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
            if (phase != Phase.Closed) return;
            if (gm.score < Price) { gm.hud?.Toast($"Servono {Price} punti."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
            gm.AddPoints(-Price);
            uses++;
            offered = PickWeapon(gm.player.weapon, FloorDirector.I != null ? FloorDirector.I.floor : 1);
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
        public static int PickWeapon(Weapon w, int floor)
        {
            int start = WeaponDefs.IndexOf("p9_pieta");
            var pool = new List<int>(); var weights = new List<float>(); float sum = 0f;
            for (int i = 0; i < WeaponDefs.All.Length; i++)
            {
                if (i == start || WeaponDefs.All[i].cityOnly || (w != null && w.Owns(i))) continue;
                float wt = GradeWeight(WeaponDefs.All[i].grade, floor);
                pool.Add(i); weights.Add(wt); sum += wt;
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
            foreach (var r in preview.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
            if (lid == null || phase == Phase.Closed) return;
            float dt = Time.deltaTime;
            if (phase == Phase.Opening || phase == Phase.Offering) { openAge += dt; angle = OpenAngle(openAge); }
            else { closeAge += dt; angle = CloseAngle(closeAge - CloseDelay, closeFrom); }
            // il lampo del tesoro: con le due note di vetro che seguono il tonfo del coperchio (e, dopo, all'apparire dell'arma e quando la prendi)
            if (!flashed && phase != Phase.Closing && openAge >= OpenDur + 0.12f) { flashed = true; flare = Mathf.Max(flare, 0.7f); }
            flare = Mathf.MoveTowards(flare, 0f, dt / 0.9f);
            Pose(dt);
        }

        /// <summary>Mette in posa tutto cio' che dipende dall'angolo del coperchio: cerniera, fermaglio, luce, tesoro, fascio, polvere d'oro.</summary>
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
            // la serratura lampeggia quando molla e si spegne a poco a poco
            float key = phase == Phase.Closed ? 3f : Mathf.Lerp(9f, 1.0f, Sm(openAge / 0.7f));
            if (phase == Phase.Closing) key = Mathf.Lerp(1.0f, 3f, Sm((closeAge - CloseDelay - CloseDur) / 0.3f));
            keyMat.SetColor("_EmissionColor", Amber * key);
            // il tesoro e la luce crescono con l'apertura; il lampo li gonfia per un attimo
            float boost = 1f + flare * 1.4f;
            heapMat.SetColor("_EmissionColor", HeapColor * (Mathf.Lerp(0.08f, 0.75f, k) * boost));
            ShrineKit.SetGlow(glow, Mathf.Lerp(ClosedGlow, OpenGlow, k) + flare * 0.9f);
            // il fascio di luce e il bagliore sulla bocca della cassa
            float sh = k * k * (0.50f + 0.30f * flare);
            bool on = sh > 0.008f;
            if (shaft.gameObject.activeSelf != on) shaft.gameObject.SetActive(on);
            if (mouth.enabled != on) mouth.enabled = on;
            if (on)
            {
                shaftMpb.SetColor("_BaseColor", new Color(1.15f, 0.80f, 0.36f, Mathf.Min(1f, sh)));
                foreach (var r in shaftPlanes) r.SetPropertyBlock(shaftMpb);
                mouthMpb.SetColor("_BaseColor", new Color(1.2f, 0.78f, 0.30f, Mathf.Min(1f, k * (0.65f + 0.25f * flare))));
                mouth.SetPropertyBlock(mouthMpb);
            }
            // polvere d'oro che sale dal tesoro
            bool mv = k > 0.4f;
            float t = Time.time;
            for (int i = 0; i < motes.Length; i++)
            {
                var m = motes[i];
                if (m.gameObject.activeSelf != mv) m.gameObject.SetActive(mv);
                if (!mv) continue;
                float f = Mathf.Repeat(t * moteSpeed[i] * (1f + 2f * flare) + moteSeed[i], 1f);
                float a = moteSeed[i] * 40f + t * 0.5f;
                float rr = moteRad[i] * (0.45f + 0.55f * f);
                m.localPosition = new Vector3(Mathf.Cos(a) * rr, 0.6f + f * 1.45f, Mathf.Sin(a) * rr * 0.62f);
                m.localScale = Vector3.one * (0.017f * Mathf.Sin(f * Mathf.PI) * (0.5f + 0.7f * k));
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

        void OnDestroy()
        {
            Interact.Remove(this);
            if (Current == this) Current = null;
            if (heapMat != null) Destroy(heapMat);
            if (keyMat != null) Destroy(keyMat);
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

        static void Materials()
        {
            if (velvet != null) return;
            velvet = ShrineKit.Lit("Scrigno_Velluto", new Color(0.20f, 0.018f, 0.03f), 0f, 0.2f);
            coinA = ShrineKit.Lit("Scrigno_MonetaA", new Color(0.85f, 0.52f, 0.07f), 0.3f, 0.5f);
            coinB = ShrineKit.Lit("Scrigno_MonetaB", new Color(0.62f, 0.36f, 0.05f), 0.3f, 0.45f);
            foreach (var m in new[] { coinA, coinB })
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.08f) * 0.22f);
            }
            // fascio e bagliore: sfumature morbide, nessun bordo netto
            shaftMat = ShrineKit.Additive(ShrineKit.SoftTex("ScrignoFascio", 32, 64, (u, v) =>
            {
                float x = u * 2f - 1f;                                       // v = 0 e' la bocca della cassa: la colonna e' piu' forte in basso e svanisce in alto
                float lo = Mathf.Clamp01(v / 0.05f);
                return Mathf.Exp(-x * x * 3.4f) * Mathf.Pow(1f - v, 1.5f) * (lo * lo * (3f - 2f * lo));
            }));
            mouthMat = ShrineKit.Additive(ShrineKit.SoftTex("ScrignoBocca", 32, 32, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float e = Mathf.Clamp01(1f - r);
                return e * e * 0.9f;
            }));
        }

        void Build()
        {
            ShrineKit.Mats();
            Materials();
            var wood = ShrineKit.Wood; var iron = ShrineKit.Iron; var brass = ShrineKit.Brass;
            heapMat = ShrineKit.EmitOwn(HeapColor, 0.1f);
            heapMat.color = new Color(0.50f, 0.29f, 0.06f);          // oro scuro che prende luce: con la base nera il cumulo era una chiazza gialla piatta
            heapMat.SetFloat("_Metallic", 0.3f);
            heapMat.SetFloat("_Smoothness", 0.5f);
            keyMat = ShrineKit.EmitOwn(Amber, 3f);

            // ---- la cassa: pavimento e quattro pareti (vuota dentro), foderata di velluto
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Fondo", wood, new Vector3(0f, 0.035f, 0f), new Vector3(1.0f, 0.07f, 0.62f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "ParetePosteriore", wood, new Vector3(0f, 0.25f, -0.29f), new Vector3(1.0f, 0.5f, 0.04f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "ParetePrincipale", wood, new Vector3(0f, 0.25f, 0.29f), new Vector3(1.0f, 0.5f, 0.04f));
            foreach (float x in new[] { -0.48f, 0.48f })
                ShrineKit.Prim(transform, PrimitiveType.Cube, "ParetePiccola", wood, new Vector3(x, 0.25f, 0f), new Vector3(0.04f, 0.5f, 0.54f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "FoderaDietro", velvet, new Vector3(0f, 0.285f, -0.267f), new Vector3(0.92f, 0.43f, 0.006f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "FoderaDavanti", velvet, new Vector3(0f, 0.285f, 0.267f), new Vector3(0.92f, 0.43f, 0.006f));
            foreach (float x in new[] { -0.457f, 0.457f })
                ShrineKit.Prim(transform, PrimitiveType.Cube, "FoderaLato", velvet, new Vector3(x, 0.285f, 0f), new Vector3(0.006f, 0.43f, 0.528f));
            // ferri: zoccolo, collare sul bordo, spigoli, due fasce verticali davanti e dietro
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Zoccolo", iron, new Vector3(0f, 0.03f, 0f), new Vector3(1.03f, 0.06f, 0.65f));
            foreach (float z in new[] { -0.316f, 0.316f })
                ShrineKit.Prim(transform, PrimitiveType.Cube, "Collare", iron, new Vector3(0f, 0.485f, z), new Vector3(1.02f, 0.03f, 0.012f));
            foreach (float x in new[] { -0.506f, 0.506f })
                ShrineKit.Prim(transform, PrimitiveType.Cube, "Collare", iron, new Vector3(x, 0.485f, 0f), new Vector3(0.012f, 0.03f, 0.62f));
            foreach (float x in new[] { -0.4825f, 0.4825f })
                foreach (float z in new[] { -0.2925f, 0.2925f })
                    ShrineKit.Prim(transform, PrimitiveType.Cube, "Spigolo", iron, new Vector3(x, 0.25f, z), new Vector3(0.045f, 0.5f, 0.045f));
            foreach (float x in new[] { -0.30f, 0.30f })
                foreach (float z in new[] { -0.315f, 0.315f })
                    ShrineKit.Prim(transform, PrimitiveType.Cube, "Fascia", iron, new Vector3(x, 0.25f, z), new Vector3(0.075f, 0.5f, 0.012f));
            // la piastra della serratura, con il buco della chiave che brilla
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Piastra", brass, new Vector3(0f, 0.40f, 0.322f), new Vector3(0.13f, 0.17f, 0.03f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Serratura", keyMat, new Vector3(0f, 0.385f, 0.343f), new Vector3(0.035f, 0.075f, 0.012f));
            // le bandelle della cerniera, fissate dietro
            foreach (float x in new[] { -0.25f, 0.25f })
                ShrineKit.Prim(transform, PrimitiveType.Cube, "Bandella", brass, new Vector3(x, 0.455f, HingeZ - 0.004f), new Vector3(0.10f, 0.075f, 0.012f));

            // ---- il tesoro: un cumulo che arriva appena sopra il bordo, con monete e gemme
            treasure = new GameObject("Tesoro").transform;                      // acceso solo a coperchio aperto: chiuso non si vede, non va disegnato
            treasure.SetParent(transform, false);
            var heap = ShrineKit.LatheObj(treasure, "Cumulo", heapMat, ShrineKit.Lathe("scrignoCumulo", HeapProfile, 24));
            heap.transform.localScale = new Vector3(1f, 1f, 0.58f);
            var rnd = new System.Random(Mathf.RoundToInt(transform.position.x * 37f) * 7919 + Mathf.RoundToInt(transform.position.z * 53f));
            float Rf(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int i = 0; i < 38; i++)
            {
                float a = Rf(0f, Mathf.PI * 2f), rr = Mathf.Sqrt(Rf(0f, 1f));
                float x = Mathf.Cos(a) * rr * 0.42f, z = Mathf.Sin(a) * rr * 0.24f;
                float y = HeapY(Mathf.Sqrt(x * x + (z / 0.58f) * (z / 0.58f))) + 0.010f;
                ShrineKit.Prim(treasure, PrimitiveType.Cylinder, "Moneta", i % 3 == 0 ? coinB : coinA, new Vector3(x, y, z), new Vector3(0.068f, 0.004f, 0.068f), new Vector3(Rf(-30f, 30f), Rf(0f, 180f), Rf(-30f, 30f)));
            }
            var gems = new[] { new Color(1f, 0.10f, 0.16f), new Color(0.12f, 0.95f, 0.40f), new Color(0.25f, 0.45f, 1f), new Color(0.75f, 0.30f, 1f), new Color(1f, 0.10f, 0.16f), new Color(0.25f, 0.45f, 1f), new Color(0.12f, 0.95f, 0.40f) };
            for (int i = 0; i < gems.Length; i++)
            {
                float a = Rf(0f, Mathf.PI * 2f), rr = Mathf.Sqrt(Rf(0.05f, 0.8f));
                float x = Mathf.Cos(a) * rr * 0.36f, z = Mathf.Sin(a) * rr * 0.21f;
                float y = HeapY(Mathf.Sqrt(x * x + (z / 0.58f) * (z / 0.58f))) + 0.018f;
                ShrineKit.Prim(treasure, PrimitiveType.Sphere, "Gemma", ShrineKit.Emit(gems[i], 2.4f), new Vector3(x, y, z), new Vector3(0.050f, 0.040f, 0.050f), new Vector3(Rf(0f, 40f), Rf(0f, 180f), Rf(0f, 40f)));
            }

            // ---- il coperchio a botte, incernierato sul bordo posteriore: legno fuori, velluto dentro, due cerchi di ferro, fermaglio
            var hinge = new GameObject("Cerniera");
            hinge.transform.SetParent(transform, false);
            hinge.transform.localPosition = new Vector3(0f, HingeY, HingeZ);
            lid = hinge.transform;
            var vol = ShrineKit.LatheObj(lid, "Volta", wood, ShrineKit.Vault("scrignoVolta", 1.04f, R, 0.03f, R));
            vol.GetComponent<MeshRenderer>().sharedMaterials = new[] { wood, velvet };
            foreach (float x in new[] { -0.30f, 0.30f })
            {
                var hoop = ShrineKit.LatheObj(lid, "Cerchio", iron, ShrineKit.Vault("scrignoCerchio", 0.08f, R + 0.012f, 0.027f, R));
                hoop.transform.localPosition = new Vector3(x, 0f, 0f);
                hoop.GetComponent<MeshRenderer>().sharedMaterials = new[] { iron, iron };
            }
            foreach (float x in new[] { -0.25f, 0.25f })
                ShrineKit.Prim(lid, PrimitiveType.Cylinder, "Nocca", brass, new Vector3(x, 0f, 0f), new Vector3(0.045f, 0.055f, 0.045f), new Vector3(0f, 0f, 90f));
            var cl = new GameObject("Fermaglio");
            cl.transform.SetParent(lid, false);
            cl.transform.localPosition = new Vector3(0f, 0.015f, 2f * R + 0.035f);
            clasp = cl.transform;
            ShrineKit.Prim(clasp, PrimitiveType.Cube, "Linguetta", brass, new Vector3(0f, -0.045f, 0f), new Vector3(0.07f, 0.09f, 0.016f));

            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.34f, 0f);
            col.size = new Vector3(1.04f, 0.68f, 0.66f);

            // ---- il fascio di luce (tre piani verticali incrociati), il bagliore sulla bocca e la polvere d'oro: spenti finche' e' chiuso
            shaftMpb = new MaterialPropertyBlock();
            mouthMpb = new MaterialPropertyBlock();
            shaft = new GameObject("Fascio").transform;
            shaft.SetParent(transform, false);
            shaft.localPosition = new Vector3(0f, 0.52f, 0f);
            shaftPlanes = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Piano" + (i + 1);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(shaft, false);
                q.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                q.transform.localRotation = Quaternion.Euler(0f, 60f * i, 0f);
                q.transform.localScale = new Vector3(1.05f, 2.0f, 1f);
                var qr = q.GetComponent<MeshRenderer>();
                qr.sharedMaterial = shaftMat;
                qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                qr.receiveShadows = false;
                shaftPlanes[i] = qr;
            }
            shaft.gameObject.SetActive(false);
            var mo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mo.name = "Bocca";
            Destroy(mo.GetComponent<Collider>());
            mo.transform.SetParent(transform, false);
            mo.transform.localPosition = new Vector3(0f, 0.585f, 0f);
            mo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            mo.transform.localScale = new Vector3(1.25f, 0.9f, 1f);
            mouth = mo.GetComponent<MeshRenderer>();
            mouth.sharedMaterial = mouthMat;
            mouth.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mouth.receiveShadows = false;
            mouth.enabled = false;
            const int N = 14;
            motes = new Transform[N]; moteSpeed = new float[N]; moteSeed = new float[N]; moteRad = new float[N];
            var moteMat = ShrineKit.Emit(new Color(1f, 0.80f, 0.36f), 3.2f);
            for (int i = 0; i < N; i++)
            {
                motes[i] = ShrineKit.Prim(transform, PrimitiveType.Sphere, "Scintilla", moteMat, Vector3.zero, Vector3.one * 0.015f).transform;
                moteSpeed[i] = Rf(0.22f, 0.50f); moteSeed[i] = Rf(0f, 1f); moteRad[i] = Rf(0.10f, 0.34f);
                motes[i].gameObject.SetActive(false);
            }

            var sr = new GameObject("ArmaGira");
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = new Vector3(0f, ShowY, 0f);
            spinRoot = sr.transform;
            // in alto e un po' dietro: a 25 cm dal legno lo bruciava fino all'arancione piatto, a 30 cm dall'arma in mostra la faceva bianca
            glow = ShrineKit.PointLight(transform, "Luce", new Vector3(0f, 1.7f, -0.3f), Amber, ClosedGlow, 5.2f, 0.18f, 2.0f);
            angle = 0f;
            Pose(0f);
        }
    }
}
