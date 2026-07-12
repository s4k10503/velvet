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
        // distinguish it from the generic layout-passthrough wrappers (all of which use
        // PickingMode.Ignore so the wrapper never intercepts pointer events).
        internal const string OutletContainerClass = "velvet-outlet";

        // USS class added to the wrapper VisualElement emitted for a
        // ContextProviderNode. Mirrors OutletContainerClass's role
        // for OutletNode: the wrapper is layout-passthrough so context propagation does not
        // distort layout, and the class lets tests and consumers identify Provider boundaries
        // in the DOM. A Provider emitting its own wrapper element is a deliberate choice here;
        // the layout-passthrough class keeps it transparent to layout.
        internal const string ContextProviderClassName = "velvet-context-provider";

        private void InvokeRefCallback(Func<VisualElement, Action>? refCallback, VisualElement element)
            => _ctx.InvokeRefCallback(element, refCallback);

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
                {
                    var element = _ctx.FiberElementFactory.Create(elementNode);
                    // Capture an element's own Text prop (Label / Button) before the text-effect pass below
                    // transforms it, so a re-apply works from the raw value.
                    if (element is TextElement && elementNode.Props != null && elementNode.Props.Text != null)
                    {
                        StyleTextEffectResolver.CaptureRaw(_ctx, element, elementNode.Props.Text);
                    }
                    if (elementNode.Children != null)
                    {
                        var childContainer = FiberNodePatcher.GetChildContainer(element);
                        // ReconcileChildren (= ChildReconciler.ExpandInlineRecursive) inline-expands
                        // ComponentNode / ContextProviderNode / FragmentNode so children appear as
                        // direct siblings under <paramref name="childContainer"/>. These node kinds
                        // produce no DOM element of their own. The same path is used
                        // on patch (PatchCommon) so initial-mount and patch DOM layouts stay
                        // symmetric — both omit the per-Component wrapper VE.
                        _host.ReconcileChildren(childContainer, Array.Empty<VNode>(), elementNode.Children);
                    }
                    elementNode.OnCreated?.Invoke(element);
                    InvokeRefCallback(elementNode.RefCallback, element);
                    _patcher.Appliers.ApplyGestureManipulator(element, elementNode.WhileHoverClass, elementNode.WhileTapClass, elementNode.WhileFocusClass);
                    _patcher.ApplyVariantManipulators(element, elementNode.ClassNames);
                    // After ApplyVariantManipulators (which registers the data-/aria- variant rules): seed the
                    // attribute store from the props and evaluate, so a data-[..]/aria-[..] variant lights from
                    // the element's carried attribute values at mount.
                    _patcher.ApplyAttributes(element, elementNode.Props);
                    StyleFontResolver.ApplyIfPresent(element, elementNode.ClassNames);
                    // After ReconcileChildren so the gap / divide manipulators see the final child list.
                    _patcher.ApplyGapManipulator(element, elementNode.ClassNames);
                    _patcher.ApplyDivideManipulator(element, elementNode.ClassNames);
                    _patcher.ApplyGridManipulator(element, elementNode.ClassNames);
                    // Same post-children timing: structural variants (first:/last:/odd:/…) need the placed children.
                    _patcher.ApplyStructuralVariants(element);
                    // has-[.class]: (element as subject) likewise needs the placed children to scan.
                    _patcher.ApplyHasClassVariants(element);
                    // has-[:checked]: / has-[:focus]: re-scan: an already-checked descendant mounted under this
                    // element fires no ChangeEvent, so re-derive the manipulator from the placed children.
                    _patcher.ApplyHasVariantManipulators(element);
                    // text-transform / -decoration cascade: after children are placed so it can reach descendant
                    // text leaves, and after the element's own text is set so it transforms the final value.
                    StyleTextEffectResolver.Apply(_ctx, element, elementNode.ClassNames);

                    // Skew is wrapper-less (the sheared silhouette is the element's own painted
                    // content), so it attaches before — and composes with — any wrap layer below,
                    // including a user wrapElement.
                    _patcher.Appliers.ApplySkewOnCreate(element, elementNode.ClassNames);
                    // Gradient is also wrapper-less (baked texture set as the element's own
                    // background-image, clipped to its border-radius), so it attaches on the element too.
                    _patcher.Appliers.ApplyGradientOnCreate(element, elementNode.ClassNames);
                    // animate-* motion (gradient pan / hue cycle) drives the element's own inline style; runs
                    // after the gradient so a pan mode sees the baked gradient already applied.
                    _patcher.Appliers.ApplyAnimateOnCreate(element, elementNode.ClassNames);
                    // Drop shadow is wrapper-less too (the baked shadow texture is painted behind the
                    // element's own content, bleeding outside the box) — a non-structural paint like CSS
                    // box-shadow, so it composes with any wrap layer below and a user wrapElement. The paint
                    // self-suppresses while an active clip-path-* is present (clip-path clips the box-shadow).
                    _patcher.Appliers.ApplyShadowOnCreate(element, elementNode.ClassNames);

                    if (elementNode.WrapElement != null)
                    {
                        var wrapper = elementNode.WrapElement(element);
                        if (wrapper != null && wrapper != element)
                        {
                            _ctx.WrapperToInnerMap[wrapper] = element;
                            return wrapper;
                        }
                        return element;
                    }

                    // No explicit wrapElement: a clip-path-* class auto-wraps the element in a stencil-
                    // masking container, else a ring-* class wraps it in a native-border overlay container.
                    // Clip takes precedence: the two are mutually exclusive (one structural wrapper per
                    // element). The shadow is NOT here — it is a wrapper-less paint attached above (a clipped
                    // element renders no shadow because the shadow paint self-suppresses on an active clip).
                    var clipWrapped = _patcher.Appliers.ApplyClipPathOnCreate(element, elementNode.ClassNames);
                    if (!ReferenceEquals(clipWrapped, element))
                    {
                        return clipWrapped;
                    }
                    return _patcher.Appliers.ApplyRingOnCreate(element, elementNode.ClassNames);
                }
                case MotionNode motionNode:
                {
                    // Resolve the applied classes against the effective label (own Animate, else the nearest
                    // ancestor Motion's label read from MotionContext) — the variant-inheritance model.
                    var motionAmbient = _ctx.ComponentContextStack.Get(MotionContext.ActiveLabel);
                    var appliedClasses = MotionVariantResolver.ResolveApplied(motionNode, motionAmbient, out var variantApplied);
                    var element = _ctx.FiberElementFactory.CreateMotion(motionNode, appliedClasses);
                    // Only record applied-class bookkeeping when a variant actually merged; the variant-less
                    // majority needs no entry (patch falls back to oldNode.ClassNames for the diff baseline).
                    if (variantApplied)
                    {
                        _ctx.MotionAppliedClasses[element] = appliedClasses;
                    }
                    // Record the label propagated to children now (regardless of whether this Motion currently
                    // has any) so the FIRST patch on this element has an accurate baseline: PatchMotion diffs
                    // against this stored value to detect an ACTUAL label change before it (re-)triggers
                    // staggerChildren/delayChildren orchestration — without seeding it here, that first patch
                    // would see no previous entry and could misfire even when the label held steady across
                    // mount and the first re-render. Orchestration itself only ever starts from a PATCH-time
                    // label change (see FiberNodePatcher.PatchMotion), never on mount.
                    var childLabel = MotionVariantResolver.LabelForChildren(motionNode, motionAmbient);
                    if (childLabel != null)
                    {
                        _ctx.MotionChildLabel[element] = childLabel;
                    }
                    else
                    {
                        _ctx.MotionChildLabel.Remove(element);
                    }
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
                    InvokeRefCallback(motionNode.RefCallback, element);
                    _patcher.Appliers.ApplyGestureManipulator(element, motionNode.WhileHoverClass, motionNode.WhileTapClass, motionNode.WhileFocusClass);
                    _patcher.ApplyVariantManipulators(element, appliedClasses);
                    _patcher.ApplyAttributes(element, motionNode.Props);
                    StyleFontResolver.ApplyIfPresent(element, appliedClasses);
                    _patcher.ApplyGapManipulator(element, appliedClasses);
                    _patcher.ApplyDivideManipulator(element, appliedClasses);
                    _patcher.ApplyGridManipulator(element, appliedClasses);
                    _patcher.ApplyStructuralVariants(element);
                    _patcher.ApplyHasClassVariants(element);
                    _patcher.ApplyHasVariantManipulators(element);
                    _patcher.Appliers.ApplyGradientOnCreate(element, appliedClasses);
                    _patcher.Appliers.ApplyAnimateOnCreate(element, appliedClasses);
                    // Motion does NOT paint a drop shadow: the animation scheduler hides a subtree's shadow
                    // paints for the lifetime of an enter / exit (the opacity-blind shadow would otherwise
                    // show through the fading caster as a dark box), and a shadow ON the animating Motion
                    // itself would be the very paint that must stay hidden — so the shadow belongs on a Div the
                    // Motion wraps (the Div carries the shadow, the Motion carries the transition). Warn and
                    // skip the paint. Warn only for an ACTIVE shadow (shadow-none deliberately cancelling the
                    // cascade is not "ignored" — nothing would render anywhere).
                    if (StyleShadowClass.HasShadowClass(appliedClasses)
                        && StyleShadowClass.TryExtract(appliedClasses, out _))
                    {
                        FiberLogger.LogWarning("Motion",
                            "A shadow-* utility on a Motion is ignored: the enter/exit fade hides shadow paints, "
                            + "so a shadow on the animating element itself cannot show. "
                            + "Wrap the Motion around a shadowed Div instead.");
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
                    // Exit tweens are scheduled only by the AnimatePresence expansion — something has to defer
                    // the unmount for a removal to animate against, and AnimatePresence is what does that — so
                    // exit outside one is genuinely inert. Warn like the shadow-*/clip-path-* gates above. Initial
                    // is NOT warned here (see the standalone enter below): unlike exit, a mount-time enter needs
                    // no deferred unmount to play against, so it works on any Motion, matching Framer parity
                    // (initial/animate apply to any motion.* component; only AnimatePresence is exit-only).
                    if (_ctx.PresenceExpansionDepth == 0 && motionNode.Exit != null)
                    {
                        FiberLogger.LogWarning("Motion",
                            "exit on a Motion outside AnimatePresence is inert: exit tweens are driven by the "
                            + "AnimatePresence expansion. Wrap the Motion in V.AnimatePresence (or drop exit).");
                    }
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
                        if (motionNode.Transition != null && GeneralPathReconciler.TryResolveVariantInitial(
                                motionNode, out var standaloneFromClasses, out var standaloneToClasses))
                        {
                            var t = motionNode.Transition;
                            _ctx.StyleAnimationScheduler.PlayVariantEnter(element, standaloneFromClasses, standaloneToClasses,
                                t.DurationSec, t.Easing, t.DelaySec, motionNode.OnEnterComplete,
                                propertyOverrides: t.PropertyOverrides,
                                type: t.Type, stiffness: t.Stiffness, damping: t.Damping, mass: t.Mass);
                        }
                        else
                        {
                            // Initial declared but unresolvable: no own Animate (an inherited-label
                            // configuration is not yet driven by the standalone enter), or the label is missing
                            // from Variants / maps to an empty class. Warn instead of silently mounting inert,
                            // matching the Exit gate's own inert-configuration diagnostic above.
                            FiberLogger.LogWarning("Motion",
                                "initial is set but has no resolvable enter: this Motion needs its own animate + "
                                + "variants (with initial mapping to a non-empty class) for a standalone mount "
                                + "enter. An inherited animate label does not yet drive one.");
                        }
                    }
                    return element;
                }
                case AnimatePresenceNode:
                    // AnimatePresence is DOM-less: it never becomes a single element.
                    // ChildReconciler.ExpandAnimatePresenceInline expands its keyed children directly into
                    // the parent's slot range, so CreateElement is never invoked on it.
                    throw new System.InvalidOperationException(
                        "[FiberNodeFactory] AnimatePresenceNode is DOM-less and must be inline-expanded, not created as an element.");
                case PortalNode portalNode:
                {
                    var placeholder = new VisualElement
                    {
                        style =
                        {
                            display = DisplayStyle.None
                        }
                    };
                    var target = FiberPortalRegistry.Get(portalNode.TargetId);
                    if (target == null)
                    {
                        FiberLogger.LogWarning("Portal", $"Target \"{portalNode.TargetId}\" is not registered. Children will not be rendered.");
                        _ctx.PortalState[placeholder] = new PortalSlotInfo(portalNode.TargetId, portalNode.Children ?? Array.Empty<VNode>(), 0, 0);
                        return placeholder;
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
                    // Capture the context enclosing the Portal's TREE position now (the live cursor is correct
                    // here, mid-reconcile). The children mount later in DrainPendingPortalMounts, after the
                    // main pass has unwound the cursor, so without this snapshot they would mount with an empty
                    // cursor and lose all enclosing Provider / MotionContext values. By design: Portal
                    // children inherit context from their tree position, not their mount location.
                    var contextSnapshot = _ctx.ComponentContextStack.SnapshotTops();
                    _ctx.PendingPortalMounts.Enqueue((placeholder, portalNode, target, contextSnapshot));
                    return placeholder;
                }
                case VirtualListNode virtualListNode:
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
                    StyleFontResolver.ApplyIfPresent(scrollView, virtualListNode.ClassNames);
                    return scrollView;
                }
                case TextNode textNode:
                {
                    var label = _ctx.FiberElementFactory.CreateText(textNode);
                    // Capture the raw text (and apply any already-resolved ancestor effect) so a text-transform /
                    // -decoration carried by an ANCESTOR cascades onto this leaf — a TextNode has no class of its
                    // own. At mount the ancestor's own effect is parsed in its later post-children pass, which
                    // re-applies; OnTextSet here makes an isolated later leaf re-render self-sufficient.
                    StyleTextEffectResolver.OnTextSet(_ctx, label, textNode.Text);
                    return label;
                }
                case ComponentNode componentNode:
                {
                    // Wrapper-mount path: reached only when CreateElement is invoked directly on a
                    // ComponentNode the reconcile walk did not inline-expand — a MemoNode whose
                    // resolved inner is a ComponentNode, or a ComponentNode that is a direct child of
                    // an AnimatePresence keyed entry. ComponentNodes reached during a
                    // ChildReconciler.Reconcile pass (top-level or nested under an element) are
                    // inline-mounted (no wrapper VE) by ChildReconciler.ExpandInlineRecursive, so this
                    // case is unreachable for them.
                    // A Component does not emit a DOM element; its rendered tree attaches
                    // directly to the parent. Velvet needs an anchor element for fiber tracking,
                    // so the wrapper is made layout-transparent so its single child can size
                    // against the real parent.
                    var wrapper = CreateLayoutPassthroughContainer();
                    _patcher.HandleComponentMount(wrapper, componentNode);
                    return wrapper;
                }
                case ContextProviderNode providerNode:
                {
                    // A context Provider emits no DOM element of its own; descendants attach directly to
                    // the parent fiber's host. Velvet maps each VNode to exactly one VisualElement so a
                    // layout-passthrough wrapper anchors the Provider subtree without imposing layout.
                    // The wrapper is what AnimatePresence's keyed map (and any BaseElementNode children
                    // list reached through MemoNode's resolved inner) tracks for this Provider entry —
                    // the wrapper element is a deliberate choice, as documented on ContextProviderNode.
                    //
                    // ChildReconciler.ExpandInlineRecursive expands Provider inline (no wrapper) during
                    // a Reconcile pass, so this case is unreachable for slot-based reconciliation; it is
                    // reached only when CreateElement is invoked directly on a Provider VNode — e.g. a
                    // MemoNode resolved inner.
                    var container = CreateLayoutPassthroughContainer();
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
                case OutletNode outletNode:
                {
                    // The container is layout-transparent so the matched route's element resolves
                    // its size against the Outlet's parent box, and doubles as the fiber anchor
                    // for the matched route's Component (one wrapper, not two).
                    var container = CreateLayoutPassthroughContainer();
                    container.AddToClassList(OutletContainerClass);
                    // Identity-side registration for FiberContextSpine: separate from the USS class
                    // (which is for styling and is user-mutable). Populated unconditionally so the
                    // spine walker can identify Outlet hosts before Router setup completes.
                    _ctx.OutletContainers.Add(container);

                    if (!_patcher.ResolveOutletMatch(out var routeElement, out var routeDepth, out var match))
                    {
                        return container;
                    }

                    var scopeFactory = Router.Current?.ScopeFactory;
                    if (scopeFactory != null)
                    {
                        outletNode.Scope = scopeFactory.CreateScope(match.Route, null);
                        _ctx.OutletScopes[container] = outletNode.Scope;
                    }

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
                default:
                    // Unknown VNode type: FragmentNode (which should have been expanded by the parent),
                    // null, or a missing branch for a newly added VNode type. Log a warning for debuggability.
                    FiberLogger.LogWarning("FiberNodeFactory",
                        $"Unsupported VNode type: {node?.GetType().Name ?? "null"}. Returning empty VisualElement.");
                    return new VisualElement();
            }
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
        // Layout-transparent via absolute insets so the wrapper fills its parent's full box at any
        // depth — bare wrappers collapse to 0 height when their only child is absolute, because
        // Yoga measures parents from in-flow children alone, and deep Provider / Outlet chains
        // then cascade-collapse every descendant to 0x0. PickingMode.Ignore ensures clicks fall
        // through to user-emitted elements (otherwise overlay passthrough wrappers would steal
        // clicks from routed pages beneath them).
        internal static VisualElement CreateLayoutPassthroughContainer()
            => new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                    overflow = Overflow.Visible
                }
            };

        // Resolves the MotionNode that drives an AnimatePresence keyed entry's
        // enter / exit lifecycle (Transition + OnEnterComplete). Walks transparent wrappers
        // (ContextProviderNode, FragmentNode) via
        // FindFirstMotionDescendant and emits a warning when no Motion is found so
        // the keyed entry surfaces as a missing animation in logs. AnimatePresence mount and patch
        // paths read both Transition and OnEnterComplete from the same resolved
        // Motion, so this helper exists to fold the walk + warning into a single pass — callsites
        // that read both fields would otherwise traverse the transparent-wrapper chain twice.
        internal static MotionNode? ResolveAnimatePresenceMotion(VNode node)
        {
            var motion = FindFirstMotionDescendant(node);
            if (motion == null && node != null && node is not TextNode)
            {
                FiberLogger.LogWarning("FiberNodeFactory",
                    $"Non-MotionNode child ({node.GetType().Name}) has no transition. Use V.Motion() to wrap children for enter/exit animations.");
            }
            return motion;
        }

        // Walks node and returns the first MotionNode descendant
        // reachable through transparent wrappers — ContextProviderNode and
        // FragmentNode. Returns the node itself when it is already a MotionNode, or
        // null when no MotionNode exists in this transparent-wrapper chain. Used by
        // ResolveAnimatePresenceMotion and AnimatePresence's else-branch
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
            // The only transparent wrappers whose children can carry the Motion: a Provider or a Fragment.
            var children = node switch
            {
                ContextProviderNode provider => provider.Children,
                FragmentNode fragment => fragment.Children,
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
