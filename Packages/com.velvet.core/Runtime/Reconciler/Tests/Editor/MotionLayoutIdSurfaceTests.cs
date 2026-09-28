using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The layoutId cases that reach <see cref="StyleTransitionConfig.Layout"/> or the driver's bookkeeping on
    /// <c>ReconcilerContext</c>. They sit apart from <see cref="MotionScheduledMechanicsTests"/> so that fixture
    /// still builds on a tree without those members.
    /// </summary>
    [TestFixture]
    internal sealed class MotionLayoutIdSurfaceTests : MotionSimulatedPanelTestsBase
    {
        private static readonly StyleTransitionConfig s_spring =
            new() { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f };

        private static StateUpdater<int> s_setStep;
        private static StyleTransitionConfig s_transition;
        private static float? s_duration;

        private MountedTree _mounted;

        [TearDown]
        public override void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            base.TearDown();
        }

        // Step 1 moves the box 200px right on s_transition and s_duration.
        [Component]
        private static VNode MovingBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "shared", layoutId: "shared-box", transition: s_transition, duration: s_duration,
                    className: $"absolute left-[{step * 200}px] top-[0px] w-[100px] h-[100px]"),
            });
        }

        // Mounts MovingBoxRender and plays step 1 up to the frame its layout settles on.
        private VisualElement MoveBox(StyleTransitionConfig transition, float? duration = null)
        {
            s_transition = transition;
            s_duration = duration;
            _mounted = V.Mount(Root, V.Component(MovingBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            _mounted.FlushStateForTest();
            Tick();
            return Root.Q<VisualElement>("shared");
        }

        private static float TranslateX(VisualElement element) =>
            element.style.translate.keyword == StyleKeyword.Null ? 0f : element.style.translate.value.x.value;

        [Test]
        public void Given_ALayoutIdMotionWithALayoutTransition_When_ItMoves_Then_ItTweensThoughItsOwnTransitionLandsAtOnce()
        {
            // Arrange / Act — the Motion's own transition has no duration; its Layout is a spring.
            var element = MoveBox(new StyleTransitionConfig { Layout = s_spring });

            // Assert
            Assert.That(TranslateX(element), Is.LessThan(-50f));
        }

        [Test]
        public void Given_ALayoutIdMotionWithALayoutTransition_When_VMotionIsGivenADuration_Then_ItStillTweensOnItsLayout()
        {
            // Arrange / Act — the duration parameter rebuilds the transition through With().
            var element = MoveBox(new StyleTransitionConfig { Layout = s_spring }, duration: 0f);

            // Assert
            Assert.That(TranslateX(element), Is.LessThan(-50f));
        }

        [Test]
        public void Given_ALayoutIdTween_When_ItHasSettled_Then_ItsPanelRunsNoFrameForIt()
        {
            // Arrange
            MoveBox(s_spring);

            // Act
            AdvancePast(3f);

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.LayoutIdFrames, Is.Empty);
        }

        private static StateUpdater<bool> s_setMoved;

        // One layoutId Motion, moved from the second parent to the first at the same box.
        [Component]
        private static VNode AcrossParentsBoxRender()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setMoved = setMoved;
            VNode Box() => V.Motion(name: "shared", layoutId: "shared-box", transition: s_spring,
                className: "left-[200px] top-[0px] w-[100px] h-[100px]");
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", children: moved ? new[] { Box() } : Array.Empty<VNode>()),
                V.Div(key: "second", children: moved ? Array.Empty<VNode>() : new[] { Box() }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionThatNeverMoved_When_ItMovesToAnotherParentAtTheSameBox_Then_NoProjectionRunsOnTheReplacement()
        {
            // Arrange
            _mounted = V.Mount(Root, V.Component(AcrossParentsBoxRender, key: "root"));
            Tick();

            // Act
            s_setMoved.Invoke(true);
            _mounted.FlushStateForTest();
            Tick();

            // Assert
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That(_mounted.Root.Reconciler.Context.LayoutIdProjections.ContainsKey(replacement), Is.False);
        }
    }
}
