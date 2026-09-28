#nullable enable
using System.Collections.Generic;
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
    // A box is a layout rect together with the parent it was read under, and its centre and size in panel
    // space. A box read under this same parent compares layout rects; any other is taken into the new parent
    // less the inverse translate every ancestor still waiting on its own layoutId settle is about to apply.
    // A box forgets its parent when the pool takes that parent back (ForgetParent), since the pool can hand
    // it to another Motion's element before the box is claimed.
    // Panel space throughout was rejected: an inner layoutId Motion that moves inside an outer one still
    // tweening then no longer tweens by its own move inside it
    // (Given_AnOuterLayoutIdMotionStillTweening_When_OnlyTheInnerMovesInsideIt_Then_TheInnerTweensOnlyItsOwnMove).
    // The ancestors' translate is subtracted rather than read off their transform: an inner Motion settles
    // before the outer one has applied it
    // (Given_ALayoutIdMotionInsideATypeFlippedOne_When_BothMove_Then_TheInnerTweensOnlyItsOwnMoveInsideTheOuter).
    // The centre is mapped as a point and the size through each parent's scale factors rather than as a
    // rect: a rect's panel mapping is the bounding box of its transformed corners, which a rotated ancestor
    // inflates
    // (Given_ALayoutIdMotionInARotatedBoard_When_ItMovesToTheOtherColumn_Then_ItTweensFromItsOldPlaceAtItsOwnSize).
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

            var pending = new LayoutIdPendingSettle(fromBox);
            pending.Callback = _ =>
            {
                CancelPendingSettle(element, ctx);
                var plan = ComputeDeltaPlan(FromRect(element, fromBox, ctx), element.layout, TransformOrigin(element));
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
            element.RegisterCallback(pending.Callback);
            ctx.LayoutIdPendingSettles[element] = pending;
        }

        // The old box in the frame of the element's current parent.
        private static Rect FromRect(VisualElement element, LayoutIdBox fromBox, ReconcilerContext ctx)
        {
            var parent = element.hierarchy.parent;
            if (ReferenceEquals(fromBox.Parent, parent)) return fromBox.Local;
            var centre = parent.WorldToLocal(fromBox.PanelCentre - PendingPanelShift(parent, ctx));
            var size = fromBox.PanelSize / PanelScale(parent);
            return new Rect(centre - size / 2f, size);
        }

        // How long a unit step along each local axis is in panel space.
        private static Vector2 PanelScale(VisualElement element)
        {
            var origin = element.LocalToWorld(Vector2.zero);
            return new Vector2((element.LocalToWorld(Vector2.right) - origin).magnitude,
                (element.LocalToWorld(Vector2.up) - origin).magnitude);
        }

        private static Vector2 TransformOrigin(VisualElement element) => element.resolvedStyle.transformOrigin;

        // How far, in panel space, the inverse translates the ancestors' pending settles will apply move this
        // element. Only translate: an ancestor's inverse scale is not accounted for.
        private static Vector2 PendingPanelShift(VisualElement? ancestor, ReconcilerContext ctx)
        {
            var shift = Vector2.zero;
            for (; ancestor?.hierarchy.parent is { } parent; ancestor = parent)
            {
                if (!ctx.LayoutIdPendingSettles.TryGetValue(ancestor, out var pending)) continue;
                var layout = ancestor.layout;
                var plan = ComputeDeltaPlan(FromRect(ancestor, pending.From, ctx), layout, TransformOrigin(ancestor));
                var translate = new Vector2(plan.TranslateX?.from ?? 0f, plan.TranslateY?.from ?? 0f);
                shift += parent.LocalToWorld(layout.position + translate) - parent.LocalToWorld(layout.position);
            }
            return shift;
        }

        private static bool TryReadBox(VisualElement element, out LayoutIdBox box)
        {
            box = default;
            var layout = element.layout;
            if (element.hierarchy.parent is not { } parent || !IsFiniteRect(layout)) return false;
            box = new LayoutIdBox(parent, layout, parent.LocalToWorld(layout.center), layout.size * PanelScale(parent));
            return true;
        }

        private static void CancelPendingSettle(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.LayoutIdPendingSettles.Remove(element, out var pending))
            {
                element.UnregisterCallback(pending.Callback);
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
        // later in that render — a same-key type flip creates it after this teardown — and ExpireSnapshots
        // drops what nobody claimed where the render ends.
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

        // Called from FiberElementCleaner as it returns an element to the pool.
        internal static void ForgetParent(VisualElement parent, ReconcilerContext ctx)
        {
            List<string>? forgotten = null;
            foreach (var entry in ctx.LayoutIdRegistry)
            {
                if (entry.Value.Box is { } box && ReferenceEquals(box.Parent, parent))
                {
                    (forgotten ??= new List<string>()).Add(entry.Key);
                }
            }
            if (forgotten == null) return;
            foreach (var layoutId in forgotten)
            {
                var entry = ctx.LayoutIdRegistry[layoutId];
                ctx.LayoutIdRegistry[layoutId] = (entry.Element, entry.Box!.Value.Detached());
            }
        }

        // Called where a render ends: the end of a batch drain, whose passes are one render, and the
        // boundary of a top-level pass outside a drain.
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

        // A power of two, so a rect off by exactly this much is representable and the boundary testable.
        internal const float PixelTolerance = 1f / 128f;

        // Pure(ish) mechanics, panel-free by design (mirrors MotionSpringDriverTests' own rationale for
        // testing the spring math directly): resolves an old→new rect pair into a SpringPlan whose Scale
        // channel animates the (averaged, uniform) size ratio back to 1 and whose TranslateX/Y channels
        // animate back to zero the offset between the new rect's transform origin (in its own pixels) and
        // the point at the same fraction of the old rect — the scale holds the origin still, so aligning
        // those two points is what starts the tween over the old rect. Empty (IsEmpty) when the rects differ
        // by no more than PixelTolerance, so the caller can skip building spring state for a patch that
        // didn't actually move/resize anything, and when either rect is not finite: NaN before a first
        // layout, infinite in a parent drawn at zero scale.
        internal static MotionSpringClassParser.SpringPlan ComputeDeltaPlan(Rect oldRect, Rect newRect, Vector2 origin)
        {
            if (!IsFiniteRect(oldRect) || !IsFiniteRect(newRect)) return default;

            var scaleX = newRect.width > 0.01f ? oldRect.width / newRect.width : 1f;
            var scaleY = newRect.height > 0.01f ? oldRect.height / newRect.height : 1f;
            var scale = (scaleX + scaleY) / 2f;
            var oldOrigin = oldRect.position + new Vector2(origin.x * scaleX, origin.y * scaleY);
            var delta = oldOrigin - (newRect.position + origin);

            // In pixels, not float equality: a box mapped through a rotated parent carries float noise.
            var translateChanged = Mathf.Abs(delta.x) > PixelTolerance || Mathf.Abs(delta.y) > PixelTolerance;
            var scaleChanged = Mathf.Abs(scale - 1f) * Mathf.Max(newRect.width, newRect.height) > PixelTolerance;

            return new MotionSpringClassParser.SpringPlan
            {
                TranslateX = translateChanged ? (delta.x, 0f) : null,
                TranslateY = translateChanged ? (delta.y, 0f) : null,
                Scale = scaleChanged ? (scale, 1f) : null,
            };
        }

        private static bool IsFiniteRect(Rect r) =>
            float.IsFinite(r.x) && float.IsFinite(r.y) && float.IsFinite(r.width) && float.IsFinite(r.height);
    }

    internal readonly struct LayoutIdBox
    {
        public LayoutIdBox(VisualElement? parent, Rect local, Vector2 panelCentre, Vector2 panelSize)
        {
            Parent = parent;
            Local = local;
            PanelCentre = panelCentre;
            PanelSize = panelSize;
        }

        public VisualElement? Parent { get; }
        public Rect Local { get; }
        public Vector2 PanelCentre { get; }
        public Vector2 PanelSize { get; }

        // Without the parent it was read under, which the pool has taken back and can hand to another
        // Motion's element, where it would pass for that parent
        // (Given_TwoListComponentsHoldingTheCardInAPooledButton_When_TheSecondIsSelected_Then_TheCardTweensFromTheFirst).
        public LayoutIdBox Detached() => new(null, Local, PanelCentre, PanelSize);
    }

    internal sealed class LayoutIdPendingSettle
    {
        public LayoutIdPendingSettle(LayoutIdBox from) => From = from;

        public LayoutIdBox From { get; }
        public EventCallback<GeometryChangedEvent> Callback { get; set; } = null!;
    }
}
