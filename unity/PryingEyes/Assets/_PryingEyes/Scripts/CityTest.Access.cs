#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    public sealed partial class CityTest
    {
        void LookAtGate(CityGate gate, Vector3 position)
        {
            var player = GameManager.I.player; player.Teleport(position);
            Vector3 look = gate.Point - (position + Vector3.up * player.eyeStand);
            player.yaw = Mathf.Atan2(look.x, look.z); player.pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude);
            PlayerController.holdLook = true;
        }

        bool GatePath(Vector3 outside, Vector3 inside)
        {
            var path = new NavMeshPath();
            return NavMesh.SamplePosition(outside, out var from, 1, NavMesh.AllAreas) && NavMesh.SamplePosition(inside, out var to, 1, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(from.position, to.position, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        IEnumerator RunAccess()
        {
            var gm = GameManager.I; var player = gm.player; var city = FindFirstObjectByType<CityDirector>();
            bool initial = city.Access != null && city.Access.IsOpen("street");
            foreach (var area in city.areas) initial &= city.Access != null && city.Access.IsOpen(area.block) == (area.price == 0);
            Record("ACCESS_BOOT", "city", initial, city.Gates.Count.ToString(), "street and configured free areas open; paid entries sealed");
            var layout = JsonUtility.FromJson<Production.Layout.DistrictPlan>(city.layoutJson);
            var seen = new HashSet<string>();
            foreach (var gate in city.Gates)
            {
                if (!seen.Add(gate.AreaId)) continue;
                var block = System.Array.Find(layout.blocks, b => b.id == gate.AreaId);
                Vector3 normal = (gate.Shutter.center - new Vector3(block.building.x, 0, block.building.z)).normalized;
                Vector3 outside = gate.Shutter.center + normal * 1.8f + Vector3.up * .3f;
                Vector3 inside = gate.Shutter.center - normal * 1.8f + Vector3.up * .3f;
                int price = city.Access.Price(gate.AreaId);
                Record("ACCESS_NAV_CLOSED", gate.AreaId, !GatePath(outside, inside), "entry sealed", "no complete path into unpaid area");
                LookAtGate(gate, outside); gm.AddPoints(price - 1 - gm.score); yield return new WaitForSeconds(.15f);
                bool selected = Interact.Tick(player.cam); Interact.TryUse();
                Record("ACCESS_NO_FUNDS", gate.AreaId, selected && !city.Access.IsOpen(gate.AreaId) && gm.score == price - 1,
                    $"selected={selected}, score={gm.score}", "visible prompt, no charge or opening with insufficient points");
                gm.AddPoints(price - gm.score);
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "AccessLineOfSightProbe";
                wall.transform.position = (player.cam.position + gate.Point) / 2;
                wall.transform.rotation = Quaternion.LookRotation(gate.Point - player.cam.position);
                wall.transform.localScale = new Vector3(2, 2, .12f); Physics.SyncTransforms();
                Interact.TryUse(); bool direct = city.TryPurchase(gate);
                Record("ACCESS_WALL_REJECTED", gate.AreaId, !direct && !city.Access.IsOpen(gate.AreaId) && gm.score == price,
                    gm.score.ToString(), "stale interaction and direct call cannot buy through wall");
                Object.Destroy(wall); yield return null;
                LookAtGate(gate, inside); yield return new WaitForSeconds(.1f);
                Record("ACCESS_CLOSED_SIDE_REJECTED", gate.AreaId, !city.TryPurchase(gate) && gm.score == price, gm.score.ToString(), "cannot buy from inside a closed area");
                LookAtGate(gate, outside); yield return new WaitForSeconds(.1f);
                PlayerController.holdLook = false; PlayerController.botDir = -normal;
                yield return new WaitForSeconds(.85f); PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
                float distance = Vector3.Dot(player.transform.position - gate.Shutter.center, normal);
                Record("ACCESS_CONTROLLER_BLOCKED", gate.AreaId, distance > .25f && Vector3.Distance(player.transform.position, outside) > .2f,
                    distance.ToString("F2"), "moving controller remains outside the closed shutter");
                LookAtGate(gate, outside); yield return new WaitForSeconds(.1f);
                selected = Interact.Tick(player.cam); if (Photos) GameManager.CamShot(PhotoPath(gate.AreaId + "_closed"), null, true);
                Interact.TryUse();
                Record("ACCESS_EXACT_PURCHASE", gate.AreaId, selected && city.Access.IsOpen(gate.AreaId) && gm.score == 0, gm.score.ToString(), "exact price charged and ownership opened");
                Record("ACCESS_ANIMATION_SEALED", gate.AreaId, !gate.Shutter.IsUp && !GatePath(outside, inside), gate.Shutter.IsUp.ToString(), "navigation stays sealed during shutter lift");
                yield return new WaitForSeconds(1.8f);
                Record("ACCESS_NAV_OPEN", gate.AreaId, gate.Shutter.IsUp && GatePath(outside, inside), gate.Shutter.IsUp.ToString(), "complete path after lift");
                bool repeat = city.TryPurchase(gate);
                Record("ACCESS_NO_DOUBLE_CHARGE", gate.AreaId, !repeat && gm.score == 0 && gate.Prompt == null, gm.score.ToString(), "opened access cannot charge again");
                float deadline = Time.time + 6;
                PlayerController.holdLook = false;
                while (Time.time < deadline && Vector3.Distance(player.transform.position, inside) > .6f)
                { PlayerController.botDir = (inside - player.transform.position).normalized; yield return null; }
                PlayerController.botDir = Vector3.zero; PlayerController.holdLook = true;
                Record("ACCESS_CONTROLLER_OPEN", gate.AreaId, Vector3.Distance(player.transform.position, inside) <= .6f,
                    player.transform.position.ToString(), "real controller enters purchased area");
                if (Photos) GameManager.CamShot(PhotoPath(gate.AreaId + "_open"), null, true);
            }
            int paid = 0; foreach (var area in city.areas) if (area.price > 0) paid++;
            Record("ACCESS_ALL_PAID_AREAS", "city", seen.Count == paid, seen.Count.ToString(), paid.ToString());
        }
    }
}
#endif
