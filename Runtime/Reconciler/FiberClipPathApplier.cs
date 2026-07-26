using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The structural-WRAPPER layer for clip-path-* utilities. UI Toolkit (6000.3) has no USS clip-path;
    // the supported arbitrary-shape mask is an overflow-hidden element whose background-image is a VECTOR
    // image (UIR stencil-clips the subtree to the vector geometry). The wrapper carries that baked
    // VectorImage (ClipPathVectorImageBaker), so the inner element's own background, borders, text and
    // children are ALL clipped to the shape — CSS clip-path's "clips everything, including descendants"
    // semantics. Limitations vs CSS: pointer picking is unchanged (the clipped-away corners still
    // hit-test), and world-space panels (which only support rectangle clipping) ignore the mask.
    internal sealed class FiberClipPathApplier
    {
        private readonly ReconcilerContext _ctx;
        private readonly WrapperInfrastructure _wrappers;

        public FiberClipPathApplier(ReconcilerContext ctx, WrapperInfrastructure wrappers)
        {
            _ctx = ctx;
            _wrappers = wrappers;
        }

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
                    WrapperInfrastructure.UnwrapRingInPlace(_ctx, element, staleRing);
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
            var wrapper = WrapperInfrastructure.CreatePassthroughWrapper(FiberWrapperElementAppliers.ClipPathWrapperClass);
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
    }
}
