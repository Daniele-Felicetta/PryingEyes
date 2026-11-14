#if !PE_DISTRIBUTION_BUILD
using System;
using System.IO;
using System.Text;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace PryingEyes.Proc
{
    /// <summary>Cattura dei fotogrammi del trailer. Gira alla fine di LateUpdate (ordine di esecuzione massimo): a quel punto il
    /// giocatore, i nemici e l'arma in mano (Weapon.LateUpdate) hanno gia' preso la posa del fotogramma. Disegna la camera del giocatore
    /// su una RenderTexture (in batchmode la camera non disegna da sola), legge i pixel e li scrive su ffmpeg, che li comprime in un
    /// MP4 senza passare dal disco; oppure salva una foto JPEG (ricognizione e anteprime). Una richiesta per fotogramma:
    /// il regista (FloorDirector.Trailer.cs) la fa dopo gli Update e prima dei LateUpdate dello stesso fotogramma.
    /// Con la posa libera la camera viene spostata solo per il disegno e poi rimessa dov'era, cosi' la logica del giocatore non se ne accorge.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class TrailerCapture : MonoBehaviour
    {
        public static TrailerCapture I;
        /// <summary>Toglie dal disegno lo strato dell'arma in mano (lo strato 2 'Ignore Raycast': Weapon.BuildViewmodel).</summary>
        public bool hideView = true;
        public int Frames { get; private set; }
        public bool EncoderFailed => encDead;

        Camera cam;
        Transform camT;
        RenderTexture rt, prevTarget;
        Texture2D tex;
        byte[] buf;
        int W, H;
        bool pending, ovr;
        Vector3 rPos;
        Quaternion rRot;
        float rFov;
        string rStill;
        Process enc;
        Stream encIn;
        bool encDead;
        readonly StringBuilder encErr = new StringBuilder();

        public static TrailerCapture Create(Camera camera, int w, int h, int msaa)
        {
            var go = new GameObject("TrailerCapture");
            var c = go.AddComponent<TrailerCapture>();
            c.cam = camera;
            c.camT = camera.transform;
            c.W = w; c.H = h;
            c.prevTarget = camera.targetTexture;
            c.rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = Mathf.Clamp(msaa, 1, 8) };
            c.rt.Create();
            c.tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            c.buf = new byte[w * h * 3];
            camera.targetTexture = c.rt;
            I = c;
            return c;
        }

        /// <summary>Chiede di disegnare al prossimo LateUpdate. Con overridePose la camera si mette in pos/rot/fov solo per il disegno;
        /// senza, si usa la posa che il giocatore ha calcolato (vista con l'arma in mano). stillPath: foto JPEG invece di un fotogramma del filmato.</summary>
        public void Request(bool overridePose, Vector3 pos, Quaternion rot, float fov, string stillPath = null)
        {
            ovr = overridePose; rPos = pos; rRot = rot; rFov = fov; rStill = stillPath;
            pending = true;
        }

        void LateUpdate()
        {
            if (!pending || cam == null) return;
            pending = false;
            int mask = cam.cullingMask;
            Vector3 p0 = camT.position; Quaternion r0 = camT.rotation; float f0 = cam.fieldOfView;
            if (hideView) cam.cullingMask = mask & ~(1 << 2);
            if (ovr) { camT.SetPositionAndRotation(rPos, rRot); cam.fieldOfView = rFov; }
            cam.Render();
            cam.cullingMask = mask;
            if (ovr) { camT.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; }
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            RenderTexture.active = old;
            if (rStill != null)
            {
                File.WriteAllBytes(rStill, tex.EncodeToJPG(90));
                rStill = null;
            }
            else WriteFrame();
        }

        void WriteFrame()
        {
            if (encIn == null || encDead) return;
            var raw = tex.GetRawTextureData<byte>();
            raw.CopyTo(buf);
            try { encIn.Write(buf, 0, buf.Length); Frames++; }
            catch (Exception ex)
            {
                encDead = true;
                Debug.LogError("[PryingEyes] trailer: ffmpeg non risponde: " + ex.Message + " " + EncoderLog());
            }
        }

        string EncoderLog() { lock (encErr) return encErr.ToString(); }

        /// <summary>Apre ffmpeg: fotogrammi grezzi RGB dalla stdin, capovolti (ReadPixels parte dal basso), H.264 con la matrice BT.709
        /// dichiarata (senza, i lettori ricolorano). Falso se ffmpeg non parte.</summary>
        public bool BeginVideo(string path, int fps, int crf)
        {
            EndVideo();
            Frames = 0; encDead = false;
            lock (encErr) encErr.Length = 0;
            string args = $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -video_size {W}x{H} -framerate {fps} -i - " +
                          "-vf \"vflip,scale=out_color_matrix=bt709:out_range=tv,format=yuv420p\" " +
                          $"-c:v libx264 -preset medium -crf {crf} -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv " +
                          $"-movflags +faststart \"{path}\"";
            var psi = new ProcessStartInfo("ffmpeg", args)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            try { enc = Process.Start(psi); }
            catch (Exception ex) { Debug.LogError("[PryingEyes] trailer: ffmpeg non parte: " + ex.Message); enc = null; encDead = true; return false; }
            enc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (encErr) encErr.AppendLine(e.Data); };
            enc.BeginErrorReadLine();
            encIn = enc.StandardInput.BaseStream;
            return true;
        }

        public void EndVideo()
        {
            if (enc == null) return;
            try { encIn?.Flush(); encIn?.Close(); } catch (Exception) { }
            try { if (!enc.WaitForExit(180000)) enc.Kill(); } catch (Exception) { }
            string err = EncoderLog();
            if (err.Length > 0) Debug.LogWarning("[PryingEyes] trailer: ffmpeg: " + err);
            try { enc.Dispose(); } catch (Exception) { }
            enc = null; encIn = null;
        }

        /// <summary>Rimette la camera com'era e libera la RenderTexture.</summary>
        public void Dispose()
        {
            EndVideo();
            if (cam != null) cam.targetTexture = prevTarget;
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (tex != null) Destroy(tex);
            if (I == this) I = null;
            Destroy(gameObject);
        }
    }
}
#endif
