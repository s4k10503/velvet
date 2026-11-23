using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Paints the border, and the face beneath it, of an element whose skew or shadow layer owns its face while the
    /// element clips its content, on a sibling overlay placed directly after the element and over its box, as
    /// <see cref="RingOverlay"/> places its band.
    /// </summary>
    /// <remarks>
    /// The element's own clip cuts what it paints itself at its padding box (styling-variants.md's hidden-overflow
    /// table), which its border lies outside, and CSS clips neither the border nor the background beneath it so. The
    /// overlay is outside the element's clip and inside every ancestor's. Painted after the element, it covers the
    /// element's content only where that content is clipped away, past the padding box.
    /// </remarks>
    internal static class FaceBorderOverlay
    {
        // Marks the overlay as a reconciler-invisible child of its host (SilhouetteBoundsSpacer.IsSpacer).
        internal const string MarkerClass = "velvet-face-border-overlay";

        // What an overlay's paints belong to. The face layer (a border or a shadow's face) paints under the divider
        // layer, as an element's own border does under the divider's band.
        internal enum Layer
        {
            Face,
            Divider,
        }

        private sealed class Binding
        {
            public readonly VisualElement Overlay;
            public Action<MeshGenerationContext, float, float>? FacePaint;
            public Action<MeshGenerationContext, float, float>? DividerPaint;
            public EventCallback<GeometryChangedEvent>? OnGeometry;
            public EventCallback<AttachToPanelEvent>? OnAttach;
            public IVisualElementScheduledItem? StateTick;

            public Binding(VisualElement overlay) => Overlay = overlay;

            public bool Holds(Layer layer) => (layer == Layer.Face ? FacePaint : DividerPaint) != null;

            public void Set(Layer layer, Action<MeshGenerationContext, float, float>? paint)
            {
                if (layer == Layer.Face)
                {
                    FacePaint = paint;
                }
                else
                {
                    DividerPaint = paint;
                }
            }
        }

        private static readonly ConditionalWeakTable<VisualElement, Binding> s_byElement = new();

        public static bool Has(VisualElement element, Layer layer = Layer.Face)
            => s_byElement.TryGetValue(element, out var bound) && bound.Holds(layer);

        // Whether element has an overlay at all, whichever layers it holds.
        public static bool HasOverlay(VisualElement element) => s_byElement.TryGetValue(element, out _);

        // Paints element's border with paint, which draws in the element's own box, given its laid-out width and
        // height, from now on, in layer's place among the layers the overlay holds.
        public static void Ensure(VisualElement element, Action<MeshGenerationContext, float, float> paint,
            Layer layer = Layer.Face)
        {
            if (s_byElement.TryGetValue(element, out var bound))
            {
                bound.Set(layer, paint);
                bound.Overlay.MarkDirtyRepaint();
                return;
            }
            var overlay = new VisualElement
            {
                name = MarkerClass,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute },
            };
            overlay.AddToClassList(MarkerClass);
            var binding = new Binding(overlay);
            binding.Set(layer, paint);
            overlay.generateVisualContent += mgc =>
            {
                var w = element.layout.width;
                var h = element.layout.height;
                if (w > 0f && h > 0f)
                {
                    binding.FacePaint?.Invoke(mgc, w, h);
                    binding.DividerPaint?.Invoke(mgc, w, h);
                }
            };
            binding.OnGeometry = _ => Place(element, binding);
            element.RegisterCallback(binding.OnGeometry);
            binding.OnAttach = _ => Place(element, binding);
            element.RegisterCallback(binding.OnAttach);
            // Each frame, so an animated transform, opacity or face colour reaches the overlay.
            binding.StateTick = element.schedule.Execute(() =>
            {
                SyncElementState(element, binding);
                binding.Overlay.MarkDirtyRepaint();
            }).Every(StyleAnimateDriver.TickMs);
            s_byElement.AddOrUpdate(element, binding);
            Place(element, binding);
        }

        // Whether element clips its content, and, where that disagrees with whether it holds layer, queues settle once
        // outside the paint that asks, since settling writes the hierarchy. pending is the caller's mark that a settle
        // is queued.
        public static bool ClipsAndSettle<T>(VisualElement element, ref bool pending, T state, Action<VisualElement, T> settle,
            Layer layer = Layer.Face)
        {
            var clips = StyleCascade.ClipsOverflow(element);
            if (clips != Has(element, layer) && !pending)
            {
                pending = true;
                Defer(element, state, settle);
            }
            return clips;
        }

        private static void Defer<T>(VisualElement element, T state, Action<VisualElement, T> settle)
            => element.schedule.Execute(() => settle(element, state));

        // Ensures the overlay where want, else removes it, and repaints the element.
        public static void Sync(VisualElement element, bool want, Action<MeshGenerationContext, float, float> paint,
            Layer layer = Layer.Face)
        {
            if (want)
            {
                Ensure(element, paint, layer);
            }
            else
            {
                Remove(element, layer);
            }
            element.MarkDirtyRepaint();
        }

        public static void Remove(VisualElement element, Layer layer = Layer.Face)
            => Remove(element, layer, deferHierarchy: false);

        // As Remove, but the overlay is hidden now and leaves the hierarchy on the host's next scheduler pass, so the
        // host's child count does not change inside the call. The divided child's teardown takes this one.
        public static void RemoveLater(VisualElement element, Layer layer = Layer.Face)
            => Remove(element, layer, deferHierarchy: true);

        // The overlay stays while another layer still holds a paint.
        private static void Remove(VisualElement element, Layer layer, bool deferHierarchy)
        {
            if (!s_byElement.TryGetValue(element, out var binding))
            {
                return;
            }
            binding.Set(layer, null);
            if (binding.FacePaint != null || binding.DividerPaint != null)
            {
                binding.Overlay.MarkDirtyRepaint();
                return;
            }
            element.UnregisterCallback(binding.OnGeometry);
            element.UnregisterCallback(binding.OnAttach);
            binding.StateTick?.Pause();
            s_byElement.Remove(element);
            var overlay = binding.Overlay;
            var host = overlay.parent;
            if (!deferHierarchy || host == null)
            {
                ChildListWrites.RemoveFromHierarchy(overlay);
            }
            else
            {
                overlay.style.display = DisplayStyle.None;
                host.schedule.Execute(() => ChildListWrites.RemoveFromHierarchy(overlay));
            }
            RingOverlay.Replace(element);
        }

        // Directly after the element, where RingOverlay places its band after this.
        private static void Place(VisualElement element, Binding binding)
        {
            var host = element.parent;
            if (host == null)
            {
                return;
            }
            if (ChildListWrites.PhysicalIndexOf(host, binding.Overlay) != ChildListWrites.PhysicalIndexOf(host, element) + 1)
            {
                ChildListWrites.RemoveFromHierarchy(binding.Overlay);
                ChildListWrites.Insert(host, ChildListWrites.PhysicalIndexOf(host, element) + 1, binding.Overlay);
                RingOverlay.Replace(element);
            }
            var style = binding.Overlay.style;
            var x = element.layout.x;
            var y = element.layout.y;
            // An absolute child's left and top resolve against the host's padding box (RingOverlay.SyncGeometry).
            var hostLeft = host.resolvedStyle.borderLeftWidth;
            var hostTop = host.resolvedStyle.borderTopWidth;
            style.left = (float.IsNaN(x) ? 0f : x) - (float.IsNaN(hostLeft) ? 0f : hostLeft);
            style.top = (float.IsNaN(y) ? 0f : y) - (float.IsNaN(hostTop) ? 0f : hostTop);
            style.width = element.layout.width;
            style.height = element.layout.height;
            SyncElementState(element, binding);
            // MUTANT_SURVIVES(equivalent, line removed): of what a placement writes, only the size changes what is painted.
            // That resize repaints the overlay in the same update, which FaceBorderOverlayTests'
            // Given_AnOverlaidElement_When_ItIsWidened_Then_TheSameUpdatePaintsItAtTheNewWidth holds.
            binding.Overlay.MarkDirtyRepaint();
        }

        // What the element's own subtree would give the overlay had it been inside: its opacity, visibility and
        // display, and its transform about the element's pivot (RingOverlay.SyncElementState).
        private static void SyncElementState(VisualElement element, Binding binding)
        {
            var rs = element.resolvedStyle;
            var style = binding.Overlay.style;
            style.opacity = rs.opacity;
            style.visibility = rs.visibility;
            style.display = rs.display;
            if (float.IsNaN(rs.width))
            {
                return;
            }
            style.transformOrigin = new TransformOrigin(rs.transformOrigin.x, rs.transformOrigin.y, rs.transformOrigin.z);
            style.translate = new Translate(rs.translate.x, rs.translate.y, rs.translate.z);
            style.scale = rs.scale;
            style.rotate = rs.rotate;
        }
    }
}
