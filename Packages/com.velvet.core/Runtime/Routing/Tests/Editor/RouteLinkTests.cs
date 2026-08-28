// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class RouteLinkTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private MountedTree MountAt(string path, VNode tree) =>
            MountUnder(new Router(new[] { Route(path, element: V.Component(StubA)) }), path, tree);

        private MountedTree MountNestedAt(string path, VNode tree) =>
            MountUnder(
                new Router(new[]
                {
                    Route("/", children: new[]
                    {
                        Route("settings", children: new[] { Route("profile") }),
                    }),
                }),
                path,
                tree);

        private MountedTree MountUnder(Router router, string path, VNode tree)
        {
            router.NavigateSync(path);

            return V.Mount(_root,
                V.Provider(RouterContext.Location, router.CurrentLocation, children: new[] { tree }));
        }

        private static Button? FindButton(VisualElement root) =>
            root.Query<Button>().ToList().FirstOrDefault();

        [Test]
        public void Given_Link_When_Rendered_Then_ButtonCarriesText()
        {
            // Arrange

            // Act
            using var mounted = MountAt("/home", V.Link(to: "/about", text: "About"));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the link rendered a button");
            Assert.That(button!.text, Is.EqualTo("About"));
        }

        [Test]
        public void Given_Link_When_Clicked_Then_NavigatesToTarget()
        {
            // Arrange
            using var mounted = MountAt("/home", V.Link(to: "/home", text: "Home"));
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the link rendered a button");
            var navigated = false;
            Router.Current!.OnLocationChanged += _ => navigated = true;

            // Act
            button!.SimulateClick();

            // Assert
            Assert.That(navigated, Is.True, "A Link click navigates via the active router");
        }

        [Test]
        public void Given_NavLink_When_Clicked_Then_NavigatesToTarget()
        {
            // Arrange
            using var mounted = MountAt("/home",
                V.NavLink(to: "/home", activeClass: "is-active", text: "Home"));
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            var navigated = false;
            Router.Current!.OnLocationChanged += _ => navigated = true;

            // Act
            button!.SimulateClick();

            // Assert
            Assert.That(navigated, Is.True, "A NavLink click navigates via the active router");
        }

        [Test]
        public void Given_NavLink_When_LocationMatchesTarget_Then_AppliesActiveClass()
        {
            // Arrange

            // Act
            using var mounted = MountAt("/home",
                V.NavLink(to: "/home", activeClass: "is-active", text: "Home", end: true));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.True);
        }

        [Test]
        public void Given_NavLink_When_LocationDoesNotMatchTarget_Then_OmitsActiveClass()
        {
            // Arrange

            // Act
            using var mounted = MountAt("/home",
                V.NavLink(to: "/about", activeClass: "is-active", text: "About", end: true));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.False);
        }

        [Test]
        public void Given_NonEndNavLink_When_LocationIsSubPathOfTarget_Then_IsActive()
        {
            // Arrange

            // Act
            using var mounted = MountAt("/users/42",
                V.NavLink(to: "/users", activeClass: "is-active", text: "Users"));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.True);
        }

        [Test]
        public void Given_DefaultNavLink_When_LocationCaseDiffersFromTarget_Then_IsActive()
        {
            // Arrange

            // Act
            using var mounted = MountAt("/about",
                V.NavLink(to: "/About", activeClass: "is-active", text: "About", end: true));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.True);
        }

        [Test]
        public void Given_DefaultNonEndNavLink_When_SubPathCaseDiffers_Then_IsActive()
        {
            // Arrange

            // Act
            using var mounted = MountAt("/USERS/42",
                V.NavLink(to: "/users", activeClass: "is-active", text: "Users"));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.True);
        }

        [Test]
        public void Given_CaseSensitiveNavLink_When_LocationCaseDiffersFromTarget_Then_IsNotActive()
        {
            // Arrange

            // Act
            using var mounted = MountAt("/about",
                V.NavLink(to: "/About", activeClass: "is-active", text: "About", end: true, caseSensitive: true));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.False);
        }

        [Test]
        public void Given_CaseSensitiveNavLink_When_LocationCaseMatchesTarget_Then_IsActive()
        {
            // Arrange

            // Act
            using var mounted = MountAt("/About",
                V.NavLink(to: "/About", activeClass: "is-active", text: "About", end: true, caseSensitive: true));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.True);
        }

        [Test]
        public void Given_RelativeNavLink_When_LocationIsUnderTheResolvedTarget_Then_AppliesActiveClass()
        {
            // Arrange + Act
            using var mounted = MountNestedAt("/settings/profile",
                V.NavLink(to: "..", activeClass: "is-active", text: "Settings"));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.True);
        }

        [Test]
        public void Given_BareSegmentNavLink_When_RenderedAtANestedOutletDepth_Then_AppliesActiveClass()
        {
            // Arrange + Act — depth 2 is what an Outlet pushes for the "settings" route's own element, so
            // "profile" resolves against "/settings" rather than against the leaf.
            using var mounted = MountNestedAt("/settings/profile",
                V.Provider(RouterContext.Depth, 2, children: new VNode[]
                {
                    V.NavLink(to: "profile", activeClass: "is-active", text: "Profile", end: true),
                }));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the absolute target a location without a router already matches,
        // which resolving relative targets through that router must leave standing.
        [Test]
        public void Given_AbsoluteNavLink_When_ThereIsNoRouterToResolveThrough_Then_AppliesActiveClass()
        {
            // Arrange + Act
            using var mounted = V.Mount(_root,
                V.Provider(
                    RouterContext.Location,
                    new RouterLocation { Path = "/home", Params = new Dictionary<string, string>() },
                    children: new VNode[]
                    {
                        V.NavLink(to: "/home", activeClass: "is-active", text: "Home", end: true),
                    }));

            // Assert — the absent router is folded in: with one mounted, an absolute target resolves to
            // itself, so the active class alone would hold either way.
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(
                (routerAbsent: Router.Current == null, active: button!.ClassListContains("is-active")),
                Is.EqualTo((routerAbsent: true, active: true)));
        }

        [Test]
        public void Given_RootNavLink_When_ThereIsNoLocation_Then_OmitsActiveClass()
        {
            // Arrange

            // Act
            using var mounted = V.Mount(_root,
                V.NavLink(to: "/", activeClass: "is-active", text: "Home", end: true));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.False);
        }
    }
}
