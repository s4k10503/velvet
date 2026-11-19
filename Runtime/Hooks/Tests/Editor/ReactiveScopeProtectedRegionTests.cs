using System;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a run which begins where a catch block ends is cached, by reading the
    /// <see cref="MemoizationReportAttribute"/> the weaver leaves on the component that
    /// <see cref="ReactiveScopeInjectionTests"/> declares for it.
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeProtectedRegionTests
    {
        // GREEN_ON_BASE(characterization): the base caches a label whose run begins where a catch block ends.
        // Reading `>` as `>=` where `HoldsEnding` compares the end of a region with the start of the run takes the run
        // for one that holds that end, and declines it.
        [Test]
        public void Given_ALabelWhoseRunBeginsWhereACatchBlockEnds_When_Compiled_Then_NoProtectedRegionDeclinesIt()
        {
            // Act
            var declined = ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeInjectionTests), "AssignsALabelRightAfterATryStatement")?
                .Bailouts.Any(note => note.EndsWith("a protected region begins or ends inside it", StringComparison.Ordinal));

            // Assert
            Assert.That(declined, Is.False);
        }
    }
}
