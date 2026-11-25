#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // The engine's reads of which transitions run on an element, bound once as open-instance delegates for
    // StyleTransitionBehavior and RunningStyleTransition to share: whether any runs, and whether one of a property runs,
    // taking the int the engine's StylePropertyId is held as. Each answers null where nothing was bound, which
    // EngineMemberResolutionTests names, and from the first call that throws on, so that a caller falls back as it does
    // where nothing was bound rather than throwing.
    internal static class RunningStyleAnimations
    {
        // Not readonly: a delegate that throws is dropped.
        private static Func<VisualElement, bool>? s_any =
            EngineMember.HasRunningStyleAnimations.ResolveGetter<Func<VisualElement, bool>>();

        private static Func<VisualElement, int, bool>? s_of =
            EngineMember.HasRunningStyleAnimation.ResolveOpenDelegate<Func<VisualElement, int, bool>>();

        internal static bool? Any(VisualElement element)
        {
            if (s_any is not { } any)
            {
                return null;
            }
            try
            {
                return any(element);
            }
            catch (Exception)
            {
                s_any = null;
                return null;
            }
        }

        internal static bool? Of(VisualElement element, int id)
        {
            if (s_of is not { } of)
            {
                return null;
            }
            try
            {
                return of(element, id);
            }
            catch (Exception)
            {
                s_of = null;
                return null;
            }
        }
    }
}
