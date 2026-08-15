using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the sections <c>scripts/test_quality/base_red_check.py</c> names against the ones a runner
    /// opens around a test case. A case carries what its setup, teardown or test actions threw on its
    /// own result, under the status and label a throw from its body would have carried, so the script
    /// reads only the section in front of the first marker to decide which side threw. Read past one, a
    /// scaffold that reached production code is credited to the merge base as a disagreement over a case
    /// the base agreed with; over a case declared green on the base, that reading tells the author to
    /// delete a correct declaration.
    /// </summary>
    /// <remarks>Pinned here for the reason <c>ResultStateVocabularyTests</c> is.</remarks>
    [TestFixture]
    internal sealed class ScaffoldingSectionRecordingTests
    {
        private const string Script = "scripts/test_quality/base_red_check.py";

        private static readonly Regex SectionTuple =
            new(@"SCAFFOLD_SECTIONS\s*=\s*\(([^)]*)\)", RegexOptions.Compiled);

        private static readonly Regex Quoted = new("\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>The sections the script names, read out of it so neither side is written from memory.</summary>
        private static string[] ScriptSections()
        {
            var tuple = SectionTuple.Match(File.ReadAllText(Path.GetFullPath(Script)));
            return tuple.Success
                ? Quoted.Matches(tuple.Groups[1].Value).Select(match => match.Groups[1].Value)
                    .OrderBy(name => name).ToArray()
                : Array.Empty<string>();
        }

        /// <summary>Those sections as the marker that opens one, which is where the script cuts a trace.</summary>
        private static string[] ScriptMarkers()
            => ScriptSections().Select(name => "--" + name).ToArray();

        /// <summary>An exception that has been thrown, so it carries the trace an unthrown one has not.</summary>
        private static Exception Thrown(string message, Exception inner = null)
        {
            try
            {
                throw new InvalidOperationException(message, inner);
            }
            catch (InvalidOperationException caught)
            {
                return caught;
            }
        }

        /// <summary>
        /// A thrown exception carrying a result state of its own, which is the shape Unity's end-of-scope
        /// log check raises out of a scaffolding section. <c>AssertionException</c> stands in for it
        /// because the runner branches on the base type they share, and because that type is public.
        /// </summary>
        private static Exception ThrownWithAState(string message)
        {
            try
            {
                throw new AssertionException(message);
            }
            catch (AssertionException caught)
            {
                return caught;
            }
        }

        /// <summary>
        /// A test the runner would report as one case. Which method it names is nothing here reads — what
        /// it has to be is a case rather than a suite, since that decides both whether a teardown site is
        /// written beside the result and which sections a wrapping command opens.
        /// </summary>
        private static TestMethod CaseTest()
            => new(new MethodWrapper(typeof(object), nameof(object.ToString)));

        private static TestCaseResult CaseResult() => new(CaseTest());

        private static string FirstLine(string text) => text.Split('\n')[0].TrimEnd('\r');

        /// <summary>The trace up to the first line that is one of the script's markers, as it cuts.</summary>
        private static string BeforeFirstMarker(string text)
        {
            var markers = ScriptMarkers();
            return string.Join("\n", text.Split('\n')
                .TakeWhile(line => !markers.Contains(line.TrimEnd('\r'))));
        }

        /// <summary>A command the runner's wrapping commands can be built around, which needs only a test.</summary>
        private sealed class Innermost : TestCommand
        {
            public Innermost(Test test) : base(test)
            {
            }

            public override TestResult Execute(ITestExecutionContext context) => context.CurrentResult;
        }

        private static Assembly Runner()
            => AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(loaded => loaded.GetName().Name == "UnityEngine.TestRunner");

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
                                         | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// The runner's own before/after commands that wrap one test case, enumerated rather than named:
        /// a command it adds is one this has to answer for too. What separates them from the one-time
        /// pair, whose sections land on the suite the script does not read, is that they are built
        /// around another command instead of around a suite.
        /// </summary>
        private static Type[] WrappingCommands()
            => (Runner()?.GetTypes() ?? Type.EmptyTypes)
                .Where(type => type.BaseType is { IsGenericType: true }
                               && type.BaseType.GetGenericTypeDefinition().Name
                                   .StartsWith("BeforeAfterTestCommandBase", StringComparison.Ordinal))
                .Where(type => type.GetConstructor(Any, null, new[] { typeof(TestCommand) }, null) != null)
                .ToArray();

        /// <summary>
        /// The section names one wrapping command records under, read off an instance of it rather than
        /// out of this file, so a section the runner renames arrives here as a different name.
        /// </summary>
        private static string[] SectionNamesOf(Type command)
        {
            var built = Activator.CreateInstance(command, Any, null,
                new object[] { new Innermost(CaseTest()) }, null);
            return new[] { "m_BeforeErrorPrefix", "m_AfterErrorPrefix" }
                .Select(name => command.BaseType?.GetField(name, Any))
                .Select(field => field == null ? "<no prefix field>" : (string)field.GetValue(built))
                .ToArray();
        }

        /// <summary>Records <paramref name="thrown"/> onto a result the way the runner records a scaffold's throw.</summary>
        private static TestCaseResult RecordedUnder(string prefix, Exception thrown,
            TestCaseResult onto = null)
        {
            var record = Runner()?.GetType("UnityEngine.TestRunner.NUnitExtensions.TestResultExtensions")
                ?.GetMethod("RecordPrefixedException", Any);
            var result = onto ?? CaseResult();
            record?.Invoke(null, new object[] { result, prefix, thrown, null });
            return result;
        }

        private static bool HasRecorder()
            => Runner()?.GetType("UnityEngine.TestRunner.NUnitExtensions.TestResultExtensions")
                ?.GetMethod("RecordPrefixedException", Any) != null;

        /// <summary>Opens a section the way the runner does, and hands back the line it opened with.</summary>
        private static string SectionOpenedBy(string prefix)
            => HasRecorder()
                ? FirstLine(RecordedUnder(prefix, Thrown("a scaffold threw")).StackTrace)
                : "<no recorder>";

        [Test]
        public void Given_ATeardownThrowOnACaseThatLeftNoTrace_When_TheResultIsRead_Then_AScriptMarkerOpensIt()
        {
            // Arrange — a case whose body passed reaches its teardown holding no trace, which is the
            // state a fresh result is in.
            var result = CaseResult();

            // Act
            result.RecordTearDownException(Thrown("the fixture disposed what the base cannot"));

            // Assert — an empty section in front of the marker is the script's only evidence that the
            // case itself never threw, and it is empty only while a marker is what opens the trace.
            Assert.That(ScriptMarkers(), Does.Contain(FirstLine(result.StackTrace)),
                $"{Script} cuts a stack trace at markers the runner no longer opens one with");
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
            Assert.That(BeforeFirstMarker(result.StackTrace).TrimEnd(), Is.EqualTo(body.TrimEnd()));
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

        [Test]
        public void Given_TheSectionsTheRunnerWrapsACaseIn_When_EachIsOpened_Then_TheScriptCutsAtEveryOne()
        {
            // Arrange
            var sections = WrappingCommands().SelectMany(SectionNamesOf).Distinct();

            // Act
            var opened = sections.Select(SectionOpenedBy).Distinct().OrderBy(line => line).ToArray();

            // Assert
            Assert.That(opened, Is.EqualTo(ScriptMarkers()));
        }

        // GREEN_ON_BASE(characterization): what a throw carrying its own result state leaves behind,
        // which is what the script reads the message for rather than the trace.
        [Test]
        public void Given_AThrowCarryingAStateOutOfAScaffold_When_TheResultIsRead_Then_OnlyTheMessageNamesTheSection()
        {
            // Arrange — the body passed, so everything on this result is the scaffold's.
            var result = RecordedUnder("TearDown", ThrownWithAState("an unhandled log message"));

            // Act
            var opened = FirstLine(result.StackTrace ?? string.Empty);

            // Assert — the trace no longer opens with a marker and the message does open with the
            // section, so one comparison over the pair says which of them the script can read.
            Assert.That((ScriptMarkers().Contains(opened),
                    result.Message.StartsWith("TearDown : ", StringComparison.Ordinal)),
                Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): the two attributes that would separate such a result from
        // a body that failed an assertion, and are the same on both.
        [Test]
        public void Given_AThrowCarryingAStateOutOfAScaffold_When_ItIsWrittenToXml_Then_NoLabelStandsBesideIt()
        {
            // Arrange
            var result = RecordedUnder("TearDown", ThrownWithAState("an unhandled log message"));

            // Act
            var written = result.ToXml(recursive: false);

            // Assert
            Assert.That((written.Attributes["result"], written.Attributes["label"]),
                Is.EqualTo(("Failed", (string)null)));
        }

        // GREEN_ON_BASE(characterization): that such a result still carries a trace. Reading its
        // absence instead of the message was the alternative, and there is no absence to read.
        [Test]
        public void Given_AThrowCarryingAStateOutOfAScaffold_When_TheResultIsRead_Then_ItStillCarriesATrace()
        {
            // Arrange
            var result = RecordedUnder("TearDown", ThrownWithAState("an unhandled log message"));

            // Act
            var trace = result.StackTrace;

            // Assert
            Assert.That(string.IsNullOrWhiteSpace(trace), Is.False);
        }

        // GREEN_ON_BASE(characterization): where a body's own message stands once a scaffold has
        // thrown behind it, which is what keeps a case that disagreed from reading as one it did not.
        [Test]
        public void Given_AScaffoldThrowAfterTheBodyFailed_When_TheResultIsRead_Then_TheBodysMessageStandsInFront()
        {
            // Arrange
            var result = CaseResult();
            result.RecordException(ThrownWithAState("the body disagreed"));

            // Act
            RecordedUnder("TearDown", ThrownWithAState("and then a cleanup threw"), result);

            // Assert
            Assert.That(FirstLine(result.Message), Is.EqualTo("the body disagreed"));
        }

        // GREEN_ON_BASE(characterization): an opener the throw itself brought is not a section marker.
        // That is a property of the runner rather than a reading this change decides.
        [Test]
        public void Given_AThrowCarryingAnInner_When_TheTraceIsBuilt_Then_TheScriptDoesNotCutAtTheInnersOpener()
        {
            // Arrange — the inner's frames are opened the same way a section is, which is why the
            // script names its markers one by one instead of matching any opener of that shape.
            var result = CaseResult();
            result.RecordException(Thrown("the body threw", new InvalidTimeZoneException("beneath it")));

            // Act — the first opener in the trace, which throws here rather than passing if the
            // runner writes none.
            var opener = result.StackTrace.Split('\n').Select(line => line.TrimEnd('\r'))
                .First(line => line.StartsWith("--", StringComparison.Ordinal));

            // Assert — it names the inner exception and is none of the sections, so cutting at
            // openers by shape would take the body's own throw for its scaffolding's.
            Assert.That((opener, ScriptMarkers().Contains(opener)),
                Is.EqualTo(("--" + nameof(InvalidTimeZoneException), false)));
        }
    }
}
