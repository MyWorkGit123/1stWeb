using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Net.Client;
using Brinehold.Sim.World;

namespace Brinehold.Client.Selection
{
    /// <summary>
    /// What the player currently has selected.
    ///
    /// This is deliberately engine-independent: selection is the single most-used mechanic in an
    /// RTS and the one players notice immediately when it is subtly wrong, so it is worth being
    /// able to unit test it without opening an editor. The Unity layer above only turns mouse
    /// positions into world coordinates and calls into here.
    /// </summary>
    public sealed class SelectionModel
    {
        private readonly List<EntityId> _selected = new List<EntityId>();
        private readonly ReplicaWorld _replica;

        public SelectionModel(ReplicaWorld replica) => _replica = replica;

        public IReadOnlyList<EntityId> Selected => _selected;
        public int Count => _selected.Count;
        public bool IsEmpty => _selected.Count == 0;

        /// <summary>The player may command only their own units. Enemies can be inspected, not ordered.</summary>
        public bool IsCommandable(EntityId id)
            => _replica.TryGet(id, out ReplicaWorld.Entity e) && e.Owner == _replica.LocalPlayer && IsMobileOrBuilding(e.Kind);

        private static bool IsMobileOrBuilding(EntityKind kind)
            => kind == EntityKind.Worker || kind == EntityKind.Soldier || kind == EntityKind.Ship || kind == EntityKind.Building;

        public void Clear() => _selected.Clear();

        public bool Contains(EntityId id) => _selected.Contains(id);

        public void Set(EntityId id)
        {
            _selected.Clear();
            if (!id.IsNone) _selected.Add(id);
        }

        public void Add(EntityId id)
        {
            if (id.IsNone || _selected.Contains(id)) return;
            _selected.Add(id);
        }

        public void Toggle(EntityId id)
        {
            if (id.IsNone) return;
            if (!_selected.Remove(id)) _selected.Add(id);
        }

        public void SetMany(IEnumerable<EntityId> ids)
        {
            _selected.Clear();
            foreach (EntityId id in ids) if (!_selected.Contains(id)) _selected.Add(id);
        }

        public void AddMany(IEnumerable<EntityId> ids)
        {
            foreach (EntityId id in ids) if (!_selected.Contains(id)) _selected.Add(id);
        }

        /// <summary>
        /// Entity under a click point, preferring units over buildings so that clicking a worker
        /// standing on a warehouse selects the worker — which is what the player meant.
        /// </summary>
        public EntityId Pick(Fix2 worldPoint, Fix64 radius)
        {
            EntityId bestUnit = EntityId.None, bestBuilding = EntityId.None;
            Fix64 bestUnitSqr = Fix64.MaxValue, bestBuildingSqr = Fix64.MaxValue;

            foreach (ReplicaWorld.Entity entity in _replica.Entities)
            {
                if (entity.Kind == EntityKind.ResourceNode) continue;

                Fix64 reach = entity.Kind == EntityKind.Building
                    ? radius + Fix64.FromInt(Brinehold.Sim.Content.PrototypeContent.ForBuilding(entity.Building).FootprintHalf)
                    : radius;

                Fix64 sqr = Fix2.SqrDistance(entity.State.Value.Position, worldPoint);
                if (sqr > reach * reach) continue;

                if (entity.Kind == EntityKind.Building)
                {
                    if (sqr < bestBuildingSqr) { bestBuildingSqr = sqr; bestBuilding = entity.Id; }
                }
                else
                {
                    if (sqr < bestUnitSqr) { bestUnitSqr = sqr; bestUnit = entity.Id; }
                }
            }

            return bestUnit.IsNone ? bestBuilding : bestUnit;
        }

        /// <summary>Resource node under a click point, for issuing harvest orders.</summary>
        public EntityId PickResourceNode(Fix2 worldPoint, Fix64 radius)
        {
            EntityId best = EntityId.None;
            Fix64 bestSqr = Fix64.MaxValue;

            foreach (ReplicaWorld.Entity entity in _replica.Entities)
            {
                if (entity.Kind != EntityKind.ResourceNode) continue;
                Fix64 sqr = Fix2.SqrDistance(entity.State.Value.Position, worldPoint);
                if (sqr > radius * radius) continue;
                if (sqr < bestSqr) { bestSqr = sqr; best = entity.Id; }
            }
            return best;
        }

        /// <summary>
        /// Drag selection. Own mobile units win outright: dragging a box across your army and a
        /// building should give you the army, and dragging across enemies should give you nothing
        /// commandable. Falls back to a single building only when the box caught nothing else.
        /// </summary>
        public List<EntityId> BoxSelect(Fix2 cornerA, Fix2 cornerB, bool ownUnitsOnly = true)
        {
            Fix64 minX = FixMath.Min(cornerA.X, cornerB.X), maxX = FixMath.Max(cornerA.X, cornerB.X);
            Fix64 minY = FixMath.Min(cornerA.Y, cornerB.Y), maxY = FixMath.Max(cornerA.Y, cornerB.Y);

            var units = new List<EntityId>();
            var buildings = new List<EntityId>();

            foreach (ReplicaWorld.Entity entity in _replica.Entities)
            {
                if (entity.Kind == EntityKind.ResourceNode) continue;
                if (ownUnitsOnly && entity.Owner != _replica.LocalPlayer) continue;

                Fix2 position = entity.State.Value.Position;
                if (position.X < minX || position.X > maxX) continue;
                if (position.Y < minY || position.Y > maxY) continue;

                if (entity.Kind == EntityKind.Building) buildings.Add(entity.Id);
                else units.Add(entity.Id);
            }

            return units.Count > 0 ? units : buildings;
        }

        /// <summary>
        /// Every own unit of the same kind inside a region — the double-click behaviour. Restricting
        /// it to the visible region rather than the whole map is what stops a double-click pulling
        /// workers off the far side of the settlement.
        /// </summary>
        public List<EntityId> SelectSameKindInRegion(EntityId template, Fix2 cornerA, Fix2 cornerB)
        {
            var result = new List<EntityId>();
            if (!_replica.TryGet(template, out ReplicaWorld.Entity reference)) return result;

            Fix64 minX = FixMath.Min(cornerA.X, cornerB.X), maxX = FixMath.Max(cornerA.X, cornerB.X);
            Fix64 minY = FixMath.Min(cornerA.Y, cornerB.Y), maxY = FixMath.Max(cornerA.Y, cornerB.Y);

            foreach (ReplicaWorld.Entity entity in _replica.Entities)
            {
                if (entity.Kind != reference.Kind) continue;
                if (entity.Owner != reference.Owner) continue;
                if (entity.Kind == EntityKind.Building && entity.Building != reference.Building) continue;

                Fix2 position = entity.State.Value.Position;
                if (position.X < minX || position.X > maxX) continue;
                if (position.Y < minY || position.Y > maxY) continue;
                result.Add(entity.Id);
            }
            return result;
        }

        /// <summary>Drops entities that have died or left vision. Called once per tick.</summary>
        public void Prune()
        {
            for (int i = _selected.Count - 1; i >= 0; i--)
                if (!_replica.Knows(_selected[i])) _selected.RemoveAt(i);
        }

        /// <summary>Own commandable entities in the selection, which is what an order applies to.</summary>
        public EntityId[] CommandableSelection()
        {
            var result = new List<EntityId>(_selected.Count);
            for (int i = 0; i < _selected.Count; i++)
                if (IsCommandable(_selected[i])) result.Add(_selected[i]);
            return result.ToArray();
        }

        /// <summary>Cycles through own idle workers, for the idle-worker hotkey.</summary>
        public EntityId NextIdleWorker(EntityId after)
        {
            EntityId first = EntityId.None;
            bool passedAfter = after.IsNone;

            foreach (ReplicaWorld.Entity entity in _replica.Entities)
            {
                if (entity.Owner != _replica.LocalPlayer) continue;
                if (entity.Kind != EntityKind.Worker) continue;
                if (entity.State.Value.Job != JobType.Idle) continue;

                if (first.IsNone) first = entity.Id;
                if (passedAfter) return entity.Id;
                if (entity.Id == after) passedAfter = true;
            }
            return first;
        }
    }
}
