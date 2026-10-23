using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the animator <c>Hooks.UseAnimate</c> hands out: how a sequence lays its segments on the timeline, what its
    /// controls do and which elements a selector names inside the scope.
    /// </summary>
    [TestFixture]
    internal sealed class UseAnimateTests
    {
        private static StyleTransitionConfig OneSecondLinear()
            => new() { DurationSec = 1f, Easing = EasingMode.Linear };

        private static Animator NewAnimator() => new(new Ref<VisualElement>());

        [Test]
        public void Given_TwoSegmentsPlacedTogether_When_HalfASecondPasses_Then_BothAreHalfwayThroughTheirOwnValues()
        {
            // Arrange
            var (faded, moved) = (new VisualElement(), new VisualElement());
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(faded, new AnimateValues { Opacity = new[] { 0f, 1f } }, OneSecondLinear()),
                new AnimateSegment(moved, new AnimateValues { X = new[] { 0f, 100f } }, OneSecondLinear(), AnimateAt.WithPrevious),
            });

            // Act
            controls.Tick(0.5f);

            // Assert
            Assert.That((faded.style.opacity.value, moved.style.translate.value.x.value), Is.EqualTo((0.5f, 50f)));
        }

        [Test]
        public void Given_ASegmentOffsetPastThePreviousEnd_When_ThePreviousEndsAndTheOffsetHasNotPassed_Then_ItHasNotStarted()
        {
            // Arrange
            var (first, second) = (new VisualElement(), new VisualElement());
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(first, new AnimateValues { Opacity = new[] { 0f, 1f } }, OneSecondLinear()),
                new AnimateSegment(second, new AnimateValues { X = new[] { 0f, 100f } }, OneSecondLinear(), AnimateAt.Offset(0.5f)),
            });

            // Act
            controls.Tick(1f);

            // Assert
            Assert.That(second.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ASegmentOffsetPastThePreviousEnd_When_TheWholeTimelineHasPassed_Then_TheControlsAreFinished()
        {
            // Arrange
            var (first, second) = (new VisualElement(), new VisualElement());
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(first, new AnimateValues { Opacity = new[] { 0f, 1f } }, OneSecondLinear()),
                new AnimateSegment(second, new AnimateValues { X = new[] { 0f, 100f } }, OneSecondLinear(), AnimateAt.Offset(0.5f)),
            });

            // Act
            controls.Tick(2.5f);

            // Assert
            Assert.That((controls.IsFinished, second.style.translate.value.x.value), Is.EqualTo((true, 100f)));
        }

        [Test]
        public void Given_ASegmentPlacedAtATime_When_ThatTimeHasNotCome_Then_TheElementIsUntouched()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(element, new AnimateValues { Opacity = new[] { 0f, 1f } }, OneSecondLinear(), AnimateAt.Time(2f)),
            });

            // Act
            controls.Tick(1f);

            // Assert
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnAnimationBegun_When_ItIsCancelled_Then_TheValueIsBackAtItsFirstKeyframe()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0.2f, 1f } }, OneSecondLinear());
            controls.Tick(0.5f);

            // Act
            controls.Cancel();

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.2f));
        }

        [Test]
        public void Given_APausedAnimation_When_TimePasses_Then_ItDoesNotMove()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } }, OneSecondLinear());
            controls.Pause();

            // Act
            controls.Tick(0.5f);

            // Assert
            Assert.That(controls.TimeSec, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AMountedScope_When_ASelectorNamesElementsByClassAndName_Then_TheyAreTheScopedMatches()
        {
            // Arrange
            var scope = new VisualElement();
            var list = new VisualElement();
            list.AddToClassList("list");
            var (a, b) = (new VisualElement { name = "a" }, new VisualElement { name = "b" });
            a.AddToClassList("row");
            b.AddToClassList("row");
            list.Add(a);
            list.Add(b);
            scope.Add(list);

            // Act
            var matched = new[] { ".row", "#a", ".list .row", ".list #b, #a" }
                .Select(selector => string.Join("+", AnimateSelector.Resolve(scope, selector).Select(e => e.name)));

            // Assert
            Assert.That(string.Join("|", matched), Is.EqualTo("a+b|a|a+b|b+a"));
        }
    }
}
