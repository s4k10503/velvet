// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies route actions as React Router's data router runs them.
    /// <list type="bullet">
    /// <item>A submission other than <c>get</c> calls the action of the route it targets with the method and the
    /// form data, then runs every matched loader, and commits the action's result as that route's action
    /// data, which the next commit clears.</item>
    /// <item>An action that throws, or a route with none, commits the failure as the route's error, and only
    /// the loaders above the route rendering it run.</item>
    /// <item>A mutation submitted to the current location replaces its entry, unless its action fails; a
    /// <c>get</c> submission navigates with the form data as the query string.</item>
    /// <item>With no action named, a submission goes to the submitting route, an index route putting a bare
    /// <c>index</c> in front of the current search in place of the one there.</item>
    /// <item><c>UseSubmit</c> submits from the calling route, <c>UseActionData</c> reads the result there, and
    /// <c>UseNavigation</c> reports <c>Submitting</c> with the submission while the action runs.</item>
    /// </list>
    /// </summary>
    // Bounded so a case awaiting a task that never completes fails at thirty seconds rather than at the
    // runner's own bound, which UnityRunnerDefaultTimeoutTests pins.
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouteActionTests
    {
        private readonly List<string> _log = new();
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            _root = new VisualElement();
            s_submit = null;
            s_navigation = default;
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private static readonly SubmitOptions PostToItems = new() { Method = "post", Action = "/items" };

        private Func<RouteLoaderContext, CancellationToken, VelvetTask<object>> Loader(string name) => (_, _) =>
        {
            _log.Add(name);
            return VelvetTask.FromResult<object>(name);
        };

        private Router ItemsRouter(string start, Func<RouteActionContext, CancellationToken, VelvetTask<object>> action)
            => BuildRouter(start,
                Route("/", loader: Loader("root"), children: new[]
                {
                    Route("items", loader: Loader("items"), action: action, children: new[]
                    {
                        Route(":id", loader: Loader("item")),
                    }),
                    Route("other"),
                }));

        private VelvetTask<object> Created(RouteActionContext context, CancellationToken _)
        {
            _log.Add("action");
            return VelvetTask.FromResult<object>($"{context.Method} {context.FormData}");
        }

        private static VelvetTask<object> Throws(RouteActionContext _, CancellationToken __)
            => throw new InvalidOperationException("action-failed");

        private static NavigationResult Submit(Router router, object? formData, SubmitOptions options)
            => router.SubmitAsync(formData, options).GetAwaiter().GetResult();

        #region The action and what it commits

        [Test]
        public void Given_ARouteWithAnAction_When_ItIsPosted_Then_ItsResultIsTheActionDataOfThatRoute()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, "lamp", PostToItems);

            // Assert
            Assert.That(router.CurrentActionData["/items"], Is.EqualTo("POST lamp"));
        }

        [Test]
        public void Given_ActionData_When_TheNextNavigationCommits_Then_ItIsCleared()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            Submit(router, "lamp", PostToItems);

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That(router.CurrentActionData, Is.Empty);
        }

        [Test]
        public void Given_AnAction_When_ItIsPostedFromBelowItsRoute_Then_EveryMatchedLoaderRunsAfterIt()
        {
            // Arrange — the root and items layouts would keep their data on a plain navigation to /items.
            var router = ItemsRouter("/items/1", Created);

            // Act
            Submit(router, "lamp", PostToItems);

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("root,items,item,action,root,items"));
        }

        [Test]
        public void Given_AnActionThatThrows_When_ItIsPosted_Then_ItsFailureIsTheRoutesErrorAndNoActionDataCommits()
        {
            // Arrange
            var router = ItemsRouter("/other", Throws);

            // Act
            Submit(router, "lamp", PostToItems);

            // Assert
            Assert.That((router.CurrentLoaderErrors["/items"].Message, router.CurrentActionData.Count),
                Is.EqualTo(("action-failed", 0)));
        }

        [UnityTest]
        public IEnumerator Given_AnActionStillRunning_When_TheCallersTokenIsCancelled_Then_TheSubmissionEndsCancelledAndTheRouterIdle()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = ItemsRouter("/other", (_, ct) =>
            {
                var running = new VelvetTaskCompletionSource<object>();
                ct.Register(() => running.TrySetCanceled());
                return running.Task;
            });
            using var caller = new CancellationTokenSource();
            var submission = router.SubmitAsync("lamp", PostToItems, caller.Token);

            // Act
            caller.Cancel();
            var result = await submission;

            // Assert
            Assert.That((result, router.Status, router.PendingLocation == null),
                Is.EqualTo((NavigationResult.Cancelled, RouterStatus.Idle, true)));
        });

        [Test]
        public void Given_ARouteWithoutAnAction_When_ItIsPosted_Then_ItsErrorSaysSo()
        {
            // Arrange
            var router = BuildRouter("/items", Route("items"));

            // Act
            Submit(router, null, PostToItems);

            // Assert
            Assert.That(router.CurrentLoaderErrors["/items"].Message, Is.EqualTo(
                "You made a POST request to \"/items\" but did not provide an `action` for route \"/items\", so there "
                + "is no way to handle the request."));
        }

        [Test]
        public void Given_AnActionThatThrowsBelowAnErrorElement_When_ItIsPosted_Then_OnlyTheLoadersAboveThatRouteRun()
        {
            // Arrange
            var router = BuildRouter("/other",
                Route("/", loader: Loader("root"), children: new[]
                {
                    Route("items", loader: Loader("items"), errorElement: V.Component(StubB), action: Throws),
                    Route("other"),
                }));
            _log.Clear();

            // Act
            Submit(router, "lamp", PostToItems);

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("root"));
        }

        #endregion

        #region History

        [Test]
        public void Given_AMutationSubmittedToTheCurrentLocation_When_ItCommits_Then_ItReplacesTheEntry()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            router.NavigateSync("/items");

            // Act
            Submit(router, "lamp", new SubmitOptions { Method = "post" });

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1));
        }

        [Test]
        public void Given_AMutationSubmittedToAnotherLocation_When_ItCommits_Then_ItPushesAnEntry()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, "lamp", PostToItems);

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1));
        }

        [Test]
        public void Given_AFailingMutationSubmittedToTheCurrentLocation_When_ItCommits_Then_ItPushesAnEntry()
        {
            // Arrange
            var router = ItemsRouter("/other", Throws);
            router.NavigateSync("/items");

            // Act
            Submit(router, "lamp", new SubmitOptions { Method = "post" });

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(2));
        }

        [Test]
        public void Given_AFailingMutationAskedToReplace_When_ItCommits_Then_ItReplacesTheEntry()
        {
            // Arrange
            var router = ItemsRouter("/other", Throws);
            router.NavigateSync("/items");

            // Act
            Submit(router, "lamp", new SubmitOptions { Method = "post", Replace = true });

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1));
        }

        #endregion

        #region Get submissions and methods

        [Test]
        public void Given_AGetSubmission_When_ItCommits_Then_TheFormDataIsTheQueryAndNoActionRan()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            var query = new SearchParams();
            query.Append("q", "lamp");

            // Act
            Submit(router, query, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That((router.CurrentLocation!.Path, _log.Contains("action")), Is.EqualTo(("/items?q=lamp", false)));
        }

        [Test]
        public void Given_AGetSubmissionOfSomethingOtherThanSearchParams_When_Submitted_Then_ItThrows()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            TestDelegate submit = () => router.SubmitAsync("lamp", new SubmitOptions { Action = "/items" });

            // Assert
            Assert.Throws<ArgumentException>(submit);
        }

        [Test]
        public void Given_AMethodNoFormTakes_When_Submitted_Then_ItThrows()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            TestDelegate submit = () => router.SubmitAsync("lamp", new SubmitOptions { Method = "head" });

            // Assert
            Assert.Throws<ArgumentException>(submit);
        }

        #endregion

        #region Which action a submission goes to

        private Router IndexRouter(string start) => BuildRouter(start,
            Route("/", action: (_, _) => VelvetTask.FromResult<object>("layout"), children: new[]
            {
                Route("", action: (_, _) => VelvetTask.FromResult<object>("index")),
            }));

        [Test]
        public void Given_AnIndexRouteWithAnAction_When_ItSubmitsWithoutNamingOne_Then_ItsOwnActionRuns()
        {
            // Arrange
            var router = IndexRouter("/");

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentActionData["/?index"], Is.EqualTo("index"));
        }

        [Test]
        public void Given_ALayoutAboveAnIndexRoute_When_ItSubmitsWithoutNamingAnAction_Then_TheLayoutsActionRuns()
        {
            // Arrange
            var router = IndexRouter("/");

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 0, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentActionData["/"], Is.EqualTo("layout"));
        }

        [Test]
        public void Given_ARouterWithNoLocationYet_When_ItIsPostedWithoutNamingAnAction_Then_TheRootsActionRuns()
        {
            // Arrange
            var router = new Router(new[] { Route("/", action: (_, _) => VelvetTask.FromResult<object>("root")) });

            // Act
            Submit(router, null, new SubmitOptions { Method = "post" });

            // Assert
            Assert.That(router.CurrentActionData["/"], Is.EqualTo("root"));
        }

        [Test]
        public void Given_ASearchHoldingABareIndex_When_AnIndexRouteSubmitsWithoutNamingAnAction_Then_OnlyTheBareIndexIsReplaced()
        {
            // Arrange
            var router = IndexRouter("/?index&index=5&flag");

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/?index&index=5&flag="));
        }

        #endregion

        #region V.Route

        [Test]
        public void Given_AnActionPassedToVRoute_When_ItBuildsTheRoute_Then_TheRouteCarriesItAndStaysCaseInsensitive()
        {
            // Arrange
            Func<RouteActionContext, CancellationToken, VelvetTask<object>> action = Created;

            // Act
            var route = V.Route("items", element: V.Component(StubA), action: action);

            // Assert
            Assert.That((ReferenceEquals(route.Action, action), route.CaseSensitive), Is.EqualTo((true, false)));
        }

        #endregion

        #region Hooks

        private static SubmitFunction? s_submit;
        private static NavigationState s_navigation;

        [Component]
        private static VNode SubmittingItems()
        {
            s_submit = Hooks.UseSubmit();
            s_navigation = Hooks.UseNavigation();
            return V.Label(text: "result-" + Hooks.UseActionData<string>());
        }

        private MountedTree MountItems(Func<RouteActionContext, CancellationToken, VelvetTask<object>> action)
        {
            var router = BuildRouter("/items", Route("items", element: V.Component(SubmittingItems), action: action));
            var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        [Test]
        public void Given_AComponentThatSubmitsWithoutNamingAnAction_When_ItsRoutesActionReturns_Then_UseActionDataRendersTheResult()
        {
            // Arrange
            using var mounted = MountItems(Created);

            // Act
            s_submit!("lamp", new SubmitOptions { Method = "post" }).GetAwaiter().GetResult();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("result-POST lamp"), Is.Not.Null);
        }

        [Test]
        public void Given_AnActionStillRunning_When_TheComponentRenders_Then_UseNavigationReportsTheSubmission()
        {
            // Arrange
            var pending = new VelvetTaskCompletionSource<object>();
            using var mounted = MountItems((_, _) => pending.Task);

            // Act
            s_submit!("lamp", new SubmitOptions { Method = "put" }).Forget();
            mounted.FlushStateForTest();

            // Assert
            Assert.That($"{s_navigation.State} {s_navigation.FormMethod} {s_navigation.FormAction} {s_navigation.FormData}",
                Is.EqualTo("Submitting PUT /items lamp"));
        }

        #endregion
    }
}
