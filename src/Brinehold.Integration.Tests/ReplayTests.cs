using System.Linq;
using Brinehold.Core.Collections;
using Brinehold.Net.Transport;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Map;
using Brinehold.Sim.Replay;
using Brinehold.Sim.World;
using Xunit;

namespace Brinehold.Integration.Tests
{
    /// <summary>
    /// Replays are the project's determinism proof and its bug-reproduction mechanism.
    ///
    /// These tests play a match, record it, replay the recording, and require the reproduction to
    /// match the original at every checkpoint. If that ever stops being true, either the simulation
    /// has become non-deterministic or the recording is incomplete — both are release blockers, so
    /// this runs on every commit rather than nightly.
    /// </summary>
    public class ReplayTests
    {
        /// <summary>Plays a scripted opening so the recording exercises most of the simulation.</summary>
        private static MatchHarness PlayScriptedMatch(int ticks)
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);

            var w0 = harness.UnitsOf(0, EntityKind.Worker);
            var w1 = harness.UnitsOf(1, EntityKind.Worker);
            EntityId forest0 = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[w0[0].Index], ResourceNodeType.Forest);
            EntityId forest1 = PrototypeMap.FindNearestNode(harness.World, harness.World.Entities.Position[w1[0].Index], ResourceNodeType.Forest);

            harness.Clients[0].Send(Command.Harvest(0, 0, w0.Take(6).ToArray(), forest0));
            harness.Clients[0].Send(Command.Build(0, 0, w0.Skip(6).ToArray(), BuildingType.House,
                PrototypeMap.StartCellX[0] + 10, PrototypeMap.StartCellY[0] - 8));
            harness.Clients[1].Send(Command.Harvest(1, 0, w1.ToArray(), forest1));

            for (int t = 0; t < ticks; t++)
            {
                if (t == 600)
                {
                    EntityId core = harness.CoreOf(0);
                    harness.Clients[0].Send(Command.Train(0, 0, core, EntityKind.Worker));
                }
                if (t == 900)
                {
                    harness.Clients[1].Send(Command.Move(1, 0, w1.Take(3).ToArray(),
                        PrototypeMap.StartCellX[1] - 18, PrototypeMap.StartCellY[1] + 12));
                }
                harness.Tick();
            }

            return harness;
        }

        [Fact]
        public void ARecordedMatchReplaysToTheSameStateHashes()
        {
            MatchHarness harness = PlayScriptedMatch(1400);

            Assert.True(harness.Host.Replay.CommandCount >= 4, "the script recorded no commands");
            Assert.True(harness.Host.Replay.CheckpointCount >= 5, "no state checkpoints were recorded");

            byte[] bytes = harness.Host.Replay.ToArray();
            Assert.True(ReplayData.TryParse(bytes, out ReplayData data, out string error), error);

            var player = new ReplayPlayer(data);
            bool reproduced = player.Verify();

            Assert.True(reproduced,
                "the replay diverged from the recorded match: " +
                string.Join("; ", player.Divergences.Select(d => d.ToString())));
            Assert.Equal(harness.World.ComputeStateHash(), player.World.ComputeStateHash());
        }

        [Fact]
        public void TheReplayReproducesTheEconomyExactly()
        {
            MatchHarness harness = PlayScriptedMatch(1400);
            ReplayData.TryParse(harness.Host.Replay.ToArray(), out ReplayData data, out _);

            var player = new ReplayPlayer(data);
            player.Verify();

            for (int p = 0; p < 2; p++)
            {
                Assert.Equal(harness.World.Players[p].Wood, player.World.Players[p].Wood);
                Assert.Equal(harness.World.Players[p].Food, player.World.Players[p].Food);
                Assert.Equal(harness.World.Players[p].PopulationUsed, player.World.Players[p].PopulationUsed);
                Assert.Equal(harness.World.Players[p].PopulationCap, player.World.Players[p].PopulationCap);
            }

            Assert.True(harness.World.Players[0].Wood > 200, "the scripted match gathered nothing, so it proves little");
        }

        [Fact]
        public void ReplayingTwiceGivesIdenticalResults()
        {
            MatchHarness harness = PlayScriptedMatch(900);
            byte[] bytes = harness.Host.Replay.ToArray();

            ReplayData.TryParse(bytes, out ReplayData first, out _);
            ReplayData.TryParse(bytes, out ReplayData second, out _);

            var a = new ReplayPlayer(first);
            var b = new ReplayPlayer(second);
            a.Verify();
            b.Verify();

            Assert.Equal(a.World.ComputeStateHash(), b.World.ComputeStateHash());
            Assert.Empty(a.Divergences);
            Assert.Empty(b.Divergences);
        }

        [Fact]
        public void ACorruptedCheckpointIsDetected()
        {
            MatchHarness harness = PlayScriptedMatch(900);
            ReplayData.TryParse(harness.Host.Replay.ToArray(), out ReplayData data, out _);

            Assert.NotEmpty(data.Checkpoints);

            // Falsify one checkpoint: playback must notice, and must name the tick it happened on.
            ReplayCheckpoint corrupted = data.Checkpoints[1];
            uint corruptedTick = corrupted.Tick;
            corrupted.Hash ^= 0xDEADBEEF;
            data.Checkpoints[1] = corrupted;

            var player = new ReplayPlayer(data);
            bool reproduced = player.Verify();

            Assert.False(reproduced, "a falsified checkpoint was not detected");
            Assert.Contains(player.Divergences, d => d.Tick == corruptedTick);
        }

        [Fact]
        public void DroppingACommandChangesTheOutcomeAndIsDetected()
        {
            MatchHarness harness = PlayScriptedMatch(1200);
            ReplayData.TryParse(harness.Host.Replay.ToArray(), out ReplayData data, out _);

            // Remove the harvest order. The economy must then diverge from the recording, which is
            // the property that makes the checkpoints worth writing at all.
            int removed = data.Commands.FindIndex(c => c.Command.Type == CommandType.Harvest);
            Assert.True(removed >= 0);
            data.Commands.RemoveAt(removed);

            var player = new ReplayPlayer(data);
            bool reproduced = player.Verify();

            Assert.False(reproduced, "removing a command did not change the outcome");
        }

        [Fact]
        public void AReplayIsSmall()
        {
            MatchHarness harness = PlayScriptedMatch(2400);   // two minutes of match time
            int bytes = harness.Host.Replay.ByteLength;

            // Commands and checkpoints only: a couple of kilobytes for two minutes, which is what
            // makes attaching a replay to a bug report reasonable.
            Assert.True(bytes < 4096, $"the replay was {bytes} bytes for two minutes of play");
            Assert.True(bytes > 100, "the replay is suspiciously empty");
        }

        [Fact]
        public void TheHeaderRoundTripsTheMatchConfiguration()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect, players: 2, seed: 424242);
            harness.Tick(220);

            ReplayData.TryParse(harness.Host.Replay.ToArray(), out ReplayData data, out string error);
            Assert.True(string.IsNullOrEmpty(error), error);

            Assert.Equal(424242UL, data.Header.Seed);
            Assert.Equal(2, data.Header.PlayerCount);
            Assert.Equal(harness.Host.Config.ContentHash(), data.Header.ContentHash);
            Assert.Equal(harness.Host.Config.MapWidth, data.Header.MapWidth);
        }

        [Fact]
        public void AMatchPlayedToVictoryRecordsItsResult()
        {
            var harness = new MatchHarness(NetworkConditions.Perfect);
            EntityId enemyCore = harness.CoreOf(1);

            Brinehold.Core.Math.Fix2 near = harness.World.Entities.Position[enemyCore.Index]
                + new Brinehold.Core.Math.Fix2(Brinehold.Core.Math.Fix64.FromInt(4), Brinehold.Core.Math.Fix64.Zero);
            var raiders = new EntityId[8];
            for (int i = 0; i < raiders.Length; i++)
                raiders[i] = harness.World.SpawnUnit(EntityKind.Soldier, 0,
                    near + new Brinehold.Core.Math.Fix2(Brinehold.Core.Math.Fix64.FromInt(i), Brinehold.Core.Math.Fix64.Zero));

            harness.Tick(2);
            harness.Clients[0].Send(Command.Attack(0, 0, raiders, enemyCore));
            for (int t = 0; t < 4000 && !harness.World.MatchOver; t++) harness.Tick();

            Assert.True(harness.World.MatchOver);

            // The tick the match actually ended on. The world keeps stepping afterwards — a real
            // server stops its loop, but the harness does not, and the replay must record the
            // moment of victory rather than whenever recording happened to be inspected.
            uint tickAtVictory = harness.World.Tick;
            harness.Tick(2);

            ReplayData.TryParse(harness.Host.Replay.ToArray(), out ReplayData data, out _);
            Assert.True(data.HasEnd, "the replay did not record the end of the match");
            Assert.Equal(harness.World.WinningTeam, data.WinningTeam);
            Assert.Equal(tickAtVictory, data.EndTick);
        }

        [Fact]
        public void ATruncatedReplayStillParsesWhatItRecorded()
        {
            MatchHarness harness = PlayScriptedMatch(900);
            byte[] full = harness.Host.Replay.ToArray();

            // Simulate a server that crashed mid-write.
            var truncated = new byte[full.Length * 2 / 3];
            System.Array.Copy(full, truncated, truncated.Length);

            Assert.True(ReplayData.TryParse(truncated, out ReplayData data, out string error), error);
            Assert.NotEmpty(data.Commands);
            Assert.False(data.HasEnd);

            // And it still replays as far as it goes, without throwing.
            var player = new ReplayPlayer(data);
            player.Verify();
            Assert.True(player.World.Tick > 0);
        }

        [Fact]
        public void GarbageIsRejectedRatherThanCrashing()
        {
            var rng = new System.Random(1234);
            for (int i = 0; i < 100; i++)
            {
                var junk = new byte[rng.Next(0, 500)];
                rng.NextBytes(junk);
                ReplayData.TryParse(junk, out _, out _);   // must not throw
            }
        }
    }
}
