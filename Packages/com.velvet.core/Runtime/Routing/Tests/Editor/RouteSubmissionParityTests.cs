using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the parts of a submission React Router 7 settles that the base's own surface can ask about.
    /// <list type="bullet">
    /// <item>A route with an empty path is not an index route, so a submission from it takes no bare
    /// <c>index</c> and goes to the deepest route with a path.</item>
    /// <item>Under the default encoding every method reads its body through the URLSearchParams constructor,
    /// so a body it cannot encode commits the encoding error whatever the method.</item>
    /// <item>A <c>get</c>'s query is written as URLSearchParams writes itself.</item>
    /// <item>The navigation reports the method upper-case.</item>
    /// </list>
    /// </summary>
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouteSubmissionParityTests
    {
        private readonly List<string> _log = new();

        [SetUp]
        public void SetUp() => _log.Clear();

        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        private Func<RouteActionContext, CancellationToken, VelvetTask<object>> Logged(string name) => (_, _) =>
        {
            _log.Add(name);
            return VelvetTask.FromResult<object>(name);
        };

        private Router EmptyPathChildRouter() => BuildRouter("/",
            V.Route("/", V.Component(StubA), action: Logged("layout"), children: new[]
            {
                V.Route("", V.Component(StubA), action: Logged("child")),
            }));

        private static Router ItemsRouter(Func<RouteActionContext, CancellationToken, VelvetTask<object>> action)
            => BuildRouter("/other", Route("/", children: new[]
            {
                V.Route("items", V.Component(StubA), action: action),
                Route("other"),
            }));

        private static NavigationResult Submit(Router router, object body, SubmitOptions options)
            => router.SubmitAsync(body, options).GetAwaiter().GetResult();

        [Test]
        public void Given_AnEmptyPathChild_When_ItSubmitsWithoutNamingAnAction_Then_NoBareIndexIsAdded()
        {
            // Arrange
            var router = EmptyPathChildRouter();

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/"));
        }

        [Test]
        public void Given_AnEmptyPathChild_When_ItSubmitsWithoutNamingAnAction_Then_TheLayoutsActionRuns()
        {
            // Arrange
            var router = EmptyPathChildRouter();

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("layout"));
        }

        [Test]
        public void Given_APostOfASequenceOfNonPairs_When_Submitted_Then_ItCommitsTheEncodingError()
        {
            // Arrange
            var router = ItemsRouter((_, _) => VelvetTask.FromResult<object>("saved"));

            // Act
            Submit(router, new[] { 1, 2 }, new SubmitOptions { Method = "post", Action = "/items" });

            // Assert
            Assert.That((router.CurrentLoaderErrors.GetValueOrDefault("0") as ErrorResponse)?.Detail,
                Is.EqualTo("Error: Unable to encode submission body"));
        }

        [Test]
        public void Given_AGetSubmissionHoldingASpaceATildeAndAnAsterisk_When_Submitted_Then_TheQueryIsFormEncoded()
        {
            // Arrange
            var router = ItemsRouter((_, _) => VelvetTask.FromResult<object>("saved"));

            // Act
            Submit(router, new Dictionary<string, string> { ["q"] = "a b~*" }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/items?q=a+b%7E*"));
        }

        [Test]
        public void Given_APostToARouteWithNoAction_When_Submitted_Then_ItCommitsA405()
        {
            // Arrange
            var router = BuildRouter("/items", Route("items"));

            // Act
            Submit(router, "a=1", new SubmitOptions { Method = "post" });

            // Assert
            Assert.That((router.CurrentLoaderErrors.GetValueOrDefault("0") as ErrorResponse)?.Status, Is.EqualTo(405));
        }

        [Test]
        public void Given_AMethodNoFormTakes_When_Submitted_Then_ItCommitsAMethodNotAllowedResponse()
        {
            // Arrange
            var router = ItemsRouter((_, _) => VelvetTask.FromResult<object>("saved"));

            // Act
            Submit(router, "a=1", new SubmitOptions { Method = "head", Action = "/items" });

            // Assert
            Assert.That((router.CurrentLoaderErrors.GetValueOrDefault("0") as ErrorResponse)?.StatusText,
                Is.EqualTo("Method Not Allowed"));
        }

        [Test]
        public void Given_ABodyNoEncodingTakes_When_Submitted_Then_ItCommitsA400()
        {
            // Arrange
            var router = ItemsRouter((_, _) => VelvetTask.FromResult<object>("saved"));

            // Act
            Submit(router, new[] { 1, 2 }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That((router.CurrentLoaderErrors.GetValueOrDefault("0") as ErrorResponse)?.Status, Is.EqualTo(400));
        }

        [Test]
        public void Given_AGetSubmissionWhoseMemberIsAnObject_When_Submitted_Then_TheMemberIsWrittenAsJavaScriptsString()
        {
            // Arrange
            var router = ItemsRouter((_, _) => VelvetTask.FromResult<object>("saved"));

            // Act
            Submit(router, new { a = new { b = 1 } }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/items?a=%5Bobject+Object%5D"));
        }

        [Test]
        public void Given_APostWhoseActionIsStillRunning_When_TheNavigationIsRead_Then_ItsFormMethodIsUpperCase()
        {
            // Arrange
            var router = ItemsRouter((_, _) => new VelvetTaskCompletionSource<object>().Task);

            // Act
            router.SubmitAsync("lamp", new SubmitOptions { Method = "post", Action = "/items" }).Forget();

            // Assert
            Assert.That(router.Navigation.FormMethod, Is.EqualTo("POST"));
        }
    }
}
