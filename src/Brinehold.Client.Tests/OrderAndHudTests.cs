using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Net.Client;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Client.Tests
{
    public class OrderIssuerTests
    {
        private static EntityId FirstOwnWorker(ClientHarness h)
        {
            foreach (ReplicaWorld.Entity e in h.Replica.Entities)
                if (e.Owner == 0 && e.Kind == EntityKind.Worker) return e.Id;
            return EntityId.None;
        }

        [Fact]
        public void RightClickingGroundIsAMoveOrder()
        {
            var h = new ClientHarness();
            h.Tick(5);
            h.Selection.Set(FirstOwnWorker(h));

            // Open ground north of the base, clear of the forest stands and stone outcrops.
            var point = Fix2.FromInt(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] - 12);
            Command? order = h.Orders.RightClick(point, h.ClientNav);

            Assert.NotNull(order);
            Assert.Equal(CommandType.Move, order!.Type);
            Assert.Equal(PrototypeMap.StartCellX[0], order.TargetCellX);
            Assert.Equal(PrototypeMap.StartCellY[0] - 12, order.TargetCellY);
        }

        [Fact]
        public void RightClickingAForestIsAHarvestOrder()
        {
            var h = new ClientHarness();
            h.Tick(5);
            h.Selection.Set(FirstOwnWorker(h));

            EntityId forest = EntityId.None;
            Fix2 forestPoint = Fix2.Zero;
            foreach (ReplicaWorld.Entity e in h.Replica.Entities)
            {
                if (e.Kind != EntityKind.ResourceNode || e.Node != ResourceNodeType.Forest) continue;
                forest = e.Id;
                forestPoint = e.State.Value.Position;
                break;
            }
            Assert.False(forest.IsNone, "no forest was visible to the client");

            Command? order = h.Orders.RightClick(forestPoint, h.ClientNav);

            Assert.NotNull(order);
            Assert.Equal(CommandType.Harvest, order!.Type);
            Assert.Equal(forest, order.TargetEntity);
        }

        [Fact]
        public void RightClickingAnEnemyIsAnAttackOrderForSoldiers()
        {
            var h = new ClientHarness();
            Fix2 spot = Fix2.FromInt(PrototypeMap.StartCellX[0] + 4, PrototypeMap.StartCellY[0] + 4);
            EntityId soldier = h.World.SpawnUnit(EntityKind.Soldier, 0, spot);
            EntityId enemy = h.World.SpawnUnit(EntityKind.Soldier, 1, spot + new Fix2(Fix64.FromInt(3), Fix64.Zero));
            h.Tick(5);

            h.Selection.Set(soldier);
            h.Replica.TryGet(enemy, out ReplicaWorld.Entity enemyView);
            Command? order = h.Orders.RightClick(enemyView.State.Value.Position, h.ClientNav);

            Assert.NotNull(order);
            Assert.Equal(CommandType.Attack, order!.Type);
            Assert.Equal(enemy, order.TargetEntity);
        }

        [Fact]
        public void WorkersAloneDoNotProduceAnAttackOrder()
        {
            var h = new ClientHarness();
            Fix2 spot = Fix2.FromInt(PrototypeMap.StartCellX[0] + 4, PrototypeMap.StartCellY[0] + 4);
            EntityId enemy = h.World.SpawnUnit(EntityKind.Soldier, 1, spot);
            h.Tick(5);

            h.Selection.Set(FirstOwnWorker(h));
            h.Replica.TryGet(enemy, out ReplicaWorld.Entity enemyView);
            Command? order = h.Orders.RightClick(enemyView.State.Value.Position, h.ClientNav);

            // Workers walk toward the enemy rather than attacking it.
            Assert.NotNull(order);
            Assert.Equal(CommandType.Move, order!.Type);
        }

        [Fact]
        public void AnEmptySelectionProducesNoOrder()
        {
            var h = new ClientHarness();
            h.Tick(5);
            h.Selection.Clear();

            Assert.Null(h.Orders.RightClick(Fix2.FromInt(50, 50), h.ClientNav));
        }

        [Fact]
        public void SelectingOnlyEnemyUnitsProducesNoOrder()
        {
            var h = new ClientHarness();
            EntityId enemy = h.World.SpawnUnit(EntityKind.Soldier, 1,
                Fix2.FromInt(PrototypeMap.StartCellX[0] + 3, PrototypeMap.StartCellY[0] + 3));
            h.Tick(5);

            h.Selection.Set(enemy);
            Assert.Null(h.Orders.RightClick(Fix2.FromInt(50, 50), h.ClientNav));
        }

        [Fact]
        public void OrdersIssuedByTheClientAreExecutedByTheServer()
        {
            var h = new ClientHarness();
            h.Tick(5);

            EntityId worker = FirstOwnWorker(h);
            h.Selection.Set(worker);
            Fix2 before = h.World.Entities.Position[worker.Index];

            var destination = Fix2.FromInt(PrototypeMap.StartCellX[0] + 15, PrototypeMap.StartCellY[0] + 15);
            Command? order = h.Orders.RightClick(destination, h.ClientNav);
            Assert.NotNull(order);
            h.Connection.Send(order!);

            h.Tick(400);

            double moved = Fix2.Distance(before, h.World.Entities.Position[worker.Index]).ToDouble();
            Assert.True(moved > 5, $"the worker only moved {moved:0.0} m; the client's order never took effect");
        }

        [Fact]
        public void TrainOrdersFindABuildingThatCanTrainTheUnit()
        {
            var h = new ClientHarness();
            h.Tick(5);

            EntityId core = EntityId.None;
            foreach (ReplicaWorld.Entity e in h.Replica.Entities)
                if (e.Owner == 0 && e.Building == BuildingType.Warehouse) core = e.Id;
            Assert.False(core.IsNone);

            h.Selection.Set(core);
            Command? order = h.Orders.Train(EntityKind.Worker);

            Assert.NotNull(order);
            Assert.Equal(CommandType.Train, order!.Type);
            Assert.Equal(EntityKind.Worker, order.TrainKind);

            // A warehouse cannot build ships, so that request finds nothing.
            Assert.Null(h.Orders.Train(EntityKind.Ship));
        }
    }

    public class HudTests
    {
        [Fact]
        public void TheHudMirrorsTheServersEconomyExactly()
        {
            var h = new ClientHarness();
            h.Tick(30);

            Assert.Equal(h.World.Players[0].Wood, h.Hud.Wood);
            Assert.Equal(h.World.Players[0].Food, h.Hud.Food);
            Assert.Equal(h.World.Players[0].Stone, h.Hud.Stone);
            Assert.Equal(h.World.Players[0].Coin, h.Hud.Coin);
        }

        [Fact]
        public void ThePopulationReadoutShowsTheOpeningIsCapped()
        {
            var h = new ClientHarness();
            h.Tick(30);

            Assert.Equal(10, h.Hud.PopulationUsed);
            Assert.Equal(10, h.Hud.PopulationCap);
            Assert.True(h.Hud.PopulationBlocked);
            Assert.Equal("Build more housing first", h.Hud.TrainBlockedReason(EntityKind.Worker));
        }

        [Fact]
        public void AffordabilityMatchesTheContentCosts()
        {
            var h = new ClientHarness();
            h.Tick(30);

            Assert.True(h.Hud.CanAfford(BuildingType.House));      // 50 wood against 200
            Assert.Null(h.Hud.BuildBlockedReason(BuildingType.House));

            h.World.Players[0].Wood = 10;
            h.Tick(30);
            Assert.False(h.Hud.CanAfford(BuildingType.House));
            Assert.Equal("Need 40 more wood", h.Hud.BuildBlockedReason(BuildingType.House));
        }

        [Fact]
        public void UnitCountsAndIdleWorkersAreReported()
        {
            var h = new ClientHarness();
            h.Tick(10);

            h.Hud.CountOwnUnits(out int workers, out int soldiers, out int ships, out int idle);
            Assert.Equal(10, workers);
            Assert.Equal(0, soldiers);
            Assert.Equal(0, ships);
            Assert.Equal(10, idle);
        }

        [Fact]
        public void TheMatchClockCountsFromTheAuthoritativeTick()
        {
            var h = new ClientHarness();
            h.Tick(20 * 65);   // 65 seconds
            Assert.Equal("01:05", h.Hud.MatchClock());
        }

        [Fact]
        public void SelectionDescriptionsReadNaturally()
        {
            var h = new ClientHarness();
            h.Tick(10);

            EntityId worker = EntityId.None;
            foreach (ReplicaWorld.Entity e in h.Replica.Entities)
                if (e.Owner == 0 && e.Kind == EntityKind.Worker) { worker = e.Id; break; }

            Assert.Equal("Deckhand (Yours) — idle", h.Hud.Describe(worker));
        }
    }

    public class CameraTests
    {
        [Fact]
        public void PanningMovesTheFocus()
        {
            var camera = new Brinehold.Client.CameraControl.CameraRig(160, 160);
            Fix2 before = camera.Focus;
            camera.Pan(Fix2.UnitX, Fix64.FromFraction(1, 10));
            Assert.True(camera.Focus.X > before.X);
        }

        [Fact]
        public void TheFocusCannotLeaveTheMap()
        {
            var camera = new Brinehold.Client.CameraControl.CameraRig(160, 160);
            for (int i = 0; i < 500; i++) camera.Pan(Fix2.UnitX, Fix64.One);
            Assert.True(camera.Focus.X.ToInt() <= 172, $"camera ran off the map at x={camera.Focus.X}");

            for (int i = 0; i < 1000; i++) camera.Pan(-Fix2.UnitX, Fix64.One);
            Assert.True(camera.Focus.X.ToInt() >= -13, $"camera ran off the map at x={camera.Focus.X}");
        }

        [Fact]
        public void ZoomIsClampedAndChangesHeight()
        {
            var camera = new Brinehold.Client.CameraControl.CameraRig(160, 160);
            Fix64 midHeight = camera.Height;

            camera.AddZoom(Fix64.FromInt(5));
            Assert.Equal(Fix64.One, camera.Zoom);
            Assert.True(camera.Height > midHeight);

            camera.AddZoom(Fix64.FromInt(-5));
            Assert.Equal(Fix64.Zero, camera.Zoom);
            Assert.Equal(camera.MinHeight, camera.Height);
        }

        [Fact]
        public void PanSpeedRisesWithZoom()
        {
            var camera = new Brinehold.Client.CameraControl.CameraRig(160, 160);
            camera.AddZoom(Fix64.FromInt(-5));
            Fix64 slow = camera.PanSpeed;
            camera.AddZoom(Fix64.FromInt(5));
            Assert.True(camera.PanSpeed > slow);
        }

        [Fact]
        public void RotatingChangesThePanDirection()
        {
            var straight = new Brinehold.Client.CameraControl.CameraRig(160, 160);
            var rotated = new Brinehold.Client.CameraControl.CameraRig(160, 160);
            rotated.Rotate(Fix64.HalfPi);

            straight.Pan(Fix2.UnitX, Fix64.One);
            rotated.Pan(Fix2.UnitX, Fix64.One);

            Assert.NotEqual(straight.Focus, rotated.Focus);
        }

        [Fact]
        public void JumpToSnapsAndClamps()
        {
            var camera = new Brinehold.Client.CameraControl.CameraRig(160, 160);
            camera.JumpTo(Fix2.FromInt(40, 60));
            Assert.Equal(40, camera.Focus.X.ToInt());

            camera.JumpTo(Fix2.FromInt(9999, 9999));
            Assert.True(camera.Focus.X.ToInt() <= 172);
        }
    }

    public class PlacementTests
    {
        [Fact]
        public void AHouseOnOpenGroundIsLegal()
        {
            var h = new ClientHarness();
            h.Tick(5);

            h.Placement.Begin(BuildingType.House);
            h.Placement.MoveTo(Fix2.FromInt(PrototypeMap.StartCellX[0] + 12, PrototypeMap.StartCellY[0] - 8));

            Assert.True(h.Placement.Legal, h.Placement.Reason ?? "unexpectedly illegal");
        }

        [Fact]
        public void TheGhostRefusesWaterAndSaysWhy()
        {
            var h = new ClientHarness();
            h.Tick(5);

            h.Placement.Begin(BuildingType.House);
            h.Placement.MoveTo(Fix2.FromInt(60, 5));

            Assert.False(h.Placement.Legal);
            Assert.Equal("Cannot build on water", h.Placement.Reason);
        }

        [Fact]
        public void TheGhostRefusesRock()
        {
            var h = new ClientHarness();
            h.Tick(5);

            h.Placement.Begin(BuildingType.House);
            h.Placement.MoveTo(Fix2.FromInt(80, 40));   // the central ridge

            Assert.False(h.Placement.Legal);
            Assert.Equal("Cannot build on rock", h.Placement.Reason);
        }

        [Fact]
        public void ADockIsRefusedInlandAndAllowedOnTheShore()
        {
            var h = new ClientHarness();
            h.Tick(5);

            h.Placement.Begin(BuildingType.Dock);
            h.Placement.MoveTo(Fix2.FromInt(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] - 8));
            Assert.False(h.Placement.Legal);
            Assert.Equal("Must be built on the shore", h.Placement.Reason);

            h.Placement.MoveTo(Fix2.FromInt(PrototypeMap.StartCellX[0], PrototypeMap.SeaLine + 2));
            Assert.True(h.Placement.Legal, h.Placement.Reason ?? "unexpectedly illegal");
        }

        [Fact]
        public void TheGhostAgreesWithTheServerVerdict()
        {
            var h = new ClientHarness();
            h.Tick(5);

            // Somewhere the client thinks is fine.
            h.Placement.Begin(BuildingType.House);
            h.Placement.MoveTo(Fix2.FromInt(PrototypeMap.StartCellX[0] + 12, PrototypeMap.StartCellY[0] - 8));
            Assert.True(h.Placement.Legal);

            var min = Fix2.FromInt(PrototypeMap.StartCellX[0] - 20, PrototypeMap.StartCellY[0] - 20);
            var max = Fix2.FromInt(PrototypeMap.StartCellX[0] + 20, PrototypeMap.StartCellY[0] + 20);
            h.Selection.SetMany(h.Selection.BoxSelect(min, max));

            Command? order = h.Orders.PlaceBuilding(BuildingType.House, h.Placement.CellX, h.Placement.CellY);
            Assert.NotNull(order);
            h.Connection.Send(order!);
            h.Tick(10);

            // The server agreed: no rejection came back, and a site exists.
            Assert.Empty(h.Replica.Rejections);
            bool siteExists = false;
            for (int i = 1; i < h.World.Entities.Count; i++)
                if (h.World.Entities.Alive[i] && h.World.Entities.Building[i] == BuildingType.House) siteExists = true;
            Assert.True(siteExists, "the server did not create the site the ghost said was legal");
        }
    }
}
