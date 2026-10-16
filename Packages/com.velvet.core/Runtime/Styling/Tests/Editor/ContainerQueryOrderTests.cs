using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where a container query rule sits among an element's rules against the order Tailwind 4.3.3's
    /// generated CSS emits them in, where a later rule wins a tie: ceilings before floors, a floor ascending and a
    /// ceiling descending by threshold, a pixel length before a rem one, a named container before the unnamed
    /// one, and the container queries after every viewport breakpoint and before <c>dark:</c>.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerQueryOrderTests
    {
        [Test]
        public void Given_FloorsWrittenOutOfOrder_When_Ordered_Then_TheLargerThresholdComesLater()
        {
            // Arrange — Tailwind emits @sm, @md, @lg, @xl.
            var names = new[] { "@lg:p-2", "@sm:p-2", "@xl:p-2", "@md:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 2, 0, 3, 1 }));
        }

        [Test]
        public void Given_CeilingsWrittenOutOfOrder_When_Ordered_Then_TheSmallerThresholdComesLater()
        {
            // Arrange — Tailwind emits @max-xl, @max-md, @max-sm.
            var names = new[] { "@max-sm:p-2", "@max-xl:p-2", "@max-md:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 2, 0, 1 }));
        }

        [Test]
        public void Given_AFloorAndACeilingAtOneThreshold_When_Ordered_Then_TheFloorComesLater()
        {
            // Arrange — Tailwind emits @max-sm before @sm.
            var names = new[] { "@sm:p-2", "@max-sm:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Given_ACeilingBelowAFloorsThreshold_When_Ordered_Then_TheFloorStillComesLater()
        {
            // Arrange — Tailwind emits @max-xl, @max-lg, then @md, though @md's threshold is the smallest.
            var names = new[] { "@max-lg:p-2", "@md:p-2", "@max-xl:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 2, 0 }));
        }

        [Test]
        public void Given_APixelAndARemFloor_When_Ordered_Then_ThePixelOneComesFirstWhateverTheThresholds()
        {
            // Arrange — Tailwind emits @min-[1000px] before @min-[10rem], which is 160px.
            var names = new[] { "@min-[10rem]:p-2", "@min-[1000px]:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Given_APixelAndARemCeiling_When_Ordered_Then_ThePixelOneComesFirstWhateverTheThresholds()
        {
            // Arrange — Tailwind emits @max-[1000px] before @max-[10rem].
            var names = new[] { "@max-[10rem]:p-2", "@max-[1000px]:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Given_NamedAndUnnamedQueriesAtOneThreshold_When_Ordered_Then_NamesComeFirstAndTheUnnamedLast()
        {
            // Arrange — Tailwind emits @md/a, @md/b, @md.
            var names = new[] { "@md:p-2", "@md/b:p-2", "@md/a:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 2, 1, 0 }));
        }

        [Test]
        public void Given_AContainerQueryAndAViewportBreakpoint_When_Ordered_Then_TheContainerQueryComesLater()
        {
            // Arrange — Tailwind emits 2xl: before @sm:.
            var names = new[] { "@sm:p-2", "2xl:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Given_AContainerQueryAndDark_When_Ordered_Then_DarkComesLater()
        {
            // Arrange — Tailwind emits @sm: before dark:.
            var names = new[] { "dark:p-2", "@sm:p-2" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }
    }
}
