using UnityEngine;

namespace PryingEyes
{
    /// <summary>Hitscan weapon: RPM fire, mag/reserve, reload, recoil, muzzle flash, procedural viewmodel.</summary>
    public class Weapon : MonoBehaviour
    {
        [Header("Tuning")]
        public int magSize = 30;
        public int mag = 30;
        public int reserve = 120;
        public float rpm = 600f;
        public float damage = 34f;
        public float range = 120f;

        [Header("State")]
        public bool reloading = false;
        public float ads = 0f;
        public float recoil = 0f;
        float lastShot = -99f, reloadT = 0f;

        [Header("Viewmodel materials (assigned by HotelBuilder)")]
        public Material bodyMat;
        public Material gripMat;
        public Material flashMat;

        Transform cam;
        GameObject flashQuad;
        Light flashLight;
        HUD hud;
        bool adsHeld;

        public void Bind(Transform camera, HUD h)
        {
            cam = camera;
            hud = h;
            BuildViewmodel();
        }

        void BuildViewmodel()
        {
            if (bodyMat == null || gripMat == null || flashMat == null)
            {
                Debug.LogError("[PryingEyes] Weapon materials missing - viewmodel skipped");
                return;
            }
            var dark = bodyMat;
            var grip = gripMat;

            GameObject root = new GameObject("Viewmodel");
            root.layer = 2;
            root.transform.SetParent(cam, false);
            root.transform.localPosition = new Vector3(0.28f, -0.26f, 0.55f);
            root.transform.localRotation = Quaternion.identity;

            AddBox(root, "Body", new Vector3(0.09f, 0.11f, 0.42f), new Vector3(0, 0, 0), dark);
            AddBox(root, "Barrel", new Vector3(0.05f, 0.05f, 0.35f), new Vector3(0, 0.02f, 0.36f), dark);
            AddBox(root, "Grip", new Vector3(0.07f, 0.16f, 0.09f), new Vector3(0, -0.12f, 0.05f), grip);
            AddBox(root, "SightR", new Vector3(0.02f, 0.06f, 0.02f), new Vector3(-0.035f, 0.085f, 0.05f), dark);
            AddBox(root, "SightL", new Vector3(0.02f, 0.06f, 0.02f), new Vector3(0.035f, 0.085f, 0.05f), dark);

            flashQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            flashQuad.name = "Flash";
            NoPhys(flashQuad);
            flashQuad.transform.SetParent(root.transform, false);
            flashQuad.transform.localPosition = new Vector3(0, 0.02f, 0.56f);
            flashQuad.transform.localScale = new Vector3(0.13f, 0.13f, 1f);
            flashQuad.GetComponent<MeshRenderer>().material = flashMat;
            flashQuad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flashQuad.SetActive(false);

            flashLight = root.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.6f, 0.25f);
            flashLight.intensity = 0f;
            flashLight.range = 6f;

            // luce dedicata all'arma, come nella versione web: con la hall al buio l'arma (quasi tutta metallo,
            // riflette l'ambiente) era una sagoma nera. Raggio di un metro: non illumina la stanza.
            var fill = new GameObject("ViewmodelFill").AddComponent<Light>();
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.25f, 0.3f, -0.2f);
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.95f, 0.88f);
            fill.intensity = 0.6f;
            fill.range = 1.1f;
            fill.shadows = LightShadows.None;
            transform.GetChild(0).localPosition = new Vector3(0.28f, -0.26f, 0.55f);
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
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // l'arma non proietta ombre sul mondo
            b.layer = 2; // Ignore Raycast: il viewmodel non blocca mai l'hitscan
        }

        public bool IsBound { get { return cam != null && flashQuad != null; } }

        void Awake()
        {
            // runtime rebind: refs set by the editor builder don't survive serialization
            if (cam == null)
            {
                var t = transform.Find("MainCamera");
                if (t != null) cam = t;
            }
            if (flashQuad == null && cam != null)
            {
                var vm = cam.Find("Viewmodel");
                if (vm != null)
                {
                    var f = vm.Find("Flash");
                    if (f != null) flashQuad = f.gameObject;
                    flashLight = vm.GetComponent<Light>();
                }
            }
        }

        public void Tick(float dt, bool sprinting)
        {
            if (cam == null || flashQuad == null) return;
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
                }
            }
            if (Input.GetKeyDown(KeyCode.R)) Reload();

            float interval = 60f / rpm;
            bool wantFire = Input.GetMouseButton(0) || GameManager.autoTest;
            if (wantFire && !reloading && Time.time - lastShot >= interval)
                Shoot();

            if (flashQuad.activeSelf && Time.time - lastShot > 0.055f)
            {
                flashQuad.SetActive(false);
                flashLight.intensity = 0f;
            }
        }

        public void Reload()
        {
            if (reloading || mag >= magSize || reserve <= 0) return;
            reloading = true;
            reloadT = 1.4f;
            hud?.Toast("Ricarica...");
            Synth.Reload();
        }

        void Shoot()
        {
            if (mag <= 0) { Reload(); return; }
            mag--;
            lastShot = Time.time;
            GameManager.shotsFired++;
            recoil += 0.06f;
            flashQuad.SetActive(true);
            flashLight.intensity = 3f;
            hud?.SetAmmo(mag, reserve);
            Synth.Shot();

            // hitscan dal centro camera, spread leggero
            Vector3 dir = cam.forward;
            dir += cam.right * Random.Range(-0.006f, 0.006f) + cam.up * Random.Range(-0.006f, 0.006f);
            if (Physics.Raycast(cam.position, dir.normalized, out RaycastHit hit, range))
            {
                Enemy e = hit.collider.GetComponentInParent<Enemy>();
                if (e != null)
                {
                    bool head = hit.collider.CompareTag("Head");
                    e.Damage(damage * (head ? 2f : 1f), hit.point, head);
                }
            }
        }
    }
}
