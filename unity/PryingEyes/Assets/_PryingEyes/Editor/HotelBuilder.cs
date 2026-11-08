using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.IO;

namespace PryingEyes.EditorTools
{
    /// <summary>One-shot hotel + scene construction. Port of js/world.js. Run via CLI, saves Hotel.unity.</summary>
    public static class HotelBuilder
    {
        const float HX = 35f, HZ = 23f;
        static Material floorMat, wallMat, ceilMat, woodMat, goldMat, darkMat, steelMat, marbleMat;
        static Material carpetMat, velvetR, velvetG, glassMat, sandMat, rockMat;
        static readonly List<Vector3> spawnPts = new List<Vector3>();

        public static void Build()
        {
            SetupURP.Configure();
            EnsureTagsLayers();
            MakeMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            scene.name = "Hotel";

            Floor();
            Walls();
            Columns();
            Mezzanine();
            Stairs();
            Reception();
            Lounge();
            Elevators();
            Windows();
            Entrance();
            Chandeliers();
            CrocCage();
            Lights();
            Probe();
            SpawnPoints();
            PlayerAndGame();

            if (!Directory.Exists("Assets/_PryingEyes/Scenes")) Directory.CreateDirectory("Assets/_PryingEyes/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/_PryingEyes/Scenes/Hotel.unity");
            Debug.Log("[PryingEyes] Hotel scene built OK");
        }

        // ---------- setup ----------
        static void EnsureTagsLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tm.FindProperty("tags");
            bool hasHead = false;
            for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == "Head") hasHead = true;
            if (!hasHead) { tags.InsertArrayElementAtIndex(tags.arraySize); tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "Head"; }
            var layers = tm.FindProperty("layers");
            for (int i = 8; i <= 10; i++)
            {
                var l = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(l.stringValue)) l.stringValue = i == 8 ? "Weapon" : ("User Layer " + i);
            }
            tm.ApplyModifiedProperties();
        }

        static Texture2D SaveTex(Texture2D t, string name)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes")) AssetDatabase.CreateFolder("Assets", "_PryingEyes");
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Textures")) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Textures");
            AssetDatabase.CreateAsset(t, "Assets/_PryingEyes/Textures/" + name + ".asset");
            return t;
        }

        static Material SaveMat(string name, Material m)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Materials")) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Materials");
            string path = "Assets/_PryingEyes/Materials/" + name + ".mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material LitMat(string name, Color c, float metallic, float smooth, Texture2D map = null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (map != null)
            {
                m.SetTexture("_BaseMap", map);
                m.SetTextureScale("_BaseMap", m.GetTextureScale("_MainTex"));
            }
            return SaveMat(name, m);
        }

        static void MakeMaterials()
        {
            var checker = SaveTex(ProcTextures.Checker(512, 8), "MarbleFloor");
            var stripes = SaveTex(ProcTextures.Stripes(512, 24), "Wallpaper");
            var plaster = SaveTex(ProcTextures.Plaster(256), "Plaster");

            floorMat = LitMat("Floor", Color.white, 0.08f, 0.68f, checker);
            floorMat.mainTextureScale = new Vector2(10, 7);
            wallMat = LitMat("Wall", Color.white, 0f, 0.1f, stripes);
            wallMat.mainTextureScale = new Vector2(14, 2);
            ceilMat = LitMat("Ceil", new Color(0.09f, 0.09f, 0.12f), 0.1f, 0.1f, plaster);
            woodMat = LitMat("Wood", new Color(0.18f, 0.13f, 0.09f), 0.15f, 0.3f);
            goldMat = LitMat("Gold", new Color(0.54f, 0.42f, 0.18f), 1f, 0.65f);
            darkMat = LitMat("Dark", new Color(0.09f, 0.11f, 0.15f), 0.8f, 0.5f);
            steelMat = LitMat("Steel", new Color(0.42f, 0.44f, 0.47f), 0.9f, 0.6f);
            // marmo liscio per colonne/scale/banco (prima usavano la scacchiera del pavimento: effetto codice a barre)
            marbleMat = LitMat("Marble", new Color(0.86f, 0.84f, 0.79f), 0f, 0.72f);
            carpetMat = LitMat("Carpet", new Color(0.37f, 0.07f, 0.13f), 0f, 0.02f);
            velvetR = LitMat("VelvetR", new Color(0.35f, 0.12f, 0.16f), 0f, 0.05f);
            velvetG = LitMat("VelvetG", new Color(0.16f, 0.29f, 0.27f), 0f, 0.05f);
            sandMat = LitMat("Sand", new Color(0.6f, 0.54f, 0.37f), 0f, 0.05f);
            rockMat = LitMat("Rock", new Color(0.33f, 0.34f, 0.35f), 0.05f, 0.05f);
            glassMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            glassMat.color = new Color(0.85f, 0.93f, 0.89f, 0.12f);
            glassMat.SetFloat("_Surface", 1f);
            glassMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glassMat.SetFloat("_Metallic", 0f);
            glassMat.SetFloat("_Smoothness", 0.94f);
            SaveMat("Glass", glassMat);
        }

        // ---------- helpers ----------
        static GameObject Box(string name, float w, float h, float d, float x, float y, float z, Material m, bool collide = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = new Vector3(x, y, z);
            go.transform.localScale = new Vector3(w, h, d);
            go.GetComponent<MeshRenderer>().material = m;
            if (!collide) Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.isStatic = true;
            return go;
        }

        static GameObject NoShadow(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // ---------- geometry ----------
        static void Floor()
        {
            Box("Floor", HX * 2, 0.1f, HZ * 2, 0, -0.05f, 0, floorMat);
            Box("Carpet1", 16, 0.02f, 30, -10, 0.01f, 4, carpetMat, false);
            Box("Carpet2", 6, 0.02f, 20, 24, 0.01f, -4, carpetMat, false);
        }

        static void Walls()
        {
            float H = 10f, T = 1.2f;
            Box("WallN", HX * 2 + 4, H, T, 0, H / 2, -HZ - 1, wallMat);
            Box("WallS", HX * 2 + 4, H, T, 0, H / 2, HZ + 1, wallMat);
            Box("WallW", T, H, HZ * 2 + 4, -HX - 1, H / 2, 0, wallMat);
            Box("WallE", T, H, HZ * 2 + 4, HX + 1, H / 2, 0, wallMat);
            Box("WainsN", HX * 2, 1.3f, 0.12f, 0, 0.65f, -HZ + 0.65f, woodMat, false);
            Box("WainsS", HX * 2, 1.3f, 0.12f, 0, 0.65f, HZ - 0.65f, woodMat, false);
            Box("CeilA", 70, 0.5f, 26, 0, 9.25f, 10, ceilMat, false);
            Box("CeilB", 70, 0.5f, 14, 0, 9.25f, -16, ceilMat, false);
            for (int i = -3; i <= 3; i++)
                Box("Beam" + i, 0.35f, 0.45f, 26, i * 9, 8.8f, 10, woodMat, false);
        }

        static void Columns()
        {
            foreach (var p in new[] { new Vector2(-12, -8), new Vector2(12, -8), new Vector2(-12, 8), new Vector2(12, 8), new Vector2(-12, -16), new Vector2(12, -16) })
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                c.name = "Column";
                c.transform.position = new Vector3(p.x, 5, p.y);
                c.transform.localScale = new Vector3(1.4f, 10, 1.4f);
                c.GetComponent<MeshRenderer>().material = marbleMat;
                c.isStatic = true;
                Box("Cap", 1.7f, 0.3f, 1.7f, p.x, 8.4f, p.y, goldMat, false);
            }
        }

        static void Mezzanine()
        {
            Box("MezzSlab", 70, 0.4f, 12, 0, 4.0f, -17, woodMat);
            Box("RailW", 31, 1.1f, 0.3f, -19.5f, 4.75f, -11, woodMat);
            Box("RailE", 31, 1.1f, 0.3f, 19.5f, 4.75f, -11, woodMat);
            for (float x = -34; x <= 34; x += 4)
                Box("Bal", 0.12f, 1.1f, 0.12f, x, 4.75f, -11, woodMat, false);
            foreach (float x in new[] { -12f, 12f })
                Box("MezzPillar", 0.8f, 4, 0.8f, x, 2, -11, marbleMat);
            Box("DoorW", 2.4f, 3.2f, 0.3f, -20, 5.8f, -22.8f, woodMat);
            Box("DoorE", 2.4f, 3.2f, 0.3f, 20, 5.8f, -22.8f, woodMat);
        }

        static void Stairs()
        {
            for (int i = 0; i < 12; i++)
                Box("Step" + i, 8, 0.35f * (i + 1), 0.75f, 0, 0.35f * (i + 1) / 2f, -2.375f - i * 0.75f, marbleMat);
            foreach (float sx in new[] { -4.2f, 4.2f })
                Box("StairRail", 0.25f, 0.25f, 10.4f, sx, 2.9f, -6.5f, woodMat, false);
        }

        static void Reception()
        {
            Box("DeskTop", 8.4f, 0.08f, 1.5f, 26, 1.14f, 2, marbleMat);
            Box("DeskFront", 8, 1.1f, 1.2f, 26, 0.55f, 2, woodMat);
            Box("DeskSide", 1.2f, 1.1f, 4, 21.5f, 0.55f, 0, woodMat);
            var bell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bell.name = "Bell"; bell.transform.position = new Vector3(24, 1.28f, 2);
            bell.transform.localScale = Vector3.one * 0.18f;
            bell.GetComponent<MeshRenderer>().material = goldMat;
            Box("Sign", 7, 1.4f, 0.1f, 0, 6.8f, -22.3f, woodMat, false);
        }

        static void Lounge()
        {
            Sofa(-12, 6, 0, velvetR); Sofa(-12, 12, Mathf.PI, velvetG);
            Sofa(-18, 9, Mathf.PI / 2, velvetR); Sofa(-6, 9, -Mathf.PI / 2, velvetG);
            Box("CoffeeTable", 2.4f, 0.12f, 1.2f, -12, 0.5f, 9, woodMat);
            Box("Rug", 9, 0.02f, 7, -12, 0.012f, 9, carpetMat, false);
        }

        static void Sofa(float x, float z, float ry, Material m)
        {
            var g = new GameObject("Sofa");
            g.transform.position = new Vector3(x, 0, z);
            g.transform.rotation = Quaternion.Euler(0, ry * Mathf.Rad2Deg, 0);
            AddPart(g, new Vector3(3, 0.5f, 1.05f), new Vector3(0, 0.45f, 0), m);
            AddPart(g, new Vector3(3, 0.85f, 0.28f), new Vector3(0, 0.9f, -0.42f), m);
            AddPart(g, new Vector3(0.28f, 0.75f, 1.05f), new Vector3(-1.5f, 0.75f, 0), m);
            AddPart(g, new Vector3(0.28f, 0.75f, 1.05f), new Vector3(1.5f, 0.75f, 0), m);
            var cc = g.AddComponent<BoxCollider>();
            cc.center = new Vector3(0, 0.7f, 0); cc.size = new Vector3(3.2f, 1.4f, 1.2f);
            g.isStatic = true;
        }

        static void AddPart(GameObject g, Vector3 size, Vector3 pos, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = "Part";
            Object.DestroyImmediate(b.GetComponent<BoxCollider>());
            b.transform.SetParent(g.transform, false);
            b.transform.localPosition = pos;
            b.transform.localScale = size;
            b.GetComponent<MeshRenderer>().material = m;
        }

        static void Elevators()
        {
            foreach (float z in new[] { -4f, 0f, 4f })
            {
                Box("ElevFrame", 0.4f, 3.4f, 3.0f, -34.8f, 1.7f, z, woodMat);
                Box("ElevDoor", 0.25f, 2.9f, 2.3f, -34.85f, 1.45f, z, steelMat, false);
            }
        }

        static void Windows()
        {
            var glow = LitMat("WinGlow", new Color(0.04f, 0.07f, 0.13f), 0f, 0.2f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(0.62f, 0.75f, 0.88f) * 1.6f); // HDR: entra nel bloom
            for (int i = 0; i < 5; i++)
            {
                float z = -8 + i * 4;
                Box("WinFrame" + i, 0.5f, 5.4f, 3.2f, 34.7f, 3.5f, z, woodMat);
                // vetro luminoso verticale rivolto verso la hall (prima: Plane orizzontale, invisibile da terra)
                var pane = GameObject.CreatePrimitive(PrimitiveType.Quad);
                pane.name = "Pane" + i;
                Object.DestroyImmediate(pane.GetComponent<Collider>());
                pane.transform.position = new Vector3(34.42f, 3.5f, z);
                pane.transform.rotation = Quaternion.Euler(0, 90, 0);
                pane.transform.localScale = new Vector3(2.6f, 4.7f, 1f);
                pane.GetComponent<MeshRenderer>().material = glow;
                NoShadow(pane);
                pane.isStatic = true;
            }
        }

        static void Entrance()
        {
            Box("DoorFrameL", 1.0f, 4.2f, 0.5f, -2.2f, 2.1f, 22.8f, woodMat);
            Box("DoorFrameR", 1.0f, 4.2f, 0.5f, 2.2f, 2.1f, 22.8f, woodMat);
            Box("DoorTop", 5.4f, 0.8f, 0.5f, 0, 4.4f, 22.8f, woodMat);
            Box("DoorR", 1.9f, 4.0f, 0.18f, 1.0f, 2.0f, 22.9f, woodMat);
        }

        static Material bulbMat;

        static void Chandeliers()
        {
            bulbMat = LitMat("Bulb", new Color(0.07f, 0.07f, 0.07f), 0f, 0.3f);
            bulbMat.EnableKeyword("_EMISSION");
            bulbMat.SetColor("_EmissionColor", new Color(1f, 0.79f, 0.54f) * 4f); // HDR: le lampadine brillano col bloom
            Chandelier(0, 4); Chandelier(-22, 4);
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = "ChandOff"; c.transform.position = new Vector3(22, 6.6f, 4);
            c.transform.localScale = new Vector3(0.1f, 2.4f, 0.1f);
            c.GetComponent<MeshRenderer>().material = darkMat;
            c.isStatic = true;
        }

        static void Chandelier(float x, float z)
        {
            var g = new GameObject("Chandelier");
            g.transform.position = new Vector3(x, 6.6f, z);
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(stem.GetComponent<CapsuleCollider>());
            stem.transform.SetParent(g.transform, false);
            stem.transform.localPosition = new Vector3(0, 1.2f, 0);
            stem.transform.localScale = new Vector3(0.1f, 2.4f, 0.1f);
            stem.GetComponent<MeshRenderer>().material = darkMat;
            foreach (var ring in new[] { new Vector2(1.6f, -1.1f), new Vector2(1.0f, -0.5f) })
            {
                var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(t.GetComponent<CapsuleCollider>());
                t.transform.SetParent(g.transform, false);
                t.transform.localPosition = new Vector3(0, ring.y, 0);
                t.transform.localScale = new Vector3(ring.x * 2, 0.12f, ring.x * 2);
                t.GetComponent<MeshRenderer>().material = goldMat;
            }
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(b.GetComponent<SphereCollider>());
                b.transform.SetParent(g.transform, false);
                b.transform.localPosition = new Vector3(Mathf.Cos(a) * 1.6f, -0.92f, Mathf.Sin(a) * 1.6f);
                b.transform.localScale = Vector3.one * 0.14f;
                b.GetComponent<MeshRenderer>().material = bulbMat;
            }
            NoShadow(g); // il lampadario non oscura la sua stessa luce
            var pl = g.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.color = new Color(1f, 0.79f, 0.54f);
            pl.intensity = 40f; pl.range = 30f;
            pl.shadows = LightShadows.Soft;
            g.isStatic = true;
        }

        static Material crocSkin;

        static void CrocCage()
        {
            float CX = -29f, CZ = -18.5f;
            crocSkin = LitMat("CrocSkin", new Color(0.30f, 0.29f, 0.17f), 0.05f, 0.3f);
            Box("CageBase", 10, 0.5f, 7, CX, 0.25f, CZ, rockMat);
            Box("Pool", 9.4f, 0.1f, 6.4f, CX, 0.56f, CZ, darkMat, false);
            Box("Island", 4.4f, 0.5f, 3.2f, CX - 1, 0.55f, CZ - 0.5f, sandMat, false);
            float glassH = 2.2f, glassY = 0.5f + glassH / 2f;
            NoShadow(Box("GlassN", 10, glassH, 0.06f, CX, glassY, CZ - 3.5f, glassMat)); // il vetro non fa ombra
            NoShadow(Box("GlassS", 10, glassH, 0.06f, CX, glassY, CZ + 3.5f, glassMat));
            NoShadow(Box("GlassW", 0.06f, glassH, 7, CX - 5, glassY, CZ, glassMat));
            NoShadow(Box("GlassE", 0.06f, glassH, 7, CX + 5, glassY, CZ, glassMat));
            // lampada riscaldante rossa sopra l'isolotto, come nel web
            var heat = new GameObject("HeatLamp").AddComponent<Light>();
            heat.type = LightType.Point;
            heat.transform.position = new Vector3(CX - 1f, 2.9f, CZ - 0.5f);
            heat.color = new Color(1f, 0.36f, 0.16f);
            heat.intensity = 9f; heat.range = 10f;
            heat.shadows = LightShadows.Soft;
            Croc(CX + 1.5f, 0.42f, CZ + 0.5f, 0.7f, 1f);
            Croc(CX - 1.2f, 0.95f, CZ - 0.6f, -2.2f, 0.75f);
            Box("Plaque", 1.6f, 0.8f, 0.05f, CX, 1.05f, CZ + 3.56f, woodMat, false);
        }

        static void Croc(float x, float y, float z, float ry, float s)
        {
            var g = new GameObject("Croc");
            g.transform.position = new Vector3(x, y, z);
            g.transform.rotation = Quaternion.Euler(0, ry * Mathf.Rad2Deg, 0);
            g.transform.localScale = Vector3.one * s;
            var skin = crocSkin;
            AddPart(g, new Vector3(0.55f, 0.32f, 1.5f), new Vector3(0, 0.30f, 0), skin);
            AddPart(g, new Vector3(0.34f, 0.22f, 0.5f), new Vector3(0, 0.28f, -1.0f), skin);
            AddPart(g, new Vector3(0.4f, 0.24f, 0.97f), new Vector3(0, 0.32f, 1.1f), skin);
            var jaw = new GameObject("Jaw");
            jaw.transform.SetParent(g.transform, false);
            jaw.transform.localPosition = new Vector3(0, 0.24f, 1.0f);
            AddPart(jaw, new Vector3(0.27f, 0.07f, 0.9f), new Vector3(0, -0.03f, 0.35f), darkMat);
            var anim = g.AddComponent<PryingEyes.CrocAnim>();
            anim.jaw = jaw.transform;
            g.isStatic = false;
        }

        static void Lights()
        {
            // ambiente notturno a gradiente (prima: Flat grigio chiaro, tutto illuminato uguale e niente profondità)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.40f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.24f, 0.22f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.12f, 0.11f);

            // cielo notturno (prima: skybox diurno di default) + nebbia come nel web (FogExp2 0x0a0f1c, 0.016)
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.EnableKeyword("_SUNDISK_SIMPLE");
            sky.SetFloat("_SunDisk", 1f);
            sky.SetFloat("_SunSize", 0.025f);
            sky.SetFloat("_AtmosphereThickness", 0.45f);
            sky.SetColor("_SkyTint", new Color(0.22f, 0.28f, 0.45f));
            sky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
            sky.SetFloat("_Exposure", 0.22f);
            RenderSettings.skybox = SaveMat("NightSky", sky);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.07f, 0.09f, 0.14f);
            RenderSettings.fogDensity = 0.016f;

            // luna con ombre: entra solo dal lucernario (prima, senza ombre, attraversava il soffitto)
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.66f, 0.77f, 0.91f);
            moon.intensity = 1.6f;
            moon.transform.rotation = Quaternion.Euler(50, -20, 0);
            moon.shadows = LightShadows.Soft;
            RenderSettings.sun = moon;

            WarmPoint(26, 3, 2, 25f, 12f);    // reception
            WarmPoint(-14, 3.4f, 11, 14f, 14f); // lounge (spostata: attaccata alla colonna la bruciava)
            WarmPoint(0, 7, -17, 18f, 16f);   // mezzanine
            Emergency(-33.5f, 3.6f, 0f);      // luci d'emergenza rosse, come nel web
            Emergency(0f, 3.4f, 21.5f);

            // chiaro di luna che entra dalle vetrate est
            var win = new GameObject("WindowMoon").AddComponent<Light>();
            win.type = LightType.Spot;
            win.transform.position = new Vector3(33.5f, 5.5f, 0f);
            win.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.55f, 0f));
            win.color = new Color(0.56f, 0.70f, 0.91f);
            win.intensity = 30f; win.range = 30f; win.spotAngle = 110f;
            win.shadows = LightShadows.Soft;

            // post: ACES + grading + bloom + vignetta + grana come nel web.
            // Add<T>(true) attiva i parametri: prima i valori erano impostati senza override, quindi ignorati.
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            prof.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
            var ca = prof.Add<ColorAdjustments>(true);
            ca.postExposure.value = 0.7f;
            ca.contrast.value = 8f;
            ca.saturation.value = 12f;
            var bloom = prof.Add<Bloom>(true);
            bloom.intensity.value = 0.6f;
            bloom.threshold.value = 0.9f;
            bloom.scatter.value = 0.7f;
            var vig = prof.Add<Vignette>(true);
            vig.intensity.value = 0.32f;
            vig.smoothness.value = 0.45f;
            var grain = prof.Add<FilmGrain>(true);
            grain.type.value = FilmGrainLookup.Thin1;
            grain.intensity.value = 0.15f;
            const string volPath = "Assets/_PryingEyes/Settings/HotelVolume.asset";
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Settings")) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Settings");
            AssetDatabase.DeleteAsset(volPath);
            AssetDatabase.CreateAsset(prof, volPath);
            // i componenti vanno salvati dentro l'asset (prima andavano persi: components = {fileID: 0})
            foreach (var comp in prof.components)
            {
                comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(comp, prof);
            }
            AssetDatabase.SaveAssets();
            var volGo = new GameObject("Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = prof; // "profile" non viene salvato nella scena: serve sharedProfile
        }

        static void WarmPoint(float x, float y, float z, float intensity, float range)
        {
            var g = new GameObject("WarmLight");
            g.transform.position = new Vector3(x, y, z);
            var pl = g.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.color = new Color(1f, 0.79f, 0.54f);
            pl.intensity = intensity;
            pl.range = range;
            pl.shadows = LightShadows.Soft;
        }

        static void Emergency(float x, float y, float z)
        {
            var pl = new GameObject("EmergencyLight").AddComponent<Light>();
            pl.transform.position = new Vector3(x, y, z);
            pl.type = LightType.Point;
            pl.color = new Color(1f, 0.13f, 0.13f);
            pl.intensity = 4f;
            pl.range = 11f;
        }

        // riflessi: probe della hall con box projection, cotta in editor; vale anche come riflesso di default
        static void Probe()
        {
            var go = new GameObject("HallProbe");
            go.transform.position = new Vector3(0f, 3f, 2f);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Custom;
            p.center = new Vector3(0f, 2f, -2f);
            p.size = new Vector3(72f, 10f, 48f);
            p.boxProjection = true;
            p.resolution = 256;
            p.hdr = true;
            const string path = "Assets/_PryingEyes/Settings/HallProbe.exr";
            try
            {
                if (!Lightmapping.BakeReflectionProbe(p, path)) { Debug.LogWarning("[PryingEyes] reflection probe bake failed"); return; }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                p.customBakedTexture = cube != null ? (Texture)cube : AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (cube != null)
                {
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                    RenderSettings.customReflectionTexture = cube;
                }
                Debug.Log("[PryingEyes] reflection probe baked: cubemap=" + (cube != null));
            }
            catch (System.Exception e) { Debug.LogWarning("[PryingEyes] reflection probe error: " + e.Message); }
        }

        static void SpawnPoints()
        {
            var defs = new[] {
                new Vector3(-32, 0, 0), new Vector3(0, 0, 20.5f), new Vector3(30, 0, -19),
                new Vector3(-30, 4.2f, -17), new Vector3(30, 4.2f, -17), new Vector3(0, 0, -6)
            };
            var holder = new GameObject("SpawnPoints");
            var gm = GameObject.Find("GameManager");
            var list = new List<Transform>();
            foreach (var d in defs)
            {
                var t = new GameObject("SP").transform;
                t.SetParent(holder.transform);
                t.position = d;
                list.Add(t);
            }
            spawnPts.AddRange(defs);
        }

        static void PlayerAndGame()
        {
            // player
            var p = new GameObject("Player");
            p.layer = 2; // Ignore Raycast: la propria capsula non blocca mai l'hitscan
            p.transform.position = new Vector3(0, 0.1f, 19f);
            var cc = p.AddComponent<CharacterController>();
            cc.height = 1.75f; cc.radius = 0.38f; cc.center = new Vector3(0, 0.875f, 0); // misure del web
            var ctrl = p.AddComponent<PryingEyes.PlayerController>();
            var camGo = new GameObject("MainCamera");
            camGo.layer = 2;
            camGo.transform.SetParent(p.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.62f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.tag = "MainCamera";
            // in URP post-processing e anti-aliasing vanno accesi per camera (di default sono spenti)
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            camGo.AddComponent<AudioListener>();
            ctrl.cam = camGo.transform;

            // weapon + materials
            var w = p.AddComponent<PryingEyes.Weapon>();
            w.bodyMat = LitMat("GunBody", new Color(0.08f, 0.09f, 0.11f), 0.85f, 0.4f);
            w.gripMat = LitMat("GunGrip", new Color(0.16f, 0.12f, 0.09f), 0.1f, 0.2f);
            var fm = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_PryingEyes/Materials/GunFlash.mat");
            if (fm == null)
            {
                fm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                fm.color = new Color(1f, 0.6f, 0.15f);
                if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/_PryingEyes/Materials")) UnityEditor.AssetDatabase.CreateFolder("Assets/_PryingEyes", "Materials");
                UnityEditor.AssetDatabase.CreateAsset(fm, "Assets/_PryingEyes/Materials/GunFlash.mat");
            }
            w.flashMat = fm;

            // hud
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<PryingEyes.HUD>();

            w.Bind(camGo.transform, hud);
            ctrl.weapon = w;
            ctrl.hud = hud;
            hud.SetAmmo(30, 120);
            hud.SetBars(100, 50);

            // manager
            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<PryingEyes.GameManager>();
            gm.player = ctrl;
            gm.hud = hud;
            var sps = new List<Transform>();
            var holder = GameObject.Find("SpawnPoints");
            foreach (Transform t in holder.transform) sps.Add(t);
            gm.spawnPoints = sps.ToArray();
        }
    }
}
