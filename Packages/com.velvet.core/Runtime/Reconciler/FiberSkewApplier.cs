using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The skew-* utilities. An element that carries no other rotate or scale source takes the skew as the
    // inline rotate and scale slots (SkewTransformFactor), which UI Toolkit draws, picks and lays its descendants
    // out under as the shear it stands for. One that does carry another (a rotate-* or scale-* class, a Motion)
    // keeps the older layer: a sheared silhouette drawn via the element's own generateVisualContent, no
    // structural element added, which also owns any bg-gradient-* on the element (SyncSkewGradient) since a
    // skewed face must paint the gradient on its sheared mesh rather than as an un-sheared background-image
    // rectangle.
    internal sealed class FiberSkewApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberSkewApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point: when classNames resolves to an active skew,
        // wires the sheared-silhouette painter onto the element (no structural wrapper — the
        // silhouette is the element's own generateVisualContent). The first color stash is
        // event-driven (the element is not attached / style-resolved yet at create time).
        internal void ApplySkewOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleSkewClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            if (CanUseTransform(element, classNames))
            {
                WriteTransform(element, spec);
                return;
            }
            var binding = SkewSilhouette.Attach(element, spec);
            _ctx.SkewBindings[element] = binding;
            SetPick(element, spec);
            // A skewed element OWNS any bg-gradient-* on it: the silhouette paints the gradient on a
            // sheared mesh (the rectangular background-image the non-skew path would set cannot follow the
            // shear). ApplyGradientOnCreate runs next and defers to this binding.
            SyncSkewGradient(element, binding, classNames);
            SkewSilhouette.SetWantSpacer(element, binding, WrapperInfrastructure.CarriesFilter(_ctx, element, classNames), classNames);
        }

        // Feeds the gradient resolved from the element's class list into a skew binding (or clears it), so
        // a skewed element paints bg-gradient-* on its sheared mesh. The non-skew gradient path defers to
        // the binding whenever the element is skewed, so the gradient renders exactly once.
        private static void SyncSkewGradient(VisualElement element, SkewBinding binding, string[] classNames)
        {
            GradientSpec gradient = default;
            var has = StyleGradientClass.HasGradientClass(classNames)
                && StyleGradientClass.TryExtract(classNames, out gradient);
            SkewSilhouette.SetGradient(element, binding, has, gradient);
        }

        // Patch-time reconciliation of an element's skew state against its new class list. Four
        // cases mirroring the clip/shadow layers: update the existing binding's spec, attach a
        // newly-skewed element, detach one whose skew classes were removed, or do nothing. Runs
        // AFTER SyncClassDrivenStyling so the stash sync observes this patch's freshly-applied
        // styling (the inline slot is shared with the arbitrary-value resolver). Returns the
        // resolved skew (zero on both axes = none) — the caller forwards it to ApplyShadowOnPatch so the
        // shadow follows the sheared silhouette without re-parsing the skew classes.
        // classesChanged comes from the caller because the answer depends on which class SOURCE this pass
        // was given: a variant re-sync hands over the same reconciled array both times and the change lives
        // entirely in what the live class list added, which a comparison here could not see.
        internal SkewSpec ApplySkewOnPatch(VisualElement element, string[] newClassNames, bool classesChanged)
        {
            var bound = _ctx.SkewBindings.TryGetValue(element, out var binding);
            var has = StyleSkewClass.TryGetWinningSkewClasses(newClassNames, out var winnerX, out var winnerY);
            var transformed = _ctx.SkewTransformed.ContainsKey(element);
            // Fast path: no skew anywhere near this element.
            if (!bound && !has && !transformed)
            {
                return default;
            }
            if (SettleTransform(element, newClassNames, has, transformed, bound))
            {
                return default;
            }
            bound = _ctx.SkewBindings.TryGetValue(element, out binding);

            // Steady state: the winning tokens are exactly what the live binding was built from —
            // skip the parse, but keep the color stash in sync with this patch's styling.
            if (bound && has && binding.Spec.SourceX == winnerX && binding.Spec.SourceY == winnerY)
            {
                SkewSilhouette.SyncStashOnPatch(element, binding, classesChanged);
                // Re-seat the descendant-shear child translate unconditionally (not gated on classesChanged): a
                // child add / remove / reorder leaves the caster's own skew tokens untouched, so the children
                // must still re-seat even when this element's class list did not change.
                SkewSilhouette.SyncChildTranslate(binding);
                // Re-resolve the gradient only when the class list changed (an unchanged list cannot
                // have changed the gradient); SetGradient is itself a no-op when the spec is unchanged.
                if (classesChanged)
                {
                    SyncSkewGradient(element, binding, newClassNames);
                    // A filter utility / animate-hue can appear or vanish without the skew tokens changing.
                    SkewSilhouette.SetWantSpacer(element, binding, WrapperInfrastructure.CarriesFilter(_ctx, element, newClassNames), newClassNames);
                }
                return binding.Spec;
            }

            SkewSpec spec = default;
            var want = has && StyleSkewClass.TryExtract(newClassNames, out spec);

            if (want && bound)
            {
                binding.Spec = spec;
                SetPick(element, spec);
                SkewSilhouette.SyncStashOnPatch(element, binding, classesChanged: true);
                // The manipulator reads Spec live, so the just-changed angle re-seats the children here.
                SkewSilhouette.SyncChildTranslate(binding);
                SyncSkewGradient(element, binding, newClassNames);
                SkewSilhouette.SetWantSpacer(element, binding, WrapperInfrastructure.CarriesFilter(_ctx, element, newClassNames), newClassNames);
                element.MarkDirtyRepaint();
                return spec;
            }
            if (want)
            {
                var fresh = SkewSilhouette.Attach(element, spec);
                _ctx.SkewBindings[element] = fresh;
                SetPick(element, spec);
                SkewSilhouette.SyncStashOnPatch(element, fresh, classesChanged: true);
                SyncSkewGradient(element, fresh, newClassNames);
                SkewSilhouette.SetWantSpacer(element, fresh, WrapperInfrastructure.CarriesFilter(_ctx, element, newClassNames), newClassNames);
                return spec;
            }
            if (bound)
            {
                SkewSilhouette.Detach(element, binding);
                _ctx.SkewBindings.Remove(element);
                ClearPick(element);
            }
            return default;
        }

        // Whether the element takes the skew as its transform slots: nothing else writes them, so nothing is lost
        // to the skew's inline value, and nothing it writes is lost to theirs.
        private bool CanUseTransform(VisualElement element, string[] classNames)
            => !_ctx.MotionNodes.ContainsKey(element) && !_ctx.ElementToLayoutId.ContainsKey(element)
                && !CarriesTransformSource(classNames);

        // A class that writes rotate or scale, whatever variant or modifier it sits behind, or a preset that animates
        // them.
        private static bool CarriesTransformSource(string[] classNames) => Array.Exists(classNames, IsTransformSource);

        private static bool IsTransformSource(string cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            var start = 0;
            var depth = 0;
            for (var i = 0; i < cls.Length; i++)
            {
                if (cls[i] == '[')
                {
                    depth++;
                }
                else if (cls[i] == ']')
                {
                    depth--;
                }
                else if (cls[i] == ':' && depth == 0)
                {
                    start = i + 1;
                }
            }
            var core = cls.AsSpan(start);
            while (core.Length > 0 && (core[0] == '!' || core[0] == '-'))
            {
                core = core.Slice(1);
            }
            return core.StartsWith("rotate".AsSpan(), StringComparison.Ordinal)
                || core.StartsWith("scale".AsSpan(), StringComparison.Ordinal)
                || core.StartsWith("animate".AsSpan(), StringComparison.Ordinal)
                || core.StartsWith("anim-".AsSpan(), StringComparison.Ordinal);
        }

        // Moves the element between the transform and the painted layer as its classes now call for. True where the
        // element takes the skew as a transform, so the painted layer has nothing to do.
        private bool SettleTransform(VisualElement element, string[] classNames, bool has, bool transformed, bool bound)
        {
            SkewSpec spec = default;
            var want = has && StyleSkewClass.TryExtract(classNames, out spec);
            var useTransform = want && CanUseTransform(element, classNames);
            if (useTransform)
            {
                if (bound)
                {
                    SkewSilhouette.Detach(element, _ctx.SkewBindings[element]);
                    _ctx.SkewBindings.Remove(element);
                }
                var owned = transformed && _ctx.SkewTransformed[element] == (spec.SourceX, spec.SourceY);
                if (!owned)
                {
                    WriteTransform(element, spec);
                }
                return true;
            }
            if (transformed)
            {
                ClearTransform(element);
            }
            return false;
        }

        private void WriteTransform(VisualElement element, SkewSpec spec)
        {
            _ctx.SkewTransformed[element] = (spec.SourceX, spec.SourceY);
            SkewTransformFactor.TryFactor(spec.XDeg, spec.YDeg, out var rotation, out var scale);
            rotation.ToAngleAxis(out var angle, out var axis);
            element.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree), axis);
            element.style.scale = new Scale(new Vector3(scale, scale, 1f));
            SetPick(element, default);
        }

        // The shape the element is picked by (SkewPickRegion): its box sheared by the painted layer's tangents, which
        // are zero where the shear is the element's transform. Only the element types Velvet creates as ISkewPickTarget
        // have one; any other picks by its rectangle.
        private static void SetPick(VisualElement element, SkewSpec spec)
        {
            if (element is not ISkewPickTarget target)
            {
                return;
            }
            var region = target.PickRegion ??= new SkewPickRegion();
            region.TanX = Mathf.Tan(spec.XDeg * Mathf.Deg2Rad);
            region.TanY = Mathf.Tan(spec.YDeg * Mathf.Deg2Rad);
        }

        private static void ClearPick(VisualElement element)
        {
            if (element is ISkewPickTarget target)
            {
                target.PickRegion = null;
            }
        }

        private void ClearTransform(VisualElement element)
        {
            _ctx.SkewTransformed.Remove(element);
            element.style.rotate = StyleKeyword.Null;
            element.style.scale = StyleKeyword.Null;
            ClearPick(element);
        }
    }
}
