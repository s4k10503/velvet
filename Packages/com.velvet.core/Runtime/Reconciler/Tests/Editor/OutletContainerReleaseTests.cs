using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins per-element release of the Outlet-container identity set. Every Outlet mount registers
    /// its layout-passthrough container so the context spine can identify Outlet hosts, but unlike
    /// every other element-keyed side table the set had no per-element removal — only the
    /// whole-reconciler dispose cleared it. Each route change that destroys and recreates an Outlet
    /// then pinned the dead container element for the remaining lifetime of the mount, growing
    /// without bound over a long session.
    /// </summary>
    [TestFixture]
    internal sealed class OutletContainerReleaseTests
    {
        private readonly record struct ToggleState(bool Show);

        private sealed class ToggleStore : Store<ToggleState>
        {
            public ToggleStore() : base(new ToggleState(true)) { }
            public void Set(bool show) => SetState(_ => new ToggleState(show));
            protected override void ResetCore() => SetState(_ => new ToggleState(true));
        }

        private static ToggleStore s_store;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        [Component]
        private static VNode App()
        {
            var show = Hooks.UseStore(s_store, s => s.Show);
            return V.Div(name: "app", children: show
                ? new VNode[] { V.Outlet() }
                : Array.Empty<VNode>());
        }

        [Test]
        public void Given_AMountedOutlet_When_ItUnmounts_Then_ItsContainerRegistrationIsReleased()
        {
            // Arrange — a mounted Outlet registers its container in the identity set.
            using var store = new ToggleStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(App, key: "app"));
            var context = mounted.Root.Reconciler.Context;
            var scheduler = context.BatchScheduler;
            Assume.That(context.OutletContainers.Count, Is.EqualTo(1),
                "Precondition: the mounted Outlet's container is registered");

            // Act — the Outlet unmounts.
            store.Set(false);
            scheduler.DrainImmediateForTest();

            // Assert — the registration is gone, so dead containers cannot accumulate across route changes.
            Assert.AreEqual(0, context.OutletContainers.Count);
        }
    }
}
