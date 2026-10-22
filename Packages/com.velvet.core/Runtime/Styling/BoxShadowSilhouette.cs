using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one shadowed element, keyed in ReconcilerContext.ShadowBindings by
    // the element itself (a box shadow needs NO structural wrapper — the baked shadow texture is painted by
    // the element's own generateVisualContent, BEHIND its content and bleeding outside the box, matching CSS
    // box-shadow as a non-structural paint). Holds the resolved layers (color/blur/offset/spread, first on top),
    // the corner radius and skew the bakes follow, and the registered callbacks (so they can be unregistered on
    // detach).
    internal sealed class BoxShadowBinding
    {
        public ShadowSpec[] Layers = System.Array.Empty<ShadowSpec>();
        // The class list this element carries, kept current so the geometry callback can re-resolve the
        // rounded-* corner radius after a class change.
        public string[]? ClassNames;
        // The caster's skew-x and skew-y angles (degrees; 0 = upright). A skewed caster's shadow follows the slant.
        public float SkewXDeg;
        public float SkewYDeg;
        // Whether the caster is skewed on ANY axis (skew-x or skew-y). A skewed caster already has a
        // SkewSilhouette that suppresses + repaints its sheared fill/border, so the shadow paint must NOT
        // suppress or repaint the face — it paints ONLY the (sheared) shadow quad, behind that sheared fill.
        // An UPRIGHT shadowed caster has no SkewSilhouette, so the shadow paint owns the face: it suppresses
        // the native chrome and repaints an upright fill + border ON TOP of the outer shadow quads, keeping the
        // fill above their anti-aliased cut edge, with the inset layers between the fill and the border.
        public bool CasterSkewed;
        // The corner radii the bakes round to (top-left, top-right, bottom-right, bottom-left). Resolved from
        // rounded-* off-panel and from the laid-out resolvedStyle once on a panel.
        public Vector4 CornerRadii;

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

        // The line style border-dashed / border-dotted give the repainted border, which FiberBorderStyleApplier
        // records here while it defers its own layer to this one; Solid otherwise.
        public BorderLineStyle BorderStyle;

        // Whether a check of who owns the face is waiting to run (BoxShadowSilhouette.SettleFace).
        public bool FacePending;

        public Action<MeshGenerationContext>? OnGenerate;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
        public EventCallback<CustomStyleResolvedEvent>? OnStyleResolved;

        // The native-face stash + sentinel suppression (UPRIGHT casters only). Shared with the skew layer; a
        // skewed caster leaves this untouched (HasStash false) because its SkewSilhouette owns the face.
        public readonly SilhouetteFaceStash Face = new();
    }

    // The four values the reconciler resolves together for one patch; each means what the like-named
    // BoxShadowBinding field means.
    internal readonly struct BoxShadowSyncRequest
    {
        public ShadowSpec[] Layers { get; init; }
        public string[] ClassNames { get; init; }
        public float SkewXDeg { get; init; }
        public float SkewYDeg { get; init; }
        public bool CasterSkewed { get; init; }
    }

    /// <summary>
    /// Paints a shadowed element's box shadow — one baked quad per layer drawn BEHIND the element's content in
    /// its own generateVisualContent. This is the <c>shadow-*</c> parity mechanism: UI Toolkit (6000.3) has no
    /// <c>box-shadow</c>, and a CSS box-shadow is a non-structural paint (it does not change layout and it
    /// follows a transform on the element), so the shadow is painted in the caster's own content rather than
    /// hosted in a structural wrapper (a wrapper altered flex/grid sizing and did not follow the caster's
    /// transform).
    /// </summary>
    /// <remarks>
    /// Each bake is cut out where the caster's border box lies, as CSS draws an outer shadow only outside it,
    /// so a translucent background shows what is behind the element rather than its shadow. UI Toolkit draws
    /// the native background BEFORE generateVisualContent, so for an UPRIGHT caster the binding SUPPRESSES the
    /// native background/border (the sentinel mechanism shared with <see cref="SkewSilhouette"/>) and REPAINTS
    /// an upright fill + border ON TOP of the shadow quads in one callback (order: shadow → fill → border,
    /// native text after), keeping the chrome above the quads' anti-aliased cut edge. A SKEWED caster already
    /// has a <see cref="SkewSilhouette"/> doing the suppression + sheared repaint, so the shadow paint only
    /// draws the quads and fires BEFORE the skew face. A layer's color is its quad's vertex tint, so a color
    /// change retints with no re-bake. The bake + cache live in <see cref="BoxShadowBaker"/>.
    /// <para>
    /// Caveat — toggling a caster between skewed and upright at runtime moves face ownership between this
    /// binding and <see cref="SkewSilhouette"/> and can flash a stale face color for one frame, self-healing on
    /// the next patch.
    /// </para>
    /// </remarks>
    internal static class BoxShadowSilhouette
    {
        // Lets the animation scheduler find a shadow paint binding on any element of an animating subtree (the
        // shadow is the caster's own paint, not a separate child element it could query for). Auto-drops
        // entries when an element is GC'd; Detach removes the entry eagerly so a pooled element cannot ghost a
        // prior consumer's shadow. Mirrors StyleArbitraryValueResolver's per-element side-channel.
        private static readonly ConditionalWeakTable<VisualElement, BoxShadowBinding> s_byElement = new();

        // The shadow colours each element's classes declare, recorded whether or not it paints a shadow, for a
        // child's -inherit to take.
        private static readonly ConditionalWeakTable<VisualElement, DeclaredShadowColours> s_declaredColours = new();

        // Records the colours classNames declares on element, or forgets them where it declares none.
        internal static void RecordDeclaredColours(VisualElement element, string[]? classNames)
        {
            if (classNames != null && StyleShadowClass.HasShadowClass(classNames))
            {
                s_declaredColours.AddOrUpdate(element, StyleShadowClass.DeclaredColours(classNames));
            }
            else if (s_declaredColours.TryGetValue(element, out _))
            {
                s_declaredColours.Remove(element);
            }
        }

        // The colour family takes on element through inherit: the parent's declared colour, the grandparent's where the
        // parent's is itself inherit, and so on up; null where the ancestor reached declares no colour for family.
        private static Color? InheritedColour(VisualElement element, ShadowColourFamily family)
        {
            for (var parent = element.hierarchy.parent; parent != null; parent = parent.hierarchy.parent)
            {
                if (!s_declaredColours.TryGetValue(parent, out var declared))
                {
                    return null;
                }
                var colour = declared.Of(family);
                if (!colour.Inherit)
                {
                    return colour.Colour;
                }
            }
            return null;
        }

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
        public static BoxShadowBinding Attach(VisualElement element, ShadowSpec[] layers, string[] classNames,
            float skewXDeg, float skewYDeg, bool casterSkewed = false)
        {
            var binding = new BoxShadowBinding
            {
                Layers = layers,
                ClassNames = classNames,
                SkewXDeg = skewXDeg,
                SkewYDeg = skewYDeg,
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
                if (!binding.CasterSkewed && !binding.Face.HasStash && !NativeFace(element, binding))
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
                    if (!binding.Face.HasStash && !NativeFace(element, binding))
                    {
                        binding.Face.TryStash(element);
                    }
                };
                element.RegisterCallback(binding.OnStyleResolved);
                // Synchronous attempt: if the resolver's inline bg-[…]/border-[…] are already on the element,
                // suppress now so there is not even a one-frame ghost. Off-panel with no inline value it bails.
                if (!NativeFace(element, binding))
                {
                    binding.Face.TryStash(element);
                }
            }

            s_byElement.AddOrUpdate(element, binding);
            element.MarkDirtyRepaint();
            return binding;
        }

        // Unregisters the callbacks, releases the face suppression (so a pooled / detached upright caster
        // restores its native chrome), and drops the side-channel entry so a pooled element cannot ghost a
        // prior consumer's shadow. The baked textures are cached process-wide and are NOT destroyed here.
        public static void Detach(VisualElement element, BoxShadowBinding binding)
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
            FaceBorderOverlay.Remove(element);
            SilhouetteBoundsSpacer.Remove(element, ref binding.BoundsSpacer);
            s_byElement.Remove(element);
            element.MarkDirtyRepaint();
        }

        // Records whether the caster carries a filter (so its shadow bleed needs the bounds-spacer to survive
        // the filter's offscreen render tree) and syncs the spacer now. Called from the reconciler on create /
        // patch; the geometry callback re-syncs when the size / radius settles.
        public static void SetWantSpacer(VisualElement element, BoxShadowBinding binding, bool want, string[] classNames)
        {
            binding.WantSpacer = want;
            SilhouetteBoundsSpacer.BorderInset(classNames, out binding.BorderLeft, out binding.BorderTop);
            SyncBoundsSpacer(element, binding);
        }

        // Sizes (or removes) the bounds-spacer to the shadow's extent: the rects every layer's quad covers
        // (BoxShadowBaker.ShadowRect), unioned with the box — sheared too on a skewed caster. Empty AABB until
        // layout gives a size.
        private static void SyncBoundsSpacer(VisualElement element, BoxShadowBinding binding)
        {
            var aabb = default(Rect);
            if (SilhouetteBoundsSpacer.TryGetLayoutSize(element, out var w, out var h))
            {
                var skew = new Vector2(binding.SkewXDeg, binding.SkewYDeg);
                aabb = new Rect(0f, 0f, w, h);
                if (binding.CasterSkewed)
                {
                    aabb = SilhouetteBoundsSpacer.ExpandForShear(aabb, Mathf.Tan(skew.x * Mathf.Deg2Rad),
                        Mathf.Tan(skew.y * Mathf.Deg2Rad));
                }
                var box = BoxOf(element, binding, w, h);
                foreach (var layer in binding.Layers)
                {
                    aabb = SilhouetteBoundsSpacer.Union(aabb, BoxShadowBaker.ShadowRect(box, layer, skew));
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
        public static void Sync(VisualElement element, BoxShadowBinding binding,
            in BoxShadowSyncRequest request)
        {
            binding.Layers = request.Layers;
            binding.ClassNames = request.ClassNames;
            binding.SkewXDeg = request.SkewXDeg;
            binding.SkewYDeg = request.SkewYDeg;
            ResolveCornerRadius(element, binding);

            var casterSkewed = request.CasterSkewed;
            if (binding.CasterSkewed != casterSkewed)
            {
                binding.CasterSkewed = casterSkewed;
                if (casterSkewed)
                {
                    // Now skewed: hand the face to SkewSilhouette. Release our suppression, and drop the
                    // OnStyleResolved retry — the skew layer owns the face now. The skew attached earlier in this
                    // patch, while this layer's sentinel stood in the slots, so it takes the colours Release writes
                    // back (Retake).
                    if (binding.Face.SuppressionApplied)
                    {
                        binding.Face.Release(element);
                        SilhouetteFaceStash.Retake(element);
                    }
                    if (binding.OnStyleResolved != null)
                    {
                        element.UnregisterCallback(binding.OnStyleResolved);
                        binding.OnStyleResolved = null;
                    }
                }
                else if (!binding.Face.HasStash && !NativeFace(element, binding))
                {
                    // No longer skewed: this paint owns the face again — re-capture + suppress.
                    binding.Face.TryStash(element);
                }
            }
            else if (!casterSkewed && binding.Face.SuppressionApplied)
            {
                // Steady upright: keep the face stash synced with this patch's styling, exactly as the skew
                // layer does (the shared inline bg/border slot is also written by the arbitrary-value resolver).
                binding.Face.SyncOnPatch(element, classesChanged: true);
            }

            element.MarkDirtyRepaint();
        }

        // Returns the live binding for an element, or null. Used by the animation scheduler to collect the
        // shadow paints under an animating subtree.
        public static BoxShadowBinding? TryGet(VisualElement element)
            => s_byElement.TryGetValue(element, out var binding) ? binding : null;

        // The radii prefer the laid-out resolvedStyle corners (handles %, arbitrary, and inline radii) once on a
        // panel, and fall back to the rounded-* class scale off-panel / pre-layout, where the top-left corner's
        // class stands for all four, so a value is always set.
        private static void ResolveCornerRadius(VisualElement element, BoxShadowBinding binding)
        {
            // A layoutId projection drawing the element at a different scale on each axis holds its radius slots
            // at none and draws a radius per axis (ProjectedCornerFace); the horizontal one stands for the corner.
            if (ProjectedCornerFace.TryGetRadii(element, out var projected))
            {
                binding.CornerRadii = new Vector4(projected.TopLeft.x, projected.TopRight.x, projected.BottomRight.x,
                    projected.BottomLeft.x);
                return;
            }
            var style = element.resolvedStyle;
            if (element.panel != null && !float.IsNaN(style.borderTopLeftRadius))
            {
                binding.CornerRadii = new Vector4(style.borderTopLeftRadius, style.borderTopRightRadius,
                    style.borderBottomRightRadius, style.borderBottomLeftRadius);
            }
            else if (StyleShadowClass.TryResolveCornerRadius(binding.ClassNames, out var classRadius))
            {
                binding.CornerRadii = new Vector4(classRadius, classRadius, classRadius, classRadius);
            }
            else
            {
                binding.CornerRadii = Vector4.zero;
            }
        }

        // The caster box the bakes are taken against. Each radius is bounded as the face painter bounds a
        // corner; RoundedFullSilhouettePlaybackTests pins the halo an unbounded --radius-full bakes away.
        private static ShadowBox BoxOf(VisualElement ve, BoxShadowBinding binding, float w, float h)
        {
            // Read again on each paint, since a layoutId projection hands the radius slots back with no geometry
            // change to re-read them on.
            ResolveCornerRadius(ve, binding);
            var bound = SilhouetteFace.MaxRadius(w, h);
            var r = binding.CornerRadii;
            var style = ve.resolvedStyle;
            return new ShadowBox(w, h,
                new Vector4(Mathf.Min(r.x, bound), Mathf.Min(r.y, bound), Mathf.Min(r.z, bound), Mathf.Min(r.w, bound)),
                new Vector4(Finite(style.borderLeftWidth), Finite(style.borderTopWidth),
                    Finite(style.borderRightWidth), Finite(style.borderBottomWidth)));
        }

        private static float Finite(float v) => float.IsNaN(v) ? 0f : v;

        // Paints the box shadow (and, for an upright caster, the repainted face) in the caster's own
        // generateVisualContent. Within each kind, the last layer is drawn first, since CSS paints the first
        // shadow of a list on top. Draw order: (1) the outer layers; then for an UPRIGHT caster, (2) an upright
        // rounded-rect fill of the captured background color, (3) the inset layers, which CSS paints above the
        // background, and (4) the border stroke. A SKEWED caster stops after (1): its SkewSilhouette repaints
        // the sheared fill, calls DrawInsetLayers, and strokes the border just after this paint. Skipped before
        // layout gives a real size.
        private static void Draw(MeshGenerationContext mgc, VisualElement ve, BoxShadowBinding binding)
        {
            var w = ve.layout.width;
            var h = ve.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }

            var (box, unscale) = LayerBox(ve, binding, w, h);

            // A skewed caster's face is owned (suppressed + repainted, sheared) by its SkewSilhouette, which
            // paints just after this callback. Painting an upright fill here would double the face and undo the
            // shear, so only paint the outer layers.
            if (binding.CasterSkewed)
            {
                DrawLayers(mgc, binding, box, inset: false, unscale);
                return;
            }

            // An element that clips its content cuts this paint at its padding box (styling-variants.md's
            // hidden-overflow table), which its border and its outer layers lie outside, and CSS clips neither. Such
            // an element's outer layers are painted on a sibling over it (FaceBorderOverlay), where they never
            // overlap the element's own box, being cut out there; it keeps its native background and solid border,
            // and a dashed or dotted border, which the native chrome cannot draw, is painted with the background
            // beneath it on that sibling too. Who owns the face is settled outside the paint, as settling it writes
            // the element's inline style and the hierarchy.
            var clips = StyleCascade.ClipsOverflow(ve);
            var native = clips && binding.BorderStyle == BorderLineStyle.Solid;
            if ((native == binding.Face.SuppressionApplied || clips != FaceBorderOverlay.Has(ve)) && !binding.FacePending)
            {
                binding.FacePending = true;
                ve.schedule.Execute(() => SettleFace(ve, binding));
            }
            if (!clips)
            {
                DrawLayers(mgc, binding, box, inset: false, unscale);
            }
            if (native && !binding.Face.SuppressionApplied)
            {
                DrawLayers(mgc, binding, box, inset: true, unscale);
                return;
            }
            RepaintUprightFill(mgc, ve, binding, w, h);
            DrawLayers(mgc, binding, box, inset: true, unscale);
            if (!clips)
            {
                RepaintUprightBorder(mgc, ve, binding, w, h);
            }
        }

        // The box the layers are baked against and the scale that draws it back onto the element's own
        // (ProjectedBox).
        private static (ShadowBox Box, Vector2 Unscale) LayerBox(VisualElement ve, BoxShadowBinding binding, float w, float h)
        {
            var box = BoxOf(ve, binding, w, h);
            var projection = binding.CasterSkewed ? null : ProjectedBox(ve, w, h);
            return projection is { } projected
                ? (projected.Box, new Vector2(1f / projected.Scale.x, 1f / projected.Scale.y))
                : (box, Vector2.one);
        }

        // Whether the element's native chrome draws its face: while it clips its content and its border is solid.
        private static bool NativeFace(VisualElement element, BoxShadowBinding binding)
            => StyleCascade.ClipsOverflow(element) && binding.BorderStyle == BorderLineStyle.Solid;

        // Gives the face to the native chrome while it draws it (NativeFace), else takes it back, and puts the border
        // of a clipping element on its overlay.
        private static void SettleFace(VisualElement ve, BoxShadowBinding binding)
        {
            binding.FacePending = false;
            if (binding.CasterSkewed || !ReferenceEquals(TryGet(ve), binding))
            {
                return;
            }
            if (NativeFace(ve, binding))
            {
                if (binding.Face.SuppressionApplied)
                {
                    binding.Face.Release(ve);
                }
            }
            else if (!binding.Face.SuppressionApplied)
            {
                binding.Face.TryStash(ve);
            }
            FaceBorderOverlay.Sync(ve, StyleCascade.ClipsOverflow(ve), (mgc, w, h) => PaintOverlay(mgc, ve, binding, w, h));
        }

        // A clipping element's outer layers, and a border the native chrome cannot draw with the background beneath it.
        private static void PaintOverlay(MeshGenerationContext mgc, VisualElement ve, BoxShadowBinding binding, float w,
            float h)
        {
            var (box, unscale) = LayerBox(ve, binding, w, h);
            DrawLayers(mgc, binding, box, inset: false, unscale);
            var shearing = new SilhouetteFace.Shearing(w, h, 0f, 0f);
            if (binding.Face.SuppressionApplied)
            {
                SilhouetteFace.PaintBand(mgc.painter2D, ve, binding.Face.CurrentBg, SilhouetteFace.BorderOf(ve, binding.Face),
                    binding.BorderStyle, shearing);
                return;
            }
            // The layers' anti-aliased cut-out edge reaches a pixel into the box, which the element's own fill covers
            // where the layers are painted with it; here the native face beneath shows it, so its outermost pixel is
            // painted over again as the native chrome draws it.
            var rs = ve.resolvedStyle;
            Color Rim(float width, Color border) => width >= 1f ? border : rs.backgroundColor;
            BorderBandPainter.Paint(mgc.painter2D, ShearedRoundedRect.ForElement(ve, 0f, w, h, 0f, 0f), new BorderSides
            {
                Left = 1f, Top = 1f, Right = 1f, Bottom = 1f,
                LeftColor = Rim(rs.borderLeftWidth, rs.borderLeftColor), TopColor = Rim(rs.borderTopWidth, rs.borderTopColor),
                RightColor = Rim(rs.borderRightWidth, rs.borderRightColor),
                BottomColor = Rim(rs.borderBottomWidth, rs.borderBottomColor),
            });
        }

        // Draws the inset layers of a skewed caster's shadow, between the fill and the border its SkewSilhouette
        // paints. A no-op for an element with no shadow or no inset layer.
        public static void DrawInsetLayers(MeshGenerationContext mgc, VisualElement ve, float w, float h)
        {
            if (s_byElement.TryGetValue(ve, out var binding) && binding.CasterSkewed)
            {
                DrawLayers(mgc, binding, BoxOf(ve, binding, w, h), inset: true, Vector2.one);
            }
        }

        // unscale maps the box the layers are baked against back onto the element's own: Vector2.one but while a
        // layoutId projection draws the element scaled differently on each axis (ProjectedBox).
        private static void DrawLayers(MeshGenerationContext mgc, BoxShadowBinding binding, in ShadowBox box, bool inset,
            Vector2 unscale)
        {
            for (var i = binding.Layers.Length - 1; i >= 0; i--)
            {
                if (binding.Layers[i].Inset == inset)
                {
                    DrawShadowQuad(mgc, binding, binding.Layers[i], box, unscale);
                }
            }
        }

        // While a layoutId projection draws the element at a different scale on each axis (ProjectedCornerFace), the
        // box as drawn on screen, its corners the projection's radii there, and that scale: the layers are baked
        // against it and drawn back at the element's own size, so the scale leaves their corners circular and their
        // offset, blur and spread as written (motion.md, where this differs from Framer's correctBoxShadow). Null
        // otherwise.
        private static (ShadowBox Box, Vector2 Scale)? ProjectedBox(VisualElement ve, float w, float h)
        {
            if (!ProjectedCornerFace.TryGetProjection(ve, out var radii, out var s))
            {
                return null;
            }
            var sw = w * s.x;
            var sh = h * s.y;
            var bound = SilhouetteFace.MaxRadius(sw, sh);
            float Corner(Vector2 r) => Mathf.Min(Mathf.Min(r.x * s.x, r.y * s.y), bound);
            var style = ve.resolvedStyle;
            return (new ShadowBox(sw, sh,
                new Vector4(Corner(radii.TopLeft), Corner(radii.TopRight), Corner(radii.BottomRight), Corner(radii.BottomLeft)),
                new Vector4(Finite(style.borderLeftWidth) * s.x, Finite(style.borderTopWidth) * s.y,
                    Finite(style.borderRightWidth) * s.x, Finite(style.borderBottomWidth) * s.y)), s);
        }

        // Draws the baked shadow silhouette quad. Returns early if the shadow color is transparent or the bake
        // is not yet available (no graphics device / shader) — the geometry callback retries.
        //
        // A fading caster needs no correction here, and a multiplier would apply the caster's opacity a SECOND
        // time and land the shadow at opacity squared; one existed and was removed. What makes that so is the
        // renderer behaviour pinned by PaintOpacityParityPlaybackTests, with its consequence on this paint
        // pinned by ShadowFadeOpacityPlaybackTests — so restoring a multiplier means refuting those rather
        // than re-deriving a measurement.
        private static void DrawShadowQuad(MeshGenerationContext mgc, BoxShadowBinding binding, in ShadowSpec layer,
            in ShadowBox box, Vector2 unscale)
        {
            var tint = layer.InheritFrom == ShadowColourFamily.None
                ? layer.Color
                : InheritedColour(mgc.visualElement, layer.InheritFrom) ?? layer.Color;
            if (float.IsNaN(tint.r))
            {
                var current = mgc.visualElement.resolvedStyle.color;
                current.a *= tint.a;
                tint = current;
            }
            if (tint.a <= 0.004f)
            {
                return;
            }

            var skew = new Vector2(binding.SkewXDeg, binding.SkewYDeg);
            var tex = BoxShadowBaker.GetOrBakeSilhouette(box, layer, skew);
            if (tex == null)
            {
                return; // no graphics device / shader yet — retry on the next geometry change
            }

            // The quad is the rect the bake's UVs span, at its real size: the texture is sized by QuadSize, which
            // clamps, so a quad at the texture's size would not match the rect the UVs span.
            var rect = BoxShadowBaker.ShadowRect(box, layer, skew);
            var minX = rect.x * unscale.x;
            var minY = rect.y * unscale.y;
            var maxX = rect.xMax * unscale.x;
            var maxY = rect.yMax * unscale.y;

            // UVs are raw 0..1 (UI Toolkit remaps them into the atlas slot); the texture's alpha carries the
            // SDF antialiasing. texUV.y is flipped (box top samples v = 1) to match the bake orientation and
            // SkewSilhouette's gradient quad, so a skewed caster's shadow slants the same direction on screen.
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

        // Repaints the UPRIGHT caster's suppressed fill, then (after the inset layers) its border, on top of the
        // outer layers, mirroring how SkewSilhouette repaints a sheared face — but with zero shear
        // (tanX = tanY = 0), so it traces the element's own rounded-rect box.
        private static void RepaintUprightFill(MeshGenerationContext mgc, VisualElement ve, BoxShadowBinding binding,
            float w, float h)
        {
            var p = mgc.painter2D;

            // Pre-stash frame (first paint racing the stash): read the live styles directly — the native chrome
            // is still visible this frame, so the overdraw is identical-colored.
            var fill = binding.Face.HasStash ? binding.Face.CurrentBg : ve.resolvedStyle.backgroundColor;
            if (fill.a > 0.004f)
            {
                SilhouetteFace.BuildShearedRoundedRect(p, ShearedRoundedRect.ForElement(ve, 0f, w, h, 0f, 0f));
                p.fillColor = fill;
                p.Fill();
            }
        }

        private static void RepaintUprightBorder(MeshGenerationContext mgc, VisualElement ve, BoxShadowBinding binding,
            float w, float h)
            => SilhouetteFace.PaintBorder(mgc.painter2D, ve, SilhouetteFace.BorderOf(ve, binding.Face), binding.BorderStyle,
                new SilhouetteFace.Shearing(w, h, 0f, 0f));
    }
}
