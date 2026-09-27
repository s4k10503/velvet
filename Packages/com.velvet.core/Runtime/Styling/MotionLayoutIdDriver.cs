#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Shared-element layout animation via FLIP (First-Last-Invert-Play). When a V.Motion(layoutId:)
    // patches at a box different from the one the SAME id stood at — including across a DIFFERENT
    // physical element entirely, e.g. after a same-key type flip or a move to a different parent — it
    // tweens from the old box to the new one instead of jump-cutting: capture the OLD box, let this
    // frame's layout settle at the NEW one, compute the delta, apply it as an inline inverse transform
    // (Invert), then spring that inverse back to zero (Play). Reuses MotionSpringDriver's existing
    // panel-independent physics channels (translate x/y, uniform scale) — the same machinery every other
    // spring-driven Motion transition already shares — rather than building a second driver.
    //
    // A box is a layout rect together with the parent it is relative to and its position in panel space.
    // Two boxes under one parent compare their layout rects; under different parents, their panel-space
    // positions. Panel space throughout was rejected: a nested layoutId Motion that moves inside a moving
    // one then no longer tweens by its own move inside it
    // (Given_ALayoutIdMotionInsideAnother_When_BothMove_Then_TheInnerTweensOnlyItsOwnMoveInsideTheOuter).
    //
    // Scope: uniform scale only. MotionSpringDriver.SpringChannel.Scale drives a single Vector2(v, v),
    // so a non-uniform rect change (width and height scale by different factors) averages the two axis
    // scale factors into one uniform factor instead of distorting the element on two independent axes.
    internal static class MotionLayoutIdDriver
    {
        // Called from FiberNodePatcher.PatchMotion for a MotionNode carrying a LayoutId, once the
        // patch's own class/style/children work is done. element.layout still holds the PRE-patch
        // resolved rect at this point (this frame's Yoga pass has not run yet) — capturing it now is the
        // same "read .layout synchronously before the mutation that invalidates it" pattern
        // GeneralPathReconciler.PinExitingChildOutOfFlow uses for a PopLayout exit. The NEW rect is not
        // trustworthy yet (a reparented/freshly-created element's .layout stays stale until the next
        // Yoga pass — see FiberWrapperElementAppliers's clip-wrapper comment on the same window), so it
        // is captured on this element's own first post-patch GeometryChangedEvent instead.
        internal static void OnPatched(VisualElement element, string layoutId, float stiffness, float damping, float mass, ReconcilerContext ctx)
        {
            // The old box is read off whichever element the id is registered to — this one, or the one it
            // replaces, which teardown has not reached yet — rather than stored at registration: a freshly
            // created element registers before its first layout, with no box to store, and a stored zero
            // rect reads as a real box at the parent's origin. The box an entry carries is the fallback for
            // an element not laid out yet, and the whole entry once teardown has taken its element.
            LayoutIdBox? oldBox = null;
            if (ctx.LayoutIdRegistry.TryGetValue(layoutId, out var previous))
            {
                oldBox = previous.Element != null && TryReadBox(previous.Element, out var live) ? live : previous.Box;
            }

            ctx.ElementToLayoutId[element] = layoutId;
            ctx.LayoutIdRegistry[layoutId] = (element, oldBox);

            // A second patch before a layout settles the first replaces its wait rather than adding one.
            CancelPendingSettle(element, ctx);
            if (oldBox is not { } fromBox) return;

            EventCallback<GeometryChangedEvent>? onSettled = null;
            onSettled = _ =>
            {
                CancelPendingSettle(element, ctx);
                var parent = element.hierarchy.parent;
                var from = ReferenceEquals(fromBox.Parent, parent)
                    ? fromBox.Local
                    : new Rect(parent.WorldToLocal(fromBox.PanelPosition), fromBox.Local.size);

                var plan = ComputeDeltaPlan(from, element.layout);
                if (plan.IsEmpty) return;

                var state = MotionSpringDriver.Create(plan, stiffness, damping, mass);
                if (state == null) return;

                // Supersede a tween already in flight on this same element before starting a fresh one — a
                // rapid-fire re-layout (two patches within one tween's own lifetime) must not stack a second
                // independent tick. Not CancelForTeardown: that also drops the registration written just
                // above, and the next move of this id would then find nothing to tween from.
                StopTick(element, ctx);
                MotionSpringDriver.ApplyCurrentValues(element, state);
                StartTick(element, state, ctx);
            };
            element.RegisterCallback(onSettled);
            ctx.LayoutIdPendingSettles[element] = onSettled;
        }

        private static bool TryReadBox(VisualElement element, out LayoutIdBox box)
        {
            box = default;
            var layout = element.layout;
            if (element.hierarchy.parent is not { } parent || !IsFiniteRect(layout)) return false;
            box = new LayoutIdBox(parent, layout, parent.LocalToWorld(layout.position));
            return true;
        }

        private static void CancelPendingSettle(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdPendingSettles.Remove(element, out var pending))
            {
                element.UnregisterCallback(pending);
            }
        }

        private static void StartTick(VisualElement element, MotionSpringState state, ReconcilerContext ctx)
        {
            var host = element.panel?.visualTree;
            if (host == null) return;

            var tick = host.schedule.Execute((TimerState ts) =>
            {
                var dt = ts.deltaTime / 1000f;
                if (dt <= 0f) return;
                if (!MotionSpringDriver.Step(element, state, dt)) return;
                StopTick(element, ctx);
            }).Every(StyleAnimateDriver.TickMs);
            ctx.LayoutIdTicks[element] = (tick, state);
        }

        // Ends the in-flight tween the way its own settle does, so one superseded mid-flight hands back the
        // transition suspension it took, which no later settle would release.
        private static void StopTick(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdTicks.Remove(element, out var running))
            {
                running.Tick.Pause();
                MotionSpringDriver.ClearInlineOverrides(element, running.State);
            }
        }

        // Called from FiberElementCleaner before an element is pooled or disposed. The pending settle goes
        // too: a pooled element keeps its callbacks, so whatever the pool hands it to next would otherwise
        // play this element's tween. Torn down inside a pass, the element leaves its box for a replacement
        // later in that pass — a same-key type flip creates it after this teardown — and the pass boundary
        // drops what nobody claimed (ExpireSnapshots).
        internal static void CancelForTeardown(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdTicks.Remove(element, out var running))
            {
                running.Tick.Pause();
            }
            CancelPendingSettle(element, ctx);
            if (ctx.ElementToLayoutId.TryGetValue(element, out var layoutId)
                && ctx.LayoutIdRegistry.TryGetValue(layoutId, out var current)
                && ReferenceEquals(current.Element, element))
            {
                if (ctx.CurrentPass != null)
                {
                    ctx.LayoutIdRegistry[layoutId] = (null, TryReadBox(element, out var live) ? live : current.Box);
                    ctx.LayoutIdSnapshots.Add(layoutId);
                }
                else
                {
                    ctx.LayoutIdRegistry.Remove(layoutId);
                }
            }
        }

        // Called at the top-level pass boundary.
        internal static void ExpireSnapshots(ReconcilerContext ctx)
        {
            foreach (var layoutId in ctx.LayoutIdSnapshots)
            {
                if (ctx.LayoutIdRegistry.TryGetValue(layoutId, out var entry) && entry.Element == null)
                {
                    ctx.LayoutIdRegistry.Remove(layoutId);
                }
            }
            // MUTANT_SURVIVES(equivalent): CancelForTeardown lists every id it gives a null element, so an id
            // left over here is removed at a later boundary exactly when it would have been listed again.
            ctx.LayoutIdSnapshots.Clear();
        }

        // Pure(ish) mechanics, panel-free by design (mirrors MotionSpringDriverTests' own rationale for
        // testing the spring math directly): resolves an old→new rect pair into a SpringPlan whose
        // TranslateX/Y channels animate the position delta back to zero and whose Scale channel
        // animates the (averaged, uniform) size ratio back to 1 — empty (IsEmpty) when the rects are
        // equal within Mathf.Approximately's tolerance, so the caller can skip building spring state
        // for a patch that didn't actually move/resize anything, and when either rect is not a resolved
        // layout (NaN).
        internal static MotionSpringClassParser.SpringPlan ComputeDeltaPlan(Rect oldRect, Rect newRect)
        {
            if (!IsFiniteRect(oldRect) || !IsFiniteRect(newRect)) return default;

            var dx = oldRect.x - newRect.x;
            var dy = oldRect.y - newRect.y;
            var scaleX = newRect.width > 0.01f ? oldRect.width / newRect.width : 1f;
            var scaleY = newRect.height > 0.01f ? oldRect.height / newRect.height : 1f;
            var scale = (scaleX + scaleY) / 2f;

            var translateChanged = !Mathf.Approximately(dx, 0f) || !Mathf.Approximately(dy, 0f);
            var scaleChanged = !Mathf.Approximately(scale, 1f);

            return new MotionSpringClassParser.SpringPlan
            {
                TranslateX = translateChanged ? (dx, 0f) : null,
                TranslateY = translateChanged ? (dy, 0f) : null,
                Scale = scaleChanged ? (scale, 1f) : null,
            };
        }

        private static bool IsFiniteRect(Rect r) =>
            float.IsFinite(r.x) && float.IsFinite(r.y) && float.IsFinite(r.width) && float.IsFinite(r.height);
    }

    internal readonly struct LayoutIdBox
    {
        public LayoutIdBox(VisualElement parent, Rect local, Vector2 panelPosition)
        {
            Parent = parent;
            Local = local;
            PanelPosition = panelPosition;
        }

        public VisualElement Parent { get; }
        public Rect Local { get; }
        public Vector2 PanelPosition { get; }
    }
}
