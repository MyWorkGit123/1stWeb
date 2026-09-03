using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Content;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Sim.Tests
{
    public class CombatTests
    {
        [Fact]
        public void ASoldierKillsAnEnemyWorker()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId victim = SimFixture.UnitsOf(world, 1, EntityKind.Worker)[0];
            Fix2 near = world.Entities.Position[victim.Index] + new Fix2(Fix64.FromInt(3), Fix64.Zero);
            EntityId soldier = world.SpawnUnit(EntityKind.Soldier, 0, near);

            world.EnqueueCommand(Command.Attack(0, 1, new[] { soldier }, victim));
            bool killed = SimFixture.StepUntil(world, w => !w.Entities.IsAlive(victim), 600);

            Assert.True(killed, "the worker survived a sustained attack");
            Assert.True(world.Entities.IsAlive(soldier));
        }

        [Fact]
        public void DamageIsDealtAtTheCooldownRateNotEveryTick()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId victim = SimFixture.UnitsOf(world, 1, EntityKind.Worker)[0];
            Fix2 near = world.Entities.Position[victim.Index] + new Fix2(Fix64.One, Fix64.Zero);
            EntityId soldier = world.SpawnUnit(EntityKind.Soldier, 0, near);

            world.EnqueueCommand(Command.Attack(0, 1, new[] { soldier }, victim));

            int hits = 0;
            for (int i = 0; i < 100; i++)
            {
                world.Step();
                hits += SimFixture.CountEvents(world, SimEventType.DamageApplied);
            }

            // 100 ticks at a 20-tick cooldown is at most six swings, allowing for the approach.
            Assert.InRange(hits, 1, 6);
        }

        [Fact]
        public void IdleSoldiersDefendThemselvesAgainstNearbyEnemies()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId enemyWorker = SimFixture.UnitsOf(world, 1, EntityKind.Worker)[0];
            Fix2 near = world.Entities.Position[enemyWorker.Index] + new Fix2(Fix64.FromInt(5), Fix64.Zero);
            EntityId soldier = world.SpawnUnit(EntityKind.Soldier, 0, near);

            // No order at all: the auto-acquire path must engage.
            bool engaged = SimFixture.StepUntil(world,
                w => w.Entities.Job[soldier.Index] == JobType.Attacking || !w.Entities.IsAlive(enemyWorker), 600);

            Assert.True(engaged, "an idle soldier ignored an enemy standing next to it");
        }

        [Fact]
        public void WorkersDoNotAutoAttack()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId enemyWorker = SimFixture.UnitsOf(world, 1, EntityKind.Worker)[0];
            Fix2 near = world.Entities.Position[enemyWorker.Index] + new Fix2(Fix64.FromInt(2), Fix64.Zero);
            EntityId ourWorker = world.SpawnUnit(EntityKind.Worker, 0, near);

            world.Step(200);

            Assert.Equal(JobType.Idle, world.Entities.Job[ourWorker.Index]);
            Assert.True(world.Entities.IsAlive(enemyWorker));
        }

        [Fact]
        public void BuildingsCanBeDestroyedBySoldiers()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId core = SimFixture.FirstBuilding(world, 1, BuildingType.Warehouse);
            Fix2 near = world.Entities.Position[core.Index] + new Fix2(Fix64.FromInt(4), Fix64.Zero);

            var soldiers = new EntityId[6];
            for (int i = 0; i < soldiers.Length; i++)
                soldiers[i] = world.SpawnUnit(EntityKind.Soldier, 0, near + new Fix2(Fix64.FromInt(i), Fix64.Zero));

            world.EnqueueCommand(Command.Attack(0, 1, soldiers, core));
            bool destroyed = SimFixture.StepUntil(world, w => !w.Entities.IsAlive(core), 4000);

            Assert.True(destroyed, "six soldiers could not destroy a warehouse in 200 seconds");
        }

        [Fact]
        public void ShipsCannotChaseOntoLand()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId landTarget = SimFixture.UnitsOf(world, 1, EntityKind.Worker)[0];
            EntityId ship = world.SpawnUnit(EntityKind.Ship, 0,
                world.Nav.CellCentre(world.Nav.Index(PrototypeMap.StartCellX[1], 8)));

            world.EnqueueCommand(Command.Attack(0, 1, new[] { ship }, landTarget));
            world.Step(600);

            int cell = world.Nav.CellAt(world.Entities.Position[ship.Index]);
            Assert.Equal(TerrainType.Water, world.Nav.TerrainAt(cell));
        }

        [Fact]
        public void DeathReleasesPopulationAndFootprint()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId core = SimFixture.FirstBuilding(world, 0, BuildingType.Warehouse);
            int cell = world.Nav.CellAt(world.Entities.Position[core.Index]);
            Assert.True(world.Nav.IsOccupied(cell));

            int capBefore = world.Players[0].PopulationCap;
            world.Entities.Health[core.Index] = Fix64.Zero;
            world.Step();

            Assert.False(world.Nav.IsOccupied(cell));
            Assert.Equal(capBefore - 5, world.Players[0].PopulationCap);
        }
    }

    public class VictoryTests
    {
        [Fact]
        public void LosingTheLastCoreDefeatsThePlayerAndEndsTheMatch()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId core = SimFixture.FirstBuilding(world, 1, BuildingType.Warehouse);

            world.Entities.Health[core.Index] = Fix64.Zero;
            world.Step(2);

            Assert.True(world.Players[1].Defeated);
            Assert.True(world.Players[0].Victorious);
            Assert.True(world.MatchOver);
            Assert.Equal(world.Players[0].Team, world.WinningTeam);
        }

        [Fact]
        public void TheMatchIsNotOverWhileBothCoresStand()
        {
            SimWorld world = SimFixture.NewMatch();
            world.Step(400);
            Assert.False(world.MatchOver);
            Assert.False(world.Players[0].Defeated);
            Assert.False(world.Players[1].Defeated);
        }

        [Fact]
        public void CommandsAreRefusedOnceTheMatchIsOver()
        {
            SimWorld world = SimFixture.NewMatch();
            EntityId core = SimFixture.FirstBuilding(world, 1, BuildingType.Warehouse);
            world.Entities.Health[core.Index] = Fix64.Zero;
            world.Step(2);
            Assert.True(world.MatchOver);

            var worker = SimFixture.UnitsOf(world, 0, EntityKind.Worker)[0];
            world.EnqueueCommand(Command.Move(0, 99, new[] { worker }, 50, 50));
            world.Step();

            Assert.Equal(1, SimFixture.CountEvents(world, SimEventType.CommandRejected));
        }
    }
}
