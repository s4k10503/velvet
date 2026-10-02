using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    // Apart from RouteTreeTests: these name RouteDefinition.Id, so a checkout without it cannot compile this
    // file, and the base-red check withdraws a file it cannot compile whole.
    [TestFixture]
    internal sealed class RouteIdTests
    {
        [Test]
        public void Given_ARouteDeclaringAnId_When_Matched_Then_ItsRouteIdIsThatIdAndItsChildKeepsItsPosition()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("/"),
                new RouteDefinition
                {
                    Path = "users", Id = "users", Element = V.Component(StubA), Children = new[] { Route("new") },
                },
            });

            // Act
            var result = tree.Match("/users/new");

            // Assert
            Assert.That((result[0].RouteId, result[1].RouteId), Is.EqualTo(("users", "1-0")));
        }

        [Test]
        public void Given_ARouteDeclaringAnIdAnotherRouteHasByPosition_When_TheTreeIsBuilt_Then_ItThrows()
        {
            // Arrange
            var routes = new[]
            {
                Route("a"),
                new RouteDefinition { Path = "b", Id = "0", Element = V.Component(StubA) },
            };

            // Act
            TestDelegate build = () => new RouteTree(routes);

            // Assert
            Assert.That(build, Throws.ArgumentException.With.Message.Contains("route id collision on id \"0\""));
        }

        [Test]
        public void Given_AnIdPassedToVRoute_When_ItBuildsTheRoute_Then_TheRouteCarriesIt()
        {
            // Arrange
            const string id = "items";

            // Act
            var route = V.Route("items", element: V.Component(StubA), id: id);

            // Assert
            Assert.That(route.Id, Is.EqualTo(id));
        }
    }
}
