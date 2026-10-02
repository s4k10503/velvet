using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class StyleRuleOrderTests
    {
        [Test]
        public void Given_AnInvisibleAndABackgroundRuleOfOneVariant_When_Ordered_Then_InvisibleTakesTheEarlierPlace()
        {
            // Arrange — Tailwind 4.3.3 emits hover:invisible before hover:bg-red-500, its property order putting
            // visibility before background-color.
            var classNames = new[] { "hover:bg-red-500", "hover:invisible" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(classNames);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }
    }
}
