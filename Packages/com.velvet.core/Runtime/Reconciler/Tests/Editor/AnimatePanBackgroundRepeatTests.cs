using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The inline <c>background-repeat</c> under a pan loop (<c>animate-gradient</c> / <c>animate-shimmer</c>):
    /// the loop holds <c>no-repeat</c> while it runs, and when it stops the element's own value comes back — a
    /// <see cref="StyleOverrides.BackgroundRepeat"/>, the newest one if it changed meanwhile, or none, which
    /// leaves the slot to the classes.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePanBackgroundRepeatTests : PanelTestBase
    {
        private const string GradientBase = "w-[100px] h-[40px] bg-gradient-to-r to-blue-500";

        private static readonly BackgroundRepeat Tiled = new(Repeat.Repeat, Repeat.Repeat);
        private static readonly BackgroundRepeat AcrossOnly = new(Repeat.Repeat, Repeat.NoRepeat);

        private static StateUpdater<int> s_setStep;
        private static Func<int, (string ClassName, StyleOverrides Styles)> s_nodeFor;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setStep = default;
            s_nodeFor = _ => (GradientBase, null);
        }

        [Component]
        private static VNode RenderCard()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var (className, styles) = s_nodeFor(step);
            return V.Div(className: className, name: "card", styles: styles);
        }

        private VisualElement Card => _window.rootVisualElement.Q<VisualElement>("card");

        private void Mount(Func<int, (string, StyleOverrides)> nodeFor)
        {
            s_nodeFor = nodeFor;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
        }

        private void Step(int n)
        {
            s_setStep.Invoke(n);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static StyleOverrides Repeating(BackgroundRepeat repeat)
            => new StyleOverrides { BackgroundRepeat = new StyleBackgroundRepeat(repeat) };

        [Test]
        public void Given_ARepeatOverrideUnderAShimmer_When_TheShimmerStops_Then_TheOverrideIsBack()
        {
            // Arrange — read while the loop runs, so a loop that never took the slot cannot pass.
            var styles = Repeating(Tiled);
            Mount(step => (step == 0 ? GradientBase + " animate-shimmer" : GradientBase, styles));
            var running = Card.style.backgroundRepeat.value;

            // Act
            Step(1);

            // Assert
            var stopped = Card.style.backgroundRepeat;
            Assert.That((running.x, stopped.keyword, stopped.value.x, stopped.value.y),
                Is.EqualTo((Repeat.NoRepeat, StyleKeyword.Undefined, Repeat.Repeat, Repeat.Repeat)));
        }

        [Test]
        public void Given_ARepeatOverrideChangedUnderAGradientPan_When_ThePanStops_Then_TheNewestOverrideIsBack()
        {
            // Arrange
            Mount(step => step switch
            {
                0 => (GradientBase + " animate-gradient", Repeating(Tiled)),
                1 => (GradientBase + " animate-gradient", Repeating(AcrossOnly)),
                _ => (GradientBase, Repeating(AcrossOnly)),
            });
            Step(1);
            var running = Card.style.backgroundRepeat.value;

            // Act
            Step(2);

            // Assert — the pan keeps its no-repeat through the change and hands back the changed value.
            var stopped = Card.style.backgroundRepeat.value;
            Assert.That((running.x, stopped.x, stopped.y), Is.EqualTo((Repeat.NoRepeat, Repeat.Repeat, Repeat.NoRepeat)));
        }

        // GREEN_ON_BASE(characterization): the base already writes a repeat override beside a loop that is no pan.
        // The pan's hold must not reach a loop that never took the slot.
        [Test]
        public void Given_APulseLoop_When_TheRepeatOverrideChanges_Then_ItIsWrittenAtOnce()
        {
            // Arrange
            Mount(step => (GradientBase + " animate-pulse", Repeating(step == 0 ? Tiled : AcrossOnly)));

            // Act
            Step(1);

            // Assert
            var written = Card.style.backgroundRepeat.value;
            Assert.That((written.x, written.y), Is.EqualTo((Repeat.Repeat, Repeat.NoRepeat)));
        }

        // GREEN_ON_BASE(characterization): the base already clears the slot when a pan stops over no override.
        // That is the element's own value when it declares none; this pins that the restore keeps doing so.
        [Test]
        public void Given_NoRepeatOverrideUnderAShimmer_When_TheShimmerStops_Then_TheSlotIsLeftToTheClasses()
        {
            // Arrange
            Mount(step => (step == 0 ? GradientBase + " animate-shimmer" : GradientBase, null));
            var running = Card.style.backgroundRepeat;

            // Act
            Step(1);

            // Assert
            Assert.That((running.keyword, Card.style.backgroundRepeat.keyword),
                Is.EqualTo((StyleKeyword.Undefined, StyleKeyword.Null)));
        }
    }
}
