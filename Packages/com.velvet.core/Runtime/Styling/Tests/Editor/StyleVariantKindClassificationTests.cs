using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the discard-less classification switches over <see cref="StyleVariantKind"/> answer:
    /// every named member is classified by each of them, and a value naming no member is refused rather
    /// than answered. The compiler raises CS8509 for the first and CS8524 for the second, and the enum's
    /// own remarks lean on that; these cases are what makes either survive a discard arm being added.
    /// </summary>
    [TestFixture]
    internal sealed class StyleVariantKindClassificationTests
    {
        private static IEnumerable<StyleVariantKind> NamedKinds() =>
            Enum.GetValues(typeof(StyleVariantKind)).Cast<StyleVariantKind>();

        private static StyleVariantKind UnnamedKind() =>
            (StyleVariantKind)(NamedKinds().Cast<int>().Max() + 1);

        private static List<StyleVariantKind> Unclassified(Action<StyleVariantKind> classify)
        {
            var refused = new List<StyleVariantKind>();
            foreach (var kind in NamedKinds())
            {
                try
                {
                    classify(kind);
                }
                catch (Exception)
                {
                    refused.Add(kind);
                }
            }
            return refused;
        }

        // GREEN_ON_BASE(characterization): a discard arm added later silences CS8509 and leaves this red.
        [Test]
        public void Given_EveryNamedKind_When_BreakpointPxIsAsked_Then_NoneIsUnclassified()
        {
            // Act
            var refused = Unclassified(kind => StyleVariantClass.BreakpointPx(kind));

            // Assert
            Assert.That(refused, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): a discard arm added later silences CS8509 and leaves this red.
        [Test]
        public void Given_EveryNamedKind_When_RelationalOfIsAsked_Then_NoneIsUnclassified()
        {
            // Act
            var refused = Unclassified(kind => StyleVariantClass.RelationalOf(kind));

            // Assert
            Assert.That(refused, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): pins the delegation both summaries claim, against either growing
        // its own arms.
        [Test]
        public void Given_EveryNamedKind_When_BothResponsiveQuestionsAreAsked_Then_TheyAgree()
        {
            // Act
            var disagreeing = NamedKinds()
                .Where(kind => StyleVariantClass.IsResponsive(kind) != StyleVariantClass.BreakpointPx(kind) > 0f)
                .ToList();

            // Assert
            Assert.That(disagreeing, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): the refusal is the recorded behaviour, so a discard arm returning
        // zero cannot come back unnoticed.
        [Test]
        public void Given_AValueNamingNoKind_When_BreakpointPxIsAsked_Then_ItIsRefused()
        {
            // Arrange
            var unnamed = UnnamedKind();
            Assume.That(Enum.IsDefined(typeof(StyleVariantKind), unnamed), Is.False);

            // Assert
            Assert.That(() => StyleVariantClass.BreakpointPx(unnamed), Throws.Exception);
        }

        // GREEN_ON_BASE(characterization): the refusal is the recorded behaviour, so a discard arm returning
        // false cannot come back unnoticed.
        [Test]
        public void Given_AValueNamingNoKind_When_IsResponsiveIsAsked_Then_ItIsRefused()
        {
            // Arrange
            var unnamed = UnnamedKind();
            Assume.That(Enum.IsDefined(typeof(StyleVariantKind), unnamed), Is.False);

            // Assert
            Assert.That(() => StyleVariantClass.IsResponsive(unnamed), Throws.Exception);
        }
    }
}
