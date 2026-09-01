using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// Turns construction sites into buildings.
    ///
    /// Writes: BuildProgress, UnderConstruction, Health, player population cap, worker jobs.
    ///
    /// Progress accrues per worker per tick, so more builders finish sooner. A site is fragile
    /// (one tenth health) until it completes.
    /// </summary>
    public sealed class ConstructionSystem : ISimSystem
    {
        public void Execute(SimWorld world)
        {
            EntityStore store = world.Entities;
            int count = store.Count;

            // Workers apply labour to their assigned site.
            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                if (store.Kind[i] != EntityKind.Worker) continue;

                JobType job = store.Job[i];
                if (job != JobType.MoveToBuild && job != JobType.Building) continue;

                EntityId site = store.JobTarget[i];
                if (!store.IsAlive(site) || !store.UnderConstruction[site.Index])
                {
                    store.ClearPath(i);
                    world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
                    continue;
                }

                if (!SimRange.InReach(store, i, site.Index, SimRange.InteractionReach))
                {
                    if (job == JobType.Building)
                    {
                        // Pushed out of reach somehow: walk back.
                        world.SetJobIfChanged(i, JobType.MoveToBuild, store.Position[site.Index], site);
                        world.RequestPath(i, store.Position[site.Index]);
                    }
                    else if (!store.HasPath(i))
                    {
                        world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
                    }
                    continue;
                }

                if (job == JobType.MoveToBuild)
                {
                    store.ClearPath(i);
                    world.SetJobIfChanged(i, JobType.Building, store.Position[i], site);
                }

                store.BuildProgress[site.Index] += world.Content.BuildProgressPerWorkerTick;
            }

            // Complete any site that has reached its required labour.
            for (int b = 1; b < count; b++)
            {
                if (!store.Alive[b]) continue;
                if (store.Kind[b] != EntityKind.Building) continue;
                if (!store.UnderConstruction[b]) continue;
                if (store.BuildProgress[b] < store.BuildRequired[b]) continue;

                store.UnderConstruction[b] = false;
                store.Health[b] = store.MaxHealth[b];

                BuildingType type = store.Building[b];
                byte owner = store.Owner[b];
                ContentDatabase.BuildingStats stats = world.Content.Building(type);

                if (owner < world.Players.Length && stats.PopulationCapacity > 0)
                {
                    world.Players[owner].PopulationCap = System.Math.Min(
                        world.Players[owner].PopulationCap + stats.PopulationCapacity,
                        world.Content.MaxPopulationCap);
                }

                world.Events.Add(new SimEvent
                {
                    Type = SimEventType.ConstructionCompleted,
                    Entity = store.IdOf(b),
                    Player = owner,
                    ValueA = (int)type,
                    Position = store.Position[b]
                });

                // Release the builders.
                EntityId siteId = store.IdOf(b);
                for (int i = 1; i < count; i++)
                {
                    if (!store.Alive[i]) continue;
                    if (store.Kind[i] != EntityKind.Worker) continue;
                    if (store.JobTarget[i] != siteId) continue;
                    if (store.Job[i] != JobType.Building && store.Job[i] != JobType.MoveToBuild) continue;
                    store.ClearPath(i);
                    world.SetJobIfChanged(i, JobType.Idle, store.Position[i], EntityId.None);
                }
            }
        }
    }
}
