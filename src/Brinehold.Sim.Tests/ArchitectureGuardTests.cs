using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Brinehold.Sim.Tests
{
    /// <summary>
    /// Enforces the architectural rules the whole design rests on.
    ///
    /// TECHNICAL_ARCHITECTURE.md states these as coding standards; a standard nobody checks is a
    /// standard that erodes. The eventual home for them is a Roslyn analyser that fails the build
    /// (BH0001–BH0003), but a source scan in the test suite catches the same mistakes today at a
    /// fraction of the cost, and it fails loudly with the file and line.
    ///
    /// Each rule exists for a reason a reviewer can check:
    ///   - floating point in the simulation breaks cross-platform determinism, which breaks replays,
    ///     the CI matrix and eventually a live match;
    ///   - a UnityEngine reference outside the client kills the headless server and the fast tests;
    ///   - iterating a hash-based collection lets allocation history leak into game outcomes.
    /// </summary>
    public class ArchitectureGuardTests
    {
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "packages")) &&
                    File.Exists(Path.Combine(directory.FullName, "Brinehold.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("could not locate the repository root");
        }

        private static IEnumerable<(string path, string[] lines)> SourceFiles(params string[] relativeDirectories)
        {
            string root = RepositoryRoot();
            foreach (string relative in relativeDirectories)
            {
                string directory = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;

                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                    if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                    yield return (Path.GetRelativePath(root, file), File.ReadAllLines(file));
                }
            }
        }

        /// <summary>Strips comments and string literals so documentation cannot trip a rule.</summary>
        private static string StripCommentsAndStrings(string line)
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line.Substring(0, comment);
            line = Regex.Replace(line, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");
            return line;
        }

        [Fact]
        public void BH0001_TheSimulationContainsNoFloatingPoint()
        {
            // Integer overloads of Math (Min, Max, Abs, Clamp on int) are deterministic and allowed.
            // These are the members that return or consume floating point.
            var bannedPatterns = new (string pattern, string reason)[]
            {
                (@"\bfloat\b", "float is not deterministic across platforms"),
                (@"\bdouble\b", "double is not deterministic across platforms"),
                (@"\bdecimal\b", "decimal has no place in the tick loop"),
                (@"System\.Math\.(Sqrt|Sin|Cos|Tan|Atan|Atan2|Asin|Acos|Pow|Exp|Log|Log10|Cbrt|Ceiling|Floor|Round|Truncate)\b",
                    "floating-point Math member — use FixMath"),
                (@"\bMathF\.", "MathF is floating point"),
                (@"\bMathf\.", "UnityEngine.Mathf is floating point"),
                (@"\bnew System\.Random\b", "System.Random is not reproducible — use DeterministicRandom"),
                (@"\bnew Random\(", "use DeterministicRandom"),
                (@"DateTime\.(Now|UtcNow)", "wall-clock time is not reproducible — the tick is the only clock"),
                (@"Environment\.TickCount", "wall-clock time is not reproducible"),
                (@"Stopwatch", "wall-clock time is not reproducible inside the simulation"),
                (@"Guid\.NewGuid", "not reproducible")
            };

            var violations = new List<string>();

            foreach ((string path, string[] lines) in SourceFiles("packages/com.brinehold.sim/Runtime"))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripCommentsAndStrings(lines[i]);
                    if (code.Contains("BH0001-allow")) continue;

                    foreach ((string pattern, string reason) in bannedPatterns)
                        if (Regex.IsMatch(code, pattern))
                            violations.Add($"{path}:{i + 1}  {reason}  →  {lines[i].Trim()}");
                }
            }

            Assert.True(violations.Count == 0,
                "Floating point or non-reproducible state found in the simulation:\n  " +
                string.Join("\n  ", violations));
        }

        [Fact]
        public void BH0002_NothingOutsideTheUnityClientReferencesUnityEngine()
        {
            var violations = new List<string>();

            foreach ((string path, string[] lines) in SourceFiles(
                         "packages/com.brinehold.core/Runtime",
                         "packages/com.brinehold.sim/Runtime",
                         "packages/com.brinehold.content/Runtime",
                         "packages/com.brinehold.protocol/Runtime",
                         "packages/com.brinehold.net/Runtime",
                         "packages/com.brinehold.client/Runtime",
                         "src"))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripCommentsAndStrings(lines[i]);
                    if (Regex.IsMatch(code, @"\bUnityEngine\b"))
                        violations.Add($"{path}:{i + 1}  →  {lines[i].Trim()}");
                }
            }

            Assert.True(violations.Count == 0,
                "A UnityEngine reference outside the Unity client would end the headless server and " +
                "the sub-second test suite:\n  " + string.Join("\n  ", violations));
        }

        [Fact]
        public void BH0003_TheSimulationNeverIteratesAHashBasedCollection()
        {
            var violations = new List<string>();

            foreach ((string path, string[] lines) in SourceFiles("packages/com.brinehold.sim/Runtime"))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripCommentsAndStrings(lines[i]);
                    if (!code.Contains("foreach")) continue;
                    if (code.Contains("BH0003-allow")) continue;

                    // Look back a little for what is being iterated.
                    string context = string.Join(" ", lines.Skip(Math.Max(0, i - 12)).Take(13));
                    if (Regex.IsMatch(context, @"\b(Dictionary|HashSet)<"))
                        violations.Add($"{path}:{i + 1}  →  {lines[i].Trim()}");
                }
            }

            Assert.True(violations.Count == 0,
                "Iterating a hash-based collection lets allocation history leak into game outcomes:\n  " +
                string.Join("\n  ", violations));
        }

        /// <summary>
        /// Windows PowerShell 5.1 reads a .ps1 without a byte-order mark as Windows-1252, so a UTF-8
        /// character decodes into several bytes of mojibake. An em dash is the nastiest: its final
        /// byte, 0x94, is a smart closing quote, which silently terminates a string and produces a
        /// parse error pointing at a line twenty further down. This cost a round trip once; the
        /// rule is that developer scripts stay pure ASCII.
        /// </summary>
        [Fact]
        public void PowerShellScriptsArePureAscii()
        {
            string root = RepositoryRoot();
            var violations = new List<string>();

            foreach (string file in Directory.GetFiles(root, "*.ps1", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}")) continue;

                byte[] bytes = File.ReadAllBytes(file);
                int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

                for (int i = start; i < bytes.Length; i++)
                {
                    if (bytes[i] < 128) continue;
                    violations.Add($"{Path.GetRelativePath(root, file)}: non-ASCII byte 0x{bytes[i]:X2} at offset {i}");
                    break;
                }
            }

            Assert.True(violations.Count == 0,
                "PowerShell scripts must be pure ASCII:\n  " + string.Join("\n  ", violations));
        }

        [Fact]
        public void TheSimulationAssemblyDependsOnlyOnCore()
        {
            string[] referenced = typeof(Brinehold.Sim.World.SimWorld).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => n.StartsWith("Brinehold", StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(new[] { "Brinehold.Core" }, referenced);
        }

        [Fact]
        public void TheCoreAssemblyDependsOnNothingOfOurs()
        {
            string[] referenced = typeof(Brinehold.Core.Math.Fix64).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => n.StartsWith("Brinehold", StringComparison.Ordinal))
                .ToArray();

            Assert.Empty(referenced);
        }

        [Fact]
        public void EverySimulationSystemIsCoveredByTheDeclaredSchedule()
        {
            // The tick order is the simulation's contract. A system that exists but is never
            // scheduled is dead code; one that is scheduled twice would double-apply its effects.
            var systemTypes = typeof(Brinehold.Sim.Systems.ISimSystem).Assembly
                .GetTypes()
                .Where(t => typeof(Brinehold.Sim.Systems.ISimSystem).IsAssignableFrom(t))
                .Where(t => t is { IsInterface: false, IsAbstract: false })
                .ToArray();

            var world = new Brinehold.Sim.World.SimWorld(Brinehold.Sim.World.MatchConfig.TwoPlayer());
            var scheduleField = typeof(Brinehold.Sim.World.SimWorld)
                .GetField("_systems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(scheduleField);

            var scheduled = (Brinehold.Sim.Systems.ISimSystem[])scheduleField!.GetValue(world)!;
            Type[] scheduledTypes = scheduled.Select(s => s.GetType()).ToArray();

            Assert.Equal(scheduledTypes.Length, scheduledTypes.Distinct().Count());
            foreach (Type type in systemTypes)
                Assert.Contains(type, scheduledTypes);
        }
    }
}
