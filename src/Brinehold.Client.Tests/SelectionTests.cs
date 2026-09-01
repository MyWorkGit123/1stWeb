using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Net.Client;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Client.Tests
{
    public class SelectionTests
    {
        private static EntityId FirstOwnWorker(ClientHarness h)
        {
            foreach (ReplicaWorld.Entity entity in h.Replica.Entities)
                if (entity.Owner == 0 && entity.Kind == EntityKind.Worker) return entity.Id;
            return EntityId.None;
        }

        [Fact]
        public void ClickingAWorkerSelectsIt()
        {
            var h = new ClientHarness();
            h.Tick(5);

            EntityId worker = FirstOwnWorker(h);
            Assert.False(worker.IsNone);

            h.Replica.TryGet(worker, out ReplicaWorld.Entity entity);
            EntityId picked = h.Selection.Pick(entity.State.Value.Position, Fix64.One);

            Assert.Equal(worker, picked);
            h.Selection.Set(picked);
            Assert.Equal(1, h.Selection.Count);
            Assert.True(h.Selection.IsCommandable(picked));
        }

        [Fact]
        public void BoxSelectionPicksUpTheWholeStartingCrew()
        {
            var h = new ClientHarness();
            h.Tick(5);

            var min = Fix2.FromInt(PrototypeMap.StartCellX[0] - 20, PrototypeMap.StartCellY[0] - 20);
            var max = Fix2.FromInt(PrototypeMap.StartCellX[0] + 20, PrototypeMap.StartCellY[0] + 20);

            var boxed = h.Selection.BoxSelect(min, max);
            h.Selection.SetMany(boxed);

            Assert.Equal(10, h.Selection.Count);
            Assert.All(h.Selection.Selected, id => Assert.True(h.Selection.IsCommandable(id)));
        }

        [Fact]
        public void BoxSelectionPrefersUnitsOverBuildings()
        {
            var h = new ClientHarness();
            h.Tick(5);

            // A box containing both the warehouse and the workers should yield only workers.
            var min = Fix2.FromInt(PrototypeMap.StartCellX[0] - 12, PrototypeMap.StartCellY[0] - 12);
            var max = Fix2.FromInt(PrototypeMap.StartCellX[0] + 12, PrototypeMap.StartCellY[0] + 12);

            var boxed = h.Selection.BoxSelect(min, max);
            foreach (EntityId id in boxed)
            {
                h.Replica.TryGet(id, out ReplicaWorld.Entity entity);
                Assert.NotEqual(EntityKind.Building, entity.Kind);
            }
        }

        [Fact]
        public void BoxSelectionIgnoresEnemyUnits()
        {
            var h = new ClientHarness();
            EntityId enemy = h.World.SpawnUnit(EntityKind.Soldier, 1,
                Fix2.FromInt(PrototypeMap.StartCellX[0], PrototypeMap.StartCellY[0] + 3));
            h.Tick(5);

            var min = Fix2.FromInt(PrototypeMap.StartCellX[0] - 20, PrototypeMap.StartCellY[0] - 20);
            var max = Fix2.FromInt(PrototypeMap.StartCellX[0] + 20, PrototypeMap.StartCellY[0] + 20);

            var boxed = h.Selection.BoxSelect(min, max);
            Assert.DoesNotContain(enemy, boxed);
        }

        [Fact]
        public void ShiftClickAddsAndRemoves()
        {
            var h = new ClientHarness();
            h.Tick(5);

            var workers = new System.Collections.Generic.List<EntityId>();
            foreach (ReplicaWorld.Entity e in h.Replica.Entities)
                if (e.Owner == 0 && e.Kind == EntityKind.Worker) workers.Add(e.Id);

            h.Selection.Set(workers[0]);
            h.Selection.Toggle(workers[1]);
            Assert.Equal(2, h.Selection.Count);

            h.Selection.Toggle(workers[1]);
            Assert.Equal(1, h.Selection.Count);
            Assert.True(h.Selection.Contains(workers[0]));
        }

        [Fact]
        public void EnemyUnitsCanBeInspectedButNotCommanded()
        {
            var h = new ClientHarness();
            EntityId enemy = h.World.SpawnUnit(EntityKind.Soldier, 1,
                Fix2.FromInt(PrototypeMap.StartCellX[0] + 2, PrototypeMap.StartCellY[0] + 2));
            h.Tick(5);

            Assert.True(h.Replica.Knows(enemy), "the enemy standing in our base should be visible");
            h.Selection.Set(enemy);

            Assert.Equal(1, h.Selection.Count);
            Assert.False(h.Selection.IsCommandable(enemy));
            Assert.Empty(h.Selection.CommandableSelection());
        }

        [Fact]
        public void SelectionDropsUnitsThatDie()
        {
            var h = new ClientHarness();
            h.Tick(5);

            EntityId worker = FirstOwnWorker(h);
            h.Selection.Set(worker);
            Assert.Equal(1, h.Selection.Count);

            h.World.Entities.Health[worker.Index] = Fix64.Zero;
            h.Tick(5);

            Assert.Equal(0, h.Selection.Count);
        }

        [Fact]
        public void IdleWorkerCyclingVisitsEveryIdleWorkerOnce()
        {
            var h = new ClientHarness();
            h.Tick(5);

            var seen = new System.Collections.Generic.HashSet<uint>();
            EntityId current = EntityId.None;
            for (int i = 0; i < 10; i++)
            {
                current = h.Selection.NextIdleWorker(current);
                Assert.False(current.IsNone);
                seen.Add(current.Raw);
            }

            Assert.Equal(10, seen.Count);
        }

        [Fact]
        public void DoubleClickSelectsSameKindInView()
        {
            var h = new ClientHarness();
            h.Tick(5);

            EntityId worker = FirstOwnWorker(h);
            var min = Fix2.FromInt(PrototypeMap.StartCellX[0] - 25, PrototypeMap.StartCellY[0] - 25);
            var max = Fix2.FromInt(PrototypeMap.StartCellX[0] + 25, PrototypeMap.StartCellY[0] + 25);

            var sameKind = h.Selection.SelectSameKindInRegion(worker, min, max);
            Assert.Equal(10, sameKind.Count);
        }
    }

    public class ControlGroupTests
    {
        [Fact]
        public void AssignAndRecall()
        {
            var h = new ClientHarness();
            h.Tick(5);

            var min = Fix2.FromInt(PrototypeMap.StartCellX[0] - 20, PrototypeMap.StartCellY[0] - 20);
            var max = Fix2.FromInt(PrototypeMap.StartCellX[0] + 20, PrototypeMap.StartCellY[0] + 20);
            h.Selection.SetMany(h.Selection.BoxSelect(min, max));

            h.Groups.Assign(1, h.Selection.Selected);
            h.Selection.Clear();
            Assert.Equal(0, h.Selection.Count);

            h.Selection.SetMany(h.Groups.Recall(1));
            Assert.Equal(10, h.Selection.Count);
        }

        [Fact]
        public void AppendAddsWithoutDuplicating()
        {
            var h = new ClientHarness();
            h.Tick(5);

            var workers = new System.Collections.Generic.List<EntityId>();
            foreach (ReplicaWorld.Entity e in h.Replica.Entities)
                if (e.Owner == 0 && e.Kind == EntityKind.Worker) workers.Add(e.Id);

            h.Groups.Assign(2, workers.Take(3).ToList());
            h.Groups.Append(2, workers.Take(5).ToList());

            Assert.Equal(5, h.Groups.Count(2));
        }

        [Fact]
        public void GroupsShrinkAsMembersDie()
        {
            var h = new ClientHarness();
            h.Tick(5);

            var workers = new System.Collections.Generic.List<EntityId>();
            foreach (ReplicaWorld.Entity e in h.Replica.Entities)
                if (e.Owner == 0 && e.Kind == EntityKind.Worker) workers.Add(e.Id);

            h.Groups.Assign(3, workers);
            Assert.Equal(10, h.Groups.Count(3));

            h.World.Entities.Health[workers[0].Index] = Fix64.Zero;
            h.World.Entities.Health[workers[1].Index] = Fix64.Zero;
            h.Tick(5);

            Assert.Equal(8, h.Groups.Count(3));
        }

        [Fact]
        public void AllTenGroupsAreIndependent()
        {
            var h = new ClientHarness();
            h.Tick(5);

            var workers = new System.Collections.Generic.List<EntityId>();
            foreach (ReplicaWorld.Entity e in h.Replica.Entities)
                if (e.Owner == 0 && e.Kind == EntityKind.Worker) workers.Add(e.Id);

            for (int g = 0; g < 10; g++)
                h.Groups.Assign(g, new[] { workers[g] });

            for (int g = 0; g < 10; g++)
            {
                Assert.Equal(1, h.Groups.Count(g));
                Assert.Equal(workers[g], h.Groups.Group(g)[0]);
            }
        }
    }
}
