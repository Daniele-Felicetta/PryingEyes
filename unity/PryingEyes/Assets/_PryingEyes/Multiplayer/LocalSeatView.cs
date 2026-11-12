using UnityEngine;

namespace PryingEyes.Multiplayer
{
    /// <summary>Explicit avatar-local references only. Does not touch Camera.main or other scene cameras.</summary>
    public sealed class LocalSeatView : MonoBehaviour
    {
        [SerializeField] Camera seatCamera;
        [SerializeField] AudioListener seatListener;
        [SerializeField] Canvas seatHud;

        public void Bind(bool local, int seat, int localCount)
        {
            if (seatCamera != null)
            {
                seatCamera.enabled = local;
                if (local)
                {
                    var rect = SplitLayout.ForSeat(seat, localCount);
                    seatCamera.rect = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
            // Exactly one listener among these avatars; bootstrap scene must have no other listener.
            if (seatListener != null) seatListener.enabled = local && seat == 0;
            if (seatHud != null)
            {
                seatHud.enabled = local;
                seatHud.renderMode = RenderMode.ScreenSpaceCamera;
                seatHud.worldCamera = seatCamera;
            }
        }
    }
}
