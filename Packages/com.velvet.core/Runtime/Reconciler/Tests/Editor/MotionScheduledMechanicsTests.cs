using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the scheduled Motion mechanics that only run against a REAL (simulated) panel — their
    /// scheduled swap-to-animate, deferred inline-style clears, and layout-driven FLIP inverse all run
    /// through <c>schedule.Execute().ExecuteLater(ms)</c>, which only fires once a panel ticks its
    /// scheduler against its own clock (the batchmode EditMode PlayerLoop never does). Four mechanics:
    /// (1) <c>V.Motion(layoutId:)</c>'s FLIP behavior — when a Motion's resolved layout rect changes
    /// across a re-render while carrying the same layoutId, MotionLayoutIdDriver applies an inverse
    /// inline transform immediately after layout settles at the new rect, then springs it back to zero,
    /// instead of jump-cutting straight to the new pose; (2) <c>StyleTransitionConfig.StaggerChildrenSec</c>
    /// / <c>DelayChildrenSec</c> / <c>When</c> orchestration — a PLAIN parent → child variant-tree (no
    /// AnimatePresence), where a descendant that follows the ambient label (no own <c>animate</c>) claims
    /// a sequential slot ADDED on top of its own declared delay, riding the runtime-swap play's
    /// <c>additionalDelaySec</c> so the claim delays the SWAP itself rather than parking an inline
    /// <c>transition-delay</c>, while a descendant with its own explicit <c>animate</c> opts out entirely
    /// (Framer parity); (3) a standalone <c>V.Motion(variants:, initial:, animate:)</c> mounted with
    /// NO AnimatePresence — Framer parity dictates <c>initial</c>/<c>animate</c> drive the mount enter on
    /// any <c>motion.*</c> component regardless, with the scheduled swap to the resting
    /// <c>variants[animate]</c> firing on the next tick and a later unrelated re-render never replaying
    /// <c>initial</c>; and (4) a classic (tween) variant enter's frame discipline — the from-state must
    /// survive the tick that started the enter, because the panel computes styles only after the timer
    /// queue drains, and the dangerous shape is production's own: the mount runs inside the panel's own
    /// timer tick, so a zero-delay swap item can become runnable in the very tick that mounted the element.
    /// </summary>
    [TestFixture]
    internal sealed class MotionScheduledMechanicsTests : MotionSimulatedPanelTestsBase
    {
        private const float DurationSec = 0.1f;

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static StateUpdater<bool> s_setMoved;
        private static Action<int> s_bump;

        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore(string initial) : base(new SetState(initial)) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState(""));
        }

        private static SetStore s_store;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setMoved = default;
            s_setStop = default;
            s_setStep = default;
            s_tabs = null;
            s_bump = null;
            s_store = null;
        }

        [Component]
        private static VNode SharedBoxRender()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setMoved = setMoved;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "shared",
                    layoutId: "shared-box",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f },
                    className: moved
                        ? "absolute left-[200px] top-[0px] w-[100px] h-[100px]"
                        : "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
            });
        }

        [Test]
        public void Given_ALayoutIdMotion_When_ItsRectChangesAcrossARerender_Then_AnInverseTranslateAppliesImmediatelyAfterLayoutSettles()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            Assume.That(element, Is.Not.Null, "Precondition: the Motion mounted");

            // Act — move the Motion 200px to the right; wait one tick for GeometryChangedEvent to fire and
            // the driver to apply the inverse pose.
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the element is pinned at (roughly) its OLD screen position via an inline translate,
            // even though the resolved layout already moved it 200px right (translate.x ~= -200).
            Assert.That(element.style.translate.value.x.value, Is.LessThan(-50f));
        }

        [Test]
        public void Given_ALayoutIdMotion_When_SeveralTicksElapseAfterARectChange_Then_TheInverseTranslateSettlesToZero()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();
            Assume.That(element.style.translate.value.x.value, Is.LessThan(-50f),
                "Precondition: the inverse pose applied after the rect change");

            // Act — let the spring settle.
            AdvancePast(2f);

            // Assert — the inline translate override is cleared once the spring settles (StyleKeyword.Null),
            // reporting back to StyleKeyword.Auto / 0 the way MotionSpringDriver.ClearInlineOverrides always
            // leaves a settled channel.
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // Where the replacement sits after the move across parents; the mounted element sits at left 200.
        private static int s_movedLeft;

        // One layoutId Motion, moved from the second parent to the first. The first parent reconciles first,
        // so the replacement is created while the element it replaces is still mounted, and the mounted one
        // is never patched in between.
        [Component]
        private static VNode AcrossParentsBoxRender()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setMoved = setMoved;
            VNode Box(int left) => V.Motion(
                name: "shared",
                layoutId: "shared-box",
                transition: new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f },
                className: $"left-[{left}px] top-[0px] w-[100px] h-[100px]");
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", children: moved ? new[] { Box(s_movedLeft) } : Array.Empty<VNode>()),
                V.Div(key: "second", children: moved ? Array.Empty<VNode>() : new[] { Box(200) }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionThatNeverMoved_When_ItMovesToAnotherParentAtTheSameBox_Then_TheReplacementCarriesNoInversePose()
        {
            // Arrange
            s_movedLeft = 200;
            using var mounted = V.Mount(Root, V.Component(AcrossParentsBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();

            // Assert — a new element, standing where the old one stood, with neither an inline translate nor
            // an inline scale.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement), replacement.style.translate.keyword, replacement.style.scale.keyword),
                Is.EqualTo((false, StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALayoutIdMotionThatNeverMoved_When_ItMovesToAnotherParentAtAnotherBox_Then_TheReplacementTweensFromTheOldBoxAtFullSize()
        {
            // Arrange
            s_movedLeft = 400;
            using var mounted = V.Mount(Root, V.Component(AcrossParentsBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near the old element's box, 200px left of its own, at its own size.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement),
                    UnityEngine.Mathf.Abs(replacement.style.translate.value.x.value + 200f) < 50f,
                    replacement.style.scale.keyword),
                Is.EqualTo((false, true, StyleKeyword.Null)));
        }

        private static StateUpdater<int> s_setStep;

        private static readonly StyleTransitionConfig s_layoutSpring =
            new() { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f };

        private static VNode SharedBox(int left, Type elementType = null) => V.Motion(
            name: "shared",
            layoutId: "shared-box",
            elementType: elementType,
            transition: s_layoutSpring,
            className: $"left-[{left}px] top-[0px] w-[100px] h-[100px]");

        // Step 1 is a same-key type flip: the element type changes, so the reconciler tears the mounted
        // element down before it creates the replacement.
        [Component]
        private static VNode TypeFlipBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[] { step == 0 ? SharedBox(200) : SharedBox(200 + step * 200, typeof(Box)) });
        }

        [Test]
        public void Given_ALayoutIdMotion_When_ASameKeyTypeFlipReplacesItElsewhere_Then_TheReplacementTweensFromTheOldBoxAtFullSize()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(TypeFlipBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near the old element's box, 200px left of its own, at its own size.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement),
                    UnityEngine.Mathf.Abs(replacement.style.translate.value.x.value + 200f) < 50f,
                    replacement.style.scale.keyword),
                Is.EqualTo((false, true, StyleKeyword.Null)));
        }

        // GREEN_ON_BASE(characterization): the base tweens a moved Motion from its own previous box.
        // A replacement's registration must outlive the pass boundary that expires teardown boxes.
        [Test]
        public void Given_ASameKeyTypeFlipsReplacementWhoseTweenSettled_When_ItMovesAgain_Then_ItTweensFromItsOwnBox()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(TypeFlipBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            AdvancePast(2f);

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near its own previous box, 200px left of the new one.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That(UnityEngine.Mathf.Abs(replacement.style.translate.value.x.value + 200f), Is.LessThan(50f));
        }

        // Step 1 removes the Motion and step 2 mounts another under the same id: two renders, with no
        // frame between them.
        [Component]
        private static VNode RemoveThenRemountBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: step switch
            {
                0 => new[] { SharedBox(200) },
                1 => Array.Empty<VNode>(),
                _ => new[] { SharedBox(400) },
            });
        }

        // GREEN_ON_BASE(characterization): the base keeps no box for an id whose element left in an earlier render.
        // The box a teardown now leaves for a same-key type flip must expire with its render.
        [Test]
        public void Given_ALayoutIdMotionRemovedInOneRender_When_ALaterRenderMountsAnotherUnderTheSameId_Then_ItCarriesNoInversePose()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(RemoveThenRemountBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            var removed = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert
            var remounted = Root.Q<VisualElement>("shared");
            Assert.That((removed, remounted?.style.translate.keyword),
                Is.EqualTo(((VisualElement)null, (StyleKeyword?)StyleKeyword.Null)));
        }

        // Two 150px-tall parents, one below the other: the box stands at the same place inside either, and
        // 150px apart on screen.
        [Component]
        private static VNode OffsetParentsBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", className: "h-[150px]", children: step == 1 ? new[] { SharedBox(200) } : Array.Empty<VNode>()),
                V.Div(key: "second", className: "h-[150px]", children: step == 1 ? Array.Empty<VNode>() : new[] { SharedBox(200) }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotion_When_ItMovesToAParentAboveAtTheSameLocalBox_Then_TheReplacementTweensFromTheOldScreenPosition()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(OffsetParentsBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near the old element's box, 150px below its own.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement),
                    UnityEngine.Mathf.Abs(replacement.style.translate.value.y.value - 150f) < 50f,
                    replacement.style.scale.keyword),
                Is.EqualTo((false, true, StyleKeyword.Null)));
        }

        // A pooled Label-typed layoutId Motion moves twice (steps 1 and 2) and is removed (step 3) before any
        // layout settles either move; step 4 rents a Label for an unrelated Motion that carries no layoutId.
        [Component]
        private static VNode PooledMotionReuseRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: step switch
            {
                0 => new[] { SharedBox(0, typeof(Label)) },
                1 => new[] { SharedBox(200, typeof(Label)) },
                2 => new[] { SharedBox(250, typeof(Label)) },
                3 => Array.Empty<VNode>(),
                _ => new VNode[]
                {
                    V.Motion(name: "reused", elementType: typeof(Label), transition: s_layoutSpring,
                        className: "left-[300px] top-[0px] w-[50px] h-[50px]"),
                },
            });
        }

        [Test]
        public void Given_ALayoutIdMotionRemovedBeforeEitherOfTwoMovesSettled_When_ThePoolHandsItsElementToAnotherMotion_Then_TheOtherMotionCarriesNoInversePose()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(PooledMotionReuseRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            s_setStep.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setStep.Invoke(4);
            mounted.FlushStateForTest();
            Tick();

            // Assert
            var reused = Root.Q<VisualElement>("reused");
            Assert.That((ReferenceEquals(original, reused), reused.style.translate.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        // Three parents; each step moves the box to the parent before the one holding it, so every
        // replacement is created while the element it replaces is still mounted.
        [Component]
        private static VNode ThreeParentsBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            VNode[] At(int parent, int left) => step == 2 - parent ? new[] { SharedBox(left) } : Array.Empty<VNode>();
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", children: At(0, 400)),
                V.Div(key: "second", children: At(1, 200)),
                V.Div(key: "third", children: At(2, 0)),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionReplacedTwiceBeforeALayout_When_TheLastReplacementSettles_Then_ItTweensFromTheBoxOnScreenBeforeBoth()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(ThreeParentsBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near the first element's box, 400px left of its own, at its own size.
            var last = Root.Q<VisualElement>("shared");
            Assert.That((UnityEngine.Mathf.Abs(last.style.translate.value.x.value + 400f) < 50f, last.style.scale.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        // A layoutId Motion inside another: step 1 moves the outer one 200px right, and the inner one 20px
        // right inside it.
        [Component]
        private static VNode NestedLayoutIdRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "outer",
                    layoutId: "outer-box",
                    transition: s_layoutSpring,
                    className: $"left-[{step * 200}px] top-[0px] w-[300px] h-[300px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            className: $"left-[{20 + step * 20}px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        // GREEN_ON_BASE(characterization): the base tweens a nested layoutId Motion by its own move inside its parent.
        [Test]
        public void Given_ALayoutIdMotionInsideAnother_When_BothMove_Then_TheInnerTweensOnlyItsOwnMoveInsideTheOuter()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(NestedLayoutIdRender, key: "root"));
            Tick();
            var outer = Root.Q<VisualElement>("outer");
            var inner = Root.Q<VisualElement>("inner");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the outer one tweens from its old place and carries the inner one, which tweens only
            // the 20px it moved inside the outer one.
            Assert.That((outer.style.translate.value.x.value < -150f,
                    UnityEngine.Mathf.Abs(inner.style.translate.value.x.value + 20f) < 10f),
                Is.EqualTo((true, true)));
        }

        // The host's padding changes outside any render: the box moves without a patch.
        [Component]
        private static VNode PaddedHostBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(name: "host", children: new VNode[] { SharedBox(step * 200) });
        }

        // GREEN_ON_BASE(characterization): the base plays a layoutId tween once per patch that moved the box.
        // A layout change no render made must not replay the wait a settled tween already consumed.
        [Test]
        public void Given_ALayoutIdTweenThatSettled_When_ALayoutChangeNoRenderMadeMovesTheBox_Then_NoTweenPlays()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(PaddedHostBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            AdvancePast(2f);
            var element = Root.Q<VisualElement>("shared");

            // Act
            Root.Q<VisualElement>("host").style.paddingLeft = 30f;
            Tick();

            // Assert — the box did move, and carries no inline translate.
            Assert.That((element.layout.x, element.style.translate.keyword), Is.EqualTo((230f, StyleKeyword.Null)));
        }

        private static readonly string[] s_rowIds =
        {
            "row-0", "row-1", "row-2", "row-3", "row-4", "row-5", "row-6", "row-7", "row-8", "row-9", "row-10", "row-11",
        };

        // Each row is a layoutId Motion under its own id; a scroll renders the range outside any render.
        private static VirtualListNode LayoutIdRows() => V.VirtualList(
            items: s_rowIds,
            keySelector: id => id,
            itemHeight: 50f,
            renderer: id => V.Motion(key: id, name: id, layoutId: id, transition: s_layoutSpring, className: "w-[100px] h-[50px]"),
            overscan: 0);

        // GREEN_ON_BASE(characterization): the base forgets an id whose element a scroll took out of range.
        // A teardown outside every pass must not leave a box a later render claims.
        [Test]
        public void Given_ALayoutIdRowAScrollTookOutOfRange_When_ALaterRenderMountsAnotherUnderItsId_Then_ItCarriesNoInversePose()
        {
            // Arrange — the rows re-render once after layout, so row-0 holds its own box when the scroll takes it.
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            Root.Add(scrollView);
            using var controller = new FiberVirtualListController(scrollView, LayoutIdRows(), _reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);
            Tick();
            controller.Update(LayoutIdRows());
            Tick();
            var row0 = Root.Q<VisualElement>("row-0");
            controller.UpdateVisibleRange(scrollY: 500f, viewportHeight: 100f);
            var host = new VisualElement();
            Root.Add(host);

            // Act
            _reconciler.Reconcile(host, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(name: "remount", layoutId: "row-0", transition: s_layoutSpring,
                    className: "left-[300px] top-[0px] w-[100px] h-[50px]"),
            });
            Tick();

            // Assert
            var remounted = host.Q<VisualElement>("remount");
            Assert.That((row0.panel, remounted.style.translate.keyword), Is.EqualTo(((IPanel)null, StyleKeyword.Null)));
        }

        // Step 1 flips the outer Motion's element type and moves it 200px right, and moves the inner one 20px
        // right inside it: both are recreated in one render, the inner one under a new parent.
        [Component]
        private static VNode NestedInTypeFlippedOuterRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "outer",
                    layoutId: "outer-box",
                    elementType: step == 0 ? null : typeof(Box),
                    transition: s_layoutSpring,
                    className: $"left-[{step * 200}px] top-[0px] w-[300px] h-[300px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            className: $"left-[{20 + step * 20}px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionInsideATypeFlippedOne_When_BothMove_Then_TheInnerTweensOnlyItsOwnMoveInsideTheOuter()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(NestedInTypeFlippedOuterRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the outer one tweens from 200px left and carries the inner one, which tweens only the
            // 20px it moved inside the outer one.
            var outer = Root.Q<VisualElement>("outer");
            var inner = Root.Q<VisualElement>("inner");
            Assert.That((UnityEngine.Mathf.Abs(outer.style.translate.value.x.value + 200f) < 50f,
                    UnityEngine.Mathf.Abs(inner.style.translate.value.x.value + 20f) < 10f),
                Is.EqualTo((true, true)));
        }

        private readonly record struct TabState(int Index);

        private sealed class TabStore : Store<TabState>
        {
            public TabStore(int initial) : base(new TabState(initial)) { }
            public void Select(int index) => SetState(_ => new TabState(index));
            protected override void ResetCore() => SetState(_ => new TabState(0));
        }

        private static TabStore s_tabs;

        // Two tab components 200px apart, each reading the selected index from one store; the selected one
        // renders the underline. One store update re-renders both.
        private static VNode TabBody(int index, int selected) => V.Div(
            className: $"left-[{index * 200}px] top-[0px] w-[100px] h-[50px]",
            children: selected == index
                ? new VNode[] { V.Motion(name: "underline", layoutId: "underline", transition: s_layoutSpring, className: "w-[100px] h-[10px]") }
                : Array.Empty<VNode>());

        [Component]
        private static VNode FirstTabRender() => TabBody(0, Hooks.UseStore(s_tabs, s => s.Index));

        [Component]
        private static VNode SecondTabRender() => TabBody(1, Hooks.UseStore(s_tabs, s => s.Index));

        [Component]
        private static VNode TabsRender() => V.Div(children: new VNode[]
        {
            V.Component(FirstTabRender, key: "first"),
            V.Component(SecondTabRender, key: "second"),
        });

        private float UnderlineTranslateAfterSelecting(int from, int to)
        {
            s_tabs = new TabStore(from);
            using var mounted = V.Mount(Root, V.Component(TabsRender, key: "root"));
            Tick();
            Tick();
            s_tabs.Select(to);
            Tick();
            Tick();
            var underline = Root.Q<VisualElement>("underline");
            return underline.style.scale.keyword == StyleKeyword.Null ? underline.style.translate.value.x.value : float.NaN;
        }

        [Test]
        public void Given_TwoTabComponentsOnOneStore_When_TheSecondIsSelected_Then_TheUnderlineTweensFromTheFirst()
        {
            // Arrange / Act
            var translate = UnderlineTranslateAfterSelecting(0, 1);

            // Assert — pinned near the first tab, 200px left of the second.
            Assert.That(UnityEngine.Mathf.Abs(translate + 200f), Is.LessThan(50f));
        }

        [Test]
        public void Given_TwoTabComponentsOnOneStore_When_TheFirstIsSelected_Then_TheUnderlineTweensFromTheSecond()
        {
            // Arrange / Act
            var translate = UnderlineTranslateAfterSelecting(1, 0);

            // Assert — pinned near the second tab, 200px right of the first.
            Assert.That(UnityEngine.Mathf.Abs(translate - 200f), Is.LessThan(50f));
        }

        // GREEN_ON_BASE(characterization): the base keeps no box for an id whose element left in an earlier render.
        // A box a batch drain leaves must expire at that drain's end rather than wait for a pass outside one.
        [Test]
        public void Given_TwoTabComponentsOnOneStore_When_NoneIsSelectedAndThenTheSecond_Then_TheUnderlineAppearsInPlace()
        {
            // Arrange
            s_tabs = new TabStore(0);
            using var mounted = V.Mount(Root, V.Component(TabsRender, key: "root"));
            Tick();
            Tick();
            s_tabs.Select(-1);
            Tick();
            Tick();
            var removed = Root.Q<VisualElement>("underline");

            // Act
            s_tabs.Select(1);
            Tick();
            Tick();

            // Assert
            var underline = Root.Q<VisualElement>("underline");
            Assert.That((removed, underline?.style.translate.keyword),
                Is.EqualTo(((VisualElement)null, (StyleKeyword?)StyleKeyword.Null)));
        }

        // Step 1 moves the box out of a parent drawn at half scale into an unscaled one above it.
        [Component]
        private static VNode ScaledParentBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", className: "w-[400px] h-[200px]", children: step == 1 ? new[] { SharedBox(200) } : Array.Empty<VNode>()),
                V.Div(key: "second", className: "w-[400px] h-[200px] scale-[0.5]",
                    children: step == 1 ? Array.Empty<VNode>() : new[] { SharedBox(200) }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionInAHalfScaleParent_When_ItMovesToAnUnscaledOne_Then_ItTweensFromHalfItsSize()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(ScaledParentBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — a new element, starting at the half size the old one was drawn at.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement),
                    UnityEngine.Mathf.Abs(replacement.style.scale.value.value.x - 0.5f) < 0.1f),
                Is.EqualTo((false, true)));
        }

        // Step 1 moves the outer layoutId Motion 200px right; step 2 moves only the inner one, 20px right
        // inside it.
        [Component]
        private static VNode OuterThenInnerMoveRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "outer",
                    layoutId: "outer-box",
                    transition: s_layoutSpring,
                    className: $"left-[{(step >= 1 ? 200 : 0)}px] top-[0px] w-[300px] h-[300px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            className: $"left-[{(step >= 2 ? 40 : 20)}px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        // GREEN_ON_BASE(characterization): the base compares a nested layoutId Motion's rects inside its parent.
        // Comparing them in panel space instead takes in the outer Motion's tween between patch and settle.
        [Test]
        public void Given_AnOuterLayoutIdMotionStillTweening_When_OnlyTheInnerMovesInsideIt_Then_TheInnerTweensOnlyItsOwnMove()
        {
            // Arrange — the outer one's tween is a few frames in.
            using var mounted = V.Mount(Root, V.Component(OuterThenInnerMoveRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 5; i++) Tick();
            var outer = Root.Q<VisualElement>("outer");
            var inner = Root.Q<VisualElement>("inner");

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the outer one is still mid-tween, and the inner one tweens only the 20px it moved.
            Assert.That((outer.style.translate.value.x.value < -50f,
                    UnityEngine.Mathf.Abs(inner.style.translate.value.x.value + 20f) < 5f),
                Is.EqualTo((true, true)));
        }

        private static StateUpdater<int> s_setStop;

        // Three stops along x, transition-transform so the tween takes a transition suspension.
        [Component]
        private static VNode ThreeStopBoxRender()
        {
            var (stop, setStop) = Hooks.UseState(0);
            s_setStop = setStop;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "shared",
                    layoutId: "shared-box",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f },
                    className: $"absolute left-[{stop * 200}px] top-[0px] w-[100px] h-[100px] transition-transform"),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionWhoseTweenSettled_When_ItMovesAgain_Then_TheInverseTranslateAppliesAgain()
        {
            // Arrange — one move, played out to rest.
            using var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();
            AdvancePast(2f);

            // Act — move back to where it started.
            s_setMoved.Invoke(false);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near its old screen position again, 200px to the right of the new one.
            Assert.That(element.style.translate.value.x.value, Is.GreaterThan(50f));
        }

        // GREEN_ON_BASE(characterization): the base already stops a departing element's tween at teardown.
        // The tick's new bookkeeping must go on doing so.
        [Test]
        public void Given_ALayoutIdMotionMidTween_When_ItIsTornDown_Then_ItsTweenStopsWritingToIt()
        {
            // Arrange
            var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();
            mounted.Dispose();
            var atTeardown = element.style.translate.value.x.value;

            // Act
            Tick();
            Tick();

            // Assert — nothing has written a later frame.
            Assert.That(element.style.translate.value.x.value, Is.EqualTo(atTeardown));
        }

        [Test]
        public void Given_ALayoutIdTweenSupersededMidFlight_When_TheLastTweenSettles_Then_TheElementsTransitionsAreHandedBack()
        {
            // Arrange — three moves, each landing while the tween before it is still in flight.
            using var mounted = V.Mount(Root, V.Component(ThreeStopBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            for (var stop = 1; stop <= 3; stop++)
            {
                s_setStop.Invoke(stop);
                mounted.FlushStateForTest();
                Tick();
                Tick();
            }

            // Act
            AdvancePast(3f);

            // Assert — every tween has settled and both inline slots are back with the classes.
            Assert.That((element.style.translate.keyword, element.style.transitionProperty.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        // Builds a parent Motion (a PURE COORDINATOR: it declares no `variants` of its own, only `animate` +
        // `transition` — the orchestration must key off the label it PROPAGATES, not off its own resolved
        // class, since a coordinator like this never gets a MotionAppliedClasses entry) with two inheriting
        // children. child0Animate, when non-null, gives c0 its OWN explicit `animate` (opting it out of
        // inheriting the parent's label, and so out of this stagger — see test (f)).
        private static VNode[] Tree(
            string parentLabel, StyleTransitionConfig parentTransition,
            string child0Animate = null, StyleTransitionConfig childTransition = null)
        {
            childTransition ??= new StyleTransitionConfig { DurationSec = 0.15f };
            return new VNode[]
            {
                V.Motion(key: "p", name: "p", animate: parentLabel, transition: parentTransition,
                    children: new VNode[]
                    {
                        V.Motion(key: "c0", name: "c0", variants: s_fade, animate: child0Animate, transition: childTransition),
                        V.Motion(key: "c1", name: "c1", variants: s_fade, transition: childTransition),
                    }),
            };
        }

        // Whether the element's inline transition-duration is currently set — the runtime-swap play's own
        // tell (see MotionRuntimeSwapTests), used here to confirm a claimed swap actually started/settled.
        private static bool InlineDurationIsSet(VisualElement element)
        {
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0;
        }

        [Test]
        public void Given_AParentLabelFlipWithStaggerChildren_When_TheChildrenInheritTheNewLabel_Then_EachSwapsOnItsOwnIncreasingSlot()
        {
            // Arrange — mount with the parent hidden (orchestration only ever starts from a PATCH-time label
            // change, never on mount — see FiberNodePatcher.PatchMotion), so nothing has swapped yet.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f, StaggerChildrenSec = 0.1f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));
            Assume.That(Root.Q<VisualElement>("c1").ClassListContains("opacity-100"), Is.False,
                "Precondition: no orchestrated swap has fired on mount");

            // Act — flip the parent's label (the render that actually triggers orchestration). Sample at
            // 256ms (past child 0's 200ms slot, short of child 1's 300ms one), then again once both have
            // elapsed.
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));
            for (var i = 0; i < 16; i++) Tick();
            var c0SwappedAtMidpoint = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");
            var c1SwappedAtMidpoint = Root.Q<VisualElement>("c1").ClassListContains("opacity-100");
            AdvancePast(0.3f);
            var c1SwappedLate = Root.Q<VisualElement>("c1").ClassListContains("opacity-100");

            // Assert — child 0 claims index 0 (200ms = delayChildren + 0*stagger) and has already swapped
            // by 256ms; child 1 claims index 1 (300ms = delayChildren + 1*stagger) and has not yet, only
            // swapping once its own later slot elapses.
            Assert.That((c0SwappedAtMidpoint, c1SwappedAtMidpoint, c1SwappedLate), Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_DelayChildrenSecWithNoStagger_When_TheParentLabelFlips_Then_BothChildrenSwapAtTheSameFixedSlot()
        {
            // Arrange — isolate delayChildren's own contribution (StaggerChildrenSec = 0, so the per-index
            // term vanishes and every inheriting child should swap at exactly the same fixed slot).
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.5f, StaggerChildrenSec = 0f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));

            // Act — flip the parent's label. Sample shortly before the shared 500ms slot (neither should
            // have swapped) and again once it has elapsed.
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));
            for (var i = 0; i < 28; i++) Tick();
            var beforeSlot = (Root.Q<VisualElement>("c0").ClassListContains("opacity-100"),
                Root.Q<VisualElement>("c1").ClassListContains("opacity-100"));
            AdvancePast(0.2f);
            var afterSlot = (Root.Q<VisualElement>("c0").ClassListContains("opacity-100"),
                Root.Q<VisualElement>("c1").ClassListContains("opacity-100"));

            // Assert — both children are still un-swapped right up to the shared 500ms slot, then both
            // have swapped once it elapses: the same fixed delay regardless of stagger index.
            Assert.That((beforeSlot, afterSlot), Is.EqualTo(((false, false), (true, true))));
        }

        [Test]
        public void Given_WhenIsBeforeChildren_When_TheParentLabelFlips_Then_TheChildDoesNotSwapUntilTheParentsOwnDurationElapses()
        {
            // Arrange — no delayChildren/staggerChildren, isolating BeforeChildren's own contribution:
            // children wait for the parent's own 400ms swap to finish before starting theirs.
            var transition = new StyleTransitionConfig { DurationSec = 0.4f, When = TransitionWhen.BeforeChildren };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));

            // Act — flip the parent's label. Sample shortly before the 400ms slot and again once it elapses.
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));
            for (var i = 0; i < 23; i++) Tick();
            var beforeSlot = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");
            AdvancePast(0.2f);
            var afterSlot = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");

            // Assert — the inheriting child does not swap until exactly the parent's own DurationSec has
            // elapsed.
            Assert.That((beforeSlot, afterSlot), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_BeforeChildrenWithAParentDelay_When_TheLabelFlips_Then_TheChildWaitsForTheDelayAndTheDuration()
        {
            // Arrange — the parent's own swap spans [DelaySec, DelaySec + DurationSec]; BeforeChildren
            // means children start after it ENDS, so the parent's DelaySec must be part of the wait.
            var transition = new StyleTransitionConfig
            {
                DurationSec = 0.4f,
                DelaySec = 0.2f,
                When = TransitionWhen.BeforeChildren,
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));

            // Act — flip the parent's label. Sample shortly before the 600ms slot (DelaySec + DurationSec)
            // and again once it elapses.
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));
            for (var i = 0; i < 35; i++) Tick();
            var beforeSlot = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");
            AdvancePast(0.2f);
            var afterSlot = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");

            // Assert — 600ms = the parent's DelaySec (200) plus its DurationSec (400); the child does not
            // swap until both have elapsed.
            Assert.That((beforeSlot, afterSlot), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnInheritingOrchestratorWithItsOwnChildStagger_When_TheAncestorLabelFlips_Then_TheGrandchildWaitsForBothDelays()
        {
            // Arrange — "mid" both CLAIMS a delay from "gp"'s orchestration (it inherits gp's label and
            // declares its own Variants, so it actually claims a stagger slot) and ESTABLISHES a fresh
            // orchestration frame for "gc" (its own Transition declares DelayChildrenSec). "gc"'s total delay
            // must be measured from render-commit time, not from when "mid"'s own already-delayed swap starts,
            // or the grandchild would start animating before its own parent's swap even begins.
            var midVariants = new Dictionary<string, MotionVariant> { ["hidden"] = "translate-x-0", ["visible"] = "translate-x-4" };
            VNode[] NestedTree(string label) => new VNode[]
            {
                V.Motion(key: "gp", name: "gp", animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.1f, DelayChildrenSec = 0.5f },
                    children: new VNode[]
                    {
                        V.Motion(key: "mid", name: "mid", variants: midVariants,
                            transition: new StyleTransitionConfig { DurationSec = 0.1f, DelayChildrenSec = 0.25f },
                            children: new VNode[]
                            {
                                V.Motion(key: "gc", name: "gc", variants: s_fade,
                                    transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                            }),
                    }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), NestedTree("hidden"));
            Assume.That(Root.Q<VisualElement>("gc").ClassListContains("opacity-100"), Is.False,
                "Precondition: no orchestrated swap has fired on mount");

            // Act — flip the top ancestor's label. Sample shortly before the 750ms slot and again once it
            // elapses.
            _reconciler.Reconcile(Root, NestedTree("hidden"), NestedTree("visible"));
            for (var i = 0; i < 45; i++) Tick();
            var beforeSlot = Root.Q<VisualElement>("gc").ClassListContains("opacity-100");
            AdvancePast(0.2f);
            var afterSlot = Root.Q<VisualElement>("gc").ClassListContains("opacity-100");

            // Assert — 750ms = gp's delayChildren (500ms, claimed by "mid") + mid's OWN delayChildren
            // (250ms), folded together rather than measuring mid's fresh frame from zero; the grandchild
            // does not swap until both have elapsed.
            Assert.That((beforeSlot, afterSlot), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnInheritingLayerWithNoOrchestrationOfItsOwn_When_TheAncestorLabelFlips_Then_TheGrandchildKeepsTheAncestorsSequence()
        {
            // Arrange — the case above gives "mid" its own DelayChildrenSec, so mid establishes a frame
            // whatever the gate decides. Here mid declares no orchestration knob at all: it has to pass gp's
            // frame through, leaving mid and gc as slots 0 and 1 of ONE sequence rather than restarting it.
            VNode[] NestedTree(string label) => new VNode[]
            {
                V.Motion(key: "gp", name: "gp", animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.4f },
                    children: new VNode[]
                    {
                        V.Motion(key: "mid", name: "mid", variants: s_fade,
                            transition: new StyleTransitionConfig { DurationSec = 0.05f },
                            children: new VNode[]
                            {
                                V.Motion(key: "gc", name: "gc", variants: s_fade,
                                    transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                            }),
                    }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), NestedTree("hidden"));

            // Act — flip the ancestor, then sample past mid's slot 0 and inside gc's 0.4s slot.
            _reconciler.Reconcile(Root, NestedTree("hidden"), NestedTree("visible"));
            for (var i = 0; i < 12; i++) Tick();

            // Assert — mid took slot 0 and gc is still parked on slot 1. A layer that established a frame of
            // its own would hand gc index 0 of an empty sequence and both would swap at once; the mid term is
            // what separates that from nothing having been orchestrated at all.
            Assert.That(
                (Root.Q<VisualElement>("mid").ClassListContains("opacity-100"),
                 Root.Q<VisualElement>("gc").ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASwapDeclaringAfterChildren_When_ItPropagatesALabelToItsChildren_Then_TheUnorchestratedWaitIsDiagnosed()
        {
            // Arrange — AfterChildren is accepted by the config but not yet orchestrated for label
            // propagation, so the swap that would have to wait says so instead of quietly animating early.
            // StaggerChildrenSec is set too, which opens the orchestration gate on its own: that leaves this
            // case measuring which When value is diagnosed rather than whether the gate above it opened.
            var diagnosed = 0;
            void OnLog(string condition, string stackTrace, UnityEngine.LogType type)
            {
                if (type == UnityEngine.LogType.Warning && condition.Contains("When = AfterChildren"))
                {
                    diagnosed++;
                }
            }

            VNode[] Tree(string label) => new VNode[]
            {
                V.Motion(key: "p", animate: label,
                    transition: new StyleTransitionConfig
                    {
                        DurationSec = DurationSec,
                        StaggerChildrenSec = 0.4f,
                        When = TransitionWhen.AfterChildren,
                    },
                    children: new VNode[]
                    {
                        V.Motion(key: "c", variants: s_fade,
                            transition: new StyleTransitionConfig { DurationSec = DurationSec }),
                    }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden"));

            // Act
            UnityEngine.Application.logMessageReceived += OnLog;
            try
            {
                _reconciler.Reconcile(Root, Tree("hidden"), Tree("visible"));
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= OnLog;
            }

            // Assert — said once, for the one parent whose propagated label changed.
            Assert.That(diagnosed, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildWithItsOwnExplicitAnimate_When_TheParentLabelFlips_Then_ItNeverPlaysButItsSiblingIsDelayed()
        {
            // Arrange — c0 declares its OWN explicit animate ("visible", fixed across both trees below),
            // opting it out of inheriting the parent's ambient label and so out of this stagger; c1 declares no
            // own animate and inherits normally, so it MUST still be delayed regardless of which stagger index
            // it claims (c0 never calls into the shared counter at all, since it never satisfies the ambient-
            // following gate) — DelayChildrenSec alone (no stagger) makes that unambiguous.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition, child0Animate: "visible"));

            // Act — flip the parent's label. c1's own resolved variant changes (hidden -> visible) and must
            // wait for its claimed 200ms slot; c0's never changes (fixed at "visible" throughout), so no
            // runtime-swap play is ever triggered for it at all.
            _reconciler.Reconcile(Root, Tree("hidden", transition, child0Animate: "visible"),
                Tree("visible", transition, child0Animate: "visible"));
            var c1SwappedImmediately = Root.Q<VisualElement>("c1").ClassListContains("opacity-100");
            AdvancePast(0.2f);
            var c0NeverPlayed = !InlineDurationIsSet(Root.Q<VisualElement>("c0"));
            var c1SwappedAfterItsSlot = Root.Q<VisualElement>("c1").ClassListContains("opacity-100");

            // Assert — c0 (own explicit animate) never got a runtime-swap play at all; c1 (ambient-inheriting)
            // did not swap immediately and only reached its target once its claimed delay elapsed.
            Assert.That((c0NeverPlayed, c1SwappedImmediately, c1SwappedAfterItsSlot), Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_AnOrchestratedSwapOnAPanel_When_ItsTransitionWouldHaveFinished_Then_TheInlineTransitionStylesClearAutomatically()
        {
            // Arrange — the same parent-flip scenario as the tests above; c0's runtime-swap play is claimed
            // behind a non-zero orchestrated delay.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f, StaggerChildrenSec = 0.1f };
            var childTransition = new StyleTransitionConfig { DurationSec = 0.1f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition, childTransition: childTransition));
            _reconciler.Reconcile(Root, Tree("hidden", transition, childTransition: childTransition),
                Tree("visible", transition, childTransition: childTransition));
            var c0 = Root.Q<VisualElement>("c0");
            Assume.That(InlineDurationIsSet(c0), Is.True, "Precondition: the runtime-swap play set its inline transition");

            // Act — advance the simulated clock well past this child's claimed delay (200ms) + its own swap
            // duration (100ms).
            AdvancePast(0.2f + 0.1f);

            // Assert — the completion cleanup fired and released the inline transition styles.
            Assert.That(InlineDurationIsSet(c0), Is.False);
        }

        [Test]
        public void Given_AStandaloneMotionWithInitial_When_Mounted_Then_ItStartsAtTheInitialVariant()
        {
            // Arrange / Act — no AnimatePresence anywhere: initial/animate must still drive the mount enter.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = DurationSec }),
            });

            // Assert — starts at variants[initial]=opacity-0; variants[animate]=opacity-100 is stripped during
            // the from-frame (swapped back in, and kept on completion — see the next two tests).
            var element = Root.Q<VisualElement>("m");
            Assert.That((element.ClassListContains("opacity-0"), element.ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AStandaloneMotionWithoutInitial_When_Mounted_Then_ItStartsAtTheAnimateVariantWithNoTweenScheduled()
        {
            // Arrange / Act — no `initial` declared, so there is no starting pose to enter FROM.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(name: "m", variants: s_fade, animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = DurationSec }),
            });

            // Assert — rests directly at variants[animate], and (unlike the `initial` case above) no transition
            // was scheduled: no inline transition-duration was ever applied.
            var element = Root.Q<VisualElement>("m");
            Assert.That(
                (element.ClassListContains("opacity-100"), element.style.transitionDuration.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Component]
        private static VNode StandaloneHostRender()
        {
            var (_, bump) = Hooks.UseState(0);
            s_bump = bump;
            return V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible",
                transition: new StyleTransitionConfig { DurationSec = DurationSec });
        }

        [Test]
        public void Given_AStandaloneMotionThatFinishedEntering_When_AnUnrelatedStateChangeReRenders_Then_ItKeepsTheAnimateVariant()
        {
            // Arrange — mount under a component (so a self-contained state update can re-render it), and let
            // the enter complete: it rests at variants[animate], persistently.
            using var mounted = V.Mount(Root, V.Component(StandaloneHostRender, key: "host"));
            AdvancePast(DurationSec);
            var element = Root.Q<VisualElement>("m");
            Assume.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0")),
                Is.EqualTo((true, false)),
                "Precondition: the enter completed and rests at variants[animate]");

            // Act — an UNRELATED state change re-renders the same Motion node through PatchMotion (not
            // CreateElement again), which resolves the applied classes from Animate/ambient only.
            s_bump.Invoke(1);
            Tick();

            // Assert — the patch never replays `initial`: the element keeps resting at variants[animate].
            Assert.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0")),
                Is.EqualTo((true, false)));
        }

        // The dangerous shape is production's own: the mount runs inside the panel's timer tick (the
        // batch scheduler's drain is itself a scheduled item), the enter's swap is registered on a
        // freshly attached element, and a zero-delay swap item then becomes runnable in the very tick
        // that mounted the element.
        private VisualElement StartEnterInsideATimerTick(StyleAnimationScheduler scheduler)
        {
            var element = new VisualElement();
            Root.Add(element);
            element.AddToClassList("opacity-100");
            Tick();
            element.schedule.Execute(() =>
            {
                scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                    durationSec: 0.3f, easing: EasingMode.EaseInOut, delaySec: 0f);
            });
            return element;
        }

        [Test]
        public void Given_AVariantEnterStartedInsideATimerTick_When_ThatTickEnds_Then_TheFromStateIsStillApplied()
        {
            // Arrange — mirror production: the enter's step 1 (strip to-classes, apply from-classes,
            // schedule the swap) runs inside the panel's own timer tick.
            var scheduler = new StyleAnimationScheduler();
            var element = StartEnterInsideATimerTick(scheduler);

            // Act — the single tick that both starts the enter and drains the timer queue.
            Tick();

            // Assert — the from-state must survive the tick that started the enter; a swap that ran
            // in the same tick strips it before the panel computes it once, so the transition sees
            // no change and the enter degenerates to an instant jump.
            Assert.That(element.ClassListContains("opacity-0"), Is.True);
        }

        [Test]
        public void Given_AVariantEnterStartedInsideATimerTick_When_TheNextTickRuns_Then_TheSwapReachesTheAnimateState()
        {
            // Arrange — same production shape as above.
            var scheduler = new StyleAnimationScheduler();
            var element = StartEnterInsideATimerTick(scheduler);
            Tick();
            Assume.That(element.ClassListContains("opacity-0"), Is.True,
                "Precondition: the from-state survived the starting tick");

            // Act — the next tick is where the deferred swap belongs.
            Tick();

            // Assert — the swap did fire on the following tick (the enter must still make progress,
            // not park the from-state forever).
            Assert.That(element.ClassListContains("opacity-0"), Is.False);
        }

        [Component]
        private static VNode LateMountHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "late-" + key, key: key.ToString(), variants: s_fade,
                    initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "host", children: children.ToArray());
        }

        [Test]
        public void Given_AMotionMountedByATimerTickDrain_When_ThatTickEnds_Then_TheFromStateIsStillApplied()
        {
            // Arrange — mount the host and settle, then dirty the store WITHOUT a manual drain, so
            // the new Motion's whole mount (create detached -> play enter -> attach) happens inside
            // the panel's own timer tick via the batch scheduler's scheduled drain, exactly like
            // production. The enter's zero-delay swap item is attached mid-tick with its deadline
            // already reached.
            using var store = new SetStore("");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(LateMountHost, key: "root"));
            Tick();
            store.Set("a");

            // Act — the single tick that both drains the batch (mounting the Motion) and the timer
            // queue (where the just-scheduled swap must NOT yet run).
            Tick();

            // Assert — the from-state survived its mounting tick; swapping in the same tick would
            // strip it before its first style pass and the enter would play as an instant jump.
            Assert.That(Root.Q<VisualElement>("late-a").ClassListContains("opacity-0"), Is.True);
        }
    }
}
