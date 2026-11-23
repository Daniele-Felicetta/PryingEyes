using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.Pista
{
    /// <summary>The showroom of the garage: a bike with its rider on a dark plate, turning slowly under three lights, photographed by a camera of its own into a texture the menu shows. It stands far from everything
    /// else (nothing of the stage is in the picture of the game and nothing of the game in the stage's), is built when the garage is first opened and is switched off when it closes. The camera is never left to
    /// draw by itself: the stage draws it by hand every frame it is on (a window that is hidden, as in the automated runs, does not draw the cameras of its own accord).</summary>
    public sealed class GarageStage : MonoBehaviour
    {
        public const int Width = 720, Height = 480;
        /// <summary>Degrees a second: a bit more than a turn in a quarter of a minute.</summary>
        public const float TurnRate = 24f;
        static readonly Vector3 Far = new Vector3(0f, -3000f, 0f);

        public RenderTexture Picture { get; private set; }
        public float Spin { get; private set; }
        /// <summary>The bike and the livery on the plate (-1 when empty).</summary>
        public int Bike { get; private set; } = -1;
        public int Skin { get; private set; } = -1;
        /// <summary>The outfit of the rider on the plate (-1 for the leathers and the helmet of the bike's livery).</summary>
        public int Outfit { get; private set; } = -1;
        /// <summary>The bike on the plate has its rider (false: the bike alone, as the desert's store shows it).</summary>
        public bool Rider { get; private set; } = true;
        /// <summary>For the tests: the bike put together and how many parts it has.</summary>
        public PistaBikeRig Rig { get; private set; }
        public Camera Cam { get; private set; }
        /// <summary>The garage is open: the lights are on and the picture is drawn every frame.</summary>
        public bool Active { get; private set; }

        GameObject bike;
        Light key, rim, fill;
        Material wallMat;

        /// <summary>'xOffset' puts a second stage beside the first (the lights reach 18 m: the desert's store has one for every seat of a shared screen, 60 m apart).</summary>
        public static GarageStage Create(float xOffset = 0f)
        {
            var go = new GameObject("Garage - vetrina");
            go.transform.position = Far + new Vector3(xOffset, 0f, 0f);
            var stage = go.AddComponent<GarageStage>();
            stage.Build();
            stage.SetActive(false);
            return stage;
        }

        void Build()
        {
            Picture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Vetrina del garage", antiAliasing = 4, useMipMap = false };

            var plate = Disc("Piatto", 3.0f, 0.035f, -0.035f, PistaTex.Lit("Piatto", new Color(0.04f, 0.045f, 0.052f), 0.55f, 0.2f));
            var ring = Disc("Anello", 3.12f, 0.03f, -0.045f, PistaTex.Lit("Anello d'ottone", new Color(0.55f, 0.42f, 0.2f), 0.6f, 0.8f));
            plate.transform.SetParent(transform, false); ring.transform.SetParent(transform, false);
            plate.GetComponent<MeshRenderer>().receiveShadows = true;                          // the bike casts its shadow on the plate

            // The backdrop. The desert's sky dome has enormous bounds and is drawn for every camera, so without a wall behind the plate the desert's sky (and its hour of the day) would stand behind the
            // bike in the store of the desert. An opaque wall in the colour of the studio hides it; in the track mode nothing is there to hide and the wall is the colour the camera clears to anyway.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wall.name = "Fondale";
            var wallCollider = wall.GetComponent<Collider>();
            if (wallCollider != null) Destroy(wallCollider);
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = new Vector3(0f, 0f, -9f);
            wall.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);                       // a Quad faces -z: turned to face the camera, which stands on the +z side
            wall.transform.localScale = new Vector3(70f, 50f, 1f);
            wallMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Pista_Fondale" };
            wallMat.SetColor("_BaseColor", new Color(0.022f, 0.03f, 0.034f, 1f));
            var wallRenderer = wall.GetComponent<MeshRenderer>();
            wallRenderer.sharedMaterial = wallMat; wallRenderer.shadowCastingMode = ShadowCastingMode.Off; wallRenderer.receiveShadows = false;

            key = MakeLight("Luce principale", LightType.Spot, new Vector3(-3.4f, 3.6f, 3.2f), 42f, new Color(1f, 0.95f, 0.86f), 18f, 62f, true);
            rim = MakeLight("Controluce", LightType.Spot, new Vector3(3.6f, 2.8f, -3.4f), 36f, new Color(0.72f, 0.82f, 1f), 18f, 60f);
            fill = MakeLight("Luce di riempimento", LightType.Point, new Vector3(3.0f, 1.0f, 3.4f), 7f, new Color(1f, 0.82f, 0.62f), 14f, 0f);

            var cgo = new GameObject("Camera vetrina");
            cgo.transform.SetParent(transform, false);
            cgo.transform.localPosition = new Vector3(0f, 1.3f, 5.4f);
            cgo.transform.LookAt(transform.position + new Vector3(0f, 0.74f, 0f));
            Cam = cgo.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.022f, 0.03f, 0.034f, 1f);
            Cam.fieldOfView = 26f; Cam.nearClipPlane = 0.2f; Cam.farClipPlane = 40f;
            Cam.allowHDR = false; Cam.depth = -50f; Cam.enabled = false;
            Cam.targetTexture = Picture;
            var extra = Cam.GetUniversalAdditionalCameraData();
            extra.renderPostProcessing = false; extra.renderShadows = true;                    // the key light's shadow only (the other two cast none; the sun of the world is a world away)
            var listener = cgo.GetComponent<AudioListener>();
            if (listener != null) Destroy(listener);
        }

        GameObject Disc(string name, float diameter, float thickness, float y, Material mat)
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            d.name = name;
            var collider = d.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            d.transform.localScale = new Vector3(diameter, thickness, diameter);
            d.transform.localPosition = new Vector3(0f, y, 0f);
            var r = d.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return d;
        }

        Light MakeLight(string name, LightType type, Vector3 at, float intensity, Color colour, float range, float angle, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.transform.LookAt(transform.position + new Vector3(0f, 0.6f, 0f));
            var l = go.AddComponent<Light>();
            l.type = type; l.color = colour; l.intensity = intensity; l.range = range; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (type == LightType.Spot) l.spotAngle = angle;
            if (shadows)
            {
                l.shadowStrength = 0.9f; l.shadowBias = 0.015f; l.shadowNormalBias = 0.5f;
                l.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
            }
            return l;
        }

        /// <summary>The stage on or off (the picture is drawn by Tick every frame it is on).</summary>
        public void SetActive(bool on)
        {
            Active = on;
            if (key != null) key.enabled = on;
            if (rim != null) rim.enabled = on;
            if (fill != null) fill.enabled = on;
        }

        /// <summary>Put this bike in this livery (a skin of Livery, or -1 for the livery the bike comes in) on the plate, its rider in the leathers and helmet of this outfit (a skin of Livery, or -1 for the bike's
        /// own); nothing is rebuilt when it is already there.</summary>
        public void Show(int bikeIndex, int skin, int outfit = -1, bool rider = true)
        {
            bikeIndex = Mathf.Clamp(bikeIndex, 0, Bikes.Count - 1);
            if (bike != null && bikeIndex == Bike && skin == Skin && outfit == Outfit && rider == Rider) return;
            if (bike != null) Destroy(bike);
            var spec = Bikes.Get(bikeIndex);
            bike = new GameObject("Moto " + spec.name);
            bike.transform.SetParent(transform, false);
            var livery = Livery.For(skin >= 0 ? skin : spec.skin);
            if (outfit >= 0) livery = Livery.WithRider(livery, outfit);
            Rig = PistaBikeMesh.Build(bike.transform, spec.meshStyle, livery, rider);
            Rig.root.localScale = Vector3.one * spec.meshScale;
            // the bike casts and receives the shadow of the key light (the helmet on the shoulder, the fairing on the engine, the wheels on the plate); the windscreen is glass and casts none
            foreach (var r in bike.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
            foreach (var r in Rig.screenParts) if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
            Bike = bikeIndex; Skin = skin; Outfit = outfit; Rider = rider;
            Pose(0f);
        }

        void Pose(float dt)
        {
            if (Rig == null) return;
            var p = transform.position;
            Rig.Pose(new BikeView { x = p.x, y = p.y, z = p.z, heading = Spin * Mathf.Deg2Rad }, dt);
            Rig.Lights(false, false);
        }

        /// <summary>Every frame the garage is open: the plate turns and the picture is drawn.</summary>
        public void Tick(float dt)
        {
            Spin = Mathf.Repeat(Spin + TurnRate * dt, 360f);
            Pose(dt);
            if (Active && Cam != null) Cam.Render();
        }

        /// <summary>Turn the plate to an angle at once (the tests photograph it from the side and from the front).</summary>
        public void SetSpin(float degrees) { Spin = Mathf.Repeat(degrees, 360f); Pose(0f); }

        /// <summary>For the tests: a photograph of the bike on the plate, w x h pixels, drawn at once. The plate is turned to 'spin' degrees first (0 the bike faces the camera, 90 it shows its right side, 180 its tail), the
        /// camera stands 'dist' metres away at the height 'camY' and looks at the height 'aim' above the plate. The camera goes back to where it was afterwards.</summary>
        public Texture2D Snapshot(float spin, float dist, float camY, float aim, float fov, int w, int h)
        {
            SetSpin(spin);
            var t = Cam.transform;
            var p0 = t.localPosition; var r0 = t.localRotation; float f0 = Cam.fieldOfView; var rt0 = Cam.targetTexture;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
            t.localPosition = new Vector3(0f, camY, dist);
            t.LookAt(transform.position + new Vector3(0f, aim, 0f));
            Cam.fieldOfView = fov;
            Cam.targetTexture = rt;
            Cam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            Cam.targetTexture = rt0; t.localPosition = p0; t.localRotation = r0; Cam.fieldOfView = f0;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        void OnDestroy()
        {
            if (Picture != null) { Picture.Release(); Destroy(Picture); }
            if (wallMat != null) Destroy(wallMat);
        }
    }
}
