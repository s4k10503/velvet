using System.Collections.Generic;
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
    /// scope through a portal, the engine's draggable claim on a move, the keyboard sensor, the context's
    /// public state, modifiers and the sortable preset.
    /// </summary>
    internal sealed class DndParityTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private VisualElement _mountPoint;

        private static readonly List<string> s_events = new();
        private static StateUpdater<bool> s_setFlag;
        private static VisualElement s_portalTarget;

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
    }
}
