using System;

namespace Brinehold.Core.Collections
{
    /// <summary>
    /// A 24-bit dense index plus an 8-bit generation counter, packed into a uint.
    ///
    /// The generation makes stale references detectable: when an entity dies its slot is recycled
    /// with an incremented generation, so a command that names a dead entity is rejected rather
    /// than silently acting on whoever took its place. That check is part of the anti-cheat surface,
    /// not just a safety net.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public const int MaxIndex = 0xFFFFFF;

        public readonly uint Raw;

        public EntityId(uint raw) => Raw = raw;

        public EntityId(int index, byte generation)
        {
            if ((uint)index > MaxIndex) throw new ArgumentOutOfRangeException(nameof(index));
            Raw = ((uint)index & 0xFFFFFF) | ((uint)generation << 24);
        }

        public static EntityId None => new EntityId(0u);

        public int Index => (int)(Raw & 0xFFFFFF);
        public byte Generation => (byte)(Raw >> 24);
        public bool IsNone => Raw == 0u;

        public bool Equals(EntityId other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is EntityId other && Raw == other.Raw;
        public override int GetHashCode() => (int)Raw;
        public int CompareTo(EntityId other) => Raw.CompareTo(other.Raw);
        public static bool operator ==(EntityId a, EntityId b) => a.Raw == b.Raw;
        public static bool operator !=(EntityId a, EntityId b) => a.Raw != b.Raw;
        public override string ToString() => IsNone ? "E:none" : $"E:{Index}v{Generation}";
    }
}
