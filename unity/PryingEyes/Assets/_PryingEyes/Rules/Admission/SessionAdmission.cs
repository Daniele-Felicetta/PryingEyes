using System;
using System.Text;

namespace PryingEyes.Multiplayer
{
    public static class SessionAdmission
    {
        const byte Protocol = 1;

        public static byte[] Create(int seats, string contentRevision)
        {
            if (seats < 1 || seats > SessionRoster.MaxLocalPlayers) throw new ArgumentOutOfRangeException(nameof(seats));
            if (!ValidRevision(contentRevision)) throw new ArgumentException("Use 1..64 ASCII letters, digits, '.', '_' or '-'.", nameof(contentRevision));
            var bytes = new byte[2 + contentRevision.Length];
            bytes[0] = Protocol; bytes[1] = (byte)seats;
            Encoding.ASCII.GetBytes(contentRevision, 0, contentRevision.Length, bytes, 2);
            return bytes;
        }

        public static bool TryRead(byte[] token, string expectedRevision, out int seats)
        {
            seats = 0;
            if (!ValidRevision(expectedRevision) || token == null || token.Length != expectedRevision.Length + 2 ||
                token[0] != Protocol || token[1] < 1 || token[1] > SessionRoster.MaxLocalPlayers) return false;
            for (int i = 0; i < expectedRevision.Length; i++)
                if (token[i + 2] != expectedRevision[i]) return false;
            seats = token[1];
            return true;
        }

        static bool ValidRevision(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') &&
                    c != '.' && c != '_' && c != '-') return false;
            return true;
        }
    }
}
