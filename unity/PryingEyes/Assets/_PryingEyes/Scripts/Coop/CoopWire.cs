using System;
using System.Text;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Tipi di messaggio della cooperativa. Un solo pacchetto di rete (CoopPacket: tipo + byte) porta tutti: cosi' il formato
    /// e' tutto qui, sotto il nostro controllo, e la generazione di codice di FishNet vede una sola struttura.
    /// Cambiando il formato di un messaggio va alzata CoopProtocol.Revision: chi ha una versione diversa viene rifiutato all'ingresso.</summary>
    public enum CoopKind : byte
    {
        // ---- giocatore -> ospite
        PlayerState = 1,      // non affidabile, ~15 Hz: posa e dati furtivi
        Hello,                // appena ammesso: il mio nome
        FloorReady,           // ho costruito il piano (epoca)
        UseCandle,            // indice della candela
        UseExit,              // sono entrato nell'ascensore aperto
        HitClaim,             // ho colpito un nemico (id, danno, testa, fonte)
        NoiseEvt,             // ho fatto rumore (posizione, raggio, tipo)
        DropClaim,            // ho preso una goccia
        PlayerEvent,          // morto / rinato
        // ---- ospite -> giocatore
        Welcome = 64,         // slot, seme, piano, epoca
        PlayerJoined,
        PlayerLeft,
        PlayersState,         // non affidabile: le pose degli altri
        FloorChange,          // nuovo piano: seme, numero, epoca
        EnemySpawn,
        EnemyGone,
        EnemyState,           // non affidabile, ~10 Hz
        EnemyEvent,           // attacco, lancio, grido
        AwardPoints,
        HurtPlayer,
        SedatePlayer,
        CandleLit,
        ExitState,
        RoundState,
        DropSpawn,
        DropTaken,
        Announce,             // riga di testo per tutti (toast / banner)
        Bolt,                 // dardo magico (solo grafica sui giocatori)
        KillCredit,           // un'uccisione a tuo nome: fonte e testa; punti, conteggio e cura li calcola chi la riceve (GameManager.CreditKill)
        Teleport,             // l'ospite ti sposta (una serranda che cala sulla soglia): epoca e posizione
        // ---- in tutte e due le direzioni
        Test = 120,           // solo per la prova a due processi (CoopTest, con -cooptest): senza quel flag chi lo riceve lo butta
    }

    public static class CoopProtocol
    {
        /// <summary>Versione del protocollo + contenuto: da alzare a ogni cambio di formato dei messaggi o delle regole del piano.</summary>
        public const string Revision = "pe-coop-14";
        public const ushort DefaultPort = 7807;
        public const int MaxPlayers = 4;
        /// <summary>Un pacchetto non affidabile resta sotto l'MTU di Tugboat (1023 byte): lasciamo margine per l'intestazione di FishNet.</summary>
        public const int MaxUnreliable = 900;
        /// <summary>Quanti secondi senza pacchetti prima di considerare perso l'altro (LiteNetLib manda un ping al secondo; la costruzione di un
        /// piano ferma il gioco per qualche decimo di secondo, non per quindici).</summary>
        public const float TimeoutSeconds = 15f;

        public static byte QuantYaw(float yawRad)
        {
            float t = Mathf.Repeat(yawRad / (Mathf.PI * 2f), 1f);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(t * 255f), 0, 255);
        }
        public static float DequantYaw(byte b) => b / 255f * Mathf.PI * 2f;
        public static byte QuantPitch(float pitchRad)
        {
            float t = Mathf.Clamp(pitchRad / (Mathf.PI * 0.5f), -1f, 1f) * 0.5f + 0.5f;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(t * 255f), 0, 255);
        }
        public static float DequantPitch(byte b) => (b / 255f - 0.5f) * 2f * Mathf.PI * 0.5f;
    }

    /// <summary>Scrive un messaggio in un buffer che cresce da solo.</summary>
    public sealed class NetWriter
    {
        byte[] buf = new byte[128];
        int n;
        public int Length => n;
        public NetWriter Reset() { n = 0; return this; }
        void Ensure(int k) { if (n + k > buf.Length) Array.Resize(ref buf, Mathf.Max(buf.Length * 2, n + k)); }
        public NetWriter U8(int v) { Ensure(1); buf[n++] = (byte)v; return this; }
        public NetWriter Bool(bool v) => U8(v ? 1 : 0);
        public NetWriter U16(int v) { Ensure(2); buf[n++] = (byte)v; buf[n++] = (byte)(v >> 8); return this; }
        public NetWriter I32(int v) { Ensure(4); buf[n++] = (byte)v; buf[n++] = (byte)(v >> 8); buf[n++] = (byte)(v >> 16); buf[n++] = (byte)(v >> 24); return this; }
        public NetWriter F32(float v) => I32(BitConverter.SingleToInt32Bits(v));
        public NetWriter V3(Vector3 v) => F32(v.x).F32(v.y).F32(v.z);
        public NetWriter Str(string s)
        {
            if (s == null) s = "";
            var b = Encoding.UTF8.GetBytes(s.Length > 40 ? s.Substring(0, 40) : s);
            U8(b.Length); Ensure(b.Length); Buffer.BlockCopy(b, 0, buf, n, b.Length); n += b.Length;
            return this;
        }
        public byte[] ToArray() { var r = new byte[n]; Buffer.BlockCopy(buf, 0, r, 0, n); return r; }
    }

    /// <summary>Legge un messaggio senza mai lanciare eccezioni: i dati arrivano da un'altra macchina, quindi un messaggio corto o
    /// storto non deve far cadere il gioco. Dopo una lettura oltre la fine Ok resta falso e tutto vale zero.</summary>
    public struct NetReader
    {
        readonly byte[] b;
        int p;
        public bool Ok { get; private set; }
        public int Left => b == null ? 0 : b.Length - p;
        public NetReader(byte[] data) { b = data; p = 0; Ok = data != null; }
        bool Need(int k)
        {
            if (b == null || p + k > b.Length) { Ok = false; return false; }
            return true;
        }
        public int U8() => Need(1) ? b[p++] : 0;
        public bool Bool() => U8() != 0;
        public int U16() { if (!Need(2)) return 0; int v = b[p] | (b[p + 1] << 8); p += 2; return v; }
        public int I32() { if (!Need(4)) return 0; int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24); p += 4; return v; }
        public float F32()
        {
            float v = BitConverter.Int32BitsToSingle(I32());
            return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        }
        public Vector3 V3() => new Vector3(F32(), F32(), F32());
        public string Str()
        {
            int len = U8();
            if (!Need(len)) return "";
            string s = Encoding.UTF8.GetString(b, p, len);
            p += len;
            return s;
        }
    }
}
