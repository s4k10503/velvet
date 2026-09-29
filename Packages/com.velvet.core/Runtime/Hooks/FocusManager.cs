#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Options for a <see cref="FocusManager"/> move, React Aria's <c>FocusManagerOptions</c>.
    /// </summary>
    /// <param name="From">
    /// The element to move from, instead of the focused one. Read by <see cref="FocusManager.FocusNext"/> and
    /// <see cref="FocusManager.FocusPrevious"/> only.
    /// </param>
    /// <param name="Tabbable">Only elements with a non-negative <c>tabIndex</c>, rather than every focusable one.</param>
    /// <param name="Wrap">
    /// Past the scope's last element, continue from its first (and the reverse). Read by
    /// <see cref="FocusManager.FocusNext"/> and <see cref="FocusManager.FocusPrevious"/> only.
    /// </param>
    /// <param name="Accept">Only elements this returns true for.</param>
    public sealed record FocusManagerOptions(
        VisualElement? From = null,
        bool Tabbable = false,
        bool Wrap = false,
        Func<VisualElement, bool>? Accept = null);

    /// <summary>
    /// Moves focus among the focusable descendants of the focus scope around the component that called
    /// <c>Hooks.UseFocusManager</c>, in hierarchy order — React Aria's <c>FocusManager</c>. A field such as a
    /// <c>TextField</c> or a <c>Toggle</c> is one element. The scope is the
    /// nearest <c>V.FocusScope</c> or <c>FocusScope</c> element prop above the component, a portal's content
    /// reaching the scope its portal is declared in. Each method focuses the element it finds and returns it;
    /// it returns null, leaving focus where it is, when it finds none or the component is in no scope.
    /// </summary>
    public sealed class FocusManager
    {
        private readonly ComponentFiber _fiber;

        internal FocusManager(ComponentFiber fiber) => _fiber = fiber;

        /// <summary>Focuses the scope element after the focused one (or <see cref="FocusManagerOptions.From"/>), or the scope's first when focus is outside the scope.</summary>
        public VisualElement? FocusNext(FocusManagerOptions? options = null) => Step(options, forward: true);

        /// <summary>Focuses the scope element before the focused one (or <see cref="FocusManagerOptions.From"/>), or the scope's last when focus is outside the scope.</summary>
        public VisualElement? FocusPrevious(FocusManagerOptions? options = null) => Step(options, forward: false);

        /// <summary>Focuses the scope's first element.</summary>
        public VisualElement? FocusFirst(FocusManagerOptions? options = null) => Edge(options, first: true);

        /// <summary>Focuses the scope's last element.</summary>
        public VisualElement? FocusLast(FocusManagerOptions? options = null) => Edge(options, first: false);

        private VisualElement? Step(FocusManagerOptions? options, bool forward)
        {
            var root = FindScopeRoot();
            if (root == null)
            {
                return null;
            }
            // The panel reports a focused field as the field, the unit Collect keeps.
            var from = options?.From ?? root.panel?.focusController?.focusedElement as VisualElement;
            var order = new List<VisualElement>();
            Collect(root, order);
            var fromIndex = from == null ? -1 : order.IndexOf(from);
            VisualElement? found = null;
            if (forward)
            {
                for (var i = fromIndex + 1; i < order.Count && found == null; i++)
                {
                    found = Candidate(order[i], options);
                }
            }
            else
            {
                var start = fromIndex < 0 ? order.Count - 1 : fromIndex - 1;
                for (var i = start; i >= 0 && found == null; i--)
                {
                    found = Candidate(order[i], options);
                }
            }
            if (found == null && options is { Wrap: true })
            {
                found = FindEdge(order, options, first: forward);
            }
            found?.Focus();
            return found;
        }

        private VisualElement? Edge(FocusManagerOptions? options, bool first)
        {
            var root = FindScopeRoot();
            if (root == null)
            {
                return null;
            }
            var order = new List<VisualElement>();
            Collect(root, order);
            // React Aria's focusFirst and focusLast read neither From nor Wrap.
            var found = FindEdge(order, options, first);
            found?.Focus();
            return found;
        }

        private static VisualElement? FindEdge(List<VisualElement> order, FocusManagerOptions? options, bool first)
        {
            for (var n = 0; n < order.Count; n++)
            {
                if (Candidate(order[first ? n : order.Count - 1 - n], options) is { } found)
                {
                    return found;
                }
            }
            return null;
        }

        private static VisualElement? Candidate(VisualElement element, FocusManagerOptions? options)
        {
            // A field delegates its focus to its input, and is still the unit to land on.
            if (!element.canGrabFocus || (element.delegatesFocus && !IsCompositeRoot(element)))
            {
                return null;
            }
            if (options == null)
            {
                return element;
            }
            if (options.Tabbable && element.tabIndex < 0)
            {
                return null;
            }
            return options.Accept == null || options.Accept(element) ? element : null;
        }

        // A subtree under an element that is not displayed is skipped whole, as the engine's ring and React
        // Aria's isElementVisible skip it; canGrabFocus reads only the element's own display. A field's parts are
        // skipped too, so the field is one element.
        private static void Collect(VisualElement parent, List<VisualElement> order)
        {
            var count = parent.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = parent.hierarchy[i];
                if (child.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }
                order.Add(child);
                if (!IsCompositeRoot(child))
                {
                    Collect(child, order);
                }
            }
        }

        // isCompositeRoot is internal. A later engine that drops it leaves every field's parts reachable
        // rather than throwing.
        private static readonly PropertyInfo? s_isCompositeRoot =
            typeof(VisualElement).GetProperty("isCompositeRoot", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool IsCompositeRoot(VisualElement element)
            => s_isCompositeRoot?.GetValue(element) is true;

        private VisualElement? FindScopeRoot()
        {
            var ctx = _fiber.Reconciler?.Context;
            if (ctx == null)
            {
                return null;
            }
            for (var current = LogicalMountPoint(ctx); current != null; current = FiberFocusNavigator.LogicalParentOf(current))
            {
                if (ctx.FocusScopeBindings.ContainsKey(current))
                {
                    return current;
                }
            }
            return null;
        }

        // A component mounted straight into a portal's target stands at the portal's placeholder. A fiber below
        // the portal's own child can have lost that stamp (ComponentRegistry says when), so the walk goes on to
        // the fiber above it.
        private VisualElement? LogicalMountPoint(ReconcilerContext ctx)
        {
            for (var fiber = _fiber; fiber != null; fiber = fiber.Parent)
            {
                var mountPoint = fiber.MountPoint;
                if (mountPoint == null)
                {
                    continue;
                }
                if (!IsPortalTarget(mountPoint, ctx))
                {
                    return mountPoint;
                }
                if (fiber.OwningPortalPlaceholder != null)
                {
                    return fiber.OwningPortalPlaceholder;
                }
            }
            return null;
        }

        private static bool IsPortalTarget(VisualElement element, ReconcilerContext ctx)
        {
            foreach (var info in ctx.PortalState.Values)
            {
                if (ReferenceEquals(info.Target, element))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
