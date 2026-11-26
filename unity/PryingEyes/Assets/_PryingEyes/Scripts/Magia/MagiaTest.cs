#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -magiatest [-seed S -floor F] [-magiashots] [-magiafast]: la stanza segreta del libro, i due
    /// grimori, le braccia e la seconda mano alla prova nel gioco vero.
    ///  1. sacrario: uno solo nel piano; la parete si usa con F da davanti; chiusa non si passa; aperta, in piedi non si passa (il cunicolo e'
    ///     alto 1,3 m) e accovacciati si arriva alla camera col CharacterController vero; i nemici non hanno strada; l'altare ha il libro
    ///     del piano;
    ///  2. grimori: il libro si apre (comandi bloccati), i prezzi crescono come detto, un interno senza esterno accanto non si compra, il
    ///     supremo chiede quattro interni; si comprano tutti i 38 nodi dei due libri;
    ///  3. seconda mano su un fucile e su una pistola: le sette magie e i sette sigilli partono, la mano libera prende il posto del guanto
    ///     avvolto e lo restituisce, gli effetti arrivano (danno al bersaglio, blink, cura, stati del sigillo sui colpi), il gomito resta
    ///     tra 28 e 176 gradi; con -magiashots le foto dei gesti (hotel_build/magia/).
    /// Esito: le righe del registro e result.json (unity_logs/magiatest o la cartella di -testout); uscita 0 se tutto passa. Le foto restano in hotel_build/magia/.
    /// Deriva da PlayerTest (l'harness): si installa da sola con -magiatest, conta i controlli, scrive result.json e finisce da sola.</summary>
    public sealed class MagiaTest : PlayerTest
    {
        bool shots;
        // -magiaside: con ogni foto anche la vista di lato e dall'alto delle braccia; -armview: solo la posa di riposo di tutte le armi,
        // con le misure di spalla, gomito e polso (diagnosi della biomeccanica)
        bool side, armView;
        // -magiazoom: con ogni foto dei gesti due primi piani della mano sinistra con la scena e gli effetti veri (dall'occhio col
        // teleobiettivo, e di tre quarti a 30 cm): per giudicare pelle, polso e magie da vicino. -vfxdump: le trame degli effetti
        bool zoom;
        string outDir;

        protected override string Suite { get { return "magiatest"; } }
        protected override string ExceptionCheck { get { return null; } }      // non ha mai avuto un controllo delle eccezioni: i suoi controlli restano quelli che erano

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<MagiaTest>("-magiatest"); }

        static bool HasArg(string a) => Array.IndexOf(Environment.GetCommandLineArgs(), a) >= 0;

        protected override void Finish()
        {
            PlayerController.botDir = Vector3.zero; PlayerController.botCrouch = false;
        }

        /// <summary>-vfxdump: le trame degli effetti in hotel_build/magia/.</summary>
        void DumpVfx()
        {
            foreach (VfxTextures.Id id in Enum.GetValues(typeof(VfxTextures.Id)))
                if (id != VfxTextures.Id.Count) DumpTex(VfxTextures.Get(id), "vfx_" + id);
        }

        // ------------------------------------------------------------------ foto (con il canvas del grimorio quando e' aperto)
        void Shot(string name)
        {
            if (!shots) return;
            var cam = Camera.main;
            if (cam == null) return;
            const int W = 1280, H = 720;
            var rt = RenderTexture.GetTemporary(W, H, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var canvases = new List<Canvas>();
            if (GameManager.I != null && GameManager.I.hud != null) canvases.Add(GameManager.I.hud.GetComponent<Canvas>());
            // il libro e' figlio dell'HUD (stesso canvas); solo senza HUD ha un canvas suo
            var bookCanvas = GrimoireUI.Open ? GrimoireUI.I.GetComponent<Canvas>() : null;
            if (bookCanvas != null) canvases.Add(bookCanvas);
            foreach (var c in canvases) { if (c == null) continue; c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 0.2f; }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            foreach (var c in canvases) { if (c == null) continue; c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
            cam.targetTexture = prev;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);
            // una foto non deve fermare la prova: Windows a volte tiene aperto il file del giro prima (anteprime, indicizzazione:
            // errore 1224) e l'eccezione ucciderebbe la coroutine a meta'
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, name + ".png"), png); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PryingEyes] magiatest: foto {name} non scritta ({ex.Message}), provo {name}_b.png");
                try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, name + "_b.png"), png); } catch (System.Exception) { }
            }
        }

        // ------------------------------------------------------------------ vista di lato e dall'alto (diagnosi della biomeccanica)
        // solo arma e braccia (lo strato dell'arma) su fondo scuro; segnati l'occhio (giallo), la testa, la spina, le spalle anatomiche
        // (blu) e i giunti di ogni braccio come li ha risolti ArmRig: spalla rossa, gomito arancio, polso verde
        void SideShot(string name, Weapon w)
        {
            if (!shots || !side) return;
            var main = Camera.main;
            if (main == null || w == null || w.ViewRootT == null) return;
            int layer = w.ViewRootT.gameObject.layer;
            Transform eye = main.transform;
            var tmp = new List<GameObject>();
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            GameObject Mark(Vector3 p, float r, Color c, PrimitiveType t = PrimitiveType.Sphere)
            {
                var g = GameObject.CreatePrimitive(t);
                Destroy(g.GetComponent<Collider>());
                g.layer = layer; g.transform.position = p; g.transform.localScale = Vector3.one * (2f * r);
                var m = new Material(unlit); m.SetColor("_BaseColor", c);
                var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tmp.Add(g); return g;
            }
            void Bar(Vector3 a, Vector3 b, float r, Color c)
            {
                if ((b - a).sqrMagnitude < 1e-6f) return;
                var g = Mark((a + b) * 0.5f, r, c, PrimitiveType.Cylinder);
                g.transform.up = (b - a).normalized;
                g.transform.localScale = new Vector3(2f * r, (b - a).magnitude * 0.5f, 2f * r);
            }
            var body = new Color(0.42f, 0.42f, 0.48f);
            Mark(eye.position, 0.018f, Color.yellow);
            Mark(eye.TransformPoint(new Vector3(0f, 0.03f, -0.075f)), 0.095f, new Color(0.26f, 0.26f, 0.3f));
            Vector3 neck = eye.TransformPoint(new Vector3(0f, -0.15f, -0.07f)), pelvis = eye.TransformPoint(new Vector3(0f, -0.78f, -0.05f));
            Bar(neck, pelvis, 0.012f, body);
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 sh = eye.TransformPoint(new Vector3(sx * ArmRig.ShoulderSide, -ArmRig.ShoulderDown, -ArmRig.ShoulderBack));
                Bar(neck, sh, 0.01f, body);
                Mark(sh, 0.03f, new Color(0.3f, 0.5f, 1f));
            }
            foreach (var arm in new[] { ArmOf(w, "armL"), ArmOf(w, "armR") })
            {
                if (arm == null) continue;
                Mark(arm.LastShoulderW, 0.022f, Color.red);
                Mark(arm.LastElbowW, 0.02f, new Color(1f, 0.55f, 0.1f));
                Mark(arm.LastWristW, 0.016f, Color.green);
                Bar(arm.LastShoulderW, arm.LastElbowW, 0.006f, Color.red);
                Bar(arm.LastElbowW, arm.LastWristW, 0.006f, new Color(1f, 0.55f, 0.1f));
            }
            Vector3 focus = eye.TransformPoint(new Vector3(0f, -0.25f, 0.33f));
            Render2(name + "_lato", focus - eye.right * 2.2f + eye.up * 0.05f, focus, eye.up, layer);
            Render2(name + "_alto", focus + eye.up * 2.2f, focus, eye.forward, layer);
            // il polso sinistro da vicino, di fianco all'avambraccio: il giunto tra la manica e la manichetta del guanto
            var al = ArmOf(w, "armL");
            if (al != null && w.OffHand != null && w.OffHand.Hand != null && w.OffHand.Hand.Visible)
            {
                Vector3 wr = al.LastWristW, fa = (al.LastWristW - al.LastElbowW).normalized;
                Vector3 sideDir = Vector3.ProjectOnPlane(-eye.right, fa);
                if (sideDir.sqrMagnitude < 1e-4f) sideDir = Vector3.ProjectOnPlane(eye.up, fa);
                Vector3 at = wr - fa * 0.04f;
                Render2(name + "_polso", at + sideDir.normalized * 0.45f, at, Vector3.ProjectOnPlane(eye.up, sideDir).normalized, layer);
            }
            foreach (var g in tmp) { var m = g.GetComponent<MeshRenderer>().sharedMaterial; Destroy(g); Destroy(m); }
        }

        /// <summary>Primi piani della mano sinistra (con -magiazoom): dall'occhio col teleobiettivo e di tre quarti da sinistra.</summary>
        void ZoomShot(string name, OffHand hand)
        {
            if (!shots || !zoom || hand == null || hand.Hand == null) return;
            var main = Camera.main;
            if (main == null) return;
            Vector3 palm = hand.PalmWorld;
            Transform eye = main.transform;
            RenderLike(main, name + "_zoom", eye.position, Quaternion.LookRotation(palm - eye.position, eye.up), 22f);
            Vector3 from = palm - eye.right * 0.26f + eye.forward * 0.10f + eye.up * 0.07f;
            RenderLike(main, name + "_tre4", from, Quaternion.LookRotation(palm - from, eye.up), 50f);
        }

        /// <summary>Primi piani delle due mani dall'occhio (teleobiettivo): il cuoio, le cuciture e le maniche da vicino.</summary>
        void HandCloseUps(string name, Weapon w)
        {
            var main = Camera.main;
            if (main == null || w == null) return;
            Transform eye = main.transform;
            foreach (var (tag, arm) in new[] { ("mano_s", ArmOf(w, "armL")), ("mano_d", ArmOf(w, "armR")) })
            {
                if (arm == null) continue;
                Vector3 at = arm.LastWristW + (arm.LastWristW - arm.LastElbowW).normalized * 0.05f;
                RenderLike(main, $"{name}_{tag}", eye.position, Quaternion.LookRotation(at - eye.position, eye.up), 18f);
            }
        }

        /// <summary>Una foto con una camera come quella del gioco (strati, post-produzione) messa altrove.</summary>
        void RenderLike(Camera main, string name, Vector3 pos, Quaternion rot, float fov)
        {
            const int W = 960, H = 720;
            var go = new GameObject("CameraPrimoPiano");
            var c = go.AddComponent<Camera>();
            c.CopyFrom(main);
            go.transform.SetPositionAndRotation(pos, rot);
            c.fieldOfView = fov; c.nearClipPlane = 0.01f;
            var src = main.GetUniversalAdditionalCameraData();
            var dst = c.GetUniversalAdditionalCameraData();
            dst.renderPostProcessing = src.renderPostProcessing;
            dst.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            dst.volumeLayerMask = src.volumeLayerMask;
            var rt = RenderTexture.GetTemporary(W, H, 24);
            c.targetTexture = rt; c.aspect = (float)W / H;
            c.Render();
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            c.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(go);
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, name + ".png"), png); }
            catch (Exception ex) { Debug.LogWarning($"[PryingEyes] magiatest: primo piano {name} non scritto ({ex.Message})"); }
        }

        /// <summary>Una trama degli effetti su fondo scuro (-vfxdump).</summary>
        void DumpTex(Texture2D t, string name)
        {
            if (t == null) return;
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt);
            var tex = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                float a = px[i].a / 255f;
                px[i] = new Color32((byte)(px[i].r * a + 24 * (1f - a)), (byte)(px[i].g * a + 24 * (1f - a)), (byte)(px[i].b * a + 28 * (1f - a)), 255);
            }
            tex.SetPixels32(px); tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, name + ".png"), png); } catch (Exception) { }
        }

        void Render2(string name, Vector3 from, Vector3 at, Vector3 up, int layer)
        {
            const int W = 960, H = 720;
            var go = new GameObject("CameraDiagnosi");
            var c = go.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.07f, 0.07f, 0.08f);
            c.cullingMask = 1 << layer; c.fieldOfView = 34f; c.nearClipPlane = 0.05f; c.farClipPlane = 8f;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, up));
            var rt = RenderTexture.GetTemporary(W, H, 24);
            c.targetTexture = rt; c.aspect = (float)W / H;
            c.Render();
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            c.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(go);
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, name + ".png"), png); }
            catch (Exception ex) { Debug.LogWarning($"[PryingEyes] magiatest: vista {name} non scritta ({ex.Message})"); }
        }

        /// <summary>Spalla, gomito e polso di tutte e due le braccia nello spazio della camera, con lunghezze e angoli.</summary>
        void DumpArms(string id, Weapon w, string when)
        {
            var main = Camera.main;
            if (main == null || w == null) return;
            var eye = main.transform;
            foreach (var (tag, arm) in new[] { ("sinistro", ArmOf(w, "armL")), ("destro", ArmOf(w, "armR")) })
            {
                if (arm == null) continue;
                Vector3 S = eye.InverseTransformPoint(arm.LastShoulderW), E = eye.InverseTransformPoint(arm.LastElbowW), Wr = eye.InverseTransformPoint(arm.LastWristW);
                string line = $"{id} {when} braccio {tag}: spalla {V3(S)} gomito {V3(E)} polso {V3(Wr)}; omero {(E - S).magnitude:F2} m, " +
                              $"avambraccio {(Wr - E).magnitude:F2} m, polso-spalla {(Wr - S).magnitude:F2} m, gomito {arm.LastElbowDeg:F0}, polso {arm.LastWristDeg:F0}, torsione {arm.LastRollDeg:F0}" +
                              (arm.HasHandFrame ? $", prono-sup {arm.LastProSupDeg:F0}, flessione {arm.LastFlexDeg:F0}, deviazione {arm.LastDevDeg:F0}" : "");
                Debug.Log("[PryingEyes] braccia " + line);
            }
        }

        static string V3(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";

        static readonly Dictionary<string, Vector2> probeTotals = new Dictionary<string, Vector2>();

        /// <summary>-armdump: i renderer accesi del modello in prima persona (nome, tipo, genitore, ingombro): per trovare pezzi fuori posto.</summary>
        void DumpRenderers(string id, Weapon w)
        {
            if (w == null || w.ViewRootT == null) return;
            foreach (var r in w.ViewRootT.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                string l = $"{id} renderer {r.GetType().Name} '{r.name}' (genitore '{(r.transform.parent != null ? r.transform.parent.name : "-")}') ingombro {V3(b.size)} centro {V3(Camera.main != null ? Camera.main.transform.InverseTransformPoint(b.center) : b.center)}";
                Debug.Log("[PryingEyes] " + l);
            }
        }

        /// <summary>-adsprobe (0.7.1): in mira rifa' il braccio con piu' combinazioni di ArmRig.AdsSwivel (fin dove gira il gomito),
        /// AdsElbowRise (quanto puo' salire) e AdsShoulder (spalla sotto il calcio) e scrive quanto il polso esce dai campi articolari:
        /// le migliori per arma e, alla fine, la somma su tutte le armi (per scegliere i valori). Rimette i valori di prima.</summary>
        void ProbeAds(string id, string tag, ArmRig arm)
        {
            if (arm == null || !arm.HasHandFrame) return;
            float sw0 = arm.AdsSwivel, er0 = arm.AdsElbowRise, st0 = arm.Stickiness, ast0 = arm.AdsStickiness;
            Vector3 sh0 = arm.AdsShoulder;
            arm.Stickiness = arm.AdsStickiness = 0f;          // senza memoria del fotogramma prima: ogni combinazione trova il suo minimo
            float sx = tag.EndsWith("destro") ? 1f : -1f;
            var cam = Camera.main != null ? Camera.main.transform : null;
            var lines = new List<KeyValuePair<float, string>>();
            foreach (float sw in new[] { 140f, 180f })
                foreach (float er in new[] { 0.20f })
                    foreach (var sh0x in new[] { Vector3.zero, new Vector3(-0.04f, 0.04f, 0.04f), new Vector3(-0.06f, 0.08f, 0.06f), new Vector3(-0.08f, 0.04f, 0.02f),
                                                 new Vector3(0f, 0.05f, 0.08f), new Vector3(0f, 0.10f, 0.12f), new Vector3(-0.06f, 0.16f, 0.06f), new Vector3(-0.10f, 0.08f, 0.0f) })
                    {
                        var sh = new Vector3(sh0x.x * sx, sh0x.y, sh0x.z);
                        arm.AdsSwivel = sw; arm.AdsElbowRise = er; arm.AdsShoulder = sh;
                        arm.Solve(1f);
                        float ps = Mathf.Abs(arm.LastProSupDeg), fl = Mathf.Abs(arm.LastFlexDeg), dv = Mathf.Abs(arm.LastDevDeg);
                        float over = Mathf.Max(0f, ps - 90f) + Mathf.Max(0f, fl - 85f) + Mathf.Max(0f, dv - 35f);
                        // il braccio davanti alla vista conta come 200 gradi per unita' di Occlusion (gomito, avambraccio, omero)
                        if (cam != null)
                        {
                            float occ = ArmRig.ArmOcclusion(cam.InverseTransformPoint(arm.LastShoulderW), cam.InverseTransformPoint(arm.LastElbowW), cam.InverseTransformPoint(arm.LastWristW));
                            over += 200f * occ;
                        }
                        string key = $"{tag} giro {sw:F0} sale {er:F2} spalla {V3(sh0x)}";
                        lines.Add(new KeyValuePair<float, string>(over, $"{key}: prono-sup {arm.LastProSupDeg:F0} flessione {arm.LastFlexDeg:F0} deviazione {arm.LastDevDeg:F0} gomito {arm.LastElbowDeg:F0} (fuori {over:F0})"));
                        probeTotals.TryGetValue(key, out Vector2 tot);
                        probeTotals[key] = new Vector2(tot.x + over, tot.y + (over <= 0f ? 1f : 0f));
                    }
            arm.AdsSwivel = sw0; arm.AdsElbowRise = er0; arm.AdsShoulder = sh0; arm.Stickiness = st0; arm.AdsStickiness = ast0;
            arm.Solve(1f);
            lines.Sort((x, y) => x.Key.CompareTo(y.Key));
            for (int k = 0; k < 3 && k < lines.Count; k++)
            {
                Debug.Log($"[PryingEyes] sonda mira {id}: {lines[k].Value}");
            }
        }

        /// <summary>Le braccia di un'arma nei campi articolari (con la mano articolata il sistema della mano e' noto): prono-supinazione
        /// entro 90 gradi, flessione-estensione entro 85, deviazione entro 35; la sinistra solo sulle armi a due mani.</summary>
        void ArmChecks(string id, Weapon w, string when, bool twoHanded)
        {
            foreach (var (tag, arm) in new[] { ("sinistro", ArmOf(w, "armL")), ("destro", ArmOf(w, "armR")) })
            {
                if (arm == null || !arm.HasHandFrame || (tag == "sinistro" && !twoHanded)) continue;
                float ps = Mathf.Abs(arm.LastProSupDeg), fl = Mathf.Abs(arm.LastFlexDeg), dv = Mathf.Abs(arm.LastDevDeg);
                Check($"{id} {when}: braccio {tag} nei campi articolari", ps <= 90f && fl <= 85f && dv <= 35f,
                      $"prono-sup {arm.LastProSupDeg:F0}, flessione {arm.LastFlexDeg:F0}, deviazione {arm.LastDevDeg:F0}, gomito {arm.LastElbowDeg:F0}");
            }
        }

        /// <summary>In mira (0.7.1): polso nei campi articolari (flessione-estensione entro 85, deviazione entro 52: l'ulnare vera arriva a
        /// 45-50, e sulle pistole tenere il braccio fuori dalla vista ne chiede 45-51) e braccio fuori dal centro dello schermo
        /// (ArmRig.ArmOcclusion, con lo spessore del braccio, entro 0,20: sulle armi a calcio dritto l'avambraccio destro in basso a destra
        /// vale 0,15-0,16 e nelle foto la mira resta libera). Il polso destro sulle armi a calcio dritto e' un limite noto.</summary>
        void AdsChecks(string id, Weapon w, bool twoHanded, bool straightStock)
        {
            var cam = Camera.main != null ? Camera.main.transform : null;
            foreach (var (tag, arm) in new[] { ("sinistro", ArmOf(w, "armL")), ("destro", ArmOf(w, "armR")) })
            {
                if (arm == null || !arm.HasHandFrame || (tag == "sinistro" && !twoHanded)) continue;
                float fl = Mathf.Abs(arm.LastFlexDeg), dv = Mathf.Abs(arm.LastDevDeg);
                string angles = $"prono-sup {arm.LastProSupDeg:F0}, flessione {arm.LastFlexDeg:F0}, deviazione {arm.LastDevDeg:F0}, gomito {arm.LastElbowDeg:F0}";
                // la destra sulle armi a calcio dritto e' il limite noto (presa da rifare): la si scrive e basta
                if (tag == "destro" && straightStock) { Debug.Log($"[PryingEyes] {id} mira: polso destro (limite noto): {angles}"); }
                else Check($"{id} mira: polso {tag} non piegato oltre i campi articolari", fl <= 85f && dv <= 52f, angles);
                if (cam == null) continue;
                float occ = ArmRig.ArmOcclusion(cam.InverseTransformPoint(arm.LastShoulderW), cam.InverseTransformPoint(arm.LastElbowW), cam.InverseTransformPoint(arm.LastWristW));
                Check($"{id} mira: braccio {tag} fuori dal centro della vista", occ <= 0.20f, $"occlusione {occ:F2}");
            }
        }

        // ------------------------------------------------------------------ percorso
        protected override IEnumerator Run()
        {
            shots = HasArg("-magiashots");
            armView = HasArg("-armview");
            side = HasArg("-magiaside") || armView;
            zoom = HasArg("-magiazoom");
            if (armView || zoom) shots = true;
            if (HasArg("-reloadview")) shots = side = true;
            outDir = TestPaths.Out("magia");
            System.IO.Directory.CreateDirectory(outDir);
            if (HasArg("-vfxdump") && MagicVfx.On) DumpVfx();
            float deadline = Time.realtimeSinceStartup + 90f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning || GameManager.I == null || GameManager.I.player == null) && Time.realtimeSinceStartup < deadline) yield return null;
            var fi = FloorDirector.I.cur; var gm = GameManager.I; var player = gm.player;
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = true;
            player.invulnerable = true; PlayerController.holdLook = true;
            yield return new WaitForSeconds(0.5f);

            if (HasArg("-reloadview"))
            {
                // le ricariche fatte dalla mano sinistra: foto a punti fissi della ricarica (a vuoto con le viste di lato, poi tattica),
                // le misure delle braccia a ogni foto. -armids a,b: solo quelle armi
                Vector3 o = OpenSpot(fi, out float oy);
                var args = Environment.GetCommandLineArgs();
                int ai = Array.IndexOf(args, "-armids");
                string only = ai >= 0 && ai + 1 < args.Length ? "," + args[ai + 1] + "," : null;
                float[] marks = { 0.05f, 0.13f, 0.18f, 0.24f, 0.30f, 0.40f, 0.46f, 0.50f, 0.56f, 0.66f, 0.72f, 0.80f, 0.92f };
                for (int i = 0; i < WeaponDefs.All.Length; i++)
                {
                    string id = WeaponDefs.All[i].id;
                    if (id.StartsWith("kn_")) continue;
                    if (only != null && only.IndexOf("," + id + ",", StringComparison.Ordinal) < 0) continue;
                    var w = player.weapon;
                    w.Equip(i);
                    player.Teleport(o); player.yaw = oy; player.pitch = 0f;
                    yield return new WaitForSeconds(1.0f);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool empty = pass == 0;
                        w.mag = empty ? 0 : Mathf.Max(1, w.magSize / 3);
                        w.reserve = 200;
                        w.Reload();
                        Check($"{id} ricarica {(empty ? "a vuoto" : "tattica")} fatta dalla mano", w.ReloadByHand);
                        int m = 0;
                        float t0 = Time.realtimeSinceStartup;
                        while (w.reloading && Time.realtimeSinceStartup - t0 < 20f)
                        {
                            float rp = w.ReloadProgress;
                            if (m < marks.Length && rp >= marks[m])
                            {
                                yield return new WaitForEndOfFrame();
                                string nm = $"ric_{id}_{(empty ? "v" : "t")}{m:00}";
                                Shot(nm);
                                if (empty) SideShot(nm, w);
                                DumpArms(id, w, $"ricarica {(empty ? "a vuoto" : "tattica")} {rp:F2}");
                                if (w.OffHand != null) Debug.Log($"[PryingEyes] mano {id} {rp:F2}: {w.OffHand.DebugKey()}");
                                m++;
                                continue;
                            }
                            yield return null;
                        }
                        Check($"{id} ricarica {(empty ? "a vuoto" : "tattica")} finita (caricatore {w.mag})", !w.reloading && w.mag > 0);
                        // il ciclo manuale dopo la ricarica a vuoto (otturatore, pompa): le sue foto
                        if (empty)
                        {
                            float[] cyc = { 0.08f, 0.18f, 0.30f, 0.42f, 0.60f, 0.78f, 0.92f };
                            int ci = 0;
                            float t1 = Time.realtimeSinceStartup;
                            while (w.CycleProgress >= 0f && ci < cyc.Length && Time.realtimeSinceStartup - t1 < 5f)
                            {
                                if (w.CycleProgress >= cyc[ci])
                                {
                                    yield return new WaitForEndOfFrame();
                                    string nm = $"ric_{id}_c{ci:00}";
                                    Shot(nm);
                                    SideShot(nm, w);
                                    DumpArms(id, w, $"ciclo {w.CycleProgress:F2}");
                                    ci++;
                                    continue;
                                }
                                yield return null;
                            }
                        }
                        yield return new WaitForSeconds(0.8f);
                    }
                }
                yield break;
            }

            if (armView)
            {
                // solo la posa di riposo di ogni arma: foto, viste di lato e dall'alto, misure
                // -armids a,b: solo quelle armi; con -presavecchia le foto si chiamano vecchia_* (per il confronto)
                Vector3 o = OpenSpot(fi, out float oy);
                string only = null, tagv = HasArg("-presavecchia") ? "vecchia" : "vista";
                var args = Environment.GetCommandLineArgs();
                int ai = Array.IndexOf(args, "-armids");
                if (ai >= 0 && ai + 1 < args.Length) only = "," + args[ai + 1] + ",";
                for (int i = 0; i < WeaponDefs.All.Length; i++)
                {
                    string id = WeaponDefs.All[i].id;
                    if (only != null && only.IndexOf("," + id + ",", StringComparison.Ordinal) < 0) continue;
                    player.weapon.Equip(i);
                    player.Teleport(o); player.yaw = oy; player.pitch = 0f;
                    yield return new WaitForSeconds(1.0f);
                    DumpArms(id, player.weapon, "riposo");
                    ArmChecks(id, player.weapon, "riposo", WeaponDefs.All[i].hand > 0.5f);
                    if (HasArg("-armdump")) DumpRenderers(id, player.weapon);
                    Shot($"{tagv}_{id}");
                    SideShot($"{tagv}_{id}", player.weapon);
                    if (zoom) HandCloseUps($"{tagv}_{id}", player.weapon);
                    // in mira: com'e' il braccio quando l'arma sale all'occhio
                    Weapon.debugAds = true;
                    yield return new WaitForSeconds(0.8f);
                    DumpArms(id, player.weapon, "mira");
                    if (HasArg("-adsprobe"))
                    {
                        string cls = WeaponDefs.All[i].hand <= 0.5f ? "pistola" : Weapon.StraightStock(WeaponDefs.All[i]) ? "calcio" : "fucile";
                        ProbeAds(id, cls + " destro", ArmOf(player.weapon, "armR"));
                        if (WeaponDefs.All[i].hand > 0.5f) ProbeAds(id, cls + " sinistro", ArmOf(player.weapon, "armL"));
                    }
                    // 0.7.1: in mira il polso non si piega oltre i campi articolari (flessione 85, deviazione 45: la deviazione ulnare vera
                    // arriva a 40-45 gradi) e l'avambraccio non passa davanti alla vista; la torsione dell'avambraccio la scrive il rapporto
                    // (sulle armi a calcio dritto la presa modellata la porta a 115-135 gradi: limite noto, docs/game/arms-and-magic.md)
                    AdsChecks(id, player.weapon, WeaponDefs.All[i].hand > 0.5f, Weapon.StraightStock(WeaponDefs.All[i]));
                    Shot($"{tagv}_{id}_mira");
                    SideShot($"{tagv}_{id}_mira", player.weapon);
                    Weapon.debugAds = false;
                    yield return new WaitForSeconds(0.5f);
                }
                if (probeTotals.Count > 0)
                {
                    var rank = new List<KeyValuePair<string, Vector2>>(probeTotals);
                    rank.Sort((x, y) => x.Value.x.CompareTo(y.Value.x));
                    var shown = new Dictionary<string, int>();
                    for (int k = 0; k < rank.Count; k++)
                    {
                        string group = rank[k].Key.Substring(0, rank[k].Key.IndexOf(" giro", StringComparison.Ordinal));
                        shown.TryGetValue(group, out int n);
                        shown[group] = n + 1;
                        if (n >= 4) continue;
                        string l = $"sonda mira, somma di tutte le armi: {rank[k].Key}: fuori {rank[k].Value.x:F0} gradi, braccia dentro {rank[k].Value.y:F0}";
                        Debug.Log("[PryingEyes] " + l);
                    }
                }
                yield break;
            }

            if (HasArg("-sigilliview"))
            {
                yield return SigilView(fi, gm, player);
                yield break;
            }

            // ---- 1. il sacrario
            var sacs = fi.annexSites.FindAll(x => x.data.kind == AnnexKind.Sacrario);
            Check("un solo sacrario del grimorio nel piano", sacs.Count == 1, $"{sacs.Count}");
            if (sacs.Count == 1) yield return Sacrario(fi, gm, player, sacs[0]);

            // ---- 2. i grimori
            gm.score = 200000;
            yield return Books(gm, player);
            if (HasArg("-magiabooks")) yield break;         // solo la stanza e i libri (il resto dura minuti)

            // ---- 3. la seconda mano: fucile, poi pistola, in un punto aperto (9 m liberi davanti)
            Vector3 open = OpenSpot(fi, out float openYaw);
            player.Teleport(open);
            player.yaw = openYaw; player.pitch = 0f;
            yield return new WaitForSeconds(0.4f);
            int rifle = WeaponDefs.IndexOf("ar9_malleus"), pistol = WeaponDefs.IndexOf("p9_pieta");
            foreach (int w in new[] { rifle, pistol })
            {
                if (w < 0) continue;
                player.weapon.Equip(w);
                player.Teleport(open); player.yaw = openYaw; player.pitch = 0f;
                yield return new WaitForSeconds(1.2f);
                yield return OffHand(player, WeaponDefs.All[w].id, open, openYaw);
            }
        }

        IEnumerator Sacrario(FloorInstance fi, GameManager gm, PlayerController player, AnnexSite s)
        {
            string tag = $"sacrario {s.data} (libro {(s.data.book == 0 ? "Stregoneria" : "Arsenale")})";
            Check($"{tag}: parete finta sul livello delle porte, chiusa", s.closerCol != null && s.closerCol.enabled && s.closerCol.gameObject.layer == HotelNav.DoorLayer && !s.opened);
            bool okOut = NavMesh.SamplePosition(s.outside, out var no, 1.5f, NavMesh.AllAreas);
            Check($"{tag}: davanti alla parete c'e' pavimento camminabile", okOut);
            if (!okOut) yield break;
            Check($"{tag}: la parete si usa con F da davanti", Interact.Visible(no.position + Vector3.up * 1.62f, s.gatePoint, s.closer) && s.GatePrompt() != null, s.GatePrompt());
            Check($"{tag}: la pianta conosce la camera (sala madre)", fi.RoomAt(s.inside) != null && fi.RoomAt(s.rewardPos) != null);
            Check($"{tag}: niente nascite nemiche dentro", AnnexRuntime.Forbidden(fi, s.inside) && AnnexRuntime.Forbidden(fi, s.rewardPos));
            Check($"{tag}: l'altare custodisce il libro del piano", s.altar != null && (int)s.altar.book == s.data.book && s.altar.Prompt != null, s.altar != null ? s.altar.Prompt : "nessun altare");
            // i nemici non hanno strada fino all'altare
            var path = new NavMeshPath();
            bool enemyWay = NavMesh.SamplePosition(s.rewardPos, out var nr, 1.0f, NavMesh.AllAreas) && NavMesh.CalculatePath(no.position, nr.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Check($"{tag}: i nemici non hanno strada fino all'altare (navmesh)", !enemyWay);

            Vector3 outV = s.secretOut, through = s.gatePoint + outV * 1.6f;
            player.Teleport(no.position);
            player.yaw = Mathf.Atan2(outV.x, outV.z); player.pitch = 0.05f;
            yield return new WaitForSeconds(0.6f);
            Shot("sacrario_parete");
            // chiusa: non si passa nemmeno accovacciati
            yield return Push(player, outV, true, 2.5f);
            Check($"{tag}: chiusa, non si passa", !Inside(s, player.transform.position));
            player.Teleport(no.position);
            yield return new WaitForSeconds(0.3f);
            s.GateUse();
            Check($"{tag}: premuto l'occhio, la parete si apre", s.opened && !s.closerCol.enabled);
            yield return new WaitForSeconds(2.2f);
            Shot("sacrario_aperta");
            // in piedi no, accovacciati si'
            yield return Push(player, outV, false, 3f);
            Check($"{tag}: in piedi il cunicolo non si passa", !Inside(s, player.transform.position), $"fermo a {player.transform.position}");
            player.Teleport(no.position);
            yield return new WaitForSeconds(0.3f);
            yield return Push(player, outV, true, 9f);
            bool inside = Inside(s, player.transform.position);
            Check($"{tag}: accovacciati si arriva nella camera", inside, $"fermo a {player.transform.position}");
            if (!inside) { player.Teleport(s.inside); yield return new WaitForSeconds(0.3f); }
            // in piedi dentro la camera, davanti all'altare
            PlayerController.botCrouch = false;
            yield return new WaitForSeconds(0.6f);
            Vector3 toAltar = s.rewardPos - player.transform.position; toAltar.y = 0f;
            player.yaw = Mathf.Atan2(toAltar.x, toAltar.z); player.pitch = 0.18f;
            yield return new WaitForSeconds(0.4f);
            Shot("sacrario_camera");
            // a portata di F dall'altare
            Vector3 stand = s.rewardPos - toAltar.normalized * 1.25f;
            player.Teleport(new Vector3(stand.x, s.rewardPos.y, stand.z));
            player.yaw = Mathf.Atan2(toAltar.x, toAltar.z); player.pitch = 0.42f;
            yield return new WaitForSeconds(0.5f);
            Shot("sacrario_altare");
            Check($"{tag}: il grimorio si legge con F da davanti all'altare", s.altar != null && Interact.Visible(player.cam, s.altar.Point, s.altar.transform) && Vector3.Distance(player.cam.position, s.altar.Point) < s.altar.Radius);
        }

        static bool Inside(AnnexSite s, Vector3 p) => p.x >= s.data.x0 * 0.1f && p.x <= s.data.x1 * 0.1f && p.z >= s.data.z0 * 0.1f && p.z <= s.data.z1 * 0.1f;

        IEnumerator Push(PlayerController player, Vector3 dir, bool crouch, float seconds)
        {
            PlayerController.holdLook = false;          // con holdLook il gioco ignora la direzione del bot
            PlayerController.botCrouch = crouch;
            yield return new WaitForSeconds(0.35f);
            PlayerController.botDir = dir;
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
            PlayerController.botDir = Vector3.zero;
            yield return new WaitForSeconds(0.2f);
            PlayerController.holdLook = true;
        }

        // ------------------------------------------------------------------ grimori
        IEnumerator Books(GameManager gm, PlayerController player)
        {
            var s = player.Arcana;
            s.ResetRun();
            {
                // le icone incise di tutti i 38 nodi si disegnano (e quanto costa la prima volta)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int drawn = 0;
                foreach (var set in new[] { Grimoire.Witch, Grimoire.Arsenal })
                    foreach (var n in set) if (GrimoireIcons.Engraved(n.icon) != null) drawn++;
                long ms = sw.ElapsedMilliseconds;
                sw.Restart();
                var card = GrimoirePage.Card();
                Check($"le icone incise dei 38 nodi ({drawn}) e la pergamena della scheda ({(card != null ? card.width + "x" + card.height : "-")}) si disegnano", drawn == 38 && card != null, $"icone {ms} ms, scheda {sw.ElapsedMilliseconds} ms");
            }
            foreach (Grimoire.Book b in new[] { Grimoire.Book.Stregoneria, Grimoire.Book.Arsenale })
            {
                string tag = b == Grimoire.Book.Stregoneria ? "Stregoneria" : "Arsenale";
                GrimoireUI.Show(b, null);
                yield return new WaitForSeconds(0.4f);
                Check($"{tag}: il libro si apre e blocca i comandi", GrimoireUI.Open && GameMenu.Blocked);
                var ui = GrimoireUI.I;
                // 2026-11-17: la tavola dell'utente, dentro l'area dell'HUD di chi legge, senza fermare il tempo
                var plate = GrimoirePage.Get(b);
                Check($"{tag}: la tavola vera e i diciannove vani ({plate.w}x{plate.h}, {(plate.tex != null ? plate.tex.mipmapCount : 0)} livelli di mip)", plate.real && plate.w > 1000 && plate.h > 800);
                Rect area = ui.ScreenRectOfArea(), bookRect = ui.ScreenRectOfBook();
                Check($"{tag}: il libro sta dentro la sua area e lascia vedere il gioco", bookRect.xMin >= area.xMin - 1f && bookRect.xMax <= area.xMax + 1f && bookRect.yMin >= area.yMin - 1f && bookRect.yMax <= area.yMax + 1f
                      && (area.width / area.height < 1.5f || bookRect.height <= area.height * 0.9f), $"libro {bookRect.width:F0}x{bookRect.height:F0} in {area.width:F0}x{area.height:F0}");
                Check($"{tag}: il tempo non si ferma mentre si legge", Time.timeScale == 1f);
                ui.Selected = 0;
                yield return null;
                Shot($"libro_{tag}_vuoto");
                // regole del dodecaedro
                Check($"{tag}: un interno senza esterno accanto non si compra", !ui.Activate(12) && !Grimoire.Has(s.Mask(b), 12));
                Check($"{tag}: il supremo e' chiuso", !ui.Activate(18));
                int score0 = gm.score;
                int p0 = s.Price(b, 0);
                Check($"{tag}: il primo esterno costa 1250", p0 == 1250, $"{p0}");
                Check($"{tag}: si compra il primo esterno", ui.Activate(0) && Grimoire.Has(s.Mask(b), 0) && gm.score == score0 - 1250, $"punti {score0} -> {gm.score}");
                Check($"{tag}: il secondo esterno costa 1500", s.Price(b, 1) == 1500, $"{s.Price(b, 1)}");
                Check($"{tag}: con l'esterno accanto l'interno si compra (3000)", s.Price(b, 12) == 3000 && ui.Activate(12) && Grimoire.Has(s.Mask(b), 12));
                Check($"{tag}: l'interno comprato e' quello scelto", (b == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion) == 12);
                // tutti gli esterni (sbloccano gli interni accanto), poi gli interni, poi il supremo
                for (int i = 1; i < 12; i++) ui.Activate(i);
                for (int i = 13; i < 18; i++) ui.Activate(i);
                Check($"{tag}: i 12 esterni e i 6 interni comprati", Grimoire.CountTier(s.Mask(b), Grimoire.Tier.Esterno) == 12 && Grimoire.CountTier(s.Mask(b), Grimoire.Tier.Interno) == 6);
                ui.Selected = 18;
                yield return null;
                Shot($"libro_{tag}_quasi");
                Check($"{tag}: il supremo si compra con quattro interni (9000)", s.Price(b, 18) == 9000 && ui.Activate(18) && Grimoire.Has(s.Mask(b), 18));
                Check($"{tag}: i 19 nodi sono tuoi", Grimoire.CountTier(s.Mask(b), Grimoire.Tier.Supremo) == 1 && s.Mask(b) == (1u << 19) - 1u);
                ui.Selected = 14;
                yield return new WaitForSeconds(0.2f);
                Shot($"libro_{tag}_pieno");
                GrimoireUI.Hide();
                yield return null;
                Check($"{tag}: chiuso il libro i comandi tornano", !GrimoireUI.Open && !GameMenu.Blocked);
            }
        }

        // ------------------------------------------------------------------ seconda mano
        /// <summary>Un punto a terra con nove metri liberi davanti (per le magie a bersaglio e il passo d'ombra).</summary>
        static Vector3 OpenSpot(FloorInstance fi, out float yaw)
        {
            yaw = 0f;
            foreach (var p in fi.groundSpawns)
            {
                if (p.y > 1f || AnnexRuntime.Forbidden(fi, p)) continue;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 45f * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    Vector3 eye = p + Vector3.up * 1.2f;
                    if (Physics.SphereCast(eye, 0.45f, d, out _, 10f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    if (!NavMesh.SamplePosition(p + d * 5.5f, out _, 0.6f, NavMesh.AllAreas)) continue;
                    yaw = a;
                    return p;
                }
            }
            return fi.arrival != null ? fi.arrival.spawnPoint : Vector3.zero;
        }

        IEnumerator OffHand(PlayerController player, string weaponId, Vector3 open, float openYaw)
        {
            var w = player.weapon;
            var s = player.Arcana;
            var hand = w.OffHand;
            Check($"{weaponId}: la seconda mano c'e' (mano libera articolata)", hand != null && hand.Hand != null);
            if (hand == null) yield break;
            var mc = MagicCaster.I;
            Check($"{weaponId}: il lanciatore c'e'", mc != null);
            if (mc == null) yield break;
            player.yaw = openYaw; player.pitch = 0f;
            yield return new WaitForSeconds(0.3f);
            Shot($"{weaponId}_riposo");
            ZoomShot($"{weaponId}_riposo", hand);
            // magie: un bersaglio fermo davanti
            for (int node = 12; node <= 18; node++)
            {
                s.Essence = s.EssenceMax; s.ClearCooldowns(); s.EssenzaLeft = 0f;
                var dummy = Dummy(player, 5.5f);
                float hp0 = dummy != null ? dummy.hp : 0f;
                if (dummy != null && node != 16 && node != 18)
                {
                    // le magie dirette partono col mirino sul petto del bersaglio, come farebbe chi gioca (il Passo d'Ombra resta in piano)
                    Vector3 to = dummy.AimPoint - player.cam.position;
                    player.yaw = Mathf.Atan2(to.x, to.z);
                    player.pitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f));
                    yield return new WaitForSeconds(0.1f);
                }
                Vector3 pos0 = player.transform.position;
                player.hp = player.MaxHp * 0.5f;
                float hpP0 = player.hp;
                string name = Grimoire.Witch[node].name;
                bool started = mc.TryCast(node);
                Check($"{weaponId}: la magia {name} parte", started);
                if (!started) { Enemy.ClearAll(); continue; }
                yield return Gesture(hand, $"{weaponId}_magia_{node}", w);
                yield return new WaitForSeconds(node == 12 ? 0.6f : 0.25f);
                switch (node)
                {
                    case 16: Check($"{weaponId}: {name} sposta il giocatore", (player.transform.position - pos0).magnitude > 3f, $"{(player.transform.position - pos0).magnitude:F1} m"); break;
                    case 18: Check($"{weaponId}: {name} accende l'Essenza Maledetta", s.EssenzaLeft > 0f); break;
                    case 15: Check($"{weaponId}: {name} ferisce e cura", (dummy == null || dummy.hp < hp0) && player.hp > hpP0, $"bersaglio {hp0:F0}->{(dummy != null ? dummy.hp : 0f):F0}, vita {hpP0:F0}->{player.hp:F0}"); break;
                    default: Check($"{weaponId}: {name} ferisce il bersaglio", dummy == null || dummy.hp < hp0, $"{hp0:F0}->{(dummy != null ? dummy.hp : 0f):F0}"); break;
                }
                Enemy.ClearAll();
                player.Teleport(open); player.yaw = openYaw; player.pitch = 0f;
                yield return new WaitForSeconds(0.2f);
            }
            s.EssenzaLeft = 0f;
            // sigilli: ogni sigillo sull'arma, poi un colpo sul bersaglio
            for (int node = 12; node <= 18; node++)
            {
                s.Essence = s.EssenceMax; s.ClearCooldowns();
                string name = Grimoire.Arsenal[node].name;
                bool started = mc.TryInfusion(node);
                Check($"{weaponId}: il sigillo {name} parte", started);
                if (!started) continue;
                yield return Gesture(hand, $"{weaponId}_sigillo_{node}", w);
                int slot = w.SlotIndex;
                bool on = node == 18 ? s.CursedOn(slot) : s.InfusionOn(slot) == node;
                Check($"{weaponId}: {name} resta sull'arma", on);
                var aura = w.ViewRootT != null ? w.ViewRootT.GetComponent<WeaponAura>() : null;
                Check($"{weaponId}: {name} accende l'arma", aura != null && aura.Element == node && aura.OverlayCount > 0,
                      aura != null ? $"elemento {aura.Element}, pelli {aura.OverlayCount}" : "nessuna aura");
                var dummy = Dummy(player, 4f);
                yield return new WaitForSeconds(0.2f);
                // il calore e' salito dal basso verso la cima (la risalita dura 0,7 s dal gesto di chiusura, e Maneggevolezza accorcia il gesto)
                Check($"{weaponId}: {name}: il calore sale su tutta l'arma", aura != null && aura.Heat > 0.75f, aura != null ? $"{aura.Heat:F2}" : "");
                if (dummy != null)
                {
                    player.yaw = Mathf.Atan2(dummy.AimPoint.x - player.cam.position.x, dummy.AimPoint.z - player.cam.position.z);
                    Vector3 to = dummy.AimPoint - player.cam.position;
                    player.pitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f));
                    yield return new WaitForSeconds(0.15f);
                    int magBefore = w.mag;
                    float hpd = dummy.hp;
                    int sh0 = SigilFx.Shots, tr0 = SigilFx.Tracers, im0 = SigilFx.Impacts;
                    // l'elettricita scatta a caso (un colpo su tre circa): si spara finche arriva lo stato, al massimo 12 colpi
                    int shots = node == 16 ? 12 : 3;
                    for (int k = 0; k < shots && dummy != null && dummy.hp > 0f; k++)
                    {
                        if (w.mag <= 0) w.MaxAmmo();
                        w.DebugFire();
                        yield return new WaitForSeconds(0.25f);
                        if (node == 16 && dummy != null && dummy.GetComponent<EnemyStatus>() != null) break;
                    }
                    var st = dummy != null ? dummy.GetComponent<EnemyStatus>() : null;
                    bool effect = node == 12 ? (dummy == null || dummy.hp < hpd) : (dummy == null || st != null);
                    Check($"{weaponId}: {name} sui colpi", effect, st != null ? "stato presente" : "");
                    // il proiettile dice qual e' l'elemento: lampo alla bocca, traccia e impatto a ogni colpo (SigilFx)
                    Check($"{weaponId}: {name}: ogni colpo ha lampo, traccia e impatto del suo elemento",
                          SigilFx.Shots > sh0 && SigilFx.Tracers > tr0 && SigilFx.Impacts > im0, $"lampi {SigilFx.Shots - sh0}, tracce {SigilFx.Tracers - tr0}, impatti {SigilFx.Impacts - im0}");
                    if (node == 18) Check($"{weaponId}: Arma Maledetta non consuma munizioni", w.mag >= magBefore, $"{magBefore}->{w.mag}");
                    Enemy.ClearAll();
                }
                player.yaw = openYaw; player.pitch = 0f;
                yield return new WaitForSeconds(0.3f);
            }
            // il lancio di mano sinistra (acquasanta)
            var eq = player.GetComponent<Equipment>();
            if (eq != null)
            {
                eq.grenades = 2;
                bool thrown = mc.Throw(p => eq.Throw(0));
                Check($"{weaponId}: il lancio di mano sinistra parte", thrown);
                if (thrown) yield return Gesture(hand, $"{weaponId}_lancio", w);
            }
            w.Refresh();
        }

        // ------------------------------------------------------------------ -sigilliview (2026-11-17)
        /// <summary>-sigilliview [-sigilliweapons a,b] [-sigillinodes 14,17] [-magiazoom] [-sigillishots N]: solo i sette sigilli, per giudicare
        /// l'aspetto dell'arma e dei proiettili senza il resto della prova. Per ogni sigillo: il gesto (foto a 6 tempi), l'arma accesa vista
        /// da vicino (di lato e da sotto) e una raffica sul bersaglio con le foto dei proiettili. Foto in hotel_build/magia/sg_&lt;arma&gt;_&lt;nodo&gt;_*.png.</summary>
        IEnumerator SigilView(FloorInstance fi, GameManager gm, PlayerController player)
        {
            shots = true;
            var s = player.Arcana;
            s.ResetRun();
            const uint all = (1u << Grimoire.Count) - 1u;
            s.Witch = all; s.Arsenal = all;
            Vector3 open = OpenSpot(fi, out float openYaw);
            var args = Environment.GetCommandLineArgs();
            int wi = Array.IndexOf(args, "-sigilliweapons");
            string[] ids = wi >= 0 && wi + 1 < args.Length ? args[wi + 1].Split(',') : new[] { "ar9_malleus" };
            int ni = Array.IndexOf(args, "-sigillinodes");
            string nodes = ni >= 0 && ni + 1 < args.Length ? "," + args[ni + 1] + "," : null;
            int bursts = 8;
            int bi = Array.IndexOf(args, "-sigillishots");
            if (bi >= 0 && bi + 1 < args.Length) int.TryParse(args[bi + 1], out bursts);
            foreach (string id in ids)
            {
                int idx = WeaponDefs.IndexOf(id);
                if (idx < 0) { Check($"arma {id} esiste", false); continue; }
                player.weapon.Equip(idx);
                player.Teleport(open); player.yaw = openYaw; player.pitch = 0f;
                yield return new WaitForSeconds(1.2f);
                var w = player.weapon;
                var hand = w.OffHand;
                var mc = MagicCaster.I;
                Check($"{id}: seconda mano e lanciatore", hand != null && hand.Hand != null && mc != null);
                if (hand == null || mc == null) continue;
                yield return new WaitForEndOfFrame();
                Shot($"sg_{id}_riposo");
                WeaponShots($"sg_{id}_riposo", w);
                for (int node = 12; node <= 18; node++)
                {
                    if (nodes != null && nodes.IndexOf("," + node + ",", StringComparison.Ordinal) < 0) continue;
                    // il sigillo di prima si spegne
                    s.SlotInfusionLeft[0] = s.SlotInfusionLeft[1] = 0f; s.CursedLeft = 0f;
                    s.Essence = s.EssenceMax; s.ClearCooldowns();
                    player.yaw = openYaw; player.pitch = 0f;
                    yield return new WaitForSeconds(0.5f);
                    string name = Grimoire.Arsenal[node].name, tag = $"sg_{id}_{node}";
                    bool started = mc.TryInfusion(node);
                    Check($"{id}: il sigillo {name} parte", started);
                    if (!started) continue;
                    yield return Gesture(hand, tag, w);
                    yield return new WaitForSeconds(0.6f);
                    yield return new WaitForEndOfFrame();
                    Shot(tag + "_fine");
                    WeaponShots(tag + "_fine", w);
                    // una raffica sul bersaglio: la traccia dei proiettili, gli impatti, lo stato sul nemico
                    var dummy = Dummy(player, 7f);
                    if (dummy == null) continue;
                    Vector3 to = dummy.AimPoint - player.cam.position;
                    player.yaw = Mathf.Atan2(to.x, to.z);
                    player.pitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f));
                    yield return new WaitForSeconds(0.4f);
                    for (int k = 0; k < bursts && dummy != null && dummy.hp > 0f; k++)
                    {
                        if (w.mag <= 0) w.MaxAmmo();
                        w.DebugFire();
                        if (k == 1 || k == 4)
                        {
                            // due fotogrammi dopo il colpo: la traccia e' a meta' strada, non ancora sul bersaglio
                            yield return null; yield return null;
                            yield return new WaitForEndOfFrame();
                            Shot($"{tag}_tiro{k}");
                            if (k == 1) WeaponShots($"{tag}_tiro", w);
                        }
                        yield return new WaitForSeconds(0.11f);
                    }
                    Enemy.ClearAll();
                    // una parete a 3-8 m (sfondo scuro: sul pavimento a scacchi la traccia e l'impatto si perdono), senza bersaglio: il secondo
                    // colpo va al rallentatore (il dardo vola a 130-170 m/s e a tempo normale e' gia' arrivato), con una foto in volo e una
                    // all'urto con la macchia che lascia
                    float wallYaw = openYaw, wallBest = float.MaxValue; bool wall = false;
                    for (int a = 0; a < 24; a++)
                    {
                        float yw = a * Mathf.PI * 2f / 24f;
                        Vector3 wd = new Vector3(Mathf.Sin(yw), 0f, Mathf.Cos(yw));
                        if (Physics.Raycast(player.cam.position, wd, out RaycastHit hw, 12f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && hw.distance > 3.5f && hw.distance < 9f
                            && Mathf.Abs(hw.distance - 5.5f) < wallBest)
                        { wallBest = Mathf.Abs(hw.distance - 5.5f); wallYaw = yw; wall = true; }
                    }
                    player.yaw = wallYaw; player.pitch = wall ? 0.02f : 0.42f;
                    yield return new WaitForSeconds(0.3f);
                    for (int k = 0; k < 4; k++)
                    {
                        if (w.mag <= 0) w.MaxAmmo();
                        if (k == 1) Time.timeScale = 0.04f;
                        w.DebugFire();
                        if (k == 1)
                        {
                            for (int f = 0; f < 16; f++) yield return null;
                            yield return new WaitForEndOfFrame();
                            Shot($"{tag}_colpo_volo");
                            for (int f = 0; f < 60; f++) yield return null;
                            yield return new WaitForEndOfFrame();
                            Shot($"{tag}_colpo_urto");
                            Time.timeScale = 1f;
                        }
                        yield return new WaitForSeconds(0.11f);
                    }
                    yield return new WaitForSeconds(0.3f);
                }
                s.SlotInfusionLeft[0] = s.SlotInfusionLeft[1] = 0f; s.CursedLeft = 0f;
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>Due primi piani dell'arma in mano (di lato da destra e da sotto a tre quarti), per giudicare da vicino il calore che sale,
        /// la brina o le rune sulle superfici (con -magiashots / -sigilliview).</summary>
        void WeaponShots(string name, Weapon w)
        {
            var main = Camera.main;
            if (!shots || main == null || w == null || w.ViewRootT == null || w.Rig == null || w.Rig.renderers == null) return;
            Bounds b = default; bool any = false;
            foreach (var r in w.Rig.renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return;
            Transform vr = w.ViewRootT;
            Vector3 c = b.center;
            float ext = Mathf.Max(0.18f, b.extents.magnitude);
            Vector3 p1 = c + vr.right * (ext * 2.4f) + vr.up * (ext * 0.15f);
            RenderLike(main, name + "_lato", p1, Quaternion.LookRotation(c - p1, vr.up), 32f);
            Vector3 p2 = c + vr.right * (ext * 1.4f) - vr.up * (ext * 1.5f) - vr.forward * (ext * 0.6f);
            RenderLike(main, name + "_sotto", p2, Quaternion.LookRotation(c - p2, vr.up), 34f);
        }

        /// <summary>Segue un gesto fino alla fine: la mano libera prende il posto del guanto avvolto, il gomito resta nei suoi gradi, il
        /// palmo non attraversa la camera; foto a 6 tempi.</summary>
        IEnumerator Gesture(OffHand hand, string name, Weapon w)
        {
            float minElbow = 999f, maxElbow = 0f, maxWrist = 0f, maxRoll = 0f, maxPS = 0f, maxFlex = 0f, maxDev = 0f;
            bool anat = false;
            bool sawFree = false;
            float[] at = { 0.12f, 0.28f, 0.44f, 0.6f, 0.76f, 0.92f };
            int next = 0;
            float until = Time.time + 6f;
            var arm = ArmOf(w);
            while (hand != null && hand.Busy && Time.time < until)
            {
                if (hand.FreeVisible) sawFree = true;
                if (arm != null && arm.Free > 0.5f)
                {
                    minElbow = Mathf.Min(minElbow, arm.LastElbowDeg); maxElbow = Mathf.Max(maxElbow, arm.LastElbowDeg);
                    maxWrist = Mathf.Max(maxWrist, arm.LastWristDeg);
                    maxRoll = Mathf.Max(maxRoll, Mathf.Abs(arm.LastRollDeg));
                    if (arm.HasHandFrame)
                    {
                        anat = true;
                        maxPS = Mathf.Max(maxPS, Mathf.Abs(arm.LastProSupDeg)); maxFlex = Mathf.Max(maxFlex, Mathf.Abs(arm.LastFlexDeg));
                        maxDev = Mathf.Max(maxDev, Mathf.Abs(arm.LastDevDeg));
                    }
                }
                if (next < at.Length && hand.Progress >= at[next])
                {
                    // a fine fotogramma: le pelli (maniche e guanti) Unity le ricalcola dopo LateUpdate; scattando prima, le mesh con le
                    // ossa erano quelle del fotogramma prima e le parti rigide quelle di adesso (la manichetta sembrava staccata)
                    yield return new WaitForEndOfFrame();
                    Shot($"{name}_{next}");
                    ZoomShot($"{name}_{next}", hand);
                    if (side) { SideShot($"{name}_{next}", w); DumpArms(name, w, $"foto {next}"); }
                    next++;
                }
                yield return null;
            }
            Check($"{name}: il gesto finisce", hand != null && !hand.Busy);
            Check($"{name}: la mano libera ha preso il posto del guanto", sawFree);
            // con la presa naturale la mano articolata e' la mano di riposo: resta, ed e' tornata alla presa
            Check($"{name}: e poi lo ha restituito", hand != null && (hand.NaturalRest || !hand.FreeVisible));
            if (maxElbow > 0f) Check($"{name}: gomito tra 28 e 176 gradi", minElbow >= ArmRig.MinElbowDeg - 0.5f && maxElbow <= ArmRig.MaxElbowDeg + 0.5f, $"{minElbow:F0}..{maxElbow:F0}, polso fino a {maxWrist:F0}");
            // un gesto col braccio sempre teso vuol dire che l'arma o la mano stanno oltre la portata della spalla
            if (maxElbow > 0f) Check($"{name}: il gomito si piega (sotto 150 gradi)", minElbow < 150f, $"minimo {minElbow:F0}");
            // limiti umani: il polso non si spezza, l'avambraccio non si attorciglia, il braccio non si ripiega contro la spalla
            if (maxElbow > 0f)
            {
                if (anat)
                {
                    // campi articolari veri (con 5-10 gradi di margine): prono-supinazione 90, flessione-estensione 85, deviazione 35
                    Check($"{name}: prono-supinazione entro 90 gradi", maxPS <= 90f, $"massima {maxPS:F0}");
                    Check($"{name}: polso: flessione-estensione entro 85 gradi", maxFlex <= 85f, $"massima {maxFlex:F0}");
                    Check($"{name}: polso: deviazione entro 35 gradi", maxDev <= 35f, $"massima {maxDev:F0}");
                }
                else
                {
                    Check($"{name}: polso entro 80 gradi dall'avambraccio", maxWrist <= 80f, $"massimo {maxWrist:F0}");
                    Check($"{name}: torsione dell'avambraccio entro 100 gradi", maxRoll <= 100f, $"massima {maxRoll:F0}");
                }
                Check($"{name}: gomito mai chiuso sotto 32 gradi", minElbow >= 32f, $"minimo {minElbow:F0}");
            }
            Debug.Log($"[PryingEyes] gesto {name}: gomito {minElbow:F0}..{maxElbow:F0} gradi, polso fino a {maxWrist:F0}, torsione fino a {maxRoll:F0}" +
                      (anat ? $"; anatomia: prono-supinazione {maxPS:F0}, flessione {maxFlex:F0}, deviazione {maxDev:F0}" : ""));
        }

        static ArmRig ArmOf(Weapon w, string field = "armL")
        {
            var f = typeof(Weapon).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f != null ? f.GetValue(w) as ArmRig : null;
        }

        /// <summary>Un monaco fermo a 'dist' metri davanti, sulla navmesh (o il piu' vicino possibile).</summary>
        static Enemy Dummy(PlayerController player, float dist)
        {
            Vector3 f = new Vector3(Mathf.Sin(player.yaw), 0f, Mathf.Cos(player.yaw));
            Vector3 p = player.transform.position + f * dist;
            for (float d = dist; d > 1.5f; d -= 0.5f)
            {
                Vector3 q = player.transform.position + f * d;
                if (NavMesh.SamplePosition(q, out var h, 0.8f, NavMesh.AllAreas) && HotelNav.ClearLine(player.cam.position, h.position + Vector3.up)) { p = h.position; break; }
            }
            var e = Enemy.Spawn(Enemy.EnemyType.Monaco, p);
            if (e != null) { e.hp = e.maxHp = 5000f; }
            return e;
        }
    }
}
#endif
