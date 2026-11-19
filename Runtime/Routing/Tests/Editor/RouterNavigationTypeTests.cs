using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="Router.NavigationType"/>, which says how the committed location was reached as
    /// React Router's <c>historyAction</c> does: a push, a replace, or a step along the history.
    /// </summary>
    [TestFixture]
    internal sealed class RouterNavigationTypeTests
    {
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        [Test]
        public void Given_ARouterThatHasCommittedNothing_When_ItIsRead_Then_ItIsPop()
        {
            // Arrange
            var router = new Router(new[] { Route("home") });

            // Act
            var type = router.NavigationType;

            // Assert
            Assert.That(type, Is.EqualTo(NavigationType.Pop));
        }

        [Test]
        public void Given_APushedLocation_When_ItIsRead_Then_ItIsPush()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"));
            router.NavigateSync("/about");

            // Act
            var type = router.NavigationType;

            // Assert
            Assert.That(type, Is.EqualTo(NavigationType.Push));
        }

        [Test]
        public void Given_AReplacedLocation_When_ItIsRead_Then_ItIsReplace()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"));
            router.NavigateAsync("/about", new NavigateOptions { Replace = true }).GetAwaiter().GetResult();

            // Act
            var type = router.NavigationType;

            // Assert
            Assert.That(type, Is.EqualTo(NavigationType.Replace));
        }

        [Test]
        public void Given_ALocationReachedByGoingBack_When_ItIsRead_Then_ItIsPop()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"));
            router.NavigateSync("/about");
            router.GoBackSync();

            // Act
            var type = router.NavigationType;

            // Assert
            Assert.That(type, Is.EqualTo(NavigationType.Pop));
        }
    }
}
