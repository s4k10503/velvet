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
    /// covers, and a catch-all arm silences that in turn. So the suppression is held to an assembly whose
    /// <c>csc.rsp</c> raises CS8509 to an error, and to opening no arm for a new member to fall into.
    /// <para>
    /// The second half reaches a catch-all written on its own line, which is how every arm in these
    /// switches is written. An arm sharing a line with the one before it is not reached.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class ExhaustiveSwitchSeverityTests
    {
        private const string PackageRoot = "Packages/com.velvet.core";
        private const string Severity = "-warnaserror:CS8509";
        private const string SuppressedCode = "CS8524";

        private static readonly Regex PragmaDirective = new(
            @"^\s*#pragma\s+warning\s+(?<kind>disable|restore)\s+(?<codes>.*)$", RegexOptions.Compiled);

        // `_` and `var x` are catch-alls outright; two bare identifiers are a type pattern (`StyleVariantKind
        // other =>`), which is what someone writes to get the offending value in scope. A constant arm carries
        // a '.' in its single token, so it does not match.
        private static readonly Regex CatchAllArm = new(
            @"^\s*(_|var\s+[A-Za-z_]\w*|[A-Za-z_][\w.]*\s+[A-Za-z_]\w*)\s*(=>|when\b)", RegexOptions.Compiled);

        // Test material is out of scope and deliberately carries no severity flag. TestUtilities is named
        // because it holds no `/Tests/` segment while being test material by every other reckoning —
        // base_red_check.py's `is_test_side` counts it by name for the same reason.
        private static bool IsProduction(string path) =>
            !path.Contains("/Tests/", StringComparison.Ordinal)
            && !path.Contains("/TestUtilities/", StringComparison.Ordinal)
            && !path.Contains("Generators~", StringComparison.Ordinal)
            && !path.Contains("Samples~", StringComparison.Ordinal);

        private static IEnumerable<string> ProductionSources() =>
            Directory.EnumerateFiles(Path.GetFullPath(PackageRoot), "*.cs", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .Where(IsProduction)
                .OrderBy(path => path, StringComparer.Ordinal);

        private static string Codes(Match directive)
        {
            var codes = directive.Groups["codes"].Value;
            var comment = codes.IndexOf("//", StringComparison.Ordinal);
            return comment < 0 ? codes : codes.Substring(0, comment);
        }

        /// <summary>
        /// Each region a production source suppresses <see cref="SuppressedCode"/> over, as (path, 1-based
        /// opening line, body).
        /// <para>
        /// Read line by line rather than as a disable/restore pair, because a pair requires the restore to
        /// exist: a file-scoped suppression written without one is legal C# and dropped the whole file out
        /// of both cases. An unclosed region runs to end of file; a directive for another code inside one
        /// does not end it.
        /// </para>
        /// </summary>
        private static IEnumerable<(string Path, int Line, string Body)> SuppressedRegions()
        {
            foreach (var path in ProductionSources())
            {
                var lines = File.ReadAllLines(path);
                var open = -1;
                for (var i = 0; i < lines.Length; i++)
                {
                    var directive = PragmaDirective.Match(lines[i]);
                    if (!directive.Success || !Codes(directive).Contains(SuppressedCode, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (open >= 0)
                    {
                        yield return (path, open + 1, string.Join("\n", lines.Skip(open + 1).Take(i - open - 1)));
                        open = -1;
                    }

                    if (directive.Groups["kind"].Value == "disable")
                    {
                        open = i;
                    }
                }

                if (open >= 0)
                {
                    yield return (path, open + 1, string.Join("\n", lines.Skip(open + 1)));
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
            var suppressing = SuppressedRegions().Select(region => region.Path).Distinct().ToList();

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

        // GREEN_ON_BASE(characterization): no such region carries a catch-all on either side; what the
        // branch adds is the error that makes adding one the tempting answer.
        [Test]
        public void Given_EveryRegionSuppressingCs8524_When_ItsArmsAreRead_Then_NoneOpensACatchAll()
        {
            // Arrange — this is the escape from the case above: a member added without an arm fails the
            // build, and answering that with a catch-all makes it build and silently classify as whatever
            // the catch-all says.
            var regions = SuppressedRegions().ToList();

            // Act
            var falling = regions
                .Where(region => region.Body.Split('\n').Any(line => CatchAllArm.IsMatch(line)))
                .Select(region => Path.GetFileName(region.Path) + ":" + region.Line)
                .ToList();

            // Assert — same floor, for the same reason.
            Assert.That((regions.Count >= 2, string.Join("\n", falling)), Is.EqualTo((true, string.Empty)));
        }
    }
}
