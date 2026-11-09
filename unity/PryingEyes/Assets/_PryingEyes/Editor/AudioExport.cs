using System.IO;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    /// <summary>Salva i suoni procedurali come WAV (16 bit) in hotel_build/audio: per ascoltarli senza aprire il gioco
    /// e per controllarli (livello di picco, RMS, durata, campioni non validi).</summary>
    public static class AudioExport
    {
        const string Dir = "C:/Users/danie/Desktop/prying-eyes/hotel_build/audio";

        public static void Run()
        {
            Directory.CreateDirectory(Dir);
            int r = SoundGen.Rate;
            Save("sparo", SoundGen.Gunshot(100), r);
            Save("colpito", SoundGen.HitTick(), r);
            Save("ucciso", SoundGen.KillThunk(), r);
            Save("ferito", SoundGen.Hurt(), r);
            Save("ricarica", SoundGen.Reload(), r);
            Save("passo_marmo", SoundGen.Step(0, 0), r);
            Save("passo_moquette", SoundGen.Step(1, 0), r);
            Save("passo_legno", SoundGen.Step(2, 0), r);
            Save("campanello_reception", SoundGen.ReceptionBell(), r);
            Save("ascensore_ding", SoundGen.ElevatorDing(), r);
            Save("ascensore_porte", SoundGen.ElevatorDoors(), r);
            Save("scricchiolio", SoundGen.Creak(200), r);
            Save("vento_loop", SoundGen.Wind(7), r);
            Save("ronzio_stanza_loop", SoundGen.RoomTone(), r);
            Save("sussurro", SoundGen.Whisper(300), r);
            Save("bastone_passo", SoundGen.StaffTap(400), r);
            Save("bastone_a_terra", SoundGen.StaffStrike(), r);
            Save("monaco_cade", SoundGen.MonkDeath(), r);
            Save("drone_loop", SoundGen.DroneBuzz(), r);
            Save("bruto_passo", SoundGen.BruteStep(500), r);
            var chant = SoundGen.MonkChant(600, out int cr);
            Save("litania_monaco_loop", chant, cr);
            var waltz = SoundGen.Waltz(out int wr);
            Save("valzer_grammofono_loop", waltz, wr);
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
            Debug.Log($"[PryingEyes] audio {name}: {s.Length / (float)rate:F2} s, picco {peak:F2}, rms {rms:F3}, non validi {bad}");
        }
    }
}
