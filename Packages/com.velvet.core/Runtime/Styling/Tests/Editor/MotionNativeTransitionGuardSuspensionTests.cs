using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The observable half of <see cref="MotionNativeTransitionGuard"/>: whether a play suspends the element's
    /// native transitions, which is the only thing its declared-slot answer is consulted for. Suspension is
    /// element-wide, so suspending an element whose cascade transitions nothing the play drives makes its
    /// unrelated transitions land instantly for the play's whole duration.
    /// </summary>
    [TestFixture]
    internal sealed class MotionNativeTransitionGuardSuspensionTests
    {
        private static VisualElement Classed(params string[] classNames)
        {
            var element = new VisualElement();
            foreach (var className in classNames)
            {
                element.AddToClassList(className);
            }
            return element;
        }

        private static bool Suspends(VisualElement element, MotionTransitionSlots drivenSlots)
        {
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, new object(), drivenSlots);
            return element.style.transitionProperty.keyword != StyleKeyword.Null;
        }

        [Test]
        public void Given_TransitionAllThenTransitionNone_When_AnOpacityPlayRuns_Then_NothingIsSuspended()
        {
            // Arrange — the cascade leaves this element transitioning nothing at all.
            var element = Classed("transition-all", "transition-none");

            // Act
            var suspended = Suspends(element, MotionTransitionSlots.Opacity);

            // Assert
            Assert.That(suspended, Is.False);
        }

        [Test]
        public void Given_TransitionTransformThenTransitionColors_When_ATranslatePlayRuns_Then_NothingIsSuspended()
        {
            // Arrange — the later transition-colors replaces the transform list outright, so no transform
            // property is transitioning for a translate play to fight.
            var element = Classed("transition-transform", "transition-colors");

            // Act
            var suspended = Suspends(element, MotionTransitionSlots.Translate);

            // Assert
            Assert.That(suspended, Is.False);
        }

        [Test]
        public void Given_TransitionAllThenTransitionColors_When_AnOpacityPlayRuns_Then_NothingIsSuspended()
        {
            // Arrange — transition-colors is declared later than transition-all, so opacity is not in the
            // resolved list.
            var element = Classed("transition-all", "transition-colors");

            // Act
            var suspended = Suspends(element, MotionTransitionSlots.Opacity);

            // Assert
            Assert.That(suspended, Is.False);
        }

        [Test]
        public void Given_TransitionFilter_When_AnOpacityPlayRuns_Then_NothingIsSuspended()
        {
            // Arrange — the class pins transition-property to filter, which no driver writes.
            var element = Classed("transition-filter");

            // Act
            var suspended = Suspends(element, MotionTransitionSlots.Opacity);

            // Assert
            Assert.That(suspended, Is.False);
        }

        [Test]
        public void Given_TransitionColors_When_AColorPlayRuns_Then_TheElementIsSuspended()
        {
            // Arrange — the case the guard exists for: the class transitions exactly what the play writes.
            var element = Classed("transition-colors");

            // Act
            var suspended = Suspends(element, MotionTransitionSlots.Color);

            // Assert
            Assert.That(suspended, Is.True);
        }

        [Test]
        public void Given_AWrittenTransitionPropertyList_When_TheInlineSlotIsReadBack_Then_ItIsNotTheListThatWasWritten()
        {
            // CHARACTERIZATION PIN, not a requirement on Velvet: the guard decides whether the inline slot is
            // still holding its own suspension by comparing what it reads back by CONTENT, because this editor
            // hands back a different list instance than the one assigned. Should that ever change, an identity
            // comparison becomes available and this is the test that says so.
            //
            // Arrange
            var written = new List<StylePropertyName> { new StylePropertyName("none") };
            var element = new VisualElement();

            // Act
            element.style.transitionProperty = written;

            // Assert
            Assert.That(ReferenceEquals(element.style.transitionProperty.value, written), Is.False);
        }

        [Test]
        public void Given_ADurationOnlyUtility_When_AnOpacityPlayRuns_Then_TheElementIsSuspended()
        {
            // Arrange — a duration with no property utility leaves transition-property at its initial `all`.
            var element = Classed("duration-300");

            // Act
            var suspended = Suspends(element, MotionTransitionSlots.Opacity);

            // Assert
            Assert.That(suspended, Is.True);
        }
    }
}
