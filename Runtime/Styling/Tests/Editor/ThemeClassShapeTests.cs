using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which classes the reconciler keeps for a theme sheet to configure again, read with no mount's scope
    /// open: a class whose utility a theme family opens and nothing else gives a meaning to is one, a bundled utility
    /// is not, and a class holding no utility after its variants is not. GWT, one assert per case.
    /// </summary>
    internal sealed class ThemeClassShapeTests
    {
        // GREEN_ON_BASE(refactor): the base gives both verdicts; the rewrite that stops it cutting the utility out early keeps them.
        [TestCase("hover:bg-brandtest", true)]
        [TestCase("hover:text-red-500", false)]
        public void Given_AVariantOverAThemeFamilyClass_When_AskedWhetherItMayBecomeAThemeClass_Then_ItIsOneUnlessItsUtilityIsBundled(
            string core, bool expected)
        {
            // Act
            var may = StyleThemeClass.MayBecomeThemeClass(core);

            // Assert
            Assert.That(may, Is.EqualTo(expected));
        }

        // "!" leaves the empty class once its bang is stripped.
        [TestCase("hover:")]
        [TestCase("")]
        public void Given_AClassHoldingNothingAfterItsVariants_When_AskedWhetherItMayBecomeAThemeClass_Then_ItIsNot(
            string core)
        {
            // Act
            var may = StyleThemeClass.MayBecomeThemeClass(core);

            // Assert
            Assert.That(may, Is.False);
        }
    }
}
