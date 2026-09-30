using UnityEngine.UIElements;

namespace Velvet
{
    // Reads the computed font size of an element declaring an em or percentage leading-[…], which CSS
    // computes the line-height from, and re-resolves the text under it when that size changes.
    //
    // UI Toolkit's style traversal sends CustomStyleResolvedEvent to an element it has just restyled,
    // after SetComputedStyle and before layout, when the element holds a custom property and listens for
    // the event (VisualTreeStyleUpdaterTraversal). A class change, or an inline font-size write, on the
    // element or an ancestor restyles the element: both raise a StyleSheet version change, which that
    // traversal propagates to the children. The marker class's rule in _typography.uss supplies the custom
    // property. GeometryChangedEvent is the fallback for a panel without the bundled sheet, where the
    // event never arrives.
    internal sealed class LeadingLengthProbe
    {
        internal const string MarkerClass = "velvet-leading-length";

        private readonly ReconcilerContext _ctx;
        private readonly VisualElement _element;
        private readonly EventCallback<CustomStyleResolvedEvent> _onStyleResolved;
        private readonly EventCallback<GeometryChangedEvent> _onGeometryChanged;

        // NaN until UI Toolkit has resolved the element's style.
        public float FontSize { get; private set; } = float.NaN;

        private LeadingLengthProbe(ReconcilerContext ctx, VisualElement element)
        {
            _ctx = ctx;
            _element = element;
            _onStyleResolved = _ => Refresh();
            _onGeometryChanged = _ => Refresh();
        }

        public static void Sync(ReconcilerContext ctx, VisualElement element, bool wanted)
        {
            ctx.LeadingLengthProbes.TryGetValue(element, out var probe);
            if (wanted && probe == null)
            {
                probe = new LeadingLengthProbe(ctx, element);
                ctx.LeadingLengthProbes[element] = probe;
                element.AddToClassList(MarkerClass);
                element.RegisterCallback(probe._onStyleResolved);
                element.RegisterCallback(probe._onGeometryChanged);
                return;
            }
            if (!wanted && probe != null)
            {
                Detach(ctx, element, probe);
            }
        }

        public static void Detach(ReconcilerContext ctx, VisualElement element, LeadingLengthProbe probe)
        {
            // MUTANT_SURVIVES(equivalent, line removed): a callback left registered only re-resolves text
            // the resolver then resolves to the same string and white-space.
            element.UnregisterCallback(probe._onStyleResolved);
            // MUTANT_SURVIVES(equivalent, line removed): the geometry callback re-resolves the same way.
            element.UnregisterCallback(probe._onGeometryChanged);
            element.RemoveFromClassList(MarkerClass);
            ctx.LeadingLengthProbes.Remove(element);
        }

        private void Refresh()
        {
            var size = _element.resolvedStyle.fontSize;
            if (size == FontSize)
            {
                return;
            }
            FontSize = size;
            StyleTextEffectResolver.Reapply(_ctx, _element);
        }
    }
}
