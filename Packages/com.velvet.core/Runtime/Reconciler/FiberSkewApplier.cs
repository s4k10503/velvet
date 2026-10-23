using UnityEngine.UIElements;

namespace Velvet
{
    // The skew-* utilities. A skewed element takes the skew as its inline rotate and scale slots, composed with the
    // rotate and scale its classes and writers give it (AuthoredTransform), which UI Toolkit draws, picks and lays its
    // descendants out under as the shear it stands for.
    internal sealed class FiberSkewApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberSkewApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point: writes the transform when classNames resolves to an active skew.
        internal void ApplySkewOnCreate(VisualElement element, string[] classNames)
        {
            if (StyleSkewClass.TryExtract(classNames, out var spec))
            {
                WriteTransform(element, spec);
            }
        }

        // Patch-time reconciliation of an element's skew against its new class list: write the transform for a newly
        // skewed element or one whose winning skew classes changed, compose it again when the other classes did, and
        // give the slots back for one whose skew classes were removed. Runs AFTER SyncClassDrivenStyling so the
        // composite observes this patch's freshly-applied styling (the inline slots are shared with the
        // arbitrary-value resolver). Always the zero spec: the shear is the transform's, which the shadow and
        // the borders are drawn under.
        // classesChanged comes from the caller because the answer depends on which class SOURCE this pass
        // was given: a variant re-sync hands over the same reconciled array both times and the change lives
        // entirely in what the live class list added, which a comparison here could not see.
        internal SkewSpec ApplySkewOnPatch(VisualElement element, string[] newClassNames, bool classesChanged)
        {
            if (_ctx.SkewDriven.ContainsKey(element))
            {
                SettleDriven(element, newClassNames, classesChanged);
                return default;
            }
            var transformed = _ctx.SkewTransformed.TryGetValue(element, out var owner);
            var has = StyleSkewClass.TryGetWinningSkewClasses(newClassNames, out _, out _);
            if (!has && !transformed)
            {
                return default;
            }
            SkewSpec spec = default;
            if (!has || !StyleSkewClass.TryExtract(newClassNames, out spec))
            {
                if (transformed)
                {
                    ClearTransform(element);
                }
            }
            else if (!transformed || owner != (spec.SourceX, spec.SourceY))
            {
                WriteTransform(element, spec);
            }
            else if (classesChanged)
            {
                AuthoredTransform.Engage(element, spec.XDeg, spec.YDeg);
            }
            return default;
        }

        // A play is drawing the angles; the classes only say where it lands.
        private void SettleDriven(VisualElement element, string[] classNames, bool classesChanged)
        {
            StyleSkewClass.TryExtract(classNames, out var landing);
            _ctx.SkewDriven[element] = (landing.XDeg, landing.YDeg);
            _ctx.SkewTransformed[element] = (landing.SourceX, landing.SourceY);
            if (classesChanged)
            {
                AuthoredTransform.Resync(element);
            }
        }

        // Draws a motion play's frame: the angle on each axis, null for an axis the play does not move, which shows
        // the angle the element's classes resolve to. An element that rests unskewed takes the transform for the
        // length of the play, which EndDrive gives back.
        internal void Drive(VisualElement element, float? xDeg, float? yDeg)
        {
            if (!_ctx.SkewDriven.TryGetValue(element, out var resting))
            {
                AuthoredTransform.TryGetSkew(element, out var restingX, out var restingY);
                resting = (restingX, restingY);
                _ctx.SkewDriven[element] = resting;
                _ctx.SkewTransformed.TryAdd(element, (null, null));
            }
            AuthoredTransform.Engage(element, xDeg ?? resting.X, yDeg ?? resting.Y);
        }

        // Ends the play's hold on the angles. A play that landed on its own target passes it, null for an axis it
        // does not move; one cut short passes none and the element shows what its classes resolve to.
        internal void EndDrive(VisualElement element, float? landedXDeg, float? landedYDeg)
        {
            if (!_ctx.SkewDriven.Remove(element, out var resting))
            {
                return;
            }
            var x = landedXDeg ?? resting.X;
            var y = landedYDeg ?? resting.Y;
            if (x == 0f && y == 0f)
            {
                ClearTransform(element);
                return;
            }
            AuthoredTransform.Engage(element, x, y);
        }

        private void WriteTransform(VisualElement element, SkewSpec spec)
        {
            _ctx.SkewTransformed[element] = (spec.SourceX, spec.SourceY);
            AuthoredTransform.Engage(element, spec.XDeg, spec.YDeg);
            // The shape the element is picked by (SkewPickRegion); any other element type picks by its rectangle.
            if (element is ISkewPickTarget target)
            {
                target.PickRegion ??= new SkewPickRegion();
            }
        }

        private void ClearTransform(VisualElement element)
        {
            _ctx.SkewTransformed.Remove(element);
            AuthoredTransform.Release(element);
            if (element is ISkewPickTarget target)
            {
                target.PickRegion = null;
            }
        }
    }
}
