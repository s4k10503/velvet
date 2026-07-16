#nullable enable
using System;
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
        public EventCallback<AttachToPanelEvent>? OnAttach;

        public FocusScopeBinding(FocusScopeSettings settings)
        {
            Settings = settings;
        }
    }

    /// <summary>
    /// The single owner of every focus-navigation interception in Velvet — the keyboard sibling of
    /// <c>FiberCrossPanelInput</c>'s two classes, attached exactly once per panel root (never per scope).
    /// Sequential (Next/Previous) <see cref="NavigationMoveEvent"/>s are intercepted on the verified engine
    /// contract pinned by <c>FocusNavigationInterceptionTests</c>: the default focus move runs post-dispatch,
    /// after every listener, and its only suppression is <see cref="FocusController.IgnoreEvent"/> — so a
    /// TrickleDown listener deterministically preempts it. Spatial moves (Left/Right/Up/Down) are NEVER
    /// intercepted: 2D arrow/dpad/stick navigation is the engine's own <c>GetNextFocusable2D</c>, which this
    /// layer composes with rather than reimplements. Sequential prediction and redirection use the public
    /// <see cref="VisualElementFocusRing"/> — the exact class the runtime ring delegates Next/Previous to
    /// (<c>NavigateFocusRing.m_Ring</c>), so predictions cannot drift from what the engine would have done.
    /// </summary>
    internal static class FiberFocusNavigator
    {
        // Bounds the SingleTabStop exit walk (skipping members while searching for the first focusable
        // outside the scope) so a pathological ring cannot spin this listener unboundedly.
        private const int MaxRingWalk = 4096;

        // Attaches the navigator's two listeners to a panel root exactly once (tracked in
        // ctx.NavigatorAttachments so Reconciler.Dispose can unregister them). Called from V.Mount (main
        // panel), PanelHostFactory (host panels), and lazily when a scope/chained placeholder attaches to a
        // panel this navigator has not seen yet.
        internal static void EnsureAttached(VisualElement? panelRoot, ReconcilerContext ctx)
        {
            if (panelRoot == null || ctx.NavigatorAttachments.ContainsKey(panelRoot))
            {
                return;
            }
            EventCallback<NavigationMoveEvent> onMove = evt => OnNavigationMove(evt, panelRoot, ctx);
            EventCallback<FocusInEvent> onFocusIn = evt => OnFocusIn(evt, ctx);
            panelRoot.RegisterCallback(onMove, TrickleDown.TrickleDown);
            panelRoot.RegisterCallback(onFocusIn);
            ctx.NavigatorAttachments[panelRoot] = (onMove, onFocusIn);
        }

        internal static void DetachAll(ReconcilerContext ctx)
        {
            foreach (var (root, (onMove, onFocusIn)) in ctx.NavigatorAttachments)
            {
                root.UnregisterCallback(onMove, TrickleDown.TrickleDown);
                root.UnregisterCallback(onFocusIn);
            }
            ctx.NavigatorAttachments.Clear();
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
                // ring-edge escape (flow between two portals' slots stays panel-internal either way).
                ctx.ChainedHostRoots.TryAdd(hostRoot, placeholder);
                EnsureAttached(placeholder.panel?.visualTree, ctx);
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
                if (hostRoot != null && ctx.ChainedHostRoots.TryGetValue(hostRoot, out var owner)
                    && ReferenceEquals(owner, placeholder))
                {
                    ctx.ChainedHostRoots.Remove(hostRoot);
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

            // Innermost scope wins (nested modals resolve naturally: the walk starts at the focused element
            // and the first registered scope root on the parent chain is the closest enclosing one).
            var scopeRoot = FindEnclosingScopeRoot(focused, ctx, out var binding);
            if (scopeRoot != null && binding != null)
            {
                if (binding.Settings.Contain)
                {
                    // A ring scoped to the subtree root computes the same sequential order the panel ring
                    // would, restricted to the scope's members — and wraps at the scope edges, which IS the
                    // containment contract.
                    var scoped = new VisualElementFocusRing(scopeRoot);
                    Redirect(evt, panel!, scoped.GetNextFocusable(focused, ToRingDirection(forward)) as VisualElement);
                    return;
                }
                if (binding.Settings.SingleTabStop)
                {
                    // The whole subtree acts as ONE tab stop: walk the panel ring in the move's direction,
                    // skipping every other member of this scope, and land on the first focusable outside it.
                    var ring = new VisualElementFocusRing(panelRoot);
                    var direction = ToRingDirection(forward);
                    var candidate = ring.GetNextFocusable(focused, direction) as VisualElement;
                    var guard = MaxRingWalk;
                    while (candidate != null && candidate != focused && scopeRoot.Contains(candidate) && guard-- > 0)
                    {
                        candidate = ring.GetNextFocusable(candidate, direction) as VisualElement;
                    }
                    if (candidate != null && candidate != focused && !scopeRoot.Contains(candidate))
                    {
                        Redirect(evt, panel!, candidate);
                    }
                    return;
                }
            }

            // Chained host escape: focus sits in a host panel that joined its declaring panel's Tab order,
            // and this move would wrap around the host's own ring edge — cross the boundary instead, to the
            // declaring-panel element right after (forward) / before (backward) the portal's placeholder.
            if (ctx.ChainedHostRoots.TryGetValue(panelRoot, out var placeholder) && placeholder.panel != null)
            {
                var hostRing = new VisualElementFocusRing(panelRoot);
                var direction = ToRingDirection(forward);
                var predicted = hostRing.GetNextFocusable(focused, direction);
                // GetNextFocusable(null, right) is the ring's first element and (null, left) its last, so a
                // predicted move landing there is exactly the wrap this escape replaces. A single-element
                // host wraps onto itself and still escapes, which is the correct chained behavior.
                var ringEdge = hostRing.GetNextFocusable(null, direction);
                if (predicted != null && ReferenceEquals(predicted, ringEdge))
                {
                    var declaringRing = new VisualElementFocusRing(placeholder.panel.visualTree);
                    var escapeTarget = declaringRing.GetNextFocusable(placeholder, direction) as VisualElement;
                    if (escapeTarget != null && !ReferenceEquals(escapeTarget, placeholder))
                    {
                        // This panel's own move is suppressed NOW, and ITS focused element is blurred
                        // synchronously (its own controller, mid-its-own-dispatch — safe); the cross-panel
                        // Focus is deferred to the TARGET panel's next scheduler tick. A synchronous Focus()
                        // into another panel from inside this panel's dispatch does not stick (verified
                        // empirically), and the blur must come first: two panels holding a focused element
                        // simultaneously gets reconciled against the still-focused source panel, unfocusing
                        // the target again. The placeholder-entry direction needs no such hop: it runs
                        // inside a FOCUS event, whose nested switches ride the engine's pending-focus gate.
                        panel!.focusController.IgnoreEvent(evt);
                        evt.StopPropagation();
                        focused.Blur();
                        escapeTarget.schedule.Execute(() => escapeTarget.Focus());
                    }
                    return;
                }
            }

            // Entering a SingleTabStop scope from outside: the group is one tab stop, so the move lands on
            // the member last used (else the predicted entry element, which for a forward move is the
            // scope's ring-first anyway).
            var panelRing = new VisualElementFocusRing(panelRoot);
            var entryPredicted = panelRing.GetNextFocusable(focused, ToRingDirection(forward)) as VisualElement;
            if (entryPredicted != null)
            {
                var enteredRoot = FindEnclosingScopeRoot(entryPredicted, ctx, out var enteredBinding);
                if (enteredRoot != null && enteredBinding is { Settings.SingleTabStop: true }
                    && !enteredRoot.Contains(focused))
                {
                    var last = enteredBinding.LastFocusedMember;
                    if (last != null && last.panel != null && enteredRoot.Contains(last) && last.canGrabFocus)
                    {
                        Redirect(evt, panel!, last);
                    }
                    // No remembered member: the predicted element is already the correct entry point; let
                    // the engine's own move proceed untouched.
                }
            }
        }

        private static void OnFocusIn(FocusInEvent evt, ReconcilerContext ctx)
        {
            if (evt.target is not VisualElement target)
            {
                return;
            }

            // Chained placeholder forwarding: the placeholder is a zero-size proxy tab stop in the declaring
            // panel's ring — focus reaching it means the sequential order crossed the portal's call site, so
            // hand focus into the host panel at the edge matching the travel direction.
            if (ctx.ChainedPlaceholders.TryGetValue(target, out var hostRecord))
            {
                var hostRoot = hostRecord.Document != null ? hostRecord.Document.rootVisualElement : null;
                if (hostRoot != null)
                {
                    var backward = evt.direction == VisualElementFocusChangeDirection.left;
                    var hostRing = new VisualElementFocusRing(hostRoot);
                    var entry = hostRing.GetNextFocusable(null,
                        backward ? VisualElementFocusChangeDirection.left : VisualElementFocusChangeDirection.right) as VisualElement;
                    entry?.Focus();
                }
                return;
            }

            var scopeRoot = FindEnclosingScopeRoot(target, ctx, out var binding);
            if (scopeRoot != null && binding != null)
            {
                binding.LastFocusedMember = target;
                // First entry from outside (or from nothing): remember where focus came from, so RestoreFocus
                // can return it there when the scope unmounts while holding focus.
                if (!binding.RestoreCaptured)
                {
                    var from = evt.relatedTarget as VisualElement;
                    if (from == null || !scopeRoot.Contains(from))
                    {
                        binding.RestoreTarget = from;
                        binding.RestoreCaptured = true;
                    }
                }
                return;
            }

            // Contain snap-back: focus left a contained scope through a path the sequential interception
            // cannot see (a spatial 2D move, or a pointer press outside) — pull it back inside within the
            // same event flush. A nested Focus() from inside a FocusIn handler starts a new pending switch
            // that wins (the engine's pending-focus gate), so this is deterministic, not a race.
            if (evt.relatedTarget is VisualElement from2 && from2.panel == target.panel)
            {
                var fromScopeRoot = FindEnclosingScopeRoot(from2, ctx, out var fromBinding);
                if (fromScopeRoot != null && fromBinding is { Settings.Contain: true }
                    && !fromScopeRoot.Contains(target))
                {
                    var back = fromBinding.LastFocusedMember;
                    if (back == null || back.panel == null || !back.canGrabFocus)
                    {
                        back = new VisualElementFocusRing(fromScopeRoot)
                            .GetNextFocusable(null, VisualElementFocusChangeDirection.right) as VisualElement;
                    }
                    back?.Focus();
                }
            }
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
                FiberFocusNavigator.EnsureAttached(element.panel?.visualTree, ctx);
                if (binding.Settings.AutoFocus)
                {
                    AutoFocusFirst(element);
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
        // inside the scope — an AutoFocus modal re-attached by a keyed reorder must not steal focus back.
        // Deliberately NOT computed through a focus ring: ring membership requires the element chain to be
        // resolved as displayed, which has not happened yet at attach time (the panel's style pass runs
        // after this frame's mounts) — the engine ring would read empty here. A plain traversal over the
        // same focusability predicate (minus the displayed check) is what attach-time can actually answer.
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
