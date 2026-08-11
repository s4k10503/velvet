using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// The arms are read out of the switch expressions themselves rather than off the lines, because these
    /// switches are not written one arm per line — <c>PriorityFor</c> in
    /// <c>StyleRelationalVariantManipulator</c> puts the pattern on one line and its <c>=&gt;</c> on the
    /// next, and a catch-all written in that style is what a line reading misses. Comment and string
    /// content is blanked first, so a catch-all quoted in either is not one.
    /// <para/>
    /// A region opened by a file-scoped suppression runs to the end of the file, so an unrelated switch
    /// expression inside it is read as well. Scoping the suppression to the switch it means is the answer
    /// to that, and it is what every region here already does.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class ExhaustiveSwitchSeverityTests
    {
        private const string PackageRoot = "Packages/com.velvet.core";
        private const string Severity = "-warnaserror:CS8509";

        private static readonly Regex PragmaDirective = new(
            @"^[ \t]*#pragma\s+warning\s+(?<kind>disable|restore)(?<codes>[^\r\n]*)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // `_` and `var x` are catch-alls outright; two bare identifiers are a type pattern (`StyleVariantKind
        // other =>`), which is what someone writes to get the offending value in scope. A constant arm carries
        // a '.' in its single token, so it does not match. Anchored on the arm list's opening brace or a
        // comma, so it reads patterns rather than anything else an arm's body contains.
        private static readonly Regex CatchAllArm = new(
            @"[{,]\s*(_|var\s+[A-Za-z_]\w*|[A-Za-z_][\w.]*\s+[A-Za-z_]\w*)\s*(=>|when\b)",
            RegexOptions.Compiled);

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

        /// <summary>
        /// The source with comment and string-literal content replaced by spaces, same length and same
        /// newlines. Every index below is therefore an index into the original too.
        /// </summary>
        // One alternation scanned left to right, so whichever of the five opens first at a position claims
        // the run: a `//` inside a string belongs to the string, and a quote inside a comment to the comment.
        private static readonly Regex CommentOrLiteral = new(
            @"//[^\r\n]*|/\*.*?\*/|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static string Redacted(string source)
        {
            var blanked = new StringBuilder(source);
            foreach (Match run in CommentOrLiteral.Matches(source))
            {
                for (var i = run.Index; i < run.Index + run.Length; i++)
                {
                    blanked[i] = source[i] == '\n' || source[i] == '\r' ? source[i] : ' ';
                }
            }

            return blanked.ToString();
        }

        /// <summary>
        /// The brace-delimited arm list of each switch EXPRESSION in <paramref name="redacted"/>. A switch
        /// statement is `switch (` and never reaches the brace test, so it is not one of these.
        /// </summary>
        private static IEnumerable<(int Start, int End)> SwitchArmLists(string redacted)
        {
            foreach (Match keyword in Regex.Matches(redacted, @"\bswitch\b"))
            {
                var i = keyword.Index + keyword.Length;
                while (i < redacted.Length && char.IsWhiteSpace(redacted[i])) { i++; }
                if (i >= redacted.Length || redacted[i] != '{') { continue; }

                var depth = 0;
                for (var j = i; j < redacted.Length; j++)
                {
                    if (redacted[j] == '{') { depth++; }
                    else if (redacted[j] == '}' && --depth == 0) { yield return (i, j); break; }
                }
            }
        }

        private static int LineOf(string text, int index) =>
            text.Take(index).Count(c => c == '\n') + 1;

        /// <summary>
        /// Each region a production source suppresses CS8524 over, as (path, source, 1-based opening line,
        /// start, end).
        /// <para>
        /// Read directive by directive rather than as a disable/restore pair, because a pair requires the
        /// restore to exist: a file-scoped suppression written without one is legal C# and dropped the whole
        /// file out of both cases. An unclosed region runs to end of file; a directive for another code
        /// inside one does not end it. A directive naming no code at all suppresses everything, so it opens
        /// or closes a region like one that names this code.
        /// </para>
        /// </summary>
        private static IEnumerable<(string Path, string Redacted, int Line, int Start, int End)> SuppressedRegions()
        {
            foreach (var path in ProductionSources())
            {
                var redacted = Redacted(File.ReadAllText(path));
                var open = -1;
                var openLine = 0;
                foreach (Match directive in PragmaDirective.Matches(redacted))
                {
                    var codes = directive.Groups["codes"].Value;
                    var named = Regex.Matches(codes, @"\d+").Select(m => m.Value).ToList();
                    if (named.Count > 0 && !named.Contains("8524")) { continue; }

                    if (open >= 0)
                    {
                        yield return (path, redacted, openLine, open, directive.Index);
                        open = -1;
                    }

                    if (directive.Groups["kind"].Value == "disable")
                    {
                        open = directive.Index + directive.Length;
                        openLine = LineOf(redacted, directive.Index);
                    }
                }

                if (open >= 0)
                {
                    yield return (path, redacted, openLine, open, redacted.Length);
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

        // GREEN_ON_BASE(characterization): no switch in these regions carries a catch-all on either side;
        // what the branch adds is the error that makes adding one the tempting answer.
        [Test]
        public void Given_EverySwitchInARegionSuppressingCs8524_When_ItsArmsAreRead_Then_NoneOpensACatchAll()
        {
            // Arrange — this is the escape from the case above: a member added without an arm fails the
            // build, and answering that with a catch-all makes it build and silently classify as whatever
            // the catch-all says.
            var regions = SuppressedRegions().ToList();

            // Act
            var falling = new List<string>();
            foreach (var region in regions)
            {
                foreach (var (start, end) in SwitchArmLists(region.Redacted))
                {
                    if (start < region.Start || end > region.End) { continue; }
                    var arm = CatchAllArm.Match(region.Redacted, start, end - start);
                    if (arm.Success)
                    {
                        falling.Add(Path.GetFileName(region.Path) + ":" + LineOf(region.Redacted, arm.Index));
                    }
                }
            }

            // Assert — the floor is on the regions rather than on the switches, because a region holding no
            // switch expression is a suppression with nothing to suppress and worth seeing as a failure of
            // this reading rather than as agreement.
            Assert.That((regions.Count >= 2, string.Join("\n", falling)), Is.EqualTo((true, string.Empty)));
        }
    }
}
