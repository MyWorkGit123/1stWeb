using System.IO;
using System.Linq;
using Brinehold.Sim.Replay;
using Xunit;

namespace Brinehold.Integration.Tests
{
    /// <summary>
    /// The determinism corpus.
    ///
    /// Every replay in <c>tests/replays</c> is re-simulated and required to reproduce its recorded
    /// state hashes exactly. CI runs this on Linux, Windows and macOS: because the simulation is
    /// integer-only, all three must agree to the bit. If they ever stop agreeing, floating point or
    /// unordered iteration has crept into the simulation and the merge is blocked.
    ///
    /// The corpus grows over time — one replay per milestone, and one for every determinism bug
    /// ever found, so a fixed bug can never quietly come back.
    /// </summary>
    public class GoldenReplayTests
    {
        /// <summary>Walks up from the test assembly to the repository root.</summary>
        private static string FindReplayDirectory()
        {
            var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "tests", "replays");
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            return string.Empty;
        }

        public static TheoryData<string> Replays()
        {
            var data = new TheoryData<string>();
            string directory = FindReplayDirectory();
            if (string.IsNullOrEmpty(directory)) return data;

            foreach (string file in Directory.GetFiles(directory, "*.brhr").OrderBy(f => f, System.StringComparer.Ordinal))
                data.Add(Path.GetFileName(file));
            return data;
        }

        [Theory]
        [MemberData(nameof(Replays))]
        public void GoldenReplayReproducesExactly(string fileName)
        {
            string path = Path.Combine(FindReplayDirectory(), fileName);
            byte[] bytes = File.ReadAllBytes(path);

            Assert.True(ReplayData.TryParse(bytes, out ReplayData data, out string error), $"{fileName}: {error}");
            Assert.True(data.Checkpoints.Count > 0, $"{fileName} has no checkpoints, so it verifies nothing");

            var player = new ReplayPlayer(data);
            bool reproduced = player.Verify();

            Assert.True(reproduced,
                $"{fileName} did not reproduce: " +
                string.Join("; ", player.Divergences.Select(d => d.ToString())));
        }

        [Fact]
        public void TheCorpusIsNotEmpty()
        {
            string directory = FindReplayDirectory();
            Assert.False(string.IsNullOrEmpty(directory), "could not locate tests/replays");
            Assert.NotEmpty(Directory.GetFiles(directory, "*.brhr"));
        }
    }
}
