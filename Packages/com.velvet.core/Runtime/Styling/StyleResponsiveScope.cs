using System;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Resolves which element's width drives an element's responsive (sm:/md:/...) breakpoints — the CSS
    // container-query analog. An element marked with the MarkerClass becomes a "responsive root"
    // (container-type: inline-size): its descendants' breakpoints evaluate against ITS width instead of the
    // panel root's. Resolution walks up from the target to the nearest marked ancestor; with no marked
    // ancestor it returns the panel root, or for a portal host's panel the root ViewportRoot names.
    //
    // Mechanism mirrors group-/peer- relational sources: the marker is a plain utility class that lands on the
    // element's class list (it is not a variant, so the patcher adds it verbatim), found by an ancestor class
    // walk — no ReconcilerContext tracking, userData, or extra manipulator needed.
    //
    // A responsive manipulator resolves its width source when it attaches, and again whenever a reconcile
    // toggles MarkerClass on any element (ScopesChanged): the walk finds the nearest marked ancestor at that
    // moment, so a descendant attached before the toggle re-points just as one attached after it would.
    internal static class StyleResponsiveScope
    {
        // The class that marks an element as a responsive scope. Spelled like the CSS container-query
        // at-rule so it is unambiguous (an app would not use it as an incidental layout class) and reads as the
        // container-query behavior it provides.
        internal const string MarkerClass = VelvetResponsive.ContainerClass;

        // The element whose width should drive responsive breakpoints for descendants of target: the nearest
        // ancestor carrying MarkerClass, or ViewportRoot(panelRoot) when none is marked.
        // panelRoot is typically panel.visualTree; passing it in keeps this independent of how the caller
        // obtained the panel (AttachToPanelEvent.destinationPanel vs target.panel). Reuses the shared ancestor
        // class walk so this resolution and the group-/peer- relational resolution stay one implementation.
        internal static VisualElement? ResolveWidthSource(VisualElement target, VisualElement? panelRoot)
            => StyleRelationalVariantManipulator.FindAncestorWithClass(target, MarkerClass) ?? ViewportRoot(panelRoot);

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

        // Points source at target's width source as target's ancestors now stand. A target off a panel is
        // left alone: its source was unhooked when it detached, and it resolves again when it attaches.
        internal static void Rebind(VisualElement? target, ResponsiveWidthSource? source)
        {
            if (target?.panel == null)
            {
                return;
            }
            source?.Hook(ResolveWidthSource(target, target.panel.visualTree));
        }

        // Raised by the class diff, which is where a className gains or loses the marker.
        internal static void OnClassesChanged(string[] oldClasses, string[] newClasses)
        {
            if ((Array.IndexOf(oldClasses, MarkerClass) >= 0) != (Array.IndexOf(newClasses, MarkerClass) >= 0))
            {
                ScopesChanged?.Invoke();
            }
        }
    }
}
