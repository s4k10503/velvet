using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how many synthetic-bubbling bridges a portal target carries: one, however many portals resolve to it
    /// and however often they unmount and mount again, because each bridge delivers a logical ancestor's handler
    /// once more.
    /// </summary>
    [TestFixture]
    internal sealed class SamePanelPortalBridgeCountTests : PanelTestBase
    {
        private const string TargetId = "bridge-count-target";

        private static StateUpdater<bool> s_setShown;

        private VisualElement _target;
        private int _delivered;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            RuntimeStateProbe.ClearPortalRegistry();
            s_setShown = default;
            _delivered = 0;
            _target = new VisualElement();
            _window.rootVisualElement.Add(_target);
            FiberPortalRegistry.Register(TargetId, _target);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            RuntimeStateProbe.ClearPortalRegistry();
        }

        // Portal content is a component rather than a bare element, as in SamePanelPortalBubblingTests: the
        // logical chain the bridge follows is stamped onto the component fibers a portal mounts.
        [Component]
        private static VNode TwoPortalsOnOneTarget() => V.Div(children: new VNode[]
        {
            V.Portal(TargetId, key: "first", children: new VNode[] { V.Component(FirstChild) }),
            V.Portal(TargetId, key: "second", children: new VNode[] { V.Component(SecondChild) }),
        });

        [Component]
        private static VNode FirstChild() => V.Div(name: "first");

        [Component]
        private static VNode SecondChild() => V.Div(name: "second");

        [Component]
        private static VNode ToggledChild() => V.Div(name: "toggled");

        [Component]
        private static VNode TogglingPortal()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(children: new VNode[]
            {
                shown ? V.Portal(TargetId, key: "p", children: new VNode[] { V.Component(ToggledChild) }) : null,
            });
        }

        private void MountUnderCountingAncestor(VNode tree)
        {
            var root = new VisualElement();
            _window.rootVisualElement.Add(root);
            var binding = new PointerDownBinding { Handler = _ => _delivered++ };
            _mounted = V.Mount(root, V.Motion(
                name: "counting-ancestor",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { tree }));
        }

        private static void DispatchPointerDown(VisualElement element)
        {
            using var evt = PointerDownEvent.GetPooled();
            evt.target = element;
            element.SendEvent(evt);
        }

        // GREEN_ON_BASE(characterization): the base already bridges a target once for every portal on it.
        [Test]
        public void Given_TwoPortalsOnOneTarget_When_AChildOfTheFirstIsPressed_Then_TheLogicalAncestorHearsItOnce()
        {
            // Arrange
            MountUnderCountingAncestor(V.Component(TwoPortalsOnOneTarget, key: "host"));

            // Act
            DispatchPointerDown(_target.Q<VisualElement>("first"));

            // Assert
            Assert.That(_delivered, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base already detaches the bridge when the last portal leaves.
        [Test]
        public void Given_APortalUnmountedAndMountedAgainOnItsTarget_When_ItsChildIsPressed_Then_TheLogicalAncestorHearsItOnce()
        {
            // Arrange — the unmount leaves no portal on the target, so the mount after it bridges the target anew.
            MountUnderCountingAncestor(V.Component(TogglingPortal, key: "host"));
            s_setShown.Invoke(false);
            _mounted.FlushStateForTest();
            s_setShown.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            DispatchPointerDown(_target.Q<VisualElement>("toggled"));

            // Assert
            Assert.That(_delivered, Is.EqualTo(1));
        }
    }
}
