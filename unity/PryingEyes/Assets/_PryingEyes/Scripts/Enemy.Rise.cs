using UnityEngine;

namespace PryingEyes
{
    /// <summary>Chi cammina, quando nasce in un'ondata, sale dal pavimento invece di comparire (come nei Black Ops Zombies): parte 1,9 m
    /// sotto, risale in poco piu' di un secondo con la frenata in cima, tra polvere e schegge e uno scricchiolio; nel frattempo niente
    /// navmesh e niente intelligenza, ma i colpi lo prendono. Sugli ospitati la salita arriva con le posizioni dell'ospite.</summary>
    public partial class Enemy
    {
        float riseT = -1f, riseDur = 1.2f, riseDust;
        Vector3 riseTo;
        public bool Rising => riseT >= 0f;
        const float RiseDepth = 1.9f;

        public void RiseFromGround(float duration = 1.2f)
        {
            if (puppet || IsFlying(type) || dead) return;
            riseTo = transform.position;
            riseDur = Mathf.Max(0.3f, duration);
            riseT = 0f;
            riseDust = 0f;
            if (agent != null) agent.enabled = false;
            transform.position = riseTo + Vector3.down * RiseDepth;
            Fx.MeleeImpact(riseTo + Vector3.up * 0.05f, Vector3.up);
            Fx.EnemyImpact(riseTo + Vector3.up * 0.1f, Vector3.up, new Color(0.40f, 0.35f, 0.28f));
            Fx.BloodPool(riseTo, new Color(0.10f, 0.085f, 0.07f, 1f), 0.75f);          // il pavimento rotto, scuro
            Sfx.At(SoundBank.Creak, riseTo + Vector3.up * 0.3f, 0.8f, Random.Range(0.55f, 0.7f), 2f, 26f);
        }

        /// <summary>Un passo della salita (Update). Finita, la navmesh riprende da dove deve stare.</summary>
        void RiseTick(float dt)
        {
            riseT += dt;
            float k = Mathf.Clamp01(riseT / riseDur);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);
            transform.position = riseTo + Vector3.down * (RiseDepth * (1f - e));
            riseDust -= dt;
            if (riseDust <= 0f && k < 0.85f)
            {
                riseDust = 0.12f;
                Fx.MeleeImpact(riseTo + Random.insideUnitSphere * 0.25f + Vector3.up * 0.05f, Vector3.up);
            }
            if (anim != null) { anim.SetFloat("Velocita", 0f); anim.SetFloat("PassoVel", 0.6f); }
            if (k < 1f) return;
            riseT = -1f;
            transform.position = riseTo;
            if (agent != null)
            {
                agent.enabled = true;
                if (agent.isOnNavMesh) agent.Warp(riseTo);
            }
        }
    }
}
