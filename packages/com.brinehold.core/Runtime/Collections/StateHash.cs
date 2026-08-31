namespace Brinehold.Core.Collections
{
    /// <summary>
    /// Incremental FNV-1a 64-bit hash used to fingerprint simulation state.
    ///
    /// The server writes one of these into the replay every 200 ticks; CI re-simulates the replay on
    /// three platforms and requires identical values at every checkpoint. Feed fields in a fixed
    /// order — the hash is order-sensitive, which is the point.
    /// </summary>
    public struct StateHash
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _value;

        public static StateHash Create() => new StateHash { _value = Offset };

        public ulong Value => _value;

        public void Add(byte b)
        {
            _value ^= b;
            _value *= Prime;
        }

        public void Add(int v)
        {
            Add((byte)v); Add((byte)(v >> 8)); Add((byte)(v >> 16)); Add((byte)(v >> 24));
        }

        public void Add(uint v) => Add(unchecked((int)v));

        public void Add(long v)
        {
            Add(unchecked((int)v));
            Add(unchecked((int)(v >> 32)));
        }

        public void Add(ulong v) => Add(unchecked((long)v));

        public void Add(bool v) => Add((byte)(v ? 1 : 0));

        public void Add(Brinehold.Core.Math.Fix64 v) => Add(v.Raw);

        public void Add(Brinehold.Core.Math.Fix2 v) { Add(v.X.Raw); Add(v.Y.Raw); }

        public void Add(EntityId v) => Add(v.Raw);
    }
}
