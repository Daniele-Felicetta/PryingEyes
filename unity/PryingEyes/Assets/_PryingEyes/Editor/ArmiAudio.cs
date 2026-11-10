using System.IO;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    /// <summary>Salva in WAV (hotel_build/audio/armi) gli spari per classe, i rumori meccanici e i bossoli: per controllarli con
    /// numpy (picco, durata, centro spettrale) e per ascoltarli senza aprire il gioco.</summary>
    public static class ArmiAudio
    {
        const string Dir = "C:/Users/danie/Desktop/prying-eyes/hotel_build/audio/armi";

        public static void Run()
        {
            Directory.CreateDirectory(Dir);
            int r = SoundGen.Rate;
            foreach (var k in SoundBank.GunKinds) Save("sparo_" + k, SoundBank.GunSamples(k, 200), r);
            foreach (var k in SoundBank.MechKinds) Save("mecc_" + k, SoundGen.Mech(k, 300), r);
            foreach (var k in new[] { "pistol", "rifle", "big", "shell" }) Save("bossolo_" + k, SoundGen.CasingDrop(k, 400), r);
        }

        static void Save(string name, float[] s, int rate)
        {
            float peak = 0f, sum = 0f; int bad = 0;
            foreach (var x in s)
            {
                if (float.IsNaN(x) || float.IsInfinity(x)) { bad++; continue; }
                peak = Mathf.Max(peak, Mathf.Abs(x));
                sum += x * x;
            }
            float rms = Mathf.Sqrt(sum / Mathf.Max(1, s.Length));
            using (var f = new FileStream(Path.Combine(Dir, name + ".wav"), FileMode.Create))
            using (var w = new BinaryWriter(f))
            {
                int bytes = s.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var x in s)
                {
                    float v = float.IsNaN(x) ? 0f : Mathf.Clamp(x, -1f, 1f);
                    w.Write((short)Mathf.RoundToInt(v * 32767f));
                }
            }
            Debug.Log($"[ArmiAudio] {name}: {s.Length / (float)rate:F2} s, picco {peak:F2}, rms {rms:F3}, non validi {bad}");
        }
    }
}
