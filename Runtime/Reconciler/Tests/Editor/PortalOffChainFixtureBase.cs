using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.Tests.PortalOffChainSupport;

namespace Velvet.Tests
{
    /// <summary>
    /// The arrangement the fixtures about events reaching a portal's physical ancestors share: a clean log and
    /// portal registry, a root to mount into, and mounts that put the portal's target under an element beside
    /// the call site.
    /// </summary>
    internal abstract class PortalOffChainFixtureBase : PanelTestBase
    {
        protected VisualElement _root;
        protected MountedTree _other;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_log.Clear();
            s_showPortal = default;
            RuntimeStateProbe.ClearPortalRegistry();
            _root = new VisualElement();
            _window.rootVisualElement.Add(_root);
        }

        [TearDown]
        public override void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _other?.Dispose();
            _other = null;
            RuntimeStateProbe.ClearPortalRegistry();
            base.TearDown();
        }

        // The portal's target is put under "holder" once mounted, so "holder" holds the content without being
        // above the call site, which sits under "caller". Returns the portal's content.
        protected VisualElement MountHolderBesideCaller(
            System.Type holderType, FiberEventBinding[] holderEvents, System.Type callerType, FiberEventBinding[] callerEvents)
        {
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Motion(name: "holder", elementType: holderType, events: holderEvents,
                    children: new VNode[] { V.Div(name: "slot") }),
                V.Motion(name: "caller", elementType: callerType, events: callerEvents,
                    children: new VNode[] { V.Component(CallerRender) }),
            }));
            var target = new VisualElement();
            _root.Q<VisualElement>("slot").Add(target);
            FiberPortalRegistry.Register(TargetId, target);
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();
            return target.Q<VisualElement>("content");
        }

        // The field holds the portal's target without being above the call site. Returns the portal's content.
        protected VisualElement MountTextFieldAroundTarget(EventCallback<FocusInEvent> onFocus)
        {
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.TextField(name: "field", onFocus: onFocus),
                V.Motion(name: "caller",
                    events: new FiberEventBinding[] { new FocusInBinding { Handler = _ => s_log.Add("caller") } },
                    children: new VNode[] { V.Component(CallerRender) }),
            }));
            var target = new VisualElement();
            _root.Q<VisualElement>("field").Add(target);
            FiberPortalRegistry.Register(TargetId, target);
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();
            return target.Q<VisualElement>("content");
        }
    }
}
