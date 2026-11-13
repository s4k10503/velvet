using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the drag-and-drop behaviours that close the remaining gaps from dnd-kit, on a real (headless)
    /// editor panel driven through the panel's own dispatcher as <see cref="DndInteractionTests"/> drives its
    /// cases: what a session leaves on an element that leaves the tree mid-drag, an overlay that pairs with its
    /// scope through a portal, the engine's draggable claim on a move, the drag hooks and the monitor, the
    /// keyboard sensor, a custom sensor, announcements, modifiers, an overlay in an editor-panel mount and its
    /// drop animation, the sortable preset and auto-scroll.
    /// </summary>
    internal sealed class DndParityTests
    {
        private HeadlessEditorPanelHost _host;
        private HeadlessEditorPanelHost _otherHost;
        private MountedTree _mounted;
        private VisualElement _mountPoint;
        private object _screenReaderToRestore;
        private AssistiveSupport.ScreenReaderStatusOverride _overrideToRestore;

        private static readonly List<string> s_events = new();
        private static StateUpdater<bool> s_setFlag;
        private static VisualElement s_portalTarget;
        private static IReadOnlyList<DndSensor> s_sensors;
        private static IReadOnlyList<DndModifier> s_modifiers;
        private static DndAccessibility s_accessibility;
        private static DndCollisionDetection s_collisionDetection;
        private static DndAutoScroll s_autoScroll;
        private static (bool IsDragging, bool IsOver, string ActiveId) s_probe;
        private static int s_contextRenders;
        private static float[] s_endDelta;
        private static DndSensorSession s_sensorSession;
        private static double s_clock;
        private static readonly List<int> s_keyboardDroppableCounts = new();

        private static readonly DndMonitorListener s_monitor = new(
            OnDragOver: e => s_events.Add("monitor-over:" + e.Over?.Id),
            OnDragEnd: e => s_events.Add("monitor-end:" + e.Over?.Id));

        private static readonly RecordingScreenReader s_screenReader = new();
        private static readonly FieldInfo s_dispatcherField = typeof(AssistiveSupport)
            .GetField("<notificationDispatcher>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _mountPoint = new VisualElement { name = "mount" };
            _host.Root.Add(_mountPoint);
            s_portalTarget = new VisualElement { name = "portal-target" };
            _host.Root.Add(s_portalTarget);
            s_events.Clear();
            s_setFlag = default;
            s_sensors = null;
            s_modifiers = null;
            s_accessibility = null;
            s_collisionDetection = null;
            s_autoScroll = null;
            s_probe = default;
            s_contextRenders = 0;
            s_endDelta = null;
            s_sensorSession = null;
            s_clock = 0d;
            s_keyboardDroppableCounts.Clear();
            s_screenReader.Announcements.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            _otherHost?.Dispose();
            _otherHost = null;
            s_portalTarget = null;
            if (_screenReaderToRestore != null)
            {
                s_dispatcherField.SetValue(null, _screenReaderToRestore);
                AssistiveSupport.screenReaderStatusOverride = _overrideToRestore;
                _screenReaderToRestore = null;
            }
        }

        private VisualElement Q(string name) => _host.Root.Q<VisualElement>(name);

        private ReconcilerContext Ctx => _mounted.Root.Reconciler.Context;

        // Collision reads live worldBound rects, so the fixture forces the style/layout pass batchmode never runs on
        // its own, as DndInteractionTests does.
        private void Mount(System.Func<VNode> body)
        {
            _mounted = V.Mount(_mountPoint, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private static float[] TranslateOf(VisualElement element)
            => new[] { element.style.translate.value.x.value, element.style.translate.value.y.value };

        private static void SendKey(VisualElement target, KeyCode key)
        {
            using var evt = KeyDownEvent.GetPooled('\0', key, EventModifiers.None);
            evt.target = target;
            target.SendEvent(evt);
        }

        private static int TrickleDownCallbackCount(VisualElement element)
        {
            var registry = typeof(CallbackEventHandler)
                .GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(element);
            if (registry == null)
            {
                return 0;
            }
            var trickleDown = registry.GetType()
                .GetField("m_TrickleDownCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(registry);
            return (int)trickleDown.GetType().GetProperty("Count")!.GetValue(trickleDown);
        }

        // A 50x50 draggable whose face is a grip shown while the flag holds, under a scope whose 4 px constraint
        // keeps a short press pending.
        [Component]
        private static VNode GripScene()
        {
            var (showGrip, setShowGrip) = Hooks.UseState(true);
            s_setFlag = setShowGrip;
            return V.DndContext(
                onDragStart: e => s_events.Add("start:" + e.Active.Id),
                onDragCancel: e => s_events.Add("cancel:" + e.Active.Id),
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", name: "item",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]",
                        children: new VNode[]
                        {
                            showGrip ? V.Div(name: "grip", key: "grip", className: "w-[50px] h-[50px]") : null,
                        }),
                });
        }

        [Test]
        public void Given_AClosedSession_When_AMoveCallbackItLeftBehindRuns_Then_TheSourceKeepsItsTranslate()
        {
            // Arrange — Escape closes the session while the button is still held, and the session's own move
            // handler is registered again by hand: the leak the closed-session guard is there for.
            Mount(GripScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(20, 10));
            var session = Ctx.ActiveDrag;
            SendKey(item, KeyCode.Escape);
            var onDragMove = (EventCallback<PointerMoveEvent>)System.Delegate.CreateDelegate(
                typeof(EventCallback<PointerMoveEvent>), session,
                typeof(DndActiveDrag).GetMethod("OnDragMove", BindingFlags.Instance | BindingFlags.NonPublic)!);
            item.RegisterCallback(onDragMove, TrickleDown.TrickleDown);

            // Act
            item.SendPointerMoveEvent(new Vector2(60, 60));

            // Assert — Undefined is an inline value written; the restore left none.
            Assert.That(item.style.translate.keyword, Is.Not.EqualTo(StyleKeyword.Undefined));
        }

        [Test]
        public void Given_ADragPressedOnAGrip_When_TheGripLeavesTheTreeMidDrag_Then_ItCarriesNoneOfTheSessionsCallbacks()
        {
            // Arrange
            Mount(GripScene);
            var grip = Q("grip");
            var before = TrickleDownCallbackCount(grip);
            grip.SendPointerDownEvent(new Vector2(10, 10));
            grip.SendPointerMoveEvent(new Vector2(20, 10));

            // Act
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(TrickleDownCallbackCount(grip), Is.EqualTo(before));
        }

        // GREEN_ON_BASE(characterization): the base already takes the pending callbacks off; deleting the pending-move or the pending-cancel unregister from DndActiveDrag.ForgetPressChainElement reddens it.
        [Test]
        public void Given_APendingPressOnAGrip_When_TheGripLeavesTheTree_Then_ItCarriesNoneOfTheSessionsCallbacks()
        {
            // Arrange — a press with no travel leaves the session pending, so the grip holds its pending callbacks.
            Mount(GripScene);
            var grip = Q("grip");
            var before = TrickleDownCallbackCount(grip);
            grip.SendPointerDownEvent(new Vector2(10, 10));
            var pressed = TrickleDownCallbackCount(grip);

            // Act
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert — -1 where the press registered nothing on the grip, which no count equals.
            Assert.That(pressed > before ? TrickleDownCallbackCount(grip) : -1, Is.EqualTo(before));
        }

        // GREEN_ON_BASE(characterization): the base already takes a discarded press's callbacks off; deleting any of the three unregisters in DndActiveDrag.UnregisterPendingObservers reddens it.
        [Test]
        public void Given_APressReleasedBelowTheDistance_When_ItsPendingSessionCloses_Then_TheDraggableCarriesNoneOfItsCallbacks()
        {
            // Arrange
            Mount(HookScene);
            var item = Q("item");
            var before = TrickleDownCallbackCount(item);
            item.SendPointerDownEvent(new Vector2(10, 10));
            var pressed = TrickleDownCallbackCount(item);

            // Act — released where it was pressed, under the 4 px constraint.
            item.SendPointerUpEvent(new Vector2(10, 10));

            // Assert — -1 where the press registered nothing on the draggable, which no count equals.
            Assert.That(pressed > before ? TrickleDownCallbackCount(item) : -1, Is.EqualTo(before));
        }

        // GREEN_ON_BASE(characterization): the base already takes a dropped drag's callbacks off; deleting the pointer-down or the pointer-cancel unregister from DndActiveDrag.UnregisterActiveObservers reddens it.
        [Test]
        public void Given_ADraggedItem_When_ItIsReleased_Then_ItCarriesNoneOfTheSessionsCallbacks()
        {
            // Arrange — 10 px of travel crosses the 4 px constraint.
            Mount(HookScene);
            var item = Q("item");
            var before = TrickleDownCallbackCount(item);
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(20, 10));
            var dragging = TrickleDownCallbackCount(item);

            // Act
            item.SendPointerUpEvent(new Vector2(20, 10));

            // Assert — -1 where the drag registered nothing on the draggable, which no count equals.
            Assert.That(dragging > before ? TrickleDownCallbackCount(item) : -1, Is.EqualTo(before));
        }

        // A shadowed draggable under a scope whose 4 px constraint keeps a press pending, unmounted when the flag
        // drops.
        [Component]
        private static VNode RemovableShadowedSourceScene()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setFlag = setShow;
            return V.DndContext(
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    show
                        ? V.Draggable("item", name: "item", key: "item",
                            className: "w-[50px] h-[50px] bg-[#0000ff] shadow-lg")
                        : null,
                });
        }

        // GREEN_ON_BASE(characterization): the base hands back the background the shadow captured, as it does for an element no drag holds; writing `>=` for the `>` of DndActiveDrag.ReleaseClassesOf's dragging-class test reddens it.
        [Test]
        public void Given_AShadowedSourceWhoseBackgroundARefWroteDuringAPress_When_ItUnmounts_Then_ItKeepsTheBackgroundTheShadowCaptured()
        {
            // Arrange — the shadow suppresses the source's background, and the write over it stands for app code
            // reaching the source through a ref.
            Mount(RemovableShadowedSourceScene);
            var item = Q("item");
            var suppressed = SilhouetteFace.IsSentinel(item.style.backgroundColor.value);
            item.SendPointerDownEvent(new Vector2(10, 10));
            var pending = Ctx.ActiveDrag != null;
            item.style.backgroundColor = Color.red;

            // Act
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert — null where the shadow never suppressed the background or the press armed no session.
            Assert.That(suppressed && pending ? item.style.backgroundColor.value : (Color?)null,
                Is.EqualTo(new Color(0f, 0f, 1f, 1f)));
        }

        [Test]
        public void Given_APendingPress_When_ThePointerMoves_Then_TheMoveCarriesTheEnginesDraggableClaim()
        {
            // Arrange — an ancestor's bubble-phase listener reads the mark, where a reorderable ListView's
            // dragger and a touch ScrollView read it.
            Mount(GripScene);
            var item = Q("item");
            var handled = typeof(PointerMoveEvent).GetProperty("isHandledByDraggable",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            var claimed = false;
            Q("mount").RegisterCallback<PointerMoveEvent>(evt => claimed = (bool)handled.GetValue(evt));
            item.SendPointerDownEvent(new Vector2(10, 10));

            // Act — 2 px, under the 4 px constraint, so the session is still pending.
            item.SendPointerMoveEvent(new Vector2(12, 10));

            // Assert
            Assert.That(claimed, Is.True);
        }

        // A scope holding a draggable and a portal into an element outside the scope; inside the portal a
        // component mounts a raw DragOverlay prop on its own re-render.
        [Component]
        private static VNode PortalOverlayScene() => V.DndContext(
            className: "w-[300px] h-[300px]",
            activation: new DragActivation(Distance: 4f),
            children: new VNode[]
            {
                V.Draggable("item", name: "item", movement: DragMovement.None,
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                V.Portal(s_portalTarget, key: "portal", children: new VNode[]
                {
                    V.Component(RawOverlayHolder, key: "holder"),
                }),
            });

        [Component]
        private static VNode RawOverlayHolder()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_setFlag = setShow;
            return show
                ? V.Div(name: "raw-overlay", key: "raw-overlay",
                    props: new FiberElementProps { DragOverlay = new DragOverlaySettings() })
                : V.Fragment(System.Array.Empty<VNode>());
        }

        [Test]
        public void Given_ARawOverlayInAPortal_When_ItsOwnComponentMountsItMidDrag_Then_ItShowsThePreview()
        {
            // Arrange
            Mount(PortalOverlayScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(20, 10));

            // Act — the holder's own re-render mounts the overlay, outside any reconcile of the portal.
            s_setFlag.Invoke(true);
            _mounted.FlushStateForTest();
            item.SendPointerMoveEvent(new Vector2(30, 10));

            // Assert
            Assert.That(Q("raw-overlay").style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        // A 50x50 draggable at (0,0) and a 100x100 droppable at (150,0) under a scope whose 4 px constraint keeps
        // a short press pending, with a component reading the drag through every hook and one reading the context
        // alone.
        [Component]
        private static VNode HookScene() => V.DndContext(
            onDragEnd: e => s_endDelta = new[] { e.Delta.x, e.Delta.y },
            collisionDetection: s_collisionDetection,
            activation: new DragActivation(Distance: 4f),
            sensors: s_sensors,
            modifiers: s_modifiers,
            accessibility: s_accessibility,
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("item", name: "item", key: "item",
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                V.Droppable("slot", name: "slot", key: "slot",
                    className: "absolute left-[150px] top-[0px] w-[100px] h-[100px]"),
                V.Component(HookProbe, key: "probe"),
                V.Component(ContextProbe, key: "context-probe"),
            });

        [Component]
        private static VNode HookProbe()
        {
            var draggable = Hooks.UseDraggable("item");
            var droppable = Hooks.UseDroppable("slot");
            var context = Hooks.UseDndContext();
            Hooks.UseDndMonitor(s_monitor);
            s_probe = (draggable.IsDragging, droppable.IsOver, context.Active?.Id);
            return V.Fragment(System.Array.Empty<VNode>());
        }

        // Counts its renders. Unlike HookProbe it reads no transform, so a move that changes nothing but the drag's
        // translate does not render it.
        [Component]
        private static VNode ContextProbe()
        {
            _ = Hooks.UseDndContext();
            s_contextRenders++;
            return V.Fragment(System.Array.Empty<VNode>());
        }

        // Offered every event a sensor ahead of it passes on, and claims none.
        private sealed class RecordingSensor : DndSensor
        {
            protected override bool TryActivate(EventBase evt, DndSensorActivation activation)
            {
                s_events.Add("offered:" + evt.GetType().Name);
                return false;
            }
        }

        // Ranks the slot alone, at one value, wherever the drag is.
        private static DndCollision[] AlwaysOverTheSlot(in DndCollisionQuery query)
            => new[] { new DndCollision("slot", null, 1d) };

        // Presses the item at (10,10) and moves past the constraint, which activates the drag.
        private VisualElement StartPointerDrag()
        {
            Mount(HookScene);
            // UseDndMonitor registers from a passive effect.
            _mounted.FlushEffectsForTest();
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(20, 10));
            return item;
        }

        [Test]
        public void Given_AComponentReadingUseDraggable_When_ADragOfThatDraggableStarts_Then_ItRendersIsDragging()
        {
            // Arrange
            var item = StartPointerDrag();

            // Act
            item.SendPointerMoveEvent(new Vector2(30, 10));
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_probe.IsDragging, Is.True);
        }

        [Test]
        public void Given_AComponentReadingUseDroppable_When_TheDragMovesOverThatDroppable_Then_ItRendersIsOver()
        {
            // Arrange
            var item = StartPointerDrag();

            // Act — the active rect moved by (160,10) covers the droppable at (150,0).
            item.SendPointerMoveEvent(new Vector2(170, 20));
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_probe.IsOver, Is.True);
        }

        [Test]
        public void Given_AComponentReadingUseDndContext_When_ADragStarts_Then_ItRendersTheActiveId()
        {
            // Arrange
            var item = StartPointerDrag();

            // Act
            item.SendPointerMoveEvent(new Vector2(30, 10));
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_probe.ActiveId, Is.EqualTo("item"));
        }

        [Test]
        public void Given_AComponentWithUseDndMonitor_When_TheDragMovesOverADroppable_Then_ItsListenerHearsTheOverEvent()
        {
            // Arrange
            var item = StartPointerDrag();

            // Act
            item.SendPointerMoveEvent(new Vector2(170, 20));

            // Assert
            Assert.That(s_events, Has.Member("monitor-over:slot"));
        }

        // GREEN_ON_BASE(characterization): the base already tells the monitor of the drop; deleting the OnDragEnd dispatch from DndActiveDrag.Drop reddens it.
        [Test]
        public void Given_AComponentWithUseDndMonitor_When_TheDragIsDroppedOnADroppable_Then_ItsListenerHearsTheEndEvent()
        {
            // Arrange
            var item = StartPointerDrag();
            item.SendPointerMoveEvent(new Vector2(170, 20));

            // Act
            item.SendPointerUpEvent(new Vector2(170, 20));

            // Assert
            Assert.That(s_events, Has.Member("monitor-end:slot"));
        }

        // GREEN_ON_BASE(characterization): the base already publishes nothing for a move that leaves the drag's state as it was; writing `!=` for the `==` comparing ids in DndActiveDrag.SameCollisions reddens it.
        [Test]
        public void Given_AComponentReadingUseDndContextOverADroppable_When_AMoveLeavesTheRankingAsItWas_Then_ItDoesNotRenderAgain()
        {
            // Arrange — the first move after activation puts the slot in the ranking.
            s_collisionDetection = AlwaysOverTheSlot;
            var item = StartPointerDrag();
            item.SendPointerMoveEvent(new Vector2(30, 10));
            _mounted.FlushStateForTest();
            var renders = s_contextRenders;

            // Act
            item.SendPointerMoveEvent(new Vector2(40, 10));
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_contextRenders, Is.EqualTo(renders));
        }

        // GREEN_ON_BASE(characterization): the base already measures the end at the release; deleting the line that applies the modifiers in DndActiveDrag.OnDragUp reddens it.
        [Test]
        public void Given_AnActiveDrag_When_ThePointerReleasesAwayFromItsLastMove_Then_OnDragEndCarriesTheDeltaToTheRelease()
        {
            // Arrange
            var item = StartPointerDrag();
            item.SendPointerMoveEvent(new Vector2(60, 60));

            // Act — released 20 px on along each axis, with no move between.
            item.SendPointerUpEvent(new Vector2(80, 80));

            // Assert — from the press at (10,10).
            Assert.That(s_endDelta, Is.EqualTo(new[] { 70f, 70f }));
        }

        [Test]
        public void Given_TheDefaultSensors_When_SpaceThenRightArrowReachTheDraggable_Then_ItTranslatesOneStepRight()
        {
            // Arrange
            Mount(HookScene);
            var item = Q("item");
            SendKey(item, KeyCode.Space);

            // Act
            SendKey(item, KeyCode.RightArrow);

            // Assert
            Assert.That(TranslateOf(item), Is.EqualTo(new[] { 25f, 0f }));
        }

        // Records how many droppables each arrow key's move is handed, then steps as the default getter does.
        private static Vector2? CountingCoordinates(in DndKeyboardMove move)
        {
            s_keyboardDroppableCounts.Add(move.Droppables.Count);
            return DndKeyboardSensor.DefaultCoordinates(in move);
        }

        // GREEN_ON_BASE(characterization): the base already lists it once; deleting `_queryBuffer.Clear();` from KeyboardMove reddens it.
        [Test]
        public void Given_AKeyboardDragOverOneDroppable_When_ASecondArrowKeyMovesIt_Then_ItsMoveListsThatDroppableOnce()
        {
            // Arrange
            s_sensors = new DndSensor[] { new DndKeyboardSensor(coordinateGetter: CountingCoordinates) };
            Mount(HookScene);
            var item = Q("item");
            SendKey(item, KeyCode.Space);
            SendKey(item, KeyCode.RightArrow);

            // Act
            SendKey(item, KeyCode.RightArrow);

            // Assert
            Assert.That(s_keyboardDroppableCounts.Last(), Is.EqualTo(1));
        }

        // A draggable beside an element the scope can take away mid-drag.
        [Component]
        private static VNode RemovableSiblingScene()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setFlag = setShow;
            return V.DndContext(
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", name: "item", key: "item",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                    show ? V.Div(name: "sibling", key: "sibling", className: "w-[20px] h-[20px]") : null,
                });
        }

        // GREEN_ON_BASE(characterization): the base already removes it; deleting `|| !_pressChain.Remove(element)` from ForgetPressChainElement reddens it.
        // A keyboard drag holds no pointer and no press chain, so an element leaving mid-drag has nothing of the
        // session's to give back.
        [Test]
        public void Given_AKeyboardDrag_When_AnElementOutsideItLeavesTheTree_Then_ItIsRemoved()
        {
            // Arrange
            Mount(RemovableSiblingScene);
            SendKey(Q("item"), KeyCode.Space);

            // Act
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Q("sibling"), Is.Null);
        }

        [Test]
        public void Given_AScopeAnnouncingThroughACallback_When_APointerDragStarts_Then_ItAnnouncesThePickUp()
        {
            // Arrange
            s_accessibility = new DndAccessibility(Announce: text => s_events.Add(text));
            Mount(HookScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));

            // Act
            item.SendPointerMoveEvent(new Vector2(20, 10));

            // Assert
            Assert.That(s_events, Has.Member("Picked up draggable item item."));
        }

        // GREEN_ON_BASE(characterization): the base already announces the drop; deleting the OnDragEnd announcement from DndActiveDrag.Drop reddens it.
        [Test]
        public void Given_AScopeAnnouncingThroughACallback_When_TheDragIsDroppedOnADroppable_Then_ItAnnouncesTheDrop()
        {
            // Arrange
            s_accessibility = new DndAccessibility(Announce: text => s_events.Add(text));
            var item = StartPointerDrag();
            item.SendPointerMoveEvent(new Vector2(170, 20));

            // Act
            item.SendPointerUpEvent(new Vector2(170, 20));

            // Assert
            Assert.That(s_events, Has.Member("Draggable item item was dropped over droppable area slot"));
        }

        private sealed class RecordingScreenReader : IAccessibilityNotificationDispatcher
        {
            internal readonly List<string> Announcements = new();

            public void SendAnnouncement(string announcement) => Announcements.Add(announcement);

            public void SendPageScrolledAnnouncement(string announcement, AccessibilityNode nodeToFocus = null)
            {
            }

            public void SendScreenChanged(AccessibilityNode nodeToFocus = null)
            {
            }

            public void SendLayoutChanged(AccessibilityNode nodeToFocus = null)
            {
            }
        }

        // GREEN_ON_BASE(characterization): the base already sends the screen reader the pick-up; deleting the SendAnnouncement call from DndActiveDrag.Announce reddens it.
        [Test]
        public void Given_AScopeWithNoAnnounceCallbackWhileAScreenReaderIsOn_When_APointerDragStarts_Then_TheScreenReaderIsSentThePickUp()
        {
            // Arrange — the engine's dispatcher and its screen-reader reading are both put back in TearDown.
            _screenReaderToRestore = s_dispatcherField.GetValue(null);
            _overrideToRestore = AssistiveSupport.screenReaderStatusOverride;
            s_dispatcherField.SetValue(null, s_screenReader);
            AssistiveSupport.screenReaderStatusOverride = AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;
            Mount(HookScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));

            // Act
            item.SendPointerMoveEvent(new Vector2(20, 10));

            // Assert
            Assert.That(s_screenReader.Announcements, Has.Member("Picked up draggable item item."));
        }

        // Starts on the N key and moves the drag by (7,3) at once.
        private sealed class NudgeSensor : DndSensor
        {
            protected override bool TryActivate(EventBase evt, DndSensorActivation activation)
            {
                if (evt is not KeyDownEvent { keyCode: KeyCode.N })
                {
                    return false;
                }
                var session = activation.Start(Vector2.zero);
                session?.Move(new Vector2(7, 3));
                return session != null;
            }
        }

        // Starts on the S key and never moves the drag, leaving the session where a case can end it.
        private sealed class StillSensor : DndSensor
        {
            protected override bool TryActivate(EventBase evt, DndSensorActivation activation)
            {
                if (evt is not KeyDownEvent { keyCode: KeyCode.S })
                {
                    return false;
                }
                s_sensorSession = activation.Start(Vector2.zero);
                return s_sensorSession != null;
            }
        }

        [Test]
        public void Given_AScopeWithACustomSensor_When_ItsKeyReachesTheDraggable_Then_TheSensorsMoveTranslatesIt()
        {
            // Arrange
            s_sensors = new DndSensor[] { new NudgeSensor() };
            Mount(HookScene);
            var item = Q("item");

            // Act
            SendKey(item, KeyCode.N);

            // Assert
            Assert.That(TranslateOf(item), Is.EqualTo(new[] { 7f, 3f }));
        }

        // GREEN_ON_BASE(characterization): the base already stops at the pointer sensor once it arms; writing `false` for ArmPointer's closing `true` reddens it.
        [Test]
        public void Given_APointerSensorAheadOfAnother_When_APrimaryPressArmsIt_Then_TheOtherIsOfferedOnlyTheKeyAfterIt()
        {
            // Arrange — no pointer sensor claims a key, so the key's offer shows the second sensor is reached at all.
            s_sensors = new DndSensor[] { new DndPointerSensor(), new RecordingSensor() };
            Mount(HookScene);
            var item = Q("item");

            // Act
            item.SendPointerDownEvent(new Vector2(10, 10));
            SendKey(item, KeyCode.N);

            // Assert — a value no offer list equals where the press armed no session.
            Assert.That(Ctx.ActiveDrag != null ? string.Join(",", s_events) : "the press armed nothing",
                Is.EqualTo("offered:KeyDownEvent"));
        }

        // GREEN_ON_BASE(characterization): the base already passes on a start key it could start no drag with; writing `true` for the `false` DndKeyboardSensor.TryActivate returns there reddens it.
        [Test]
        public void Given_AKeyboardSensorAheadOfAnother_When_ItsStartKeyArrivesDuringAPointerDrag_Then_TheOtherIsOfferedTheKey()
        {
            // Arrange — the pointer drag holds the tree's one drag, so the keyboard sensor's start is refused.
            s_sensors = new DndSensor[] { new DndPointerSensor(), new DndKeyboardSensor(), new RecordingSensor() };
            var item = StartPointerDrag();

            // Act
            SendKey(item, KeyCode.Space);

            // Assert
            Assert.That(s_events, Has.Member("offered:KeyDownEvent"));
        }

        [Test]
        public void Given_AScopeRestrictedToTheHorizontalAxis_When_ThePointerMovesDiagonally_Then_TheSourceMovesOnlyAlongX()
        {
            // Arrange
            s_modifiers = new[] { DndModifiers.RestrictToHorizontalAxis };
            var item = StartPointerDrag();

            // Act — (50,50) past the press at (10,10).
            item.SendPointerMoveEvent(new Vector2(60, 60));

            // Assert
            Assert.That(TranslateOf(item), Is.EqualTo(new[] { 50f, 0f }));
        }

        // A draggable left in place under a scope whose start callback mounts the preview and whose end callback
        // clears it: dnd-kit's activeId recipe.
        [Component]
        private static VNode OverlayRecipeScene()
        {
            var (activeId, setActiveId) = Hooks.UseState((string)null);
            return V.DndContext(
                onDragStart: e => setActiveId.Invoke(e.Active.Id),
                onDragEnd: _ => setActiveId.Invoke((string)null),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", name: "item", key: "item", movement: DragMovement.None,
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                    V.DragOverlay(key: "overlay", children: activeId != null
                        ? new VNode[] { V.Label("ghost", name: "ghost", key: "ghost") }
                        : System.Array.Empty<VNode>()),
                });
        }

        [Test]
        public void Given_ADragOverlayInAnEditorPanelMount_When_ThePointerMoves_Then_ItShowsThePreviewOnThatPanel()
        {
            // Arrange
            Mount(OverlayRecipeScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));

            // Act
            item.SendPointerMoveEvent(new Vector2(110, 60));

            // Assert — the ghost's parent is the overlay's positioner.
            Assert.That(Q("ghost").parent.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        // GREEN_ON_BASE(characterization): main already keeps an overlay mounted at the top of an editor panel's tree in place.
        [Test]
        public void Given_ADragOverlayThatIsTheMountedRoot_When_MountedInAnEditorPanel_Then_ItsChildrenStayInTheMount()
        {
            // Arrange & Act — the first render is the one that decides whether to portal.
            _mounted = V.Mount(_mountPoint, V.DragOverlay(key: "overlay", children: new VNode[]
            {
                V.Label("ghost", name: "ghost", key: "ghost"),
            }));

            // Assert
            Assert.That(_mountPoint.Q<VisualElement>("ghost"), Is.Not.Null,
                "In an editor panel the overlay stays where it is declared instead of portaling");
        }

        [Test]
        public void Given_ADragOverlayAwayFromItsSource_When_TheDropClearsItsChildren_Then_ItKeepsThemForTheDropAnimation()
        {
            // Arrange
            Mount(OverlayRecipeScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(110, 60));

            // Act
            item.SendPointerUpEvent(new Vector2(110, 60));
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Q("ghost"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base already drops them at once; turning the `||` before IsOnSource in DndDropAnimator.Begin into `&&` reddens it.
        [Test]
        public void Given_ADragOverlayBackOnItsSource_When_TheDropClearsItsChildren_Then_TheyGoAtOnce()
        {
            // Arrange — the pointer leaves the press and comes back to it, so the overlay ends where the source is.
            Mount(OverlayRecipeScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(110, 60));
            item.SendPointerMoveEvent(new Vector2(10, 10));

            // Act
            item.SendPointerUpEvent(new Vector2(10, 10));
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Q("ghost"), Is.Null);
        }

        // OverlayRecipeScene with the drop animation turned off.
        [Component]
        private static VNode OverlayWithoutDropAnimationScene()
        {
            var (activeId, setActiveId) = Hooks.UseState((string)null);
            return V.DndContext(
                onDragStart: e => setActiveId.Invoke(e.Active.Id),
                onDragEnd: _ => setActiveId.Invoke((string)null),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", name: "item", key: "item", movement: DragMovement.None,
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                    V.DragOverlay(key: "overlay", dropAnimation: DndDropAnimation.None, children: activeId != null
                        ? new VNode[] { V.Label("ghost", name: "ghost", key: "ghost") }
                        : System.Array.Empty<VNode>()),
                });
        }

        // GREEN_ON_BASE(characterization): the base already drops them at once; turning the first `||` of the no-animation test in DndDropAnimator into `&&` reddens it.
        [Test]
        public void Given_ADragOverlayWithoutADropAnimationAwayFromItsSource_When_TheDropClearsItsChildren_Then_TheyGoAtOnce()
        {
            // Arrange
            Mount(OverlayWithoutDropAnimationScene);
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(110, 60));

            // Act
            item.SendPointerUpEvent(new Vector2(110, 60));
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Q("ghost"), Is.Null);
        }

        // A draggable left in place beside an overlay whose own component shows the preview while the context holds a
        // drag, so that only the overlay renders when the drag ends.
        [Component]
        private static VNode ContextOverlayScene() => V.DndContext(
            sensors: s_sensors,
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("item", name: "item", key: "item", movement: DragMovement.None,
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                V.Component(ContextOverlay, key: "overlay-holder"),
            });

        // Mounts the overlay while the flag holds.
        [Component]
        private static VNode ContextOverlay()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setFlag = setShow;
            var context = Hooks.UseDndContext();
            return show
                ? V.DragOverlay(key: "overlay", children: context.Active != null
                    ? new VNode[] { V.Label("ghost", name: "ghost", key: "ghost") }
                    : System.Array.Empty<VNode>())
                : V.Fragment(System.Array.Empty<VNode>());
        }

        // Presses ContextOverlayScene's item, drags it by (100,50) and releases it there, on the fake clock at zero.
        private VisualElement DropAwayFromTheSource()
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => s_clock);
            Mount(ContextOverlayScene);
            // UseDndContext subscribes from a passive effect.
            _mounted.FlushEffectsForTest();
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(110, 60));
            item.SendPointerUpEvent(new Vector2(110, 60));
            _mounted.FlushStateForTest();
            return item;
        }

        // GREEN_ON_BASE(characterization): the base already finds the source on the drop animation's first frame; writing `!=` for the `==` in DndDropAnimator.TargetOf reddens it.
        [Test]
        public void Given_ADragOverlayDroppedAwayFromItsSource_When_TheDropAnimationsFirstFrameRuns_Then_ItKeepsThePreview()
        {
            // Arrange
            DropAwayFromTheSource();

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Q("ghost"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base already ends the drop animation on its last frame; deleting `Stop(run.Binding);` from DndDropAnimator.Step reddens it.
        [Test]
        public void Given_ADropAnimationKeepingThePreview_When_ItsDurationHasPassed_Then_ThePreviewGoes()
        {
            // Arrange
            DropAwayFromTheSource();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            _mounted.FlushStateForTest();
            var keptThroughTheFirstFrame = Q("ghost") != null;

            // Act — past the default 250 ms.
            s_clock = 0.3d;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            _mounted.FlushStateForTest();

            // Assert — a value that is not null where the animation never kept the preview.
            Assert.That(keptThroughTheFirstFrame ? Q("ghost") : (object)"the animation did not keep the preview", Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already gives the source its opacity back; deleting `RestoreSource(run);` from DndDropAnimator.Stop reddens it.
        [Test]
        public void Given_ADropAnimationThatHidTheSource_When_ItsDurationHasPassed_Then_TheSourceHasNoInlineOpacityLeft()
        {
            // Arrange
            var item = DropAwayFromTheSource();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            var hidden = item.style.opacity.keyword == StyleKeyword.Undefined;

            // Act
            s_clock = 0.3d;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — Undefined is an inline value written, and null stands where the animation never hid the source.
            Assert.That(hidden ? item.style.opacity.keyword : (StyleKeyword?)null, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base already ends the drop animation with its overlay; deleting `DndDropAnimator.Stop(binding);` from DndOverlayDriver.Detach reddens it.
        [Test]
        public void Given_ADropAnimationThatHidTheSource_When_ItsOverlayUnmounts_Then_TheSourceHasNoInlineOpacityLeft()
        {
            // Arrange
            var item = DropAwayFromTheSource();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            var hidden = item.style.opacity.keyword == StyleKeyword.Undefined;

            // Act
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert — Undefined is an inline value written, and null stands where the animation never hid the source.
            Assert.That(hidden ? item.style.opacity.keyword : (StyleKeyword?)null, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base already hands a cancelled drag's overlay to the drop animation; writing `false` for the `animateOverlays: true` in DndActiveDrag.Cancel reddens it.
        [Test]
        public void Given_ADragOverlayAwayFromItsSource_When_EscapeCancelsTheDrag_Then_ItKeepsThePreviewForTheDropAnimation()
        {
            // Arrange
            Mount(ContextOverlayScene);
            _mounted.FlushEffectsForTest();
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(110, 60));

            // Act
            SendKey(item, KeyCode.Escape);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Q("ghost"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base already starts the drop animation for a source out of its panel; making DndDropAnimator.IsOnSource answer true for one reddens it.
        [Test]
        public void Given_AnUnmovedSensorDragWhoseSourceLeftItsPanel_When_TheSensorEndsIt_Then_TheOverlayKeepsThePreviewForTheDropAnimation()
        {
            // Arrange — an overlay that never moved is over its source, which then leaves its panel by a removal the
            // tree did not make.
            s_sensors = new DndSensor[] { new StillSensor() };
            Mount(ContextOverlayScene);
            _mounted.FlushEffectsForTest();
            var item = Q("item");
            SendKey(item, KeyCode.S);
            item.RemoveFromHierarchy();

            // Act
            s_sensorSession.End();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Q("ghost"), Is.Not.Null);
        }

        // A draggable whose overlay a portal puts on another editor panel.
        [Component]
        private static VNode OtherPanelOverlayScene() => V.DndContext(
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("item", name: "item", key: "item", movement: DragMovement.None,
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                V.Portal(s_portalTarget, key: "portal", children: new VNode[]
                {
                    V.DragOverlay(key: "overlay", children: new VNode[] { V.Label("ghost", name: "ghost", key: "ghost") }),
                }),
            });

        // GREEN_ON_BASE(characterization): the base already hides an overlay it cannot place; writing `true` for the `false` DndPanelSpace.TryPoint returns where CanMap refuses reddens it.
        [Test]
        public void Given_ADragOverlayPortaledOntoAnotherEditorPanel_When_TheDragStarts_Then_ItIsHidden()
        {
            // Arrange — the press is the drag under the default activation.
            _otherHost = new HeadlessEditorPanelHost();
            s_portalTarget = new VisualElement { name = "other-panel-target" };
            _otherHost.Root.Add(s_portalTarget);
            Mount(OtherPanelOverlayScene);
            var positioner = s_portalTarget.Q<VisualElement>("ghost").parent;

            // Act
            Q("item").SendPointerDownEvent(new Vector2(10, 10));

            // Assert — the session sizes an overlay it joins, so a width left unset is one that never joined.
            var joined = positioner.style.width.keyword == StyleKeyword.Undefined;
            Assert.That(joined ? positioner.style.display.value : (DisplayStyle?)null, Is.EqualTo(DisplayStyle.None));
        }

        // Three 50x50 items stacked in a vertical list by the container's own column flow: the fixture attaches no
        // stylesheet, so a class the stylesheet owns (absolute) would not place them.
        [Component]
        private static VNode SortableScene() => V.DndContext(
            activation: new DragActivation(Distance: 4f),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.SortableContext(new[] { "a", "b", "c" }, strategy: DndSortingStrategies.VerticalList, key: "list",
                    children: new VNode[]
                    {
                        V.Sortable("a", key: "a", name: "a", className: "w-[50px] h-[50px]"),
                        V.Sortable("b", key: "b", name: "b", className: "w-[50px] h-[50px]"),
                        V.Sortable("c", key: "c", name: "c", className: "w-[50px] h-[50px]"),
                    }),
            });

        [Test]
        public void Given_AVerticalSortableList_When_TheFirstItemIsDraggedOverTheLast_Then_TheMiddleItemMovesUpOneSlot()
        {
            // Arrange
            Mount(SortableScene);
            var first = Q("a");
            first.SendPointerDownEvent(new Vector2(10, 10));
            first.SendPointerMoveEvent(new Vector2(20, 10));

            // Act — the active rect lands on the last item's slot.
            first.SendPointerMoveEvent(new Vector2(20, 110));

            // Assert
            Assert.That(TranslateOf(Q("b")), Is.EqualTo(new[] { 0f, -50f }));
        }

        // SortableScene, its middle item easing linearly by a class of its own, which writes the timing inline.
        [Component]
        private static VNode SortableSceneWithAnEasedItem() => V.DndContext(
            activation: new DragActivation(Distance: 4f),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.SortableContext(new[] { "a", "b", "c" }, strategy: DndSortingStrategies.VerticalList, key: "list",
                    children: new VNode[]
                    {
                        V.Sortable("a", key: "a", name: "a", className: "w-[50px] h-[50px]"),
                        V.Sortable("b", key: "b", name: "b", className: "w-[50px] h-[50px] ease-[cubic-bezier(0,0,1,1)]"),
                        V.Sortable("c", key: "c", name: "c", className: "w-[50px] h-[50px]"),
                    }),
            });

        [Test]
        public void Given_AVerticalSortableListThatShiftedAnItemWithATimingOfItsOwn_When_TheDragEnds_Then_TheItemHasItsOwnTimingBack()
        {
            // Arrange
            Mount(SortableSceneWithAnEasedItem);
            var first = Q("a");
            first.SendPointerDownEvent(new Vector2(10, 10));
            first.SendPointerMoveEvent(new Vector2(20, 10));
            first.SendPointerMoveEvent(new Vector2(20, 110));

            // Act
            first.SendPointerUpEvent(new Vector2(20, 110));

            // Assert — read as null where the item holds no single timing, which is not a mode.
            var timing = Q("b").style.transitionTimingFunction.value;
            Assert.That(timing is { Count: 1 } ? timing[0].mode : (EasingMode?)null, Is.EqualTo(EasingMode.Linear));
        }

        // GREEN_ON_BASE(characterization): the base already takes the transition off; deleting `element.style.transitionProperty = saved.TransitionProperty;` from DndSortableSession.Put reddens it.
        [Test]
        public void Given_AVerticalSortableListThatShiftedItsMiddleItem_When_TheDragEnds_Then_TheMiddleItemHasNoInlineTransitionProperty()
        {
            // Arrange
            Mount(SortableScene);
            var first = Q("a");
            first.SendPointerDownEvent(new Vector2(10, 10));
            first.SendPointerMoveEvent(new Vector2(20, 10));
            first.SendPointerMoveEvent(new Vector2(20, 110));

            // Act
            first.SendPointerUpEvent(new Vector2(20, 110));

            // Assert
            Assert.That(Q("b").style.transitionProperty.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // A draggable at the top of a 100 px ScrollView over 400 px of content.
        [Component]
        private static VNode ScrollScene() => V.DndContext(
            autoScroll: s_autoScroll,
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.ScrollView(key: "scroller", name: "scroller", className: "w-[100px] h-[100px]", children: new VNode[]
                {
                    V.Div(key: "tall", className: "w-[100px] h-[400px]", children: new VNode[]
                    {
                        V.Draggable("item", name: "item", key: "item", className: "w-[50px] h-[50px]"),
                    }),
                }),
            });

        [Test]
        public void Given_ADragInsideAScrollView_When_ThePointerMovesIntoItsBottomEdgeZone_Then_ItScrollsDown()
        {
            // Arrange — the press is the drag; the second move is toward the bottom and inside its 20 px zone.
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => s_clock);
            Mount(ScrollScene);
            // The scroller's range is set to the 300 px the 400 px of content overflows the 100 px view by.
            _host.Root.Q<ScrollView>("scroller").verticalScroller.highValue = 300f;
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(10, 40));
            item.SendPointerMoveEvent(new Vector2(10, 95));
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Act — one more scheduler run, 100 ms on from the one above.
            s_clock = 0.1d;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(_host.Root.Q<ScrollView>("scroller").scrollOffset.y, Is.GreaterThan(0f));
        }

        // GREEN_ON_BASE(characterization): the base already moves the source with the view it scrolls; deleting `WriteSourceTranslate();` from DndActiveDrag.OnAutoScrolled reddens it.
        [Test]
        public void Given_ADragInsideAScrollView_When_TheViewAutoScrolls_Then_TheSourcesTranslateMakesUpTheScroll()
        {
            // Arrange — the drag of the case above: 85 px down, into the bottom edge zone.
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => s_clock);
            Mount(ScrollScene);
            var scroller = _host.Root.Q<ScrollView>("scroller");
            scroller.verticalScroller.highValue = 300f;
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(10, 40));
            item.SendPointerMoveEvent(new Vector2(10, 95));
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Act
            s_clock = 0.1d;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — the translate less the scroll is the drag's own 85 px; NaN where the view never scrolled.
            var scrolled = scroller.scrollOffset.y;
            Assert.That(scrolled > 0f ? TranslateOf(item)[1] - scrolled : float.NaN, Is.EqualTo(85f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base already leaves an axis with no edge zone unscrolled; writing `>=` for the `>` of DndAutoScroller.VelocityFor's vertical zone test reddens it.
        [Test]
        public void Given_AnAutoScrollWithNoVerticalEdgeZone_When_ThePointerMovesDownPastTheViewsBottomEdge_Then_ItDoesNotScroll()
        {
            // Arrange
            s_autoScroll = new DndAutoScroll(ThresholdY: 0f);
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => s_clock);
            Mount(ScrollScene);
            var scroller = _host.Root.Q<ScrollView>("scroller");
            scroller.verticalScroller.highValue = 300f;
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(10, 40));
            item.SendPointerMoveEvent(new Vector2(10, 150));
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Act
            s_clock = 0.1d;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(scroller.scrollOffset.y, Is.EqualTo(0f));
        }
    }
}
