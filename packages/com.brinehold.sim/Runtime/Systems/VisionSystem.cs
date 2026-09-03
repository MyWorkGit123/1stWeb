using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// Rebuilds each player's visibility from their live vision sources.
    ///
    /// Writes: the fog grid. Reads: entity positions, ownership, vision ranges.
    ///
    /// This runs late in the tick, after everything has moved and died, so the fog the replication
    /// layer consults immediately afterwards reflects the state it is about to describe. Allies
    /// share vision, which is why the loop reveals to every player on the owner's team.
    /// </summary>
    public sealed class VisionSystem : ISimSystem
    {
        public void Execute(SimWorld world)
        {
            EntityStore store = world.Entities;
            world.Fog.ClearVisible();

            int count = store.Count;
            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                byte owner = store.Owner[i];
                if (owner >= world.Players.Length) continue;
                if (store.VisionRange[i] <= Brinehold.Core.Math.Fix64.Zero) continue;

                byte team = world.Players[owner].Team;
                for (int p = 0; p < world.Players.Length; p++)
                {
                    if (world.Players[p].Team != team) continue;
                    world.Fog.RevealCircle(p, world.Nav, store.Position[i], store.VisionRange[i]);
                }
            }
        }
    }
}
