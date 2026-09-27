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
            // The old rect is read off whichever element the id is registered to — this one, or the one it
            // replaces, which teardown has not reached yet — rather than stored at registration: a freshly
            // created element registers before its first layout, with no rect to store, and a stored zero
            // rect reads as a real box at the parent's origin.
            var hadPrevious = ctx.LayoutIdRegistry.TryGetValue(layoutId, out var previous);
            var oldRect = hadPrevious ? previous.layout : default;

            ctx.ElementToLayoutId[element] = layoutId;
            ctx.LayoutIdRegistry[layoutId] = element;

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

                var plan = ComputeDeltaPlan(oldRect, newRect);
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

        // Cancels any in-flight tick and drops the registry entries for a departing element — called
        // from FiberElementCleaner before an element is pooled/disposed, so a layoutId tween never keeps
        // ticking against (or leaves a stale rect behind for) a torn-down element.
        internal static void CancelForTeardown(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdTicks.Remove(element, out var running))
            {
                running.Tick.Pause();
            }
            if (ctx.ElementToLayoutId.TryGetValue(element, out var layoutId)
                && ctx.LayoutIdRegistry.TryGetValue(layoutId, out var current)
                && ReferenceEquals(current, element))
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
