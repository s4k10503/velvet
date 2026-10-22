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

        private sealed class Binding
        {
            public readonly VisualElement Overlay;
            public Action<MeshGenerationContext, float, float> Paint = null!;
            public EventCallback<GeometryChangedEvent>? OnGeometry;
            public EventCallback<AttachToPanelEvent>? OnAttach;
            public IVisualElementScheduledItem? StateTick;

            public Binding(VisualElement overlay) => Overlay = overlay;
        }

        private static readonly ConditionalWeakTable<VisualElement, Binding> s_byElement = new();

        public static bool Has(VisualElement element) => s_byElement.TryGetValue(element, out _);

        // Paints element's border with paint, which draws in the element's own box, given its laid-out width and
        // height, from now on.
        public static void Ensure(VisualElement element, Action<MeshGenerationContext, float, float> paint)
        {
            if (s_byElement.TryGetValue(element, out var bound))
            {
                bound.Paint = paint;
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
            var binding = new Binding(overlay) { Paint = paint };
            overlay.generateVisualContent += mgc =>
            {
                var w = element.layout.width;
                var h = element.layout.height;
                if (w > 0f && h > 0f)
                {
                    binding.Paint(mgc, w, h);
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

        // Ensures the overlay where want, else removes it, and repaints the element.
        public static void Sync(VisualElement element, bool want, Action<MeshGenerationContext, float, float> paint)
        {
            if (want)
            {
                Ensure(element, paint);
            }
            else
            {
                Remove(element);
            }
            element.MarkDirtyRepaint();
        }

        public static void Remove(VisualElement element)
        {
            if (!s_byElement.TryGetValue(element, out var binding))
            {
                return;
            }
            element.UnregisterCallback(binding.OnGeometry);
            element.UnregisterCallback(binding.OnAttach);
            binding.StateTick?.Pause();
            binding.Overlay.RemoveFromHierarchy();
            s_byElement.Remove(element);
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
            if (host.IndexOf(binding.Overlay) != host.IndexOf(element) + 1)
            {
                binding.Overlay.RemoveFromHierarchy();
                host.Insert(host.IndexOf(element) + 1, binding.Overlay);
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
            style.transformOrigin = rs.transformOrigin;
            style.translate = new Translate(rs.translate.x, rs.translate.y, rs.translate.z);
            style.scale = rs.scale;
            style.rotate = rs.rotate;
        }
    }
}
