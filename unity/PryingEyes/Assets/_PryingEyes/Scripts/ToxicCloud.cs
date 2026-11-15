using UnityEngine;

namespace PryingEyes
{
    /// <summary>Nube tossica dell'Appestato: qualche secondo di veleno verde a terra dove e' scoppiato. Sull'ospite (o da solo) ferisce
    /// chi ci sta dentro: i giocatori poco (8 al secondo), i nemici molto (60 al secondo: e' il motivo per farlo scoppiare in mezzo
    /// all'orda). Sugli ospitati e' solo da vedere. Le uccisioni della nube pagano come un'esplosione.</summary>
    public class ToxicCloud : MonoBehaviour
    {
        const float Radius = 3.0f;
        float life, age, tick = 0.3f, puffT;
        bool damaging;
        Light glow;

        public static void Spawn(Vector3 pos, float seconds, bool damaging)
        {
            var go = new GameObject("NubeTossica");
            go.transform.position = pos + Vector3.up * 0.1f;
            var c = go.AddComponent<ToxicCloud>();
            c.life = seconds;
            c.damaging = damaging;
            c.glow = go.AddComponent<Light>();
            c.glow.type = LightType.Point;
            c.glow.color = new Color(0.45f, 1f, 0.22f);
            c.glow.range = 6.5f;
            c.glow.intensity = 1.6f;
            c.glow.shadows = LightShadows.None;
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] nube tossica a {pos} per {seconds:F1} s{(damaging ? "" : " (solo vista)")}");
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            float k = Mathf.Clamp01(age / life);
            puffT -= dt;
            if (puffT <= 0f && k < 0.85f)
            {
                puffT = 0.07f;
                Vector2 r = Random.insideUnitCircle * Radius * 0.85f;
                Fx.ToxicPuff(transform.position + new Vector3(r.x, Random.Range(0.1f, 0.9f), r.y));
            }
            if (glow != null) glow.intensity = 1.6f * (1f - k) * (0.85f + 0.15f * Mathf.Sin(age * 7f));
            if (damaging)
            {
                tick -= dt;
                if (tick <= 0f)
                {
                    tick = 0.5f;
                    Vector3 c = transform.position;
                    for (int i = 0, n = Targets.Count; i < n; i++)
                    {
                        var t = Targets.At(i);
                        if (t == null || !t.Alive) continue;
                        Vector3 f = t.Feet;
                        if (Mathf.Abs(f.y - c.y) > 2.2f || new Vector2(f.x - c.x, f.z - c.z).magnitude > Radius) continue;
                        t.Hurt(4f, c);
                        if (t is LocalTarget) GameManager.damageTaken++;
                    }
                    var list = new System.Collections.Generic.List<Enemy>(Enemy.All);
                    foreach (var e in list)
                    {
                        if (e == null || e.IsDead || Enemy.IsFlying(e.type)) continue;
                        Vector3 p = e.transform.position;
                        if (Mathf.Abs(p.y - c.y) > 2.2f || new Vector2(p.x - c.x, p.z - c.z).magnitude > Radius) continue;
                        e.Damage(30f, p + Vector3.up, false, false, DamageSource.Esplosione);
                    }
                }
            }
            if (age >= life) Destroy(gameObject);
        }
    }
}
