using UnityEngine;

namespace PryingEyes
{
    /// <summary>Reazione ai colpi procedurale, sopra l'animazione (2026-11-15). Quando un monaco (ogni costume) o l'abate viene colpito
    /// senza morire, bacino, busto e testa vengono spinti via dal colpo da una molla smorzata e tornano in circa mezzo secondo: un
    /// colpo al petto piega il busto all'indietro e la testa lo segue, un colpo in testa la butta indietro di piu', i pallini della
    /// doppietta si sommano fino a un tetto; un filo di torsione a caso perche' due colpi uguali non sembrino uguali. Si applica in
    /// LateUpdate, dopo l'Animator, come rotazione nel mondo attorno all'asse orizzontale perpendicolare al colpo; per ogni osso si
    /// ricorda la posa dell'Animator, cosi' se in un fotogramma l'Animator non la riscrive (fuori inquadratura) la piega non si
    /// accumula. Oltre 35 m dalla camera si ferma. La statua di pietra non reagisce; l'abate, piu' pesante, al 70%.</summary>
    public sealed class EnemyFlinch : MonoBehaviour
    {
        const float Stiff = 120f, Damp = 15f, MaxDeg = 28f;

        struct Bone
        {
            public Transform t;
            public Quaternion baseLocal, applied;
            public bool has;
        }

        Bone pelvis, chest, head;
        // vettori di rotazione (asse * gradi) nel mondo e loro velocita', per bacino, busto e testa
        Vector3 vPel, wPel, vChest, wChest, vHead, wHead;
        int lastFrame = -1;
        float frameKick;
        public float Scale = 1f;

        /// <summary>Gradi di piega adesso (le prove li leggono).</summary>
        public float ChestDeg => vChest.magnitude;
        public float HeadDeg => vHead.magnitude;
        /// <summary>Asse della piega del busto nel mondo (zero se fermo).</summary>
        public Vector3 ChestAxis => vChest.sqrMagnitude > 1e-6f ? vChest.normalized : Vector3.zero;
        public Transform ChestBone => chest.t;

        public static EnemyFlinch For(GameObject body)
        {
            var f = body.GetComponent<EnemyFlinch>();
            if (f != null) return f.chest.t != null ? f : null;
            f = body.AddComponent<EnemyFlinch>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Renderer>() != null) continue;
                switch (t.name)
                {
                    case "bacino": if (f.pelvis.t == null) f.pelvis.t = t; break;
                    case "petto": if (f.chest.t == null) f.chest.t = t; break;
                    case "testa": if (f.head.t == null) f.head.t = t; break;
                }
            }
            if (f.pelvis.t == null || f.chest.t == null || f.head.t == null) { f.chest.t = null; f.enabled = false; return null; }
            return f;
        }

        /// <summary>Un colpo: 'dir' da chi spara verso il nemico, 'dmg' il danno, 'head' se in testa.</summary>
        public void Kick(Vector3 dir, float dmg, bool head)
        {
            if (!enabled) return;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            // i pallini dello stesso fotogramma si sommano, con un tetto (la doppietta piega, non spezza)
            if (Time.frameCount != lastFrame) { lastFrame = Time.frameCount; frameKick = 0f; }
            float k = Mathf.Clamp(6f + dmg * 0.22f, 6f, 16f) * Scale;
            k = Mathf.Min(k, Mathf.Max(0f, 22f * Scale - frameKick));
            if (k <= 0f) return;
            frameKick += k;
            Vector3 axis = Vector3.Cross(Vector3.up, dir);          // la parte alta va nella direzione del colpo
            Vector3 twist = Vector3.up * Random.Range(-0.35f, 0.35f) * k;
            const float vel = 13f;                                   // gradi al secondo di partenza per grado di colpo
            wChest += (axis * k * (head ? 0.55f : 1f) + twist) * vel;
            wHead += axis * k * (head ? 1.9f : 0.6f) * vel;
            wPel += axis * k * 0.25f * vel;
        }

        static void Spring(ref Vector3 v, ref Vector3 w, float dt)
        {
            Vector3 a = -Stiff * v - Damp * w;
            w += a * dt;
            v += w * dt;
            if (v.magnitude > MaxDeg) { v = v.normalized * MaxDeg; w *= 0.5f; }
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            bool moving = vChest.sqrMagnitude + wChest.sqrMagnitude + vHead.sqrMagnitude + wHead.sqrMagnitude + vPel.sqrMagnitude + wPel.sqrMagnitude > 1e-4f;
            float eyeD = Seats.EyeDistanceSqr(transform.position);        // dall'occhio piu' vicino (a schermo condiviso, di qualunque giocatore)
            bool far = !float.IsPositiveInfinity(eyeD) && eyeD > 35f * 35f;
            if (!moving || far)
            {
                vChest = wChest = vHead = wHead = vPel = wPel = Vector3.zero;
                Release(ref pelvis); Release(ref chest); Release(ref head);
                return;
            }
            // a sottopassi di 8 ms: con un passo lungo (un fotogramma lento) Eulero si mangerebbe la spinta nel primo passo
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Spring(ref vPel, ref wPel, h);
                Spring(ref vChest, ref wChest, h);
                Spring(ref vHead, ref wHead, h);
            }
            Apply(ref pelvis, vPel);                                  // dal padre ai figli: la testa porta anche la piega del busto
            Apply(ref chest, vChest);
            Apply(ref head, vHead);
        }

        /// <summary>Riparte dalla posa dell'Animator (quella di questo fotogramma, o quella ricordata se non l'ha riscritta) e ci aggiunge la piega.</summary>
        static void Apply(ref Bone b, Vector3 v)
        {
            if (b.t == null) return;
            Quaternion cur = b.t.localRotation;
            if (!b.has || cur != b.applied) b.baseLocal = cur;
            b.t.localRotation = b.baseLocal;
            float deg = v.magnitude;
            if (deg >= 0.01f) b.t.rotation = Quaternion.AngleAxis(deg, v / deg) * b.t.rotation;
            b.applied = b.t.localRotation;
            b.has = true;
        }

        /// <summary>Fermo: rimette la posa dell'Animator se nessuno l'ha riscritta, poi non tocca piu' niente.</summary>
        static void Release(ref Bone b)
        {
            if (!b.has || b.t == null) return;
            if (b.t.localRotation == b.applied) b.t.localRotation = b.baseLocal;
            b.has = false;
        }

        void OnDisable() { Release(ref pelvis); Release(ref chest); Release(ref head); }
    }
}
