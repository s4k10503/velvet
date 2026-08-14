using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the labels <c>scripts/test_quality/base_red_check.py</c> reads a results file by against the
    /// ones NUnit declares. That script separates a case the merge base ran and disagreed with from one that
    /// never reached a verdict there, and what carries the difference into the file is the label written
    /// beside each result. A label the script does not know reads as a disagreement; over a case declared
    /// green on the base, that reading tells the author to delete a correct declaration.
    /// </summary>
    /// <remarks>
    /// Both sides are read rather than listed: the script's tuple out of the script, NUnit's out of NUnit.
    /// A drift guard written from memory goes stale on the side nobody re-reads. This is also the one fact
    /// the script rests on that its own tests cannot reach — they hand it hand-written XML, which agrees
    /// with whatever it was written to say.
    /// </remarks>
    [TestFixture]
    internal sealed class ResultStateVocabularyTests
    {
        private const string Script = "scripts/test_quality/base_red_check.py";

        private static readonly Regex LabelTuple =
            new(@"NOT_A_VERDICT_LABEL\s*=\s*\(([^)]*)\)", RegexOptions.Compiled);

        private static readonly Regex Quoted = new("\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>The labels the script reads a results file by, sorted so the comparison is of the set.</summary>
        private static IReadOnlyList<string> ScriptLabels()
        {
            var tuple = LabelTuple.Match(File.ReadAllText(Path.GetFullPath(Script)));
            return tuple.Success
                ? Quoted.Matches(tuple.Groups[1].Value).Select(match => match.Groups[1].Value)
                    .OrderBy(label => label).ToList()
                : new List<string>();
        }

        /// <summary>Every label NUnit pairs with a failing status — the set the script has to match.</summary>
        private static IReadOnlyList<string> NUnitFailureLabels()
            => typeof(ResultState)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(ResultState))
                .Select(field => (ResultState)field.GetValue(null))
                .Where(state => state.Status == TestStatus.Failed && !string.IsNullOrEmpty(state.Label))
                .Select(state => state.Label)
                .Distinct()
                .OrderBy(label => label)
                .ToList();

        [Test]
        public void Given_EveryLabelNUnitPairsWithAFailure_When_TheScriptIsRead_Then_ItNamesTheSameSet()
        {
            // Arrange — the expectation is NUnit's, so a script this can read nothing out of fails
            // against a non-empty set rather than matching an empty one.
            var declared = NUnitFailureLabels();

            // Act
            var read = ScriptLabels();

            // Assert — a failure is either half: a label NUnit added that the script would read as a
            // disagreement, or one the script reads that NUnit no longer writes.
            Assert.That(string.Join(",", read), Is.EqualTo(string.Join(",", declared)),
                $"{Script} reads a results file by labels that are no longer NUnit's");
        }

        // GREEN_ON_BASE(characterization): the state NUnit reports for an ignored case, which the
        // script's split between a result vocabulary and a label one is built on.
        [Test]
        public void Given_TheStateAnIgnoredCaseCarries_When_ItIsRead_Then_ItIsSkippedRatherThanFailed()
        {
            // Arrange — the exception `Assert.Ignore` raises, which is what `TestGraphics.IgnoreIfHeadless`
            // stops a graphics-dependent case with on a runner that has no device.
            var ignored = new IgnoreException("requires a graphics device");

            // Act
            var state = ignored.ResultState;

            // Assert — the reading arrives as the result and not as the label, which is why the script
            // reads it out of its result vocabulary and why the set above correctly leaves it out.
            Assert.That((state.Status.ToString(), state.Label), Is.EqualTo(("Skipped", "Ignored")));
        }
    }
}
