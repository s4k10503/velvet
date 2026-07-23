using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>z-*</c> stacking feature's behavior on a real (headless) editor panel: the parts that
    /// need resolved layout or the engine's own focus ring — coordinate neutrality (relocating an absolute
    /// child into its layer container must not move it, since the container is coincident with the stacking
    /// parent's own content box), focus order (a z-relocated element's placeholder is a real, displayed,
    /// zero-size proxy tab stop, so Tab order follows the DECLARED position, not wherever the layer container
    /// physically paints), and event bubbling (a relocated element's physical ancestor chain still passes
    /// through its own stacking parent, one hop deeper via the container). GWT, one assert per case.
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
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _host.Root.styleSheets.Add(sheet);
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

            // Act — relocates the same element into the front layer container.
            store.Set(true);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(_host.Root.Q<VisualElement>("child")),
                Is.True, "Precondition: the child actually relocated into a z-layer container");

            // Assert — the layer container is coincident with the stacking parent's own content box, so the
            // child's left/top (parent-relative) resolve to the identical world rect.
            Assert.That(_host.Root.Q<VisualElement>("child").worldBound, Is.EqualTo(before));
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
                Is.True, "Precondition: the z-managed button actually relocated into a layer container");

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("zmanaged")));
        }

        [Test]
        public void Given_FocusInsideAZManagedButton_When_TabDispatchesForward_Then_FocusContinuesToItsDeclaredSuccessorNotWhereverTheLayerPaints()
        {
            // Arrange — the layer container is trailing (the button physically paints as the parent's LAST
            // child), so the engine's own ring would otherwise have nothing (or something unrelated) after it.
            Mount(FocusOrderHost);
            var zmanaged = _host.Root.Q<Button>("zmanaged");
            zmanaged.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zmanaged),
                "Precondition: the z-managed button holds focus");

            // Act
            SendMove(zmanaged, NavigationMoveEvent.Direction.Next);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zmanaged),
                Is.True, "Precondition: the z-managed button actually relocated into a layer container");

            // Assert — focus continues to "after", the TRUE next sibling at the element's declared position.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("after")));
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
            Assume.That(_host.Root.Q<VisualElement>("zchild").parent?.ClassListContains(FiberZLayerCoordinator.FrontMarkerClass),
                Is.True, "Precondition: zchild's real element physically relocated into the front layer container");

            // Act — dispatched through the real panel: zchild -> its layer container -> the stacking parent
            // ("parent") -> "ancestor", one hop deeper than an ordinary sibling but never leaving the panel.
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

            // Act — bumps z-10 -> z-20: Reposition detaches and re-inserts the same real element within the
            // same front container (InsertSorted's own unconditional RemoveFromHierarchy).
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
            // element itself (every other resort test in this file focuses the z-managed element directly,
            // which only pins CaptureFocusForRescue's ReferenceEquals(focused, moving) branch, never its
            // moving.Contains(focused) branch).
            using var store = new ToggleStore<bool>(false);
            s_resortZContainerStore = store;
            Mount(ResortZContainerHost);
            var inner = _host.Root.Q<Button>("inner");
            inner.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(inner),
                "Precondition: the inner button holds focus before the resort");

            // Act — bumps z-10 -> z-20: Reposition detaches and re-inserts the z-managed div (and its whole
            // subtree, "inner" included) within the same front container.
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
        // ring-neighbour is the trailing front container itself (real's own layer), not another ordinary
        // sibling the way FocusOrderHost's "after" is.
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
            // container (its own physical ring-neighbour), landing back on "zlast" itself.
            SendMove(zlast, NavigationMoveEvent.Direction.Next);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zlast), Is.True,
                "Precondition: the z-managed button actually relocated into the front layer container");

            // Assert — RED without the fix: the redirect lands back on "zlast" itself (a no-op) instead of
            // escaping past "parent" to "outerAfter", the true next stop under the shared ancestor.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("outerAfter")));
        }

        // "zfirst" is the stacking parent's own LOGICALLY FIRST child and negative-z, so its placeholder's
        // physical ring-neighbour going backward is the LEADING back container itself — the symmetric
        // mirror of the forward/trailing-front-container case above.
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
            // container (its own physical ring-neighbour going backward), landing back on "zfirst" itself.
            SendMove(zfirst, NavigationMoveEvent.Direction.Previous);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zfirst), Is.True,
                "Precondition: the z-managed button actually relocated into the back layer container");

            // Assert — RED without the fix: the redirect lands back on "zfirst" itself (a no-op) instead of
            // escaping past "parent" to "outerBefore", the true previous stop under the shared ancestor.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(_host.Root.Q<Button>("outerBefore")));
        }

        #endregion

        #region Focus exit past a co-tenant sharing the same layer container

        // "zb" is "parent"'s own LOGICALLY LAST child, but its LOWER z (10, vs "za"'s 20) sorts it
        // PHYSICALLY FIRST within the shared front container — the declaration/physical-order mismatch that
        // exposes a co-tenant landing: the very next ring step past "zb" itself is "za" (the OTHER
        // z-managed sibling sharing that SAME container), not something genuinely outside it.
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
        public void Given_AZManagedButtonsRingNeighbourIsACoTenantInTheSameContainer_When_TabDispatchesForward_Then_FocusStillExitsPastTheStackingParent()
        {
            // Arrange — "za" and "zb" share the front container ("za" sorts after "zb": z-20 > z-10), so
            // "zb"'s very next physical ring step lands on "za", not outside the container.
            Mount(ZExitCoTenantForwardHost);
            var zb = _host.Root.Q<Button>("zb");
            zb.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zb),
                "Precondition: \"zb\" holds focus");

            // Act
            SendMove(zb, NavigationMoveEvent.Direction.Next);
            Assume.That(_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zb), Is.True,
                "Precondition: \"zb\" actually relocated into the shared front layer container");

            // Assert — RED without the fix: the walk stops the instant it steps past "zb" itself, landing on
            // "za" (a co-tenant sharing the SAME container) instead of continuing until it is genuinely
            // outside the whole container — past "parent" — onto "outerAfter".
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

            // Act — TryZLayerExit's own walk stays inside the container or wraps back onto the placeholder
            // itself every step, so it falls through to "no valid exit" (returns false) instead of
            // redirecting anywhere.
            SendMove(zonly, NavigationMoveEvent.Direction.Next);

            // Assert — graceful no-move: focus stays exactly where it was instead of the walk finding a
            // spurious target or the panel throwing/looping.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(zonly));
        }

        #endregion
    }
}
