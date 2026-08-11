using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the two halves of what suppressing CS8524 declares. Suppressing it says the arms cover the
    /// enum and the only uncovered input is a cast; CS8509 is then the sole report of a member no arm
    /// covers, and a discard arm silences that in turn. So the suppression is held to an assembly whose
    /// <c>csc.rsp</c> raises CS8509 to an error, and to having no discard arm to fall into.
    /// </summary>
    [TestFixture]
    internal sealed class ExhaustiveSwitchSeverityTests
    {
        private const string PackageRoot = "Packages/com.velvet.core";
        private const string Severity = "-warnaserror:CS8509";
        private const string SuppressedCode = "CS8524";

        // Matched rather than searched for as a literal, so a site suppressing a second code alongside —
        // `disable CS8524, CS0618`, the natural spelling once one is needed — stays in scope instead of
        // dropping out while the other sites keep the set non-empty.
        private static readonly Regex SuppressedBlock = new(
            @"#pragma\s+warning\s+disable\s+(?<codes>[^\r\n]*)\r?\n(?<body>.*?)#pragma\s+warning\s+restore",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex DiscardArm = new(@"^\s*_\s*(=>|when\b)", RegexOptions.Compiled);

        // Test material is out of scope and deliberately carries no severity flag. TestUtilities is named
        // because it holds no `/Tests/` segment while being test material by every other reckoning —
        // base_red_check.py's `is_test_side` counts it by name for the same reason. Skipping them is also
        // what keeps this file, which spells out the pragma it looks for, from matching itself.
        private static bool IsProduction(string path) =>
            !path.Contains("/Tests/", StringComparison.Ordinal)
            && !path.Contains("/TestUtilities/", StringComparison.Ordinal)
            && !path.Contains("Generators~", StringComparison.Ordinal)
            && !path.Contains("Samples~", StringComparison.Ordinal);

        private static IEnumerable<(string File, Match Block)> SuppressingBlocks()
        {
            foreach (var path in Directory
                         .EnumerateFiles(Path.GetFullPath(PackageRoot), "*.cs", SearchOption.AllDirectories)
                         .Select(path => path.Replace('\\', '/'))
                         .Where(IsProduction)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                foreach (Match block in SuppressedBlock.Matches(File.ReadAllText(path)))
                {
                    var codes = block.Groups["codes"].Value;
                    var comment = codes.IndexOf("//", StringComparison.Ordinal);
                    if ((comment < 0 ? codes : codes.Substring(0, comment)).Contains(SuppressedCode, StringComparison.Ordinal))
                    {
                        yield return (Path.GetFileName(path), block);
                    }
                }
            }
        }

        private static string? AssemblyDirectoryOf(string file)
        {
            var package = Path.GetFullPath(PackageRoot);
            for (var dir = Directory.GetParent(file); dir != null; dir = dir.Parent)
            {
                if (Directory.EnumerateFiles(dir.FullName, "*.asmdef").Any())
                {
                    return dir.FullName;
                }
                if (string.Equals(dir.FullName, package, StringComparison.Ordinal))
                {
                    break;
                }
            }
            return null;
        }

        [Test]
        public void Given_EveryProductionSourceSuppressingCs8524_When_ItsAssemblyIsAsked_Then_Cs8509IsAnError()
        {
            // Arrange
            var suppressing = Directory
                .EnumerateFiles(Path.GetFullPath(PackageRoot), "*.cs", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .Where(IsProduction)
                .Where(path => SuppressedBlock.Matches(File.ReadAllText(path))
                    .Any(block => block.Groups["codes"].Value.Contains(SuppressedCode, StringComparison.Ordinal)))
                .ToList();

            // Act
            var unguarded = new List<string>();
            foreach (var file in suppressing)
            {
                var assembly = AssemblyDirectoryOf(file);
                var rsp = assembly == null ? null : Path.Combine(assembly, "csc.rsp");
                if (rsp == null || !File.Exists(rsp)
                    || !File.ReadAllText(rsp).Contains(Severity, StringComparison.Ordinal))
                {
                    unguarded.Add(Path.GetFileName(file));
                }
            }

            // Assert — the floor rides along because a package root that read empty reports nothing
            // unguarded and would pass having measured nothing.
            Assert.That((suppressing.Count >= 2, string.Join("\n", unguarded)), Is.EqualTo((true, string.Empty)));
        }

        // GREEN_ON_BASE(characterization): no such block carries a discard arm on either side; what the
        // branch adds is the error that makes adding one the tempting answer.
        [Test]
        public void Given_EveryBlockSuppressingCs8524_When_ItsArmsAreRead_Then_NoneFallsToADiscard()
        {
            // Arrange — this is the escape from the case above: a member added without an arm fails the
            // build, and answering that with a discard arm makes it build and silently classify as
            // whatever the discard says.
            var blocks = SuppressingBlocks().ToList();

            // Act
            var falling = blocks
                .Where(entry => entry.Block.Groups["body"].Value
                    .Split('\n')
                    .Any(line => DiscardArm.IsMatch(line)))
                .Select(entry => entry.File)
                .ToList();

            // Assert — same floor, for the same reason.
            Assert.That((blocks.Count >= 2, string.Join("\n", falling)), Is.EqualTo((true, string.Empty)));
        }
    }
}
