using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a portal into a container Velvet renders keeps addressing its own children while
    /// the container's own children change around it, the way <c>createPortal</c>'s children stay put
    /// while React renders the container's other children. The container's own rows sit ahead of the
    /// portal's, so each case changes how many there are and then patches the portal — or a component
    /// inside it — and reads where the patch landed.
    /// </summary>
    internal sealed class PortalContainerOwnRowsTests
    {
        private const string TargetId = "container-own-rows";

        private MountedTree? _mounted;
        private VisualElement _root = null!;
        private static string[] s_initialOwn = Array.Empty<string>();
        private static bool s_ownRowsFromComponent;
        private static bool s_portalChildIsComponent;
        private static Action<string[]>? s_setOwn;
        private static Action<string[]>? s_setComponentRows;
        private static Action<string>? s_setPortalChild;
        private static Action<int>? s_changePortalComponent;

        [SetUp]
        public void SetUp()
        {
            s_initialOwn = Array.Empty<string>();
            s_ownRowsFromComponent = false;
            s_portalChildIsComponent = false;
            s_setOwn = null;
            s_setComponentRows = null;
            s_setPortalChild = null;
            s_changePortalComponent = null;
            s_setRows = null;
            RuntimeStateProbe.ClearPortalRegistry();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            RuntimeStateProbe.ClearPortalRegistry();
        }

        private static string Names(VisualElement parent) =>
            string.Join("|", parent.Children().Select(child => child.name));

        private static VNode?[] Rows(string[] names) => names.Select(name => (VNode?)V.Div(name: name)).ToArray();

        private static Action RegisterContainer(VisualElement element)
        {
            FiberPortalRegistry.Register(TargetId, element);
            return () => FiberPortalRegistry.Unregister(TargetId);
        }

        [Component]
        private static VNode HostRender()
        {
            var (own, setOwn) = Hooks.UseState(s_initialOwn);
            s_setOwn = setOwn;
            var (portalChild, setPortalChild) = Hooks.UseState("p");
            s_setPortalChild = setPortalChild;
            var ownChildren = s_ownRowsFromComponent
                ? new VNode?[] { V.Component(ComponentRowsRender, key: "rows") }
                : Rows(own);
            VNode portalContent = s_portalChildIsComponent
                ? V.Component(PortalComponentRender, key: "portal-component")
                : V.Div(name: portalChild);
            return V.Fragment(children: new VNode?[]
            {
                V.Div(name: "container", refCallback: RegisterContainer, children: ownChildren),
                V.Portal(TargetId, children: new VNode?[] { portalContent }),
            });
        }

        [Component]
        private static VNode ComponentRowsRender()
        {
            var (rows, setRows) = Hooks.UseState(s_initialOwn);
            s_setComponentRows = setRows;
            return V.Fragment(children: Rows(rows));
        }

        [Component]
        private static VNode PortalComponentRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_changePortalComponent = setTick;
            return V.Div(name: tick == 0 ? "content" : "changed");
        }

        // Mounts the host and runs the render the container's registration asks for, which mounts the portal.
        private VisualElement MountContainer()
        {
            _mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            Flush();
            return _root.Q<VisualElement>("container");
        }

        private void Flush()
        {
            _mounted!.FlushStateForTest();
            _mounted.FlushEffectsForTest();
        }

        // Each render's rows, grown together by the one state write, so the portal patches inside the
        // container's own reconcile.
        private static Action<(string[] Own, string[] Portal)>? s_setRows;

        private static string NamedChildren(VisualElement parent) =>
            string.Join("|", parent.Children().Select(child => child.name).Where(name => name.Length > 0));

        private static Action Register(string id, VisualElement element)
        {
            FiberPortalRegistry.Register(id, element);
            return () => FiberPortalRegistry.Unregister(id);
        }

        [Component]
        private static VNode SelfTargetRender()
        {
            var (rows, setRows) = Hooks.UseState((Own: new[] { "a" }, Portal: new[] { "p1" }));
            s_setRows = setRows;
            // The portal is the container's first own child and targets the container itself.
            return V.Div(name: "container", refCallback: element => Register("self", element),
                children: new VNode?[] { V.Portal("self", children: Rows(rows.Portal)) }.Concat(Rows(rows.Own)).ToArray());
        }

        [Test]
        public void Given_APortalInsideTheContainerItTargets_When_ItAndTheContainersRowsGrowInOneRender_Then_ItsNextPatchLandsOnItsChildren()
        {
            // Arrange — the portal's growth is counted by its own patch, which runs inside the container's
            // reconcile, and the container's own growth by the container: one render grows both.
            _mounted = V.Mount(_root, V.Component(SelfTargetRender, key: "host"));
            Flush();
            var container = _root.Q<VisualElement>("container");
            s_setRows!.Invoke((new[] { "a", "b" }, new[] { "p1", "p2" }));
            Flush();

            // Act
            s_setRows!.Invoke((new[] { "a", "b" }, new[] { "q1", "q2" }));
            Flush();

            // Assert
            Assert.That(NamedChildren(container), Is.EqualTo("a|b|q1|q2"));
        }

        [Component]
        private static VNode TwoTargetsRender()
        {
            var (rows, setRows) = Hooks.UseState((Own: new[] { "b1" }, Portal: new[] { "a1", "a2", "a3", "a4", "a5" }));
            s_setRows = setRows;
            var (portalChild, setPortalChild) = Hooks.UseState("p");
            s_setPortalChild = setPortalChild;
            return V.Fragment(children: new VNode?[]
            {
                V.Div(name: "first", refCallback: element => Register("first", element)),
                // The second container's own rows hold the portal into the first, which reaches further
                // down its target than the second container's own portal range does.
                V.Div(name: "second", refCallback: element => Register("second", element),
                    children: new VNode?[] { V.Portal("first", children: Rows(rows.Portal)) }.Concat(Rows(rows.Own)).ToArray()),
                V.Portal("second", children: new VNode?[] { V.Div(name: portalChild) }),
            });
        }

        [Test]
        public void Given_TwoContainersEachATarget_When_OnesRowsGrowWithThePortalIntoTheOther_Then_EachPortalsNextPatchLandsOnItsChildren()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(TwoTargetsRender, key: "host"));
            Flush();
            var first = _root.Q<VisualElement>("first");
            var second = _root.Q<VisualElement>("second");
            s_setRows!.Invoke((new[] { "b1", "b2" }, new[] { "a1", "a2", "a3", "a4", "a5", "a6" }));
            Flush();

            // Act
            s_setRows!.Invoke((new[] { "b1", "b2" }, new[] { "c1", "c2", "c3", "c4", "c5", "c6" }));
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(
                NamedChildren(first) + " / " + NamedChildren(second),
                Is.EqualTo("c1|c2|c3|c4|c5|c6 / b1|b2|q"));
        }

        [Test]
        public void Given_APortalIntoARenderedContainer_When_TheContainerGainsAChildAndThePortalPatches_Then_ThePatchLandsOnThePortalsChild()
        {
            // Arrange
            s_initialOwn = new[] { "a" };
            var container = MountContainer();
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(container), Is.EqualTo("a|b|q"));
        }

        [Test]
        public void Given_APortalIntoARenderedContainer_When_TheContainerLosesAChildAndThePortalPatches_Then_ThePatchLandsOnThePortalsChild()
        {
            // Arrange
            s_initialOwn = new[] { "a", "b" };
            var container = MountContainer();
            s_setOwn!.Invoke(new[] { "a" });
            Flush();

            // Act
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(container), Is.EqualTo("a|q"));
        }

        [Test]
        public void Given_AChildAppendedBehindThePortal_When_TheContainerGainsAChildAndThePortalPatches_Then_TheAppendedChildStaysBehind()
        {
            // Arrange — the appended child sits behind the portal's range before the container's own rows grow.
            s_initialOwn = new[] { "a" };
            var container = MountContainer();
            container.Add(new VisualElement { name = "m" });
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(container), Is.EqualTo("a|b|q|m"));
        }

        [Test]
        public void Given_TheContainersOwnRowsFromAComponent_When_ThatComponentGrowsAloneAndThePortalPatches_Then_ThePatchLandsOnThePortalsChild()
        {
            // Arrange — the rows ahead of the portal are a component's, which re-renders on its own state.
            s_initialOwn = new[] { "a" };
            s_ownRowsFromComponent = true;
            var container = MountContainer();
            s_setComponentRows!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(container), Is.EqualTo("a|b|q"));
        }

        [Test]
        public void Given_AComponentInsideThePortal_When_TheContainerGainsAChildAndThatComponentRerendersAlone_Then_ItPatchesItsOwnElement()
        {
            // Arrange
            s_initialOwn = new[] { "a" };
            s_portalChildIsComponent = true;
            var container = MountContainer();
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_changePortalComponent!.Invoke(1);
            Flush();

            // Assert
            Assert.That(Names(container), Is.EqualTo("a|b|changed"));
        }
    }
}
