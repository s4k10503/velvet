using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The className-driven effect appliers: the wrapper-less PAINT layers (skew silhouettes, gradient
    // backgrounds, drop shadow, animate-* motion — each the element's own generateVisualContent / inline
    // style, no structural element added) and the two structural-WRAPPER layers (ring/outline, clip-path),
    // plus the gesture (whileHover/whileTap/whileFocus) manipulator. The patcher's PatchElement/PatchMotion
    // and the node factory orchestrate these at patch/create time; the shared wrap/unwrap surgery and
    // wrapper<->inner resolution live in WrapperInfrastructure, which both this and the patcher reference.
    internal sealed class FiberWrapperElementAppliers
    {
        private readonly ReconcilerContext _ctx;
        private readonly WrapperInfrastructure _wrappers;

        public FiberWrapperElementAppliers(ReconcilerContext ctx, WrapperInfrastructure wrappers)
        {
            _ctx = ctx;
            _wrappers = wrappers;
        }

        #region Skew

        // Create-time entry point: when classNames resolves to an active skew-x-*/skew-y-* pair,
        // wires the sheared-silhouette painter onto the element (no structural wrapper — the
        // silhouette is the element's own generateVisualContent). The first color stash is
        // event-driven (the element is not attached / style-resolved yet at create time).
        internal void ApplySkewOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleSkewClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            var binding = SkewSilhouette.Attach(element, spec);
            _ctx.SkewBindings[element] = binding;
            // A skewed element OWNS any bg-gradient-* on it: the silhouette paints the gradient on a
            // sheared mesh (the rectangular background-image the non-skew path would set cannot follow the
            // shear). ApplyGradientOnCreate runs next and defers to this binding.
            SyncSkewGradient(element, binding, classNames);
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
        // resolved X angle (0 = no skew) — PatchElement forwards it to ApplyShadowOnPatch so the
        // shadow follows the sheared silhouette without re-parsing the skew classes.
        internal float ApplySkewOnPatch(VisualElement element, string[] oldClassNames, string[] newClassNames)
        {
            var bound = _ctx.SkewBindings.TryGetValue(element, out var binding);
            var has = StyleSkewClass.TryGetWinningSkewClasses(newClassNames, out var winnerX, out var winnerY);
            // Fast path: no skew anywhere near this element.
            if (!bound && !has)
            {
                return 0f;
            }

            var classesChanged = !ReferenceEquals(oldClassNames, newClassNames);

            // Steady state: the winning tokens are exactly what the live binding was built from —
            // skip the parse, but keep the color stash in sync with this patch's styling.
            if (bound && has && binding.Spec.SourceX == winnerX && binding.Spec.SourceY == winnerY)
            {
                SkewSilhouette.SyncStashOnPatch(element, binding, classesChanged);
                // Re-resolve the gradient only when the class list changed (an unchanged list cannot
                // have changed the gradient); SetGradient is itself a no-op when the spec is unchanged.
                if (classesChanged)
                {
                    SyncSkewGradient(element, binding, newClassNames);
                }
                return binding.Spec.XDeg;
            }

            SkewSpec spec = default;
            var want = has && StyleSkewClass.TryExtract(newClassNames, out spec);

            if (want && bound)
            {
                binding.Spec = spec;
                SkewSilhouette.SyncStashOnPatch(element, binding, classesChanged: true);
                SyncSkewGradient(element, binding, newClassNames);
                element.MarkDirtyRepaint();
                return spec.XDeg;
            }
            if (want)
            {
                var fresh = SkewSilhouette.Attach(element, spec);
                _ctx.SkewBindings[element] = fresh;
                SkewSilhouette.SyncStashOnPatch(element, fresh, classesChanged: true);
                SyncSkewGradient(element, fresh, newClassNames);
                return spec.XDeg;
            }
            if (bound)
            {
                SkewSilhouette.Detach(element, binding);
                _ctx.SkewBindings.Remove(element);
            }
            return 0f;
        }

        #endregion

        #region Gradient background

        // Create-time entry point: when classNames resolves to an active gradient (bg-gradient-to-* plus
        // at least one from/to stop), bakes it to a texture and applies it as the element's own
        // background-image (UI Toolkit clips it to the element's border-radius). No structural wrapper.
        internal void ApplyGradientOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleGradientClass.HasGradientClass(classNames)
                || !StyleGradientClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            // A skewed element owns its gradient (ApplySkewOnCreate ran first and fed the spec into the
            // skew binding, which paints it on the sheared mesh). Defer — a straight background-image here
            // would render a second, un-sheared rectangle behind the slant.
            if (_ctx.SkewBindings.ContainsKey(element))
            {
                return;
            }
            GradientBackground.Apply(element, spec);
            _ctx.GradientBackgrounds[element] = spec;
        }

        // Patch-time reconciliation of an element's gradient against its new class list. Mirrors the skew
        // layer's four cases: re-apply on a changed spec, attach a newly-gradiented element, clear one
        // whose gradient classes were removed, or no-op. The steady-state (spec unchanged) skips the
        // re-bake; DiffStyles only writes background-image on an actual node-style change (guarded), which
        // a gradient element never carries, so the skip cannot leave the gradient stale.
        internal void ApplyGradientOnPatch(VisualElement element, string[] classNames, bool skewable)
        {
            var bound = _ctx.GradientBackgrounds.TryGetValue(element, out var current);
            GradientSpec spec = default;
            var want = StyleGradientClass.HasGradientClass(classNames)
                && StyleGradientClass.TryExtract(classNames, out spec);

            // A skewed element paints its gradient on the sheared mesh (ApplySkewOnPatch runs after this and
            // feeds it the spec), so the straight background-image path must stand down. Only an element node
            // is skewable — a Motion never attaches a sheared silhouette, so its gradient stays on the
            // straight path even with skew classes present. Drop any straight gradient left from a prior
            // non-skew state so the un-sheared rectangle does not linger behind the slant.
            if (skewable && StyleSkewClass.TryExtract(classNames, out _))
            {
                if (bound)
                {
                    ClearStraightGradient(element, classNames);
                    _ctx.GradientBackgrounds.Remove(element);
                }
                return;
            }

            if (!bound && !want)
            {
                return;
            }
            if (want)
            {
                if (!bound || !current.Equals(spec))
                {
                    GradientBackground.Apply(element, spec);
                    _ctx.GradientBackgrounds[element] = spec;
                }
                return;
            }
            // Bound, not skewed, but the gradient classes were removed: clear the straight gradient.
            ClearStraightGradient(element, classNames);
            _ctx.GradientBackgrounds.Remove(element);
        }

        // Clears the straight gradient background-image, but only nulls the image when no className-driven
        // image (bg-[addr:…]) owns it — that resolver writes the SAME inline property, so an unconditional
        // clear would wipe an image it applied earlier in this same patch (or one it left from a prior
        // render and did not re-apply). The backgroundSize the gradient set is reset either way.
        private static void ClearStraightGradient(VisualElement element, string[] classNames)
        {
            if (StyleBackgroundImageResolver.HasBackgroundImageClass(classNames))
            {
                GradientBackground.ClearSizeOnly(element);
            }
            else
            {
                GradientBackground.Clear(element);
            }
        }

        #endregion

        #region Animate motion

        // Create-time entry point: when classNames resolves to an active animate-* motion, attaches the
        // driver. Runs AFTER ApplyGradientOnCreate so a pan mode (gradient/shimmer) sees the baked gradient
        // already on the element; a pan mode with no gradient is inert (nothing to pan). Hue / Pulse, being
        // non-pan, need no gradient and attach on any element.
        internal void ApplyAnimateOnCreate(VisualElement element, string[] classNames)
        {
            // TryExtract is itself the cheap gate (its per-class probe costs the same as a separate scan),
            // so no-animation elements pay one pass, not two.
            if (!StyleAnimateClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            if (IsPanMode(spec.Mode) && !_ctx.GradientBackgrounds.ContainsKey(element))
            {
                // A pan utility with no gradient to pan is a no-op (parity with a lone gradient stop).
                return;
            }
            _ctx.AnimationBindings[element] = StyleAnimateDriver.Attach(element, spec, ResolvePanVertical(element, spec));
        }

        // Patch-time reconciliation of an element's animate-* motion against its new class list. Mirrors the
        // gradient layer's four cases: restart on a changed spec, attach a newly-animated element, detach one
        // whose animate-* classes were removed, or no-op the steady state. A pan mode also detaches if its
        // gradient was removed (nothing left to pan). Runs AFTER ApplyGradientOnPatch for the same reason as
        // create (the pan reads the live gradient).
        internal void ApplyAnimateOnPatch(VisualElement element, string[] classNames)
        {
            var bound = _ctx.AnimationBindings.TryGetValue(element, out var binding);
            var want = StyleAnimateClass.TryExtract(classNames, out var spec);
            // A pan mode needs a gradient; if it is gone, the motion cannot run.
            if (want && IsPanMode(spec.Mode) && !_ctx.GradientBackgrounds.ContainsKey(element))
            {
                want = false;
            }

            if (!bound && !want)
            {
                return;
            }
            if (want)
            {
                if (!bound || !binding.Spec.Equals(spec))
                {
                    if (bound)
                    {
                        var detachedMode = binding.Spec.Mode;
                        StyleAnimateDriver.Detach(element, binding);
                        RestoreSharedInlineSlot(element, detachedMode, classNames);
                    }
                    _ctx.AnimationBindings[element] = StyleAnimateDriver.Attach(element, spec, ResolvePanVertical(element, spec));
                }
                else
                {
                    // Steady state: a gradient re-bake under a pan (ApplyGradientOnPatch ran just before this
                    // and may have reset backgroundSize to 100% stretch) would drag the pan's clamped edge into
                    // view — re-assert the pan oversize. No-op for the non-pan modes (Hue / Pulse).
                    StyleAnimateDriver.ReapplyPanSizing(element, binding);
                }
                return;
            }
            // Bound but the animate-* classes (or the gradient a pan needs) were removed: tear down.
            var teardownMode = binding.Spec.Mode;
            StyleAnimateDriver.Detach(element, binding);
            _ctx.AnimationBindings.Remove(element);
            RestoreSharedInlineSlot(element, teardownMode, classNames);
        }

        // Hue and Pulse own a shared inline slot while active — style.filter (Hue) / style.opacity (Pulse) —
        // that an inline-resolved utility also writes (the arbitrary filter-[..] / opacity-[.x] forms, and the
        // filter presets blur-sm etc.). Detach nulls that slot to return to the no-motion state: a NAMED USS
        // class (opacity-50) then re-resolves on its own, but a surviving inline-resolved value is lost —
        // DiffClassList does not re-apply a token that did not change across the patch. So after Detach, re-assert
        // the new class list's inline-resolved values to restore the element's class-driven appearance. Pan modes
        // own no shared inline slot (they drive background-size/position), so they skip this.
        private void RestoreSharedInlineSlot(VisualElement element, AnimateMode detachedMode, string[] classNames)
        {
            if (detachedMode == AnimateMode.Hue || detachedMode == AnimateMode.Pulse)
            {
                FiberNodePatcher.ReapplyArbitraryValues(element, classNames);
            }
        }

        private static bool IsPanMode(AnimateMode mode) => mode == AnimateMode.Gradient || mode == AnimateMode.Shimmer;

        // Pan axis from the element's bound gradient angle (Hue ignores it). Defaults to horizontal when the
        // element has no gradient (a Hue motion, or a pan that was already filtered out above).
        private bool ResolvePanVertical(VisualElement element, AnimateSpec spec)
        {
            if (IsPanMode(spec.Mode) && _ctx.GradientBackgrounds.TryGetValue(element, out var gradient))
            {
                return StyleAnimateDriver.PanVerticalForAngle(gradient.AngleDeg);
            }
            return false;
        }

        #endregion

        #region Drop Shadow

        // Create-time entry point: when classNames carries a shadow-* utility, attaches the drop-shadow
        // paint onto the element (no structural wrapper — the baked shadow texture is the element's own
        // generateVisualContent, drawn behind its content and bleeding outside the box). Composes like skew
        // and gradient: a paint, not a wrapper, so it works alongside a clip / ring wrapper and a user
        // wrapElement. The element is NOT yet in the hierarchy here, which the paint does not need.
        internal void ApplyShadowOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleShadowClass.HasShadowClass(classNames) || !StyleShadowClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            // A clip-path-* on the same element clips the box-shadow too (CSS semantics): skip the paint when
            // a clip can apply. WantsClipWrapper (not just an active base clip) mirrors the patch path's
            // clipActive gate, so a clip VARIANT (hover:clip-path-[…]) on a base-shadow element suppresses the
            // shadow at all times — the same documented limitation the wrapper era had (the wrapper layers
            // were mutually exclusive); pure base clip and pure shadow are unaffected.
            if (StyleClipPathClass.WantsClipWrapper(classNames))
            {
                return;
            }
            // A skewed caster's shadow follows the sheared silhouette (the drop-shadow behavior);
            // create-time resolves the skew here, the patch path forwards it from ApplySkewOnPatch.
            var skewXDeg = StyleSkewClass.TryExtract(classNames, out var skew) ? skew.XDeg : 0f;
            // ApplySkewOnCreate ran before this (the factory order), so a skewed caster already has a
            // SkewSilhouette owning its face. When skewed, the shadow paints ONLY the shadow quad (the skew
            // layer repaints the sheared fill/border); when upright, the shadow paint owns the face itself,
            // suppressing the native chrome and repainting an upright fill over the shadow quad. skew-y casters
            // have skewXDeg 0 yet are skewed, so this gate is the SkewBindings presence, not the X angle.
            var casterSkewed = _ctx.SkewBindings.ContainsKey(element);
            _ctx.ShadowBindings[element] =
                DropShadowSilhouette.Attach(element, spec, classNames, skewXDeg, casterSkewed);
        }

        // Patch-time reconciliation of an element's shadow state against its new class list. Mirrors the
        // skew / gradient paint layers' four cases: update the existing paint's spec, attach a newly-shadowed
        // element, detach one whose shadow was removed, or do nothing.
        // clipActive: whether the class list resolves to an active clip-path-* — resolved ONCE by the caller
        // (PatchElement forwards ApplyClipPathOnPatch's result; PatchMotion passes false). An active clip
        // suppresses the shadow (CSS clip-path clips the box-shadow too).
        // The shadow is no longer a wrapper, so the allowWrap / skip-on-Motion plumbing is gone — a Motion
        // can carry a shadow paint without becoming an AnimatePresence anchor (nothing structural is added).
        internal void ApplyShadowOnPatch(VisualElement element, string[] classNames, bool clipActive,
            float skewXDeg = 0f)
        {
            var bound = _ctx.ShadowBindings.TryGetValue(element, out var binding);
            // Fast path: no shadow anywhere near this element.
            if (!bound && !StyleShadowClass.HasShadowClass(classNames))
            {
                return;
            }

            var spec = default(ShadowSpec);
            var want = !clipActive && StyleShadowClass.TryExtract(classNames, out spec);

            // ApplySkewOnPatch ran before this (PatchElement order), so the SkewBindings entry is in its
            // post-patch state: a skewed caster's face is owned by its SkewSilhouette, an upright caster's by
            // this shadow paint. Tracks skew-y too (a skewXDeg-0 yet skewed caster keeps a SkewBinding).
            var casterSkewed = _ctx.SkewBindings.ContainsKey(element);

            if (want && bound)
            {
                DropShadowSilhouette.Sync(element, binding, spec, classNames, skewXDeg, casterSkewed);
            }
            else if (want)
            {
                _ctx.ShadowBindings[element] =
                    DropShadowSilhouette.Attach(element, spec, classNames, skewXDeg, casterSkewed);
            }
            else if (bound)
            {
                DropShadowSilhouette.Detach(element, binding);
                _ctx.ShadowBindings.Remove(element);
            }
        }

        #endregion

        #region Ring / Outline

        // USS class on the structural wrapper Velvet emits to host a ring-*/outline-* overlay. UI Toolkit has
        // no CSS box-shadow / outline, so the outset (or inset) HARD border these utilities describe is drawn
        // as a native rounded-border OVERLAY element — hardware-rendered, follows rounded-* corners, with no
        // custom material / draw-order hazard (unlike the soft, blurred drop shadow, which needs an SDF shader).
        // Lower precedence of the two structural-WRAPPER layers: clip-path takes the wrapper first, so a
        // clipped element carries no ring wrapper (the two wrappers are mutually exclusive — one per element).
        // The drop shadow is a wrapper-less paint, so a ring composes with a shadow (it does not compete).
        internal const string RingWrapperClass = "velvet-ring-wrapper";

        // Create-time entry point: when classNames resolves to a ring/outline (and the element was not already
        // wrapped), wraps element in a ring container and returns the wrapper; else returns element unchanged.
        // Mirrors ApplyShadowOnCreate. The factory calls this AFTER clip-path and shadow (lowest precedence).
        internal VisualElement ApplyRingOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleRingClass.HasRingClass(classNames) || !StyleRingClass.TryExtract(classNames, out var spec))
            {
                return element;
            }
            return BuildRingWrapper(element, spec, classNames);
        }

        // Patch-time reconciliation of an element's ring state. element is the resolved INNER. Mirrors
        // ApplyShadowOnPatch's four cases (update / wrap / unwrap / nothing). suppress is true when a
        // higher-precedence layer (clip-path or shadow) owns the wrapper, so the ring must not also wrap
        // (mutual exclusion) — a suppressed element with an existing ring binding is unwrapped. allowWrap is
        // false on the Motion patch path (a structural wrapper would become the AnimatePresence enter/exit
        // anchor while the transition stays on the inner Motion, breaking it — same rule the shadow layer keeps).
        internal void ApplyRingOnPatch(VisualElement element, string[] classNames, bool suppress, bool allowWrap)
        {
            var wrapped = _ctx.RingBindings.TryGetValue(element, out var binding);
            if (!wrapped && !StyleRingClass.HasRingClass(classNames))
            {
                return;
            }

            var spec = default(RingSpec);
            var want = !suppress && StyleRingClass.TryExtract(classNames, out spec);

            if (want && wrapped)
            {
                binding.ClassNames = classNames;
                binding.Spec = spec;
                ApplyRingSpec(binding.Overlay, spec);
                SyncRingGeometry(element, binding, classNames);
            }
            else if (want)
            {
                if (!allowWrap || _wrappers.IsAlreadyWrapped(element))
                {
                    return;
                }
                WrapRingInPlace(element, spec, classNames);
            }
            else if (wrapped)
            {
                UnwrapRingInPlace(element, binding);
            }
        }

        // Builds the ring wrapper around element: a layout-passthrough container holding element plus an
        // absolutely-positioned native-border overlay as its LAST child (so an inset band paints over the
        // inner edge; an outset band never overlaps the inner anyway). Does NOT touch any parent — the caller
        // inserts the returned wrapper.
        private VisualElement BuildRingWrapper(VisualElement element, RingSpec spec, string[] classNames)
        {
            var wrapper = WrapperInfrastructure.CreatePassthroughWrapper(RingWrapperClass);

            var overlay = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, backgroundColor = Color.clear },
            };
            ApplyRingSpec(overlay, spec);

            wrapper.Add(element); // reparents element from its current parent (if any) into the wrapper
            wrapper.Add(overlay);

            var binding = new RingBinding(wrapper, overlay) { ClassNames = classNames, Spec = spec };
            binding.OnGeometry = _ => SyncRingGeometry(element, binding, binding.ClassNames);
            element.RegisterCallback(binding.OnGeometry);

            _ctx.RingBindings[element] = binding;
            _ctx.WrapperToInnerMap[wrapper] = element;

            // Resolve geometry now so EditMode / pre-layout reads a sensible band without a tick.
            SyncRingGeometry(element, binding, classNames);
            return wrapper;
        }

        private void WrapRingInPlace(VisualElement element, RingSpec spec, string[] classNames)
        {
            var parent = element.parent;
            if (parent == null)
            {
                BuildRingWrapper(element, spec, classNames);
                return;
            }
            var index = parent.IndexOf(element);
            var wrapper = BuildRingWrapper(element, spec, classNames); // removes element from parent
            parent.Insert(index, wrapper);
        }

        private void UnwrapRingInPlace(VisualElement element, RingBinding binding)
        {
            if (binding.OnGeometry != null)
            {
                element.UnregisterCallback(binding.OnGeometry);
            }
            _ctx.RingBindings.Remove(element);
            _ctx.WrapperToInnerMap.Remove(binding.Wrapper);
            WrapperInfrastructure.RemoveWrapperRestoreInner(element, binding.Wrapper);
        }

        // Paints the spec onto the overlay (native border width + color, all four sides). The band's geometry
        // (size / position / corner radius) is set by SyncRingGeometry once the inner is laid out.
        private static void ApplyRingSpec(VisualElement overlay, RingSpec spec)
        {
            overlay.style.borderTopWidth = spec.Width;
            overlay.style.borderRightWidth = spec.Width;
            overlay.style.borderBottomWidth = spec.Width;
            overlay.style.borderLeftWidth = spec.Width;
            overlay.style.borderTopColor = spec.Color;
            overlay.style.borderRightColor = spec.Color;
            overlay.style.borderBottomColor = spec.Color;
            overlay.style.borderLeftColor = spec.Color;
        }

        // Keeps the ring overlay tracking its target: forwards the inner's flex to the wrapper, then sizes and
        // positions the overlay to the inner's resolved box. Outset (default): the band sits OUTSIDE the inner
        // edge by Offset, so the overlay inflates by (Offset + Width) per side and its outer corner radius is
        // innerRadius + Offset + Width. Inset (ring-inset): the band sits inside, so the overlay matches the
        // inner box exactly at the inner radius. Radius prefers the laid-out resolvedStyle.borderTopLeftRadius
        // (handles %, arbitrary, inline radii), falling back to the rounded-* class scale pre-layout. Pre-layout
        // (no resolved size) it defers to the geometry callback.
        private static void SyncRingGeometry(VisualElement element, RingBinding binding, string[] classNames)
        {
            if (binding == null)
            {
                return;
            }
            var overlay = binding.Overlay;
            var spec = binding.Spec;

            float innerRadius;
            var resolvedRadius = element.resolvedStyle.borderTopLeftRadius;
            // Prefer a NON-ZERO laid-out radius (handles %, arbitrary, inline radii). The `> 0f` is deliberate:
            // a USS rounded-* class does not always reflect into
            // resolvedStyle.borderTopLeftRadius off-screen / pre-layout (it reads 0 there), so a resolved 0
            // must fall back to the rounded-* class scale rather than being trusted as "no rounding" — else a
            // rounded card would get a square ring. A genuine no-rounding element resolves 0 here and also
            // misses the class scale, landing at 0 correctly.
            if (element.panel != null && !float.IsNaN(resolvedRadius) && resolvedRadius > 0f)
            {
                innerRadius = resolvedRadius;
            }
            else if (StyleRingClass.TryResolveCornerRadius(classNames, out var classRadius))
            {
                innerRadius = classRadius;
            }
            else
            {
                innerRadius = 0f;
            }

            WrapperInfrastructure.ForwardInnerFlexToWrapper(element, binding.Wrapper);

            var width = element.resolvedStyle.width;
            var height = element.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0)
            {
                return;
            }
            var originX = element.layout.x;
            var originY = element.layout.y;
            if (float.IsNaN(originX)) originX = 0f;
            if (float.IsNaN(originY)) originY = 0f;

            var grow = spec.Inset ? 0f : spec.Offset + spec.Width;
            overlay.style.left = originX - grow;
            overlay.style.top = originY - grow;
            overlay.style.width = width + (grow * 2f);
            overlay.style.height = height + (grow * 2f);

            var outerRadius = spec.Inset ? innerRadius : innerRadius + spec.Offset + spec.Width;
            overlay.style.borderTopLeftRadius = outerRadius;
            overlay.style.borderTopRightRadius = outerRadius;
            overlay.style.borderBottomLeftRadius = outerRadius;
            overlay.style.borderBottomRightRadius = outerRadius;
        }

        #endregion

        #region Clip Path

        // USS class on the structural wrapper Velvet emits to host a clip-path-* element. UI Toolkit
        // (6000.3) has no USS clip-path; the supported arbitrary-shape mask is an overflow-hidden
        // element whose background-image is a VECTOR image (UIR stencil-clips the subtree to the
        // vector geometry). The wrapper carries that baked VectorImage (ClipPathVectorImageBaker), so
        // the inner element's own background, borders, text and children are ALL clipped to the shape
        // — CSS clip-path's "clips everything, including descendants" semantics. Limitations vs CSS:
        // pointer picking is unchanged (the clipped-away corners still hit-test), and world-space
        // panels (which only support rectangle clipping) ignore the mask.
        internal const string ClipPathWrapperClass = "velvet-clip-path-wrapper";

        // Create-time entry point: when classNames carries an active clip-path-* utility (and the
        // element was not already wrapped by a user wrapElement), wraps element in a clip container
        // and returns the wrapper; otherwise returns element unchanged. Mirrors ApplyShadowOnCreate.
        // TryExtract alone is the gate — its per-class probe costs the same as a separate
        // HasClipPathClass scan, so no-clip elements pay one pass, not two.
        internal VisualElement ApplyClipPathOnCreate(VisualElement element, string[] classNames)
        {
            // Wrap whenever a clip can EVER apply — base OR a variant (hover:clip-*) — so the stencil wrapper
            // persists and a hover never has to wrap/unwrap (which would mutate the parent mid-event). The
            // at-rest shape is resolved from the live class list (base clip; a variant-only clip is null here
            // and lights up on its state via ReResolveClipPathLive).
            if (!StyleClipPathClass.WantsClipWrapper(classNames))
            {
                return element;
            }
            StyleClipPathClass.TryExtractLive(element, out var spec);
            return BuildClipPathWrapper(element, spec);
        }

        // Patch-time reconciliation of an element's clip state against its new class list. Same four
        // cases as the other effect layers: update the existing clip's spec, wrap a newly-clipped element
        // in place, unwrap one whose clip class was removed, or do nothing. Runs BEFORE the shadow patch
        // (see PatchElement): clip-path clips the box-shadow (CSS), so the shadow patch reads this result
        // and suppresses its paint while a clip is active.
        // Returns whether a clip WRAPPER owns this element after the patch (PatchElement forwards it so the
        // shadow paint self-suppresses and the lower-precedence ring layer does not also wrap). KNOWN
        // LIMITATION: this is "a clip can apply" (base or any variant), not "a clip is applied right now" —
        // the clip / ring WRAPPERS are mutually exclusive (one wrapper per element) and the shadow paint
        // suppression keys off the same gate, so a clip VARIANT on an element that also has a base shadow-* /
        // ring-* suppresses that shadow / ring at ALL times, not only while the variant's state is on. The
        // (rare) combo `shadow-lg hover:clip-*` therefore shows no shadow even at rest. Pure base clip-path
        // and pure shadow/ring are unaffected.
        internal bool ApplyClipPathOnPatch(VisualElement element, string[] classNames)
        {
            var wrapped = _ctx.ClipPathBindings.TryGetValue(element, out var binding);
            // Wrap whenever a clip can apply (base OR a variant) — the wrapper is persistent so a hover toggle
            // never wraps/unwraps; the active shape is the live cascade, resolved below.
            var wantWrap = StyleClipPathClass.WantsClipWrapper(classNames);
            if (!wrapped && !wantWrap)
            {
                return false;
            }

            // The at-rest shape from the live class list (base clip; a variant-only clip is null here and is
            // applied transiently by ReResolveClipPathLive on its state). A null spec = no mask, wrapper kept.
            StyleClipPathClass.TryExtractLive(element, out var spec);

            if (wantWrap && wrapped)
            {
                if ((binding.Spec?.Source) != (spec?.Source))
                {
                    binding.Spec = spec;
                    // Force the next sync to re-evaluate even at the same box size (cached if seen before).
                    binding.BakedWidth = -1f;
                    binding.BakedHeight = -1f;
                    SyncClipPathGeometry(element, binding);
                }
                return true;
            }
            if (wantWrap)
            {
                // A clip added to an element that was ring-wrapped on a previous render: the ring patch
                // (suppressed by the active clip) will not unwrap this pass, so swap wrappers here — clip-path
                // clips the ring, and the two are mutually-exclusive wrappers (one per element). The shadow is
                // a paint, not a wrapper, so it needs no unwrap here: the shadow patch runs after this one,
                // sees the now-active clip (clipActive), and detaches the paint (clip-path clips the shadow).
                if (_ctx.RingBindings.TryGetValue(element, out var staleRing))
                {
                    UnwrapRingInPlace(element, staleRing);
                }
                // Honor the user wrapElement opt-out on patch too (same rule as the ring layer).
                if (_wrappers.IsAlreadyWrapped(element))
                {
                    return true;
                }
                WrapClipPathInPlace(element, spec);
                return true;
            }
            // Wrapped, but no clip token (base or variant) remains: unwrap.
            UnwrapClipPathInPlace(element, binding);
            return false;
        }

        // Re-resolves a clipped element's mask from its LIVE class list — invoked when a variant manipulator
        // toggles a clip-path payload (hover/focus/dark/…), since a clip class toggle alone does nothing (UITK
        // has no clip-path property). The wrapper already exists (WantsClipWrapper wrapped it), so this only
        // swaps the mask — the per-binding bake cache makes a return to a previously-seen shape re-bake-free.
        internal void ReResolveClipPathLive(VisualElement element)
        {
            if (!_ctx.ClipPathBindings.TryGetValue(element, out var binding))
            {
                return;
            }
            StyleClipPathClass.TryExtractLive(element, out var spec);
            if ((binding.Spec?.Source) == (spec?.Source))
            {
                return;
            }
            binding.Spec = spec;
            binding.BakedWidth = -1f;
            binding.BakedHeight = -1f;
            SyncClipPathGeometry(element, binding);
        }

        // Builds the clip wrapper around element: a layout-passthrough container (same passthrough
        // styling as the ring wrapper) that additionally hides overflow and carries the baked
        // vector shape as its background — the combination UIR stencil-clips descendants to.
        // Does NOT touch any parent — the caller inserts the returned wrapper.
        private VisualElement BuildClipPathWrapper(VisualElement element, ClipPathSpec? spec)
        {
            var wrapper = WrapperInfrastructure.CreatePassthroughWrapper(ClipPathWrapperClass);
            // overflow:hidden + vector background-image = UIR stencil mask of the subtree. A variant-only clip
            // (spec null at rest) leaves overflow visible so the unclipped element is not rectangle-clipped;
            // SyncClipPathGeometry toggles overflow as the mask comes and goes.
            wrapper.style.overflow = spec != null ? Overflow.Hidden : Overflow.Visible;
            wrapper.Add(element); // reparents element from its current parent (if any) into the wrapper

            var binding = new ClipPathBinding(wrapper) { Spec = spec };
            binding.OnGeometry = _ => SyncClipPathGeometry(element, binding);
            element.RegisterCallback(binding.OnGeometry);

            _ctx.ClipPathBindings[element] = binding;
            _ctx.WrapperToInnerMap[wrapper] = element;

            // Off-panel / pre-layout the size is unknown (NaN) and the sync no-ops; on a patch-time
            // wrap of an already-laid-out element it bakes immediately. Either way the inner sits at
            // the FRESH wrapper's origin — element.layout still holds stale OLD-parent coordinates
            // until the next layout pass, so the anchor must not read it here (a (100,50) card would
            // otherwise show its mask offset by (100,50) for one frame).
            SyncClipPathGeometry(element, binding, innerAtWrapperOrigin: true);
            return wrapper;
        }

        // Wraps an already-mounted element in place, inserting the wrapper at the element's slot.
        private void WrapClipPathInPlace(VisualElement element, ClipPathSpec? spec)
        {
            var parent = element.parent;
            if (parent == null)
            {
                // Not in the hierarchy (defensive): build the binding but there is no slot to insert into.
                BuildClipPathWrapper(element, spec);
                return;
            }
            var index = parent.IndexOf(element);
            var wrapper = BuildClipPathWrapper(element, spec); // removes element from parent
            parent.Insert(index, wrapper);
        }

        // Removes the clip wrapper, destroying the baked VectorImage, and restores the inner at the
        // same slot.
        private void UnwrapClipPathInPlace(VisualElement element, ClipPathBinding binding)
        {
            var wrapper = binding.Wrapper;
            binding.DisposeImage();
            if (binding.OnGeometry != null)
            {
                element.UnregisterCallback(binding.OnGeometry);
            }
            _ctx.ClipPathBindings.Remove(element);
            _ctx.WrapperToInnerMap.Remove(wrapper);
            WrapperInfrastructure.RemoveWrapperRestoreInner(element, wrapper);
        }

        // Keeps the mask tracking its target: forwards the inner's flex to the wrapper (same rule as
        // the ring wrapper) and (re)bakes the vector shape at the inner's resolved box. The baked
        // VectorImage stores TIGHT bounds, so the background is explicitly positioned and sized by
        // the analytic path bounds, anchored at the inner's layout origin within the wrapper.
        // innerAtWrapperOrigin: true on the wrap-time call, when element.layout still holds
        // OLD-parent coordinates — inside the fresh wrapper the inner sits at the origin until the
        // next layout pass (whose GeometryChangedEvent re-anchors with real coordinates).
        private static void SyncClipPathGeometry(VisualElement element, ClipPathBinding binding,
            bool innerAtWrapperOrigin = false)
        {
            WrapperInfrastructure.ForwardInnerFlexToWrapper(element, binding.Wrapper);

            // No active clip (a variant-only clip at rest, e.g. an element carrying only hover:clip-path-[…]
            // while not hovered): the persistent wrapper shows the subtree unclipped. Drop the mask but KEEP
            // the wrapper + the bake cache, so a later state change re-applies a cached shape with no re-bake.
            if (binding.Spec == null)
            {
                binding.DetachBackground();
                binding.Wrapper.style.visibility = StyleKeyword.Null;
                // No mask ⇒ no clipping at all: drop overflow:hidden so an unclipped (e.g. hover-only) element
                // is not rectangle-clipped at rest.
                binding.Wrapper.style.overflow = Overflow.Visible;
                return;
            }

            var width = element.resolvedStyle.width;
            var height = element.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0)
            {
                // Pre-layout: bake on the first GeometryChangedEvent instead.
                return;
            }

            // The wrapper centers the inner, so a forwarded flex-grow that enlarges the wrapper can
            // leave the inner off-origin; the background must follow the inner's layout origin.
            var originX = innerAtWrapperOrigin ? 0f : element.layout.x;
            var originY = innerAtWrapperOrigin ? 0f : element.layout.y;
            if (float.IsNaN(originX)) originX = 0f;
            if (float.IsNaN(originY)) originY = 0f;

            var sizeUnchanged = Mathf.Abs(width - binding.BakedWidth) < 0.5f
                && Mathf.Abs(height - binding.BakedHeight) < 0.5f;
            if (sizeUnchanged)
            {
                // Same box, possibly moved within the wrapper: re-anchor the existing bake only.
                if (binding.Image != null)
                {
                    ApplyClipPathBackgroundRect(binding, originX, originY);
                }
                return;
            }

            // Stretch-invariant (all-percentage) shapes scale linearly with the box: rescale the
            // existing bake via background-size instead of re-tessellating a new VectorImage —
            // a size animation then re-bakes zero times instead of once per frame.
            if (binding.Image != null && binding.Spec.StretchInvariant
                && ClipPathVectorImageBaker.TryComputeBounds(binding.Spec, width, height, out var stretched))
            {
                binding.Bounds = stretched;
                binding.BakedWidth = width;
                binding.BakedHeight = height;
                ApplyClipPathBackgroundRect(binding, originX, originY);
                return;
            }

            // GetOrBake reuses a cached VectorImage for this (spec, size) — so toggling a state variant back
            // to a previously-seen shape is an O(1) lookup, not a re-tessellation. The cache owns the image
            // (destroyed on teardown), so a switch never destroys the outgoing shape.
            if (!binding.GetOrBake(binding.Spec, width, height, out var image, out var bounds))
            {
                // CSS: an empty basic shape clips EVERYTHING (css-shapes-1 even reduces over-100%
                // inset() offsets to a zero-area box). Hide the subtree rather than dropping the
                // mask — a crossing inset must render nothing, not everything. Record the attempted
                // size so the next identical geometry event does not re-attempt the bake.
                binding.Image = null;
                binding.Wrapper.style.backgroundImage = StyleKeyword.Null;
                binding.BakedWidth = width;
                binding.BakedHeight = height;
                binding.Wrapper.style.visibility = Visibility.Hidden;
                return;
            }
            binding.Wrapper.style.visibility = StyleKeyword.Null;
            // Active mask ⇒ stencil-clip the subtree (a prior variant-only rest state left overflow visible).
            binding.Wrapper.style.overflow = Overflow.Hidden;

            binding.Image = image;
            binding.Bounds = bounds;
            binding.BakedWidth = width;
            binding.BakedHeight = height;
            var ws = binding.Wrapper.style;
            ws.backgroundImage = Background.FromVectorImage(image);
            ws.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            ApplyClipPathBackgroundRect(binding, originX, originY);
        }

        // Writes the background anchor (and, for the stretch path, the rescaled size) from the
        // binding's current analytic bounds.
        private static void ApplyClipPathBackgroundRect(ClipPathBinding binding, float originX, float originY)
        {
            var ws = binding.Wrapper.style;
            ws.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, originX + binding.Bounds.x);
            ws.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, originY + binding.Bounds.y);
            ws.backgroundSize = new BackgroundSize(binding.Bounds.width, binding.Bounds.height);
        }

        #endregion

        #region Gesture Manipulator

        // Configures (creates / updates / removes) the element's StyleGestureClassManipulator from the
        // whileHover/whileTap/whileFocus class strings. Creates one when gesture classes are present and none
        // exists, updates the existing one's classes when present, and removes it (clearing the tracking entry)
        // once all three class strings are empty.
        internal void ApplyGestureManipulator(VisualElement element, string? whileHoverClass, string? whileTapClass, string? whileFocusClass)
        {
            var hoverClasses = V.ParseClassNames(whileHoverClass);
            var tapClasses = V.ParseClassNames(whileTapClass);
            var focusClasses = V.ParseClassNames(whileFocusClass);
            var hasGesture = hoverClasses.Length > 0 || tapClasses.Length > 0 || focusClasses.Length > 0;

            if (_ctx.GestureManipulators.TryGetValue(element, out var existing))
            {
                if (hasGesture)
                {
                    existing.UpdateClasses(hoverClasses, tapClasses, focusClasses);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.GestureManipulators.Remove(element);
                }
            }
            else if (hasGesture)
            {
                var manipulator = new StyleGestureClassManipulator(hoverClasses, tapClasses, focusClasses);
                element.AddManipulator(manipulator);
                _ctx.GestureManipulators[element] = manipulator;
            }
        }

        #endregion

        #region Element bindings (SceneView / Particles)

        // Binds / re-binds / releases a SceneView element's camera-output machinery — the mount paths
        // (plain element AND Motion host) and the props diff all land here, beside the sibling binding
        // lifecycles above, because the binding owns live resources (a framework-created RenderTexture,
        // a registered geometry callback, an editor-panel repaint tick) tracked per element for the
        // cleaner and the reconciler dispose sweep to release.
        internal void ApplySceneView(VisualElement element, SceneViewSettings? settings)
        {
            if (element is not SceneViewElement)
            {
                return;
            }
            ApplyElementBinding(element, settings, _ctx.SceneViewBindings,
                s_sceneViewAttach, s_sceneViewUpdate, s_sceneViewDetach);
        }

        // Binds / re-binds / releases a Particles element's simulation-and-draw machinery, on the same
        // dispatch as the SceneView binding: live resources here are the hidden simulation host
        // GameObject, the painter callback and the repaint tick.
        internal void ApplyParticles(VisualElement element, ParticlesSettings? settings)
        {
            if (element is not ParticlesElement)
            {
                return;
            }
            ApplyElementBinding(element, settings, _ctx.ParticlesBindings,
                s_particlesAttach, s_particlesUpdate, s_particlesDetach);
        }

        // Unlike SceneView/Particles, Anchored has no dedicated element type to gate on — V.Anchored builds
        // a plain ElementNode (any host type is valid; the binding only ever writes inline left/top), so
        // this dispatches straight to the shared binding logic with no type check.
        internal void ApplyAnchored(VisualElement element, AnchoredSettings? settings)
        {
            ApplyElementBinding(element, settings, _ctx.AnchoredBindings,
                s_anchoredAttach, s_anchoredUpdate, s_anchoredDetach);
        }

        // Cached method-group delegates so the shared dispatch below adds no per-call allocation.
        private static readonly Func<VisualElement, SceneViewSettings, SceneViewBinding> s_sceneViewAttach = SceneViewDriver.Attach;
        private static readonly Action<VisualElement, SceneViewBinding, SceneViewSettings> s_sceneViewUpdate = SceneViewDriver.Update;
        private static readonly Action<VisualElement, SceneViewBinding> s_sceneViewDetach = SceneViewDriver.Detach;
        private static readonly Func<VisualElement, ParticlesSettings, ParticlesBinding> s_particlesAttach = ParticlesDriver.Attach;
        private static readonly Action<VisualElement, ParticlesBinding, ParticlesSettings> s_particlesUpdate = ParticlesDriver.Update;
        private static readonly Action<VisualElement, ParticlesBinding> s_particlesDetach = ParticlesDriver.Detach;
        private static readonly Func<VisualElement, AnchoredSettings, AnchoredBinding> s_anchoredAttach = AnchoredDriver.Attach;
        private static readonly Action<VisualElement, AnchoredBinding, AnchoredSettings> s_anchoredUpdate = AnchoredDriver.Update;
        private static readonly Action<VisualElement, AnchoredBinding> s_anchoredDetach = AnchoredDriver.Detach;

        // The attach/update/detach dispatch both element bindings share. A vanished settings prop only
        // happens on a hand-built ElementNode (the factories always carry settings, even for a null
        // camera/effect): release everything and drop the binding — the element stays mounted, inert.
        private static void ApplyElementBinding<TSettings, TBinding>(
            VisualElement element,
            TSettings? settings,
            Dictionary<VisualElement, TBinding> bindings,
            Func<VisualElement, TSettings, TBinding> attach,
            Action<VisualElement, TBinding, TSettings> update,
            Action<VisualElement, TBinding> detach)
            where TSettings : class
        {
            if (bindings.TryGetValue(element, out var binding))
            {
                if (settings == null)
                {
                    detach(element, binding);
                    bindings.Remove(element);
                    return;
                }
                update(element, binding, settings);
            }
            else if (settings != null)
            {
                bindings[element] = attach(element, settings);
            }
        }

        #endregion
    }
}
