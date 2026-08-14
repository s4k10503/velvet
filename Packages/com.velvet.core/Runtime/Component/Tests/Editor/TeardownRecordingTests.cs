using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the marker <c>scripts/test_quality/base_red_check.py</c> cuts a stack trace at against the one
    /// a runner writes. A case carries its teardown's throw on its own result, under the status and label a
    /// throw from its body would have carried, so the script reads only the section in front of that marker
    /// to decide which side threw. Read past it, a teardown that reached production code is credited to the
    /// merge base as a disagreement over a case the base agreed with; over a case declared green on the
    /// base, that reading tells the author to delete a correct declaration.
    /// </summary>
    /// <remarks>Pinned here for the reason <c>ResultStateVocabularyTests</c> is.</remarks>
    [TestFixture]
    internal sealed class TeardownRecordingTests
    {
        private const string Script = "scripts/test_quality/base_red_check.py";

        private static readonly Regex CutMarker =
            new(@"TEARDOWN_SECTION\s*=\s*re\.compile\(r""\^(--[A-Za-z]+)", RegexOptions.Compiled);

        /// <summary>Where the script cuts, read out of the script so neither side is written from memory.</summary>
        private static string ScriptMarker()
        {
            var found = CutMarker.Match(File.ReadAllText(Path.GetFullPath(Script)));
            return found.Success ? found.Groups[1].Value : "";
        }

        /// <summary>An exception that has been thrown, so it carries the trace an unthrown one has not.</summary>
        private static Exception Thrown(string message)
        {
            try
            {
                throw new InvalidOperationException(message);
            }
            catch (InvalidOperationException caught)
            {
                return caught;
            }
        }

        /// <summary>
        /// A result for one test case. Which method it names is nothing here reads — what it has to be is a
        /// case rather than a suite, since that is what decides whether the teardown site below is written.
        /// </summary>
        private static TestCaseResult CaseResult()
            => new(new TestMethod(new MethodWrapper(typeof(object), nameof(object.ToString))));

        private static string FirstLine(string text) => text.Split('\n')[0].TrimEnd('\r');

        [Test]
        public void Given_ATeardownThrowOnACaseThatLeftNoTrace_When_TheResultIsRead_Then_TheScriptsMarkerOpensIt()
        {
            // Arrange — a case whose body passed reaches its teardown holding no trace, which is the
            // state a fresh result is in.
            var result = CaseResult();

            // Act
            result.RecordTearDownException(Thrown("the fixture disposed what the base cannot"));

            // Assert — an empty section in front of the marker is the script's only evidence that the
            // case itself never threw, and it is empty only while the marker is what opens the trace.
            Assert.That(FirstLine(result.StackTrace), Is.EqualTo(ScriptMarker()),
                $"{Script} cuts a stack trace at a marker the runner no longer opens one with");
        }

        [Test]
        public void Given_ATeardownThrowAfterTheBodyThrew_When_TheResultIsRead_Then_TheBodysTraceStandsInFront()
        {
            // Arrange
            var result = CaseResult();
            result.RecordException(Thrown("the body threw"));
            var body = result.StackTrace;

            // Act
            result.RecordTearDownException(Thrown("and then the teardown did"));

            // Assert — the section in front of the marker is read as the case's own, which holds only
            // while that is the end the runner adds the teardown's at.
            Assert.That(result.StackTrace.Split(new[] { ScriptMarker() }, StringSplitOptions.None)[0]
                    .TrimEnd(),
                Is.EqualTo(body.TrimEnd()));
        }

        // GREEN_ON_BASE(characterization): the attributes a runner writes beside a teardown throw,
        // which is why the trace is what gets read and not something this change decides.
        [Test]
        public void Given_ATeardownThrowOnATestCase_When_ItIsWrittenToXml_Then_NoAttributeNamesTheTeardown()
        {
            // Arrange — the attribute would be the separator the script would rather have than the
            // trace. The teardown site is reached for only on a suite, and a case is not one.
            var result = CaseResult();
            result.RecordTearDownException(Thrown("the fixture disposed what the base cannot"));

            // Act
            var written = result.ToXml(recursive: false);

            // Assert — the label is the one a throw from the body carries, so no attribute here
            // tells the two apart.
            Assert.That((written.Attributes["label"], written.Attributes["site"]),
                Is.EqualTo(("Error", (string)null)));
        }
    }
}
