using System;
using NUnit.Framework;
using UnityEngine;
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

        private static bool s_boxMounted;

        // The box stands under "shared-box" while s_boxMounted.
        [Component]
        private static VNode RemovableBoxRender()
        {
            var (_, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: s_boxMounted
                ? new VNode[] { V.Motion(name: "shared", layoutId: "shared-box", className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]") }
                : Array.Empty<VNode>());
        }

        [Test]
        public void Given_TheOnlyLayoutIdMotionHoldingAnId_When_ItLeaves_Then_TheIdKeepsNoMembers()
        {
            // Arrange
            s_boxMounted = true;
            _mounted = V.Mount(Root, V.Component(RemovableBoxRender, key: "root"));
            Tick();

            // Act
            s_boxMounted = false;
            s_setStep.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.LayoutIdMembers.ContainsKey("shared-box"), Is.False);
        }

        // A holder torn down outside a render, as a recycled virtual-list row is, leaves its box and no entry naming it.
        private static readonly StyleTransitionConfig s_slow = new() { DurationSec = 1f, Easing = EasingMode.Linear };

        private static readonly System.Collections.Generic.Dictionary<string, MotionVariant> s_hide = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static bool s_cardOpen;

        // A frame that widens threefold at step 1, holding a layoutId Motion rotated 45 degrees.
        [Component]
        private static VNode RotatedInStretchedRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "frame", layoutId: "frame", transition: s_slow,
                    className: $"absolute left-[0px] top-[0px] w-[{(step == 0 ? 100 : 300)}px] h-[100px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner", transition: s_slow,
                            className: "absolute left-[10px] top-[10px] w-[40px] h-[40px] rotate-[45deg]"),
                    }),
            });
        }

        // The image of a vector under the scale and the rotate each element from this one up to the root writes.
        private Vector2 DrawnVector(VisualElement element, Vector2 vector)
        {
            for (var e = element; e != Root; e = e.hierarchy.parent)
            {
                var scale = e.style.scale.keyword == StyleKeyword.Undefined ? (Vector2)e.style.scale.value.value : Vector2.one;
                var degrees = e.style.rotate.keyword == StyleKeyword.Undefined ? e.style.rotate.value.angle.ToDegrees() : 0f;
                vector = Quaternion.Euler(0f, 0f, degrees) * Vector2.Scale(scale, vector);
            }
            return vector;
        }

        [Test]
        public void Given_ARotatedLayoutIdMotionInAMotionStretchingOnOneAxis_When_TheStretchPlays_Then_ItsAxesAreDrawnAtTheirOwnLengths()
        {
            // Arrange
            _mounted = V.Mount(Root, V.Component(RotatedInStretchedRender, key: "root"));
            Tick();
            var inner = Root.Q<VisualElement>("inner");

            // Act
            s_setStep.Invoke(1);
            _mounted.FlushStateForTest();
            Tick();

            // Assert — the frame is drawn narrower than its layout, and the card's axes are not stretched with it.
            var stretched = Root.Q<VisualElement>("frame").style.scale.value.value.x < 0.9f;
            var undistorted = Mathf.Abs(DrawnVector(inner, Vector2.right).magnitude - 1f) < 0.02f
                && Mathf.Abs(DrawnVector(inner, Vector2.up).magnitude - 1f) < 0.02f;
            Assert.That((stretched, undistorted), Is.EqualTo((true, true)));
        }

        // A frame holding a presence whose card, rotated and holding a layoutId, leaves with a fade.
        [Component]
        private static VNode RotatedCardInPresenceRender()
        {
            var (_, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "frame", layoutId: "frame", transition: s_slow,
                    className: "absolute left-[0px] top-[0px] w-[400px] h-[200px]",
                    children: new VNode[]
                    {
                        V.AnimatePresence(key: "presence", children: s_cardOpen
                            ? new VNode[]
                            {
                                V.Motion(key: "card", name: "card", layoutId: "card", variants: s_hide, animate: "visible",
                                    exit: "hidden", transition: s_slow,
                                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px] rotate-[30deg]"),
                            }
                            : Array.Empty<VNode>()),
                    }),
            });
        }

        [Test]
        public void Given_ARotatedLayoutIdCardInAPresenceInsideAnother_When_ItsExitCompletes_Then_ItsWrapperLeavesWithIt()
        {
            // Arrange
            s_cardOpen = true;
            _mounted = V.Mount(Root, V.Component(RotatedCardInPresenceRender, key: "root"));
            Tick();
            var wrappedWhileOpen = Root.Q<VisualElement>("card").parent.ClassListContains("velvet-clip-path-wrapper");

            // Act
            s_cardOpen = false;
            s_setStep.Invoke(1);
            _mounted.FlushStateForTest();
            AdvancePast(2f);

            // Assert
            Assert.That((wrappedWhileOpen, Root.Q<VisualElement>("frame").childCount), Is.EqualTo((true, 0)));
        }

        private static int s_rows;

        // s_rows boxes, each under an id of its own.
        [Component]
        private static VNode ManyIdsRender()
        {
            var (_, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var rows = new VNode[s_rows];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = V.Motion(key: $"row-{i}", layoutId: $"row-{i}", className: "absolute left-[0px] top-[0px] w-[10px] h-[10px]");
            }
            return V.Div(children: rows);
        }

        [Test]
        public void Given_MoreIdsThanTheCapHoldingBoxes_When_TheirMotionsLeave_Then_OnlyTheCapKeepsABox()
        {
            // Arrange
            s_rows = 300;
            _mounted = V.Mount(Root, V.Component(ManyIdsRender, key: "root"));
            Tick();
            s_rows = 0;

            // Act
            s_setStep.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.LayoutIdRegistry.Count, Is.EqualTo(256));
        }

        [Test]
        public void Given_TheOnlyLayoutIdMotionHoldingAnId_When_ItIsTornDownOutsideARender_Then_TheIdKeepsItsBoxWithoutTheElement()
        {
            // Arrange
            s_boxMounted = true;
            _mounted = V.Mount(Root, V.Component(RemovableBoxRender, key: "root"));
            Tick();
            var ctx = _mounted.Root.Reconciler.Context;

            // Act — the cleanup a virtual list gives a row it recycles, with no render in progress, and the row taken out
            // of the tree as the list takes it.
            var shared = Root.Q<VisualElement>("shared");
            ((IReconcilerBridge)_mounted.Root.Reconciler).CleanupElementForController(shared);
            shared.RemoveFromHierarchy();

            // Assert
            ctx.LayoutIdRegistry.TryGetValue("shared-box", out var entry);
            Assert.That((entry.Element, entry.Box.HasValue), Is.EqualTo(((VisualElement)null, true)));
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
    }
}
