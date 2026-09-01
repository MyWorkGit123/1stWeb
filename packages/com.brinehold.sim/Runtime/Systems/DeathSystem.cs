using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// Removes entities whose health has run out and releases everything they held.
    ///
    /// Writes: Alive, navigation footprints, player population and population cap.
    ///
    /// Destruction is deliberately consequential: a destroyed drop-off strands the workers that
    /// were delivering to it, and a destroyed house lowers the population cap below the population,
    /// which stops further training until the player rebuilds.
    /// </summary>
    public sealed class DeathSystem : ISimSystem
    {
        public void Execute(SimWorld world)
        {
            EntityStore store = world.Entities;
            int count = store.Count;

            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                if (store.Kind[i] == EntityKind.ResourceNode) continue;
                if (store.Health[i] > Fix64.Zero) continue;

                byte owner = store.Owner[i];
                EntityId id = store.IdOf(i);

                if (store.Kind[i] == EntityKind.Building)
                {
                    int cell = world.Nav.CellAt(store.Position[i]);
                    world.Nav.SetFootprint(world.Nav.CellX(cell), world.Nav.CellY(cell), store.FootprintHalf[i], false);

                    if (owner < world.Players.Length && !store.UnderConstruction[i])
                    {
                        int capacity = world.Content.Building(store.Building[i]).PopulationCapacity;
                        world.Players[owner].PopulationCap = System.Math.Max(0, world.Players[owner].PopulationCap - capacity);
                    }
                }
                else if (owner < world.Players.Length)
                {
                    int cost = world.Content.Unit(store.Kind[i]).PopulationCost;
                    world.Players[owner].PopulationUsed = System.Math.Max(0, world.Players[owner].PopulationUsed - cost);
                }

                world.Events.Add(SimEvent.Destroyed(id, owner, store.Position[i]));
                store.Destroy(id);
            }
        }
    }
}
