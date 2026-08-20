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
    /// <para>
    /// The suppression is not what puts a switch in scope, though — it is what a switch already written
    /// exhaustively adds. A switch that never became exhaustive carries a catch-all and no directive, and
    /// the last cases here are the ones that reach those: over an enum the package declares, a catch-all
    /// answering with a VALUE is the silence, because the member added tomorrow gets that value and the
    /// build stays green. A catch-all that THROWS is left alone. It defeats CS8509 the same way, and the
    /// exemption is deliberate: the wrong answer becomes a stack trace at the first call instead of a
    /// value nobody traces back. The switches whose catch-all still answers with a value are named one by
    /// one in <c>KnownValueAnsweringSites</c>, with what stands in the way of each.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class ExhaustiveSwitchSeverityTests
    {
        private const string PackageRoot = "Packages/com.velvet.core";
        private const string Severity = "-warnaserror:CS8509";

        // The `\r?` is what keeps a CRLF working tree from matching no directive at all and leaving both
        // cases green over nothing: .NET's `$` matches before `\n` and not before `\r\n`. `.gitattributes`
        // pins these sources to LF, so this is belt and braces rather than a state reachable here.
        private static readonly Regex PragmaDirective = new(
            @"^[ \t]*#pragma\s+warning\s+(?<kind>disable|restore)(?<codes>[^\r\n]*)\r?$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // `_` and `var x` are catch-alls outright; two bare identifiers are a type pattern (`StyleVariantKind
        // other =>`), which is what someone writes to get the offending value in scope. A constant arm is one
        // token where this needs two, which is what excludes it; the '.' the alternative allows is there so a
        // qualified type pattern still matches. Anchored on the arm list's opening brace or a comma, so it
        // reads patterns rather than anything else an arm's body contains.
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

        /// <summary>
        /// The simple name of every enum the package's own assemblies declare. Read off the assemblies
        /// rather than off the sources because a name here has to be a type this repository can add a
        /// member to. CS8509 over one of those is a member somebody here forgot to answer; over a UI
        /// Toolkit enum it is an editor upgrade, which no arm written here would have got ahead of.
        /// <para/>
        /// A simple name is the whole of the reading, so a UI Toolkit enum sharing one with a package enum
        /// is read as the package's. That is the direction to be wrong in — a switch reported and named by
        /// hand costs a review, and one not reported costs the guard.
        /// </para>
        /// </summary>
        private static HashSet<string> PackageEnumNames()
        {
            var names = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name is { } name
                    && name.StartsWith("Velvet", StringComparison.Ordinal)
                    && !name.Contains("Test", StringComparison.Ordinal))
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsEnum)
                .Select(type => type.Name);
            return new HashSet<string>(names, StringComparer.Ordinal);
        }

        /// <summary>
        /// The arms of the brace-delimited list at <paramref name="start"/>..<paramref name="end"/>, as
        /// (pattern, body, index of the pattern's first character). The commas and the <c>=&gt;</c> are
        /// read at the list's own nesting depth, so a nested switch expression's arms stay that switch's
        /// and a lambda in a body is not mistaken for the arm's own arrow.
        /// </summary>
        private static IEnumerable<(string Pattern, string Body, int Index)> Arms(
            string redacted, int start, int end)
        {
            var depth = 0;
            var armStart = start + 1;
            for (var i = armStart; i < end; i++)
            {
                var c = redacted[i];
                if (c == '{' || c == '(' || c == '[') { depth++; }
                else if (c == '}' || c == ')' || c == ']') { depth--; }
                else if (c == ',' && depth == 0)
                {
                    if (SplitArm(redacted, armStart, i) is { } arm) { yield return arm; }
                    armStart = i + 1;
                }
            }

            if (SplitArm(redacted, armStart, end) is { } last) { yield return last; }
        }

        private static (string Pattern, string Body, int Index)? SplitArm(string redacted, int from, int to)
        {
            var depth = 0;
            for (var i = from; i + 1 < to; i++)
            {
                var c = redacted[i];
                if (c == '{' || c == '(' || c == '[') { depth++; }
                else if (c == '}' || c == ')' || c == ']') { depth--; }
                else if (c == '=' && redacted[i + 1] == '>' && depth == 0)
                {
                    var head = from;
                    while (head < i && char.IsWhiteSpace(redacted[head])) { head++; }
                    return (redacted.Substring(from, i - from),
                        redacted.Substring(i + 2, to - i - 2), head);
                }
            }

            return null;
        }

        // The catch-all forms, anchored over a whole pattern rather than found inside one: `_`, a `var`
        // designation, and a type pattern (`UILayer other`), which is what someone writes to get the
        // offending value in scope. A `when` clause does not make an arm safe — its guard can be true for
        // the member no arm names — so a pattern is tested with the clause stripped.
        private static readonly Regex WholeCatchAllPattern = new(
            @"^(?:_|var\s+[A-Za-z_]\w*|[A-Za-z_][\w.]*\s+[A-Za-z_]\w*)$", RegexOptions.Compiled);

        // A qualified constant, capturing the segment the member hangs off, so a spelling carrying its
        // namespace resolves to the same name as a bare one.
        private static readonly Regex QualifiedConstant = new(
            @"(?<![\w.])(?:[A-Za-z_]\w*\.)*([A-Za-z_]\w*)\.[A-Za-z_]\w*(?![\w.])", RegexOptions.Compiled);

        private static bool Throws(string body)
        {
            var trimmed = body.TrimStart();
            return trimmed.StartsWith("throw", StringComparison.Ordinal)
                && (trimmed.Length == 5 || !char.IsLetterOrDigit(trimmed[5]));
        }

        /// <summary>
        /// Every catch-all arm answering with a value, in each switch expression of
        /// <paramref name="redacted"/> whose other arms name a member of one of
        /// <paramref name="packageEnums"/>. <c>Examined</c> counts the switches those arms identified, so a
        /// reading that resolved no governing type at all is visible rather than agreeable.
        /// </summary>
        // A pattern holding a brace or a colon is a property or positional pattern, and the constant
        // inside it belongs to a member rather than to the switch's own type — reading it would put a
        // switch over some other type in scope on the strength of one subpattern.
        private static (List<(int Line, string Enum)> Offenders, int Examined) ValueAnsweringCatchAlls(
            string redacted, ICollection<string> packageEnums)
        {
            var offenders = new List<(int, string)>();
            var examined = 0;
            foreach (var (start, end) in SwitchArmLists(redacted))
            {
                string? governing = null;
                var catchAlls = new List<(int Index, string Body)>();
                foreach (var (pattern, body, index) in Arms(redacted, start, end))
                {
                    var clause = pattern.IndexOf(" when ", StringComparison.Ordinal);
                    var bare = string.Join(" ", (clause < 0 ? pattern : pattern.Substring(0, clause))
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    if (WholeCatchAllPattern.IsMatch(bare))
                    {
                        catchAlls.Add((index, body));
                        continue;
                    }
                    if (bare.IndexOf('{') >= 0 || bare.IndexOf(':') >= 0) { continue; }
                    foreach (Match constant in QualifiedConstant.Matches(bare))
                    {
                        if (packageEnums.Contains(constant.Groups[1].Value))
                        {
                            governing = constant.Groups[1].Value;
                        }
                    }
                }

                if (governing == null) { continue; }

                examined++;
                offenders.AddRange(catchAlls
                    .Where(arm => !Throws(arm.Body))
                    .Select(arm => (LineOf(redacted, arm.Index), governing)));
            }

            return (offenders, examined);
        }

        /// <summary>
        /// Every catch-all answering with a value across the sources of an assembly whose <c>csc.rsp</c>
        /// raises CS8509, and the number of switch expressions the reading resolved a governing enum for.
        /// </summary>
        // Held to the assemblies that raise CS8509 because naming the arms of a switch the compiler
        // reports nothing about buys nothing there — the catch-all silences no error.
        private static (List<string> Sites, int Examined) ValueAnsweringSites()
        {
            var enums = PackageEnumNames();
            var sites = new List<string>();
            var examined = 0;
            foreach (var file in ProductionSources())
            {
                var assembly = AssemblyDirectoryOf(file);
                var rsp = assembly == null ? null : Path.Combine(assembly, "csc.rsp");
                if (rsp == null || !File.Exists(rsp)
                    || !File.ReadAllText(rsp).Contains(Severity, StringComparison.Ordinal))
                {
                    continue;
                }

                var (found, seen) = ValueAnsweringCatchAlls(Redacted(File.ReadAllText(file)), enums);
                examined += seen;
                sites.AddRange(found.Select(site => $"{Path.GetFileName(file)} ({site.Enum})"));
            }

            return (sites, examined);
        }

        /// <summary>
        /// The three switches whose catch-all still answers with a value, each for a reason naming the arms
        /// would not remove. The list is a floor to work down, not a budget: an entry leaving it is a
        /// tightening, and one arriving is the failure this case exists for.
        /// <para>
        /// <c>AllowsNegativeLength</c> cannot be written exhaustively at all. Covering an enum of M members
        /// costs M branching decisions whatever the grouping — VEL501 charges one per arm and one per
        /// <c>or</c> — and <c>ArbitraryProperty</c> is several times the cap, so the compiler this guard
        /// speaks for refuses the arms before CS8509 could ask for them.
        /// </para>
        /// <para>
        /// The two <c>Router</c> switches could be named, and naming them would move a public failure. A
        /// mode outside the enum is reported by the commit's own switch, and these two are what carry it
        /// there; arms here would raise it earlier, before the navigation Status the caller reads has been
        /// set. Where the router should reject such a cast is a question for the router rather than for
        /// this reading.
        /// </para>
        /// </summary>
        private static readonly string[] KnownValueAnsweringSites =
        {
            "MotionPropertyClassParser.cs (ArbitraryProperty)",
            "Router.cs (NavigationMode)",
            "Router.cs (NavigationMode)",
        };

        [Test]
        public void Given_EveryProductionSwitchOverAPackageEnum_When_ItsArmsAreRead_Then_OnlyTheKnownSitesAnswerACatchAllWithAValue()
        {
            // Arrange / Act
            var (sites, examined) = ValueAnsweringSites();

            // Assert — the floor rides along because an enum set that came back empty, or a source tree
            // that read as none, reports nothing and would otherwise pass having measured nothing.
            Assert.That((examined >= 12, string.Join("\n", sites.OrderBy(site => site, StringComparer.Ordinal))),
                Is.EqualTo((true, string.Join("\n", KnownValueAnsweringSites))));
        }

        // A switch expression over a package enum, one arm named and a catch-all under it. `{0}` takes the
        // catch-all's body, so each case below differs from the guard's subject in one term.
        private const string OneArmAndACatchAll = @"
internal static class Sample
{
    internal static int Of(UILayer layer) => layer switch
    {
        UILayer.Background => 0,
        {0},
    };
}";

        private static (List<(int Line, string Enum)> Offenders, int Examined) Scan(string body) =>
            ValueAnsweringCatchAlls(
                Redacted(OneArmAndACatchAll.Replace("{0}", body)), PackageEnumNames());

        // GREEN_ON_BASE(characterization): the reading is the branch's and travels with this file, so it
        // answers the same on either side. What it is for is the case above, whose list the branch works
        // down to three and which would agree with a shorter one just as readily with the reading broken.
        [Test]
        public void Given_ACatchAllAnsweringWithAValue_When_TheSwitchIsRead_Then_ItIsReported()
        {
            // Arrange / Act
            var (offenders, examined) = Scan("_ => 1");

            // Assert — the count rides along so a reading that resolved no governing type cannot report
            // the one offender this case exists to see and be believed.
            Assert.That((examined, string.Join("\n", offenders.Select(o => o.Enum))),
                Is.EqualTo((1, "UILayer")));
        }

        // GREEN_ON_BASE(characterization): the exemption is the branch's own and reads the same on either
        // side; it is here so the guard cannot be widened into the two throwing sites without a red.
        [Test]
        public void Given_ACatchAllThatThrows_When_TheSwitchIsRead_Then_ItIsNotReported()
        {
            // Arrange / Act
            var (offenders, examined) = Scan("_ => throw new InvalidOperationException()");

            // Assert
            Assert.That((examined, string.Join("\n", offenders.Select(o => o.Enum))),
                Is.EqualTo((1, string.Empty)));
        }

        // GREEN_ON_BASE(characterization): no switch on either side is written this way, so the case is a
        // statement about the reading rather than about the tree — and the reading is the branch's.
        [Test]
        public void Given_ACatchAllUnderAWhenClause_When_TheSwitchIsRead_Then_ItIsReported()
        {
            // Arrange / Act — a guard is not an exemption: it can be true for the member no arm names, and
            // then the value is the same silence an unguarded catch-all gives.
            var (offenders, examined) = Scan("_ when layer != UILayer.Topmost => 1");

            // Assert
            Assert.That((examined, string.Join("\n", offenders.Select(o => o.Enum))),
                Is.EqualTo((1, "UILayer")));
        }

        // GREEN_ON_BASE(characterization): the boundary the branch draws, read off synthetic sources, so
        // both sides answer alike. It is what stops the reading being widened to every enum, which would
        // report a switch whose arms no one here writes.
        [Test]
        public void Given_ASwitchOverAnEnumThePackageDoesNotDeclare_When_ItIsRead_Then_ItIsNotExamined()
        {
            // Arrange — FilterFunctionType is UI Toolkit's. A member arrives there when UI Toolkit ships
            // one, so an exhaustive switch over it is a build that breaks on an editor upgrade.
            var source = OneArmAndACatchAll
                .Replace("UILayer.Background", "FilterFunctionType.Blur")
                .Replace("UILayer layer", "FilterFunctionType type")
                .Replace("layer switch", "type switch")
                .Replace("{0}", "_ => 1");

            // Act
            var engine = ValueAnsweringCatchAlls(Redacted(source), PackageEnumNames());
            var package = Scan("_ => 1");

            // Assert — the package half rides along because a reading that examines nothing at all leaves
            // the engine half at zero too, and would pass on the strength of being broken.
            Assert.That((package.Examined, engine.Examined), Is.EqualTo((1, 0)));
        }
    }
}
