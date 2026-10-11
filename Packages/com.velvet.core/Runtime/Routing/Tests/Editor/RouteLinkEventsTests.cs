// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestMounts;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the <c>events:</c> array <c>V.Link</c> and <c>V.NavLink</c> take against React Router's
    /// <c>Link</c>: the caller's click handler runs ahead of the navigation, and a click it prevented does
    /// not navigate.
    /// </summary>
    [TestFixture]
    internal sealed class RouteLinkEventsTests
    {
        private VisualElement _root = null!;
        private readonly List<string> _log = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _log.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private MountedTree MountAtHome(VNode link)
        {
            var router = new Router(new[]
            {
                Route("/home", element: V.Component(StubA)),
                Route("/about", element: V.Component(StubA)),
            });
            router.NavigateSync("/home");
            var mounted = V.Mount(_root, WithRouter(router, link));
            router.OnLocationChanged += _ => _log.Add("navigated");
            return mounted;
        }

        private FiberEventBinding[] Click(bool prevent) => new FiberEventBinding[]
        {
            new ClickedEventBinding
            {
                Handler = click =>
                {
                    _log.Add("onClick");
                    if (prevent) click.PreventDefault();
                },
            },
        };

        [Test]
        public void Given_ALinkWhoseClickHandlerPreventsTheDefault_When_Clicked_Then_ItDoesNotNavigate()
        {
            // Arrange
            using var mounted = MountAtHome(V.Link(to: "/about", text: "About", events: Click(prevent: true)));

            // Act
            _root.Q<Button>().SimulateClick();

            // Assert — the handler's own entry tells a prevented navigation from a handler never bound.
            Assert.That(string.Join(",", _log), Is.EqualTo("onClick"));
        }

        [Test]
        public void Given_ALinkWhoseClickHandlerLeavesTheDefault_When_Clicked_Then_TheHandlerRunsAndThenItNavigates()
        {
            // Arrange
            using var mounted = MountAtHome(V.Link(to: "/about", text: "About", events: Click(prevent: false)));

            // Act
            _root.Q<Button>().SimulateClick();

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("onClick,navigated"));
        }

        [Test]
        public void Given_ANavLinkWhoseClickHandlerPreventsTheDefault_When_Clicked_Then_ItDoesNotNavigate()
        {
            // Arrange — NavLink renders through Link, so this is the hand-off of the array between the two.
            using var mounted = MountAtHome(
                V.NavLink(to: "/about", activeClass: "active", text: "About", events: Click(prevent: true)));

            // Act
            _root.Q<Button>().SimulateClick();

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("onClick"));
        }

        [Test]
        public void Given_ANavLinkWhoseClickHandlerLeavesTheDefault_When_Clicked_Then_TheHandlerRunsAndThenItNavigates()
        {
            // Arrange
            using var mounted = MountAtHome(
                V.NavLink(to: "/about", activeClass: "active", text: "About", events: Click(prevent: false)));

            // Act
            _root.Q<Button>().SimulateClick();

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("onClick,navigated"));
        }
    }
}
