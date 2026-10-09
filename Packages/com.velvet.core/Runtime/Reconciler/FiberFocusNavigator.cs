#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// How a host panel (<c>V.Portal(layer:)</c> / <c>V.WorldSpace</c>) participates in sequential
    /// (Tab/Shift-Tab) focus order relative to the panel declaring it. <see cref="Isolated"/> (the default)
    /// is the pre-existing behavior: the host panel's focus ring wraps internally and never crosses the
    /// panel boundary — the explicit-opt-in stance of the cross-panel navigation decision.
    /// <see cref="Chained"/> joins the declaring panel's Tab order at the portal's call site (iframe
    /// semantics): Tab past the host ring's last element exits to the element after the portal's
    /// placeholder; Tab reaching the placeholder's position enters the host at its ring-first (Shift-Tab
    /// symmetric). Arrow/2D navigation never crosses panels.
    /// </summary>
    public enum PanelFocusOrder
    {
        Isolated,
        Chained,
    }

    // Reconciler-side bookkeeping for one focus-scope element, keyed in ReconcilerContext.FocusScopeBindings
    // by the scope root element itself. Mutable per-scope focus state the navigator maintains from FocusIn
    // events: the member that last held focus (SingleTabStop re-entry / Contain snap-back target). The
    // element focused when the scope mounted, RestoreFocus's target on unmount, is React Aria's nodeToRestore.
    internal sealed class FocusScopeBinding
    {
        public FocusScopeSettings Settings;
        public VisualElement? LastFocusedMember;
        public VisualElement? RestoreTarget;
        // AutoFocus is mount-once: the latch is set on the scope's FIRST attach no matter
        // what the setting held then, so neither a keyed reorder's re-attach nor a post-mount settings
        // flip can ever fire it again.
        public bool AutoFocusFired;
        public EventCallback<AttachToPanelEvent>? OnAttach;

        // Creation order across every mounted tree. Of two contained scopes a landing crosses between, the
        // newer one keeps it; a strict order is what stops the two pulling focus back from each other.
        public readonly long Sequence = ++s_lastSequence;
        private static long s_lastSequence;

        public FocusScopeBinding(FocusScopeSettings settings)
        {
            Settings = settings;
        }
    }

    /// <summary>
    /// The single owner of every focus-navigation interception in Velvet — the keyboard sibling of
    /// <c>FiberCrossPanelInput</c>'s two classes, attached exactly once per panel (always to the panel's
    /// TRUE root, <c>panel.visualTree</c>, never to a subtree or per scope — an element whose panel is not
    /// resolved yet defers the attach to its own <c>AttachToPanelEvent</c>).
    /// Sequential (Next/Previous) <see cref="NavigationMoveEvent"/>s are intercepted on the verified engine
    /// contract pinned by <c>FocusNavigationInterceptionTests</c>: the default focus move runs post-dispatch,
    /// after every listener, and its only suppression is <see cref="FocusController.IgnoreEvent"/> — so a
    /// TrickleDown listener deterministically preempts it. Spatial moves (Left/Right/Up/Down) are not
    /// intercepted, except an arrow on an axis a <c>singleTabStop</c> group's orientation excludes, which is
    /// ignored before the engine moves focus: 2D arrow/dpad/stick navigation is the engine's own <c>GetNextFocusable2D</c>, which this
    /// layer composes with rather than reimplements. Sequential prediction and redirection use the public
    /// <see cref="VisualElementFocusRing"/> — the exact class the runtime ring delegates Next/Previous to
    /// (<c>NavigateFocusRing.m_Ring</c>), so predictions cannot drift from what the engine would have done.
    /// Containment always resolves through the NEAREST enclosing contain scope — an element inside a plain
    /// or SingleTabStop scope nested in a modal still belongs to the modal's containment, on every path.
    /// </summary>
    internal static class FiberFocusNavigator
    {
        // Bounds the SingleTabStop exit walk (skipping members while searching for the first focusable
        // outside the scope) so a pathological ring cannot spin this listener unboundedly.
        private const int MaxRingWalk = 4096;

        // Every context whose navigator is attached to a panel. Containment reads across all of them, so that
        // two mounted trees' scopes rank against each other.
        private static readonly List<ReconcilerContext> s_attachedContexts = new();

        // Attaches the navigator's listener trio to `element`'s panel root exactly once per panel. The
        // anchor is always the panel's TRUE root (panel.visualTree): registering on anything narrower
        // would compute subtree rings that drift from the engine's own panel-wide ring, and registering
        // per call-site element would stack duplicate listeners whose relative order inverts the branch
        // priority. An element with no panel yet defers via a tracked self-removing attach hook (one per
        // element — repeat calls while detached dedup). Called from V.Mount (main panel),
        // ConfigureChainedPlaceholder (declaring + host panels), and the focus-scope attach hook
        // (whatever panel the scope lands in).
        internal static void EnsureAttached(VisualElement? element, ReconcilerContext ctx)
        {
            var pending = Request(element, ctx);
            if (pending != null)
            {
                pending.Pinned = true;
            }
        }

        // EnsureAttached for a caller that stops needing the navigator on the element's panel: the release it
        // returns unregisters a deferred hook only where no EnsureAttached caller asked for one too. Null where
        // the element already has a panel and nothing is left pending.
        internal static Action? HoldAttached(VisualElement element, ReconcilerContext ctx)
        {
            var pending = Request(element, ctx);
            if (pending == null)
            {
                return null;
            }
            return () =>
            {
                if (pending.Pinned)
                {
                    return;
                }
                pending.Element.UnregisterCallback(pending.Hook);
                ctx.NavigatorPendingAttachHooks.Remove(pending);
            };
        }

        private static NavigatorPendingAttach? Request(VisualElement? element, ReconcilerContext ctx)
        {
            if (element == null)
            {
                return null;
            }
            var root = element.panel?.visualTree;
            if (root != null)
            {
                AttachToRoot(root, ctx);
                return null;
            }
            foreach (var existing in ctx.NavigatorPendingAttachHooks)
            {
                if (ReferenceEquals(existing.Element, element))
                {
                    return existing;
                }
            }
            NavigatorPendingAttach pending = null!;
            EventCallback<AttachToPanelEvent> hook = _ =>
            {
                element.UnregisterCallback(pending.Hook);
                ctx.NavigatorPendingAttachHooks.Remove(pending);
                AttachToRoot(element.panel?.visualTree, ctx);
            };
            pending = new NavigatorPendingAttach(element, hook);
            element.RegisterCallback(hook);
            ctx.NavigatorPendingAttachHooks.Add(pending);
            return pending;
        }

        // Unregisters and forgets any deferred attach hook still pending on `element`. Part of the
        // element-teardown scrub: a pooled element must not carry a live hook into its next role (it
        // would attach the navigator to whatever panel the recycled element lands in).
        internal static void ReleasePendingAttachHooks(VisualElement element, ReconcilerContext ctx)
        {
            for (var i = ctx.NavigatorPendingAttachHooks.Count - 1; i >= 0; i--)
            {
                var pending = ctx.NavigatorPendingAttachHooks[i];
                if (ReferenceEquals(pending.Element, element))
                {
                    element.UnregisterCallback(pending.Hook);
                    ctx.NavigatorPendingAttachHooks.RemoveAt(i);
                }
            }
        }

        private static void AttachToRoot(VisualElement? root, ReconcilerContext ctx)
        {
            if (root == null || ctx.NavigatorAttachments.ContainsKey(root))
            {
                return;
            }
            // From the first render into a panel, so a press that opens the first focus-visible user counts.
            InputModality.Track(root.panel);
            EventCallback<NavigationMoveEvent> onMove = evt => OnNavigationMove(evt, root, ctx);
            EventCallback<FocusInEvent> onFocusIn = evt => OnFocusIn(evt, ctx);
            EventCallback<FocusOutEvent> onFocusOut = evt => OnFocusOut(evt, ctx);
            root.RegisterCallback(onMove, TrickleDown.TrickleDown);
            root.RegisterCallback(onFocusIn);
            root.RegisterCallback(onFocusOut);
            ctx.NavigatorAttachments[root] = (onMove, onFocusIn, onFocusOut);
            if (!s_attachedContexts.Contains(ctx))
            {
                s_attachedContexts.Add(ctx);
            }
        }

        internal static void DetachAll(ReconcilerContext ctx)
        {
            foreach (var (root, callbacks) in ctx.NavigatorAttachments)
            {
                root.UnregisterCallback(callbacks.OnMove, TrickleDown.TrickleDown);
                root.UnregisterCallback(callbacks.OnFocusIn);
                root.UnregisterCallback(callbacks.OnFocusOut);
            }
            ctx.NavigatorAttachments.Clear();
            // MUTANT_SURVIVES(equivalent, line removed): a disposed context has emptied its scope, portal
            // and z-layer tables, so a focused element it still finds is one a live context finds as well,
            // or one pulled back from exactly as a reading that finds nothing is.
            s_attachedContexts.Remove(ctx);
            foreach (var pending in ctx.NavigatorPendingAttachHooks)
            {
                pending.Element.UnregisterCallback(pending.Hook);
            }
            ctx.NavigatorPendingAttachHooks.Clear();
        }

        // Configures a portal placeholder's participation in the declaring panel's Tab order
        // (PanelFocusOrder). Chained: the hidden placeholder becomes a zero-size, out-of-flow proxy tab
        // stop (ring membership requires the element to be displayed; absolute keeps it out of flex flow
        // and the gap/divide index walks, which skip out-of-flow children) and both chained registries are
        // wired. Isolated: everything is reset to the plain hidden placeholder — idempotent in both
        // directions, so a patch flipping FocusOrder routes back through here.
        internal static void ConfigureChainedPlaceholder(
            VisualElement placeholder, PanelHostRecord record, bool chained, ReconcilerContext ctx)
        {
            var hostRoot = record.Document != null ? record.Document.rootVisualElement : null;
            if (chained && hostRoot != null)
            {
                placeholder.style.display = DisplayStyle.Flex;
                placeholder.style.position = Position.Absolute;
                placeholder.style.width = 0f;
                placeholder.style.height = 0f;
                placeholder.focusable = true;
                placeholder.tabIndex = 0;
                ctx.ChainedPlaceholders[placeholder] = record;
                // A shared layer host can carry several portals; the first chained one owns the host's
                // ring-edge escape (flow between two portals' slots stays panel-internal either way), and
                // ownership is handed to a survivor when the owner departs — see ReleaseChainedOwnership.
                ctx.ChainedHostRoots.TryAdd(hostRoot, placeholder);
                EnsureAttached(placeholder, ctx);
                EnsureAttached(hostRoot, ctx);
                return;
            }
            if (ctx.ChainedPlaceholders.Remove(placeholder))
            {
                placeholder.style.display = DisplayStyle.None;
                placeholder.style.position = StyleKeyword.Null;
                placeholder.style.width = StyleKeyword.Null;
                placeholder.style.height = StyleKeyword.Null;
                placeholder.focusable = false;
                placeholder.tabIndex = 0;
                ReleasePendingAttachHooks(placeholder, ctx);
                ReleaseChainedOwnership(placeholder, hostRoot, ctx);
            }
        }

        // Drops `placeholder`'s ring-edge-escape ownership of `hostRoot` and re-elects any surviving
        // chained placeholder of the same host, so the remaining portals keep their exit. Runs when a
        // chained portal unmounts (FiberElementCleaner) and when a patch flips it back to Isolated. The
        // caller must have removed the departing placeholder from ChainedPlaceholders first, or it would
        // re-elect itself.
        internal static void ReleaseChainedOwnership(
            VisualElement placeholder, VisualElement? hostRoot, ReconcilerContext ctx)
        {
            if (hostRoot == null || !ctx.ChainedHostRoots.TryGetValue(hostRoot, out var owner)
                || !ReferenceEquals(owner, placeholder))
            {
                return;
            }
            ctx.ChainedHostRoots.Remove(hostRoot);
            foreach (var (candidate, record) in ctx.ChainedPlaceholders)
            {
                var candidateRoot = record.Document != null ? record.Document.rootVisualElement : null;
                if (ReferenceEquals(candidateRoot, hostRoot))
                {
                    ctx.ChainedHostRoots[hostRoot] = candidate;
                    return;
                }
            }
        }

        // One sequential-navigation move, as every mode below sees it. The six travel together because a
        // mode is a decision about this exact move: swapping any one of them for another mode's value would
        // redirect a move that was never dispatched. Taken by `in` so the mode chain stays allocation-free
        // inside an event dispatch.
        private readonly struct NavigationMove
        {
            internal NavigationMoveEvent Evt { get; init; }
            internal IPanel Panel { get; init; }
            internal VisualElement PanelRoot { get; init; }
            internal VisualElement Focused { get; init; }
            internal bool Forward { get; init; }
            internal ReconcilerContext Ctx { get; init; }
        }

        private static void OnNavigationMove(NavigationMoveEvent evt, VisualElement panelRoot, ReconcilerContext ctx)
        {
            var forward = evt.direction == NavigationMoveEvent.Direction.Next;
            if (!forward && evt.direction != NavigationMoveEvent.Direction.Previous)
            {
                IgnoreExcludedAxisMove(evt, panelRoot, ctx);
                return;
            }
            var panel = panelRoot.panel;
            var focused = panel?.focusController?.focusedElement as VisualElement;
            if (focused == null)
            {
                return;
            }

            // A scope that is both a group and the nearest contain scope behaves as contain.
            var containRoot = FindEnclosingContainScopeRoot(focused, ctx, out _);
            var groupRoot = FindOutermostSingleTabStopRoot(focused, ctx, out _);
            var singleTabStop = groupRoot != null && !ReferenceEquals(groupRoot, containRoot);

            // Evaluated in this exact order — each mode is only reached once the earlier ones declined,
            // mirroring their real precedence (SingleTabStop group > Contain wrap > boundary escape >
            // SingleTabStop group-entry prediction).
            var move = new NavigationMove
            {
                Evt = evt,
                Panel = panel!,
                PanelRoot = panelRoot,
                Focused = focused,
                Forward = forward,
                Ctx = ctx,
            };
            if (TryHandleSingleTabStopGroupExit(in move, containRoot, groupRoot, singleTabStop))
            {
                return;
            }
            if (TryHandleContainScopeWrap(in move, containRoot))
            {
                return;
            }
            if (TryHandleBoundaryEscape(in move))
            {
                return;
            }
            HandleSingleTabStopGroupEntryPrediction(in move);
        }

        // Mode (a): the whole subtree acts as ONE tab stop. Every reachable outcome inside this mode is a
        // terminal move outcome (it never falls through to a later mode).
        private static bool TryHandleSingleTabStopGroupExit(
            in NavigationMove move, VisualElement? containRoot, VisualElement? scopeRoot, bool singleTabStop)
        {
            if (!singleTabStop)
            {
                return false;
            }
            var focused = move.Focused;
            // The whole subtree acts as ONE tab stop: walk the sequential ring in the move's
            // direction, skipping every other member of this scope, and land on the first focusable
            // outside it. The walk ring is bounded by the nearest enclosing CONTAIN scope when one
            // exists — a group inside a modal must wrap within the modal, never escape it.
            var ring = new VisualElementFocusRing(containRoot ?? move.PanelRoot);
            var direction = ToRingDirection(move.Forward);
            // The ring's direction-first element: stepping onto it mid-walk means the walk crossed
            // the ring edge (wrapped) — in a chained host, that crossing is the boundary exit.
            var ringEdge = ring.GetNextFocusable(null, direction);
            var candidate = ring.GetNextFocusable(focused, direction) as VisualElement;
            var wrapped = candidate != null && ReferenceEquals(candidate, ringEdge);
            var guard = MaxRingWalk;
            while (candidate != null && candidate != focused && scopeRoot!.Contains(candidate) && guard-- > 0)
            {
                candidate = ring.GetNextFocusable(candidate, direction) as VisualElement;
                if (candidate != null && ReferenceEquals(candidate, ringEdge))
                {
                    wrapped = true;
                }
            }
            if (candidate != null && candidate != focused && !scopeRoot!.Contains(candidate))
            {
                // A wrap in an unconstrained chained host takes the boundary exit instead of landing
                // back at the ring's start — the pinned iframe contract: Tab past the host ring's
                // end exits to the declaring panel.
                if (wrapped && containRoot == null && TryChainedEscape(in move, requireRingEdge: false))
                {
                    return true;
                }
                Redirect(move.Evt, move.Panel, ResolveScopeEntryTarget(candidate, move.Ctx));
                return true;
            }
            // Every reachable focusable is a member. In an unconstrained chained host the one-stop
            // exit crosses the panel boundary; under containment (or with nowhere to go) the move is
            // suppressed with focus held in place — the engine default would cycle the members,
            // breaking the one-stop contract, and a boundary escape would break the modal.
            if (containRoot == null && TryChainedEscape(in move, requireRingEdge: false))
            {
                return true;
            }
            move.Panel.focusController.IgnoreEvent(move.Evt);
            move.Evt.StopPropagation();
            return true;
        }

        // Mode (b): applies only when focus sits inside a contain scope (and mode (a) declined).
        private static bool TryHandleContainScopeWrap(in NavigationMove move, VisualElement? containRoot)
        {
            if (containRoot == null)
            {
                return false;
            }
            // A ring scoped to the contain root computes the same sequential order the panel ring
            // would, restricted to the scope's members — and wraps at the scope edges, which IS the
            // containment contract. Keyed on the NEAREST contain scope, so focus sitting in a plain
            // scope nested inside a modal still wraps within the modal.
            var scoped = new VisualElementFocusRing(containRoot);
            Redirect(move.Evt, move.Panel,
                scoped.GetNextFocusable(move.Focused, ToRingDirection(move.Forward)) as VisualElement);
            return true;
        }

        // Mode (c): a boundary escape, either a chained host's ring wrap (cross-panel hop) or focus
        // leaving a z-relocated element's real subtree (same-panel placeholder redirect). Reached only
        // when neither mode (a) nor (b) claimed the move.
        private static bool TryHandleBoundaryEscape(in NavigationMove move)
        {
            // Chained host escape: focus sits in a host panel that joined its declaring panel's Tab order,
            // and this move would wrap around the host's own ring edge — cross the boundary instead, to the
            // declaring-panel element right after (forward) / before (backward) the portal's placeholder.
            if (TryChainedEscape(in move, requireRingEdge: true))
            {
                return true;
            }

            // Same-panel counterpart: focus sits inside a z-relocated element's subtree (its real element
            // physically lives in a layer container, elsewhere in the same panel) and this move would leave
            // that subtree — redirect to the panel-ring neighbour of its PLACEHOLDER (its declared position)
            // instead of wherever the layer container happens to sit physically. Reached only when neither
            // Contain nor SingleTabStop claims this focus (both returned above), mirroring the chained escape's
            // own scope precedence.
            return TryZLayerExit(in move);
        }

        // Mode (d): entering a SingleTabStop scope from outside — the group is one tab stop, so a move
        // predicted to land inside it is redirected to the group's roving stop instead. Reached only when
        // no earlier mode claimed the move.
        private static void HandleSingleTabStopGroupEntryPrediction(in NavigationMove move)
        {
            var focused = move.Focused;
            var ctx = move.Ctx;
            // Entering a SingleTabStop scope from outside: the group is one tab stop, so the move lands on
            // the member last used, else the group's first member — from either direction (the WAI-ARIA
            // composite contract; a backward move's raw prediction would be the group's ring-LAST).
            var panelRing = new VisualElementFocusRing(move.PanelRoot);
            var entryPredicted =
                panelRing.GetNextFocusable(focused, ToRingDirection(move.Forward)) as VisualElement;
            if (entryPredicted == null)
            {
                return;
            }
            // Focus sitting in a group never reaches this mode, so a group the prediction lands in is entered
            // from outside. Landing == predicted: the engine's own move already enters at the group's correct
            // stop (a forward move's raw prediction IS the group's ring-first), or lands outside any group.
            var landing = ResolveScopeEntryTarget(entryPredicted, ctx);
            if (!ReferenceEquals(landing, entryPredicted))
            {
                Redirect(move.Evt, move.Panel, landing);
            }
        }

        // A landing inside a SingleTabStop group must enter at the group's roving tab stop — the member
        // last used, else the group's ring-first — no matter how the landing was produced (an engine
        // prediction, an exit-walk redirect, a placeholder forwarding, or a cross-panel escape hop).
        // Direction-agnostic by the WAI-ARIA composite contract: a backward entry lands on the same stop,
        // never the group's last member. Landings outside any SingleTabStop scope pass through untouched.
        private static VisualElement ResolveScopeEntryTarget(VisualElement candidate, ReconcilerContext ctx)
        {
            var root = FindOutermostSingleTabStopRoot(candidate, ctx, out var binding);
            if (root == null)
            {
                return candidate;
            }
            var last = binding!.LastFocusedMember;
            if (last != null && last.panel != null && root.Contains(last) && last.canGrabFocus)
            {
                return last;
            }
            return new VisualElementFocusRing(root)
                .GetNextFocusable(null, VisualElementFocusChangeDirection.right) as VisualElement ?? candidate;
        }

        // The chained-host boundary exit. With requireRingEdge, escapes only when the engine's own move
        // would wrap around the host ring's edge (the normal Tab-past-the-end case); without it, escapes
        // unconditionally in the move's direction (the SingleTabStop-covers-the-whole-host case, where ANY
        // member is the exit point — the caller has already established no contain scope intervenes).
        // Returns true when the move was converted into a cross-panel hop.
        private static bool TryChainedEscape(in NavigationMove move, bool requireRingEdge)
        {
            var evt = move.Evt;
            var panel = move.Panel;
            var focused = move.Focused;
            var ctx = move.Ctx;
            var placeholder = FindChainedPlaceholderForPanel(panel, ctx);
            if (placeholder == null || placeholder.panel == null)
            {
                return false;
            }
            var direction = ToRingDirection(move.Forward);
            if (requireRingEdge)
            {
                var hostRing = new VisualElementFocusRing(move.PanelRoot);
                var predicted = hostRing.GetNextFocusable(focused, direction);
                // GetNextFocusable(null, right) is the ring's first element and (null, left) its last, so a
                // predicted move landing there is exactly the wrap this escape replaces. A single-element
                // host wraps onto itself and still escapes, which is the correct chained behavior.
                var ringEdge = hostRing.GetNextFocusable(null, direction);
                if (predicted == null || !ReferenceEquals(predicted, ringEdge))
                {
                    return false;
                }
            }
            var declaringRoot = placeholder.panel.visualTree;
            if (ResolveEscapeTarget(placeholder, declaringRoot, direction, ctx) == null)
            {
                return false;
            }
            // This panel's own move is suppressed NOW, and ITS focused element is blurred synchronously
            // (its own controller, mid-its-own-dispatch — safe); the cross-panel Focus is deferred to the
            // TARGET panel's next scheduler tick. A synchronous Focus() into another panel from inside this
            // panel's dispatch does not stick (verified empirically), and the blur must come first: two
            // panels holding a focused element simultaneously gets reconciled against the still-focused
            // source panel, unfocusing the target again. The placeholder-entry direction needs no such hop:
            // it runs inside a FOCUS event, whose nested switches ride the engine's pending-focus gate.
            // The hop is scheduled on the DECLARING PANEL'S ROOT (stable, never pooled), and the landing is
            // RE-RESOLVED at fire time from the placeholder's then-current ring position — a landing
            // captured now could be unmounted and pool-recycled into an unrelated same-panel role within
            // the one-tick window, which no identity guard on the captured reference can detect.
            panel.focusController.IgnoreEvent(evt);
            evt.StopPropagation();
            focused.Blur();
            declaringRoot.schedule.Execute(() =>
            {
                if (placeholder.panel == null || placeholder.panel.visualTree != declaringRoot)
                {
                    return;
                }
                var target = ResolveEscapeTarget(placeholder, declaringRoot, direction, ctx);
                if (target != null)
                {
                    ResolveScopeEntryTarget(target, ctx).Focus();
                }
            });
            return true;
        }

        // The declaring-panel element the escape lands on: the next focusable after/before the
        // placeholder, in a ring bounded by the placeholder's own nearest contain scope when one exists —
        // a chained portal declared inside a modal must hand focus back within the modal, never past it.
        private static VisualElement? ResolveEscapeTarget(
            VisualElement placeholder, VisualElement declaringRoot, FocusChangeDirection direction, ReconcilerContext ctx)
        {
            var placeholderContain = FindEnclosingContainScopeRoot(placeholder, ctx, out _);
            var ring = new VisualElementFocusRing(placeholderContain ?? declaringRoot);
            var target = ring.GetNextFocusable(placeholder, direction) as VisualElement;
            return target == null || ReferenceEquals(target, placeholder) || !target.canGrabFocus ? null : target;
        }

        // Resolves the chained placeholder owning `panel`'s ring-edge escape. The registry is keyed by the
        // host's document root element; the lookup matches by panel identity so it is independent of which
        // element the move listener happened to be registered on (the canonical visualTree).
        private static VisualElement? FindChainedPlaceholderForPanel(IPanel panel, ReconcilerContext ctx)
        {
            foreach (var (hostRoot, placeholder) in ctx.ChainedHostRoots)
            {
                if (hostRoot.panel == panel)
                {
                    return placeholder;
                }
            }
            return null;
        }

        // Same-panel counterpart to TryChainedEscape. `focused` sits inside a z-relocated real element's
        // subtree, so the engine's own ring prediction reflects where that element physically paints (its
        // layer container), not its declared position. A prediction that STAYS inside the real element's own
        // subtree is left alone (moving between two focusables both inside it never crosses its boundary);
        // only a prediction that would LEAVE it gets redirected, to the panel ring's neighbour of the
        // element's PLACEHOLDER — its logical position — instead. No panel hop (same panel throughout), so
        // this redirects synchronously, unlike the cross-panel chained escape.
        private static bool TryZLayerExit(in NavigationMove move)
        {
            var evt = move.Evt;
            var panel = move.Panel;
            var panelRoot = move.PanelRoot;
            var focused = move.Focused;
            var forward = move.Forward;
            var ctx = move.Ctx;
            var real = FindEnclosingZLayerReal(focused, ctx, out var placeholder, out var container);
            if (real == null)
            {
                return false;
            }

            var direction = ToRingDirection(forward);
            var ring = new VisualElementFocusRing(panelRoot);
            var predicted = ring.GetNextFocusable(focused, direction) as VisualElement;
            if (predicted != null && real.Contains(predicted))
            {
                return false;
            }

            // The placeholder's own physical ring-neighbour can BE the layer container itself (or a landing
            // inside it, on ANY co-tenant's real — not only this element's own): a trailing front container
            // sits right after its stacking parent's last ordinary slot, so when the z-managed element is
            // that parent's logically LAST child (forward) — or, symmetrically, a leading back container's
            // FIRST child (backward) — the very next ring step from the placeholder re-enters the container
            // instead of escaping past it. With 2+ z-managed siblings sharing that container, this can land on
            // a DIFFERENT member's real (physically adjacent within the same container) rather than this
            // element's own, which the OLD "inside real's own subtree" test would wrongly accept as an escape
            // — so the walk is scoped to the whole CONTAINER (any co-tenant's real), not just `real`. A
            // landing on a NEIGHBOUR's PLACEHOLDER is unaffected: a placeholder lives in the stacking parent's
            // own slot range, never as a child of the container (see InsertSorted / Place), so it is never
            // "inside container" and the walk correctly stops there — the legitimate adjacent-sibling handoff
            // (OnFocusIn forwards from a placeholder to its own real) that the existing backward test relies
            // on. Keep walking past any container landing until it is genuinely outside the container
            // entirely, bounded exactly like the SingleTabStop exit walk above so a pathological ring cannot
            // spin this forever; a ring with nothing else reachable (every walk stays inside the container, or
            // wraps back to the placeholder itself) falls through to "no valid exit".
            var landing = ring.GetNextFocusable(placeholder, direction) as VisualElement;
            var guard = MaxRingWalk;
            while (landing != null && (ReferenceEquals(landing, container) || container.Contains(landing)) && guard-- > 0)
            {
                landing = ring.GetNextFocusable(landing, direction) as VisualElement;
            }

            if (landing == null || ReferenceEquals(landing, placeholder) || !landing.canGrabFocus
                || ReferenceEquals(landing, container) || container.Contains(landing))
            {
                return false;
            }
            Redirect(evt, panel, ResolveScopeEntryTarget(landing, ctx));
            return true;
        }

        // Walks UP from `element` (inclusive) for the nearest ancestor currently registered as a z-managed
        // real element (ReconcilerContext.ZLayerMembers), returning it, its placeholder, and the layer
        // container it currently lives in. Physical containment, mirroring FindNearestScopeRootWhere's own walk
        // — robust across pool reuse and independent of any logical-tree bookkeeping.
        private static VisualElement? FindEnclosingZLayerReal(
            VisualElement element, ReconcilerContext ctx, out VisualElement? placeholder, out VisualElement? container)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (ctx.ZLayerMembers.TryGetValue(current, out var member))
                {
                    placeholder = member.Placeholder;
                    container = member.Container;
                    return current;
                }
            }
            placeholder = null;
            container = null;
            return null;
        }

        private static void ForwardChainedPlaceholder(
            FocusInEvent evt, VisualElement target, PanelHostRecord hostRecord, ReconcilerContext ctx)
        {
            if (TrySnapBackToContainScope(target, evt.relatedTarget as VisualElement, ctx))
            {
                return;
            }
            var hostRoot = hostRecord.Document != null ? hostRecord.Document.rootVisualElement : null;
            if (hostRoot == null)
            {
                return;
            }
            var backward = evt.direction == VisualElementFocusChangeDirection.left;
            var hostRing = new VisualElementFocusRing(hostRoot.panel?.visualTree ?? hostRoot);
            var entry = hostRing.GetNextFocusable(null,
                backward ? VisualElementFocusChangeDirection.left : VisualElementFocusChangeDirection.right) as VisualElement;
            if (entry != null)
            {
                ResolveScopeEntryTarget(entry, ctx).Focus();
            }
        }

        private static void ForwardZLayerPlaceholder(
            FocusInEvent evt, VisualElement target, VisualElement real, ReconcilerContext ctx)
        {
            if (TrySnapBackToContainScope(target, evt.relatedTarget as VisualElement, ctx))
            {
                return;
            }
            if (real.canGrabFocus)
            {
                ResolveScopeEntryTarget(real, ctx).Focus();
                return;
            }
            var backward = evt.direction == VisualElementFocusChangeDirection.left;
            var entry = new VisualElementFocusRing(real).GetNextFocusable(null,
                backward ? VisualElementFocusChangeDirection.left : VisualElementFocusChangeDirection.right) as VisualElement;
            if (entry != null)
            {
                ResolveScopeEntryTarget(entry, ctx).Focus();
            }
        }

        private static void OnFocusIn(FocusInEvent evt, ReconcilerContext ctx)
        {
            if (evt.target is not VisualElement target)
            {
                return;
            }

            // Ahead of the placeholder forwarding below, which would carry a landing on a placeholder
            // outside the group on to whatever that placeholder stands for.
            if (IsSpatialMove(evt.direction)
                && TryHoldInSingleTabStopGroup(target, evt.relatedTarget as VisualElement, ctx))
            {
                return;
            }

            // Chained placeholder forwarding: the placeholder is a zero-size proxy tab stop in the declaring
            // panel's ring — focus reaching it means the sequential order crossed the portal's call site, so
            // hand focus into the host panel at the edge matching the travel direction. Containment is
            // checked FIRST: a placeholder outside the scope that held focus is an escape like any other
            // (a spatial move can land here), and forwarding it would cross the panel boundary out of a
            // modal. A placeholder INSIDE the modal forwards normally — the portal is the modal's own
            // content.
            if (ctx.ChainedPlaceholders.TryGetValue(target, out var hostRecord))
            {
                ForwardChainedPlaceholder(evt, target, hostRecord, ctx);
                return;
            }

            // z-layer placeholder forwarding: Tab reached the proxy stand-in at a z-relocated element's
            // logical position — same panel, so no host-panel hop is needed, just the chained case's
            // forwarding shape minus the panel-boundary machinery. Contain snap-back is checked first for the
            // same reason the chained branch above checks it first.
            if (ctx.ZLayerPlaceholders.TryGetValue(target, out var real))
            {
                ForwardZLayerPlaceholder(evt, target, real, ctx);
                return;
            }

            // Contain snap-back: focus left a contained scope through a path the sequential interception
            // cannot see (a spatial 2D move, or a pointer press outside) — pull it back inside within the
            // same event flush. A landing inside a NEWER contain scope stands instead (a stacked dialog must
            // be able to take focus from the modal underneath). A snap-back happens only when the landing's
            // contain scope, if it has one, is the older, so the snap-back's own landing stands and the
            // recursion ends there — no re-entrancy flag needed (one would not work anyway: UI Toolkit
            // QUEUES focus events raised from inside a dispatch, so a nested FocusIn runs only after this
            // handler returns).
            if (TrySnapBackToContainScope(target, evt.relatedTarget as VisualElement, ctx))
            {
                // The landing was reverted: recording it in the landing scope's bookkeeping would corrupt
                // that scope's roving memory with a member the user never actually reached.
                return;
            }

            // A drag session's keyboard anchor is plumbing, not user intent: recording it would corrupt
            // a scope's roving-stop memory and consume its one-shot restore capture.
            if (ctx.ActiveDrag != null && ctx.ActiveDrag.IsAnchorFocus(target))
            {
                return;
            }

            // Every enclosing scope records the landing, not only the innermost: a scope nested inside a group or
            // a modal must not hide the landing from the scope around it.
            for (var scopeRoot = target; scopeRoot != null; scopeRoot = scopeRoot.parent)
            {
                if (ctx.FocusScopeBindings.TryGetValue(scopeRoot, out var binding))
                {
                    binding.LastFocusedMember = target;
                }
            }
        }

        // The shared snap-back: when focus is leaving `relatedTarget`'s nearest contain scope for a
        // `target` logically outside it and outside any newer contain scope (same panel — a cross-panel move
        // is OnFocusOut's), refocus the scope's remembered member (else its ring-first) and report true.
        // The scope must still be REGISTERED:
        // FiberElementCleaner drops a dying scope's registry entry before firing its restore focus, so a
        // teardown's restore is never reverted back into the detaching subtree.
        private static bool TrySnapBackToContainScope(
            VisualElement target, VisualElement? relatedTarget, ReconcilerContext ctx)
        {
            if (relatedTarget == null || relatedTarget.panel != target.panel)
            {
                return false;
            }
            var containRoot = FindLogicalContainScopeRoot(relatedTarget, ctx, out var binding);
            if (containRoot == null || binding == null || IsLogicallyWithin(target, containRoot)
                || LandsInANewerContainScope(target, binding))
            {
                return false;
            }
            // Portal content in another panel than the scope's: handed back on the scope's tick, as OnFocusOut
            // hands back a move across panels.
            if (containRoot.panel != target.panel)
            {
                SchedulePullBack(containRoot, binding, relatedTarget, ctx);
                return true;
            }
            var back = binding.LastFocusedMember;
            if (back == null || back.panel == null || !containRoot.Contains(back) || !back.canGrabFocus)
            {
                back = new VisualElementFocusRing(containRoot)
                    .GetNextFocusable(null, VisualElementFocusChangeDirection.right) as VisualElement;
            }
            if (back == null)
            {
                return false;
            }
            back.Focus();
            ScheduleRevertedLandingSettle(target, ctx);
            return true;
        }

        // Arrow/d-pad moves never leave a SingleTabStop group, the composite-widget contract whose Tab half
        // TryHandleSingleTabStopGroupExit owns. The engine's 2D search is not public, so the move is corrected
        // after it lands rather than predicted: a landing outside the group returns to the member the
        // move started from.
        private static bool TryHoldInSingleTabStopGroup(
            VisualElement target, VisualElement? relatedTarget, ReconcilerContext ctx)
        {
            var groupRoot = FindOutermostSingleTabStopRoot(relatedTarget, ctx, out _);
            if (groupRoot == null || groupRoot.Contains(target))
            {
                return false;
            }
            relatedTarget!.Focus();
            ScheduleRevertedLandingSettle(target, ctx);
            return true;
        }

        // A move on the axis a group's orientation excludes is ignored before the focus controller acts on it,
        // so no member receives focus events for it. The event still propagates, which leaves a slider or a
        // text field inside the group its own use of the arrow.
        private static void IgnoreExcludedAxisMove(NavigationMoveEvent evt, VisualElement panelRoot, ReconcilerContext ctx)
        {
            var controller = panelRoot.panel?.focusController;
            if (controller?.focusedElement is not VisualElement focused)
            {
                return;
            }
            var groupRoot = FindOutermostSingleTabStopRoot(focused, ctx, out var binding);
            if (groupRoot != null && ExcludesAxis(binding!.Settings.Orientation, evt.direction))
            {
                controller.IgnoreEvent(evt);
            }
        }

        private static bool ExcludesAxis(FocusScopeOrientation orientation, NavigationMoveEvent.Direction direction)
            => direction switch
            {
                NavigationMoveEvent.Direction.Left or NavigationMoveEvent.Direction.Right
                    => orientation == FocusScopeOrientation.Vertical,
                NavigationMoveEvent.Direction.Up or NavigationMoveEvent.Direction.Down
                    => orientation == FocusScopeOrientation.Horizontal,
                // MUTANT_SURVIVES(equivalent): OnNavigationMove returns Next and Previous before this call, and a
                // None move travels nowhere either way; FocusScopeOrientationPlaybackTests holds the engine to that.
                _ => false,
            };

        // A spatial move reaches FocusIn under a direction that is neither sequential nor the unspecified
        // one a pointer press and a programmatic Focus() carry. FocusScopeSingleTabStopPlaybackTests holds
        // the engine to that.
        private static bool IsSpatialMove(FocusChangeDirection direction)
            => direction is not VisualElementFocusChangeDirection
                && !ReferenceEquals(direction, FocusChangeDirection.unspecified);

        // A reverted landing's focus events can interleave — under the engine's queued focus dispatch —
        // such that the element never receives a terminating Blur: every focus-derived consumer on it
        // (variant styling, UseFocusRing, user Blur handlers) then believes it is still focused. One
        // tick later, after the focus queue has fully drained, an element that did not end up holding
        // focus is dealt the Blur it is semantically owed as a real dispatched event — every consumer
        // hooked on the element heals uniformly, not just the ones the reconciler knows by table. An
        // element DETACHED at fire time cannot receive events, so its registered variant consumers are
        // settled directly through the shared sweep instead (detached means not focused, and a transient
        // keyed-reorder detach must not carry the residue back in); a hook-local consumer on a detached
        // element is the unmount path, which owns its own correction.
        private static void ScheduleRevertedLandingSettle(VisualElement reverted, ReconcilerContext ctx)
        {
            var root = reverted.panel?.visualTree;
            if (root == null)
            {
                return;
            }
            root.schedule.Execute(() =>
            {
                if (IsFocusedElementWithin(reverted, out _))
                {
                    return;
                }
                if (reverted.panel != null)
                {
                    using var blur = BlurEvent.GetPooled();
                    blur.target = reverted;
                    reverted.SendEvent(blur);
                    return;
                }
                VariantSettleSweep.ForEach(reverted, ctx, static settler => settler.SettleFocusLoss());
            });
        }

        // The escape containment cannot see from FocusIn: focus cleared to NOTHING (a pointer press on
        // empty non-focusable space, or a programmatic Blur) raises no FocusInEvent for the snap-back to
        // ride — only this FocusOut with a null relatedTarget. The re-focus is deferred one scheduler tick
        // and re-validated at fire time, because the identical event also fires when the focused element
        // (or the whole scope) is being torn down mid-flush, and when focus legitimately moves to ANOTHER
        // panel (the engine blurs the old panel to nothing on a panel switch). By the tick: a real
        // teardown has detached the scope root or replaced its binding (skip — the binding identity check
        // covers a pooled root recycled into a NEW scope under the same element key), a same-panel move
        // has repopulated focusedElement (skip — the FocusIn side owns it), and a cross-panel move has left
        // another panel holding focus, in this tree or another. That landing stands when it is logically
        // inside this scope, or inside a newer contain scope as OnFocusIn lets a same-panel one stand; any
        // other is pulled back like focus that went nowhere.
        private static void OnFocusOut(FocusOutEvent evt, ReconcilerContext ctx)
        {
            if (evt.relatedTarget != null || evt.target is not VisualElement leaving)
            {
                return;
            }
            var containRoot = FindLogicalContainScopeRoot(leaving, ctx, out var armedBinding);
            if (containRoot == null)
            {
                return;
            }
            SchedulePullBack(containRoot, armedBinding!, leaving, ctx);
        }

        // Pulls focus that left `leaving` back into the scope on its panel's next tick, unless by then focus has
        // moved within the scope's own panel, or the landing is inside the scope or inside a newer contain scope.
        // Focus that went nowhere is pulled back only from the scope's own elements: React Aria listens for a blur
        // on those alone, so a blur from portal content elsewhere is left.
        private static void SchedulePullBack(
            VisualElement containRoot, FocusScopeBinding armedBinding, VisualElement leaving, ReconcilerContext ctx)
        {
            var fromOwnContent = ReferenceEquals(FindEnclosingContainScopeRoot(leaving, ctx, out _), containRoot);
            var fromScopePanel = leaving.panel == containRoot.panel;
            var root = containRoot.panel?.visualTree;
            if (root == null)
            {
                return;
            }
            root.schedule.Execute(() =>
            {
                if (containRoot.panel == null
                    || !ctx.FocusScopeBindings.TryGetValue(containRoot, out var binding)
                    || !ReferenceEquals(binding, armedBinding)
                    || !binding.Settings.Contain)
                {
                    return;
                }
                // A landing on the scope's panel from that same panel raised a FocusIn with a related target, which
                // the snap-back there owns; from another panel it had none, so it is judged here.
                var held = containRoot.panel.focusController?.focusedElement as VisualElement;
                if (held != null && fromScopePanel)
                {
                    return;
                }
                var elsewhere = held ?? FocusedElementInAnyTree() ?? FocusedElementInAnyDocument();
                if (elsewhere == null
                        ? !fromOwnContent
                        : IsLogicallyWithin(elsewhere, containRoot) || LandsInANewerContainScope(elsewhere, binding))
                {
                    return;
                }
                var back = binding.LastFocusedMember;
                if (back == null || back.panel == null || !containRoot.Contains(back) || !back.canGrabFocus)
                {
                    back = FocusScopeDriver.FindFirstFocusableInSubtree(containRoot);
                }
                if (back == null)
                {
                    return;
                }
                // A panel that gained focus refocuses its last focused element on its next tick, which can run
                // after this one in the same frame and take the landing back for a frame. Blurring the landing
                // first leaves it nothing to refocus; FocusCrossPanelContainmentTests' portal-outside case
                // counts the landings that would otherwise repeat.
                elsewhere?.Blur();
                back.Focus();
            });
        }

        private static bool IsLogicallyWithin(VisualElement element, VisualElement root)
        {
            for (var current = element; current != null; current = LogicalParentOf(current))
            {
                if (ReferenceEquals(current, root))
                {
                    return true;
                }
            }
            return false;
        }

        // Whether a landing's nearest contain scope, in any mounted tree, was created after `scope`.
        private static bool LandsInANewerContainScope(VisualElement landing, FocusScopeBinding scope)
        {
            for (var current = landing; current != null; current = LogicalParentOf(current))
            {
                foreach (var ctx in s_attachedContexts)
                {
                    if (ctx.FocusScopeBindings.TryGetValue(current, out var found) && found.Settings.Contain)
                    {
                        // MUTANT_SURVIVES(equivalent, boundary): equal sequences are `scope` itself, and a
                        // landing whose nearest contain scope that is lies logically within it, which both
                        // callers return on before asking.
                        return found.Sequence > scope.Sequence;
                    }
                }
            }
            return false;
        }

        // The physical parent, except that content an element was relocated out of its declared slot stands
        // at that slot: a z-managed element at its placeholder, and a portal target's child at the placeholder
        // whose slot range holds it.
        internal static VisualElement? LogicalParentOf(VisualElement current)
        {
            foreach (var ctx in s_attachedContexts)
            {
                if (ctx.ZLayerMembers.TryGetValue(current, out var member))
                {
                    return member.Placeholder;
                }
            }
            var parent = current.parent;
            if (parent == null)
            {
                return null;
            }
            foreach (var ctx in s_attachedContexts)
            {
                if (ctx.PortalHoldingRow(current, parent) is { } placeholder)
                {
                    return placeholder;
                }
            }
            return parent;
        }

        // True when any panel this reconciler manages (the main panel, a layer or world-space host, a panel
        // an element-valued portal targets) currently holds a focused element. UI Toolkit focus is per panel,
        // so "this panel's controller reads null" alone cannot distinguish focus-went-nowhere from
        // focus-went-to-another-panel.
        internal static bool AnyManagedPanelHoldsFocus(ReconcilerContext ctx)
            => FocusedElementInManagedPanels(ctx) != null;

        // The element holding focus in this tree's panels, else any other mounted tree's, else a panel no tree
        // manages: UI Toolkit focus is per panel, where React Aria reads the document's one active element.
        internal static VisualElement? FocusedElementAnywhere(ReconcilerContext ctx)
            => FocusedElementInManagedPanels(ctx) ?? FocusedElementInAnyTree() ?? FocusedElementInAnyDocument();

        private static VisualElement? FocusedElementInAnyTree()
        {
            foreach (var ctx in s_attachedContexts)
            {
                if (FocusedElementInManagedPanels(ctx) is { } held)
                {
                    return held;
                }
            }
            return null;
        }

        // The focused element of a UIDocument's panel, managed by a mounted tree or not.
        private static VisualElement? FocusedElementInAnyDocument()
        {
            foreach (var document in UnityEngine.Object.FindObjectsByType<UIDocument>(UnityEngine.FindObjectsSortMode.None))
            {
                if (document.rootVisualElement?.panel?.focusController?.focusedElement is VisualElement held)
                {
                    return held;
                }
            }
            return null;
        }

        private static VisualElement? FocusedElementInManagedPanels(ReconcilerContext ctx)
        {
            if (ctx.MainPanelRoot?.panel?.focusController?.focusedElement is VisualElement main)
            {
                return main;
            }
            // A layer or world-space portal's target is its host's root, so this reads those hosts as well as
            // a panel an element-valued portal targets.
            foreach (var info in ctx.PortalState.Values)
            {
                if (info.Target?.panel?.focusController?.focusedElement is VisualElement held)
                {
                    return held;
                }
            }
            return null;
        }

        // True when `root`'s panel currently has a focused element that is `root` itself or one of its
        // descendants — the shared "does this subtree currently hold focus" check needed by every path
        // that must undo something ONLY while it still holds focus (a focus anchor's release, a reverted
        // landing's settle, UseFocusRing's unmount correction). Returns the held element via `held` (null
        // when the result is false) so a caller that also needs it — e.g. to Blur it — does not re-read
        // focusController a second time.
        internal static bool IsFocusedElementWithin(VisualElement root, out VisualElement? held)
        {
            held = root.panel?.focusController?.focusedElement as VisualElement;
            return held != null && (held == root || root.Contains(held));
        }

        // The nearest scope whose settings CONTAIN — an element inside a plain or SingleTabStop scope nested
        // in a modal still belongs to the modal's containment.
        private static VisualElement? FindEnclosingContainScopeRoot(
            VisualElement? element, ReconcilerContext ctx, out FocusScopeBinding? binding)
            => FindNearestScopeRootWhere(element, ctx, static settings => settings.Contain, out binding);

        // The nearest contain scope through logical parents, so portal content reaches the scope its portal is
        // declared in.
        private static VisualElement? FindLogicalContainScopeRoot(
            VisualElement element, ReconcilerContext ctx, out FocusScopeBinding? binding)
        {
            for (var current = element; current != null; current = LogicalParentOf(current))
            {
                if (!ctx.FocusScopeBindings.TryGetValue(current, out var found))
                {
                    continue;
                }
                if (found.Settings.Contain)
                {
                    binding = found;
                    return current;
                }
            }
            binding = null;
            return null;
        }

        // The outermost group up to the nearest contain scope, that scope included. As in React Aria's useToolbar,
        // only the outermost of nested groups handles keys: Tab leaves all of them, arrows move across the nested
        // ones, and entry returns to the last member focused at any depth. A group around the contain scope is not
        // reached: the containment decides there.
        private static VisualElement? FindOutermostSingleTabStopRoot(
            VisualElement? element, ReconcilerContext ctx, out FocusScopeBinding? binding)
        {
            VisualElement? outermost = null;
            binding = null;
            for (var current = element; current != null; current = current.parent)
            {
                if (!ctx.FocusScopeBindings.TryGetValue(current, out var found))
                {
                    continue;
                }
                if (found.Settings.SingleTabStop)
                {
                    outermost = current;
                    binding = found;
                }
                if (found.Settings.Contain)
                {
                    break;
                }
            }
            return outermost;
        }

        // Physical containment is deliberately the membership definition — robust at event time, across pool
        // reuse, and against the logical-tree caveats that limit userData-based resolution for bare portal
        // children.
        private static VisualElement? FindNearestScopeRootWhere(
            VisualElement? element, ReconcilerContext ctx, Func<FocusScopeSettings, bool> kind,
            out FocusScopeBinding? binding)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (ctx.FocusScopeBindings.TryGetValue(current, out var found) && kind(found.Settings))
                {
                    binding = found;
                    return current;
                }
            }
            binding = null;
            return null;
        }

        private static FocusChangeDirection ToRingDirection(bool forward)
            => forward ? VisualElementFocusChangeDirection.right : VisualElementFocusChangeDirection.left;

        // The pinned suppression contract: IgnoreEvent is what the post-dispatch focus move actually checks;
        // StopPropagation only silences other listeners (and is kept so a bubble-up default action like a
        // text field's own SwitchFocusOnEvent cannot double-move).
        private static void Redirect(NavigationMoveEvent evt, IPanel panel, VisualElement? target)
        {
            panel.focusController.IgnoreEvent(evt);
            evt.StopPropagation();
            target?.Focus();
        }
    }

    // Attach/Update/Detach lifecycle for a focus-scope binding, dispatched through the shared
    // ApplyElementBinding plumbing exactly like SceneView/Particles/Anchored. Attach registers the
    // AutoFocus hook and lazily ensures the navigator on whatever panel the scope lands in; Detach is the
    // pool-reuse scrub (RestoreFocus itself runs in FiberElementCleaner BEFORE the element leaves the tree,
    // since focus dies the moment an element detaches).
    internal static class FocusScopeDriver
    {
        public static FocusScopeBinding Attach(VisualElement element, FocusScopeSettings settings, ReconcilerContext ctx)
        {
            var binding = new FocusScopeBinding(settings) { RestoreTarget = FiberFocusNavigator.FocusedElementAnywhere(ctx) };
            binding.OnAttach = _ =>
            {
                FiberFocusNavigator.EnsureAttached(element, ctx);
                // Mount-once (it acts on mount, never again): the latch is
                // taken on the FIRST attach regardless of the setting's value at that moment, so a
                // post-mount settings flip cannot re-arm it for a later physical re-attach (a keyed
                // reorder moves the subtree via RemoveAt + Insert, whose transient detach also clears
                // panel focus — AutoFocusFirst's focus-already-inside guard cannot cover that by itself).
                if (!binding.AutoFocusFired)
                {
                    binding.AutoFocusFired = true;
                    if (binding.Settings.AutoFocus)
                    {
                        AutoFocusFirst(element);
                    }
                }
            };
            element.RegisterCallback(binding.OnAttach);
            if (element.panel != null)
            {
                binding.OnAttach(null!);
            }
            return binding;
        }

        public static void Update(VisualElement element, FocusScopeBinding binding, FocusScopeSettings settings)
        {
            binding.Settings = settings;
        }

        public static void Detach(VisualElement element, FocusScopeBinding binding)
        {
            if (binding.OnAttach != null)
            {
                element.UnregisterCallback(binding.OnAttach);
                binding.OnAttach = null;
            }
        }

        // Focuses the scope's first focusable descendant (document order), skipped when focus already sits
        // inside the scope. Deliberately NOT computed through a focus ring: ring membership requires the
        // element chain to be resolved as displayed, which has not happened yet at attach time (the panel's
        // style pass runs after this frame's mounts) — the engine ring would read empty here. A plain
        // traversal over the same focusability predicate (minus the displayed check) is what attach-time
        // can actually answer.
        private static void AutoFocusFirst(VisualElement scopeRoot)
        {
            var controller = scopeRoot.panel?.focusController;
            if (controller?.focusedElement is VisualElement current && scopeRoot.Contains(current))
            {
                return;
            }
            FindFirstFocusableInSubtree(scopeRoot)?.Focus();
        }

        internal static VisualElement? FindFirstFocusableInSubtree(VisualElement root)
        {
            var count = root.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = root.hierarchy[i];
                if (child.canGrabFocus && child.tabIndex >= 0 && !child.delegatesFocus)
                {
                    return child;
                }
                var nested = FindFirstFocusableInSubtree(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }
    }

    // A deferred navigator attach on an element with no panel yet. Pinned once an EnsureAttached caller asks
    // for it, which no release undoes; a HoldAttached release removes an unpinned one.
    internal sealed class NavigatorPendingAttach
    {
        internal NavigatorPendingAttach(VisualElement element, EventCallback<AttachToPanelEvent> hook)
        {
            Element = element;
            Hook = hook;
        }

        internal VisualElement Element { get; }

        internal EventCallback<AttachToPanelEvent> Hook { get; }

        internal bool Pinned { get; set; }
    }
}
