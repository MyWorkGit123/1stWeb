using Brinehold.Core.Math;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// Advances entities along their current path.
    ///
    /// Writes: Position, Heading, PathCursor. Reads: MoveSpeed, Domain, path buffers.
    ///
    /// Movement is a pure function of (position, path, speed, tick), which is exactly why the
    /// network layer can replicate a move as one intent message and let the client extrapolate it
    /// locally for the next thirty seconds instead of streaming transforms.
    /// </summary>
    public sealed class MovementSystem : ISimSystem
    {
        public void Execute(SimWorld world)
        {
            EntityStore store = world.Entities;
            int count = store.Count;

            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                if (store.Domain[i] == MovementDomain.Static) continue;
                if (!store.HasPath(i)) continue;

                Fix64 stepDistance = store.MoveSpeed[i] / SimConstants.TicksPerSecond;
                if (stepDistance <= Fix64.Zero) continue;

                Fix2 position = store.Position[i];

                // One tick may cross several short waypoints, so consume distance in a loop.
                while (stepDistance > Fix64.Zero && store.HasPath(i))
                {
                    int[] path = store.PathBuffer(i);
                    int targetCell = path[store.PathCursor[i]];
                    Fix2 waypoint = world.Nav.CellCentre(targetCell);

                    Fix64 remaining = Fix2.Distance(position, waypoint);
                    if (remaining <= stepDistance)
                    {
                        position = waypoint;
                        stepDistance -= remaining;
                        store.PathCursor[i]++;
                    }
                    else
                    {
                        Fix2 next = Fix2.MoveTowards(position, waypoint, stepDistance);
                        store.Heading[i] = (waypoint - position).Angle;
                        position = next;
                        stepDistance = Fix64.Zero;
                    }
                }

                store.Position[i] = position;

                if (!store.HasPath(i))
                {
                    store.ClearPath(i);
                    // A plain move order is finished the moment the path runs out. Task jobs
                    // (harvest, build, attack) are resolved by their own systems on arrival.
                    if (store.Job[i] == JobType.MoveTo)
                        world.SetJobIfChanged(i, JobType.Idle, store.Position[i], Brinehold.Core.Collections.EntityId.None);
                }
            }
        }
    }
}
