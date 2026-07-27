using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Suspends the element's native USS transitions for as long as a per-frame driver owns its inline styles
    /// (<see cref="MotionSpringDriver"/> / <see cref="BezierTweenDriver"/> / <see cref="MotionLayoutIdDriver"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A driver writes the exact value the curve or the physics says the element should show THIS frame. If the
    /// element's own classes also declare a transition covering that property (<c>transition-all</c>,
    /// <c>transition-colors</c>, <c>transition-transform</c>, …), every one of those writes instead STARTS a
    /// fresh native transition toward the value, so the painted value trails the driver by the class's whole
    /// duration for the entire play and then jumps when the driver hands the slot back. The driven property must
    /// therefore have no native transition while the driver owns it.
    /// </para>
    /// <para>
    /// UI Toolkit's <c>transition-property</c> is a positive list — there is no "everything except these"
    /// spelling — and the property set a play drives is decided per play, so the suspension is element-wide
    /// rather than per-slot: a property the driver is NOT touching also lands instantly for the duration of the
    /// play. That matches Framer Motion, where a value driven per-frame and a CSS transition on the same element
    /// are mutually exclusive by construction. The suspension is an INLINE override, so restoring it is a single
    /// <see cref="StyleKeyword.Null"/> that hands the element straight back to whatever its classes declare.
    /// </para>
    /// </remarks>
    internal static class MotionNativeTransitionGuard
    {
        // A property name that resolves to no style property computes to zero transitions, which is exactly
        // "transition-property: none". A shared, never-mutated list: StyleList retains the reference as-is
        // (mirroring StyleAnimationScheduler's own transition-property: all list), and Restore releases it.
        private static readonly List<StylePropertyName> s_none = new() { new StylePropertyName("none") };

        public static void Suspend(VisualElement element) => element.style.transitionProperty = s_none;

        public static void Restore(VisualElement element) => element.style.transitionProperty = StyleKeyword.Null;
    }
}
