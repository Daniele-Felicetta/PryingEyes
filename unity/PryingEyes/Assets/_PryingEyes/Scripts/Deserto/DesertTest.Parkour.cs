#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    public sealed partial class DesertTest
    {
        IEnumerator Parkour()
        {
            var site = DesertWorld.Landmark();
            var east = site.levels[0].rooms[PlanGen.StackRooms - 1];
            var pl = Pl;
            var cc = pl.GetComponent<CharacterController>();
            PlayerController.botDir = Vector3.zero;
            yield return GoTo(east.x1 * 0.1f + 11f, east.CenterZ + 4.8f);
            yield return WaitFor(() => AllBuilt(RtOf(site)), 90f);
            var route = RtOf(site)?.fis[0]?.root.transform.parent.GetComponentInChildren<DesertTowerPipes>();
            Check("parkour: the loaded tower has one route from the sand to the roof", route != null && route.Count > site.LevelCount);
            if (route == null) yield break;

            float ground = pl.transform.position.y;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.25f);
            float first = pl.transform.position.y;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.23f);
            float second = pl.transform.position.y;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.25f);
            float third = pl.transform.position.y;
            Check("parkour: a second jump gains height and a third adds no impulse", first > ground + 0.4f && second > first + 0.45f && third < second + 0.18f,
                $"{first - ground:F2}, {second - ground:F2}, {third - ground:F2} m");
            yield return WaitFor(() => cc.isGrounded, 3f);
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.25f);
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.26f);
            Check("parkour: landing restores the air jump", pl.transform.position.y > ground + 1.05f);
            yield return WaitFor(() => cc.isGrounded, 3f);

            Vector3 bar = route.Bar(0);
            var outward = DesertTowerPipes.Outward;
            yield return CatchFirstPipe(bar, outward);
            Check("parkour: the first pipe catches from the first jump", pl.OnParkourPipe,
                $"feet {pl.transform.position:F2}, first pipe {bar:F2}");
            if (pl.OnParkourPipe)
            {
                var hands = pl.cam.Find("Parkour hands");
                Check("parkour: both gloves and sleeves are drawn while the weapon is stowed",
                    hands != null && hands.GetComponentsInChildren<SkinnedMeshRenderer>().Length >= 4 && !pl.weapon.ViewRootT.gameObject.activeSelf && !pl.weapon.CanFireNow);
                if (shots)
                {
                    PlayerController.holdLook = true;
                    Face(bar);
                    yield return null;
                    Shot("parkour_grip");
                    PlayerController.holdLook = false;
                }

                Vector3 heldAt = pl.transform.position;
                PlayerController.botDir = Vector3.zero;
                pl.parkourTestJump = true;
                yield return new WaitForSeconds(0.18f);
                Check("parkour: a neutral jump pushes away from the wall", !pl.OnParkourPipe &&
                    Vector3.Dot(pl.transform.position - heldAt, outward) > 0.5f && pl.transform.position.y > heldAt.y + 0.27f);
                yield return CatchFirstPipe(bar, outward);

                int i = 1;
                for (; i < route.Count && pl.OnParkourPipe; i++)
                {
                    pl.parkourTestJump = true;
                    if (shots && i == 1)
                    {
                        yield return new WaitForSeconds(0.19f);
                        Shot("parkour_transfer");
                    }
                    yield return new WaitForSeconds(0.43f);
                    if (Mathf.Abs(pl.transform.position.y - (route.Bar(i).y - 1.78f)) > 0.08f) break;
                }
                Check("parkour: every transfer reaches the next pipe", i == route.Count && pl.OnParkourPipe, $"{i}/{route.Count} at {pl.transform.position:F2}");
                pl.parkourTestJump = true;
                yield return new WaitForSeconds(1.25f);
                PlayerController.botDir = Vector3.zero;
                Check("parkour: the last grab pulls over the roof edge and restores the weapon", !pl.OnParkourPipe && cc.isGrounded &&
                    Mathf.Abs(pl.transform.position.y - route.RoofY) < 0.12f && pl.weapon.ViewRootT.gameObject.activeSelf, $"feet {pl.transform.position:F2}, roof {route.RoofY:F2}");
                Shot("parkour_roof");
            }

            PlayerController.botDir = PlayerController.botLook = Vector3.zero;
            PlayerController.holdLook = true;
        }

        IEnumerator CatchFirstPipe(Vector3 bar, Vector3 outward)
        {
            PlayerController.botDir = Vector3.zero;
            yield return GoTo(bar.x + 0.80f, bar.z, 1.5f);
            PlayerController.holdLook = false;
            PlayerController.botLook = -outward;
            Face(bar);
            PlayerController.botDir = -outward;
            Pl.parkourTestJump = true;
            yield return WaitFor(() => Pl.OnParkourPipe, 0.5f);
            yield return new WaitForSeconds(0.22f);
        }
    }
}
#endif
