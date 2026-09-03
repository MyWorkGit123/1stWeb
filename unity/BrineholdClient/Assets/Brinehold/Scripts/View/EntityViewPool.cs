using System.Collections.Generic;
using Brinehold.Net.Client;
using Brinehold.Sim.World;
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// Pooled GameObjects for everything the player can see.
    ///
    /// Nothing is instantiated in steady state: views are taken from and returned to per-kind pools,
    /// which is what keeps a settlement of a thousand workers from producing a thousand allocations
    /// every time a raid crosses the fog boundary.
    ///
    /// The pool is driven entirely by the replica. If an entity is not in the replica, no view exists
    /// for it — so a player cannot find an enemy by inspecting the scene hierarchy, because the
    /// object was never created.
    /// </summary>
    public sealed class EntityViewPool : MonoBehaviour
    {
        [Header("Prefabs")]
        public GameObject WorkerPrefab;
        public GameObject SoldierPrefab;
        public GameObject ShipPrefab;
        public GameObject BuildingPrefab;
        public GameObject ResourcePrefab;

        [Header("Player colours")]
        public Color[] PlayerColours = { new Color(0.20f, 0.45f, 0.85f), new Color(0.85f, 0.35f, 0.20f) };
        public Color NeutralColour = new Color(0.55f, 0.55f, 0.50f);

        private readonly Dictionary<uint, EntityView> _active = new Dictionary<uint, EntityView>();
        private readonly Dictionary<EntityKind, Stack<EntityView>> _pools = new Dictionary<EntityKind, Stack<EntityView>>();
        private readonly List<uint> _toRemove = new List<uint>();

        /// <summary>Adds, removes and re-targets views to match the replica. Called once per tick.</summary>
        public void Synchronise(ReplicaWorld replica)
        {
            foreach (ReplicaWorld.Entity entity in replica.Entities)
            {
                if (!_active.TryGetValue(entity.Id.Raw, out EntityView view))
                {
                    view = Take(entity.Kind);
                    if (view == null) continue;
                    view.Bind(entity, ColourFor(entity.Owner));
                    _active[entity.Id.Raw] = view;
                }

                view.SetTarget(entity);
            }

            // Anything the replica has dropped has either died or left vision. Either way its view goes.
            _toRemove.Clear();
            foreach (KeyValuePair<uint, EntityView> pair in _active)
                if (!replica.Knows(new Brinehold.Core.Collections.EntityId(pair.Key))) _toRemove.Add(pair.Key);

            for (int i = 0; i < _toRemove.Count; i++)
            {
                EntityView view = _active[_toRemove[i]];
                _active.Remove(_toRemove[i]);
                Return(view);
            }
        }

        /// <summary>Interpolates every view. Called every frame, not every tick.</summary>
        public void Interpolate(float alpha)
        {
            foreach (KeyValuePair<uint, EntityView> pair in _active) pair.Value.Interpolate(alpha);
        }

        public bool TryGetView(uint rawId, out EntityView view) => _active.TryGetValue(rawId, out view);

        private Color ColourFor(byte owner)
        {
            if (owner == SimConstants.NeutralPlayer) return NeutralColour;
            return owner < PlayerColours.Length ? PlayerColours[owner] : NeutralColour;
        }

        private EntityView Take(EntityKind kind)
        {
            if (_pools.TryGetValue(kind, out Stack<EntityView> pool) && pool.Count > 0)
            {
                EntityView pooled = pool.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }

            GameObject prefab = PrefabFor(kind);
            if (prefab == null) return null;

            GameObject instance = Instantiate(prefab, transform);

            // The prefab templates are inactive so they are never drawn or ticked themselves, and a
            // clone inherits that. Activating here is not optional: without it every newly visible
            // entity would exist, move and fight while being completely invisible.
            instance.SetActive(true);

            EntityView view = instance.GetComponent<EntityView>();
            if (view == null) view = instance.AddComponent<EntityView>();
            view.Kind = kind;
            return view;
        }

        private void Return(EntityView view)
        {
            view.gameObject.SetActive(false);
            if (!_pools.TryGetValue(view.Kind, out Stack<EntityView> pool))
            {
                pool = new Stack<EntityView>();
                _pools[view.Kind] = pool;
            }
            pool.Push(view);
        }

        private GameObject PrefabFor(EntityKind kind)
        {
            switch (kind)
            {
                case EntityKind.Worker: return WorkerPrefab;
                case EntityKind.Soldier: return SoldierPrefab;
                case EntityKind.Ship: return ShipPrefab;
                case EntityKind.Building: return BuildingPrefab;
                case EntityKind.ResourceNode: return ResourcePrefab;
                default: return null;
            }
        }
    }
}
