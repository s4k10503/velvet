using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Resolves which element's width answers an element's width-driven variants, which are two separate
    // questions with separate answers:
    //
    // - sm:/md:/…/2xl: ask the VIEWPORT: the panel root, or for a portal host's panel the root ViewportRoot
    //   names, unless an ancestor carries VelvetResponsive.ViewportClass, which stands in for the panel
    //   (ResolveWidthSource).
    // - @md:/@max-lg/main:/… ask a CONTAINER: the nearest ancestor marked @container, or for a named query the
    //   nearest one marked @container/<name>, and nothing when there is none (ResolveContainer).
    //
    // Mechanism mirrors group-/peer- relational sources: a marker is a plain utility class that lands on the
    // element's class list (it is not a variant, so the patcher adds it verbatim), found by an ancestor class
    // walk — no ReconcilerContext tracking, userData, or extra manipulator needed.
    //
    // A width-driven manipulator resolves its source when it attaches, and again whenever a reconcile toggles
    // a marker on any element (ScopesChanged): the walk finds the nearest marked ancestor at that moment, so a
    // descendant attached before the toggle re-points just as one attached after it would.
    internal static class StyleResponsiveScope
    {
        // The class that marks an element as a container for @-prefixed variants, named or not. Spelled like
        // the CSS container-query at-rule so it is unambiguous (an app would not use it as an incidental
        // layout class) and reads as the container-query behavior it provides.
        internal const string ContainerClass = VelvetResponsive.ContainerClass;

        // `@container/<name>`: the container a `@md/<name>:` variant asks for.
        private const string NamedContainerPrefix = ContainerClass + "/";

        internal const string ViewportClass = VelvetResponsive.ViewportClass;

        // The element whose width should drive sm:/md:/… for descendants of target: the nearest ancestor
        // carrying ViewportClass, or ViewportRoot(panelRoot) when none is marked.
        // panelRoot is typically panel.visualTree; passing it in keeps this independent of how the caller
        // obtained the panel (AttachToPanelEvent.destinationPanel vs target.panel). Reuses the shared ancestor
        // class walk so this resolution and the group-/peer- relational resolution stay one implementation.
        internal static VisualElement? ResolveWidthSource(VisualElement target, VisualElement? panelRoot)
            => StyleRelationalVariantManipulator.FindAncestorWithClass(target, ViewportClass) ?? ViewportRoot(panelRoot);

        // The container a query named name asks for, looking up from target's parent: the nearest ancestor
        // marked with the matching container class, or for an unnamed query the nearest marked with any. Null
        // when there is none, in which case the query matches nothing.
        internal static VisualElement? ResolveContainer(VisualElement target, string name)
        {
            if (name.Length > 0)
            {
                return StyleRelationalVariantManipulator.FindAncestorWithClass(target, NamedContainerPrefix + name);
            }

            for (var ancestor = target.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (IsContainer(ancestor))
                {
                    return ancestor;
                }
            }
            return null;
        }

        private static bool IsContainer(VisualElement element)
        {
            foreach (var cls in element.GetClasses())
            {
                if (IsContainerClass(cls))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsContainerClass(string cls)
            => cls == ContainerClass || cls.StartsWith(NamedContainerPrefix, StringComparison.Ordinal);

        // A framework host panel — a layer or world-space portal's — keyed by its panel root to the root of the
        // panel its portal was declared on, which PanelHostFactory records when it creates the host.
        private static readonly ConditionalWeakTable<VisualElement, VisualElement> s_declaringRoots = new();

        internal static void RecordDeclaringRoot(VisualElement hostRoot, VisualElement declaringRoot)
            => s_declaringRoots.AddOrUpdate(hostRoot, declaringRoot);

        // A page has one viewport, and a portal's children answer its media queries like the rest of the page,
        // so a host panel's unscoped breakpoints follow the panel its portal was declared on, through every host
        // that portal was itself declared inside.
        private static VisualElement? ViewportRoot(VisualElement? panelRoot)
            => panelRoot == null ? null : DeclaringRootOrSelf(panelRoot);

        private static VisualElement DeclaringRootOrSelf(VisualElement root)
            => s_declaringRoots.TryGetValue(root, out var declaring) ? DeclaringRootOrSelf(declaring) : root;

        // Carries no element, so a handler re-resolves whatever was toggled rather than filtering to the toggled
        // element's descendants: a toggle is rare, and such a filter would be a second ancestor walk that has to
        // agree with the one above.
        internal static event Action? ScopesChanged;

        // Points source at target's viewport source as target's ancestors now stand. A target off a panel is
        // left alone: its source was unhooked when it detached, and it resolves again when it attaches.
        internal static void Rebind(VisualElement? target, ResponsiveWidthSource? source)
        {
            if (target?.panel == null)
            {
                return;
            }
            source?.Hook(ResolveWidthSource(target, target.panel.visualTree));
        }

        // Raised by the class diff, which is where a className gains or loses a marker, or swaps one container
        // name for another.
        internal static void OnClassesChanged(string[] oldClasses, string[] newClasses)
        {
            if (!MarkersKept(oldClasses, newClasses) || !MarkersKept(newClasses, oldClasses))
            {
                ScopesChanged?.Invoke();
            }
        }

        private static readonly ConditionalWeakTable<VisualElement, string> s_seenMarkers = new();
        private static readonly List<string> s_scratchMarkers = new();

        // Compares the markers on element's live class list with the ones the last observation saw, which is how a
        // marker written straight to the class list (AddToClassList("@container")) re-points descendants: no event
        // announces it, so the element's next reconcile does.
        internal static void ObserveMarkers(VisualElement element)
        {
            if (ScopesChanged == null)
            {
                return;
            }
            s_scratchMarkers.Clear();
            foreach (var cls in element.GetClasses())
            {
                if (IsMarker(cls))
                {
                    s_scratchMarkers.Add(cls);
                }
            }
            s_scratchMarkers.Sort(StringComparer.Ordinal);
            var signature = s_scratchMarkers.Count == 0 ? string.Empty : string.Join(" ", s_scratchMarkers);
            s_seenMarkers.TryGetValue(element, out var seen);
            if ((seen ?? string.Empty) == signature)
            {
                return;
            }
            if (signature.Length == 0)
            {
                s_seenMarkers.Remove(element);
            }
            else
            {
                s_seenMarkers.AddOrUpdate(element, signature);
            }
            ScopesChanged?.Invoke();
        }

        // Whether every marker class in from is also in other.
        private static bool MarkersKept(string[] from, string[] other)
        {
            foreach (var cls in from)
            {
                if (IsMarker(cls) && Array.IndexOf(other, cls) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsMarker(string cls) => cls == ViewportClass || IsContainerClass(cls);
    }
}
