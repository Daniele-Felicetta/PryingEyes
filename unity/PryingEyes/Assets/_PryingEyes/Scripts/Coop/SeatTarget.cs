using UnityEngine;

namespace PryingEyes
{
    /// <summary>Un giocatore dello split screen come lo vede un nemico: come un compagno in cooperativa (ITarget), ma il corpo e' in questo stesso
    /// gioco. Quello che richiede i dati del giocatore (visibilita', ferite, sedativo) entra nel suo posto (Seats.Enter).</summary>
    public sealed class SeatTarget : ITarget
    {
        readonly Seat seat;
        public SeatTarget(Seat seat) { this.seat = seat; }

        PlayerController P => seat.player;
        public int Slot => seat.index;
        public Vector3 Feet => P != null ? P.transform.position : Vector3.zero;
        public bool Alive => P != null && P.alive && !P.Downed;
        /// <summary>Vivo, anche se a terra.</summary>
        public bool Living => P != null && P.alive;
        public float BodyHeight => P != null ? P.BodyHeight : 1.75f;
        public float Lean => P != null ? P.lean : 0f;
        public Vector3 LeanedPoint(float h) => P != null ? P.LeanedPoint(h) : Vector3.up * h;
        public Transform LookAt => P != null ? P.cam : null;
        public Transform BeamT { get { var t = seat.TorchNow; return t != null && t.Lit && t.Beam != null ? t.Beam.transform : null; } }
        public float SightRange(Enemy e) { using (Seats.Enter(seat)) return Stealth.SightRange(e); }
        public void Hurt(float dmg, Vector3? origin = null) { using (Seats.Enter(seat)) { if (P != null) P.Hurt(dmg, origin); } }
        public void Sedate(float seconds) { using (Seats.Enter(seat)) { if (P != null) P.Sedate(seconds); } }
    }
}
