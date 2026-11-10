using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a memoization report describes the one body it was written on: an override of a method that
    /// carries one does not report it.
    /// </summary>
    [TestFixture]
    internal sealed class MemoizationReportAttributeTests
    {
        private class Reported
        {
            [MemoizationReport(true, 0, "")]
            public virtual VNode Render() => null;
        }

        private sealed class Overriding : Reported
        {
            public override VNode Render() => null;
        }

        // GREEN_ON_BASE(characterization): the base already declares the report not inherited; no case asserted it before.
        [Test]
        public void Given_AnOverrideOfAReportedMethod_When_ItsReportIsReadWithInheritance_Then_ThereIsNone()
        {
            // Arrange
            var method = typeof(Overriding).GetMethod(nameof(Overriding.Render));

            // Act
            var report = method!.GetCustomAttribute<MemoizationReportAttribute>(inherit: true);

            // Assert
            Assert.That(report, Is.Null);
        }
    }
}
