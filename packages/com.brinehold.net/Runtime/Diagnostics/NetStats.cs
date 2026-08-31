namespace Brinehold.Net
{
    /// <summary>
    /// Per-player traffic counters, broken down by replication tier.
    ///
    /// These are not decoration: the prototype's acceptance criteria are stated in bytes per second
    /// and in "no per-frame transform replication", and this is what measures them. The netgraph
    /// overlay in the client reads the same numbers.
    /// </summary>
    public sealed class NetStats
    {
        public enum Category
        {
            Lifecycle = 0,
            Intent = 1,
            Correction = 2,
            Private = 3,
            Event = 4
        }

        private const int CategoryCount = 5;

        private readonly int[] _messageCounts = new int[8 * CategoryCount];
        private readonly long[] _bytes = new long[8];
        private readonly int[] _packets = new int[8];

        public void Record(int player, Category category, int messages)
        {
            if (player < 0 || player >= 8) return;
            _messageCounts[player * CategoryCount + (int)category] += messages;
        }

        public void RecordPacket(int player, int bytes)
        {
            if (player < 0 || player >= 8) return;
            _bytes[player] += bytes;
            _packets[player]++;
        }

        public int MessageCount(int player, Category category) => _messageCounts[player * CategoryCount + (int)category];

        public long TotalBytes(int player) => _bytes[player];

        public int PacketCount(int player) => _packets[player];

        /// <summary>Average bytes per second for a player, given how many ticks have elapsed.</summary>
        public double BytesPerSecond(int player, uint ticks)
            => ticks == 0 ? 0 : _bytes[player] / (ticks / (double)Brinehold.Sim.World.SimConstants.TicksPerSecond);

        public void Reset()
        {
            System.Array.Clear(_messageCounts, 0, _messageCounts.Length);
            System.Array.Clear(_bytes, 0, _bytes.Length);
            System.Array.Clear(_packets, 0, _packets.Length);
        }

        public string Summary(int player, uint ticks)
            => $"player {player}: {TotalBytes(player)} B over {ticks} ticks " +
               $"({BytesPerSecond(player, ticks):0.0} B/s) — " +
               $"lifecycle {MessageCount(player, Category.Lifecycle)}, " +
               $"intent {MessageCount(player, Category.Intent)}, " +
               $"correction {MessageCount(player, Category.Correction)}, " +
               $"private {MessageCount(player, Category.Private)}, " +
               $"event {MessageCount(player, Category.Event)}";
    }
}
