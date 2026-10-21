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
        // Neither has a SlotsOf arm yet: the driver reports the slot it writes, and an element reaches
        // both through `all`. `.transition-filter` is the one utility that could name a declared side
        // finer, which is a separate change from giving the driver a flag.
        Filter = 1 << 6,
        BackgroundPosition = 1 << 7,
        // MotionLayoutIdDriver hides a layoutId member that another holder leads through `visibility`.
        Visibility = 1 << 8,
        All = Opacity | Translate | Scale | Rotate | Color | Length | Filter | BackgroundPosition | Visibility,
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
        // "transition-property: none".
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
            // The owners NarrowIfIntercepted found nothing to narrow for, with their slots and the rules it read.
            public readonly Dictionary<object, (MotionTransitionSlots Slots, long? Rules)> Undecided = new();
            // Where Narrow writes the duration, delay and curve lists too (WriteNarrowed), through MotionTweenTiming's
            // narrowed layer, and the element's own lists it derived them from, so that a list code writes over them
            // since is taken (OwnTimingChanged).
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
            => SuspendIfIntercepted(element, owner, drivenSlots, LonghandsOf(drivenSlots & MotionTransitionSlots.Length));

        internal static void SuspendIfIntercepted(VisualElement element, object owner, MotionTransitionSlots drivenSlots,
            StyleLonghandSet drivenLengths)
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
                ExcludeFromHeldList(element, LonghandsOf(drivenSlots & ~MotionTransitionSlots.Length).Union(drivenLengths));
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
        /// Two departures from <see cref="SuspendIfIntercepted(VisualElement, object, MotionTransitionSlots)"/>,
        /// both because this runs on a patch rather than at a play's start. The inline transition-duration is not read (see
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
        /// transitions of what a layout animation does not write. Where the element's cascade transitions none of those
        /// longhands over some time — its own classes deciding where the cascade cannot be read — this is a no-op,
        /// decided again on a pass that finds the element's rules changed (<see cref="Refresh"/>).
        /// </summary>
        /// <remarks>
        /// The element's transitions are read off its cascade (<see cref="StyleCascade.Lists"/>) and written inline
        /// without those longhands, an <c>all</c> becoming the other longhands the utility tables name. The duration,
        /// delay and curve lists the element holds inline are its own, and are handed back as the last narrower lets go.
        /// A variant tween's list in the slot is narrowed instead, and a play's suspension left to suspend everything;
        /// either is replaced by the narrowed lists as it ends (<see cref="RestoreAfterForeignWrite"/>,
        /// <see cref="Release"/>).
        /// </remarks>
        public static void NarrowIfIntercepted(VisualElement element, object owner, MotionTransitionSlots drivenSlots)
        {
            var driven = LonghandsOf(drivenSlots);
            ExcludeFromHeldList(element, driven);
            if (Intercepts(element, driven) ?? (DeclaredSlots(element) & drivenSlots) != MotionTransitionSlots.None)
            {
                Narrow(element, owner, drivenSlots);
                return;
            }
            // Decided again on a pass that finds the element's rules changed
            // (Given_AFollowerGivenTransitionClassesAsItsLeadLeaves_When_ItsMoveRuns_Then_ItIsDrawnWhereItIsWritten).
            // Kept with the slots the owner asked about before
            // (Given_AFollowerGivenTransitionClassesAsALargerLeadLeaves_When_ItsMoveRuns_Then_ItIsDrawnWhereItIsWritten).
            var undecided = s_suspensions.GetValue(element, static _ => new Suspension()).Undecided;
            undecided[owner] = ((undecided.TryGetValue(owner, out var asked) ? asked.Slots : MotionTransitionSlots.None) | drivenSlots,
                StyleCascade.RulesHash(element));
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
            if (read == null) ScheduleRenarrow(element);
            var lists = read ?? new TransitionLists(null, null, null, null);
            var count = lists.Properties.Count;
            var excluded = StyleLonghandSet.Empty;
            foreach (var longhands in suspension.Narrowers.Values)
            {
                excluded = excluded.Union(longhands);
            }
            var names = new List<StylePropertyName>(lists.Properties);
            var sources = Exclude(names, excluded);
            style.transitionProperty = names;
            // Written only where one of them pairs its entries with the properties by position, so that elsewhere a class
            // changing them is taken by the style pass that sees it
            // (Given_ANarrowedMotionWhoseTransitionClassesChangeMidMove_When_ItsBackgroundChanges_Then_ItRunsOnTheNewTiming);
            // a playing tween's stay on top of them, and the element's own come back as the narrowing ends.
            if (!MotionTweenTiming.Playing(element))
            {
                if (Math.Max(lists.Durations.Count, Math.Max(lists.Delays.Count, lists.Curves.Count)) > 1)
                {
                    (suspension.OwnDuration, suspension.OwnDelay, suspension.OwnCurve) = MotionTweenTiming.Own(element);
                    MotionTweenTiming.Narrow(element, Realigned(Cycle(lists.Durations, count), count, sources),
                        Realigned(Cycle(lists.Curves, count), count, sources), Realigned(Cycle(lists.Delays, count), count, sources));
                    suspension.WroteTiming = true;
                }
                else
                {
                    HandBackTiming(element, suspension);
                }
            }
            (suspension.Written, suspension.Rules, suspension.Stale) = (new List<StylePropertyName>(names), rules, read == null);
        }

        // Kept out of WriteNarrowed: a lambda there capturing its parameter would allocate its closure on every call,
        // the early exits a steady pass takes included
        // (Given_ANarrowedLeadMidMove_When_ItsProjectionRefreshes_Then_NothingIsAllocated).
        private static void ScheduleRenarrow(VisualElement element) => element.schedule.Execute(() => Renarrow(element));

        private static void Renarrow(VisualElement element)
        {
            if (!s_suspensions.TryGetValue(element, out var suspension)) return;
            if (suspension.Narrowers.Count > 0) WriteNarrowed(element, suspension);
        }

        private static bool Wrote(Suspension suspension, List<StylePropertyName> names) =>
            suspension.Written != null && names != null && Same(new StyleList<StylePropertyName>(names), new StyleList<StylePropertyName>(suspension.Written));

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
            // A tween playing over the narrowing puts the element's own back as it ends, rather than the narrowing's
            // (Given_ANarrowedMotionWithPairedTimingWhosePoseTweenOutlastsItsMove_When_TheTweenEnds_Then_NoTimingIsLeftInline),
            // and none of the lists is the narrowing's from here on, whoever holds the slots
            // (Given_ANarrowedMotionDrivenByAnAnimateClassWhosePoseTweenOutlastsItsMove_When_ItTakesAnArbitraryDuration_Then_NoTransitionIsWrittenInline).
            HandBackTiming(element, suspension);
            if (Wrote(suspension, slot.value)) style.transitionProperty = suspension.Owners.Count > 0 ? s_none : StyleKeyword.Null;
            suspension.Written = null;
        }

        /// <summary>
        /// The duration, delay and curve lists the element runs its transitions by inline: its own, which
        /// <see cref="Narrow"/> keeps beneath the lists it writes while no tween plays on the element, else the slots', a
        /// tween's while one holds them.
        /// </summary>
        internal static (StyleList<TimeValue> Duration, StyleList<TimeValue> Delay, StyleList<EasingFunction> Curve) OwnTiming(
            VisualElement element)
        {
            if (!s_suspensions.TryGetValue(element, out var suspension) || !suspension.WroteTiming || MotionTweenTiming.Playing(element))
            {
                return (element.style.transitionDuration, element.style.transitionDelay, element.style.transitionTimingFunction);
            }
            return MotionTweenTiming.Own(element);
        }

        // Whether the element's own lists have changed since the narrowed ones were derived from them, as when its code
        // writes one over them, which is the element's own from then on
        // (Given_ANarrowedMotionWhoseCodeWritesItsDelayMidMove_When_TheMoveLands_Then_ThatDelayIsHandedBack).
        private static bool OwnTimingChanged(VisualElement element, Suspension suspension)
        {
            if (!suspension.WroteTiming || MotionTweenTiming.Playing(element)) return false;
            var (duration, delay, curve) = MotionTweenTiming.Own(element);
            return !Same(duration, suspension.OwnDuration) || !Same(delay, suspension.OwnDelay) || !Same(curve, suspension.OwnCurve);
        }

        // Indexed rather than through SequenceEqual, whose enumerators are allocated on each comparison.
        private static bool Same<T>(StyleList<T> a, StyleList<T> b)
        {
            var (x, y) = (a.value ?? NoList<T>.Value, b.value ?? NoList<T>.Value);
            if (a.keyword != b.keyword || x.Count != y.Count) return false;
            for (var i = 0; i < x.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(x[i], y[i])) return false;
            }
            return true;
        }

        private static class NoList<T>
        {
            public static readonly List<T> Value = new();
        }

        private static void HandBackTiming(VisualElement element, Suspension suspension)
        {
            if (!suspension.WroteTiming) return;
            MotionTweenTiming.Unnarrow(element);
            suspension.WroteTiming = false;
        }

        /// <summary>
        /// Whether a driver holds a suspension of the element's transitions.
        /// </summary>
        internal static bool DriverSuspends(VisualElement element) =>
            s_suspensions.TryGetValue(element, out var suspension) && suspension.Owners.Count > 0;

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
            // Called on every pass a layoutId projection draws, so a list that names none of the driven longhands is left
            // before anything is copied.
            if (!Names(held, driven)) return;
            // Counted before the write below: a list read out of a slot is refilled by that slot's next write, which
            // MotionNativeTransitionGuardSuspensionTests pins.
            var heldCount = held.Count;
            var names = new List<StylePropertyName>(held);
            if (Exclude(names, driven) is not { } sources)
            {
                return;
            }
            element.style.transitionProperty = names;
            MotionTweenTiming.Write(element, Realigned(element.style.transitionDuration.value, heldCount, sources),
                Realigned(element.style.transitionTimingFunction.value, heldCount, sources),
                Realigned(element.style.transitionDelay.value, heldCount, sources));
        }

        // Whether the list holds `all` or a driven longhand, which is what makes Exclude take something out.
        private static bool Names(List<StylePropertyName> held, StyleLonghandSet driven)
        {
            for (var i = 0; i < held.Count; i++)
            {
                if (held[i] == s_allName || NamesADrivenLonghand(held[i], driven)) return true;
            }
            return false;
        }

        // Takes the driven longhands out of names in place, and returns, for each name left, the entry of the list as
        // given it takes its duration, easing and delay from; null, the list left as it was, where nothing is taken out.
        private static List<int>? Exclude(List<StylePropertyName> names, StyleLonghandSet driven)
        {
            var held = names.ToArray();
            var sources = new List<int>(held.Length);
            // MUTANT_SURVIVES(equivalent): with nothing left out, the list rebuilt below is the list as given.
            // Its names come back in the order read, each taking its timing from its own entry.
            var changed = false;
            names.Clear();
            for (var i = 0; i < held.Length; i++)
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
            return changed ? sources : null;
        }

        // Whether the element's transitions, as its cascade gives them, run a longhand of driven over some time, named
        // itself or through `all`; null where the cascade cannot be read.
        private static bool? Intercepts(VisualElement element, StyleLonghandSet driven)
        {
            if (StyleCascade.Lists(element) is not { } lists) return null;
            for (var i = 0; i < lists.Properties.Count; i++)
            {
                var name = lists.Properties[i];
                // The duration and delay pair with the name by position, repeating as UI Toolkit repeats them, and an entry
                // runs where its duration, or its delay, is positive
                // (Given_AMotionWhoseStylesheetDelaysAZeroDurationTranslate_When_ItsMoveRuns_Then_ItIsDrawnWhereItIsWritten).
                var duration = lists.Durations.Count > 0 ? StyleFilterTransitionDriver.Milliseconds(lists.Durations[i % lists.Durations.Count]) : 0;
                var delay = lists.Delays.Count > 0 ? StyleFilterTransitionDriver.Milliseconds(lists.Delays[i % lists.Delays.Count]) : 0;
                var timed = Math.Max(0, duration) + delay > 0;
                if (timed && (name == s_allName || NamesADrivenLonghand(name, driven))) return true;
            }
            return false;
        }

        private static bool NamesADrivenLonghand(StylePropertyName name, StyleLonghandSet driven)
        {
            var longhand = Array.IndexOf(s_longhandNames, name);
            // MUTANT_SURVIVES(equivalent): index 0 is -unity-background-image-tint-color, which no slot's longhands
            // include, so reading it as absent changes no answer.
            return longhand >= 0 && driven.Contains((StyleLonghand)longhand);
        }

        // None, leaving the list as it was, where the slot holds none or it does not pair one-to-one with the held list;
        // the list itself where sources is null, Exclude having taken nothing out.
        private static List<T> Realigned<T>(List<T> list, int heldCount, List<int> sources)
        {
            if (list == null || list.Count != heldCount)
            {
                return null;
            }
            if (sources == null) return list;
            var realigned = new List<T>(sources.Count);
            foreach (var source in sources)
            {
                realigned.Add(list[source]);
            }
            return realigned;
        }

        internal static StyleLonghandSet SetOf(params StyleLonghand[] longhands)
        {
            var set = StyleLonghandSet.Empty;
            foreach (var longhand in longhands)
            {
                set = set.Union(StyleLonghandSet.Of(longhand));
            }
            return set;
        }

        internal static VisualElement? SyncLayoutOwner<TChannel>(VisualElement element, object owner,
            VisualElement? previous, List<TChannel>? lengths, Func<TChannel, ArbitraryProperty> property,
            MotionTransitionSlots drivenSlots)
        {
            var next = LayoutOwner(element, lengths, property);
            if (ReferenceEquals(previous, next)) return previous;
            if (previous != null) Release(previous, owner);
            if (next != null) SuspendIfIntercepted(next, owner, MotionTransitionSlots.Length,
                LengthLonghands(element, next, lengths, property));
            else if (previous != null) SuspendIfIntercepted(element, owner, drivenSlots,
                LengthLonghands(element, element, lengths, property));
            return next;
        }

        internal static StyleLonghandSet LengthLonghands<TChannel>(VisualElement element, VisualElement styleOwner,
            List<TChannel>? lengths, Func<TChannel, ArbitraryProperty> property)
        {
            var longhands = StyleLonghandSet.Empty;
            if (lengths == null) return longhands;
            foreach (var channel in lengths)
            {
                var name = property(channel);
                if (ReferenceEquals(ClipPathLayoutBox.StyleFor(element, name), styleOwner.style))
                {
                    longhands = longhands.Union(StyleArbitraryLonghands.Of(name));
                }
            }
            return longhands;
        }

        private static VisualElement? LayoutOwner<TChannel>(VisualElement element, List<TChannel>? lengths,
            Func<TChannel, ArbitraryProperty> property)
        {
            if (lengths == null) return null;
            foreach (var channel in lengths)
            {
                if (!ReferenceEquals(ClipPathLayoutBox.StyleFor(element, property(channel)), element.style))
                {
                    return ClipPathLayoutBox.Of(element);
                }
            }
            return null;
        }

        /// <summary>Drops one owner, handing the element back to the cascade once the LAST owner has let go.</summary>
        public static void Release(VisualElement element, object owner)
        {
            if (!s_suspensions.TryGetValue(element, out var suspension)) return;
            suspension.Driven.Remove(owner);
            suspension.Undecided.Remove(owner);
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
            // The record goes with the last of its owners, narrowers, drivers and undecided owners
            // (Given_ADriverWhoseSlotsAnElementsClassesDoNotTransition_When_ItIsReleased_Then_TheGuardKeepsNoRecordOfTheElement).
            if (suspension.Owners.Count + suspension.Narrowers.Count + suspension.Driven.Count + suspension.Undecided.Count == 0) s_suspensions.Remove(element);
        }

        /// <summary>
        /// Hands <paramref name="from"/>'s narrowing on to <paramref name="to"/>.
        /// </summary>
        internal static void Transfer(VisualElement element, object from, object to)
        {
            if (!s_suspensions.TryGetValue(element, out var suspension)) return;
            if (suspension.Narrowers.Remove(from, out var longhands)) suspension.Narrowers[to] = longhands;
            if (suspension.Undecided.Remove(from, out var undecided)) suspension.Undecided[to] = undecided;
        }

        /// <summary>
        /// Narrows <paramref name="owner"/> to the longhands of <paramref name="drivenSlots"/> alone, for one that has
        /// stopped writing some of what it narrowed; a no-op for an owner that narrows nothing.
        /// </summary>
        internal static void NarrowTo(VisualElement element, object owner, MotionTransitionSlots drivenSlots)
        {
            if (!s_suspensions.TryGetValue(element, out var suspension)) return;
            // Nor is a slot it no longer writes decided again
            // (Given_ALeadWhoseRotateRunsOnPastALandingWithItsTranslateUnnarrowed_When_ItsClassesChangeAndItsTranslateMoves_Then_TheTranslateTransitions).
            if (suspension.Undecided.TryGetValue(owner, out var undecided))
            {
                suspension.Undecided[owner] = (undecided.Slots & drivenSlots, undecided.Rules);
            }
            if (!suspension.Narrowers.ContainsKey(owner)) return;
            (suspension.Narrowers[owner], suspension.Stale) = (LonghandsOf(drivenSlots), true);
            WriteNarrowed(element, suspension);
        }

        /// <summary>
        /// Writes the narrowed lists again where the element's rules have changed since they were written, called on
        /// each pass a layoutId projection draws the element
        /// (Given_ANarrowedMotionWhoseTransitionClassesChangeMidMove_When_ItsBackgroundChanges_Then_ItRunsOnTheNewTiming)
        /// and each step it runs its rotate on past its landing.
        /// </summary>
        internal static void Refresh(VisualElement element)
        {
            if (!s_suspensions.TryGetValue(element, out var suspension)) return;
            if (suspension.Undecided.Count > 0) Redecide(element, suspension);
            if (suspension.Written == null) return;
            // A variant swap's list holds what a narrower writes until taken out again, as LayoutIdLook.Narrow takes rotate
            // out (Given_ANarrowedLayoutIdMotionWhosePoseSwapsMidMove_When_ItsMoveRuns_Then_ItIsDrawnWhereItIsWritten).
            var narrowed = StyleLonghandSet.Empty;
            foreach (var longhands in suspension.Narrowers.Values)
            {
                narrowed = narrowed.Union(longhands);
            }
            ExcludeFromHeldList(element, narrowed);
            if (OwnTimingChanged(element, suspension)) suspension.Stale = true;
            WriteNarrowed(element, suspension);
        }

        private static readonly List<object> s_redecided = new();

        private static void Redecide(VisualElement element, Suspension suspension)
        {
            var rules = StyleCascade.RulesHash(element);
            foreach (var (owner, (_, read)) in suspension.Undecided)
            {
                if (read != rules) s_redecided.Add(owner);
            }
            foreach (var owner in s_redecided)
            {
                var slots = suspension.Undecided[owner].Slots;
                suspension.Undecided.Remove(owner);
                if (slots != MotionTransitionSlots.None) NarrowIfIntercepted(element, owner, slots);
            }
            s_redecided.Clear();
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
            foreach (var slots in suspension.Driven.Values)
            {
                if (LonghandsOf(slots).Contains((StyleLonghand)longhand)) return true;
            }
            return false;
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
                MotionTweenTiming.Forget(element);
            }
        }
    }
}
