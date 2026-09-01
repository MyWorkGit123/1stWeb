namespace Brinehold.Sim.World
{
    public enum VictoryCondition : byte
    {
        /// <summary>Strike the Colours: eliminate every rival settlement core.</summary>
        StrikeTheColours = 0
    }

    /// <summary>
    /// Everything needed to start a match. This is hashed into the replay header and checked at
    /// handshake: two clients that disagree about the config cannot join the same match.
    /// </summary>
    public sealed class MatchConfig
    {
        public ulong Seed = 1;
        public int PlayerCount = 2;
        public int MapWidth = 160;
        public int MapHeight = 160;
        public VictoryCondition Victory = VictoryCondition.StrikeTheColours;

        /// <summary>Team per player slot. Distinct values mean hostile.</summary>
        public byte[] Teams = { 0, 1 };
        public string[] PlayerNames = { "Player 1", "Player 2" };

        /// <summary>
        /// The ruleset this match runs. Null means the shipped default. Its fingerprint is part of
        /// the handshake, so a client with edited content is refused rather than quietly playing a
        /// different game from everyone else.
        /// </summary>
        public Brinehold.Sim.Content.ContentDatabase? Content;

        public static MatchConfig TwoPlayer(ulong seed = 1) => new MatchConfig
        {
            Seed = seed,
            PlayerCount = 2,
            Teams = new byte[] { 0, 1 },
            PlayerNames = new[] { "Player 1", "Player 2" }
        };

        public ulong ContentHash()
        {
            var hash = Brinehold.Core.Collections.StateHash.Create();
            hash.Add(Seed);
            hash.Add(PlayerCount);
            hash.Add(MapWidth);
            hash.Add(MapHeight);
            hash.Add((int)Victory);
            for (int i = 0; i < PlayerCount && i < Teams.Length; i++) hash.Add((int)Teams[i]);
            hash.Add((Content ?? Brinehold.Sim.Content.PrototypeContent.Default).ContentHash());
            return hash.Value;
        }
    }
}
