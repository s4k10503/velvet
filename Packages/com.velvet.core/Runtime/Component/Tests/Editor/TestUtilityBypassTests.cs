using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Requires every <c>TestUtilities</c> helper named for tests to say which production path it does not
    /// take, so the bypass is visible where the helper is read rather than only where it is written.
    /// <para>
    /// EditMode drives neither the scheduler, the layout pass nor animations, so bypassing is a necessity
    /// rather than a shortcut. The hazard is that the bypass is invisible at the call site:
    /// <c>FlushEffectsForTest</c> says it flushes effects and does not say it skips the registration that
    /// hosts the drain — which is the question a tree-wide effect stall turned on, and the reason no
    /// existing fixture could ask it.
    /// </para>
    /// <para>
    /// Each declaration is a claim about this repository's own code, read off the production call site, so
    /// it is a decision the repo owns rather than an engine fact. What this holds is only that one exists:
    /// whether it is true stays with whoever writes it, and a helper added without one fails here rather
    /// than joining the set in silence.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class TestUtilityBypassTests
    {
        private const string UtilitiesDirectory = "Packages/com.velvet.core/TestUtilities";

        // A member is in scope for its name: the convention that marks a helper as test-only is the same one
        // TestOnlyMemberConventionTests reads, so the two cannot disagree about which members these are.
        private static readonly Regex DeclarationPattern =
            new(@"^\s*(?:public|internal)\s[^;{]*\b(?<name>\w+ForTest)\b", RegexOptions.Compiled);

        private static readonly Regex BypassPattern =
            new(@"^\s*//\s*Bypasses:\s*(?<what>\S.*)$", RegexOptions.Compiled);

        [Test]
        public void Given_EveryTestOnlyHelper_When_ItsSourceIsRead_Then_ItSaysWhichPathItDoesNotTake()
        {
            // Arrange
            var sources = Directory.GetFiles(Path.GetFullPath(UtilitiesDirectory), "*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            // Act
            var found = new List<string>();
            var silent = new List<string>();
            foreach (var path in sources)
            {
                var lines = File.ReadAllLines(path);
                for (var index = 0; index < lines.Length; index++)
                {
                    var declaration = DeclarationPattern.Match(lines[index]);
                    if (!declaration.Success)
                    {
                        continue;
                    }

                    var name = declaration.Groups["name"].Value;
                    found.Add(name);
                    // The line above it, which is where the writer of the declaration is looking.
                    if (index == 0 || !BypassPattern.IsMatch(lines[index - 1]))
                    {
                        silent.Add($"{Path.GetFileName(path)}: {name}");
                    }
                }
            }

            // Assert — the count rides along because an empty scan reports nothing silent either, and this
            // fixture's whole subject is a check that does not run looking like one that found nothing.
            Assert.That((found.Count > 10, string.Join("\n", silent)), Is.EqualTo((true, string.Empty)),
                "a helper reaches its behaviour by a different route than production, and the call site "
                + "cannot see that; say which path it does not take, or say it takes them all");
        }
    }
}
