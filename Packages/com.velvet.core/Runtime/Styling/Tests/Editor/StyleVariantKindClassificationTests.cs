using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <see cref="StyleVariantClass.BreakpointPx"/> and <see cref="StyleVariantClass.IsResponsive"/>
    /// answer for a value that names no <see cref="StyleVariantKind"/>, and that the two of them stay one
    /// question. <see cref="StyleVariantClass.RelationalOf"/> is enumerated over the named members, which
    /// nothing else does — <see cref="StyleVariantClass.BreakpointPx"/> is enumerated by the agreement case
    /// beside it.
    /// <para>
    /// That enumeration is not redundant with CS8509 being an error, because nothing here establishes that
    /// the flag reaches the compiler: <c>ExhaustiveSwitchSeverityTests</c> reads the response file, not the
    /// build.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class StyleVariantKindClassificationTests
    {
        private static StyleVariantKind UnnamedKind() =>
            (StyleVariantKind)(Enum.GetValues(typeof(StyleVariantKind)).Cast<int>().Max() + 1);

        // GREEN_ON_BASE(characterization): the only enumeration of RelationalOf. Its other defence is the
        // compiler flag, and no case here establishes that the flag reaches the build.
        [Test]
        public void Given_EveryNamedKind_When_RelationalOfIsAsked_Then_NoneIsRefused()
        {
            // Act
            var refused = new List<StyleVariantKind>();
            foreach (var kind in Enum.GetValues(typeof(StyleVariantKind)).Cast<StyleVariantKind>())
            {
                try
                {
                    StyleVariantClass.RelationalOf(kind);
                }
                catch (SwitchExpressionException)
                {
                    refused.Add(kind);
                }
            }

            // Assert
            Assert.That(refused, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): pins the delegation both summaries claim, against either
        // growing its own arms. Also the only enumeration of BreakpointPx over the named members.
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

        // GREEN_ON_BASE(characterization): the refusal is the behaviour the CHANGELOG now records. The type
        // is named to separate it from a member throwing for a reason of its own; it is the compiler's
        // signature for the absent arm rather than a contract, so a deliberate move to
        // ArgumentOutOfRangeException updates this line rather than being blocked by it.
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

        // GREEN_ON_BASE(characterization): the refusal is the behaviour the CHANGELOG now records. The type
        // is named to separate it from a member throwing for a reason of its own; it is the compiler's
        // signature for the absent arm rather than a contract, so a deliberate move to
        // ArgumentOutOfRangeException updates this line rather than being blocked by it.
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
