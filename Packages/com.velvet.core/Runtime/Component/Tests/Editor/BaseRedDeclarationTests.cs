using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the in-file declaration <c>scripts/test_quality/base_red_check.py</c> reads — the one way to say
    /// that a case belongs on the merge base — against the categories that script accepts and the example
    /// CONTRIBUTING.md shows. A declaration whose category the script does not know is refused by it, and
    /// until then reads to everyone else as an approved exemption; the guide's example is what a
    /// contributor copies, so a format change that leaves it behind teaches the wrong shape.
    /// </summary>
    /// <remarks>
    /// Both what the script accepts — its categories and how long a reason has to be — are read out of it
    /// rather than listed here. A second copy is a second thing to update, and the failure of the copy
    /// that nobody updated is silence.
    /// </remarks>
    [TestFixture]
    internal sealed class BaseRedDeclarationTests
    {
        private const string Script = "scripts/test_quality/base_red_check.py";
        private const string Guide = "CONTRIBUTING.md";

        private static readonly Regex CategoryTuple =
            new(@"CATEGORIES\s*=\s*\(([^)]*)\)", RegexOptions.Compiled);

        private static readonly Regex Quoted = new("\"([^\"]+)\"", RegexOptions.Compiled);

        private static readonly Regex MinimumWords =
            new(@"MINIMUM_REASON_WORDS\s*=\s*(\d+)", RegexOptions.Compiled);

        private static readonly Regex Declaration =
            new(@"GREEN_ON_BASE\(([A-Za-z]*)\)\s*:\s*(.*)", RegexOptions.Compiled);

        private static string Read(string path) => File.ReadAllText(Path.GetFullPath(path));

        private static IReadOnlyList<string> Categories()
        {
            var tuple = CategoryTuple.Match(Read(Script));
            return tuple.Success
                ? Quoted.Matches(tuple.Groups[1].Value).Select(match => match.Groups[1].Value).ToList()
                : new List<string>();
        }

        private static int MinimumReasonWords()
        {
            var match = MinimumWords.Match(Read(Script));
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        /// <summary>Every declaration written in this repository's own C# test sources.</summary>
        private static IEnumerable<(string File, Match Match)> Declared()
            => from file in Directory.EnumerateFiles(Path.GetFullPath("Packages/com.velvet.core"),
                                                     "*.cs", SearchOption.AllDirectories)
               let text = File.ReadAllText(file)
               from Match match in Declaration.Matches(text)
               select (file, match);

        [Test]
        public void Given_TheScript_When_ItsCategoriesAreRead_Then_ThereIsMoreThanOne()
        {
            // Act
            var categories = Categories();

            // Assert — a reading that comes back empty makes every case below vacuously true, and a
            // single category would mean the declaration says nothing beyond its own presence.
            Assert.That(categories.Count, Is.GreaterThan(1),
                $"{Script} declares no CATEGORIES tuple this fixture can read");
        }

        [Test]
        public void Given_EveryDeclarationInTheCSharpSources_When_ItsCategoryIsRead_Then_TheScriptKnowsIt()
        {
            // Arrange
            var categories = Categories();

            // Act
            var unknown = (from written in Declared()
                           let category = written.Match.Groups[1].Value
                           where !categories.Contains(category)
                           select $"{Path.GetFileName(written.File)}: {category}").ToList();

            // Assert
            Assert.That(unknown, Is.Empty,
                $"{Script} refuses these categories, so the case is measured rather than exempt:\n"
                + string.Join("\n", unknown));
        }

        [Test]
        public void Given_TheScript_When_ItsMinimumReasonLengthIsRead_Then_ItIsMoreThanOneWord()
        {
            // Act
            var minimum = MinimumReasonWords();

            // Assert — a reading that comes back zero makes every reason below long enough by
            // arithmetic, which is the same silence a hand-copied literal fails with.
            Assert.That(minimum, Is.GreaterThan(1),
                $"{Script} declares no MINIMUM_REASON_WORDS this fixture can read");
        }

        [Test]
        public void Given_TheDeclarationTheGuideShows_When_TheScriptsOwnPatternReadsIt_Then_ItIsAccepted()
        {
            // Arrange — what a contributor copies out of the guide, read back by the thing that will
            // judge it. Both the categories and the length come from the script: the script splits on
            // whitespace and drops what falls out empty, so this must too or a reason with a trailing
            // space is long enough here and short there.
            var categories = Categories();
            var minimum = MinimumReasonWords();

            // Act
            var shown = Declaration.Matches(Read(Guide)).Cast<Match>()
                .Select(match => (Category: match.Groups[1].Value, Reason: match.Groups[2].Value))
                .ToList();

            // Assert — the count rides along because a guide that shows no example passes on the rest.
            // A floor rather than an exact number: the guide shows one spelling per lane, and pinning
            // how many lanes there are is a mirror that goes stale when a third one arrives.
            Assert.That((shown.Count >= 1, shown.All(sample => categories.Contains(sample.Category)
                    && sample.Reason.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                        .Length >= minimum)),
                Is.EqualTo((true, true)),
                $"{Guide} shows an example {Script} would not accept");
        }
    }
}
