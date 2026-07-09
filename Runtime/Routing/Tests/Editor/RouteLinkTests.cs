// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the rendered structure of the <c>V.Link</c> / <c>V.NavLink</c> navigation primitives.
    /// <list type="bullet">
    /// <item><c>V.Link</c> renders a button carrying the supplied text.</item>
    /// <item><c>V.NavLink</c> applies its active class when the current location matches the target and omits
    /// it otherwise; with <c>end: false</c> a sub-path of the target also counts as active.</item>
    /// <item>Active matching is case-insensitive by default (including the non-end sub-path form);
    /// <c>caseSensitive: true</c> opts into ordinal comparison so a different-case target is inactive while the
    /// same-case target stays active.</item>
    /// <item>Clicking a <c>V.Link</c> or <c>V.NavLink</c> navigates via the active router.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Click dispatch through a real panel is unreliable in EditMode, so structure tests assert on the rendered
    /// output (button text and applied classes derived from the current location), and click tests drive the
    /// button's callback registry directly via the synthetic-event helper instead of a panel.
    /// </remarks>
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

        private MountedTree MountAt(string path, VNode tree)
        {
            var router = new Router(new[] { Route(path, element: V.Component(StubA)) });
            router.NavigateSync(path);

            return V.Mount(_root,
                V.Provider(RouterContext.Location, router.CurrentLocation, children: new[] { tree }));
        }

        private static Button? FindButton(VisualElement root) =>
            root.Query<Button>().ToList().FirstOrDefault();

        [Test]
        public void Given_Link_When_Rendered_Then_ButtonCarriesText()
        {
            // Arrange + Act
            using var mounted = MountAt("/home", V.Link(to: "/about", text: "About"));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the link rendered a button");
            Assert.That(button!.text, Is.EqualTo("About"));
        }

        [Test]
        public void Given_Link_When_Clicked_Then_NavigatesToTarget()
        {
            // Arrange — SimulateClick drives the button's callback registry directly, so the click
            // path is exercisable without a live panel.
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
            // NavLink delegates its click/navigate wiring to Link; a click must still navigate.
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
            // Arrange + Act
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
            // Arrange + Act
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
            // Arrange + Act
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
            // Active state is case-insensitive by default.
            // Arrange + Act
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
            // Non-end sub-path activation also follows the case-insensitive default.
            // Arrange + Act
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
            // caseSensitive: true opts into ordinal comparison.
            // Arrange + Act
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
            // Arrange + Act
            using var mounted = MountAt("/About",
                V.NavLink(to: "/About", activeClass: "is-active", text: "About", end: true, caseSensitive: true));

            // Assert
            var button = FindButton(_root);
            Assume.That(button, Is.Not.Null, "Precondition: the nav link rendered a button");
            Assert.That(button!.ClassListContains("is-active"), Is.True);
        }
    }
}
