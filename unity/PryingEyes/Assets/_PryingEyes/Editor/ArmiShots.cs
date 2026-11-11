using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.EditorTools
{
    /// <summary>Foto di verifica delle armi in edit-mode (niente playmode): in mano come le vede il giocatore, in mira, e da
    /// un lato su fondo neutro. Uscita: hotel_build/armi/&lt;id&gt;_{mano,mira,lato}.png.
    /// Da CLI: Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.ArmiShots.Run [-armi id1,id2] [-viste mano,mira,lato] -quit</summary>
    public static class ArmiShots
    {
        const string Out = "C:/Users/danie/Desktop/prying-eyes/hotel_build/armi";

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static void Run()
        {
            ShaderUtil.allowAsyncCompilation = false;
            Directory.CreateDirectory(Out);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string only = Arg("-armi");
            string views = Arg("-viste") ?? "mano,mira,lato";
            var ids = new List<string>();
            foreach (var d in WeaponDefs.All)
                if (only == null || System.Array.IndexOf(only.Split(','), d.id) >= 0) ids.Add(d.id);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.10f, 0.12f);
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            // riflessi scuri come nell'albergo di notte: senza un cubemap il riflesso di default e' chiaro e il polimero esce bianco
            var dark = new Cubemap(16, TextureFormat.RGBA32, false);
            var darkPx = new Color[16 * 16];
            for (int i = 0; i < darkPx.Length; i++) darkPx[i] = new Color(0.035f, 0.035f, 0.04f, 1f);
            for (int f = 0; f < 6; f++) dark.SetPixels(darkPx, (CubemapFace)f);
            dark.Apply();
            RenderSettings.customReflectionTexture = dark;
            RenderSettings.reflectionIntensity = 1f;
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 0.25f; key.color = new Color(1f, 0.92f, 0.82f);
            key.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            key.shadows = LightShadows.None;

            string dbg = Arg("-luce");           // prove: "solofill" = niente ambiente, niente riflessi, niente luce chiave
            if (dbg == "solofill")
            {
                RenderSettings.ambientLight = Color.black; RenderSettings.reflectionIntensity = 0f; key.intensity = 0f;
            }
            else if (dbg == "solokey")
            {
                RenderSettings.ambientLight = Color.black; RenderSettings.reflectionIntensity = 0f; key.intensity = 1f;
            }
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            var ad = cam.GetUniversalAdditionalCameraData();
            ad.renderPostProcessing = false;
            ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // un riferimento lontano per la mira (una colonna chiara a 12 m e un pavimento)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position = new Vector3(0f, -1.6f, 8f); floor.transform.localScale = new Vector3(3f, 1f, 3f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.18f, 0.17f, 0.16f));
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.transform.position = new Vector3(0f, 0.5f, 12f); post.transform.localScale = new Vector3(0.5f, 2.2f, 0.5f);
            post.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.55f, 0.50f, 0.42f));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 1f, 18f); wall.transform.localScale = new Vector3(40f, 8f, 0.5f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.22f, 0.20f, 0.18f));

            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            foreach (var id in ids)
            {
                var def = WeaponDefs.All[WeaponDefs.IndexOf(id)];
                var prefab = Resources.Load<GameObject>("Armi/" + id);
                if (prefab == null) { Debug.LogWarning("[ArmiShots] manca il prefab " + id); continue; }
                foreach (var v in views.Split(','))
                {
                    var rootGo = new GameObject("Viewmodel");
                    if (v == "lato") BuildSide(rootGo, prefab, def, cam);
                    else BuildView(rootGo, prefab, def, cam, v == "mira", v == "ricarica");
                    cam.Render(); cam.Render();
                    Save(rt, Out + "/" + id + "_" + v + ".png");
                    Object.DestroyImmediate(rootGo);
                }
                Debug.Log("[ArmiShots] " + id);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }

        static Material Flat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c; m.SetFloat("_Smoothness", 0.2f);
            return m;
        }

        static void Save(RenderTexture rt, string path)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void SetLayerAndShadows(GameObject g)
        {
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Come in gioco: arma e mani nel riferimento della camera, in mano o in mira.</summary>
        static void BuildView(GameObject root, GameObject prefab, WeaponDef d, Camera cam, bool ads, bool reload = false)
        {
            cam.transform.position = new Vector3(0f, 0f, 0f);
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = ads ? d.adsFov : 75f;
            root.transform.SetParent(cam.transform, false);
            var model = Object.Instantiate(prefab, root.transform);
            model.name = "Arma";
            var rig = model.GetComponent<WeaponRig>();
            rig.Setup();
            SetLayerAndShadows(model);
            Vector3 hip = new Vector3(0.28f, -0.26f, 0.62f);
            if (d.cls == "PISTOLA" || d.cls == "REVOLVER" || d.cls == "PISTOLA MAGNUM" || d.cls == "PISTOLA MITRAGLIATRICE") hip = new Vector3(0.25f, -0.245f, 0.55f);
            Vector3 a = rig.AdsLocal;
            root.transform.localPosition = ads ? new Vector3(0f, 0f, d.adsDist) - a : hip;
            root.transform.localRotation = ads ? Quaternion.identity : Quaternion.Euler(0f, -3.5f, 0f);
            var handL = AddHands(root, rig, d);
            if (reload) PoseReload(root, rig, d, handL);
            if (ads)
            {
                // dove stanno le mani rispetto all'occhio (riferimento della camera: x destra, y su, z avanti), per tarare le maniche
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "mano_d" || t.name == "mano_s")
                    {
                        Vector3 p = cam.transform.InverseTransformPoint(t.position);
                        Debug.Log($"[ArmiMani] {d.id} {t.name} x={p.x:F3} y={p.y:F3} z={p.z:F3} fov={d.adsFov:F0} adsDist={d.adsDist:F3}");
                        // come Weapon.AddHands: la mano destra a meno di 17 cm dall'occhio in mira non si disegna
                        if (t.name == "mano_d" && p.z < 0.17f) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    }
                // la manica destra in coordinate camera: profondita' minima e dove sta la parte tra 5 e 15 cm dall'occhio
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.name.Contains("_manica")) continue;
                    var m = cam.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    float zmin = 9f, zmax = -9f; Vector3 nearC = Vector3.zero; int nn = 0;
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        Vector3 p = m.MultiplyPoint3x4(v);
                        zmin = Mathf.Min(zmin, p.z); zmax = Mathf.Max(zmax, p.z);
                        if (p.z > 0.05f && p.z < 0.15f) { nearC += p; nn++; }
                    }
                    Debug.Log($"[ArmiMani] {d.id} {mf.name} z=[{zmin:F3},{zmax:F3}] vicino n={nn} centro={(nn > 0 ? nearC / nn : Vector3.zero):F3}");
                }
            }
            // luce di riempimento come in gioco
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.32f, 0.55f, -0.35f);
            fill.type = LightType.Point; fill.intensity = Arg("-luce") == "solokey" ? 0f : 0.55f; fill.range = 2.6f; fill.color = new Color(1f, 0.95f, 0.88f);
            SetupScope(rig, d, cam, ads);
        }

        /// <summary>La posa di Weapon.UpdatePose e AnimateParts nel punto piu' basso della ricarica (circa al 40%): arma inclinata,
        /// caricatore sfilato di 24 cm e mano sinistra dove la manda il gioco (stesse formule: se cambiano li', cambiano anche qui).</summary>
        static void PoseReload(GameObject root, WeaponRig rig, WeaponDef d, Transform handL)
        {
            bool tube = d.style == ReloadStyle.Tube;
            Vector3 tiltPos = new Vector3(-0.035f, -0.055f, -0.02f), tiltRot = new Vector3(-15f, 10f, 22f);
            if (tube) { tiltPos = new Vector3(-0.06f, -0.03f, -0.02f); tiltRot = new Vector3(-8f, 30f, 38f); }
            if (d.style == ReloadStyle.Break) { tiltPos = new Vector3(-0.02f, -0.05f, 0f); tiltRot = new Vector3(-6f, 8f, 18f); }
            Vector3 rest = handL != null ? root.transform.InverseTransformPoint(handL.position) : Vector3.zero;
            if (rig.mag != null && !tube && d.style != ReloadStyle.Revolver && d.style != ReloadStyle.Break)
            {
                rig.Offset(rig.mag, (d.magUp ? Vector3.up : Vector3.down) * 0.24f);
                rig.Rotate(rig.mag, new Vector3(18f, 0f, 0f));
            }
            root.transform.localPosition += tiltPos;
            root.transform.localRotation *= Quaternion.Euler(tiltRot);
            if (handL == null) return;
            foreach (var r in handL.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            Vector3 grab = rig.mag != null ? root.transform.InverseTransformPoint(rig.mag.position) + new Vector3(0f, -0.07f, 0.02f) : rest;
            Vector3 target = rest;
            if (d.hand > 0.5f)
            {
                if (rig.mag != null && !tube) target = grab;
                else if (tube || d.style == ReloadStyle.Break || d.style == ReloadStyle.Revolver) target = rest + new Vector3(-0.02f, -0.10f, -0.12f) * 0.8f;
            }
            else target = grab;
            handL.position = root.transform.TransformPoint(target);
        }

        static Transform AddHands(GameObject root, WeaponRig rig, WeaponDef d)
        {
            var mani = Resources.Load<GameObject>("ManiArmi/" + d.id);       // mani su misura (genera_mani2.py), se no il paio unico
            bool onMeasure = mani != null;
            if (!onMeasure) mani = Resources.Load<GameObject>("Mani");
            if (mani == null) return null;
            var g = Object.Instantiate(mani, root.transform);
            g.name = "Mani";
            SetLayerAndShadows(g);
            Transform handL = null, handR = null;
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) { if (t.name == "mano_s") handL = t; else if (t.name == "mano_d") handR = t; }
            if (!onMeasure && handR != null && d.handDrop != 0f) handR.position += root.transform.TransformVector(new Vector3(0f, -d.handDrop, 0f));
            if (handL != null)
            {
                if (!onMeasure)
                {
                    Vector3 hg = new Vector3(0f, 0.005f, 0.31f);
                    Vector3 target = rig.gripL != null ? root.transform.InverseTransformPoint(rig.gripL.position) : hg;
                    handL.position += root.transform.TransformVector(target - hg);
                }
                if (d.hand < 0.5f) foreach (var r in handL.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
            return handL;
        }

        /// <summary>Per le ottiche ingrandite: una seconda camera rende l'immagine sulla lente, come in gioco.</summary>
        static void SetupScope(WeaponRig rig, WeaponDef d, Camera cam, bool ads)
        {
            if (rig.scopeLens == null || d.zoom < 1.4f) return;
            var rend = rig.scopeLens.GetComponent<Renderer>();
            var mf = rig.scopeLens.GetComponent<MeshFilter>();
            float radius = mf != null ? Mathf.Max(mf.sharedMesh.bounds.extents.x, mf.sharedMesh.bounds.extents.z) : 0.018f;
            var rt = new RenderTexture(768, 768, 16, RenderTextureFormat.ARGB32);
            var go = new GameObject("ScopeCam");
            go.transform.SetParent(rig.scopeLens, false);
            var sc = go.AddComponent<Camera>();
            sc.CopyFrom(cam);
            sc.targetTexture = rt;
            sc.nearClipPlane = 0.15f;
            float dist = Mathf.Max(0.03f, Vector3.Distance(cam.transform.position, sc.transform.position));
            float alpha = 2f * Mathf.Atan(radius / dist);
            sc.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(alpha * 0.5f) / d.zoom) * Mathf.Rad2Deg, 1f, 40f);
            sc.transform.rotation = Quaternion.identity;
            sc.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            sc.cullingMask = ~(1 << 2);
            var mat = new Material(rend.sharedMaterial);
            mat.SetTexture("_ScopeTex", rt);
            mat.SetFloat("_Radius", radius);
            mat.SetFloat("_Active", ads ? 1f : 0f);
            rend.sharedMaterial = mat;
            sc.Render();
        }

        /// <summary>Vista laterale su fondo scuro, da destra, con luci morbide: per controllare forma e materiali.</summary>
        static void BuildSide(GameObject root, GameObject prefab, WeaponDef d, Camera cam)
        {
            var model = Object.Instantiate(prefab, root.transform);
            var rig = model.GetComponent<WeaponRig>();
            rig.Setup();
            SetLayerAndShadows(model);
            var rs = model.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            root.transform.position = new Vector3(0f, 0f, 2f) - b.center;
            cam.transform.position = new Vector3(Mathf.Max(b.size.z, 0.6f) * 1.05f + 0.15f, 0f, 2f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            cam.fieldOfView = 36f;
            var l1 = new GameObject("L1").AddComponent<Light>(); l1.transform.SetParent(root.transform, false);
            l1.type = LightType.Point; l1.intensity = 1.1f; l1.range = 4f; l1.transform.position = cam.transform.position + new Vector3(0.1f, 0.6f, -0.5f);
            var l2 = new GameObject("L2").AddComponent<Light>(); l2.transform.SetParent(root.transform, false);
            l2.type = LightType.Point; l2.intensity = 0.6f; l2.range = 4f; l2.color = new Color(0.7f, 0.8f, 1f); l2.transform.position = cam.transform.position + new Vector3(-0.1f, 0.3f, 1.0f);
        }
    }
}
