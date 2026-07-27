using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
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
    /// the declared list cannot be read off-panel, so the suspension is necessarily element-wide: for its
    /// duration the element's OTHER transitions land instantly too. That cost is only worth paying where the
    /// conflict is real, so only a play driving a color- or length-valued property suspends (see the drivers'
    /// own apply paths) — a play confined to opacity and the transform trio leaves the element's transitions
    /// exactly as its classes declare them.
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

        public static void Suspend(VisualElement element, object owner)
        {
            var suspension = s_suspensions.GetValue(element, static _ => new Suspension());
            if (suspension.Owners.Add(owner) && suspension.Owners.Count == 1)
            {
                element.style.transitionProperty = s_none;
            }
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
