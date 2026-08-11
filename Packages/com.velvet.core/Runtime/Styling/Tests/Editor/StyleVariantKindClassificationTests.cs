using System;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the classification surface answers for a value that names no
    /// <see cref="StyleVariantKind"/>, and that the two responsive questions stay one question.
    /// Coverage of the named members is the compiler's job — <c>Runtime/csc.rsp</c> raises CS8509 to an
    /// error, so a member no arm covers does not build — and asking it here as well would only pass,
    /// since no tree that fails it can be compiled to run the case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleVariantKindClassificationTests
    {
        private static StyleVariantKind UnnamedKind() =>
            (StyleVariantKind)(Enum.GetValues(typeof(StyleVariantKind)).Cast<int>().Max() + 1);

        // GREEN_ON_BASE(characterization): pins the delegation both summaries claim, against either
        // growing its own arms.
        [Test]
        public void Given_EveryNamedKind_When_BothResponsiveQuestionsAreAsked_Then_TheyAgree()
        {
            // Act
            var disagreeing = Enum.GetValues(typeof(StyleVariantKind))
                .Cast<StyleVariantKind>()
                .Where(kind => StyleVariantClass.IsResponsive(kind) != StyleVariantClass.BreakpointPx(kind) > 0f)
                .ToList();

            // Assert
            Assert.That(disagreeing, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): the refusal is the behaviour the CHANGELOG now records, and
        // naming the type is what separates it from a member that throws for a reason of its own.
        [Test]
        public void Given_AValueNamingNoKind_When_BreakpointPxIsAsked_Then_ItIsRefused()
        {
            // Arrange
            var unnamed = UnnamedKind();
            Assume.That(Enum.IsDefined(typeof(StyleVariantKind), unnamed), Is.False,
                "the probe value has to name no member for the refusal to be what is measured");

            // Assert
            Assert.That(() => StyleVariantClass.BreakpointPx(unnamed),
                Throws.InstanceOf<SwitchExpressionException>());
        }

        // GREEN_ON_BASE(characterization): the refusal is the behaviour the CHANGELOG now records, and
        // naming the type is what separates it from a member that throws for a reason of its own.
        [Test]
        public void Given_AValueNamingNoKind_When_IsResponsiveIsAsked_Then_ItIsRefused()
        {
            // Arrange
            var unnamed = UnnamedKind();
            Assume.That(Enum.IsDefined(typeof(StyleVariantKind), unnamed), Is.False,
                "the probe value has to name no member for the refusal to be what is measured");

            // Assert
            Assert.That(() => StyleVariantClass.IsResponsive(unnamed),
                Throws.InstanceOf<SwitchExpressionException>());
        }
    }
}
