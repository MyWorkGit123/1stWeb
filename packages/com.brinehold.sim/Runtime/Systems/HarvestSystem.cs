using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// The harvest-and-haul loop: walk to a node, extract until full, carry the load to a drop-off,
    /// deposit it, walk back.
    ///
    /// Writes: Job, JobTimer, CarriedAmount/Type, NodeRemaining, player resources, paths.
    ///
    /// Goods are physical here: a worker holds a load, and the player's resource count only rises
    /// at the moment of deposit. That is what makes haul distance a real cost and a destroyed
    /// warehouse a real disruption rather than a cosmetic one.
    /// </summary>
    public sealed class HarvestSystem : ISimSystem
    {
        public void Execute(SimWorld world)
        {
            EntityStore store = world.Entities;
            int count = store.Count;

            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                if (store.Kind[i] != EntityKind.Worker) continue;

                switch (store.Job[i])
                {
                    case JobType.MoveToHarvest: TickTravellingToNode(world, store, i); break;
                    case JobType.Harvesting: TickHarvesting(world, store, i); break;
                    case JobType.Delivering: TickDelivering(world, store, i); break;
                }
            }
        }

        private static void TickTravellingToNode(SimWorld world, EntityStore store, int i)
        {
            EntityId node = store.JobTarget[i];
            if (!store.IsAlive(node) || store.NodeRemaining[node.Index] <= 0)
            {
                GoIdle(world, store, i);
                return;
            }

            if (SimRange.InReach(store, i, node.Index, SimRange.InteractionReach))
            {
                store.ClearPath(i);
                world.SetJobIfChanged(i, JobType.Harvesting, store.Position[i], node);
                store.JobTimer[i] = PrototypeContent.HarvestTicksPerUnit;
                return;
            }

            // Arrived where the path ended but still out of reach: the node is unreachable.
            if (!store.HasPath(i)) GoIdle(world, store, i);
        }

        private static void TickHarvesting(SimWorld world, EntityStore store, int i)
        {
            EntityId node = store.JobTarget[i];
            if (!store.IsAlive(node) || store.NodeRemaining[node.Index] <= 0)
            {
                // The node ran dry mid-swing. Deliver whatever is already carried.
                if (store.CarriedAmount[i] > 0) BeginDelivery(world, store, i);
                else GoIdle(world, store, i);
                return;
            }

            if (--store.JobTimer[i] > 0) return;
            store.JobTimer[i] = PrototypeContent.HarvestTicksPerUnit;

            int n = node.Index;
            ResourceType resource = store.NodeResource[n];

            // Switching resource while carrying would silently transmute goods; drop the old load first.
            if (store.CarriedAmount[i] > 0 && store.CarriedType[i] != resource)
            {
                BeginDelivery(world, store, i);
                return;
            }

            store.NodeRemaining[n]--;
            store.CarriedType[i] = resource;
            store.CarriedAmount[i]++;
            store.HomeNode[i] = node;

            if (store.NodeRemaining[n] <= 0)
            {
                world.Events.Add(new SimEvent
                {
                    Type = SimEventType.ResourceNodeDepleted,
                    Entity = node,
                    Position = store.Position[n],
                    ValueA = (int)resource
                });
                store.Destroy(node);
            }

            int capacity = PrototypeContent.Worker.CarryCapacity;
            if (store.CarriedAmount[i] >= capacity) BeginDelivery(world, store, i);
        }

        private static void TickDelivering(SimWorld world, EntityStore store, int i)
        {
            EntityId dropOff = store.JobTarget[i];
            if (!store.IsAlive(dropOff) || store.UnderConstruction[dropOff.Index])
            {
                // The drop-off was destroyed or is not finished. Find another; if none exists the
                // worker is stranded holding its load, which is the intended consequence of losing
                // your warehouse.
                EntityId replacement = FindDropOff(world, store, i);
                if (replacement.IsNone)
                {
                    store.ClearPath(i);
                    world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
                    return;
                }
                world.SetJob(i, JobType.Delivering, store.Position[replacement.Index], replacement);
                world.RequestPath(i, store.Position[replacement.Index]);
                return;
            }

            if (!SimRange.InReach(store, i, dropOff.Index, SimRange.InteractionReach))
            {
                if (!store.HasPath(i))
                {
                    // Out of reach with no path left: try once more, then give up this tick.
                    if (!world.RequestPath(i, store.Position[dropOff.Index]))
                        world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
                }
                return;
            }

            byte owner = store.Owner[i];
            int amount = store.CarriedAmount[i];
            if (amount > 0 && owner < world.Players.Length)
            {
                world.Players[owner].Add(store.CarriedType[i], amount);
                world.Events.Add(SimEvent.Deposited(store.IdOf(i), owner, store.CarriedType[i], amount));
            }
            store.CarriedAmount[i] = 0;
            store.ClearPath(i);

            // Return to the node if it still has anything left.
            EntityId node = store.HomeNode[i];
            if (store.IsAlive(node) && store.NodeRemaining[node.Index] > 0)
            {
                world.SetJob(i, JobType.MoveToHarvest, store.Position[node.Index], node);
                if (!world.RequestPath(i, store.Position[node.Index]))
                    world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
            }
            else
            {
                world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
            }
        }

        private static void BeginDelivery(SimWorld world, EntityStore store, int i)
        {
            EntityId dropOff = FindDropOff(world, store, i);
            if (dropOff.IsNone)
            {
                store.ClearPath(i);
                world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
                return;
            }

            world.SetJob(i, JobType.Delivering, store.Position[dropOff.Index], dropOff);
            if (!world.RequestPath(i, store.Position[dropOff.Index]))
                world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
        }

        /// <summary>
        /// Nearest finished drop-off building owned by this worker's player. Ties break on entity
        /// index so that two equidistant warehouses always resolve the same way on every machine.
        /// </summary>
        internal static EntityId FindDropOff(SimWorld world, EntityStore store, int worker)
        {
            byte owner = store.Owner[worker];
            Fix2 from = store.Position[worker];
            EntityId best = EntityId.None;
            Fix64 bestSqr = Fix64.MaxValue;

            int count = store.Count;
            for (int b = 1; b < count; b++)
            {
                if (!store.Alive[b]) continue;
                if (store.Kind[b] != EntityKind.Building) continue;
                if (store.Owner[b] != owner) continue;
                if (store.UnderConstruction[b]) continue;
                if (!PrototypeContent.ForBuilding(store.Building[b]).IsDropOff) continue;

                Fix64 sqr = Fix2.SqrDistance(from, store.Position[b]);
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = store.IdOf(b);
                }
            }
            return best;
        }

        private static void GoIdle(SimWorld world, EntityStore store, int i)
        {
            store.ClearPath(i);
            if (store.CarriedAmount[i] > 0) BeginDelivery(world, store, i);
            else world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
        }
    }
}
