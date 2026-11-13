using System;
using System.Collections;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -torchreview: stessa esposizione, modelli e misure dei pixel illuminati.</summary>
    public sealed class TorchReviewTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        string output;
        Camera camera;
        Material maskMaterial;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-torchreview") >= 0) new GameObject("TorchReviewTest").AddComponent<TorchReviewTest>();
        }
        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] torchreview " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }
        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            output = Path.GetFullPath("unity_logs/torch-review");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-torchout") output = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(output);
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] torchreview RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }
        Color32[] Capture(string file)
        {
            GameManager.CamShot(Path.Combine(output, file + ".png"), camera, false, 640, 360);
            var image = new Texture2D(2, 2); image.LoadImage(File.ReadAllBytes(Path.Combine(output, file + ".png")));
            var pixels = image.GetPixels32(); Destroy(image); return pixels;
        }
        Color32[] Mask(GameObject subject, string label)
        {
            var renderers = subject.GetComponentsInChildren<Renderer>();
            var materials = Array.ConvertAll(renderers, r => r.sharedMaterials);
            try
            {
                foreach (var renderer in renderers)
                {
                    var masks = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < masks.Length; i++) masks[i] = maskMaterial;
                    renderer.sharedMaterials = masks;
                }
                return Capture(label + "-maschera");
            }
            finally { for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = materials[i]; }
        }
        void Measure(Color32[] pixels, Color32[] mask, string label, Light source = null)
        {
            int count = 0, clipped = 0; float luminance = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (mask[i].r < 153) continue;
                var p = pixels[i]; count++;
                luminance += (p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f) / 255f;
                // Confronto con il bianco dello stesso render: la colorazione cinematografica
                // porta il bianco visibile sotto 255, quindi una soglia RGB assoluta perdeva il difetto.
                if (p.r >= mask[i].r * 0.98f && p.g >= mask[i].g * 0.98f && p.b >= mask[i].b * 0.98f) clipped++;
            }
            float white = count > 0 ? (float)clipped / count : 1f;
            float mean = count > 0 ? luminance / count : 0;
            Debug.Log($"[PryingEyes] torchreview misura {label}: pixel={count} bruciati={white:P2} luminanza={mean:F3} intensita={(source != null ? source.intensity : Torch.I.Beam.intensity):F3}");
            Check(label + " soggetto renderizzato", count > 100);
            Check(label + " dettagli conservati (bianchi saturi sotto 4%)", white < 0.04f);
            Check(label + " soggetto ancora leggibile", mean > 0.04f);
        }
        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            float brightness = GameSettings.Brightness;
            float savedBrightness = PlayerPrefs.GetFloat("video_brightness", 0.5f);
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = true;
            var player = GameManager.I.player; player.enabled = false; player.invulnerable = true;
            Vector3 stage = new Vector3(0, 120, 0); player.Teleport(stage);
            var floor = new GameObject("Pavimento fixture", typeof(BoxCollider));
            floor.transform.position = stage - Vector3.up * 0.05f; floor.transform.localScale = new Vector3(40, 0.1f, 40);
            camera = player.cam.GetComponent<Camera>();
            camera.transform.SetPositionAndRotation(stage + Vector3.up * 1.62f, Quaternion.identity);
            camera.cullingMask = (1 << 0) | (1 << HotelNav.EnemyLayer); camera.farClipPlane = 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.fieldOfView = 65;
            maskMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")); maskMaterial.SetColor("_BaseColor", Color.white);
            Torch.I.charge = 1; Torch.I.drainMul = 0; Torch.I.on = true;
            LightPool.Tick(false, camera.transform.position);
            foreach (string model in new[] { "Monaco", "Infermiere" })
            {
                var subject = Instantiate(Resources.Load<GameObject>(model));
                foreach (var transform in subject.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = HotelNav.EnemyLayer;
                var animator = subject.GetComponentInChildren<Animator>(); animator.enabled = false;
                var idle = Array.Find(animator.runtimeAnimatorController.animationClips, c => c.name == "fermo"); idle.SampleAnimation(animator.gameObject, 0.4f);
                float farPower = 0;
                foreach (var setup in new[] { new Vector2(8,0), new Vector2(1.8f,0), new Vector2(0.9f,0), new Vector2(1.8f,22) })
                {
                    string label = model + "-" + setup.x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "m-" + setup.y;
                    subject.transform.SetPositionAndRotation(stage + Quaternion.Euler(0,setup.y,0) * Vector3.forward * setup.x, Quaternion.Euler(0,180,0));
                    Physics.SyncTransforms(); Torch.I.SendMessage("LateUpdate");
                    if (setup.x <= 1.8f && farPower > 0) Check(label + " riduce subito prima del rendering", Torch.I.Beam.intensity < farPower * 0.5f);
                    var mask = Mask(subject, label);
                    yield return new WaitForSeconds(0.8f);
                    Torch.I.SendMessage("LateUpdate");
                    var pixels = Capture(label); Measure(pixels, mask, label);
                    if (setup.x == 8)
                    {
                        farPower = Torch.I.Beam.intensity;
                        Check(model + " pavimento radente non oscura il bersaglio lontano", farPower > Torch.BeamIntensity * 0.8f);
                    }
                    if (setup.x == 1.8f && setup.y == 0) Check(model + " adatta la luce al nemico vicino", Torch.I.Beam.intensity < farPower * 0.5f);
                }
                Destroy(subject); yield return null;
            }
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = stage + new Vector3(0,1.2f,1.8f); wall.transform.localScale = new Vector3(1.4f,2.4f,0.1f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = Color.gray;
            wall.GetComponent<Renderer>().sharedMaterial = material;
            Physics.SyncTransforms(); var wallMask = Mask(wall, "parete-1.8m");
            yield return new WaitForSeconds(0.25f); Torch.I.SendMessage("LateUpdate");
            Measure(Capture("parete-1.8m"), wallMask, "parete-1.8m");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            float lastScale = 0;
            for (int i = 0; i < 200; i++) lastScale = Torch.ProximityScale(Torch.I.Beam.transform);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            clock.Stop();
            Debug.Log($"[PryingEyes] torchreview costo misura {clock.Elapsed.TotalMilliseconds * 1000 / 200:F2} us, {bytes} byte gestiti, scala={lastScale:F4}");
            Check("misurazione senza allocazioni gestite", bytes == 0);

            Check("sessione locale per torcia del compagno", Coop.StartHost(port: 27831, localOnly: true));
            Coop.OnFloorStart(FloorDirector.I.floor); Coop.OnFloorBuilt();
            var peer = RemotePlayer.Create(1, "Torcia di prova"); Coop.Game.Players[1] = peer;
            Torch.I.on = false;
            Vector3 peerPosition = stage + Vector3.left + Vector3.back;
            Vector3 delta = wall.transform.position - peerPosition - Vector3.up * 1.62f;
            peer.Apply(new PlayerNet { epoch = Coop.Game.EpochByte, pos = peerPosition, alive = true, torch = true, hp = 1,
                yaw = Mathf.Atan2(delta.x, delta.z), pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) });
            yield return new WaitForSeconds(0.8f); LightPool.Tick(false, camera.transform.position);
            var remoteBeam = Array.Find(peer.GetComponentsInChildren<Light>(), l => l.name == "Fascio");
            Check("torcia del compagno adatta la potenza vicino alla parete", remoteBeam != null && remoteBeam.enabled && remoteBeam.intensity < Torch.BeamIntensity * 0.5f);
            Measure(Capture("parete-compagno-vicino"), wallMask, "parete-compagno-vicino", remoteBeam);
            Torch.I.on = true;
            yield return new WaitForSeconds(0.3f); LightPool.Tick(false, camera.transform.position);
            Check("due torce accese sullo stesso soggetto", Torch.I.Beam.enabled && remoteBeam.enabled);
            Measure(Capture("parete-due-torce"), wallMask, "parete-due-torce", remoteBeam);
            Torch.I.on = false;
            peer.Apply(new PlayerNet { epoch = Coop.Game.EpochByte, pos = stage + Vector3.back * 6.5f, alive = true, torch = true, hp = 1 });
            yield return new WaitForSeconds(0.8f); LightPool.Tick(false, camera.transform.position);
            Check("torcia del compagno recupera la potenza a distanza", remoteBeam.intensity > Torch.BeamIntensity * 0.8f);
            Measure(Capture("parete-compagno-lontano"), wallMask, "parete-compagno-lontano", remoteBeam);
            Coop.Stop();
            Check("preferenza di luminosita conservata", GameSettings.Brightness == brightness && PlayerPrefs.GetFloat("video_brightness", 0.5f) == savedBrightness);
            Debug.Log($"[PryingEyes] torchreview luminosita {brightness:F3}");
        }
    }
}
