using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Sim.Tests
{
    /// <summary>
    /// The simulation half of the anti-cheat design.
    ///
    /// Every one of these tests submits a command that a modified client could plausibly send, and
    /// asserts that the world is unchanged. This is the harness the prototype's cheat-client mode
    /// drives over the wire (TESTING.md section 7.3); running the same cases here keeps them cheap
    /// enough to be a per-commit regression net.
    /// </summary>
    public class AuthorityTests
    {
        [Fact]
        public void APlayerCannotMoveAnotherPlayersUnits()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId enemyWorker = SimFixture.UnitsOf(world, 1, EntityKind.Worker)[0];
            Fix2 before = world.Entities.Position[enemyWorker.Index];

            // Player 0 orders player 1's worker around.
            world.EnqueueCommand(Command.Move(0, 1, new[] { enemyWorker }, 60, 60));
            world.Step(60);

            Assert.Equal(before, world.Entities.Position[enemyWorker.Index]);
            Assert.Equal(JobType.Idle, world.Entities.Job[enemyWorker.Index]);
        }

        [Fact]
        public void APlayerCannotHarvestWithAnotherPlayersWorkers()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId enemyWorker = SimFixture.UnitsOf(world, 1, EntityKind.Worker)[0];
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[enemyWorker.Index], ResourceNodeType.Forest);
            int woodBefore = world.Players[0].Wood;

            world.EnqueueCommand(Command.Harvest(0, 1, new[] { enemyWorker }, forest));
            world.Step(600);

            Assert.Equal(woodBefore, world.Players[0].Wood);
        }

        [Fact]
        public void APlayerCannotTrainAtAnotherPlayersBuilding()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId enemyCore = SimFixture.FirstBuilding(world, 1, BuildingType.Warehouse);
            world.Players[0].PopulationCap = 50;
            int foodBefore = world.Players[0].Food;

            world.EnqueueCommand(Command.Train(0, 1, enemyCore, EntityKind.Worker));
            world.Step();

            Assert.Equal(foodBefore, world.Players[0].Food);
            Assert.Equal(0, world.Entities.TrainingQueued[enemyCore.Index]);
            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
        }

        [Fact]
        public void APlayerCannotAttackTheirOwnUnits()
        {
            SimWorld world = SimFixture.NewMatch();
            var workers = SimFixture.UnitsOf(world, 0, EntityKind.Worker);
            EntityId soldier = world.SpawnUnit(EntityKind.Soldier, 0, world.Entities.Position[workers[0].Index]);

            world.EnqueueCommand(Command.Attack(0, 1, new[] { soldier }, workers[1]));
            world.Step(200);

            Assert.True(world.Entities.IsAlive(workers[1]));
            Assert.Equal(world.Entities.MaxHealth[workers[1].Index], world.Entities.Health[workers[1].Index]);
        }

        [Fact]
        public void ResourceNodesCannotBeAttacked()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId soldier = world.SpawnUnit(EntityKind.Soldier, 0,
                world.Nav.CellCentre(world.Nav.Index(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] + 6)));
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[soldier.Index], ResourceNodeType.Forest);

            world.EnqueueCommand(Command.Attack(0, 1, new[] { soldier }, forest));
            world.Step();

            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
            Assert.True(world.Entities.IsAlive(forest));
        }

        [Fact]
        public void AStaleEntityIdIsRejectedEvenAfterItsSlotIsReused()
        {
            SimWorld world = SimFixture.NewMatch();
            var workers = SimFixture.UnitsOf(world, 0, EntityKind.Worker);
            EntityId doomed = workers[5];

            world.Entities.Health[doomed.Index] = Fix64.Zero;
            world.Step();
            Assert.False(world.Entities.IsAlive(doomed));

            // Recycle the slot. The new entity has the same index but a new generation.
            EntityId recycled = world.SpawnUnit(EntityKind.Soldier, 0, Fix2.FromInt(50, 50));
            Assert.Equal(doomed.Index, recycled.Index);
            Assert.NotEqual(doomed.Generation, recycled.Generation);

            Fix2 before = world.Entities.Position[recycled.Index];
            world.EnqueueCommand(Command.Move(0, 1, new[] { doomed }, 70, 70));
            world.Step();

            Assert.Equal(before, world.Entities.Position[recycled.Index]);
            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
        }

        [Fact]
        public void GarbageEntityIdsAreRejectedWithoutCrashing()
        {
            SimWorld world = SimFixture.NewMatch();
            var bogus = new[]
            {
                new EntityId(0xFFFFFF, 200),
                new EntityId(123456, 7),
                EntityId.None
            };

            world.EnqueueCommand(Command.Move(0, 1, bogus, 50, 50));
            world.EnqueueCommand(Command.Attack(0, 2, bogus, new EntityId(999999, 3)));
            world.EnqueueCommand(Command.Harvest(0, 3, bogus, new EntityId(888888, 1)));
            world.Step(5);

            Assert.True(SimFixture.CountEvents(world, SimEventType.CommandRejected) >= 0);
            Assert.False(world.MatchOver);
        }

        [Fact]
        public void OutOfBoundsMoveTargetsAreRejected()
        {
            SimWorld world = SimFixture.NewMatch();
            var worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            Fix2 before = world.Entities.Position[worker.Index];

            world.EnqueueCommand(Command.Move(0, 1, new[] { worker }, -50, 99999));
            // Events are per-tick, so the rejection has to be read on the tick it happens.
            world.Step();
            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));

            world.Step(20);
            Assert.Equal(before, world.Entities.Position[worker.Index]);
        }

        [Fact]
        public void AnOversizedSelectionIsRejectedWholesale()
        {
            SimWorld world = SimFixture.NewMatch();
            var huge = new EntityId[Command.MaxEntities + 1];
            var workers = SimFixture.UnitsOf(world, 0, EntityKind.Worker);
            for (int i = 0; i < huge.Length; i++) huge[i] = workers[i % workers.Count];

            Fix2 before = world.Entities.Position[workers[0].Index];
            world.EnqueueCommand(Command.Move(0, 1, huge, 60, 60));
            world.Step();
            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));

            world.Step(20);
            Assert.Equal(before, world.Entities.Position[workers[0].Index]);
        }

        [Fact]
        public void AnUnknownPlayerIdIsRejected()
        {
            SimWorld world = SimFixture.NewMatch();
            var worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            Fix2 before = world.Entities.Position[worker.Index];

            world.EnqueueCommand(Command.Move(200, 1, new[] { worker }, 60, 60));
            world.Step(20);

            Assert.Equal(before, world.Entities.Position[worker.Index]);
        }

        [Fact]
        public void ADefeatedPlayerCannotIssueCommands()
        {
            SimWorld world = SimFixture.NewMatch();
            world.Players[0].Defeated = true;
            var worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            Fix2 before = world.Entities.Position[worker.Index];

            world.EnqueueCommand(Command.Move(0, 1, new[] { worker }, 60, 60));
            world.Step(20);

            Assert.Equal(before, world.Entities.Position[worker.Index]);
        }

        [Fact]
        public void BuildingOnAResourceNodeIsRejected()
        {
            SimWorld world = SimFixture.NewMatch();
            var builders = SimFixture.UnitsOf(world, 0, EntityKind.Worker).Take(2).ToArray();
            EntityId forest = PrototypeMap.FindNearestNode(world, world.Entities.Position[builders[0].Index], ResourceNodeType.Forest);
            int fx = world.Entities.Position[forest.Index].X.ToInt();
            int fy = world.Entities.Position[forest.Index].Y.ToInt();
            int woodBefore = world.Players[0].Wood;

            world.EnqueueCommand(Command.Build(0, 1, builders, BuildingType.House, fx, fy));
            world.Step();

            Assert.Equal(woodBefore, world.Players[0].Wood);
            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
        }
    }

    public class FogTests
    {
        [Fact]
        public void PlayersCannotSeeTheOpponentsStartingArea()
        {
            SimWorld world = SimFixture.NewMatch();
            world.Step();

            int enemyCore = world.Nav.Index(PrototypeMap.StartCellX[1], PrototypeMap.StartCellY[1]);
            int ownCore = world.Nav.Index(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0]);

            Assert.True(world.Fog.IsVisible(0, ownCore));
            Assert.False(world.Fog.IsVisible(0, enemyCore));
            Assert.False(world.Fog.IsExplored(0, enemyCore));
        }

        [Fact]
        public void MovingAUnitRevealsTerrainAndItStaysExplored()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId scout = world.SpawnUnit(EntityKind.Soldier, 0,
                world.Nav.CellCentre(world.Nav.Index(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] + 6)));

            int target = world.Nav.Index(PrototypeMap.StartCellX[0] + 30, PrototypeMap.StartCellY[0] + 6);
            Assert.False(world.Fog.IsExplored(0, target));

            world.EnqueueCommand(Command.Move(0, 1, new[] { scout },
                world.Nav.CellX(target), world.Nav.CellY(target)));
            bool reached = SimFixture.StepUntil(world, w => w.Fog.IsVisible(0, target), 1200);
            Assert.True(reached, "the scout never revealed the target cell");

            // Walk away again: the cell stays explored but stops being visible.
            world.EnqueueCommand(Command.Move(0, 2, new[] { scout },
                PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] + 6));
            SimFixture.StepUntil(world, w => !w.Fog.IsVisible(0, target), 1200);

            Assert.False(world.Fog.IsVisible(0, target));
            Assert.True(world.Fog.IsExplored(0, target));
        }

        [Fact]
        public void VisionIsIndependentPerPlayer()
        {
            SimWorld world = SimFixture.NewMatch();
            world.Step();
            int ownCore = world.Nav.Index(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0]);

            Assert.True(world.Fog.IsVisible(0, ownCore));
            Assert.False(world.Fog.IsVisible(1, ownCore));
        }

        [Fact]
        public void DeadUnitsStopProvidingVision()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId scout = world.SpawnUnit(EntityKind.Soldier, 0, Fix2.FromInt(90, 100));
            world.Step();
            int cell = world.Nav.Index(90, 100);
            Assert.True(world.Fog.IsVisible(0, cell));

            world.Entities.Health[scout.Index] = Fix64.Zero;
            world.Step(2);

            Assert.False(world.Fog.IsVisible(0, cell));
            Assert.True(world.Fog.IsExplored(0, cell));
        }
    }
}
