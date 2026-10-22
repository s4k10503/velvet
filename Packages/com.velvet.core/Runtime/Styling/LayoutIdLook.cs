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
        // restored: the rotate a projection ended earlier in this render handed back to an element no projection draws now,
        // or NaN
        // (RotateOf).
        public static LayoutIdLook Read(VisualElement element, LayoutIdProjection? projection, float restored) =>
            projection is { Shared: true, Moving: true }
                ? new LayoutIdLook(element.resolvedStyle.opacity, RotateOf(element, projection, restored), RadiiOf(element, projection, new Length[4]), s_released)
                : new LayoutIdLook(MotionOpacity.Own(element), RotateOf(element, projection, restored), RadiiOf(element, projection, new Length[4]), element);

        // The rotate and radii an element is drawn with: what its projection wrote, or else its own. The resolved style
        // still shows what a projection that ended earlier in this render drew until a style pass has run, so what that
        // projection handed back is read in its place, off the projection that took the record or else restored
        // (Given_AHolderThatTookTheLeadBackAtOnce_When_AnotherJoinsUnderAnotherParentInThatRender_Then_ItMixesFromItsOwnRotate).
        public static float RotateOf(VisualElement element, LayoutIdProjection? projection, float restored) =>
            projection is { WritesRotate: true } ? projection.DrawnRotate : Unspun(element, element.style.rotate, projection?.RestoredRotate ?? restored);

        // The rotate the element is drawn at less a running spin's turn, which the spin adds to the element's own
        // (StyleAnimateDriver.WriteRotation) rather than being a rotate of the element's own.
        private static float Unspun(VisualElement element, StyleRotate inline, float restored) =>
            StyleAnimateDriver.TryReadSpinOwn(element, inline, out _, out var own) ? own
            : float.IsNaN(restored) ? element.resolvedStyle.rotate.angle.ToDegrees() : restored;

        // Written into the four corners of into, which is returned. A pass that reads them again, as a member behind the
        // lead does each frame, hands an array it keeps, so that reading them allocates nothing
        // (Given_AHolderRoundedByItsRules_When_ItsRadiiAreReadAgain_Then_NothingIsAllocated); Read, which runs once for a
        // box taken at a patch or a move's start, hands a new one.
        public static Length[] RadiiOf(VisualElement element, LayoutIdProjection? projection, Length[] into)
        {
            if (projection is { WritesRadii: true })
            {
                Array.Copy(projection.DrawnRadii, into, 4);
                return into;
            }
            return Declared(element, into);
        }

        // The radii the element holds with no projection writing them, written into radii: its inline slots, else what
        // its rules declare, else what it is resolved at, each where a transition of it is running drawn as UI Toolkit
        // draws it now
        // (Given_AHolderRoundingOnATransition_When_AnotherTakesItsId_Then_TheOtherMixesFromTheRadiusItIsDrawnAt).
        private static Length[] Declared(VisualElement element, Length[] radii)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                var slot = CornerRadiusFit.InlineCorner(element.style, corner);
                radii[corner] = RunningStyleTransition.CurrentRadius(element, corner, Holds(slot) ? Inline(slot)
                    : StyleCascade.Radius(element, corner) ?? new Length(CornerRadiusFit.ResolvedCorner(element.resolvedStyle, corner)));
            }
            return radii;
        }

        // Whether an inline slot gives its corner's radius: a value, or `initial`, which is read as none, the value CSS
        // gives border-radius
        // (Given_AHolderWithAnInlineInitialRadius_When_ItsRadiiAreRead_Then_ItIsSquare).
        private static bool Holds(StyleLength slot) => slot.keyword is StyleKeyword.Undefined or StyleKeyword.Initial;

        private static Length Inline(StyleLength slot) => slot.keyword == StyleKeyword.Initial ? new Length(0f) : slot.value;

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
                projection.InlineRadii[corner] = Holds(slot);
            }
        }

        private static readonly string[] s_cornerNames =
            { "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius" };
        private static readonly StyleLonghandSet s_rotate = StyleLonghandSet.Of(StyleLonghand.Rotate);
        private static readonly StyleLonghandSet s_radii = StyleLonghandSet.Of(StyleLonghand.BorderTopLeftRadius)
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderTopRightRadius))
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderBottomRightRadius))
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderBottomLeftRadius));
        // Each pass, before anything is read: takes the timing a list a variant swap holds inline gives rotate, each
        // corner, translate and scale, then takes the rotate and radii the projection writes out of that list, as
        // MotionOpacity does for opacity on every draw, so that a swap started mid-move carries none of the writes here.
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
            for (var axis = 0; axis < 2; axis++)
            {
                projection.TranslateCarries[axis].Hold(lists, listed, "translate", null);
                projection.ScaleCarries[axis].Hold(lists, listed, "scale", null);
            }
            if (projection.WritesRotate) MotionNativeTransitionGuard.ExcludeFromHeldList(element, s_rotate);
            if (projection.WritesRadii) MotionNativeTransitionGuard.ExcludeFromHeldList(element, s_radii);
        }

        // The element's own rotate as it is now. While the projection writes the slot it is the inline rotate the
        // projection took over, else what the element's rules declare, else the one it had as the projection began,
        // carried as OwnRadii carries a corner, over dtSec more. Before that it is where the element is drawn, which is the
        // rotate a projection ended earlier in this render handed back rather than its resolved style until a style pass
        // has run (Given_AFollowerDrawnOverALead_When_TheLeadLeaves_Then_ItMovesFromItsOwnTransform).
        public static float OwnRotate(VisualElement element, LayoutIdProjection projection, float dtSec)
        {
            var inline = projection.WritesRotate ? projection.OwnInlineRotate : element.style.rotate;
            if (!projection.WritesRotate)
            {
                if (StyleAnimateDriver.TryReadSpinOwn(element, inline, out _, out var unspun)) return unspun;
                if (inline.keyword == StyleKeyword.Undefined) return inline.value.angle.ToDegrees();
                return float.IsNaN(projection.RestoredRotate) ? element.resolvedStyle.rotate.angle.ToDegrees() : projection.RestoredRotate;
            }
            var declared = DeclaredRotate(element, projection);
            return projection.RotateCarry.Step(declared.angle.ToDegrees(), Tag(declared), dtSec, element, "rotate", null);
        }

        // The rotate the element is given while the projection writes the slot, which the slot holds once it is handed back.
        public static Rotate DeclaredRotate(VisualElement element, LayoutIdProjection projection)
        {
            var inline = projection.OwnInlineRotate;
            if (inline.keyword == StyleKeyword.Undefined) return inline.value;
            if (inline.keyword != StyleKeyword.Null) return UnityEngine.UIElements.Rotate.None();
            return PendingClassTransform.DeclaredRotate(element) ?? StyleCascade.Rotation(element) ?? new Rotate(new Angle(projection.StartRotate));
        }

        // A rotate tagged as LayoutIdCarry.TargetTag reads it.
        internal static int Tag(Rotate rotate) => rotate == UnityEngine.UIElements.Rotate.None() ? LayoutIdCarry.NoneTag : (int)rotate.angle.unit;

        // The element's own radii as they are now. While the projection writes the slots a corner's own is an inline
        // value the projection took over from something other than CornerRadiusFit, else the radius the fit gives
        // it, else what the element's rules declare, else the one it had as the projection began.
        // A corner's own changes are carried as MotionOpacity carries opacity: from where it stands towards what gives
        // it now, on the timing a variant swap's held list gave the corner, else the one UI Toolkit runs it by, over
        // dtSec more.
        // Written into own, which is returned.
        public static Length[] OwnRadii(VisualElement element, LayoutIdProjection projection, float dtSec, Length[] own)
        {
            if (!projection.WritesRadii) return Declared(element, own);
            for (var corner = 0; corner < 4; corner++)
            {
                var target = projection.InlineRadii[corner] ? Inline(projection.OwnInlineRadii[corner])
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
                carry.Land(target.value, (int)target.unit);
                return target;
            }
            return new Length(carry.Step(target.value, dtSec, element, s_cornerNames[corner], "border-radius"), target.unit);
        }

        // Mixes two radii as Framer's mixValues does (canMix): a radius of none takes the other's unit, two of one
        // unit mix and do not go below zero, and a pixel radius and a percent one do not mix, the lead's being taken.
        // Written into mixed, which is returned.
        public static Length[] Mix(Length[] from, Length[] to, float progress, Length[] mixed)
        {
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
                // A running spin's frame in the slot is the spin's; what it turns from is the element's own.
                projection.OwnInlineRotate = StyleAnimateDriver.TryReadSpinOwn(element, element.style.rotate, out var spinOwn, out _)
                    ? spinOwn
                    : element.style.rotate;
                projection.WritesRotate = true;
                projection.StartRotate = own;
                projection.RotateCarry.Land(own, Tag(DeclaredRotate(element, projection)));
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

        // Draws the element with the given radii, or its own for null, so that the scale it is drawn at leaves them as
        // given, as Framer's correctBorderRadius does. A percent radius is of the box and scales with it, so it is
        // written as it is. A pixel one is divided by the scale, on each axis by that axis's own as Framer divides it
        // (`x% y%`). IStyle takes one length for a corner, so where the two axes differ the slots are written none and
        // ProjectedCornerFace paints the face with a radius per axis; where it declines the element's background, a
        // pixel radius is divided by the geometric mean of the two. A box drawn with no extent on an axis is written none.
        public static void WriteRadii(VisualElement element, LayoutIdProjection projection, Length[]? radii, Vector2 scale, float dtSec)
        {
            var divisor = Mathf.Sqrt(scale.x * scale.y);
            if (!projection.WritesRadii)
            {
                var own = Declared(element, s_own);
                var target = radii ?? own;
                if (IsNone(own) && IsNone(target) || Same(target, own) && Unscaled(scale)) return;
                projection.WritesRadii = true;
                projection.OwnInlineRadii = new StyleLength[4];
                projection.InlineRadii = new bool[4];
                projection.WrittenRadii = new StyleLength[4];
                for (var corner = 0; corner < 4; corner++)
                {
                    var slot = CornerRadiusFit.InlineCorner(element.style, corner);
                    projection.OwnInlineRadii[corner] = slot;
                    projection.InlineRadii[corner] = Holds(slot) && !CornerRadiusFit.Wrote(element, corner, slot);
                }
                projection.StartRadii = (Length[])own.Clone();
                projection.DrawnRadii = new Length[4];
                for (var corner = 0; corner < 4; corner++)
                {
                    projection.RadiusCarries[corner].Land(own[corner].value, (int)own[corner].unit);
                    projection.RadiusUnits[corner] = own[corner].unit;
                    RunningStyleTransition.TakeOverRadius(element, corner, projection.RadiusCarries[corner], own[corner].unit);
                }
                CornerRadiusFit.Hold(element);
                MotionNativeTransitionGuard.NarrowIfIntercepted(element, projection, MotionTransitionSlots.Radius);
            }
            Array.Copy(radii ?? OwnRadii(element, projection, dtSec, projection.OwnRadiiBuffer), projection.DrawnRadii, 4);
            if (!Uniform(scale) && HasPixelRadius(projection.DrawnRadii)
                && ProjectedCornerFace.Draw(element, PerAxis(projection.DrawnRadii, scale, element.layout), dtSec))
            {
                for (var corner = 0; corner < 4; corner++)
                {
                    projection.WrittenRadii[corner] = new Length(0f);
                    Write(element.style, corner, projection.WrittenRadii[corner]);
                }
                return;
            }
            ProjectedCornerFace.End(element);
            var written = s_written;
            for (var corner = 0; corner < 4; corner++)
            {
                var radius = projection.DrawnRadii[corner];
                written[corner] = radius.unit == LengthUnit.Percent ? radius
                    : new Length(divisor > 0f ? radius.value / divisor : 0f, radius.unit);
            }
            // Scaled down together where adjacent ones overlap on a side, as CSS scales radii, in the element's own box, which
            // is the drawn box divided by the scale on each axis: the corners CornerRadiusFit fits at rest, as it fits them
            // there (Given_ALayoutIdMotionWithUnequalArbitraryRadiiThatOverlapAsItsTweenStarts_When_ItStarts_Then_TheyAreScaledDownTogether).
            var fit = CornerRadiusFit.ScaleFactor(element.layout.width, element.layout.height, Extent(written, element.layout));
            for (var corner = 0; corner < 4; corner++)
            {
                var scaled = CornerRadiusFit.Fitted(element, corner) != null ? fit : 1f;
                projection.WrittenRadii[corner] = written[corner] = new Length(written[corner].value * scaled, written[corner].unit);
                Write(element.style, corner, written[corner]);
            }
        }

        private static bool Unscaled(Vector2 scale) => Mathf.Approximately(scale.x, 1f) && Mathf.Approximately(scale.y, 1f);

        // Within a ten-thousandth of each other, which moves a 100px radius a hundredth of a pixel.
        private static bool Uniform(Vector2 scale) =>
            Mathf.Abs(scale.x - scale.y) <= 1e-4f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));

        private static bool HasPixelRadius(Length[] radii) => Array.Exists(radii, r => r.unit != LengthUnit.Percent && r.value > 0f);

        // Each corner's radius in the element's own box, horizontal and vertical: a pixel one divided by the scale on
        // that axis, or none on an axis drawn with no extent, and a percent one of the box's width and height.
        internal static CornerRadii PerAxis(Length[] radii, Vector2 scale, Rect box)
        {
            Vector2 Of(Length radius) => radius.unit == LengthUnit.Percent
                ? new Vector2(radius.value / 100f * box.width, radius.value / 100f * box.height)
                : new Vector2(scale.x > 0f ? radius.value / scale.x : 0f, scale.y > 0f ? radius.value / scale.y : 0f);
            return new CornerRadii { TopLeft = Of(radii[0]), TopRight = Of(radii[1]), BottomRight = Of(radii[2]), BottomLeft = Of(radii[3]) };
        }

        // Scratch for a single pass, which no caller keeps.
        private static readonly Length[] s_own = new Length[4];
        private static readonly Length[] s_written = new Length[4];

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

        // A projection that ended with its own rotate, radii, translate or scale still carried, stepped until they land.
        private static readonly ConditionalWeakTable<VisualElement, Tail> s_tails = new();

        // Called as the projection ends: hands back the slots it wrote here and its narrowing once the rotate, radii,
        // translate and scale it carries have landed, running them on until then, as MotionOpacity runs an opacity that
        // outlasts a crossfade (Given_ALeadWhoseOwnRotateChangesLateInItsMove_When_TheMoveLands_Then_ItsRotateRunsOnAsTheEngineRunsIt,
        // Given_ALeadWhoseTransformChangesLateInItsMove_When_TheMoveLands_Then_ItRunsOnAsTheEngineRunsIt). The translate
        // and scale that have landed MotionLayoutIdDriver has handed back already.
        // clock: the mount's, on which the tail steps.
        // Returns whether it handed them back at once.
        public static bool End(VisualElement element, LayoutIdProjection projection, MotionClock clock)
        {
            var landed = Landed(projection);
            if (landed) Hand(element, projection);
            else RunOnPast(element, projection, clock);
            return landed;
        }

        private static void RunOnPast(VisualElement element, LayoutIdProjection projection, MotionClock clock)
        {
            // The tail writes the rotate and radii, and the translate and scale it still carries, so a translate or scale the
            // projection wrote that has landed transitions again
            // (Given_ALeadWhoseRotateRunsOnPastItsLanding_When_ItsTranslateChanges_Then_TheTranslateTransitions).
            MotionNativeTransitionGuard.NarrowTo(element, projection, MotionLayoutIdDriver.WrittenSlots(projection)
                | (projection.WritesRadii ? MotionTransitionSlots.Radius : MotionTransitionSlots.None));
            var lastSec = clock.NowSec;
            var item = element.schedule.Execute(state => StepTail(element, projection, clock.StepSince(ref lastSec, state)))
                .Every(StyleAnimateDriver.TickMs);
            s_tails.AddOrUpdate(element, new Tail(projection, item));
        }

        // Whether anything an ending projection carries runs on past it (End).
        public static bool RunsOn(LayoutIdProjection projection) => !Landed(projection);

        // Hands an ended projection's carried rotate, radii, translate and scale, with the slots and the narrowing it holds
        // for them, to a projection starting on the element, which runs them on
        // (Given_ALeadWhoseRotateRunsOnPastItsLanding_When_ItMovesAgain_Then_ItsRotateRunsOnAsTheEngineRunsIt).
        public static void Settle(VisualElement element, LayoutIdProjection next)
        {
            if (!s_tails.TryGetValue(element, out var tail)) return;
            Forget(element);
            var held = tail.Projection;
            (next.WritesRotate, next.OwnInlineRotate, next.StartRotate, next.DrawnRotate, next.WrittenRotate, next.RotateCarry) =
                (held.WritesRotate, held.OwnInlineRotate, held.StartRotate, held.DrawnRotate, held.WrittenRotate, held.RotateCarry);
            (next.WritesRadii, next.OwnInlineRadii, next.InlineRadii, next.StartRadii, next.DrawnRadii, next.WrittenRadii, next.RadiusCarries,
                next.RadiusUnits) = (held.WritesRadii, held.OwnInlineRadii, held.InlineRadii, held.StartRadii, held.DrawnRadii, held.WrittenRadii,
                held.RadiusCarries, held.RadiusUnits);
            // A translate or scale that landed as the projection ended was handed back then, and the slot is the element's.
            if (held.WritesTranslate)
            {
                (next.WritesTranslate, next.OwnInlineTranslate, next.WrittenTranslate, next.TranslateCarries, next.TranslateUnits) =
                    (true, held.OwnInlineTranslate, held.WrittenTranslate, held.TranslateCarries, held.TranslateUnits);
            }
            if (held.WritesScale)
            {
                (next.WritesScale, next.OwnInlineScale, next.WrittenScale, next.ScaleCarries) =
                    (true, held.OwnInlineScale, held.WrittenScale, held.ScaleCarries);
            }
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
            if (projection.WritesRadii) WriteRadii(element, projection, null, Vector2.one, dtSec);
            MotionLayoutIdDriver.RunOn(element, projection, dtSec);
            if (!Landed(projection)) return;
            Forget(element);
            Hand(element, projection);
        }

        private static bool Landed(LayoutIdProjection projection) =>
            projection.RotateCarry.Landed && Landed(projection.RadiusCarries) && Landed(projection.TranslateCarries)
            && Landed(projection.ScaleCarries);

        internal static bool Landed(LayoutIdCarry[] carries) => Array.TrueForAll(carries, carry => carry.Landed);

        private static void Hand(VisualElement element, LayoutIdProjection projection)
        {
            Restore(element, projection);
            MotionNativeTransitionGuard.Release(element, projection);
            // A running spin takes the slot back from the element's own rotate, written back above.
            StyleAnimateDriver.YieldSlots(element, projection, MotionTransitionSlots.None);
            StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.None);
        }

        // Hands back what the projection wrote here, and the radius slots to CornerRadiusFit, which refits them for
        // whatever changed meanwhile.
        private static void Restore(VisualElement element, LayoutIdProjection projection)
        {
            if (projection.WritesRotate) element.style.rotate = projection.OwnInlineRotate;
            if (projection.WritesTranslate) element.style.translate = projection.OwnInlineTranslate;
            if (projection.WritesScale) element.style.scale = projection.OwnInlineScale;
            if (!projection.WritesRadii) return;
            ProjectedCornerFace.End(element);
            for (var corner = 0; corner < 4; corner++)
            {
                Write(element.style, corner, projection.OwnInlineRadii[corner]);
            }
            CornerRadiusFit.Unhold(element);
        }
    }
}
