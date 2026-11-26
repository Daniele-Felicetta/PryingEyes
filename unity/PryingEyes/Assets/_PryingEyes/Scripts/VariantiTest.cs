#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -variantitest [-variantishots]: le varianti dell'orda col giocatore e la navmesh veri.
    /// Appestato: arrivato vicino scoppia, ferisce il giocatore e il monaco accanto e lascia la nube; ucciso da lontano scoppia lo stesso
    /// e il giocatore lontano non si fa niente. Statua: guardata resta ferma, girati e si avvicina; i colpi la scheggiano al 40%.
    /// Flagellante: da cinque metri balza. Cardinale: e' il capo (barra della vita accesa), a meta' vita si infuria, abbattuto paga 1000
    /// punti e lascia le munizioni, la barra si spegne. Con -variantishots le foto da vicino in unity_logs/varianti.</summary>
    public sealed class VariantiTest : PlayerTest
    {
        Vector3 arena, forward;
        Camera review;

        protected override string Suite { get { return "variantitest"; } }
        protected override string PhotoFlag { get { return "-variantishots"; } }
        protected override string OutFolder { get { return "varianti"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<VariantiTest>("-variantitest"); }

        protected override void Finish()
        {
            PlayerController.botDir = Vector3.zero;
        }

        void Photo(Enemy enemy, string name, float side = 2.4f, float front = 2.6f, float height = 1.5f)
        {
            if (!Photos || enemy == null) return;
            LightPool.Tick(false, Camera.main.transform.position);
            if (review == null)
            {
                review = new GameObject("Revisione varianti").AddComponent<Camera>();
                review.CopyFrom(Camera.main); review.enabled = false; review.cullingMask &= ~(1 << 2);
                var data = review.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true; data.volumeLayerMask = Camera.main.GetUniversalAdditionalCameraData().volumeLayerMask;
                var l = review.gameObject.AddComponent<Light>();
                l.type = LightType.Point; l.range = 9f; l.intensity = 2.2f; l.color = new Color(1f, 0.93f, 0.85f);
            }
            // un punto di vista con la linea libera fino al nemico (scaffali e colonne coprivano le prime foto)
            Vector3 target = enemy.transform.position + Vector3.up * 1.2f;
            float r = new Vector2(side, front).magnitude, a0 = Mathf.Atan2(side, front);
            Vector3 eye = enemy.transform.position + enemy.transform.right * side + enemy.transform.forward * front + Vector3.up * height;
            for (int k = 0; k < 12; k++)
            {
                float a = a0 + k * Mathf.PI / 6f;
                Vector3 e = enemy.transform.position + (enemy.transform.right * Mathf.Sin(a) + enemy.transform.forward * Mathf.Cos(a)) * r + Vector3.up * height;
                if (HotelNav.ClearLine(e, target) && HotelNav.ClearLine(e, enemy.transform.position + Vector3.up * 0.3f)) { eye = e; break; }
            }
            review.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            review.fieldOfView = 50;
            GameManager.CamShot(PhotoPath(name), review, false);
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var player = GameManager.I.player; var hud = GameManager.I.hud;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("spazio di prova libero sulla navmesh reale", MeleeTest.FindArena(out arena, out forward));
            if (Failed > 0) yield break;
            if (!Torch.I.on) Torch.I.Toggle();
            hud.Banner("", ""); hud.Toast("");
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            void Place(float yawOffset = 0f)
            {
                Enemy.ClearAll(); Fx.Clear();
                foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) Destroy(e.gameObject);     // anche chi sta ancora cadendo
                foreach (var c in FindObjectsByType<ToxicCloud>(FindObjectsSortMode.None)) Destroy(c.gameObject);
                player.Teleport(arena + Vector3.up * 0.05f); player.hp = 100; player.shield = 50; player.sedT = 0; player.invulnerable = false;
                player.yaw = Mathf.Atan2(forward.x, forward.z) + yawOffset * Mathf.Deg2Rad; player.pitch = 0;
            }

            // ---------------- Appestato vicino: scoppia, ferisce, nube
            Place();
            yield return null;
            var monk = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 3.2f + right * 1.3f);
            var plague = Enemy.Spawn(Enemy.EnemyType.Appestato, arena + forward * 3.4f);
            plague.MakeRelentless();
            Check("l'appestato ha il suo corpo (pustole)", plague.GetComponentsInChildren<MeshRenderer>().Length >= 8);
            Photo(plague, "appestato");
            float health = player.hp + player.shield, monkHp = monk.hp;
            deadline = Time.time + 8f;
            while (plague != null && !plague.IsDead && Time.time < deadline) yield return null;
            Check("l'appestato scoppia arrivato vicino", plague == null || plague.IsDead);
            yield return null;
            Check("lo scoppio ferisce il giocatore", player.hp + player.shield < health - 1f);
            Check("lo scoppio ferisce il monaco accanto", monk == null || monk.IsDead || monk.hp < monkHp - 1f);
            Check("resta la nube tossica", FindObjectsByType<ToxicCloud>(FindObjectsSortMode.None).Length > 0);
            yield return new WaitForSeconds(0.35f);
            Photo("appestato_scoppio");
            Debug.Log($"[PryingEyes] variantitest appestato: giocatore {health:F0} -> {player.hp + player.shield:F0}, monaco {monkHp:F0} -> {(monk != null ? monk.hp : 0f):F0}");

            // ---------------- Appestato ucciso da lontano: scoppia lo stesso, il giocatore lontano resta illeso
            Place();
            yield return null;
            var far = Enemy.Spawn(Enemy.EnemyType.Appestato, arena + forward * 7.5f);
            var neighbour = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 7.5f + right * 1.4f);
            Enemy.debugFreeze = true;
            yield return null;
            health = player.hp + player.shield; monkHp = neighbour.hp;
            int score0 = GameManager.I.score;
            far.Damage(far.hp + 5f, far.AimPoint, false, true, DamageSource.Gun);
            yield return null;
            Check("ucciso, l'appestato scoppia", far == null || far.IsDead);
            Check("lo scoppio colpisce il vicino", neighbour == null || neighbour.IsDead || neighbour.hp < monkHp - 1f);
            Check("il giocatore lontano resta illeso", Mathf.Abs(player.hp + player.shield - health) < 0.01f);
            Check("l'uccisione paga", GameManager.I.score > score0);
            Enemy.debugFreeze = false;

            // ---------------- Statua: guardata ferma, non guardata si avvicina, colpi al 40%
            Place();
            yield return null;
            var statue = Enemy.Spawn(Enemy.EnemyType.Statua, arena + forward * 6.5f);
            statue.MakeRelentless();
            yield return new WaitForSeconds(0.3f);
            Photo(statue, "statua");
            Photo("statua_guardata");
            Vector3 s0 = statue.transform.position;
            yield return new WaitForSeconds(2.5f);
            float still = Flat(statue.transform.position, s0);
            Check($"guardata, la statua resta ferma ({still:F2} m)", still < 0.3f);
            player.yaw += Mathf.PI;                                   // il giocatore si gira
            float d0 = Flat(statue.transform.position, player.transform.position);
            yield return new WaitForSeconds(1.2f);
            float d1 = Flat(statue.transform.position, player.transform.position);
            Check($"non guardata, la statua si avvicina ({d0:F1} -> {d1:F1} m)", d1 < d0 - 1.0f);
            player.yaw -= Mathf.PI;                                   // si rigira: la statua si ferma di nuovo
            yield return new WaitForSeconds(0.3f);
            Vector3 s2 = statue.transform.position;
            yield return new WaitForSeconds(1.0f);
            Check("riguardata, la statua si ferma di nuovo", Flat(statue.transform.position, s2) < 0.3f);
            Photo("statua_vicina");
            float shp = statue.hp;
            statue.Damage(100f, statue.AimPoint, false, false);
            Check($"i colpi scheggiano la statua al 40% ({shp - statue.hp:F0} su 100)", Mathf.Abs(shp - statue.hp - 40f) < 1f);

            // ---------------- Flagellante: balza da qualche metro
            Place();
            yield return null;
            var flag = Enemy.Spawn(Enemy.EnemyType.Flagellante, arena + forward * 5.2f);
            flag.MakeRelentless();
            Photo(flag, "flagellante");
            float best = 0f;
            Vector3 prev = flag.transform.position;
            deadline = Time.time + 3f;
            while (Time.time < deadline && flag != null && !flag.IsDead)
            {
                yield return null;
                float sp = Flat(flag.transform.position, prev) / Mathf.Max(1e-4f, Time.deltaTime);
                prev = flag.transform.position;
                if (sp < 30f) best = Mathf.Max(best, sp);
            }
            Check($"il flagellante balza (velocita' massima {best:F1} m/s)", best > 5f);

            // ---------------- Cardinale: capo, barra, furia, premio
            Place();
            yield return null;
            var card = Enemy.Spawn(Enemy.EnemyType.Cardinale, arena + forward * 6f);
            Check("il cardinale e' il capo del piano", Enemy.Boss == card);
            Check($"il cardinale e' robusto ({card.maxHp:F0} punti vita)", card.maxHp >= 3000f);
            yield return new WaitForSeconds(1.2f);
            var fill = hud.transform.Find("BossFill")?.GetComponent<Image>();
            Check("la barra del capo e' accesa", fill != null && fill.color.a > 0.5f);
            Photo(card, "cardinale", 3.2f, 3.6f, 1.9f);
            Photo("cardinale_barra");
            float spd = card.speed;
            card.Damage(card.maxHp * 0.55f, card.AimPoint, false, false);
            yield return null; yield return null;
            Check("a meta' vita il cardinale si infuria", card.speed > spd * 1.2f);
            int score1 = GameManager.I.score;
            card.Damage(card.hp + 10f, card.AimPoint, false, false);
            yield return null;
            Check("abbattuto paga almeno 1000 punti", GameManager.I.score >= score1 + 1000);
            bool ammo = false;
            foreach (var d in FindObjectsByType<DropPickup>(FindObjectsSortMode.None)) if (d.kind == DropKind.Ammo) ammo = true;
            Check("lascia le munizioni", ammo);
            Check("la barra si spegne", Enemy.Boss == null);
            yield return new WaitForSeconds(1.5f);
            Check("barra sfumata", fill == null || fill.color.a < 0.3f);
            Enemy.ClearAll();
        }
    }
}
#endif
