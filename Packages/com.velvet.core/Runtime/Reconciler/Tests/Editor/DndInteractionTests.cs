using System.Collections.Generic;
using System.Linq;
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
    /// Also pins the four built-in collision strategies as pure functions over a
    /// <c>DndCollisionQuery</c> — no panel involved — each returning its full ranking: <c>RectIntersection</c>
    /// by intersection ratio, <c>ClosestCenter</c> by center distance, <c>ClosestCorners</c> and
    /// <c>PointerWithin</c> by mean corner distance, with registration order kept among equal values.
    /// </para>
    /// </summary>
    internal sealed class DndInteractionTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static readonly List<string> s_started = new();
        private static readonly List<string> s_overIds = new();
        private static readonly List<string> s_ended = new();
        private static readonly List<string> s_overRankings = new();
        private static readonly List<string> s_endedRankings = new();
        private static string s_receivedAttachOrder;
        private static int s_cancelCount;
        private static int s_blurs;
        private static StateUpdater<bool> s_setShowSource;
        private static StateUpdater<bool> s_setAltDraggingClass;
        private static StateUpdater<bool> s_setDeclaringFocusable;
        private static StateUpdater<bool> s_setFlag;
        private static StateUpdater<int> s_setStage;
        private static bool s_innerDisabled;
        private static bool s_outerNoDrag;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_started.Clear();
            s_overIds.Clear();
            s_ended.Clear();
            s_overRankings.Clear();
            s_endedRankings.Clear();
            s_receivedAttachOrder = null;
            s_cancelCount = 0;
            s_blurs = 0;
            s_setShowSource = default;
            s_setAltDraggingClass = default;
            s_setDeclaringFocusable = default;
            s_setFlag = default;
            s_setStage = default;
            s_innerDisabled = false;
            s_outerNoDrag = false;
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

        // A draggable and a droppable whose own bracket values a drag's classes set the same properties as.
        private static VNode ChannelOverBaseScene() => V.DndContext(
            activation: new DragActivation(Distance: 4f),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("item", key: "item", name: "item", whileDraggingClass: "opacity-50 shadow-lg",
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px] opacity-[0.9] bg-white"),
                V.Droppable("slot", key: "slot", name: "slot", whileOverClass: "bg-[#ff0000]",
                    whileDragActiveClass: "w-20",
                    className: "absolute left-[150px] top-[0px] w-[100px] h-[100px] bg-[#0000ff]"),
            });

        [Test]
        public void Given_ADraggableWithItsOwnOpacity_When_ItIsDragged_Then_ItsOwnInlineOpacityStandsDown()
        {
            // Arrange
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));

            // Act
            SendPointerMove(item, new Vector2(20, 10));

            // Assert
            Assert.That(item.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ADraggableWithItsOwnOpacity_When_ItIsDraggedAndDropped_Then_ItsOwnOpacityComesBack()
        {
            // Arrange
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            var stoodDown = item.style.opacity.keyword == StyleKeyword.Null;

            // Act
            SendPointerUp(item, new Vector2(20, 10));

            // Assert — NaN where the drag never stood the own opacity down, so it had nothing to give back.
            Assert.That(stoodDown ? item.style.opacity.value : float.NaN, Is.EqualTo(0.9f));
        }

        [Test]
        public void Given_ADraggingClassThatPaints_When_ItIsDragged_Then_ThePaintIsBound()
        {
            // Arrange — shadow-lg is a paint Velvet draws itself.
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));

            // Act
            SendPointerMove(item, new Vector2(20, 10));

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(item), Is.True);
        }

        [Test]
        public void Given_ADraggingClassThatPaints_When_ItIsDraggedAndDropped_Then_ThePaintGoes()
        {
            // Arrange
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            var bound = _mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(item);

            // Act
            SendPointerUp(item, new Vector2(20, 10));

            // Assert — the paint the drag bound, gone once it ends.
            Assert.That((bound, _mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(item)),
                Is.EqualTo((true, false)));
        }

        // A skewed draggable whose dragging class brings in a background of its own.
        private static VNode SkewedDraggableScene() => V.DndContext(
            activation: new DragActivation(Distance: 4f),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("item", key: "item", name: "item", whileDraggingClass: "bg-[#ff0000]",
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px] bg-[#0000ff] -skew-x-6"),
            });

        [Test]
        public void Given_ASkewedDraggableWithADraggingBackground_When_ItIsDragged_Then_ItsFaceTakesTheDraggingColor()
        {
            // Arrange
            Mount(SkewedDraggableScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));

            // Act
            SendPointerMove(item, new Vector2(20, 10));

            // Assert — the skew silhouette repaints the face, so the colour it paints is what the drag shows.
            Assert.That(_mounted.Root.Reconciler.Context.SkewBindings[item].BgColor, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_ADroppableWithItsOwnBackground_When_TheItemIsOverIt_Then_TheOverClassTakesTheBackground()
        {
            // Arrange
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            var slot = Q("slot");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Act
            SendPointerMove(item, new Vector2(180, 30));

            // Assert
            Assert.That(slot.style.backgroundColor.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_ADroppableWithItsOwnBackground_When_TheItemIsDroppedOnIt_Then_TheOverClassGivesItBack()
        {
            // Arrange
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            var slot = Q("slot");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            SendPointerMove(item, new Vector2(180, 30));
            var tookIt = slot.style.backgroundColor.value == Color.red;

            // Act
            SendPointerUp(item, new Vector2(180, 30));

            // Assert — clear where the over class never took the background, so it had nothing to give back.
            Assert.That(tookIt ? slot.style.backgroundColor.value : Color.clear, Is.EqualTo(Color.blue));
        }

        // A droppable that paints while a drag is active and while the item is over it, which flag unmounts.
        [Component]
        private static VNode PaintingDroppableScene()
        {
            var (showSlot, setShowSlot) = Hooks.UseState(true);
            s_setFlag = setShowSlot;
            return V.DndContext(
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", key: "item", name: "item",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                    showSlot
                        ? V.Droppable("slot", key: "slot", name: "slot", whileOverClass: "shadow-lg bg-red-500",
                            whileDragActiveClass: "ring-2 w-20",
                            className: "absolute left-[150px] top-[0px] w-[100px] h-[100px] bg-white")
                        : null,
                });
        }

        [Test]
        public void Given_ADroppableWhoseActiveClassPaints_When_ADragIsActive_Then_ItPaints()
        {
            // Arrange
            Mount(PaintingDroppableScene);
            var item = Q("item");
            var slot = Q("slot");

            // Act
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.RingBindings.ContainsKey(slot), Is.True);
        }

        [Test]
        public void Given_ADroppableWhoseOverClassPaints_When_TheItemIsOverIt_Then_ItPaints()
        {
            // Arrange
            Mount(PaintingDroppableScene);
            var item = Q("item");
            var slot = Q("slot");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Act
            SendPointerMove(item, new Vector2(180, 30));

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(slot), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base takes the drag's classes off the class list itself.
        // What reddens it is the droppable's detach leaving its active or over classes on.
        [Test]
        public void Given_ADroppableUnmountedMidDrag_When_TheFlushCompletes_Then_NoDragClassRemainsOnIt()
        {
            // Arrange
            Mount(PaintingDroppableScene);
            var item = Q("item");
            var slot = Q("slot");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            SendPointerMove(item, new Vector2(180, 30));

            // Act
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(slot.ClassListContains("w-20") || slot.ClassListContains("bg-red-500"), Is.False);
        }

        // A draggable Motion with a dragging class and a blur handler, which flag unmounts.
        [Component]
        private static VNode BlurringSourceScene()
        {
            var (showSource, setShowSource) = Hooks.UseState(true);
            s_setFlag = setShowSource;
            return V.DndContext(
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    showSource
                        ? V.Motion(name: "item", key: "item", className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]",
                            props: new FiberElementProps
                            {
                                Draggable = new DraggableSettings("item", WhileDraggingClass: "opacity-50"),
                            },
                            events: new FiberEventBinding[] { new BlurBinding { Handler = _ => s_blurs++ } })
                        : null,
                });
        }

        // GREEN_ON_BASE(characterization): the base unbinds the source's handlers before it cancels the drag.
        // What reddens it is the drag cleanup running ahead of the style group, whose unbind it then precedes.
        [Test]
        public void Given_ADraggedSourceWithABlurHandler_When_ItUnmountsMidDrag_Then_TheHandlerIsNotCalled()
        {
            // Arrange
            Mount(BlurringSourceScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(item.panel.focusController.focusedElement, Is.SameAs(item),
                "Precondition: a source never focused has nothing to blur");

            // Act
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_blurs, Is.EqualTo(0));
        }

        // A draggable and a droppable whose drag classes start without a paint and gain one on a re-render.
        [Component]
        private static VNode PaintArrivingScene()
        {
            var (painting, setPainting) = Hooks.UseState(false);
            s_setFlag = setPainting;
            return V.DndContext(
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Draggable("item", key: "item", name: "item", whileDraggingClass: painting ? "shadow-lg" : "opacity-50",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                    V.Droppable("slot", key: "slot", name: "slot", whileOverClass: painting ? "shadow-lg" : null,
                        className: "absolute left-[150px] top-[0px] w-[100px] h-[100px]"),
                });
        }

        [Test]
        public void Given_ADraggingClassThatGainsAPaintOnARerender_When_ItIsDragged_Then_ItPaints()
        {
            // Arrange
            Mount(PaintArrivingScene);
            s_setFlag.Invoke(true);
            _mounted.FlushStateForTest();
            var item = Q("item");

            // Act
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(item), Is.True);
        }

        [Test]
        public void Given_AnOverClassThatArrivesWithAPaintOnARerender_When_TheItemIsOverIt_Then_ItPaints()
        {
            // Arrange
            Mount(PaintArrivingScene);
            s_setFlag.Invoke(true);
            _mounted.FlushStateForTest();
            var item = Q("item");
            var slot = Q("slot");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Act
            SendPointerMove(item, new Vector2(180, 30));

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(slot), Is.True);
        }

        // A V.Motion made draggable through its props, whose dragging class is a gradient — a paint a Motion
        // draws, unlike the silhouettes.
        private static VNode DraggableMotionScene() => V.DndContext(
            activation: new DragActivation(Distance: 4f),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Motion(name: "item", key: "item", className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]",
                    props: new FiberElementProps
                    {
                        Draggable = new DraggableSettings("item",
                            WhileDraggingClass: "bg-gradient-to-r from-red-500 to-blue-500"),
                    }),
            });

        // A draggable Motion halving its opacity while dragged, which springs from opacity-100 to opacity-0 once
        // s_setFlag says so.
        [Component]
        private static VNode DraggableSwapScene()
        {
            var (lit, setLit) = Hooks.UseState(false);
            s_setFlag = setLit;
            var poses = new Dictionary<string, MotionVariant> { ["a"] = "opacity-100", ["b"] = "opacity-0" };
            return V.DndContext(
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                children: new VNode[]
                {
                    V.Motion(name: "item", key: "item", className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]",
                        variants: poses, animate: lit ? "b" : "a",
                        transition: new StyleTransitionConfig
                        {
                            Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f,
                        },
                        props: new FiberElementProps
                        {
                            Draggable = new DraggableSettings("item", WhileDraggingClass: "opacity-50"),
                        }),
                });
        }

        [Test]
        public void Given_ADraggableMotionSpringingItsOpacity_When_ItIsDraggedPartWay_Then_TheSpringLeavesTheOpacityToTheDraggingClass()
        {
            // Arrange — the swap's spring started and run once, so it writes the opacity.
            Mount(DraggableSwapScene);
            s_setFlag.Invoke(true);
            _mounted.FlushStateForTest();
            var item = Q("item");
            EditorPanelTestHelpers.DriveSchedulerOnce(item.panel);
            var driving = item.style.opacity.keyword == StyleKeyword.Undefined;

            // Act
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Assert — read as null where the spring never wrote the opacity.
            Assert.That(driving ? item.style.opacity.keyword : (StyleKeyword?)null, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ADraggableMotionWhoseDraggingClassPaints_When_ItIsDragged_Then_ItPaints()
        {
            // Arrange
            Mount(DraggableMotionScene);
            var item = Q("item");

            // Act
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.GradientBackgrounds.ContainsKey(item), Is.True);
        }

        [Test]
        public void Given_ADroppableWithItsOwnWidth_When_ADragIsActive_Then_ItsOwnInlineWidthStandsDown()
        {
            // Arrange
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            var slot = Q("slot");
            SendPointerDown(item, new Vector2(10, 10));

            // Act
            SendPointerMove(item, new Vector2(20, 10));

            // Assert
            Assert.That(slot.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ADroppableWithItsOwnWidth_When_ADragEnds_Then_ItsOwnWidthComesBack()
        {
            // Arrange
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            var slot = Q("slot");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            var stoodDown = slot.style.width.keyword == StyleKeyword.Null;

            // Act
            SendPointerUp(item, new Vector2(20, 10));

            // Assert — NaN where the drag never stood the own width down, so it had nothing to give back.
            Assert.That(stoodDown ? slot.style.width.value.value : float.NaN, Is.EqualTo(100f));
        }

        [Test]
        public void Given_ADroppableWithItsOwnBackground_When_ADragPassesOverItAndLeaves_Then_TheOverClassGivesItBack()
        {
            // Arrange
            Mount(ChannelOverBaseScene);
            var item = Q("item");
            var slot = Q("slot");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            SendPointerMove(item, new Vector2(180, 30));
            var tookIt = slot.style.backgroundColor.value == Color.red;

            // Act
            SendPointerMove(item, new Vector2(20, 200));

            // Assert — clear where the over class never took the background, so it had nothing to give back.
            Assert.That(tookIt ? slot.style.backgroundColor.value : Color.clear, Is.EqualTo(Color.blue));
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
                    props: new FiberElementProps { NoDrag = s_outerNoDrag },
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

        // GREEN_ON_BASE(characterization): NoDrag on an ancestor must still leave an inner draggable armed.
        [Test]
        public void Given_ADraggableInsideOneMarkedNoDrag_When_APressLandsOnTheInnerOne_Then_OnlyTheInnerOneDrags()
        {
            // Arrange
            s_outerNoDrag = true;
            Mount(NestedDraggablesScene);

            // Act
            SendPointerDown(Q("inner"), new Vector2(10, 10));

            // Assert
            Assert.That(s_started, Is.EqualTo(new[] { "inner" }));
        }

        [Component]
        private static VNode NoDragSourceScene()
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
                        props: new FiberElementProps { NoDrag = flag },
                        children: new VNode[] { V.Div(name: "thumb", className: "w-[20px] h-[20px]") }),
                });
        }

        [TestCase("item")]
        [TestCase("thumb")]
        public void Given_ADraggableMarkedNoDrag_When_PressedBeforeAndAfterClearingTheMarker_Then_OnlyTheLaterPressStartsADrag(string targetName)
        {
            // Arrange
            Mount(NoDragSourceScene);

            // Act
            SendPointerDown(Q(targetName), new Vector2(10, 10));
            var startsBeforeClearing = s_started.Count;
            SendPointerUp(Q(targetName), new Vector2(10, 10));
            s_setFlag.Invoke(false);
            _mounted.FlushStateForTest();
            SendPointerDown(Q(targetName), new Vector2(10, 10));

            // Assert
            Assert.That((startsBeforeClearing, string.Join(",", s_started)), Is.EqualTo((0, "item")));
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

        // GREEN_ON_BASE(characterization): disabling an active source must also block its next press.
        [Test]
        public void Given_ADragPressedOnADescendantCarryingWhileTap_When_ARenderDisablesTheDraggableMidDrag_Then_TheTapSettlesAndALaterPressCannotDrag()
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
            var pressedAfterDisabling = grip.ClassListContains("pressed");
            SendPointerUp(grip, new Vector2(20, 10));
            SendPointerDown(grip, new Vector2(10, 10));
            SendPointerMove(grip, new Vector2(20, 10));
            SendPointerMove(grip, new Vector2(30, 10));

            // Assert
            Assert.That((pressedDuringTheDrag, pressedAfterDisabling,
                Q("item").style.translate.keyword != StyleKeyword.Undefined), Is.EqualTo((true, false, true)));
        }

        // GREEN_ON_BASE(characterization): a pending session the base already discards on a pointer cancel, which its root and source
        // registrations see here; this branch moves those registrations onto the press chain.
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

        // GREEN_ON_BASE(characterization): an active session the base already cancels on a pointer cancel, delivered to the capturing
        // source here; this branch moves that registration onto the press chain.
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

        // GREEN_ON_BASE(characterization): the base already cancels a touch drag on a second press under the same finger id, delivered
        // to the capturing source here; this branch moves that registration onto the press chain.
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

        // A strategy that ranks the candidates in reverse registration order, so the first entry is the
        // last-registered droppable wherever the pointer is, and whose values fall as the ranking rises so
        // the first entry does not hold the smallest or the greatest one.
        [Component]
        private static VNode ReverseRankedScene()
        {
            return V.DndContext(
                onDragOver: e => s_overRankings.Add(string.Join(",", e.Collisions.Select(c => c.Id))),
                onDragEnd: e =>
                {
                    s_ended.Add(e.Over?.Id);
                    s_endedRankings.Add(string.Join(",", e.Collisions.Select(c => c.Id)));
                },
                collisionDetection: (in DndCollisionQuery query) =>
                {
                    var ranked = new DndCollision[query.Droppables.Count];
                    for (var i = 0; i < ranked.Length; i++)
                    {
                        var candidate = query.Droppables[ranked.Length - 1 - i];
                        ranked[i] = new DndCollision(candidate.Id, candidate.Data, ranked.Length - i);
                    }
                    return ranked;
                },
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                name: "scope",
                children: new VNode[]
                {
                    V.Draggable("item", key: "item", name: "item",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                    V.Droppable("first", key: "first", name: "first",
                        className: "absolute left-[150px] top-[0px] w-[100px] h-[100px]"),
                    V.Droppable("second", key: "second", name: "second",
                        className: "absolute left-[150px] top-[150px] w-[100px] h-[100px]"),
                });
        }

        // A strategy that reports no collision by returning null.
        [Component]
        private static VNode NullStrategyScene()
        {
            return V.DndContext(
                onDragEnd: e => s_ended.Add(e.Over?.Id),
                collisionDetection: (in DndCollisionQuery query) => null,
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                name: "scope",
                children: new VNode[]
                {
                    V.Draggable("item", key: "item", name: "item",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                    V.Droppable("slot", key: "slot", name: "slot",
                        className: "absolute left-[150px] top-[0px] w-[100px] h-[100px]"),
                });
        }

        // "b" mounts before "c" and unmounts, then "d" mounts over "c"'s rect, so "d" reuses the slot "b"
        // vacated in the context's droppable dictionary. The strategy records the ids it receives and
        // whether their attach orders strictly increase, which holds only if each candidate carries its own
        // order — a sort over equal orders would leave that false whatever sequence it returned.
        [Component]
        private static VNode ChurnedDroppablesScene()
        {
            var (stage, setStage) = Hooks.UseState(0);
            s_setStage = setStage;
            return V.DndContext(
                collisionDetection: (in DndCollisionQuery query) =>
                {
                    var increasing = true;
                    for (var i = 1; i < query.Droppables.Count; i++)
                    {
                        increasing &= query.Droppables[i - 1].Order < query.Droppables[i].Order;
                    }
                    s_receivedAttachOrder = string.Join(",", query.Droppables.Select(d => d.Id))
                        + (increasing ? " increasing" : " not increasing");
                    return DndCollisions.RectIntersection(in query);
                },
                activation: new DragActivation(Distance: 4f),
                className: "w-[300px] h-[300px]",
                name: "scope",
                children: new VNode[]
                {
                    V.Draggable("item", key: "item", name: "item",
                        className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                    stage == 0
                        ? V.Droppable("b", key: "b", name: "b",
                            className: "absolute left-[0px] top-[150px] w-[100px] h-[100px]")
                        : null,
                    V.Droppable("c", key: "c", name: "c",
                        className: "absolute left-[150px] top-[0px] w-[100px] h-[100px]"),
                    stage == 2
                        ? V.Droppable("d", key: "d", name: "d",
                            className: "absolute left-[150px] top-[0px] w-[100px] h-[100px]")
                        : null,
                });
        }

        [Test]
        public void Given_AStrategyRankingTwoDroppables_When_TheDragEnds_Then_TheFirstRankedEntryIsTheOverTarget()
        {
            // Arrange
            Mount(ReverseRankedScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Act — release with the pointer inside "first", which the strategy ranks second.
            SendPointerMove(item, new Vector2(190, 20));
            SendPointerUp(item, new Vector2(190, 20));

            // Assert
            Assert.That(s_ended, Is.EqualTo(new[] { "second" }));
        }

        [Test]
        public void Given_AStrategyRankingTwoDroppables_When_TheDragEnds_Then_DragEndArgsCarryTheFullRanking()
        {
            // Arrange
            Mount(ReverseRankedScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            SendPointerMove(item, new Vector2(190, 20));

            // Act
            SendPointerUp(item, new Vector2(190, 20));

            // Assert
            Assert.That(s_endedRankings, Is.EqualTo(new[] { "second,first" }));
        }

        [Test]
        public void Given_AStrategyRankingTwoDroppables_When_TheOverTargetChanges_Then_DragOverArgsCarryTheFullRanking()
        {
            // Arrange
            Mount(ReverseRankedScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));

            // Act
            SendPointerMove(item, new Vector2(20, 10));
            SendPointerMove(item, new Vector2(190, 20));

            // Assert
            Assert.That(s_overRankings, Is.EqualTo(new[] { "second,first" }));
        }

        [Test]
        public void Given_AStrategyReturningNull_When_ThePointerMovesAndReleases_Then_NoDropTargetIsReported()
        {
            // Arrange
            Mount(NullStrategyScene);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Act
            SendPointerMove(item, new Vector2(190, 20));
            SendPointerUp(item, new Vector2(190, 20));

            // Assert
            Assert.That(s_ended, Is.EqualTo(new object[] { null }));
        }

        [Test]
        public void Given_ADroppableMountedAfterAnotherUnmountedBeforeTheOtherTiedOne_When_TheDragMoves_Then_TheStrategyReceivesTheTiedPairInAttachOrder()
        {
            // Arrange — "c" and "d" share one rect and tie.
            Mount(ChurnedDroppablesScene);
            s_setStage.Invoke(1);
            _mounted.FlushStateForTest();
            s_setStage.Invoke(2);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var item = Q("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));

            // Act
            SendPointerMove(item, new Vector2(190, 20));

            // Assert
            Assert.That(s_receivedAttachOrder, Is.EqualTo("c,d increasing"));
        }

        #region Collision strategies (pure functions, no panel)

        private static DndCollisionQuery Query(Rect activeRect, Vector2 pointer, params DndDroppableRect[] droppables)
            => new(activeRect, pointer, new List<DndDroppableRect>(droppables));

        private static DndDroppableRect Candidate(string id, Rect rect) => new(id, rect, null);

        private static string Ids(DndCollision[] ranked)
            => string.Join(",", ranked.Select(collision => collision.Id));

        // The round-trip format, so a value is compared whole rather than at display precision.
        private static string Values(DndCollision[] ranked)
            => string.Join(",", ranked.Select(collision =>
                collision.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

        // The active rect shared by the ranking cases: 10x10 at the origin, center (5,5).
        private static readonly Rect s_active = new(0, 0, 10, 10);

        [Test]
        public void Given_CandidatesRegisteredOutOfOrder_When_RectIntersectionRuns_Then_TheyAreRankedByIntersectionRatioNotOverlapArea()
        {
            // Arrange — "big" and "high" both overlap by 100, at ratios 0.01 and 1; "mid" and "low" are
            // 0.6667 and 0.3333, and "apart" does not overlap at all.
            var query = Query(s_active, Vector2.zero,
                Candidate("low", new Rect(5, 0, 10, 10)),
                Candidate("high", new Rect(0, 0, 10, 10)),
                Candidate("apart", new Rect(20, 0, 10, 10)),
                Candidate("big", new Rect(0, 0, 100, 100)),
                Candidate("mid", new Rect(2, 0, 10, 10)));

            // Act
            var ranked = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("high,mid,low,big"));
        }

        [Test]
        public void Given_CandidatesThatOnlyTouchOrAreSeparatedFromTheActiveRect_When_RectIntersectionRuns_Then_NoCollisionIsReported()
        {
            // Arrange — "edge" shares the right edge, "corner" only the bottom-right corner; "right" is apart
            // on x only, "below" on y only, "diagonal" on both.
            var query = Query(s_active, Vector2.zero,
                Candidate("edge", new Rect(10, 0, 10, 10)),
                Candidate("corner", new Rect(10, 10, 10, 10)),
                Candidate("right", new Rect(20, 0, 10, 10)),
                Candidate("below", new Rect(0, 20, 10, 10)),
                Candidate("diagonal", new Rect(20, 20, 10, 10)));

            // Act
            var ranked = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo(""));
        }

        [Test]
        public void Given_ARatioOfExactlyAFifthDecimalHalf_When_RectIntersectionRuns_Then_ItRoundsUpLikeToFixed()
        {
            // Arrange — the overlap is 400 of a union of 12800, exactly 1/32, a representable half; (1/32).toFixed(4) is "0.0313".
            var query = Query(new Rect(0, 0, 100, 100), Vector2.zero,
                Candidate("half", new Rect(80, 80, 40, 80)));

            // Act
            var ranked = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(Values(ranked), Is.EqualTo("0.0313"));
        }

        [Test]
        public void Given_ARatioWhoseDoubleSitsJustBelowAFifthDecimalHalf_When_RectIntersectionRuns_Then_ItRoundsDownLikeToFixed()
        {
            // Arrange — the overlap is 7 of a union of 20000, so the ratio is the double nearest 0.00035, which lies below the half; (7 / 20000).toFixed(4) is "0.0003".
            var query = Query(new Rect(0, 0, 100, 100), Vector2.zero,
                Candidate("strip", new Rect(93, 99, 10007, 1)));

            // Act
            var ranked = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(Values(ranked), Is.EqualTo("0.0003"));
        }

        [Test]
        public void Given_TwoRatiosThatRoundToTheSameFourDecimals_When_RectIntersectionRuns_Then_RegistrationOrderIsKept()
        {
            // Arrange — "a" is exactly 1/3 and "b" about 0.333336, so only the rounding ties them and keeps "a" ahead of the greater unrounded "b".
            var query = Query(s_active, Vector2.zero,
                Candidate("a", new Rect(5, 0, 10, 10)),
                Candidate("b", new Rect(5, 0, 9.9999f, 10)));

            // Act
            var ranked = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("a,b"));
        }

        [Test]
        public void Given_CandidatesAtDifferentCenterDistances_When_ClosestCenterRuns_Then_EveryCandidateIsRankedNearestFirst()
        {
            // Arrange — the centers are 200, 5 and 30 from (5,5); "unlaid" has NaN edges and sits between the
            // first two, where a NaN value would hold the ranking apart.
            var query = Query(s_active, Vector2.zero,
                Candidate("far", new Rect(0, 200, 10, 10)),
                Candidate("unlaid", new Rect(float.NaN, float.NaN, float.NaN, float.NaN)),
                Candidate("near", new Rect(3, 4, 10, 10)),
                Candidate("mid", new Rect(30, 0, 10, 10)));

            // Act
            var ranked = DndCollisions.ClosestCenter(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("near,mid,far"));
        }

        [Test]
        public void Given_TwoCandidatesAtTheSameCenterDistanceAndANearerOneRegisteredLast_When_ClosestCenterRuns_Then_TheTiedPairKeepsRegistrationOrder()
        {
            // Arrange — centers (25,5) and (-15,5) are both 20 from (5,5), "near" 5 away.
            var query = Query(s_active, Vector2.zero,
                Candidate("right", new Rect(20, 0, 10, 10)),
                Candidate("left", new Rect(-20, 0, 10, 10)),
                Candidate("near", new Rect(3, 4, 10, 10)));

            // Act
            var ranked = DndCollisions.ClosestCenter(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("near,right,left"));
        }

        [Test]
        public void Given_ALargerCandidateWithItsCenterOffsetEquallyOnEachAxis_When_ClosestCenterRuns_Then_TheValueIsTheUnroundedEuclideanDistance()
        {
            // Arrange — its center (11,11) is 6 from (5,5) on each axis, though its corner is 1 from the origin.
            var query = Query(s_active, Vector2.zero,
                Candidate("diag", new Rect(1, 1, 20, 20)));

            // Act
            var ranked = DndCollisions.ClosestCenter(in query);

            // Assert
            Assert.That(Values(ranked), Is.EqualTo("8.48528137423857"));
        }

        [Test]
        public void Given_ACenteredLargeRectAndANearerSmallRect_When_ClosestCornersRuns_Then_TheRectWithNearerCornersRanksFirst()
        {
            // Arrange — "wide" shares the active center, but its corners are 63.6396 away on average and
            // "small"'s 20; "unlaid" has NaN edges and sits between them, where a NaN value would hold the
            // ranking apart.
            var query = Query(s_active, Vector2.zero,
                Candidate("wide", new Rect(-45, -45, 100, 100)),
                Candidate("unlaid", new Rect(0, 0, float.NaN, float.NaN)),
                Candidate("small", new Rect(20, 0, 10, 10)));

            // Act
            var ranked = DndCollisions.ClosestCorners(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("small,wide"));
        }

        [Test]
        public void Given_CandidatesAtDifferentCornerDistances_When_ClosestCornersRuns_Then_ValuesAreMeanCornerDistancesPairedByCornerAndRounded()
        {
            // Arrange — corners pair as top-left, top-right, bottom-left, bottom-right: "big" is (0 + 90 + 90 + 127.2792) / 4 away, "below" 50 on each pair and "right" 30 on each.
            var query = Query(s_active, Vector2.zero,
                Candidate("big", new Rect(0, 0, 100, 100)),
                Candidate("below", new Rect(0, 50, 10, 10)),
                Candidate("right", new Rect(30, 0, 10, 10)));

            // Act
            var ranked = DndCollisions.ClosestCorners(in query);

            // Assert
            Assert.That(Values(ranked), Is.EqualTo("30,50,76.8198"));
        }

        [Test]
        public void Given_TwoRectsOneFloatStepApartInX_When_ClosestCornersRuns_Then_TheirRoundedMeanDistancesTieAndRegistrationOrderIsKept()
        {
            // Arrange — "x" sits one float step left of "y". Computed in double, as dnd-kit computes, their
            // mean corner distances round to the same four decimals.
            var query = Query(new Rect(-398.061767578125f, -135.78981018066406f, 635.7106323242188f, 875.8890380859375f), Vector2.zero,
                Candidate("y", new Rect(862.5891723632812f, 1045.541015625f, 148.20156860351562f, 39.06645202636719f)),
                Candidate("x", new Rect(862.589111328125f, 1045.541015625f, 148.20156860351562f, 39.06645202636719f)));

            // Act
            var ranked = DndCollisions.ClosestCorners(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("y,x"));
        }

        [Test]
        public void Given_ContainingRectsOfDifferentAreasRegisteredOutOfOrder_When_PointerWithinRuns_Then_TheyAreRankedByMeanCornerDistanceNotArea()
        {
            // Arrange — the pointer is the center of every rect but "strip", which has a smaller area than "outer" (2400 against 10000) but far corners.
            var query = Query(s_active, new Vector2(50, 50),
                Candidate("c", new Rect(-50, -50, 200, 200)),
                Candidate("strip", new Rect(45, 45, 200, 12)),
                Candidate("outer", new Rect(0, 0, 100, 100)),
                Candidate("a", new Rect(40, 40, 20, 20)),
                Candidate("b", new Rect(25, 25, 50, 50)));

            // Act
            var ranked = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("a,b,outer,strip,c"));
        }

        [Test]
        public void Given_ARectWithTheSmallerAreaButFarCorners_When_PointerWithinRuns_Then_ValuesAreMeanCornerDistancesRoundedToFourDecimals()
        {
            // Arrange — "outer" has mean corner distance 70.7107 and "strip" 101.4658; by area "strip" would come first.
            var query = Query(s_active, new Vector2(50, 50),
                Candidate("strip", new Rect(45, 45, 200, 12)),
                Candidate("outer", new Rect(0, 0, 100, 100)));

            // Act
            var ranked = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(Values(ranked), Is.EqualTo("70.7107,101.4658"));
        }

        [Test]
        public void Given_APointerOnTheTopLeftCorner_When_PointerWithinRuns_Then_TheRectCollides()
        {
            // Arrange
            var query = Query(s_active, new Vector2(0, 0),
                Candidate("a", new Rect(0, 0, 10, 10)));

            // Act
            var ranked = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("a"));
        }

        [Test]
        public void Given_APointerOnTheBottomRightCorner_When_PointerWithinRuns_Then_TheRectCollides()
        {
            // Arrange
            var query = Query(s_active, new Vector2(10, 10),
                Candidate("a", new Rect(0, 0, 10, 10)));

            // Act
            var ranked = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo("a"));
        }

        [Test]
        public void Given_APointerOutsideEachRectOnExactlyOneSide_When_PointerWithinRuns_Then_NoCollisionIsReported()
        {
            // Arrange — pointer (50,5): "xBefore" ends left of it and "xAfter" starts right of it, both spanning its y; "yBefore" and "yAfter" bracket it on x but sit off its y.
            var query = Query(s_active, new Vector2(50, 5),
                Candidate("xBefore", new Rect(0, 0, 10, 10)),
                Candidate("xAfter", new Rect(60, 0, 10, 10)),
                Candidate("yAfter", new Rect(40, 100, 20, 10)),
                Candidate("yBefore", new Rect(40, -100, 20, 10)));

            // Act
            var ranked = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(Ids(ranked), Is.EqualTo(""));
        }

        [Test]
        public void Given_ACandidateCarryingData_When_EachStrategyRuns_Then_EachCollisionCarriesThatData()
        {
            // Arrange
            var data = new object();
            var query = Query(s_active, new Vector2(5, 5), new DndDroppableRect("a", new Rect(0, 0, 10, 10), data));

            // Act
            var carried = new[]
            {
                DndCollisions.RectIntersection(in query)[0].Data,
                DndCollisions.ClosestCenter(in query)[0].Data,
                DndCollisions.ClosestCorners(in query)[0].Data,
                DndCollisions.PointerWithin(in query)[0].Data,
            };

            // Assert
            Assert.That(carried, Has.All.SameAs(data));
        }

        [Test]
        public void Given_ACandidateOfInfiniteWidth_When_ClosestCornersRuns_Then_ItsValueStaysInfinite()
        {
            // Arrange
            var query = Query(s_active, Vector2.zero, Candidate("inf", new Rect(0, 0, float.PositiveInfinity, 10)));

            // Act
            var ranked = DndCollisions.ClosestCorners(in query);

            // Assert
            Assert.That(ranked[0].Value, Is.EqualTo(double.PositiveInfinity));
        }

        #endregion
    }
}
