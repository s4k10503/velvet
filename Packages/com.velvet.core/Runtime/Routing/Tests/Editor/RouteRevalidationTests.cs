using System.Threading;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which loaders a navigation runs, as React Router's default <c>shouldRevalidate</c> decides.
    /// <list type="bullet">
    /// <item>A route at the same place in the committed chain, over the same pathname, keeps its data, on a
    /// push and on a Back step alike, and its loader's token stays live.</item>
    /// <item>A loader runs again when the URL is unchanged, when the search changes, or where its route
    /// holds no settled data.</item>
    /// <item>A different route at the same place runs its own loader even over the same pathname.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteRevalidationTests
    {
        private int _root;
        private int _users;
        private int _user;

        [SetUp]
        public void SetUp()
        {
            _root = 0;
            _users = 0;
            _user = 0;
        }

        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        private string Calls => $"{_root},{_users},{_user}";

        private static VelvetTask<object> Loaded(string data) => VelvetTask.FromResult<object>(data);

        private Router UsersRouter(string start) => BuildRouter(start,
            Route("/", loader: (_, _) => Loaded($"root-{++_root}"), children: new[]
            {
                Route("users", loader: (_, _) => Loaded($"users-{++_users}"), children: new[]
                {
                    Route(":id", loader: (_, _) => Loaded($"user-{++_user}")),
                }),
            }));

        [Test]
        public void Given_LayoutsWhosePathnameIsUnchanged_When_NavigatingBetweenChildren_Then_OnlyTheChildsLoaderRuns()
        {
            // Arrange
            var router = UsersRouter("/users/1");

            // Act
            router.NavigateSync("/users/2");

            // Assert
            Assert.That(Calls, Is.EqualTo("1,1,2"));
        }

        [Test]
        public void Given_KeptLayouts_When_TheChainGrowsBelowThem_Then_OnlyTheNewRoutesLoaderRuns()
        {
            // Arrange
            var router = UsersRouter("/users");

            // Act
            router.NavigateSync("/users/1");

            // Assert
            Assert.That(Calls, Is.EqualTo("1,1,1"));
        }

        [Test]
        public void Given_LayoutsWhosePathnameIsUnchanged_When_SteppingBack_Then_OnlyTheChildsLoaderRuns()
        {
            // Arrange
            var router = UsersRouter("/users/1");
            router.NavigateSync("/users/2");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(Calls, Is.EqualTo("1,1,3"));
        }

        // GREEN_ON_BASE(characterization): the base runs every loader on every navigation.
        [Test]
        public void Given_TheCurrentUrl_When_NavigatedToAgain_Then_EveryLoaderRuns()
        {
            // Arrange
            var router = UsersRouter("/users/1");

            // Act
            router.NavigateSync("/users/1");

            // Assert
            Assert.That(Calls, Is.EqualTo("2,2,2"));
        }

        // GREEN_ON_BASE(characterization): the base runs every loader on every navigation.
        [Test]
        public void Given_ASearchChange_When_Navigating_Then_EveryLoaderRuns()
        {
            // Arrange
            var router = UsersRouter("/users/1");

            // Act
            router.NavigateSync("/users/1?tab=posts");

            // Assert
            Assert.That(Calls, Is.EqualTo("2,2,2"));
        }

        [Test]
        public void Given_AKeptLayout_When_ASiblingChildCommits_Then_OnlyTheLeftChildsTokenIsCancelled()
        {
            // Arrange
            CancellationToken rootToken = default;
            CancellationToken firstUserToken = default;
            var router = BuildRouter("/users/1",
                Route("/", loader: (_, ct) =>
                {
                    rootToken = ct;
                    return Loaded("root");
                }, children: new[]
                {
                    Route("users/:id", loader: (ctx, ct) =>
                    {
                        if (ctx.Params["id"] == "1")
                        {
                            firstUserToken = ct;
                        }
                        return Loaded("user");
                    }),
                }));

            // Act
            router.NavigateSync("/users/2");

            // Assert
            Assert.That((rootToken.IsCancellationRequested, firstUserToken.IsCancellationRequested),
                Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): the base runs every loader on every navigation.
        [Test]
        public void Given_ALayoutWhoseLoaderHasNotSettled_When_NavigatingBetweenChildren_Then_ItsLoaderRunsAgain()
        {
            // Arrange
            var router = BuildRouter("/users/1",
                Route("/", loaderMode: LoaderMode.Suspend, loader: (_, _) =>
                {
                    _root++;
                    return new VelvetTaskCompletionSource<object>().Task;
                }, children: new[] { Route("users/:id") }));

            // Act
            router.NavigateSync("/users/2");

            // Assert
            Assert.That(_root, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base runs every loader on every navigation.
        [Test]
        public void Given_TwoPathlessLayoutsAtOnePlace_When_NavigatingFromOneToTheOther_Then_TheSecondsLoaderRuns()
        {
            // Arrange — both layouts sit at index 1 over the pathname "/".
            var router = BuildRouter("/x",
                Route("/", children: new[]
                {
                    Route("", loader: (_, _) => Loaded($"first-{++_users}"), children: new[] { Route("x") }),
                    Route("", loader: (_, _) => Loaded($"second-{++_user}"), children: new[] { Route("y") }),
                }));

            // Act
            router.NavigateSync("/y");

            // Assert
            Assert.That(_user, Is.EqualTo(1));
        }
    }
}
