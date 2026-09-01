using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// Advances training queues at buildings and spawns the finished unit.
    ///
    /// Writes: TrainingTimer, TrainingQueued, spawns entities.
    ///
    /// Costs were already deducted when the order was queued, so cancelling refunds. Completion is
    /// decided here, on the server, which is why a client cannot conjure a unit by claiming a timer
    /// finished.
    /// </summary>
    public sealed class ProductionSystem : ISimSystem
    {
        public void Execute(SimWorld world)
        {
            EntityStore store = world.Entities;
            int count = store.Count;

            for (int b = 1; b < count; b++)
            {
                if (!store.Alive[b]) continue;
                if (store.Kind[b] != EntityKind.Building) continue;
                if (store.UnderConstruction[b]) continue;
                if (store.TrainingQueued[b] <= 0) continue;

                if (--store.TrainingTimer[b] > 0) continue;

                EntityKind kind = store.TrainingKind[b];
                byte owner = store.Owner[b];
                Fix2 spawnPoint = FindSpawnPoint(world, store, b, kind);

                EntityId spawned = world.SpawnUnit(kind, owner, spawnPoint);
                world.Events.Add(new SimEvent
                {
                    Type = SimEventType.TrainingCompleted,
                    Entity = spawned,
                    Other = store.IdOf(b),
                    Player = owner,
                    ValueA = (int)kind,
                    Position = spawnPoint
                });

                store.TrainingQueued[b]--;
                store.TrainingTimer[b] = store.TrainingQueued[b] > 0
                    ? world.Content.Unit(kind).TrainTicks
                    : 0;
            }
        }

        /// <summary>
        /// Nearest legal cell outside the building's footprint, searched in a fixed ring order so
        /// two machines place a new unit in exactly the same cell.
        /// </summary>
        private static Fix2 FindSpawnPoint(SimWorld world, EntityStore store, int building, EntityKind kind)
        {
            MovementDomain domain = world.Content.Unit(kind).Domain;
            int centre = world.Nav.CellAt(store.Position[building]);
            int cx = world.Nav.CellX(centre);
            int cy = world.Nav.CellY(centre);
            int half = store.FootprintHalf[building];

            for (int r = half + 1; r <= half + 12; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (System.Math.Abs(dx) != r && System.Math.Abs(dy) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if (!world.Nav.InBounds(x, y)) continue;
                    int cell = world.Nav.Index(x, y);
                    if (world.Nav.IsPassable(cell, domain)) return world.Nav.CellCentre(cell);
                }
            }

            return store.Position[building];
        }
    }
}
