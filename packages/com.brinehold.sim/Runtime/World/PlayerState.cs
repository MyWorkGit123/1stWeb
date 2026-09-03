using Brinehold.Core.Collections;
using Brinehold.Sim.World;

namespace Brinehold.Sim.World
{
    /// <summary>
    /// Per-player state owned exclusively by the server.
    ///
    /// The client holds a mirror of its own PlayerState only, delivered as replication tier D, and
    /// never sees another player's. Every field here is authoritative: a client that edits its copy
    /// changes nothing but its own display, and the next tier-D delta overwrites it.
    /// </summary>
    public sealed class PlayerState
    {
        public readonly byte PlayerId;
        public string Name;

        /// <summary>Indexed by <see cref="ResourceType"/>.</summary>
        public readonly int[] Resources = new int[SimConstants.ResourceTypeCount];

        public int PopulationUsed;
        public int PopulationCap;

        public bool Defeated;
        /// <summary>Set when this player has met a victory condition.</summary>
        public bool Victorious;

        /// <summary>Team number. Players on the same team never take friendly fire.</summary>
        public byte Team;

        public PlayerState(byte playerId, string name, byte team)
        {
            PlayerId = playerId;
            Name = name;
            Team = team;
            PopulationCap = 0;
        }

        public int Wood { get => Resources[(int)ResourceType.Wood]; set => Resources[(int)ResourceType.Wood] = value; }
        public int Food { get => Resources[(int)ResourceType.Food]; set => Resources[(int)ResourceType.Food] = value; }
        public int Stone { get => Resources[(int)ResourceType.Stone]; set => Resources[(int)ResourceType.Stone] = value; }
        public int Coin { get => Resources[(int)ResourceType.Coin]; set => Resources[(int)ResourceType.Coin] = value; }

        public bool CanAfford(int wood, int food, int stone, int coin)
            => Wood >= wood && Food >= food && Stone >= stone && Coin >= coin;

        public void Spend(int wood, int food, int stone, int coin)
        {
            Wood -= wood; Food -= food; Stone -= stone; Coin -= coin;
        }

        public void Refund(int wood, int food, int stone, int coin)
        {
            Wood += wood; Food += food; Stone += stone; Coin += coin;
        }

        public void Add(ResourceType type, int amount) => Resources[(int)type] += amount;
    }
}
