using System;
using System.Collections.Generic;
using System.Linq;
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
        // Neither has a SlotsOf arm yet: the driver reports the slot it writes, and an element reaches
        // both through `all`. `.transition-filter` is the one utility that could name a declared side
        // finer, which is a separate change from giving the driver a flag.
        Filter = 1 << 6,
        BackgroundPosition = 1 << 7,
        // MotionLayoutIdDriver hides a layoutId member that another holder leads through `visibility`.
        Visibility = 1 << 8,
        // The four corner radii alone, for a layoutId projection that writes them and no other length.
        Radius = 1 << 9,
        All = Opacity | Translate | Scale | Rotate | Color | Length | Filter | BackgroundPosition | Visibility | Radius,
    }

    /// <summary>
    /// An owner <see cref="MotionNativeTransitionGuard.DriverSuspends"/> does not count as a driver.
    /// </summary>
    internal interface ICarryingOwner
    {
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
    /// The RESOLVED <c>transition-property</c> cannot be read before the element is on a panel, so there is no
    /// list of the element's own to narrow, and the suspension is element-wide: for its duration the element's
    /// OTHER transitions land instantly too. That cost is only
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
    /// play and a <c>layoutId</c> move registered by the same patch): an absolute restore would let whichever
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
            // The owners that take only what they write out of the element's transitions (Narrow), with those longhands.
            public readonly Dictionary<object, StyleLonghandSet> Narrowers = new();
            // The drivers writing the element's slots frame by frame, suspended or not, with those slots (Drives).
            public readonly Dictionary<object, MotionTransitionSlots> Driven = new();
            // The transition-property list Narrow last wrote, null while it has written none, and the rules it read it off.
            public List<StylePropertyName> Written;
            public long? Rules;
            public bool Stale;
            // Where Narrow writes the duration, delay and curve lists too (WriteNarrowed), and the element's own, which it
            // hands back as the last narrower lets go (HandBackTiming).
            public bool WroteTiming;
            public StyleList<TimeValue> OwnDuration;
            public StyleList<TimeValue> OwnDelay;
            public StyleList<EasingFunction> OwnCurve;
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
            if (drivenSlots == MotionTransitionSlots.None)
            {
                return;
            }
            s_suspensions.GetValue(element, static _ => new Suspension()).Driven[owner] = drivenSlots;
            // A variant tween holding the slot is narrowed whatever the classes say: its list names this play's
            // slots, and writing `none` over it instead would land the tween at its target.
            // A keyword in the slot is not a list to narrow, and is written over as it always was.
            var held = HoldsAForeignValue(element) && element.style.transitionProperty.value != null;
            if (held)
            {
                ExcludeFromHeldList(element, LonghandsOf(drivenSlots));
            }
            if ((DeclaredSlots(element) & drivenSlots) == MotionTransitionSlots.None)
            {
                return;
            }
            s_suspensions.GetValue(element, static _ => new Suspension()).Owners.Add(owner);
            if (!held)
            {
                element.style.transitionProperty = s_none;
            }
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
        /// A slot a variant tween holds is narrowed on every call instead, owner or not: FiberNodePatcher starts
        /// a swap before the class pass that calls this, so a swap started under a running motion is narrowed on
        /// the patch that starts it.
        /// </remarks>
        public static void SyncSuspension(VisualElement element, object owner, MotionTransitionSlots drivenSlots) =>
            SyncSuspension(element, owner, drivenSlots,
                (DeclaredSlots(element, readInlineDuration: false) & drivenSlots) != MotionTransitionSlots.None);

        /// <summary>
        /// <see cref="SyncSuspension(VisualElement, object, MotionTransitionSlots)"/> with whether the element
        /// transitions <paramref name="drivenSlots"/> decided by the caller, for one that reads more than the classes.
        /// </summary>
        public static void SyncSuspension(VisualElement element, object owner, MotionTransitionSlots drivenSlots, bool intercepted)
        {
            ExcludeFromHeldList(element, LonghandsOf(drivenSlots));
            if (!intercepted)
            {
                Release(element, owner);
            }
            else if (s_suspensions.GetValue(element, static _ => new Suspension()).Owners.Add(owner) && !HoldsAForeignValue(element))
            {
                element.style.transitionProperty = s_none;
            }
            s_suspensions.GetValue(element, static _ => new Suspension()).Driven[owner] = drivenSlots;
        }

        /// <summary>
        /// Takes the longhands of <paramref name="drivenSlots"/> out of the element's own transitions on
        /// <paramref name="owner"/>'s behalf, leaving the element's other transitions running, as Framer leaves the CSS
        /// transitions of what a layout animation does not write. Where the element's own classes name none of
        /// those slots this is a no-op, as for <see cref="SuspendIfIntercepted"/>.
        /// </summary>
        /// <remarks>
        /// The element's transitions are read off its cascade (<see cref="StyleCascade.Lists"/>) and written inline
        /// without those longhands, an <c>all</c> becoming every other longhand, and the duration its own classes
        /// wrote inline is handed back as the last narrower lets go. A variant tween's list in the slot is narrowed
        /// instead, and a play's suspension left to suspend everything; either is replaced by the narrowed lists as
        /// it ends (<see cref="RestoreAfterForeignWrite"/>, <see cref="Release"/>).
        /// </remarks>
        public static void NarrowIfIntercepted(VisualElement element, object owner, MotionTransitionSlots drivenSlots)
        {
            ExcludeFromHeldList(element, LonghandsOf(drivenSlots));
            if ((DeclaredSlots(element) & drivenSlots) != MotionTransitionSlots.None) Narrow(element, owner, drivenSlots);
        }

        /// <summary>
        /// <see cref="NarrowIfIntercepted"/> for a caller that has decided the element transitions
        /// <paramref name="drivenSlots"/>, and asks again on each pass, so that a change of the element's rules is taken.
        /// </summary>
        public static void Narrow(VisualElement element, object owner, MotionTransitionSlots drivenSlots)
        {
            var driven = LonghandsOf(drivenSlots);
            ExcludeFromHeldList(element, driven);
            var suspension = s_suspensions.GetValue(element, static _ => new Suspension());
            var narrowed = suspension.Narrowers.GetValueOrDefault(owner, StyleLonghandSet.Empty).Union(driven);
            suspension.Stale |= !suspension.Narrowers.TryGetValue(owner, out var held) || !held.Equals(narrowed);
            suspension.Narrowers[owner] = narrowed;
            WriteNarrowed(element, suspension);
        }

        /// <summary>
        /// <see cref="Narrow(VisualElement, object, MotionTransitionSlots)"/> where <paramref name="intercepted"/>, and the
        /// owner's narrowing released where not, as <see cref="SyncSuspension(VisualElement, object, MotionTransitionSlots, bool)"/>
        /// re-decides a suspension.
        /// </summary>
        public static void Narrow(VisualElement element, object owner, MotionTransitionSlots drivenSlots, bool intercepted)
        {
            if (intercepted)
            {
                Narrow(element, owner, drivenSlots);
                return;
            }
            ExcludeFromHeldList(element, LonghandsOf(drivenSlots));
            Release(element, owner);
        }

        // Writes the element's transitions less every narrower's longhands into the inline lists, unless another layer
        // holds the slot or the lists written already stand for the element's rules as they are.
        private static void WriteNarrowed(VisualElement element, Suspension suspension)
        {
            var style = element.style;
            var slot = style.transitionProperty;
            if (slot.keyword != StyleKeyword.Null && !Wrote(suspension, slot.value)) return;
            var rules = StyleCascade.RulesHash(element);
            if (slot.keyword != StyleKeyword.Null && !suspension.Stale && rules == suspension.Rules) return;
            // Before a style pass has matched the element's rules none can be read: nothing is left to transition until the
            // next tick, which narrows again
            // (Given_AFollowerMountedWithItsLead_When_ItsBackgroundChanges_Then_TheBackgroundTransitions).
            var read = StyleCascade.Lists(element);
            if (read == null) element.schedule.Execute(() => Renarrow(element));
            var lists = read ?? new TransitionLists(null, null, null, null);
            var count = lists.Properties.Count;
            style.transitionProperty = new List<StylePropertyName>(lists.Properties);
            // Written only where one of them pairs its entries with the properties by position, so that elsewhere a class
            // changing them is taken by the style pass that sees it
            // (Given_ANarrowedMotionWhoseTransitionClassesChangeMidMove_When_ItsBackgroundChanges_Then_ItRunsOnTheNewTiming);
            // a tween's stay where it wrote them, with the element's own kept aside until it ends.
            var writes = !s_tweenTimings.TryGetValue(element, out _) && Math.Max(lists.Durations.Count, Math.Max(lists.Delays.Count, lists.Curves.Count)) > 1;
            HandBackTiming(element, suspension);
            if (writes)
            {
                (suspension.OwnDuration, suspension.OwnDelay, suspension.OwnCurve, suspension.WroteTiming) =
                    (Copy(style.transitionDuration), Copy(style.transitionDelay), Copy(style.transitionTimingFunction), true);
                (style.transitionDuration, style.transitionDelay, style.transitionTimingFunction) =
                    (Cycle(lists.Durations, count), Cycle(lists.Delays, count), Cycle(lists.Curves, count));
            }
            var excluded = StyleLonghandSet.Empty;
            foreach (var longhands in suspension.Narrowers.Values)
            {
                excluded = excluded.Union(longhands);
            }
            Exclude(element, style.transitionProperty.value, excluded, writes);
            (suspension.Written, suspension.Rules, suspension.Stale) =
                (new List<StylePropertyName>(style.transitionProperty.value), rules, read == null);
        }

        private static void Renarrow(VisualElement element)
        {
            if (!s_suspensions.TryGetValue(element, out var suspension)) return;
            if (suspension.Narrowers.Count > 0) WriteNarrowed(element, suspension);
        }

        private static bool Wrote(Suspension suspension, List<StylePropertyName> names) =>
            suspension.Written != null && names != null && names.SequenceEqual(suspension.Written);

        // A list read out of a slot is refilled by that slot's next write, so a kept one is a copy.
        private static StyleList<T> Copy<T>(StyleList<T> list) =>
            list.keyword == StyleKeyword.Undefined ? new StyleList<T>(new List<T>(list.value)) : list;

        // Companion lists repeat to the length of transition-property, as UI Toolkit pairs them, so that one entry can be
        // taken out of all four by position.
        private static List<T> Cycle<T>(IList<T> list, int count)
        {
            var cycled = new List<T>(count);
            for (var i = 0; i < count && list is { Count: > 0 }; i++)
            {
                cycled.Add(list[i % list.Count]);
            }
            return cycled;
        }

        // Hands the element's own inline lists back once no narrower is left, or narrows for those left.
        private static void Unnarrow(VisualElement element, Suspension suspension)
        {
            if (suspension.Narrowers.Count > 0)
            {
                suspension.Stale = true;
                WriteNarrowed(element, suspension);
                return;
            }
            var style = element.style;
            var slot = style.transitionProperty;
            // A variant tween writing over the lists keeps them until it ends, and clears them itself.
            if (!HoldsAForeignValue(element)) HandBackTiming(element, suspension);
            if (Wrote(suspension, slot.value)) style.transitionProperty = suspension.Owners.Count > 0 ? s_none : StyleKeyword.Null;
            suspension.Written = null;
        }

        /// <summary>
        /// The duration, delay and curve lists the element runs its transitions by inline: its own, which
        /// <see cref="Narrow"/> keeps aside while it writes its own lists there, else the slots', a tween's while one
        /// holds them.
        /// </summary>
        internal static (StyleList<TimeValue> Duration, StyleList<TimeValue> Delay, StyleList<EasingFunction> Curve) OwnTiming(
            VisualElement element) =>
            s_suspensions.TryGetValue(element, out var suspension) && suspension.WroteTiming && !s_tweenTimings.TryGetValue(element, out _)
                ? (suspension.OwnDuration, suspension.OwnDelay, suspension.OwnCurve)
                : (element.style.transitionDuration, element.style.transitionDelay, element.style.transitionTimingFunction);

        // Writes back the element's own lists where Narrow wrote its own over them.
        private static void HandBackTiming(VisualElement element, Suspension suspension)
        {
            if (!suspension.WroteTiming || s_tweenTimings.TryGetValue(element, out _)) return;
            (element.style.transitionDuration, element.style.transitionDelay, element.style.transitionTimingFunction) =
                (suspension.OwnDuration, suspension.OwnDelay, suspension.OwnCurve);
            suspension.WroteTiming = false;
        }

        // The elements a tween holds the inline duration, delay and curve of (TweenTiming).
        private static readonly ConditionalWeakTable<VisualElement, TweenHold> s_tweenTimings = new();

        // The duration, delay and curve lists the element held as a tween first wrote its own over them.
        private sealed class TweenHold
        {
            public StyleList<TimeValue> Duration;
            public StyleList<TimeValue> Delay;
            public StyleList<EasingFunction> Curve;
        }

        /// <summary>
        /// Called by <see cref="StyleAnimationScheduler"/> as it writes a tween's duration, delay and curve inline and as
        /// it clears them: none of them is the element's own, to be handed back after a narrowing
        /// (Given_ALayoutIdMotionWhoseEnterTweenEndsBeforeItsMove_When_TheMoveLands_Then_NoTimingIsLeftInline). What the
        /// element held before the tween's first write is put back as the tween clears its own
        /// (Given_AMotionWithAnArbitraryDuration_When_AVariantTweenEnds_Then_ItsDurationIsItsOwnAgain).
        /// </summary>
        internal static void TweenTiming(VisualElement element, bool held)
        {
            var style = element.style;
            var holding = s_tweenTimings.TryGetValue(element, out var hold);
            if (held && !holding)
            {
                s_tweenTimings.Add(element, new TweenHold
                {
                    Duration = Copy(style.transitionDuration), Delay = Copy(style.transitionDelay), Curve = Copy(style.transitionTimingFunction),
                });
            }
            else if (!held && holding)
            {
                s_tweenTimings.Remove(element);
                (style.transitionDuration, style.transitionDelay, style.transitionTimingFunction) = (hold.Duration, hold.Delay, hold.Curve);
            }
        }

        /// <summary>
        /// Writes the element's own inline duration list, as a <c>duration-[x]</c> class gives it. Where
        /// <see cref="Narrow"/> has written its own lists over the element's, it replaces the one kept aside and the
        /// narrowed lists are written again from it
        /// (Given_ANarrowedMotionWhoseArbitraryDurationChangesMidMove_When_TheMoveLands_Then_TheNewDurationIsHandedBack);
        /// where a tween has, it replaces the one the tween puts back as it ends.
        /// </summary>
        internal static void WriteOwnDuration(VisualElement element, StyleList<TimeValue> duration)
        {
            if (s_suspensions.TryGetValue(element, out var suspension) && suspension.WroteTiming)
            {
                (suspension.OwnDuration, suspension.Stale) = (Copy(duration), true);
                WriteNarrowed(element, suspension);
            }
            else if (s_tweenTimings.TryGetValue(element, out var hold))
            {
                hold.Duration = Copy(duration);
            }
            else
            {
                element.style.transitionDuration = duration;
            }
        }

        /// <summary>
        /// Whether an owner other than an <see cref="ICarryingOwner"/> — a per-frame driver — holds a suspension of the
        /// element's transitions.
        /// </summary>
        internal static bool DriverSuspends(VisualElement element) =>
            s_suspensions.TryGetValue(element, out var suspension) && suspension.Owners.Any(owner => owner is not ICarryingOwner);

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
            var held = s_suspensions.TryGetValue(element, out var suspension);
            if (held && suspension.Owners.Count > 0)
            {
                element.style.transitionProperty = s_none;
                return;
            }
            element.style.transitionProperty = StyleKeyword.Null;
            if (held && suspension.Narrowers.Count > 0) WriteNarrowed(element, suspension);
        }

        // Whether the slot holds a value this class did not write, neither its suspension nor a narrowing, which is a
        // variant tween's transition-property (StyleAnimationScheduler.ApplyTransitionStyles): a driver can start or stop with the tween's list
        // already in the slot (FiberNodePatcher starts a swap before the class passes that attach and detach an
        // animate-* driver, and a layoutId move starts on the layout pass after its patch), and writing or
        // reverting it there would cancel that tween. What puts a still-held suspension back afterwards is the
        // tween's own teardown, through RestoreAfterForeignWrite. Compared by CONTENT rather than by list
        // identity, which the read back out of the slot does not preserve — MotionNativeTransitionGuardSuspensionTests
        // holds that.
        internal static bool HoldsAForeignValue(VisualElement element)
        {
            var current = element.style.transitionProperty;
            if (current.keyword != StyleKeyword.Null)
            {
                var value = current.value;
                return (value == null || value.Count != 1 || value[0] != s_noneName)
                    && !(s_suspensions.TryGetValue(element, out var suspension) && Wrote(suspension, value));
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
        /// An inline transition-property is not read here: the only one this package originates besides this class
        /// is a variant tween's, and both entry points deal with that before asking (see
        /// <see cref="HoldsAForeignValue"/>).
        /// One residual blind spot is accepted rather than fixed: a play asks once at its start, so a variant
        /// that turns on <c>transition-all</c> midway through one is not picked up until the next.
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

        private static readonly StyleLonghandSet s_radiusProperties = SetOf(
            StyleLonghand.BorderTopLeftRadius,
            StyleLonghand.BorderTopRightRadius,
            StyleLonghand.BorderBottomLeftRadius,
            StyleLonghand.BorderBottomRightRadius);

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
            if (declared.Overlaps(s_radiusProperties)) slots |= MotionTransitionSlots.Radius;
            if (declared.Contains(StyleLonghand.Visibility)) slots |= MotionTransitionSlots.Visibility;
            return slots;
        }

        /// <summary>The longhands a driver reporting <paramref name="slots"/> can write.</summary>
        private static StyleLonghandSet LonghandsOf(MotionTransitionSlots slots)
        {
            var set = StyleLonghandSet.Empty;
            if ((slots & MotionTransitionSlots.Opacity) != 0) set = set.Union(StyleLonghandSet.Of(StyleLonghand.Opacity));
            if ((slots & MotionTransitionSlots.Translate) != 0) set = set.Union(StyleLonghandSet.Of(StyleLonghand.Translate));
            if ((slots & MotionTransitionSlots.Scale) != 0) set = set.Union(StyleLonghandSet.Of(StyleLonghand.Scale));
            if ((slots & MotionTransitionSlots.Rotate) != 0) set = set.Union(StyleLonghandSet.Of(StyleLonghand.Rotate));
            if ((slots & MotionTransitionSlots.Color) != 0) set = set.Union(s_colorProperties);
            if ((slots & MotionTransitionSlots.Length) != 0) set = set.Union(s_lengthProperties);
            if ((slots & MotionTransitionSlots.Radius) != 0) set = set.Union(s_radiusProperties);
            if ((slots & MotionTransitionSlots.Visibility) != 0) set = set.Union(StyleLonghandSet.Of(StyleLonghand.Visibility));
            if ((slots & MotionTransitionSlots.Filter) != 0) set = set.Union(StyleLonghandSet.Of(StyleLonghand.Filter));
            if ((slots & MotionTransitionSlots.BackgroundPosition) != 0)
            {
                set = set.Union(SetOf(StyleLonghand.BackgroundPositionX, StyleLonghand.BackgroundPositionY));
            }
            return set;
        }

        private static readonly StylePropertyName s_allName = new("all");

        // Indexed by StyleLonghand.
        private static readonly StylePropertyName[] s_longhandNames = LonghandNames();

        private static StylePropertyName[] LonghandNames()
        {
            var names = new StylePropertyName[StyleUtilityProperties.LonghandCount];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = new StylePropertyName(StyleUtilityProperties.UssName((StyleLonghand)i));
            }
            return names;
        }

        // Rewrites the held transition-property list without the driven longhands — an `all` entry
        // becoming every other longhand, on that entry's timing — and rebuilds each companion list to match,
        // since those pair with transition-property by position.
        internal static void ExcludeFromHeldList(VisualElement element, StyleLonghandSet driven)
        {
            var held = element.style.transitionProperty.value;
            // The lists Narrow wrote leave out every narrower's longhands already, and are rewritten as those change.
            if (held == null || s_suspensions.TryGetValue(element, out var suspension) && Wrote(suspension, held))
            {
                return;
            }
            Exclude(element, held, driven);
        }

        private static void Exclude(VisualElement element, List<StylePropertyName> held, StyleLonghandSet driven, bool realign = true)
        {
            var names = new List<StylePropertyName>(held.Count);
            // The held entry each kept name takes its duration, easing and delay from.
            var sources = new List<int>(held.Count);
            // MUTANT_SURVIVES(equivalent): with nothing left out, the rewrite this skips writes back the names and
            // timings it read, in the order it read them.
            var changed = false;
            for (var i = 0; i < held.Count; i++)
            {
                if (held[i] == s_allName)
                {
                    changed = true;
                    for (var longhand = 0; longhand < s_longhandNames.Length; longhand++)
                    {
                        if (!driven.Contains((StyleLonghand)longhand))
                        {
                            names.Add(s_longhandNames[longhand]);
                            sources.Add(i);
                        }
                    }
                }
                else if (NamesADrivenLonghand(held[i], driven))
                {
                    changed = true;
                }
                else
                {
                    names.Add(held[i]);
                    sources.Add(i);
                }
            }
            if (!changed)
            {
                return;
            }
            // Counted before the write below: a list read out of a slot is refilled by that slot's next write, which
            // MotionNativeTransitionGuardSuspensionTests pins.
            var heldCount = held.Count;
            element.style.transitionProperty = names;
            if (!realign) return;
            WriteRealigned(element.style.transitionDuration.value, heldCount, sources,
                list => element.style.transitionDuration = list);
            WriteRealigned(element.style.transitionTimingFunction.value, heldCount, sources,
                list => element.style.transitionTimingFunction = list);
            WriteRealigned(element.style.transitionDelay.value, heldCount, sources,
                list => element.style.transitionDelay = list);
        }

        private static bool NamesADrivenLonghand(StylePropertyName name, StyleLonghandSet driven)
        {
            var longhand = Array.IndexOf(s_longhandNames, name);
            // MUTANT_SURVIVES(equivalent): index 0 is -unity-background-image-tint-color, which no slot's longhands
            // include, so reading it as absent changes no answer.
            return longhand >= 0 && driven.Contains((StyleLonghand)longhand);
        }

        // Leaves the list as it was where the slot holds none or it does not pair one-to-one with the held list.
        private static void WriteRealigned<T>(List<T> list, int heldCount, List<int> sources, Action<List<T>> write)
        {
            if (list == null || list.Count != heldCount)
            {
                return;
            }
            var realigned = new List<T>(sources.Count);
            foreach (var source in sources)
            {
                realigned.Add(list[source]);
            }
            write(realigned);
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
            if (!s_suspensions.TryGetValue(element, out var suspension)) return;
            suspension.Driven.Remove(owner);
            var suspended = suspension.Owners.Remove(owner);
            var narrowed = suspension.Narrowers.Remove(owner);
            if (narrowed) Unnarrow(element, suspension);
            // A narrowing still held takes the slot back from the suspension that ends here
            // (Given_ANarrowedMotionWhosePulseEndsMidMove_When_ItsBackgroundChanges_Then_TheBackgroundTransitions).
            if ((suspended || narrowed) && suspension.Owners.Count == 0)
            {
                if (!HoldsAForeignValue(element) && !Wrote(suspension, element.style.transitionProperty.value))
                {
                    element.style.transitionProperty = StyleKeyword.Null;
                }
                if (suspension.Narrowers.Count > 0) WriteNarrowed(element, suspension);
            }
            // The record goes with the last of its owners, narrowers and drivers
            // (Given_AMotionWhoseSpringDroveNothingItTransitions_When_ATweenEnds_Then_NoTransitionIsLeftInline).
            if (suspension.Owners.Count + suspension.Narrowers.Count + suspension.Driven.Count == 0) s_suspensions.Remove(element);
        }

        /// <summary>
        /// Writes the narrowed lists again where the element's rules have changed since they were written, called on
        /// each pass a layoutId projection draws the element
        /// (Given_ANarrowedMotionWhoseTransitionClassesChangeMidMove_When_ItsBackgroundChanges_Then_ItRunsOnTheNewTiming).
        /// </summary>
        internal static void Refresh(VisualElement element)
        {
            if (s_suspensions.TryGetValue(element, out var suspension) && suspension.Written != null) WriteNarrowed(element, suspension);
        }

        /// <summary>
        /// Whether a driver that took the element through <see cref="SuspendIfIntercepted"/> or
        /// <see cref="SyncSuspension(VisualElement, object, MotionTransitionSlots, bool)"/> writes the property frame by
        /// frame, whether or not it suspended the element's transitions.
        /// </summary>
        internal static bool Drives(VisualElement element, string property)
        {
            var longhand = Array.IndexOf(s_longhandNames, new StylePropertyName(property));
            if (longhand < 0 || !s_suspensions.TryGetValue(element, out var suspension)) return false;
            return suspension.Driven.Values.Any(slots => LonghandsOf(slots).Contains((StyleLonghand)longhand));
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
                s_tweenTimings.Remove(element);
            }
        }
    }
}
