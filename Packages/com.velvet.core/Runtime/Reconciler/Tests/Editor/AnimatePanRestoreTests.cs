using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What a pan loop (<c>animate-shimmer</c>) leaves behind when it stops: the inline background size and
    /// position the element carried before the loop started, here written by a <c>refCallback</c>.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePanRestoreTests : PanelTestBase
    {
        private const string GradientBase = "w-[100px] h-[40px] bg-gradient-to-r to-blue-500";

        private static StateUpdater<int> s_setStep;
        private static Func<int, string> s_classFor;

        // The element's own background sizing, written once it is attached.
        private static readonly Func<VisualElement, Action> s_ownSizing = element =>
        {
            element.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(Length.Percent(50f), Length.Percent(50f)));
            element.style.backgroundPositionX = new StyleBackgroundPosition(
                new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(10f)));
            element.style.backgroundPositionY = new StyleBackgroundPosition(
                new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(6f)));
            return null;
        };

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setStep = default;
            s_classFor = _ => GradientBase;
        }

        [Component]
        private static VNode RenderCard()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(className: s_classFor(step), name: "card", refCallback: s_ownSizing);
        }

        private VisualElement Card => _window.rootVisualElement.Q<VisualElement>("card");

        private void Step(int n)
        {
            s_setStep.Invoke(n);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_OwnBackgroundSizingUnderAShimmer_When_TheShimmerStops_Then_TheSizingIsBack()
        {
            // Arrange — the loop starts on a patch, after the refCallback wrote the sizing, and is read while
            // it runs, so a loop that never took the slots cannot pass.
            s_classFor = step => step == 1 ? GradientBase + " animate-shimmer" : GradientBase;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
            Step(1);
            var running = Card.style.backgroundSize.value.x.value;

            // Act
            Step(2);

            // Assert
            var size = Card.style.backgroundSize.value;
            var x = Card.style.backgroundPositionX.value;
            var y = Card.style.backgroundPositionY.value;
            Assert.That((running, size.x.value, size.y.value, x.offset.value, y.offset.value),
                Is.EqualTo((100f, 50f, 50f, 10f, 6f)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AShimmerThatHasPannedItsAxis_When_TheShimmerStops_Then_ThatAxisIsBack(bool vertical)
        {
            // Arrange — a laid-out box and the loop's first frame, so it writes its own position over the
            // element's.
            var gradient = vertical ? "w-[100px] h-[40px] bg-gradient-to-b to-blue-500" : GradientBase;
            s_classFor = step => step == 1 ? gradient + " animate-shimmer" : gradient;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
            Step(1);
            ForcePanelUpdate(Card.panel);
            _mounted.Root.Reconciler.Context.AnimationBindings.TryGetValue(Card, out var binding);
            StyleAnimateDriver.ApplyFrame(Card, binding, 0f);
            float Offset() => vertical
                ? Card.style.backgroundPositionY.value.offset.value
                : Card.style.backgroundPositionX.value.offset.value;
            var panned = Offset();

            // Act
            Step(2);

            // Assert — the element's own position on that axis, 6px down or 10px across.
            Assert.That((panned != (vertical ? 6f : 10f), Offset()), Is.EqualTo((true, vertical ? 6f : 10f)));
        }

        [Test]
        public void Given_SizingWrittenFromOutsideDuringAShimmer_When_TheShimmerStops_Then_TheLaterWriteStands()
        {
            // Arrange — written while the loop runs, the way a refCallback or any other imperative write lands.
            s_classFor = step => step == 1 ? GradientBase + " animate-shimmer" : GradientBase;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
            Step(1);
            Card.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(Length.Percent(30f), Length.Percent(30f)));
            Card.style.backgroundPositionX = new StyleBackgroundPosition(
                new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(4f)));

            // Act
            Step(2);

            // Assert
            var size = Card.style.backgroundSize.value;
            var x = Card.style.backgroundPositionX.value;
            Assert.That((size.x.value, x.offset.value), Is.EqualTo((30f, 4f)));
        }
    }
}
