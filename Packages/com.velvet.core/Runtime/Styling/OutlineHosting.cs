using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Where an outline's overlay lives. CSS paints an outline after all the content of its stacking context and
    // clips it only by its own ancestors' overflow, so the overlay is a trailing child of the stacking context's
    // root — the nearest ancestor that composites, transforms, hides or z-stacks what it holds — and each
    // ancestor between that clips is stood in for by a clip proxy: an element over that ancestor's padding box
    // that clips as it does. Outlines sharing a root are kept in their elements' tree order.
    internal static class OutlineHosting
    {
        // Marks a clip proxy or an outline overlay at the top of a host, so the reconciler's child accounting
        // (SilhouetteBoundsSpacer.IsSpacer, through RingOverlay.MarkerClass) and the outline ordering both see it.
        internal const string ProxyName = "velvet-outline-clip";

        private static readonly Func<VisualElement, bool>? s_shouldClip = ShouldClipDelegate();

        // The element each top-level outline node belongs to, for the tree-order comparison between outlines.
        private static readonly ConditionalWeakTable<VisualElement, VisualElement> s_owner = new();

        // Each panel's hierarchy version reader. The version moves when any element of the panel is added, removed or
        // reordered, which can change an outline's host, the clips on its way there and its place among the host's
        // outlines; the styles that decide a host or a clip are read off the element's own ancestors (Unchanged).
        private static readonly ConditionalWeakTable<IPanel, Func<uint>?> s_hierarchyVersions = new();

        private static Func<VisualElement, bool>? ShouldClipDelegate()
        {
            try
            {
                return EngineMember.ElementShouldClip.ResolveMethod() is { } method
                    ? (Func<VisualElement, bool>)Delegate.CreateDelegate(typeof(Func<VisualElement, bool>), method)
                    : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static Func<uint>? HierarchyVersionOf(IPanel panel)
        {
            if (s_hierarchyVersions.TryGetValue(panel, out var reader))
            {
                return reader;
            }
            try
            {
                var getter = EngineMember.PanelHierarchyVersion.ResolveProperty()?.GetMethod;
                reader = getter != null && getter.DeclaringType!.IsInstanceOfType(panel)
                    ? (Func<uint>)Delegate.CreateDelegate(typeof(Func<uint>), panel, getter)
                    : null;
            }
            catch (ArgumentException)
            {
                reader = null;
            }
            s_hierarchyVersions.AddOrUpdate(panel, reader);
            return reader;
        }

        // Re-hosts and re-positions the outline unless nothing that decides either changed since it last did; force
        // skips that check, for a call that knows its own element changed. The check reads the panel's hierarchy
        // version and the element's own ancestors up to the host, allocating nothing, so a style change anywhere
        // else in the panel costs an outline nothing.
        internal static void Place(VisualElement element, RingBinding binding, bool force)
        {
            if (!force && (Unchanged(element, binding) || (binding.OutlineHost == null && binding.Spec.Outline.Width <= 0f)))
            {
                return;
            }
            Rehost(element, binding);
            // Read after the moves above, which move the version themselves.
            binding.OutlineVersion = element.panel is { } after ? HierarchyVersionOf(after)?.Invoke() : null;
        }

        // Whether the host, the clips and the boxes the last placement read still stand: the same hierarchy, the same
        // stacking root with no new one below it, the same clipping ancestors with the same boxes and masks, and the
        // element where it was.
        private static bool Unchanged(VisualElement element, RingBinding binding)
        {
            var host = binding.OutlineHost;
            if (host == null || binding.Spec.Outline.Width <= 0f || element.panel is not { } panel
                || HierarchyVersionOf(panel)?.Invoke() is not { } version || version != binding.OutlineVersion
                || !(host.parent == null || host.parent == panel.visualTree || IsStackingRoot(host)))
            {
                return false;
            }
            var clip = binding.OutlineClipOwners.Count - 1;
            var (x, y) = (0f, 0f);
            for (var a = element; a != host; a = a.parent)
            {
                if (a == null)
                {
                    return false;
                }
                if (a != element)
                {
                    if (IsStackingRoot(a))
                    {
                        return false;
                    }
                    if (Clips(a))
                    {
                        if (clip < 0 || binding.OutlineClipOwners[clip] != a || binding.OutlineClipBoxes[clip] != ClipBoxOf(a))
                        {
                            return false;
                        }
                        clip--;
                    }
                }
                x += a.layout.x;
                y += a.layout.y;
            }
            return clip < 0 && new Vector2(x, y) == binding.OutlineOffset
                && new Vector2(binding.Width, binding.Height) == binding.OutlineSize;
        }

        // What a clip proxy copies off a clipping ancestor, which a placement compares to know the proxy still stands
        // for it: its box in its parent, its size and its mask.
        private static (Rect Layout, object? Mask) ClipBoxOf(VisualElement c)
            => (c.layout, c.style.backgroundImage.value.vectorImage);

        internal static void Remove(RingBinding binding)
        {
            binding.OutlineOverlay.RemoveFromHierarchy();
            foreach (var proxy in binding.OutlineClips)
            {
                proxy.RemoveFromHierarchy();
            }
            binding.OutlineClips.Clear();
        }

        private static void Rehost(VisualElement element, RingBinding binding)
        {
            var host = binding.Spec.Outline.Width > 0f && element.parent != null ? Host(element) : null;
            binding.OutlineHost = host;
            if (host == null)
            {
                Remove(binding);
                return;
            }

            // The clipping ancestors between the element and the host, outermost first.
            var clips = new List<VisualElement>();
            for (var a = element.parent; a != null && a != host; a = a.parent)
            {
                if (Clips(a))
                {
                    clips.Insert(0, a);
                }
            }

            SyncProxies(binding, clips.Count);
            binding.OutlineClipOwners.Clear();
            binding.OutlineClipOwners.AddRange(clips);
            binding.OutlineClipBoxes.Clear();
            foreach (var c in clips)
            {
                binding.OutlineClipBoxes.Add(ClipBoxOf(c));
            }
            var top = clips.Count > 0 ? binding.OutlineClips[0] : binding.OutlineOverlay;
            var chain = binding.OutlineOverlay;
            for (var i = clips.Count - 1; i >= 0; i--)
            {
                var proxy = binding.OutlineClips[i];
                if (chain.parent != proxy)
                {
                    proxy.Add(chain);
                }
                chain = proxy;
            }
            s_owner.AddOrUpdate(top, element);
            PlaceInTreeOrder(host, top, element);

            // Positions: each node over its stand-in's box, relative to its parent node's padding box.
            if (!TryOffsetWithin(element, host, out var ex, out var ey) || binding.Width <= 0f)
            {
                binding.OutlineHost = null; // not yet laid out: the next tick places it
                return;
            }
            binding.OutlineOffset = new Vector2(ex + Sane(host.resolvedStyle.borderLeftWidth), ey + Sane(host.resolvedStyle.borderTopWidth));
            binding.OutlineSize = new Vector2(binding.Width, binding.Height);
            float px = 0f, py = 0f;
            for (var i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                if (!TryOffsetWithin(c, host, out var cx, out var cy))
                {
                    return;
                }
                var rs = c.resolvedStyle;
                cx += Sane(rs.borderLeftWidth);
                cy += Sane(rs.borderTopWidth);
                var w = c.layout.width - Sane(rs.borderLeftWidth) - Sane(rs.borderRightWidth);
                var h = c.layout.height - Sane(rs.borderTopWidth) - Sane(rs.borderBottomWidth);
                var proxy = binding.OutlineClips[i];
                SetBox(proxy, cx - px, cy - py, w, h);
                SetRadii(proxy, c);
                SetMask(proxy, c);
                (px, py) = (cx, cy);
            }
            SetBox(binding.OutlineOverlay, ex - px, ey - py, binding.Width, binding.Height);
        }

        // Grows or shrinks the proxy list to count, detaching the ones dropped.
        private static void SyncProxies(RingBinding binding, int count)
        {
            while (binding.OutlineClips.Count > count)
            {
                var last = binding.OutlineClips[binding.OutlineClips.Count - 1];
                last.RemoveFromHierarchy();
                binding.OutlineClips.RemoveAt(binding.OutlineClips.Count - 1);
            }
            while (binding.OutlineClips.Count < count)
            {
                var proxy = new VisualElement
                {
                    name = ProxyName,
                    pickingMode = PickingMode.Ignore,
                    style = { position = Position.Absolute, overflow = Overflow.Hidden },
                };
                proxy.AddToClassList(RingOverlay.MarkerClass);
                proxy.AddToClassList(RingOverlay.OutlineMarkerClass);
                binding.OutlineClips.Add(proxy);
            }
            if (count > 0 && binding.OutlineOverlay.parent != null && !binding.OutlineClips.Contains(binding.OutlineOverlay.parent))
            {
                binding.OutlineOverlay.RemoveFromHierarchy();
            }
            for (var i = 1; i < count; i++)
            {
                if (binding.OutlineClips[i].parent != binding.OutlineClips[i - 1])
                {
                    binding.OutlineClips[i].RemoveFromHierarchy();
                }
            }
        }

        // The stacking context's root: the nearest ancestor that composites, transforms, hides or z-stacks what it
        // holds; failing one, the topmost ancestor under the panel's root.
        internal static VisualElement? Host(VisualElement element)
        {
            var candidate = element.parent;
            while (candidate != null)
            {
                var up = candidate.parent;
                if (up == null || up == candidate.panel?.visualTree || IsStackingRoot(candidate))
                {
                    return candidate;
                }
                candidate = up;
            }
            return null;
        }

        private static bool IsStackingRoot(VisualElement e)
        {
            var rs = e.resolvedStyle;
            // Velvet writes filters inline and its sheets declare none, so the inline list is the one read.
            var filter = e.style.filter;
            return rs.display == DisplayStyle.None || rs.opacity < 1f || rs.translate != Vector3.zero
                || rs.scale.value != Vector3.one || rs.rotate.angle.value != 0f
                || (filter.keyword == StyleKeyword.Undefined && filter.value is { Count: > 0 })
                || FiberZLayerCoordinator.IsRelocatedReal(e);
        }

        // Whether e clips its children, as UI Toolkit decides it; an inline overflow: hidden where the engine member
        // does not resolve.
        internal static bool Clips(VisualElement e)
            => s_shouldClip?.Invoke(e)
                ?? (e.style.overflow.keyword == StyleKeyword.Undefined && e.style.overflow.value == Overflow.Hidden);

        // top after every child of host that is not an outline node, and among the outline nodes after those whose
        // elements come before element in tree order and before those whose elements come after it.
        private static void PlaceInTreeOrder(VisualElement host, VisualElement top, VisualElement element)
        {
            var index = host.childCount;
            for (var i = host.childCount - 1; i >= 0; i--)
            {
                var child = host[i];
                if (child == top)
                {
                    continue;
                }
                if (!child.ClassListContains(RingOverlay.OutlineMarkerClass)
                    || !s_owner.TryGetValue(child, out var owner) || CompareTreeOrder(owner, element) < 0)
                {
                    break;
                }
                index = i;
            }
            var current = top.parent == host ? host.IndexOf(top) : -1;
            var target = current >= 0 && current < index ? index - 1 : index;
            if (current != target)
            {
                top.RemoveFromHierarchy();
                host.Insert(Math.Min(target, host.childCount), top);
            }
        }

        // Negative where a comes before b in a depth-first walk of their tree.
        internal static int CompareTreeOrder(VisualElement a, VisualElement b)
        {
            if (a == b)
            {
                return 0;
            }
            var pathA = PathOf(a);
            var pathB = PathOf(b);
            var i = 0;
            while (i < pathA.Count && i < pathB.Count && pathA[i] == pathB[i])
            {
                i++;
            }
            if (i == pathA.Count)
            {
                return -1; // a is an ancestor of b
            }
            if (i == pathB.Count)
            {
                return 1;
            }
            var parent = pathA[i].parent;
            return parent == null ? 0 : parent.IndexOf(pathA[i]).CompareTo(parent.IndexOf(pathB[i]));
        }

        private static List<VisualElement> PathOf(VisualElement e)
        {
            var path = new List<VisualElement>();
            for (var a = e; a != null; a = a.parent)
            {
                path.Insert(0, a);
            }
            return path;
        }

        // Where element's border box sits in host's padding box: its layout offsets up to host, each relative to its
        // parent's border box, less host's border. False where a layout is not known yet.
        internal static bool TryOffsetWithin(VisualElement element, VisualElement host, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            for (var e = element; e != host && e != null; e = e.parent)
            {
                var layout = e.layout;
                if (float.IsNaN(layout.x) || float.IsNaN(layout.y))
                {
                    return false;
                }
                x += layout.x;
                y += layout.y;
            }
            x -= Sane(host.resolvedStyle.borderLeftWidth);
            y -= Sane(host.resolvedStyle.borderTopWidth);
            return true;
        }

        // Writes the box only where it changed, so a placement that changes nothing writes nothing.
        internal static void SetBox(VisualElement e, float x, float y, float width, float height)
        {
            width = Mathf.Max(0f, width);
            height = Mathf.Max(0f, height);
            var s = e.style;
            if (s.left.value.value != x || s.left.keyword != StyleKeyword.Undefined) s.left = x;
            if (s.top.value.value != y || s.top.keyword != StyleKeyword.Undefined) s.top = y;
            if (s.width.value.value != width || s.width.keyword != StyleKeyword.Undefined) s.width = width;
            if (s.height.value.value != height || s.height.keyword != StyleKeyword.Undefined) s.height = height;
        }

        // A clip-path-* wrapper clips by the shape its vector background carries (FiberClipPathApplier), which the
        // proxy carries too, positioned alike; any other clip has none.
        private static void SetMask(VisualElement proxy, VisualElement c)
        {
            var ps = proxy.style;
            if (!c.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass))
            {
                ps.backgroundImage = StyleKeyword.Null;
                return;
            }
            var cs = c.style;
            ps.backgroundImage = cs.backgroundImage;
            ps.backgroundRepeat = cs.backgroundRepeat;
            ps.backgroundPositionX = cs.backgroundPositionX;
            ps.backgroundPositionY = cs.backgroundPositionY;
            ps.backgroundSize = cs.backgroundSize;
        }

        // The clip's corners: the clipping ancestor's padding-box radii, its outer radius less the wider border at
        // the corner, which is what UI Toolkit's style can hold of an elliptical inner corner.
        private static void SetRadii(VisualElement proxy, VisualElement c)
        {
            var rs = c.resolvedStyle;
            var (l, t, r, b) = (Sane(rs.borderLeftWidth), Sane(rs.borderTopWidth), Sane(rs.borderRightWidth), Sane(rs.borderBottomWidth));
            proxy.style.borderTopLeftRadius = Mathf.Max(0f, Sane(rs.borderTopLeftRadius) - Mathf.Max(l, t));
            proxy.style.borderTopRightRadius = Mathf.Max(0f, Sane(rs.borderTopRightRadius) - Mathf.Max(r, t));
            proxy.style.borderBottomRightRadius = Mathf.Max(0f, Sane(rs.borderBottomRightRadius) - Mathf.Max(r, b));
            proxy.style.borderBottomLeftRadius = Mathf.Max(0f, Sane(rs.borderBottomLeftRadius) - Mathf.Max(l, b));
        }

        private static float Sane(float v) => float.IsNaN(v) ? 0f : v;
    }
}
