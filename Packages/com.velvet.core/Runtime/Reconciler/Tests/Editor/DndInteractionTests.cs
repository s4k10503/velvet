using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the drag-and-drop session on a real (headless) editor panel, driven end-to-end through
    /// the panel's own dispatcher (IMGUI-event-constructed pointer events via <c>SendEvent</c>, so the
    /// engine's own pressed-button bookkeeping stays truthful): activation constraints keep clicks
    /// working, an active drag writes/restores the inline translate, collision resolves against live
    /// rects, Escape cancels, and a source unmounting mid-drag scrubs synchronously while deferring the
    /// user cancel callback past the flush — the pool-reuse ghosting contract.
    /// <para>
    /// Also pins the three built-in collision strategies as pure functions over a
    /// <c>DndCollisionQuery</c> — no panel involved: <c>RectIntersection</c> ranks by overlap area
    /// (ties to registration order), <c>ClosestCenter</c> by center distance, and
    /// <c>PointerWithin</c> by pointer containment with innermost-wins nesting.
    /// </para>
    /// </summary>
    internal sealed class DndInteractionTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static readonly List<string> s_started = new();
        private static readonly List<string> s_overIds = new();
        private static readonly List<string> s_ended = new();
        private static int s_cancelCount;
        private static StateUpdater<bool> s_setShowSource;
        private static StateUpdater<bool> s_setAltDraggingClass;
        private static StateUpdater<bool> s_setDeclaringFocusable;
        private static StateUpdater<bool> s_setFlag;
        private static StateUpdater<int> s_setStage;
        private static bool s_innerDisabled;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_started.Clear();
            s_overIds.Clear();
            s_ended.Clear();
            s_cancelCount = 0;
            s_setShowSource = default;
            s_setAltDraggingClass = default;
            s_setDeclaringFocusable = default;
            s_setFlag = default;
            s_setStage = default;
            s_innerDisabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private VisualElement Q(string name) => _host.Root.Q<VisualElement>(name);

        // Collision reads live worldBound rects, so the fixture forces the style/layout pass batchmode
        // never runs on its own — the same discipline as every resolvedStyle-reading fixture.
        private void Mount(System.Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        // Thin aliases over the SHARED pointer senders (TestUtilities), which construct events from
        // IMGUI system events so the engine's own PointerDeviceState (pressed buttons) stays truthful —
        // one implementation for the EditMode and PlayMode suites, so the event shape cannot drift.
        private static void SendPointerDown(VisualElement target, Vector2 position)
            => target.SendPointerDownEvent(position);

        private static void SendPointerMove(VisualElement target, Vector2 position)
            => target.SendPointerMoveEvent(position);

        private static void SendPointerUp(VisualElement target, Vector2 position)
            => target.SendPointerUpEvent(position);

        // A 300x300 scene: a 50x50 draggable at (0,0), a 100x100 droppable at (150,0), and a second,
        // disabled droppable at (150,150). Absolute placement keeps every rect deterministic, and the
        // scope's 4 px distance constraint lets a sub-threshold press stay a click.
        [Component]
        private static VNode Scene()
        {
            var (showSource, setShowSource) = Hooks.UseState(true);
            s_setShowSource = setShowSource;
            return V.DndContext(
                onDragStart: e => s_started.Add(e.Active.Id),
                onDragOver: e => s_overIds.Add(e.Over?.Id),
                onDragEnd: e => s_ended.Add(e.Over?.Id),
                onDragCancel: _ => s_cancelCount++,
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                name: "scope",
                children: new VNode[]
                {
                    showSource
                        ? V.Draggable("item", key: "item", name: "item",
                            whileDraggingClass: "opacity-50",
                            className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]")
                        : null,
                    V.Droppable("slot", key: "slot", name: "slot",
                        className: "absolute left-[150px] top-[0px] w-[100px] h-[100px]"),
                    V.Droppable("dead", key: "dead", name: "dead", disabled: true,
                        className: "absolute left-[150px] top-[150px] w-[100px] h-[100px]"),
                });
        }

        // GREEN_ON_BASE(characterization): a declared 4 px distance, the constraint the base applied by default.
        [Test]
        public void Given_ADraggableWithADistanceActivation_When_APressTravelsBelowTheDistanceAndReleases_Then_NoDragEverStarts()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");

            // Act — 2 px of travel, below the scope's 4 px constraint: a plain click gesture.
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(12, 10));
            SendPointerUp(item, new Vector2(12, 10));

            // Assert
            Assert.That(s_started, Is.Empty);
        }

        [Test]
        public void Given_ADraggableElement_When_APressStaysBelowTheActivationDistance_Then_ThePointerUpStillReachesBubbleListeners()
        {
            // Arrange — the activation threshold exists exactly so presses on draggables keep behaving
            // as plain clicks. (The real Clickable `clicked` contract needs the engine dispatcher's
            // capture routing and is pinned by the PlayMode suite.)
            Mount(Scene);
            var item = Q("item");
            var releases = 0;
            item.RegisterCallback<PointerUpEvent>(_ => releases++);

            // Act
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerUp(item, new Vector2(10, 10));

            // Assert — the sub-threshold release is untouched (no swallow, no suppression).
            Assert.That(releases, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): a declared 4 px distance, the constraint the base applied by default.
        [Test]
        public void Given_ADraggableWithADistanceActivation_When_TravelExceedsTheDistance_Then_TheDragStartsOnceWithTheActiveId()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");

            // Act — cross the 4 px constraint, then keep moving (activation must not repeat).
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            SendPointerMove(item, new Vector2(30, 10));

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { "item" }));
        }

        [Test]
        public void Given_AnActiveTranslateDrag_When_ThePointerMovesByADelta_Then_TheInlineTranslateFollowsIt()
        {
            // Arrange — activate first (10 px travel), so the delta below is measured from activation.
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(s_started, Is.Not.Empty, "Precondition: the drag activated");

            // Act — move a further (15, 25) from the activation point.
            SendPointerMove(item, new Vector2(35, 35));

            // Assert
            var translate = item.style.translate.value;
            Assert.That((translate.x.value, translate.y.value), Is.EqualTo((15f, 25f)));
        }

        [Test]
        public void Given_AnActiveDragOverlappingADroppable_When_ThePointerReleases_Then_OnDragEndReportsThatDroppable()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(s_started, Is.Not.Empty, "Precondition: the drag activated");

            // Act — drag the 50x50 source by +170 px so its translated rect overlaps the slot at x=150,
            // then release there.
            SendPointerMove(item, new Vector2(190, 20));
            SendPointerUp(item, new Vector2(190, 20));

            // Assert
            Assert.That(s_ended, Is.EqualTo(new[] { "slot" }));
        }

        [Test]
        public void Given_AnActiveDragOverEmptySpace_When_ThePointerReleases_Then_OnDragEndReportsNoTarget()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(s_started, Is.Not.Empty, "Precondition: the drag activated");

            // Act — release over the scene's empty bottom-left region.
            SendPointerMove(item, new Vector2(30, 120));
            SendPointerUp(item, new Vector2(30, 120));

            // Assert
            Assert.That(s_ended, Is.EqualTo(new object[] { null }));
        }

        [Test]
        public void Given_ADisabledDroppableUnderTheDraggedRect_When_CollisionRuns_Then_ItNeverBecomesTheOverTarget()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(s_started, Is.Not.Empty, "Precondition: the drag activated");

            // Act — park the dragged rect squarely over the DISABLED droppable at (150,150).
            SendPointerMove(item, new Vector2(190, 190));

            // Assert — every over-change reported so far is null (entering the disabled rect must not
            // produce a winner).
            Assert.That(s_overIds, Has.All.Null);
        }

        [Test]
        public void Given_AnActiveDrag_When_EscapeIsPressed_Then_TheDragCancels()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(s_started, Is.Not.Empty, "Precondition: the drag activated");

            // Act
            using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                evt.target = item;
                item.SendEvent(evt);
            }

            // Assert
            Assert.That(s_cancelCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_ACancelledDrag_When_TheSessionCloses_Then_TheInlineTranslateIsRestored()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            SendPointerMove(item, new Vector2(60, 60));
            Assume.That(item.style.translate.keyword, Is.EqualTo(StyleKeyword.Undefined),
                "Precondition: the drag wrote an inline translate");

            // Act
            using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                evt.target = item;
                item.SendEvent(evt);
            }

            // Assert — the pre-drag inline value (none) is restored verbatim.
            Assert.That(item.style.translate.keyword, Is.Not.EqualTo(StyleKeyword.Undefined));
        }

        [Test]
        public void Given_AnActiveDrag_When_TheSourceUnmountsMidFlush_Then_TheUserCancelFiresExactlyOnceAfterTheFlush()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(s_started, Is.Not.Empty, "Precondition: the drag activated");

            // Act — unmount the source mid-drag: the cleaner scrubs synchronously but must defer the
            // user callback past the flush (a state write from inside it would be silently lost).
            s_setShowSource.Invoke(false);
            _mounted.FlushStateForTest();
            var firedDuringFlush = s_cancelCount;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That((firedDuringFlush, s_cancelCount), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_ASourceUnmountedMidDrag_When_TheFlushCompletes_Then_NoDragResidueRemainsOnTheElement()
        {
            // Arrange — pins the teardown scrub contract on the element itself: after a mid-drag
            // unmount it carries no translate and no while-dragging class (the pool's own reset is the
            // second line of defense for poolable primitives, not exercised here).
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            SendPointerMove(item, new Vector2(60, 60));
            Assume.That(item.ClassListContains("opacity-50"), Is.True,
                "Precondition: the while-dragging class is applied mid-drag");

            // Act
            s_setShowSource.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert — everything the session wrote is gone the moment the element leaves.
            Assert.That(
                (item.ClassListContains("opacity-50"), item.style.translate.keyword == StyleKeyword.Undefined),
                Is.EqualTo((false, false)));
        }

        [Component]
        private static VNode ImmediateActivationScene()
        {
            var (showFirst, setShowFirst) = Hooks.UseState(true);
            s_setShowSource = setShowFirst;
            return V.DndContext(
                onDragStart: e =>
                {
                    s_started.Add(e.Active.Id);
                    // The activeId recipe taken to its edge: the immediate activation's own synchronous
                    // flush unmounts the source.
                    if (e.Active.Id == "first")
                    {
                        s_setShowSource.Invoke(false);
                    }
                },
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    showFirst
                        ? V.Draggable("first", key: "first", name: "first", activation: DragActivation.None,
                            className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]")
                        : null,
                    V.Draggable("second", key: "second", name: "second",
                        className: "absolute left-[100px] top-[0px] w-[50px] h-[50px]"),
                });
        }

        [Test]
        public void Given_AnImmediateActivationWhoseStartUnmountsTheSource_When_TheNextPressArrives_Then_ArmingStillWorks()
        {
            // Arrange — DragActivation.None activates inside the pointer-down dispatch, and its
            // OnDragStart flush tears the source down; the session must already be installed as the
            // tree's active drag when that happens, or the torn-down session wedges arming forever.
            Mount(ImmediateActivationScene);
            Q("first").SendPointerDownEvent(new Vector2(10, 10));
            Assume.That(s_started, Is.EqualTo(new[] { "first" }), "Precondition: the immediate drag started");

            // Act — a fresh gesture on the surviving draggable.
            var second = Q("second");
            second.SendPointerDownEvent(new Vector2(110, 10));
            second.SendPointerMoveEvent(new Vector2(130, 10));

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { "first", "second" }));
        }

        [Component]
        private static VNode SwappingDraggingClassScene()
        {
            var (alt, setAlt) = Hooks.UseState(false);
            s_setAltDraggingClass = setAlt;
            return V.DndContext(
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", key: "item", name: "item",
                        whileDraggingClass: alt ? "opacity-25" : "opacity-50",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                });
        }

        [Test]
        public void Given_AWhileDraggingClassThatChangesMidDrag_When_TheDragCancels_Then_TheOriginallyAppliedClassIsRemoved()
        {
            // Arrange — activate with "opacity-50" applied, then swap the setting mid-drag: restore
            // symmetry must target what was actually applied, not the re-parsed replacement.
            Mount(SwappingDraggingClassScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(30, 10));
            Assume.That(item.ClassListContains("opacity-50"), Is.True,
                "Precondition: the activation-time dragging class is applied");
            s_setAltDraggingClass.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                evt.target = item;
                item.SendEvent(evt);
            }

            // Assert
            Assert.That(item.ClassListContains("opacity-50"), Is.False);
        }

        [Test]
        public void Given_ANonFocusableSourceAnchoredForEscape_When_TheDragEnds_Then_ItKeepsNeitherFocusNorFocusability()
        {
            // Arrange — with nothing focused, activation anchors keyboard focus on the source (made
            // focusable transiently) so the Escape KeyDownEvent is deliverable; the anchor is the
            // session's own creation and every part of it must be undone on close. (An ALREADY-focusable
            // source is focused by the engine's own click-to-focus at the down, which the session must
            // NOT undo — that focus is the user's, not the anchor's.)
            Mount(Scene);
            var item = Q("item");
            Assume.That(_host.Panel.focusController.focusedElement, Is.Null,
                "Precondition: nothing holds focus before the drag");

            // Act
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(30, 10));
            item.SendPointerUpEvent(new Vector2(30, 10));

            // Assert — neither the anchor focus nor the transient focusability survives the drop.
            Assert.That(
                (ReferenceEquals(_host.Panel.focusController.focusedElement, item), item.focusable),
                Is.EqualTo((false, false)));
        }

        [Component]
        private static VNode DeclaredFocusableMidDragScene()
        {
            var (declaring, setDeclaring) = Hooks.UseState(false);
            s_setDeclaringFocusable = setDeclaring;
            return V.DndContext(
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", key: "item", name: "item",
                        props: declaring ? new FiberElementProps { Focusable = true } : null,
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                });
        }

        [Test]
        public void Given_AFocusablePropFirstDeclaredWhileTheAnchorHoldsIt_When_ThePropIsDropped_Then_TheSourceIsNotLeftFocusable()
        {
            // Arrange — with nothing focused, activation makes the source focusable transiently so the Escape
            // KeyDownEvent is deliverable. That write is the session's, not the element's own value, and a
            // Focusable prop declared for the first time while it stands must not be able to capture it: a
            // later render dropping the prop restores what the element carried before Velvet touched the flag.
            Mount(DeclaredFocusableMidDragScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(30, 10));
            var whileAnchored = item.focusable;

            // Act — declare the prop under the anchor, end the drag, then stop declaring it.
            s_setDeclaringFocusable.Invoke(true);
            _mounted.FlushStateForTest();
            SendPointerUp(item, new Vector2(30, 10));
            s_setDeclaringFocusable.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert — the first term keeps the anchor load-bearing: without its transient write the
            // declaration would have nothing to capture and the case would pin nothing.
            Assert.That((whileAnchored, item.focusable), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AFocusablePropDroppedWhileTheAnchorStillNeedsIt_When_TheDragThenEnds_Then_TheAnchorReclaimsTheFlagAndStillGivesItUp()
        {
            // Arrange — with nothing focused, activation makes the source focusable transiently so the Escape
            // KeyDownEvent is deliverable, then a render declares the prop and hands it the flag.
            Mount(DeclaredFocusableMidDragScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(30, 10));
            var whileAnchored = item.focusable;
            s_setDeclaringFocusable.Invoke(true);
            _mounted.FlushStateForTest();

            // Act — the declaration goes away mid-drag, which restores the element's own default over the
            // anchor's write, and the drag then ends.
            s_setDeclaringFocusable.Invoke(false);
            _mounted.FlushStateForTest();
            var afterTheDeclarationWent = item.focusable;
            SendPointerUp(item, new Vector2(30, 10));

            // Assert — the anchor takes the flag back for the rest of the session and hands it back at close.
            // The first term keeps the anchor load-bearing: with nothing transient written there would be
            // nothing to reclaim and the case would pin nothing.
            Assert.That(
                (whileAnchored, afterTheDeclarationWent, item.focusable),
                Is.EqualTo((true, true, false)));
        }

        [Test]
        public void Given_ADraggableInsideNoDndContext_When_APressTravelsPastTheDistance_Then_NothingActivates()
        {
            // Arrange — a draggable with no enclosing scope warns once and stays inert.
            _mounted = V.Mount(_host.Root, V.Component(OrphanDraggable, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var item = Q("orphan");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no enclosing DndContext"));

            // Act
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(30, 10));

            // Assert
            Assert.That(s_started, Is.Empty);
        }

        [Component]
        private static VNode OrphanDraggable() => V.Div(
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("stray", name: "orphan", className: "w-[50px] h-[50px]"),
            });

        [Component]
        private static VNode UndeclaredActivationScene() => V.DndContext(
            onDragStart: e => s_started.Add(e.Active.Id),
            onDragCancel: _ => s_cancelCount++,
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("item", name: "item",
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
            });

        [Test]
        public void Given_NeitherTheDraggableNorItsScopeDeclaresAnActivation_When_ThePointerGoesDown_Then_TheDragStartsAtThePress()
        {
            // Arrange — dnd-kit's PointerSensor with no activation constraint starts the drag on the press.
            Mount(UndeclaredActivationScene);

            // Act
            SendPointerDown(Q("item"), new Vector2(10, 10));

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { "item" }));
        }

        // A draggable whose whole face is a grip child: every press lands on the grip, and both carry a
        // whileTap class. The flag disables the draggable.
        [Component]
        private static VNode TappedGripScene()
        {
            var (disabled, setDisabled) = Hooks.UseState(false);
            s_setFlag = setDisabled;
            return V.DndContext(
                onDragEnd: e => s_ended.Add(e.Over?.Id),
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", name: "item", disabled: disabled, whileTapClass: "lifted",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]",
                        children: new VNode[]
                        {
                            V.Div(name: "grip", className: "w-[50px] h-[50px]", whileTapClass: "pressed"),
                        }),
                });
        }

        [Test]
        public void Given_ADragPressedOnADescendantCarryingWhileTap_When_TheDragEnds_Then_TheDescendantsTapClassIsSettled()
        {
            // Arrange — the session swallows the release at the source, so the descendant's own
            // press signal never sees it. Every position stays inside the grip, so no pointer-out ends
            // the press on its own.
            Mount(TappedGripScene);
            var grip = Q("grip");
            SendPointerDown(grip, new Vector2(10, 10));
            SendPointerMove(grip, new Vector2(20, 10));
            var pressedDuringTheDrag = grip.ClassListContains("pressed");

            // Act
            SendPointerUp(grip, new Vector2(20, 10));

            // Assert
            Assert.That((pressedDuringTheDrag, grip.ClassListContains("pressed")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADragPressedOnADescendantCarryingWhileTap_When_EscapeCancelsIt_Then_TheDescendantsTapClassIsSettled()
        {
            // Arrange
            Mount(TappedGripScene);
            var grip = Q("grip");
            SendPointerDown(grip, new Vector2(10, 10));
            SendPointerMove(grip, new Vector2(20, 10));
            var pressedDuringTheDrag = grip.ClassListContains("pressed");

            // Act
            using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                evt.target = grip;
                grip.SendEvent(evt);
            }

            // Assert
            Assert.That((pressedDuringTheDrag, grip.ClassListContains("pressed")), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the draggable's own whileTap settle, which the base already
        // performs and the descendant settle must not displace.
        [Test]
        public void Given_ADragPressedThroughADescendant_When_TheDragEnds_Then_TheDraggablesOwnTapClassIsSettled()
        {
            // Arrange
            Mount(TappedGripScene);
            var grip = Q("grip");
            var item = Q("item");
            SendPointerDown(grip, new Vector2(10, 10));
            SendPointerMove(grip, new Vector2(20, 10));
            var liftedDuringTheDrag = item.ClassListContains("lifted");

            // Act
            SendPointerUp(grip, new Vector2(20, 10));

            // Assert
            Assert.That((liftedDuringTheDrag, item.ClassListContains("lifted")), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): a closed session leaves no move listener behind, which the
        // base already holds for the draggable and the branch extends to the press path.
        [Test]
        public void Given_ADragPressedThroughADescendantThatEnded_When_ANewPressMovesBelowTheDistance_Then_TheDraggableStaysUntranslated()
        {
            // Arrange
            Mount(TappedGripScene);
            var grip = Q("grip");
            SendPointerDown(grip, new Vector2(10, 10));
            SendPointerMove(grip, new Vector2(20, 10));
            SendPointerUp(grip, new Vector2(20, 10));

            // Act — a fresh press whose 2 px of travel stays under the constraint.
            SendPointerDown(grip, new Vector2(20, 10));
            SendPointerMove(grip, new Vector2(22, 10));

            // Assert
            Assert.That(Q("item").style.translate.keyword, Is.Not.EqualTo(StyleKeyword.Undefined));
        }

        // GREEN_ON_BASE(characterization): a closed session leaves no release listener behind, which the
        // base already holds for the draggable and the branch extends to the press path.
        [Test]
        public void Given_ADragPressedThroughADescendantThatEnded_When_ALaterPressReleasesInPlace_Then_NoSecondDropIsReported()
        {
            // Arrange
            Mount(TappedGripScene);
            var grip = Q("grip");
            SendPointerDown(grip, new Vector2(10, 10));
            SendPointerMove(grip, new Vector2(20, 10));
            SendPointerUp(grip, new Vector2(20, 10));

            // Act
            SendPointerDown(grip, new Vector2(20, 10));
            SendPointerUp(grip, new Vector2(20, 10));

            // Assert
            Assert.That(s_ended.Count, Is.EqualTo(1));
        }

        [Component]
        private static VNode NestedDraggablesScene() => V.DndContext(
            onDragStart: e => s_started.Add(e.Active.Id),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("outer", name: "outer",
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]",
                    children: new VNode[]
                    {
                        V.Draggable("inner", name: "inner", disabled: s_innerDisabled,
                            className: "w-[50px] h-[50px]"),
                    }),
            });

        [Test]
        public void Given_ADraggableInsideAnother_When_APressLandsOnTheInnerOne_Then_OnlyTheInnerOneDrags()
        {
            // Arrange — dnd-kit's innermost activator claims the press before the outer one sees it.
            Mount(NestedDraggablesScene);

            // Act
            SendPointerDown(Q("inner"), new Vector2(10, 10));

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { "inner" }));
        }

        [Test]
        public void Given_ADisabledDraggableInsideAnother_When_APressLandsOnTheInnerOne_Then_TheOuterOneDrags()
        {
            // Arrange — a disabled dnd-kit draggable carries no listeners, so the press reaches the outer one.
            s_innerDisabled = true;
            Mount(NestedDraggablesScene);

            // Act
            SendPointerDown(Q("inner"), new Vector2(10, 10));

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { "outer" }));
        }

        // A draggable holding a plain wrapper, inside it an element whose NoDrag the flag sets, and inside
        // that a thumb: the press path runs thumb, marked element, wrapper.
        [Component]
        private static VNode NoDragChildScene()
        {
            var (flag, setFlag) = Hooks.UseState(true);
            s_setFlag = setFlag;
            return V.DndContext(
                onDragStart: e => s_started.Add(e.Active.Id),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", name: "item",
                        className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]",
                        children: new VNode[]
                        {
                            V.Div(name: "wrapper", className: "w-[80px] h-[80px]", children: new VNode[]
                            {
                                V.Div(name: "control", className: "w-[60px] h-[60px]",
                                    props: new FiberElementProps { NoDrag = flag },
                                    children: new VNode[] { V.Div(name: "thumb", className: "w-[20px] h-[20px]") }),
                            }),
                        }),
                });
        }

        [Test]
        public void Given_AChildMarkedNoDrag_When_APressLandsInsideIt_Then_NoDragStarts()
        {
            // Arrange
            Mount(NoDragChildScene);

            // Act — the press lands on the marked element's own child.
            SendPointerDown(Q("thumb"), new Vector2(10, 10));

            // Assert
            Assert.That(s_started, Is.Empty);
        }

        [Test]
        public void Given_AChildWhoseNoDragARenderDropped_When_APressLandsInsideIt_Then_TheDragStarts()
        {
            // Arrange
            Mount(NoDragChildScene);
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();

            // Act
            SendPointerDown(Q("thumb"), new Vector2(10, 10));

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { "item" }));
        }

        // A draggable whose child is, by stage, a Button carrying NoDrag, nothing, then a Button without it:
        // the empty stage returns the first Button to the element pool before the second one is created.
        [Component]
        private static VNode SwappedNoDragButtonScene()
        {
            var (stage, setStage) = Hooks.UseState(0);
            s_setStage = setStage;
            return V.DndContext(
                onDragStart: e => s_started.Add(e.Active.Id),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", name: "item",
                        className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]",
                        children: new VNode[]
                        {
                            stage == 1
                                ? null
                                : new ElementNode
                                {
                                    Key = stage == 0 ? "marked" : "plain",
                                    ElementType = typeof(Button),
                                    Name = "control",
                                    ClassNames = V.ParseClassNames("w-[60px] h-[30px]"),
                                    Props = new FiberElementProps { NoDrag = stage == 0 },
                                    Children = System.Array.Empty<VNode>(),
                                    Events = System.Array.Empty<FiberEventBinding>(),
                                },
                        }),
                });
        }

        [Test]
        public void Given_ANoDragButtonReplacedByAPooledPlainOne_When_APressLandsOnTheReplacement_Then_TheDragStarts()
        {
            // Arrange — the replacement comes back from the element pool as the very instance that
            // carried NoDrag.
            Mount(SwappedNoDragButtonScene);
            var marked = Q("control");
            s_setStage.Invoke(1);
            _mounted.FlushStateForTest();
            s_setStage.Invoke(2);
            _mounted.FlushStateForTest();
            var plain = Q("control");

            // Act
            SendPointerDown(plain, new Vector2(10, 10));

            // Assert
            Assert.That((ReferenceEquals(marked, plain), s_started.Count), Is.EqualTo((true, 1)));
        }

        [Test]
        public void Given_ADragPressedOnADescendantCarryingWhileTap_When_ARenderDisablesTheDraggableMidDrag_Then_TheDescendantsTapClassIsSettled()
        {
            // Arrange — disabling the active source cancels its session teardown-flavored.
            Mount(TappedGripScene);
            var grip = Q("grip");
            SendPointerDown(grip, new Vector2(10, 10));
            SendPointerMove(grip, new Vector2(20, 10));
            var pressedDuringTheDrag = grip.ClassListContains("pressed");

            // Act
            s_setFlag.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((pressedDuringTheDrag, grip.ClassListContains("pressed")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APendingPress_When_APointerCancelArrives_Then_LaterTravelPastTheDistanceStartsNoDrag()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));

            // Act
            using (var cancel = PointerCancelEvent.GetPooled())
            {
                cancel.target = item;
                item.SendEvent(cancel);
            }
            SendPointerMove(item, new Vector2(30, 10));

            // Assert
            Assert.That(s_started, Is.Empty);
        }

        [Test]
        public void Given_AnActiveDrag_When_APointerCancelArrives_Then_TheDragCancels()
        {
            // Arrange
            Mount(Scene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Act
            using (var cancel = PointerCancelEvent.GetPooled())
            {
                cancel.target = item;
                item.SendEvent(cancel);
            }

            // Assert
            Assert.That((s_started.Count, s_cancelCount), Is.EqualTo((1, 1)));
        }

        private static void SendTouch<TEvent>(VisualElement target, TouchPhase phase)
            where TEvent : PointerEventBase<TEvent>, new()
        {
            using var evt = PointerEventBase<TEvent>.GetPooled(
                new Touch { fingerId = 0, phase = phase, position = new Vector2(10, 10) });
            evt.target = target;
            target.SendEvent(evt);
        }

        [Test]
        public void Given_ATouchDragWhoseReleaseWentUnseen_When_TheSameFingerPressesAgain_Then_TheStaleDragCancels()
        {
            // Arrange — a finger cannot go down twice, so a second press under its id means the
            // first release never reached the session.
            Mount(UndeclaredActivationScene);
            var item = Q("item");
            SendTouch<PointerDownEvent>(item, TouchPhase.Began);

            // Act — the second press, then a release that leaves the finger up for later fixtures.
            SendTouch<PointerDownEvent>(item, TouchPhase.Began);
            SendTouch<PointerUpEvent>(item, TouchPhase.Ended);

            // Assert
            Assert.That(s_cancelCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_AMountedNoDragElement_When_TheTreeIsDisposed_Then_TheContextStopsHoldingIt()
        {
            // Arrange
            Mount(NoDragChildScene);
            var context = _mounted.Root.Reconciler.Context;
            var heldWhileMounted = context.NoDragElements.Count;

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That((heldWhileMounted, context.NoDragElements.Count), Is.EqualTo((1, 0)));
        }

        #region Collision strategies (pure functions, no panel)

        private static DndCollisionQuery Query(Rect activeRect, Vector2 pointer, params DndDroppableRect[] droppables)
            => new(activeRect, pointer, new List<DndDroppableRect>(droppables));

        private static DndDroppableRect Candidate(string id, Rect rect) => new(id, rect, null);

        [Test]
        public void Given_TwoOverlappingCandidates_When_RectIntersectionRuns_Then_TheLargerOverlapWins()
        {
            // Arrange — the active rect overlaps "small" by 10x10 and "large" by 30x10.
            var query = Query(new Rect(0, 0, 40, 10), Vector2.zero,
                Candidate("small", new Rect(30, 0, 100, 100)),
                Candidate("large", new Rect(10, 0, 100, 100)));

            // Act
            var winner = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(winner, Is.EqualTo("large"));
        }

        [Test]
        public void Given_NoCandidateOverlapsTheActiveRect_When_RectIntersectionRuns_Then_NoCollisionIsReported()
        {
            // Arrange
            var query = Query(new Rect(0, 0, 10, 10), Vector2.zero,
                Candidate("far", new Rect(100, 100, 10, 10)));

            // Act
            var winner = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(winner, Is.Null);
        }

        [Test]
        public void Given_TwoCandidatesWithEqualOverlap_When_RectIntersectionRuns_Then_TheFirstRegisteredWins()
        {
            // Arrange — both candidates overlap the active rect by an identical 10x10 corner.
            var query = Query(new Rect(0, 0, 20, 20), Vector2.zero,
                Candidate("first", new Rect(10, 10, 50, 50)),
                Candidate("second", new Rect(10, 10, 50, 50)));

            // Act
            var winner = DndCollisions.RectIntersection(in query);

            // Assert — ties resolve deterministically by registration order.
            Assert.That(winner, Is.EqualTo("first"));
        }

        [Test]
        public void Given_TwoCandidates_When_ClosestCenterRuns_Then_TheNearerCenterWinsEvenWithoutOverlap()
        {
            // Arrange — neither candidate overlaps the active rect; "near" has the closer center.
            var query = Query(new Rect(0, 0, 10, 10), Vector2.zero,
                Candidate("far", new Rect(200, 200, 10, 10)),
                Candidate("near", new Rect(30, 0, 10, 10)));

            // Act
            var winner = DndCollisions.ClosestCenter(in query);

            // Assert
            Assert.That(winner, Is.EqualTo("near"));
        }

        [Test]
        public void Given_NoCandidates_When_ClosestCenterRuns_Then_NoCollisionIsReported()
        {
            // Arrange
            var query = Query(new Rect(0, 0, 10, 10), Vector2.zero);

            // Act
            var winner = DndCollisions.ClosestCenter(in query);

            // Assert
            Assert.That(winner, Is.Null);
        }

        [Test]
        public void Given_APointerInsideOneCandidate_When_PointerWithinRuns_Then_TheContainingRectWins()
        {
            // Arrange — the active rect overlaps "other" more, but the POINTER sits inside "hit".
            var query = Query(new Rect(0, 0, 60, 60), new Vector2(105, 105),
                Candidate("other", new Rect(0, 0, 50, 50)),
                Candidate("hit", new Rect(100, 100, 20, 20)));

            // Act
            var winner = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(winner, Is.EqualTo("hit"));
        }

        [Test]
        public void Given_NestedCandidatesBothContainingThePointer_When_PointerWithinRuns_Then_TheInnermostWins()
        {
            // Arrange
            var query = Query(new Rect(0, 0, 10, 10), new Vector2(55, 55),
                Candidate("outer", new Rect(0, 0, 200, 200)),
                Candidate("inner", new Rect(50, 50, 20, 20)));

            // Act
            var winner = DndCollisions.PointerWithin(in query);

            // Assert — on nesting, the smallest containing rect takes the collision.
            Assert.That(winner, Is.EqualTo("inner"));
        }

        [Test]
        public void Given_APointerOutsideEveryCandidate_When_PointerWithinRuns_Then_NoCollisionIsReported()
        {
            // Arrange
            var query = Query(new Rect(0, 0, 10, 10), new Vector2(500, 500),
                Candidate("a", new Rect(0, 0, 50, 50)),
                Candidate("b", new Rect(100, 100, 50, 50)));

            // Act
            var winner = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(winner, Is.Null);
        }

        #endregion
    }
}
