using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// What the fixtures about events reaching a portal's physical ancestors share: the portal's component, the
    /// log their handlers write, and the helpers that bind handlers and dispatch events into the content.
    /// </summary>
    internal static class PortalOffChainSupport
    {
        internal const string TargetId = "off-chain-target";

        internal static readonly List<string> s_log = new();
        internal static StateUpdater<bool> s_showPortal;

        [Component]
        internal static VNode CallerRender()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_showPortal = setShow;
            return V.When(show, () => V.Portal(TargetId, children: new VNode[] { V.Div(name: "content") }));
        }

        internal static FiberEventBinding[] PointerDown(string entry, bool capture) =>
            new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_log.Add(entry), Capture = capture } };

        internal static FiberEventBinding[] Wheel(string entry, bool capture) =>
            new FiberEventBinding[] { new WheelBinding { Handler = _ => s_log.Add(entry), Capture = capture } };

        internal static FiberEventBinding[] Change(string entry) =>
            new FiberEventBinding[] { new ChangeEventBinding<bool> { Handler = _ => s_log.Add(entry) } };

        internal static void Dispatch<TEvent>(TEvent evt, VisualElement target) where TEvent : EventBase<TEvent>, new()
        {
            using (evt)
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        internal static void DispatchFocus<TEvent>(VisualElement target) where TEvent : FocusEventBase<TEvent>, new()
        {
            using (var evt = FocusEventBase<TEvent>.GetPooled(target, null, FocusChangeDirection.unspecified, target.focusController))
            {
                target.SendEvent(evt);
            }
        }

        internal static System.Action RegisterTarget(VisualElement element)
        {
            FiberPortalRegistry.Register(TargetId, element);
            return () => FiberPortalRegistry.Unregister(TargetId);
        }
    }
}
