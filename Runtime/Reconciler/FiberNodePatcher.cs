#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // VNode diff engine.
    // Compares oldNode and newNode and updates the VisualElement with minimal DOM operations.
    internal sealed class FiberNodePatcher
    {
        private readonly ReconcilerContext _ctx;
        private readonly WrapperInfrastructure _wrappers;
        private readonly FiberWrapperElementAppliers _appliers;
        private IReconcilerHost _host = null!;

        public FiberNodePatcher(ReconcilerContext ctx)
        {
            _ctx = ctx;
            _wrappers = new WrapperInfrastructure(ctx);
            _appliers = new FiberWrapperElementAppliers(ctx, _wrappers);
            // Let the variant manipulators (via StyleVariantPayload) re-resolve a clip-path mask when a
            // hover:/focus:/dark: clip payload toggles — the class toggle alone does nothing in UITK.
            _ctx.ClipPathReResolve = _appliers.ReResolveClipPathLive;
        }

        // The className-driven effect appliers (skew/gradient/motion/shadow/ring/clip-path/gesture).
        // Exposed so the node factory can run the create-time appliers; PatchElement/PatchMotion call
        // the patch-time ones directly through _appliers.
        internal FiberWrapperElementAppliers Appliers => _appliers;

        internal void SetHost(IReconcilerHost host)
        {
            // Throw an exception (not Assert) so that release builds detect the issue.
            // Double invocation indicates a Reconciler wiring-order bug, so fail fast.
            if (_host != null)
            {
                throw new System.InvalidOperationException("[FiberNodePatcher] SetHost called twice");
            }
            _host = host;
        }

        #region Node Patching

        // Applies the oldNode → newNode diff to the existing DOM element element.
        // Contract (important):
        // PatchNode must not modify the childCount or child order of element's
        // parent. Reconciling the child container (children of element itself) is not subject
        // to this constraint, but removing/inserting the element from its parent is forbidden.
        // Violating this contract breaks the old.index == DOM index invariant in
        // ChildReconciler.ReconcileKeyed Pass 2, causing the wrong element to be patched.
        // New VNode types must preserve this contract.
        // Exception: the MemoNode branch in PatchNode does replace the element itself, but it preserves
        // childCount via parent.RemoveAt + Insert.
        internal void PatchNode(VisualElement element, VNode? oldNode, VNode? newNode)
        {
            switch (oldNode)
            {
                case ElementNode oldElem when newNode is ElementNode newElem:
                    PatchElement(element, oldElem, newElem);
                    break;
                case TextNode oldText when newNode is TextNode newText:
                    // Invariant: TextNode is always mapped to Label by CreateElement.
                    // Assert (rather than silently ignore) if `element is Label` does not hold.
                    UnityEngine.Debug.Assert(
                        element is Label,
                        $"[FiberNodePatcher] TextNode is expected to be mapped to Label, but got {element.GetType().Name}.");
                    if (element is Label label)
                    {
                        PatchText(label, oldText, newText);
                    }
                    break;
                case MotionNode oldMotion when newNode is MotionNode newMotion:
                    PatchMotion(element, oldMotion, newMotion);
                    break;
                // AnimatePresence is DOM-less (inline-expanded by ChildReconciler), so it is never a
                // patchable leaf — no case here.
                case VirtualListNode oldVirtualList when newNode is VirtualListNode newVirtualList:
                {
                    if (element is ScrollView scrollView && _ctx.VirtualListControllers.TryGetValue(scrollView, out var controller))
                    {
                        controller.Update(newVirtualList);

                        // The ScrollView persists across patches, so re-sync its class-driven styling the
                        // same way ElementNode does. DiffClassList (inside the helper) is non-destructive —
                        // delta only, never ClearClassList — so the ScrollView's built-in USS classes survive.
                        SyncClassDrivenStyling(scrollView, oldVirtualList.ClassNames, newVirtualList.ClassNames);
                    }
                    else
                    {
                        FiberLogger.LogWarning("FiberNodePatcher", "Controller not found for ScrollView during patch. This should not happen.");
                    }
                    break;
                }
                case PortalNode oldPortal when newNode is PortalNode newPortal:
                    PatchPortal(element, oldPortal, newPortal);
                    break;
                case WorldSpaceNode oldWorldSpace when newNode is WorldSpaceNode newWorldSpace:
                    PatchWorldSpace(element, oldWorldSpace, newWorldSpace);
                    break;
                case ContextProviderNode oldProvider when newNode is ContextProviderNode newProvider:
                    PatchContextProvider(element, oldProvider, newProvider);
                    break;
                case ComponentNode when newNode is ComponentNode newComp:
                    HandleComponentMount(element, newComp);
                    break;
                case OutletNode oldOutlet when newNode is OutletNode newOutlet:
                {
                    if (!ResolveOutletMatch(out var routeElement, out var routeDepth, out var match)
                        || routeElement == null)
                    {
                        break;
                    }

                    // The Outlet's container doubles as the route Component's fiber anchor.
                    // RemoveIfDifferentIdentity detects route change and disposes the previous fiber.
                    if (_ctx.ComponentRegistry.RemoveIfDifferentIdentity(element, routeElement.ResolvedIdentity))
                    {
                        element.Clear();

                        oldOutlet.Scope?.Dispose();
                        _ctx.OutletScopes.Remove(element);
                        var scopeFactory = Router.Current?.ScopeFactory;
                        if (scopeFactory != null)
                        {
                            newOutlet.Scope = scopeFactory.CreateScope(match!.Route, null);
                            _ctx.OutletScopes[element] = newOutlet.Scope;
                        }
                    }
                    else if (_ctx.OutletScopes.TryGetValue(element, out var existingScope))
                    {
                        newOutlet.Scope = existingScope;
                    }
                    else
                    {
                        // First match: no fiber exists yet, and no scope is registered.
                        var scopeFactory = Router.Current?.ScopeFactory;
                        if (scopeFactory != null)
                        {
                            newOutlet.Scope = scopeFactory.CreateScope(match!.Route, null);
                            _ctx.OutletScopes[element] = newOutlet.Scope;
                        }
                    }

                    // Mount the matched route Component with Depth+1 pushed live so the next nested
                    // Outlet resolves the following route in the match chain.
                    // The Outlet's context value is pushed too for Hooks.UseOutletContext.
                    _ctx.ComponentContextStack.Push(RouterContext.Depth, routeDepth);
                    _ctx.ComponentContextStack.Push(RouterContext.OutletContext, newOutlet.OutletContextValue);
                    try
                    {
                        HandleComponentMount(element, routeElement);
                    }
                    finally
                    {
                        _ctx.ComponentContextStack.Pop(RouterContext.OutletContext);
                        _ctx.ComponentContextStack.Pop(RouterContext.Depth);
                    }
                    break;
                }
            }
        }

        private void PatchBaseElement(VisualElement element, BaseElementNode oldNode, BaseElementNode newNode)
            => PatchBaseElement(element, oldNode, newNode, oldNode.ClassNames, newNode.ClassNames);

        // Base-element patch using explicit APPLIED class arrays for the class-driven styling diff (the Motion
        // path passes base + ancestor-propagated variant classes here; everything else passes raw ClassNames).
        private void PatchBaseElement(VisualElement element, BaseElementNode oldNode, BaseElementNode newNode,
            string[] appliedOldClasses, string[] appliedNewClasses)
        {
            SyncClassDrivenStyling(element, appliedOldClasses, appliedNewClasses);
            DiffProps(element, oldNode.Props, newNode.Props);
            // Track an element's own Text prop as raw so the text-effect pass (run post-children) transforms the
            // current value rather than an already-transformed one. When the Text prop is removed, drop the raw
            // entry so the effect pass does not re-apply a stale value over the just-cleared text.
            if (element is TextElement)
            {
                if (newNode.Props != null && newNode.Props.Text != null)
                {
                    StyleTextEffectResolver.CaptureRaw(_ctx, element, newNode.Props.Text);
                }
                else
                {
                    _ctx.TextRawText.Remove(element);
                }
            }
            // After DiffProps (and the class-driven config it follows): re-sync the data-/aria- attribute
            // store and re-evaluate its variants, so a changed Data / Aria prop re-derives the payload even
            // when the class list is unchanged (SyncClassDrivenStyling re-registers the rules when the class
            // list DID change, and this runs after, so the rules exist either way).
            ApplyAttributes(element, newNode.Props);
            PatchCommon(element, oldNode.Name, newNode.Name, newNode.Events,
                oldNode.Children, newNode.Children, newNode.RefCallback);
            if (oldNode.WhileHoverClass != newNode.WhileHoverClass || oldNode.WhileTapClass != newNode.WhileTapClass
                || oldNode.WhileFocusClass != newNode.WhileFocusClass)
            {
                _appliers.ApplyGestureManipulator(element, newNode.WhileHoverClass, newNode.WhileTapClass, newNode.WhileFocusClass);
            }
        }

        // Re-syncs every class-driven styling mechanism for an element whose class list may have changed:
        // the USS class list (and the arbitrary-value inline styles it carries) via
        // DiffClassList, the state / responsive / relational variant manipulators, and the
        // inline font layer. Both PatchBaseElement and the VirtualList patch path call this,
        // so the full set of class-driven mechanisms lives in one place and the two paths cannot drift.
        // Gap is intentionally excluded: it is re-applied separately by the per-node patch methods
        // (PatchElement / PatchMotion) AFTER children reconcile, so it sees the
        // final child list. The variant/font work is gated on DiffClassList's own verdict of whether the
        // class list actually changed CONTENT (not merely array identity) — every variant manipulator this
        // derives from (ApplyVariantManipulators and its callees) reads ONLY the classNames array, so a
        // freshly-allocated array with the same tokens carries no new information and re-deriving from it
        // would just rebuild the same payloads. A component that rebuilds its VNode tree every render
        // (no ILPP memoization, or a Motion resolving the same active variant label — MotionVariantResolver
        // .ResolveApplied always concatenates a fresh array) hits this path on every patch.
        internal void SyncClassDrivenStyling(VisualElement element, string[] oldClasses, string[] newClasses)
        {
            var changed = DiffClassList(element, oldClasses, newClasses);
            if (changed)
            {
                ApplyVariantManipulators(element, newClasses);
                // Font family / weight / italic are resolved together (so font-bold + italic compose)
                // and written as inline style that overrides the USS fallback classes.
                StyleFontResolver.ApplyOnClassChange(element, oldClasses, newClasses);
            }
        }

        private void PatchElement(VisualElement element, ElementNode oldNode, ElementNode newNode)
        {
            PatchBaseElement(element, oldNode, newNode);
            DiffStyles(element, oldNode.Styles, newNode.Styles);
            // The shared post-children passes run after PatchCommon (which reconciles children) AND
            // DiffStyles — keeping gap after DiffStyles preserves the ordering invariant that the
            // manipulator's container-margin writes are never clobbered by a later inline-style diff on
            // the same element. (Today DiffStyles only touches color properties, but the invariant must
            // hold if a margin-writing StyleOverride is ever added.)
            ApplyPostChildrenClassPasses(element, newNode.ClassNames, gradientSkewable: true);
            // After the shared passes: reconcile the paint + wrapper effect layers against the new class
            // list. They run last so each is the final word on this element. Skew and
            // shadow are wrapper-less paints (the silhouette / shadow are the element's own
            // generateVisualContent); their stash / spec sync must observe this patch's freshly-applied class
            // styling. clip-path runs BEFORE shadow: a clip clips the box-shadow too (CSS), so the shadow
            // patch reads the post-clip result (clipActive) and suppresses the paint while a clip is active.
            // The clip / skew results are forwarded so the shadow gate never re-parses those class families.
            var skewXDeg = _appliers.ApplySkewOnPatch(element, oldNode.ClassNames, newNode.ClassNames);
            var clipActive = _appliers.ApplyClipPathOnPatch(element, newNode.ClassNames);
            _appliers.ApplyShadowOnPatch(element, newNode.ClassNames, clipActive, skewXDeg);
            // Ring is the lowest-precedence WRAPPER layer: suppressed only when clip owns the wrapper (the two
            // are mutually exclusive — one wrapper per element). The shadow is now a paint, so a ring composes
            // with it rather than competing for the wrapper.
            _appliers.ApplyRingOnPatch(element, newNode.ClassNames, suppress: clipActive, allowWrap: true);
        }

        // The ordered post-children effect-pass sequence shared by PatchElement and PatchMotion, kept in
        // one place so the two patch paths cannot drift (a pass added here reaches both). The ORDER is
        // load-bearing:
        // - Gap runs first but still AFTER PatchCommon (which reconciles children) so the manipulator's
        //   margin writes are the final word on the element — the wrap path writes the container's OWN
        //   margins (-gap/2) — and so it re-applies against the current child set (a child add / remove
        //   re-spaces even when the className did not change). Divide / grid follow at the same timing
        //   for the same child-set reason.
        // - Structural variants (first:/last:/nth) re-derive every child's position-based match from the
        //   final sibling order.
        // - has-[.class]: re-evaluated with the element AS subject (its descendants drive its own payload),
        //   at the same post-children timing — a child added / removed re-derives the match.
        // - has-[:checked]: / has-[:focus]: re-scanned at the same timing — a checked / focused descendant
        //   added or removed fires no event, so the manipulator must re-derive from the live subtree.
        // - text-transform / -decoration cascade (post-children so it reaches descendant text leaves).
        // - Gradient runs after the node-style diff so its background-image is the last word on this
        //   element — DiffStyles only writes background-image on an actual node-style change, which a
        //   gradient element never carries, so the two never fight. gradientSkewable is the one per-path
        //   knob: an ElementNode may render a sheared silhouette, a Motion never does (see PatchMotion).
        // - animate-* motion runs after the gradient (a pan mode reads the live gradient) and reconciles
        //   its own restart/attach/detach against the new class list.
        // The element-only paint/wrapper tail (skew / clip-path / shadow, then ring) stays with the
        // callers: PatchMotion intentionally omits the trio and passes different ring flags.
        private void ApplyPostChildrenClassPasses(VisualElement element, string[] classNames, bool gradientSkewable)
        {
            ApplyGapManipulator(element, classNames);
            ApplyDivideManipulator(element, classNames);
            ApplyGridManipulator(element, classNames);
            ApplyStructuralVariants(element);
            ApplyHasClassVariants(element);
            ApplyHasVariantManipulators(element);
            StyleTextEffectResolver.Apply(_ctx, element, classNames);
            _appliers.ApplyGradientOnPatch(element, classNames, skewable: gradientSkewable);
            _appliers.ApplyAnimateOnPatch(element, classNames);
            // Here, not in the Particles-settings diff: the spacer follows the class list (a filter comes and
            // goes via a class swap or variant), and this pass is the one hook shared by the element and Motion
            // patch paths — a particle can be Motion-hosted.
            _appliers.ApplyParticlesSpacer(element, classNames);
        }

        private void PatchText(Label label, TextNode oldNode, TextNode newNode)
        {
            if (oldNode.Text != newNode.Text)
            {
                // OnTextSet captures the new raw AND applies the cascade-resolved effect, so an isolated leaf
                // re-render (its text changed via an inner component's state while the effect-bearing ancestor
                // did not re-render) still shows the inherited transform; a whole-component render also re-applies
                // via the ancestor's post-children pass (idempotent).
                StyleTextEffectResolver.OnTextSet(_ctx, label, newNode.Text);
            }
        }

        // Shared logic for PatchElement / PatchMotion: rebind events, update name, recurse into
        // children Reconcile, and replace the callback ref's cleanup → setup pair.
        private void PatchCommon(
            VisualElement element,
            string? oldName, string? newName,
            FiberEventBinding[] newEvents,
            VNode?[] oldChildren, VNode?[] newChildren,
            Func<VisualElement, Action>? refCallback)
        {
            if (!_ctx.EventManager.HasSameBindings(element, newEvents))
            {
                _ctx.EventManager.UnbindAll(element);
                if (newEvents != null)
                {
                    foreach (var evt in newEvents)
                    {
                        _ctx.EventManager.Bind(element, evt);
                    }
                }
            }

            // Sync the name to the new value, INCLUDING clearing it when the prop is removed (null / empty) — an
            // attribute that disappears from the VNode must disappear from the element (parity with className /
            // text / etc.). On in-place reuse (esp. positional, no key) a stale name would otherwise make a later
            // Q("old") mis-hit the reused element. Compare against the live element.name so the set is idempotent.
            var resolvedName = newName ?? string.Empty;
            if (element.name != resolvedName)
            {
                element.name = resolvedName;
            }

            var childContainer = GetChildContainer(element);
            // BaseElementNode children are inline-expanded by both the initial CreateElement chain
            // (which routes through ReconcileChildren) and this patch path. Keeping both passes on
            // the same expansion strategy means ComponentNode siblings under an ElementNode appear
            // as direct VE children — never wrapped in the layout-passthrough container that would
            // collapse N keyed Components to the same absolute slot.
            _host.ReconcileChildren(childContainer,
                oldChildren ?? Array.Empty<VNode>(),
                newChildren ?? Array.Empty<VNode>());

            _ctx.InvokeRefCallback(element, refCallback);
        }

        private void PatchMotion(VisualElement element, MotionNode oldNode, MotionNode newNode)
        {
            // Effective label = own Animate, else the inherited MotionContext label (read BEFORE we push this
            // node's label for its own children).
            var motionAmbient = _ctx.ComponentContextStack.Get(MotionContext.ActiveLabel);
            var ambientOrchestration = _ctx.ComponentContextStack.Get(MotionContext.Orchestration);
            var appliedNew = MotionVariantResolver.ResolveApplied(newNode, motionAmbient, out var newVariantClasses);
            // Diff against the previously-APPLIED set (base + resolved variant), not the raw ClassNames — so a
            // changed effective label swaps the variant classes even when this node's base classes are equal.
            // When no entry exists (variant-less, never stored) the baseline is the node's base classes with no
            // variant classes — an explicit pair (MotionAppliedClassSet), not something re-derived from the
            // merged array's tail by position (see ResolveApplied's own doc for why that would be fragile).
            var hasPreviousApplied = _ctx.MotionAppliedClasses.TryGetValue(element, out var previousApplied);
            var appliedOld = hasPreviousApplied ? previousApplied.Merged : oldNode.ClassNames;
            var oldVariantClasses = hasPreviousApplied ? previousApplied.VariantClasses : Array.Empty<string>();
            var variantApplied = newVariantClasses.Length > 0;
            // Keep an entry only while a variant is applied; drop it when a variant→no-variant transition happens
            // (the diff above still uses the stored old classes to REMOVE the now-stale variant utilities).
            if (variantApplied)
            {
                _ctx.MotionAppliedClasses[element] = new MotionAppliedClassSet(appliedNew, newVariantClasses);
            }
            else
            {
                _ctx.MotionAppliedClasses.Remove(element);
            }

            // staggerChildren/delayChildren propagation (plain variant-tree orchestration — no AnimatePresence
            // required): this node FOLLOWS the ambient label (no own Animate opting it out) and an ancestor
            // Motion is currently orchestrating THIS render (its own active label just changed and its
            // Transition declared the knobs) — claim the next sequential slot. The claim rides along as the
            // runtime-swap play's additionalDelaySec further below (delaying the SWAP itself, not a parked CSS
            // transition-delay for utilities this element may not even declare), layered on top of whatever
            // this node's own Transition.DelaySec the play's own config already carries. Declared OUTSIDE the
            // `if` (0f when this node claims nothing) so it can be folded into a fresh orchestration frame THIS
            // node establishes below for its OWN children (see ResolveChildOrchestration): this node's own swap
            // does not start until extraDelaySec has elapsed, so a child frame it establishes must measure its
            // claims from that same origin, not from render-commit time as if this node's swap were immediate.
            var extraDelaySec = 0f;
            if (newNode.Animate == null && variantApplied && ambientOrchestration != null)
            {
                extraDelaySec = ambientOrchestration.ClaimNextChildDelaySec();
            }

            var childLabel = MotionVariantResolver.LabelForChildren(newNode, motionAmbient);
            // Compare against the label THIS element propagated to children last time (not merely whether ITS
            // OWN classes changed — a "coordinator" Motion may propagate a label while carrying no Variants of
            // its own) to detect an ACTUAL change before (re-)establishing a fresh orchestration frame: a
            // re-render that keeps the same label must not re-trigger the stagger.
            var previousChildLabel = _ctx.MotionChildLabel.TryGetValue(element, out var prevChildLabel) ? prevChildLabel : null;
            var childLabelChanged = childLabel != previousChildLabel;
            // Only touch the map when the label actually changed: an unchanged null already has no entry (the
            // else branch below already removed it last time), and an unchanged non-null value is already
            // stored under this exact key — re-writing/re-removing it every render would just be a wasted
            // Dictionary op on the (overwhelming) common "same label" re-render.
            if (childLabelChanged)
            {
                if (childLabel != null)
                {
                    _ctx.MotionChildLabel[element] = childLabel;
                }
                else
                {
                    _ctx.MotionChildLabel.Remove(element);
                }
            }

            if (childLabel != null)
            {
                var childOrchestration = ResolveChildOrchestration(newNode, childLabelChanged, ambientOrchestration, extraDelaySec);
                // Skip the Orchestration round-trip when this node passes the ambient frame through UNCHANGED
                // (including the common "no orchestration anywhere in this subtree" case, both null): a
                // descendant's Get already sees exactly ambientOrchestration without anything new pushed, so
                // pushing then popping the identical reference back off is pure overhead.
                var pushOrchestration = !ReferenceEquals(childOrchestration, ambientOrchestration);
                _ctx.ComponentContextStack.Push(MotionContext.ActiveLabel, childLabel);
                if (pushOrchestration)
                {
                    _ctx.ComponentContextStack.Push(MotionContext.Orchestration, childOrchestration);
                }
                try
                {
                    PatchBaseElement(element, oldNode, newNode, appliedOld, appliedNew);
                }
                finally
                {
                    if (pushOrchestration)
                    {
                        _ctx.ComponentContextStack.Pop(MotionContext.Orchestration);
                    }
                    _ctx.ComponentContextStack.Pop(MotionContext.ActiveLabel);
                }
            }
            else
            {
                PatchBaseElement(element, oldNode, newNode, appliedOld, appliedNew);
            }

            // Runtime variant swap: PatchBaseElement above already synced the class list to the final resting
            // state (appliedNew) via a plain, instant diff. When the effective label actually changed WHICH
            // variant classes are applied AND this Motion declares a Transition, replay that same swap as a
            // VISUAL tween on the scheduler instead — Framer applies `transition` to every animate update, not
            // just the first. A null Transition keeps today's plain, instant diff (Velvet does not imitate
            // Framer's implicit default transition).
            // Gated off an element the scheduler already treats as EXITING (not off PresenceAnchorMotion
            // identity — that field is set for every current AnimatePresence child, including a plain
            // PERSISTING one this swap must still drive when its ambient label changes, e.g. a coordinator
            // orchestrating a presence-managed child). A Motion's own resolved variant only actually changes
            // (the precondition above) while ReferenceEquals(node, motion) && Variants != null, which is
            // exactly GeneralPathReconciler's own isVariantMotion — and its explicit enter dispatch for that
            // shape either runs on a fresh CREATE (never reaches PatchMotion) or, for a still-exiting /
            // cancelled-exit reproduction, plays no competing animation of its own (CancelExit's reversal, or
            // no-op) — so the one real overlap is a GHOST re-patched on a LATER render while still exiting
            // (skipping the ghost dispatch's own CancelEnter, which only runs the FIRST time
            // state.Exiting.Add(key) succeeds): IsExiting catches exactly that window.
            if (newNode.Transition != null && !_ctx.StyleAnimationScheduler.IsExiting(element)
                && !SequenceEqual(oldVariantClasses, newVariantClasses))
            {
                _ctx.StyleAnimationScheduler.PlayVariantEnter(element, oldVariantClasses, newVariantClasses,
                    newNode.Transition, onComplete: null, additionalDelaySec: extraDelaySec);
            }

            // MotionNode has no Styles diff, so the shared passes follow PatchCommon (which reconciles
            // children) directly. A Motion never renders skew (the animation node never attaches a sheared
            // silhouette), so its gradient always takes the straight background-image path even with skew
            // classes present (gradientSkewable: false).
            ApplyPostChildrenClassPasses(element, appliedNew, gradientSkewable: false);
            // A Motion carries no shadow paint: the create path warns and skips a shadow-* on a Motion (the
            // animation node owns its transition; a shadow belongs on a wrapped Div), so there is never a
            // binding to update — and the patch must not start attaching one. Ring likewise never wraps a
            // Motion (allowWrap false); a Motion thus carries no ring binding, so this only updates/unwraps an
            // (absent by this rule) binding.
            _appliers.ApplyRingOnPatch(element, appliedNew, suppress: false, allowWrap: false);

            // Shared-element layout animation (Framer's layoutId): independent of the variant swap
            // above — runs from the ACTUAL resolved-rect delta, not a class-defined from/to pair — so
            // it fires whether or not this patch also changed Variants/Animate. Falls back to
            // StyleTransitionConfig's own documented spring defaults (Stiffness 100 / Damping 10 /
            // Mass 1) when this Motion declares no Transition, since a layoutId tween needs SOME spring
            // shape to animate with and Velvet does not imitate Framer's implicit default transition
            // for the variant swap either.
            if (newNode.LayoutId != null)
            {
                var t = newNode.Transition;
                MotionLayoutIdDriver.OnPatched(element, newNode.LayoutId,
                    t?.Stiffness ?? 100f, t?.Damping ?? 10f, t?.Mass ?? 1f, _ctx);
            }
        }

        // Resolves the MotionOrchestrationFrame this node exposes to its OWN inheriting children:
        // - A FRESH frame when this node's propagated label just changed AND its own Transition declares
        //   StaggerChildrenSec / DelayChildrenSec / a non-Together When — establishing a new stagger sequence
        //   (When == AfterChildren is not orchestrated; it warns once here and falls back to Together's
        //   no-extra-delay semantics for the parent's own swap — see TransitionWhen.AfterChildren). The frame's
        //   base offset is this node's own [DelaySec, DelaySec + DurationSec] span when When == BeforeChildren
        //   (children wait for the delay AND the swap, not just the swap), PLUS extraDelaySec — the delay THIS
        //   node itself claimed a moment ago in PatchMotion when it is, itself, an inheriting descendant of a
        //   FURTHER-OUT orchestration. Folding extraDelaySec in regardless of When matters because this node's
        //   own swap does not start at render-commit time when extraDelaySec > 0 — without it, a claim from the
        //   fresh frame below would be measured as if this node's (already-delayed) swap started immediately,
        //   letting a grandchild start animating before its own parent does.
        // - null when this node drives its children via its OWN explicit Animate: an ambient orchestration
        //   meant for a sibling branch must not leak through a node that is no longer inheriting (it computes
        //   its own child label independently of the ambient one, so it is a natural cut point).
        // - Otherwise (a pure pass-through inheritor with no orchestration of its own) the ambient frame is
        //   passed through UNCHANGED, so a non-orchestrating intermediate layer does not interrupt an outer
        //   ancestor's stagger sequence reaching its own grandchildren.
        private static MotionOrchestrationFrame? ResolveChildOrchestration(
            MotionNode newNode, bool childLabelChanged, MotionOrchestrationFrame? ambientOrchestration, float extraDelaySec)
        {
            var transition = newNode.Transition;
            var hasOwnOrchestration = transition != null
                && (transition.StaggerChildrenSec > 0f || transition.DelayChildrenSec > 0f
                    || transition.When != TransitionWhen.Together);
            if (childLabelChanged && hasOwnOrchestration)
            {
                if (transition.When == TransitionWhen.AfterChildren)
                {
                    FiberLogger.LogWarning("Motion",
                        "transition.When = AfterChildren is not yet orchestrated for label propagation; "
                        + "children animate as if When = Together (no wait for the parent's own transition).");
                }
                var extraBeforeChildrenSec = transition.When == TransitionWhen.BeforeChildren
                    ? transition.DelaySec + transition.DurationSec
                    : 0f;
                return new MotionOrchestrationFrame(transition.DelayChildrenSec, transition.StaggerChildrenSec,
                    extraBeforeChildrenSec + extraDelaySec);
            }
            return newNode.Animate != null ? null : ambientOrchestration;
        }

        // Applies the diff for a PortalNode. Reconciles only this Portal's own slot range
        // (PortalState.slotStart .. slotStart + slotLength) against the target,
        // preserving children placed by other Portals sharing the same target. When the slot
        // range grows or shrinks, downstream Portals whose ranges sit after this one have their
        // slotStart shifted by the delta so subsequent patches stay correctly addressed.
        // When the Portal target is the same element currently being reconciled, the
        // "old.index == DOM index" invariant in ReconcileKeyed breaks down.
        // This combination is forbidden by design (the target must not itself be a Reconcile subject).
        internal void PatchPortal(VisualElement placeholder, PortalNode oldNode, PortalNode newNode)
        {
            VisualElement? target;
            string describe;
            if (newNode.Layer is { } layer)
            {
                // The per-layer framework host was created when the mount drained and persists
                // until reconciler disposal, so a patch resolves it from the table. A record whose
                // GameObject a scene unload killed reads as dead here and counts as missing.
                describe = layer.ToString();
                if (!_ctx.LayerHosts.TryGetValue(layer, out var layerHost) || layerHost.Document == null)
                {
                    FiberLogger.LogWarning("Portal", $"Layer host for \"{describe}\" is missing. Children will not be rendered.");
                    return;
                }
                // Recurring re-sync point for late declaring resolution and runtime drift.
                PanelHostFactory.SyncDeclaring(layerHost, layer, placeholder.panel, _ctx);
                target = layerHost.Document.rootVisualElement;
                if (oldNode.FocusOrder != newNode.FocusOrder)
                {
                    FiberFocusNavigator.ConfigureChainedPlaceholder(placeholder, layerHost,
                        newNode.FocusOrder == PanelFocusOrder.Chained, _ctx);
                }
            }
            else
            {
                describe = newNode.TargetId!;
                if (_ctx.PortalState.TryGetValue(placeholder, out var recorded) && recorded.Target != null)
                {
                    // A live portal keeps the target its children mounted into: re-registering the
                    // id only points FUTURE portals elsewhere, and patching into a re-registered
                    // element would diff this portal's slot range against another element's
                    // children.
                    target = recorded.Target;
                }
                else
                {
                    // Mounted before the id was registered (the mount warned and recorded no
                    // target): resolve fresh so the first patch after registration heals the mount.
                    target = FiberPortalRegistry.Get(describe);
                    if (target == null)
                    {
                        FiberLogger.LogWarning("Portal", $"Target \"{describe}\" is not registered. Children will not be rendered.");
                        return;
                    }
                }
            }

            PatchPortalChildren(placeholder, target, oldNode.Children, newNode.Children, describe);
        }

        // Applies the diff for a WorldSpaceNode: the host transform and virtual panel size follow
        // the node, and the children reconcile through the same slot bookkeeping every portal
        // flavor uses (the world-space host root is the recorded target).
        internal void PatchWorldSpace(VisualElement placeholder, WorldSpaceNode oldNode, WorldSpaceNode newNode)
        {
            if (!_ctx.WorldSpaceBindings.TryGetValue(placeholder, out var record))
            {
                FiberLogger.LogError("WorldSpace", "Host record missing for a world-space placeholder. Patch skipped.");
                return;
            }
            if (record.Document == null)
            {
                // A scene unload can kill the host GameObject while the owning fiber tree survives
                // (a persistent root anchoring per-scene world-space UI). Patching a dead document
                // would throw out of the whole reconcile pass, so every patch skips it on this same
                // warning path — the record stays so later patches keep landing here rather than
                // degrading into the missing-record corruption error (mirrors the layer flavor);
                // remount the world-space node to rebuild its host.
                FiberLogger.LogWarning("WorldSpace",
                    "Host died externally (scene unload?). Patch skipped; remount the world-space node to rebuild its host.");
                return;
            }
            // Recurring re-sync point for late declaring resolution and runtime drift (null layer:
            // world-space panels depth-sort in the scene, not by sorting order).
            PanelHostFactory.SyncDeclaring(record, null, placeholder.panel, _ctx);

            if (oldNode.Position != newNode.Position || oldNode.Rotation != newNode.Rotation)
            {
                record.Document.transform.SetPositionAndRotation(newNode.Position, newNode.Rotation);
            }
            if (oldNode.PanelSize != newNode.PanelSize)
            {
                record.Document.worldSpaceSize = newNode.PanelSize;
            }
            if (oldNode.FocusOrder != newNode.FocusOrder)
            {
                FiberFocusNavigator.ConfigureChainedPlaceholder(placeholder, record,
                    newNode.FocusOrder == PanelFocusOrder.Chained, _ctx);
            }
            PatchPortalChildren(placeholder, record.Document.rootVisualElement, oldNode.Children, newNode.Children, "world-space");
        }

        // The shared slot-range child patch for every portal flavor (registry, layer, world-space):
        // reconciles only this placeholder's own slot range against the target and shifts the
        // downstream ranges on the same target by the growth delta. describe names the target in
        // diagnostics only.
        private void PatchPortalChildren(
            VisualElement placeholder, VisualElement target,
            VNode?[]? oldChildrenRaw, VNode?[]? newChildrenRaw, string describe)
        {
            UnityEngine.Debug.Assert(
                target != placeholder.parent,
                "[Portal] Portal target must not be the same element currently being reconciled. " +
                "This would invalidate DOM index invariants in ReconcileKeyed.");

            if (!_ctx.PortalState.TryGetValue(placeholder, out var prevState))
            {
                // PortalState missing means CreateElement never recorded this Portal's slot range
                // (mounting was skipped or state was cleared mid-patch). Appending blindly would
                // alias another Portal's slot, so skip patch and surface the inconsistency.
                FiberLogger.LogError("Portal", $"PortalState missing for placeholder targeting \"{describe}\". Patch skipped to avoid corrupting other Portals' slot ranges.");
                return;
            }

            var oldChildren = oldChildrenRaw ?? Array.Empty<VNode>();
            var newChildren = newChildrenRaw ?? Array.Empty<VNode>();
            var beforeTailCount = target.childCount;
            _host.ReconcileChildren(target, oldChildren, newChildren, slotStart: prevState.SlotStart);
            // (beforeTailCount - prevState.SlotLength) is the count of target children that do NOT belong to
            // this Portal's slot — unchanged by the reconcile above. Subtracting it from the new total childCount
            // isolates this Portal's new slot length without re-counting the foreign children.
            var newSlotLength = target.childCount - (beforeTailCount - prevState.SlotLength);
            var delta = newSlotLength - prevState.SlotLength;

            // The RESOLVED target is written back: a portal that mounted before its id registered
            // recorded no target, and the first healing patch must fill it in so the slot-shift
            // grouping, the eventual cleanup, and the has-variant portal-target sweep all see where
            // the children actually live. For an already-recorded portal this rewrites the same
            // reference.
            _ctx.PortalState[placeholder] = prevState with { Target = target, SlotLength = newSlotLength };

            PortalSlotTracker.ShiftSlotStartsAfter(_ctx.PortalState, target, prevState.SlotStart, delta, placeholder);
        }

        // Applies the diff for a ContextProviderNode.
        // Pushes the new context value, fires IReconcilerHost.NotifyContextValueChange
        // when the value changed since the previous render, then recursively reconciles the
        // Provider's children against the wrapper emitted by
        // FiberNodeFactory.CreateElement's ContextProviderNode case. Used when the
        // Provider is reached as a node-typed keyed entry (e.g. inside an AnimatePresence subtree,
        // or as a MemoNode's resolved inner) — paths where
        // ChildReconciler.ExpandInlineRecursive's inline Provider expansion does not apply.
        internal void PatchContextProvider(VisualElement wrapper, ContextProviderNode oldNode, ContextProviderNode newNode)
        {
            newNode.PushContext(_ctx.ComponentContextStack);
            try
            {
                if (newNode.HasValueChanged(oldNode))
                {
                    _host.NotifyContextValueChange(newNode);
                    // A replaced VNode-valued provider value has no other retirement point: the sweep
                    // deliberately never returns provider values (a consumer may hold the CURRENT one),
                    // but a superseded value is out of distribution for good — every subscribed consumer
                    // re-renders off the change notification above and commits the NEW value's nodes,
                    // and the pass-scoped release staging keeps the old parts un-rentable until those
                    // re-renders have run. A consumer that committed the old node retires it through its
                    // own old tree too; pool returns are idempotent, so the overlap is harmless.
                    var oldValueRoot = oldNode.BoxedValueForRecycleMark;
                    if (oldValueRoot != null && !ReferenceEquals(oldValueRoot, newNode.BoxedValueForRecycleMark))
                    {
                        switch (oldValueRoot)
                        {
                            case VNode oldValueNode:
                                FiberTreeReturn.ReturnRetiredTree(
                                    FiberTreeReturn.NormalizeToArray(oldValueNode), _ctx.FiberStack.Current);
                                break;
                            case VNode?[] oldValueTree:
                                FiberTreeReturn.ReturnRetiredTree(oldValueTree, _ctx.FiberStack.Current);
                                break;
                        }
                    }
                }
                var oldChildren = oldNode.Children ?? Array.Empty<VNode>();
                var newChildren = newNode.Children ?? Array.Empty<VNode>();
                _host.ReconcileChildren(wrapper, oldChildren, newChildren);
            }
            finally
            {
                newNode.PopContext(_ctx.ComponentContextStack);
            }
        }

        #endregion

        #region Diff Helpers

        // ClassList diff. Skips work via fast paths using ReferenceEquals and SequenceEqual.
        // Uses linear comparison for sizes ≤ 8 and a HashSet otherwise.
        // Returns whether the class list changed content (false when either fast path hit), so the caller
        // can gate variant/font re-derivation on real change rather than array identity — a
        // content-identical but freshly-allocated array must not re-derive anything.
        internal bool DiffClassList(VisualElement element, string[] oldClasses, string[] newClasses)
        {
            oldClasses ??= Array.Empty<string>();
            newClasses ??= Array.Empty<string>();
            if (ReferenceEquals(oldClasses, newClasses))
            {
                return false;
            }

            if (SequenceEqual(oldClasses, newClasses))
            {
                return false;
            }

            const int linearThreshold = 8;
            var removedArbitrary = oldClasses.Length <= linearThreshold && newClasses.Length <= linearThreshold
                ? DiffClassListLinear(element, oldClasses, newClasses, out var removedFilterFamily)
                : DiffClassListWithHashSet(element, oldClasses, newClasses, out removedFilterFamily);

            // Arbitrary values are cleared per property, so removing a value of the same property
            // also clears any other values that should have remained.
            // Reapply the arbitrary values from the new list only when a removal occurred to preserve
            // consistency. Filter-family survivors are exempt unless a filter-family token itself was
            // removed: other properties' clears never touch their per-name layers and every real filter
            // mutation recomposes inline during the diff, so re-resolving survivors here would only
            // repeat registry lookups to rebuild an identical composed list.
            if (removedArbitrary)
            {
                ReapplyArbitraryValues(element, newClasses, skipFilterFamily: !removedFilterFamily);
            }
            return true;
        }

        // Re-asserts the class list's inline-resolved (arbitrary / preset) values. Shared with the
        // wrapper element appliers, which call it after detaching a motion that owned a shared inline
        // slot. skipFilterFamily exempts composed-filter tokens for callers that know no filter layer
        // was disturbed (the class-diff reapply); full-scrub callers keep the default full pass.
        internal static void ReapplyArbitraryValues(VisualElement element, string[] classes, bool skipFilterFamily = false)
        {
            foreach (var rawCls in classes)
            {
                if (string.IsNullOrEmpty(rawCls) || StyleVariantClass.IsVariant(rawCls)
                    || StyleStructuralVariantClass.IsStructural(rawCls)
                    || StyleHasVariantClass.IsHas(rawCls)
                    || StyleAttributeVariantClass.IsAttribute(rawCls)
                    || StyleSupportsVariantClass.IsSupports(rawCls))
                {
                    continue;
                }

                // Strip the important bang so it reapplies on the same Important layer AddClass used.
                var cls = StyleArbitraryValueResolver.StripImportant(rawCls, out var important);
                if (!StyleArbitraryValueResolver.IsInlineResolved(cls))
                {
                    continue;
                }
                if (skipFilterFamily && StyleArbitraryValueResolver.IsFilterFamilyToken(cls))
                {
                    continue;
                }
                var priority = important ? StyleLayerPriority.Important : StyleLayerPriority.Base;

                // No class-list fallback: an inline-classified token unresolvable here is owned by another
                // resolver (e.g. font-[..] by StyleFontResolver) and must not enter the USS class list.
                StyleArbitraryValueResolver.ApplyClassToken(element, cls, priority, addToClassListFallback: false);
            }
        }

        private static bool SequenceEqual(string[] a, string[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool DiffClassListLinear(VisualElement element, string[] oldClasses, string[] newClasses, out bool removedFilterFamily)
        {
            var removedArbitrary = false;
            removedFilterFamily = false;
            foreach (var cls in oldClasses)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }

                var found = false;
                foreach (var newCls in newClasses)
                {
                    if (cls == newCls) { found = true; break; }
                }
                if (!found)
                {
                    var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                    if (StyleArbitraryValueResolver.IsInlineResolved(core))
                    {
                        removedArbitrary = true;
                        removedFilterFamily |= StyleArbitraryValueResolver.IsFilterFamilyToken(core);
                    }

                    RemoveClass(element, cls);
                }
            }
            foreach (var cls in newClasses)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }

                var found = false;
                foreach (var oldCls in oldClasses)
                {
                    if (cls == oldCls) { found = true; break; }
                }
                if (!found)
                {
                    AddClass(element, cls);
                }
            }
            return removedArbitrary;
        }

        private bool DiffClassListWithHashSet(VisualElement element, string[] oldClasses, string[] newClasses, out bool removedFilterFamily)
        {
            var removedArbitrary = false;
            removedFilterFamily = false;

            // Rent from the pool to make this re-entrant-safe.
            // Even along the path PatchElement → DiffClassList → PatchCommon → Reconcile (recursive)
            // → PatchElement → DiffClassList, each level holds its own set, so they do not collide.
            var oldSet = _ctx.BufferPool.RentClassSet();
            var newSet = _ctx.BufferPool.RentClassSet();
            try
            {
                oldSet.UnionWith(oldClasses);
                newSet.UnionWith(newClasses);

                foreach (var cls in oldClasses)
                {
                    if (!string.IsNullOrEmpty(cls) && !newSet.Contains(cls))
                    {
                        var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                        if (StyleArbitraryValueResolver.IsInlineResolved(core))
                        {
                            removedArbitrary = true;
                            removedFilterFamily |= StyleArbitraryValueResolver.IsFilterFamilyToken(core);
                        }

                        RemoveClass(element, cls);
                    }
                }
                foreach (var cls in newClasses)
                {
                    if (!string.IsNullOrEmpty(cls) && !oldSet.Contains(cls))
                    {
                        AddClass(element, cls);
                    }
                }
                return removedArbitrary;
            }
            finally
            {
                _ctx.BufferPool.ReturnClassSet(oldSet);
                _ctx.BufferPool.ReturnClassSet(newSet);
            }
        }

        private static void AddClass(VisualElement element, string cls)
        {
            // State-variant tokens (hover:/focus:/active:) are not real classes; the variant
            // manipulator (configured separately) owns them. Never add them to the class list.
            if (StyleVariantClass.IsVariant(cls))
            {
                return;
            }

            // Structural variants (first:/last:/[&:nth-child(N)]:) are owned by the reconciler's structural
            // pass (evaluated against sibling position); never added as classes.
            if (StyleStructuralVariantClass.IsStructural(cls))
            {
                return;
            }

            // has-[...] variants (parent styled by a descendant condition) are owned by the has-variant
            // manipulator / the has-class post-children pass; never added as classes.
            if (StyleHasVariantClass.IsHas(cls))
            {
                return;
            }

            // data-[...] / aria-[...] variants (element styled by its own carried attribute) are owned by
            // the attribute side-table; never added as classes.
            if (StyleAttributeVariantClass.IsAttribute(cls))
            {
                return;
            }

            // supports-[...] feature-query variants (element styled when the engine supports a declaration)
            // are owned by the supports side-table (static / always-applied in UITK); never added as classes.
            if (StyleSupportsVariantClass.IsSupports(cls))
            {
                return;
            }

            // font-[...] arbitrary font classes are resolved from the whole class array by
            // StyleFontResolver and applied as inline style; like other arbitrary values they must not
            // enter the USS class list.
            if (StyleFontClass.IsArbitraryFontClass(cls))
            {
                return;
            }

            // Important modifier (!utility / utility!): strip the bang; when present, elevate the
            // inline-resolved utility to the Important layer. A class-only utility's bang is inert.
            var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
            if (string.IsNullOrEmpty(core))
            {
                return;
            }
            var priority = important ? StyleLayerPriority.Important : StyleLayerPriority.Base;

            // Plain classes (the common case) go straight to the USS class list and skip both resolvers;
            // inline-value tokens (bracketed, color-opacity, static-scale) resolve to inline style.
            if (!StyleArbitraryValueResolver.IsInlineResolved(core))
            {
                element.AddToClassList(core);
                return;
            }

            StyleArbitraryValueResolver.ApplyClassToken(element, core, priority);
        }

        private static void RemoveClass(VisualElement element, string cls)
        {
            // State-variant tokens are owned by the variant manipulator, never the class list.
            if (StyleVariantClass.IsVariant(cls))
            {
                return;
            }

            // Structural variants never entered the class list (see AddClass); their applied payloads are
            // cleared by the structural config pass on the class change, not here.
            if (StyleStructuralVariantClass.IsStructural(cls))
            {
                return;
            }

            // has-[...] variants never entered the class list (see AddClass); their applied payloads are
            // cleared by the has-variant config pass on the class change, not here.
            if (StyleHasVariantClass.IsHas(cls))
            {
                return;
            }

            // data-[...] / aria-[...] variants never entered the class list (see AddClass); their applied
            // payloads are cleared by the attribute config pass on the class change, not here.
            if (StyleAttributeVariantClass.IsAttribute(cls))
            {
                return;
            }

            // supports-[...] feature-query variants never entered the class list (see AddClass); their
            // applied payloads are cleared by the supports config pass on the class change, not here.
            if (StyleSupportsVariantClass.IsSupports(cls))
            {
                return;
            }

            // font-[...] arbitrary font classes never entered the class list (see AddClass); the inline
            // style they drove is cleared by StyleFontResolver on the class change, not here.
            if (StyleFontClass.IsArbitraryFontClass(cls))
            {
                return;
            }

            // Important modifier: strip the bang and clear the same layer AddClass applied it on.
            var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
            if (string.IsNullOrEmpty(core))
            {
                return;
            }
            var priority = important ? StyleLayerPriority.Important : StyleLayerPriority.Base;

            // Plain classes (the common case) leave the USS class list directly; inline-value tokens
            // (bracketed, color-opacity, static-scale) clear the inline style they applied.
            if (!StyleArbitraryValueResolver.IsInlineResolved(core))
            {
                element.RemoveFromClassList(core);
                return;
            }

            StyleArbitraryValueResolver.ClearClassToken(element, core, priority);
        }

        // Applies the diff of element props between renders.
        // Maintenance note: this method diffs each property of FiberElementProps
        // individually, so any new property added to FiberElementProps must also receive a matching
        // branch here. Missing the addition causes the new property's diff to be silently ignored
        // (the prop applies on the initial mount but never updates on a re-render) without a compile error.
        // Exception: the Data / Aria attribute props are NOT diffed here — they drive the data-/aria- variant
        // side-table (no direct VisualElement property to set), so PatchBaseElement re-syncs them via
        // ApplyAttributes right after this call (which rebuilds the store unconditionally, so a change is
        // always observed).
        internal void DiffProps(VisualElement element, FiberElementProps? oldProps, FiberElementProps? newProps)
        {
            oldProps ??= FiberElementProps.Empty;
            newProps ??= FiberElementProps.Empty;

            if (oldProps.Text != newProps.Text)
            {
                FiberPropApplier.ApplyText(element, newProps.Text);
            }

            if (oldProps.Tooltip != newProps.Tooltip)
            {
                FiberPropApplier.ApplyTooltip(element, newProps.Tooltip);
            }

            if (oldProps.Enabled != newProps.Enabled)
            {
                FiberPropApplier.ApplyEnabled(element, newProps.Enabled);
            }

            if (oldProps.Visible != newProps.Visible)
            {
                FiberPropApplier.ApplyVisible(element, newProps.Visible);
            }

            if (oldProps.Focusable != newProps.Focusable)
            {
                FiberPropApplier.ApplyFocusable(element, newProps.Focusable);
                // A declared Focusable now owns the flag: the drag session's transient keyboard-focus
                // anchor must not restore over it on close (the value-diffed prop would never re-apply).
                _ctx.ActiveDrag?.OnSourceFocusableDeclared(element);
            }

            if (oldProps.TabIndex != newProps.TabIndex)
            {
                FiberPropApplier.ApplyTabIndex(element, newProps.TabIndex);
            }

            if (oldProps.DelegatesFocus != newProps.DelegatesFocus)
            {
                FiberPropApplier.ApplyDelegatesFocus(element, newProps.DelegatesFocus);
            }

            if (!Equals(oldProps.FieldValue, newProps.FieldValue))
            {
                FiberPropApplier.ApplyFieldValue(element, newProps.FieldValue);
            }

            if (oldProps.Slider != newProps.Slider)
            {
                FiberPropApplier.ApplySlider(element, newProps.Slider);
            }

            if (oldProps.ScrollView != newProps.ScrollView)
            {
                FiberPropApplier.ApplyScrollView(element, newProps.ScrollView);
            }

            if (oldProps.TextField != newProps.TextField)
            {
                FiberPropApplier.ApplyTextField(element, newProps.TextField);
            }

            if (oldProps.Choices != newProps.Choices)
            {
                FiberPropApplier.ApplyChoices(element, newProps.Choices);
            }

            // Record (value) equality: a re-render carrying the same camera + scale in a fresh record is
            // not a change, so a camera swap / removal is the only thing that lands here — a class-driven
            // RESIZE arrives through the binding's geometry callback instead, never through this diff.
            if (oldProps.SceneView != newProps.SceneView)
            {
                _appliers.ApplySceneView(element, newProps.SceneView);
            }

            // Record (value) equality, like SceneView above: only an effect swap / removal (or a
            // trigger / pixel-scale change) lands here — a re-render carrying identical settings in a
            // fresh record is not a change.
            if (oldProps.Particles != newProps.Particles)
            {
                _appliers.ApplyParticles(element, newProps.Particles);
            }

            // Record (value) equality, like SceneView/Particles above: a re-render carrying the same
            // target/camera/offset in a fresh record is not a change — the position itself updates every
            // tick regardless (AnchoredDriver's own recurring Sync), not through this diff.
            if (oldProps.Anchored != newProps.Anchored)
            {
                _appliers.ApplyAnchored(element, newProps.Anchored);
            }

            // Record (value) equality, like the bindings above: only an actual scope-behavior change (or a
            // scope arriving/leaving) lands here.
            if (oldProps.FocusScope != newProps.FocusScope)
            {
                _appliers.ApplyFocusScope(element, newProps.FocusScope);
            }

            // Record (value) equality for all four drag-and-drop slots: fresh-but-equal settings never
            // re-attach, and delegate-bearing records refresh their binding in place on any inequality.
            if (oldProps.DndContext != newProps.DndContext)
            {
                _appliers.ApplyDndContext(element, newProps.DndContext);
            }
            if (oldProps.Draggable != newProps.Draggable)
            {
                _appliers.ApplyDraggable(element, newProps.Draggable);
            }
            if (oldProps.Droppable != newProps.Droppable)
            {
                _appliers.ApplyDroppable(element, newProps.Droppable);
            }
            if (oldProps.DragOverlay != newProps.DragOverlay)
            {
                _appliers.ApplyDragOverlay(element, newProps.DragOverlay);
            }
        }

        // Applies the StyleOverrides diff to element.style.
        // Maintenance note: this method diffs each property of StyleOverrides
        // individually, so any new property added to StyleOverrides must also receive a matching
        // branch here. Missing the addition causes the new property's diff to be silently ignored
        // without a compile error.
        internal void DiffStyles(VisualElement element, StyleOverrides? oldStyles, StyleOverrides? newStyles)
        {
            oldStyles ??= StyleOverrides.Empty;
            newStyles ??= StyleOverrides.Empty;

            // Routed through the SceneView ownership gate: while a live camera texture owns the slot
            // the poster is deferred (and restored on release); with no live texture — no camera yet,
            // camera removed, plain elements — the write lands directly.
            if (!Equals(oldStyles.BackgroundImage, newStyles.BackgroundImage))
            {
                SceneViewElement.WriteBackground(element, newStyles.BackgroundImage ?? StyleKeyword.Null);
            }

            if (!Equals(oldStyles.BackgroundColor, newStyles.BackgroundColor))
            {
                element.style.backgroundColor = newStyles.BackgroundColor ?? StyleKeyword.Null;
            }

            if (!Equals(oldStyles.Color, newStyles.Color))
            {
                element.style.color = newStyles.Color ?? StyleKeyword.Null;
            }
        }

        #endregion

        #region Wrapper Resolution

        // Reconciler-facing wrapper<->inner resolution. The mechanics live in WrapperInfrastructure
        // (shared with the wrapper element appliers); the patcher fronts them for the reconciler,
        // which talks to the patcher rather than reaching into the shared collaborator.

        // Returns the inner real element when the input is a wrapper container; else the input.
        internal VisualElement ResolveWrapped(VisualElement domElement)
            => _wrappers.ResolveWrapped(domElement);

        // The inverse of ResolveWrapped: the element's current top-level DOM node — its
        // wrapper when it is the inner of one, else itself. Callers that hold a pre-patch element
        // reference (the VirtualList bridge) use this after a patch, because a class-driven
        // wrap/unwrap during the patch swaps which element occupies the slot.
        internal VisualElement ResolveOuter(VisualElement element)
            => _wrappers.ResolveOuter(element);

        #endregion

        #region Component Handling

        // Resolves the matched RouteMatch for an Outlet by reading
        // RouterContext.Location / RouterContext.Depth from the live
        // ComponentContextStack (valid because the Outlet is reconciled during the
        // walk's commit while ancestor Providers are pushed), and returns the depth to push for the
        // matched route's Component (routeDepth = current depth + 1). Returns false when there
        // is no match to render (no location, depth out of range, or the matched Route has no element).
        internal bool ResolveOutletMatch(
            out ComponentNode? routeElement,
            out int routeDepth,
            out RouteMatch match)
        {
            // RouterContext is read from the live cursor: CreateElement(Outlet) / PatchNode(Outlet) run
            // during the reconcile walk's commit while the ancestor Providers are still pushed, so the
            // live ComponentContextStack reflects the Outlet's enclosing Location / Depth. A standalone
            // re-render (a layout component's own setState) reconstructs those ancestor Providers onto the
            // cursor via FiberContextSpine before the body / its Outlet reconcile runs, so the live read
            // is correct on that path too. The matched route Component is then mounted with Depth+1 pushed
            // live (see the Outlet mount sites).
            var location = _ctx.ComponentContextStack.Get(RouterContext.Location);
            var depth = _ctx.ComponentContextStack.Get(RouterContext.Depth);

            if (location?.Matches == null || depth >= location.Matches.Count)
            {
                routeElement = null;
                routeDepth = 0;
                match = null!;
                return false;
            }

            match = location.Matches[depth];

            // A loader error bubbles to the nearest ancestor route (at or above the
            // errored route) that defines an ErrorElement. That boundary route renders its ErrorElement in
            // place of its Element and descendant Outlet subtree; ancestors above the boundary render
            // normally, and routes below the boundary do not render. Errors are keyed by RouteId on
            // RouterContext.Errors, read from the live cursor (reconstructed via FiberContextSpine on a
            // standalone re-render, the same as Location / Depth above).
            var errors = _ctx.ComponentContextStack.Get(RouterContext.Errors);

            var boundaryDepth = ResolveErrorBoundaryDepth(location.Matches, errors);
            if (boundaryDepth >= 0)
            {
                var boundaryElement = location.Matches[boundaryDepth].Route?.ErrorElement;

                if (depth == boundaryDepth)
                {
                    if (boundaryElement == null)
                    {
                        // Implicit root boundary with no ErrorElement (Velvet has no default error surface):
                        // the error bubbles to the root and, with no boundary defined anywhere in the
                        // chain, the erroring subtree renders nothing.
                        routeElement = null;
                        routeDepth = 0;
                        match = null!;
                        return false;
                    }

                    // This Outlet renders the boundary route's ErrorElement in place of its Element.
                    routeElement = boundaryElement;
                    routeDepth = depth + 1;
                    return true;
                }

                if (depth > boundaryDepth)
                {
                    // Below the boundary: the ErrorElement subtree replaced everything here, so render nothing.
                    routeElement = null;
                    routeDepth = 0;
                    match = null!;
                    return false;
                }

                // depth < boundaryDepth: an ancestor above the boundary renders normally below.
            }

            routeElement = match.Route?.Element;
            if (routeElement == null)
            {
                routeDepth = 0;
                match = null!;
                return false;
            }

            routeDepth = depth + 1;
            return true;
        }

        // Computes the error boundary depth (index into the parent-first matched chain) for the current
        // errors: the nearest route, scanning from the deepest errored route up toward the root, that
        // defines an RouteDefinition.ErrorElement. The boundary route's ErrorElement renders
        // in place of its Element and descendant Outlet subtree. Returns -1 when no
        // route errored. When a route errored but no route at or above it defines an ErrorElement, returns
        // the root index 0 as an implicit boundary (the error bubbles all the way to the root):
        // because Velvet has no default error surface, the caller renders nothing at that boundary, so the
        // erroring matched tree renders nothing.
        private static int ResolveErrorBoundaryDepth(
            IReadOnlyList<RouteMatch> matches,
            IReadOnlyDictionary<string, Exception> errors)
        {
            if (errors == null || errors.Count == 0 || matches == null || matches.Count == 0)
            {
                return -1;
            }

            // The deepest errored route determines the boundary: a deeper error truncates the chain at the
            // nearest boundary at or above it, which is at least as deep as any shallower error's boundary.
            var deepestErrored = -1;
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var routeId = matches[i].RouteId;
                if (routeId != null && errors.ContainsKey(routeId))
                {
                    deepestErrored = i;
                    break;
                }
            }

            if (deepestErrored < 0)
            {
                return -1;
            }

            for (var i = deepestErrored; i >= 0; i--)
            {
                if (matches[i].Route?.ErrorElement != null)
                {
                    return i;
                }
            }

            // No route at or above the errored route defines an ErrorElement: bubble to the implicit root
            // boundary. The root has no ErrorElement, so the caller renders nothing there.
            return 0;
        }

        internal void HandleComponentMount(VisualElement wrapper, ComponentNode? componentNode)
        {
            if (componentNode == null) return;
            // The wrapper-mounted Component (an Outlet route Component) is mounted during the
            // reconcile walk's commit while its enclosing Providers — and, for an Outlet, the pushed
            // Depth+1 — are live on the ComponentContextStack. UseContext reads that live cursor, so no
            // snapshot is captured here; an isolated re-render reconstructs the enclosing Providers via
            // FiberContextSpine.
            componentNode.Mount(_ctx.ComponentRegistry, wrapper);
        }

        #endregion

        #region Variant Manipulator

        // Configures (creates / updates / removes) the element's StyleVariantManipulator
        // from the state-variant tokens (hover:/focus:/active:) found in classNames.
        // Mirrors ApplyGestureManipulator.
        internal void ApplyVariantManipulator(VisualElement element, string[] classNames)
        {
            var hover = ExtractVariant(classNames, StyleVariantKind.Hover);
            var focus = ExtractVariant(classNames, StyleVariantKind.Focus);
            var focusVisible = ExtractVariant(classNames, StyleVariantKind.FocusVisible);
            var active = ExtractVariant(classNames, StyleVariantKind.Active);
            var @checked = ExtractVariant(classNames, StyleVariantKind.Checked);
            var hasAny = hover.Length > 0 || focus.Length > 0 || focusVisible.Length > 0
                || active.Length > 0 || @checked.Length > 0;

            if (_ctx.VariantManipulators.TryGetValue(element, out var existing))
            {
                if (hasAny)
                {
                    existing.UpdatePayloads(hover, focus, focusVisible, active, @checked);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.VariantManipulators.Remove(element);
                }
            }
            else if (hasAny)
            {
                var manipulator = new StyleVariantManipulator(_ctx, hover, focus, focusVisible, active, @checked);
                element.AddManipulator(manipulator);
                _ctx.VariantManipulators[element] = manipulator;
            }
        }

        private static string[] ExtractVariant(string[] classNames, StyleVariantKind kind)
        {
            if (classNames == null || classNames.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string>? payloads = null;
            foreach (var cls in classNames)
            {
                if (StyleVariantClass.TryParse(cls, out var k, out var payload) && k == kind)
                {
                    (payloads ??= new List<string>()).Add(payload ?? string.Empty);
                }
            }

            return payloads?.ToArray() ?? Array.Empty<string>();
        }

        // Configures the element's StyleConditionalVariantManipulator from the responsive
        // (sm:/md:/lg:/xl:/2xl:) and dark: tokens in classNames. Mirrors
        // ApplyVariantManipulator.
        internal void ApplyConditionalVariantManipulator(VisualElement element, string[] classNames)
        {
            var responsive = new[]
            {
                ExtractVariant(classNames, StyleVariantKind.Sm),
                ExtractVariant(classNames, StyleVariantKind.Md),
                ExtractVariant(classNames, StyleVariantKind.Lg),
                ExtractVariant(classNames, StyleVariantKind.Xl),
                ExtractVariant(classNames, StyleVariantKind.Xxl),
            };
            var dark = ExtractVariant(classNames, StyleVariantKind.Dark);

            var hasAny = dark.Length > 0;
            for (var i = 0; i < responsive.Length && !hasAny; i++)
            {
                hasAny = responsive[i].Length > 0;
            }

            if (_ctx.ConditionalVariantManipulators.TryGetValue(element, out var existing))
            {
                if (hasAny)
                {
                    existing.UpdatePayloads(responsive, dark);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.ConditionalVariantManipulators.Remove(element);
                }
            }
            else if (hasAny)
            {
                var manipulator = new StyleConditionalVariantManipulator(_ctx, responsive, dark);
                element.AddManipulator(manipulator);
                _ctx.ConditionalVariantManipulators[element] = manipulator;
            }
        }

        // Configures the element's StyleRelationalVariantManipulator from the group-*/peer- tokens (incl. the
        // named group-*/name · peer-*/name forms) in classNames. Mirrors ApplyVariantManipulator.
        internal void ApplyRelationalVariantManipulator(VisualElement element, string[] classNames)
        {
            var configs = BuildRelationalConfigs(classNames);
            var hasAny = configs != null && configs.Count > 0;

            if (_ctx.RelationalVariantManipulators.TryGetValue(element, out var existing))
            {
                if (hasAny)
                {
                    existing.UpdatePayloads(configs);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.RelationalVariantManipulators.Remove(element);
                }
            }
            else if (hasAny)
            {
                var manipulator = new StyleRelationalVariantManipulator(_ctx, configs);
                element.AddManipulator(manipulator);
                _ctx.RelationalVariantManipulators[element] = manipulator;
            }
        }

        // Groups the relational tokens by (relation, name) into one binding config each — so the unnamed
        // group/peer and every distinct named source (group-hover/sidebar:, peer-checked/email:, …) become
        // separate bindings the manipulator resolves independently. Returns null when no relational token is
        // present (the common case), so a non-relational element pays nothing.
        private static List<RelationalBindingConfig>? BuildRelationalConfigs(string[] classNames)
        {
            if (classNames == null || classNames.Length == 0)
            {
                return null;
            }

            // (isPeer, name) -> per-state payload lists, indexed by (int)RelationalState.
            Dictionary<(bool IsPeer, string Name), List<string>[]>? map = null;
            foreach (var cls in classNames)
            {
                if (!StyleVariantClass.TryParse(cls, out var kind, out var name, out var payload)
                    || !StyleVariantClass.IsRelational(kind))
                {
                    continue;
                }
                var key = (StyleVariantClass.RelationalIsPeer(kind), name ?? string.Empty);
                map ??= new Dictionary<(bool, string), List<string>[]>();
                if (!map.TryGetValue(key, out var states))
                {
                    states = new List<string>[5];
                    map[key] = states;
                }
                var slot = (int)StyleVariantClass.RelationalStateOf(kind);
                (states[slot] ??= new List<string>()).Add(payload ?? string.Empty);
            }

            if (map == null)
            {
                return null;
            }

            var configs = new List<RelationalBindingConfig>(map.Count);
            foreach (var kv in map)
            {
                var s = kv.Value;
                configs.Add(new RelationalBindingConfig(
                    kv.Key.IsPeer, kv.Key.Name,
                    ToPayloadArray(s[(int)StyleVariantClass.RelationalState.Hover]),
                    ToPayloadArray(s[(int)StyleVariantClass.RelationalState.Focus]),
                    ToPayloadArray(s[(int)StyleVariantClass.RelationalState.FocusWithin]),
                    ToPayloadArray(s[(int)StyleVariantClass.RelationalState.Active]),
                    ToPayloadArray(s[(int)StyleVariantClass.RelationalState.Checked])));
            }
            return configs;
        }

        private static string[] ToPayloadArray(List<string> payloads)
            => payloads?.ToArray() ?? Array.Empty<string>();

        // Applies the three className-driven variant manipulators (pseudo-class hover:/focus:/
        // active:, conditional dark:/sm:…, relational group-/peer-) in one
        // call. The gap manipulator is deliberately excluded — it must run AFTER children are reconciled.
        internal void ApplyVariantManipulators(VisualElement element, string[] classNames)
        {
            ApplyVariantManipulator(element, classNames);
            ApplyConditionalVariantManipulator(element, classNames);
            ApplyRelationalVariantManipulator(element, classNames);
            ApplyStructuralVariantConfig(element, classNames);
            ApplyHasVariantManipulator(element, classNames);
            ApplyHasClassVariantConfig(element, classNames);
            ApplyAttributeVariantConfig(element, classNames);
            ApplySupportsVariantConfig(element, classNames);
        }

        // Configures (creates / updates / removes) the element's StyleHasVariantManipulator from the
        // event-driven has- tokens (has-[:checked]: / has-[:focus]:) in classNames. Mirrors
        // ApplyVariantManipulator. The has-[.class]: form is handled separately by
        // ApplyHasClassVariantConfig (a side-table, not an event manipulator).
        internal void ApplyHasVariantManipulator(VisualElement element, string[] classNames)
        {
            var @checked = ExtractHas(classNames, StyleHasKind.Checked);
            var focus = ExtractHas(classNames, StyleHasKind.Focus);
            var hasAny = @checked.Length > 0 || focus.Length > 0;

            if (_ctx.HasVariantManipulators.TryGetValue(element, out var existing))
            {
                if (hasAny)
                {
                    existing.UpdatePayloads(@checked, focus);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.HasVariantManipulators.Remove(element);
                }
            }
            else if (hasAny)
            {
                var manipulator = new StyleHasVariantManipulator(_ctx, @checked, focus);
                element.AddManipulator(manipulator);
                _ctx.HasVariantManipulators[element] = manipulator;
            }
        }

        // Collects the payloads of every has-[:checked]: / has-[:focus]: token of the given kind. A payload
        // that is itself a structural / has variant is skipped (it would have no gating owner on this path),
        // mirroring the structural-config skip.
        private static string[] ExtractHas(string[] classNames, StyleHasKind kind)
        {
            if (classNames == null || classNames.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string>? payloads = null;
            foreach (var cls in classNames)
            {
                if (StyleHasVariantClass.TryParse(cls, out var k, out _, out var payload)
                    && k == kind
                    && !StyleStructuralVariantClass.IsStructural(payload)
                    && !StyleHasVariantClass.IsHas(payload)
                    && !StyleAttributeVariantClass.IsAttribute(payload)
                    && !StyleSupportsVariantClass.IsSupports(payload))
                {
                    (payloads ??= new List<string>()).Add(payload ?? string.Empty);
                }
            }

            return payloads?.ToArray() ?? Array.Empty<string>();
        }

        // Registers (or clears) the element's has-[.class]: rules in the context side-table, re-deriving them
        // from classNames. Clears any previously-applied has-class payloads first (the rule set may have
        // changed). If the element already has children (a patch / child-only re-render) it is evaluated
        // immediately against its current descendants; on initial mount its children are not placed yet, so
        // the element's post-children pass (ApplyHasClassVariants) applies it once they are.
        private void ApplyHasClassVariantConfig(VisualElement element, string[] classNames)
        {
            if (_ctx.HasClassVariants.TryGetValue(element, out var oldRules))
            {
                foreach (var rule in oldRules)
                {
                    StyleVariantPayload.Apply(element, rule.Payloads, false, StyleLayerPriority.Has);
                }
                _ctx.HasClassVariants.Remove(element);
            }

            List<(string? ClassName, string?[] Payloads)>? rules = null;
            if (classNames != null)
            {
                foreach (var cls in classNames)
                {
                    if (StyleHasVariantClass.TryParse(cls, out var kind, out var className, out var payload)
                        && kind == StyleHasKind.Class
                        // A has-[.foo]: payload has no gating owner on this side-table path, so a nested
                        // variant / structural / has / attribute / supports payload would only become a dead
                        // token — skip it.
                        && !StyleVariantClass.IsVariant(payload)
                        && !StyleStructuralVariantClass.IsStructural(payload)
                        && !StyleHasVariantClass.IsHas(payload)
                        && !StyleAttributeVariantClass.IsAttribute(payload)
                        && !StyleSupportsVariantClass.IsSupports(payload))
                    {
                        (rules ??= new List<(string? ClassName, string?[] Payloads)>())
                            .Add((className, new[] { payload }));
                    }
                }
            }

            if (rules == null)
            {
                return;
            }
            _ctx.HasClassVariants[element] = rules;

            // Evaluate now when children are already placed — the normal create / patch paths reconcile
            // children BEFORE this class pass, so the just-registered rule lights up in the same pass. The
            // element's own post-children pass re-derives again (idempotent) to catch a later child-set change,
            // and the settled-flush sweep (RefreshHasVariants) covers a deep independent re-render. A childless
            // element has nothing to scan yet.
            if (element.childCount > 0)
            {
                EvaluateHasClass(element, rules);
            }
        }

        // Applies / clears each has-[.class]: rule's payload by querying the element's descendants for the
        // named class. Stateless and idempotent. The scan iterates the direct children's subtrees (element.Q
        // is root-inclusive, so querying the element itself would self-match — :has() is descendant-only, and
        // a self-match would also latch when the payload class equals the queried class).
        private static void EvaluateHasClass(VisualElement element, List<(string? ClassName, string?[] Payloads)> rules)
        {
            foreach (var rule in rules)
            {
                var on = false;
                foreach (var child in element.Children())
                {
                    if (child.Q(className: rule.ClassName) != null)
                    {
                        on = true;
                        break;
                    }
                }
                StyleVariantPayload.Apply(element, rule.Payloads, on, StyleLayerPriority.Has);
            }
        }

        // Element-as-subject post-children pass for has-[.class]:. Re-derives every has-class rule on the
        // element from a fresh descendant query, so a child carrying / dropping the class (added / removed)
        // re-derives the element's payload. Runs at the same hook as ApplyStructuralVariants (after the
        // element's children reconcile). Idempotent, with a cheap global early-out when no has-class rule
        // exists anywhere. A deep INDEPENDENT re-render that never re-renders this element is covered
        // separately by the settled-flush sweep (RefreshHasVariants).
        internal void ApplyHasClassVariants(VisualElement element)
        {
            if (element == null || _ctx.HasClassVariants.Count == 0)
            {
                return;
            }
            if (_ctx.HasClassVariants.TryGetValue(element, out var rules))
            {
                EvaluateHasClass(element, rules);
            }
        }

        // Element-as-subject post-children pass for the event-driven has-[:checked]: / has-[:focus]: forms.
        // Their state lives in the manipulator and is otherwise touched only by a discrete bubbling event, so a
        // descendant added / removed by reconciliation (which fires no ChangeEvent / FocusEvent) would leave
        // the payload stale. Re-scanning the subtree here re-derives it, mirroring how ApplyHasClassVariants /
        // ApplyStructuralVariants re-derive on every child-set change. Runs at the same post-children hook,
        // right after ApplyHasClassVariants. Idempotent, with a cheap global early-out when no event-driven
        // has- manipulator exists anywhere.
        internal void ApplyHasVariantManipulators(VisualElement element)
        {
            if (element == null || _ctx.HasVariantManipulators.Count == 0)
            {
                return;
            }
            if (_ctx.HasVariantManipulators.TryGetValue(element, out var manipulator))
            {
                manipulator.Rescan();
            }
        }

        // Re-derives the has- elements a just-committed flush could have affected, decoupling has- reactivity
        // from the has- element's OWN reconcile. The per-element post-children passes above only fire when the
        // has- element itself reconciles, so a has- condition driven by an INDEPENDENT nested re-render — a
        // child component's own state toggling a descendant's class, or applying a controlled FieldValue via
        // SetValueWithoutNotify (which fires no ChangeEvent the manipulator could catch) — would otherwise leave
        // the payload stale. The reconciler is the one mutating those descendants, so it re-derives here once a
        // flush has committed its DOM changes (FiberWorkLoop drives this at every settled flush).
        //
        // Dirty-scoped: a flush mutates only its OWN region — the elements committed as children of regionRoot
        // (the flushing fiber's MountPoint) and their subtrees — plus any Portal target it (re)mounted children
        // into. A has- element matches on its DESCENDANTS, so its match can change only if a mutation landed
        // inside its subtree; that means the has- element is regionRoot itself or one of its ANCESTORS (a has-
        // element strictly BELOW regionRoot whose descendant changed was reconciled in this same flush, so its
        // own post-children pass already covered it). So rather than re-scan EVERY registered has- element each
        // flush, walk up from regionRoot — and from each active Portal target, whose children live outside
        // regionRoot's subtree — and re-derive only the registered has- elements found on those chains. This is
        // O(depth) instead of O(registered has- elements x subtree scan), the win the sweep needs under many
        // has- elements x frequent flushes. A null regionRoot (region unknown) falls back to re-deriving all so
        // a stale payload is never missed. Idempotent (each evaluation reads the current subtree); no has- map
        // is mutated by an evaluation (payloads resolve to USS classes / inline styles / stacked manipulators,
        // not has- registrations), so walking and re-deriving in place is safe.
        internal static void RefreshHasVariants(ReconcilerContext? ctx, VisualElement? regionRoot)
        {
            if (ctx == null)
            {
                return;
            }
            var hasClass = ctx.HasClassVariants.Count > 0;
            var hasManip = ctx.HasVariantManipulators.Count > 0;
            if (!hasClass && !hasManip)
            {
                // Zero-cost when has- is unused anywhere (the common case): both maps empty.
                return;
            }

            if (regionRoot == null)
            {
                // Region unknown — re-derive every registered has- element so a stale payload is never missed.
                if (hasClass)
                {
                    foreach (var kv in ctx.HasClassVariants)
                    {
                        EvaluateHasClass(kv.Key, kv.Value);
                    }
                }
                if (hasManip)
                {
                    foreach (var kv in ctx.HasVariantManipulators)
                    {
                        kv.Value.Rescan();
                    }
                }
                return;
            }

            ReevaluateHasOnAncestorChain(ctx, regionRoot, hasClass, hasManip);

            // A Portal commits its children into a target OUTSIDE regionRoot's subtree, so a has- ancestor of
            // that target is not on regionRoot's chain. Portals are rare (this map is empty otherwise); when any
            // is mounted, also walk up from each target. Re-deriving a target this flush did not touch is merely
            // idempotent, so seeding from every active target stays correct without tracking which one changed.
            if (ctx.PortalState.Count > 0)
            {
                foreach (var info in ctx.PortalState.Values)
                {
                    // The resolved target recorded at mount covers every portal flavor (registry,
                    // layer, world-space); null only for the never-mounted missing-registry path.
                    if (info.Target != null)
                    {
                        ReevaluateHasOnAncestorChain(ctx, info.Target, hasClass, hasManip);
                    }
                }
            }
        }

        // Re-derives every registered has- element on the ancestor chain from root (inclusive) up to the DOM
        // root. O(depth) with O(1) lookups — see RefreshHasVariants for why only this chain can be affected.
        private static void ReevaluateHasOnAncestorChain(
            ReconcilerContext ctx, VisualElement root, bool hasClass, bool hasManip)
        {
            for (var e = root; e != null; e = e.parent)
            {
                if (hasClass && ctx.HasClassVariants.TryGetValue(e, out var rules))
                {
                    EvaluateHasClass(e, rules);
                }
                if (hasManip && ctx.HasVariantManipulators.TryGetValue(e, out var manipulator))
                {
                    manipulator.Rescan();
                }
            }
        }

        // Prefixes that namespace the data / aria families inside the single DataAttributes store map, so one
        // dictionary holds both without a key collision (a data-key and an aria-key of the same name stay
        // distinct). Match the variant's namespace to a prefix when resolving a rule.
        private const string DataStorePrefix = "data:";
        private const string AriaStorePrefix = "aria:";

        private static string StorePrefix(StyleAttributeNamespace ns)
            => ns == StyleAttributeNamespace.Aria ? AriaStorePrefix : DataStorePrefix;

        // Registers (or clears) the element's data-[...] / aria-[...] rules in the context side-table,
        // re-deriving them from classNames. Clears any previously-applied attribute payloads first (the rule
        // set may have changed), then evaluates the new rules against the element's current attribute store.
        // Mirrors ApplyHasClassVariantConfig: there is no UI-Toolkit attribute-changed signal, so reactivity
        // comes from this config pass (a class change) and from ApplyAttributes (an attribute-store change).
        private void ApplyAttributeVariantConfig(VisualElement element, string[] classNames)
        {
            if (_ctx.AttributeVariants.TryGetValue(element, out var oldRules))
            {
                foreach (var rule in oldRules)
                {
                    StyleVariantPayload.Apply(element, rule.Payloads, false, StyleLayerPriority.Attribute);
                }
                _ctx.AttributeVariants.Remove(element);
            }

            List<(StyleAttributeNamespace Ns, string Key, string? ExpectedValue, string[] Payloads)>? rules = null;
            if (classNames != null)
            {
                foreach (var cls in classNames)
                {
                    if (StyleAttributeVariantClass.TryParse(cls, out var ns, out var key, out var value, out var payload)
                        // An attribute payload has no gating owner on this side-table path (the side-table is
                        // re-evaluated as a whole, not by a per-payload manipulator), so a nested state /
                        // structural / has- / attribute / supports payload would only become a dead token —
                        // skip it, mirroring the has-[.class]: side-table.
                        && !StyleVariantClass.IsVariant(payload)
                        && !StyleStructuralVariantClass.IsStructural(payload)
                        && !StyleHasVariantClass.IsHas(payload)
                        && !StyleAttributeVariantClass.IsAttribute(payload)
                        && !StyleSupportsVariantClass.IsSupports(payload))
                    {
                        if (payload == null) continue;
                        (rules ??= new List<(StyleAttributeNamespace, string, string?, string[])>())
                            .Add((ns, key ?? string.Empty, value, new[] { payload }));
                    }
                }
            }

            if (rules == null)
            {
                return;
            }
            _ctx.AttributeVariants[element] = rules;
            _ctx.DataAttributes.TryGetValue(element, out var store);
            EvaluateAttributes(element, store, rules);
        }

        // Rebuilds the element's attribute store from props (Data + Aria, folded into one namespaced map)
        // and re-evaluates its attribute-variant rules so a changed Data/Aria prop re-derives the payload.
        // Called on mount (FiberNodeFactory) and on the props patch path (PatchBaseElement). When the element
        // carries no attribute variant rule this is a cheap early-out (no store is kept for it), so only an
        // element actually styled by data-/aria- pays the store-tracking cost.
        internal void ApplyAttributes(VisualElement element, FiberElementProps? props)
        {
            // Only an element with attribute-variant rules needs a store: the store exists solely to be
            // matched against those rules. An element that carries Data/Aria props but no data-/aria- variant
            // has nothing to evaluate, so skip building a store for it (and drop any stale one).
            if (!_ctx.AttributeVariants.TryGetValue(element, out var rules))
            {
                _ctx.DataAttributes.Remove(element);
                return;
            }

            var store = BuildAttributeStore(props);
            if (store == null)
            {
                _ctx.DataAttributes.Remove(element);
            }
            else
            {
                _ctx.DataAttributes[element] = store;
            }
            EvaluateAttributes(element, store, rules);
        }

        // Folds props.Data and props.Aria into a single namespaced map (data:<key> / aria:<key>), or null
        // when neither is present. Static + allocation-free in the common (no-attribute) case.
        private static Dictionary<string, string>? BuildAttributeStore(FiberElementProps? props)
        {
            if (props == null)
            {
                return null;
            }
            Dictionary<string, string>? store = null;
            if (props.Data != null)
            {
                foreach (var kv in props.Data)
                {
                    (store ??= new Dictionary<string, string>())[DataStorePrefix + kv.Key] = kv.Value;
                }
            }
            if (props.Aria != null)
            {
                foreach (var kv in props.Aria)
                {
                    (store ??= new Dictionary<string, string>())[AriaStorePrefix + kv.Key] = kv.Value;
                }
            }
            return store;
        }

        // Applies / clears each attribute rule's payload by matching it against the element's store. A null
        // store means the element carries no attributes, so every presence / equality rule is off. Stateless
        // and idempotent (StyleVariantPayload.Apply is a no-op when the layer is already in the target state).
        private static void EvaluateAttributes(
            VisualElement element, Dictionary<string, string>? store,
            List<(StyleAttributeNamespace Ns, string Key, string? ExpectedValue, string[] Payloads)> rules)
        {
            foreach (var rule in rules)
            {
                var present = false;
                string? actual = null;
                if (store != null)
                {
                    present = store.TryGetValue(StorePrefix(rule.Ns) + rule.Key, out actual);
                }
                var on = StyleAttributeVariantClass.Matches(rule.ExpectedValue, present, actual);
                StyleVariantPayload.Apply(element, rule.Payloads, on, StyleLayerPriority.Attribute);
            }
        }

        // Registers the element's supports-[prop:value]: payloads in the context side-table and applies them.
        // A feature query is STATIC in UI Toolkit (one fixed engine, no runtime feature variation), so a
        // well-formed token is ALWAYS-APPLIED and a malformed one never parses — there is no reactive signal
        // and no post-children / props re-evaluation to schedule (unlike the structural / has-class /
        // attribute side-tables). The side-table is kept only so a class-list change can clear the prior
        // payload before re-deriving: clear any previously-applied supports payloads first, then apply the
        // new rule set on (always-on). Idempotent — re-running with the same class list re-applies the same
        // always-on layer (StyleVariantPayload.Apply is a no-op when the layer is unchanged).
        private void ApplySupportsVariantConfig(VisualElement element, string[] classNames)
        {
            if (_ctx.SupportsVariants.TryGetValue(element, out var oldRules))
            {
                foreach (var payloads in oldRules)
                {
                    StyleVariantPayload.Apply(element, payloads, false, StyleLayerPriority.Supports);
                }
                _ctx.SupportsVariants.Remove(element);
            }

            List<string[]>? rules = null;
            if (classNames != null)
            {
                foreach (var cls in classNames)
                {
                    if (StyleSupportsVariantClass.TryParse(cls, out _, out _, out var payload)
                        // A supports- payload has no gating owner on this side-table path (the layer is
                        // applied unconditionally, not driven by a per-payload manipulator), so a nested
                        // state / structural / has / attribute / supports payload would only become a dead
                        // token — skip it, mirroring the data-/aria- and has-[.class]: side-tables.
                        && !StyleVariantClass.IsVariant(payload)
                        && !StyleStructuralVariantClass.IsStructural(payload)
                        && !StyleHasVariantClass.IsHas(payload)
                        && !StyleAttributeVariantClass.IsAttribute(payload)
                        && !StyleSupportsVariantClass.IsSupports(payload))
                    {
                        (rules ??= new List<string[]>()).Add(new string[] { payload ?? string.Empty });
                    }
                }
            }

            if (rules == null)
            {
                return;
            }
            _ctx.SupportsVariants[element] = rules;

            // Always-applied: the property is, by construction, one the author is using on a fixed engine.
            foreach (var payloads in rules)
            {
                StyleVariantPayload.Apply(element, payloads, true, StyleLayerPriority.Supports);
            }
        }

        // Registers (or clears) the element's structural-variant rules (first:/last:/odd:/[&:nth-child(N)]:)
        // in the context side-table, re-deriving them from classNames. Clears any previously-applied
        // structural payloads first (the rule set may have changed). If the element is already parented (a
        // patch / child-only re-render) it is evaluated immediately against its current position; on initial
        // mount it is not parented yet, so the container's post-children pass applies it once placed.
        private void ApplyStructuralVariantConfig(VisualElement element, string[] classNames)
        {
            if (_ctx.StructuralVariants.TryGetValue(element, out var oldRules))
            {
                foreach (var rule in oldRules)
                {
                    StyleVariantPayload.Apply(element, rule.Payloads, false, StyleLayerPriority.Structural);
                }
                _ctx.StructuralVariants.Remove(element);
            }

            List<(StyleStructuralKind Kind, int N, string[] Payloads)>? rules = null;
            if (classNames != null)
            {
                foreach (var cls in classNames)
                {
                    if (StyleStructuralVariantClass.TryParse(cls, out var kind, out var n, out var payload)
                        // Structural variants do not compose with a nested variant (first:hover:…), a has-
                        // variant (first:has-[:checked]:…), an attribute variant (first:data-[x]:…), or a
                        // supports- variant (first:supports-[…]:…): there is no gating owner on this path, so
                        // such a payload would only become a dead class. Skip it rather than add a no-op token.
                        && !StyleVariantClass.IsVariant(payload)
                        && !StyleStructuralVariantClass.IsStructural(payload)
                        && !StyleHasVariantClass.IsHas(payload)
                        && !StyleAttributeVariantClass.IsAttribute(payload)
                        && !StyleSupportsVariantClass.IsSupports(payload))
                    {
                        (rules ??= new List<(StyleStructuralKind Kind, int N, string[] Payloads)>())
                            .Add((kind, n, new string[] { payload ?? string.Empty }));
                    }
                }
            }

            if (rules == null)
            {
                return;
            }
            _ctx.StructuralVariants[element] = rules;

            // Evaluate immediately if already placed (a patch / child-only re-render). Resolve the OUTER slot
            // element (the wrapper, when this element is shadow-/clip-wrapped) so the position is the real
            // sibling index, not the wrapper's 1-child interior. On initial mount the element is not parented
            // yet, so the container post-children pass applies it once placed.
            var outer = ResolveOuter(element);
            if (outer.parent != null)
            {
                // Exclude the trailing filter bounds-spacer(s) from the sibling count: they are internal
                // render-bounds children, not part of the logical child list a first:/last:/nth match sees.
                EvaluateStructural(element, outer.parent.IndexOf(outer),
                    SilhouetteBoundsSpacer.NonSpacerChildCount(outer.parent), rules);
            }
        }

        // Applies / clears each structural rule's payload for an element at the given sibling position.
        private static void EvaluateStructural(
            VisualElement element, int index, int count,
            List<(StyleStructuralKind Kind, int N, string[] Payloads)> rules)
        {
            foreach (var rule in rules)
            {
                var on = StyleStructuralVariantClass.Matches(rule.Kind, rule.N, index, count);
                StyleVariantPayload.Apply(element, rule.Payloads, on, StyleLayerPriority.Structural);
            }
        }

        // Container post-children pass: re-derives every structural child's position-based match from the
        // live sibling order, so a child added / removed / reordered updates first:/last:/odd:/even: across
        // the whole list. Runs at the same hook as gap/divide (after children reconcile). Stateless and
        // idempotent, with a cheap global early-out when no structural variant exists anywhere.
        internal void ApplyStructuralVariants(VisualElement container)
        {
            if (container == null || _ctx.StructuralVariants.Count == 0)
            {
                return;
            }

            // The trailing filter bounds-spacer(s) are internal render-bounds children, not logical siblings:
            // exclude them from the count and skip evaluating them so first:/last:/nth match the real children.
            var count = SilhouetteBoundsSpacer.NonSpacerChildCount(container);
            for (var i = 0; i < count; i++)
            {
                // The slot may hold a shadow / clip-path WRAPPER; the structural rules are keyed by the inner.
                var inner = ResolveWrapped(container.ElementAt(i));
                if (_ctx.StructuralVariants.TryGetValue(inner, out var rules))
                {
                    EvaluateStructural(inner, i, count, rules);
                }
            }
        }

        // Configures the element's StyleGapManipulator from the gap-* / gap-x-*
        // / gap-y-* token in classNames and (re-)applies it so the inter-child
        // margins reflect the current child set. Mirrors ApplyVariantManipulator. Call this
        // AFTER the container's children have been reconciled so the manipulator sees the final child list.
        internal void ApplyGapManipulator(VisualElement element, string[] classNames)
        {
            // A grid container routes its gap through StyleGridManipulator (the grid owns the children's
            // widths AND their margins, so the two manipulators must never both write the margin edges).
            // Suppress the gap manipulator entirely when a grid-cols-* spec is present.
            if (StyleGridClass.HasGridClass(classNames))
            {
                if (_ctx.GapManipulators.TryGetValue(element, out var gridOwned))
                {
                    element.RemoveManipulator(gridOwned);
                    _ctx.GapManipulators.Remove(element);
                }
                return;
            }

            // Fast early-out for the ~99% of elements with no gap class and no existing manipulator: a
            // cheap prefix scan (no dictionary lookup, no substring) before the full TryExtract parse.
            if (!StyleGapClass.HasGapClass(classNames))
            {
                if (_ctx.GapManipulators.TryGetValue(element, out var stale))
                {
                    element.RemoveManipulator(stale);
                    _ctx.GapManipulators.Remove(element);
                }
                return;
            }

            var hasGap = StyleGapClass.TryExtract(classNames, out var gap, out var axis);

            if (_ctx.GapManipulators.TryGetValue(element, out var existing))
            {
                if (hasGap)
                {
                    existing.UpdateGap(gap, axis);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.GapManipulators.Remove(element);
                }
            }
            else if (hasGap)
            {
                var manipulator = new StyleGapManipulator(gap, axis);
                element.AddManipulator(manipulator);
                _ctx.GapManipulators[element] = manipulator;
            }
        }

        // Configures the element's StyleDivideManipulator from the divide-x / divide-y (+ width / color)
        // tokens in classNames and (re-)applies it so the inter-child borders reflect the current child
        // set. Mirrors ApplyGapManipulator — call AFTER the container's children have been reconciled so
        // the manipulator sees the final child list.
        internal void ApplyDivideManipulator(VisualElement element, string[] classNames)
        {
            // Fast early-out for the ~99% of elements with no divide class and no existing manipulator.
            if (!StyleDivideClass.HasDivideClass(classNames))
            {
                if (_ctx.DivideManipulators.TryGetValue(element, out var stale))
                {
                    element.RemoveManipulator(stale);
                    _ctx.DivideManipulators.Remove(element);
                }
                return;
            }

            var hasDivide = StyleDivideClass.TryExtract(classNames, out var spec);

            if (_ctx.DivideManipulators.TryGetValue(element, out var existing))
            {
                if (hasDivide)
                {
                    existing.UpdateSpec(spec);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.DivideManipulators.Remove(element);
                }
            }
            else if (hasDivide)
            {
                var manipulator = new StyleDivideManipulator(spec);
                element.AddManipulator(manipulator);
                _ctx.DivideManipulators[element] = manipulator;
            }
        }

        // Configures the element's StyleGridManipulator from the grid-cols-* token (and the gap-* it owns) in
        // classNames and (re-)applies it so the column sizing reflects the current child set. Mirrors
        // ApplyGapManipulator — call AFTER the container's children have been reconciled so the manipulator
        // sees the final child list.
        internal void ApplyGridManipulator(VisualElement element, string[] classNames)
        {
            // Fast early-out for the ~99% of elements with no grid-cols class and no existing manipulator.
            if (!StyleGridClass.HasGridClass(classNames))
            {
                if (_ctx.GridManipulators.TryGetValue(element, out var stale))
                {
                    element.RemoveManipulator(stale);
                    _ctx.GridManipulators.Remove(element);
                }
                return;
            }

            var hasGrid = StyleGridClass.TryExtract(classNames, out var columns);
            StyleGridClass.ExtractGaps(classNames, out var columnGap, out var rowGap);
            var spec = new GridSpec(columns, columnGap, rowGap);

            if (_ctx.GridManipulators.TryGetValue(element, out var existing))
            {
                if (hasGrid)
                {
                    existing.UpdateSpec(spec);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.GridManipulators.Remove(element);
                }
            }
            else if (hasGrid)
            {
                var manipulator = new StyleGridManipulator(spec);
                element.AddManipulator(manipulator);
                _ctx.GridManipulators[element] = manipulator;
            }
        }

        #endregion

        #region Helpers

        // Returns contentContainer when the element is a ScrollView; otherwise returns the element itself.
        internal static VisualElement GetChildContainer(VisualElement element)
        {
            if (element is ScrollView scrollView)
            {
                return scrollView.contentContainer;
            }

            return element;
        }

        #endregion
    }
}
