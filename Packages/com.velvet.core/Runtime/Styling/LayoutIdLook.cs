#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a holder of a layoutId looked like where its box was read: the values Framer's mixValues mixes a shared
    // layout animation's lead in from (mix-values.ts), which NodeStack.promote takes as the previous lead's
    // animationValues or else its latestValues (stack.ts): as the holder was drawn, and a holder's opacity its own
    // unless it was itself moving from another's box (Read).
    internal readonly struct LayoutIdLook
    {
        // The source of a look whose holder has let go of the id: an element no Motion is, so no id holds it.
        private static readonly VisualElement s_released = new();

        private LayoutIdLook(float opacity, float rotate, VisualElement source)
        {
            Opacity = opacity;
            Rotate = rotate;
            Source = source;
        }

        public float Opacity { get; }
        // In degrees.
        public float Rotate { get; }
        // The holder whose own opacity is read again while it still holds the id; compared by reference only, since
        // once it leaves the pool can hand it to another Motion.
        public VisualElement Source { get; }

        // Without the holder, which has let go of the id: its element can come back from the pool as another Motion.
        public LayoutIdLook Released() => new(Opacity, Rotate, s_released);

        // Released, at the given opacity.
        public LayoutIdLook Holding(float opacity) => new(opacity, Rotate, s_released);

        // A holder read mid-way through a shared move of its own gives the opacity that move drew it at, and no
        // holder to read again, as promote takes animationValues, which the new lead's animation stops.
        public static LayoutIdLook Read(VisualElement element, LayoutIdProjection? projection) =>
            projection is { Shared: true, Moving: true }
                ? new LayoutIdLook(element.resolvedStyle.opacity, RotateOf(element, projection), s_released)
                : new LayoutIdLook(MotionOpacity.Own(element), RotateOf(element, projection), element);

        // The rotate an element is drawn with: what its projection wrote, or else its own.
        public static float RotateOf(VisualElement element, LayoutIdProjection? projection) =>
            projection is { WritesRotate: true } ? projection.DrawnRotate : Unspun(element, element.style.rotate);

        // The rotate the element is drawn at less a running spin's turn, which the spin adds to the element's own
        // (StyleAnimateDriver.WriteRotation) rather than being a rotate of the element's own.
        private static float Unspun(VisualElement element, StyleRotate inline) =>
            StyleAnimateDriver.TryReadSpinOwn(element, inline, out _, out var own) ? own : element.resolvedStyle.rotate.angle.ToDegrees();

        // A slot that no longer holds the last value written here was written by someone else, and that value is the
        // element's own from then on, as AdoptForeignWrites takes the transform.
        public static void Adopt(VisualElement element, LayoutIdProjection projection)
        {
            var rotate = element.style.rotate;
            if (projection.WritesRotate && rotate != projection.WrittenRotate) projection.OwnInlineRotate = rotate;
        }

        private static readonly StyleLonghandSet s_rotate = StyleLonghandSet.Of(StyleLonghand.Rotate);
        // Each pass, before anything is read: takes the timing a list a variant swap holds inline gives rotate, then
        // takes what the projection writes out of that list, as MotionOpacity does for opacity on every draw, so that a
        // swap started mid-move carries none of the writes here.
        public static void Narrow(VisualElement element, LayoutIdProjection projection)
        {
            var style = element.style;
            var lists = new TransitionLists(style.transitionProperty.value, style.transitionDuration.value,
                style.transitionDelay.value, style.transitionTimingFunction.value);
            var listed = MotionNativeTransitionGuard.HoldsAForeignValue(element);
            projection.RotateCarry.Hold(lists, listed, "rotate", null);
            if (projection.WritesRotate) MotionNativeTransitionGuard.ExcludeFromHeldList(element, s_rotate);
        }

        // The element's own rotate as it is now. While the projection writes the slot it is the inline rotate the
        // projection took over, else what the element's rules declare, else the one it had as the projection began,
        // carried on the transition the element declares for rotate, over dtSec more.
        public static float OwnRotate(VisualElement element, LayoutIdProjection projection, float dtSec)
        {
            var inline = projection.WritesRotate ? projection.OwnInlineRotate : element.style.rotate;
            if (!projection.WritesRotate)
            {
                if (StyleAnimateDriver.TryReadSpinOwn(element, inline, out _, out var unspun)) return unspun;
                return inline.keyword == StyleKeyword.Undefined ? inline.value.angle.ToDegrees() : element.resolvedStyle.rotate.angle.ToDegrees();
            }
            var declared = inline.keyword != StyleKeyword.Null ? inline.value.angle.ToDegrees() : StyleCascade.Rotate(element);
            return projection.RotateCarry.Step(float.IsNaN(declared) ? projection.StartRotate : declared, dtSec, element, "rotate", null);
        }

        // Draws the element at the given rotate, or at its own for NaN. The slot is left alone until the projection
        // first needs a value there other than the element's own.
        public static void WriteRotate(VisualElement element, LayoutIdProjection projection, float rotate, float dtSec)
        {
            if (!projection.WritesRotate)
            {
                var own = OwnRotate(element, projection, 0f);
                if (float.IsNaN(rotate) || Mathf.Approximately(rotate, own)) return;
                // A running spin's frame in the slot is the spin's; what it turns from is the element's own.
                projection.OwnInlineRotate = StyleAnimateDriver.TryReadSpinOwn(element, element.style.rotate, out var spinOwn, out _)
                    ? spinOwn
                    : element.style.rotate;
                projection.WritesRotate = true;
                projection.StartRotate = own;
                projection.RotateCarry.Land(own);
                RunningStyleTransition.TakeOverRotate(element, projection.RotateCarry);
                MotionNativeTransitionGuard.NarrowIfIntercepted(element, projection, MotionTransitionSlots.Rotate);
                StyleAnimateDriver.YieldSlots(element, projection, MotionLayoutIdDriver.WrittenSlots(projection));
            }
            projection.DrawnRotate = float.IsNaN(rotate) ? OwnRotate(element, projection, dtSec) : rotate;
            // A running spin leaves the slot to the projection and its turn is added here, as the projection adds a
            // bounce's lift or a ping's growth.
            element.style.rotate = new Rotate(new Angle(projection.DrawnRotate + StyleAnimateDriver.CurrentSpinDeg(element), AngleUnit.Degree));
            projection.WrittenRotate = element.style.rotate;
        }

        private sealed class Tail
        {
            public Tail(LayoutIdProjection projection, IVisualElementScheduledItem item) => (Projection, Item) = (projection, item);

            public readonly LayoutIdProjection Projection;
            public readonly IVisualElementScheduledItem Item;
        }

        // A projection that ended with its own rotate still carried, stepped until it lands.
        private static readonly ConditionalWeakTable<VisualElement, Tail> s_tails = new();

        // Called as the projection ends: hands back the slots it wrote here and its narrowing once the rotate it
        // carries has landed, running it on until then, as MotionOpacity runs an opacity that outlasts a crossfade (Given_ALeadWhoseOwnRotateChangesLateInItsMove_When_TheMoveLands_Then_ItsRotateRunsOnAsTheEngineRunsIt).
        // clock: the mount's, on which the tail steps.
        public static void End(VisualElement element, LayoutIdProjection projection, MotionClock clock)
        {
            if (Landed(projection))
            {
                Hand(element, projection);
                return;
            }
            // The tail writes the rotate alone, so the translate and scale the projection wrote transition again
            // (Given_ALeadWhoseRotateRunsOnPastItsLanding_When_ItsTranslateChanges_Then_TheTranslateTransitions).
            MotionNativeTransitionGuard.NarrowTo(element, projection, MotionTransitionSlots.Rotate);
            var lastSec = clock.NowSec;
            var item = element.schedule.Execute(state => StepTail(element, projection, clock.StepSince(ref lastSec, state)))
                .Every(StyleAnimateDriver.TickMs);
            s_tails.AddOrUpdate(element, new Tail(projection, item));
        }

        // Whether an ending projection's rotate runs on past it (End).
        public static bool RunsOn(LayoutIdProjection projection) => !Landed(projection);

        // Hands an ended projection's carried rotate, with the slots and the narrowing it holds for it, to a projection
        // starting on the element, which runs it on
        // (Given_ALeadWhoseRotateRunsOnPastItsLanding_When_ItMovesAgain_Then_ItsRotateRunsOnAsTheEngineRunsIt).
        public static void Settle(VisualElement element, LayoutIdProjection next)
        {
            if (!s_tails.TryGetValue(element, out var tail)) return;
            Forget(element);
            var held = tail.Projection;
            (next.WritesRotate, next.OwnInlineRotate, next.StartRotate, next.DrawnRotate, next.WrittenRotate, next.RotateCarry) =
                (held.WritesRotate, held.OwnInlineRotate, held.StartRotate, held.DrawnRotate, held.WrittenRotate, held.RotateCarry);
            MotionNativeTransitionGuard.Transfer(element, held, next);
            StyleAnimateDriver.YieldSlots(element, held, MotionTransitionSlots.None);
            StyleAnimateDriver.YieldSlots(element, next, MotionLayoutIdDriver.WrittenSlots(next));
        }

        // Drops an ended projection's tail without writing, for an element torn down.
        public static void Forget(VisualElement element)
        {
            if (!s_tails.TryGetValue(element, out var tail)) return;
            tail.Item.Pause();
            s_tails.Remove(element);
        }

        private static void StepTail(VisualElement element, LayoutIdProjection projection, float dtSec)
        {
            MotionNativeTransitionGuard.Refresh(element);
            Adopt(element, projection);
            Narrow(element, projection);
            if (projection.WritesRotate) WriteRotate(element, projection, float.NaN, dtSec);
            if (!Landed(projection)) return;
            Forget(element);
            Hand(element, projection);
        }

        private static bool Landed(LayoutIdProjection projection) => projection.RotateCarry.Landed;

        private static void Hand(VisualElement element, LayoutIdProjection projection)
        {
            Restore(element, projection);
            MotionNativeTransitionGuard.Release(element, projection);
            // A running spin takes the slot back from the element's own rotate, written back above.
            StyleAnimateDriver.YieldSlots(element, projection, MotionTransitionSlots.None);
            StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.None);
        }

        // Hands back what the projection wrote here.
        private static void Restore(VisualElement element, LayoutIdProjection projection)
        {
            if (projection.WritesRotate) element.style.rotate = projection.OwnInlineRotate;
        }
    }
}
