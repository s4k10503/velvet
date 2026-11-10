using System;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which writes a run may make and still be cached, and how the report carries its notes, by reading
    /// the <see cref="MemoizationReportAttribute"/> the weaver leaves on each component method.
    /// <list type="bullet">
    /// <item>A run may write into an object it creates, from its first instruction on; a run that writes a static
    /// field is declined.</item>
    /// <item>Each note is one entry of the report, with nothing ahead of the first.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeRunEffectTests
    {
        private sealed class Row
        {
            public int Value;

            public override string ToString() => Convert.ToString(Value);
        }

        private static string s_last = string.Empty;

        private static MemoizationReportAttribute? ReportOf(string methodName)
            => ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeRunEffectTests), methodName);

        [Component]
        private static VNode BuildsAnObjectAtTheStartOfARun(int seed, int other)
            => V.Div("built", V.Text(new Row { Value = seed }.ToString()), V.Text(Convert.ToString(other)));

        // GREEN_ON_BASE(characterization): the base caches a run that writes into the object its first instruction creates.
        // Reading `>=` as `>` where `EffectProblem` compares the object's origin with the start of the run reads
        // that object as one the run did not create and declines the run.
        [Test]
        public void Given_ARunThatWritesIntoTheObjectItsFirstInstructionCreates_When_Compiled_Then_TheReportHoldsNoEntry()
        {
            // Act
            var notes = ReportOf(nameof(BuildsAnObjectAtTheStartOfARun))?.Bailouts.Count;

            // Assert
            Assert.That(notes, Is.EqualTo(0));
        }

        [Component]
        private static VNode WritesAStaticInARun(string name, int other)
            => V.Div("static", V.Text(s_last = name), V.Text(Convert.ToString(other)));

        // GREEN_ON_BASE(characterization): the base declines a run that writes a static field of the component's class.
        // Reading `==` as `!=` where `IsLambdaCache` compares the declaring type's name with `<>c` takes the field
        // for a lambda cache and caches the run.
        [Test]
        public void Given_ARunThatWritesAStaticField_When_Compiled_Then_TheReportSaysItWritesAStatic()
        {
            // Act
            var declined = ReportOf(nameof(WritesAStaticInARun))?.Bailouts
                .Any(note => note.StartsWith("the call to V.Text", StringComparison.Ordinal) && note.EndsWith("it writes a static", StringComparison.Ordinal));

            // Assert
            Assert.That(declined, Is.True);
        }

        [Component]
        private static VNode ReadsAMutableStatic(int other)
            => V.Text(s_last + Convert.ToString(other));

        // GREEN_ON_BASE(characterization): the base reports the one run it declines as the report's only entry.
        // Reading `>` as `>=` where `WeaveReport.Joined` tests what it has joined so far puts a line break ahead of the
        // first note, which the report then reads as an empty entry of its own.
        [Test]
        public void Given_AMethodWithOneDeclinedRun_When_Compiled_Then_TheReportHoldsOneEntry()
        {
            // Act
            var notes = ReportOf(nameof(ReadsAMutableStatic))?.Bailouts.Count;

            // Assert
            Assert.That(notes, Is.EqualTo(1));
        }
    }
}
