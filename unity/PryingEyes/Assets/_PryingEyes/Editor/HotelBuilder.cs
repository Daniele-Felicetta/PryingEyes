using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.IO;

namespace PryingEyes.EditorTools
{
    /// <summary>One-shot hotel + scene construction. Port of js/world.js. Run via CLI, saves Hotel.unity,
    /// then bakes the lighting (lightmaps + light probes) and the reflection probe.</summary>
    public static class HotelBuilder
    {
        const float HX = 35f, HZ = 23f;
        // lucernario sopra lo scalone: unica apertura nel soffitto (y 9.0-9.5), tra le colonne a x = +-12
        const float SkyX0 = -11f, SkyX1 = 11f, SkyZ0 = -9f, SkyZ1 = -3f, CeilY = 9f;
        // luna alta a nord, oltre lo scalone: le lame scendono verso l'ingresso e cadono sui gradini bassi,
        // e chi entra dall'ingresso le vede venirgli incontro
        static readonly Vector3 MoonDir = new Vector3(-0.097f, -0.927f, 0.362f);
        // ascensore centrale sulla parete ovest: vano 2.3 x 2.9 m, cabina profonda 2.3 m dietro il muro
        const float ElevZ = 0f, ElevHalfW = 1.15f, ElevH = 2.9f, CabDepth = 2.3f;
        // luci che devono restare in tempo reale (non Mixed): accese e spente durante il gioco
        static readonly HashSet<Light> keepRealtime = new HashSet<Light>();
        const string ScenePath = "Assets/_PryingEyes/Scenes/Hotel.unity";
        const string VolPath = "Assets/_PryingEyes/Settings/HotelVolume.asset";
        const StaticEditorFlags AllStatic = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic
            | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;

        static Material floorMat, wallMat, ceilMat, woodMat, goldMat, darkMat, steelMat, marbleMat;
        static Material carpetMat, velvetR, velvetG, glassMat, sandMat, rockMat;
        static readonly List<Vector3> spawnPts = new List<Vector3>();

        public static void Build()
        {
            keepRealtime.Clear();
            SetupURP.Configure();
            EnsureTagsLayers();
            MakeMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            scene.name = "Hotel";

            Floor();
            Walls();
            Skylight();
            Columns();
            Mezzanine();
            Stairs();
            Reception();
            Lounge();
            Elevators();
            Windows();
            Entrance();
            Chandeliers();
            FallenChandelier(22f, 4f);
            Sconces();
            CrocCage();
            Lights();
            MoonShafts();
            Puddles();
            Grime();
            SpawnPoints();
            PlayerAndGame();
            PrepareGI();

            if (!Directory.Exists("Assets/_PryingEyes/Scenes")) Directory.CreateDirectory("Assets/_PryingEyes/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);   // il bake vuole la scena salvata
            BakeLighting();
            Probe();                                           // dopo il bake: i riflessi vedono la luce che rimbalza
            Eyes();                                            // dopo il bake: si mettono dove la luce non arriva
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[PryingEyes] Hotel scene built OK");
        }

        /// <summary>Ritocco veloce del look senza ricostruire ne' ricuocere: colori, lame di luna, polvere,
        /// poi gli screenshot. Per luci e geometria serve Build.</summary>
        public static void TuneLook()
        {
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolPath);
            if (prof != null) ApplyGrading(prof);
            ApplyShaftParams(ShaftMaterial());
            ApplyDustParams(DustMaterial());
            AssetDatabase.SaveAssets();
            ShotScene.Shots();
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

        const string HotelTex = "Assets/_PryingEyes/Textures/Hotel/";

        /// <summary>Impostazioni d'import delle texture della hall (come il monaco): normal map come NormalMap,
        /// maschere lineari con l'alfa (lucidita'), colori sRGB, forme con l'alfa e bordo bloccato. Reimporta solo
        /// quelle da cambiare. Falso se le texture non ci sono (genera_texture.py non lanciato).</summary>
        static bool ImportHotelTextures()
        {
            if (!AssetDatabase.IsValidFolder(HotelTex.TrimEnd('/')))
            {
                Debug.LogWarning("[PryingEyes] texture dell'hotel mancanti: lanciare modelli/hotel/genera_texture.py");
                return false;
            }
            AssetDatabase.Refresh();
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { HotelTex.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool normal = path.EndsWith("_normal.png"), mask = path.EndsWith("_mask.png");
                bool shape = !normal && !mask && !path.EndsWith("_base.png");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool srgb = !normal && !mask;
                var alpha = mask || shape ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                var wrap = shape ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                if (ti.textureType == type && ti.sRGBTexture == srgb && ti.alphaSource == alpha && ti.wrapMode == wrap
                    && ti.anisoLevel == 8 && ti.textureCompression == TextureImporterCompression.CompressedHQ) continue;
                ti.textureType = type;
                ti.sRGBTexture = srgb;
                ti.alphaSource = alpha;
                ti.alphaIsTransparency = shape;
                ti.wrapMode = wrap;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
                changed++;
            }
            Debug.Log($"[PryingEyes] texture dell'hotel: {changed} reimportate");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(HotelTex + "marmo_pavimento_base.png") != null;
        }

        /// <summary>Materiale PBR da un set di texture: colore, normal map, maschera (metallicita' in R, occlusione
        /// in G, lucidita' nell'alfa). tile = metri coperti da una ripetizione (le UV sono in metri).</summary>
        static Material PbrMat(string name, string set, Color tint, float tile)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = tint;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(HotelTex + set + "_base.png"));
            m.SetTextureScale("_BaseMap", Vector2.one / tile);   // tutte le mappe di Lit usano _BaseMap_ST
            var nm = AssetDatabase.LoadAssetAtPath<Texture2D>(HotelTex + set + "_normal.png");
            if (nm != null) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            var mk = AssetDatabase.LoadAssetAtPath<Texture2D>(HotelTex + set + "_mask.png");
            if (mk != null)
            {
                m.SetTexture("_MetallicGlossMap", mk);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetTexture("_OcclusionMap", mk);                  // l'occlusione e' nel canale verde
                m.EnableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_OcclusionStrength", 1f);
                m.SetFloat("_Smoothness", 1f);                      // con la mappa: moltiplica l'alfa
                m.SetFloat("_SmoothnessTextureChannel", 0f);        // alfa della mappa metallica
            }
            return SaveMat(name, m);
        }

        /// <summary>Emissione che sopravvive al salvataggio. URP rimette _EMISSION a posto in base ai flag GI del
        /// materiale, e da codice partono a EmissiveIsBlack: cosi' lampadine e finestre restavano spente.</summary>
        static void Emissive(Material m, Color hdr, bool bakeIntoGI)
        {
            m.SetColor("_EmissionColor", hdr);
            m.globalIlluminationFlags = bakeIntoGI ? MaterialGlobalIlluminationFlags.BakedEmissive
                                                   : MaterialGlobalIlluminationFlags.None;
            m.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
        }

        static void MakeMaterials()
        {
            // bianco del marmo a ~0.78 (prima 0.92: piu' chiaro della neve, sotto le lampade bruciava)
            var checker = SaveTex(ProcTextures.Checker(512, 8, new Color(0.80f, 0.78f, 0.74f), new Color(0.05f, 0.05f, 0.06f)), "MarbleFloor");
            var stripes = SaveTex(ProcTextures.Stripes(512, 24), "Wallpaper");
            var plaster = SaveTex(ProcTextures.Plaster(256), "Plaster");

            // fase 3: materiali PBR con texture ripetibili (modelli/hotel/genera_texture.py), tiling in metri:
            // le UV in metri le scrive PrepareGI. Se le texture mancano si torna ai vecchi materiali generati qui.
            bool pbr = ImportHotelTextures();
            floorMat = pbr ? PbrMat("Floor", "marmo_pavimento", Color.white, 1.2f)
                           : LitMat("Floor", Color.white, 0.08f, 0.68f, checker);
            if (!pbr) floorMat.mainTextureScale = new Vector2(10, 7);
            wallMat = pbr ? PbrMat("Wall", "damasco", Color.white, 0.64f) : LitMat("Wall", Color.white, 0f, 0.1f, stripes);
            if (!pbr) wallMat.mainTextureScale = new Vector2(14, 2);
            // intonaco color crema invecchiato (prima quasi nero: il soffitto sembrava un buco)
            ceilMat = pbr ? PbrMat("Ceil", "intonaco", new Color(0.74f, 0.73f, 0.72f), 2f)
                          : LitMat("Ceil", new Color(0.50f, 0.47f, 0.43f), 0f, 0.08f, plaster);
            woodMat = pbr ? PbrMat("Wood", "noce", Color.white, 1f) : LitMat("Wood", new Color(0.18f, 0.13f, 0.09f), 0.15f, 0.3f);
            goldMat = pbr ? PbrMat("Gold", "ottone", Color.white, 0.5f) : LitMat("Gold", new Color(0.54f, 0.42f, 0.18f), 1f, 0.65f);
            darkMat = LitMat("Dark", new Color(0.09f, 0.11f, 0.15f), 0.8f, 0.5f);
            steelMat = pbr ? PbrMat("Steel", "acciaio", Color.white, 1f) : LitMat("Steel", new Color(0.42f, 0.44f, 0.47f), 0.9f, 0.6f);
            // marmo liscio per colonne/scale/banco (prima usavano la scacchiera del pavimento: effetto codice a barre)
            marbleMat = pbr ? PbrMat("Marble", "marmo_bianco", Color.white, 1f) : LitMat("Marble", new Color(0.78f, 0.76f, 0.71f), 0f, 0.72f);
            carpetMat = pbr ? PbrMat("Carpet", "moquette", Color.white, 1f) : LitMat("Carpet", new Color(0.37f, 0.07f, 0.13f), 0f, 0.02f);
            // velluto: la texture e' grigia, il colore lo da' la tinta
            velvetR = pbr ? PbrMat("VelvetR", "velluto", new Color(0.46f, 0.15f, 0.20f), 0.3f) : LitMat("VelvetR", new Color(0.35f, 0.12f, 0.16f), 0f, 0.05f);
            velvetG = pbr ? PbrMat("VelvetG", "velluto", new Color(0.20f, 0.37f, 0.34f), 0.3f) : LitMat("VelvetG", new Color(0.16f, 0.29f, 0.27f), 0f, 0.05f);
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

        /// <summary>Statico per tutto l'albero. gi=false: niente lightmap (oggetti piccoli o trasparenti, prendono
        /// la luce dalle sonde); batch=false: niente static batching (materiale cambiato a runtime).</summary>
        static void MarkStatic(GameObject root, bool gi = true, bool batch = true)
        {
            var flags = AllStatic;
            if (!gi) flags &= ~StaticEditorFlags.ContributeGI;
            if (!batch) flags &= ~StaticEditorFlags.BatchingStatic;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        static GameObject Part(GameObject parent, PrimitiveType type, Vector3 scale, Vector3 pos, Vector3 euler, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
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
            // muri a filo del pavimento (prima stavano 40 cm oltre il bordo: c'era una fessura buia lungo tutta la
            // hall) e boiserie appoggiata al muro (prima galleggiava a un metro)
            Box("WallN", HX * 2 + 4, H, T, 0, H / 2, -HZ - T / 2, wallMat);
            Box("WallS", HX * 2 + 4, H, T, 0, H / 2, HZ + T / 2, wallMat);
            // muro ovest con il vano dell'ascensore centrale (z -1.15..1.15, alto 2.9 m)
            float wz = HZ + 2f, zs = ElevHalfW + ElevZ;
            Box("WallW_S", T, H, wz - zs, -HX - T / 2, H / 2, -(wz + zs) / 2f, wallMat);
            Box("WallW_N", T, H, wz - zs, -HX - T / 2, H / 2, (wz + zs) / 2f, wallMat);
            Box("WallW_Top", T, H - ElevH, ElevHalfW * 2f, -HX - T / 2, ElevH + (H - ElevH) / 2f, ElevZ, wallMat);
            Box("WallE", T, H, HZ * 2 + 4, HX + T / 2, H / 2, 0, wallMat);
            Box("WainsN", HX * 2, 1.3f, 0.12f, 0, 0.65f, -HZ + 0.06f, woodMat, false);
            Box("WainsS", HX * 2, 1.3f, 0.12f, 0, 0.65f, HZ - 0.06f, woodMat, false);
            Box("CeilA", 70, 0.5f, 26, 0, 9.25f, 10, ceilMat, false);
            Box("CeilB", 70, 0.5f, 14, 0, 9.25f, -16, ceilMat, false);
            for (int i = -3; i <= 3; i++)
                Box("Beam" + i, 0.35f, 0.45f, 26, i * 9, 8.8f, 10, woodMat, false);
        }

        /// <summary>Tra CeilA e CeilB c'era una fessura aperta per tutti i 70 m della hall. Ora resta aperto solo
        /// il lucernario sopra lo scalone: telaio di ferro e vetri sporchi, molti rotti. La luna passa solo dai
        /// vetri rotti, quindi entra a lame separate invece che come una foschia uniforme.</summary>
        static void Skylight()
        {
            float zc = (SkyZ0 + SkyZ1) / 2f, zw = SkyZ1 - SkyZ0;
            Box("CeilSkyW", SkyX0 + HX, 0.5f, zw, (-HX + SkyX0) / 2f, 9.25f, zc, ceilMat, false);
            Box("CeilSkyE", HX - SkyX1, 0.5f, zw, (SkyX1 + HX) / 2f, 9.25f, zc, ceilMat, false);
            const float step = 2.2f;
            int nx = Mathf.RoundToInt((SkyX1 - SkyX0) / step);
            for (int i = 0; i <= nx; i++)
                Box("SkyBar", 0.3f, 0.34f, zw + 0.3f, SkyX0 + i * step, 9.15f, zc, darkMat, false);
            foreach (float z in new[] { SkyZ0, zc, SkyZ1 })
                Box("SkyRail", SkyX1 - SkyX0 + 0.3f, 0.34f, 0.3f, 0f, 9.15f, z, darkMat, false);
            // vetri rimasti interi: opachi di sporco, appena schiariti dalla luna sopra. Lo schema e' fisso
            // (stesso seme), circa un vetro su tre resiste.
            var dirty = LitMat("SkyGlassDirty", new Color(0.10f, 0.11f, 0.12f), 0f, 0.35f);
            Emissive(dirty, new Color(0.05f, 0.065f, 0.09f), bakeIntoGI: false);
            var rnd = new System.Random(3);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < 2; j++)
                {
                    if (rnd.NextDouble() > 0.36) continue;
                    float cx = SkyX0 + (i + 0.5f) * step, cz = SkyZ0 + (j + 0.5f) * (zw / 2f);
                    Box("SkyPane", step - 0.3f, 0.02f, zw / 2f - 0.3f, cx, 9.3f, cz, dirty, false);
                }
        }

        static void Columns()
        {
            foreach (var p in new[] { new Vector2(-12, -8), new Vector2(12, -8), new Vector2(-12, 8), new Vector2(12, 8), new Vector2(-12, -16), new Vector2(12, -16) })
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                c.name = "Column";
                // alte 9 m, dal pavimento al soffitto (prima 20 m: bucavano il soffitto e si vedevano dal lucernario)
                c.transform.position = new Vector3(p.x, 4.5f, p.y);
                c.transform.localScale = new Vector3(1.4f, 4.5f, 1.4f);
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
            MarkStatic(bell, gi: false, batch: false);   // non in batch: quando suona da solo sobbalza
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

            // imbottitura morbida: ellissoidi e capsule al posto delle scatole
            Part(g, PrimitiveType.Sphere, new Vector3(3.0f, 0.50f, 1.05f), new Vector3(0, 0.45f, 0), Vector3.zero, m);
            for (int i = -1; i <= 1; i++)
                Part(g, PrimitiveType.Sphere, new Vector3(0.88f, 0.22f, 0.88f),
                    new Vector3(i * 0.95f, 0.78f, 0.02f), Vector3.zero, m);
            // schienale e cuscini schienale leggermente inclinati
            Part(g, PrimitiveType.Sphere, new Vector3(2.90f, 0.90f, 0.30f), new Vector3(0, 0.96f, -0.42f), Vector3.zero, m);
            for (int i = -1; i <= 1; i++)
                Part(g, PrimitiveType.Sphere, new Vector3(0.86f, 0.52f, 0.20f),
                    new Vector3(i * 0.95f, 1.08f, -0.30f), new Vector3(-8, 0, 0), m);
            // braccioli a rullo
            Part(g, PrimitiveType.Capsule, new Vector3(0.30f, 0.525f, 0.30f), new Vector3(-1.50f, 0.75f, 0), new Vector3(90, 0, 0), m);
            Part(g, PrimitiveType.Capsule, new Vector3(0.30f, 0.525f, 0.30f), new Vector3(1.50f, 0.75f, 0), new Vector3(90, 0, 0), m);
            // cuscino decorativo
            Part(g, PrimitiveType.Sphere, new Vector3(0.46f, 0.40f, 0.16f), new Vector3(0.65f, 1.02f, -0.14f), new Vector3(10, 0, -12), m);
            // gambe in legno scuro
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Part(g, PrimitiveType.Cylinder, new Vector3(0.07f, 0.10f, 0.07f),
                        new Vector3(sx * 1.2f, 0.10f, sz * 0.40f), Vector3.zero, woodMat);

            var cc = g.AddComponent<BoxCollider>();
            cc.center = new Vector3(0, 0.7f, 0); cc.size = new Vector3(3.2f, 1.45f, 1.2f);
            MarkStatic(g);   // prima solo il padre: i cuscini restavano fuori dalla luce cotta
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
            // i due laterali restano finti, appoggiati al muro
            foreach (float z in new[] { -4f, 4f })
            {
                Box("ElevFrame", 0.4f, 3.4f, 3.0f, -34.8f, 1.7f, z, woodMat);
                Box("ElevDoor", 0.25f, 2.9f, 2.3f, -34.85f, 1.45f, z, steelMat, false);
            }
            WorkingElevator();
        }

        /// <summary>L'ascensore centrale funziona: cabina scavata dietro il muro, ante che scorrono dentro il muro,
        /// luce interna e freccia. Ad ogni ondata ne esce un monaco (Elevator.cs, GameManager).</summary>
        static void WorkingElevator()
        {
            float wallIn = -HX, wallOut = -HX - 1.2f, back = wallOut - CabDepth;
            float cx = (back + wallIn) / 2f, cw = ElevHalfW * 2f + 0.2f;
            var root = new GameObject("Ascensore");
            root.transform.position = new Vector3(wallIn, 0f, ElevZ);
            // cabina: pavimento (anche sotto lo spessore del muro), soffitto, fondo, fianchi, corrimano
            Box("CabFloor", wallIn - back, 0.1f, cw, cx, -0.05f, ElevZ, darkMat);
            Box("CabCeil", wallIn - back, 0.1f, cw, cx, ElevH + 0.05f, ElevZ, darkMat, false);
            Box("CabBack", 0.1f, ElevH, cw, back - 0.05f, ElevH / 2f, ElevZ, woodMat);
            Box("CabSideN", CabDepth, ElevH, 0.1f, (back + wallOut) / 2f, ElevH / 2f, ElevZ + ElevHalfW + 0.05f, woodMat);
            Box("CabSideS", CabDepth, ElevH, 0.1f, (back + wallOut) / 2f, ElevH / 2f, ElevZ - ElevHalfW - 0.05f, woodMat);
            Box("CabRail", 0.05f, 0.05f, cw - 0.4f, back + 0.08f, 0.95f, ElevZ, goldMat, false);
            var panelMat = LitMat("CabLamp", new Color(0.2f, 0.2f, 0.18f), 0f, 0.3f);
            Emissive(panelMat, new Color(1f, 0.94f, 0.8f) * 1.5f, bakeIntoGI: false);
            Box("CabLampPanel", 0.7f, 0.02f, 0.7f, (back + wallOut) / 2f, ElevH - 0.01f, ElevZ, panelMat, false);
            // cornice d'ottone sul lato hall
            Box("ElevPostN", 0.12f, 3.05f, 0.15f, wallIn + 0.06f, 1.525f, ElevZ + ElevHalfW + 0.08f, goldMat, false);
            Box("ElevPostS", 0.12f, 3.05f, 0.15f, wallIn + 0.06f, 1.525f, ElevZ - ElevHalfW - 0.08f, goldMat, false);
            Box("ElevLintel", 0.12f, 0.15f, ElevHalfW * 2f + 0.46f, wallIn + 0.06f, ElevH + 0.075f, ElevZ, goldMat, false);
            // freccia che si accende all'arrivo
            var arrowMat = LitMat("ElevArrow", new Color(0.12f, 0.08f, 0.05f), 0f, 0.5f);
            Emissive(arrowMat, Color.black, bakeIntoGI: false);
            var arrow = Box("ElevArrow", 0.03f, 0.14f, 0.3f, wallIn + 0.04f, ElevH + 0.35f, ElevZ, arrowMat, false);
            MarkStatic(arrow, gi: false, batch: false);
            // ante: non statiche (si muovono), layer porte (fuori dalla navmesh), collider per il giocatore
            Transform Door(string n, float z)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
                d.name = n;
                d.layer = PryingEyes.HotelNav.DoorLayer;
                d.transform.SetParent(root.transform, true);
                d.transform.position = new Vector3(wallIn - 0.06f, (ElevH - 0.05f) / 2f, z);
                d.transform.localScale = new Vector3(0.06f, ElevH - 0.05f, ElevHalfW);
                d.GetComponent<MeshRenderer>().sharedMaterial = steelMat;
                return d.transform;
            }
            var dl = Door("AntaNord", ElevZ + ElevHalfW / 2f);
            var dr = Door("AntaSud", ElevZ - ElevHalfW / 2f);
            // luce della cabina: in tempo reale (accesa solo con le porte aperte), con ombre: il monaco che esce
            // allunga la sua ombra nella hall
            var lg = new GameObject("LuceCabina");
            lg.transform.SetParent(root.transform, true);
            lg.transform.position = new Vector3((back + wallOut) / 2f, ElevH - 0.3f, ElevZ);
            var cl = lg.AddComponent<Light>();
            cl.type = LightType.Point;
            cl.color = new Color(1f, 0.93f, 0.78f);
            cl.intensity = 2.2f;
            cl.range = 5f;
            cl.shadows = LightShadows.Soft;
            keepRealtime.Add(cl);
            var sp = new GameObject("SpawnCabina").transform;
            sp.SetParent(root.transform, true);
            sp.position = new Vector3((back + wallOut) / 2f, 0.05f, ElevZ);
            var el = root.AddComponent<PryingEyes.Elevator>();
            el.doorL = dl;
            el.doorR = dr;
            el.spawn = sp;
            el.cabinLight = cl;
            el.indicator = arrow.GetComponent<MeshRenderer>();
        }

        static void Windows()
        {
            var glow = LitMat("WinGlow", new Color(0.04f, 0.07f, 0.13f), 0f, 0.2f);
            // notte fuori dai vetri: un blu tenue (a 1.6 venivano pannelli bianchi), che nella luce cotta
            // lascia un alone freddo sul pavimento davanti alle vetrate
            Emissive(glow, new Color(0.40f, 0.55f, 0.85f) * 0.5f, bakeIntoGI: true);
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
                // montanti: senza sembravano pannelli blu appesi al muro
                Box("Mullion", 0.08f, 4.7f, 0.1f, 34.36f, 3.5f, z, woodMat, false);
                Box("Transom", 0.08f, 0.1f, 2.6f, 34.36f, 4.6f, z, woodMat, false);
            }
        }

        /// <summary>Applique alle pareti: luce bassa e calda che fa leggere carta da parati e profondita' della hall.
        /// Luci Baked (dirette e rimbalzi nella lightmap: zero costo a runtime); alcune sono rotte e spente.</summary>
        static void Sconces()
        {
            var plate = goldMat;
            var lit = LitMat("SconceBulb", new Color(0.1f, 0.08f, 0.06f), 0f, 0.4f);
            Emissive(lit, new Color(1f, 0.72f, 0.45f) * 3f, bakeIntoGI: false);
            var dead = AssetDatabase.LoadAssetAtPath<Material>("Assets/_PryingEyes/Materials/BulbDead.mat");
            // posizione sulla parete, normale verso la hall, accesa?
            var defs = new (Vector3 p, Vector3 n, bool on)[]
            {
                (new Vector3(-30f, 3f, HZ), Vector3.back, true), (new Vector3(-18f, 3f, HZ), Vector3.back, false),
                (new Vector3(-8f, 3f, HZ), Vector3.back, true),  (new Vector3(8f, 3f, HZ), Vector3.back, true),
                (new Vector3(18f, 3f, HZ), Vector3.back, true),  (new Vector3(30f, 3f, HZ), Vector3.back, false),
                (new Vector3(-30f, 6.6f, -HZ), Vector3.forward, true), (new Vector3(-10f, 6.6f, -HZ), Vector3.forward, true),
                (new Vector3(10f, 6.6f, -HZ), Vector3.forward, false), (new Vector3(30f, 6.6f, -HZ), Vector3.forward, true),
                (new Vector3(-HX, 3f, -10f), Vector3.right, true), (new Vector3(-HX, 3f, 10f), Vector3.right, true),
                (new Vector3(-HX, 3f, 18f), Vector3.right, false),
                (new Vector3(HX, 3f, -16f), Vector3.left, true), (new Vector3(HX, 3f, 14f), Vector3.left, true),
                (new Vector3(HX, 3f, 19f), Vector3.left, true),
            };
            foreach (var d in defs)
            {
                var g = new GameObject(d.on ? "Sconce" : "SconceBroken");
                g.transform.position = d.p;
                g.transform.rotation = Quaternion.LookRotation(d.n);
                Part(g, PrimitiveType.Cube, new Vector3(0.16f, 0.32f, 0.05f), new Vector3(0f, 0f, 0.02f), Vector3.zero, plate);
                Part(g, PrimitiveType.Cube, new Vector3(0.03f, 0.03f, 0.16f), new Vector3(0f, 0.05f, 0.1f), Vector3.zero, plate);
                var bulb = Part(g, PrimitiveType.Sphere, Vector3.one * 0.11f, new Vector3(0f, 0.1f, 0.19f), Vector3.zero, d.on ? lit : dead);
                MarkStatic(g);
                MarkStatic(bulb, gi: false);
                if (!d.on) continue;
                var l = new GameObject("SconceLight").AddComponent<Light>();
                l.transform.SetParent(g.transform, false);
                l.transform.localPosition = new Vector3(0f, 0.12f, 0.3f);
                l.type = LightType.Point;
                l.color = new Color(1f, 0.74f, 0.48f);
                l.intensity = 2.1f;   // 2.6 sul damasco faceva una macchia rosso fuoco
                l.range = 7f;
                l.shadows = LightShadows.Soft;          // nel bake: ombre morbide cotte
                l.lightmapBakeType = LightmapBakeType.Baked;
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
        static readonly Color BulbGlow = new Color(1f, 0.79f, 0.54f) * 4f;

        static void Chandeliers()
        {
            bulbMat = LitMat("Bulb", new Color(0.07f, 0.07f, 0.07f), 0f, 0.3f);
            Emissive(bulbMat, BulbGlow, bakeIntoGI: false);   // la luce la fa la point light, qui serve il bagliore
            Chandelier(0, 4, flicker: true);   // quello centrale e' guasto e sfarfalla, come nel web
            Chandelier(-22, 4, flicker: false);
            // il terzo (x 22) e' caduto: vedi FallenChandelier
        }

        static void Chandelier(float x, float z, bool flicker)
        {
            var g = new GameObject(flicker ? "ChandelierFlicker" : "Chandelier");
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
            var bulbs = new List<Renderer>();
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(b.GetComponent<SphereCollider>());
                b.transform.SetParent(g.transform, false);
                b.transform.localPosition = new Vector3(Mathf.Cos(a) * 1.6f, -0.92f, Mathf.Sin(a) * 1.6f);
                b.transform.localScale = Vector3.one * 0.14f;
                b.GetComponent<MeshRenderer>().material = bulbMat;
                bulbs.Add(b.GetComponent<MeshRenderer>());
            }
            NoShadow(g); // il lampadario non oscura la sua stessa luce
            var pl = g.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.color = new Color(1f, 0.79f, 0.54f);
            // prima 40 su 30 m: senza rimbalzi serviva, con la luce cotta illuminava la hall come di giorno
            pl.intensity = 15f; pl.range = 18f;
            pl.shadows = LightShadows.Soft;
            MarkStatic(g);
            // le lampadine sono minuscole: luce dalle sonde, non dalla lightmap
            foreach (var b in bulbs) MarkStatic(b.gameObject, gi: false, batch: !flicker);
            if (flicker)
            {
                var f = g.AddComponent<PryingEyes.Flicker>();
                f.lamp = pl;
                f.bulbs = bulbs.ToArray();
                f.emission = BulbGlow;
            }
        }

        /// <summary>Il terzo lampadario e' venuto giu': moncone e cavo appesi al soffitto, anelli d'ottone
        /// schiantati e inclinati, lampadine spente sparse, schegge di vetro e calcinacci. Fa da ostacolo.</summary>
        static void FallenChandelier(float x, float z)
        {
            var g = new GameObject("FallenChandelier");
            g.transform.position = new Vector3(x, 0f, z);
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            var keep = new List<GameObject>
            {
                Part(g, PrimitiveType.Cylinder, new Vector3(0.1f, 0.35f, 0.1f), new Vector3(0f, 8.65f, 0f), Vector3.zero, darkMat),
                Part(g, PrimitiveType.Cylinder, new Vector3(0.025f, 0.6f, 0.025f), new Vector3(0.1f, 7.75f, 0.05f), new Vector3(0f, 0f, 9f), darkMat),
                Part(g, PrimitiveType.Cylinder, new Vector3(3.2f, 0.12f, 3.2f), new Vector3(-0.3f, 0.22f, 1.0f), new Vector3(9f, 20f, -6f), goldMat),
                Part(g, PrimitiveType.Cylinder, new Vector3(2.0f, 0.12f, 2.0f), new Vector3(0.6f, 0.55f, 0.6f), new Vector3(-17f, 0f, 11f), goldMat),
                Part(g, PrimitiveType.Cylinder, new Vector3(0.1f, 1.2f, 0.1f), new Vector3(-1.7f, 0.08f, 2.3f), new Vector3(0f, 35f, 88f), darkMat),
            };
            MarkStatic(g);

            var deadBulb = LitMat("BulbDead", new Color(0.06f, 0.06f, 0.07f), 0f, 0.9f);
            var small = new List<GameObject>();
            for (int i = 0; i < 12; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(0.4f, 2.8f);
                small.Add(Part(g, PrimitiveType.Sphere, Vector3.one * 0.14f,
                    new Vector3(Mathf.Cos(a) * d - 0.2f, 0.07f, Mathf.Sin(a) * d + 1.0f), Vector3.zero, deadBulb));
            }
            for (int i = 0; i < 22; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(0.3f, 3.2f);
                small.Add(Part(g, PrimitiveType.Cube, new Vector3(R(0.04f, 0.12f), 0.006f, R(0.03f, 0.09f)),
                    new Vector3(Mathf.Cos(a) * d - 0.2f, 0.004f, Mathf.Sin(a) * d + 1.0f), new Vector3(0f, R(0f, 360f), 0f), glassMat));
            }
            for (int i = 0; i < 14; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(0.2f, 2.4f), s = R(0.08f, 0.32f);
                var sc = new Vector3(s, s * R(0.4f, 0.8f), s * R(0.6f, 1f));
                small.Add(Part(g, PrimitiveType.Cube, sc,
                    new Vector3(Mathf.Cos(a) * d - 0.2f, sc.y * 0.35f, Mathf.Sin(a) * d + 1.0f),
                    new Vector3(R(-25f, 25f), R(0f, 360f), R(-25f, 25f)), ceilMat));
            }
            foreach (var s in small) MarkStatic(s, gi: false);

            // ostacolo basso: ci si gira intorno o ci si ripara
            var bc = g.AddComponent<BoxCollider>();
            bc.center = new Vector3(-0.1f, 0.35f, 0.9f);
            bc.size = new Vector3(3.6f, 0.7f, 3.6f);
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
            // il vetro non fa ombra e non entra nella luce cotta (trasparente)
            foreach (var gl in new[] {
                Box("GlassN", 10, glassH, 0.06f, CX, glassY, CZ - 3.5f, glassMat),
                Box("GlassS", 10, glassH, 0.06f, CX, glassY, CZ + 3.5f, glassMat),
                Box("GlassW", 0.06f, glassH, 7, CX - 5, glassY, CZ, glassMat),
                Box("GlassE", 0.06f, glassH, 7, CX + 5, glassY, CZ, glassMat) })
            {
                NoShadow(gl);
                MarkStatic(gl, gi: false);
            }
            // lampada riscaldante rossa sopra l'isolotto, come nel web
            var heat = new GameObject("HeatLamp").AddComponent<Light>();
            heat.type = LightType.Point;
            heat.transform.position = new Vector3(CX - 1f, 2.9f, CZ - 0.5f);
            heat.color = new Color(1f, 0.36f, 0.16f);
            heat.intensity = 6f; heat.range = 10f;
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
            // ambiente notturno a gradiente (prima: Flat grigio chiaro, tutto illuminato uguale e niente profondità).
            // Con la luce cotta questo cielo entra solo dal lucernario, come luce fredda di riempimento.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.14f, 0.17f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.10f, 0.10f, 0.10f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.05f, 0.05f);

            // cielo notturno (prima: skybox diurno di default) + nebbia come nel web (FogExp2 0x0a0f1c, 0.016)
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.EnableKeyword("_SUNDISK_SIMPLE");
            sky.SetFloat("_SunDisk", 1f);
            sky.SetFloat("_SunSize", 0.025f);
            sky.SetFloat("_AtmosphereThickness", 0.45f);
            sky.SetColor("_SkyTint", new Color(0.22f, 0.28f, 0.45f));
            sky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
            sky.SetFloat("_Exposure", 0.09f);   // dal lucernario si vedeva un blu da crepuscolo
            RenderSettings.skybox = SaveMat("NightSky", sky);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.07f, 0.09f, 0.14f);
            RenderSettings.fogDensity = 0.016f;

            // luna con ombre: entra solo dal lucernario (prima, senza ombre, attraversava il soffitto)
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.62f, 0.74f, 0.95f);
            moon.intensity = 0.85f;
            moon.transform.rotation = Quaternion.LookRotation(MoonDir.normalized);
            moon.shadows = LightShadows.Soft;
            RenderSettings.sun = moon;

            // intensita' ridotte di circa due terzi: ora la luce rimbalza e si somma
            WarmPoint(26, 3, 2, 10f, 12f);      // reception
            WarmPoint(-15, 2.4f, 11.5f, 6f, 12f); // lounge (lontana dalla colonna, che bruciava)
            WarmPoint(0, 7, -17, 7f, 14f);      // mezzanine
            Emergency(-33.5f, 3.6f, 0f);        // luci d'emergenza rosse, come nel web
            Emergency(0f, 3.4f, 21.5f);

            // chiaro di luna che entra dalle vetrate est
            var win = new GameObject("WindowMoon").AddComponent<Light>();
            win.type = LightType.Spot;
            win.transform.position = new Vector3(33.5f, 5.5f, 0f);
            win.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.55f, 0f));
            win.color = new Color(0.56f, 0.70f, 0.91f);
            win.intensity = 12f; win.range = 30f; win.spotAngle = 110f;
            win.shadows = LightShadows.Soft;

            // post: il profilo resta lo stesso asset (stesso GUID), si aggiornano solo i valori
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Settings")) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Settings");
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolPath);
            if (prof == null)
            {
                prof = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(prof, VolPath);
            }
            ApplyGrading(prof);
            AssetDatabase.SaveAssets();
            var volGo = new GameObject("Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = prof; // "profile" non viene salvato nella scena: serve sharedProfile
        }

        /// <summary>Componente del profilo: quello esistente, o uno nuovo salvato dentro l'asset
        /// (prima i componenti andavano persi: components = {fileID: 0}).</summary>
        static T Comp<T>(VolumeProfile prof) where T : VolumeComponent
        {
            if (prof.TryGet<T>(out var c)) return c;
            c = prof.Add<T>(true);
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            if (AssetDatabase.Contains(prof)) AssetDatabase.AddObjectToAsset(c, prof);
            return c;
        }

        /// <summary>Colori da horror: niente saturazione in piu' (la moquette veniva fucsia), ombre fredde come la
        /// luna e luci calde come le lampadine, contrasto e neri piu' profondi, grana da pellicola.</summary>
        static void ApplyGrading(VolumeProfile prof)
        {
            Comp<Tonemapping>(prof).mode.Override(TonemappingMode.ACES);
            var ca = Comp<ColorAdjustments>(prof);
            ca.postExposure.Override(1.0f);
            ca.contrast.Override(14f);
            ca.saturation.Override(-16f);
            ca.colorFilter.Override(Color.white);
            var st = Comp<SplitToning>(prof);
            st.shadows.Override(new Color(0.36f, 0.45f, 0.58f));
            st.highlights.Override(new Color(0.63f, 0.53f, 0.41f));
            st.balance.Override(-12f);
            var bloom = Comp<Bloom>(prof);
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.93f, 0.85f));
            var vig = Comp<Vignette>(prof);
            vig.intensity.Override(0.36f);
            vig.smoothness.Override(0.42f);
            var grain = Comp<FilmGrain>(prof);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.22f);
            grain.response.Override(0.75f);
            EditorUtility.SetDirty(prof);
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
            pl.intensity = 3f;
            pl.range = 11f;
        }

        // ---------- lame di luna ----------
        /// <summary>Un cubo che contiene tutte le lame (dal lucernario a terra lungo la direzione della luna) con lo
        /// shader a ray marching, piu' la polvere che brilla solo dentro la luce.</summary>
        static void MoonShafts()
        {
            Vector3 L = MoonDir.normalized;
            Vector3 off = L * (CeilY / -L.y);   // da dove entra la luce a dove tocca terra
            float x0 = Mathf.Min(SkyX0, SkyX0 + off.x) - 0.5f, x1 = Mathf.Max(SkyX1, SkyX1 + off.x) + 0.5f;
            float z0 = Mathf.Min(SkyZ0, SkyZ0 + off.z) - 0.5f, z1 = Mathf.Max(SkyZ1, SkyZ1 + off.z) + 0.5f;
            var center = new Vector3((x0 + x1) / 2f, CeilY / 2f, (z0 + z1) / 2f);
            var size = new Vector3(x1 - x0, CeilY, z1 - z0);

            var vol = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vol.name = "MoonShafts";
            Object.DestroyImmediate(vol.GetComponent<BoxCollider>());
            vol.transform.position = center;
            vol.transform.localScale = size;
            var mr = vol.GetComponent<MeshRenderer>();
            mr.sharedMaterial = ShaftMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // resta non statico: lo shader usa la trasformazione del cubo, lo static batching la cancellerebbe

            var dust = new GameObject("MoonDust");
            dust.transform.position = center;
            var ps = dust.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.03f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startColor = Color.white;
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.0004f;
            var em = ps.emission;
            em.rateOverTime = 2000f / 18f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.04f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.04f;
            noise.damping = true;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var pr = dust.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = DustMaterial();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.maxParticleSize = 0.02f;     // niente minimo: i granelli lontani devono sparire, non diventare stelle
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.sortMode = ParticleSystemSortMode.None;
        }

        // ---------- acqua e sporco (fase 3) ----------
        /// <summary>Quad decorativo: niente collider, niente ombre, niente luce cotta (non statico).</summary>
        static GameObject Decal(string name, Material m, Vector3 pos, Vector3 euler, Vector2 size)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.position = pos;
            q.transform.rotation = Quaternion.Euler(euler);
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return q;
        }

        /// <summary>Pozzanghere sotto i vetri rotti del lucernario (e due davanti allo scalone, dove cade la luna):
        /// acqua scura e quasi a specchio, riflette la hall dalla sonda. Sotto ognuna un alone di sporco bagnato.</summary>
        static void Puddles()
        {
            var shape = AssetDatabase.LoadAssetAtPath<Texture2D>(HotelTex + "pozzanghera.png");
            var stainTex = AssetDatabase.LoadAssetAtPath<Texture2D>(HotelTex + "macchia.png");
            var grime = Shader.Find("PryingEyes/Grime");
            if (shape == null || stainTex == null || grime == null) return;
            var water = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            water.SetTexture("_BaseMap", shape);
            // acqua vera: si vede il marmo sotto (alfa bassa), il riflesso resta pieno (URP "preserve specular")
            water.color = new Color(0.03f, 0.035f, 0.04f, 0.38f);
            water.SetFloat("_Surface", 1f);
            water.SetFloat("_Blend", 0f);
            water.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            water.SetFloat("_Metallic", 0f);
            water.SetFloat("_Smoothness", 0.97f);
            water.renderQueue = 3000;
            SaveMat("Pozzanghera", water);
            var wet = new Material(grime);
            wet.SetTexture("_MainTex", stainTex);
            wet.SetColor("_Color", new Color(0.60f, 0.58f, 0.56f));   // bagnato: piu' scuro, non marrone
            wet.SetFloat("_Strength", 0.7f);
            SaveMat("SporcoBagnato", wet);
            // sul marmo (meta' est del lucernario, davanti allo scalone, due gradini): acqua che specchia
            var spots = new (float x, float y, float z, float size, float rot)[]
            {
                (7.6f, 0f, -6.8f, 2.4f, -15f), (9.6f, 0f, -4.2f, 1.6f, 40f), (5.4f, 0f, -8.6f, 1.3f, 10f),
                (10.2f, 0f, -8.2f, 1.1f, 65f), (5.8f, 0f, -4.4f, 1.0f, -50f),
                (-1.2f, 0f, -0.9f, 1.2f, 15f), (2.8f, 0f, -0.6f, 0.9f, -40f),
                (-1.5f, 0.35f * 6f, -2.375f - 5f * 0.75f, 0.55f, 0f), (2.0f, 0.35f * 9f, -2.375f - 8f * 0.75f, 0.5f, 30f),
            };
            foreach (var s in spots)
            {
                Decal("AloneBagnato", wet, new Vector3(s.x, s.y + 0.002f, s.z), new Vector3(90f, s.rot + 37f, 0f), Vector2.one * s.size * 1.6f);
                Decal("Pozzanghera", water, new Vector3(s.x, s.y + 0.004f, s.z), new Vector3(90f, s.rot, 0f), new Vector2(s.size, s.size * 0.8f));
            }
            // meta' ovest del lucernario: c'e' la moquette (2 cm sopra il pavimento), l'acqua l'ha impregnata
            var soaked = new (float x, float z, float size, float rot)[]
            {
                (-8.0f, -6.2f, 3.0f, 20f), (-5.6f, -3.8f, 1.8f, 70f), (-10.0f, -8.3f, 2.2f, -30f), (-6.5f, -8.5f, 1.6f, 5f),
            };
            foreach (var s in soaked)
                Decal("MoquetteBagnata", wet, new Vector3(s.x, 0.022f, s.z), new Vector3(90f, s.rot, 0f), new Vector2(s.size, s.size * 0.8f));
        }

        /// <summary>Sporco che non si ripete: fascia scura alla base dei muri, aloni d'acqua sulle pareti alte e sul
        /// soffitto attorno al lucernario, chiazze negli angoli. Posizioni con seme fisso.</summary>
        static void Grime()
        {
            var sh = Shader.Find("PryingEyes/Grime");
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(HotelTex + "sporco_base.png");
            var stainTex = AssetDatabase.LoadAssetAtPath<Texture2D>(HotelTex + "macchia.png");
            if (sh == null || baseTex == null || stainTex == null) return;
            Material Mk(string name, Texture2D tex, Color c, float k)
            {
                var m = new Material(sh);
                m.SetTexture("_MainTex", tex);
                m.SetColor("_Color", c);
                m.SetFloat("_Strength", k);
                return SaveMat(name, m);
            }
            var foot = Mk("SporcoBase", baseTex, new Color(0.34f, 0.29f, 0.24f), 0.8f);
            var stain = Mk("AloneAcqua", stainTex, new Color(0.50f, 0.40f, 0.30f), 0.7f);
            var dirt = Mk("SporcoAngoli", stainTex, new Color(0.45f, 0.40f, 0.34f), 0.6f);
            var rnd = new System.Random(77);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            const float off = 0.003f, wains = 0.12f;

            // fascia alla base: segmenti da 4 m, a filo della boiserie (nord/sud) o del muro (est/ovest)
            for (float x = -32f; x <= 32.1f; x += 4f)
            {
                Decal("SporcoBase", foot, new Vector3(x, 0.7f, -HZ + wains + off), new Vector3(0f, 180f, 0f), new Vector2(4.1f, 1.4f));
                if (Mathf.Abs(x) > 3.5f)   // non davanti all'ingresso
                    Decal("SporcoBase", foot, new Vector3(x, 0.7f, HZ - wains - off), Vector3.zero, new Vector2(4.1f, 1.4f));
            }
            for (float z = -20f; z <= 20.1f; z += 4f)
            {
                if (Mathf.Abs(z) > 6.5f)   // non sugli ascensori
                    Decal("SporcoBase", foot, new Vector3(-HX + off, 0.7f, z), new Vector3(0f, -90f, 0f), new Vector2(4.1f, 1.4f));
                Decal("SporcoBase", foot, new Vector3(HX - off, 0.7f, z), new Vector3(0f, 90f, 0f), new Vector2(4.1f, 1.4f));
            }

            // aloni d'acqua sulle pareti alte (lontano da finestre e ascensori)
            for (int i = 0; i < 18; i++)
            {
                int wall = rnd.Next(4);
                float along = R(-30f, 30f), h = R(2.6f, 8.6f), size = R(1.2f, 3.2f);
                if (wall == 3 && Mathf.Abs(along) < 11f && h < 6.8f) h = R(6.8f, 8.6f);   // sopra le finestre
                if (wall == 2 && Mathf.Abs(along) < 6.5f && h < 3.8f) h = R(3.8f, 8.6f);  // sopra gli ascensori
                Vector3 p, e;
                if (wall == 0) { p = new Vector3(along, h, -HZ + off); e = new Vector3(0f, 180f, R(0f, 360f)); }
                else if (wall == 1) { p = new Vector3(along, h, HZ - off); e = new Vector3(0f, 0f, R(0f, 360f)); }
                else if (wall == 2) { p = new Vector3(-HX + off, h, along * 0.7f); e = new Vector3(0f, -90f, R(0f, 360f)); }
                else { p = new Vector3(HX - off, h, along * 0.7f); e = new Vector3(0f, 90f, R(0f, 360f)); }
                Decal("AloneAcqua", stain, p, e, new Vector2(size, size * R(0.7f, 1.3f)));
            }
            // soffitto: infiltrazioni attorno al lucernario e qualche alone sparso
            for (int i = 0; i < 14; i++)
            {
                bool nearSky = i < 9;
                float x = nearSky ? R(-15f, 15f) : R(-32f, 32f);
                float z = nearSky ? (rnd.NextDouble() < 0.5 ? R(-12.5f, -9.2f) : R(-2.8f, 0.5f)) : R(-21f, 21f);
                if (!nearSky && z > -9.5f && z < -2.5f) z += 8f;    // non dentro l'apertura
                float size = R(1.5f, 4f);
                Decal("AloneSoffitto", stain, new Vector3(x, CeilY - off, z), new Vector3(-90f, R(0f, 360f), 0f), new Vector2(size, size * R(0.7f, 1.3f)));
            }
            // chiazze di sporco a terra negli angoli e lungo i muri
            var corners = new[] { new Vector2(-33.5f, -21.5f), new Vector2(33.5f, -21.5f), new Vector2(-33.5f, 21.5f), new Vector2(33.5f, 21.5f),
                                  new Vector2(-20f, -21.8f), new Vector2(15f, 21.7f), new Vector2(33.8f, 12f), new Vector2(-33.8f, 12f) };
            foreach (var c in corners)
            {
                float size = R(2f, 3.5f);
                Decal("SporcoAngolo", dirt, new Vector3(c.x, 0.002f, c.y), new Vector3(90f, R(0f, 360f), 0f), new Vector2(size, size * R(0.6f, 1f)));
            }
        }

        static Material ShaftMaterial()
        {
            const string path = "Assets/_PryingEyes/Materials/MoonShafts.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("PryingEyes/MoonShafts"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_NoiseTex", Noise3D());
            ApplyShaftParams(m);
            return m;
        }

        static void ApplyShaftParams(Material m)
        {
            m.SetColor("_Color", new Color(0.62f, 0.74f, 0.95f));
            m.SetFloat("_Density", 0.05f);
            m.SetFloat("_Steps", 28f);
            m.SetFloat("_NoiseScale", 0.22f);
            m.SetFloat("_NoiseAmount", 0.65f);
            m.SetVector("_Drift", new Vector4(0.06f, -0.015f, 0.035f, 0f));
            m.SetFloat("_Anisotropy", 0.35f);
            m.SetFloat("_MaxBrightness", 1.5f);
            EditorUtility.SetDirty(m);
        }

        static Material DustMaterial()
        {
            const string path = "Assets/_PryingEyes/Materials/DustMotes.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("PryingEyes/DustMotes"));
                AssetDatabase.CreateAsset(m, path);
            }
            ApplyDustParams(m);
            return m;
        }

        static void ApplyDustParams(Material m)
        {
            m.SetColor("_Color", new Color(0.78f, 0.85f, 1f));
            m.SetFloat("_Intensity", 2.0f);
            m.SetFloat("_FadeStart", 5f);
            m.SetFloat("_FadeEnd", 12f);
            EditorUtility.SetDirty(m);
        }

        /// <summary>Rumore a valori 3D periodico (32^3, due ottave) per far muovere la polvere nelle lame.</summary>
        static Texture3D Noise3D()
        {
            const string path = "Assets/_PryingEyes/Textures/Noise3D.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
            if (existing != null) return existing;
            const int N = 32;
            var rnd = new System.Random(99);
            float[] Lattice(int p) { var a = new float[p * p * p]; for (int i = 0; i < a.Length; i++) a[i] = (float)rnd.NextDouble(); return a; }
            float Sample(float[] lat, int p, float x, float y, float z)
            {
                x *= p; y *= p; z *= p;
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
                float fx = x - xi, fy = y - yi, fz = z - zi;
                fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
                float V(int i, int j, int k) => lat[((k % p + p) % p) * p * p + ((j % p + p) % p) * p + ((i % p + p) % p)];
                float x00 = Mathf.Lerp(V(xi, yi, zi), V(xi + 1, yi, zi), fx);
                float x10 = Mathf.Lerp(V(xi, yi + 1, zi), V(xi + 1, yi + 1, zi), fx);
                float x01 = Mathf.Lerp(V(xi, yi, zi + 1), V(xi + 1, yi, zi + 1), fx);
                float x11 = Mathf.Lerp(V(xi, yi + 1, zi + 1), V(xi + 1, yi + 1, zi + 1), fx);
                return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
            }
            var l4 = Lattice(4); var l8 = Lattice(8);
            var px = new Color32[N * N * N];
            for (int z = 0; z < N; z++)
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (float)x / N, v = (float)y / N, w = (float)z / N;
                        float n = 0.65f * Sample(l4, 4, u, v, w) + 0.35f * Sample(l8, 8, u, v, w);
                        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(n) * 255f);
                        px[z * N * N + y * N + x] = new Color32(b, b, b, 255);
                    }
            var tex = new Texture3D(N, N, N, TextureFormat.R8, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Noise3D" };
            tex.SetPixels32(px);
            tex.Apply(false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        // ---------- luce cotta ----------
        /// <summary>Prepara la scena al bake: scala dentro le mesh (UV di lightmap proporzionali alle superfici vere:
        /// con un cubo unitario scalato il pavimento da 70x46 m avrebbe avuto un sesto dei texel), luci Mixed
        /// (diretta e ombre in tempo reale, rimbalzi cotti), griglia di sonde per chi si muove, impostazioni.</summary>
        static void PrepareGI()
        {
            var meshes = new HashSet<Mesh>();
            int count = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var go = mf.gameObject;
                if (mf.sharedMesh == null || !GameObjectUtility.AreStaticEditorFlagsSet(go, StaticEditorFlags.ContributeGI)) continue;
                var t = go.transform;
                bool unitParent = t.parent == null || (t.parent.lossyScale - Vector3.one).sqrMagnitude < 1e-6f;
                Vector3 s = unitParent ? t.localScale : Vector3.one;
                var src = mf.sharedMesh;
                // una mesh per oggetto: con le UV in metri dipende anche da dove sta l'oggetto
                var m = ScaledCopy(src, s);
                if (s != Vector3.one)
                {
                    t.localScale = Vector3.one;
                    foreach (var c in go.GetComponents<Collider>()) ScaleCollider(c, s);
                }
                MetricUV(m, src.name, t);
                Unwrapping.GenerateSecondaryUVSet(m);
                mf.sharedMesh = m;
                meshes.Add(m);
                count++;
            }
            var cache = meshes;   // per il log qui sotto

            int mixed = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.GetComponentInParent<PryingEyes.PlayerController>() != null) continue; // fiammata dell'arma: realtime
                if (l.lightmapBakeType == LightmapBakeType.Baked) continue;                  // applique: tutte cotte
                if (keepRealtime.Contains(l)) { l.lightmapBakeType = LightmapBakeType.Realtime; continue; }
                l.lightmapBakeType = LightmapBakeType.Mixed;
                mixed++;
            }

            Physics.SyncTransforms();
            // due sonde dentro la cabina dell'ascensore: il monaco che ne esce non resta nero
            var pts = new List<Vector3> { new Vector3(-HX - 2.3f, 1.0f, ElevZ), new Vector3(-HX - 2.3f, 2.2f, ElevZ) };
            foreach (float y in new[] { 0.5f, 1.8f, 3.4f, 5.2f, 7.2f })
                for (float x = -33.25f; x <= 33.3f; x += 3.5f)
                    for (float z = -21.5f; z <= 21.6f; z += 3.5f)
                    {
                        var p = new Vector3(x, y, z);
                        if (!Physics.CheckSphere(p, 0.3f)) pts.Add(p);   // niente sonde dentro scale, banco, colonne, divani
                    }
            var lpg = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
            lpg.probePositions = pts.ToArray();

            const string lsPath = "Assets/_PryingEyes/Settings/HotelLighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "HotelLighting" };
                AssetDatabase.CreateAsset(ls, lsPath);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.autoGenerate = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 8f;      // texel per metro: per i rimbalzi bastano, il dettaglio fine lo fa l'SSAO
            ls.lightmapMaxSize = 2048;
            ls.lightmapPadding = 4;
            ls.directionalityMode = LightmapsMode.CombinedDirectional;
            ls.lightmapCompression = LightmapCompression.HighQuality;
            ls.ao = true;
            ls.aoMaxDistance = 1.2f;
            ls.aoExponentIndirect = 1f;
            ls.aoExponentDirect = 0f;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 512;
            ls.environmentSampleCount = 256;
            ls.maxBounces = 4;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            ls.indirectScale = 1.2f;
            ls.albedoBoost = 1f;
            EditorUtility.SetDirty(ls);
            AssetDatabase.SaveAssets();
            Lightmapping.lightingSettings = ls;
            Debug.Log($"[PryingEyes] GI prep: {count} renderer in lightmap ({cache.Count} mesh), {mixed} luci Mixed, {pts.Count} sonde");
        }

        /// <summary>UV in metri, cosi' una ripetizione di texture ha sempre la stessa misura vera (prima ogni faccia
        /// di ogni cubo andava da 0 a 1: la scacchiera di 70 m e il gradino di 8 m avevano texel diversissimi).
        /// Cubi e quad: proiezione a scatola in coordinate del mondo (le venature continuano tra gradini vicini),
        /// orientata come la vede chi guarda la faccia. Cilindri: arco e altezza sul fianco, pianta sui tappi.
        /// Sfere e capsule (cuscini): le UV originali scalate alla circonferenza. Va dopo che la scala e' nella mesh.</summary>
        static void MetricUV(Mesh m, string kind, Transform t)
        {
            var v = m.vertices;
            var n = m.normals;
            var uv = m.uv;
            if (n == null || n.Length != v.Length) return;
            var o = new Vector2[v.Length];
            var b = m.bounds;
            if (kind == "Cylinder")
            {
                float r = Mathf.Max(b.extents.x, b.extents.z), h = b.size.y;
                for (int i = 0; i < v.Length; i++)
                {
                    if (Mathf.Abs(n[i].y) > 0.5f) { var w = t.TransformPoint(v[i]); o[i] = new Vector2(w.x, w.z); }
                    else o[i] = new Vector2(uv[i].x * 2f * Mathf.PI * r, uv[i].y * h);
                }
            }
            else if (kind == "Sphere" || kind == "Capsule")
            {
                float c = Mathf.PI * (b.size.x + b.size.z) * 0.5f;
                for (int i = 0; i < v.Length; i++) o[i] = new Vector2(uv[i].x * c, uv[i].y * b.size.y * 1.6f);
            }
            else
            {
                for (int i = 0; i < v.Length; i++)
                {
                    var w = t.TransformPoint(v[i]);
                    var wn = t.TransformDirection(n[i]);
                    float ax = Mathf.Abs(wn.x), ay = Mathf.Abs(wn.y), az = Mathf.Abs(wn.z);
                    if (ay >= ax && ay >= az) o[i] = new Vector2(w.x, w.z);
                    else if (ax >= az) o[i] = new Vector2(wn.x > 0f ? w.z : -w.z, w.y);
                    else o[i] = new Vector2(wn.z > 0f ? -w.x : w.x, w.y);
                }
            }
            m.uv = o;
            m.RecalculateTangents();   // le normal map seguono le UV: tangenti da rifare
        }

        static Mesh ScaledCopy(Mesh src, Vector3 s)
        {
            var m = Object.Instantiate(src);
            m.name = src.name + "_" + s.x.ToString("F2") + "x" + s.y.ToString("F2") + "x" + s.z.ToString("F2");
            if (s == Vector3.one) return m;
            var inv = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            var v = m.vertices; var n = m.normals; var tg = m.tangents;
            for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], s);
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.Scale(n[i], inv).normalized;
            for (int i = 0; i < tg.Length; i++)
            {
                var d = Vector3.Scale(new Vector3(tg[i].x, tg[i].y, tg[i].z), s).normalized;
                tg[i] = new Vector4(d.x, d.y, d.z, tg[i].w);
            }
            m.vertices = v; m.normals = n; m.tangents = tg;
            m.RecalculateBounds();
            return m;
        }

        static void ScaleCollider(Collider c, Vector3 s)
        {
            if (c is BoxCollider b) { b.center = Vector3.Scale(b.center, s); b.size = Vector3.Scale(b.size, s); }
            else if (c is SphereCollider sp) { sp.center = Vector3.Scale(sp.center, s); sp.radius *= Mathf.Max(s.x, Mathf.Max(s.y, s.z)); }
            else if (c is CapsuleCollider cap)
            {
                cap.center = Vector3.Scale(cap.center, s);
                float along = cap.direction == 0 ? s.x : cap.direction == 1 ? s.y : s.z;
                float across = cap.direction == 0 ? Mathf.Max(s.y, s.z) : cap.direction == 1 ? Mathf.Max(s.x, s.z) : Mathf.Max(s.x, s.y);
                cap.height *= along;
                cap.radius *= across;
            }
        }

        static void BakeLighting()
        {
            var t0 = System.DateTime.Now;
            bool ok = Lightmapping.Bake();
            var lp = LightmapSettings.lightProbes;
            Debug.Log($"[PryingEyes] lightmap bake ok={ok} in {(System.DateTime.Now - t0).TotalSeconds:F0}s " +
                      $"lightmaps={LightmapSettings.lightmaps.Length} sonde={(lp != null ? lp.count : 0)}");
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
            // l'arma del giocatore non deve finire nei riflessi
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
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
            finally { if (player != null) player.SetActive(true); }
        }

        /// <summary>Occhi nel buio: tra punti candidati lungo le pareti, sotto e sopra il soppalco, sceglie i piu' bui
        /// (rimbalzi letti dalle sonde + stima della luce diretta di lampade e luna, con test di visibilita') e li
        /// tiene distanti tra loro. Va chiamato dopo il bake.</summary>
        static void Eyes()
        {
            const string mp = "Assets/_PryingEyes/Materials/EyeGlow.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (mat == null) { mat = new Material(Shader.Find("PryingEyes/EyeGlow")); AssetDatabase.CreateAsset(mat, mp); }
            // abbastanza grandi e luminosi da accendere il bloom: a 9 cm, da 20 m, erano due pixel
            mat.SetColor("_Color", new Color(3.6f, 2.5f, 0.8f));
            mat.SetFloat("_Open", 1f);
            EditorUtility.SetDirty(mat);

            var cands = new List<Vector3>();
            for (float x = -32f; x <= 32.1f; x += 4f)
            {
                cands.Add(new Vector3(x, 1.7f, -21.6f));
                cands.Add(new Vector3(x, 1.7f, 21.6f));
                cands.Add(new Vector3(x, 5.9f, -21.8f));
            }
            for (float z = -20f; z <= 20.1f; z += 4f)
            {
                cands.Add(new Vector3(-33.6f, 1.7f, z));
                cands.Add(new Vector3(33.6f, 1.7f, z));
            }
            for (float x = -30f; x <= 30.1f; x += 6f) cands.Add(new Vector3(x, 1.6f, -18f));

            Physics.SyncTransforms();
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var dirs = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            var shOut = new Color[dirs.Length];
            var scored = new List<(Vector3 p, float lum)>();
            foreach (var c in cands)
            {
                if (Physics.CheckSphere(c, 0.25f)) continue;
                float lum = 0f;
                LightProbes.GetInterpolatedProbe(c, null, out SphericalHarmonicsL2 sh);
                sh.Evaluate(dirs, shOut);
                foreach (var col in shOut) lum += col.grayscale / dirs.Length;
                foreach (var l in lights)
                {
                    if (!l.enabled || l.type == LightType.Directional || keepRealtime.Contains(l)) continue;
                    if (l.GetComponentInParent<PryingEyes.PlayerController>() != null) continue;
                    if (l.lightmapBakeType == LightmapBakeType.Baked) continue;      // gia' dentro le sonde
                    float d = Vector3.Distance(c, l.transform.position);
                    if (d > l.range || !FreeLine(c, l.transform.position)) continue;
                    float att = Mathf.Clamp01(1f - (d / l.range) * (d / l.range));
                    lum += l.intensity / Mathf.Max(d * d, 0.5f) * att * att * l.color.grayscale;
                }
                var sun = RenderSettings.sun;
                if (sun != null && FreeLine(c, c - sun.transform.forward * 40f)) lum += sun.intensity;
                scored.Add((c, lum));
            }
            scored.Sort((a, b) => a.lum.CompareTo(b.lum));
            var rnd = new System.Random(21);
            var chosen = new List<Vector3>();
            foreach (var s in scored)
            {
                if (chosen.Count >= 9) break;
                bool far = true;
                foreach (var q in chosen) if (Vector3.Distance(q, s.p) < 8f) { far = false; break; }
                if (!far) continue;
                chosen.Add(s.p);
                var g = new GameObject("Occhi");
                g.transform.position = s.p + new Vector3(0f, (float)rnd.NextDouble() * 0.4f - 0.2f, 0f);
                // in edit-mode (screenshot) guardano verso il centro della hall; in gioco seguono il giocatore
                g.transform.rotation = Quaternion.LookRotation(g.transform.position - new Vector3(0f, 1.7f, 0f));
                Renderer Eye(float x)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(q.GetComponent<Collider>());
                    q.name = "Occhio";
                    q.transform.SetParent(g.transform, false);
                    q.transform.localPosition = new Vector3(x, 0f, 0f);
                    q.transform.localScale = Vector3.one * 0.16f;
                    var mr = q.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    return mr;
                }
                var we = g.AddComponent<PryingEyes.WatchingEyes>();
                we.left = Eye(-0.1f);
                we.right = Eye(0.1f);
                Debug.Log($"[PryingEyes] occhi a {s.p} (luce stimata {s.lum:F3})");
            }
        }

        static bool FreeLine(Vector3 a, Vector3 b) => !Physics.Linecast(a, b, ~(1 << 2), QueryTriggerInteraction.Ignore);

        static void SpawnPoints()
        {
            var defs = new[] {
                new Vector3(-32, 0, 0), new Vector3(0, 0, 20.5f), new Vector3(30, 0, -19),
                new Vector3(-30, 4.2f, -17), new Vector3(30, 4.2f, -17), new Vector3(0, 0, -6)
            };
            var holder = new GameObject("SpawnPoints");
            foreach (var d in defs)
            {
                var t = new GameObject("SP").transform;
                t.SetParent(holder.transform);
                t.position = d;
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
            // meno metallo (era 0.85): un po' di diffusa, cosi' prende anche la luce delle sonde
            w.bodyMat = LitMat("GunBody", new Color(0.09f, 0.10f, 0.12f), 0.6f, 0.45f);
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
