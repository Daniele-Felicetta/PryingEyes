using UnityEngine;

namespace PryingEyes
{
    /// <summary>Tastiera e mouse come li vede il gioco: li riceve solo il posto che li ha (il giocatore 1 nello split screen); gli altri giocatori
    /// usano il loro controller (PadControls, che e' per posto). Fuori dallo split screen e' l'Input di Unity, tale e quale.</summary>
    public static class SeatInput
    {
        public static bool GetKey(KeyCode k) => Seats.Keyboard && Input.GetKey(k);
        public static bool GetKeyDown(KeyCode k) => Seats.Keyboard && Input.GetKeyDown(k);
        public static bool GetMouseButton(int b) => Seats.Keyboard && Input.GetMouseButton(b);
        public static bool GetMouseButtonDown(int b) => Seats.Keyboard && Input.GetMouseButtonDown(b);
        public static float GetAxis(string axis) => Seats.Keyboard ? Input.GetAxis(axis) : 0f;
    }
}
