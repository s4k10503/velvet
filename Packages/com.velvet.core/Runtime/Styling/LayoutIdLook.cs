#nullable enable
using System;
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

        private LayoutIdLook(float opacity, float rotate, Length[] radii, VisualElement source)
        {
            Opacity = opacity;
            Rotate = rotate;
            Radii = radii;
            Source = source;
        }

        public float Opacity { get; }
        // In degrees.
        public float Rotate { get; }
        // Top-left, top-right, bottom-right and bottom-left.
        public Length[] Radii { get; }
        // The holder whose own opacity is read again while it still holds the id; compared by reference only, since
        // once it leaves the pool can hand it to another Motion.
        public VisualElement Source { get; }

        // Without the holder, which has let go of the id: its element can come back from the pool as another Motion.
        public LayoutIdLook Released() => new(Opacity, Rotate, Radii, s_released);

        // Released, at the given opacity.
        public LayoutIdLook Holding(float opacity) => new(opacity, Rotate, Radii, s_released);

        // A holder read mid-way through a shared move of its own gives the opacity that move drew it at, and no
        // holder to read again, as promote takes animationValues, which the new lead's animation stops.
        public static LayoutIdLook Read(VisualElement element, LayoutIdProjection? projection) =>
            projection is { Shared: true, Moving: true }
                ? new LayoutIdLook(element.resolvedStyle.opacity, RotateOf(element, projection), RadiiOf(element, projection), s_released)
                : new LayoutIdLook(MotionOpacity.Own(element), RotateOf(element, projection), RadiiOf(element, projection), element);

        // The rotate and radii an element is drawn with: what its projection wrote, or else its own.
        public static float RotateOf(VisualElement element, LayoutIdProjection? projection) =>
            projection is { WritesRotate: true } ? projection.DrawnRotate : element.resolvedStyle.rotate.angle.ToDegrees();

        public static Length[] RadiiOf(VisualElement element, LayoutIdProjection? projection) =>
            projection is { WritesRadii: true } ? projection.DrawnRadii : Declared(element);

        // The radii the element holds with no projection writing them: its inline slots, else what its rules declare,
        // else what it is resolved at, each where a transition of it is running drawn as UI Toolkit draws it now
        // (Given_AHolderRoundingOnATransition_When_AnotherTakesItsId_Then_TheOtherMixesFromTheRadiusItIsDrawnAt).
        private static Length[] Declared(VisualElement element)
        {
            var radii = new Length[4];
            for (var corner = 0; corner < 4; corner++)
            {
                var slot = CornerRadiusFit.InlineCorner(element.style, corner);
                radii[corner] = RunningStyleTransition.CurrentRadius(element, corner, slot.keyword == StyleKeyword.Undefined ? slot.value
                    : StyleCascade.Radius(element, corner) ?? new Length(CornerRadiusFit.ResolvedCorner(element.resolvedStyle, corner)));
            }
            return radii;
        }

        // A slot that no longer holds the last value written here was written by someone else, and that value is the
        // element's own from then on, as AdoptForeignWrites takes the transform; a radius slot corner by corner.
        public static void Adopt(VisualElement element, LayoutIdProjection projection)
        {
            var rotate = element.style.rotate;
            if (projection.WritesRotate && rotate != projection.WrittenRotate) projection.OwnInlineRotate = rotate;
            if (!projection.WritesRadii) return;
            for (var corner = 0; corner < 4; corner++)
            {
                var slot = CornerRadiusFit.InlineCorner(element.style, corner);
                if (slot == projection.WrittenRadii[corner]) continue;
                projection.OwnInlineRadii[corner] = slot;
                projection.InlineRadii[corner] = slot.keyword != StyleKeyword.Null;
            }
        }

        private static readonly string[] s_cornerNames =
            { "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius" };
        private static readonly StyleLonghandSet s_rotate = StyleLonghandSet.Of(StyleLonghand.Rotate);
        private static readonly StyleLonghandSet s_radii = StyleLonghandSet.Of(StyleLonghand.BorderTopLeftRadius)
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderTopRightRadius))
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderBottomRightRadius))
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderBottomLeftRadius));
        // Each pass, before anything is read: takes the timing a list a variant swap holds inline gives rotate and each
        // corner, then takes what the projection writes out of that list, as MotionOpacity does for opacity on every
        // draw, so that a swap started mid-move carries none of the writes here.
        public static void Narrow(VisualElement element, LayoutIdProjection projection)
        {
            var style = element.style;
            var lists = new TransitionLists(style.transitionProperty.value, style.transitionDuration.value,
                style.transitionDelay.value, style.transitionTimingFunction.value);
            var listed = MotionNativeTransitionGuard.HoldsAForeignValue(element);
            projection.RotateCarry.Hold(lists, listed, "rotate", null);
            for (var corner = 0; corner < 4; corner++)
            {
                projection.RadiusCarries[corner].Hold(lists, listed, s_cornerNames[corner], "border-radius");
            }
            if (projection.WritesRotate) MotionNativeTransitionGuard.ExcludeFromHeldList(element, s_rotate);
            if (projection.WritesRadii) MotionNativeTransitionGuard.ExcludeFromHeldList(element, s_radii);
        }

        // The element's own rotate as it is now. While the projection writes the slot it is the inline rotate the
        // projection took over, else what the element's rules declare, else the one it had as the projection began,
        // carried as OwnRadii carries a corner, over dtSec more.
        public static float OwnRotate(VisualElement element, LayoutIdProjection projection, float dtSec)
        {
            var inline = projection.WritesRotate ? projection.OwnInlineRotate : element.style.rotate;
            if (!projection.WritesRotate)
            {
                return inline.keyword == StyleKeyword.Undefined ? inline.value.angle.ToDegrees() : element.resolvedStyle.rotate.angle.ToDegrees();
            }
            var declared = inline.keyword != StyleKeyword.Null ? inline.value.angle.ToDegrees() : StyleCascade.Rotate(element);
            return projection.RotateCarry.Step(float.IsNaN(declared) ? projection.StartRotate : declared, dtSec, element, "rotate", null);
        }

        // The element's own radii as they are now. While the projection writes the slots a corner's own is an inline
        // value the projection took over from something other than CornerRadiusFit, else the radius the fit gives
        // it, else what the element's rules declare, else the one it had as the projection began.
        // A corner's own changes are carried as MotionOpacity carries opacity: from where it stands towards what gives
        // it now, on the timing a variant swap's held list gave the corner, else the one UI Toolkit runs it by, over
        // dtSec more.
        public static Length[] OwnRadii(VisualElement element, LayoutIdProjection projection, float dtSec)
        {
            if (!projection.WritesRadii) return Declared(element);
            var own = new Length[4];
            for (var corner = 0; corner < 4; corner++)
            {
                var target = projection.InlineRadii[corner] ? projection.OwnInlineRadii[corner].value
                    : CornerRadiusFit.Fitted(element, corner) ?? StyleCascade.Radius(element, corner) ?? projection.StartRadii[corner];
                own[corner] = Carry(element, projection, corner, target, dtSec);
            }
            return own;
        }

        // A change of unit lands at once, since a pixel radius and a percent one do not interpolate.
        private static Length Carry(VisualElement element, LayoutIdProjection projection, int corner, Length target, float dtSec)
        {
            var carry = projection.RadiusCarries[corner];
            if (target.unit != projection.RadiusUnits[corner])
            {
                projection.RadiusUnits[corner] = target.unit;
                carry.Land(target.value);
                return target;
            }
            return new Length(carry.Step(target.value, dtSec, element, s_cornerNames[corner], "border-radius"), target.unit);
        }

        // Mixes two radii as Framer's mixValues does (canMix): a radius of none takes the other's unit, two of one
        // unit mix and do not go below zero, and a pixel radius and a percent one do not mix, the lead's being taken.
        public static Length[] Mix(Length[] from, Length[] to, float progress)
        {
            var mixed = new Length[4];
            for (var corner = 0; corner < 4; corner++)
            {
                var (a, b) = (from[corner], to[corner]);
                var unit = a.value == 0f ? b.unit : a.unit;
                mixed[corner] = b.value != 0f && b.unit != unit ? b
                    : new Length(Mathf.Max(0f, Mathf.LerpUnclamped(a.value, b.value, progress)), b.value == 0f ? unit : b.unit);
            }
            return mixed;
        }

        // Draws the element at the given rotate, or at its own for NaN. The slot is left alone until the projection
        // first needs a value there other than the element's own.
        public static void WriteRotate(VisualElement element, LayoutIdProjection projection, float rotate, float dtSec)
        {
            if (!projection.WritesRotate)
            {
                var own = OwnRotate(element, projection, 0f);
                if (float.IsNaN(rotate) || Mathf.Approximately(rotate, own)) return;
                projection.WritesRotate = true;
                projection.OwnInlineRotate = element.style.rotate;
                projection.StartRotate = own;
                projection.RotateCarry.Land(own);
                RunningStyleTransition.TakeOverRotate(element, projection.RotateCarry);
                MotionNativeTransitionGuard.NarrowIfIntercepted(element, projection, MotionTransitionSlots.Rotate);
            }
            projection.DrawnRotate = float.IsNaN(rotate) ? OwnRotate(element, projection, dtSec) : rotate;
            element.style.rotate = new Rotate(new Angle(projection.DrawnRotate, AngleUnit.Degree));
            projection.WrittenRotate = element.style.rotate;
        }

        // Draws the element with the given radii, or its own for null, so that the scale it is drawn at leaves them as
        // given, as Framer's correctBorderRadius does. A percent radius is of the box and scales with it, so it is
        // written as it is. A pixel one is divided by the scale; IStyle takes one length for a corner, where a scale
        // different on each axis would need one per axis, so it is divided by the geometric mean of the two, and a box
        // drawn with no extent on an axis is written none.
        public static void WriteRadii(VisualElement element, LayoutIdProjection projection, Length[]? radii, Vector2 scale, float dtSec)
        {
            var divisor = Mathf.Sqrt(scale.x * scale.y);
            if (!projection.WritesRadii)
            {
                var own = Declared(element);
                var target = radii ?? own;
                if (IsNone(own) && IsNone(target) || Same(target, own) && Mathf.Approximately(divisor, 1f)) return;
                projection.WritesRadii = true;
                projection.OwnInlineRadii = new StyleLength[4];
                projection.InlineRadii = new bool[4];
                projection.WrittenRadii = new StyleLength[4];
                for (var corner = 0; corner < 4; corner++)
                {
                    var slot = CornerRadiusFit.InlineCorner(element.style, corner);
                    projection.OwnInlineRadii[corner] = slot;
                    projection.InlineRadii[corner] = slot.keyword == StyleKeyword.Undefined && !CornerRadiusFit.Wrote(element, corner, slot);
                }
                projection.StartRadii = own;
                for (var corner = 0; corner < 4; corner++)
                {
                    projection.RadiusCarries[corner].Land(own[corner].value);
                    projection.RadiusUnits[corner] = own[corner].unit;
                    RunningStyleTransition.TakeOverRadius(element, corner, projection.RadiusCarries[corner], own[corner].unit);
                }
                CornerRadiusFit.Hold(element);
                MotionNativeTransitionGuard.NarrowIfIntercepted(element, projection, MotionTransitionSlots.Radius);
            }
            projection.DrawnRadii = radii ?? OwnRadii(element, projection, dtSec);
            var written = new Length[4];
            for (var corner = 0; corner < 4; corner++)
            {
                var radius = projection.DrawnRadii[corner];
                written[corner] = radius.unit == LengthUnit.Percent ? radius
                    : new Length(divisor > 0f ? radius.value / divisor : 0f, radius.unit);
            }
            // Scaled down together where adjacent ones overlap on a side, as CSS scales radii, in the element's own box, which
            // is the drawn box divided by the scale on each axis: the corners CornerRadiusFit fits at rest, as it fits them
            // there (Given_ALayoutIdMotionRoundedFullStartingAtAQuarterOfItsWidth_When_ItsTweenStarts_Then_ItsCornersFitItsBox).
            var fit = CornerRadiusFit.ScaleFactor(element.layout.width, element.layout.height, Extent(written, element.layout));
            for (var corner = 0; corner < 4; corner++)
            {
                var scaled = CornerRadiusFit.Fitted(element, corner) != null ? fit : 1f;
                projection.WrittenRadii[corner] = written[corner] = new Length(written[corner].value * scaled, written[corner].unit);
                Write(element.style, corner, written[corner]);
            }
        }

        // Each corner's horizontal and vertical extent in the box, a percent one being of the box's width and height.
        private static CornerRadii Extent(Length[] radii, Rect box)
        {
            Vector2 Of(Length radius) => radius.unit == LengthUnit.Percent
                ? new Vector2(radius.value / 100f * box.width, radius.value / 100f * box.height)
                : new Vector2(radius.value, radius.value);
            return new CornerRadii { TopLeft = Of(radii[0]), TopRight = Of(radii[1]), BottomRight = Of(radii[2]), BottomLeft = Of(radii[3]) };
        }

        private static bool IsNone(Length[] radii) =>
            radii[0].value == 0f && radii[1].value == 0f && radii[2].value == 0f && radii[3].value == 0f;

        private static bool Same(Length[] a, Length[] b) => a[0] == b[0] && a[1] == b[1] && a[2] == b[2] && a[3] == b[3];

        private static void Write(IStyle style, int corner, StyleLength value)
        {
            switch (corner)
            {
                case 0: style.borderTopLeftRadius = value; break;
                case 1: style.borderTopRightRadius = value; break;
                case 2: style.borderBottomRightRadius = value; break;
                default: style.borderBottomLeftRadius = value; break;
            }
        }

        private sealed class Tail
        {
            public Tail(LayoutIdProjection projection, IVisualElementScheduledItem item) => (Projection, Item) = (projection, item);

            public readonly LayoutIdProjection Projection;
            public readonly IVisualElementScheduledItem Item;
        }

        // A projection that ended with its own rotate or radii still carried, stepped until they land.
        private static readonly ConditionalWeakTable<VisualElement, Tail> s_tails = new();

        // Called as the projection ends: hands back the slots it wrote here and its transition suspension once the rotate
        // and radii it carries have landed, running them on until then, as MotionOpacity runs an opacity that outlasts a
        // crossfade (Given_ALeadWhoseOwnRotateChangesLateInItsMove_When_TheMoveLands_Then_ItsRotateRunsOnAsTheEngineRunsIt).
        public static void End(VisualElement element, LayoutIdProjection projection)
        {
            if (Landed(projection))
            {
                Hand(element, projection);
                return;
            }
            // The tail writes the rotate and radii alone, so the translate and scale the projection wrote transition again
            // (Given_ALeadWhoseRotateRunsOnPastItsLanding_When_ItsTranslateChanges_Then_TheTranslateTransitions).
            MotionNativeTransitionGuard.NarrowTo(element, projection,
                (projection.WritesRotate ? MotionTransitionSlots.Rotate : MotionTransitionSlots.None)
                | (projection.WritesRadii ? MotionTransitionSlots.Radius : MotionTransitionSlots.None));
            var item = element.schedule.Execute(state => StepTail(element, projection, state.deltaTime / 1000f)).Every(StyleAnimateDriver.TickMs);
            s_tails.AddOrUpdate(element, new Tail(projection, item));
        }

        // Lands an ended projection's carried values at once, for a projection about to start on the element.
        public static void Settle(VisualElement element)
        {
            if (!s_tails.TryGetValue(element, out var tail)) return;
            Forget(element);
            Hand(element, tail.Projection);
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
            if (projection.WritesRadii) WriteRadii(element, projection, null, Vector2.one, dtSec);
            if (Landed(projection)) Settle(element);
        }

        private static bool Landed(LayoutIdProjection projection) =>
            projection.RotateCarry.Landed && Array.TrueForAll(projection.RadiusCarries, carry => carry.Landed);

        private static void Hand(VisualElement element, LayoutIdProjection projection)
        {
            Restore(element, projection);
            MotionNativeTransitionGuard.Release(element, projection);
        }

        // Hands back what the projection wrote here, and the radius slots to CornerRadiusFit, which refits them for
        // whatever changed meanwhile.
        private static void Restore(VisualElement element, LayoutIdProjection projection)
        {
            if (projection.WritesRotate) element.style.rotate = projection.OwnInlineRotate;
            if (!projection.WritesRadii) return;
            for (var corner = 0; corner < 4; corner++)
            {
                Write(element.style, corner, projection.OwnInlineRadii[corner]);
            }
            CornerRadiusFit.Unhold(element);
        }
    }
}
