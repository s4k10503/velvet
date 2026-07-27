using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The style slots a per-frame Motion driver can write, grouped the way the bundled <c>transition-*</c>
    /// utilities group them, so a play's driven set and an element's declared set intersect directly.
    /// </summary>
    [Flags]
    internal enum MotionTransitionSlots
    {
        None = 0,
        Opacity = 1 << 0,
        Translate = 1 << 1,
        Scale = 1 << 2,
        Rotate = 1 << 3,
        // background-color / color / border-color move together: every transition utility that names one names
        // all three, and no driver channel writes them independently of that grouping.
        Color = 1 << 4,
        // Every length-valued property. Only `transition-all` covers these, so a finer split would never change
        // an intersection result.
        Length = 1 << 5,
        All = Opacity | Translate | Scale | Rotate | Color | Length,
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
    /// worth paying where the conflict is real, so the decision is made from the element's own CLASS LIST — the
    /// transition utilities are a small closed set with known property sets, and reading class strings is how
    /// the rest of this feature derives its answers off-panel. A play suspends only when the slots it drives
    /// intersect what those classes leave transitionable (see <see cref="DeclaredSlots"/>): a
    /// <c>transition-colors</c> element running an opacity/translate play keeps its hover fade, while the same
    /// play on a <c>transition-transform</c> or <c>transition-all</c> element does suspend, because there the
    /// class covers the very slots the driver is writing.
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
        private static readonly List<StylePropertyName> s_none = new() { new StylePropertyName("none") };

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
        /// The slots the element's own classes leave natively transitionable.
        /// </summary>
        /// <remarks>
        /// UI Toolkit's INITIAL <c>transition-property</c> is <c>all</c> and its initial duration is 0s, so an
        /// element transitions everything as soon as anything gives it a duration — and nothing at all until
        /// then. That makes the answer three-way rather than a union: a <c>transition-*</c> utility that
        /// declares a property list (mirroring <c>_state_variants.uss</c> / <c>_effects.uss</c>) pins the set;
        /// otherwise a duration-only source (<c>transition-filter</c>, which deliberately sets duration and
        /// timing alone, or any <c>duration-*</c> utility, including the bracket form the resolver applies as an
        /// inline value rather than a class) leaves the initial <c>all</c> standing; otherwise there is no
        /// duration and nothing transitions.
        /// <para>
        /// Two residual blind spots, both accepted rather than fixed: an inline whole-property value written by
        /// something other than a duration utility (the scheduler's own tween swap sets one, though never on a
        /// spring or bezier play) is not read here, and the answer is computed once at play start, so a variant
        /// that turns on <c>transition-all</c> midway through a play is not picked up until the next one.
        /// </para>
        /// </remarks>
        internal static MotionTransitionSlots DeclaredSlots(VisualElement element)
        {
            var declaredByUtility = MotionTransitionSlots.None;
            var sawPropertyUtility = false;
            var sawDurationOnly = false;
            foreach (var core in element.GetClasses())
            {
                if (string.IsNullOrEmpty(core))
                {
                    continue;
                }
                if (core.StartsWith("duration-", StringComparison.Ordinal))
                {
                    sawDurationOnly = true;
                    continue;
                }
                var declared = core switch
                {
                    "transition-all" => MotionTransitionSlots.All,
                    "transition-none" => MotionTransitionSlots.None,
                    "transition-opacity" => MotionTransitionSlots.Opacity,
                    "transition-colors" => MotionTransitionSlots.Color,
                    "transition-colors-scale" => MotionTransitionSlots.Color | MotionTransitionSlots.Scale,
                    "transition-colors-scale-opacity"
                        => MotionTransitionSlots.Color | MotionTransitionSlots.Scale | MotionTransitionSlots.Opacity,
                    "transition-transform"
                        => MotionTransitionSlots.Translate | MotionTransitionSlots.Scale | MotionTransitionSlots.Rotate,
                    // Sets duration and timing only, so it leaves transition-property at its initial `all`.
                    "transition-filter" => MotionTransitionSlots.None,
                    _ => (MotionTransitionSlots?)null,
                };
                if (declared == null)
                {
                    continue;
                }
                if (core == "transition-filter")
                {
                    sawDurationOnly = true;
                    continue;
                }
                sawPropertyUtility = true;
                declaredByUtility |= declared.Value;
            }
            if (sawPropertyUtility)
            {
                return declaredByUtility;
            }
            // duration-[400ms] resolves to an inline value rather than a class, so the class scan cannot see it;
            // the inline slot can.
            return sawDurationOnly || element.style.transitionDuration.keyword != StyleKeyword.Null
                ? MotionTransitionSlots.All
                : MotionTransitionSlots.None;
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
                element.style.transitionProperty = StyleKeyword.Null;
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
