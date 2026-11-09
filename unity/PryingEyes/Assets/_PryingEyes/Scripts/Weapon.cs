using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace PryingEyes
{
    /// <summary>Definizione di un'arma acquistabile sui muri o di partenza.</summary>
    [System.Serializable]
    public class WeaponDef
    {
        public string name;
        public int magSize, reserve, reserveMax;
        public float rpm, damage, range, spread, reloadTime, shotRecoil;
        public int pellets;
        public bool auto;
        public float headMult;
        public string model;      // rifle / smg / shotgun / dmr
        public string modeLabel;  // AUTO / SEMI / PUMP
    }

    /// <summary>Hitscan multi-arma: RPM, caricatore/riserva, ricarica, rinculo, muzzle flash,
    /// viewmodel procedurale per arma. Le armi si comprano sui muri con i punti (WallBuy.cs).</summary>
    public class Weapon : MonoBehaviour
    {
        // 0 = arma di partenza, 1..3 = armi sui muri
        public static readonly WeaponDef[] Defs =
        {
            new WeaponDef { name = "AR-9 MALLEUS", magSize = 30, reserve = 120, reserveMax = 240, rpm = 700f,
                damage = 30f, range = 120f, spread = 0.006f, reloadTime = 1.7f, shotRecoil = 0.06f, pellets = 1,
                auto = true, headMult = 2f, model = "rifle", modeLabel = "AUTO" },
            new WeaponDef { name = "VESPER-9", magSize = 40, reserve = 200, reserveMax = 320, rpm = 850f,
                damage = 19f, range = 90f, spread = 0.012f, reloadTime = 1.5f, shotRecoil = 0.045f, pellets = 1,
                auto = true, headMult = 2f, model = "smg", modeLabel = "AUTO" },
            new WeaponDef { name = "BREACHER-12", magSize = 8, reserve = 48, reserveMax = 80, rpm = 75f,
                damage = 14f, range = 45f, spread = 0.035f, reloadTime = 2.4f, shotRecoil = 0.14f, pellets = 8,
                auto = true, headMult = 1.6f, model = "shotgun", modeLabel = "PUMP" },
            new WeaponDef { name = "LANCE-7", magSize = 12, reserve = 72, reserveMax = 120, rpm = 260f,
                damage = 70f, range = 140f, spread = 0.002f, reloadTime = 2.0f, shotRecoil = 0.10f, pellets = 1,
                auto = false, headMult = 2.5f, model = "dmr", modeLabel = "SEMI" },
        };

        [Header("Tuning (sincronizzati con l'arma equipaggiata)")]
        public int weaponIndex = 0;
        public int magSize = 30;
        public int mag = 30;
        public int reserve = 120;
        public int reserveMax = 240;
        public float rpm = 700f;
        public float damage = 30f;
        public float range = 120f;

        [Header("State")]
        public bool reloading = false;
        public float ads = 0f;
        public float recoil = 0f;
        float lastShot = -99f, reloadT = 0f;
        float adsSightY = 0.10f;   // altezza del mirino: in ADS la arma si alza fino a centrarlo
        float equipT;              // animazione di entrata dell'arma
        bool triggerHeld;
        Transform magPart, chargePart, pumpPart;
        Vector3 magRest, chargeRest, pumpRest;
        static Material flashRuntime;

        [Header("Viewmodel materials (assigned by HotelBuilder)")]
        public Material bodyMat;
        public Material gripMat;
        public Material flashMat;

        Transform cam;
        Transform viewRoot;
        GameObject flashQuad;
        Light flashLight;
        HUD hud;
        bool adsHeld;

        public WeaponDef Def => Defs[Mathf.Clamp(weaponIndex, 0, Defs.Length - 1)];
        public int CurrentIndex => weaponIndex;

        public void Bind(Transform camera, HUD h)
        {
            cam = camera;
            hud = h;
            BuildViewmodel();
        }

        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>Fiammata procedurale a stella (niente quadrato arancione).</summary>
        static Material FlashRuntimeMat()
        {
            if (flashRuntime != null) return flashRuntime;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float nx = (x + 0.5f) / S * 2f - 1f;
                    float ny = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float core = Mathf.Clamp01(1f - r * 1.7f);
                    float ang = Mathf.Atan2(ny, nx);
                    float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(2f * ang)), 10f) * Mathf.Clamp01(1.15f - r * 1.15f);
                    float a = Mathf.Clamp01(core * 1.25f + spike * 0.9f);
                    Color c = Color.Lerp(new Color(1f, 0.42f, 0.08f), new Color(1f, 0.98f, 0.85f), core);
                    px[y * S + x] = new Color(c.r, c.g, c.b, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false);

            flashRuntime = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            flashRuntime.SetTexture("_BaseMap", tex);
            flashRuntime.SetTexture("_MainTex", tex);
            flashRuntime.SetColor("_BaseColor", Color.white);
            flashRuntime.SetFloat("_Surface", 1f);
            flashRuntime.SetFloat("_Blend", 2f);   // additivo
            flashRuntime.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            flashRuntime.SetFloat("_DstBlend", (float)BlendMode.One);
            flashRuntime.SetFloat("_ZWrite", 0f);
            flashRuntime.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            flashRuntime.renderQueue = (int)RenderQueue.Transparent;
            return flashRuntime;
        }

        void BuildViewmodel()
        {
            if (cam == null) return;
            if (viewRoot != null) { SafeDestroy(viewRoot.gameObject); viewRoot = null; }
            // pulizia dei viewmodel rimasti nella scena (ricostruita dall'editor)
            for (int i = cam.childCount - 1; i >= 0; i--)
            {
                var c = cam.GetChild(i);
                if (c != null && c.name == "Viewmodel") SafeDestroy(c.gameObject);
            }
            if (bodyMat == null || gripMat == null || flashMat == null)
            {
                Debug.LogError("[PryingEyes] Weapon materials missing - viewmodel skipped");
                return;
            }

            var d = Def;
            if (Application.isPlaying)
            {
                // PBR procedurale: metallo usurato, acciaio, polimero o legno (nessun asset scaricato)
                GunMats.Ensure();
                WeaponRef.Body = GunMats.Metal;
                WeaponRef.Steel = GunMats.Steel;
                WeaponRef.Dark = GunMats.Dark;
                WeaponRef.Lens = GunMats.Lens;
                WeaponRef.Dot = GunMats.Dot;
                WeaponRef.Grip = d.model == "shotgun" ? GunMats.Wood : GunMats.Polymer;
            }
            else
            {
                // in editor (build della scena) bastano i materiali serializzati
                WeaponRef.Body = bodyMat;
                WeaponRef.Steel = bodyMat;
                WeaponRef.Dark = bodyMat;
                WeaponRef.Lens = bodyMat;
                WeaponRef.Dot = bodyMat;
                WeaponRef.Grip = gripMat;
            }
            var root = new GameObject("Viewmodel");
            root.layer = 2;
            root.transform.SetParent(cam, false);
            root.transform.localPosition = new Vector3(0.28f, -0.26f, 0.62f);
            root.transform.localRotation = Quaternion.identity;
            viewRoot = root.transform;

            Vector3 muzzle;
            float sightY;
            WeaponRef.Mag = WeaponRef.Charge = WeaponRef.Pump = null;
            switch (d.model)
            {
                case "smg": BuildSmg(root, out muzzle, out sightY); break;
                case "shotgun": BuildShotgun(root, out muzzle, out sightY); break;
                case "dmr": BuildDmr(root, out muzzle, out sightY); break;
                default: BuildRifle(root, out muzzle, out sightY); break;
            }
            adsSightY = sightY;
            magPart = WeaponRef.Mag; chargePart = WeaponRef.Charge; pumpPart = WeaponRef.Pump;
            if (magPart != null) magRest = magPart.localPosition;
            if (chargePart != null) chargeRest = chargePart.localPosition;
            if (pumpPart != null) pumpRest = pumpPart.localPosition;
            equipT = 0.30f;

            flashQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            flashQuad.name = "Flash";
            NoPhys(flashQuad);
            flashQuad.transform.SetParent(root.transform, false);
            flashQuad.transform.localPosition = muzzle;
            flashQuad.transform.localScale = new Vector3(0.13f, 0.13f, 1f);
            flashQuad.GetComponent<MeshRenderer>().material =
                Application.isPlaying ? FlashRuntimeMat() : flashMat;
            flashQuad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flashQuad.SetActive(false);

            flashLight = root.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.6f, 0.25f);
            flashLight.intensity = 0f;
            flashLight.range = 6f;

            // luce dedicata all'arma (la hall e' buia e il viewmodel metallico sparirebbe)
            var fill = new GameObject("ViewmodelFill").AddComponent<Light>();
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.18f, 0.32f, 0.15f);
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.95f, 0.88f);
            fill.intensity = 1.15f;
            fill.range = 1.5f;
            fill.shadows = LightShadows.None;
        }

        /// <summary>Modello dettagliato per l'esposizione sul muro (stessa geometria del viewmodel,
        /// materiali PBR procedurali, senza mani né fiammata).</summary>
        public static GameObject BuildDisplayModel(int index)
        {
            GunMats.Ensure();
            // copie schiarite solo per l'esposizione: sul muro la luce e' poca
            var displayBody = new Material(GunMats.Metal) { color = new Color(1.8f, 1.85f, 1.95f) };
            var displaySteel = new Material(GunMats.Steel) { color = new Color(2.0f, 2.05f, 2.15f) };
            displayBody.SetFloat("_Smoothness", 0.48f);
            displaySteel.SetFloat("_Smoothness", 0.55f);
            WeaponRef.Body = displayBody;
            WeaponRef.Steel = displaySteel;
            WeaponRef.Dark = GunMats.Dark;
            WeaponRef.Lens = GunMats.Lens;
            WeaponRef.Dot = GunMats.Dot;
            WeaponRef.Grip = Defs[index].model == "shotgun" ? GunMats.Wood : GunMats.Polymer;
            WeaponRef.Mag = WeaponRef.Charge = WeaponRef.Pump = null;
            var g = new GameObject("Arma");
            Vector3 muzzle; float sightY;
            switch (Defs[index].model)
            {
                case "smg": BuildSmg(g, out muzzle, out sightY); break;
                case "shotgun": BuildShotgun(g, out muzzle, out sightY); break;
                case "dmr": BuildDmr(g, out muzzle, out sightY); break;
                default: BuildRifle(g, out muzzle, out sightY); break;
            }
            return g;
        }

        // ---------- viewmodel: geometrie e mirini distinti per arma ----------
        static void BuildRifle(GameObject root, out Vector3 muzzle, out float sightY)
        {
            var body = WeaponRef.Body;
            var steel = WeaponRef.Steel;
            var dark = WeaponRef.Dark;
            var grip = WeaponRef.Grip;
            AddEllipsoid(root, "Body", new Vector3(0.10f, 0.12f, 0.43f), new Vector3(0, 0, 0), body);
            AddEllipsoid(root, "Upper", new Vector3(0.085f, 0.05f, 0.36f), new Vector3(0, 0.077f, 0.02f), dark);
            AddCapsule(root, "Handguard", 0.0375f, 0.26f, new Vector3(0, 0.005f, 0.31f), steel);
            for (int i = 0; i < 3; i++)
            {
                AddBox(root, "VentL", new Vector3(0.004f, 0.028f, 0.05f), new Vector3(-0.039f, 0.01f, 0.22f + i * 0.09f), dark);
                AddBox(root, "VentR", new Vector3(0.004f, 0.028f, 0.05f), new Vector3(0.039f, 0.01f, 0.22f + i * 0.09f), dark);
            }
            AddCyl(root, "Barrel", 0.016f, 0.30f, new Vector3(0, 0.015f, 0.52f), steel);
            AddCyl(root, "Brake", 0.030f, 0.06f, new Vector3(0, 0.015f, 0.69f), steel);
            for (int i = 0; i < 3; i++)
                AddBox(root, "BrakePort", new Vector3(0.062f, 0.006f, 0.012f), new Vector3(0, 0.015f, 0.672f + i * 0.018f), dark);
            AddCapsule(root, "Grip", 0.031f, 0.16f, new Vector3(0, -0.12f, 0.10f), grip, true, 12f);
            AddBox(root, "TriggerGuard", new Vector3(0.05f, 0.008f, 0.09f), new Vector3(0, -0.145f, 0.02f), dark);
            AddBox(root, "Trigger", new Vector3(0.008f, 0.03f, 0.012f), new Vector3(0, -0.115f, 0.03f), steel);
            var magG = new GameObject("MagGroup").transform;
            magG.SetParent(root.transform, false);
            magG.localPosition = new Vector3(0, -0.135f, -0.02f);
            AddCapsule(magG.gameObject, "Mag", 0.029f, 0.18f, Vector3.zero, grip, true);
            for (int i = 0; i < 3; i++)
                AddBox(magG.gameObject, "MagRib", new Vector3(0.057f, 0.006f, 0.087f), new Vector3(0, 0.06f - i * 0.035f, 0), dark);
            AddBox(magG.gameObject, "MagBase", new Vector3(0.062f, 0.016f, 0.092f), new Vector3(0, -0.093f, 0), dark);
            WeaponRef.Mag = magG;
            AddCapsule(root, "Stock", 0.038f, 0.19f, new Vector3(0, -0.01f, -0.28f), grip);
            AddBox(root, "Cheek", new Vector3(0.055f, 0.022f, 0.10f), new Vector3(0, 0.048f, -0.30f), grip);
            AddBox(root, "ButtPad", new Vector3(0.075f, 0.11f, 0.03f), new Vector3(0, -0.01f, -0.378f), dark);
            AddBox(root, "EjectPort", new Vector3(0.006f, 0.032f, 0.09f), new Vector3(0.046f, 0.02f, 0.04f), dark);
            var chG = new GameObject("ChargeGroup").transform;
            chG.SetParent(root.transform, false);
            chG.localPosition = new Vector3(0.03f, 0.06f, -0.18f);
            AddBox(chG.gameObject, "Charge", new Vector3(0.03f, 0.018f, 0.06f), Vector3.zero, steel);
            WeaponRef.Charge = chG;
            for (int i = 0; i < 6; i++)
                AddBox(root, "Rail", new Vector3(0.055f, 0.008f, 0.032f), new Vector3(0, 0.104f, -0.07f + i * 0.045f), dark);

            // red dot: base, gabbia, lente, punto
            AddBox(root, "DotBase", new Vector3(0.06f, 0.012f, 0.085f), new Vector3(0, 0.113f, 0.05f), dark);
            AddBox(root, "DotL", new Vector3(0.012f, 0.06f, 0.08f), new Vector3(-0.026f, 0.147f, 0.05f), dark);
            AddBox(root, "DotR", new Vector3(0.012f, 0.06f, 0.08f), new Vector3(0.026f, 0.147f, 0.05f), dark);
            AddBox(root, "DotTop", new Vector3(0.064f, 0.012f, 0.08f), new Vector3(0, 0.177f, 0.05f), dark);
            AddBox(root, "DotLens", new Vector3(0.052f, 0.055f, 0.006f), new Vector3(0, 0.147f, 0.093f), WeaponRef.Lens);
            AddSphere(root, "Dot", 0.0042f, new Vector3(0, 0.147f, 0.099f), WeaponRef.Dot);
            sightY = 0.147f;
            muzzle = new Vector3(0, 0.015f, 0.72f);
        }

        static void BuildSmg(GameObject root, out Vector3 muzzle, out float sightY)
        {
            var body = WeaponRef.Body;
            var steel = WeaponRef.Steel;
            var dark = WeaponRef.Dark;
            var grip = WeaponRef.Grip;
            AddEllipsoid(root, "Body", new Vector3(0.09f, 0.11f, 0.31f), Vector3.zero, body);
            AddEllipsoid(root, "Upper", new Vector3(0.075f, 0.045f, 0.26f), new Vector3(0, 0.068f, 0), dark);
            AddCapsule(root, "Handguard", 0.0325f, 0.18f, new Vector3(0, 0, 0.20f), steel);
            for (int i = 0; i < 2; i++)
            {
                AddBox(root, "VentL", new Vector3(0.004f, 0.024f, 0.05f), new Vector3(-0.034f, 0.005f, 0.14f + i * 0.09f), dark);
                AddBox(root, "VentR", new Vector3(0.004f, 0.024f, 0.05f), new Vector3(0.034f, 0.005f, 0.14f + i * 0.09f), dark);
            }
            AddCyl(root, "Barrel", 0.015f, 0.22f, new Vector3(0, 0.012f, 0.36f), steel);
            AddCyl(root, "Shroud", 0.024f, 0.10f, new Vector3(0, 0.012f, 0.30f), dark);
            var magG = new GameObject("MagGroup").transform;
            magG.SetParent(root.transform, false);
            magG.localPosition = new Vector3(0, -0.13f, -0.02f);
            AddCapsule(magG.gameObject, "Mag", 0.027f, 0.18f, Vector3.zero, grip, true);
            AddBox(magG.gameObject, "MagBase", new Vector3(0.056f, 0.015f, 0.076f), new Vector3(0, -0.092f, 0), dark);
            WeaponRef.Mag = magG;
            AddCapsule(root, "Grip", 0.028f, 0.12f, new Vector3(0, -0.09f, 0.10f), grip, true, 12f);
            AddBox(root, "TriggerGuard", new Vector3(0.045f, 0.008f, 0.08f), new Vector3(0, -0.115f, 0.02f), dark);
            AddCapsule(root, "Stock", 0.032f, 0.17f, new Vector3(0, -0.01f, -0.20f), dark);
            AddBox(root, "EjectPort", new Vector3(0.006f, 0.026f, 0.07f), new Vector3(0.041f, 0.018f, 0.04f), dark);
            var chG = new GameObject("ChargeGroup").transform;
            chG.SetParent(root.transform, false);
            chG.localPosition = new Vector3(0.03f, 0.055f, -0.13f);
            AddBox(chG.gameObject, "Charge", new Vector3(0.025f, 0.015f, 0.05f), Vector3.zero, steel);
            WeaponRef.Charge = chG;
            for (int i = 0; i < 5; i++)
                AddBox(root, "Rail", new Vector3(0.05f, 0.007f, 0.03f), new Vector3(0, 0.095f, -0.06f + i * 0.042f), dark);

            // reflex compatto
            AddBox(root, "RefBase", new Vector3(0.056f, 0.012f, 0.08f), new Vector3(0, 0.104f, 0.04f), dark);
            AddBox(root, "RefL", new Vector3(0.011f, 0.055f, 0.075f), new Vector3(-0.024f, 0.134f, 0.04f), dark);
            AddBox(root, "RefR", new Vector3(0.011f, 0.055f, 0.075f), new Vector3(0.024f, 0.134f, 0.04f), dark);
            AddBox(root, "RefTop", new Vector3(0.058f, 0.011f, 0.075f), new Vector3(0, 0.162f, 0.04f), dark);
            AddBox(root, "RefLens", new Vector3(0.05f, 0.052f, 0.006f), new Vector3(0, 0.134f, 0.08f), WeaponRef.Lens);
            AddSphere(root, "Dot", 0.0046f, new Vector3(0, 0.134f, 0.086f), WeaponRef.Dot);
            sightY = 0.134f;
            muzzle = new Vector3(0, 0.012f, 0.48f);
        }

        static void BuildShotgun(GameObject root, out Vector3 muzzle, out float sightY)
        {
            var body = WeaponRef.Body;
            var steel = WeaponRef.Steel;
            var dark = WeaponRef.Dark;
            var wood = WeaponRef.Grip;
            AddEllipsoid(root, "Body", new Vector3(0.09f, 0.12f, 0.37f), new Vector3(0, 0, 0.02f), body);
            AddBox(root, "Top", new Vector3(0.085f, 0.025f, 0.30f), new Vector3(0, 0.065f, 0.03f), steel);
            AddCyl(root, "Barrel", 0.023f, 0.55f, new Vector3(0, 0.025f, 0.44f), steel);
            AddCyl(root, "BarrelRib", 0.026f, 0.48f, new Vector3(0, 0.028f, 0.42f), dark);
            AddCyl(root, "Tube", 0.016f, 0.50f, new Vector3(0, -0.05f, 0.42f), dark);
            var pumpG = new GameObject("PumpGroup").transform;
            pumpG.SetParent(root.transform, false);
            pumpG.localPosition = new Vector3(0, -0.05f, 0.36f);
            AddCapsule(pumpG.gameObject, "Pump", 0.038f, 0.20f, Vector3.zero, wood);
            for (int i = 0; i < 4; i++)
                AddBox(pumpG.gameObject, "PumpRib", new Vector3(0.078f, 0.006f, 0.012f), new Vector3(0, 0, -0.08f + i * 0.055f), dark);
            WeaponRef.Pump = pumpG;
            AddCapsule(root, "Stock", 0.040f, 0.22f, new Vector3(0, -0.03f, -0.28f), wood);
            AddBox(root, "ButtPad", new Vector3(0.08f, 0.13f, 0.03f), new Vector3(0, -0.03f, -0.40f), dark);
            AddCapsule(root, "Grip", 0.028f, 0.13f, new Vector3(0, -0.10f, 0.06f), wood, true, 10f);
            AddBox(root, "TriggerGuard", new Vector3(0.04f, 0.008f, 0.08f), new Vector3(0, -0.15f, -0.02f), dark);

            // tacca fantasma posteriore + mirino a sfera frontale
            AddBox(root, "GhostBase", new Vector3(0.022f, 0.05f, 0.02f), new Vector3(0, 0.072f, 0.02f), dark);
            AddBox(root, "GhostL", new Vector3(0.008f, 0.05f, 0.008f), new Vector3(-0.024f, 0.105f, 0.02f), dark);
            AddBox(root, "GhostR", new Vector3(0.008f, 0.05f, 0.008f), new Vector3(0.024f, 0.105f, 0.02f), dark);
            AddBox(root, "GhostTop", new Vector3(0.056f, 0.008f, 0.008f), new Vector3(0, 0.13f, 0.02f), dark);
            AddBox(root, "GhostBot", new Vector3(0.056f, 0.008f, 0.008f), new Vector3(0, 0.08f, 0.02f), dark);
            AddBox(root, "FrontPost", new Vector3(0.012f, 0.055f, 0.012f), new Vector3(0, 0.075f, 0.70f), dark);
            AddSphere(root, "Bead", 0.006f, new Vector3(0, 0.105f, 0.70f), WeaponRef.Dot);
            sightY = 0.105f;
            muzzle = new Vector3(0, 0.025f, 0.73f);
        }

        static void BuildDmr(GameObject root, out Vector3 muzzle, out float sightY)
        {
            var body = WeaponRef.Body;
            var steel = WeaponRef.Steel;
            var dark = WeaponRef.Dark;
            var grip = WeaponRef.Grip;
            AddEllipsoid(root, "Body", new Vector3(0.09f, 0.12f, 0.44f), new Vector3(0, 0, 0.02f), body);
            AddEllipsoid(root, "Upper", new Vector3(0.075f, 0.05f, 0.38f), new Vector3(0, 0.077f, 0.04f), dark);
            AddCapsule(root, "Handguard", 0.035f, 0.20f, new Vector3(0, 0.005f, 0.30f), steel);
            AddCyl(root, "Barrel", 0.016f, 0.55f, new Vector3(0, 0.015f, 0.54f), steel);
            AddCyl(root, "Brake", 0.028f, 0.06f, new Vector3(0, 0.015f, 0.82f), steel);
            for (int i = 0; i < 6; i++)
                AddBox(root, "Rail", new Vector3(0.05f, 0.008f, 0.032f), new Vector3(0, 0.104f, -0.04f + i * 0.045f), dark);
            var magG = new GameObject("MagGroup").transform;
            magG.SetParent(root.transform, false);
            magG.localPosition = new Vector3(0, -0.13f, -0.02f);
            AddCapsule(magG.gameObject, "Mag", 0.027f, 0.16f, Vector3.zero, grip, true);
            AddBox(magG.gameObject, "MagBase", new Vector3(0.056f, 0.015f, 0.086f), new Vector3(0, -0.082f, 0), dark);
            WeaponRef.Mag = magG;
            AddCapsule(root, "Grip", 0.028f, 0.13f, new Vector3(0, -0.10f, 0.11f), grip, true, 12f);
            AddBox(root, "TriggerGuard", new Vector3(0.045f, 0.008f, 0.08f), new Vector3(0, -0.13f, 0.02f), dark);
            AddCapsule(root, "Stock", 0.038f, 0.21f, new Vector3(0, -0.02f, -0.30f), grip);
            AddBox(root, "Cheek", new Vector3(0.055f, 0.022f, 0.11f), new Vector3(0, 0.045f, -0.30f), grip);
            AddBox(root, "ButtPad", new Vector3(0.075f, 0.13f, 0.03f), new Vector3(0, -0.02f, -0.40f), dark);

            // ottica con attacchi, lenti e torrette
            AddBox(root, "ScopeMountA", new Vector3(0.045f, 0.05f, 0.04f), new Vector3(0, 0.085f, -0.02f), dark);
            AddBox(root, "ScopeMountB", new Vector3(0.045f, 0.05f, 0.04f), new Vector3(0, 0.085f, 0.14f), dark);
            AddCyl(root, "Scope", 0.035f, 0.32f, new Vector3(0, 0.12f, 0.06f), dark);
            AddCyl(root, "ScopeFront", 0.031f, 0.006f, new Vector3(0, 0.12f, 0.222f), WeaponRef.Lens);
            AddCyl(root, "ScopeRear", 0.031f, 0.006f, new Vector3(0, 0.12f, -0.102f), WeaponRef.Lens);
            AddCyl(root, "TurretTop", 0.014f, 0.03f, new Vector3(0, 0.158f, 0.06f), dark);
            sightY = 0.12f;
            muzzle = new Vector3(0, 0.015f, 0.85f);
        }

        // piccole classi d'appoggio per i materiali usati dai builder statici
        static class WeaponRef
        {
            public static Material Body, Grip, Steel, Dark, Lens, Dot;
            public static Transform Mag, Charge, Pump;
        }

        void Awake()
        {
            if (cam == null)
            {
                var t = transform.Find("MainCamera");
                if (t != null) cam = t;
            }
            // il riferimento all'HUD non e' serializzato nella scena: senza questo lookup
            // colpi e ricarica non arrivavano mai al contatore delle munizioni
            if (hud == null) hud = FindAnyObjectByType<HUD>();
            if (cam != null) BuildViewmodel();
            SyncTuning();
        }

        void SyncTuning()
        {
            var d = Def;
            magSize = d.magSize;
            reserveMax = d.reserveMax;
            rpm = d.rpm;
            damage = d.damage;
            range = d.range;
            mag = Mathf.Clamp(mag, 0, magSize);
            reserve = Mathf.Clamp(reserve, 0, reserveMax);
            hud?.SetAmmo(mag, reserve);
            hud?.SetWeaponName(d.name + " · " + d.modeLabel);
        }

        /// <summary>Equipaggia un'arma (acquisto al muro o reset). full = caricatore e riserva pieni.</summary>
        public void Equip(int index, bool full = true)
        {
            weaponIndex = Mathf.Clamp(index, 0, Defs.Length - 1);
            var d = Def;
            magSize = d.magSize;
            reserveMax = d.reserveMax;
            rpm = d.rpm;
            damage = d.damage;
            range = d.range;
            mag = full ? d.magSize : Mathf.Clamp(mag, 0, d.magSize);
            reserve = full ? d.reserve : Mathf.Clamp(reserve, 0, d.reserveMax);
            reloading = false; reloadT = 0f; triggerHeld = false; recoil = 0f;
            BuildViewmodel();
            hud?.SetReloading(false);
            hud?.SetAmmo(mag, reserve);
            hud?.SetWeaponName(d.name + " · " + d.modeLabel);
        }

        static void NoPhys(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c == null) return;
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }

        static void AddBox(GameObject parent, string name, Vector3 size, Vector3 pos, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            NoPhys(b);
            b.transform.SetParent(parent.transform, false);
            b.transform.localPosition = pos;
            b.transform.localScale = size;
            var r = b.GetComponent<MeshRenderer>();
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.layer = 2; // Ignore Raycast: il viewmodel non blocca mai l'hitscan
        }

        static void AddEllipsoid(GameObject parent, string name, Vector3 size, Vector3 pos, Material m, float tiltX = 0f)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = name;
            NoPhys(s);
            s.transform.SetParent(parent.transform, false);
            s.transform.localPosition = pos;
            s.transform.localRotation = Quaternion.Euler(tiltX, 0f, 0f);
            s.transform.localScale = size;
            var r = s.GetComponent<MeshRenderer>();
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.layer = 2;
        }

        /// <summary>Capsula arrotondata: asse verticale (Y) oppure lungo Z, con inclinazione opzionale.</summary>
        static void AddCapsule(GameObject parent, string name, float radius, float length, Vector3 pos, Material m,
            bool vertical = false, float tilt = 0f)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            c.name = name;
            NoPhys(c);
            c.transform.SetParent(parent.transform, false);
            c.transform.localPosition = pos;
            c.transform.localRotation = vertical ? Quaternion.Euler(tilt, 0f, 0f) : Quaternion.Euler(90f + tilt, 0f, 0f);
            c.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            var r = c.GetComponent<MeshRenderer>();
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.layer = 2;
        }

        static void AddSphere(GameObject parent, string name, float radius, Vector3 pos, Material m)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = name;
            NoPhys(s);
            s.transform.SetParent(parent.transform, false);
            s.transform.localPosition = pos;
            s.transform.localScale = Vector3.one * (radius * 2f);
            var r = s.GetComponent<MeshRenderer>();
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.layer = 2;
        }

        static void AddCyl(GameObject parent, string name, float radius, float length, Vector3 pos, Material m)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = name;
            NoPhys(c);
            c.transform.SetParent(parent.transform, false);
            c.transform.localPosition = pos;
            c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            c.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            var r = c.GetComponent<MeshRenderer>();
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.layer = 2;
        }

        public bool IsBound { get { return cam != null && flashQuad != null; } }

        public void Tick(float dt, bool sprinting, float speed = 0f)
        {
            if (cam == null || flashQuad == null) return;
            var d = Def;
            adsHeld = Input.GetMouseButton(1);
            ads += ((adsHeld ? 1 : 0) - ads) * Mathf.Min(1f, 13f * dt);
            recoil *= Mathf.Max(0f, 1f - 11f * dt);

            if (reloading)
            {
                reloadT -= dt;
                if (reloadT <= 0f)
                {
                    int need = magSize - mag;
                    int take = Mathf.Min(need, reserve);
                    mag += take; reserve -= take;
                    reloading = false;
                    hud?.SetAmmo(mag, reserve);
                    hud?.SetReloading(false);
                }
            }
            if (Input.GetKeyDown(KeyCode.R)) Reload();

            float interval = 60f / rpm;
            bool wantFire = Input.GetMouseButton(0) || GameManager.autoTest;
            bool triggerOk = GameManager.autoTest || d.auto || !triggerHeld;
            if (wantFire && triggerOk && !reloading && Time.time - lastShot >= interval)
            {
                Shoot(d);
                triggerHeld = true;
            }
            if (!wantFire) triggerHeld = false;

            if (flashQuad.activeSelf && Time.time - lastShot > 0.055f)
            {
                flashQuad.SetActive(false);
                flashLight.intensity = 0f;
            }

            UpdatePose(dt, speed, sprinting);
        }

        /// <summary>Posa del viewmodel: respiro, camminata, corsa, rinculo, ricarica e ingresso arma.</summary>
        void UpdatePose(float dt, float speed, bool sprinting)
        {
            if (viewRoot == null) return;
            Vector3 hip = new Vector3(0.28f, -0.26f, 0.62f);
            Vector3 adsP = new Vector3(0f, -adsSightY, 0.52f);
            Vector3 pos = Vector3.Lerp(hip, adsP, ads);
            Quaternion rot = Quaternion.identity;

            // respiro
            float calm = 1f - ads * 0.75f;
            pos.x += Mathf.Sin(Time.time * 1.7f) * 0.0032f * calm;
            pos.y += Mathf.Sin(Time.time * 2.3f) * 0.0026f * calm;

            // camminata: dondolio e passo
            float bob = Mathf.Clamp01(speed / 5.5f) * (1f - ads * 0.8f);
            pos.y += Mathf.Sin(Time.time * 8.6f) * 0.0075f * bob;
            pos.x += Mathf.Cos(Time.time * 4.3f) * 0.0065f * bob;

            // corsa: arma abbassata e ruotata
            if (sprinting)
            {
                pos += new Vector3(0.045f, -0.065f, -0.03f);
                rot *= Quaternion.Euler(-10f, 20f, 13f);
            }

            // rinculo
            pos.z -= recoil * 0.32f;
            pos.y += recoil * 0.055f;
            rot *= Quaternion.Euler(-recoil * 38f, 0f, 0f);

            // ricarica: l'arma scende e ruota, il caricatore esce e rientra
            float rp = -1f;
            if (reloading)
            {
                rp = 1f - Mathf.Clamp01(reloadT / Mathf.Max(0.01f, Def.reloadTime));
                float tilt = Mathf.Sin(Mathf.Clamp01(rp / 0.9f) * Mathf.PI);
                pos += new Vector3(-0.035f, -0.055f, -0.02f) * tilt;
                rot *= Quaternion.Euler(-15f * tilt, 10f * tilt, 22f * tilt);
            }

            // ingresso arma (acquisto al muro)
            float e = Mathf.Clamp01(equipT / 0.30f);
            pos += new Vector3(0.03f, -0.11f, -0.04f) * e;
            rot *= Quaternion.Euler(24f * e, 0f, 7f * e);
            equipT = Mathf.Max(0f, equipT - dt);

            viewRoot.localPosition = pos;
            viewRoot.localRotation = rot;
            AnimateParts(rp);
        }

        static float Band(float v, float a, float b) => Mathf.Clamp01((v - a) / Mathf.Max(0.0001f, b - a));

        void AnimateParts(float rp)
        {
            if (magPart != null)
            {
                if (rp >= 0f)
                {
                    // esce tra il 12% e il 30%, rientra tra il 52% e il 68% della ricarica
                    float down = Mathf.Clamp01(Band(rp, 0.12f, 0.30f) - Band(rp, 0.52f, 0.68f));
                    magPart.localPosition = magRest + Vector3.down * (0.24f * down);
                    magPart.localRotation = Quaternion.Euler(18f * down, 0f, 0f);
                }
                else
                {
                    magPart.localPosition = magRest;
                    magPart.localRotation = Quaternion.identity;
                }
            }
            if (chargePart != null)
            {
                float pull = rp >= 0f
                    ? Mathf.Clamp01(Band(rp, 0.78f, 0.84f) - Band(rp, 0.88f, 0.94f))
                    : 0f;
                chargePart.localPosition = chargeRest + Vector3.back * (0.045f * pull);
            }
            if (pumpPart != null)
            {
                float cycle = rp >= 0f ? Mathf.Sin(Mathf.Clamp01((rp - 0.15f) / 0.70f) * Mathf.PI * 2f) : 0f;
                pumpPart.localPosition = pumpRest + Vector3.back * (0.06f * cycle);
            }
        }

        public void Reload()
        {
            if (reloading || mag >= magSize || reserve <= 0) return;
            reloading = true;
            reloadT = Def.reloadTime;
            hud?.SetReloading(true);
            Synth.Reload();
        }

        void Shoot(WeaponDef d)
        {
            if (mag <= 0) { Reload(); return; }
            mag--;
            lastShot = Time.time;
            GameManager.shotsFired++;
            recoil += d.shotRecoil;
            flashQuad.SetActive(true);
            flashQuad.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flashQuad.transform.localScale = Vector3.one * Random.Range(0.11f, 0.16f);
            flashLight.intensity = 3f;
            hud?.SetAmmo(mag, reserve);
            Synth.Shot();

            // più pallini per il fucile a pompa, uno solo per le altre armi
            var hitSet = new HashSet<Enemy>();
            for (int p = 0; p < d.pellets; p++)
            {
                Vector3 dir = cam.forward
                    + cam.right * Random.Range(-d.spread, d.spread)
                    + cam.up * Random.Range(-d.spread, d.spread);
                if (Physics.Raycast(cam.position, dir.normalized, out RaycastHit hit, d.range))
                {
                    Enemy e = hit.collider.GetComponentInParent<Enemy>();
                    if (e == null) continue;
                    bool head = hit.collider.CompareTag("Head");
                    bool firstOnThisEnemy = hitSet.Add(e);
                    e.Damage(d.damage * (head ? d.headMult : 1f), hit.point, head, firstOnThisEnemy);
                }
            }
        }
    }
}
