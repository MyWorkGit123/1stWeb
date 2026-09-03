using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Net.Client;

namespace Brinehold.Client.Selection
{
    /// <summary>
    /// The ten control groups bound to Ctrl+0..9.
    ///
    /// Groups hold entity ids, so a group survives units dying — it simply gets smaller — and a
    /// recycled entity slot cannot silently join a group, because the generation in the id will not
    /// match. Mixed groups of land units and ships are allowed by design: a player who wants one
    /// key for "the invasion force" should get it.
    /// </summary>
    public sealed class ControlGroups
    {
        public const int GroupCount = 10;

        private readonly List<EntityId>[] _groups = new List<EntityId>[GroupCount];
        private readonly ReplicaWorld _replica;

        public ControlGroups(ReplicaWorld replica)
        {
            _replica = replica;
            for (int i = 0; i < GroupCount; i++) _groups[i] = new List<EntityId>();
        }

        public IReadOnlyList<EntityId> Group(int index)
            => index >= 0 && index < GroupCount ? _groups[index] : System.Array.Empty<EntityId>();

        public int Count(int index) => index >= 0 && index < GroupCount ? _groups[index].Count : 0;

        /// <summary>Ctrl+N: replace the group with the current selection.</summary>
        public void Assign(int index, IReadOnlyList<EntityId> selection)
        {
            if (index < 0 || index >= GroupCount) return;
            _groups[index].Clear();
            for (int i = 0; i < selection.Count; i++)
                if (!_groups[index].Contains(selection[i])) _groups[index].Add(selection[i]);
        }

        /// <summary>Shift+N: add the current selection to the group.</summary>
        public void Append(int index, IReadOnlyList<EntityId> selection)
        {
            if (index < 0 || index >= GroupCount) return;
            for (int i = 0; i < selection.Count; i++)
                if (!_groups[index].Contains(selection[i])) _groups[index].Add(selection[i]);
        }

        /// <summary>N: recall the group, dropping anything that has since died.</summary>
        public List<EntityId> Recall(int index)
        {
            var result = new List<EntityId>();
            if (index < 0 || index >= GroupCount) return result;

            List<EntityId> group = _groups[index];
            for (int i = group.Count - 1; i >= 0; i--)
            {
                if (!_replica.Knows(group[i])) { group.RemoveAt(i); continue; }
            }
            result.AddRange(group);
            return result;
        }

        /// <summary>Removes dead entities from every group. Called once per tick.</summary>
        public void Prune()
        {
            for (int g = 0; g < GroupCount; g++)
            {
                List<EntityId> group = _groups[g];
                for (int i = group.Count - 1; i >= 0; i--)
                    if (!_replica.Knows(group[i])) group.RemoveAt(i);
            }
        }

        /// <summary>Which groups an entity belongs to, for the selection panel's group badges.</summary>
        public int GroupOf(EntityId id)
        {
            for (int g = 0; g < GroupCount; g++)
                if (_groups[g].Contains(id)) return g;
            return -1;
        }
    }
}
