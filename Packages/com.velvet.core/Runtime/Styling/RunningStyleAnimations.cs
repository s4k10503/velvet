#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // The engine's reads of which transitions run on an element, bound once as open-instance delegates for
    // StyleTransitionBehavior and RunningStyleTransition to share: whether any runs, and whether one of a property runs,
    // taking the int the engine's StylePropertyId is held as. Each is null where the runtime refuses the bind, which
    // EngineMemberResolutionTests names.
    internal static class RunningStyleAnimations
    {
        internal static readonly Func<VisualElement, bool>? Any =
            EngineMember.HasRunningStyleAnimations.ResolveGetter<Func<VisualElement, bool>>();

        internal static readonly Func<VisualElement, int, bool>? Of =
            EngineMember.HasRunningStyleAnimation.ResolveOpenDelegate<Func<VisualElement, int, bool>>();
    }
}
