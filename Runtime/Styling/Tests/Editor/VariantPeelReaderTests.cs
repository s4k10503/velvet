using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the filter, filter-transition and animate-hue reads a patch hands every class of a list still
    /// find their utility behind a variant and with none, and the filter read behind stacked variants as well.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class VariantPeelReaderTests
    {
        private static Func<string[], bool> FilterTransitionRead()
            => (Func<string[], bool>)Delegate.CreateDelegate(typeof(Func<string[], bool>),
                typeof(FiberFilterTransitionApplier).GetMethod("HasFilterTransitionClass",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

        // GREEN_ON_BASE(characterization): the base finds every one of these filter utilities, behind a variant or none.
        [TestCase("hover:blur")]
        [TestCase("hover:grayscale")]
        [TestCase("hover:invert")]
        [TestCase("hover:sepia")]
        [TestCase("hover:blur-sm")]
        [TestCase("hover:drop-shadow-md")]
        [TestCase("hover:contrast-125")]
        [TestCase("hover:hue-rotate-90")]
        [TestCase("hover:-hue-rotate-90")]
        [TestCase("hover:grayscale-0")]
        [TestCase("hover:invert-0")]
        [TestCase("hover:sepia-0")]
        [TestCase("hover:brightness-50")]
        [TestCase("hover:saturate-50")]
        [TestCase("hover:filter-[pixelate:2]")]
        [TestCase("dark:hover:blur-sm")]
        [TestCase("blur-sm")]
        public void Given_AFilterUtility_When_AskedWhetherTheClassesHoldAFilter_Then_TheyDo(string cls)
        {
            // Act
            var held = StyleFilterValueParser.HasFilterClass(new[] { cls });

            // Assert
            Assert.That(held, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base finds an animate-hue behind a variant and with none.
        [TestCase("hover:animate-hue")]
        [TestCase("animate-hue")]
        public void Given_AnAnimateHue_When_AskedWhetherTheClassesCarryAFilter_Then_TheyDo(string cls)
        {
            // Act
            var carried = WrapperInfrastructure.CarriesFilter(new[] { cls });

            // Assert
            Assert.That(carried, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base finds a transition-filter behind a variant and with none.
        [TestCase("hover:transition-filter")]
        [TestCase("transition-filter")]
        public void Given_ATransitionFilter_When_TheClassesAreReadForOne_Then_ItIsFound(string cls)
        {
            // Arrange
            var read = FilterTransitionRead();

            // Act
            var found = read(new[] { cls });

            // Assert
            Assert.That(found, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads a class ending in the utility's name as no transition-filter.
        [Test]
        public void Given_AClassEndingInTheTransitionFiltersName_When_TheClassesAreReadForOne_Then_NoneIsFound()
        {
            // Arrange
            var read = FilterTransitionRead();

            // Act
            var found = read(new[] { "no-transition-filter" });

            // Assert
            Assert.That(found, Is.False);
        }
    }
}
