using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Tests
{
    /// <summary>Shared helpers for building a prototype world and poking at it.</summary>
    public static class SimFixture
    {
        public static SimWorld NewMatch(ulong seed = 1)
        {
            var world = new SimWorld(MatchConfig.TwoPlayer(seed));
            PrototypeMap.Build(world);
            return world;
        }

        public static List<EntityId> UnitsOf(SimWorld world, byte player, EntityKind kind)
        {
            var result = new List<EntityId>();
            for (int i = 1; i < world.Entities.Count; i++)
            {
                if (!world.Entities.Alive[i]) continue;
                if (world.Entities.Owner[i] != player) continue;
                if (world.Entities.Kind[i] != kind) continue;
                result.Add(world.Entities.IdOf(i));
            }
            return result;
        }

        public static EntityId FirstBuilding(SimWorld world, byte player, BuildingType type)
        {
            for (int i = 1; i < world.Entities.Count; i++)
            {
                if (!world.Entities.Alive[i]) continue;
                if (world.Entities.Owner[i] != player) continue;
                if (world.Entities.Kind[i] != EntityKind.Building) continue;
                if (world.Entities.Building[i] != type) continue;
                return world.Entities.IdOf(i);
            }
            return EntityId.None;
        }

        public static int CountEvents(SimWorld world, SimEventType type)
        {
            int n = 0;
            foreach (SimEvent e in world.Events) if (e.Type == type) n++;
            return n;
        }

        /// <summary>Steps the world until <paramref name="predicate"/> holds or the budget runs out.</summary>
        public static bool StepUntil(SimWorld world, System.Func<SimWorld, bool> predicate, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (predicate(world)) return true;
                world.Step();
            }
            return predicate(world);
        }
    }
}
