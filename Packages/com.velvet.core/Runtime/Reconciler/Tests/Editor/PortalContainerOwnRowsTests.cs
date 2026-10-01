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
            s_portalLast = false;
            s_portalBeforeContainer = false;
            s_selfPortalUnkeyed = false;
            s_ownInFragment = false;
            s_registerScrollContent = false;
            s_scrollRowsFromComponent = false;
            s_scrollHostRenders = 0;
            s_portalShownLater = false;
            s_portalHoldsScroll = false;
            s_setPortalShown = null;
            s_setFirstPortalRows = null;
            s_setSecondPortalChild = null;
            s_registerSelfOnRef = true;
            s_showSecondSelfPortal = true;
            s_setSelfPortals = null;
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
            var container = V.Div(name: "container", refCallback: RegisterContainer, children: ownChildren);
            var portal = V.Portal(TargetId, children: new VNode?[] { portalContent });
            return V.Fragment(children: s_portalBeforeContainer
                ? new VNode?[] { portal, container }
                : new VNode?[] { container, portal });
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

        // Whether the portal is the container's last own child rather than its first.
        private static bool s_portalLast;

        [Component]
        private static VNode SelfTargetRender()
        {
            var (rows, setRows) = Hooks.UseState((Own: new[] { "a" }, Portal: new[] { "p1" }));
            s_setRows = setRows;
            // The portal is one of the container's own children and targets the container itself; its key keeps
            // it the same portal where rows ahead of it are added, and the unkeyed and Fragment spellings take the
            // positional and the general reconcile instead.
            var portal = new VNode?[]
            {
                s_selfPortalUnkeyed
                    ? V.Portal("self", children: Rows(rows.Portal))
                    : V.Portal("self", key: "portal", children: Rows(rows.Portal)),
            };
            var own = s_ownInFragment ? new VNode?[] { V.Fragment(children: Rows(rows.Own)) } : Rows(rows.Own);
            Func<VisualElement, Action>? register = s_registerSelfOnRef ? element => Register("self", element) : null;
            return V.Div(name: "container", refCallback: register,
                children: s_portalLast ? own.Concat(portal).ToArray() : portal.Concat(own).ToArray());
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

        [Test]
        public void Given_APortalInsideTheContainerItTargets_When_TheTargetRegistersInTheRenderTheContainerGrows_Then_ItsNextPatchLandsOnItsChildren()
        {
            // Arrange — registered from here, so the render the registration asks for is the one that adds the
            // container's own row behind the portal: the portal heals from inside the container's reconcile,
            // before that row is placed.
            s_registerSelfOnRef = false;
            _mounted = V.Mount(_root, V.Component(SelfTargetRender, key: "host"));
            var container = _root.Q<VisualElement>("container");
            FiberPortalRegistry.Register("self", container);
            s_setRows!.Invoke((new[] { "a", "b" }, new[] { "p1" }));
            Flush();

            // Act
            s_setRows!.Invoke((new[] { "a", "b" }, new[] { "q1" }));
            Flush();

            // Assert
            Assert.That(NamedChildren(container), Is.EqualTo("a|b|q1"));
        }

        // GREEN_ON_BASE(characterization): the base opens no own-rows frame to leave behind.
        // This pins that every frame a render opens around a container's own children closes with that render.
        [Test]
        public void Given_APortalIntoARenderedContainer_When_ARenderPatchesTheContainer_Then_NoOwnRowsFrameStaysOpen()
        {
            // Arrange
            s_initialOwn = new[] { "a" };
            MountContainer();

            // Act
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Assert — read by reflection, since the base has no such list and the case must still build there.
            var context = _mounted!.Root.Reconciler!.Context;
            var frames = typeof(ReconcilerContext).GetProperty("OwnRowFrames",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(context)
                as System.Collections.ICollection;
            Assert.That(frames?.Count ?? 0, Is.EqualTo(0));
        }

        [Test]
        public void Given_APortalAfterTheContainerRowsItTargets_When_ThoseRowsAndItGrowInOneRender_Then_ItsPatchInThatRenderLandsOnItsChildren()
        {
            // Arrange — the container's own row ahead of the portal is added in the reconcile that patches the
            // portal, which patches the portal at its old position before it inserts the row.
            s_portalLast = true;
            _mounted = V.Mount(_root, V.Component(SelfTargetRender, key: "host"));
            Flush();
            var container = _root.Q<VisualElement>("container");

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
            // Behind the first container's portal, so the two containers stand differently behind their ranges.
            first.Add(new VisualElement { name = "m" });
            s_setRows!.Invoke((new[] { "b1", "b2" }, new[] { "a1", "a2", "a3", "a4", "a5", "a6" }));
            Flush();

            // Act
            s_setRows!.Invoke((new[] { "b1", "b2" }, new[] { "c1", "c2", "c3", "c4", "c5", "c6" }));
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(
                NamedChildren(first) + " / " + NamedChildren(second),
                Is.EqualTo("c1|c2|c3|c4|c5|c6|m / b1|b2|q"));
        }

        [Component]
        private static VNode ContainerInsidePortalRender()
        {
            var (own, setOwn) = Hooks.UseState(new[] { "a" });
            s_setOwn = setOwn;
            var (portalChild, setPortalChild) = Hooks.UseState("p");
            s_setPortalChild = setPortalChild;
            return V.Fragment(children: new VNode?[]
            {
                V.Div(name: "outer", refCallback: element => Register("outer", element)),
                // The container sits in another portal's children, so the component among its own rows is
                // reached by that portal's patch and carries that portal's placeholder.
                V.Portal("outer", children: new VNode?[]
                {
                    V.Div(name: "container", refCallback: element => Register("inner", element),
                        children: Rows(own).Append(V.Component(ComponentRowsRender, key: "rows")).ToArray()),
                }),
                V.Portal("inner", children: new VNode?[] { V.Div(name: portalChild) }),
            });
        }

        [Test]
        public void Given_AContainerInsideAnotherPortal_When_ItsOwnRowsGrowAndTheirComponentGrowsAlone_Then_BothItAndThePortalIntoTheContainerPatchTheirOwnRows()
        {
            // Arrange — the element rows grow first, then the component among them on its own state.
            s_initialOwn = new[] { "f1" };
            _mounted = V.Mount(_root, V.Component(ContainerInsidePortalRender, key: "host"));
            Flush();
            Flush();
            var container = _root.Q<VisualElement>("container");
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_setComponentRows!.Invoke(new[] { "f1", "f2" });
            Flush();
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(container), Is.EqualTo("a|b|f1|f2|q"));
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
        private static bool s_portalBeforeContainer;
        private static bool s_registerSelfOnRef = true;

        [Test]
        public void Given_APortalDeclaredBeforeTheContainer_When_TheContainerGainsAChildAndAComponentInThePortalRerendersAlone_Then_ItPatchesItsOwnElement()
        {
            // Arrange — the portal patches before the container in the render that adds the row, so nothing
            // re-places its component after the container's rows move it.
            s_initialOwn = new[] { "a" };
            s_portalChildIsComponent = true;
            s_portalBeforeContainer = true;
            var container = MountContainer();
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_changePortalComponent!.Invoke(1);
            Flush();

            // Assert
            Assert.That(Names(container), Is.EqualTo("a|b|changed"));
        }

        // GREEN_ON_BASE(characterization): the base's reconcile of the container places the component at its row too.
        // This pins that following the container's rows leaves a component carrying another portal's placeholder alone.
        [Test]
        public void Given_AContainerInsideAnotherPortal_When_ItsOwnRowsGrow_Then_TheComponentAmongThemStartsWhereItsRowIs()
        {
            // Arrange — the component carries the placeholder of the portal the container sits in, whose range
            // is on another element.
            s_initialOwn = new[] { "f1" };
            _mounted = V.Mount(_root, V.Component(ContainerInsidePortalRender, key: "host"));
            Flush();
            Flush();
            var container = _root.Q<VisualElement>("container");
            // Taken now: a later patch of another portal into the container can rename it.
            var row = container.Q<VisualElement>("f1");

            // Act
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Assert
            var fiber = row.userData as ComponentFiber;
            Assert.That(fiber?.MountSlotStart ?? -1, Is.EqualTo(container.IndexOf(row)));
        }

        private static bool s_showSecondSelfPortal = true;
        private static Action<(bool ShowSecond, string FirstChild)>? s_setSelfPortals;

        [Component]
        private static VNode TwoSelfPortalsRender()
        {
            var (state, setState) = Hooks.UseState((ShowSecond: s_showSecondSelfPortal, FirstChild: "p1"));
            s_setSelfPortals = setState;
            var children = new System.Collections.Generic.List<VNode?>
            {
                V.Div(name: "a"),
                V.Portal("self-pair", key: "first", children: new VNode?[] { V.Div(name: state.FirstChild) }),
            };
            if (state.ShowSecond)
            {
                // Two children against the first's one, so the two ranges' lengths differ.
                children.Add(V.Portal("self-pair", key: "second",
                    children: new VNode?[] { V.Div(name: "p2"), V.Div(name: "p3") }));
            }
            return V.Div(name: "container", refCallback: element => Register("self-pair", element), children: children.ToArray());
        }

        [Test]
        public void Given_TwoPortalsAmongTheChildrenOfTheContainerTheyTarget_When_TheSecondGoes_Then_TheFirstsNextPatchLandsOnItsChild()
        {
            // Arrange — the second portal's placeholder is a row of the container's own ahead of the first
            // portal's range, and its range is the last on the container, so its removal moves both the rows
            // ahead of the first range and where the last range ends.
            _mounted = V.Mount(_root, V.Component(TwoSelfPortalsRender, key: "host"));
            Flush();
            var container = _root.Q<VisualElement>("container");
            s_setSelfPortals!.Invoke((false, "p1"));
            Flush();

            // Act
            s_setSelfPortals!.Invoke((false, "q1"));
            Flush();

            // Assert
            Assert.That(NamedChildren(container), Is.EqualTo("a|q1"));
        }
        private static bool s_selfPortalUnkeyed;
        private static bool s_ownInFragment;

        [Test]
        public void Given_AnUnkeyedPortalAmongTheContainerRowsItTargets_When_ThoseRowsAndItChangeInOneRender_Then_ItsPatchInThatRenderLandsOnItsChildren()
        {
            // Arrange — the portal is the first row and unkeyed, so the positional reconcile patches it before it
            // adds the row behind it.
            s_selfPortalUnkeyed = true;
            _mounted = V.Mount(_root, V.Component(SelfTargetRender, key: "host"));
            Flush();
            var container = _root.Q<VisualElement>("container");

            // Act
            s_setRows!.Invoke((new[] { "a", "b" }, new[] { "q1", "q2" }));
            Flush();

            // Assert
            Assert.That(NamedChildren(container), Is.EqualTo("a|b|q1|q2"));
        }

        [Test]
        public void Given_APortalBesideAFragmentOfTheContainerRowsItTargets_When_ThoseRowsAndItChangeInOneRender_Then_ItsPatchInThatRenderLandsOnItsChildren()
        {
            // Arrange — the container's own rows are a Fragment, which takes the general reconcile.
            s_ownInFragment = true;
            _mounted = V.Mount(_root, V.Component(SelfTargetRender, key: "host"));
            Flush();
            var container = _root.Q<VisualElement>("container");

            // Act
            s_setRows!.Invoke((new[] { "a", "b" }, new[] { "q1", "q2" }));
            Flush();

            // Assert
            Assert.That(NamedChildren(container), Is.EqualTo("a|b|q1|q2"));
        }

        private static bool s_registerScrollContent;
        private static bool s_scrollRowsFromComponent;

        // A ScrollView's children sit in its contentContainer, which is another element; the portal is handed
        // either the ScrollView or that container.
        [Component]
        private static VNode ScrollHostRender()
        {
            s_scrollHostRenders++;
            var (own, setOwn) = Hooks.UseState(new[] { "a" });
            s_setOwn = setOwn;
            var (portalChild, setPortalChild) = Hooks.UseState("p");
            s_setPortalChild = setPortalChild;
            var ownChildren = s_scrollRowsFromComponent
                ? new VNode?[] { V.Component(ComponentRowsRender, key: "rows") }
                : Rows(own);
            VNode portalContent = s_portalChildIsComponent
                ? V.Component(PortalComponentRender, key: "portal-component")
                : V.Div(name: portalChild);
            // Shown once registered (a later render's create), or handed the ScrollView itself.
            var (shown, setShown) = Hooks.UseState(!s_portalShownLater);
            s_setPortalShown = setShown;
            var (held, setHeld) = Hooks.UseState((VisualElement?)null);
            VNode? portal = s_portalHoldsScroll
                ? held == null ? null : V.Portal(held, children: new VNode?[] { portalContent })
                : shown ? V.Portal("scroll-target", children: new VNode?[] { portalContent }) : null;
            return V.Fragment(children: new VNode?[]
            {
                V.ScrollView(name: "scroll", refCallback: element =>
                {
                    if (s_portalHoldsScroll)
                    {
                        setHeld.Invoke(element);
                        return () => { };
                    }
                    return Register("scroll-target", s_registerScrollContent ? element.contentContainer : element);
                }, children: ownChildren),
                portal,
            });
        }

        private static bool s_portalShownLater;
        private static bool s_portalHoldsScroll;
        private static Action<bool>? s_setPortalShown;

        [Test]
        public void Given_APortalCreatedOnceItsScrollViewIsRegistered_When_TheComponentOfItsOwnRowsGrowsAloneAndThePortalsComponentRerendersAlone_Then_ItPatchesItsOwnElement()
        {
            // Arrange — the portal is created by a render after the registration, so its creation resolves the id.
            s_initialOwn = new[] { "a" };
            s_scrollRowsFromComponent = true;
            s_portalChildIsComponent = true;
            s_portalShownLater = true;
            var content = MountScroll();
            s_setPortalShown!.Invoke(true);
            Flush();
            s_setComponentRows!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_changePortalComponent!.Invoke(1);
            Flush();

            // Assert
            Assert.That(Names(content), Is.EqualTo("a|b|changed"));
        }

        [Test]
        public void Given_APortalHandedTheScrollViewItself_When_TheComponentOfItsOwnRowsGrowsAloneAndThePortalsComponentRerendersAlone_Then_ItPatchesItsOwnElement()
        {
            // Arrange
            s_initialOwn = new[] { "a" };
            s_scrollRowsFromComponent = true;
            s_portalChildIsComponent = true;
            s_portalHoldsScroll = true;
            var content = MountScroll();
            s_setComponentRows!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_changePortalComponent!.Invoke(1);
            Flush();

            // Assert
            Assert.That(Names(content), Is.EqualTo("a|b|changed"));
        }

        // GREEN_ON_BASE(characterization): the base keeps a ScrollView portal's child across its host's renders too.
        // This pins that the recorded content container is not taken for a different element at each patch.
        [Test]
        public void Given_APortalIntoAScrollView_When_ItsHostRendersAgain_Then_ThePortalsChildIsTheSameElement()
        {
            // Arrange
            var content = MountScroll();
            var child = content.Q<VisualElement>("p");

            // Act
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Assert — whether the child mounted at all is folded in, since two nulls are the same too.
            Assert.That((child != null, ReferenceEquals(content.Q<VisualElement>("p"), child)), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base keeps a held ScrollView portal's child across renders too.
        // This pins that the held element's content container counts as the one the portal recorded.
        [Test]
        public void Given_APortalHandedTheScrollViewItself_When_ItsHostRendersAgain_Then_ThePortalsChildIsTheSameElement()
        {
            // Arrange
            s_portalHoldsScroll = true;
            var content = MountScroll();
            var child = content.Q<VisualElement>("p");

            // Act
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Assert — whether the child mounted at all is folded in, since two nulls are the same too.
            Assert.That((child != null, ReferenceEquals(content.Q<VisualElement>("p"), child)), Is.EqualTo((true, true)));
        }

        // An element whose contentContainer the test switches, as a custom element may.
        private sealed class SwitchableContent : VisualElement
        {
            public VisualElement? Content;

            public override VisualElement contentContainer => Content ?? this;
        }

        [Test]
        public void Given_APortalHandedAnElement_When_ThatElementsContentContainerChanges_Then_TheChildrenMoveToTheNewOne()
        {
            // Arrange
            var reconciler = new Reconciler();
            var holder = new SwitchableContent();
            var first = new VisualElement();
            var second = new VisualElement();
            holder.hierarchy.Add(first);
            holder.hierarchy.Add(second);
            holder.Content = first;
            var tree = new VNode[] { V.Portal(holder, children: new VNode?[] { V.Div(name: "p") }) };
            var root = new VisualElement();
            reconciler.Reconcile(root, Array.Empty<VNode>(), tree);
            holder.Content = second;

            // Act
            reconciler.Reconcile(root, tree, new VNode[] { V.Portal(holder, children: new VNode?[] { V.Div(name: "p") }) });
            var counts = (first.childCount, second.childCount);
            reconciler.Dispose();

            // Assert
            Assert.That(counts, Is.EqualTo((0, 1)));
        }

        private VisualElement MountScroll()
        {
            _mounted = V.Mount(_root, V.Component(ScrollHostRender, key: "host"));
            Flush();
            return _root.Q<VisualElement>("scroll").contentContainer;
        }

        [Test]
        public void Given_APortalIntoAScrollView_When_ItsOwnChildrenGrowAndThePortalPatches_Then_ThePatchLandsOnThePortalsChild()
        {
            // Arrange
            var content = MountScroll();
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(content), Is.EqualTo("a|b|q"));
        }

        [Test]
        public void Given_APortalIntoAScrollViewsContentContainer_When_ItsOwnChildrenGrowAndThePortalPatches_Then_ThePatchLandsOnThePortalsChild()
        {
            // Arrange
            s_registerScrollContent = true;
            var content = MountScroll();
            s_setOwn!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(content), Is.EqualTo("a|b|q"));
        }

        [Test]
        public void Given_APortalIntoAScrollView_When_TheComponentOfItsOwnRowsGrowsAloneAndThePortalPatches_Then_ThePatchLandsOnThePortalsChild()
        {
            // Arrange
            s_initialOwn = new[] { "a" };
            s_scrollRowsFromComponent = true;
            var content = MountScroll();
            s_setComponentRows!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_setPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(content), Is.EqualTo("a|b|q"));
        }
        [Test]
        public void Given_AComponentInAPortalIntoAScrollView_When_TheComponentOfItsOwnRowsGrowsAloneAndThePortalsComponentRerendersAlone_Then_ItPatchesItsOwnElement()
        {
            // Arrange — both components are mounted among the rows of the ScrollView's content container.
            s_initialOwn = new[] { "a" };
            s_scrollRowsFromComponent = true;
            s_portalChildIsComponent = true;
            var content = MountScroll();
            s_setComponentRows!.Invoke(new[] { "a", "b" });
            Flush();

            // Act
            s_changePortalComponent!.Invoke(1);
            Flush();

            // Assert
            Assert.That(Names(content), Is.EqualTo("a|b|changed"));
        }

        private static Action<string[]>? s_setFirstPortalRows;
        private static Action<string>? s_setSecondPortalChild;

        // One portal is handed the ScrollView and the other its content container, which hold the same rows.
        [Component]
        private static VNode ScrollPairRender()
        {
            var (firstRows, setFirstRows) = Hooks.UseState(new[] { "p1" });
            s_setFirstPortalRows = setFirstRows;
            var (secondChild, setSecondChild) = Hooks.UseState("b1");
            s_setSecondPortalChild = setSecondChild;
            return V.Fragment(children: new VNode?[]
            {
                V.ScrollView(name: "scroll", refCallback: element =>
                {
                    var unregisterElement = Register("scroll-element", element);
                    var unregisterContent = Register("scroll-content", element.contentContainer);
                    return () =>
                    {
                        unregisterElement();
                        unregisterContent();
                    };
                }, children: Rows(new[] { "a" })),
                V.Portal("scroll-element", children: Rows(firstRows)),
                V.Portal("scroll-content", children: new VNode?[] { V.Div(name: secondChild) }),
            });
        }

        [Test]
        public void Given_PortalsIntoAScrollViewAndItsContentContainer_When_TheFirstGrows_Then_TheSecondsNextPatchLandsOnItsChild()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(ScrollPairRender, key: "host"));
            Flush();
            var content = _root.Q<VisualElement>("scroll").contentContainer;
            s_setFirstPortalRows!.Invoke(new[] { "p1", "p2" });
            Flush();

            // Act
            s_setSecondPortalChild!.Invoke("q");
            Flush();

            // Assert
            Assert.That(Names(content), Is.EqualTo("a|p1|p2|q"));
        }
        private static int s_scrollHostRenders;

        // GREEN_ON_BASE(characterization): the base renders nothing again for the element its portal already holds.
        // This pins that the portal's recorded content container still counts as the ScrollView registered again.
        [Test]
        public void Given_APortalIntoAScrollView_When_TheScrollViewIsRegisteredAgain_Then_TheDeclaringComponentDoesNotRenderAgain()
        {
            // Arrange
            MountScroll();
            var scroll = _root.Q<VisualElement>("scroll");
            var rendersBefore = s_scrollHostRenders;

            // Act
            FiberPortalRegistry.Register("scroll-target", scroll);
            Flush();

            // Assert
            Assert.That(s_scrollHostRenders, Is.EqualTo(rendersBefore));
        }
    }
}
