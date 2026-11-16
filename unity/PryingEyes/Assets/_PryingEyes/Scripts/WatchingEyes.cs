using UnityEngine;

namespace PryingEyes
{
    /// <summary>Un paio d'occhi nel buio (posizioni scelte da HotelBuilder dove la luce non arriva). Ti fissano e
    /// ogni tanto sbattono le palpebre; se li guardi in faccia o ti avvicini si chiudono di scatto, a volte con un
    /// sussurro, e riaprono piano dopo qualche secondo. Si vedono soprattutto con la coda dell'occhio.</summary>
    public class WatchingEyes : MonoBehaviour
    {
        public Renderer left, right;
        /// <summary>Quante volte si sono chiusi perche' guardati (letto dall'autotest).</summary>
        public static int closings;

        Material mat;
        Transform cam;
        float open = 1f, target = 1f, hiddenT, blinkT, blinking;

        void Start()
        {
            if (left == null || right == null) { enabled = false; return; }
            mat = new Material(left.sharedMaterial);
            left.sharedMaterial = mat;
            right.sharedMaterial = mat;
            blinkT = Random.Range(2f, 6f);
        }

        void Update()
        {
            // a schermo condiviso gli occhi nel buio guardano chi hanno piu' vicino, ma si chiudono se li guarda in faccia uno qualunque
            cam = Seats.NearestEye(transform.position);
            if (cam == null) return;
            float dt = Time.deltaTime;
            Vector3 to = cam.position - transform.position;
            float dist = to.magnitude;
            if (dist > 0.01f) transform.rotation = Quaternion.LookRotation(-to);   // il lato visibile del quad e' -Z

            if (hiddenT > 0f)
            {
                hiddenT -= dt;
                target = hiddenT > 0f ? 0f : 1f;
            }
            else
            {
                bool stared = StaredAt(cam);
                if (!stared && Seats.Split)
                    foreach (var s in Seats.All) if (s.Eye != null && s.Eye != cam && StaredAt(s.Eye)) { stared = true; break; }
                if (stared || dist < 6f)
                {
                    hiddenT = Random.Range(8f, 18f);
                    target = 0f;
                    closings++;
                    if (dist < 25f && Random.value < 0.5f)
                        Sfx.At(SoundBank.Whisper, transform.position, 0.22f, Random.Range(0.9f, 1.1f), 2f, 18f);
                }
                else
                {
                    blinkT -= dt;
                    if (blinkT <= 0f) { blinking = 0.14f; blinkT = Random.Range(2.5f, 7f); }
                    if (blinking > 0f) { blinking -= dt; target = 0f; }
                    else target = 1f;
                }
            }
            // si chiudono di scatto, si riaprono piano
            open = Mathf.MoveTowards(open, target, (target > open ? 2.2f : 12f) * dt);
            mat.SetFloat("_Open", open);
            bool vis = open > 0.01f;
            left.enabled = vis;
            right.enabled = vis;
        }

        /// <summary>Questo occhio e' guardato in faccia da chi ha la camera 'eye' (nel mirino, entro 40 m, con la linea libera)?</summary>
        bool StaredAt(Transform eye)
        {
            Vector3 to = eye.position - transform.position;
            return Vector3.Angle(eye.forward, -to) < 9f && to.magnitude < 40f && HotelNav.ClearLine(eye.position, transform.position);
        }
    }
}
