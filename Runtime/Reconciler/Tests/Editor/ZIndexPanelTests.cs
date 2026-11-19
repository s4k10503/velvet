using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>z-*</c> stacking feature's behavior on a real (headless) editor panel: the parts that
    /// need resolved layout or the engine's own focus ring — coordinate neutrality (relocating a child within
    /// its parent must not move it), focus order (a z-relocated element's placeholder is a real, displayed,
    /// zero-size proxy tab stop, so Tab order follows the DECLARED position, not wherever the band physically
    /// paints), and event bubbling (a relocated element stays a child of its own parent). GWT, one assert per
    /// case.
    /// </summary>
    [TestFixture]
    internal sealed class ZIndexPanelTests
    {
        private sealed class ToggleStore<T> : Store<T>
        {
            private readonly T _initial;
            public ToggleStore(T initial) : base(initial) => _initial = initial;
            public void Set(T value) => SetState(_ => value);
            protected override void ResetCore() => SetState(_ => _initial);
        }

        private static ToggleStore<bool> s_coordinateStore;
        private static ToggleStore<bool> s_resortZStore;
        private static ToggleStore<bool> s_resortZContainerStore;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_coordinateStore = null;
            s_resortZStore = null;
            s_resortZContainerStore = null;
            s_firstClass = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        // Focus-ring membership (and resolvedStyle / worldBound) requires a resolved style pass, which
        // batchmode never runs on its own. The bundled utility sheet must be attached explicitly: the
        // z-* scope gate recognizes the "absolute" CLASS token at reconcile time, but the element only
        // actually leaves flow when the `.absolute { position: absolute; }` USS rule resolves — without
        // the sheet this fixture would relocate elements that never really were absolute.
        private void Mount(System.Func<VNode> body)
        {
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private static void SendMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using var move = NavigationMoveEvent.GetPooled(direction);
            move.target = target;
            target.SendEvent(move);
        }

        #region Coordinate neutrality

        [Component]
        private static VNode CoordinateHost()
        {
            var withZ = Hooks.UseStore(s_coordinateStore, x => x);
            var cls = "absolute left-[24px] top-[16px] w-[10px] h-[10px]" + (withZ ? " z-10" : "");
            return V.Div(name: "parent", className: "relative w-[200px] h-[200px]", children: new VNode[]
            {
                V.Div(name: "sibling", className: "w-[50px] h-[50px]"),
                V.Div(name: "child", className: cls),
            });
        }

        [Test]
        public void Given_AnAbsoluteChildWithAFixedOffset_When_ItGainsAZClass_Then_ItsResolvedWorldPositionIsUnchanged()
        {
            // Arrange
            using var store = new ToggleStore<bool>(false);
            s_coordinateStore = store;
            Mount(CoordinateHost);
            Assume.That(_host.Root.Q<VisualElement>("child")?.resolvedStyle.position,
                Is.EqualTo(Position.Absolute),
                "Precondition: the child mounted as a genuinely out-of-flow absolute sibling");
            var before = _host.Root.Q<VisualElement>("child").worldBound;

            // Act — relocates the same element into its parent's front band.
            store.Set(true);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var child = _host.Root.Q<VisualElement>("child");

            // Assert — the child stays a child of the same parent, so its left/top resolve to the identical world
            // rect. The relocation is a term: a child left in its slot keeps its rect as well.
            Assert.That((ZBandProbe.InBand(child, front: true), child.worldBound), Is.EqualTo((true, before)));
        }

        #endregion

        #region Layout of a relocated in-flow element

        private static string s_firstClass;

        [Component]
        private static VNode InFlowHost() => V.Div(name: "parent", className: "flex flex-row w-[100px] h-[20px]",
            children: new VNode[]
            {
                V.Div(name: "first", className: s_firstClass + " w-[20px] h-[10px]"),
                V.Div(name: "second", className: "w-[30px] h-[10px]"),
            });

        [TestCase("z-10")]
        [TestCase("relative")]
        public void Given_AnInFlowChildPaintingAfterItsLaterSibling_When_LaidOut_Then_ItKeepsItsDeclaredPlaceInTheRow(
            string paintClass)
        {
            // Arrange
            s_firstClass = paintClass;

            // Act
            Mount(InFlowHost);

            // Assert — it paints after "second" and still lays out ahead of it.
            Assert.That(
                (ZBandProbe.PaintOrder(_host.Root.Q<VisualElement>("parent")),
                    _host.Root.Q<VisualElement>("first").layout.x, _host.Root.Q<VisualElement>("second").layout.x),
                Is.EqualTo(("second,first", 0f, 20f)));
        }

        [Component]
        private static VNode OrderHost() => V.Div(name: "parent", className: "flex flex-row w-[100px] h-[20px]",
            children: new VNode[]
            {
                V.Div(name: "a", className: "order-2 w-[10px] h-[10px]"),
                V.Div(name: "b", className: "w-[10px] h-[10px]"),
                V.Div(name: "c", className: "order-1 w-[10px] h-[10px]"),
            });

        [Test]
        public void Given_FlexOrderClasses_When_LaidOut_Then_TheRowFollowsFlexOrder()
        {
            // Arrange / Act
            Mount(OrderHost);

            // Assert
            Assert.That(
                (_host.Root.Q<VisualElement>("b").layout.x, _host.Root.Q<VisualElement>("c").layout.x,
                    _host.Root.Q<VisualElement>("a").layout.x),
                Is.EqualTo((0f, 10f, 20f)));
        }

        #endregion

        #region Focus order

        [Component]
        private static VNode FocusOrderHost() => V.Div(name: "parent", className: "relative", children: new VNode[]
        {
            V.Button(name: "before"),
            V.Button(name: "zmanaged", className: "absolute z-10"),
            V.Button(name: "after"),
        });

        [Test]
        public void Given_AZManagedButtonAmongOrdinaryButtons_When_TabDispatchesFromItsDeclaredPredecessor_Then_FocusLandsOnItAtItsLogicalPosition()
        {
            // Arrange
            Mount(FocusOrderHost);
            var before = _host.Root.Q<Button>("before");
            before.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(before),
                "Precondition: the declared predecessor holds focus");

            // Act — the engine's own move lands on the placeholder (physically right after "before"); the
            // FocusIn forwarding hands focus into the real z-managed element.
            SendMove(before, NavigationMoveEvent.Direction.Next);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(_host.Root.Q<Button>("zmanaged")),
                Is.True, "Precondition: the z-managed button actually relocated into its parent's front band");

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("zmanaged")));
        }

        [Test]
        public void Given_FocusInsideAZManagedButton_When_TabDispatchesForward_Then_FocusContinuesToItsDeclaredSuccessorNotWhereverTheLayerPaints()
        {
            // Arrange — the front band is trailing (the button physically paints as the parent's LAST child),
            // so the engine's own ring would otherwise have nothing (or something unrelated) after it.
            Mount(FocusOrderHost);
            var zmanaged = _host.Root.Q<Button>("zmanaged");
            zmanaged.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zmanaged),
                "Precondition: the z-managed button holds focus");

            // Act
            SendMove(zmanaged, NavigationMoveEvent.Direction.Next);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zmanaged),
                Is.True, "Precondition: the z-managed button actually relocated into its parent's front band");

            // Assert — focus continues to "after", the TRUE next sibling at the element's declared position.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("after")));
        }

        [Component]
        private static VNode LiftedFocusHost() => V.Div(name: "ancestor", children: new VNode[]
        {
            V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Button(name: "overlay", className: "absolute"),
                V.Button(name: "content"),
            }),
            V.Button(name: "outerAfter"),
        });

        [Test]
        public void Given_AnOverlayPaintingAfterTheContentItPrecedes_When_TabDispatchesFromTheContent_Then_FocusLeavesTheParentInsteadOfReturningToTheOverlay()
        {
            // Arrange — the overlay is positioned and declared first, so it paints in the parent's front band,
            // physically after "content", where the engine's own ring would go next.
            Mount(LiftedFocusHost);
            var content = _host.Root.Q<Button>("content");
            content.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(content),
                "Precondition: the content button holds focus");

            // Act
            SendMove(content, NavigationMoveEvent.Direction.Next);

            // Assert — the relocation is a term: an overlay left in its slot is already behind "content".
            Assert.That(
                (ZBandProbe.InBand(_host.Root.Q<Button>("overlay"), front: true),
                    _host.Panel.focusController.focusedElement),
                Is.EqualTo((true, (Focusable)_host.Root.Q<Button>("outerAfter"))));
        }

        #endregion

        #region Event bubbling

        [Component]
        private static VNode BubbleHost() => V.Div(name: "ancestor", children: new VNode[]
        {
            V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "ordinary"),
                V.Div(name: "zchild", className: "absolute z-10"),
            }),
        });

        [Test]
        public void Given_AZManagedElement_When_ItReceivesAPointerDownEvent_Then_TheEventBubblesToAnAncestorAboveTheStackingParent()
        {
            // Arrange
            Mount(BubbleHost);
            var received = 0;
            _host.Root.Q<VisualElement>("ancestor").RegisterCallback<PointerDownEvent>(_ => received++);
            Assume.That(ZBandProbe.InBand(_host.Root.Q<VisualElement>("zchild"), front: true),
                Is.True, "Precondition: zchild's real element physically relocated into its parent's front band");

            // Act — dispatched through the real panel: zchild -> its parent ("parent") -> "ancestor".
            _host.Root.Q<VisualElement>("zchild").SendPointerDownEvent(UnityEngine.Vector2.zero);

            // Assert
            Assert.That(received, Is.EqualTo(1));
        }

        #endregion

        #region Focus preservation across a z resort

        [Component]
        private static VNode ResortZHost()
        {
            var bumped = Hooks.UseStore(s_resortZStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Button(name: "zmanaged", className: "absolute " + (bumped ? "z-20" : "z-10")),
            });
        }

        [Test]
        public void Given_FocusInsideAZManagedButton_When_ItsZChanges_Then_ItStillHoldsFocusAfterTheResort()
        {
            // Arrange
            using var store = new ToggleStore<bool>(false);
            s_resortZStore = store;
            Mount(ResortZHost);
            var button = _host.Root.Q<Button>("zmanaged");
            button.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(button),
                "Precondition: the z-managed button holds focus before the resort");

            // Act — bumps z-10 -> z-20 within the same front band.
            store.Set(true);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(button));
        }

        [Component]
        private static VNode ResortZContainerHost()
        {
            var bumped = Hooks.UseStore(s_resortZContainerStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "zdiv", className: "absolute " + (bumped ? "z-20" : "z-10"), children: new VNode[]
                {
                    V.Button(name: "inner"),
                }),
            });
        }

        [Test]
        public void Given_FocusInsideAButtonNestedInAZManagedDiv_When_TheDivsZChanges_Then_TheInnerButtonStillHoldsFocusAfterTheResort()
        {
            // Arrange — the focused element is a DESCENDANT of the z-managed element, not the z-managed
            // element itself, which every other resort test in this file focuses directly.
            using var store = new ToggleStore<bool>(false);
            s_resortZContainerStore = store;
            Mount(ResortZContainerHost);
            var inner = _host.Root.Q<Button>("inner");
            inner.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(inner),
                "Precondition: the inner button holds focus before the resort");

            // Act — bumps z-10 -> z-20, which re-sorts the z-managed div (and its whole subtree, "inner"
            // included) within the same front band.
            store.Set(true);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(inner));
        }

        #endregion

        #region Adjacent z-managed siblings backward focus exit

        [Component]
        private static VNode AdjacentZHost() => V.Div(name: "parent", className: "relative", children: new VNode[]
        {
            V.Button(name: "first", className: "absolute z-10"),
            V.Button(name: "second", className: "absolute z-20"),
        });

        [Test]
        public void Given_TwoAdjacentZManagedButtons_When_FocusOnTheSecondNavigatesPrevious_Then_FocusLandsOnTheFirstsRealButton()
        {
            // Arrange
            Mount(AdjacentZHost);
            var second = _host.Root.Q<Button>("second");
            second.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(second),
                "Precondition: the second z-managed button holds focus");

            // Act — TryZLayerExit's own ring prediction lands on the first's PLACEHOLDER (the panel-ring
            // neighbour of "second"'s own placeholder); OnFocusIn's existing z-layer-placeholder forwarding
            // branch chains a second redirect from there onto the first's real button.
            SendMove(second, NavigationMoveEvent.Direction.Previous);

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("first")));
        }

        #endregion

        #region Focus exit past the stacking parent's own boundary

        // "zlast" is the stacking parent's own LOGICALLY LAST child, so its placeholder's physical
        // ring-neighbour is the trailing front band itself (real's own), not another ordinary sibling the way
        // FocusOrderHost's "after" is.
        [Component]
        private static VNode ZExitForwardHost() => V.Div(name: "ancestor", children: new VNode[]
        {
            V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Button(name: "before"),
                V.Button(name: "zlast", className: "absolute z-10"),
            }),
            V.Button(name: "outerAfter"),
        });

        [Test]
        public void Given_AZManagedButtonIsTheStackingParentsLastLogicalChild_When_TabDispatchesForward_Then_FocusExitsPastTheStackingParentToTheOrdinaryAncestorSuccessor()
        {
            // Arrange
            Mount(ZExitForwardHost);
            var zlast = _host.Root.Q<Button>("zlast");
            zlast.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zlast),
                "Precondition: the z-managed button holds focus");

            // Act — the naive one-step ring walk from "zlast"'s placeholder re-enters the trailing front
            // band (its own physical ring-neighbour), landing back on "zlast" itself.
            SendMove(zlast, NavigationMoveEvent.Direction.Next);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zlast), Is.True,
                "Precondition: the z-managed button actually relocated into its parent's front band");

            // Assert — RED without the fix: the redirect lands back on "zlast" itself (a no-op) instead of
            // escaping past "parent" to "outerAfter", the true next stop under the shared ancestor.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("outerAfter")));
        }

        // "zfirst" is the stacking parent's own LOGICALLY FIRST child and negative-z, so its placeholder's
        // physical ring-neighbour going backward is the LEADING back band itself — the symmetric mirror of the
        // forward/trailing-front-band case above.
        [Component]
        private static VNode ZExitBackwardHost() => V.Div(name: "ancestor", children: new VNode[]
        {
            V.Button(name: "outerBefore"),
            V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Button(name: "zfirst", className: "absolute -z-10"),
                V.Button(name: "after"),
            }),
        });

        [Test]
        public void Given_ANegativeZButtonIsTheStackingParentsFirstLogicalChild_When_TabDispatchesBackward_Then_FocusExitsPastTheStackingParentToTheOrdinaryAncestorPredecessor()
        {
            // Arrange
            Mount(ZExitBackwardHost);
            var zfirst = _host.Root.Q<Button>("zfirst");
            zfirst.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zfirst),
                "Precondition: the z-managed button holds focus");

            // Act — the naive one-step ring walk from "zfirst"'s placeholder re-enters the leading back
            // band (its own physical ring-neighbour going backward), landing back on "zfirst" itself.
            SendMove(zfirst, NavigationMoveEvent.Direction.Previous);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zfirst), Is.True,
                "Precondition: the z-managed button actually relocated into its parent's back band");

            // Assert — RED without the fix: the redirect lands back on "zfirst" itself (a no-op) instead of
            // escaping past "parent" to "outerBefore", the true previous stop under the shared ancestor.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("outerBefore")));
        }

        #endregion

        #region Focus exit past a co-tenant sharing the same band

        // "zb" is "parent"'s own LOGICALLY LAST child, but its LOWER z (10, vs "za"'s 20) sorts it
        // PHYSICALLY FIRST within the shared front band — the declaration/physical-order mismatch that
        // exposes a co-tenant landing: the very next ring step past "zb" itself is "za" (the OTHER
        // z-managed sibling sharing that SAME band), not something genuinely outside it.
        [Component]
        private static VNode ZExitCoTenantForwardHost() => V.Div(name: "ancestor", children: new VNode[]
        {
            V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Button(name: "before"),
                V.Button(name: "za", className: "absolute z-20"),
                V.Button(name: "zb", className: "absolute z-10"),
            }),
            V.Button(name: "outerAfter"),
        });

        [Test]
        public void Given_AZManagedButtonsRingNeighbourIsACoTenantInTheSameBand_When_TabDispatchesForward_Then_FocusStillExitsPastTheStackingParent()
        {
            // Arrange — "za" and "zb" share the front band ("za" sorts after "zb": z-20 > z-10), so "zb"'s very
            // next physical ring step lands on "za", not outside the band.
            Mount(ZExitCoTenantForwardHost);
            var zb = _host.Root.Q<Button>("zb");
            zb.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zb),
                "Precondition: \"zb\" holds focus");

            // Act
            SendMove(zb, NavigationMoveEvent.Direction.Next);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zb), Is.True,
                "Precondition: \"zb\" actually relocated into the shared front band");

            // Assert — RED without the fix: the walk stops the instant it steps past "zb" itself, landing on
            // "za" (a co-tenant sharing the SAME band) instead of continuing until it is genuinely outside every
            // relocated element — past "parent" — onto "outerAfter".
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("outerAfter")));
        }

        #endregion

        #region No valid exit

        [Component]
        private static VNode ZExitNoTargetHost() => V.Div(name: "parent", className: "relative", children: new VNode[]
        {
            V.Button(name: "zonly", className: "absolute z-10"),
        });

        [Test]
        public void Given_AZManagedButtonIsTheOnlyFocusableInThePanel_When_TabDispatchesForward_Then_FocusStaysOnItInstead()
        {
            // Arrange — nothing else exists anywhere in the panel to escape to.
            Mount(ZExitNoTargetHost);
            var zonly = _host.Root.Q<Button>("zonly");
            zonly.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zonly),
                "Precondition: the z-managed (and only) button holds focus");

            // Act — TryZLayerExit's own walk stays inside the relocated button or wraps back onto the
            // placeholder itself every step, so it falls through to "no valid exit" (returns false) instead of
            // redirecting anywhere.
            SendMove(zonly, NavigationMoveEvent.Direction.Next);

            // Assert — graceful no-move: focus stays exactly where it was instead of the walk finding a
            // spurious target or the panel throwing/looping.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zonly));
        }

        #endregion
    }
}
