using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor.Preview
{
    /// <summary>
    /// Non-interactive inspection overlay for the preview stage — the "outline" + "measure" addons.
    /// It draws above the mounted story but never intercepts pointer events (<see cref="PickingMode.Ignore"/>),
    /// so the live story keeps full hover/click behaviour underneath.
    /// <list type="bullet">
    /// <item><b>Outline</b>: a 1px stroke around every element in the story subtree.</item>
    /// <item><b>Measure</b>: the box model (content + padding + margin bands, with px labels) of the deepest
    /// element under the pointer.</item>
    /// </list>
    /// Each element's box is read in the preview panel's coordinates and mapped through the canvas that shows
    /// the panel, whose world transform carries the zoom, into this overlay's local space, so outlines and
    /// boxes stay aligned at any zoom.
    /// </summary>
    internal sealed class PreviewInspectOverlay : VisualElement
    {
        private static readonly Color OutlineColor = new(0.30f, 0.80f, 1f, 0.55f);
        private static readonly Color ContentColor = new(0.36f, 0.66f, 1f, 0.35f);
        private static readonly Color PaddingColor = new(0.45f, 0.85f, 0.45f, 0.35f);
        private static readonly Color MarginColor = new(0.95f, 0.65f, 0.30f, 0.30f);
        private const float LabelFontSize = 10f;

        // The canvas showing the preview panel; the panel root's descendants are what gets outlined / measured.
        private readonly VisualElement _canvas;
        private readonly PreviewPanelBridge _bridge;
        private readonly Label _label;

        private bool _outline;
        private bool _measure;
        private VisualElement _measured;

        public PreviewInspectOverlay(VisualElement canvas, PreviewPanelBridge bridge)
        {
            _canvas = canvas;
            _bridge = bridge;

            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            // The whole point: draw on top without ever stealing a pointer event from the live story.
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;

            _label = new Label
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    fontSize = LabelFontSize,
                    color = Color.white,
                    backgroundColor = new Color(0f, 0f, 0f, 0.78f),
                    paddingLeft = 4f, paddingRight = 4f, paddingTop = 1f, paddingBottom = 1f,
                    display = DisplayStyle.None,
                },
            };
            Add(_label);

            // Listen for pointer movement over the canvas so measure can track the element under the cursor. The
            // handlers are named (not lambdas) so they can be unregistered on detach — see OnDetachFromPanel.
            _canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _canvas.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        private void OnPointerLeave(PointerLeaveEvent evt) => ClearMeasured();

        // Symmetric teardown: drop the callbacks this overlay registered on the (separate) canvas element so
        // they do not outlive the overlay when the window closes / the panel detaches.
        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            _canvas.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            _canvas.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        /// <summary>Whether the outline pass is drawn.</summary>
        public bool OutlineEnabled
        {
            get => _outline;
            set
            {
                if (_outline == value) return;
                _outline = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Whether the measure box-model is drawn for the hovered element.</summary>
        public bool MeasureEnabled
        {
            get => _measure;
            set
            {
                if (_measure == value) return;
                _measure = value;
                if (!_measure) ClearMeasured();
                MarkDirtyRepaint();
            }
        }

        /// <summary>Re-draws the overlay; call after a (re)mount or a stage/canvas geometry change so the
        /// outline tracks the new subtree and the new layout.</summary>
        public void Refresh()
        {
            UpdateLabel();
            MarkDirtyRepaint();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_measure) return;
            // Picked on the preview panel at the pointer's position there. Kept only when it is part of the story
            // subtree.
            var root = _bridge.Root;
            var picked = root?.panel?.Pick(_bridge.ToPanel(evt.localPosition));
            var next = IsInStory(root, picked) ? picked : null;
            // Only repaint when the hovered element actually changes; a pointer move within one element would
            // otherwise repaint the whole overlay every frame for no visual change.
            if (ReferenceEquals(next, _measured)) return;
            _measured = next;
            Refresh();
        }

        private void ClearMeasured()
        {
            if (_measured == null && _label.style.display == DisplayStyle.None) return;
            _measured = null;
            _label.style.display = DisplayStyle.None;
            MarkDirtyRepaint();
        }

        private static bool IsInStory(VisualElement root, VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e == root) return element != root; // the root frame itself is not a useful pick
            }

            return false;
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var root = _bridge.Root;
            if (root == null) return;

            if (_outline)
            {
                foreach (var element in Descendants(root))
                {
                    StrokeRect(ctx, ToLocalRect(element.worldBound), OutlineColor);
                }
            }

            if (_measure && _measured != null)
            {
                DrawMeasure(ctx, _measured);
            }
        }

        // Draws the hovered element's box model: margin band (outermost), then the bordered box, then the
        // content rect inset by padding. The label is not touched here: a panel does not take a change that dirties
        // an element while it generates visual content, so UpdateLabel writes it.
        private void DrawMeasure(MeshGenerationContext ctx, VisualElement element)
        {
            var (_, border, marginRect, contentRect) = Boxes(element);

            FillBand(ctx, marginRect, border, MarginColor);     // margin ring
            FillRect(ctx, border, PaddingColor);                 // padding box (under content)
            FillRect(ctx, contentRect, ContentColor);            // content rect
            StrokeRect(ctx, border, OutlineColor);
        }

        // The content rect in the panel's coordinates, and the border, margin and content rects in this overlay's.
        private (Rect PanelContent, Rect Border, Rect Margin, Rect Content) Boxes(VisualElement element)
        {
            var rs = element.resolvedStyle;
            var panelBorder = element.worldBound; // worldBound is the border box
            var panelContent = Rect.MinMaxRect(
                panelBorder.xMin + rs.paddingLeft, panelBorder.yMin + rs.paddingTop,
                panelBorder.xMax - rs.paddingRight, panelBorder.yMax - rs.paddingBottom);

            var border = ToLocalRect(panelBorder);
            var marginRect = ToLocalRect(Rect.MinMaxRect(
                panelBorder.xMin - rs.marginLeft, panelBorder.yMin - rs.marginTop,
                panelBorder.xMax + rs.marginRight, panelBorder.yMax + rs.marginBottom));
            var contentRect = ToLocalRect(panelContent);
            return (panelContent, border, marginRect, contentRect);
        }

        // Labels the measured element's content size and reports its padding and margin in the panel's units, above
        // its margin box or just below it if that would clip off the top.
        private void UpdateLabel()
        {
            if (!_measure || _measured == null || _bridge.Root == null)
            {
                _label.style.display = DisplayStyle.None;
                return;
            }

            var rs = _measured.resolvedStyle;
            var (panelContent, _, marginRect, _) = Boxes(_measured);
            var w = Mathf.RoundToInt(panelContent.width);
            var h = Mathf.RoundToInt(panelContent.height);
            var pad = $"p {Px(rs.paddingTop)} {Px(rs.paddingRight)} {Px(rs.paddingBottom)} {Px(rs.paddingLeft)}";
            var mar = $"m {Px(rs.marginTop)} {Px(rs.marginRight)} {Px(rs.marginBottom)} {Px(rs.marginLeft)}";
            _label.text = $"{w}×{h}   {pad}   {mar}";
            _label.style.display = DisplayStyle.Flex;
            _label.style.left = marginRect.xMin;
            var top = marginRect.yMin - LabelFontSize - 6f;
            _label.style.top = top < 0f ? marginRect.yMax + 2f : top;
        }

        private static int Px(float v) => Mathf.RoundToInt(v);

        // Converts a rect in the preview panel's coordinates into this overlay's local coordinates by mapping
        // its two corners through the canvas; the overlay itself is unscaled, so this yields screen-aligned
        // pixels at any zoom.
        private Rect ToLocalRect(Rect panelRect)
        {
            var world = _canvas.LocalToWorld(_bridge.ToCanvas(panelRect));
            var min = this.WorldToLocal(new Vector2(world.xMin, world.yMin));
            var max = this.WorldToLocal(new Vector2(world.xMax, world.yMax));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static IEnumerable<VisualElement> Descendants(VisualElement root)
        {
            foreach (var child in root.Children())
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        #region Mesh helpers
        private static void StrokeRect(MeshGenerationContext ctx, Rect r, Color color)
        {
            const float t = 1f;
            FillRect(ctx, new Rect(r.xMin, r.yMin, r.width, t), color);            // top
            FillRect(ctx, new Rect(r.xMin, r.yMax - t, r.width, t), color);        // bottom
            FillRect(ctx, new Rect(r.xMin, r.yMin, t, r.height), color);           // left
            FillRect(ctx, new Rect(r.xMax - t, r.yMin, t, r.height), color);       // right
        }

        // Fills the ring between an outer and inner rect (the four sides), used for the margin band.
        private static void FillBand(MeshGenerationContext ctx, Rect outer, Rect inner, Color color)
        {
            FillRect(ctx, Rect.MinMaxRect(outer.xMin, outer.yMin, outer.xMax, inner.yMin), color);  // top
            FillRect(ctx, Rect.MinMaxRect(outer.xMin, inner.yMax, outer.xMax, outer.yMax), color);  // bottom
            FillRect(ctx, Rect.MinMaxRect(outer.xMin, inner.yMin, inner.xMin, inner.yMax), color);  // left
            FillRect(ctx, Rect.MinMaxRect(inner.xMax, inner.yMin, outer.xMax, inner.yMax), color);  // right
        }

        private static void FillRect(MeshGenerationContext ctx, Rect r, Color color)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            var mesh = ctx.Allocate(4, 6);
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = color });
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(1);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(3);
        }
        #endregion
    }
}
