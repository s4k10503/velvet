using System.Collections.Generic;
using System.Linq;
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

        // GREEN_ON_BASE(characterization): an engine fact the guard's list rewrite orders its reads and writes
        // around, pinned so a change to it fails here rather than in a mid-tween paint.
        [Test]
        public void Given_AListReadOutOfTheSlot_When_TheSlotIsWrittenAgain_Then_TheReadListHoldsTheNewValue()
        {
            // Arrange
            var element = new VisualElement();
            element.style.transitionProperty = new List<StylePropertyName> { new("translate"), new("opacity") };
            var read = element.style.transitionProperty.value;

            // Act
            element.style.transitionProperty = new List<StylePropertyName> { new("opacity") };

            // Assert
            Assert.That(read.Count, Is.EqualTo(1));
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

        [TestCase(MotionTransitionSlots.Opacity, "filter,opacity")]
        [TestCase(MotionTransitionSlots.Translate, "filter,translate")]
        [TestCase(MotionTransitionSlots.Scale, "filter,scale")]
        [TestCase(MotionTransitionSlots.Rotate, "filter,rotate")]
        [TestCase(MotionTransitionSlots.Color,
            "background-color,border-bottom-color,border-left-color,border-right-color,border-top-color,color,filter")]
        [TestCase(MotionTransitionSlots.Length,
            "border-bottom-left-radius,border-bottom-right-radius,border-bottom-width,border-left-width,"
            + "border-right-width,border-top-left-radius,border-top-right-radius,border-top-width,bottom,"
            + "filter,flex-basis,font-size,height,left,letter-spacing,margin-bottom,margin-left,margin-right,margin-top,"
            + "max-height,max-width,min-height,min-width,padding-bottom,padding-left,padding-right,padding-top,"
            + "right,top,width")]
        [TestCase(MotionTransitionSlots.Filter, "filter")]
        [TestCase(MotionTransitionSlots.BackgroundPosition, "background-position-x,background-position-y,filter")]
        public void Given_ATweenHoldingTransitionPropertyAll_When_APlayStarts_Then_WhatThePlayDrivesIsLeftOutOfTheList(
            MotionTransitionSlots drivenSlots, string expectedLeftOut)
        {
            // Arrange — the list a variant tween writes. Filter is left out of every expansion, whatever the
            // play drives.
            var element = new VisualElement();
            element.style.transitionProperty = new List<StylePropertyName> { new("all") };
            element.style.transitionDuration = new List<TimeValue> { new(350, TimeUnit.Millisecond) };

            // Act
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, new object(), drivenSlots);

            // Assert
            Assert.That(LonghandsLeftOut(element), Is.EqualTo(expectedLeftOut));
        }

        [Test]
        public void Given_ATweenListNamingFilter_When_AFilterPlayStarts_Then_OnlyFilterLeavesTheList()
        {
            // Arrange — a per-property list, which names filter itself rather than through `all`.
            var element = new VisualElement();
            element.style.transitionProperty = new List<StylePropertyName> { new("filter"), new("opacity") };
            element.style.transitionDuration = new List<TimeValue>
            {
                new(100, TimeUnit.Millisecond), new(350, TimeUnit.Millisecond),
            };

            // Act
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, new object(), MotionTransitionSlots.Filter);

            // Assert
            Assert.That(string.Join(",", element.style.transitionProperty.value), Is.EqualTo("opacity"));
        }

        [Test]
        public void Given_APerPropertyTweenList_When_APlayDrivingOneOfItsPropertiesStarts_Then_TheRestKeepTheirOwnTimings()
        {
            // Arrange — the four positional lists a per-property swap writes, every entry distinct.
            var element = new VisualElement();
            element.style.transitionProperty = new List<StylePropertyName> { new("translate"), new("opacity") };
            element.style.transitionDuration = new List<TimeValue>
            {
                new(10, TimeUnit.Millisecond), new(350, TimeUnit.Millisecond),
            };
            element.style.transitionTimingFunction = new List<EasingFunction>
            {
                new(EasingMode.Linear), new(EasingMode.EaseIn),
            };
            element.style.transitionDelay = new List<TimeValue>
            {
                new(0, TimeUnit.Millisecond), new(200, TimeUnit.Millisecond),
            };

            // Act
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, new object(), MotionTransitionSlots.Translate);

            // Assert — opacity alone, still on its own duration, easing and delay.
            Assert.That(
                string.Join(",", element.style.transitionProperty.value)
                + "|" + string.Join(",", element.style.transitionDuration.value)
                + "|" + string.Join(",", element.style.transitionTimingFunction.value.Select(e => e.mode))
                + "|" + string.Join(",", element.style.transitionDelay.value),
                Is.EqualTo("opacity|350ms|EaseIn|200ms"));
        }

        // Every longhand the element's inline transition-property does not name, sorted and joined.
        private static string LonghandsLeftOut(VisualElement element)
        {
            var listed = new HashSet<string>();
            foreach (var name in element.style.transitionProperty.value)
            {
                listed.Add(name.ToString());
            }
            var leftOut = new List<string>();
            for (var i = 0; i < StyleUtilityProperties.LonghandCount; i++)
            {
                var ussName = StyleUtilityProperties.UssName((StyleLonghand)i);
                if (!listed.Contains(ussName))
                {
                    leftOut.Add(ussName);
                }
            }
            leftOut.Sort(System.StringComparer.Ordinal);
            return string.Join(",", leftOut);
        }
    }
}
