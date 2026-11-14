using System;
using System.Collections.Generic;
using Production.Layout;
using Production.Survival;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes
{
    [Serializable] public sealed class CityAreaDefinition { public string block, title; public int price; }

    public sealed partial class CityDirector
    {
        public CityAreaDefinition[] areas;
        public Material gateMetal, gateTrim;
        public AreaAccess Access { get; private set; }
        readonly List<CityGate> gates = new List<CityGate>();
        readonly HashSet<int> closedNavAreas = new HashSet<int>();
        public IReadOnlyList<CityGate> Gates => gates;
        DistrictPlan accessPlan;

        public static void ValidateAreas(CityAreaDefinition[] definitions, DistrictPlan plan)
        {
            var seen = new HashSet<string>();
            if (definitions == null || definitions.Length != plan.blocks.Length) throw new ArgumentException("CITY_AREAS_MISSING");
            foreach (var area in definitions)
                if (area == null || string.IsNullOrWhiteSpace(area.title) || area.price < 0 || !seen.Add(area.block) || !Array.Exists(plan.blocks, b => b.id == area.block))
                    throw new ArgumentException("CITY_AREA_INVALID");
        }

        /// <summary>Called explicitly before the navmesh build, independent of MonoBehaviour Awake ordering.</summary>
        public void PrepareAccess()
        {
            if (Access != null) return;
            accessPlan = JsonUtility.FromJson<DistrictPlan>(layoutJson); ValidateAreas(areas, accessPlan);
            var definitions = new List<AreaAccess.Area> { new AreaAccess.Area("street", 0) };
            var links = new List<AreaAccess.Link>(); var initial = new List<string> { "street" };
            foreach (var area in areas)
            {
                definitions.Add(new AreaAccess.Area(area.block, area.price)); links.Add(new AreaAccess.Link("street", area.block));
                if (area.price == 0) initial.Add(area.block);
            }
            Access = new AreaAccess(definitions, links, initial);
            HotelNav.DoorBoxes.Clear(); HotelNav.Unseal();
            for (int i = 0; i < areas.Length; i++)
            {
                var area = areas[i]; if (Access.IsOpen(area.block)) continue;
                var block = Array.Find(accessPlan.blocks, b => b.id == area.block);
                var room = GameObject.Find(block.id)?.GetComponent<CityRoom>();
                if (room == null) MakeGate(area, new Vector3(block.entrance.x, 0, block.entrance.z), true, 3.4f, 2.6f, HotelNav.FirstDoorArea + i);
                else foreach (var door in room.Plan.doors)
                {
                    bool horizontal = door.side == "front" || door.side == "back";
                    float sign = door.side == "front" || door.side == "right" ? 1 : -1;
                    var local = horizontal ? new Vector3(door.center, 0, sign * room.Plan.depth / 2) : new Vector3(sign * room.Plan.width / 2, 0, door.center);
                    MakeGate(area, room.transform.TransformPoint(local), horizontal, door.width, door.height + .24f, HotelNav.FirstDoorArea + i);
                }
            }
            Update();
        }

        void MakeGate(CityAreaDefinition area, Vector3 point, bool alongX, float width, float height, int navArea)
        {
            var opening = new Opening { alongX = alongX, line = Mathf.RoundToInt((alongX ? point.z : point.x) * 10),
                center = Mathf.RoundToInt((alongX ? point.x : point.z) * 10), width = Mathf.RoundToInt(width * 10), top = height };
            var block = Array.Find(accessPlan.blocks, b => b.id == area.block);
            float inward = Mathf.Sign(alongX ? block.building.z - point.z : block.building.x - point.x);
            var shutter = Shutter.Make(transform, opening, inward, gateMetal, gateTrim); shutter.SnapDown();
            var gate = shutter.gameObject.AddComponent<CityGate>(); gate.owner = this; gate.area = area; gate.shutter = shutter; gate.navArea = navArea;
            gates.Add(gate); Interact.Add(gate);
            HotelNav.DoorBoxes.Add(new HotelNav.DoorBox { center = point + Vector3.up, size = alongX ? new Vector3(width + .2f, 4, 1.6f) : new Vector3(1.6f, 4, width + .2f), area = navArea });
        }

        public string AreaAt(Vector3 feet)
        {
            foreach (var block in accessPlan.blocks)
                if (Mathf.Abs(feet.x - block.building.x) < block.width / 2 - .1f && Mathf.Abs(feet.z - block.building.z) < block.depth / 2 - .1f) return block.id;
            return "street";
        }

        public bool TryPurchase(CityGate gate)
        {
            var game = GameManager.I; var buyer = Targets.Local;
            if (gate == null || !gates.Contains(gate) || buyer == null || !buyer.Alive || buyer.LookAt == null) return false;
            Vector3 eye = buyer.LeanedPoint(Mathf.Min(1.6f, buyer.BodyHeight * .92f)); var to = gate.Point - eye;
            if (to.magnitude > gate.Radius || Vector3.Dot(buyer.LookAt.forward, to.normalized) < .6f || !Interact.Visible(eye, gate.Point, gate.transform)) return false;
            if (!Access.TryBuy(AreaAt(buyer.Feet), gate.area.block, game.score, out int cost)) return false;
            game.AddPoints(-cost);
            foreach (var other in gates) if (other.area.block == gate.area.block) other.shutter.Release();
            game.hud?.Toast(gate.area.title + " aperta");
            return true;
        }

        void Update()
        {
            if (Access == null) return;
            closedNavAreas.Clear();
            foreach (var gate in gates) if (!gate.shutter.IsUp) closedNavAreas.Add(gate.navArea);
            HotelNav.SealAreas(closedNavAreas);
        }

        void OnDestroy()
        {
            if (Access == null) return;
            HotelNav.DoorBoxes.Clear(); HotelNav.Unseal(); HotelNav.Clear();
        }
    }

    public sealed class CityGate : MonoBehaviour, IInteract
    {
        internal CityDirector owner;
        internal CityAreaDefinition area;
        internal Shutter shutter;
        internal int navArea;
        public string AreaId => area.block;
        public Shutter Shutter => shutter;
        public Vector3 Point => shutter.center + Vector3.up * 1.3f;
        public float Radius => 3f;
        public string Prompt => owner.Access.IsOpen(area.block) ? null : $"APRI {area.title.ToUpperInvariant()} - {owner.Access.Price(area.block)} PUNTI";
        public void Use()
        {
            if (!owner.TryPurchase(this)) { GameManager.I.hud?.Toast("Punti insufficienti o accesso non raggiungibile."); Sfx.UI(SoundBank.BookNo, .5f); }
        }
        void OnDestroy() => Interact.Remove(this);
    }
}
