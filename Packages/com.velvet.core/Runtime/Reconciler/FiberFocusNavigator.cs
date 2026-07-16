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
    // events: the member that last held focus (SingleTabStop re-entry / Contain snap-back target), and the
    // element focus came FROM when it first entered the scope (RestoreFocus's target on unmount).
    internal sealed class FocusScopeBinding
    {
        public FocusScopeSettings Settings;
        public VisualElement? LastFocusedMember;
        public VisualElement? RestoreTarget;
        public bool RestoreCaptured;
        // AutoFocus is mount-once, matching React: the latch is set on the scope's FIRST attach no matter
        // what the setting held then, so neither a keyed reorder's re-attach nor a post-mount settings
        // flip can ever fire it again.
        public bool AutoFocusFired;
        public EventCallback<AttachToPanelEvent>? OnAttach;

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
    /// TrickleDown listener deterministically preempts it. Spatial moves (Left/Right/Up/Down) are NEVER
    /// intercepted: 2D arrow/dpad/stick navigation is the engine's own <c>GetNextFocusable2D</c>, which this
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
            if (element == null)
            {
                return;
            }
            var root = element.panel?.visualTree;
            if (root != null)
            {
                AttachToRoot(root, ctx);
                return;
            }
            foreach (var (pendingElement, _) in ctx.NavigatorPendingAttachHooks)
            {
                if (ReferenceEquals(pendingElement, element))
                {
                    return;
                }
            }
            EventCallback<AttachToPanelEvent> hook = null!;
            hook = _ =>
            {
                element.UnregisterCallback(hook);
                ctx.NavigatorPendingAttachHooks.Remove((element, hook));
                AttachToRoot(element.panel?.visualTree, ctx);
            };
            element.RegisterCallback(hook);
            ctx.NavigatorPendingAttachHooks.Add((element, hook));
        }

        // Unregisters and forgets any deferred attach hook still pending on `element`. Part of the
        // element-teardown scrub: a pooled element must not carry a live hook into its next role (it
        // would attach the navigator to whatever panel the recycled element lands in).
        internal static void ReleasePendingAttachHooks(VisualElement element, ReconcilerContext ctx)
        {
            for (var i = ctx.NavigatorPendingAttachHooks.Count - 1; i >= 0; i--)
            {
                var (pendingElement, hook) = ctx.NavigatorPendingAttachHooks[i];
                if (ReferenceEquals(pendingElement, element))
                {
                    element.UnregisterCallback(hook);
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
            EventCallback<NavigationMoveEvent> onMove = evt => OnNavigationMove(evt, root, ctx);
            EventCallback<FocusInEvent> onFocusIn = evt => OnFocusIn(evt, ctx);
            EventCallback<FocusOutEvent> onFocusOut = evt => OnFocusOut(evt, ctx);
            root.RegisterCallback(onMove, TrickleDown.TrickleDown);
            root.RegisterCallback(onFocusIn);
            root.RegisterCallback(onFocusOut);
            ctx.NavigatorAttachments[root] = (onMove, onFocusIn, onFocusOut);
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
            foreach (var (element, hook) in ctx.NavigatorPendingAttachHooks)
            {
                element.UnregisterCallback(hook);
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

        private static void OnNavigationMove(NavigationMoveEvent evt, VisualElement panelRoot, ReconcilerContext ctx)
        {
            // Spatial moves always fall through to the engine's own 2D navigation.
            var forward = evt.direction == NavigationMoveEvent.Direction.Next;
            if (!forward && evt.direction != NavigationMoveEvent.Direction.Previous)
            {
                return;
            }
            var panel = panelRoot.panel;
            var focused = panel?.focusController?.focusedElement as VisualElement;
            if (focused == null)
            {
                return;
            }

            // Containment resolves through the NEAREST contain scope; the innermost scope of any kind
            // decides SingleTabStop behavior. A scope that is both contain and singleTabStop behaves as
            // contain (the pre-existing precedence).
            var containRoot = FindEnclosingContainScopeRoot(focused, ctx, out _);
            var scopeRoot = FindEnclosingScopeRoot(focused, ctx, out var binding);
            var singleTabStop = scopeRoot != null && binding is { Settings.SingleTabStop: true }
                && !ReferenceEquals(scopeRoot, containRoot);

            if (singleTabStop)
            {
                // The whole subtree acts as ONE tab stop: walk the sequential ring in the move's
                // direction, skipping every other member of this scope, and land on the first focusable
                // outside it. The walk ring is bounded by the nearest enclosing CONTAIN scope when one
                // exists — a group inside a modal must wrap within the modal, never escape it.
                var ring = new VisualElementFocusRing(containRoot ?? panelRoot);
                var direction = ToRingDirection(forward);
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
                    if (wrapped && containRoot == null
                        && TryChainedEscape(evt, panel!, panelRoot, focused, forward, ctx, requireRingEdge: false))
                    {
                        return;
                    }
                    Redirect(evt, panel!, ResolveScopeEntryTarget(candidate, ctx));
                    return;
                }
                // Every reachable focusable is a member. In an unconstrained chained host the one-stop
                // exit crosses the panel boundary; under containment (or with nowhere to go) the move is
                // suppressed with focus held in place — the engine default would cycle the members,
                // breaking the one-stop contract, and a boundary escape would break the modal.
                if (containRoot == null
                    && TryChainedEscape(evt, panel!, panelRoot, focused, forward, ctx, requireRingEdge: false))
                {
                    return;
                }
                panel!.focusController.IgnoreEvent(evt);
                evt.StopPropagation();
                return;
            }

            if (containRoot != null)
            {
                // A ring scoped to the contain root computes the same sequential order the panel ring
                // would, restricted to the scope's members — and wraps at the scope edges, which IS the
                // containment contract. Keyed on the NEAREST contain scope, so focus sitting in a plain
                // scope nested inside a modal still wraps within the modal.
                var scoped = new VisualElementFocusRing(containRoot);
                Redirect(evt, panel!, scoped.GetNextFocusable(focused, ToRingDirection(forward)) as VisualElement);
                return;
            }

            // Chained host escape: focus sits in a host panel that joined its declaring panel's Tab order,
            // and this move would wrap around the host's own ring edge — cross the boundary instead, to the
            // declaring-panel element right after (forward) / before (backward) the portal's placeholder.
            if (TryChainedEscape(evt, panel!, panelRoot, focused, forward, ctx, requireRingEdge: true))
            {
                return;
            }

            // Entering a SingleTabStop scope from outside: the group is one tab stop, so the move lands on
            // the member last used, else the group's first member — from either direction (the WAI-ARIA
            // composite contract; a backward move's raw prediction would be the group's ring-LAST).
            var panelRing = new VisualElementFocusRing(panelRoot);
            var entryPredicted = panelRing.GetNextFocusable(focused, ToRingDirection(forward)) as VisualElement;
            if (entryPredicted != null)
            {
                var enteredRoot = FindEnclosingScopeRoot(entryPredicted, ctx, out var enteredBinding);
                if (enteredRoot != null && enteredBinding is { Settings.SingleTabStop: true }
                    && !enteredRoot.Contains(focused))
                {
                    var landing = ResolveScopeEntryTarget(entryPredicted, ctx);
                    if (!ReferenceEquals(landing, entryPredicted))
                    {
                        Redirect(evt, panel!, landing);
                    }
                    // Landing == predicted: the engine's own move already enters at the group's correct
                    // stop (a forward move's raw prediction IS the group's ring-first).
                }
            }
        }

        // A landing inside a SingleTabStop group must enter at the group's roving tab stop — the member
        // last used, else the group's ring-first — no matter how the landing was produced (an engine
        // prediction, an exit-walk redirect, a placeholder forwarding, or a cross-panel escape hop).
        // Direction-agnostic by the WAI-ARIA composite contract: a backward entry lands on the same stop,
        // never the group's last member. Landings outside any SingleTabStop scope pass through untouched.
        private static VisualElement ResolveScopeEntryTarget(VisualElement candidate, ReconcilerContext ctx)
        {
            var root = FindEnclosingScopeRoot(candidate, ctx, out var binding);
            if (root == null || binding is not { Settings.SingleTabStop: true })
            {
                return candidate;
            }
            var last = binding.LastFocusedMember;
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
        private static bool TryChainedEscape(
            NavigationMoveEvent evt, IPanel panel, VisualElement panelRoot, VisualElement focused,
            bool forward, ReconcilerContext ctx, bool requireRingEdge)
        {
            var placeholder = FindChainedPlaceholderForPanel(panel, ctx);
            if (placeholder == null || placeholder.panel == null)
            {
                return false;
            }
            var direction = ToRingDirection(forward);
            if (requireRingEdge)
            {
                var hostRing = new VisualElementFocusRing(panelRoot);
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

        private static void OnFocusIn(FocusInEvent evt, ReconcilerContext ctx)
        {
            if (evt.target is not VisualElement target)
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
                if (TrySnapBackToContainScope(target, evt.relatedTarget as VisualElement, ctx))
                {
                    return;
                }
                var hostRoot = hostRecord.Document != null ? hostRecord.Document.rootVisualElement : null;
                if (hostRoot != null)
                {
                    var backward = evt.direction == VisualElementFocusChangeDirection.left;
                    var hostRing = new VisualElementFocusRing(hostRoot.panel?.visualTree ?? hostRoot);
                    var entry = hostRing.GetNextFocusable(null,
                        backward ? VisualElementFocusChangeDirection.left : VisualElementFocusChangeDirection.right) as VisualElement;
                    if (entry != null)
                    {
                        ResolveScopeEntryTarget(entry, ctx).Focus();
                    }
                }
                return;
            }

            // Contain snap-back: focus left a contained scope through a path the sequential interception
            // cannot see (a spatial 2D move, or a pointer press outside) — pull it back inside within the
            // same event flush. A landing INSIDE any contain scope stands instead: the scope receiving
            // focus claims it (React Aria's newest-scope activation — a stacked dialog must be able to
            // take focus from the modal underneath), and because a snap-back's own landing is by
            // construction inside a contain scope, the recursion is structurally terminal — no re-entrancy
            // flag needed (one would not work anyway: UI Toolkit QUEUES focus events raised from inside a
            // dispatch, so a nested FocusIn runs only after this handler returns).
            if (FindEnclosingContainScopeRoot(target, ctx, out _) == null
                && TrySnapBackToContainScope(target, evt.relatedTarget as VisualElement, ctx))
            {
                // The landing was reverted: recording it in the landing scope's bookkeeping would corrupt
                // that scope's roving memory with a member the user never actually reached.
                return;
            }

            var scopeRoot = FindEnclosingScopeRoot(target, ctx, out var binding);
            if (scopeRoot == null || binding == null)
            {
                return;
            }
            binding.LastFocusedMember = target;
            // First entry from outside (or from nothing): remember where focus came from, so RestoreFocus
            // can return it there when the scope unmounts while holding focus.
            if (!binding.RestoreCaptured)
            {
                var cameFrom = evt.relatedTarget as VisualElement;
                if (cameFrom == null || !scopeRoot.Contains(cameFrom))
                {
                    binding.RestoreTarget = cameFrom;
                    binding.RestoreCaptured = true;
                }
            }
        }

        // The shared snap-back: when focus is leaving `relatedTarget`'s nearest contain scope for a
        // `target` outside it (same panel — cross-panel moves are never snapped), refocus the scope's
        // remembered member (else its ring-first) and report true. The scope must still be REGISTERED:
        // FiberElementCleaner drops a dying scope's registry entry before firing its restore focus, so a
        // teardown's restore is never reverted back into the detaching subtree.
        private static bool TrySnapBackToContainScope(
            VisualElement target, VisualElement? relatedTarget, ReconcilerContext ctx)
        {
            if (relatedTarget == null || relatedTarget.panel != target.panel)
            {
                return false;
            }
            var containRoot = FindEnclosingContainScopeRoot(relatedTarget, ctx, out var binding);
            if (containRoot == null || binding == null || containRoot.Contains(target))
            {
                return false;
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

        // A reverted landing's focus events can interleave — under the engine's queued focus dispatch —
        // such that the element never receives a terminating Blur: its focus / focus-visible variant
        // styling then sticks lit on an element that is not focused (observed against real editor
        // input). One tick later, after the focus queue has fully drained, an element that did not end
        // up holding focus has its focus-derived variant state settled explicitly; an element that DID
        // end up focused is left alone (its state is live and correct), and the settle is idempotent for
        // an element whose Blur arrived normally. The manipulators are resolved at SCHEDULE time: a
        // snap-back fires per pointer press outside a modal, so an unstyled landing (the common case,
        // and always the chained proxy placeholder) must not pay for a closure, a scheduled item, and a
        // full stacked-variant scan every press. A manipulator torn down inside the one-tick window
        // settles as a no-op (its unregister already cleared the state its settle would clear); one
        // attached inside the window has no stale focus state to clear. An element DETACHED at fire time
        // is settled too — detached means not focused, and a transiently-detached element (a keyed
        // reorder) would otherwise carry the residue back in with it.
        private static void ScheduleRevertedLandingSettle(VisualElement reverted, ReconcilerContext ctx)
        {
            var root = reverted.panel?.visualTree;
            if (root == null)
            {
                return;
            }
            ctx.GestureManipulators.TryGetValue(reverted, out var gesture);
            ctx.VariantManipulators.TryGetValue(reverted, out var variant);
            List<StyleStackedVariantManipulator>? stacked = null;
            if (ctx.StackedVariantManipulators.Count > 0)
            {
                foreach (var kv in ctx.StackedVariantManipulators)
                {
                    if (kv.Key.target == reverted)
                    {
                        (stacked ??= new List<StyleStackedVariantManipulator>()).Add(kv.Value);
                    }
                }
            }
            if (gesture == null && variant == null && stacked == null)
            {
                return;
            }
            root.schedule.Execute(() =>
            {
                if (reverted.panel?.focusController?.focusedElement is VisualElement held
                    && (held == reverted || reverted.Contains(held)))
                {
                    return;
                }
                gesture?.SettleFocusLoss();
                variant?.SettleFocusLoss();
                if (stacked != null)
                {
                    foreach (var manipulator in stacked)
                    {
                        manipulator.SettleFocusLoss();
                    }
                }
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
        // has repopulated focusedElement (skip — the FocusIn side owns it), and a cross-panel move left
        // some other managed panel holding focus (skip — containment is per panel and must not fight it).
        // Only the true "focus went nowhere" case remains, and containment pulls it back inside.
        private static void OnFocusOut(FocusOutEvent evt, ReconcilerContext ctx)
        {
            if (evt.relatedTarget != null || evt.target is not VisualElement leaving)
            {
                return;
            }
            var containRoot = FindEnclosingContainScopeRoot(leaving, ctx, out var armedBinding);
            if (containRoot == null || armedBinding == null)
            {
                return;
            }
            var root = leaving.panel?.visualTree;
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
                if (AnyManagedPanelHoldsFocus(ctx))
                {
                    return;
                }
                var back = binding.LastFocusedMember;
                if (back == null || back.panel == null || !containRoot.Contains(back) || !back.canGrabFocus)
                {
                    back = FocusScopeDriver.FindFirstFocusableInSubtree(containRoot);
                }
                back?.Focus();
            });
        }

        // True when any panel this reconciler manages (the main panel, a layer host, a world-space host)
        // currently holds a focused element. UI Toolkit focus is per panel, so "this panel's controller
        // reads null" alone cannot distinguish focus-went-nowhere from focus-went-to-another-panel.
        private static bool AnyManagedPanelHoldsFocus(ReconcilerContext ctx)
        {
            if (ctx.MainPanelRoot?.panel?.focusController?.focusedElement != null)
            {
                return true;
            }
            foreach (var host in ctx.LayerHosts.Values)
            {
                if (host.Document != null
                    && host.Document.rootVisualElement?.panel?.focusController?.focusedElement != null)
                {
                    return true;
                }
            }
            foreach (var record in ctx.WorldSpaceBindings.Values)
            {
                if (record.Document != null
                    && record.Document.rootVisualElement?.panel?.focusController?.focusedElement != null)
                {
                    return true;
                }
            }
            return false;
        }

        // Walks the parent chain from `element` (inclusive) to the first registered scope root. Physical
        // containment is deliberately the membership definition — robust at event time, across pool reuse,
        // and against the logical-tree caveats that limit userData-based resolution for bare portal children.
        private static VisualElement? FindEnclosingScopeRoot(
            VisualElement element, ReconcilerContext ctx, out FocusScopeBinding? binding)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (ctx.FocusScopeBindings.TryGetValue(current, out var found))
                {
                    binding = found;
                    return current;
                }
            }
            binding = null;
            return null;
        }

        // Same walk, but resolving the nearest scope whose settings actually CONTAIN — an element inside a
        // plain or SingleTabStop scope nested in a modal still belongs to the modal's containment.
        private static VisualElement? FindEnclosingContainScopeRoot(
            VisualElement? element, ReconcilerContext ctx, out FocusScopeBinding? binding)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (ctx.FocusScopeBindings.TryGetValue(current, out var found) && found.Settings.Contain)
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
            var binding = new FocusScopeBinding(settings);
            binding.OnAttach = _ =>
            {
                FiberFocusNavigator.EnsureAttached(element, ctx);
                // Mount-once, matching React's autoFocus (it acts on mount, never again): the latch is
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
}
