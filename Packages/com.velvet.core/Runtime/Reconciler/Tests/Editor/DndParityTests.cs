using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
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
        private MountedTree _mounted;
        private VisualElement _mountPoint;

        private static readonly List<string> s_events = new();
        private static StateUpdater<bool> s_setFlag;
        private static VisualElement s_portalTarget;
        private static IReadOnlyList<DndSensor> s_sensors;
        private static IReadOnlyList<DndModifier> s_modifiers;
        private static DndAccessibility s_accessibility;
        private static (bool IsDragging, bool IsOver, string ActiveId) s_probe;
        private static double s_clock;

        private static readonly DndMonitorListener s_monitor =
            new(OnDragOver: e => s_events.Add("monitor-over:" + e.Over?.Id));

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
            s_probe = default;
            s_clock = 0d;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            s_portalTarget = null;
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
        // a short press pending, with a component reading the drag through every hook.
        [Component]
        private static VNode HookScene() => V.DndContext(
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

        [Test]
        public void Given_AScopeRestrictedToTheHorizontalAxis_When_ThePointerMovesDiagonally_Then_TheSourceMovesOnlyAlongX()
        {
            // Arrange
            s_modifiers = new[] { DndModifiers.RestrictToHorizontalAxis };
            var item = StartPointerDrag();

            // Act — (40,50) past the activation point at (20,10).
            item.SendPointerMoveEvent(new Vector2(60, 60));

            // Assert
            Assert.That(TranslateOf(item), Is.EqualTo(new[] { 40f, 0f }));
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

        // Three 50x50 items stacked in a vertical list.
        [Component]
        private static VNode SortableScene() => V.DndContext(
            activation: new DragActivation(Distance: 4f),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.SortableContext(new[] { "a", "b", "c" }, strategy: DndSortingStrategies.VerticalList, key: "list",
                    children: new VNode[]
                    {
                        V.Sortable("a", key: "a", name: "a", className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                        V.Sortable("b", key: "b", name: "b", className: "absolute left-[0px] top-[50px] w-[50px] h-[50px]"),
                        V.Sortable("c", key: "c", name: "c", className: "absolute left-[0px] top-[100px] w-[50px] h-[50px]"),
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

        // A draggable at the top of a 100 px ScrollView over 400 px of content.
        [Component]
        private static VNode ScrollScene() => V.DndContext(
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
            var item = Q("item");
            item.SendPointerDownEvent(new Vector2(10, 10));
            item.SendPointerMoveEvent(new Vector2(10, 40));
            item.SendPointerMoveEvent(new Vector2(10, 95));

            // Act
            s_clock = 0.1d;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(_host.Root.Q<ScrollView>("scroller").scrollOffset.y, Is.GreaterThan(0f));
        }
    }
}
