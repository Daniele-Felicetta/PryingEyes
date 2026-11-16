using UnityEngine;

namespace PryingEyes
{
    // Il colpo che uccide decide come cade il corpo (EnemyRagdoll): dove ha preso, da dove veniva, quanto era forte (i pallini della
    // doppietta nello stesso fotogramma si sommano) e, per le esplosioni, il centro dello scoppio.
    public partial class Enemy
    {
        Vector3 lastHitPoint, lastHitDir;
        float lastHitDmg;
        int lastHitFrame = -1;
        static Vector3 blastCenter;
        static int blastFrame = -1;

        /// <summary>Chi fa scoppiare qualcosa (botte, Appestato, granata) lo dice prima di ferire i nemici: chi muore in questo
        /// fotogramma vola via dal centro invece di crollare.</summary>
        public static void NoteBlast(Vector3 center)
        {
            blastCenter = center;
            blastFrame = Time.frameCount;
        }

        void NoteHit(float dmg, Vector3 point, ITarget shooter)
        {
            if (Time.frameCount != lastHitFrame) { lastHitFrame = Time.frameCount; lastHitDmg = 0f; }
            lastHitDmg += Mathf.Min(dmg, 300f);
            lastHitPoint = point;
            Vector3 from = shooter != null ? (shooter.LookAt != null ? shooter.LookAt.position : shooter.Feet + Vector3.up * 1.5f)
                                           : point - transform.forward;
            lastHitDir = point - from;
        }

        /// <summary>Il ragdoll per la morte appena avvenuta, o null (allora la vecchia animazione). 'carried' = la velocita' che aveva.</summary>
        EnemyRagdoll BeginRagdoll(DamageSource src, Vector3 carried)
        {
            if (riseT >= 0f || type == EnemyType.Statua) return null;      // mezzo sotto il pavimento; la statua di pietra crolla rigida
            bool explosive = src == DamageSource.Esplosione || src == DamageSource.Scoppio;
            bool blast = explosive && blastFrame == Time.frameCount;
            bool hit = lastHitFrame >= 0;
            float impulse;
            if (src == DamageSource.Nuke || (explosive && !blast)) impulse = 0f;  // la Campana, il fuoco, la nube: si accascia
            else if (blast) impulse = 5.5f;                                         // m/s al centro, meno con la distanza
            else if (src == DamageSource.Takedown) impulse = 5f;
            else if (src == DamageSource.Knife) impulse = 18f;
            else impulse = Mathf.Clamp(10f + lastHitDmg * 0.25f, 12f, 45f);         // pistola ~20, doppietta e cecchino 45
            Vector3 point = blast ? blastCenter : hit ? lastHitPoint : transform.position + Vector3.up * 1.2f;
            var chaser = tgt ?? Targets.Local;                                        // chi inseguiva (a schermo condiviso non e' sempre il posto 0)
            Vector3 dir = hit ? lastHitDir : (chaser != null ? transform.position - chaser.Feet : -transform.forward);
            return EnemyRagdoll.Begin(gameObject, anim, carried, point, dir, impulse, blast);
        }
    }
}
