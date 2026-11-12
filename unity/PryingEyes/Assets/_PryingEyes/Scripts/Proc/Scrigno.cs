using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Lo Scrigno (la scatola misteriosa dei Black Ops Zombies): un forziere di legno con le fasce di ferro. Per 950 punti il
    /// coperchio si alza e dentro girano, sempre piu' lente, le armi di tutti e cinque i gradi; si ferma su una a caso (piu' si scende,
    /// piu' pesano i gradi alti, mai quelle che hai gia' ne' la pistola di partenza). Hai nove secondi per prenderla con F: se non lo
    /// fai si richiude e i punti sono persi. A tasche piene sostituisce l'arma in mano.</summary>
    public sealed class Scrigno : MonoBehaviour, IInteract
    {
        public const int Price = 950;
        public enum Phase { Closed, Opening, Offering, Closing }
        public static Scrigno Current;

        public Phase phase = Phase.Closed;
        public int uses;
        public int OfferedWeapon => offered;
        public float rollTime = 3.2f, offerTime = 9f;
        Transform lid, spinRoot;
        GameObject preview;
        Light glow;
        int offered = -1;
        float lidK, spin;
        Renderer interior;

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
                phase = Phase.Closing;       // l'offerta finisce: il giro la vede e richiude
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
                if (i == start || (w != null && w.Owns(i))) continue;
                float wt = GradeWeight(WeaponDefs.All[i].grade, floor);
                pool.Add(i); weights.Add(wt); sum += wt;
            }
            if (pool.Count == 0) return start;
            float r = Random.value * sum;
            for (int k = 0; k < pool.Count; k++) { r -= weights[k]; if (r <= 0f) return pool[k]; }
            return pool[pool.Count - 1];
        }

        // ------------------------------------------------------------------ il giro
        IEnumerator Run()
        {
            phase = Phase.Opening;
            Sfx.At(SoundBank.ChestOpen, transform.position, 0.95f, 1f, 3f, 45f);
            for (float t = 0f; t < 1.3f; t += Time.deltaTime) { SetLid(Mathf.SmoothStep(0f, 1f, t / 1.3f)); yield return null; }
            SetLid(1f);
            if (interior != null) interior.enabled = true;
            if (glow != null) glow.intensity = 2.2f;
            float el = 0f, next = 0f;
            int shown = -1;
            while (el < rollTime)
            {
                el += Time.deltaTime;
                if (el >= next)
                {
                    int pick;
                    do pick = Random.Range(0, WeaponDefs.All.Length); while (pick == shown || pick == offered);
                    shown = pick;
                    ShowWeapon(shown);
                    Sfx.UI(SoundBank.DropTick, 0.4f, Mathf.Lerp(1.25f, 0.8f, el / rollTime));
                    float k = el / rollTime;
                    next = el + Mathf.Lerp(0.07f, 0.45f, k * k);
                }
                spin += 140f * Time.deltaTime;
                if (spinRoot != null) spinRoot.localRotation = Quaternion.Euler(0f, spin, 0f);
                yield return null;
            }
            ShowWeapon(offered);
            Sfx.UI(SoundBank.ChestReveal, 0.75f);
            phase = Phase.Offering;
            float left = offerTime;
            while (phase == Phase.Offering && left > 0f)
            {
                left -= Time.deltaTime;
                spin += 60f * Time.deltaTime;
                if (spinRoot != null)
                {
                    spinRoot.localRotation = Quaternion.Euler(0f, spin, 0f);
                    spinRoot.localPosition = new Vector3(0f, 0.95f + 0.05f * Mathf.Sin(Time.time * 2.4f), 0f);
                    bool vis = left > 2.5f || Mathf.Repeat(Time.time * 5f, 1f) < 0.65f;
                    if (preview != null && preview.activeSelf != vis) preview.SetActive(vis);
                }
                yield return null;
            }
            if (phase == Phase.Offering) GameManager.I?.hud?.Toast("Lo Scrigno si richiude: l'arma è sparita.");
            phase = Phase.Closing;
            if (preview != null) Destroy(preview);
            preview = null;
            for (float t = 0f; t < 0.9f; t += Time.deltaTime) { SetLid(1f - Mathf.SmoothStep(0f, 1f, t / 0.9f)); yield return null; }
            SetLid(0f);
            if (interior != null) interior.enabled = false;
            if (glow != null) glow.intensity = 0.55f;
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

        void SetLid(float k)
        {
            lidK = k;
            if (lid != null) lid.localRotation = Quaternion.Euler(-108f * k, 0f, 0f);
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

        void OnDestroy() { Interact.Remove(this); if (Current == this) Current = null; }

        void Build()
        {
            var wood = ShrineKit.Wood; var iron = ShrineKit.Iron; var brass = ShrineKit.Brass;
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Cassa", wood, new Vector3(0f, 0.25f, 0f), new Vector3(1.0f, 0.5f, 0.62f));
            foreach (float x in new[] { -0.33f, 0.33f })
                ShrineKit.Prim(transform, PrimitiveType.Cube, "Fascia", iron, new Vector3(x, 0.25f, 0f), new Vector3(0.075f, 0.52f, 0.645f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Zoccolo", iron, new Vector3(0f, 0.03f, 0f), new Vector3(1.03f, 0.06f, 0.65f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Bordo", iron, new Vector3(0f, 0.495f, 0f), new Vector3(1.02f, 0.03f, 0.64f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Piastra", brass, new Vector3(0f, 0.40f, 0.322f), new Vector3(0.13f, 0.17f, 0.03f));
            ShrineKit.Prim(transform, PrimitiveType.Cube, "Serratura", ShrineKit.Emit(new Color(1f, 0.62f, 0.12f), 3f), new Vector3(0f, 0.395f, 0.340f), new Vector3(0.035f, 0.075f, 0.012f));
            // l'interno: una lastra che brilla d'ambra, nascosta finche' il coperchio e' chiuso
            var inn = ShrineKit.Prim(transform, PrimitiveType.Cube, "Interno", ShrineKit.Emit(new Color(1f, 0.58f, 0.14f), 2.2f), new Vector3(0f, 0.505f, 0f), new Vector3(0.9f, 0.01f, 0.5f));
            interior = inn.GetComponent<Renderer>();
            interior.enabled = false;
            // coperchio a botte, incernierato sul bordo posteriore
            var hinge = new GameObject("Cerniera");
            hinge.transform.SetParent(transform, false);
            hinge.transform.localPosition = new Vector3(0f, 0.5f, -0.31f);
            lid = hinge.transform;
            ShrineKit.Prim(lid, PrimitiveType.Cylinder, "Botte", wood, new Vector3(0f, 0f, 0.31f), new Vector3(0.62f, 0.5f, 0.62f), new Vector3(0f, 0f, 90f));
            foreach (float x in new[] { -0.33f, 0.33f })
                ShrineKit.Prim(lid, PrimitiveType.Cylinder, "Cerchio", iron, new Vector3(x, 0f, 0.31f), new Vector3(0.645f, 0.040f, 0.645f), new Vector3(0f, 0f, 90f));
            ShrineKit.Prim(lid, PrimitiveType.Cube, "Fermaglio", brass, new Vector3(0f, 0.015f, 0.625f), new Vector3(0.07f, 0.11f, 0.025f));
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.34f, 0f);
            col.size = new Vector3(1.04f, 0.68f, 0.66f);
            var sr = new GameObject("ArmaGira");
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            spinRoot = sr.transform;
            glow = ShrineKit.PointLight(transform, "Luce", new Vector3(0f, 0.75f, 0f), new Color(1f, 0.62f, 0.2f), 0.55f, 4.8f, 0.18f, 2.0f);
        }
    }
}
