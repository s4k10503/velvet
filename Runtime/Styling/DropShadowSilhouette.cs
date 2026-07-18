using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one shadowed element, keyed in ReconcilerContext.ShadowBindings by
    // the element itself (a drop shadow needs NO structural wrapper — the baked shadow texture is painted by
    // the element's own generateVisualContent, BEHIND its content and bleeding outside the box, matching CSS
    // box-shadow as a non-structural paint). Holds the resolved spec (color/blur/offset/spread), the corner
    // radius and skew the bake follows, the registered callbacks (so they can be unregistered on detach), and
    // a co-fade opacity multiplier (animation-driven — see SetCoFade / EndCoFade) so the shadow fades together
    // with its element during an enter / exit.
    internal sealed class DropShadowBinding
    {
        public ShadowSpec Spec;
        // The class list this element carries, kept current so the geometry callback can re-resolve the
        // rounded-* corner radius after a class change.
        public string[]? ClassNames;
        // The caster's skew-x angle (degrees; 0 = upright). A skewed caster's shadow follows the slant.
        public float SkewXDeg;
        // Whether the caster is skewed on ANY axis (skew-x or skew-y). A skewed caster already has a
        // SkewSilhouette that suppresses + repaints its sheared fill/border, so the shadow paint must NOT
        // suppress or repaint the face — it paints ONLY the (sheared) shadow quad, behind that sheared fill.
        // An UPRIGHT shadowed caster has no SkewSilhouette, so the shadow paint owns the face: it suppresses
        // the native chrome and repaints an upright fill + border ON TOP of the shadow quad, so the opaque fill
        // covers the shadow's interior and offset-up overlap, leaving only the outer halo (the drop shadow).
        public bool CasterSkewed;
        // The corner radius the bake rounds to. Resolved from rounded-* off-panel and from the laid-out
        // resolvedStyle.borderTopLeftRadius once on a panel — the same precedence the wrapper path used.
        public float CornerRadius;

        // When the caster carries an inline filter, the shadow bleed (painted outside the box) would be
        // clipped to the layout rect by the filter's offscreen render tree. A transparent last-child spacer
        // sized to the shadow's extent widens boundingBox so the bleed survives. WantSpacer is decided by the
        // reconciler from the class list; BoundsSpacer is the child. See SilhouetteBoundsSpacer.
        public bool WantSpacer;
        public VisualElement? BoundsSpacer;
        // The caster's left/top border, parsed from the class list, to shift the spacer origin into padding-box
        // space (see SilhouetteBoundsSpacer.BorderInset).
        public float BorderLeft;
        public float BorderTop;

        public Action<MeshGenerationContext>? OnGenerate;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
        public EventCallback<CustomStyleResolvedEvent>? OnStyleResolved;

        // The native-face stash + sentinel suppression (UPRIGHT casters only). Shared with the skew layer; a
        // skewed caster leaves this untouched (HasStash false) because its SkewSilhouette owns the face.
        public readonly SilhouetteFaceStash Face = new();

        // Multiplier applied to the shadow's alpha at paint time (Draw). The baked quad does NOT honor UI
        // Toolkit opacity, so an enter / exit fade drives this from the caster's animated opacity (see
        // SetCoFade / EndCoFade) and the shadow fades together with its element, like a CSS box-shadow, instead
        // of showing through the still-translucent caster as a flat box. 1 = fully shown (at rest).
        public float ShadowOpacity = 1f;

        // Co-fade drivers: each in-flight enter / exit covering this shadow contributes a [0,1] factor, and
        // ShadowOpacity is their PRODUCT (1 when none). Nested fades (an enclosing screen-enter wrapping a
        // list-item fade that both cover this shadow) compose multiplicatively, matching how UI Toolkit
        // composites ancestor opacity down the tree. The common case is zero or one driver, so the first slot
        // is inline; a dictionary is allocated only when a second overlapping animation appears.
        private object? _driver0;
        private float _factor0 = 1f;
        private Dictionary<object, float>? _extraDrivers;

        // Registers or updates this driver's factor, recomputing ShadowOpacity. A driver not seen before is
        // added; the same driver re-setting its factor each tick is updated in place.
        public void SetCoFadeFactor(object driver, float factor)
        {
            factor = Mathf.Clamp01(factor);
            if (_driver0 == null || ReferenceEquals(_driver0, driver))
            {
                _driver0 = driver;
                _factor0 = factor;
            }
            else
            {
                (_extraDrivers ??= new Dictionary<object, float>())[driver] = factor;
            }
            RecomputeShadowOpacity();
        }

        // Drops this driver's contribution. When the inline slot is freed, one extra driver is promoted into it
        // so the single-driver fast path stays allocation-free; when no drivers remain ShadowOpacity returns to 1.
        public void RemoveCoFadeDriver(object driver)
        {
            if (ReferenceEquals(_driver0, driver))
            {
                _driver0 = null;
                _factor0 = 1f;
                if (_extraDrivers != null && _extraDrivers.Count > 0)
                {
                    object? promotedKey = null;
                    foreach (var kv in _extraDrivers)
                    {
                        _driver0 = kv.Key;
                        _factor0 = kv.Value;
                        promotedKey = kv.Key;
                        break;
                    }
                    if (promotedKey != null)
                    {
                        _extraDrivers.Remove(promotedKey);
                    }
                }
            }
            else
            {
                _extraDrivers?.Remove(driver);
            }
            RecomputeShadowOpacity();
        }

        private void RecomputeShadowOpacity()
        {
            var product = _driver0 != null ? _factor0 : 1f;
            if (_extraDrivers != null)
            {
                foreach (var f in _extraDrivers.Values)
                {
                    product *= f;
                }
            }
            ShadowOpacity = product;
        }
    }

    /// <summary>
    /// Paints a shadowed element's drop shadow — a baked, SDF-antialiased silhouette drawn as a single quad
    /// BEHIND the element's content in its own generateVisualContent. This is the <c>shadow-*</c> /
    /// <c>drop-shadow-*</c> parity mechanism: UI Toolkit (6000.3) has no <c>box-shadow</c>, and a CSS box-shadow
    /// is a non-structural paint (it does not change layout and it follows a transform on the element), so the
    /// shadow is painted in the caster's own content rather than hosted in a structural wrapper (a wrapper
    /// altered flex/grid sizing and did not follow the caster's transform).
    /// </summary>
    /// <remarks>
    /// The baked texture is the FULL soft silhouette (interior opaque); to leave only the outer halo the
    /// caster's OPAQUE fill must cover the interior. UI Toolkit draws the native background BEFORE
    /// generateVisualContent, so for an UPRIGHT caster the binding SUPPRESSES the native background/border (the
    /// sentinel mechanism shared with <see cref="SkewSilhouette"/>) and REPAINTS an upright fill + border ON
    /// TOP of the shadow quad in one callback (order: shadow → fill → border, native text after). A SKEWED
    /// caster already has a <see cref="SkewSilhouette"/> doing the suppression + sheared repaint, so the shadow
    /// paint only draws the shadow quad and fires BEFORE the skew face. The shadow color is the quad's vertex
    /// tint, so a color change retints with no re-bake. The bake + size-keyed cache live in
    /// <see cref="DropShadowBaker"/>.
    /// <para>
    /// Caveat — only an OPAQUE caster background fully hides the silhouette interior. A transparent or
    /// semi-transparent caster background lets the soft interior show through (the repainted fill is that same
    /// translucent color), so the shadow reads as a box-wide tint instead of an outer halo; give a shadowed
    /// caster an opaque background. Caveat — toggling a caster between skewed and upright at runtime moves face
    /// ownership between this binding and <see cref="SkewSilhouette"/> and can flash a stale face color for one
    /// frame, self-healing on the next patch.
    /// </para>
    /// </remarks>
    internal static class DropShadowSilhouette
    {
        // Lets the animation scheduler find a shadow paint binding on any element of an animating subtree (the
        // shadow is the caster's own paint, not a separate child element it could query for). Auto-drops
        // entries when an element is GC'd; Detach removes the entry eagerly so a pooled element cannot ghost a
        // prior consumer's shadow. Mirrors StyleArbitraryValueResolver's per-element side-channel.
        private static readonly ConditionalWeakTable<VisualElement, DropShadowBinding> s_byElement = new();

        // Wires the paint (+ face-stash, for an upright caster) callbacks onto the element and returns the
        // binding. casterSkewed: whether a SkewSilhouette already owns this element's face (any skew axis) —
        // resolved by the reconciler, which reconciles skew before the shadow. The shadow is the BACKGROUND,
        // so its paint must end up behind both the (native-or-repainted) fill and the text:
        // - UPRIGHT caster: this paint OWNS the face — it suppresses the native chrome and repaints the fill +
        //   border over the shadow quad in one callback. The callback is PREPENDED for a TextElement (whose
        //   text callback is registered at construction) so the shadow + repainted fill render behind the text.
        // - SKEWED caster: SkewSilhouette (attached BEFORE this) suppresses + repaints the sheared face. This
        //   paint draws ONLY the shadow quad and is PREPENDED so it fires before the skew face, drawing behind
        //   the sheared fill.
        public static DropShadowBinding Attach(VisualElement element, ShadowSpec spec, string[] classNames,
            float skewXDeg, bool casterSkewed = false)
        {
            var binding = new DropShadowBinding
            {
                Spec = spec,
                ClassNames = classNames,
                SkewXDeg = skewXDeg,
                CasterSkewed = casterSkewed,
            };
            ResolveCornerRadius(element, binding);

            binding.OnGenerate = mgc => Draw(mgc, element, binding);
            // Prepend so the shadow quad (and, for an upright caster, the repainted fill/border) renders BEHIND
            // the element's text (TextElement registers its text callback at construction) AND behind a skewed
            // caster's SkewSilhouette face (registered just before this paint). generateVisualContent callbacks
            // fire in registration order, later ones painting over earlier — prepending keeps this paint first.
            element.generateVisualContent = binding.OnGenerate + element.generateVisualContent;

            // Re-resolve the radius once layout gives the resolved border radius, and repaint at the real size.
            binding.OnGeometryChanged = _ =>
            {
                ResolveCornerRadius(element, binding);
                // Only an upright caster owns the face; a skewed caster's SkewSilhouette already stashed it.
                if (!binding.CasterSkewed && !binding.Face.HasStash)
                {
                    binding.Face.TryStash(element);
                }
                // Size the filter bounds-spacer to the now-known shadow extent (no-op when not wanted).
                SyncBoundsSpacer(element, binding);
                element.MarkDirtyRepaint();
            };
            element.RegisterCallback(binding.OnGeometryChanged);

            // CustomStyleResolvedEvent fires when the element has --var custom props; an extra (unreliable
            // alone) chance to capture the face on an upright caster before the first paint, mirroring skew.
            if (!casterSkewed)
            {
                binding.OnStyleResolved = _ =>
                {
                    if (!binding.Face.HasStash)
                    {
                        binding.Face.TryStash(element);
                    }
                };
                element.RegisterCallback(binding.OnStyleResolved);
                // Synchronous attempt: if the resolver's inline bg-[…]/border-[…] are already on the element,
                // suppress now so there is not even a one-frame ghost. Off-panel with no inline value it bails.
                binding.Face.TryStash(element);
            }

            s_byElement.AddOrUpdate(element, binding);
            element.MarkDirtyRepaint();
            return binding;
        }

        // Unregisters the callbacks, releases the face suppression (so a pooled / detached upright caster
        // restores its native chrome), and drops the side-channel entry so a pooled element cannot ghost a
        // prior consumer's shadow. The baked textures are cached process-wide and are NOT destroyed here.
        public static void Detach(VisualElement element, DropShadowBinding binding)
        {
            element.generateVisualContent -= binding.OnGenerate;
            if (binding.OnGeometryChanged != null)
            {
                element.UnregisterCallback(binding.OnGeometryChanged);
            }
            if (binding.OnStyleResolved != null)
            {
                element.UnregisterCallback(binding.OnStyleResolved);
            }
            if (binding.Face.SuppressionApplied)
            {
                binding.Face.Release(element);
            }
            SilhouetteBoundsSpacer.Remove(element, ref binding.BoundsSpacer);
            s_byElement.Remove(element);
            element.MarkDirtyRepaint();
        }

        // Records whether the caster carries a filter (so its shadow bleed needs the bounds-spacer to survive
        // the filter's offscreen render tree) and syncs the spacer now. Called from the reconciler on create /
        // patch; the geometry callback re-syncs when the size / radius settles.
        public static void SetWantSpacer(VisualElement element, DropShadowBinding binding, bool want, string[] classNames)
        {
            binding.WantSpacer = want;
            SilhouetteBoundsSpacer.BorderInset(classNames, out binding.BorderLeft, out binding.BorderTop);
            SyncBoundsSpacer(element, binding);
        }

        // Sizes (or removes) the bounds-spacer to the shadow's extent: the baked quad (box grown by
        // blur + ExtraPadding per side, shifted by the offset) unioned with the box, and — for a skewed caster,
        // whose shadow shears with it — grown by the shear overhang. Empty AABB until layout gives a size.
        private static void SyncBoundsSpacer(VisualElement element, DropShadowBinding binding)
        {
            var aabb = default(Rect);
            if (SilhouetteBoundsSpacer.TryGetLayoutSize(element, out var w, out var h))
            {
                var spec = binding.Spec;
                var pad = DropShadowBaker.QuantizePx(spec.Blur) + DropShadowBaker.ExtraPadding
                    + DropShadowBaker.QuantizePx(spec.Spread);
                var quad = new Rect(-pad + spec.OffsetX, -pad + spec.OffsetY, w + (2f * pad), h + (2f * pad));
                aabb = SilhouetteBoundsSpacer.Union(new Rect(0f, 0f, w, h), quad);
                if (binding.CasterSkewed)
                {
                    var tanX = Mathf.Tan(binding.SkewXDeg * Mathf.Deg2Rad);
                    aabb = SilhouetteBoundsSpacer.ExpandForShear(aabb, tanX, 0f);
                }
            }
            aabb = SilhouetteBoundsSpacer.ShiftToPaddingBox(aabb, binding.BorderLeft, binding.BorderTop);
            SilhouetteBoundsSpacer.Sync(element, ref binding.BoundsSpacer, binding.WantSpacer, aabb);
        }

        // Re-applies the spec / skew of an already-attached element after a patch, refreshing the radius from
        // the new class list (and the laid-out border radius when on a panel). Repaints so a preset / color /
        // skew change shows without waiting for a geometry event (a skew angle change fires none).
        //
        // A change in casterSkewed flips which layer owns the face: if the element gained a skew (an upright
        // shadow caster becoming skewed), this paint RELEASES its own suppression so SkewSilhouette's takes
        // over cleanly (both writing the sentinel would otherwise leave it stuck after one detaches); if it
        // LOST its skew, this paint re-stashes the face it must now own. The skew layer is reconciled before
        // the shadow, so its binding is already in its post-patch state when this runs.
        public static void Sync(VisualElement element, DropShadowBinding binding, ShadowSpec spec,
            string[] classNames, float skewXDeg, bool casterSkewed = false)
        {
            binding.Spec = spec;
            binding.ClassNames = classNames;
            binding.SkewXDeg = skewXDeg;
            ResolveCornerRadius(element, binding);

            if (binding.CasterSkewed != casterSkewed)
            {
                binding.CasterSkewed = casterSkewed;
                if (casterSkewed)
                {
                    // Now skewed: hand the face to SkewSilhouette. Release our suppression (it re-stashes via
                    // its own attach), and drop the OnStyleResolved retry — the skew layer owns the face now.
                    if (binding.Face.SuppressionApplied)
                    {
                        binding.Face.Release(element);
                    }
                    if (binding.OnStyleResolved != null)
                    {
                        element.UnregisterCallback(binding.OnStyleResolved);
                        binding.OnStyleResolved = null;
                    }
                }
                else if (!binding.Face.HasStash)
                {
                    // No longer skewed: this paint owns the face again — re-capture + suppress.
                    binding.Face.TryStash(element);
                }
            }
            else if (!casterSkewed)
            {
                // Steady upright: keep the face stash synced with this patch's styling, exactly as the skew
                // layer does (the shared inline bg/border slot is also written by the arbitrary-value resolver).
                binding.Face.SyncOnPatch(element, classesChanged: true);
            }

            element.MarkDirtyRepaint();
        }

        // Returns the live binding for an element, or null. Used by the animation scheduler to collect the
        // shadow paints under an animating subtree.
        public static DropShadowBinding? TryGet(VisualElement element)
            => s_byElement.TryGetValue(element, out var binding) ? binding : null;

        // Registers / updates an enter or exit animation's co-fade factor on this shadow and repaints. The
        // baked-quad paint does not inherit UITK opacity, so the scheduler samples the caster's animated
        // opacity each frame and pushes it here; Draw multiplies the shadow alpha by the resulting ShadowOpacity
        // so the shadow fades together with its element. factor 0 = fully faded, 1 = at rest. OVERLAPPING
        // drivers compose multiplicatively (see DropShadowBinding), so the shadow can never out-shine the
        // most-faded fade covering it.
        public static void SetCoFade(DropShadowBinding binding, VisualElement element, object driver, float factor)
        {
            binding.SetCoFadeFactor(driver, factor);
            element.MarkDirtyRepaint();
        }

        // Drops a finished / cancelled animation's contribution and repaints. When the LAST driver is gone the
        // shadow returns to full strength (ShadowOpacity 1), so a cancelled fade never leaves it stuck faded.
        public static void EndCoFade(DropShadowBinding binding, VisualElement element, object driver)
        {
            binding.RemoveCoFadeDriver(driver);
            element.MarkDirtyRepaint();
        }

        // Radius prefers the laid-out resolvedStyle.borderTopLeftRadius (handles %, arbitrary, and inline
        // radii) once on a panel, and falls back to the rounded-* class scale off-panel / pre-layout so a
        // value is always set.
        private static void ResolveCornerRadius(VisualElement element, DropShadowBinding binding)
        {
            var resolved = element.resolvedStyle.borderTopLeftRadius;
            if (element.panel != null && !float.IsNaN(resolved))
            {
                binding.CornerRadius = resolved;
            }
            else if (StyleShadowClass.TryResolveCornerRadius(binding.ClassNames, out var classRadius))
            {
                binding.CornerRadius = classRadius;
            }
            else
            {
                binding.CornerRadius = 0f;
            }
        }

        // Paints the drop shadow (and, for an upright caster, the repainted fill + border) in the caster's own
        // generateVisualContent. Draw order within this callback: (1) the baked shadow silhouette quad — the
        // caster box grown by pad = blur + ExtraPadding per side and shifted by the shadow offset, so the soft
        // edge bleeds outside the box; then for an UPRIGHT caster, (2) an upright rounded-rect fill of the
        // captured background color, and (3) the border stroke. The opaque fill covers the shadow silhouette's
        // interior and its offset-up overlap, leaving only the outer halo. A SKEWED caster skips (2)/(3): its
        // SkewSilhouette repaints the sheared fill/border just after this paint. Skipped before layout gives a
        // real size. During an enter / exit only the shadow QUAD's alpha is scaled by the co-fade ShadowOpacity
        // (DrawShadowQuad) so it fades with its element; the repainted upright fill/border are left at full
        // alpha because UI Toolkit already scales them by the caster's own animated opacity (only the
        // opacity-blind baked quad needs the correction).
        private static void Draw(MeshGenerationContext mgc, VisualElement ve, DropShadowBinding binding)
        {
            var w = ve.layout.width;
            var h = ve.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }

            DrawShadowQuad(mgc, ve, binding, w, h);

            // A skewed caster's face is owned (suppressed + repainted, sheared) by its SkewSilhouette, which
            // paints just after this callback. Painting an upright fill here would double the face and undo the
            // shear, so only paint the shadow quad.
            if (binding.CasterSkewed)
            {
                return;
            }

            RepaintUprightFace(mgc, ve, binding, w, h);
        }

        // Draws the baked shadow silhouette quad. Returns early if the shadow color is transparent or the bake
        // is not yet available (no graphics device / shader) — the geometry callback retries.
        private static void DrawShadowQuad(MeshGenerationContext mgc, VisualElement ve, DropShadowBinding binding,
            float w, float h)
        {
            var spec = binding.Spec;
            // Scale the shadow alpha by the co-fade multiplier so the opacity-blind baked quad fades with its
            // element during an enter / exit (1 at rest). Bail before baking when effectively invisible.
            var alpha = spec.Color.a * binding.ShadowOpacity;
            if (alpha <= 0.004f)
            {
                return;
            }

            var tex = DropShadowBaker.GetOrBakeSilhouette(binding.CornerRadius, spec.Blur, spec.Spread,
                w, h, binding.SkewXDeg);
            if (tex == null)
            {
                return; // no graphics device / shader yet — retry on the next geometry change
            }

            // Quantize the blur to whole pixels for the anchor, exactly as QuadSize / the bake do, so the
            // quad's top-left lines up with the baked texel grid (a non-integer arbitrary shadow-[…] blur would
            // otherwise offset the quad up to ~0.5px from the texture).
            var pad = DropShadowBaker.QuantizePx(spec.Blur) + DropShadowBaker.ExtraPadding;
            DropShadowBaker.QuadSize(w, h, spec.Blur, out var quadW, out var quadH);
            // Anchor the quad so its solid core sits over the box: top-left at (-pad + offsetX, -pad + offsetY)
            // and size (w + 2pad, h + 2pad) — clamped to the baked texel size so one texel maps to one pixel.
            var minX = -pad + spec.OffsetX;
            var minY = -pad + spec.OffsetY;
            var maxX = minX + quadW;
            var maxY = minY + quadH;

            // UVs are raw 0..1 (UI Toolkit remaps them into the atlas slot); the texture's alpha carries the
            // SDF antialiasing. texUV.y is flipped (box top samples v = 1) to match the bake orientation and
            // SkewSilhouette's gradient quad, so a skewed caster's shadow slants the same direction on screen.
            var tint = spec.Color;
            tint.a = alpha;
            var mwd = mgc.Allocate(4, 6, tex);
            mwd.SetNextVertex(new Vertex { position = new Vector3(minX, minY, Vertex.nearZ), tint = tint, uv = new Vector2(0f, 1f) }); // top-left
            mwd.SetNextVertex(new Vertex { position = new Vector3(maxX, minY, Vertex.nearZ), tint = tint, uv = new Vector2(1f, 1f) }); // top-right
            mwd.SetNextVertex(new Vertex { position = new Vector3(maxX, maxY, Vertex.nearZ), tint = tint, uv = new Vector2(1f, 0f) }); // bottom-right
            mwd.SetNextVertex(new Vertex { position = new Vector3(minX, maxY, Vertex.nearZ), tint = tint, uv = new Vector2(0f, 0f) }); // bottom-left
            mwd.SetNextIndex(0);
            mwd.SetNextIndex(1);
            mwd.SetNextIndex(2);
            mwd.SetNextIndex(0);
            mwd.SetNextIndex(2);
            mwd.SetNextIndex(3);
        }

        // Repaints the UPRIGHT caster's suppressed fill + border on top of the shadow quad, mirroring how
        // SkewSilhouette repaints a sheared face — but with zero shear (tanX = tanY = 0), so it traces the
        // element's own rounded-rect box. The opaque fill is what covers the shadow silhouette's interior.
        private static void RepaintUprightFace(MeshGenerationContext mgc, VisualElement ve, DropShadowBinding binding,
            float w, float h)
        {
            var p = mgc.painter2D;

            // Pre-stash frame (first paint racing the stash): read the live styles directly — the native chrome
            // is still visible this frame, so the overdraw is identical-colored.
            var fill = binding.Face.HasStash ? binding.Face.BgColor : ve.resolvedStyle.backgroundColor;
            if (fill.a > 0.004f)
            {
                SilhouetteFace.BuildShearedRoundedRect(p, ve, 0f, w, h, 0f, 0f);
                p.fillColor = fill;
                p.Fill();
            }

            var borderColor = binding.Face.HasStash ? binding.Face.BorderColor : ve.resolvedStyle.borderLeftColor;
            var borderWidth = ve.resolvedStyle.borderLeftWidth;
            if (borderWidth > 0.01f && borderColor.a > 0.004f)
            {
                // Stroke centered on a half-width-inset path ≈ CSS's inside border, matching the skew layer.
                SilhouetteFace.BuildShearedRoundedRect(p, ve, borderWidth * 0.5f, w, h, 0f, 0f);
                p.strokeColor = borderColor;
                p.lineWidth = borderWidth;
                p.lineJoin = LineJoin.Miter;
                p.Stroke();
            }
        }
    }
}
