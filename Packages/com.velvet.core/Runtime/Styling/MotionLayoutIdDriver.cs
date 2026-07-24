#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Shared-element layout animation via FLIP (First-Last-Invert-Play). When a V.Motion(layoutId:)
    // patches at a resolved layout rect different from the rect the SAME id last settled at —
    // including across a DIFFERENT physical element entirely, e.g. after a same-key type flip or a
    // move to a different parent — it tweens from the old rect to the new one instead of jump-cutting:
    // capture the OLD rect, let this frame's layout settle at the NEW one, compute the delta, apply it
    // as an inline inverse transform (Invert), then spring that inverse back to zero (Play). Reuses
    // MotionSpringDriver's existing panel-independent physics channels (translate x/y, uniform scale)
    // — the same machinery every other spring-driven Motion transition already shares — rather than
    // building a second driver.
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
            var hadPrevious = ctx.LayoutIdRegistry.TryGetValue(layoutId, out var previous);
            // A same-key type flip creates a NEW element for the SAME layoutId — its own .layout has
            // never been resolved (fresh element), so the old rect has to come from the id's last known
            // settle point instead of this element's own (nonexistent) history.
            var oldRect = ReferenceEquals(previous.Element, element) ? element.layout : previous.Rect;

            ctx.ElementToLayoutId[element] = layoutId;
            // Placeholder until the post-patch GeometryChangedEvent below overwrites it with the real
            // new rect — anything reading the registry in between (a second, concurrent layoutId patch
            // elsewhere in the same pass) sees the STALE rect, which only risks that other patch's own
            // delta momentarily missing a change this element hasn't settled into yet, never corrupting
            // it.
            ctx.LayoutIdRegistry[layoutId] = (element, oldRect);

            if (!hadPrevious || !IsFiniteRect(oldRect))
            {
                // First-ever registration for this id, or the captured rect is not a real resolved
                // layout (NaN — an EditMode pass with no forced layout) — nothing to tween from.
                return;
            }

            element.RegisterCallback<GeometryChangedEvent>(OnGeometrySettled);

            void OnGeometrySettled(GeometryChangedEvent evt)
            {
                element.UnregisterCallback<GeometryChangedEvent>(OnGeometrySettled);
                var newRect = element.layout;
                if (!IsFiniteRect(newRect)) return;

                ctx.LayoutIdRegistry[layoutId] = (element, newRect);

                var plan = ComputeDeltaPlan(oldRect, newRect);
                if (plan.IsEmpty) return;

                var state = MotionSpringDriver.Create(plan, stiffness, damping, mass);
                if (state == null) return;

                // Cancel a tween already in flight on this same element before starting a fresh one — a
                // rapid-fire re-layout (two patches within one tween's own lifetime) must retarget from
                // wherever the element visually sits right now, not stack a second independent tick.
                CancelForTeardown(element, ctx);
                MotionSpringDriver.ApplyCurrentValues(element, state);
                StartTick(element, state, ctx);
            }
        }

        private static void StartTick(VisualElement element, MotionSpringState state, ReconcilerContext ctx)
        {
            var host = element.panel?.visualTree;
            if (host == null) return;

            ctx.LayoutIdTicks[element] = host.schedule.Execute((TimerState ts) =>
            {
                var dt = ts.deltaTime / 1000f;
                if (dt <= 0f) return;
                if (!MotionSpringDriver.Step(element, state, dt)) return;

                if (ctx.LayoutIdTicks.TryGetValue(element, out var self))
                {
                    self.Pause();
                    ctx.LayoutIdTicks.Remove(element);
                }
                MotionSpringDriver.ClearInlineOverrides(element, state);
            }).Every(StyleAnimateDriver.TickMs);
        }

        // Cancels any in-flight tick and drops the registry entries for a departing element — called
        // from FiberElementCleaner before an element is pooled/disposed, so a layoutId tween never keeps
        // ticking against (or leaves a stale rect behind for) a torn-down element. Also called internally
        // above to retarget a rapid re-layout instead of stacking concurrent ticks on one element.
        internal static void CancelForTeardown(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdTicks.TryGetValue(element, out var tick))
            {
                tick.Pause();
                ctx.LayoutIdTicks.Remove(element);
            }
            if (ctx.ElementToLayoutId.TryGetValue(element, out var layoutId)
                && ctx.LayoutIdRegistry.TryGetValue(layoutId, out var current)
                && ReferenceEquals(current.Element, element))
            {
                ctx.LayoutIdRegistry.Remove(layoutId);
            }
        }

        // Pure(ish) mechanics, panel-free by design (mirrors MotionSpringDriverTests' own rationale for
        // testing the spring math directly): resolves an old→new rect pair into a SpringPlan whose
        // TranslateX/Y channels animate the position delta back to zero and whose Scale channel
        // animates the (averaged, uniform) size ratio back to 1 — empty (IsEmpty) when the rects are
        // equal within Mathf.Approximately's tolerance, so the caller can skip building spring state
        // for a patch that didn't actually move/resize anything.
        internal static MotionSpringClassParser.SpringPlan ComputeDeltaPlan(Rect oldRect, Rect newRect)
        {
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
}
