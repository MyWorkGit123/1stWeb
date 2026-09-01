using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Net.Client;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;

namespace Brinehold.Client.Input
{
    /// <summary>
    /// Turns a right-click into the order the player meant.
    ///
    /// The contextual default is the whole ergonomics of an RTS: click a tree and workers harvest,
    /// click an enemy and soldiers attack, click the ground and everyone walks. Getting this wrong
    /// is felt on every single click, so it lives here where it can be tested rather than inside a
    /// MonoBehaviour.
    ///
    /// It produces a Command; it never changes any state. The server decides whether the order is
    /// legal, and the client's own preview is advisory.
    /// </summary>
    public sealed class OrderIssuer
    {
        private static readonly Fix64 PickRadius = Fix64.FromFraction(15, 10);

        private readonly ReplicaWorld _replica;
        private readonly Selection.SelectionModel _selection;

        public OrderIssuer(ReplicaWorld replica, Selection.SelectionModel selection)
        {
            _replica = replica;
            _selection = selection;
        }

        /// <summary>
        /// The order a right-click at <paramref name="worldPoint"/> should produce, or null when the
        /// click means nothing (empty selection, or nothing commandable selected).
        /// </summary>
        public Command? RightClick(Fix2 worldPoint, NavGrid nav)
        {
            EntityId[] commandable = _selection.CommandableSelection();
            if (commandable.Length == 0) return null;

            // An enemy under the cursor is an attack order for anything that can fight.
            EntityId target = _selection.Pick(worldPoint, PickRadius);
            if (!target.IsNone && _replica.TryGet(target, out ReplicaWorld.Entity targetEntity))
            {
                if (targetEntity.Owner != _replica.LocalPlayer &&
                    targetEntity.Owner != SimConstants.NeutralPlayer)
                {
                    EntityId[] fighters = FilterCanFight(commandable);
                    if (fighters.Length > 0)
                        return Command.Attack(_replica.LocalPlayer, 0, fighters, target);
                }
            }

            // A resource node is a harvest order for any workers in the selection.
            EntityId node = _selection.PickResourceNode(worldPoint, PickRadius);
            if (!node.IsNone)
            {
                EntityId[] workers = FilterKind(commandable, EntityKind.Worker);
                if (workers.Length > 0)
                    return Command.Harvest(_replica.LocalPlayer, 0, workers, node);
            }

            // Otherwise: walk there.
            int cell = nav.CellAt(worldPoint);
            EntityId[] movers = FilterMobile(commandable);
            if (movers.Length == 0) return null;

            return Command.Move(_replica.LocalPlayer, 0, movers, nav.CellX(cell), nav.CellY(cell));
        }

        /// <summary>The build order for a confirmed placement.</summary>
        public Command? PlaceBuilding(BuildingType type, int cellX, int cellY)
        {
            EntityId[] workers = FilterKind(_selection.CommandableSelection(), EntityKind.Worker);
            if (workers.Length == 0) return null;
            return Command.Build(_replica.LocalPlayer, 0, workers, type, cellX, cellY);
        }

        /// <summary>Queues a unit at the first selected building that can train it.</summary>
        public Command? Train(EntityKind kind)
        {
            foreach (EntityId id in _selection.CommandableSelection())
            {
                if (!_replica.TryGet(id, out ReplicaWorld.Entity entity)) continue;
                if (entity.Kind != EntityKind.Building || entity.UnderConstruction) continue;
                if (!Brinehold.Sim.Content.PrototypeContent.CanTrain(entity.Building, kind)) continue;
                return Command.Train(_replica.LocalPlayer, 0, id, kind);
            }
            return null;
        }

        public Command? Stop()
        {
            EntityId[] commandable = _selection.CommandableSelection();
            if (commandable.Length == 0) return null;
            return Command.Stop(_replica.LocalPlayer, 0, commandable);
        }

        private EntityId[] FilterKind(EntityId[] ids, EntityKind kind)
        {
            var result = new System.Collections.Generic.List<EntityId>(ids.Length);
            foreach (EntityId id in ids)
                if (_replica.TryGet(id, out ReplicaWorld.Entity e) && e.Kind == kind) result.Add(id);
            return result.ToArray();
        }

        private EntityId[] FilterMobile(EntityId[] ids)
        {
            var result = new System.Collections.Generic.List<EntityId>(ids.Length);
            foreach (EntityId id in ids)
            {
                if (!_replica.TryGet(id, out ReplicaWorld.Entity e)) continue;
                if (e.Kind == EntityKind.Worker || e.Kind == EntityKind.Soldier || e.Kind == EntityKind.Ship)
                    result.Add(id);
            }
            return result.ToArray();
        }

        private EntityId[] FilterCanFight(EntityId[] ids)
        {
            var result = new System.Collections.Generic.List<EntityId>(ids.Length);
            foreach (EntityId id in ids)
            {
                if (!_replica.TryGet(id, out ReplicaWorld.Entity e)) continue;
                if (e.Kind == EntityKind.Soldier || e.Kind == EntityKind.Ship) result.Add(id);
            }
            return result.ToArray();
        }
    }
}
