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
        // TryExtract alone is the gate; StyleClipPathClass' own remarks own why there is no cheaper one.
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
        // shadow and ring layers self-suppress). KNOWN LIMITATION: this is "a clip can apply" (base or any
        // variant), not "a clip is applied right now" — both suppressions key off this one gate, so a clip
        // VARIANT on an element that also has a base shadow-* /
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
                ScheduleLayoutReSync(element, binding);
                return true;
            }
            if (wantWrap)
            {
                // Neither the shadow nor the ring needs tearing down here: both are wrapper-less and both run
                // AFTER this pass, see the now-active clip (clipActive) and detach themselves.
                // Honor the user wrapElement opt-out on patch too.
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

        // Builds the clip wrapper around element: a layout-passthrough container that additionally
        // hides overflow and carries the baked
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
            // The wrapper's box follows the parent, and a parent change that leaves the inner's own box where it
            // was (a row becoming a column around an element with no width) raises no event on the inner. Never
            // unregistered: the wrapper leaves with its binding and is not reused.
            wrapper.RegisterCallback(binding.OnGeometry);

            _ctx.ClipPathBindings[element] = binding;
            _ctx.WrapperToInnerMap[wrapper] = element;

            // Off-panel / pre-layout the size is unknown (NaN) and the sync no-ops; on a patch-time
            // wrap of an already-laid-out element it bakes immediately. element.layout still holds stale
            // OLD-parent coordinates until the next layout pass, so the anchor must not read it here (a
            // (100,50) card would otherwise show its mask offset by (100,50) for one frame); that pass's
            // GeometryChangedEvent re-anchors it at the inner's real place in the wrapper.
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

        // Keeps the mask tracking its target: lays the wrapper out, forwards the inner's flex to it and (re)bakes
        // the vector shape at the inner's resolved box. The baked
        // VectorImage stores TIGHT bounds, so the background is explicitly positioned and sized by
        // the analytic path bounds, anchored at the inner's layout origin within the wrapper.
        // innerAtWrapperOrigin: true on the wrap-time call, when element.layout still holds
        // OLD-parent coordinates, so the anchor takes the wrapper's origin until the next layout pass
        // (whose GeometryChangedEvent re-anchors with real coordinates).
        private static void SyncClipPathGeometry(VisualElement element, ClipPathBinding binding,
            bool innerAtWrapperOrigin = false)
        {
            SyncWrapperLayout(element, binding);
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

            // The inner need not sit at the wrapper's origin — its margins, or an out-of-flow inner's edge
            // offsets, place it inside — so the background follows the inner's layout origin.
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

        // CSS clip-path is paint-only, so the wrapper has to leave the inner where the parent would have put it.
        // In flow the parent lays out the wrapper in the inner's place: the wrapper takes the parent's
        // flex-direction, so the inner's flex-grow acts along the same axis inside it, and the inner's
        // align-self, so the parent aligns the wrapper as it would have aligned the inner. Out of flow the inner
        // resolves its edge offsets against the wrapper, so the wrapper leaves the flow, spans the real parent
        // (inset 0) and takes the parent's justify-content and align-items, which place an inner with no offset
        // on an axis. ClipPathWrapperFlowParityPanelTests compares the result against an unclipped twin.
        // Position and offsets are written only when the inner changes mode or something has cleared the
        // position: a PopLayout exit pins the wrapper (GeneralPathReconciler.PinExitingChildOutOfFlow), a sync
        // must leave that pin alone, and cancelling the exit clears it.
        // KNOWN LIMITATION: in flow the wrapper takes its size from the inner along the parent's main axis, and
        // along the cross axis unless the parent stretches it. On such an axis a percentage width, height or
        // flex-basis resolves against the wrapper rather than the parent, and auto margins centre nothing. A
        // size a parent's manipulator writes on the slot — a grid-cols-* column width, a V.VirtualList row
        // height — lands on the wrapper while the inner keeps its own. An out-of-flow inner's wrapper spans the
        // parent with overflow hidden, so whatever of the inner lies outside the parent's box is cut, where CSS
        // clips only to the shape; and it reads the parent's justify-content and align-items again only at a
        // sync, which a change to the parent's alignment alone does not start.
        private static void SyncWrapperLayout(VisualElement element, ClipPathBinding binding)
        {
            var ws = binding.Wrapper.style;
            var parentStyle = binding.Wrapper.parent?.resolvedStyle;
            var outOfFlow = StyleOutOfFlowChild.IsOutOfFlow(element);
            ws.flexDirection = parentStyle != null ? parentStyle.flexDirection : StyleKeyword.Null;
            if (binding.WrapperOutOfFlow != outOfFlow || ws.position.keyword == StyleKeyword.Null)
            {
                binding.WrapperOutOfFlow = outOfFlow;
                ws.position = outOfFlow ? Position.Absolute : Position.Relative;
                var offset = outOfFlow ? new StyleLength(0f) : new StyleLength(StyleKeyword.Null);
                ws.left = offset;
                ws.top = offset;
                ws.right = offset;
                ws.bottom = offset;
            }
            if (outOfFlow)
            {
                ws.alignSelf = StyleKeyword.Null;
                ws.justifyContent = parentStyle != null ? parentStyle.justifyContent : StyleKeyword.Null;
                ws.alignItems = parentStyle != null ? parentStyle.alignItems : StyleKeyword.Null;
                return;
            }
            ws.justifyContent = StyleKeyword.Null;
            ws.alignItems = StyleKeyword.Null;
            ws.alignSelf = element.resolvedStyle.alignSelf;
        }

        // A patch or a variant toggle can move the inner (self-end, grow) without changing the box of either
        // element, so no geometry event follows, and resolvedStyle still holds the previous values while the
        // patch or toggle runs. The re-sync is therefore left to the panel scheduler's next tick, and any layout
        // pass before that tick keeps the old values. A patch that wraps the element needs none of this: the
        // fresh wrapper's first layout raises a geometry event, which the same-patch case in
        // ClipPathWrapperFlowParityPanelTests relies on.
        internal void ScheduleLayoutReSync(VisualElement element)
        {
            if (_ctx.ClipPathBindings.TryGetValue(element, out var binding))
            {
                ScheduleLayoutReSync(element, binding);
            }
        }

        private static void ScheduleLayoutReSync(VisualElement element, ClipPathBinding binding)
            => binding.Wrapper.schedule.Execute(() => SyncClipPathGeometry(element, binding));

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
