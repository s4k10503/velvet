using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when the diff reads an element off the live child list by slot, where it names an old instance through
    /// <see cref="OldSlotOccupants"/> instead. The methods allowed one read a Portal's rows in its target, which are
    /// not old leaves of the container being diffed.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerOldInstanceReadTests
    {
        private static readonly string[] DiffSources =
        {
            "Packages/com.velvet.core/Runtime/Reconciler/ChildReconciler.cs",
            "Packages/com.velvet.core/Runtime/Reconciler/GeneralPathReconciler.cs",
        };

        private static readonly HashSet<string> PortalRowScans = new()
        {
            "HideMountUnderHiddenSuspense", "AddWithPortalChildren", "HoldsAMotionInPortal", "CollectPortalExits",
        };

        // ElementAt, and an indexer whose argument converts a logical slot.
        private static readonly Regex DirectSlotRead =
            new(@"\bElementAt\s*\(|\[[^\[\]\n]*ToPhysical\s*\(", RegexOptions.Compiled);

        // A physical index a slot conversion bound to a name: TryGetPhysical's out argument, or a local assigned
        // from ToPhysical. Indexing a child list by that name is the third spelling of a slot read.
        private static readonly Regex BoundPhysicalIndex =
            new(@"TryGetPhysical\s*\([^;{}]*?\bout\s+(?:var\s+|int\s+)?(?<name>[A-Za-z]\w*)\s*\)"
                + @"|\b(?:var|int)\s+(?<name>\w+)\s*=\s*[^;]*?ToPhysical\s*\(",
                RegexOptions.Compiled);

        private static readonly Regex MethodDeclaration =
            new(@"^\s*(?:(?:private|internal|public|protected|static|override|readonly)\s+)+[\w<>\[\]?,. ]+?\s(\w+)\s*\(",
                RegexOptions.Compiled | RegexOptions.Multiline);

        [Test]
        public void Given_TheDiffSources_When_ScannedForASlotRead_Then_OnlyThePortalRowScansHaveOne()
        {
            // Arrange
            var missing = DiffSources.Where(path => !File.Exists(Path.GetFullPath(path))).ToList();

            // Act
            var offenders = new List<string>();
            foreach (var path in DiffSources.Except(missing))
            {
                var code = BlankCommentsAndStrings(File.ReadAllText(Path.GetFullPath(path)));
                foreach (var (method, start, end) in Methods(code))
                {
                    if (PortalRowScans.Contains(method)) continue;
                    var body = code.Substring(start, end - start);
                    var reads = DirectSlotRead.Matches(body).Cast<Match>().Select(m => m.Index).ToList();
                    foreach (var name in BoundPhysicalIndex.Matches(body).Cast<Match>()
                                 .Select(m => m.Groups["name"].Value).Where(n => n != "_").Distinct())
                    {
                        reads.AddRange(Regex.Matches(body, @"\[\s*" + Regex.Escape(name) + @"\s*\]")
                            .Cast<Match>().Select(m => m.Index));
                    }
                    foreach (var read in reads.Distinct().OrderBy(i => i))
                    {
                        var line = code.Take(start + read).Count(c => c == '\n') + 1;
                        offenders.Add($"{Path.GetFileName(path)}:{line} in {method}");
                    }
                }
            }

            // Assert — a source that moved would scan as clean, so its absence is reported in the same text.
            Assert.That(
                string.Join("\n", missing.Select(path => "missing: " + path).Concat(offenders)),
                Is.Empty,
                "the diff names an old instance through OldSlotOccupants after the container's entry");
        }

        // Each method declaration with the text up to the next one; what precedes the first is attributed to "<none>".
        private static IEnumerable<(string Method, int Start, int End)> Methods(string code)
        {
            var declarations = MethodDeclaration.Matches(code).Cast<Match>().ToList();
            var previousName = "<none>";
            var previousStart = 0;
            foreach (var declaration in declarations)
            {
                yield return (previousName, previousStart, declaration.Index);
                previousName = declaration.Groups[1].Value;
                previousStart = declaration.Index;
            }
            yield return (previousName, previousStart, code.Length);
        }

        // Comments and string literals become spaces, newlines kept, so a name in prose or in a message is not a read
        // and line numbers still point at the source.
        private static string BlankCommentsAndStrings(string source)
        {
            var output = new StringBuilder(source.Length);
            var i = 0;
            while (i < source.Length)
            {
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') { output.Append(' '); i++; }
                }
                else if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    var end = source.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                    end = end < 0 ? source.Length : end + 2;
                    for (; i < end; i++) output.Append(source[i] == '\n' ? '\n' : ' ');
                }
                else if (source[i] == '"')
                {
                    output.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '"' && source[i] != '\n')
                    {
                        if (source[i] == '\\') { output.Append(' '); i++; }
                        if (i < source.Length) { output.Append(' '); i++; }
                    }
                    if (i < source.Length) { output.Append(' '); i++; }
                }
                else
                {
                    output.Append(source[i]);
                    i++;
                }
            }
            return output.ToString();
        }
    }
}
