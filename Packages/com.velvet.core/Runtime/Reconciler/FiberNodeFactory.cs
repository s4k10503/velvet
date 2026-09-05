using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // VNode → VisualElement creation.
    internal sealed class FiberNodeFactory
    {
        private readonly ReconcilerContext _ctx;
        private readonly FiberNodePatcher _patcher;
        private IReconcilerHost _host = null!;

        // The reserved key prefix for unkeyed AnimatePresence children (BuildKeyedMapCopy). Internal so
        // FiberContextSpine can replicate the same keying when descending into a DOM-less AnimatePresence
        // to reconstruct context for a wrapper-hosted descendant's isolated re-render.
        internal const string AutoKeyPrefix = "__ap_auto_";

        // USS class added to OutletNode's container so tests and consumers can
        // distinguish it from the generic layout anchors (all of which use
        // PickingMode.Ignore so the anchor never intercepts pointer events).
        internal const string OutletContainerClass = "velvet-outlet";

        // USS class added to the wrapper VisualElement emitted for a
        // ContextProviderNode. Mirrors OutletContainerClass's role
        // for OutletNode: the class lets tests and consumers identify Provider boundaries in the DOM.
        internal const string ContextProviderClassName = "velvet-context-provider";

        public FiberNodeFactory(ReconcilerContext ctx, FiberNodePatcher patcher)
        {
            _ctx = ctx;
            _patcher = patcher;
        }

        internal void SetHost(IReconcilerHost host)
        {
            if (_host != null)
            {
                throw new System.InvalidOperationException("[FiberNodeFactory] SetHost called twice");
            }
            _host = host;
        }

        public VisualElement CreateElement(VNode? node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            switch (node)
            {
                case ElementNode elementNode:
                    return CreateForElementNode(elementNode);
                case MotionNode motionNode:
                    return CreateForMotionNode(motionNode);
                case AnimatePresenceNode:
                    // AnimatePresence is DOM-less: it never becomes a single element.
                    // GeneralPathReconciler.ExpandAnimatePresenceInline expands its keyed children directly into
                    // the parent's slot range, so CreateElement is never invoked on it.
                    throw new System.InvalidOperationException(
                        "[FiberNodeFactory] AnimatePresenceNode is DOM-less and must be inline-expanded, not created as an element.");
                case PortalNode portalNode:
                    return CreateForPortalNode(portalNode);
                case WorldSpaceNode worldSpaceNode:
                    // The same deferred-mount flow as PortalNode, with a per-instance world-space
                    // host created at drain time (see DrainPendingPortalMounts).
                    return EnqueueDeferredHostMount(worldSpaceNode, null);
                case VirtualListNode virtualListNode:
                    return CreateForVirtualListNode(virtualListNode);
                case TextNode textNode:
                    return CreateForTextNode(textNode);
                case ComponentNode componentNode:
                    return CreateForComponentNode(componentNode);
                case ContextProviderNode providerNode:
                    return CreateForContextProviderNode(providerNode);
                case OutletNode outletNode:
                    return CreateForOutletNode(outletNode);
                default:
                    // Unknown VNode type: FragmentNode (which should have been expanded by the parent),
                    // null, or a missing branch for a newly added VNode type. Log a warning for debuggability.
                    FiberLogger.LogWarning("FiberNodeFactory",
                        $"Unsupported VNode type: {node?.GetType().Name ?? "null"}. Returning empty VisualElement.");
                    return new VisualElement();
            }
        }

        private VisualElement CreateForElementNode(ElementNode elementNode)
        {
            var element = _ctx.FiberElementFactory.Create(elementNode);
            // Stamps the ComponentFiber whose Body is mid-render right now (the element's
            // logical owner) onto the reserved userData slot — reset to null on pool reuse
            // (FiberElementPoolReset.ResetCommonState) and otherwise never written by Velvet.
            // This is the only reverse index from a native VisualElement back to the logical
            // fiber tree; cross-panel synthetic event dispatch (V.Portal(layer:)/V.WorldSpace)
            // walks it to climb ComponentFiber.Parent from wherever a pointer/focus event
            // physically landed. Null at the true tree root (nothing on FiberStack yet).
            element.userData = _ctx.FiberStack.Current;
            CaptureOwnRawText(element, elementNode.Props);
            if (elementNode.Children != null)
            {
                var childContainer = FiberNodePatcher.GetChildContainer(element);
                // ReconcileChildren (= GeneralPathReconciler.ExpandInlineRecursive) inline-expands
                // ComponentNode / ContextProviderNode / FragmentNode so children appear as
                // direct siblings under <paramref name="childContainer"/>. These node kinds
                // produce no DOM element of their own. The same path is used
                // on patch (PatchCommon) so initial-mount and patch DOM layouts stay
                // symmetric — both omit the per-Component wrapper VE.
                _host.ReconcileChildren(childContainer, Array.Empty<VNode>(), elementNode.Children);
            }
            try
            {
                elementNode.OnCreated?.Invoke(element);
            }
            catch (Exception exception)
            {
                ReconcilerContext.ContainUserCallbackFailure(_ctx.FiberStack.Current, exception);
            }
            _ctx.SyncRefCallback(element, elementNode.RefCallback);
            _patcher.Appliers.ApplyGestureManipulator(element, elementNode.WhileHoverClass, elementNode.WhileTapClass, elementNode.WhileFocusClass);
            _patcher.ApplyVariantManipulators(element, elementNode.ClassNames);
            // After ApplyVariantManipulators (which registers the data-/aria- variant rules): seed the
            // attribute store from the props and evaluate, so a data-[..]/aria-[..] variant lights from
            // the element's carried attribute values at mount.
            _patcher.ApplyAttributes(element, elementNode.Props);
            _patcher.ApplyFontLayerOnCreate(element, elementNode.ClassNames);
            // After ReconcileChildren so the gap / divide manipulators see the final child list.
            // [&>*]: runs before gap / divide / grid so those win a shared child edge (see
            // ApplyPostChildrenClassPasses for the same ordering on the patch path).
            _patcher.ApplyChildVariantManipulator(element, elementNode.ClassNames);
            _patcher.ApplyLayoutManipulators(element, elementNode.ClassNames);
            // Same post-children timing: structural variants (first:/last:/odd:/…) need the placed children.
            _patcher.ApplyStructuralVariants(element);
            // has-[.class]: (element as subject) likewise needs the placed children to scan.
            _patcher.ApplyHasClassVariants(element);
            // has-[:checked]: / has-[:focus]: re-scan: an already-checked descendant mounted under this
            // element fires no ChangeEvent, so re-derive the manipulator from the placed children.
            _patcher.ApplyHasVariantManipulators(element);
            // text-transform / -decoration cascade: after children are placed so it can reach descendant
            // text leaves, and after the element's own text is set so it transforms the final value.
            _patcher.ApplyTextEffects(element, elementNode.ClassNames);

            // The paint layers read a class source that also carries what the passes above have already
            // written onto the live class list, so a payload ALREADY LIT at this point paints from the
            // first frame instead of waiting for an unrelated re-render to bring the token in
            // literally. That means the families resolved from this element's own placed subtree a few
            // lines up — has-[.class]:shadow-lg over a matching child, structural, data-/aria-,
            // supports- — and not dark: or md:, which are still off while the element is detached and
            // arrive later through attach.
            var paintClasses = _patcher.ResolveVariantClassesOnCreate(
                element, elementNode.ClassNames, paintTail: true);
            // Skew is wrapper-less (the sheared silhouette is the element's own painted
            // content), so it attaches before — and composes with — any wrap layer below,
            // including a user wrapElement.
            _patcher.Appliers.ApplySkewOnCreate(element, paintClasses);
            // Gradient is also wrapper-less (baked texture set as the element's own
            // background-image, clipped to its border-radius), so it attaches on the element too.
            _patcher.Appliers.ApplyGradientOnCreate(element, paintClasses);
            // animate-* motion (gradient pan / hue cycle) drives the element's own inline style; runs
            // after the gradient so a pan mode sees the baked gradient already applied.
            _patcher.Appliers.ApplyAnimateOnCreate(element, paintClasses);
            // transition-filter: register the tween binding so a later filter change animates.
            // The mount's own filter is already applied instantly above (the binding is not enabled
            // yet), matching CSS's no-transition-on-initial-value.
            _patcher.Appliers.ApplyFilterTransitionOnCreate(element, paintClasses);
            // Drop shadow is wrapper-less too (the baked shadow texture is painted behind the
            // element's own content, bleeding outside the box) — a non-structural paint like CSS
            // box-shadow, so it composes with any wrap layer below and a user wrapElement. The paint
            // self-suppresses while an active clip-path-* is present (clip-path clips the box-shadow).
            _patcher.Appliers.ApplyShadowOnCreate(element, paintClasses);
            // border-dashed / border-dotted: another wrapper-less paint (the dashed outline is the
            // element's own generateVisualContent; only the border color is suppressed). Attaches after
            // skew / shadow so it can defer to whichever owns the face. ElementNode only — a Motion never
            // renders this silhouette (mirroring skew's own silent Motion exclusion).
            _patcher.Appliers.ApplyBorderStyleOnCreate(element, paintClasses);
            // ring-* / outline-*: the band is a native-border overlay hosted as a reconciler-invisible
            // SIBLING of this element (RingOverlay), so nothing is added to the element's own slot and it
            // composes with every layer above and with a user wrapElement. The element has no parent yet at
            // this point; the overlay places itself when the reconcile boundary drains.
            _patcher.Appliers.ApplyRingOnCreate(element, paintClasses);
            ApplyOptionalCreateBindings(element, elementNode.Props, elementNode.ClassNames);

            VisualElement outer;
            if (elementNode.WrapElement != null)
            {
                VisualElement? wrapper = null;
                try
                {
                    wrapper = elementNode.WrapElement(element);
                }
                catch (Exception exception)
                {
                    // The element itself takes the slot when the wrap fails. Leaving the throw here
                    // left the slot empty instead, with the element fully built and its ref already
                    // queued against it by the sync above.
                    ReconcilerContext.ContainUserCallbackFailure(_ctx.FiberStack.Current, exception);
                }
                if (wrapper != null && wrapper != element)
                {
                    _ctx.WrapperToInnerMap[wrapper] = element;
                    outer = wrapper;
                }
                else
                {
                    outer = element;
                }
            }
            else
            {
                // No explicit wrapElement: a clip-path-* class auto-wraps the element in a stencil-
                // masking container. It is the only remaining structural wrapper — the shadow and ring
                // layers are wrapper-less and attached above, and both self-suppress on an active clip
                // (CSS clip-path clips a box-shadow and an outline alike).
                outer = _patcher.Appliers.ApplyClipPathOnCreate(element, elementNode.ClassNames);
            }

            // z-* scope gate: only an ALSO-absolute element with an explicit z-* class routes into a
            // layer container; everything else (the overwhelming majority) returns `outer` unchanged
            // at the cost of one cheap prefix scan. Gated on the OUTERMOST element — a clip-path-*/
            // ring/wrapElement wrapper is what physically occupies the slot, so it (not the inner
            // `element`) is what must relocate.
            if (FiberZLayerCoordinator.TryClassify(elementNode.ClassNames, elementNode.Props, out var resolvedZ))
            {
                return FiberZLayerCoordinator.EnqueueMount(_ctx, outer, resolvedZ);
            }
            return outer;
        }

        // Runs before either create path's text-effect pass: that pass rewrites the element's displayed text
        // from the raw value tracked here, and the element factory has just written the Text prop straight
        // onto the element. FiberNodePatcher.PatchBaseElement is the same seam on the patch side.
        private void CaptureOwnRawText(VisualElement element, FiberElementProps? props)
        {
            if (element is TextElement && props?.Text != null)
            {
                StyleTextEffectResolver.CaptureRaw(_ctx, element, props.Text);
            }
        }

        private VisualElement CreateForMotionNode(MotionNode motionNode)
        {
            // Resolve the applied classes against the effective label (own Animate, else the nearest
            // ancestor Motion's label read from MotionContext) — the variant-inheritance model.
            var motionAmbient = _ctx.ComponentContextStack.Get(MotionContext.ActiveLabel);
            // The create path plays no swap between two poses (the element is built already carrying the
            // resting variant), so the resolved variant's own transition is not read here — the mount enter
            // further down resolves the one for its OWN target label instead.
            var appliedClasses = MotionVariantResolver.ResolveApplied(motionNode, motionAmbient,
                out var variantClasses, out _);
            var element = _ctx.FiberElementFactory.CreateMotion(motionNode, appliedClasses);
            // The presence expansion dispatches this anchor Motion's variant enter/exit against the
            // Motion's OWN element (the resting variant classes live here, not on a wrapper) — record
            // it for the expansion that is emitting this keyed child right now.
            if (ReferenceEquals(motionNode, _ctx.PresenceAnchorMotion))
            {
                _ctx.PresenceAnchorMotionElement = element;
            }
            // See CreateForElementNode's comment on this same assignment (reserved userData
            // slot for cross-panel synthetic event dispatch's VE-to-logical-fiber reverse index).
            element.userData = _ctx.FiberStack.Current;
            // Only record applied-class bookkeeping when a variant actually merged; the variant-less
            // majority needs no entry (patch falls back to oldNode.ClassNames for the diff baseline).
            if (variantClasses.Length > 0)
            {
                _ctx.MotionAppliedClasses[element] = new MotionAppliedClassSet(appliedClasses, variantClasses);
            }
            // Record the label propagated to children now (regardless of whether this Motion currently
            // has any) so the FIRST patch on this element has an accurate baseline: PatchMotion diffs
            // against this stored value to detect an ACTUAL label change before it (re-)triggers
            // staggerChildren/delayChildren orchestration — without seeding it here, that first patch
            // would see no previous entry and could misfire even when the label held steady across
            // mount and the first re-render. Orchestration itself only ever starts from a PATCH-time
            // label change (see FiberNodePatcher.PatchMotion), never on mount. A null childLabel needs no
            // removal here: a brand-new element was never in this map, and a pooled one already had its
            // entry cleared by ReconcilerContext.ClearElementSideTables when it was returned (see
            // MotionChildLabel's own doc).
            var childLabel = MotionVariantResolver.LabelForChildren(motionNode, motionAmbient);
            if (childLabel != null)
            {
                _ctx.MotionChildLabel[element] = childLabel;
            }
            CaptureOwnRawText(element, motionNode.Props);
            if (motionNode.Children != null)
            {
                var childContainer = FiberNodePatcher.GetChildContainer(element);
                // Provide this Motion's active label to its descendants while their subtree reconciles
                // (same ComponentContextStack the Router/Outlet ambient values ride on). Skip the
                // stack round-trip entirely when there is no label to propagate (the common case).
                if (childLabel != null)
                {
                    _ctx.ComponentContextStack.Push(MotionContext.ActiveLabel, childLabel);
                    try
                    {
                        _host.ReconcileChildren(childContainer, Array.Empty<VNode>(), motionNode.Children);
                    }
                    finally
                    {
                        _ctx.ComponentContextStack.Pop(MotionContext.ActiveLabel);
                    }
                }
                else
                {
                    _host.ReconcileChildren(childContainer, Array.Empty<VNode>(), motionNode.Children);
                }
            }
            _ctx.SyncRefCallback(element, motionNode.RefCallback);
            _patcher.Appliers.ApplyGestureManipulator(element, motionNode.WhileHoverClass, motionNode.WhileTapClass, motionNode.WhileFocusClass);
            _patcher.ApplyVariantManipulators(element, appliedClasses);
            _patcher.ApplyAttributes(element, motionNode.Props);
            ApplyOptionalCreateBindings(element, motionNode.Props, appliedClasses);
            _patcher.ApplyFontLayerOnCreate(element, appliedClasses);
            _patcher.ApplyChildVariantManipulator(element, appliedClasses);
            _patcher.ApplyLayoutManipulators(element, appliedClasses);
            _patcher.ApplyStructuralVariants(element);
            _patcher.ApplyHasClassVariants(element);
            _patcher.ApplyHasVariantManipulators(element);
            _patcher.ApplyTextEffects(element, appliedClasses);
            // Same composed source as the element path, recorded as a NON-paint-tail element so a
            // later variant re-sync never attaches to a Motion the three silhouette paints its own
            // patch would refuse.
            var motionPaintClasses = _patcher.ResolveVariantClassesOnCreate(
                element, appliedClasses, paintTail: false);
            _patcher.Appliers.ApplyGradientOnCreate(element, motionPaintClasses);
            _patcher.Appliers.ApplyAnimateOnCreate(element, motionPaintClasses);
            // transition-filter on a Motion host: a Motion can carry filter utilities + that class
            // just like a plain element, so register the tween binding here too.
            _patcher.Appliers.ApplyFilterTransitionOnCreate(element, motionPaintClasses);
            WarnIgnoredMotionUtilities(motionNode, appliedClasses);
            // Standalone `initial` enter: outside AnimatePresence this Motion still plays its own
            // mount animation, the same variant enter the presence expansion drives
            // (GeneralPathReconciler.ExpandAnimatePresenceInline) — just with no stagger (there is no
            // AnimatePresence boundary to stagger against). The element above was created carrying the
            // resting variants[animate] classes (appliedClasses), with MotionAppliedClasses already
            // recorded against that resting state, so PlayVariantEnter's synchronous strip-to-`initial` is
            // purely a transient visual state: a later patch (PatchMotion) always diffs against the
            // resting baseline and never replays this entrance.
            // Gated on IDENTITY, not PresenceExpansionDepth: the presence expansion drives an enter for
            // only its ONE resolved anchor Motion (PresenceAnchorMotion, set by GeneralPathReconciler
            // around the exact EmitPresenceChild call whose enter/exit it dispatches explicitly) — every
            // OTHER Motion created while that expansion is on the stack (nested deeper, sitting under a
            // non-anchor wrapper — e.g. a plain Div — or simply a sibling keyed child) is not presence-
            // managed at all and must keep this mount enter, or wrapping unrelated content in
            // AnimatePresence would silently disable it.
            if (!ReferenceEquals(motionNode, _ctx.PresenceAnchorMotion) && motionNode.Initial != null)
            {
                if (GeneralPathReconciler.TryResolveVariantInitial(
                        motionNode, out var standaloneFromClasses, out var standaloneToClasses,
                        out var standaloneTransition)
                    && standaloneTransition != null)
                {
                    // Contained on the same terms the presence expansion's own enters are, and attributed
                    // to the component whose render reached this create — the owner SyncRefCallback reads
                    // for the same element, captured here because the callback can fire frames later.
                    _ctx.StyleAnimationScheduler.PlayVariantEnter(element, standaloneFromClasses, standaloneToClasses,
                        standaloneTransition,
                        GeneralPathReconciler.ContainedEnterComplete(motionNode, _ctx.FiberStack.Current));
                }
                else
                {
                    // Initial declared but unresolvable: no own Animate (an inherited-label
                    // configuration is not yet driven by the standalone enter), the label is missing
                    // from Variants / maps to an empty class, or neither the target variant nor this
                    // Motion carries a transition to play on. Warn instead of silently mounting inert,
                    // matching the Exit gate's own inert-configuration diagnostic in
                    // WarnIgnoredMotionUtilities.
                    FiberLogger.LogWarning("Motion",
                        "initial is set but has no resolvable enter: this Motion needs its own animate + "
                        + "variants (with initial mapping to a non-empty class) for a standalone mount "
                        + "enter. An inherited animate label does not yet drive one.");
                }
            }
            // Shared-element layout animation (layoutId) on a freshly-created element —
            // the same-key-type-flip case PatchMotion's own registration cannot reach (a type
            // flip tears down the OLD element and creates a genuinely NEW one for the SAME id,
            // never routing through PatchMotion at all). MotionLayoutIdDriver.OnPatched already
            // handles "new physical element, existing registry entry" by falling back to the
            // registry's own stored rect instead of this element's own (nonexistent) layout
            // history — see its own comment.
            if (motionNode.LayoutId != null)
            {
                var lt = motionNode.Transition;
                MotionLayoutIdDriver.OnPatched(element, motionNode.LayoutId,
                    lt?.Stiffness ?? 100f, lt?.Damping ?? 10f, lt?.Mass ?? 1f, _ctx);
            }
            return element;
        }

        // The utilities a Motion host silently cannot honour. Each is diagnosed rather than applied,
        // because the class reaches this element through the ordinary utility pipeline and a silent drop
        // reads as a broken utility.
        private void WarnIgnoredMotionUtilities(MotionNode motionNode, string[] appliedClasses)
        {
            // Motion does NOT paint a drop shadow: the three silhouette paints stand down on a Motion
            // on BOTH halves — here, and on the patch path through ApplyResolvedClassPasses' paintTail
            // gate — so attaching one here would leave a binding the Motion's own patch never syncs
            // against a class change and never detaches. The shadow belongs on a Div the Motion wraps
            // (the Div carries the shadow, the Motion carries the transition). Warn and skip the paint.
            // Warn only for an ACTIVE shadow (shadow-none deliberately cancelling the cascade is not
            // "ignored" — nothing would render anywhere).
            if (StyleShadowClass.HasShadowClass(appliedClasses)
                && StyleShadowClass.TryExtract(appliedClasses, out _))
            {
                FiberLogger.LogWarning("Motion",
                    "A shadow-* utility on a Motion is ignored: a Motion carries the transition, not "
                    + "the paint layers. Wrap the Motion around a shadowed Div instead.");
            }
            // ring-* / outline-* is ignored on a Motion because the band is a SIBLING placed from the
            // element's LAYOUT box, and UI Toolkit composites a transform onto the transformed element's own
            // subtree only — so a Motion animating translate / scale / rotate slides out from under its own
            // band and leaves it behind for the whole play. Both halves of that are pinned by
            // RingOverlayTests' transform pair. On a Div the Motion wraps, the band is IN the Motion's
            // subtree and rides its transform, which is what the advice below buys.
            // Rejected: warning only for a Motion whose transition declares a transform channel. layoutId, the
            // gesture class channels and a later Transition swap each introduce one without recreating the
            // element, and this gate runs once, at create.
            // Same active-only gate as the shadow above.
            if (StyleRingClass.HasRingClass(appliedClasses)
                && StyleRingClass.TryExtract(appliedClasses, out _))
            {
                FiberLogger.LogWarning("Motion",
                    "A ring-* / outline-* utility on a Motion is ignored: the band is placed from the "
                    + "element's laid-out box, which a Motion's transform does not move, so a slide / scale / "
                    + "layoutId play would leave it behind. Wrap the Motion around a ringed Div instead.");
            }
            // clip-path-* is a structural wrapper, which would become the AnimatePresence anchor while
            // the enter/exit transition stays on the inner Motion: ignored on a Motion, never wrapped.
            // Same active-only gate: clip-path-none / an unparseable value activates nothing.
            if (StyleClipPathClass.WantsClipPath(appliedClasses))
            {
                FiberLogger.LogWarning("Motion",
                    "A clip-path-* utility on a Motion is ignored: it would break AnimatePresence enter/exit "
                    + "(same constraint as shadow-*). Wrap the Motion around a clipped Div instead.");
            }
            // z-* is ignored on a Motion: the Motion create path never consults FiberZLayerCoordinator at
            // all (only CreateForElementNode does), so a Motion never relocates into a layer container
            // — TryClassify's out-of-flow half runs off the declared class list / Anchored prop alone
            // (no live element needed), so it can be evaluated here for diagnostics purposes even though
            // that path never acts on it.
            if (FiberZLayerCoordinator.TryClassify(appliedClasses, motionNode.Props, out _))
            {
                FiberLogger.LogWarning("Motion",
                    "A z-* utility on a Motion is ignored: z-* does not apply to Motion elements. "
                    + "Wrap the Motion around a z-managed Div instead.");
            }
            // Exit tweens are scheduled only by the AnimatePresence expansion — something has to defer
            // the unmount for a removal to animate against, and AnimatePresence is what does that — so
            // exit outside one is genuinely inert. Initial is NOT warned here (see the standalone enter
            // in CreateForMotionNode): unlike exit, a mount-time enter needs no deferred unmount to play
            // against, so it works on any Motion (initial/animate apply anywhere; only exit is
            // AnimatePresence-only).
            if (_ctx.PresenceExpansionDepth == 0 && motionNode.Exit != null)
            {
                FiberLogger.LogWarning("Motion",
                    "exit on a Motion outside AnimatePresence is inert: exit tweens are driven by the "
                    + "AnimatePresence expansion. Wrap the Motion in V.AnimatePresence (or drop exit).");
            }
        }

        private VisualElement CreateForPortalNode(PortalNode portalNode)
        {
            // TargetId, Layer and TargetElement are a one-of triple; a hand-built node violating
            // that has no meaningful routing, so it warns and mounts an inert placeholder rather
            // than silently picking a side.
            var hasTargetId = !string.IsNullOrEmpty(portalNode.TargetId);
            var hasLayer = portalNode.Layer != null;
            var hasElement = portalNode.TargetElement != null;
            var kinds = (hasTargetId ? 1 : 0) + (hasLayer ? 1 : 0) + (hasElement ? 1 : 0);
            if (kinds != 1)
            {
                FiberLogger.LogWarning("Portal",
                    "A PortalNode must set exactly one of TargetId, Layer or TargetElement (they are a one-of triple). Children will not be rendered.");
                return CreateHiddenPlaceholder();
            }

            // A layer portal resolves its target at DRAIN time — the framework layer host is
            // created lazily there, once the placeholder is attached and the declaring panel
            // is therefore known. The other two resolve here, keeping the registry portal's
            // not-registered warning a mount-time signal.
            VisualElement? target = null;
            if (hasElement)
            {
                // Already resolved by the caller. Nothing can be missing and nothing heals later:
                // a container that changes is a different portal, which ReconcileKeying.CanPatch
                // turns into a remount rather than a patch.
                target = portalNode.TargetElement;
            }
            else if (!hasLayer)
            {
                target = FiberPortalRegistry.Get(portalNode.TargetId!);
                if (target == null)
                {
                    FiberLogger.LogWarning("Portal", $"Target \"{portalNode.TargetId}\" is not registered. Children will not be rendered.");
                    var placeholder = CreateHiddenPlaceholder();
                    _ctx.PortalState[placeholder] = new PortalSlotInfo(
                        null, 0, 0, portalNode.TargetId, _ctx.FiberStack.Current);
                    return placeholder;
                }
            }

            // Defer the target-side mount to the post-reconcile drain so this Portal's
            // slot range does not overlap with an outer Portal's slot when both target
            // the same DOM node. Synchronous mount would let inner Portal write into the
            // outer's slot range before outer's slotLength is finalized, leaving every
            // nested slot index stale after the outer's placeholder insertion. The drain
            // mounts each queued Portal at a fresh slotStart = target.childCount once
            // outer reconcile has finished, so Portal subtrees stack as
            // independent ranges (slots [outer..outerEnd) then [outerEnd..innerEnd) ...).
            // PortalState is recorded only at drain time — between enqueue and drain the
            // placeholder has no entry and PatchPortal/CleanupPortal handle the missing
            // case explicitly (LogError + skip / early return).
            return EnqueueDeferredHostMount(portalNode, target);
        }

        private VisualElement CreateForVirtualListNode(VirtualListNode virtualListNode)
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            var bridge = _ctx.ReconcilerBridge;
            // Capture the host fiber rendering this list and the live cursor (correct mid-reconcile, in
            // this commit walk) so the controller can mount items under the host's shared context — its
            // items render later, outside any reconcile pass, where the cursor is empty.
            var controller = new FiberVirtualListController(
                scrollView, virtualListNode, bridge, _ctx.FiberStack.Current, _ctx.ComponentContextStack);
            _ctx.VirtualListControllers[scrollView] = controller;

            // Apply class-driven styling the same way the ElementNode path does, so a virtualized
            // list container honours variants and the font layer. Gap is intentionally omitted: a
            // ScrollView's direct children are the height spacer + absolutely-positioned visible
            // container, not the list items, so gap-* would have nothing meaningful to space.
            _patcher.ApplyVariantManipulators(scrollView, virtualListNode.ClassNames);
            _patcher.ApplyFontLayerOnCreate(scrollView, virtualListNode.ClassNames);
            return scrollView;
        }

        private VisualElement CreateForTextNode(TextNode textNode)
        {
            var label = _ctx.FiberElementFactory.CreateText(textNode);
            // Capture the raw text (and apply any already-resolved ancestor effect) so a text-transform /
            // -decoration carried by an ANCESTOR cascades onto this leaf — a TextNode has no class of its
            // own. At mount the ancestor's own effect is parsed in its later post-children pass, which
            // re-applies; OnTextSet here makes an isolated later leaf re-render self-sufficient.
            StyleTextEffectResolver.OnTextSet(_ctx, label, textNode.Text);
            return label;
        }

        private VisualElement CreateForComponentNode(ComponentNode componentNode)
        {
            // Wrapper-mount path, reached from a VirtualList item and nothing else: the item renderer's
            // return goes to IReconcilerBridge.CreateElementForController unexpanded, while a reconcile
            // rules a ComponentNode out on both of its paths — the fast one runs only where
            // GeneralPathReconciler.NeedsExpansion found none, and the general one expands each one it
            // reaches, a Memo's resolved inner and an AnimatePresence keyed entry included.
            // A Component does not emit a DOM element; its rendered tree attaches
            // directly to the parent. Velvet needs an anchor element for fiber tracking, and
            // CreateLayoutAnchor owns how that anchor takes part in layout.
            var wrapper = CreateLayoutAnchor();
            _patcher.HandleComponentMount(wrapper, componentNode);
            return wrapper;
        }

        private VisualElement CreateForContextProviderNode(ContextProviderNode providerNode)
        {
            // A context Provider emits no DOM element of its own; descendants attach directly to
            // the parent fiber's host. This path is asked for an element regardless, so an anchor
            // stands in for the Provider.
            // Reached the one way CreateForComponentNode above is, and ruled out on the reconcile
            // paths for the same reason.
            var container = CreateLayoutAnchor();
            container.AddToClassList(ContextProviderClassName);

            providerNode.PushContext(_ctx.ComponentContextStack);
            try
            {
                if (providerNode.Children != null)
                {
                    _host.ReconcileChildren(container, Array.Empty<VNode>(), providerNode.Children);
                }
            }
            finally
            {
                providerNode.PopContext(_ctx.ComponentContextStack);
            }
            return container;
        }

        private VisualElement CreateForOutletNode(OutletNode outletNode)
        {
            // One wrapper, not two: the container doubles as the fiber anchor for the matched route's
            // Component. CreateLayoutAnchor owns how it takes part in layout.
            var container = CreateLayoutAnchor();
            container.AddToClassList(OutletContainerClass);
            // Identity-side registration for FiberContextSpine: separate from the USS class
            // (which is for styling and is user-mutable). Populated unconditionally so the
            // spine walker can identify Outlet hosts before Router setup completes.
            _ctx.OutletContainers.Add(container);

            if (!_patcher.ResolveOutletMatch(out var routeElement, out var routeDepth, out var match))
            {
                return container;
            }

            outletNode.Scope = FiberOutletScope.CreateOutletScope(_ctx, match.Route, container);

            // Mount the matched route Component with Depth+1 pushed live so its UseContext
            // reads the incremented router depth: an Outlet provides the
            // next RouteContext value to its descendants. The Outlet's context value (if any) is
            // pushed too so the child route can read it via Hooks.UseOutletContext.
            _ctx.ComponentContextStack.Push(RouterContext.Depth, routeDepth);
            _ctx.ComponentContextStack.Push(RouterContext.OutletContext, outletNode.OutletContextValue);
            try
            {
                _patcher.HandleComponentMount(container, routeElement);
            }
            finally
            {
                _ctx.ComponentContextStack.Pop(RouterContext.OutletContext);
                _ctx.ComponentContextStack.Pop(RouterContext.Depth);
            }

            return container;
        }

        // The optional bindings shared by ElementNode and MotionNode's create paths — a Motion can host
        // any element type, so each of these must attach on its create path exactly like the plain
        // element path. classNames is the classes actually applied to element (the declared ClassNames
        // for a plain element, or the variant-resolved appliedClasses for a Motion), since
        // ApplyParticlesSpacer reads the utility classes off it.
        private void ApplyOptionalCreateBindings(VisualElement element, FiberElementProps? props, string[] classNames)
        {
            // SceneView (V.SceneView): wire the camera-output binding. The element has no panel
            // yet, so the first real texture sync runs from the binding's geometry callback once
            // layout settles; later camera swaps arrive through the props diff.
            if (props?.SceneView != null)
            {
                _patcher.Appliers.ApplySceneView(element, props.SceneView);
            }
            // Particles (V.Particles): wire the simulation host + painter binding. The host is
            // panel-independent (only the draw needs one); later effect swaps arrive through the
            // props diff.
            if (props?.Particles != null)
            {
                _patcher.Appliers.ApplyParticles(element, props.Particles);
                // After ApplyParticles: the spacer sync needs the binding that call creates.
                _patcher.Appliers.ApplyParticlesSpacer(element, classNames);
            }
            // Anchored (V.Anchored): wire the per-frame screen-projection tick. Panel-independent at
            // creation (Attach's own synchronous Sync call bails cleanly if the element has no panel
            // yet); the first real tick fires once mounted.
            if (props?.Anchored != null)
            {
                _patcher.Appliers.ApplyAnchored(element, props.Anchored);
            }
            // Focus scope (V.FocusScope / props.FocusScope): register the scope binding. AutoFocus
            // and the lazy navigator attach ride the binding's own AttachToPanelEvent, so an
            // off-panel create is fine here.
            if (props?.FocusScope != null)
            {
                _patcher.Appliers.ApplyFocusScope(element, props.FocusScope);
            }
            // Drag-and-drop slots: register the bindings. Panel-independent at creation — the
            // draggable's pointer-down armer and the overlay's positioning resolve panels at
            // event time.
            if (props?.DndContext != null)
            {
                _patcher.Appliers.ApplyDndContext(element, props.DndContext);
            }
            if (props?.Draggable != null)
            {
                _patcher.Appliers.ApplyDraggable(element, props.Draggable);
            }
            if (props?.Droppable != null)
            {
                _patcher.Appliers.ApplyDroppable(element, props.Droppable);
            }
            if (props?.DragOverlay != null)
            {
                _patcher.Appliers.ApplyDragOverlay(element, props.DragOverlay);
            }
        }

        // The invisible stand-in a deferred-host node (Portal / WorldSpace) leaves at its own tree
        // position while its children live elsewhere.
        private static VisualElement CreateHiddenPlaceholder() => new()
        {
            style =
            {
                display = DisplayStyle.None
            }
        };

        // The deferred-mount skeleton shared by PortalNode and WorldSpaceNode: a hidden placeholder
        // holds the node's tree position, and the context enclosing that position is snapshotted NOW
        // (the live cursor is correct here, mid-reconcile) — the children mount later in
        // DrainPendingPortalMounts, after the main pass has unwound the cursor, so without the
        // snapshot they would mount with an empty cursor and lose all enclosing Provider /
        // MotionContext values. By design: deferred-host children inherit context from their tree
        // position, not their mount location.
        private VisualElement EnqueueDeferredHostMount(VNode node, VisualElement? target)
        {
            var placeholder = CreateHiddenPlaceholder();
            var contextSnapshot = _ctx.ComponentContextStack.SnapshotTops();
            // FiberStack.Current is the component whose Body is mid-render right now — the one that
            // actually wrote `V.Portal(...)`/`V.WorldSpace(...)` into its returned tree. Capturing it
            // here is what makes it available at all: the pass that had it on the stack has unwound by
            // drain time, and the drain pushes this captured value back rather than reading one.
            var logicalParent = _ctx.FiberStack.Current;
            _ctx.PendingPortalMounts.Enqueue((placeholder, node, target, contextSnapshot, logicalParent));
            return placeholder;
        }

        // Converts a children array into a keyed list, copying into a list rented from the pool.
        // The returned list must be returned via
        // ReconcilerBufferPool.Return after use.
        // Nested AnimatePresence does not corrupt this list because BufferPool provides recursion safety.
        internal List<(string key, VNode node)> BuildKeyedMapCopy(VNode?[] children)
        {
            var result = _ctx.BufferPool.RentKeyedList();
            if (children == null)
            {
                return result;
            }

            var indexByKey = _ctx.BufferPool.RentIndexByKeyMap();
            try
            {
                var autoIndex = 0;
                foreach (var child in children)
                {
                    switch (child)
                    {
                        case null:
                            continue;
                        // By design: AnimatePresence's direct children must each be a
                        // keyable element so enter/exit can be tracked per key. A FragmentNode has no key and
                        // is intentionally NOT auto-expanded here — silently flattening it would let its items
                        // share the Fragment's (absent) key and break exit tracking. Surface a clear LogError
                        // pointing at the fix (use MotionNode directly) rather than guessing.
                        case FragmentNode:
                            FiberLogger.LogError("FiberNodeFactory",
                                "FragmentNode is not supported as a direct child of AnimatePresence. Fragment children will not be expanded. Use MotionNode directly.");
                            continue;
                    }

                    if (child.Key != null && child.Key.StartsWith(AutoKeyPrefix))
                    {
                        FiberLogger.LogWarning("FiberNodeFactory",
                            $"Key \"{child.Key}\" uses reserved prefix \"{AutoKeyPrefix}\". This may conflict with auto-generated keys.");
                    }
                    var key = child.Key ?? $"{AutoKeyPrefix}{autoIndex++}";
                    if (indexByKey.TryGetValue(key, out var existingIndex))
                    {
                        FiberLogger.LogWarning("FiberNodeFactory",
                            $"Duplicate key \"{key}\" detected. Later child will overwrite the earlier one.");
                        result[existingIndex] = (key, child);
                        continue;
                    }
                    indexByKey[key] = result.Count;
                    result.Add((key, child));
                }
                return result;
            }
            finally
            {
                _ctx.BufferPool.ReturnIndexByKeyMap(indexByKey);
            }
        }

        // Anchor VisualElement emitted for Provider / Component / Outlet to track fiber lifecycle.
        // It takes a slot in its container's flow, so the padding, the gap and the sibling order that
        // container declares reach the subtree under it. Absolute insets were the earlier choice and are
        // rejected: an anchor pinned to its container's edges is drawn over the siblings declared before
        // it. What those insets also gave the anchor was a size for a percentage-sized child to resolve
        // against, on both axes; Align.Stretch keeps that on the cross axis and SyncLayoutAnchorGrowth on
        // the main one. LayoutAnchorFlowTests pins both, for a column container and for a row one.
        // The anchor is not the author's element, which is why it declines picking.
        private VisualElement CreateLayoutAnchor()
        {
            var anchor = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    alignSelf = Align.Stretch,
                    overflow = Overflow.Visible
                }
            };
            _ctx.LayoutAnchors.Add(anchor);
            return anchor;
        }

        // An anchor holding nothing takes no main-axis space: an Outlet that matched no route would
        // otherwise claim a share of the container away from the siblings declared beside it.
        // Read where a reconcile finishes against an anchor, so a route body that renders itself away is
        // read the same as one that never matched.
        internal static void SyncLayoutAnchorGrowth(VisualElement anchor)
            => anchor.style.flexGrow = anchor.childCount > 0 ? 1 : 0;

        // Walks node and returns the first MotionNode descendant
        // reachable through transparent wrappers — ContextProviderNode, FragmentNode, and a z-managed
        // ElementNode. Returns the node itself when it is already a MotionNode, or
        // null when no MotionNode exists in this transparent-wrapper chain. Used by
        // AnimatePresence's else-branch
        // (Initial=false) where no warning should be emitted — so a Provider-wrapped Motion
        // contributes its transition / OnEnterComplete to the keyed entry: AnimatePresence tracks
        // the outer wrapper element while transitions remain on the inner motion components.
        internal static MotionNode? FindFirstMotionDescendant(VNode? node)
        {
            if (node == null) return null;
            if (node is MotionNode motion)
            {
                return motion;
            }
            // The transparent wrappers whose children can carry the Motion: a Provider, a Fragment, or a
            // z-managed ElementNode. The z-managed case is a narrow, deliberate carve-out — z-* is a
            // documented no-op on a Motion itself (FiberNodeFactory's own create-time warning), so the ONLY
            // way to combine z-* with an AnimatePresence-driven Motion is to wrap it in a z-managed Div; that
            // wrapper exists purely to satisfy the out-of-flow scope gate, not as an opaque animation
            // boundary the author intended, so treating it like Provider/Fragment for this walk is exactly
            // the same "structurally forced, not a user choice" reasoning. An ORDINARY (non-z) ElementNode is
            // deliberately NOT walked into: unlike Provider/Fragment it emits its own real DOM element, so
            // silently treating any Motion nested anywhere inside it as the presence anchor would surprise a
            // caller who wrapped a Motion in a plain structural Div for unrelated styling reasons.
            var children = node switch
            {
                ContextProviderNode provider => provider.Children,
                FragmentNode fragment => fragment.Children,
                ElementNode elementNode when FiberZLayerCoordinator.TryClassify(elementNode.ClassNames, elementNode.Props, out _)
                    => elementNode.Children,
                _ => null,
            };
            if (children != null)
            {
                foreach (var child in children)
                {
                    var found = FindFirstMotionDescendant(child);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
