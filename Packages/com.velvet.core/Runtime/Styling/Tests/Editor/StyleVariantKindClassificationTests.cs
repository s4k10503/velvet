using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <see cref="StyleVariantClass.BreakpointPx"/> and <see cref="StyleVariantClass.IsResponsive"/>
    /// answer for a value that names no <see cref="StyleVariantKind"/>, and that the two of them stay one
    /// question. <see cref="StyleVariantClass.RelationalOf"/> is enumerated over the named members, and
    /// <see cref="StyleVariantClass.BreakpointPx"/> by the agreement case beside it.
    /// </summary>
    [TestFixture]
    internal sealed class StyleVariantKindClassificationTests
    {
        private static StyleVariantKind UnnamedKind() =>
            (StyleVariantKind)(Enum.GetValues(typeof(StyleVariantKind)).Cast<int>().Max() + 1);

        // GREEN_ON_BASE(characterization): every named kind was answered on the base as well, where a
        // switch did the lookup; VariantKindTableTests holds the table itself.
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
                catch (ArgumentOutOfRangeException)
                {
                    refused.Add(kind);
                }
            }

            // Assert
            Assert.That(refused, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): the only enumeration of BreakpointPx over the named members,
        // and it separates the two the moment a responsive kind reaches one of them and not the other. It
        // does not pin the delegation itself: a rewritten IsResponsive listing the same five kinds agrees
        // with BreakpointPx and keeps this green.
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

        // The type is named to separate the refusal the CHANGELOG records from a member throwing for a
        // reason of its own; it is the lookup table's refusal of a value naming no kind.
        [Test]
        public void Given_AValueNamingNoKind_When_BreakpointPxIsAsked_Then_ItIsRefused()
        {
            // Assert
            Assert.That(() => StyleVariantClass.BreakpointPx(UnnamedKind()),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        // The type is named to separate the refusal the CHANGELOG records from a member throwing for a
        // reason of its own; it is the lookup table's refusal of a value naming no kind.
        [Test]
        public void Given_AValueNamingNoKind_When_IsResponsiveIsAsked_Then_ItIsRefused()
        {
            // Assert
            Assert.That(() => StyleVariantClass.IsResponsive(UnnamedKind()),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }
    }
}
