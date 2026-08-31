using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// Target acquisition and damage application.
    ///
    /// Writes: AttackTimer, Job, paths, and (via SimWorld.ApplyDamage) Health.
    ///
    /// Every hit in the game is decided here, on the server, from server-owned statistics. There is
    /// no message in the protocol by which a client can report damage, which is why a damage hack
    /// is not something we detect — it is something that has nowhere to enter.
    ///
    /// The prototype uses flat damage with no armour table, accuracy roll, morale or terrain
    /// modifier; those arrive in M11 (COMBAT_DESIGN.md).
    /// </summary>
    public sealed class CombatSystem : ISimSystem
    {
        /// <summary>Re-path toward a moving target at most this often, to bound pathfinding cost.</summary>
        private const int RepathIntervalTicks = 10;

        public void Execute(SimWorld world)
        {
            EntityStore store = world.Entities;
            int count = store.Count;

            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                if (store.AttackDamage[i] <= Fix64.Zero) continue;
                if (store.Kind[i] == EntityKind.Building || store.Kind[i] == EntityKind.ResourceNode) continue;

                if (store.AttackTimer[i] > 0) store.AttackTimer[i]--;

                JobType job = store.Job[i];
                bool ordered = job == JobType.MoveToAttack || job == JobType.Attacking;

                if (!ordered)
                {
                    // Idle military units defend themselves: acquire a hostile inside vision.
                    if (store.Kind[i] == EntityKind.Worker) continue;
                    if (job != JobType.Idle) continue;

                    EntityId acquired = AcquireTarget(world, store, i);
                    if (acquired.IsNone) continue;
                    world.SetJob(i, JobType.MoveToAttack, store.Position[acquired.Index], acquired);
                    continue;
                }

                EntityId target = store.JobTarget[i];
                if (!store.IsAlive(target))
                {
                    store.ClearPath(i);
                    world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
                    continue;
                }

                int t = target.Index;
                Fix64 reach = store.AttackRange[i] + Fix64.FromInt(store.FootprintHalf[t]);
                Fix64 sqr = Fix2.SqrDistance(store.Position[i], store.Position[t]);

                if (sqr <= reach * reach)
                {
                    if (store.Job[i] != JobType.Attacking)
                    {
                        store.ClearPath(i);
                        world.SetJobIfChanged(i, JobType.Attacking, store.Position[i], target);
                    }

                    if (store.AttackTimer[i] <= 0)
                    {
                        store.Heading[i] = (store.Position[t] - store.Position[i]).Angle;
                        world.ApplyDamage(store.IdOf(i), target, store.AttackDamage[i]);
                        store.AttackTimer[i] = store.AttackCooldown[i];
                    }
                }
                else
                {
                    // Chase. Ships cannot chase onto land and vice versa; the path simply fails.
                    if (store.Job[i] == JobType.Attacking)
                    {
                        world.SetJobIfChanged(i, JobType.MoveToAttack, store.Position[t], target);
                        world.RequestPath(i, store.Position[t]);
                    }
                    else if (!store.HasPath(i) || (world.Tick % RepathIntervalTicks) == (uint)(i % RepathIntervalTicks))
                    {
                        if (!world.RequestPath(i, store.Position[t]) && !store.HasPath(i))
                            world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
                    }
                }
            }
        }

        /// <summary>
        /// Nearest hostile entity within vision. Ties break on entity index, so every machine picks
        /// the same target for the same situation.
        /// </summary>
        private static EntityId AcquireTarget(SimWorld world, EntityStore store, int i)
        {
            byte owner = store.Owner[i];
            Fix64 vision = store.VisionRange[i];
            Fix64 visionSqr = vision * vision;
            Fix2 from = store.Position[i];

            EntityId best = EntityId.None;
            Fix64 bestSqr = Fix64.MaxValue;

            int count = store.Count;
            for (int j = 1; j < count; j++)
            {
                if (!store.Alive[j] || j == i) continue;
                if (store.Kind[j] == EntityKind.ResourceNode) continue;
                if (!world.AreHostile(owner, store.Owner[j])) continue;

                Fix64 sqr = Fix2.SqrDistance(from, store.Position[j]);
                if (sqr > visionSqr) continue;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = store.IdOf(j);
                }
            }
            return best;
        }
    }
}
