#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -flinchtest [-flinchshots]: le reazioni ai colpi (EnemyFlinch). Un colpo al petto piega il
    /// busto nella direzione del colpo e la molla lo riporta dritto entro un secondo; un colpo in testa butta indietro la testa piu'
    /// del busto; otto pallini nello stesso fotogramma piegano di piu' ma restano sotto il tetto; il colpo che uccide spegne la
    /// molla (il ragdoll prende il corpo); la statua non reagisce.</summary>
    public sealed class FlinchTest : PlayerTest
    {
        protected override string Suite { get { return "flinchtest"; } }
        protected override string PhotoFlag { get { return "-flinchshots"; } }
        protected override string OutFolder { get { return "reazioni"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<FlinchTest>("-flinchtest"); }

        static void Face(PlayerController player, Vector3 target)
        {
            Vector3 d = target - player.cam.position;
            player.yaw = Mathf.Atan2(d.x, d.z);
            player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        static Transform Bone(Enemy e, string name)
        {
            foreach (var t in e.GetComponentsInChildren<Transform>(true)) if (t.name == name && t.GetComponent<Renderer>() == null) return t;
            return null;
        }

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            if (Failed > 0) yield break;
            player.Teleport(arena - forward * 2.5f + Vector3.up * 0.05f); player.invulnerable = true;
            Enemy.debugFreeze = true;
            var m = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 2.5f);
            m.transform.rotation = Quaternion.LookRotation(-forward);
            yield return new WaitForSeconds(0.4f);
            var chestB = Bone(m, "petto"); var headB = Bone(m, "testa");
            Face(player, chestB.position);
            yield return null;
            Photo("prima");
            Vector3 eye = player.cam.position;
            Vector3 dir = chestB.position - eye; dir.y = 0f; dir.Normalize();

            // colpo al petto
            m.Damage(12f, chestB.position, false);
            var f = m.GetComponent<EnemyFlinch>();
            Check("il colpo al petto fa reagire il monaco", f != null && f.enabled);
            if (Failed > 0) yield break;
            float chest1 = 0f, along = 0f;
            int frames = 0;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                yield return null;
                if (frames++ < 6) Debug.Log($"[PryingEyes] flinchtest fotogramma dt={Time.deltaTime:F4} busto={f.ChestDeg:F2} testa={f.HeadDeg:F2}");
                if (f.ChestDeg > chest1) { chest1 = f.ChestDeg; along = Vector3.Dot(f.ChestAxis, Vector3.Cross(Vector3.up, dir)); }
            }
            Photo("colpo_petto");
            Check($"il busto si piega nella direzione del colpo ({chest1:F1} gradi, allineamento {along:F2})", chest1 > 3f && along > 0.7f);
            yield return new WaitForSeconds(0.9f);
            Check($"poi torna dritto ({f.ChestDeg:F2} gradi, testa {f.HeadDeg:F2})", f.ChestDeg < 0.3f && f.HeadDeg < 0.3f);

            // colpo in testa
            m.Damage(12f, headB.position, true);
            float headPeak = 0f, chestAtHead = 0f;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                yield return null;
                if (f.HeadDeg > headPeak) { headPeak = f.HeadDeg; chestAtHead = f.ChestDeg; }
            }
            Photo("colpo_testa");
            Check($"il colpo in testa la butta indietro piu' del busto ({headPeak:F1} contro {chestAtHead:F1})", headPeak > 6f && headPeak > chestAtHead);
            yield return new WaitForSeconds(1f);

            // otto pallini nello stesso fotogramma
            for (int i = 0; i < 8; i++) m.Damage(9f, chestB.position + UnityEngine.Random.insideUnitSphere * 0.1f, false);
            float peak = 0f;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime) { peak = Mathf.Max(peak, f.ChestDeg); yield return null; }
            Check($"la doppietta piega di piu' ma sotto il tetto ({peak:F1} gradi)", peak > chest1 && peak < 28.1f);
            yield return new WaitForSeconds(1f);

            // il colpo che uccide: la molla si spegne, il corpo e' del ragdoll
            m.Damage(9999f, chestB.position, false);
            yield return null;
            Check("il colpo che uccide spegne la reazione", f == null || !f.enabled);

            // la statua non reagisce
            var s = Enemy.Spawn(Enemy.EnemyType.Statua, arena + forward * 2.5f + Vector3.Cross(Vector3.up, forward) * 1.5f);
            yield return new WaitForSeconds(0.3f);
            s.Damage(10f, s.AimPoint, false);
            yield return new WaitForSeconds(0.08f);
            var fs = s.GetComponent<EnemyFlinch>();
            Check("la statua di pietra non reagisce", fs == null || fs.ChestDeg < 0.01f);
            Enemy.debugFreeze = false;
            player.invulnerable = false;
        }
    }
}
#endif
