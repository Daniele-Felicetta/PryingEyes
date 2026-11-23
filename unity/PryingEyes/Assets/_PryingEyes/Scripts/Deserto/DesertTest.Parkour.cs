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
            var room = site.levels[0].rooms[PlanGen.StackRooms - 1];
            float wall = room.x1 * 0.1f + FloorBuilder.TH;
            var pl = Pl;
            var cc = pl.GetComponent<CharacterController>();
            yield return GoTo(wall + 11f, room.CenterZ);
            yield return WaitFor(() => AllBuilt(RtOf(site)), 90f);
            if (!AllBuilt(RtOf(site))) { Check("parkour: the tower shell is loaded", false); yield break; }

            float ground = pl.transform.position.y;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.25f);
            float first = pl.transform.position.y;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.23f);
            float second = pl.transform.position.y;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.25f);
            Check("parkour: one extra jump per airtime", first > ground + 0.4f && second > first + 0.45f && pl.transform.position.y < second + 0.18f);
            yield return WaitFor(() => cc.isGrounded, 3f);

            // A plain collider exercises the normal player input path without a generated wall or marker.
            float deckY = Mathf.Max(ground, site.padY) + 0.52f;
            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.transform.position = new Vector3(pl.transform.position.x - 3.1f, deckY - 0.17f, pl.transform.position.z);
            deck.transform.localScale = new Vector3(5.7f, 0.34f, 6.4f);
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Unmarked stone test structure";
            block.transform.position = new Vector3(pl.transform.position.x - 4.1f, deckY + 1.65f, pl.transform.position.z);
            block.transform.localScale = new Vector3(1.7f, 3.3f, 3.6f);
            Physics.SyncTransforms();
            float blockEdge = block.transform.position.x + 0.85f;
            yield return ApproachClimb(blockEdge, block.transform.position.z, deckY);
            Check("parkour: the first grab works on an ordinary static structure", pl.OnClimbWall, pl.transform.position.ToString("F2"));
            var hands = pl.cam.Find("Parkour hands");
            Check("parkour: both gloves and sleeves are drawn and the weapon is lowered",
                hands != null && hands.GetComponentsInChildren<SkinnedMeshRenderer>().Length >= 4 && !pl.weapon.CanFireNow);
            var gripAt = pl.transform.position;
            Pl.parkourTestDirection = Vector3.zero;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.18f);
            Check("parkour: a neutral jump releases and pushes out", !pl.OnClimbWall && pl.transform.position.x > gripAt.x + 0.5f);
            yield return ApproachClimb(blockEdge, block.transform.position.z, deckY);
            Vector3 sideStart = pl.transform.position;
            Pl.parkourTestDirection = Vector3.forward;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.09f);
            Check("parkour: a sideways jump leaves the wall", !pl.OnClimbWall);
            yield return WaitFor(() => pl.OnClimbWall, 1.0f);
            Check("parkour: the sideways leap catches a grip farther along", pl.OnClimbWall && pl.transform.position.z > sideStart.z + 0.82f,
                pl.transform.position.ToString("F2"));
            yield return ApproachClimb(blockEdge, block.transform.position.z, deckY);
            var opposite = GameObject.CreatePrimitive(PrimitiveType.Cube);
            opposite.transform.position = new Vector3(blockEdge + 4.9f, deckY + 2.4f, block.transform.position.z);
            opposite.transform.localScale = new Vector3(1.6f, 4.8f, 3.6f);
            Physics.SyncTransforms();
            pl.yaw = Mathf.PI * 0.5f;
            pl.parkourTestDirection = Vector3.right;
            pl.parkourTestJump = true;
            yield return new WaitForSeconds(0.10f);
            yield return WaitFor(() => pl.OnClimbWall, 1.1f);
            yield return new WaitForSeconds(0.18f);
            Check("parkour: a forward leap reaches a different wall across the gap", pl.OnClimbWall &&
                pl.transform.position.x > blockEdge + 3.2f, pl.transform.position.ToString("F2"));
            Destroy(opposite);
            yield return ApproachClimb(blockEdge, block.transform.position.z, deckY);
            Pl.parkourTestDirection = Vector3.forward;
            float outerCorner = block.transform.position.z + 1.8f;
            yield return WaitFor(() => pl.OnClimbWall && pl.transform.position.z > outerCorner + 0.48f && pl.transform.position.x < blockEdge - 0.16f, 3f);
            Check("parkour: sideways movement rounds a solid outside corner", pl.OnClimbWall && pl.transform.position.z > outerCorner + 0.48f &&
                pl.transform.position.x < blockEdge - 0.16f, pl.transform.position.ToString("F2"));
            yield return ApproachClimb(blockEdge, block.transform.position.z, deckY);
            yield return WaitFor(() => !pl.OnClimbWall && pl.transform.position.y > deckY + 3f, 5f);
            Check("parkour: holding forward pulls onto a generic ledge", !pl.OnClimbWall && pl.transform.position.y > deckY + 3f &&
                pl.transform.position.x < blockEdge - 0.4f, pl.transform.position.ToString("F2"));
            StopClimbBot();
            Destroy(block);
            Destroy(deck);

            float balconyZ = PlanGen.TowerBalconyCenter(room) * 0.1f;
            float firstBalcony = site.LevelY(1);
            float lane = balconyZ - 1.83f;
            yield return ApproachClimb(wall, lane, site.padY);
            yield return WaitFor(() => pl.OnClimbWall && pl.transform.position.y > firstBalcony - 0.85f, 7f);
            Pl.parkourTestDirection = Vector3.zero;
            yield return new WaitForSeconds(0.28f);
            Check("parkour: held forward climbs successive stone courses", pl.OnClimbWall && pl.transform.position.y > firstBalcony - 0.85f,
                $"feet {pl.transform.position:F2}, balcony {firstBalcony:F2}");
            if (shots && pl.OnClimbWall)
            {
                Face(new Vector3(wall, pl.transform.position.y + 1.70f, pl.transform.position.z));
                yield return null;
                Shot("parkour_masonry");
            }

            Pl.parkourTestDirection = Vector3.forward;
            yield return WaitFor(() => !pl.OnClimbWall && Mathf.Abs(pl.transform.position.y - firstBalcony) < 0.20f, 3f);
            Pl.parkourTestDirection = Vector3.zero;
            yield return new WaitForSeconds(0.17f);
            Check("parkour: sidestepping from the wall lands on the balcony", !pl.OnClimbWall && cc.isGrounded &&
                Mathf.Abs(pl.transform.position.y - firstBalcony) < 0.20f, pl.transform.position.ToString("F2"));
            yield return WalkRoofLeg(new Vector3(wall + 0.70f, firstBalcony, balconyZ));
            PlayerController.holdLook = true;
            Pl.yaw = -Mathf.PI * 0.5f;
            Pl.parkourTestDirection = Vector3.left;
            yield return WaitFor(() => pl.transform.position.x < wall - 1f, 2f);
            StopClimbBot();
            Check("parkour: the balcony doorway leads into the tower", pl.transform.position.x < wall - 1f &&
                Mathf.Abs(pl.transform.position.y - firstBalcony) < 0.20f, pl.transform.position.ToString("F2"));

            float windowZ = room.CenterZ - PlanGen.TowerBalconyOffset * 0.1f;
            float windowApproachY = firstBalcony - 1.47f;
            var windowApproach = GameObject.CreatePrimitive(PrimitiveType.Cube);
            windowApproach.transform.position = new Vector3(wall + 0.85f, windowApproachY - 0.15f, windowZ);
            windowApproach.transform.localScale = new Vector3(1.25f, 0.30f, 2.4f);
            Physics.SyncTransforms();
            yield return ApproachClimb(wall, windowZ, windowApproachY);
            yield return WaitFor(() => !pl.OnClimbWall && pl.transform.position.x < wall - 0.65f, 3.5f);
            StopClimbBot();
            Check("parkour: climbing through a glassless window lands inside the floor", pl.transform.position.x < wall - 0.65f &&
                Mathf.Abs(pl.transform.position.y - firstBalcony) < 0.22f, pl.transform.position.ToString("F2"));
            Destroy(windowApproach);

            int top = site.LevelCount - 1;
            var stair = PlanGen.StairRoom(site.levels[top]);
            float topY = site.LevelY(top);
            float roofY = topY + PlanGen.LevelHeight + 0.14f;
            float south = stair.z0 * 0.1f + 0.8f, north = stair.z1 * 0.1f - 0.8f;
            float east = stair.x1 * 0.1f, west = stair.x0 * 0.1f + 0.74f;
            pl.Teleport(new Vector3(east - 0.55f, topY + 0.08f, south));
            yield return new WaitForSeconds(0.2f);
            yield return WalkRoofLeg(new Vector3(west, topY + 2.7f, south));
            yield return WalkRoofLeg(new Vector3(west, topY + 2.7f, north));
            yield return WalkRoofLeg(new Vector3(east + 1.8f, roofY, north));
            Check("parkour: the top floor stairs open directly onto a solid roof", pl.transform.position.x > east + 1f &&
                Mathf.Abs(pl.transform.position.y - roofY) < 0.20f, $"feet {pl.transform.position:F2}, roof {roofY:F2}");
            Shot("parkour_roof_access");
            yield return WalkRoofLeg(new Vector3(west, topY + 2.7f, north));
            yield return WalkRoofLeg(new Vector3(west, topY + 2.7f, south));
            yield return WalkRoofLeg(new Vector3(east - 0.65f, topY, south));
            yield return WalkRoofLeg(new Vector3(east - 0.65f, topY, stair.CenterZ));
            yield return WalkRoofLeg(new Vector3(east + 1.8f, topY, stair.CenterZ));
            Check("parkour: the roof stairs lead back into the top floor", pl.transform.position.x > east + 1f &&
                Mathf.Abs(pl.transform.position.y - topY) < 0.20f, pl.transform.position.ToString("F2"));

            float edgeZ = room.CenterZ - 4.8f, startY = roofY - 3.2f;
            var scaffold = GameObject.CreatePrimitive(PrimitiveType.Cube);
            scaffold.transform.position = new Vector3(wall + 0.95f, startY - 0.15f, edgeZ);
            scaffold.transform.localScale = new Vector3(1.65f, 0.30f, 2.3f);
            Physics.SyncTransforms();
            yield return ApproachClimb(wall, edgeZ, startY);
            yield return WaitFor(() => !pl.OnClimbWall && pl.transform.position.y >= roofY - 0.1f, 7f);
            Check("parkour: climbing from outside clears the battlements onto the roof", !pl.OnClimbWall &&
                pl.transform.position.x < wall - 0.5f && Mathf.Abs(pl.transform.position.y - roofY) < 0.20f,
                $"feet {pl.transform.position:F2}, roof {roofY:F2}");
            Destroy(scaffold);
            StopClimbBot();
            yield return Wingsuit();
            pl.parkourTestDirection = null;
        }

        IEnumerator Wingsuit()
        {
            var pl = Pl;
            var sand = OpenSand();
            yield return GoTo(sand.x, sand.y);
            Vector3 ground = pl.transform.position;
            pl.Teleport(ground + Vector3.up * 17f);
            pl.yaw = Mathf.PI * 0.5f;
            pl.pitch = 0.04f;
            pl.parkourTestGlide = true;
            yield return WaitFor(() => pl.Gliding, 1.6f);
            Vector3 from = pl.transform.position;
            yield return new WaitForSeconds(1.05f);
            Vector3 travel = pl.transform.position - from;
            int panels = 0;
            foreach (var renderer in pl.cam.GetComponentsInChildren<MeshRenderer>())
                if (renderer.name == "Wingsuit fabric") panels++;
            Check("parkour: holding jump during a fall opens the wingsuit and covers ground", pl.Gliding && travel.x > 6.4f &&
                travel.y < -1.8f && travel.y > -6.0f && panels == 2 && !pl.weapon.CanFireNow, $"flight {travel:F2}, {panels} panels");
            if (shots && pl.Gliding)
            {
                pl.pitch = 0.57f;
                yield return null;
                Shot("parkour_wingsuit");
                pl.pitch = 0.04f;
            }
            pl.yaw = 0f;
            yield return new WaitForSeconds(0.70f);
            Check("parkour: steering banks the wingsuit and changes the flight path", pl.Gliding &&
                pl.GetComponent<CharacterController>().velocity.z > 4f);
            pl.parkourTestGlide = false;
            yield return new WaitForSeconds(0.34f);
            Check("parkour: releasing jump folds the suit and brings back the weapon", !pl.Gliding && pl.weapon.ViewRootT.gameObject.activeSelf);

            pl.Teleport(ground + Vector3.up * 12f);
            pl.yaw = Mathf.PI * 0.5f;
            pl.pitch = 0.34f;
            pl.parkourTestGlide = true;
            yield return WaitFor(() => pl.Gliding, 1.6f);
            bool opened = pl.Gliding;
            var cc = pl.GetComponent<CharacterController>();
            yield return WaitFor(() => cc.isGrounded, 5f);
            Check("parkour: landing folds the wingsuit even with jump held", opened && cc.isGrounded && !pl.Gliding);

            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = ground + Vector3.right * 12.5f + Vector3.up * 8f;
            target.transform.localScale = new Vector3(1.8f, 16f, 4.5f);
            Physics.SyncTransforms();
            pl.Teleport(ground + Vector3.up * 13f);
            pl.yaw = Mathf.PI * 0.5f;
            pl.pitch = 0.04f;
            pl.parkourTestDirection = Vector3.right;
            yield return WaitFor(() => pl.Gliding, 1.6f);
            opened = pl.Gliding;
            yield return WaitFor(() => pl.OnClimbWall, 2.9f);
            Check("parkour: a flight can chain directly into a wall grip", opened && pl.OnClimbWall && !pl.Gliding &&
                pl.transform.position.x > ground.x + 9f, pl.transform.position.ToString("F2"));
            Destroy(target);
            pl.parkourTestGlide = false;
            StopClimbBot();

            var writer = new NetWriter();
            new PlayerNet { gliding = true, heading = 0.65f, gun = 255 }.Write(writer);
            var reader = new NetReader(writer.ToArray());
            var snapshot = PlayerNet.Read(ref reader);
            Check("parkour: the flight state round-trips through the co-op snapshot", snapshot.gliding && !snapshot.climbing &&
                snapshot.gun == 255 && reader.Ok && reader.Left == 0);

            var dummy = new GameObject("Wingsuit pose test");
            dummy.transform.position = ground + Vector3.up * 20f;
            var body = SurvivorBody.Create(dummy.transform, 0, 0);
            int bodyPanels = 0;
            if (body != null)
            {
                var input = SurvivorInput.Idle;
                input.gliding = true;
                body.input = input;
                body.Step(0.25f);
                foreach (var renderer in dummy.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.name == "Wingsuit fabric") bodyPanels++;
            }
            Check("parkour: the external avatar wears two deployed fabric panels", bodyPanels == 2);
            Destroy(dummy);
        }

        IEnumerator ApproachClimb(float faceX, float z, float y)
        {
            StopClimbBot();
            Pl.Teleport(new Vector3(faceX + 0.86f, y + 0.25f, z));
            yield return WaitFor(() => Pl.GetComponent<CharacterController>().isGrounded, 3f);
            Pl.yaw = -Mathf.PI * 0.5f;
            Pl.pitch = -0.10f;
            PlayerController.holdLook = true;
            Pl.parkourTestDirection = Vector3.left;
            Pl.parkourTestJump = true;
            yield return WaitFor(() => Pl.OnClimbWall, 0.7f);
            Pl.parkourTestDirection = Vector3.zero;
            yield return new WaitForSeconds(0.20f);
            Pl.parkourTestDirection = Vector3.left;
        }

        IEnumerator WalkRoofLeg(Vector3 target)
        {
            PlayerController.holdLook = true;
            float end = Time.time + 4f;
            while (Flat(Pl.transform.position, target) > 0.28f && Time.time < end)
            {
                Vector3 flat = Vector3.ProjectOnPlane(target - Pl.transform.position, Vector3.up);
                Pl.yaw = Mathf.Atan2(flat.x, flat.z);
                Pl.parkourTestDirection = Vector3.ClampMagnitude(flat * 2f, 1f);
                yield return null;
            }
            StopClimbBot();
            yield return new WaitForSeconds(0.15f);
        }

        void StopClimbBot()
        {
            Pl.parkourTestDirection = Vector3.zero;
            PlayerController.holdLook = true;
        }
    }
}
#endif
