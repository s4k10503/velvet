using System.Collections.Generic;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a router stands once constructed, as React Router's <c>createMemoryRouter(routes)</c>
    /// does with no options: on <c>/</c>, its history's one entry, with the loaders matched there run at once
    /// and a path no route matches committed as a 404. Every case here builds with the base's own surface.
    /// </summary>
    [TestFixture]
    internal sealed class RouterStartParityTests
    {
        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        [Test]
        public void Given_NoNavigation_When_TheRouterIsConstructed_Then_ItStandsOnTheRoot()
        {
            // Arrange / Act
            var router = new Router(new[] { Route("/") });

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/"));
        }

        [Test]
        public void Given_ARootWithALoader_When_TheRouterIsConstructed_Then_ItsLoaderHasRun()
        {
            // Arrange / Act
            var router = new Router(new[] { Route("/", loader: (_, _) => VelvetTask.FromResult<object>("root")) });

            // Assert
            Assert.That(router.GetLoaderData("0"), Is.EqualTo("root"));
        }

        [Test]
        public void Given_NoRouteAtTheRoot_When_TheRouterIsConstructed_Then_TheRootsNotFoundIsCommitted()
        {
            // Arrange / Act
            var router = new Router(new[] { Route("home") });

            // Assert
            Assert.That((router.CurrentLoaderErrors.GetValueOrDefault("0") as ErrorResponse)?.Status, Is.EqualTo(404));
        }

        [Test]
        public void Given_AConstructedRouter_When_ItNavigates_Then_TheStartingEntryIsOneStepBack()
        {
            // Arrange
            var router = new Router(new[] { Route("/"), Route("home") });

            // Act
            router.NavigateSync("/home");

            // Assert
            Assert.That(router.CanGoBack, Is.True);
        }

        [Test]
        public void Given_AConstructedRouter_When_ItNavigates_Then_TheNewEntryIsNotTheDefaultKey()
        {
            // Arrange
            var router = new Router(new[] { Route("/"), Route("home") });

            // Act
            router.NavigateSync("/home");

            // Assert
            Assert.That(router.CurrentLocation?.Key, Is.Not.EqualTo("default"));
        }
    }
}
