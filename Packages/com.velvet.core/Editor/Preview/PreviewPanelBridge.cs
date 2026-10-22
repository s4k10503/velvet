using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor.Preview
{
    // Joins the canvas the window shows a VelvetPreviewPanel's texture on to that panel: positions map
    // between the two, and the pointer, wheel, keyboard and navigation events the canvas receives are sent
    // on to the panel, a pointer's at the mapped position. The panel is read through a delegate because the
    // window replaces it when the chosen PanelSettings change.
    internal sealed class PreviewPanelBridge
    {
        private static readonly Vector2 OffPanel = new(-1e6f, -1e6f);

        private readonly VisualElement _canvas;
        private readonly Func<VelvetPreviewPanel> _panel;

        public PreviewPanelBridge(VisualElement canvas, Func<VelvetPreviewPanel> panel)
        {
            _canvas = canvas;
            _panel = panel;
            _canvas.focusable = true;
            _canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _canvas.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            _canvas.RegisterCallback<WheelEvent>(OnWheel);
            _canvas.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _canvas.RegisterCallback<KeyUpEvent>(OnKeyUp);
            _canvas.RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            _canvas.RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit);
            _canvas.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
        }

        /// <summary>The root of the panel the canvas shows, or <c>null</c> while there is none.</summary>
        public VisualElement Root => _panel()?.Root;

        /// <summary>A point in the canvas's local space, in the panel's coordinates.</summary>
        public Vector2 ToPanel(Vector2 canvasLocal) => Vector2.Scale(canvasLocal, CanvasToPanel());

        /// <summary>A rect in the panel's coordinates, in the canvas's local space.</summary>
        public Rect ToCanvas(Rect panelRect)
        {
            var ratio = CanvasToPanel();
            return Rect.MinMaxRect(
                panelRect.xMin / ratio.x, panelRect.yMin / ratio.y,
                panelRect.xMax / ratio.x, panelRect.yMax / ratio.y);
        }

        private Vector2 CanvasToPanel()
        {
            var panelSize = Root?.panel?.visualTree.layout.size ?? Vector2.zero;
            var canvasSize = _canvas.layout.size;
            return new Vector2(Ratio(panelSize.x, canvasSize.x), Ratio(panelSize.y, canvasSize.y));
        }

        private static float Ratio(float panel, float canvas) =>
            panel > 0f && canvas > 0f && !float.IsNaN(panel) && !float.IsNaN(canvas) ? panel / canvas : 1f;

        private void OnPointerDown(PointerDownEvent evt)
        {
            _canvas.Focus();
            _canvas.CapturePointer(evt.pointerId);
            SendPointer(EventType.MouseDown, evt, ToPanel(evt.localPosition));
        }

        private void OnPointerMove(PointerMoveEvent evt) =>
            SendPointer(evt.pressedButtons == 0 ? EventType.MouseMove : EventType.MouseDrag, evt, ToPanel(evt.localPosition));

        private void OnPointerUp(PointerUpEvent evt)
        {
            SendPointer(EventType.MouseUp, evt, ToPanel(evt.localPosition));
            _canvas.ReleasePointer(evt.pointerId);
        }

        // The story's pointer leaves when the canvas's does.
        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (!_canvas.HasPointerCapture(evt.pointerId)) SendPointer(EventType.MouseMove, evt, OffPanel);
        }

        private void SendPointer(EventType type, IPointerEvent source, Vector2 position)
        {
            var imgui = new Event
            {
                type = type,
                mousePosition = position,
                button = source.button,
                clickCount = source.clickCount,
                modifiers = source.modifiers,
            };
            Send(type switch
            {
                EventType.MouseDown => PointerDownEvent.GetPooled(imgui),
                EventType.MouseUp => PointerUpEvent.GetPooled(imgui),
                _ => PointerMoveEvent.GetPooled(imgui),
            });
        }

        private void OnWheel(WheelEvent evt) =>
            Send(WheelEvent.GetPooled(new Event
            {
                type = EventType.ScrollWheel,
                mousePosition = ToPanel(evt.localMousePosition),
                delta = evt.delta,
                modifiers = evt.modifiers,
            }));

        private void OnKeyDown(KeyDownEvent evt) =>
            Send(KeyDownEvent.GetPooled(evt.character, evt.keyCode, evt.modifiers));

        private void OnKeyUp(KeyUpEvent evt) =>
            Send(KeyUpEvent.GetPooled(evt.character, evt.keyCode, evt.modifiers));

        // The window's own focus stays on the canvas, so the next key still reaches the story.
        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            _canvas.focusController?.IgnoreEvent(evt);
            evt.StopPropagation();
            Send(NavigationMoveEvent.GetPooled(evt.direction));
        }

        private void OnNavigationSubmit(NavigationSubmitEvent evt) => Send(NavigationSubmitEvent.GetPooled());

        private void OnNavigationCancel(NavigationCancelEvent evt) => Send(NavigationCancelEvent.GetPooled());

        private void Send(EventBase evt)
        {
            using (evt)
            {
                Root?.panel?.visualTree.SendEvent(evt);
            }
        }
    }
}
