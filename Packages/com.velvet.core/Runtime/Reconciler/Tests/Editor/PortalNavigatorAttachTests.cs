using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a portal leaves of the focus navigator's deferred attach on a target with no panel yet:
    /// the hook it asked for goes with it, and one another caller asked for on the same element stays, so
    /// the navigator still attaches when that element reaches a panel. A portal binding the target again
    /// asks afresh.
    /// </summary>
    [TestFixture]
    internal sealed class PortalNavigatorAttachTests : PanelTestBase
    {
        private static VisualElement s_mountRoot;
        private static VisualElement s_container;
        private static StateUpdater<bool> s_setShown;

        public override void SetUp()
        {
            base.SetUp();
            s_mountRoot = new VisualElement();
            s_container = new VisualElement();
            s_setShown = default;
        }

        // The portal targets the element the tree is mounted on, which V.Mount has already asked the navigator
        // to attach to once it has a panel.
        [Component]
        private static VNode PortalIntoMountRootRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(name: "host", children: new VNode[]
            {
                shown ? V.Portal(s_mountRoot, children: new VNode[] { V.Div(name: "portal-child") }) : null,
            });
        }

        [Component]
        private static VNode PortalIntoContainerRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(name: "host", children: new VNode[]
            {
                shown ? V.Portal(s_container, children: new VNode[] { V.Div(name: "portal-child") }) : null,
            });
        }

        [Test]
        public void Given_APortalLeftAContainerWithNoPanel_When_ItBindsTheContainerAgainAndTheContainerReachesAPanel_Then_TheNavigatorAttachesThere()
        {
            // Arrange — the tree is mounted on an element that never reaches a panel, so only the container's
            // hook can attach the navigator to the window's.
            _mounted = V.Mount(s_mountRoot, V.Component(PortalIntoContainerRender, key: "host"));
            s_setShown.Invoke(false);
            _mounted.FlushStateForTest();
            s_setShown.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            _window.rootVisualElement.Add(s_container);

            // Assert — the portal's child is read beside the attachment, because a portal that never bound the
            // container again asks nothing either.
            Assert.That(
                (s_container.Q<VisualElement>("portal-child") != null,
                    _mounted.Root.Reconciler.Context.NavigatorAttachments.ContainsKey(s_container.panel.visualTree)),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base's portal asks nothing of the navigator, so the mount's own
        // deferred attach is the only one on the root.
        [Test]
        public void Given_APortalIntoAMountRootWithNoPanel_When_ThePortalLeavesAndTheRootReachesAPanel_Then_TheNavigatorAttachesThere()
        {
            // Arrange
            _mounted = V.Mount(s_mountRoot, V.Component(PortalIntoMountRootRender, key: "host"));
            var portalMounted = s_mountRoot.Q<VisualElement>("portal-child") != null;
            s_setShown.Invoke(false);
            _mounted.FlushStateForTest();

            // Act
            _window.rootVisualElement.Add(s_mountRoot);

            // Assert — the portal's child is read beside the attachment, because a portal that never bound the
            // root releases nothing either.
            Assert.That(
                (portalMounted, _mounted.Root.Reconciler.Context.NavigatorAttachments.ContainsKey(s_mountRoot.panel.visualTree)),
                Is.EqualTo((true, true)));
        }
    }
}
