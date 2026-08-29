using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The style slots a per-frame Motion driver can write, at the granularity a play can report driving one,
    /// so a play's driven set and an element's declared set intersect directly.
    /// </summary>
    [Flags]
    internal enum MotionTransitionSlots
    {
        None = 0,
        Opacity = 1 << 0,
        Translate = 1 << 1,
        Scale = 1 << 2,
        Rotate = 1 << 3,
        // A play's colour channels are resolved from its own delta, so a driver reports "writes colours"
        // without saying which; splitting the declared side finer could not change an intersection result.
        Color = 1 << 4,
        // Every length-valued property, grouped for the same reason as Color.
        Length = 1 << 5,
        // `animate-hue` writes `filter` whole, and the pan modes write one of the two background-position
        // longhands per tick -- which of the two is the binding's own axis, so both are one slot here for
        // the same reason Color is one: the driver reports the slot, not the channel.
        //
        // No SlotsOf arm for BackgroundPosition: the bundled sheets declare no transition-property naming
        // it, and StyleTransitionUtilities is derived from them, so the declared side reaches this slot
        // through `all` alone. Filter has `.transition-filter` and so has an arm.
        Filter = 1 << 6,
        BackgroundPosition = 1 << 7,
        All = Opacity | Translate | Scale | Rotate | Color | Length | Filter | BackgroundPosition,
    }

    /// <summary>
    /// Suspends an element's native USS transitions for as long as a per-frame driver is writing a style slot
    /// those transitions would otherwise intercept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A driver writes the exact value the curve or the physics calls for THIS frame. If the element's own
    /// classes also declare a transition covering that property (<c>transition-colors</c>, <c>transition-all</c>,
    /// …), every one of those writes instead STARTS a fresh native transition toward the value, so the painted
    /// value trails the driver for the whole play and then jumps when the driver hands the slot back.
    /// </para>
    /// <para>
    /// UI Toolkit's <c>transition-property</c> is a positive list with no "everything except these" spelling, and
    /// the RESOLVED list cannot be read before the element is on a panel, so the suspension is necessarily
    /// element-wide: for its duration the element's OTHER transitions land instantly too. That cost is only
    /// worth paying where the conflict is real, so the decision is made from the element's own CLASS LIST,
    /// resolved against the table <c>Generators~/src/Velvet.StyleTable</c> derives from the bundled
    /// stylesheets. A play suspends only when the slots it drives intersect what those classes leave
    /// transitionable (see <see cref="DeclaredSlots(VisualElement)"/>): a <c>transition-colors</c> element running an
    /// opacity/translate play keeps its hover fade, while the same play on a <c>transition-transform</c> or
    /// <c>transition-all</c> element does suspend, because there the class covers the very slots the driver is
    /// writing.
    /// </para>
    /// <para>
    /// Suspension is tracked by OWNER because two drivers can write one element at once (a scheduler variant
    /// play and a <c>layoutId</c> spring registered by the same patch): an absolute restore would let whichever
    /// settled first un-suspend the other mid-flight. Owners are held in a set rather than counted, so a release
    /// for an owner that never suspended — or a second release of the same one — is a no-op instead of
    /// unbalancing the state.
    /// </para>
    /// </remarks>
    internal static class MotionNativeTransitionGuard
    {
        // A property name that resolves to no style property computes to zero transitions, which is exactly
        // "transition-property: none". A shared, never-mutated list: StyleList retains the reference as-is
        // (mirroring StyleAnimationScheduler's own transition-property: all list), and the release frees it.
        private static readonly StylePropertyName s_noneName = new("none");
        private static readonly List<StylePropertyName> s_none = new() { s_noneName };

        private sealed class Suspension
        {
            public readonly HashSet<object> Owners = new();
        }

        // Auto-drops entries when an element is collected; a pooled element is scrubbed explicitly through
        // ReleaseAll so a stale owner cannot keep a reused element permanently suspended.
        private static readonly ConditionalWeakTable<VisualElement, Suspension> s_suspensions = new();

        /// <summary>
        /// Suspends on <paramref name="owner"/>'s behalf when the element's own transition utilities name any of
        /// the <paramref name="drivenSlots"/> this play writes; a no-op otherwise, so a play whose slots nothing
        /// transitions leaves the element alone.
        /// </summary>
        public static void SuspendIfIntercepted(VisualElement element, object owner, MotionTransitionSlots drivenSlots)
        {
            if (drivenSlots == MotionTransitionSlots.None
                || (DeclaredSlots(element) & drivenSlots) == MotionTransitionSlots.None)
            {
                return;
            }
            var suspension = s_suspensions.GetValue(element, static _ => new Suspension());
            suspension.Owners.Add(owner);
            // Written on EVERY suspend rather than only the first owner's: this costs one inline write per play
            // (never per tick), and re-asserting it means a foreign write to transition-property landing between
            // two overlapping plays cannot leave the second one unprotected.
            element.style.transitionProperty = s_none;
        }

        /// <summary>
        /// Re-decides <paramref name="owner"/>'s suspension against the element's CURRENT classes, for a driver
        /// whose life spans class-list patches: it suspends once the classes start naming
        /// <paramref name="drivenSlots"/> and releases once they stop.
        /// </summary>
        /// <remarks>
        /// Two departures from <see cref="SuspendIfIntercepted"/>, both because this runs on a patch rather than
        /// at a play's start. The inline transition-duration is not read (see
        /// <see cref="DeclaredSlots(VisualElement, bool)"/>), and the suspension is written only on the patch
        /// that ADDS this owner, and then only over a slot this class still holds (see
        /// <see cref="HoldsAForeignValue"/>) — a patch is not a new play, so re-asserting would overwrite
        /// whatever the element's inline transition-property legitimately became since the owner took it.
        /// </remarks>
        public static void SyncSuspension(VisualElement element, object owner, MotionTransitionSlots drivenSlots)
        {
            if (drivenSlots == MotionTransitionSlots.None
                || (DeclaredSlots(element, readInlineDuration: false) & drivenSlots) == MotionTransitionSlots.None)
            {
                Release(element, owner);
                return;
            }
            var suspension = s_suspensions.GetValue(element, static _ => new Suspension());
            if (suspension.Owners.Add(owner) && !HoldsAForeignValue(element))
            {
                element.style.transitionProperty = s_none;
            }
        }

        /// <summary>
        /// Re-decides the inline slot for a layer that has just finished writing it itself: back to the
        /// suspension while a driver still holds one, back to the cascade otherwise.
        /// </summary>
        /// <remarks>
        /// <see cref="StyleAnimationScheduler"/> clears the slot at the end of EVERY play, while a driver whose
        /// life spans those plays takes its suspension once — so a play that ends by clearing the slot outright
        /// leaves a still-running driver unprotected for good, with nothing that would write the suspension
        /// again.
        /// </remarks>
        public static void RestoreAfterForeignWrite(VisualElement element)
        {
            if (s_suspensions.TryGetValue(element, out var suspension) && suspension.Owners.Count > 0)
            {
                element.style.transitionProperty = s_none;
                return;
            }
            element.style.transitionProperty = StyleKeyword.Null;
        }

        // Whether the slot holds a value this class did not write. FiberNodePatcher starts a Motion's variant
        // swap BEFORE the class passes that attach and detach an animate-* driver, so a patch that swaps a
        // variant while starting or stopping a motion reaches this class with the swap's own
        // transition-property already in the slot — writing or reverting it there would cancel that swap.
        // What puts a still-held suspension back afterwards is the swap's own teardown, through
        // RestoreAfterForeignWrite. Compared by CONTENT rather than by list identity, which the read back out
        // of the slot does not preserve — MotionNativeTransitionGuardSuspensionTests holds that.
        private static bool HoldsAForeignValue(VisualElement element)
        {
            var current = element.style.transitionProperty;
            if (current.keyword != StyleKeyword.Null)
            {
                var value = current.value;
                return value == null || value.Count != 1 || value[0] != s_noneName;
            }
            return false;
        }

        /// <summary>
        /// The slots the element's own classes leave natively transitionable.
        /// </summary>
        /// <remarks>
        /// UI Toolkit's INITIAL <c>transition-property</c> is <c>all</c> and its initial duration is 0s, so an
        /// element transitions everything as soon as anything gives it a duration — and nothing at all until
        /// then. The classes that set <c>transition-property</c> do not combine: it holds one value, so the
        /// LAST of them declared wins outright and the earlier ones contribute nothing, which is the ordering
        /// <see cref="StyleTransitionUtilities"/> records. Failing one of those, a duration from any source —
        /// a <c>duration-*</c> utility, or the bracket form the resolver applies as an inline value rather than
        /// a class — leaves the initial <c>all</c> standing. Failing that too, nothing transitions.
        /// <para>
        /// Two residual blind spots, both accepted rather than fixed: an inline whole-property value written by
        /// something other than a duration utility (the scheduler's own tween swap sets one, though never on a
        /// spring or bezier play) is not read here, and a play asks once at its start, so a variant that turns
        /// on <c>transition-all</c> midway through one is not picked up until the next.
        /// <see cref="SyncSuspension"/> is what a driver outliving a patch asks instead.
        /// </para>
        /// </remarks>
        internal static MotionTransitionSlots DeclaredSlots(VisualElement element)
            => DeclaredSlots(element, readInlineDuration: true);

        /// <param name="readInlineDuration">
        /// False for a driver that can be live while <see cref="StyleAnimationScheduler"/> is playing a variant
        /// tween on the same element. That play holds an inline transition-duration for its whole length, so
        /// reading the slot would report every property transitionable and hand the driver a suspension over
        /// state the scheduler owns — it writes the same inline transition-property this class does.
        /// </param>
        /// <inheritdoc cref="DeclaredSlots(VisualElement)"/>
        internal static MotionTransitionSlots DeclaredSlots(VisualElement element, bool readInlineDuration)
        {
            var winningPosition = -1;
            var declared = StyleLonghandSet.Empty;
            var sawDuration = false;
            foreach (var core in element.GetClasses())
            {
                if (StyleTransitionUtilities.TryGet(core, out var position, out var properties))
                {
                    if (position > winningPosition)
                    {
                        winningPosition = position;
                        declared = properties;
                    }
                    continue;
                }
                sawDuration = sawDuration
                    || (StyleUtilityProperties.TryGet(core, out var rule)
                        && rule.Gate == StyleUtilityGate.None
                        && rule.Properties.Contains(StyleLonghand.TransitionDuration));
            }
            if (winningPosition >= 0)
            {
                return SlotsOf(declared);
            }
            // duration-[400ms] resolves to an inline value rather than a class, so the class scan cannot see it;
            // the inline slot can.
            return sawDuration
                || (readInlineDuration && element.style.transitionDuration.keyword != StyleKeyword.Null)
                ? MotionTransitionSlots.All
                : MotionTransitionSlots.None;
        }

        // Every property the matching driver channel can write, which is what the grouped slot stands for.
        // Both sets track MotionSpringClassParser's channel resolution: a property it can put in a delta and
        // neither set names is a property a play would drive without ever asking the engine to stand down.
        private static readonly StyleLonghandSet s_colorProperties = SetOf(
            StyleLonghand.BackgroundColor,
            StyleLonghand.Color,
            StyleLonghand.BorderTopColor,
            StyleLonghand.BorderRightColor,
            StyleLonghand.BorderBottomColor,
            StyleLonghand.BorderLeftColor);

        private static readonly StyleLonghandSet s_lengthProperties = SetOf(
            StyleLonghand.Width,
            StyleLonghand.Height,
            StyleLonghand.MinWidth,
            StyleLonghand.MinHeight,
            StyleLonghand.MaxWidth,
            StyleLonghand.MaxHeight,
            StyleLonghand.Top,
            StyleLonghand.Right,
            StyleLonghand.Bottom,
            StyleLonghand.Left,
            StyleLonghand.PaddingTop,
            StyleLonghand.PaddingRight,
            StyleLonghand.PaddingBottom,
            StyleLonghand.PaddingLeft,
            StyleLonghand.MarginTop,
            StyleLonghand.MarginRight,
            StyleLonghand.MarginBottom,
            StyleLonghand.MarginLeft,
            StyleLonghand.BorderTopLeftRadius,
            StyleLonghand.BorderTopRightRadius,
            StyleLonghand.BorderBottomLeftRadius,
            StyleLonghand.BorderBottomRightRadius,
            StyleLonghand.BorderTopWidth,
            StyleLonghand.BorderRightWidth,
            StyleLonghand.BorderBottomWidth,
            StyleLonghand.BorderLeftWidth,
            StyleLonghand.FlexBasis,
            StyleLonghand.FontSize,
            StyleLonghand.LetterSpacing);

        /// <summary>The driver channels that would contend for the properties a declaration names.</summary>
        private static MotionTransitionSlots SlotsOf(StyleLonghandSet declared)
        {
            var slots = MotionTransitionSlots.None;
            if (declared.Contains(StyleLonghand.Opacity)) slots |= MotionTransitionSlots.Opacity;
            if (declared.Contains(StyleLonghand.Translate)) slots |= MotionTransitionSlots.Translate;
            if (declared.Contains(StyleLonghand.Scale)) slots |= MotionTransitionSlots.Scale;
            if (declared.Contains(StyleLonghand.Rotate)) slots |= MotionTransitionSlots.Rotate;
            if (declared.Overlaps(s_colorProperties)) slots |= MotionTransitionSlots.Color;
            if (declared.Overlaps(s_lengthProperties)) slots |= MotionTransitionSlots.Length;
            if (declared.Contains(StyleLonghand.Filter)) slots |= MotionTransitionSlots.Filter;
            return slots;
        }

        private static StyleLonghandSet SetOf(params StyleLonghand[] longhands)
        {
            var set = StyleLonghandSet.Empty;
            foreach (var longhand in longhands)
            {
                set = set.Union(StyleLonghandSet.Of(longhand));
            }
            return set;
        }

        /// <summary>Drops one owner, handing the element back to the cascade once the LAST owner has let go.</summary>
        public static void Release(VisualElement element, object owner)
        {
            if (!s_suspensions.TryGetValue(element, out var suspension) || !suspension.Owners.Remove(owner))
            {
                return;
            }
            if (suspension.Owners.Count == 0)
            {
                s_suspensions.Remove(element);
                if (!HoldsAForeignValue(element))
                {
                    element.style.transitionProperty = StyleKeyword.Null;
                }
            }
        }

        /// <summary>
        /// Forgets every owner of an element being torn down or returned to a pool. The drivers release their
        /// own suspensions on settle and on cancel, so this is the backstop for a teardown ordering that skips
        /// one: a surviving owner would otherwise make the reused element's next play believe another driver is
        /// still live, and never restore its transitions.
        /// </summary>
        public static void ReleaseAll(VisualElement element)
        {
            if (element != null)
            {
                s_suspensions.Remove(element);
            }
        }
    }
}
