using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;

namespace Brinehold.Net.Replication
{
    /// <summary>
    /// Reproduces an entity's movement from its intent alone.
    ///
    /// This is the single most important class in the networking design. Both sides run it: the
    /// client uses it to animate entities between messages, and the server runs a shadow copy so it
    /// can tell how far the client has drifted from the truth and send a correction only when it
    /// actually matters.
    ///
    /// Because both sides run the *same* code against the same nav grid, an entity walking forty
    /// metres costs one message rather than six hundred position updates. Drift appears only where
    /// the two grids genuinely differ — for instance when the client cannot see a building that is
    /// blocking the server's path — and that is precisely when a correction is worth its bytes.
    /// </summary>
    public sealed class IntentExtrapolator
    {
        private readonly NavGrid _grid;
        private readonly PathFinder _finder;

        public IntentExtrapolator(NavGrid grid)
        {
            _grid = grid;
            _finder = new PathFinder(grid);
        }

        public struct Entity
        {
            public EntityId Id;
            public EntityKind Kind;
            public byte Owner;
            public MovementDomain Domain;
            public Fix64 Speed;
            public Fix2 Position;
            public Fix64 Heading;

            public JobType Job;
            public Fix2 Destination;
            public EntityId Target;

            public int[]? Path;
            public int PathLength;
            public int PathCursor;

            public bool HasPath => Path != null && PathCursor < PathLength;
        }

        /// <summary>
        /// Applies a new intent, recomputing the path the same way the server did. A job that does
        /// not involve travel simply stops the entity where it stands.
        /// </summary>
        public void SetIntent(ref Entity entity, JobType job, Fix2 destination, EntityId target)
        {
            entity.Job = job;
            entity.Destination = destination;
            entity.Target = target;
            entity.PathLength = 0;
            entity.PathCursor = 0;

            if (!IsTravelling(job)) return;
            if (entity.Domain == MovementDomain.Static) return;

            entity.Path ??= new int[SimConstants.MaxPathLength];
            int start = _grid.CellAt(entity.Position);
            int goal = _grid.CellAt(destination);
            entity.PathLength = _finder.FindPath(start, goal, entity.Domain, entity.Path);
        }

        /// <summary>Advances one entity by one tick. Mirrors MovementSystem exactly.</summary>
        public void Step(ref Entity entity)
        {
            if (!entity.HasPath) return;
            if (entity.Domain == MovementDomain.Static) return;

            Fix64 stepDistance = entity.Speed / SimConstants.TicksPerSecond;
            if (stepDistance <= Fix64.Zero) return;

            Fix2 position = entity.Position;

            while (stepDistance > Fix64.Zero && entity.PathCursor < entity.PathLength)
            {
                Fix2 waypoint = _grid.CellCentre(entity.Path![entity.PathCursor]);
                Fix64 remaining = Fix2.Distance(position, waypoint);

                if (remaining <= stepDistance)
                {
                    position = waypoint;
                    stepDistance -= remaining;
                    entity.PathCursor++;
                }
                else
                {
                    entity.Heading = (waypoint - position).Angle;
                    position = Fix2.MoveTowards(position, waypoint, stepDistance);
                    stepDistance = Fix64.Zero;
                }
            }

            entity.Position = position;
        }

        public static bool IsTravelling(JobType job)
        {
            switch (job)
            {
                case JobType.MoveTo:
                case JobType.MoveToHarvest:
                case JobType.MoveToBuild:
                case JobType.MoveToAttack:
                case JobType.Delivering:
                    return true;
                default:
                    return false;
            }
        }
    }
}
