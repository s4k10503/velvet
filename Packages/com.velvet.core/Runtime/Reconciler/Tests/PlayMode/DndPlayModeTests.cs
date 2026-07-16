#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the drag-and-drop behaviors that only a real runtime panel with the engine's own pointer
    /// dispatcher can prove: captured pointer events route to the capturing source regardless of where
    /// they land (so a drag keeps tracking outside the source's bounds), a child that captures at its
    /// own pointer-down blacks out distance activation (interactive capturing children are documented
    /// non-drag zones) while its click stays intact, a real drag ending on a Clickable source does NOT
    /// fire its click, and the DragOverlay positioner tracks the pointer on the Overlay layer panel with
    /// picking disabled.
    /// </summary>
    internal sealed class DndPlayModeTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        private static readonly List<string> s_started = new();
        private static readonly List<string> s_ended = new();

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_started.Clear();
            s_ended.Clear();
            _panelGo = new GameObject("DndPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        private VisualElement Main(string name)
            => _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>(name);

        // Thin aliases over the SHARED pointer senders (TestUtilities) — one implementation for the
        // EditMode and PlayMode suites, so the event shape (IMGUI-event construction keeping
        // PointerDeviceState truthful) cannot drift between them.
        private static void SendPointerDown(VisualElement target, Vector2 position)
            => target.SendPointerDownEvent(position);

        private static void SendPointerMove(VisualElement target, Vector2 position)
            => target.SendPointerMoveEvent(position);

        private static void SendPointerUp(VisualElement target, Vector2 position)
            => target.SendPointerUpEvent(position);

        private static void SendPointerMoveUntargeted(IPanel panel, Vector2 position)
            => panel.SendPointerMoveUntargeted(position);

        [Component]
        private static VNode PlainScene() => V.DndContext(
            onDragStart: e => s_started.Add(e.Active.Id),
            onDragEnd: e => s_ended.Add(e.Over?.Id),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("item", name: "item",
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
            });

        [UnityTest]
        public IEnumerator Given_AnActiveDragHoldingPointerCapture_When_MovesDispatchToTheRoot_Then_TheDragStillTracksTheDelta()
        {
            // Arrange
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(PlainScene, key: "root"));
            yield return null;
            yield return null;
            var item = Main("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(s_started, Is.Not.Empty, "Precondition: the drag activated");

            // Act — dispatch the next move with NO preset target, at a position far outside the
            // source's bounds: the engine's own capture routing must deliver it to the capturing source.
            SendPointerMoveUntargeted(item.panel, new Vector2(220, 110));
            yield return null;

            // Assert — delta from the activation point (20,10) is (200,100).
            var translate = item.style.translate.value;
            Assert.That((translate.x.value, translate.y.value), Is.EqualTo((200f, 100f)));
        }

        [Component]
        private static VNode ClickableChildScene() => V.DndContext(
            onDragStart: e => s_started.Add(e.Active.Id),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("card", name: "card",
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]",
                    children: new VNode[]
                    {
                        V.Button(name: "child", className: "w-[60px] h-[30px]"),
                    }),
            });

        [UnityTest]
        public IEnumerator Given_APressOnAClickableChildInsideADraggable_When_TheChildCapturesThePointer_Then_DistanceActivationNeverFires()
        {
            // Arrange — the engine ground truth: captured pointer events are delivered to the capturing
            // element ONLY, so the pending phase's panel-root observers never see these moves. An
            // interactive capturing child is a documented non-drag zone in distance mode.
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(ClickableChildScene, key: "root"));
            yield return null;
            yield return null;
            var child = Main("child");

            // Act — a press on the Button (whose Clickable captures at pointer-down), then travel far
            // past the activation distance.
            SendPointerDown(child, new Vector2(10, 10));
            SendPointerMove(child, new Vector2(60, 10));
            yield return null;

            // Assert
            Assert.That(s_started, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Given_APressOnAClickableChildInsideADraggable_When_ItReleasesInPlace_Then_TheChildsClickStillFires()
        {
            // Arrange
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(ClickableChildScene, key: "root"));
            yield return null;
            yield return null;
            var child = (Button)Main("child");
            var clicks = 0;
            child.clicked += () => clicks++;

            // Act
            SendPointerDown(child, new Vector2(10, 10));
            SendPointerUp(child, new Vector2(10, 10));
            yield return null;

            // Assert — the draggable ancestor's arming must not cost the child its click.
            Assert.That(clicks, Is.EqualTo(1));
        }

        [Component]
        private static VNode DraggableButtonScene() => V.DndContext(
            onDragStart: e => s_started.Add(e.Active.Id),
            onDragEnd: e => s_ended.Add(e.Over?.Id),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                // V.Button has no props: parameter (its DSL surfaces text/enabled/tooltip only), so the
                // draggable-Button shape is declared as a raw ElementNode carrying the Draggable slot —
                // the props form any element type supports.
                new ElementNode
                {
                    Key = "btn",
                    ElementType = typeof(Button),
                    Name = "btn",
                    ClassNames = V.ParseClassNames("absolute left-[0px] top-[0px] w-[80px] h-[40px]"),
                    Props = new FiberElementProps { Draggable = new DraggableSettings("btn") },
                    Children = System.Array.Empty<VNode>(),
                    Events = System.Array.Empty<FiberEventBinding>(),
                },
            });

        [UnityTest]
        public IEnumerator Given_ADraggableClickableButton_When_ARealDragEndsOnIt_Then_TheClickDoesNotFire()
        {
            // Arrange — the Draggable settings sit ON the Clickable's own element, so captured events
            // reach the session's callbacks; the post-drag PointerUp must be swallowed before the
            // Clickable's bubble handler turns it into a click.
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(DraggableButtonScene, key: "root"));
            yield return null;
            yield return null;
            var btn = (Button)Main("btn");
            var clicks = 0;
            btn.clicked += () => clicks++;

            // Act — a real drag: press, travel past the constraint, release.
            SendPointerDown(btn, new Vector2(10, 10));
            SendPointerMove(btn, new Vector2(40, 10));
            SendPointerUp(btn, new Vector2(40, 10));
            yield return null;

            // Assert — the drag completed (OnDragEnd fired) and the click did not.
            Assert.That((s_ended.Count, clicks), Is.EqualTo((1, 0)));
        }

        [Component]
        private static VNode OverlayScene() => V.DndContext(
            onDragStart: e => s_started.Add(e.Active.Id),
            className: "w-[300px] h-[300px]",
            children: new VNode[]
            {
                V.Draggable("item", name: "item", movement: DragMovement.None,
                    className: "absolute left-[0px] top-[0px] w-[50px] h-[50px]"),
                V.DragOverlay(key: "overlay", children: new VNode[]
                {
                    V.Label("ghost", name: "ghost"),
                }),
            });

        private static VisualElement FindPositioner(VisualElement root)
        {
            // The positioner is the overlay host child carrying PickingMode.Ignore + absolute position
            // (forced at Attach) — resolved structurally so the test does not depend on internal names.
            var count = root.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = root.hierarchy[i];
                if (child.pickingMode == PickingMode.Ignore)
                {
                    return child;
                }
                var nested = FindPositioner(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        [UnityTest]
        public IEnumerator Given_AnActiveDragWithADragOverlay_When_ThePointerMoves_Then_TheOverlayPositionerTracksItOnTheOverlayPanel()
        {
            // Arrange
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(OverlayScene, key: "root"));
            yield return null;
            yield return null;
            var item = Main("item");
            SendPointerDown(item, new Vector2(10, 10));
            SendPointerMove(item, new Vector2(20, 10));
            Assume.That(s_started, Is.Not.Empty, "Precondition: the drag activated");

            // Act
            SendPointerMove(item, new Vector2(120, 60));
            yield return null;

            // Assert — grab offset was (10,10) at the down... measured from the ACTIVATION point
            // (20,10) against the source rect at (0,0): offset (20,10). The positioner's top-left is
            // pointer − grabOffset = (100, 50) in the (identically-scaled) overlay panel space.
            var positioner = FindPositioner(
                _mounted.Root.Reconciler.Context.LayerHosts[UILayer.Overlay].Document.rootVisualElement);
            Assert.That((positioner.style.left.value.value, positioner.style.top.value.value),
                Is.EqualTo((100f, 50f)));
        }

        [UnityTest]
        public IEnumerator Given_ADragOverlay_When_ItMounts_Then_ItsPositionerNeverInterceptsPicking()
        {
            // Arrange & Act
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(OverlayScene, key: "root"));
            yield return null;
            yield return null;

            // Assert
            var positioner = FindPositioner(
                _mounted.Root.Reconciler.Context.LayerHosts[UILayer.Overlay].Document.rootVisualElement);
            Assert.That(positioner.pickingMode, Is.EqualTo(PickingMode.Ignore));
        }
    }
}
#endif
