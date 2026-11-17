using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Il corpo di un giocatore dello schermo condiviso come lo vedono gli altri (dalla 0.7.1: prima ognuno aveva solo le braccia in
    /// prima persona e nessuno vedeva nessuno). E' lo stesso manichino dei compagni della cooperativa (RemotePlayer.BuildBody, col colore
    /// del posto) con il nome sopra la testa, girato ogni volta verso la camera che disegna. Sta sullo strato del suo posto
    /// (Seats.FirstAvatarLayer + indice), che la sua camera non disegna (Seats.Layout): ognuno vede gli altri e non se stesso. Segue il
    /// corpo vero (PlayerController) a fine fotogramma: niente rete, niente collider (colpi e nemici non lo vedono), niente luce (la torcia
    /// vera del posto c'e' gia').</summary>
    public sealed class SeatAvatar : MonoBehaviour
    {
        Seat seat;
        Transform visual, legL, legR, headT, torchT;
        TextMesh label;
        float walkPhase;
        Vector3 lastPos;
        bool placed;

        static readonly List<SeatAvatar> all = new List<SeatAvatar>();
        static bool hooked;

        public Seat Seat => seat;
        public int Layer => Seats.FirstAvatarLayer + (seat != null ? seat.index : 0);

        public static SeatAvatar Create(Seat seat)
        {
            var go = new GameObject("Corpo visto dagli altri " + (seat.index + 1));
            var a = go.AddComponent<SeatAvatar>();
            a.seat = seat;
            a.visual = RemotePlayer.BuildBody(go.transform, seat.index, out a.legL, out a.legR, out a.headT, out a.torchT);
            var tg = new GameObject("Nome");
            tg.transform.SetParent(go.transform, false);
            tg.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            a.label = tg.AddComponent<TextMesh>();
            a.label.font = HUD.Serif();
            tg.GetComponent<MeshRenderer>().sharedMaterial = a.label.font.material;
            a.label.text = seat.Name;
            a.label.fontSize = 64; a.label.characterSize = 0.045f;
            a.label.anchor = TextAnchor.MiddleCenter; a.label.alignment = TextAlignment.Center;
            a.label.color = new Color(0.95f, 0.92f, 0.82f, 0.95f);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = a.Layer;
            a.Follow(0f);
            return a;
        }

        void OnEnable()
        {
            all.Add(this);
            if (!hooked) { hooked = true; RenderPipelineManager.beginCameraRendering += FaceCamera; }
        }

        void OnDisable() { all.Remove(this); }

        /// <summary>Prima che ogni camera disegni, i nomi guardano lei (una camera per posto: un nome solo non puo' guardarle tutte).</summary>
        static void FaceCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a.label == null) continue;
                Vector3 d = a.label.transform.position - eye;
                if (d.sqrMagnitude > 1e-6f) a.label.transform.rotation = Quaternion.LookRotation(d);
            }
        }

        void LateUpdate() { Follow(Time.deltaTime); }

        void Follow(float dt)
        {
            var p = seat != null ? seat.player : null;
            if (p == null) { if (visual != null && visual.gameObject.activeSelf) visual.gameObject.SetActive(false); return; }
            if (visual != null && !visual.gameObject.activeSelf) visual.gameObject.SetActive(true);
            Vector3 pos = p.transform.position;
            float v = 0f;
            if (placed && dt > 0f) v = Mathf.Min(6f, new Vector2(pos.x - lastPos.x, pos.z - lastPos.z).magnitude / dt);   // un teletrasporto non fa correre le gambe
            placed = true;
            lastPos = pos;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, p.yaw * Mathf.Rad2Deg, 0f));
            if (visual == null) return;
            walkPhase += dt * (3f + v * 1.6f);
            float swing = Mathf.Sin(walkPhase) * 32f * Mathf.Clamp01(v / 2.5f);
            if (legL != null) { legL.localRotation = Quaternion.Euler(swing, 0f, 0f); legR.localRotation = Quaternion.Euler(-swing, 0f, 0f); }
            // accovacciato: piu' basso; morto: a terra (come i compagni della cooperativa)
            float k = dt > 0f ? 1f - Mathf.Exp(-10f * dt) : 1f, k8 = dt > 0f ? 1f - Mathf.Exp(-8f * dt) : 1f;
            var vs = visual.localScale;
            visual.localScale = new Vector3(1f, Mathf.Lerp(vs.y, p.IsCrouching ? 0.72f : 1f, k), 1f);
            // a terra: semisdraiato sul fianco (0.7.2); morto: disteso
            Quaternion pose = !p.alive ? Quaternion.Euler(-88f, 0f, 0f) : p.Downed ? Quaternion.Euler(-62f, 0f, 14f) : Quaternion.identity;
            Vector3 lift = !p.alive ? new Vector3(0f, 0.18f, 0f) : p.Downed ? new Vector3(0f, 0.12f, -0.15f) : Vector3.zero;
            visual.localRotation = Quaternion.Slerp(visual.localRotation, pose, k8);
            visual.localPosition = Vector3.Lerp(visual.localPosition, lift, k8);
            if (label != null)
            {
                if (label.gameObject.activeSelf != p.alive) label.gameObject.SetActive(p.alive);
                string want = p.Downed ? seat.Name + "\nA TERRA " + Mathf.CeilToInt(p.BleedLeft) + " s" : seat.Name;
                if (label.text != want) label.text = want;
                label.color = p.Downed ? new Color(1f, 0.42f, 0.36f, 1f) : new Color(0.95f, 0.92f, 0.82f, 0.95f);
            }
        }
    }
}
