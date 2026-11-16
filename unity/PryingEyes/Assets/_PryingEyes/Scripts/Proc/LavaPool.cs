using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Una vasca di lava della Forgia del Guardaroba: ferisce chi ci mette i piedi dentro. Il bordo di pietra (alto mezzo metro, con il suo
    /// collider) non si scavalca per caso: ci si entra saltando o spinti, e la navmesh la aggira come ogni arredo. Chi simula il mondo (da solo o
    /// l'ospite) toglie la vita: ai giocatori 40 al secondo, ai nemici da terra quanto basta a ucciderli in un colpo di lava (senza punti: non e'
    /// un'arma). Gli ospitati la vedono soltanto.</summary>
    public sealed class LavaPool : MonoBehaviour
    {
        public static readonly List<LavaPool> All = new List<LavaPool>();

        /// <summary>La superficie nello spazio del mondo (x, z).</summary>
        public Rect area;
        /// <summary>La quota della superficie: sotto il bordo.</summary>
        public float surfaceY = 0.22f;
        float tick = 0.2f;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>Vero se il punto e' sulla superficie della lava (a quota di chi ci sta dentro).</summary>
        public bool Covers(Vector3 feet) =>
            area.Contains(new Vector2(feet.x, feet.z)) && feet.y < surfaceY + 0.55f && feet.y > surfaceY - 3f;

        void Update()
        {
            if (!Coop.Sim) return;
            tick -= Time.deltaTime;
            if (tick > 0f) return;
            tick = 0.25f;
            Vector3 c = new Vector3(area.center.x, surfaceY, area.center.y);
            for (int i = 0, n = Targets.Count; i < n; i++)
            {
                var t = Targets.At(i);
                if (t == null || !t.Alive || !Covers(t.Feet)) continue;
                t.Hurt(10f, c);
                if (t is LocalTarget) GameManager.damageTaken++;
            }
            var list = new List<Enemy>(Enemy.All);
            foreach (var e in list)
            {
                if (e == null || e.IsDead || Enemy.IsFlying(e.type)) continue;
                Vector3 p = e.transform.position;
                if (!Covers(p)) continue;
                e.Damage(400f, p + Vector3.up, false, false, DamageSource.Scoppio);
            }
        }
    }
}
