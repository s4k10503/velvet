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
            // Same seam for every other class-driven pass a variant can change — the layout manipulators and
            // the paint layers (skew / gradient / animate / shadow / border-style). A variant that toggles
            // one of their gate tokens changes what the element should carry, and that toggle never reaches
            // the reconciled class array those passes are otherwise configured from.
            _ctx.VariantGatedReSync = ReSyncVariantGatedPasses;
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
                    // z-* candidacy is a runtime property of the CLASS LIST, not the VNode type (unlike
                    // Portal/WorldSpace, which dispatch on a distinct type) — CanPatch already says
                    // "patch in place" for any two ElementNodes of the same ElementType/wrapper-presence
                    // regardless of z-classes, so the four transitions (still z / z-to-none / none-to-z /
                    // z-changed) must be intercepted HERE, before the ordinary element patch, or `element`
                    // (a z-managed slot's PLACEHOLDER, whenever oldElem was z-managed) would be patched as if
                    // it were the real content.
                    if (!FiberZLayerCoordinator.TryClassify(oldElem.ClassNames, oldElem.Props, out _)
                        && !FiberZLayerCoordinator.TryClassify(newElem.ClassNames, newElem.Props, out _))
                    {
                        PatchElement(element, oldElem, newElem);
                    }
                    else
                    {
                        PatchZLayerElement(element, oldElem, newElem);
                    }
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
                default:
                    PatchIndirectNode(element, oldNode, newNode);
                    break;
            }
        }

        // The node kinds whose VisualElement is a placeholder (Portal / WorldSpace: the children live in
        // another panel) or a layout-passthrough anchor (Provider / Component / Outlet), rather than the
        // node's own rendered surface.
        private void PatchIndirectNode(VisualElement element, VNode? oldNode, VNode? newNode)
        {
            switch (oldNode)
            {
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
                    PatchOutlet(element, oldOutlet, newOutlet);
                    break;
            }
        }

        private void PatchOutlet(VisualElement element, OutletNode oldOutlet, OutletNode newOutlet)
        {
            if (!ResolveOutletMatch(out var routeElement, out var routeDepth, out var match)
                || routeElement == null)
            {
                return;
            }

            // The Outlet's container doubles as the route Component's fiber anchor.
            // RemoveIfDifferentIdentity detects route change and disposes the previous fiber.
            if (_ctx.ComponentRegistry.RemoveIfDifferentIdentity(element, routeElement.ResolvedIdentity))
            {
                element.Clear();

                // The departing route's scope is the application's, built by the IRouteScopeFactory
                // handed to Router, so its Dispose is a call out into the caller's code — contained the
                // way FiberElementCleaner contains the same Dispose on the unmount path. element.Clear()
                // has already run by the time it is called, so a throw leaving instead would strand the
                // Outlet holding neither the route it left nor the route being patched in.
                _ctx.OutletScopes.Remove(element);
                try
                {
                    oldOutlet.Scope?.Dispose();
                }
                catch (System.Exception exception)
                {
                    FiberLogger.LogException("FiberNodePatcher", exception);
                }
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
                    // The element stays MOUNTED across this transition (no pool cycle), so
                    // ReconcilerContext.ClearElementSideTables never runs for it, and with no raw text left
                    // to resolve from, StyleTextEffectResolver.ApplyToElement can never run for it again
                    // either — nothing would otherwise clear a resolver-owned inline PreWrap this element
                    // was carrying, leaking it forever. Mirrors FiberElementPoolReset's unconditional
                    // whiteSpace null on the pool-RETURN path, scoped to just the element the resolver
                    // actually wrote to (see StyleTextEffectResolver's type comment for the ownership rule).
                    if (_ctx.TextWhitespaceOwned.Remove(element))
                    {
                        element.style.whiteSpace = StyleKeyword.Null;
                    }
                    // Same leak, same fix, for the Overline paint binding (see StyleTextEffectResolver's
                    // type comment): with no raw text left, ApplyToElement can never run again to detach it,
                    // so a generateVisualContent subscription this element carried would otherwise survive
                    // forever on an element that no longer has any text to decorate at all.
                    if (_ctx.TextOverlineBindings.TryGetValue(element, out var overlineBinding))
                    {
                        TextOverlineSilhouette.Detach(element, overlineBinding);
                        _ctx.TextOverlineBindings.Remove(element);
                    }
                }
            }
            // After DiffProps (and the class-driven config it follows): re-sync the data-/aria- attribute
            // store and re-evaluate its variants, so a changed Data / Aria prop re-derives the payload even
            // when the class list is unchanged (SyncClassDrivenStyling re-registers the rules when the class
            // list DID change, and this runs after, so the rules exist either way).
            ApplyAttributes(element, newNode.Props);
            PatchCommon(element, oldNode, newNode);
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
        // final child list. The variant work is gated on DiffClassList's own verdict of whether the
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
            }
            // The font layer reads the COMPOSED source, so the class diff answers only half of whether its
            // input moved: applying a variant's payload moves the token set without moving either class
            // array, and a [&>*]: one moves nothing on the child at all. Lit tokens are the other half.
            // Its place here, ahead of ApplyPostChildrenClassPasses, is the order the variant re-sync
            // mirrors.
            if (changed || HasLitVariantGateTokens(element))
            {
                // Font family / weight / italic are resolved together (so font-bold + italic compose)
                // and written as inline style that overrides the USS fallback classes.
                ApplyFontLayer(element, oldClasses, newClasses);
            }
        }

        // The condition under which ResolveGateClasses hands back something other than the reconciled
        // array, which is what the class diff cannot answer for.
        private bool HasLitVariantGateTokens(VisualElement element)
            => _ctx.VariantGateClasses.Count != 0
                && _ctx.VariantGateClasses.TryGetValue(element, out var state)
                && state.Tokens.Count != 0;

        private void PatchElement(VisualElement element, ElementNode oldNode, ElementNode newNode)
        {
            PatchBaseElement(element, oldNode, newNode);
            DiffStyles(element, oldNode.Styles, newNode.Styles);
            // clip-path decides a structural WRAPPER, which is parent surgery, so only a reconcile pass may
            // run it — hence its place here rather than inside the shared, re-runnable pass sequence. It runs
            // BEFORE the shadow paint: a clip clips the box-shadow too (CSS), so the shadow reads this verdict
            // (clipActive) and suppresses the paint while a clip is active.
            var clipActive = _appliers.ApplyClipPathOnPatch(element, newNode.ClassNames);
            // The shared post-children passes run after PatchCommon (which reconciles children) AND
            // DiffStyles — keeping gap after DiffStyles preserves the ordering invariant that the
            // manipulator's container-margin writes are never clobbered by a later inline-style diff on
            // the same element. (Today DiffStyles only touches color properties, but the invariant must
            // hold if a margin-writing StyleOverride is ever added.)
            ApplyPostChildrenClassPasses(element, oldNode.ClassNames, newNode.ClassNames,
                paintTail: true, clipActive);
        }

        // Applies the oldNode -> newNode diff when EITHER side is z-managed (FiberZLayerCoordinator.
        // TryClassify), covering the three reachable transitions (the caller already excludes "neither side is
        // z-managed", which falls through to the ordinary PatchElement):
        //   still z-managed (z -> z, possibly a different resolved value): `element` is the PLACEHOLDER;
        //     the real element is patched normally, then repositioned only if its resolved z (or front/back
        //     sign) actually changed.
        //   z -> none: `element` is the PLACEHOLDER; the real element is patched normally, then relocated
        //     back into the placeholder's own slot, replacing it.
        //   none -> z: `element` IS the real, still-ordinary element; it is patched normally in place, then
        //     relocated out into its layer, leaving a fresh placeholder at its old slot.
        // Every branch patches the REAL element (props/children/styles) before any relocation, so a size or
        // content change that a coordinate-neutral reposition depends on is already reflected.
        // Contract-preserving: exactly like PatchElement, the net effect on `element`'s own parent's
        // childCount/order is zero in every branch — a same-index swap (mirroring PatchNode's own MemoNode-
        // branch precedent) or a pure container-membership move — so ProcessKeyedNode / CommitLeaf's post-
        // patch re-fetch at the same slot (already written to tolerate a WrapElement-style reference swap)
        // picks up the new occupant without any change to those call sites.
        private void PatchZLayerElement(VisualElement element, ElementNode oldNode, ElementNode newNode)
        {
            var oldIsZ = FiberZLayerCoordinator.TryClassify(oldNode.ClassNames, oldNode.Props, out _);
            var newIsZ = FiberZLayerCoordinator.TryClassify(newNode.ClassNames, newNode.Props, out var newResolvedZ);

            if (!oldIsZ)
            {
                // none -> z: `element` is still the ordinary, in-flow element.
                PatchElement(element, oldNode, newNode);
                FiberZLayerCoordinator.RelocateFromOrdinarySlot(_ctx, element, newResolvedZ);
                return;
            }

            // `element` is the placeholder for both remaining branches (oldIsZ is true): the real element
            // lives elsewhere and must be resolved before it can be patched.
            if (!_ctx.ZLayerPlaceholders.TryGetValue(element, out var real))
            {
                // Defensive: a live tree should never reach a z-managed placeholder with no registered real
                // element. Patching the placeholder itself would be wrong (it is not the declared content),
                // so at minimum do not silently drop the node's props/children.
                PatchElement(element, oldNode, newNode);
                return;
            }

            PatchElement(real, oldNode, newNode);
            if (newIsZ)
            {
                FiberZLayerCoordinator.Reposition(_ctx, element, real, newResolvedZ);
            }
            else
            {
                FiberZLayerCoordinator.RelocateToOrdinarySlot(_ctx, element, real);
            }
        }

        // The ordered post-children effect-pass sequence shared by PatchElement and PatchMotion, kept in
        // one place so the two patch paths cannot drift (a pass added here reaches both). The ORDER is
        // load-bearing:
        // - The [&>*]: child-combinator variant runs first so gap / divide / grid (next) win a shared child
        //   edge — [&>*]:ml-[2px] behaves like a child's own margin, which gap already overwrites. It too runs
        //   AFTER PatchCommon so it sees the final child set.
        // - The layout manipulators (gap, divide, grid and text-balance — ApplyResolvedLayoutManipulators
        //   owns the order among those four) run next but still
        //   AFTER PatchCommon (which reconciles children) so gap's margin writes are the final word on the
        //   element — the wrap path writes the container's OWN margins (-gap/2) — and so they re-apply
        //   against the current child set (a child add / remove re-spaces even when the className did not
        //   change). text-balance rides the same slot for consistency (every per-element style manipulator
        //   attaches from one place), though its own ordering is not load-bearing: it measures the
        //   element's own text, not a shared child edge or the child set.
        // - Structural variants (first:/last:/nth) re-derive every child's position-based match from the
        //   final sibling order.
        // - has-[.class]: re-evaluated with the element AS subject (its descendants drive its own payload),
        //   at the same post-children timing — a child added / removed re-derives the match.
        // - has-[:checked]: / has-[:focus]: re-scanned at the same timing — a checked / focused descendant
        //   added or removed fires no event, so the manipulator must re-derive from the live subtree.
        // - text-transform / -decoration cascade (post-children so it reaches descendant text leaves).
        // - The paint sequence last, from a class source resolved HERE rather than at the top: the has- /
        //   attribute / supports passes just above apply payloads to this very element, so a gate token they
        //   toggle has to be in the source the paint layers read, and an array resolved before them would not
        //   carry it. The re-sync those passes raise also runs, a few lines earlier, against the same
        //   composition — so the two agree instead of the later one undoing the earlier.
        // The clip-path wrapper stays with the callers: it is parent surgery on the element's own slot, which
        // only a reconcile pass may perform.
        private void ApplyPostChildrenClassPasses(VisualElement element, string[] oldClassNames,
            string[] newClassNames, bool paintTail, bool clipActive)
        {
            // Before gap / divide / grid: on a SHARED style property ([&>*]:ml-[2px] alongside gap-x-4) those
            // three own the edge and must win, exactly as they already win over a child's own explicit margin
            // — running [&>*]: first makes it behave as if the child itself carried the wrapped class.
            ApplyChildVariantManipulator(element, newClassNames);
            ApplyLayoutManipulators(element, newClassNames);
            ApplyStructuralVariants(element);
            ApplyHasClassVariants(element);
            ApplyHasVariantManipulators(element);
            ApplyTextEffects(element, newClassNames);
            var resolved = ResolveVariantClasses(element, oldClassNames, newClassNames, paintTail,
                out var classesChanged);
            // canReleaseFace: a reconcile pass may let the silhouette stashes release, because this same
            // sequence runs on the element's next patch and re-takes the stash. The re-sync below cannot.
            ApplyResolvedClassPasses(element, resolved, classesChanged, paintTail, clipActive,
                canReleaseFace: true);
        }

        // The ordered paint-pass sequence, shared by the reconcile path and the variant re-sync so the two
        // cannot drift and so a re-run produces exactly what a full pass would. Every pass here reads its
        // class source and nothing else about the element's position, which is what makes it re-runnable
        // outside a reconcile — unlike the clip-path wrapper layer, which swaps which element occupies the
        // element's own slot and is therefore forbidden inside a pointer / focus callback or a breakpoint
        // notification. The ring layer's overlay is a TRAILING, spacer-marked sibling, which occupies no slot
        // (SilhouetteBoundsSpacer.NonSpacerChildCount trims it), so adding and removing it here is safe — and
        // that is precisely what makes focus:ring-* render instead of toggling an inert class.
        // The ORDER is load-bearing:
        // - Gradient runs after the node-style diff so its background-image is the last word on this
        //   element — DiffStyles only writes background-image on an actual node-style change, which a
        //   gradient element never carries, so the two never fight.
        // - animate-* motion runs after the gradient (a pan mode reads the live gradient) and reconciles
        //   its own restart/attach/detach against the new class list.
        // - Skew is a wrapper-less paint (the sheared silhouette is the element's own generateVisualContent);
        //   its stash / spec sync must observe this patch's freshly-applied class styling, so it follows the
        //   passes that apply it. Its resolved X angle is forwarded so the shadow gate never re-parses the
        //   skew family.
        // - The shadow paint follows skew (a skewed caster's shadow follows the sheared silhouette) and reads
        //   the caller's clipActive verdict.
        // - border-dashed / border-dotted runs strictly AFTER skew and shadow so it reads their final
        //   ownership — while either owns the face the dashed layer defers (the border stays solid), so an
        //   add/remove of skew/shadow in the same pass resolves without a race.
        // - The ring overlay runs last. It owns no part of the element's face and contends with none of the
        //   layers above, so its position here is not load-bearing; it sits at the end because it is the only
        //   one that touches the element's PARENT rather than the element.
        // The particles spacer is here, not in the Particles-settings diff, because it follows the CLASS list
        // (a filter comes and goes via a class swap or a variant).
        // paintTail is the one per-path knob, and for the three silhouette layers it is the same distinction
        // the gradient's skewable flag draws: an ElementNode may render a sheared silhouette, a Motion never
        // does, so a Motion's gradient stays on the straight background-image path and those three stand down
        // entirely. The ring rides the same knob on a reason of its own, stated where the Motion path warns
        // about it (FiberNodeFactory.WarnIgnoredMotionUtilities).
        private void ApplyResolvedClassPasses(VisualElement element, string[] classNames, bool classesChanged,
            bool paintTail, bool clipActive, bool canReleaseFace)
        {
            _appliers.ApplyGradientOnPatch(element, classNames, skewable: paintTail);
            _appliers.ApplyAnimateOnPatch(element, classNames);
            _appliers.ApplyFilterTransitionOnPatch(element, classNames);
            _appliers.ApplyParticlesSpacer(element, classNames);
            if (!paintTail)
            {
                return;
            }
            var skewXDeg = _appliers.ApplySkewOnPatch(element, classNames, classesChanged, canReleaseFace);
            _appliers.ApplyShadowOnPatch(element, classNames, clipActive, skewXDeg, canReleaseFace);
            _appliers.ApplyBorderStyleOnPatch(element, classNames, classesChanged, canReleaseFace);
            _appliers.ApplyRingOnPatch(element, classNames, clipActive);
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
        private void PatchCommon(VisualElement element, BaseElementNode oldNode, BaseElementNode newNode)
        {
            var newEvents = newNode.Events;
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
            var resolvedName = newNode.Name ?? string.Empty;
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
                oldNode.Children ?? Array.Empty<VNode>(),
                newNode.Children ?? Array.Empty<VNode>());

            _ctx.SyncRefCallback(element, newNode.RefCallback);
        }

        private void PatchMotion(VisualElement element, MotionNode oldNode, MotionNode newNode)
        {
            // Mirror the create path's anchor-element recording: a presence keyed child that REUSES its
            // element (a ghost's old-side reproduction, a cancelled exit's re-entry) reaches its Motion
            // through this patch, and the expansion still needs the Motion's own element to dispatch
            // variant enter/exit against.
            if (ReferenceEquals(newNode, _ctx.PresenceAnchorMotion))
            {
                _ctx.PresenceAnchorMotionElement = element;
            }
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
            // VISUAL tween on the scheduler instead — a transition should apply to every animate update, not
            // just the first. A null Transition keeps today's plain, instant diff (Velvet applies no implicit
            // default transition).
            // Gated off an element the scheduler already treats as EXITING (not off PresenceAnchorMotion
            // identity — that field is set for every current AnimatePresence child, including a plain
            // PERSISTING one this swap must still drive when its ambient label changes, e.g. a coordinator
            // orchestrating a presence-managed child). A Motion's own resolved variant only actually changes
            // (the precondition above) while it carries Variants of its own — the shape whose enter/exit
            // GeneralPathReconciler dispatches explicitly against this very element (isVariantMotion; a
            // wrapped anchor Motion's dispatch also lands here, on the Motion's own element, not on its
            // wrapper) — and that explicit dispatch either runs on a fresh CREATE (never reaches PatchMotion)
            // or, for a still-exiting / cancelled-exit reproduction, plays no competing animation of its own
            // (CancelExit's reversal, or no-op) — so the one real overlap is a GHOST re-patched on a LATER
            // render while still exiting (skipping the ghost dispatch's own CancelEnter, which only runs the
            // FIRST time state.Exiting.Add(key) succeeds): IsExiting catches exactly that window.
            if (newNode.Transition != null && !_ctx.StyleAnimationScheduler.IsExiting(element)
                && !SequenceEqual(oldVariantClasses, newVariantClasses))
            {
                _ctx.StyleAnimationScheduler.PlayVariantEnter(element, oldVariantClasses, newVariantClasses,
                    newNode.Transition, onComplete: null, additionalDelaySec: extraDelaySec);
            }

            // MotionNode has no Styles diff, so the shared passes follow PatchCommon (which reconciles
            // children) directly. A Motion never renders skew (the animation node never attaches a sheared
            // silhouette), so its gradient always takes the straight background-image path even with skew
            // classes present, and the three silhouette paints stand down (skewable: false). A Motion also
            // carries no clip wrapper, so nothing can suppress a paint here.
            ApplyPostChildrenClassPasses(element, appliedOld, appliedNew, paintTail: false, clipActive: false);
            // A Motion carries neither a shadow paint nor a ring overlay: the create path warns and skips both
            // on a Motion, and paintTail:false above keeps the patch from attaching one — so there is nothing
            // here to update.

            // Shared-element layout animation (layoutId): independent of the variant swap
            // above — runs from the ACTUAL resolved-rect delta, not a class-defined from/to pair — so
            // it fires whether or not this patch also changed Variants/Animate. Falls back to
            // StyleTransitionConfig's own documented spring defaults (Stiffness 100 / Damping 10 /
            // Mass 1) when this Motion declares no Transition, since a layoutId tween needs SOME spring
            // shape to animate with and Velvet applies no implicit default transition
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
            var (target, isHeal) = ResolvePortalTarget(placeholder, oldNode, newNode, out var describe);
            if (target == null)
            {
                return;
            }

            // Non-null only when this call is healing a target that just registered (isHeal, set by
            // ResolvePortalTarget's heal case). DrainPendingPortalMounts stamps DetachedMountContext on a
            // normal mount's newly-created top-level children so FiberCrossPanelEventDispatcher.Continue
            // can find its way from the target back to the logical chain; a heal instead creates those
            // children through this ordinary synchronous patch, which never runs that stamp. Snapshotting
            // here (before the shared PatchPortalChildren call below) and stamping after lets the heal
            // apply the same marker to whatever it just created, without touching the drain path or any
            // other patch of an already-healthy Portal. Every OTHER path through this method
            // (ResolvePortalTarget's layer case, and its already-resolved registry case) needs the
            // identical marker for the identical reason — see steadyStateDeclaringFiber further down,
            // after target resolves either way. This one stays split out because it is the rare branch:
            // PatchPortalChildren records the resolved target, so a placeholder re-enters here only when
            // a re-registration moves it off that element again.
            ComponentFiber? healingDeclaringFiber = null;
            HashSet<ComponentFiber>? healingChildFibersBefore = null;
            if (isHeal)
            {
                // FiberStack.Current is the declaring fiber here (RenderAndReconcile keeps it pushed for
                // the whole patch of its own returned tree), so it doubles as both the ComponentRegistry
                // parent new children below will actually register under AND the logical ancestor
                // FiberCrossPanelEventDispatcher.Continue needs. The deferred mount arrives at the same
                // fiber by pushing it explicitly — DrainPendingPortalMounts owns why it has to.
                healingDeclaringFiber = CaptureDeclaringChildFibers(pooled: false, out healingChildFibersBefore);
            }

            // Every patch that reaches here WITHOUT healing (ResolvePortalTarget's layer case, or its
            // already-resolved registry case) still needs the same DetachedMountContext marker: a
            // Portal can drain its first mount with ZERO top-level children (e.g. `V.Portal(id,
            // children: isOpen ? real : Array.Empty<VNode>())`) and gain its first ones on a LATER
            // patch — long after DrainPendingPortalMounts' own one-time stamp already ran with nothing
            // to mark. Unlike the heal branch above (at most once per placeholder, ever), this runs on
            // EVERY patch of an already-mounted Portal, so the "before" snapshot is rented from the
            // shared pool instead of freshly allocated — the walk itself is already cheap (a declaring
            // fiber's own direct children are typically a handful at most); pooling removes the one part
            // of it that would otherwise cost real GC pressure across many patches per frame.
            ComponentFiber? steadyStateDeclaringFiber = null;
            HashSet<ComponentFiber>? steadyStateChildFibersBefore = null;
            if (healingDeclaringFiber == null)
            {
                steadyStateDeclaringFiber = CaptureDeclaringChildFibers(pooled: true, out steadyStateChildFibersBefore);
            }
            try
            {
                PatchPortalChildren(placeholder, target, oldNode.Children, newNode.Children, describe);

                if (healingDeclaringFiber != null)
                {
                    StampNewTopLevelChildren(healingDeclaringFiber, healingChildFibersBefore!, newNode.Children);
                }
                else if (steadyStateDeclaringFiber != null)
                {
                    StampNewTopLevelChildren(steadyStateDeclaringFiber, steadyStateChildFibersBefore!, newNode.Children);
                }
            }
            finally
            {
                if (steadyStateChildFibersBefore != null)
                {
                    _ctx.BufferPool.ReturnFiberSet(steadyStateChildFibersBefore);
                }
            }
        }

        // Resolves the target VisualElement a Portal patch reconciles its slot range against, across the
        // four ways a Portal can address one: an explicit Layer (host table lookup, plus re-chaining the
        // placeholder when FocusOrder changed), the element the caller passed (already resolved, and
        // ReconcileKeying.CanPatch has refused the patch unless it is the one this Portal mounted into),
        // an id whose element is still the one this Portal mounted into, or an id with no children of
        // this Portal on it yet — either because the mount warned and recorded no target, or because a
        // re-registration just moved this Portal off the element it had. Both of those go through the
        // same tail, which also attaches the same-panel synthetic-bubbling bridge the mount-time drain
        // never got to run for this target. Returns a null target when
        // resolution fails (already warned); the caller bails without patching. IsHeal tells the caller
        // whether this pass took the not-yet-healed case, which needs a declaring-fiber snapshot from
        // before this call for the DetachedMountContext stamp (see PatchPortal).
        private (VisualElement? Target, bool IsHeal) ResolvePortalTarget(
            VisualElement placeholder, PortalNode oldNode, PortalNode newNode, out string describe)
        {
            if (newNode.Layer is { } layer)
            {
                // The per-layer framework host was created when the mount drained and persists
                // until reconciler disposal, so a patch resolves it from the table. A record whose
                // GameObject a scene unload killed reads as dead here and counts as missing.
                describe = layer.ToString();
                if (!_ctx.LayerHosts.TryGetValue(layer, out var layerHost) || layerHost.Document == null)
                {
                    FiberLogger.LogWarning("Portal", $"Layer host for \"{describe}\" is missing. Children will not be rendered.");
                    return (null, false);
                }
                // Recurring re-sync point for late declaring resolution and runtime drift.
                PanelHostFactory.SyncDeclaring(layerHost, layer, placeholder.panel, _ctx);
                var target = layerHost.Document.rootVisualElement;
                if (oldNode.FocusOrder != newNode.FocusOrder)
                {
                    FiberFocusNavigator.ConfigureChainedPlaceholder(placeholder, layerHost,
                        newNode.FocusOrder == PanelFocusOrder.Chained, _ctx);
                }
                return (target, false);
            }

            if (newNode.TargetElement is { } held)
            {
                describe = "an element the caller holds";
                return (held, false);
            }

            describe = newNode.TargetId!;
            if (_ctx.PortalState.TryGetValue(placeholder, out var recorded) && recorded.Target != null)
            {
                var registered = FiberPortalRegistry.Get(describe);
                if (registered == null || ReferenceEquals(registered, recorded.Target))
                {
                    // Nothing to follow: an unregistered id names no element to move to, so the children
                    // keep being patched where they live rather than being stranded.
                    return (recorded.Target, false);
                }
                // The id now names a different element. The children leave the old one and are created
                // into the new one rather than being reparented into it, which is what createPortal does
                // when its container changes and the route V.Portal(container:) takes through
                // ReconcileKeying.CanPatch — state, refs and effects under the portal do not survive.
                // Patching the existing children into the replacement is what is not available: this
                // portal's slot range addresses positions in the element it mounted into, and reusing it
                // against another element's children would diff one portal's range against another's.
                _host.ReleasePortalRangeForRetarget(placeholder);
            }

            // Mounted before the id was registered (the mount warned and recorded no
            // target), or released by the retarget just above: resolve fresh so this patch mounts the
            // children into the element the id names now.
            var resolvedTarget = FiberPortalRegistry.Get(describe);
            if (resolvedTarget == null)
            {
                FiberLogger.LogWarning("Portal", $"Target \"{describe}\" is not registered. Children will not be rendered.");
                return (null, false);
            }
            // Both entrances reach here holding a range that addresses nothing on this target — the
            // unregistered mount recorded slot 0 against no element at all, the retarget emptied the range
            // it held on a different one — so the range is rebased to the end of whatever this target
            // already holds, the slot ChildReconciler's deferred-mount pass would have taken. Patching from
            // an unrebased slot 0 instead diffs this Portal's first child against the container's own.
            if (_ctx.PortalState.TryGetValue(placeholder, out var released))
            {
                _ctx.PortalState[placeholder] = released with
                {
                    SlotStart = LogicalChildSlots.Count(resolvedTarget),
                    SlotLength = 0,
                };
            }
            // The mount-time attach (ChildReconciler's same-panel drain branch) never ran for this
            // target — a mount while the id was unregistered enqueued no drain entry at all, and a
            // retarget resolves an element that mount never saw — so this patch is where the same-panel
            // synthetic-bubbling bridge gets attached. Guarded exactly like that branch: a target
            // another Portal already bridged is not double-attached.
            if (!_ctx.SamePanelPortalBridges.ContainsKey(resolvedTarget))
            {
                _ctx.SamePanelPortalBridges[resolvedTarget] =
                    FiberCrossPanelEventDispatcher.AttachBridge(resolvedTarget, _ctx);
            }
            return (resolvedTarget, true);
        }

        // Captures declaringFiber's current direct children so a later diff against its children after
        // a patch can tell which ones are new (see StampNewTopLevelChildren's own comment). pooled
        // selects a rented set for the steady-state caller, which runs this on every patch of an
        // already-mounted Portal/WorldSpace; the heal caller runs at most once per placeholder ever and
        // never returns its set to the pool, so it takes a fresh allocation instead.
        private ComponentFiber? CaptureDeclaringChildFibers(bool pooled, out HashSet<ComponentFiber>? childFibersBefore)
        {
            var declaringFiber = _ctx.FiberStack.Current;
            childFibersBefore = null;
            if (declaringFiber != null)
            {
                childFibersBefore = pooled ? _ctx.BufferPool.RentFiberSet() : new HashSet<ComponentFiber>();
                for (var f = declaringFiber.Child; f != null; f = f.Sibling)
                {
                    childFibersBefore.Add(f);
                }
            }
            return declaringFiber;
        }

        // Stamps DetachedMountContext on every top-level child of declaringFiber NOT present in
        // childFibersBefore — the fibers this specific PatchPortalChildren call just created — so
        // FiberCrossPanelEventDispatcher.Continue can resolve the logical ancestor for a pointer/focus
        // event landing on them. Shared by PatchPortal's one-time heal and every steady-state patch of
        // an already-mounted Portal/WorldSpace (see each call site for why its "before" set comes from a
        // different source); the diff itself — walk the current list, skip anything already in the
        // "before" set, lazily create one DetachedMountContext for the rest — is identical either way.
        private void StampNewTopLevelChildren(
            ComponentFiber declaringFiber, HashSet<ComponentFiber> childFibersBefore, VNode?[]? descendantNodes)
        {
            DetachedMountContext? detached = null;
            for (var f = declaringFiber.Child; f != null; f = f.Sibling)
            {
                if (childFibersBefore.Contains(f)) continue;
                detached ??= new DetachedMountContext(_ctx.ComponentContextStack.SnapshotTops(),
                    descendantNodes, declaringFiber, declaringFiber);
                f.DetachedMountContext = detached;
            }
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

            // Every patch reaches here already resolved — a world-space host has no "late
            // registration" heal path the way a registry Portal does (DrainPendingPortalMounts creates
            // it outright on first mount, never leaving it to a later patch to resolve) — so this
            // always stamps through the same steady-state mechanism PatchPortal's own already-resolved
            // path uses (see its own comment): a world-space panel can likewise mount with zero
            // children and gain its first ones on a later patch, after the one-time drain stamp already
            // ran with nothing to mark.
            var declaringFiber = CaptureDeclaringChildFibers(pooled: true, out var childFibersBefore);
            try
            {
                PatchPortalChildren(placeholder, record.Document.rootVisualElement, oldNode.Children, newNode.Children, "world-space");
                if (declaringFiber != null)
                {
                    StampNewTopLevelChildren(declaringFiber, childFibersBefore!, newNode.Children);
                }
            }
            finally
            {
                if (childFibersBefore != null)
                {
                    _ctx.BufferPool.ReturnFiberSet(childFibersBefore);
                }
            }
        }

        // The shared slot-range child patch for every portal flavor (registry, held element, layer,
        // world-space): reconciles only this placeholder's own slot range against the target and shifts
        // the downstream ranges on the same target by the growth delta. describe names the target in
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
            // LOGICAL counts on both sides of the reconcile, matching the basis SlotStart and SlotLength are
            // stored in. Measuring this physically let an invisible child arriving on the target — a portal
            // child gaining a filter, or a negative z — inflate the recorded length by one, which then
            // shifted every downstream portal's SlotStart and made the cleanup walk over-remove.
            var beforeTailCount = LogicalChildSlots.Count(target);
            // Restored rather than cleared: a Portal declared inside another Portal's children patches from
            // within this call, and what the outer one mounts after that returns is still the outer one's.
            var enclosingPortal = _ctx.CurrentPortalPlaceholder;
            _ctx.CurrentPortalPlaceholder = placeholder;
            // The mount half sets the same scope for the same reason — DrainPendingPortalMounts owns it —
            // and the two have to agree or a patch looks this Portal's children up under a key the mount
            // did not register them by. No push here: this runs from the reconcile of the tree the
            // declaring component returned, so its fiber is already current.
            var enclosingChildScope = _ctx.EnterPortalChildKeyScope(placeholder);
            try
            {
                _host.ReconcileChildren(target, oldChildren, newChildren, slotStart: prevState.SlotStart);
            }
            finally
            {
                _ctx.ExitPortalChildKeyScope(enclosingChildScope);
                _ctx.CurrentPortalPlaceholder = enclosingPortal;
            }
            // (beforeTailCount - prevState.SlotLength) is the count of target children that do NOT belong to
            // this Portal's slot — unchanged by the reconcile above. Subtracting it from the new total
            // isolates this Portal's new slot length without re-counting the foreign children.
            var newSlotLength = LogicalChildSlots.Count(target) - (beforeTailCount - prevState.SlotLength);
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
        // GeneralPathReconciler.ExpandInlineRecursive's inline Provider expansion does not apply.
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
                    || StyleSupportsVariantClass.IsSupports(rawCls)
                    || StyleChildVariantClass.IsChildVariant(rawCls))
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
                var priority = important
                    ? StyleLayerPriority.ImportantOf(StyleLayerPriority.Base)
                    : StyleLayerPriority.Base;

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

        // Token families a manipulator (or a side-table config pass) owns outright: none of them ever
        // enters the USS class list, on either the add or the remove side (see AddClass / RemoveClass).
        private static bool IsManipulatorOwnedClass(string cls)
        {
            // State-variant tokens (hover:/focus:/active:) are not real classes; the variant
            // manipulator (configured separately) owns them.
            if (StyleVariantClass.IsVariant(cls))
            {
                return true;
            }

            // [&>*]: child-combinator tokens style the CONTAINER's children, never the container itself; the
            // child-variant manipulator owns them. Guarded here (before the inline-resolved branch) because the
            // token starts with '[' and would otherwise be mis-routed as an arbitrary value.
            if (StyleChildVariantClass.IsChildVariant(cls))
            {
                return true;
            }

            // Structural variants (first:/last:/[&:nth-child(N)]:) are owned by the reconciler's structural
            // pass (evaluated against sibling position); never added as classes.
            if (StyleStructuralVariantClass.IsStructural(cls))
            {
                return true;
            }

            // has-[...] variants (parent styled by a descendant condition) are owned by the has-variant
            // manipulator / the has-class post-children pass; never added as classes.
            if (StyleHasVariantClass.IsHas(cls))
            {
                return true;
            }

            // data-[...] / aria-[...] variants (element styled by its own carried attribute) are owned by
            // the attribute side-table; never added as classes.
            if (StyleAttributeVariantClass.IsAttribute(cls))
            {
                return true;
            }

            // supports-[...] feature-query variants (element styled when the engine supports a declaration)
            // are owned by the supports side-table (static / always-applied in UITK); never added as classes.
            if (StyleSupportsVariantClass.IsSupports(cls))
            {
                return true;
            }

            // font-[...] arbitrary font classes are resolved from the whole class array by
            // StyleFontResolver and applied as inline style; like other arbitrary values they must not
            // enter the USS class list.
            if (StyleFontClass.IsArbitraryFontClass(cls))
            {
                return true;
            }

            // leading-[...] arbitrary line-height classes are resolved from the whole class array by
            // StyleTextEffectResolver and folded into the rich-text tag it wraps the display string in;
            // like font-[...] they must not enter the USS class list, regardless of whether the bracket
            // value itself parses.
            if (StyleTextEffectClass.IsArbitraryLeadingClass(cls))
            {
                return true;
            }

            return false;
        }

        private static void AddClass(VisualElement element, string cls)
        {
            if (IsManipulatorOwnedClass(cls))
            {
                return;
            }

            // Important modifier (!utility / utility!): strip the bang; when present, elevate the utility
            // into the important band, on whichever of the two layers below carries it.
            var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
            if (string.IsNullOrEmpty(core))
            {
                return;
            }
            var priority = important
                ? StyleLayerPriority.ImportantOf(StyleLayerPriority.Base)
                : StyleLayerPriority.Base;

            // Plain classes (the common case) go to the USS class list through the projection, which decides
            // whether a higher-priority payload has already taken every property they write; inline-value
            // tokens (bracketed, color-opacity, static-scale) resolve to inline style.
            if (!StyleArbitraryValueResolver.IsInlineResolved(core))
            {
                StyleClassProjection.Add(element, core, priority);
                return;
            }

            StyleArbitraryValueResolver.ApplyClassToken(element, core, priority);
        }

        // None of IsManipulatorOwnedClass's token families ever entered the class list (see AddClass), so
        // there is nothing here to remove for them; each family's applied payload is cleared elsewhere by
        // its own owning pass on the class change instead (the variant manipulator; the child-variant /
        // structural / has-variant / attribute / supports config passes; StyleFontResolver; or
        // StyleTextEffectResolver re-resolving) — never by this method.
        private static void RemoveClass(VisualElement element, string cls)
        {
            if (IsManipulatorOwnedClass(cls))
            {
                return;
            }

            // Important modifier: strip the bang and clear the same layer AddClass applied it on.
            var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
            if (string.IsNullOrEmpty(core))
            {
                return;
            }
            var priority = important
                ? StyleLayerPriority.ImportantOf(StyleLayerPriority.Base)
                : StyleLayerPriority.Base;

            // Plain classes (the common case) leave the USS class list through the projection, which may
            // hand the properties they held back to a payload it had suppressed; inline-value tokens
            // (bracketed, color-opacity, static-scale) clear the inline style they applied.
            if (!StyleArbitraryValueResolver.IsInlineResolved(core))
            {
                StyleClassProjection.Remove(element, core, priority);
                return;
            }

            StyleArbitraryValueResolver.ClearClassToken(element, core, priority);
        }

        // Applies the diff of element props between renders.
        // Maintenance note: this method (with DiffBindingProps) diffs each property of FiberElementProps
        // individually, so any new property added to FiberElementProps must also receive a matching
        // branch in one of the two. Missing the addition causes the new property's diff to be silently
        // ignored (the prop applies on the initial mount but never updates on a re-render) without a
        // compile error.
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
                // Ownership of the flag moves with the declaration, in both directions — the branch also
                // fires when the prop goes away, which is the moment the anchor has to take it back.
                // Ordering: after the applier, whose write is what the session reads.
                _ctx.ActiveDrag?.OnSourceFocusableDeclarationChanged(element, newProps.Focusable.HasValue);
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
                RaiseCheckedSignal(element);
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

            DiffBindingProps(element, oldProps, newProps);
        }

        // A controlled value reaches the control through SetValueWithoutNotify, so the ChangeEvent the
        // checked: and peer-checked: variants listen for never arrives — the same suppression
        // RefreshHasVariants compensates for on the has- side. Reads the control back instead of re-deriving
        // the value from the prop: a FieldValue cleared to null resets a bool control through
        // ApplyFieldValue's own branch, and the control is where both branches land.
        // Two sweeps because the two families are keyed opposite ways (see IRelationalSettleTarget): the
        // element-local one visits what is registered against this element, the relational one offers the
        // edge to every consumer so each can recognise its own source.
        private void RaiseCheckedSignal(VisualElement element)
        {
            if (element is not INotifyValueChanged<bool> boolField)
            {
                return;
            }

            var value = boolField.value;
            VariantSettleSweep.ForEach(element, _ctx, consumer => consumer.SettleChecked(value));
            VariantSettleSweep.SettleCheckedFromSource(element, _ctx, value);
        }

        // The props that wire a binding onto the element rather than writing a VisualElement property; the
        // create-path counterpart is FiberNodeFactory.ApplyOptionalCreateBindings.
        private void DiffBindingProps(VisualElement element, FiberElementProps oldProps, FiberElementProps newProps)
        {
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

        #region Manipulator Configure

        // One manipulator family's tracking table, constructor and update call. Implementations must be
        // structs: Configure is constrained to a struct so every instantiation is specialized and each
        // member below binds to a direct call. A class implementation, or a delegate parameter in place of
        // this interface, would allocate once per element per patch on a path that runs for every styled
        // element in the tree.
        // Create takes the context because every family's manipulator reaches it — to resolve its payloads,
        // or (gap, grid, divide) to claim the children it writes to.
        private interface IManipulatorOp<TManip> where TManip : Manipulator
        {
            Dictionary<VisualElement, TManip> Table(ReconcilerContext ctx);
            TManip Create(ReconcilerContext ctx);
            void Update(TManip manipulator);
        }

        // Creates, updates or tears down element's manipulator of the family described by op. wanted is
        // the family's own verdict on whether the current class list still asks for the manipulator;
        // each caller computes it from its own token scan (an emptiness check, a parse result, or the
        // prefix scan it already ran to early-out).
        // The table entry is dropped only AFTER the element has released the manipulator. These tables
        // are the index FiberElementCleaner detaches from, so an entry cleared ahead of a detach that
        // throws or re-enters would strand an attached manipulator nothing can reach.
        // `default` is a legal op ONLY where wanted is statically false: a default op carries zeroed
        // fields, so a computed wanted that turns out true would build a manipulator out of them.
        // op is taken by value. `where TOp : struct` does not promise the concrete type is readonly, so
        // an `in` parameter would make each member call below copy defensively rather than copying once
        // at the call site.
        private void Configure<TOp, TManip>(VisualElement element, bool wanted, TOp op)
            where TOp : struct, IManipulatorOp<TManip>
            where TManip : Manipulator
        {
            var table = op.Table(_ctx);
            if (table.TryGetValue(element, out var existing))
            {
                if (wanted)
                {
                    // Keep this unconditional — never gate it on "the spec is unchanged". Gap, divide and
                    // grid re-derive against the CURRENT child set, which moves without any class token
                    // changing.
                    op.Update(existing);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    table.Remove(element);
                }
            }
            else if (wanted)
            {
                var manipulator = op.Create(_ctx);
                element.AddManipulator(manipulator);
                table[element] = manipulator;
            }
        }

        private readonly struct VariantOp : IManipulatorOp<StyleVariantManipulator>
        {
            private readonly VariantPayloads _payloads;
            private readonly VariantDeclarations _declarations;

            internal VariantOp(VariantPayloads payloads, VariantDeclarations declarations)
            {
                _payloads = payloads;
                _declarations = declarations;
            }

            public Dictionary<VisualElement, StyleVariantManipulator> Table(ReconcilerContext ctx)
                => ctx.VariantManipulators;

            public StyleVariantManipulator Create(ReconcilerContext ctx)
                => new StyleVariantManipulator(ctx, _payloads, _declarations);

            public void Update(StyleVariantManipulator manipulator)
                => manipulator.UpdatePayloads(_payloads, _declarations);
        }

        private readonly struct ConditionalVariantOp : IManipulatorOp<StyleConditionalVariantManipulator>
        {
            private readonly string[][] _responsive;
            private readonly string[] _dark;

            private readonly int[][] _responsiveDeclarations;
            private readonly int[] _darkDeclarations;

            internal ConditionalVariantOp(string[][] responsive, string[] dark,
                int[][] responsiveDeclarations, int[] darkDeclarations)
            {
                _responsive = responsive;
                _dark = dark;
                _responsiveDeclarations = responsiveDeclarations;
                _darkDeclarations = darkDeclarations;
            }

            public Dictionary<VisualElement, StyleConditionalVariantManipulator> Table(ReconcilerContext ctx)
                => ctx.ConditionalVariantManipulators;

            public StyleConditionalVariantManipulator Create(ReconcilerContext ctx)
                => new StyleConditionalVariantManipulator(ctx, _responsive, _dark,
                    _responsiveDeclarations, _darkDeclarations);

            public void Update(StyleConditionalVariantManipulator manipulator)
                => manipulator.UpdatePayloads(_responsive, _dark, _responsiveDeclarations, _darkDeclarations);
        }

        private readonly struct RelationalVariantOp : IManipulatorOp<StyleRelationalVariantManipulator>
        {
            private readonly List<RelationalBindingConfig>? _configs;

            internal RelationalVariantOp(List<RelationalBindingConfig>? configs)
            {
                _configs = configs;
            }

            public Dictionary<VisualElement, StyleRelationalVariantManipulator> Table(ReconcilerContext ctx)
                => ctx.RelationalVariantManipulators;

            public StyleRelationalVariantManipulator Create(ReconcilerContext ctx)
                => new StyleRelationalVariantManipulator(ctx, _configs);

            public void Update(StyleRelationalVariantManipulator manipulator)
                => manipulator.UpdatePayloads(_configs);
        }

        private readonly struct HasVariantOp : IManipulatorOp<StyleHasVariantManipulator>
        {
            private readonly string[] _checked;
            private readonly string[] _focus;
            private readonly int[] _checkedDeclarations;
            private readonly int[] _focusDeclarations;

            internal HasVariantOp(string[] @checked, string[] focus,
                int[] checkedDeclarations, int[] focusDeclarations)
            {
                _checked = @checked;
                _focus = focus;
                _checkedDeclarations = checkedDeclarations;
                _focusDeclarations = focusDeclarations;
            }

            public Dictionary<VisualElement, StyleHasVariantManipulator> Table(ReconcilerContext ctx)
                => ctx.HasVariantManipulators;

            public StyleHasVariantManipulator Create(ReconcilerContext ctx)
                => new StyleHasVariantManipulator(ctx, _checked, _focus,
                    _checkedDeclarations, _focusDeclarations);

            public void Update(StyleHasVariantManipulator manipulator)
                => manipulator.UpdatePayloads(_checked, _focus, _checkedDeclarations, _focusDeclarations);
        }

        private readonly struct ChildVariantOp : IManipulatorOp<StyleChildVariantManipulator>
        {
            private readonly string[] _payloads;

            internal ChildVariantOp(string[] payloads)
            {
                _payloads = payloads;
            }

            public Dictionary<VisualElement, StyleChildVariantManipulator> Table(ReconcilerContext ctx)
                => ctx.ChildVariantManipulators;

            public StyleChildVariantManipulator Create(ReconcilerContext ctx)
                => new StyleChildVariantManipulator(ctx, _payloads);

            public void Update(StyleChildVariantManipulator manipulator)
                => manipulator.UpdatePayloads(_payloads);
        }

        private readonly struct GapOp : IManipulatorOp<StyleGapManipulator>
        {
            private readonly float _gap;
            private readonly GapAxis _axis;
            private readonly bool _xReverse;
            private readonly bool _yReverse;

            internal GapOp(float gap, GapAxis axis, bool xReverse, bool yReverse)
            {
                _gap = gap;
                _axis = axis;
                _xReverse = xReverse;
                _yReverse = yReverse;
            }

            public Dictionary<VisualElement, StyleGapManipulator> Table(ReconcilerContext ctx)
                => ctx.GapManipulators;

            public StyleGapManipulator Create(ReconcilerContext ctx)
                => new StyleGapManipulator(ctx, _gap, _axis, _xReverse, _yReverse);

            public void Update(StyleGapManipulator manipulator)
                => manipulator.UpdateGap(_gap, _axis, _xReverse, _yReverse);
        }

        private readonly struct DivideOp : IManipulatorOp<StyleDivideManipulator>
        {
            private readonly DivideSpec _spec;

            internal DivideOp(DivideSpec spec)
            {
                _spec = spec;
            }

            public Dictionary<VisualElement, StyleDivideManipulator> Table(ReconcilerContext ctx)
                => ctx.DivideManipulators;

            public StyleDivideManipulator Create(ReconcilerContext ctx)
                => new StyleDivideManipulator(_spec, ctx);

            public void Update(StyleDivideManipulator manipulator)
                => manipulator.UpdateSpec(_spec);
        }

        private readonly struct GridOp : IManipulatorOp<StyleGridManipulator>
        {
            private readonly GridSpec _spec;

            internal GridOp(GridSpec spec)
            {
                _spec = spec;
            }

            public Dictionary<VisualElement, StyleGridManipulator> Table(ReconcilerContext ctx)
                => ctx.GridManipulators;

            public StyleGridManipulator Create(ReconcilerContext ctx)
                => new StyleGridManipulator(ctx, _spec);

            public void Update(StyleGridManipulator manipulator)
                => manipulator.UpdateSpec(_spec);
        }

        #endregion

        #region Variant Manipulator

        // Configures (creates / updates / removes) the element's StyleVariantManipulator
        // from the state-variant tokens (hover:/focus:/active:) found in classNames.
        internal void ApplyVariantManipulator(VisualElement element, string[] classNames)
        {
            var hover = ExtractVariant(classNames, StyleVariantKind.Hover, out var hoverDecl);
            var focus = ExtractVariant(classNames, StyleVariantKind.Focus, out var focusDecl);
            var focusVisible = ExtractVariant(classNames, StyleVariantKind.FocusVisible, out var focusVisibleDecl);
            var active = ExtractVariant(classNames, StyleVariantKind.Active, out var activeDecl);
            var @checked = ExtractVariant(classNames, StyleVariantKind.Checked, out var checkedDecl);
            var hasAny = hover.Length > 0 || focus.Length > 0 || focusVisible.Length > 0
                || active.Length > 0 || @checked.Length > 0;

            Configure<VariantOp, StyleVariantManipulator>(element, hasAny,
                new VariantOp(
                    new VariantPayloads(hover, focus, focusVisible, active, @checked),
                    new VariantDeclarations(hoverDecl, focusDecl, focusVisibleDecl, activeDecl, checkedDecl)));
        }

        private static string[] ExtractVariant(string[] classNames, StyleVariantKind kind, out int[] declarations)
        {
            declarations = Array.Empty<int>();
            if (classNames == null || classNames.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string>? payloads = null;
            List<int>? positions = null;
            for (var i = 0; i < classNames.Length; i++)
            {
                if (StyleVariantClass.TryParse(classNames[i], out var k, out var payload) && k == kind)
                {
                    (payloads ??= new List<string>()).Add(payload ?? string.Empty);
                    (positions ??= new List<int>()).Add(i);
                }
            }

            declarations = positions?.ToArray() ?? Array.Empty<int>();
            return payloads?.ToArray() ?? Array.Empty<string>();
        }

        // Configures the element's StyleConditionalVariantManipulator from the responsive
        // (sm:/md:/lg:/xl:/2xl:) and dark: tokens in classNames.
        internal void ApplyConditionalVariantManipulator(VisualElement element, string[] classNames)
        {
            var responsiveKinds = new[]
            {
                StyleVariantKind.Sm, StyleVariantKind.Md, StyleVariantKind.Lg,
                StyleVariantKind.Xl, StyleVariantKind.Xxl,
            };
            var responsive = new string[responsiveKinds.Length][];
            var responsiveDeclarations = new int[responsiveKinds.Length][];
            for (var i = 0; i < responsiveKinds.Length; i++)
            {
                responsive[i] = ExtractVariant(classNames, responsiveKinds[i], out var breakpointDecl);
                responsiveDeclarations[i] = breakpointDecl;
            }
            var dark = ExtractVariant(classNames, StyleVariantKind.Dark, out var darkDecl);

            var hasAny = dark.Length > 0;
            for (var i = 0; i < responsive.Length && !hasAny; i++)
            {
                hasAny = responsive[i].Length > 0;
            }

            Configure<ConditionalVariantOp, StyleConditionalVariantManipulator>(element, hasAny,
                new ConditionalVariantOp(responsive, dark, responsiveDeclarations, darkDecl));
        }

        // Configures the element's StyleRelationalVariantManipulator from the group-*/peer- tokens (incl. the
        // named group-*/name · peer-*/name forms) in classNames.
        internal void ApplyRelationalVariantManipulator(VisualElement element, string[] classNames)
        {
            var configs = BuildRelationalConfigs(classNames);
            var hasAny = configs != null && configs.Count > 0;

            Configure<RelationalVariantOp, StyleRelationalVariantManipulator>(element, hasAny,
                new RelationalVariantOp(configs));
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

            // (isPeer, name) -> per-state payload lists, indexed by (int)RelationalState, and the className
            // position of each of those payloads in the parallel list beside it.
            Dictionary<(bool IsPeer, string Name), List<string>[]>? map = null;
            Dictionary<(bool IsPeer, string Name), List<int>[]>? positions = null;
            for (var i = 0; i < classNames.Length; i++)
            {
                if (!StyleVariantClass.TryParse(classNames[i], out var kind, out var name, out var payload)
                    || StyleVariantClass.RelationalOf(kind) is not { } relational)
                {
                    continue;
                }
                var key = (relational.IsPeer, name ?? string.Empty);
                map ??= new Dictionary<(bool, string), List<string>[]>();
                positions ??= new Dictionary<(bool, string), List<int>[]>();
                if (!map.TryGetValue(key, out var states))
                {
                    states = new List<string>[StyleVariantClass.RelationalStateCount];
                    map[key] = states;
                    positions[key] = new List<int>[StyleVariantClass.RelationalStateCount];
                }
                var slot = (int)relational.State;
                (states[slot] ??= new List<string>()).Add(payload ?? string.Empty);
                (positions![key][slot] ??= new List<int>()).Add(i);
            }

            if (map == null)
            {
                return null;
            }

            var configs = new List<RelationalBindingConfig>(map.Count);
            foreach (var kv in map)
            {
                var s = kv.Value;
                var d = positions![kv.Key];
                configs.Add(new RelationalBindingConfig(
                    kv.Key.IsPeer, kv.Key.Name,
                    new VariantPayloads(
                        ToPayloadArray(s[(int)StyleVariantClass.RelationalState.Hover]),
                        ToPayloadArray(s[(int)StyleVariantClass.RelationalState.Focus]),
                        ToPayloadArray(s[(int)StyleVariantClass.RelationalState.FocusWithin]),
                        ToPayloadArray(s[(int)StyleVariantClass.RelationalState.Active]),
                        ToPayloadArray(s[(int)StyleVariantClass.RelationalState.Checked])),
                    new VariantDeclarations(
                        ToPositionArray(d[(int)StyleVariantClass.RelationalState.Hover]),
                        ToPositionArray(d[(int)StyleVariantClass.RelationalState.Focus]),
                        ToPositionArray(d[(int)StyleVariantClass.RelationalState.FocusWithin]),
                        ToPositionArray(d[(int)StyleVariantClass.RelationalState.Active]),
                        ToPositionArray(d[(int)StyleVariantClass.RelationalState.Checked]))));
            }
            return configs;
        }

        private static int[] ToPositionArray(List<int>? positions)
            => positions?.ToArray() ?? Array.Empty<int>();

        private static string[] ToPayloadArray(List<string> payloads)
            => payloads?.ToArray() ?? Array.Empty<string>();

        // Applies the three className-driven variant manipulators (pseudo-class hover:/focus:/
        // active:, conditional dark:/sm:…, relational group-/peer-) in one
        // call. The gap manipulator is deliberately excluded — it must run AFTER children are reconciled.
        internal void ApplyVariantManipulators(VisualElement element, string[] classNames)
        {
            RecordVariantGateSource(element, classNames);
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
        // event-driven has- tokens (has-[:checked]: / has-[:focus]:) in classNames. The has-[.class]:
        // form is handled separately by ApplyHasClassVariantConfig (a side-table, not an event
        // manipulator).
        internal void ApplyHasVariantManipulator(VisualElement element, string[] classNames)
        {
            var @checked = ExtractHas(classNames, StyleHasKind.Checked, out var checkedDeclarations);
            var focus = ExtractHas(classNames, StyleHasKind.Focus, out var focusDeclarations);
            var hasAny = @checked.Length > 0 || focus.Length > 0;

            Configure<HasVariantOp, StyleHasVariantManipulator>(element, hasAny,
                new HasVariantOp(@checked, focus, checkedDeclarations, focusDeclarations));
        }

        // Collects the payloads of every has-[:checked]: / has-[:focus]: token of the given kind. A payload
        // that is itself a structural / has variant is skipped (it would have no gating owner on this path),
        // mirroring the structural-config skip.
        private static string[] ExtractHas(string[] classNames, StyleHasKind kind, out int[] declarations)
        {
            declarations = Array.Empty<int>();
            if (classNames == null || classNames.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string>? payloads = null;
            List<int>? positions = null;
            for (var i = 0; i < classNames.Length; i++)
            {
                var cls = classNames[i];
                if (StyleHasVariantClass.TryParse(cls, out var k, out _, out var payload)
                    && k == kind
                    && !StyleStructuralVariantClass.IsStructural(payload)
                    && !StyleHasVariantClass.IsHas(payload)
                    && !StyleAttributeVariantClass.IsAttribute(payload)
                    && !StyleSupportsVariantClass.IsSupports(payload))
                {
                    (payloads ??= new List<string>()).Add(payload ?? string.Empty);
                    (positions ??= new List<int>()).Add(i);
                }
            }

            declarations = positions?.ToArray() ?? Array.Empty<int>();
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
                    StyleVariantPayload.Apply(element, rule.Payloads, false, StyleLayerPriority.Has, _ctx,
                        declarations: rule.Declarations);
                }
                _ctx.HasClassVariants.Remove(element);
            }

            List<(string? ClassName, string?[] Payloads, int[] Declarations)>? rules = null;
            if (classNames != null)
            {
                for (var i = 0; i < classNames.Length; i++)
                {
                    var cls = classNames[i];
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
                        (rules ??= new List<(string? ClassName, string?[] Payloads, int[] Declarations)>())
                            .Add((className, new[] { payload }, new[] { i }));
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
                EvaluateHasClass(_ctx, element, rules);
            }
        }

        // Applies / clears each has-[.class]: rule's payload by querying the element's descendants for the
        // named class. Stateless and idempotent. The scan iterates the direct children's subtrees (element.Q
        // is root-inclusive, so querying the element itself would self-match — :has() is descendant-only, and
        // a self-match would also latch when the payload class equals the queried class).
        private static void EvaluateHasClass(ReconcilerContext ctx, VisualElement element,
            List<(string? ClassName, string?[] Payloads, int[] Declarations)> rules)
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
                StyleVariantPayload.Apply(element, rule.Payloads, on, StyleLayerPriority.Has, ctx,
                    declarations: rule.Declarations);
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
                EvaluateHasClass(_ctx, element, rules);
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
                        EvaluateHasClass(ctx, kv.Key, kv.Value);
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
                    // The resolved target recorded at mount covers every portal flavor; null only for
                    // the never-mounted missing-registry path.
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
                    EvaluateHasClass(ctx, e, rules);
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
                    StyleVariantPayload.Apply(element, rule.Payloads, false, StyleLayerPriority.Attribute, _ctx,
                        declarations: rule.Declarations);
                }
                _ctx.AttributeVariants.Remove(element);
            }

            List<(StyleAttributeNamespace Ns, string Key, string? ExpectedValue, string[] Payloads,
                int[] Declarations)>? rules = null;
            if (classNames != null)
            {
                for (var i = 0; i < classNames.Length; i++)
                {
                    var cls = classNames[i];
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
                        (rules ??= new List<(StyleAttributeNamespace, string, string?, string[], int[])>())
                            .Add((ns, key ?? string.Empty, value, new[] { payload }, new[] { i }));
                    }
                }
            }

            if (rules == null)
            {
                return;
            }
            _ctx.AttributeVariants[element] = rules;
            _ctx.DataAttributes.TryGetValue(element, out var store);
            EvaluateAttributes(_ctx, element, store, rules);
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
            EvaluateAttributes(_ctx, element, store, rules);
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
            ReconcilerContext ctx, VisualElement element, Dictionary<string, string>? store,
            List<(StyleAttributeNamespace Ns, string Key, string? ExpectedValue, string[] Payloads,
                int[] Declarations)> rules)
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
                StyleVariantPayload.Apply(element, rule.Payloads, on, StyleLayerPriority.Attribute, ctx,
                    declarations: rule.Declarations);
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
                foreach (var rule in oldRules)
                {
                    StyleVariantPayload.Apply(element, rule.Payloads, false, StyleLayerPriority.Supports, _ctx,
                        declarations: rule.Declarations);
                }
                _ctx.SupportsVariants.Remove(element);
            }

            List<(string[] Payloads, int[] Declarations)>? rules = null;
            if (classNames != null)
            {
                for (var i = 0; i < classNames.Length; i++)
                {
                    var cls = classNames[i];
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
                        (rules ??= new List<(string[], int[])>())
                            .Add((new string[] { payload ?? string.Empty }, new[] { i }));
                    }
                }
            }

            if (rules == null)
            {
                return;
            }
            _ctx.SupportsVariants[element] = rules;

            // Always-applied: the property is, by construction, one the author is using on a fixed engine.
            foreach (var rule in rules)
            {
                StyleVariantPayload.Apply(element, rule.Payloads, true, StyleLayerPriority.Supports, _ctx,
                    declarations: rule.Declarations);
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
                    StyleVariantPayload.Apply(element, rule.Payloads, false, StyleLayerPriority.Structural, _ctx,
                        declarations: rule.Declarations);
                }
                _ctx.StructuralVariants.Remove(element);
            }

            List<(StyleStructuralKind Kind, int N, string[] Payloads, int[] Declarations)>? rules = null;
            if (classNames != null)
            {
                for (var i = 0; i < classNames.Length; i++)
                {
                    var cls = classNames[i];
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
                        (rules ??= new List<(StyleStructuralKind Kind, int N, string[] Payloads,
                                int[] Declarations)>())
                            .Add((kind, n, new string[] { payload ?? string.Empty }, new[] { i }));
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
            // A z-managed outer's PHYSICAL parent is its layer container (its position there is relative to
            // unrelated same-layer siblings, not this element's true siblings) — TryGetLogicalPosition
            // resolves to the stacking parent + the placeholder's slot instead; an ordinary outer resolves to
            // its own parent/index unchanged, same as the previous outer.parent / outer.parent.IndexOf(outer).
            if (!FiberZLayerCoordinator.TryGetLogicalPosition(_ctx, outer, out var logicalParent, out var logicalIndex))
            {
                return;
            }
            // Exclude the trailing filter bounds-spacer(s) AND a leading back z-layer container (either may
            // be present) from the sibling count: neither is part of the logical child list a
            // first:/last:/nth match sees.
            EvaluateStructural(_ctx, element, LogicalChildSlots.ToLogical(logicalParent!, logicalIndex),
                LogicalChildSlots.Count(logicalParent!), rules);
        }

        // Applies / clears each structural rule's payload for an element at the given sibling position.
        private static void EvaluateStructural(
            ReconcilerContext ctx, VisualElement element, int index, int count,
            List<(StyleStructuralKind Kind, int N, string[] Payloads, int[] Declarations)> rules)
        {
            foreach (var rule in rules)
            {
                var on = StyleStructuralVariantClass.Matches(rule.Kind, rule.N, index, count);
                StyleVariantPayload.Apply(element, rule.Payloads, on, StyleLayerPriority.Structural, ctx,
                    declarations: rule.Declarations);
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

            // The reconciler-invisible children — a filter bounds spacer, a z-index layer container, a ring
            // band — are internal, not logical siblings, so first:/last:/nth must neither count them nor
            // land on one. Walking logical slots and converting each to a physical index does both, wherever
            // in the child list they happen to sit.
            var count = LogicalChildSlots.Count(container);
            for (var i = 0; i < count; i++)
            {
                var slotOccupant = container.ElementAt(LogicalChildSlots.ToPhysical(container, i));
                // A z-managed child's slot holds its PLACEHOLDER, not the element the rules were registered
                // against (ApplyStructuralVariantConfig keys by the real element / its own wrapper) — resolve
                // through the z-registry first, then the ordinary wrapper unwrap (the slot may ALSO hold a
                // shadow / clip-path WRAPPER either way).
                var inner = ResolveWrapped(
                    _ctx.ZLayerPlaceholders.TryGetValue(slotOccupant, out var real) ? real : slotOccupant);
                if (_ctx.StructuralVariants.TryGetValue(inner, out var rules))
                {
                    EvaluateStructural(_ctx, inner, i, count, rules);
                }
            }
        }

        // Configures the element's StyleChildVariantManipulator from the [&>*]:<utility> tokens in classNames
        // and (re-)applies it so every direct child carries the wrapped payload. Mirrors ApplyGapManipulator —
        // call AFTER the container's children have been reconciled so the manipulator sees the final child set.
        internal void ApplyChildVariantManipulator(VisualElement element, string[] classNames)
        {
            // Fast early-out for the ~99% of elements with no [&>*]: class and no existing manipulator.
            if (!StyleChildVariantClass.HasChildVariantClass(classNames))
            {
                Configure<ChildVariantOp, StyleChildVariantManipulator>(element, wanted: false, default);
                return;
            }

            // A [&>*]: token can still resolve to no payload (every wrapped payload was a dead-token kind —
            // structural / has- / attribute- / supports-), so the real gate is TryExtract, not the prefix scan.
            var hasPayloads = StyleChildVariantClass.TryExtract(classNames, out var payloads);

            Configure<ChildVariantOp, StyleChildVariantManipulator>(element, hasPayloads,
                new ChildVariantOp(payloads));
        }

        // Configures the four manipulators whose existence is gated purely on a layout utility class being
        // present: gap, divide, grid, text-balance. They are configured as a unit because gap and grid share
        // one ownership rule — a grid owns its children's margins, so the gap manipulator must be suppressed
        // for exactly the class lists that produce a grid manipulator. Call AFTER the container's children
        // have been reconciled so each sees the final child list.
        // Resolves its own class source rather than taking the one the paint passes use: those resolve after
        // the structural / has- passes (see ApplyPostChildrenClassPasses), and gap has to run before them.
        internal void ApplyLayoutManipulators(VisualElement element, string[] classNames)
            => ApplyResolvedLayoutManipulators(element, ResolveGateClasses(element, classNames));

        // The composed source (see ResolveGateClasses) rather than the reconciled array: the bare class
        // `dark:font-mono` puts on the live list carries no rule of its own, so nothing but this resolver
        // realises the payload. TypographyHasNoStylesheetRuleTests fails if a sheet ever declares one.
        internal void ApplyFontLayer(VisualElement element, string[] oldClassNames, string[] newClassNames)
            => StyleFontResolver.ApplyOnClassChange(element, oldClassNames,
                ResolveGateClasses(element, newClassNames));

        internal void ApplyFontLayerOnCreate(VisualElement element, string[] classNames)
            => StyleFontResolver.ApplyIfPresent(element, ResolveGateClasses(element, classNames));

        // Same rule as ApplyFontLayer, over the same guard's other half.
        internal void ApplyTextEffects(VisualElement element, string[] classNames)
            => StyleTextEffectResolver.Apply(_ctx, element, ResolveGateClasses(element, classNames));

        // Re-runs every class-driven pass a variant payload can change, for an element a variant just
        // toggled a gate token on (wired to ReconcilerContext.VariantGatedReSync). Runs the same bodies the
        // reconcile path runs, against the same composed source, so a toggle lands where a full patch would
        // put it — bar the two wrapper layers, whose parent surgery is forbidden in the pointer / focus
        // callback or breakpoint notification this is called from.
        private void ReSyncVariantGatedPasses(VisualElement element)
        {
            _ctx.VariantGateClasses.TryGetValue(element, out var state);
            // The array the element's own reconcile pass last applied, or none when no pass ever recorded
            // one: the trigger was a width payload, which gates nothing and so never earns an entry, or the
            // payload came from a [&>*]: rule on the PARENT, whose children are fully created before it runs.
            // The second case is temporary for a child rendered as an element — its own next patch runs the
            // class passes and records the array. A V.Text child's patch runs none, ever, which is why the
            // empty array below is its reconciled array rather than a stand-in; the live list stands in for
            // the layout gates in the element case only. The paint sequence stands down for both, since
            // PaintTail is unknown and a Motion must not be given a silhouette.
            // That makes a [&>*]: paint land inconsistently, which is the cost of not guessing: a child that
            // declares ANY gated payload of its own was recorded at create, so the parent's payload finds
            // PaintTail set and paints at mount, while a child that declares none paints only from its next
            // patch. Recording how a child is driven before its parent's rules run would close it.
            // A TextNode's element applies no class of its own at any render, so an empty array is not a
            // stand-in for its reconciled one — it IS its reconciled one. Without this the two resolvers
            // below stand down for such a child forever, and a container's [&>*]:font-mono or
            // [&>*]:uppercase lands on its class list and changes nothing, at mount and at every patch.
            var reconciled = state?.Reconciled
                ?? (_ctx.TextNodeElements.ContainsKey(element) ? System.Array.Empty<string>() : null);
            var previous = state?.Resolved;
            var resolved = reconciled == null ? LiveClasses(element)
                : state == null ? reconciled
                : ComposeVariantClasses(reconciled, state.Tokens, state.Resolved);
            var classesChanged = !ReferenceEquals(previous, resolved);
            if (state != null)
            {
                state.Resolved = resolved;
            }
            // Font and text effects run only from a RECORDED array, never from the live-list stand-in the
            // layout gates accept above. Both resolvers rewrite unconditionally, and the live list is not a
            // narrower version of their source but a different one: the reconciler keeps a DECLARED font-[…]
            // / leading-[…] off it, since those two are resolver-owned, and the channels that raise no
            // signal (whileHoverClass and its siblings) put utilities on it that no reconcile ever saw.
            // Handing it over would not lose the payload, it would resolve some other answer over the
            // element's correct one with nothing left to put it back.
            // Their order is the reconcile path's own (SyncClassDrivenStyling ahead of
            // ApplyPostChildrenClassPasses), so a re-sync cannot resolve a pass against a different
            // predecessor than a patch would.
            if (reconciled != null)
            {
                StyleFontResolver.ApplyOnClassChange(element, previous ?? resolved, resolved);
            }
            ApplyResolvedLayoutManipulators(element, resolved);
            if (reconciled != null)
            {
                StyleTextEffectResolver.Apply(_ctx, element, resolved);
            }
            var paintTail = state?.PaintTail;
            if (paintTail == null)
            {
                return;
            }
            // Both recorded answers run the sequence — a Motion still carries the gradient, the animate driver
            // and the filter tween, and only the three silhouette layers stand down. The clip verdict is the
            // same one the reconcile path forwards from its own clip patch: that call returns exactly its own
            // wantWrap answer, so reading the predicate here cannot disagree. Only the shadow reads it, so the
            // Motion path skips the scan the way PatchMotion hard-codes the answer.
            ApplyResolvedClassPasses(element, resolved, classesChanged, paintTail.Value,
                paintTail.Value && StyleClipPathClass.WantsClipWrapper(resolved), canReleaseFace: false);
        }

        // Records the class array an element that DECLARES a variant-gated payload will need if that payload
        // ever fires. The re-sync has no reconciled array of its own to compose from, and the toggle that
        // wakes it can arrive with no reconcile anywhere in sight (a breakpoint crossing, a theme flip), so
        // the array has to be put aside while a pass still holds it. Runs where the variant manipulators are
        // configured, which is exactly the create + class-content-changed pair; a class list whose content
        // did not change leaves an equivalent array already recorded.
        private void RecordVariantGateSource(VisualElement element, string[] classNames)
        {
            if (_ctx.VariantGateClasses.TryGetValue(element, out var state))
            {
                state.Reconciled = classNames;
                return;
            }
            if (StyleVariantPayload.DeclaresGatePayload(classNames))
            {
                _ctx.VariantGateClasses[element] = new VariantGateState { Reconciled = classNames };
            }
        }

        // The ordered configure sequence, shared by the reconcile path and the variant re-sync so the two
        // cannot drift.
        // Gap and grid are mutually exclusive owners of the children's margins — a grid class suppresses
        // the gap manipulator — so a class change that flips ownership creates one and removes the other in
        // the SAME pass. Each clears the margins it wrote as it detaches, so the DEPARTING one has to run
        // first: the reverse order lets that clear wipe the arriving manipulator's fresh writes, leaving
        // the children unspaced until something unrelated forces a re-apply. Which one departs is exactly
        // the grid-class verdict, so it selects the order — and is forwarded so the gap gate does not
        // re-scan for it.
        // Divide and text-balance are in no such handoff: divide writes only the border width and color of
        // the one edge it draws on (and nulls that same pair on teardown), text-balance writes only the
        // element's OWN width, and neither gap nor grid writes a border at all — so the three write sets
        // are disjoint and the position of these two in the sequence is not load-bearing. Grid writes its
        // CHILDREN's widths, which is the one slot text-balance also writes, and the handoff for that is
        // the child's own: a text-balance element inside a grid container stands down entirely (see
        // StyleTextBalanceManipulator's grid-parent check).
        private void ApplyResolvedLayoutManipulators(VisualElement element, string[] classNames)
        {
            if (StyleGridClass.HasGridClass(classNames))
            {
                ApplyGapManipulator(element, classNames, gridSuppressed: true);
                ApplyGridManipulator(element, classNames);
            }
            else
            {
                ApplyGridManipulator(element, classNames);
                ApplyGapManipulator(element, classNames, gridSuppressed: false);
            }
            ApplyDivideManipulator(element, classNames);
            ApplyTextBalanceManipulator(element, classNames);
        }

        // The class source every gate-driven pass reads: the reconciled array, followed by each gate token a
        // variant currently has applied, weakest payload first.
        //
        // A variant payload is realized by writing its bare utility onto the live class list, and the bare
        // form is the only one the gates recognize — `md:shadow-lg` is not a shadow token, `shadow-lg` is —
        // so the reconciled array alone can never see it. The live list alone cannot stand in for the
        // reconciled array either: it structurally cannot hold a variant token, a bracket or static-scale
        // value (those resolve to inline style), or a class the projection has suppressed, and the passes
        // read all three (the bounds spacer parses border-[8px], the gradient teardown asks whether a
        // bg-[addr:…] owns the background-image, the animate teardown re-asserts every inline-resolved
        // token). Appending rather than substituting keeps both, which is what lets every pass here take one
        // array and lets a pass be added later without auditing which source it needs.
        //
        // Appending the payloads LAST is what ranks them above the base utilities: each of these families
        // resolves last-token-wins, and a payload outranks the base it overrides. Composing them that way
        // makes it structural rather than a property of the live list's own append order, which the class
        // diff can invert by re-adding a base token after a payload that is already lit. Two payloads of one
        // family rank against each other by the priority each was applied at, which VariantGateState keeps
        // the token order in.
        //
        // changed reports whether the result differs from what this element was last resolved to, which is
        // what the skew and border-style stashes need: a release-and-re-stash on a pass that changed nothing
        // would drop the suppression and re-expose the native rectangle behind the silhouette, and the
        // re-stash depends on events a variant toggle does not fire.
        //
        // paintTail records how this element is driven, so the re-sync can read it back and never attach a
        // silhouette a Motion's own patch would not have attached.
        //
        // The global Count check keeps a tree that uses no variant-gated class on the reconciled-array path
        // entirely, at one int compare per element per patch.
        private string[] ResolveVariantClasses(VisualElement element, string[] oldClassNames,
            string[] newClassNames, bool paintTail, out bool changed)
        {
            if (_ctx.VariantGateClasses.Count == 0
                || !_ctx.VariantGateClasses.TryGetValue(element, out var state))
            {
                changed = !ReferenceEquals(oldClassNames, newClassNames);
                return newClassNames;
            }

            // An element that DECLARES a gated payload but has none applied right now reads its reconciled
            // array unchanged — there is no token to append — so the common `hover:shadow-lg` element pays
            // the composition only while it is actually hovered, and a dictionary probe the rest of the time.
            var resolved = state.Tokens.Count == 0
                ? newClassNames
                : ComposeVariantClasses(newClassNames, state.Tokens, state.Resolved);
            changed = !ReferenceEquals(oldClassNames, newClassNames)
                || !ReferenceEquals(state.Resolved, resolved);
            state.Reconciled = newClassNames;
            state.Resolved = resolved;
            state.PaintTail = paintTail;
            return resolved;
        }

        // The same source for the CREATE path, which has no previous array to compare against — every layer
        // is being attached for the first time, so the change verdict has no reader. Call it at the same
        // point in the create sequence the patch path resolves at: AFTER the structural / has- passes, whose
        // payloads land on this very element.
        internal string[] ResolveVariantClassesOnCreate(VisualElement element, string[] classNames, bool paintTail)
            => ResolveVariantClasses(element, classNames, classNames, paintTail, out _);

        // The same source for the gates that resolve at their own point in the sequence — the four layout
        // manipulators (ApplyLayoutManipulators), the font layer (ApplyFontLayer) and the text-effect cascade
        // (ApplyTextEffects). It reads the cached array but does not REPLACE it: the paint resolve a few
        // passes later answers "did the classes change" by comparing against that same record, and advancing it
        // here would swallow a payload one of the has- / attribute passes in between had just toggled.
        private string[] ResolveGateClasses(VisualElement element, string[] classNames)
            => _ctx.VariantGateClasses.Count == 0
                || !_ctx.VariantGateClasses.TryGetValue(element, out var state)
                || state.Tokens.Count == 0
                    ? classNames
                    : ComposeVariantClasses(classNames, state.Tokens, state.Resolved);

        // Builds classNames plus every variant-applied gate token, handing back reuse unchanged when the
        // result would match it token for token, and classNames itself when no payload is lit.
        //
        // The tracked tokens rather than the element's live class list: they are the same set for every
        // family these passes read, they are already ranked by the priority each payload was applied at, and
        // reading them costs no enumerator — where walking the live list costs one per element per patch,
        // plus a membership test per class on it. The narrowing that buys is real and deliberate: a bare utility
        // written by a subsystem that raises no signal (whileHoverClass / whileTapClass / whileFocusClass,
        // the animation scheduler, the drag layer) is on the live list but never in this source, so it drives
        // no gate. Only a signalling writer can, and a writer with no signal could not keep a pass correct
        // across the toggle back off anyway.
        //
        // A token the reconciled array ALREADY names is appended again rather than left where it sits.
        // Declaring one literally and behind a variant is legal (gap-4 md:gap-4), and the duplicate is inert
        // to every last-token-wins reader here — but leaving the literal occurrence to stand for the payload
        // ranks it below every appended token, so `shadow-lg dark:shadow-sm hover:shadow-lg` would paint the
        // dark preset even though the hover layer outranks it.
        //
        // The reuse compare runs before anything is built, so the steady state allocates NOTHING. This runs
        // on every patch of every element a variant has a gate token applied to.
        private static string[] ComposeVariantClasses(string[] classNames, List<string> tokens, string[]? reuse)
        {
            if (tokens.Count == 0)
            {
                return classNames;
            }
            var total = classNames.Length + tokens.Count;
            if (reuse != null && reuse.Length == total && TailEquals(reuse, classNames.Length, tokens)
                && HeadEquals(reuse, classNames))
            {
                return reuse;
            }

            var composed = new string[total];
            Array.Copy(classNames, composed, classNames.Length);
            for (var i = 0; i < tokens.Count; i++)
            {
                composed[classNames.Length + i] = tokens[i];
            }
            return composed;
        }

        // Whether array carries exactly tokens from start on.
        private static bool TailEquals(string[] array, int start, List<string> tokens)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (array[start + i] != tokens[i])
                {
                    return false;
                }
            }
            return true;
        }

        // Whether array opens with exactly head's tokens. The reconciled array can be a fresh instance
        // carrying the same tokens on every render, so the cached composition is still reusable then — but
        // only once its head is confirmed.
        private static bool HeadEquals(string[] array, string[] head)
        {
            for (var i = 0; i < head.Length; i++)
            {
                if (array[i] != head[i])
                {
                    return false;
                }
            }
            return true;
        }

        // Materializes the element's live USS class list, for the one caller that has no recorded array to
        // compose on (see ReSyncVariantGatedPasses). Allocates a fresh list per call rather than reusing a
        // shared buffer, because a patch can re-enter this path through a nested reconcile.
        private static string[] LiveClasses(VisualElement element)
        {
            var classes = new List<string>();
            foreach (var cls in element.GetClasses())
            {
                classes.Add(cls);
            }
            return classes.ToArray();
        }

        // Configures the element's StyleGapManipulator from the gap-* / gap-x-* / gap-y-* token in
        // classNames and (re-)applies it so the inter-child margins reflect the current child set. Call
        // this AFTER the container's children have been reconciled so the manipulator sees the final
        // child list. gridSuppressed is the caller's grid-class verdict: a grid container routes its gap
        // through StyleGridManipulator (the grid owns the children's widths AND their margins, so the two
        // must never both write the margin edges), and the caller already needs that verdict to order the
        // two calls.
        private void ApplyGapManipulator(VisualElement element, string[] classNames, bool gridSuppressed)
        {
            if (gridSuppressed)
            {
                Configure<GapOp, StyleGapManipulator>(element, wanted: false, default);
                return;
            }

            // Fast early-out for the ~99% of elements with no gap class and no existing manipulator: a
            // cheap prefix scan (no dictionary lookup, no substring) before the full TryExtract parse.
            if (!StyleGapClass.HasGapClass(classNames))
            {
                Configure<GapOp, StyleGapManipulator>(element, wanted: false, default);
                return;
            }

            var hasGap = StyleGapClass.TryExtract(classNames, out var gap, out var axis);
            StyleGapClass.ExtractReverseMarkers(classNames, out var xReverse, out var yReverse);

            Configure<GapOp, StyleGapManipulator>(element, hasGap, new GapOp(gap, axis, xReverse, yReverse));
        }

        // Configures the element's StyleDivideManipulator from the divide-x / divide-y (+ width / color /
        // reverse marker) tokens in classNames and (re-)applies it so the inter-child borders reflect the
        // current child set. Mirrors ApplyGapManipulator — call AFTER the container's children have been
        // reconciled so the manipulator sees the final child list.
        private void ApplyDivideManipulator(VisualElement element, string[] classNames)
        {
            // Fast early-out for the ~99% of elements with no divide class and no existing manipulator.
            if (!StyleDivideClass.HasDivideClass(classNames))
            {
                Configure<DivideOp, StyleDivideManipulator>(element, wanted: false, default);
                return;
            }

            var hasDivide = StyleDivideClass.TryExtract(classNames, out var spec);

            Configure<DivideOp, StyleDivideManipulator>(element, hasDivide, new DivideOp(spec));
        }

        // Configures the element's StyleGridManipulator from the grid-cols-* token (and the gap-* it owns) in
        // classNames and (re-)applies it so the column sizing reflects the current child set. Mirrors
        // ApplyGapManipulator — call AFTER the container's children have been reconciled so the manipulator
        // sees the final child list.
        private void ApplyGridManipulator(VisualElement element, string[] classNames)
        {
            // Fast early-out for the ~99% of elements with no grid-cols class and no existing manipulator.
            if (!StyleGridClass.HasGridClass(classNames))
            {
                Configure<GridOp, StyleGridManipulator>(element, wanted: false, default);
                return;
            }

            var hasGrid = StyleGridClass.TryExtract(classNames, out var columns);
            StyleGridClass.ExtractGaps(classNames, out var columnGap, out var rowGap);

            Configure<GridOp, StyleGridManipulator>(element, hasGrid,
                new GridOp(new GridSpec(columns, columnGap, rowGap)));
        }

        // Carries no per-element spec to diff, unlike Gap/Grid/Divide — the manipulator re-derives
        // everything itself. Kept outside the shared Configure step because its teardown owes the element a
        // restore of the shared inline width slot, which the shared body has no way to signal.
        private void ApplyTextBalanceManipulator(VisualElement element, string[] classNames)
        {
            // Fast early-out for the ~99% of elements with no text-balance class and no existing manipulator.
            if (!StyleTextBalanceClass.HasTextBalanceClass(classNames))
            {
                if (_ctx.TextBalanceManipulators.TryGetValue(element, out var stale))
                {
                    element.RemoveManipulator(stale);
                    _ctx.TextBalanceManipulators.Remove(element);
                    // Detach nulls the borrowed width slot, taking a w-[..] or size-[..] applied in this
                    // same patch with it — the class diff re-applies a token only on a change. The layer
                    // map rather than a class array: a w-[600px] never enters the class list, and the array
                    // here may be the element's LIVE one — ReSyncVariantGatedPasses substitutes it for an
                    // element no pass has recorded a reconciled array for — which carries no bracket tokens.
                    StyleArbitraryValueResolver.ReapplyWidthSlot(element);
                }
                return;
            }

            if (_ctx.TextBalanceManipulators.TryGetValue(element, out var existing))
            {
                existing.Refresh();
            }
            else
            {
                var manipulator = new StyleTextBalanceManipulator(_ctx);
                element.AddManipulator(manipulator);
                _ctx.TextBalanceManipulators[element] = manipulator;
            }
        }

        #endregion

        #region Helpers

        // The element a child added to this one actually lands under — the rule VisualElement.Add follows.
        // Keyed on the redirect, not on a widget type: V.Custom<T> mounts ANY VisualElement subclass with
        // children, so the composites that redirect are not a set Velvet gets to choose. Every caller that
        // names "the children's container" must name this same element — the reconcile target, and the box
        // the layout manipulators read direction / wrap from and write a container margin to.
        //
        // A null contentContainer (the collection views, which build their rows themselves) answers with the
        // element. Not a crash guard: VisualElement is null-safe throughout — childCount reads 0, the indexer
        // yields nothing. Insert quietly does nothing there, so nothing should be reconciled into one.
        internal static VisualElement GetChildContainer(VisualElement element)
        {
            var content = element.contentContainer;
            return content ?? element;
        }

        #endregion
    }
}
