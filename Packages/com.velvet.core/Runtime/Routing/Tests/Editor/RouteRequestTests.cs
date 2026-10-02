using System.Linq;
using System.Threading;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    // What a loader, a guard and an action are told about the URL, as React Router's request carries it.
    [TestFixture]
    internal sealed class RouteRequestTests
    {
        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        private static object OnlyLoaderData(Router router) => router.CurrentLoaderData.Values.Single();

        [Test]
        public void Given_ALoader_When_NavigatingToAPathWithAQuery_Then_ItReadsTheUrl()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("users/:id", loader: (ctx, _) => VelvetTask.FromResult<object>(ctx.Url)),
            });

            // Act
            router.NavigateSync("/users/7?tab=posts");

            // Assert
            Assert.That(OnlyLoaderData(router), Is.EqualTo("/users/7?tab=posts"));
        }

        [Test]
        public void Given_ALoader_When_NavigatingToAPathWithAQuery_Then_ItReadsTheSearchParams()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("users/:id", loader: (ctx, _) => VelvetTask.FromResult<object>(ctx.SearchParams.Get("tab"))),
            });

            // Act
            router.NavigateSync("/users/7?tab=posts");

            // Assert
            Assert.That(OnlyLoaderData(router), Is.EqualTo("posts"));
        }

        [Test]
        public void Given_AGetSubmission_When_ItNavigates_Then_TheLoaderReadsTheFormDataAsTheSearch()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("search", loader: (ctx, _) => VelvetTask.FromResult<object>(ctx.SearchParams.Get("q"))),
            });
            var formData = new SearchParams();
            formData.Append("q", "lamp");

            // Act
            router.SubmitAsync(formData, new SubmitOptions { Action = "/search" }).GetAwaiter().GetResult();

            // Assert
            Assert.That(OnlyLoaderData(router), Is.EqualTo("lamp"));
        }

        [Test]
        public void Given_AGuard_When_NavigatingToAPathWithAQuery_Then_ItReadsTheUrl()
        {
            // Arrange
            string seen = null;
            var router = new Router(new[]
            {
                Route("users/:id", guard: ctx =>
                {
                    seen = ctx.Url;
                    return null;
                }),
            });

            // Act
            router.NavigateSync("/users/7?tab=posts");

            // Assert
            Assert.That(seen, Is.EqualTo("/users/7?tab=posts"));
        }

        [Test]
        public void Given_AnAction_When_SubmittedToAPathWithAQuery_Then_ItReadsTheUrl()
        {
            // Arrange
            string seen = null;
            var router = BuildRouter("/items", V.Route("items", V.Component(StubA), action: (ctx, _) =>
            {
                seen = ctx.Url;
                return VelvetTask.FromResult<object>(null);
            }));

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post", Action = "/items?sort=new" })
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(seen, Is.EqualTo("/items?sort=new"));
        }

        [Test]
        public void Given_AnAction_When_SubmittedToAPathWithAQuery_Then_ItReadsTheSearchParams()
        {
            // Arrange
            string seen = null;
            var router = BuildRouter("/items", V.Route("items", V.Component(StubA), action: (ctx, _) =>
            {
                seen = ctx.SearchParams.Get("sort");
                return VelvetTask.FromResult<object>(null);
            }));

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post", Action = "/items?sort=new" })
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(seen, Is.EqualTo("new"));
        }
    }
}
