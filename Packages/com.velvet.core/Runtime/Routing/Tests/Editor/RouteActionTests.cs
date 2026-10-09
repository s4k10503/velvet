// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
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

        private static RouteDefinition ActionRoute(string path,
            Func<RouteActionContext, CancellationToken, VelvetTask<object>> action,
            Func<RouteLoaderContext, CancellationToken, VelvetTask<object>>? loader = null,
            RouteDefinition[]? children = null,
            ComponentNode? element = null,
            ComponentNode? errorElement = null)
            => V.Route(path, element ?? V.Component(StubA), loader: loader, errorElement: errorElement,
                children: children, action: action);

        private Func<RouteLoaderContext, CancellationToken, VelvetTask<object>> Loader(string name) => (_, _) =>
        {
            _log.Add(name);
            return VelvetTask.FromResult<object>(name);
        };

        private Router ItemsRouter(string start, Func<RouteActionContext, CancellationToken, VelvetTask<object>> action)
            => BuildRouter(start,
                Route("/", loader: Loader("root"), children: new[]
                {
                    ActionRoute("items", action, Loader("items"), children: new[]
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
            Assert.That((result, router.Navigation.State, router.Navigation.Location == null),
                Is.EqualTo((NavigationResult.Cancelled, NavigationLifecycle.Idle, true)));
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
                    ActionRoute("items", Throws, Loader("items"), errorElement: V.Component(StubB)),
                    Route("other"),
                }));
            _log.Clear();

            // Act
            Submit(router, "lamp", PostToItems);

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("root"));
        }

        [Test]
        public void Given_AnActionThatThrowsAtARouteRenderingItsError_When_ItIsPosted_Then_ThatRouteKeepsItsLoaderData()
        {
            // Arrange
            var router = BuildRouter("/items",
                Route("/", loader: Loader("root"), children: new[]
                {
                    ActionRoute("items", Throws, Loader("items"), errorElement: V.Component(StubB)),
                }));
            _log.Clear();

            // Act
            Submit(router, "lamp", PostToItems);

            // Assert
            Assert.That((string.Join(",", _log), router.GetLoaderData("/items")), Is.EqualTo(("root", (object)"items")));
        }

        [Test]
        public void Given_AFailingActionWhoseLoadersStillRun_When_TheRouterIsRead_Then_ThePreviousActionDataStands()
        {
            // Arrange — the first submission succeeds; the second fails while the root's loader holds.
            var submissions = 0;
            var loads = 0;
            var router = BuildRouter("/items",
                Route("/", loader: (_, _) => ++loads < 3
                    ? VelvetTask.FromResult<object>("root")
                    : new VelvetTaskCompletionSource<object>().Task, children: new[]
                {
                    ActionRoute("items", (_, _) => ++submissions == 1
                        ? VelvetTask.FromResult<object>("saved")
                        : throw new InvalidOperationException("rejected"), errorElement: V.Component(StubB)),
                }));
            Submit(router, "first", PostToItems);

            // Act
            router.SubmitAsync("second", PostToItems).Forget();

            // Assert
            Assert.That((router.Navigation.State, router.CurrentActionData["/items"]),
                Is.EqualTo((NavigationLifecycle.Loading, (object)"saved")));
        }

        [Test]
        public void Given_ASubmissionStillRunning_When_ANavigationTakesOver_Then_ItRunsEveryLoader()
        {
            // Arrange
            var router = BuildRouter("/projects/1",
                Route("/", loader: Loader("root"), children: new[]
                {
                    Route("projects", loader: Loader("projects"), children: new[]
                    {
                        ActionRoute(":id", (_, _) => new VelvetTaskCompletionSource<object>().Task, Loader("project")),
                    }),
                }));
            router.SubmitAsync("rename", new SubmitOptions { Method = "post" }).Forget();
            _log.Clear();

            // Act
            router.NavigateSync("/projects/2");

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("root,projects,project"));
        }

        [Test]
        public void Given_ASubmissionAGuardRedirects_When_TheTargetLoads_Then_TheSubmissionIsStillReported()
        {
            // Arrange
            var router = BuildRouter("/start",
                Route("start"),
                V.Route("items", V.Component(StubA), guard: _ => "/other", action: Created),
                Route("other", loader: (_, _) => new VelvetTaskCompletionSource<object>().Task));

            // Act
            router.SubmitAsync("lamp", PostToItems).Forget();

            // Assert
            Assert.That($"{router.Navigation.Location?.Path} {router.Navigation.FormMethod} {_log.Contains("action")}",
                Is.EqualTo("/other post False"));
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
        public void Given_AGetSubmissionOfAString_When_Submitted_Then_ItIsParsedAsAQueryStringTheWayUrlSearchParamsDoes()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, "?a=b+c&d=%26&e", new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?a=b%20c&d=%26&e="));
        }

        [Test]
        public void Given_AGetSubmissionOfNameValuePairs_When_Submitted_Then_TheyAreTheQuery()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            var pairs = new Dictionary<string, string> { ["q"] = "lamp" };

            // Act
            Submit(router, pairs, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?q=lamp"));
        }

        [Test]
        public void Given_AGetSubmissionOfAStringRepeatingAKey_When_Submitted_Then_TheQueryKeepsTheBodysOrder()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, "a=1&b=2&a=3", new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?a=1&b=2&a=3"));
        }

        [Test]
        public void Given_AGetSubmissionOfAnObject_When_Submitted_Then_ItsPublicPropertiesAreTheQueryStringifiedAsJavaScriptDoes()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new { q = "lamp", page = 2, all = true, none = (string?)null },
                new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?q=lamp&page=2&all=true&none=null"));
        }

        [Test]
        public void Given_AGetSubmissionOfAKeyValuePairSequence_When_Submitted_Then_ThePairsAreTheQueryInOrder()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new List<KeyValuePair<string, string>> { new("b", "1"), new("a", "2"), new("b", "3") }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?b=1&a=2&b=3"));
        }

        [Test]
        public void Given_AGetSubmissionOfAListOfTwoElementLists_When_Submitted_Then_ThePairsAreTheQuery()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new[] { new[] { "q", "lamp" } }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?q=lamp"));
        }

        [Test]
        public void Given_AGetSubmissionOfATuple_When_Submitted_Then_ItIsAPairOfTheQuery()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new[] { ("q", "lamp") }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?q=lamp"));
        }

        [Test]
        public void Given_AGetSubmissionOfAPrimitive_When_Submitted_Then_ItParsesAsAQueryString()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, 5, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?5="));
        }

        // GREEN_ON_BASE(characterization): a null get body already gave an empty query on the base; this pins
        // that the new encoding keeps null on that path rather than reporting it as unencodable.
        [Test]
        public void Given_AGetSubmissionOfNull_When_Submitted_Then_TheQueryIsEmpty()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, null, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That((router.CurrentLocation!.Path, router.CurrentLoaderErrors.Count), Is.EqualTo(("/items", 0)));
        }

        [Test]
        public void Given_AGetSubmissionOfAnObjectWithAListMember_When_Submitted_Then_TheListIsItsElementsJoinedByCommas()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new { tags = new[] { "a", "b" } }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?tags=a%2Cb"));
        }

        // GREEN_ON_BASE(characterization): an ISearchParams body was the one get body the base accepted; this pins
        // that the new encoding leaves its grouping of repeated keys as the base wrote it.
        [Test]
        public void Given_AGetSubmissionOfASearchParams_When_Submitted_Then_ItsRepeatedKeysAreGrouped()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            var query = new SearchParams();
            query.Append("a", "1");
            query.Append("b", "2");
            query.Append("a", "3");

            // Act
            Submit(router, query, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?a=1&a=3&b=2"));
        }

        [Test]
        public void Given_AGetSubmissionOfADoubleUnderACommaCulture_When_Submitted_Then_TheNumberHasNoCulture()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            // Act
            try
            {
                Submit(router, new { price = 1.5 }, new SubmitOptions { Action = "/items" });
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?price=1.5"));
        }

        [TestCase(-0.0, "0")]
        [TestCase(1e21, "1e%2B21")]
        [TestCase(1e-7, "1e-7")]
        [TestCase(123456789012345680000.0, "123456789012345680000")]
        [TestCase(25.0, "25")]
        [TestCase(0.5, "0.5")]
        [TestCase(double.NaN, "NaN")]
        [TestCase(double.PositiveInfinity, "Infinity")]
        [TestCase(double.NegativeInfinity, "-Infinity")]
        public void Given_AGetSubmissionOfADoubleMember_When_Submitted_Then_ItIsWrittenAsJavaScriptWritesIt(double value, string written)
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new { n = value }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?n=" + written));
        }

        [Test]
        public void Given_AGetSubmissionOfAStringPairWithAnEmptyName_When_Submitted_Then_ThePairKeepsTheEmptyName()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, "=x", new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?=x"));
        }

        [Test]
        public void Given_AGetSubmissionOfAnEnum_When_Submitted_Then_ItsNameParsesAsAQueryString()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, Shade.Dark, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?Dark="));
        }

        [Test]
        public void Given_AGetSubmissionOfADecimal_When_Submitted_Then_ItParsesAsAQueryString()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, 1.5m, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?1.5="));
        }

        [Test]
        public void Given_AGetSubmissionOfABigInteger_When_Submitted_Then_ItParsesAsAQueryString()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
                new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?123456789012345678901234567890="));
        }

        [Test]
        public void Given_AGetSubmissionOfAnObjectWithAWriteOnlyProperty_When_Submitted_Then_OnlyTheReadableOneIsTheQuery()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new WriteOnlyBody(), new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?Q=lamp"));
        }

        [Test]
        public void Given_AGetSubmissionOfAnObjectWithAPublicField_When_Submitted_Then_TheFieldIsTheQuery()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new FieldBody(), new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?Q=lamp"));
        }

        private enum Shade
        {
            Dark,
        }

        private sealed class WriteOnlyBody
        {
            public string Q => "lamp";

            public string Sink
            {
                set { }
            }
        }

        private sealed class FieldBody
        {
            public string Q = "lamp";
        }

        [Test]
        public void Given_AGetSubmissionWhoseMemberThrows_When_Submitted_Then_ItCommitsTheEncodingError()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new ThrowingBody(), new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(router.CurrentLoaderErrors["/items"].Message, Is.EqualTo("Unable to encode submission body"));
        }

        // GREEN_ON_BASE(characterization): the base already left a post's one-shot sequence unenumerated; this
        // pins that the encoding added for get bodies does not reach a post's.
        [Test]
        public void Given_APostOfAOneShotSequence_When_Submitted_Then_ItIsNotEnumerated()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            var reads = 0;
            IEnumerable<int> OneShot()
            {
                reads++;
                yield return 1;
            }

            // Act
            Submit(router, OneShot(), PostToItems);

            // Assert
            Assert.That(reads, Is.EqualTo(0));
        }

        private sealed class ThrowingBody
        {
            public string Name => throw new InvalidOperationException();
        }

        [Test]
        public void Given_AGetSubmissionOfASequenceOfNonPairs_When_Submitted_Then_ItCommitsTheEncodingErrorAtTheLeafAndRunsNoAction()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, new[] { 1, 2 }, new SubmitOptions { Action = "/items" });

            // Assert
            Assert.That(
                (router.CurrentLocation!.Path, router.CurrentLoaderErrors["/items"].Message, _log.Contains("action")),
                Is.EqualTo(("/items", "Unable to encode submission body", false)));
        }

        [Test]
        public void Given_ARefusedGetSubmissionWithNoAction_When_Submitted_Then_ItLandsOnTheCurrentLocationWithItsSearch()
        {
            // Arrange
            var router = ItemsRouter("/items?q=1", Created);

            // Act
            Submit(router, new[] { 1, 2 }, new SubmitOptions());

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?q=1"));
        }

        [Test]
        public void Given_AMethodNoFormTakes_When_Submitted_Then_ItCommitsTheRefusalAndRunsNoAction()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);

            // Act
            Submit(router, "lamp", new SubmitOptions { Method = "head", Action = "/items" });

            // Assert
            Assert.That((router.CurrentLoaderErrors["/items"].Message, _log.Contains("action")),
                Is.EqualTo(("Invalid request method \"HEAD\"", false)));
        }

        [Test]
        public void Given_AMethodNoFormTakes_When_Submitted_Then_ItIsReportedAsALoadAndNotASubmission()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            var states = new List<NavigationLifecycle>();
            router.OnNavigationChanged += navigation => states.Add(navigation.State);

            // Act
            Submit(router, "lamp", new SubmitOptions { Method = "head", Action = "/items" });

            // Assert
            Assert.That(string.Join(",", states), Is.EqualTo("Loading,Idle"));
        }

        [Test]
        public void Given_AGetSubmissionWhoseLoaderIsStillRunning_When_TheRouterIsRead_Then_ItReportsTheSubmission()
        {
            // Arrange
            var router = BuildRouter("/start", Route("start"),
                Route("search", loader: (_, _) => new VelvetTaskCompletionSource<object>().Task));
            var query = new SearchParams();
            query.Append("q", "lamp");

            // Act
            router.SubmitAsync(query, new SubmitOptions { Action = "/search" }).Forget();

            // Assert
            Assert.That(router.Navigation.FormMethod, Is.EqualTo("get"));
        }

        [Test]
        public void Given_AnEmptyMethod_When_Submitted_Then_ItIsAGetSubmission()
        {
            // Arrange
            var router = ItemsRouter("/other", Created);
            var query = new SearchParams();
            query.Append("q", "lamp");

            // Act
            Submit(router, query, new SubmitOptions { Method = "", Action = "/items" });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/items?q=lamp"));
        }

        #endregion

        #region Which action a submission goes to

        private Router IndexRouter(string start) => BuildRouter(start,
            ActionRoute("/", Logged("layout"), children: new[] { ActionRoute("", Logged("index")) }));

        private Func<RouteActionContext, CancellationToken, VelvetTask<object>> Logged(string name) => (_, _) =>
        {
            _log.Add(name);
            return VelvetTask.FromResult<object>(name);
        };

        [Test]
        public void Given_AnIndexRouteWithAnAction_When_ItSubmitsWithoutNamingOne_Then_ItsOwnActionRuns()
        {
            // Arrange
            var router = IndexRouter("/");

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("index"));
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
            Assert.That(string.Join(",", _log), Is.EqualTo("layout"));
        }

        [Test]
        public void Given_ARouterWithNoLocationYet_When_ItIsPostedWithoutNamingAnAction_Then_TheRootsActionRuns()
        {
            // Arrange
            var router = new Router(new[] { ActionRoute("/", (_, _) => VelvetTask.FromResult<object>("root")) });

            // Act
            Submit(router, null, new SubmitOptions { Method = "post" });

            // Assert
            Assert.That(router.CurrentActionData["/"], Is.EqualTo("root"));
        }

        [Test]
        public void Given_ASearchHoldingABareIndex_When_AnIndexRouteSubmitsWithoutNamingAnAction_Then_TheSearchIsKept()
        {
            // Arrange
            var router = IndexRouter("/?index&index=5&flag");

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/?index&index=5&flag"));
        }

        [Test]
        public void Given_ASearchHoldingABareIndex_When_ALayoutSubmitsWithoutNamingAnAction_Then_OnlyTheBareIndexIsTakenOut()
        {
            // Arrange
            var router = IndexRouter("/?index&index=5&flag");

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 0, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That((router.CurrentLocation!.Path, string.Join(",", _log)), Is.EqualTo(("/?index=5&flag=", "layout")));
        }

        [Test]
        public void Given_ASearchWithNoBareIndex_When_ALayoutSubmitsWithoutNamingAnAction_Then_TheSearchIsKeptAsWritten()
        {
            // Arrange
            var router = IndexRouter("/?flag");

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 0, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/?flag"));
        }

        [Test]
        public void Given_AnIndexRouteDeclaringAnEmptyChildList_When_ItSubmitsWithoutNamingAnAction_Then_ItsOwnActionRuns()
        {
            // Arrange — an empty list declares no child, so the route is still an index route.
            var router = BuildRouter("/", ActionRoute("/", Logged("layout"), children: new[]
            {
                ActionRoute("", Logged("index"), children: new RouteDefinition[0]),
            }));

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("index"));
        }

        [TestCase(".")]
        [TestCase("")]
        public void Given_AnIndexRouteWithAnAction_When_ItSubmitsToItself_Then_ItsOwnActionRuns(string action)
        {
            // Arrange
            var router = IndexRouter("/");

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post", Action = action }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("index"));
        }

        [Test]
        public void Given_APathlessLayoutUnderARootAction_When_ItSubmitsWithoutNamingAnAction_Then_TheRootsActionRuns()
        {
            // Arrange
            var router = BuildRouter("/settings",
                ActionRoute("/", Logged("root"), children: new[]
                {
                    V.Route("", V.Component(StubA), children: new[] { V.Route("settings", V.Component(StubB)) }),
                }));

            // Act
            router.SubmitAsync(null, new SubmitOptions { Method = "post" }, baseRouteIndex: 1, default)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("root"));
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
            var router = BuildRouter("/items", ActionRoute("items", action, element: V.Component(SubmittingItems)));
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
                Is.EqualTo("Submitting put /items lamp"));
        }

        [Test]
        public void Given_AnActionStillRunning_When_ASecondSubmissionTakesOver_Then_UseNavigationReportsTheSecond()
        {
            // Arrange
            using var mounted = MountItems((_, _) => new VelvetTaskCompletionSource<object>().Task);
            s_submit!("first", new SubmitOptions { Method = "post" }).Forget();
            mounted.FlushStateForTest();

            // Act
            s_submit!("second", new SubmitOptions { Method = "post" }).Forget();
            mounted.FlushStateForTest();

            // Assert
            Assert.That($"{s_navigation.State} {s_navigation.FormData}", Is.EqualTo("Submitting second"));
        }

        [Test]
        public void Given_AnActionThatReturned_When_TheLoadersAfterItStillRun_Then_UseActionDataRendersTheResult()
        {
            // Arrange — the route's loader answers at once the first time and holds the second.
            var loads = 0;
            var router = BuildRouter("/items", ActionRoute("items", Created,
                (_, _) => ++loads == 1 ? VelvetTask.FromResult<object>("items") : new VelvetTaskCompletionSource<object>().Task,
                element: V.Component(SubmittingItems)));
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Act
            s_submit!("lamp", new SubmitOptions { Method = "post" }).Forget();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_navigation.State, _root.FindLabelByText("result-POST lamp") != null),
                Is.EqualTo((NavigationLifecycle.Loading, true)));
        }

        #endregion
    }
}
