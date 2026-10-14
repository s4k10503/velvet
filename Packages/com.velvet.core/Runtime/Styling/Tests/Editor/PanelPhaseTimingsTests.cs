#if UNITY_EDITOR
using System.IO;
using System.Linq;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class PanelPhaseTimingsTests
    {
        // GREEN_ON_BASE(construction): the base run carries the branch's TestUtilities, so both sides here are this change's own text.
        // Deleting the line `PanelPhaseTimings.Record(PanelPhaseTimings.Teardown, teardown.Elapsed.TotalMilliseconds);` from `PanelTestBase.TearDown` is what reddens it.
        [Test]
        public void Given_APanelFixtureCase_When_ItSetsUpLaysOutAndTearsDown_Then_EveryPhaseIsRecordedForIt()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            var probe = new ProbeFixture();
            var self = TestContext.CurrentContext.Test;

            // Act
            probe.SetUp();
            probe.Layout();
            probe.TearDown();
            var phases = PanelPhaseTimings.Rows
                .Where(row => row.Fixture == self.ClassName && row.CaseName == self.Name)
                .Select(row => row.Phase);

            // Assert
            Assert.That(string.Join(",", phases), Is.EqualTo("create_show,layout,teardown,case"));
        }

        // GREEN_ON_BASE(construction): the base run carries the branch's TestUtilities, so both sides here are this change's own text.
        // Replacing `if (s_fixture == null || s_caseName == null)` in `Record` with `if (false)` is what reddens it.
        [Test]
        public void Given_NoCaseIsOpen_When_APhaseIsRecorded_Then_NoRowIsAdded()
        {
            // Arrange
            var before = PanelPhaseTimings.Rows.Count;

            // Act
            PanelPhaseTimings.Record(PanelPhaseTimings.Layout, 1.0);

            // Assert
            Assert.That(PanelPhaseTimings.Rows.Count, Is.EqualTo(before));
        }

        // GREEN_ON_BASE(construction): the base run carries the branch's TestUtilities, so both sides here are this change's own text.
        // Replacing `Path.GetDirectoryName(Path.GetFullPath(commandLineArgs[at + 1]))` in `ResultsDirectory` with `Path.GetFullPath(commandLineArgs[at + 1])` is what reddens it.
        [Test]
        public void Given_TheResultsFlag_When_TheDirectoryIsAsked_Then_ItIsTheDirectoryOfTheFileTheFlagNames()
        {
            // Arrange
            var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
            try
            {
                var args = new[] { "Unity", "-runTests", "-testResults", Path.Combine(directory.FullName, "r.xml") };

                // Act
                var resolved = PanelPhaseTimings.ResultsDirectory(args);

                // Assert
                Assert.That(resolved, Is.EqualTo(directory.FullName));
            }
            finally
            {
                directory.Delete(true);
            }
        }

        // GREEN_ON_BASE(construction): the base run carries the branch's TestUtilities, so both sides here are this change's own text.
        // Replacing `if (at < 0 || at + 1 >= commandLineArgs.Length)` in `ResultsDirectory` with `if (at + 1 >= commandLineArgs.Length)` is what reddens it.
        [Test]
        public void Given_NoResultsFlag_When_TheDirectoryIsAsked_Then_ThereIsNone()
        {
            // Act
            var resolved = PanelPhaseTimings.ResultsDirectory(new[] { "Unity", "-runTests" });

            // Assert
            Assert.That(resolved, Is.Null);
        }

        // GREEN_ON_BASE(construction): the base run carries the branch's TestUtilities, so both sides here are this change's own text.
        // Replacing `return directory != null && Directory.Exists(directory) ? directory : null;` in `ResultsDirectory` with `return directory;` is what reddens it.
        [Test]
        public void Given_AResultsDirectoryThatDoesNotExist_When_TheDirectoryIsAsked_Then_ItIsNotCreated()
        {
            // Arrange
            var missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "r.xml");

            // Act
            var resolved = PanelPhaseTimings.ResultsDirectory(new[] { "-testResults", missing });

            // Assert
            Assert.That((resolved == null, Directory.Exists(Path.GetDirectoryName(missing))), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(construction): the base run carries the branch's TestUtilities, so both sides here are this change's own text.
        // Replacing `if (!File.Exists(path))` in `Append` with `if (true)` is what reddens it.
        [Test]
        public void Given_RowsAppendedTwice_When_TheFileIsRead_Then_TheHeaderIsWrittenOnceBeforeTheRowsInOrder()
        {
            // Arrange
            var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
            try
            {
                var first = new[] { new PanelPhaseTimings.Row("F", "C1", PanelPhaseTimings.CreateShow, 12.3456) };
                var second = new[] { new PanelPhaseTimings.Row("F", "C2", PanelPhaseTimings.Teardown, 4) };

                // Act
                PanelPhaseTimings.Append(directory.FullName, first);
                PanelPhaseTimings.Append(directory.FullName, second);
                var lines = File.ReadAllLines(Path.Combine(directory.FullName, PanelPhaseTimings.FileName));

                // Assert
                Assert.That(string.Join("|", lines),
                    Is.EqualTo("fixture,case,phase,milliseconds|F,C1,create_show,12.346|F,C2,teardown,4.000"));
            }
            finally
            {
                directory.Delete(true);
            }
        }

        // GREEN_ON_BASE(construction): the base run carries the branch's TestUtilities, so both sides here are this change's own text.
        // Replacing `field.Replace("\"", "\"\"")` in `Quote` with `field` is what reddens it.
        [Test]
        public void Given_ACaseNameWithACommaAndAQuote_When_TheRowIsFormatted_Then_TheFieldIsQuoted()
        {
            // Arrange
            var row = new PanelPhaseTimings.Row("F", "Case(\"a,b\")", PanelPhaseTimings.Layout, 1);

            // Act
            var line = PanelPhaseTimings.Line(row);

            // Assert
            Assert.That(line, Is.EqualTo("F,\"Case(\"\"a,b\"\")\",layout,1.000"));
        }

        private sealed class ProbeFixture : PanelTestBase
        {
            public void Layout() => MountAndResolve("flex");
        }
    }
}
#endif
