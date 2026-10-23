using UnityEngine.UIElements;

namespace Velvet
{
    // Reads the computed font size of an element declaring an em or percentage leading-[…], which CSS
    // computes the line-height from, and re-resolves the text under it when that size changes. For a
    // leading-[…] that reads more than that size (LeadingUnit.Measured) it measures the expression on the
    // element as the other relative lengths are, and a viewport unit in it is read again on a scheduler
    // tick, since the panel moving raises no event on the element.
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

        // The tick StyleAnimateDriver and AnchoredDriver poll at.
        private const long TickMs = 16;

        private StyleLengthExpression? _expression;
        private IVisualElementScheduledItem? _tick;
        private float _measured = float.NaN;

        // NaN until UI Toolkit has resolved the element's style.
        public float FontSize { get; private set; } = float.NaN;

        // What the declared expression comes to now, or false while it cannot be measured.
        public bool TryMeasure(out float px)
        {
            px = 0f;
            return _expression != null && StyleRelativeLengths.TryMeasureLineHeight(_element, _expression, out px);
        }

        private LeadingLengthProbe(ReconcilerContext ctx, VisualElement element)
        {
            _ctx = ctx;
            _element = element;
            _onStyleResolved = _ => Refresh();
            _onGeometryChanged = _ => Refresh();
        }

        public static void Sync(ReconcilerContext ctx, VisualElement element, LeadingValue? leading)
        {
            var wanted = leading?.Unit is LeadingUnit.EmLength or LeadingUnit.Measured;
            ctx.LeadingLengthProbes.TryGetValue(element, out var probe);
            if (wanted && probe == null)
            {
                probe = new LeadingLengthProbe(ctx, element);
                ctx.LeadingLengthProbes[element] = probe;
                element.AddToClassList(MarkerClass);
                element.RegisterCallback(probe._onStyleResolved);
                element.RegisterCallback(probe._onGeometryChanged);
            }
            if (wanted)
            {
                probe!.Declare(leading!.Value.Expression);
                return;
            }
            if (probe != null)
            {
                Detach(ctx, element, probe);
            }
        }

        // Takes the expression the element now declares, if any, so that the next change is measured against it
        // and a viewport unit in it is polled.
        private void Declare(StyleLengthExpression? expression)
        {
            _expression = expression;
            _measured = TryMeasure(out var px) ? px : float.NaN;
            var polled = expression != null && expression.ReadsViewport;
            if (polled && _tick == null)
            {
                _tick = _element.schedule.Execute(Refresh).Every(TickMs);
            }
            else if (!polled && _tick != null)
            {
                _tick.Pause();
                _tick = null;
            }
        }

        public static void Detach(ReconcilerContext ctx, VisualElement element, LeadingLengthProbe probe)
        {
            // MUTANT_SURVIVES(equivalent, line removed): a callback left registered only re-resolves text
            // the resolver then resolves to the same string and white-space.
            element.UnregisterCallback(probe._onStyleResolved);
            // MUTANT_SURVIVES(equivalent, line removed): the geometry callback re-resolves the same way.
            element.UnregisterCallback(probe._onGeometryChanged);
            probe._tick?.Pause();
            probe._tick = null;
            element.RemoveFromClassList(MarkerClass);
            ctx.LeadingLengthProbes.Remove(element);
        }

        private void Refresh()
        {
            var size = _element.resolvedStyle.fontSize;
            var measured = TryMeasure(out var px) ? px : float.NaN;
            if (size == FontSize && (measured == _measured || (float.IsNaN(measured) && float.IsNaN(_measured))))
            {
                return;
            }
            FontSize = size;
            _measured = measured;
            StyleTextEffectResolver.Reapply(_ctx, _element);
        }
    }
}
